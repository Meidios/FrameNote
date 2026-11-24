using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FrameNote;

// The transport is outside the video rectangle, so no separate overlay window is needed.
public sealed class VideoSurface : HwndHost
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct WindowClass {
        public uint Style;
        public nint WindowProc;
        public int ClassExtra,WindowExtra;
        public nint Instance,Icon,Cursor,Background,MenuName;
        public string ClassName;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern nint GetModuleHandle(string? name);
    [DllImport("gdi32.dll")] static extern nint GetStockObject(int index);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern nint LoadCursor(nint instance,nint cursor);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern nint DefWindowProc(nint hwnd,uint msg,nint wp,nint lp);
    delegate nint WindowProcedure(nint hwnd,uint msg,nint wp,nint lp);
    // The WPF video area owns pointer gestures. Let hit testing pass through both native layers.
    static readonly WindowProcedure surfaceProc=(hwnd,msg,wp,lp)=>msg==0x0084?-1:DefWindowProc(hwnd,msg,wp,lp);
    static readonly nint module=GetModuleHandle(null);
    static readonly Lazy<string> videoClass=new(()=>{
        var cls=new WindowClass {
            ClassName="FrameNote.VideoSurface", Instance=module,
            WindowProc=Marshal.GetFunctionPointerForDelegate(surfaceProc),
            Cursor=LoadCursor(0,32512), // IDC_ARROW
            Background=GetStockObject(4) // BLACK_BRUSH, independent of the Windows theme
        };
        if(RegisterClass(ref cls)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        return cls.ClassName;
    });
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern nint CreateWindowEx(int exStyle,string cls,string title,int style,int x,int y,int w,int h,nint parent,nint menu,nint instance,nint param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] static extern bool MoveWindow(nint hwnd,int x,int y,int width,int height,bool repaint);
    int width,height;
    Rect viewport=new(0,0,1,1);
    internal nint VideoHandle { get; private set; }

    internal void SetViewport(Rect bounds){
        viewport=bounds;
        PositionVideo();
    }
    void PositionVideo(){
        if(VideoHandle==0 || width==0 || height==0)return;
        MoveWindow(VideoHandle,(int)Math.Round(viewport.X*width),(int)Math.Round(viewport.Y*height),
            Math.Max(1,(int)Math.Round(viewport.Width*width)),Math.Max(1,(int)Math.Round(viewport.Height*height)),true);
    }
    protected override nint WndProc(nint hwnd,int msg,nint wParam,nint lParam,ref bool handled){
        const int wmSize=0x0005;
        if(msg==wmSize){
            width=(int)(lParam&0xffff);height=(int)((lParam>>16)&0xffff);
            PositionVideo();
        }
        return base.WndProc(hwnd,msg,wParam,lParam,ref handled);
    }

    protected override HandleRef BuildWindowCore(HandleRef parent){
        const int style=0x40000000|0x10000000|0x02000000; // WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN
        var hwnd=CreateWindowEx(0,videoClass.Value,"",style,0,0,1,1,parent.Handle,0,module,0);
        if(hwnd==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        // VLC renders into the inner window. Its parent clips zoomed video to the player area.
        VideoHandle=CreateWindowEx(0,videoClass.Value,"",style,0,0,1,1,hwnd,0,module,0);
        if(VideoHandle==0){
            var error=Marshal.GetLastWin32Error();DestroyWindow(hwnd);throw new Win32Exception(error);
        }
        return new HandleRef(this,hwnd);
    }
    protected override void DestroyWindowCore(HandleRef hwnd){VideoHandle=0;DestroyWindow(hwnd.Handle);}
}
