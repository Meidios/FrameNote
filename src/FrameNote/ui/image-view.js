'use strict';
const imageView=(()=>{
 const stage=$('stage'),img=$('openedImage');
 let scale=1,x=0,y=0,drag=null;
 const clamp=(n,min,max)=>Math.max(min,Math.min(max,n));
 function stopDrag(){
  if(drag && stage.hasPointerCapture(drag.id))stage.releasePointerCapture(drag.id);
  drag=null;stage.classList.remove('dragging');
 }
 function draw(){
  const w=stage.clientWidth,h=stage.clientHeight;
  if(w && h && img.naturalWidth && img.naturalHeight){
   // Constrain the actual bitmap, not the letterboxed <img> element.
   const fit=Math.min(w/img.naturalWidth,h/img.naturalHeight);
   const constrain=(offset,view,extent)=>{
    const visible=Math.min(24,view/2,extent/2);
    return clamp(offset,visible-(view/2+extent/2),view/2+extent/2-visible);
   };
   x=constrain(x,w,img.naturalWidth*fit*scale);y=constrain(y,h,img.naturalHeight*fit*scale);
  }
  if(!img.hidden && img.naturalWidth && img.naturalHeight){
   const k=Math.min(w/img.naturalWidth,h/img.naturalHeight)*scale;
   const width=img.naturalWidth*k,height=img.naturalHeight*k;
   Object.assign(overlay.style,{left:((w-width)/2+x)+'px',top:((h-height)/2+y)+'px',width:width+'px',height:height+'px'});
   paint();
  }
  img.style.transform='translate('+x+'px,'+y+'px) scale('+scale+')';
  stage.classList.toggle('pannable',scale>1);
 }
 function reset(){stopDrag();scale=1;x=y=0;draw()}
 function show(src,folder){
  img.hidden=false;img.src=src;reset();
  $('imageHelp').textContent='滚轮缩放　放大后左键拖动'+(folder?'　← → 切图':'');
  stage.focus({preventScroll:true});
 }
 stage.addEventListener('wheel',e=>{
  if(img.hidden)return;
  e.preventDefault();if(!e.deltaY || !img.naturalWidth)return;
  const r=stage.getBoundingClientRect();
  const cx=e.clientX-r.left-r.width/2,cy=e.clientY-r.top-r.height/2;
  const units=e.deltaMode===1?16:e.deltaMode===2?r.height:1;
  const next=clamp(scale*2**(-clamp(e.deltaY*units,-1200,1200)/480),.1,32);
  const ratio=next/scale;
  x=cx-(cx-x)*ratio;y=cy-(cy-y)*ratio;scale=next;
  if(scale<=1)stopDrag();draw();
 },{passive:false});
 stage.addEventListener('pointerdown',e=>{
  if(img.hidden || tool || e.target.closest("[data-note],[data-picture]") || scale<=1 || e.button!==0 || !e.isPrimary)return;
  e.preventDefault();stage.focus({preventScroll:true});stage.setPointerCapture(e.pointerId);
  drag={id:e.pointerId,lastX:e.clientX,lastY:e.clientY};stage.classList.add('dragging');
 });
 stage.addEventListener('pointermove',e=>{
  if(drag?.id!==e.pointerId)return;
  x+=e.clientX-drag.lastX;y+=e.clientY-drag.lastY;drag.lastX=e.clientX;drag.lastY=e.clientY;draw();
 });
 for(const event of ['pointerup','pointercancel','lostpointercapture'])
  stage.addEventListener(event,e=>{if(drag?.id===e.pointerId)stopDrag()});
 img.addEventListener("load",draw);
 new ResizeObserver(()=>{if(!img.hidden)draw()}).observe(stage);
 return {show,reset};
})();
