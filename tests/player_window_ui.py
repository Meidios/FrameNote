"""Watch a Player file launch from process creation; a second VLC window must never own the video."""
import os, shutil, subprocess, time
import win32gui, win32process
from pywinauto import Desktop
from review_ui import app, fixtures,work

profile=work/"player-profile";profile.mkdir()
(profile/"updates.json").write_text('{"automatic":false}')
clip=work/"原片 sample.mp4";shutil.copyfile(fixtures/"capture-60.mp4",clip)
title="FrameNote Player - "+clip.name
exe=app.with_name("FrameNote Player.exe")

def run(review):
    proc=subprocess.Popen([str(exe),str(clip)],env=dict(os.environ,FRAMENOTE_DATA_DIR=str(profile)))
    def windows():
        found=[]
        def visit(h,_):
            if win32process.GetWindowThreadProcessId(h)[1]==proc.pid and win32gui.IsWindowVisible(h):
                text=win32gui.GetWindowText(h)
                if text:found.append((h,text))
        win32gui.EnumWindows(visit,None)
        return found
    try:
        handle=None
        for i in range(300):
            visible=windows()
            assert all(t==title for h,t in visible),visible
            if visible:handle=visible[0][0]
            if handle and i>=100:break
            time.sleep(.02)
        assert handle,"No player window"
        win=Desktop(backend="uia").window(handle=handle)
        clock=win.child_window(auto_id="Clock")
        assert clock.exists(timeout=4)
        end=time.monotonic()+7
        while not clock.window_text().split(" / ")[0].strip("0:. "):
            assert time.monotonic()<end,win.child_window(auto_id="Hint").window_text()
            assert windows()==[(handle,title)]
            time.sleep(.05)
        assert windows()==[(handle,title)]
        assert not (profile/"Browser").exists()
        if review:
            second=work/"第二段 clip.mp4";shutil.copyfile(clip,second)
            win.child_window(auto_id="OpenButton").invoke()
            dialog=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
            dialog.wait("ready",timeout=8)
            filename=dialog.child_window(class_name="Edit",found_index=0)
            filename.set_edit_text(str(second));dialog.child_window(control_id=1).click()
            end=time.monotonic()+8
            while win32gui.GetWindowText(handle)!="FrameNote Player - "+second.name:
                assert time.monotonic()<end,windows()
                time.sleep(.05)
            assert windows()==[(handle,"FrameNote Player - "+second.name)]
            win.child_window(auto_id="ReviewButton").invoke()
            end=time.monotonic()+14
            while win32gui.GetWindowText(handle)!=second.name+" · FrameNote":
                assert time.monotonic()<end,windows()
                time.sleep(.05)
            assert win.child_window(title="播放倍速").exists(timeout=8),"Review transport missing"
        win.close()
        proc.wait(timeout=7)
        assert proc.returncode==0 and not windows()
    finally:
        if proc.poll() is None:proc.terminate();proc.wait()
run(False);run(True)
print("PASS: direct video, single titled window, advancing clock, no startup WebView, close exits and same-window review.")
