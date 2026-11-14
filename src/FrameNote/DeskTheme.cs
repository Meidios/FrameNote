using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace FrameNote;

// Both renderers consume this palette. Native chrome remains owned by Windows.
internal static class DeskTheme
{
    internal static event Action? Changed;
    static readonly Dictionary<string,string> colors = new();
    internal static bool Dark { get; private set; }
    static readonly (string Key,string Dark,string Light)[] surfaces = [
        ("bg","#141416","#E5E9EE"), ("chrome","#1B1B1D","#EDF0F4"), ("panel","#202022","#F4F6F9"),
        ("card","#252628","#F8FAFC"), ("canvas","#18181A","#EEF2F6"), ("hover","#2A2B2E","#EBEFF4"),
        ("pressed","#18181A","#E2E7ED"), ("edge","#34373C","#AEB7C2"), ("border","#44484E","#929EAB"),
        ("ink","#ECEEF1","#1C232A"), ("muted","#A5AAB2","#495766"), ("disabled","#6E747C","#7B8794"),
        ("danger","#E04F4F","#C23D43"), ("scroll","#4E5258","#4957664D")];

    internal static void Start(Application app)
    {
        var folder=new Uri(Path.Combine(AppContext.BaseDirectory,"ui","fonts")+Path.DirectorySeparatorChar);
        app.Resources["UiFont"]=new FontFamily(folder,"./#Noto Sans SC");
        app.Resources["MonoFont"]=new FontFamily(folder,"./#Cascadia Mono");
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
        var abgr=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM","AccentColor",null);
        var raw=abgr is int n && (n & 0xffffff)!=0 ? Color.FromRgb((byte)n,(byte)(n>>8),(byte)(n>>16)) : Color.FromRgb(0,120,212);
        Apply(light is int i && i==0,raw);
    }

    static double Luma(Color c)
    {
        static double F(byte v){double x=v/255d;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}
        return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B);
    }
    static Color Mix(Color a,Color b,double t)=>Color.FromRgb((byte)Math.Round(a.R+(b.R-a.R)*t),(byte)Math.Round(a.G+(b.G-a.G)*t),(byte)Math.Round(a.B+(b.B-a.B)*t));
    static double Contrast(Color a,Color b){double x=Luma(a)+.05,y=Luma(b)+.05;return Math.Max(x,y)/Math.Min(x,y);}
    static string Hex(Color c)=>$"#{c.R:X2}{c.G:X2}{c.B:X2}";
    internal static void Apply(bool dark,Color raw)
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
        var accent=raw;
        for(int i=1;i<=255 && Contrast(accent,card)<3;i++)accent=Mix(raw,dark?Colors.White:Colors.Black,i/255d);
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
    internal static void Caption(Window w){
        var handle=new WindowInteropHelper(w).Handle;
        if(handle==IntPtr.Zero || w.WindowStyle==WindowStyle.None)return;
        int dark=Dark?1:0;
        if(DwmSetWindowAttribute(handle,20,ref dark,4)!=0)DwmSetWindowAttribute(handle,19,ref dark,4);
    }
    internal static void Window(Window w){
        w.SetResourceReference(System.Windows.Controls.Control.FontFamilyProperty,"UiFont");
        w.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,"ink");
        w.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty,"bg");
        w.SourceInitialized+=(_,_)=>Caption(w);
    }
    internal static string WebScript(){
        var css=string.Join(";",colors.Select(k=>"--"+k.Key+":"+k.Value))+";--chevron:url(icons/"+(Dark?"chevron_dark.svg":"chevron_down.svg")+");color-scheme:"+(Dark?"dark":"light");
        return "document.documentElement.style.cssText="+JsonSerializer.Serialize(css);
    }
}

