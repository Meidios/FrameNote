"""Exercise click-click and press-drag rulers and their shared Windows unit preference."""
import json
import os
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import at, fixtures, launch, settings, stop, wait, work

profile=work/"profile"
profile.mkdir()
(profile/"updates.json").write_text('{"automatic":false}')
(profile/"shortcuts.json").write_text('{"splitClip":{"mods":0,"vk":87}}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
picture=work/"ruler.png"
Image.new("RGB",(800,600),"#303438").save(picture)
units_per_pixel={"mm":25.4/96,"cm":2.54/96,"m":.0254/96,"px":1}


def choose_unit(proc,page,label,expected,save=True):
    dialog=settings(proc,"批注工具")
    dialog.child_window(auto_id="DefaultRulerUnit",control_type="ComboBox").select(label)
    dialog.child_window(title="保存" if save else "取消",control_type="Button",depth=5).invoke()
    Desktop(backend="win32").window(process=proc.pid,title="设置").wait_not("exists")
    wait(page,"u=>defaultRulerUnit===u",expected)


def measure(p,width,y,drag=False,calibrated=False,context_menu=False):
    if context_menu:
        menu_item=p.locator('#menuCreate [data-action="ruler"]')
        # An initial native window resize dismisses an already open canvas menu.
        for _ in range(3):
            p.locator("#openedImage").click(button="right")
            if menu_item.is_visible():
                break
        menu_item.click(timeout=3000)
    else:
        p.locator("#toolRuler").click()
    bounds=p.locator("#overlay").bounding_box()
    x=round(bounds["x"]+bounds["width"]*.22)
    y=round(bounds["y"]+bounds["height"]*y)
    count=p.locator("#list .note").count()
    p.mouse.move(x,y)
    p.mouse.down()
    if not drag:
        p.mouse.up()
    for distance in (width//2,width):
        p.mouse.move(x+distance,y,steps=3)
        wait(p,"""([count,endX])=>{
            const g=$('measurementPreview'),line=g.querySelector('.inkShape');
            if(!line||!g.querySelector('circle')||!g.querySelector('text'))return false;
            const r=overlay.getBoundingClientRect();
            return doc.notes.length===count && Math.abs(r.left+parseFloat(line.getAttribute('x2'))*r.width/overlay.clientWidth-endX)<1.5;
        }""",[count,x+distance])
        label=p.locator("#measurementPreview text").text_content()
        if calibrated:
            # The extra timeline lanes for coincident Marks can resize the video between strokes.
            span=p.evaluate("Math.abs(doc.notes[0].shape.toX-doc.notes[0].shape.x)")
            current_width=p.locator("#overlay").bounding_box()["width"]
            cm=100*distance/current_width/span
            assert label.endswith(" cm") and abs(float(label.split()[0])-cm)<.01,(label,cm)
        else:
            pixels=p.evaluate("sourceSize()[0]")*distance/bounds["width"]
            value,unit=label.split()
            assert unit==p.evaluate("defaultRulerUnit") and abs(float(value)/units_per_pixel[unit]-pixels)<.2,label
    if drag:
        p.mouse.up()
    else:
        p.mouse.click(x+width,y)
    wait(p,"n=>!tool && !$('measurementPreview').childElementCount && doc.notes.length===n+1",count)
    assert p.evaluate("doc.notes.at(-1).text")=="距离标尺 "+label
    p.mouse.move(x+width+8,y-8)
    assert p.locator("#body").input_value()=="距离标尺 "+label


with sync_playwright() as pw:
    for image in (False,True):
        ready="typeof imageDoc==='function' && imageDoc() && $('openedImage').naturalWidth" if image else "typeof ready!=='undefined' && ready"
        proc,p=launch(pw,picture if image else fixtures/"vfr.mp4",ready)
        try:
            host=Desktop(backend="win32").window(process=proc.pid,title_re=".*FrameNote.*",found_index=0)
            host.maximize()
            host.set_focus()
            if not image:
                at(p,3)
            unit="px" if image else "m"
            wait(p,"u=>canAnnotate() && defaultRulerUnit===u && overlay.clientWidth>0",unit)
            for key,label in [("px","96 px"),("mm","25.4 mm"),("cm","2.54 cm"),("m","0.0254 m")]:
                assert p.evaluate("u=>rulerLabel({x:0,y:0,toX:.25,toY:0},null,384,216,u)",key)==label
            measure(p,120,.3,context_menu=image)
            assert p.locator("#rulerUnit").input_value()==unit
            if not image:
                p.locator("#rulerLength").fill("100")
                choose_unit(proc,p,"厘米（cm）","cm")
                assert p.locator("#rulerLength").input_value()=="100"
                live=p.locator("#ink [data-shape] text").text_content()
                assert live.endswith(" cm") and p.locator("#body").input_value()==p.locator(".noteBody").inner_text()=="距离标尺 "+live
            else:
                p.locator("#rulerUnit").select_option("cm")
                p.locator("#rulerLength").fill("100")
            assert p.locator("#rulerUnit").input_value()=="cm"
            p.locator("#calibrateRuler").click()
            if not image:
                choose_unit(proc,p,"毫米（mm）","mm")
                choose_unit(proc,p,"像素（px）","px")
                choose_unit(proc,p,"米（m）","px",save=False)
                assert p.locator("#rulerUnit").input_value()=="cm"
                saved=json.loads((profile/"shortcuts.json").read_text())
                assert saved["defaultRulerUnit"]=="px" and saved["splitClip"][0]["vk"]==87
            measure(p,60,.53,drag=True,calibrated=True)
            measure(p,90,.72,calibrated=True)
            p.locator("#toolRuler").click()
            p.locator("#overlay").click(position={"x":100,"y":90})
            p.keyboard.press("Escape")
            assert p.evaluate("!tool && doc.notes.length===3 && !$('measurementPreview').childElementCount")
            if not image:
                at(p,9)
                measure(p,120,.3)
                assert p.locator("#rulerUnit").input_value()=="px"
                p.locator("#body").fill("保留手写距离")
                p.evaluate("setDefaultRulerUnit('mm')")
                assert p.locator("#ink [data-shape] text").last.text_content().endswith(" mm")
                assert p.locator("#body").input_value()=="保留手写距离"
                p.evaluate("setDefaultRulerUnit('px')")
                p.locator("#rulerLength").fill("96")
                p.locator("#calibrateRuler").click()
                assert p.locator("#ink [data-shape] text").last.text_content()=="96 px"
                assert p.locator("#body").input_value()=="保留手写距离"
            print("PASS:","image" if image else "video","click-click, drag, live lengths, retained frame calibration, cancellation and default units.",flush=True)
        finally:
            stop(proc)
