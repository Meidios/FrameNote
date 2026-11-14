'use strict';
let activePicture=-1,imageDrag=null,imagePending=null;
function drawPictures(group,n,w,h){
 n.pictures.forEach((p,i)=>{
  const x=p.x*w,y=p.y*h,width=p.width*w,height=width*p.pixelHeight/p.pixelWidth;
  const el=svg('image',{x,y,width,height,href:imageBase+p.asset+'.png','data-picture':i,class:'picture'});
  group.append(el);
  if(editing && i===activePicture){
   group.append(svg('rect',{x,y,width,height,fill:'none',stroke:noteColor(n),'pointer-events':'none'}));
   for(const [dx,dy] of [[0,0],[1,0],[0,1],[1,1]]){
    group.append(svg('rect',{x:x+dx*width-5,y:y+dy*height-5,width:10,height:10,rx:2,
      fill:noteColor(n),stroke:'#192028',class:'pictureHandle','data-picture':i,'data-corner':dx+','+dy,
      cursor:dx===dy?'nwse-resize':'nesw-resize'}));
   }
  }
 });
}

async function addPicture(file){
 if(!(ready||imageDoc())||busy||imagePending)return;
 if(!['image/png','image/jpeg','image/webp','image/bmp'].includes(file.type))return status('请选择 PNG、JPEG、WebP 或 BMP 图片。',true);
 if(file.size>32*1024*1024)return status('请选择小于 32 MB 的图片。',true);
 if(!editing)newNote(imageDoc()?0:stamp(),null);
 if(!editing)return;
 if((editing.pictures?.length??0)>=16)return status('每条批注最多可添加 16 张图片。',true);
 const pending={request:crypto.randomUUID(),note:editing};imagePending=pending;
 try{
  const bitmap=await createImageBitmap(file);
  const canvas=document.createElement('canvas');
  try{
   const k=Math.min(1,2048/Math.max(bitmap.width,bitmap.height));
   canvas.width=Math.max(1,Math.round(bitmap.width*k));canvas.height=Math.max(1,Math.round(bitmap.height*k));
   canvas.getContext('2d').drawImage(bitmap,0,0,canvas.width,canvas.height);
  }finally{bitmap.close()}
  if(editing!==pending.note){imagePending=null;return}
  send('storePicture',{request:pending.request,data:canvas.toDataURL('image/png').split(',')[1]});
 }catch{imagePending=null;status('无法读取这张图片。',true)}
}

document.addEventListener('DOMContentLoaded',()=>{
 const input=$('pictureFile');
 $('addPicture').onclick=$('startPicture').onclick=()=>{if(!imagePending)input.click()};
 input.onchange=()=>{const file=input.files[0];input.value='';if(file)addPicture(file)};
 document.addEventListener('paste',e=>{
  const item=Array.from(e.clipboardData?.items??[]).find(i=>i.kind==='file'&&i.type.startsWith('image/'));
  const file=item?.getAsFile();if(!file||!(ready||(imageDoc()&&editing))||busy)return;
  e.preventDefault();addPicture(file);
 });
 chrome.webview.addEventListener('message',({data:m})=>{
  if(m.type!=='pictureStored'||imagePending?.request!==m.request)return;
  const target=imagePending.note;imagePending=null;if(editing!==target)return;
  if(m.error)return status(m.error,true);
  const img=$('openedImage');const ratio=(imageDoc()?img.naturalWidth/img.naturalHeight:v.videoWidth/v.videoHeight)*(m.pixelHeight/m.pixelWidth);
  const width=Math.min(.38,.48/ratio),height=width*ratio;
  (editing.pictures??=[]).push({asset:m.asset,pixelWidth:m.pixelWidth,pixelHeight:m.pixelHeight,
    x:(1-width)/2,y:(1-height)/2,width});
  activePicture=editing.pictures.length-1;setTool(null);render();
  status('拖动图片移动位置，拖动角点等比缩放。');
 });
 $('removePicture').onclick=()=>{if(!editing||activePicture<0)return;
  editing.pictures.splice(activePicture,1);activePicture=editing.pictures.length-1;render()};
 overlay.addEventListener('pointerdown',e=>{
  const el=e.target.closest('[data-picture]');if(!editing||tool||!el||e.button!==0)return;
  const i=Number(el.dataset.picture),p=editing.pictures[i],corner=el.dataset.corner?.split(',').map(Number);
  const bounds=overlay.getBoundingClientRect();
  activePicture=i;imageDrag={pointer:e.pointerId,p,start:{...p},at:pos(e),corner,
    ratio:bounds.width/bounds.height*p.pixelHeight/p.pixelWidth};
  overlay.setPointerCapture(e.pointerId);e.preventDefault();e.stopImmediatePropagation();paint();
 },true);
 overlay.addEventListener('pointermove',e=>{
  if(imageDrag?.pointer!==e.pointerId)return;
  const {p,start,at,corner,ratio}=imageDrag,[x,y]=pos(e);
  const clamp=(v,min,max)=>Math.max(min,Math.min(max,v));
  if(!corner){
   p.x=clamp(start.x+x-at[0],0,1-start.width);p.y=clamp(start.y+y-at[1],0,1-start.width*ratio);
  }else{
   const [right,bottom]=corner,fx=start.x+(right?0:start.width),fy=start.y+(bottom?0:start.width*ratio);
   const sx=right?1:-1,sy=bottom?1:-1;
   const bounds=overlay.getBoundingClientRect(),aspect=p.pixelHeight/p.pixelWidth;
   // Project the pointer displacement onto the original diagonal, anchoring the opposite corner.
   const delta=(sx*(x-at[0])+sy*(y-at[1])*bounds.height/bounds.width*aspect)/(1+aspect*aspect);
   const limit=Math.min(right?1-fx:fx,(bottom?1-fy:fy)/ratio);
   p.width=clamp(start.width+delta,Math.min(12/bounds.width,limit),limit);
   p.x=right?fx:fx-p.width;p.y=bottom?fy:fy-p.width*ratio;
   p.x=Math.max(0,p.x);p.y=Math.max(0,p.y);
  }
  e.stopImmediatePropagation();paint();
 },true);
 const finish=e=>{if(imageDrag?.pointer!==e.pointerId)return;imageDrag=null;e.stopImmediatePropagation()};
 for(const event of ['pointerup','pointercancel','lostpointercapture'])overlay.addEventListener(event,finish,true);
});
