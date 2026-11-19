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
    sealed record MixerOutput(string Id,string Name,bool Preferred,bool Loopback);
    static MixerOutput[] MixerOutputs(){
        var renders=Recorder.GetSystemAudioLoopbackDevices();
        var inputs=Recorder.GetSystemAudioCaptureDevices();
        // Matrix In is a playback endpoint. Its mix is exposed to recorders on
        // a separately routable Matrix Out bus, not necessarily on WASAPI loopback.
        static bool MatrixBus(string name,string side)=>System.Text.RegularExpressions.Regex.IsMatch(
            name,@"^VBMatrix "+side+@" [0-9]+ \(VB-Audio Matrix VAIO\)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var returns=inputs.Where(d=>MatrixBus(d.FriendlyName,"Out")).ToArray();
        var preferred=renders.FirstOrDefault(d=>d.IsDefaultDevice);
        var returnName=preferred!=null && MatrixBus(preferred.FriendlyName,"In")
            ? preferred.FriendlyName.Replace("VBMatrix In ","VBMatrix Out ",StringComparison.OrdinalIgnoreCase):null;
        var defaultReturn=returns.FirstOrDefault(d=>string.Equals(d.FriendlyName,returnName,StringComparison.OrdinalIgnoreCase));
        return returns.Select(d=>new MixerOutput(d.DeviceName,d.FriendlyName+" · 矩阵回送",d.DeviceName==defaultReturn?.DeviceName,false))
            .Concat(renders.Select(d=>new MixerOutput(d.DeviceName,d.FriendlyName+(MatrixBus(d.FriendlyName,"In")?" · Windows 回环":""),
                d.IsDefaultDevice && defaultReturn==null,true))).ToArray();
    }
    bool catalogPending;
    async Task Catalog(){
        if(catalogPending)return;
        static string Handle(IntPtr h)=>h.ToInt64().ToString(CultureInfo.InvariantCulture);
        var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;
        catalogPending=true;
        try{
            var catalog=await Task.Run(()=>new{type="catalog",
                displays=Recorder.GetDisplays().Select(d=>new{id=d.DeviceName,name=d.FriendlyName+"  "+d.DeviceName}).ToArray(),
                windows=Recorder.GetWindows().Where(w=>w.Handle!=handle && w.Title!="FrameNote 选区边框").Select(w=>new{id=Handle(w.Handle),name=w.Title}).ToArray(),
                outputs=MixerOutputs().Select(d=>new{id=d.Id,name=d.Name,preferred=d.Preferred}).ToArray(),
                inputs=Recorder.GetSystemAudioCaptureDevices().Select(d=>new{id=d.DeviceName,name=d.FriendlyName,preferred=d.IsDefaultDevice}).ToArray()},lifetime.Token);
            Send(catalog);
        }finally{catalogPending=false;}
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
        Send(new{type="meters",levels=meterDevices.ToDictionary(p=>p.Key,p=>recorder is {} session?session.RecordedPeak(p.Key):Peak(p.Value))});
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
            var outputs=MixerOutputs().ToDictionary(d=>d.Id);
            foreach(var channel in s.Outputs){
                if(!outputs.Remove(channel.Id,out var output))throw new InvalidOperationException("输出设备已断开或重复，请刷新采集来源。");
                audio.Add(new(channel.Id,output.Loopback,channel.GainDb,channel.Muted));
            }
            if(s.Mic is {} mic){
                if(audio.Any(ch=>ch.Id==mic.Id))throw new InvalidOperationException("同一设备不能同时作为系统输出和麦克风录制。");
                if(!Recorder.GetSystemAudioCaptureDevices().Any(d=>d.DeviceName==mic.Id))throw new InvalidOperationException("麦克风已断开，请刷新采集来源。");
                audio.Add(new(mic.Id,false,mic.GainDb,mic.Muted));
            }
        }
        return new CaptureSession(source,audio,s.Fps,s.High);
    }
}
