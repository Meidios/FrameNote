'use strict';
let catalog=null, region=null, recording=null, starting=false;
function populate(id,items,value){
 const el=$(id);el.replaceChildren(...items.map(d=>new Option(d.name,d.id)));
 if(items.some(d=>d.id===value))el.value=value;
}
function sources(preserve){
 const mode=$('mode').value,prev=preserve?$('source').value:null;
 populate('source',(mode==='window'?catalog?.windows:catalog?.displays)??[],prev);
 $('regionControls').hidden=mode!=='region';
 if(!preserve || (region && region.device!==$('source').value))clearRegion();
 window.captureButtons();
}
function clearRegion(){region=null;send('clearRegion');$('pickRegion').textContent='框选区域';$('regionSize').textContent='';}
const outputs=new Map();
let microphone=null;
function newChannel(device){return {id:device.id,name:device.name,gainDb:0,muted:false,peak:0,offline:false}}
function settingsFor({id,gainDb,muted}){return {id,gainDb,muted}}
function channels(){return [...outputs.values(),...(microphone?[microphone]:[])]}
function paintPeak(ch){
 const db=ch.muted||ch.offline?-60:Math.min(0,Math.max(-60,20*Math.log10(ch.peak)+ch.gainDb));
 ch.mask.style.transform='scaleX('+(-db/60)+')';
 ch.meter.setAttribute('aria-valuenow',Math.round(db));
}
function strip(ch,isMic){
 const row=$('channelStrip').content.firstElementChild.cloneNode(true);
 const title=row.querySelector('strong'),remove=row.querySelector('.removeChannel'),mute=row.querySelector('.muteChannel'),
       slider=row.querySelector('input'),value=row.querySelector('.gainValue');
 title.textContent=(ch.offline?'已断开 · ':'')+ch.name;title.title=title.textContent;row.classList.toggle('offline',ch.offline);
 if(isMic)remove.remove();else{
  remove.setAttribute('aria-label','移除 '+ch.name);
  remove.onclick=()=>{outputs.delete(ch.id);renderMixer();$('addOutput').focus()};
 }
 mute.setAttribute('aria-label',ch.name+' 录制静音');slider.setAttribute('aria-label',ch.name+' 录制增益');
 ch.meter=row.querySelector('.channelMeter');ch.mask=ch.meter.firstElementChild;
 ch.meter.setAttribute('aria-label',ch.name+' 设备电平（应用录制增益，dBFS）');
 ch.meter.title='Windows 设备峰值（已计入录制增益和静音）';
 function update(){
  mute.setAttribute('aria-pressed',String(ch.muted));mute.title=ch.muted?'取消静音':'静音';
  mute.querySelector('use').setAttribute('href',ch.muted?'#icon-speaker_mute':'#icon-speaker_2');
  slider.value=ch.gainDb;value.textContent=ch.gainDb+' dB';slider.setAttribute('aria-valuetext',value.textContent);paintPeak(ch);
 }
 slider.oninput=()=>{ch.gainDb=Number(slider.value);update()};
 mute.onclick=()=>{ch.muted=!ch.muted;update()};update();return row;
}
function renderMixer(){
 const available=catalog.outputs.filter(d=>!outputs.has(d.id));
 populate('addOutput',[{id:'',name:available.length?'添加输出设备…':'没有更多输出设备'},...available],'');
 $('addOutput').disabled=!available.length;
 $('outputStrips').replaceChildren(...Array.from(outputs.values(),ch=>strip(ch,false)));
 const inputs=[{id:'',name:'关闭'},...catalog.inputs];
 if(microphone?.offline)inputs.push({id:microphone.id,name:'已断开 · '+microphone.name});
 populate('mic',inputs,microphone?.id??'');
 $('micStrip').replaceChildren(...(microphone?[strip(microphone,true)]:[]));
 send('meterSources',{ids:channels().filter(ch=>!ch.offline).map(ch=>ch.id)});window.captureButtons();
}
window.captureButtons=()=>{
 const usable=!!catalog && !!$('source').value && ($('mode').value!=='region'||region?.device===$('source').value);
 for(const [id,kind,label] of [['record','video','录制视频'],['recordGif','gif','录制 GIF']]){
  const stopping=recording===kind, action=stopping?'停止录制':label;
  const hint=kind==='video'?'（Ctrl+Alt+F9）':stopping?' GIF':'（最长 30 秒）';
  setIconAction(id,stopping?'stop':kind==='video'?'video_recording':'gif',action,action+hint);
  $(id).disabled=starting||(recording?recording!==kind:(!usable||!!editing||(kind==='video'&&channels().some(ch=>ch.offline))));
  $(id).classList.toggle('active',recording===kind);
 }
 for(const group of $('capturePanel').querySelectorAll('fieldset'))group.disabled=!!recording||starting;
 $('capturePanel').classList.toggle('isRecording',!!recording);
};
function capture(gif){
 if(recording){send('stop');return}
 if($(gif?'recordGif':'record').disabled)return;
 starting=true;window.captureButtons();send('record',{gif,settings:{mode:$('mode').value,source:$('source').value,
 fps:+$('fps').value,high:$('quality').value==='high',outputs:Array.from(outputs.values(),settingsFor),mic:microphone?settingsFor(microphone):null,cursor:$('cursor').checked}});
}
$('record').onclick=()=>capture(false);$('recordGif').onclick=()=>capture(true);
$('mode').onchange=()=>{sources(false);send('catalog')};$('source').onchange=()=>{clearRegion();window.captureButtons()};
$('refreshSources').onclick=()=>send('catalog');
$('addOutput').onchange=()=>{const d=catalog.outputs.find(d=>d.id===$('addOutput').value);if(d&&!outputs.has(d.id))outputs.set(d.id,newChannel(d));renderMixer()};
$('mic').onchange=()=>{const d=catalog.inputs.find(d=>d.id===$('mic').value);microphone=d?newChannel(d):null;renderMixer()};
$('pickRegion').onclick=()=>send('pickRegion',{source:$('source').value});
chrome.webview.addEventListener('message',({data:m})=>{
 if(m.type==='image')$('open').before($('captureCommands'));
 else if(m.type==='loaded')$('captureDock').append($('captureCommands'));
 else if(m.type==='catalog'){
  const old=catalog;catalog=m;sources(true);
  const preferred=m.outputs.find(d=>d.preferred);
  if(!old&&preferred)outputs.set(preferred.id,newChannel(preferred));
  for(const ch of channels()){
   const d=(ch===microphone?m.inputs:m.outputs).find(d=>d.id===ch.id);
   ch.offline=!d;if(d)ch.name=d.name;else ch.peak=0;
  }
  renderMixer();
 }else if(m.type==='meters'){for(const ch of channels()){ch.peak=m.levels[ch.id]??0;paintPeak(ch)}}
 else if(m.type==='region'){region=m.width?m:null;$('pickRegion').textContent=region?'重新框选':'框选区域';$('regionSize').textContent=region?m.width+' × '+m.height+' 像素':'';window.captureButtons()}
 else if(m.type==='recording'){starting=false;recording=m.value?(m.gif?'gif':'video'):null;window.captureButtons()}
 else if(m.type==='recordShortcut')capture(false);
 else if(m.type==='error' && starting){starting=false;window.captureButtons()}
});
send('catalog');
