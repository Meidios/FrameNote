using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace FrameNote;

// Width and origin are fractions of the video frame. Image height is always
// derived from the original aspect ratio, never resized independently.
public record Picture(string Asset,int PixelWidth,int PixelHeight,double X,double Y,double Width);

internal static class ReviewImages
{
    internal const int MaxBytes=20*1024*1024,MaxSide=2048;
    internal static string[] Assets(Review review)=>review.StoredNotes().SelectMany(n=>n.Pictures??[]).Select(p=>p.Asset).Distinct().ToArray();
    internal static string PathFor(string dir,string hash)=>Path.Combine(dir,hash+".png");
    internal static (string hash,int width,int height) Store(string dir,byte[] data,string? expected=null){
        if(data.Length<33||data.Length>MaxBytes||!data.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||
            !data.AsSpan(12,4).SequenceEqual("IHDR"u8))throw new InvalidDataException(L.Text("图片数据无效。"));
        int width=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16)),height=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20));
        if(width is <1 or >MaxSide||height is <1 or >MaxSide)throw new InvalidDataException(L.Text("图片尺寸超出限制。"));
        var hash=Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if(expected!=null&&expected!=hash)throw new InvalidDataException(L.Text("审阅包图片校验失败。"));
        var dest=PathFor(dir,hash);if(!File.Exists(dest))Packages.Atomic(dest,s=>s.Write(data));
        return (hash,width,height);
    }
    internal static void Write(ZipArchive zip,Review review,string dir){
        ReviewVoice.Write(zip,review,dir);
        foreach(var hash in Assets(review)){
            using var src=File.OpenRead(PathFor(dir,hash));
            using var dst=zip.CreateEntry("images/"+hash+".png",CompressionLevel.NoCompression).Open();src.CopyTo(dst);
        }
    }
    internal static void Read(ZipArchive zip,Review review,string? dir){
        var hashes=Assets(review);
        if(zip.Entries.Count!=2+hashes.Length+ReviewVoice.Assets(review).Length)throw new InvalidDataException(L.Text("审阅包附件数量不符。"));
        ReviewVoice.Read(zip,review,dir);
        foreach(var hash in hashes){
            var e=zip.GetEntry("images/"+hash+".png");
            if(e==null||e.Length is <33 or >MaxBytes)throw new InvalidDataException(L.Text("审阅包图片缺失或无效。"));
            if(dir==null)continue; // REAPER only needs the video and marker text.
            using var stream=e.Open();var data=new byte[(int)e.Length];stream.ReadExactly(data);
            Store(dir,data,hash);
        }
    }
}
