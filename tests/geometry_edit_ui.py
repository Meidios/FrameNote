"""Edit existing annotation geometry in actual video and image review windows."""
import hashlib
import io
import json
import math
import os
import uuid
import zipfile

from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop

from review_ui import exported_file, fixtures, launch, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
coordinates = {
    "arrow": (.12, .16, .34, .23), "line": (.55, .16, .78, .22),
    "angle": (.12, .42, .23, .33, .34, .45), "ruler": (.54, .40, .80, .40),
    "box": (.34, .68, .12, .53), "text": (.55, .52, .82, .67),
    "magnifier": (.12, .77, .34, .92),
}

def png(size, color):
    buf = io.BytesIO()
    Image.new("RGB", size, color).save(buf, "PNG")
    return buf.getvalue()

def make_package(image):
    attachment = png((180, 120), "#7896b8")
    asset = hashlib.sha256(attachment).hexdigest()
    media = png((800, 600), "#364650") if image else (fixtures / "vfr.mp4").read_bytes()
    name = "画面.png" if image else "画面.mp4"
    notes = []
    for number, kind in enumerate([*coordinates, "picture"], 1):
        n = dict(id=str(uuid.uuid4()), number=number, startUs=0, endUs=None if image else 2000000,
                 text="文字框" if kind == "text" else kind, shape=None)
        if kind == "picture":
            n["shape"] = dict(kind="box", x=.52, y=.74, toX=.74, toY=.97)
            n["pictures"] = [dict(asset=asset, pixelWidth=180, pixelHeight=120, x=.56, y=.77, width=.15),
                             dict(asset=asset, pixelWidth=180, pixelHeight=120, x=.80, y=.77, width=.10)]
        else:
            n["shape"] = dict(kind=kind, **dict(zip(("x","y","toX","toY","thirdX","thirdY"), coordinates[kind])))
        notes.append(n)
    doc = dict(id=str(uuid.uuid4()), version=2 if image else 1, notes=notes, lastNoteNumber=len(notes),
               fileName=name, durationUs=0 if image else 3000000)
    package = work / (name + ".framenote")
    with zipfile.ZipFile(package, "w") as z:
        z.writestr("review.json", json.dumps(doc))
        z.writestr("images/" + asset + ".png", attachment)
        z.writestr("media/source" + os.path.splitext(name)[1], media)
    return package

def drag(p, x, y, dx, dy):
    p.mouse.move(x, y)
    p.mouse.down()
    p.mouse.move(x+dx, y+dy, steps=5)
    p.mouse.up()

def center(locator):
    box = locator.bounding_box()
    assert box
    return box["x"] + box["width"]/2, box["y"] + box["height"]/2

def near(actual, expected):
    assert abs(actual-expected) < .004, (actual, expected)

