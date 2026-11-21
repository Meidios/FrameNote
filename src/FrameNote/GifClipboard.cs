using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace FrameNote;

internal static class GifClipboard
{
    // File-drop consumers open the file at paste time, possibly after FrameNote exits.
    static readonly string DirectoryPath=Path.Combine(Path.GetTempPath(),"FrameNote","Clipboard");

    internal static async Task Publish(string rendered,string stem,CancellationToken token){
        string folder=Directory.CreateDirectory(Path.Combine(DirectoryPath,Guid.NewGuid().ToString("N"))).FullName;
        string name=stem.Length<=160?stem:stem[..(char.IsHighSurrogate(stem[159])?159:160)];
        string path=Path.Combine(folder,name+".gif");
        try{
            File.Move(rendered,path);
            using var stream=File.OpenRead(path);
            var data=new DataObject();
            data.SetFileDropList(new StringCollection{path});
            // WPF's HGLOBAL stream representation is limited to a signed 32-bit length.
            if(stream.Length<=int.MaxValue)data.SetData("image/gif",stream,false);
            for(int attempt=0;;attempt++){
                token.ThrowIfCancellationRequested();
                try{Clipboard.SetDataObject(data,true);break;}
                catch(COMException e) when((uint)e.HResult==0x800401D0 && attempt<6){await Task.Delay(75,token);}
            }
        }catch{File.Delete(path);Directory.Delete(folder);throw;}
        // Keep recent copies for asynchronous paste and clipboard history.
        try{
            foreach(var candidate in new DirectoryInfo(DirectoryPath).EnumerateDirectories()){
                if(candidate.FullName==folder || !Guid.TryParseExact(candidate.Name,"N",out _) ||
                   candidate.Attributes.HasFlag(FileAttributes.ReparsePoint) || candidate.CreationTimeUtc>=DateTime.UtcNow.AddDays(-1))continue;
                foreach(var file in candidate.EnumerateFiles("*.gif"))file.Delete();
                candidate.Delete();
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
