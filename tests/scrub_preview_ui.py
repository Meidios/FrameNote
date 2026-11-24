"""Count presented preview frames during repeated high-rate scrubs and exercise decoder recovery."""
import json
import os
import subprocess
import sys
from playwright.sync_api import sync_playwright
from review_ui import fixtures, launch, stop, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
media = fixtures / "scrub-long-gop.mp4"
if not media.exists():
    subprocess.run(["ffmpeg", "-v", "error", "-f", "lavfi", "-i",
                    "testsrc2=size=960x540:rate=30:duration=12", "-c:v", "libx264",
                    "-g", "120", "-sc_threshold", "0", str(media)], check=True)


def sweep(p, reverse):
    p.evaluate("transport.pause()")
    p.locator("#seek").click(position={"x": 12, "y": 10})
    wait(p, "!v.seeking && v.paused")
    result = p.evaluate("""async reverse=>{
        const rail=$('seek'), pts=new Set();
        let callback, inputs=0;
        function presented(_,m){pts.add(m.mediaTime);callback=v.requestVideoFrameCallback(presented)}
        callback=v.requestVideoFrameCallback(presented);
        const start=performance.now(),span=1400;
        await new Promise(resolve=>{
            const id=setInterval(()=>{
                const fraction=Math.min(1,(performance.now()-start)/span);
                rail.value=Math.round(1e6+10e6*(reverse?1-fraction:fraction));
                rail.dispatchEvent(new Event('input'));inputs++;
                if(fraction===1){clearInterval(id);resolve()}
            },4);
        });
        v.cancelVideoFrameCallback(callback);
        return {inputs,presented:pts.size};
    }""", reverse)
    expected = 1 if reverse else 11
    wait(p, "t=>!v.seeking && Math.abs(shown/1e6-t)<.034", expected)
    return result


with sync_playwright() as pw:
    proc, page = launch(pw, media)
    try:
        wait(page, "document.querySelector('#sourceFrames image[href]')")
        results = [sweep(page, reverse) for reverse in [False, True, False]]
        print("Scrub sweeps:", json.dumps(results), flush=True)
        if "--measure-only" not in sys.argv:
            assert all(r["presented"] > 1 for r in results), results
            rail = page.locator("#seek").bounding_box()
            for fraction in [.15, .8, .2, .7]:
                page.mouse.move(rail["x"] + rail["width"] * fraction, rail["y"] + 10)
                if fraction == .15:
                    page.mouse.down()
            page.mouse.up()
            wait(page, "!scrubber.active() && !v.seeking && v.paused && Math.abs(shown/1e6-8.4)<.15")
            page.locator("#point").click()
            page.locator("#body").fill("保留这条批注")
            page.keyboard.press("Control+Enter")
            before = page.evaluate("({selected, notes:doc.notes, undo:history.length, position:stamp(), playbackId})")
            assert not page.evaluate("reviewProxy")
            # A failed decode during a drag must recover at the newest target, even before its first paint.
            before["position"]=6_000_000
            page.evaluate("""t=>{
                $('seek').value=t;$('seek').dispatchEvent(new Event('input'));
                v.dispatchEvent(new Event('error'));
            }""",before["position"])
            wait(page, "id=>ready && !busy && playbackId!==id && reviewProxy && !v.seeking",
                 before["playbackId"], timeout=90000)
            wait(page, "t=>Math.abs(shown-t)<=2", before["position"])
            after = page.evaluate("({selected, notes:doc.notes, undo:history.length, paused:transport.paused})")
            assert after["paused"] and all(after[key] == before[key] for key in ["selected","notes","undo"])
            page.keyboard.press("Control+z")
            assert page.evaluate("history.length") == before["undo"]-1
            print("PASS: real pointer release, accurate final frame, decoder recovery in reviewer and undo.", flush=True)
    finally:
        stop(proc)
    if "--measure-only" not in sys.argv:
        proc, page = launch(pw, fixtures / "vfr.mp4")
        try:
            page.evaluate("""()=>{
                transport.pause();window.lastPresented=-1;
                function frame(_,m){lastPresented=Math.round(m.mediaTime*1e6);v.requestVideoFrameCallback(frame)}
                v.requestVideoFrameCallback(frame);
            }""")
            for edited in [False, True]:
                if edited:
                    page.locator("#splitClip").click()
                    assert page.locator("#videoClips .editClip").count()==2
                pts=page.evaluate("frames")
                for i in [len(pts)-2,1,len(pts)//2]:
                    page.locator("#seek").evaluate("""(el,t)=>{
                        el.value=t;el.dispatchEvent(new Event('input'));
                    }""",pts[i])
                    wait(page,"t=>!scrubber.active() && !v.seeking && Math.abs(lastPresented-t)<=2",pts[i])
            print("PASS: exact presented VFR timestamps while scrubbing original and edited media.",flush=True)
        finally:
            stop(proc)
