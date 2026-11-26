'use strict';
function waveformPath({peaks,stepUs},duration,width){
 const top=[],bottom=[];
 for(let i=0;i<peaks.length;i++){
  const x=Math.min(width,width*(i+.5)*stepUs/duration).toFixed(2),h=15*Math.sqrt(peaks[i]/255);
  top.push(x+','+(16-h).toFixed(2));bottom.push(x+','+(16+h).toFixed(2));
 }
 const end=(width*Math.min(1,peaks.length*stepUs/duration)).toFixed(2);
 return top.length?'M0,16 L'+top.join(' ')+' L'+end+',16 '+bottom.reverse().join(' ')+' Z':'';
}
(()=>{
 const axis=$('ticks'),film=$('sourceFrames'),wave=$('sourceWave');
 const viewport=$('scroll'),timeline=$('timeline');
 let scale=1;
 function navigate(e){
  const pan=wheelDirection('panTimeline',e),direction=pan||wheelDirection('zoomTimeline',e);
  if(!duration||!ready||busy||editing||drag||!direction)return;
  const unit=e.deltaMode===1?16:e.deltaMode===2?viewport.clientWidth:1;
  const delta=(e.deltaY||e.deltaX)*unit*direction;
  if(!delta)return;
  e.preventDefault();
  if(pan){viewport.scrollLeft+=delta;return;}
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
 let duration=0,sourceDuration=0,id=null,frameset=[],requested=0;
 let tickDuration=0,tickStep=0,lastLabel=-1;
 window.refreshTimeline=()=>{duration=doc?.durationUs??0;ruler();scheduleFilm()};
 function ruler(){
  if(!duration||!axis.clientWidth)return;
  const width=axis.clientWidth,ideal=duration*86/width,base=10**Math.floor(Math.log10(ideal));
  const major=[1,2,5,10].find(n=>n*base>=ideal)*base,minor=major/5;
  // Tick positions are percentages. Resizing only changes the DOM when the
  // interval or the last label that fits at the right edge actually changes.
  const last=Math.floor(duration*(1-50/width)/major);
  if(tickDuration===duration&&tickStep===major&&lastLabel===last)return;
  tickDuration=duration;tickStep=major;lastLabel=last;
  const ticks=document.createDocumentFragment();
  for(let i=0;i*minor<=duration;i++){
   const t=i*minor,el=document.createElement('span');el.style.left=(100*t/duration)+'%';
   if(i%5===0){
    el.className='major';
    if(i/5<=last){const label=document.createElement('small');label.textContent=fmt(t).slice(0,major>=1e6?-4:undefined);el.append(label)}
   }
   ticks.append(el);
  }
  axis.replaceChildren(ticks);
 }
 function paintFilm(){
  requested=0;
  if(!frameset.length||!duration)return;
  const sourceWidth=axis.clientWidth*sourceDuration/duration;
  const count=Math.max(1,Math.min(2048,Math.ceil(sourceWidth/76)));
  if(film.childElementCount!==count)film.replaceChildren(...Array.from({length:count},(_,i)=>
   svg('image',{x:100*i/count+'%',width:100/count+'%',height:'100%',preserveAspectRatio:'xMidYMid slice'})));
  for(let i=0;i<count;i++){
   const src=frameset[Math.floor((i+.5)*frameset.length/count)],im=film.children[i];
   if(src && im.getAttribute('href')!==src)im.setAttribute('href',src);
  }
 }
 function scheduleFilm(){if(!requested)requested=requestAnimationFrame(paintFilm)}
 new ResizeObserver(()=>{ruler();scheduleFilm()}).observe(axis);
 function reset(m){
  scale=1;timeline.style.width='100%';viewport.scrollLeft=0;
  duration=m?.doc.durationUs??0;sourceDuration=m?.doc.timeline?.sourceDurationUs??duration;
  id=m?.playbackId??null;frameset=[];
  $('trackDesk').hidden=!id;film.replaceChildren();wave.removeAttribute('d');
  $('filmState').textContent=id?tr('正在生成缩略图…'):'';
  $('waveState').textContent=id?tr('正在读取波形…'):'';
  $('timeline').style.setProperty('--position','0%');ruler();
 }
 chrome.webview.addEventListener('message',({data:m})=>{
  if(m.type==='image'){reset(null);return}
  if(m.type==='loaded'){if(m.recovering)id=m.playbackId;else reset(m);return}
  if(m.type!=='timelineData'||m.playbackId!==id)return;
  if(m.track==='frame'){
   frameset.length=m.data.count;frameset[m.data.i]=m.data.src;
   $('filmState').textContent='';scheduleFilm();
  }else if(m.track==='wave'){
   wave.setAttribute('d',waveformPath(m.data,sourceDuration,1000));
   $('waveState').textContent=m.data.peaks.some(p=>p>0)?'':tr('静音');
  }else if(m.track==='silent')$('waveState').textContent=tr('无音轨');
  else if(m.track==='waveError')$('waveState').textContent=tr('波形不可用');
  else if((m.track==='filmDone'||m.track==='filmError')&&!frameset.some(Boolean))$('filmState').textContent=tr('缩略图不可用');
 });
})();
