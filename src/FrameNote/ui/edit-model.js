'use strict';
// Pure edit operations shared by the UI and regression tests. All times are integer microseconds.
const EditModel=(()=>{
 const uid=()=>crypto.randomUUID(),end=c=>c.startUs+c.durationUs;
 const clamp=(value,{min,max})=>Math.max(min,Math.min(max,Math.round(value)));
 function clipBounds(doc,clip,action){
  const stop=end(clip);
  if(action==='left')return {min:Math.max(0,clip.startUs-clip.sourceUs),max:stop-1};
  if(action==='right'){
   const sourceDuration=clip.track==='voice'?clip.sourceDurationUs:doc.timeline.sourceDurationUs;
   return {min:clip.startUs+1,max:Math.min(doc.durationUs,clip.startUs+sourceDuration-clip.sourceUs)};
  }
  return {min:0,max:doc.durationUs-clip.durationUs};
 }
 function joinStarts(timeline,target,durationUs){
  if(!timeline||!target||target.track==='note')return null;
  const track=timeline.clips.filter(c=>c.track===target.track).sort((a,b)=>a.startUs-b.startUs);
  const index=track.findIndex(c=>c.id===target.id);
  if(index<0)return null;
  const clip=track[index];
  const previous=index===0?0:end(track[index-1]);
  const next=(track[index+1]?.startUs??durationUs)-clip.durationUs;
  return {
   previous:previous!==clip.startUs&&previous+clip.durationUs<=durationUs?previous:null,
   next:next!==clip.startUs&&next>=0?next:null
  };
 }
 function noteMoveBounds(doc,n){
  let first=n.startUs,last=n.endUs??n.startUs+1;
  for(const c of doc.timeline?.clips??[])if(c.track==='voice'&&c.noteId===n.id){
   first=Math.min(first,c.startUs);last=Math.max(last,end(c));
  }
  return {min:n.startUs-first,max:n.startUs+doc.durationUs-last};
 }
 // Forbidden displacement intervals are open: both touching edges remain valid drops.
 function moveObstacles(timeline,source){
  if(source.track==='voice')return [];
  const blocked=[];
  for(const track of ['video','audio']){
   const moving=[],fixed=[];
   for(const c of timeline.clips.filter(c=>c.track===track)){
    const a=Math.max(c.startUs,source.startUs),b=Math.min(end(c),end(source));
    if((c.id!==source.id&&!timeline.linked)||a>=b){fixed.push([c.startUs,end(c)]);continue}
    moving.push([a,b]);
    if(c.startUs<a)fixed.push([c.startUs,a]);
    if(b<end(c))fixed.push([b,end(c)]);
   }
   // Adjacent moving pieces can be treated as one continuous span.
   function spans(parts){
    const result=[];
    for(const [a,b] of parts.sort((x,y)=>x[0]-y[0])){
     if(result.length&&a===result.at(-1)[1])result.at(-1)[1]=b;else result.push([a,b]);
    }
    return result;
   }
   const stops=spans(fixed);
   for(const [a,b] of spans(moving))for(const [x,y] of stops)blocked.push([x-b,y-a]);
  }
  const merged=[];
  for(const [a,b] of blocked.sort((x,y)=>x[0]-y[0])){
   if(merged.length && a<merged.at(-1)[1])merged.at(-1)[1]=Math.max(b,merged.at(-1)[1]);
   else merged.push([a,b]);
  }
  return merged;
 }
 function fitMove(start,value,obstacles){
  const delta=value-start,hit=obstacles.find(([a,b])=>a<delta&&delta<b);
  return hit?start+(delta>0?hit[0]:hit[1]):value;
 }
 // Use the pointer on the original layout so moving the preview cannot change the target.
 function insertionEdge(neighbors,clip,pointer,blocked){
  if(!blocked&&!neighbors.some(c=>c.startUs<=pointer&&pointer<=end(c)))return null;
  if(pointer<clip.startUs){
   const crossed=neighbors.find(c=>c.startUs<clip.startUs&&pointer<=c.startUs+c.durationUs/2);
   return crossed?.startUs??null;
  }
  if(pointer>end(clip)){
   const crossed=neighbors.findLast(c=>c.startUs>=end(clip)&&pointer>=c.startUs+c.durationUs/2);
   return crossed?end(crossed):null;
  }
  return null;
 }
 function noteRange(note,edge,value,durationUs){
  if(note.voice)throw Error('语音批注的时长由录音决定。');
  const at=Math.max(0,Math.min(durationUs,Math.round(value)));
  const anchor=edge==='left'?(note.endUs??note.startUs):note.startUs;
  const start=edge==='left'?Math.min(at,anchor):anchor,stop=edge==='right'?Math.max(at,anchor):anchor;
  return start===stop?{startUs:Math.min(start,durationUs-1),endUs:null}:{startUs:start,endUs:stop};
 }
 function initial(doc){return doc.timeline?structuredClone(doc.timeline):{sourceDurationUs:doc.durationUs,linked:true,clips:[...['video','audio'].map(track=>({id:uid(),track,startUs:0,sourceUs:0,durationUs:doc.durationUs})),...doc.notes.filter(n=>n.voice).map(n=>({id:uid(),track:'voice',startUs:n.startUs,sourceUs:0,durationUs:Math.min(n.voice.durationUs,(n.endUs??doc.durationUs)-n.startUs),asset:n.voice.asset,sourceDurationUs:n.voice.durationUs,noteId:n.id}))]}}
 // A linked edit affects only the part of each clip inside the selected interval.
 // Temporary drag pieces reuse their source ID. Only committed cuts need new IDs.
 function clipPieces(c,action,start,stop,value,newId){
  const left=action==='left',right=action==='right',tail=end(c);
  if(left&&value<start || right&&value>stop){
   if(left&&c.startUs===start){
    const delta=value-start;
    return [{...c,startUs:value,sourceUs:c.sourceUs+delta,durationUs:c.durationUs-delta}];
   }
   if(right&&tail===stop)return [{...c,durationUs:value-c.startUs}];
   return [c];
  }
  const a=right?value:start,b=left?value:stop;
  if(a>=b||c.startUs>=b||tail<=a||action==='move'&&value===start)return [c];
  const cuts=[c.startUs,...[a,b].filter(t=>t>c.startUs&&t<tail),tail];
  return cuts.slice(0,-1).flatMap((from,i)=>{
   const to=cuts[i+1],inside=from>=a&&to<=b;
   if(inside&&action!=='move')return [];
   return [{...c,id:i&&newId&&!(action==='left'&&c.startUs>=a)?newId():c.id,startUs:from+(inside?value-start:0),
    sourceUs:c.sourceUs+from-c.startUs,durationUs:to-from}];
  });
 }
 // A voice edit changes one recording piece. Its Region covers all pieces of the same note.
 function editVoiceClip(timeline,source,action,value){
  const [clip]=clipPieces(source,action,source.startUs,end(source),value);
  let startUs=clip.startUs,endUs=end(clip);
  if(source.noteId)for(const other of timeline.clips){
   if(other.track!=='voice'||other.noteId!==source.noteId||other.id===source.id)continue;
   startUs=Math.min(startUs,other.startUs);endUs=Math.max(endUs,end(other));
  }
  return {clip,range:{startUs,endUs}};
 }
 // Store only material removed by a linked edge trim, in the owner's source
 // coordinates. Moving the owner changes its timeline origin, not these ranges.
 function trimOwner(t,clip){
  if(clip.track==='audio'&&t.linked)return t.clips.find(c=>c.track==='video'&&c.startUs===clip.startUs&&c.sourceUs===clip.sourceUs&&c.durationUs===clip.durationUs)??clip;
  return clip;
 }
 function storeTrim(doc,owner,a,b){
  const saved=doc.timeline.trimmed??=[],offset=owner.sourceUs-owner.startUs;
  for(const n of doc.notes){
   const start=Math.max(a,n.startUs),stop=Math.min(b,n.endUs??n.startUs+1);
   if(start<stop)saved.push({clipId:owner.id,note:{...n,startUs:start+offset,endUs:n.endUs==null?null:stop+offset}});
  }
  for(const c of doc.timeline.clips.filter(c=>c.track==='voice')){
   const start=Math.max(a,c.startUs),stop=Math.min(b,end(c));
   if(start<stop)saved.push({clipId:owner.id,voice:{...c,startUs:start+offset,sourceUs:c.sourceUs+start-c.startUs,durationUs:stop-start}});
  }
  if(saved.length){
   // Repeated drags of the same handle must restore one Region/recording.
   const ordered=saved.sort((a,b)=>a.clipId.localeCompare(b.clipId)||(a.note??a.voice).id.localeCompare((b.note??b.voice).id)||(a.note??a.voice).startUs-(b.note??b.voice).startUs);
   const merged=[];
   for(const part of ordered){
    const prev=merged.at(-1);
    if(prev?.clipId===part.clipId&&part.note&&prev.note?.id===part.note.id&&prev.note.endUs===part.note.startUs)
     prev.note.endUs=part.note.endUs;
    else if(prev?.clipId===part.clipId&&part.voice&&prev.voice?.id===part.voice.id&&end(prev.voice)===part.voice.startUs&&prev.voice.sourceUs+prev.voice.durationUs===part.voice.sourceUs)
     prev.voice.durationUs+=part.voice.durationUs;
    else merged.push(part);
   }
   doc.timeline.trimmed=merged;
  }
 }
 function restoreTrim(doc,owner,a,b){
  const t=doc.timeline,offset=owner.sourceUs-owner.startUs;
  if(!t.trimmed?.length)return;
  const remaining=[];
  for(const entry of t.trimmed){
   const item=entry.note??entry.voice,start=item.startUs,stop=entry.note?(item.endUs??start+1):end(item);
   const x=Math.max(a+offset,start),y=Math.min(b+offset,stop);
   if(entry.clipId!==owner.id||x>=y){remaining.push(entry);continue}
   function slice(from,to){
    return entry.note?{...entry,note:{...item,startUs:from,endUs:item.endUs==null?null:to}}:
     {...entry,voice:{...item,startUs:from,sourceUs:item.sourceUs+from-start,durationUs:to-from}};
   }
   if(start<x)remaining.push(slice(start,x));
   if(y<stop)remaining.push(slice(y,stop));
   const part=slice(x,y);
   if(part.note){
    const n=part.note;n.startUs-=offset;if(n.endUs!=null)n.endUs-=offset;
    const prev=doc.notes.find(p=>p.id===n.id);
    if(prev&&prev.endUs!=null&&n.endUs!=null&&(prev.startUs===n.endUs||prev.endUs===n.startUs)){
     prev.startUs=Math.min(prev.startUs,n.startUs);prev.endUs=Math.max(prev.endUs,n.endUs);
    }else{
     if(prev)n.id=uid();
     if(doc.notes.some(p=>p.number===n.number))n.number=++doc.lastNoteNumber;
     doc.notes.push(n);
    }
   }else{
    const c=part.voice;c.startUs-=offset;
    const prev=t.clips.find(p=>p.id===c.id);
    if(prev&&prev.asset===c.asset&&prev.sourceUs-prev.startUs===c.sourceUs-c.startUs&&(end(prev)===c.startUs||end(c)===prev.startUs)){
     prev.startUs=Math.min(prev.startUs,c.startUs);prev.sourceUs=Math.min(prev.sourceUs,c.sourceUs);prev.durationUs+=c.durationUs;
    }else{if(prev)c.id=uid();t.clips.push(c)}
   }
  }
  t.trimmed=remaining;
 }
 function execute(original,target,action,value){
  if(target?.track==='note'&&action==='move'){
   const note=original.notes.find(n=>n.id===target.id);
   if(note?.endUs!=null){
    const {min,max}=noteMoveBounds(original,note);
    const startUs=Math.max(min,Math.min(max,Math.round(value))),delta=startUs-note.startUs;
    if(!delta)return original;
    const notes=original.notes.map(n=>n===note?{...n,startUs,endUs:n.endUs+delta}:n);
    const timeline=original.timeline;
    if(!timeline)return {...original,notes};
    const clips=timeline.clips.map(c=>c.track==='voice'&&c.noteId===note.id?{...c,startUs:c.startUs+delta}:c);
    return {...original,notes,timeline:{...timeline,clips}};
   }
  }
  if(target?.track==='note'&&(action==='left'||action==='right')){
   const note=original.notes.find(n=>n.id===target.id);
   if(!note)throw Error('请先选择要编辑的批注。');
   const range=noteRange(note,action,value,original.durationUs);
   return {...original,notes:original.notes.map(n=>n===note?{...n,...range}:n)};
  }
  const d=structuredClone(original);d.timeline=initial(d);d.version=3;
  const t=d.timeline, noteTarget=target?.track==='note';
  const source=noteTarget?d.notes.find(n=>n.id===target.id):t.clips.find(c=>c.id===target?.id);
  if(!source&&action!=='split')throw Error('请先选择要编辑的片段。');
  if(noteTarget&&action==='delete'&&t.trimmed)
   t.trimmed=t.trimmed.filter(part=>(part.note?.id??part.voice?.noteId)!==source.id);
  const start=source?.startUs??value,stop=source?(noteTarget?(source.endUs??source.startUs+1):end(source)):value;
  const joining=action==='join';
  if(joining){
   const starts=joinStarts(t,target,d.durationUs);
   value=value==='next'?starts?.next:starts?.previous;
   if(value==null)return original;
   action='move';
  }
  if(noteTarget&&source.endUs===null){
   if(action==='move'){
    source.startUs=clamp(value,noteMoveBounds(d,source));
    if(source.startUs===start)return original;
   }else if(action==='delete')d.notes=d.notes.filter(n=>n.id!==source.id);
   validate(d);return d;
  }
  if(!noteTarget&&['move','left','right'].includes(action)){
   value=clamp(value,clipBounds(d,source,action));
   if(action==='move'&&!joining)value=fitMove(start,value,moveObstacles(t,source));
   if(value===(action==='right'?stop:start))return original;
   if(source.track==='voice'){
    const {clip,range}=editVoiceClip(t,source,action,value);
    Object.assign(source,clip);
    const note=d.notes.find(n=>n.id===source.noteId);
    if(note)Object.assign(note,range);
    validate(d);return d;
   }
  }
  const linked=t.linked;
  // Keep the adjacent clip in place even on a voice track with overlapping clips.
  const affects=c=>(!noteTarget&&c.id===source?.id)||(linked&&(!joining||c.track!==source.track));
  const affectsNote=n=>linked||(noteTarget&&n.id===source.id);
  function notePiece(n,a,b,identity){return {...n,id:identity?n.id:uid(),number:identity?n.number:++d.lastNoteNumber,startUs:a,endUs:n.endUs===null?null:b}}
  function editNotes(a,b,delta,remove){
   d.notes=d.notes.flatMap(n=>{
    if(!affectsNote(n))return [n];
    if(n.endUs===null)return n.startUs>=a&&n.startUs<b?(remove?[]:[{...n,startUs:n.startUs+delta}]):[n];
    const cuts=[n.startUs,...[a,b].filter(x=>x>n.startUs&&x<n.endUs),n.endUs].sort((x,y)=>x-y);
    let first=true;return cuts.slice(0,-1).flatMap((x,i)=>{const y=cuts[i+1],inside=x>=a&&y<=b;if(inside&&remove)return [];const p=notePiece(n,x+(inside?delta:0),y+(inside?delta:0),first);first=false;return [p]});
   });
  }
  const trim=linked&&!noteTarget&&source?.track!=='voice'&&(action==='left'||action==='right');
  const inward=action==='left'?value>start:value<stop;
  const owner=trim?trimOwner(t,source):null;
  if(trim&&inward)storeTrim(d,owner,action==='left'?start:value,action==='left'?value:stop);
  if(['move','left','right','delete'].includes(action))
   t.clips=t.clips.flatMap(c=>affects(c)&&!(trim&&!inward&&c.track==='voice')?clipPieces(c,action,start,stop,value,uid):[c]);
  function splitAt(at){
   const rightClips=new Map(),rightNotes=new Map();
   t.clips=t.clips.flatMap(c=>{
    if(!affects(c)||at<=c.startUs||at>=end(c))return [c];
    const right={...c,id:uid(),startUs:at,sourceUs:c.sourceUs+at-c.startUs,durationUs:end(c)-at};
    rightClips.set(c.id,right);
    return [{...c,durationUs:at-c.startUs},right];
   });
   d.notes=d.notes.flatMap(n=>{
    if(!affectsNote(n)||n.endUs==null||at<=n.startUs||at>=n.endUs)return [n];
    const right=notePiece(n,at,n.endUs,false);rightNotes.set(n.id,right);
    return [{...n,endUs:at},right];
   });
   for(const c of t.clips)if(c.track==='voice'&&c.startUs>=at&&rightNotes.has(c.noteId))c.noteId=rightNotes.get(c.noteId).id;
   for(const part of t.trimmed??[]){
    const owner=rightClips.get(part.clipId);
    if(!owner||(part.note??part.voice).startUs<owner.sourceUs)continue;
    part.clipId=owner.id;
    const note=rightNotes.get(part.note?.id??part.voice?.noteId);
    if(part.note&&note){part.note.id=note.id;part.note.number=note.number}
    if(part.voice){
     if(note)part.voice.noteId=note.id;
     part.voice.id=rightClips.get(part.voice.id)?.id??part.voice.id;
    }
   }
  }
  if(action==='split')splitAt(Math.round(value));
  else if(action==='insert'){
   if(!['video','audio'].includes(source.track)||!(value<start||value>stop)||
      !t.clips.some(c=>c.track===source.track&&(value<start?c.startUs:end(c))===value))return original;
   // Rotate the selected interval and the intervening time, including any gaps.
   // Split linked material only where neighboring time ranges will move apart.
   if(linked)for(const cut of [value,start,stop].sort((a,b)=>a-b))splitAt(cut);
   const length=stop-start,left=value<start;
   function shift(at){
    if(start<=at&&at<stop)return left?value-start:value-stop;
    if(left&&value<=at&&at<start)return length;
    if(!left&&stop<=at&&at<value)return -length;
    return 0;
   }
   for(const c of t.clips)if(linked||c.track===source.track)c.startUs+=shift(c.startUs);
   if(linked)for(const n of d.notes){
    const delta=shift(n.startUs);n.startUs+=delta;if(n.endUs!=null)n.endUs+=delta;
   }
  }else if(action==='move')editNotes(start,stop,Math.round(value)-start,false);
  else if(action==='delete')editNotes(start,stop,0,true);
  else if(action==='left'||action==='right'){
   const edge=Math.round(value),left=action==='left';
   if(left?edge>=stop:edge<=start)throw Error('片段必须保留正时长。');
   if(left?edge>=start:edge<=stop)editNotes(left?start:edge,left?edge:stop,0,true);
   else if(trim)restoreTrim(d,owner,left?edge:stop,left?start:edge);
  }
  if(t.trimmed?.length){const ids=new Set(t.clips.map(c=>c.id));t.trimmed=t.trimmed.filter(e=>ids.has(e.clipId))}
  d.durationUs=Math.max(d.durationUs,...t.clips.map(end),...d.notes.map(n=>n.endUs??n.startUs+1));
  validate(d);return d;
 }
 function validate(d){
  if(d.durationUs>604800000000||d.timeline.clips.length>2000||d.notes.length>10000||(d.timeline.trimmed?.length??0)>20000)throw Error('时间线或片段数量超出限制。');
  for(const c of d.timeline.clips)if(c.startUs<0||c.sourceUs<0||c.durationUs<=0||c.sourceUs+c.durationUs>(c.track==='voice'?c.sourceDurationUs:d.timeline.sourceDurationUs))throw Error('不能超出原始素材范围或移动到时间线起点之前。');
  if(d.notes.some(n=>n.startUs<0))throw Error('联动批注不能移到时间线起点之前。');
  for(const track of ['video','audio']){let last=0;for(const c of d.timeline.clips.filter(c=>c.track===track).sort((a,b)=>a.startUs-b.startUs)){if(c.startUs<last)throw Error('片段发生重叠，请先移到空白处或吸附到相邻边缘。');last=end(c)}}
 }
 function snap(value,length,edges,tolerance,bounds){
  let result=value,distance=tolerance;
  for(const edge of edges)for(const candidate of [edge,edge-length]){
   if(bounds&&(candidate<bounds.min||candidate>bounds.max))continue;
   const d=Math.abs(candidate-value);
   if(d<=distance){result=candidate;distance=d}
  }
  return Math.round(result);
 }
 return {initial,execute,validate,snap,end,clamp,clipBounds,clipPieces,editVoiceClip,noteMoveBounds,noteRange,joinStarts,moveObstacles,fitMove,insertionEdge};
})();
if(typeof module!=='undefined')module.exports=EditModel;
