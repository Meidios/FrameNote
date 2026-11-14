using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Text.Json;
using System.Globalization;
using NAudio.CoreAudioApi;
using ScreenRecorderLib;

namespace FrameNote;

public sealed partial class Desk
{
    sealed record CaptureSettings(string Mode,string Source,int Fps,bool High,string Output,string Mic,bool Cursor);
    readonly DispatcherTimer meters=new(){Interval=TimeSpan.FromMilliseconds(160)};
    MMDeviceEnumerator? endpoints;
    MMDevice? outMeter,micMeter;
    string outputId="",micId="",regionDevice="";
    ScreenRect? area;
    Int32Rect regionMonitor;
    void Catalog(){
        static string Handle(IntPtr h)=>h.ToInt64().ToString(CultureInfo.InvariantCulture);
        Send(new{type="catalog",
            displays=Recorder.GetDisplays().Select(d=>new{id=d.DeviceName,name=d.FriendlyName+"  "+d.DeviceName}),
            windows=Recorder.GetWindows().Where(w=>w.Handle!=new System.Windows.Interop.WindowInteropHelper(this).Handle && w.Title!="FrameNote 选区边框").Select(w=>new{id=Handle(w.Handle),name=w.Title}),
            outputs=Recorder.GetSystemAudioLoopbackDevices().Select(d=>new{id=d.DeviceName,name=d.FriendlyName,preferred=d.IsDefaultDevice}),
            inputs=Recorder.GetSystemAudioCaptureDevices().Select(d=>new{id=d.DeviceName,name=d.FriendlyName})});
    }
    void MeterSources(string output,string mic){
        MMDevice? Device(string id){
            if(id=="")return null;
            try{return (endpoints??=new()).GetDevice(id);}catch(System.Runtime.InteropServices.COMException){return null;}
        }
        if(output!=outputId){outMeter?.Dispose();outMeter=Device(output);outputId=output;}
        if(mic!=micId){micMeter?.Dispose();micMeter=Device(mic);micId=mic;}
        if(outMeter!=null||micMeter!=null)meters.Start();else meters.Stop();
    }
    void InitMeters(){
        static float Peak(MMDevice? device){try{return device?.AudioMeterInformation.MasterPeakValue??0;}catch(System.Runtime.InteropServices.COMException){return 0;}}
        meters.Tick+=(_,_)=>Send(new{type="meters",output=Peak(outMeter),mic=Peak(micMeter)});
        Closed+=(_,_)=>{meters.Stop();outMeter?.Dispose();micMeter?.Dispose();endpoints?.Dispose();ClearOutline();};
    }
    void ResetRegion(){ClearOutline();area=null;regionDevice="";}
    void PickRegion(string device){
        if(configuring || recorder!=null)return;
        configuring=true;var previous=WindowState;
        try{
            regionOutline?.Hide();WindowState=WindowState.Minimized;
            var crop=new CropWindow(device);
            if(crop.ShowDialog()==true && crop.Crop is {} rect){
                ResetRegion();area=rect;regionDevice=device;regionMonitor=crop.Bounds;
                regionOutline=new RegionOutline(regionMonitor,rect);regionOutline.Show();
            }
        }finally{WindowState=previous;Activate();regionOutline?.Show();configuring=false;
            Send(new{type="region",device=regionDevice,width=area?.Width,height=area?.Height});}
    }
    CaptureSession Session(CaptureSettings s,bool gif){
        if(s.Fps!=30 && s.Fps!=60)throw new InvalidDataException("录制帧率无效。");
        RecordingSourceBase source;
        if(s.Mode=="window"){
            var win=Recorder.GetWindows().FirstOrDefault(w=>w.Handle.ToInt64().ToString(CultureInfo.InvariantCulture)==s.Source)
                ??throw new InvalidOperationException("所选窗口已关闭，请刷新采集来源。");
            source=new WindowRecordingSource(win.Handle){IsCursorCaptureEnabled=s.Cursor};
        }else{
            var d=Recorder.GetDisplays().FirstOrDefault(d=>d.DeviceName==s.Source)??throw new InvalidOperationException("显示器已断开，请刷新采集来源。");
            var display=new DisplayRecordingSource(d.DeviceName){IsCursorCaptureEnabled=s.Cursor};
            if(s.Mode=="region"){
                if(area==null||regionDevice!=s.Source)throw new InvalidOperationException("请先框选录制区域。");
                display.SourceRect=area;
                if(regionOutline==null){regionOutline=new RegionOutline(regionMonitor,area);regionOutline.Show();}
            }else if(s.Mode!="display")throw new InvalidDataException("采集范围无效。");
            source=display;
        }
        List<AudioSourceBase> audio=[];
        if(!gif && s.Output!="")audio.Add(Recorder.GetSystemAudioLoopbackDevices().FirstOrDefault(d=>d.DeviceName==s.Output)??throw new InvalidOperationException("系统声设备已断开。"));
        if(!gif && s.Mic!="")audio.Add(Recorder.GetSystemAudioCaptureDevices().FirstOrDefault(d=>d.DeviceName==s.Mic)??throw new InvalidOperationException("麦克风已断开。"));
        return new CaptureSession(source,audio,s.Fps,s.High);
    }
}
