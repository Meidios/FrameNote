using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;

namespace FrameNote;
public partial class NativePlayer : UserControl, IDisposable
{
    readonly LibVLC engine;
    readonly MediaPlayer player;
    readonly string scratch;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    bool syncing, disposed, fullscreen, pauseOnStart, stepping, capturing;
    long startMs;
    int volume=100, audibleVolume=100;
    string source="";
    Review? review;
    Probe.Info info=null!;
    CancellationTokenSource? session;
    long[]? frames;
    int? stepped;
    byte[]? stillPng;
    int seekRevision;
    public event Action? OpenRequested, ReviewRequested, CancelRequested;
    public event Action<int>? VolumeRequested;
    public event Action<bool>? FullscreenRequested;
    public long TimeUs => stepped is int i && frames!=null ? frames[i] : Math.Max(0,player.Time)*1000;
    long TimeMs => (TimeUs+500)/1000;

    public NativePlayer(string scratch){
        this.scratch=scratch;InitializeComponent();
        Core.Initialize(Path.Combine(AppContext.BaseDirectory,"libvlc","win-x64"));
        engine=new LibVLC("--no-video-title-show","--no-snapshot-preview");
        player=new MediaPlayer(engine){EnableMouseInput=false,EnableKeyInput=false};
        Surface.MediaPlayer=player;
        player.Playing+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(!disposed && session!=null){
            player.Volume=volume;
            if(startMs>0){player.Time=startMs;startMs=0;}
            if(pauseOnStart || !ReviewButton.IsEnabled){player.SetPause(true);pauseOnStart=false;}
        }});
        timer.Tick+=(_,_)=>RefreshControls();
        Timeline.ValueChanged+=(_,_)=>{if(!syncing && player.IsSeekable){ClearStill();player.Time=(long)Timeline.Value;}};
        Toggle.Click+=(_,_)=>TogglePlay();
        Previous.Click+=async (_,_)=>await Step(-1);
        Next.Click+=async (_,_)=>await Step(1);
        CopyFrame.Click+=async (_,_)=>await Snapshot(false);
        SaveFrame.Click+=async (_,_)=>await Snapshot(true);
        VolumeSlider.ValueChanged+=(_,_)=>{if((int)VolumeSlider.Value!=volume)VolumeRequested?.Invoke((int)VolumeSlider.Value);};
        Mute.Click+=(_,_)=>VolumeRequested?.Invoke(volume==0?audibleVolume:0);
        Full.Click+=(_,_)=>FullscreenRequested?.Invoke(!fullscreen);
        PreviewKeyDown+=async (_,e)=>{
            if(e.Key==Key.Escape && fullscreen){e.Handled=true;FullscreenRequested?.Invoke(false);}
            else if(Shuttle.IsEnabled && e.Key==Key.Space && e.OriginalSource is not Button){e.Handled=true;TogglePlay();}
            else if(Shuttle.IsEnabled && (e.Key is Key.Left or Key.Right) && !VolumeSlider.IsKeyboardFocusWithin && !Timeline.IsKeyboardFocusWithin){
                e.Handled=true;await Step(e.Key==Key.Left?-1:1);
            }
        };
        OpenButton.Click+=(_,_)=>OpenRequested?.Invoke();
        ReviewButton.Click+=(_,_)=>ReviewRequested?.Invoke();
        CancelButton.Click+=(_,_)=>CancelRequested?.Invoke();
    }
    void Icon(Button button,string key,string label){
        var asset=FindResource(key);
        if(!ReferenceEquals(button.Content,asset))button.Content=asset;
        if(!Equals(button.ToolTip,label))button.ToolTip=label;
    }
    public void SetVolume(int value){
        volume=value;if(value>0)audibleVolume=value;
        VolumeSlider.Value=value;player.Volume=value;
        Icon(Mute,value==0?"speaker_mute":"speaker_2",value==0?"取消静音":"播放静音");
    }
    public void SetFullscreen(bool on){
        fullscreen=on;Header.Visibility=on?Visibility.Collapsed:Visibility.Visible;
        Icon(Full,on?"full_screen_minimize":"full_screen_maximize",on?"退出全屏（Esc）":"全屏预览");
        Full.Focus();
    }
    static string Stamp(long ms){
        var t=TimeSpan.FromMilliseconds(Math.Max(0,ms));
        return t.TotalHours>=1?t.ToString(@"hh\:mm\:ss\.fff"):t.ToString(@"mm\:ss\.fff");
    }
    void RefreshControls(){
        syncing=true;
        Timeline.Maximum=Math.Max(1,player.Length);
        if(!Timeline.IsMouseCaptureWithin)Timeline.Value=TimeMs;
        syncing=false;
        Icon(Toggle,player.IsPlaying?"pause":"play",player.State==VLCState.Ended?"重播":player.IsPlaying?"暂停（空格）":"播放（空格）");
        Clock.Text=Stamp(TimeMs)+" / "+Stamp(player.Length);
        if(player.State==VLCState.Error)Hint.Text="无法播放此文件；可尝试进入批注。";
    }
    void TogglePlay(){
        if(stepping)return;
        if(stepped!=null){long ms=TimeMs;ClearStill();player.Time=ms;}
        if(player.State==VLCState.Ended)player.Stop();
        if(player.IsPlaying)player.SetPause(true);else player.Play();
    }
    void ClearStill(){
        seekRevision++;stepped=null;stillPng=null;Still.Source=null;
        Still.Visibility=Visibility.Collapsed;Surface.Visibility=Visibility.Visible;
    }
    async Task Step(int direction){
        if(stepping || session==null || !Shuttle.IsEnabled)return;
        var token=session.Token;
        bool ended=player.State==VLCState.Ended;
        if(!ended && !player.IsSeekable)return;
        long us=ended?info.Duration:TimeMs*1000;
        int revision=seekRevision;
        var file=Path.Combine(scratch,Guid.NewGuid().ToString("N")+".png");
        player.SetPause(true);stepping=true;
        try {
            var pts=frames??await Probe.Frames(source,info,token);
            token.ThrowIfCancellationRequested();
            frames=pts;
            var i=Array.BinarySearch(pts,us);
            var current=stepped??(i>=0?i:Math.Max(0,~i-1));
            var target=Math.Clamp(current+direction,0,pts.Length-1);
            // VLC 3 cannot step backwards and a paused seek need not present the target frame.
            // Decode the indexed PTS once; display and export these same pixels while stopped here.
            Directory.CreateDirectory(scratch);
            await Probe.DecodeFrame(source,pts[target],file,token);
            var png=await File.ReadAllBytesAsync(file,token);
            if(token.IsCancellationRequested || revision!=seekRevision)return;
            using var stream=new MemoryStream(png);
            var image=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            image.Freeze();stillPng=png;Still.Source=image;
            Surface.Visibility=Visibility.Hidden;Still.Visibility=Visibility.Visible;
            long ms=(pts[target]+999)/1000;
            if(ended){player.Stop();startMs=ms;pauseOnStart=true;player.Play();}
            else player.Time=ms;
            stepped=target;RefreshControls();
        }catch(OperationCanceledException){}
        catch(Exception e){if(!token.IsCancellationRequested)Hint.Text="无法逐帧定位："+e.Message;}
        finally{File.Delete(file);if(!token.IsCancellationRequested)stepping=false;}
    }
    async Task Snapshot(bool save){
        if(capturing || session==null)return;
        var token=session.Token;
        var file=Path.Combine(scratch,Guid.NewGuid().ToString("N")+".png");
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Taken(object? sender,MediaPlayerSnapshotTakenEventArgs e){if(string.Equals(e.Filename,file,StringComparison.OrdinalIgnoreCase))ready.TrySetResult();}
        capturing=true;Hint.Text="";player.SnapshotTaken+=Taken;
        try{
            var name=Caption.Text;long us=TimeUs;var still=stillPng;
            Note[] notes=[];
            Directory.CreateDirectory(scratch);
            if(review is {Notes.Count:>0} document){
                // Resolve both the pixels and the notes at the button press, even if VLC keeps playing.
                var pts=frames??await Probe.Frames(source,info,token);token.ThrowIfCancellationRequested();
                frames=pts;
                int FrameAt(long t){int i=Array.BinarySearch(pts,t+2);return i>=0?i:Math.Max(0,~i-1);}
                int i=FrameAt(us);us=pts[i];
                notes=document.Notes.Where(n=>n.EndUs is long end?us>=n.StartUs-2 && us<end:FrameAt(n.StartUs)==i).ToArray();
                if(still==null)await Probe.DecodeFrame(source,us,file,token);
            }
            else if(still==null){
                if(!player.TakeSnapshot(0,file,0,0))throw new IOException("当前没有可捕获的画面。");
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(5),token);
            }
            if(still!=null)await File.WriteAllBytesAsync(file,still,token);
            token.ThrowIfCancellationRequested();
            Hint.Text=await ((Desk)Window.GetWindow(this)).DeliverVideoFrame(file,save,name,TimeSpan.FromTicks(us*10),notes,token);
        }catch(OperationCanceledException){}
        catch(Exception e){if(!token.IsCancellationRequested)Hint.Text="未能导出当前帧："+e.Message;}
        finally{player.SnapshotTaken-=Taken;if(!token.IsCancellationRequested)capturing=false;File.Delete(file);}
    }
    public void Start(string path,Review document,long ms,Probe.Info mediaInfo){
        Stop();session=new();source=path;info=mediaInfo;review=document;startMs=ms;
        Caption.Text=document.FileName;Hint.Text="";
        using var input=new Media(engine,path,FromType.FromPath);
        if(!player.Play(input))throw new IOException("VLC 未能打开视频。");
        timer.Start();Focus();
    }
    public void Pause()=>player.SetPause(true);
    public void Preparing(bool preparing,string message){
        OpenButton.IsEnabled=ReviewButton.IsEnabled=Shuttle.IsEnabled=Output.IsEnabled=Timeline.IsEnabled=!preparing;
        CancelButton.Visibility=preparing?Visibility.Visible:Visibility.Collapsed;Hint.Text=message;
    }
    public void Stop(){
        timer.Stop();session?.Cancel();session?.Dispose();session=null;
        ClearStill();frames=null;stepping=capturing=pauseOnStart=false;player.Stop();
    }
    public void Dispose(){disposed=true;Stop();Surface.MediaPlayer=null;player.Dispose();engine.Dispose();}
}
