namespace FrameNote;

internal sealed record ToolbarEntry(string Id,bool Visible=true,bool Wide=false)
{
    internal const string Separator="separator";
    internal static readonly Dictionary<string,string> Names=new(){
        ["point"]="点批注",["arrow"]="箭头",["box"]="框选",["line"]="线",["brush"]="画笔",["angle"]="角度",
        ["toolRuler"]="距离标尺",["magnifier"]="放大镜",["text"]="文字框",["voiceRecord"]="语音批注",
        ["addAudio"]="音频批注",["addPicture"]="添加图片",["removePicture"]="移除图片",["clear"]="清除图形"
    };
    internal static ToolbarEntry[] Defaults()=>Names.Keys.Select(id=>new ToolbarEntry(id)).ToArray();
    internal static ToolbarEntry[] Normalize(ToolbarEntry[]? saved){
        var seen=new HashSet<string>();
        var entries=new List<ToolbarEntry>();
        foreach(var item in saved??[]){
            if(item?.Id==Separator)entries.Add(new(Separator));
            else if(item?.Id!=null && Names.ContainsKey(item.Id) && seen.Add(item.Id))entries.Add(item);
        }
        entries.AddRange(Defaults().Where(entry=>!seen.Contains(entry.Id)));
        return [..entries];
    }
}
