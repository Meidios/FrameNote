"""Click overlapping point/range labels in the release desk at normal and enlarged zoom."""
import sys, uuid, json, zipfile
from playwright.sync_api import sync_playwright
from review_ui import fixtures,work,launch,stop,wait

source=fixtures/"vfr.mp4"
notes=[dict(id=str(uuid.uuid4()),number=i,startUs=(i*40000 if i<19 else 2800000),
            endUs=(2950000 if i==20 else None),text="批注 "+str(i),shape=None) for i in range(1,23)]
notes[18]["shape"]=dict(kind="arrow",x=.12,y=.3,toX=.39,toY=.55)
notes[19]["shape"]=dict(kind="box",x=.57,y=.3,toX=.84,toY=.7)
# A lone region to the right of stacked points must remain in the bottom lane.
notes[0]["startUs"]=notes[1]["startUs"]
notes[17].update(startUs=880000,endUs=1500000)
notes[18]["color"]="#ff9d97";notes[19]["color"]="#8de3af"
review=dict(version=1,id=str(uuid.uuid4()),fileName=source.name,durationUs=3000000,
            notes=notes,lastNoteNumber=len(notes))
package=work/"编号联动.framenote"
with zipfile.ZipFile(package,"w") as z:
    z.writestr("review.json",json.dumps(review));z.write(source,"media/source.mp4")

