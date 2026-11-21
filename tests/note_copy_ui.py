"""Verify card-local plain-text copy in the Windows review UI using a test clipboard."""
import os
import time
import win32clipboard as cb
from PIL import Image
from playwright.sync_api import sync_playwright
from review_ui import fixtures, launch, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
body = "检查音效 <b>对白</b>\n参考 https://example.org/cue?id=12\n  保留缩进"
picture = work / "frame.png"
Image.new("RGB", (240, 135), "#345678").save(picture)

def verify_clipboard():
    for attempt in range(20):
        try:
            cb.OpenClipboard()
            break
        except cb.error:
            if attempt == 19:
                raise
            time.sleep(.05)
    try:
        assert cb.GetClipboardData(cb.CF_UNICODETEXT).replace("\r\n", "\n") == body
        for name in ("HTML Format", "Rich Text Format"):
            assert not cb.IsClipboardFormatAvailable(cb.RegisterClipboardFormat(name))
    finally:
        cb.CloseClipboard()

with sync_playwright() as pw:
    for path, condition in [(fixtures / "vfr.mp4", "typeof ready!=='undefined' && ready"),
                            (picture, "typeof imageDoc==='function' && imageDoc()")]:
        proc, page = launch(pw, path, condition)
        try:
            page.locator("#point").click()
            page.locator("#body").fill(body)
            page.keyboard.press("Control+Enter")
            page.locator("#point").click()
            rows = page.locator("#list .note")
            copy = rows.first.get_by_role("button", name="复制批注 #1 的文字", exact=True)
            assert rows.last.locator(".noteCopy").is_disabled() and copy.is_enabled()
            assert rows.first.locator(".noteBody a").count() == 1
            assert rows.first.locator(".noteDelete").bounding_box()["y"] < copy.bounding_box()["y"]
            assert copy.locator("svg use").evaluate("el=>el.getBBox().width>0")
            before = page.evaluate("[selected,shown,doc.notes]")
            previous = cb.GetClipboardSequenceNumber()
            if path == picture:
                copy.focus()
                page.keyboard.press("Space")
            else:
                copy.click()
            wait(page, "$('status').textContent==='已复制批注 #1 的文字'")
            assert cb.GetClipboardSequenceNumber() != previous
            verify_clipboard()
            assert page.evaluate("[selected,shown,doc.notes]") == before
            if path != picture:
                page.mouse.move(1, 1)
                rows.first.screenshot(path=str(work / "copy-note.png"))
        finally:
            stop(proc)
print("PASS: video and image notes, clipboard Unicode text, line breaks, URLs, empty notes and unchanged selection.", work)
