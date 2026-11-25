"""Exercise the release WebView2 UI. Requires playwright and pywinauto; see README."""
import hashlib, json, os, re, shutil, socket, subprocess, sys, tempfile, time, zipfile
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import url2pathname
from playwright.sync_api import sync_playwright
from pywinauto import Desktop

app = Path(sys.argv[1]).resolve()
fixtures = Path(sys.argv[2]).resolve()
work = Path(tempfile.mkdtemp(prefix="review-ui-",dir=fixtures))
sample = work / "同步演示.mp4"
package = work / "中文审阅.framenote"
def desk(proc):
    window=Desktop(backend="win32").window(process=proc.pid,title_re=".*FrameNote.*",found_index=0)
    window.wait("ready",timeout=30)
    return Desktop(backend="uia").window(handle=window.handle)

def settings(proc, section="通用"):
    win = desk(proc)
    file_menu = win.child_window(title_re="文件.*", control_type="MenuItem",depth=3)
    file_menu.expand()
    win.child_window(title="设置", control_type="MenuItem",found_index=0).invoke()
    window = Desktop(backend="win32").window(process=proc.pid,title="设置")
    window.wait("ready",timeout=10)
    dlg = Desktop(backend="uia").window(handle=window.handle)
    dlg.wait("ready")
    assert dlg.child_window(title="打开默认应用设置", control_type="Button").exists()
    if section!="通用":
        dlg.child_window(title=section,control_type="TabItem").select()
    return dlg

def wait(page,expr,arg=None,timeout=6000):
    until=time.monotonic()+timeout/1000
    while time.monotonic()<until:
        if page.evaluate(expr,arg):return
        time.sleep(.035)
    raise AssertionError((expr,page.url,page.locator('body').inner_text()[-1000:]))

def export_window(proc):
    window=Desktop(backend="win32").window(process=proc.pid,title_re=r"^(?:(?:正在导出|Exporting) .+|导出完成|Export complete|导出失败|Export failed)$")
    window.wait("visible",timeout=15,retry_interval=.025)
    return Desktop(backend="uia").window(handle=window.handle)

def exported_file(proc, destination=None, timeout=90):
    """Dismiss the completion prompt and read the export result from the review session."""
    window=Desktop(backend="win32").window(process=proc.pid,title_re=r"^(导出完成|Export complete)$")
    window.wait("visible",timeout=timeout)
    prompt=Desktop(backend="uia").window(handle=window.handle)
    assert not any(Path(t.window_text()).is_absolute() for t in prompt.descendants(control_type="Text"))
    status=proc.review_page.locator("#status").text_content()
    match=re.search(r"(?:[A-Za-z]:[\\/]|\\\\).+$",status)
    assert match, status
    path=Path(match.group())
    assert path.is_file(), path
    prompt.child_window(auto_id="DismissExport",control_type="Button").invoke()
    window.wait_not("visible")
    if destination is not None and path!=destination:
        assert path.suffix==destination.suffix and not destination.exists()
        path.rename(destination)
        return destination
    return path
def launch(pw,path,condition="typeof ready!=='undefined' && ready"):
    with socket.socket() as s:
        s.bind(("127.0.0.1",0)); port=s.getsockname()[1]
    env=dict(os.environ, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=f"--remote-debugging-port={port} --remote-debugging-address=127.0.0.1",
             WEBVIEW2_USER_DATA_FOLDER=str(work / ("browser-"+str(port))))
    profile=Path(env.setdefault("FRAMENOTE_DATA_DIR",str(work/"profile")))
    if profile.is_relative_to(work):
        profile.mkdir(parents=True,exist_ok=True)
        config=profile/"capture.json"
        capture=json.loads(config.read_text(encoding="utf-8-sig")) if config.exists() else {}
        if "exportDirectory" not in capture:
            capture["exportDirectory"]=str(work/"exports")
            config.write_text(json.dumps(capture),encoding="utf-8")
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
        page.wait_for_url("**/ui/index.html",wait_until="domcontentloaded",timeout=30000)
        wait(page,condition,timeout=30000)
        proc.review_page=page
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
def switch_file(proc,page,path):
    page.locator("#open").click()
    dlg=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
    dlg.wait("ready",timeout=10)
    filename=dlg.child_window(class_name="Edit",found_index=0)
    filename.wait("ready")
    filename.set_edit_text(str(path))
    filename.type_keys("{ENTER}")
    wait(page,"name=>!busy && doc?.fileName===name && (ready || imageDoc())",path.name,timeout=20000)
