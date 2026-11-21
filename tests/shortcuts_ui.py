"""Check the Windows settings dialog, persisted bindings and real canvas gestures."""
import ctypes
import json
import os
import subprocess
import time

from playwright.sync_api import sync_playwright
from pywinauto import Desktop, keyboard
from review_ui import app, at, fixtures, launch, notes, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
(profile / "shortcuts.json").write_text(json.dumps({"splitClip":{"mods":0,"vk":87},"record":{"mods":3,"vk":130}}))
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
clip = fixtures / "vfr.mp4"

def desk(proc):
    window=Desktop(backend="win32").window(process=proc.pid,title_re=".*FrameNote.*",found_index=0)
    window.wait("ready",timeout=30)
    return Desktop(backend="uia").window(handle=window.handle)

def settings(proc):
    win = desk(proc)
    file_menu = win.child_window(title_re="文件.*", control_type="MenuItem",depth=3)
    file_menu.expand()
    win.child_window(title="设置", control_type="MenuItem",found_index=0).invoke()
    window = Desktop(backend="win32").window(process=proc.pid,title="设置 · FrameNote")
    window.wait("ready",timeout=10)
    dlg = Desktop(backend="uia").window(handle=window.handle)
    dlg.wait("ready")
    assert dlg.child_window(title="打开默认应用设置", control_type="Button").exists()
    return dlg

def open_editor(dlg,name):
    button = dlg.child_window(auto_id=name, control_type="Button")
    button.set_focus()
    button.invoke()
    dlg.child_window(auto_id="ApplyBinding").wait("visible")

def listen(dlg,name,extra=False):
    open_editor(dlg,name)
    key=dlg.child_window(auto_id="recordBinding")
    if not key.exists(timeout=.1):
        dlg.child_window(auto_id="AddBinding" if extra else "editBinding0").invoke()
    key.wait("visible")
    return key

def apply(dlg):
    dlg.child_window(auto_id="ApplyBinding").invoke()
    dlg.child_window(auto_id="ApplyBinding").wait_not("visible")

def available_global(vk):
    user32 = ctypes.windll.user32
    if not user32.RegisterHotKey(None, 9821, 3, vk):
        return False
    assert user32.UnregisterHotKey(None, 9821)
    return True

def gesture(page, button, modifier, drag=True, cancel=False):
    r = page.locator("#overlay").bounding_box()
    x, y = r["x"] + .31*r["width"], r["y"] + .32*r["height"]
    page.keyboard.down(modifier)
    page.mouse.move(x,y)
    page.mouse.down(button=button)
    if drag:
        page.mouse.move(x+.3*r["width"],y+.31*r["height"],steps=5)
    if cancel:
        page.keyboard.press("Escape")
    page.keyboard.up(modifier)
    page.mouse.up(button=button)
    assert not page.locator("#canvasMenu").is_visible()

def ordinary_menu(page):
    page.locator("#overlay").click(position={"x":15,"y":15},button="right",force=True)
    assert page.locator("#canvasMenu").is_visible()
    page.keyboard.press("Escape")

def side_drag(page):
    rect = page.locator("#overlay").bounding_box()
    cdp = page.context.new_cdp_session(page)
    try:
        for event, offset in [("mousePressed",.3), ("mouseMoved",.5), ("mouseReleased",.6)]:
            cdp.send("Input.dispatchMouseEvent", {
                "type":event, "x":rect["x"]+rect["width"]*offset, "y":rect["y"]+rect["height"]*offset,
                "modifiers":1, "button":"back", "buttons":0 if event=="mouseReleased" else 8, "clickCount":1,
            })
    finally:
        cdp.detach()

