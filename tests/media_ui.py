"""Exercise playback, shared volume, independent images and both paste targets in the real app."""
import io, os, time, json
from pathlib import Path
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
import win32clipboard as cb
from review_ui import launch, stop, wait, work, fixtures
profile=work/"profile";profile.mkdir()
(profile/"updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
colors=[Image.new("RGB",(64,48),c) for c in ("red","blue")]
for ext,fmt in [("gif","GIF"),("apng","PNG"),("webp","WEBP")]:
    colors[0].save(work/("animated."+ext),format=fmt,save_all=True,append_images=colors[1:],duration=200,loop=0)
for ext in ("jpeg","png","bmp","tiff","ico","avif"):
    colors[0].save(work/("still."+ext))
def open_file(page,proc,path):
    page.keyboard.press("Control+o")
    dialog=Desktop(backend="win32").window(process=proc.pid,class_name="#32770");dialog.wait("ready",timeout=8)
    dialog.child_window(class_name="Edit",found_index=0).set_edit_text(str(path.resolve()));dialog.child_window(control_id=1).click()
def clipboard_png():
    raw=io.BytesIO();colors[0].save(raw,format="BMP")
    for _ in range(30):
        try:cb.OpenClipboard();break
        except Exception:time.sleep(.06)
    try:cb.EmptyClipboard();cb.SetClipboardData(cb.CF_DIB,raw.getvalue()[14:])
    finally:cb.CloseClipboard()
with sync_playwright() as pw:
    proc,page=launch(pw,None,"typeof ready!=='undefined'")
    try:
        clipboard_png();page.locator("#welcome").click();page.keyboard.press("Control+v")
        wait(page,"$('openedImage').naturalWidth===64 && imageDoc() && !doc.notes.length && !editing && $('status').textContent==='剪贴板图片'")
        pasted=page.locator("#openedImage").get_attribute("src")
        page.keyboard.press("ArrowRight");wait(page,"$('status').textContent.includes('没有所在文件夹')")
        assert page.locator("#openedImage").get_attribute("src")==pasted
        dpr=page.evaluate("devicePixelRatio");page.keyboard.down("Control");page.mouse.wheel(0,-480);page.keyboard.up("Control")
        page.wait_for_timeout(180);assert page.evaluate("devicePixelRatio")==dpr
        for file in sorted([*work.glob("animated.*"),*work.glob("still.*")]):
            open_file(page,proc,file)
            wait(page,"n=>$('status').textContent===n && $('openedImage').complete && $('openedImage').naturalWidth>0 && imageDoc() && !doc.notes.length",file.name)
            if file.stem=="animated":
                pixels=set()
                r=page.locator("#openedImage").bounding_box()
                for _ in range(10):
                    shot=page.screenshot(clip=dict(x=int(r["x"]+r["width"]/2),y=int(r["y"]+r["height"]/2),width=2,height=2))
                    pixels.add(Image.open(io.BytesIO(shot)).getpixel((0,0))[:3]);page.wait_for_timeout(45)
                assert len(pixels)>1,(file,pixels)
        print("PASS empty paste, disabled zoom, static formats and changing GIF/APNG/WebP pixels",flush=True)
        open_file(page,proc,fixtures/"capture-60.mp4")
        wait(page,"ready && !v.paused && shown>0 && !v.muted && v.volume===1")
        page.locator("#seek").evaluate("e=>{e.value=doc.durationUs/2;e.dispatchEvent(new Event('input'))}")
        wait(page,"!v.paused && shown>=doc.durationUs*.48")
        page.locator("#play").click();page.locator("#seek").evaluate("e=>{e.value=doc.durationUs/4;e.dispatchEvent(new Event('input'))}")
        wait(page,"v.paused && Math.abs(shown-doc.durationUs/4)<100000")
        main=Desktop(backend="uia").window(process=proc.pid,title_re=".* · FrameNote",found_index=0)
        assert not main.child_window(title="播放音量",control_type="Slider",framework_id="WPF").exists(timeout=.4)
        page.locator("#transport #playbackVolume").evaluate("e=>{e.value=37;e.dispatchEvent(new Event('input'))}")
        wait(page,"v.volume===0.37")
        page.locator("#mutePlayback").click();wait(page,"v.volume===0")
        page.locator("#mutePlayback").click()
        wait(page,"v.volume===0.37")
        clipboard_png();page.locator("#stage").click();page.keyboard.press("Control+v")
        wait(page,"editing?.pictures?.length===1 && doc && ready")
        page.keyboard.press("Control+z")
        print("PASS autoplay, seek in both states, volume and video paste as annotation",flush=True)
    finally:stop(proc)
    proc,page=launch(pw,Path("examples/SampleReview.framenote").resolve())
    try:
        wait(page,"v.volume===0.37")
        for kind in ("point","region"):
            page.evaluate("v.play()");page.locator("#marks button."+kind).first.click()
            wait(page,"v.paused && selected && stamp()===doc.notes.find(n=>n.id===selected).startUs")
        print("PASS persisted volume and click-to-pause on marker/region",flush=True)
    finally:stop(proc)
