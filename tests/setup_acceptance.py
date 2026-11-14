"""Install an old test build, upgrade via the delivery Setup, shell-open a package, then uninstall."""
import os,sys,subprocess,tempfile,json,time,winreg,ctypes,socket
from pathlib import Path
from pywinauto import Desktop
from playwright.sync_api import sync_playwright
base=Path(tempfile.mkdtemp(prefix="FrameNote-setup-acceptance-"))
target=base/"Program Files"/"FrameNote"
profile=base/"profile";profile.mkdir()
old,new=map(lambda x:Path(x).resolve(),sys.argv[1:3])
sample=Path("examples/SampleReview.framenote").resolve()
state="Software\\Meidios\\FrameNote"
uninstall="Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{798D3800-50B8-422F-990F-39EA016163C2}_is1"
def value(key,name=""):
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key) as k:return winreg.QueryValueEx(k,name)[0]
    except FileNotFoundError:return None
ext="Software\\Classes\\.framenote"
cmd="Software\\Classes\\Applications\\FrameNote.exe\\shell\\open\\command"
choice="Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\.framenote\\UserChoice"
before=[value(ext),value(cmd),value(choice,"ProgId")]
assert value(uninstall,"InstallLocation") is None and value(state,"ExtensionKnown") is None,"existing installed product; do not touch"
def setup(exe,*extra):
    res=subprocess.run([str(exe),"/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART",*extra],timeout=150)
    assert res.returncode==0,(exe,res.returncode)
def until(fn,seconds=18):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        if fn():return
        time.sleep(.1)
    raise AssertionError(fn)
ps=profile/"updates.json"
ps.write_text(json.dumps({"automatic":False,"skip":None,"lastRun":None}))
env=os.environ.copy()
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
proc=None
try:
    setup(old,"/DIR="+str(target),"/LOG="+str(base/"old.log"))
    assert (target/"FrameNote.exe").is_file()
    setup(new,"/DIR="+str(target),"/LOG="+str(base/"upgrade.log"))
    assert value(state,"PreviousVersion")=="0.1.0.0"
    assert value(ext)=="FrameNote.Review"
    assert value(ext+"\\OpenWithProgids","FrameNote.Review") is not None
    assert value("Software\\Classes\\FrameNote.Review\\DefaultIcon")==f'"{target}\\Assets\\FrameNote_File.ico",0'
    assert value(cmd)==f'"{target}\\FrameNote.exe" "%1"'
    assert (target/"tools/ffprobe.exe").exists() and (target/"hostfxr.dll").exists()
    with socket.socket() as s:s.bind(("127.0.0.1",0));port=s.getsockname()[1]
    os.environ["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"]=f"--remote-debugging-port={port} --remote-debugging-address=127.0.0.1"
    # Query the effective shell handler without launching unrelated applications.
    cb=ctypes.c_ulong(4096);buf=ctypes.create_unicode_buffer(cb.value)
    rc=ctypes.windll.shlwapi.AssocQueryStringW(0,2,".framenote","open",buf,ctypes.byref(cb))
    assert rc==0 and Path(buf.value).resolve()==(target/"FrameNote.exe").resolve(),(rc,buf.value)
    print("Effective handler (Windows may retain an earlier explicit choice):",buf.value,flush=True)
    proc=subprocess.Popen([str(target/"FrameNote.exe")])
    ui=Desktop(backend="uia")
    def dialog(pid,title):
        win=Desktop(backend="win32").window(process=pid,title=title)
        win.wait("visible",timeout=15)
        return ui.window(handle=win.handle)
    try:dlg=dialog(proc.pid,"更新记录 · FrameNote")
    except Exception:
        print("Visible windows:",[(w.window_text(),w.handle) for w in Desktop(backend="win32").windows(process=proc.pid)],flush=True)
        print("Baseline:",value(state,"PreviousVersion"),"uninstaller:",(target/"unins000.exe").exists(),flush=True)
        raise
    dlg.wait("visible",timeout=20)
    assert dlg.child_window(title="已从 0.1.0 更新到 0.2.0",control_type="Text").exists()
    assert dlg.child_window(control_type="ComboBox").selected_text()=="v0.2.0"
    dlg.close()
    main=ui.window(process=proc.pid,title_re="FrameNote · 视频审阅")
    main.wait("visible");main.close();proc.wait(12);proc=None
    assert json.loads(ps.read_text())["lastRun"]=="0.2.0"
    print("PASS old Setup -> new Setup, upgrade record offline, package ProgID and effective shell handler",flush=True)
    import win32com.shell.shell as shell,win32con,win32process,win32api
    h=shell.ShellExecuteEx(fMask=0x41,lpClass="FrameNote.Review",lpVerb="open",lpFile=str(sample),nShow=1)["hProcess"]
    import psutil
    def dispatched():
        global pid
        for inst in psutil.process_iter(["name","exe"]):
            if inst.info["name"]=="FrameNote.exe" and inst.info["exe"] and Path(inst.info["exe"]).resolve()==(target/"FrameNote.exe").resolve():
                pid=inst.pid;return True
        return False
    until(dispatched)
    assert Path(psutil.Process(pid).exe()).resolve()==(target/"FrameNote.exe").resolve()
    with sync_playwright() as pw:
        def connect():
            global browser
            try:browser=pw.chromium.connect_over_cdp(f"http://127.0.0.1:{port}",timeout=600);return True
            except Exception:return False
        until(connect);page=browser.contexts[0].pages[0]
        until(lambda:page.evaluate("typeof ready!=='undefined' && ready && doc.notes.length>0"),22)
        main=ui.window(handle=Desktop(backend="win32").window(process=pid,title_re=".* · FrameNote").handle)
        assert not ui.window(process=pid,title="更新记录 · FrameNote").exists(timeout=.6)
        menu=main.child_window(title_re="帮助.*",control_type="MenuItem")
        menu.expand()
        choices=[w for w in ui.windows(process=pid) for w in w.descendants(control_type="MenuItem") if w.window_text()=="关于 FrameNote"]
        assert choices,"About menu item not exposed"
        choices[0].invoke()
        about=dialog(pid,"关于 FrameNote · FrameNote");about.wait("visible");assert about.child_window(title="FrameNote 0.2.0",control_type="Text").exists()
        about.close()
        main.close();psutil.Process(pid).wait(15)
    print("PASS registered shell verb -> installed executable -> video and notes, About version, no duplicate upgrade notice",flush=True)
finally:
    if proc and proc.poll() is None:proc.terminate();proc.wait()
    import psutil
    for instance in psutil.process_iter(["name","exe"]):
        if instance.info["name"]=="FrameNote.exe" and instance.info["exe"] and Path(instance.info["exe"]).resolve()==(target/"FrameNote.exe").resolve():
            instance.terminate();instance.wait(8)
    if (target/"unins000.exe").exists():setup(target/"unins000.exe","/LOG="+str(base/"uninstall.log"))
    assert [value(ext),value(cmd)]==before[:2] and not (target/"FrameNote.exe").exists()
    assert ps.exists()
    print("PASS uninstall preserved profile and restored original association; logs:",base,flush=True)



