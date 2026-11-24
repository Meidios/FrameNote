"""Check capture settings across real app restarts with an isolated user profile."""
import json
import os
import subprocess
import sys
import time
from contextlib import contextmanager
from urllib.parse import urlsplit
from urllib.request import url2pathname

from playwright.sync_api import sync_playwright
from pywinauto import Desktop, mouse
from pywinauto.timings import wait_until
import win32con
import win32gui

from review_ui import settings, desk, launch, stop, wait, work


profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
prefs = profile / "capture.json"
title = "FrameNote capture preferences QC"


def target_window():
    script = "import tkinter as t; w=t.Tk(); w.title(%r); w.geometry('480x270'); w.mainloop()" % title
    proc = subprocess.Popen([sys._base_executable, "-c", script])
    Desktop(backend="win32").window(process=proc.pid, title=title).wait("visible")
    return proc


@contextmanager
def session(pw):
    proc, page = launch(pw, None, "typeof catalog!=='undefined' && !!catalog")
    handle = desk(proc).handle
    try:
        yield proc, page
    finally:
        win32gui.PostMessage(handle, win32con.WM_CLOSE, 0, 0)
        try:
            proc.wait(timeout=8)
        except subprocess.TimeoutExpired:
            stop(proc)


def clear_audio(page):
    rows = page.locator("#outputStrips .removeChannel")
    while rows.count():
        rows.first.click()
    page.locator("#mic").select_option("")


def pick_region(proc, page):
    page.locator("#mode").select_option("region")
    page.locator("#pickRegion").click()
    picker = Desktop(backend="win32").window(process=proc.pid, title="框选录制区域")
    picker.wait("visible")
    x, y = win32gui.ClientToScreen(picker.handle, (100, 80))
    mouse.press(coords=(x, y))
    mouse.move(coords=(x+160, y+120))
    mouse.release(coords=(x+160, y+120))
    wait(page, "region?.width===160 && region?.height===120")
    return page.evaluate("region")


with sync_playwright() as pw:
    target = target_window()
    try:
        with session(pw) as (proc, page):
            assert page.locator("#fps").count()==0 and page.locator("#cursor").is_checked()
            page.locator("#mode").select_option("window")
            page.locator("#source").select_option(label=title)
            dlg=settings(proc,"录制设置")
            rate=dlg.child_window(auto_id="RecordingFps")
            assert rate.selected_text()=="60 fps"
            rate.select("30 fps");dlg.child_window(auto_id="RecordingQuality").select("高画质")
            dlg.child_window(title="保存",control_type="Button").invoke()
            page.locator("#cursor").uncheck()
            clear_audio(page)
            for device in page.evaluate("catalog.outputs.slice(0,2)"):
                page.locator("#addOutput").select_option(device["id"])
            microphone = page.evaluate("catalog.inputs.find(d=>!outputs.has(d.id))")
            if microphone:
                page.locator("#mic").select_option(microphone["id"])
            for strip in page.locator("#capturePanel .channelStrip").all():
                strip.locator("input").press("ArrowLeft")
                strip.locator(".muteChannel").click()
            mixer = page.evaluate("mixerSettings()")
            selected = list(mixer["outputs"])
            if microphone:
                selected.append(mixer["mic"])
            assert all(ch["gainDb"] == -1 and ch["muted"] for ch in selected)
            crop = pick_region(proc, page)
        with session(pw) as (proc, page):
            assert page.locator("#mode").input_value() == "region"
            assert page.evaluate("region") == crop and page.locator("#record").is_enabled()
            assert page.evaluate("mixerSettings()") == mixer
            dlg=settings(proc,"录制设置")
            assert dlg.child_window(auto_id="RecordingFps").selected_text()=="30 fps"
            assert dlg.child_window(auto_id="RecordingQuality").selected_text()=="高画质"
            dlg.child_window(title="取消",control_type="Button").invoke()
            Desktop(backend="win32").window(process=proc.pid,title="设置").wait_not("exists")
            assert not page.locator("#cursor").is_checked()
            page.locator("#mode").select_option("window")
            assert page.locator("#source option:checked").inner_text() == title
            wait_until(5,.05,lambda:json.loads(prefs.read_text(encoding="utf8"))["mode"]=="window")
        target.terminate()
        target.wait()
        with session(pw) as (proc, page):
            assert page.locator("#source").input_value() == "" and page.locator("#record").is_disabled()
            assert title in page.locator("#source option:checked").inner_text()
            target = target_window()
            page.locator("#refreshSources").click()
            wait(page, "title=>$('source').selectedOptions[0].text===title", title)
            clear_audio(page)
        with session(pw) as (proc, page):
            assert page.evaluate("mixerSettings()") == {"outputs": [], "mic": None}
            assert page.locator("#source option:checked").inner_text() == title
            # Only the synthetic window is recorded. Both audio paths are off.
            page.locator("#record").click()
            wait(page, "recording==='video'")
            time.sleep(1.4)
            page.locator("#record").click()
            wait(page, "temporary && ready", timeout=20000)
            path = url2pathname(urlsplit(page.evaluate("v.currentSrc")).path)
            streams = json.loads(subprocess.check_output([
                "ffprobe", "-v", "error", "-show_entries", "stream=codec_type,r_frame_rate", "-of", "json", path
            ]))["streams"]
            assert streams[0]["codec_type"] == "video" and streams[0]["r_frame_rate"] == "30/1", streams

        # Unplugged device and changed monitor bounds, without changing Windows hardware.
        saved = json.loads(prefs.read_text(encoding="utf8"))
        saved.update(mode="region", mic={"id":"absent-mic","name":"QC microphone","gainDb":-9,"muted":True})
        saved["outputs"] = [{"id":"absent-output","name":"QC output","gainDb":-7,"muted":False}]
        saved["crop"]["x"] = 999999
        prefs.write_text(json.dumps(saved), encoding="utf8")
        with session(pw) as (proc, page):
            assert page.locator("#capturePanel .offline").count() == 2
            assert page.locator("#mic").input_value() == "absent-mic"
            assert page.evaluate("region===null") and page.locator("#record").is_disabled()
            assert page.locator("#pickRegion").is_enabled() and page.locator("#regionSize").inner_text() == ""
            page.locator("#refreshSources").click()
            assert page.locator("#outputStrips input").input_value() == "-7"
            assert page.locator("#micStrip input").input_value() == "-9"
        assert json.loads(prefs.read_text(encoding="utf8")) == saved
        with session(pw) as (first_proc, first), session(pw) as (_, second):
            dlg=settings(first_proc,"录制设置")
            dlg.child_window(auto_id="RecordingFps").select("60 fps")
            dlg.child_window(title="保存",control_type="Button").invoke()
            second.locator("#cursor").check()
        with session(pw) as (proc, page):
            assert page.locator("#fps").count()==0 and page.locator("#cursor").is_checked()
            assert page.locator("#capturePanel .offline").count() == 2
            assert json.loads(prefs.read_text(encoding="utf8"))["fps"]==60
        print("PASS: window/region restore, device gain/mute, offline and empty selections, fresh window handle and real 30 fps recording.", flush=True)
    finally:
        if target.poll() is None:
            target.terminate()
            target.wait()
