"""Check source-pixel measurement and real 2x lens pixels in exported annotation images."""
import io,json,os,time,uuid,zipfile
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import launch,stop,wait,work

profile=work/'profile';profile.mkdir();(profile/'updates.json').write_text('{"automatic":false}')
os.environ['FRAMENOTE_DATA_DIR']=str(profile)
im=Image.new('RGB',(800,600));im.putdata([(x%256,y%256,(x+y)%256) for y in range(600) for x in range(800)])
png=io.BytesIO();im.save(png,format='PNG')
specs=[('line',.1,.1,.35,.15),('angle',.1,.3,.1,.5),('ruler',.55,.1,.9,.1),('magnifier',.55,.5,.9,.9),('text',.1,.6,.4,.85)]
notes=[]
for i,(kind,x,y,tx,ty) in enumerate(specs):
    shape=dict(kind=kind,x=x,y=y,toX=tx,toY=ty)
    if kind=='angle':shape.update(thirdX=.3,thirdY=.5)
    notes.append(dict(id=str(uuid.uuid4()),number=i+1,startUs=0,endUs=None,text='文字框实时更新' if kind=='text' else kind,color='#00ffff',shape=shape))
doc=dict(version=2,id=str(uuid.uuid4()),fileName='geometry.png',durationUs=0,lastNoteNumber=5,notes=notes)
package=work/'geometry.framenote'
with zipfile.ZipFile(package,'w') as z:z.writestr('review.json',json.dumps(doc));z.writestr('media/source.png',png.getvalue())
with sync_playwright() as pw:
    proc,p=launch(pw,package,"typeof imageDoc==='function' && imageDoc() && $('openedImage').naturalWidth===800")
    try:
        errors=[];p.on('pageerror',lambda e:errors.append(str(e)))
        assert '280.0 px' in p.locator('#ink').text_content()
        assert '90.0°' in p.locator('#ink').text_content()
        assert p.locator('#exportVideo').is_disabled() and p.locator('#voiceTools').is_hidden()
        p.locator('.note').last.click();p.locator('#body').fill('文字即时变化')
        assert '文字即时变化' in p.locator('#ink').text_content()
        p.keyboard.press('Escape')
        dest=work/'geometry.png';p.locator('#saveImage').click()
        dlg=Desktop(backend='win32').window(process=proc.pid,class_name='#32770');dlg.wait('ready');time.sleep(.2)
        dlg.child_window(class_name='Edit',found_index=0).set_edit_text(str(dest));dlg.child_window(control_id=1).click()
        wait(p,"!busy && $('status').textContent.startsWith('批注图已保存')",timeout=20000)
        result=Image.open(dest).convert('RGB');assert result.size==(800,600)
        assert result.getpixel((400,580))==im.getpixel((400,580))
        expected=im.getpixel((530,390));actual=result.getpixel((480,360))
        assert all(abs(a-b)<=2 for a,b in zip(actual,expected)),(actual,expected)
        assert result.getpixel((500,60))==(0,255,255)
        p.screenshot(path=str(work/'geometry-preview.png'));assert not errors,errors
    finally:stop(proc)
print('PASS: all five shapes, live text update, source-pixel ruler, 2x lens exported pixel match.',work)
