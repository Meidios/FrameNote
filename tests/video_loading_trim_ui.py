"""Check actual load progress and presented source frames while dragging a trim handle."""
import bisect
from pathlib import Path
import subprocess
import tempfile

from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import fixtures, launch, stop, wait, work, switch_file


def pick(proc,page,path):
    page.locator("#open").click()
    dialog=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
    dialog.wait("ready")
    edit=dialog.child_window(class_name="Edit",found_index=0)
    edit.set_edit_text(str(path))
    edit.type_keys("{ENTER}")


def trim(page,clip_id,side,destinations,pts,cancel=False):
    clip=page.locator(f'#videoClips button[data-id="{clip_id}"]')
    box=clip.locator(f'[data-edge="{side}"]').bounding_box()
    origin=page.evaluate("({shown, clips:doc.timeline?.clips??null})")
    source=page.evaluate("id=>currentEditDoc().timeline.clips.find(c=>c.id===id)",clip_id)
    duration=page.evaluate("doc.durationUs")
    start=source['startUs']+(source['durationUs'] if side=='right' else 0)
    x,y=box['x']+box['width']/2,box['y']+box['height']/2
    width=page.locator('#ticks').bounding_box()['width']
    page.mouse.move(x,y);page.mouse.down()
    for dest in destinations:
        page.mouse.move(x+(dest-start)*width/duration,y,steps=6)
        cut=page.evaluate("drag.value")
        assert abs(cut-dest)<duration*10/width,(cut,dest)
        source_us=source['sourceUs']+cut-source['startUs']-(side=='right')
        frame=pts[max(0,bisect.bisect_right(pts,source_us)-1)]
        wait(page,"t=>!v.seeking && Math.abs(lastPresented-t)<=2",frame)
        assert page.evaluate("scrubber.trimming() && getComputedStyle(v).visibility==='visible' && $('clock').textContent===fmt(drag.value)")
    if cancel:page.keyboard.press("Escape")
    page.mouse.up()
    wait(page,"!scrubber.trimming() && !v.seeking")
    if cancel:assert page.evaluate("doc.timeline?.clips")==origin['clips']
    assert abs(page.evaluate("shown")-origin['shown'])<=2
    return cut


with tempfile.TemporaryDirectory(prefix='FrameNote-long-') as media_dir,sync_playwright() as pw:
    long=Path(media_dir)/'one-hour.mp4'
    subprocess.run(["ffmpeg","-v","error","-stream_loop","1199","-i",str(fixtures/'60.mp4'),"-c","copy",str(long)],check=True)
    proc,p=launch(pw,None,"typeof ready!=='undefined' && !$('welcome').hidden")
    try:
        errors=[]
        p.on("pageerror",lambda e:errors.append(str(e)))
        p.evaluate("""window.loadStages=[];
            chrome.webview.addEventListener('message',({data:m})=>{
                if(m.type==='mediaLoading'||m.type==='loaded')
                    loadStages.push([m.type,$('mediaLoadTitle').textContent,$('mediaLoadBar').getAttribute('value'),!$('mediaLoading').hidden]);
            })""")
        pick(proc,p,long)
        wait(p,"!$('mediaLoading').hidden && $('mediaLoadBar').value>0 && $('mediaLoadBar').value<100")
        evidence=Path(tempfile.mkdtemp(prefix='FrameNote-load-'))/'progress.png'
        p.locator('#mediaLoading').screenshot(path=str(evidence),animations='allow')
        wait(p,"ready && !busy && $('mediaLoading').hidden",timeout=30000)
        stages=p.evaluate("loadStages")
        values=[int(value) for _,stage,value,visible in stages if stage=='正在读取视频帧…' and visible and value is not None]
        assert values==sorted(values) and values[0]==0 and values[-1]==100 and len(set(values))>3
        assert stages[-1][1:] == ['正在加载首帧…',None,True]
        print("Opened",round(long.stat().st_size/1e6),"MB,",p.evaluate("frames.length"),"frames. Progress screenshot:",evidence,flush=True)

        bad=work/'invalid.mp4';bad.write_bytes(b'broken file')
        pick(proc,p,bad)
        wait(p,"!busy && $('status').classList.contains('error') && $('mediaLoading').hidden")
        assert p.evaluate("ready && doc.fileName==='one-hour.mp4'")
        switch_file(proc,p,fixtures/'vfr.mp4')
        pts=p.evaluate("transport.pause();frames.slice()")
        p.evaluate("""window.lastPresented=-1;v.requestVideoFrameCallback(function frame(_,m){
            lastPresented=Math.round(m.mediaTime*1e6);v.requestVideoFrameCallback(frame);
        })""")
        mark=pts[len(pts)//2]
        p.evaluate("t=>seekUs(t)",mark);wait(p,"t=>shown===t && !v.seeking",mark)
        first=p.locator('#videoClips button').get_attribute('data-id')
        # Snapping to a VFR timestamp must show the frame immediately before a right cut.
        assert trim(p,first,'right',[mark+250000,mark-300000,mark],pts,cancel=True)==mark
        p.locator('#splitClip').click()
        first,second=p.evaluate("doc.timeline.clips.filter(c=>c.track==='video').sort((a,b)=>a.startUs-b.startUs).map(c=>c.id)")
        p.locator(f'#videoClips [data-id="{first}"]').click()
        p.locator('#deleteClip').click()
        # Move the second piece to zero, leaving its source start at the old split.
        node=p.locator(f'#videoClips [data-id="{second}"]').bounding_box()
        width=p.locator('#ticks').bounding_box()['width']
        x,y=node['x']+node['width']/2,node['y']+node['height']/2
        p.mouse.move(x,y);p.mouse.down();p.mouse.move(x-width*mark/p.evaluate("doc.durationUs"),y,steps=10);p.mouse.up()
        wait(p,"doc.timeline.clips.find(c=>c.track==='video').startUs===0")
        p.evaluate("seekUs(200000)");wait(p,"!v.seeking")
        cut=trim(p,second,'left',[550000,650000],pts)
        assert p.evaluate("doc.timeline.clips.find(c=>c.track==='video').startUs")==cut
        trim(p,second,'left',[400000,300000],pts,cancel=True)
        assert not errors,errors
        print("Right-edge VFR boundary, offset source, both drag directions, release, Escape and load failure passed",flush=True)
    finally:stop(proc)
