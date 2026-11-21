"""Split from the focused time ruler, using default and customized shortcuts."""
import json
import os
from playwright.sync_api import sync_playwright
from review_ui import fixtures, launch, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)


def seek_by_mouse(page):
    ruler = page.locator("#seek")
    ruler.click(position={"x": round(ruler.bounding_box()["width"] * .64), "y": 10})
    wait(page, "document.activeElement===$('seek') && !v.seeking && stamp()>0 && stamp()<doc.durationUs")
    return page.evaluate("stamp()")


def clip_counts(page):
    return [page.locator(f"#{track}Clips .editClip").count() for track in ("video", "audio")]


with sync_playwright() as pw:
    for key in ("s", "w"):
        if key == "w":
            (profile / "shortcuts.json").write_text(json.dumps({"splitClip": {"mods": 0, "vk": 87}}))
        proc, page = launch(pw, fixtures / "vfr.mp4")
        try:
            page.evaluate("transport.pause()")
            cut = seek_by_mouse(page)
            if key == "w":
                page.keyboard.press("s")
                assert clip_counts(page) == [1, 1]
            page.keyboard.press(key)
            counts = clip_counts(page)
            outline = page.locator("#seek").evaluate("el=>getComputedStyle(el).outlineStyle")
            assert counts == [2, 2] and outline == "none", (key, cut, counts, outline)
            assert page.evaluate("doc.timeline.clips.filter(c=>c.startUs>0).map(c=>c.startUs)") == [cut, cut]
            assert page.evaluate("document.activeElement===$('seek')")
            page.keyboard.press("Control+z")
            assert clip_counts(page) == [1, 1]
            page.locator("#linkTracks").click()
            page.locator("#videoClips .editClip").click()
            seek_by_mouse(page)
            page.keyboard.press(key)
            assert clip_counts(page) == [2, 1]
            page.locator("#point").click()
            page.locator("#body").press(key)
            assert page.locator("#body").input_value() == key
            assert clip_counts(page) == [2, 1]
        finally:
            stop(proc)
print("PASS: ruler focus, linked and independent splits, undo, remapped shortcut and text entry.")
