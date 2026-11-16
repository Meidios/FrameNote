"""Verify bundled VLC playback and the transfer from original media to frame review."""
import os, subprocess, time, zipfile, json, hashlib
from PIL import ImageStat, Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import fixtures, work, launch, stop, wait, at

os.environ["FRAMENOTE_DATA_DIR"]=str(work/"profile")
mkv=work/"变帧率 PCM.mkv"
subprocess.run(["ffmpeg","-v","error","-i",str(fixtures/"vfr.mp4"),"-c:v","ffv1","-fps_mode","passthrough","-c:a","pcm_s16le",str(mkv)],check=True)
def file_dialog(proc,path):
    d=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
    d.wait("ready",timeout=10)
    d.child_window(class_name="Edit",found_index=0).set_edit_text(str(path))
    d.child_window(control_id=1).click()
def until(fn,timeout=12):
    end=time.monotonic()+timeout
    while time.monotonic()<end:
        if fn():return
        time.sleep(.12)
    raise AssertionError(fn)
with sync_playwright() as pw:
    proc,page=launch(pw,fixtures/"vfr.mp4")
    try:
        window=Desktop(backend="uia").window(process=proc.pid,title_re=".*FrameNote$",control_type="Window",found_index=0)
        def btn(name):return window.child_window(auto_id=name,control_type="Button")
        at(page,7)
        page.locator("#vlc").click()
        btn("ReviewButton").wait("visible",timeout=12)
        until(lambda:" / " in window.child_window(auto_id="Clock").window_text())
        btn("ReviewButton").invoke()
        btn("ReviewButton").wait_not("visible",timeout=12)
        wait(page,"!busy && ready && !reviewProxy && !v.seeking")
        at(page,3)
        print("MP4: native playback -> direct review",flush=True)
        page.locator("#open").click(no_wait_after=True);file_dialog(proc,mkv)
        btn("ReviewButton").wait("visible",timeout=12)
        until(lambda:"变帧率" in window.child_window(auto_id="Caption").window_text())
        until(lambda:window.child_window(auto_id="Clock").window_text().split(" / ")[0] not in ("","00:00.000"))
        picture=window.wrapper_object().capture_as_image()
        w,h=picture.size;picture=picture.crop((w//4,h//4,w*3//4,h*3//4))
        assert max(ImageStat.Stat(picture).stddev)>20,"VLC output remained blank"
        print("MKV: native clock and visible picture verified",flush=True)
        btn("ReviewButton").invoke()
        btn("CancelButton").invoke()
        until(lambda:"已取消" in window.child_window(auto_id="Hint").window_text())
        btn("Toggle").invoke()
        print("Preview cancellation returned to playback",flush=True)
        btn("ReviewButton").invoke()
        wait(page,"!busy && ready && reviewProxy",timeout=20000)
        for i in [0,7,page.evaluate("frames.length")-1]:at(page,i)
        at(page,7);page.keyboard.press("m");page.locator("#body").fill("PCM 同步点");page.keyboard.press("Control+Enter")
        png=work/"review.png";page.locator("#saveFrame").click(no_wait_after=True);file_dialog(proc,png)
        until(png.exists);wait(page,"!busy")
        with Image.open(png) as image:assert image.width==page.evaluate("v.videoWidth")
        package=work/"兼容性.framenote";page.locator("#save").click(no_wait_after=True);file_dialog(proc,package)
        until(package.exists);wait(page,"!busy")
        with zipfile.ZipFile(package) as z:
            assert hashlib.sha256(z.read("media/source.mkv")).digest()==hashlib.sha256(mkv.read_bytes()).digest()
            assert json.loads(z.read("review.json"))["notes"][0]["startUs"]==page.evaluate("frames[7]")
        old_id=page.evaluate("playbackId");page.locator("#open").click(no_wait_after=True);file_dialog(proc,package)
        wait(page,"id=>id!==playbackId && !busy && ready && reviewProxy && doc.notes.length===1",old_id)
        at(page,7);assert page.locator("#ink .inkLabels").text_content().strip()
        print("MKV: original VFR frame times, annotated snapshot and byte-identical package passed",flush=True)
    except Exception:
        print("STATUS:",page.locator("#status").inner_text(),flush=True)
        print([(w.window_text(),w.class_name()) for w in Desktop(backend="uia").windows(process=proc.pid)],flush=True)
        raise
    finally:stop(proc)



