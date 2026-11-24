"""Edit measurements and colors using the same menus on images and videos."""
import json
import math
import os
import time
import uuid
import zipfile
import psutil
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop, mouse
from pywinauto.timings import wait_until
from review_ui import exported_file, fixtures, launch, stop, wait, work

profile=work/"profile"
profile.mkdir()
(profile/"updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
image=work/"sheet.png"
Image.new("RGB",(800,480),"#303438").save(image)

def fixture(media,picture):
    shapes=[dict(kind="angle",x=.1,y=.36,toX=.35,toY=.36,thirdX=.35,thirdY=.17),
            dict(kind="ruler",x=.12,y=.65,toX=.52,toY=.65,rulerFrame=0),
            dict(kind="ruler",x=.68,y=.8,toX=.88,toY=.8,rulerFrame=0)]
    notes=[dict(id=str(uuid.uuid4()),number=i+1,startUs=0,endUs=None,color="#ffd886",shape=s,
                text="手写说明" if i==2 else "") for i,s in enumerate(shapes)]
    doc=dict(version=2 if picture else 1,id=str(uuid.uuid4()),fileName=media.name,durationUs=0 if picture else 3000000,
             notes=notes,lastNoteNumber=3)
    path=work/(media.stem+".framenote")
    with zipfile.ZipFile(path,"w") as z:
        z.writestr("review.json",json.dumps(doc));z.write(media,"media/source"+media.suffix)
    return path

def save_package(proc,p,path):
    p.keyboard.press("Control+s")
    exported_file(proc,path)
    wait(p,"!busy && $('status').textContent.startsWith('审阅包已保存')")

def pick_dark_color(proc,p,button,index):
    previous=p.evaluate("i=>doc.notes[i].color",index)
    steps=p.evaluate("history.length")
    control=p.locator(button)
    control.scroll_into_view_if_needed()
    bounds=control.bounding_box()
    host=Desktop(backend="win32").window(process=proc.pid,title_re=".*FrameNote.*",found_index=0)
    view=host.child_window(class_name="Chrome_RenderWidgetHostHWND",found_index=0).rectangle()
    scale=p.evaluate("devicePixelRatio")
    anchor_x=view.left+(bounds["x"]+bounds["width"]/2)*scale
    anchor_y=view.top+(bounds["y"]+bounds["height"]/2)*scale
    control.click()
    children={c.pid for c in psutil.Process(proc.pid).children(recursive=True)}
    popup=Desktop(backend="win32").window(class_name="Chrome_WidgetWin_1",visible_only=True,
            predicate_func=lambda e:e.process_id in children and e.rectangle.width()>100)
    popup.wait("visible")
    # WebView2 exposes the popup window before the saturation gradient is painted.
    def picker_painted():
        img=popup.capture_as_image().convert("RGB")
        y=round(.44*img.height)
        return img.getpixel((img.width//4,y))!=img.getpixel((3*img.width//4,y))
    wait_until(3,.05,picker_painted)
    r=popup.rectangle()
    # Screen-edge clamping can flip the picker above or to the left of its button.
    slack=(bounds["height"]/2+6)*scale
    assert r.left-slack<=anchor_x<=r.right+slack and r.top-slack<=anchor_y<=r.bottom+slack,(button,anchor_x,anchor_y,r)
    for x in (.35,.60 if button=="#customColor" else .80):
        mouse.click(coords=(r.left+round(x*r.width()),r.top+round(.44*r.height())))
        wait(p,"([i,last])=>doc.notes[i].color!==last && doc.notes[i].color===$('noteColorPicker').value",[index,previous])
        previous=p.locator("#noteColorPicker").input_value()
    color=previous
    p.evaluate("refreshDetails()")
    assert p.locator("#noteColorPicker").input_value()==color
    assert p.locator("#customColor").get_attribute("aria-pressed")=="true"
    assert p.locator("#list .note").nth(index).evaluate("row=>row.style.getPropertyValue('--note')")==color
    assert p.evaluate("i=>$('ink').querySelector('g[data-note=\"'+doc.notes[i].id+'\"]').getAttribute('color')",index)==color
    if not p.evaluate("imageDoc()"):
        note_id=p.evaluate("i=>doc.notes[i].id",index)
        assert p.locator('#markLanes [data-id="'+note_id+'"]').evaluate("mark=>mark.style.getPropertyValue('--mark')")==color
    frame=Desktop(backend="win32").window(process=proc.pid,title_re=".*FrameNote.*",found_index=0).rectangle()
    x=next(x for x in (frame.left+32,frame.right-32) if not r.left<=x<r.right)
    mouse.click(coords=(x,frame.bottom-20))
    assert p.evaluate("history.length")==steps+1
    return color

with sync_playwright() as pw:
    for media,picture in [(image,True),(fixtures/"vfr.mp4",False)]:
        ready="typeof imageDoc==='function' && imageDoc() && $('openedImage').naturalWidth" if picture else "typeof ready!=='undefined' && ready"
        proc,p=launch(pw,fixture(media,picture),ready)
        saved=work/("saved-"+media.stem+".framenote")
        try:
            p.evaluate("transport.pause()")
            errors=[];p.on("pageerror",lambda e:errors.append(str(e)))
            ids=p.evaluate("doc.notes.map(n=>n.id)")
            def label(i):
                return p.locator('#ink > g[data-note="'+ids[i]+'"] .measurementLabel')
            def menu(i):
                label(i).click(button="right")
                assert p.locator("#menuNoteName").inner_text()==f"批注 #{i+1}"
                assert p.locator("#menuTarget").bounding_box()["y"]<p.locator("#menuCreate").bounding_box()["y"]
                assert p.locator("#canvasMenu button:visible:not([data-color])").evaluate_all(
                    "buttons=>buttons.every(b=>document.getElementById(b.querySelector('use')?.getAttribute('href')?.slice(1)))")
            # Right-click a different note from the one selected in the sidebar.
            p.locator(".note").nth(0).click()
            menu(1)
            p.locator("#menuValue").fill("20")
            unit=p.locator("#menuUnit")
            unit.click()
            wait(p,"$('menuUnit').matches(':open')")
            p.keyboard.press("Escape")
            assert p.locator("#canvasMenu").is_visible() and not unit.evaluate("s=>s.matches(':open')")
            unit.click()
            unit.locator('option[value="cm"]').click()
            p.locator("#menuApply").click()
            assert label(1).text_content()=="20 cm" and label(2).text_content()=="10 cm"
            assert p.locator(".noteBody").nth(2).text_content()=="手写说明"
            menu(1)
            p.locator("#menuUnit").select_option("mm")
            assert p.locator("#menuValue").input_value()=="200"
            p.locator("#menuValue").press("Enter")
            assert label(1).text_content()=="200 mm" and label(2).text_content()=="100 mm"
            p.keyboard.press("Control+z")
            assert label(1).text_content()=="20 cm"
            menu(1);p.locator("#menuValue").fill("0");p.locator("#menuApply").click()
            assert p.locator("#canvasMenu").is_visible() and label(1).text_content()=="20 cm"
            p.keyboard.press("Escape")
            before=p.evaluate("doc.notes[0].shape")
            menu(0)
            assert p.locator("#menuUnit").is_hidden() and float(p.locator("#menuValue").input_value())==90
            p.locator("#menuValue").fill("190");p.locator("#menuValue").press("Enter")
            assert p.locator("#canvasMenu").is_visible() and p.evaluate("doc.notes[0].shape")==before
            p.locator("#menuValue").fill("37.5");p.locator("#menuValue").press("Enter")
            after=p.evaluate("doc.notes[0].shape")
            assert [after[k] for k in ("x","y","toX","toY")]==[before[k] for k in ("x","y","toX","toY")]
            w,h=p.evaluate("sourceSize()")
            arm=lambda s:math.hypot((s["thirdX"]-s["toX"])*w,(s["thirdY"]-s["toY"])*h)
            assert abs(arm(after)-arm(before))<.001
            assert label(0).text_content()=="37.5°" and p.locator(".noteBody").nth(0).text_content()=="角度 37.5°"
            p.locator("#body").fill("保留手写角度")
            for degree in [0,180,75.5]:
                p.locator("#angleDegrees").fill(str(degree));p.locator("#applyAngle").click()
                assert label(0).text_content()==f"{degree:.1f}°"
            assert p.locator("#body").input_value()=="保留手写角度"
            custom=pick_dark_color(proc,p,"#customColor",0)
            assert p.locator(".noteNumber").first.evaluate("el=>getComputedStyle(el).color")=="rgb(255, 255, 255)"
            p.keyboard.press("Control+z");assert p.evaluate("doc.notes[0].color")=="#ffd886"
            p.keyboard.press("Control+Shift+z");assert p.evaluate("doc.notes[0].color")==custom
            menu(1)
            assert p.locator("#menuCustomColor").get_attribute("aria-checked")=="false"
            other=pick_dark_color(proc,p,"#menuCustomColor",1)
            assert other!=custom and p.evaluate("doc.notes[0].color")==custom
            p.keyboard.press("Control+z");assert p.evaluate("doc.notes[1].color")=="#ffd886"
            p.keyboard.press("Control+Shift+z");assert p.evaluate("doc.notes[1].color")==other
            menu(1)
            assert p.locator("#menuCustomColor").get_attribute("aria-checked")=="true"
            custom_buttons=p.locator("#customColor, #menuCustomColor")
            assert custom_buttons.count()==2 and custom_buttons.all_inner_texts()==["",""]
            detail_style,menu_style=custom_buttons.evaluate_all("""buttons=>buttons.map(b=>{
                const s=getComputedStyle(b), svg=b.querySelector('svg');
                return [b.offsetWidth,b.offsetHeight,s.borderRadius,s.outline,s.outlineOffset,
                        s.backgroundColor,s.color,svg.clientWidth,svg.clientHeight,svg.querySelector('use').getAttribute('href')];
            })""")
            assert menu_style==detail_style and detail_style[:3]==[23,23,"50%"],(detail_style,menu_style)
            p.locator("#canvasMenu").screenshot(path=str(work/(media.stem+"-menu.png")));p.keyboard.press("Escape")
            expect=p.evaluate("[doc.notes,doc.rulerScales]")
            save_package(proc,p,saved)
            assert not errors,errors
        finally:stop(proc)
        proc,p=launch(pw,saved,ready)
        try:
            assert p.evaluate("[doc.notes,doc.rulerScales]")==expect
        finally:stop(proc)
        print("PASS:",media.name,"targeted ruler, unit conversion, angle geometry, custom color, undo/redo and package reopen.",flush=True)
