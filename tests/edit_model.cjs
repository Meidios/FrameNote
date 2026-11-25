const assert=require('node:assert/strict');global.crypto=require('node:crypto').webcrypto;
const M=require('../src/FrameNote/ui/edit-model.js');
function checkClipGroups(){
 const piece=(track,startUs,durationUs)=>({id:crypto.randomUUID(),track,startUs,sourceUs:startUs,durationUs});
 const video=[piece('video',0,10),piece('video',10,10),piece('video',20,10),piece('video',30,10)];
 const audio=piece('audio',0,40);
 const note={id:crypto.randomUUID(),number:1,startUs:12,endUs:34,text:'feedback'};
 const base={version:3,durationUs:65,lastNoteNumber:1,notes:[note],
  timeline:{sourceDurationUs:40,linked:true,clips:[...video,audio]}};
 const chosen=video.slice(1,3),group=M.clipGroup(base,chosen,'move');
 const order=d=>d.timeline.clips.filter(c=>c.track==='video').sort((a,b)=>a.startUs-b.startUs).map(c=>c.sourceUs);
 const inserted=M.editGroup(group,'insert',40);
 assert.deepEqual(order(inserted),[0,30,10,20]);
 assert.deepEqual(inserted.timeline.clips.filter(c=>c.track==='audio').sort((a,b)=>a.startUs-b.startUs)
  .map(c=>[c.startUs,c.sourceUs,c.durationUs]),[[0,0,10],[10,30,10],[20,10,20]]);
 assert.deepEqual(inserted.notes.map(n=>[n.startUs,n.endUs]),[[22,40],[10,14]]);
 assert.equal(M.editGroup(group,'move',15),base);
 const moved=M.editGroup(group,'move',60);
 assert.deepEqual(moved.timeline.clips.filter(c=>chosen.some(v=>c.id===v.id)).map(c=>c.startUs),[45,55]);
 assert.deepEqual(moved.notes.map(n=>[n.startUs,n.endUs]),[[47,65],[30,34]]);
 const removed=M.editGroup(M.clipGroup(base,chosen,'delete'),'delete');
 assert.deepEqual(order(removed),[0,30]);
 assert.deepEqual(removed.notes,[{...note,startUs:30}]);
 assert.equal(base.timeline.clips.length,5);assert.equal(base.notes[0].startUs,12);

 const detached={...base,timeline:{...base.timeline,linked:false}};
 assert.deepEqual(M.editGroup(M.clipGroup(detached,chosen,'move'),'insert',0).timeline.clips.at(-1),audio);
 const separate=[video[0],video[2]];
 const apart=M.editGroup(M.clipGroup(detached,separate,'move'),'move',10);
 assert.equal(apart,detached);
 const gaps={...detached,timeline:{...detached.timeline,clips:[...separate,audio]}};
 const shifted=M.editGroup(M.clipGroup(gaps,separate,'move'),'move',7);
 assert.deepEqual(shifted.timeline.clips.map(c=>c.startUs),[7,27,0]);assert.deepEqual(shifted.notes,[note]);
 const both=[...video.slice(0,2),{...audio,durationUs:20}];
 const aligned={...base,timeline:{...base.timeline,clips:both}};
 assert.deepEqual(M.editGroup(M.clipGroup(aligned,both,'move'),'move',5).timeline.clips.map(c=>c.startUs),[5,15,5]);
 assert.deepEqual(M.editGroup(M.clipGroup(aligned,both,'delete'),'delete').timeline.clips,[]);

 const voice=(at)=>({...piece('voice',at,3),sourceUs:0,sourceDurationUs:3,noteId:note.id});
 const voices=[voice(12),voice(18),voice(31)];
 const withVoice={...base,timeline:{...base.timeline,clips:[...base.timeline.clips,...voices]}};
 const voiceMove=M.editGroup(M.clipGroup(withVoice,voices.slice(0,2),'move'),'move',20);
 assert.deepEqual(voiceMove.timeline.clips.filter(c=>c.track==='voice').map(c=>c.startUs),[20,26,31]);
 assert.deepEqual(voiceMove.notes.map(n=>[n.startUs,n.endUs]),[[20,34]]);
 assert.deepEqual(voiceMove.timeline.clips.filter(c=>c.track!=='voice'),base.timeline.clips);
 const detachedVoice={...withVoice,timeline:{...withVoice.timeline,linked:false}};
 const voiceDelete=M.editGroup(M.clipGroup(detachedVoice,voices.slice(0,2),'delete'),'delete');
 assert.deepEqual(voiceDelete.notes,detachedVoice.notes);
 assert.deepEqual(voiceDelete.timeline.clips,[...base.timeline.clips,voices[2]]);
}
checkClipGroups();
console.log('Clip groups preserve spacing, source ranges, linked notes, voice ownership, gaps and neighboring clips');
const id=()=>crypto.randomUUID(),U=1000000;
let d={version:1,durationUs:10*U,lastNoteNumber:1,notes:[{id:id(),number:1,startUs:2*U,endUs:8*U,text:'region'}]};d.timeline=M.initial(d);
d.durationUs=12*U; // Two seconds of empty timeline after the source.
const video=d.timeline.clips[0];
d=M.execute(d,{id:video.id,track:'video'},'split',5*U);assert.equal(d.timeline.clips.length,4);assert.deepEqual(d.notes.map(n=>[n.startUs,n.endUs]),[[2*U,5*U],[5*U,8*U]]);
const second=d.timeline.clips.find(c=>c.track==='video'&&c.startUs===5*U);
d=M.execute(d,second,'move',7*U);assert.equal(d.durationUs,12*U);assert.deepEqual(d.timeline.clips.filter(c=>c.startUs===7*U).map(c=>c.track),['video','audio']);assert.equal(d.notes[1].startUs,7*U);
d=M.execute(d,second,'left',8*U);assert.equal(d.timeline.clips.find(c=>c.track==='video'&&c.startUs===8*U).sourceUs,6*U);
d.timeline.linked=false;let a=d.timeline.clips.find(c=>c.track==='audio'&&c.startUs===8*U);d=M.execute(d,a,'delete');assert(d.timeline.clips.some(c=>c.track==='video'&&c.startUs===8*U));assert.equal(d.notes.length,2);
assert.equal(M.snap(5010000,U,[5*U],20000),5*U);assert.equal(M.snap(3990000,U,[5*U],20000),4*U);
assert.equal(M.execute(d,d.timeline.clips.find(c=>c.track==='video'&&c.startUs===8*U),'move',3*U).timeline.clips.find(c=>c.id===second.id).startUs,5*U);
console.log('linked split/move/trim, detached delete, gap preservation, edge snapping and overlap docking passed');
const mark={id:id(),number:3,startUs:U,endUs:null,text:'mark'};let z={...d,notes:[mark],lastNoteNumber:3};z.timeline.linked=true;let moved=M.execute(z,{id:mark.id,track:'note'},'move',3*U);assert.deepEqual(moved.timeline.clips,z.timeline.clips);assert.equal(moved.notes[0].startUs,3*U);
z.timeline.linked=false;z.timeline.clips.push({id:id(),track:'voice',startUs:2*U,sourceUs:0,durationUs:2*U,sourceDurationUs:3*U,asset:'a'.repeat(64)});const vc=z.timeline.clips.at(-1);moved=M.execute(z,vc,'move',6*U);assert.equal(moved.notes[0].startUs,U);assert.equal(moved.timeline.clips.at(-1).startUs,6*U);
const restored=M.execute(moved,moved.timeline.clips.at(-1),'right',9*U);assert.equal(restored.timeline.clips.at(-1).durationUs,3*U);
console.log('zero-duration Mark handling, detached voice movement and source-bound trim restoration passed');

