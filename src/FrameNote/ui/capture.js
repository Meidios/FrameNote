'use strict';
let catalog=null, region=null, recording=null, starting=false;
let capturePrefs={};
function saveCapture(changes){
 Object.assign(capturePrefs,changes);
 send('capturePreferences',{changes});
}
function populate(id,items,value){
 const el=$(id);el.replaceChildren(...items.map(d=>new Option(d.name,d.id)));
 if(items.some(d=>d.id===value))el.value=value;
}
function captureTarget({id,name,app,pid,model,monitorId}){return {id,name,app,pid,model,monitorId}}
function matchTarget(mode,items,saved){
 if(!saved?.id)return items[0];
 if(mode!=='window'){
  if(saved.monitorId)return items.find(d=>d.monitorId===saved.monitorId);
  return items.find(d=>d.id===saved.id && d.model===saved.model);
 }
 const live=items.find(d=>d.id===saved.id && d.app===saved.app && d.pid===saved.pid && (d.pid || d.name===saved.name));
 if(live)return live;
 const matches=items.filter(d=>saved.app && d.app===saved.app && d.name===saved.name);
 return matches.length===1?matches[0]:null;
}
function cropFits(crop,target){
 if(!target || !crop || (crop.monitorId?crop.monitorId!==target.monitorId:crop.device!==target.id))return false;
 const {x,y,width,height}=crop;
 return [x,y,width,height].every(Number.isInteger) && x>=0 && y>=0 && width>=4 && height>=4
  && width%2===0 && height%2===0 && x+width<=target.width && y+height<=target.height;
}
function sources(){
 const mode=$('mode').value,key=mode+'Target',saved=capturePrefs[key];
 const items=mode==='window'?catalog.windows:catalog.displays;
 const target=matchTarget(mode,items,saved);
 if(target)capturePrefs[key]=captureTarget(target);
 const missing=!target && saved?.id;
 populate('source',missing?[{id:'',name:tr('不可用 · ')+saved.name},...items]:items,target?.id??'');
 $('regionControls').hidden=mode!=='region';
 const crop=capturePrefs.crop,previous=region;
 region=mode==='region' && cropFits(crop,target)?{...crop,device:target.id}:null;
 if(previous && !region)send('clearRegion');
 $('pickRegion').disabled=!target;
 $('pickRegion').textContent=region?tr('重新框选'):tr('框选区域');
 $('regionSize').textContent=region?region.width+' × '+region.height+tr(' 像素'):'';
 window.captureButtons();
}
const outputs=new Map();
let microphone=null;
function newChannel(device){return {id:device.id,name:device.name,gainDb:0,muted:false,peak:0,offline:false}}
function settingsFor({id,gainDb,muted}){return {id,gainDb,muted}}
function mixerSettings(){return {outputs:Array.from(outputs.values(),settingsFor),mic:microphone?settingsFor(microphone):null}}
function channels(){return [...outputs.values(),...(microphone?[microphone]:[])]}
function saveAudio(isMic){
 const saved=ch=>({...settingsFor(ch),name:ch.name});
 saveCapture(isMic?{mic:microphone?saved(microphone):null}:{outputs:Array.from(outputs.values(),saved)});
}
function restoreCapture(prefs){
 capturePrefs=prefs??{};
 const {mode,cursor}=capturePrefs;
 if(['display','window','region'].includes(mode))$('mode').value=mode;
 if(typeof cursor==='boolean')$('cursor').checked=cursor;
 function channel(d){
  if(!d || typeof d.id!=='string' || !d.id)return null;
  const ch=newChannel({id:d.id,name:typeof d.name==='string'?d.name:d.id});
  if(Number.isInteger(d.gainDb))ch.gainDb=Math.max(-60,Math.min(0,d.gainDb));
  ch.muted=d.muted===true;
  return ch;
 }
 microphone=channel(capturePrefs.mic);
 const preferred=catalog.outputs.find(d=>d.preferred);
 const list=Array.isArray(capturePrefs.outputs)?capturePrefs.outputs:(preferred?[preferred]:[]);
 for(const d of list){
  const ch=channel(d);
  if(ch && ch.id!==microphone?.id)outputs.set(ch.id,ch);
 }
}
let meterRows=[],meterVisible=false,meterFrame=0,meterPending=false,latestLevels=null;
function scheduleMeters(){
 const active=meterVisible && !document.hidden && meterRows.length>0;
 if(active && !meterFrame)meterFrame=requestAnimationFrame(tickMeters);
 else if(!active && meterFrame){cancelAnimationFrame(meterFrame);meterFrame=0;}
}
function tickMeters(){
 meterFrame=0;
 if(latestLevels){
  for(const ch of meterRows){
   const peak=latestLevels[ch.id]??0;
   if(ch.peak!==peak){ch.peak=peak;paintPeak(ch);}
  }
  latestLevels=null;
 }
 // Sample all channels together, with at most one native read in flight.
 if(!meterPending){meterPending=true;send('readMeters');}
 scheduleMeters();
}
new IntersectionObserver(([entry])=>{meterVisible=entry.isIntersecting;scheduleMeters()}).observe(document.querySelector('.mixer'));
document.addEventListener('visibilitychange',scheduleMeters);
function paintPeak(ch){
 const db=ch.muted||ch.offline?-60:Math.min(0,Math.max(-60,20*Math.log10(ch.peak)+ch.gainDb));
 ch.mask.style.transform='scaleX('+(-db/60)+')';
 const rounded=String(Math.round(db));
 if(ch.meter.getAttribute('aria-valuenow')!==rounded)ch.meter.setAttribute('aria-valuenow',rounded);
}
function strip(ch,isMic){
 const row=$('channelStrip').content.firstElementChild.cloneNode(true);
 const title=row.querySelector('strong'),remove=row.querySelector('.removeChannel'),mute=row.querySelector('.muteChannel'),
       slider=row.querySelector('input'),value=row.querySelector('.gainValue');
 title.textContent=(ch.offline?tr('已断开 · '):'')+ch.name;title.title=title.textContent;row.classList.toggle('offline',ch.offline);
 if(isMic)remove.remove();else{
  remove.setAttribute('aria-label',tr('移除 ')+ch.name);
  remove.onclick=()=>{outputs.delete(ch.id);saveAudio(false);renderMixer();$('addOutput').focus()};
 }
 mute.setAttribute('aria-label',ch.name+tr(' 录制静音'));slider.setAttribute('aria-label',ch.name+tr(' 录制增益'));
 ch.meter=row.querySelector('.channelMeter');ch.mask=ch.meter.firstElementChild;
 ch.meter.setAttribute('aria-label',ch.name+tr(' 设备电平（应用录制增益，dBFS）'));
 ch.meter.title=tr('录制前显示所选设备电平；录制中显示实际采集电平（已计入增益和静音）');
 function update(){
  mute.setAttribute('aria-pressed',String(ch.muted));mute.title=ch.muted?tr('取消静音'):tr('静音');
  mute.querySelector('use').setAttribute('href',ch.muted?'#icon-speaker_mute':'#icon-speaker_2');
  slider.value=ch.gainDb;value.textContent=ch.gainDb+' dB';slider.setAttribute('aria-valuetext',value.textContent);paintPeak(ch);
 }
 slider.oninput=()=>{ch.gainDb=Number(slider.value);update()};
 slider.onchange=()=>saveAudio(isMic);
 mute.onclick=()=>{ch.muted=!ch.muted;saveAudio(isMic);update();window.refreshVoice?.()};update();return row;
}
function renderMixer(){
 const available=catalog.outputs.filter(d=>!outputs.has(d.id)&&d.id!==microphone?.id);
 populate('addOutput',[{id:'',name:available.length?tr('添加输出设备…'):tr('没有更多输出设备')},...available],'');
 $('addOutput').disabled=!available.length;
 $('outputStrips').replaceChildren(...Array.from(outputs.values(),ch=>strip(ch,false)));
 const inputs=[{id:'',name:tr('关闭')},...catalog.inputs.filter(d=>!outputs.has(d.id))];
 if(microphone?.offline)inputs.push({id:microphone.id,name:tr('已断开 · ')+microphone.name});
 populate('mic',inputs,microphone?.id??'');
 $('micStrip').replaceChildren(...(microphone?[strip(microphone,true)]:[]));
 meterRows=channels().filter(ch=>!ch.offline);
 send('meterSources',{ids:meterRows.map(ch=>ch.id)});scheduleMeters();window.captureButtons();window.refreshVoice?.();
}
window.captureButtons=()=>{
 const usable=!!catalog && !!$('source').value && ($('mode').value!=='region'||region?.device===$('source').value);
 for(const [id,kind,label] of [['record','video',tr('录制视频')],['recordGif','gif',tr('录制 GIF')]]){
  const stopping=recording===kind, action=stopping?tr('停止录制'):label;
  const hint=kind==='video'?shortcutSuffix('record'):stopping?' GIF':tr('（最长 30 秒）');
  setIconAction(id,null,action,action+hint);
  $(id).disabled=starting||(recording?recording!==kind:(!usable||!!editing||(kind==='video'&&channels().some(ch=>ch.offline))));
  $(id).classList.toggle('active',recording===kind);
 }
 for(const group of $('capturePanel').querySelectorAll('fieldset'))group.disabled=!!recording||starting||(!catalog&&!group.contains($('refreshSources')));
 $('mode').disabled=$('source').disabled=!catalog;
};
function capture(gif){
 if(recording){send('stop');return}
 if($(gif?'recordGif':'record').disabled)return;
 starting=true;window.captureButtons();send('record',{gif,settings:{mode:$('mode').value,source:$('source').value,
 mixer:mixerSettings(),cursor:$('cursor').checked,region}});
}
$('record').onclick=()=>capture(false);$('recordGif').onclick=()=>capture(true);
$('mode').onchange=()=>{
 sources();const mode=$('mode').value,key=mode+'Target';
 saveCapture(capturePrefs[key]?{mode,[key]:capturePrefs[key]}:{mode});send('catalog');
};
$('source').onchange=()=>{
 const mode=$('mode').value,items=mode==='window'?catalog.windows:catalog.displays;
 const target=items.find(d=>d.id===$('source').value);
 if(target){saveCapture({[mode+'Target']:captureTarget(target)});sources();}
};
$('cursor').onchange=()=>saveCapture({cursor:$('cursor').checked});
$('refreshSources').onclick=()=>send('catalog',{initial:!catalog});
$('addOutput').onchange=()=>{const d=catalog.outputs.find(d=>d.id===$('addOutput').value);if(d&&!outputs.has(d.id)){outputs.set(d.id,newChannel(d));saveAudio(false);}renderMixer()};
$('mic').onchange=()=>{const d=catalog.inputs.find(d=>d.id===$('mic').value);microphone=d?newChannel(d):null;saveAudio(true);renderMixer()};
$('pickRegion').onclick=()=>send('pickRegion',{target:capturePrefs.regionTarget});
chrome.webview.addEventListener('message',({data:m})=>{
 if(m.type==='catalog'){
  const first=!catalog;catalog=m;
  if(first)restoreCapture(m.preferences);
  sources();
  for(const ch of channels()){
   const d=(ch===microphone?m.inputs:m.outputs).find(d=>d.id===ch.id);
   ch.offline=!d;if(d)ch.name=d.name;else ch.peak=0;
  }
  renderMixer();
 }else if(m.type==='meters'){meterPending=false;latestLevels=m.levels}
 else if(m.type==='region'){capturePrefs.crop=m.crop;sources();}
 else if(m.type==='recording'){starting=false;recording=m.value?(m.gif?'gif':'video'):null;window.captureButtons()}
 else if(m.type==='recordShortcut')capture(false);
 else if(m.type==='error' && starting){starting=false;window.captureButtons()}
});
window.captureButtons();send('catalog',{initial:true});
