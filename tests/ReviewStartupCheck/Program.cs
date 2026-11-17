using System.IO;
using System.Windows;
using FrameNote;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class Program
{
    [STAThread] static int Main(){
        var dir=Directory.CreateTempSubdirectory("FrameNote-startup-").FullName;
        var app=new Application();using var web=new WebView2();
        var window=new Window{Content=web,Width=300,Height=180,ShowActivated=false};
        window.Loaded+=async (_,_)=>{
            try{
                string file=Path.Combine(dir,"同事's [#100%]","index.html");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file,"<title>review ready</title>");
                var policy=new ReviewNavigation(file,Path.Combine(dir,"recovered"));
                string url=new Uri(file).AbsoluteUri;
                if(!policy.IsPage(url.Replace("'","%27")) || policy.IsPage(url+"?other") || policy.IsPage(url+"#other") ||
                    policy.IsPage(url+".bak") || policy.IsPage("https://example.invalid/index.html"))throw new Exception("Unexpected page identity");
                await web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(dir,"browser")));
                var core=web.CoreWebView2;
                var finished=new List<(ulong Id,bool Success,CoreWebView2WebErrorStatus Status)>();
                core.NavigationCompleted+=(_,e)=>finished.Add((e.NavigationId,e.IsSuccess,e.WebErrorStatus));
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var overlap=new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
                int cancel=0,attempts=0;ulong unrelated=0;
                core.NavigationStarting+=(_,e)=>{
                    if(!policy.IsPage(e.Uri)){
                        e.Cancel=true;unrelated=e.NavigationId;
                        overlap.TrySetResult(policy.Load(core,timeout.Token));
                    }else{attempts++;if(cancel>0){e.Cancel=true;cancel--;}}
                };
                core.Navigate("about:blank");
                await await overlap.Task.WaitAsync(timeout.Token);
                if(finished[0].Id!=unrelated || finished[0].Status!=CoreWebView2WebErrorStatus.OperationCanceled ||
                    attempts!=1 || core.DocumentTitle!="review ready")throw new Exception("Unrelated cancellation was not isolated");
                cancel=1;attempts=0;await policy.Load(core,timeout.Token);
                if(attempts!=2 || !policy.IsPage(core.Source) || policy.IsPage(url) ||
                    await core.ExecuteScriptAsync("typeof imageView.show")!="\"function\"")throw new Exception("Bundled recovery UI did not start");
                cancel=2;attempts=0;
                try{await policy.Load(core,timeout.Token);throw new Exception("Permanent cancellation reported success");}
                catch(IOException e) when(attempts==2 && e.Message.Contains("OperationCanceled")){}
                Console.WriteLine("PASS: unrelated cancellation arrives first, target-only retry, persistent failure and page identity.");
                app.Shutdown(0);
            }catch(Exception e){Console.Error.WriteLine(e);app.Shutdown(1);}
        };
        return app.Run(window);
    }
}
