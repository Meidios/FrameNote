-- FrameNote receiver v2
-- @description FrameNote: Import review at edit cursor
-- @version 0.2.0
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
local function insert(lines,projectDir,copyKey,title,placement)
  local id,path,duration=lines[1],unhex(lines[2]),tonumber(lines[3])/1e6
  for i=0,R.CountMediaItems(0)-1 do
    local _,tag=R.GetSetMediaItemInfo_String(R.GetMediaItem(0,i),"P_EXT:FrameNoteReview","",false)
    if id~="-" and tag==id then return false,text("当前工程已包含这个审阅包。", "This review package is already in the project.") end
  end
  local pos=R.GetCursorPosition()
  local track=R.GetSelectedTrack(0,0)
  if placement and placement~="cursor" then
    local x,y=placement:match("^drop\t(%-?%d+)\t(%-?%d+)$")
    x,y=tonumber(x),tonumber(y);assert(x and y,text("放置位置无效。", "Invalid drop position."))
    local context
    track,context=R.GetThingFromPoint(x,y)
    assert(context and context:match("^arrange") and track~=R.GetMasterTrack(0),text("请将按钮拖到 REAPER 编排区的轨道上或轨道下方。", "Drop on a track or below the tracks in the REAPER arrange view."))
    pos=math.max(0,(R.GetSet_ArrangeView2(0,false,x,x+1,0,0)))
  end
  if copyKey then
    local folder=projectDir.."/FrameNote Media"
    R.RecursiveCreateDirectory(folder,0)
    local dest=folder.."/"..copyKey..assert(path:match("(%.[%w]+)$"))
    local input=assert(io.open(path,"rb"))
    local ok,err=pcall(function()
      local out=assert(io.open(dest..".part","wb"))
      local copied,why=pcall(function()
        while true do local block,err=input:read(1048576); if not block then assert(not err,err);break end;assert(out:write(block)) end
      end)
      local closed,whyClose=out:close();assert(copied,why);assert(closed,whyClose);assert(os.rename(dest..".part",dest))
    end)
    input:close();if not ok then os.remove(dest..".part");error(err) end
    path=dest
  end
  -- Build the item explicitly to avoid auto-tempo, ripple and insertion preferences.
  local src=assert(R.PCM_Source_CreateFromFile(path),text("REAPER 无法打开这个视频。", "REAPER cannot open this video."))
  if R.GetMediaSourceLength(src)<=0 then R.PCM_Source_Destroy(src); error(text("没有可播放的媒体流。", "No playable media stream.")) end
  R.Undo_BeginBlock2(0)
  local created,item,transferred
  local added={}
  local ok,err=pcall(function()
    if not track then
    local n=R.CountTracks(0); R.InsertTrackAtIndex(n,true); track=assert(R.GetTrack(0,n))
    created=true;R.GetSetMediaTrackInfo_String(track,"P_NAME","FrameNote",true)
    end
    item=assert(R.AddMediaItemToTrack(track)); local take=assert(R.AddTakeToMediaItem(item))
    R.SetMediaItemTake_Source(take,src);transferred=true
    R.SetMediaItemInfo_Value(item,"D_POSITION",pos); R.SetMediaItemInfo_Value(item,"D_LENGTH",duration)
    R.SetMediaItemInfo_Value(item,"C_BEATATTACHMODE",0)
    R.GetSetMediaItemTakeInfo_String(take,"P_NAME",title or text("FrameNote 审阅", "FrameNote review"),true)
    if id~="-" then R.GetSetMediaItemInfo_String(item,"P_EXT:FrameNoteReview",id,true) end
    local start=R.GetMediaItemInfo_Value(item,"D_POSITION")
    for i=4,#lines do
      local a,b,hexName,rgb=lines[i]:match("^(%d+)\t(%-?%d+)\t(%x+)\t(%x%x%x%x%x%x)$")
      assert(rgb,text("标记颜色无效。", "Invalid marker color."))
      local color=R.ColorToNative(tonumber(rgb:sub(1,2),16),tonumber(rgb:sub(3,4),16),tonumber(rgb:sub(5,6),16)) | 0x1000000
      local region=tonumber(b)>=0
      local idx=R.AddProjectMarker2(0,region,start+tonumber(a)/1e6,region and (start+tonumber(b)/1e6) or 0,unhex(hexName),-1,color)
      assert(idx>=0,text("无法添加标记。", "Could not add a marker.")); added[#added+1]={idx,region}
    end
  end)
  if not ok then
    for _,m in ipairs(added) do R.DeleteProjectMarker(0,m[1],m[2]) end
    if created then R.DeleteTrack(track) elseif item then R.DeleteTrackMediaItem(track,item) end
    if not transferred then R.PCM_Source_Destroy(src) end
  end
  R.TrackList_AdjustWindows(false); R.UpdateArrange()
  R.Undo_EndBlock2(0,text("FrameNote：导入审阅包", "FrameNote: import review"),-1)
  if not ok then error(err) end
  return true
end
local function import(package, projectDir)
  local app = read(script .. ".app") or (script:match("^(.*)[/\\]") .. "\\..\\FrameNote.exe")
  local tmp = os.tmpname()
  local cmd = table.concat({quoted(app), "--prepare-reaper", quoted(package), quoted(projectDir .. "\\FrameNote Media"), quoted(tmp), "--colors"}, " ")
  local reply = R.ExecProcess(cmd, 0) or ""
  local code = tonumber(reply:match("^%s*(%-?%d+)"))
  local body = read(tmp); os.remove(tmp)
  if code~=0 then local err=read(tmp..".error"); os.remove(tmp..".error"); error(err or text("FrameNote 无法读取审阅包。如果移动了应用，请重新导出脚本。", "FrameNote could not read the package. Export the action again if the app moved. ") .. reply) end
  assert(body, text("没有导入数据。", "No import data."))
  local lines={}; for l in body:gmatch("[^\r\n]+") do lines[#lines+1]=l end
  return insert(lines,projectDir)
end
if mode=="receive" then return function(request,dir,key)
  local lines={request[3],request[4],request[5]}
  for i=9,#request do lines[#lines+1]=request[i] end
  return insert(lines,dir,request[6]=="copy" and key or nil,unhex(request[7]),request[8])
end end
if mode=="module" then return import end
local _,rpp=R.EnumProjects(-1,"")
if not rpp or rpp=="" then R.MB(text("请先保存当前工程，再导入审阅包。", "Save the current project before importing a review package."),"FrameNote",0); return end
local ok,file=R.GetUserFileNameForRead("",text("FrameNote：导入审阅包", "FrameNote: Import review"),"framenote")
if ok then
  local ran,result,msg=pcall(import,file,R.GetProjectPathEx(0))
  if not ran or not result then R.MB(msg or tostring(result),"FrameNote",0) end
end


