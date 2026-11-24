using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameNote;

public record LensPosition(double X,double Y);
public record Drawing(string Kind, double X, double Y, double ToX, double ToY, double? ThirdX=null, double? ThirdY=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] long? RulerFrame=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] LensPosition? Preview=null){
    internal double LensScale=>Preview==null?2:Math.Min(2,1/Math.Max(Math.Abs(ToX-X),Math.Abs(ToY-Y)));
    internal string LensLabel=>LensScale.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"×";
}
public record RulerScale(double MetresPerWidth,double Aspect,string Unit){
    internal const string DefaultUnit="m";
    const double MetresPerPixel=.0254/96;
    static double MetresPerUnit(string? unit)=>unit switch{"mm"=>.001,"cm"=>.01,"m"=>1,"px"=>MetresPerPixel,_=>double.NaN};
    internal static bool SupportsUnit(string? unit)=>double.IsFinite(MetresPerUnit(unit));
    internal static string Label(Drawing shape,IReadOnlyDictionary<long,RulerScale>? scales,int width,int height,string unit){
        if(shape.RulerFrame is long frame && scales?.GetValueOrDefault(frame) is {} scale)return scale.Measure(shape);
        double dx=(shape.ToX-shape.X)*width,dy=(shape.ToY-shape.Y)*height,pixels=Math.Sqrt(dx*dx+dy*dy);
        var culture=System.Globalization.CultureInfo.InvariantCulture;
        return (unit=="px"?pixels.ToString("0.#",culture):(pixels*MetresPerPixel/MetresPerUnit(unit)).ToString("G6",culture))+" "+unit;
    }
    string Measure(Drawing s){
        double dx=s.ToX-s.X,dy=(s.ToY-s.Y)*Aspect;
        double length=Math.Sqrt(dx*dx+dy*dy)*MetresPerWidth/MetresPerUnit(Unit);
        return length.ToString("G6",System.Globalization.CultureInfo.InvariantCulture)+" "+Unit;
    }
}
public record Voice(string Asset, long DurationUs);
public record Note(Guid Id, long StartUs, long? EndUs, string Text, Drawing? Shape, int Number=0, string? Color=null, List<Picture>? Pictures=null, Voice? Voice=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] bool AutoMeasurementText=false)
{
    internal string DisplayMeasurementText(IReadOnlyDictionary<long,RulerScale>? scales,int width,int height,string unit)=>
        Shape is {Kind:"ruler"} ruler && (AutoMeasurementText||Text.Length==0)
            ?L.Text("距离标尺 ")+RulerScale.Label(ruler,scales,width,height,unit):Text;
    public string ReaperName() => $"#{Number} {(Text.Length==0?(Voice!=null?L.Text("语音批注"):Shape!=null?L.Text("图形批注"):Pictures is {Count:>0}?L.Text("图片批注"):L.Text("批注")):Text.ReplaceLineEndings(" "))}";
    // Legacy notes without an explicit color use the same numbered swatches as the desk.
    static readonly string[] legacyColors=["#ffd886","#8ddcff","#8de3af","#ff9d97","#cfb1ff","#ffaed8"];
    public string DisplayColor() => Color??legacyColors[(Number-1)%legacyColors.Length];
    public string ReaperRow(bool colors) {
        var row=$"{StartUs}\t{EndUs??-1}\t{Convert.ToHexString(Encoding.UTF8.GetBytes(ReaperName()))}";
        return colors?row+"\t"+DisplayColor()[1..]:row;
    }
}
public record Review(int Version, Guid Id, string FileName, long DurationUs, List<Note> Notes, int LastNoteNumber=0, EditTimeline? Timeline=null,Dictionary<long,RulerScale>? RulerScales=null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Formats = [".mp4", ".m4v", ".mov", ".webm", ".mkv", ".avi", ".wmv", ".asf", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".flv", ".mxf", ".vob", ".ogv", ".3gp"];
    public string Extension => Path.GetExtension(FileName).ToLowerInvariant();
    // Old v1 documents have no numbers. Assign them once in timeline order;
    // break ties by identity to keep numbers stable when reopening a package.
    public Review WithLegacyNumbers(){
        if(LastNoteNumber!=0 || Notes is not {Count:>0} || !Notes.All(n=>n?.Number==0))return this;
        var numbers=Notes.OrderBy(n=>n.StartUs).ThenBy(n=>n.Id).Select((n,i)=>(n.Id,Number:i+1)).ToDictionary(n=>n.Id,n=>n.Number);
        return this with{Notes=Notes.Select(n=>n with{Number=numbers[n.Id]}).ToList(),LastNoteNumber=Notes.Count};
    }
    public Review WithNotes(List<Note> notes) => this with{Notes=notes,LastNoteNumber=Math.Max(LastNoteNumber,notes.Count==0?0:notes.Max(n=>n.Number))};
    internal IEnumerable<Note> StoredNotes()=>Notes.Concat((Timeline?.Trimmed??[]).Select(p=>p.Note).OfType<Note>());
    public void Check()
    {
        bool image=Version==2;
        if (Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(FileName) || FileName.Length > 255 || FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            (image ? !Desk.ImageFormats.Contains(Extension) || DurationUs!=0 : (Version!=1 && Version!=3) || !Formats.Contains(Extension) || DurationUs<=0 || DurationUs>604800000000L) || Notes == null || Notes.Count > 10000 || LastNoteNumber<0)
            throw new InvalidDataException(L.Text("不支持的审阅包版本、媒体或时长。"));
        if((Version==3)!=(Timeline!=null))throw new InvalidDataException(L.Text("剪辑版本与时间线不匹配。"));
        Timeline?.Check(DurationUs);
        if(RulerScales is {Count:>10000}||RulerScales?.Values.Any(s=>s==null||!double.IsFinite(s.MetresPerWidth)||s.MetresPerWidth<=0||!double.IsFinite(s.Aspect)||s.Aspect<=0||!RulerScale.SupportsUnit(s.Unit))==true)
            throw new InvalidDataException(L.Text("标尺比例或单位无效。"));
        HashSet<Guid> ids = [];
        HashSet<int> numbers = [];
        foreach (var n in Notes)if(n==null||!ids.Add(n.Id)||!numbers.Add(n.Number))throw new InvalidDataException(L.Text("批注编号或标识重复。"));
        foreach (var (n,limit) in Notes.Select(n=>(n,DurationUs)).Concat((Timeline?.Trimmed??[]).Select(p=>p.Note).OfType<Note>().Select(n=>(n,Timeline!.SourceDurationUs))))
        {
            if (n == null || n.Id == Guid.Empty || n.Number<=0 || n.Number>LastNoteNumber || n.Text==null || n.Text.Length > 4000 ||
                n.Text.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t') ||
                (image ? n.StartUs!=0 || n.EndUs!=null : n.StartUs < 0 || n.StartUs >= limit || (n.EndUs is long e && (e <= n.StartUs || e > limit))))
                throw new InvalidDataException(L.Text("批注时间、文字或编号无效。"));
            if (n.Shape is Drawing d && (!(d.Kind is "arrow" or "box" or "line" or "angle" or "ruler" or "magnifier" or "text") ||
                new[]{d.X,d.Y,d.ToX,d.ToY}.Any(x => !double.IsFinite(x) || x < 0 || x > 1) ||
                (d.Preview is {} preview && (d.Kind!="magnifier" || !double.IsFinite(preview.X+preview.Y) || preview.X is <0 or >1 || preview.Y is <0 or >1)) ||
                (d.Kind=="angle" && (d.ThirdX==null || d.ThirdY==null)) ||
                new[]{d.ThirdX,d.ThirdY}.Any(x=>x.HasValue && (!double.IsFinite(x.Value)||x<0||x>1))))
                throw new InvalidDataException(L.Text("画面标记无效。"));
            if(n.Voice is {} voice && (image || voice.Asset is not {Length:64} || !voice.Asset.All(c=>char.IsAsciiDigit(c)||c is >= 'a' and <= 'f') || voice.DurationUs<=0 || voice.DurationUs>600_000_000))
                throw new InvalidDataException(L.Text("语音批注无效。"));
            if(n.Color is {} color && (color.Length!=7 || color[0]!='#' || !color.Skip(1).All(Uri.IsHexDigit)))
                throw new InvalidDataException(L.Text("批注颜色无效。"));
            if(n.Pictures is { } pics && (pics.Count>16 || pics.Any(p=>p==null||p.Asset is not {Length:64}||
                !p.Asset.All(c=>char.IsAsciiDigit(c)||c is >= 'a' and <= 'f')||
                p.PixelWidth is <1 or >ReviewImages.MaxSide || p.PixelHeight is <1 or >ReviewImages.MaxSide ||
                !double.IsFinite(p.X+p.Y+p.Width)||p.X<0||p.Y<0||p.Y>=1||p.Width<=0||p.X+p.Width>1.000001)))
                throw new InvalidDataException(L.Text("批注图片或位置无效。"));
        }
    }
}

