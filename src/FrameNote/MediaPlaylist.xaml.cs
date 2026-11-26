using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameNote;

internal sealed class PlaylistFile(string path){
    public string Path {get;}=path;
    public string Name=>System.IO.Path.GetFileName(Path);
    internal BitmapSource? Thumbnail;
}

public partial class MediaPlaylist : UserControl {
    internal readonly ObservableCollection<PlaylistFile> Files=[];
    readonly SemaphoreSlim slots=new(2);
    internal CancellationToken Token;
    public MediaPlaylist(){InitializeComponent();Items.ItemsSource=Files;}
    // A realized template can already be visible and have its DataContext before the change handlers are attached.
    void LoadedThumb(object sender,RoutedEventArgs e)=>Load((Image)sender);
    void VisibleThumb(object sender,DependencyPropertyChangedEventArgs e){if((bool)e.NewValue)Load((Image)sender);}
    void ChangeThumb(object sender,DependencyPropertyChangedEventArgs e){var image=(Image)sender;image.Source=null;if(image.IsVisible)Load(image);}
    async void Load(Image image){
        if(!image.IsVisible || image.DataContext is not PlaylistFile file)return;
        if(file.Thumbnail is {} cached){image.Source=cached;return;}
        if(ReferenceEquals(image.Tag,file))return;
        image.Tag=file;
        try{
            await slots.WaitAsync(Token);
            try{
                if(!image.IsVisible || !ReferenceEquals(image.DataContext,file))return;
                file.Thumbnail??=await Task.Run(()=>Thumbnail(file.Path),Token)??await DecodeThumb(file.Path);
                if(ReferenceEquals(image.DataContext,file))image.Source=file.Thumbnail;
            }finally{slots.Release();}
        }catch(Exception ex) when(ex is OperationCanceledException or IOException or COMException or NotSupportedException or ArgumentException or InvalidDataException or System.ComponentModel.Win32Exception){}
        finally{if(ReferenceEquals(image.Tag,file))image.ClearValue(TagProperty);}
    }
    async Task<BitmapSource> DecodeThumb(string path){
        string temp=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"FrameNote-thumb-"+Guid.NewGuid().ToString("N")+".png");
        try{
            await Probe.Run(Token,"ffmpeg","-v","error","-nostdin","-i",path,"-map","0:v:0","-frames:v","1","-vf","scale=144:96:force_original_aspect_ratio=decrease","-threads","1",temp);
            using var s=File.OpenRead(temp);var frame=BitmapFrame.Create(s,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);frame.Freeze();return frame;
        }finally{File.Delete(temp);}
    }
    // Ask Explorer for a real thumbnail (including cached movie frames), never a file icon.
    [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFactory{[PreserveSig]int GetImage(long size,uint flags,out nint bitmap);}
    [DllImport("shell32",CharSet=CharSet.Unicode,PreserveSig=true)]
    static extern int SHCreateItemFromParsingName(string path,nint context,in Guid iid,[MarshalAs(UnmanagedType.Interface)]out IFactory item);
    [DllImport("gdi32")]static extern bool DeleteObject(nint handle);
    static BitmapSource? Thumbnail(string path){
        if(SHCreateItemFromParsingName(path,0,typeof(IFactory).GUID,out var item)<0)return null;
        try{
            if(item.GetImage((96L<<32)|144,8,out var bitmap)<0)return null;
            try{var img=Imaging.CreateBitmapSourceFromHBitmap(bitmap,0,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());img.Freeze();return img;}
            finally{DeleteObject(bitmap);}
        }finally{Marshal.ReleaseComObject(item);}
    }
}
