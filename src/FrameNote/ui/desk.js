'use strict';
const $=id=>document.getElementById(id), v=$('video'), overlay=$('overlay');
const send=(type,data={})=>chrome.webview.postMessage({type,...data});
window.addEventListener('dragover',e=>{e.preventDefault();e.dataTransfer.dropEffect='copy'});
window.addEventListener('drop',e=>{e.preventDefault();if(!editing && e.dataTransfer.files.length)chrome.webview.postMessageWithAdditionalObjects({type:'drop'},Array.from(e.dataTransfer.files))});
let imageBase="",temporary=false,doc=null,frames=[],shown=0,selected=null,editing=null,inUs=null,loopId=null,history=[],future=[],lastEditGroup=null,tool=null,anchor=null,busy=false,ready=false;
const imageDoc=()=>doc?.version===2;
const fmt=us=>{const ms=Math.floor((us+500)/1000);return String(Math.floor(ms/60000)).padStart(2,'0')+':'+String(Math.floor(ms/1000)%60).padStart(2,'0')+'.'+String(ms%1000).padStart(3,'0')};
function status(t,error=false){$('status').textContent=t;$('status').title=t;$('status').classList.toggle('error',error)}
function index(us){let lo=0,hi=frames.length;while(lo<hi){const mid=(lo+hi)>>>1;if(frames[mid]<=us+2)lo=mid+1;else hi=mid}return Math.max(0,lo-1)}
function seekUs(us,pause=true){if(!ready)return;if(pause)transport.pause();const i=index(us);transport.currentTime=(frames[i]+(doc.timeline?0:((frames[i+1]??doc.durationUs)-frames[i])*.5))/1e6;}
function stamp(){return frames[index(shown)]}
function annotationTime(){return imageDoc()?0:Math.min(doc.durationUs-1,doc.timeline?Math.round(transport.currentTime*1e6):stamp())}
function canAnnotate(){return !!(imageDoc()||ready)&&!busy&&!imagePending&&!window.voiceActive?.()}
function tick(_,m){if(doc?.timeline){v.requestVideoFrameCallback(tick);return;}window.syncNoteVoices?.();shown=Math.round(m.mediaTime*1e6);$('clock').textContent=fmt(shown);$('seek').value=shown;$('timeline').style.setProperty('--position',(100*shown/(doc?.durationUs||1))+'%');paintPlayback();const n=loopId&&doc?.notes.find(n=>n.id===loopId);if(n && !transport.paused && shown>=n.endUs)transport.currentTime=n.startUs/1e6;v.requestVideoFrameCallback(tick)}
v.requestVideoFrameCallback(tick);
let playbackId=null,entryUs=0,reviewProxy=false,autoPlay=false;
v.onloadeddata=()=>{if(!doc || imageDoc())return;ready=true;if(entryUs>0){seekUs(entryUs);entryUs=0;}if(autoPlay){autoPlay=false;transport.play().catch(e=>{if(e.name!=='AbortError')status('请点击播放：'+e.message,true)})}layout();render();status(temporary?"临时录屏：切换视频或退出后会清理。要保留视频和批注，请导出审阅包。":doc.fileName+(reviewProxy?' · 临时审阅预览':''))};
v.onerror=()=>{if(!doc || imageDoc())return;ready=false;render();if(playbackId && !reviewProxy && v.getAttribute('src'))send('decodeError',{playbackId,us:stamp()??0});else status('无法解码审阅预览。可使用 VLC 播放原片。',true)};
function setIconAction(id,icon,label,hint){
 const button=$(id);
 if(button.ariaDescription===hint)return;
 button.querySelector('use').setAttribute('href','#icon-'+icon);
 button.querySelector('.actionName').textContent=label;
 button.ariaDescription=hint;
}
v.onplay=v.onpause=()=>{const label=transport.paused?'播放':'暂停';setIconAction('play',transport.paused?'play':'pause',label,shortcutHint(label,'play'));paint()};
v.onended=()=>{if(doc?.timeline)return;const n=doc?.notes.find(n=>n.id===loopId);if(n){transport.currentTime=n.startUs/1e6;transport.play().catch(e=>status(e.message,true))}};
function layout(){if(imageDoc())return;const w=v.clientWidth,h=v.clientHeight,k=Math.min(w/(v.videoWidth||1),h/(v.videoHeight||1)),vw=v.videoWidth*k,vh=v.videoHeight*k;
 Object.assign(overlay.style,{width:vw+'px',height:vh+'px',left:(w-vw)/2+'px',top:(h-vh)/2+'px'});paint()}
