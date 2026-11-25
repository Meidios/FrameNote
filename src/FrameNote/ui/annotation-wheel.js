'use strict';
(()=>{
 let gesture=null;
 const inside=(r,x,y)=>x>=r.x&&x<=r.x+r.width&&y>=r.y&&y<=r.y+r.height;
 function resizeFrame(s,factor,limit=1){
  const r=shapeRect(s,1,1,0),cx=r.x+r.width/2,cy=r.y+r.height/2;
  if(!r.width||!r.height)return s;
  const max=Math.min(limit/r.width,limit/r.height);
  const min=Math.min(1,4/Math.max(1,Math.min(r.width*overlay.clientWidth,r.height*overlay.clientHeight)));
  const scale=Math.min(max,Math.max(min,factor));
  if(scale===1)return s;
  const hw=r.width*scale/2,hh=r.height*scale/2;
  const x=clamp(cx,hw,1-hw),y=clamp(cy,hh,1-hh);
  const dx=s.x<=s.toX?hw:-hw,dy=s.y<=s.toY?hh:-hh;
  return {...s,x:clamp(x-dx,0,1),y:clamp(y-dy,0,1),toX:clamp(x+dx,0,1),toY:clamp(y+dy,0,1)};
 }
 function adjust(s,action,steps){
  if(action==='size')return resizeFrame(s,1.12**steps,s.preview&&s.zoom?1/s.zoom:1);
  if(action==='zoom'){
   const zoom=clamp(Math.round(magnifierLayout(s,1,1).scale*2)/2+steps*.5,1,16);
   const next={...s,zoom};
   return s.preview?resizeFrame(next,1,1/zoom):next;
  }
  if(action==='brush')return {...s,brushSize:clamp(s.brushSize+steps,1,64)};
  if(s.points)return {...s,smoothness:clamp(Math.round(((s.smoothness??1)+steps*.1)*10)/10,0,2)};
  const [mx,my]=curveMidpoint(s),w=overlay.clientWidth,h=overlay.clientHeight;
  // A signed offset perpendicular to the chord is independent of its orientation.
  const dx=(s.toX-s.x)*w,dy=(s.toY-s.y)*h,offset=.04*steps;
  return {...s,thirdX:clamp(mx+dy*offset/w,0,1),thirdY:clamp(my-dx*offset/h,0,1)};
 }
 function stepsFor(e,key){
  const now=performance.now();
  if(!gesture||gesture.key!==key||now-gesture.time>450){
   gesture={key,remainder:0,group:'wheel-'+now};
  }
  Object.assign(gesture,{time:now,x:e.clientX,y:e.clientY});
  const units=e.wheelDeltaY||-e.deltaY*(e.deltaMode===1?40:e.deltaMode===2?120:1.2);
  gesture.remainder+=units/120;
  const steps=Math.trunc(gesture.remainder);
  gesture.remainder-=steps;return steps;
 }
 function wheel(e){
  if(!e.deltaY||!canAnnotate()||geometryDrag)return;
  const stage=e.target.closest('#stage'),row=e.target.closest('#list .note');
  if(!stage&&!row)return;
  const color=wheelDirection('cycleColor',e),opacity=wheelDirection('opacity',e),direction=color||opacity;
  if(!direction&&(keyMods(e)||row||e.target.closest('#notePreview')))return;
  let hit=e.target;
  if(direction&&tool&&strokePointer===null&&stage){
   overlay.classList.remove('drawing');
   hit=document.elementFromPoint(e.clientX,e.clientY)??hit;
   overlay.classList.add('drawing');
  }
  let id=row?.dataset.note??hit.closest('[data-note]')?.dataset.note??(hit.closest('#notePreview')?hovered:null);
  let action;
  if(direction&&id&&strokePointer===null)action=color?'color':'opacity';
  else if(!direction&&tool==='brush'&&overlay.contains(e.target))action='drawing';
  else if(!direction&&!tool&&stage){
   // Keep a continuous wheel gesture on a curve as it moves away from a stationary pointer.
   if(!id&&gesture?.key.endsWith(':curve')&&performance.now()-gesture.time<450&&
       Math.hypot(e.clientX-gesture.x,e.clientY-gesture.y)<2)id=gesture.key.split(':')[0];
   const note=doc.notes.find(n=>n.id===id),s=note?.shape;
   if(!s||hit.closest('.inkBadge,[data-picture],#imageNav,button'))return;
   if(s.kind==='line'||s.kind==='arrow')action='curve';
   else if(s.kind==='brush')action='brush';
   else if(s.kind==='box'||s.kind==='magnifier'){
    const bounds=overlay.getBoundingClientRect(),x=(e.clientX-bounds.x)/bounds.width,y=(e.clientY-bounds.y)/bounds.height;
    const lens=s.kind==='magnifier'?magnifierLayout(s,1,1):null;
    if(lens&&inside(lens.target,x,y)&&(!s.preview||hit.hasAttribute('data-lens-preview')))action='zoom';
    else if(inside(lens?.source??shapeRect(s,1,1,0),x,y))action='size';
   }
  }
  if(!action)return;
  e.preventDefault();e.stopImmediatePropagation();
  const steps=stepsFor(e,(action==='drawing'?'tool':id)+':'+action)*(direction||1);
  if(!steps)return;
  if(action==='drawing'){
   brushSize=clamp(brushSize+steps,1,64);
   if(strokeNote.shape)strokeNote.shape.brushSize=brushSize;
   brushCursor=pos(e);scheduleBrushPreview();return;
  }
  if(editing&&editing.id!==id)finishEdit();
  const note=editing?.id===id?editing:doc.notes.find(n=>n.id===id);
  if(!note)return;
  transport.pause();
  if(action==='color'){
   const i=palette.findIndex(([,hex])=>hex===noteColor(note).toLowerCase()),count=palette.length;
   const start=i<0&&steps>0?count:i;
   applyNoteColor(id,palette[clamp(start-steps,0,count-1)][1],gesture.group);
  }else if(action==='opacity'){
   applyOpacity(id,noteOpacity(note)+5*steps,gesture.group);
  }else{
   const shape=adjust(note.shape,action,steps);
   if(JSON.stringify(shape)===JSON.stringify(note.shape))return;
   if(note===editing){editing.shape=shape;persistEdit(gesture.group)}
   else commit(doc.notes.map(n=>n.id===id?{...n,shape}:n),id,gesture.group);
   if(imageDoc()&&shape.kind==='magnifier')imageView.ensureOriginal();
  }
 }
 document.addEventListener('wheel',wheel,{capture:true,passive:false});
})();
