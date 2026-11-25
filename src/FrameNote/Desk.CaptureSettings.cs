using System.IO;
using System.Windows;
using System.Text.Json;
using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using ScreenRecorderLib;

namespace FrameNote;

public sealed partial class Desk
{
    sealed record MixerChannel(string Id,int GainDb,bool Muted);
    sealed record MixerSettings(MixerChannel[] Outputs,MixerChannel? Mic);
    sealed record CaptureCrop(string Device,int X,int Y,int Width,int Height,string? MonitorId);
    sealed record CaptureSettings(string Mode,string Source,MixerSettings Mixer,bool Cursor,CaptureCrop? Region);
    MMDeviceEnumerator? endpoints;
    readonly Dictionary<string,MMDevice> meterDevices=[];
    string CapturePrefsPath=>Path.Combine(root,"capture.json");
    Dictionary<string,JsonElement> ReadCapturePrefs(bool forStartup=false){
        try{
            using var file=new FileStream(CapturePrefsPath,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            return JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(file)??[];
        }
        catch(Exception e) when(e is FileNotFoundException or DirectoryNotFoundException or JsonException){return [];}
        catch(Exception e) when(forStartup && e is (IOException or UnauthorizedAccessException)){return [];}
    }
    void SaveCapturePrefs(JsonElement changes)=>ChangeCapturePrefs(prefs=>{
        foreach(var field in changes.EnumerateObject())
            if(field.Name is "mode" or "displayTarget" or "regionTarget" or "windowTarget" or "crop" or "cursor" or "outputs" or "mic")
                prefs[field.Name]=field.Value.Clone();
    });
    void ChangeCapturePrefs(Action<Dictionary<string,JsonElement>> edit,Action? saveRelated=null){
        // Separate windows can update unrelated controls at the same time.
        using var guard=new Mutex(false,@"Local\FrameNote.CapturePreferences");
        try{guard.WaitOne();}catch(AbandonedMutexException){}
        try{
            var prefs=ReadCapturePrefs();
            var changed=new Dictionary<string,JsonElement>(prefs);
            edit(changed);
            bool dirty=!prefs.SequenceEqual(changed);
            void Write(Dictionary<string,JsonElement> value)=>Packages.Atomic(CapturePrefsPath,s=>JsonSerializer.Serialize(s,value,Review.Json));
            if(dirty)Write(changed);
            try{saveRelated?.Invoke();}catch{if(dirty)Write(prefs);throw;}
        }finally{guard.ReleaseMutex();}
    }
    sealed record MixerOutput(string Id,string Name,bool Preferred,bool Loopback);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct DisplayDevice{
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string DeviceKey;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    static extern bool EnumDisplayDevices(string adapter,uint index,ref DisplayDevice display,uint flags);
    static string? MonitorId(string adapter){
        var display=new DisplayDevice{Size=Marshal.SizeOf<DisplayDevice>()};
        const uint getDeviceInterfaceName=1;
        return EnumDisplayDevices(adapter,0,ref display,getDeviceInterfaceName) && !string.IsNullOrEmpty(display.DeviceId)
            ? display.DeviceId:null;
    }
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
        return returns.Select(d=>new MixerOutput(d.DeviceName,d.FriendlyName+L.Text(" · 矩阵回送"),d.DeviceName==defaultReturn?.DeviceName,false))
            .Concat(renders.Select(d=>new MixerOutput(d.DeviceName,d.FriendlyName+(MatrixBus(d.FriendlyName,"In")?L.Text(" · Windows 回环"):""),
                d.IsDefaultDevice && defaultReturn==null,true))).ToArray();
    }
    bool catalogPending;
    async Task Catalog(bool initial){
        if(catalogPending)return;
        static string Handle(IntPtr h)=>h.ToInt64().ToString(CultureInfo.InvariantCulture);
        static string? WindowApp(int? pid){
            if(pid==null)return null;
            try{using var process=Process.GetProcessById(pid.Value);return process.ProcessName;}
            catch(Exception e) when(e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return null;}
        }
        var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var self=new{id=Handle(handle),name=Title+L.Text("（本窗口）"),pid=(int?)Environment.ProcessId,app=WindowApp(Environment.ProcessId),self=true};
        catalogPending=true;
        try{
            var catalog=await Task.Run(()=>new{type="catalog",preferences=initial?ReadCapturePrefs(forStartup:true):null,
                displays=Recorder.GetDisplays().Select(d=>{
                    var bounds=CropWindow.DisplayBounds(d.DeviceName);
                    return new{id=d.DeviceName,name=d.FriendlyName+"  "+d.DeviceName,model=d.FriendlyName,monitorId=MonitorId(d.DeviceName),width=bounds.Width,height=bounds.Height};
                }).ToArray(),
                windows=Recorder.GetWindows().Where(w=>w.Handle!=handle && w.Title!=L.Text("FrameNote 选区边框") && w.Title!=L.Text("FrameNote 录制控制"))
                    .Select(w=>new{id=Handle(w.Handle),name=w.Title,pid=(int?)w.Pid,app=WindowApp(w.Pid),self=false}).Append(self).ToArray(),
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
        Send(new{type="meters",levels=meterDevices.ToDictionary(p=>p.Key,p=>recorder?.RecordedPeak(p.Key)??voiceCapture?.RecordedPeak(p.Key)??Peak(p.Value))});
    }
    void InitMeters(){
        Closed+=(_,_)=>{foreach(var device in meterDevices.Values)device.Dispose();endpoints?.Dispose();ClearOutline();};
    }
    void PickRegion(JsonElement target){
        if(configuring || recorder!=null)return;
        var device=target.GetProperty("id").GetString()!;
        configuring=true;var previous=WindowState;
        try{
            regionOutline?.Hide();WindowState=WindowState.Minimized;
            var crop=new CropWindow(device,shortcuts["cancel"]);
            if(crop.ShowDialog()==true && crop.Crop is {} rect){
                var selected=new CaptureCrop(device,(int)rect.Left,(int)rect.Top,(int)rect.Width,(int)rect.Height,MonitorId(device));
                SaveCapturePrefs(JsonSerializer.SerializeToElement(new{mode="region",regionTarget=target,crop=selected},Review.Json));
                ClearOutline();regionOutline=new RegionOutline(crop.Bounds,rect);regionOutline.Show();
                Send(new{type="region",crop=selected});
            }
        }finally{WindowState=previous;Activate();regionOutline?.Show();configuring=false;}
    }
    CaptureSession Session(CaptureSettings s,bool gif,OutputSettings output){
        RecordingSourceBase source;
        if(s.Mode=="window"){
            var hwnd=new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if(!TargetsDesk(s))hwnd=(Recorder.GetWindows().FirstOrDefault(w=>w.Handle.ToInt64().ToString(CultureInfo.InvariantCulture)==s.Source)
                ??throw new InvalidOperationException(L.Text("所选窗口已关闭，请刷新采集来源。"))).Handle;
            source=new WindowRecordingSource(hwnd){IsCursorCaptureEnabled=s.Cursor};
        }else{
            var d=Recorder.GetDisplays().FirstOrDefault(d=>d.DeviceName==s.Source)??throw new InvalidOperationException(L.Text("显示器已断开，请刷新采集来源。"));
            var display=new DisplayRecordingSource(d.DeviceName){IsCursorCaptureEnabled=s.Cursor};
            if(output.HideWhileRecording && OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)){
                display.RecorderApi=RecorderApi.WindowsGraphicsCapture;
                display.IsBorderRequired=false;
            }
            if(s.Mode=="region"){
                var bounds=CropWindow.DisplayBounds(s.Source);
                if(s.Region is not {} r || r.Device!=s.Source || (r.MonitorId!=null && r.MonitorId!=MonitorId(s.Source)) ||
                    r.X<0 || r.Y<0 || r.Width<4 || r.Height<4 ||
                    r.Width%2!=0 || r.Height%2!=0 || r.X>bounds.Width-r.Width || r.Y>bounds.Height-r.Height)
                    throw new InvalidOperationException(L.Text("请重新框选录制区域。"));
                display.SourceRect=new ScreenRect(r.X,r.Y,r.Width,r.Height);
                ClearOutline();regionOutline=new RegionOutline(bounds,display.SourceRect);regionOutline.Show();
            }else if(s.Mode!="display")throw new InvalidDataException(L.Text("采集范围无效。"));
            source=display;
        }
        return new CaptureSession(source,gif?[]:ResolveAudio(s.Mixer),output.Fps,output.Quality=="high");
    }
    static List<CaptureInput> ResolveAudio(MixerSettings mixer){
        List<CaptureInput> audio=[];
        var outputs=MixerOutputs().ToDictionary(d=>d.Id);
        foreach(var channel in mixer.Outputs){
            if(!outputs.Remove(channel.Id,out var output))throw new InvalidOperationException(L.Text("输出设备已断开或重复，请刷新采集来源。"));
            audio.Add(new(channel.Id,output.Loopback,channel.GainDb,channel.Muted));
        }
        if(mixer.Mic is {} mic){
            if(audio.Any(ch=>ch.Id==mic.Id))throw new InvalidOperationException(L.Text("同一设备不能同时作为系统输出和麦克风录制。"));
            if(!Recorder.GetSystemAudioCaptureDevices().Any(d=>d.DeviceName==mic.Id))throw new InvalidOperationException(L.Text("麦克风已断开，请刷新采集来源。"));
            audio.Add(new(mic.Id,false,mic.GainDb,mic.Muted));
        }
        return audio;
    }
}
