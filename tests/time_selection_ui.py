"""Exercise time selection, playback looping and decoded range exports in the desktop app."""
import hashlib
import json
import shutil
import subprocess
import tempfile
import uuid
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image
from playwright.sync_api import sync_playwright
from review_ui import exported_file, fixtures, launch, stop, switch_file, wait, work


def select_range(p,a,b,lane="#ticks"):
    axis=p.locator("#ticks").bounding_box()
    box=p.locator(lane).bounding_box()
    duration=p.evaluate("doc.durationUs/1e6")
    y=box["y"]+box["height"]/2
    x=lambda t: axis["x"]+t/duration*axis["width"]
    p.mouse.move(x(a),y);p.mouse.down(button="right")
    p.mouse.move(x(b),y,steps=8);p.mouse.up(button="right")
    return p.evaluate("timeSelection")


def looping(p,start,end):
    wait(p,"loopEnabled && !transport.paused")
    values=p.evaluate("""async ([a,b])=>{
      transport.currentTime=(b-80000)/1e6;
      const seen=[];
      for(let i=0;i<40;i++){await new Promise(r=>setTimeout(r,15));seen.push(transport.currentTime*1e6)}
      return seen;
    }""",[start,end])
    # Native currentTime can advance for one refresh before the next animation callback seeks.
    assert min(values)>=start-2 and max(values)<=end+35000 and any(b<a-30000 for a,b in zip(values,values[1:])),values


def resize(p,edge,to,leave_ruler=False):
    axis=p.locator("#ticks").bounding_box()
    times=p.evaluate("[timeSelection.startUs,timeSelection.endUs,doc.durationUs]")
    x=lambda us:axis["x"]+axis["width"]*us/times[2]
    y=axis["y"]+axis["height"]/2
    p.mouse.move(x(times[edge]),y);p.mouse.down()
    p.mouse.move(x(to-500000 if leave_ruler else to),y,steps=6)
    if leave_ruler:
        p.mouse.move(x(to-500000),y+axis["height"])
        p.mouse.move(x(to),y+axis["height"],steps=5)
    p.mouse.up()
    return p.evaluate("timeSelection")


def make_package(edited):
    def clip(track,start,length,offset,**options):
        return dict(id=str(uuid.uuid4()),track=track,startUs=start,durationUs=length,sourceUs=offset,**options)
    shape=dict(kind="magnifier",x=.1,y=.24,toX=.2,toY=.34,preview=dict(x=.5,y=.3),zoom=2)
    region=dict(id=str(uuid.uuid4()),number=1,startUs=500000,endUs=2500000,text="voice and lens",
                color="#FF0000",shape=shape,voice=dict(asset=asset,durationUs=2000000))
    d=dict(version=3 if edited else 1,id=str(uuid.uuid4()),durationUs=4000000,
           notes=[region],fileName=source.name,lastNoteNumber=1)
    if edited:
        media=[clip(t,0,1500000,1000000) for t in ("audio","video")]
        media.extend(clip(t,1750000,2250000,0) for t in ("audio","video"))
        media.append(clip("voice",500000,2000000,0,asset=asset,sourceDurationUs=2000000,noteId=region["id"]))
        d["timeline"]=dict(sourceDurationUs=4000000,linked=True,clips=media)
    path=work/("edited.framenote" if edited else "original.framenote")
    with zipfile.ZipFile(path,"w") as z:
        z.writestr("review.json",json.dumps(d));z.write(source,"media/source.mp4");z.write(voice,"voice/"+asset+".m4a")
    return path


