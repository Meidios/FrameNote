"""Exercise grouped marks, scoped editing shortcuts, audio imports and Excel links in the desktop build."""
import hashlib
import json
import os
import subprocess
import time
import uuid
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

from PIL import ImageGrab, ImageChops
from playwright.sync_api import sync_playwright
from pywinauto import Desktop, mouse
from review_ui import at, exported_file, fixtures, launch, notes, settings, stop, wait, work

profile=work/"profile"
profile.mkdir()
(profile/"updates.json").write_text('{"automatic":false,"language":"zh-CN"}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
sound=work/"冲击声 #1.wav"
subprocess.run(["ffmpeg","-v","error","-f","lavfi","-i","sine=frequency=523:duration=0.35",str(sound)],check=True)
errors=[]
def path_xy(page,x,y):
    b=page.locator("#overlay").bounding_box()
    return (b["x"]+x*b["width"],b["y"]+y*b["height"])

def choose_audio(proc,file):
    picker=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
    picker.wait("ready")
    field=picker.child_window(class_name="Edit",found_index=0)
    field.wait("ready");field.set_focus();field.set_edit_text(str(file))
    field.type_keys("{ENTER}")
    picker.wait_not("visible",timeout=10)

with sync_playwright() as pw:
    proc,p=launch(pw,fixtures/"2997.mp4")
    p.on("pageerror",lambda e:errors.append(str(e)))
    try:
        dlg=settings(proc,"批注工具")
        def reorder(first,second):
            a=first.rectangle();b=second.rectangle()
            origin=(a.left+5,(a.top+a.bottom)//2)
            dest=(origin[0],b.bottom-2)
            mouse.move(coords=origin);mouse.press(coords=origin)
            try:
                for i in range(1,13):
                    mouse.move(coords=(origin[0],round(origin[1]+(dest[1]-origin[1])*i/12)))
                    time.sleep(.025)
            finally:mouse.release(coords=dest)
        color=lambda value:dlg.child_window(title="修改颜色 "+value,control_type="Button")
        reorder(color("#ffd886"),color("#8ddcff"))
        # Bring the tool rows into the scroll viewport before using actual pointer events.
        tool=lambda name:dlg.child_window(title=name,control_type="CheckBox")
        panel=dlg.rectangle()
        for step in range(20):
            r=tool("箭头").rectangle()
            if panel.top+100<r.top<panel.bottom-110:break
            mouse.scroll(coords=(panel.right-125,panel.bottom-150),wheel_dist=-2)
            time.sleep(.06)
        def visible_icon(name):
            r=tool(name).rectangle()
            mid=(r.top+r.bottom)//2
            return ImageGrab.grab(bbox=(r.left-24,mid-10,r.left-5,mid+10)).convert("L")
        icons=[visible_icon(name) for name in ("点批注","箭头")]
        assert all(im.getextrema()[1]-im.getextrema()[0]>40 for im in icons)
        assert ImageChops.difference(*icons).getbbox()
        reorder(tool("点批注"),tool("箭头"))
        dlg.child_window(title="保存",control_type="Button").invoke()
        wait(p,"palette[0][1]==='#8ddcff' && $('annotationTools').firstElementChild.id==='arrow'")
        at(p,0)
        wait(p,"catalog && !busy && !v.seeking")
        heights=[]
        for tool in ["arrow","line"]:
            p.locator("#"+tool).click()
            for x,y in [(.2,.4),(.3,.2),(.5,.5)]:p.mouse.click(*path_xy(p,x,y))
            p.mouse.click(*path_xy(p,.6,.3),button="right")
            assert notes(p)[-1]["shape"]["kind"]==tool and len(notes(p)[-1]["shape"]["points"])==3
            assert p.locator("#canvasMenu").is_hidden()
            heights.append(p.locator("#stage").bounding_box()["height"])
        p.locator("#point").click()
        heights.append(p.locator("#stage").bounding_box()["height"])
        assert len(set(heights))==1,heights
        assert p.locator("#markLanes [data-id]:visible").count()==1
        p.locator(".markCount").click()
        assert p.locator("#noteGroupMenu button").count()==3
        p.locator("#noteGroupMenu button").first.click()
        assert p.evaluate("selected===doc.notes[0].id")
        right=p.evaluate("['noteOpacity','noteStart','noteDuration'].map(id=>$(id).getBoundingClientRect().right)")
        assert max(right)-min(right)<1,right
        p.mouse.click(*path_xy(p,.85,.8))
        assert p.evaluate("selected===null && editing===null")
        p.locator("#list .stamp").first.click()
        p.locator("#stage").press("Escape")
        assert p.evaluate("!selected")
        p.locator("#list .stamp").first.click()
        clip=p.locator("#videoClips .editClip").first
        clip.click()
        assert p.evaluate("!selected")
        p.keyboard.press("Control+c");p.keyboard.press("Control+d")
        wait(p,"doc.timeline?.clips.filter(c=>c.track==='video').length===2")
        p.keyboard.press("Control+z");wait(p,"$('videoClips').children.length===1")
        clip.click();p.keyboard.press("Control+x");wait(p,"$('videoClips').children.length===0")
        p.keyboard.press("Control+v");wait(p,"$('videoClips').children.length===1")
        p.keyboard.press("Control+z");p.keyboard.press("Control+z")
        wait(p,"$('videoClips').children.length===1")
        # Timeline Esc and canvas Esc affect only their own selection.
        axis=p.locator("#ticks").bounding_box();y=axis["y"]+axis["height"]/2
        p.mouse.move(axis["x"]+15,y);p.mouse.down(button="right");p.mouse.move(axis["x"]+80,y);p.mouse.up(button="right")
        wait(p,"!!timeSelection")
        p.locator("#list .stamp").last.click();p.locator("#stage").press("Escape")
        assert p.evaluate("!selected && !!timeSelection")
        p.locator("#timeline").press("Escape");assert p.evaluate("!timeSelection")
        assert p.locator("#rangeHint").count()==0
        # Pass the original file through the native audio picker.
        at(p,4)
        p.locator("#addAudio").click(no_wait_after=True)
        choose_audio(proc,sound)
        wait(p,"doc.notes.some(n=>n.voice?.original) && !busy")
        first=next(n for n in notes(p) if n.get("voice"))
        assert first["voice"]["original"]["asset"]==hashlib.sha256(sound.read_bytes()).hexdigest()
        assert p.locator("#voiceClips .editClip").count()==1
        # Chromium receives filesystem-backed files through its external drag entry point.
        lane=p.locator("#voiceClips").bounding_box();x=lane["x"]+.48*lane["width"];y=lane["y"]+lane["height"]/2
        cdp=p.context.new_cdp_session(p)
        for kind in ["dragEnter","dragOver","drop"]:
            cdp.send("Input.dispatchDragEvent",dict(type=kind,x=x,y=y,data=dict(items=[],files=[str(sound)],dragOperationsMask=1)))
        wait(p,"doc.notes.filter(n=>n.voice?.original).length===2 && !busy")
        assert p.locator("#voiceClips .editClip").count()==2
        times=[n["startUs"] for n in notes(p) if n.get("voice")]
        assert abs(times[1]-.48*p.evaluate("doc.durationUs"))<40000,times
        p.locator("#save").click();package=exported_file(proc,work/"imports-only.framenote")
        with zipfile.ZipFile(package) as z:
            assert z.read("voice/"+first["voice"]["original"]["asset"]+".original")==sound.read_bytes()
            assert z.read("voice/"+first["voice"]["asset"]+".mp3")
        # Attach a file to an existing annotation without replacing the drawing.
        wait(p,"ready && !busy")
        original=notes(p)[0];count=len(notes(p))
        p.locator("#list .stamp").first.click()
        p.locator("#addAudio").click(no_wait_after=True)
        choose_audio(proc,sound)
        wait(p,"id=>!!doc.notes.find(n=>n.id===id)?.voice?.original && !busy",original["id"])
        current=next(n for n in notes(p) if n["id"]==original["id"])
        assert len(notes(p))==count and current["shape"]==original["shape"]
        formats=[".flac",".ogg",".mp3",".aiff"]
        files=[]
        for ext in formats:
            file=work/("batch"+ext)
            subprocess.run(["ffmpeg","-v","error","-i",str(sound),str(file)],check=True)
            files.append(str(file))
        for kind in ["dragEnter","dragOver","drop"]:
            cdp.send("Input.dispatchDragEvent",dict(type=kind,x=x,y=y,data=dict(items=[],files=files,dragOperationsMask=1)))
        wait(p,"count=>doc.notes.length===count && !busy",count+len(formats),timeout=20000)
        extra=notes(p)[-len(formats):]
        assert [n["voice"]["original"]["name"] for n in extra]==[Path(file).name for file in files]
        assert all(a["endUs"]==b["startUs"] for a,b in zip(extra,extra[1:]))
        # Duplicating a trimmed clip must preserve its hidden notes in the saved review.
        p.evaluate("""()=>{const d=currentEditDoc(),clip=d.timeline.clips.find(c=>c.track==='video');
            commitTimeline(EditModel.execute(d,clip,'left',Math.round(clip.durationUs*.8)))}""")
        assert p.evaluate("doc.timeline.trimmed.some(part=>part.note)")
        p.locator("#videoClips .editClip").first.click()
        p.keyboard.press("Control+d")
        wait(p,"doc.timeline.clips.filter(c=>c.track==='video').length===2")
        expected=p.evaluate("doc.timeline.trimmed.filter(part=>part.note).map(part=>part.note.number)")
        p.locator("#save").click();trimmed=exported_file(proc)
        with zipfile.ZipFile(trimmed) as z:
            saved=json.loads(z.read("review.json"))
            assert [part["note"]["number"] for part in saved["timeline"]["trimmed"] if part.get("note")]==expected
            assert saved["lastNoteNumber"]>=max(expected)
        assert not errors,errors
    finally:stop(proc)
    # Add a pre-MP3 voice note and a current MP3 voice note to the same review.
    mixed=work/"mixed.framenote"
    with zipfile.ZipFile(package) as src,zipfile.ZipFile(mixed,"w") as out:
        d=json.loads(src.read("review.json"))
        for name in src.namelist():
            if name!="review.json":out.writestr(name,src.read(name))
        for ext in ("m4a","mp3"):
            audio=work/("voice."+ext)
            subprocess.run(["ffmpeg","-v","error","-i",str(sound),str(audio)],check=True)
            h=hashlib.sha256(audio.read_bytes()).hexdigest()
            out.writestr("voice/"+h+"."+ext,audio.read_bytes())
            voice={"asset":h,"durationUs":350000}
            if ext=="mp3":voice["format"]="mp3"
            d["lastNoteNumber"]+=1
            n={"id":str(uuid.uuid4()),"number":d["lastNoteNumber"],"text":ext,"startUs":0,"endUs":350000,"voice":voice}
            d["notes"].append(n)
            d["timeline"]["clips"].append(dict(id=str(uuid.uuid4()),track="voice",noteId=n["id"],startUs=0,sourceUs=0,sourceDurationUs=350000,**voice))
        out.writestr("review.json",json.dumps(d))
    proc,p=launch(pw,mixed)
    try:
        assert p.locator("#voiceClips .editClip").count()==4
        assert p.evaluate("palette[0][1]")=="#8ddcff"
        assert p.locator("#annotationTools > :first-child").get_attribute("id")=="arrow"
        p.locator("#exportExcel").click();book=exported_file(proc)
        if book.suffix==".zip":
            with zipfile.ZipFile(book) as z:z.extractall(work/"excel")
            book=next((work/"excel").glob("*.xlsx"))
        originals=list((book.parent/"audio").glob("*.wav"))
        assert len(originals)==2 and all(f.read_bytes()==sound.read_bytes() for f in originals)
        with zipfile.ZipFile(book) as z:
            rels=ET.fromstring(z.read("xl/worksheets/_rels/sheet1.xml.rels"))
            assert sum("audio/" in x.attrib["Target"] for x in rels)==4
        encoded=sorted((book.parent/"audio").glob("*.mp3"))
        assert len(encoded)==2 and encoded[-1].read_bytes()==(work/"voice.mp3").read_bytes()
        for file in encoded:
            assert subprocess.check_output(["ffprobe","-v","error","-select_streams","a:0","-show_entries","stream=codec_name","-of","csv=p=0",str(file)]).strip()==b"mp3"
        print("PASS native drag sorting, legacy/new voice MP3 links, right-click paths, single-row groups, focus/Esc, clip clipboard, picker/drop, package and Excel audio files",work,flush=True)
    finally:stop(proc)
