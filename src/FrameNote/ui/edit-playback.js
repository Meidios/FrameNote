'use strict';
// The project clock is independent of decoder time, so gaps and detached audio remain playable.
const transport=(()=>{
 let paused=true,time=0,rate=1,last=0,sourceFrames=[],sourceUrl='',videoId=null;
 const sounds=new Map(),active=()=>!!doc?.timeline;
 function icon(){const stop=api.paused;setIconAction('play',stop?'play':'pause',stop?'播放':'暂停',(stop?'播放':'暂停')+'（空格）')}
 function audio(c,us,live){
  let a=sounds.get(c.id);const url=c.track==='voice'?imageBase+c.asset+'.m4a':sourceUrl;
  if(!a||a.src!==url){a?.pause();a=new Audio(url);sounds.set(c.id,a)}
  const offset=(c.sourceUs+us-c.startUs)/1e6;
  if(Math.abs(a.currentTime-offset)>.07)a.currentTime=offset;
  a.volume=v.volume;a.muted=v.volume===0;a.playbackRate=rate;
  if(live&&a.paused)a.play().catch(()=>{});else if(!live)a.pause();
 }
 function sync(){
  if(!active())return;
  const us=time*1e6,c=doc.timeline.clips.find(c=>c.track==='video'&&us>=c.startUs&&us<EditModel.end(c));
  v.muted=true;v.style.visibility=c?'visible':'hidden';
  if(c){const offset=(c.sourceUs+us-c.startUs)/1e6;if(videoId!==c.id||Math.abs(v.currentTime-offset)>.12){v.currentTime=offset;videoId=c.id;}v.playbackRate=rate;if(!paused&&v.paused)v.play().catch(()=>{});if(paused)v.pause();}
  else{videoId=null;v.pause()}
  const ids=new Set();for(const clip of doc.timeline.clips)if(clip.track!=='video'&&us>=clip.startUs&&us<EditModel.end(clip)){ids.add(clip.id);audio(clip,us,!paused&&(clip.track==='audio'||!window.voiceActive?.()))}
  for(const [id,a] of sounds)if(!ids.has(id)){a.pause();sounds.delete(id)}
  shown=Math.round(us);$('clock').textContent=fmt(shown);$('seek').value=shown;$('timeline').style.setProperty('--position',100*shown/doc.durationUs+'%');paint();icon();
 }
 function step(now){
  if(active()&&ready){
   if(!paused){time=Math.min(doc.durationUs/1e6,time+Math.max(0,(now-last)/1000)*rate);const n=doc.notes.find(n=>n.id===loopId);if(n&&time*1e6>=n.endUs)time=n.startUs/1e6;if(time>=doc.durationUs/1e6)paused=true;}
   sync();window.syncNoteVoices?.();
  }
  last=now;requestAnimationFrame(step);
 }
 requestAnimationFrame(step);
 const api={
  get currentTime(){return active()?time:v.currentTime},set currentTime(x){if(active()){time=Math.max(0,Math.min(doc.durationUs/1e6,x));videoId=null;sync()}else v.currentTime=x},
  get paused(){return active()?paused:v.paused},get ended(){return active()?time>=doc.durationUs/1e6:v.ended},
  get playbackRate(){return active()?rate:v.playbackRate},set playbackRate(x){rate=x;v.playbackRate=x},
  play(){if(!active())return v.play();if(api.ended)time=0;paused=false;last=performance.now();sync();return Promise.resolve()},
  pause(){paused=true;v.pause();for(const a of sounds.values())a.pause();if(active())icon()},
  configure(m){paused=true;time=(m?.us??shown??0)/1e6;videoId=null;for(const a of sounds.values())a.pause();sounds.clear();if(m){sourceFrames=m.frames;sourceUrl=m.src;}v.style.visibility='visible';v.muted=false;
   if(active()){
    const projected=new Set([0]);for(const c of doc.timeline.clips.filter(c=>c.track==='video')){projected.add(c.startUs);projected.add(EditModel.end(c));for(const t of sourceFrames)if(t>=c.sourceUs&&t<c.sourceUs+c.durationUs)projected.add(c.startUs+t-c.sourceUs)}
    // Frame stepping also works across silent/black gaps.
    for(let t=0;t<doc.durationUs;t+=33333)if(!doc.timeline.clips.some(c=>c.track==='video'&&t>=c.startUs&&t<EditModel.end(c)))projected.add(t);
    frames=[...projected].filter(t=>t<doc.durationUs).sort((a,b)=>a-b);time=Math.min(time,(doc.durationUs-1)/1e6);sync();
   }else if(sourceFrames.length){frames=sourceFrames;if(v.readyState)v.currentTime=time;}
   $('duration').textContent=fmt(doc?.durationUs??0);$('seek').max=doc?.durationUs??0;window.refreshTimeline?.();
  },active
 };return api;
})();
