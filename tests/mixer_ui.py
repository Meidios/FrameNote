"""Exercise two real WASAPI loopbacks in the release UI using two virtual outputs.

Arguments: FrameNote.exe, fixtures directory, CaptureCheck.exe, two device name prefixes.
Never changes Windows routing or endpoint volume.
"""
import array, math, os, subprocess, sys, time
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import url2pathname
from playwright.sync_api import sync_playwright
from review_ui import fixtures, work, launch, stop, wait

os.environ["FRAMENOTE_DATA_DIR"]=str(work/"profile")
target=subprocess.Popen([sys.executable,"-c",'import tkinter as t;w=t.Tk();w.title("FrameNote mixer QC");w.geometry("400x200");w.mainloop()'])
def measure(path):
    data=subprocess.check_output(["ffmpeg","-v","error","-ss","0.6","-i",str(path),"-t","1","-map","0:a:0","-ac","1","-ar","48000","-f","f32le","-"])
    x=array.array("f");x.frombytes(data);assert len(x)==48000
    def db(f):
        a=sum(v*math.cos(i*2*math.pi*f/48000) for i,v in enumerate(x))
        b=sum(v*math.sin(i*2*math.pi*f/48000) for i,v in enumerate(x))
        return 20*math.log10(max(1e-12,2*math.hypot(a,b)/len(x)))
    return [db(619),db(1423)]

try:
    with sync_playwright() as pw:
        proc,page=launch(pw,fixtures/"vfr.mp4")
        tone=None
        try:
            page.locator("#mode").select_option("window")
            page.locator("#source").select_option(label="FrameNote mixer QC")
            panel=page.locator("#capturePanel")
            rows=panel.locator("#outputStrips .channelStrip")
            while rows.count():rows.first.locator(".removeChannel").click()
            ids=[]
            for prefix in sys.argv[4:6]:
                d=page.evaluate("p=>catalog.outputs.find(d=>d.name.startsWith(p))",prefix)
                assert d,prefix
                page.locator("#addOutput").select_option(d["id"]);ids.append(d["id"])
            assert rows.count()==2 and not any(x in page.locator("#addOutput option").evaluate_all("o=>o.map(x=>x.value)") for x in ids)
            mic=page.evaluate("catalog.inputs.find(d=>d.name.startsWith('CABLE Output'))")
            if mic:
                page.locator("#mic").select_option(mic['id']);assert panel.locator("#micStrip .channelStrip").count()==1
                panel.locator("#micStrip .muteChannel").click()
            tone=subprocess.Popen([sys.argv[3],"--mixer-tones",*ids],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True)
            assert tone.stdout.readline().strip()=="ready"
            panel.locator(".mixer").scroll_into_view_if_needed()
            wait(page,"Array.from(outputs.values()).every(c=>c.peak>0)",timeout=12000)
            rate=page.evaluate("""async()=>{
              const times=[],paints=[];let frame=0;
              const arrived=e=>{if(e.data.type==='meters')times.push(performance.now())};
              const paint=t=>{paints.push(t);frame=requestAnimationFrame(paint)};
              chrome.webview.addEventListener('message',arrived);frame=requestAnimationFrame(paint);
              const pause=ms=>new Promise(r=>setTimeout(r,ms));
              try {
                await pause(3200);
                const hz=a=>1000*(a.length-1)/(a.at(-1)-a[0]);
                const stats={samples:hz(times),display:hz(paints),count:times.length};
                const mixer=document.querySelector('.mixer');mixer.hidden=true;
                try {await pause(250);const start=times.length;await pause(350);stats.hidden=times.length-start}
                finally {mixer.hidden=false}
                return stats;
              } finally {chrome.webview.removeEventListener('message',arrived);cancelAnimationFrame(frame)}
            }""")
            print("Meter samples and display frames per second:",rate,flush=True)
            assert rate["count"]>0 and rate["samples"]>=min(60,rate["display"])*.95 and rate["hidden"]==0,rate
            panel.screenshot(path=str(work/"horizontal-mixer.png"))
            def record():
                old=page.evaluate("doc.id");page.locator("#record").click();wait(page,"recording==='video'")
                assert rows.first.locator("input").is_disabled()
                time.sleep(2.8);page.locator("#record").click()
                wait(page,"old=>temporary && ready && doc.id!==old",old,timeout=20000)
                return measure(Path(url2pathname(urlsplit(page.evaluate("v.currentSrc")).path)))
            baseline=record();assert min(baseline)>-65,baseline
            fader=rows.last.locator("input")
            for _ in range(12):fader.press("ArrowLeft")
            assert fader.input_value()=="-12"
            page.locator("#refreshSources").click()
            wait(page,"document.querySelector('#outputStrips .channelStrip:last-child input').value==='-12'")
            adjusted=record()
            rows.first.locator(".muteChannel").click()
            assert rows.first.get_by_role("meter").get_attribute("aria-valuenow")=="-60"
            muted=record()
            print("619 / 1423 Hz amplitudes in three recorded MP4s (dB):",baseline,adjusted,muted,flush=True)
            assert abs(adjusted[0]-baseline[0])<1 and abs(adjusted[1]-baseline[1]+12)<1
            assert muted[0]<baseline[0]-45 and abs(muted[1]-adjusted[1])<1
            print("Verified mixer selection, meter activity, gain, mute and recording lock. Screenshot:",work/"horizontal-mixer.png")
        finally:
            if tone:tone.communicate("\n",timeout=5)
            stop(proc)
finally:target.terminate();target.wait()
