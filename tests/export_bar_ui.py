"""Verify the burned-in strip through actual MP4 exports and native settings."""
import io
import json
import os
import statistics
import subprocess
import tempfile
import time
import uuid
import zipfile
from pathlib import Path

from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import exported_file, launch, settings, stop, work


profile = Path(tempfile.mkdtemp(prefix="framenote-bar-settings-"))
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
(profile / "updates.json").write_text('{"automatic":false,"language":"zh-CN"}')
(profile / "capture.json").write_text(json.dumps({"exportDirectory":str(work/"exports")}))
source = work / "source.mp4"
width, height, duration = 1280, 720, 8
subprocess.run(["ffmpeg", "-v", "error", "-f", "lavfi", "-i",
                "testsrc2=size=1280x720:rate=30:duration=10,hue=s=0",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=10",
                "-c:v", "libx264", "-preset", "veryfast", "-c:a", "aac", str(source)], check=True)


def make_review(name, lens=False):
    def note(n, start, end, color):
        return dict(id=str(uuid.uuid4()), number=n, startUs=int(start*1e6), endUs=None if end is None else int(end*1e6),
                    text=f"Note {n}", color=color)
    notes = [note(1, 2, 4, "#FF8000"), note(2, 6, None, "#30C0FF")]
    if lens:
        notes[0]["shape"] = dict(kind="magnifier", x=.2, y=.2, toX=.4, toY=.4)
        notes.append(note(3, 6, None, "#FF3030"))
        notes.append(note(4, 3, 3.75, "#AC58F4"))
    clips = [dict(id=str(uuid.uuid4()), track=track, startUs=0, sourceUs=1000000, durationUs=duration*1000000)
             for track in ("video","audio")]
    data = dict(version=3, id=str(uuid.uuid4()), fileName=source.name, durationUs=10000000, notes=notes,
                lastNoteNumber=len(notes), timeline=dict(sourceDurationUs=10000000, linked=True, clips=clips))
    path = work / (name+".framenote")
    with zipfile.ZipFile(path,"w") as z:
        z.writestr("review.json",json.dumps(data))
        z.write(source,"media/source.mp4")
    return path


def render(proc,page):
    start=time.perf_counter()
    page.locator("#exportVideo").click()
    result=exported_file(proc)
    return result,time.perf_counter()-start


def frame(path,seconds):
    data=subprocess.check_output(["ffmpeg","-v","error","-ss",str(seconds),"-i",str(path),"-frames:v","1",
                                  "-f","image2pipe","-c:v","png","-"])
    return Image.open(io.BytesIO(data)).convert("RGB")


def media_info(path):
    return json.loads(subprocess.check_output(["ffprobe","-v","error","-show_streams","-of","json",str(path)]))["streams"]


def blend(top,bottom,opacity):
    return tuple(round(opacity*t+(1-opacity)*b) for t,b in zip(top,bottom))


def check_color(frame,x,y,expected,tolerance=15):
    actual=frame.getpixel((x,y))
    assert max(abs(a-b) for a,b in zip(actual,expected))<tolerance,(x,y,actual,expected)


def toggle(proc,enabled):
    dlg=settings(proc)
    checkbox=dlg.child_window(auto_id="ExportBarEnabled")
    if checkbox.get_toggle_state()!=enabled:
        checkbox.toggle()
    assert dlg.child_window(auto_id="ExportBarFill").is_enabled()==enabled
    dlg.child_window(title="保存",control_type="Button").invoke()
    Desktop(backend="win32").window(process=proc.pid,title="设置").wait_not("visible")


def pick(proc,dlg,field,rgb):
    dlg.child_window(auto_id=field).invoke()
    picker=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
    picker.wait("ready")
    for key,value in zip((706,707,708),rgb):
        picker.child_window(control_id=key).set_edit_text(str(value))
    picker.child_window(control_id=1).click()
    picker.wait_not("visible")


