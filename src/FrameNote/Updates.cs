using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FrameNote;

internal record UpdateRelease(Version Version,string Notes,Uri Page,Uri? Setup,long Size,string? Hash);
internal sealed class Updates
{
    internal const string Home="https://github.com/Meidios/FrameNote";
    internal static readonly Version Running=Version.Parse(typeof(Updates).Assembly.GetName().Version!.ToString(3));
    static readonly HttpClient Network=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(45)};
    readonly HttpClient client;
    internal Updates(HttpClient? http=null)=>client=http??Network;
    static bool Official(Uri u)=>u.IsAbsoluteUri && u.Scheme=="https" && u.IsDefaultPort && u.UserInfo=="" && u.Host is ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com");
    internal static UpdateRelease? Parse(JsonElement release){
        string? S(JsonElement obj,string key)=>obj.TryGetProperty(key,out var x) && x.ValueKind==JsonValueKind.String?x.GetString():null;
        if(release.GetProperty("draft").GetBoolean()||release.GetProperty("prerelease").GetBoolean())return null;
        var tag=S(release,"tag_name")??"";
        if(!Regex.IsMatch(tag,@"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")||!Version.TryParse(tag[1..],out var version))throw new InvalidDataException("发布版本号无效。");
        string name=$"FrameNote-{version}-win-x64-Setup.exe";
        Uri page=new($"{Home}/releases/tag/{tag}"),asset=new($"{Home}/releases/download/{tag}/{name}");
        var notes=S(release,"body");UpdateRelease result=new(version,string.IsNullOrWhiteSpace(notes)?"此版本未提供更新说明":notes,page,null,0,null);
        foreach(var a in release.GetProperty("assets").EnumerateArray()){
            var hash=S(a,"digest")??"";
            if(S(a,"name")==name && S(a,"state")=="uploaded" && S(a,"browser_download_url")==asset.AbsoluteUri && a.TryGetProperty("size",out var n)
                && n.TryGetInt64(out var size) && size is >0 and <=2147483648L && Regex.IsMatch(hash,"^sha256:[a-fA-F0-9]{64}$"))
                return result with{Setup=asset,Size=size,Hash=hash[7..]};
        }
        return result;
    }
    async Task<HttpResponseMessage> Get(Uri uri,CancellationToken token){
        using var req=new HttpRequestMessage(HttpMethod.Get,uri);
        req.Headers.UserAgent.ParseAdd("FrameNote/"+Running);
        return await client.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,token);
    }
    internal async Task<UpdateRelease?> Latest(CancellationToken token){
        using var response=await Get(new("https://api.github.com/repos/Meidios/FrameNote/releases/latest"),token);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(4*1024*1024).WaitAsync(token);
        using var json=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
        return Parse(json.RootElement);
    }
    internal async Task<string> Download(UpdateRelease release,string folder,IProgress<int> progress,CancellationToken token){
        var uri=release.Setup??throw new InvalidOperationException("没有可校验的 x64 安装包。");
        Directory.CreateDirectory(folder);
        var target=Path.Combine(folder,$"FrameNote-{release.Version}-{Guid.NewGuid():N}-Setup.exe");
        string part=target+".part";
        try{
            for(int i=0;i<6;i++){
                if(!Official(uri))throw new InvalidDataException("下载来源不属于官方发布渠道。");
                using var response=await Get(uri,token);
                if((int)response.StatusCode is >=300 and <=399 && response.Headers.Location is {} next){uri=new(uri,next);continue;}
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength is long len && len!=release.Size)throw new InvalidDataException("安装包大小与发布信息不符。");
                using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using(var output=new FileStream(part,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true)){
                    using var input=await response.Content.ReadAsStreamAsync(token);
                    var buf=new byte[131072];long received=0;int last=-1;
                    while(true){int read=await input.ReadAsync(buf,token);if(read==0)break;
                        received+=read;if(received>release.Size)throw new InvalidDataException("安装包超出预期大小。");
                        hash.AppendData(buf,0,read);await output.WriteAsync(buf.AsMemory(0,read),token);
                        int pc=(int)(100*received/release.Size);if(pc!=last){progress.Report(pc);last=pc;}
                    }
                    if(received!=release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Hash,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("安装包大小或 SHA-256 校验失败。");
                }
                token.ThrowIfCancellationRequested();File.Move(part,target);return target;
            }
            throw new InvalidDataException("下载重定向过多。");
        }finally{File.Delete(part);}
    }
}
