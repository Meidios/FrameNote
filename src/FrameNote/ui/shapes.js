'use strict';
const shapeNames={arrow:tr('箭头'),box:tr('框选'),line:tr('线'),brush:tr('画笔'),angle:tr('角度'),ruler:tr('距离标尺'),magnifier:tr('放大镜'),text:tr('文字框')};
let originalImageSize=null;
function sourceSize(){const im=$('openedImage');return imageDoc()?(originalImageSize??[im.naturalWidth,im.naturalHeight]):[v.videoWidth,v.videoHeight]}
const metresPerUnit={mm:.001,cm:.01,m:1,px:.0254/96};
function rulerDistance(s,aspect){return Math.hypot(s.toX-s.x,(s.toY-s.y)*aspect)}
function rulerValue(s,scale){return Number((rulerDistance(s,scale.aspect)*scale.metresPerWidth/metresPerUnit[scale.unit]).toPrecision(6))}
function angleGeometry(s,w,h){
 const a=Math.atan2((s.y-s.toY)*h,(s.x-s.toX)*w),b=Math.atan2((s.thirdY-s.toY)*h,(s.thirdX-s.toX)*w);
 const delta=Math.atan2(Math.sin(b-a),Math.cos(b-a));
 return {a,b,delta,label:(Math.abs(delta)*180/Math.PI).toFixed(1)+'°'};
}
function shapeWithAngle(s,degrees,w,h){
 if(!(w>0&&h>0)||!Number.isFinite(degrees)||degrees<0||degrees>180)return null;
 const first=Math.hypot((s.x-s.toX)*w,(s.y-s.toY)*h);
 if(!first)return null;
 const length=Math.hypot((s.thirdX-s.toX)*w,(s.thirdY-s.toY)*h)||first;
 const {a,delta}=angleGeometry(s,w,h),rotation=(delta<0?-1:1)*degrees*Math.PI/180;
 // Rotate in source pixels. Shorten the second arm only if it reaches the image edge.
 for(const direction of [a+rotation,a-rotation]){
  const dx=Math.cos(direction)/w,dy=Math.sin(direction)/h;
  const limit=(origin,step)=>Math.abs(step)<1e-12?Infinity:(step>0?1-origin:-origin)/step;
  const radius=Math.min(length,limit(s.toX,dx),limit(s.toY,dy));
  if(radius>1e-7)return {...s,thirdX:clamp(s.toX+radius*dx,0,1),thirdY:clamp(s.toY+radius*dy,0,1)};
 }
 return null;
}
function rulerMetres(s,scale,w,h){
 return scale?rulerDistance(s,scale.aspect)*scale.metresPerWidth:Math.hypot((s.toX-s.x)*w,(s.toY-s.y)*h)*metresPerUnit.px;
}
function rulerLabel(s,scale,w,h,unit=defaultRulerUnit){
 if(scale)return rulerValue(s,scale)+' '+scale.unit;
 if(!w||!h)return '';
 const length=rulerMetres(s,null,w,h)/metresPerUnit[unit];
 const value=unit==='px'?Number(length.toFixed(1)):Number(length.toPrecision(6));
 return value+' '+unit;
}
function withMeasurementText(n,scales){
 if(n.text&&!n.autoMeasurementText)return n;
 const s=n.shape;
 if(s?.kind!=='angle'&&s?.kind!=='ruler'){
  if(!n.autoMeasurementText)return n;
  const clean={...n,text:''};delete clean.autoMeasurementText;return clean;
 }
 const [w,h]=sourceSize();if(!w||!h)return n;
 const value=s.kind==='angle'?angleGeometry(s,w,h).label:rulerLabel(s,scales?.[s.rulerFrame],w,h);
 const text=shapeNames[s.kind]+' '+value;
 return n.autoMeasurementText&&n.text===text?n:{...n,text,autoMeasurementText:true};
}
function shapeRect(s,w,h,minSize=1){return {x:Math.min(s.x,s.toX)*w,y:Math.min(s.y,s.toY)*h,width:Math.max(minSize,Math.abs(s.toX-s.x)*w),height:Math.max(minSize,Math.abs(s.toY-s.y)*h)}}
function curveMidpoint(s){return [s.thirdX??(s.x+s.toX)/2,s.thirdY??(s.y+s.toY)/2]}
function splinePath(s,w,h){
 const points=s.points.map(p=>[p.x*w,p.y*h]),k=(s.smoothness??1)/6;
 let path='M'+points[0].join(' '),tangent;
 for(let i=1;i<points.length;i++){
  const a=points[i-1],b=points[i],prev=points[Math.max(0,i-2)],next=points[Math.min(points.length-1,i+1)];
  const c1=[a[0]+(b[0]-prev[0])*k,a[1]+(b[1]-prev[1])*k];
  const c2=[b[0]-(next[0]-a[0])*k,b[1]-(next[1]-a[1])*k];
  path+='C'+c1.join(' ')+' '+c2.join(' ')+' '+b.join(' ');
  tangent=k===0?a:c2;
 }
 return {path,tangent};
}
function brushWidth(size,w,h,source){return size*Math.max(w,h)/Math.max(1,Math.min(1600,Math.max(...source)))}
function brushPath(points,w,h){
 return points.map((p,i)=>(i?'L':'M')+p.x*w+' '+p.y*h).join('')+(points.length===1?'l0 0':'');
}
function magnifierLayout(s,w,h){
 const rect=shapeRect(s,w,h,0),{x,y,width,height}=rect;
 const zoom=s.zoom??2;
 if(!s.preview){
  const sw=width/zoom,sh=height/zoom;
  return {source:{x:x+(width-sw)/2,y:y+(height-sh)/2,width:sw,height:sh},target:rect,scale:zoom};
 }
 const scale=Math.min(zoom,w/width,h/height),tw=Math.min(w,width*scale),th=Math.min(h,height*scale);
 const target={x:clamp(s.preview.x*w,0,w-tw),y:clamp(s.preview.y*h,0,h-th),width:tw,height:th};
 return {source:rect,target,scale};
}
function placeMagnifier(s,w,h){
 s.preview={x:0,y:0};
 const {source:r,target:t}=magnifierLayout(s,w,h),gap=12;
 const right=r.x+r.width,bottom=r.y+r.height;
 const candidates=[[r.x+r.width/2,bottom+gap],[right+gap,r.y+r.height/2],
  [r.x+r.width/2,r.y-t.height-gap],[r.x-t.width-gap,r.y+r.height/2]];
 let best=Infinity;
 for(const [cx,cy] of candidates){
  const x=clamp(cx,0,w-t.width),y=clamp(cy,0,h-t.height);
  const overlap=Math.max(0,Math.min(x+t.width,right+gap)-Math.max(x,r.x-gap))*
                Math.max(0,Math.min(y+t.height,bottom+gap)-Math.max(y,r.y-gap));
  if(overlap<best){best=overlap;s.preview={x:x/w,y:y/h}}
  if(!overlap)break;
 }
}
// The outermost four edges form the silhouette. Cut out both frames so their pixels keep their original color.
function lensConnector(a,b){
 const right=r=>r.x+r.width,bottom=r=>r.y+r.height;
 const t=a.y<=b.y?a:b,r=right(a)>=right(b)?a:b,d=bottom(a)>=bottom(b)?a:b,l=a.x<=b.x?a:b;
 const hole=(x,y,w,h)=>`M${x} ${y}h${w}v${h}h${-w}Z`;
 let path=`M${t.x} ${t.y}H${right(t)}L${right(r)} ${r.y}V${bottom(r)}L${right(d)} ${bottom(d)}H${d.x}L${l.x} ${bottom(l)}V${l.y}Z`+
  hole(a.x,a.y,a.width,a.height)+hole(b.x,b.y,b.width,b.height);
 const x=Math.max(a.x,b.x),y=Math.max(a.y,b.y),w=Math.min(right(a),right(b))-x,h=Math.min(bottom(a),bottom(b))-y;
 if(w>0&&h>0)path+=hole(x,y,w,h);
 return path;
}
function drawMagnifiers(ctx,source,notes,w,h){
 const sw=source.videoWidth||source.naturalWidth||source.width,sh=source.videoHeight||source.naturalHeight||source.height;
 for(const n of notes){if(n.shape?.kind!=='magnifier')continue;
  const {source:r,target:t}=magnifierLayout(n.shape,w,h);
  ctx.save();ctx.globalAlpha=noteOpacity(n)/100;
  if(r.width&&r.height)ctx.drawImage(source,r.x*sw/w,r.y*sh/h,r.width*sw/w,r.height*sh/h,t.x,t.y,t.width,t.height);
  ctx.restore();
 }
}
let liveMagnifiers=[];
function refreshLiveMagnifiers(){
 for(const {ctx,rect,w,h} of liveMagnifiers){
  const source=imageDoc()?$('openedImage'):v;
  const sw=source.videoWidth||source.naturalWidth,sh=source.videoHeight||source.naturalHeight;
  const {x,y,width,height}=rect,canvas=ctx.canvas;
  ctx.clearRect(0,0,canvas.width,canvas.height);
  if(!imageDoc()&&v.style.visibility==='hidden'){ctx.fillRect(0,0,canvas.width,canvas.height);continue}
  if(sw&&sh)ctx.drawImage(source,x*sw/w,y*sh/h,width*sw/w,height*sh/h,0,0,canvas.width,canvas.height);
 }
}
function drawLiveMagnifiers(group,notes,w,h){
 for(const n of notes){
  if(n.shape?.kind!=='magnifier')continue;
  const {source,target}=magnifierLayout(n.shape,w,h);
  if(!target.width||!target.height)continue;
  const foreign=svg('foreignObject',{...target,opacity:noteOpacity(n)/100,'pointer-events':'none'}),canvas=document.createElement('canvas');
  canvas.width=Math.ceil(target.width);canvas.height=Math.ceil(target.height);
  canvas.style.cssText='width:100%;height:100%;display:block';
  liveMagnifiers.push({ctx:canvas.getContext('2d'),rect:source,w,h});
  foreign.append(canvas);group.append(foreign);
 }
 refreshLiveMagnifiers();
}
function drawShape(group,n,w,h,measurement){
 const s=n.shape,c=noteColor(n),x1=s.x*w,y1=s.y*h,x2=s.toX*w,y2=s.toY*h;
 const r=shapeRect(s,w,h),base={fill:'none',stroke:c,'stroke-width':3,'stroke-linecap':'round','stroke-linejoin':'round'};
 const line=(a,b,d,e)=>{const el=svg('line',{x1:a,y1:b,x2:d,y2:e,...base});group.append(el);return el};
 function addHitArea(shape){const hit=shape.cloneNode(false);hit.setAttribute('class','inkHit');hit.setAttribute('fill','none');group.append(hit)}
 function label(text,x,y){const el=svg('text',{x,y,fill:c,stroke:'#17191d','stroke-width':3,'paint-order':'stroke','font-size':14,'font-weight':600});if(s.kind==='ruler'||s.kind==='angle')el.classList.add('measurementLabel');el.textContent=text;group.append(el)}
 let shape;
 if(['arrow','line','ruler','angle'].includes(s.kind)){
  let cx=x1,cy=y1;
  if(s.points){
   const curve=splinePath(s,w,h);[cx,cy]=curve.tangent;
   shape=svg('path',{...base,d:curve.path});group.append(shape);
  }else if((s.kind==='arrow'||s.kind==='line')&&s.thirdX!=null){
   cx=2*s.thirdX*w-(x1+x2)/2;cy=2*s.thirdY*h-(y1+y2)/2;
   shape=svg('path',{...base,d:`M${x1} ${y1}Q${cx} ${cy} ${x2} ${y2}`});group.append(shape);
   if(cx===x2&&cy===y2){cx=x1;cy=y1}
  }else shape=line(x1,y1,x2,y2);
  if(s.kind==='arrow'){const a=Math.atan2(y2-cy,x2-cx)*180/Math.PI;group.append(svg('path',{d:'M0 0 L-13 -6 L-13 6 Z',transform:`translate(${x2} ${y2}) rotate(${a})`,fill:c}))}
  if(s.kind==='ruler'){
   const a=Math.atan2(y2-y1,x2-x1),dx=Math.sin(a)*7,dy=-Math.cos(a)*7;
   line(x1-dx,y1-dy,x1+dx,y1+dy);line(x2-dx,y2-dy,x2+dx,y2+dy);
   const scales=measurement?measurement.scales:doc?.rulerScales;
   label(rulerLabel(s,scales?.[s.rulerFrame],...(measurement?.size??sourceSize()),measurement?.unit),(x1+x2)/2+4,(y1+y2)/2-8);
  }
  if(s.kind==='angle'&&s.thirdX!=null){
   const x3=s.thirdX*w,y3=s.thirdY*h;addHitArea(line(x2,y2,x3,y3));
   const {a,b,delta,label:degrees}=angleGeometry(s,...(measurement?.size??sourceSize()));
   const radius=Math.min(24,Math.hypot(x1-x2,y1-y2)/2,Math.hypot(x3-x2,y3-y2)/2);
   group.append(svg('path',{d:`M${x2+radius*Math.cos(a)} ${y2+radius*Math.sin(a)} A${radius} ${radius} 0 0 ${delta>=0?1:0} ${x2+radius*Math.cos(b)} ${y2+radius*Math.sin(b)}`,...base}));
   label(degrees,x2+8,y2-10);
  }
 }else if(s.kind==='brush'){
  shape=svg('path',{...base,'stroke-width':brushWidth(s.brushSize,w,h,measurement?.size??sourceSize()),d:brushPath(s.points,w,h)});
  group.append(shape);
 }else if(s.kind==='magnifier'){
  const {source,target,scale}=magnifierLayout(s,w,h);
  if(s.preview){
   group.append(svg('path',{class:'lensConnector',d:lensConnector(source,target),fill:c,'fill-rule':'evenodd','fill-opacity':.16}));
   const frame=svg('rect',{...source,...base,class:'inkShape'});group.append(frame);addHitArea(frame);
  }
  shape=svg('rect',{...target,...base,'data-lens-preview':''});group.append(shape);
  label(Number(scale.toFixed(2))+'×',target.x+6,target.y+target.height-7);
 }else{
  shape=svg('rect',{...r,...base,fill:s.kind==='text'?'#17191ddd':'none'});group.append(shape);
  if(s.kind==='text'){
   const clipId='text-'+n.id,clip=svg('clipPath',{id:clipId});clip.append(svg('rect',r));group.append(clip);
   const text=svg('text',{x:r.x+7,y:r.y+20,fill:c,'font-size':16,'clip-path':`url(#${clipId})`});
   const capacity=Math.max(1,Math.floor((r.width-14)/16));let row=0;
   for(const paragraph of (n.text||tr('在下方填写批注文字')).split('\n')){
    const chars=Array.from(paragraph);if(!chars.length)row++;
    for(let i=0;i<chars.length;i+=capacity){const span=svg('tspan',{x:r.x+7,y:r.y+20+row++*22});span.textContent=chars.slice(i,i+capacity).join('');text.append(span)}
   }
   group.append(text);
  }
 }
 if(shape){shape.classList.add('inkShape');addHitArea(shape)}
}
