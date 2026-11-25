using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace FrameNote;

internal static class ReviewVoice
{
    internal const long MaxBytes=2L*1024*1024*1024;
    internal const string Extensions=".wav .wave .mp3 .flac .ogg .oga .opus .m4a .aac .aif .aiff .wma";
    internal static bool ValidFormat(string? format)=>format is null or "mp3";
    internal static bool Supports(string path)=>Extensions.Split(' ').Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    internal static bool ValidOriginal(OriginalAudio a)=>a.Asset is {Length:64} && a.Asset.All(c=>char.IsAsciiDigit(c)||c is >= 'a' and <= 'f') &&
        a.Name is {Length:>0 and <=255} && a.Name.IndexOfAny(Path.GetInvalidFileNameChars())<0 && Supports(a.Name);
    internal static string PathFor(string dir,string hash,string? format=null)=>Path.Combine(dir,hash+"."+(format??"m4a"));
    internal static string[] Assets(Review doc){
        var files=new HashSet<string>();
        foreach(var n in doc.StoredNotes())if(n.Voice is {} v){
            files.Add(v.Asset+"."+(v.Format??"m4a"));
            if(v.Original is {} source)files.Add(source.Asset+".original");
        }
        foreach(var clip in (doc.Timeline?.Clips??[]).Concat((doc.Timeline?.Trimmed??[]).Select(x=>x.Voice).OfType<EditClip>()))
            if(clip.Track=="voice")files.Add(clip.Asset+"."+(clip.Format??"m4a"));
        return [..files];
    }
    internal static string StoreFile(string dir,string path,string extension){
        using var src=File.OpenRead(path);
        if(src.Length is <1 or >MaxBytes)throw new InvalidDataException(L.Text("音频附件过大或为空。"));
        string hash=Convert.ToHexString(SHA256.HashData(src)).ToLowerInvariant();
        src.Position=0;string dest=Path.Combine(dir,hash+"."+extension);
        if(!File.Exists(dest))Packages.Atomic(dest,src.CopyTo);
        return hash;
    }
    internal static void Write(ZipArchive zip,Review doc,string dir){
        foreach(string file in Assets(doc)){
            using var input=File.OpenRead(Path.Combine(dir,file));using var output=zip.CreateEntry("voice/"+file,CompressionLevel.NoCompression).Open();input.CopyTo(output);
        }
    }
    internal static void Read(ZipArchive zip,Review doc,string? dir){
        foreach(string file in Assets(doc)){
            var entry=zip.GetEntry("voice/"+file);
            if(entry==null || entry.Length is <1 or >MaxBytes)throw new InvalidDataException(L.Text("语音附件缺失或过大。"));
            if(dir==null)continue;
            Packages.Atomic(Path.Combine(dir,file),output=>{
                using var input=entry.Open();using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer=new byte[81920];int count;
                int header=input.ReadAtLeast(buffer,12,throwOnEndOfStream:false);
                var bytes=buffer.AsSpan(0,header);
                bool invalid=file.EndsWith(".m4a") && (header<12||!bytes.Slice(4,4).SequenceEqual("ftyp"u8))
                    || file.EndsWith(".mp3") && !(bytes.StartsWith("ID3"u8)||header>=2&&bytes[0]==0xff&&(bytes[1]&0xe0)==0xe0);
                if(invalid)throw new InvalidDataException(L.Text("语音附件格式或大小无效。"));
                digest.AppendData(bytes);output.Write(bytes);
                while((count=input.Read(buffer))>0){
                    if(output.Position+count>entry.Length)throw new InvalidDataException(L.Text("语音附件缺失或过大。"));
                    digest.AppendData(buffer,0,count);output.Write(buffer,0,count);
                }
                string hash=Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
                if(output.Position!=entry.Length||Path.GetFileNameWithoutExtension(file)!=hash)throw new InvalidDataException(L.Text("语音附件校验失败。"));
            });
        }
    }
}
