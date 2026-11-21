"""Exercise both recording buttons using a synthetic window; never capture the desktop."""
import hashlib, json, subprocess, tempfile, time, sys, zipfile
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import url2pathname
from PIL import Image
from pywinauto import Desktop
from playwright.sync_api import sync_playwright
from review_ui import app,fixtures,work,launch,stop,wait
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
            def dialog():
                dlg=Desktop(backend="win32").window(process=proc.pid,class_name="#32770");dlg.wait("ready",timeout=10)
                return dlg
            def save_to(dest):
                from pywinauto.keyboard import send_keys
                dlg=dialog();dlg.set_focus()
                edit=dlg.child_window(class_name="Edit",control_id=1001);edit.wait("ready")
                edit.click_input();send_keys("^a");edit.set_edit_text(str(dest.resolve()));send_keys("{ENTER}")
                dlg.wait_not("visible",timeout=8)
            for attempt,gif in enumerate([False,False,True]):
                page.locator("#mode").select_option("window")
                page.locator("#refreshSources").click()
                page.locator("#source").select_option(label="FrameNote QC target")
                while page.locator("#outputStrips .removeChannel").count():
                    page.locator("#outputStrips .removeChannel").first.click()
                button=page.locator("#recordGif" if gif else "#record")
                button.click()
                wait(page,"recording!==null")
                assert button.is_enabled() and button.get_attribute("aria-description").startswith("停止录制")
                assert page.locator("#record" if gif else "#recordGif").is_disabled()
                assert all(page.locator(s).is_disabled() for s in ("#mode","#fps","#cursor","#mic"))
                assert not Desktop(backend="win32").window(process=proc.pid,class_name="#32770").exists(timeout=.1)
                dest=work/"record.gif"
                old_id=page.evaluate("doc.id")
                time.sleep(2.8);button.click()
                if gif:
                    save_to(dest)
                    wait(page,"document.querySelector('#status').textContent.startsWith('GIF 已保存')",timeout=15000)
                    with Image.open(dest) as im:assert im.info["loop"]==0 and im.n_frames>10
                else:
                    wait(page,"old=>temporary && ready && doc.id!==old",old_id,timeout=15000)
                    clip=Path(url2pathname(urlsplit(page.evaluate("v.currentSrc")).path))
                    assert clip.is_relative_to(Path(tempfile.gettempdir())/"FrameNote"/"Recordings") and clip.stat().st_size>1000
                    if clips:assert not clips[0].exists(),"Previous temporary recording remains"
                    clips.append(clip)
                    page.keyboard.press("m");page.locator("#body").fill("命中偏晚");page.keyboard.press("Control+Enter")
                    wait(page,"doc.notes.length===1")
                    if attempt==1:
                        page.locator("#save").click();dialog().child_window(control_id=2).click_input()
                        assert clip.exists() and page.evaluate("doc.notes[0].text")== "命中偏晚"
                        page.locator("#save").click();save_to(package)
                        wait(page,"document.querySelector('#status').textContent.startsWith('审阅包已保存')")
                        with zipfile.ZipFile(package) as z:
                            assert hashlib.sha256(z.read("media/source.mp4")).digest()==hashlib.sha256(clip.read_bytes()).digest()
                            assert json.loads(z.read("review.json"))["notes"][0]["text"]=="命中偏晚"
                print("Desktop capture workflow OK:",("GIF" if gif else "temporary review"),flush=True)
        finally:stop(proc)
        assert all(not clip.exists() for clip in clips),"Temporary clip remained after close"
        proc,page=launch(pw,package)
        try:assert page.evaluate("!temporary && doc.notes[0].text==='命中偏晚'")
        finally:stop(proc)
        print("Discard, cancel export, portable export and reopen passed",flush=True)
finally:target.terminate();target.wait()

