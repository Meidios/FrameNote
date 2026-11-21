"""Exercise tool-first Marks and targeted canvas actions in the shipping WebView."""
import os
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import at, fixtures, launch, notes, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
picture = work / "reference.png"
Image.new("RGB", (60, 40), "#dd8060").save(picture)
package = work / "actions.framenote"

def spot(page, x, y):
    bounds = page.locator("#overlay").bounding_box()
    return bounds["x"]+x*bounds["width"], bounds["y"]+y*bounds["height"]

def draw(page):
    page.mouse.move(*spot(page, .2, .3))
    page.mouse.down()
    page.mouse.move(*spot(page, .6, .65), steps=4)
    page.mouse.up()

def target(page, note):
    badge=page.locator('.inkBadge[data-note="'+note["id"]+'"]')
    badge.click(button="right")
    assert page.locator("#menuNoteName").inner_text() == "批注 #"+str(note["number"]), (note["number"],page.locator("#canvasMenu").inner_text())

with sync_playwright() as pw:
    proc,p = launch(pw,fixtures/"vfr.mp4")
    try:
        at(p,7)
        clock=p.evaluate("stamp()")
        p.locator("#box").click()
        p.keyboard.press("Escape")
        assert not notes(p)
        p.locator("#box").click()
        draw(p)
        first=notes(p)[0]
        assert first["startUs"]==clock and first["endUs"] is None and first["shape"]["kind"]=="box"
        target(p, first)
        p.locator('[data-action="edit"]').click()
        p.locator("#body").fill("Original")
        p.locator("#noteDuration").fill("1.5")
        p.locator("#noteDuration").press("Tab")
        p.keyboard.press("Control+Enter")
        at(p,15)
        later=p.evaluate("stamp()")
        p.mouse.click(*spot(p,.8,.8), button="right")
        assert p.locator("#menuTarget").is_hidden()
        p.locator('[data-action="arrow"]').click()
        draw(p)
        a,b=notes(p)
        assert a["text"]=="Original" and a["endUs"]==clock+1500000
        assert b["startUs"]==later and b["endUs"] is None
        p.mouse.click(*spot(p,.2,.48),button="right")
        assert p.locator("#menuNoteName").inner_text()=="批注 #1"
        p.locator('#menuColors [aria-label="珊瑚"]').click()
        assert notes(p)[0]["color"]=="#ff9d97" and notes(p)[1]==b
        target(p,a)
        with p.expect_file_chooser() as chooser:
            p.locator('[data-action="attach"]').click()
        chooser.value.set_files(str(picture))
        wait(p,"doc.notes[0].pictures?.length===1")
        target(p,a)
        p.locator('[data-action="duplicate"]').click()
        a,b,copy=notes(p)
        assert copy["id"]!=a["id"] and copy["number"]>b["number"]
        assert all(copy[k]==v for k,v in a.items() if k not in ("id","number"))
        target(p,copy)
        p.locator('[data-action="edit"]').click()
        p.locator("#body").fill("Independent")
        p.locator("#noteStart").fill("0.1")
        p.locator("#noteStart").press("Tab")
        assert notes(p)[0]==a and notes(p)[2]["startUs"]==100000 and notes(p)[2]["endUs"]==1600000
        target(p,copy)
        p.locator('[data-action="delete"]').click()
        assert notes(p)==[a,b]
        p.keyboard.press("Control+z")
        assert notes(p)[2]["text"]=="Independent"
        at(p,25)
        now=p.evaluate("stamp()")
        with p.expect_file_chooser() as chooser:
            p.locator("#addPicture").click()
        chooser.value.set_files([])
        assert len(notes(p))==3
        with p.expect_file_chooser() as chooser:
            p.locator("#addPicture").click()
        chooser.value.set_files(str(picture))
        wait(p,"doc.notes.length===4")
        photo=notes(p)[-1]
        assert photo["startUs"]==now and photo["endUs"] is None and len(photo["pictures"])==1
        target(p,photo)
        p.locator("#canvasMenu").screenshot(path=str(work/"canvas-menu.png"))
        p.keyboard.press("Escape")
        assert p.locator("#canvasMenu").is_hidden()
        p.locator("#fullPreview").click()
        wait(p,"isFullPreview()")
        target(p,photo)
        p.locator('[data-action="edit"]').click()
        wait(p,"!isFullPreview() && document.activeElement===$('body')")
        at(p,28)
        p.evaluate("""async()=>{
            const canvas=document.createElement('canvas'),data=new DataTransfer();
            const png=await new Promise(done=>canvas.toBlob(done));
            data.items.add(new File([png],'paste.png',{type:'image/png'}));
            document.dispatchEvent(new ClipboardEvent('paste',{clipboardData:data,bubbles:true,cancelable:true}));
        }""")
        wait(p,"doc.notes.length===5 && doc.notes.at(-1).pictures?.length===1")
        assert notes(p)[-1]["startUs"]==p.evaluate("frames[28]") and notes(p)[3]==photo
        expected=notes(p)
        p.locator("#save").click()
        dlg=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
        dlg.wait("ready",timeout=10)
        dlg.child_window(class_name="Edit",found_index=0).set_edit_text(str(package))
        dlg.child_window(control_id=1).click()
        wait(p,"!busy && $('status').textContent.includes('已保存')")
    finally:
        stop(proc)
    proc,p=launch(pw,package)
    try:
        assert notes(p)==expected
    finally:
        stop(proc)
print("PASS: direct tool Marks, cancel, targeted context actions, timing, independent copies, attachments, undo and package reopen.",work)
