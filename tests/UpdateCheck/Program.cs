using FrameNote;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

var folder=Directory.CreateTempSubdirectory("FrameNote-update-").FullName;
var bytes="example installer payload"u8.ToArray();
var hash=Convert.ToHexString(SHA256.HashData(bytes));
var expected="FrameNote-1.10.0-win-x64-Setup.exe";
var url=$"{Updates.Home}/releases/download/v1.10.0/{expected}";
UpdateRelease? Release(bool preview=false,string? digest=null,string? asset=null){
    var obj=new{tag_name="v1.10.0",draft=false,prerelease=preview,body="",assets=new[]{new{name=expected,state="uploaded",browser_download_url=asset??url,size=bytes.Length,digest=digest??("sha256:"+hash)}}};
    return Updates.Parse(JsonSerializer.SerializeToElement(obj));
}
void Assert(bool b){if(!b)throw new Exception("Update verification failed");}
var r=Release()!;
Assert(r.Version>new Version(1,9,9) && r.Notes=="此版本未提供更新说明" && r.Setup!=null && Release(true)==null && Release(digest:"bad")!.Setup==null && Release(asset:"https://example.com/setup.exe")!.Setup==null);
var prefs=new UpdateSettings(folder);
prefs.Change(s=>s with{Skip=r.Version.ToString(),Automatic=false});
Assert(new UpdateSettings(folder).Change(s=>s) is {Skip:"1.10.0",Automatic:false,LastRun:null});
foreach(var (last,installed,yes) in new (string?,string?,bool)[]{(null,null,false),(null,"1.9.9.0",true),("1.10.0","1.9.9.0",false),("1.11.0",null,false)})
    Assert((UpdateSettings.Upgraded(last,installed,r.Version)!=null)==yes);
async Task Fetch(string mode){
    using var cts=new CancellationTokenSource();var client=new HttpClient(new Fake((req,token)=>{
        if(mode=="redirect")return new HttpResponseMessage(HttpStatusCode.Redirect){Headers={Location=new Uri("https://example.org/setup")}};
        if(mode=="cancel")cts.Cancel();
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(mode=="short"?bytes[..4]:bytes)};
    }));
    var svc=new Updates(client);
    try{var file=await svc.Download(mode=="hash"?r with{Hash=new string('0',64)}:r,folder,new Progress<int>(),cts.Token);
        Assert(mode=="ok" && File.ReadAllBytes(file).SequenceEqual(bytes));File.Delete(file);}
    catch(Exception e) when(mode!="ok" && e is (InvalidDataException or OperationCanceledException)){}
    Assert(!Directory.EnumerateFiles(folder,"*.part").Any()&&!Directory.EnumerateFiles(folder,"*.exe").Any());
}
foreach(var mode in new[]{"ok","short","hash","redirect","cancel"})await Fetch(mode);
Console.WriteLine("PASS: version order, prerelease, missing digest, official asset, skip persistence, startup baseline, verified bytes, cancellation, size/hash mismatch and redirect rejection.");
class Fake(Func<HttpRequestMessage,CancellationToken,HttpResponseMessage> f):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken token)=>Task.FromResult(f(req,token));}
