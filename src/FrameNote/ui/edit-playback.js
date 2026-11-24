'use strict';
// Do not interrupt the audio device to chase normal decoder startup or output latency.
const mediaSyncState=new WeakMap();
function syncClipMedia(a,offset,rate,volume,playing,reposition){
 let state=mediaSyncState.get(a);
 if(!state){state={driftSince:null,checkAfter:0,starting:false};mediaSyncState.set(a,state);reposition=true}
 if(a.volume!==volume)a.volume=volume;
 if(a.playbackRate!==rate)a.playbackRate=rate;
 const now=performance.now(),error=Math.abs(a.currentTime-offset);
 if(playing&&!reposition&&!a.paused&&!a.seeking&&a.readyState>=3&&now>=state.checkAfter&&error>.18*rate){
  state.driftSince??=now;
  reposition=now-state.driftSince>=400;
 }else state.driftSince=null;
 if(reposition){
  if(error>.000001)a.currentTime=offset;
  state.driftSince=null;state.checkAfter=now+1000;
 }
 if(!playing){if(!a.paused)a.pause();return}
 if(a.paused&&!a.ended&&!a.error&&!state.starting){
  state.starting=true;
  a.play().catch(()=>{}).finally(()=>{state.starting=false});
 }
}
function releaseAudio(a){
 a.pause();a.removeAttribute('src');a.load();
}
const transport=(()=>{
 let paused=true,time=0,rate=1,last=0,sourceFrames=[],sourceUrl='',videoClip=null;
 let clockMedia=null,clockClip=null;
 const sounds=new Map(),active=()=>!!doc?.timeline;
 function icon(){const stop=api.paused;setIconAction('play',stop?'play':'pause',stop?tr('播放'):tr('暂停'),shortcutHint(stop?tr('播放'):tr('暂停'),'play'))}
 function audio(c,us,live,reposition){
  let a=sounds.get(c.id);const url=c.track==='voice'?imageBase+c.asset+'.m4a':sourceUrl;
  if(a&&a.getAttribute('src')!==url){releaseAudio(a);sounds.delete(c.id);a=null}
  if(!a){if(!live)return;a=new Audio(url);sounds.set(c.id,a)}
  syncClipMedia(a,(c.sourceUs+us-c.startUs)/1e6,rate,v.volume,live,reposition);
 }
 function sync(reposition=false){
  if(!active())return;
  const us=Math.round(time*1e6),clips=doc.timeline.clips;
  const c=clips.find(c=>c.track==='video'&&us>=c.startUs&&us<EditModel.end(c));
  // Keep an independent audio decoder for a clip spanning a video cut or gap.
  const original=clips.find(a=>a.track==='audio'&&a.startUs<=us&&us<EditModel.end(a));
  const embedded=!!(c&&original&&original.startUs>=c.startUs&&EditModel.end(original)<=EditModel.end(c)&&
   original.sourceUs-original.startUs===c.sourceUs-c.startUs);
  const muteVideo=!embedded;
  if(v.muted!==muteVideo)v.muted=muteVideo;
  if(c){
   const offset=(c.sourceUs+us-c.startUs)/1e6;
   const cut=!videoClip||c.sourceUs-c.startUs!==videoClip.sourceUs-videoClip.startUs;
   syncClipMedia(v,offset,rate,v.volume,!paused,reposition||cut);
  }else if(!v.paused)v.pause();
  if(!!videoClip!==!!c)v.style.visibility=c?'visible':'hidden';
  videoClip=c;
  const ids=new Set();
  clockMedia=c?v:null;clockClip=c;
  if(original&&!embedded){
   ids.add(original.id);audio(original,us,!paused,reposition);
   const a=sounds.get(original.id);
   if(a&&!a.error){clockMedia=a;clockClip=original}
  }
  const live=!paused&&!window.voiceActive?.()&&(!clockMedia||(!clockMedia.seeking&&clockMedia.readyState>=3));
  for(const clip of clips)if(clip.track==='voice'&&us>=clip.startUs&&us<EditModel.end(clip)){
   ids.add(clip.id);audio(clip,us,live,reposition);
  }
  for(const [id,a] of sounds)if(!ids.has(id)){releaseAudio(a);sounds.delete(id)}
  shown=Math.round(us);if(!scrubber.active())showPlayhead(shown);paintPlayback();icon();
 }
 function step(now){
  if(active()&&ready){
   if(!paused){
    // Detached source audio can run uninterrupted across video cuts and black frames.
    if(clockMedia&&!clockMedia.ended&&!clockMedia.error){
     if(!clockMedia.paused&&!clockMedia.seeking)time=Math.max(time,Math.min(EditModel.end(clockClip)/1e6,clockMedia.currentTime+(clockClip.startUs-clockClip.sourceUs)/1e6));
    }else time+=Math.max(0,(now-last)/1000)*rate;
    const n=loopId&&doc.notes.find(n=>n.id===loopId),loop=n&&time*1e6>=n.endUs;
    if(loop)time=n.startUs/1e6;
    if(time>=doc.durationUs/1e6){time=doc.durationUs/1e6;paused=true}
    sync(!!loop);window.syncNoteVoices?.();
   }
  }
  last=now;requestAnimationFrame(step);
 }
 requestAnimationFrame(step);
 const api={
  get currentTime(){return active()?time:v.currentTime},set currentTime(x){if(active()){time=Math.max(0,Math.min(doc.durationUs/1e6,x));sync(true)}else v.currentTime=x},
  get paused(){return active()?paused:v.paused},get ended(){return active()?time>=doc.durationUs/1e6:v.ended},
  get playbackRate(){return active()?rate:v.playbackRate},set playbackRate(x){rate=x;v.playbackRate=x},
  play(){if(!active())return v.play();const restart=api.ended;if(restart)time=0;paused=false;last=performance.now();sync(restart);return Promise.resolve()},
  pause(){
   paused=true;v.pause();
   if(active()){for(const a of sounds.values())a.pause();icon()}
   else{for(const a of sounds.values())releaseAudio(a);sounds.clear()}
   window.syncNoteVoices?.();
  },
  configure(m){scrubber.reset();paused=true;time=(m?.us??shown??0)/1e6;videoClip=null;for(const a of sounds.values())releaseAudio(a);sounds.clear();if(m){sourceFrames=m.frames;sourceUrl=m.src;}v.style.visibility=active()?'hidden':'visible';if(v.muted)v.muted=false;
   if(active()){
    const projected=new Set([0]);for(const c of doc.timeline.clips.filter(c=>c.track==='video')){projected.add(c.startUs);projected.add(EditModel.end(c));for(const t of sourceFrames)if(t>=c.sourceUs&&t<c.sourceUs+c.durationUs)projected.add(c.startUs+t-c.sourceUs)}
    // Frame stepping also works across silent/black gaps.
    for(let t=0;t<doc.durationUs;t+=33333)if(!doc.timeline.clips.some(c=>c.track==='video'&&t>=c.startUs&&t<EditModel.end(c)))projected.add(t);
    frames=[...projected].filter(t=>t<doc.durationUs).sort((a,b)=>a-b);time=Math.min(time,(doc.durationUs-1)/1e6);sync();
   }else if(sourceFrames.length){frames=sourceFrames;if(v.readyState)v.currentTime=time;}
   $('duration').textContent=fmt(doc?.durationUs??0);$('seek').max=doc?.durationUs??0;window.refreshTimeline?.();
  },active,
  sourceFrameAt(us){
   if(imageDoc())return 0;
   if(active()){
    const clip=doc.timeline.clips.find(c=>c.track==='video'&&us>=c.startUs&&us<EditModel.end(c));
    if(!clip)return -1-(frames[index(us)]??us);
    us=clip.sourceUs+us-clip.startUs;
   }
   let lo=0,hi=sourceFrames.length;
   while(lo<hi){const mid=(lo+hi)>>>1;if(sourceFrames[mid]<=us+2)lo=mid+1;else hi=mid}
   return sourceFrames[Math.max(0,lo-1)]??0;
  }
 };return api;
})();
