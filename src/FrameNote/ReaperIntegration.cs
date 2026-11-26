using System.Diagnostics;
using System.IO;
using System.Text;

namespace FrameNote;

internal sealed class ReaperIntegration(string profile)
{
    internal const string ActionName="FrameNote - Import review.lua";
    internal static string Bundled=>Path.Combine(AppContext.BaseDirectory,"reaper",ActionName);
    string InstallRecord=>Path.Combine(profile,"reaper-action.txt");
    string Preference=>Path.Combine(profile,"reaper-folder.txt");
    static void Write(string path,byte[] bytes){
        if(!File.Exists(path)||!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            Packages.Atomic(path,f=>f.Write(bytes));
    }
    internal string? Installed{
        get{
            var path=File.Exists(InstallRecord)?File.ReadAllText(InstallRecord).Trim():"";
            return Path.IsPathFullyQualified(path) && Path.GetFileName(path).Equals(ActionName,StringComparison.OrdinalIgnoreCase)?path:null;
        }
    }
    internal string? CustomFolder{
        get{
            if(!File.Exists(Preference))return Path.GetDirectoryName(Installed);
            var folder=File.ReadAllText(Preference).Trim();
            return Path.IsPathFullyQualified(folder)?folder:null;
        }
    }
    internal string TargetFolder{
        get{
            if(CustomFolder is {} folder)return folder;
            string standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"REAPER","Scripts");
            var active=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var processes=Process.GetProcessesByName("reaper");
            try{
                using var self=Process.GetCurrentProcess();
                foreach(var process in processes){
                    try{
                        if(process.SessionId!=self.SessionId)continue;
                        var root=Path.GetDirectoryName(process.MainModule?.FileName);
                        if(root!=null)active.Add(File.Exists(Path.Combine(root,"reaper.ini"))?Path.Combine(root,"Scripts"):standard);
                    }catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception){}
                }
            }finally{foreach(var process in processes)process.Dispose();}
            if(active.Count==1)return active.Single();
            var last=Path.GetDirectoryName(Installed);
            if(last!=null && File.Exists(Path.Combine(last,"..","reaper.ini")))return last;
            return standard;
        }
    }
    internal void SetFolder(string? path)=>Write(Preference,Encoding.UTF8.GetBytes(path??""));
    internal bool Current(string script)=>File.Exists(script) && File.Exists(script+".app") &&
        File.ReadAllBytes(script).AsSpan().SequenceEqual(File.ReadAllBytes(Bundled)) &&
        string.Equals(File.ReadAllText(script+".app"),Environment.ProcessPath,StringComparison.OrdinalIgnoreCase);
    static void RemoveFiles(string path){
        if(string.Equals(Path.GetFullPath(path),Path.GetFullPath(Bundled),StringComparison.OrdinalIgnoreCase))return;
        File.Delete(path);File.Delete(path+".app");
    }
    internal string Install(string folder){
        Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,ActionName);
        if(string.Equals(Path.GetFullPath(path),Path.GetFullPath(Bundled),StringComparison.OrdinalIgnoreCase))
            throw new IOException(L.Text("请选择 REAPER 的脚本目录。"));
        var previous=Installed;
        if(!File.Exists(Preference))SetFolder(CustomFolder);
        Write(path,File.ReadAllBytes(Bundled));
        Write(path+".app",Encoding.UTF8.GetBytes(Environment.ProcessPath!));
        Write(InstallRecord,Encoding.UTF8.GetBytes(path));
        if(previous!=null && !previous.Equals(path,StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.GetDirectoryName(previous)))RemoveFiles(previous);
        return path;
    }
    internal void Uninstall(string path){
        if(!File.Exists(Preference))SetFolder(CustomFolder);
        if(Directory.Exists(Path.GetDirectoryName(path)))RemoveFiles(path);
        if(string.Equals(path,Installed,StringComparison.OrdinalIgnoreCase))File.Delete(InstallRecord);
    }
}
