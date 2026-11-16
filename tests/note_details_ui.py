"""Check the pinned details panel, card-local deletion and keyboard history in WebView2."""
import os
import shutil
from playwright.sync_api import sync_playwright
from review_ui import fixtures, launch, notes, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
video = work / "details.mp4"
shutil.copyfile(fixtures / "vfr.mp4", video)

with sync_playwright() as pw:
    proc, page = launch(pw, video)
    try:
        panel = page.locator("#editor")
        box = panel.bounding_box()
        assert page.locator("#emptyDetails").is_visible()
        assert not page.locator("#reviewPanel :is(#startPicture,#edit,#del,#undo,#gif,#cancel,[type=submit])").count()
        for i in range(18):
            page.locator("#point").click()
            page.locator("#body").fill(f"Note {i+1}")
        page.locator(".note .stamp").first.click()
        body = page.locator("#body")
        assert body.input_value() == "Note 1"
        body.fill("Changed")
        body.press("End")
        page.keyboard.type(" together")
        page.keyboard.press("Control+z")
        assert body.input_value() == "Note 1"
        page.keyboard.press("Control+Shift+z")
        assert body.input_value() == "Changed together"
        card = page.locator(".note").nth(1)
        trash = card.get_by_role("button", name="删除批注 #2", exact=True)
        assert trash.locator("svg use").count() == 1 and not trash.inner_text()
        trash.click()
        assert body.input_value() == "Changed together" and not any(n["number"] == 2 for n in notes(page))
        page.keyboard.press("Control+z")
        assert len(notes(page)) == 18 and body.input_value() == "Changed together"
        page.keyboard.press("Control+Shift+z")
        assert len(notes(page)) == 17
        page.locator(".note.selected .noteDelete").click()
        assert page.locator("#emptyDetails").is_visible()
        page.keyboard.press("Control+z")
        assert body.input_value() == "Changed together"
        body.fill("Final revision")
        page.keyboard.press("Control+Shift+z")
        assert len(notes(page)) == 17 and body.input_value() == "Final revision"
        page.locator(".note .stamp").last.click()
        assert panel.bounding_box() == box and page.locator("#list").evaluate("el=>el.scrollHeight>el.clientHeight && el.scrollTop>0")
        page.locator(".note .stamp").first.click()
        page.locator("#reviewPanel").screenshot(path=str(work / "note-details.png"))
        expected = notes(page)
    finally:
        stop(proc)
    proc, page = launch(pw, video)
    try:
        assert notes(page) == expected
    finally:
        stop(proc)
print("PASS: pinned details, scrolling, auto-save and reopen, targeted deletion, undo/redo and branching.", work)
