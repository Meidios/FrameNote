-- FrameNote receiver v4
-- @description FrameNote: Import review at edit cursor
-- @version 0.3.0
-- Install alongside the .app file exported from FrameNote, or in its reaper directory.
local R = reaper
local script = debug.getinfo(1, "S").source:sub(2)
local function read(path)
  local f = io.open(path, "rb"); if not f then return nil end
  local data = f:read("*a"); f:close(); return data
end
local mode, language = ...
if not language then
  local profile = os.getenv("FRAMENOTE_DATA_DIR") or ((os.getenv("LOCALAPPDATA") or "") .. "/FrameNote")
  language = (read(profile .. "/updates.json") or ""):match('"language"%s*:%s*"([%w%-]+)"')
end
local function text(zh, en) return language == "en" and en or zh end
local function unhex(s) return (s:gsub("%x%x", function(h) return string.char(tonumber(h,16)) end)) end
local function quoted(s)
  assert(not s:find('["\r\n]'), text("路径无效。", "Invalid path"))
  return '"' .. s .. '"'
end
-- A take FX follows its PNG when the user moves, repeats or mutes the item.
-- The next item may itself have composited another simultaneous annotation.
local overlay=[[
// FrameNote: annotation overlay
colorspace='RGBA';
bg=input_next_item(0);
input_info(bg,w,h) ? (
  !project_wh_valid ? (project_w=w; project_h=h;);
  gfx_blit(bg,0);
  gfx_mode=0x10000;
  gfx_blit(0,0);
) : gfx_blit(0,0);
]]
local function copy_media(path,dest)
  local input=assert(io.open(path,"rb"))
  local part=dest..".part"
  local ok,err=pcall(function()
    local output=assert(io.open(part,"wb"))
    local copied,why=pcall(function()
      while true do
        local block,problem=input:read(1024*1024)
        if not block then assert(not problem,problem);break end
        assert(output:write(block))
      end
    end)
    local closed,problem=output:close();assert(copied,why);assert(closed,problem)
    assert(os.rename(part,dest))
  end)
  input:close()
  if not ok then os.remove(part);error(err) end
end

