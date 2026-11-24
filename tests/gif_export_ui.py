"""Verify annotated GIFs and the header menu. Run --clipboard on an isolated window station."""
import json
import os
import subprocess
import sys
import uuid
import zipfile
from pathlib import Path
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import exported_file, export_window, fixtures, launch, stop, wait, work

os.environ["FRAMENOTE_DATA_DIR"]=str(work/"profile")

def start_save(page):
    page.locator("#exportGif").hover()
    page.get_by_role("menuitem",name="导出 GIF").click()

def make_review(name,kind,length):
    media=work/"source.mp4"
    note=dict(id=str(uuid.uuid4()),number=1,startUs=1000000,endUs=2000000,text="GIF annotation",
              color="#ff0000",shape=dict(kind=kind,x=.1,y=.2,toX=.6,toY=.7))
    clip=dict(id=str(uuid.uuid4()),track="video",startUs=0,durationUs=length,sourceUs=2000000)
    doc=dict(id=str(uuid.uuid4()),version=3,fileName=media.name,durationUs=36000000,notes=[note],lastNoteNumber=1,
             timeline=dict(sourceDurationUs=36000000,linked=True,clips=[clip]))
    path=work/(name+".framenote")
    with zipfile.ZipFile(path,"w") as z:
        z.writestr("review.json",json.dumps(doc));z.write(media,"media/source.mp4")
    return path

def check_frames(path,length,kind):
    with Image.open(path) as gif:
        assert gif.format=="GIF" and gif.info["loop"]==0 and gif.height>144
        assert gif.n_frames==round(length*12)
        elapsed=0
        for i in range(gif.n_frames):
            gif.seek(i);elapsed+=gif.info["duration"]
            if i in (3,18,32):
                rgb=gif.convert("RGB")
                assert (rgb.crop((0,144,gif.width,gif.height)).convert("L").getextrema()[1]>160)==(i==18)
                if kind=="box":
                    edge=rgb.crop((45,27,105,32))
                    assert any(r>g+60 and r>b+60 for r,g,b in edge.get_flattened_data())==(i==18)
        assert abs(elapsed-1000*length)<=10,(elapsed,length)

with sync_playwright() as pw:
    if "--clipboard" in sys.argv:
        import win32clipboard as cb
        import win32con,win32service
        station=win32service.GetUserObjectInformation(win32service.GetProcessWindowStation(),win32con.UOI_NAME)
        assert station.lower()!="winsta0","Use a private test clipboard"
        package=Path(sys.argv[4]);expected=package.with_suffix(".gif").read_bytes()
        proc,p=launch(pw,package)
        try:
            assert Desktop(backend="win32").window(process=proc.pid,title_re=".*FrameNote.*",found_index=0).exists()
            p.locator("#exportGif").hover()
            p.get_by_role("menuitem",name="复制GIF到粘贴板").click()
            wait(p,"$('status').textContent.includes('GIF 已复制')",timeout=90000)
            # A private, non-interactive window station keeps even active windows invisible.
            dialog=Desktop(backend="win32").window(process=proc.pid,title="导出完成",visible_only=False)
            dialog.wait("exists")
            dialog.post_message(win32con.WM_CLOSE)
            wait(p,"!busy && $('status').textContent.includes('GIF 已复制')",timeout=90000)
        finally:stop(proc)
        cb.OpenClipboard()
        try:
            assert cb.GetClipboardData(cb.RegisterClipboardFormat("image/gif"))==expected
            dropped,=cb.GetClipboardData(cb.CF_HDROP)
            assert Path(dropped).name=="source_批注.gif"
            assert Path(dropped).read_bytes()==expected
        finally:cb.CloseClipboard()
        print("PASS clipboard animation bytes and pastable file survive app exit",flush=True)
    else:
        proc,p=launch(pw,None,"typeof ready==='boolean'")
        try:
            assert p.locator("#exportGif").is_disabled()
            p.locator("#exportGif").hover();p.wait_for_timeout(100)
            assert p.locator("#gifExportMenu").is_hidden()
        finally:stop(proc)
        subprocess.run(["ffmpeg","-v","error","-f","lavfi","-i","testsrc2=s=240x144:r=12:d=36,hue=s=0",
                        "-c:v","libx264","-preset","ultrafast",str(work/"source.mp4")],check=True)
        for name,kind,length in [("edited","box",31.25),("lens","magnifier",3)]:
            package=make_review(name,kind,int(length*1000000))
            proc,p=launch(pw,package)
            try:
                p.evaluate("""()=>{window.openDelay=null; const g=$('gifExport');
                  g.addEventListener('pointerenter',()=>window.enterTime=performance.now(),{once:true});
                  new MutationObserver(()=>{if(!$('gifExportMenu').inert && openDelay==null)
                    openDelay=performance.now()-enterTime}).observe($('exportGif'),{attributes:true,attributeFilter:['aria-expanded']})}""")
                p.mouse.move(1,1);trigger=p.locator("#exportGif");trigger.hover()
                wait(p,"openDelay!==null")
                assert 28<=p.evaluate("openDelay")<300
                menu=p.locator("#gifExportMenu")
                assert menu.bounding_box()["y"]>=trigger.bounding_box()["y"]+trigger.bounding_box()["height"]-1
                if kind=="box":
                    p.wait_for_timeout(150)
                    r=menu.bounding_box();bar=p.locator("header").bounding_box()
                    p.screenshot(path=str(work/"gif-menu.png"),clip=dict(x=r["x"]-80,y=bar["y"],width=bar["width"]-r["x"]+80,height=r["y"]+r["height"]+8))
                p.get_by_role("menuitem",name="复制GIF到粘贴板").hover()
                assert not menu.evaluate("el=>el.inert")
                p.mouse.move(1,1);wait(p,"$('gifExportMenu').inert")
                trigger.focus();p.keyboard.press("ArrowDown")
                assert p.evaluate("document.activeElement===$('copyGif')")
                p.keyboard.press("ArrowDown");assert p.evaluate("document.activeElement===$('saveGif')")
                p.keyboard.press("Escape");assert menu.evaluate("el=>el.inert")
                if kind=="box":
                    start_save(p)
                    export_window(proc).child_window(title="取消导出",control_type="Button").invoke()
                    wait(p,"!busy && $('status').textContent.includes('已取消 GIF')",timeout=30000)
                    assert not list((work/"exports").glob("*"))
                dest=package.with_suffix(".gif");start_save(p);exported_file(proc,dest)
                wait(p,"!busy && $('status').textContent.startsWith('带批注的 GIF 已保存')",timeout=90000)
                check_frames(dest,length,kind)
            finally:stop(proc)
        print("PASS hover, keyboard, cancellation, edited 31.25-second GIF and live lens",work,flush=True)
