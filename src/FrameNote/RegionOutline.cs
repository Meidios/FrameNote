using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenRecorderLib;

namespace FrameNote;

// A non-activating, click-through window. All painted pixels are outside the
// selected rectangle, so capture does not rely on a recorder-specific exclusion.
internal sealed class RegionOutline : Window
{
    const int Rim=3;
    [DllImport("user32.dll")] static extern int GetWindowLongW(IntPtr hwnd,int index);
    [DllImport("user32.dll")] static extern int SetWindowLongW(IntPtr hwnd,int index,int style);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);

    internal RegionOutline(Int32Rect monitor,ScreenRect region){
        Title=L.Text("FrameNote 选区边框");WindowStyle=WindowStyle.None;AllowsTransparency=true;
        ResizeMode=ResizeMode.NoResize;ShowActivated=false;Focusable=false;Topmost=true;
        ShowInTaskbar=false;Background=Brushes.Transparent;IsHitTestVisible=false;
        // A one-pixel dark outer keyline keeps the blue visible on light desktops.
        var blue=new Border();blue.SetResourceReference(Border.BorderBrushProperty,"accent");
        var dark=new Border{BorderBrush=Brushes.Black,Child=blue};
        Content=dark;SizeToContent=SizeToContent.Manual;
        void Fit(){
            var hwnd=new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd,new IntPtr(-1),monitor.X+(int)region.Left-Rim,monitor.Y+(int)region.Top-Rim,
                (int)region.Width+2*Rim,(int)region.Height+2*Rim,0x0010);
            var dpi=VisualTreeHelper.GetDpi(this);
            Thickness Pixels(int n)=>new(n/dpi.DpiScaleX,n/dpi.DpiScaleY,n/dpi.DpiScaleX,n/dpi.DpiScaleY);
            dark.BorderThickness=Pixels(1);blue.BorderThickness=Pixels(Rim-1);
        }
        SourceInitialized+=(_,_)=>{
            var hwnd=new WindowInteropHelper(this).Handle;
            const int exStyle=-20,transparent=0x20,noActivate=0x08000000;
            SetWindowLongW(hwnd,exStyle,GetWindowLongW(hwnd,exStyle)|transparent|noActivate);
            Fit();
        };
        ContentRendered+=(_,_)=>Fit();
    }
}
