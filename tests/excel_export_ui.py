"""Export from the actual review window and verify the portable workbook and media."""
import json
import math
import os
import shutil
import subprocess
import time
import uuid
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

from PIL import Image, ImageDraw
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import exported_file, export_window, launch, settings, stop, wait, work

profile = work / 'profile'
profile.mkdir()
(profile / 'updates.json').write_text('{"automatic":false}')
(profile / 'shortcuts.json').write_text('{"record":[]}')
os.environ['FRAMENOTE_DATA_DIR'] = str(profile)
source = work / 'source.mp4'
parent = work / '交付 #1'
parent.mkdir()
(profile/'capture.json').write_text(json.dumps({'exportDirectory':str(parent)}))


def gray(frame):
    return 30 + (frame * 13) % 180


with subprocess.Popen(['ffmpeg', '-v', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24',
                       '-s', '192x108', '-r', '24', '-i', '-', '-c:v', 'libx264',
                       '-crf', '0', '-pix_fmt', 'yuv420p', str(source)], stdin=subprocess.PIPE) as encoder:
    for frame in range(34 * 24):
        image = Image.new('RGB', (192, 108), (gray(frame),) * 3)
        paint = ImageDraw.Draw(image)
        paint.rectangle((50, 35, 95, 65), fill=(34, 95, 142))
        encoder.stdin.write(image.tobytes())
    encoder.stdin.close()
    assert encoder.wait() == 0


def note(number, start, end, text, kind=None):
    shape = None if kind is None else dict(kind=kind, x=.25, y=.25, toX=.75, toY=.75)
    return dict(id=str(uuid.uuid4()), number=number, startUs=start, endUs=end,
                text=text, shape=shape, color='#ff6644')


literal = '=1+1\n第二行反馈'
notes = [note(9, 8_010_000, None, literal, 'box'),
         note(3, 8_010_000, None, "'同帧独立批注"),
         note(6, 120_000, 31_370_000, '完整区间', 'magnifier'),
         note(8, 5_001_000, 5_011_000, '短区间')]


def package(name, entries, duration, timeline=None, video=source):
    path = work / (name + '.framenote')
    doc = dict(id=str(uuid.uuid4()), version=3 if timeline else 1, fileName=name + '.mp4',
               durationUs=duration, lastNoteNumber=max(n['number'] for n in entries), notes=entries)
    if timeline:
        doc['timeline'] = timeline
    with zipfile.ZipFile(path, 'w') as archive:
        archive.writestr('review.json', json.dumps(doc))
        archive.write(video, 'media/source.mp4')
    return path


def export(proc, page, cancel=False):
    previous = page.locator('#status').inner_text()
    page.locator('#exportExcel').click()
    if cancel:
        dialog=export_window(proc)
        dialog.child_window(auto_id="Message",title_re="正在导出 Excel 媒体 [2-4]/.*").wait('visible',timeout=30,retry_interval=.05)
        assert 0 < dialog.child_window(auto_id='Bar').iface_range_value.CurrentValue < 100
        dialog.child_window(auto_id='DismissExport').invoke()
        wait(page, "!busy && $('status').textContent.includes('已取消 Excel')")
        assert not list(parent.glob('.framenote-excel-*'))
        return
    exported_file(proc)
    wait(page, "old=>!busy && $('status').textContent!==old && ($('status').textContent.startsWith('Excel 批注已导出') || $('status').classList.contains('error'))", previous, timeout=90000)
    assert not page.locator('#status').evaluate("el=>el.classList.contains('error')"), page.locator('#status').inner_text()
    return Path(page.locator('#status').inner_text().split('：', 1)[1])

fields = ('Number', 'Type', 'Start', 'End', 'Text', 'Preview', 'Media')


def choose_template(dlg, selected, source_title, zipped):
    for control, checked in [('ExcelColumn' + name, name in selected) for name in fields] + [
            ('ExcelShowSource', source_title), ('ExcelZip', zipped)]:
        box = dlg.child_window(auto_id=control)
        if bool(box.get_toggle_state()) != checked:
            box.toggle()


def finish_settings(dlg, confirm=True):
    native = Desktop(backend='win32').window(handle=dlg.handle)
    dlg.child_window(title='保存' if confirm else '取消', control_type='Button').invoke()
    native.wait_not('exists')


