"""Exercise annotation gestures and preferences in the desktop app, then compare exported pixels."""
import io
import json
import os
import subprocess
import uuid
import zipfile

from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop

from review_ui import exported_file, launch, notes, settings, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false,"language":"zh-CN"}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
image = work / "canvas.png"
Image.new("RGB", (640, 400), "#202020").save(image)
image_ready = "typeof imageDoc==='function' && imageDoc() && $('openedImage').naturalWidth===640"


def point(p, x, y):
    r = p.locator("#overlay").bounding_box()
    return r["x"] + x*r["width"], r["y"] + y*r["height"]


def drag(p, *points):
    p.mouse.move(*point(p, *points[0]))
    p.mouse.down()
    for xy in points[1:]:
        p.mouse.move(*point(p, *xy), steps=8)
    p.mouse.up()


def wheel(p, x, y, delta=-120, modifier=None):
    p.mouse.move(*point(p, x, y))
    if modifier:
        p.keyboard.down(modifier)
    p.mouse.wheel(0, delta)
    p.evaluate("new Promise(done=>requestAnimationFrame(()=>requestAnimationFrame(done)))")
    if modifier:
        p.keyboard.up(modifier)


def set_color(proc, button, rgb):
    button.invoke()
    picker = Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
    picker.wait("ready")
    picker=Desktop(backend="win32").window(handle=picker.handle)
    for control, value in zip((706,707,708), rgb):
        picker.child_window(control_id=control).set_edit_text(str(value))
    picker.child_window(control_id=1).click()
    if picker.exists(timeout=.2) and picker.is_visible():
        picker.child_window(control_id=1).click_input()
    try:
        picker.wait_not("visible",timeout=8)
    except Exception:
        print("Color dialog",rgb,[(w.window_text(),w.class_name()) for w in Desktop(backend="win32").windows(process=proc.pid)],flush=True)
        raise


