"""Verify the shared progress dialog during a real edited MP4 export."""
import json
import os
import subprocess
import time
import uuid
import zipfile

from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import launch, stop, wait, work

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


def start_export(proc, page, path):
    page.evaluate("window.samples=[]")
    page.locator("#exportVideo").click()
    picker = Desktop(backend="win32").window(process=proc.pid, class_name="#32770")
    picker.wait("ready", timeout=10)
    time.sleep(.2)
    picker.child_window(class_name="Edit", found_index=0).set_edit_text(str(path))
    picker.child_window(control_id=1).click()


with sync_playwright() as pw:
    proc, page = launch(pw, package)
    try:
        page.evaluate("""() => chrome.webview.addEventListener('message', ({data}) => {
            if (data.type!=='exportProgress' || !data.active) return;
            const card=$('taskProgress'), bar=$('progressBar'), cancel=$('cancelProgress');
            const r=card.getBoundingClientRect(), button=cancel.getBoundingClientRect();
            samples.push({title:$('progressTitle').textContent, percent:bar.value,
                layout:card.matches(':modal') && Math.abs(r.left+r.width/2-innerWidth/2)<=1
                    && Math.abs(r.top+r.height/2-innerHeight/2)<=1 && button.top>bar.getBoundingClientRect().bottom
                    && Math.abs(button.left+button.width/2-innerWidth/2)<=1
                    && $('progressPercent').textContent===bar.value+'%' && cancel.textContent==='取消导出'});
        })""")
        for title in ("正在准备剪辑后的画面与声音", "正在渲染 MP4"):
            cancelled = work / "cancelled.mp4"
            start_export(proc, page, cancelled)
            wait(page, "t=>$('taskProgress').open && $('progressTitle').textContent===t", title, timeout=60000)
            if title=="正在渲染 MP4":
                page.locator("#taskProgress").screenshot(path=str(work / "export-progress.png"))
                page.keyboard.press("Escape")
            else:
                page.get_by_role("button", name="取消导出").click()
            wait(page, "!busy && !$('taskProgress').open && $('status').textContent.includes('已取消 MP4')")
            assert not cancelled.exists() and not list(work.glob("cancelled.mp4.*.tmp.mp4"))
            assert all(s["layout"] and s["percent"]<100 for s in page.evaluate("samples"))
            if title=="正在渲染 MP4":
                assert any(s["title"]=="正在准备剪辑后的画面与声音" and s["percent"]>0 for s in page.evaluate("samples"))
        output = work / "exported.mp4"
        start_export(proc, page, output)
        wait(page, "!busy && !$('taskProgress').open && $('status').textContent.startsWith('带批注的 MP4 已保存')", timeout=90000)
        samples = page.evaluate("samples")
        values = [s["percent"] for s in samples]
        assert values==sorted(values) and values[0]==0 and values[-1]==100
        assert all(s["layout"] for s in samples)
        rendering = [s["percent"] for s in samples if s["title"]=="正在渲染 MP4"]
        assert rendering[0]<rendering[-1]<100
        subprocess.run(["ffmpeg", "-v", "error", "-i", str(output), "-f", "null", "-"], check=True)
    finally:
        stop(proc)
print("PASS: centered export progress, both cancellations, monotonic retry and decoded MP4.", work)