def inspect_columns(book, expected, title=True):
    with zipfile.ZipFile(book) as archive:
        sheet = ET.fromstring(archive.read('xl/worksheets/sheet1.xml'))
        strings = [''.join(s.itertext()) for s in ET.fromstring(archive.read('xl/sharedStrings.xml'))]
        rows = sheet.findall('.//{*}sheetData/{*}row')
        headers = [strings[int(cell.findtext('{*}v'))] for cell in rows[int(title)].findall('{*}c')]
        assert headers == expected and len(rows) == 3 + int(title), (headers, len(rows))
        assert ('变帧率.mp4' in strings) == title
        previews = [name for name in archive.namelist() if name.startswith('xl/media/')]
        assert len(previews) == (2 if '画面预览' in headers else 0)
        links = sheet.findall('.//{*}hyperlink')
        assert len(links) == (2 if '媒体文件' in headers else 0)
        if links:
            rels = ET.fromstring(archive.read('xl/worksheets/_rels/sheet1.xml.rels'))
            targets = {rel.get('Target') for rel in rels if rel.get('TargetMode') == 'External'}
            assert targets == {'media/Mark-1.png', 'media/Region-2.gif'}
            assert all((book.parent / target).is_file() for target in targets)


with sync_playwright() as pw:
    proc, page = launch(pw, source)
    try:
        assert page.locator('#exportExcel').is_disabled()
    finally:
        stop(proc)
    proc, page = launch(pw, package('审阅 A', notes, 34_000_000))
    try:
        export(proc, page, cancel=True)
        assert not list(parent.iterdir())
        workbook = export(proc, page)
        copy = export(proc, page)
        assert workbook.is_file() and copy.is_file() and copy.parent != workbook.parent
    finally:
        stop(proc)
    timeline = dict(sourceDurationUs=34_000_000, linked=True, clips=[
        dict(id=str(uuid.uuid4()), track='video', startUs=1_000_000, sourceUs=2_000_000, durationUs=1_000_000)])
    edited = package('剪辑', [note(1, 500_000, None, '黑帧'), note(2, 1_500_000, None, '源片 2.5 秒')], 2_000_000, timeline)
    proc, page = launch(pw, edited)
    try:
        edit_book = export(proc, page)
    finally:
        stop(proc)
    vfr = work / 'variable.mp4'
    subprocess.run(['ffmpeg','-v','error','-i',str(source),'-t','1.5','-vf',
                    "select='if(lt(t,0.6),not(mod(n,4)),not(mod(n,2)))'",
                    '-fps_mode','vfr','-c:v','libx264','-crf','0',str(vfr)],check=True)
    variable = package('变帧率', [note(1, 360_000, None, '帧间定位'), note(2, 360_000, 1_010_000, '跨帧率区间')],
                       1_500_000, video=vfr)
    proc, page = launch(pw, variable)
    try:
        vfr_book = export(proc, page)
        dlg = settings(proc, '表格设置')
        assert all(dlg.child_window(auto_id='ExcelColumn'+field).get_toggle_state() for field in fields)
        assert not dlg.child_window(auto_id='ExcelZip').get_toggle_state()
        dlg.capture_as_image().save(work / 'spreadsheet-settings.png')
        choose_template(dlg, [], False, True)
        dlg.child_window(title='保存', control_type='Button').invoke()
        dlg.child_window(title='请至少选择一列导出。', control_type='Text').wait('visible')
        dlg.child_window(title='恢复本页默认值', control_type='Button').invoke()
        assert all(dlg.child_window(auto_id='ExcelColumn'+field).get_toggle_state() for field in fields)
        assert not dlg.child_window(auto_id='ExcelZip').get_toggle_state()
        choose_template(dlg, ['Text'], False, True)
        finish_settings(dlg, confirm=False)
        dlg = settings(proc, '表格设置')
        assert dlg.child_window(auto_id='ExcelColumnPreview').get_toggle_state()
        choose_template(dlg, ['Text'], False, True)
        finish_settings(dlg)
    finally:
        stop(proc)
    proc, page = launch(pw, variable)
    try:
        text_zip = export(proc, page)
        assert text_zip.suffix == '.zip'
        with zipfile.ZipFile(text_zip) as bundle:
            assert bundle.namelist() == [vfr_book.name]
            bundle.extractall(work / 'only-text')
        inspect_columns(work / 'only-text' / vfr_book.name, ['批注内容'], title=False)
        dlg = settings(proc, '表格设置')
        choose_template(dlg, ['Media'], False, True)
        finish_settings(dlg)
        media_zip = export(proc, page)
        assert media_zip != text_zip and text_zip.is_file()
        with zipfile.ZipFile(media_zip) as bundle:
            assert set(bundle.namelist()) == {vfr_book.name, 'media/Mark-1.png', 'media/Region-2.gif'}
            bundle.extractall(work / 'unpacked')
        inspect_columns(work / 'unpacked' / vfr_book.name, ['媒体文件'], title=False)
        dlg = settings(proc, '表格设置')
        choose_template(dlg, ['Preview'], True, False)
        finish_settings(dlg)
        embedded = export(proc, page)
        assert list(embedded.parent.iterdir()) == [embedded]
        inspect_columns(embedded, ['画面预览'])
    finally:
        stop(proc)


