"""Exercise the cross-window mouse gesture against a disposable portable REAPER project."""
import json, os, subprocess, sys, time, zipfile
from pathlib import Path
import win32api as api, win32gui as gui, win32process as processes, win32con as C
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import app, fixtures, work, launch, stop, wait

portable=Path(sys.argv[3]).resolve()
assert portable.is_relative_to(Path("artifacts").resolve()) and (portable/"reaper.ini").exists()
profile=work/"profile";profile.mkdir()
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
(profile/"updates.json").write_text('{"automatic":false}')
project=work/"drag-test.rpp";project.write_text('<REAPER_PROJECT 0.1 "6.82" 0\n>\n')
source=Path((fixtures/"latest.txt").read_text(encoding="utf8").strip())
with zipfile.ZipFile(source) as z: review=json.loads(z.read("review.json"))
script=work/"observe.lua"
script.write_text(r'''local r=reaper
local dir=os.getenv("FRAMENOTE_DROP_TEST")
local function save(name,s) local f=assert(io.open(dir.."/"..name,"wb"));f:write(s);f:close() end
r.InsertTrackAtIndex(0,true);r.InsertTrackAtIndex(1,true)
for i=0,1 do r.SetMediaTrackInfo_Value(r.GetTrack(0,i),"I_HEIGHTOVERRIDE",75) end
r.TrackList_AdjustWindows(false);r.SetOnlyTrackSelected(r.GetTrack(0,0));r.SetEditCurPos(17.25,false,false)
save("ready",tostring(r.GetMainHwnd()))
local found=false
local function observe()
 if not found then
  local f=io.open(dir.."/points","rb")
  if f then
   for l in f:lines() do
    local a,b=l:match("(%-?%d+) (%-?%d+)")
    local x,y=tonumber(a),tonumber(b)
    local track,ctx=r.GetThingFromPoint(x,y)
    if ctx and ctx:match("^arrange") and track==r.GetTrack(0,1) then
     save("expected",string.format("%d %d %.9f",x,y,(r.GetSet_ArrangeView2(0,false,x,x+1,0,0))));found=true;break
    end
   end;f:close()
  end
 end
 if r.CountMediaItems(0)>0 then
  local item=r.GetMediaItem(0,0)
  local out={string.format("%d %.9f %.9f",r.CountTrackMediaItems(r.GetTrack(0,1)),r.GetMediaItemInfo_Value(item,"D_POSITION"),r.GetCursorPosition())}
  local count=r.CountProjectMarkers(0)
  for i=0,count-1 do local _,region,a,b,n,_,color=r.EnumProjectMarkers3(0,i);local red,g,blue=r.ColorFromNative(color)
   out[#out+1]=string.format("%.9f %.9f %02x%02x%02x",a,region and b or -1,red,g,blue) end
  save("done",table.concat(out,"\n"));return
 end
 r.defer(observe)
end
r.defer(observe)''',encoding="utf8")
proc=subprocess.Popen([str(portable/"reaper.exe"),"-newinst","-nosplash",str(project),str(script)],env=dict(os.environ,FRAMENOTE_DROP_TEST=str(work)))
def windows(pid,cls):
 found=[]
 gui.EnumWindows(lambda h,_:found.append(h) if processes.GetWindowThreadProcessId(h)[1]==pid and gui.GetClassName(h)==cls else None,None)
 return found
def child(hwnd,cls):
 found=[];gui.EnumChildWindows(hwnd,lambda h,_:found.append(h) if gui.GetClassName(h)==cls and gui.IsWindowVisible(h) else None,None)
 return found[0]
def until(predicate):
 for _ in range(180):
  if predicate():return
  time.sleep(.08)
 raise AssertionError(f"Timed out in {work}")
def setup_ready():
 for h in windows(proc.pid,"#32770"):
  if gui.GetWindowText(h)=="Scanning VST plugins...":gui.PostMessage(h,C.WM_COMMAND,2,0)
  labels=[];gui.EnumChildWindows(h,lambda w,_:labels.append(gui.GetWindowText(w)),None)
  if any("not yet selected an audio device" in t for t in labels):gui.PostMessage(h,C.WM_COMMAND,7,0)
 return (work/"ready").exists()
def drag(start,end,cancel=False):
 api.SetCursorPos(start);api.mouse_event(C.MOUSEEVENTF_LEFTDOWN,0,0)
 try:
  for i in range(1,13):
   api.SetCursorPos(tuple(round(a+(b-a)*i/12) for a,b in zip(start,end)));time.sleep(.035)
  if cancel:
   api.keybd_event(C.VK_ESCAPE,0);api.keybd_event(C.VK_ESCAPE,0,C.KEYEVENTF_KEYUP);time.sleep(.1)
 finally:api.mouse_event(C.MOUSEEVENTF_LEFTUP,0,0)
