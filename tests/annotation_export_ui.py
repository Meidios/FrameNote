"""Verify same-frame drawings through a package round trip and decoded MP4 frames."""
import json
import os
import re
import subprocess
import time

from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import at, launch, notes, stop, wait, work

profile = work / 'profile'
profile.mkdir()
(profile / 'updates.json').write_text('{"automatic":false}')
os.environ['FRAMENOTE_DATA_DIR'] = str(profile)
source, package, output = (work / name for name in ('source.mp4', 'drawings.framenote', 'annotated.mp4'))
subprocess.run(['ffmpeg', '-v', 'error', '-f', 'lavfi', '-i',
                'testsrc2=size=1280x720:rate=30000/1001:duration=5,hue=s=0',
                '-c:v', 'libx264', '-crf', '12', '-pix_fmt', 'yuv420p', str(source)], check=True)


def save(page, proc, button, path):
    page.locator('#' + button).click()
    dialog = Desktop(backend='win32').window(process=proc.pid, class_name='#32770')
    dialog.wait('ready', timeout=10)
    time.sleep(.2)
    dialog.child_window(class_name='Edit', found_index=0).set_edit_text(str(path))
    dialog.child_window(control_id=1).click()
    wait(page, "name=>!busy && $('status').textContent.endsWith(name)", path.name, timeout=180000)


with sync_playwright() as pw:
    proc, p = launch(pw, source)
    try:
        at(p, 30)
        for tool, y in [('line', .2), ('box', .5), ('arrow', .8)]:
            p.locator('#' + tool).click()
            bounds = p.locator('#overlay').bounding_box()
            def point(x, y):
                return bounds['x'] + x*bounds['width'], bounds['y'] + y*bounds['height']
            p.mouse.move(*point(.2, y))
            p.mouse.down()
            p.mouse.move(*point(.7, y + (.15 if tool == 'box' else 0)))
            p.mouse.up()
            if tool=='line':p.locator('#body').fill('Same-frame Mark')
        assert [n['shape']['kind'] for n in notes(p)] == ['line', 'box', 'arrow']
        assert p.locator('#ink .inkBadge').count() == 3
        p.keyboard.press('Control+z')
        assert [n['shape']['kind'] for n in notes(p)] == ['line', 'box']
        p.keyboard.press('Control+Shift+z')
        assert len(notes(p)) == 3
        at(p, 90)
        p.locator('#stage').press('i')
        at(p, 106)
        p.keyboard.press('o')
        p.locator('#body').fill('Region')
        p.keyboard.press('Escape')
        at(p, 142)
        p.locator('#point').click()
        p.locator('#body').fill('Tail Mark')
        p.keyboard.press('Escape')
        expected = notes(p)
        save(p, proc, 'save', package)
    finally:
        stop(proc)
    proc, p = launch(pw, package)
    try:
        assert notes(p) == expected
        at(p, 30)
        assert p.locator('#ink .inkBadge').count() == 3
        save(p, proc, 'exportVideo', output)
    finally:
        stop(proc)

streams = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_streams', '-of', 'json', str(output)]))['streams']
assert len(streams) == 1 and streams[0]['codec_name'] == 'h264'
assert int(streams[0]['nb_frames']) == 150 and streams[0]['width'] == 1280


def cropped_frames(crop, pixel_format, stride):
    raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(output), '-vf', 'crop=' + crop,
                                   '-f', 'rawvideo', '-pix_fmt', pixel_format, '-'])
    assert len(raw) == stride * 150
    return (raw[i:i+stride] for i in range(0, len(raw), stride))


footer_height = streams[0]['height'] - 720
footer = cropped_frames('iw:ih-720:0:720', 'gray', 1280 * footer_height)
for i, pixels in enumerate(footer):
    assert (max(pixels) > 180) == (30 <= i < 60 or 90 <= i < 106 or 142 <= i), ('footer', i)
# The source is monochrome. Sample the colored stroke of each drawing on every frame.
for y in (.2, .5, .8):
    region = cropped_frames(f'24:16:600:{int(720*y)-8}', 'rgb24', 24 * 16 * 3)
    for i, pixels in enumerate(region):
        colored = any(max(pixel) - min(pixel) > 30 for pixel in zip(*[iter(pixels)] * 3))
        assert colored == (30 <= i < 60), ('drawing', y, i)

result = subprocess.run(['ffmpeg', '-i', str(source), '-i', str(output), '-filter_complex',
                         '[0:v]trim=end=0.9,setpts=PTS-STARTPTS[ref];[1:v]crop=1280:720:0:0,trim=end=0.9,setpts=PTS-STARTPTS[test];[ref][test]ssim',
                         '-f', 'null', '-'], capture_output=True, text=True, check=True)
ssim = float(re.search(r'All:([0-9.]+)', result.stderr).group(1))
ratio = output.stat().st_size / source.stat().st_size
assert ssim > .95 and ratio < 1, (ssim, ratio)
print(f'PASS: 150 decoded frames at 29.97 fps, three simultaneous drawings, undo/redo, package reopen, '
      f'1s Mark, Region, tail clipping and silent video; size {ratio:.1%}, SSIM {ssim:.6f}.', work)