const region={id:id(),startUs:2*U,endUs:4*U,voice:{asset:'b'.repeat(64),durationUs:2*U}};
const other={id:id(),startUs:5*U,endUs:7*U,text:'overlapping feedback'};
const review={version:1,durationUs:10*U,notes:[region,other]},target={track:'note',id:region.id};
for(const linked of [true,false]){
 const timeline=M.initial(review);timeline.linked=linked;
 const voice=timeline.clips.find(c=>c.track==='voice');
 const untouched=[...timeline.clips.filter(c=>c!==voice),{...voice,id:id(),noteId:other.id}];
 timeline.clips.push(untouched.at(-1));
 let state={...review,timeline};
 for(const [action,value,start,stop,offset] of [
  ['move',5,5,7,0],['left',6,6,7,1],['left',5,5,7,0],['right',6,5,6,0],['right',7,5,7,0]
 ]){
  state=M.execute(state,voice,action,value*U);
  assert.deepEqual(state.timeline.clips.filter(c=>c.id!==voice.id),untouched);
  assert.deepEqual(state.notes,[{...region,startUs:start*U,endUs:stop*U},other]);
  assert.deepEqual(state.timeline.clips.find(c=>c.id===voice.id),{...voice,startUs:start*U,durationUs:(stop-start)*U,sourceUs:offset*U});
 }
 // Detached splits can leave several pieces associated with one note.
 const sibling={...voice,id:id(),startUs:U,sourceUs:0,durationUs:U};
 state.timeline.clips.push(sibling);
 state=M.execute(state,voice,'right',6*U);
 assert.deepEqual(state.notes[0],{...region,startUs:U,endUs:6*U});
 assert.deepEqual(state.timeline.clips.find(c=>c.id===sibling.id),sibling);
}
console.log('Voice moves and reversible edge trims isolate the selected piece and keep its Region in sync');
const plain=M.execute(review,target,'move',5*U);
assert.equal(plain.timeline,undefined);
assert.deepEqual(plain.notes,[{...region,startUs:5*U,endUs:7*U},other]);
for(const linked of [true,false]){
 const timeline=M.initial(review);timeline.linked=linked;
 const attached=timeline.clips.find(c=>c.track==='voice');
 // A trimmed recording can have several pieces positioned independently of its Region.
 Object.assign(attached,{startUs:U,durationUs:U});
 timeline.clips.push({...attached,id:id(),startUs:6*U,sourceUs:U});
 timeline.clips.push({...attached,id:id(),noteId:other.id});
 const doc={...review,timeline};
 for(const [requested,expected] of [[-U,U],[5*U,5*U],[20*U,5*U]]){
  const next=M.execute(doc,target,'move',requested),delta=expected-region.startUs;
  assert.deepEqual(next.notes,[{...region,startUs:expected,endUs:expected+2*U},other]);
  assert.deepEqual(next.timeline.clips,timeline.clips.map(c=>c.noteId===region.id?{...c,startUs:c.startUs+delta}:c));
  assert.equal(next.durationUs,doc.durationUs);M.validate(next);
 }
 assert.equal(M.execute(doc,target,'move',region.startUs),doc);
}
console.log('Region moves preserve length, bound notes and attached voice pieces, and allow overlaps on both link settings');

