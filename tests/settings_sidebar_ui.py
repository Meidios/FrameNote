"""Exercise native settings navigation, modal ownership and cross-page preferences."""
import json
import os
import re

from playwright.sync_api import sync_playwright
from pywinauto import Desktop, keyboard
from review_ui import app, desk, launch, settings, stop, wait, work


profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
(profile / "shortcuts.json").write_text('{"record":[]}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
sections = ["通用", "批注工具", "录制设置", "表格设置", "REAPER集成", "快捷键", "使用说明", "更新", "问题反馈", "关于"]


def section(dlg, name):
    return dlg.child_window(title=name, control_type="TabItem", visible_only=False)


def release_text(label):
    text = (app.parent / "releases" / (label + ".md")).read_text(encoding="utf-8-sig")
    chinese = re.split(r"(?m)^\**English\b", text, maxsplit=1)[0]
    return [line.removeprefix("- ") for line in chinese.splitlines() if line and not line.startswith("# ")]


def displayed_entries(dlg):
    body = dlg.child_window(auto_id="ReleaseText")
    return [item.window_text() for item in body.descendants(control_type="Text")]


def dismiss(dlg, label):
    window = Desktop(backend="win32").window(process=dlg.process_id(), title="设置")
    dlg.child_window(title=label, control_type="Button").invoke()
    window.wait_not("exists")


with sync_playwright() as pw:
    proc, page = launch(pw, None, "typeof render==='function'")
    try:
        dlg = settings(proc)
        tabs = dlg.descendants(control_type="TabItem")
        assert [tab.window_text() for tab in tabs] == sections
        positions = [tab.rectangle() for tab in tabs]
        assert all(a.left == b.left and a.bottom <= b.top for a, b in zip(positions, positions[1:]))
        title = dlg.child_window(title=dlg.window_text(), control_type="Text").rectangle()
        assert title.bottom <= positions[0].top
        unit = dlg.child_window(auto_id="DefaultRulerUnit", control_type="ComboBox")
        assert not unit.exists(timeout=0)
        dlg.capture_as_image().save(work / "settings-general.png")
        section(dlg, "批注工具").select()
        assert positions[1].right < unit.rectangle().left
        unit.select("厘米（cm）")

        for source,target,field in [("通用","批注工具","DefaultOpacity"),("批注工具","录制设置","SaveCopy"),
                                    ("录制设置","表格设置","ExcelZip"),("表格设置","REAPER集成","ReaperFolder"),("REAPER集成","快捷键","point")]:
            section(dlg,source).set_focus()
            keyboard.send_keys("{DOWN}")
            assert section(dlg,target).is_selected(),[t.window_text() for t in tabs if t.is_selected()]
            dlg.child_window(auto_id=field).wait("exists")
        section(dlg, "使用说明").select()
        guide = dlg.child_window(auto_id="GuideScroll")
        heading = guide.child_window(auto_id="GuideOverviewTitle")
        summary = guide.child_window(auto_id="GuideOverviewText")
        first = guide.child_window(title="打开素材", control_type="Text")
        first.wait("visible")
        assert heading.window_text() == "把反馈标在具体画面上"
        assert heading.rectangle().left == summary.rectangle().left
        assert summary.rectangle().bottom < first.rectangle().top
        dlg.capture_as_image().save(work / "settings-guide.png")
        native = Desktop(backend="win32").window(handle=dlg.handle)
        native.move_window(width=590, height=400)
        scroll = guide.wrapper_object().iface_scroll
        assert scroll.CurrentVerticallyScrollable and not scroll.CurrentHorizontallyScrollable
        scroll.SetScrollPercent(-1,100)
        last = guide.child_window(title_re=r"^语言、更新与反馈.*", control_type="Text")
        last.wait("visible")
        assert guide.rectangle().top <= last.rectangle().top < guide.rectangle().bottom
        dlg.capture_as_image().save(work / "settings-guide-compact.png")
        for name in sections:
            tab = section(dlg, name)
            tab.select()
            assert tab.is_selected()
            assert all(label.rectangle().right <= tab.rectangle().right
                       for label in dlg.descendants(title=name, control_type="Text"))
        assert dlg.child_window(title="保存", control_type="Button").rectangle().bottom < native.rectangle().bottom
        section(dlg, "问题反馈").select()
        go=dlg.child_window(auto_id="IssueButton", control_type="Button")
        assert go.is_enabled() and go.wrapper_object().element_info.element.CurrentHelpText=="https://github.com/Meidios/FrameNote/issues/new"
        assert dlg.child_window(title="打开日志目录",control_type="Button").is_enabled()
        dlg.capture_as_image().save(work / "settings-feedback.png")
        section(dlg, "关于").select()
        assert "Windows x64" in dlg.child_window(auto_id="AboutVersion").window_text()
        assert "Meidios" in dlg.child_window(auto_id="AboutAuthor").window_text()
        home = dlg.child_window(title="Github主页", control_type="Button")
        assert home.is_visible() and home.wrapper_object().element_info.element.CurrentHelpText == "https://github.com/Meidios/FrameNote"
        assert not dlg.child_window(auto_id="CheckButton").exists(timeout=0)

        section(dlg, "更新").select()
        tip = dlg.child_window(auto_id="UpdateHint")
        assert "GitHub" in tip.window_text() and tip.rectangle().top >= dlg.child_window(auto_id="CheckButton").rectangle().bottom
        version = dlg.child_window(auto_id="CurrentVersion").window_text()
        assert re.search(r"当前版本\s+\d+\.\d+\.\d+", version)
        auto = dlg.child_window(auto_id="AutomaticUpdates")
        assert auto.get_toggle_state() == 0
        auto.toggle()
        versions = dlg.child_window(auto_id="ReleaseVersions", control_type="ComboBox")
        current = "v" + version.split()[-1]
        assert versions.selected_text() == current
        assert displayed_entries(dlg) == release_text(current)
        assert not dlg.child_window(title="更新记录", control_type="Button").exists(timeout=0)
        viewport = dlg.child_window(auto_id="UpdatesScroll")
        scroll = viewport.wrapper_object().iface_scroll
        assert scroll.CurrentVerticallyScrollable and not scroll.CurrentHorizontallyScrollable
        scroll.SetScrollPercent(-1, 100)
        assert scroll.CurrentVerticalScrollPercent > 95
        assert viewport.rectangle().bottom <= dlg.child_window(title="保存", control_type="Button").rectangle().top
        versions.select("v0.8.0")
        assert displayed_entries(dlg) == release_text("v0.8.0")
        assert scroll.CurrentVerticalScrollPercent <= 0
        section(dlg, "关于").select()
        section(dlg, "更新").select()
        assert versions.selected_text() == "v0.8.0" and auto.get_toggle_state() == 1
        versions.select(current)
        native.move_window(width=820, height=700)
        dlg.capture_as_image().save(work / "settings-update-history.png")
        dlg.child_window(auto_id="CheckButton").invoke()
        result = Desktop(backend="win32").window(process=proc.pid, title_re="^(检查更新|发现新版本)$")
        result.wait("ready", timeout=30)
        assert not native.is_enabled()
        print("Update result:", [t.window_text() for t in Desktop(backend="uia").window(handle=result.handle).descendants(control_type="Text")], flush=True)
        result.close()
        native.wait("ready")
        section(dlg, "批注工具").select()
        assert unit.selected_text() == "厘米（cm）"
        dismiss(dlg, "取消")
        assert not json.loads((profile / "updates.json").read_text())["automatic"]

        dlg = settings(proc,"批注工具")
        unit = dlg.child_window(auto_id="DefaultRulerUnit", control_type="ComboBox")
        assert unit.selected_text() == "米（m）"
        unit.select("厘米（cm）")
        section(dlg, "更新").select()
        auto = dlg.child_window(auto_id="AutomaticUpdates")
        assert auto.get_toggle_state() == 0
        auto.toggle()
        dismiss(dlg, "保存")
        wait(page, "defaultRulerUnit==='cm'")
        dlg = settings(proc,"批注工具")
        assert dlg.child_window(auto_id="DefaultRulerUnit").selected_text() == "厘米（cm）"
        section(dlg, "更新").select()
        assert dlg.child_window(auto_id="AutomaticUpdates").get_toggle_state() == 1
        assert json.loads((profile / "updates.json").read_text())["automatic"]
        dismiss(dlg, "取消")
        main = desk(proc)
        main.child_window(title="帮助", control_type="MenuItem", depth=3).expand()
        main.child_window(title="更新记录", control_type="MenuItem", found_index=0).invoke()
        history = Desktop(backend="win32").window(process=proc.pid, title="设置")
        history.wait("ready")
        popup = Desktop(backend="uia").window(handle=history.handle)
        assert section(popup,"更新").is_selected()
        assert popup.child_window(auto_id="ReleaseVersions").selected_text() == current
        assert displayed_entries(popup) == release_text(current)
        history.close()
        print("PASS: settings sections, feedback links, compact scrolling, release history, update dialogs, save and cancel.", flush=True)
    finally:
        stop(proc)
