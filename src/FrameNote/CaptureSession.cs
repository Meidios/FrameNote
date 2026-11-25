using System.Globalization;
using System.IO;
using System.Text.Json;
using ScreenRecorderLib;

namespace FrameNote;

public sealed record CaptureInput(string Id,bool Loopback,int GainDb=0,bool Muted=false);

// Alternating pause/resume times in the same 100 ns QPC units as WASAPI.
// A packet that arrives late still uses the transitions at its acquisition time.
internal sealed class CapturePauses
{
    long[] changes=[];
    internal long[] Changes=>Volatile.Read(ref changes);
    internal void Set(bool paused,long now){
        var previous=Changes;
        if((previous.Length%2==1)==paused)return;
        long[] next=[..previous,now];
        Volatile.Write(ref changes,next);
    }
    internal long Elapsed(long start,long now){
        var cuts=Changes;
        for(int i=0;i<cuts.Length;i+=2)start+=(i+1<cuts.Length?cuts[i+1]:now)-cuts[i];
        return Math.Max(0,now-start);
    }
}

// All endpoint timestamps use the same monotonic clock. Keep the original video
// bitstream; resample/mix only at stop, outside the realtime capture path.
public sealed class CaptureSession : IDisposable
{
    readonly Recorder video;
    readonly List<CaptureInput> sources;
    readonly CapturePauses pauses=new();
    CaptureAudioMix? audio;
    readonly TaskCompletionSource videoDone=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource completed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    string? directory;
    long firstFrame;
    internal long VideoEpoch100ns=>Interlocked.Read(ref firstFrame);
    internal Task VideoStopped=>videoDone.Task;
    internal bool Paused=>video.Status==RecorderStatus.Paused;
    internal bool CanPause=>VideoEpoch100ns!=0 && video.Status is RecorderStatus.Recording or RecorderStatus.Paused;
    internal TimeSpan RecordedTime=>VideoEpoch100ns==0?TimeSpan.Zero:TimeSpan.FromTicks(pauses.Elapsed(VideoEpoch100ns,TimestampedAudio.Now100ns()));
    internal event Action? RecordingStateChanged;
    public Task Completion=>completed.Task;
    internal float RecordedPeak(string deviceId)=>audio?.RecordedPeak(deviceId)??0;
    public CaptureSession(RecordingSourceBase source,List<CaptureInput> sources,int fps,bool high){
        if(sources.Any(s=>s.GainDb is < -60 or > 0))throw new ArgumentOutOfRangeException(nameof(sources));
        this.sources=sources;
        video=Capture.CreateVideo(source,fps,high);
        video.OnStatusChanged+=(_,e)=>{
            if(e.Status is not (RecorderStatus.Recording or RecorderStatus.Paused))return;
            long now=TimestampedAudio.Now100ns();
            if(e.Status==RecorderStatus.Recording)Interlocked.CompareExchange(ref firstFrame,now,0);
            pauses.Set(e.Status==RecorderStatus.Paused,now);
            RecordingStateChanged?.Invoke();
        };
        video.OnRecordingComplete+=(_,_)=>videoDone.TrySetResult();
        video.OnRecordingFailed+=(_,e)=>videoDone.TrySetException(new IOException(e.Error));
    }
    public void Start(string destination){
        if(directory!=null)throw new InvalidOperationException();
        directory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!,".framenote-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try{
            audio=new CaptureAudioMix(sources,directory,pauses);
            video.Record(Path.Combine(directory,"video.mp4"));
            _=Finish(destination,audio);
        }catch{try{audio?.Dispose();}finally{Clean();}throw;}
    }
    async Task Finish(string destination,CaptureAudioMix mix){
        try{
            try{await videoDone.Task.ConfigureAwait(false);}finally{mix.Dispose();}
            var file=Path.Combine(directory!,"video.mp4");
            if(sources.Count>0){
                if(firstFrame==0)throw new IOException(L.Text("缺少视频起点时间。"));
                var withSound=Path.Combine(directory!,"complete.mp4");
                await Mux(file,withSound,(firstFrame-mix.Epoch)/1e7,mix.Tracks).ConfigureAwait(false);file=withSound;
            }
            File.Move(file,destination,true);Clean();completed.TrySetResult();
        }catch(Exception e){
            // Preserve the original recording if audio finalization or the final move fails.
            completed.TrySetException(new IOException(L.Format($"{e.Message}\n本次录制的临时素材：{directory}"),e));
        }
    }
    internal static async Task Mux(string file,string output,double videoStart,IEnumerable<(string Path,int GainDb)> tracks){
        var raw=await Probe.Run("ffprobe","-v","error","-select_streams","v:0","-show_entries","stream=duration","-of","json",file).ConfigureAwait(false);
        using var doc=JsonDocument.Parse(raw);
        var duration=double.Parse(doc.RootElement.GetProperty("streams")[0].GetProperty("duration").GetString()!,CultureInfo.InvariantCulture);
        if(!double.IsFinite(duration)||duration<=0)throw new InvalidDataException(L.Text("无有效视频时长。"));
        var args=new List<string>{"-hide_banner","-loglevel","error","-nostdin","-copyts","-i",file};
        CaptureAudioMix.AppendMix(args,1,videoStart,tracks);
        args.AddRange(["-map","0:v:0","-map","[mix]","-c:v","copy","-c:a","aac","-b:a","192k","-t",duration.ToString("F7",CultureInfo.InvariantCulture),"-movflags","+faststart",output]);
        await Probe.Run("ffmpeg",args.ToArray()).ConfigureAwait(false);
    }
    public void Stop()=>video.Stop();
    internal void TogglePause(){
        if(!CanPause)return;
        if(Paused)video.Resume();else video.Pause();
    }
    void Clean(){try{if(directory!=null)Directory.Delete(directory,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    public void Dispose()=>video.Dispose();
}