try:
 until(setup_ready)
 rh=windows(proc.pid,"REAPERwnd")[0];gui.ShowWindow(rh,C.SW_RESTORE)
 width=api.GetSystemMetrics(0);height=api.GetSystemMetrics(1)
 gui.MoveWindow(rh,max(0,width-940),0,940,min(height-50,900),True)
 with sync_playwright() as pw:
  desk,page=launch(pw,source)
  try:
   handles=[];gui.EnumWindows(lambda h,_:handles.append(h) if processes.GetWindowThreadProcessId(h)[1]==desk.pid and gui.IsWindowVisible(h) and "FrameNote" in gui.GetWindowText(h) else None,None)
   dh=handles[0];gui.ShowWindow(dh,C.SW_RESTORE);gui.MoveWindow(dh,0,0,840,min(height-50,730),True)
   deskview=child(dh,"Chrome_RenderWidgetHostHWND")
   button=page.locator("#sendReaper")
   button.screenshot(path=str(work/"reaper-button.png"))
   x,y,w,h=button.bounding_box().values()
   scale=page.evaluate("devicePixelRatio");start=gui.ClientToScreen(deskview,(round((x+w/2)*scale),round((y+h/2)*scale)))
   arrange=child(rh,"REAPERTrackListWindow");left,top,right,bottom=gui.GetWindowRect(arrange)
   targetx=right-95;points=work/"points"
   points.write_text("".join(f"{targetx} {yy}\n" for yy in range(top+4,bottom-4,4)))
   until(lambda:(work/"expected").exists())
   x,y,t=(work/"expected").read_text().split();end=(int(x),int(y));pos=float(t)
   assert gui.GetAncestor(gui.WindowFromPoint(end),C.GA_ROOT)==rh
   drag(start,end,True)
   assert not (work/"done").exists() and not (profile/"reaper-action.txt").exists()
   drag(start,(start[0]-110,start[1]+35))
   wait(page,"!busy && $('status').textContent.includes('已取消导入')")
   drag(start,end)
   wait(page,"!busy && /已发送到 REAPER|REAPER 未将|请将按钮|未响应/.test($('status').textContent)",timeout=23000)
   assert "已发送到 REAPER" in page.locator("#status").inner_text(),page.locator("#status").inner_text()
   until(lambda:(work/"done").exists())
   result=(work/"done").read_text().splitlines()
   count,at,cursor=result[0].split();assert int(count)==1 and abs(float(at)-pos)<.00001 and float(cursor)==17.25
   swatches=["ffd886","8ddcff","8de3af","ff9d97","cfb1ff","ffaed8"]
   for row,n in zip(result[1:],sorted(review["notes"],key=lambda n:n["startUs"]),strict=True):
    a,b,color=row.split();assert abs(float(a)-pos-n["startUs"]/1e6)<.00001
    assert abs(float(b)-(pos+n["endUs"]/1e6 if n["endUs"] is not None else -1))<.00001
    assert color==(n.get("color") or swatches[(n["number"]-1)%6]).strip("#").lower()
   print("PASS: physical cross-window drag to track 2 at",pos,"s; cursor unchanged, marker colors and offsets, Escape cancels.",work)
  finally:stop(desk)
  # Without an open document, clicking checks the installation but cannot change a project.
  desk,page=launch(pw,None,"typeof send==='function'")
  try:
   folder=work/"Scripts";folder.mkdir()
   page.locator("#sendReaper").click()
   dlg=Desktop(backend="win32").window(process=desk.pid,class_name="#32770")
   dlg.wait("ready",timeout=8);edit=dlg.child_window(class_name="Edit",found_index=0)
   edit.set_edit_text(str(folder));dlg.child_window(control_id=1).click_input()
   wait(page,"!busy && $('status').textContent.includes('脚本已安装')")
   installed=folder/"FrameNote - Import review.lua"
   expected=(app.parent/"reaper"/installed.name).read_bytes()
   assert installed.read_bytes()==expected
   for missing in (False,True):
    if missing:installed.unlink()
    else:installed.write_text("-- FrameNote receiver v1\n-- old copy")
    sidecar=Path(str(installed)+".app");sidecar.write_text("old app path")
    page.locator("#sendReaper").click()
    until(lambda:installed.is_file() and installed.read_bytes()==expected and Path(sidecar.read_text(encoding="utf8"))==app)
    wait(page,"!busy && $('status').textContent.includes('请先打开视频')")
   stamp=installed.stat().st_mtime_ns
   page.locator("#sendReaper").click()
   wait(page,"!busy && $('status').textContent.includes('请先打开视频')")
   assert installed.stat().st_mtime_ns==stamp
   print("PASS: first click installs; stale/missing receiver repaired in place; current file unchanged.")
  finally:stop(desk)
finally:proc.terminate();proc.wait(timeout=10)
