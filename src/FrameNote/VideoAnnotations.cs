using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameNote;

// Source-pixel geometry is shared with the browser: normalized points, 2x lenses,
// three-point angles and source-pixel rulers. The footer never covers source pixels.
internal sealed class VideoAnnotations
{
    readonly Review review;
    readonly int width,height,sourceWidth,sourceHeight;
    readonly double unit;
    readonly Dictionary<string,BitmapSource> images=[];
    readonly Dictionary<string,DrawingGroup> layers=[];
    WriteableBitmap? source;
    readonly RenderTargetBitmap target;
    byte[]? output;
    readonly string assets;
    internal int OutputHeight{get;}
    static readonly Typeface Font=new("Microsoft YaHei UI");
    internal static long End(Note n,long duration)=>Math.Min(duration,n.EndUs??n.StartUs+1_000_000);
    Note[] Active(long us)=>review.Notes.Where(n=>us>=n.StartUs && us<End(n,review.DurationUs)).OrderBy(n=>n.Number).ToArray();
    FormattedText Text(string text,double size,Brush brush,double maxWidth)=>new(text,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,Font,size,brush,1){MaxTextWidth=Math.Max(1,maxWidth)};
    internal VideoAnnotations(Review review,int width,int height,int sourceWidth,int sourceHeight,string assets,bool showFooter=true){
        this.review=review;this.width=width;this.height=height;this.sourceWidth=sourceWidth;this.sourceHeight=sourceHeight;this.assets=assets;unit=Math.Max(1,Math.Max(width,height)/1600d);
        double footer=0;
        if(showFooter)
            foreach(long time in review.Notes.SelectMany(n=>new[]{n.StartUs,End(n,review.DurationUs)}).Append(0).Distinct())footer=Math.Max(footer,Footer(null,Active(time)));
        if(footer>4096 || (height+footer)*width>32_000_000)throw new InvalidDataException("同时显示的批注文字过多，请缩短文字或拆分批注时间后再导出。");
        OutputHeight=(int)Math.Ceiling((height+footer)/2)*2;
        target=new(width,OutputHeight,96,96,PixelFormats.Pbgra32);
    }
    double Footer(DrawingContext? dc,Note[] notes){
        if(review.Notes.Count==0)return 0;
        double pad=12*unit,y=height+pad;
        foreach(var n in notes){
            var color=new SolidColorBrush((Color)ColorConverter.ConvertFromString(n.DisplayColor()));
            string body=string.IsNullOrWhiteSpace(n.Text)?n.Voice!=null?"语音批注":n.Shape!=null?"图形批注":n.Pictures is {Count:>0}?"图片批注":"待填写批注":n.Text;
            var text=Text($"#{n.Number}  {body}",16*unit,Brushes.White,width-pad*3);
            dc?.DrawRectangle(color,null,new Rect(pad,y,3*unit,text.Height));dc?.DrawText(text,new Point(pad*2,y));y+=text.Height+pad;
        }
        return Math.Max(48*unit,y-height);
    }
    BitmapSource Picture(string asset){
        if(!images.TryGetValue(asset,out var bitmap)){bitmap=BitmapFrame.Create(new Uri(ReviewImages.PathFor(assets,asset)),BitmapCreateOptions.None,BitmapCacheOption.OnLoad);bitmap.Freeze();images[asset]=bitmap;}return bitmap;
    }
    DrawingGroup Layer(Note[] notes){
        string key=string.Join(',',notes.Select(n=>n.Id));if(layers.TryGetValue(key,out var cached))return cached;
        var layer=new DrawingGroup();
        using(var dc=layer.Open()){
            var badges=new List<Rect>();
            var labels=new List<Action>();
            if(OutputHeight>height)dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28,30,33)),null,new Rect(0,height,width,OutputHeight-height));
            foreach(var n in notes){
                var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(n.DisplayColor()));var pen=new Pen(brush,3*unit){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
                double badgeX=4,badgeY=4;
                foreach(var p in n.Pictures??[]){dc.DrawImage(Picture(p.Asset),new Rect(p.X*width,p.Y*height,p.Width*width,p.Width*width*p.PixelHeight/p.PixelWidth));badgeX=p.X*width;badgeY=p.Y*height-22*unit;}
                if(n.Shape is {} s){
                    var a=new Point(s.X*width,s.Y*height);var b=new Point(s.ToX*width,s.ToY*height);var rect=new Rect(a,b);badgeX=rect.X+3*unit;badgeY=rect.Y-22*unit;
                    void Line(Point p,Point q)=>dc.DrawLine(pen,p,q);
                    void Label(string value,Point at){var text=Text(value,14*unit,brush,width);var geometry=text.BuildGeometry(at);dc.DrawGeometry(null,new Pen(Brushes.Black,3*unit),geometry);dc.DrawGeometry(brush,null,geometry);}
                    if(s.Kind is "arrow" or "line" or "ruler" or "angle"){
                        Line(a,b);double angle=Math.Atan2(b.Y-a.Y,b.X-a.X);
                        if(s.Kind=="arrow"){
                            var geo=new StreamGeometry();using(var g=geo.Open()){
                                g.BeginFigure(b,true,true);g.LineTo(new Point(b.X-unit*(13*Math.Cos(angle)-6*Math.Sin(angle)),b.Y-unit*(13*Math.Sin(angle)+6*Math.Cos(angle))),true,false);
                                g.LineTo(new Point(b.X-unit*(13*Math.Cos(angle)+6*Math.Sin(angle)),b.Y-unit*(13*Math.Sin(angle)-6*Math.Cos(angle))),true,false);
                            }dc.DrawGeometry(brush,null,geo);
                        }
                        if(s.Kind=="ruler"){
                            var d=new Vector(Math.Sin(angle)*7*unit,-Math.Cos(angle)*7*unit);Line(a-d,a+d);Line(b-d,b+d);
                            double distance=Math.Sqrt(Math.Pow((s.ToX-s.X)*sourceWidth,2)+Math.Pow((s.ToY-s.Y)*sourceHeight,2));Label(distance.ToString("F1",CultureInfo.InvariantCulture)+" px",new Point((a.X+b.X)/2+4*unit,(a.Y+b.Y)/2-22*unit));
                        }
                        if(s.Kind=="angle"){
                            var c=new Point(s.ThirdX!.Value*width,s.ThirdY!.Value*height);Line(b,c);
                            double first=Math.Atan2(a.Y-b.Y,a.X-b.X),last=Math.Atan2(c.Y-b.Y,c.X-b.X),delta=Math.Atan2(Math.Sin(last-first),Math.Cos(last-first));
                            double radius=Math.Min(24*unit,Math.Min((a-b).Length,(c-b).Length)/2);
                            if(radius>0){var geo=new StreamGeometry();using(var g=geo.Open()){g.BeginFigure(new Point(b.X+radius*Math.Cos(first),b.Y+radius*Math.Sin(first)),false,false);g.ArcTo(new Point(b.X+radius*Math.Cos(last),b.Y+radius*Math.Sin(last)),new Size(radius,radius),0,false,delta>=0?SweepDirection.Clockwise:SweepDirection.Counterclockwise,true,false);}dc.DrawGeometry(null,pen,geo);}
                            Label((Math.Abs(delta)*180/Math.PI).ToString("F1",CultureInfo.InvariantCulture)+"°",new Point(b.X+8*unit,b.Y-24*unit));
                        }
                    }else{
                        dc.DrawRectangle(s.Kind=="text"?new SolidColorBrush(Color.FromArgb(221,23,25,29)):null,pen,rect);
                        if(s.Kind=="text" && rect.Width>1 && rect.Height>1){
                            dc.PushClip(new RectangleGeometry(rect));int capacity=Math.Max(1,(int)Math.Floor((rect.Width-14*unit)/(16*unit))),row=0;
                            foreach(string paragraph in n.Text.Split('\n')){
                                var chars=paragraph.EnumerateRunes().Select(r=>r.ToString()).ToArray();if(chars.Length==0)row++;
                                for(int i=0;i<chars.Length;i+=capacity)dc.DrawText(Text(string.Concat(chars.Skip(i).Take(capacity)),16*unit,brush,width),new Point(rect.X+7*unit,rect.Y+(4+22*row++)*unit));
                            }
                            dc.Pop();
                        }
                        if(s.Kind=="magnifier")Label("2×",new Point(rect.X+6*unit,rect.Bottom-21*unit));
                    }
                }
                var tag=Text("#"+n.Number,13*unit,Brushes.Black,width);badgeX=Math.Clamp(badgeX,1,Math.Max(1,width-tag.Width-8*unit));badgeY=Math.Clamp(badgeY,1,Math.Max(1,height-20*unit));
                double badgeWidth=tag.Width+8*unit,badgeHeight=20*unit;
                bool Clear(double x,double y)=>badges.All(b=>!b.IntersectsWith(new Rect(x-unit,y-unit,badgeWidth+2*unit,badgeHeight+2*unit)));
                if(!Clear(badgeX,badgeY)){
                    bool found=false;
                    for(double dx=0;dx<width&&!found;dx+=badgeWidth+3*unit)foreach(double x in new[]{badgeX+dx,badgeX-dx}){
                        if(x<0||x+badgeWidth>width)continue;
                        for(double dy=0;dy<height&&!found;dy+=badgeHeight+3*unit)foreach(double y in new[]{badgeY+dy,badgeY-dy}){
                            if(y>=0&&y+badgeHeight<=height&&Clear(x,y)){badgeX=x;badgeY=y;found=true;break;}
                        }
                        if(found)break;
                    }
                }
                var badge=new Rect(badgeX,badgeY,badgeWidth,badgeHeight);badges.Add(badge);
                labels.Add(()=>{dc.DrawRoundedRectangle(brush,null,badge,3*unit,3*unit);dc.DrawText(tag,new Point(badge.X+4*unit,badge.Y+unit));});
            }
            foreach(var label in labels)label();
            if(OutputHeight>height)Footer(dc,notes);
        }
        layer.Freeze();if(layers.Count>128)layers.Clear();layers[key]=layer;return layer;
    }
    // Each PNG starts on the first output frame whose rounded timestamp reaches the boundary.
    // FFmpeg holds it over the moving source until the next change.
    internal async Task<string> WriteOverlayTimeline(string directory,double fps,string rate,CancellationToken token,Action<int> report){
        long Timestamp(long frame)=>(long)Math.Round(frame*1e6/fps);
        long FrameAt(long us){
            long frame=(long)Math.Floor(us*fps/1e6);
            return Timestamp(frame)<us?frame+1:frame;
        }
        var changes=review.Notes.SelectMany(n=>new[]{n.StartUs,End(n,review.DurationUs)})
            .Append(0L).Where(t=>t<review.DurationUs).Select(FrameAt).Distinct().Order().ToArray();
        var playlist=new StringBuilder("ffconcat version 1.0\n");
        bool savedEmpty=false;
        for(int i=0;i<changes.Length;i++){
            token.ThrowIfCancellationRequested();
            var notes=Active(Timestamp(changes[i]));
            string name=notes.Length==0?"empty.png":$"layer-{i}.png";
            if(notes.Length!=0 || !savedEmpty){
                WriteLayerPng(Path.Combine(directory,name),notes);
                if(notes.Length==0)savedEmpty=true;
            }
            playlist.AppendLine($"file {name}").AppendLine($"option framerate {rate}");
            if(i+1<changes.Length){
                var duration=(Timestamp(changes[i+1])-Timestamp(changes[i]))/1e6;
                playlist.AppendLine("duration "+duration.ToString("F6",CultureInfo.InvariantCulture));
            }
            // Yield to the WPF dispatcher between rasterizations for progress and cancellation.
            int percent=(i+1)*100/changes.Length;
            if(percent>i*100/changes.Length)report(percent);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }
        string path=Path.Combine(directory,"layers.ffconcat");
        await File.WriteAllTextAsync(path,playlist.ToString(),new UTF8Encoding(false),token);
        return path;
    }
    internal void WriteLayerPng(string path,Note[] notes){
        var visual=new DrawingVisual();
        using(var context=visual.RenderOpen())context.DrawDrawing(Layer(notes));
        target.Clear();target.Render(visual);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(target));
        using var file=File.Create(path);png.Save(file);
    }
    internal static (Rect Target,Int32Rect Source) LensBounds(Drawing s,int width,int height){
        var rect=new Rect(new Point(s.X*width,s.Y*height),new Point(s.ToX*width,s.ToY*height));
        int x=Math.Clamp((int)(rect.X+rect.Width/4),0,width-1),y=Math.Clamp((int)(rect.Y+rect.Height/4),0,height-1);
        return (rect,new Int32Rect(x,y,Math.Clamp((int)(rect.Width/2),1,width-x),Math.Clamp((int)(rect.Height/2),1,height-y)));
    }
    internal byte[] Render(byte[] pixels,long us){
        source??=new(width,height,96,96,PixelFormats.Bgra32,null);
        output??=new byte[width*OutputHeight*4];
        source.WritePixels(new Int32Rect(0,0,width,height),pixels,width*4,0);var notes=Active(us);var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()){
            dc.DrawImage(source,new Rect(0,0,width,height));
            foreach(var n in notes.Where(n=>n.Shape?.Kind=="magnifier")){
                var (rect,bounds)=LensBounds(n.Shape!,width,height);
                dc.DrawImage(new CroppedBitmap(source,bounds),rect);
            }
            dc.DrawDrawing(Layer(notes));
        }
        target.Clear();target.Render(visual);target.CopyPixels(output,width*4,0);return output;
    }
}