def pixel(path, xy=(180, 95)):
    with Image.open(path) as im:
        return im.convert('RGB').getpixel(xy)


assert max(pixel(edit_book.parent / 'media/Mark-1.png')) < 8
assert abs(pixel(edit_book.parent / 'media/Mark-2.png')[0] - gray(60)) < 5
assert abs(pixel(vfr_book.parent / 'media/Mark-1.png')[0] - gray(8)) < 5
source_frames = [i for i in range(36) if i % (4 if i < 15 else 2) == 0]
with Image.open(vfr_book.parent / 'media/Region-2.gif') as animation:
    assert animation.n_frames == 8
    for i in range(animation.n_frames):
        animation.seek(i)
        index = max(frame for frame in source_frames if frame/24 <= .36+i/12)
        assert abs(animation.convert('RGB').getpixel((180,95))[0]-gray(index)) < 5
relocated = work / '移到另一个位置'
shutil.move(workbook.parent, relocated)
workbook = relocated / workbook.name
media = relocated / 'media'
assert abs(pixel(media / 'Mark-9.png')[0] - gray(192)) < 4
r, g, b = pixel(media / 'Mark-9.png', (96, 27))
assert r > g + 75 and r > b + 75, (r, g, b)
assert max(pixel(media / 'Mark-3.png', (96, 27))) - min(pixel(media / 'Mark-3.png', (96, 27))) < 5
with Image.open(media / 'Region-6.gif') as animation:
    assert animation.n_frames == 375 and animation.info['loop'] == 0
    total_ms = 0
    for i in range(animation.n_frames):
        animation.seek(i)
        expected = gray(math.floor((.12 + i/12) * 24))
        value = animation.convert('RGB').getpixel((180, 95))[0]
        assert abs(value - expected) < 5, (i, value, expected)
        total_ms += animation.info['duration']
    assert abs(total_ms - 31250) <= 10, total_ms
with Image.open(media / 'Region-8.gif') as tiny:
    assert tiny.n_frames == 1 and tiny.info['duration'] == 10

with zipfile.ZipFile(workbook) as archive:
    root = ET.fromstring(archive.read('xl/worksheets/_rels/sheet1.xml.rels'))
    targets = [r.attrib['Target'] for r in root if r.get('TargetMode') == 'External']
    assert set(targets) == {'media/' + p.name for p in media.iterdir()}, targets
    assert all((relocated / target).is_file() for target in targets)
    assert len([name for name in archive.namelist() if name.startswith('xl/media/')]) == 4
    assert not ET.fromstring(archive.read('xl/worksheets/sheet1.xml')).findall('.//{*}f')
    strings = [''.join(item.itertext()) for item in ET.fromstring(archive.read('xl/sharedStrings.xml'))]
    assert literal in strings and "'同帧独立批注" in strings
print('PASS: native export, cancellation, duplicate destination, relocated links and previews, exact PNG frame, '
      '31.25s and subframe GIFs, independent drawings, edited gap/offset.', workbook, flush=True)
