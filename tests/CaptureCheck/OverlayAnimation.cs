using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using FrameNote;
using Cursor=System.Windows.Forms.Cursor;
using Screen=System.Windows.Forms.Screen;

internal static class OverlayAnimation
{
    internal static int Run(){
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        DeskTheme.Start(app);
        var screen=Screen.PrimaryScreen!;
        var original=Cursor.Position;
        var outside=new System.Drawing.Point(screen.WorkingArea.Left+50,screen.WorkingArea.Top+50);
        Cursor.Position=outside;
        var controls=new RecordingControls(screen,()=>{},()=>{});
        try{
            controls.ShowForCapture(false);
            controls.Dispatcher.InvokeAsync(async ()=>{
                try{
                    await Task.Delay(250);
                    var rotation=(RotateTransform)controls.FindName("StopRotation");
                    var slide=(TranslateTransform)controls.FindName("PauseOffset");
                    var button=(FrameworkElement)controls.FindName("StopButton");
                    var center=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));
                    var inside=new System.Drawing.Point((int)center.X,(int)center.Y);
                    double travel=slide.Y;
                    if(rotation.Angle!=0 || travel<=0)throw new Exception("Expected a collapsed overlay");

                    async Task<List<double>> MoveUntil(System.Drawing.Point pointer,Func<double,bool> arrived){
                        var angles=new List<double>{rotation.Angle};
                        Cursor.Position=pointer;
                        var watch=Stopwatch.StartNew();
                        while(!arrived(angles[^1])){
                            if(watch.ElapsedMilliseconds>2000)throw new TimeoutException("Overlay did not reach its target");
                            await Task.Delay(6);
                            angles.Add(rotation.Angle);
                            if(Math.Abs(slide.Y-travel*(1-rotation.Angle/90))>1)
                                throw new Exception("Pause motion and stop rotation are out of sync");
                        }
                        return angles;
                    }
                    static bool Increasing(IEnumerable<double> angles)=>angles.SequenceEqual(angles.Order());
                    var opening=await MoveUntil(inside,a=>a==90);
                    var closing=await MoveUntil(outside,a=>a==0);
                    if(!Increasing(opening) || !Increasing(closing.AsEnumerable().Reverse()) ||
                        opening.Distinct().Count()<4 || closing.Distinct().Count()<4)
                        throw new Exception("Expected a smooth clockwise quarter-turn and counterclockwise return");

                    await MoveUntil(inside,a=>a>=18);
                    var reversed=await MoveUntil(outside,a=>a==0);
                    int peak=reversed.IndexOf(reversed.Max());
                    if(reversed[peak]>=90 || !Increasing(reversed.Take(peak+1)) ||
                        !Increasing(reversed.Skip(peak).Reverse()))
                        throw new Exception("Interrupted rotation jumped to an endpoint or changed direction twice");
                    Console.WriteLine($"Overlay rotation 0 -> 90 -> 0, reversed mid-flight at {reversed[peak]:0.0} degrees");
                    app.Shutdown(0);
                }catch(Exception e){Console.Error.WriteLine(e);app.Shutdown(1);}
            });
            return app.Run();
        }finally{Cursor.Position=original;}
    }
}
