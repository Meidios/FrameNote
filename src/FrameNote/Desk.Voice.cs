using System.IO;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace FrameNote;

public sealed partial class Desk
{
    CaptureAudioMix? voiceCapture;
    TaskCompletionSource<long>? voiceStopTime;
    void StopVoice()=>voiceStopTime?.TrySetResult(TimestampedAudio.Now100ns());
    async Task ImportNoteAudio(JsonElement message,string[] files){
        if(doc==null||viewingImage||pending||voiceCapture!=null||files.Length==0||
            message.GetProperty("documentId").GetGuid()!=doc.Id)return;
        var documentId=doc.Id;
        long at=message.GetProperty("at").GetInt64();
        if(at<0||at>doc.DurationUs)return;
        string? targetId=message.TryGetProperty("targetId",out var target)?target.GetString():null;
        await Work(async()=>{
            if(files.Any(p=>!ReviewVoice.Supports(p)))throw new InvalidDataException(L.Text("请选择支持的音频文件。"));
            var voices=new List<Voice>();
            string proxy=Path.Combine(scratch,Guid.NewGuid()+".mp3");
            try{
                foreach(string path in files){
                    string original=await Task.Run(()=>ReviewVoice.StoreFile(ImageDirectory,path,"original"));
                    string input=Path.Combine(ImageDirectory,original+".original");
                    await Probe.Run(lifetime.Token,"ffmpeg","-v","error","-nostdin","-y","-i",input,"-map","0:a:0","-vn","-ac","2","-ar","48000","-c:a","libmp3lame","-q:a","2",proxy);
                    Task<string> DurationOf(string file)=>Probe.Run(lifetime.Token,"ffprobe","-v","error","-show_entries","format=duration","-of","default=nw=1:nk=1",file);
                    string duration=await DurationOf(input);
                    if(string.IsNullOrWhiteSpace(duration)||duration.Trim()=="N/A")duration=await DurationOf(proxy);
                    if(!double.TryParse(duration.Trim(),System.Globalization.CultureInfo.InvariantCulture,out double seconds)||!double.IsFinite(seconds)||seconds<=0||seconds>604800)
                        throw new InvalidDataException(L.Text("音频时长无效。"));
                    string asset=await Task.Run(()=>ReviewVoice.StoreFile(ImageDirectory,proxy,"mp3"));
                    voices.Add(new(asset,(long)Math.Round(seconds*1e6),"mp3",new(original,Path.GetFileName(path))));
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
            using var mix=new CaptureAudioMix(sources,directory);
            var stopped=voiceStopTime=new(TaskCreationOptions.RunContinuationsAsynchronously);
            voiceCapture=mix;
            long start=TimestampedAudio.Now100ns(),maxUs=Math.Min(600_000_000,doc.DurationUs-startUs);
            long duration;
            try{
                using var silence=new VoiceOutputMute();
                Send(new{type="voiceStarted",id});
                using var limit=new CancellationTokenSource(TimeSpan.FromTicks(maxUs*10));
                using var stop=limit.Token.Register(()=>stopped.TrySetResult(start+maxUs*10));
                // A disconnected endpoint or failed writer must also end the recording.
                var finished=await Task.WhenAny(mix.Workers.Append(stopped.Task));
                if(finished!=stopped.Task){
                    await finished;
                    throw new IOException(L.Text("音频设备意外停止采集。"));
                }
                duration=Math.Min(maxUs,((await stopped.Task)-start)/10);
            }finally{voiceStopTime=null;voiceCapture=null;mix.Dispose();}
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