def check_clip_shortcuts(page):
    clips = page.locator("#videoClips .editClip")
    frame_count = page.evaluate("frames.length")
    for frame in [frame_count // 3, 2 * frame_count // 3]:
        at(page, frame)
        page.locator("#splitClip").click()
    assert clips.count() == 3
    clips.nth(1).click()
    page.locator("#deleteClip").click()
    assert clips.count() == 2
    gap = page.evaluate("doc.timeline.clips.filter(c=>c.track==='video')")
    assert gap[0]["startUs"] + gap[0]["durationUs"] < gap[1]["startUs"]
    for i, key, action in [(0, "F8", "joinNextClip"), (1, "F7", "joinClip")]:
        clips.nth(i).click()
        assert page.locator("#" + action).is_enabled()
        page.locator("#stage").focus()
        page.keyboard.press(key)
        aligned = page.evaluate("doc.timeline.clips.filter(c=>c.track==='video')")
        assert aligned[0]["startUs"] + aligned[0]["durationUs"] == aligned[1]["startUs"]
        assert aligned[1-i] == gap[1-i]
        assert page.locator("#" + action).is_disabled()
        page.keyboard.press(key)
        page.keyboard.press("Control+z")
        assert page.evaluate("doc.timeline.clips.filter(c=>c.track==='video')") == gap

with sync_playwright() as pw:
    assert available_global(130) and available_global(131)
    proc,p = launch(pw,clip)
    try:
        at(p,7)
        clock = p.evaluate("annotationTime()")
        gesture(p,"left","Control",cancel=True)
        assert not notes(p),notes(p)
        gesture(p,"right","Control",drag=False)
        assert not notes(p),notes(p)
        gesture(p,"left","Control")
        gesture(p,"right","Control")
        gesture(p,"middle","Control",drag=False)
        assert p.locator("#body").evaluate("el=>el===document.activeElement")
        p.keyboard.type("Frame comment")
        p.keyboard.press("Control+Enter")
        ns=notes(p)
        assert [n["shape"]["kind"] for n in ns]==["box","arrow","text"]
        assert all(n["startUs"]==clock and n["endUs"] is None for n in ns)
        assert ns[-1]["text"]=="Frame comment"
        ordinary_menu(p)
        dlg=settings(proc)
        dlg.set_focus()
        assert p.evaluate("bindingLabel('splitClip')==='W'")
        assert not available_global(130)
        dlg.capture_as_image().save(work/"shortcut-settings.png")
        for action in ["joinClip", "joinNextClip"]:
            dlg.child_window(auto_id=action).child_window(title="未设置",control_type="Text").wait("exists")
        listen(dlg,"joinClip")
        keyboard.send_keys("{F7}")
        apply(dlg)
        listen(dlg,"joinNextClip")
        keyboard.send_keys("{F7}")
        dlg.child_window(title_re="此快捷键已用于「贴到上一片段」.*",control_type="Text").wait("exists")
        keyboard.send_keys("{F8}")
        apply(dlg)
        listen(dlg,"point")
        keyboard.send_keys("p",vk_packet=False)
        assert dlg.child_window(title_re="此快捷键已用于.*",control_type="Text").exists(timeout=2), [t.window_text() for t in dlg.descendants(control_type="Text")][:29]
        keyboard.send_keys("{F6}")
        apply(dlg)
        dlg.child_window(auto_id="point").child_window(title="F6",control_type="Text").wait("exists")
        listen(dlg,"point",extra=True)
        keyboard.send_keys("{F5}")
        dlg.child_window(title="确认此绑定",control_type="Button").invoke()
        dlg.capture_as_image().save(work/"shortcut-keys.png")
        apply(dlg)
        listen(dlg,"picture")
        keyboard.send_keys("{F5}")
        dlg.child_window(title_re="此快捷键已用于「添加点批注」.*",control_type="Text").wait("visible")
        dlg.child_window(auto_id="cancelBinding").invoke()
        open_editor(dlg,"picture")
        dlg.child_window(title="删除 P",control_type="Button").invoke()
        apply(dlg)
        listen(dlg,"play")
        keyboard.send_keys("k",vk_packet=False)
        apply(dlg)
        dlg.child_window(auto_id="play").child_window(title="K",control_type="Text").wait("exists")
        listen(dlg,"play",extra=True)
        keyboard.send_keys("b",vk_packet=False)
        apply(dlg)
        listen(dlg,"confirm")
        keyboard.send_keys("j",vk_packet=False)
        apply(dlg)
        dlg.child_window(auto_id="confirm").child_window(title="J",control_type="Text").wait("exists")
        listen(dlg,"quickBox")
        dlg.child_window(title="Ctrl",control_type="CheckBox").toggle()
        dlg.child_window(title="Shift",control_type="CheckBox").toggle()
        dlg.capture_as_image().save(work/"shortcut-mouse.png")
        apply(dlg)
        box=dlg.child_window(auto_id="quickBox",control_type="Button")
        box.child_window(title="Shift+鼠标左键拖拽",control_type="Text").wait("exists")
        listen(dlg,"quickBox",extra=True)
        dlg.child_window(title="Alt",control_type="CheckBox").toggle()
        dlg.child_window(auto_id="MouseChoice").select("鼠标侧键 1拖拽")
        apply(dlg)
        listen(dlg,"record",extra=True)
        keyboard.send_keys("^%{F20}")
        apply(dlg)
        dlg.child_window(title="保存",control_type="Button").invoke()
        wait(p,"bindingLabel('point')==='F6 / F5' && shortcutBindings.quickBox.keys[0].mods===4")
        assert not available_global(130) and not available_global(131)
        assert p.locator("#point").get_attribute("aria-description")=="添加点批注（F6 / F5）"
        assert p.locator("#joinClip").get_attribute("aria-description").endswith("（F7）")
        assert p.locator("#joinNextClip").get_attribute("aria-description").endswith("（F8）")
        assert "K" in p.locator("#play").get_attribute("aria-description")
        p.locator("#stage").focus()
        p.keyboard.press("m")
        assert len(notes(p))==3
        p.keyboard.press("F6")
        assert len(notes(p))==4
        p.keyboard.type("mk")
        p.keyboard.press("j")
        assert notes(p)[-1]["text"]=="mk" and not p.evaluate("!!editing")
        p.locator("#stage").focus()
        p.keyboard.press("F5")
        assert len(notes(p))==5
        p.keyboard.press("j")
        gesture(p,"left","Shift")
        assert len(notes(p))==6 and notes(p)[-1]["shape"]["kind"]=="box"
        side_drag(p)
        assert len(notes(p))==7 and notes(p)[-1]["shape"]["kind"]=="box"
        assert (profile/"shortcuts.json").is_file()
        print("PASS: default gestures, cancellation, menu, conflicts, custom shortcuts and typing.",flush=True)
    finally: stop(proc)
    proc,p=launch(pw,clip)
    try:
        assert p.evaluate("shortcutBindings.quickBox.keys[0].mods===4 && bindingLabel('play')==='K / B' && shortcutBindings.picture.keys.length===0")
        assert not available_global(130) and not available_global(131)
        gesture(p,"left","Control")
        assert not notes(p)
        gesture(p,"left","Shift")
        assert len(notes(p))==1
        dlg=settings(proc)
        dlg.child_window(title="恢复默认",control_type="Button").invoke()
        dlg.child_window(title="取消",control_type="Button").invoke()
        Desktop(backend="win32").window(process=proc.pid,title="设置 · FrameNote").wait_not("exists")
        assert p.evaluate("shortcutBindings.quickBox.keys[0].mods===4")
        check_clip_shortcuts(p)
        dlg=settings(proc)
        dlg.child_window(title="恢复「文件与批注」默认快捷键",control_type="Button").invoke()
        open_editor(dlg,"record")
        for label in ["删除 Ctrl+Alt+F19", "删除 Ctrl+Alt+F20"]:
            dlg.child_window(title=label,control_type="Button").invoke()
        apply(dlg)
        dlg.child_window(title="保存",control_type="Button").invoke()
        wait(p,"bindingLabel('picture')==='P' && bindingLabel('point')==='M' && bindingLabel('play')==='K / B' && !shortcutBindings.record.keys.length")
        assert available_global(130) and available_global(131)
        print("PASS: alternates, secondary conflicts, gesture chooser, group reset, unbinding, global registration and release.",flush=True)
        print("PASS: both saved clip shortcuts close a real gap after restart and undo in one step.",flush=True)
    finally: stop(proc)

player=subprocess.Popen([str(app.with_name("FrameNote Player.exe")),str(clip)])
try:
    win=desk(player)
    toggle=win.child_window(auto_id="Toggle",control_type="Button")
    toggle.wait("ready",timeout=15)
    end=time.monotonic()+10
    def play_hint():return toggle.wrapper_object().element_info.element.CurrentHelpText
    while "K" not in play_hint() and time.monotonic()<end:
        time.sleep(.1)
    assert "K / B" in play_hint()
    opening=win.child_window(auto_id="OpenButton",control_type="Button")
    opening.set_focus()
    before=play_hint()
    keyboard.send_keys("b",vk_packet=False)
    end=time.monotonic()+3
    while play_hint()==before and time.monotonic()<end:time.sleep(.04)
    assert play_hint()!=before,(before,play_hint())
    dlg=settings(player)
    # Another running copy may already own the default recording key.
    # Hold it here when it is free so that the failed save is deterministic.
    held_default = ctypes.windll.user32.RegisterHotKey(None, 9822, 3, 120)
    dlg.child_window(title="恢复默认",control_type="Button").invoke()
    dlg.child_window(title="保存",control_type="Button").invoke()
    try:
        dlg.child_window(title_re="无法注册全局录屏快捷键.*",control_type="Text").wait("visible")
        assert json.loads((profile/"shortcuts.json").read_text())["record"] == []
        assert "K / B" in play_hint()
    finally:
        if held_default:
            assert ctypes.windll.user32.UnregisterHotKey(None, 9822)
    if not held_default:
        open_editor(dlg,"record")
        dlg.child_window(title="删除 Ctrl+Alt+F9",control_type="Button").invoke()
        apply(dlg)
    dlg.child_window(title="保存",control_type="Button").invoke()
    Desktop(backend="win32").window(process=player.pid,title="设置 · FrameNote").wait_not("exists")
    assert json.loads((profile/"shortcuts.json").read_text())==({} if held_default else {"record":[]})
    assert "空格" in toggle.wrapper_object().element_info.element.CurrentHelpText
    print("PASS: restart, settings in standalone Player, shared bindings and restore defaults.",flush=True)
finally: stop(player)
