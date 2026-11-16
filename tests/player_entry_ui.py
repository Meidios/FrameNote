"""Verify the separate Windows video registrations and both launch paths on an installed build."""
import ctypes, os, shutil, winreg
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
import review_ui
from review_ui import fixtures, work, launch, stop, wait

root=review_ui.app.parent
player=root/"FrameNote Player.exe"
def reg(key,name=""):
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key) as k:return winreg.QueryValueEx(k,name)[0]
def shell_name(progid):
    buf=ctypes.create_unicode_buffer(1024);size=ctypes.c_uint(len(buf))
    hr=ctypes.windll.shlwapi.AssocQueryStringW(0,4,progid,"open",buf,ctypes.byref(size))
    assert hr==0,(progid,hr)
    return buf.value
def verify(name,progid):
    cap=reg(r"Software\RegisteredApplications",name)
    for ext in ".mp4 .m4v .mov .webm .mkv .avi .wmv .asf .mpg .mpeg .ts .m2ts .mts .flv .mxf .vob .ogv .3gp".split():
        assert reg(cap+r"\FileAssociations",ext)==progid
        assert reg("Software\\Classes\\"+ext+r"\OpenWithProgids",progid)==""
    command=reg("Software\\Classes\\"+progid+r"\shell\open\command")
    assert command==f'"{root/(name+".exe")}" "%1"',command
    assert shell_name(progid)==name,(progid,shell_name(progid))
verify("FrameNote Player","FrameNote.Player.Video")
verify("FrameNote","FrameNote.Review.Video")
assert reg(r"Software\Meidios\FrameNote\Capabilities\FileAssociations",".framenote")=="FrameNote.Review"
profile=work/"profile";profile.mkdir();(profile/"updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
path=work/"原片 sample.mp4";shutil.copyfile(fixtures/"capture-60.mp4",path)
with sync_playwright() as pw:
    proc,page=launch(pw,path)
    try:assert page.locator("#transport").is_visible()
    finally:stop(proc)
    review_ui.app=player
    proc,page=launch(pw,path,"document.querySelector('#transport')!==null")
    try:
        win=Desktop(backend="uia").window(process=proc.pid,title_re=".*FrameNote Player$")
        button=win.child_window(auto_id="ReviewButton");button.wait("visible",timeout=12)
        clock=win.child_window(auto_id="Clock")
        import time
        deadline=time.monotonic()+6
        while not clock.window_text().split(" / ")[0].strip("0:.") and time.monotonic()<deadline:time.sleep(.1)
        assert clock.window_text().split(" / ")[0].strip("0:.")
        button.invoke();wait(page,"ready && !reviewProxy && !busy")
        assert page.locator("#transport").is_visible()
    finally:stop(proc)
print("PASS: both Shell friendly names, 18 associations, Unicode MP4 review/playback, and entering review from Player.")
