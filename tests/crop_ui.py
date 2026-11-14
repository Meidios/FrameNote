"""Measure a real region recording on an active Windows monitor (see verification.md)."""
import ctypes
import itertools
import json
import multiprocessing
import subprocess
import sys
import time
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import url2pathname

import win32api
import win32con as C
import win32gui as G
import win32process


def physical_pixels():
    assert ctypes.windll.user32.SetThreadDpiAwarenessContext(ctypes.c_void_p(-4))


def card(bounds, crop, ready):
    physical_pixels()
    x, y, right, bottom = bounds
    l, t, r, b = crop
    brushes = [G.CreateSolidBrush(win32api.RGB(i, i, i)) for i in (28, 95, 240)]

    def paint(hwnd, msg, wp, lp):
        if msg == C.WM_PAINT:
            dc, ps = G.BeginPaint(hwnd)
            G.FillRect(dc, G.GetClientRect(hwnd), brushes[0])
            G.FillRect(dc, crop, brushes[1])
            for rect in [(l+12,t,l+20,b), (r-20,t,r-12,b),
                         (l,t+12,r,t+20), (l,b-20,r,b-12)]:
                G.FillRect(dc, rect, brushes[2])
            G.EndPaint(hwnd, ps)
            return 0
        return G.DefWindowProc(hwnd, msg, wp, lp)

    wc = G.WNDCLASS()
    wc.lpszClassName = "FrameNoteCropReference"
    wc.hInstance = win32api.GetModuleHandle(None)
    wc.lpfnWndProc = paint
    G.RegisterClass(wc)
    hwnd = G.CreateWindowEx(C.WS_EX_TOOLWINDOW | C.WS_EX_NOACTIVATE, wc.lpszClassName,
                            "FrameNote crop reference", C.WS_POPUP, x,y,right-x,bottom-y,
                            0,0,wc.hInstance,None)
    G.SetWindowPos(hwnd, C.HWND_TOPMOST, x,y,right-x,bottom-y, C.SWP_NOACTIVATE | C.SWP_SHOWWINDOW)
    G.UpdateWindow(hwnd)
    ready.set()
    G.PumpMessages()


