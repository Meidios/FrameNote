using System.IO;
using System.Text.Json;

namespace FrameNote;

public sealed partial class Desk
{
    CaptureAudioMix? voiceCapture;
    TaskCompletionSource<long>? voiceStopTime;
    void StopVoice()=>voiceStopTime?.TrySetResult(TimestampedAudio.Now100ns());
    async Task RecordVoice(JsonElement p){
        Guid id=p.GetProperty("id").GetGuid();
        if(voiceCapture!=null || recorder!=null || pending || configuring || doc==null || viewingImage){Send(new{type="voiceStored",id,error="当前有录制或文件操作，请结束后再录语音。"});return;}
        long startUs=p.GetProperty("startUs").GetInt64();
        if(p.GetProperty("playbackId").GetGuid()!=playbackId || startUs<0 || startUs>=doc.DurationUs || doc.Notes.Count>=10000){
            Send(new{type="voiceStored",id,error="当前播放位置无法添加语音批注。"});return;
        }
        configuring=true;
        string directory=Path.Combine(scratch,"voice-"+Guid.NewGuid().ToString("N")),encoded=Path.Combine(directory,"voice.m4a");
        try{
            var sources=ResolveAudio(p.GetProperty("mixer").Deserialize<MixerSettings>(Review.Json)!);
            if(!sources.Any(s=>!s.Muted))throw new InvalidOperationException("请在左侧音频混音器中启用至少一路声音。");
            Directory.CreateDirectory(directory);
            using var mix=new CaptureAudioMix(sources,directory);
            var stopped=voiceStopTime=new(TaskCreationOptions.RunContinuationsAsynchronously);
            voiceCapture=mix;
            long start=TimestampedAudio.Now100ns(),maxUs=Math.Min(600_000_000,doc.DurationUs-startUs);
            long duration;
            try{
                Send(new{type="voiceStarted",id});
                using var limit=new CancellationTokenSource(TimeSpan.FromTicks(maxUs*10));
                using var stop=limit.Token.Register(()=>stopped.TrySetResult(start+maxUs*10));
                // A disconnected endpoint or failed writer must also end the recording.
                var finished=await Task.WhenAny(mix.Workers.Append(stopped.Task));
                if(finished!=stopped.Task){
                    await finished;
                    throw new IOException("音频设备意外停止采集。");
                }
                duration=Math.Min(maxUs,((await stopped.Task)-start)/10);
            }finally{voiceStopTime=null;voiceCapture=null;mix.Dispose();}
            Send(new{type="voiceFinishing",id});
            if(duration<100_000)throw new InvalidOperationException("录音太短，请至少录制 0.1 秒。");
            var args=new List<string>{"-nostdin","-v","error","-copyts"};
            CaptureAudioMix.AppendMix(args,0,(start-mix.Epoch)/1e7,mix.Tracks);
            args.AddRange(["-map","[mix]","-t",FormattableString.Invariant($"{duration/1e6:F6}"),"-ac","1","-ar","48000","-c:a","aac","-b:a","128k","-movflags","+faststart",encoded]);
            await Probe.Run(lifetime.Token,"ffmpeg",args.ToArray());
            string hash=ReviewVoice.Store(ImageDirectory,await File.ReadAllBytesAsync(encoded));
            Send(new{type="voiceStored",id,voice=new Voice(hash,duration),endUs=Math.Min(doc.DurationUs,startUs+duration)});
        }catch(Exception e){Send(new{type="voiceStored",id,error="语音录制失败："+e.Message});}
        finally{configuring=false;if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}
