using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ScreenRecorderLib;
using Screen=System.Windows.Forms.Screen;
using ScreenPoint=System.Drawing.Point;

namespace FrameNote;

internal partial class RecordingControls : Window
{
    static readonly DependencyProperty RevealProperty=DependencyProperty.Register(
        "Reveal",typeof(double),typeof(RecordingControls),
        new PropertyMetadata(0d,(sender,e)=>((RecordingControls)sender).ApplyReveal((double)e.NewValue)));
    Screen screen;
    readonly Action stop,pause;
    readonly DispatcherTimer hoverDelay=new(){Interval=TimeSpan.FromMilliseconds(15)};
    (int width,int height,int stopTop,int pauseTop,int diameterX,int diameterY)? lastShape;
    bool expanded;
    ScreenPoint? pressedAt,placedAt;
    Point grabOffset;
    bool dragging;

    internal RecordingControls(Screen screen,Action stop,Action pause){
        this.screen=screen;this.stop=stop;this.pause=pause;
        InitializeComponent();
        StopButton.PreviewMouseLeftButtonDown+=(_,e)=>{
            pressedAt=System.Windows.Forms.Cursor.Position;
            grabOffset=e.GetPosition(StopButton);dragging=false;
        };
        StopButton.PreviewMouseMove+=(_,e)=>{
            if(e.LeftButton==MouseButtonState.Pressed)MoveStopButton();
        };
        StopButton.PreviewMouseLeftButtonUp+=(_,e)=>{
            if(pressedAt==null)return;
            MoveStopButton();
            pressedAt=null;
            if(!dragging)return;
            // Consume the release before ButtonBase can raise Click, even after a round trip.
            e.Handled=true;StopButton.ReleaseMouseCapture();
        };
        StopButton.LostMouseCapture+=(_,_)=>pressedAt=null;
        MouseEnter+=(_,_)=>ScheduleHover();MouseLeave+=(_,_)=>ScheduleHover();
        hoverDelay.Tick+=UpdateHover;
        DpiChanged+=(_,_)=>Dispatcher.InvokeAsync(Anchor,DispatcherPriority.Loaded);
        Closed+=(_,_)=>hoverDelay.Stop();
    }
    void ScheduleHover(){hoverDelay.Stop();hoverDelay.Start();}
    void UpdateHover(object? sender,EventArgs e){
        // The gap passes clicks to the desktop. Keep checking the pointer while it
        // crosses the gap since Windows sends no further mouse events there.
        if(expanded && !IsMouseOver){
            var cursor=System.Windows.Forms.Cursor.Position;
            var p=PointFromScreen(new Point(cursor.X,cursor.Y));
            if(p.X>=0 && p.X<Width && p.Y>=0 && p.Y<Height)return;
        }
        hoverDelay.Stop();
        if(expanded==IsMouseOver)return;
        expanded=IsMouseOver;
        PauseButton.IsHitTestVisible=expanded;
        double from=(double)GetValue(RevealProperty),to=expanded?1:0;
        var duration=TimeSpan.FromMilliseconds(180*Math.Abs(to-from));
        BeginAnimation(RevealProperty,new DoubleAnimation(from,to,duration){
            EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}
        });
    }
    void ApplyReveal(double progress){
        var dpi=VisualTreeHelper.GetDpi(this);
        int width=(int)Math.Round(Width*dpi.DpiScaleX),height=(int)Math.Round(Height*dpi.DpiScaleY);
        int stopTop=height-(int)Math.Round(StopButton.Height*dpi.DpiScaleY);
        int pauseTop=(int)Math.Round(stopTop*(1-progress));
        PauseOffset.Y=pauseTop/dpi.DpiScaleY;
        PauseButton.Visibility=progress>0?Visibility.Visible:Visibility.Hidden;
        StopRotation.Angle=90*progress;

        // Shape the non-layered HWND to the two button faces. The uncovered desktop
        // stays visible and clickable while Windows capture exclusion remains available.
        double diameter=2*((CornerRadius)FindResource("ControlCorners")).TopLeft;
        int dx=(int)Math.Round(diameter*dpi.DpiScaleX),dy=(int)Math.Round(diameter*dpi.DpiScaleY);
        var shape=(width,height,stopTop,pauseTop,dx,dy);
        var hwnd=new WindowInteropHelper(this).Handle;
        if(hwnd==IntPtr.Zero || lastShape==shape)return;
        var region=CreateRoundRectRgn(0,stopTop,width+1,height+1,dx,dy);
        if(region==IntPtr.Zero)return;
        try{
            if(pauseTop!=stopTop){
                var upper=CreateRoundRectRgn(0,pauseTop,width+1,pauseTop+height-stopTop+1,dx,dy);
                if(upper==IntPtr.Zero)return;
                try{if(CombineRgn(region,region,upper,2)==0)return;} // RGN_OR
                finally{DeleteObject(upper);}
            }
            if(SetWindowRgn(hwnd,region,true)!=0){
                region=IntPtr.Zero; // Windows now owns this region.
                lastShape=shape;
            }
        }finally{if(region!=IntPtr.Zero)DeleteObject(region);}
    }
    void StopClicked(object sender,RoutedEventArgs e){Focus();stop();}
    void PauseClicked(object sender,RoutedEventArgs e)=>pause();
    internal void BeginStopping(){
        StopButton.IsEnabled=false;PauseButton.IsEnabled=false;
    }
    internal void SetState(bool canPause,bool paused){
        PauseButton.IsEnabled=canPause;
        PauseIcon.Data=(Geometry)FindResource(paused?"play":"pause");
        AutomationProperties.SetName(PauseButton,L.Text(paused?"继续录制":"暂停录制"));
    }
    internal void ShowForCapture(bool windowTarget){
        // Keep this top-level HWND opaque. A layered WPF window can reject display affinity.
        var hwnd=new WindowInteropHelper(this).EnsureHandle();
        const int exStyle=-20,toolWindow=0x80;
        SetWindowLongW(hwnd,exStyle,GetWindowLongW(hwnd,exStyle)|toolWindow);
        HwndSource.FromHwnd(hwnd).AddHook((IntPtr h,int message,IntPtr w,IntPtr l,ref bool handled)=>{
            // Pause without interrupting the captured app's focus. A click on Stop
            // activates this process so Windows can hand keyboard focus back to the desk.
            if(message==0x0021 && PauseButton.IsMouseOver){handled=true;return new IntPtr(3);} // WM_MOUSEACTIVATE / MA_NOACTIVATE
            return IntPtr.Zero;
        });
        bool excluded=OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)&&Recorder.SetExcludeFromCapture(hwnd,true);
        if(!excluded&&!windowTarget)
            throw new InvalidOperationException(L.Text("Windows 无法将悬浮按钮排除在录屏之外。可关闭「录制时自动隐藏主窗口」后再录制。"));
        Anchor();Show();Anchor();
    }
    void MoveStopButton(){
        if(pressedAt is not {} origin || !StopButton.IsMouseCaptured)return;
        var cursor=System.Windows.Forms.Cursor.Position;
        var dpi=VisualTreeHelper.GetDpi(this);
        if(!dragging && Math.Abs(cursor.X-origin.X)<SystemParameters.MinimumHorizontalDragDistance*dpi.DpiScaleX &&
           Math.Abs(cursor.Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance*dpi.DpiScaleY)return;
        dragging=true;
        screen=Screen.FromPoint(cursor);
        int x=cursor.X-(int)Math.Round(grabOffset.X*dpi.DpiScaleX);
        int y=cursor.Y-(int)Math.Round((Height-StopButton.Height+grabOffset.Y)*dpi.DpiScaleY);
        var position=new ScreenPoint(x,y);
        if(position==placedAt)return;
        placedAt=position;
        Anchor();
    }
    void Anchor(){
        var hwnd=new WindowInteropHelper(this).Handle;
        if(hwnd==IntPtr.Zero)return;
        var dpi=VisualTreeHelper.GetDpi(this);
        var area=screen.WorkingArea;
        int x=placedAt?.X??(area.Right-(int)Math.Round((Width+20)*dpi.DpiScaleX));
        int y=placedAt?.Y??(area.Bottom-(int)Math.Round((Height+20)*dpi.DpiScaleY));
        x=Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-(int)Math.Ceiling(Width*dpi.DpiScaleX)));
        y=Math.Clamp(y,area.Top,Math.Max(area.Top,area.Bottom-(int)Math.Ceiling(Height*dpi.DpiScaleY)));
        SetWindowPos(hwnd,new IntPtr(-1),x,y,0,0,0x0011); // SWP_NOACTIVATE | SWP_NOSIZE
        ApplyReveal((double)GetValue(RevealProperty));
    }
    [DllImport("gdi32.dll")]static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int ellipseWidth,int ellipseHeight);
    [DllImport("gdi32.dll")]static extern int CombineRgn(IntPtr result,IntPtr first,IntPtr second,int mode);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")]static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
    [DllImport("user32.dll")]static extern int GetWindowLongW(IntPtr hwnd,int index);
    [DllImport("user32.dll")]static extern int SetWindowLongW(IntPtr hwnd,int index,int style);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
}
