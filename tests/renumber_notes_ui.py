"""Renumber same-time and out-of-order notes without moving or recoloring them."""
import hashlib
import json
import os
import subprocess
import time
import uuid
import zipfile
from xml.etree import ElementTree as ET

from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import fixtures, launch, stop, wait, work

os.environ["FRAMENOTE_DATA_DIR"] = str(work / "profile")
uid = lambda: str(uuid.uuid4())
notes = [dict(id=uid(), number=num, startUs=start, endUs=end, text=f"Body {num}", shape=None)
         for num, start, end in [(2, 900000, 1200000), (5, 0, 850000), (9, 1800000, None), (4, 0, 500000)]]
notes[1]["color"] = "#c43257"
notes[3]["shape"] = dict(kind="box", x=.2, y=.1, toX=.7, toY=.4)
voice = work / "voice.m4a"
subprocess.run(["ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=450:duration=0.3", str(voice)], check=True)
voice_id = hashlib.sha256(voice.read_bytes()).hexdigest()
notes[0]["voice"] = dict(asset=voice_id, durationUs=300000)
picture = work / "picture.png"
Image.new("RGB", (24, 16), "#acaed1").save(picture)
picture_id = hashlib.sha256(picture.read_bytes()).hexdigest()
notes[1]["pictures"] = [dict(asset=picture_id, pixelWidth=24, pixelHeight=16, x=.1, y=.5, width=.3)]
clips = [dict(id=uid(), track=track, sourceUs=0, startUs=0, durationUs=3000000) for track in ("video", "audio")]
clips.append(dict(id=uid(), track="voice", sourceUs=0, startUs=900000, durationUs=300000,
                  asset=voice_id, noteId=notes[0]["id"], sourceDurationUs=300000))
doc = dict(id=uid(), version=3, durationUs=3000000, timeline=dict(linked=True, sourceDurationUs=3000000, clips=clips),
           fileName="source.mp4", notes=notes, lastNoteNumber=11)
package = work / "input.framenote"
with zipfile.ZipFile(package, "w") as z:
    z.writestr("review.json", json.dumps(doc))
    z.write(fixtures / "vfr.mp4", "media/source.mp4")
    z.write(picture, f"images/{picture_id}.png")
    z.write(voice, f"voice/{voice_id}.m4a")


def choose_path(proc, path):
    picker = Desktop(backend="win32").window(process=proc.pid, class_name="#32770")
    picker.wait("ready", timeout=10)
    time.sleep(.2)
    picker.child_window(class_name="Edit", found_index=0).set_edit_text(str(path))
    picker.child_window(control_id=1).click()
    time.sleep(.35)
    if picker.exists(timeout=.2) and picker.is_visible():
        picker.child_window(control_id=1).click()


with sync_playwright() as pw:
    proc, page = launch(pw, package)
    try:
        button = page.get_by_role("button", name="重新排序", exact=True)
        assert button.is_enabled()
        rows = page.locator("#list .noteNumber")
        assert rows.all_text_contents() == ["#4", "#5", "#2", "#9"]
        page.locator("#list .stamp").first.click()
        page.locator("#body").focus()
        before = page.evaluate("doc")
        cursor = page.evaluate("[selected,shown,transport.currentTime]")
        colored = "doc.notes.map(n=>({...n,color:noteColor(n)}))"
        expected = [dict(note, number=n) for note,n in zip(page.evaluate(colored), [3, 2, 4, 1])]
        button.click()
        assert button.is_disabled()
        assert rows.all_text_contents() == ["#1", "#2", "#3", "#4"]
        assert page.locator("#markLanes .markLabel").all_text_contents() == rows.all_text_contents()
        assert sorted(page.locator(".inkBadge text").all_text_contents()) == ["#1", "#2"]
        assert page.evaluate("[selected,shown,transport.currentTime]") == cursor
        assert page.evaluate(colored) == expected
        assert page.evaluate("doc.timeline") == before["timeline"]
        assert page.evaluate("editing.number") == 1
        page.keyboard.press("Control+z")
        assert page.evaluate("doc.notes") == before["notes"]
        assert page.evaluate("doc.timeline") == before["timeline"]
        assert page.evaluate("selected") == cursor[0]
        page.keyboard.press("Control+Shift+z")
        assert page.evaluate(colored) == expected
        page.locator("#reviewPanel .sideTitle").screenshot(path=str(work / "renumber-header.png"))
        saved = work / "renumbered.framenote"
        page.locator("#save").click()
        choose_path(proc, saved)
        wait(page, "!busy && $('status').textContent.startsWith('审阅包已保存')")
        with zipfile.ZipFile(saved) as z:
            restored = json.loads(z.read("review.json"))
            assert restored["notes"] == page.evaluate("doc.notes") and restored["timeline"] == before["timeline"]
            assert z.read(f"voice/{voice_id}.m4a") == voice.read_bytes()
            assert z.read(f"images/{picture_id}.png") == picture.read_bytes()
        page.locator("#exportExcel").click()
        choose_path(proc, work)
        wait(page, "!busy && $('status').textContent.startsWith('Excel 批注已导出')", timeout=60000)
        with zipfile.ZipFile(next(work.rglob("*.xlsx"))) as z:
            sheet = ET.fromstring(z.read("xl/worksheets/sheet1.xml"))
            for i in range(1, 5):
                assert sheet.find(f".//{{*}}c[@r='A{i+2}']/{{*}}v").text == str(i)
    finally:
        stop(proc)

    proc, page = launch(pw, picture, "typeof imageDoc==='function' && imageDoc()")
    try:
        button = page.get_by_role("button", name="重新排序", exact=True)
        assert button.is_disabled()
        for _ in range(2):
            page.locator("#point").click()
            page.keyboard.press("Control+Enter")
        assert button.is_disabled()
        page.locator("#list .noteDelete").first.click()
        rows = page.locator("#list .noteNumber")
        assert rows.all_text_contents() == ["#2"]
        button.click()
        assert rows.all_text_contents() == ["#1"] and button.is_disabled()
        page.keyboard.press("Control+z")
        assert rows.all_text_contents() == ["#2"] and button.is_enabled()
    finally:
        stop(proc)
print("PASS: video and image numbering, stable ties and colors, selection, undo/redo, package and Excel.", work)
