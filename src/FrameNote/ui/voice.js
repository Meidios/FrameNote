'use strict';
let voiceRecording=null,voicePending=false;
let voiceDraft=null,discardVoice=false;
const voicePlayers=new Map();
let voiceNoteSource=null,voiceNotes=[];
const take=$('voiceTake'),wave=take.querySelector('canvas'),takeTime=document.createElement('time');
takeTime.setAttribute('aria-hidden','true');
let envelope=[],binUs=20000,recordedUs=0;
function paintTake(){
 if(!voiceDraft||take.hidden||!doc)return;
 const length=Math.min(recordedUs,doc.durationUs-voiceDraft.startUs);
 take.style.left=(100*voiceDraft.startUs/doc.durationUs)+'%';take.style.width=(100*length/doc.durationUs)+'%';
 takeTime.textContent=' · '+fmt(length);
 const w=Math.max(1,Math.min(4096,Math.round(wave.clientWidth*devicePixelRatio)));
 const h=Math.max(1,Math.round(wave.clientHeight*devicePixelRatio));
 if(wave.width!==w)wave.width=w;if(wave.height!==h)wave.height=h;
 const ctx=wave.getContext('2d'),bins=Math.max(1,Math.ceil(length/binUs));
 ctx.clearRect(0,0,w,h);ctx.fillStyle=getComputedStyle(take).color;
 for(let x=0;x<w;x++){
  const first=Math.floor(x*bins/w),last=Math.min(envelope.length,Math.ceil((x+1)*bins/w));let peak=0;
  for(let i=first;i<last;i++)peak=Math.max(peak,envelope[i]);
  const level=peak>0?Math.max(0,1+20*Math.log10(peak)/60):0;
  const bar=Math.max(1,level*h);ctx.fillRect(x,(h-bar)/2,1,bar);
 }
}
new ResizeObserver(()=>{if(!take.hidden)paintTake()}).observe($('voiceClips'));
function clearTake(){take.hidden=true;envelope=[];recordedUs=0;takeTime.remove()}
window.voiceActive=()=>!!voiceRecording||voicePending;
function voiceMixerHint(){
 return microphone&&!microphone.offline&&!microphone.muted?'':tr('请启用麦克风后再录语音批注。');
}
window.refreshVoice=()=>{
 const record=$('voiceRecord'),label=voiceRecording?tr('停止语音录制'):tr('录制语音');
 const hint=voiceRecording?'':voiceMixerHint();
 $('addAudio').hidden=imageDoc();$('addAudio').disabled=!ready||busy||voicePending||!!voiceRecording;
 record.hidden=imageDoc();
 record.disabled=voicePending||(!voiceRecording&&(!ready||busy||!!hint));
 record.ariaDescription=hint||label;
 if(record.ariaLabel!==label){
  record.ariaLabel=label;
  record.querySelector('use').setAttribute('href',voiceRecording?'#icon-stop':'#icon-mic');
  record.classList.toggle('recording',!!voiceRecording);
 }
};
function voiceLock(on){
 syncHistoryButtons();
 // The record button doubles as Stop, so lock its siblings instead of the whole toolbar.
 for(const el of document.querySelectorAll('header,#timelineTools,#editTools,#annotationTools > :not(#voiceRecord),#trackDesk,#capturePanel,#list,#noteActions,#transport,#detailFields,#stage'))el.inert=on;
}
function recordAtPlayhead(at=annotationTime()){
 if(voiceRecording){finishRecording();return}
 if(!ready||!canAnnotate()||voiceMixerHint())return;
 finishEdit();transport.pause();loopEnabled=false;updateLoopButton();
 const note=blankNote(at,null);if(!note)return;
 if(isFullPreview())send('previewFullscreen',{on:false});
 voiceDraft=note;discardVoice=false;transport.currentTime=at/1e6;voicePending=true;voiceLock(true);refreshVoice();
 send('voiceStart',{id:note.id,playbackId,startUs:at,mixer:mixerSettings()});
}
$('voiceRecord').onclick=()=>recordAtPlayhead();
function finishRecording(){
 if(voiceRecording&&!voicePending){voicePending=true;transport.pause();send('voiceStop');refreshVoice()}
}
function syncVoices(reposition=false){
 if(voiceRecording&&!voicePending&&(transport.ended||transport.paused))finishRecording();
 if(doc?.timeline&&!voicePlayers.size)return;
 if(voiceNoteSource!==doc?.notes){voiceNoteSource=doc?.notes;voiceNotes=voiceNoteSource?.filter(n=>n.voice)??[]}
 const active=new Set(),time=transport.currentTime*1e6;
 const live=!v.paused&&!v.seeking&&!v.ended&&v.readyState>=3;
 if(!voiceRecording&&!voicePending&&!imageDoc()&&!doc?.timeline)for(const n of voiceNotes){
  if(time<n.startUs||time>=Math.min(n.endUs??Infinity,n.startUs+n.voice.durationUs))continue;
  const url=imageBase+n.voice.asset+'.'+(n.voice.format??'m4a');let a=voicePlayers.get(n.id);
  if(a&&a.getAttribute('src')!==url){releaseAudio(a);voicePlayers.delete(n.id);a=null}
  if(!a){if(!live)continue;a=new Audio(url);voicePlayers.set(n.id,a)}
  active.add(n.id);syncClipMedia(a,(time-n.startUs)/1e6,v.playbackRate,v.volume,live,reposition);
 }
 for(const [id,a] of voicePlayers)if(!active.has(id)){releaseAudio(a);voicePlayers.delete(id)}
}
window.syncNoteVoices=syncVoices;
v.addEventListener('seeked',()=>syncVoices(true));
for(const event of ['timeupdate','playing','waiting','pause','seeking','ratechange','volumechange','ended'])v.addEventListener(event,()=>syncVoices());
document.addEventListener('keydown',e=>{if(voiceRecording||voicePending){
 e.preventDefault();e.stopImmediatePropagation();
 if(bound('cancel',e)){discardVoice=true;clearTake();finishRecording()}
 else if(!e.repeat&&e.target===$('voiceRecord')&&(e.key==='Enter'||e.key===' '))finishRecording();
}},true);
chrome.webview.addEventListener('message',async({data:m})=>{
 if(m.type==='voiceStarted'){
  if(voiceDraft?.id!==m.id||discardVoice){send('voiceStop');return}
  voicePending=false;voiceRecording=m.id;v.volume=0;transport.playbackRate=1;$('rate').value='1';
  $('voiceStatus').textContent=tr('录音中 · 视频同步播放，点击停止')+bindingText('cancel',tr('或按 '));
  clearTake();binUs=m.binUs;take.hidden=false;$('voiceStatus').append(takeTime);paintTake();
  refreshVoice();$('voiceRecord').focus({preventScroll:true});
  try{await transport.play()}catch{finishRecording()}
 }
 if(m.type==='voiceProgress'&&m.id===voiceRecording&&!voicePending){
  for(let i=0;i<m.peaks.length;i++)envelope[m.offset+i]=m.peaks[i];
  recordedUs=m.elapsedUs;paintTake();
 }
 if(m.type==='voiceFinishing'&&voiceDraft?.id===m.id){voicePending=true;transport.pause();refreshVoice()}
 if(m.type==='voiceStored'){
  const note=voiceDraft;if(!note||note.id!==m.id)return;
  clearTake();voiceDraft=null;
  voiceRecording=null;voicePending=false;transport.pause();v.volume=Number($('playbackVolume').value)/100;voiceLock(false);closeEditor();
  if(discardVoice)status(tr('已取消录音。'));
  else if(m.error)status(m.error,true);
  else{commit([...doc.notes,{...note,voice:m.voice,endUs:m.endUs}],note.id);status(tr('语音批注已保存，播放视频时同步回放。'))}
  $('voiceStatus').textContent='';refreshVoice();
 }
 if(m.type==='loaded'||m.type==='image'){clearTake();clearDrop();for(const a of voicePlayers.values())releaseAudio(a);voicePlayers.clear();voiceNoteSource=null;voiceNotes=[];$('voiceStatus').textContent='';refreshVoice()}
});
refreshVoice();

