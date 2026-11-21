using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace FrameNote;

internal static class ReviewVoice
{
    internal const int MaxBytes=32*1024*1024;
    internal static string[] Assets(Review doc)=>doc.Notes.Where(n=>n.Voice!=null).Select(n=>n.Voice!.Asset).Concat(doc.Timeline?.Clips.Where(c=>c.Track=="voice").Select(c=>c.Asset!)??[]).Distinct().ToArray();
    internal static string PathFor(string dir,string hash)=>Path.Combine(dir,hash+".m4a");
    internal static string Store(string dir,byte[] bytes,string? expected=null){
        if(bytes.Length<12 || bytes.Length>MaxBytes || !bytes.AsSpan(4,4).SequenceEqual("ftyp"u8))throw new InvalidDataException("语音附件格式或大小无效。");
        string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if(expected!=null && expected!=hash)throw new InvalidDataException("语音附件校验失败。");
        string dest=PathFor(dir,hash);if(!File.Exists(dest))Packages.Atomic(dest,s=>s.Write(bytes));return hash;
    }
    internal static void Write(ZipArchive zip,Review doc,string dir){
        foreach(string hash in Assets(doc)){
            using var input=File.OpenRead(PathFor(dir,hash));using var output=zip.CreateEntry("voice/"+hash+".m4a",CompressionLevel.NoCompression).Open();input.CopyTo(output);
        }
    }
    internal static void Read(ZipArchive zip,Review doc,string? dir){
        foreach(string hash in Assets(doc)){
            var entry=zip.GetEntry("voice/"+hash+".m4a");
            if(entry==null || entry.Length<12 || entry.Length>MaxBytes)throw new InvalidDataException("语音附件缺失或过大。");
            if(dir==null)continue;
            using var input=entry.Open();byte[] bytes=new byte[(int)entry.Length];input.ReadExactly(bytes);Store(dir,bytes,hash);
        }
    }
}
