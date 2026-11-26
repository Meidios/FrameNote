using System.Globalization;
using System.IO;

namespace FrameNote;

// Screen recordings and voice annotations use the same endpoint clocks and mix.
internal sealed class CaptureAudioMix : IDisposable
{
    readonly List<(TimestampedAudio Track,int GainDb)> audio=[];
    bool stopped;
    internal long Epoch {get;}=TimestampedAudio.Now100ns();
    internal IEnumerable<(string Path,int GainDb)> Tracks=>audio.Where(t=>t.Track.HasPackets).Select(t=>(t.Track.Path,t.GainDb));
    internal IEnumerable<Task> Workers=>audio.Select(t=>t.Track.Completion);
    internal float RecordedPeak(string id)=>audio.FirstOrDefault(t=>t.Track.DeviceId==id).Track?.RecordedPeak??0;

    internal CaptureAudioMix(IEnumerable<CaptureInput> sources,string directory,CapturePauses? pauses=null,Action<long,float>? observePeak=null){
        try{
            foreach(var source in sources){
                if(source.GainDb is < -60 or > 0)throw new ArgumentOutOfRangeException(nameof(sources));
                if(source.Muted)continue;
                var path=Path.Combine(directory,$"audio-{audio.Count}.mka");
                audio.Add((new TimestampedAudio(source.Id,source.Loopback,path,Epoch,pauses,observePeak),source.GainDb));
            }
        }catch{Dispose();throw;}
    }
    public void Dispose(){
        if(stopped)return;
        stopped=true;
        Exception? error=null;
        foreach(var (track,_) in audio){try{track.Dispose();}catch(Exception e){error??=e;}}
        if(error!=null)throw new IOException(L.Text("采集音频失败。"),error);
    }
    // Callers map [mix] and trim the padded audio to the recording duration.
    internal static void AppendMix(List<string> args,int firstInput,double startSeconds,IEnumerable<(string Path,int GainDb)> tracks){
        var filters=new List<string>();var labels=new List<string>();
        var start=startSeconds.ToString("F7",CultureInfo.InvariantCulture);
        foreach(var (path,gainDb) in tracks){
            args.AddRange(["-i",path]);
            int n=firstInput+labels.Count;var label=$"[a{n}]";labels.Add(label);
            // first_pts=0 trims preroll or inserts leading silence. Recover later
            // gaps and clock skew from packet PTS, not callback arrival time.
            filters.Add($"[{n}:a]asetpts=PTS-{start}/TB,aresample=48000:async=1000:first_pts=0:min_hard_comp=0.04,aformat=channel_layouts=stereo,volume={gainDb.ToString(CultureInfo.InvariantCulture)}dB{label}");
        }
        // An idle loopback endpoint can legitimately deliver no packets.
        if(labels.Count==0)filters.Add("anullsrc=sample_rate=48000:channel_layout=stereo[mix]");
        else filters.Add(string.Concat(labels)+(labels.Count==1?"anull":$"amix=inputs={labels.Count}:normalize=0")+",apad[mix]");
        args.AddRange(["-filter_complex",string.Join(';',filters)]);
    }
}
