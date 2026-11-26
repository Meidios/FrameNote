"""Measure file acceptance to first presented frame in the review editor, including FLAC-in-MP4."""
import json
import os
from pathlib import Path
import subprocess
import sys

from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import fixtures, launch, stop, wait, work

profile=work/"profile"
profile.mkdir()
(profile/"volume.json").write_text("0")
(profile/"updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)

def measure(player,source,*,reject_flac=False,check_frames=False):
    proc,page=launch(player,None,"typeof ready!=='undefined'")
    try:
        if reject_flac:
            page.evaluate("""()=>{
                const canPlay=HTMLMediaElement.prototype.canPlayType;
                HTMLMediaElement.prototype.canPlayType=function(type){
                    return /flac/i.test(type)?'':canPlay.call(this,type);
                };
            }""")
        page.evaluate("""()=>{
          window.openTimes={};
          const mark=name=>openTimes[name]??=performance.now();
          chrome.webview.addEventListener('message',({data:m})=>{
            if(m.type==='busy'&&m.value)mark('open');
            if(m.type==='loaded')mark('indexed');
            if(m.type==='timelineData')mark(m.track);
          });
          v.addEventListener('loadeddata',()=>mark('loadeddata'),{once:true});
          v.addEventListener('playing',()=>mark('playing'),{once:true});
          v.requestVideoFrameCallback(()=>mark('firstFrame'));
        }""")
        page.locator("#start").click()
        picker=Desktop(backend="win32").window(process=proc.pid,class_name="#32770")
        picker.wait("ready")
        field=picker.child_window(class_name="Edit",found_index=0)
        field.wait("ready");field.set_edit_text(str(source))
        picker.child_window(control_id=1,class_name="Button").click()
        wait(page,"openTimes.firstFrame && openTimes.playing && ready && !busy && v.webkitAudioDecodedByteCount>0",timeout=120000)
        wait(page,"openTimes.filmDone && openTimes.wave",timeout=90000)
        times=page.evaluate("Object.fromEntries(Object.entries(openTimes).map(([k,t])=>[k,Math.round(t-openTimes.open)]))")
        result=page.evaluate("({proxy:reviewProxy,frames:frames.length,audioBytes:v.webkitAudioDecodedByteCount,videoBytes:v.webkitVideoDecodedByteCount})")
        result["ms"]=times
        print(json.dumps(result),flush=True)
        if check_frames:
            decoded=subprocess.check_output(["ffprobe","-v","error","-select_streams","v:0","-show_entries",
                "frame=best_effort_timestamp_time","-of","json",str(source)])
            pts=[round(float(frame["best_effort_timestamp_time"])*1e6) for frame in json.loads(decoded)["frames"]]
            assert page.evaluate("frames")==pts
            for target in (pts[len(pts)//2],pts[-1],pts[1]):
                page.evaluate("t=>seekUs(t)",target)
                wait(page,"t=>shown===t && !v.seeking",target)
        return result
    finally:stop(proc)

with sync_playwright() as player:
    if len(sys.argv)>3:
        source=Path(sys.argv[3]).resolve()
        for _ in range(int(sys.argv[4]) if len(sys.argv)>4 else 1):
            measure(player,source)
    else:
        source=work/"vfr-flac.mp4"
        subprocess.run(["ffmpeg","-v","error","-i",str(fixtures/"vfr.mp4"),"-c:v","copy","-c:a","flac","-strict","experimental",str(source)],check=True)
        for reject_flac in (False,True):
            result=measure(player,source,reject_flac=reject_flac,check_frames=True)
            assert result["proxy"]==reject_flac and result["videoBytes"]>0
