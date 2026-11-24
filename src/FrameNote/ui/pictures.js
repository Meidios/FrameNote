'use strict';
let activePicture=-1,imagePending=null;
let pictureChoice=null;
function drawPictures(group,n,w,h){
 n.pictures.forEach((p,i)=>{
  const x=p.x*w,y=p.y*h,width=p.width*w,height=width*p.pixelHeight/p.pixelWidth;
  const el=svg('image',{x,y,width,height,href:imageBase+p.asset+'.png','data-picture':i,class:'picture'});
  group.append(el);
 });
}

function pickPicture(at=annotationTime(),targetId=null){
 if(!canAnnotate())return;
 finishEdit();transport.pause();
 pictureChoice={documentId:doc.id,at,targetId};
 $('pictureFile').click();
}
async function addPicture(file,choice){
 if(!canAnnotate()||choice&&choice.documentId!==doc.id)return;
 if(!['image/png','image/jpeg','image/webp','image/bmp'].includes(file.type))return status(tr('请选择 PNG、JPEG、WebP 或 BMP 图片。'),true);
 if(file.size>32*1024*1024)return status(tr('请选择小于 32 MB 的图片。'),true);
 const at=choice?.at??annotationTime(),targetId=choice?.targetId;
 finishEdit();transport.pause();
 if((doc.notes.find(n=>n.id===targetId)?.pictures?.length??0)>=16)return status(tr('每条批注最多可添加 16 张图片。'),true);
 const pending={request:crypto.randomUUID(),at,targetId};imagePending=pending;syncHistoryButtons();
 try{
  const bitmap=await createImageBitmap(file);
  const canvas=document.createElement('canvas');
  try{
   const k=Math.min(1,2048/Math.max(bitmap.width,bitmap.height));
   canvas.width=Math.max(1,Math.round(bitmap.width*k));canvas.height=Math.max(1,Math.round(bitmap.height*k));
   canvas.getContext('2d').drawImage(bitmap,0,0,canvas.width,canvas.height);
  }finally{bitmap.close()}
  if(imagePending!==pending)return;
  send('storePicture',{request:pending.request,data:canvas.toDataURL('image/png').split(',')[1]});
 }catch{if(imagePending===pending){imagePending=null;syncHistoryButtons();status(tr('无法读取这张图片。'),true)}}
}

document.addEventListener('DOMContentLoaded',()=>{
 const input=$('pictureFile');
 $('addPicture').onclick=()=>pickPicture();
 input.onchange=()=>{const file=input.files[0],choice=pictureChoice;input.value='';pictureChoice=null;if(file)addPicture(file,choice)};
 input.oncancel=()=>{pictureChoice=null};
 document.addEventListener('paste',e=>{
  const paste=window.shortcutBindings.paste;
  if(!paste.keys.some(b=>b.mods===2&&b.vk===86)&&e.target.closest?.('textarea,input,[contenteditable=true]'))return;
  const item=Array.from(e.clipboardData?.items??[]).find(i=>i.kind==='file'&&i.type.startsWith('image/'));
  const file=item?.getAsFile();if(!file||!canAnnotate())return;
  e.preventDefault();addPicture(file);
 });
 chrome.webview.addEventListener('message',({data:m})=>{
  if(m.type==='clipboardPicture'){
   if(m.documentId===doc?.id){
    const bytes=Uint8Array.from(atob(m.data),c=>c.charCodeAt(0));
    addPicture(new Blob([bytes],{type:'image/png'}),{documentId:m.documentId,at:m.at});
   }
   return;
  }
  if(m.type==='loaded'||m.type==='image'){imagePending=pictureChoice=null;return}
  if(m.type!=='pictureStored'||imagePending?.request!==m.request)return;
  const {at,targetId}=imagePending;imagePending=null;syncHistoryButtons();
  if(m.error)return status(m.error,true);
  finishEdit();
  const note=targetId?doc.notes.find(n=>n.id===targetId):blankNote(at,null);if(!note)return;
  begin(note,false);
  const img=$('openedImage');const ratio=(imageDoc()?img.naturalWidth/img.naturalHeight:v.videoWidth/v.videoHeight)*(m.pixelHeight/m.pixelWidth);
  const width=Math.min(.38,.48/ratio),height=width*ratio;
  (editing.pictures??=[]).push({asset:m.asset,pixelWidth:m.pixelWidth,pixelHeight:m.pixelHeight,
    x:(1-width)/2,y:(1-height)/2,width});
  activePicture=editing.pictures.length-1;setTool(null);persistEdit();render();
  status(tr('拖动图片移动位置，拖动角点等比缩放。'));
 });
 $('removePicture').onclick=()=>{if(!editing?.pictures?.length)return;
  editing.pictures.splice(activePicture<0?editing.pictures.length-1:activePicture,1);activePicture=editing.pictures.length-1;persistEdit();render()};
});

function dragPicture(p,start,corner,dx,dy,bounds){
 const ratio=bounds.width/bounds.height*p.pixelHeight/p.pixelWidth;
 if(!corner){
  p.x=clamp(start.x+dx,0,1-start.width);p.y=clamp(start.y+dy,0,1-start.width*ratio);
  return;
 }
 const [right,bottom]=corner,fx=start.x+(right?0:start.width),fy=start.y+(bottom?0:start.width*ratio);
 const sx=right?1:-1,sy=bottom?1:-1,aspect=p.pixelHeight/p.pixelWidth;
 // Project the pointer displacement onto the original diagonal, anchoring the opposite corner.
 const delta=(sx*dx+sy*dy*bounds.height/bounds.width*aspect)/(1+aspect*aspect);
 const limit=Math.min(right?1-fx:fx,(bottom?1-fy:fy)/ratio);
 p.width=clamp(start.width+delta,Math.min(12/bounds.width,limit),limit);
 p.x=Math.max(0,right?fx:fx-p.width);p.y=Math.max(0,bottom?fy:fy-p.width*ratio);
}