public static class Packages
{
    public static void Atomic(string dest, Action<FileStream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dest))!);
        string temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var s = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { write(s); s.Flush(true); }
            File.Move(temp, dest, true);
        } finally { File.Delete(temp); }
    }
    public static void Save(string dest, string media, Review doc,string images,CancellationToken token=default,IProgress<int>? progress=null)
    {
        doc.Check();
        if (!dest.EndsWith(".framenote", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.Text("请选择 .framenote 文件名。"));
        Atomic(dest, s => {
            using var zip = new ZipArchive(s, ZipArchiveMode.Create, true);
            token.ThrowIfCancellationRequested();
            using (var j = zip.CreateEntry("review.json").Open()) JsonSerializer.Serialize(j, doc, Review.Json);
            ReviewImages.Write(zip,doc,images);
            using var v = zip.CreateEntry("media/source" + doc.Extension, CompressionLevel.NoCompression).Open();
            using var src = File.OpenRead(media);
            var buffer=new byte[128*1024];
            int last=-1;
            while(true){
                token.ThrowIfCancellationRequested();
                int count=src.Read(buffer);
                if(count==0)break;
                v.Write(buffer,0,count);
                int percent=(int)(99L*src.Position/src.Length);
                if(percent!=last){progress?.Report(percent);last=percent;}
            }
        });
    }
    public static (Review doc,string media) Read(string path, string dir,string? images=null)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.GetEntry("review.json") is not {} j || j.Length > 16*1024*1024)
            throw new InvalidDataException(L.Text("审阅包缺少内容或结构无效。"));
        using var js = j.Open();
        var doc = JsonSerializer.Deserialize<Review>(js, Review.Json) ?? throw new InvalidDataException(L.Text("审阅数据为空。"));
        doc=doc.WithLegacyNumbers();
        doc.Check();
        ReviewImages.Read(zip,doc,images);
        var media = zip.GetEntry("media/source" + doc.Extension) ?? throw new InvalidDataException(L.Text("审阅包缺少视频。"));
        if (media.Length <= 0 || media.Length > 64L<<30 || media.Length > media.CompressedLength * 2L) throw new InvalidDataException(L.Text("不支持的媒体大小或压缩方式。"));
        Directory.CreateDirectory(dir);
        // Only use names generated locally. Content addressing avoids replacing media in an existing REAPER project.
        string tmp = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".part");
        try {
            string hash;
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)) {
                using var source = media.Open(); source.CopyTo(stream); stream.Position = 0;
                hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }
            var dest = Path.Combine(dir, hash + doc.Extension);
            if (File.Exists(dest)) {
                using var existing = File.OpenRead(dest);
                if (!Convert.ToHexString(SHA256.HashData(existing)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(L.Text("同名媒体内容不一致，未覆盖文件。"));
            } else File.Move(tmp,dest);
            return (doc,dest);
        } finally { File.Delete(tmp); }
    }
    public static void PrepareReaper(string package, string dir, string output,bool colors=false)
    {
        var assets=Path.Combine(dir,"assets");
        var (doc, media) = Read(package, dir,assets);
        if(doc.Timeline!=null){var edited=Path.Combine(dir,Guid.NewGuid()+".mp4");MediaEdits.Compose(media,doc,assets,edited,default).GetAwaiter().GetResult();media=edited;}
        if(doc.Version==2)throw new InvalidDataException(L.Text("图片审阅包不含视频时间线，无法导入 REAPER。"));
        static string Hex(string s) => Convert.ToHexString(Encoding.UTF8.GetBytes(s));
        Atomic(output, s => {
            using var w = new StreamWriter(s,new UTF8Encoding(false),1024,true);
            w.WriteLine(doc.Id); w.WriteLine(Hex(media)); w.WriteLine(doc.DurationUs);
            foreach (var n in doc.Notes) w.WriteLine(n.ReaperRow(colors));
        });
    }
}