const first={id:id(),track:'video',startUs:U,sourceUs:0,durationUs:2*U};
const last={id:id(),track:'video',startUs:6*U,sourceUs:4*U,durationUs:2*U};
const audio={...last,id:id(),track:'audio'};
for(const linked of [false,true]){
 const doc={version:3,durationUs:10*U,lastNoteNumber:0,notes:[],
  timeline:{linked,sourceDurationUs:10*U,clips:[last,audio,first]}};
 const join=M.execute(doc,last,'join');
 const clip=(d,c)=>d.timeline.clips.find(item=>item.id===c.id);
 assert.deepEqual(clip(join,last),{...last,startUs:3*U});
 assert.deepEqual(clip(join,audio),{...audio,startUs:linked?3*U:6*U});
 assert.equal(M.execute(join,last,'join'),join);
 const next=M.execute(doc,first,'join','next');
 assert.deepEqual(clip(next,first),{...first,startUs:last.startUs-first.durationUs});
 assert.deepEqual(clip(next,last),last);
 assert.equal(M.execute(next,first,'join','next'),next);
 const tail=M.execute(doc,last,'join','next');
 assert.deepEqual(clip(tail,last),{...last,startUs:8*U});
 assert.deepEqual(clip(tail,audio),{...audio,startUs:linked?8*U:6*U});
 assert.equal(M.execute(tail,last,'join','next'),tail);
 assert.equal(clip(M.execute(doc,first,'join'),first).startUs,0);
 assert.equal(clip(M.execute(doc,first,'move',-U),first).startUs,0);
 const atEnd=M.execute(doc,last,'move',12*U);
 assert.equal(M.end(clip(atEnd,last)),doc.durationUs);
 assert.equal(atEnd.durationUs,doc.durationUs);
 assert.equal(M.execute(atEnd,last,'move',12*U),atEnd);
}
const blocked={version:3,durationUs:10*U,lastNoteNumber:0,notes:[],
 timeline:{linked:true,sourceDurationUs:10*U,clips:[first,last,{...audio,startUs:3*U},audio]}};
