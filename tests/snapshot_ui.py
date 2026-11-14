"""Copy one annotated frame, paste as an image and compare with a saved PNG."""
import io, time, os, zipfile, json, hashlib, subprocess
from PIL import Image, ImageChops, ImageStat
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import fixtures, work, launch, stop, wait, at
import win32clipboard as cb
import pywintypes

os.environ["FRAMENOTE_DATA_DIR"]=str(work/"profile")
raw=io.BytesIO();Image.new("RGB",(100,60),(224,49,137)).save(raw,"PNG")
data=raw.getvalue();asset=hashlib.sha256(data).hexdigest()
hd=work/"hd.mp4"
subprocess.run(["ffmpeg","-hide_banner","-loglevel","error","-i",str(fixtures/"vfr.mp4"),"-vf","scale=1920:1080","-an","-preset","ultrafast",str(hd)],check=True)
package=work/"帧捕获.framenote"
with zipfile.ZipFile("examples/SampleReview.framenote") as src, zipfile.ZipFile(package,"w") as archive:
    review=json.loads(src.read("review.json"));n=review["notes"][0]
    n["pictures"]=[dict(asset=asset,pixelWidth=100,pixelHeight=60,x=.4,y=.4,width=.3)]
    archive.writestr("images/"+asset+".png",data);archive.writestr("review.json",json.dumps(review))
    media=next(s for s in src.namelist() if s.startswith("media/"));archive.write(hd,media)

with sync_playwright() as pw:
    proc,page=launch(pw,package)
    try:
        page.locator(".note .stamp").first.click()
        wait(page,"Math.abs(shown-doc.notes[0].startUs)<=2")
        page.locator("#ink image").screenshot()
        page.locator("#copyFrame").click()
        wait(page,"document.querySelector('#status').textContent.startsWith('当前帧已复制')")
        for attempt in range(30):
            try:cb.OpenClipboard();break
            except pywintypes.error:
                if attempt==29:raise
                time.sleep(.05)
        try:
            png=cb.GetClipboardData(cb.RegisterClipboardFormat("PNG"))
            assert cb.IsClipboardFormatAvailable(cb.CF_DIB)
        finally:cb.CloseClipboard()
        frame=Image.open(io.BytesIO(png)).convert("RGB")
        dims=tuple(page.evaluate("[v.videoWidth,v.videoHeight]"))
        assert frame.size==dims,(frame.size,dims)
        assert frame.getpixel((int(dims[0]*.54),int(dims[1]*.48)))==(224,49,137)
        # Independently decode the same VFR frame. Compare a clear strip away from notes.
        stamp=page.evaluate("shown/1e6")
        reference=subprocess.check_output(["ffmpeg","-v","error","-ss",str(stamp),"-i",str(hd),
            "-frames:v","1","-pix_fmt","rgb24","-f","rawvideo","-"])
        strip=(0,int(dims[1]*.83),dims[0],dims[1])
        def sample(im):return im.crop(strip).point(lambda n:255 if n>128 else 0).resize((100,12),Image.Resampling.BOX)
        delta=ImageChops.difference(sample(frame),sample(Image.frombytes("RGB",dims,reference)))
        if max(ImageStat.Stat(delta).mean)>=4:
            print("Capture comparison:",work,stamp,flush=True)
            frame.resize((480,270)).save(work/"capture-check.png")
            Image.frombytes("RGB",dims,reference).resize((480,270)).save(work/"decode-check.png")
        assert max(ImageStat.Stat(delta).mean)<4,ImageStat.Stat(delta).mean
        page.locator("#saveFrame").click()
        dest=work/"发送给同事.png"
        dlg=Desktop(backend="win32").window(process=proc.pid,class_name="#32770");dlg.wait("ready",timeout=10)
        edit=dlg.child_window(class_name="Edit",found_index=0);edit.wait("ready");time.sleep(.35);edit.set_edit_text(str(dest))
        dlg.child_window(control_id=1).click()
        wait(page,"document.querySelector('#status').textContent.startsWith('当前帧已保存')")
        assert ImageChops.difference(frame,Image.open(dest).convert("RGB")).getbbox() is None
        # A composer receives an actual image MIME item through Ctrl+V.
        chat=page.context.new_page();chat.set_content('<textarea autofocus></textarea>')
        chat.evaluate("""document.addEventListener('paste',e=>{window.pasted=e.clipboardData.files[0]?.type})""")
        chat.locator("textarea").focus();chat.keyboard.press("Control+v");assert chat.evaluate("window.pasted")=="image/png";chat.close()
        print("PASS: annotated frame",frame.size,"Windows PNG+DIB, composer paste and saved pixels")
    finally:stop(proc)
