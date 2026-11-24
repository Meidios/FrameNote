"""Exercise the settings search against live, edited and localized shortcut bindings."""
import json
import os

import win32api
import win32con
from playwright.sync_api import sync_playwright
from pywinauto import Desktop, keyboard, mouse
from review_ui import desk, launch, stop, work


profile = work / "profile"
profile.mkdir()
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
bindings = {"splitClip": [{"mods": 0, "vk": 117}, {"mods": 0, "vk": 118}],
            "record": [{"mods": 3, "vk": 132}]}
config = profile / "shortcuts.json"
config.write_text(json.dumps(bindings))

with sync_playwright() as pw:
    for language, title, tab, query in [("zh-CN", "设置", "快捷键", "分割"), ("en", "Settings", "Shortcuts", "SPLIT")]:
        (profile / "updates.json").write_text(json.dumps({"automatic": False, "language": language}))
        proc, page = launch(pw, None, "typeof render==='function'")
        try:
            desk(proc).child_window(auto_id="SettingsGear").invoke()
            window = Desktop(backend="win32").window(process=proc.pid, title=title)
            window.wait("ready", timeout=15)
            dlg = Desktop(backend="uia").window(handle=window.handle)
            dlg.child_window(title=tab, control_type="TabItem").select()
            field = dlg.child_window(auto_id="ShortcutSearch")
            split = dlg.child_window(auto_id="splitClip")
            field.set_edit_text(query)
            split.wait("visible")
            dlg.child_window(auto_id="open").wait_not("visible")
            field.set_edit_text("nosuchaction")
            dlg.child_window(auto_id="NoSearchResults").wait("visible")
            dlg.child_window(auto_id="ClearSearch").invoke()
            dlg.child_window(auto_id="open").wait("visible")

            mode = dlg.child_window(auto_id="FindByKey")
            mode.toggle()
            for key, action in [("{F7}", "splitClip"), ("^d", "duplicateNote"), ("{ESC}", "cancel"),
                                ("{ENTER}", "fullPreview"), ("%^{F21}", "record")]:
                keyboard.send_keys(key, vk_packet=False)
                dlg.child_window(auto_id=action).wait("visible")
                dlg.child_window(auto_id="open").wait_not("visible")
            keyboard.send_keys("s", vk_packet=False)
            dlg.child_window(auto_id="NoSearchResults").wait("visible")
            win32api.keybd_event(190, 0, 0, 0)
            try:
                keyboard.send_keys(",", vk_packet=False)
            finally:
                win32api.keybd_event(190, 0, win32con.KEYEVENTF_KEYUP, 0)
            dlg.child_window(auto_id="rateReset").wait("visible")
            dlg.child_window(auto_id="rateDown").wait_not("visible")

            point = field.rectangle().mid_point()
            keyboard.send_keys("{VK_CONTROL down}")
            try:
                mouse.scroll(coords=(point.x, point.y), wheel_dist=1)
                for action in ("zoomVideo", "zoomTimeline"):
                    dlg.child_window(auto_id=action).wait("visible")
                mouse.click(button="right", coords=(point.x, point.y))
                dlg.child_window(auto_id="quickArrow").wait("visible")
            finally:
                keyboard.send_keys("{VK_CONTROL up}")

            keyboard.send_keys("{F6}")
            split.invoke()
            dlg.child_window(auto_id="editBinding0").invoke()
            keyboard.send_keys("{F8}")
            dlg.child_window(auto_id="ApplyBinding").invoke()
            dlg.child_window(auto_id="NoSearchResults").wait("visible")
            keyboard.send_keys("{F8}")
            split.wait("visible")
            mode.toggle()
            field.set_edit_text(query)
            split.wait("visible")
            dlg.capture_as_image().save(work / f"shortcut-search-{language}.png")
            dlg.child_window(title="取消" if language=="zh-CN" else "Cancel", control_type="Button").invoke()
            assert json.loads(config.read_text()) == bindings
            print("PASS:", language, "name, custom keys, global shortcut, pair, mouse, editing and cancel", flush=True)
        finally:
            stop(proc)
