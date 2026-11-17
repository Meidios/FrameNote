using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FrameNote;
public static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        if(args is ["--prepare-vlc-cache"]){
            using var engine=NativePlayer.CreateEngine(true);
            return File.Exists(Path.Combine(AppContext.BaseDirectory,"libvlc","win-x64","plugins","plugins.dat"))?0:1;
        }
        if (args.FirstOrDefault()=="--prepare-reaper") {
            try { if(args.Length!=4 && !(args.Length==5 && args[4]=="--colors")) return 2; Packages.PrepareReaper(args[1],args[2],args[3],args.Length==5); return 0; }
            catch(Exception e) { if(args.Length>=4) File.WriteAllText(args[3]+".error",e.Message); return 1; }
        }
        var player=string.Equals(Path.GetFileName(Environment.ProcessPath),"FrameNote Player.exe",StringComparison.OrdinalIgnoreCase);
        if(player && args.FirstOrDefault()=="--shell-selection")return ShellSelection.Run(args.Skip(1).ToArray());
        bool direct=(player||args.Length>1) && args.Length>0 && Review.Formats.Contains(Path.GetExtension(args[0]).ToLowerInvariant());
        var warming=direct?Task.Run(()=>NativePlayer.CreateEngine()):null;
        var app=new Application();DeskTheme.Start(app);return app.Run(new Desk(args,player,warming));
    }
}
public sealed partial class Desk : Window
{
    readonly WebView2 web = new();
    readonly string root = Environment.GetEnvironmentVariable("FRAMENOTE_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FrameNote");
    readonly string page = new Uri(Path.Combine(AppContext.BaseDirectory,"ui","index.html")).AbsoluteUri;
    readonly string scratch;
    string ImageDirectory=>Path.Combine(scratch,"images");
    readonly TemporaryRecordings recordings=new();
    string? activeRecording;
    string? media, draft;
    Review? doc;
    bool pending,openedPackage;
    TaskCompletionSource? currentWork;
    readonly bool playerEntry;
    Task? webStartup;
    bool webReady,openingPlayer;
    public Desk(string[] paths,bool playerEntry=false,Task<LibVLCSharp.Shared.LibVLC>? engine=null) {
        startingEngine=engine;
        string? initial=paths.FirstOrDefault();
        this.playerEntry=playerEntry;
        bool direct=(playerEntry || paths.Length>1) && (initial==null || Review.Formats.Contains(Path.GetExtension(initial).ToLowerInvariant()));
        Title=direct?(initial==null?"FrameNote Player":"FrameNote Player - "+Path.GetFileName(initial)):"FrameNote · 视频审阅"; Width=1230; Height=800; MinWidth=840; MinHeight=560;
        DeskTheme.Window(this); WindowStartupLocation=WindowStartupLocation.CenterScreen;
        InitWindowSize();
        // Only the WPF host surface uses software rendering. WebView2 and VLC
        // own separate child HWNDs and keep their normal video rendering paths.
        SourceInitialized+=(_,_)=>{
            var target=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.CompositionTarget;
            if(target!=null)target.RenderMode=RenderMode.SoftwareOnly;
        };
        scratch=Path.Combine(Path.GetTempPath(),"FrameNote",Guid.NewGuid().ToString("N"));
        InitCapture();
        InitPlaybackVolume(); InitHelp(direct); AllowDrop=true;
        if(direct){
            var player=EnsurePlayer();player.Caption.Text=initial==null?"FrameNote Player":Path.GetFileName(initial);
            player.ReviewButton.IsEnabled=player.Timeline.IsEnabled=player.Shuttle.IsEnabled=player.Output.IsEnabled=false;
        }
        Drop+=async (_,e)=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] files){e.Handled=true;await OpenFiles(files,true);}};
        Loaded+=async (_,_)=>{
            try{
                openingPlayer=direct;
                if(!direct && (initial==null || !ImageFormats.Contains(Path.GetExtension(initial).ToLowerInvariant())))await EnsureReviewWeb();
                if(initial!=null && !lifetime.IsCancellationRequested)await OpenFiles(paths);
            }catch(Exception e){if(!lifetime.IsCancellationRequested)MessageBox.Show(this,e.Message,"FrameNote");}
            finally{openingPlayer=false;initialOpen.TrySetResult();}
        };
        Closing+=(_,e)=>{if(pending && !openingPlayer) {e.Cancel=true; preparing?.Cancel(); Send(new {type="status",text="请等当前操作结束后再关闭。"});} };
        Closed+=(_,_)=>{StopTimeline();native?.Dispose();web.Dispose();recordings.Dispose(); try {if(Directory.Exists(scratch))Directory.Delete(scratch,true);} catch(IOException){}};
    }
    Task EnsureReviewWeb()=>webStartup??=InitializeReviewWeb();
    async Task InitializeReviewWeb(){
        try{
            surfaces.Children.Insert(0,web);
            web.Visibility=native?.IsVisible==true?Visibility.Hidden:Visibility.Visible;
            await web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"Browser"),new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required")));
            var core=web.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("fonts.framenote.invalid",Path.Combine(AppContext.BaseDirectory,"ui","fonts"),CoreWebView2HostResourceAccessKind.Allow);
            void ApplyWebTheme(){
                core.Profile.PreferredColorScheme=DeskTheme.Dark?CoreWebView2PreferredColorScheme.Dark:CoreWebView2PreferredColorScheme.Light;
                if(core.Source==page) _=web.ExecuteScriptAsync(DeskTheme.WebScript());
            }
            ApplyWebTheme();DeskTheme.Changed+=ApplyWebTheme;Closed+=(_,_)=>DeskTheme.Changed-=ApplyWebTheme;
            core.DOMContentLoaded+=(_,_)=>ApplyWebTheme();
            core.Settings.AreDefaultContextMenusEnabled=false; core.Settings.IsStatusBarEnabled=false; core.Settings.AreHostObjectsAllowed=false;
            core.Settings.IsZoomControlEnabled=false;core.Settings.IsPinchZoomEnabled=false;web.ZoomFactor=1;
            core.Settings.AreBrowserAcceleratorKeysEnabled=false; core.Settings.AreDevToolsEnabled=false;
            InitImageExport();
            core.NavigationStarting+=(_,e)=>{if(e.Uri!=page)e.Cancel=true;};
            core.NewWindowRequested+=(_,e)=>e.Handled=true;
            web.AllowExternalDrop=true;
            core.WebMessageReceived+=async (_,e)=>{
                if(e.Source!=page)return;
                if(e.AdditionalObjects is {Count:>0} objects)
                    await OpenFiles(objects.OfType<CoreWebView2File>().Select(f=>f.Path).ToArray(),true);
                else await Message(e.WebMessageAsJson);
            };
            var loaded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Navigated(object? sender,CoreWebView2NavigationCompletedEventArgs e){
                core.NavigationCompleted-=Navigated;
                if(e.IsSuccess){webReady=true;loaded.TrySetResult();}
                else loaded.TrySetException(new IOException("无法打开审阅界面："+e.WebErrorStatus));
            }
            core.NavigationCompleted+=Navigated;core.Navigate(page);
            await loaded.Task.WaitAsync(lifetime.Token);
        }catch{surfaces.Children.Remove(web);webStartup=null;throw;}
    }
    void ReportError(string text){if(lifetime.IsCancellationRequested)return;if(native?.IsVisible==true)native.Preparing(false,text);else Send(new{type="error",text});}
    void Send(object o){if(!lifetime.IsCancellationRequested && web.CoreWebView2!=null)web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(o,Review.Json));}
    async Task Work(Func<Task> action) {
        if(pending)return; pending=true;var done=currentWork=new();Send(new{type="busy",value=true});
        try { await action(); } catch(Exception e){ReportError(e.Message);}
        finally {pending=false;currentWork=null;Send(new{type="busy",value=false});ShowPlaylist();done.SetResult();}
    }
    async Task Open(string path,string? recordingTitle=null,bool viewer=false) {
        path=Path.GetFullPath(path);var info=new FileInfo(path);
        if(!info.Exists)throw new FileNotFoundException("找不到文件。",path);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{path.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")));
        string? nextDraft=recordingTitle==null ? Path.Combine(root,"Drafts",key+".json") : null;
        string nextMedia=path;Review? next=null;
        if(path.EndsWith(".framenote",StringComparison.OrdinalIgnoreCase)) (next,nextMedia)=await Task.Run(()=>Packages.Read(path,scratch,ImageDirectory));
        else if(!ImageFormats.Contains(info.Extension.ToLowerInvariant()) && !Review.Formats.Contains(info.Extension.ToLowerInvariant())) throw new InvalidDataException("请选择支持的视频文件或 .framenote 审阅包。");
        bool picture=ImageFormats.Contains(Path.GetExtension(nextMedia).ToLowerInvariant());
        bool playOriginal=(viewer || playerEntry) && !picture && recordingTitle==null && !info.Extension.Equals(".framenote",StringComparison.OrdinalIgnoreCase);
        var inspect=picture?null:Probe.Inspect(nextMedia,lifetime.Token);
        bool earlyPlayback=playOriginal && doc==null;
        if(earlyPlayback){
            var player=EnsurePlayer();SetPreviewFullscreen(false);
            web.Visibility=Visibility.Hidden;player.Visibility=Visibility.Visible;player.SetVolume(volume);
            player.Preparing(false,"");Title="FrameNote Player - "+info.Name;
            await player.Start(nextMedia,info.Name,0);
        }
        var infoMedia=inspect==null?null:await inspect;
        lifetime.Token.ThrowIfCancellationRequested();
        next??=new Review(picture?2:1,Guid.NewGuid(),recordingTitle??info.Name,infoMedia?.Duration??0,[]);
        bool recovered=false;
        if(File.Exists(nextDraft)){
            var saved=JsonSerializer.Deserialize<Review>(await File.ReadAllTextAsync(nextDraft),Review.Json) ?? throw new InvalidDataException("草稿损坏。");
            saved=saved.WithLegacyNumbers();saved.Check(); if(saved.DurationUs==next.DurationUs && (!path.EndsWith(".framenote",StringComparison.OrdinalIgnoreCase)||saved.Id==next.Id)){next=saved;recovered=true;}
        }
        next.Check();
        if(recovered)ReviewImages.Copy(next,nextDraft+".images",ImageDirectory);
        if(picture){await ShowImage(nextMedia,next.FileName,nextDraft:nextDraft,review:next,package:path.EndsWith(".framenote",StringComparison.OrdinalIgnoreCase));return;}
        var compatibility=!infoMedia!.Direct(nextMedia);
        long[]? directFrames=null;

        if(!compatibility && !playOriginal){
            try{directFrames=await Probe.Frames(nextMedia,infoMedia);}
            catch(InvalidDataException){compatibility=true;}
        }
        viewingImage=false;imageFile=null;var previous=activeRecording; doc=next; openedPackage=path.EndsWith(".framenote",StringComparison.OrdinalIgnoreCase); media=nextMedia; draft=nextDraft;
        lifetime.Token.ThrowIfCancellationRequested();
        activeRecording=recordingTitle!=null?path:null;
        Title=playOriginal?"FrameNote Player - "+doc.FileName:(activeRecording!=null?"临时录屏 · ":"")+doc.FileName+" · FrameNote";
        SetPreviewFullscreen(false);StopTimeline();playbackId=Guid.NewGuid();playbackInfo=infoMedia;deskLoaded=false;preview=null;
        draftRestored=recovered;needsPreview=compatibility;
        if(webReady)await web.ExecuteScriptAsync("v.pause();v.removeAttribute('src');v.load();ready=false;render()");
        if(earlyPlayback){native!.BindReview(doc,infoMedia!);}
        else if(playOriginal){await ShowPlayer(0);}
        else if(needsPreview){await ShowPlayer(0);if(openedPackage)await EnterReview(0);}
        else {
            if(native!=null){native.Stop();native.Visibility=Visibility.Collapsed;}
            web.Visibility=Visibility.Visible;
            await LoadReview(0,default,directFrames);
        }
        if(previous!=null && !string.Equals(previous,media,StringComparison.OrdinalIgnoreCase))recordings.Discard(previous);
    }
    async Task Message(string raw){
        try{
            using var msg=JsonDocument.Parse(raw);var p=msg.RootElement; var op=p.GetProperty("type").GetString();
            if(op=="canOpen"){reviewCanOpen=p.GetProperty("value").GetBoolean();return;}
            if(op=="previewFullscreen"){var on=p.GetProperty("on").GetBoolean();if(!on || (!viewingImage && deskLoaded && !pending && web.IsVisible))SetPreviewFullscreen(on);return;}
            if(op=="playbackVolume"){SetPlaybackVolume(p.GetProperty("value").GetInt32());return;}
            if(op=="catalog"){Catalog();return;}
            if(op=="meterSources"){MeterSources(p.GetProperty("ids").Deserialize<string[]>()!);return;}
            if(op=="readMeters"){ReadMeterPeaks();return;}
            if(op=="storePicture"){
                var request=p.GetProperty("request").GetGuid();
                try{
                    if(pending||configuring||doc==null)throw new InvalidOperationException("当前无法添加图片，请稍后再试。");
                    var encoded=p.GetProperty("data").GetString();
                    if(encoded==null||encoded.Length>ReviewImages.MaxBytes*4L/3+4)throw new InvalidDataException("图片过大。");
                    var (asset,pixelWidth,pixelHeight)=ReviewImages.Store(ImageDirectory,Convert.FromBase64String(encoded));
                    Send(new{type="pictureStored",request,asset,pixelWidth,pixelHeight});
                }catch(Exception e){Send(new{type="pictureStored",request,error=e.Message});}
                return;
            }
            if(pending || configuring)return;
            if(op=="fullImage" && viewingImage && doc?.Id==p.GetProperty("id").GetGuid()){
                await Work(async()=>Send(new{type="fullImage",id=doc.Id,src=new Uri(await OriginalImage()).AbsoluteUri}));return;
            }
            if(op=="adjacentImage"){if(viewingImage)await Work(()=>AdjacentImage(p.GetProperty("direction").GetInt32()));return;}
            if(op=="pasteAsImage"){await Work(PasteAsImage);return;}
            if(op=="stop"){StopCapture();return;}
            if(op=="clearRegion"){if(recorder==null)ResetRegion();return;}
            if(op=="pickRegion"){PickRegion(p.GetProperty("source").GetString()!);return;}
            if(op=="record"){if(recorder==null)Record(p.GetProperty("settings").Deserialize<CaptureSettings>(Review.Json)!,p.GetProperty("gif").GetBoolean());return;}
            if(op=="gif"){await ExportGif(p.GetProperty("id").GetGuid());return;}
            if(op=="open"){await PickVideo();return;}
            if(op is "vlc" or "decodeError"){
                if(media==null || playbackId!=p.GetProperty("playbackId").GetGuid())return;
                if(op=="decodeError"){needsPreview=true;deskLoaded=false;}
                await ShowPlayer(Math.Max(0,p.GetProperty("us").GetInt64()));return;
            }
            if(op=="sendReaper"){await Work(()=>SendReaper(null));return;}
            if(op=="dropReaper" && !viewingImage && doc!=null){
                var screen=web.PointToScreen(new Point(p.GetProperty("x").GetDouble(),p.GetProperty("y").GetDouble()));
                await Work(()=>SendReaper(screen));return;
            }
            if(doc==null || media==null)return;
            if(op=="imageExport"){if(viewingImage)await Work(()=>ExportImage(p.GetProperty("save").GetBoolean()));return;}
            if(op=="snapshot"){await Snapshot(p);return;}
            if(op=="openLink"){
                if(!Uri.TryCreate(p.GetProperty("url").GetString(),UriKind.Absolute,out var link) ||
                    !(link.Scheme=="http" || link.Scheme=="https") || link.Host.Length==0)
                    throw new InvalidDataException("仅支持打开 HTTP 或 HTTPS 网页链接。");
                using var browser=Process.Start(new ProcessStartInfo(link.AbsoluteUri){UseShellExecute=true});
                return;
            }
            if(op=="notes"){
                var notes=p.GetProperty("notes").Deserialize<List<Note>>(Review.Json) ?? throw new InvalidDataException("批注格式有误。");
                var updated=doc.WithNotes(notes);updated.Check();
                if(draft!=null){ReviewImages.Copy(updated,ImageDirectory,draft+".images");Packages.Atomic(draft,s=>JsonSerializer.Serialize(s,updated,Review.Json));}doc=updated;
                Send(new{type="status",text=draft!=null?"草稿已保存" :viewingImage?"批注已更新；导出审阅包可保留剪贴板图片和批注。":"批注已更新；要保留这次临时录屏，请导出审阅包。"});return;
            }
            if(op=="save"){
                var fd=new SaveFileDialog{FileName=Path.GetFileNameWithoutExtension(doc.FileName)+".framenote",Filter="FrameNote 审阅包|*.framenote",DefaultExt=".framenote"};
                if(fd.ShowDialog(this)==true)await Work(async()=>{
                    var visible=doc.WithNotes(p.GetProperty("notes").Deserialize<List<Note>>(Review.Json) ?? throw new InvalidDataException("批注格式有误。"));
                    await Task.Run(()=>Packages.Save(fd.FileName,media,visible,ImageDirectory));
                    Send(new{type="status",text="审阅包已保存："+fd.FileName});
                });
            }
        }catch(Exception e){Send(new{type="error",text=e.Message});}
    }
}


