using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using TranslateTransform = System.Windows.Media.TranslateTransform;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;

namespace FrameNote;
public partial class NativePlayer : UserControl, IDisposable
{
    readonly LibVLC engine;
    readonly MediaPlayer player;
    readonly string scratch;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    readonly DispatcherTimer volumeHoverDelay=new(){Interval=TimeSpan.FromMilliseconds(15)};
    readonly DispatcherTimer volumePercentDelay=new(){Interval=TimeSpan.FromMilliseconds(900)};
    bool volumeExpanded;
    int volumeWheelRemainder;
    bool syncing, disposed, fullscreen, pauseOnStart, stepping, capturing;
    long startMs;
    int volume=100, audibleVolume=100;
    static readonly float[] speeds=[.25f,.5f,.75f,1f,1.25f,1.5f,2f];
    readonly Dictionary<int,Shortcut> heldSpeedKeys=[];
    string source="";
    Review? review;
    Probe.Info info=null!;
    CancellationTokenSource? session;
    long[]? frames;
    int? stepped;
    byte[]? stillPng;
    int seekRevision;
    Rect videoViewport=new(0,0,1,1);
    public event Action? OpenRequested, ReviewRequested, CancelRequested, PlaybackEnded;
    public event Action<int>? VolumeRequested;
    public event Action<bool>? FullscreenRequested;
    public long TimeUs => stepped is int i && frames!=null ? frames[i] : Math.Max(0,player.Time)*1000;
    long TimeMs => (TimeUs+500)/1000;

