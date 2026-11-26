using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace FrameNote;

public sealed partial class Desk
{
    static string Num(double value)=>value.ToString("0.######",CultureInfo.InvariantCulture);
    enum VideoOutput { Mp4, GifFile, GifClipboard }
    sealed record ExportTimeRange(long StartUs,long EndUs){
        public long DurationUs=>EndUs-StartUs;

        public Review Slice(Review source){
            if(StartUs<0 || StartUs>=EndUs || EndUs>source.DurationUs)
                throw new InvalidDataException(L.Text("导出范围超出当前时间线。"));
            var notes=new List<Note>();
            foreach(var note in source.Notes){
                long start=Math.Max(StartUs,note.StartUs),end=Math.Min(EndUs,VideoAnnotations.End(note,source.DurationUs));
                if(start>=end)continue;
                // A Mark already on screen at the cut keeps only its remaining display time.
                long? localEnd=note.EndUs==null && note.StartUs>=StartUs?null:end-StartUs;
                notes.Add(note with{StartUs=start-StartUs,EndUs=localEnd});
            }
            // The renderer gets a local clock over one media input. Any track edits
            // and gaps are already represented by that input, on their original frame grid.
            return source with{Version=1,DurationUs=this.DurationUs,Notes=notes,Timeline=null};
        }
    }
    async Task ExportVideo(VideoOutput target,ExportTimeRange? range){
        if(doc==null || media==null || playbackInfo==null || viewingImage || voiceCapture!=null)return;
        var source=doc;
        bool hasSourceAudio=playbackInfo.Audio!="";
        bool gif=target!=VideoOutput.Mp4;
        string format=gif?"GIF":"MP4",extension=gif?".gif":".mp4";
        string stem=Path.GetFileNameWithoutExtension(doc.FileName)+L.Text("_批注");
        await Work(()=>WithExportProgress(format,async token=>{
            var review=range?.Slice(source)??MediaEdits.ForFlattenedExport(source,hasSourceAudio);
            var mediaReview=range==null?review:source;
            var options=CurrentOutput();
            async Task Render(string output){
                bool original=MediaEdits.OriginalTracks(mediaReview,hasSourceAudio);
                int prepared=original?0:40;
                if(prepared>0)ReportExport(L.Text("正在准备剪辑后的画面与声音"),0);
                var input=original?media!:await EditedMedia(token,p=>ReportExport(L.Text("正在准备剪辑后的画面与声音"),p*prepared/100),mediaReview);
                await RenderVideo(review,source,range,input,output,prepared,gif,options.ExportBar,original,token);
            }
            if(target!=VideoOutput.GifClipboard){
                var path=await WriteOutput(options.ExportDirectory,stem+extension,Render,token);
                Send(new{type="status",text=L.Format($"带批注的 {format} 已保存：")+path});
                return new FileInfo(path);
            }
            string output=Path.Combine(scratch,Guid.NewGuid().ToString("N")+extension);
            try{
                await Render(output);
                await GifClipboard.Publish(output,stem,token);
                Send(new{type="status",text=L.Text("带批注的 GIF 已复制到粘贴板。")});
                return null;
            }finally{File.Delete(output);}
        }));
    }
    async Task RenderVideo(Review review,Review source,ExportTimeRange? range,string input,string output,int prepared,bool gif,ExportBarOptions bar,bool original,CancellationToken token){
        review.Check();ReportExport(L.Text("正在准备视频与批注"),prepared);
        int renderStart=prepared+5;
        void RenderProgress(int percent)=>ReportExport(gif?L.Text("正在渲染 GIF"):L.Text("正在渲染 MP4"),renderStart+(100-renderStart)*percent/100);
        var format=await Probe.ReadVideoFormat(input,token);
        var (width,height)=gif?format.Fit(800,800):format.Fit(1920,1080);
        const int gifFps=12;
        long gifFrames=(review.DurationUs*gifFps+999_999)/1_000_000;
        double fps=gif?gifFps:format.Fps;
        string frameRate=gif?gifFps.ToString(CultureInfo.InvariantCulture):format.FrameRate;
        int barHeight=!gif && bar.Enabled?VideoProgressBar.Height(height):0;
        var renderer=new VideoAnnotations(review,width,height,format.Width,format.Height,ImageDirectory,defaultRulerUnit,footerBottomInset:barHeight);
        string videoFilter;
        var lenses=review.Notes.Where(n=>n.Shape?.Kind=="magnifier").OrderBy(n=>n.Number).ToArray();
        bool liveLens=lenses.Length>4;
        string[] mediaInput=["-i",input];
        long audioLeadUs=0;
        if(range!=null){
            // A Region can start between frames. Decode the frame already on screen
            // and shift subsequent presentation times onto the exact selection clock.
            long frameStart;
            if(!original){
                double frame=Math.Floor((range.StartUs+.5)*format.Fps/1e6);
                frameStart=(long)Math.Round(frame*1e6/format.Fps);
            }else{
                int index=Array.BinarySearch(reviewFrameTimes,range.StartUs);
                frameStart=reviewFrameTimes[index>=0?index:Math.Max(0,~index-1)];
            }
            long seekUs=Math.Max(0,Math.Min(frameStart,range.StartUs)-1);
            audioLeadUs=range.StartUs-seekUs;
            mediaInput=["-ss",Num(seekUs/1e6),"-t",Num((range.EndUs-seekUs)/1e6),"-i",input];
            long frameCount=gif?gifFrames:(long)Math.Ceiling(review.DurationUs*fps/1e6);
            videoFilter=$"settb=AVTB,setpts='max(0,PTS-{audioLeadUs})',tpad=stop_mode=clone:stop=-1,fps={frameRate}:start_time=0:round=up,trim=end_frame={frameCount}";
        }else videoFilter=$"fps={frameRate}"+(gif?$":round=up,tpad=stop_mode=clone:stop=-1,trim=end_frame={gifFrames}":"");
        videoFilter+=$",scale={width}:{height},setsar=1";
        var encode=new List<string>{"-nostdin","-v","error"};
        encode.AddRange(mediaInput);
        var filters=new List<string>();
        string videoMap="[video]";
        string? overlayDirectory=null;
        try{
            if(liveLens){
                encode.AddRange(["-f","rawvideo","-pixel_format","bgra","-video_size",$"{width}x{renderer.OutputHeight}","-framerate",frameRate,"-i","pipe:0"]);
                videoMap="1:v:0";
            }else if(review.Notes.Count>0){
                overlayDirectory=Directory.CreateTempSubdirectory("framenote-overlay-").FullName;
                var timeline=await renderer.WriteOverlayTimeline(overlayDirectory,fps,frameRate,token,
                    p=>ReportExport(L.Text("正在准备视频与批注"),prepared+5*p/100));
                encode.AddRange(["-f","concat","-safe","0","-i",timeline]);
                filters.Add($"[0:v:0]{videoFilter},setpts=PTS-STARTPTS,pad={width}:{renderer.OutputHeight}[base]");
                string background="[base]";
                if(lenses.Length>0){
                    filters.Add("[base]split="+(lenses.Length+1)+"[background]"+string.Concat(lenses.Select((_,i)=>$"[lens{i}]")));
                    background="[background]";
                    for(int i=0;i<lenses.Length;i++){
                        var n=lenses[i];var (rect,crop)=VideoAnnotations.LensBounds(n.Shape!,width,height);
                        filters.Add($"[lens{i}]crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}:exact=1,scale={Math.Max(1,(int)rect.Width)}:{Math.Max(1,(int)rect.Height)},format=yuva444p,colorchannelmixer=aa={Num(n.Alpha)}[glass{i}]");
                        filters.Add($"{background}[glass{i}]overlay={(int)rect.X}:{(int)rect.Y}:enable='gte(t,{Num(n.StartUs/1e6)})*lt(t,{Num(VideoAnnotations.End(n,review.DurationUs)/1e6)})'[zoom{i}]");
                        background=$"[zoom{i}]";
                    }
                }
                filters.Add(background+"[1:v:0]overlay=eof_action=repeat[video]");
            }else{
                filters.Add($"[0:v:0]{videoFilter},setpts=PTS-STARTPTS[video]");
            }
            bool audio=false;
            if(gif){
                string scene=liveLens?"[1:v:0]":videoMap;
                filters.Add(scene+"split[pixels][colors]");
                filters.Add("[colors]palettegen=stats_mode=single[palette]");
                filters.Add("[pixels][palette]paletteuse=new=1:"+ExportEncoding.GifDither+"[gif]");
                videoMap="[gif]";
            }else{
                var info=original?playbackInfo!:await Probe.Inspect(input,token);
                int firstVoice=review.Notes.Count>0?2:1;
                int voiceCount=AddExportAudio(encode,filters,source,range??new(0,review.DurationUs),info.Audio!="",firstVoice,audioLeadUs,original);
                audio=info.Audio!=""||voiceCount>0;
                if(barHeight>0){
                    overlayDirectory??=Directory.CreateTempSubdirectory("framenote-overlay-").FullName;
                    string strip=Path.Combine(overlayDirectory,"progress.png");
                    VideoProgressBar.Write(strip,review,width,barHeight,bar);
                    int stripInput=firstVoice+voiceCount;
                    encode.AddRange(["-framerate",frameRate,"-i",strip]);
                    VideoProgressBar.AddFilters(filters,liveLens?"[1:v:0]":videoMap,stripInput,width,barHeight,review.DurationUs,fps);
                    videoMap="[withProgress]";
                }
            }
            if(filters.Count>0)encode.AddRange(["-filter_complex",string.Join(';',filters)]);
            encode.AddRange(["-map",videoMap]);
            if(gif){
                int lastDelay=Math.Max(1,(int)(Math.Round(review.DurationUs/10000d)-Math.Round((gifFrames-1)*100d/gifFps)));
                encode.AddRange(["-frames:v",gifFrames.ToString(),"-an","-loop","0","-final_delay",lastDelay.ToString()]);
            }else{
                if(audio)encode.AddRange(["-map","[audio]","-c:a","aac","-b:a","192k"]);
                encode.AddRange(["-t",Num(review.DurationUs/1e6),"-pix_fmt","yuv420p","-movflags","+faststart"]);
            }
            RenderProgress(0);
            Task Write(string[] codec){
                if(liveLens)return EncodeLiveLenses(mediaInput,videoFilter,renderer,width,height,fps,review.DurationUs,[..encode,..codec,output],RenderProgress,token);
                return Probe.EncodeWithProgress([..encode,..codec,"-abort_on","empty_output_stream",output],review.DurationUs,RenderProgress,token);
            }
            if(gif)await Write([]);
            else await ExportEncoding.H264(width,renderer.OutputHeight,20,output,token,Write);
        }finally{if(overlayDirectory!=null)Directory.Delete(overlayDirectory,true);}
    }
    int AddExportAudio(List<string> encode,List<string> filters,Review source,ExportTimeRange range,bool hasSourceAudio,int firstVoice,long audioLeadUs,bool separateVoices=false){
        var mix=new List<string>();
        if(hasSourceAudio){
            filters.Add($"[0:a:0]aresample=async=1:first_pts=0,atrim=start={Num(audioLeadUs/1e6)}:duration={Num(range.DurationUs/1e6)},asetpts=PTS-STARTPTS[original]");mix.Add("[original]");
        }
        int count=0;
        // Edited timelines already contain their mixed voice clips in the composed input.
        if(source.Timeline==null || separateVoices)foreach(var clip in MediaEdits.AudioNotes(source)){
            long start=Math.Max(range.StartUs,clip.StartUs);
            long end=Math.Min(range.EndUs,clip.StartUs+clip.DurationUs);
            if(start>=end)continue;
            encode.AddRange(["-ss",Num((clip.SourceUs+start-clip.StartUs)/1e6),"-t",Num((end-start)/1e6),"-i",ReviewVoice.PathFor(ImageDirectory,clip.Asset!,clip.Format)]);
            string label=$"[voice{count}]";
            filters.Add($"[{firstVoice+count}:a:0]asetpts=PTS-STARTPTS,adelay={Num((start-range.StartUs)/1000d)}:all=1{label}");
            mix.Add(label);count++;
        }
        if(mix.Count>0)filters.Add(string.Concat(mix)+$"amix=inputs={mix.Count}:duration=longest:normalize=0,alimiter=limit=0.95:level=0:latency=1[audio]");
        return count;
    }
    async Task EncodeLiveLenses(string[] mediaInput,string scale,VideoAnnotations renderer,int width,int height,double fps,long duration,List<string> encode,Action<int> report,CancellationToken token){
        using var decoder=Probe.StartFfmpeg(["-nostdin","-v","error",..mediaInput,"-map","0:v:0","-an","-vf",scale,"-f","rawvideo","-pix_fmt","bgra","pipe:1"],false,true);
        using var encoder=Probe.StartFfmpeg(encode,true,false);
        using var stop=token.Register(()=>{Probe.Kill(decoder);Probe.Kill(encoder);});
        var decodeError=decoder.StandardError.ReadToEndAsync();var encodeError=encoder.StandardError.ReadToEndAsync();
        try{
            byte[] pixels=new byte[width*height*4];long frame=0;var progress=Stopwatch.StartNew();
            while(true){
                token.ThrowIfCancellationRequested();int offset=0;
                while(offset<pixels.Length){int count=await decoder.StandardOutput.BaseStream.ReadAsync(pixels.AsMemory(offset),token);if(count==0)break;offset+=count;}
                if(offset==0)break;if(offset!=pixels.Length)throw new IOException(L.Text("视频解码帧不完整。"));
                var rendered=renderer.Render(pixels,(long)Math.Round(frame*1e6/fps));await encoder.StandardInput.BaseStream.WriteAsync(rendered,token);frame++;
                if(progress.ElapsedMilliseconds>250){report(Math.Min(99,(int)(frame*1e8/fps/duration)));progress.Restart();await Task.Delay(1,token);}
            }
            encoder.StandardInput.Close();await decoder.WaitForExitAsync(token);await encoder.WaitForExitAsync(token);
            if(decoder.ExitCode!=0)throw new IOException(L.Text("视频解码失败：")+await decodeError);
            if(encoder.ExitCode!=0)throw new IOException(L.Text("视频编码失败：")+await encodeError);
            if(frame==0)throw new IOException(L.Text("视频中没有可导出的画面。"));
        }catch{
            Probe.Kill(decoder);Probe.Kill(encoder);
            await Task.WhenAll(decoder.WaitForExitAsync(),encoder.WaitForExitAsync());
            token.ThrowIfCancellationRequested();throw;
        }
    }
}
