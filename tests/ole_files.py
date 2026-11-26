"""Hold a real cross-process Windows file drag until the test drops it or presses Esc."""
import json
import os
import subprocess
import time
from contextlib import contextmanager

from pywinauto import Desktop, mouse

@contextmanager
def hold_files(paths):
    script="""Add-Type -AssemblyName PresentationFramework
$w=New-Object Windows.Window
$w.Title='FrameNote QC file source';$w.WindowStartupLocation='Manual';$w.Left=12;$w.Top=12;$w.Width=220;$w.Height=100;$w.Topmost=$true
$b=New-Object Windows.Controls.Button;$b.Content='Drag files';$w.Content=$b
$b.Add_PreviewMouseMove({if($_.LeftButton -eq 'Pressed'){
$d=New-Object Windows.DataObject;$d.SetData([Windows.DataFormats]::FileDrop,[string[]](ConvertFrom-Json $env:FRAMENOTE_QC_FILES))
[void][Windows.DragDrop]::DoDragDrop($b,$d,[Windows.DragDropEffects]::Copy)
}})
[void]$w.ShowDialog()"""
    proc=subprocess.Popen(['powershell','-NoProfile','-STA','-Command',script],creationflags=subprocess.CREATE_NO_WINDOW,
                          env=dict(os.environ,FRAMENOTE_QC_FILES=json.dumps(list(map(str,paths)))))
    try:
        source=Desktop(backend='win32').window(title='FrameNote QC file source',process=proc.pid)
        source.wait('ready',timeout=12)
        r=source.rectangle()
        mouse.press(coords=(r.left+70,r.top+55));mouse.move(coords=(r.left+86,r.top+55));time.sleep(.3)
        yield
    finally:
        mouse.release();proc.terminate();proc.wait()
