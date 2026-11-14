"""Compare clipboard and saved image exports against known whole-image geometry."""
import io, os, time, uuid, zipfile, json, hashlib
from PIL import Image, ImageDraw, ImageChops
import win32clipboard as cb
from pywinauto import Desktop
from playwright.sync_api import sync_playwright
from review_ui import work, launch, stop, wait
profile=work/"profile";profile.mkdir();(profile/"updates.json").write_text('{"automatic":false}');os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
W,H=2048,1024
im=Image.new("RGBA",(W,H),(90,100,110,255))
draw=ImageDraw.Draw(im)
for (x,y),c in [((0,0),"red"),((W-8,0),"green"),((0,H-8),"blue"),((W-8,H-8),"yellow")]:draw.rectangle((x,y,x+7,y+7),fill=c)
draw.rectangle((600,0,850,130),fill=(0,0,0,0))
# Exceed a transfer chunk to exercise stitching.
im.paste(Image.effect_noise((W,530),64).convert("RGB"),(0,470))
def png(image):
    out=io.BytesIO();image.save(out,format="PNG");return out.getvalue()
tile=png(Image.new("RGB",(64,32),"magenta"));asset=hashlib.sha256(tile).hexdigest()
def uid():return str(uuid.uuid4())
doc=dict(version=2,id=uid(),fileName="full.png",durationUs=0,lastNoteNumber=2,notes=[
 dict(id=uid(),startUs=0,endUs=None,number=1,text="box",color="#00ff00",shape=dict(kind="box",x=.10,y=.25,toX=.20,toY=.40)),
 dict(id=uid(),startUs=0,endUs=None,number=2,text="arrow",color="#00ffff",shape=dict(kind="arrow",x=.76,y=.40,toX=.89,toY=.40),
 pictures=[dict(asset=asset,pixelWidth=64,pixelHeight=32,x=.50,y=.58,width=.10)])])
package=work/"source.framenote"
with zipfile.ZipFile(package,"w") as z:
    z.writestr("review.json",json.dumps(doc));z.writestr("media/source.png",png(im));z.writestr("images/"+asset+".png",tile)
with sync_playwright() as pw:
    proc,p=launch(pw,package,"typeof imageDoc==='function' && imageDoc() && $('openedImage').naturalWidth===2048")
    try:
        errors=[];p.on("console",lambda m:errors.append(m.text) if m.type=="error" else None)
        p.locator("#copyImage").click()
        try:wait(p,"!busy && $('status').textContent.includes('批注图已复制')",timeout=16000)
        except Exception:print(errors);raise
        for i in range(30):
            try:cb.OpenClipboard();break
            except Exception:time.sleep(.06)
        try:data=cb.GetClipboardData(cb.RegisterClipboardFormat("PNG"));assert cb.IsClipboardFormatAvailable(cb.CF_DIB)
        finally:cb.CloseClipboard()
        result=Image.open(io.BytesIO(data)).convert("RGBA");assert result.size==(W,H)
        assert len(data)>768*1024
        for x,y in [(1,1),(W-2,1),(1,H-2),(W-2,H-2),(645,40),(164,650)]:assert result.getpixel((x,y))==im.getpixel((x,y))
        for x,y,col in [(.1,.3,(0,255,0)),(.80,.4,(0,255,255)),(.55,.62,(255,0,255))]:
            assert result.getpixel((round(W*x),round(H*y)))[:3]==col
        # Each badge contains colored fill and dark glyph pixels, above its own shape.
        for x,y,bg in [(.1,.25,(0,255,0)),(.76,.4,(0,255,255))]:
            crop=result.crop((int(W*x)+5,int(H*y)-25,int(W*x)+35,int(H*y)-7))
            assert bg+(255,) in crop.get_flattened_data() and (21,32,42,255) in crop.get_flattened_data()
        box=p.locator("#stage").bounding_box();x=box["x"]+box["width"]*.8;y=box["y"]+box["height"]*.5
        p.mouse.move(x,y);p.mouse.wheel(0,-960);wait(p,"$('stage').classList.contains('pannable')")
        p.mouse.down();p.mouse.move(x+45,y-70);p.mouse.up()
        before=p.locator("#openedImage").get_attribute("style")
        dest=work/"share.png";p.locator("#saveImage").click(no_wait_after=True)
        d=Desktop(backend="win32").window(process=proc.pid,class_name="#32770");d.wait("ready");edit=d.child_window(class_name="Edit",found_index=0)
        time.sleep(.3);edit.set_edit_text(str(dest));d.child_window(control_id=1).click()
        wait(p,"!busy && $('status').textContent.startsWith('批注图已保存')",timeout=16000)
        assert Image.open(dest).convert("RGBA").tobytes()==result.tobytes()
        assert before==p.locator("#openedImage").get_attribute("style")
        chat=p.context.new_page();chat.set_content('<textarea autofocus></textarea>');chat.evaluate("document.addEventListener('paste',e=>{window.mime=e.clipboardData.files[0]?.type})")
        chat.locator("textarea").focus();chat.keyboard.press("Control+v");assert chat.evaluate("window.mime")=="image/png";chat.close()
        print("PASS full 2048x1024 PNG: original corners/alpha, box, arrow, both numbered badges, attachment, >1 chunk, identical after zoom/pan, PNG+DIB and chat paste.")
    finally:stop(proc)

