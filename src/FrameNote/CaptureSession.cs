using System.Globalization;
using System.IO;
using System.Text.Json;
using ScreenRecorderLib;

namespace FrameNote;

// All endpoint timestamps use the same monotonic clock. Keep the original video
// bitstream; resample/mix only at stop, outside the realtime capture path.
public sealed class CaptureSession : IDisposable
{
    readonly Recorder video;
    readonly List<AudioSourceBase> sources;
    readonly List<TimestampedAudio> audio=[];
    readonly TaskCompletionSource videoDone=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource completed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    string? directory;
    long epoch,firstFrame;
    internal long VideoEpoch100ns=>Interlocked.Read(ref firstFrame);
    public Task Completion=>completed.Task;
    public CaptureSession(RecordingSourceBase source,List<AudioSourceBase> sources,int fps,bool high){
        this.sources=sources;
        video=Capture.CreateVideo(source,fps,high);
        video.OnStatusChanged+=(_,e)=>{if(e.Status==RecorderStatus.Recording)Interlocked.CompareExchange(ref firstFrame,TimestampedAudio.Now100ns(),0);};
        video.OnRecordingComplete+=(_,_)=>videoDone.TrySetResult();
        video.OnRecordingFailed+=(_,e)=>videoDone.TrySetException(new IOException(e.Error));
    }
    public void Start(string destination){
        if(directory!=null)throw new InvalidOperationException();
        directory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!,".framenote-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        epoch=TimestampedAudio.Now100ns();
        try{
            foreach(var source in sources){
                string id=source switch {LoopbackAudioSource l=>l.DeviceName,CaptureAudioSource c=>c.DeviceName,_=>throw new NotSupportedException("未知音源类型。")};
                audio.Add(new TimestampedAudio(id,source is LoopbackAudioSource,Path.Combine(directory,$"audio-{audio.Count}.mka"),epoch));
            }
            video.Record(Path.Combine(directory,"video.mp4"));
            _=Finish(destination);
        }catch{try{StopAudio();}finally{Clean();}throw;}
    }
    void StopAudio(){
        Exception? error=null;
        foreach(var track in audio){try{track.Dispose();}catch(Exception e){error??=e;}}
        if(error!=null)throw new IOException("采集音频失败。",error);
    }
    async Task Finish(string destination){
        try{
            try{await videoDone.Task.ConfigureAwait(false);}finally{StopAudio();}
            var file=Path.Combine(directory!,"video.mp4");
            if(audio.Count>0){
                if(firstFrame==0)throw new IOException("缺少视频起点时间。");
                var withSound=Path.Combine(directory!,"complete.mp4");
                await Mux(file,withSound,(firstFrame-epoch)/1e7,audio.Where(t=>t.HasPackets).Select(t=>t.Path)).ConfigureAwait(false);file=withSound;
            }
            File.Move(file,destination,true);Clean();completed.TrySetResult();
        }catch(Exception e){
            // Preserve the original recording if audio finalization or the final move fails.
            completed.TrySetException(new IOException($"{e.Message}\n本次录制的临时素材：{directory}",e));
        }
    }
    internal static async Task Mux(string file,string output,double videoStart,IEnumerable<string> tracks){
        var raw=await Probe.Run("ffprobe","-v","error","-select_streams","v:0","-show_entries","stream=duration","-of","json",file).ConfigureAwait(false);
        using var doc=JsonDocument.Parse(raw);
        var duration=double.Parse(doc.RootElement.GetProperty("streams")[0].GetProperty("duration").GetString()!,CultureInfo.InvariantCulture);
        if(!double.IsFinite(duration)||duration<=0)throw new InvalidDataException("无有效视频时长。");
        var args=new List<string>{"-hide_banner","-loglevel","error","-nostdin","-copyts","-i",file};
        var filters=new List<string>();var labels=new List<string>();
        // A completely idle loopback endpoint may legitimately deliver no packets.
        foreach(var track in tracks){
            args.AddRange(["-i",track]);int n=labels.Count+1;var label=$"[a{n}]";labels.Add(label);
            // first_pts=0 trims preroll or inserts missing leading silence. Later gaps
            // and clock skew are recovered from packet PTS, not from callback arrival.
            var start=videoStart.ToString("F7",CultureInfo.InvariantCulture);
            filters.Add($"[{n}:a]asetpts=PTS-{start}/TB,aresample=48000:async=1000:first_pts=0:min_hard_comp=0.04,aformat=channel_layouts=stereo{label}");
        }
        if(labels.Count==0)filters.Add("anullsrc=sample_rate=48000:channel_layout=stereo[mix]");
        else filters.Add(string.Concat(labels)+(labels.Count==1?"anull":$"amix=inputs={labels.Count}:normalize=0")+ ",apad[mix]");
        args.AddRange(["-filter_complex",string.Join(';',filters),"-map","0:v:0","-map","[mix]","-c:v","copy","-c:a","aac","-b:a","192k","-t",duration.ToString("F7",CultureInfo.InvariantCulture),"-movflags","+faststart",output]);
        await Probe.Run("ffmpeg",args.ToArray()).ConfigureAwait(false);
    }
    public void Stop()=>video.Stop();
    void Clean(){try{if(directory!=null)Directory.Delete(directory,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    public void Dispose()=>video.Dispose();
}
