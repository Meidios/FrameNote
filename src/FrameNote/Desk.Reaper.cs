using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace FrameNote;

public sealed partial class Desk
{
    async Task SendReaper(){
        const string action="FrameNote - Import review.lua";
        var bundled=Path.Combine(AppContext.BaseDirectory,"reaper",action);
        var config=Path.Combine(root,"reaper-action.txt");
        var installed=File.Exists(config)?(await File.ReadAllTextAsync(config)).Trim():"";
        // The installation marker distinguishes this receiver from the older manual-only action.
        if(!File.Exists(installed)||!File.ReadAllText(installed).Contains("-- FrameNote receiver v1",StringComparison.Ordinal)){
            var folder=new OpenFolderDialog{Title="选择 REAPER 脚本安装目录",InitialDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"REAPER","Scripts")};
            if(folder.ShowDialog(this)!=true){Send(new{type="status",text="已取消安装 REAPER 脚本。"});return;}
            installed=Path.Combine(folder.FolderName,action);
            if(!Path.GetFullPath(installed).Equals(bundled,StringComparison.OrdinalIgnoreCase))
                Packages.Atomic(installed,f=>{using var src=File.OpenRead(bundled);src.CopyTo(f);});
            File.WriteAllText(installed+".app",Environment.ProcessPath!,new UTF8Encoding(false));
            Packages.Atomic(config,f=>f.Write(Encoding.UTF8.GetBytes(installed)));
            Send(new{type="status",text="REAPER 脚本已安装。打开目标工程后，点此按钮发送当前文件。"});return;
        }
        if(media==null||doc==null)throw new InvalidOperationException("请先打开视频或 .framenote 审阅包。");
        var candidates=Process.GetProcessesByName("reaper");
        try{
            using var self=Process.GetCurrentProcess();
            var live=candidates.Where(p=>p.SessionId==self.SessionId && p.MainWindowHandle!=IntPtr.Zero).ToArray();
            if(live.Length!=1)throw new InvalidOperationException(live.Length==0?"请先打开目标 REAPER 工程，再点击发送。":"检测到多个 REAPER 实例，请只保留目标实例后再发送。");
            var target=live[0];
            var job=Path.Combine(scratch,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
            try{
                string Hex(string s)=>Convert.ToHexString(Encoding.UTF8.GetBytes(s));
                var lines=new List<string>{Hex(installed),target.Id.ToString(),openedPackage?doc.Id.ToString():"-",Hex(media),
                    doc.DurationUs.ToString(),openedPackage||activeRecording!=null?"copy":"link",Hex(doc.FileName)};
                if(openedPackage)foreach(var n in doc.Notes)lines.Add($"{n.StartUs}\t{n.EndUs??-1}\t{Hex(n.ReaperName())}");
                await File.WriteAllLinesAsync(Path.Combine(job,"request"),lines,new UTF8Encoding(false));
                var entry=Path.Combine(job,"send.lua");
                File.Copy(Path.Combine(AppContext.BaseDirectory,"reaper","send.lua"),entry);
                var start=new ProcessStartInfo(target.MainModule!.FileName){UseShellExecute=false};
                start.ArgumentList.Add("-nonewinst");start.ArgumentList.Add(entry);
                using var dispatch=Process.Start(start)??throw new IOException("无法联系 REAPER。");
                Send(new{type="status",text="正在发送到 REAPER…"});
                var clock=Stopwatch.StartNew();var result=Path.Combine(job,"result");
                while(!File.Exists(result)){
                    if(target.HasExited)throw new IOException("REAPER 已退出。");
                    if(clock.Elapsed.TotalSeconds>15 && !File.Exists(Path.Combine(job,"started")))throw new IOException("REAPER 未响应，请关闭其模态对话框后再试。");
                    await Task.Delay(100);
                }
                var response=await File.ReadAllTextAsync(result);
                if(response!="OK")throw new InvalidOperationException(response);
                Send(new{type="status",text=openedPackage?"已发送到 REAPER：视频、Marker 和 Region 已插入。":"视频已插入 REAPER。"});
            }finally{try{Directory.Delete(job,true);}catch(IOException){}}
        }finally{foreach(var p in candidates)p.Dispose();}
    }
}

