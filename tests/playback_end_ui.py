"""Check the playback clock against actual track ends in the release WebView2 desk."""
import json
import hashlib
import subprocess
import uuid
import zipfile

from playwright.sync_api import sync_playwright
from review_ui import launch, stop, wait, work


def clip(track, start, end):
    return dict(id=str(uuid.uuid4()), track=track, startUs=start, sourceUs=0, durationUs=end-start)


def package(name, clips, notes=(), source=None):
    source = source or video
    review = dict(version=3, id=str(uuid.uuid4()), fileName=source.name, durationUs=30_000_000,
                  notes=notes, lastNoteNumber=len(notes),
                  timeline=dict(sourceDurationUs=30_000_000, linked=False, clips=clips))
    path = work/(name+".framenote")
    with zipfile.ZipFile(path, "w") as z:
        z.writestr("review.json", json.dumps(review))
        z.write(source, "media/source.mp4")
        if any(c["track"]=="voice" for c in clips):
            z.write(voice_file,"voice/"+voice_asset+".m4a")
    return path


def check_stop(page, end):
    page.evaluate("t=>{transport.pause();transport.currentTime=Math.max(0,t/1e6-.35)}", end)
    page.locator("#play").click()
    wait(page, "t=>transport.paused && transport.ended && shown===t", end)
    page.wait_for_timeout(180)
    assert page.evaluate("t=>shown===t && $('clock').textContent===fmt(t) && $('duration').textContent===fmt(t)", end)


video = work/"30s.mp4"
silent = work/"silent.mp4"
subprocess.run(["ffmpeg","-v","error","-f","lavfi","-i","testsrc2=s=240x144:r=25:d=30",
                "-f","lavfi","-i","sine=d=30","-c:v","libx264","-preset","ultrafast","-c:a","aac",video],check=True)
subprocess.run(["ffmpeg","-v","error","-i",video,"-an","-c:v","copy",silent],check=True)
voice_file=work/"tail.m4a"
subprocess.run(["ffmpeg","-v","error","-i",video,"-vn","-t","1","-c:a","aac",voice_file],check=True)
voice_asset=hashlib.sha256(voice_file.read_bytes()).hexdigest()
with sync_playwright() as pw:
    proc,p = launch(pw,video)
    try:
        p.evaluate("seekUs(10000000)")
        wait(p,"shown===10000000 && !v.seeking")
        p.locator("#splitClip").click()
        p.locator("#videoClips .editClip").last.click()
        p.locator("#deleteClip").click()
        check_stop(p,10_000_000)
        assert p.evaluate("v.style.visibility==='visible' && v.currentTime>=9.96 && v.currentTime<10")
        p.locator("#play").click()
        wait(p,"!transport.paused && shown>0 && shown<1000000")
        p.evaluate("transport.pause()")
        p.keyboard.press("Control+z")
        check_stop(p,30_000_000)
        p.keyboard.press("Control+Shift+z")
        check_stop(p,10_000_000)
        print("30s source cut to 10s, last frame, restart, undo and redo passed",flush=True)
    finally:
        stop(proc)
    mark = dict(id=str(uuid.uuid4()),number=1,startUs=13_000_000,endUs=None,text="tail",shape=None)
    speech=dict(mark,startUs=1_000_000,endUs=2_000_000,voice=dict(asset=voice_asset,durationUs=1_000_000))
    voice_clip=dict(clip("voice",15_000_000,16_000_000),noteId=mark["id"],asset=voice_asset,sourceDurationUs=1_000_000)
    for name, clips, notes, end, source in [
        ("gap-and-audio", [clip("video",0,10_000_000),clip("audio",12_000_000,14_000_000)], [],14_000_000,video),
        ("region", [clip("video",0,10_000_000)], [dict(mark,endUs=14_500_000)],14_500_000,video),
        ("voice-tail", [clip("video",0,10_000_000),voice_clip], [speech],16_000_000,video),
        ("mark", [clip("video",0,10_000_000)], [mark],13_000_000,video),
        ("empty", [], [],0,video),
        ("silent-source", [clip("video",0,10_000_000),clip("audio",0,30_000_000)], [],10_000_000,silent)
    ]:
        proc,p=launch(pw,package(name,clips,notes,source))
        try:
            if name=="gap-and-audio":
                p.evaluate("transport.currentTime=11.85;transport.play()")
                wait(p,"shown>12100000 && !transport.paused")
            check_stop(p,end)
            print(name, end, "us passed",flush=True)
        finally:
            stop(proc)