if __name__ == '__main__':
    profile=work/"profile"
    os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
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
                at(page,7);page.locator("#arrow").click()
                box=page.locator("#overlay").bounding_box()
                page.mouse.move(box['x']+20,box['y']+20);page.mouse.down();page.mouse.move(box['x']+110,box['y']+80);page.mouse.up()
                page.locator("#body").fill("撞击声偏晚");page.locator('#colors').get_by_role("button",name="紫藤").click()
                page.keyboard.press("Control+Enter");assert len(notes(page))==1 and notes(page)[0]["shape"]["kind"]=="arrow"
                at(page,11);page.keyboard.press("i");at(page,n-1);page.keyboard.press("o")
                page.locator("#body").fill("压低环境声");page.keyboard.press("Control+Enter")
                page.locator("#body").fill("保留对白");page.keyboard.press("Control+Enter")
                page.keyboard.press("Control+z");assert notes(page)[1]["text"]=="压低环境声"
                assert page.locator(".noteNumber").all_text_contents()==["#1","#2"]
                at(page,0);page.keyboard.press("m");page.locator("#body").fill("片头");page.keyboard.press("Control+Enter")
                assert page.locator(".noteNumber").all_text_contents()==["#3","#1","#2"]
                assert page.locator("#marks button").first.get_attribute("aria-description").startswith("#3 ")
                page.locator(".note.selected .noteDelete").click();page.keyboard.press("Control+z")
                assert page.locator(".noteNumber").first.inner_text()=="#3"
                page.locator(".note").first.click();page.locator(".note.selected .noteDelete").click()
                region=page.locator(".note").last
                region.click();assert page.locator("#loop").is_disabled()
                region.dblclick()
                wait(page,"!v.paused && $('loop').getAttribute('aria-pressed')==='true'")
                expected_region=notes(page)[1]
                assert page.evaluate("timeSelection")=={k:expected_region[k] for k in ("startUs","endUs")}
                page.locator("#loop").click()
                page.keyboard.press("Escape");assert page.locator("#loop").is_disabled()
                page.locator("#save").click()
                package=exported_file(proc)
                wait(page,"document.querySelector('#status').textContent.startsWith('审阅包已保存')")
                before=notes(page);page.screenshot(path=str(work/"review.png"))
                at(page,7);page.locator("#splitClip").click()
                assert page.locator("#videoClips .editClip").count()==2
                page.locator("#pictureFile").set_input_files(work/"review.png")
                wait(page,"editing?.pictures?.length===1")
                images=Path(url2pathname(urlsplit(page.evaluate("imageBase")).path))
                picture=images/(page.evaluate("editing.pictures[0].asset")+".png")
                assert picture.is_file()
                page.keyboard.press("Control+Enter")
                in_session=notes(page)
                timeline=page.evaluate("doc.timeline")
                switch_file(proc,page,fixtures/"60.mp4")
                assert notes(page)==[]
                switch_file(proc,page,sample)
                actual=notes(page)
                assert len(actual)==len(in_session)
                for restored,original in zip(actual,in_session):
                    for key,value in original.items():assert restored[key]==value,(key,restored,original)
                restored_timeline=page.evaluate("doc.timeline")
                assert restored_timeline["linked"]==timeline["linked"]
                assert len(restored_timeline["clips"])==len(timeline["clips"])
                for restored,original in zip(restored_timeline["clips"],timeline["clips"]):
                    for key,value in original.items():assert restored[key]==value,(key,restored,original)
                assert not (profile/"Drafts").exists()
                # Model a decoder that releases its attachment shortly after WM_CLOSE.
                from win32con import WM_CLOSE
                window=Desktop(backend="win32").window(process=proc.pid,title_re=".*FrameNote$",found_index=0)
                with picture.open("rb"):
                    window.post_message(WM_CLOSE)
                    time.sleep(.35)
                proc.wait(timeout=8)
            finally:
                if proc.poll() is None:stop(proc)
        assert not images.parent.exists(),images.parent
        proc,page=launch(pw,sample)
        try:assert notes(page)==[] and page.evaluate("doc.timeline == null")
        finally:stop(proc)
        digest=hashlib.sha256(sample.read_bytes()).hexdigest();sample.rename(sample.with_suffix(".moved"))
        with zipfile.ZipFile(package) as z:assert hashlib.sha256(z.read("media/source.mp4")).hexdigest()==digest
        proc,page=launch(pw,package)
        try:
            assert notes(page)==before
            page.keyboard.press("m");assert page.locator(".note.selected .noteNumber").inner_text()=="#4"
            page.keyboard.press("Escape")
            # Empty notes now persist immediately; Escape finishes editing, not creation.
            assert any(n["number"]==4 and n["text"]=="" for n in notes(page))
            page.locator(".note.selected .noteDelete").click();page.locator(".note").first.click()
            wait(page,"Math.abs(shown-doc.notes[0].startUs)<=2")
            assert page.locator("#ink line:not(.inkHit)").count()==1
            assert notes(page)[0]["color"]=="#cfb1ff"
            assert page.locator("#ink line:not(.inkHit)").get_attribute("stroke")=="#cfb1ff"
            assert page.locator(".noteNumber").first.evaluate("el=>getComputedStyle(el).backgroundColor")=="rgb(207, 177, 255)"
            assert page.locator("#marks button").first.evaluate("el=>getComputedStyle(el).getPropertyValue('--mark')")=="#cfb1ff"
            page.locator("#body").fill("复核");page.keyboard.press("Control+Enter")
            assert notes(page)[0]["text"]=="复核" or notes(page)[1]["text"]=="复核"
        finally:stop(proc)
        proc,page=launch(pw,package)
        try:assert notes(page)==before
        finally:stop(proc)
    assert not (profile/"Drafts").exists()
    (fixtures/"latest.txt").write_text(str(package),encoding="utf8")
    print("Fresh source on restart, portable reopen, editable notes, media SHA-256 OK:",package,flush=True)
    
    
    
