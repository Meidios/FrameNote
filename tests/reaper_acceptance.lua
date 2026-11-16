-- Run only in a fresh, isolated portable REAPER instance with FRAMENOTE_TEST_DIR set.
local r=reaper
local dir=assert(os.getenv("FRAMENOTE_TEST_DIR"))
local function read(p) local f=assert(io.open(p,"rb")); local s=f:read("*a"); f:close();return s end
local function near(actual,expected) assert(math.abs(actual-expected)<0.000002,actual.." vs "..expected) end
local function check()
  local action=assert(loadfile(assert(os.getenv("FRAMENOTE_TEST_ACTION"))))("module")
  local package=read(dir.."\\latest.txt"):match("^[^\r\n]+")
  assert(r.CountTracks(0)==0,"Test requires a blank project")
  r.Undo_BeginBlock2(0)
  r.InsertTrackAtIndex(0,true)
  r.GetSetMediaTrackInfo_String(r.GetTrack(0,0),"P_NAME","Existing track",true)
  r.AddProjectMarker2(0,false,4.5,0,"Existing marker",-1,0)
  r.Undo_EndBlock2(0,"Existing project setup",-1)
  r.SetEditCurPos(60,false,false)
  assert(action(package,dir))
  assert(r.CountTracks(0)==2 and r.CountMediaItems(0)==1)
  local item=r.GetMediaItem(0,0);near(r.GetMediaItemInfo_Value(item,"D_POSITION"),60)
  local src=r.GetMediaItemTake_Source(r.GetActiveTake(item))
  local path=r.GetMediaSourceFileName(src,"");assert(path:find("FrameNote Media",1,true),path)
  local video,h=r.GetMediaSourceType(src,""),r.GetMediaSourceLength(src)
  assert(h>2.9 and h<3.1,"Duration: "..h)
  local _,num,regions=r.CountProjectMarkers(0);assert(num==2 and regions==1)
  local expected=read(dir.."\\expected.tsv")
  for i=1,2 do
    local _,isrgn,pos,ending,name,_,color=r.EnumProjectMarkers3(0,i)
    local a,b,rgb,text=expected:match("^(%d+)\t(%-?%d+)\t(%x+)\t([^\r\n]+)")
    local red,green,blue=r.ColorFromNative(color)
    assert(color & 0x1000000 ~= 0 and string.format("%02x%02x%02x",red,green,blue)==rgb:lower(),"Wrong color: "..name)
    assert(name==text,name.." vs "..text);near(pos,60+tonumber(a)/1e6)
    assert(isrgn==(tonumber(b)>=0));if isrgn then near(ending,60+tonumber(b)/1e6) end
    expected=expected:gsub("^[^\n]*\n?","",1)
  end
  assert(action(package,dir)==false,"Duplicate import permitted")
  r.Main_SaveProjectEx(0,dir.."\\verified-import.rpp",0)
  assert(r.Undo_CanUndo2(0)=="FrameNote: import review",tostring(r.Undo_CanUndo2(0)))
  r.Undo_DoUndo2(0)
  assert(r.CountTracks(0)==1 and r.CountMediaItems(0)==0, "Undo: tracks="..r.CountTracks(0).." items="..r.CountMediaItems(0))
  local _,nm,nr=r.CountProjectMarkers(0);assert(nm==1 and nr==0)
  -- The click receiver must still use the selected track and edit cursor.
  local receive=assert(loadfile(os.getenv("FRAMENOTE_TEST_ACTION")))("receive")
  local function hex(s) return (s:gsub(".",function(c)return string.format("%02X",c:byte())end)) end
  r.SetOnlyTrackSelected(r.GetTrack(0,0));r.SetEditCurPos(17.25,false,false)
  assert(receive({"","","-",hex(path),"3000000","link",hex("Click test"),"cursor",
      "250000\t-1\t41\t1289FE","500000\t1250000\t42\tE42639"},dir,"unused"))
  assert(r.CountTrackMediaItems(r.GetTrack(0,0))==1)
  near(r.GetMediaItemInfo_Value(r.GetMediaItem(0,0),"D_POSITION"),17.25)
  for i,rgb in ipairs({{18,137,254},{228,38,57}}) do
    local _,region,a,b,_,_,color=r.EnumProjectMarkers3(0,i)
    near(a,i==1 and 17.5 or 17.75);if region then near(b,18.5) end
    assert(color==(r.ColorToNative(table.unpack(rgb)) | 0x1000000))
  end
  r.Undo_DoUndo2(0);assert(r.CountMediaItems(0)==0)
  assert(action(package,dir),"Undo left duplicate tag")
  return "PASS "..r.GetAppVersion().." source="..video.." duration="..h.." media="..path
end
local ok,result=xpcall(check,debug.traceback)
local out=assert(io.open(dir.."\\reaper-result.txt","wb"));out:write(tostring(result));out:close()
if ok then r.Main_SaveProject(0,false) end
r.defer(function() r.Main_OnCommand(40004,0) end)



