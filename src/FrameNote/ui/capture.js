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
function setMeters(){send('meterSources',{output:$('output').value,mic:$('mic').value});$('outLevel').value=$('micLevel').value=0}
window.captureButtons=()=>{
 const usable=!!catalog && !!$('source').value && ($('mode').value!=='region'||region?.device===$('source').value);
 for(const [id,kind,label] of [['record','video','录制视频'],['recordGif','gif','录制 GIF']]){
  $(id).textContent=recording===kind?'停止录制':label;
  $(id).disabled=starting||(recording?recording!==kind:(!usable||!!editing));
  $(id).classList.toggle('active',recording===kind);
 }
 $('captureFields').disabled=!!recording||starting;
};
function capture(gif){
 if(recording){send('stop');return}
 if($(gif?'recordGif':'record').disabled)return;
 starting=true;window.captureButtons();send('record',{gif,settings:{mode:$('mode').value,source:$('source').value,
 fps:+$('fps').value,high:$('quality').value==='high',output:$('output').value,mic:$('mic').value,cursor:$('cursor').checked}});
}
$('record').onclick=()=>capture(false);$('recordGif').onclick=()=>capture(true);
$('mode').onchange=()=>{sources(false);send('catalog')};$('source').onchange=()=>{clearRegion();window.captureButtons()};
$('refreshSources').onclick=()=>send('catalog');$('output').onchange=$('mic').onchange=setMeters;
$('pickRegion').onclick=()=>send('pickRegion',{source:$('source').value});
chrome.webview.addEventListener('message',({data:m})=>{
 if(m.type==='catalog'){
  const old=catalog;catalog=m;sources(true);
  populate('output',[{id:'',name:'关闭'},...m.outputs],old?$('output').value:m.outputs.find(d=>d.preferred)?.id);
  populate('mic',[{id:'',name:'关闭'},...m.inputs],$('mic').value);setMeters();
 }else if(m.type==='meters'){$('outLevel').value=m.output;$('micLevel').value=m.mic}
 else if(m.type==='region'){region=m.width?m:null;$('pickRegion').textContent=region?'重新框选':'框选区域';$('regionSize').textContent=region?m.width+' × '+m.height+' 像素':'';window.captureButtons()}
 else if(m.type==='recording'){starting=false;recording=m.value?(m.gif?'gif':'video'):null;window.captureButtons()}
 else if(m.type==='recordShortcut')capture(false);
 else if(m.type==='error' && starting){starting=false;window.captureButtons()}
});
send('catalog');
