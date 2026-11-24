"""Verify selection boundaries with real pointer and keyboard input in WebView2."""
import json
import os
import uuid
import zipfile

from playwright.sync_api import sync_playwright
from review_ui import fixtures, launch, stop, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
video = fixtures / "vfr.mp4"
note = dict(id=str(uuid.uuid4()), number=1, startUs=250000, endUs=1000000,
            text="Editable annotation", shape=dict(kind="ruler", x=.2, y=.3, toX=.7, toY=.6))
review = dict(version=1, id=str(uuid.uuid4()), fileName=video.name, durationUs=3000000,
              notes=[note], lastNoteNumber=1)
package = work / "selection.framenote"
with zipfile.ZipFile(package, "w") as archive:
    archive.writestr("review.json", json.dumps(review))
    archive.write(video, "media/source.mp4")


def drag(page, start, end):
    page.mouse.move(*start)
    page.mouse.down()
    page.mouse.move(*end, steps=8)
    page.mouse.up()


def text_start(locator):
    return locator.evaluate("""el => {
        const range = document.createRange();
        range.selectNodeContents(el.firstChild);
        const rect = range.getBoundingClientRect();
        return [rect.x + 1, rect.y + rect.height / 2];
    }""")


with sync_playwright() as pw:
    proc, page = launch(pw, package)
    try:
        page.locator("#list .stamp").click()
        heading = page.locator("#capturePanel h2").first
        details = page.locator("#editorHeading")
        drag(page, text_start(heading), text_start(details))
        assert page.evaluate("getSelection().isCollapsed"), "A drag selected read-only interface text"

        page.locator("#stage").focus()
        page.keyboard.press("Control+a")
        assert page.evaluate("getSelection().isCollapsed"), "Select All highlighted the workspace"
        for selector in ("#overlay", "#list .noteBody", "#ticks", "#rate", "#playbackVolume", "#point", "#cursor"):
            assert page.locator(selector).evaluate("el=>getComputedStyle(el).userSelect") == "none", selector

        body = page.locator("#body")
        box = body.bounding_box()
        y = box["y"] + 15
        drag(page, (box["x"] + 9, y), (box["x"] + 62, y))
        assert body.evaluate("el=>el.selectionEnd>el.selectionStart")
        body.press("Control+a")
        assert body.evaluate("el=>el.selectionStart===0 && el.selectionEnd===el.value.length")
        page.keyboard.insert_text("Revised note")
        assert body.input_value() == "Revised note"

        page.locator("#rulerLength").fill("12.34")
        for field in ("noteStart", "noteDuration", "rulerLength"):
            entry = page.locator("#" + field)
            value = entry.input_value()
            box = entry.bounding_box()
            y = box["y"] + box["height"] / 2
            drag(page, (box["x"] + 2, y), (box["x"] + box["width"] - 20, y))
            assert page.evaluate("getSelection().toString()") == value, field
            entry.press("Control+a")
            page.keyboard.insert_text(value)
            assert entry.input_value() == value, field
        print("PASS: drag and Select All outside editors, mouse selection, select-all and typing inside all four fields.")
    finally:
        stop(proc)
