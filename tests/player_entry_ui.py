"""Verify the separate Windows video registrations and both launch paths on an installed build."""
import ctypes, os, shutil, subprocess, sys, winreg
from playwright.sync_api import sync_playwright
from pathlib import Path
import review_ui
from review_ui import fixtures, work, launch, stop

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
subprocess.run([sys.executable,str(Path(__file__).with_name("player_window_ui.py")),*sys.argv[1:]],check=True)
print("PASS: both Shell friendly names, 18 associations, Unicode MP4 review/playback, and entering review from Player.")
