const assert=require('node:assert/strict');global.crypto=require('node:crypto').webcrypto;
const M=require('../src/FrameNote/ui/edit-model.js');
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
assert.throws(()=>M.execute(d,d.timeline.clips.find(c=>c.track==='video'&&c.startUs===8*U),'move',3*U),/重叠/);
console.log('linked split/move/trim, detached delete, gap preservation, edge snapping, overlap rejection passed');
const mark={id:id(),number:3,startUs:U,endUs:null,text:'mark'};let z={...d,notes:[mark],lastNoteNumber:3};z.timeline.linked=true;let moved=M.execute(z,{id:mark.id,track:'note'},'move',3*U);assert.deepEqual(moved.timeline.clips,z.timeline.clips);assert.equal(moved.notes[0].startUs,3*U);
z.timeline.linked=false;z.timeline.clips.push({id:id(),track:'voice',startUs:2*U,sourceUs:0,durationUs:2*U,sourceDurationUs:3*U,asset:'a'.repeat(64)});const vc=z.timeline.clips.at(-1);moved=M.execute(z,vc,'move',6*U);assert.equal(moved.notes[0].startUs,U);assert.equal(moved.timeline.clips.at(-1).startUs,6*U);
const restored=M.execute(moved,moved.timeline.clips.at(-1),'right',9*U);assert.equal(restored.timeline.clips.at(-1).durationUs,3*U);
console.log('zero-duration Mark handling, detached voice movement and source-bound trim restoration passed');

const region={id:id(),startUs:2*U,endUs:4*U,voice:{asset:'b'.repeat(64),durationUs:2*U}};
const other={id:id(),startUs:5*U,endUs:7*U,text:'overlapping feedback'};
const review={version:1,durationUs:10*U,notes:[region,other]},target={track:'note',id:region.id};
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
