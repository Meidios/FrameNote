'use strict';
let clipSelection=null;
function editState(){return structuredClone({notes:doc.notes,timeline:doc.timeline??null,rulerScales:doc.rulerScales??null,durationUs:doc.durationUs,version:doc.version,selected})}
function applyEditState(state){
 doc={...doc,...state};selected=state.selected??null;doc.lastNoteNumber=Math.max(doc.lastNoteNumber,...doc.notes.map(n=>n.number));
 doc.notes=doc.notes.map(n=>withMeasurementText(n,doc.rulerScales));
 transport.configure();inkKey=null;sendReviewNotes();render();
}
function commitTimeline(next,nextSelection=selected){
 history.push(editState());if(history.length>80)history.shift();future=[];lastEditGroup=null;loopId=null;
 if(clipSelection?.track!=='note'&&!next.timeline?.clips.some(c=>c.id===clipSelection?.id)){
  const old=doc.timeline?.clips.find(c=>c.id===clipSelection?.id);const replacement=old&&next.timeline?.clips.find(c=>c.track===old.track&&c.sourceUs>=old.sourceUs&&c.sourceUs<old.sourceUs+old.durationUs);clipSelection=replacement?{id:replacement.id,track:replacement.track}:null;
 }
 applyEditState({...next,selected:nextSelection});
}
function editAction(action,value){
 if(!ready||busy||window.voiceActive?.())return;
 finishEdit();transport.pause();
 try{
  const base=currentEditDoc(),next=EditModel.execute(base,clipSelection,action,value);
  if(next===base)return;
  commitTimeline(next);
  status((action==='join'?tr('片段已贴齐'):tr('剪辑已更新 · 删除保留空隙'))+bindingText('undo',' · ',tr(' 撤销')));
 }
 catch(e){status(e.message,true)}
}
function positionClip(el,c,duration){
 el.style.left=100*c.startUs/duration+'%';
 el.style.width=100*c.durationUs/duration+'%';
 el.style.setProperty('--source-offset',100*c.sourceUs/duration+'cqw');
}
function renderClips(previewTimeline=null){
 const usable=ready&&!imageDoc(),timeline=previewTimeline??(doc&&!imageDoc()?(doc.timeline??EditModel.initial(doc)):null);
 // Keep identities stable before the first edit.
 if(!previewTimeline&&timeline&&!doc.timeline){
  const previous=window.initialEdits?.docId===doc.id?window.initialEdits.value.clips:[];
  const clipKey=c=>c.track==='voice'?[c.track,c.noteId,c.asset].join(':'):c.track;
  const ids=new Map(previous.map(c=>[clipKey(c),c.id]));
  for(const c of timeline.clips)c.id=ids.get(clipKey(c))??c.id;
  window.initialEdits={docId:doc.id,value:timeline};
 }
 const linked=timeline?.linked??true,linkButton=$('linkTracks');
 linkButton.disabled=!usable;
 linkButton.setAttribute('aria-pressed',String(linked));
 linkButton.querySelector('use').setAttribute('href',linked?'#icon-link':'#icon-unlink');
 for(const track of ['video','audio','voice']){
  const host=$(track+'Clips');host.replaceChildren();
  if(timeline)host.style.setProperty('--source-width',100*timeline.sourceDurationUs/doc.durationUs+'cqw');
  for(const c of timeline?.clips.filter(c=>c.track===track)??[]){
   const el=document.createElement('button');el.className='editClip';el.dataset.id=c.id;el.dataset.track=track;positionClip(el,c,doc.durationUs);el.setAttribute('aria-pressed',String(clipSelection?.id===c.id));el.title=tr("{0} {1} – {2} · 拖动移动，两端裁剪",track==='video'?tr('视频'):track==='audio'?tr('原声'):tr('语音'),fmt(c.startUs),fmt(EditModel.end(c)));
   if(track!=='voice')el.title+=tr(' · 拖过另一片段的中点可换序');
   el.setAttribute('aria-label',track==='voice'?tr('语音批注'):track==='video'?tr('视频'):tr('原声'));
   if(c.track==='voice'&&c.noteId)el.dataset.noteId=c.noteId;
   if(track!=='voice'){
    const layer=svg('svg',{class:'clipMedia','aria-hidden':'true'});
    if(track==='audio'){layer.setAttribute('viewBox','0 0 1000 32');layer.setAttribute('preserveAspectRatio','none')}
    layer.append(svg('use',{href:track==='video'?'#sourceFrames':'#sourceWave'}));el.append(layer);
   }
   addTrimHandles(el);
   host.append(el);
  }
 }
 const exists=clipSelection&&(clipSelection.track==='note'?doc?.notes.some(n=>n.id===clipSelection.id):timeline?.clips.some(c=>c.id===clipSelection.id));
 const joins=usable?EditModel.joinStarts(timeline,clipSelection,doc.durationUs):null;
 $('joinClip').disabled=joins?.previous==null;
 $('joinNextClip').disabled=joins?.next==null;
 $('moreEdits').disabled=!usable;
 $('deleteClip').disabled=!usable||!exists;$('splitClip').disabled=!usable||(!timeline?.linked&&!exists);
 document.body.classList.toggle('editedTimeline',!!doc?.timeline);
}
function currentEditDoc(){return structuredClone({...doc,timeline:doc.timeline??window.initialEdits?.value??EditModel.initial(doc)})}
$('linkTracks').onclick=()=>{finishEdit();transport.pause();const next=currentEditDoc();next.version=3;next.timeline.linked=!next.timeline.linked;commitTimeline(next)};
$('splitClip').onclick=()=>{if(!doc.timeline)doc={...doc,timeline:null};const base=currentEditDoc();try{finishEdit();transport.pause();commitTimeline(EditModel.execute(base,clipSelection,'split',stamp()))}catch(e){status(e.message,true)}};
$('deleteClip').onclick=()=>editAction('delete');
$('joinClip').onclick=()=>editAction('join');
$('joinNextClip').onclick=()=>editAction('join','next');
let drag=null;
function clipDragViews(timeline,selectedClip){
 const elements=new Map(Array.from(document.querySelectorAll('.editClip'),el=>[el.dataset.id,el]));
 return timeline.clips.filter(c=>c.id===selectedClip.id || timeline.linked&&
  c.startUs<EditModel.end(selectedClip)&&EditModel.end(c)>selectedClip.startUs)
  .map(clip=>({clip,nodes:[elements.get(clip.id)]}));
}
function previewClipDrag(d,value){
 const {startUs}=d.c,stop=EditModel.end(d.c);
 for(const {clip,nodes} of d.views){
  const pieces=EditModel.clipPieces(clip,d.action,startUs,stop,value);
  while(nodes.length<pieces.length){
   const copy=nodes[0].cloneNode(true);nodes.at(-1).after(copy);nodes.push(copy);
  }
  nodes.forEach((el,i)=>{
   el.hidden=i>=pieces.length;
   if(!el.hidden)positionClip(el,pieces[i],d.base.durationUs);
  });
 }
}
function showInsertion(d,preview){
 const state=preview??d.base;
 renderClips(state.timeline);
 if(state.timeline.linked){
  $('markLanes').replaceChildren();notesByTime(state.notes).forEach(appendTimelineMark);layoutMarks();
 }
 d.el=$(d.c.track+'Clips').querySelector('[data-id="'+d.c.id+'"]');
 d.el.classList.toggle('snapped',!!preview);
 if(!preview)d.views=clipDragViews(state.timeline,d.c);
}
$('timeline').addEventListener('pointerdown',e=>{
 const el=e.target.closest('[data-track][data-id]');if(drag||!el||e.button||!ready||busy||window.voiceActive?.())return;
 e.preventDefault();e.stopPropagation();finishEdit();transport.pause();
 $('timeline').focus({preventScroll:true});
 clipSelection={id:el.dataset.id,track:el.dataset.track};
 const action=e.target.dataset.edge??'move',note=clipSelection.track==='note',resizeNote=note&&action!=='move';
 const annotation=note?doc.notes.find(n=>n.id===clipSelection.id):null;
 const moveRegion=note&&action==='move'&&annotation?.endUs!=null;
 const base=resizeNote||moveRegion?doc:currentEditDoc(),c=note?annotation:base.timeline.clips.find(c=>c.id===clipSelection.id);
 if(!c||resizeNote&&c.voice)return;
 const width=$(note?'markLanes':'ticks').getBoundingClientRect().width;
 if(!width)return;
 const edges=note?base.notes.filter(n=>n.id!==c.id).flatMap(n=>[n.startUs,n.endUs??n.startUs]):base.timeline.clips.filter(other=>other.id!==c.id&&other.track===clipSelection.track).flatMap(other=>[other.startUs,EditModel.end(other)]);
 edges.push(0,base.durationUs);
 const bounds=note?(resizeNote?{min:0,max:base.durationUs}:EditModel.noteMoveBounds(base,c)):EditModel.clipBounds(base,c,action);
 const voices=moveRegion?Array.from($('voiceClips').querySelectorAll('[data-note-id="'+c.id+'"]'),el=>({el,left:parseFloat(el.style.left)})):[];
 const voice=c.track==='voice',mark=note?el:voice&&c.noteId?$('markLanes').querySelector('[data-id="'+c.noteId+'"]'):null;
 drag={el,base,c,target:clipSelection,x:e.clientX,width,action,edges,resizeNote,moveRegion,bounds,voices,mark,views:note||voice?null:clipDragViews(base.timeline,c),labelWidth:mark?.firstElementChild.offsetWidth??0,pointerId:e.pointerId,moved:false};$('timeline').setPointerCapture(e.pointerId);
 if(!note&&action==='move')drag.obstacles=EditModel.moveObstacles(base.timeline,c);
 if(action==='move'&&(c.track==='video'||c.track==='audio'))
  drag.neighbors=base.timeline.clips.filter(other=>other.track===c.track&&other.id!==c.id).sort((a,b)=>a.startUs-b.startUs);
 el.setAttribute('aria-pressed','true');$('deleteClip').disabled=false;
},true);
$('timeline').addEventListener('pointermove',e=>{
 if(drag?.pointerId!==e.pointerId)return;const d=drag,delta=Math.round((e.clientX-d.x)/d.width*d.base.durationUs);if(Math.abs(e.clientX-d.x)<3&&!d.moved)return;d.moved=true;
 const note=d.target.track==='note',length=note?(d.c.endUs??d.c.startUs)-d.c.startUs:d.c.durationUs;
 const start=d.c.startUs,stop=start+length,raw=(d.action==='right'?stop:start)+delta;
 const snapped=bound('noSnap',e,0)?raw:EditModel.snap(raw,d.action==='move'?length:0,[...d.edges,shown],d.base.durationUs*8/d.width,d.bounds);
 const bounded=EditModel.clamp(snapped,d.bounds),value=d.obstacles?EditModel.fitMove(start,bounded,d.obstacles):bounded;
 if(d.neighbors?.length){
  const pointer=(e.clientX-$('ticks').getBoundingClientRect().left)/d.width*d.base.durationUs;
  const edge=EditModel.insertionEdge(d.neighbors,d.c,pointer,value!==bounded);
  if(edge!=null){
   if(edge!==d.insertEdge){
    try{d.insertPreview=EditModel.execute(d.base,d.target,'insert',edge)}
    catch(err){cancelTimelineDrag();status(err.message,true);return}
    d.insertEdge=edge;showInsertion(d,d.insertPreview);
   }
   return;
  }
  if(d.insertPreview){d.insertEdge=null;d.insertPreview=null;showInsertion(d,null)}
 }
 const didSnap=value!==bounded||value===snapped&&value!==raw;
 d.value=value;d.el.classList.toggle('snapped',didSnap);
 let range;
 if(d.c.track==='voice'){
  const edit=EditModel.editVoiceClip(d.base.timeline,d.c,d.action,value);
  positionClip(d.el,edit.clip,d.base.durationUs);
  range=edit.range;
 }else if(note){
  range=d.resizeNote?EditModel.noteRange(d.c,d.action,value,d.base.durationUs):{startUs:value,endUs:d.c.endUs===null?null:value+length};
  for(const voice of d.voices)voice.el.style.left=(voice.left+100*(value-start)/d.base.durationUs)+'%';
 }else{
  previewClipDrag(d,value);return;
 }
 if(d.mark){
  const scale=d.width/d.base.durationUs;
  positionMark(d.mark,range.startUs*scale,(range.endUs??range.startUs)*scale,range.endUs!==null,d.labelWidth);
 }
});
$('timeline').addEventListener('pointerup',e=>{
 if(drag?.pointerId!==e.pointerId)return;const d=drag;drag=null;if($('timeline').hasPointerCapture(e.pointerId))$('timeline').releasePointerCapture(e.pointerId);
 if(d.moved&&d.resizeNote){
  if(doc!==d.base||!doc.notes.includes(d.c)||d.c.voice){render();return}
  const next=EditModel.execute(doc,d.target,d.action,d.value),n=next.notes.find(n=>n.id===d.c.id);
  if(n&&(n.startUs!==d.c.startUs||n.endUs!==d.c.endUs)){loopId=null;commit(next.notes,n.id);status(tr('批注时长已更新')+bindingText('undo',' · ',tr(' 撤销')))}
  else render();
 }
 else if(d.moved&&d.moveRegion){
  if(doc!==d.base||!doc.notes.includes(d.c)){render();return}
  const next=EditModel.execute(doc,d.target,'move',d.value);
  if(next!==doc){commitTimeline(next,d.c.id);status(tr('批注位置已更新')+bindingText('undo',' · ',tr(' 撤销')))}
  else render();
 }
 else if(d.moved){
  try{
   const next=d.insertPreview??EditModel.execute(d.base,d.target,d.action,d.value);
   if(next===d.base){render();return}
   commitTimeline(next);status((d.insertPreview?tr('片段顺序已调整'):tr('剪辑已更新'))+bindingText('undo',' · ',tr(' 撤销')));
  }catch(err){status(err.message,true);render()}
 }
 else{if(d.target.track==='note')select(d.target.id);else seekUs(d.c.startUs);render()}
});
function cancelTimelineDrag(){
 if(!drag)return;
 const id=drag.pointerId;drag=null;
 if($('timeline').hasPointerCapture(id))$('timeline').releasePointerCapture(id);
 render();status(tr('已取消拖动'));
}
for(const type of ['pointercancel','lostpointercapture'])$('timeline').addEventListener(type,e=>{
 if(drag?.pointerId===e.pointerId&&e.target===$('timeline'))cancelTimelineDrag();
});
document.addEventListener('keydown',e=>{
 if(drag&&bound('cancel',e)){e.preventDefault();e.stopImmediatePropagation();cancelTimelineDrag()}
},true);
$('timeline').addEventListener('click',e=>{
 const el=e.target.closest('button[data-track][data-id]');
 if(!el)return;
 e.preventDefault();e.stopPropagation();
 if(e.detail!==0||!ready||busy||window.voiceActive?.())return;
 const {track,id}=el.dataset;
 clipSelection={track,id};
 if(track==='note')select(id);
 else{
  const clip=currentEditDoc().timeline.clips.find(c=>c.id===id);
  if(!clip)return;
  finishEdit();seekUs(clip.startUs);render();
 }
 $('timeline').querySelector('button[data-id="'+CSS.escape(id)+'"]')?.focus({preventScroll:true});
},true);
chrome.webview.addEventListener('message',({data:m})=>{
 if(m.type!=='loaded'&&m.type!=='image')return;
 if(!m.recovering){clipSelection=null;window.initialEdits=null}
 if(m.type==='loaded')transport.configure(m);else transport.pause();
 renderClips();
});
