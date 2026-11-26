global using ShortcutMap = System.Collections.Generic.Dictionary<string, FrameNote.Shortcut[]>;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace FrameNote;

internal enum InputKind { Keyboard, Drag, Click, Wheel, Modifier }
internal readonly record struct Shortcut(int Mods,int Vk,
    [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int With=0)
{
    internal const int Wheel=256,ReverseWheel=257;
    internal static Shortcut Pair(Shortcut first,int second)=>new(first.Mods,Math.Min(first.Vk,second),Math.Max(first.Vk,second));
    internal bool Includes(Shortcut key)=>With!=0 && key.Mods==Mods && (Vk==key.Vk||With==key.Vk);
    internal static Shortcut From(KeyEventArgs e){
        var vk=KeyInterop.VirtualKeyFromKey(e.Key==Key.System?e.SystemKey:e.Key);
        return new((int)Keyboard.Modifiers,vk switch{160 or 161=>16,162 or 163=>17,164 or 165=>18,_=>vk});
    }
    internal string Label(InputKind kind){
        var parts=new List<string>();
        if((Mods&2)!=0)parts.Add("Ctrl");
        if((Mods&1)!=0)parts.Add("Alt");
        if((Mods&4)!=0)parts.Add("Shift");
        if((Mods&8)!=0)parts.Add("Win");
        if(Vk!=0){
            var key=Vk switch{1=>L.Text("鼠标左键"),2=>L.Text("鼠标右键"),4=>L.Text("鼠标中键"),5=>L.Text("鼠标侧键 1"),6=>L.Text("鼠标侧键 2"),Wheel=>L.Text("滚轮"),ReverseWheel=>L.Text("鼠标滚轮 - 反转"),27=>"Esc",13=>"Enter",
                32=>L.Text("空格"),37=>"←",38=>"↑",39=>"→",40=>"↓",46=>"Delete",188=>",",190=>".",>=48 and <=57=>((char)Vk).ToString(),
                _=>new KeyConverter().ConvertToString(KeyInterop.KeyFromVirtualKey(Vk))??Vk.ToString()};
            parts.Add(key+(kind==InputKind.Drag?L.Text("拖拽"):kind==InputKind.Click?L.Text("点击"):""));
        }
        if(With!=0)parts.Add(new Shortcut(0,With).Label(kind));
        return string.Join("+",parts);
    }
}
internal sealed record ShortcutAction(string Id,string Name,string Group,Shortcut Default,InputKind Kind=InputKind.Keyboard){
    internal Shortcut[] Defaults=>Default==default?[]:[Default];
    internal string BindingScope=>Kind==InputKind.Wheel?Group:"";
}

internal static class ShortcutLabels{
    internal static string Label(this Shortcut[] bindings,InputKind kind)=>string.Join(" / ",bindings.Select(b=>b.Label(kind)));
}

internal sealed record EditorPreferences(ShortcutMap Bindings,string RulerUnit,double LensZoom,string[] PresetColors,int DefaultOpacity,ToolbarEntry[] Toolbar){
    internal bool ShowCanvasNumbers {get;init;}=true;
    internal EditorPreferences(ShortcutMap bindings):this(bindings,RulerScale.DefaultUnit,Drawing.DefaultZoom,Note.DefaultColors,80,ToolbarEntry.Defaults()){}
    internal static bool ValidPalette(string[]? colors)=>colors is {Length:>=1 and <=10} &&
        colors.All(c=>c is {Length:7} && c[0]=='#' && c.Skip(1).All(Uri.IsHexDigit)) &&
        colors.Distinct(StringComparer.OrdinalIgnoreCase).Count()==colors.Length;
}

internal sealed class Shortcuts(string root)
{
    static readonly string ReviewGroup=L.Text("文件与批注"),Playback=L.Text("播放与导航"),Timeline=L.Text("时间线"),Canvas=L.Text("画面快速批注");
    internal static readonly ShortcutAction[] Actions=[
        new("open",L.Text("打开文件"),ReviewGroup,new(2,'O')),new("save",L.Text("导出审阅包"),ReviewGroup,new(2,'S')),
        new("paste",L.Text("粘贴图片"),ReviewGroup,new(2,'V')),new("undo",L.Text("撤销"),ReviewGroup,new(2,'Z')),
        new("redo",L.Text("重做"),ReviewGroup,new(6,'Z')),new("point",L.Text("添加点批注"),ReviewGroup,new(0,'M')),
        new("picture",L.Text("添加图片批注"),ReviewGroup,new(0,'P')),new("confirm",L.Text("完成文字编辑"),ReviewGroup,new(2,13)),
        new("cancel",L.Text("取消当前操作 / 退出全屏"),ReviewGroup,new(0,27)),
        new("duplicateNote",L.Text("复制批注"),ReviewGroup,new(2,'D')),
        new("copyFrame",L.Text("复制当前帧到剪贴板"),Playback,new(2,'C')),
        new("fullPreview",L.Text("全屏预览"),Playback,new(0,13)),
        new("rateDown",L.Text("降低播放速度"),Playback,new(0,188)),new("rateUp",L.Text("提高播放速度"),Playback,new(0,190)),
        new("rateReset",L.Text("恢复默认播放速度"),Playback,new(0,188,190)),
        new("play",L.Text("播放 / 暂停"),Playback,new(0,32)),new("prev",L.Text("上一帧 / 上一张图"),Playback,new(0,37)),
        new("next",L.Text("下一帧 / 下一张图"),Playback,new(0,39)),new("prevNote",L.Text("上一条批注"),Playback,new(0,38)),
        new("nextNote",L.Text("下一条批注"),Playback,new(0,40)),
        new("zoomVideo",L.Text("缩放视频画面"),Playback,new(2,Shortcut.Wheel),InputKind.Wheel),
        new("inBtn",L.Text("设置区间进点"),Timeline,new(0,'I')),
        new("outBtn",L.Text("设置区间出点"),Timeline,new(0,'O')),new("splitClip",L.Text("分割片段"),Timeline,new(0,'S')),
        new("joinClip",L.Text("贴到上一片段"),Timeline,default),new("joinNextClip",L.Text("贴到下一片段"),Timeline,default),
        new("deleteClip",L.Text("删除片段"),Timeline,new(0,46)),
        new("linkTracks",L.Text("轨道联动"),Timeline,new(0,'F')),
        new("zoomTimeline",L.Text("缩放时间线"),Timeline,new(2,Shortcut.Wheel),InputKind.Wheel),
        new("panTimeline",L.Text("平移时间线"),Timeline,new(4,Shortcut.Wheel),InputKind.Wheel),
        new("noSnap",L.Text("拖动时暂停吸附"),Timeline,new(1,0),InputKind.Modifier),
        new("quickBox",L.Text("画框"),Canvas,new(2,1),InputKind.Drag),new("quickArrow",L.Text("画箭头"),Canvas,new(2,2),InputKind.Drag),
        new("quickText",L.Text("输入文字批注"),Canvas,new(2,4),InputKind.Click),
        new("cycleColor",L.Text("切换批注预设颜色"),Canvas,new(1,Shortcut.Wheel),InputKind.Wheel),
        new("opacity",L.Text("调整批注不透明度"),Canvas,new(4,Shortcut.Wheel),InputKind.Wheel),
        new("record",L.Text("开始 / 停止录屏（全局）"),L.Text("录屏"),new(3,0x78))
    ];
    internal static ShortcutMap Defaults()=>Actions.ToDictionary(a=>a.Id,a=>a.Defaults);
    internal static string? Problem(ShortcutAction a,Shortcut value){
        if(value.With!=0){
            if(a.Id!="rateReset" || value.With<=value.Vk)return L.Text("这个按键组合不可用。");
            if(Problem(a,new(value.Mods,value.With)) is {} invalid)return invalid;
        }
        if(value.Mods<0 || value.Mods>(a.Kind==InputKind.Keyboard?7:15))return L.Text("这个修饰键组合不可用。");
        if((a.Id is "cycleColor" or "opacity") && value.Mods==0)return L.Text("请选择一个修饰键，普通滚轮用于调整批注。");
        bool valid=a.Kind switch{
            InputKind.Drag or InputKind.Click=>value.Mods!=0 && value.Vk is 1 or 2 or 4 or 5 or 6,
            InputKind.Wheel=>value.Vk is Shortcut.Wheel or Shortcut.ReverseWheel,
            InputKind.Modifier=>value.Mods!=0 && value.Vk==0,
            _=>value.Vk is >=8 and <255 and not (16 or 17 or 18 or 91 or 92 or >=160 and <=165 or 229)
                && KeyInterop.KeyFromVirtualKey(value.Vk)!=Key.None
        };
        if(!valid)return a.Kind is InputKind.Drag or InputKind.Click?L.Text("请按住修饰键并点击一个鼠标键。"):L.Text("这个按键组合不可用。");
        if(a.Id=="record" && value.Mods==0 && value.Vk is <112 or >135)return L.Text("全局录屏请使用组合键或功能键。");
        if(value is {Mods:1,Vk:115 or 9} or {Mods:0,Vk:9})return L.Text("此组合用于窗口或焦点导航，请换一个。");
        return null;
    }
    string FileName=>Path.Combine(root,"shortcuts.json");
    internal static bool Overlap(ShortcutAction a,Shortcut x,ShortcutAction b,Shortcut y){
        if(a.BindingScope!=b.BindingScope)return false;
        if(x==y || a.Kind==InputKind.Wheel && b.Kind==InputKind.Wheel && x.Mods==y.Mods)return true;
        // A speed key may also be half of the reset gesture. Its single action runs on release.
        static bool Speed(ShortcutAction action)=>action.Id is "rateDown" or "rateUp" or "rateReset";
        return (x.With==0 && y.Includes(x) && !Speed(a)) || (y.With==0 && x.Includes(y) && !Speed(b));
    }
    internal static string? Conflict(ShortcutMap bindings){
        var seen=new List<(ShortcutAction Action,Shortcut Key)>();
        foreach(var a in Actions)foreach(var key in bindings.GetValueOrDefault(a.Id)??[]){
            foreach(var item in seen)if(Overlap(a,key,item.Action,item.Key))return L.Format($"「{item.Action.Name}」与「{a.Name}」使用了相同的快捷键。");
            seen.Add((a,key));
        }
        return null;
    }
    internal EditorPreferences Read(){
        var map=Defaults();
        try{
            using var saved=JsonDocument.Parse(File.ReadAllText(FileName));
            if(saved.RootElement.ValueKind!=JsonValueKind.Object)return new(map);
            var unit=saved.RootElement.TryGetProperty("defaultRulerUnit",out var stored)&&stored.ValueKind==JsonValueKind.String?stored.GetString():null;
            double zoom=saved.RootElement.TryGetProperty("defaultLensZoom",out var lens) && lens.ValueKind==JsonValueKind.Number &&
                lens.TryGetDouble(out double value) && Drawing.ValidZoom(value)?value:Drawing.DefaultZoom;
            var colors=saved.RootElement.TryGetProperty("presetColors",out var palette) && palette.ValueKind==JsonValueKind.Array &&
                palette.GetArrayLength()<=10 && palette.EnumerateArray().All(c=>c.ValueKind==JsonValueKind.String)
                ?palette.EnumerateArray().Select(c=>c.GetString()!.ToLowerInvariant()).ToArray():null;
            int opacity=saved.RootElement.TryGetProperty("defaultOpacity",out var alpha) && alpha.ValueKind==JsonValueKind.Number &&
                alpha.TryGetInt32(out var percent) && percent is >=0 and <=100?percent:80;
            var toolbar=ToolbarEntry.Defaults();
            if(saved.RootElement.TryGetProperty("annotationToolbar",out var layout) && layout.ValueKind==JsonValueKind.Array){
                try{toolbar=ToolbarEntry.Normalize(layout.Deserialize<ToolbarEntry[]>(Review.Json));}catch(JsonException){}
            }
            var savedKeys=new ShortcutMap();
            foreach(var a in Actions){
                if(!saved.RootElement.TryGetProperty(a.Id,out var raw))continue;
                // Earlier releases persisted one object per action. An empty array now explicitly unbinds it.
                Shortcut[]? keys=raw.ValueKind switch{
                    JsonValueKind.Array=>raw.Deserialize<Shortcut[]>(Review.Json),
                    JsonValueKind.Object=>[raw.Deserialize<Shortcut>(Review.Json)],
                    _=>null};
                if(keys!=null && keys.All(key=>Problem(a,key)==null))savedKeys[a.Id]=keys;
            }
            if(Conflict(savedKeys)==null){
                foreach(var a in Actions)map[a.Id]=savedKeys.TryGetValue(a.Id,out var keys)?keys:a.Defaults
                    .Where(key=>!Actions.Any(b=>savedKeys.TryGetValue(b.Id,out var other) && other.Any(k=>Overlap(a,key,b,k)))).ToArray();
            }
            return new(map,RulerScale.SupportsUnit(unit)?unit!:RulerScale.DefaultUnit,zoom,EditorPreferences.ValidPalette(colors)?colors!:Note.DefaultColors,opacity,toolbar){
                ShowCanvasNumbers=!saved.RootElement.TryGetProperty("showCanvasNumbers",out var show)||show.ValueKind!=JsonValueKind.False
            };
        }catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){return new(Defaults());}
    }
    internal EditorPreferences Save(EditorPreferences original,EditorPreferences edited,Action<Shortcut[],Shortcut[]> register){
        if(!RulerScale.SupportsUnit(edited.RulerUnit))throw new InvalidDataException(L.Text("请选择有效的标尺单位。"));
        if(!Drawing.ValidZoom(edited.LensZoom)||!EditorPreferences.ValidPalette(edited.PresetColors))
            throw new InvalidDataException(L.Text("请检查放大倍率和预设颜色。"));
        using var guard=new Mutex(false,@"Local\FrameNote.ShortcutPreferences");
        try{guard.WaitOne();}catch(AbandonedMutexException){}
        try{
            var latest=Read();
            var unit=original.RulerUnit==edited.RulerUnit?latest.RulerUnit:edited.RulerUnit;
            var zoom=original.LensZoom==edited.LensZoom?latest.LensZoom:edited.LensZoom;
            var colors=original.PresetColors.SequenceEqual(edited.PresetColors)?latest.PresetColors:edited.PresetColors;
            int opacity=original.DefaultOpacity==edited.DefaultOpacity?latest.DefaultOpacity:Math.Clamp(edited.DefaultOpacity,0,100);
            var toolbar=original.Toolbar.SequenceEqual(edited.Toolbar)?latest.Toolbar:ToolbarEntry.Normalize(edited.Toolbar);
            bool showCanvasNumbers=original.ShowCanvasNumbers==edited.ShowCanvasNumbers?latest.ShowCanvasNumbers:edited.ShowCanvasNumbers;
            var next=latest.Bindings;
            var previousRecord=next["record"];
            foreach(var a in Actions)if(!original.Bindings[a.Id].SequenceEqual(edited.Bindings[a.Id]))next[a.Id]=edited.Bindings[a.Id];
            if(Conflict(next) is {} conflict)throw new InvalidDataException(conflict);
            bool changed=!previousRecord.SequenceEqual(next["record"]);
            if(changed)register(next["record"],previousRecord);
            try{
                var values=next.Where(p=>!DefaultsMap[p.Key].SequenceEqual(p.Value)).ToDictionary(p=>p.Key,p=>(object)p.Value);
                if(unit!=RulerScale.DefaultUnit)values["defaultRulerUnit"]=unit;
                if(zoom!=Drawing.DefaultZoom)values["defaultLensZoom"]=zoom;
                if(!colors.SequenceEqual(Note.DefaultColors))values["presetColors"]=colors;
                if(opacity!=80)values["defaultOpacity"]=opacity;
                if(!toolbar.SequenceEqual(ToolbarEntry.Defaults()))values["annotationToolbar"]=toolbar;
                if(!showCanvasNumbers)values["showCanvasNumbers"]=false;
                Packages.Atomic(FileName,s=>JsonSerializer.Serialize(s,values,Review.Json));
            }
            catch{if(changed)register(previousRecord,previousRecord);throw;}
            return new(next,unit,zoom,colors,opacity,toolbar){ShowCanvasNumbers=showCanvasNumbers};
        }finally{guard.ReleaseMutex();}
    }
    static readonly ShortcutMap DefaultsMap=Defaults();
}
