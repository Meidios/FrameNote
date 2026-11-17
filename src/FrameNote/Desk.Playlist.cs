using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FrameNote;
public sealed partial class Desk {
    readonly MediaPlaylist playlist=new();
    readonly MenuItem listToggle=new(){Header="播放列表(_L)",IsCheckable=true};
    bool expanded,selecting;
    internal static bool IsViewerMedia(string path){
        var ext=Path.GetExtension(path).ToLowerInvariant();return ImageFormats.Contains(ext)||Review.Formats.Contains(ext);
    }
    void InitPlaylist(DockPanel host){
        playlist.Token=lifetime.Token;
        DockPanel.SetDock(playlist,Dock.Left);host.Children.Add(playlist);
        playlist.Visibility=listToggle.Visibility=Visibility.Collapsed;menuStrip.Items.Insert(1,listToggle);
        listToggle.Click+=(_,_)=>{expanded=listToggle.IsChecked;ShowPlaylist();};
        playlist.Items.SelectionChanged+=async (_,_)=>{
            if(selecting || playlist.Items.SelectedItem is not PlaylistFile item)return;
            if(!pending && !configuring && recorder==null && item.Path!=media)await Work(()=>Open(item.Path,viewer:true));
            ShowPlaylist();
        };
    }
    void ShowPlaylist(){
        bool visible=beforeFullscreen==null && (viewingImage && imageFile!=null || native?.IsVisible==true && !openedPackage);
        listToggle.Visibility=visible?Visibility.Visible:Visibility.Collapsed;listToggle.IsChecked=expanded;
        selecting=true;
        try{
            if(visible && media!=null && !playlist.Files.Any(f=>string.Equals(f.Path,media,StringComparison.OrdinalIgnoreCase))){
                playlist.Files.Clear();playlist.Files.Add(new(media));expanded=listToggle.IsChecked=false;
            }
            playlist.Items.SelectedItem=playlist.Files.FirstOrDefault(f=>string.Equals(f.Path,media,StringComparison.OrdinalIgnoreCase));
            playlist.Visibility=visible&&expanded?Visibility.Visible:Visibility.Collapsed;
            if(playlist.IsVisible && playlist.Items.SelectedItem!=null)playlist.Items.ScrollIntoView(playlist.Items.SelectedItem);
        }finally{selecting=false;}
    }
    readonly TaskCompletionSource initialOpen=new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal async void AppendShellSelection(string[] paths){
        await initialOpen.Task;
        if(!lifetime.IsCancellationRequested)await OpenFiles(paths,true);
    }
    async Task OpenFiles(string[] paths,bool append=false){
        if(paths.Length==0)return;
        while(currentWork is {} work)await work.Task;
        if(configuring||recorder!=null||lifetime.IsCancellationRequested)return;
        var files=paths.Where(IsViewerMedia).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(files.Length==0){if(paths.Length==1)await Work(()=>Open(paths[0]));return;}
        bool viewer=viewingImage || native?.IsVisible==true || playerEntry || files.Length>1;
        bool keep=append && (viewingImage || native?.IsVisible==true) && media!=null;
        await Work(async()=>{
            if(!keep)await Open(files[0],viewer:viewer);
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
