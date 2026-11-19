'use strict';
const shapeNames={arrow:'箭头',box:'框选',line:'线',angle:'角度',ruler:'距离标尺',magnifier:'放大镜',text:'文字框'};
function sourceSize(){const im=$('openedImage');return imageDoc()?[im.naturalWidth,im.naturalHeight]:[v.videoWidth,v.videoHeight]}
function lensRect(s,w,h){return {x:Math.min(s.x,s.toX)*w,y:Math.min(s.y,s.toY)*h,width:Math.max(1,Math.abs(s.toX-s.x)*w),height:Math.max(1,Math.abs(s.toY-s.y)*h)}}
function drawMagnifiers(ctx,source,notes,w,h){
 const sw=source.videoWidth||source.naturalWidth||source.width,sh=source.videoHeight||source.naturalHeight||source.height;
 for(const n of notes){if(n.shape?.kind!=='magnifier')continue;const r=lensRect(n.shape,w,h);
  ctx.drawImage(source,(r.x+r.width/4)*sw/w,(r.y+r.height/4)*sh/h,r.width*sw/w/2,r.height*sh/h/2,r.x,r.y,r.width,r.height);
 }
}
function drawLiveMagnifiers(group,notes,w,h){
 for(const n of notes){
  if(n.shape?.kind!=='magnifier')continue;
  const r=lensRect(n.shape,w,h),foreign=svg('foreignObject',r),canvas=document.createElement('canvas');
  canvas.width=Math.max(1,Math.ceil(w));canvas.height=Math.max(1,Math.ceil(h));
  // Draw only source pixels; reference pictures and other annotations stay above lenses.
  try{drawMagnifiers(canvas.getContext('2d'),imageDoc()?$('openedImage'):v,[n],w,h)}catch{}
  canvas.style.cssText=`position:absolute;left:${-r.x}px;top:${-r.y}px;width:${w}px;height:${h}px`;
  foreign.append(canvas);group.append(foreign);
 }
}
function drawShape(group,n,w,h,pixels=sourceSize()){
 const s=n.shape,c=noteColor(n),x1=s.x*w,y1=s.y*h,x2=s.toX*w,y2=s.toY*h;
 const r=lensRect(s,w,h),base={fill:'none',stroke:c,'stroke-width':3,'stroke-linecap':'round','stroke-linejoin':'round'};
 const line=(a,b,d,e)=>{const el=svg('line',{x1:a,y1:b,x2:d,y2:e,...base});group.append(el);return el};
 function label(text,x,y){const el=svg('text',{x,y,fill:c,stroke:'#17191d','stroke-width':3,'paint-order':'stroke','font-family':'Microsoft YaHei UI, sans-serif','font-size':14,'font-weight':600});el.textContent=text;group.append(el)}
 let shape;
 if(['arrow','line','ruler','angle'].includes(s.kind)){
  shape=line(x1,y1,x2,y2);
  if(s.kind==='arrow'){const a=Math.atan2(y2-y1,x2-x1)*180/Math.PI;group.append(svg('path',{d:'M0 0 L-13 -6 L-13 6 Z',transform:`translate(${x2} ${y2}) rotate(${a})`,fill:c}))}
  if(s.kind==='ruler'){
   const a=Math.atan2(y2-y1,x2-x1),dx=Math.sin(a)*7,dy=-Math.cos(a)*7;
   line(x1-dx,y1-dy,x1+dx,y1+dy);line(x2-dx,y2-dy,x2+dx,y2+dy);
   const [sw,sh]=pixels;label(Math.hypot((s.toX-s.x)*sw,(s.toY-s.y)*sh).toFixed(1)+' px',(x1+x2)/2+4,(y1+y2)/2-8);
  }
  if(s.kind==='angle'&&s.thirdX!=null){
   const x3=s.thirdX*w,y3=s.thirdY*h;line(x2,y2,x3,y3);
   const a=Math.atan2(y1-y2,x1-x2),b=Math.atan2(y3-y2,x3-x2),delta=Math.atan2(Math.sin(b-a),Math.cos(b-a));
   const radius=Math.min(24,Math.hypot(x1-x2,y1-y2)/2,Math.hypot(x3-x2,y3-y2)/2);
   group.append(svg('path',{d:`M${x2+radius*Math.cos(a)} ${y2+radius*Math.sin(a)} A${radius} ${radius} 0 0 ${delta>=0?1:0} ${x2+radius*Math.cos(b)} ${y2+radius*Math.sin(b)}`,...base}));
   label((Math.abs(delta)*180/Math.PI).toFixed(1)+'°',x2+8,y2-10);
  }
 }else{
  shape=svg('rect',{...r,...base,fill:s.kind==='text'?'#17191ddd':'none'});group.append(shape);
  if(s.kind==='magnifier')label('2×',r.x+6,r.y+r.height-7);
  if(s.kind==='text'){
   const clipId='text-'+n.id,clip=svg('clipPath',{id:clipId});clip.append(svg('rect',r));group.append(clip);
   const text=svg('text',{x:r.x+7,y:r.y+20,fill:c,'font-family':'Microsoft YaHei UI, sans-serif','font-size':16,'clip-path':`url(#${clipId})`});
   const capacity=Math.max(1,Math.floor((r.width-14)/16));let row=0;
   for(const paragraph of (n.text||'在下方填写批注文字').split('\n')){
    const chars=Array.from(paragraph);if(!chars.length)row++;
    for(let i=0;i<chars.length;i+=capacity){const span=svg('tspan',{x:r.x+7,y:r.y+20+row++*22});span.textContent=chars.slice(i,i+capacity).join('');text.append(span)}
   }
   group.append(text);
  }
 }
 if(shape){const hit=shape.cloneNode(false);hit.setAttribute('class','inkHit');hit.setAttribute('fill','none');group.append(hit)}
}