def exercise(p):
    wait(p,"!busy && document.querySelectorAll('.inkBadge').length===8")
    p.evaluate("new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))")
    def screen(x, y):
        return overlay["x"]+x*w, overlay["y"]+y*h
    for number, kind in enumerate(coordinates, 1):
        overlay = p.locator("#overlay").bounding_box()
        w, h = overlay["width"], overlay["height"]
        read = lambda: p.evaluate("i=>doc.notes[i].shape", number-1)
        start = read()
        # Pick the body of each object without opening the details editor first.
        x, y = screen((start["x"]+start["toX"])/2, (start["y"]+start["toY"])/2)
        target = p.evaluate("([x,y])=>document.elementFromPoint(x,y)?.closest('[data-note]')?.dataset.note",[x,y])
        assert target==p.evaluate("i=>doc.notes[i].id",number-1),(kind,target,x,y,overlay)
        previous_history = p.evaluate("history.length")
        drag(p, x, y, 9, 6)
        moved = read()
        pairs = [("x","y"), ("toX","toY")]
        if kind == "angle":
            pairs.append(("thirdX","thirdY"))
        if kind=="magnifier":
            # Detaching an old inline lens preserves its original center-half sample.
            for a,b in [("x","toX"),("y","toY")]:
                span=start[b]-start[a]
                near(moved[a],start[a]+span/4);near(moved[b],start[b]-span/4)
            near(moved["preview"]["x"],start["x"]+9/w);near(moved["preview"]["y"],start["y"]+6/h)
        else:
            for a,b in pairs:
                near(moved[a], start[a]+9/w)
                near(moved[b], start[b]+6/h)
        assert p.evaluate("history.length") == previous_history + 1, kind
        p.keyboard.press("Control+z")
        assert read() == start
        p.keyboard.press("Control+Shift+z")
        assert read() == moved
        if kind in ("box","text","magnifier"):
            pairs += [("x","toY"), ("toX","y")]
        assert p.locator(".shapeHandle").count() == len(pairs)
        for a,b in pairs:
            start = read()
            handle = p.locator(f'.shapeHandle[data-point="{a},{b}"]')
            drag(p, *center(handle), 4, -3)
            actual = read()
            near(actual[a], start[a]+4/w)
            near(actual[b], start[b]-3/h)
            assert {k:v for k,v in actual.items() if k not in (a,b)} == {k:v for k,v in start.items() if k not in (a,b)}
        if kind == "ruler":
            p.locator("#rulerLength").fill("1")
            p.locator("#calibrateRuler").click()
            actual=read()
            x,y = center(p.locator('.shapeHandle[data-point="toX,toY"]'))
            p.mouse.move(x,y);p.mouse.down();p.mouse.move(x+16,y-5,steps=3)
            live = p.evaluate("editing.shape")
            sw,sh = p.evaluate("sourceSize()")
            def length(s):return math.hypot((s["toX"]-s["x"])*sw,(s["toY"]-s["y"])*sh)
            assert f"{length(live)/length(actual):.6g} m" in p.locator("#ink").text_content()
            assert read() == actual and live != actual
            p.evaluate("""overlay.dispatchEvent(new PointerEvent('pointercancel',
                       {pointerId:geometryDrag.pointer,bubbles:true}))""")
            p.mouse.up()
            assert read() == p.evaluate("editing.shape") == actual
        if kind == "text":
            p.mouse.dblclick(*screen((actual["x"]+actual["toX"])/2, (actual["y"]+actual["toY"])/2))
            assert p.locator("#body").evaluate("el=>document.activeElement===el")
            p.keyboard.insert_text("修订文字")
            assert p.locator("#ink").text_content().find("修订文字") >= 0
        p.keyboard.press("Escape")

    pic = p.locator("#ink image.picture").first
    combined = p.evaluate("doc.notes.at(-1)")
    before = combined["pictures"][0]
    drag(p, *center(pic), -8, 4)
    moved = p.evaluate("doc.notes.at(-1).pictures[0]")
    near(moved["x"], before["x"]-8/w)
    near(moved["y"], before["y"]+4/h)
    for a,b in ((0,0),(0,1),(1,0),(1,1)):
        old = pic.bounding_box()
        drag(p, *center(p.locator(f'.pictureHandle[data-corner="{a},{b}"]')), 6 if a else -6, 4 if b else -4)
        new = pic.bounding_box()
        assert new["width"] > old["width"]+2
        near(new["width"]/new["height"], 1.5)
        assert abs(new["x"]+(1-a)*new["width"]-old["x"]-(1-a)*old["width"]) < 1
        assert abs(new["y"]+(1-b)*new["height"]-old["y"]-(1-b)*old["height"]) < 1
    assert p.evaluate("doc.notes.at(-1).shape")==combined["shape"]
    assert p.evaluate("doc.notes.at(-1).pictures[1]")==combined["pictures"][1]

    # Escape rolls back an uncommitted move, including the live geometry.
    old = p.evaluate("doc.notes")
    x,y = center(pic)
    p.mouse.move(x,y);p.mouse.down();p.mouse.move(x+14,y-7,steps=3)
    p.keyboard.press("Escape");p.mouse.up()
    assert old == p.evaluate("doc.notes") and old[-1]["pictures"] == p.evaluate("editing.pictures")
    p.keyboard.press("Escape")

    # Move all three angle vertices together against the top-left boundary.
    s = old[2]["shape"]
    x,y = screen((s["x"]+s["toX"])/2, (s["y"]+s["toY"])/2)
    drag(p, x, y, -w, -h)
    result = p.evaluate("doc.notes[2].shape")
    for a,b in (("x","y"),("toX","toY"),("thirdX","thirdY")):
        near(result[a], s[a]-min(s["x"],s["toX"],s["thirdX"]))
        near(result[b], s[b]-min(s["y"],s["toY"],s["thirdY"]))
    p.keyboard.press("Control+z")
    assert p.evaluate("doc.notes")==old

with sync_playwright() as pw:
    for image in (False, True):
        package = make_package(image)
        ready = "typeof imageDoc==='function' && imageDoc() && $('openedImage').naturalWidth===800" if image else "typeof ready!=='undefined' && ready"
        proc,p = launch(pw, package, ready)
        errors = []
        p.on("pageerror", lambda e: errors.append(str(e)))
        p.on("console", lambda msg: errors.append(msg.text) if msg.type=="error" else None)
        try:
            if not image:
                p.evaluate("seekUs(0)")
                wait(p,"v.paused && !v.seeking && shown<100000")
            exercise(p)
            if image:
                p.locator(".note .stamp").nth(2).click()
                p.screenshot(path=str(work/"angle-handles.png"))
                p.mouse.move(*center(p.locator("#stage")));p.mouse.wheel(0,-240)
                wait(p,"$('stage').classList.contains('pannable')")
                p.locator(".note .stamp").nth(5).click()
                old = p.evaluate("doc.notes[5].shape")
                w = p.locator("#overlay").bounding_box()["width"]
                drag(p, *center(p.locator('.shapeHandle[data-point="x,y"]')), 14, 0)
                near(p.evaluate("doc.notes[5].shape.x"), old["x"]+14/w)
                p.screenshot(path=str(work/"edited-text.png"))
            assert not errors, errors
            expected = p.evaluate("doc.notes")
            dest = work / ("edited-" + package.name)
            p.keyboard.press("Control+s")
            dest=exported_file(proc)
            wait(p,"!busy && $('status').textContent.startsWith('审阅包已保存')")
        finally:
            stop(proc)
        proc,p = launch(pw, dest, ready)
        try:
            assert p.evaluate("doc.notes")==expected
            assert p.title()=="FrameNote" and p.locator("#list .note").count()==8
        finally:
            stop(proc)
print("PASS: all eight geometry types, every handle, bounds, cancel, undo/redo, zoomed image, edited text and package reopen.",work)
