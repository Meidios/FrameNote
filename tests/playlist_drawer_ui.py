"""Measure the actual viewer width during playlist transitions in both renderers."""
import ctypes
import os
import subprocess
import time

from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop, keyboard
from pywinauto.timings import wait_until
import win32gui

from review_ui import app, fixtures, launch, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
photo = work / "playlist.png"
Image.new("RGB", (640, 360), "#34736b").save(photo)
animation = ctypes.c_int()
assert ctypes.windll.user32.SystemParametersInfoW(0x1042, 0, ctypes.byref(animation), 0)


def check_widths(widths, closing=False):
    if closing:
        widths = widths[::-1]
    assert widths[0] - widths[-1] >= 200, widths
    assert widths == sorted(widths, reverse=True), widths
    if animation.value:
        assert len(set(widths)) > 2, widths


with sync_playwright() as pw:
    proc, page = launch(pw, photo, "typeof imageDoc==='function' && imageDoc()")
    try:
        toggle = page.get_by_role("button", name="播放列表", exact=True)
        toggle.wait_for()
        stage = page.locator("#stage").bounding_box()
        box = toggle.bounding_box()
        assert box["x"]-stage["x"] == 8 and box["y"]-stage["y"] == 6
        assert toggle.evaluate("b=>getComputedStyle(b).opacity") == page.locator("#prevImage").evaluate("b=>getComputedStyle(b).opacity")
        page.evaluate("window.widths=[innerWidth];window.addEventListener('resize',()=>widths.push(innerWidth))")
        initial = page.evaluate("innerWidth")
        toggle.click()
        wait(page, "w=>innerWidth === w-208", initial)
        check_widths(page.evaluate("widths"))
        assert toggle.get_attribute("aria-expanded") == "true"
        page.evaluate("window.widths=[innerWidth]")
        page.keyboard.press("Alt+l")
        wait(page, "w=>innerWidth === w", initial)
        check_widths(page.evaluate("widths"), closing=True)
        # Reverse the slide while the sidebar is still opening.
        toggle.click()
        page.keyboard.press("Alt+l")
        wait(page, "w=>innerWidth===w && $('playlistToggle').getAttribute('aria-expanded')==='false'", initial)
    finally:
        stop(proc)

proc = subprocess.Popen([str(app.with_name("FrameNote Player.exe")), str(fixtures / "capture-60.mp4")])
try:
    host = Desktop(backend="win32").window(process=proc.pid, title_re="FrameNote Player - .*")
    host.wait("ready", timeout=15)
    win = Desktop(backend="uia").window(handle=host.handle)
    host.set_focus()
    toggle = win.child_window(auto_id="PlaylistButton")
    toggle.wait("visible", timeout=15)
    assert not win.child_window(title_re="播放列表.*", control_type="MenuItem").exists(timeout=.1)
    surface = host.child_window(class_name="FrameNote.VideoSurface", found_index=0)
    surface.wait("visible", timeout=8)
    surface_handle = surface.handle
    def width():
        left, _, right, _ = win32gui.GetWindowRect(surface_handle)
        return right-left
    initial = width()
    def sample(action):
        values = [width()]
        action()
        until = time.monotonic()+.6
        while time.monotonic() < until:
            values.append(width())
            time.sleep(.008)
        return values
    check_widths(sample(toggle.wrapper_object().iface_toggle.Toggle))
    assert toggle.get_toggle_state() == 1
    toggle.set_focus()
    check_widths(sample(lambda: keyboard.send_keys("%l", vk_packet=False)), closing=True)
    assert width() == initial and toggle.get_toggle_state() == 0
    full = win.child_window(auto_id="Full")
    full.invoke()
    wait_until(5,.05,lambda: "退出全屏" in full.window_text())
    assert not toggle.exists(timeout=.1) or not toggle.rectangle().width()
    keyboard.send_keys("{ESC}")
    toggle.wait("visible", timeout=5)
finally:
    stop(proc)
print("PASS: image and Player icons, animated content resize, reverse, Alt+L and fullscreen.", work)
