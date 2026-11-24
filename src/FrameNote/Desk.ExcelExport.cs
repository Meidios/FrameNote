using System.IO;
using System.IO.Compression;

namespace FrameNote;

public sealed partial class Desk
{
    async Task ExportExcel(){
        var review=doc;
        if(review==null || media==null || viewingImage || voiceCapture!=null || review.Notes.Count==0)return;
        await Work(()=>WithExportProgress("Excel",async token=>{
            var settings=CurrentOutput();
            var template=settings.Excel;
            string parent=Directory.CreateDirectory(OutputSettings.DirectoryPath(settings.ExportDirectory)).FullName;
            string stage=Path.Combine(parent,".framenote-excel-"+Guid.NewGuid().ToString("N"));
            string working=Path.Combine(scratch,"excel-"+Guid.NewGuid().ToString("N"));
            try{
                ReportExport(L.Text("正在准备 Excel 批注清单"),0);
                review.Check();
                var notes=review.Notes.OrderBy(n=>n.StartUs).ThenBy(n=>n.Number).ToArray();
                Directory.CreateDirectory(stage);
                if((template.Columns & (ExcelColumns.Preview|ExcelColumns.Media))!=0)
                    await PrepareExcelMedia(review,notes,template.Columns,stage,working,token);
                ReportExport(L.Text("正在写入 Excel 工作簿"),95);
                await Task.Run(()=>ExcelReview.Write(stage,working,review.FileName,notes,template,token),token);
                token.ThrowIfCancellationRequested();
                string stem=Path.GetFileNameWithoutExtension(review.FileName);
                string path;
                if(template.Zip){
                    ReportExport(L.Text("正在打包 Excel 和附件"),98);
                    path=await WriteOutput(parent,stem+"_Excel.zip",zip=>Task.Run(()=>ZipExcel(stage,zip,token),token),token);
                }else{
                    string directory=await PublishExcelFolder(stage,parent,stem,token);
                    path=Path.Combine(directory,ExcelReview.WorkbookName);
                }
                Send(new{type="status",text=L.Text("Excel 批注已导出：")+path});
                // The media links in the workbook resolve relative to the exported folder.
                if(!template.Zip && template.Columns.HasFlag(ExcelColumns.Media))return new DirectoryInfo(Path.GetDirectoryName(path)!);
                return new FileInfo(path);
            }finally{
                if(Directory.Exists(working))await RetrySharingViolation(()=>Directory.Delete(working,true),CancellationToken.None);
                if(Directory.Exists(stage))await RetrySharingViolation(()=>Directory.Delete(stage,true),CancellationToken.None);
            }
        }));
    }

    static async Task ZipExcel(string directory,string path,CancellationToken token){
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        foreach(string file in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories)){
            token.ThrowIfCancellationRequested();
            var name=Path.GetRelativePath(directory,file).Replace(Path.DirectorySeparatorChar,'/');
            var entry=zip.CreateEntry(name,CompressionLevel.Fastest);
            using var input=File.OpenRead(file);
            using var output=entry.Open();
            await input.CopyToAsync(output,token).ConfigureAwait(false);
        }
    }

    async Task PrepareExcelMedia(Review review,Note[] notes,ExcelColumns columns,string stage,string working,CancellationToken token){
        int prepared=review.Timeline==null?0:30;
        string input=await EditedMedia(token,p=>ReportExport(L.Text("正在准备剪辑后的画面与声音"),p*prepared/100));
        ReportExport(L.Text("正在准备 Excel 批注清单"),prepared);
        var format=await Probe.ReadVideoFormat(input,token);
        var frames=await Probe.Frames(input,await Probe.Inspect(input,token),token);
        Directory.CreateDirectory(working);
        if(columns.HasFlag(ExcelColumns.Media))Directory.CreateDirectory(Path.Combine(stage,"media"));
        for(int i=0;i<notes.Length;i++){
            token.ThrowIfCancellationRequested();
            var note=notes[i];
            string title=L.Format($"正在导出 Excel 媒体 {i+1}/{notes.Length} · #{note.Number}");
            void MediaProgress(int p)=>ReportExport(title,prepared+(95-prepared)*(100*i+p)/(100*notes.Length));
            MediaProgress(0);
            string? output=columns.HasFlag(ExcelColumns.Media)?Path.Combine(stage,ExcelReview.MediaFile(note)):null;
            string? preview=columns.HasFlag(ExcelColumns.Preview)?Path.Combine(working,$"{note.Number}.png"):null;
            await WriteExcelMedia(input,review,note,format,frames,working,output,preview,MediaProgress,token);
        }
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

    async Task WriteExcelMedia(string input,Review review,Note note,Probe.VideoFormat format,long[] frames,string working,string? output,string? preview,Action<int> report,CancellationToken token){
        bool gif=note.EndUs.HasValue;
        string layer=Path.Combine(working,"layer.png");
        var (width,height)=gif?format.Fit(800,800):format.Fit(1920,1080);
        var renderer=new VideoAnnotations(review,width,height,format.Width,format.Height,ImageDirectory,defaultRulerUnit,showFooter:false);
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
        string branches=output!=null && preview!=null?",split[annotated][poster]":output!=null?"[annotated]":"[poster]";
        filters.Add($"[{scene}][1:v:0]overlay=format=rgb{branches}");
        if(preview!=null){
            double scale=Math.Min(1,Math.Min(160d/width,88d/height));
            int previewWidth=Math.Max(1,(int)Math.Round(width*scale)),previewHeight=Math.Max(1,(int)Math.Round(height*scale));
            filters.Add($"[poster]trim=end_frame=1,scale={previewWidth}:{previewHeight}:flags=lanczos[preview]");
        }
        string result="annotated";
        bool writeGif=gif && output!=null;
        if(writeGif){
            // Per-frame palettes stream long Regions without buffering the entire clip.
            filters.Add("[annotated]split[color][pixels]");
            filters.Add("[color]palettegen=stats_mode=single[palette]");
            filters.Add("[pixels][palette]paletteuse=new=1:dither=sierra2_4a[gif]");
            result="gif";
        }
        args.AddRange(["-filter_complex",string.Join(';',filters)]);
        if(output!=null){
            args.AddRange(["-map",$"[{result}]","-an"]);
            if(writeGif){
                long count=(duration*12+999_999)/1_000_000;
                int finalDelay=Math.Max(1,(int)(Math.Round(duration/10000d)-Math.Round((count-1)*100d/12)));
                args.AddRange(["-frames:v",count.ToString(),"-loop","0","-final_delay",finalDelay.ToString()]);
            }else args.AddRange(["-frames:v","1","-c:v","png","-threads:v","1"]);
            args.Add(output);
        }
        if(preview!=null)args.AddRange(["-map","[preview]","-an","-frames:v","1","-c:v","png","-threads:v","1",preview]);
        if(writeGif)await Probe.EncodeWithProgress(args,duration,report,token);
        else await Probe.Run(token,"ffmpeg",args.ToArray());
    }
}