new ResizeObserver(layout).observe($('stage'));
const palette=[['琥珀','#ffd886'],['天蓝','#8ddcff'],['薄荷','#8de3af'],['珊瑚','#ff9d97'],['紫藤','#cfb1ff'],['粉红','#ffaed8']];
const noteColor=n=>n.color??palette[(n.number-1)%palette.length][1];
for(const [name,color] of palette){
 const b=document.createElement('button');b.type='button';b.title=name;b.setAttribute('aria-label',name);b.dataset.color=color;
 b.style.setProperty('--note',color);b.onclick=()=>{if(editing){editing.color=color;persistEdit();refreshDetails();paint()}};$('colors').append(b);
}
function updateColors(note){for(const b of $('colors').children)b.setAttribute('aria-pressed',String(b.dataset.color===noteColor(note)))}
const noteStamp=n=>imageDoc()?'':fmt(n.startUs)+(n.endUs===null?'':' – '+fmt(n.endUs));
const pointSuffix=n=>!imageDoc() && n.endUs===null?'　点':'';
const noteDescription=n=>n.text||(n.voice?'语音批注':shapeNames[n.shape?.kind]||(n.pictures?.length?'图片批注':'待填写批注'));
const notesByTime=notes=>[...notes].sort((a,b)=>a.startUs-b.startUs||a.number-b.number);
const notePreview=$('notePreview');
let hovered=null,hoverBadge=null,inkKey='',rows=new Map(),hidePreviewTimer,previewPointer=null;
function placePreview(noteSelector,pointer){
 const stage=$('stage').getBoundingClientRect(),group=$('ink').querySelector(':scope > g'+noteSelector);
 const drawing=group.querySelector('.inkShape')??group.querySelector('.picture');
 const bounds=(drawing??hoverBadge).getBoundingClientRect();
 const halfStroke=Number(drawing?.getAttribute('stroke-width')??0)/2;
 const gap=3,pad=8,width=notePreview.offsetWidth,height=notePreview.offsetHeight;
 let x=bounds.left-halfStroke,y=drawing?bounds.top-halfStroke:bounds.bottom+gap;
 // Keep a stage-height card beside the pointer so the original hit target stays reachable.
 if(notePreview.classList.contains('scrollable')){
  x=pointer.clientX+gap+width<=stage.right-pad?pointer.clientX+gap:pointer.clientX-gap-width;
  y=pointer.clientY;
 }else if(x<stage.left || x>stage.right || y<stage.top || y>stage.bottom){
  x=pointer.clientX;y=pointer.clientY;
 }
 const top=y-gap-height>=stage.top+pad?y-gap-height:y+gap;
 notePreview.style.left=Math.max(pad,Math.min(x-stage.left,stage.width-width-pad))+'px';
 notePreview.style.top=Math.max(pad,Math.min(top-stage.top,stage.height-height-pad))+'px';
}
function highlight(id,pointer){
 clearTimeout(hidePreviewTimer);
 if(hovered===id)return;
 hoverBadge?.classList.remove('inPreview');hoverBadge=null;
 rows.get(hovered)?.classList.remove('hovered');hovered=id;
 const row=rows.get(id),note=id && doc?.notes.find(n=>n.id===id);
 if(!note){notePreview.hidden=true;return}
 if(row){row.classList.add('hovered');row.scrollIntoView({block:'nearest'})}
 $('hoverNumber').textContent='#'+note.number;
 $('hoverStamp').textContent=noteStamp(note)+pointSuffix(note);
 const body=$('hoverText');body.textContent=noteDescription(note);
 notePreview.style.setProperty('--note',noteColor(note));notePreview.hidden=false;body.scrollTop=0;
 notePreview.classList.toggle('scrollable',body.scrollHeight>body.clientHeight);
 const noteSelector='[data-note="'+CSS.escape(id)+'"]';
 hoverBadge=overlay.querySelector('.inkBadge'+noteSelector);
 hoverBadge.classList.add('inPreview');
 placePreview(noteSelector,pointer);
}
function svg(tag,attrs){
 const el=document.createElementNS('http://www.w3.org/2000/svg',tag);
 for(const [key,val] of Object.entries(attrs))el.setAttribute(key,val);
 return el;
}
let strokeNote=null;
let paintedFrom=0,nextInkChange=0;
function paintPlayback(){
 if(!editing && shown>=paintedFrom && shown<nextInkChange){refreshLiveMagnifiers();return}
 paint();
}
function paint(){
 const g=$('ink'),w=overlay.clientWidth,h=overlay.clientHeight;
 paintedFrom=shown;nextInkChange=Infinity;
 const visible=(doc?.notes??[]).filter(n=>{
  if(n.id===editing?.id)return false;
  if(imageDoc())return true;
  const frame=n.endUs===null?index(n.startUs):0;
  const start=n.endUs!==null?n.startUs-2:frame===0?-Infinity:frames[frame]-2;
  const end=n.endUs??((frames[frame+1]??Infinity)-2);
  if(shown<start){nextInkChange=Math.min(nextInkChange,start);return false}
  if(shown>=end)return false;
  nextInkChange=Math.min(nextInkChange,end);return true;
 });
 if(editing)visible.push(editing);
 if(strokeNote?.shape)visible.push(strokeNote);
 const activeId=editing?.id??selected;
 const controlNote=activeId && !tool && (imageDoc()||transport.paused)?visible.find(n=>n.id===activeId):null;
 const key=JSON.stringify([w,h,...sourceSize(),editing?.id,controlNote?.id,activePicture,visible.map(n=>[n.id,n.number,n.color,n.text,n.shape,n.pictures])]);
 if(key===inkKey){refreshLiveMagnifiers();return}
 inkKey=key;highlight(null);g.replaceChildren();liveMagnifiers=[];if(!w||!h)return;
 drawLiveMagnifiers(g,visible,w,h);
 drawInk(g,visible,w,h);
 if(controlNote)drawEditHandles(g,controlNote,w,h);
 // A frame change can replace the shape under a stationary mouse.
 if(previewPointer && visible.length && !tool && !busy && !geometryDrag){
  const {clientX,clientY}=previewPointer;
  const hit=document.elementFromPoint(clientX,clientY);
  const id=!hit?.closest('.geometryHandles') && hit?.closest('#ink [data-note]')?.dataset.note;
  if(id)highlight(id,previewPointer);
 }
}
function drawInk(g,visible,w,h,pixels=sourceSize()){
 const badges=[],labels=svg('g',{class:'inkLabels'});
 for(const n of visible){
  const s=n.shape,c=noteColor(n),group=svg('g',{'data-note':n.id,color:c});
  let x=4,y=4;
  if(n.pictures?.length){const p=n.pictures[0];x=p.x*w;y=p.y*h-22;drawPictures(group,n,w,h)}
  if(s){const drawing=svg('g',{'data-shape':''});group.append(drawing);drawShape(drawing,n,w,h,pixels);x=Math.min(s.x,s.toX)*w+3;y=Math.min(s.y,s.toY)*h-22;}
  g.append(group);
  const bw=12+8*String('#'+n.number).length,bh=19;
  x=Math.max(1,Math.min(x,w-bw-1));y=Math.max(1,Math.min(y,h-bh-1));
  // Keep tags attached to their shapes when possible; move coincident tags to the nearest free slot.
  const clear=(px,py)=>badges.every(b=>px+bw+2<=b.x||b.x+b.w+2<=px||py+bh+2<=b.y||b.y+bh+2<=py);
  if(!clear(x,y)){
   let found=false;
   for(let dx=0;dx<w&&!found;dx+=bw+3)for(const px of new Set([x+dx,x-dx])){
    if(px<0||px+bw>w)continue;
    for(let dy=0;dy<h&&!found;dy+=bh+3)for(const py of [y+dy,y-dy]){
     if(py>=0&&py+bh<=h&&clear(px,py)){x=px;y=py;found=true;break}
    }
    if(found)break;
   }
  }
  badges.push({x,y,w:bw,h:bh});const tag=svg('g',{'data-note':n.id,class:'inkBadge',transform:'translate('+x+' '+y+')'});
  tag.append(svg('rect',{width:bw,height:bh,rx:3,fill:c,stroke:'#172029','stroke-width':1}));
  const text=svg('text',{x:bw/2,y:13.5,'text-anchor':'middle'});text.textContent='#'+n.number;tag.append(text);labels.append(tag);
 }
 g.append(labels);
}
const previewStage=$('stage');
previewStage.addEventListener('pointermove',e=>{
 previewPointer=e.pointerType!=='touch' && !e.buttons?{clientX:e.clientX,clientY:e.clientY}:null;
});
function resetPreview(){previewPointer=null;highlight(null)}
previewStage.addEventListener('pointerleave',resetPreview);
previewStage.addEventListener('pointerdown',()=>{previewPointer=null},true);
overlay.addEventListener('pointerover',e=>{
 const id=e.target.closest('[data-note]')?.dataset.note;
 if(e.target.closest('.geometryHandles')){highlight(null);return;}
 if(id && !tool && !geometryDrag && !busy && e.buttons===0 && e.pointerType!=='touch')highlight(id,e);
});
function leavePreview(e){
 if(e.relatedTarget?.closest?.('#ink [data-note],#notePreview'))return;
 // Allow the pointer to cross the small gap to scroll a note taller than the stage.
 if(e.relatedTarget && notePreview.classList.contains('scrollable') && !notePreview.hidden){
  clearTimeout(hidePreviewTimer);hidePreviewTimer=setTimeout(()=>highlight(null),180);
 }else highlight(null);
}
overlay.addEventListener('pointerout',leavePreview);
overlay.addEventListener('pointercancel',resetPreview);
notePreview.addEventListener('pointerenter',()=>clearTimeout(hidePreviewTimer));
notePreview.addEventListener('pointerleave',leavePreview);
notePreview.addEventListener('wheel',e=>e.stopPropagation());
notePreview.addEventListener('pointerdown',e=>e.stopPropagation());
window.addEventListener('blur',resetPreview);
overlay.addEventListener('click',e=>{
 const id=e.target.closest('[data-note]')?.dataset.note;
 if(id && (ready||imageDoc()) && id!==editing?.id && !tool && !busy)select(id,{seek:false});
});
function pos(e){const r=overlay.getBoundingClientRect(),clamp=x=>Math.min(1,Math.max(0,x));return [clamp((e.clientX-r.x)/r.width),clamp((e.clientY-r.y)/r.height)]}
let anglePoints=[],strokePointer=null,textClick=null;
function startDrawing(kind,at=annotationTime()){
 if(!canAnnotate())return;
 finishEdit();transport.pause();loopId=null;
 const note=blankNote(at,null);if(!note)return;
 setTool(kind);strokeNote=note;
}
function finishStroke(){
 const note=strokeNote;
 setTool(null);begin(note,false);persistEdit();
 if(note.shape.kind==='text')focusNoteText();
}
function captureStroke(e){
 strokePointer=e.pointerId;overlay.setPointerCapture(e.pointerId);
}
let quickButton=-1,quickMods=0;
$('stage').addEventListener('pointerdown',e=>{
 quickButton=-1;
 if(e.pointerType!=='mouse'||!e.isPrimary||!canAnnotate()||geometryDrag||strokePointer!==null||e.target.closest('#imageNav,button'))return;
 const vk=[1,4,2,5,6][e.button];
 const kind=bound('quickBox',e,vk)?'box':bound('quickArrow',e,vk)?'arrow':bound('quickText',e,vk)?'text':null;
 if(!kind)return;
 const r=overlay.getBoundingClientRect();
 if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)return;
 e.preventDefault();e.stopImmediatePropagation();quickButton=e.button;
 startDrawing(kind);if(!strokeNote)return;
 quickMods=keyMods(e);
 if(kind==='text')textClick={x:e.clientX,y:e.clientY,at:pos(e)};
 else anchor=pos(e);
 captureStroke(e);
},true);
for(const event of ['contextmenu','auxclick','click']){
 $('stage').addEventListener(event,e=>{
  if(quickButton!==e.button)return;
  e.preventDefault();e.stopImmediatePropagation();
 },true);
}
overlay.onpointerdown=e=>{
 if(!strokeNote||!tool||e.button!==0||!e.isPrimary)return;e.preventDefault();
 if(tool==='angle'){
  anglePoints.push(pos(e));
  if(anglePoints.length===3){const [a,b,c]=anglePoints;strokeNote.shape={kind:'angle',x:a[0],y:a[1],toX:b[0],toY:b[1],thirdX:c[0],thirdY:c[1]};finishStroke()}
  else status(anglePoints.length===1?'点击角度顶点':'点击第二条边的端点');
  return;
 }
 anchor=pos(e);captureStroke(e);
};
overlay.onpointermove=e=>{if(e.pointerId!==strokePointer||!anchor||!strokeNote)return;const [toX,toY]=pos(e);strokeNote.shape={kind:tool,x:anchor[0],y:anchor[1],toX,toY,thirdX:null,thirdY:null};paint()};
overlay.onpointerup=e=>{
 if(!strokeNote||e.pointerId!==strokePointer)return;
 if(textClick){
  if(Math.hypot(e.clientX-textClick.x,e.clientY-textClick.y)>5){setTool(null);return}
  const [px,py]=textClick.at,w=Math.min(.8,180/overlay.clientWidth),h=Math.min(.6,48/overlay.clientHeight);
  const x=Math.min(px,1-w),y=Math.min(py,1-h);
  strokeNote.shape={kind:'text',x,y,toX:x+w,toY:y+h,thirdX:null,thirdY:null};
  finishStroke();return;
 }
 if(!anchor)return;
 overlay.onpointermove(e);
 const s=strokeNote.shape;
 if(Math.hypot((s.toX-s.x)*overlay.clientWidth,(s.toY-s.y)*overlay.clientHeight)<2){setTool(null);return}
 finishStroke();
};
overlay.onpointercancel=()=>setTool(null);
overlay.addEventListener('lostpointercapture',e=>{if(e.target===overlay&&strokePointer===e.pointerId)setTool(null)});
window.addEventListener('blur',()=>{if(strokePointer!==null)setTool(null)});
function setTool(t){
 const changed=tool!==t;
 if(t)highlight(null);
 if(imageDoc()&&['ruler','magnifier'].includes(t))imageView.ensureOriginal();
 tool=t;anglePoints=[];strokeNote=null;anchor=null;textClick=null;quickMods=0;
 const pointer=strokePointer;strokePointer=null;
 if(pointer!==null&&overlay.hasPointerCapture(pointer))overlay.releasePointerCapture(pointer);
 overlay.classList.toggle('drawing',!!t);
 for(const kind of Object.keys(shapeNames)){
  const button=$(kind==='ruler'?'toolRuler':kind);
  button.classList.toggle('active',kind===t);
  button.setAttribute('aria-pressed',String(kind===t));
 }
 if(t)status(t==='angle'?'依次点击第一条边端点、顶点、第二条边端点':t==='text'?'拖出文字框，内容使用当前批注文字':t==='magnifier'?'拖出放大区域（2×），随视频实时更新':'在画面上按住并拖动以绘制标记');
 if(changed)paint();
}
function refreshDetails(){
 window.refreshVoice?.();
 const n=editing??doc?.notes.find(x=>x.id===selected);
 $('emptyDetails').hidden=!!n;$('detailFields').hidden=!n;
 const canEdit=ready||imageDoc();
 $('annotationTools').disabled=!canEdit;
 $('clear').disabled=!n?.shape;
 $('removePicture').hidden=canEdit&&!n?.pictures?.length;
 if(!n)return;
 if(!editing)$('body').value=n.text;
 $('noteTiming').hidden=imageDoc();if(document.activeElement!==$('noteDuration'))$('noteDuration').value=n.endUs===null?'0':String((n.endUs-n.startUs)/1e6);$('noteDuration').max=(doc.durationUs-n.startUs)/1e6;
 if(document.activeElement!==$('noteStart'))$('noteStart').value=n.startUs/1e6;
 $('noteStart').max=(doc.durationUs-Math.max(1,(n.endUs??n.startUs)-n.startUs))/1e6;
 $('noteDuration').disabled=!!n.voice;
 updateColors(n);
}
function closeEditor(){
 endGeometryDrag(false);activePicture=-1;editing=null;lastEditGroup=null;anchor=null;setTool(null);refreshDetails();paint();window.captureButtons?.();
}
function begin(note,focus=true){
 if(!(ready||imageDoc())||!note||window.voiceActive?.())return;
 if(editing?.id!==note.id){
  closeEditor();transport.pause();loopId=null;editing=structuredClone(note);
  activePicture=note.shape?-1:(editing.pictures?.length??0)-1;$('body').value=note.text;refreshDetails();paint();
 }
 if(focus)$('body').focus({preventScroll:true});
}
function persistEdit(group=null){
 if(!editing)return;
 editing.text=$('body').value.trim();const n=structuredClone(editing);

 const i=doc.notes.findIndex(x=>x.id===n.id);
 if(i>=0&&JSON.stringify(doc.notes[i])===JSON.stringify(n))return;
 const notes=doc.notes.slice();if(i<0)notes.push(n);else notes[i]=n;
 commit(notes,n.id,group);
}
function finishEdit(){
 if(tool)setTool(null);
 if(!editing)return;
 endGeometryDrag(false);
 persistEdit();
 closeEditor();
}
function enterDetails(e){
 if(!editing&&e.target.closest('#detailFields,#clear,#removePicture,#voiceRemove'))begin(doc?.notes.find(x=>x.id===selected),false);
}
document.addEventListener('focusin',enterDetails);
document.addEventListener('click',enterDetails,true);
$('body').addEventListener('input',e=>{if(!e.isComposing)persistEdit('text')});
$('body').addEventListener('compositionend',()=>persistEdit('text'));
$('body').addEventListener('blur',()=>{lastEditGroup=null});
function leaveDetails(e){
 if((editing||tool)&&!imagePending&&!e.target.closest('#editor,#annotationTools,#overlay,#notePreview,#canvasMenu,.noteCopy,#renumberNotes'))finishEdit();
}
document.addEventListener('pointerdown',leaveDetails,true);
document.addEventListener('focusin',leaveDetails,true);
function blankNote(a,b){
 if(doc.notes.length>=10000){status('本审阅已达到 10000 条批注上限。',true);return null}
 if(doc.lastNoteNumber>=2147483647){status('本审阅的批注编号已用尽。',true);return null}
 return {id:crypto.randomUUID(),number:doc.lastNoteNumber+1,color:palette[doc.lastNoteNumber%palette.length][1],pictures:null,startUs:a,endUs:b,text:'',shape:null,voice:null};
}
function newNote(a,b){finishEdit();const n=blankNote(a,b);if(!n)return;begin(n);persistEdit();rows.get(n.id)?.scrollIntoView({block:'nearest'})}
let focusAfterFullscreen=false;
function focusNoteText(){
 if(isFullPreview()){focusAfterFullscreen=true;send('previewFullscreen',{on:false})}
 else $('body').focus({preventScroll:true});
}
function select(id,{seek=true}={}){
 finishEdit();const n=doc.notes.find(x=>x.id===id);if(!n)return;
 selected=id;loopId=null;if(seek)seekUs(n.startUs);render();rows.get(id)?.scrollIntoView({block:'nearest'});
}
function snapshotNotes(){return editState()}
function applyNotes(notes,id,copyFrom){
 if(doc.timeline){
  const before=new Map(doc.notes.map(n=>[n.id,n])),after=new Map(notes.map(n=>[n.id,n]));
  const clips=doc.timeline.clips.flatMap(c=>{
   const old=before.get(c.noteId),n=after.get(c.noteId);
   if(c.track!=='voice'||!old?.voice)return [c];
   if(!n?.voice||n.voice.asset!==old.voice.asset)return [];
   return n.startUs===old.startUs?[c]:[{...c,startUs:c.startUs+n.startUs-old.startUs}];
  });
  for(const n of notes)if(n.voice&&n.voice.asset!==before.get(n.id)?.voice?.asset){
   if(copyFrom&&n.id===id)clips.push(...doc.timeline.clips.filter(c=>c.track==='voice'&&c.noteId===copyFrom).map(c=>({...c,id:crypto.randomUUID(),noteId:id})));
   else clips.push({id:crypto.randomUUID(),track:'voice',startUs:n.startUs,sourceUs:0,durationUs:Math.min(n.voice.durationUs,(n.endUs??doc.durationUs)-n.startUs),asset:n.voice.asset,sourceDurationUs:n.voice.durationUs,noteId:n.id});
  }
  doc.timeline={...doc.timeline,clips};
 }
 doc.notes=notes;selected=id;doc.lastNoteNumber=notes.reduce((max,n)=>Math.max(max,n.number),doc.lastNoteNumber);
 if(loopId&&!notes.some(n=>n.id===loopId))loopId=null;
 send('notes',{notes,timeline:doc.timeline??null,durationUs:doc.durationUs});render();
}
function commit(notes,id,group=null,copyFrom=null){
 const key=group&&id+':'+group;
 if(!key||key!==lastEditGroup){history.push(snapshotNotes());if(history.length>80)history.shift()}
 lastEditGroup=key;future=[];applyNotes(notes,id,copyFrom);
}
function moveHistory(redo){
 const from=redo?future:history,to=redo?history:future;
 if(!from.length)return;
 closeEditor();to.push(snapshotNotes());const state=from.pop();loopId=null;
 applyEditState(state);
}
function deleteNote(id){
 finishEdit();
 commit(doc.notes.filter(n=>n.id!==id),selected===id?null:selected);
}
$('renumberNotes').onclick=()=>{
 if(!canAnnotate()||geometryDrag||drag)return;
 const numbers=new Map(notesByTime(doc.notes).map((n,i)=>[n.id,i+1]));
 const notes=doc.notes.map(n=>{
  const number=numbers.get(n.id);
  if(n.number===number)return n;
  // Legacy notes derive their color from the old number.
  const color=noteColor(n);
  if(editing?.id===n.id){editing.number=number;editing.color=color}
  return {...n,number,color};
 });
 commit(notes,selected);
};
// The slider thumb and the note lanes share the same usable time axis.
function positionMark(el,start,end,region,labelWidth){
 const width=region?end-start:labelWidth;
 el.style.left=(region?start:start-labelWidth/2)+'px';el.style.width=width+'px';
 el.classList.toggle('point',!region);el.classList.toggle('region',region);
 el.classList.toggle('short',region&&width<labelWidth);
}
function addTrimHandles(button){
 for(const edge of ['left','right']){
  const handle=document.createElement('span');handle.className='clipHandle '+edge;
  handle.dataset.edge=edge;handle.setAttribute('aria-hidden','true');button.append(handle);
 }
}
function layoutMarks(){
 const lanes=$('markLanes'),entries=[...lanes.children];
 const sizes=entries.map(el=>el.firstElementChild.offsetWidth);
 const pad=Math.max(9,Math.ceil(sizes.reduce((m,n)=>Math.max(m,n),0)/2)+2);
 $('timeline').style.setProperty('--pad',pad+'px');
 const width=lanes.getBoundingClientRect().width;
 function place(spanWidth,draw){
  const ends=[];
  entries.forEach((el,i)=>{
   const start=Number(el.dataset.start)*spanWidth,end=Number(el.dataset.end)*spanWidth,span=end-start;
   const region=el.classList.contains('region'),body=region?span:sizes[i],center=region?(start+end)/2:start;
   const occupied=Math.max(body,sizes[i]);
   let row=ends.findIndex(x=>x+3<=center-occupied/2);if(row<0)row=ends.length;
   ends[row]=center+occupied/2;if(!draw)return;
   el.style.bottom=(row*25+3)+'px';el.style.setProperty('--label-width',sizes[i]+'px');
   positionMark(el,start,end,region,sizes[i]);
  });
  return ends.length*25;
 }
 const height=place(width,true);
 // Keep the visible track as tall as it is at 1×, even when zoom separates overlapping labels.
 const fitWidth=Math.max(0,$('scroll').clientWidth-2*pad);
 $('marks').style.height=(width===fitWidth?height:place(fitWidth,false))+'px';
 lanes.style.height=Math.max(4,height)+'px';
 $('timeline').style.setProperty('--gutter',($('marks').offsetWidth-$('marks').clientWidth)+'px');
}
let marksWidth=-1;
new ResizeObserver(([entry])=>{if(entry.contentRect.width!==marksWidth){marksWidth=entry.contentRect.width;layoutMarks()}}).observe($('markLanes'));
function noteBody(text){
 const body=document.createElement('span');body.className='noteBody';
 // Full-width sentence punctuation separates URLs in Chinese feedback.
 const searchable=text.replace(/[。，、；：！？（）【】《》“”‘’]/g,' ');
 let end=0;
 for(const link of linkify.find(searchable,'url',{defaultProtocol:'https'})){
  if(!/^https?:\/\//i.test(link.href))continue;
  body.append(text.slice(end,link.start));const a=document.createElement('a');
  a.href=link.href;a.textContent=text.slice(link.start,link.end);
  a.onclick=e=>{e.preventDefault();e.stopPropagation();if(!busy && e.detail<=1)send('openLink',{url:a.href})};
  a.ondblclick=e=>e.stopPropagation();body.append(a);end=link.end;
 }
 body.append(text.slice(end));return body;
}
function noteActionButton(className,icon,label,action){
 const button=document.createElement('button');
 button.type='button';button.className=className;button.title=label;button.setAttribute('aria-label',label);
 const graphic=svg('svg',{width:16,height:16,'aria-hidden':'true',viewBox:'0 0 24 24'});
 graphic.append(svg('use',{href:'#icon-'+icon}));button.append(graphic);
 button.onclick=e=>{e.stopPropagation();action()};return button;
}
async function copyNoteText(note){
 try{
  await navigator.clipboard.writeText(note.text);
  status('已复制批注 #'+note.number+' 的文字');
 }catch{status('未能写入剪贴板，请重试。',true)}
}
let reportedCanOpen;
function render(){$('exportExcel').disabled=!ready||imageDoc()||!doc?.notes.length;$('exportVideo').disabled=$('exportGif').disabled=!ready||imageDoc();refreshDetails();const notes=doc?.notes??[];rows.clear();$('count').textContent=notes.length;$('list').replaceChildren();$('markLanes').replaceChildren();
 const ordered=notesByTime(notes);
 $('renumberNotes').disabled=!(ready||imageDoc())||ordered.every((n,i)=>n.number===i+1);
 for(const n of ordered){
 const b=document.createElement('div'),t=document.createElement('button');rows.set(n.id,b);b.style.setProperty('--note',noteColor(n));b.className='note'+(n.id===selected?' selected':'');b.classList.toggle('hovered',n.id===hovered);t.className='stamp';t.textContent=noteStamp(n);const badge=document.createElement('strong');badge.className='noteNumber';badge.textContent='#'+n.number;t.prepend(badge,' ');b.append(t,noteBody(noteDescription(n)));b.onclick=()=>select(n.id);
 const trash=noteActionButton('noteDelete','trash','删除批注 #'+n.number,()=>deleteNote(n.id));
 const copy=noteActionButton('noteCopy','copy','复制批注 #'+n.number+' 的文字',()=>copyNoteText(n));
 copy.disabled=!n.text?.trim();
 b.append(trash,copy);$('list').append(b);
 if(imageDoc())continue;
 const m=document.createElement('button'),label=document.createElement('span');m.className=n.endUs===null?'point':'region';
 m.style.setProperty('--mark',noteColor(n));m.dataset.start=n.startUs/doc.durationUs;m.dataset.end=(n.endUs??n.startUs)/doc.durationUs;
 label.className='markLabel';label.textContent='#'+n.number;m.append(label);m.title=t.textContent+pointSuffix(n)+' '+n.text;m.setAttribute('aria-label',m.title);
 m.dataset.track='note';m.dataset.id=n.id;
 if(!n.voice){addTrimHandles(m);m.title+=' · 拖动两端调整批注时长'}
 m.setAttribute('aria-pressed',String(n.id===selected));m.disabled=!ready;
 m.onclick=()=>{select(n.id);
  $('markLanes').querySelector('[aria-pressed="true"]')?.focus({preventScroll:true})};$('markLanes').append(m)}
 layoutMarks();
 if(!notes.length){const p=document.createElement('div');p.className='empty';p.textContent='选择绘图工具，或右键画面新增批注。';$('list').append(p)}
 $('rangeHint').textContent=inUs===null?'':'起点 '+fmt(inUs)+bindingText('outBtn','　定位后按 ');
 for(const id of ['rate','fullPreview','mutePlayback','playbackVolume','play','prev','next','seek','point','inBtn'])$(id).disabled=!ready;
 $('copyImage').disabled=$('saveImage').disabled=!imageDoc();
 $('copyFrame').disabled=$('saveFrame').disabled=!ready;
 $('point').disabled=$('save').disabled=!(ready||imageDoc());$('open').disabled=false;$('outBtn').disabled=!ready||inUs===null;
 if(reportedCanOpen!==!$('open').disabled){reportedCanOpen=!$('open').disabled;send('canOpen',{value:reportedCanOpen})}
 $('vlc').disabled=!doc;
 const n=notes.find(x=>x.id===selected);$('loop').disabled=!n||n.endUs===null;window.captureButtons?.();$('loop').classList.toggle('active',!!loopId);$('loop').setAttribute('aria-pressed',String(!!loopId));paint();window.renderClips?.()}
$('exportExcel').onclick=()=>{finishEdit();transport.pause();send('exportExcel')};
$('exportVideo').onclick=()=>{finishEdit();transport.pause();send('exportVideo')};
const progressDialog=$('taskProgress');
function cancelProgress(){send(progressDialog.dataset.cancelCommand)}
$('cancelProgress').onclick=cancelProgress;
progressDialog.oncancel=e=>{e.preventDefault();cancelProgress()};
function showProgress(message){
 const dialog=progressDialog,loading=message.type==='preparingPreview';
 const command=loading?'cancelPreview':'cancelVideoExport';
 if(!message.active){if(dialog.open&&dialog.dataset.cancelCommand===command)dialog.close();return}
 dialog.dataset.cancelCommand=command;
 $('progressTitle').textContent=loading?'正在生成批注预览':message.text;
 $('progressBar').value=message.percent;
 $('progressPercent').textContent=message.percent+'%';
 $('cancelProgress').textContent=loading?'取消加载':'取消导出';
 if(!dialog.open)dialog.showModal();
}
$('open').onclick=$('start').onclick=()=>send('open');$('save').onclick=()=>send('save',{notes:doc.notes});
// Pointer capture keeps the release coordinates relative to this WebView even outside its window.
{
 const button=$('sendReaper');let down=null,suppressClick=false;
 const cancel=()=>{if(down){suppressClick=true;down=null;button.classList.remove('dragging')}};
 button.onpointerdown=e=>{
  if(e.button!==0||!e.isPrimary||busy)return;
  down={id:e.pointerId,x:e.clientX,y:e.clientY,drag:false};suppressClick=false;
  button.setPointerCapture(e.pointerId);
 };
 button.onpointermove=e=>{
  if(down?.id===e.pointerId&&!down.drag&&Math.hypot(e.clientX-down.x,e.clientY-down.y)>=5){
   down.drag=suppressClick=true;button.classList.add('dragging');
  }
 };
 button.onpointerup=e=>{
  if(down?.id!==e.pointerId)return;
  const drag=down.drag||Math.hypot(e.clientX-down.x,e.clientY-down.y)>=5;
  suppressClick=drag;down=null;button.classList.remove('dragging');
  if(drag)send('dropReaper',{x:e.clientX,y:e.clientY});
 };
 button.onlostpointercapture=button.onpointercancel=cancel;
 document.addEventListener('keydown',e=>{if(bound('cancel',e)&&down){cancel();e.preventDefault();e.stopImmediatePropagation()}},true);
 button.onclick=e=>{if(!suppressClick||e.detail===0)send('sendReaper')};
}
async function snapshot(save){
 if(busy||!ready||v.seeking)return status('请等当前帧显示后再捕获。');
 transport.pause();finishEdit();await new Promise(requestAnimationFrame);paint();
 send('snapshot',{save,playbackId,us:stamp(),ids:[...overlay.querySelectorAll('.inkBadge')].map(el=>el.dataset.note)});
}
$('copyFrame').onclick=()=>snapshot(false);$('saveFrame').onclick=()=>snapshot(true);
$('vlc').onclick=()=>{if(isFullPreview())send('previewFullscreen',{on:false});transport.pause();send('vlc',{playbackId,us:stamp()??0})};
$('play').onclick=()=>{if(transport.paused)transport.play().catch(e=>status(e.message,true));else transport.pause()};
$('prev').onclick=()=>seekUs(frames[Math.max(0,index(shown)-1)]);$('next').onclick=()=>seekUs(frames[Math.min(frames.length-1,index(shown)+1)]);
function clearPlaybackLoop(){if(loopId){loopId=null;render()}}
$('rate').onchange=()=>{v.defaultPlaybackRate=transport.playbackRate=Number($('rate').value)};
let lastAudible=100;
function displayVolume(value){
 v.volume=value/100;$('playbackVolume').value=value;$('playbackVolume').title=value+'%';
 if(value>0)lastAudible=value;
 setIconAction('mutePlayback',value?'speaker_2':'speaker_mute',value?'静音':'取消静音',value?'播放静音':'取消播放静音');
 $('mutePlayback').setAttribute('aria-pressed',String(value===0));
}
function changeVolume(value){displayVolume(value);send('playbackVolume',{value})}
$('playbackVolume').oninput=e=>changeVolume(Number(e.target.value));
$('mutePlayback').onclick=()=>changeVolume(Number($('playbackVolume').value)?0:lastAudible);
const isFullPreview=()=>document.body.classList.contains('previewFullscreen');
$('fullPreview').onclick=()=>send('previewFullscreen',{on:!isFullPreview()});
function showFullPreview(on){document.body.classList.toggle('previewFullscreen',on);setIconAction('fullPreview',on?'full_screen_minimize':'full_screen_maximize',on?'退出全屏':'全屏预览',on?shortcutHint('退出全屏','cancel'):'全屏预览');(focusAfterFullscreen&&!on&&editing?$('body'):$('fullPreview')).focus({preventScroll:true});focusAfterFullscreen=false;layout()}
function seekTimeline(us){clearPlaybackLoop();seekUs(us,false)}
$('seek').oninput=e=>seekTimeline(+e.target.value);
$('point').onclick=()=>newNote(annotationTime(),null);$('inBtn').onclick=()=>{transport.pause();inUs=stamp();render()};
$('outBtn').onclick=()=>{const end=stamp();if(end<=inUs)return status('终点必须晚于起点。',true);const a=inUs;inUs=null;newNote(a,end)};
$('loop').onclick=()=>{const n=doc.notes.find(x=>x.id===selected);if(loopId){loopId=null;transport.pause()}else{loopId=n.id;transport.currentTime=n.startUs/1e6;transport.play().catch(e=>status(e.message,true))}render()};
$('noteStart').onchange=()=>{
 if(!editing||imageDoc())return;
 const input=$('noteStart'),at=Math.round(input.valueAsNumber*1e6),delta=at-editing.startUs;
 const outside=doc.timeline?.clips.some(c=>c.noteId===editing.id&&(c.startUs+delta<0||c.startUs+delta+c.durationUs>doc.durationUs));
 if(!input.validity.valid||!Number.isFinite(at)||outside){input.value=editing.startUs/1e6;status('批注及关联语音必须位于时间线内。',true);return}
 editing.startUs=at;if(editing.endUs!==null)editing.endUs+=delta;
 persistEdit();seekUs(at);
};
$('noteDuration').onchange=()=>{
 if(!editing||imageDoc())return;
 const value=$('noteDuration').value,us=Math.round(Number(value)*1e6),max=doc.durationUs-editing.startUs;
 if(value===''||!Number.isFinite(us)||us<0||us>max||!$('noteDuration').validity.valid){status('时长须在 0 与剩余视频时长之间。',true);$('noteDuration').value=editing.endUs===null?0:(editing.endUs-editing.startUs)/1e6;return}
 editing.endUs=us>0?editing.startUs+us:null;persistEdit();refreshDetails();
};
$('editor').onsubmit=e=>{e.preventDefault();finishEdit();document.activeElement?.blur()};
for(const k of Object.keys(shapeNames))$(k==='ruler'?'toolRuler':k).onclick=()=>startDrawing(k);
$('clear').onclick=()=>{if(editing){editing.shape=null;setTool(null);persistEdit();paint()}};
const playlistToggle=$('playlistToggle');
playlistToggle.onclick=()=>send('togglePlaylist');
document.onkeydown=e=>{
 if(e.isComposing)return;
 const is=id=>bound(id,e);
 const typing=e.target.closest('textarea,input:not([type=range]),[contenteditable=true]');
 if(progressDialog.open){
  if(is('cancel')){e.preventDefault();cancelProgress()}
  else if(e.key==='Escape')e.preventDefault();
  return;
 }
 if(typing&&e.key.length===1&&!e.ctrlKey&&!e.altKey&&!e.metaKey&&!(editing&&(is('confirm')||is('cancel'))))return;
 const cancel=window.shortcutBindings.cancel,mods=keyMods(e);
 if(tool&&cancel.keys.some(b=>e.keyCode===b.vk&&(mods&b.mods)===b.mods&&(mods&~(quickMods|b.mods))===0)){
  e.preventDefault();setTool(null);return;
 }
 if(is('cancel')&&isFullPreview()){e.preventDefault();send('previewFullscreen',{on:false});return}
 if(busy||e.repeat||e.target.closest('#capturePanel'))return;
 if(!playlistToggle.hidden && e.code==='KeyL' && keyMods(e)===1){e.preventDefault();playlistToggle.click();return;}
 if(doc&&(is('undo')||is('redo'))){
  e.preventDefault();moveHistory(is('redo'));if(e.target===$('body'))e.target.blur();return;
 }
 if(doc&&is('save')){
  e.preventDefault();
  if(!geometryDrag&&!imagePending&&!tool&&!window.voiceActive?.()){finishEdit();$('save').click()}
  return;
 }
 if(!doc&&is('paste')){e.preventDefault();send('pasteAsImage');return}
 if(doc&&is('paste')&&!(e.ctrlKey&&e.keyCode===86&&keyMods(e)===2)){
  e.preventDefault();if(canAnnotate()){transport.pause();send('pasteNotePicture',{documentId:doc.id,at:annotationTime()})}return;
 }
 if(editing){
  if(is('cancel')){e.preventDefault();finishEdit();document.activeElement?.blur();return}
  if(is('confirm')){e.preventDefault();$('editor').requestSubmit();return}
 }
 if(typing||e.target.closest('select,#playbackVolume'))return;
 if(e.keyCode===86&&keyMods(e)===2&&!is('paste')&&!Object.values(window.shortcutBindings).some(action=>action.keys.some(b=>b.vk===86&&b.mods===2))){
  e.preventDefault();return;
 }
 if(isFullPreview()&&!['play','prev','next'].some(is))return;
 if(is('open')){e.preventDefault();$('open').click();return}
 if(imageDoc()&&(is('prev')||is('next'))){e.preventDefault();imageView.adjacent(is('prev')?-1:1);return}
 if(!(ready||imageDoc()))return;
 if(e.key===' '&&e.target.closest('#transport button,#imageNav button,#playlistToggle,.noteCopy,#marks button[aria-pressed="false"]'))return;
 if(ready&&is('picture')){e.preventDefault();pickPicture();return}
 if(ready&&is('splitClip')){
  if(!drag&&!geometryDrag&&!window.voiceActive?.()&&!$('splitClip').disabled){e.preventDefault();$('splitClip').click()}
  return;
 }
 if(ready&&(is('joinClip')||is('joinNextClip'))){
  if(!drag&&!geometryDrag&&!e.target.closest('input')){e.preventDefault();$(is('joinClip')?'joinClip':'joinNextClip').click()}
  return;
 }
 const action=['point','inBtn','outBtn','play','prev','next'].find(is);
 if(action){e.preventDefault();$(action).click();return}
 if((is('nextNote')||is('prevNote'))&&doc.notes.length){
  e.preventDefault();const ns=notesByTime(doc.notes);
  select(ns[Math.min(ns.length-1,Math.max(0,ns.findIndex(x=>x.id===selected)+(is('nextNote')?1:-1)))].id);
 }
};
chrome.webview.addEventListener('message',({data:m})=>{if(m.type==='volume'){displayVolume(m.value);return}if(m.type==='previewFullscreen'){showFullPreview(m.on);return}if(m.type==='timelineData'||m.type==='shortcuts'||m.type==='clipboardPicture')return;
 if(m.type==='playlistState'){playlistToggle.hidden=!m.visible;playlistToggle.setAttribute('aria-expanded',String(m.expanded));return;}
 if(m.type==='fullImage'){if(m.id===doc?.id)imageView.upgrade(m.src);return;}
 if(m.type==='image'){
  doc=m.doc;imageBase=m.images;$('imageToolbar').append($('point'),$('annotationTools'));refreshPointButton();$('modeLabel').textContent='图片审阅';ready=autoPlay=temporary=false;entryUs=0;frames=[];shown=0;playbackId=inUs=selected=loopId=null;history=[];future=[];
  transport.pause();v.removeAttribute('src');v.load();closeEditor();$('welcome').hidden=true;document.body.classList.add('imageView');
  imageView.show(m.src,m.folder,m.scaled);render();status(m.name);return;
 }
 if(m.type==='loaded'){transport.pause();$('inBtn').before($('point'));$('editTools').before($('annotationTools'));$('modeLabel').textContent='视频审阅';imageView.reset();document.body.classList.remove('imageView');$('openedImage').hidden=true;$('openedImage').removeAttribute('src');autoPlay=m.autoPlay;displayVolume(m.volume);$('duration').textContent=fmt(m.doc.durationUs);$('rate').value='1';v.defaultPlaybackRate=transport.playbackRate=1;playbackId=m.playbackId;entryUs=m.us;reviewProxy=m.proxy;imageBase=m.images;temporary=!!m.temporary;doc=m.doc;refreshPointButton();frames=m.frames;shown=frames[0];ready=false;inUs=selected=loopId=null;history=[];future=[];closeEditor();$('welcome').hidden=true;$('seek').max=doc.durationUs;$('seek').value=0;$('seek').style.removeProperty('--played');v.src=m.src;render();status('正在打开视频…')}
 else if(m.type==='resumeReview'){seekUs(m.us);status(doc.fileName+(reviewProxy?' · 临时审阅预览':''))}
 else if(m.type==='preparingPreview'||m.type==='exportProgress')showProgress(m);
 else if(['voiceStarted','voiceFinishing','voiceStored','catalog','meters','region','recording','recordShortcut','pictureStored'].includes(m.type))return;
 else if(m.type==='busy'){busy=m.value;document.body.classList.toggle('busy',busy);if(busy){highlight(null);status('正在处理文件…')}}
 else status(m.text,m.type==='error')});render();



