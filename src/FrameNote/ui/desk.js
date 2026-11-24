'use strict';
const $=id=>document.getElementById(id), v=$('video'), overlay=$('overlay');
const send=(type,data={})=>chrome.webview.postMessage({type,...data});
window.addEventListener('dragover',e=>{e.preventDefault();e.dataTransfer.dropEffect='copy'});
window.addEventListener('drop',e=>{e.preventDefault();if(!editing && e.dataTransfer.files.length)chrome.webview.postMessageWithAdditionalObjects({type:'drop'},Array.from(e.dataTransfer.files))});
let imageBase="",temporary=false,doc=null,frames=[],shown=0,selected=null,editing=null,inUs=null,loopId=null,history=[],future=[],lastEditGroup=null,tool=null,anchor=null,busy=false,ready=false;
const imageDoc=()=>doc?.version===2;
const fmt=us=>{const ms=Math.floor((us+500)/1000);return String(Math.floor(ms/60000)).padStart(2,'0')+':'+String(Math.floor(ms/1000)%60).padStart(2,'0')+'.'+String(ms%1000).padStart(3,'0')};
let errorNoticeTimer;
function status(text,error=false){
 if(!text)return;
 if(error)text=tr(text);
 const notice=$('status');
 send('diagnostic',{message:text,error});
 clearTimeout(errorNoticeTimer);
 notice.textContent=text;
 notice.classList.toggle('error',error);
 notice.classList.toggle('actionName',!error);
 if(error)errorNoticeTimer=setTimeout(()=>notice.classList.add('actionName'),8000);
}
window.addEventListener('error',e=>send('diagnostic',{message:e.error?.stack||e.message,error:true}));
window.addEventListener('unhandledrejection',e=>send('diagnostic',{message:e.reason?.stack||String(e.reason),error:true}));
function index(us){let lo=0,hi=frames.length;while(lo<hi){const mid=(lo+hi)>>>1;if(frames[mid]<=us+2)lo=mid+1;else hi=mid}return Math.max(0,lo-1)}
function seekFrame(i){transport.currentTime=(frames[i]+(doc.timeline?0:((frames[i+1]??doc.durationUs)-frames[i])*.5))/1e6}
function seekUs(us,pause=true){if(!ready)return;scrubber.reset();if(pause)transport.pause();seekFrame(index(us))}
function showPlayhead(us){$('clock').textContent=fmt(us);$('seek').value=us;$('timeline').style.setProperty('--position',100*us/(doc?.durationUs||1)+'%')}
function stamp(){return frames[index(shown)]}
function annotationTime(){return imageDoc()?0:Math.min(doc.durationUs-1,doc.timeline?Math.round(transport.currentTime*1e6):stamp())}
function canAnnotate(){return !!(imageDoc()||ready)&&!busy&&!imagePending&&!window.voiceActive?.()}
function presentSourceFrame(us){
 shown=us;if(!scrubber.active())showPlayhead(shown);
 paintPlayback();window.syncNoteVoices?.();
 const n=loopId&&doc.notes.find(n=>n.id===loopId);
 if(n&&!transport.paused&&shown>=n.endUs)transport.currentTime=n.startUs/1e6;
}
function tick(_,m){
 if(ready&&!doc?.timeline){
  const pts=Math.round(m.mediaTime*1e6);
  // A presentation callback queued before a paused seek can arrive after it finishes.
  if(!v.paused||(!v.seeking&&index(pts)===index(v.currentTime*1e6)))presentSourceFrame(pts);
 }else if(ready&&doc?.timeline&&transport.paused)refreshLiveMagnifiers();
 v.requestVideoFrameCallback(tick);
}
v.requestVideoFrameCallback(tick);
v.addEventListener('seeked',()=>{
 if(ready&&!doc?.timeline&&v.paused&&!v.seeking&&frames.length)presentSourceFrame(frames[index(v.currentTime*1e6)]);
});
let playbackId=null,entryUs=0,reviewProxy=false,autoPlay=false;
v.onloadedmetadata=()=>{if(doc&&!imageDoc()&&entryUs>0&&frames.length){seekFrame(index(entryUs));entryUs=0}};
v.onloadeddata=()=>{
 if(!doc||imageDoc())return;
 ready=true;
 if(!doc.timeline&&frames.length)presentSourceFrame(frames[index(v.currentTime*1e6)]);
 if(autoPlay){autoPlay=false;transport.play().catch(e=>{if(e.name!=='AbortError')status(tr('请点击播放：')+e.message,true)})}
 refreshLoadedMeasurements();layout();render();status(temporary?tr("临时录屏：切换视频或退出后会清理。要保留视频和批注，请导出审阅包。"):doc.fileName+(reviewProxy?tr(' · 临时审阅预览'):''));
};
v.onerror=()=>{if(!doc||imageDoc()||!v.getAttribute('src')||v.error?.code===1)return;const us=scrubber.position()??stamp()??0;scrubber.reset();ready=false;render();if(playbackId&&!reviewProxy)send('decodeError',{playbackId,us});else status(tr('无法解码审阅预览。可使用 VLC 播放原片。'),true)};
function setIconAction(id,icon,label,hint){
 const button=$(id);
 if(button.ariaDescription===hint)return;
 if(icon)button.querySelector('use').setAttribute('href','#icon-'+icon);
 button.querySelector('.actionName').textContent=label;
 button.ariaDescription=hint;
}
v.onplay=v.onpause=()=>{const label=transport.paused?tr('播放'):tr('暂停');setIconAction('play',transport.paused?'play':'pause',label,shortcutHint(label,'play'));paint()};
v.onended=()=>{if(doc?.timeline)return;const n=doc?.notes.find(n=>n.id===loopId);if(n){transport.currentTime=n.startUs/1e6;transport.play().catch(e=>status(e.message,true))}};
function layout(){if(imageDoc())return;const w=v.clientWidth,h=v.clientHeight,k=Math.min(w/(v.videoWidth||1),h/(v.videoHeight||1)),vw=v.videoWidth*k,vh=v.videoHeight*k;
 Object.assign(overlay.style,{width:vw+'px',height:vh+'px',left:(w-vw)/2+'px',top:(h-vh)/2+'px'});paint(Math.round(vw),Math.round(vh))}
