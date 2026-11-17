using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace FrameNote;

// Explorer starts the static verb once per path. Only the first invocation
// creates a window; its short-lived inbox collects the rest of that selection.
internal static class ShellSelection {
    static readonly string Name="FrameNote.Selection."+Process.GetCurrentProcess().SessionId+"."+
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToUpperInvariant())))[..16];
    internal static int Run(string[] paths){
        paths=paths.Where(Desk.IsViewerMedia).Select(Path.GetFullPath).ToArray();
        if(paths.Length==0)return 1;
        for(int attempt=0;attempt<3;attempt++){
            using var election=new Mutex(false,@"Local\"+Name,out bool first);
            if(!first){
                try{
                    using var pipe=new NamedPipeClientStream(".",Name,PipeDirection.InOut,PipeOptions.CurrentUserOnly);
                    pipe.Connect(2500);
                    var bytes=JsonSerializer.SerializeToUtf8Bytes(paths);
                    using var writer=new BinaryWriter(pipe,Encoding.UTF8,true);
                    writer.Write(bytes.Length);writer.Write(bytes);writer.Flush();
                    if(pipe.ReadByte()==1)return 0;
                }catch(Exception e) when(e is IOException or TimeoutException){}
                continue;
            }
            var app=new Application();DeskTheme.Start(app);
            using var cancel=new CancellationTokenSource();
            var window=new Desk(paths,true);
            var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
            void EndBatch(){timer.Stop();if(!cancel.IsCancellationRequested){cancel.Cancel();election.Dispose();}}
            timer.Tick+=(_,_)=>EndBatch();window.Closed+=(_,_)=>EndBatch();
            var inbox=Listen();
            timer.Start();
            try{return app.Run(window);}
            finally{EndBatch();}

            async Task Listen(){
                var token=cancel.Token;
                try{
                    while(!token.IsCancellationRequested){
                        using var server=new NamedPipeServerStream(Name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                        await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                        try{
                            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
                            deadline.CancelAfter(2500);
                            var header=new byte[4];await server.ReadExactlyAsync(header,deadline.Token).ConfigureAwait(false);
                            int n=BitConverter.ToInt32(header);
                            if(n<=0 || n>1024*1024)continue;
                            var body=new byte[n];await server.ReadExactlyAsync(body,deadline.Token).ConfigureAwait(false);
                            var batch=JsonSerializer.Deserialize<string[]>(body);
                            if(batch==null || batch.Length==0 || batch.Any(p=>p==null || !Path.IsPathFullyQualified(p) || !Desk.IsViewerMedia(p)))continue;
                            await app.Dispatcher.InvokeAsync(()=>{
                                timer.Stop();timer.Start();
                                window.AppendShellSelection(batch);
                            },DispatcherPriority.Normal,token);
                            server.WriteByte(1);server.Flush();
                        }catch(Exception e) when(e is IOException or JsonException or OperationCanceledException){}
                    }
                }catch(OperationCanceledException) when(token.IsCancellationRequested){}
            }
        }
        // A previous batch may have ended between election and connection.
        var fallback=new Application();DeskTheme.Start(fallback);return fallback.Run(new Desk(paths,true));
    }
}
