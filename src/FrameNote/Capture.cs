using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using ScreenRecorderLib;
using NAudio.CoreAudioApi;

namespace FrameNote;
public static class Capture
{
    internal static Recorder CreateVideo(RecordingSourceBase source,int fps,bool high)
    {
        var size=Recorder.GetOutputDimensionsForRecordingSources([source]).CombinedOutputSize;
        double w=source.SourceRect?.Width>0?source.SourceRect.Width:size.Width;
        double h=source.SourceRect?.Height>0?source.SourceRect.Height:size.Height;
        if(w<=0||h<=0)throw new InvalidOperationException("所选窗口没有可录画面，请保持窗口可见。");
        double k=Math.Min(1,Math.Min(1920/w,1080/h));
        var options=RecorderOptions.Default;
        options.SourceOptions=new SourceOptions{RecordingSources=[source]};
        options.AudioOptions=new AudioOptions{IsAudioEnabled=false};
        options.OutputOptions=new OutputOptions{OutputFrameSize=new ScreenSize(Math.Max(2,2*(int)(w*k/2)),Math.Max(2,2*(int)(h*k/2))),Stretch=StretchMode.Uniform};
        options.VideoEncoderOptions=new VideoEncoderOptions{
            Encoder=new H264VideoEncoder{BitrateMode=H264BitrateControlMode.Quality},
            IsHardwareEncodingEnabled=true,IsFixedFramerate=true,Framerate=fps,Quality=high?84:70,
            IsMp4FastStartEnabled=true};
        return Recorder.CreateRecorder(options);
    }
    public static async Task Gif(string source,double start,double seconds,string output){
        if(!double.IsFinite(seconds)||!double.IsFinite(start)||seconds<=0||seconds>30||start<0)throw new InvalidDataException("请选择不超过 30 秒的区间。");
        var temp=Path.Combine(Path.GetDirectoryName(output)!,Guid.NewGuid().ToString("N")+".gif");
        try{
            await Probe.Run("ffmpeg","-hide_banner","-loglevel","error","-nostdin","-ss",FormattableString.Invariant($"{start:F6}"),"-t",FormattableString.Invariant($"{seconds:F6}"),"-i",source,
                "-an","-filter_complex","fps=12,scale='min(800,iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse=dither=sierra2_4a","-loop","0",temp);
            File.Move(temp,output,true);
        }finally{File.Delete(temp);}
    }
}

public sealed class CropWindow:Window
{
    [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool EnumDisplaySettings(string name,int mode,IntPtr settings);
    public ScreenRect? Crop{get;private set;}
    internal Int32Rect Bounds{get;}
    public CropWindow(string device){
        Title="框选录制区域";WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;Topmost=true;ShowInTaskbar=false;
        Background=new SolidColorBrush(Color.FromArgb(32,0,0,0));Cursor=Cursors.Cross;
        // DEVMODEW display union: use physical pixels, including desktops with negative origins.
        var mem=System.Runtime.InteropServices.Marshal.AllocHGlobal(220);int x,y,w,h;
        try{System.Runtime.InteropServices.Marshal.Copy(new byte[220],0,mem,220);System.Runtime.InteropServices.Marshal.WriteInt16(mem,68,220);
            if(!EnumDisplaySettings(device,-1,mem))throw new InvalidOperationException("无法读取显示器尺寸。");
            x=System.Runtime.InteropServices.Marshal.ReadInt32(mem,76);y=System.Runtime.InteropServices.Marshal.ReadInt32(mem,80);
            w=System.Runtime.InteropServices.Marshal.ReadInt32(mem,172);h=System.Runtime.InteropServices.Marshal.ReadInt32(mem,176);
        }finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(mem);}
        Bounds=new Int32Rect(x,y,w,h);
        void FitMonitor(){var hwnd=new System.Windows.Interop.WindowInteropHelper(this).Handle;SetWindowPos(hwnd,new IntPtr(-1),x,y,w,h,0x40);}
        // Moving onto a monitor can apply WM_DPICHANGED's suggested size during startup.
        // Once WPF has rendered there, fit the client to the physical monitor bounds.
        SourceInitialized+=(_,_)=>FitMonitor();
        ContentRendered+=(_,_)=>FitMonitor();
        var canvas=new Canvas();Content=canvas;var label=new TextBlock{Text="拖动选择录制区域　·　Esc 取消",Padding=new Thickness(10,5,10,5),FontSize=16};label.SetResourceReference(TextBlock.ForegroundProperty,"ink");label.SetResourceReference(TextBlock.BackgroundProperty,"card");label.SetResourceReference(TextBlock.FontFamilyProperty,"UiFont");Canvas.SetTop(label,18);Canvas.SetLeft(label,24);canvas.Children.Add(label);
        var box=new System.Windows.Shapes.Rectangle{StrokeThickness=3};box.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"accent");canvas.Children.Add(box);
        Point Pixel(MouseEventArgs e){
            var p=canvas.PointToScreen(e.GetPosition(canvas));
            return new Point(Math.Clamp(Math.Round(p.X)-x,0,w),Math.Clamp(Math.Round(p.Y)-y,0,h));
        }
        // H.264 output uses even dimensions. Snap the moving edge in the preview
        // as well as the capture, so releasing the mouse never changes the region.
        Point End(Point start,MouseEventArgs e){
            var p=Pixel(e);
            return new Point(start.X+2*Math.Truncate((p.X-start.X)/2),start.Y+2*Math.Truncate((p.Y-start.Y)/2));
        }
        Point? from=null;
        MouseLeftButtonDown+=(_,e)=>{from=Pixel(e);CaptureMouse();};
        MouseMove+=(_,e)=>{if(from is not Point start)return;var end=End(start,e);
            var a=canvas.PointFromScreen(new Point(x+start.X,y+start.Y));var b=canvas.PointFromScreen(new Point(x+end.X,y+end.Y));
            Canvas.SetLeft(box,Math.Min(a.X,b.X));Canvas.SetTop(box,Math.Min(a.Y,b.Y));box.Width=Math.Abs(a.X-b.X);box.Height=Math.Abs(a.Y-b.Y);};
        MouseLeftButtonUp+=(_,e)=>{if(from is not Point a)return;var b=End(a,e);from=null;ReleaseMouseCapture();
            var width=Math.Abs(b.X-a.X);var height=Math.Abs(b.Y-a.Y);
            if(width<4||height<4)return;Crop=new ScreenRect(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),width,height);DialogResult=true;};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape)DialogResult=false;};
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr insert,int x,int y,int w,int h,uint flags);
}
