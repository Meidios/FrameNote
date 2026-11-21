using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace FrameNote;

public sealed partial class Desk
{
    CancellationTokenSource? exportCancel;
    void ReportExport(string text,int percent)=>Send(new{type="exportProgress",active=true,text,percent});
    static string Num(double value)=>value.ToString("0.######",CultureInfo.InvariantCulture);
    enum VideoOutput { Mp4, GifFile, GifClipboard }
    static Review ExportReview(Review review,bool hasSourceAudio){
        if(review.Timeline is not {} timeline)return review;
        // A silent source can still have an editable original-audio lane.
        if(!hasSourceAudio)timeline=timeline with{Clips=timeline.Clips.Where(c=>c.Track!="audio").ToList()};
        long end=timeline.Clips.Select(c=>c.StartUs+c.DurationUs).DefaultIfEmpty().Max();
        end=Math.Max(end,review.Notes.Max(n=>n.EndUs)??0);
        // A Mark inside the cut stops at its end. A standalone Mark after the cut needs display time.
        var lastMark=review.Notes.Where(n=>n.EndUs==null).MaxBy(n=>n.StartUs);
        if(lastMark!=null && lastMark.StartUs>=end)end=VideoAnnotations.End(lastMark,review.DurationUs);
        if(end==0)throw new InvalidDataException("时间线上没有可导出的片段或批注。");
        return review with{DurationUs=end,Timeline=timeline};
    }
    async Task ExportVideo(VideoOutput target=VideoOutput.Mp4){
        if(doc==null || media==null || playbackInfo==null || viewingImage || voiceCapture!=null)return;
        var review=ExportReview(doc,playbackInfo.Audio!="");
        bool gif=target!=VideoOutput.Mp4;
        string format=gif?"GIF":"MP4",extension=gif?".gif":".mp4";
        string stem=Path.GetFileNameWithoutExtension(doc.FileName)+"_批注";
        string? destination=null;
        if(target!=VideoOutput.GifClipboard){
            var dialog=new SaveFileDialog{Filter=$"带批注的 {format}|*{extension}",DefaultExt=extension,FileName=stem+extension,
                Title=gif?"将带批注的 GIF 另存为":"导出 MP4 · H.264 高画质压缩，最高 1080p，包含语音批注"};
            if(dialog.ShowDialog(this)!=true)return;
            destination=dialog.FileName;
            if(string.Equals(Path.GetFullPath(destination),Path.GetFullPath(media),StringComparison.OrdinalIgnoreCase))throw new IOException("请选择新的文件名，以保留原片。");
        }
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        await Work(async()=>{
            exportCancel=cancel;
            string output=Path.Combine(destination==null?scratch:Path.GetDirectoryName(destination)!,Guid.NewGuid().ToString("N")+".tmp"+extension);
            try{
                int prepared=review.Timeline==null?0:40;
                if(prepared>0)ReportExport("正在准备剪辑后的画面与声音",0);
                var input=await EditedMedia(cancel.Token,p=>ReportExport("正在准备剪辑后的画面与声音",p*prepared/100),review);
                await RenderVideo(review,input,output,prepared,gif,cancel.Token);
                cancel.Token.ThrowIfCancellationRequested();
                if(destination!=null)File.Move(output,destination,true);
                else await GifClipboard.Publish(output,stem,cancel.Token);
                ReportExport(format+" 导出完成",100);
                Send(new{type="status",text=destination==null?"带批注的 GIF 已复制到粘贴板。":$"带批注的 {format} 已保存："+destination});
            }catch(OperationCanceledException){Send(new{type="status",text=$"已取消 {format} 导出。"});}
            finally{exportCancel=null;Send(new{type="exportProgress",active=false});File.Delete(output);}
        });
    }
    async Task RenderVideo(Review review,string input,string output,int prepared,bool gif,CancellationToken token){
        review.Check();ReportExport("正在准备视频与批注",prepared);
        int renderStart=prepared+5;
        void RenderProgress(int percent)=>ReportExport(gif?"正在渲染 GIF":"正在渲染 MP4",renderStart+(100-renderStart)*percent/100);
        var format=await Probe.ReadVideoFormat(input,token);
        var (width,height)=gif?format.Fit(800,800):format.Fit(1920,1080);
        const int gifFps=12;
        long gifFrames=(review.DurationUs*gifFps+999_999)/1_000_000;
        double fps=gif?gifFps:format.Fps;
        string frameRate=gif?gifFps.ToString(CultureInfo.InvariantCulture):format.FrameRate;
        var renderer=new VideoAnnotations(review,width,height,format.Width,format.Height,ImageDirectory);
        string videoFilter=$"scale={width}:{height},setsar=1,fps={frameRate}"+(gif?$":round=up,tpad=stop_mode=clone:stop=-1,trim=end_frame={gifFrames}":"");
        bool liveLens=review.Notes.Any(n=>n.Shape?.Kind=="magnifier");
        var encode=new List<string>{"-nostdin","-v","error","-i",input};
        var filters=new List<string>();
        string videoMap="[video]";
        string? overlayDirectory=null;
        try{
            if(liveLens){
                encode.AddRange(["-f","rawvideo","-pixel_format","bgra","-video_size",$"{width}x{renderer.OutputHeight}","-framerate",frameRate,"-i","pipe:0"]);
                videoMap="1:v:0";
            }else if(review.Notes.Count>0){
                overlayDirectory=Directory.CreateTempSubdirectory("framenote-overlay-").FullName;
                var timeline=await renderer.WriteOverlayTimeline(overlayDirectory,fps,frameRate,token,
                    p=>ReportExport("正在准备视频与批注",prepared+5*p/100));
                encode.AddRange(["-f","concat","-safe","0","-i",timeline]);
                filters.Add($"[0:v:0]{videoFilter},setpts=PTS-STARTPTS,pad={width}:{renderer.OutputHeight}[base]");
                filters.Add("[base][1:v:0]overlay=eof_action=repeat[video]");
            }else{
                filters.Add($"[0:v:0]{videoFilter},setpts=PTS-STARTPTS[video]");
            }
            bool audio=false;
            if(gif){
                string scene=liveLens?"[1:v:0]":videoMap;
                filters.Add(scene+"split[pixels][colors]");
                filters.Add("[colors]palettegen=stats_mode=single[palette]");
                filters.Add("[pixels][palette]paletteuse=new=1:dither=sierra2_4a[gif]");
                videoMap="[gif]";
            }else{
                var info=await Probe.Inspect(input,token);
                var voices=review.Timeline==null?review.Notes.Where(n=>n.Voice!=null).ToArray():Array.Empty<Note>();
                AddExportAudio(encode,filters,voices,review,info.Audio!="");
                audio=info.Audio!=""||voices.Length>0;
            }
            if(filters.Count>0)encode.AddRange(["-filter_complex",string.Join(';',filters)]);
            encode.AddRange(["-map",videoMap]);
            if(gif){
                int lastDelay=Math.Max(1,(int)(Math.Round(review.DurationUs/10000d)-Math.Round((gifFrames-1)*100d/gifFps)));
                encode.AddRange(["-frames:v",gifFrames.ToString(),"-an","-loop","0","-final_delay",lastDelay.ToString()]);
            }else{
                if(audio)encode.AddRange(["-map","[audio]","-c:a","aac","-b:a","192k"]);
                encode.AddRange(["-t",Num(review.DurationUs/1e6),"-c:v","libx264","-preset","medium","-crf","20","-pix_fmt","yuv420p","-movflags","+faststart"]);
            }
            RenderProgress(0);
            if(liveLens){
                encode.Add(output);
                await EncodeLiveLenses(input,videoFilter,renderer,width,height,fps,review.DurationUs,encode,RenderProgress,token);
            }else{
                encode.AddRange(["-abort_on","empty_output_stream",output]);
                await Probe.EncodeWithProgress(encode,review.DurationUs,RenderProgress,token);
            }
        }finally{if(overlayDirectory!=null)Directory.Delete(overlayDirectory,true);}
    }
    void AddExportAudio(List<string> encode,List<string> filters,Note[] voices,Review review,bool hasSourceAudio){
        var mix=new List<string>();
        // A voice is attached to a note, so the second input is always its visual overlay or rendered video.
        foreach(var n in voices)encode.AddRange(["-i",ReviewVoice.PathFor(ImageDirectory,n.Voice!.Asset)]);
        if(hasSourceAudio){filters.Add("[0:a:0]asetpts=PTS-STARTPTS[original]");mix.Add("[original]");}
        for(int i=0;i<voices.Length;i++){
            var n=voices[i];double length=Math.Min(n.Voice!.DurationUs,Math.Min(review.DurationUs,n.EndUs??n.StartUs+n.Voice.DurationUs)-n.StartUs)/1e6;
            filters.Add($"[{i+2}:a:0]atrim=duration={Num(length)},asetpts=PTS-STARTPTS,adelay={Num(n.StartUs/1000d)}:all=1[voice{i}]");mix.Add($"[voice{i}]");
        }
        if(mix.Count>0)filters.Add(string.Concat(mix)+$"amix=inputs={mix.Count}:duration=longest:normalize=0,alimiter=limit=0.95:level=0:latency=1[audio]");
    }
    async Task EncodeLiveLenses(string input,string scale,VideoAnnotations renderer,int width,int height,double fps,long duration,List<string> encode,Action<int> report,CancellationToken token){
        using var decoder=Probe.StartFfmpeg(["-nostdin","-v","error","-i",input,"-map","0:v:0","-an","-vf",scale,"-f","rawvideo","-pix_fmt","bgra","pipe:1"],false,true);
        using var encoder=Probe.StartFfmpeg(encode,true,false);
        using var stop=token.Register(()=>{Probe.Kill(decoder);Probe.Kill(encoder);});
        var decodeError=decoder.StandardError.ReadToEndAsync();var encodeError=encoder.StandardError.ReadToEndAsync();
        try{
            byte[] pixels=new byte[width*height*4];long frame=0;var progress=Stopwatch.StartNew();
            while(true){
                token.ThrowIfCancellationRequested();int offset=0;
                while(offset<pixels.Length){int count=await decoder.StandardOutput.BaseStream.ReadAsync(pixels.AsMemory(offset),token);if(count==0)break;offset+=count;}
                if(offset==0)break;if(offset!=pixels.Length)throw new IOException("视频解码帧不完整。");
                var rendered=renderer.Render(pixels,(long)Math.Round(frame*1e6/fps));await encoder.StandardInput.BaseStream.WriteAsync(rendered,token);frame++;
                if(progress.ElapsedMilliseconds>250){report(Math.Min(99,(int)(frame*1e8/fps/duration)));progress.Restart();await Task.Delay(1,token);}
            }
            encoder.StandardInput.Close();await decoder.WaitForExitAsync(token);await encoder.WaitForExitAsync(token);
            if(decoder.ExitCode!=0)throw new IOException("视频解码失败："+await decodeError);
            if(encoder.ExitCode!=0)throw new IOException("视频编码失败："+await encodeError);
            if(frame==0)throw new IOException("视频中没有可导出的画面。");
        }catch{
            Probe.Kill(decoder);Probe.Kill(encoder);
            await Task.WhenAll(decoder.WaitForExitAsync(),encoder.WaitForExitAsync());
            token.ThrowIfCancellationRequested();throw;
        }
    }
}
