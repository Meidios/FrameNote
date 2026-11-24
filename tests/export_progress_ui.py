"""Verify the shared progress dialog during a real edited MP4 export."""
import json
import os
import subprocess
import time
import uuid
import zipfile

from playwright.sync_api import sync_playwright
from review_ui import exported_file, export_window, launch, stop, wait, work

os.environ["FRAMENOTE_DATA_DIR"] = str(work / "profile")
source = work / "source.mp4"
subprocess.run(["ffmpeg", "-v", "error", "-f", "lavfi", "-i",
                "testsrc2=size=1280x720:rate=30:duration=12", "-c:v", "libx264",
                "-preset", "ultrafast", str(source)], check=True)
clip = dict(id=str(uuid.uuid4()), track="video", startUs=0, sourceUs=500000, durationUs=11000000)
review = dict(version=3, id=str(uuid.uuid4()), fileName=source.name, durationUs=12000000,
              notes=[], lastNoteNumber=0, timeline=dict(sourceDurationUs=12000000, linked=True, clips=[clip]))
package = work / "edited.framenote"
with zipfile.ZipFile(package, "w") as archive:
    archive.writestr("review.json", json.dumps(review))
    archive.write(source, "media/source.mp4")


def start_export(page, proc):
    page.locator("#exportVideo").click()
    dialog=export_window(proc)
    dialog.wait("visible", timeout=15)
    return dialog


with sync_playwright() as pw:
    proc, page = launch(pw, package)
    try:
        for title in ("正在准备剪辑后的画面与声音", "正在渲染 MP4"):
            dialog=start_export(page,proc)
            dialog.child_window(auto_id="Message", title=title).wait("visible",timeout=60,retry_interval=.03)
            cancel=dialog.child_window(auto_id="DismissExport")
            assert cancel.window_text()=="取消导出"
            if title=="正在渲染 MP4":
                dialog.capture_as_image().save(work/"export-progress.png")
                dialog.type_keys("{ESC}")
            else:
                cancel.invoke()
            wait(page,"!busy && $('status').textContent.includes('已取消 MP4')")
            assert not list((work/"exports").iterdir())
        dialog=start_export(page,proc)
        hwnd,bounds=dialog.handle,dialog.rectangle()
        values=[]
        bar=dialog.child_window(auto_id="Bar").wrapper_object()
        deadline=time.monotonic()+90
        while bar.is_visible() and time.monotonic()<deadline:
            values.append(bar.iface_range_value.CurrentValue)
            time.sleep(.03)
        dialog.child_window(title="打开目录",control_type="Button").wait("visible",timeout=5)
        assert dialog.handle==hwnd and dialog.rectangle()==bounds
        assert len(values)>1 and values==sorted(values) and any(0<v<100 for v in values)
        dialog.capture_as_image().save(work/"export-complete.png")
        output=exported_file(proc)
        wait(page,"!busy && $('status').textContent.startsWith('带批注的 MP4 已保存')")
        subprocess.run(["ffmpeg","-v","error","-i",str(output),"-f","null","-"],check=True)
    finally:
        stop(proc)
    profile=work/"profile"
    (profile/"updates.json").write_text(json.dumps(dict(automatic=False,language="en")))
    blocked=work/"blocked"
    blocked.touch()
    (profile/"capture.json").write_text(json.dumps(dict(exportDirectory=str(blocked))))
    proc,page=launch(pw,package)
    try:
        page.locator("#save").click()
        dialog=export_window(proc)
        retry=dialog.child_window(title="Retry",control_type="Button")
        retry.wait("visible")
        assert dialog.window_text()=="Export failed" and dialog.child_window(auto_id="Message").window_text()
        hwnd,bounds=dialog.handle,dialog.rectangle()
        dialog.capture_as_image().save(work/"export-error-en.png")
        blocked.unlink()
        retry.invoke()
        dialog.child_window(title="Open folder",control_type="Button").wait("visible")
        assert dialog.window_text()=="Export complete" and dialog.handle==hwnd and dialog.rectangle()==bounds
        assert exported_file(proc).parent==blocked
    finally:
        stop(proc)
print("PASS: same native window for export, both cancellations, MP4 decode, English failure and successful retry.", work)
