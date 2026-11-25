using System.IO;
using System.Text.Json;

namespace FrameNote;

internal sealed record ExportBarOptions(bool Enabled,string Background,string Fill)
{
    internal static readonly ExportBarOptions Defaults=new(true,"#000000","#B0B0B0");
}

[Flags]
internal enum ExcelColumns
{
    Number=1, Type=2, Start=4, End=8, Text=16, Preview=32, Media=64, AudioAttachment=128,
    All=Number|Type|Start|End|Text|Preview|Media
}

internal sealed record ExcelTemplate(ExcelColumns Columns,bool ShowSource,bool Zip)
{
    internal static readonly ExcelTemplate Defaults=new(ExcelColumns.All,true,false);
}

internal sealed record OutputSettings(int Fps,string Quality,bool SaveCopy,string CopyDirectory,string ExportDirectory,ExportBarOptions ExportBar,ExcelTemplate Excel,bool HideWhileRecording=true)
{
    static string Folder(Environment.SpecialFolder location,string child)=>
        Path.Combine(Environment.GetFolderPath(location,Environment.SpecialFolderOption.DoNotVerify),"FrameNote",child);
    internal static OutputSettings Defaults=>new(60,"balanced",false,
        Folder(Environment.SpecialFolder.MyVideos,"Recordings"),Folder(Environment.SpecialFolder.MyDocuments,"Exports"),ExportBarOptions.Defaults,ExcelTemplate.Defaults);

    internal static OutputSettings Read(Dictionary<string,JsonElement> fields){
        var defaults=Defaults;
        string? Text(string key)=>fields.TryGetValue(key,out var v) && v.ValueKind==JsonValueKind.String?v.GetString():null;
        string Color(string key,string fallback)=>Text(key) is {Length:7} hex && hex[0]=='#' && hex.Skip(1).All(Uri.IsHexDigit)?hex.ToUpperInvariant():fallback;
        var saved=fields.GetValueOrDefault("excelColumns");
        var columns=saved.ValueKind==JsonValueKind.Number && saved.TryGetInt32(out int mask)?(ExcelColumns)mask & ExcelColumns.All:ExcelColumns.All;
        return new(fields.TryGetValue("fps",out var fps) && fps.ValueKind==JsonValueKind.Number && fps.TryGetInt32(out int n) && n==30?30:60,
            Text("quality")=="high"?"high":"balanced",fields.GetValueOrDefault("saveCopy").ValueKind==JsonValueKind.True,
            Text("copyDirectory")??defaults.CopyDirectory,Text("exportDirectory")??defaults.ExportDirectory,
            new(fields.GetValueOrDefault("exportBarEnabled").ValueKind!=JsonValueKind.False,
                Color("exportBarBackground",defaults.ExportBar.Background),Color("exportBarFill",defaults.ExportBar.Fill)),
            new(columns==0?ExcelColumns.All:columns,fields.GetValueOrDefault("excelShowSource").ValueKind!=JsonValueKind.False,
                fields.GetValueOrDefault("excelZip").ValueKind==JsonValueKind.True),
            fields.GetValueOrDefault("hideWhileRecording").ValueKind!=JsonValueKind.False);
    }

    internal void ApplyChanges(OutputSettings old,Dictionary<string,JsonElement> fields){
        void Set<T>(string key,T value,T previous){
            if(!Equals(value,previous))fields[key]=JsonSerializer.SerializeToElement(value);
        }
        Set("fps",Fps,old.Fps);Set("quality",Quality,old.Quality);Set("saveCopy",SaveCopy,old.SaveCopy);
        Set("hideWhileRecording",HideWhileRecording,old.HideWhileRecording);
        Set("copyDirectory",CopyDirectory,old.CopyDirectory);Set("exportDirectory",ExportDirectory,old.ExportDirectory);
        Set("exportBarEnabled",ExportBar.Enabled,old.ExportBar.Enabled);
        Set("exportBarBackground",ExportBar.Background,old.ExportBar.Background);Set("exportBarFill",ExportBar.Fill,old.ExportBar.Fill);
        Set("excelColumns",(int)Excel.Columns,(int)old.Excel.Columns);
        Set("excelShowSource",Excel.ShowSource,old.Excel.ShowSource);Set("excelZip",Excel.Zip,old.Excel.Zip);
    }

    internal static string DirectoryPath(string value){
        value=value.Trim();
        if(!Path.IsPathFullyQualified(value) || value.IndexOfAny(['*','?','"','<','>','|'])>=0)
            throw new InvalidDataException(L.Text("请输入完整的文件夹路径。"));
        string path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if(File.Exists(path))throw new InvalidDataException(L.Text("所选路径是文件，请选择文件夹。"));
        return path;
    }
}
