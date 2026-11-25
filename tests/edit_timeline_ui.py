"""Real WebView2 timeline edits, persistence, gaps, decoded export and audio timing."""
import array,hashlib,json,math,os,subprocess,time,uuid,zipfile,io
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import url2pathname
from PIL import Image,ImageChops,ImageStat
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import exported_file, launch,stop,wait,work
profile=work/'profile';profile.mkdir();(profile/'updates.json').write_text('{"automatic":false}');os.environ['FRAMENOTE_DATA_DIR']=str(profile)
source=work/'source.mp4';voice=work/'voice.m4a';package=work/'input.framenote';saved=work/'edited.framenote';out=work/'edited.mp4'
subprocess.run(['ffmpeg','-v','error','-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=14','-f','lavfi','-i','sine=frequency=440:duration=14','-af',"volume='if(between(t,3,4),0.01,1)':eval=frame",'-c:v','libx264','-c:a','aac','-shortest',str(source)],check=True)
subprocess.run(['ffmpeg','-v','error','-f','lavfi','-i','sine=frequency=1100:duration=3','-c:a','aac',str(voice)],check=True)
hash=hashlib.sha256(voice.read_bytes()).hexdigest()
doc={'version':1,'id':str(uuid.uuid4()),'fileName':'source.mp4','durationUs':14000000,'lastNoteNumber':1,'notes':[{'id':str(uuid.uuid4()),'number':1,'startUs':6000000,'endUs':9000000,'text':'voice region','shape':None,'voice':{'asset':hash,'durationUs':3000000}}]}
with zipfile.ZipFile(package,'w') as z:z.writestr('review.json',json.dumps(doc));z.write(source,'media/source.mp4');z.write(voice,'voice/'+hash+'.m4a')
def dialog(proc,path):
    d=Desktop(backend='win32').window(process=proc.pid,class_name='#32770');d.wait('ready',timeout=15);time.sleep(.3);d.child_window(class_name='Edit',found_index=0).set_edit_text(str(path));d.child_window(control_id=1).click()
    if not path.exists() and d.exists(timeout=.5) and d.is_visible():d.child_window(class_name='Edit',found_index=0).type_keys('{ENTER}')
def drag(p,selector,delta,edge=None,preview_start=None,while_held=None,cancel=False):
    el=p.locator(selector);box=el.bounding_box();axis=p.locator('#ticks').bounding_box();duration=p.evaluate('doc.durationUs');x=box['x']+(box['width']-3 if edge=='right' else 3 if edge=='left' else box['width']/2);y=box['y']+box['height']/2
    p.mouse.move(x,y);p.mouse.down();p.mouse.move(x+delta/duration*axis['width'],y,steps=8)
    if preview_start is not None:
        preview=el.evaluate("el=>parseFloat(el.style.left)*doc.durationUs/100")
        assert abs(preview-preview_start)<duration*1e-6,(preview,preview_start)
    if while_held:while_held()
    if cancel:p.keyboard.press('Escape')
    p.mouse.up();time.sleep(.15)

def media_images(p):
    return [Image.open(io.BytesIO(p.locator('#'+track+'Clips').screenshot())).convert('RGB') for track in ('video','audio')]

def check_trim_preview(p,before,first,last):
    video=p.locator('#videoClips .editClip').bounding_box()
    audio=p.locator('#audioClips .editClip').bounding_box()
    assert abs(video['x']-audio['x'])<1 and abs(video['width']-audio['width'])<1,(video,audio)
    for track,clip,old,shown in zip(('video','audio'),(video,audio),before,media_images(p)):
        top=round(clip['y']-p.locator('#'+track+'Clips').bounding_box()['y'])
        box=(round(first*old.width),top+4,round(last*old.width),old.height-5)
        retained=old.crop(box)
        assert len(retained.getcolors(retained.width*retained.height))>2
        difference=ImageChops.difference(retained,shown.crop(box))
        # Ignore the playhead and the uniform tint change under a pressed clip.
        for channel in difference.split():
            tint=ImageStat.Stat(channel).median[0]
            matching=sum(channel.histogram()[max(0,tint-2):tint+3])
            assert matching>.98*channel.width*channel.height,(tint,matching,box)