local function insert(lines,projectDir,copyKey,title,placement,jobKey,progress)
  local id,path,duration=lines[1],unhex(lines[2]),tonumber(lines[3])/1e6
  for i=0,R.CountMediaItems(0)-1 do
    local _,tag=R.GetSetMediaItemInfo_String(R.GetMediaItem(0,i),"P_EXT:FrameNoteReview","",false)
    if id~="-" and tag==id then return false,text("当前工程已包含这个审阅包。","This review package is already in the project.") end
  end
  local pos=R.GetCursorPosition()
  local track=R.GetSelectedTrack(0,0)
  if placement and placement~="cursor" then
    local x,y=placement:match("^drop\t(%-?%d+)\t(%-?%d+)$")
    x,y=tonumber(x),tonumber(y);assert(x and y,text("放置位置无效。","Invalid drop position."))
    local context
    track,context=R.GetThingFromPoint(x,y)
    assert(context and context:match("^arrange") and track~=R.GetMasterTrack(0),text("请将按钮拖到 REAPER 编排区的轨道上或轨道下方。","Drop on a track or below the tracks in the REAPER arrange view."))
    pos=math.max(0,(R.GetSet_ArrangeView2(0,false,x,x+1,0,0)))
  end

  if lines[4] and lines[4]:match("^title\t%x+$") then title=unhex(table.remove(lines,4):sub(7)) end
  title=title or path:match("([^/\\]+)$")
  local items={}
  if duration>0 then items[1]={kind="video",path=path,start=0,length=duration,offset=0,name=title,row=0} end
  local markers={}
  for i=4,#lines do
    local kind,a,len,off,file,name,ext,alt=lines[i]:match("^(%a+)\t(%d+)\t(%d+)\t(%d+)\t(%x+)\t(%x+)\t(%.[%w]+)\t?(%x*)$")
    if kind=="audio" or kind=="image" then
      local v={kind=kind,start=tonumber(a)/1e6,length=tonumber(len)/1e6,offset=tonumber(off)/1e6,path=unhex(file),name=unhex(name),ext=ext,alt=alt~="" and unhex(alt) or nil}
      if kind=="image" then
        v.loop=v.length>0
        if not v.loop then
          local src=assert(R.PCM_Source_CreateFromFile(v.path),v.path)
          v.length=R.GetMediaSourceLength(src);R.PCM_Source_Destroy(src)
          assert(v.length>0,text("无法读取静态图片时长。","Could not read the still image duration."))
        end
      end
      items[#items+1]=v
    else
      local begin,ending,label,rgb=lines[i]:match("^(%d+)\t(%-?%d+)\t(%x+)\t(%x%x%x%x%x%x)$")
      assert(rgb,text("标记颜色无效。","Invalid marker color."))
      local color=R.ColorToNative(tonumber(rgb:sub(1,2),16),tonumber(rgb:sub(3,4),16),tonumber(rgb:sub(5,6),16)) | 0x1000000
      markers[#markers+1]={start=tonumber(begin)/1e6,ending=tonumber(ending)/1e6,name=unhex(label),color=color}
    end
  end
  -- Images sit above annotation audio. Overlapping audio clips each get a visible row.
  local rowCount=duration>0 and 1 or 0
  for _,kind in ipairs({"image","audio"}) do
    local ends={}
    local matching={}
    for _,v in ipairs(items) do if v.kind==kind then matching[#matching+1]=v end end
    table.sort(matching,function(a,b)return a.start<b.start end)
    for _,v in ipairs(matching) do
      local row=1
      while ends[row] and ends[row]>v.start do row=row+1 end
      ends[row]=v.start+v.length;v.row=rowCount+row-1
    end
    rowCount=rowCount+#ends
  end

  local first,last=math.huge,0
  for _,v in ipairs(items) do first=math.min(first,v.start);last=math.max(last,v.start+v.length) end
  for _,m in ipairs(markers) do first=math.min(first,m.start);last=math.max(last,m.start,m.ending) end
  assert(last>first,text("时间线上没有可导入的内容。","There is no timeline content to import."))
  markers[#markers+1]={start=first,ending=last,name=title,color=0}
  local copied,files={},{}
  local function durable(file,ext,copy)
    if not copy and file:lower():sub(-#ext)==ext:lower() then return file end
    if copied[file] then return copied[file] end
    local folder=projectDir.."/FrameNote Media"
    R.RecursiveCreateDirectory(folder,0)
    local key=jobKey or R.genGuid():gsub("[{}]","")
    local dest=folder.."/"..key.."-"..(#files+1)..ext
    copy_media(file,dest);files[#files+1]=dest;copied[file]=dest;return dest
  end
  local added,addedMarkers={},{}
  local created,oldMode,oldLanes
  R.Undo_BeginBlock2(0)
  R.PreventUIRefresh(1)
  local ok,err=pcall(function()
    if not track then
      local n=R.CountTracks(0);R.InsertTrackAtIndex(n,true);track=assert(R.GetTrack(0,n));created=true
      R.GetSetMediaTrackInfo_String(track,"P_NAME","FrameNote",true)
    end
    oldMode=R.GetMediaTrackInfo_Value(track,"I_FREEMODE")
    if rowCount>1 then
      if oldMode==2 then
        oldLanes=R.GetMediaTrackInfo_Value(track,"I_NUMFIXEDLANES")
        R.SetMediaTrackInfo_Value(track,"I_NUMFIXEDLANES",oldLanes+rowCount)
        for row=0,rowCount-1 do R.SetMediaTrackInfo_Value(track,"C_LANEPLAYS:"..(oldLanes+row),2) end
      elseif oldMode==0 then R.SetMediaTrackInfo_Value(track,"I_FREEMODE",1) end
      R.UpdateTimeline()
    end
    for i,v in ipairs(items) do
      local file=durable(v.path,v.ext or assert(v.path:match("(%.[%w]+)$")),v.kind=="video" and copyKey or v.kind~="video" and jobKey)
      local item=assert(R.AddMediaItemToTrack(track));added[#added+1]=item
      local src=R.PCM_Source_CreateFromFile(file)
      if src and v.kind~="image" and R.GetMediaSourceLength(src)<=0 then R.PCM_Source_Destroy(src);src=nil end
      if not src and v.alt and v.alt~=v.path then
        file=durable(v.alt,assert(v.alt:match("(%.[%w]+)$")),jobKey);src=R.PCM_Source_CreateFromFile(file)
      end
      assert(src,text("REAPER 无法打开媒体：","REAPER cannot open media: ")..file)
      local take=R.AddTakeToMediaItem(item)
      if not take then R.PCM_Source_Destroy(src);error("Could not create take") end
      R.SetMediaItemTake_Source(take,src)
      R.SetMediaItemTakeInfo_Value(take,"D_STARTOFFS",v.offset)
      R.GetSetMediaItemTakeInfo_String(take,"P_NAME",v.name,true)
      R.SetMediaItemInfo_Value(item,"D_POSITION",pos+v.start)
      R.SetMediaItemInfo_Value(item,"D_LENGTH",v.length)
      R.SetMediaItemInfo_Value(item,"B_LOOPSRC",v.loop and 1 or 0)
      if v.kind=="image" then
        local fx=R.TakeFX_AddByName(take,"Video processor",-1)
        assert(fx>=0 and R.TakeFX_SetNamedConfigParm(take,fx,"VIDEO_CODE",overlay),
          text("无法启用 REAPER 的视频叠加处理器。","Could not enable the REAPER video overlay processor."))
        R.SetMediaItemInfo_Value(item,"D_FADEINLEN",0)
        R.SetMediaItemInfo_Value(item,"D_FADEOUTLEN",0)
      end
      R.SetMediaItemInfo_Value(item,"C_BEATATTACHMODE",0)
      if rowCount>1 then
        R.SetMediaItemInfo_Value(item,"I_MIXFLAG",1)
        if oldMode==2 then R.SetMediaItemInfo_Value(item,"I_FIXEDLANE",oldLanes+v.row)
        else
          R.SetMediaItemInfo_Value(item,"F_FREEMODE_Y",v.row/rowCount)
          R.SetMediaItemInfo_Value(item,"F_FREEMODE_H",1/rowCount)
        end
      end
      if i==1 and id~="-" then R.GetSetMediaItemInfo_String(item,"P_EXT:FrameNoteReview",id,true) end
      if progress then progress(i/#items) end
    end
    for _,m in ipairs(markers) do
      local region=m.ending>=0
      local idx=R.AddProjectMarker2(0,region,pos+m.start,region and pos+m.ending or 0,m.name,-1,m.color)
      assert(idx>=0,text("无法添加标记。","Could not add a marker."));addedMarkers[#addedMarkers+1]={idx,region}
    end
  end)
  if not ok then
    for _,m in ipairs(addedMarkers) do R.DeleteProjectMarker(0,m[1],m[2]) end
    if created then R.DeleteTrack(track)
    elseif track then
      for _,item in ipairs(added) do R.DeleteTrackMediaItem(track,item) end
      if oldLanes then R.SetMediaTrackInfo_Value(track,"I_NUMFIXEDLANES",oldLanes) end
      if oldMode then R.SetMediaTrackInfo_Value(track,"I_FREEMODE",oldMode) end
    end
    for _,file in ipairs(files) do os.remove(file) end
  end
  R.PreventUIRefresh(-1);R.TrackList_AdjustWindows(false);R.UpdateTimeline();R.UpdateArrange()
  R.Undo_EndBlock2(0,text("FrameNote：导入审阅包","FrameNote: import review"),-1)
  if not ok then error(err) end
  return true
end
local function import(package, projectDir)
  local app = read(script .. ".app") or (script:match("^(.*)[/\\]") .. "\\..\\FrameNote.exe")
  local tmp = os.tmpname()
  local cmd = table.concat({quoted(app), "--prepare-reaper", quoted(package), quoted(projectDir .. "\\FrameNote Media"), quoted(tmp), "--layers"}, " ")
  local reply = R.ExecProcess(cmd, 0) or ""
  local code = tonumber(reply:match("^%s*(%-?%d+)"))
  local body = read(tmp); os.remove(tmp)
  if code~=0 then local err=read(tmp..".error"); os.remove(tmp..".error"); error(err or text("FrameNote 无法读取审阅包。如果移动了应用，请重新导出脚本。", "FrameNote could not read the package. Export the action again if the app moved. ") .. reply) end
  assert(body, text("没有导入数据。", "No import data."))
  local lines={}; for l in body:gmatch("[^\r\n]+") do lines[#lines+1]=l end
  return insert(lines,projectDir)
end
if mode=="receive" then return function(request,dir,key,progress)
  local lines={request[3],request[4],request[5]}
  for i=9,#request do lines[#lines+1]=request[i] end
  return insert(lines,dir,request[6]=="copy" and key or nil,unhex(request[7]),request[8],key,progress)
end end
if mode=="module" then return import end
local _,rpp=R.EnumProjects(-1,"")
if not rpp or rpp=="" then R.MB(text("请先保存当前工程，再导入审阅包。", "Save the current project before importing a review package."),"FrameNote",0); return end
local ok,file=R.GetUserFileNameForRead("",text("FrameNote：导入审阅包", "FrameNote: Import review"),"framenote")
if ok then
  local ran,result,msg=pcall(import,file,R.GetProjectPathEx(0))
  if not ran or not result then R.MB(msg or tostring(result),"FrameNote",0) end
end


