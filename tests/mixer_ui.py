"""Exercise two real WASAPI loopbacks in the release UI using two virtual outputs.

Arguments: FrameNote.exe, fixtures directory, CaptureCheck.exe, two device name prefixes.
Checks the default playback mute state before, during and after microphone recording.
"""
import array, math, os, shutil, subprocess, sys, time
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import url2pathname
from playwright.sync_api import sync_playwright
from review_ui import fixtures, work, launch, stop, wait

os.environ["FRAMENOTE_DATA_DIR"]=str(work/"profile")
(work/"profile").mkdir()
(work/"profile"/"capture.json").write_text('{"hideWhileRecording":false}')
target=subprocess.Popen([sys.executable,"-c",'import tkinter as t;w=t.Tk();w.title("FrameNote mixer QC");w.geometry("400x200");w.mainloop()'])
def measure(path):
    data=subprocess.check_output(["ffmpeg","-v","error","-ss","0.6","-i",str(path),"-t","1","-map","0:a:0","-ac","1","-ar","48000","-f","f32le","-"])
    x=array.array("f");x.frombytes(data);assert len(x)==48000
    def db(f):
        # Measure power in short windows. Independent WASAPI clocks can move
        # phase across a second even when a continuous tone keeps its gain.
        size=4800
        window=[.5-.5*math.cos(2*math.pi*i/size) for i in range(size)]
        cosine=[w*math.cos(i*2*math.pi*f/48000) for i,w in enumerate(window)]
        sine=[w*math.sin(i*2*math.pi*f/48000) for i,w in enumerate(window)]
        power=[]
        for start in range(0,len(x),size):
            block=x[start:start+size]
            a=sum(v*c for v,c in zip(block,cosine))
            b=sum(v*s for v,s in zip(block,sine))
            power.append((2*math.hypot(a,b)/sum(window))**2)
        return 10*math.log10(max(1e-24,sum(power)/len(power)))
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
            # System playback stays active on a second virtual output during mic capture.
            page.locator("#mic").select_option("")
            assert page.locator("#voiceRecord").is_disabled()
            assert mic and page.evaluate("catalog.outputs.find(d=>d.name.startsWith('CABLE Input'))?.id")==ids[0]
            page.locator("#mic").select_option(mic["id"])
            mic_toggle=panel.locator("#micStrip .muteChannel")
            if mic_toggle.get_attribute("aria-pressed")=="true":mic_toggle.click()
            def mutes():
                return subprocess.check_output([sys.argv[3],"--playback-mutes"],text=True).splitlines()
            original_mutes=mutes()
            def voice():
                count=page.evaluate("doc.notes.length")
                page.evaluate("()=>{transport.pause();transport.currentTime=0}")
                wait(page,"!v.seeking && transport.currentTime<.05")
                page.locator("#voiceRecord").click()
                wait(page,"!!voiceRecording && !transport.paused")
                assert panel.evaluate("el=>el.inert") and page.evaluate("v.volume===0")
                current=mutes()
                assert current and all(x.endswith(" True") for x in current),current
                time.sleep(1.8)
                page.locator("#voiceRecord").click()
                wait(page,"!voicePending && !voiceRecording")
                assert mutes()==original_mutes
                assert page.evaluate("v.volume===Number($('playbackVolume').value)/100")
                assert page.evaluate("doc.notes.length")==count+1,page.locator("#status").inner_text()
                uri=page.evaluate("imageBase+doc.notes.at(-1).voice.asset+'.mp3'")
                sample=work/f"voice-mic-{count}.mp3"
                shutil.copyfile(Path(url2pathname(urlsplit(uri).path)),sample)
                return measure(sample)
            isolated=voice()
            assert isolated[0]>-65 and isolated[1]<isolated[0]-45,isolated
            mic_gain=page.locator("#micStrip input")
            for _ in range(6):mic_gain.press("ArrowLeft")
            quieter=voice()
            assert abs(quieter[0]-isolated[0]+6)<1 and quieter[1]<quieter[0]-45,(quieter,isolated)
            print("Microphone vs simultaneous system-only tone in MP3 (dB):",isolated,quieter,flush=True)
            mic_toggle.click()
            unmuted=rows.locator('.muteChannel[aria-pressed="false"]')
            while unmuted.count():unmuted.first.click()
            assert page.locator("#voiceRecord").is_disabled()
            page.locator("#stage").click(button="right",position={"x":12,"y":12})
            assert page.locator('[data-action="voice"]').is_disabled()
            print("Verified mixer selection, meter activity, gain, mute and recording lock. Screenshot:",work/"horizontal-mixer.png")
        finally:
            if tone:tone.communicate("\n",timeout=5)
            stop(proc)
finally:target.terminate();target.wait()
