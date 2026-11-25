"""Exercise both recording buttons using a synthetic window; never capture the desktop."""
import os, hashlib, json, subprocess, tempfile, time, sys, zipfile
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import url2pathname
from PIL import Image
from pywinauto import Desktop
from pywinauto import mouse
import win32gui
from playwright.sync_api import sync_playwright
from review_ui import exported_file, settings, app,fixtures,work,launch,stop,wait

def floating(proc):
    w=Desktop(backend="win32").window(process=proc.pid,title="FrameNote 录制控制")
    w.wait("visible",timeout=8)
    return Desktop(backend="uia").window(handle=w.handle)

def hover_pause(panel):
    stop_button=panel.child_window(auto_id="StopButton")
    pos=stop_button.rectangle().mid_point()
    mouse.move(coords=(pos.x,pos.y))
    pause=panel.child_window(auto_id="PauseButton")
    pause.wait("ready",timeout=5)
    time.sleep(.2)
    assert pause.rectangle().bottom<=stop_button.rectangle().top
    return pause
profile=work/"profile"
profile.mkdir()
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
copies=work/"副本"
target=subprocess.Popen([sys.executable,"-c",'''import tkinter as tk
w=tk.Tk();w.title("FrameNote QC target");w.geometry("480x270")
c=tk.Canvas(w,bg="#293c51");c.pack(fill="both",expand=True);i=c.create_text(160,100,text="",font=("Consolas",36),fill="white")
def tick(n):
 c.itemconfigure(i,text=str(n));w.after(40,tick,n+1)
tick(0);w.mainloop()'''])
try:
    with sync_playwright() as pw:
        proc,page=launch(pw,fixtures/"vfr.mp4")
        try:
            clips=[]
            package=work/"录屏批注.framenote"
            main=Desktop(backend="win32").window(process=proc.pid,title_re=".*FrameNote.*",found_index=0).handle
            for attempt,gif in enumerate([False,False,True]):
                if attempt==1:
                    dlg=settings(proc,"录制设置")
                    hide=dlg.child_window(auto_id="HideWhileRecording",control_type="CheckBox")
                    assert hide.get_toggle_state()
                    hide.toggle()
                    copy=dlg.child_window(auto_id="SaveCopy",control_type="CheckBox")
                    folder=dlg.child_window(auto_id="CopyDirectory",control_type="Edit")
                    assert not copy.get_toggle_state() and not folder.is_enabled()
                    copy.toggle()
                    folder.set_edit_text(str(copies))
                    dlg.child_window(title="保存",control_type="Button").invoke()
                if gif:
                    dlg=settings(proc,"录制设置")
                    hide=dlg.child_window(auto_id="HideWhileRecording",control_type="CheckBox")
                    assert not hide.get_toggle_state()
                    hide.toggle()
                    dlg.child_window(title="保存",control_type="Button").invoke()
                page.locator("#mode").select_option("window")
                page.locator("#refreshSources").click()
                page.locator("#source").select_option(label="FrameNote QC target")
                while page.locator("#outputStrips .removeChannel").count():
                    page.locator("#outputStrips .removeChannel").first.click()
                controls=page.get_by_role("banner").get_by_role("group",name="录制控制")
                button=controls.locator("#recordGif" if gif else "#record")
                assert controls.get_by_role("button").count()==2
                if attempt==0:
                    button.press("Space")
                else:
                    button.click()
                wait(page,"recording!==null")
                assert button.is_enabled() and button.get_attribute("aria-description").startswith("停止录制")
                assert page.locator("#record" if gif else "#recordGif").is_disabled()
                assert all(page.locator(s).is_disabled() for s in ("#mode","#cursor","#mic"))
                assert not Desktop(backend="win32").window(process=proc.pid,class_name="#32770").exists(timeout=.1)
                old_id=page.evaluate("doc.id")
                time.sleep(1.2)
                if attempt==1:
                    assert win32gui.IsWindowVisible(main)
                    button.click()
                else:
                    assert not win32gui.IsWindowVisible(main)
                    panel=floating(proc)
                    pause=hover_pause(panel)
                    pause.click_input()
                    panel.child_window(title="继续录制").wait("exists",timeout=4)
                    time.sleep(1.3)
                    if not gif:
                        pause.click_input()
                        panel.child_window(title="暂停录制").wait("exists",timeout=4)
                        time.sleep(.7)
                    page.evaluate("""()=>{
                        window.stopStates=[];
                        chrome.webview.addEventListener('message',({data:m})=>{
                            if(['captureStopping','recordingRestored'].includes(m.type))stopStates.push({
                                event:m.type,active:!!document.querySelector('#captureCommands .active'),
                                disabled:$('record').disabled,loading:!$('mediaLoading').hidden
                            });
                        },{signal:(window.stopObserver=new AbortController()).signal});
                    }""")
                    panel.child_window(auto_id="StopButton").click_input()
                if attempt==1:
                    page.keyboard.press("Space")
                if gif:
                    dest=exported_file(proc)
                    assert dest.parent==work/"exports"
                    assert (copies/dest.name).read_bytes()==dest.read_bytes()
                    wait(page,"document.querySelector('#status').textContent.startsWith('GIF 已保存')",timeout=15000)
                    with Image.open(dest) as im:assert im.info["loop"]==0 and im.n_frames>10
                else:
                    wait(page,"old=>temporary && ready && doc.id!==old",old_id,timeout=15000)
                    assert win32gui.IsWindowVisible(main)
                    if attempt==0:
                        states=page.evaluate("stopObserver.abort();stopStates")
                        assert [s['event'] for s in states]==['captureStopping','recordingRestored'],states
                        assert all(not s['active'] and s['disabled'] and s['loading'] for s in states),states
                        assert page.locator("#mediaLoading").is_hidden()
                        assert page.evaluate("v.paused && v.currentTime===0"),"New recording should wait at the start"
                        page.keyboard.press("Space")
                    wait(page,"!v.paused && v.currentTime>0 && recording===null && !starting")
                    page.keyboard.press("Space")
                    wait(page,"v.paused && recording===null && !starting")
                    clip=Path(url2pathname(urlsplit(page.evaluate("v.currentSrc")).path))
                    assert clip.is_relative_to(Path(tempfile.gettempdir())/"FrameNote"/"Recordings") and clip.stat().st_size>1000
                    if attempt==0:assert not copies.exists()
                    if clips:assert not clips[0].exists(),"Previous temporary recording remains"
                    clips.append(clip)
                    page.keyboard.press("m");page.locator("#body").fill("命中偏晚");page.keyboard.press("Control+Enter")
                    wait(page,"doc.notes.length===1")
                    if attempt==1:
                        assert next(copies.glob("*.mp4")).read_bytes()==clip.read_bytes()
                        page.locator("#save").click()
                        package=exported_file(proc)
                        with zipfile.ZipFile(package) as z:
                            assert hashlib.sha256(z.read("media/source.mp4")).digest()==hashlib.sha256(clip.read_bytes()).digest()
                            assert json.loads(z.read("review.json"))["notes"][0]["text"]=="命中偏晚"
                print("Desktop capture workflow OK:",("GIF" if gif else "temporary review"),flush=True)
            page.locator("#source").select_option(page.evaluate("catalog.windows.find(w=>w.self).id"))
            page.locator("#record").click()
            wait(page,"recording==='video'")
            assert win32gui.IsWindowVisible(main)
            assert not Desktop(backend="win32").window(process=proc.pid,title="FrameNote 录制控制").exists(timeout=.2)
            old=page.evaluate("doc.id")
            time.sleep(1)
            page.locator("#record").click()
            wait(page,"id=>temporary && ready && doc.id!==id",old,timeout=15000)
            assert page.evaluate("v.videoWidth>400 && v.videoHeight>250")
            print("Self-window capture remained visible and produced reviewable video",flush=True)
            page.locator("#source").select_option(label="FrameNote QC target")
            from pywinauto.keyboard import send_keys
            for early in (False,True):
                old=page.evaluate("doc.id")
                page.locator("#record").click();wait(page,"recording==='video'")
                panel=floating(proc);time.sleep(1.2)
                panel.child_window(auto_id="StopButton").click_input()
                if not early:
                    Desktop(backend="win32").window(handle=main).wait("visible",timeout=6,retry_interval=.01)
                send_keys("{SPACE}")
                wait(page,"id=>temporary && ready && doc.id!==id && !v.paused && v.currentTime>0 && !recording",old,timeout=7000)
                print("Space immediately after Stop" if early else "Space on window restoration","played the new recording",flush=True)
        finally:stop(proc)
        assert all(not clip.exists() for clip in clips),"Temporary clip remained after close"
        proc,page=launch(pw,package)
        try:assert page.evaluate("!temporary && doc.notes[0].text==='命中偏晚'")
        finally:stop(proc)
        print("Recording copies, temporary cleanup, review export and reopen passed",flush=True)
finally:target.terminate();target.wait()

