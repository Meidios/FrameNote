"""Resize timeline annotation labels in the Windows review UI."""
import hashlib
import io
import json
import os
import subprocess
import uuid
import zipfile

from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import exported_file, at, fixtures, launch, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)

def note(number, start, end=None, **fields):
    return dict(id=str(uuid.uuid4()), number=number, startUs=start, endUs=end, text=f"Note {number}", **fields)

png = io.BytesIO()
Image.new("RGB", (32, 32), "#5688cc").save(png, "PNG")
picture = png.getvalue()
asset = hashlib.sha256(picture).hexdigest()
voice = work / "voice.m4a"
subprocess.run(["ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=duration=0.3", str(voice)], check=True)
audio = hashlib.sha256(voice.read_bytes()).hexdigest()
notes = [note(1, 600000), note(2, 600000, 601000, shape=dict(kind="arrow", x=.1, y=.1, toX=.4, toY=.3)),
         note(3, 1600000, pictures=[dict(asset=asset, pixelWidth=32, pixelHeight=32, x=.3, y=.2, width=.2)]),
         note(4, 2300000, 2600000, voice=dict(asset=audio, durationUs=300000))]
project = dict(version=1, id=str(uuid.uuid4()), fileName="vfr.mp4", durationUs=3000000,
               lastNoteNumber=len(notes), notes=notes)
package, saved = work / "input.framenote", work / "resized.framenote"
with zipfile.ZipFile(package, "w") as archive:
    archive.writestr("review.json", json.dumps(project))
    archive.write(fixtures / "vfr.mp4", "media/source.mp4")
    archive.writestr(f"images/{asset}.png", picture)
    archive.write(voice, f"voice/{audio}.m4a")

def drag_label(page, number, edge, delta, cancel=False):
    label = page.locator('#markLanes button[data-id="' + notes[number-1]["id"] + '"]')
    grip = label.locator('[data-edge="' + edge + '"]') if edge else label.locator(".markLabel")
    grip.scroll_into_view_if_needed()
    rect = grip.bounding_box()
    x, y = rect["x"] + rect["width"]/2, rect["y"] + rect["height"]/2
    assert page.evaluate("([x,y])=>document.elementFromPoint(x,y).dataset.edge??null", [x,y]) == edge
    width = page.locator("#markLanes").bounding_box()["width"]
    page.mouse.move(x, y)
    page.mouse.down()
    page.mouse.move(x + delta * width / project["durationUs"], y, steps=5)
    if cancel:
        page.keyboard.press("Escape")
    page.mouse.up()

