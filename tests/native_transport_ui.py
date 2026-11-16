"""Check the native transport against a VFR clip with a machine-readable frame number."""
import io, os, subprocess, time
from pathlib import Path
from PIL import Image, ImageDraw
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
import win32clipboard
from review_ui import fixtures, work, launch, stop, wait

profile=work/"profile";profile.mkdir();os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
(profile/"updates.json").write_text('{"automatic":false}')
clip=work/"Frame index.mkv";w,h=320,180
raw=bytearray()
for i in range(150):
    im=Image.new("RGB",(w,h),(42,56,70));draw=ImageDraw.Draw(im)
    for bit in range(8):
        light=bool(i & (1<<bit))
        draw.rectangle((bit*40,80,bit*40+39,99),fill="white" if light else "black")
    raw.extend(im.tobytes())
subprocess.run(["ffmpeg","-loglevel","error","-f","rawvideo","-pixel_format","rgb24","-video_size","320x180","-framerate","25","-i","pipe:0","-vf","setpts=3*N+floor(N/2)","-fps_mode","vfr","-c:v","ffv1","-pix_fmt","yuv444p",str(clip)],input=raw,check=True)
def until(fn,timeout=9):
    end=time.monotonic()+timeout
    while time.monotonic()<end:
        if fn():return
        time.sleep(.07)
    raise AssertionError(fn)
def bits(im):
    im=im.convert("RGB");return sum((im.getpixel((i*40+20,90))[0]>128)<<i for i in range(8))
with sync_playwright() as pw:
    proc,page=launch(pw,clip,"document.readyState==='complete'")
    try:
        window=Desktop(backend="uia").window(process=proc.pid,title_re=".*FrameNote$",found_index=0)
        def el(name):return window.child_window(auto_id=name)
        def click(name):el(name).invoke()
        def clockms():
            m,s=el("Clock").window_text().split(" / ")[0].split(":")
            return int(m)*60000+round(float(s)*1000)
        def copy():
            seq=win32clipboard.GetClipboardSequenceNumber()
            click("CopyFrame")
            until(lambda: win32clipboard.GetClipboardSequenceNumber()!=seq and "已复制" in el("Hint").window_text())
            time.sleep(.15)
            win32clipboard.OpenClipboard()
            try:data=win32clipboard.GetClipboardData(win32clipboard.RegisterClipboardFormat("PNG"))
            finally:win32clipboard.CloseClipboard()
            im=Image.open(io.BytesIO(data));im.load();assert im.size==(w,h);return im
        el("ReviewButton").wait("visible",timeout=15)
        until(lambda:clockms()>800)
        click("Toggle");time.sleep(.3)
        assert el("Caption").window_text()==clip.name
        assert not el("Hint").window_text() and len(window.descendants(control_type="Slider"))==2
        pic=window.wrapper_object().capture_as_image();pic.save(work/"native-player.png")
        copy()  # The normal VLC surface also supports capture before any frame stepping.
        click("Next");time.sleep(.4);first=bits(copy())
        click("Next");time.sleep(.35);second=bits(copy())
        assert second==first+1,(first,second,clockms())
        click("Previous");time.sleep(.35);previous=copy()
        assert bits(previous)==first,(first,bits(previous),clockms())
        assert clockms()==(3*first+first//2)*40,(first,clockms())
        click("SaveFrame")
        d=Desktop(backend="win32").window(process=proc.pid,class_name="#32770");d.wait("ready",timeout=10)
        dest=work/"native-frame.png"
        d.child_window(class_name="Edit",found_index=0).set_edit_text(str(dest));d.child_window(control_id=1).click()
        until(dest.exists)
        with Image.open(dest) as saved:assert saved.convert("RGB").tobytes()==previous.convert("RGB").tobytes()
        el("VolumeSlider").set_value(37);click("Mute");until(lambda:el("VolumeSlider").iface_range_value.CurrentValue==0)
        click("Mute");until(lambda:el("VolumeSlider").iface_range_value.CurrentValue==37)
        rectangle=window.rectangle();click("Full");el("ReviewButton").wait_not("visible")
        click("Full");el("ReviewButton").wait("visible");assert rectangle==window.rectangle()
        t=clockms();click("Toggle");until(lambda:clockms()>t+300)
        click("ReviewButton");wait(page,"ready && !busy",timeout=25000)
        assert page.locator("#playbackVolume").input_value()=="37"
        print("PASS native VFR pixels, timecode, clipboard/PNG, volume, fullscreen toggle, review handoff.",work,flush=True)
    except Exception:
        if el("Hint").exists():print("native status:",el("Hint").window_text(),flush=True)
        raise
    finally:stop(proc)
