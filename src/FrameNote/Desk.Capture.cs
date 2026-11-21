using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
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
                throw new InvalidOperationException($"无法注册全局录屏快捷键 {binding.Label(InputKind.Keyboard)}，请换一个。");
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
                    var latest=shortcutStore.Read();
                    if(!latest["record"].Contains(pressed)){ApplyShortcuts(latest);return IntPtr.Zero;}
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
    void StopCapture(){if(recorder==null||stopping)return;stopping=true;gifLimit.Stop();ClearOutline();recorder.Stop();Send(new{type="status",text="正在结束录制…"});}
    void Record(CaptureSettings settings,bool gif){
        if(recorder!=null){StopCapture();return;}if(configuring||pending)return;configuring=true;
        try{
            string clip=recordings.NewPath();
            string title="录屏 "+DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss")+".mp4";
            var r=Session(settings,gif);recorder=r;stopping=false;
            r.Start(clip);_=CompleteCapture(r,clip,title,gif);Send(new{type="recording",value=true,gif});
            var keys=shortcuts["record"];
            Send(new{type="status",text="录制中　·　点击「停止录制」"+(keys.Length==0?"":$"或按 {keys.Label(InputKind.Keyboard)} 结束")});if(gif)gifLimit.Start();
        }catch(Exception e){Release();Send(new{type="error",text=e.Message});}
        finally{if(recorder==null)ClearOutline();configuring=false;}
    }
    async Task CompleteCapture(CaptureSession session,string clip,string title,bool gif){
        try{await session.Completion;}
        catch(Exception e){Release();Send(new{type="error",text="录制失败："+e.Message});return;}
        while(currentWork is {} work)await work.Task;
        Release();await Work(async()=>{
            if(gif){
                try{
                    var fd=new SaveFileDialog{DefaultExt=".gif",Filter="GIF|*.gif",FileName="FrameNote "+DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss")};
                    if(fd.ShowDialog(this)!=true){Send(new{type="status",text="已取消保存 GIF。"});return;}
                    await Capture.Gif(clip,0,30,fd.FileName);Send(new{type="status",text="GIF 已保存："+fd.FileName});
                }finally{recordings.Discard(clip);}
            }
            else await Open(clip,title);
        });
    }
    void Release(){ClearOutline();gifLimit.Stop();recorder?.Dispose();recorder=null;stopping=false;Send(new{type="recording",value=false});}
    async Task ExportGif(Guid id){
        var note=doc?.Notes.FirstOrDefault(n=>n.Id==id);
        if(note?.EndUs is not long end || media==null)return;
        if(end-note.StartUs>30_000_000)throw new InvalidDataException("GIF 最长 30 秒，请缩短区间。");
        var fd=new SaveFileDialog{DefaultExt=".gif",Filter="GIF|*.gif",FileName=Path.GetFileNameWithoutExtension(doc!.FileName)+".gif"};
        if(fd.ShowDialog(this)==true)await Work(async()=>{await Capture.Gif(await EditedMedia(lifetime.Token),note.StartUs/1e6,(end-note.StartUs)/1e6,fd.FileName);Send(new{type="status",text="GIF 已保存："+fd.FileName});});
    }
}
