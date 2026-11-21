"""Open videos from the welcome screen using the Windows file picker."""
import os
import shutil
import subprocess
from pathlib import Path

import psutil
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from pywinauto.timings import wait_until

from review_ui import app, fixtures, launch, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
mp4 = work / "1 direct.mp4"
mkv = work / "2 preview.mkv"
bad = work / "unsupported audio.mkv"
shutil.copyfile(fixtures / "vfr.mp4", mp4)
subprocess.run(["ffmpeg", "-v", "error", "-i", str(mp4), "-c:v", "ffv1", "-fps_mode",
                "passthrough", "-c:a", "pcm_s16le", str(mkv)], check=True)
# This track can be inspected, but its channel layout cannot be encoded as AAC.
subprocess.run(["ffmpeg", "-v", "error", "-i", str(mp4), "-f", "lavfi", "-i",
                "anullsrc=cl=22.2:r=48000", "-map", "0:v", "-map", "1:a", "-c:v",
                "copy", "-c:a", "pcm_s16le", "-shortest", str(bad)], check=True)


def pick(proc, page, *paths):
    page.locator("#start").click()
    select_files(proc, *paths)


def select_files(proc, *paths):
    dialog = Desktop(backend="win32").window(process=proc.pid, class_name="#32770")
    dialog.wait("ready", timeout=10)
    filename = dialog.child_window(class_name="Edit", found_index=0)
    value = str(paths[0])
    if len(paths) > 1:
        filename.set_edit_text(str(paths[0].parent) + os.sep)
        dialog.child_window(control_id=1).click()
        value = " ".join(f'"{p.name}"' for p in paths)
    filename.set_edit_text(value)
    dialog.child_window(control_id=1).click()


def assert_no_player(proc):
    assert not any(Path(m.path).name.lower() == "libvlccore.dll"
                   for m in psutil.Process(proc.pid).memory_maps())


def assert_welcome_toolbar(page):
    buttons = page.locator("#timelineBar").get_by_role("button")
    assert buttons.evaluate_all("els => els.map(el => el.id)") == [
        "point", "arrow", "box", "line", "angle", "toolRuler", "magnifier", "text",
        "voiceRecord", "addPicture", "removePicture", "clear", "linkTracks", "splitClip", "joinClip", "joinNextClip", "deleteClip",
    ]
    assert buttons.evaluate_all("els => els.every(el => el.matches(':disabled'))")
    assert page.locator("#linkTracks").evaluate("el => getComputedStyle(el).color === getComputedStyle($('point')).color")


def annotate(page, text):
    page.locator("#point").click()
    page.locator("#body").fill(text)
    page.keyboard.press("Control+Enter")
    wait(page, "text=>doc.notes.some(n=>n.text===text)", text)


def check_player_loading(path):
    proc = subprocess.Popen([str(app.with_name("FrameNote Player.exe"))])
    try:
        root = Desktop(backend="win32").window(process=proc.pid, title="FrameNote Player")
        root.wait("ready", timeout=15)
        window = Desktop(backend="uia").window(handle=root.handle)
        control = lambda name: window.child_window(auto_id=name)
        assert control("OpenButton").window_text() == "打开文件"
        control("OpenButton").invoke()
        select_files(proc, path)
        review = control("ReviewButton")
        review.wait("enabled", timeout=15)
        review.invoke()
        bar = control("PreviewBar")
        bar.wait("visible", timeout=12)
        wait_until(15, .05, lambda: 0 < bar.iface_range_value.CurrentValue < 100)
        assert control("LoadingTitle").window_text() == "正在生成批注预览"
        assert 0 < int(control("PreviewPercent").window_text().rstrip("%")) < 100
        cancel = control("CancelButton")
        assert cancel.window_text() == "取消加载"
        button, track, bounds = cancel.rectangle(), bar.rectangle(), window.rectangle()
        assert button.top > track.bottom
        assert abs(button.mid_point().x - track.mid_point().x) <= 2
        assert abs(track.mid_point().x - bounds.mid_point().x) <= 5
        assert abs(track.mid_point().y - bounds.mid_point().y) < bounds.height() * .08
        window.wrapper_object().capture_as_image().save(work / "player-preview.png")
        cancel.invoke()
        wait_until(8, .1, lambda: "已取消" in control("Hint").window_text())
        bar.wait_not("visible", timeout=5)
        assert review.is_enabled()
        clock = control("Clock")
        paused = clock.window_text()
        control("Toggle").invoke()
        wait_until(6, .1, lambda: clock.window_text() != paused)
        review.invoke()
        wait_until(45, .15, lambda: window.window_text() == path.name + " · FrameNote")
        assert window.child_window(title="播放倍速").exists(timeout=10)
    finally:
        stop(proc)


