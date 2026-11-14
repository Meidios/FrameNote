using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using FrameNote;
using ScreenRecorderLib;
using NAudio.CoreAudioApi;
using NAudio.Wave;
class Check
{
    [STAThread] static int Main(string[] args)
    {
        if(args is ["--mixer-tones",var first,var second]){
            using var devices=new MMDeviceEnumerator();
            using var a=devices.GetDevice(first);using var b=devices.GetDevice(second);
            using var left=new WasapiOut(a,AudioClientShareMode.Shared,true,50);
            using var right=new WasapiOut(b,AudioClientShareMode.Shared,true,50);
            static IWaveProvider Tone(double hz)=>new NAudio.Wave.SampleProviders.SignalGenerator(48000,2){
                Frequency=hz,Gain=.05,Type=NAudio.Wave.SampleProviders.SignalGeneratorType.Sin}.ToWaveProvider();
            left.Init(Tone(619));right.Init(Tone(1423));left.Play();right.Play();
            Console.WriteLine("ready");Console.ReadLine();return 0;
        }
        if(args.Length>=4 && args[0]=="--check-mux"){
            CaptureSession.Mux(args[1],args[2],double.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture),args.Skip(4).Select(p=>(p,0))).GetAwaiter().GetResult();return 0;
        }
        var app=new Application();
        var video=new MediaElement{LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Manual,Stretch=Stretch.Fill,IsMuted=true};
        var panel=new Grid();panel.Children.Add(video);
        var flash=new Border{Width=140,Height=100,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Background=Brushes.Black};panel.Children.Add(flash);
        var window=new Window{Content=panel,WindowStyle=WindowStyle.None,Width=1920,Height=1080,ResizeMode=ResizeMode.NoResize,Title="FrameNote capture test",Background=Brushes.Black};
        window.Loaded+=async (_,_)=>{
            try{
                var started=new TaskCompletionSource();
                video.MediaOpened+=(_,_)=>started.TrySetResult();video.MediaFailed+=(_,e)=>started.TrySetException(e.ErrorException);
                video.Source=new Uri(Path.GetFullPath(args[0]));video.MediaEnded+=(_,_)=>{video.Position=TimeSpan.Zero;video.Play();};
                video.Play();await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                foreach(var fps in args.Contains("--60-only")?new[]{60}:new[]{30,60}){
                    var file=Path.GetFullPath(Path.Combine(args[1],$"capture-{fps}.mp4"));var src=new WindowRecordingSource(new WindowInteropHelper(window).Handle){IsCursorCaptureEnabled=false};
                    var outputs=Recorder.GetSystemAudioLoopbackDevices();var system=(args.Contains("--virtual-cable") ? outputs.FirstOrDefault(x=>x.FriendlyName.StartsWith("CABLE Input")) : null)??outputs.FirstOrDefault(x=>x.IsDefaultDevice);
                    var audio=new List<CaptureInput>();
                    if(args.Contains("--system-audio") && system!=null)audio.Add(new(system.DeviceName,true));
                    using var devices=new MMDeviceEnumerator();
                    using var endpoint=system==null?null:devices.GetDevice(system.DeviceName);
                    using var probe=endpoint==null?null:new EndpointProbe(endpoint.ID,Path.Combine(args[1],$"endpoint-{fps}.csv"));
                    using var visual=new StreamWriter(Path.Combine(args[1],$"source-{fps}.csv"));visual.WriteLine("qpc_s,pulse,played_bytes");
                    using var player=endpoint==null?null:new WasapiOut(endpoint,AudioClientShareMode.Shared,true,30);
                    var samples=new byte[48000*2*2];
                    for(int i=0;i<4800;i++){short s=(short)(9000*Math.Sin(i*2*Math.PI*1000/48000));for(int c=0;c<2;c++)BitConverter.TryWriteBytes(samples.AsSpan(i*4+2*c),s);}
                    using var loop=new MemoryStream();for(int i=0;i<180;i++)loop.Write(samples);loop.Position=0;
                    using var wave=new RawSourceWaveStream(loop,new WaveFormat(48000,16,2));
                    player?.Init(wave);player?.Play();Console.WriteLine($"Test tone: {endpoint?.FriendlyName}, muted={endpoint?.AudioEndpointVolume.Mute}, level={endpoint?.AudioEndpointVolume.MasterVolumeLevelScalar}, playback={player?.PlaybackState}");
                    var sw=System.Diagnostics.Stopwatch.StartNew();var clock=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(8)};
                    bool white=false;
                    clock.Tick+=(_,_)=>{
                        long bytes=player?.GetPosition()??0;
                        long ms=args.Contains("--device-clock") ? bytes*1000/player!.OutputWaveFormat.AverageBytesPerSecond : sw.ElapsedMilliseconds;
                        bool next=ms%1000<100;
                        if(next&&!white)visual.WriteLine(FormattableString.Invariant($"{System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency:F7},{ms/1000},{bytes}"));
                        white=next;flash.Background=next?Brushes.White:Brushes.Black;
                    };clock.Start();
                    using var r=new CaptureSession(src,audio,fps,fps==60);
                    r.Start(file);await Task.WhenAny(r.Completion,Task.Delay(args.Contains("--long")?120500:10500));if(!r.Completion.IsCompleted)r.Stop();
                    await r.Completion.WaitAsync(TimeSpan.FromSeconds(60));clock.Stop();player?.Stop();Console.WriteLine($"{fps} fps: {new FileInfo(file).Length} bytes, audio sources {audio.Count}");
                    File.WriteAllText(Path.Combine(args[1],$"video-epoch-{fps}.txt"),(r.VideoEpoch100ns/1e7).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                }
                if(!args.Contains("--60-only"))await Capture.Gif(Path.Combine(args[1],"capture-30.mp4"),1,2,Path.Combine(args[1],"capture.gif"));
                video.Stop();app.Shutdown(0);
            }catch(Exception e){Console.Error.WriteLine(e);app.Shutdown(1);}
        };
        return app.Run(window);
    }
}

