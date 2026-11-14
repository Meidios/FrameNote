-- FrameNote receiver v1
-- @description FrameNote: Import review at edit cursor
-- @version 0.1.0
-- Install alongside the .app file exported from FrameNote, or in its reaper directory.
local R = reaper
local script = debug.getinfo(1, "S").source:sub(2)
local function read(path)
  local f = io.open(path, "rb"); if not f then return nil end
  local data = f:read("*a"); f:close(); return data
end
local function unhex(s) return (s:gsub("%x%x", function(h) return string.char(tonumber(h,16)) end)) end
local function quoted(s)
  assert(not s:find('["\r\n]'), "Invalid path")
  return '"' .. s .. '"'
end
local function insert(lines,projectDir,copyKey,title)
  local id,path,duration=lines[1],unhex(lines[2]),tonumber(lines[3])/1e6
  for i=0,R.CountMediaItems(0)-1 do
    local _,tag=R.GetSetMediaItemInfo_String(R.GetMediaItem(0,i),"P_EXT:FrameNoteReview","",false)
    if id~="-" and tag==id then return false,"当前工程已包含这个审阅包。" end
  end
  local pos=R.GetCursorPosition()
  local track=R.GetSelectedTrack(0,0)
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
  local src=assert(R.PCM_Source_CreateFromFile(path),"REAPER cannot open this video.")
  if R.GetMediaSourceLength(src)<=0 then R.PCM_Source_Destroy(src); error("No playable media stream.") end
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
    R.GetSetMediaItemTakeInfo_String(take,"P_NAME",title or "FrameNote review",true)
    if id~="-" then R.GetSetMediaItemInfo_String(item,"P_EXT:FrameNoteReview",id,true) end
    local start=R.GetMediaItemInfo_Value(item,"D_POSITION")
    for i=4,#lines do
      local a,b,text=lines[i]:match("^(%d+)\t(%-?%d+)\t(%x+)$")
      local region=tonumber(b)>=0
      local idx=R.AddProjectMarker2(0,region,start+tonumber(a)/1e6,region and (start+tonumber(b)/1e6) or 0,unhex(text),-1,0)
      assert(idx>=0,"Could not add a marker."); added[#added+1]={idx,region}
    end
  end)
  if not ok then
    for _,m in ipairs(added) do R.DeleteProjectMarker(0,m[1],m[2]) end
    if created then R.DeleteTrack(track) elseif item then R.DeleteTrackMediaItem(track,item) end
    if not transferred then R.PCM_Source_Destroy(src) end
  end
  R.TrackList_AdjustWindows(false); R.UpdateArrange()
  R.Undo_EndBlock2(0,"FrameNote: import review",-1)
  if not ok then error(err) end
  return true
end
local function import(package, projectDir)
  local app = read(script .. ".app") or (script:match("^(.*)[/\\]") .. "\\..\\FrameNote.exe")
  local tmp = os.tmpname()
  local cmd = table.concat({quoted(app), "--prepare-reaper", quoted(package), quoted(projectDir .. "\\FrameNote Media"), quoted(tmp)}, " ")
  local reply = R.ExecProcess(cmd, 0) or ""
  local code = tonumber(reply:match("^%s*(%-?%d+)"))
  local body = read(tmp); os.remove(tmp)
  if code~=0 then local err=read(tmp..".error"); os.remove(tmp..".error"); error(err or "FrameNote could not read the package. Export the action again if the app moved. " .. reply) end
  assert(body, "No import data.")
  local lines={}; for l in body:gmatch("[^\r\n]+") do lines[#lines+1]=l end
  return insert(lines,projectDir)
end
if ...=="receive" then return function(request,dir,key)
  local lines={request[3],request[4],request[5]}
  for i=8,#request do lines[#lines+1]=request[i] end
  return insert(lines,dir,request[6]=="copy" and key or nil,unhex(request[7]))
end end
if ...=="module" then return import end
local _,rpp=R.EnumProjects(-1,"")
if not rpp or rpp=="" then R.MB("请先保存当前工程，再导入审阅包。","FrameNote",0); return end
local ok,file=R.GetUserFileNameForRead("","FrameNote: Import review","framenote")
if ok then
  local ran,result,msg=pcall(import,file,R.GetProjectPathEx(0))
  if not ran or not result then R.MB(msg or tostring(result),"FrameNote",0) end
end


