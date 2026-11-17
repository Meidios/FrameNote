using System.IO;
using Microsoft.Web.WebView2.Core;

namespace FrameNote;

internal sealed class ReviewNavigation(string indexPath,string recoveryDirectory)
{
    Uri page=new(Path.GetFullPath(indexPath));

    async Task RecoverPage(CancellationToken token){
        // Only use assets compiled into this build, never files from a review package.
        const string prefix="FrameNote.Recovery/";
        var dest=Path.Combine(recoveryDirectory,Guid.NewGuid().ToString("N"),"ui");
        var assembly=typeof(ReviewNavigation).Assembly;
        foreach(var name in assembly.GetManifestResourceNames().Where(n=>n.StartsWith(prefix,StringComparison.Ordinal))){
            string file=Path.Combine(dest,name[prefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using var input=assembly.GetManifestResourceStream(name)!;
            using var output=File.Create(file);
            await input.CopyToAsync(output,token);
        }
        page=new Uri(Path.Combine(dest,"index.html"));
        if(!File.Exists(page.LocalPath))throw new IOException("无法恢复审阅页，请重新安装 FrameNote。");
    }

    internal bool IsPage(string? address)=>
        address!=null && address.StartsWith("file://",StringComparison.OrdinalIgnoreCase) &&
        Uri.TryCreate(address,UriKind.Absolute,out var uri) && uri.IsFile &&
        uri.Query.Length==0 && uri.Fragment.Length==0 && uri.UserInfo.Length==0 &&
        uri.IdnHost.Equals(page.IdnHost,StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.LocalPath,page.LocalPath,StringComparison.Ordinal);

    internal async Task Load(CoreWebView2 core,CancellationToken token)
    {
        for(int attempt=0;attempt<2;attempt++){
            token.ThrowIfCancellationRequested();
            if(attempt==1)await RecoverPage(token);
            if(!File.Exists(page.LocalPath))continue;
            ulong? target=null;
            var done=new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Starting(object? _,CoreWebView2NavigationStartingEventArgs e){
                if(IsPage(e.Uri))target??=e.NavigationId;
            }
            void Completed(object? _,CoreWebView2NavigationCompletedEventArgs e){
                if(e.NavigationId==target)done.TrySetResult(e);
            }
            core.NavigationStarting+=Starting;core.NavigationCompleted+=Completed;
            try{
                core.Navigate(page.AbsoluteUri);
                var result=await done.Task.WaitAsync(TimeSpan.FromSeconds(30),token);
                if(result.IsSuccess)return;
                if(attempt==0)continue;
                throw new IOException($"无法打开审阅界面：{result.WebErrorStatus}。已尝试从当前用户目录加载。（FrameNote {Updates.Running}，WebView2 {core.Environment.BrowserVersionString}）");
            }catch(TimeoutException e){
                if(attempt==0)continue;
                throw new IOException($"等待本地审阅界面加载超时（WebView2 {core.Environment.BrowserVersionString}）。",e);
            }finally{core.NavigationStarting-=Starting;core.NavigationCompleted-=Completed;}
        }
        throw new IOException("无法读取本地审阅页或恢复副本。");
    }
}
