'use strict';
let clipSelection=null;
function editState(){return structuredClone({notes:doc.notes,timeline:doc.timeline??null,durationUs:doc.durationUs,version:doc.version,selected})}
function applyEditState(state){
 doc={...doc,...state};selected=state.selected??null;doc.lastNoteNumber=Math.max(doc.lastNoteNumber,...doc.notes.map(n=>n.number));
 transport.configure();send('notes',{notes:doc.notes,timeline:doc.timeline??null,durationUs:doc.durationUs});render();
}
function commitTimeline(next){
 history.push(editState());if(history.length>80)history.shift();future=[];lastEditGroup=null;loopId=null;
 if(clipSelection?.track!=='note'&&!next.timeline?.clips.some(c=>c.id===clipSelection?.id)){
  const old=doc.timeline?.clips.find(c=>c.id===clipSelection?.id);const replacement=old&&next.timeline?.clips.find(c=>c.track===old.track&&c.sourceUs>=old.sourceUs&&c.sourceUs<old.sourceUs+old.durationUs);clipSelection=replacement?{id:replacement.id,track:replacement.track}:null;
 }
 applyEditState({...next,selected});
}
function editAction(action,value){
 if(!ready||busy||window.voiceActive?.())return;
 finishEdit();transport.pause();
 try{const base=currentEditDoc();commitTimeline(EditModel.execute(base,clipSelection,action,value));status('剪辑已保存 · 删除保留空隙，Ctrl+Z 撤销');}
 catch(e){status(e.message,true)}
}
function renderClips(){
 const usable=ready&&!imageDoc(),timeline=doc&&!imageDoc()?(doc.timeline??EditModel.initial(doc)):null;
 // Keep identities stable before the first edit.
 if(timeline&&!doc.timeline){if(!window.initialEdits||window.initialEdits.docId!==doc.id)window.initialEdits={docId:doc.id,value:timeline};else {const old=window.initialEdits.value.clips;timeline.clips=timeline.clips.map(c=>old.find(x=>x.track===c.track&&(c.track!=="voice"||x.noteId===c.noteId&&x.asset===c.asset))??c);window.initialEdits.value=timeline;}}
 $('editTools').hidden=!usable;$('linkTracks').setAttribute('aria-pressed',String(timeline?.linked??true));$('linkTracks').textContent=timeline?.linked===false?'独立编辑':'联动所有轨道';
 for(const track of ['video','audio','voice']){
  const host=$(track+'Clips');host.replaceChildren();
  for(const c of timeline?.clips.filter(c=>c.track===track)??[]){
   const el=document.createElement('button');el.className='editClip';el.dataset.id=c.id;el.dataset.track=track;el.style.left=100*c.startUs/doc.durationUs+'%';el.style.width=100*c.durationUs/doc.durationUs+'%';el.setAttribute('aria-pressed',String(clipSelection?.id===c.id));el.title=`${track==='video'?'视频':track==='audio'?'原声':'语音'} ${fmt(c.startUs)} – ${fmt(EditModel.end(c))} · 拖动移动，两端裁剪`;
   const label=document.createElement('span');label.textContent=track==='voice'?'语音批注':track==='video'?'视频':'原声';el.append(label);
   for(const edge of ['left','right']){const handle=document.createElement('span');handle.className='clipHandle '+edge;handle.dataset.edge=edge;el.append(handle)}
   host.append(el);
  }
 }
 const exists=clipSelection&&(clipSelection.track==='note'?doc?.notes.some(n=>n.id===clipSelection.id):timeline?.clips.some(c=>c.id===clipSelection.id));
 $('deleteClip').disabled=!exists;$('splitClip').disabled=!usable||(!timeline?.linked&&!exists);
 for(const el of $('markLanes').children){const n=doc.notes.find(n=>'#'+n.number===el.firstElementChild?.textContent);if(n){el.dataset.id=n.id;el.dataset.track='note';if(n.endUs!==null)for(const edge of ['left','right']){const h=document.createElement('span');h.className='clipHandle '+edge;h.dataset.edge=edge;el.append(h)}}}
 document.body.classList.toggle('editedTimeline',!!doc?.timeline);window.paintClipMedia?.();
}
function currentEditDoc(){return structuredClone({...doc,timeline:doc.timeline??window.initialEdits?.value??EditModel.initial(doc)})}
$('linkTracks').onclick=()=>{finishEdit();transport.pause();const next=currentEditDoc();next.version=3;next.timeline.linked=!next.timeline.linked;commitTimeline(next)};
$('splitClip').onclick=()=>{if(!doc.timeline)doc={...doc,timeline:null};const base=currentEditDoc();try{finishEdit();transport.pause();commitTimeline(EditModel.execute(base,clipSelection,'split',stamp()))}catch(e){status(e.message,true)}};
$('deleteClip').onclick=()=>editAction('delete');
let drag=null;
$('timeline').addEventListener('pointerdown',e=>{
 const el=e.target.closest('[data-track][data-id]');if(!el||e.button||!ready||busy||window.voiceActive?.())return;
 e.preventDefault();e.stopPropagation();finishEdit();transport.pause();
 clipSelection={id:el.dataset.id,track:el.dataset.track};
 const base=currentEditDoc(),c=clipSelection.track==='note'?base.notes.find(n=>n.id===clipSelection.id):base.timeline.clips.find(c=>c.id===clipSelection.id);
 if(!c)return;const rect=$('ticks').getBoundingClientRect();
 drag={el,base,c,target:clipSelection,x:e.clientX,width:rect.width,action:e.target.dataset.edge??'move',moved:false};$('timeline').setPointerCapture(e.pointerId);
 el.setAttribute('aria-pressed','true');$('deleteClip').disabled=false;
},true);
$('timeline').addEventListener('pointermove',e=>{
 if(!drag)return;const d=drag,delta=Math.round((e.clientX-d.x)/d.width*d.base.durationUs);if(Math.abs(e.clientX-d.x)<3&&!d.moved)return;d.moved=true;
 const length=d.target.track==='note'?(d.c.endUs??d.c.startUs+1)-d.c.startUs:d.c.durationUs;
 const start=d.c.startUs,stop=start+length,raw=(d.action==='right'?stop:start)+delta;
 const edges=[0,...d.base.timeline.clips.filter(c=>c.id!==d.c.id&&c.track===d.target.track).flatMap(c=>[c.startUs,EditModel.end(c)]),...d.base.notes.filter(n=>d.target.track==='note'&&n.id!==d.c.id).flatMap(n=>[n.startUs,n.endUs??n.startUs])];
 const value=e.altKey?raw:EditModel.snap(raw,d.action==='move'?length:0,edges,d.base.durationUs*8/d.width);
 d.value=value;const a=d.action==='right'?start:value,b=d.action==='left'?stop:d.action==='right'?value:value+length;
 d.el.style.left=100*a/d.base.durationUs+'%';d.el.style.width=Math.max(.1,100*(b-a)/d.base.durationUs)+'%';d.el.classList.toggle('snapped',value!==raw);
 status(`${d.action==='move'?'移动':'裁剪'} ${fmt(value)}${value!==raw?' · 已吸附':''} · Alt 暂停吸附`);
});
$('timeline').addEventListener('pointerup',e=>{
 if(!drag)return;const d=drag;drag=null;if($('timeline').hasPointerCapture(e.pointerId))$('timeline').releasePointerCapture(e.pointerId);
 if(d.moved){try{commitTimeline(EditModel.execute(d.base,d.target,d.action,d.value));status('剪辑已保存 · Ctrl+Z 撤销')}catch(err){status(err.message,true);render()}}
 else{if(d.target.track==='note')select(d.target.id);else seekUs(d.c.startUs);render()}
});
$('timeline').addEventListener('pointercancel',()=>{drag=null;render()});
$('timeline').addEventListener('click',e=>{if(e.target.closest('[data-track]')){e.preventDefault();e.stopPropagation()}},true);
document.addEventListener('keydown',e=>{if(e.target.closest('input,textarea,select')||!ready||busy||window.voiceActive?.())return;if(e.key==='Delete'&&clipSelection){e.preventDefault();editAction('delete')}if(e.key.toLowerCase()==='s'&&!e.ctrlKey&&!e.altKey&&!e.metaKey){e.preventDefault();$('splitClip').click()}});
chrome.webview.addEventListener('message',({data:m})=>{if(m.type==='loaded'||m.type==='image'){clipSelection=null;window.initialEdits=null;if(m.type==='loaded')transport.configure(m);else transport.pause();renderClips()}});