with sync_playwright() as pw:
    proc, page = launch(pw, package)
    try:
        def state():
            return page.evaluate("doc.notes")

        before = state()
        assert page.locator('#markLanes [data-edge]').count() == 6
        assert page.locator('#voiceClips [data-edge]').count() == 2
        # Move a Mark, and then each edge of a whole Region, within three pixels of the playhead.
        cursor_frame=page.evaluate('frames.findIndex(t=>t>=1000000)')
        offset=round(3*project["durationUs"]/page.locator("#markLanes").bounding_box()["width"])
        for number,field in [(3,"startUs"),(4,"endUs"),(4,"startUs")]:
            at(page,cursor_frame)
            cursor=page.evaluate("shown")
            drag_label(page,number,None,cursor-before[number-1][field]+offset)
            assert state()[number-1][field]==cursor
            page.keyboard.press("Control+z")
            assert state()==before
        at(page,cursor_frame)
        cursor=page.evaluate("shown")
        page.keyboard.down("Alt")
        try:
            drag_label(page,3,None,cursor-before[2]["startUs"]+offset)
        finally:
            page.keyboard.up("Alt")
        assert 0<state()[2]["startUs"]-cursor<2*offset
        page.keyboard.press("Control+z")
        at(page,0)
        def voice_start():
            return page.locator('#voiceClips .editClip').evaluate("el=>parseFloat(el.style.left)*doc.durationUs/100")
        drag_label(page, 4, None, -1700000)
        moved_voice = state()[3]
        assert abs(moved_voice["startUs"]-600000)<5000 and moved_voice["endUs"]-moved_voice["startUs"]==300000
        assert abs(voice_start()-moved_voice["startUs"])<1 and state()[:3]==before[:3]
        assert page.evaluate("doc.timeline==null && !$('status').classList.contains('error')")
        page.keyboard.press("Control+z")
        assert state()==before and abs(voice_start()-2300000)<5,(state()==before,voice_start())
        page.keyboard.press("Control+Shift+z")
        assert state()[3]==moved_voice and abs(voice_start()-moved_voice["startUs"])<5
        drag_label(page, 4, None, 500000, cancel=True)
        assert state()[3]==moved_voice and abs(voice_start()-moved_voice["startUs"])<5
        page.keyboard.press("Control+z")
        drag_label(page, 1, "right", 450000)
        after = state()
        assert after[0]["startUs"] == 600000 and abs(after[0]["endUs"] - 1050000) < 10000
        assert after[1:] == before[1:] and page.evaluate("doc.timeline == null && history.length === 1")
        assert abs(float(page.locator("#noteDuration").input_value())-.45) < .01
        drag_label(page, 1, None, 900000)
        moved_region=state()[0]
        assert abs(moved_region["startUs"]-1500000)<10000
        assert moved_region["endUs"]-moved_region["startUs"]==after[0]["endUs"]-after[0]["startUs"]
        assert state()[1:]==before[1:] and page.evaluate("doc.timeline==null")
        page.keyboard.press("Control+z")
        assert state()==after
        page.keyboard.press("Control+z")
        assert state() == before
        page.keyboard.press("Control+Shift+z")
        assert state() == after
        drag_label(page, 1, "right", -550000)
        assert state() == before
        drag_label(page, 1, "left", -750000)
        assert (state()[0]["startUs"], state()[0]["endUs"]) == (0, 600000)
        drag_label(page, 2, "left", -300000)
        arrow = state()[1]
        assert abs(arrow["startUs"]-300000) < 10000 and arrow["endUs"] == 601000
        assert arrow["shape"] == before[1]["shape"]
        snapshot = state()
        drag_label(page, 2, "right", 300000, cancel=True)
        assert state() == snapshot
        drag_label(page, 3, None, 200000)
        point = state()[2]
        assert point["endUs"] is None and abs(point["startUs"]-1800000) < 10000
        clips = page.evaluate("doc.timeline.clips")
        assert page.evaluate("doc.timeline.linked")
        page.locator('#linkTracks').click()
        drag_label(page, 4, None, -1200000)
        voice_after = state()[3]
        delta = voice_after["startUs"]-before[3]["startUs"]
        assert abs(delta+1200000)<10000 and voice_after["endUs"]==before[3]["endUs"]+delta
        clips = [dict(c,startUs=c["startUs"]+delta) if c.get("noteId")==voice_after["id"] else c for c in clips]
        assert page.evaluate("doc.timeline.clips")==clips
        drag_label(page, 3, "right", 1500000)
        assert state()[2]["endUs"] == project["durationUs"] and state()[2]["pictures"] == point["pictures"]
        assert state()[3] == voice_after and page.evaluate("doc.timeline.clips") == clips
        ruler = page.locator("#ruler").bounding_box()
        page.mouse.move(ruler["x"]+ruler["width"]/2, ruler["y"]+8)
        page.keyboard.down("Control")
        page.mouse.wheel(0, -480)
        page.keyboard.up("Control")
        wait(page, "$('timeline').style.width === '800%'")
        drag_label(page, 3, "left", 50000)
        assert abs(state()[2]["startUs"] - point["startUs"] - 50000) < 2000
        expected = state()
        page.locator("#save").click()
        saved=exported_file(proc)
    finally:
        stop(proc)
    proc, page = launch(pw, saved)
    try:
        assert page.evaluate("doc.notes") == expected
        restored = page.evaluate("doc.timeline.clips")
        assert restored == [dict({"asset": None, "sourceDurationUs": None, "noteId": None}, **c) for c in clips]
    finally:
        stop(proc)
print("PASS: point and short-label grips, collapse, bounds, move, cancel, undo, redo, linked clips, voice exclusion and package reload.")
