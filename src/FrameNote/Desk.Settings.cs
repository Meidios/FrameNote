using System.Windows;

namespace FrameNote;

public sealed partial class Desk
{
    Shortcuts shortcutStore=null!;
    ShortcutMap shortcuts=null!;
    void InitShortcuts(){
        shortcutStore=new(root);shortcuts=shortcutStore.Read();
        Activated+=(_,_)=>{
            var latest=shortcutStore.Read();
            if(!shortcuts.All(p=>latest[p.Key].SequenceEqual(p.Value)))ApplyShortcuts(latest);
            else if(recordHotkeys.Count!=shortcuts["record"].Length)UpdateRecordHotkey(shortcuts["record"]);
        };
    }
    object ShortcutPayload()=>Shortcuts.Actions.ToDictionary(a=>a.Id,a=>new{
        keys=shortcuts[a.Id],label=shortcuts[a.Id].Label(a.Kind)});
    void ApplyShortcuts(ShortcutMap next){
        shortcuts=next;
        UpdateRecordHotkey(shortcuts["record"]);
        native?.SetShortcuts(next);
        if(webReady)Send(new{type="shortcuts",bindings=ShortcutPayload()});
    }
    void OpenSettings(){
        var original=shortcutStore.Read();ApplyShortcuts(original);
        var dialog=new ShortcutDialog(this,original,
            edited=>ApplyShortcuts(shortcutStore.Save(original,edited,UpdateRecordHotkey)),
            OpenDefaultApps);
        dialog.ShowDialog();
    }
    void OpenDefaultApps(){
        string name=playerEntry?"FrameNote Player":"FrameNote";
        bool installed=Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\RegisteredApplications",name,null) is string;
        Browse(installed?"ms-settings:defaultapps?registeredAppUser="+Uri.EscapeDataString(name):"ms-settings:defaultapps");
    }
}
