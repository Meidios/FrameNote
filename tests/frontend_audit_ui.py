"""Exercise page-scoped resets, compact commands and note activation in the release UI."""
import json
import os

import win32con
import win32gui
from playwright.sync_api import expect, sync_playwright
from pywinauto import Desktop
from review_ui import desk, fixtures, launch, stop, switch_file, wait, work


profile = work / "profile"
profile.mkdir()
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
(profile / "updates.json").write_text('{"language":"zh-CN","automatic":false}')
(profile / "shortcuts.json").write_text(json.dumps({"splitClip":[{"mods":0,"vk":117}],"record":[]}))


def save(dlg):
    popup = Desktop(backend="win32").window(process=dlg.process_id(), title="设置")
    dlg.child_window(title="保存", control_type="Button").invoke()
    try:
        popup.wait_not("exists")
    except Exception:
        print("Settings save:",dlg.child_window(auto_id="SaveError",visible_only=False).window_text(),flush=True)
        raise


def settings(proc):
    window = desk(proc)
    win32gui.SetWindowPos(window.handle, win32con.HWND_TOPMOST, 0, 0, 0, 0,
                          win32con.SWP_NOMOVE | win32con.SWP_NOSIZE)
    window.set_focus()
    window.child_window(auto_id="SettingsGear", control_type="Button").click_input()
    native=Desktop(backend="win32").window(process=proc.pid,title="设置")
    native.wait("ready")
    return Desktop(backend="uia").window(handle=native.handle)


with sync_playwright() as pw:
    proc, page = launch(pw, None, "typeof render==='function'")
    try:
        assert "视频、图片或审阅包" in page.locator("#welcome p").inner_text()
        assert not page.locator("#list").inner_text().strip()
        wait(page, "bindingLabel('splitClip')==='F6'")
        dlg = settings(proc)
        dlg.child_window(title="批注工具", control_type="TabItem").select()
        unit = dlg.child_window(auto_id="DefaultRulerUnit")
        unit.select("厘米（cm）")
        dlg.child_window(title="通用", control_type="TabItem").select()
        dlg.child_window(auto_id="ResetButton").invoke()
        dlg.child_window(title="批注工具", control_type="TabItem").select()
        assert unit.selected_text() == "厘米（cm）"
        dlg.child_window(auto_id="ResetButton").invoke()
        assert unit.selected_text() == "米（m）"
        save(dlg)
        assert page.evaluate("bindingLabel('splitClip')")== "F6"
        dlg = settings(proc)
        dlg.child_window(title="批注工具", control_type="TabItem").select()
        dlg.child_window(auto_id="DefaultRulerUnit").select("厘米（cm）")
        dlg.child_window(title="快捷键", control_type="TabItem").select()
        dlg.child_window(auto_id="ResetButton").invoke()
        save(dlg)
        wait(page, "defaultRulerUnit==='cm' && bindingLabel('splitClip')==='S'")

        switch_file(proc, page, fixtures / "vfr.mp4")
        assert "绘图工具" in page.locator("#list").inner_text()
        for body in ("对白提前", "环境声淡出"):
            page.locator("#point").click()
            page.locator("#body").fill(body)
            page.keyboard.press("Control+Enter")
        stamps = page.locator(".stamp")
        for i, key in [(0, "Enter"), (1, "Space")]:
            stamps.nth(i).press(key)
            current = page.locator(".stamp[aria-current=true]")
            assert current.count() == 1 and current.evaluate("el=>el===document.activeElement")
            description = current.get_attribute("aria-describedby")
            assert page.locator("#"+description).inner_text() == ("对白提前", "环境声淡出")[i]
            expect(current).to_have_accessible_description(("对白提前", "环境声淡出")[i])
            assert not page.evaluate("isFullPreview()")
        clip = page.locator("#videoClips .editClip").first
        clip.press("Enter")
        assert clip.get_attribute("aria-pressed")=="true" and clip.evaluate("el=>el===document.activeElement")
        page.locator("#stage").focus()
        page.keyboard.press("Enter")
        wait(page,"isFullPreview()")
        page.keyboard.press("Escape")
        wait(page,"!isFullPreview()")
        assert page.locator("#renumberNotes").inner_text()=="重新编号"
        assert "仅更改批注编号" in page.locator("#renumberNotes").get_attribute("aria-description")
        assert page.evaluate("['#ff00ff','#000033','#ffffff'].map(badgeInk)") == ["#000","#fff","#000"]
        window = Desktop(backend="win32").window(handle=desk(proc).handle)
        for width, height in [(840,560), (1024,650), (1440,900)]:
            window.move_window(x=24,y=24,width=width,height=height)
            wait(page, "w=>Math.abs(innerWidth-w)<25", width)
            page.wait_for_timeout(150)
            stage = page.locator("#stage").bounding_box()
            print(f"{width}x{height} preview {stage['width']:.0f}x{stage['height']:.0f}",flush=True)
            assert stage["height"] >= 190
            toggle = page.locator("#capturePanelToggle")
            if width < 1100:
                assert toggle.is_visible() and page.locator("#capturePanel").is_hidden()
                toggle.click()
                assert page.locator("#capturePanel select").first.is_visible()
                page.keyboard.press("Escape")
                assert page.locator("#capturePanel").is_hidden()
            else:
                assert toggle.is_hidden() and page.locator("#capturePanel").is_visible()
            if width==840:
                more = page.locator("#moreEdits")
                more.click()
                joins = page.locator("#alignClips")
                assert joins.is_visible() and joins.get_by_text("贴到上一片段").is_visible()
                box=joins.bounding_box()
                assert box["x"]>=0 and box["y"]>=0 and box["x"]+box["width"]<=page.evaluate("innerWidth")
                page.keyboard.press("Escape")
                assert joins.is_hidden()
            page.mouse.move(0,0)
            wait(page,"getComputedStyle($('buttonHint')).opacity==='0'")
            page.screenshot(path=str(work/f"workspace-{width}.png"))
        space = page.evaluate("""()=> {
          const gap = (a,b) => $(b).getBoundingClientRect().left-$(a).getBoundingClientRect().right;
          return [gap('box','line'),gap('text','voiceRecord'),gap('addPicture','clear')];
        }""")
        assert space[0]>=0 and max(space)-min(space)<1,space
        print("PASS: settings isolation, focused commands, note semantics and compact workspace.",work,flush=True)
    finally:
        stop(proc)
