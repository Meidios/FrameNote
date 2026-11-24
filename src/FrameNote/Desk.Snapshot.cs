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
            if(media==null||doc==null||viewingImage||p.GetProperty("playbackId").GetGuid()!=playbackId)return;
            long us=p.GetProperty("us").GetInt64();
            var ids=p.GetProperty("ids").EnumerateArray().Select(id=>id.GetGuid()).ToHashSet();
            var notes=doc.Notes.Where(n=>ids.Contains(n.Id)).ToArray();
            Directory.CreateDirectory(scratch);
            var file=Path.Combine(scratch,Guid.NewGuid().ToString("N")+".png");
            try{
                await Probe.DecodeFrame(await EditedMedia(lifetime.Token),us,file,lifetime.Token);
                var message=await DeliverVideoFrame(file,save,doc.FileName,TimeSpan.FromTicks(us*10),notes);
                Send(new{type="status",text=message});
            }finally{File.Delete(file);}
        });
    }
    internal async Task<string> DeliverVideoFrame(string file,bool save,string source,TimeSpan time,IReadOnlyList<Note> notes,CancellationToken token=default){
        byte[] png;
        if(notes.Count==0)png=await File.ReadAllBytesAsync(file,token);
        else{
            using var annotated=new MemoryStream();
            await RenderAnnotatedImage(file,notes,annotated,true);
            annotated.Position=0;
            png=FrameDetails.Append(annotated,source,time,notes,doc?.RulerScales,defaultRulerUnit);
        }
        token.ThrowIfCancellationRequested();
        if(save){
            var fd=new SaveFileDialog{Filter=L.Text("PNG 图片|*.png"),DefaultExt=".png",
                FileName=Path.GetFileNameWithoutExtension(source)+"_"+time.ToString(@"hh\-mm\-ss\.fff"),Title=L.Text("当前帧另存为")};
            if(fd.ShowDialog(this)!=true)return L.Text("已取消保存当前帧。");
            Packages.Atomic(fd.FileName,s=>s.Write(png));
            return L.Text("当前帧已保存：")+fd.FileName;
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
        return L.Text("当前帧已复制，可直接在聊天框按 Ctrl+V 粘贴。");
    }
}
