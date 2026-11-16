'use strict';
(()=>{
 const axis=$('ticks'),film=$('filmstrip'),wave=$('waveform').querySelector('path');
 const viewport=$('scroll'),timeline=$('timeline');
 let scale=1;
 function navigate(e){
  if(!duration||!ready||busy||editing||e.altKey||e.metaKey||e.ctrlKey===e.shiftKey)return;
  const unit=e.deltaMode===1?16:e.deltaMode===2?viewport.clientWidth:1;
  const delta=(e.deltaY||e.deltaX)*unit;
  if(!delta)return;
  e.preventDefault();
  if(e.shiftKey){viewport.scrollLeft+=delta;return;}
  const next=Math.max(1,Math.min(8,scale*2**(-delta/120)));
  if(next===scale)return;
  const bounds=viewport.getBoundingClientRect(),x=Math.max(bounds.left,Math.min(bounds.right,e.clientX));
  const before=axis.getBoundingClientRect();
  if(!before.width)return;
  const fraction=Math.max(0,Math.min(1,(x-before.left)/before.width));
  scale=next;timeline.style.width=(100*scale)+'%';
  const after=axis.getBoundingClientRect();
  viewport.scrollLeft+=after.left+fraction*after.width-x;
 }
 for(const area of [$('trackDesk'),$('timelineTools')])area.addEventListener('wheel',navigate,{passive:false});
 let duration=0,id=null,frameset=[],requested=0;
 function ruler(){
  if(!duration||!axis.clientWidth)return;
  const width=axis.clientWidth,ideal=duration*86/width,base=10**Math.floor(Math.log10(ideal));
  const major=[1,2,5,10].find(n=>n*base>=ideal)*base,minor=major/5;
  const ticks=document.createDocumentFragment();
  for(let i=0;i*minor<=duration;i++){
   const t=i*minor,el=document.createElement('span');el.style.left=(100*t/duration)+'%';
   if(i%5===0){
    el.className='major';
    if((1-t/duration)*width>=50){const label=document.createElement('small');label.textContent=fmt(t).slice(0,major>=1e6?-4:undefined);el.append(label)}
   }
   ticks.append(el);
  }
  axis.replaceChildren(ticks);
 }
 function paintFilm(){
  requested=0;
  if(!frameset.length)return;
  const count=Math.max(1,Math.ceil(film.clientWidth/76));
  if(film.childElementCount!==count)film.replaceChildren(...Array.from({length:count},()=>{const im=new Image();im.alt='';im.draggable=false;return im}));
  for(let i=0;i<count;i++){
   const src=frameset[Math.floor((i+.5)*frameset.length/count)],im=film.children[i];
   if(src && im.getAttribute('src')!==src)im.src=src;
  }
 }
 function scheduleFilm(){if(!requested)requested=requestAnimationFrame(paintFilm)}
 new ResizeObserver(()=>{ruler();scheduleFilm()}).observe(axis);
 function reset(m){
  scale=1;timeline.style.width='100%';viewport.scrollLeft=0;
  duration=m?.doc.durationUs??0;id=m?.playbackId??null;frameset=[];
  $('trackDesk').hidden=!id;film.replaceChildren();wave.removeAttribute('d');
  $('filmState').textContent=id?'正在生成缩略图…':'';
  $('waveState').textContent=id?'正在读取波形…':'';
  $('timeline').style.setProperty('--position','0%');ruler();
 }
 chrome.webview.addEventListener('message',({data:m})=>{
  if(m.type==='image'){reset(null);return}
  if(m.type==='loaded'){reset(m);return}
  if(m.type!=='timelineData'||m.playbackId!==id)return;
  if(m.track==='frame'){
   frameset.length=m.data.count;frameset[m.data.i]=m.data.src;
   $('filmState').textContent='';scheduleFilm();
  }else if(m.track==='wave'){
   const {peaks,stepUs}=m.data,top=[],bottom=[];
   for(let i=0;i<peaks.length;i++){
    const x=Math.min(1000,1000*(i+.5)*stepUs/duration).toFixed(2),h=15*Math.sqrt(peaks[i]/255);
    top.push(x+','+(16-h).toFixed(2));bottom.push(x+','+(16+h).toFixed(2));
   }
   wave.setAttribute('d',top.length?'M0,16 L'+top.join(' ')+' L'+bottom.reverse().join(' ')+' Z':'');
   $('waveState').textContent=peaks.some(p=>p>0)?'':'静音';
  }else if(m.track==='silent')$('waveState').textContent='无音轨';
  else if(m.track==='waveError')$('waveState').textContent='波形不可用';
  else if((m.track==='filmDone'||m.track==='filmError')&&!frameset.some(Boolean))$('filmState').textContent='缩略图不可用';
 });
 function scrub(e){
  const r=axis.getBoundingClientRect();if(!ready||editing||!r.width)return;
  seekTimeline(Math.max(0,Math.min(1,(e.clientX-r.left)/r.width))*duration);
 }
 for(const track of document.querySelectorAll('.mediaTrack')){
  track.onpointerdown=e=>{if(e.button||!ready||editing)return;track.setPointerCapture(e.pointerId);scrub(e)};
  track.onpointermove=e=>{if(track.hasPointerCapture(e.pointerId))scrub(e)};
 }
})();
