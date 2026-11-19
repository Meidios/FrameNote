using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace FrameNote;

public sealed partial class Desk
{
    CancellationTokenSource? videoExportCancel;
    static string Num(double value)=>value.ToString("0.######",CultureInfo.InvariantCulture);
    static Process MediaProcess(List<string> args,bool input,bool output){
        var start=new ProcessStartInfo(Probe.Tool("ffmpeg")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=input,RedirectStandardOutput=output,RedirectStandardError=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);return Process.Start(start)??throw new IOException("无法启动 FFmpeg。");
    }
    async Task ExportVideo(){
        if(doc==null || media==null || viewingImage || voiceCapture!=null)return;
        var dialog=new SaveFileDialog{Filter="带批注的 MP4 视频|*.mp4",DefaultExt=".mp4",FileName=Path.GetFileNameWithoutExtension(doc.FileName)+"_批注.mp4",Title="导出 MP4 · H.264 高画质压缩，最高 1080p，包含语音批注"};
        if(dialog.ShowDialog(this)!=true)return;
        if(string.Equals(Path.GetFullPath(dialog.FileName),Path.GetFullPath(media),StringComparison.OrdinalIgnoreCase))throw new IOException("请选择新的文件名，以保留原片。");
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);videoExportCancel=cancel;
        await Work(async()=>{
            string output=dialog.FileName+"."+Guid.NewGuid().ToString("N")+".tmp.mp4";
            try{
                await RenderVideo(doc,media,output,cancel.Token);
                cancel.Token.ThrowIfCancellationRequested();File.Move(output,dialog.FileName,true);
                Send(new{type="status",text="带批注的 MP4 已保存："+dialog.FileName});
            }catch(OperationCanceledException){Send(new{type="status",text="已取消 MP4 导出。"});}
            finally{File.Delete(output);videoExportCancel=null;Send(new{type="exportProgress",active=false});}
        });
    }
    async Task RenderVideo(Review review,string input,string output,CancellationToken token){
        review.Check();Send(new{type="exportProgress",active=true,text="正在准备视频与批注…"});
        using var meta=JsonDocument.Parse(await Probe.Run(token,"ffprobe","-v","error","-select_streams","v:0","-show_entries","stream=width,height,avg_frame_rate,sample_aspect_ratio:stream_side_data=rotation","-of","json",input));
        var stream=meta.RootElement.GetProperty("streams")[0];int sw=stream.GetProperty("width").GetInt32(),sh=stream.GetProperty("height").GetInt32();
        double sar=1;
        if(stream.TryGetProperty("sample_aspect_ratio",out var aspect)){
            var pair=aspect.GetString()?.Split(':');if(pair is {Length:2} && double.TryParse(pair[0],CultureInfo.InvariantCulture,out var numerator) && double.TryParse(pair[1],CultureInfo.InvariantCulture,out var denominator) && numerator>0 && denominator>0)sar=numerator/denominator;
        }
        if(stream.TryGetProperty("side_data_list",out var sides) && sides.EnumerateArray().Any(s=>s.TryGetProperty("rotation",out var r)&&Math.Abs(r.GetDouble())%180==90)){(sw,sh)=(sh,sw);sar=1/sar;}
        var rate=stream.GetProperty("avg_frame_rate").GetString()!.Split('/');double fps=double.Parse(rate[0],CultureInfo.InvariantCulture)/double.Parse(rate[1],CultureInfo.InvariantCulture);
        if(!double.IsFinite(fps)||fps<=0)fps=30;fps=Math.Min(fps,120);
        double displayWidth=sw*sar,scale=Math.Min(1,Math.Min(1920d/displayWidth,1080d/sh));int width=Math.Max(2,(int)(displayWidth*scale)/2*2),height=Math.Max(2,(int)(sh*scale)/2*2);
        var renderer=new VideoAnnotations(review,width,height,sw,sh,ImageDirectory);
        var info=await Probe.Inspect(input,token);var voices=review.Notes.Where(n=>n.Voice!=null).ToArray();
        var encode=new List<string>{"-nostdin","-v","error","-f","rawvideo","-pixel_format","bgra","-video_size",$"{width}x{renderer.OutputHeight}","-framerate",Num(fps),"-i","pipe:0","-i",input};
        foreach(var n in voices)encode.AddRange(["-i",ReviewVoice.PathFor(ImageDirectory,n.Voice!.Asset)]);
        var filters=new List<string>();var mix=new List<string>();
        if(info.Audio!=""){filters.Add("[1:a:0]asetpts=PTS-STARTPTS[original]");mix.Add("[original]");}
        for(int i=0;i<voices.Length;i++){
            var n=voices[i];double length=Math.Min(n.Voice!.DurationUs,Math.Min(review.DurationUs,n.EndUs??n.StartUs+n.Voice.DurationUs)-n.StartUs)/1e6;
            filters.Add($"[{i+2}:a:0]atrim=duration={Num(length)},asetpts=PTS-STARTPTS,adelay={Num(n.StartUs/1000d)}:all=1[voice{i}]");mix.Add($"[voice{i}]");
        }
        encode.AddRange(["-map","0:v:0"]);
        if(mix.Count>0){filters.Add(string.Concat(mix)+$"amix=inputs={mix.Count}:duration=longest:normalize=0,alimiter=limit=0.95:level=0:latency=1[audio]");encode.AddRange(["-filter_complex",string.Join(';',filters),"-map","[audio]","-c:a","aac","-b:a","192k"]);}
        encode.AddRange(["-t",Num(review.DurationUs/1e6),"-c:v","libx264","-preset","medium","-crf","20","-pix_fmt","yuv420p","-movflags","+faststart",output]);
        using var decoder=MediaProcess(["-nostdin","-v","error","-i",input,"-map","0:v:0","-an","-vf",$"scale={width}:{height},setsar=1,fps={Num(fps)}","-f","rawvideo","-pix_fmt","bgra","pipe:1"],false,true);
        using var encoder=MediaProcess(encode,true,false);
        static void Kill(Process process){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}}
        using var stop=token.Register(()=>{Kill(decoder);Kill(encoder);});
        var decodeError=decoder.StandardError.ReadToEndAsync();var encodeError=encoder.StandardError.ReadToEndAsync();
        try{
            byte[] pixels=new byte[width*height*4];long frame=0;var progress=Stopwatch.StartNew();
            while(true){
                token.ThrowIfCancellationRequested();int offset=0;
                while(offset<pixels.Length){int count=await decoder.StandardOutput.BaseStream.ReadAsync(pixels.AsMemory(offset),token);if(count==0)break;offset+=count;}
                if(offset==0)break;if(offset!=pixels.Length)throw new IOException("视频解码帧不完整。");
                var rendered=renderer.Render(pixels,(long)Math.Round(frame*1e6/fps));await encoder.StandardInput.BaseStream.WriteAsync(rendered,token);frame++;
                if(progress.ElapsedMilliseconds>250){Send(new{type="exportProgress",active=true,text=$"正在渲染 MP4 · {Math.Min(99,(int)(frame*1e8/fps/review.DurationUs))}%"});progress.Restart();await Task.Delay(1,token);}
            }
            encoder.StandardInput.Close();await decoder.WaitForExitAsync(token);await encoder.WaitForExitAsync(token);
            if(decoder.ExitCode!=0)throw new IOException("视频解码失败："+await decodeError);
            if(encoder.ExitCode!=0)throw new IOException("视频编码失败："+await encodeError);
            if(frame==0)throw new IOException("视频中没有可导出的画面。");
        }catch{Kill(decoder);Kill(encoder);token.ThrowIfCancellationRequested();throw;}
    }
}
