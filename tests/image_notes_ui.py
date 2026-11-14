"""Exercise image annotation geometry, persistence, clipboard packages and the native volume switch."""
import io,json,os,time,zipfile
from pathlib import Path
import win32clipboard as cb
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import launch,stop,wait,work,fixtures

profile=work/"profile";profile.mkdir();(profile/"updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
base=work/"images";base.mkdir();source=base/"1 原图.tif";other=base/"2.png"
im=Image.new("RGB",(800,400),"#447766");im.save(source);im.save(other)
def dialog(proc,path):
    d=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
    d.wait("ready",timeout=10);d.child_window(class_name="Edit",found_index=0).set_edit_text(str(path));d.child_window(control_id=1).click()
def save(page,proc,path):
    page.locator("#save").click(no_wait_after=True);dialog(proc,path)
    wait(page,"!busy && $('status').textContent.includes('已保存：')")
def vol(proc):
    w=Desktop(backend="uia").window(process=proc.pid,title_re=".*FrameNote$",found_index=0)
    return w.child_window(title="播放音量",control_type="Slider").exists(timeout=.4)
def mark(p,kind,msg):
    p.locator("#point").click();p.locator("#body").fill(msg);p.locator("#"+kind).click()
    r=p.locator("#overlay").bounding_box();x,y,w,h=[r[k] for k in ('x','y','width','height')]
    p.mouse.move(x+w*.45,y+h*.42);p.mouse.down();p.mouse.move(x+w*.61,y+h*.62,steps=5);p.mouse.up()
    p.keyboard.press("Control+Enter");wait(p,"!editing && doc.notes.length>0")
    shape=p.evaluate("doc.notes.at(-1).shape")
    assert shape["kind"]==kind and abs(shape["x"]-.45)<.005 and abs(shape["toY"]-.62)<.005,shape
def geometry(p):
    return p.evaluate("""()=>{const img=$('openedImage'),r=img.getBoundingClientRect(),k=Math.min(r.width/img.naturalWidth,r.height/img.naturalHeight),w=img.naturalWidth*k,h=img.naturalHeight*k,b=document.querySelector('#ink rect[stroke-width="3"]'),s=b.getBoundingClientRect();return [(s.x-r.x-(r.width-w)/2)/w,(s.y-r.y-(r.height-h)/2)/h]}""")
with sync_playwright() as pw:
    proc,p=launch(pw,source,"typeof imageDoc==='function' && imageDoc() && $('openedImage').naturalWidth===800")
    try:
        assert not vol(proc)
        r=p.locator("#stage").bounding_box();p.mouse.move(r["x"]+r["width"]*.49,r["y"]+r["height"]*.5);p.mouse.wheel(0,-480)
        wait(p,"$('stage').classList.contains('pannable')")
        mark(p,"box","检查边缘");wait(p,"$('status').textContent==='草稿已保存'")
        r=p.locator("#stage").bounding_box();p.mouse.move(r["x"]+55,r["y"]+62);p.mouse.down();p.mouse.move(r["x"]+92,r["y"]+75);p.mouse.up()
        assert all(abs(a-b)<.006 for a,b in zip(geometry(p),(.45,.42)))
        p.keyboard.press("ArrowRight");wait(p,"doc.fileName==='2.png' && !busy && doc.notes.length===0")
        p.keyboard.press("ArrowLeft");wait(p,"doc.notes[0]?.text==='检查边缘' && !busy")
        pack=work/"file.framenote";save(p,proc,pack)
        with zipfile.ZipFile(pack) as z:assert z.read("media/source.tif")==source.read_bytes()
        p.locator("#open").click(no_wait_after=True);dialog(proc,fixtures/"capture-60.mp4");wait(p,"ready && !imageDoc()");assert vol(proc)
        print("PASS TIFF draft, drawing after zoom, pan registration, original TIFF package and native volume",flush=True)
    finally:stop(proc)
    proc,p=launch(pw,None,"typeof imageDoc==='function'")
    try:
        raw=io.BytesIO();im.save(raw,"BMP");cb.OpenClipboard()
        try:cb.EmptyClipboard();cb.SetClipboardData(cb.CF_DIB,raw.getvalue()[14:])
        finally:cb.CloseClipboard()
        p.locator("#welcome").click();p.keyboard.press("Control+v")
        wait(p,"imageDoc() && $('openedImage').naturalWidth===800 && !busy")
        mark(p,"arrow","");pack=work/"clipboard.framenote";save(p,proc,pack)
    finally:stop(proc)
    proc,p=launch(pw,pack,"typeof imageDoc==='function' && imageDoc() && $('openedImage').naturalWidth===800 && !busy")
    try:
        wait(p,"doc.notes[0]?.shape?.kind==='arrow'");assert not vol(proc)
        p.locator(".inkBadge").click();p.locator("#edit").click();p.locator("#body").fill("调整箭头");p.keyboard.press("Control+Enter")
        wait(p,"doc.notes[0].text==='调整箭头' && !editing")
        print("PASS clipboard arrow-only note -> exported package -> restart -> selection and edit",flush=True)
    finally:stop(proc)

