using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FrameNote;
public static class Probe
{
    public static string Tool(string name) {
        var bundled=Path.Combine(AppContext.BaseDirectory,"tools",name+".exe");
        return File.Exists(bundled)?bundled:name+".exe";
    }
    public static Task<string> Run(string name,params string[] args)=>Run(default,name,args);
    public static async Task<string> Run(CancellationToken token,string name,params string[] args) {
        token.ThrowIfCancellationRequested();
        var start=new ProcessStartInfo(Tool(name)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var a in args)start.ArgumentList.Add(a);
        using var p=Process.Start(start)??throw new IOException("无法启动媒体工具。");
        using var cancel=token.Register(()=>{try{p.Kill(entireProcessTree:true);}catch(InvalidOperationException){}});
        var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();var error=await stderr;var output=await stdout;
        token.ThrowIfCancellationRequested();
        if(p.ExitCode!=0)throw new InvalidDataException(error.Trim());
        return output;
    }
    static long Us(string s)=>(long)Math.Round(double.Parse(s,CultureInfo.InvariantCulture)*1e6);
    public sealed record Info(long Duration,long Origin,string Video,string Audio,string PixelFormat){
        public bool Direct(string path)=>Path.GetExtension(path).ToLowerInvariant() is (".mp4" or ".mov" or ".m4v" or ".webm")
            && Video is ("h264" or "vp8" or "vp9" or "av1") && (Video!="h264" || PixelFormat is "yuv420p" or "yuvj420p")
            && Audio is ("" or "aac" or "opus" or "vorbis" or "mp3");
    }
    public static async Task<Info> Inspect(string path,CancellationToken token=default){
        var meta=await Run(token,"ffprobe","-v","error","-show_entries","format=start_time,duration:stream=codec_type,codec_name,pix_fmt","-of","json","-i",path);
        using var json=JsonDocument.Parse(meta);
        var streams=json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var v=streams.FirstOrDefault(s=>s.GetProperty("codec_type").GetString()=="video");
        if(v.ValueKind==JsonValueKind.Undefined)throw new InvalidDataException("没有视频轨。");
        var a=streams.FirstOrDefault(s=>s.GetProperty("codec_type").GetString()=="audio");
        var f=json.RootElement.GetProperty("format");
        return new(f.TryGetProperty("duration",out var d)?Us(d.GetString()!):0,f.TryGetProperty("start_time",out var o)?Us(o.GetString()!):0,
            v.GetProperty("codec_name").GetString()!,a.ValueKind==JsonValueKind.Undefined?"":a.GetProperty("codec_name").GetString()!,v.TryGetProperty("pix_fmt",out var fmt)?fmt.GetString()!:"");
    }
    public static async Task<long[]> Frames(string path,Info info,CancellationToken token=default){
        var packets=await Run(token,"ffprobe","-v","error","-select_streams","v:0","-show_entries","packet=pts_time","-of","csv=p=0","-i",path);
        if(packets.Contains("N/A") || string.IsNullOrWhiteSpace(packets))
            packets=await Run(token,"ffprobe","-v","error","-select_streams","v:0","-show_entries","frame=best_effort_timestamp_time","-of","csv=p=0","-i",path);
        var pts=packets.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(l=>l.Trim().Split(',')[0]).Where(l=>double.TryParse(l,CultureInfo.InvariantCulture,out _)).Select(s=>Us(s)-info.Origin).Where(t=>t>=0 && t<info.Duration).Distinct().Order().ToArray();
        if(pts.Length==0)throw new InvalidDataException("无法读取视频帧时间。");
        return pts;
    }
}
