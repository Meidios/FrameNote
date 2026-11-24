"""Exercise the shared title bar using real Windows non-client input."""
import ctypes
from ctypes import wintypes
import os
import subprocess
import sys
import time

from PIL import Image, ImageChops, ImageGrab
from playwright.sync_api import sync_playwright
from pywinauto import Desktop, keyboard, mouse
from pywinauto.timings import wait_until
from pywinauto.win32structures import GUITHREADINFO
import win32api
import win32con
import win32gui

from review_ui import app, at, desk, fixtures, launch, settings, stop, switch_file, wait, work


profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)


def foreground(win):
    win32gui.SetWindowPos(win.handle, win32con.HWND_TOPMOST, 0, 0, 0, 0,
                          win32con.SWP_NOMOVE | win32con.SWP_NOSIZE)
    win.set_focus()


def check_native_frame(hwnd, fullscreen=False):
    user = ctypes.windll.user32
    user.GetWindowDpiAwarenessContext.restype = ctypes.c_void_p
    user.AreDpiAwarenessContextsEqual.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    context = user.GetWindowDpiAwarenessContext(wintypes.HWND(hwnd))
    assert user.AreDpiAwarenessContextsEqual(context, -4), "Window would be bitmap-scaled across monitors"
    if sys.getwindowsversion().build >= 22000:
        corners = wintypes.DWORD()
        assert ctypes.windll.dwmapi.DwmGetWindowAttribute(hwnd, 33, ctypes.byref(corners), ctypes.sizeof(corners)) == 0
        assert corners.value == (1 if fullscreen else 2)


def history_buttons(win):
    return [win.child_window(auto_id=id, control_type="Button") for id in ("UndoButton","RedoButton")]


def check_history(proc,win,page):
    undo,redo=history_buttons(win)
    assert not undo.is_enabled() and not redo.is_enabled()
    page.locator("#point").click()
    undo.wait("enabled")
    assert not redo.is_enabled()
    at(page,7)
    page.locator("#splitClip").click()
    clips=page.locator("#videoClips .editClip")
    assert clips.count()==2
    undo.click_input()
    wait(page,"$('videoClips').children.length===1 && doc.notes.length===1")
    redo.wait("enabled")
    mouse.move(coords=tuple(undo.rectangle().mid_point()))
    frame=win.rectangle()
    buttons=redo.rectangle()
    win.capture_as_image().crop((0,0,buttons.right-frame.left+8,buttons.bottom-frame.top+5)).save(work/"history-buttons.png")
    redo.click_input()
    wait(page,"$('videoClips').children.length===2")
    keyboard.send_keys("^z",vk_packet=False)
    wait(page,"$('videoClips').children.length===1")
    undo.click_input()
    wait(page,"doc.notes.length===0")
    redo.click_input()
    wait(page,"doc.notes.length===1")
    page.locator("#point").click()
    wait_until(3,.05,lambda:not redo.is_enabled())
    page.locator("#stage").click()
    switch_file(proc,page,fixtures/"60.mp4")
    wait_until(3,.05,lambda:not undo.is_enabled() and not redo.is_enabled())


def gear_opens_settings(proc):
    win = desk(proc)
    foreground(win)
    button = win.child_window(auto_id="SettingsGear", control_type="Button")
    button.wait("visible")
    assert win32gui.WindowFromPoint(tuple(button.rectangle().mid_point())) == win.handle
    button.click_input()
    dlg = Desktop(backend="win32").window(process=proc.pid, title="设置")
    dlg.wait("ready")
    check_native_frame(dlg.handle)
    assert Desktop(backend="uia").window(handle=dlg.handle).child_window(title="通用", control_type="TabItem").is_selected()
    dlg.close()
    win.wait("ready")


def hit(hwnd, x, y):
    return win32gui.SendMessage(hwnd, win32con.WM_NCHITTEST, 0, win32api.MAKELONG(x, y))


