using System.IO;
using Microsoft.Win32;

namespace FrameNote;

public sealed partial class Desk
{
    async Task ExportExcel(){
        var review=doc;
        if(review==null || media==null || viewingImage || voiceCapture!=null || review.Notes.Count==0)return;
        var picker=new OpenFolderDialog{Title="选择保存位置 · 将新建含 Excel 清单和媒体的文件夹"};
        if(picker.ShowDialog(this)!=true)return;
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token=cancel.Token;
        await Work(async()=>{
            exportCancel=cancel;
            string stage=Path.Combine(picker.FolderName,".framenote-excel-"+Guid.NewGuid().ToString("N"));
            string working=Path.Combine(scratch,"excel-"+Guid.NewGuid().ToString("N"));
            try{
                ReportExport("正在准备 Excel 批注清单",0);
                review.Check();
                int prepared=review.Timeline==null?0:30;
                string input=await EditedMedia(token,p=>ReportExport("正在准备剪辑后的画面与声音",p*prepared/100));
                ReportExport("正在准备 Excel 批注清单",prepared);
                var format=await Probe.ReadVideoFormat(input,token);
                var frames=await Probe.Frames(input,await Probe.Inspect(input,token),token);
                var notes=review.Notes.OrderBy(n=>n.StartUs).ThenBy(n=>n.Number).ToArray();
                Directory.CreateDirectory(Path.Combine(stage,"media"));
                Directory.CreateDirectory(working);
                for(int i=0;i<notes.Length;i++){
                    token.ThrowIfCancellationRequested();
                    var note=notes[i];
                    string title=$"正在导出 Excel 媒体 {i+1}/{notes.Length} · #{note.Number}";
                    void MediaProgress(int p)=>ReportExport(title,prepared+(95-prepared)*(100*i+p)/(100*notes.Length));
                    MediaProgress(0);
                    await WriteExcelMedia(input,review,note,format,frames,working,Path.Combine(stage,ExcelReview.MediaFile(note)),MediaProgress,token);
                }
                ReportExport("正在写入 Excel 工作簿",95);
                await Task.Run(()=>ExcelReview.Write(stage,working,review.FileName,notes,token),token);
                token.ThrowIfCancellationRequested();
                string directory=await PublishExcelFolder(stage,picker.FolderName,Path.GetFileNameWithoutExtension(review.FileName),token);
                ReportExport("Excel 导出完成",100);
                Send(new{type="status",text="Excel 批注已导出："+Path.Combine(directory,ExcelReview.WorkbookName)});
            }catch(OperationCanceledException) when(token.IsCancellationRequested){
                Send(new{type="status",text="已取消 Excel 导出。"});
            }finally{
                exportCancel=null;Send(new{type="exportProgress",active=false});
                if(Directory.Exists(working))await RetrySharingViolation(()=>Directory.Delete(working,true),CancellationToken.None);
                if(Directory.Exists(stage))await RetrySharingViolation(()=>Directory.Delete(stage,true),CancellationToken.None);
            }
        });
    }

    static async Task RetrySharingViolation(Action operation,CancellationToken token){
        // A sync provider can briefly hold a handle after the writer has closed the file.
        for(int attempt=0;;attempt++){
            token.ThrowIfCancellationRequested();
            try{operation();return;}
            catch(IOException e) when(attempt<5 && (e.HResult & 0xffff) is 32 or 33 or 145){
                await Task.Delay(100<<attempt,token);
            }
        }
    }

    static async Task<string> PublishExcelFolder(string stage,string parent,string stem,CancellationToken token){
        // Leave room for the suffix and collision counter within a Windows path component.
        const int maxStem=235;
        if(stem.Length>maxStem)stem=stem[..(char.IsHighSurrogate(stem[maxStem-1])?maxStem-1:maxStem)];
        for(int copy=1;;copy++){
            string name=stem+"_Excel"+(copy==1?"":$" ({copy})");
            string target=Path.Combine(parent,name);
            try{await RetrySharingViolation(()=>Directory.Move(stage,target),token);return target;}
            catch(IOException) when(Directory.Exists(target)||File.Exists(target)){}
        }
    }

