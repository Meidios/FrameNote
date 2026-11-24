using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace FrameNote;
public sealed partial class Desk
{
    const string ExportOrigin="https://export.framenote.invalid/";
    string? displayImagePath,exportBase;
    Dictionary<string,string>? exportFiles;
    void InitImageExport(){
        var core=web.CoreWebView2;
        core.AddWebResourceRequestedFilter(ExportOrigin+"*",CoreWebView2WebResourceContext.All);
        core.WebResourceRequested+=(_,e)=>{
            string uri=e.Request.Uri;
            if(!uri.StartsWith(ExportOrigin,StringComparison.Ordinal))return;
            // File-origin canvases cannot read local images. Expose only the active
            // export's decoded source file and its validated, content-addressed assets.
            if(exportBase!=null && uri.StartsWith(exportBase,StringComparison.Ordinal) && e.Request.Method=="GET" &&
               exportFiles?.TryGetValue(uri[exportBase.Length..],out var path)==true){
                try{
                    string mime=Path.GetExtension(path).ToLowerInvariant() switch {
                        ".jpg" or ".jpeg" or ".jfif"=>"image/jpeg",".gif"=>"image/gif",".webp"=>"image/webp",".bmp"=>"image/bmp",
                        ".avif"=>"image/avif",".ico"=>"image/x-icon",_=>"image/png"};
                    e.Response=core.Environment.CreateWebResourceResponse(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete),200,"OK",
                        $"Content-Type: {mime}\r\nAccess-Control-Allow-Origin: *\r\nCache-Control: no-store");return;
                }catch(IOException){}catch(UnauthorizedAccessException){}
            }
            e.Response=core.Environment.CreateWebResourceResponse(null,404,"Not Found","");
        };
    }
    async Task<JsonElement> ExportJs(string expression){
        var reply=await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",JsonSerializer.Serialize(new{expression,awaitPromise=true,returnByValue=true}));
        using var json=JsonDocument.Parse(reply);
        if(json.RootElement.TryGetProperty("exceptionDetails",out _) || !json.RootElement.GetProperty("result").TryGetProperty("value",out var value))
            throw new InvalidDataException(L.Text("无法生成原尺寸批注图。请确认原图和批注附图可读，且有足够内存。"));
        return value.Clone();
    }
    async Task ExportImage(bool save){
        if(!viewingImage || doc==null || displayImagePath==null)return;
        string? destination=null;
        if(save){
            var fd=new SaveFileDialog{Filter=L.Text("PNG 图片|*.png"),DefaultExt=".png",FileName=Path.GetFileNameWithoutExtension(doc.FileName)+L.Text("_批注.png"),Title=L.Text("批注图另存为 PNG")};
            if(fd.ShowDialog(this)!=true){Send(new{type="status",text=L.Text("已取消保存批注图。")});return;}
            if(string.Equals(fd.FileName,media,StringComparison.OrdinalIgnoreCase))throw new IOException(L.Text("请选择其他文件名，以保留未合成的原图。"));
            destination=fd.FileName;
        }
        Directory.CreateDirectory(scratch);
        string tmp=Path.Combine(scratch,Guid.NewGuid().ToString("N")+".png");
        using(var stream=new FileStream(tmp,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,131072,FileOptions.DeleteOnClose)){
            await RenderAnnotatedImage(await OriginalImage(),doc.Notes,stream,false);
            stream.Position=0;
            if(destination!=null){
                await Task.Run(()=>Packages.Atomic(destination,s=>stream.CopyTo(s)));
                Send(new{type="status",text=L.Text("批注图已保存：")+destination});
            }else{
                using var png=doc.Notes.Count>0
                    ?new MemoryStream(FrameDetails.Append(stream,doc.FileName,null,doc.Notes,doc.RulerScales,defaultRulerUnit))
                    :new MemoryStream();
                if(doc.Notes.Count==0)await stream.CopyToAsync(png);
                png.Position=0;
                var bitmap=BitmapFrame.Create(png,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);bitmap.Freeze();png.Position=0;
                var data=new DataObject();data.SetImage(bitmap);data.SetData("PNG",png);Clipboard.SetDataObject(data,true);
                Send(new{type="status",text=L.Text("批注图已复制，可直接在聊天框按 Ctrl+V 粘贴。")});
            }
        }
    }
    async Task RenderAnnotatedImage(string source,IReadOnlyList<Note> notes,Stream stream,bool videoFrame){
        await EnsureReviewWeb();
        if(exportBase!=null)throw new InvalidOperationException(L.Text("上一张图片仍在导出，请稍后再试。"));
        exportFiles=notes.SelectMany(n=>n.Pictures??[]).Select(p=>p.Asset).Distinct()
            .ToDictionary(hash=>hash+".png",hash=>ReviewImages.PathFor(ImageDirectory,hash));
        exportFiles["source"]=source;exportBase=ExportOrigin+Guid.NewGuid().ToString("N")+"/";
        try{
            var args=JsonSerializer.Serialize(new object?[]{exportBase,videoFrame?notes:null,doc?.RulerScales,defaultRulerUnit},Review.Json);
            var size=(await ExportJs("imageExport.prepare(..."+args+")")).GetInt64();
            if(size<=0)throw new InvalidDataException(L.Text("PNG 内容为空。"));
            // Bound each WebView message even for a large, incompressible photo.
            for(long offset=0;offset<size;){
                int count=(int)Math.Min(768*1024,size-offset);
                var bytes=(await ExportJs($"imageExport.chunk({offset},{count})")).GetBytesFromBase64();
                if(bytes.Length!=count)throw new IOException(L.Text("PNG 数据不完整。"));
                await stream.WriteAsync(bytes);offset+=count;
            }
        }finally{exportBase=null;exportFiles=null;await web.ExecuteScriptAsync("imageExport.release()");}
    }
}
