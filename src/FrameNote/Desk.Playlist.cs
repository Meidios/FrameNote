using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace FrameNote;
public sealed partial class Desk {
    enum VideoOpenMode { Auto, Review, Player }
    VideoOpenMode PlaylistOpenMode=>reviewOnly && native?.IsVisible!=true?VideoOpenMode.Review:VideoOpenMode.Player;
    readonly MediaPlaylist playlist=new();
    readonly Border playlistPanel=new(){Width=0,ClipToBounds=true,Visibility=Visibility.Collapsed};
    bool expanded,selecting;
    bool PlaylistAvailable=>beforeFullscreen==null && (viewingImage && imageFile!=null || native?.IsVisible==true && !openedPackage || reviewOnly && playlist.Files.Count>1);
    internal static bool IsViewerMedia(string path){
        var ext=Path.GetExtension(path).ToLowerInvariant();return ImageFormats.Contains(ext)||Review.Formats.Contains(ext);
    }
    void InitPlaylist(DockPanel host){
        playlist.Token=lifetime.Token;
        playlist.HorizontalAlignment=HorizontalAlignment.Left;
        playlistPanel.Child=playlist;DockPanel.SetDock(playlistPanel,Dock.Left);host.Children.Add(playlistPanel);
        PreviewKeyDown+=(_,e)=>{
            if(Shortcut.From(e)!=new Shortcut((int)ModifierKeys.Alt,'L') || !PlaylistAvailable)return;
            e.Handled=true;if(!e.IsRepeat)TogglePlaylist();
        };
        playlist.Items.SelectionChanged+=async (_,_)=>{
            if(selecting || playlist.Items.SelectedItem is not PlaylistFile item)return;
            if(!pending && !configuring && recorder==null && item.Path!=media)await Work(()=>Open(item.Path,mode:PlaylistOpenMode));
            ShowPlaylist();
        };
    }
    void TogglePlaylist(){
        if(!PlaylistAvailable || pending || configuring)return;
        expanded=!expanded;ShowPlaylist();
    }
    void SizePlaylist(bool open,bool animate){
        double target=open?playlist.Width:0;
        double from=playlistPanel.ActualWidth;
        playlistPanel.IsHitTestVisible=open;
        if(animate && (double)playlistPanel.GetAnimationBaseValue(WidthProperty)==target)return;
        playlistPanel.BeginAnimation(WidthProperty,null);
        playlistPanel.Width=target;
        if(!animate || !SystemParameters.ClientAreaAnimation || Math.Abs(target-from)<1){
            playlistPanel.Visibility=open?Visibility.Visible:Visibility.Collapsed;return;
        }
        playlistPanel.Visibility=Visibility.Visible;
        var slide=new DoubleAnimation(from,target,TimeSpan.FromMilliseconds(220*Math.Abs(target-from)/playlist.Width)){
            EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
        if(!open)slide.Completed+=(_,_)=>{
            if((double)playlistPanel.GetAnimationBaseValue(WidthProperty)==0)playlistPanel.Visibility=Visibility.Collapsed;
        };
        playlistPanel.BeginAnimation(WidthProperty,slide);
    }
    void ShowPlaylist(){
        bool visible=PlaylistAvailable;
        selecting=true;
        try{
            if(visible && media!=null && !playlist.Files.Any(f=>string.Equals(f.Path,media,StringComparison.OrdinalIgnoreCase))){
                playlist.Files.Clear();playlist.Files.Add(new(media));expanded=false;
            }
            playlist.Items.SelectedItem=playlist.Files.FirstOrDefault(f=>string.Equals(f.Path,media,StringComparison.OrdinalIgnoreCase));
            SizePlaylist(visible&&expanded,visible);
            if(visible&&expanded && playlist.Items.SelectedItem!=null)playlist.Items.ScrollIntoView(playlist.Items.SelectedItem);
            if(native!=null){
                native.PlaylistButton.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
                native.PlaylistButton.IsChecked=expanded;
            }
            if(webReady)Send(new{type="playlistState",visible=visible&&web.IsVisible,expanded});
        }finally{selecting=false;}
    }
    readonly TaskCompletionSource initialOpen=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal async void AppendShellSelection(string[] paths){
        await initialOpen.Task;
        if(!lifetime.IsCancellationRequested)await OpenFiles(paths,true,fromShell:true);
    }
    async Task OpenFiles(string[] paths,bool append=false,bool fromShell=false){
        if(paths.Length==0)return;
        while(currentWork is {} work)await work.Task;
        if(configuring||recorder!=null||lifetime.IsCancellationRequested)return;
        var files=paths.Where(IsViewerMedia).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        bool welcome=!fromShell && !playerEntry && !viewingImage && !deskLoaded && web.IsVisible;
        var mode=VideoOpenMode.Auto;
        if(welcome)mode=VideoOpenMode.Review;
        else if(viewingImage || native?.IsVisible==true || playerEntry || files.Length>1)mode=VideoOpenMode.Player;
        if(files.Length==0){if(paths.Length==1)await Work(()=>Open(paths[0],mode:mode));return;}
        bool keep=append && (viewingImage || native?.IsVisible==true) && media!=null;
        await Work(async()=>{
            if(!keep)await Open(files[0],mode:mode);
            selecting=true;
            try{
                if(!keep)playlist.Files.Clear();
                var known=playlist.Files.Select(f=>f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if(keep && !openedPackage && (imageFile!=null || !viewingImage) && known.Add(media!))playlist.Files.Add(new(media!));
                foreach(var path in files)if(known.Add(path))playlist.Files.Add(new(path));
                expanded=keep||files.Length>1;
            }finally{selecting=false;}
        });
    }
}
