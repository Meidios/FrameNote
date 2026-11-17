"""Run a packaged build from a private install tree with its review HTML missing.
Exercise normal startup and a Shell open verb without changing any file association.
"""
import json,os,shutil,socket,subprocess,time,uuid,winreg,zipfile
from pathlib import Path
import win32api,win32process,win32con
from win32com.shell import shell,shellcon
from playwright.sync_api import sync_playwright
from review_ui import app,work,fixtures,launch,wait,stop
import review_ui

install=work/"同事's [审阅] # 100%"
install.mkdir()
for entry in app.parent.iterdir():
    dest=install/entry.name
    if entry.name=="ui":
        shutil.copytree(entry,dest,ignore=shutil.ignore_patterns("index.html"))
    elif entry.is_dir():
        subprocess.run(["powershell","-NoProfile","-Command","New-Item -ItemType Junction -Path $env:FN_LINK -Target $env:FN_SOURCE | Out-Null"],
                       env=dict(os.environ,FN_LINK=str(dest),FN_SOURCE=str(entry)),check=True,creationflags=subprocess.CREATE_NO_WINDOW)
    else:
        try:os.link(entry,dest)
        except OSError:shutil.copy2(entry,dest)

review_ui.app=install/"FrameNote.exe"
profile=work/"profile";profile.mkdir();(profile/"updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
with sync_playwright() as pw:
    proc,page=launch(pw,None,"typeof catalog!=='undefined' && !!catalog")
    try:
        assert '/interface/' in page.url and not page.evaluate('!!doc')
        before=page.url;page.evaluate("location.href='about:blank'");time.sleep(.4);assert page.url==before
        print('PASS: ordinary startup restored bundled UI, catalog bridge works, foreign navigation blocked.',flush=True)
    finally:stop(proc)

    package=work/"同事 发来的.framenote"
    shutil.copyfile(Path(__file__).resolve().parents[1]/"examples"/"SampleReview.framenote",package)
    with zipfile.ZipFile(package) as z:expected=json.loads(z.read("review.json"))
    # A reachable server that never returns the installed page's subresource.
    # Previous releases only switched to the bundled page after a 30-second timeout.
    stalled=socket.socket();stalled.bind(("127.0.0.1",0));stalled.listen()
    (install/"ui"/"index.html").write_text(f'<img src="http://127.0.0.1:{stalled.getsockname()[1]}/pending">')
    verb="FrameNote.QC."+uuid.uuid4().hex
    key="Software\\Classes\\"+verb
    h=None
    try:
        with winreg.CreateKey(winreg.HKEY_CURRENT_USER,key+r"\shell\open\command") as k:
            winreg.SetValueEx(k,"",0,winreg.REG_SZ,f'"{review_ui.app}" "%1"')
        for media in (package,fixtures/"vfr.mp4"):
            with socket.socket() as s:s.bind(("127.0.0.1",0));port=s.getsockname()[1]
            os.environ["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"]=f"--remote-debugging-port={port} --remote-debugging-address=127.0.0.1"
            os.environ["WEBVIEW2_USER_DATA_FOLDER"]=str(work/f"shell-{port}")
            start=time.monotonic();end=start+50
            result=shell.ShellExecuteEx(fMask=shellcon.SEE_MASK_CLASSNAME|shellcon.SEE_MASK_NOCLOSEPROCESS,lpVerb="open",lpFile=str(media),lpClass=verb,nShow=win32con.SW_SHOWNORMAL)
            h=result["hProcess"]
            try:
                while True:
                    try:b=pw.chromium.connect_over_cdp(f"http://127.0.0.1:{port}",timeout=750);break
                    except Exception:
                        if time.monotonic()>end:raise
                        time.sleep(.1)
                ctx=b.contexts[0]
                while not ctx.pages and time.monotonic()<end:time.sleep(.1)
                p=ctx.pages[0];p.wait_for_url("**/interface/**/ui/index.html",timeout=45000)
                wait(p,"typeof ready!=='undefined' && ready && v.videoWidth>0 && v.readyState>=2",timeout=10000)
                elapsed=time.monotonic()-start
                assert p.evaluate("doc.fileName")== (expected["fileName"] if media==package else media.name)
                assert p.evaluate("doc.notes.length")== (len(expected["notes"]) if media==package else 0)
                print(f"Shell open {media.suffix}: {elapsed:.2f}s to decoded video",flush=True)
                assert elapsed<15, f"Startup stalled for {elapsed:.2f}s"
            finally:
                from pywinauto import Application
                try:Application().connect(process=win32process.GetProcessId(h)).top_window().close()
                except Exception:
                    if win32process.GetExitCodeProcess(h)==259:win32api.TerminateProcess(h,0)
                finally:h.Close()
    finally:
        stalled.close()
        for tail in [r"\shell\open\command",r"\shell\open",r"\shell",""]:
            try:winreg.DeleteKey(winreg.HKEY_CURRENT_USER,key+tail)
            except FileNotFoundError:pass
