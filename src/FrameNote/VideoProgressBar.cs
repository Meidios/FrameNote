using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameNote;

internal static class VideoProgressBar
{
    internal static int Height(int videoHeight){
        double previousHeight=Math.Clamp(2*Math.Round(videoHeight/90d),6,24);
        return Math.Min(videoHeight,2*(int)Math.Round(previousHeight*1.1/2,MidpointRounding.AwayFromZero));
    }

    // Top: the played tint followed by transparency. Bottom: the fixed track and notes.
    // A moving crop reveals the tint over the same time-aligned notes on every frame.
    internal static void Write(string path,Review review,int width,int height,ExportBarOptions options){
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()){
            Brush Tint(string hex,double opacity)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)){Opacity=opacity};
            dc.DrawRectangle(Tint(options.Fill,.5),null,new Rect(0,0,width,height));
            dc.DrawRectangle(Tint(options.Background,.65),null,new Rect(0,height,width,height));
            DrawNotes(dc,review,width,height);
        }
        var bitmap=new RenderTargetBitmap(2*width,2*height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png=new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path);
        png.Save(file);
    }

    static void DrawNotes(DrawingContext dc,Review review,int width,int height){
        if(review.Notes.Count==0)return;
        var notes=review.Notes.OrderBy(n=>n.StartUs).ThenBy(n=>n.Number).ToArray();
        var brushes=new SolidColorBrush[notes.Length];
        var labels=new Rect[notes.Length];
        var edges=new List<(int X,int Index,bool Start)>(notes.Length*2);
        int markWidth=Math.Min(width,Math.Max(4,height/3));
        for(int i=0;i<notes.Length;i++){
            var note=notes[i];
            int start=(int)((double)note.StartUs/review.DurationUs*width);
            int x=Math.Clamp(note.EndUs.HasValue?start:start-markWidth/2,0,width-markWidth);
            int right=note.EndUs is long end?(int)Math.Ceiling((double)end/review.DurationUs*width):x+markWidth;
            right=Math.Clamp(Math.Max(right,x+markWidth),0,width);
            edges.Add((x,i,true));edges.Add((right,i,false));
            brushes[i]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(note.DisplayColor()));
        }
        // Share height only over the pixels where notes actually coincide.
        var active=new SortedSet<int>();
        int left=0;
        foreach(var edge in edges.OrderBy(e=>e.X).GroupBy(e=>e.X)){
            int right=edge.Key, laneCount=Math.Min(active.Count,height), lane=0;
            foreach(int i in active.Take(laneCount)){
                int top=lane*height/laneCount, bottom=(lane+1)*height/laneCount;
                var rect=new Rect(left,height+top,right-left,bottom-top);
                dc.DrawRectangle(brushes[i],null,rect);
                if(notes[i].EndUs!=null && rect.Height>=12 && rect.Width>labels[i].Width)labels[i]=rect;
                lane++;
            }
            foreach(var change in edge){
                if(change.Start)active.Add(change.Index);else active.Remove(change.Index);
            }
            left=right;
        }

        var font=new Typeface(DeskTheme.UiFont,FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
        for(int i=0;i<notes.Length;i++){
            var area=labels[i];
            if(area.Width<20)continue;
            var color=brushes[i].Color;
            var ink=DeskTheme.UseWhiteInk(color)?Brushes.White:Brushes.Black;
            var label=new FormattedText($"#{notes[i].Number}",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,font,area.Height*.69,ink,1);
            if(label.Width+6<=area.Width)dc.DrawText(label,new Point(area.X+(area.Width-label.Width)/2,area.Y+(area.Height-label.Height)/2));
        }
    }

    internal static void AddFilters(List<string> filters,string scene,int input,int width,int height,long durationUs,double fps){
        // Only the tiny cached strip advances every frame. The final overlay is
        // part of the existing encode, including when the scene comes from the live lens pipe.
        string duration=(durationUs/1e6).ToString("R",CultureInfo.InvariantCulture);
        string last=Math.Max(0,durationUs/1e6-1/fps).ToString("R",CultureInfo.InvariantCulture);
        filters.Add($"[{input}:v]format=rgba,loop=loop=-1:size=1:start=0,split[barColors][barTrack]");
        filters.Add($"[barColors]crop={width*2}:{height}:0:0,"+
            $"crop={width}:{height}:x='if(gte(t,{last}),0,ow*(1-t/{duration}))':y=0[barFill]");
        filters.Add($"[barTrack]crop={width}:{height}:0:{height}[barBase]");
        filters.Add("[barBase][barFill]overlay=format=rgb[bar]");
        filters.Add($"{scene}[bar]overlay=x=0:y=main_h-overlay_h:eof_action=repeat[withProgress]");
    }
}
