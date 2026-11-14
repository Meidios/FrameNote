"""Exercise the release WebView2 UI. Requires playwright and pywinauto; see README."""
import hashlib, json, os, shutil, socket, subprocess, sys, tempfile, time, zipfile
from pathlib import Path
from playwright.sync_api import sync_playwright
from pywinauto import Desktop

app = Path(sys.argv[1]).resolve()
fixtures = Path(sys.argv[2]).resolve()
work = Path(tempfile.mkdtemp(prefix="review-ui-",dir=fixtures))
sample = work / "同步演示.mp4"
package = work / "中文审阅.framenote"
def wait(page,expr,arg=None,timeout=6000):
    until=time.monotonic()+timeout/1000
    while time.monotonic()<until:
        if page.evaluate(expr,arg):return
        time.sleep(.035)
    raise AssertionError((expr,page.url,page.locator('body').inner_text()[-1000:]))
def launch(pw,path,condition="typeof ready!=='undefined' && ready"):
    with socket.socket() as s:
        s.bind(("127.0.0.1",0)); port=s.getsockname()[1]
    env=dict(os.environ, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=f"--remote-debugging-port={port} --remote-debugging-address=127.0.0.1",
             WEBVIEW2_USER_DATA_FOLDER=str(work / ("browser-"+str(port))))
    proc=subprocess.Popen([str(app)]+([str(path)] if path else []),env=env)
    try:
        until=time.monotonic()+30
        while True:
            try: b=pw.chromium.connect_over_cdp(f"http://127.0.0.1:{port}",timeout=1200);break
            except Exception:
                if time.monotonic()>until: raise
                time.sleep(.2)
        ctx=b.contexts[0]
        while not ctx.pages and time.monotonic()<until:time.sleep(.2)
        page=ctx.pages[0]

        wait(page,condition,timeout=30000)
        return proc,page
    except: proc.kill();raise
def stop(proc):
    from pywinauto import Application
    try: Application().connect(process=proc.pid).top_window().close();proc.wait(timeout=8)
    except: proc.kill();proc.wait()
def at(page,i):
    page.evaluate("v.pause()")
    # Seek through the same range input as a human. Check presented pixels' PTS, not currentTime.
    page.locator("#seek").evaluate("(el,i)=>{el.value=frames[i];el.dispatchEvent(new Event('input'))}",i)
    wait(page,"i=>Math.abs(shown-frames[i])<=2",arg=i,timeout=5000)
def notes(page):return page.evaluate("doc.notes")
if __name__ == '__main__':
    with sync_playwright() as pw:
        for label in ["2997","60","vfr"]:
            shutil.copyfile(fixtures/(label+".mp4"),sample)
            proc,page=launch(pw,sample)
            try:
                n=page.evaluate("frames.length")
                for i in [1,n//2,n-1]:
                    at(page,i);page.locator("#prev").click()
                    wait(page,"i=>Math.abs(shown-frames[i])<=2",arg=i-1)
                    page.locator("#next").click();wait(page,"i=>Math.abs(shown-frames[i])<=2",arg=i)
                print(label,n,"first/middle/end presentation timestamps OK",flush=True)
                if label!="vfr":continue
                at(page,7);page.keyboard.press("m");page.locator("#body").fill("撞击声偏晚");page.get_by_role("button",name="紫藤").click();page.locator("#arrow").click()
                box=page.locator("#overlay").bounding_box()
                page.mouse.move(box['x']+20,box['y']+20);page.mouse.down();page.mouse.move(box['x']+110,box['y']+80);page.mouse.up()
                page.keyboard.press("Control+Enter");assert len(notes(page))==1 and notes(page)[0]["shape"]["kind"]=="arrow"
                at(page,11);page.keyboard.press("i");at(page,n-1);page.keyboard.press("o")
                page.locator("#body").fill("压低环境声");page.keyboard.press("Control+Enter")
                page.locator("#edit").click();page.locator("#body").fill("保留对白");page.keyboard.press("Control+Enter")
                page.locator("#undo").click();assert notes(page)[1]["text"]=="压低环境声"
                assert page.locator(".noteNumber").all_text_contents()==["#1","#2"]
                at(page,0);page.keyboard.press("m");page.locator("#body").fill("片头");page.keyboard.press("Control+Enter")
                assert page.locator(".noteNumber").all_text_contents()==["#3","#1","#2"]
                assert page.locator("#marks button").first.get_attribute("title").startswith("#3 ")
                page.locator("#del").click();page.locator("#undo").click()
                assert page.locator(".noteNumber").first.inner_text()=="#3"
                page.locator(".note").first.click();page.locator("#del").click()
                page.locator(".note").last.click();page.locator("#loop").click()
                wait(page,"!v.paused && loopId!==null");page.locator("#loop").click()
                page.locator("#gif").click()
                dlg=Desktop(backend="win32").window(process=proc.pid,class_name="#32770");dlg.wait("ready",timeout=10)
                dlg.child_window(class_name="Edit",found_index=0).set_edit_text(str(work/"range.gif"))
                dlg.child_window(control_id=1).click()
                wait(page,"document.querySelector('#status').textContent.startsWith('GIF 已保存')")
                from PIL import Image
                with Image.open(work/"range.gif") as gif:
                    assert gif.info["loop"]==0 and 26<=gif.n_frames<=28
                page.locator("#save").click()
                dlg=Desktop(backend="win32").window(process=proc.pid,class_name="#32770");dlg.wait("ready",timeout=10)
                dlg.child_window(class_name="Edit",found_index=0).set_edit_text(str(package))
                dlg.child_window(control_id=1).click()
                wait(page,"document.querySelector('#status').textContent.startsWith('审阅包已保存')")
                before=notes(page);page.screenshot(path=str(work/"review.png"))
            finally:stop(proc)
        proc,page=launch(pw,sample)
        try:assert notes(page)==before
        finally:stop(proc)
        digest=hashlib.sha256(sample.read_bytes()).hexdigest();sample.rename(sample.with_suffix(".moved"))
        with zipfile.ZipFile(package) as z:assert hashlib.sha256(z.read("media/source.mp4")).hexdigest()==digest
        proc,page=launch(pw,package)
        try:
            assert notes(page)==before
            page.keyboard.press("m");assert page.locator("#editTitle").inner_text().startswith("#4")
            page.keyboard.press("Escape");page.locator(".note").first.click()
            wait(page,"Math.abs(shown-doc.notes[0].startUs)<=2")
            assert page.locator("#ink line:not(.inkHit)").count()==1
            assert notes(page)[0]["color"]=="#cfb1ff"
            assert page.locator("#ink line:not(.inkHit)").get_attribute("stroke")=="#cfb1ff"
            assert page.locator(".noteNumber").first.evaluate("el=>getComputedStyle(el).backgroundColor")=="rgb(207, 177, 255)"
            assert page.locator("#marks button").first.evaluate("el=>getComputedStyle(el).getPropertyValue('--mark')")=="#cfb1ff"
            page.locator("#edit").click();page.locator("#body").fill("复核");page.keyboard.press("Control+Enter")
            assert notes(page)[0]["text"]=="复核" or notes(page)[1]["text"]=="复核"
        finally:stop(proc)
    (fixtures/"latest.txt").write_text(str(package),encoding="utf8")
    print("Draft, portable reopen, editable notes, media SHA-256 OK:",package,flush=True)
    
    
    
