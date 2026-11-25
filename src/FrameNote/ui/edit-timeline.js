'use strict';
let clipSelection=[];
const singleClip=()=>clipSelection.length===1?clipSelection[0]:null;
function editState(){return structuredClone({notes:doc.notes,timeline:doc.timeline??null,rulerScales:doc.rulerScales??null,durationUs:doc.durationUs,version:doc.version,selected,clipSelection})}
function applyEditState({clipSelection:selection,...state}){
 if(selection)clipSelection=selection;
 doc={...doc,...state};selected=state.selected??null;doc.lastNoteNumber=Math.max(doc.lastNoteNumber,...doc.notes.map(n=>n.number));
 doc.notes=doc.notes.map(n=>withMeasurementText(n,doc.rulerScales));
 if(timeSelection)setTimeSelection(timeSelection);
 transport.configure();inkKey=null;sendReviewNotes();render();
}
function commitTimeline(next,nextSelection=selected){
 history.push(editState());if(history.length>80)history.shift();future=[];lastEditGroup=null;
 const ids=new Set(next.timeline?.clips.map(c=>c.id));
 clipSelection=clipSelection.filter(c=>c.track==='note'?next.notes.some(n=>n.id===c.id):ids.has(c.id));
 applyEditState({...next,selected:next.notes.some(n=>n.id===nextSelection)?nextSelection:null});
}
function editAction(action,value){
 if(!ready||busy||window.voiceActive?.())return;
 finishEdit();transport.pause();
 try{
  const base=currentEditDoc(),next=clipSelection.length>1&&action==='delete'
   ?EditModel.editGroup(EditModel.clipGroup(base,clipSelection,'delete'),'delete')
   :EditModel.execute(base,singleClip(),action,value);
  if(next===base)return;
  commitTimeline(next);
  status((action==='join'?tr('片段已贴齐'):tr('剪辑已更新 · 删除保留空隙'))+bindingText('undo',' · ',tr(' 撤销')));
 }
 catch(e){status(e.message,true)}
}
let clipClipboard=null;
function runClipCommand(action){
 if(!ready||busy||imageDoc()||tool||drag||geometryDrag||window.voiceActive?.())return false;
 const selection=clipSelection.filter(c=>c.track==='video'||c.track==='audio');
 const pasted=action==='paste';
 if(pasted?clipClipboard?.docId!==doc.id:!selection.length)return false;
 try{
  finishEdit();transport.pause();const base=currentEditDoc();
  const group=pasted?null:EditModel.clipGroup(base,selection,'delete');
  const copy=pasted?clipClipboard.copy:EditModel.copyClips(group);
  if(action==='cut'||action==='copy'){
   if(action==='cut')commitTimeline(EditModel.editGroup(group,'delete'),null);
   clipClipboard={docId:doc.id,copy};status(tr(action==='cut'?'已剪切片段':'已复制片段'));
  }else{
   const at=pasted?Math.round(transport.currentTime*1e6):group.startUs+group.durationUs;
   const result=EditModel.pasteClips(base,copy,at);
   commitTimeline(result.doc,null);
   clipSelection=result.inserted.filter(c=>c.track==='video'||c.track==='audio').map(({track,id})=>({track,id}));
   renderClips();seekUs(at,false);
  }
 }catch(error){status(error.message,true)}
 return true;
}
function clipShortcut(e){
 if(!e.ctrlKey||e.shiftKey||e.altKey||e.metaKey||!e.target.closest('#trackDesk,#seek')||e.target.closest('textarea,input:not([type=range]),[contenteditable=true]'))return false;
 const action={KeyX:'cut',KeyC:'copy',KeyV:'paste',KeyD:'duplicate'}[e.code];
 return action&&(runClipCommand(action)||action==='paste'&&!tool);
}
function showClipMenu(e,target){
 const menu=$('clipMenu');
 if(target&&!clipSelection.some(c=>c.id===target.id))clipSelection=[target];
 if(!target)clipSelection=[];
 renderClips();menu.querySelectorAll('button').forEach(b=>{
  b.disabled=b.dataset.clipAction==='paste'?clipClipboard?.docId!==doc.id:!target;
 });
 if(!menu.querySelector('button:enabled'))return;
 menu.showPopover();menu.style.left=clamp(e.clientX,4,Math.max(4,innerWidth-menu.offsetWidth-4))+'px';
 menu.style.top=clamp(e.clientY,4,Math.max(4,innerHeight-menu.offsetHeight-4))+'px';
 menu.querySelector('button:enabled').focus({preventScroll:true});
}
$('clipMenu').onclick=e=>{
 const b=e.target.closest('button');if(!b)return;
 $('clipMenu').hidePopover();$('timeline').focus({preventScroll:true});runClipCommand(b.dataset.clipAction);
};
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
 const ids=new Set(clipSelection.map(c=>c.id));
 const audioNames=new Map(doc?.notes.filter(n=>n.voice?.original).map(n=>[n.id,n.voice.original.name]));
 for(const track of ['video','audio','voice']){
  const host=$(track+'Clips');host.replaceChildren();
  if(timeline)host.style.setProperty('--source-width',100*timeline.sourceDurationUs/doc.durationUs+'cqw');
  for(const c of timeline?.clips.filter(c=>c.track===track)??[]){
   const label=track==='video'?tr('视频'):track==='audio'?tr('原声'):audioNames.get(c.noteId)??tr('语音批注');
   const el=document.createElement('button');el.className='editClip';el.dataset.id=c.id;el.dataset.track=track;positionClip(el,c,doc.durationUs);el.setAttribute('aria-pressed',String(ids.has(c.id)));el.ariaDescription=tr("{0} {1} – {2} · 拖动移动，两端裁剪",label,fmt(c.startUs),fmt(EditModel.end(c)));
   if(track!=='voice')el.ariaDescription+=tr(' · 拖过另一片段的中点可换序');
   el.setAttribute('aria-label',label);
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
 updateClipActions(timeline,usable);
 document.body.classList.toggle('editedTimeline',!!doc?.timeline);
}
function updateClipActions(timeline,usable){
 const target=singleClip(),exists=clipSelection.length>1||(target?.track==='note'?doc?.notes.some(n=>n.id===target.id):timeline?.clips.some(c=>c.id===target?.id));
 const joins=usable?EditModel.joinStarts(timeline,target,doc.durationUs):null;
 $('joinClip').disabled=joins?.previous==null;
 $('joinNextClip').disabled=joins?.next==null;
 $('moreEdits').disabled=!usable;
 $('deleteClip').disabled=!usable||!exists;$('splitClip').disabled=!usable||clipSelection.length>1||(!timeline?.linked&&!exists);
}
function currentEditDoc(){return structuredClone({...doc,timeline:doc.timeline??window.initialEdits?.value??EditModel.initial(doc)})}
$('linkTracks').onclick=()=>{finishEdit();transport.pause();const next=currentEditDoc();next.version=3;next.timeline.linked=!next.timeline.linked;commitTimeline(next)};
$('splitClip').onclick=()=>{if(!doc.timeline)doc={...doc,timeline:null};const base=currentEditDoc();try{finishEdit();transport.pause();commitTimeline(EditModel.execute(base,singleClip(),'split',stamp()))}catch(e){status(e.message,true)}};
$('deleteClip').onclick=()=>editAction('delete');
$('joinClip').onclick=()=>editAction('join');
$('joinNextClip').onclick=()=>editAction('join','next');
let drag=null;
function paintTimeSelection(range){
 const band=$('timeRange');band.hidden=!range;
 if(range){
  band.style.left=100*range.startUs/doc.durationUs+'%';
  band.style.width=100*(range.endUs-range.startUs)/doc.durationUs+'%';
 }
}
function setTimeSelection(range){
 const endUs=Math.min(range?.endUs??0,doc?.durationUs??0);
 timeSelection=range&&range.startUs<endUs?{startUs:range.startUs,endUs}:null;
 if(!timeSelection)loopEnabled=false;
 paintTimeSelection(timeSelection);updateLoopButton();
}
function timeAtPointer(x){
 const axis=$('ticks').getBoundingClientRect();
 return Math.round(Math.max(0,Math.min(1,(x-axis.left)/axis.width))*doc.durationUs);
}
function previewTimeRange(d,e){
 const x=e.clientX;
 if(!d.moved&&Math.abs(x-d.x)<3)return timeSelection;
 d.moved=true;
 if(d.edge){
  const {startUs,endUs}=timeSelection,width=$('ticks').getBoundingClientRect().width;
  const delta=Math.round((x-d.x)*doc.durationUs/width);
  if(!delta){paintTimeSelection(timeSelection);return timeSelection}
  const first=d.edge==='start',raw=Math.max(0,Math.min(doc.durationUs,(first?startUs:endUs)+delta));
  const time=first?Math.min(raw,endUs-1):Math.max(raw,startUs+1);
  const i=index(time,frames,0);
  const range=first?{startUs:time<frames[0]?0:frames[i],endUs}:
   {startUs,endUs:frames[i]>=time?frames[i]:frames[i+1]??doc.durationUs};
  paintTimeSelection(range);return range;
 }
 const time=timeAtPointer(x),from=Math.min(d.anchor,time),to=Math.max(d.anchor,time);
 if(from===to){paintTimeSelection(timeSelection);return timeSelection}
 // Cover every touched frame, including the last frame at the end of the ruler.
 const first=index(from),last=index(to);
 const startUs=from<frames[0]?0:frames[first];
 const endUs=frames[last]>=to-2?frames[last]:frames[last+1]??doc.durationUs;
 const range={startUs,endUs:endUs>startUs?endUs:frames[first+1]??doc.durationUs};
 paintTimeSelection(range);return range;
}
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
function beginMarquee(e){
 const timeline=$('timeline'),rect=timeline.getBoundingClientRect();
 const entries=[];
 for(const lane of timeline.querySelectorAll('.mediaTrack')){
  const row=lane.getBoundingClientRect();
  for(const el of lane.querySelectorAll('.editClip')){
   const clip=el.getBoundingClientRect();
   entries.push({el,target:{id:el.dataset.id,track:el.dataset.track},left:clip.left-rect.left,right:clip.right-rect.left,
    top:row.top-rect.top,bottom:row.bottom-rect.top});
  }
 }
 drag={mode:'marquee',x:e.clientX-rect.left,y:e.clientY-rect.top,entries,previous:clipSelection,pointerId:e.pointerId,moved:false};
 timeline.setPointerCapture(e.pointerId);
}
function updateMarquee(d,e){
 const rect=$('timeline').getBoundingClientRect();
 const x=Math.max(0,Math.min(rect.width,e.clientX-rect.left)),y=Math.max(0,Math.min(rect.height,e.clientY-rect.top));
 if(!d.moved&&Math.hypot(x-d.x,y-d.y)<3)return;
 d.moved=true;
 const left=Math.min(d.x,x),right=Math.max(d.x,x),top=Math.min(d.y,y),bottom=Math.max(d.y,y);
 const box=$('clipMarquee');box.hidden=false;
 Object.assign(box.style,{left:left+'px',top:top+'px',width:right-left+'px',height:bottom-top+'px'});
 clipSelection=[];
 for(const item of d.entries){
  const hit=item.left<=right&&item.right>=left&&item.top<=bottom&&item.bottom>top;
  if(hit)clipSelection.push(item.target);
  if(item.el.getAttribute('aria-pressed')!==String(hit))item.el.setAttribute('aria-pressed',String(hit));
 }
}
function previewGroup(d,e,delta){
 if(!d.group){
  d.group=EditModel.clipGroup(d.base,clipSelection,'move');
  d.neighbors=d.group.fixed.filter(c=>c.track===d.c.track).sort((a,b)=>a.startUs-b.startUs);
  d.edges=[0,d.base.durationUs,...d.neighbors.flatMap(c=>[c.startUs,EditModel.end(c)])];
 }
 const group=d.group,raw=group.startUs+delta;
 const snapped=bound('noSnap',e,0)?raw:EditModel.snap(raw,group.durationUs,[...d.edges,shown],d.base.durationUs*8/d.width,group.bounds);
 const bounded=EditModel.clamp(snapped,group.bounds),value=EditModel.fitMove(group.startUs,bounded,group.obstacles);
 const pointer=(e.clientX-$('ticks').getBoundingClientRect().left)/d.width*d.base.durationUs;
 let edge=group.canInsert&&d.c.track!=='voice'?EditModel.insertionEdge(d.neighbors,group,pointer,value!==bounded):null;
 if(edge!=null&&d.insertEdge!==edge){
  const preview=EditModel.editGroup(group,'insert',edge);
  if(preview!==d.base){d.preview=preview;d.insertEdge=edge;d.nodes=null}
  else edge=null;
 }else if(edge!=null)return;
 if(edge==null){
  if(d.value===value&&d.insertEdge==null)return;
  d.insertEdge=null;d.value=value;
  d.preview=EditModel.editGroup(group,'move',value);
 }
 // Rebuild only when a cut changes the set of visible pieces. Free dragging
 // updates existing nodes, including notes that belong to moved recordings.
 if(!d.nodes||edge!=null||d.preview===d.base){
  renderClips(d.preview.timeline);
  $('markLanes').replaceChildren();notesByTime(d.preview.notes).forEach(appendTimelineMark);layoutMarks();
  d.nodes=edge==null&&d.preview!==d.base?new Map([...$('timeline').querySelectorAll('[data-track][data-id]')].map(el=>[el.dataset.id,el])):null;
 }else{
  for(const c of d.preview.timeline.clips)if(group.moving.has(c.id))positionClip(d.nodes.get(c.id),c,d.base.durationUs);
  for(const n of d.preview.notes){
   if(!group.notes.has(n.id)&&!group.directNotes.has(n.id))continue;
   const el=d.nodes.get(n.id),start=n.startUs/d.base.durationUs,end=(n.endUs??n.startUs)/d.base.durationUs;
   el.dataset.start=start;el.dataset.end=end;
   positionMark(el,start*d.width,end*d.width,n.endUs!=null,parseFloat(el.style.getPropertyValue('--label-width')));
  }
 }
}
$('timeline').addEventListener('pointerdown',e=>{
 const el=e.target.closest('[data-track][data-id]'),row=e.target.closest('.mediaTrack');
 if(drag||!e.isPrimary||!ready||busy||window.voiceActive?.())return;
 const handle=e.button===0&&e.target.closest('[data-time-edge]');
 if((handle||e.button===2)&&frames.length){
  e.preventDefault();e.stopPropagation();finishEdit();
  const timeline=$('timeline');timeline.focus({preventScroll:true});
  drag={mode:'time',x:e.clientX,anchor:timeAtPointer(e.clientX),moved:false,pointerId:e.pointerId};
  if(e.button===2&&row){
   drag.menuTarget=el&&['video','audio'].includes(el.dataset.track)?{id:el.dataset.id,track:el.dataset.track}:null;
   drag.menuTime=timeAtPointer(e.clientX);
  }
  if(handle){
   const band=$('timeRange').getBoundingClientRect();
   drag.edge=Math.abs(e.clientX-band.left)<=Math.abs(e.clientX-band.right)?'start':'end';
  }
  timeline.setPointerCapture(e.pointerId);return;
 }
 if(!el&&!row||e.button)return;
 e.preventDefault();e.stopPropagation();finishEdit();
 $('timeline').focus({preventScroll:true});
 if(row&&(!el||e.shiftKey)){beginMarquee(e);return}
 transport.pause();
 const target={id:el.dataset.id,track:el.dataset.track};
 const action=e.target.dataset.edge??'move',note=target.track==='note',resizeNote=note&&action!=='move';
 const multi=action==='move'&&clipSelection.length>1&&clipSelection.some(c=>c.id===target.id);
 if(!multi)clipSelection=[target];
 const annotation=note?doc.notes.find(n=>n.id===target.id):null;
 const moveRegion=note&&action==='move'&&annotation?.endUs!=null;
 const base=resizeNote||moveRegion?doc:currentEditDoc(),c=note?annotation:base.timeline.clips.find(c=>c.id===target.id);
 if(!c||resizeNote&&c.voice)return;
 const width=$(note?'markLanes':'ticks').getBoundingClientRect().width;
 if(!width)return;
 if(multi){
  drag={mode:'group',base,c,target,x:e.clientX,width,pointerId:e.pointerId,moved:false};
  $('timeline').setPointerCapture(e.pointerId);return;
 }
 for(const pressed of $('timeline').querySelectorAll('.editClip[aria-pressed=true]'))pressed.setAttribute('aria-pressed','false');
 const edges=note?base.notes.filter(n=>n.id!==c.id).flatMap(n=>[n.startUs,n.endUs??n.startUs]):base.timeline.clips.filter(other=>other.id!==c.id&&other.track===target.track).flatMap(other=>[other.startUs,EditModel.end(other)]);
 edges.push(0,base.durationUs);
 const bounds=note?(resizeNote?{min:0,max:base.durationUs}:EditModel.noteMoveBounds(base,c)):EditModel.clipBounds(base,c,action);
 const voices=moveRegion?Array.from($('voiceClips').querySelectorAll('[data-note-id="'+c.id+'"]'),el=>({el,left:parseFloat(el.style.left)})):[];
 const voice=c.track==='voice',mark=note?el:voice&&c.noteId?$('markLanes').querySelector('[data-id="'+c.noteId+'"]'):null;
 drag={el,base,c,target,x:e.clientX,width,action,edges,resizeNote,moveRegion,bounds,voices,mark,views:note||voice?null:clipDragViews(base.timeline,c),labelWidth:mark?.firstElementChild.offsetWidth??0,pointerId:e.pointerId,moved:false};
 (note&&el.isConnected?el:$('timeline')).setPointerCapture(e.pointerId);
 if(!note&&action==='move')drag.obstacles=EditModel.moveObstacles(base.timeline,c);
 if(action==='move'&&(c.track==='video'||c.track==='audio'))
  drag.neighbors=base.timeline.clips.filter(other=>other.track===c.track&&other.id!==c.id).sort((a,b)=>a.startUs-b.startUs);
 el.setAttribute('aria-pressed','true');$('deleteClip').disabled=false;
},true);
$('timeline').addEventListener('contextmenu',e=>{if(e.button===2)e.preventDefault()});
function moveTimelinePointer(e){
 if(drag?.pointerId!==e.pointerId)return;const d=drag;
 if(d.mode==='time'){previewTimeRange(d,e);return}
 if(d.mode==='marquee'){updateMarquee(d,e);return}
 const delta=Math.round((e.clientX-d.x)/d.width*d.base.durationUs);if(Math.abs(e.clientX-d.x)<3&&!d.moved)return;d.moved=true;
 if(d.mode==='group'){
  try{previewGroup(d,e,delta)}catch(err){cancelTimelineDrag();status(err.message,true)}
  return;
 }
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
  previewClipDrag(d,value);
  if(d.c.track==='video'&&d.action!=='move')scrubber.previewTrim(d.c,d.action,value);
  return;
 }
 if(d.mark){
  const scale=d.width/d.base.durationUs;
  positionMark(d.mark,range.startUs*scale,(range.endUs??range.startUs)*scale,range.endUs!==null,d.labelWidth);
 }
}
$('timeline').addEventListener('pointermove',moveTimelinePointer);
function releaseTimelinePointer(d){
 const owner=d.el?.hasPointerCapture(d.pointerId)?d.el:$('timeline');
 if(owner.hasPointerCapture(d.pointerId))owner.releasePointerCapture(d.pointerId);
}
$('timeline').addEventListener('pointerup',e=>{
 if(drag?.pointerId!==e.pointerId)return;const d=drag;
 if(d.c?.track==='video'&&(d.action==='left'||d.action==='right')){
  moveTimelinePointer(e);scrubber.endTrim();
 }
 drag=null;releaseTimelinePointer(d);
 if(d.mode==='time'){
  setTimeSelection(previewTimeRange(d,e));
  if(!d.moved&&'menuTarget' in d){seekUs(d.menuTime,false);showClipMenu(e,d.menuTarget)}
  return;
 }
 if(d.mode==='marquee'){
  updateMarquee(d,e);$('clipMarquee').hidden=true;
  if(!d.moved){
   clipSelection=[];
   const r=$('ticks').getBoundingClientRect();
   seekUs(Math.max(0,Math.min(1,(e.clientX-r.left)/r.width))*doc.durationUs,false);
  }
  renderClips();return;
 }
 if(d.mode==='group'&&(d.moved||Math.abs(e.clientX-d.x)>=3)){
  try{
   previewGroup(d,e,Math.round((e.clientX-d.x)/d.width*d.base.durationUs));
   if(d.preview&&d.preview!==d.base){commitTimeline(d.preview);status(tr('剪辑已更新')+bindingText('undo',' · ',tr(' 撤销')))}
   else render();
  }catch(err){render();status(err.message,true)}
  return;
 }
 if(d.moved&&d.resizeNote){
  if(doc!==d.base||!doc.notes.includes(d.c)||d.c.voice){render();return}
  const next=EditModel.execute(doc,d.target,d.action,d.value),n=next.notes.find(n=>n.id===d.c.id);
  if(n&&(n.startUs!==d.c.startUs||n.endUs!==d.c.endUs)){commit(next.notes,n.id);status(tr('批注时长已更新')+bindingText('undo',' · ',tr(' 撤销')))}
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
 else{
  if(d.mode==='group')clipSelection=[d.target];
  if(d.target.track==='note')select(d.target.id);
  else{seekUs(d.c.startUs);render()}
 }
});
function cancelTimelineDrag(){
 if(!drag)return;
 if(drag.mode==='time')paintTimeSelection(timeSelection);
 if(drag.mode==='marquee'){clipSelection=drag.previous;$('clipMarquee').hidden=true}
 const d=drag;drag=null;releaseTimelinePointer(d);
 scrubber.endTrim();
 render();status(tr('已取消拖动'));
}
for(const type of ['pointercancel','lostpointercapture'])$('timeline').addEventListener(type,e=>{
 if(drag?.pointerId===e.pointerId&&(e.target===$('timeline')||e.target===drag.el))cancelTimelineDrag();
});
document.addEventListener('keydown',e=>{
 if(drag?.mode==='time'&&(bound('cancel',e)||e.key==='Escape')){
  e.preventDefault();e.stopImmediatePropagation();cancelTimelineDrag();setTimeSelection(null);return;
 }
 if(drag&&(bound('cancel',e)||drag.mode==='marquee'&&e.shiftKey&&bound('cancel',e,e.keyCode,keyMods(e)&~4))){
  e.preventDefault();e.stopImmediatePropagation();cancelTimelineDrag();
 }
},true);
$('markLanes').addEventListener('dblclick',e=>{
 const el=e.target.closest('button.region');
 if(e.button!==0||!el)return;
 const note=doc?.notes.find(n=>n.id===el.dataset.id);
 if(note){e.preventDefault();loopRegion(note)}
});
$('timeline').addEventListener('click',e=>{
 const el=e.target.closest('button[data-track][data-id]');
 if(!el)return;
 e.preventDefault();e.stopPropagation();
 if(e.detail!==0||!ready||busy||window.voiceActive?.())return;
 const {track,id}=el.dataset;
 clipSelection=[{track,id}];
 if(track==='note')select(id);
 else{
  const clip=currentEditDoc().timeline.clips.find(c=>c.id===id);
  if(!clip)return;
  clearNoteSelection();finishEdit();seekUs(clip.startUs);render();
 }
 $('timeline').querySelector('button[data-id="'+CSS.escape(id)+'"]')?.focus({preventScroll:true});
},true);
chrome.webview.addEventListener('message',({data:m})=>{
 if(m.type!=='loaded'&&m.type!=='image')return;
 if(clipClipboard?.docId!==m.doc?.id)clipClipboard=null;
 if(!m.recovering){if(drag)cancelTimelineDrag();setTimeSelection(null);clipSelection=[];window.initialEdits=null}
 if(m.type==='loaded')transport.configure(m);else transport.pause();
 renderClips();
});
