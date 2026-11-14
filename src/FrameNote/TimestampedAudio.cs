using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NEbml.Core;

namespace FrameNote;

// Matroska PCM retains the WASAPI acquisition time of each block. WAV cannot
// represent these timestamps, especially after a silent period or device glitch.
sealed class TimestampedAudio : IDisposable
{
    const int Rate=48000;
    readonly CancellationTokenSource stop=new();
    readonly Task worker;
    public string Path { get; }
    public bool HasPackets { get; private set; }
    public int Channels { get; private set; }

    public static long Now100ns() => (long)(Stopwatch.GetTimestamp()*(10_000_000.0/Stopwatch.Frequency));

    public TimestampedAudio(string id,bool loopback,string path,long epoch)
    {
        Path=path;
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        worker=Task.Factory.StartNew(()=>{
            try {
                using var devices=new MMDeviceEnumerator();
                using var device=devices.GetDevice(id);
                using var client=device.AudioClient;
                Channels=loopback?2:Math.Clamp(client.MixFormat.Channels,1,2);
                var format=new WaveFormat(Rate,16,Channels);
                var flags=AudioClientStreamFlags.AutoConvertPcm|AudioClientStreamFlags.SrcDefaultQuality;
                if(loopback)flags|=AudioClientStreamFlags.Loopback;
                client.Initialize(AudioClientShareMode.Shared,flags,1_000_000,0,format,Guid.Empty);
                var capture=client.AudioCaptureClient;
                using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);
                var writer=new EbmlWriter(file);
                Header(writer,Channels);
                var bytes=Array.Empty<byte>();long previousQpc=-1,previousDevice=0;
                client.Start();ready.SetResult();
                try {
                    while(!stop.IsCancellationRequested){
                        while(capture.GetNextPacketSize()>0){
                            var ptr=capture.GetBuffer(out var count,out var state,out long position,out long qpc);
                            try {
                                if((state & AudioClientBufferFlags.TimestampError)!=0){
                                    // Bad timestamps must never reposition the stream. Interpolate only from a valid anchor.
                                    if(previousQpc<0||position<previousDevice)continue;
                                    qpc=previousQpc+(position-previousDevice)*10_000_000/Rate;
                                }
                                if(qpc<epoch || qpc<=previousQpc || count==0)continue;
                                previousQpc=qpc;previousDevice=position;
                                int length=checked(count*format.BlockAlign);
                                if(bytes.Length<length+4)bytes=new byte[length+4];
                                bytes[0]=0x81;bytes[1]=bytes[2]=0;bytes[3]=0x80; // track 1, cluster-relative time 0, keyframe
                                if((state&AudioClientBufferFlags.Silent)!=0)Array.Clear(bytes,4,length);
                                else Marshal.Copy(ptr,bytes,4,length);
                                using var cluster=writer.StartMasterElement(Id(0x1F43B675));
                                cluster.Write(Id(0xE7),(ulong)((qpc-epoch)/10)); // microsecond time base
                                cluster.Write(Id(0xA3),bytes,0,length+4);HasPackets=true;
                            } finally {capture.ReleaseBuffer(count);}
                        }
                        stop.Token.WaitHandle.WaitOne(4);
                    }
                } finally {client.Stop();}
            }catch(Exception e){ready.TrySetException(e);throw;}
        },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        try {ready.Task.GetAwaiter().GetResult();}catch{stop.Dispose();throw;}
    }
    static VInt Id(uint id)=>VInt.FromEncoded(id);
    static void Header(EbmlWriter w,int channels){
        using(var h=w.StartMasterElement(Id(0x1A45DFA3))){
            h.WriteAscii(Id(0x4282),"matroska");h.Write(Id(0x4287),4UL);h.Write(Id(0x4285),2UL);
        }
        w.WriteElementHeader(Id(0x18538067),VInt.UnknownSize(8));
        using(var info=w.StartMasterElement(Id(0x1549A966))){
            info.Write(Id(0x2AD7B1),1000UL);info.WriteUtf(Id(0x4D80),"FrameNote");info.WriteUtf(Id(0x5741),"FrameNote");
        }
        using var tracks=w.StartMasterElement(Id(0x1654AE6B));
        using var entry=tracks.StartMasterElement(Id(0xAE));
        entry.Write(Id(0xD7),1UL);entry.Write(Id(0x73C5),1UL);entry.Write(Id(0x83),2UL);entry.WriteAscii(Id(0x86),"A_PCM/INT/LIT");
        using var audio=entry.StartMasterElement(Id(0xE1));
        audio.Write(Id(0xB5),(double)Rate);audio.Write(Id(0x9F),(ulong)channels);audio.Write(Id(0x6264),16UL);
    }
    public void Dispose(){stop.Cancel();try{worker.GetAwaiter().GetResult();}finally{stop.Dispose();}}
}
