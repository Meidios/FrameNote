using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameNote;

internal static class FrameDetails
{
    static readonly CultureInfo Chinese=CultureInfo.GetCultureInfo("zh-CN");
    static readonly Typeface Normal=new("Microsoft YaHei UI");
    static readonly Typeface Bold=new(Normal.FontFamily,FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
    static string Stamp(TimeSpan t)=>(t.TotalHours>=1?$"{(long)t.TotalHours:00}:":"")+t.ToString(@"mm\:ss\.fff",CultureInfo.InvariantCulture);
    static string Stamp(long us)=>Stamp(TimeSpan.FromMilliseconds((us+500)/1000));

    public static byte[] Append(byte[] png,string source,TimeSpan time,IReadOnlyList<Note> notes){
        using var input=new MemoryStream(png);
        return Append(input,source,time,notes);
    }
    public static byte[] Append(Stream input,string source,TimeSpan? time,IReadOnlyList<Note> notes){
        var frame=BitmapFrame.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        int width=Math.Max(256,frame.PixelWidth),imageHeight=frame.PixelHeight;
        double unit=Math.Clamp(width/1100d,1,3),pad=Math.Round(16*unit),x=pad+11*unit;
        double y=imageHeight+pad;
        var visual=new DrawingVisual();
        var background=new SolidColorBrush(Color.FromRgb(28,30,33));
        var muted=new SolidColorBrush(Color.FromRgb(182,193,201));
        void CheckHeight(double h){
            if(h>32767||width*h>64_000_000)throw new InvalidOperationException("当前帧的批注过多，合成图片超出尺寸上限。可导出审阅包保留全部内容。");
        }
        using(var dc=visual.RenderOpen()){
            void Line(string value,double size,Typeface face,Brush brush,double left){
                var text=new FormattedText(value,Chinese,FlowDirection.LeftToRight,face,size*unit,brush,1){
                    MaxTextWidth=width-pad-left
                };
                CheckHeight(y+text.Height+pad);
                dc.DrawText(text,new Point(left,y));y+=Math.Ceiling(text.Height);
            }
            dc.DrawImage(frame,new Rect((width-frame.PixelWidth)/2,0,frame.PixelWidth,imageHeight));
            string timestamp=time is {} t?"　·　"+Stamp(TimeSpan.FromMilliseconds((t.Ticks+5000)/10000)):"";
            Line($"{source}{timestamp}　·　{notes.Count} 条批注",15,Bold,Brushes.White,pad);
            foreach(var note in notes.OrderBy(n=>n.Number)){
                y+=11*unit;double top=y;
                string range=time==null?"图片批注":note.EndUs is long end?$"区间批注　{Stamp(note.StartUs)} – {Stamp(end)}":$"点批注　{Stamp(note.StartUs)}";
                string attachment=note.Pictures is {Count:>0} pics?$"　·　参考图 ×{pics.Count}":"";
                string shape=note.Shape?.Kind switch{"arrow"=>"　·　箭头","box"=>"　·　框选",_=>""};
                var color=new SolidColorBrush((Color)ColorConverter.ConvertFromString(note.DisplayColor()));
                Line($"#{note.Number}　{range}{shape}{attachment}",14,Bold,muted,x);
                if(!string.IsNullOrWhiteSpace(note.Text)){
                    y+=3*unit;Line(note.Text.ReplaceLineEndings("\n"),17,Normal,Brushes.White,x);
                }
                dc.DrawRectangle(color,null,new Rect(pad,top,4*unit,y-top));
            }
        }
        int height=checked((int)Math.Ceiling(y+pad));CheckHeight(height);
        var page=new DrawingVisual();
        using(var dc=page.RenderOpen()){
            // Keep the image's transparency; only the annotation legend needs a background.
            dc.DrawRectangle(background,null,new Rect(0,imageHeight,width,height-imageHeight));dc.DrawDrawing(visual.Drawing);
        }
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(page);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=new MemoryStream();encoder.Save(output);return output.ToArray();
    }
}
