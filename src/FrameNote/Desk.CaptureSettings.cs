using System.IO;
using System.Windows;
using System.Text.Json;
using System.Globalization;
using NAudio.CoreAudioApi;
using ScreenRecorderLib;

namespace FrameNote;

public sealed partial class Desk
{
    sealed record MixerChannel(string Id,int GainDb,bool Muted);
    sealed record CaptureSettings(string Mode,string Source,int Fps,bool High,MixerChannel[] Outputs,MixerChannel? Mic,bool Cursor);
    MMDeviceEnumerator? endpoints;
    readonly Dictionary<string,MMDevice> meterDevices=[];
    string regionDevice="";
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
    void MeterSources(string[] ids){
        var selected=ids.ToHashSet(StringComparer.Ordinal);
        foreach(var id in meterDevices.Keys.Where(id=>!selected.Contains(id)).ToArray()){
            meterDevices[id].Dispose();meterDevices.Remove(id);
        }
        foreach(var id in selected.Where(id=>!meterDevices.ContainsKey(id))){
            try{meterDevices[id]=(endpoints??=new()).GetDevice(id);}catch(System.Runtime.InteropServices.COMException){}
        }
    }
    void ReadMeterPeaks(){
        static float Peak(MMDevice? device){try{return device?.AudioMeterInformation.MasterPeakValue??0;}catch(System.Runtime.InteropServices.COMException){return 0;}}
        Send(new{type="meters",levels=meterDevices.ToDictionary(p=>p.Key,p=>Peak(p.Value))});
    }
    void InitMeters(){
        Closed+=(_,_)=>{foreach(var device in meterDevices.Values)device.Dispose();endpoints?.Dispose();ClearOutline();};
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
        List<CaptureInput> audio=[];
        if(!gif){
            var outputs=Recorder.GetSystemAudioLoopbackDevices().Select(d=>d.DeviceName).ToHashSet();
            foreach(var channel in s.Outputs){
                if(!outputs.Remove(channel.Id))throw new InvalidOperationException("输出设备已断开或重复，请刷新采集来源。");
                audio.Add(new(channel.Id,true,channel.GainDb,channel.Muted));
            }
            if(s.Mic is {} mic){
                if(!Recorder.GetSystemAudioCaptureDevices().Any(d=>d.DeviceName==mic.Id))throw new InvalidOperationException("麦克风已断开，请刷新采集来源。");
                audio.Add(new(mic.Id,false,mic.GainDb,mic.Muted));
            }
        }
        return new CaptureSession(source,audio,s.Fps,s.High);
    }
}
