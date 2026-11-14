import subprocess,sys,tempfile,winreg,os
from pathlib import Path
from playwright.sync_api import sync_playwright
from PIL import Image
from review_ui import work,launch,stop
import review_ui
setup=Path(sys.argv[3]).resolve();dest=work/"Installed FrameNote"
def reg(key,name=""):
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER,key) as k:return winreg.QueryValueEx(k,name)[0]
    except FileNotFoundError:return None
assert not reg(r"Software\Microsoft\Windows\CurrentVersion\Uninstall\{798D3800-50B8-422F-990F-39EA016163C2}_is1","InstallLocation"),"Use a disposable profile without FrameNote installed"
exts="png apng jpg jpeg jfif bmp gif webp avif ico tif tiff".split()
def choice(e):return reg(r"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\."+e+r"\UserChoice","ProgId")
prior={e:choice(e) for e in exts}
os.environ["FRAMENOTE_DATA_DIR"]=str(work/"profile")
try:
    subprocess.run([str(setup),"/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/LOG="+str(work/"install.log"),"/DIR="+str(dest)],check=True)
    for e in exts:
        assert reg(r"Software\Meidios\FrameNote\Capabilities\FileAssociations","."+e)=="FrameNote.Image",e
        assert reg(r"Software\Classes\."+e+r"\OpenWithProgids","FrameNote.Image")==""
        assert choice(e)==prior[e]
    assert reg(r"Software\Classes\FrameNote.Image\shell\open\command")==f'"{dest}\\FrameNote.exe" "%1"'
    review_ui.app=dest/"FrameNote.exe";pic=work/"test image 透明.png";Image.new("RGBA",(58,37),(8,20,200,180)).save(pic)
    with sync_playwright() as pw:
        proc,page=launch(pw,pic,"document.querySelector('#openedImage')?.naturalWidth===58")
        try:assert page.evaluate("imageDoc() && !doc.notes.length && !ready && $('openedImage').naturalHeight===37")
        finally:stop(proc)
    print("PASS twelve Windows image associations, unchanged user defaults, installed app opening a picture from its command line")
finally:
    if (dest/"unins000.exe").exists():subprocess.run([str(dest/"unins000.exe"),"/VERYSILENT","/NORESTART"],check=True)
    assert all(choice(e)==prior[e] for e in exts)
