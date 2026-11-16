using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FrameNote;

public sealed partial class Desk
{
    async Task Snapshot(JsonElement p){
        var save=p.GetProperty("save").GetBoolean();
        await Work(async()=>{
            // Capture the composited video + SVG from the WebView surface. This
            // also works for local media and image attachments on separate origins.
            await web.ExecuteScriptAsync("document.body.classList.add('snapshot')");
            try{
                var raw=await web.ExecuteScriptAsync("""
                    (()=>{const r=overlay.getBoundingClientRect();return {
                      x:r.x+scrollX,y:r.y+scrollY,width:r.width,height:r.height,
                      pixels:v.videoWidth,dpr:devicePixelRatio,us:stamp()}})()
                    """);
                using var json=JsonDocument.Parse(raw);var r=json.RootElement;
                double N(string key)=>r.GetProperty(key).GetDouble();
                double width=N("width"),height=N("height");
                if(width<=0||height<=0||N("pixels")<=0)throw new InvalidOperationException("当前没有可捕获的画面。");
                var args=JsonSerializer.Serialize(new{format="png",fromSurface=true,captureBeyondViewport=true,
                    clip=new{x=N("x"),y=N("y"),width,height,scale=N("pixels")/(width*N("dpr"))}});
                var reply=await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",args);
                using var result=JsonDocument.Parse(reply);
                var png=result.RootElement.GetProperty("data").GetBytesFromBase64();
                var message=await DeliverFrame(this,png,save,doc!.FileName,TimeSpan.FromTicks(r.GetProperty("us").GetInt64()*10));
                Send(new{type="status",text=message});
            }finally{await web.ExecuteScriptAsync("document.body.classList.remove('snapshot')");}
        });
    }
    internal static async Task<string> DeliverFrame(Window owner,byte[] png,bool save,string source,TimeSpan time,CancellationToken token=default){
        if(save){
            var fd=new SaveFileDialog{Filter="PNG 图片|*.png",DefaultExt=".png",
                FileName=Path.GetFileNameWithoutExtension(source)+"_"+time.ToString(@"hh\-mm\-ss\.fff"),Title="当前帧另存为"};
            if(fd.ShowDialog(owner)!=true)return "已取消保存当前帧。";
            Packages.Atomic(fd.FileName,s=>s.Write(png));
            return "当前帧已保存："+fd.FileName;
        }
        using var stream=new MemoryStream(png);
        var bitmap=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        bitmap.Freeze();
        for(int attempt=0;;attempt++){
            token.ThrowIfCancellationRequested();
            try{
                var data=new DataObject();data.SetImage(bitmap);data.SetData("PNG",new MemoryStream(png));
                Clipboard.SetDataObject(data,true);break;
            }catch(System.Runtime.InteropServices.COMException e) when ((uint)e.HResult==0x800401D0 && attempt<6){
                await Task.Delay(75,token);
            }
        }
        return "当前帧已复制，可直接在聊天框按 Ctrl+V 粘贴。";
    }
}