new ResizeObserver(layout).observe($('stage'));
const videoZoom=(()=>{
 const viewport=$('mediaViewport'),stage=$('stage');
 let scale=1,x=0,y=0;
 function reset(){scale=1;x=y=0;viewport.style.transform=''}
 stage.addEventListener('wheel',e=>{
  const delta=e.deltaY||e.deltaX;
  if(!ready||imageDoc()||busy||!bound('zoomVideo',e,256)||e.target.closest('button')||!delta)return;
  e.preventDefault();
  const r=stage.getBoundingClientRect(),unit=e.deltaMode===1?16:e.deltaMode===2?r.height:1;
  const next=clamp(scale*2**(-delta*unit/480),.25,4);
  if(next===scale)return;
  const ratio=next/scale,min=Math.min(0,1-next),max=Math.max(0,1-next);
  const cx=(e.clientX-r.left)/r.width,cy=(e.clientY-r.top)/r.height;
  x=clamp(cx+(x-cx)*ratio,min,max);y=clamp(cy+(y-cy)*ratio,min,max);scale=next;
  viewport.style.transform='translate('+x*100+'%,'+y*100+'%) scale('+scale+')';
  resetPreview();scheduleMeasurementPreview();
 },{passive:false,capture:true});
 return {reset};
})();
const palette=[[tr('琥珀'),'#ffd886'],[tr('天蓝'),'#8ddcff'],[tr('薄荷'),'#8de3af'],[tr('珊瑚'),'#ff9d97'],[tr('紫藤'),'#cfb1ff'],[tr('粉红'),'#ffaed8']];
const noteColor=n=>n.color??palette[(n.number-1)%palette.length][1];
function badgeInk(color){
 const linear=byte=>{const s=byte/255;return s<=.04045?s/12.92:((s+.055)/1.055)**2.4};
 const rgb=parseInt(color.slice(1),16);
 const luminance=.2126*linear(rgb>>16)+.7152*linear((rgb>>8)&255)+.0722*linear(rgb&255);
 return 1.05/(luminance+.05)>(luminance+.05)/.05?'#fff':'#000';
}
function setNoteStyle(el,color,property='--note'){
 el.style.setProperty(property,color);el.style.setProperty('--badge-ink',badgeInk(color));
}
function applyNoteColor(id,color,group=null){
 if(!canAnnotate())return;
 const note=editing?.id===id?editing:doc.notes.find(n=>n.id===id);
 if(!note||noteColor(note).toLowerCase()===color)return;
 if(note===editing){editing.color=color;persistEdit(group)}
 else commit(doc.notes.map(n=>n.id===id?{...n,color}:n),id,group);
}
function openNoteColorPicker(note,bounds){
 if(!note||!canAnnotate())return;
 const picker=$('noteColorPicker'),review=doc,id=note.id;
 picker.value=noteColor(note);
 picker.style.left=bounds.left+'px';picker.style.top=bounds.top+'px';
 picker.style.width=bounds.width+'px';picker.style.height=bounds.height+'px';
 // Keep a continuous picker gesture in one undo step, even across detail redraws.
 lastEditGroup=null;
 picker.oninput=picker.onchange=()=>{if(doc===review)applyNoteColor(id,picker.value,'color')};
 // WebView2 positions its native popup from the last laid-out input bounds.
 picker.getBoundingClientRect();
 picker.showPicker();
}
for(const [name,color] of palette){
 const b=document.createElement('button');b.type='button';b.title=name;b.setAttribute('aria-label',name);b.dataset.color=color;
 b.style.setProperty('--note',color);b.onclick=()=>applyNoteColor(detailNote()?.id,color);$('customColor').before(b);
}
$('customColor').onclick=e=>openNoteColorPicker(detailNote(),e.currentTarget.getBoundingClientRect());
function updateColors(note,container,attribute){
 const color=noteColor(note).toLowerCase();
 for(const b of container.querySelectorAll('[data-color]'))b.setAttribute(attribute,String(b.dataset.color===color));
 const custom=container.querySelector('.customColor');
 custom.setAttribute(attribute,String(!palette.some(([,c])=>c===color)));setNoteStyle(custom,color,'--custom');
}
const noteStamp=n=>imageDoc()?'':fmt(n.startUs)+(n.endUs===null?'':' – '+fmt(n.endUs));
const pointSuffix=n=>!imageDoc() && n.endUs===null?tr('　点'):'';
const noteDescription=n=>n.text||(n.voice?tr('语音批注'):shapeNames[n.shape?.kind]||(n.pictures?.length?tr('图片批注'):tr('待填写批注')));
const notesByTime=notes=>[...notes].sort((a,b)=>a.startUs-b.startUs||a.number-b.number);
const notePreview=$('notePreview');
let hovered=null,hoverBadge=null,inkKey='',rows=new Map(),hidePreviewTimer,previewPointer=null;
function placePreview(noteSelector,pointer){
 const stage=$('stage').getBoundingClientRect(),group=$('ink').querySelector(':scope > g'+noteSelector);
 const hit=document.elementFromPoint(pointer.clientX,pointer.clientY);
 const drawing=hit?.hasAttribute('data-lens-preview')?group.querySelector('.inkShape[data-lens-preview]'):group.querySelector('.inkShape')??group.querySelector('.picture');
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
 setNoteStyle(notePreview,noteColor(note));notePreview.hidden=false;body.scrollTop=0;
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
function paint(w=overlay.clientWidth,h=overlay.clientHeight){
 const g=$('ink');
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
function drawInk(g,visible,w,h,measurement){
 const badges=[],labels=svg('g',{class:'inkLabels'});
 for(const n of visible){
  const s=n.shape,c=noteColor(n),group=svg('g',{'data-note':n.id,color:c});
  let x=4,y=4;
  if(n.pictures?.length){const p=n.pictures[0];x=p.x*w;y=p.y*h-22;drawPictures(group,n,w,h)}
  if(s){const drawing=svg('g',{'data-shape':''});group.append(drawing);drawShape(drawing,n,w,h,measurement);x=Math.min(s.x,s.toX)*w+3;y=Math.min(s.y,s.toY)*h-22;}
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
  const text=svg('text',{x:bw/2,y:13.5,fill:badgeInk(c),'text-anchor':'middle','font-weight':700,'font-size':13});text.textContent='#'+n.number;tag.append(text);labels.append(tag);
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
let measurementPoints=[],measurementCursor=null,measurementFrame=0,strokePointer=null,textClick=null;
const measurementPreview=$('measurementPreview');
function samePoint(a,b){return a[0]===b[0]&&a[1]===b[1]}
function measurementShape(cursor){
 const [[x,y],vertex]=measurementPoints;
 const [toX,toY]=vertex??cursor??measurementPoints[0];
 if(tool==='ruler')return {kind:'ruler',x,y,toX,toY,thirdX:null,thirdY:null,rulerFrame:transport.sourceFrameAt(strokeNote.startUs)};
 if(!vertex||!cursor||samePoint(vertex,cursor))return {kind:'line',x,y,toX,toY};
 return {kind:'angle',x,y,toX,toY,thirdX:cursor[0],thirdY:cursor[1]};
}
function drawMeasurementPreview(){
 if(!measurementPoints.length)return;
 const w=overlay.clientWidth,h=overlay.clientHeight;if(!w||!h)return;
 const shape=measurementShape(measurementCursor&&pos(measurementCursor)),layer=document.createDocumentFragment();
 drawShape(layer,{...strokeNote,shape},w,h);
 for(const [x,y] of measurementPoints)layer.append(svg('circle',{cx:x*w,cy:y*h,r:4.5,fill:noteColor(strokeNote),stroke:'#17191d','stroke-width':1.5}));
 measurementPreview.replaceChildren(layer);
}
function scheduleMeasurementPreview(){
 if(measurementPoints.length&&!measurementFrame)measurementFrame=requestAnimationFrame(()=>{measurementFrame=0;drawMeasurementPreview()});
}
new ResizeObserver(scheduleMeasurementPreview).observe(overlay);
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
 if(note.shape.kind==='ruler'&&!doc.rulerScales?.[note.shape.rulerFrame]&&!isFullPreview())$('rulerLength').focus({preventScroll:true});
}
function captureStroke(e){
 strokePointer=e.pointerId;overlay.setPointerCapture(e.pointerId);
}
function releaseStrokePointer(){
 const pointer=strokePointer;strokePointer=null;
 if(pointer!==null&&overlay.hasPointerCapture(pointer))overlay.releasePointerCapture(pointer);
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
 if(tool==='angle'||tool==='ruler'){
  const point=pos(e);
  if(measurementPoints.length&&samePoint(measurementPoints.at(-1),point))return;
  if(measurementPoints.length===(tool==='ruler'?1:2)){strokeNote.shape=measurementShape(point);finishStroke();return}
  measurementPoints.push(point);measurementCursor=null;drawMeasurementPreview();
  if(tool==='ruler')captureStroke(e);
  status(tool==='ruler'?tr('点击终点或拖至终点松手'):measurementPoints.length===1?tr('点击角度顶点'):tr('点击第二条边的端点'));
  return;
 }
 anchor=pos(e);captureStroke(e);
};
overlay.onpointermove=e=>{
 if(measurementPoints.length&&e.isPrimary){measurementCursor={clientX:e.clientX,clientY:e.clientY};scheduleMeasurementPreview();return}
 if(e.pointerId!==strokePointer||!anchor||!strokeNote)return;
 const [toX,toY]=pos(e);strokeNote.shape={kind:tool,x:anchor[0],y:anchor[1],toX,toY,thirdX:null,thirdY:null};
 if(tool==='magnifier')placeMagnifier(strokeNote.shape,overlay.clientWidth,overlay.clientHeight);
 paint();
};
overlay.addEventListener('pointerleave',()=>{if(measurementCursor){measurementCursor=null;scheduleMeasurementPreview()}});
overlay.onpointerup=e=>{
 if(!strokeNote||e.pointerId!==strokePointer)return;
 if(tool==='ruler'){
  const end=pos(e),[x,y]=measurementPoints[0];
  if(Math.hypot((end[0]-x)*overlay.clientWidth,(end[1]-y)*overlay.clientHeight)<2){
   releaseStrokePointer();status(tr('移动鼠标预览长度，点击终点固定标尺'));return;
  }
  strokeNote.shape=measurementShape(end);finishStroke();return;
 }
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
 if(s.kind==='magnifier'&&(s.toX===s.x||s.toY===s.y)){setTool(null);return}
 if(Math.hypot((s.toX-s.x)*overlay.clientWidth,(s.toY-s.y)*overlay.clientHeight)<2){setTool(null);return}
 finishStroke();
};
overlay.onpointercancel=()=>setTool(null);
overlay.addEventListener('lostpointercapture',e=>{if(e.target===overlay&&strokePointer===e.pointerId)setTool(null)});
window.addEventListener('blur',()=>{if(strokePointer!==null||measurementPoints.length)setTool(null)});
function setTool(t){
 const changed=tool!==t;
 if(t)highlight(null);
 if(imageDoc()&&['ruler','magnifier'].includes(t))imageView.ensureOriginal();
 if(measurementPoints.length){cancelAnimationFrame(measurementFrame);measurementFrame=0;measurementPoints=[];measurementCursor=null;measurementPreview.replaceChildren()}
 tool=t;strokeNote=null;anchor=null;textClick=null;quickMods=0;
 releaseStrokePointer();
 overlay.classList.toggle('drawing',!!t);
 for(const kind of Object.keys(shapeNames)){
  const button=$(kind==='ruler'?'toolRuler':kind);
  button.classList.toggle('active',kind===t);
  button.setAttribute('aria-pressed',String(kind===t));
 }
 if(t)status(t==='angle'?tr('依次点击第一条边端点、顶点、第二条边端点'):t==='ruler'?tr('依次点击标尺起点和终点，或按住并拖动'):t==='text'?tr('拖出文字框，内容使用当前批注文字'):t==='magnifier'?tr('框选要放大的内容，松开后可分别拖动取景框和放大预览'):tr('在画面上按住并拖动以绘制标记'));
 if(changed)paint();
}
let rulerFieldKey='',defaultRulerUnit='m';
function setDefaultRulerUnit(unit){
 if(unit===defaultRulerUnit||!Object.hasOwn(metresPerUnit,unit))return;
 const n=editing??doc?.notes.find(n=>n.id===selected);
 if(n?.shape?.kind==='ruler'&&!doc.rulerScales?.[n.shape.rulerFrame]&&$('rulerUnit').value===defaultRulerUnit)$('rulerUnit').value=unit;
 defaultRulerUnit=unit;
 if(!doc)return;
 inkKey=null;
 refreshLoadedMeasurements();render();scheduleMeasurementPreview();
}
function refreshRulerFields(n){
 const s=n?.shape,isRuler=s?.kind==='ruler';
 $('rulerSettings').hidden=!isRuler;
 if(!isRuler){rulerFieldKey='';return}
 const scale=doc.rulerScales?.[s.rulerFrame],value=scale?rulerValue(s,scale):'';
 const key=[n.id,value,scale?.unit].join(':');
 if(key===rulerFieldKey)return;
 rulerFieldKey=key;$('rulerLength').value=value;$('rulerUnit').value=scale?.unit??defaultRulerUnit;
}
function calibrateRuler(n,length,unit){
 if(n?.shape?.kind!=='ruler')return;
 const [w,h]=sourceSize();
 const aspect=h/w,span=rulerDistance(n.shape,aspect),factor=length*metresPerUnit[unit]/span;
 if(!(factor>0&&Number.isFinite(factor)&&span>0)){status(tr('请输入大于零的已知长度，并拉开标尺两端。'),true);return}
 const frame=n.shape.rulerFrame??transport.sourceFrameAt(n.startUs);
 const notes=doc.notes.map(note=>note.shape?.kind==='ruler'&&note.shape.rulerFrame==null&&transport.sourceFrameAt(note.startUs)===frame?
  {...note,shape:{...note.shape,rulerFrame:frame}}:note);
 const scales={...doc.rulerScales,[frame]:{metresPerWidth:factor,aspect,unit}};
 if(editing?.id===n.id){
  editing.shape.rulerFrame=frame;editing=withMeasurementText(editing,scales);
  if(editing.autoMeasurementText)$('body').value=editing.text;
 }
 inkKey=null;
 commit(notes.map(note=>withMeasurementText(note,scales)),n.id,null,null,scales);
 status(tr('本帧标尺已标定'));return true;
}
function applyAngle(n,degrees){
 if(n?.shape?.kind!=='angle')return;
 const shape=shapeWithAngle(n.shape,degrees,...sourceSize());
 if(!shape){status(tr('请确认角度在 0° 到 180° 之间，并将顶点或重合的端点移开后重试。'),true);return false}
 if(Math.abs(shape.thirdX-n.shape.thirdX)<1e-12&&Math.abs(shape.thirdY-n.shape.thirdY)<1e-12)return true;
 const updated=withMeasurementText({...n,shape},doc.rulerScales);
 if(editing?.id===n.id){editing=structuredClone(updated);$('body').value=updated.text}
 commit(doc.notes.map(note=>note.id===n.id?updated:note),n.id);
 status(tr('角度已更新'));return true;
}
function detailNote(){return editing??doc?.notes.find(n=>n.id===selected)}
$('calibrateRuler').onclick=()=>calibrateRuler(detailNote(),$('rulerLength').valueAsNumber,$('rulerUnit').value);
$('applyAngle').onclick=()=>{if($('angleDegrees').reportValidity())applyAngle(detailNote(),$('angleDegrees').valueAsNumber)};
for(const [field,button] of [['rulerLength','calibrateRuler'],['angleDegrees','applyAngle']]){
 $(field).onkeydown=e=>{if(e.key==='Enter'&&!e.isComposing){e.preventDefault();e.stopPropagation();$(button).click()}};
}
let angleFieldKey='';
function refreshDetails(){
 window.refreshVoice?.();
 const n=detailNote();
 refreshRulerFields(n);
 const angle=n?.shape?.kind==='angle'?angleGeometry(n.shape,...sourceSize()).label:null;
 $('angleSettings').hidden=$('angleDegrees').disabled=!angle;
 const key=angle?n.id+angle:'';
 if(key!==angleFieldKey){angleFieldKey=key;$('angleDegrees').value=angle?parseFloat(angle):''}
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
 updateColors(n,$('colors'),'aria-pressed');
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
 const input=$('body').value.trim();
 if(group==='text'||input!==editing.text){editing.text=input;delete editing.autoMeasurementText}
 const measured=withMeasurementText(editing,doc.rulerScales);
 if(group!=='text'&&(measured!==editing||measured.autoMeasurementText))$('body').value=measured.text;
 editing=measured;
 const n=structuredClone(editing);
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
 if(!editing&&e.target.closest('#detailFields,#clear,#removePicture'))begin(doc?.notes.find(x=>x.id===selected),false);
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
 if(doc.notes.length>=10000){status(tr('本审阅已达到 10000 条批注上限。'),true);return null}
 if(doc.lastNoteNumber>=2147483647){status(tr('本审阅的批注编号已用尽。'),true);return null}
 return {id:crypto.randomUUID(),number:doc.lastNoteNumber+1,color:palette[doc.lastNoteNumber%palette.length][1],pictures:null,startUs:a,endUs:b,text:'',shape:null,voice:null};
}
function newNote(a,b){finishEdit();const n=blankNote(a,b);if(!n)return;begin(n);persistEdit();rows.get(n.id)?.scrollIntoView({block:'nearest'})}
function duplicateNote(id){
 if(!id||!canAnnotate()||tool||drag||geometryDrag)return;
 finishEdit();
 const note=doc.notes.find(n=>n.id===id);if(!note)return;
 const fresh=blankNote(note.startUs,note.endUs);if(!fresh)return;
 const copy={...structuredClone(note),id:fresh.id,number:fresh.number,color:noteColor(note)};
 commit([...doc.notes,copy],copy.id,null,id);
}
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
function sendReviewNotes(){
 send('notes',{documentId:doc.id,notes:doc.notes,timeline:doc.timeline??null,rulerScales:doc.rulerScales??null,durationUs:doc.durationUs});
}
function refreshLoadedMeasurements(){
 if(busy||!(ready||imageDoc()))return;
 if(editing?.autoMeasurementText){
  const measured=withMeasurementText(editing,doc.rulerScales);
  if(measured!==editing){editing=measured;$('body').value=editing.text}
 }
 const notes=doc.notes.map(n=>withMeasurementText(n,doc.rulerScales));
 if(notes.every((n,i)=>n===doc.notes[i]))return;
 doc.notes=notes;sendReviewNotes();return true;
}
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
  if(doc.timeline.trimmed?.length){
   doc.timeline.trimmed=doc.timeline.trimmed.flatMap(entry=>{
    const id=entry.note?.id??entry.voice?.noteId;
    if(!before.has(id))return [entry];
    const n=after.get(id);
    if(!n||entry.voice&&n.voice?.asset!==entry.voice.asset)return [];
    // A partially trimmed Region is still one note when its text, color or attachments change.
    if(entry.note)return [{...entry,note:{...n,startUs:entry.note.startUs,endUs:entry.note.endUs}}];
    return [entry];
   });
  }
 }
 doc.notes=notes;selected=id;doc.lastNoteNumber=notes.reduce((max,n)=>Math.max(max,n.number),doc.lastNoteNumber);
 if(loopId&&!notes.some(n=>n.id===loopId))loopId=null;
 sendReviewNotes();render();
}
function commit(notes,id,group=null,copyFrom=null,scales=doc.rulerScales){
 const key=group&&id+':'+group;
 if(!key||key!==lastEditGroup){history.push(snapshotNotes());if(history.length>80)history.shift()}
 doc.rulerScales=scales;lastEditGroup=key;future=[];applyNotes(notes,id,copyFrom);
}
function moveHistory(redo){
 if(!canAnnotate())return;
 const from=redo?future:history,to=redo?history:future;
 if(from.length){
  closeEditor();to.push(snapshotNotes());const state=from.pop();loopId=null;
  applyEditState(state);
 }
 if(document.activeElement===$('body'))$('body').blur();
}
let reportedUndo=false,reportedRedo=false;
function syncHistoryButtons(){
 const available=canAnnotate(),undo=available&&history.length>0,redo=available&&future.length>0;
 if(undo===reportedUndo&&redo===reportedRedo)return;
 reportedUndo=undo;reportedRedo=redo;send('historyState',{undo,redo});
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
  status(tr('已复制批注 #{0} 的文字',note.number));
 }catch{status(tr('未能写入剪贴板，请重试。'),true)}
}
function appendTimelineMark(n){
 const m=document.createElement('button'),label=document.createElement('span');m.className=n.endUs===null?'point':'region';
 setNoteStyle(m,noteColor(n),'--mark');m.dataset.start=n.startUs/doc.durationUs;m.dataset.end=(n.endUs??n.startUs)/doc.durationUs;
 label.className='markLabel';label.textContent='#'+n.number;m.append(label);m.title=label.textContent+' '+noteStamp(n)+pointSuffix(n)+' '+n.text;m.setAttribute('aria-label',m.title);
 m.dataset.track='note';m.dataset.id=n.id;
 if(!n.voice){addTrimHandles(m);m.title+=tr(' · 拖动两端调整批注时长')}
 m.setAttribute('aria-pressed',String(n.id===selected));m.disabled=!ready;
 $('markLanes').append(m);
}
let reportedCanOpen;
function render(){$('exportExcel').disabled=!ready||imageDoc()||!doc?.notes.length;$('exportVideo').disabled=$('exportGif').disabled=!ready||imageDoc();refreshDetails();const notes=doc?.notes??[];rows.clear();$('count').textContent=notes.length;$('list').replaceChildren();$('markLanes').replaceChildren();
 const ordered=notesByTime(notes);
 $('renumberNotes').disabled=!(ready||imageDoc())||ordered.every((n,i)=>n.number===i+1);
 for(const n of ordered){
 const b=document.createElement('div'),t=document.createElement('button');rows.set(n.id,b);setNoteStyle(b,noteColor(n));b.className='note'+(n.id===selected?' selected':'');b.classList.toggle('hovered',n.id===hovered);t.className='stamp';t.textContent=noteStamp(n);const badge=document.createElement('strong');badge.className='noteNumber';badge.textContent='#'+n.number;t.prepend(badge,' ');
 const description=noteBody(noteDescription(n));description.id='note-body-'+n.id;
 t.setAttribute('aria-describedby',description.id);
 if(n.id===selected)t.setAttribute('aria-current','true');
 b.append(t,description);
 b.onclick=e=>{
  select(n.id);
  if(e.detail===0)rows.get(n.id)?.querySelector('.stamp').focus({preventScroll:true});
 };
 const trash=noteActionButton('noteDelete','trash',tr('删除批注 #')+n.number,()=>deleteNote(n.id));
 const copy=noteActionButton('noteCopy','copy',tr('复制批注 #{0} 的文字',n.number),()=>copyNoteText(n));
 copy.disabled=!n.text?.trim();
 b.append(trash,copy);$('list').append(b);
 if(!imageDoc())appendTimelineMark(n)}
 layoutMarks();
 if((ready||imageDoc())&&!notes.length){const p=document.createElement('div');p.className='empty';p.textContent=tr('选择绘图工具，或右键画面新增批注。');$('list').append(p)}
 $('rangeHint').textContent=inUs===null?'':tr('起点 ')+fmt(inUs)+bindingText('outBtn',tr('　定位后按 '));
 for(const id of ['rate','fullPreview','mutePlayback','playbackVolume','play','prev','next','seek','point','inBtn'])$(id).disabled=!ready;
 $('copyImage').disabled=$('saveImage').disabled=!imageDoc();
 $('copyFrame').disabled=$('saveFrame').disabled=!ready;
 $('point').disabled=$('save').disabled=!(ready||imageDoc());$('open').disabled=false;$('outBtn').disabled=!ready||inUs===null;
 if(reportedCanOpen!==!$('open').disabled){reportedCanOpen=!$('open').disabled;send('canOpen',{value:reportedCanOpen})}
 syncHistoryButtons();
 $('vlc').disabled=!doc;
 const n=notes.find(x=>x.id===selected);$('loop').disabled=!n||n.endUs===null;window.captureButtons?.();$('loop').classList.toggle('active',!!loopId);$('loop').setAttribute('aria-pressed',String(!!loopId));paint();window.renderClips?.()}