def check_live_resize(win, page=None):
    foreground(win)
    hwnd = win.handle
    initial = win32gui.GetWindowRect(hwnd)
    dpi = ctypes.windll.user32.GetDpiForWindow(hwnd) / 96
    work_area = win32api.GetMonitorInfo(win32api.MonitorFromWindow(hwnd))["Work"]
    width = min(round(1180*dpi),work_area[2]-work_area[0]-90)
    height = min(round(760*dpi),work_area[3]-work_area[1]-90)
    win32gui.SetWindowPos(hwnd,0,work_area[0]+40,work_area[1]+40,width,height,win32con.SWP_NOZORDER)
    thread = ctypes.windll.user32.GetWindowThreadProcessId(hwnd,None)
    def sizing():
        info = GUITHREADINFO()
        info.cbSize = ctypes.sizeof(info)
        assert ctypes.windll.user32.GetGUIThreadInfo(thread,ctypes.byref(info))
        return bool(info.flags & 2)  # GUI_INMOVESIZE
    if page is None:
        children = []
        win32gui.EnumChildWindows(hwnd,lambda h,_: children.append(h) or True,None)
        surface = next(h for h in children if win32gui.GetParent(h)==hwnd and
                       win32gui.GetClassName(h)=="FrameNote.VideoSurface" and win32gui.IsWindowVisible(h))
    try:
        for side, hit_code in (("left", win32con.HTLEFT), ("top", win32con.HTTOP),
                               ("corner", win32con.HTBOTTOMRIGHT)):
            left, top, right, bottom = win32gui.GetWindowRect(hwnd)
            if side == "left":
                start = (left + 2, (top + bottom) // 2)
                direction = (1, 0)
            elif side == "top":
                start = ((left + right) // 2, top + 1)
                direction = (0, 1)
            else:
                start = (right - 2, bottom - 2)
                direction = (1, 1)
            assert hit(hwnd, *start) == hit_code
            if page:
                before = page.evaluate("[innerWidth,innerHeight]")
            else:
                before = win32gui.GetClientRect(surface)[2:]
            mouse.move(coords=start)
            assert win32gui.WindowFromPoint(start) == hwnd, (side,start,win.rectangle())
            mouse.press(coords=start)
            try:
                for distance in (12, 24, 36):
                    win32api.SetCursorPos(tuple(p+round(d*distance*dpi) for p,d in zip(start,direction)))
                    if distance == 12:
                        wait_until(2,.02,sizing)
                    # Verify the surface itself follows several sizes before button-up.
                    if page:
                        wait(page, """([previous,dx,dy])=>
                            (!dx || innerWidth!==previous[0]) && (!dy || innerHeight!==previous[1])""",
                             [before,*direction])
                        before = page.evaluate("[innerWidth,innerHeight]")
                    else:
                        wait_until(2,.02,lambda: all(not d or size!=old for d,size,old in
                                   zip(direction,win32gui.GetClientRect(surface)[2:],before)))
                        before = win32gui.GetClientRect(surface)[2:]
            finally:
                mouse.release()
                wait_until(2,.02,lambda: not sizing())
            if page:
                assert page.evaluate("""()=>{const fit=$('overlay').getBoundingClientRect(),[w,h]=sourceSize();
                    return Math.abs(fit.width/fit.height-w/h)<.01}""")
    finally:
        left,top,right,bottom=initial
        win32gui.SetWindowPos(hwnd,0,left,top,right-left,bottom-top,win32con.SWP_NOZORDER)
        wait_until(2,.02,lambda: win32gui.GetWindowRect(hwnd)==initial)


def menu_highlight_visible(win, item):
    rect, frame = item.rectangle(), win.rectangle()
    dpi = ctypes.windll.user32.GetDpiForWindow(win.handle) / 96
    tile = win.capture_as_image().crop((rect.left-frame.left, rect.top-frame.top,
                                       rect.right-frame.left, rect.bottom-frame.top)).convert("RGB")
    y = tile.height//2
    fill = tile.getpixel((round(8*dpi), y))
    if fill == tile.getpixel((round(2*dpi), y)):
        return False
    mask = ImageChops.difference(tile, Image.new("RGB", tile.size, fill)).convert("L").point(lambda v: 0 if v else 255)
    # The first menu item can include an antialiased pixel from the window frame.
    inset = round(2*dpi)
    left, top, right, bottom = mask.crop((inset, 0, tile.width-inset, tile.height)).getbbox()
    # A keyboard focus outline can cover one pixel of fill on each side.
    return abs(right-left-42*dpi) <= 2*dpi and abs(bottom-top-26*dpi) <= 2*dpi


def check_menu_labels(win, verify_highlights=False):
    for name, key in (("文件", "f"), ("帮助", "h")):
        item = win.child_window(title=name, control_type="MenuItem", depth=3).wrapper_object()
        if verify_highlights:
            win.set_focus()
            def hover():
                win32api.SetCursorPos(tuple(item.rectangle().mid_point()))
                return menu_highlight_visible(win, item)
            wait_until(3, .05, hover)
            if key == "f":
                win.capture_as_image().crop((0, 0, 138, 34)).save(work / "menu-hover.png")
        item.click_input()
        wait_until(3, .05, lambda: item.get_expand_state() == 1)
        if verify_highlights:
            wait_until(3, .05, lambda: menu_highlight_visible(win, item))
        keyboard.send_keys("{ESC}")
        wait_until(3, .05, lambda: item.get_expand_state() == 0)
        win.child_window(auto_id="SettingsGear").set_focus()
        keyboard.send_keys("%" + key, vk_packet=False)
        wait_until(3, .05, lambda: item.get_expand_state() == 1)
        keyboard.send_keys("{ESC}")


def check_caption(win):
    hwnd = win.handle
    check_native_frame(hwnd)
    left, top, _, _ = win32gui.GetWindowRect(hwnd)
    buttons = wintypes.RECT()
    assert ctypes.windll.dwmapi.DwmGetWindowAttribute(hwnd, 5, ctypes.byref(buttons), ctypes.sizeof(buttons)) == 0
    width = buttons.right - buttons.left
    y = top + (buttons.top + buttons.bottom) // 2
    gear = win.child_window(auto_id="SettingsGear").rectangle()
    menu = win.child_window(title_re="文件.*", control_type="MenuItem").rectangle()
    assert menu.top <= gear.mid_point().y <= menu.bottom and gear.top <= y <= gear.bottom
    assert hit(hwnd, gear.right - 1, gear.mid_point().y) != win32con.HTMINBUTTON
    for index, expected in enumerate((win32con.HTMINBUTTON, win32con.HTMAXBUTTON, win32con.HTCLOSE)):
        x = left + buttons.left + round((index + .5) * width / 3)
        assert hit(hwnd, x, y) == expected, (index, x, y, hit(hwnd, x, y))
    assert hit(hwnd, *gear.mid_point()) == win32con.HTCLIENT
    drag = ((menu.right + gear.left) // 2, gear.mid_point().y)
    assert hit(hwnd, *drag) == win32con.HTCAPTION
    return drag


def check_caption_glyphs(win, image):
    gear = win.child_window(auto_id="SettingsGear").rectangle()
    frame = win.rectangle()
    dpi = ctypes.windll.user32.GetDpiForWindow(win.handle) / 96
    radius = round(12 * dpi)
    y = gear.mid_point().y - frame.top
    boxes = []
    for i in range(4):
        x = gear.mid_point().x - frame.left + i * gear.width()
        tile = image.crop((x-radius, y-radius, x+radius, y+radius)).convert("RGB")
        background = Image.new("RGB", tile.size, tile.getpixel((0, 0)))
        ink = ImageChops.difference(tile, background).convert("L").point(lambda v: 255 if v > 36 else 0)
        box = ink.getbbox()
        assert box, (i, gear, frame)
        boxes.append((x-radius+(box[0]+box[2])/2, (box[1]+box[3])/2, box[3]-box[1]))
    gaps = [b[0]-a[0] for a, b in zip(boxes, boxes[1:])]
    assert max(gaps)-min(gaps) <= 2*dpi, (boxes, gaps)
    assert max(b[1] for b in boxes)-min(b[1] for b in boxes) <= 1.5*dpi, boxes
    assert abs(boxes[0][2]-14*dpi) <= 1, boxes


def caption_positions(win):
    frame = win32gui.GetWindowRect(win.handle)
    buttons = [win.child_window(title=name, control_type="MenuItem", depth=3) for name in ("文件","帮助")]
    buttons += [win.child_window(auto_id=name) for name in ("UndoButton","RedoButton","SettingsGear")]
    rects = [(r.left,r.top,r.right,r.bottom) for r in (button.rectangle() for button in buttons)]
    top = min(rect[1] for rect in rects)
    strip = ImageGrab.grab((frame[0], top, frame[2], max(rect[3] for rect in rects)), all_screens=True)
    ink = []
    for left, y, right, bottom in rects:
        tile = strip.crop((left-frame[0], y-top, right-frame[0], bottom-top)).convert("L")
        threshold = tile.getpixel((1, tile.height//2))
        bounds = tile.point(lambda pixel: 255 if abs(pixel-threshold)>36 else 0).getbbox()
        assert bounds, "Missing title bar glyph"
        ink.append(bounds)
    return frame, rects, ink


def check_fullscreen_round_trips(win, enter, leave):
    original = ctypes.windll.user32.IsZoomed(win.handle)
    gear = win.child_window(auto_id="SettingsGear")
    try:
        for maximized in (False, True):
            win32gui.ShowWindow(win.handle, win32con.SW_MAXIMIZE if maximized else win32con.SW_RESTORE)
            wait_until(3, .05, lambda: bool(ctypes.windll.user32.IsZoomed(win.handle)) == maximized)
            frame = win.rectangle()
            mouse.move(coords=tuple(frame.mid_point()))
            time.sleep(.4)  # Let the desktop maximize/restore animation finish.
            before = caption_positions(win)
            for _ in range(3):
                enter()
                check_native_frame(win.handle, fullscreen=True)
                assert not gear.exists(timeout=.1)
                leave()
                gear.wait("visible")
                # Hovering over a stale icon repaints it and masks the regression.
                # Compare screen pixels as well as the automation hit rectangles.
                wait_until(2, .05, lambda: caption_positions(win) == before)
            check_caption(win)
    finally:
        win32gui.ShowWindow(win.handle, win32con.SW_MAXIMIZE if original else win32con.SW_RESTORE)


def check_gear_hover(win, screenshot):
    rect = win.child_window(auto_id="SettingsGear").rectangle()
    frame = win.rectangle()
    dpi = ctypes.windll.user32.GetDpiForWindow(win.handle) / 96
    bounds = (rect.left-frame.left, rect.top-frame.top, rect.right-frame.left, rect.bottom-frame.top)
    outside = (rect.left-round(20*dpi), rect.mid_point().y)
    mouse.move(coords=outside)
    idle = win.capture_as_image().crop(bounds).convert("RGB")
    background = Image.new("RGB", idle.size, idle.getpixel((2, idle.height//2)))
    glyph = ImageChops.difference(idle, background).convert("L").point(lambda v: 255 if v > 36 else 0).getbbox()
    assert glyph
    # Even the transparent part of the caption button should respond.
    mouse.move(coords=(rect.left+round(5*dpi), rect.mid_point().y))
    try:
        def hover_bounds():
            image = win.capture_as_image().crop(bounds).convert("RGB")
            return ImageChops.difference(image, idle).getbbox()
        wait_until(2, .05, lambda: hover_bounds() is not None)
        face = hover_bounds()
        assert abs(face[2]-face[0]-26*dpi) <= 1 and abs(face[3]-face[1]-26*dpi) <= 1, face
        for axis in (0, 1):
            assert abs((face[axis]+face[axis+2])-(glyph[axis]+glyph[axis+2])) <= 2, (face, glyph)
        image = win.capture_as_image()
        image.crop((bounds[0]-8, max(0, bounds[1]-2), image.width, bounds[3]+5)).save(work / screenshot)
    finally:
        mouse.move(coords=outside)
    wait_until(2, .05, lambda: hover_bounds() is None)

    # Compare the visible keyboard outline to the hover fill in screen pixels.
    win.child_window(title="文件", control_type="MenuItem", depth=3).set_focus()
    keyboard.send_keys("{RIGHT}")
    gear = win.child_window(auto_id="SettingsGear")
    gear.set_focus()
    wait_until(2, .05, gear.has_keyboard_focus)
    def focus_bounds():
        tile = win.capture_as_image().crop(bounds).convert("RGB")
        mask = Image.new("L", tile.size)
        mask.putdata([255 if b-r > 45 and g-r > 25 else 0 for r,g,b in tile.get_flattened_data()])
        return mask.getbbox()
    wait_until(2, .05, lambda: focus_bounds() is not None)
    assert focus_bounds() == face, (focus_bounds(), face)
    mouse.move(coords=tuple(rect.mid_point()))
    wait_until(2, .05, lambda: hover_bounds() == face and focus_bounds() == face)
    win.capture_as_image().crop((bounds[0]-8, max(0, bounds[1]-2), frame.width(), bounds[3]+5)).save(work / ("focus-" + screenshot))
    mouse.click(coords=outside)


def check_review_fonts(page):
    page.evaluate("document.fonts.ready")
    session = page.context.new_cdp_session(page)
    try:
        session.send("DOM.enable")
        session.send("CSS.enable")
        root = session.send("DOM.getDocument")["root"]["nodeId"]

        def faces(selector):
            node = session.send("DOM.querySelector", {"nodeId": root, "selector": selector})["nodeId"]
            fonts = session.send("CSS.getPlatformFontsForNode", {"nodeId": node})["fonts"]
            assert fonts and all(f["isCustomFont"] for f in fonts), fonts
            return {f["postScriptName"] for f in fonts}

        heading = "#reviewPanel .sideTitle h2"
        assert page.locator(heading).evaluate("e=>[getComputedStyle(e).fontSize,getComputedStyle(e).fontWeight]") == ["17px", "500"]
        assert faces(heading) == {"NotoSansSC-Medium", "Inter-Regular"}
        assert faces("#sourceTitle") == {"NotoSansSC-Bold"}
        assert faces("#refreshSources") == {"NotoSansSC"}
        assert faces("#welcome h1") == {"Inter-Bold"}
        assert faces("#clock") == {"Inter-Regular"}
        assert page.locator("#clock").evaluate("e=>getComputedStyle(e).fontVariantNumeric") == "tabular-nums"
    finally:
        session.detach()


with sync_playwright() as pw:
    proc, page = launch(pw, None, "typeof render==='function'")
    try:
        win = desk(proc)
        hwnd = win.handle
        foreground(win)
        time.sleep(.4)  # Let the DWM finish the window opening animation before capture.
        img = win.capture_as_image()
        assert all(b.is_visible() and not b.is_enabled() for b in history_buttons(win))
        check_caption_glyphs(win, img)
        img.crop((0, 0, img.width, 85)).save(work / "titlebar.png")
        check_gear_hover(win, "gear-hover.png")
        client_width = win32gui.GetClientRect(hwnd)[2]
        check_review_fonts(page)
        web_width = page.evaluate("Math.round(innerWidth*devicePixelRatio)")
        assert abs(client_width - web_width) <= 1, (client_width, web_width)
        drag = check_caption(win)
        check_menu_labels(win, verify_highlights=True)
        mouse.click(button="right", coords=drag)
        menu = Desktop(backend="win32").window(process=proc.pid, class_name="#32768")
        menu.wait("visible")
        keyboard.send_keys("{ESC}")
        menu.wait_not("exists")
        gear_opens_settings(proc)
        settings(proc).child_window(title="取消", control_type="Button").invoke()
        win.wait("ready")
        before = win32gui.GetWindowRect(hwnd)
        edge = (before[2]-2, (before[1]+before[3])//2)
        mouse.press(coords=edge)
        mouse.move(coords=(edge[0]+40,edge[1]))
        mouse.release(coords=(edge[0]+40,edge[1]))
        wait_until(3, .05, lambda: win32gui.GetWindowRect(hwnd)[2] > before[2]+30)
        before = win32gui.GetWindowRect(hwnd)
        mouse.press(coords=drag)
        mouse.move(coords=(drag[0]+60, drag[1]+36))
        mouse.release(coords=(drag[0]+60, drag[1]+36))
        wait_until(4, .05, lambda: win32gui.GetWindowRect(hwnd)[0] != before[0])
        drag = check_caption(win)
        mouse.double_click(coords=drag)
        wait_until(4, .05, lambda: ctypes.windll.user32.IsZoomed(hwnd))
        check_caption(win)
        time.sleep(.4)
        img = win.capture_as_image()
        check_caption_glyphs(win, img)
        img.crop((0, 0, img.width, 90)).save(work / "maximized-titlebar.png")
        check_gear_hover(win, "maximized-gear-hover.png")
        gear_opens_settings(proc)
        mouse.double_click(coords=check_caption(win))
        wait_until(4, .05, lambda: not ctypes.windll.user32.IsZoomed(hwnd))
        win.set_focus()
        before = win32gui.GetWindowRect(hwnd)
        keyboard.send_keys("{VK_LWIN down}{LEFT}{VK_LWIN up}")
        wait_until(4, .05, lambda: win32gui.GetWindowRect(hwnd) != before)
        check_caption(win)
    finally:
        stop(proc)

    proc, page = launch(pw, fixtures / "vfr.mp4")
    try:
        win = desk(proc)
        foreground(win)
        check_history(proc,win,page)
        def enter():
            page.locator("#fullPreview").click()
            wait(page,"isFullPreview()")
        def leave():
            page.keyboard.press("Escape")
            wait(page,"!isFullPreview()")
        check_fullscreen_round_trips(win, enter, leave)
        check_live_resize(win,page)
    finally:
        stop(proc)

    photo = work / "titlebar-image.png"
    Image.new("RGB", (100, 80), "#345678").save(photo)
    proc, page = launch(pw, photo, "typeof imageDoc==='function' && imageDoc()")
    try:
        win = desk(proc)
        check_live_resize(win,page)
        check_caption(win)
        assert photo.name in win.window_text()
        check_menu_labels(win)
        gear_opens_settings(proc)
        undo,redo=history_buttons(win)
        page.locator("#point").click()
        undo.wait("enabled")
        undo.click_input()
        wait(page,"doc.notes.length===0")
        redo.click_input()
        wait(page,"doc.notes.length===1")
    finally:
        stop(proc)

player = subprocess.Popen([str(app.with_name("FrameNote Player.exe")), str(fixtures / "vfr.mp4")])
try:
    win = desk(player)
    win.child_window(auto_id="Full").wait("ready")
    assert all(b.is_visible() and not b.is_enabled() for b in history_buttons(win))
    check_live_resize(win)
    gear_opens_settings(player)
    check_menu_labels(win)
    full = win.child_window(auto_id="Full")
    full.wait("ready")
    full.set_focus()
    def enter_player():
        full.invoke()
        wait_until(5,.05,lambda: "退出全屏" in full.window_text())
    check_fullscreen_round_trips(win, enter_player, lambda: keyboard.send_keys("{ESC}"))
    close = win.rectangle()
    y = win.child_window(auto_id="SettingsGear").rectangle().mid_point().y
    minimize = next(x for x in range(close.right-200,close.right) if hit(win.handle,x,y)==win32con.HTMINBUTTON)
    mouse.click(coords=(minimize+3, y))
    wait_until(4, .05, lambda: win32gui.IsIconic(win.handle))
    win32gui.ShowWindow(win.handle, win32con.SW_RESTORE)
    win.wait("ready")
    foreground(win)
    x = close.right-20
    assert hit(win.handle, x, y) == win32con.HTCLOSE
    mouse.click(coords=(x, y))
    player.wait(timeout=5)
finally:
    if player.poll() is None:
        stop(player)
print("PASS: title bar, settings, native caption buttons, drag, maximize, snap and fullscreen.", work, flush=True)
