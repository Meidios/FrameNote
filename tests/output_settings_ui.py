"""Check folder editing, recording preferences, and the folder offered after export."""
import json
import os
import win32con
import win32gui
from pathlib import Path

from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from pywinauto.timings import wait_until
from win32com.client import Dispatch
from review_ui import fixtures, launch, settings, stop, work, exported_file

profile=work/"profile"
profile.mkdir()
(profile/"shortcuts.json").write_text('{"record":[]}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
config=profile/"capture.json"
original=work/"old"
copies=work/"录制副本"
exports=work/"交付目录"
exports.mkdir()
config.write_text(json.dumps(dict(fps=30,quality="high",exportDirectory=str(original))))

with sync_playwright() as pw:
    proc,page=launch(pw,fixtures/"vfr.mp4")
    try:
        dlg=settings(proc,"录制设置")
        field=dlg.child_window(auto_id="CopyDirectory",control_type="Edit")
        browse=dlg.child_window(title="浏览…",control_type="Button")
        switch=dlg.child_window(auto_id="SaveCopy")
        assert dlg.child_window(auto_id="RecordingFps").selected_text()=="30 fps"
        assert dlg.child_window(auto_id="RecordingQuality").selected_text()=="高画质"
        assert not switch.get_toggle_state() and not field.is_enabled() and not browse.is_enabled()
        switch.toggle()
        assert field.is_enabled() and browse.is_enabled()
        field.set_edit_text(str(copies))
        dlg.child_window(title="取消",control_type="Button").invoke()
        assert "saveCopy" not in json.loads(config.read_text())

        dlg=settings(proc)
        field=dlg.child_window(auto_id="ExportDirectory",control_type="Edit")
        field.set_edit_text("relative")
        dlg.child_window(title="保存",control_type="Button").invoke()
        dlg.child_window(title="请输入完整的文件夹路径。").wait("visible")
        assert json.loads(config.read_text())["exportDirectory"]==str(original)
        dlg.child_window(title="浏览…",control_type="Button").invoke()
        picker=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
        picker.wait("ready")
        picker.child_window(class_name="Edit",found_index=0).set_edit_text(str(exports))
        picker.child_window(control_id=1).click()
        if picker.exists(timeout=.4) and picker.is_visible():
            picker.child_window(control_id=1).click()
        wait_until(5,.1,lambda:field.get_value()==str(exports))
        dlg.child_window(title="录制设置",control_type="TabItem").select()
        dlg.child_window(auto_id="SaveCopy").toggle()
        dlg.child_window(auto_id="CopyDirectory").set_edit_text(str(copies))
        settings_window=Desktop(backend="win32").window(handle=dlg.handle)
        dlg.child_window(title="保存",control_type="Button").invoke()
        settings_window.wait_not("exists")
    finally:
        stop(proc)

    proc,page=launch(pw,fixtures/"vfr.mp4")
    try:
        dlg=settings(proc)
        assert dlg.child_window(auto_id="ExportDirectory").get_value()==str(exports)
        dlg.child_window(title="录制设置",control_type="TabItem").select()
        assert dlg.child_window(auto_id="CopyDirectory").get_value()==str(copies)
        dlg.child_window(title="取消",control_type="Button").invoke()
        page.locator("#point").click()
        page.locator("#save").click()
        first=exported_file(proc)
        assert first==exports/"vfr.framenote"
        before=first.read_bytes()
        page.locator("#save").click()
        window=Desktop(backend="win32").window(process=proc.pid,title="导出完成")
        window.wait("visible")
        prompt=Desktop(backend="uia").window(handle=window.handle)
        second=exports/"vfr (2).framenote"
        assert second.is_file() and first.read_bytes()==before
        assert not prompt.child_window(title=str(second)).exists(timeout=0)
        assert not win32gui.GetWindowLong(window.handle,win32con.GWL_STYLE) & (win32con.WS_MINIMIZEBOX|win32con.WS_MAXIMIZEBOX)
        title=prompt.child_window(title="导出完成",control_type="Text").rectangle()
        question=prompt.child_window(title="是否打开文件所在目录？",control_type="Text").rectangle()
        open_button=prompt.child_window(title="打开目录",control_type="Button")
        left=open_button.rectangle()
        middle=prompt.child_window(auto_id="CopyExport",title="复制",control_type="Button").rectangle()
        right=prompt.child_window(auto_id="DismissExport").rectangle()
        bounds=prompt.rectangle()
        assert title.left < question.left and title.bottom < question.top < left.top
        assert bounds.left < question.left and question.right < bounds.right
        assert abs(left.left+right.right-bounds.left-bounds.right)<=4 and left.width()==right.width()
        assert left.height()==middle.height()==right.height()>=36 and left.width()==middle.width()
        assert middle.left-left.right == right.left-middle.right > 0
        prompt.capture_as_image().save(work/"export-complete.png")
        open_button.invoke()
        shell=Dispatch("Shell.Application")
        def opened():
            for item in shell.Windows():
                try:
                    if Path(item.Document.Folder.Self.Path)==exports:return item
                except AttributeError:
                    continue
        wait_until(10,.2,lambda:opened() is not None)
        opened().Quit()
        print("PASS: folder picker, path validation, cancel, legacy settings, restart, duplicate export and Explorer destination.")
    finally:
        stop(proc)