const unchanged=structuredClone(blocked);
assert.throws(()=>M.execute(blocked,last,'join'),/重叠/);
assert.deepEqual(blocked,unchanged);
assert.equal(M.snap(-1,U,[0,2*U],10,{min:0,max:4*U}),0);
assert.equal(M.snap(2*U-10,U,[3*U],15,{min:0,max:4*U}),2*U);
console.log('Adjacent joins, zero and end bounds, linked movement and bounded snapping passed');

// Three packed clips with unequal lengths, independent of their storage order.
{
 const clip=(track,startUs,durationUs)=>({id:id(),track,startUs,sourceUs:startUs,durationUs});
 const [a,b,c]=[clip('video',0,3*U),clip('video',3*U,5*U),clip('video',8*U,2*U)];
 const mic={asset:'f'.repeat(64),durationUs:5*U};
 const region={id:id(),number:2,startUs:2*U,endUs:7*U,voice:mic,text:'take'};
 const voice={...clip('voice',2*U,5*U),sourceUs:0,sourceDurationUs:5*U,asset:mic.asset,noteId:region.id};
 const timeline={sourceDurationUs:10*U,linked:true,clips:[b,voice,c,clip('audio',0,10*U),a]};
 const base={version:3,timeline,durationUs:10*U,lastNoteNumber:2,
  notes:[{id:id(),number:1,startUs:9*U,endUs:null,text:'last'},region]};
 const byTime=(doc,track)=>doc.timeline.clips.filter(x=>x.track===track).sort((p,q)=>p.startUs-q.startUs);
 const spans=(doc,track)=>byTime(doc,track).map(x=>[x.sourceUs/U,x.startUs/U,x.durationUs/U]);
 assert.equal(M.insertionEdge([a,c],b,2.5*U,true),null);
 assert.equal(M.insertionEdge([a,c],b,1.5*U,true),0);
 assert.equal(M.insertionEdge([b,c],a,9*U,true),10*U);
 assert.equal(M.insertionEdge([a,b],c,4*U,true),3*U);
 const gapTarget={...a,startUs:2*U};
 assert.equal(M.insertionEdge([gapTarget],c,0,false),null);
 const front=M.execute(base,c,'insert',0);
 assert.deepEqual(byTime(front,'video').map(x=>x.id),[c.id,a.id,b.id]);
 assert.deepEqual(spans(front,'video'),[[8,0,2],[0,2,3],[3,5,5]]);
 assert.deepEqual(spans(front,'audio'),[[8,0,2],[0,2,8]]);
 assert.deepEqual(spans(front,'voice'),[[0,4,5]]);
 assert.equal(front.notes[0].startUs,U);
 assert.equal(front.notes[1].startUs,4*U);
 assert.equal(front.durationUs,base.durationUs);
 const back=M.execute(front,c,'insert',10*U);
 assert.deepEqual(byTime(back,'video'),[a,b,c]);
 assert.deepEqual(back.notes,base.notes);
 const swapped=M.execute(base,b,'insert',0);
 assert.deepEqual(spans(swapped,'voice'),[[1,0,4],[0,7,1]]);
 for(const piece of byTime(swapped,'voice')){
  const note=swapped.notes.find(n=>n.id===piece.noteId);
  assert.equal(note.startUs,piece.startUs);assert.equal(note.endUs,M.end(piece));
 }
 const detached=M.execute({...base,timeline:{...timeline,linked:false}},b,'insert',0);
 assert.deepEqual(byTime(detached,'video').map(x=>x.id),[b.id,a.id,c.id]);
 assert.deepEqual(detached.notes,base.notes);
 assert.deepEqual(byTime(detached,'audio'),byTime(base,'audio'));
 assert.deepEqual(byTime(detached,'voice'),[voice]);
 console.log('Packed clip insertion, midpoint gestures, both directions, linked voice/Regions and independent tracks passed');
}

