"""Calibrate real rulers, render them to MP4 and reopen a package saved with hidden notes."""
import hashlib
import io
import json
import os
import subprocess
import time
import uuid
import zipfile
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import exported_file, launch, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
media, voice, picture = (work / name for name in ["source.mp4", "voice.m4a", "image.png"])
subprocess.run(["ffmpeg","-v","error","-f","lavfi","-i","color=black:size=640x360:rate=30:duration=14",
                "-c:v","libx264",str(media)],check=True)
subprocess.run(["ffmpeg","-v","error","-f","lavfi","-i","sine=frequency=440:duration=1",
                "-c:a","aac",str(voice)],check=True)
Image.new("RGB",(32,24),"red").save(picture)
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def note(number, start, **extra):
    return dict(id=str(uuid.uuid4()),number=number,startUs=start,shape=None,text=f"批注 {number}",**extra)
original_notes = [note(1,10_000_000,endUs=None,pictures=[dict(asset=digest(picture),pixelWidth=32,
                   pixelHeight=24,x=.2,y=.2,width=.2)]),
                  note(2,8_000_000,endUs=9_000_000,voice=dict(asset=digest(voice),durationUs=1_000_000))]
reference=dict(kind="ruler",x=.25,y=.5,toX=.4,toY=.5)
for number,start in [(3,6_000_000),(4,7_000_000)]:
    original_notes.append(dict(id=str(uuid.uuid4()),number=number,startUs=start,endUs=None,
                               shape={**reference,"rulerFrame":start},text="",color="#ffffff"))
review = dict(version=1,id=str(uuid.uuid4()),fileName=media.name,durationUs=14_000_000,
              lastNoteNumber=4,notes=original_notes)
package,saved,exported = (work/name for name in ["input.framenote","trimmed.framenote","rulers.mp4"])
with zipfile.ZipFile(package,"w") as z:
    z.writestr("review.json",json.dumps(review))
    z.write(media,"media/source.mp4")
    z.write(picture,"images/"+digest(picture)+".png")
    z.write(voice,"voice/"+digest(voice)+".m4a")


def save(proc,p,button,path,message):
    p.locator(button).click()
    if button in ("#save","#exportVideo"):
        exported_file(proc,path);return
    dlg=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
    dlg.wait("ready",timeout=15)
    time.sleep(.3)
    edit=dlg.child_window(class_name="Edit",found_index=0)
    edit.set_edit_text(str(path))
    dlg.child_window(control_id=1).click()
    if not path.exists() and dlg.exists(timeout=.5) and dlg.is_visible():
        edit.type_keys("{ENTER}")
    wait(p,"s=>!busy && $('status').textContent.startsWith(s)",message,timeout=90000)


def trim_left(p,delta):
    box=p.locator("#videoClips .editClip [data-edge=left]").bounding_box()
    x,y=box["x"]+box["width"]/2,box["y"]+box["height"]/2
    width=p.locator("#ticks").bounding_box()["width"]
    p.mouse.move(x,y)
    p.mouse.down()
    p.mouse.move(max(1,x+width*delta/14),y,steps=8)
    p.mouse.up()


def seek(p,seconds):
    p.evaluate("s=>seekUs(s*1e6)", seconds)
    wait(p,"s=>!v.seeking && Math.abs(shown/1e6-s)<.001",seconds)


def ruler(p,length,y):
    p.locator("#toolRuler").click()
    box=p.locator("#overlay").bounding_box()
    x,y=round(box["x"]+.2*box["width"]),round(box["y"]+y*box["height"])
    p.mouse.move(x,y)
    p.mouse.down()
    p.mouse.move(x+length,y,steps=4)
    p.mouse.up()
    return p.evaluate("editing.shape")


def calibrate(p,value,unit):
    p.locator("#rulerLength").fill(str(value))
    p.locator("#rulerUnit").select_option(unit)
    p.locator("#calibrateRuler").click()


def label_width(seconds,shape):
    frame=subprocess.check_output(["ffmpeg","-v","error","-ss",str(seconds),"-i",str(exported),
                                   "-frames:v","1","-f","image2pipe","-c:v","png","-"])
    im=Image.open(io.BytesIO(frame)).convert("L")
    x=round((shape["x"]+shape["toX"])*320)
    y=round(shape["y"]*360)
    label=im.crop((x,y-26,x+110,y-10)).point(lambda p: 255 if p>100 else 0).getbbox()
    assert label, (seconds,x,y)
    return label[2]-label[0]


