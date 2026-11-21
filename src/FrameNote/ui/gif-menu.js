'use strict';
(()=>{
 const group=$('gifExport'),trigger=$('exportGif'),menu=$('gifExportMenu');
 const items=[...menu.querySelectorAll('[role=menuitem]')];
 let timer;
 function setOpen(open){
  clearTimeout(timer);
  open=open&&!trigger.disabled&&!busy&&!group.closest('[inert]');
  trigger.setAttribute('aria-expanded',String(open));
  menu.inert=!open;
 }
 group.addEventListener('pointerenter',e=>{
  clearTimeout(timer);
  if(e.pointerType==='mouse'&&menu.inert)timer=setTimeout(()=>setOpen(true),30);
 });
 group.addEventListener('pointerleave',()=>{
  clearTimeout(timer);
  if(!menu.contains(document.activeElement))timer=setTimeout(()=>setOpen(false),100);
 });
 trigger.onclick=e=>{setOpen(true);if(!menu.inert&&e.detail===0)items[0].focus()};
 group.addEventListener('focusout',e=>{if(!group.contains(e.relatedTarget))setOpen(false)});
 group.addEventListener('keydown',e=>{
  if(e.key==='Escape'&&!menu.inert){setOpen(false);trigger.focus()}
  else if(e.key==='ArrowDown'||e.key==='ArrowUp'){
   setOpen(true);
   const i=items.indexOf(document.activeElement),step=e.key==='ArrowDown'?1:-1;
   items[i<0?(step===1?0:items.length-1):(i+step+items.length)%items.length].focus();
  }else if(e.key===' '||e.key==='Enter'){e.stopPropagation();return}
  else return;
  e.preventDefault();e.stopPropagation();
 });
 document.addEventListener('pointerdown',e=>{if(!group.contains(e.target))setOpen(false)},true);
 document.addEventListener('keydown',e=>{
  if(e.key==='Escape'&&!menu.inert&&!group.contains(e.target)){setOpen(false);e.preventDefault();e.stopImmediatePropagation()}
 },true);
 window.addEventListener('blur',()=>setOpen(false));
 menu.onclick=e=>{
  const item=e.target.closest('[role=menuitem]');
  if(!item||menu.inert)return;
  setOpen(false);trigger.focus({preventScroll:true});
  finishEdit();transport.pause();send('exportGif',{copy:item.id==='copyGif'});
 };
 new MutationObserver(()=>{if(trigger.disabled)setOpen(false)}).observe(trigger,{attributes:true,attributeFilter:['disabled']});
 chrome.webview.addEventListener('message',({data})=>{if(data.type==='busy'&&data.value)setOpen(false)});
})();
