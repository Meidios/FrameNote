global using ShortcutMap = System.Collections.Generic.Dictionary<string, FrameNote.Shortcut[]>;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace FrameNote;

internal enum InputKind { Keyboard, Drag, Click, Wheel, Modifier }
internal readonly record struct Shortcut(int Mods,int Vk)
{
    internal const int Wheel=256;
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
            var key=Vk switch{1=>"鼠标左键",2=>"鼠标右键",4=>"鼠标中键",5=>"鼠标侧键 1",6=>"鼠标侧键 2",Wheel=>"滚轮",27=>"Esc",13=>"Enter",
                32=>"空格",37=>"←",38=>"↑",39=>"→",40=>"↓",46=>"Delete",>=48 and <=57=>((char)Vk).ToString(),
                _=>new KeyConverter().ConvertToString(KeyInterop.KeyFromVirtualKey(Vk))??Vk.ToString()};
            parts.Add(key+(kind==InputKind.Drag?"拖拽":kind==InputKind.Click?"点击":""));
        }
        return string.Join("+",parts);
    }
}
internal sealed record ShortcutAction(string Id,string Name,string Group,Shortcut Default,InputKind Kind=InputKind.Keyboard){
    internal Shortcut[] Defaults=>Default==default?[]:[Default];
}

internal static class ShortcutLabels{
    internal static string Label(this Shortcut[] bindings,InputKind kind)=>string.Join(" / ",bindings.Select(b=>b.Label(kind)));
}

internal sealed class Shortcuts(string root)
{
    const string ReviewGroup="文件与批注",Playback="播放与导航",Timeline="时间线",Canvas="画面快速批注";
    internal static readonly ShortcutAction[] Actions=[
        new("open","打开文件",ReviewGroup,new(2,'O')),new("save","导出审阅包",ReviewGroup,new(2,'S')),
        new("paste","粘贴图片",ReviewGroup,new(2,'V')),new("undo","撤销",ReviewGroup,new(2,'Z')),
        new("redo","重做",ReviewGroup,new(6,'Z')),new("point","添加点批注",ReviewGroup,new(0,'M')),
        new("picture","添加图片批注",ReviewGroup,new(0,'P')),new("confirm","完成文字编辑",ReviewGroup,new(2,13)),
        new("cancel","取消当前操作 / 退出全屏",ReviewGroup,new(0,27)),
        new("play","播放 / 暂停",Playback,new(0,32)),new("prev","上一帧 / 上一张图",Playback,new(0,37)),
        new("next","下一帧 / 下一张图",Playback,new(0,39)),new("prevNote","上一条批注",Playback,new(0,38)),
        new("nextNote","下一条批注",Playback,new(0,40)),new("inBtn","设置区间进点",Timeline,new(0,'I')),
        new("outBtn","设置区间出点",Timeline,new(0,'O')),new("splitClip","分割片段",Timeline,new(0,'S')),
        new("joinClip","贴到上一片段",Timeline,default),new("joinNextClip","贴到下一片段",Timeline,default),
        new("deleteClip","删除所选片段",Timeline,new(0,46)),
        new("zoomTimeline","缩放时间线",Timeline,new(2,Shortcut.Wheel),InputKind.Wheel),
        new("panTimeline","平移时间线",Timeline,new(4,Shortcut.Wheel),InputKind.Wheel),
        new("noSnap","拖动时暂停吸附",Timeline,new(1,0),InputKind.Modifier),
        new("quickBox","画框",Canvas,new(2,1),InputKind.Drag),new("quickArrow","画箭头",Canvas,new(2,2),InputKind.Drag),
        new("quickText","输入文字批注",Canvas,new(2,4),InputKind.Click),
        new("record","开始 / 停止录屏（全局）","录屏",new(3,0x78))
    ];
    internal static ShortcutMap Defaults()=>Actions.ToDictionary(a=>a.Id,a=>a.Defaults);
    internal static string? Problem(ShortcutAction a,Shortcut value){
        if(value.Mods<0 || value.Mods>(a.Kind==InputKind.Keyboard?7:15))return "这个修饰键组合不可用。";
        bool valid=a.Kind switch{
            InputKind.Drag or InputKind.Click=>value.Mods!=0 && value.Vk is 1 or 2 or 4 or 5 or 6,
            InputKind.Wheel=>value.Vk==Shortcut.Wheel,
            InputKind.Modifier=>value.Mods!=0 && value.Vk==0,
            _=>value.Vk is >=8 and <255 and not (16 or 17 or 18 or 91 or 92 or >=160 and <=165 or 229)
                && KeyInterop.KeyFromVirtualKey(value.Vk)!=Key.None
        };
        if(!valid)return a.Kind is InputKind.Drag or InputKind.Click?"请按住修饰键并点击一个鼠标键。":"这个按键组合不可用。";
        if(a.Id=="record" && value.Mods==0 && value.Vk is <112 or >135)return "全局录屏请使用组合键或功能键。";
        if(value is {Mods:1,Vk:115 or 9} or {Mods:0,Vk:9})return "此组合用于窗口或焦点导航，请换一个。";
        return null;
    }
    string FileName=>Path.Combine(root,"shortcuts.json");
    internal static string? Conflict(ShortcutMap bindings){
        var owners=new Dictionary<Shortcut,string>();
        foreach(var a in Actions)foreach(var key in bindings[a.Id]){
            if(!owners.TryAdd(key,a.Name))return $"「{owners[key]}」与「{a.Name}」使用了相同的快捷键。";
        }
        return null;
    }
    internal ShortcutMap Read(){
        var map=Defaults();
        try{
            using var saved=JsonDocument.Parse(File.ReadAllText(FileName));
            if(saved.RootElement.ValueKind!=JsonValueKind.Object)return map;
            foreach(var a in Actions){
                if(!saved.RootElement.TryGetProperty(a.Id,out var raw))continue;
                // Earlier releases persisted one object per action. An empty array now explicitly unbinds it.
                Shortcut[]? keys=raw.ValueKind switch{
                    JsonValueKind.Array=>raw.Deserialize<Shortcut[]>(Review.Json),
                    JsonValueKind.Object=>[raw.Deserialize<Shortcut>(Review.Json)],
                    _=>null};
                if(keys!=null && keys.All(key=>Problem(a,key)==null))map[a.Id]=keys;
            }
            return Conflict(map)==null?map:Defaults();
        }catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){return Defaults();}
    }
    internal ShortcutMap Save(ShortcutMap original,ShortcutMap edited,Action<Shortcut[],Shortcut[]> register){
        using var guard=new Mutex(false,@"Local\FrameNote.ShortcutPreferences");
        try{guard.WaitOne();}catch(AbandonedMutexException){}
        try{
            var next=Read();
            var previousRecord=next["record"];
            foreach(var a in Actions)if(!original[a.Id].SequenceEqual(edited[a.Id]))next[a.Id]=edited[a.Id];
            if(Conflict(next) is {} conflict)throw new InvalidDataException(conflict);
            bool changed=!previousRecord.SequenceEqual(next["record"]);
            if(changed)register(next["record"],previousRecord);
            try{Packages.Atomic(FileName,s=>JsonSerializer.Serialize(s,next.Where(p=>!DefaultsMap[p.Key].SequenceEqual(p.Value)).ToDictionary(),Review.Json));}
            catch{if(changed)register(previousRecord,previousRecord);throw;}
            return next;
        }finally{guard.ReleaseMutex();}
    }
    static readonly ShortcutMap DefaultsMap=Defaults();
}
