using System.Windows;

namespace FrameNote;

public sealed partial class Desk
{
    Shortcuts shortcutStore=null!;
    ShortcutMap shortcuts=null!;
    string defaultRulerUnit=RulerScale.DefaultUnit;
    double defaultLensZoom=Drawing.DefaultZoom;
    int defaultOpacity=80;
    bool showCanvasNumbers=true;
    ToolbarEntry[] annotationToolbar=ToolbarEntry.Defaults();
    string[] presetColors=Note.DefaultColors;
    void InitShortcuts(){
        shortcutStore=new(root);
        var initial=shortcutStore.Read();
        (shortcuts,defaultRulerUnit,defaultLensZoom,presetColors,defaultOpacity,annotationToolbar)=initial;
        showCanvasNumbers=initial.ShowCanvasNumbers;
        Activated+=(_,_)=>{
            var latest=shortcutStore.Read();
            if(showCanvasNumbers!=latest.ShowCanvasNumbers||defaultOpacity!=latest.DefaultOpacity||!annotationToolbar.SequenceEqual(latest.Toolbar)||defaultRulerUnit!=latest.RulerUnit||defaultLensZoom!=latest.LensZoom||!presetColors.SequenceEqual(latest.PresetColors)||
                !shortcuts.All(p=>latest.Bindings[p.Key].SequenceEqual(p.Value)))ApplyPreferences(latest);
            else if(recordHotkeys.Count!=shortcuts["record"].Length)UpdateRecordHotkey(shortcuts["record"]);
        };
    }
    object ShortcutPayload()=>Shortcuts.Actions.ToDictionary(a=>a.Id,a=>new{
        keys=shortcuts[a.Id],label=shortcuts[a.Id].Label(a.Kind)});
    void SendEditorPreferences()=>Send(new{type="shortcuts",bindings=ShortcutPayload(),defaultRulerUnit,defaultLensZoom,presetColors,defaultOpacity,annotationToolbar,showCanvasNumbers});
    void ToggleCanvasNumbers(){
        var current=shortcutStore.Read();
        var saved=shortcutStore.Save(current,current with{ShowCanvasNumbers=!showCanvasNumbers},UpdateRecordHotkey);
        showCanvasNumbers=saved.ShowCanvasNumbers;
        Send(new{type="canvasNumbers",showCanvasNumbers});
    }
    void ApplyPreferences(EditorPreferences next){
        (shortcuts,defaultRulerUnit,defaultLensZoom,presetColors,defaultOpacity,annotationToolbar)=next;
        showCanvasNumbers=next.ShowCanvasNumbers;
        UpdateRecordHotkey(shortcuts["record"]);
        native?.SetShortcuts(shortcuts);
        titleBar.SetHistoryShortcuts(shortcuts);
        if(webReady)SendEditorPreferences();
    }
    enum SettingsPage { General, Guide, Updates, About }
    void OpenSettings(SettingsPage page=SettingsPage.General){
        var original=shortcutStore.Read();ApplyPreferences(original);
        var previous=prefs.Change(p=>p);
        string language=previous.Language??L.Language;
        var output=OutputSettings.Read(ReadCapturePrefs());
        var dialog=new ShortcutDialog(this,original,previous.Automatic,language,output,(edited,nextAuto,nextLanguage,nextOutput)=>
            prefs.Change(p=>p with{
                Automatic=previous.Automatic==nextAuto?p.Automatic:nextAuto,
                Language=language==nextLanguage?p.Language:nextLanguage},
                ()=>ChangeCapturePrefs(fields=>nextOutput.ApplyChanges(output,fields),
                    ()=>ApplyPreferences(shortcutStore.Save(original,edited,UpdateRecordHotkey)))));
        switch(page){
            case SettingsPage.Guide:dialog.GuideTab.IsSelected=true;break;
            case SettingsPage.Updates:dialog.UpdatesTab.IsSelected=true;break;
            case SettingsPage.About:dialog.AboutTab.IsSelected=true;break;
        }
        dialog.ShowDialog();
    }
    internal void OpenDefaultApps(){
        string name=playerEntry?"FrameNote Player":"FrameNote";
        bool installed=Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\RegisteredApplications",name,null) is string;
        Browse(installed?"ms-settings:defaultapps?registeredAppUser="+Uri.EscapeDataString(name):"ms-settings:defaultapps");
    }
}