with sync_playwright() as pw:
    proc,page=launch(pw,make_review("edited"))
    try:
        dlg=settings(proc)
        assert dlg.child_window(auto_id="ExportBarEnabled").get_toggle_state()==1
        dlg.child_window(title="取消",control_type="Button").invoke()
        Desktop(backend="win32").window(process=proc.pid,title="设置").wait_not("visible")
        times={True:[],False:[]}
        for on in (True,False,False,True,True,False):
            toggle(proc,on)
            path,seconds=render(proc,page)
            times[on].append(seconds)
            if on: enabled=path
            else: disabled=path
        print("MP4 export seconds",times,"medians",{k:statistics.median(v) for k,v in times.items()},flush=True)
        for file in (enabled,disabled):
            streams=media_info(file)
            assert {s["codec_type"] for s in streams}=={"video","audio"}
            assert all(abs(float(s["duration"])-duration)<.04 for s in streams)
        early,middle,late=(frame(enabled,t) for t in (1,3,7))
        plain=frame(disabled,1)
        orange,cyan,gray=(255,128,0),(48,192,255),(176,176,176)
        bar_top=early.height-18
        check_color(early,380,bar_top-1,plain.getpixel((380,plain.height-1)))
        for y in (bar_top,bar_top+8,early.height-1):
            check_color(early,380,y,orange)
            check_color(early,960,y,cyan)
            check_color(middle,380,y,blend(gray,orange,.5))
            check_color(middle,565,y,orange)
            check_color(late,565,y,blend(gray,orange,.5))
            check_color(late,960,y,blend(gray,cyan,.5))
        backdrop=plain.getpixel((800,plain.height-8))
        track=blend((0,0,0),backdrop,.65)
        check_color(early,800,early.height-8,track,6)
        check_color(late,800,late.height-8,blend(gray,track,.5),7)
        late.save(work/"export-progress-example.png")

        dlg=settings(proc)
        dlg.child_window(auto_id="ExportBarEnabled").toggle()
        pick(proc,dlg,"ExportBarFill",(32,220,32))
        dlg.child_window(title="取消",control_type="Button").invoke()
        Desktop(backend="win32").window(process=proc.pid,title="设置").wait_not("visible")
        dlg=settings(proc)
        assert dlg.child_window(auto_id="ExportBarEnabled").get_toggle_state()==0
        dlg.child_window(auto_id="ExportBarEnabled").toggle()
        pick(proc,dlg,"ExportBarFill",(32,220,32))
        pick(proc,dlg,"ExportBarBackground",(220,32,180))
        dlg.child_window(title="保存",control_type="Button").invoke()
        Desktop(backend="win32").window(process=proc.pid,title="设置").wait_not("visible")
        saved=json.loads((profile/"capture.json").read_text(encoding="utf-8-sig"))
        assert saved["exportBarFill"]=="#20DC20" and saved["exportBarBackground"]=="#DC20B4",saved
        print("PASS edited timeline, pixels, audio and custom color picker",flush=True)
    finally:
        stop(proc)

    for path in (source,make_review("live",lens=True)):
        proc,page=launch(pw,path)
        try:
            result,_=render(proc,page)
            f=frame(result,3.5)
            played=f.getpixel((60,f.height-2))
            unplayed=f.getpixel((1160,f.height-2))
            assert played[1]>played[0] and unplayed[0]>unplayed[1]+70,(played,unplayed)
            if path==source:
                original=frame(source,3.5)
                assert f.size==original.size
                for x in (60,1160):
                    base=original.getpixel((x,f.height-2))
                    track=blend((220,32,180),base,.65)
                    check_color(f,x,f.height-2,blend((32,220,32),track,.5) if x==60 else track)
            else:
                top=f.getpixel((961,f.height-14))
                bottom=f.getpixel((961,f.height-6))
                assert top[2]>top[0]+100 and bottom[0]>bottom[2]+100,(top,bottom)
                # Only the intersecting portion splits into lanes. The Region returns to full height at 3.75 s.
                check_color(f,590,f.height-15,(255,128,0))
                check_color(f,590,f.height-4,(172,88,244))
                check_color(f,625,f.height-1,(255,128,0))
                dlg=settings(proc)
                dlg.child_window(auto_id="ResetButton").invoke()
                dlg.child_window(title="保存",control_type="Button").invoke()
                Desktop(backend="win32").window(process=proc.pid,title="设置").wait_not("visible")
                saved=json.loads((profile/"capture.json").read_text(encoding="utf-8-sig"))
                assert saved["exportBarBackground"]=="#000000" and saved["exportBarFill"]=="#B0B0B0"
        finally:
            stop(proc)
print("PASS opacity, live lens, coincident Marks, saved colors and defaults",work,flush=True)
