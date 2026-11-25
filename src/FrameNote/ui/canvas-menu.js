'use strict';
(()=>{
 const menu=$('canvasMenu');
 let context=null;
 bindOpacity($('menuOpacity'),()=>doc?.notes.find(n=>n.id===context?.id));
 function close(){
  if(context){menu.hidePopover();context=null}
 }
 function item(label,action,icon){
  const b=document.createElement('button');
  b.type='button';b.setAttribute('role','menuitem');b.dataset.action=action;
  b.append(icon.cloneNode(true));
  b.append(label);$('menuCreate').append(b);return b;
 }
 for(const [kind,label] of Object.entries(shapeNames)){
  const button=$(kind==='ruler'?'toolRuler':kind);
  item(label,kind,button.querySelector('svg'));
 }
 const addAudio=item(tr('音频批注'),'audio',$('addAudio').querySelector('svg'));
 item(tr('图片批注'),'picture',$('addPicture').querySelector('svg'));
 item(tr('文字批注'),'point',$('point').querySelector('svg'));
 const voice=item(tr('语音批注'),'voice',$('voiceRecord').querySelector('svg'));
 const valueField=$('menuValue'),unitField=$('menuUnit');
 unitField.append(...Array.from($('rulerUnit').options,o=>o.cloneNode(true)));
 function showMeasurement(note){
  const ruler=note?.shape?.kind==='ruler',angle=note?.shape?.kind==='angle';
  $('menuMeasurement').hidden=!ruler&&!angle;
  if(!ruler&&!angle)return;
  $('menuFieldName').textContent=ruler?tr('距离'):tr('角度');
  $('menuFieldIcon').setAttribute('href',ruler?'#icon-ruler':'#icon-drafting-compass');
  const action=ruler?tr('标定本帧'):tr('应用角度');
  $('menuApply').setAttribute('aria-label',action);$('menuApply').ariaDescription=ruler?tr('用此长度标定同一帧的所有标尺'):action;
  unitField.hidden=!ruler;$('menuDegrees').hidden=!angle;
  valueField.setCustomValidity('');valueField.max=angle?'180':'';
  if(ruler){
   const scale=doc.rulerScales?.[note.shape.rulerFrame];
   context.metres=rulerMetres(note.shape,scale,...sourceSize());
   unitField.value=scale?.unit??defaultRulerUnit;
   displayDistance();
  }else valueField.value=parseFloat(angleGeometry(note.shape,...sourceSize()).label);
 }
 function displayDistance(){
  valueField.value=Number((context.metres/metresPerUnit[unitField.value]).toPrecision(6));
 }
 valueField.oninput=()=>{if(context)context.typed=true;valueField.setCustomValidity('')};
 unitField.onchange=()=>{if(context&&!context.typed)displayDistance()};
 function saveMeasurement(){
  const note=doc?.notes.find(n=>n.id===context?.id);
  if(!canAnnotate()||!note||!valueField.reportValidity())return;
  if(note.shape?.kind==='ruler'){
   const length=context.typed?valueField.valueAsNumber:context.metres/metresPerUnit[unitField.value];
   if(!(length>0)){valueField.setCustomValidity(tr('距离必须大于零'));valueField.reportValidity();return}
   if(calibrateRuler(note,length,unitField.value))close();
  }else if(note.shape?.kind==='angle'&&applyAngle(note,valueField.valueAsNumber))close();
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
  if(pathTool()&&measurementPoints.length){if(measurementPoints.length>=2)finishClickedPath();else setTool(null);pressed=null;return}
  const id=pressed&&Math.abs(pressed.x-e.clientX)<2&&Math.abs(pressed.y-e.clientY)<2?pressed.id:hitNote(e);pressed=null;
  transport.pause();const at=annotationTime();
  close();finishEdit();resetPreview();
  const note=doc.notes.find(n=>n.id===id);
  context={id:note?.id,at};
  $('menuTarget').hidden=!note;
  showMeasurement(note);
  if(note){
   $('menuNoteName').textContent=tr('批注 #')+note.number;
   $('menuOpacity').value=noteOpacity(note);
   updateColors(note,$('menuColors'),'aria-checked');
  }
  addAudio.hidden=voice.hidden=imageDoc();voice.ariaDescription=voiceMixerHint();voice.disabled=!!voice.ariaDescription;
  menu.showPopover();menu.scrollTop=0;
  menu.style.left=Math.max(4,Math.min(e.clientX,innerWidth-menu.offsetWidth-4))+'px';
  menu.style.top=Math.max(4,Math.min(e.clientY,innerHeight-menu.offsetHeight-4))+'px';
  menu.querySelector(note?'[data-action="edit"]':'#menuCreate button').focus({preventScroll:true});
 });
 menu.addEventListener('click',e=>{
  const b=e.target.closest('button');
  if(!b||b.disabled||!context)return;
  if(b===$('menuApply')){saveMeasurement();return}
  const {at,id}=context,action=b.dataset.action,color=b.dataset.color;
  const colorAnchor=action==='customColor'?b.getBoundingClientRect():null;
  close();if(!canAnnotate())return;
  if(Object.hasOwn(shapeNames,action)){startDrawing(action,at);return}
  if(action==='point'){newNote(at,null);focusNoteText();return}
  if(action==='audio'){pickAudio(at,id);return}
  if(action==='picture'){pickPicture(at);return}
  if(action==='voice'){recordAtPlayhead(at);return}
  const note=doc.notes.find(n=>n.id===id);if(!note)return;
  if(action==='edit'){select(id,{seek:false});begin(note,false);focusNoteText()}
  else if(colorAnchor)openNoteColorPicker(note,colorAnchor);
  else if(color)applyNoteColor(id,color);
  else if(action==='delete')deleteNote(id);
  else if(action==='attach')pickPicture(at,id);
  else if(action==='duplicate')duplicateNote(id);
 });
 document.addEventListener('pointerdown',e=>{if(!menu.contains(e.target))close()},true);
 document.addEventListener('keydown',e=>{
  if(!context)return;
  if(e.target.closest('option,select:is(:open)')){e.stopImmediatePropagation();return}
  if(bound('cancel',e)){e.preventDefault();e.stopImmediatePropagation();close();return}
  if(e.key==='Tab'){
   const items=[...menu.querySelectorAll('button:enabled,input,select')].filter(el=>el.offsetWidth);
   const next=items.indexOf(document.activeElement)+(e.shiftKey?-1:1);
   if(next<0||next>=items.length){close();return}
   e.preventDefault();items[next].focus();
  }else if(e.target===valueField&&e.key==='Enter'&&!e.isComposing){e.preventDefault();saveMeasurement()}
  else if(!e.target.matches('input,select')&&['ArrowDown','ArrowUp','ArrowLeft','ArrowRight','Home','End'].includes(e.key)){
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