    async Task WriteExcelMedia(string input,Review review,Note note,Probe.VideoFormat format,long[] frames,string working,string output,Action<int> report,CancellationToken token){
        bool gif=note.EndUs.HasValue;
        string layer=Path.Combine(working,"layer.png");
        string preview=Path.Combine(working,$"{note.Number}.png");
        var (width,height)=gif?format.Fit(800,800):format.Fit(1920,1080);
        var renderer=new VideoAnnotations(review,width,height,format.Width,format.Height,ImageDirectory,showFooter:false);
        renderer.WriteLayerPng(layer,[note]);

        int index=Array.BinarySearch(frames,note.StartUs);
        long first=frames[index>=0?index:Math.Max(0,~index-1)];
        // Start decoding at the displayed source frame, including when a note falls between VFR timestamps.
        var args=new List<string>{"-nostdin","-v","error","-abort_on","empty_output_stream","-ss",Num(Math.Max(0,first-1)/1e6),"-i",input,"-i",layer};
        var filters=new List<string>();
        long duration=note.EndUs.GetValueOrDefault()-note.StartUs;
        string timing=gif?$"settb=1/1000000,setpts=PTS-STARTPTS+({first-note.StartUs}),trim=end_pts={duration},tpad=stop_mode=clone:stop=-1,fps=12:start_time=0:round=up,":"";
        filters.Add($"[0:v:0]{timing}scale={width}:{height}:flags=lanczos,setsar=1,format=rgb24[base]");
        string scene="base";
        if(note.Shape is {Kind:"magnifier"} lens){
            var (rect,crop)=VideoAnnotations.LensBounds(lens,width,height);
            if(rect.Width>=1 && rect.Height>=1){
                filters.Add("[base]split[background][zoom]");
                filters.Add($"[zoom]crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}:exact=1,scale={(int)rect.Width}:{(int)rect.Height}[glass]");
                filters.Add($"[background][glass]overlay={(int)rect.X}:{(int)rect.Y}:format=rgb[scene]");
                scene="scene";
            }
        }
        filters.Add($"[{scene}][1:v:0]overlay=format=rgb,split[annotated][poster]");
        double scale=Math.Min(1,Math.Min(160d/width,88d/height));
        int previewWidth=Math.Max(1,(int)Math.Round(width*scale)),previewHeight=Math.Max(1,(int)Math.Round(height*scale));
        filters.Add($"[poster]trim=end_frame=1,scale={previewWidth}:{previewHeight}:flags=lanczos[preview]");
        string result="annotated";
        if(gif){
            // Per-frame palettes stream long Regions without buffering the entire clip.
            filters.Add("[annotated]split[color][pixels]");
            filters.Add("[color]palettegen=stats_mode=single[palette]");
            filters.Add("[pixels][palette]paletteuse=new=1:dither=sierra2_4a[gif]");
            result="gif";
        }
        args.AddRange(["-filter_complex",string.Join(';',filters),"-map",$"[{result}]","-an"]);
        if(gif){
            long count=(duration*12+999_999)/1_000_000;
            int finalDelay=Math.Max(1,(int)(Math.Round(duration/10000d)-Math.Round((count-1)*100d/12)));
            args.AddRange(["-frames:v",count.ToString(),"-loop","0","-final_delay",finalDelay.ToString()]);
        }else args.AddRange(["-frames:v","1","-c:v","png","-threads:v","1"]);
        args.Add(output);
        args.AddRange(["-map","[preview]","-an","-frames:v","1","-c:v","png","-threads:v","1",preview]);
        if(gif)await Probe.EncodeWithProgress(args,duration,report,token);
        else await Probe.Run(token,"ffmpeg",args.ToArray());
    }
}
