'use strict';
const keyMods=e=>(e.altKey?1:0)|(e.ctrlKey?2:0)|(e.shiftKey?4:0)|(e.metaKey?8:0);
const bindingLabel=id=>window.shortcutBindings[id].label;
function bindingText(ids,before,after=''){
 const keys=[].concat(ids).map(bindingLabel).filter(Boolean).join(' ');
 return keys?before+keys+after:'';
}
const shortcutSuffix=id=>bindingText(id,tr('（'),tr('）'));
const shortcutHint=(label,id)=>label+shortcutSuffix(id);
function bound(id,e,vk=e.keyCode,mods=keyMods(e)){
 return window.shortcutBindings[id].keys.some(b=>!b.with&&b.vk===vk&&b.mods===mods);
}
function wheelDirection(id,e){
 const key=window.shortcutBindings[id].keys.find(b=>b.mods===keyMods(e));
 return key?.vk===256?1:key?.vk===257?-1:0;
}
function applyAnnotationToolbar(entries){
 const bar=$('annotationTools');
 bar.querySelectorAll('.toolSeparator').forEach(el=>el.remove());
 for(const item of entries){
  if(item.id==='separator'){
   const separator=document.createElement('span');separator.className='toolSeparator';
   separator.setAttribute('role','separator');bar.append(separator);continue;
  }
  const button=$(item.id);if(!button)continue;
  button.toggleAttribute('data-toolbar-hidden',!item.visible);
  button.classList.toggle('doubleWidth',item.wide);bar.append(button);
 }
}
// If a speed key also starts a two-key reset, wait for release before changing speed.
const speedKeys=(()=>{
 const held=new Map();
 function down(e){
  const key=e.keyCode,mods=keyMods(e);
  const pairs=window.shortcutBindings.rateReset.keys.filter(b=>b.with&&b.mods===mods&&(b.vk===key||b.with===key));
  const single=['rateDown','rateUp','rateReset'].find(id=>bound(id,e));
  if(!pairs.length){if(single)adjustPlaybackRate(single);return !!single}
  for(const pair of pairs){
   const other=pair.vk===key?pair.with:pair.vk;
   if(held.get(other)?.mods===mods){
    held.delete(other);adjustPlaybackRate('rateReset');return true;
   }
  }
  held.set(key,{mods,single});return true;
 }
 document.addEventListener('keyup',e=>{
  const press=held.get(e.keyCode);held.delete(e.keyCode);
  if(press){e.preventDefault();adjustPlaybackRate(press.single)}
 });
 for(const event of ['blur','pointerdown','focusin'])window.addEventListener(event,()=>held.clear());
 return {down,clear:()=>held.clear()};
})();
function refreshPointButton(){
 const point=$('point'),image=imageDoc();
 point.ariaLabel=image?tr('添加批注'):tr('点批注');
 point.ariaDescription=shortcutHint(image?tr('添加批注'):tr('添加点批注'),'point');
}
function refreshShortcutHints(){
 refreshPointButton();
 for(const [id,action,label] of [
  ['open','open',tr('打开文件')],['save','save',tr('导出审阅包')],['prev','prev',tr('上一帧')],['next','next',tr('下一帧')],
  ['prevImage','prev',tr('上一张图片')],['nextImage','next',tr('下一张图片')],
  ['inBtn','inBtn',tr('设置区间进点')],['outBtn','outBtn',tr('设置区间出点并添加批注')],['splitClip','splitClip',tr('分割')],
  ['joinClip','joinClip',tr('将片段开头贴到同轨上一片段结尾，首段移到零点')],
  ['joinNextClip','joinNextClip',tr('将片段结尾贴到同轨下一片段开头，末段贴到时间线末尾')],
  ['linkTracks','linkTracks',tr('轨道联动')],['deleteClip','deleteClip',tr('删除片段')],
  ['copyFrame','copyFrame',tr('复制当前帧到剪贴板')],
  ['box','quickBox',tr('框选')],['arrow','quickArrow',tr('箭头')],['text','quickText',tr('文字框')]
 ]){
  const el=$(id);el.ariaDescription=shortcutHint(label,action);
  if(id==='inBtn'||id==='outBtn')el.setAttribute('aria-label',el.ariaDescription);
  const badge=el.querySelector('kbd');if(badge)badge.textContent=bindingLabel(action);
 }
 const pathHint=tr('拖拽绘制，或单击连续落点。右键、双击或 Enter 完成，Esc 取消。');
 $('arrow').ariaDescription+=' · '+pathHint;
 $('line').ariaDescription=tr('线')+' · '+pathHint;
 $('welcome').querySelector('p').textContent=tr('打开视频、图片或审阅包')+bindingText('paste',tr('，或按 '),tr(' 查看剪贴板图片'))+tr('。');
 $('timelineTools').title=[bindingText('zoomTimeline','',tr('缩放时间线')),bindingText('panTimeline','',tr('平移时间线'))].filter(Boolean).join(tr('，'));
 $('play').ariaDescription=shortcutHint(transport.paused?tr('播放'):tr('暂停'),'play');
 $('fullPreview').ariaDescription=shortcutHint(isFullPreview()?tr('退出全屏'):tr('全屏预览'),'fullPreview');
 window.captureButtons?.();
}
document.addEventListener('DOMContentLoaded',refreshShortcutHints);
chrome.webview.addEventListener('message',({data:m})=>{
 if(m.type==='shortcuts'){
  speedKeys.clear();window.shortcutBindings=m.bindings;refreshShortcutHints();setDefaultRulerUnit(m.defaultRulerUnit);
  defaultLensZoom=m.defaultLensZoom;defaultOpacity=m.defaultOpacity;applyAnnotationToolbar(m.annotationToolbar);
  buildPalette(m.presetColors);refreshDetails();
 }
});
