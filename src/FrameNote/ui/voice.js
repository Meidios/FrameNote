'use strict';
let voiceRecording=null,voicePending=false,voicePreview=null;
let voiceDraft=null,discardVoice=false;
const voicePlayers=new Map();
let voiceNoteSource=null,voiceNotes=[];
window.voiceActive=()=>!!voiceRecording||voicePending;
function voiceMixerHint(){
 const selected=channels();
 if(selected.some(ch=>ch.offline))return '请在左侧音频混音器中移除或重新连接已断开的设备';
 return selected.some(ch=>!ch.muted)?'':'请在左侧音频混音器中启用至少一路声音';
}
window.refreshVoice=()=>{
 const n=editing??doc?.notes.find(n=>n.id===selected);
 const record=$('voiceRecord'),label=voiceRecording?'停止语音录制':'录制语音';
 const hint=voiceRecording?'':voiceMixerHint();
 record.hidden=imageDoc();
 $('voiceTools').hidden=!ready||record.hidden||(!n?.voice&&!$('voiceStatus').textContent);
 record.disabled=voicePending||(!voiceRecording&&(!ready||busy||!!hint));
 record.ariaDescription=hint||label;
 if(record.ariaLabel!==label){
  record.ariaLabel=label;
  record.querySelector('use').setAttribute('href',voiceRecording?'#icon-stop':'#icon-mic');
  record.classList.toggle('recording',!!voiceRecording);
 }
 $('voiceActions').hidden=!n?.voice;
};
function voiceLock(on){
 // The record button doubles as Stop, so lock its siblings instead of the whole toolbar.
 for(const el of document.querySelectorAll('header,#timelineTools,#editTools,#annotationTools > :not(#voiceRecord),#trackDesk,#capturePanel,#list,#noteActions,#transport,#detailFields,#voiceActions,#stage'))el.inert=on;
}
function recordAtPlayhead(at=annotationTime()){
 if(voiceRecording){finishRecording();return}
 if(!ready||!canAnnotate()||voiceMixerHint())return;
 finishEdit();transport.pause();loopId=null;
 const note=blankNote(at,null);if(!note)return;
 if(isFullPreview())send('previewFullscreen',{on:false});
 voiceDraft=note;discardVoice=false;transport.currentTime=at/1e6;voicePending=true;voiceLock(true);refreshVoice();
 send('voiceStart',{id:note.id,playbackId,startUs:at,mixer:mixerSettings()});
}
$('voiceRecord').onclick=()=>recordAtPlayhead();
function finishRecording(){
 if(voiceRecording&&!voicePending){voicePending=true;transport.pause();send('voiceStop');refreshVoice()}
}
function stopVoicePreview(){
 if(voicePreview){const a=voicePreview;voicePreview=null;releaseAudio(a)}
}
$('voicePreview').onclick=()=>{
 const n=editing??doc?.notes.find(n=>n.id===selected);if(!n?.voice)return;
 transport.pause();
 const a=voicePreview=new Audio(imageBase+n.voice.asset+'.m4a');a.volume=v.volume;
 a.onended=()=>{if(voicePreview===a)stopVoicePreview()};
 a.play().catch(e=>{if(voicePreview===a){stopVoicePreview();status(e.message,true)}});
};
$('voiceRemove').onclick=()=>{if(!editing)return;stopVoicePreview();editing.voice=null;persistEdit();refreshVoice()};
function syncVoices(reposition=false){
 if(voiceRecording&&!voicePending&&(transport.ended||transport.paused))finishRecording();
 if(doc?.timeline&&!voicePlayers.size)return;
 if(voiceNoteSource!==doc?.notes){voiceNoteSource=doc?.notes;voiceNotes=voiceNoteSource?.filter(n=>n.voice)??[]}
 const active=new Set(),time=transport.currentTime*1e6;
 const live=!v.paused&&!v.seeking&&!v.ended&&v.readyState>=3;
 if(!voiceRecording&&!voicePending&&!imageDoc()&&!doc?.timeline)for(const n of voiceNotes){
  if(time<n.startUs||time>=Math.min(n.endUs??Infinity,n.startUs+n.voice.durationUs))continue;
  const url=imageBase+n.voice.asset+'.m4a';let a=voicePlayers.get(n.id);
  if(a&&a.getAttribute('src')!==url){releaseAudio(a);voicePlayers.delete(n.id);a=null}
  if(!a){if(!live)continue;a=new Audio(url);voicePlayers.set(n.id,a)}
  active.add(n.id);syncClipMedia(a,(time-n.startUs)/1e6,v.playbackRate,v.volume,live,reposition);
 }
 for(const [id,a] of voicePlayers)if(!active.has(id)){releaseAudio(a);voicePlayers.delete(id)}
}
window.syncNoteVoices=syncVoices;
v.addEventListener('seeked',()=>syncVoices(true));
v.addEventListener('volumechange',()=>{if(voicePreview)voicePreview.volume=v.volume;syncVoices()});
for(const event of ['timeupdate','playing','waiting','pause','seeking','ratechange','ended'])v.addEventListener(event,()=>syncVoices());
document.addEventListener('keydown',e=>{if(voiceRecording||voicePending){
 e.preventDefault();e.stopImmediatePropagation();
 if(bound('cancel',e)){discardVoice=true;finishRecording()}
 else if(!e.repeat&&e.target===$('voiceRecord')&&(e.key==='Enter'||e.key===' '))finishRecording();
}},true);
chrome.webview.addEventListener('message',async({data:m})=>{
 if(m.type==='voiceStarted'){
  if(voiceDraft?.id!==m.id||discardVoice){send('voiceStop');return}
  voicePending=false;voiceRecording=m.id;transport.playbackRate=1;$('rate').value='1';
  $('voiceStatus').textContent='录音中 · 视频同步播放，点击停止'+bindingText('cancel','或按 ');
  refreshVoice();$('voiceRecord').focus({preventScroll:true});
  try{await transport.play()}catch{finishRecording()}
 }
 if(m.type==='voiceFinishing'&&voiceDraft?.id===m.id){voicePending=true;transport.pause();refreshVoice()}
 if(m.type==='voiceStored'){
  const note=voiceDraft;if(!note||note.id!==m.id)return;
  voiceDraft=null;
  voiceRecording=null;voicePending=false;transport.pause();voiceLock(false);closeEditor();
  if(discardVoice)status('已取消录音。');
  else if(m.error)status(m.error,true);
  else{commit([...doc.notes,{...note,voice:m.voice,endUs:m.endUs}],note.id);status('语音批注已保存，播放视频时同步回放。')}
  $('voiceStatus').textContent=discardVoice?'已取消录音':m.error?'录音失败':`已录制 ${((m.voice?.durationUs??0)/1e6).toFixed(1)} 秒`;refreshVoice();
 }
 if(m.type==='loaded'||m.type==='image'){stopVoicePreview();for(const a of voicePlayers.values())releaseAudio(a);voicePlayers.clear();voiceNoteSource=null;voiceNotes=[];$('voiceStatus').textContent='';refreshVoice()}
});
refreshVoice();
