"""Exercise toolbar lighting, hint placement and native Player popups on Windows."""
import io
import os
import time
from PIL import Image, ImageChops
from playwright.sync_api import sync_playwright
from pywinauto import Desktop, mouse
from review_ui import fixtures, launch, stop, wait, work

os.environ["FRAMENOTE_DATA_DIR"]=str(work/"profile")
picture=work/"sample.png"
Image.new("RGB",(400,240),"#466477").save(picture)

def snapshot(locator):
    return Image.open(io.BytesIO(locator.screenshot())).convert("RGB")

def check_hint(page,button):
    wait(page,"id=>$('buttonHint').classList.contains('visible') && $('buttonHint').textContent===$(id).ariaDescription",button)
    r=page.locator("#buttonHint").bounding_box()
    assert r["x"]>=0 and r["y"]>=0 and r["x"]+r["width"]<=page.evaluate("innerWidth")+1
    return r

with sync_playwright() as pw:
    proc,p=launch(pw,fixtures/"vfr.mp4")
    try:
        p.mouse.move(2,2)
        arrow=p.locator("#arrow")
        box=p.locator("#box")
        size=box.bounding_box()
        baseline=snapshot(arrow)
        box.hover(position={"x":2,"y":12})
        r=check_hint(p,"box")
        assert r["y"]+r["height"]<size["y"]
        p.wait_for_timeout(160)
        left=snapshot(arrow)
        assert ImageChops.difference(left,baseline).getbbox(), "Neighbor never lit up"
        box.hover(position={"x":int(size["width"])-3,"y":12})
        assert ImageChops.difference(left,snapshot(arrow)).getbbox(), "Light did not follow pointer"
        assert box.bounding_box()==size
        p.locator("#text").hover()
        check_hint(p,"text")
        p.keyboard.press("Escape")
        wait(p,"!$('buttonHint').classList.contains('visible')")
        p.mouse.move(2,2)
        p.wait_for_timeout(180)
        assert not ImageChops.difference(snapshot(arrow),baseline).getbbox()
        p.locator("#vlc").click()
        handle=Desktop(backend="win32").window(process=proc.pid,title_re=".*FrameNote.*",found_index=0).handle
        window=Desktop(backend="uia").window(handle=handle)
        previous=window.child_window(auto_id="Previous",control_type="Button")
        previous.wait("visible",timeout=10)
        window.set_focus()
        bounds=previous.rectangle()
        mouse.move(coords=(bounds.mid_point().x,bounds.mid_point().y))
        time.sleep(.35)
        hints=[w for w in Desktop(backend="win32").windows(process=proc.pid)
               if w.handle!=handle and w.is_visible() and w.rectangle().bottom<=bounds.top]
        assert hints, "Player hint did not open above the toolbar"
        label=Desktop(backend="uia").window(handle=hints[0].handle).descendants(control_type="Text")
        assert any("上一帧" in item.window_text() for item in label)
    finally:stop(proc)
    proc,p=launch(pw,picture,"typeof imageDoc==='function' && imageDoc() && !busy")
    try:
        p.set_viewport_size({"width":824,"height":506})
        p.locator("#box").hover()
        r=check_hint(p,"box")
        assert r["y"]>=p.locator("#box").bounding_box()["height"]
        p.wait_for_timeout(160)
        panel=p.locator("#reviewPanel").bounding_box()
        p.screenshot(path=str(work/"hover-preview.png"),clip={**panel,"height":125})
        p.locator("#clear").hover()
        assert p.locator("#clear").is_disabled()
        assert p.locator("#clear").evaluate("b=>getComputedStyle(b,'::after').opacity==='0'")
        p.locator("#box").click()
        assert p.evaluate("tool==='box'")
        p.keyboard.press("Escape")
        p.locator("#stage").press("m")
        assert p.locator("#list .note").count()==1
        print("PASS toolbar spotlight, hint switches, edge placement, disabled state, Player popup and shortcuts.",work)
    finally:stop(proc)