def check_marquee(proc,p):
    for sec in (2,4,6,9,12):
        p.evaluate('us=>seekUs(us)',sec*1000000)
        wait(p,'us=>!v.seeking && Math.abs(shown-us)<1000',sec*1000000)
        p.locator('#splitClip').click()
    baseline=p.evaluate('doc.timeline')
    def state():
        return p.evaluate('doc.timeline')
    def selected():
        return set(p.locator('.editClip[aria-pressed=true]').evaluate_all('els=>els.map(el=>el.dataset.id)'))
    def ids(track,starts):
        return {c['id'] for c in baseline['clips'] if c['track']==track and c['startUs']/1000000 in starts}
    def point(sec,track,strip=False):
        row=p.locator('#'+track+'Clips').bounding_box()
        return (row['x']+row['width']*sec/14,row['y']+(2 if strip else row['height']/2))
    def marquee(a,b,expected,shift=False,cancel=False):
        if shift:p.keyboard.down('Shift')
        p.mouse.move(*a);p.mouse.down();p.mouse.move(*b,steps=6)
        assert p.locator('#clipMarquee').is_visible() and selected()==expected,(expected,selected())
        if cancel:p.keyboard.press('Escape')
        p.mouse.up()
        if shift:p.keyboard.up('Shift')
        assert p.locator('#clipMarquee').is_hidden()
    pair=ids('video',(4,6))
    marquee(point(4.2,'video',True),point(8,'video',True),pair)
    assert selected()==pair and p.locator('#deleteClip').is_enabled()
    all_tracks=pair|ids('audio',(4,6))|ids('voice',(6,))
    marquee(point(8,'voice'),point(4.2,'video'),all_tracks,shift=True,cancel=True)
    assert selected()==pair and state()==baseline,(selected(),pair,state()==baseline,p.locator('#status').text_content())
    marquee(point(8,'voice'),point(4.2,'video'),all_tracks,shift=True)
    assert selected()==all_tracks
    marquee(point(8,'video'),point(4.2,'video'),pair,shift=True)
    p.mouse.move(*point(6,'video'));p.keyboard.down('Control');p.mouse.wheel(0,-120);p.keyboard.up('Control')
    wait(p,"$('timeline').style.width==='200%' && $('scroll').scrollLeft>0")
    marquee(point(8,'audio',True),point(4.2,'video',True),pair|ids('audio',(4,6)))
    p.keyboard.down('Control');p.mouse.wheel(0,120);p.keyboard.up('Control')
    wait(p,"$('timeline').style.width==='100%'")
    marquee(point(4.2,'video',True),point(8,'video'),pair)
    member=next(iter(ids('video',(4,))))
    clip='[data-id="'+member+'"]'
    def preview():
        assert state()==baseline and selected()==pair
        assert abs(p.locator(clip).evaluate('el=>parseFloat(el.style.left)*doc.durationUs/100')-7000000)<2
    drag(p,clip,6400000,while_held=preview,cancel=True)
    assert state()==baseline and selected()==pair
    drag(p,clip,6400000,while_held=preview)
    reordered=state()
    for track in ('video','audio'):
        assert sorted((c['sourceUs']//1000000,c['startUs']//1000000) for c in reordered['clips'] if c['track']==track)==[(0,0),(2,2),(4,7),(6,9),(9,4),(12,12)]
    assert p.evaluate("doc.notes[0].startUs===9000000 && doc.timeline.clips.find(c=>c.track==='voice').startUs===9000000")
    p.keyboard.press('Control+z')
    assert state()==baseline and selected()==pair
    p.keyboard.press('Control+Shift+z')
    assert state()==reordered and selected()==pair
    saved_group=work/'group.framenote'
    p.locator('#save').click();exported_file(proc,saved_group)
    p.keyboard.press('Control+z');p.keyboard.press('Delete')
    assert len(state()['clips'])==8 and not p.evaluate('doc.notes') and not selected()
    p.keyboard.press('Control+z');assert state()==baseline and selected()==pair
    p.locator('#linkTracks').click()
    p.locator('#deleteClip').click()
    remaining=state()['clips']
    assert [c for c in remaining if c['track']!='video']==[c for c in baseline['clips'] if c['track']!='video']
    assert {c['id'] for c in remaining}=={c['id'] for c in baseline['clips']}-pair
    before_move=state()
    first_pair=ids('video',(0,2))
    marquee(point(.3,'video',True),point(3.5,'video'),first_pair)
    first='[data-id="'+next(iter(ids('video',(0,))))+'"]'
    # Move both clips into the gap, with snapping disabled for this drag.
    p.keyboard.down('Alt');drag(p,first,4300000);p.keyboard.up('Alt')
    start_by_id={c['id']:c['startUs'] for c in state()['clips']}
    shifts=[start_by_id[c['id']]-c['startUs'] for c in remaining if c['id'] in first_pair]
    assert len(set(shifts))==1 and abs(shifts[0]-4300000)<40000,shifts
    assert all(start_by_id[c['id']]==c['startUs'] for c in remaining if c['id'] not in first_pair)
    p.keyboard.press('Control+z');assert state()==before_move
    p.keyboard.press('Control+z');p.keyboard.press('Control+z')
    assert state()==baseline
    for _ in range(5):p.keyboard.press('Control+z')
    assert state() is None
    print('PASS: live marquee, reverse cross-track selection, cancel, group move and reorder, both delete controls and undo/redo.',flush=True)
    return saved_group,reordered

def check_reorder(proc,p):
    def split_at(us):
        p.evaluate('t=>seekUs(t)',us)
        wait(p,'t=>!v.seeking && Math.abs(shown-t)<1000',us)
        p.locator('#splitClip').click()
    def track(name):
        return sorted(p.evaluate('t=>doc.timeline.clips.filter(c=>c.track===t)',name),key=lambda c:c['startUs'])
    def drag_to(clip,us,check_preview=None,cancel=False):
        middle=clip['startUs']+clip['durationUs']/2
        selector='#videoClips [data-id="'+clip['id']+'"]'
        drag(p,selector,us-middle,while_held=check_preview,cancel=cancel)
    def order(name):
        return [c['sourceUs'] for c in track(name)]
    def undo():
        p.keyboard.press('Control+z')
    split_at(4000000)
    a,b=track('video')
    saved=p.evaluate('doc.timeline')
    def preview():
        for name in ('video','audio'):
            first,second=sorted((c for c in saved['clips'] if c['track']==name),key=lambda c:c['startUs'])
            for clip,us in [(second,0),(first,b['durationUs'])]:
                x=p.locator('[data-id="'+clip['id']+'"]').evaluate('el=>parseFloat(el.style.left)*doc.durationUs/100')
                assert abs(x-us)<14,(name,x,us)
        assert abs(float(p.locator('#markLanes button').get_attribute('data-start'))*14000000-2000000)<1
        assert p.evaluate('doc.timeline')==saved
    drag_to(b,500000,preview,cancel=True)
    assert p.evaluate('doc.timeline')==saved
    drag_to(b,500000,preview)
    assert order('video')==order('audio')==[4000000,0]
    assert track('voice')[0]['startUs']==p.evaluate('doc.notes[0].startUs')==2000000
    p.evaluate('seekUs(0)')
    wait(p,'!v.seeking && Math.abs(v.currentTime-4)<.035')
    review=work/'reordered.framenote'
    p.locator('#save').click();exported_file(proc,review)
    wait(p,"!busy && $('status').textContent.startsWith('审阅包已保存')")
    with zipfile.ZipFile(review) as z:
        timeline=json.loads(z.read('review.json'))['timeline']
        assert sorted((c['startUs'],c['sourceUs']) for c in timeline['clips'] if c['track']=='video')==[(0,4000000),(10000000,0)]
    exported=work/'reordered.mp4'
    p.locator('#exportVideo').click();exported_file(proc,exported)
    wait(p,"!busy && $('status').textContent.startsWith('带批注的 MP4 已保存')",timeout=90000)
    def video_frame(path,sec):
        data=subprocess.check_output(['ffmpeg','-v','error','-ss',str(sec),'-i',str(path),'-frames:v','1','-f','image2pipe','-c:v','png','-'])
        return Image.open(io.BytesIO(data)).convert('RGB').crop((0,0,320,180))
    for timeline_sec,source_sec in [(1,5),(11,1)]:
        actual=video_frame(exported,timeline_sec)
        def difference(sec):
            return sum(ImageStat.Stat(ImageChops.difference(actual,video_frame(source,sec))).mean)/3
        assert difference(source_sec)<difference(timeline_sec)/2
    undo()
    assert p.evaluate('doc.timeline')==saved
    p.keyboard.press('Control+Shift+z')
    drag_to(track('video')[0],13500000)
    assert p.evaluate('doc.timeline')==saved
    split_at(10000000)
    p.locator('#linkTracks').click()
    audio,notes=track('audio'),p.evaluate('doc.notes')
    drag_to(track('video')[-1],500000)
    assert order('video')==[10000000,0,4000000] and track('audio')==audio and p.evaluate('doc.notes')==notes
    for _ in range(6):undo()
    assert p.evaluate('doc.timeline') is None
    print('PASS: packed clip reordering by mouse, linked previews, decoded first frame, undo/redo and independent three-clip insertion.',flush=True)
with sync_playwright() as pw:
    proc,p=launch(pw,package);errors=[];p.on('pageerror',lambda e:errors.append(str(e)))
    try:
        window=Desktop(backend='win32').window(process=proc.pid,title_re='.*FrameNote.*',found_index=0)
        window.move_window(width=1440,height=900)
        wait(p,"$('viewer').clientWidth>730 && !$('alignClips').hasAttribute('popover')")
        images=Path(url2pathname(urlsplit(p.evaluate('imageBase')).path))
        assert (images/(hash+'.m4a')).read_bytes()==voice.read_bytes()
        assert p.locator('#videoClips .editClip').count()==1
        assert p.locator('#voiceClips .editClip').count()==1
        tools=p.get_by_role('group',name='片段剪辑')
        for name,icon in [('轨道联动','link'),('分割片段','scissors'),('贴到上一片段','arrow-left-to-line'),('贴到下一片段','arrow-right-to-line'),('删除片段','trash')]:
            button=tools.get_by_role('button',name=name,exact=True)
            assert button.is_visible() and not button.inner_text().strip(),name
            assert button.locator('use').get_attribute('href')=='#icon-'+icon
        assert tools.get_by_role('button',name='删除片段').is_disabled()
        for track,name in [('video','视频'),('audio','原声'),('voice','语音批注')]:
            clip=p.locator('#'+track+'Clips').get_by_role('button',name=name,exact=True)
            assert clip.is_visible() and not clip.inner_text().strip()
        wait(p,"""()=>{const images=[...document.querySelectorAll('#sourceFrames image')];
            return images.length>0 && images.every(im=>im.hasAttribute('href')) && $('sourceWave').hasAttribute('d')}""",timeout=15000)
        assert '#1' in p.locator('#markLanes').inner_text()
        check_reorder(proc,p)
        saved_group,reordered=check_marquee(proc,p)
        p.locator('#trackDesk').screenshot(path=str(work/'timeline-clips.png'))
        join=tools.get_by_role('button',name='贴到上一片段')
        join_next=tools.get_by_role('button',name='贴到下一片段')
        video='#videoClips .editClip'
        assert join.is_disabled() and join_next.is_disabled()
        p.evaluate('seekUs(0)')
        wait(p,'!v.seeking && transport.paused')
        original_media=media_images(p)
        drag(p,video,-4000000,'right',while_held=lambda:check_trim_preview(p,original_media,.10,.67))
        check_trim_preview(p,original_media,.10,.67)
        state=p.evaluate('doc.timeline')
        drag(p,video,2000000,'left',while_held=lambda:check_trim_preview(p,original_media,.18,.67),cancel=True)
        assert p.evaluate('doc.timeline')==state
        drag(p,video,2000000,'left')
        drag(p,video,-3000000,'left',while_held=lambda:check_trim_preview(p,original_media,.03,.67))
        check_trim_preview(p,original_media,.03,.67)
        # A split on the video track alone leaves a longer original-audio clip.
        # Trimming inside that audio clip must preview both surviving pieces.
        baseline=p.evaluate('doc.timeline')
        p.locator('#linkTracks').click()
        p.locator(video).click()
        p.evaluate('seekUs(5000000)')
        wait(p,'shown>4800000 && shown<5100000')
        p.locator('#splitClip').click()
        p.locator('#linkTracks').click()
        assert p.locator(video).count()==2 and p.locator('#audioClips .editClip').count()==1
        audio=p.locator('#audioClips .editClip:visible')
        def spans():
            return audio.evaluate_all("els=>els.map(el=>[el.style.left,el.style.width])")
        preview=[]
        def partial_trim():
            assert audio.count()==2
            assert audio.nth(1).bounding_box()['x']==p.locator(video).nth(1).bounding_box()['x']
            preview.extend(spans())
        drag(p,video+':nth-child(2)',1000000,'left',while_held=partial_trim)
        assert spans()==preview
        for _ in range(4):p.keyboard.press('Control+z')
        assert p.evaluate('doc.timeline')==baseline
        p.locator(video).click()
        assert join.is_disabled()
        limit=p.evaluate('doc.durationUs-doc.timeline.clips.find(c=>c.track==="video").durationUs')
        join_next.click()
        assert p.evaluate('EditModel.end(doc.timeline.clips.find(c=>c.track==="video"))===doc.durationUs')
        assert join_next.is_disabled()
        p.keyboard.press('Control+z')
        assert join_next.is_enabled()
        drag(p,video,-1000000,preview_start=0)
        def linked_move():
            v=p.locator(video).bounding_box()
            a=p.locator('#audioClips .editClip').bounding_box()
            assert abs(v['x']-a['x'])<1 and abs(v['width']-a['width'])<1,(v,a)
            assert p.evaluate('doc.timeline.clips.find(c=>c.track==="audio").startUs')==0
        drag(p,video,limit+1000000,preview_start=limit,while_held=linked_move)
        assert p.evaluate('doc.timeline.clips.find(c=>c.track==="video").startUs')==limit
        p.keyboard.press('Control+z')
        p.evaluate('transport.currentTime=12.125')
        duration=p.evaluate('doc.timeline.clips.find(c=>c.track==="video").durationUs')
        drag(p,video,12125000-duration+15000)
        assert p.evaluate('EditModel.end(doc.timeline.clips.find(c=>c.track==="video"))')==12125000
        p.keyboard.press('Control+z')
        p.evaluate('transport.currentTime=2.125')
        drag(p,video,2140000)
        assert p.evaluate('doc.timeline.clips.find(c=>c.track==="video").startUs')==2125000
        join.click()
        assert p.evaluate('doc.timeline.clips.find(c=>c.track==="video").startUs')==0
        assert join.is_disabled()
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
        # Release well inside the neighboring clip from each side. Verify the preview too.
        drag(p,video+':nth-child(1)',moved+1500000,preview_start=moved)
        assert p.evaluate('doc.timeline.clips.find(c=>c.track==="video").startUs')==moved
        p.keyboard.press('Control+z')
        drag(p,video+':nth-child(2)',-moved-1500000,preview_start=split)
        assert p.evaluate('doc.timeline.clips.filter(c=>c.track==="video")[1].startUs')==split
        p.keyboard.press('Control+z')
        p.locator(video).first.click()
        join_next.click()
        aligned=p.evaluate('doc.timeline.clips.filter(c=>c.track==="video")')
        assert aligned[0]['startUs']+aligned[0]['durationUs']==aligned[1]['startUs']==second['startUs']
        assert join_next.is_disabled()
        p.keyboard.press('Control+z')
        p.locator(video).nth(1).click()
        p.evaluate('seekUs(6000000)');wait(p,"v.style.visibility==='hidden'")
        p.locator('#play').click();time.sleep(.25);assert p.evaluate('transport.currentTime')>6.1;p.locator('#play').click()
        p.keyboard.press('Control+z');assert p.evaluate('doc.notes[0].startUs')==6000000
        p.keyboard.press('Control+Shift+z');assert p.evaluate('doc.notes[0].startUs')==6000000+moved
        join.click()
        assert p.evaluate('doc.timeline.clips.filter(c=>c.track==="video")[1].startUs')==split
        assert p.evaluate('doc.notes[0].startUs')==6000000 and join.is_disabled()
        p.keyboard.press('Control+z')
        # Snap the second video segment back to the first segment's tail.
        drag(p,'#videoClips .editClip:nth-child(2)',-moved+10000)
        assert p.evaluate('doc.timeline.clips.filter(c=>c.track==="video")[1].startUs')==split,(split,moved,p.evaluate('doc.timeline'),p.locator('#status').inner_text())
        # Move again, then detach original audio without moving other tracks.
        drag(p,'#videoClips .editClip:nth-child(2)',2000000)
        p.locator('#linkTracks').click();before=p.evaluate('doc.notes')
        assert tools.get_by_role('button',name='轨道联动',pressed=False).locator('use').get_attribute('href')=='#icon-unlink'
        still=p.locator(video).nth(1).bounding_box()
        def detached_move():
            assert p.locator(video).nth(1).bounding_box()==still
            assert p.locator('#audioClips .editClip').nth(1).bounding_box()['x']>still['x']+5
        drag(p,'#audioClips .editClip:nth-child(2)',1000000,while_held=detached_move)
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
        p.locator('#save').click();exported_file(proc,saved);wait(p,"$('status').textContent.startsWith('审阅包已保存')",timeout=15000)
        expected=p.evaluate('doc');p.screenshot(path=str(work/'timeline.png'))
        p.locator('#exportVideo').click();exported_file(proc,out);wait(p,"!busy && $('status').textContent.startsWith('带批注的 MP4 已保存')",timeout=180000)
        assert not errors,errors
    finally:stop(proc)
    assert not images.parent.exists(),images.parent
    assert not (profile/'Drafts').exists()
    proc,p=launch(pw,saved_group)
    try:
        actual=p.evaluate('doc.timeline')
        assert {c['id']:(c['startUs'],c['sourceUs'],c['durationUs']) for c in actual['clips']}=={c['id']:(c['startUs'],c['sourceUs'],c['durationUs']) for c in reordered['clips']}
        assert p.evaluate('doc.notes[0].startUs')==9000000
    finally:stop(proc)
    proc,p=launch(pw,package)
    try:
        assert p.evaluate('doc.timeline == null && doc.notes[0].startUs===6000000')
        assert p.locator('#videoClips .editClip').count()==1
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
