"""Verify that the editor, settings and standalone Player share a saved UI language."""
import json
import os
import subprocess
import time
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import exported_file, app, desk, fixtures, launch, stop, switch_file, wait, work


def preferences(proc, title):
    desk(proc).child_window(auto_id="SettingsGear").invoke()
    window = Desktop(backend="win32").window(process=proc.pid, title=title)
    window.wait("ready", timeout=15)
    dlg = Desktop(backend="uia").window(handle=window.handle)
    return dlg


def select_language(proc, title, original, new, action):
    dlg = preferences(proc, title)
    combo = dlg.child_window(auto_id="InterfaceLanguage", control_type="ComboBox")
    assert combo.selected_text() == original
    combo.select(new)
    dlg.child_window(title=action, control_type="Button").invoke()
    Desktop(backend="win32").window(process=proc.pid, title=title).wait_not("exists")


profile = work / "profile"
profile.mkdir()
prefs = profile / "updates.json"
prefs.write_text('{"automatic":false,"language":"zh-CN"}')
(profile / "shortcuts.json").write_text('{"record":[]}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
sample = work / "视频捕获.png"
Image.new("RGB", (240, 180), "grey").save(sample)

with sync_playwright() as pw:
    proc, page = launch(pw, None, "typeof render==='function'")
    try:
        assert page.locator("#sourceTitle").inner_text() == "视频捕获"
        select_language(proc, "设置", "简体中文", "English", "取消")
        assert json.loads(prefs.read_text())["language"] == "zh-CN"
        select_language(proc, "设置", "简体中文", "English", "保存")
        assert json.loads(prefs.read_text())["language"] == "en"
    finally:
        stop(proc)

    proc, page = launch(pw, None, "typeof render==='function'")
    try:
        assert page.locator("html").get_attribute("lang") == "en"
        assert page.locator("#sourceTitle").inner_text() == "Video capture"
        assert page.locator("#copyGif").text_content() == "Copy GIF to clipboard"
        assert page.locator("#body").get_attribute("placeholder") == "Describe what needs adjusting…"
        assert page.locator("#addOutput").get_attribute("aria-label") == "Add output device"
        dlg = preferences(proc, "Settings")
        assert [item.window_text() for item in dlg.descendants(control_type="TabItem")] == [
            "General", "Recording settings", "Spreadsheet", "Shortcuts", "User guide", "Updates", "Feedback", "About"]
        dlg.child_window(title="Spreadsheet", control_type="TabItem").select()
        assert dlg.child_window(auto_id="ExcelColumnPreview").window_text() == "Preview"
        assert dlg.child_window(auto_id="ExcelZip").window_text() == "Export as a ZIP archive"
        dlg.child_window(title="Shortcuts", control_type="TabItem").select()
        assert dlg.child_window(auto_id="point").window_text() == "Add point annotation"
        dlg.child_window(title="User guide", control_type="TabItem").select()
        guide = dlg.child_window(auto_id="GuideScroll")
        assert guide.child_window(auto_id="GuideOverviewTitle").window_text() == "Pin feedback to the exact frame"
        assert guide.child_window(title="Open media", control_type="Text").is_visible()
        texts = [item.window_text() for item in guide.descendants(control_type="Text")]
        assert any(text.startswith("Language, updates and feedback") for text in texts)
        assert not any("\u3400" <= char <= "\u9fff" for text in texts for char in text)
        native = Desktop(backend="win32").window(handle=dlg.handle)
        original = native.rectangle()
        native.move_window(width=590, height=400)
        scroll = guide.wrapper_object().iface_scroll
        assert scroll.CurrentVerticallyScrollable and not scroll.CurrentHorizontallyScrollable
        scroll.SetScrollPercent(-1, 100)
        last = guide.child_window(title_re=r"^Language, updates and feedback.*", control_type="Text").rectangle()
        assert guide.rectangle().top <= last.top < guide.rectangle().bottom
        dlg.capture_as_image().save(work / "settings-guide-en-compact.png")
        native.move_window(width=original.width(), height=original.height())
        dlg.child_window(title="Updates", control_type="TabItem").select()
        versions = dlg.child_window(auto_id="ReleaseVersions", control_type="ComboBox")
        body = dlg.child_window(auto_id="ReleaseText")
        for note in (app.parent / "releases").glob("v*.md"):
            versions.select(note.stem)
            text = " ".join(item.window_text() for item in body.descendants(control_type="Text"))
            assert text and not any("\u3400" <= ch <= "\u9fff" for ch in text), note.stem
            assert not any(markup in text for markup in ("# FrameNote ", "**", "`", "](")), note.stem
        dlg.child_window(title="Cancel", control_type="Button").invoke()
        switch_file(proc, page, sample)
        page.locator("#point").click()
        page.locator("#body").fill("视频捕获")
        page.keyboard.press("Control+Enter")
        assert page.evaluate("doc.notes[0].text") == "视频捕获"
        assert page.locator("#list").get_by_text("视频捕获", exact=True).is_visible()
    finally:
        stop(proc)

    proc, page = launch(pw, fixtures / "vfr.mp4")
    try:
        page.locator("#stage").click(button="right")
        assert page.locator("#menuCreate").get_by_text("Distance ruler", exact=True).is_visible()
        page.keyboard.press("Escape")
        page.locator("#point").click()
        page.locator("#body").fill("视频捕获")
        page.keyboard.press("Control+Enter")
        page.locator("#exportExcel").click()
        book=exported_file(proc)
        with zipfile.ZipFile(book) as archive:
            strings = ET.fromstring(archive.read("xl/sharedStrings.xml"))
            cells = list(strings.itertext())
            assert "视频捕获" in cells and "Annotation" in cells and "Start time" in cells
    finally:
        stop(proc)

    package = Path(__file__).resolve().parents[1] / "examples" / "SampleReview.framenote"
    with zipfile.ZipFile(package) as archive:
        original_notes = json.loads(archive.read("review.json"))["notes"]
    proc, page = launch(pw, package)
    try:
        assert page.evaluate("doc.notes.map(note=>note.text)") == [note["text"] for note in original_notes]
    finally:
        stop(proc)

proc = subprocess.Popen([str(app.with_name("FrameNote Player.exe"))])
try:
    win = desk(proc)
    assert win.child_window(auto_id="ReviewButton").window_text() == "Annotate"
    select_language(proc, "Settings", "English", "简体中文", "Save")
    assert json.loads(prefs.read_text())["language"] == "zh-CN"
finally:
    stop(proc)
print("PASS: bilingual editor, image viewer, settings and Player; save, cancel and user text.")