def measure(clip, width, height):
    stream = json.loads(subprocess.check_output(["ffprobe","-v","error","-select_streams","v:0",
        "-show_entries","stream=width,height","-of","json",clip]))["streams"][0]
    w,h = stream["width"],stream["height"]
    pixels = subprocess.check_output(["ffmpeg","-v","error","-ss","0.5","-i",clip,
        "-frames:v","1","-pix_fmt","gray","-f","rawvideo","-"])
    assert len(pixels) == w*h
    def stripes(line):
        return [(g[0],g[-1]+1) for white, group in itertools.groupby(range(len(line)),
                lambda i: line[i]>195) if white and (g:=list(group))]
    xs = stripes(pixels[(h//2)*w:(h//2+1)*w])
    ys = stripes(pixels[w//2::w])
    print("Decoded MP4:", (w,h), "white bands X/Y:",xs,ys, flush=True)
    assert (w,h)==(width,height) and xs==[(12,20),(width-20,width-12)] and ys==[(12,20),(height-20,height-12)]
    # The test card is greyscale right up to its edges. A recorded blue outline
    # would add chroma even if it did not cross the white-band threshold.
    rgb=subprocess.check_output(["ffmpeg","-v","error","-ss","0.5","-i",clip,
        "-frames:v","1","-pix_fmt","rgb24","-f","rawvideo","-"])
    def chroma(x,y):
        i=3*(y*w+x);p=rgb[i:i+3]
        return max(p)-min(p)
    assert max(chroma(x,y) for y in range(h) for x in range(w)
               if min(x,y,w-1-x,h-1-y)<3)<12


def run():
    from playwright.sync_api import sync_playwright
    from pywinauto import Desktop
    from review_ui import launch, stop, wait, fixtures
    physical_pixels()
    name, direction = sys.argv[3:5]
    [(monitor, bounds)] = [(h,tuple(rect)) for h,_,rect in win32api.EnumDisplayMonitors()
                           if win32api.GetMonitorInfo(h)["Device"].endswith("\\"+name)]
    dx,dy = ctypes.c_uint(),ctypes.c_uint()
    assert ctypes.windll.shcore.GetDpiForMonitor(ctypes.c_void_p(int(monitor)),0,
                                                 ctypes.byref(dx),ctypes.byref(dy))==0
    x,y,right,bottom = bounds
    width,height = 642,358
    reverse = direction.startswith("reverse")
    assert direction in ("forward","reverse","forward-odd","reverse-odd")
    l,t = (right-x-width-1,bottom-y-height-1) if reverse else (103,137)
    crop=(l,t,l+width,t+height)
    print(name, bounds, "DPI:", dx.value, "selection:",crop,direction,flush=True)
    with sync_playwright() as pw:
        previous_cursor=win32api.GetCursorPos()
        proc,page=launch(pw,fixtures/"vfr.mp4")
        ready=multiprocessing.Event()
        target=multiprocessing.Process(target=card,args=(bounds,crop,ready))
        try:
            page.locator("#mode").select_option("region")
            device=next(d["id"] for d in page.evaluate("catalog.displays") if d["id"].endswith(name))
            page.locator("#source").select_option(device)
            page.locator("#output").select_option("")
            page.locator("#cursor").uncheck()
            target.start()
            assert ready.wait(8)
            record=page.locator("#record");gif=page.locator("#recordGif")
            assert not record.is_enabled() and not gif.is_enabled()
            def pick(title):
                page.get_by_role("button",name=title,exact=True).click()
                win=Desktop(backend="win32").window(process=proc.pid,title="框选录制区域")
                win.wait("visible",timeout=6)
                time.sleep(.25)
                return win.handle
            def drag(a,b):
                win32api.SetCursorPos(a);time.sleep(.08);win32api.mouse_event(C.MOUSEEVENTF_LEFTDOWN,0,0)
                time.sleep(.08);win32api.SetCursorPos(b);time.sleep(.12)
            def rgb_at(i,j):
                dc=G.GetDC(0)
                try:return G.GetPixel(dc,i,j)
                finally:G.ReleaseDC(0,dc)
            outline=Desktop(backend="win32").window(process=proc.pid,title="FrameNote 选区边框")
            pick("框选区域")
            # A bright stripe and its grey surroundings must remain legible under the picker.
            bright=rgb_at(x+l+15,y+t+60)&255;gray=rgb_at(x+l+50,y+t+60)&255
            assert bright>175 and 55<gray<120 and bright-gray>100,(bright,gray)
            drag((x+l+24,y+t+32),(x+l+240,y+t+148))
            win32api.mouse_event(C.MOUSEEVENTF_LEFTUP,0,0)
            outline.wait("visible",timeout=4)
            assert record.is_enabled() and gif.is_enabled()
            old=G.GetWindowRect(outline.handle)
            picker=pick("重新框选")
            G.PostMessage(picker,C.WM_KEYDOWN,C.VK_ESCAPE,0)
            outline.wait("visible",timeout=4)
            assert G.GetWindowRect(outline.handle)==old
            pick("重新框选")
            def overlays():
                handles=[]
                def visit(h,_):
                    if (G.IsWindowVisible(h) and win32process.GetWindowThreadProcessId(h)[1]==proc.pid
                        and G.GetWindowLong(h,C.GWL_EXSTYLE)&C.WS_EX_TOPMOST):
                        handles.append(h)
                G.EnumWindows(visit,None)
                return handles
            until=time.monotonic()+6
            while not (windows:=overlays()) and time.monotonic()<until:time.sleep(.05)
            assert windows,"No selection overlay"
            hwnd=windows[0];time.sleep(.3)
            ox,oy=G.ClientToScreen(hwnd,(0,0));_,_,ow,oh=G.GetClientRect(hwnd)
            overlay=(ox,oy,ox+ow,oy+oh)
            print("Overlay bounds:",overlay,flush=True)
            a=(x+l,y+t); b=(x+l+width,y+t+height)
            if reverse:a,b=b,a
            if direction.endswith("-odd"):
                sign=-1 if reverse else 1
                b=(b[0]+sign,b[1]+sign)
            assert G.WindowFromPoint(a)==hwnd,("Selection start outside overlay",a,overlay)
            drag(a,b)
            dc=G.GetDC(0)
            try:
                # The blue outline must appear beside the same four physical edges.
                def blue(i,j):
                    rgb=G.GetPixel(dc,i,j)
                    return ((rgb>>16)&255)-(rgb&255)>20
                cx,cy=x+l+width//2,y+t+height//2
                assert all(any(blue(px+d,cy) for d in range(-2,3)) for px in (x+l,x+l+width))
                assert all(any(blue(cx,py+d) for d in range(-2,3)) for py in (y+t,y+t+height))
            finally:G.ReleaseDC(0,dc)
            win32api.mouse_event(C.MOUSEEVENTF_LEFTUP,0,0)
            outline.wait("visible",timeout=4)
            def check_outline():
                time.sleep(.2)
                assert G.GetWindowRect(outline.handle)==(x+l-3,y+t-3,x+l+width+3,y+t+height+3)
                for px,py in [(x+l-1,cy),(cx,y+t-1)]:
                    rgb=rgb_at(px,py)
                    assert ((rgb>>16)&255)-(rgb&255)>25,(px,py,rgb)
                    assert G.WindowFromPoint((px,py))!=outline.handle
            check_outline()
            assert page.locator("#record").inner_text()!="停止录制"
            record.click()
            wait(page,"document.querySelector('#record').textContent==='停止录制'",timeout=10000)
            check_outline()
            time.sleep(1.6);page.locator("#record").click()
            wait(page,"temporary && ready",timeout=15000)
            clip=Path(url2pathname(urlsplit(page.evaluate("v.currentSrc")).path))
            measure(clip,width,height)
            assert not outline.exists(timeout=.2)
            assert overlay==bounds,(overlay,bounds)
        finally:
            win32api.mouse_event(C.MOUSEEVENTF_LEFTUP,0,0)
            win32api.SetCursorPos(previous_cursor)
            if target.pid:target.terminate();target.join()
            if proc.poll() is None:stop(proc)


if __name__=="__main__":run()
