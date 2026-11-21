"""Exercise annotation creation, geometry, voice attachments and rendered MP4 in WebView2."""
import array, json, math, os, shutil, subprocess, time, zipfile
from pathlib import Path
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import fixtures, launch, notes, stop, wait, work, at

profile=work/'profile';profile.mkdir();(profile/'updates.json').write_text('{"automatic":false}')
os.environ['FRAMENOTE_DATA_DIR']=str(profile)
video=work/'tools.mp4';shutil.copyfile(fixtures/'2997.mp4',video)
def save_dialog(proc,path):
    dialog=Desktop(backend='win32').window(process=proc.pid,class_name='#32770');dialog.wait('ready',timeout=10)
    time.sleep(.2);dialog.child_window(class_name='Edit',found_index=0).set_edit_text(str(path));dialog.child_window(control_id=1).click()

with sync_playwright() as pw:
    proc,p=launch(pw,video)
    tone=None;errors=[];p.on('pageerror',lambda e:errors.append(str(e)))
    try:
        at(p,0)
        assert p.title()=='FrameNote'
        assert p.locator('#inBtn').is_hidden() and p.locator('#outBtn').is_hidden()
        p.locator('#point').click()
        assert len(notes(p))==1 and p.locator('.noteBody').inner_text()=='待填写批注'
        assert p.locator('#marks .point').count()==1
        p.locator('#noteDuration').fill('2');p.locator('#noteDuration').press('Tab')
        assert notes(p)[0]['endUs']==2_000_000 and p.locator('#marks .region').count()==1
        p.locator('#noteDuration').fill('0');p.locator('#noteDuration').press('Tab')
        assert notes(p)[0]['endUs'] is None
        p.locator('#noteDuration').fill('-1');p.locator('#noteDuration').press('Tab')
        assert notes(p)[0]['endUs'] is None
        p.locator('#body').fill('新增图形验证 · 中文文字')
        for kind in ['line','ruler','magnifier','text','angle']:
            p.locator('#'+('toolRuler' if kind=='ruler' else kind)).click();box=p.locator('#overlay').bounding_box()
            def point(x,y):return (box['x']+x*box['width'],box['y']+y*box['height'])
            if kind=='angle':
                for x,y in [(.2,.3),(.2,.6),(.5,.6)]:p.mouse.click(*point(x,y))
                assert '90.0°' in p.locator('#ink').text_content()
            else:
                p.mouse.move(*point(.25,.25));p.mouse.down();p.mouse.move(*point(.6,.65));p.mouse.up()
            assert notes(p)[-1]['shape']['kind']==kind
            if kind=='ruler':assert 'px' in p.locator('#ink').text_content()
            if kind=='magnifier':assert p.locator('#ink foreignObject canvas').count()==1
            p.locator('#point').click();p.locator('#body').fill(kind+' 验证')
        p.keyboard.press('Escape');at(p,0)
        p.locator('#stage').press('i');at(p,30);p.keyboard.press('o')
        assert notes(p)[-1]['endUs']>notes(p)[-1]['startUs']
        p.keyboard.press('Escape')
        # Only a virtual input receives a generated test signal; never capture a physical microphone.
        wait(p,"catalog && catalog.inputs.length>0",timeout=15000)
        assert p.locator('#voiceDevice').count()==0
        while p.locator('#outputStrips .removeChannel').count():p.locator('#outputStrips .removeChannel').first.click()
        assert p.locator('#voiceRecord').is_disabled()
        # Also exercise native recording while the independent project clock is active.
        if os.environ.get('FRAMENOTE_TEST_EDITS'):
            p.evaluate("()=>{window.audioProbes=[];window.Audio=class extends Audio{constructor(...args){super(...args);audioProbes.push(this)}}}");p.locator('#linkTracks').click();p.locator('#linkTracks').click()
        devices=p.evaluate("catalog.inputs")
        p.evaluate("""()=>{catalog.inputs.push({id:'missing-test-device',name:'Disconnected test input'});renderMixer()}""")
        p.locator('#mic').select_option('missing-test-device')
        before_voice=notes(p)
        p.locator('#voiceRecord').click()
        wait(p,"!voicePending && $('voiceStatus').textContent==='录音失败'",timeout=15000)
        assert notes(p)==before_voice
        assert not p.locator('header').evaluate('el=>el.inert')
        assert '语音录制失败' in p.locator('#status').text_content()
        cable=next((d for d in devices if d['name'].startswith('CABLE Output')),None)
        if cable:
            p.locator('#mic').select_option(cable['id'])
            stage=p.locator('#stage').bounding_box()
            p.mouse.click(stage['x']+stage['width']*.9,stage['y']+stage['height']*.85,button="right")
            p.locator('[data-action="voice"]').click()
            wait(p,'!!voiceRecording')
            stop_button=p.locator('#annotationTools').get_by_role('button',name='停止语音录制',exact=True)
            assert stop_button.is_enabled() and stop_button.locator('use').get_attribute('href')=='#icon-stop'
            assert p.locator('#arrow').evaluate("el=>!!el.closest('[inert]')")
            p.keyboard.press('Escape')
            wait(p,'!voicePending && !voiceRecording')
            assert p.locator('#voiceRecord use').get_attribute('href')=='#icon-mic'
            assert notes(p)==before_voice
            render=p.evaluate("catalog.outputs.find(d=>d.name.startsWith('CABLE Input'))?.id")
            helper=Path(__file__).resolve().parents[1]/'artifacts/voice-test-helper/CaptureCheck.exe'
            if render and helper.exists():
                tone=subprocess.Popen([str(helper),'--mixer-tones',render,render],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True)
                assert tone.stdout.readline().strip()=='ready'
            at(p,3);record_start=p.evaluate("annotationTime()")
            p.locator('#voiceRecord').click()
            wait(p,'voiceRecording!==null && !v.paused')
            time.sleep(1.2);stop_button.click();wait(p,'!voicePending && !voiceRecording',timeout=20000)
            assert notes(p)[-1]['voice']['durationUs']>100_000
            assert notes(p)[-1]['endUs']>notes(p)[-1]['startUs']
            assert notes(p)[-1]['voice']['durationUs']>1_000_000
            assert notes(p)[:-1]==before_voice and notes(p)[-1]['startUs']==record_start
            original=notes(p)[-1]
            at(p,3)
            p.locator('.inkBadge[data-note="'+original["id"]+'"]').click(button="right")
            p.locator('[data-action="duplicate"]').click()
            copied=notes(p)[-1]
            assert copied["id"]!=original["id"] and copied["voice"]==original["voice"]
            p.locator('.inkBadge[data-note="'+copied["id"]+'"]').click(button="right")
            p.locator('[data-action="delete"]').click()
            assert notes(p)==before_voice+[original]
            assert p.evaluate("id=>!doc.timeline?.clips.some(c=>c.noteId===id)",copied["id"])
            p.locator(".note .stamp").last.click()
            p.locator('#voicePreview').click();wait(p,'voicePreview && !voicePreview.paused')
            p.evaluate('voicePreview.pause()')
            at(p,0);p.locator('#play').click()
            if os.environ.get('FRAMENOTE_TEST_EDITS'):
                wait(p,"audioProbes.some(a=>a.src.endsWith(doc.notes.at(-1).voice.asset+'.m4a') && !a.paused && Math.abs(a.currentTime-transport.currentTime)<.2)")
            else:wait(p,"(()=>{const a=voicePlayers.get(doc.notes.at(-1).id);return a && !a.paused && Math.abs(a.currentTime-v.currentTime)<.2})()")
            p.locator('#play').click()
        package=work/'annotation-tools.framenote';p.locator('#save').click();save_dialog(proc,package)
        wait(p,"!busy && $('status').textContent.startsWith('审阅包已保存')",timeout=15000)
        expected=notes(p)
        with zipfile.ZipFile(package) as z:
            data=json.loads(z.read('review.json'));assert data['notes']==expected
            if cable:
                asset=next(n for n in z.namelist() if n.startswith('voice/'))
                recording=work/'voice.m4a';recording.write_bytes(z.read(asset))
                if tone:
                    raw=subprocess.check_output(['ffmpeg','-v','error','-i',str(recording),'-f','f32le','-ar','48000','-ac','1','-'])
                    samples=array.array('f');samples.frombytes(raw)
                    rms=math.sqrt(sum(v*v for v in samples)/len(samples));assert rms>.005,rms
                    print('Recorded virtual test-tone RMS:',rms,flush=True)
        p.locator('#exportVideo').click();out=work/'annotated.mp4';save_dialog(proc,out)
        wait(p,"!busy && $('status').textContent.startsWith('带批注的 MP4 已保存')",timeout=180000)
        meta=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams','-of','json',str(out)]))
        vstream=next(s for s in meta['streams'] if s['codec_type']=='video')
        assert vstream['codec_name']=='h264' and vstream['height']>360
        assert any(s['codec_type']=='audio' for s in meta['streams'])
        if tone:
            raw=subprocess.check_output(['ffmpeg','-v','error','-i',str(out),'-t','0.8','-f','f32le','-ar','48000','-ac','1','-'])
            samples=array.array('f');samples.frombytes(raw)
            for hz in [619,1423]:
                real=sum(v*math.cos(i*2*math.pi*hz/48000) for i,v in enumerate(samples))
                imag=sum(v*math.sin(i*2*math.pi*hz/48000) for i,v in enumerate(samples))
                amplitude=2*math.hypot(real,imag)/len(samples);assert amplitude>.005,(hz,amplitude)
                print('MP4 voice tone amplitude:',hz,amplitude,flush=True)
        subprocess.run(['ffmpeg','-v','error','-i',str(out),'-f','null','-'],check=True)
        p.screenshot(path=str(work/'annotation-tools.png'))
        assert not errors,errors
    finally:
        stop(proc)
        if tone:tone.communicate('\n',timeout=5)
    proc,p=launch(pw,package)
    try:
        assert notes(p)==expected
        p.locator('#exportVideo').click();cancelled=work/'cancelled.mp4';save_dialog(proc,cancelled)
        wait(p,"$('taskProgress').open")
        p.locator('#cancelProgress').click();wait(p,'!busy',timeout=20000)
        assert not cancelled.exists() and not list(work.glob('cancelled.mp4.*.tmp.mp4'))
    finally:stop(proc)
print('PASS: immediate notes, duration conversion, five tools, voice/package roundtrip, MP4 decode and cancellation.',work)

