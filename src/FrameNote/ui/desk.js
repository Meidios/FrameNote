'use strict';
const $=id=>document.getElementById(id), v=$('video'), overlay=$('overlay');
const send=(type,data={})=>chrome.webview.postMessage({type,...data});
window.addEventListener('dragover',e=>{e.preventDefault();e.dataTransfer.dropEffect='copy'});
window.addEventListener('drop',e=>{e.preventDefault();if(!busy&&!editing && e.dataTransfer.files.length===1)chrome.webview.postMessageWithAdditionalObjects({type:'drop'},[e.dataTransfer.files[0]])});
let imageBase="",temporary=false,doc=null,frames=[],shown=0,selected=null,editing=null,inUs=null,loopId=null,history=[],tool=null,anchor=null,busy=false,ready=false;
const imageDoc=()=>doc?.version===2;
const fmt=us=>{const ms=Math.floor((us+500)/1000);return String(Math.floor(ms/60000)).padStart(2,'0')+':'+String(Math.floor(ms/1000)%60).padStart(2,'0')+'.'+String(ms%1000).padStart(3,'0')};
function status(t,error=false){$('status').textContent=t;$('status').title=t;$('status').classList.toggle('error',error)}
function index(us){let lo=0,hi=frames.length;while(lo<hi){const mid=(lo+hi)>>>1;if(frames[mid]<=us+2)lo=mid+1;else hi=mid}return Math.max(0,lo-1)}
function seekUs(us,pause=true){if(!ready)return;if(pause)v.pause();const i=index(us);v.currentTime=(frames[i]+((frames[i+1]??doc.durationUs)-frames[i])*.5)/1e6;}
function stamp(){return frames[index(shown)]}
function tick(_,m){shown=Math.round(m.mediaTime*1e6);$('clock').textContent=fmt(shown);$('seek').value=shown;$('timeline').style.setProperty('--position',(100*shown/(doc?.durationUs||1))+'%');paint();const n=doc?.notes.find(n=>n.id===loopId);if(n && !v.paused && shown>=n.endUs)v.currentTime=n.startUs/1e6;v.requestVideoFrameCallback(tick)}
v.requestVideoFrameCallback(tick);
let playbackId=null,entryUs=0,reviewProxy=false,autoPlay=false;
v.onloadeddata=()=>{if(!doc || imageDoc())return;ready=true;if(entryUs>0){seekUs(entryUs);entryUs=0;}if(autoPlay){autoPlay=false;v.play().catch(e=>{if(e.name!=='AbortError')status('请点击播放：'+e.message,true)})}layout();render();status(temporary?"临时录屏：切换视频或退出后会清理。要保留视频和批注，请导出审阅包。":doc.fileName+(reviewProxy?' · 临时审阅预览':''))};
v.onerror=()=>{if(!doc || imageDoc())return;ready=false;render();if(playbackId && !reviewProxy && v.getAttribute('src'))send('decodeError',{playbackId,us:stamp()??0});else status('无法解码审阅预览。可使用 VLC 播放原片。',true)};
function setIconAction(id,icon,label,hint){
 const button=$(id);
 if(button.title===hint)return;
 button.querySelector('use').setAttribute('href','#icon-'+icon);
 button.querySelector('.actionName').textContent=label;
 button.title=hint;
}
v.onplay=v.onpause=()=>{const label=v.paused?'播放':'暂停';setIconAction('play',v.paused?'play':'pause',label,label+'（空格）')};
v.onended=()=>{const n=doc?.notes.find(n=>n.id===loopId);if(n){v.currentTime=n.startUs/1e6;v.play().catch(e=>status(e.message,true))}};
function layout(){if(imageDoc())return;const w=v.clientWidth,h=v.clientHeight,k=Math.min(w/(v.videoWidth||1),h/(v.videoHeight||1)),vw=v.videoWidth*k,vh=v.videoHeight*k;
 Object.assign(overlay.style,{width:vw+'px',height:vh+'px',left:(w-vw)/2+'px',top:(h-vh)/2+'px'});paint()}
