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
    static Process StartReader(string name,params string[] args){
        var start=new ProcessStartInfo(Tool(name)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var a in args)start.ArgumentList.Add(a);
        return Process.Start(start)??throw new IOException(L.Text("无法启动媒体工具。"));
    }
    public static async Task<string> Run(CancellationToken token,string name,params string[] args) {
        token.ThrowIfCancellationRequested();
        using var p=StartReader(name,args);
        using var cancel=token.Register(()=>Kill(p));
        var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();var error=await stderr;var output=await stdout;
        token.ThrowIfCancellationRequested();
        if(p.ExitCode!=0)throw new InvalidDataException(error.Trim());
        return output;
    }
    internal static Process StartFfmpeg(List<string> args,bool input,bool output){
        var start=new ProcessStartInfo(Tool("ffmpeg")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=input,RedirectStandardOutput=output,RedirectStandardError=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);return Process.Start(start)??throw new IOException(L.Text("无法启动 FFmpeg。"));
    }
    internal static void Kill(Process process){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}}
    internal static async Task EncodeWithProgress(List<string> args,long durationUs,Action<int> report,CancellationToken token){
        token.ThrowIfCancellationRequested();
        using var encoder=StartFfmpeg(["-progress","pipe:1","-nostats",..args],false,true);
        using var stop=token.Register(()=>Kill(encoder));
        var error=encoder.StandardError.ReadToEndAsync();
        int last=-1;
        while(await encoder.StandardOutput.ReadLineAsync() is {} line){
            if(durationUs>0 && line.StartsWith("out_time_us=",StringComparison.Ordinal) && long.TryParse(line.AsSpan(12),out long us)){
                int percent=(int)Math.Clamp(100d*us/durationUs,0,99);
                if(percent>last && !token.IsCancellationRequested){last=percent;report(percent);}
            }
        }
        await encoder.WaitForExitAsync();token.ThrowIfCancellationRequested();
        if(encoder.ExitCode!=0)throw new IOException(L.Text("视频编码失败：")+await error);
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
        if(v.ValueKind==JsonValueKind.Undefined)throw new InvalidDataException(L.Text("没有视频轨。"));
        var a=streams.FirstOrDefault(s=>s.GetProperty("codec_type").GetString()=="audio");
        var f=json.RootElement.GetProperty("format");
        return new(f.TryGetProperty("duration",out var d)?Us(d.GetString()!):0,f.TryGetProperty("start_time",out var o)?Us(o.GetString()!):0,
            v.GetProperty("codec_name").GetString()!,a.ValueKind==JsonValueKind.Undefined?"":a.GetProperty("codec_name").GetString()!,v.TryGetProperty("pix_fmt",out var fmt)?fmt.GetString()!:"");
    }
    internal sealed record VideoFormat(int Width,int Height,double Aspect,double Fps,string FrameRate){
        internal (int Width,int Height) Fit(int maxWidth,int maxHeight){
            double displayWidth=Width*Aspect,scale=Math.Min(1,Math.Min(maxWidth/displayWidth,(double)maxHeight/Height));
            return (Math.Max(2,(int)(displayWidth*scale)/2*2),Math.Max(2,(int)(Height*scale)/2*2));
        }
    }
    internal static async Task<VideoFormat> ReadVideoFormat(string input,CancellationToken token){
        using var meta=JsonDocument.Parse(await Run(token,"ffprobe","-v","error","-select_streams","v:0","-show_entries","stream=width,height,avg_frame_rate,sample_aspect_ratio:stream_side_data=rotation","-of","json",input));
        var stream=meta.RootElement.GetProperty("streams")[0];int sw=stream.GetProperty("width").GetInt32(),sh=stream.GetProperty("height").GetInt32();
        double sar=1;
        if(stream.TryGetProperty("sample_aspect_ratio",out var aspect)){
            var pair=aspect.GetString()?.Split(':');if(pair is {Length:2} && double.TryParse(pair[0],CultureInfo.InvariantCulture,out var numerator) && double.TryParse(pair[1],CultureInfo.InvariantCulture,out var denominator) && numerator>0 && denominator>0)sar=numerator/denominator;
        }
        if(stream.TryGetProperty("side_data_list",out var sides) && sides.EnumerateArray().Any(s=>s.TryGetProperty("rotation",out var r)&&Math.Abs(r.GetDouble())%180==90)){(sw,sh)=(sh,sw);sar=1/sar;}
        string frameRate=stream.GetProperty("avg_frame_rate").GetString()!;
        var rate=frameRate.Split('/');double fps=double.Parse(rate[0],CultureInfo.InvariantCulture)/double.Parse(rate[1],CultureInfo.InvariantCulture);
        if(!double.IsFinite(fps)||fps<=0){fps=30;frameRate="30";}
        else if(fps>120){fps=120;frameRate="120";}
        return new(sw,sh,sar,fps,frameRate);
    }
    public static Task<string> DecodeFrame(string source,long us,string file,CancellationToken token)=>
        Run(token,"ffmpeg","-nostdin","-v","error","-ss",(us/1e6).ToString("F6",CultureInfo.InvariantCulture),
            "-i",source,"-map","0:v:0","-frames:v","1","-threads","1",file);
    public static Task<long[]> Frames(string path,Info info,CancellationToken token=default,IProgress<int>? progress=null)=>Task.Run(()=>{
        int lastPercent=-1;
        foreach(var entry in new[]{"packet=pts_time","frame=best_effort_timestamp_time"}){
            token.ThrowIfCancellationRequested();
            using var process=StartReader("ffprobe","-v","error","-select_streams","v:0","-show_entries",entry,"-of","csv=p=0","-i",path);
            using var cancel=token.Register(()=>Kill(process));
            var error=process.StandardError.ReadToEndAsync();
            var times=new List<long>();
            bool missing=false;
            while(process.StandardOutput.ReadLine() is {} line){
                var field=line.AsSpan();
                int comma=field.IndexOf(',');
                if(comma>=0)field=field[..comma];
                if(!double.TryParse(field,CultureInfo.InvariantCulture,out double seconds)){
                    if(field.SequenceEqual("N/A"))missing=true;
                    continue;
                }
                long us=(long)Math.Round(seconds*1e6)-info.Origin;
                if(us<0||us>=info.Duration)continue;
                times.Add(us);
                if(progress==null)continue;
                int percent=Math.Min(99,(int)(100d*us/info.Duration));
                if(percent>lastPercent){progress.Report(percent);lastPercent=percent;}
            }
            process.WaitForExit();token.ThrowIfCancellationRequested();
            if(process.ExitCode!=0)throw new InvalidDataException(error.GetAwaiter().GetResult().Trim());
            if(times.Count==0 || missing && entry=="packet=pts_time")continue;
            var result=times.Distinct().Order().ToArray();
            progress?.Report(100);
            return result;
        }
        throw new InvalidDataException(L.Text("无法读取视频帧时间。"));
    },token);
}
