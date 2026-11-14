"""Run against an isolated portable REAPER directory, never a working session."""
import os, sys, time, subprocess, json, zipfile, tempfile
from pathlib import Path
import win32gui as gui
import win32process
base=Path(sys.argv[1]).resolve(); assert base.is_relative_to(Path("artifacts").resolve())
exe=base/"reaper.exe"; dir=Path(sys.argv[2]).resolve()
test=Path(tempfile.mkdtemp(prefix="reaper-run-",dir=dir))
package=Path((dir/"latest.txt").read_text(encoding="utf8").strip())
(test/"latest.txt").write_text(str(package),encoding="utf8")
with zipfile.ZipFile(package) as z: ns=json.loads(z.read("review.json"))["notes"]
(test/"expected.tsv").write_text("".join(f"{n['startUs']}\t{n['endUs'] if n['endUs'] is not None else -1}\t#{n.get('number',i)} {n['text']}\n" for i,n in enumerate(sorted(ns,key=lambda n:(n['startUs'],n['id'])),1)),encoding="utf8")
rpp=test/"test.rpp";rpp.write_text('<REAPER_PROJECT 0.1 "6.82" 0\n>\n')
env=dict(os.environ,FRAMENOTE_TEST_DIR=str(test),FRAMENOTE_TEST_ACTION=str(Path(sys.argv[3]).resolve()))
proc=subprocess.Popen([str(exe),"-newinst","-nosplash",str(rpp),str(Path(__file__).with_name("reaper_acceptance.lua").resolve())],env=env)
def dismiss(h,_):
    if win32process.GetWindowThreadProcessId(h)[1]!=proc.pid or gui.GetClassName(h)!="#32770":return
    title=gui.GetWindowText(h)
    if title=="Scanning VST plugins...":gui.PostMessage(h,273,2,0)
    if title=="REAPER":
        labels=[];gui.EnumChildWindows(h,lambda w,_:labels.append(gui.GetWindowText(w)),None)
        if any("not yet selected an audio device" in t for t in labels):gui.PostMessage(h,273,7,0)
try:
    until=time.monotonic()+60
    while time.monotonic()<until:
        gui.EnumWindows(dismiss,None)
        if (test/"reaper-result.txt").exists():break
        time.sleep(.2)
    result=(test/"reaper-result.txt").read_text(encoding="utf8");print(result,flush=True)
    assert result.startswith("PASS")
finally:
    try:proc.wait(timeout=3)
    except subprocess.TimeoutExpired:proc.terminate()

