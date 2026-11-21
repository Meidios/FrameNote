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
   return [{...c,id:i&&newId?newId():c.id,startUs:from+(inside?value-start:0),
    sourceUs:c.sourceUs+from-c.startUs,durationUs:to-from}];
  });
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
   if(value===(action==='right'?stop:start))return original;
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
  if(['move','left','right','delete'].includes(action))
   t.clips=t.clips.flatMap(c=>affects(c)?clipPieces(c,action,start,stop,value,uid):[c]);
  if(action==='split'){
   const at=Math.round(value);
   t.clips=t.clips.flatMap(c=>affects(c)&&at>c.startUs&&at<end(c)?[{...c,durationUs:at-c.startUs},{...c,id:uid(),startUs:at,sourceUs:c.sourceUs+at-c.startUs,durationUs:end(c)-at}]:[c]);
   d.notes=d.notes.flatMap(n=>affectsNote(n)&&n.endUs!==null&&at>n.startUs&&at<n.endUs?[{...n,endUs:at},notePiece(n,at,n.endUs,false)]:[n]);
  }else if(action==='move')editNotes(start,stop,Math.round(value)-start,false);
  else if(action==='delete')editNotes(start,stop,0,true);
  else if(action==='left'||action==='right'){
   const edge=Math.round(value),left=action==='left';
   if(left?edge>=stop:edge<=start)throw Error('片段必须保留正时长。');
   if(left?edge>=start:edge<=stop)editNotes(left?start:edge,left?edge:stop,0,true);
   else{
    for(const n of d.notes)if(affectsNote(n)&&n.endUs!==null&&(left?n.startUs===start:n.endUs===stop)){if(left)n.startUs=edge;else n.endUs=edge;}
   }
  }
  d.durationUs=Math.max(d.durationUs,...t.clips.map(end),...d.notes.map(n=>n.endUs??n.startUs+1));
  validate(d);return d;
 }
 function validate(d){
  if(d.durationUs>604800000000||d.timeline.clips.length>2000||d.notes.length>10000)throw Error('时间线或片段数量超出限制。');
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
 return {initial,execute,validate,snap,end,clamp,clipBounds,clipPieces,noteMoveBounds,noteRange,joinStarts};
})();
if(typeof module!=='undefined')module.exports=EditModel;