new ResizeObserver(layout).observe($('stage'));
const palette=[['琥珀','#ffd886'],['天蓝','#8ddcff'],['薄荷','#8de3af'],['珊瑚','#ff9d97'],['紫藤','#cfb1ff'],['粉红','#ffaed8']];
const noteColor=n=>n.color??palette[(n.number-1)%palette.length][1];
for(const [name,color] of palette){
 const b=document.createElement('button');b.type='button';b.title=name;b.setAttribute('aria-label',name);b.dataset.color=color;
 b.style.setProperty('--note',color);b.onclick=()=>{if(editing){editing.color=color;updateColors();paint()}};$('colors').append(b);
}
function updateColors(){for(const b of $('colors').children)b.setAttribute('aria-pressed',String(b.dataset.color===noteColor(editing)))}
let hovered=null,inkKey='',rows=new Map();
function highlight(id){
 if(hovered===id)return;
 rows.get(hovered)?.classList.remove('hovered');hovered=id;
 const row=rows.get(id);if(row){row.classList.add('hovered');row.scrollIntoView({block:'nearest'})}
}
function svg(tag,attrs){
 const el=document.createElementNS('http://www.w3.org/2000/svg',tag);
 for(const [key,val] of Object.entries(attrs))el.setAttribute(key,val);
 return el;
}
function paint(){
 const g=$('ink'),w=overlay.clientWidth,h=overlay.clientHeight;
 const visible=editing?[editing]:imageDoc()?doc.notes:doc?.notes.filter(n=>n.endUs!==null?shown>=n.startUs-2&&shown<n.endUs:index(shown)===index(n.startUs))??[];
 const key=JSON.stringify([w,h,!!editing,activePicture,visible.map(n=>[n.id,n.number,n.color,n.shape,n.pictures])]);
 if(key===inkKey)return;inkKey=key;highlight(null);g.replaceChildren();if(!w||!h)return;
 drawInk(g,visible,w,h);
}
function drawInk(g,visible,w,h){
 const badges=[],labels=svg('g',{class:'inkLabels'});
 for(const n of visible){
  const s=n.shape,c=noteColor(n),group=svg('g',{'data-note':n.id,color:c});
  let x=4,y=4;
  if(n.pictures?.length){const p=n.pictures[0];x=p.x*w;y=p.y*h-22;drawPictures(group,n,w,h)}
  if(s){
   const x1=s.x*w,y1=s.y*h,x2=s.toX*w,y2=s.toY*h,arrow=s.kind==='arrow';
   const attrs=arrow?{x1,y1,x2,y2}:{x:Math.min(x1,x2),y:Math.min(y1,y2),width:Math.abs(x2-x1),height:Math.abs(y2-y1)};
   const shape=svg(arrow?'line':'rect',{...attrs,fill:'none',stroke:c,'stroke-width':3});
   group.append(shape);
   if(arrow){
    const angle=Math.atan2(y2-y1,x2-x1)*180/Math.PI;
    group.append(svg('path',{d:'M0 0 L-13 -6 L-13 6 Z',transform:'translate('+x2+' '+y2+') rotate('+angle+')',fill:c}));
   }
   const hit=shape.cloneNode(false);hit.setAttribute('class','inkHit');group.append(hit);
   x=(arrow?x1:attrs.x)+3;y=(arrow?y1:attrs.y)-22;
  }
  g.append(group);
  const bw=12+8*String('#'+n.number).length,bh=19;
  x=Math.max(1,Math.min(x,w-bw-1));y=Math.max(1,Math.min(y,h-bh-1));
  // Keep tags attached to their shapes when possible; move coincident tags to the nearest free slot.
  const clear=(px,py)=>badges.every(b=>px+bw+2<=b.x||b.x+b.w+2<=px||py+bh+2<=b.y||b.y+bh+2<=py);
  if(!clear(x,y)){
   let found=false;
   for(let dx=0;dx<w&&!found;dx+=bw+3)for(const px of new Set([x+dx,x-dx])){
    if(px<0||px+bw>w)continue;
    for(let dy=0;dy<h&&!found;dy+=bh+3)for(const py of [y+dy,y-dy]){
     if(py>=0&&py+bh<=h&&clear(px,py)){x=px;y=py;found=true;break}
    }
    if(found)break;
   }
  }
  badges.push({x,y,w:bw,h:bh});const tag=svg('g',{'data-note':n.id,class:'inkBadge',transform:'translate('+x+' '+y+')'});
  tag.append(svg('rect',{width:bw,height:bh,rx:3,fill:c,stroke:'#172029','stroke-width':1}));
  const text=svg('text',{x:bw/2,y:13.5,'text-anchor':'middle'});text.textContent='#'+n.number;tag.append(text);labels.append(tag);
 }
 g.append(labels);
}
overlay.addEventListener('pointerover',e=>{if(!editing)highlight(e.target.closest('[data-note]')?.dataset.note??null)});
overlay.addEventListener('pointerout',e=>{if(!editing)highlight(e.relatedTarget?.closest?.('#ink [data-note]')?.dataset.note??null)});
overlay.addEventListener('click',e=>{
 const id=e.target.closest('[data-note]')?.dataset.note;
 if(id && (ready||imageDoc()) && !editing && !busy)select(id,{seek:false});
});
function pos(e){const r=overlay.getBoundingClientRect(),clamp=x=>Math.min(1,Math.max(0,x));return [clamp((e.clientX-r.x)/r.width),clamp((e.clientY-r.y)/r.height)]}
overlay.onpointerdown=e=>{if(!tool||!editing)return;anchor=pos(e);overlay.setPointerCapture(e.pointerId)};
overlay.onpointermove=e=>{if(!anchor||!editing)return;const [toX,toY]=pos(e);editing.shape={kind:tool,x:anchor[0],y:anchor[1],toX,toY};paint()};
overlay.onpointerup=e=>{if(anchor)overlay.onpointermove(e);anchor=null;setTool(null)};overlay.onpointercancel=()=>{anchor=null;setTool(null)};
function setTool(t){tool=t;overlay.classList.toggle('drawing',!!t);for(const k of ['arrow','box'])$(k).classList.toggle('active',k===t);if(t)status('在画面上按住并拖动以绘制标记')}
function closeEditor(){activePicture=-1;imageDrag=null;editing=null;anchor=null;setTool(null);$('editor').hidden=true;paint()}
function begin(note){if(!(ready||imageDoc())||editing)return;v.pause();loopId=null;editing=structuredClone(note);activePicture=(editing.pictures?.length??0)-1;updateColors();$('editor').hidden=false;$('editTitle').textContent='#'+note.number+'　'+(imageDoc()?'图片批注':(note.endUs===null?'点批注　':'区间批注　')+fmt(note.startUs)+(note.endUs===null?'':' – '+fmt(note.endUs)));$('body').value=note.text;render();rows.get(note.id)?.scrollIntoView({block:'nearest'});$('body').focus({preventScroll:true})}
function newNote(a,b){if(doc.lastNoteNumber>=2147483647)return status('本审阅的批注编号已用尽。',true);begin({id:crypto.randomUUID(),number:doc.lastNoteNumber+1,color:palette[doc.lastNoteNumber%palette.length][1],pictures:null,startUs:a,endUs:b,text:'',shape:null})}
function select(id,{seek=true}={}){
 if(editing)return;const n=doc.notes.find(x=>x.id===id);if(!n)return;
 selected=id;loopId=null;if(seek)seekUs(n.startUs);render();rows.get(id)?.scrollIntoView({block:'nearest'});
}
function commit(notes){history.push(doc.notes);if(history.length>80)history.shift();doc.notes=notes;doc.lastNoteNumber=notes.reduce((max,n)=>Math.max(max,n.number),doc.lastNoteNumber);send('notes',{notes});render()}
$('gif').onclick=()=>send('gif',{id:selected});
// The slider thumb and the note lanes share the same usable time axis.
function layoutMarks(){
 const lanes=$('markLanes'),entries=[...lanes.children],ends=[];
 const sizes=entries.map(el=>el.firstElementChild.offsetWidth);
 const pad=Math.max(9,Math.ceil(sizes.reduce((m,n)=>Math.max(m,n),0)/2)+2);
 $('timeline').style.setProperty('--pad',pad+'px');
 const width=lanes.getBoundingClientRect().width;
 entries.forEach((el,i)=>{
  const start=Number(el.dataset.start)*width,end=Number(el.dataset.end)*width,span=end-start;
  const region=el.classList.contains('region'),body=region?span:sizes[i],center=region?(start+end)/2:start;
  const left=center-body/2,occupied=Math.max(body,sizes[i]);
  let row=ends.findIndex(x=>x+3<=center-occupied/2);if(row<0)row=ends.length;
  ends[row]=center+occupied/2;el.style.bottom=(row*25+3)+'px';el.style.left=left+'px';el.style.width=body+'px';
  el.classList.toggle('short',region&&span<sizes[i]);
 });
 lanes.style.height=Math.max(4,ends.length*25)+'px';
 $('timeline').style.setProperty('--gutter',($('marks').offsetWidth-$('marks').clientWidth)+'px');
}
let marksWidth=-1;
new ResizeObserver(([entry])=>{if(entry.contentRect.width!==marksWidth){marksWidth=entry.contentRect.width;layoutMarks()}}).observe($('markLanes'));
function noteBody(text){
 const body=document.createElement('span');body.className='noteBody';
 // Full-width sentence punctuation separates URLs in Chinese feedback.
 const searchable=text.replace(/[。，、；：！？（）【】《》“”‘’]/g,' ');
 let end=0;
 for(const link of linkify.find(searchable,'url',{defaultProtocol:'https'})){
  if(!/^https?:\/\//i.test(link.href))continue;
  body.append(text.slice(end,link.start));const a=document.createElement('a');
  a.href=link.href;a.textContent=text.slice(link.start,link.end);
  a.onclick=e=>{e.preventDefault();e.stopPropagation();if(!busy && e.detail<=1)send('openLink',{url:a.href})};
  a.ondblclick=e=>e.stopPropagation();body.append(a);end=link.end;
 }
 body.append(text.slice(end));return body;
}
let reportedCanOpen;
function render(){$('removePicture').hidden=!editing||activePicture<0;const notes=doc?.notes??[];rows.clear();$('count').textContent=notes.length;$('list').replaceChildren();$('markLanes').replaceChildren();
 for(const n of [...notes].sort((a,b)=>a.startUs-b.startUs||a.number-b.number)){
 const b=document.createElement('div'),t=document.createElement('button');rows.set(n.id,b);b.style.setProperty('--note',noteColor(n));b.className='note'+(n.id===selected?' selected':'');b.classList.toggle('hovered',n.id===hovered);t.className='stamp';t.textContent=imageDoc()?'':fmt(n.startUs)+(n.endUs===null?'　点':' – '+fmt(n.endUs));const badge=document.createElement('strong');badge.className='noteNumber';badge.textContent='#'+n.number;t.prepend(badge,' ');b.append(t,noteBody(n.text||(n.shape?.kind==='arrow'?'箭头':n.shape?.kind==='box'?'框选':'图片批注')));b.onclick=()=>select(n.id);b.ondblclick=()=>begin(n);$('list').append(b);
 if(imageDoc())continue;
 const m=document.createElement('button'),label=document.createElement('span');m.className=n.endUs===null?'point':'region';
 m.style.setProperty('--mark',noteColor(n));m.dataset.start=n.startUs/doc.durationUs;m.dataset.end=(n.endUs??n.startUs)/doc.durationUs;
 label.textContent='#'+n.number;m.append(label);m.title=t.textContent+' '+n.text;m.setAttribute('aria-label',m.title);
 m.setAttribute('aria-pressed',String(n.id===selected));m.disabled=!ready||!!editing;
 m.onclick=()=>{select(n.id);
  $('markLanes').querySelector('[aria-pressed="true"]')?.focus({preventScroll:true})};$('markLanes').append(m)}
 layoutMarks();
 if(!notes.length){const p=document.createElement('div');p.className='empty';p.textContent=imageDoc()?'按 M 添加图片批注，可写文字、画箭头或框选。':'还没有批注。按 M 记录当前帧。';$('list').append(p)}
 $('rangeHint').textContent=inUs===null?'':'起点 '+fmt(inUs)+'　定位后按 O';
 for(const id of ['rate','fullPreview','mutePlayback','playbackVolume','play','prev','next','seek','point','startPicture','inBtn','zoom'])$(id).disabled=!ready||!!editing;
 $('copyImage').disabled=$('saveImage').disabled=!imageDoc()||!!editing;
 $('copyFrame').disabled=$('saveFrame').disabled=!ready;
 $('point').disabled=$('save').disabled=!(ready||imageDoc())||!!editing;$('open').disabled=!!editing;$('outBtn').disabled=!ready||inUs===null||!!editing;
 if(reportedCanOpen!==!$('open').disabled){reportedCanOpen=!$('open').disabled;send('canOpen',{value:reportedCanOpen})}
 $('vlc').disabled=!doc||!!editing;
 const n=notes.find(x=>x.id===selected);for(const id of ['edit','del'])$(id).disabled=!n||!!editing;
 $('undo').disabled=!history.length||!!editing;$('loop').disabled=!n||n.endUs===null||!!editing;$('gif').disabled=$('loop').disabled;$('sendReaper').disabled=!!editing;window.captureButtons?.();$('loop').classList.toggle('active',!!loopId);$('loop').setAttribute('aria-pressed',String(!!loopId));paint()}
$('open').onclick=$('start').onclick=()=>send('open');$('save').onclick=()=>send('save',{notes:doc.notes});$('sendReaper').onclick=()=>send('sendReaper');
async function snapshot(save){
 if(busy||!ready||v.seeking)return status('请等当前帧显示后再捕获。');
 v.pause();await new Promise(requestAnimationFrame);send('snapshot',{save});
}
$('copyFrame').onclick=()=>snapshot(false);$('saveFrame').onclick=()=>snapshot(true);
$('vlc').onclick=()=>{if(isFullPreview())send('previewFullscreen',{on:false});v.pause();send('vlc',{playbackId,us:stamp()??0})};
$('play').onclick=()=>{if(v.paused)v.play().catch(e=>status(e.message,true));else v.pause()};
$('prev').onclick=()=>seekUs(frames[Math.max(0,index(shown)-1)]);$('next').onclick=()=>seekUs(frames[Math.min(frames.length-1,index(shown)+1)]);
function clearPlaybackLoop(){if(loopId){loopId=null;render()}}
$('rate').onchange=()=>{v.defaultPlaybackRate=v.playbackRate=Number($('rate').value)};
let lastAudible=100;
function displayVolume(value){
 v.volume=value/100;$('playbackVolume').value=value;$('playbackVolume').title=value+'%';
 if(value>0)lastAudible=value;
 setIconAction('mutePlayback',value?'speaker_2':'speaker_mute',value?'静音':'取消静音',value?'播放静音':'取消播放静音');
 $('mutePlayback').setAttribute('aria-pressed',String(value===0));
}
function changeVolume(value){displayVolume(value);send('playbackVolume',{value})}
$('playbackVolume').oninput=e=>changeVolume(Number(e.target.value));
$('mutePlayback').onclick=()=>changeVolume(Number($('playbackVolume').value)?0:lastAudible);
const isFullPreview=()=>document.body.classList.contains('previewFullscreen');
$('fullPreview').onclick=()=>send('previewFullscreen',{on:!isFullPreview()});
function showFullPreview(on){document.body.classList.toggle('previewFullscreen',on);setIconAction('fullPreview',on?'full_screen_minimize':'full_screen_maximize',on?'退出全屏':'全屏预览',on?'退出全屏（Esc）':'全屏预览');$('fullPreview').focus({preventScroll:true});layout()}
$('seek').oninput=e=>{clearPlaybackLoop();seekUs(+e.target.value,false)};$('zoom').onchange=e=>{$('timeline').style.width=e.target.value*100+'%';$('marks').scrollTop=0};
$('point').onclick=()=>newNote(imageDoc()?0:stamp(),null);$('inBtn').onclick=()=>{v.pause();inUs=stamp();render()};
$('outBtn').onclick=()=>{const end=stamp();if(end<=inUs)return status('终点必须晚于起点。',true);const a=inUs;inUs=null;newNote(a,end)};
$('edit').onclick=()=>{const n=doc.notes.find(n=>n.id===selected);seekUs(n.startUs);begin(n)};
$('del').onclick=()=>{const id=selected;selected=null;loopId=null;commit(doc.notes.filter(n=>n.id!==id))};
$('undo').onclick=()=>{doc.notes=history.pop();selected=null;loopId=null;send('notes',{notes:doc.notes});render()};
$('loop').onclick=()=>{const n=doc.notes.find(x=>x.id===selected);if(loopId){loopId=null;v.pause()}else{loopId=n.id;v.currentTime=n.startUs/1e6;v.play().catch(e=>status(e.message,true))}render()};
$('editor').onsubmit=e=>{e.preventDefault();const text=$('body').value.trim();if(imagePending)return status('正在载入图片…');if(!text&&!editing.pictures?.length&&!(imageDoc()&&editing.shape))return status('请填写批注或添加图片。',true);const n={...editing,text};selected=n.id;closeEditor();commit([...doc.notes.filter(x=>x.id!==n.id),n])};
$('cancel').onclick=()=>{closeEditor();render()};for(const k of ['box','arrow'])$(k).onclick=()=>setTool(k);$('clear').onclick=()=>{editing.shape=null;setTool(null);paint()};
document.onkeydown=e=>{if(e.key==='Escape'&&isFullPreview()){e.preventDefault();send('previewFullscreen',{on:false});return}if(busy||e.isComposing||e.repeat||e.target.closest('#playbackVolume')||(e.key===' '&&e.target.closest('#transport button')))return;if(isFullPreview()&&![' ','ArrowLeft','ArrowRight'].includes(e.key))return;if(!doc && $('openedImage').hidden && e.ctrlKey && e.key.toLowerCase()==='v'){e.preventDefault();send('pasteAsImage');return}if(editing){if(e.key==='Escape')$('cancel').click();if(e.ctrlKey&&e.key==='Enter')$('editor').requestSubmit();return}if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='o'){e.preventDefault();$('open').click();return}if(!$('openedImage').hidden && !e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey && ['ArrowLeft','ArrowRight'].includes(e.key)){e.preventDefault();send('adjacentImage',{direction:e.key==='ArrowLeft'?-1:1});return}if(!(ready||imageDoc())||e.target.closest('select,input[type=checkbox],#capturePanel')||(e.key===' '&&e.target.closest('#marks button')))return;
 const action=e.ctrlKey?({s:'save',z:'undo'})[e.key.toLowerCase()]:({m:'point',i:'inBtn',o:'outBtn',' ':'play',arrowleft:'prev',arrowright:'next'})[e.key.toLowerCase()];
 if(action){e.preventDefault();$(action).click()}else if(['ArrowDown','ArrowUp'].includes(e.key)&&doc.notes.length){e.preventDefault();const ns=[...doc.notes].sort((a,b)=>a.startUs-b.startUs||a.number-b.number);select(ns[Math.min(ns.length-1,Math.max(0,ns.findIndex(x=>x.id===selected)+(e.key==='ArrowDown'?1:-1)))].id)}};
