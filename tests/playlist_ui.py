"""Exercise a mixed playlist and real Explorer-style OLE drops onto both renderers."""
import json, os, subprocess, sys, time
from pathlib import Path
from PIL import Image, ImageGrab
import win32gui,win32con,win32process
from pywinauto import Desktop, mouse
from review_ui import app, fixtures, work
from ole_files import hold_files

profile=work/'profile';profile.mkdir();(profile/'updates.json').write_text('{"automatic":false}')
photo=work/'first photo.png';Image.new('RGB',(640,360),'#c82858').save(photo)
second=work/'second photo.png';Image.new('RGB',(240,360),'#387fbe').save(second)
video=fixtures/'capture-60.mp4'
def until(fn):
    for _ in range(100):
        result=fn()
        if result:return result
        time.sleep(.1)
    raise AssertionError(str(fn))
def drop(win,paths):
    win.set_focus()
    with hold_files(paths):
        r=win.rectangle();b=(r.left+int(r.width()*.52),r.top+int(r.height()*.44))
        assert win32gui.GetAncestor(win32gui.WindowFromPoint(b),win32con.GA_ROOT)==win.handle
        mouse.move(coords=b);time.sleep(.4);mouse.release(coords=b)

def thumbnail_visible(win,row,color):
    bounds=row.rectangle();origin=win.rectangle()
    left,top=bounds.left-origin.left,bounds.top-origin.top
    # Read the thumbnail area from the player itself, including when another window overlaps it.
    image=ImageGrab.grab(window=win.handle).crop((left+8,top+5,left+70,top+bounds.height()-5))
    return any(count>150 and all(abs(a-b)<12 for a,b in zip(pixel,color)) for count,pixel in image.getcolors(image.width*image.height))
def run(paths):
    p=subprocess.Popen([str(app.with_name('FrameNote Player.exe')),*map(str,paths)],env=dict(os.environ,FRAMENOTE_DATA_DIR=str(profile)))
    try:
        handle=until(lambda:next((w.handle for w in Desktop(backend='win32').windows(process=p.pid) if photo.name in w.window_text()),None))
        win=Desktop(backend='uia').window(handle=handle);win.wait('ready',timeout=10)
        items=win.child_window(auto_id='Items',control_type='List')
        until(lambda: photo.name in win.window_text())
        if len(paths)==1:
            assert not items.exists(timeout=.4)
            drop(win,[video,second])
        items.wait('visible',timeout=10)
        def rows():return items.children(control_type='ListItem')
        until(lambda:len(rows())==len(set(paths+[video,second])))
        until(lambda:thumbnail_visible(win,rows()[0],(200,40,88)))
        assert photo.name in win.window_text()
        rows()[1].select();until(lambda:win.window_text()=='FrameNote Player - '+video.name)
        added=work/'extra photo.png';Image.new('RGB',(400,200),'#448888').save(added)
        drop(win,[added,video]);until(lambda:len(rows())==4)
        assert video.name in win.window_text()
        rows()[2].select();until(lambda:second.name in win.window_text())
        until(lambda:thumbnail_visible(win,rows()[0],(200,40,88)) and thumbnail_visible(win,rows()[2],(56,127,190)))
        ImageGrab.grab(window=win.handle).save(work/'playlist.png')
        print('PASS:',len(paths),'initial file(s), photo/video OLE drop, mixed selection, thumbnail and duplicate suppression.',flush=True)
        win.close();p.wait(timeout=8)
    finally:
        if p.poll() is None:p.terminate();p.wait()
run([photo]);run([photo,video,second])
