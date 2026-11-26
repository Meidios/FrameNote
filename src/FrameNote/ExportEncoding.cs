using System.IO;

namespace FrameNote;

internal static class ExportEncoding
{
    // Keep the existing error diffusion palette but skip unchanged areas of screen recordings.
    internal const string GifDither="dither=sierra2_4a:diff_mode=rectangle";
    static readonly Lazy<Task<bool>> Nvenc=new(CheckNvenc);
    static async Task<bool> CheckNvenc(){
        try{
            await Probe.Run("ffmpeg","-v","error","-nostdin","-f","lavfi","-i","color=s=256x256","-frames:v","1","-c:v","h264_nvenc","-preset","p4","-f","null","-");
            return true;
        }catch(InvalidDataException){return false;}
    }

    internal static async Task H264(int width,int height,int quality,string output,CancellationToken token,Func<string[],Task> write){
        if(width>=320 && height>=160 && width*height>=1280*720 && await Nvenc.Value.WaitAsync(token)){
            try{
                // NVENC CQ and x264 CRF have different scales. B-frames keep long screen captures compact.
                await write(["-c:v","h264_nvenc","-preset","p4","-tune","hq","-rc","vbr","-cq",(quality+2).ToString(),"-bf","3","-b:v","0"]);
                return;
            }catch(IOException e) when(!token.IsCancellationRequested){
                Serilog.Log.Warning(e,"Hardware encoding failed, retrying this export with x264");
                File.Delete(output);
            }
        }
        token.ThrowIfCancellationRequested();
        await write(["-c:v","libx264","-preset","veryfast","-crf",quality.ToString()]);
    }
}
