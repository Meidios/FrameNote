using System.IO;
using System.Text.Json;

namespace FrameNote;
internal record UpdatePrefs(bool Automatic=true,string? Skip=null,string? LastRun=null);
internal sealed class UpdateSettings(string root)
{
    string FileName=>Path.Combine(root,"updates.json");
    UpdatePrefs Read(){
        try{return JsonSerializer.Deserialize<UpdatePrefs>(File.ReadAllText(FileName),Review.Json)??new();}
        catch(FileNotFoundException){return new();}catch(DirectoryNotFoundException){return new();}catch(JsonException){return new();}
    }
    internal UpdatePrefs Change(Func<UpdatePrefs,UpdatePrefs> change){
        using var guard=new Mutex(false,@"Local\FrameNote.UpdatePreferences");
        try{guard.WaitOne();}catch(AbandonedMutexException){}
        try{var old=Read();var next=change(old);if(old!=next)Packages.Atomic(FileName,s=>JsonSerializer.Serialize(s,next,Review.Json));return old;}
        finally{guard.ReleaseMutex();}
    }
    internal static Version? Upgraded(string? last,string? installed,Version current){
        if(!Version.TryParse(last??installed,out var old))return null;
        old=new Version(old.Major,old.Minor,Math.Max(0,old.Build));
        return old<current?old:null;
    }
}
