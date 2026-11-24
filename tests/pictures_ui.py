"""Check selected and pasted pictures in the real desk, including portable pixels and geometry."""
import io, os, shutil, time, zipfile
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import exported_file, work,fixtures,launch,stop,wait,at

os.environ["FRAMENOTE_DATA_DIR"]=str(work/"profile")
video=work/"image-source.mp4";shutil.copyfile(fixtures/"vfr.mp4",video)
png=work/"landscape.png";Image.new("RGB",(300,120),(212,37,94)).save(png)
package=work/"images.framenote"
def ink(page,rgb):
    loc=page.locator("#ink image").last
    picture=Image.open(io.BytesIO(loc.screenshot())).convert("RGB")
    pixel=picture.getpixel((picture.width//2,picture.height//2))
    assert max(abs(a-b) for a,b in zip(pixel,rgb))<4,(pixel,rgb)
    return loc.bounding_box()

def export(page,proc):
    page.locator("#save").click()
    exported_file(proc,package)
    wait(page,"document.querySelector('#status').textContent.startsWith('审阅包已保存')")

def set_clipboard_picture(page):
    page.context.grant_permissions(["clipboard-read","clipboard-write"])
    page.evaluate("""async()=>{const c=document.createElement('canvas');c.width=80;c.height=240;
      const ctx=c.getContext('2d');ctx.fillStyle='#27a959';ctx.fillRect(0,0,80,240);
      const blob=await new Promise(r=>c.toBlob(r));await navigator.clipboard.write([new ClipboardItem({'image/png':blob})])}""")

with sync_playwright() as pw:
    proc,page=launch(pw,video)
    try:
        at(page,4)
        action=page.locator("#annotationTools #addPicture")
        assert action.is_visible() and page.locator("#startPicture").count()==0
        with page.expect_file_chooser() as chooser:action.click()
        chooser.value.set_files([])
        assert page.evaluate("!editing")
        with page.expect_file_chooser() as chooser:page.keyboard.press("p")
        chooser.value.set_files(str(png));wait(page,"editing?.pictures?.length===1")
        page.locator("#body").focus();page.keyboard.press("p");assert page.locator("#body").input_value()=="p"
        page.locator("#body").fill("")
        before=ink(page,(212,37,94))
        handle=page.locator('[data-corner="1,1"]').bounding_box()
        x,y=handle["x"]+5,handle["y"]+5
        page.mouse.move(x,y);page.mouse.down();page.mouse.move(x+55,y-5,steps=6);page.mouse.up()
        after=ink(page,(212,37,94))
        assert after["width"]>before["width"]+15 and abs(after["width"]/after["height"]-2.5)<.01
        x,y=after["x"]+30,after["y"]+20
        page.mouse.move(x,y);page.mouse.down();page.mouse.move(x-35,y+12,steps=4);page.mouse.up()
        moved=ink(page,(212,37,94));assert moved["x"]<after["x"]-30
        page.keyboard.press("Control+Enter");wait(page,"doc.notes.length===1")
        at(page,5);assert page.locator("#ink image").count()==0
        at(page,8)
        # Put a generated PNG on the OS clipboard using the browser clipboard API.
        set_clipboard_picture(page)
        page.keyboard.press("Control+v");wait(page,"editing?.pictures?.length===1")
        assert page.evaluate("editing.startUs===frames[8] && editing.endUs===null")
        page.locator("#noteDuration").fill(str(page.evaluate("(frames[22]-frames[8])/1e6")))
        page.locator("#noteDuration").press("Tab")
        b=ink(page,(39,169,89));assert abs(b["width"]/b["height"]-1/3)<.01
        page.locator("#body").fill("参考竖图");page.keyboard.press("Control+Enter")
        at(page,12);ink(page,(39,169,89))
        at(page,23);assert page.locator("#ink image").count()==0
        expected=page.evaluate("doc.notes");export(page,proc)
    finally:stop(proc)
    png.unlink()
    proc,page=launch(pw,video)
    try:
        assert page.evaluate("doc.notes")==[]
        at(page,4);assert page.locator("#ink image").count()==0
    finally:stop(proc)
    os.environ["FRAMENOTE_DATA_DIR"]=str(work/"clean-profile");video.unlink()
    with zipfile.ZipFile(package) as z:assert len([e for e in z.namelist() if e.startswith("images/")])==2
    proc,page=launch(pw,package)
    try:
        assert page.evaluate("doc.notes")==expected
        at(page,4);ink(page,(212,37,94))
        at(page,12);ink(page,(39,169,89))
        # Paste directly on the desk without opening an editor.
        set_clipboard_picture(page);page.locator("#stage").focus()
        at(page,27);page.keyboard.press("Control+v");wait(page,"editing?.pictures?.length===1")
        assert page.evaluate("editing.startUs===frames[27] && editing.endUs===null")
        page.keyboard.press("Control+z");assert page.evaluate("doc.notes.length")==2
    finally:stop(proc)
    print("PASS: file and clipboard, image-only point, range, placement, locked aspect, clean reopen and standalone package",flush=True)
