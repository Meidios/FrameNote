using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;

namespace FrameNote;

// Both renderers consume this palette. Native chrome remains owned by Windows.
internal static class DeskTheme
{
    internal static FontFamily UiFont { get; }=new(
        new Uri(Path.Combine(AppContext.BaseDirectory,"ui","fonts")+Path.DirectorySeparatorChar),
        "./#Inter, ./NotoSansSC.ttf#Noto Sans SC");
    internal static event Action? Changed;
    static readonly Dictionary<string,string> colors = new();
    static readonly Color Accent = (Color)ColorConverter.ConvertFromString("#A8DDFD");
    internal static bool Dark { get; private set; }
    static readonly (string Key,string Dark,string Light)[] surfaces = [
        ("bg","#141416","#E5E9EE"), ("chrome","#1B1B1D","#EDF0F4"), ("panel","#202022","#F4F6F9"),
        ("card","#252628","#F8FAFC"), ("canvas","#18181A","#EEF2F6"), ("hover","#2A2B2E","#EBEFF4"),
        ("pressed","#18181A","#E2E7ED"), ("edge","#34373C","#AEB7C2"), ("border","#44484E","#929EAB"),
        ("ink","#ECEEF1","#1C232A"), ("muted","#A5AAB2","#495766"), ("disabled","#6E747C","#7B8794"),
        ("danger","#E04F4F","#C23D43"), ("scroll","#4E5258","#4957664D"),
        // Preserve the capture panel's native checkbox palette in both renderers.
        ("checkFill","#3B3B3B","#FFFFFF"), ("checkEdge","#858585","#767676"),
        ("checkHoverEdge","#ACACAC","#4F4F4F"), ("checkPressedEdge","#6E6E6E","#8D8D8D"),
        ("checkGlyph","#3B3B3B","#3B3B3B"), ("checkHoverFill","#DFF2FE","#DFF2FE"),
        ("checkPressedFill","#71C8FC","#71C8FC")];

    internal static void Start(Application app)
    {
        app.Resources["UiFont"]=UiFont;
        // Some native control templates obtain their font from a system resource.
        app.Resources[SystemFonts.MessageFontFamilyKey]=UiFont;
        app.Resources[SystemFonts.MenuFontFamilyKey]=UiFont;
        app.Resources[SystemFonts.StatusFontFamilyKey]=UiFont;
        Refresh();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/FrameNote;component/Controls.xaml",UriKind.Relative)});
        SystemEvents.UserPreferenceChanged+=PreferenceChanged;
        app.Exit+=(_,_)=>SystemEvents.UserPreferenceChanged-=PreferenceChanged;
    }

    static void PreferenceChanged(object sender,UserPreferenceChangedEventArgs e)
    {
        var dispatcher=Application.Current?.Dispatcher;
        if(dispatcher is {HasShutdownStarted:false})dispatcher.BeginInvoke(Refresh);
    }

