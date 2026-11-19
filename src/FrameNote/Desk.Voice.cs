using System.IO;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FrameNote;

public sealed partial class Desk
{
    WasapiCapture? voiceCapture;
    void StopVoice(){voiceCapture?.StopRecording();}
    async Task RecordVoice(JsonElement p){
        Guid id=p.GetProperty("id").GetGuid();
        if(voiceCapture!=null || recorder!=null || pending || configuring || doc==null || viewingImage){Send(new{type="voiceStored",id,error="当前有录制或文件操作，请结束后再录语音。"});return;}
        var note=doc.Notes.FirstOrDefault(n=>n.Id==id);if(note==null){Send(new{type="voiceStored",id,error="请先选择一条视频批注。"});return;}
        configuring=true;Directory.CreateDirectory(scratch);
        string wave=Path.Combine(scratch,Guid.NewGuid()+".wav"),encoded=Path.ChangeExtension(wave,".m4a");
        try{
            using var enumerator=new MMDeviceEnumerator();
            using var device=enumerator.GetDevice(p.GetProperty("device").GetString()!);
            if(device.DataFlow!=DataFlow.Capture || device.State!=DeviceState.Active)throw new InvalidOperationException("请选择可用的音频输入设备。");
            using var capture=new WasapiCapture(device);voiceCapture=capture;
            var stopped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            long bytes=0;int bytesPerSecond=capture.WaveFormat.AverageBytesPerSecond;
            using(var writer=new WaveFileWriter(wave,capture.WaveFormat)){
                capture.DataAvailable+=(_,e)=>{try{writer.Write(e.Buffer,0,e.BytesRecorded);bytes+=e.BytesRecorded;}catch(Exception ex){stopped.TrySetException(ex);capture.StopRecording();}};
                capture.RecordingStopped+=(_,e)=>{if(e.Exception!=null)stopped.TrySetException(e.Exception);else stopped.TrySetResult();};
                capture.StartRecording();Send(new{type="voiceStarted",id});
                using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(Math.Min(600,(doc.DurationUs-note.StartUs)/1e6)));
                using var stop=limit.Token.Register(()=>{try{capture.StopRecording();}catch{}});
                await stopped.Task;
            }
            voiceCapture=null;
            if(bytes<bytesPerSecond/10)throw new InvalidOperationException("录音太短，请至少录制 0.1 秒。");
            long duration=Math.Min(600_000_000,(long)(bytes*1e6/bytesPerSecond));
            await Probe.Run(lifetime.Token,"ffmpeg","-nostdin","-v","error","-i",wave,"-ac","1","-ar","48000","-c:a","aac","-b:a","128k","-movflags","+faststart",encoded);
            string hash=ReviewVoice.Store(ImageDirectory,await File.ReadAllBytesAsync(encoded));
            var updated=note with{Voice=new(hash,duration),EndUs=Math.Min(doc.DurationUs,note.StartUs+duration)};
            var review=doc.WithNotes(doc.Notes.Select(n=>n.Id==id?updated:n).ToList());if(review.Timeline is {} edits){var clips=edits.Clips.Where(c=>c.NoteId!=id).ToList();clips.Add(new(Guid.NewGuid(),"voice",note.StartUs,0,updated.EndUs!.Value-note.StartUs,hash,duration,id));review=review with{Timeline=edits with{Clips=clips}};}review.Check();
            if(draft!=null){ReviewImages.Copy(review,ImageDirectory,draft+".images");Packages.Atomic(draft,s=>JsonSerializer.Serialize(s,review,Review.Json));}
            doc=review;Send(new{type="voiceStored",id,voice=updated.Voice,endUs=updated.EndUs});
        }catch(Exception e){Send(new{type="voiceStored",id,error="语音录制失败："+e.Message});}
        finally{voiceCapture=null;configuring=false;File.Delete(wave);File.Delete(encoded);}
    }
}