    internal static LibVLC CreateEngine(bool rebuildCache=false){
        Core.Initialize(Path.Combine(AppContext.BaseDirectory,"libvlc","win-x64"));
        return rebuildCache?new LibVLC("--reset-plugins-cache"):new LibVLC("--no-video-title-show","--no-snapshot-preview");
    }
    internal static Task<LibVLC>? WarmForVideo(string[] paths)=>
        paths.Length>0 && Review.Formats.Contains(Path.GetExtension(paths[0]).ToLowerInvariant())
            ?Task.Run(()=>CreateEngine()):null;
    public NativePlayer(string scratch,Task<LibVLC>? warming=null){
        this.scratch=scratch;InitializeComponent();
        ButtonHint.CustomPopupPlacementCallback=(tip,button,offset)=>[
            new(new Point((button.Width-tip.Width)/2,-tip.Height-6),PopupPrimaryAxis.Horizontal),
            new(new Point((button.Width-tip.Width)/2,button.Height+6),PopupPrimaryAxis.Horizontal)
        ];
        BindToolbarHints(Previous,Toggle,Next,CopyFrame,SaveFrame,Full,PlaylistButton);
        BindVolumePopup();
        Window? host=null;
        void HideHint(object? sender,EventArgs e){ButtonHint.IsOpen=false;CloseVolumePopup();heldSpeedKeys.Clear();}
        Loaded+=(_,_)=>{host=Window.GetWindow(this);if(host!=null)host.Deactivated+=HideHint;};
        Unloaded+=(_,_)=>{if(host!=null)host.Deactivated-=HideHint;ButtonHint.IsOpen=false;CloseVolumePopup();};
        engine=warming?.GetAwaiter().GetResult()??CreateEngine();
        player=new MediaPlayer(engine){EnableMouseInput=false,EnableKeyInput=false};
        player.EncounteredError+=(_,_)=>Serilog.Log.Error("VLC playback failed");
        player.EndReached+=(_,_)=>Dispatcher.BeginInvoke(()=>{
            if(!disposed && session!=null && IsVisible && player.State==VLCState.Ended)PlaybackEnded?.Invoke();
        });
        player.Playing+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(!disposed && session!=null){
            player.Volume=volume;
            if(startMs>0){player.Time=startMs;startMs=0;}
            if(pauseOnStart || CancelButton.IsVisible){player.SetPause(true);pauseOnStart=false;}
        }});
        timer.Tick+=(_,_)=>RefreshControls();
        Timeline.ValueChanged+=(_,_)=>{if(!syncing && player.IsSeekable){ClearStill();player.Time=(long)Timeline.Value;}};
        Toggle.Click+=(_,_)=>TogglePlay();
        Previous.Click+=async (_,_)=>await Step(-1);
        Next.Click+=async (_,_)=>await Step(1);
        CopyFrame.Click+=async (_,_)=>await Snapshot(false);
        SaveFrame.Click+=async (_,_)=>await Snapshot(true);
        VolumeSlider.ValueChanged+=(_,_)=>RequestVolume((int)VolumeSlider.Value);
        PreviewMouseWheel+=ZoomVideo;
        VideoArea.SizeChanged+=(_,_)=>PositionStill();
        Mute.Click+=(_,_)=>RequestVolume(volume==0?audibleVolume:0);
        Full.Click+=(_,_)=>FullscreenRequested?.Invoke(!fullscreen);
        LostKeyboardFocus+=(_,_)=>heldSpeedKeys.Clear();
        PreviewKeyUp+=(_,e)=>{
            if(heldSpeedKeys.Remove(Shortcut.From(e).Vk,out var press)){
                e.Handled=true;
                if(session!=null && Shuttle.IsEnabled)ApplySpeedKey(press);
            }
        };
        PreviewKeyDown+=async (_,e)=>{
            if(Keyboard.Modifiers==ModifierKeys.None && e.Key is (Key.Enter or Key.Space) && Keyboard.FocusedElement is ButtonBase)return;
            var key=Shortcut.From(e);
            if(bindings["cancel"].Contains(key) && fullscreen){e.Handled=true;FullscreenRequested?.Invoke(false);}
            else if(bindings["open"].Contains(key) && OpenButton.IsEnabled){e.Handled=true;OpenRequested?.Invoke();}
            else if(session!=null && Output.IsEnabled && bindings["copyFrame"].Contains(key)){e.Handled=true;if(!e.IsRepeat)await Snapshot(false);}
            else if(session!=null && Output.IsEnabled && bindings["fullPreview"].Contains(key)){
                e.Handled=true;if(!e.IsRepeat)FullscreenRequested?.Invoke(!fullscreen);
            }
            else if(session!=null && Shuttle.IsEnabled && HandleSpeedKey(key,e.IsRepeat))e.Handled=true;
            else if(Shuttle.IsEnabled && bindings["play"].Contains(key)){e.Handled=true;TogglePlay();}
            else if(Shuttle.IsEnabled && (bindings["prev"].Contains(key) || bindings["next"].Contains(key)) && !VolumeSlider.IsKeyboardFocusWithin && !Timeline.IsKeyboardFocusWithin){
                e.Handled=true;await Step(bindings["prev"].Contains(key)?-1:1);
            }
        };
        OpenButton.Click+=(_,_)=>OpenRequested?.Invoke();
        ReviewButton.Click+=(_,_)=>ReviewRequested?.Invoke();
        CancelButton.Click+=(_,_)=>CancelRequested?.Invoke();
    }
    ShortcutMap bindings=Shortcuts.Defaults();
    bool HandleSpeedKey(Shortcut key,bool repeat){
        bool pending=false;
        foreach(var pair in bindings["rateReset"]){
            if(!pair.Includes(key))continue;
            if(repeat)return true;
            int other=pair.Vk==key.Vk?pair.With:pair.Vk;
            if(heldSpeedKeys.TryGetValue(other,out var first) && first.Mods==key.Mods){
                heldSpeedKeys.Remove(other);player.SetRate(1);RefreshControls();return true;
            }
            pending=true;
        }
        if(pending){heldSpeedKeys[key.Vk]=key;return true;}
        return ApplySpeedKey(key,repeat);
    }
    bool ApplySpeedKey(Shortcut key,bool repeat=false){
        int index=Array.IndexOf(speeds,player.Rate);
        if(bindings["rateReset"].Contains(key))index=Array.IndexOf(speeds,1f);
        else if(bindings["rateDown"].Contains(key))index--;
        else if(bindings["rateUp"].Contains(key))index++;
        else return false;
        if(!repeat){player.SetRate(speeds[Math.Clamp(index,0,speeds.Length-1)]);RefreshControls();}
        return true;
    }
    void ZoomVideo(object sender,MouseWheelEventArgs e){
        if(VolumePopupHost.IsMouseOver || session==null || !VideoArea.IsVisible || !bindings["zoomVideo"].Contains(new Shortcut((int)Keyboard.Modifiers,Shortcut.Wheel)))return;
        var p=e.GetPosition(VideoArea);
        if(!new Rect(VideoArea.RenderSize).Contains(p) || VideoArea.ActualWidth<=0 || VideoArea.ActualHeight<=0)return;
        e.Handled=true;
        double next=Math.Clamp(videoViewport.Width*Math.Pow(2,e.Delta/480d),.25,4);
        if(next==videoViewport.Width)return;
        double ratio=next/videoViewport.Width;
        double Anchor(double cursor,double offset)=>Math.Clamp(cursor+(offset-cursor)*ratio,Math.Min(0,1-next),Math.Max(0,1-next));
        SetVideoViewport(new Rect(Anchor(p.X/VideoArea.ActualWidth,videoViewport.X),Anchor(p.Y/VideoArea.ActualHeight,videoViewport.Y),next,next));
    }
    void SetVideoViewport(Rect bounds){
        videoViewport=bounds;Surface.SetViewport(bounds);PositionStill();
    }
    void PositionStill(){
        StillViewport.RenderTransform=new System.Windows.Media.MatrixTransform(videoViewport.Width,0,0,videoViewport.Height,
            videoViewport.X*VideoArea.ActualWidth,videoViewport.Y*VideoArea.ActualHeight);
    }
    void BindVolumePopup(){
        volumePercentDelay.Tick+=(_,_)=>HideVolumePercent();
        void Schedule(object sender,RoutedEventArgs e){
            volumeHoverDelay.Stop();
            volumeHoverDelay.Start();
        }
        foreach(var element in new FrameworkElement[]{Mute,VolumePopupHost}){
            element.MouseEnter+=Schedule;element.MouseLeave+=Schedule;
            element.GotKeyboardFocus+=Schedule;element.LostKeyboardFocus+=Schedule;
            element.PreviewMouseWheel+=AdjustVolumeByWheel;
        }
        VolumeSlider.LostMouseCapture+=Schedule;
        Mute.IsEnabledChanged+=(_,_)=>{if(!Mute.IsEnabled)CloseVolumePopup();};
        Mute.IsVisibleChanged+=(_,_)=>{if(!Mute.IsVisible)CloseVolumePopup();};
        volumeHoverDelay.Tick+=(_,_)=>{
            volumeHoverDelay.Stop();
            bool keyboardFocus=InputManager.Current.MostRecentInputDevice is KeyboardDevice && (Mute.IsKeyboardFocused||VolumeSlider.IsKeyboardFocusWithin);
            bool open=Mute.IsEnabled && Mute.IsVisible &&
                (Mute.IsMouseOver||VolumePopupHost.IsMouseOver||VolumeSlider.IsMouseCaptureWithin||keyboardFocus);
            if(open!=volumeExpanded)AnimateVolumePopup(open);
        };
        Mute.PreviewKeyDown+=(_,e)=>{
            if(e.Key==Key.Tab && Keyboard.Modifiers==ModifierKeys.None && VolumePopup.IsOpen){
                e.Handled=true;VolumeSlider.Focus();
            }
        };
        VolumeSlider.PreviewKeyDown+=(_,e)=>{
            if(e.Key!=Key.Tab)return;
            e.Handled=true;(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?Mute:Full).Focus();
        };
    }
    void AdjustVolumeByWheel(object sender,MouseWheelEventArgs e){
        if(!Mute.IsEnabled)return;
        e.Handled=true;
        volumeWheelRemainder+=e.Delta*5;
        int step=volumeWheelRemainder/Mouse.MouseWheelDeltaForOneLine;
        volumeWheelRemainder%=Mouse.MouseWheelDeltaForOneLine;
        int next=Math.Clamp(volume+step,0,100);
        RequestVolume(next);
    }
    void RequestVolume(int value){
        if(value==volume)return;
        VolumeRequested?.Invoke(value);
        VolumePercent.Text=$"{value}%";VolumePercentBadge.Visibility=Visibility.Visible;
        volumePercentDelay.Stop();volumePercentDelay.Start();
    }
    void HideVolumePercent(){
        volumePercentDelay.Stop();VolumePercentBadge.Visibility=Visibility.Hidden;
    }
    void AnimateVolumePopup(bool open){
        volumeExpanded=open;
        if(open)VolumePopup.IsOpen=true;
        else HideVolumePercent();
        var duration=TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?130:0);
        var fade=new DoubleAnimation(VolumeCard.Opacity,open?1:0,duration);
        var slide=new DoubleAnimation(VolumeOffset.Y,open?0:8,duration){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}};
        if(!open)fade.Completed+=(_,_)=>{if(!volumeExpanded)VolumePopup.IsOpen=false;};
        VolumeCard.BeginAnimation(OpacityProperty,fade);VolumeOffset.BeginAnimation(TranslateTransform.YProperty,slide);
    }
    void CloseVolumePopup(){
        HideVolumePercent();
        volumeHoverDelay.Stop();volumeExpanded=false;VolumePopup.IsOpen=false;
        VolumeCard.BeginAnimation(OpacityProperty,null);VolumeOffset.BeginAnimation(TranslateTransform.YProperty,null);
    }
    void BindToolbarHints(params ButtonBase[] buttons){
        void Show(ButtonBase button){
            if(!button.IsEnabled||!button.IsVisible)return;
            ButtonHint.PlacementTarget=button;
            ButtonHint.IsOpen=true;
        }
        void Hide(ButtonBase button){
            if(ButtonHint.PlacementTarget==button)ButtonHint.IsOpen=false;
        }
        foreach(var button in buttons){
            button.MouseEnter+=(_,_)=>Show(button);
            button.MouseLeave+=(_,_)=>Hide(button);
            button.GotKeyboardFocus+=(_,_)=>{if(InputManager.Current.MostRecentInputDevice is KeyboardDevice)Show(button);};
            button.LostKeyboardFocus+=(_,_)=>Hide(button);
            button.PreviewMouseDown+=(_,_)=>Hide(button);
            button.IsVisibleChanged+=(_,_)=>{if(!button.IsVisible)Hide(button);};
            button.IsEnabledChanged+=(_,_)=>{if(!button.IsEnabled)Hide(button);};
            button.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Hide(button);};
        }
    }
    string ShortcutHint(string label,string id)=>L.WithShortcut(label,bindings[id].Label(InputKind.Keyboard));
    internal void SetShortcuts(ShortcutMap map){
        heldSpeedKeys.Clear();bindings=map;Previous.ToolTip=ShortcutHint(L.Text("上一帧"),"prev");Next.ToolTip=ShortcutHint(L.Text("下一帧"),"next");
        OpenButton.ToolTip=ShortcutHint(L.Text("打开文件"),"open");Toggle.ToolTip=ShortcutHint(player.IsPlaying?L.Text("暂停"):L.Text("播放"),"play");
        Full.ToolTip=ShortcutHint(fullscreen?L.Text("退出全屏"):L.Text("全屏预览"),"fullPreview");
        CopyFrame.ToolTip=ShortcutHint(L.Text("复制当前帧到剪贴板"),"copyFrame");
    }
    void Icon(Button button,string key,string label){
        var asset=FindResource(key);
        if(!ReferenceEquals(button.Content,asset))button.Content=asset;
        if(!Equals(button.ToolTip,label))button.ToolTip=label;
    }
    public void SetVolume(int value){
        volume=value;if(value>0)audibleVolume=value;
        VolumeSlider.Value=value;player.Volume=value;
        Icon(Mute,value==0?"speaker_mute":"speaker_2",value==0?L.Text("取消静音"):L.Text("播放静音"));
    }
    public void SetFullscreen(bool on){
        fullscreen=on;Header.Visibility=on?Visibility.Collapsed:Visibility.Visible;
        Icon(Full,on?"full_screen_minimize":"full_screen_maximize",ShortcutHint(on?L.Text("退出全屏"):L.Text("全屏预览"),"fullPreview"));
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
        Icon(Toggle,player.IsPlaying?"pause":"play",ShortcutHint(player.State==VLCState.Ended?L.Text("重播"):player.IsPlaying?L.Text("暂停"):L.Text("播放"),"play"));
        float rate=player.Rate;
        Clock.Text=Stamp(TimeMs)+" / "+Stamp(player.Length)+(rate>0 && rate!=1?$"  ·  {rate:0.##}×":"");
        if(player.State==VLCState.Error)Hint.Text=L.Text("无法播放此文件；可尝试进入批注。");
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
        if(stepping || review==null || session==null || !Shuttle.IsEnabled)return;
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
        catch(Exception e){if(!token.IsCancellationRequested){Serilog.Log.Error(e,"Player frame seek failed");Hint.Text=L.Text("无法逐帧定位：")+e.Message;}}
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
                if(!player.TakeSnapshot(0,file,0,0))throw new IOException(L.Text("当前没有可捕获的画面。"));
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(5),token);
            }
            if(still!=null)await File.WriteAllBytesAsync(file,still,token);
            token.ThrowIfCancellationRequested();
            Hint.Text=await ((Desk)Window.GetWindow(this)).DeliverVideoFrame(file,save,name,TimeSpan.FromTicks(us*10),notes,token);
        }catch(OperationCanceledException){}
        catch(Exception e){if(!token.IsCancellationRequested){Serilog.Log.Error(e,"Player snapshot failed");Hint.Text=L.Text("未能导出当前帧：")+e.Message;}}
        finally{player.SnapshotTaken-=Taken;if(!token.IsCancellationRequested)capturing=false;File.Delete(file);}
    }
    public void BindReview(Review document,Probe.Info mediaInfo){review=document;info=mediaInfo;Previous.IsEnabled=Next.IsEnabled=ReviewButton.IsEnabled=true;}
    public async Task Start(string path,string name,long ms){
        Stop();session=new();source=path;review=null;startMs=ms;Previous.IsEnabled=Next.IsEnabled=ReviewButton.IsEnabled=false;
        Caption.Text=name;Hint.Text="";
        var token=session.Token;
        // Wait for WPF to create the embedded child before handing video to VLC.
        // Starting VLC with a zero HWND lets it create its own Direct3D output window.
        if(Surface.VideoHandle==IntPtr.Zero){
            Surface.UpdateLayout();
            if(Surface.VideoHandle==IntPtr.Zero)await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Loaded,token);
        }
        token.ThrowIfCancellationRequested();
        if(Surface.VideoHandle==IntPtr.Zero)throw new IOException(L.Text("视频画面未能连接到播放器窗口。"));
        player.Hwnd=Surface.VideoHandle;
        if(player.Rate!=1)player.SetRate(1);
        using var input=new Media(engine,path,FromType.FromPath);
        // Local fixed disks can start with a shorter buffer. Keep read-ahead for network and removable media.
        var root=Path.GetPathRoot(path);
        bool local=root is {Length:3} && new DriveInfo(root).DriveType==DriveType.Fixed;
        input.AddOption(local?":file-caching=60":":file-caching=150");
        if(!player.Play(input))throw new IOException(L.Text("VLC 未能打开视频。"));
        timer.Start();Focus();
    }
    public void Pause()=>player.SetPause(true);
    public void Preparing(bool preparing,string message,bool showProgress=false){
        OpenButton.IsEnabled=ReviewButton.IsEnabled=Shuttle.IsEnabled=Output.IsEnabled=Timeline.IsEnabled=!preparing;
        // The VLC child HWND would otherwise cover the WPF loading panel.
        VideoArea.Visibility=preparing?Visibility.Hidden:Visibility.Visible;
        LoadingOverlay.Visibility=preparing?Visibility.Visible:Visibility.Collapsed;
        Hint.Text=preparing?"":message;
        if(preparing){
            LoadingTitle.Text=message;
            PreviewBar.Visibility=PreviewPercent.Visibility=showProgress?Visibility.Visible:Visibility.Collapsed;
            SetPreviewProgress(0);
            CancelButton.Focus();
        }
    }
    public void SetPreviewProgress(int percent){
        PreviewBar.Value=percent;PreviewPercent.Text=$"{percent}%";
    }
    public void Stop(){
        heldSpeedKeys.Clear();timer.Stop();session?.Cancel();session?.Dispose();session=null;
        ClearStill();SetVideoViewport(new Rect(0,0,1,1));frames=null;stepping=capturing=pauseOnStart=false;player.Stop();
    }
    public void Dispose(){disposed=true;Stop();player.Hwnd=IntPtr.Zero;player.Dispose();engine.Dispose();}
}
