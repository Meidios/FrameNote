'use strict';
// Export from source pixels and document coordinates, independently of the live viewport.
const imageExport=(()=>{
 let output=null;
 function dataURL(blob){return new Promise((resolve,reject)=>{const r=new FileReader();r.onload=()=>resolve(r.result);r.onerror=()=>reject(r.error);r.readAsDataURL(blob)})}
 async function bitmap(url){
  const i=new Image();i.crossOrigin='anonymous';i.src=url;await i.decode();return i;
 }
 async function prepare(base,notes){
  if(!notes){if(!imageDoc()||editing)throw Error('请先保存当前图片批注。');notes=doc.notes}
  const source=await bitmap(base+'source');
  const width=source.naturalWidth,height=source.naturalHeight;
  const canvas=document.createElement('canvas');canvas.width=width;canvas.height=height;
  try{
   const ctx=canvas.getContext('2d');if(!ctx)throw Error('无法创建原尺寸画布。');
   ctx.drawImage(source,0,0,width,height);
   drawMagnifiers(ctx,source,notes,width,height);
   if(notes.length){
    const unit=Math.max(1,Math.max(width,height)/1600);
    const layer=svg('svg',{width,height,viewBox:'0 0 '+(width/unit)+' '+(height/unit)});
    drawInk(layer,notes,width/unit,height/unit,[width,height]);
    layer.querySelectorAll('foreignObject,.inkHit,.pictureHandle,rect[pointer-events="none"]').forEach(e=>e.remove());
    layer.querySelectorAll('.inkBadge text').forEach(e=>{e.setAttribute('fill','#15202a');e.setAttribute('font-family','Consolas, monospace');e.setAttribute('font-weight','700');e.setAttribute('font-size','13')});
    // Standalone SVG images cannot load external resources; embed only the referenced attachments.
    const assets=new Map();
    for(const e of layer.querySelectorAll('image')){
     const filename=new URL(e.getAttribute('href'),document.baseURI).pathname.split('/').pop();
     if(!assets.has(filename)){
      const response=await fetch(base+filename);
      if(!response.ok)throw Error('无法读取批注附图。');
      assets.set(filename,await dataURL(await response.blob()));
     }
     e.setAttribute('href',assets.get(filename));
    }
    const svgText=new XMLSerializer().serializeToString(layer);
    const ink=await bitmap(await dataURL(new Blob([svgText],{type:'image/svg+xml'})));
    ctx.drawImage(ink,0,0,width,height);
   }
   output=await new Promise((resolve,reject)=>canvas.toBlob(b=>b?resolve(b):reject(Error('无法按原图分辨率生成 PNG。')),'image/png'));
   return output.size;
  }finally{canvas.width=canvas.height=0}
 }
 async function chunk(offset,length){return (await dataURL(output.slice(offset,offset+length))).split(',')[1]}
 return {prepare,chunk,release(){output=null}};
})();
for(const [id,save] of [['copyImage',false],['saveImage',true]])
 $(id).onclick=()=>{if(imageDoc()&&!busy&&!editing)send('imageExport',{save})};