with sync_playwright() as pw:
    proc,page=launch(pw,package)
    try:
        wait(page,"document.querySelector('#sourceFrames image[href]') && document.querySelector('#sourceWave').hasAttribute('d')",timeout=15000)
        assert not page.locator("#zoom").count()
        page.evaluate("v.pause()")
        # Click the real ruler, film and waveform while paused and while playing.
        page.locator("#rate").select_option("0.5")
        for selector in ("#seek", "#filmstrip", "#waveform"):
            for playing in (False, True):
                page.locator("#seek").click(position={"x":20,"y":6})
                if playing:
                    page.locator("#play").click()
                wait(page,"playing=>!v.seeking && v.paused!==playing && v.currentTime<0.8",playing)
                track=page.locator(selector)
                bounds=track.bounding_box()
                # The clip body selects/drags the clip. The exposed track margin scrubs.
                y=int(bounds["height"]/2) if selector=="#seek" else 0
                track.click(position={"x":int(bounds["width"]*.6),"y":y})
                wait(page,"playing=>!v.seeking && v.paused!==playing && v.currentTime>1.5 && v.currentTime<2.2",playing)
                position=page.evaluate("v.currentTime")
                if playing:
                    wait(page,"t=>v.currentTime>t+.08",position)
                    page.locator("#play").click()
                else:
                    assert page.evaluate("v.paused && Math.abs(shown/1e6-v.currentTime)<.12")
        for number in (1,18):  # a Mark and a Region, both away from end-of-file
            for playing in (False,True):
                if playing:
                    page.locator("#play").click()
                wait(page,"playing=>v.paused!==playing",playing)
                page.locator("#marks button").nth(number-1).click()
                wait(page,"i=>v.paused && !v.seeking && selected===doc.notes[i].id && Math.abs(shown-frames[index(doc.notes[i].startUs)])<=2",number-1)
                position=page.evaluate("v.currentTime")
                page.keyboard.press("Space")
                wait(page,"t=>!v.paused && v.currentTime>t+.08",position)
                page.keyboard.press("Space")
                wait(page,"t=>v.paused && v.currentTime>t+.08",position)
        print("PASS: ruler, video and audio preserve transport state; Mark/Region pause and Space resumes/pauses",flush=True)
        page.locator("#rate").select_option("1.5")
        def wheel(key,delta):
            page.keyboard.down(key)
            try:page.mouse.wheel(0,delta)
            finally:page.keyboard.up(key)
        def fraction(x):
            return page.locator("#ticks").evaluate("(e,x)=>(x-e.getBoundingClientRect().left)/e.clientWidth",x)
        def geometry():
            return page.evaluate("""()=>['stage','overlay','transport','marks','trackDesk'].map(id=>{
                const r=$(id).getBoundingClientRect();return [r.x,r.y,r.width,r.height].filter((_,i)=>id!=='marks'||i%2===1)})""")
        baseline=geometry()
        for zoom in ("1","8"):
            if zoom=="8":
                pos=page.evaluate("v.currentTime")
                r=page.locator("#scroll").bounding_box();x=round(r["x"]+.4*r["width"])
                page.mouse.move(x,r["y"]+12);before=fraction(x)
                wheel("Control",-480);wait(page,"$('timeline').style.width==='800%'")
                assert abs(before-fraction(x))*page.locator("#ticks").bounding_box()["width"]<2
                assert geometry()==baseline
                assert page.evaluate("$('scroll').offsetHeight-$('scroll').clientHeight") == 8
                left=page.evaluate("$('scroll').scrollLeft")
                wheel("Shift",120);wait(page,"x=>$('scroll').scrollLeft>x",left)
                wheel("Shift",-120);wait(page,"x=>Math.abs($('scroll').scrollLeft-x)<2",left)
                thumb=page.locator("#scroll").evaluate("""e=>{const r=e.getBoundingClientRect();
                    return [r.x+(e.scrollLeft+e.clientWidth/2)*e.clientWidth/e.scrollWidth,r.bottom-4]}""")
                page.mouse.move(*thumb);page.mouse.down();page.mouse.move(thumb[0]+24,thumb[1],steps=4);page.mouse.up()
                wait(page,"x=>$('scroll').scrollLeft>x+25",left)
                page.mouse.move(x,r["y"]+12)
                wheel("Control",480);wait(page,"$('timeline').style.width==='100%' && $('scroll').scrollLeft===0")
                assert geometry()==baseline
                wheel("Control",-480);wait(page,"$('timeline').style.width==='800%'")
                assert page.evaluate("t=>v.paused && v.playbackRate===1.5 && v.currentTime===t",pos)
            region=page.locator("#marks button").filter(has_text="#18")
            gap=region.evaluate("""el=>document.getElementById('markLanes').getBoundingClientRect().bottom-
                el.getBoundingClientRect().bottom""")
            assert 0<=gap<=4,(zoom,"isolated region bottom gap",gap)
            for number in (1,18,19,20,22):
                label=page.locator("#marks button").nth(number-1)
                assert label.inner_text()==f"#{number}"
                label.click()
                wait(page,"i=>selected===doc.notes[i-1].id && Math.abs(shown-frames[index(doc.notes[i-1].startUs)])<=2",number)
                assert label.get_attribute("aria-pressed")=="true"
                assert page.locator(".note.selected .noteNumber").inner_text()==f"#{number}"
                assert page.locator(".note.selected").evaluate("""el=>{const a=el.getBoundingClientRect(),b=el.parentElement.getBoundingClientRect();return a.top>=b.top-1 && a.bottom<=b.bottom+1}""")
            assert page.locator("#marks button").evaluate_all("""els=>els.slice(18).every((e,i,arr)=>arr.slice(i+1).every(other=>e.offsetTop!==other.offsetTop))""")
            for number in (1,18,19,20):
                error=page.locator("#markLanes button").nth(number-1).evaluate("""el=>{
                  const rail=document.getElementById('seek').getBoundingClientRect(),b=el.getBoundingClientRect();
                  const x=t=>rail.left+8+(rail.width-16)*t,s=x(+el.dataset.start),e=x(+el.dataset.end);
                  return el.classList.contains('point')?Math.abs((b.left+b.right)/2-s):Math.max(Math.abs(b.left-s),Math.abs(b.right-e))
                }""")
                assert error<1,(zoom,number,error)
        assert page.locator(".inkBadge text").all_text_contents()==["#19","#20","#21","#22"]
        tags=[el.bounding_box() for el in page.locator(".inkBadge").all()]
        for i,a in enumerate(tags):
            for b in tags[i+1:]:
                assert (a['x']+a['width']<=b['x'] or b['x']+b['width']<=a['x'] or
                        a['y']+a['height']<=b['y'] or b['y']+b['height']<=a['y'])
        position=page.evaluate("v.currentTime")
        for num,x,y in [(19,.255,.425),(20,.57,.5),(20,.705,.5),(20,.82,.68)]:
            box=page.locator("#overlay").bounding_box()
            page.mouse.move(box["x"]+box["width"]*x,box["y"]+box["height"]*y)
            wait(page,"i=>document.querySelector('.note.hovered .noteNumber')?.textContent==='#'+i",num)
            assert page.locator(".note.selected .noteNumber").inner_text()=="#22"
            assert page.evaluate("v.currentTime")==position
        page.mouse.move(box["x"]+box["width"]*.9,box["y"]+box["height"]*.5)
        assert page.locator(".note.hovered").count()==0
        def selected_number():
            return page.locator(".note.selected .noteNumber").inner_text()
        def spot(x,y):
            return box["x"]+box["width"]*x,box["y"]+box["height"]*y
        for num,x,y in [(20,.705,.5),(19,.255,.425)]:
            page.mouse.click(*spot(x,y))
            page.mouse.move(*spot(.9,.5))
            assert selected_number()==f"#{num}" and page.locator(".note.hovered").count()==0
            assert page.locator('#marks [aria-pressed=true]').inner_text()==f"#{num}"
        # Changing focus while editing used to redraw the pressed badge before click.
        for i,number in enumerate((21,22,19,21)):
            if i:
                page.locator("#body").focus()
            page.locator(".inkBadge").filter(has_text=f"#{number}").click()
            assert selected_number()==f"#{number}"
            assert page.locator('#marks [aria-pressed=true]').inner_text()==f"#{number}"
            assert page.locator("#body").input_value()==notes[number-1]["text"]
            assert page.evaluate("v.currentTime")==position
        page.mouse.move(*spot(.705,.5))
        assert page.locator(".note.hovered .noteNumber").inner_text()=="#20"
        assert selected_number()=="#21"
        # Pick the range within its duration, where a timeline click would rewind.
        page.locator("#next").click()
        wait(page,"shown>2800000 && shown<2950000")
        position=page.evaluate("v.currentTime")
        page.mouse.click(*spot(.705,.5));page.mouse.move(*spot(.9,.5))
        assert selected_number()=="#20" and page.evaluate("v.currentTime")==position
        print("PASS: visible numbers, same-frame points/range, seeking, right-list selection and scrolling at 1x and 8x")
    finally:stop(proc)

