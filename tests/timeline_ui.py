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
        wait(page,"document.querySelector('#filmstrip img[src]')?.naturalWidth>0 && document.querySelector('#waveform path').hasAttribute('d')",timeout=15000)
        for zoom in ("1","8"):
            page.locator("#zoom").select_option(zoom)
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
        page.locator(".inkBadge").filter(has_text="#21").click()
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

