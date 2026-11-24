"""Exercise annotation and playback shortcuts in both Windows renderers."""
import json
import os
import subprocess
import time

import win32api
import win32clipboard
import win32con
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from pywinauto.timings import wait_until
from review_ui import app, desk, launch, notes, settings, stop, wait, work

profile=work/"profile"
profile.mkdir()
(profile/"updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
clip=work/"sample.mp4"
subprocess.run(["ffmpeg","-v","error","-f","lavfi","-i","testsrc2=size=320x180:rate=25",
                "-t","30","-c:v","libx264","-preset","ultrafast",str(clip)],check=True)
picture=work/"sample.png"
Image.new("RGB",(160,90),"navy").save(picture)
png=win32clipboard.RegisterClipboardFormat("PNG")

def native_keys(*keys):
    for key in keys:
        win32api.keybd_event(key,0,0,0)
        time.sleep(.04)
    for key in reversed(keys):
        win32api.keybd_event(key,0,win32con.KEYEVENTF_KEYUP,0)
        time.sleep(.03)

def copies_frame(press):
    before=win32clipboard.GetClipboardSequenceNumber()
    press()
    wait_until(7,.05,lambda:win32clipboard.GetClipboardSequenceNumber()!=before and win32clipboard.IsClipboardFormatAvailable(png))

def pair(page,first,second,original):
    page.keyboard.down(first)
    assert page.evaluate("transport.playbackRate")==original
    page.keyboard.down(second)
    assert page.evaluate("transport.playbackRate")==1
    page.keyboard.up(first)
    page.keyboard.up(second)
    assert page.evaluate("transport.playbackRate")==1

with sync_playwright() as pw:
    proc,p=launch(pw,clip)
    try:
        p.evaluate("transport.pause()")
        p.locator("#point").click()
        p.locator("#body").fill("Frame note")
        p.locator("#body").press("Control+a")
        p.locator("#body").press("Control+c")
        def clipboard_text():
            win32clipboard.OpenClipboard()
            try:return win32clipboard.GetClipboardData(win32con.CF_UNICODETEXT)
            finally:win32clipboard.CloseClipboard()
        assert clipboard_text()=="Frame note"
        p.locator("#body").press("Enter")
        assert not p.evaluate("isFullPreview()")
        p.locator("#stage").focus()
        p.keyboard.press("Control+d")
        a,b=notes(p)
        assert a["id"]!=b["id"] and a["number"]!=b["number"] and a["text"]==b["text"] and a["startUs"]==b["startUs"]
        p.keyboard.press("Control+z")
        assert len(notes(p))==1
        p.keyboard.press("f")
        assert not p.evaluate("doc.timeline.linked")
        p.keyboard.press("Period")
        pair(p,"Period","Comma",1.25)
        p.keyboard.press("Comma")
        pair(p,"Comma","Period",.75)
        for key,limit in [("Comma",.25),("Period",2)]:
            for _ in range(9):p.keyboard.press(key)
            assert p.evaluate("transport.playbackRate")==limit
        p.keyboard.press("Enter");wait(p,"isFullPreview()")
        p.keyboard.press("Comma");assert p.evaluate("transport.playbackRate")==1.5
        p.keyboard.press("Enter");wait(p,"!isFullPreview()")
        copies_frame(lambda:p.keyboard.press("Control+c"))
        dlg=settings(proc,"快捷键")
        dlg.set_focus()
        bindings={"duplicateNote":[17,75],"copyFrame":[17,16,67],"fullPreview":[113],
                  "linkTracks":[74],"rateDown":[85],"rateUp":[89],"rateReset":[75,76]}
        for name,keys in bindings.items():
            row=dlg.child_window(auto_id=name,control_type="Button")
            row.set_focus();row.invoke()
            dlg.child_window(auto_id="editBinding0").invoke()
            recorder=dlg.child_window(auto_id="recordBinding")
            recorder.wait("ready")
            recorder.set_focus()
            wait_until(4,.05,recorder.has_keyboard_focus)
            native_keys(*keys)
            confirm=dlg.child_window(auto_id="ApplyBinding")
            if not confirm.is_enabled():
                raise AssertionError((name,dlg.child_window(auto_id="recordBinding").window_text(),dlg.child_window(auto_id="EditorError").window_text() if dlg.child_window(auto_id="EditorError").exists() else ""))
            confirm.invoke();confirm.wait_not("visible")
        dlg.child_window(title="保存",control_type="Button").invoke()
        wait(p,"bindingLabel('rateReset')==='K+L'")
        p.locator("#stage").focus()
        p.keyboard.press("Control+k")
        assert len(notes(p))==2
        p.keyboard.press("j")
        assert p.evaluate("doc.timeline.linked")
        p.keyboard.press("F2");wait(p,"isFullPreview()")
        p.keyboard.press("y")
        pair(p,"l","k",2)
        p.keyboard.press("F2");wait(p,"!isFullPreview()")
        copies_frame(lambda:p.keyboard.press("Control+Shift+c"))
    finally:stop(proc)
    proc,p=launch(pw,picture,"typeof imageDoc==='function' && imageDoc()")
    try:
        p.locator("#point").click();p.locator("#stage").focus()
        p.keyboard.press("Control+k")
        assert len(notes(p))==2
    finally:stop(proc)

proc=subprocess.Popen([app.with_name("FrameNote Player.exe"),clip])
try:
    window=desk(proc)
    toggle=window.child_window(auto_id="Toggle")
    clock=window.child_window(auto_id="Clock")
    wait_until(10,.05,lambda:clock.exists() and "00:30" in clock.window_text())
    window.set_focus();toggle.set_focus()
    native_keys(89);wait_until(3,.05,lambda:"1.25×" in clock.window_text())
    native_keys(76,75);wait_until(3,.05,lambda:"×" not in clock.window_text())
    native_keys(85);wait_until(3,.05,lambda:"0.75×" in clock.window_text())
    native_keys(113)
    full=window.child_window(auto_id="Full")
    wait_until(3,.05,lambda:"退出全屏" in full.window_text())
    native_keys(113);wait_until(3,.05,lambda:"全屏预览" in full.window_text())
    copies_frame(lambda:native_keys(17,16,67))
finally:stop(proc)

# A fresh Player also uses the printed punctuation keys without relying on WebView key codes.
(profile/"shortcuts.json").unlink()
proc=subprocess.Popen([app.with_name("FrameNote Player.exe"),clip])
try:
    window=desk(proc)
    clock=window.child_window(auto_id="Clock")
    wait_until(10,.05,lambda:clock.exists() and "00:30" in clock.window_text())
    window.set_focus();window.child_window(auto_id="Toggle").set_focus()
    native_keys(188);wait_until(3,.05,lambda:"0.75×" in clock.window_text())
    native_keys(190,188);wait_until(3,.05,lambda:"×" not in clock.window_text())
    native_keys(190);wait_until(3,.05,lambda:"1.25×" in clock.window_text())
    native_keys(188,190);wait_until(3,.05,lambda:"×" not in clock.window_text())
    full=window.child_window(auto_id="Full")
    # Enter activates a focused command, including Open and Full.
    window.child_window(auto_id="OpenButton").set_focus()
    native_keys(13)
    picker=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
    picker.wait("ready")
    picker.type_keys("{ESC}")
    picker.wait_not("visible")
    full.set_focus()
    native_keys(13);wait_until(3,.05,lambda:"退出全屏" in full.window_text())
    full.set_focus()
    native_keys(13);wait_until(3,.05,lambda:"全屏预览" in full.window_text())
    copies_frame(lambda:native_keys(17,67))
finally:stop(proc)

# Existing user bindings own their keys when an upgrade introduces new defaults.
(profile/"shortcuts.json").write_text(json.dumps({"point":{"mods":0,"vk":70},"save":[{"mods":2,"vk":68}],"rateUp":[]}))
with sync_playwright() as pw:
    proc,p=launch(pw,clip)
    try:
        assert p.evaluate("bindingLabel('point')==='F' && bindingLabel('save')==='Ctrl+D' && !bindingLabel('duplicateNote') && !bindingLabel('linkTracks') && !bindingLabel('rateUp')")
    finally:stop(proc)
print("PASS: seven editable bindings, speed chords in both orders, text copy, image duplication, native Player and legacy conflicts.")
