"""Check the durations of the actual video and audio streams exported by the Windows desk."""
import hashlib
import json
import os
import subprocess
import time
import uuid
import zipfile

from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import launch, stop, wait, work

os.environ["FRAMENOTE_DATA_DIR"] = str(work / "profile")
source = work / "source.mp4"
silent = work / "silent.mp4"
voice = work / "voice.m4a"
source_us = 431720000
cut_us = 157400000
subprocess.run(["ffmpeg", "-v", "error", "-f", "lavfi", "-i", f"color=c=teal:s=160x90:r=25:d={source_us/1e6}",
                "-f", "lavfi", "-i", f"sine=frequency=440:duration={source_us/1e6}", "-c:v", "libx264", "-c:a", "aac",
                "-shortest", str(source)], check=True)
subprocess.run(["ffmpeg", "-v", "error", "-i", str(source), "-an", "-c:v", "copy", str(silent)], check=True)
subprocess.run(["ffmpeg", "-v", "error", "-i", str(source), "-vn", "-t", "1", "-c:a", "aac", str(voice)], check=True)
asset = hashlib.sha256(voice.read_bytes()).hexdigest()


def clip(track, start, length, **extra):
    return dict(id=str(uuid.uuid4()), track=track, startUs=start, durationUs=length, sourceUs=0, **extra)


def package(name, clips, notes=(), media=source):
    path = work / (name + ".framenote")
    review = dict(id=str(uuid.uuid4()), version=3, durationUs=source_us, fileName=media.name, notes=notes,
                  lastNoteNumber=len(notes), timeline=dict(sourceDurationUs=source_us, linked=False, clips=clips))
    with zipfile.ZipFile(path, "w") as z:
        z.writestr("review.json", json.dumps(review))
        z.write(media, "media/source.mp4")
        if any(c["track"] == "voice" for c in clips):
            z.write(voice, f"voice/{asset}.m4a")
    return path


def export(proc, page, name, expected):
    output = work / (name + ".mp4")
    page.locator("#exportVideo").click()
    picker = Desktop(backend="win32").window(process=proc.pid, class_name="#32770")
    picker.wait("ready", timeout=10)
    time.sleep(.2)
    picker.child_window(class_name="Edit", found_index=0).set_edit_text(str(output))
    picker.child_window(control_id=1).click()
    wait(page, "name=>!busy && $('status').textContent.endsWith(name)", output.name, timeout=60000)
    data = json.loads(subprocess.check_output(["ffprobe", "-v", "error", "-show_entries",
                      "format=duration:stream=codec_type,duration", "-of", "json", str(output)]))
    lengths = {s["codec_type"]: float(s["duration"]) for s in data["streams"]}
    lengths["container"] = float(data["format"]["duration"])
    assert lengths.keys() == {"video", "audio", "container"}
    print(name, lengths, "expected", expected, flush=True)
    assert all(abs(v - expected) < .041 for v in lengths.values()), (name, lengths, expected)


with sync_playwright() as pw:
    proc, page = launch(pw, source)
    try:
        page.evaluate("t=>seekUs(t)", cut_us)
        wait(page, "t=>!v.seeking && shown === t", cut_us)
        page.locator("#splitClip").click()
        page.locator("#videoClips .editClip").last.click()
        page.locator("#deleteClip").click()
        cut = page.evaluate("Math.max(...doc.timeline.clips.map(EditModel.end))/1e6")
        assert abs(cut-cut_us/1e6) < .041 and page.evaluate("doc.durationUs") == source_us
        export(proc, page, "linked-cut", cut)
        page.keyboard.press("Control+z")
        page.keyboard.press("Control+z")
        export(proc, page, "full-length", source_us/1e6)
    finally:
        stop(proc)

    video = clip("video", 0, 4000000)
    mark = dict(id=str(uuid.uuid4()), number=1, startUs=3800000, endUs=None, text="Tail Mark", shape=None)
    later = dict(mark, startUs=6000000)
    region = dict(later, endUs=7500000, shape=dict(kind="magnifier", x=.2, y=.2, toX=.5, toY=.6))
    speech = clip("voice", 8000000, 1000000, asset=asset, sourceDurationUs=1000000, noteId=mark["id"])
    spoken_note = dict(mark, startUs=1000000, endUs=2000000, voice=dict(asset=asset, durationUs=1000000))
    for path, expected in [
        (package("silent-trim", [video, clip("audio", 0, source_us)], [mark], silent), 4),
        (package("detached-audio", [video, clip("audio", 6000000, 2000000)]), 8),
        (package("voice-tail", [video, speech], [spoken_note]), 9),
        (package("region-tail", [video], [region]), 7.5),
        (package("mark-tail", [video], [later]), 7),
    ]:
        proc, page = launch(pw, path)
        try:
            export(proc, page, path.stem, expected)
        finally:
            stop(proc)
    proc, page = launch(pw, package("empty", []))
    try:
        page.locator("#exportVideo").click()
        wait(page, "$('status').textContent === '时间线上没有可导出的片段或批注。'")
    finally:
        stop(proc)
print("PASS: edited MP4 ends at its actual content across tracks, annotations and render modes.", work)