chrome.webview.addEventListener('message',({data:m})=>{if(m.type==='volume'){displayVolume(m.value);return}if(m.type==='previewFullscreen'){showFullPreview(m.on);return}if(m.type==='timelineData')return;
 if(m.type==='image'){
  doc=m.doc;imageBase=m.images;$('noteActions').append($('point'));$('pointLabel').textContent='添加批注';$('modeLabel').textContent='图片审阅';ready=autoPlay=temporary=false;entryUs=0;frames=[];shown=0;playbackId=inUs=selected=loopId=null;history=[];
  v.pause();v.removeAttribute('src');v.load();closeEditor();$('welcome').hidden=true;document.body.classList.add('imageView');
  imageView.show(m.src,m.folder);render();status(m.name);return;
 }
 if(m.type==='loaded'){$('inBtn').before($('point'));$('pointLabel').textContent='点批注';$('modeLabel').textContent='视频审阅';imageView.reset();document.body.classList.remove('imageView');$('openedImage').hidden=true;$('openedImage').removeAttribute('src');autoPlay=m.autoPlay;displayVolume(m.volume);$('duration').textContent=fmt(m.doc.durationUs);$('rate').value='1';v.defaultPlaybackRate=v.playbackRate=1;playbackId=m.playbackId;entryUs=m.us;reviewProxy=m.proxy;imageBase=m.images;temporary=!!m.temporary;doc=m.doc;frames=m.frames;shown=frames[0];ready=false;inUs=selected=loopId=null;history=[];closeEditor();$('welcome').hidden=true;$('seek').max=doc.durationUs;$('seek').value=0;$('seek').style.removeProperty('--played');$('zoom').value='1';$('timeline').style.width='100%';v.src=m.src;render();status(m.recovered?'已恢复本地草稿':'正在打开视频…')}
 else if(m.type==='resumeReview'){seekUs(m.us);status(doc.fileName+(reviewProxy?' · 临时审阅预览':''))}
 else if(['catalog','meters','region','recording','recordShortcut','pictureStored'].includes(m.type))return;
 else if(m.type==='busy'){busy=m.value;document.body.classList.toggle('busy',busy);if(busy)status('正在处理文件…')}
 else status(m.text,m.type==='error')});render();