for(const linked of [true,false]){
 const doc={durationUs:10*U,version:3,notes:[],timeline:{linked,sourceDurationUs:10*U,clips:[first,last,audio]}};
 const forward=M.execute(doc,first,'move',6*U),backward=M.execute(doc,last,'move',2*U);
 assert.equal(M.end(forward.timeline.clips.find(c=>c.id===first.id)),last.startUs);
 assert.equal(backward.timeline.clips.find(c=>c.id===last.id).startUs,M.end(first));
 assert.equal(backward.timeline.clips.find(c=>c.id===audio.id).startUs,linked?M.end(first):audio.startUs);
}
const early={id:id(),number:1,startUs:2*U,endUs:null,text:'keep me'};
const spoken={id:id(),number:2,startUs:U,endUs:5*U,text:'voice',voice:{asset:'b'.repeat(64),durationUs:4*U}};
const full={durationUs:10*U,version:3,lastNoteNumber:2,notes:[early,spoken]};
full.timeline=M.initial(full);
full.durationUs=11*U;
const targetVideo=full.timeline.clips[0],targetAudio=full.timeline.clips[1];
let cut=M.execute(full,targetAudio,'left',3*U);
cut=M.execute(cut,targetVideo,'left',6*U);
assert.deepEqual(cut.notes,[]);assert.equal(cut.timeline.clips.filter(c=>c.track==='voice').length,0);
cut=M.execute(cut,targetVideo,'move',7*U);
cut=M.execute(JSON.parse(JSON.stringify(cut)),targetVideo,'left',4*U);
assert.equal(cut.notes.length,1);assert.equal(cut.notes[0].startUs,4*U);
cut=M.execute(cut,targetAudio,'left',U);
assert.deepEqual(cut.notes.sort((a,b)=>a.number-b.number),[early,spoken].map(n=>({...n,startUs:n.startUs+U,endUs:n.endUs==null?null:n.endUs+U})));
assert.deepEqual(cut.timeline.clips.filter(c=>c.track==='voice'),[{...full.timeline.clips[2],startUs:2*U}]);
assert.equal(cut.timeline.trimmed.length,0);
let tail=M.execute(full,targetVideo,'right',2*U);
tail=M.execute(tail,targetVideo,'split',U);
const right=tail.timeline.clips.find(c=>c.track==='video'&&c.startUs===U);
tail=M.execute(tail,right,'right',10*U);
assert.equal(tail.notes.find(n=>n.id===early.id).startUs,2*U);
assert.equal(tail.notes.find(n=>n.id===spoken.id).endUs,5*U);
console.log('Directional overlap docking, repeated linked trims, move, split, partial restore and voice offsets passed');
let regionSplit=M.execute(full,targetVideo,'right',3*U);
regionSplit=M.execute(regionSplit,targetVideo,'split',2*U);
const ownerRight=regionSplit.timeline.clips.find(c=>c.track==='video'&&c.startUs===2*U);
regionSplit=M.execute(regionSplit,ownerRight,'right',10*U);
const restoredRight=regionSplit.notes.find(n=>n.voice&&n.startUs===2*U);
assert.equal(restoredRight.endUs,5*U);
assert.equal(regionSplit.notes.filter(n=>n.voice).length,2);
assert.equal(regionSplit.timeline.clips.find(c=>c.noteId===restoredRight.id).durationUs,3*U);

// Linked clipboard operations split at the insertion point and allocate fresh note identities.
{
 const base=structuredClone(full),clip=base.timeline.clips.find(c=>c.track==='video');
 const copy=M.copyClips(M.clipGroup(base,[clip],'delete'));
 const paste=M.pasteClips(base,copy,2*U);
 assert.equal(paste.doc.timeline.clips.filter(c=>c.track==='video').length,3);
 assert.ok(copy.notes.length>0 && paste.inserted.some(c=>c.track==='voice'));
 assert.equal(new Set(paste.doc.notes.map(n=>n.number)).size,paste.doc.notes.length);
 assert.ok(paste.inserted.every(c=>!base.timeline.clips.some(p=>p.id===c.id)));
 const again=M.pasteClips(paste.doc,copy,paste.doc.durationUs);
 assert.equal(new Set(again.doc.timeline.clips.map(c=>c.id)).size,again.doc.timeline.clips.length);
 const unlocked=structuredClone(base);unlocked.timeline.linked=false;
 const single=M.copyClips(M.clipGroup(unlocked,[clip],'delete'));
 const result=M.pasteClips(unlocked,single,unlocked.durationUs+U);
 assert.deepEqual(result.doc.notes,unlocked.notes);
 assert.deepEqual(result.inserted.map(c=>c.track),['video']);
 assert.equal(result.inserted[0].sourceUs,clip.sourceUs);
 assert.equal(result.inserted[0].startUs,unlocked.durationUs+U);
}
