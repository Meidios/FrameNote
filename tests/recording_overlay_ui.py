"""Record a synthetic monitor and the render/capture endpoints of VB-CABLE. Requires sounddevice."""
import contextlib
import ctypes
import json
import multiprocessing as mp
import os
import shutil
import subprocess
import time
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import url2pathname

import numpy as np
import win32api as api
import win32con as C
import win32gui as G

COLORS=[(48,94,132),(65,181,96),(215,35,192)]
CHANGE=C.WM_APP+1


def reference(bounds,ready):
    import sounddevice as sd
    ctypes.windll.user32.SetThreadDpiAwarenessContext(-4)
    state=0;frequency=600
    brushes=[G.CreateSolidBrush(api.RGB(*c)) for c in COLORS]
    def handler(hwnd,msg,wp,lp):
        nonlocal state,frequency
        if msg==CHANGE:
            if lp:frequency=lp
            state=wp;G.InvalidateRect(hwnd,None,False);G.UpdateWindow(hwnd);return 0
        if msg==C.WM_PAINT:
            dc,ps=G.BeginPaint(hwnd);G.FillRect(dc,G.GetClientRect(hwnd),brushes[state]);G.EndPaint(hwnd,ps);return 0
        return G.DefWindowProc(hwnd,msg,wp,lp)
    wc=G.WNDCLASS();wc.lpfnWndProc=handler;wc.lpszClassName="FrameNotePauseReference";wc.hInstance=api.GetModuleHandle(None)
    G.RegisterClass(wc)
    x,y,r,b=bounds
    hwnd=G.CreateWindowEx(C.WS_EX_TOOLWINDOW|C.WS_EX_NOACTIVATE,wc.lpszClassName,"FrameNote synthetic recording reference",
                          C.WS_POPUP,x,y,r-x,b-y,0,0,wc.hInstance,None)
    G.SetWindowPos(hwnd,C.HWND_TOPMOST,x,y,r-x,b-y,C.SWP_NOACTIVATE|C.SWP_SHOWWINDOW)
    def tone():
        offset=0
        def fill(buf,n,t,status):
            nonlocal offset
            buf[:]=(.06*np.sin(2*np.pi*frequency*(offset+np.arange(n))/48000) if state==1 else np.zeros(n))[:,None];offset+=n
        return fill
    with contextlib.ExitStack() as stack:
        dev=next(i for i,d in enumerate(sd.query_devices()) if d["name"].startswith("CABLE Input") and sd.query_hostapis(d["hostapi"])["name"]=="Windows WASAPI")
        stack.enter_context(sd.OutputStream(device=dev,samplerate=48000,channels=2,dtype="float32",callback=tone()))
        G.UpdateWindow(hwnd);ready.send(hwnd);G.PumpMessages()


