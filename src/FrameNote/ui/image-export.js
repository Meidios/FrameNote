'use strict';
// Export from source pixels and document coordinates, independently of the live viewport.
const imageExport=(()=>{
 let output=null;
 function dataURL(blob){return new Promise((resolve,reject)=>{const r=new FileReader();r.onload=()=>resolve(r.result);r.onerror=()=>reject(r.error);r.readAsDataURL(blob)})}
 async function bitmap(url){
  const i=new Image();i.crossOrigin='anonymous';i.src=url;await i.decode();return i;
 }
 // An SVG decoded as an image cannot use the document's fonts. Paint the text
 // at output resolution, keeping its place in the SVG's clip and stacking order.
 async function paintText(layer,scale){
  const family=getComputedStyle(document.body).fontFamily;
  const tile=document.createElement('canvas'),ctx=tile.getContext('2d');
  for(const text of layer.querySelectorAll('text')){
   const font=(text.getAttribute('font-weight')||'400')+' '+text.getAttribute('font-size')+'px '+family;
   await document.fonts.load(font,text.textContent);
   const group=svg('g',{}),clip=text.getAttribute('clip-path');
   if(clip)group.setAttribute('clip-path',clip);
   const align=text.getAttribute('text-anchor')==='middle'?'center':'left';
   const stroke=text.getAttribute('stroke'),lineWidth=Number(text.getAttribute('stroke-width'))||0;
   for(const row of text.children.length?text.children:[text]){
    const value=row.textContent;
    if(!value.trim())continue;
    ctx.font=font;ctx.textAlign=align;
    const m=ctx.measureText(value),pad=lineWidth/2+1;
    const left=Math.floor((-m.actualBoundingBoxLeft-pad)*scale),top=Math.floor((-m.actualBoundingBoxAscent-pad)*scale);
    tile.width=Math.ceil((m.actualBoundingBoxRight+pad)*scale)-left;
    tile.height=Math.ceil((m.actualBoundingBoxDescent+pad)*scale)-top;
    ctx.setTransform(scale,0,0,scale,-left,-top);ctx.font=font;ctx.textAlign=align;
    if(stroke){ctx.strokeStyle=stroke;ctx.lineWidth=lineWidth;ctx.strokeText(value,0,0)}
    ctx.fillStyle=text.getAttribute('fill');ctx.fillText(value,0,0);
    group.append(svg('image',{x:Number(row.getAttribute('x'))+left/scale,y:Number(row.getAttribute('y'))+top/scale,
     width:tile.width/scale,height:tile.height/scale,href:tile.toDataURL()}));
   }
   text.replaceWith(group);
  }
 }
 async function prepare(base,notes,scales=doc?.rulerScales,unit=defaultRulerUnit){
  if(!notes){if(!imageDoc()||editing)throw Error(tr('请先保存当前图片批注。'));notes=doc.notes}
  const source=await bitmap(base+'source');
  const width=source.naturalWidth,height=source.naturalHeight;
  const canvas=document.createElement('canvas');canvas.width=width;canvas.height=height;
  try{
   const ctx=canvas.getContext('2d');if(!ctx)throw Error(tr('无法创建原尺寸画布。'));
   ctx.drawImage(source,0,0,width,height);
   drawMagnifiers(ctx,source,notes,width,height);
   if(notes.length){
    const strokeScale=Math.max(1,Math.max(width,height)/1600);
    const layer=svg('svg',{width,height,viewBox:'0 0 '+(width/strokeScale)+' '+(height/strokeScale)});
    drawInk(layer,notes,width/strokeScale,height/strokeScale,{size:[width,height],scales,unit});
    layer.querySelectorAll('.inkHit').forEach(e=>e.remove());
    // Standalone SVG images cannot load external resources; embed only the referenced attachments.
    const assets=new Map();
    for(const e of layer.querySelectorAll('image')){
     const filename=new URL(e.getAttribute('href'),document.baseURI).pathname.split('/').pop();
     if(!assets.has(filename)){
      const response=await fetch(base+filename);
      if(!response.ok)throw Error(tr('无法读取批注附图。'));
      assets.set(filename,await dataURL(await response.blob()));
     }
     e.setAttribute('href',assets.get(filename));
    }
    await paintText(layer,strokeScale);
    const svgText=new XMLSerializer().serializeToString(layer);
    const ink=await bitmap(await dataURL(new Blob([svgText],{type:'image/svg+xml'})));
    ctx.drawImage(ink,0,0,width,height);
   }
   output=await new Promise((resolve,reject)=>canvas.toBlob(b=>b?resolve(b):reject(Error(tr('无法按原图分辨率生成 PNG。'))),'image/png'));
   return output.size;
  }finally{canvas.width=canvas.height=0}
 }
 async function chunk(offset,length){return (await dataURL(output.slice(offset,offset+length))).split(',')[1]}
 return {prepare,chunk,release(){output=null}};
})();
for(const [id,save] of [['copyImage',false],['saveImage',true]])
 $(id).onclick=()=>{if(imageDoc()&&!busy&&!editing)send('imageExport',{save})};
