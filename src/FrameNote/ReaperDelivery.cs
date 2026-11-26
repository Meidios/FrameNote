using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace FrameNote;

internal sealed record ReaperDelivery(string Video,long VideoDurationUs,List<string> Rows)
{
    internal static string Hex(string value)=>Convert.ToHexString(Encoding.UTF8.GetBytes(value));

    static async Task RenderOnce(string destination,Func<string,Task> render){
        if(File.Exists(destination))return;
        string pending=Path.Combine(Path.GetDirectoryName(destination)!,Guid.NewGuid()+Path.GetExtension(destination));
        try{
            await render(pending);
            try{File.Move(pending,destination);}catch(IOException) when(File.Exists(destination)){}
        }finally{File.Delete(pending);}
    }

    internal static async Task<ReaperDelivery> Prepare(Review review,string source,string assets,string directory,
        string rulerUnit,CancellationToken token,IProgress<int>? progress=null,bool nativePointLength=true){
        var file=new FileInfo(source);
        var identity=JsonSerializer.SerializeToUtf8Bytes(new{schema=2,source,file.Length,file.LastWriteTimeUtc,review,rulerUnit,L.Language},Review.Json);
        directory=Path.Combine(directory,"review-"+Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant());
        Directory.CreateDirectory(directory);
        var info=await Probe.Inspect(source,token);
        string video=source;
        var rows=review.Notes.Select(n=>n.ReaperRow(true)).ToList();
        long videoEnd=review.DurationUs;
        if(review.Timeline is {} timeline){
            videoEnd=timeline.Clips.Where(c=>c.Track=="video").Select(c=>c.StartUs+c.DurationUs).DefaultIfEmpty(0L).Max();
            if(videoEnd>0 && !MediaEdits.OriginalTracks(review,info.Audio!="")){
                var clips=timeline.Clips.Where(c=>c.Track!="voice" && c.StartUs<videoEnd)
                    .Select(c=>c with{DurationUs=Math.Min(c.DurationUs,videoEnd-c.StartUs)}).ToList();
                var baseReview=review with{DurationUs=videoEnd,Notes=[],Timeline=timeline with{Clips=clips,Trimmed=null}};
                video=Path.Combine(directory,"video.mp4");
                await RenderOnce(video,p=>MediaEdits.Compose(source,baseReview,assets,p,token,v=>progress?.Report(v*60/100)));
            }
            // Unlinked original audio can continue after the final picture. Keep it as audio on the track.
            foreach(var clip in timeline.Clips.Where(c=>c.Track=="audio" && c.StartUs+c.DurationUs>videoEnd && info.Audio!="")){
                var audio=Path.Combine(directory,"original.flac");
                await RenderOnce(audio,p=>Probe.Run(token,"ffmpeg","-nostdin","-v","error","-i",source,"-map","0:a:0","-vn","-c:a","flac",p));
                long start=Math.Max(clip.StartUs,videoEnd);
                rows.Add(string.Join('\t',"audio",start,clip.StartUs+clip.DurationUs-start,clip.SourceUs+start-clip.StartUs,Hex(audio),Hex(review.FileName),".flac"));
            }
        }
        progress?.Report(60);
        var byId=review.Notes.ToDictionary(n=>n.Id);
        var voices=review.StoredNotes().Where(n=>n.Voice!=null).DistinctBy(n=>n.Voice!.Asset).ToDictionary(n=>n.Voice!.Asset);
        foreach(var c in MediaEdits.AudioNotes(review)){
            token.ThrowIfCancellationRequested();
            var note=byId.GetValueOrDefault(c.NoteId??Guid.Empty)??voices.GetValueOrDefault(c.Asset!);
            string path=ReviewVoice.PathFor(assets,c.Asset!,c.Format);
            string preferred=path,ext=Path.GetExtension(path);
            if(note?.Voice is {Original:{} original} voice && voice.Asset==c.Asset){
                preferred=Path.Combine(assets,original.Asset+".original");ext=Path.GetExtension(original.Name);
            }
            string name=note?.Voice?.Original?.Name??note?.ReaperName()??L.Text("语音批注");
            rows.Add(string.Join('\t',"audio",c.StartUs,c.DurationUs,c.SourceUs,Hex(preferred),Hex(name),ext,Hex(path)));
        }
        if(review.Notes.Count==0)return new(video,videoEnd,rows);
        var format=await Probe.ReadVideoFormat(source,token);
        var (width,height)=format.Fit(1920,1080);
        var groups=review.Notes.GroupBy(n=>(n.StartUs,n.EndUs)).OrderBy(g=>g.Key.StartUs).ToArray();
        var images=new string[groups.Length];
        int completed=0;
        await Parallel.ForEachAsync(Enumerable.Range(0,groups.Length),new ParallelOptions{MaxDegreeOfParallelism=2,CancellationToken=token},async (i,ct)=>{
            var (start,end)=groups[i].Key;
            var notes=groups[i].OrderBy(n=>n.Number).ToArray();
            var title=string.Join(", ",notes.Select(n=>"#"+n.Number));
            string image=Path.Combine(directory,$"annotation-{i+1}.png");
            await RenderOnce(image,pending=>{
                ct.ThrowIfCancellationRequested();
                var renderer=new VideoAnnotations(review,width,height,format.Width,format.Height,assets,rulerUnit,false);
                renderer.WriteLayerPng(pending,notes,captionsInsidePicture:true);
                return Task.CompletedTask;
            });
            long length=end-start??(nativePointLength?0:1_000_000);
            images[i]=string.Join('\t',"image",start,length,0,Hex(image),Hex(title),".png");
            progress?.Report(60+25*Interlocked.Increment(ref completed)/groups.Length);
        });
        rows.AddRange(images);
        return new(video,videoEnd,rows);
    }
}
