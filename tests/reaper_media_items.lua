-- Check the package produced by media_delivery_ui.py in a disposable REAPER project.
local r=reaper
local dir=assert(os.getenv("FRAMENOTE_TEST_DIR"))
local function read(path)
  local f=assert(io.open(path,"rb"));local body=f:read("*a");f:close();return body
end
local function near(actual,expected) assert(math.abs(actual-expected)<.000002,actual.." vs "..expected) end
local function finish(message)
  local f=assert(io.open(dir.."/reaper-result.txt","wb"));f:write(message);f:close()
  r.Main_SaveProject(0,false)
  r.defer(function() r.Main_OnCommand(40004,0) end)
end
local function run()
  local import=assert(loadfile(os.getenv("FRAMENOTE_TEST_ACTION")))("module")
  local package=read(dir.."/latest.txt"):match("^[^\r\n]+")
  local function check(fixed)
    r.Undo_BeginBlock2(0)
    r.InsertTrackAtIndex(0,true);local track=r.GetTrack(0,0)
    r.SetOnlyTrackSelected(track);r.SetEditCurPos(12,false,false)
    if fixed then r.SetMediaTrackInfo_Value(track,"I_FREEMODE",2);r.UpdateTimeline() end
    r.Undo_EndBlock2(0,"Set up target track",-1)
    local before=r.GetMediaTrackInfo_Value(track,"I_NUMFIXEDLANES")
    assert(import(package,dir))
    assert(r.CountTracks(0)==1 and r.CountTrackMediaItems(track)==7)
    near(r.GetMediaTrackInfo_Value(track,"I_FREEMODE"),fixed and 2 or 1)
    local counts={png=0,mp3=0,wav=0,mp4=0}
    for i=0,6 do
      local item=r.GetTrackMediaItem(track,i);local take=r.GetActiveTake(item)
      local file=r.GetMediaSourceFileName(r.GetMediaItemTake_Source(take),"")
      assert(io.open(file,"rb")):close()
      local ext=file:match("%.(%w+)$"):lower();counts[ext]=counts[ext]+1
      local pos=r.GetMediaItemInfo_Value(item,"D_POSITION")
      local offset=r.GetMediaItemTakeInfo_Value(take,"D_STARTOFFS")
      local length=r.GetMediaItemInfo_Value(item,"D_LENGTH")
      -- Older REAPER versions expose mix behavior only through the item state chunk.
      if fixed then
        local lane=r.GetMediaItemInfo_Value(item,"I_FIXEDLANE")
        assert(lane>=before and r.GetMediaTrackInfo_Value(track,"C_LANEPLAYS:"..math.floor(lane))>0)
      else near(r.GetMediaItemInfo_Value(item,"F_FREEMODE_H"),.2) end
      if ext=="mp3" then near(pos,12.5);near(length,1);near(offset,0)
      elseif ext=="wav" then near(pos,12.75);near(length,1);near(offset,.5)
      elseif ext=="mp4" then near(pos,12);near(length,2);near(r.GetMediaSourceLength(r.GetMediaItemTake_Source(take)),2)
      else
        local point=math.abs(pos-16.5)<.000001
        local sourceLength=r.GetMediaSourceLength(r.GetMediaItemTake_Source(take))
        near(length,point and sourceLength or (pos==15 and 1.4 or 1))
        near(r.GetMediaItemInfo_Value(item,"B_LOOPSRC"),point and 0 or 1)
      end
    end
    assert(counts.png==4 and counts.mp3==1 and counts.wav==1 and counts.mp4==1)
    local accessor=r.CreateTrackAudioAccessor(track)
    local samples=r.new_array(4800*2)
    local read=r.GetAudioAccessorSamples(accessor,48000,2,12.8,4800,samples)
    assert(read==1,"Could not sample the imported track")
    local data=samples.table();r.DestroyAudioAccessor(accessor)
    for _,frequency in ipairs({220,440,880}) do
      local re,im=0,0
      for j=0,4799 do
        local angle=j*frequency*2*math.pi/48000
        re=re+data[j*2+1]*math.cos(angle);im=im+data[j*2+1]*math.sin(angle)
      end
      assert(math.sqrt(re*re+im*im)/4800>.009,"Missing frequency "..frequency.." in REAPER track")
    end
    local total,marks,regions=r.CountProjectMarkers(0);assert(marks==1 and regions==4)
    local last=0
    for i=0,6 do local item=r.GetTrackMediaItem(track,i)
      last=math.max(last,r.GetMediaItemInfo_Value(item,"D_POSITION")+r.GetMediaItemInfo_Value(item,"D_LENGTH"))
    end
    local found=false
    for i=0,total-1 do local _,region,a,b,name=r.EnumProjectMarkers3(0,i)
      if name=="source.mp4" then assert(region);near(a,12);near(b,last);found=true end
    end
    assert(found)
    r.Main_SaveProjectEx(0,dir..(fixed and "/fixed.rpp" or "/free.rpp"),0)
    r.Undo_DoUndo2(0);assert(r.CountMediaItems(0)==0);r.DeleteTrack(r.GetTrack(0,0))
  end
  check(false)
  if tonumber(r.GetAppVersion():match("^%d+"))>=7 then check(true) end
  -- Inspect the final video that REAPER passes downstream, with two annotations active.
  r.InsertTrackAtIndex(0,true);local track=r.GetTrack(0,0)
  if tonumber(r.GetAppVersion():match("^%d+"))>=7 then r.SetMediaTrackInfo_Value(track,"I_FREEMODE",2) end
  r.SetOnlyTrackSelected(track);r.SetEditCurPos(0,false,false);assert(import(package,dir))
  local fx=r.TrackFX_AddByName(track,"Video processor",false,-1)
  assert(r.TrackFX_SetNamedConfigParm(track,fx,"VIDEO_CODE",[[
// FrameNote regression pixel sampler
//@gmem=FrameNote_Reaper_Test
colorspace='RGBA';
gfx_getpixel(0,310,40,r,g,b); gmem[1]=b; gmem[2]=r;
gfx_getpixel(0,32,60,r,g,b); gmem[3]=r;
gfx_getpixel(0,224,80,r,g,b); gmem[4]=r;
gmem[0]=project_time;
gfx_blit(0);
]]))
  r.gmem_attach("FrameNote_Reaper_Test");r.gmem_write(0,0)
  if r.GetToggleCommandState(50125)~=1 then r.Main_OnCommand(50125,0) end
  r.SetEditCurPos(.9,true,true);r.UpdateArrange()
  local deadline=r.time_precise()+6
  local function sample()
    if r.gmem_read(0)>.75 and r.gmem_read(0)<1.5 and r.gmem_read(1)>240 and r.gmem_read(2)<10
       and r.gmem_read(3)>230 and r.gmem_read(4)>230 then
      finish("PASS "..r.GetAppVersion().." edited length, overall region, source loops, composited PNGs, mixed audio and undo")
    elseif r.time_precise()<deadline then r.defer(sample)
    else finish("Unexpected video pixels or time: "..table.concat({r.gmem_read(0),r.gmem_read(1),r.gmem_read(2),r.gmem_read(3),r.gmem_read(4)},", ")) end
  end
  r.defer(sample)
end
local ok,result=xpcall(run,debug.traceback)
if not ok then finish(tostring(result)) end
