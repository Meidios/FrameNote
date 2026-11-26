using System.IO;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace FrameNote;

public sealed partial class Desk
{
    CaptureAudioMix? voiceCapture;
    TaskCompletionSource<long>? voiceStopTime;
    void StopVoice()=>voiceStopTime?.TrySetResult(TimestampedAudio.Now100ns());
    Task ImportNoteAudio(JsonElement message,string[] files)=>ImportNoteAudio(message.GetProperty("documentId").GetGuid(),
        message.GetProperty("at").GetInt64(),message.TryGetProperty("targetId",out var target)?target.GetString():null,files);
    async Task ImportNoteAudio(Guid documentId,long at,string? targetId,string[] files){
        if(doc?.Id!=documentId||viewingImage||pending||voiceCapture!=null||files.Length==0||at<0||at>doc.DurationUs)return;
        await Work(async()=>{
            if(files.Any(p=>!ReviewVoice.Supports(p)))throw new InvalidDataException(L.Text("请选择支持的音频文件。"));
            var voices=new List<Voice>();
            string proxy=Path.Combine(scratch,Guid.NewGuid()+".mp3");
            try{
                foreach(string path in files){
                    string original=await Task.Run(()=>ReviewVoice.StoreFile(ImageDirectory,path,"original"));
                    string input=Path.Combine(ImageDirectory,original+".original");
                    await Probe.Run(lifetime.Token,"ffmpeg","-v","error","-nostdin","-y","-i",input,"-map","0:a:0","-vn","-ac","2","-ar","48000","-c:a","libmp3lame","-q:a","2",proxy);
                    long duration=await Probe.AudioDuration(input,lifetime.Token);
                    string asset=await Task.Run(()=>ReviewVoice.StoreFile(ImageDirectory,proxy,"mp3"));
                    voices.Add(new(asset,duration,"mp3",new(original,Path.GetFileName(path))));
                }
                if(doc?.Id==documentId)Send(new{type="audioImported",documentId,at,targetId,voices});
            }finally{File.Delete(proxy);}
        });
    }
    // Match the Windows playback mute for the duration of microphone capture.
    sealed class VoiceOutputMute : IDisposable
    {
        readonly List<MMDevice> changed=[];
        public VoiceOutputMute(){
            using var devices=new MMDeviceEnumerator();
            try{
                foreach(var role in new[]{Role.Multimedia,Role.Console,Role.Communications}){
                    if(!devices.HasDefaultAudioEndpoint(DataFlow.Render,role))continue;
                    var device=devices.GetDefaultAudioEndpoint(DataFlow.Render,role);
                    try{
                        if(!device.AudioEndpointVolume.Mute){
                            device.AudioEndpointVolume.Mute=true;
                            changed.Add(device);
                        }else device.Dispose();
                    }catch{device.Dispose();throw;}
                }
            }catch{Dispose();throw;}
        }
        public void Dispose(){
            foreach(var device in changed){
                try{device.AudioEndpointVolume.Mute=false;}
                catch(Exception e){Serilog.Log.Warning(e,"Could not restore playback endpoint mute");}
                finally{device.Dispose();}
            }
            changed.Clear();
        }
    }
    // One peak per 20 ms of the same WASAPI clock used to encode the MP3.
    sealed class VoiceEnvelope(float gain){
        internal const int BinUs=20_000;
        readonly List<float> peaks=[];
        long origin;
        int sent;
        internal void Start(long time)=>Volatile.Write(ref origin,time);
        internal void Sample(long timestamp,float amplitude){
            long start=Volatile.Read(ref origin);
            if(start==0||timestamp<start)return;
            int bin=(int)((timestamp-start)/(10L*BinUs));
            if(bin>=600_000_000/BinUs)return;
            lock(peaks){
                while(peaks.Count<=bin)peaks.Add(0);
                peaks[bin]=Math.Max(peaks[bin],Math.Min(1,amplitude*gain));
            }
        }
        internal (int Offset,float[] Peaks) Read(){
            lock(peaks){
                int offset=Math.Max(0,sent-1);
                sent=peaks.Count;
                return(offset,peaks.GetRange(offset,sent-offset).ToArray());
            }
        }
    }
    async Task RecordVoice(JsonElement p){
        Guid id=p.GetProperty("id").GetGuid();
        if(voiceCapture!=null || recorder!=null || pending || configuring || doc==null || viewingImage){Send(new{type="voiceStored",id,error=L.Text("当前有录制或文件操作，请结束后再录语音。")});return;}
        long startUs=p.GetProperty("startUs").GetInt64();
        if(p.GetProperty("playbackId").GetGuid()!=playbackId || startUs<0 || startUs>=doc.DurationUs || doc.Notes.Count>=10000){
            Send(new{type="voiceStored",id,error=L.Text("当前播放位置无法添加语音批注。")});return;
        }
        configuring=true;
        string directory=Path.Combine(scratch,"voice-"+Guid.NewGuid().ToString("N")),encoded=Path.Combine(directory,"voice.mp3");
        try{
            var mixer=p.GetProperty("mixer").Deserialize<MixerSettings>(Review.Json)!;
            var sources=ResolveAudio(new MixerSettings([],mixer.Mic));
            if(!sources.Any(s=>!s.Muted))throw new InvalidOperationException(L.Text("请启用麦克风后再录语音批注。"));
            Directory.CreateDirectory(directory);
            var envelope=new VoiceEnvelope(MathF.Pow(10,sources.Single().GainDb/20f));
            using var mix=new CaptureAudioMix(sources,directory,observePeak:envelope.Sample);
            var stopped=voiceStopTime=new(TaskCreationOptions.RunContinuationsAsynchronously);
            voiceCapture=mix;
            long start=TimestampedAudio.Now100ns(),maxUs=Math.Min(600_000_000,doc.DurationUs-startUs);
            envelope.Start(start);
            var preview=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            preview.Tick+=(_,_)=>{
                var (offset,peaks)=envelope.Read();
                long elapsedUs=Math.Clamp((TimestampedAudio.Now100ns()-start)/10,0,maxUs);
                Send(new{type="voiceProgress",id,elapsedUs,offset,peaks});
            };
            long duration;
            try{
                using var silence=new VoiceOutputMute();
                Send(new{type="voiceStarted",id,binUs=VoiceEnvelope.BinUs});
                preview.Start();
                using var limit=new CancellationTokenSource(TimeSpan.FromTicks(maxUs*10));
                using var stop=limit.Token.Register(()=>stopped.TrySetResult(start+maxUs*10));
                // A disconnected endpoint or failed writer must also end the recording.
                var finished=await Task.WhenAny(mix.Workers.Append(stopped.Task));
                if(finished!=stopped.Task){
                    await finished;
                    throw new IOException(L.Text("音频设备意外停止采集。"));
                }
                duration=Math.Min(maxUs,((await stopped.Task)-start)/10);
            }finally{preview.Stop();voiceStopTime=null;voiceCapture=null;mix.Dispose();}
            Send(new{type="voiceFinishing",id});
            if(duration<100_000)throw new InvalidOperationException(L.Text("录音太短，请至少录制 0.1 秒。"));
            var args=new List<string>{"-nostdin","-v","error","-copyts"};
            CaptureAudioMix.AppendMix(args,0,(start-mix.Epoch)/1e7,mix.Tracks);
            args.AddRange(["-map","[mix]","-t",FormattableString.Invariant($"{duration/1e6:F6}"),"-ac","1","-ar","44100","-c:a","libmp3lame","-b:a","64k",encoded]);
            await Probe.Run(lifetime.Token,"ffmpeg",args.ToArray());
            string hash=await Task.Run(()=>ReviewVoice.StoreFile(ImageDirectory,encoded,"mp3"));
            Send(new{type="voiceStored",id,voice=new Voice(hash,duration,"mp3"),endUs=Math.Min(doc.DurationUs,startUs+duration)});
        }catch(Exception e){Serilog.Log.Error(e,"Voice recording failed");Send(new{type="voiceStored",id,error=L.Text("语音录制失败：")+e.Message});}
        finally{configuring=false;if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}
