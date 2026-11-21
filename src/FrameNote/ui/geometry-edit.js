'use strict';
let geometryDrag=null;
const clamp=(value,min,max)=>Math.max(min,Math.min(max,value));
const rectangularShape=s=>['box','magnifier','text'].includes(s.kind);
function shapePoints(s){
 const points=[['x','y'],['toX','toY']];
 if(s.kind==='angle')points.push(['thirdX','thirdY']);
 return points;
}

// The overlay alone gets these controls. Exports use the same drawing without this layer.
function drawEditHandles(ink,n,w,h){
 const group=svg('g',{class:'geometryHandles','data-note':n.id,fill:noteColor(n),stroke:'#192028'});
 const i=editing?.id===n.id?activePicture:-1;
 const pictureIndex=i<0&&!n.shape?0:i;
 const p=n.pictures?.[pictureIndex],s=n.shape;
 if(p){
  const x=p.x*w,y=p.y*h,width=p.width*w,height=width*p.pixelHeight/p.pixelWidth;
  group.append(svg('rect',{x,y,width,height,fill:'none',stroke:noteColor(n),'pointer-events':'none'}));
  for(const [a,b] of [[0,0],[1,0],[0,1],[1,1]]){
   group.append(svg('rect',{x:x+a*width-5,y:y+b*height-5,width:10,height:10,rx:2,
    class:'pictureHandle','data-picture':pictureIndex,'data-corner':[a,b],cursor:a===b?'nwse-resize':'nesw-resize'}));
  }
 }else if(s){
  const box=rectangularShape(s);
  const handles=box?[['x','y'],['toX','y'],['x','toY'],['toX','toY']]:shapePoints(s);
  for(const [x,y] of handles){
   const diagonal=(s[x]===Math.min(s.x,s.toX))===(s[y]===Math.min(s.y,s.toY));
   group.append(svg('circle',{cx:s[x]*w,cy:s[y]*h,r:5,class:'shapeHandle',
    'data-shape':'','data-point':[x,y],cursor:box?(diagonal?'nwse-resize':'nesw-resize'):'crosshair'}));
  }
 }
 ink.append(group);
}

function dragShape(shape,start,point,dx,dy){
 if(point){
  const [x,y]=point;
  shape[x]=clamp(start[x]+dx,0,1);shape[y]=clamp(start[y]+dy,0,1);
  return;
 }
 const points=shapePoints(start),xs=points.map(([x])=>start[x]),ys=points.map(([,y])=>start[y]);
 dx=clamp(dx,-Math.min(...xs),1-Math.max(...xs));
 dy=clamp(dy,-Math.min(...ys),1-Math.max(...ys));
 for(const [x,y] of points){
  shape[x]=clamp(start[x]+dx,0,1);shape[y]=clamp(start[y]+dy,0,1);
 }
}

function endGeometryDrag(commitChange){
 const drag=geometryDrag;
 if(!drag)return;
 geometryDrag=null;
 if(overlay.hasPointerCapture(drag.pointer))overlay.releasePointerCapture(drag.pointer);
 if(!commitChange)Object.assign(drag.target,drag.start);
 else if(drag.moved)persistEdit();
 paint();
 if(commitChange && !drag.picture && imageDoc() && ['ruler','magnifier'].includes(drag.target.kind))imageView.ensureOriginal();
}

function pictureAtPointer(e,el,n){
 if(el.hasAttribute('data-picture'))return Number(el.dataset.picture);
 if(!n.pictures?.length || !['box','magnifier'].includes(n.shape?.kind) || !e.target.matches('rect.inkHit'))return -1;
 // The transparent frame catches its interior too. A visible picture inside it stays draggable.
 const r=e.target.getBoundingClientRect();
 if(Math.min(e.clientX-r.left,r.right-e.clientX,e.clientY-r.top,r.bottom-e.clientY)<6.5)return -1;
 const picture=document.elementsFromPoint(e.clientX,e.clientY).find(hit=>
  hit.matches('.picture') && hit.closest('[data-note]')?.dataset.note===n.id);
 return picture?Number(picture.dataset.picture):-1;
}

document.addEventListener('DOMContentLoaded',()=>{
 overlay.addEventListener('pointerdown',e=>{
  if(!e.isPrimary||e.button!==0||tool||geometryDrag||busy||imagePending||window.voiceActive?.()||!(ready||imageDoc()))return;
  const el=e.target.closest('[data-shape],[data-picture]');
  if(!el)return;
  const note=doc.notes.find(n=>n.id===el.closest('[data-note]')?.dataset.note);
  if(!note)return;
  const i=pictureAtPointer(e,el,note),picture=i>=0;
  const point=el.dataset.point?.split(','),corner=el.dataset.corner?.split(',').map(Number);
  resetPreview();
  if(editing?.id!==note.id){finishEdit();selected=note.id;begin(note,false);}
  transport.pause();activePicture=i;
  const target=picture?editing.pictures[i]:editing.shape;
  geometryDrag={pointer:e.pointerId,at:pos(e),target,start:{...target},picture,corner,point,moved:false};
  overlay.setPointerCapture(e.pointerId);
  e.preventDefault();e.stopImmediatePropagation();render();
 },true);
 function update(e){
  const {at,target,start,picture,corner,point}=geometryDrag,[x,y]=pos(e);
  const dx=x-at[0],dy=y-at[1],bounds=overlay.getBoundingClientRect();
  if(!geometryDrag.moved && Math.hypot(dx*bounds.width,dy*bounds.height)<2)return;
  geometryDrag.moved=true;
  if(picture)dragPicture(target,start,corner,dx,dy,bounds);
  else dragShape(target,start,point,dx,dy);
 }
 overlay.addEventListener('pointermove',e=>{
  if(geometryDrag?.pointer!==e.pointerId)return;
  update(e);e.stopImmediatePropagation();paint();
 },true);
 for(const type of ['pointerup','pointercancel','lostpointercapture']){
  overlay.addEventListener(type,e=>{
   if(geometryDrag?.pointer!==e.pointerId)return;
   if(type==='pointerup')update(e);
   endGeometryDrag(type==='pointerup');e.stopImmediatePropagation();
  },true);
 }
 overlay.addEventListener('dblclick',e=>{
  if(tool||busy||editing?.shape?.kind!=='text')return;
  const shape=document.elementFromPoint(e.clientX,e.clientY)?.closest('[data-shape]');
  if(shape?.closest('[data-note]')?.dataset.note===editing.id){
   $('body').focus({preventScroll:true});$('body').select();e.preventDefault();
  }
 });
 document.addEventListener('keydown',e=>{
  if(bound('cancel',e)&&geometryDrag){endGeometryDrag(false);e.preventDefault();e.stopImmediatePropagation();}
 },true);
 window.addEventListener('blur',()=>endGeometryDrag(false));
});
