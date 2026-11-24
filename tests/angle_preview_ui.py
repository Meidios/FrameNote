"""Verify each stage of three-point angle drawing with a real mouse in both review modes."""
import os
from PIL import Image
from playwright.sync_api import sync_playwright
from review_ui import fixtures, launch, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
picture = work / "angle.png"
Image.new("RGB", (800, 600), "#32363c").save(picture)


def check_preview(page, anchors, points, degree=None):
    wait(page, """([anchors,points,degree])=>{
        const g=$('measurementPreview'),r=overlay.getBoundingClientRect();
        const near=(node,x,y,at)=>Math.hypot(
            r.left+parseFloat(node.getAttribute(x))*r.width/overlay.clientWidth-at[0],
            r.top+parseFloat(node.getAttribute(y))*r.height/overlay.clientHeight-at[1])<1.5;
        const dots=[...g.querySelectorAll('circle')],lines=[...g.querySelectorAll('line:not(.inkHit)')];
        const label=g.querySelector('text');
        return dots.length===anchors.length && dots.every((dot,i)=>near(dot,'cx','cy',anchors[i])) &&
            lines.length===points.length-1 && lines.every((l,i)=>
                near(l,'x1','y1',points[i]) && near(l,'x2','y2',points[i+1])) &&
            (degree===null?!label:label && Math.abs(parseFloat(label.textContent)-degree)<.2 && !!g.querySelector('path')) &&
            doc.notes.length===0 && doc.lastNoteNumber===0 && history.length===0;
    }""", [anchors, points, degree])


with sync_playwright() as pw:
    for image in (False, True):
        source = picture if image else fixtures / "vfr.mp4"
        ready = "typeof imageDoc==='function' && imageDoc() && $('openedImage').naturalWidth" if image else "typeof ready!=='undefined' && ready"
        proc, p = launch(pw, source, ready)
        try:
            if not image:
                p.evaluate("transport.pause()")
            box = p.locator("#overlay").bounding_box()
            x, y = round(box["x"]+box["width"]*.48), round(box["y"]+box["height"]*.55)
            d = round(min(box["width"], box["height"])*.20)
            a, b, c = (x-d,y), (x,y), (x,y-d)
            for clicks in (1,2):
                p.locator("#angle").click()
                p.mouse.click(*a)
                if clicks==2:
                    p.mouse.click(*b)
                p.mouse.move(*c)
                p.keyboard.press("Escape")
                wait(p,"!tool && !$('measurementPreview').childElementCount && !doc.notes.length && !history.length")

            if image:
                p.mouse.click(*b,button="right")
                p.locator('#menuCreate [data-action="angle"]').click()
            else:
                p.locator("#angle").click()
            p.mouse.click(*a)
            check_preview(p,[a],[a,a])
            p.mouse.move(*b)
            check_preview(p,[a],[a,b])
            p.mouse.click(*b)
            check_preview(p,[a,b],[a,b])
            p.mouse.move(*c)
            check_preview(p,[a,b],[a,b,c],90)
            diagonal=(x-d,y-d)
            p.mouse.move(*diagonal)
            check_preview(p,[a,b],[a,b,diagonal],45)
            p.mouse.click(*c)
            wait(p,"!tool && !$('measurementPreview').childElementCount && doc.notes.length===1 && doc.notes[0].text==='角度 90.0°'")
            shape=p.evaluate("doc.notes[0].shape")
            p.mouse.move(*diagonal)
            assert p.evaluate("doc.notes[0].shape")==shape
            assert p.locator("#ink [data-shape] text").text_content()=="90.0°"
            p.keyboard.press("Control+z")
            assert p.evaluate("doc.notes.length")==0
            p.keyboard.press("Control+Shift+z")
            assert p.evaluate("doc.notes[0].shape")==shape
            print("PASS:",source.name,"anchors, both tracking lines, 90°/45° preview, final click, cancellation and undo/redo.",flush=True)
        finally:
            stop(proc)