with sync_playwright() as pw:
    proc,p=launch(pw,package)
    try:
        seek(p,2)
        first=ruler(p,180,.35)
        assert p.locator("#body").input_value()==f"距离标尺 {abs(first['toX']-first['x'])*640*.0254/96:.6g} m"
        calibrate(p,1,"m")
        ruler(p,90,.65)
        assert "0.5 m" in p.locator("#ink").text_content()
        assert p.locator("#body").input_value()=="距离标尺 0.5 m"
        calibrate(p,50,"cm")
        assert all(t in p.locator("#ink").text_content() for t in ["100 cm","50 cm"])
        assert p.locator(".noteBody").all_text_contents()[:2]==["距离标尺 100 cm","距离标尺 50 cm"]
        calibrate(p,.5,"m")
        p.keyboard.press("Escape")
        seek(p,4)
        second=ruler(p,180,.35)
        assert p.locator("#body").input_value()==f"距离标尺 {abs(second['toX']-second['x'])*640*.0254/96:.6g} m"
        calibrate(p,1000,"mm")
        p.locator("#body").fill("校准基准")
        calibrate(p,100,"cm")
        assert p.locator("#body").input_value()=="校准基准"
        calibrate(p,1000,"mm")
        p.keyboard.press("Escape")
        wait(p,"Object.keys(doc.rulerScales).length===2")
        crops=[]
        for second_in_video in (6,7):
            seek(p,second_in_video)
            p.locator(".note").filter(has_text=f"00:0{second_in_video}.000").click()
            if second_in_video==7:
                calibrate(p,.0254,"m")
            assert p.locator("#body").input_value()=="距离标尺 0.0254 m"
            p.keyboard.press("Escape")
            png=work/f"ruler-{second_in_video}.png"
            save(proc,p,"#saveFrame",png,"当前帧已保存")
            with Image.open(png) as shot:
                crops.append(shot.convert("RGB").crop((208,144,310,176)))
        assert crops[0].getbbox() and crops[0].tobytes()==crops[1].tobytes()
        save(proc,p,"#exportVideo",exported,"带批注的 MP4 已保存")
        short,long=label_width(2.2,first),label_width(4.2,second)
        assert long>short+15,(short,long)
        assert abs(label_width(6.2,reference)-label_width(7.2,reference))<=1
        expected=p.evaluate("doc.notes")
        scales=p.evaluate("doc.rulerScales")
        trim_left(p,11)
        wait(p,"doc.timeline?.trimmed?.length && !doc.notes.length && !$('voiceClips').childElementCount")
        save(proc,p,"#save",saved,"审阅包已保存")
    finally:
        stop(proc)
    with zipfile.ZipFile(saved) as z:
        assert z.read("images/"+digest(picture)+".png")==picture.read_bytes()
        assert z.read("voice/"+digest(voice)+".m4a")==voice.read_bytes()
    proc,p=launch(pw,saved)
    try:
        p.evaluate("transport.pause()")
        assert p.evaluate("doc.notes.length") == 0
        trim_left(p,-13)
        wait(p,"n=>doc.notes.length===n && $('voiceClips').childElementCount===1",len(expected))
        restored=p.evaluate("doc.notes")
        # The native serializer may materialize optional properties as null.
        def canonical(n):
            return {k:v for k,v in n.items() if v is not None}
        assert {n["id"]:canonical(n) for n in expected}=={n["id"]:canonical(n) for n in restored}
        assert p.evaluate("doc.rulerScales")==scales
        seek(p,2)
        assert "0.5 m" in p.locator("#ink").text_content()
        assert "距离标尺 0.5 m" in p.locator(".noteBody").all_text_contents()
        assert "校准基准" in p.locator(".noteBody").all_text_contents()
        p.locator(".note").filter(has_text="距离标尺 1 m").click()
        calibrate(p,1000,"mm")
        assert "距离标尺 500 mm" in p.locator(".noteBody").all_text_contents()
        p.keyboard.press("Control+z")
        assert "距离标尺 0.5 m" in p.locator(".noteBody").all_text_contents()
        seek(p,10)
        assert p.locator("#ink image.picture").count()==1
        print("PASS: mm/cm/m, independent per-frame scales, rendered MP4 label widths",short,long,
              "and persisted hidden Marks, Regions, pictures and voice restored.",flush=True)
    finally:
        stop(proc)
