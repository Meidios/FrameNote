"""Exercise cursor-anchored zoom against decoded pixels in the review UI and native Player."""
import io, os, subprocess, time
from PIL import Image, ImageChops, ImageGrab
from playwright.sync_api import sync_playwright
from pywinauto import Desktop, keyboard, mouse
import win32gui
from review_ui import app, launch, stop, switch_file, wait, work

profile=work/"profile";profile.mkdir()
(profile/"updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
clip=work/"zoom.mp4"
subprocess.run(["ffmpeg","-v","error","-f","lavfi","-i","color=c=black:s=640x360:r=12",
                "-t","30","-vf","drawbox=x=174:y=98:w=36:h=26:color=magenta:t=fill",
                "-c:v","libx264","-preset","ultrafast",str(clip)],check=True)

def marker(image):
    red,green,blue=image.convert("RGB").split()
    mask=ImageChops.subtract(ImageChops.darker(red,blue),green).point(lambda p:255 if p>155 else 0)
    return mask.getbbox()

def size(r):return (r[2]-r[0],r[3]-r[1])
def match(actual,expected,tolerance=2):
    return actual and max(abs(a-b) for a,b in zip(actual,expected))<=tolerance
def twice(r,x,y):
    return (2*r[0]-x,2*r[1]-y,2*r[2]-x,2*r[3]-y)
def eventually(read,accept):
    until=time.monotonic()+8
    while True:
        value=read()
        if accept(value):return value
        assert time.monotonic()<until,value
        time.sleep(.08)

with sync_playwright() as pw:
    proc,page=launch(pw,clip)
    try:
        page.set_viewport_size({"width":1440,"height":900})
        page.evaluate("document.fonts.ready")
        wait(page,"!busy && !document.querySelector('dialog[open]')")
        stage=page.locator("#stage")
        snap=lambda: marker(Image.open(io.BytesIO(stage.screenshot(scale="css"))))
        before=eventually(snap,bool)
        base=page.locator("#overlay").bounding_box()
        clock=page.locator("#transport").bounding_box()
        region=stage.bounding_box()
        x,y=before[0]+3,before[1]+5
        def wheel(delta,ctrl=True):
            page.mouse.move(region["x"]+x,region["y"]+y)
            if ctrl:page.keyboard.down("Control")
            try:page.mouse.wheel(0,delta)
            finally:
                if ctrl:page.keyboard.up("Control")
        wheel(-480,False);eventually(snap,lambda r:match(r,before))
        wheel(-480);target=twice(before,x,y)
        eventually(snap,lambda r:match(r,target))
        zoomed=page.locator("#overlay").bounding_box()
        assert abs(zoomed["width"]/base["width"]-2)<.001
        assert clock==page.locator("#transport").bounding_box()
        # Draw across the colored target while zoomed and check the saved source-relative coordinates.
        page.locator("#line").click()
        for index,(px,py) in enumerate([(target[0],target[1]),(target[2],target[3])]):
            page.mouse.move(region["x"]+px,region["y"]+py)
            if index==0:page.mouse.down()
        page.mouse.up()
        wait(page,"editing?.shape?.kind==='line'")
        coords=page.evaluate("[editing.shape.x,editing.shape.y,editing.shape.toX,editing.shape.toY]")
        assert match(coords,[174/640,98/360,210/640,124/360],.012),coords
        page.keyboard.press("Control+Enter")
        wheel(480)
        eventually(lambda:page.locator("#overlay").bounding_box(),lambda r:abs(r["width"]-base["width"])<.1)
        wheel(480)
        eventually(lambda:page.locator("#overlay").bounding_box(),lambda r:abs(r["width"]-base["width"]/2)<.1)
        page.locator("#saveFrame").click()
        dialog=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
        dialog.wait("ready",timeout=10)
        dest=work/"frame.png"
        filename=dialog.child_window(class_name="Edit",found_index=0)
        filename.wait("ready");filename.set_edit_text(str(dest));dialog.child_window(control_id=1).click()
        if not dest.exists() and dialog.exists(timeout=.5) and dialog.is_visible():filename.type_keys("{ENTER}")
        def read_export():
            try:
                with Image.open(dest) as frame:
                    return frame.copy()
            except OSError:
                return None
        with eventually(read_export, bool) as frame:
            assert frame.width==640 and frame.height>=360
            assert match(marker(frame.crop((0,0,640,360))),(174,98,210,124))
        other=work/"next.mp4";other.write_bytes(clip.read_bytes());switch_file(proc,page,other)
        assert abs(page.locator("#overlay").bounding_box()["width"]-base["width"])<.1
        print("PASS review zoom in/out, cursor anchor, annotations, original-frame export and file reset",flush=True)
    finally:stop(proc)

proc=subprocess.Popen([str(app.with_name("FrameNote Player.exe")),str(clip)])
try:
    native=Desktop(backend="win32").window(process=proc.pid,title_re="FrameNote Player - .*")
    native.wait("ready",timeout=15);native.set_focus()
    window=Desktop(backend="uia").window(handle=native.handle)
    def surface():
        children=[]
        win32gui.EnumChildWindows(native.handle,lambda h,_:children.append(h),None)
        return next((win32gui.GetParent(h) for h in children if win32gui.GetClassName(h)=="FrameNote.VideoSurface"
                     and win32gui.GetClassName(win32gui.GetParent(h))=="FrameNote.VideoSurface"),None)
    area=win32gui.GetWindowRect(eventually(surface,bool))
    screen=lambda:marker(ImageGrab.grab(bbox=area,all_screens=True))
    before=eventually(screen,bool)
    x,y=before[0]+6,before[1]+5
    def scroll(count,ctrl=True):
        if ctrl:keyboard.send_keys("{VK_CONTROL down}")
        try:mouse.scroll(coords=(area[0]+x,area[1]+y),wheel_dist=count)
        finally:
            if ctrl:keyboard.send_keys("{VK_CONTROL up}")
    scroll(4,False);assert match(screen(),before)
    scroll(4);target=twice(before,x,y)
    eventually(screen,lambda r:match(r,target))
    window.child_window(auto_id="Next").invoke()
    window.child_window(auto_id="Still").wait("visible",timeout=10)
    eventually(screen,lambda r:match(r,target))
    scroll(-4);eventually(screen,lambda r:match(r,before))
    scroll(-4);eventually(screen,lambda r:r and match(size(r),[n/2 for n in size(before)]))
    window.child_window(auto_id="Toggle").invoke()
    eventually(screen,lambda r:r and match(size(r),[n/2 for n in size(before)]))
    window.child_window(auto_id="OpenButton").invoke()
    dialog=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
    dialog.wait("ready",timeout=10)
    filename=dialog.child_window(class_name="Edit",found_index=0)
    filename.wait("ready");filename.set_edit_text(str(other));dialog.child_window(control_id=1).click()
    if dialog.exists(timeout=.5) and dialog.is_visible():filename.type_keys("{ENTER}")
    eventually(native.window_text,lambda title:other.name in title)
    eventually(screen,lambda r:match(r,before))
    print("PASS native playback, stepped still, cursor anchor, zoom out, resume and file reset",flush=True)
finally:
    if proc.poll() is None:proc.terminate();proc.wait()
