'use strict';
(()=>{
 const menu=$('canvasMenu');
 let context=null;
 function close(){
  if(context){menu.hidePopover();context=null}
 }
 function item(label,action,icon){
  const b=document.createElement('button');
  b.type='button';b.setAttribute('role','menuitem');b.dataset.action=action;
  if(icon)b.append(icon.cloneNode(true));
  b.append(label);$('menuCreate').append(b);return b;
 }
 for(const [kind,label] of Object.entries(shapeNames)){
  const button=$(kind==='ruler'?'toolRuler':kind);
  item(label,kind,button.querySelector('svg'));
 }
 item('图片批注','picture',$('addPicture').querySelector('svg'));
 item('文字批注','point');
 const voice=item('语音批注','voice');
 for(const [name,color] of palette){
  const b=document.createElement('button');
  b.type='button';b.dataset.color=color;b.title=name;b.setAttribute('aria-label',name);
  b.setAttribute('role','menuitemradio');b.style.setProperty('--note',color);$('menuColors').append(b);
 }
 const hitNote=e=>e.target.closest('[data-note]')?.dataset.note??(e.target.closest('#notePreview')?hovered:null);
 let pressed=null;
 $('stage').addEventListener('pointerdown',e=>{
  pressed=null;
  if(e.button!==2||!canAnnotate())return;
  // Focus changes can repaint coincident badges before Windows sends contextmenu.
  pressed={id:hitNote(e),x:e.clientX,y:e.clientY};e.preventDefault();
 },true);
 $('stage').addEventListener('contextmenu',e=>{
  if(!canAnnotate())return;
  e.preventDefault();
  const id=pressed&&Math.abs(pressed.x-e.clientX)<2&&Math.abs(pressed.y-e.clientY)<2?pressed.id:hitNote(e);pressed=null;
  const note=doc.notes.find(n=>n.id===id);
  transport.pause();const at=annotationTime();
  close();finishEdit();resetPreview();
  context={id:note?.id,at};
  $('menuTarget').hidden=!note;
  if(note){
   $('menuNoteName').textContent='批注 #'+note.number;
   for(const b of $('menuColors').children)b.setAttribute('aria-checked',String(b.dataset.color===noteColor(note)));
  }
  voice.hidden=imageDoc();voice.title=voiceMixerHint();voice.disabled=!!voice.title;
  menu.showPopover();
  menu.style.left=Math.max(4,Math.min(e.clientX,innerWidth-menu.offsetWidth-4))+'px';
  menu.style.top=Math.max(4,Math.min(e.clientY,innerHeight-menu.offsetHeight-4))+'px';
  menu.querySelector(note?'[data-action="edit"]':'button').focus({preventScroll:true});
 });
 menu.addEventListener('click',e=>{
  const b=e.target.closest('button');
  if(!b||b.disabled||!context)return;
  const {at,id}=context,action=b.dataset.action,color=b.dataset.color;
  close();if(!canAnnotate())return;
  if(Object.hasOwn(shapeNames,action)){startDrawing(action,at);return}
  if(action==='point'){newNote(at,null);focusNoteText();return}
  if(action==='picture'){pickPicture(at);return}
  if(action==='voice'){recordAtPlayhead(at);return}
  const note=doc.notes.find(n=>n.id===id);if(!note)return;
  if(action==='edit'){select(id,{seek:false});begin(note,false);focusNoteText()}
  else if(color){commit(doc.notes.map(n=>n.id===id?{...n,color}:n),id)}
  else if(action==='delete')deleteNote(id);
  else if(action==='attach')pickPicture(at,id);
  else if(action==='duplicate'){
   const fresh=blankNote(note.startUs,note.endUs);if(!fresh)return;
   const copy={...structuredClone(note),id:fresh.id,number:fresh.number,color:noteColor(note)};
   commit([...doc.notes,copy],copy.id,null,id);
  }
 });
 document.addEventListener('pointerdown',e=>{if(!menu.contains(e.target))close()},true);
 document.addEventListener('keydown',e=>{
  if(!context)return;
  if(bound('cancel',e)){e.preventDefault();e.stopImmediatePropagation();close();return}
  if(e.key==='Tab'){close();return}
  if(['ArrowDown','ArrowUp','ArrowLeft','ArrowRight','Home','End'].includes(e.key)){
   const buttons=[...menu.querySelectorAll('button:enabled')].filter(b=>b.offsetWidth);
   const i=buttons.indexOf(document.activeElement),step=['ArrowDown','ArrowRight'].includes(e.key)?1:-1;
   const next=e.key==='Home'?0:e.key==='End'?buttons.length-1:(i+step+buttons.length)%buttons.length;
   buttons[next].focus();e.preventDefault();
  }
  e.stopImmediatePropagation();
 },true);
 window.addEventListener('blur',close);window.addEventListener('resize',close);
 chrome.webview.addEventListener('message',({data:m})=>{if(m.type==='loaded'||m.type==='image'||m.type==='busy'&&m.value)close()});
})();
