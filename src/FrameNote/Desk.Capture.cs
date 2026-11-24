using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenRecorderLib;
namespace FrameNote;
public sealed partial class Desk
{
    CaptureSession? recorder;
    RegionOutline? regionOutline;
    bool stopping,configuring;
    readonly DispatcherTimer gifLimit=new(){Interval=TimeSpan.FromSeconds(29)};
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr hwnd,int id,uint mods,uint vk);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    readonly Dictionary<int,Shortcut> recordHotkeys=new();
    void UpdateRecordHotkey(Shortcut[] bindings,Shortcut[]? previouslyConfigured=null){
        var hwnd=new WindowInteropHelper(this).Handle;
        if(hwnd==IntPtr.Zero)return;
        var added=new List<int>();
        int id=0x464e;
        foreach(var binding in bindings.Except(recordHotkeys.Values).ToArray()){
            while(recordHotkeys.ContainsKey(id))id++;
            if(id<0xc000 && RegisterHotKey(hwnd,id,0x4000u|(uint)binding.Mods,(uint)binding.Vk)){
                recordHotkeys.Add(id,binding);added.Add(id);
            }else if(previouslyConfigured!=null && !previouslyConfigured.Contains(binding)){
                foreach(int fresh in added){UnregisterHotKey(hwnd,fresh);recordHotkeys.Remove(fresh);}
                throw new InvalidOperationException(L.Format($"无法注册全局录屏快捷键 {binding.Label(InputKind.Keyboard)}，请换一个。"));
            }
        }
        foreach(var old in recordHotkeys.Where(p=>!bindings.Contains(p.Value)).ToArray()){
            UnregisterHotKey(hwnd,old.Key);recordHotkeys.Remove(old.Key);
        }
    }
    void InitCapture(){
        InitMeters();
        SourceInitialized+=(_,_)=>{
            IntPtr hwnd=new WindowInteropHelper(this).Handle;
            UpdateRecordHotkey(shortcuts["record"]);
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h,int m,IntPtr wp,IntPtr lp,ref bool handled)=>{
                if(m==0x0312 && recordHotkeys.TryGetValue(wp.ToInt32(),out var pressed)){
                    handled=true;
                    if(OwnedWindows.OfType<ShortcutDialog>().Any(dialog=>dialog.FindGlobalShortcut(pressed)))return IntPtr.Zero;
                    var latest=shortcutStore.Read();
                    if(!latest.Bindings["record"].Contains(pressed)){ApplyPreferences(latest);return IntPtr.Zero;}
                    if(web.CoreWebView2!=null && IsEnabled && OwnedWindows.Count==0 && !pending && !configuring){
                        if(recorder!=null)StopCapture();else Send(new{type="recordShortcut"});
                    }
                }
                return IntPtr.Zero;
            });
            Closed+=(_,_)=>{foreach(int id in recordHotkeys.Keys)UnregisterHotKey(hwnd,id);};
        };
        gifLimit.Tick+=(_,_)=>StopCapture();
        Closing+=(_,e)=>{if(recorder!=null){e.Cancel=true;StopCapture();}};
    }
    void ClearOutline(){regionOutline?.Close();regionOutline=null;}
    void StopCapture(){if(recorder==null||stopping)return;stopping=true;gifLimit.Stop();ClearOutline();recorder.Stop();Send(new{type="status",text=L.Text("正在结束录制…")});}
    void Record(CaptureSettings settings,bool gif){
        if(recorder!=null){StopCapture();return;}if(configuring||pending)return;configuring=true;
        try{
            string clip=recordings.NewPath();
            string title=L.Text("录屏 ")+DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss")+".mp4";
            var output=CurrentOutput();
            var r=Session(settings,gif,output);recorder=r;stopping=false;
            r.Start(clip);_=CompleteCapture(r,clip,title,gif,output);Send(new{type="recording",value=true,gif});
            var keys=shortcuts["record"];
            Send(new{type="status",text=L.Text("录制中　·　点击「停止录制」")+(keys.Length==0?"":L.Format($"或按 {keys.Label(InputKind.Keyboard)} 结束"))});if(gif)gifLimit.Start();
        }catch(Exception e){Serilog.Log.Error(e,"Could not start recording");Release();Send(new{type="error",text=e.Message});}
        finally{if(recorder==null)ClearOutline();configuring=false;}
    }
    static async Task CopyRecording(string source,string destination,CancellationToken token){
        await using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);
        await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
        await input.CopyToAsync(output,token);
    }
    async Task<string?> SaveRecordingCopy(OutputSettings options,string source,string name,CancellationToken token){
        if(!options.SaveCopy)return null;
        try{await WriteOutput(options.CopyDirectory,name,p=>CopyRecording(source,p,token),token);return null;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException){
            Serilog.Log.Error(e,"Could not save recording copy");
            return L.Text("录制副本保存失败：")+e.Message;
        }
    }
    async Task CompleteCapture(CaptureSession session,string clip,string title,bool gif,OutputSettings options){
        try{await session.Completion;}
        catch(Exception e){Serilog.Log.Error(e,"Recording failed");Release();Send(new{type="error",text=L.Text("录制失败：")+e.Message});return;}
        while(currentWork is {} work)await work.Task;
        Release();await Work(async()=>{
            string? copyError=null;
            if(gif){
                string encoded=Path.Combine(scratch,Guid.NewGuid()+".gif"),name=Path.ChangeExtension(title,".gif");
                bool backupSaved=false;
                try{await WithExportProgress("GIF",async token=>{
                    var info=await Probe.Inspect(clip,token);
                    await Capture.Gif(clip,0,Math.Min(30,info.Duration/1e6),encoded,p=>ReportExport(L.Text("正在渲染 GIF"),p),token);
                    if(options.SaveCopy && !backupSaved && !string.Equals(options.CopyDirectory,options.ExportDirectory,StringComparison.OrdinalIgnoreCase)){
                        copyError=await SaveRecordingCopy(options,encoded,name,token);
                        backupSaved=copyError==null;
                    }
                    var path=await WriteOutput(options.ExportDirectory,name,p=>CopyRecording(encoded,p,token),token);
                    Send(new{type="status",text=L.Text("GIF 已保存：")+path});
                    return new FileInfo(path);
                });}finally{recordings.Discard(clip);File.Delete(encoded);}
            }else{
                copyError=await SaveRecordingCopy(options,clip,title,lifetime.Token);
                await Open(clip,title);
            }
            if(copyError!=null)ReportError(copyError);
        });
    }
    void Release(){ClearOutline();gifLimit.Stop();recorder?.Dispose();recorder=null;stopping=false;Send(new{type="recording",value=false});}
    async Task ExportGif(Guid id){
        var note=doc?.Notes.FirstOrDefault(n=>n.Id==id);
        if(note?.EndUs is not long end || media==null)return;
        var name=Path.ChangeExtension(doc!.FileName,".gif");
        await Work(()=>WithExportProgress("GIF",async token=>{
            if(end-note.StartUs>30_000_000)throw new InvalidDataException(L.Text("GIF 最长 30 秒，请缩短区间。"));
            var input=await EditedMedia(token,p=>ReportExport(L.Text("正在准备剪辑后的画面与声音"),p/3));
            var path=await WriteOutput(CurrentOutput().ExportDirectory,name,p=>Capture.Gif(input,note.StartUs/1e6,(end-note.StartUs)/1e6,p,
                percent=>ReportExport(L.Text("正在渲染 GIF"),33+percent*2/3),token),token);
            Send(new{type="status",text=L.Text("GIF 已保存：")+path});return new FileInfo(path);
        }));
    }
}