def check_pixels(bitmap, expected):
    im = bitmap.convert("RGB")
    for note in expected:
        s = note["shape"]
        if s["kind"] not in ("brush","line","arrow"):
            continue
        if s.get("points"):
            m=s["points"][len(s["points"])//2]
            x,y=m["x"],m["y"]
        else:
            x,y=s["thirdX"],s["thirdY"]
        cx,cy=round(x*640),round(y*400)
        rgb=bytes.fromhex(note["color"][1:])
        alpha=note.get("opacity",100)/100
        rgb=[round(32*(1-alpha)+c*alpha) for c in rgb]
        error=min(max(abs(a-b) for a,b in zip(pixel,rgb))
                  for pixel in im.crop((cx-2,cy-2,cx+3,cy+3)).get_flattened_data())
        assert error<42,(s["kind"],cx,cy,error)


with sync_playwright() as pw:
    proc,p=launch(pw,image,image_ready)
    try:
        p.locator("#box").click()
        drag(p,(.08,.10),(.35,.32))
        box=notes(p)[0]["shape"]
        viewport=p.locator("#openedImage").get_attribute("style")
        wheel(p,.20,.20)
        enlarged=notes(p)[0]["shape"]
        for a,b in [("x","toX"),("y","toY")]:
            assert abs((enlarged[b]-enlarged[a])/(box[b]-box[a])-1.12)<.001
        assert p.locator("#openedImage").get_attribute("style")==viewport
        wheel(p,.48,.18)
        assert notes(p)[0]["shape"]==enlarged
        assert p.locator("#openedImage").get_attribute("style")!=viewport
        wheel(p,.48,.18,120)
        assert notes(p)[0]["opacity"]==80
        wheel(p,.20,.20,modifier="Alt")
        assert notes(p)[0]["color"]=="#ffd886"
        wheel(p,.20,.20,120,"Alt")
        assert notes(p)[0]["color"]=="#8ddcff" and notes(p)[0]["shape"]==enlarged
        for _ in range(7):
            wheel(p,.20,.20,120,"Alt")
        assert notes(p)[0]["color"]=="#ffaed8"
        for _ in range(6):
            wheel(p,.20,.20,modifier="Alt")
        assert notes(p)[0]["color"]=="#ffd886"
        wheel(p,.20,.20,120,"Shift")
        assert notes(p)[0]["opacity"]==75
        field=p.locator("#noteOpacity")
        field.fill("2");field.press("Tab")
        wheel(p,.20,.20,120,"Shift")
        assert notes(p)[0]["opacity"]==0
        wheel(p,.20,.20,modifier="Shift")
        assert notes(p)[0]["opacity"]==5
        field.fill("100");field.press("Tab")
        field.hover();p.mouse.wheel(0,-120)
        assert notes(p)[0]["opacity"]==100
        p.mouse.wheel(0,120);wait(p,"doc.notes[0].opacity===95")

        for kind,y in [("arrow",.50),("line",.82)]:
            p.locator("#"+kind).click()
            drag(p,(.08,y),(.56,y))
            drag(p,(.32,y),(.35,y-.10))
            curved=notes(p)[-1]["shape"]
            assert abs(curved["thirdX"]-.35)<.005 and abs(curved["thirdY"]-y+.10)<.005
            wheel(p,curved["thirdX"],curved["thirdY"])
            assert notes(p)[-1]["shape"]["thirdY"]<curved["thirdY"]
            p.keyboard.press("Control+z")
            assert notes(p)[-1]["shape"]==curved
            p.keyboard.press("Control+Shift+z")

        curve=notes(p)[-1]["shape"]
        p.mouse.click(*point(p,curve["thirdX"],curve["thirdY"]),button="right")
        menu_opacity=p.locator("#menuOpacity")
        menu_opacity.fill("25")
        menu_opacity.press("Tab")
        menu_opacity.hover();p.mouse.wheel(0,-120)
        wait(p,"doc.notes.at(-1).opacity===30")
        p.keyboard.press("Escape")
        p.locator("#brush").click()
        wheel(p,.68,.58)
        drag(p,(.68,.58),(.80,.70),(.92,.53))
        p.mouse.click(*point(p,.8,.9))
        p.keyboard.press("Escape")
        stroke=notes(p)[-2]["shape"]
        assert len(stroke["points"])>5 and stroke["brushSize"]==5
        assert len(notes(p)[-1]["shape"]["points"])==1
        middle=stroke["points"][len(stroke["points"])//2]
        wheel(p,middle["x"],middle["y"])
        assert notes(p)[-2]["shape"]["brushSize"]==6
        p.keyboard.press("Control+z")
        assert notes(p)[-2]["shape"]==stroke
        p.keyboard.press("Control+Shift+z")

        for kind,end_key,y in [("arrow","Enter",.45),("line",None,.80)]:
            p.locator("#"+kind).click()
            p.mouse.click(*point(p,.62,y))
            p.mouse.click(*point(p,.72,y-.20))
            p.mouse.move(*point(p,.84,y))
            assert p.locator("#measurementPreview .inkShape").get_attribute("d").count("C")==2
            if end_key:
                p.mouse.click(*point(p,.84,y));p.keyboard.press(end_key)
            else:
                p.mouse.dblclick(*point(p,.84,y))
            path=notes(p)[-1]["shape"]
            assert path["kind"]==kind and len(path["points"])==3,path
            drag(p,(.72,y-.20),(.73,y-.15))
            knot=notes(p)[-1]["shape"]["points"][1]
            assert abs(knot["x"]-.73)<.006
            wheel(p,knot["x"],knot["y"])
            assert notes(p)[-1]["shape"]["smoothness"]>1
        count=len(notes(p))
        p.locator("#line").click();p.mouse.click(*point(p,.2,.3));p.keyboard.press("Escape")
        assert len(notes(p))==count
        dlg=settings(proc,"批注工具")
        dlg.child_window(auto_id="DefaultOpacity").set_edit_text("65")
        dlg.child_window(title="箭头",control_type="CheckBox").toggle()
        dlg.child_window(title="双宽",control_type="CheckBox",found_index=0).toggle()
        dlg.child_window(title="下移",control_type="Button",found_index=0).invoke()
        dlg.child_window(title="添加分隔线",control_type="Button").invoke()
        dlg.child_window(title="上移",control_type="Button",found_index=14).invoke()
        dlg.child_window(title="添加分隔线",control_type="Button").invoke()
        dlg.child_window(title="删除分隔线",control_type="Button",found_index=1).invoke()
        dlg.child_window(auto_id="DefaultLensZoom").select("4.5×")
        set_color(proc,dlg.child_window(title="修改颜色 #ffd886",control_type="Button"),(30,200,100))
        for i in range(4):
            set_color(proc,dlg.child_window(auto_id="AddPreset"),(130+i,110,225))
        assert not dlg.child_window(auto_id="AddPreset").is_enabled()
        dlg.child_window(title="下移颜色",control_type="Button",found_index=0).invoke()
        dlg.child_window(title="删除颜色",control_type="Button",found_index=9).invoke()
        dlg.child_window(title="快捷键",control_type="TabItem").select()
        dlg.child_window(auto_id="opacity").invoke()
        dlg.child_window(auto_id="editBinding0").invoke()
        dlg.child_window(auto_id="MouseChoice").select("鼠标滚轮 - 反转")
        dlg.child_window(auto_id="ApplyBinding").invoke()
        dlg.child_window(auto_id="cycleColor").invoke()
        dlg.child_window(auto_id="editBinding0").invoke()
        dlg.child_window(auto_id="MouseChoice").select("鼠标滚轮 - 反转")
        dlg.child_window(auto_id="ApplyBinding").invoke()
        dlg.child_window(title="保存",control_type="Button").invoke()
        wait(p,"defaultLensZoom===4.5 && palette.length===9 && shortcutBindings.cycleColor.keys[0].vk===257")
        assert p.evaluate("shortcutBindings.opacity.keys[0].vk===257")
        wheel(p,.20,.20,120,"Shift")
        assert notes(p)[0]["opacity"]==100
        colors=p.evaluate("palette.map(p=>p[1])")
        assert colors[:2]==["#8ddcff","#1ec864"] and colors[-1]=="#846ee1",colors
        wheel(p,.20,.20,modifier="Alt")
        assert notes(p)[0]["color"]==colors[0]
        wheel(p,.20,.20,modifier="Alt")
        assert notes(p)[0]["color"]==colors[1]
        p.locator("#magnifier").click()
        drag(p,(.75,.12),(.81,.18))
        assert notes(p)[-1]["shape"]["zoom"]==4.5 and notes(p)[-1]["opacity"]==65
        assert not p.locator("#arrow").is_visible()
        assert p.locator("#annotationTools > :nth-child(2)").get_attribute("id")=="point"
        assert p.locator("#point").bounding_box()["width"]>=2*p.locator("#box").bounding_box()["width"]
        assert p.locator("#annotationTools > .toolSeparator + #clear").count()==1
        p.keyboard.press("Escape")
        expected=notes(p)
        p.keyboard.press("Control+s")
        package=exported_file(proc)
    finally:
        stop(proc)

    proc,p=launch(pw,package,image_ready)
    try:
        reopened=notes(p)
        assert reopened==expected,[(a["number"],{k:(a[k],b.get(k)) for k in a if a[k]!=b.get(k)}) for a,b in zip(reopened,expected) if a!=b]
        assert p.evaluate("defaultLensZoom===4.5 && palette.length===9 && shortcutBindings.cycleColor.keys[0].vk===257 && defaultOpacity===65 && $('point').classList.contains('doubleWidth')")
        assert p.locator("#colors [data-color]").count()==9 and p.locator("#menuColors [data-color]").count()==9
        dest=work/"annotated.png"
        p.locator("#saveImage").click()
        picker=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
        picker.wait("ready")
        picker.child_window(class_name="Edit",found_index=0).set_edit_text(str(dest))
        picker.child_window(control_id=1).click()
        wait(p,"!busy && $('status').textContent.includes('annotated.png')")
        check_pixels(Image.open(dest),expected)
    finally:
        stop(proc)

    video=work/"canvas.mp4"
    subprocess.run(["ffmpeg","-v","error","-loop","1","-i",str(image),"-t","1","-c:v","libx264",
                    "-pix_fmt","yuv420p",str(video)],check=True)
    video_package=work/"canvas-video.framenote"
    with zipfile.ZipFile(package) as archive, zipfile.ZipFile(video_package,"w") as out:
        doc=json.loads(archive.read("review.json"))
        del doc["notes"][0]["opacity"]  # Simulate a note saved before opacity existed.
        doc.update(version=1,id=str(uuid.uuid4()),fileName=video.name,durationUs=1000000)
        out.writestr("review.json",json.dumps(doc))
        out.write(video,"media/source.mp4")
    proc,p=launch(pw,video_package)
    try:
        assert p.evaluate("!('opacity' in doc.notes[0]) && noteOpacity(doc.notes[0])===100")
        assert p.locator("#annotationTools > .toolSeparator").count()==1 and p.locator("#arrow").is_hidden()
        assert p.locator("#point").bounding_box()["width"]>=2*p.locator("#box").bounding_box()["width"]
        p.locator("#exportVideo").click()
        mp4=exported_file(proc)
        frame=subprocess.check_output(["ffmpeg","-v","error","-ss","0.2","-i",str(mp4),
                                       "-frames:v","1","-f","image2pipe","-vcodec","png","pipe:1"])
        check_pixels(Image.open(io.BytesIO(frame)),expected)
    finally:
        stop(proc)
print("PASS annotation gestures, palette editing and limits, settings restart, package, PNG and MP4 pixels",work)
