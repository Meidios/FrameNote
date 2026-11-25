'use strict';
(()=>{
 const buttons='button[aria-description]:not([aria-haspopup="menu"],[data-no-hint])';
 const groups='header,#transport,#timelineBar,#imageToolbar,.imageExport,#imageNav,#playlistToggle,#capturePanel,.sideTitle,.rulerControls';
 const hint=document.getElementById('buttonHint');
 let group=null, target=null, fade=null,timer=0;

 function light(next,x,y){
  if(group!==next){
   group?.classList.remove('hoverLighting');
   group=next;
   group?.classList.add('hoverLighting');
  }
  if(group){
   group.style.setProperty('--light-x',x+'px');
   group.style.setProperty('--light-y',y+'px');
  }
 }
 function position(){
  if(!target)return;
  const box=target.getBoundingClientRect();
  if(!box.width||!box.height){show(null);return}
  const x=box.left+(box.width-hint.offsetWidth)/2;
  const above=box.top-hint.offsetHeight-6;
  hint.style.left=Math.max(4,Math.min(x,innerWidth-hint.offsetWidth-4))+'px';
  hint.style.top=(above>=4?above:Math.min(box.bottom+6,innerHeight-hint.offsetHeight-4))+'px';
 }
 function update(){
  const label=target?.ariaDescription;
  if(!label){show(null);return}
  hint.textContent=label;
  position();
 }
 const changes=new MutationObserver(update);
 const bounds=new ResizeObserver(position);
 function show(button,immediate=false){
  if(target===button&&!(immediate&&timer))return;
  clearTimeout(timer);timer=0;
  changes.disconnect();
  bounds.disconnect();
  fade?.cancel();
  target=button;
  hint.classList.remove('visible');
  if(hint.matches(':popover-open'))hint.hidePopover();
  if(!button)return;
  function reveal(){
   timer=0;
   if(!button.isConnected||!button.getClientRects().length){show(null);return}
   hint.showPopover();
   changes.observe(button,{attributes:true,attributeFilter:['aria-description','hidden']});
   bounds.observe(button);
   update();
   if(!target)return;
   hint.classList.add('visible');
   if(!matchMedia('(prefers-reduced-motion: reduce)').matches){
    fade=hint.animate([{opacity:0},{opacity:1}],{duration:120,easing:'ease-out'});
   }
  }
  if(immediate)reveal();else timer=setTimeout(reveal,1000);
 }
 document.addEventListener('pointermove',e=>{
  if(e.pointerType!=='mouse'||e.buttons)return;
  const next=e.target.closest(groups)??e.target.closest('select');
  light(next,e.clientX,e.clientY);
  show(e.target.closest(buttons));
 });
 function clear(){light(null);show(null)}
 document.addEventListener('pointerout',e=>{
  if(!e.relatedTarget){clear();return}
  if(target&&!target.contains(e.relatedTarget))show(null);
  if(group&&!group.contains(e.relatedTarget))light(null);
 });
 document.addEventListener('focusin',e=>{
  if(e.target.matches(buttons+':focus-visible'))show(e.target,true);
 });
 document.addEventListener('focusout',e=>{if(e.target===target)show(null)});
 document.addEventListener('pointerdown',clear,true);
 document.addEventListener('keydown',e=>{if(e.key==='Escape')clear()});
 document.addEventListener('scroll',clear,true);
 window.addEventListener('resize',clear);
 window.addEventListener('blur',clear);
})();
