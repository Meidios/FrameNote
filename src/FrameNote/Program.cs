using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;

namespace FrameNote;
public static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        if(args is ["--prepare-vlc-cache"]){
            using var engine=NativePlayer.CreateEngine(true);
            return File.Exists(Path.Combine(AppContext.BaseDirectory,"libvlc","win-x64","plugins","plugins.dat"))?0:1;
        }
        L.Initialize();
        if (args.FirstOrDefault()=="--prepare-reaper") {
            try { if(args.Length!=4 && !(args.Length==5 && args[4]=="--colors")) return 2; Packages.PrepareReaper(args[1],args[2],args[3],args.Length==5); return 0; }
            catch(Exception e) { if(args.Length>=4) File.WriteAllText(args[3]+".error",e.Message); return 1; }
        }
        var player=string.Equals(Path.GetFileName(Environment.ProcessPath),"FrameNote Player.exe",StringComparison.OrdinalIgnoreCase);
        if(player && args.FirstOrDefault()=="--shell-selection")return ShellSelection.Run(args.Skip(1).ToArray());
        var warming=player||args.Length>1?NativePlayer.WarmForVideo(args):null;
        RuntimeLog.Start(player);
        var app=new Application();
        app.Exit+=(_,e)=>{Log.Information("Exited with code {ExitCode}",e.ApplicationExitCode);Log.CloseAndFlush();};
        DeskTheme.Start(app);return app.Run(new Desk(args,player,warming));
    }
}
public sealed partial class Desk : Window
{
    readonly WebView2 web = new();
    readonly string root = RuntimeLog.DataDirectory;
    readonly ReviewNavigation reviewPage;
    readonly string scratch;
    string ImageDirectory=>Path.Combine(scratch,"images");
    readonly TemporaryRecordings recordings=new();
    string? activeRecording;
    readonly Dictionary<string,Review> sessionReviews=new(StringComparer.OrdinalIgnoreCase);
    string? media, sessionKey;
    Review? doc;
    bool pending,openedPackage;
    TaskCompletionSource? currentWork;
    readonly bool playerEntry;
    Task? webStartup;
    bool webReady,openingPlayer,webConfigured;
    public Desk(string[] paths,bool playerEntry=false,Task<LibVLCSharp.Shared.LibVLC>? engine=null) {
        startingEngine=engine;
        string? initial=paths.FirstOrDefault();
        this.playerEntry=playerEntry;
        bool direct=(playerEntry || paths.Length>1) && (initial==null || Review.Formats.Contains(Path.GetExtension(initial).ToLowerInvariant()));
        Title=direct?(initial==null?"FrameNote Player":"FrameNote Player - "+Path.GetFileName(initial)):L.Text("FrameNote · 视频审阅"); Width=1230; Height=800; MinWidth=840; MinHeight=560;
        DeskTheme.Window(this); WindowStartupLocation=WindowStartupLocation.CenterScreen;
        InitWindowSize();
        // Only the WPF host surface uses software rendering. WebView2 and VLC
        // own separate child HWNDs and keep their normal video rendering paths.
        SourceInitialized+=(_,_)=>{
            var target=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.CompositionTarget;
            if(target!=null)target.RenderMode=RenderMode.SoftwareOnly;
        };
        scratch=Path.Combine(Path.GetTempPath(),"FrameNote",Guid.NewGuid().ToString("N"));
        reviewPage=new(Path.Combine(AppContext.BaseDirectory,"ui","index.html"),Path.Combine(scratch,"interface"));
        InitShortcuts();InitCapture();
        InitPlaybackVolume(); InitHelp(direct); AllowDrop=true;
        if(direct){
            var player=EnsurePlayer();player.Caption.Text=initial==null?"FrameNote Player":Path.GetFileName(initial);
            player.ReviewButton.IsEnabled=player.Timeline.IsEnabled=player.Shuttle.IsEnabled=player.Output.IsEnabled=false;
        }
        Drop+=async (_,e)=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] files){e.Handled=true;await OpenFiles(files,true);}};
        Loaded+=OpenAtStartup;
        async void OpenAtStartup(object sender,RoutedEventArgs args){
            Loaded-=OpenAtStartup;
            try{
                openingPlayer=direct;
                if(!direct && (initial==null || !ImageFormats.Contains(Path.GetExtension(initial).ToLowerInvariant())))await EnsureReviewWeb();
                if(initial!=null && !lifetime.IsCancellationRequested)await OpenFiles(paths,fromShell:true);
            }catch(Exception e){if(!lifetime.IsCancellationRequested){Log.Error(e,"Startup failed");Simple(L.Text("启动失败"),L.Text("启动失败"),e.Message);}}
            finally{openingPlayer=false;initialOpen.TrySetResult();}
        }
        Closing+=(_,e)=>{if(voiceCapture!=null || configuring){e.Cancel=true;StopVoice();return;}if(pending && !openingPlayer) {e.Cancel=true; preparing?.Cancel();exportDialog?.Cancel(); Send(new {type="status",text=L.Text("请等当前操作结束后再关闭。")});} };
        Closed+=(_,_)=>{StopTimeline();native?.Dispose();web.Dispose();recordings.Dispose();CleanSessionFiles();};
    }
    void CleanSessionFiles(){
        // WebView2 and the media decoders can release file handles just after disposal.
        for(int attempt=0;Directory.Exists(scratch);attempt++){
            try{Directory.Delete(scratch,true);return;}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){
                if(attempt==5)return;
                Thread.Sleep(50 << attempt);
            }
        }
    }
    Task EnsureReviewWeb()=>webStartup??=InitializeReviewWeb();
    async Task InitializeReviewWeb(){
        var loading=new System.Windows.Controls.TextBlock{Text=L.Text("正在打开审阅界面…"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        try{
            surfaces.Children.Insert(0,web);
            web.Visibility=Visibility.Hidden;
            surfaces.Children.Insert(0,loading);
            if(web.CoreWebView2==null){
                // WebView2 processes sharing a profile must agree on the browser language.
                // An existing window can keep running in the previous language after a settings change.
                string profile=Path.Combine(root,L.Language=="en"?"Browser.en":"Browser");
                var options=new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required",language:L.Language);
                await web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,profile,options));
            }
            var core=web.CoreWebView2 ?? throw new InvalidOperationException(L.Text("WebView2 初始化未完成。"));
            if(!webConfigured){ConfigureReviewWeb(core);webConfigured=true;}
            await core.AddScriptToExecuteOnDocumentCreatedAsync("window.shortcutBindings="+JsonSerializer.Serialize(ShortcutPayload(),Review.Json)+";window.frameNoteEnglish="+L.WebCatalog);
            await reviewPage.Load(core,lifetime.Token);
            webReady=true;
            SendEditorPreferences();
            if(native?.IsVisible!=true)web.Visibility=Visibility.Visible;
        }catch{surfaces.Children.Remove(web);webStartup=null;throw;}
        finally{surfaces.Children.Remove(loading);}
    }
    void ConfigureReviewWeb(CoreWebView2 core){
        core.SetVirtualHostNameToFolderMapping("fonts.framenote.invalid",Path.Combine(AppContext.BaseDirectory,"ui","fonts"),CoreWebView2HostResourceAccessKind.Allow);
        void ApplyWebTheme(){
            core.Profile.PreferredColorScheme=DeskTheme.Dark?CoreWebView2PreferredColorScheme.Dark:CoreWebView2PreferredColorScheme.Light;
            if(reviewPage.IsPage(core.Source)) _=web.ExecuteScriptAsync(DeskTheme.WebScript());
        }
        ApplyWebTheme();DeskTheme.Changed+=ApplyWebTheme;Closed+=(_,_)=>DeskTheme.Changed-=ApplyWebTheme;
        core.DOMContentLoaded+=(_,_)=>ApplyWebTheme();
        core.ProcessFailed+=(_,e)=>Log.Error("WebView2 process failed: {Kind} ({Reason}, exit {Code})",e.ProcessFailedKind,e.Reason,e.ExitCode);
        core.Settings.AreDefaultContextMenusEnabled=false; core.Settings.IsStatusBarEnabled=false; core.Settings.AreHostObjectsAllowed=false;
        core.Settings.IsZoomControlEnabled=false;core.Settings.IsPinchZoomEnabled=false;web.ZoomFactor=1;
        core.Settings.AreBrowserAcceleratorKeysEnabled=false; core.Settings.AreDevToolsEnabled=false;
        InitImageExport();
        core.NavigationStarting+=(_,e)=>{if(!reviewPage.IsPage(e.Uri))e.Cancel=true;};
        core.NewWindowRequested+=(_,e)=>e.Handled=true;
        web.AllowExternalDrop=true;
        core.WebMessageReceived+=async (_,e)=>{
            if(!reviewPage.IsPage(e.Source))return;
            if(e.AdditionalObjects is {Count:>0} objects){
                var paths=objects.OfType<CoreWebView2File>().Select(f=>f.Path).ToArray();
                using var message=JsonDocument.Parse(e.WebMessageAsJson);
                if(message.RootElement.GetProperty("type").GetString()=="importAudio")
                    await ImportNoteAudio(message.RootElement,paths);
                else await OpenFiles(paths,true);
            }
            else await Message(e.WebMessageAsJson);
        };
    }
    void ReportError(string text){if(lifetime.IsCancellationRequested)return;if(native?.IsVisible==true)native.Preparing(false,text);else if(webReady)Send(new{type="error",text});else Simple(L.Text("操作失败"),L.Text("操作失败"),text);}
    void Send(object o){if(!lifetime.IsCancellationRequested && web.CoreWebView2!=null)web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(o,Review.Json));}
    void ApplyReviewEdits(Review updated){
        doc=updated;
        if(sessionKey!=null)sessionReviews[sessionKey]=updated;
    }
    async Task Work(Func<Task> action,[System.Runtime.CompilerServices.CallerMemberName] string operation="") {
        if(pending)return; pending=true;var done=currentWork=new();Send(new{type="busy",value=true});
        Log.Information("Operation started: {Operation}",operation);
        try { await action(); }
        catch(OperationCanceledException e) when(e.CancellationToken.IsCancellationRequested){Send(new{type="status",text=e.Message});}
        catch(Exception e){Log.Error(e,"Operation failed: {Operation}",operation);ReportError(e.Message);}
        finally {pending=false;currentWork=null;Send(new{type="busy",value=false});ShowPlaylist();done.SetResult();}
    }
    async Task Open(string path,string? recordingTitle=null,VideoOpenMode mode=VideoOpenMode.Auto) {
        bool showLoad=Review.Formats.Contains(Path.GetExtension(path).ToLowerInvariant()) ||
            path.EndsWith(".framenote",StringComparison.OrdinalIgnoreCase);
        if(showLoad)ReportMediaLoad("正在打开视频…");
        try{await OpenMedia(path,recordingTitle,mode);}
        catch{if(showLoad)ReportMediaLoad(null);throw;}
    }
    async Task OpenMedia(string path,string? recordingTitle,VideoOpenMode mode) {
        path=Path.GetFullPath(path);var info=new FileInfo(path);
        Log.Information("Opening {Format} in {Mode} mode",info.Extension,mode);
        if(!info.Exists)throw new FileNotFoundException(L.Text("找不到文件。"),path);
        if(webReady)Send(new{type="status",text=L.Text("正在打开 ")+info.Name+"…"});
        var nextKey=recordingTitle==null?$"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}":null;
        bool package=info.Extension.Equals(".framenote",StringComparison.OrdinalIgnoreCase);
        string nextMedia=path;Review? next=null;
        if(package) (next,nextMedia)=await Task.Run(()=>Packages.Read(path,scratch,ImageDirectory));
        else if(!ImageFormats.Contains(info.Extension.ToLowerInvariant()) && !Review.Formats.Contains(info.Extension.ToLowerInvariant())) throw new InvalidDataException(L.Text("请选择支持的视频文件或 .framenote 审阅包。"));
        bool picture=ImageFormats.Contains(Path.GetExtension(nextMedia).ToLowerInvariant());
        bool playOriginal=mode==VideoOpenMode.Player && !picture && recordingTitle==null && !package;
        bool earlyPlayback=playOriginal && doc==null;
        if(earlyPlayback){
            var player=EnsurePlayer();SetPreviewFullscreen(false);
            web.Visibility=Visibility.Hidden;player.Visibility=Visibility.Visible;player.SetVolume(volume);
            player.Preparing(false,"");Title="FrameNote Player - "+info.Name;
            await player.Start(nextMedia,info.Name,0);
        }
        var infoMedia=picture?null:await Probe.Inspect(nextMedia,lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        next??=new Review(picture?2:1,Guid.NewGuid(),recordingTitle??info.Name,infoMedia?.Duration??0,[]);
        if(nextKey!=null && sessionReviews.TryGetValue(nextKey,out var edited) && (!package || edited.Id==next.Id)
            && (edited.Timeline?.SourceDurationUs??edited.DurationUs)==(next.Timeline?.SourceDurationUs??next.DurationUs))
            next=edited;
        next.Check();
        if(picture){await ShowImage(nextMedia,next.FileName,nextKey:nextKey,review:next,package:package);reviewOnly=mode==VideoOpenMode.Review;return;}
        var compatibility=!infoMedia!.Direct(nextMedia);
        long[]? directFrames=null;
        string? nextPreview=null;

        if(!compatibility && !playOriginal){
            try{directFrames=await ReadReviewFrames(nextMedia,infoMedia,lifetime.Token);}
            catch(InvalidDataException){compatibility=true;}
        }
        if(compatibility && mode==VideoOpenMode.Review)
            (directFrames,nextPreview)=await PrepareDeskPreview(nextMedia,infoMedia);
        viewingImage=false;imageFile=null;var previous=activeRecording; doc=next; openedPackage=package; media=nextMedia; sessionKey=nextKey;
        lifetime.Token.ThrowIfCancellationRequested();
        activeRecording=recordingTitle!=null?path:null;
        Title=playOriginal?"FrameNote Player - "+doc.FileName:(activeRecording!=null?L.Text("临时录屏 · "):"")+doc.FileName+" · FrameNote";
        SetPreviewFullscreen(false);StopTimeline();playbackId=Guid.NewGuid();playbackInfo=infoMedia;reviewFrameTimes=[];deskLoaded=false;preview=nextPreview;
        needsPreview=compatibility;reviewOnly=mode==VideoOpenMode.Review;
        if(webReady)await web.ExecuteScriptAsync("transport.pause();v.removeAttribute('src');v.load();ready=false;render()");
        if(earlyPlayback){if(doc.Timeline!=null)await ShowPlayer(0);else native!.BindReview(doc,infoMedia!);}
        else if(playOriginal){await ShowPlayer(0);}
        else if(needsPreview && !reviewOnly){await ShowPlayer(0);if(openedPackage)await EnterReview(0);}
        else {
            if(native!=null){native.Stop();native.Visibility=Visibility.Collapsed;}
            web.Visibility=Visibility.Visible;
            await LoadReview(0,lifetime.Token,directFrames);
        }
        if(native?.IsVisible==true && webReady)Send(new{type="mediaLoading",stage=(string?)null});
        if(previous!=null && !string.Equals(previous,media,StringComparison.OrdinalIgnoreCase))recordings.Discard(previous);
    }
    async Task Message(string raw){
        try{
            using var msg=JsonDocument.Parse(raw);var p=msg.RootElement; var op=p.GetProperty("type").GetString();
            if(op=="diagnostic"){RuntimeLog.WebMessage(p.GetProperty("message").GetString(),p.GetProperty("error").GetBoolean());return;}
            if(op=="pickAudio"){
                if(pending||configuring||viewingImage||doc==null||voiceCapture!=null)return;
                var picker=new Microsoft.Win32.OpenFileDialog{Title=L.Text("添加音频文件"),Multiselect=true,
                    Filter=L.Text("音频文件")+"|"+string.Join(';',ReviewVoice.Extensions.Split(' ').Select(ext=>"*"+ext))};
                if(picker.ShowDialog(this)==true)await ImportNoteAudio(p,picker.FileNames);return;
            }
            if(op=="voiceStop"){StopVoice();return;}
            if(op=="cancelPreview"){preparing?.Cancel();return;}
            if(op=="voiceStart"){await RecordVoice(p);return;}
            if(op=="canOpen"){reviewCanOpen=p.GetProperty("value").GetBoolean();return;}
            if(op=="historyState"){
                titleBar.UndoButton.IsEnabled=p.GetProperty("undo").GetBoolean();
                titleBar.RedoButton.IsEnabled=p.GetProperty("redo").GetBoolean();
                return;
            }
            if(op=="previewFullscreen"){var on=p.GetProperty("on").GetBoolean();if(!on || (!viewingImage && deskLoaded && !pending && web.IsVisible))SetPreviewFullscreen(on);return;}
            if(op=="playbackVolume"){SetPlaybackVolume(p.GetProperty("value").GetInt32());return;}
            if(op=="catalog"){await Catalog(p.TryGetProperty("initial",out var initialCatalog) && initialCatalog.GetBoolean());return;}
            if(op=="capturePreferences"){SaveCapturePrefs(p.GetProperty("changes"));return;}
            if(op=="meterSources"){MeterSources(p.GetProperty("ids").Deserialize<string[]>()!);return;}
            if(op=="readMeters"){ReadMeterPeaks();return;}
            if(op=="storePicture"){
                var request=p.GetProperty("request").GetGuid();
                try{
                    if(pending||configuring||doc==null)throw new InvalidOperationException(L.Text("当前无法添加图片，请稍后再试。"));
                    var encoded=p.GetProperty("data").GetString();
                    if(encoded==null||encoded.Length>ReviewImages.MaxBytes*4L/3+4)throw new InvalidDataException(L.Text("图片过大。"));
                    var (asset,pixelWidth,pixelHeight)=ReviewImages.Store(ImageDirectory,Convert.FromBase64String(encoded));
                    Send(new{type="pictureStored",request,asset,pixelWidth,pixelHeight});
                }catch(Exception e){Log.Error(e,"Could not store annotation image");Send(new{type="pictureStored",request,error=e.Message});}
                return;
            }
            if(op=="decodeError"){
                var id=p.GetProperty("playbackId").GetGuid();
                Log.Warning("Video decoder reported a playback error");
                while(currentWork is {} work)await work.Task;
                if(lifetime.IsCancellationRequested || configuring || media==null || playbackInfo==null || id!=playbackId || preview!=null || native?.IsVisible==true)return;
                var us=Math.Max(0,p.GetProperty("us").GetInt64());
                needsPreview=true;deskLoaded=false;
                await Work(async()=>{
                    var prepared=await PrepareDeskPreview(media,playbackInfo);
                    preview=prepared.Preview;playbackId=Guid.NewGuid();
                    await LoadReview(us,lifetime.Token,prepared.Frames,recovering:true);
                });return;
            }
            if(pending || configuring)return;
            if(op=="togglePlaylist"){TogglePlaylist();return;}
            if(op=="fullImage" && viewingImage && doc?.Id==p.GetProperty("id").GetGuid()){
                await Work(async()=>Send(new{type="fullImage",id=doc.Id,src=new Uri(await OriginalImage()).AbsoluteUri}));return;
            }
            if(op=="adjacentImage"){if(viewingImage)await Work(()=>AdjacentImage(p.GetProperty("direction").GetInt32()));return;}
            if(op=="pasteAsImage"){await Work(PasteAsImage);return;}
            if(op=="pasteNotePicture" && doc?.Id==p.GetProperty("documentId").GetGuid()){
                SendClipboardPicture(doc.Id,p.GetProperty("at").GetInt64());return;
            }
            if(op=="stop"){StopCapture();return;}
            if(op=="clearRegion"){if(recorder==null)ClearOutline();return;}
            if(op=="pickRegion"){PickRegion(p.GetProperty("target"));return;}
            if(op=="record"){if(recorder==null)Record(p.GetProperty("settings").Deserialize<CaptureSettings>(Review.Json)!,p.GetProperty("gif").GetBoolean());return;}
            if(op=="gif"){await ExportGif(p.GetProperty("id").GetGuid());return;}
            if(op=="open"){await PickVideo();return;}
            if(op=="vlc"){
                if(media==null || playbackId!=p.GetProperty("playbackId").GetGuid())return;
                await ShowPlayer(Math.Max(0,p.GetProperty("us").GetInt64()));return;
            }
            if(op=="sendReaper"){await Work(()=>SendReaper(null));return;}
            if(op=="dropReaper" && !viewingImage && doc!=null){
                var screen=web.PointToScreen(new Point(p.GetProperty("x").GetDouble(),p.GetProperty("y").GetDouble()));
                await Work(()=>SendReaper(screen));return;
            }
            if(doc==null || media==null)return;
            if(op is "exportVideo" or "exportGif"){
                if(p.TryGetProperty("documentId",out var exportId)&&exportId.GetGuid()!=doc.Id)return;
                var range=p.TryGetProperty("range",out var selection)?selection.Deserialize<ExportTimeRange>(Review.Json):null;
                var target=op=="exportVideo"?VideoOutput.Mp4:p.GetProperty("copy").GetBoolean()?VideoOutput.GifClipboard:VideoOutput.GifFile;
                await ExportVideo(target,range);return;
            }
            if(op=="exportExcel"){await ExportExcel();return;}
            if(op=="imageExport"){if(viewingImage)await Work(()=>ExportImage(p.GetProperty("save").GetBoolean()));return;}
            if(op=="snapshot"){await Snapshot(p);return;}
            if(op=="openLink"){
                if(!Uri.TryCreate(p.GetProperty("url").GetString(),UriKind.Absolute,out var link) ||
                    !(link.Scheme=="http" || link.Scheme=="https") || link.Host.Length==0)
                    throw new InvalidDataException(L.Text("仅支持打开 HTTP 或 HTTPS 网页链接。"));
                using var browser=Process.Start(new ProcessStartInfo(link.AbsoluteUri){UseShellExecute=true});
                return;
            }
            if(op=="notes"){
                if(p.TryGetProperty("documentId",out var documentId)&&documentId.GetGuid()!=doc.Id)return;
                var notes=p.GetProperty("notes").Deserialize<List<Note>>(Review.Json) ?? throw new InvalidDataException(L.Text("批注格式有误。"));
                var updated=doc.WithNotes(notes);
                if(p.TryGetProperty("rulerScales",out var scales))updated=updated with{RulerScales=scales.Deserialize<Dictionary<long,RulerScale>>(Review.Json)};
                if(p.TryGetProperty("timeline",out var timeline)){var edits=timeline.Deserialize<EditTimeline>(Review.Json);updated=updated with{Timeline=edits,Version=edits==null?(viewingImage?2:1):3,DurationUs=p.GetProperty("durationUs").GetInt64()};}
                // Duplicated clips can own numbered annotations hidden by an edge trim.
                int highestTrimmed=updated.Timeline?.Trimmed?.Max(part=>part.Note?.Number)??0;
                updated=updated with{LastNoteNumber=Math.Max(updated.LastNoteNumber,highestTrimmed)};
                updated.Check();
                ApplyReviewEdits(updated);
                return;
            }
            if(op=="save"){
                var visible=doc.WithNotes(p.GetProperty("notes").Deserialize<List<Note>>(Review.Json) ?? throw new InvalidDataException(L.Text("批注格式有误。")));
                await ExportPackage(visible,media);
            }
        }catch(Exception e){Log.Error(e,"Review command failed");Send(new{type="error",text=e.Message});}
    }
}


