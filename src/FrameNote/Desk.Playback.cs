using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FrameNote;
public sealed partial class Desk
{
    readonly Grid surfaces=new();
    NativePlayer? native;
    Task<LibVLCSharp.Shared.LibVLC>? startingEngine;
    CancellationTokenSource? preparing;
    Guid playbackId;
    DeskTitleBar titleBar=null!;
    (WindowState State,WindowStyle Style,ResizeMode Resize,WindowSize Size)? beforeFullscreen;
    void SetPreviewFullscreen(bool on){
        if(on && beforeFullscreen==null){
            beforeFullscreen=(WindowState,WindowStyle,ResizeMode,CurrentWindowSize());
            titleBar.SetFullscreen(true);
            WindowState=WindowState.Normal;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;WindowState=WindowState.Maximized;
            if(native?.IsVisible==true)native.SetFullscreen(true);else Send(new{type="previewFullscreen",on=true});
            ShowPlaylist();
        }else if(!on && beforeFullscreen is {} old){
            WindowState=WindowState.Normal;WindowStyle=old.Style;ResizeMode=old.Resize;WindowState=old.State;
            titleBar.SetFullscreen(false);beforeFullscreen=null;
            if(native?.IsVisible==true)native.SetFullscreen(false);else Send(new{type="previewFullscreen",on=false});
            ShowPlaylist();
        }
    }
    Probe.Info? playbackInfo;
    long[] reviewFrameTimes=[];
    string? preview;
    bool deskLoaded,needsPreview,reviewOnly;

    async Task<bool> SupportsReviewAudio(Probe.Info info){
        if(info.Audio!="flac")return true;
        await EnsureReviewWeb();
        // A playable video stream alone does not establish that the embedded audio will play.
        return await web.ExecuteScriptAsync("""document.createElement('audio').canPlayType('audio/mp4; codecs="fLaC"') !== ''""")=="true";
    }