    static void Refresh()
    {
        var light=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1);
        Apply(light is int i && i==0);
    }

    static double Luma(Color c)
    {
        static double F(byte v){double x=v/255d;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}
        return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B);
    }
    static Color Mix(Color a,Color b,double t)=>Color.FromRgb((byte)Math.Round(a.R+(b.R-a.R)*t),(byte)Math.Round(a.G+(b.G-a.G)*t),(byte)Math.Round(a.B+(b.B-a.B)*t));
    static double Contrast(Color a,Color b){double x=Luma(a)+.05,y=Luma(b)+.05;return Math.Max(x,y)/Math.Min(x,y);}
    internal static bool UseWhiteInk(Color background) => Contrast(background,Colors.White)>Contrast(background,Colors.Black);
    static string Hex(Color c)=>$"#{c.R:X2}{c.G:X2}{c.B:X2}";
    internal static void Apply(bool dark)
    {
        Dark=dark;
        void Set(string key,string css){
            if(colors.TryGetValue(key,out var prev)&&prev==css)return;
            colors[key]=css;
            string wpf=css.Length==9?"#"+css[7..]+css[1..7]:css;
            var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(wpf));brush.Freeze();
            Application.Current.Resources[key]=brush;
        }
        foreach(var s in surfaces)Set(s.Key,dark?s.Dark:s.Light);
        var card=(Color)ColorConverter.ConvertFromString(colors["card"]);
        var accent=Accent;
        Set("accent",Hex(accent));Set("accentHover",Hex(Mix(accent,dark?Colors.White:Colors.Black,dark?.14:.08)));
        Set("accentPressed",Hex(Mix(accent,Colors.Black,dark?.12:.18)));
        Set("onAccent",Contrast(accent,Colors.White)>=4.5?"#FFFFFF":"#000000");
        Set("selection",Hex(accent)+(dark?"2E":"24"));
        var link=accent;
        for(int i=1;i<=255 && Contrast(link,card)<4.5;i++)link=Mix(accent,dark?Colors.White:Colors.Black,i/255d);
        Set("link",Hex(link));
        foreach(Window window in Application.Current.Windows)Caption(window);
        Changed?.Invoke();
    }

    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    const int DwmCaptionColor = 35;
    const int DwmWindowCornerPreference = 33;
    const int DwmBorderColor = 34;
    static int ColorRef(Color c)=>c.R | c.G << 8 | c.B << 16;
    static void FrameBorder(Window w){
        var handle=new WindowInteropHelper(w).Handle;
        if(handle==IntPtr.Zero || w.WindowStyle==WindowStyle.None || !OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))return;
        var chrome=((SolidColorBrush)w.FindResource("chrome")).Color;
        var edge=((SolidColorBrush)w.FindResource("edge")).Color;
        // Keep one native antialiased edge against the title bar and DWM shadow.
        int rgb=SystemParameters.HighContrast ? -1 : ColorRef(Mix(chrome,edge,w.IsActive ? .65 : .4));
        DwmSetWindowAttribute(handle,DwmBorderColor,ref rgb,sizeof(int));
    }
    internal static void SetWindowCorners(Window w, bool fullscreen){
        var handle=new WindowInteropHelper(w).Handle;
        if(handle==IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))return;
        int preference=fullscreen?1:2; // DWMWCP_DONOTROUND / DWMWCP_ROUND
        DwmSetWindowAttribute(handle,DwmWindowCornerPreference,ref preference,sizeof(int));
    }
    internal static void Caption(Window w){
        var handle=new WindowInteropHelper(w).Handle;
        if(handle==IntPtr.Zero || w.WindowStyle==WindowStyle.None)return;
        int dark=Dark?1:0;
        if(DwmSetWindowAttribute(handle,20,ref dark,4)!=0)DwmSetWindowAttribute(handle,19,ref dark,4);
        var color=((SolidColorBrush)w.FindResource("chrome")).Color;
        int rgb=ColorRef(color);
        DwmSetWindowAttribute(handle,DwmCaptionColor,ref rgb,sizeof(int));
        FrameBorder(w);
    }
    internal static void Window(Window w){
        w.UseLayoutRounding=true;
        w.Activated+=(_,_)=>FrameBorder(w);
        w.Deactivated+=(_,_)=>FrameBorder(w);
        w.SetResourceReference(System.Windows.Controls.Control.FontFamilyProperty,"UiFont");
        w.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,"ink");
        if(w.ReadLocalValue(System.Windows.Controls.Control.BackgroundProperty)==DependencyProperty.UnsetValue)
            w.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty,"bg");
        w.SourceInitialized+=(_,_)=>{
            Caption(w);
            if(w.WindowStyle!=WindowStyle.None)SetWindowCorners(w,false);
            if(System.Windows.Shell.WindowChrome.GetWindowChrome(w)!=null)
                ((HwndSource)PresentationSource.FromVisual(w)).AddHook(WindowResize.OnMessage);
        };
    }
    [DllImport("user32.dll")] static extern int GetWindowLongW(IntPtr hwnd,int index);
    [DllImport("user32.dll")] static extern int SetWindowLongW(IntPtr hwnd,int index,int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);

    internal static void Dialog(Window window){
        window.Background=Brushes.Transparent;
        window.FontSize=14;
        window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var content=(UIElement)window.Content;
        var frame=new DockPanel();
        var heading=(Grid)((DataTemplate)window.FindResource("DialogTitleBar")).LoadContent();
        DockPanel.SetDock(heading,Dock.Top);frame.Children.Add(heading);
        window.Content=frame;
        var body=new Border{Child=content};
        body.SetResourceReference(Border.BackgroundProperty,"chrome");frame.Children.Add(body);
        WindowChrome.SetWindowChrome(window,new WindowChrome{
            CaptionHeight=34,GlassFrameThickness=new Thickness(0,38,0,0),
            UseAeroCaptionButtons=true,NonClientFrameEdges=NonClientFrameEdges.Left|NonClientFrameEdges.Right|NonClientFrameEdges.Bottom
        });
        Window(window);
        window.SourceInitialized+=(_,_)=>{
            const int styleIndex=-16,minimizeBox=0x20000,maximizeBox=0x10000;
            const uint noSize=0x1,noMove=0x2,noZOrder=0x4,noActivate=0x10,frameChanged=0x20;
            var hwnd=new WindowInteropHelper(window).Handle;
            // Use the same glass-backed renderer and keep only the native Close button.
            if(HwndSource.FromHwnd(hwnd)?.CompositionTarget is {} target)target.RenderMode=RenderMode.SoftwareOnly;
            SetWindowLongW(hwnd,styleIndex,GetWindowLongW(hwnd,styleIndex)&~(minimizeBox|maximizeBox));
            SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,noSize|noMove|noZOrder|noActivate|frameChanged);
        };
    }
    internal static string WebScript(){
        var css=string.Join(";",colors.Select(k=>"--"+k.Key+":"+k.Value))+";--chevron:url(icons/"+(Dark?"chevron_dark.svg":"chevron_down.svg")+");color-scheme:"+(Dark?"dark":"light");
        return "document.documentElement.style.cssText="+JsonSerializer.Serialize(css);
    }
}

