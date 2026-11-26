using System.Globalization;
using System.IO;

namespace FrameNote;

public sealed partial class Desk
{
    CancellationTokenSource? timelineJob;
    CancellationTokenSource? voiceWaveJob;

    void StopTimeline(){timelineJob?.Cancel();timelineJob=null;voiceWaveJob?.Cancel();}

    async Task SendVoiceWave(System.Text.Json.JsonElement request){
        var id=request.GetProperty("playbackId").GetGuid();
        if(id!=playbackId||viewingImage)return;
        string asset=request.GetProperty("asset").GetString()!;
        string? format=request.GetProperty("format").GetString();
        long duration=request.GetProperty("durationUs").GetInt64();
        if(asset.Length!=64||!asset.All(char.IsAsciiHexDigit)||!ReviewVoice.ValidFormat(format)||duration is <=0 or >604800000000)return;
        using var job=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        voiceWaveJob?.Cancel();voiceWaveJob=job;
        Probe.Waveform? data=null;
        try{data=await Probe.ReadWaveform(ReviewVoice.PathFor(ImageDirectory,asset,format),duration,job.Token,48000);}
        catch(OperationCanceledException) when(job.IsCancellationRequested){}
        catch(Exception e){Serilog.Log.Warning(e,"Could not read annotation waveform");}
        finally{if(voiceWaveJob==job)voiceWaveJob=null;}
        if(!job.IsCancellationRequested&&id==playbackId)Send(new{type="voiceWave",playbackId=id,asset,format,data});
    }

    async Task BuildTimeline(Guid id,string path,Probe.Info info){
        timelineJob?.Cancel();
        using var job=new CancellationTokenSource();
        timelineJob=job;
        var token=job.Token;
        void Emit(string track,object? data){
            if(!token.IsCancellationRequested && id==playbackId)
                Send(new{type="timelineData",playbackId=id,track,data});
        }
        async Task Wave(){
            if(info.Audio==""){Emit("silent",null);return;}
            Emit("wave",await Probe.ReadWaveform(path,info.Duration,token));
        }
        async Task Film(){
            var file=Path.Combine(Path.GetTempPath(),"framenote-film-"+Guid.NewGuid().ToString("N")+".jpg");
            try{
                var count=Math.Clamp((int)Math.Ceiling(info.Duration/250000.0),1,32);
                for(int i=0;i<count;i++){
                    var t=(i*info.Duration/(double)count/1e6).ToString("F6",CultureInfo.InvariantCulture);
                    await Probe.Run(token,"ffmpeg","-nostdin","-v","error","-y","-ss",t,"-threads","2","-i",path,
                        "-map","0:v:0","-frames:v","1","-an","-vf","scale=144:80:force_original_aspect_ratio=decrease,pad=144:80:(ow-iw)/2:(oh-ih)/2,setsar=1","-threads","1","-q:v","5",file);
                    if(File.Exists(file)){
                        var bytes=await File.ReadAllBytesAsync(file,token);File.Delete(file);
                        if(bytes.Length>0)Emit("frame",new{i,count,src="data:image/jpeg;base64,"+Convert.ToBase64String(bytes)});
                    }
                }
                Emit("filmDone",null);
            }finally{File.Delete(file);}
        }
        async Task Track(Func<Task> run,string failure){
            try{await run();}catch(OperationCanceledException) when(token.IsCancellationRequested){}
            catch(Exception){Emit(failure,null);}
        }
        try{await Task.WhenAll(Track(Film,"filmError"),Track(Wave,"waveError"));}
        finally{if(timelineJob==job)timelineJob=null;}
    }
}
