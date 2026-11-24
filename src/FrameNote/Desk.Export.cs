using System.IO;

namespace FrameNote;

public sealed partial class Desk
{
    ExportDialog? exportDialog;
    void ReportExport(string text,int percent)=>exportDialog?.Report(text,percent);
    OutputSettings CurrentOutput()=>OutputSettings.Read(ReadCapturePrefs());

    static async Task<string> WriteOutput(string directory,string name,Func<string,Task> write,CancellationToken token){
        directory=Directory.CreateDirectory(OutputSettings.DirectoryPath(directory)).FullName;
        string ext=Path.GetExtension(name),stem=Path.GetFileNameWithoutExtension(name);
        if(stem.Length>230)stem=stem[..(char.IsHighSurrogate(stem[229])?229:230)];
        string stage=Path.Combine(directory,".framenote-"+Guid.NewGuid().ToString("N")+ext);
        try{
            await write(stage);
            for(int n=1;;n++){
                token.ThrowIfCancellationRequested();
                string dest=Path.Combine(directory,stem+(n==1?"":$" ({n})")+ext);
                try{await RetrySharingViolation(()=>File.Move(stage,dest),token);return dest;}
                catch(IOException) when(File.Exists(dest)||Directory.Exists(dest)){}
            }
        }finally{File.Delete(stage);}
    }

    async Task WithExportProgress(string format,Func<CancellationToken,Task<FileSystemInfo?>> write){
        // Leave the WebView message callback before entering WPF's modal message loop.
        await System.Windows.Threading.Dispatcher.Yield();
        if(lifetime.IsCancellationRequested)return;
        var dialog=new ExportDialog(this,format,write,lifetime.Token,shortcuts["cancel"]);
        exportDialog=dialog;
        try{
            if(dialog.ShowDialog()==true && dialog.Output is {} output){
                string folder=output is FileInfo file?file.DirectoryName!:output.FullName;
                try{Browse(folder);}
                catch(Exception e) when(e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException){
                    Serilog.Log.Warning(e,"Could not open export folder");
                    ReportError(L.Text("文件已导出，但无法打开目录：")+folder);
                }
            }
            if(dialog.Cancelled)Send(new{type="status",text=L.Format($"已取消 {format} 导出。")});
        }finally{exportDialog=null;}
    }

    Task ExportPackage(Review review,string input)=>Work(()=>WithExportProgress(L.Text("审阅包"),async token=>{
        string name=Path.ChangeExtension(review.FileName,".framenote");
        var progress=new Progress<int>(percent=>{
            if(exportDialog?.IsRunning(token)==true)ReportExport(L.Text("正在写入审阅包"),percent);
        });
        string path=await WriteOutput(CurrentOutput().ExportDirectory,name,stage=>
            Task.Run(()=>Packages.Save(stage,input,review,ImageDirectory,token,progress),token),token);
        Send(new{type="status",text=L.Text("审阅包已保存：")+path});
        return new FileInfo(path);
    }));
}
