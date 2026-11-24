"""Move the source and preview independently and compare their pixels in saved media."""
import io, json, os, subprocess, time, zipfile
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import exported_file, launch, stop, wait, work

profile=work/"profile";profile.mkdir();(profile/"updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
width,height=400,240
def color(x,y,blue):return (round(20+180*x),round(25+190*y),blue)
paths=[]
for blue in (48,162):
    bitmap=Image.new("RGB",(width,height))
    bitmap.putdata([color(x/width,y/height,blue) for y in range(height) for x in range(width)])
    path=work/f"sample{blue}.png";bitmap.save(path);paths.append(path)
movie=work/"sample.mp4"
subprocess.run(["ffmpeg","-v","error","-f","rawvideo","-pix_fmt","rgb24","-s","400x240","-r","1","-i","pipe:0",
                "-c:v","libx264","-r","12","-pix_fmt","yuv420p",str(movie)],
               input=b"".join(Image.open(path).tobytes() for path in paths),check=True)

def picker(proc,path):
    dialog=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
    dialog.wait("ready",timeout=10);field=dialog.child_window(class_name="Edit",found_index=0)
    field.wait("ready");time.sleep(.2);field.set_edit_text(str(path));dialog.child_window(control_id=1).click()
    time.sleep(.35)
    if dialog.exists(timeout=.2) and dialog.is_visible():dialog.child_window(control_id=1).click()

def check_pixels(image,shape,blue,tolerance):
    im=image.convert("RGB")
    x,y=shape["x"],shape["y"];dx,dy=shape["toX"]-x,shape["toY"]-y
    tx,ty=shape["preview"]["x"],shape["preview"]["y"]
    def sample(u,v):return im.getpixel((round(u*width),round(v*height)))
    def close(actual,expected):
        assert max(abs(a-b) for a,b in zip(actual,expected))<=tolerance,(actual,expected,shape)
    for u,v in [(.15,.17),(.75,.31),(.82,.82)]:
        expected=color(x+u*dx,y+v*dy,blue)
        close(sample(x+u*dx,y+v*dy),expected)
        close(sample(tx+2*u*dx,ty+2*v*dy),expected)
    cx=(x+dx/2+tx+dx)/2;cy=(y+dy/2+ty+dy)/2
    tint=(255,216,134)
    close(sample(cx,cy),[round(c*.84+t*.16) for c,t in zip(color(cx,cy,blue),tint)])

def draw_and_move(p):
    p.locator("#magnifier").click()
    def drag(x,y,ex,ey):
        r=p.locator("#overlay").bounding_box()
        def pos(x,y):return (r["x"]+x*r["width"],r["y"]+y*r["height"])
        p.mouse.move(*pos(x,y));p.mouse.down();p.mouse.move(*pos(ex,ey),steps=6);p.mouse.up()
    drag(.08,.12,.26,.28)
    read=lambda:p.evaluate("doc.notes[0].shape")
    first=read();pv=first["preview"];dx=abs(first["toX"]-first["x"]);dy=abs(first["toY"]-first["y"])
    assert pv["x"]>=first["toX"] or pv["y"]>=first["toY"]
    drag(pv["x"]+dx,pv["y"]+dy,.54+dx,.54+dy)
    moved=read()
    assert max(abs(moved["preview"][k]-.54) for k in ("x","y"))<.006
    assert all(moved[k]==first[k] for k in ("x","toX","y","toY"))
    p.keyboard.press("Control+z");assert read()==first
    p.keyboard.press("Control+Shift+z");assert read()==moved
    drag(.17,.20,.21,.24)
    final=read();assert final["preview"]==moved["preview"] and abs(final["x"]-first["x"]-.04)<.005
    p.locator("#list").hover()
    preview=Image.open(io.BytesIO(p.locator("#ink canvas").screenshot(scale="css"))).convert("RGB")
    rgb=preview.getpixel((int(preview.width*.75),int(preview.height*.31)))
    expected=color(final["x"]+.75*dx,final["y"]+.31*dy,48)
    assert max(abs(a-b) for a,b in zip(rgb,expected))<7,(rgb,expected)
    assert p.locator("#ink .lensConnector").evaluate("path=>getComputedStyle(path).pointerEvents==='none'")
    return final

with sync_playwright() as pw:
    for image in (True,False):
        ready="typeof imageDoc==='function' && imageDoc() && $('openedImage').naturalWidth===400" if image else "typeof ready!=='undefined' && ready"
        proc,p=launch(pw,paths[0] if image else movie,ready)
        try:
            if not image:p.evaluate("seekUs(0)")
            shape=draw_and_move(p)
            if not image:
                p.locator("#noteDuration").fill("2");p.locator("#noteDuration").press("Tab")
            p.keyboard.press("Control+Enter")
            package=work/("image.framenote" if image else "video.framenote")
            p.keyboard.press("Control+s");package=exported_file(proc)
            wait(p,"!busy && $('status').textContent.startsWith('审阅包已保存')")
            with zipfile.ZipFile(package) as archive:
                assert json.loads(archive.read("review.json"))["notes"][0]["shape"]["preview"]==shape["preview"]
        finally:stop(proc)
        proc,p=launch(pw,package,ready)
        try:
            assert p.evaluate("doc.notes[0].shape.preview")==shape["preview"]
            dest=work/("image-zoom.png" if image else "frame-zoom.png")
            p.locator("#saveImage" if image else "#saveFrame").click();picker(proc,dest)
            wait(p,"name=>!busy && $('status').textContent.endsWith(name)",dest.name)
            check_pixels(Image.open(dest),shape,48,6)
            if not image:
                for gif in (False,True):
                    dest=work/("zoom.gif" if gif else "zoom.mp4")
                    if gif:
                        p.locator("#exportGif").hover();p.locator("#saveGif").click()
                    else:p.locator("#exportVideo").click()
                    dest=exported_file(proc);wait(p,"name=>!busy && $('status').textContent.endsWith(name)",dest.name,timeout=45000)
                    for t,blue in [("0.25",48),("1.25",162)]:
                        frame=subprocess.check_output(["ffmpeg","-v","error","-ss",t,"-i",str(dest),
                                                       "-frames:v","1","-f","image2pipe","-vcodec","png","pipe:1"])
                        check_pixels(Image.open(io.BytesIO(frame)),shape,blue,15)
                p.locator("#exportExcel").click();book=exported_file(proc)
                wait(p,"!busy && $('status').textContent.includes('.xlsx')",timeout=45000)
                asset,=book.parent.glob("media/*.gif")
                with Image.open(asset) as animation:
                    animation.seek(3);check_pixels(animation,shape,48,15)
                    animation.seek(15);check_pixels(animation,shape,162,15)
        finally:stop(proc)
print("PASS independent drag, undo/redo, package reopen, PNG, snapshot, MP4, GIF and Excel live preview",work)
