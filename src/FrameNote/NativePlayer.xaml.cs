using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace FrameNote;
public partial class NativePlayer : UserControl, IDisposable
{
    readonly LibVLC engine;
    readonly MediaPlayer player;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(180)};
    bool syncing, disposed;
    long startMs;int volume=100;
    public void SetVolume(int value){volume=value;player.Volume=value;}
    public event Action? OpenRequested, ReviewRequested, CancelRequested;
    public long TimeMs => Math.Max(0,player.Time);
    public NativePlayer(){
        InitializeComponent();
        Core.Initialize(Path.Combine(AppContext.BaseDirectory,"libvlc","win-x64"));
        engine=new LibVLC("--no-video-title-show");
        player=new MediaPlayer(engine){EnableMouseInput=false,EnableKeyInput=false};
        Surface.MediaPlayer=player;
        player.Playing+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(!disposed){player.Volume=volume;if(startMs>0){player.Time=startMs;startMs=0;}if(!ReviewButton.IsEnabled)player.SetPause(true);}});
        timer.Tick+=(_,_)=>{
            syncing=true;
            Timeline.Maximum=Math.Max(1,player.Length);
            if(!Timeline.IsMouseCaptureWithin)Timeline.Value=TimeMs;
            syncing=false;
            Toggle.Content=player.State==VLCState.Ended?"重播":player.IsPlaying?"暂停":"播放";
            static string F(long ms)=>TimeSpan.FromMilliseconds(Math.Max(0,ms)).ToString(@"hh\:mm\:ss");
            Clock.Text=F(TimeMs)+" / "+F(player.Length);
            if(player.State==VLCState.Error)Hint.Text="VLC 无法播放此文件；可尝试进入批注。";
        };
        Timeline.ValueChanged+=(_,_)=>{if(!syncing && player.IsSeekable)player.Time=(long)Timeline.Value;};
        Toggle.Click+=(_,_)=>TogglePlay();
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Space && e.OriginalSource is not Button){TogglePlay();e.Handled=true;}};
        OpenButton.Click+=(_,_)=>OpenRequested?.Invoke();
        ReviewButton.Click+=(_,_)=>ReviewRequested?.Invoke();
        CancelButton.Click+=(_,_)=>CancelRequested?.Invoke();
    }
    void TogglePlay(){
        if(player.State==VLCState.Ended)player.Stop();
        if(player.IsPlaying)player.SetPause(true);else player.Play();
    }
    public void Start(string path,string name,long ms){
        player.Stop();startMs=ms;Caption.Text=name+"   ·   VLC";Hint.Text="进入批注可逐帧定位、绘图或截图。";
        using var input=new Media(engine,path,FromType.FromPath);
        if(!player.Play(input))throw new IOException("VLC 未能打开视频。");
        timer.Start();Focus();
    }
    public void Pause()=>player.SetPause(true);
    public void Preparing(bool preparing,string message){
        OpenButton.IsEnabled=ReviewButton.IsEnabled=Toggle.IsEnabled=Timeline.IsEnabled=!preparing;
        CancelButton.Visibility=preparing?Visibility.Visible:Visibility.Collapsed;Hint.Text=message;
    }
    public void Stop(){timer.Stop();player.Stop();}
    public void Dispose(){disposed=true;Stop();Surface.MediaPlayer=null;player.Dispose();engine.Dispose();}
}
