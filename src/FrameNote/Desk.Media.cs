using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FrameNote;
public sealed partial class Desk
{
    internal static readonly string[] ImageFormats=[".png",".apng",".jpg",".jpeg",".jfif",".bmp",".gif",".webp",".avif",".ico",".tif",".tiff"];
    bool viewingImage;
    string? imageFile;
    [System.Runtime.InteropServices.DllImport("shlwapi.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,ExactSpelling=true)]
    static extern int StrCmpLogicalW(string left,string right);
    async Task AdjacentImage(int direction){
        if(!viewingImage || direction is not (-1 or 1))return;
        if(imageFile is not {} source){Send(new{type="status",text=openedPackage?"审阅包中的图片没有可切换的所在文件夹。":"剪贴板图片没有所在文件夹。"});return;}
        var files=await Task.Run(()=>Directory.EnumerateFiles(Path.GetDirectoryName(source)!)
            .Where(p=>ImageFormats.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .Append(source).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName,Comparer<string?>.Create((a,b)=>StrCmpLogicalW(a!,b!)))
            .ThenBy(p=>p,StringComparer.Ordinal).ToArray());
        int i=Array.FindIndex(files,p=>StringComparer.OrdinalIgnoreCase.Equals(p,source))+direction;
        if(i<0 || i>=files.Length){Send(new{type="status",text=Path.GetFileName(source)+(direction<0?" · 已是第一张":" · 已是最后一张")});return;}
        await Open(files[i]);
    }
    int volume=100;
    bool volumeDirty;
    readonly DispatcherTimer volumeWrite=new(){Interval=TimeSpan.FromMilliseconds(250)};
    void InitPlaybackVolume(){
        string file=Path.Combine(root,"volume.json");
        try{if(File.Exists(file))volume=Math.Clamp(JsonSerializer.Deserialize<int>(File.ReadAllText(file)),0,100);}
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){}
        void Save(){
            volumeWrite.Stop();if(!volumeDirty)return;
            try{Packages.Atomic(file,s=>JsonSerializer.Serialize(s,volume));volumeDirty=false;}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){if(IsLoaded && web.CoreWebView2!=null)ReportError("音量已调整，但无法保存设置。");}
        }
        volumeWrite.Tick+=(_,_)=>Save();Closing+=(_,_)=>Save();
    }
    void SetPlaybackVolume(int value){
        value=Math.Clamp(value,0,100);
        if(volume==value)return;
        volume=value;volumeDirty=true;native?.SetVolume(volume);
        if(web.CoreWebView2!=null)Send(new{type="volume",value=volume});
        volumeWrite.Stop();volumeWrite.Start();
    }
    async Task ShowImage(string path,string name,bool fromClipboard=false,string? nextDraft=null,Review? review=null,bool package=false){
        var source=path;
        review??=new Review(2,Guid.NewGuid(),fromClipboard?"剪贴板图片.png":name,0,[]);
        var originalFile=fromClipboard||package?null:path;
        if(Path.GetExtension(path).ToLowerInvariant() is ".tif" or ".tiff"){
            string original=path;
            path=await Task.Run(()=>{
                using var s=File.OpenRead(original);
                var d=new TiffBitmapDecoder(s,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                return StorePreview(d.Frames[0]);
            });
        }
        var src=new Uri(path).AbsoluteUri;
        // Decode before releasing the current review or temporary recording.
        var json=JsonSerializer.Serialize(src);
        var response=await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",JsonSerializer.Serialize(new{
            expression="(async()=>{const i=new Image();i.src="+json+";await i.decode();return i.naturalWidth>0})()",awaitPromise=true,returnByValue=true}));
        using var result=JsonDocument.Parse(response);
        if(!result.RootElement.GetProperty("result").TryGetProperty("value",out var ok)||ok.ValueKind!=JsonValueKind.True)
            throw new InvalidDataException("无法读取图片，请确认文件完整且格式受支持。");
        var previous=activeRecording;activeRecording=null;
        if(native!=null){native.Stop();native.Visibility=Visibility.Collapsed;}
        web.Visibility=Visibility.Visible;
        SetPreviewFullscreen(false);StopTimeline();displayImagePath=path;media=source;draft=nextDraft;preview=null;doc=review;deskLoaded=false;openedPackage=package;playbackInfo=null;playbackId=Guid.NewGuid();
        viewingImage=true;imageFile=originalFile;Title=name+" · FrameNote";Send(new{type="image",src,name,doc,images=new Uri(ImageDirectory+Path.DirectorySeparatorChar).AbsoluteUri,folder=imageFile!=null});
        if(previous!=null)recordings.Discard(previous);
    }
    string StorePreview(BitmapSource source){
        Directory.CreateDirectory(scratch);var file=Path.Combine(scratch,Guid.NewGuid().ToString("N")+".png");
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(source));using var stream=File.Create(file);png.Save(stream);return file;
    }
    async Task PasteAsImage(){
        if(doc!=null||viewingImage)return;
        if(Clipboard.ContainsFileDropList()){
            var paths=Clipboard.GetFileDropList();
            if(paths.Count==1 && paths[0] is string path && ImageFormats.Contains(Path.GetExtension(path).ToLowerInvariant())){await Open(path);return;}
        }
        if(Clipboard.GetData("PNG") is Stream png){
            Directory.CreateDirectory(scratch);var path=Path.Combine(scratch,Guid.NewGuid().ToString("N")+".png");
            if(png.CanSeek)png.Position=0;using(var dst=File.Create(path))png.CopyTo(dst);
            await ShowImage(path,"剪贴板图片",true);return;
        }
        if(Clipboard.GetImage() is {} bitmap){await ShowImage(StorePreview(bitmap),"剪贴板图片",true);return;}
        throw new InvalidDataException("剪贴板中没有可打开的图片。");
    }
}