$('exportExcel').onclick=()=>{finishEdit();transport.pause();send('exportExcel')};
$('exportVideo').onclick=()=>{finishEdit();transport.pause();send('exportVideo')};
const progressDialog=$('taskProgress');
function cancelProgress(){send('cancelPreview')}
$('cancelProgress').onclick=cancelProgress;
progressDialog.oncancel=e=>{e.preventDefault();cancelProgress()};
function showProgress(message){
 const dialog=progressDialog;
 if(!message.active){if(dialog.open)dialog.close();return}
 $('progressTitle').textContent=tr('正在生成批注预览');
 $('progressBar').value=message.percent;
 $('progressPercent').textContent=message.percent+'%';
 $('cancelProgress').textContent=tr('取消加载');
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
 if(busy||!ready||v.seeking)return status(tr('请等当前帧显示后再捕获。'));
 transport.pause();finishEdit();await new Promise(requestAnimationFrame);paint();
 send('snapshot',{save,playbackId,us:stamp(),ids:[...overlay.querySelectorAll('.inkBadge')].map(el=>el.dataset.note)});
}
$('copyFrame').onclick=()=>snapshot(false);$('saveFrame').onclick=()=>snapshot(true);
$('vlc').onclick=()=>{if(isFullPreview())send('previewFullscreen',{on:false});transport.pause();send('vlc',{playbackId,us:stamp()??0})};
$('play').onclick=()=>{if(transport.paused)transport.play().catch(e=>status(e.message,true));else transport.pause()};
$('prev').onclick=()=>seekUs(frames[Math.max(0,index(shown)-1)]);$('next').onclick=()=>seekUs(frames[Math.min(frames.length-1,index(shown)+1)]);
function clearPlaybackLoop(){if(loopId){loopId=null;render()}}
$('rate').onchange=()=>{v.defaultPlaybackRate=transport.playbackRate=Number($('rate').value)};
function adjustPlaybackRate(action){
 if(!action||!ready||busy||window.voiceActive?.()||progressDialog.open||imageDoc())return;
 const rate=$('rate');
 if(action==='rateReset')rate.value='1';
 else rate.selectedIndex=Math.max(0,Math.min(rate.length-1,rate.selectedIndex+(action==='rateUp'?1:-1)));
 rate.onchange();
}
let lastAudible=100;
function displayVolume(value){
 v.volume=value/100;$('playbackVolume').value=value;
 if(value>0)lastAudible=value;
 setIconAction('mutePlayback',value?'speaker_2':'speaker_mute',value?tr('静音'):tr('取消静音'),value?tr('播放静音'):tr('取消播放静音'));
 $('mutePlayback').setAttribute('aria-pressed',String(value===0));
}
let volumePercentTimer;
function hideVolumePercent(){
 clearTimeout(volumePercentTimer);$('volumePercent').hidden=true;
}
function changeVolume(value){
 if(value===Math.round(v.volume*100))return;
 displayVolume(value);send('playbackVolume',{value});
 const label=$('volumePercent');
 label.textContent=value+'%';label.hidden=false;
 clearTimeout(volumePercentTimer);volumePercentTimer=setTimeout(hideVolumePercent,900);
}
$('volumeControl').addEventListener('pointerleave',()=>{if(!$('playbackVolume').matches(':active'))hideVolumePercent()});
window.addEventListener('blur',hideVolumePercent);
$('playbackVolume').oninput=e=>changeVolume(Number(e.target.value));
$('mutePlayback').onclick=()=>changeVolume(Number($('playbackVolume').value)?0:lastAudible);
let volumeWheelRemainder=0;
$('volumeControl').addEventListener('wheel',e=>{
 const slider=$('playbackVolume');
 if(slider.disabled||!e.deltaY)return;
 e.preventDefault();
 // WebView2 reports 120 wheel units per detent, including fractional touchpad input.
 const delta=e.wheelDeltaY||-e.deltaY*(e.deltaMode===1?40:e.deltaMode===2?120:1.2);
 volumeWheelRemainder+=delta*5/120;
 const step=Math.trunc(volumeWheelRemainder);
 volumeWheelRemainder-=step;
 const value=Math.max(0,Math.min(100,Number(slider.value)+step));
 if(value!==Number(slider.value))changeVolume(value);
},{passive:false});
const isFullPreview=()=>document.body.classList.contains('previewFullscreen');
$('fullPreview').onclick=()=>send('previewFullscreen',{on:!isFullPreview()});
function showFullPreview(on){document.body.classList.toggle('previewFullscreen',on);setIconAction('fullPreview',on?'full_screen_minimize':'full_screen_maximize',on?tr('退出全屏'):tr('全屏预览'),shortcutHint(on?tr('退出全屏'):tr('全屏预览'),'fullPreview'));(focusAfterFullscreen&&!on&&editing?$('body'):$('fullPreview')).focus({preventScroll:true});focusAfterFullscreen=false;layout()}
// Let each decoder seek finish. New pointer samples replace the pending target,
// so the decoder can present intermediate frames even under high-rate mouse input.
const scrubber=(()=>{
 let target=null,issued=null,raf=0,pointer=null,resume=false;
 function reset(){cancelAnimationFrame(raf);raf=0;target=issued=pointer=null;resume=false}
 function schedule(){if(!raf)raf=requestAnimationFrame(pump)}
 function pump(){
  raf=0;if(target===null||!ready)return;
  if(v.seeking)return; // seeked will schedule the next presentation.
  if(issued!==target){issued=target;seekFrame(issued);schedule();return}
  if(pointer===null){
   const play=resume;reset();
   if(play)transport.play().catch(e=>{if(e.name!=='AbortError')status(e.message,true)});
  }
 }
 v.addEventListener('seeked',()=>{if(target!==null)schedule()});
 function begin(e){
  if(!ready||e.button||pointer!==null)return;
  pointer=e.pointerId;resume=resume||!transport.paused;transport.pause();
 }
 function end(e){if(pointer!==e.pointerId)return;pointer=null;if(target!==null)schedule();else{const play=resume;reset();if(play)transport.play().catch(()=>{})}}
 for(const event of ['pointerup','pointercancel','lostpointercapture'])window.addEventListener(event,end,true);
 window.addEventListener('blur',()=>{if(pointer!==null)end({pointerId:pointer})});
 v.addEventListener('emptied',reset);
 return {begin,reset,active:()=>target!==null,position:()=>target===null?null:frames[target],
  seek(us){if(!ready)return;target=index(us);showPlayhead(frames[target]);schedule()}};
})();
function seekTimeline(us){clearPlaybackLoop();scrubber.seek(us)}
$('seek').addEventListener('pointerdown',scrubber.begin);
$('seek').oninput=e=>seekTimeline(+e.target.value);
$('point').onclick=()=>newNote(annotationTime(),null);$('inBtn').onclick=()=>{transport.pause();inUs=stamp();render()};
$('outBtn').onclick=()=>{const end=stamp();if(end<=inUs)return status(tr('终点必须晚于起点。'),true);const a=inUs;inUs=null;newNote(a,end)};
$('loop').onclick=()=>{const n=doc.notes.find(x=>x.id===selected);if(loopId){loopId=null;transport.pause()}else{loopId=n.id;transport.currentTime=n.startUs/1e6;transport.play().catch(e=>status(e.message,true))}render()};
$('noteStart').onchange=()=>{
 if(!editing||imageDoc())return;
 const input=$('noteStart'),at=Math.round(input.valueAsNumber*1e6),delta=at-editing.startUs;
 const outside=doc.timeline?.clips.some(c=>c.noteId===editing.id&&(c.startUs+delta<0||c.startUs+delta+c.durationUs>doc.durationUs));
 if(!input.validity.valid||!Number.isFinite(at)||outside){input.value=editing.startUs/1e6;status(tr('批注及关联语音必须位于时间线内。'),true);return}
 editing.startUs=at;if(editing.endUs!==null)editing.endUs+=delta;
 persistEdit();seekUs(at);
};
$('noteDuration').onchange=()=>{
 if(!editing||imageDoc())return;
 const value=$('noteDuration').value,us=Math.round(Number(value)*1e6),max=doc.durationUs-editing.startUs;
 if(value===''||!Number.isFinite(us)||us<0||us>max||!$('noteDuration').validity.valid){status(tr('时长须在 0 与剩余视频时长之间。'),true);$('noteDuration').value=editing.endUs===null?0:(editing.endUs-editing.startUs)/1e6;return}
 editing.endUs=us>0?editing.startUs+us:null;persistEdit();refreshDetails();
};
$('editor').onsubmit=e=>{e.preventDefault();finishEdit();document.activeElement?.blur()};
for(const k of Object.keys(shapeNames))$(k==='ruler'?'toolRuler':k).onclick=()=>startDrawing(k);
$('clear').onclick=()=>{if(editing){editing.shape=null;setTool(null);persistEdit();paint()}};
const playlistToggle=$('playlistToggle');
const narrowWindow=matchMedia('(max-width:1100px)');
function setCaptureLayout(){$('capturePanel').popover=narrowWindow.matches?'auto':null}
setCaptureLayout();narrowWindow.addEventListener('change',setCaptureLayout);
const alignClips=$('alignClips');
new ResizeObserver(([entry])=>{
 const mode=entry.contentRect.width<=730?'auto':null;
 if(alignClips.popover!==mode)alignClips.popover=mode;
}).observe($('viewer'));
alignClips.addEventListener('click',e=>{
 if(e.target.closest('button') && alignClips.matches(':popover-open'))alignClips.hidePopover();
});
playlistToggle.onclick=()=>send('togglePlaylist');
document.onkeydown=e=>{
 if(e.isComposing||e.target.closest('option,select:is(:open)'))return;
 if(e.key==='Escape'&&(alignClips.matches(':popover-open')||$('capturePanel').matches(':popover-open')))return;
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
 if(!mods && (e.key==='Enter'||e.key===' ') && e.target.closest('button,a[href],select,input[type=checkbox]'))return;
 if(e.repeat && !typing && ready && is('fullPreview') && !e.target.closest('select,#capturePanel,#canvasMenu')){e.preventDefault();return}
 if(busy||e.repeat||e.target.closest('#capturePanel'))return;
 if(!playlistToggle.hidden && e.code==='KeyL' && keyMods(e)===1){e.preventDefault();playlistToggle.click();return;}
 if(doc&&(is('undo')||is('redo'))){
  e.preventDefault();moveHistory(is('redo'));return;
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
 if(typing||e.target.closest('#canvasMenu,select:not(#rate)')||document.querySelector('dialog[open]'))return;
 if(e.keyCode===86&&keyMods(e)===2&&!is('paste')&&!Object.values(window.shortcutBindings).some(action=>action.keys.some(b=>b.vk===86&&b.mods===2))){
  e.preventDefault();return;
 }
 if(ready&&!imageDoc()&&!window.voiceActive?.()){
  if(speedKeys.down(e)){e.preventDefault();return}
  if(is('fullPreview')&&!e.target.closest('select')){e.preventDefault();$('fullPreview').click();return}
  if(is('copyFrame')){
   if(e.ctrlKey&&e.keyCode===67&&!window.getSelection()?.isCollapsed)return;
   e.preventDefault();$('copyFrame').click();return;
  }
 }
 if(e.target.closest('select,#playbackVolume'))return;
 if(isFullPreview()&&!['play','prev','next'].some(is))return;
 if(is('open')){e.preventDefault();$('open').click();return}
 if(imageDoc()&&(is('prev')||is('next'))){e.preventDefault();imageView.adjacent(is('prev')?-1:1);return}
 if(!(ready||imageDoc()))return;
 if(is('duplicateNote')){e.preventDefault();duplicateNote(selected);return}
 if(ready&&!imageDoc()&&is('linkTracks')){e.preventDefault();if(!drag&&!geometryDrag&&!tool&&!window.voiceActive?.())$('linkTracks').click();return}
 if(ready&&is('picture')){e.preventDefault();pickPicture();return}
 if(ready&&is('deleteClip')){
  if(!drag&&!geometryDrag&&!window.voiceActive?.()&&!$('deleteClip').disabled){e.preventDefault();$('deleteClip').click()}
  return;
 }
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
 if(m.type==='moveHistory'){if(doc&&m.documentId===doc.id)moveHistory(m.redo);return}
 if(m.type==='playlistState'){playlistToggle.hidden=!m.visible;playlistToggle.setAttribute('aria-expanded',String(m.expanded));return;}
 if(m.type==='fullImage'){if(m.id===doc?.id)imageView.upgrade(m.src);return;}
 if(m.type==='loaded'&&m.recovering&&m.doc.id===doc?.id){
  transport.pause();ready=autoPlay=false;playbackId=m.playbackId;reviewProxy=m.proxy;
  entryUs=m.us;v.src=m.src;render();return;
 }
 if(m.type==='image'||m.type==='loaded'){videoZoom.reset();speedKeys.clear()}
 if(m.type==='image'){
  doc=m.doc;imageBase=m.images;$('imageToolbar').append($('point'),$('annotationTools'));refreshPointButton();ready=autoPlay=temporary=false;entryUs=0;frames=[];shown=0;playbackId=inUs=selected=loopId=null;history=[];future=[];
  transport.pause();v.removeAttribute('src');v.load();closeEditor();$('welcome').hidden=true;document.body.classList.add('imageView');
  originalImageSize=m.originalSize??null;imageView.show(m.src,m.folder,m.scaled);refreshLoadedMeasurements();render();status(m.name);return;
 }
 if(m.type==='loaded'){transport.pause();$('inBtn').before($('point'));$('editTools').before($('annotationTools'));imageView.reset();document.body.classList.remove('imageView');$('openedImage').hidden=true;$('openedImage').removeAttribute('src');autoPlay=m.autoPlay;displayVolume(m.volume);$('duration').textContent=fmt(m.doc.durationUs);$('rate').value='1';v.defaultPlaybackRate=transport.playbackRate=1;playbackId=m.playbackId;entryUs=m.us;reviewProxy=m.proxy;imageBase=m.images;temporary=!!m.temporary;doc=m.doc;refreshPointButton();frames=m.frames;shown=frames[0];ready=false;inUs=selected=loopId=null;history=[];future=[];closeEditor();$('welcome').hidden=true;$('seek').max=doc.durationUs;$('seek').value=0;$('seek').style.removeProperty('--played');v.src=m.src;render();status(tr('正在打开视频…'))}
 else if(m.type==='resumeReview'){seekUs(m.us);status(doc.fileName+(reviewProxy?tr(' · 临时审阅预览'):''))}
 else if(m.type==='preparingPreview')showProgress(m);
 else if(['voiceStarted','voiceFinishing','voiceStored','catalog','meters','region','recording','recordShortcut','pictureStored'].includes(m.type))return;
 else if(m.type==='busy'){busy=m.value;syncHistoryButtons();document.body.classList.toggle('busy',busy);if(busy){highlight(null);status(tr('正在处理文件…'))}else if(refreshLoadedMeasurements())render()}
 else if(m.type==='status'||m.type==='error')status(m.text,m.type==='error')});render();



