"""Exercise a mixed playlist and real Explorer-style OLE drops onto both renderers."""
import json, os, subprocess, sys, time
from pathlib import Path
from PIL import Image
import win32gui,win32con,win32process
from pywinauto import Desktop, mouse
from review_ui import app, fixtures, work

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
    code="""Add-Type -AssemblyName PresentationFramework
$w=New-Object Windows.Window
$w.Title='Playlist QC file source';$w.WindowStartupLocation='Manual';$w.Left=12;$w.Top=12;$w.Width=220;$w.Height=100;$w.Topmost=$true
$b=New-Object Windows.Controls.Button;$b.Content='Drag files';$w.Content=$b
$b.Add_PreviewMouseMove({if($_.LeftButton -eq 'Pressed'){
$d=New-Object Windows.DataObject;$d.SetData([Windows.DataFormats]::FileDrop,[string[]](ConvertFrom-Json $env:PLAYLIST_QC_DROP))
[void][Windows.DragDrop]::DoDragDrop($b,$d,[Windows.DragDropEffects]::Copy)
}})
[void]$w.ShowDialog()"""
    src=subprocess.Popen(['powershell','-NoProfile','-STA','-Command',code],creationflags=subprocess.CREATE_NO_WINDOW,env=dict(os.environ,PLAYLIST_QC_DROP=json.dumps(list(map(str,paths)))))
    try:
        start=Desktop(backend='win32').window(title='Playlist QC file source',process=src.pid);start.wait('ready',timeout=12)
        r=start.rectangle();a=(r.left+70,r.top+58);r=win.rectangle();b=(r.left+int(r.width()*.52),r.top+int(r.height()*.44))
        mouse.press(coords=a);mouse.move(coords=(a[0]+16,a[1]));time.sleep(.35)
        mouse.move(coords=b);time.sleep(.4);mouse.release(coords=b)
    finally:src.terminate();src.wait()
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
        assert photo.name in win.window_text()
        rows()[1].select();until(lambda:win.window_text()=='FrameNote Player - '+video.name)
        added=work/'extra photo.png';Image.new('RGB',(400,200),'#448888').save(added)
        drop(win,[added,video]);until(lambda:len(rows())==4)
        assert video.name in win.window_text()
        rows()[2].select();until(lambda:second.name in win.window_text())
        time.sleep(.5)
        # The real photo thumbnail is colored, not an empty placeholder.
        im=rows()[0].capture_as_image().convert('RGB')
        assert any(r>150 and g<95 and 55<b<155 for r,g,b in im.getdata())
        win.capture_as_image().save(work/'playlist.png')
        print('PASS:',len(paths),'initial file(s), photo/video OLE drop, mixed selection, thumbnail and duplicate suppression.',flush=True)
        win.close();p.wait(timeout=8)
    finally:
        if p.poll() is None:p.terminate();p.wait()
run([photo]);run([photo,video,second])
