"""Time the first presented video pixels, including startup through the Explorer verb."""
import os
import statistics
import subprocess
import time
from collections import Counter

import win32con
import win32gui
import win32process
from review_ui import app, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
player = app.with_name("FrameNote Player.exe")
clip = work / "first-frame.mp4"
subprocess.run([
    "ffmpeg", "-v", "error", "-f", "lavfi", "-i", "color=magenta:size=1280x720:rate=30",
    "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-t", "4", "-c:v", "libx264",
    "-c:a", "aac", "-movflags", "+faststart", str(clip),
], check=True)

def measure(args, empty=False):
    start = time.perf_counter()
    command = [player, *args] if empty else [player, *args, clip]
    proc = subprocess.Popen(command, env=dict(os.environ, FRAMENOTE_DATA_DIR=str(profile)))
    dc = win32gui.GetDC(0)
    seen = Counter()
    hwnd = None
    try:
        while time.perf_counter() - start < 15:
            if not hwnd:
                windows = []
                def visit(h, _):
                    if (win32process.GetWindowThreadProcessId(h)[1] == proc.pid and win32gui.IsWindowVisible(h)
                            and win32gui.GetWindowText(h).startswith("FrameNote Player")):
                        windows.append(h)
                win32gui.EnumWindows(visit, None)
                hwnd = next(iter(windows), None)
                if hwnd:
                    win32gui.SetWindowPos(hwnd, win32con.HWND_TOPMOST, 0, 0, 0, 0,
                        win32con.SWP_NOMOVE | win32con.SWP_NOSIZE | win32con.SWP_NOACTIVATE)
            if hwnd:
                left, top, right, bottom = win32gui.GetWindowRect(hwnd)
                x, y = (left + right) // 2, (top + bottom) // 2
                owner = win32gui.WindowFromPoint((x, y))
                if owner and win32process.GetWindowThreadProcessId(owner)[1] == proc.pid:
                    rgb = win32gui.GetPixel(dc, x, y)
                    r, g, b = rgb & 255, (rgb >> 8) & 255, (rgb >> 16) & 255
                    seen[(r,g,b)] += 1
                    if (not empty and r > 200 and g < 40 and b > 200) or (empty and seen[(0,0,0)] >= 8):
                        return round((time.perf_counter()-start)*1000), seen
            time.sleep(.005)
        raise AssertionError(("No video frame", seen))
    finally:
        win32gui.ReleaseDC(0,dc)
        if hwnd:
            win32gui.PostMessage(hwnd, 0x0010, 0, 0)
        try:
            proc.wait(timeout=4)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait()

for args in ([], ["--shell-selection"]):
    results = [measure(args) for _ in range(5)]
    print(args or ["direct"], [t for t,_ in results], "median ms", statistics.median(t for t,_ in results), flush=True)
    if not os.environ.get("FRAMENOTE_MEASURE_BASELINE"):
        # The compositor can briefly blend with the desktop during the Windows opening animation.
        assert all(colors[(105,105,105)] < 4 for _,colors in results), results
if not os.environ.get("FRAMENOTE_MEASURE_BASELINE"):
    measure([], empty=True)
    print("PASS: the empty Player holds a black video surface.")
