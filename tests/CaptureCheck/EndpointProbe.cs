using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

// Independent WASAPI observer: timestamps the pulse sample, not delivery of the callback.
// Stores only edge times, never audio data.
sealed class EndpointProbe : IDisposable
{
    readonly CancellationTokenSource stop=new();
    readonly Task worker;
    public EndpointProbe(string device,string path)
    {
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        worker=Task.Factory.StartNew(()=>{
            try{
                using var devices=new MMDeviceEnumerator();using var ep=devices.GetDevice(device);
                using var client=ep.AudioClient;
                var fmt=WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
                client.Initialize(AudioClientShareMode.Shared,AudioClientStreamFlags.Loopback | AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,1_000_000,0,fmt,Guid.Empty);
                var input=client.AudioCaptureClient;
                using var log=new StreamWriter(path);
                log.WriteLine("qpc_s,device_frame,flags");
                var buf=Array.Empty<float>();long last=-48000;
                client.Start();ready.SetResult();
                try{while(!stop.IsCancellationRequested){
                    while(input.GetNextPacketSize()>0){
                        var ptr=input.GetBuffer(out int n,out var flags,out long pos,out long qpc);
                        try{
                            if((flags & AudioClientBufferFlags.TimestampError)!=0)throw new InvalidDataException("Unreliable endpoint timestamp");
                            if((flags & AudioClientBufferFlags.Silent)!=0)continue;
                            if(buf.Length<n*2)buf=new float[n*2];Marshal.Copy(ptr,buf,0,n*2);
                            for(int i=0;i<n;i++)if(Math.Abs(buf[i*2])>0.04 && pos+i-last>24000){
                                last=pos+i;
                                log.WriteLine(FormattableString.Invariant($"{qpc/1e7+i/48000.0:F7},{last},{(int)flags}"));
                            }
                        }finally{input.ReleaseBuffer(n);}
                    }
                    Thread.Sleep(3);
                }}finally{client.Stop();}
            }catch(Exception ex){ready.TrySetException(ex);throw;}
        },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        ready.Task.GetAwaiter().GetResult();
    }
    public void Dispose(){stop.Cancel();try{worker.GetAwaiter().GetResult();} finally{stop.Dispose();}}
}
