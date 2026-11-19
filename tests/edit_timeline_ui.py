"""Real WebView2 timeline edits, persistence, gaps, decoded export and audio timing."""
import array,hashlib,json,math,os,subprocess,time,uuid,zipfile,io
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import launch,stop,wait,work
profile=work/'profile';profile.mkdir();(profile/'updates.json').write_text('{"automatic":false}');os.environ['FRAMENOTE_DATA_DIR']=str(profile)
source=work/'source.mp4';voice=work/'voice.m4a';package=work/'input.framenote';saved=work/'edited.framenote';out=work/'edited.mp4'
subprocess.run(['ffmpeg','-v','error','-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=10','-f','lavfi','-i','sine=frequency=440:duration=10','-c:v','libx264','-c:a','aac','-shortest',str(source)],check=True)
subprocess.run(['ffmpeg','-v','error','-f','lavfi','-i','sine=frequency=1100:duration=3','-c:a','aac',str(voice)],check=True)
hash=hashlib.sha256(voice.read_bytes()).hexdigest()
doc={'version':1,'id':str(uuid.uuid4()),'fileName':'source.mp4','durationUs':10000000,'lastNoteNumber':1,'notes':[{'id':str(uuid.uuid4()),'number':1,'startUs':6000000,'endUs':9000000,'text':'voice region','shape':None,'voice':{'asset':hash,'durationUs':3000000}}]}
with zipfile.ZipFile(package,'w') as z:z.writestr('review.json',json.dumps(doc));z.write(source,'media/source.mp4');z.write(voice,'voice/'+hash+'.m4a')
def dialog(proc,path):
    d=Desktop(backend='win32').window(process=proc.pid,class_name='#32770');d.wait('ready',timeout=15);time.sleep(.3);d.child_window(class_name='Edit',found_index=0).set_edit_text(str(path));d.child_window(control_id=1).click()
    if not path.exists() and d.exists(timeout=.5) and d.is_visible():d.child_window(class_name='Edit',found_index=0).type_keys('{ENTER}')
def drag(p,selector,delta,edge=None):
    el=p.locator(selector);box=el.bounding_box();axis=p.locator('#ticks').bounding_box();duration=p.evaluate('doc.durationUs');x=box['x']+(box['width']-3 if edge=='right' else 3 if edge=='left' else box['width']/2);y=box['y']+box['height']/2
    p.mouse.move(x,y);p.mouse.down();p.mouse.move(x+delta/duration*axis['width'],y,steps=8);p.mouse.up();time.sleep(.15)
