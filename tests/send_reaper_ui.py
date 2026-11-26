"""Exercise the actual send button with one isolated portable REAPER instance."""
import json, os, subprocess, sys, time, zipfile
from pathlib import Path
import win32gui, win32process
from pywinauto import Desktop
from playwright.sync_api import sync_playwright
from review_ui import fixtures, work, launch, stop, wait

base=Path(sys.argv[3]).resolve()
assert base.is_relative_to(Path("artifacts").resolve())
assert not any(w.class_name()=="REAPERwnd" for w in Desktop(backend="win32").windows()),"Close other REAPER instances before running"
os.environ["FRAMENOTE_DATA_DIR"]=str(work/"desk")
folder=work/"Scripts";folder.mkdir()
project=work/"target.rpp"
project.write_text('<REAPER_PROJECT 0.1 "6.82" 0\n>\n')
setup=work/"setup.lua"
setup.write_text('''local r=reaper
r.InsertTrackAtIndex(0,true);r.InsertTrackAtIndex(1,true);local t=r.GetTrack(0,1)
r.SetOnlyTrackSelected(t);r.SetEditCurPos(17.25,false,false)
local dir=os.getenv("FRAMENOTE_SEND_TEST")
local f=assert(io.open(dir.."/ready","wb"));f:close()
local function monitor()
 if r.CountMediaItems(0)==5 then
  local ok,err=pcall(function()
   assert(r.CountTracks(0)==2 and r.CountTrackMediaItems(t)==5)
   local total,m,g=r.CountProjectMarkers(0);assert(m==1 and g==4)
   for i=0,2 do local item=r.GetMediaItem(0,i); assert(r.GetMediaItemInfo_Value(item,"D_POSITION")==17.25)
    local src=r.GetMediaItemTake_Source(r.GetActiveTake(item))
    local f=assert(io.open(dir.."/media"..i,"wb"));f:write(r.GetMediaSourceFileName(src,""));f:close()
   end
   local f=assert(io.open(dir.."/markers","wb"))
   for i=0,total-1 do local _,rg,a,b,n,_,color=r.EnumProjectMarkers3(0,i)
    if color~=0 then f:write(string.format("%.6f|%.6f|%s\\n",a,rg and b or -1,n)) end end
   f:close()
   r.Undo_DoUndo2(0);local _,mm,gg=r.CountProjectMarkers(0);assert(mm==0 and gg==2 and r.CountTrackMediaItems(t)==2)
  end)
  local out=assert(io.open(dir.."/done","wb"));out:write(ok and "OK" or tostring(err));out:close()
 else r.defer(monitor) end
end
r.defer(monitor)
''',encoding="utf-8")
proc=subprocess.Popen([str(base/"reaper.exe"),"-nosplash",str(project),str(setup)],env=dict(os.environ,FRAMENOTE_SEND_TEST=str(work)))
def tick():
 def dismiss(h,_):
  if win32process.GetWindowThreadProcessId(h)[1]!=proc.pid or win32gui.GetClassName(h)!="#32770":return
  if win32gui.GetWindowText(h)=="Scanning VST plugins...":win32gui.PostMessage(h,273,2,0)
  labels=[];win32gui.EnumChildWindows(h,lambda w,_:labels.append(win32gui.GetWindowText(w)),None)
  if any("not yet selected an audio device" in t for t in labels):win32gui.PostMessage(h,273,7,0)
 win32gui.EnumWindows(dismiss,None)
def sent(page):
 page.locator("#sendReaper").click()
 wait(page,"!busy && /视频已插入|已发送到 REAPER|当前工程已包含|请先打开目标/.test(document.querySelector('#status').textContent)",timeout=24000)
 print(page.locator("#status").inner_text(),flush=True)
card=subprocess.Popen([sys.executable,"-c",'import tkinter as t;w=t.Tk();w.title("FrameNote Send Test Card");w.geometry("480x270");t.Label(w,text="Send test",font=("Segoe UI",32)).pack();w.mainloop()'])
try:
 for _ in range(500):
  tick()
  if (work/"ready").exists():break
  time.sleep(.1)
 assert (work/"ready").exists()
 package=Path((fixtures/"latest.txt").read_text(encoding="utf-8").strip())
 with sync_playwright() as pw:
  desk,page=launch(pw,fixtures/"vfr.mp4")
  try:
   page.screenshot(path=str(work/"sidebar.png"))
   page.locator("#sendReaper").click()
   dlg=Desktop(backend="win32").window(process=desk.pid,class_name="#32770");dlg.wait("ready",timeout=8)
   edit=dlg.child_window(class_name="Edit",found_index=0);edit.set_edit_text(str(folder));dlg.child_window(control_id=1).click_input()
   wait(page,"document.querySelector('#status').textContent.includes('脚本已安装')")
   sent(page)
   # A temporary recording must survive closing FrameNote after the send.
   page.locator("#mode").select_option("window");page.locator("#refreshSources").click()
   page.locator("#source").select_option(label="FrameNote Send Test Card");page.locator("#output").select_option("")
   page.locator("#record").click();wait(page,"recording==='video'");time.sleep(2.4);page.locator("#record").click()
   wait(page,"temporary && ready",timeout=15000);sent(page)
  finally: stop(desk)
  desk,page=launch(pw,package)
  try:sent(page)
  finally:stop(desk)
 for _ in range(40):
  if (work/"done").exists():break
  time.sleep(.1)
 assert (work/"done").read_text()=="OK"
 paths=[Path((work/("media"+str(i))).read_text('utf-8')) for i in range(3)]
 assert fixtures/"vfr.mp4" in paths and all(p.exists() for p in paths)
 assert len([p for p in paths if p.parent==work/"FrameNote Media"])==2
 with zipfile.ZipFile(package) as z: notes=sorted(json.loads(z.read("review.json"))["notes"],key=lambda n:n["startUs"])
 for i,(line,n) in enumerate(zip((work/"markers").read_text("utf-8").splitlines(),notes,strict=True),1):
  a,b,text=line.split("|",2);assert text==f"#{n.get('number',i)} {n['text']}" and abs(float(a)-17.25-n["startUs"]/1e6)<2e-6
  assert n["endUs"] is None and float(b)==-1 or n["endUs"] is not None and abs(float(b)-17.25-n["endUs"]/1e6)<2e-6
 print("PASS: installation, three button sends at 17.25 s on track 2, markers, regions, durable media and undo",work)
finally:
 card.terminate();card.wait();proc.terminate();proc.wait(timeout=8)