with sync_playwright() as pw:
    proc, page = launch(pw, None, "typeof ready!=='undefined' && !$('welcome').hidden")
    try:
        long = work / "long preview.mkv"
        subprocess.run(["ffmpeg", "-v", "error", "-stream_loop", "119", "-i", str(mp4),
                        "-c", "copy", str(long)], check=True)
        assert page.locator("#start").inner_text() == "打开文件"
        assert_welcome_toolbar(page)
        page.locator("#timelineBar").screenshot(path=str(work / "welcome-toolbar.png"))
        assert page.locator("#open").get_attribute("aria-description") == page.evaluate("shortcutHint('打开文件','open')")
        page.evaluate("""window.previewSamples = [];
            chrome.webview.addEventListener('message', ({data}) => {
                if (data.type === 'preparingPreview' && data.active)
                    window.previewSamples.push($('progressBar').value);
            })""")
        pick(proc, page, long)
        wait(page, "$('taskProgress').open && $('progressBar').value > 0 && $('progressBar').value < 99", timeout=12000)
        assert page.evaluate("""() => {
            const card = $('taskProgress').getBoundingClientRect(), bar = $('progressBar').getBoundingClientRect();
            const cancel = $('cancelProgress').getBoundingClientRect();
            return Math.abs(card.left + card.width / 2 - innerWidth / 2) <= 1
                && Math.abs(card.top + card.height / 2 - innerHeight / 2) <= 1
                && cancel.top > bar.bottom && Math.abs(cancel.left + cancel.width / 2 - innerWidth / 2) <= 1
                && $('progressPercent').textContent === $('progressBar').value + '%'
                && $('cancelProgress').textContent === '取消加载';
        }""")
        page.screenshot(path=str(work / "welcome-preview.png"))
        page.locator("#cancelProgress").click(timeout=10000)
        wait(page, "!busy && $('status').textContent.includes('已取消')")
        assert_welcome_toolbar(page)
        samples = page.evaluate("window.previewSamples")
        assert samples == sorted(samples) and samples[0] == 0 and samples[-1] < 100
        pick(proc, page, bad)
        wait(page, "!busy && $('status').classList.contains('error')", timeout=20000)
        assert page.locator("#start").is_visible() and page.locator("#cancelProgress").is_hidden()
        assert_welcome_toolbar(page)
        assert_no_player(proc)
        page.evaluate("window.previewSamples.length = 0")
        pick(proc, page, mkv)
        wait(page, "ready && !busy && reviewProxy", timeout=20000)
        samples = page.evaluate("window.previewSamples")
        assert samples == sorted(samples) and samples[0] == 0 and samples[-1] == 100
        assert page.locator("#taskProgress").is_hidden()
        for button in ["point", "arrow", "addPicture", "linkTracks", "splitClip"]:
            assert page.locator("#" + button).is_enabled()
        assert page.locator("#deleteClip").is_disabled()
        annotate(page, "MKV welcome")
        assert_no_player(proc)
    finally:
        stop(proc)

    proc, page = launch(pw, None, "typeof ready!=='undefined' && !$('welcome').hidden")
    try:
        page.evaluate("""window.openedPreview = false;
            chrome.webview.addEventListener('message', ({data}) => {
                if (data.type === 'preparingPreview' && data.active) window.openedPreview = true;
            })""")
        pick(proc, page, mp4, mkv, bad)
        wait(page, "name=>ready && !busy && !reviewProxy && doc.fileName===name", mp4.name)
        assert not page.evaluate("window.openedPreview")
        annotate(page, "MP4 welcome")
        # Exercise the real WebView media-error handler after the codec probe succeeded.
        page.evaluate("v.src = new URL('missing.mp4', v.src).href")
        wait(page, "ready && !busy && reviewProxy", timeout=20000)
        assert page.evaluate("doc.notes[0].text") == "MP4 welcome"
        handle = Desktop(backend="win32").window(process=proc.pid, title_re=".*FrameNote$").handle
        window = Desktop(backend="uia").window(handle=handle)
        items = window.child_window(auto_id="Items", control_type="List")
        rows = items.children(control_type="ListItem")
        assert len(rows) == 3, [row.texts() for row in rows]
        rows[1].select()
        wait(page, "name=>ready && !busy && reviewProxy && doc.fileName===name", mkv.name)
        annotate(page, "second video")
        before = page.evaluate("playbackId")
        rows[2].select()
        wait(page, "!busy && $('status').classList.contains('error')", timeout=20000)
        assert page.evaluate("playbackId") == before and page.evaluate("ready")
        assert rows[1].is_selected() and window.window_text().startswith(mkv.name)
        annotate(page, "after failed switch")
        rows[0].select()
        wait(page, "name=>ready && !busy && doc.fileName===name", mp4.name)
        rows[1].select()
        wait(page, "name=>ready && !busy && doc.fileName===name", mkv.name)
        assert page.evaluate("doc.notes.some(n=>n.text==='after failed switch')")
        assert_no_player(proc)
    finally:
        stop(proc)

check_player_loading(long)
print("PASS: centered preview progress, cancel and retry in FrameNote and Player, welcome review, decoder fallback and playlist.")
