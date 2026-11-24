"""Measure media seeks, stream advancement and overlapping voices in the real review window."""
import hashlib
import json
import os
import subprocess
import uuid
import zipfile

from playwright.sync_api import sync_playwright
from review_ui import launch, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
source, voice = work / "source.mp4", work / "voice.m4a"
subprocess.run(["ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30:duration=12",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=12",
                "-c:v", "libx264", "-c:a", "aac", "-movflags", "+faststart", str(source)], check=True)
subprocess.run(["ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=1100:sample_rate=48000:duration=5",
                "-c:a", "aac", str(voice)], check=True)
asset = hashlib.sha256(voice.read_bytes()).hexdigest()
notes = [dict(id=str(uuid.uuid4()), number=i, text="Voice", startUs=start, endUs=start+5000000,
              voice=dict(asset=asset, durationUs=5000000)) for i, start in enumerate([500000, 1600000], 1)]
def package(name, timeline=None):
    document = dict(version=3 if timeline else 1, id=str(uuid.uuid4()), fileName=source.name,
                    durationUs=12000000, lastNoteNumber=2, notes=notes)
    if timeline:
        document["timeline"] = timeline
    dest = work / (name+".framenote")
    with zipfile.ZipFile(dest, "w") as z:
        z.writestr("review.json", json.dumps(document))
        z.write(source, "media/source.mp4")
        z.write(voice, "voice/"+asset+".m4a")
    return dest

def clip(track, start, offset, duration, **extra):
    return dict(id=str(uuid.uuid4()), track=track, startUs=start, sourceUs=offset, durationUs=duration, **extra)

probe = """() => {
    window.mediaLog = [];
    const watch = el => {
        const item = {el, seeks:0, writes:0, plays:0, waits:0};
        mediaLog.push(item);
        for (const [event,field] of [['seeking','seeks'],['playing','plays'],['waiting','waits']])
            el.addEventListener(event,()=>item[field]++);
        for(const name of ['volume','playbackRate','muted']) {
            const prop = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype,name);
            Object.defineProperty(el,name,{get(){return prop.get.call(this)},set(x){item.writes++;prop.set.call(this,x)}});
        }
        return el;
    };
    watch(v);
    window.Audio=class extends Audio {constructor(...args){super(...args);watch(this)}};
    window.measureAudio=() => mediaLog.map(({el,seeks,writes,plays,waits})=>({
        type:el===v?'video':el.currentSrc.endsWith('.m4a')?'voice':'source',seeks,writes,plays,waits,
        playing:!el.paused,position:el.currentTime,bytes:el.webkitAudioDecodedByteCount
    }));
}"""

with sync_playwright() as pw:
    proc, p = launch(pw, package("voice"))
    try:
        p.evaluate("transport.pause()")
        p.evaluate(probe)
        results = []
        for mode in ["direct", "edited"]:
            if mode == "edited":
                p.locator("#linkTracks").click()
            p.evaluate("()=>{transport.currentTime=0;return transport.play()}")
            wait(p, "transport.currentTime>2")
            reference = p.evaluate("measureAudio()[0]")
            wait(p, "transport.currentTime>3.4")
            state = p.evaluate("measureAudio()")
            clock = p.evaluate("transport.currentTime")
            live = [entry for entry in state if entry["playing"]]
            print(mode, "live", live, flush=True)
            voices = sorted((entry for entry in live if entry["type"]=="voice"),key=lambda e:e["position"],reverse=True)
            assert len(voices)==2 and all(entry["bytes"]>0 for entry in voices), state
            assert state[0]["seeks"]==reference["seeks"] and state[0]["waits"]==reference["waits"], (reference, state[0])
            for entry, n in zip(voices, notes):
                assert abs(entry["position"] - (clock - n["startUs"]/1e6)) < .3, (entry, clock)
            p.evaluate("transport.pause()")
            results.extend(live)
            p.locator(".note .stamp").first.click()
            assert p.locator("#voicePreview, #voiceRemove").count()==0
            assert p.locator("#voiceStatus").is_hidden()
            p.evaluate("()=>transport.play()")
            p.evaluate("()=>{transport.playbackRate=1.5;transport.currentTime=4}")
            wait(p, "transport.currentTime>4.6")
            delta = p.evaluate("""()=>{const active=mediaLog.map(m=>m.el).filter(a=>!a.paused&&a.currentSrc.endsWith('.m4a'));
                return active.map(a=>({rate:a.playbackRate,offset:transport.currentTime-a.currentTime})).sort((a,b)=>a.offset-b.offset)}""")
            assert len(delta)==2 and all(e["rate"]==1.5 and abs(e["offset"]-n["startUs"]/1e6)<.28 for e,n in zip(delta,notes)),delta
            p.evaluate("transport.pause()")
            p.evaluate("transport.playbackRate=1")
            # Clear observations, including the same video element reused in edited mode.
            p.evaluate("mediaLog.forEach(m=>{m.writes=m.seeks=m.waits=m.plays=0})")
        assert all(m["writes"]<20 for m in results), "Continuous playback reapplied unchanged audio settings every frame"
        assert all(m["seeks"]<=3 for m in results), "Decoders repeatedly sought instead of playing continuously"
    finally:
        stop(proc)

    clips=[clip("video",0,0,4000000),clip("video",8000000,8000000,4000000),clip("audio",0,1000000,11000000),
           clip("voice",4500000,1200000,3000000,asset=asset,sourceDurationUs=5000000,noteId=notes[0]["id"])]
    proc,p=launch(pw,package("detached",dict(linked=False,sourceDurationUs=12000000,clips=clips)))
    try:
        p.evaluate("()=>{transport.pause();transport.currentTime=12}")
        p.evaluate(probe)
        p.evaluate("()=>{transport.currentTime=3.5;return transport.play()}")
        wait(p,"transport.currentTime>5")
        audio = p.evaluate("measureAudio().filter(m=>m.playing)")
        clock = p.evaluate("transport.currentTime")
        original = next(m for m in audio if m["type"]=="source")
        comment = next(m for m in audio if m["type"]=="voice")
        assert p.evaluate("v.paused && v.style.visibility==='hidden'") and original["seeks"]<=2
        assert abs(original["position"]-clock-1)<.15 and abs(comment["position"]-(clock-4.5+1.2))<.20,(clock,audio)
        p.locator(".note .stamp").first.click()
        p.evaluate("transport.pause()")
        p.evaluate("transport.currentTime=5.2")
        p.locator("#play").click()
        wait(p,"transport.currentTime>5.6")
        assert len(p.evaluate("measureAudio().filter(m=>m.playing)"))==2
    finally:
        stop(proc)
print("PASS: voice note details, continuous source audio, overlapping voices, seek/rate changes and playback in a video gap.")
