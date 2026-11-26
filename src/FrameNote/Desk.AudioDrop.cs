using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace FrameNote;

public sealed partial class Desk
{
    Popup? audioDropTarget;
    (Guid Id,Guid DocumentId,long At,string[] Files)? releasedAudioDrop;

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);
    static bool DragButtonDown()=>GetAsyncKeyState(1)<0||GetAsyncKeyState(2)<0;

    // Chromium protects File objects until drop. During an external drag only, let the
    // visible part of the voice lane receive the Windows file list and pointer together.
    void WatchAudioDrop(JsonElement bounds){
        if(audioDropTarget!=null||!web.IsVisible||doc==null||viewingImage||pending||
            doc.Id!=bounds.GetProperty("documentId").GetGuid()||!DragButtonDown())return;
        double Field(string name)=>bounds.GetProperty(name).GetDouble();
        double scale=web.ActualWidth/Field("viewportWidth");
        double left=Field("left"),axisLeft=Field("axisLeft"),axisWidth=Field("axisWidth");
        if(!double.IsFinite(scale)||scale<=0||axisWidth<=0)return;
        var documentId=doc.Id;
        long duration=doc.DurationUs;
        var surface=new Border{Width=Field("width")*scale,Height=Field("height")*scale,AllowDrop=true,
            Background=new SolidColorBrush(Color.FromArgb(1,0,0,0))};
        var popup=new Popup{Child=surface,PlacementTarget=web,Placement=PlacementMode.Relative,
            HorizontalOffset=left*scale,VerticalOffset=Field("top")*scale,AllowsTransparency=true,StaysOpen=true,Focusable=false};
        var watchdog=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        var probe=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token=probe.Token;
        string[] files=[];
        bool entered=false,closed=false;
        long at=-1;
        void End(bool dropped=false){
            if(closed)return;
            closed=true;watchdog.Stop();probe.Cancel();probe.Dispose();audioDropTarget=null;popup.IsOpen=false;
            if(!dropped)Send(new{type="audioDropEnd",documentId});
        }
        void Position(DragEventArgs e){
            e.Handled=true;e.Effects=files.Length>0?(e.AllowedEffects&DragDropEffects.Copy):DragDropEffects.None;
            if(e.Effects==DragDropEffects.None)return;
            double x=web.PointFromScreen(surface.PointToScreen(e.GetPosition(surface))).X/scale;
            long next=Math.Clamp((long)Math.Round(duration*(x-axisLeft)/axisWidth),0,duration-1);
            if(at==next)return;
            at=next;Send(new{type="audioDropMove",documentId,at});
        }
        surface.DragEnter+=async (_,e)=>{
            entered=true;
            files=e.Data.GetData(DataFormats.FileDrop) is string[] paths&&paths.All(ReviewVoice.Supports)?paths:[];
            Position(e);
            if(files.Length==0)return;
            try{
                var lengths=new List<long>();
                foreach(string file in files)lengths.Add(await Probe.AudioDuration(file,token));
                if(!closed)Send(new{type="audioDropLengths",documentId,at,lengths});
            }catch(OperationCanceledException) when(token.IsCancellationRequested){}
            catch(Exception ex){Serilog.Log.Debug(ex,"Audio drop duration unavailable");}
        };
        surface.DragOver+=(_,e)=>Position(e);
        surface.DragLeave+=(_,e)=>{e.Handled=true;End();};
        surface.Drop+=(_,e)=>{
            Position(e);
            if(e.Effects==DragDropEffects.None||doc?.Id!=documentId){End();return;}
            var id=Guid.NewGuid();releasedAudioDrop=(id,documentId,at,files);
            End(true);Send(new{type="audioDropReleased",id,documentId,at});
        };
        watchdog.Tick+=(_,_)=>{
            if(!DragButtonDown()||doc?.Id!=documentId||!web.IsVisible||
                (!entered&&!new Rect(surface.RenderSize).Contains(System.Windows.Input.Mouse.GetPosition(surface))))End();
        };
        audioDropTarget=popup;popup.IsOpen=true;watchdog.Start();
    }
}
