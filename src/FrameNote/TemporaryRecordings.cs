using System.IO;

namespace FrameNote;

// A separate, leased folder per desk: a crashed desk can be cleaned up on the
// next launch without touching another running instance or a user's export.
internal sealed class TemporaryRecordings : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "FrameNote", "Recordings");
    readonly string folder;
    FileStream? lease;
    public TemporaryRecordings() {
        folder=Path.Combine(root,Guid.NewGuid().ToString("N"));
        if(Directory.Exists(root))
            foreach(var dir in Directory.GetDirectories(root))
                if(IsId(Path.GetFileName(dir))) Clean(dir);
    }
    public string NewPath() {
        if(lease==null){
            Directory.CreateDirectory(folder);
            lease=new FileStream(Path.Combine(folder,"session.lock"),FileMode.CreateNew,FileAccess.Write,FileShare.None);
        }
        return Path.Combine(folder,Guid.NewGuid().ToString("N")+".mp4");
    }
    public void Discard(string path) {
        if(!string.Equals(Path.GetDirectoryName(path),folder,StringComparison.OrdinalIgnoreCase) || !IsClip(path))return;
        try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    static bool IsId(string s)=>Guid.TryParseExact(s,"N",out _);
    static bool IsClip(string p)=>Path.GetExtension(p)==".mp4" && IsId(Path.GetFileNameWithoutExtension(p));
    static bool IsLink(string p)=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0;
    static void Clean(string dir){
        var marker=Path.Combine(dir,"session.lock");
        try{
            if(IsLink(dir))return;
            using(var claim=new FileStream(marker,FileMode.Open,FileAccess.Write,FileShare.None)){
                foreach(var p in Directory.GetFiles(dir))if(IsClip(p))File.Delete(p);
                foreach(var stage in Directory.GetDirectories(dir,".framenote-*")){
                    if(!IsId(Path.GetFileName(stage)[".framenote-".Length..])||IsLink(stage))continue;
                    foreach(var name in new[]{"video.mp4","complete.mp4","audio-0.mka","audio-1.mka"})File.Delete(Path.Combine(stage,name));
                    Directory.Delete(stage,false);
                }
            }
            File.Delete(marker);Directory.Delete(dir,false);
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public void Dispose(){if(lease!=null){lease.Dispose();Clean(folder);}}
}
