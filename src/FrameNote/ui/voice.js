'use strict';
let voiceRecording=null,voicePending=false,voicePreview=null;
const voicePlayers=new Map();
window.voiceActive=()=>!!voiceRecording||voicePending;
window.refreshVoice=()=>{
 const n=editing??doc?.notes.find(n=>n.id===selected);
 $('voiceTools').hidden=imageDoc();$('voiceRecord').disabled=!n||!ready||!$('voiceDevice').value||busy||voicePending;
 $('voicePreview').hidden=$('voiceRemove').hidden=!n?.voice;
 $('voiceRecord').textContent=voiceRecording?'停止语音录制':'录制语音';
};
function voiceLock(on){
 for(const selector of ['header','#timelineTools','#capturePanel','#list','#transport','#editor .bar','#noteDuration','#body','#colors','#voiceDevice']){
  const el=document.querySelector(selector);if(el)el.inert=on;
 }
 // Keep the stop action reachable while all other editing is locked.
 $('voiceRecord').parentElement.inert=false;
 $('voicePreview').disabled=$('voiceRemove').disabled=on;
 $('stage').inert=on;
}
$('voiceRecord').onclick=()=>{
 if(voiceRecording){voicePending=true;send('voiceStop');refreshVoice();return}
 const n=editing??doc?.notes.find(n=>n.id===selected);if(!n||!ready||voicePending)return;
 finishEdit();v.pause();seekUs(n.startUs);voicePending=true;voiceLock(true);refreshVoice();
 send('voiceStart',{id:n.id,device:$('voiceDevice').value});
};
$('voicePreview').onclick=()=>{
 const n=editing??doc?.notes.find(n=>n.id===selected);if(!n?.voice)return;
 voicePreview?.pause();v.pause();voicePreview=new Audio(imageBase+n.voice.asset+'.m4a');voicePreview.volume=v.volume;voicePreview.play().catch(e=>status(e.message,true));
};
$('voiceRemove').onclick=()=>{if(!editing)return;voicePreview?.pause();editing.voice=null;persistEdit();refreshVoice()};
function syncVoices(){
 const time=v.currentTime*1e6,active=new Set();
 if(!v.paused&&!voiceRecording&&!voicePending&&!imageDoc())for(const n of doc?.notes??[]){
  if(!n.voice||time<n.startUs||time>=Math.min(n.endUs??n.startUs+n.voice.durationUs,n.startUs+n.voice.durationUs))continue;
  active.add(n.id);let a=voicePlayers.get(n.id);
  if(!a||!a.src.endsWith(n.voice.asset+'.m4a')){a?.pause();a=new Audio(imageBase+n.voice.asset+'.m4a');voicePlayers.set(n.id,a)}
  const offset=(time-n.startUs)/1e6;if(Math.abs(a.currentTime-offset)>.08)a.currentTime=offset;
  a.playbackRate=v.playbackRate;a.volume=v.volume;
  if(a.paused)a.play().catch(()=>{});
 }
 for(const [id,a] of voicePlayers)if(!active.has(id)){a.pause();if(!doc?.notes.some(n=>n.id===id))voicePlayers.delete(id)}
 if(voiceRecording && (v.ended||v.paused)){voicePending=true;send('voiceStop');voiceRecording=null;refreshVoice()}
}
window.syncNoteVoices=syncVoices;
v.addEventListener('play',()=>voicePreview?.pause());
for(const event of ['timeupdate','play','pause','seeking','seeked','ratechange','volumechange','ended'])v.addEventListener(event,syncVoices);
document.addEventListener('keydown',e=>{if(voiceRecording||voicePending){e.preventDefault();e.stopImmediatePropagation();if(e.key==='Escape')send('voiceStop')}},true);
chrome.webview.addEventListener('message',async({data:m})=>{
 if(m.type==='catalog'){
  const old=$('voiceDevice').value;$('voiceDevice').replaceChildren(...m.inputs.map(d=>new Option(d.name,d.id)));
  if(m.inputs.some(d=>d.id===old))$('voiceDevice').value=old;else if(m.inputs.some(d=>d.preferred))$('voiceDevice').value=m.inputs.find(d=>d.preferred).id;refreshVoice();
 }
 if(m.type==='voiceStarted'){
  voicePending=false;voiceRecording=m.id;v.playbackRate=1;$('rate').value='1';refreshVoice();
  $('voiceStatus').textContent='录音中 · 视频同步播放，点击停止或按 Esc';
  try{await v.play()}catch{send('voiceStop')}
 }
 if(m.type==='voiceStored'){
  voiceRecording=null;voicePending=false;v.pause();voiceLock(false);closeEditor();
  if(m.error)status(m.error,true);
  else{const n=doc?.notes.find(n=>n.id===m.id);if(n){commit(doc.notes.map(n=>n.id===m.id?{...n,voice:m.voice,endUs:m.endUs}:n),m.id);status('语音批注已保存，播放视频时同步回放。')}}
  $('voiceStatus').textContent=m.error?'录音失败':`已录制 ${((m.voice?.durationUs??0)/1e6).toFixed(1)} 秒`;refreshVoice();
 }
 if(m.type==='exportProgress'){$('exportProgress').hidden=!m.active;$('exportStatus').textContent=m.text??'';}
 if(m.type==='loaded'||m.type==='image'){voicePreview?.pause();for(const a of voicePlayers.values())a.pause();voicePlayers.clear();refreshVoice()}
});
refreshVoice();