def inspect(path,area,controls,hz):
    info=json.loads(subprocess.check_output(["ffprobe","-v","error","-show_streams","-of","json",path]))
    video=info["streams"][0];w,h=video["width"],video["height"]
    x,y,r,b=area;l,t,right,bottom=controls
    # Sample every encoded frame, including the first and last. Compare the whole
    # control footprint to the synthetic surface beside it; a black rectangle also fails.
    left=max(0,int((l-x)*w/(r-x))-3);top=max(0,int((t-y)*h/(b-y))-3)
    width=min(w-left,int((right-l)*w/(r-x))+7);height=min(h-top,int((bottom-t)*h/(b-y))+7)
    def pixels(crop,cw,ch):
        raw=subprocess.check_output(["ffmpeg","-v","error","-i",path,"-vf",crop,"-an","-f","rawvideo","-pix_fmt","rgb24","-"])
        return np.frombuffer(raw,np.uint8).reshape(-1,ch,cw,3).astype(np.int16)
    frames=pixels(f"crop={width}:{height}:{left}:{top}:exact=1",width,height)
    ref_x=left-8 if left>=8 else left+width
    reference=pixels(f"crop=2:2:{ref_x}:{top+height//2}:exact=1",2,2)[:,0,0,:]
    mismatch=np.abs(frames-reference[:,None,None,:]).max()
    assert mismatch<12,(path,"captured control pixels",mismatch)
    palette=np.array(COLORS);classes=np.abs(reference[:,None,:]-palette).max(axis=2)
    # The full-display encoder can tag a different YUV matrix after scaling.
    assert classes.min(axis=1).max()<26 and not np.any(classes.argmin(axis=1)==2),np.unique(reference,axis=0)[:10]
    duration=float(video["duration"])
    if hz:
        assert any(classes.argmin(axis=1)==1)
        fps=len(reference)/duration
        def rising(active):
            return np.flatnonzero(active & ~np.r_[False,active[:-1]])
        visual=rising(classes.argmin(axis=1)==1)/fps
        assert len(visual)==2,visual
        pcm=np.frombuffer(subprocess.check_output(["ffmpeg","-v","error","-i",path,"-vn","-ac","1","-ar","48000","-f","f32le","-"]),np.float32)
        assert abs(len(pcm)/48000-duration)<.06
        blocks=pcm[:len(pcm)//960*960].reshape(-1,960)
        strength=np.abs(np.fft.rfft(blocks*np.hanning(960)))[:,hz//50]
        baseline=np.median(strength[:max(1,int((visual[0]-.1)/.02))])
        signal=np.median(strength[int((visual[-1]+.25)/.02):-4])
        assert signal>max(.01,baseline*12),(hz,signal,baseline)
        onset=rising(strength>signal*.5)*.02
        assert len(onset)==2,(hz,onset,visual)
        lag=onset-visual
        print("Audio tone",hz,"Hz, before/after two pauses (s)",lag.round(3),flush=True)
        # The virtual device contributes its own fixed delay. Compare the same path before and after pausing.
        assert abs(lag[1]-lag[0])<.065,(hz,onset,visual)
    print("Decoded",len(reference),"frames",w,h,"duration",duration,"control pixel difference",mismatch,flush=True)
    return duration


def check_hover(panel,stop_button,pause_button,reference_hwnd):
    bounds=G.GetWindowRect(panel)
    fixed=stop_button.rectangle()
    center=fixed.mid_point()
    at_stop=(center.x,center.y)
    at_pause=(center.x,bounds[1]+fixed.height()//2)
    outside=(bounds[0]-25,center.y)
    hit=G.WindowFromPoint(at_stop)
    assert hit==panel,(bounds,fixed,at_stop,G.GetWindowText(hit),G.GetClassName(hit),hit,panel)
    assert G.WindowFromPoint(at_pause)==reference_hwnd
    assert G.WindowFromPoint((fixed.left,fixed.top))==reference_hwnd

    def sample_motion(point,seconds):
        api.SetCursorPos(point)
        until=time.monotonic()+seconds
        while time.monotonic()<until:
            assert G.GetWindowRect(panel)==bounds and stop_button.rectangle()==fixed
            time.sleep(.008)

    for seconds in (.06,.09,.13):
        sample_motion(at_stop,seconds)
        sample_motion(outside,.06)
    sample_motion(at_stop,.28)
    upper=pause_button.rectangle()
    assert G.WindowFromPoint(at_pause)==panel and upper.bottom<fixed.top
    gap=(center.x,(fixed.top+upper.bottom)//2)
    assert G.WindowFromPoint(gap)==reference_hwnd
    api.SetCursorPos(gap)
    time.sleep(.30)
    assert pause_button.rectangle()==upper and G.WindowFromPoint(at_pause)==panel
    api.SetCursorPos(at_pause)
    time.sleep(.05)
    assert G.WindowFromPoint(at_pause)==panel
    sample_motion(outside,.27)
    assert G.WindowFromPoint(at_pause)==reference_hwnd
    mouse_point=(bounds[0]+2,bounds[1]+fixed.height()//2)
    api.SetCursorPos(mouse_point)
    time.sleep(.22)
    assert G.WindowFromPoint(mouse_point)==reference_hwnd
    sample_motion(at_stop,.28)
    # Cross the drag threshold, return to the original point, then release there.
    dest=(center.x-105,center.y-70)
    for end in (at_stop,dest):
        api.SetCursorPos(at_stop)
        api.mouse_event(C.MOUSEEVENTF_LEFTDOWN,0,0)
        try:
            time.sleep(.04)
            api.SetCursorPos(dest);time.sleep(.08)
            assert G.GetWindowRect(panel)[:2]==(bounds[0]-105,bounds[1]-70)
            api.SetCursorPos(end);time.sleep(.08)
        finally:api.mouse_event(C.MOUSEEVENTF_LEFTUP,0,0)
        time.sleep(.09)
        assert G.IsWindowVisible(panel)
    moved=G.GetWindowRect(panel)
    return (moved[0],moved[1],bounds[2],bounds[3])


def run():
    from PIL import Image
    from pywinauto import Desktop,mouse,keyboard
    from playwright.sync_api import sync_playwright
    from review_ui import fixtures,launch,stop,wait,exported_file,work
    private_temp=work/"tmp";private_temp.mkdir()
    os.environ["TMP"]=os.environ["TEMP"]=str(private_temp)
    # Keep this run's global hotkey independent of any open editor session.
    profile=work/"profile";profile.mkdir()
    os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
    for vk in range(C.VK_F13,C.VK_F24+1):
        if ctypes.windll.user32.RegisterHotKey(None,1,7,vk):
            ctypes.windll.user32.UnregisterHotKey(None,1)
            break
    else:raise RuntimeError("No spare test hotkey available")
    (profile/"shortcuts.json").write_text(json.dumps({"record":[{"mods":7,"vk":vk}]}),encoding="utf-8")
    ctypes.windll.user32.SetThreadDpiAwarenessContext(-4)
    monitor=api.GetMonitorInfo(api.MonitorFromPoint((0,0)))
    bounds=monitor["Monitor"];x,y,r,b=bounds
    crop=(r-832,b-504,r-2,b-2)
    prev=api.GetCursorPos()
    with sync_playwright() as pw,contextlib.ExitStack() as stack:
        proc,p=launch(pw,fixtures/"vfr.mp4");stack.callback(stop,proc)
        main=Desktop(backend="win32").window(process=proc.pid,title_re=".*FrameNote.*",found_index=0).handle
        p.evaluate("transport.pause()")
        while p.locator("#outputStrips .removeChannel").count():p.locator("#outputStrips .removeChannel").first.click()
        ids=p.evaluate("""()=>[catalog.outputs.find(d=>d.name.startsWith('CABLE Input')).id,
                                catalog.inputs.find(d=>d.name.startsWith('CABLE Output')).id]""")
        p.locator("#mic").select_option(ids[1])
        p.locator("#cursor").uncheck()
        reader,writer=mp.Pipe(False)
        card=mp.Process(target=reference,args=(bounds,writer));card.start()
        stack.callback(lambda:(card.terminate(),card.join(),api.SetCursorPos(prev)))
        assert reader.poll(8);hwnd=reader.recv()
        for mode in ("region","display","gif","failure"):
            hz=1400 if mode=="display" else 600
            if mode=="display":
                p.locator("#mic").select_option("");p.locator("#addOutput").select_option(ids[0])
            p.locator("#mode").select_option("region" if mode!="display" else mode)
            p.locator("#source").select_option(monitor["Device"])
            if mode=="region":
                p.locator("#pickRegion").click()
                picker=Desktop(backend="win32").window(process=proc.pid,title="框选录制区域");picker.wait("visible");time.sleep(.25)
                mouse.press(coords=crop[:2]);mouse.move(coords=crop[2:]);time.sleep(.1);mouse.release(coords=crop[2:])
                wait(p,"region?.width===830 && region?.height===502")
            mouse.move(coords=(x+150,y+150))
            G.SendMessage(hwnd,CHANGE,0,hz)
            G.SetWindowPos(hwnd,C.HWND_TOPMOST,0,0,0,0,C.SWP_NOACTIVATE|C.SWP_NOSIZE|C.SWP_NOMOVE)
            cx,cy=(crop[0]+crop[2])//2,(crop[1]+crop[3])//2
            assert G.WindowFromPoint((cx,cy))==hwnd
            old=p.evaluate("doc.id")
            sessions=private_temp/"FrameNote"/"Recordings"
            previous_stages=set(sessions.glob("*/.framenote-*"))
            started=time.monotonic()
            p.locator("#recordGif" if mode=="gif" else "#record").click()
            wait(p,"!!recording")
            panel=Desktop(backend="win32").window(process=proc.pid,title="FrameNote 录制控制")
            panel.wait("visible");assert not G.IsWindowVisible(main)
            G.SetWindowPos(panel.handle,C.HWND_TOPMOST,0,0,0,0,C.SWP_NOACTIVATE|C.SWP_NOSIZE|C.SWP_NOMOVE)
            G.SetWindowPos(hwnd,panel.handle,0,0,0,0,C.SWP_NOACTIVATE|C.SWP_NOSIZE|C.SWP_NOMOVE)
            uia=Desktop(backend="uia").window(handle=panel.handle);s=uia.child_window(auto_id="StopButton")
            if mode=="failure":
                stage,=set(sessions.glob("*/.framenote-*"))-previous_stages
                blocked=stage/"complete.mp4";blocked.mkdir()
                try:
                    time.sleep(.6);s.click_input()
                    wait(p,"!recording && $('status').textContent.includes('录制失败')",timeout=15000)
                    assert G.IsWindowVisible(main) and not panel.exists(timeout=.1)
                finally:blocked.rmdir()
                print("A simulated mux failure restored the main window and released the floating controls",flush=True)
                continue
            if mode!="gif":
                time.sleep(.85);G.SendMessage(hwnd,CHANGE,1,0);time.sleep(.65)
                G.SendMessage(hwnd,CHANGE,0,0);time.sleep(.45)
            pause=uia.child_window(auto_id="PauseButton")
            if mode=="region":
                rect=check_hover(panel.handle,s.wrapper_object(),pause,hwnd)
            else:
                rect=G.GetWindowRect(panel.handle);pos=s.rectangle().mid_point()
                mouse.move(coords=(pos.x,pos.y));time.sleep(.25)
            pause.wait("ready");pause.click_input()
            uia.child_window(title="继续录制").wait("exists")
            G.SendMessage(hwnd,CHANGE,2,0);time.sleep(2.4)
            G.SendMessage(hwnd,CHANGE,0,0);time.sleep(.12)
            pause.click_input();uia.child_window(title="暂停录制").wait("exists");time.sleep(.45)
            if mode=="gif":
                # The 29 s limit starts from active time. A wall-clock timeout loses these paused seconds.
                wait(p,"!recording",timeout=45000)
                dest=exported_file(proc)
                with Image.open(dest) as gif:
                    elapsed=0
                    for i in range(gif.n_frames):
                        gif.seek(i);elapsed+=gif.info["duration"]
                    assert 28500<=elapsed<=30000,elapsed
                assert time.monotonic()-started>31
                print("GIF active duration",elapsed,"ms; paused real time excluded",flush=True)
                continue
            pause.click_input();uia.child_window(title="继续录制").wait("exists")
            G.SendMessage(hwnd,CHANGE,2,0);time.sleep(1.2)
            G.SendMessage(hwnd,CHANGE,0,0);time.sleep(.12)
            pause.click_input();uia.child_window(title="暂停录制").wait("exists");time.sleep(.4)
            G.SendMessage(hwnd,CHANGE,1,0);time.sleep(.9)
            if mode=="display":keyboard.send_keys("^%+{F"+str(vk-C.VK_F1+1)+"}")
            else:s.click_input()
            wait(p,"id=>temporary && ready && doc.id!==id",old,timeout=22000)
            assert G.IsWindowVisible(main)
            path=Path(url2pathname(urlsplit(p.evaluate("v.currentSrc")).path))
            shutil.copyfile(path,work/(mode+".mp4"))
            duration=inspect(path,bounds if mode=="display" else crop,rect,hz)
            assert 1.5<duration<8 and time.monotonic()-started-duration>3.4
            print(mode,"recording passed",flush=True)


if __name__=="__main__":
    run()