function pickAudio(at=annotationTime(),targetId=selected){
 if(!ready||imageDoc()||!canAnnotate())return;
 finishEdit();transport.pause();send('pickAudio',{documentId:doc.id,at,targetId});
}
$('addAudio').onclick=()=>pickAudio();
function importAudio(files,choice){
 if(!files.length||!choice||doc?.id!==choice.documentId||!ready||imageDoc()||!canAnnotate())return;
 finishEdit();transport.pause();
 chrome.webview.postMessageWithAdditionalObjects({type:'importAudio',...choice},Array.from(files));
}
const audioLane=$('voiceClips').parentElement;
const dropPreview=$('audioDropPreview');
const dropGuides=document.querySelector('.audioDropGuides');
const dropTime=x=>Math.min(doc.durationUs-1,timeAtPointer(x));
let dropLengths=[],placingAudio=false;
function clearDrop(){dropLengths=[];placingAudio=false;dropGuides.hidden=dropPreview.hidden=true;dropPreview.replaceChildren()}
function showDrop(at){
 let start=at;
 for(const [i,length] of dropLengths.entries()){
  const block=dropPreview.children[i];
  block.style.left=(100*start/doc.durationUs)+'%';
  block.style.width=(100*length/doc.durationUs)+'%';
  start+=length;
 }
 dropGuides.firstElementChild.style.left=(100*at/doc.durationUs)+'%';
 dropGuides.firstElementChild.style.width=(100*(start-at)/doc.durationUs)+'%';
 dropGuides.hidden=dropPreview.hidden=!dropLengths.length;
}
function acceptFileDrag(e){
 if(!e.dataTransfer.types.includes('Files')||!ready||imageDoc()||!canAnnotate())return false;
 e.preventDefault();e.stopPropagation();e.dataTransfer.dropEffect='copy';
 return true;
}
audioLane.addEventListener('dragenter',e=>{
 if(!acceptFileDrag(e))return;
 const row=audioLane.getBoundingClientRect(),axis=$('ticks').getBoundingClientRect(),visible=$('scroll').getBoundingClientRect();
 const left=Math.max(visible.left,row.left),right=Math.min(visible.right,row.right);
 send('watchAudioDrop',{documentId:doc.id,viewportWidth:innerWidth,left,top:row.top,width:right-left,height:row.height,axisLeft:axis.left,axisWidth:axis.width});
});
audioLane.addEventListener('dragover',acceptFileDrag);
audioLane.addEventListener('drop',e=>{
 e.preventDefault();e.stopPropagation();clearDrop();
 if(doc)importAudio(e.dataTransfer.files,{documentId:doc.id,at:dropTime(e.clientX),targetId:null});
});
chrome.webview.addEventListener('message',({data:m})=>{
 if(m.type==='busy'&&!m.value&&placingAudio)clearDrop();
 if(!doc||m.documentId!==doc.id)return;
 if(m.type==='audioDropLengths'){
  dropLengths=m.lengths;dropPreview.replaceChildren(...dropLengths.map(()=>document.createElement('span')));showDrop(m.at);
 }else if(m.type==='audioDropMove')showDrop(m.at);
 else if(m.type==='audioDropEnd')clearDrop();
 else if(m.type==='audioDropReleased'){
  if(!ready||imageDoc()||!canAnnotate()){clearDrop();return}
  showDrop(m.at);placingAudio=true;finishEdit();transport.pause();send('acceptAudioDrop',{id:m.id});
 }
 if(m.type!=='audioImported'||m.documentId!==doc?.id)return;
 clearDrop();
 try{
  const next=currentEditDoc();let at=m.at,id=null;
  for(const [i,voice] of m.voices.entries()){
   let n=i===0&&next.notes.find(x=>x.id===m.targetId&&!x.voice);
   if(n)at=n.startUs;
   else{
    n=blankNote(at,null);if(!n)return;
    n.number=++next.lastNoteNumber;n.color=palette[(n.number-1)%palette.length][1];next.notes.push(n);
   }
   id=n.id;n.voice=voice;n.endUs=Math.max(n.endUs??at,at+voice.durationUs);
   next.timeline.clips.push({id:crypto.randomUUID(),track:'voice',noteId:id,asset:voice.asset,format:voice.format,startUs:at,sourceUs:0,durationUs:voice.durationUs,sourceDurationUs:voice.durationUs});
   at=n.endUs;next.durationUs=Math.max(next.durationUs,at);
  }
  next.version=3;EditModel.validate(next);commitTimeline(next,id);status(tr('已添加音频批注。'));
 }catch(error){status(error.message,true)}
});
