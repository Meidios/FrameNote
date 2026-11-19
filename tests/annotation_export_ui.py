"""Validate compression, Mark visibility, silent sources and MP4 source-picture quality."""
import io,json,os,re,subprocess,time
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import launch,stop,wait,work,at

profile=work/'profile';profile.mkdir();(profile/'updates.json').write_text('{"automatic":false}')
os.environ['FRAMENOTE_DATA_DIR']=str(profile)
source=work/'high-quality.mp4';out=work/'compressed.mp4'
subprocess.run(['ffmpeg','-v','error','-f','lavfi','-i','testsrc2=size=1280x720:rate=30:duration=5','-c:v','libx264','-crf','12','-pix_fmt','yuv420p',str(source)],check=True)
with sync_playwright() as pw:
    proc,p=launch(pw,source)
    try:
        at(p,0);p.locator('#point').click();p.locator('#body').fill('三秒 Mark · 输出画质验证')
        p.keyboard.press('Escape');p.locator('#exportVideo').click()
        dlg=Desktop(backend='win32').window(process=proc.pid,class_name='#32770');dlg.wait('ready');time.sleep(.2)
        dlg.child_window(class_name='Edit',found_index=0).set_edit_text(str(out));dlg.child_window(control_id=1).click()
        wait(p,"!busy && $('status').textContent.startsWith('带批注的 MP4 已保存')",timeout=180000)
    finally:stop(proc)
meta=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams','-of','json',str(out)]))
assert len(meta['streams'])==1 and meta['streams'][0]['codec_type']=='video'
assert meta['streams'][0]['nb_frames']=='150' and meta['streams'][0]['width']==1280
def frame(time):
    png=subprocess.check_output(['ffmpeg','-v','error','-ss',str(time),'-i',str(out),'-frames:v','1','-f','image2pipe','-c:v','png','-'])
    return Image.open(io.BytesIO(png)).convert('RGB')
first,last=frame(.5),frame(3.5)
bright=lambda im:sum(min(p)>180 for p in im.crop((0,720,im.width,im.height)).get_flattened_data())
assert bright(first)>100 and bright(last)==0
first.save(work/'mp4-mark-visible.png');last.save(work/'mp4-mark-finished.png')
result=subprocess.run(['ffmpeg','-i',str(source),'-i',str(out),'-filter_complex','[0:v]trim=start=3.2,setpts=PTS-STARTPTS[ref];[1:v]crop=1280:720:0:0,trim=start=3.2,setpts=PTS-STARTPTS[test];[ref][test]ssim','-f','null','-'],capture_output=True,text=True,check=True)
ssim=float(re.search(r'All:([0-9.]+)',result.stderr).group(1));assert ssim>.95,ssim
ratio=out.stat().st_size/source.stat().st_size;assert ratio<1,ratio
print(f'PASS: 5s/150 frames, silent video, Mark hidden after 3s; size {ratio:.1%} of CRF12 source, SSIM {ssim:.6f}.',work)
