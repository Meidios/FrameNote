"""Check adaptive details, annotation tools, card-local deletion and keyboard history in WebView2."""
import os
import shutil
from PIL import Image
from playwright.sync_api import sync_playwright
from review_ui import fixtures, launch, notes, stop, switch_file, wait, work

profile = work / "profile"
profile.mkdir()
(profile / "updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"] = str(profile)
video = work / "details.mp4"
shutil.copyfile(fixtures / "vfr.mp4", video)

with sync_playwright() as pw:
    proc, page = launch(pw, video)
    try:
        panel = page.locator("#editor")
        empty_box = panel.bounding_box()
        assert page.locator("#emptyDetails").is_visible()
        assert page.locator("#annotationTools").is_visible() and page.locator("#arrow").is_enabled()
        mark=page.locator("#timelineTools").get_by_role("button",name="点批注",exact=True)
        assert not page.locator("#timelineTools").inner_text().strip()
        assert mark.locator("svg use").evaluate("el=>el.getBBox().width>0")
        assert mark.get_attribute("aria-description")=="添加点批注（M）"
        with page.expect_file_chooser() as chooser:
            page.keyboard.press("p")
        chooser.value.set_files([])
        assert not page.locator("#startPicture").count()
        assert not page.locator("#reviewPanel :is(#edit,#del,#undo,#gif,#cancel,[type=submit])").count()
        for i in range(18):
            if i==0:
                page.locator("#stage").focus()
                page.keyboard.press("m")
            else:
                mark.click()
            page.locator("#body").fill(f"Note {i+1}")
        page.locator(".note .stamp").first.click()
        body = page.locator("#body")
        assert body.input_value() == "Note 1"
        body.fill("Changed")
        body.press("End")
        page.keyboard.type(" together")
        page.keyboard.press("Control+z")
        assert body.input_value() == "Note 1"
        page.keyboard.press("Control+Shift+z")
        assert body.input_value() == "Changed together"
        card = page.locator(".note").nth(1)
        trash = card.get_by_role("button", name="删除批注 #2", exact=True)
        assert trash.locator("svg use").count() == 1 and not trash.inner_text()
        trash.click()
        assert body.input_value() == "Changed together" and not any(n["number"] == 2 for n in notes(page))
        page.keyboard.press("Control+z")
        assert len(notes(page)) == 18 and body.input_value() == "Changed together"
        page.keyboard.press("Control+Shift+z")
        assert len(notes(page)) == 17
        page.locator(".note.selected .noteDelete").click()
        assert page.locator("#emptyDetails").is_visible()
        page.keyboard.press("Control+z")
        assert body.input_value() == "Changed together"
        body.fill("Final revision")
        page.keyboard.press("Control+Shift+z")
        assert len(notes(page)) == 17 and body.input_value() == "Final revision"
        page.locator(".note .stamp").last.click()
        selected_box = panel.bounding_box()
        assert selected_box["height"] > empty_box["height"]
        assert abs(selected_box["y"] + selected_box["height"] - empty_box["y"] - empty_box["height"]) < 1
        assert page.locator("#list").evaluate("el=>el.scrollHeight>el.clientHeight && el.scrollTop>0")
        page.locator(".note .stamp").first.click()
        toolbar = page.get_by_role("group", name="批注工具", exact=True)
        assert page.locator("#timelineBar > #annotationTools").count() == 1
        assert page.locator("#editor #annotationTools").count() == 0
        tools = ["箭头", "框选", "线", "角度", "距离标尺", "放大镜", "文字框", "录制语音", "添加图片", "清除图形"]
        for name in tools:
            button = toolbar.get_by_role("button", name=name, exact=True)
            assert button.get_attribute("aria-description").split("（")[0] == name and not button.inner_text()
            assert button.locator("svg use").evaluate("el=>el.getBBox().width>0")
        for name in tools[:7]:
            toolbar.get_by_role("button", name=name, exact=True).click()
            assert toolbar.get_by_role("button", pressed=True).get_attribute("aria-label") == name
        page.keyboard.press("Escape")
        assert not toolbar.get_by_role("button", pressed=True).count()
        for width, height in [(824, 506), (1214, 746), (1600, 900), (2140, 900)]:
            page.set_viewport_size({"width": width, "height": height})
            assert page.evaluate("""()=>{const editor=$('editor'),fields=$('detailFields'),list=$('list');
                return [editor,fields].every(e=>e.scrollHeight<=e.clientHeight+1 && e.scrollWidth<=e.clientWidth+1)
                    && list.clientHeight>0 && list.scrollHeight>list.clientHeight}""")
            assert page.evaluate("""() => {
                const bar=$('timelineBar').getBoundingClientRect();
                const rect=el=>el.getBoundingClientRect();
                const mark=rect($('point')), markIcon=rect($('point').querySelector('svg'));
                const firstTool=rect($('arrow')), lastEdit=rect($('deleteClip'));
                const tools=[...document.querySelectorAll('#annotationTools button, #editTools button')].filter(el=>el.offsetWidth);
                const sameSize=(a,b)=>a.width===b.width && a.height===b.height;
                const buttons=[...$('timelineBar').querySelectorAll('button')].filter(el=>el.offsetWidth).map(el=>el.getBoundingClientRect());
                const fits=buttons.every((a,i)=>a.left>=bar.left && a.right<=bar.right &&
                    buttons.slice(i+1).every(b=>a.right<=b.left+1 || b.right<=a.left+1 || a.bottom<=b.top+1 || b.bottom<=a.top+1));
                return fits && $('trackDesk').getBoundingClientRect().top>=bar.bottom-1
                    && firstTool.left-mark.right===4 && firstTool.top===mark.top
                    && tools.every(el=>sameSize(rect(el),mark) && sameSize(rect(el.querySelector('svg')),markIcon))
                    && (bar.width<=730 || [...$('editTools').children].every(el=>rect(el).top===mark.top))
                    && bar.right-lastEdit.right<10;
            }"""), width
        page.locator("#timelineBar").screenshot(path=str(work / "timeline-toolbar.png"))
        page.locator("#fullPreview").click()
        wait(page, "isFullPreview() && !$('timelineBar').offsetHeight")
        page.locator("#fullPreview").click()
        wait(page, "!isFullPreview() && $('annotationTools').offsetHeight>0")
        page.locator("#reviewPanel").screenshot(path=str(work / "note-details.png"))
        picture = work / "reference.png"
        Image.new("RGB", (200,120), "#557e99").save(picture)
        active_note = page.evaluate("selected")
        with page.expect_file_chooser() as chooser:
            toolbar.get_by_role("button", name="添加图片").click()
        chooser.value.set_files(str(picture))
        wait(page, "id=>editing && editing.id!==id && editing.pictures?.length===1", active_note)
        picture_note=page.evaluate("editing.id")
        for viewer_width in (729, 731):
            page.locator("#viewer").evaluate("(el,width)=>el.style.width=width+'px'", viewer_width)
            assert page.evaluate("""()=>['arrow','removePicture','clear'].every(id=>
                $(id).getBoundingClientRect().top===$('point').getBoundingClientRect().top)"""), viewer_width
        page.locator("#viewer").evaluate("el=>el.style.removeProperty('width')")
        toolbar.get_by_role("button", name="移除图片").click()
        wait(page, "id=>doc.notes.find(n=>n.id===id).pictures.length===0", picture_note)
        page.keyboard.press("Control+Enter")
        switch_file(proc, page, picture)
        image_toolbar=page.get_by_role("group",name="图片批注工具",exact=True)
        assert image_toolbar.locator("#annotationTools").is_visible()
        add_note=image_toolbar.get_by_role("button",name="添加批注",exact=True)
        assert not add_note.inner_text() and add_note.get_attribute("aria-description")=="添加批注（M）"
        assert add_note.locator('use[href="#icon-bookmark"]').evaluate("el=>el.getBBox().width>0")
        assert page.locator("#voiceRecord").is_hidden()
        toolbar.get_by_role("button", name="框选").click()
        assert page.evaluate("imageDoc() && tool==='box' && !doc.notes.length")
        page.keyboard.press("Escape")
        add_note.click()
        assert len(notes(page))==1
        body.fill("检查框选区域")
        for width,height in [(1400,950),(824,506)]:
            page.set_viewport_size({"width":width,"height":height})
            assert page.evaluate("""()=>{
                const rect=el=>el.getBoundingClientRect(), panel=rect($('reviewPanel'));
                const buttons=[...$('imageToolbar').querySelectorAll('button')].filter(el=>el.offsetWidth).map(rect);
                const heading=rect($('renumberNotes')), list=rect($('list')), details=rect($('editor'));
                const copy=rect($('copyImage')), save=rect($('saveImage'));
                return buttons.length===10 && buttons.every((b,i)=>b.top===buttons[0].top &&
                    b.left>=panel.left && b.right<=panel.right && (!i || b.left>=buttons[i-1].right))
                    && buttons[0].bottom<heading.top && heading.bottom<=list.top && list.height>0
                    && list.bottom<=details.top+1 && details.bottom<=copy.top && copy.right<save.left
                    && save.right<=panel.right && panel.right-save.right<=10 && panel.bottom-save.bottom<=10;
            }"""), width
            page.locator("#reviewPanel").screenshot(path=str(work / f"image-sidebar-{width}.png"))
        with page.expect_file_chooser() as chooser:
            toolbar.get_by_role("button",name="添加图片").click()
        chooser.value.set_files(str(picture))
        wait(page,"!$('removePicture').hidden")
        assert page.evaluate("""()=>['removePicture','clear'].every(id=>{
            const b=$(id).getBoundingClientRect();
            return b.top===$('point').getBoundingClientRect().top && b.right<=$('reviewPanel').getBoundingClientRect().right;
        })""")
        toolbar.get_by_role("button",name="移除图片").click()
        page.keyboard.press("Control+Enter")
        before=len(notes(page))
        page.locator("#stage").press("m")
        assert len(notes(page))==before+1
        for _ in range(8):
            add_note.click()
        note_list=page.locator("#list")
        assert note_list.evaluate("el=>el.scrollHeight>el.clientHeight")
        top_tools=image_toolbar.bounding_box()
        bottom_details=panel.bounding_box()
        note_list.evaluate("el=>el.scrollTop=el.scrollHeight")
        assert note_list.evaluate("el=>el.scrollTop>0")
        assert image_toolbar.bounding_box()==top_tools and panel.bounding_box()==bottom_details
    finally:
        stop(proc)
    proc, page = launch(pw, video)
    try:
        assert notes(page) == []
    finally:
        stop(proc)
print("PASS: adaptive details, icon tools, list scrolling, clean reopen, targeted deletion, undo/redo and branching.", work)
