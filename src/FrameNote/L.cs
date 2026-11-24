using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Markup;
using Microsoft.Win32;

namespace FrameNote;

internal static class L
{
    internal static string Language { get; private set; } = "zh-CN";
    internal static string DefaultLanguage { get; private set; } = "zh-CN";
    static readonly Lazy<Dictionary<string,string>> English = new(() => {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("FrameNote.en.json")!;
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    });
    internal static string WebCatalog => Language=="en" ? JsonSerializer.Serialize(English.Value) : "null";

    internal static string InitialLanguage(string? installed,CultureInfo displayLanguage) =>
        installed is "en" or "zh-CN" ? installed :
        displayLanguage.TwoLetterISOLanguageName=="zh" ? "zh-CN" : "en";

    internal static void Initialize(){
        var installed = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Meidios\FrameNote","InstallLanguage",null) as string;
        string initial = DefaultLanguage = InitialLanguage(installed,CultureInfo.CurrentUICulture);
        try{
            var preferences = new UpdateSettings(RuntimeLog.DataDirectory);
            var previous = preferences.Change(p=>p.Language is "en" or "zh-CN" ? p : p with{Language=initial});
            Language = previous.Language is "en" or "zh-CN" ? previous.Language : initial;
        }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){
            Language = initial;
        }
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(Language=="en" ? "en-US" : "zh-CN");
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture;
    }
    internal static string Text(string source) =>
        Language=="en" && English.Value.TryGetValue(source,out var value) ? value : source;
    internal static string Format(FormattableString message) =>
        string.Format(CultureInfo.CurrentCulture,Text(message.Format),message.GetArguments());

    internal static string WithShortcut(string label,string keys) => keys.Length==0 ? label :
        Language=="en" ? $"{label} ({keys})" : $"{label}（{keys}）";

    internal static string ReleaseNotes(string markdown){
        var marker = Regex.Match(markdown,@"(?m)^\**English\**:?[ \t]*\r?$|^\**English\**:[ \t]+");
        if(!marker.Success)return markdown.TrimEnd();
        return Language=="en" ? markdown[(marker.Index+marker.Length)..].Trim() : markdown[..marker.Index].TrimEnd();
    }
}

public sealed class TrExtension(string source) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => L.Text(source);
}
