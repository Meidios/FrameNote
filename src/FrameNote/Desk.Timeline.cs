using System.Globalization;
using System.IO;

namespace FrameNote;

public sealed partial class Desk
{
    CancellationTokenSource? timelineJob;

    void StopTimeline(){timelineJob?.Cancel();timelineJob=null;}

    async Task BuildTimeline(Guid id,string path,Probe.Info info){
        StopTimeline();
        using var job=new CancellationTokenSource();
        timelineJob=job;
        var token=job.Token;
        void Emit(string track,object? data){
            if(!token.IsCancellationRequested && id==playbackId)
                Send(new{type="timelineData",playbackId=id,track,data});
        }
        async Task Wave(){
            if(info.Audio==""){Emit("silent",null);return;}
            // Each peak covers one fixed time bucket across all channels. Preserve leading silence.
            var block=Math.Max(1,(int)Math.Ceiling(info.Duration/1e6*16000/2048));
            string filter=$"aresample=16000:async=1:first_pts=0,asetnsamples=n={block}:p=1,astats=metadata=1:reset=1:measure_perchannel=none:measure_overall=Peak_level,ametadata=mode=print:key=lavfi.astats.Overall.Peak_level:file=-";
            var log=await Probe.Run(token,"ffmpeg","-nostdin","-v","error","-copyts","-start_at_zero","-i",path,"-map","0:a:0","-af",filter,"-vn","-f","null","-");
            var peaks=log.Split('\n').Where(s=>s.StartsWith("lavfi.astats.Overall.Peak_level=",StringComparison.Ordinal))
                .Select(s=>double.TryParse(s.Split('=')[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var db)&&double.IsFinite(db)
                    ?Math.Clamp((int)Math.Round(255*Math.Pow(10,Math.Min(db,0)/20)),0,255):0).Take(2048).ToArray();
            Emit("wave",new{peaks,stepUs=block*1e6/16000});
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