with sync_playwright() as pw:
    proc,p=launch(pw,package);errors=[];p.on('pageerror',lambda e:errors.append(str(e)))
    try:
        assert p.locator('#videoClips .editClip').count()==1
        assert p.locator('#voiceClips .editClip').count()==1
        p.evaluate('seekUs(5000000)');time.sleep(.2);p.locator('#splitClip').click()
        assert p.evaluate('doc.timeline.clips.filter(c=>c.track==="video").length')==2
        # SeekUs presents midpoint of source frame; split exactly at requested clock for deterministic test.
        split=p.evaluate('doc.timeline.clips.filter(c=>c.track==="video")[1].startUs')
        drag(p,'#videoClips .editClip:nth-child(2)',2000000)
        state=p.evaluate('doc');second=next(c for c in state['timeline']['clips'] if c['track']=='video' and c['startUs']>0)
        moved=second['startUs']-split;assert abs(moved-2000000)<30000,moved
        assert state['notes'][0]['startUs']==6000000+moved
        assert next(c for c in state['timeline']['clips'] if c['track']=='voice')['startUs']==6000000+moved
        assert next(c for c in state['timeline']['clips'] if c['track']=='audio' and c['startUs']>0)['startUs']==second['startUs']
        p.evaluate('seekUs(6000000)');wait(p,"v.style.visibility==='hidden'")
        p.locator('#play').click();time.sleep(.25);assert p.evaluate('transport.currentTime')>6.1;p.locator('#play').click()
        p.keyboard.press('Control+z');assert p.evaluate('doc.notes[0].startUs')==6000000
        p.keyboard.press('Control+Shift+z');assert p.evaluate('doc.notes[0].startUs')==6000000+moved
        # Snap the second video segment back to the first segment's tail.
        drag(p,'#videoClips .editClip:nth-child(2)',-moved+10000)
        assert p.evaluate('doc.timeline.clips.filter(c=>c.track==="video")[1].startUs')==split,(split,moved,p.evaluate('doc.timeline'),p.locator('#status').inner_text())
        # Move again, then detach original audio without moving other tracks.
        drag(p,'#videoClips .editClip:nth-child(2)',2000000)
        p.locator('#linkTracks').click();before=p.evaluate('doc.notes')
        drag(p,'#audioClips .editClip:nth-child(2)',1000000)
        assert p.evaluate('doc.notes')==before
        # Trim the voice by one second, leaving source offset intact.
        drag(p,'#voiceClips .editClip',1000000,'left')
        vc=p.evaluate('doc.timeline.clips.find(c=>c.track==="voice")');assert abs(vc['sourceUs']-1000000)<30000,vc
        # Deleting only the selected voice clip preserves notes and the project gap.
        note_before=p.evaluate('doc.notes');duration_before=p.evaluate('doc.durationUs')
        p.locator('#voiceClips .editClip').click();p.locator('#deleteClip').click()
        assert p.evaluate('doc.timeline.clips.every(c=>c.track!=="voice")');assert p.evaluate('doc.notes')==note_before
        assert p.evaluate('doc.durationUs')==duration_before
        p.keyboard.press('Control+z');assert p.locator('#voiceClips .editClip').count()==1
        p.locator('#linkTracks').click();p.locator('#videoClips .editClip:nth-child(2)').click();p.locator('#deleteClip').click()
        assert not p.evaluate('doc.notes');assert p.evaluate('doc.durationUs')==duration_before
        p.keyboard.press('Control+z');assert p.evaluate('doc.notes')==note_before
        p.keyboard.press('Control+z');assert p.locator('#linkTracks').get_attribute('aria-pressed')=='false'
        p.evaluate('seekUs(6000000)');p.locator('#saveFrame').click();gap=work/'gap.png';dialog(proc,gap)
        wait(p,"!busy && $('status').textContent.startsWith('当前帧已保存')",timeout=180000)
        assert max(max(px) for px in Image.open(gap).convert('RGB').get_flattened_data())<15
        p.locator('#save').click();dialog(proc,saved);wait(p,"$('status').textContent.startsWith('审阅包已保存')",timeout=15000)
        expected=p.evaluate('doc');p.screenshot(path=str(work/'timeline.png'))
        p.locator('#exportVideo').click();dialog(proc,out);wait(p,"!busy && $('status').textContent.startsWith('带批注的 MP4 已保存')",timeout=180000)
        assert not errors,errors
    finally:stop(proc)
    proc,p=launch(pw,saved)
    try:
        actual=p.evaluate('doc'); assert actual['timeline']['linked']==expected['timeline']['linked']
        for a,b in zip(actual['timeline']['clips'],expected['timeline']['clips']):
            for key,value in b.items():assert a[key]==value,(key,a,b)
        assert actual['notes']==expected['notes']
    finally:stop(proc)
subprocess.run(['ffmpeg','-v','error','-i',str(out),'-f','null','-'],check=True)
def picture(t):return Image.open(io.BytesIO(subprocess.check_output(['ffmpeg','-v','error','-ss',str(t),'-i',str(out),'-frames:v','1','-f','image2pipe','-c:v','png','-']))).convert('RGB')
im=picture(6);assert max(max(px) for px in im.crop((0,0,320,180)).get_flattened_data())<15
im=picture(7.6);assert sum(max(px)>50 for px in im.crop((0,0,320,180)).get_flattened_data())>1000
pcm=array.array('f',subprocess.check_output(['ffmpeg','-v','error','-i',str(out),'-vn','-ac','1','-ar','48000','-f','f32le','-']))
def energy(t):
    a=pcm[int(t*48000):int((t+.2)*48000)];return sum(x*x for x in a)/len(a)
assert energy(6)<1e-7,energy(6)
assert energy(8.5)>1e-4,energy(8.5)
print('PASS: actual drag/split/trim/link/detach/snap, gap clock, undo/redo, package restore, export black/silent gap and shifted audio.',work,flush=True)
