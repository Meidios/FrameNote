using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FrameNote;

// The transport is outside the video rectangle, so no separate overlay window is needed.
public sealed class VideoSurface : HwndHost
{
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern nint CreateWindowEx(int exStyle,string cls,string title,int style,int x,int y,int w,int h,nint parent,nint menu,nint instance,nint param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint hwnd);

    protected override HandleRef BuildWindowCore(HandleRef parent){
        const int child=0x40000000,visible=0x10000000,clipChildren=0x02000000,blackRect=4;
        var hwnd=CreateWindowEx(0,"STATIC","",child|visible|clipChildren|blackRect,0,0,1,1,parent.Handle,0,0,0);
        if(hwnd==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        return new HandleRef(this,hwnd);
    }
    protected override void DestroyWindowCore(HandleRef hwnd)=>DestroyWindow(hwnd.Handle);
}
