'use strict';
const keyMods=e=>(e.altKey?1:0)|(e.ctrlKey?2:0)|(e.shiftKey?4:0)|(e.metaKey?8:0);
const bindingLabel=id=>window.shortcutBindings[id].label;
function bindingText(ids,before,after=''){
 const keys=[].concat(ids).map(bindingLabel).filter(Boolean).join(' ');
 return keys?before+keys+after:'';
}
const shortcutHint=(label,id)=>label+bindingText(id,'（','）');
function bound(id,e,vk=e.keyCode){
 return window.shortcutBindings[id].keys.some(b=>b.vk===vk&&b.mods===keyMods(e));
}
function refreshPointButton(){
 const point=$('point'),image=imageDoc();
 point.ariaLabel=image?'添加批注':'点批注';
 point.ariaDescription=shortcutHint(image?'添加批注':'添加点批注','point');
}
function refreshShortcutHints(){
 refreshPointButton();
 for(const [id,action,label] of [
  ['open','open','打开文件'],['save','save','导出审阅包'],['prev','prev','上一帧'],['next','next','下一帧'],
  ['prevImage','prev','上一张图片'],['nextImage','next','下一张图片'],
  ['inBtn','inBtn','设置区间进点'],['outBtn','outBtn','设置区间出点并添加批注'],['splitClip','splitClip','在播放头处分割'],
  ['joinClip','joinClip','将片段开头贴到同轨上一片段结尾，首段移到零点'],
  ['joinNextClip','joinNextClip','将片段结尾贴到同轨下一片段开头，末段贴到时间线末尾'],
  ['deleteClip','deleteClip','删除选中片段，保留空隙'],
  ['box','quickBox','框选'],['arrow','quickArrow','箭头'],['text','quickText','文字框']
 ]){
  const el=$(id);el.ariaDescription=shortcutHint(label,action);
  if(id==='inBtn'||id==='outBtn')el.setAttribute('aria-label',el.ariaDescription);
  const badge=el.querySelector('kbd');if(badge)badge.textContent=bindingLabel(action);
 }
 $('help').textContent=[bindingText('play','',' 播放'),bindingText(['prev','next'],'',' 逐帧'),bindingText(['prevNote','nextNote'],'',' 跳转批注')].filter(Boolean).join('　');
 $('welcome').querySelector('p').textContent='打开视频进行批注'+bindingText('paste','，或按 ',' 查看剪贴板图片')+'。';
 $('timelineTools').title=[bindingText('zoomTimeline','','缩放时间线'),bindingText('panTimeline','','平移时间线')].filter(Boolean).join('，');
 $('play').ariaDescription=shortcutHint(transport.paused?'播放':'暂停','play');
 if(isFullPreview())$('fullPreview').ariaDescription=shortcutHint('退出全屏','cancel');
 if(imageDoc())imageView.hint();
 if(inUs!==null)$('rangeHint').textContent='起点 '+fmt(inUs)+bindingText('outBtn','　定位后按 ');
 window.captureButtons?.();
}
document.addEventListener('DOMContentLoaded',refreshShortcutHints);
chrome.webview.addEventListener('message',({data:m})=>{
 if(m.type==='shortcuts'){window.shortcutBindings=m.bindings;refreshShortcutHints()}
});
