using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace FrameNote;

public sealed partial class Desk
{
    internal ReaperIntegration ReaperScripts=>new(root);
    [DllImport("user32.dll")] static extern nint WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] static extern nint GetAncestor(nint hwnd,uint flags);

    string? InstalledReceiver(){
        var scripts=ReaperScripts;
        if(scripts.Installed==null){
            string suggested=scripts.TargetFolder;
            var preference=scripts.CustomFolder;
            var folder=new OpenFolderDialog{Title=L.Text("选择 REAPER 脚本安装目录"),InitialDirectory=suggested};
            if(folder.ShowDialog(this)!=true){Send(new{type="status",text=L.Text("已取消安装 REAPER 脚本。")});return null;}
            scripts.Install(folder.FolderName);
            scripts.SetFolder(suggested.Equals(folder.FolderName,StringComparison.OrdinalIgnoreCase)?preference:folder.FolderName);
            Send(new{type="status",text=L.Text("REAPER 脚本已安装。打开目标工程后，点此按钮发送当前文件。")});return null;
        }
        return scripts.Install(Path.GetDirectoryName(scripts.Installed)!);
    }
    async Task SendReaper(Point? drop){
        var installed=drop.HasValue?ReaperIntegration.Bundled:InstalledReceiver();
        if(installed==null)return;
        if(media==null||doc==null||viewingImage)throw new InvalidOperationException(L.Text("请先打开视频或 .framenote 审阅包。"));
        var candidates=Process.GetProcessesByName("reaper");
        try{
            using var self=Process.GetCurrentProcess();
            var live=candidates.Where(p=>p.SessionId==self.SessionId && p.MainWindowHandle!=IntPtr.Zero).ToArray();
            var placement="cursor";
            if(drop is {} point){
                var xy=new System.Drawing.Point((int)Math.Round(point.X),(int)Math.Round(point.Y));
                var hwnd=GetAncestor(WindowFromPoint(xy),2); // GA_ROOT
                live=live.Where(p=>p.MainWindowHandle==hwnd).ToArray();
                if(live.Length==0){Send(new{type="status",text=L.Text("未放入 REAPER 编排区，已取消导入。")});return;}
                placement=$"drop\t{xy.X}\t{xy.Y}";
            }
            if(live.Length!=1)throw new InvalidOperationException(live.Length==0?L.Text("请先打开目标 REAPER 工程，再点击发送。"):L.Text("检测到多个 REAPER 实例，请拖放到目标工程或只保留一个实例。"));
            var target=live[0];
            var review=doc;var source=media;
            await WithExportProgress("REAPER",async token=>{
                var job=Path.Combine(scratch,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
                try{
                    Send(new{type="status",text=L.Text("正在发送到 REAPER…")});
                    var progress=new Progress<int>(p=>{
                        if(exportDialog?.IsRunning(token)==true && p<85)ReportExport(L.Text("正在准备 REAPER 媒体…"),p);
                    });
                    var delivery=await ReaperDelivery.Prepare(review,source,ImageDirectory,Path.Combine(scratch,"reaper-media"),defaultRulerUnit,token,progress);
                    var playable=delivery.Video;
                    var lines=new List<string>{ReaperDelivery.Hex(installed),target.MainWindowHandle.ToInt64().ToString("X"),openedPackage?review.Id.ToString():"-",ReaperDelivery.Hex(playable),
                        delivery.VideoDurationUs.ToString(),openedPackage||activeRecording!=null||playable!=source?"copy":"link",ReaperDelivery.Hex(review.FileName),placement};
                    lines.AddRange(delivery.Rows);
                    await File.WriteAllLinesAsync(Path.Combine(job,"request"),lines,new UTF8Encoding(false));
                    await File.WriteAllTextAsync(Path.Combine(job,"language"),L.Language);
                    var entry=Path.Combine(job,"send.lua");
                    File.Copy(Path.Combine(AppContext.BaseDirectory,"reaper","send.lua"),entry);
                    var start=new ProcessStartInfo(target.MainModule!.FileName){UseShellExecute=false};
                    start.ArgumentList.Add("-nonewinst");start.ArgumentList.Add(entry);
                    token.ThrowIfCancellationRequested();
                    exportDialog?.BeginCommit();
                    ReportExport(L.Text("正在插入 REAPER 工程…"),85);
                    using var dispatch=Process.Start(start)??throw new IOException(L.Text("无法联系 REAPER。"));
                    var clock=Stopwatch.StartNew();var result=Path.Combine(job,"result");
                    while(!File.Exists(result)){
                        if(target.HasExited)throw new IOException(L.Text("REAPER 已退出。"));
                        if(clock.Elapsed.TotalSeconds>15 && !File.Exists(Path.Combine(job,"started")))throw new IOException(L.Text("REAPER 未响应，请关闭其模态对话框后再试。"));
                        try{
                            var status=Path.Combine(job,"progress");
                            if(File.Exists(status) && int.TryParse(await File.ReadAllTextAsync(status),out var percent))
                                ReportExport(L.Text("正在插入 REAPER 工程…"),percent);
                        }catch(IOException){}
                        await Task.Delay(100);
                    }
                    var response=await File.ReadAllTextAsync(result);
                    if(response!="OK")throw new InvalidOperationException(response);
                    Send(new{type="status",text=L.Text("已发送到 REAPER。")});
                    return null;
                }finally{try{Directory.Delete(job,true);}catch(IOException){}}
            },closeOnSuccess:true);
        }finally{foreach(var p in candidates)p.Dispose();}
    }
}

