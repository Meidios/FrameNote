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
    void InitCapture(){
        InitMeters();
        SourceInitialized+=(_,_)=>{
            IntPtr hwnd=new WindowInteropHelper(this).Handle;
            bool registered=RegisterHotKey(hwnd,0x464e,0x4000|2|1,0x78);
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h,int m,IntPtr wp,IntPtr lp,ref bool handled)=>{
                if(m==0x0312 && wp.ToInt32()==0x464e && web.CoreWebView2!=null && !pending && !configuring){handled=true;if(recorder!=null)StopCapture();else Send(new{type="recordShortcut"});}
                return IntPtr.Zero;
            });
            if(registered)Closed+=(_,_)=>UnregisterHotKey(hwnd,0x464e);
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
            Send(new{type="status",text="录制中　·　点击「停止录制」或按 Ctrl+Alt+F9 结束"});if(gif)gifLimit.Start();
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
        if(fd.ShowDialog(this)==true)await Work(async()=>{await Capture.Gif(media,note.StartUs/1e6,(end-note.StartUs)/1e6,fd.FileName);Send(new{type="status",text="GIF 已保存："+fd.FileName});});
    }
}
