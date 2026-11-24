using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameNote;

internal static class FrameDetails
{

    static readonly Typeface Normal=new(DeskTheme.UiFont,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
    static readonly Typeface Bold=new(Normal.FontFamily,FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
    static string Stamp(TimeSpan t)=>(t.TotalHours>=1?$"{(long)t.TotalHours:00}:":"")+t.ToString(@"mm\:ss\.fff",CultureInfo.InvariantCulture);
    static string Stamp(long us)=>Stamp(TimeSpan.FromMilliseconds((us+500)/1000));

    public static byte[] Append(Stream input,string source,TimeSpan? time,IReadOnlyList<Note> notes,IReadOnlyDictionary<long,RulerScale>? scales,string rulerUnit){
        var frame=BitmapFrame.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        int width=Math.Max(256,frame.PixelWidth),imageHeight=frame.PixelHeight;
        double unit=Math.Clamp(width/1100d,1,3),pad=Math.Round(16*unit),x=pad+11*unit;
        double y=imageHeight+pad;
        var visual=new DrawingVisual();
        var background=new SolidColorBrush(Color.FromRgb(28,30,33));
        var muted=new SolidColorBrush(Color.FromRgb(182,193,201));
        void CheckHeight(double h){
            if(h>32767||width*h>64_000_000)throw new InvalidOperationException(L.Text("当前帧的批注过多，合成图片超出尺寸上限。可导出审阅包保留全部内容。"));
        }
        using(var dc=visual.RenderOpen()){
            void Line(string value,double size,Typeface face,Brush brush,double left){
                var text=new FormattedText(value,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,size*unit,brush,1){
                    MaxTextWidth=width-pad-left
                };
                CheckHeight(y+text.Height+pad);
                dc.DrawText(text,new Point(left,y));y+=Math.Ceiling(text.Height);
            }
            dc.DrawImage(frame,new Rect((width-frame.PixelWidth)/2,0,frame.PixelWidth,imageHeight));
            string timestamp=time is {} t?"　·　"+Stamp(TimeSpan.FromMilliseconds((t.Ticks+5000)/10000)):"";
            Line(L.Format($"{source}{timestamp}　·　{notes.Count} 条批注"),15,Bold,Brushes.White,pad);
            foreach(var note in notes.OrderBy(n=>n.Number)){
                y+=11*unit;double top=y;
                string range=time==null?L.Text("图片批注"):note.EndUs is long end?L.Format($"区间批注　{Stamp(note.StartUs)} – {Stamp(end)}"):L.Format($"点批注　{Stamp(note.StartUs)}");
                string voice=note.Voice is {} audio?L.Format($"　·　语音 {audio.DurationUs/1e6:F1} 秒"):"";
                string attachment=note.Pictures is {Count:>0} pics?L.Format($"　·　参考图 ×{pics.Count}"):"";
                string shape=note.Shape?.Kind switch{"arrow"=>L.Text("　·　箭头"),"box"=>L.Text("　·　框选"),"line"=>L.Text("　·　线"),"angle"=>L.Text("　·　角度"),"ruler"=>L.Text("　·　距离标尺"),"magnifier"=>"　·　"+note.Shape.LensLabel+L.Text(" 放大镜"),"text"=>L.Text("　·　文字框"),_=>""};
                var color=new SolidColorBrush((Color)ColorConverter.ConvertFromString(note.DisplayColor()));
                Line($"#{note.Number}　{range}{shape}{attachment}{voice}",14,Bold,muted,x);
                string body=note.DisplayMeasurementText(scales,frame.PixelWidth,imageHeight,rulerUnit);
                if(!string.IsNullOrWhiteSpace(body)){
                    y+=3*unit;Line(body.ReplaceLineEndings("\n"),17,Normal,Brushes.White,x);
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