    void ReportMediaLoad(string? stage,int? percent=null){
        if(native?.IsVisible==true){
            if(preparing!=null && !needsPreview && percent is int value)native.SetPreviewProgress(value);
            else native.ShowMediaLoad(stage==null?null:L.Text(stage),percent);
        }
        if(webReady)Send(new{type="mediaLoading",stage,percent});
    }
    Task<long[]> ReadReviewFrames(string source,Probe.Info info,CancellationToken token){
        const string stage="正在读取视频帧…";
        ReportMediaLoad(stage,0);
        return Probe.Frames(source,info,token,new Progress<int>(percent=>ReportMediaLoad(stage,percent)));
    }
    async Task PickVideo(){
        var fd=new OpenFileDialog{Multiselect=true,Filter=L.Text("图片、视频与审阅包|")+string.Join(";",Review.Formats.Concat(ImageFormats).Append(".framenote").Select(ext=>"*"+ext))};
        if(fd.ShowDialog(this)==true)await OpenFiles(fd.FileNames);
    }
    NativePlayer EnsurePlayer(){
        if(native==null){
            native=new NativePlayer(scratch,startingEngine);native.SetShortcuts(shortcuts);startingEngine=null;
            PreviewKeyDown+=(_,e)=>{
                if(shortcuts["cancel"].Contains(Shortcut.From(e)) && native.IsVisible && preparing!=null){
                    e.Handled=true;preparing.Cancel();
                }else if(shortcuts["cancel"].Contains(Shortcut.From(e)) && beforeFullscreen!=null && native.IsVisible){
                    e.Handled=true;SetPreviewFullscreen(false);
                }
            };
            void PlaylistChecked(object sender,RoutedEventArgs e){
                if(native.PlaylistButton.IsChecked==expanded)return;
                TogglePlaylist();native.PlaylistButton.IsChecked=expanded;
            }
            native.PlaylistButton.Checked+=PlaylistChecked;
            native.PlaylistButton.Unchecked+=PlaylistChecked;
            native.VolumeRequested+=SetPlaybackVolume;
            native.FullscreenRequested+=on=>SetPreviewFullscreen(on);
            native.OpenRequested+=async()=>await PickVideo();
            native.ReviewRequested+=async()=>await Work(()=>EnterReview());
            native.CancelRequested+=()=>preparing?.Cancel();
            native.PlaybackEnded+=async()=>{
                int next=playlist.Items.SelectedIndex+1;
                if(!pending && !configuring && recorder==null && next>0 && next<playlist.Files.Count)
                    await Work(()=>Open(playlist.Files[next].Path,mode:VideoOpenMode.Player));
            };
            surfaces.Children.Add(native);
        }
        return native;
    }
    async Task ShowPlayer(long us){
        if(media==null || doc==null || playbackInfo==null)return;
        var player=EnsurePlayer();
        if(playerEntry)Title="FrameNote Player - "+doc.FileName;
        web.Visibility=Visibility.Hidden;
        player.Visibility=Visibility.Visible;
        player.Preparing(false,"");
        player.ShowMediaLoad(L.Text("正在打开视频…"));
        player.SetVolume(volume);var playable=await EditedMedia(lifetime.Token);await player.Start(playable,doc.FileName,us/1000);player.BindReview(doc,doc.Timeline==null?playbackInfo:await Probe.Inspect(playable,lifetime.Token));
    }
    async Task EnterReview(long? initialUs=null){
        if(media==null || doc==null || native==null)return;
        var position=initialUs??native.TimeUs;
        native.Pause();
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);preparing=cancel;
        bool generate=needsPreview && preview==null;
        native.Preparing(true,generate?L.Text("正在生成批注预览"):L.Text("正在打开逐帧审阅…"),true);
        try{
            if(!deskLoaded && !needsPreview && playbackInfo!=null && !await SupportsReviewAudio(playbackInfo)){
                needsPreview=true;
                native.Preparing(true,L.Text("正在生成批注预览"),true);
            }
            if(!deskLoaded || needsPreview && preview==null)await LoadReview(position,cancel.Token,playOnLoad:initialUs!=null);
            else Send(new{type="resumeReview",us=position});
            SetPreviewFullscreen(false);
            native.Stop();native.Visibility=Visibility.Collapsed;web.Visibility=Visibility.Visible;web.Focus();
            Title=(activeRecording!=null?L.Text("临时录屏 · "):"")+doc.FileName+" · FrameNote";
        }catch(OperationCanceledException){native.Preparing(false,L.Text("已取消，可继续播放。"));}
        catch(Exception e){Serilog.Log.Error(e,"Player could not enter the review editor");native.Preparing(false,L.Text("无法进入批注：")+e.Message);native.ToolTip=e.Message;}
        finally{preparing=null;if(native.Visibility==Visibility.Collapsed)native.Preparing(false,"");}
    }
    async Task<(long[] Frames,string Preview)> PrepareDeskPreview(string source,Probe.Info info){
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        preparing=cancel;Send(new{type="preparingPreview",active=true,percent=0});
        try{
            var frames=await Probe.Frames(source,info,cancel.Token);
            return (frames,await CreatePreview(source,info.Duration,frames,cancel.Token));
        }
        catch(OperationCanceledException) when(cancel.IsCancellationRequested){throw new OperationCanceledException(L.Text("已取消生成批注预览。"),cancel.Token);}
        finally{preparing=null;Send(new{type="preparingPreview",active=false});}
    }
    void ReportPreviewProgress(int percent){
        if(native?.IsVisible==true)native.SetPreviewProgress(percent);
        else Send(new{type="preparingPreview",active=true,percent});
    }
    async Task<string> CreatePreview(string source,long durationUs,long[] frames,CancellationToken token){
        Directory.CreateDirectory(scratch);
        var dest=Path.Combine(scratch,Guid.NewGuid().ToString("N")+".mp4");
        try{
            await Probe.EncodeWithProgress(["-nostdin","-hide_banner","-loglevel","error","-copyts","-start_at_zero","-i",source,
                "-map","0:v:0","-map","0:a:0?","-vf","pad=ceil(iw/2)*2:ceil(ih/2)*2","-c:v","libx264","-preset","veryfast",
                "-crf","17","-pix_fmt","yuv420p","-bf","0","-fps_mode","passthrough","-enc_time_base:v","1:1000000","-video_track_timescale","1000000",
                "-c:a","aac","-b:a","256k","-movflags","+faststart","-avoid_negative_ts","disabled",dest],durationUs,ReportPreviewProgress,token);
            var encoded=await Probe.Inspect(dest,token);
            var pts=await Probe.Frames(dest,encoded,token);
            if(pts.Length!=frames.Length || pts.Where((t,i)=>Math.Abs(t-frames[i])>2).Any())
                throw new InvalidDataException(L.Text("预览与原片的帧时间不一致，无法逐帧批注。"));
            token.ThrowIfCancellationRequested();
            ReportPreviewProgress(100);
            return dest;
        }catch{File.Delete(dest);throw;}
    }
    async Task LoadReview(long us,CancellationToken token,long[]? knownFrames=null,bool playOnLoad=false,bool recovering=false){
        if(media==null || doc==null || playbackInfo==null)return;
        await EnsureReviewWeb();
        var frames=knownFrames??await ReadReviewFrames(media,playbackInfo,token);
        if(needsPreview && preview==null)preview=await CreatePreview(media,playbackInfo.Duration,frames,token);
        token.ThrowIfCancellationRequested();
        reviewFrameTimes=frames;
        if(webReady)Send(new{type="mediaLoading",stage="正在加载首帧…"});
        Send(new{type="loaded",playbackId,doc,frames,src=new Uri(preview??media).AbsoluteUri,proxy=preview!=null,recovering,autoPlay=!recovering&&(knownFrames!=null || playOnLoad),volume,us,
            images=new Uri(ImageDirectory+Path.DirectorySeparatorChar).AbsoluteUri,temporary=activeRecording!=null,hasSourceAudio=playbackInfo.Audio!=""});
        deskLoaded=true;
        _=BuildTimeline(playbackId,media,playbackInfo);
    }
}
