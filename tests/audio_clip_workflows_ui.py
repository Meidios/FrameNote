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

import win32gui
from ole_files import hold_files
from PIL import ImageGrab, ImageChops
from playwright.sync_api import sync_playwright
from pywinauto import Desktop, keyboard, mouse
from review_ui import desk, at, exported_file, fixtures, launch, notes, settings, stop, wait, work

profile=work/"profile"
profile.mkdir()
(profile/"updates.json").write_text('{"automatic":false,"language":"zh-CN"}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
sound=work/"冲击声 #1.wav"
subprocess.run(["ffmpeg","-v","error","-f","lavfi","-i","sine=frequency=523:duration=0.35",
                "-af","volume='if(lt(t,0.14),4,0)':eval=frame",str(sound)],check=True)
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
        assert p.title()=="FrameNote" and p.locator("#timeline").is_visible()
        for label,section in [("使用说明","使用说明"),("更新记录","更新")]:
            win=desk(proc)
            win.child_window(title="帮助",control_type="MenuItem",depth=3).expand()
            win.child_window(title=label,control_type="MenuItem",found_index=0).invoke()
            window=Desktop(backend="win32").window(process=proc.pid,title="设置")
            window.wait("ready")
            dialog=Desktop(backend="uia").window(handle=window.handle)
            assert dialog.child_window(title=section,control_type="TabItem").is_selected()
            window.close();window.wait_not("visible")
        dlg=settings(proc,"批注工具")
        def reorder(first,second,cancel=False):
            a=first.rectangle();b=second.rectangle()
            origin=(a.left+5,(a.top+a.bottom)//2)
            dest=(origin[0],b.bottom-2)
            mouse.move(coords=origin);mouse.press(coords=origin)
            try:
                for i in range(1,13):
                    mouse.move(coords=(origin[0],round(origin[1]+(dest[1]-origin[1])*i/12)))
                    time.sleep(.025)
                time.sleep(.18)
                assert first.rectangle().top>a.top+5 and second.rectangle().top<b.top-5
                if cancel:keyboard.send_keys("{ESC}")
                else:dlg.capture_as_image().save(work/"settings-drag.png")
            finally:mouse.release(coords=dest)
        color=lambda value:dlg.child_window(title="修改颜色 "+value,control_type="Button")
        reorder(color("#ffd886"),color("#8ddcff"),cancel=True)
        time.sleep(.18)
        assert color("#ffd886").rectangle().top<color("#8ddcff").rectangle().top
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
        for _ in range(24):
            if panel.top+65<tool("清除图形").rectangle().bottom<panel.bottom-80:break
            mouse.scroll(coords=(panel.right-125,panel.bottom-150),wheel_dist=-3)
            time.sleep(.05)
        time.sleep(.16)
        dlg.capture_as_image().save(work/"settings-tool-icons.png")
        for name in ("画笔","角度","添加图片","移除图片","清除图形"):
            icon=visible_icon(name)
            assert icon.getextrema()[1]-icon.getextrema()[0]>40,name
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
        gap=p.locator("#noteGroupMenu button").evaluate_all("(b)=>b[1].getBoundingClientRect().top-b[0].getBoundingClientRect().bottom")
        assert 2<=gap<=4,gap
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
        # Only a Windows OLE drag exposes real file durations before the mouse is released.
        children=[]
        win32gui.EnumChildWindows(desk(proc).handle,lambda h,_:children.append(h) if win32gui.GetClassName(h)=="Chrome_RenderWidgetHostHWND" else None,None)
        scale=p.evaluate("devicePixelRatio")
        def screen(px,py):
            return win32gui.ClientToScreen(children[0],(round(px*scale),round(py*scale)))
        lane=p.locator("#voiceClips").bounding_box()
        x=lane["x"]+.48*lane["width"];y=lane["y"]+lane["height"]/2
        indicator=p.locator("#audioDropPreview")
        for cancel in (True,False):
            with hold_files([sound]):
                mouse.move(coords=screen(x-40,y))
                wait(p,"!dropPreview.hidden")
                block=indicator.locator("span")
                earlier=block.bounding_box()
                assert abs(earlier["width"]-lane["width"]*350000/p.evaluate("doc.durationUs"))<1
                mouse.move(coords=screen(x,y-35))
                wait(p,"dropPreview.hidden")
                mouse.move(coords=screen(x,y))
                wait(p,"!dropPreview.hidden")
                final=block.bounding_box()
                assert abs(final["x"]-earlier["x"]-40)<2 and abs(final["width"]-earlier["width"])<1
                assert abs(final["x"]-x)<2
                expected_time=p.evaluate("Math.round(parseFloat(dropPreview.firstChild.style.left)*doc.durationUs/100)")
                assert len([n for n in notes(p) if n.get("voice")])==1
                if cancel:
                    keyboard.send_keys("{ESC}")
                    wait(p,"dropPreview.hidden")
                else:
                    p.locator("#trackDesk").screenshot(path=str(work/"drop-target.png"))
                    desk_box=p.locator("#trackDesk").bounding_box()
                    bounds=(*screen(desk_box["x"],desk_box["y"]),*screen(desk_box["x"]+desk_box["width"],desk_box["y"]+desk_box["height"]))
                    desktop=ImageGrab.grab(bbox=bounds).convert("RGB")
                    desktop.save(work/"windows-drop-target.png")
                    def color(px):
                        sx,sy=screen(px,y)
                        return desktop.getpixel((sx-bounds[0],sy-bounds[1]))
                    assert sum(color(final["x"]+final["width"]*.7))>sum(color(final["x"]+final["width"]+6))+40
                    mouse.release(coords=screen(x,y))
        wait(p,"doc.notes.filter(n=>n.voice?.original).length===2 && !busy")
        wait(p,"Array.from($('voiceClips').querySelectorAll('use')).every(el=>document.querySelector(el.getAttribute('href'))?.getAttribute('d'))")
        assert indicator.is_hidden() and p.locator("#voiceClips .editClip").count()==2
        times=[n["startUs"] for n in notes(p) if n.get("voice")]
        assert abs(times[1]-expected_time)<=1,(times,expected_time)
        landed=p.locator("#voiceClips .editClip").last.bounding_box()
        assert abs(landed["x"]-final["x"])<1 and abs(landed["width"]-final["width"])<1
        p.locator("#trackDesk").screenshot(path=str(work/"landed-wave.png"))
        clip=p.locator("#voiceClips .editClip").last
        wave=clip.locator(".voiceClipWave")
        assert wave.evaluate("""el=>{const path=document.querySelector(el.firstChild.getAttribute('href'));
            return path.isPointInFill(new DOMPoint(65,12))&&!path.isPointInFill(new DOMPoint(290,12))}""")
        handle=clip.locator(".clipHandle.left").bounding_box()
        origin=handle["x"]+handle["width"]/2
        p.mouse.move(origin,y);p.mouse.down();p.mouse.move(origin+landed["width"]*.6,y,steps=10)
        try:
            view=wave.evaluate("el=>[el.viewBox.baseVal.x,el.viewBox.baseVal.width]")
            assert view[0]>150 and abs(sum(view)-350)<.01,view
        finally:p.mouse.up()
        assert wave.evaluate("el=>el.viewBox.baseVal.x")>150
        p.keyboard.press("Control+z")
        wait(p,"$('voiceClips').lastElementChild.querySelector('svg').viewBox.baseVal.x===0")
        before=p.evaluate("structuredClone(doc.timeline.clips)")
        assert p.evaluate("doc.timeline.linked")
        p.locator("#voiceClips .editClip").first.click();p.keyboard.press("Delete")
        wait(p,"$('voiceClips').children.length===1")
        assert p.evaluate("doc.timeline.clips.filter(c=>c.track!=='voice')")==[c for c in before if c["track"]!="voice"]
        p.keyboard.press("Control+z");wait(p,"$('voiceClips').children.length===2")
        assert p.evaluate("doc.timeline.clips")==before
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
        p.mouse.move(x,y);p.keyboard.down("Control");p.mouse.wheel(0,-360);p.keyboard.up("Control")
        wait(p,"$('scroll').scrollLeft>0 && $('ticks').clientWidth>$('scroll').clientWidth")
        with hold_files(files):
            mouse.move(coords=screen(x,y))
            wait(p,"dropPreview.children.length===4 && !dropPreview.hidden")
            batch_preview=p.evaluate("Array.from(dropPreview.children,b=>[parseFloat(b.style.left),parseFloat(b.style.width)].map(n=>Math.round(n*doc.durationUs/100)))")
            assert abs(indicator.locator("span").first.bounding_box()["x"]-x)<2
            mouse.release(coords=screen(x,y))
        wait(p,"count=>doc.notes.length===count && !busy",count+len(formats),timeout=20000)
        extra=notes(p)[-len(formats):]
        assert [n["voice"]["original"]["name"] for n in extra]==[Path(file).name for file in files]
        assert all(abs(n["startUs"]-start)<=1 and abs(n["voice"]["durationUs"]-length)<=1 for n,(start,length) in zip(extra,batch_preview))
        assert all(a["endUs"]==b["startUs"] for a,b in zip(extra,extra[1:]))
        # Duplicating a trimmed clip must preserve its hidden notes in the saved review.
        p.evaluate("""()=>{const d=currentEditDoc(),clip=d.timeline.clips.find(c=>c.track==='video');
            commitTimeline(EditModel.execute(d,clip,'left',Math.round(clip.durationUs*.8)))}""")
        assert p.evaluate("doc.timeline.trimmed.some(part=>part.note)")
        p.locator("#videoClips .editClip").first.click()
        p.keyboard.press("Control+d")
        wait(p,"doc.timeline.clips.filter(c=>c.track==='video').length===2")
        wait(p,"Array.from($('voiceClips').children).every(b=>{const use=b.querySelector('use');return use && document.querySelector(use.getAttribute('href'))?.getAttribute('d')})")
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
        wait(p,"Array.from($('voiceClips').querySelectorAll('.voiceClipWave use')).filter(el=>document.querySelector(el.getAttribute('href'))?.getAttribute('d')).length===4")
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
