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
    Menu menuStrip=null!;
    (WindowState State,WindowStyle Style,ResizeMode Resize,WindowSize Size)? beforeFullscreen;
    void SetPreviewFullscreen(bool on){
        if(on && beforeFullscreen==null){
            beforeFullscreen=(WindowState,WindowStyle,ResizeMode,CurrentWindowSize());
            menuStrip.Visibility=Visibility.Collapsed;
            WindowState=WindowState.Normal;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;WindowState=WindowState.Maximized;
            if(native?.IsVisible==true)native.SetFullscreen(true);else Send(new{type="previewFullscreen",on=true});
            ShowPlaylist();
        }else if(!on && beforeFullscreen is {} old){
            WindowState=WindowState.Normal;WindowStyle=old.Style;ResizeMode=old.Resize;WindowState=old.State;
            menuStrip.Visibility=Visibility.Visible;beforeFullscreen=null;
            if(native?.IsVisible==true)native.SetFullscreen(false);else Send(new{type="previewFullscreen",on=false});
            ShowPlaylist();
        }
    }
    Probe.Info? playbackInfo;
    string? preview;
    bool deskLoaded,needsPreview,draftRestored;

    async Task PickVideo(){
        var fd=new OpenFileDialog{Multiselect=true,Filter="图片、视频与审阅包|"+string.Join(";",Review.Formats.Concat(ImageFormats).Append(".framenote").Select(ext=>"*"+ext))};
        if(fd.ShowDialog(this)==true)await OpenFiles(fd.FileNames);
    }
    NativePlayer EnsurePlayer(){
        if(native==null){
            native=new NativePlayer(scratch,startingEngine);startingEngine=null;
            PreviewKeyDown+=(_,e)=>{
                if(e.Key==System.Windows.Input.Key.Escape && beforeFullscreen!=null && native.IsVisible){
                    e.Handled=true;SetPreviewFullscreen(false);
                }
            };
            native.VolumeRequested+=SetPlaybackVolume;
            native.FullscreenRequested+=on=>SetPreviewFullscreen(on);
            native.OpenRequested+=async()=>await PickVideo();
            native.ReviewRequested+=async()=>await Work(()=>EnterReview());
            native.CancelRequested+=()=>preparing?.Cancel();
            native.PlaybackEnded+=async()=>{
                int next=playlist.Items.SelectedIndex+1;
                if(!pending && !configuring && recorder==null && next>0 && next<playlist.Files.Count)
                    await Work(()=>Open(playlist.Files[next].Path,viewer:true));
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
        player.SetVolume(volume);await player.Start(media,doc.FileName,us/1000);player.BindReview(doc,playbackInfo);
    }
    async Task EnterReview(long? initialUs=null){
        if(media==null || doc==null || native==null)return;
        var position=initialUs??native.TimeUs;
        native.Pause();
        using var cancel=new CancellationTokenSource();preparing=cancel;
        native.Preparing(true,needsPreview?"正在生成临时审阅预览，可取消并继续播放。":"正在打开逐帧审阅…");
        try{
            if(!deskLoaded || needsPreview && preview==null)await LoadReview(position,cancel.Token,playOnLoad:initialUs!=null);
            else Send(new{type="resumeReview",us=position});
            SetPreviewFullscreen(false);
            native.Stop();native.Visibility=Visibility.Collapsed;web.Visibility=Visibility.Visible;web.Focus();
            Title=(activeRecording!=null?"临时录屏 · ":"")+doc.FileName+" · FrameNote";
        }catch(OperationCanceledException){native.Preparing(false,"已取消，可继续播放。");}
        catch(Exception e){native.Preparing(false,"无法进入批注："+e.Message);native.ToolTip=e.Message;}
        finally{preparing=null;if(native.Visibility==Visibility.Collapsed)native.Preparing(false,"");}
    }
    async Task LoadReview(long us,CancellationToken token,long[]? knownFrames=null,bool playOnLoad=false){
        if(media==null || doc==null || playbackInfo==null)return;
        await EnsureReviewWeb();
        var frames=knownFrames??await Probe.Frames(media,playbackInfo,token);
        if(needsPreview && preview==null){
            Directory.CreateDirectory(scratch);
            var dest=Path.Combine(scratch,Guid.NewGuid().ToString("N")+".mp4");
            try{
                await Probe.Run(token,"ffmpeg","-nostdin","-hide_banner","-loglevel","error","-copyts","-start_at_zero","-i",media,
                    "-map","0:v:0","-map","0:a:0?","-vf","pad=ceil(iw/2)*2:ceil(ih/2)*2","-c:v","libx264","-preset","veryfast",
                    "-crf","17","-pix_fmt","yuv420p","-bf","0","-fps_mode","passthrough","-enc_time_base:v","1:1000000","-video_track_timescale","1000000",
                    "-c:a","aac","-b:a","256k","-movflags","+faststart","-avoid_negative_ts","disabled",dest);
                var encoded=await Probe.Inspect(dest,token);
                var pts=await Probe.Frames(dest,encoded,token);
                if(pts.Length!=frames.Length || pts.Where((t,i)=>Math.Abs(t-frames[i])>2).Any())
                    throw new InvalidDataException("预览与原片的帧时间不一致，已保留 VLC 播放。");
                preview=dest;
            }finally{if(preview!=dest)File.Delete(dest);}
        }
        Send(new{type="loaded",playbackId,doc,frames,src=new Uri(preview??media).AbsoluteUri,proxy=preview!=null,autoPlay=knownFrames!=null || playOnLoad,volume,us,
            images=new Uri(ImageDirectory+Path.DirectorySeparatorChar).AbsoluteUri,recovered=draftRestored,temporary=activeRecording!=null});
        deskLoaded=true;
        _=BuildTimeline(playbackId,media,playbackInfo);
    }
}
