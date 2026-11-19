'use strict';
// Pure edit operations shared by the UI and regression tests. All times are integer microseconds.
const EditModel=(()=>{
 const uid=()=>crypto.randomUUID(),end=c=>c.startUs+c.durationUs;
 function initial(doc){return doc.timeline?structuredClone(doc.timeline):{sourceDurationUs:doc.durationUs,linked:true,clips:[...['video','audio'].map(track=>({id:uid(),track,startUs:0,sourceUs:0,durationUs:doc.durationUs})),...doc.notes.filter(n=>n.voice).map(n=>({id:uid(),track:'voice',startUs:n.startUs,sourceUs:0,durationUs:Math.min(n.voice.durationUs,(n.endUs??doc.durationUs)-n.startUs),asset:n.voice.asset,sourceDurationUs:n.voice.durationUs,noteId:n.id}))]}}
 function execute(original,target,action,value){
  const d=structuredClone(original);d.timeline=initial(d);d.version=3;
  const t=d.timeline, noteTarget=target?.track==='note';
  const source=noteTarget?d.notes.find(n=>n.id===target.id):t.clips.find(c=>c.id===target?.id);
  if(!source&&action!=='split')throw Error('请先选择要编辑的片段。');
  const start=source?.startUs??value,stop=source?(noteTarget?(source.endUs??source.startUs+1):end(source)):value;
  if(noteTarget&&source.endUs===null){if(action==='move')source.startUs=Math.round(value);else if(action==='delete')d.notes=d.notes.filter(n=>n.id!==source.id);else if(action!=='split')throw Error('Mark 没有时长，请先将它改为 Region。');d.durationUs=Math.max(d.durationUs,source.startUs+1);validate(d);return d;}
  const linked=t.linked,affects=c=>linked||(!noteTarget&&c.id===source?.id),affectsNote=n=>linked||(noteTarget&&n.id===source.id);
  function notePiece(n,a,b,identity){return {...n,id:identity?n.id:uid(),number:identity?n.number:++d.lastNoteNumber,startUs:a,endUs:n.endUs===null?null:b}}
  function pieces(c,a,b){const cuts=[c.startUs,...[a,b].filter(x=>x>c.startUs&&x<end(c)),end(c)].sort((x,y)=>x-y);return cuts.slice(0,-1).map((x,i)=>({...c,id:i?uid():c.id,startUs:x,sourceUs:c.sourceUs+x-c.startUs,durationUs:cuts[i+1]-x}))}
  function range(a,b,delta,remove){
   t.clips=t.clips.flatMap(c=>!affects(c)?[c]:pieces(c,a,b).flatMap(p=>p.startUs>=a&&end(p)<=b?(remove?[]:[{...p,startUs:p.startUs+delta}]):[p]));
   d.notes=d.notes.flatMap(n=>{
    if(!affectsNote(n))return [n];
    if(n.endUs===null)return n.startUs>=a&&n.startUs<b?(remove?[]:[{...n,startUs:n.startUs+delta}]):[n];
    const cuts=[n.startUs,...[a,b].filter(x=>x>n.startUs&&x<n.endUs),n.endUs].sort((x,y)=>x-y);
    let first=true;return cuts.slice(0,-1).flatMap((x,i)=>{const y=cuts[i+1],inside=x>=a&&y<=b;if(inside&&remove)return [];const p=notePiece(n,x+(inside?delta:0),y+(inside?delta:0),first);first=false;return [p]});
   });
  }
  if(action==='split'){
   const at=Math.round(value);
   t.clips=t.clips.flatMap(c=>affects(c)&&at>c.startUs&&at<end(c)?[{...c,durationUs:at-c.startUs},{...c,id:uid(),startUs:at,sourceUs:c.sourceUs+at-c.startUs,durationUs:end(c)-at}]:[c]);
   d.notes=d.notes.flatMap(n=>affectsNote(n)&&n.endUs!==null&&at>n.startUs&&at<n.endUs?[{...n,endUs:at},notePiece(n,at,n.endUs,false)]:[n]);
  }else if(action==='move')range(start,stop,Math.round(value)-start,false);
  else if(action==='delete')range(start,stop,0,true);
  else if(action==='left'||action==='right'){
   const edge=Math.round(value),left=action==='left';
   if(noteTarget&&source.endUs===null)throw Error('Mark 没有时长，请先将它改为 Region。');
   if(left?edge>=stop:edge<=start)throw Error('片段必须保留正时长。');
   if(left?edge>=start:edge<=stop)range(left?start:edge,left?edge:stop,0,true);
   else{
    for(const c of t.clips)if(affects(c)&&(left?c.startUs===start:end(c)===stop)){
     const delta=left?edge-start:edge-stop;if(left){c.startUs+=delta;c.sourceUs+=delta;c.durationUs-=delta}else c.durationUs+=delta;
    }
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
 function snap(value,length,edges,tolerance){let result=value,distance=tolerance;for(const edge of edges)for(const candidate of [edge,edge-length]){const d=Math.abs(candidate-value);if(d<=distance){result=candidate;distance=d}}return Math.round(result)}
 return {initial,execute,validate,snap,end};
})();
if(typeof module!=='undefined')module.exports=EditModel;
