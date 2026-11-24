using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace FrameNote;

public sealed partial class Desk
{
    const string ReaperAction="FrameNote - Import review.lua";
    [DllImport("user32.dll")] static extern nint WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] static extern nint GetAncestor(nint hwnd,uint flags);

    static void SyncReaperFile(string path,byte[] bytes){
        if(!File.Exists(path)||!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            Packages.Atomic(path,f=>f.Write(bytes));
    }
    async Task<string?> InstalledReceiver(string bundled){
        var config=Path.Combine(root,"reaper-action.txt");
        var installed=File.Exists(config)?(await File.ReadAllTextAsync(config)).Trim():"";
        var first=!Path.IsPathFullyQualified(installed)||!string.Equals(Path.GetFileName(installed),ReaperAction,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(Path.GetDirectoryName(installed));
        if(first){
            var folder=new OpenFolderDialog{Title=L.Text("选择 REAPER 脚本安装目录"),InitialDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"REAPER","Scripts")};
            if(folder.ShowDialog(this)!=true){Send(new{type="status",text=L.Text("已取消安装 REAPER 脚本。")});return null;}
            installed=Path.Combine(folder.FolderName,ReaperAction);
        }
        SyncReaperFile(installed,await File.ReadAllBytesAsync(bundled));
        SyncReaperFile(installed+".app",Encoding.UTF8.GetBytes(Environment.ProcessPath!));
        if(first){
            Packages.Atomic(config,f=>f.Write(Encoding.UTF8.GetBytes(installed)));
            Send(new{type="status",text=L.Text("REAPER 脚本已安装。打开目标工程后，点此按钮发送当前文件。")});return null;
        }
        return installed;
    }
    async Task SendReaper(Point? drop){
        var bundled=Path.Combine(AppContext.BaseDirectory,"reaper",ReaperAction);
        var installed=drop.HasValue?bundled:await InstalledReceiver(bundled);
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
            var job=Path.Combine(scratch,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
            try{
                var playable=await EditedMedia(lifetime.Token);
                string Hex(string s)=>Convert.ToHexString(Encoding.UTF8.GetBytes(s));
                var lines=new List<string>{Hex(installed),target.MainWindowHandle.ToInt64().ToString("X"),openedPackage?doc.Id.ToString():"-",Hex(playable),
                    doc.DurationUs.ToString(),openedPackage||activeRecording!=null||doc.Timeline!=null?"copy":"link",Hex(doc.FileName),placement};
                if(openedPackage)foreach(var n in doc.Notes)lines.Add(n.ReaperRow(true));
                await File.WriteAllLinesAsync(Path.Combine(job,"request"),lines,new UTF8Encoding(false));
                await File.WriteAllTextAsync(Path.Combine(job,"language"),L.Language);
                var entry=Path.Combine(job,"send.lua");
                File.Copy(Path.Combine(AppContext.BaseDirectory,"reaper","send.lua"),entry);
                var start=new ProcessStartInfo(target.MainModule!.FileName){UseShellExecute=false};
                start.ArgumentList.Add("-nonewinst");start.ArgumentList.Add(entry);
                using var dispatch=Process.Start(start)??throw new IOException(L.Text("无法联系 REAPER。"));
                Send(new{type="status",text=L.Text("正在发送到 REAPER…")});
                var clock=Stopwatch.StartNew();var result=Path.Combine(job,"result");
                while(!File.Exists(result)){
                    if(target.HasExited)throw new IOException(L.Text("REAPER 已退出。"));
                    if(clock.Elapsed.TotalSeconds>15 && !File.Exists(Path.Combine(job,"started")))throw new IOException(L.Text("REAPER 未响应，请关闭其模态对话框后再试。"));
                    await Task.Delay(100);
                }
                var response=await File.ReadAllTextAsync(result);
                if(response!="OK")throw new InvalidOperationException(response);
                Send(new{type="status",text=openedPackage?L.Text("已发送到 REAPER：视频、Marker 和 Region 已插入。"):L.Text("视频已插入 REAPER。")});
            }finally{try{Directory.Delete(job,true);}catch(IOException){}}
        }finally{foreach(var p in candidates)p.Dispose();}
    }
}

