"""Check the hover bridge, vertical drag and wheel volume in both transports."""
import io
import os
import subprocess
import time

from PIL import Image
from playwright.sync_api import expect, sync_playwright
from pywinauto import Desktop, mouse
from review_ui import app, fixtures, launch, stop, wait, work

profile = work / "profile"
profile.mkdir()
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
(profile / "updates.json").write_text('{"automatic":false,"language":"zh-CN"}')
(profile / "shortcuts.json").write_text('{"record":[]}')
clip = fixtures / "vfr.mp4"

with sync_playwright() as pw:
    proc, p = launch(pw, clip)
    try:
        mute = p.locator("#mutePlayback")
        slider = p.locator("#playbackVolume")
        percent = p.locator("#volumePercent")
        expect(slider).to_be_hidden()
        mute.hover()
        expect(slider).to_be_visible()
        wait(p, "getComputedStyle(document.querySelector('.volumeCard')).opacity==='1'")
        b, s = mute.bounding_box(), slider.bounding_box()
        x = b["x"] + b["width"] / 2
        assert s["height"] > s["width"] and s["y"] + s["height"] < b["y"]
        expect(percent).to_be_hidden()
        p.mouse.wheel(0,-100)
        expect(slider).to_have_value("100")
        expect(percent).to_be_hidden()
        p.mouse.wheel(0, 100)
        expect(slider).to_have_value("95")
        expect(percent).to_have_text("95%")
        expect(percent).to_be_visible()
        label = percent.bounding_box()
        assert label["y"] + label["height"] < s["y"]
        shot=Image.open(io.BytesIO(p.screenshot(scale="css")))
        shot.crop((int(x-42),int(label["y"]-4),int(x+42),int(b["y"]+b["height"]+4))).save(work/"editor-volume.png")
        expect(percent).to_be_hidden(timeout=2000)
        expect(slider).to_be_visible()
        b = mute.bounding_box()
        p.mouse.move(x, b["y"] - 2)
        p.wait_for_timeout(220)
        expect(slider).to_be_visible()
        expect(p.locator(".volumeCard")).to_have_css("opacity", "1")
        s = slider.bounding_box()
        p.mouse.move(x,s["y"] + s["height"] / 2)
        p.mouse.wheel(0, 100)
        expect(slider).to_have_value("90")
        p.mouse.move(x, s["y"] + 12)
        p.mouse.down()
        try:
            p.mouse.move(x, s["y"] + s["height"] - 22, steps=6)
            value = int(slider.input_value())
            assert 8 < value < 32, value
            expect(percent).to_have_text(f"{value}%")
            expect(percent).to_be_visible()
            p.mouse.move(x - 70, s["y"] + s["height"] - 22)
            p.wait_for_timeout(220)
            expect(slider).to_be_visible()
        finally:
            p.mouse.up()
        p.mouse.move(2, 2)
        expect(slider).to_be_hidden()
        mute.click()
        expect(slider).to_have_value("0")
        expect(percent).to_have_text("0%")
        mute.click()
        expect(slider).to_have_value(str(value))
        print("PASS editor volume", flush=True)
    finally:
        stop(proc)

proc = subprocess.Popen([app.with_name("FrameNote Player.exe"), clip])
try:
    windows = Desktop(backend="win32")
    win = windows.window(process=proc.pid, title_re=".*FrameNote.*", found_index=0)
    win.wait("ready", timeout=30)
    dlg = Desktop(backend="uia").window(handle=win.handle)
    mute = dlg.child_window(auto_id="Mute", control_type="Button")
    slider = dlg.child_window(auto_id="VolumeSlider", control_type="Slider")
    percent = dlg.child_window(auto_id="VolumePercent", control_type="Text")
    mute.wait("ready", timeout=15)
    dlg.set_focus()
    b = mute.rectangle()
    mouse.move(coords=(b.left-70, b.top+5))
    slider.wait_not("visible")
    mute.wait("enabled", timeout=15)
    mouse.move(coords=(b.mid_point().x,b.mid_point().y))
    slider.wait("visible")
    time.sleep(.18)
    s = slider.rectangle()
    x, y = b.mid_point().x, b.mid_point().y
    assert s.height() > s.width() and s.bottom < b.top and abs(s.mid_point().x-x) <= 2, (s,b)
    percent.wait_not("visible")
    slider.set_value(65)
    mouse.scroll(coords=(x,y), wheel_dist=1)
    assert slider.iface_range_value.CurrentValue == 70
    percent.wait("visible")
    assert percent.window_text()=="70%" and percent.rectangle().bottom<s.top
    percent.wait_not("visible",timeout=2)
    slider.wait("visible")
    mouse.move(coords=(x,b.top-2))
    time.sleep(.22)
    slider.wait("visible")
    mouse.scroll(coords=(x,s.mid_point().y), wheel_dist=-1)
    assert slider.iface_range_value.CurrentValue == 65
    thumb_y = s.bottom - 7 - round((s.height()-14)*.65)
    mouse.press(coords=(x,thumb_y))
    try:
        mouse.move(coords=(x,s.bottom-17))
        assert 3 < slider.iface_range_value.CurrentValue < 25
        assert percent.window_text()==f"{int(slider.iface_range_value.CurrentValue)}%"
        mouse.move(coords=(x-65,s.bottom-17))
        time.sleep(.22)
        slider.wait("visible")
    finally:
        mouse.release(coords=(x-65,s.bottom-17))
    slider.wait_not("visible")
    mouse.move(coords=(x,y))
    slider.wait("visible")
    value = slider.iface_range_value.CurrentValue
    mute.click_input()
    assert slider.iface_range_value.CurrentValue == 0
    assert percent.window_text()=="0%"
    mute.click_input()
    assert slider.iface_range_value.CurrentValue == value
    slider.set_value(100)
    assert percent.window_text()=="100%"
    percent.wait_not("visible",timeout=2)
    mouse.scroll(coords=(x,y),wheel_dist=1)
    assert slider.iface_range_value.CurrentValue==100
    percent.wait_not("visible")
    mouse.move(coords=(b.left-70,b.top+5))
    slider.wait_not("visible")
    print("PASS Player volume", flush=True)
finally:
    stop(proc)