def check_media(path,edited,gif=False):
    if gif:
        with Image.open(path) as im:
            count=im.n_frames;frames=[]
            for i in range(count):
                im.seek(i);frames.append(np.asarray(im.convert("RGB")))
    else:
        info=json.loads(subprocess.check_output(["ffprobe","-v","error","-show_streams","-of","json",path]))
        video,audio=info["streams"]
        assert abs(float(video["duration"])-1)<.001 and abs(float(audio["duration"])-1)<.023
        data=subprocess.check_output(["ffmpeg","-v","error","-i",path,"-f","rawvideo","-pix_fmt","rgb24","-an","-"])
        frames=np.frombuffer(data,np.uint8).reshape((-1,video["height"],video["width"],3));count=len(frames)
        # The red Region spans the selection. Check the played tint at the beginning,
        # halfway across, and over the complete progress strip on the final frame.
        for i,left,right in [(0,False,False),(count//2,True,False),(count-1,True,True)]:
            green=frames[i,-3,[25,205],1]
            assert (green>55).tolist()==[left,right],(i,green)
        pcm=np.frombuffer(subprocess.check_output(["ffmpeg","-v","error","-i",path,"-vn","-ac","1","-ar","48000","-f","f32le","-"]),np.float32)
        for at in (.15,.65,.90):
            wave=pcm[int(at*48000):int((at+.08)*48000)]
            hz=np.argmax(abs(np.fft.rfft(wave)))*48000/len(wave)
            assert abs(hz-930)<20,(at,hz)
    assert count==12,count
    for i,frame in enumerate(frames):
        origin=(24+i if i<6 else None if i<9 else i-9) if edited else 12+i
        expected=0 if origin is None else 32+origin*4
        assert abs(int(frame[105,195,0])-expected)<6,(path,i,origin,frame[105,195])
        # The preview magnifies a white patch in the source. The gap is black.
        if origin is not None:assert frame[54,144,0]>218,(path,i,frame[54,144])


source=work/"ramp.mp4";voice=work/"voice.m4a"
frames=np.zeros((48,144,240,3),np.uint8)
for i,frame in enumerate(frames):
    frame[:]=32+4*i;frame[32:54,21:51]=240
subprocess.run(["ffmpeg","-v","error","-f","rawvideo","-s","240x144","-r","12","-pix_fmt","rgb24","-i","-",
                "-f","lavfi","-i","anullsrc=r=48000:cl=stereo","-t","4","-c:a","aac","-c:v","libx264","-crf","10","-pix_fmt","yuv420p",source],
               input=frames.tobytes(),check=True)
subprocess.run(["ffmpeg","-v","error","-f","lavfi","-i","sine=frequency=930:duration=2","-c:a","aac",voice],check=True)
asset=hashlib.sha256(voice.read_bytes()).hexdigest()
with sync_playwright() as pw:
    for edited in (False,True):
        proc,p=launch(pw,make_package(edited))
        try:
            p.evaluate("transport.pause()")
            region=p.locator("#markLanes [data-id]").first
            region.dblclick()
            looping(p,500000,2500000)
            p.locator("#timeline").press("Escape")
            assert p.evaluate("!loopEnabled && timeSelection===null")
            if edited:
                before=select_range(p,1.02,1.49)
                el=p.locator('#videoClips .editClip [data-edge=right]').first.bounding_box()
                x,y=el["x"]+el["width"]/2,el["y"]+el["height"]/2
                p.mouse.move(x,y);p.mouse.down();p.mouse.move(x-25,y,steps=4)
                assert p.evaluate("drag.action==='right' && scrubber.trimming()")
                p.locator("#timeline").press("Escape");p.mouse.up()
                assert p.evaluate("timeSelection")==before
            selection=select_range(p,1.98,1.02,"#videoClips")
            assert selection==dict(startUs=1000000,endUs=2000000),selection
            p.evaluate("transport.pause()")
            assert p.locator("#rangeHint").count()==0
            assert resize(p,0,760000)==dict(startUs=750000,endUs=2000000)
            assert resize(p,1,2240000,leave_ruler=True)==dict(startUs=750000,endUs=2250000)
            assert resize(p,0,1010000)==dict(startUs=1000000,endUs=2250000)
            assert resize(p,1,1990000)==selection
            pos=p.evaluate("transport.currentTime")
            p.locator("#loop").click()
            p.mouse.move(2,2);p.wait_for_timeout(220)
            assert p.evaluate("loopEnabled && transport.paused && transport.currentTime")==pos
            assert p.evaluate("""['color','backgroundColor','borderTopColor'].every(
               key=>getComputedStyle($('linkTracks'))[key]===getComputedStyle($('loop'))[key])""")
            if not edited:
                proof=Path(tempfile.mkdtemp(prefix="FrameNote-loop-"))/"loop.png"
                p.locator(".transportTools").screenshot(path=str(proof))
                print("Enabled loop icon:",proof,flush=True)
            p.locator("#play").click()
            looping(p,1000000,2000000)
            replacement=select_range(p,2.02,2.48)
            assert replacement==dict(startUs=2000000,endUs=2500000)
            looping(p,2000000,2500000)
            assert select_range(p,1.02,1.98)==selection
            p.locator("#play").click();p.locator("#play").click()
            looping(p,1000000,2000000)
            p.locator("#exportVideo").click()
            check_media(exported_file(proc),edited)
            p.locator("#exportGif").hover();p.locator("#saveGif").click()
            gif=exported_file(proc)
            check_media(gif,edited,True)
            shutil.copyfile(gif,work/(("edited" if edited else "original")+".gif"))
            # Capture the menu's native request without replacing the interactive user's clipboard.
            # The same request can be delivered to the native host on a private window station.
            p.evaluate("""()=>{const post=chrome.webview.postMessage;
              chrome.webview.postMessage=function(m){
                if(m.type==='exportGif'&&m.copy){window.copiedRange=m;chrome.webview.postMessage=post}
                else post.call(this,m)
              }}""")
            p.locator("#exportGif").hover();p.locator("#copyGif").click()
            expected=dict(type="exportGif",range=selection,copy=True,documentId=p.evaluate("doc.id"))
            assert p.evaluate("copiedRange")==expected
            (work/(("edited" if edited else "original")+"-copy.json")).write_text(json.dumps(expected))
            p.locator("#timeline").press("Escape")
            # One source frame at the end of the working timeline.
            s=select_range(p,3.94,3.99)
            assert s==dict(startUs=3916667,endUs=4000000),s
            p.locator("#exportVideo").click();dest=exported_file(proc)
            count=subprocess.check_output(["ffprobe","-v","error","-select_streams","v","-show_entries","stream=nb_frames","-of","csv=p=0",dest],text=True)
            assert int(count)==1,count
            p.locator("#exportGif").hover();p.locator("#saveGif").click()
            with Image.open(exported_file(proc)) as im:assert im.n_frames==1
            p.locator("#timeline").press("Escape")
            p.locator("#exportVideo").click();full=exported_file(proc)
            duration=float(subprocess.check_output(["ffprobe","-v","error","-show_entries","format=duration","-of","csv=p=0",full]))
            assert abs(duration-4)<.025,duration
            region.dblclick();p.evaluate("transport.pause()")
            prior=p.evaluate("timeSelection")
            box=region.bounding_box()
            x=box["x"]+box["width"]/2;y=box["y"]+box["height"]/2
            p.mouse.move(x,y);p.mouse.down();p.mouse.move(x+35,y,steps=8);p.mouse.up()
            assert p.evaluate("doc.notes[0].startUs!==500000 && loopEnabled")
            assert p.evaluate("timeSelection")==prior
            p.locator(".note").first.hover();p.locator(".noteDelete").first.click()
            assert p.evaluate("doc.notes.length===0 && loopEnabled") and p.evaluate("timeSelection")==prior
            p.locator("#timeline").press("Escape")
            axis=p.locator("#ticks").bounding_box()
            p.mouse.move(axis["x"]+axis["width"]/2,axis["y"]+4)
            p.keyboard.down("Control");p.mouse.wheel(0,-120);p.keyboard.up("Control")
            wait(p,"$('scroll').scrollLeft>0")
            s=select_range(p,1.8,2.2,"#videoClips")
            assert s["startUs"]<=1800000<s["startUs"]+84000 and s["endUs"]>=2200000
            switch_file(proc,p,fixtures/"vfr.mp4")
            assert p.evaluate("!timeSelection && !loopEnabled")
            print("Edited" if edited else "Original","range loop, voice, lens, MP4/GIF frames, single frame and full export passed",flush=True)
        finally:
            stop(proc)
    proc,p=launch(pw,fixtures/"vfr.mp4")
    try:
        s=select_range(p,2.52,2.99)
        p.evaluate("transport.pause()");p.locator("#loop").click()
        assert p.evaluate("loopEnabled && transport.paused")
        p.locator("#play").click();looping(p,s["startUs"],s["endUs"])
        p.locator("#timeline").press("Escape");p.evaluate("seekUs(1000000)")
        p.locator("#point").click();p.keyboard.press("Control+Enter")
        p.locator("#markLanes button.point").first.dblclick()
        assert p.evaluate("!loopEnabled && timeSelection===null")
        print("VFR tail loop passed",s,"fixtures",work,flush=True)
    finally:
        stop(proc)
    # A Region from a voice take can start and end between two video timestamps.
    path=work/"subframe.framenote"
    note=dict(id=str(uuid.uuid4()),number=1,text="",shape=None,startUs=1015000,endUs=1065000)
    with zipfile.ZipFile(path,"w") as z:
        z.write(source,"media/source.mp4")
        z.writestr("review.json",json.dumps(dict(version=1,id=str(uuid.uuid4()),fileName=source.name,
                         durationUs=4000000,lastNoteNumber=1,notes=[note])))
    proc,p=launch(pw,path)
    try:
        p.locator(".note").first.dblclick()
        assert p.evaluate("timeSelection")==dict(startUs=1015000,endUs=1065000)
        assert resize(p,1,1200000)["startUs"]==1015000
        p.locator(".note").first.dblclick()
        p.locator("#exportGif").hover();p.locator("#saveGif").click()
        wait(p,"$('status').textContent.includes('GIF 已保存') || $('status').classList.contains('error')",timeout=10000)
        with Image.open(exported_file(proc,timeout=3)) as im:
            assert im.n_frames==1 and im.info["duration"]==50
            assert abs(im.convert("RGB").getpixel((195,105))[0]-80)<2
        p.locator("#exportVideo").click()
        mp4=exported_file(proc)
        pixels=subprocess.check_output(["ffmpeg","-v","error","-i",mp4,"-an","-vf","crop=2:2:194:104","-f","rawvideo","-pix_fmt","rgb24","-"])
        assert len(pixels)==12 and abs(pixels[0]-80)<2,(len(pixels),pixels[:3])
        print("Between-frame Region exported the frame under its start in MP4 and GIF",flush=True)
    finally:
        stop(proc)
