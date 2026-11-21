"""Check on-canvas hover details in the native video and image review windows."""
import json
import os
import uuid
import zipfile

from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import at, fixtures, launch, stop, wait, work

os.environ["FRAMENOTE_DATA_DIR"] = str(work / "profile")


def note(number, text, color, kind, points, end=None):
    shape = dict(kind=kind, **dict(zip(("x", "y", "toX", "toY", "thirdX", "thirdY"), points)))
    return dict(id=str(uuid.uuid4()), number=number, startUs=400000, endUs=end,
                text=text, color=color, shape=shape)


def package(source, notes, image=False):
    review = dict(id=str(uuid.uuid4()), version=2 if image else 1, durationUs=0 if image else 3000000,
                  fileName=source.name, notes=notes, lastNoteNumber=len(notes))
    path = work / (source.stem + ".framenote")
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr("review.json", json.dumps(review))
        archive.write(source, "media/source" + source.suffix)
    return path


def move(page, x, y):
    rect = page.locator("#overlay").bounding_box()
    page.mouse.move(rect["x"] + x * rect["width"], rect["y"] + y * rect["height"])


def check_card(page, number, stamp, text):
    wait(page, "n=>!$('notePreview').hidden && $('hoverNumber').textContent==='#'+n", number)
    assert page.locator("#hoverStamp").text_content() == stamp
    assert page.locator("#hoverText").text_content() == text
    assert page.locator(".note.hovered .noteNumber").text_content() == f"#{number}"
    assert page.evaluate("""()=>{const card=$('notePreview').getBoundingClientRect(),stage=$('stage').getBoundingClientRect();
        return card.left>=stage.left && card.right<=stage.right && card.top>=stage.top && card.bottom<=stage.bottom}""")
    assert page.locator(".inkBadge").evaluate_all("""(badges,number)=>badges.every(b=>
        getComputedStyle(b).opacity===(b.textContent==='#'+number?'0':'1'))""", number)


def check_box_alignment(page):
    frame = page.locator('#ink rect[stroke-width="3"]:not(.inkHit)').bounding_box()
    card = page.locator("#notePreview").bounding_box()
    assert abs(card["x"] - frame["x"]) <= 1, (card, frame)
    assert abs(frame["y"] - (card["y"] + card["height"]) - 3) <= 1, (card, frame)


def save_png(page, proc, name):
    path = work / name
    # Invoke the normal Save action without moving the mouse off the drawing.
    page.locator("#saveImage").evaluate("button=>button.click()")
    dialog = Desktop(backend="win32").window(process=proc.pid, class_name="#32770")
    dialog.wait("ready", timeout=10)
    dialog.child_window(class_name="Edit", found_index=0).set_edit_text(str(path))
    dialog.child_window(control_id=1).click()
    wait(page, "name=>!busy && $('status').textContent.endsWith(name)", name)
    return path.read_bytes()


notes = [
    note(1, "让撞击声对齐箭头所指的动作。", "#8ddcff", "arrow", (.12, .30, .40, .54)),
    note(2, "检查框内的电平变化。\n保留尾音。", "#ffaed8", "box", (.40, .54, .70, .82), 1600000),
    note(3, "复核混响尾音。\n" * 80 + "最后一行", "#ffd886", "line", (.82, .04, .97, .04)),
    note(4, "", "#8de3af", "angle", (.68, .30, .76, .36, .84, .20)),
]
with sync_playwright() as pw:
    proc, page = launch(pw, package(fixtures / "vfr.mp4", notes))
    try:
        page.locator("#marks button").first.click()
        wait(page, "document.querySelectorAll('.inkBadge').length===4 && !v.seeking")
        state = page.evaluate("[selected,v.currentTime,v.paused]")
        move(page, .26, .42)
        check_card(page, 1, "00:00.400　点", notes[0]["text"])
        move(page, .40, .57)
        check_card(page, 2, "00:00.400 – 00:01.600", notes[1]["text"])
        check_box_alignment(page)
        assert page.evaluate("[selected,v.currentTime,v.paused]") == state
        assert page.locator("#hoverNumber").evaluate("e=>getComputedStyle(e).backgroundColor") == "rgb(255, 174, 216)"
        page.locator("#stage").screenshot(path=str(work / "hover-preview.png"))
        move(page, .80, .28)
        check_card(page, 4, "00:00.400　点", "角度")
        page.locator(".inkBadge").filter(has_text="#3").hover()
        check_card(page, 3, "00:00.400　点", notes[2]["text"])
        page.locator("#hoverText").hover()
        page.mouse.wheel(0, 4000)
        wait(page, "$('hoverText').scrollTop>0 && !$('notePreview').hidden")
        move(page, .08, .9)
        wait(page, "$('notePreview').hidden && !document.querySelector('.note.hovered')")
        assert page.locator(".inkBadge").evaluate_all("badges=>badges.every(b=>getComputedStyle(b).opacity==='1')")
        window_width = page.evaluate("innerWidth")
        page.locator("#fullPreview").click()
        wait(page, "isFullPreview() && innerWidth>=screen.availWidth-2")
        page.locator("#ink rect.inkHit").hover()
        check_card(page, 2, "00:00.400 – 00:01.600", notes[1]["text"])
        check_box_alignment(page)
        page.keyboard.press("Escape")
        wait(page, "w=>!isFullPreview() && Math.abs(innerWidth-w)<2", window_width)
        at(page, page.evaluate("index(1000000)"))
        page.locator("#stage").focus()
        move(page, .40, .65)
        check_card(page, 2, "00:00.400 – 00:01.600", notes[1]["text"])
        page.keyboard.press("Space")
        wait(page, "shown>=1600000 && !v.paused && $('notePreview').hidden")
    finally:
        stop(proc)

    source = work / "image.png"
    Image.new("RGB", (800, 450), "#45484d").save(source)
    region = dict(notes[1], number=1, startUs=0, endUs=None)
    proc, page = launch(pw, package(source, [region], image=True),
                        "typeof imageDoc==='function' && imageDoc() && $('openedImage').naturalWidth===800 && !busy")
    try:
        baseline = save_png(page, proc, "plain.png")
        move(page, .40, .65)
        check_card(page, 1, "", region["text"])
        check_box_alignment(page)
        assert save_png(page, proc, "hover.png") == baseline
        move(page, .40, .65)
        page.mouse.wheel(0, -480)
        wait(page, "$('stage').classList.contains('pannable')")
        move(page, .40, .61)
        check_card(page, 1, "", region["text"])
        page.locator(".inkBadge").first.click()
        page.locator("#body").fill("修改后的批注")
        move(page, .40, .61)
        check_card(page, 1, "", "修改后的批注")
        assert page.evaluate("document.activeElement===$('body')")
        page.locator("#line").click()
        move(page, .40, .61)
        assert page.locator("#notePreview").is_hidden()
    finally:
        stop(proc)
print("PASS: video and image hover details, scrolling, selection, fullscreen, playback, drawing and PNG export.", work)
