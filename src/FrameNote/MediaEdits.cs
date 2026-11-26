using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FrameNote;

public record EditClip(Guid Id,string Track,long StartUs,long SourceUs,long DurationUs,string? Asset=null,long? SourceDurationUs=null,Guid? NoteId=null, [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Format=null);
public record TrimmedPart(Guid ClipId,Note? Note=null,EditClip? Voice=null);
public record EditTimeline(long SourceDurationUs,List<EditClip> Clips,bool Linked=true,List<TrimmedPart>? Trimmed=null)
{
    public void Check(long duration){
        if(SourceDurationUs<=0 || SourceDurationUs>604800000000L || Clips==null || Clips.Count>2000)throw new InvalidDataException(L.Text("剪辑片段数量或原片时长无效。"));
        var ids=new HashSet<Guid>();
        foreach(var c in Clips){
            if(c==null || c.Id==Guid.Empty || !ids.Add(c.Id) || c.Track is not ("video" or "audio" or "voice") || c.StartUs<0 || c.SourceUs<0 || c.DurationUs<=0 || c.StartUs>duration-c.DurationUs || c.SourceUs>(c.Track=="voice"?c.SourceDurationUs??0:SourceDurationUs)-c.DurationUs)
                throw new InvalidDataException(L.Text("片段超出媒体或时间线范围。"));
            if(c.Track=="voice" && (!ReviewVoice.ValidFormat(c.Format) || c.SourceDurationUs is null or <=0 or >604800000000 || c.Asset is not {Length:64} || !c.Asset.All(x=>char.IsAsciiDigit(x)||x is >= 'a' and <= 'f')))
                throw new InvalidDataException(L.Text("语音片段附件无效。"));
        }
        foreach(string track in new[]{"video","audio"}){
            long end=0;foreach(var c in Clips.Where(c=>c.Track==track).OrderBy(c=>c.StartUs)){if(c.StartUs<end)throw new InvalidDataException(L.Text("同一视频或原声音轨的片段不能重叠。"));end=c.StartUs+c.DurationUs;}
        }
        if(Trimmed is {Count:>20000})throw new InvalidDataException(L.Text("被裁剪的批注或录音过多。"));
        foreach(var part in Trimmed??[]){
            if(part==null||!ids.Contains(part.ClipId)||(part.Note==null)==(part.Voice==null))
                throw new InvalidDataException(L.Text("裁剪记录没有对应片段。"));
            if(part.Voice is {} v && (v.Track!="voice"||v.Id==Guid.Empty||v.StartUs<0||v.DurationUs<=0||v.StartUs>SourceDurationUs-v.DurationUs||
                !ReviewVoice.ValidFormat(v.Format) || v.SourceDurationUs is null or <=0 or >604800000000||v.SourceUs<0||v.SourceUs>v.SourceDurationUs-v.DurationUs||
                v.Asset is not {Length:64}||!v.Asset.All(x=>char.IsAsciiDigit(x)||x is >= 'a' and <= 'f')))
                throw new InvalidDataException(L.Text("被裁剪的语音片段无效。"));
        }
    }
}

internal static class MediaEdits
{
    internal static Review ForFlattenedExport(Review review,bool hasSourceAudio){
        if(review.Timeline is not {} timeline)return review;
        timeline=timeline with{Trimmed=null};
        // A silent source can still have an editable original-audio lane.
        if(!hasSourceAudio)timeline=timeline with{Clips=timeline.Clips.Where(c=>c.Track!="audio").ToList()};
        long end=timeline.Clips.Select(c=>c.StartUs+c.DurationUs).DefaultIfEmpty().Max();
        end=Math.Max(end,review.Notes.Max(n=>n.EndUs)??0);
        // A Mark inside the cut stops at its end. A standalone Mark after the cut needs display time.
        var lastMark=review.Notes.Where(n=>n.EndUs==null).MaxBy(n=>n.StartUs);
        if(lastMark!=null && lastMark.StartUs>=end)end=VideoAnnotations.End(lastMark,review.DurationUs);
        if(end==0)throw new InvalidDataException(L.Text("时间线上没有可导出的片段或批注。"));
        return review with{DurationUs=end,Timeline=timeline};
    }
    internal static bool OriginalTracks(Review review,bool hasAudio){
        if(review.Timeline is not {} timeline)return true;
        if(timeline.SourceDurationUs!=review.DurationUs)return false;
        bool Whole(string track){
            var clips=timeline.Clips.Where(c=>c.Track==track).ToArray();
            return clips.Length==1 && clips[0].StartUs==0 && clips[0].SourceUs==0 && clips[0].DurationUs==review.DurationUs;
        }
        return Whole("video") && (!hasAudio || Whole("audio"));
    }
    internal static IEnumerable<EditClip> AudioNotes(Review review){
        if(review.Timeline is {} timeline)return timeline.Clips.Where(c=>c.Track=="voice");
        return review.Notes.Where(n=>n.Voice!=null).Select(n=>{
            var v=n.Voice!;
            long length=Math.Min(v.DurationUs,(n.EndUs??review.DurationUs)-n.StartUs);
            return new EditClip(n.Id,"voice",n.StartUs,0,length,v.Asset,v.DurationUs,n.Id,v.Format);
        });
    }
    static string N(double n)=>n.ToString("0.######",CultureInfo.InvariantCulture);
    internal static async Task Compose(string input,Review review,string assets,string output,CancellationToken token,Action<int>? report=null){
        review.Check();var timeline=review.Timeline!;
        var info=await Probe.Inspect(input,token);
        using var meta=JsonDocument.Parse(await Probe.Run(token,"ffprobe","-v","error","-select_streams","v:0","-show_entries","stream=width,height,avg_frame_rate,sample_aspect_ratio:stream_side_data=rotation","-of","json",input));
        var stream=meta.RootElement.GetProperty("streams")[0];int width=stream.GetProperty("width").GetInt32(),height=stream.GetProperty("height").GetInt32();
        if(stream.TryGetProperty("sample_aspect_ratio",out var sar)){var p=sar.GetString()!.Split(':');if(p.Length==2&&double.TryParse(p[0],CultureInfo.InvariantCulture,out var a)&&double.TryParse(p[1],CultureInfo.InvariantCulture,out var b)&&a>0&&b>0)width=(int)Math.Round(width*a/b);}
        if(stream.TryGetProperty("side_data_list",out var sides)&&sides.EnumerateArray().Any(s=>s.TryGetProperty("rotation",out var r)&&Math.Abs(r.GetDouble())%180==90))(width,height)=(height,width);
        width=(width+1)/2*2;height=(height+1)/2*2;
        var rate=stream.GetProperty("avg_frame_rate").GetString()!.Split('/');double fps=double.Parse(rate[0],CultureInfo.InvariantCulture)/double.Parse(rate[1],CultureInfo.InvariantCulture);if(!double.IsFinite(fps)||fps<=0)fps=30;fps=Math.Min(120,fps);
        var args=new List<string>{"-nostdin","-v","error","-i",input};
        var voices=timeline.Clips.Where(c=>c.Track=="voice").ToArray();
        foreach(var c in voices)args.AddRange(["-i",ReviewVoice.PathFor(assets,c.Asset!,c.Format)]);
        var filters=new List<string>();var parts=new List<string>();long cursor=0;int seq=0;
        void Black(long length){if(length<=0)return;string id="v"+seq++;filters.Add($"color=c=black:s={width}x{height}:r={N(fps)}:d={N(length/1e6)},setsar=1[{id}]");parts.Add($"[{id}]");}
        foreach(var c in timeline.Clips.Where(c=>c.Track=="video").OrderBy(c=>c.StartUs)){
            Black(c.StartUs-cursor);string id="v"+seq++;
            filters.Add($"[0:v:0]trim=start={N(c.SourceUs/1e6)}:duration={N(c.DurationUs/1e6)},setpts=PTS-STARTPTS,scale={width}:{height},setsar=1,fps={N(fps)},tpad=stop_mode=clone:stop_duration=1,trim=duration={N(c.DurationUs/1e6)}[{id}]");parts.Add($"[{id}]");cursor=c.StartUs+c.DurationUs;
        }
        Black(review.DurationUs-cursor);filters.Add(string.Concat(parts)+$"concat=n={parts.Count}:v=1:a=0[video]");
        var mix=new List<string>{"[silence]"};filters.Add($"anullsrc=r=48000:cl=stereo,atrim=duration={N(review.DurationUs/1e6)}[silence]");int audio=0;
        void Sound(EditClip c,int source){string id="a"+audio++;filters.Add($"[{source}:a:0]atrim=start={N(c.SourceUs/1e6)}:duration={N(c.DurationUs/1e6)},asetpts=PTS-STARTPTS,aresample=48000,adelay={N(c.StartUs/1000d)}:all=1[{id}]");mix.Add($"[{id}]");}
        if(info.Audio!="")foreach(var c in timeline.Clips.Where(c=>c.Track=="audio"))Sound(c,0);
        for(int i=0;i<voices.Length;i++)Sound(voices[i],i+1);
        filters.Add(string.Concat(mix)+$"amix=inputs={mix.Count}:duration=first:normalize=0,alimiter=limit=0.95:level=0:latency=1[audio]");
        // A script avoids Windows command-line limits on large edit lists.
        string script=output+".filters";
        try{
            await File.WriteAllTextAsync(script,string.Join(';',filters),token);
            args.AddRange(["-filter_complex_script",script,"-map","[video]","-map","[audio]","-t",N(review.DurationUs/1e6),"-pix_fmt","yuv420p","-c:a","aac","-b:a","256k","-movflags","+faststart"]);
            await ExportEncoding.H264(width,height,16,output,token,codec=>Probe.EncodeWithProgress([..args,..codec,output],review.DurationUs,report??(_=>{}),token));
        }
        catch{File.Delete(output);throw;}
        finally{File.Delete(script);}
    }
}

public sealed partial class Desk
{
    string? composedKey,composedMedia;
    async Task<string> EditedMedia(CancellationToken token,Action<int>? report=null,Review? review=null){
        review??=doc;
        if(review?.Timeline==null)return media!;
        string key=media+JsonSerializer.Serialize(new{review.DurationUs,review.Timeline},Review.Json);
        if(composedKey==key&&composedMedia!=null&&File.Exists(composedMedia))return composedMedia;
        Send(new{type="status",text=L.Text("正在合成剪辑后的媒体…")});Directory.CreateDirectory(scratch);
        string output=Path.Combine(scratch,Guid.NewGuid()+".mp4");await MediaEdits.Compose(media!,review,ImageDirectory,output,token,report);
        composedKey=key;composedMedia=output;return output;
    }
}
