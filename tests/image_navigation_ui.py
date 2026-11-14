"""Check cursor-anchored image zoom, panning and folder navigation through real WebView input."""
import io, os
from PIL import Image
from playwright.sync_api import sync_playwright
from review_ui import launch, stop, wait, work, fixtures
profile=work/"profile";profile.mkdir();(profile/"updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
folder=work/"图片 samples";folder.mkdir()
pic=Image.new("RGB",(640,360),"#4b748e")
p=pic.load()
for j in range(360):
    for i in range(640):p[i,j]=(i//10%2*100+70,j//9%2*80+60,(i+j)//7%2*60+90)
pic.save(folder/"图1.png");pic.save(folder/"图2.tif");pic.save(folder/"图10.GIF");(folder/"图3.txt").write_text("skip")
def rect(page):return page.locator("#openedImage").bounding_box()
def point(r,x,y):return ((x-r["x"])/r["width"],(y-r["y"])/r["height"])
def close(a,b,eps=.03):return abs(a-b)<eps
with sync_playwright() as pw:
    proc,page=launch(pw,folder/"图1.png","document.querySelector('#openedImage')?.naturalWidth===640")
    try:
        r=rect(page);x=round(r["x"]+.65*r["width"]);y=round(r["y"]+.45*r["height"])
        around=point(r,x,y);chrome=page.locator("header").bounding_box();dpr=page.evaluate("devicePixelRatio")
        page.mouse.move(x,y);page.mouse.wheel(0,-480)
        wait(page,"w=>$('openedImage').getBoundingClientRect().width>w*1.98",r["width"])
        big=rect(page);actual=point(big,x,y)
        assert max(abs(a-b) for a,b in zip(actual,around))<.0001
        page.mouse.down();page.mouse.move(x-84,y+37,steps=4);page.mouse.up()
        moved=rect(page);assert close(moved["x"]-big["x"],-84) and close(moved["y"]-big["y"],37)
        page.mouse.move(x,y);assert rect(page)==moved
        page.mouse.wheel(0,240)
        wait(page,"w=>$('openedImage').getBoundingClientRect().width<w*.8",big["width"])
        assert max(abs(a-b) for a,b in zip(point(moved,x,y),point(rect(page),x,y)))<.0001
        assert chrome==page.locator("header").bounding_box() and page.evaluate("devicePixelRatio")==dpr
        for key,name in [("Right","图2.tif"),("Right","图10.GIF"),("Right","已是最后一张"),("Left","图2.tif"),("Left","图1.png"),("Left","已是第一张")]:
            page.keyboard.press("Arrow"+key)
            wait(page,"s=>!busy && $('status').textContent.includes(s)",name)
            assert close(rect(page)["width"],r["width"])
        print("PASS: off-center zoom in/out, left drag, fixed app geometry, TIFF original folder, natural sort and limits",flush=True)
    finally:stop(proc)
    proc,page=launch(pw,fixtures/"capture-60.mp4")
    try:
        page.evaluate("v.pause();seekUs(frames[8])");wait(page,"stamp()===frames[8]")
        page.keyboard.press("ArrowRight");wait(page,"stamp()===frames[9]")
        page.keyboard.press("ArrowLeft");wait(page,"stamp()===frames[8]")
        print("PASS: video arrow keys still step one frame",flush=True)
    finally:stop(proc)
