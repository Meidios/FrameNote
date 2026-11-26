"""Verify playable Excel media and REAPER delivery with cropped, overlapping annotation audio."""
import hashlib
import json
import os
import subprocess
import uuid
import wave
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

import numpy as np
from PIL import Image
from playwright.sync_api import sync_playwright
from review_ui import app, exported_file, launch, stop, work

profile=work/"profile"
profile.mkdir()
(profile/"updates.json").write_text('{"automatic":false}')
(profile/"volume.json").write_text("0")
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
source=work/"source.mp4"
def ffmpeg(*args):
    subprocess.run(["ffmpeg","-nostdin","-v","error",*map(str,args)],check=True)
ffmpeg("-f","lavfi","-i","color=gray:s=320x180:r=25:d=6","-f","lavfi","-i","sine=frequency=220:duration=6",
       "-vf","drawbox=color=blue:t=fill:enable='gte(t,3)'","-c:v","libx264","-pix_fmt","yuv420p","-c:a","aac",source)
def wav(path,frequencies):
    rate=48000
    with wave.open(str(path),"wb") as audio:
        audio.setparams((1,2,rate,0,"NONE",""))
        for freq in frequencies:
            audio.writeframes((6000*np.sin(2*np.pi*freq*np.arange(rate//2)/rate)).astype("<i2").tobytes())
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def make_voice(name,freqs,original):
    raw=work/(name+".wav");wav(raw,freqs)
    mp3=raw.with_suffix(".mp3");ffmpeg("-i",raw,"-c:a","libmp3lame","-q:a","2",mp3)
    voice=dict(asset=sha(mp3),format="mp3",durationUs=len(freqs)*500000)
    assets["voice/"+sha(mp3)+".mp3"]=mp3
    if original:
        voice["original"]=dict(asset=sha(raw),name=raw.name)
        assets["voice/"+sha(raw)+".original"]=raw
    return voice
assets={}
mic=make_voice("microphone",[440,440,440],False)
music=make_voice("cue",[330,880,880,880],True)
def note(n,start,end=None,voice=None):
    return dict(id=str(uuid.uuid4()),number=n,startUs=start,endUs=end,text=f"Review {n}",voice=voice,color="#ff3020",
                shape=dict(kind="box",x=.1,y=.1,toX=.6,toY=.65))
notes=[note(1,500000,1500000,mic),note(2,750000,1750000,music),note(3,3000000,4000000),note(4,4500000)]
notes[1]["shape"].update(x=.7,toX=.9)
def clip(track,start,offset,length,n=None):
    item=dict(id=str(uuid.uuid4()),track=track,startUs=start,sourceUs=offset,durationUs=length)
    if n:
        v=n["voice"];item.update(asset=v["asset"],format=v["format"],noteId=n["id"],sourceDurationUs=v["durationUs"])
    return item
clips=[clip("video",0,0,6000000),clip("audio",0,0,1000000),
       clip("voice",500000,0,1000000,notes[0]),clip("voice",750000,500000,1000000,notes[1])]
review=dict(version=3,id=str(uuid.uuid4()),fileName=source.name,durationUs=6000000,lastNoteNumber=4,notes=notes,
            timeline=dict(sourceDurationUs=6000000,clips=clips))
package=work/"delivery.framenote"
excel_notes=[*notes,note(5,0,500000),note(6,1500000,1750000),note(7,1750000,2250000)]
with zipfile.ZipFile(package,"w") as z:
    z.writestr("review.json",json.dumps(dict(review,notes=excel_notes,lastNoteNumber=7)));z.write(source,"media/source.mp4")
    for name,file in assets.items():z.write(file,name)

def levels(path,time):
    data=subprocess.check_output(["ffmpeg","-v","error","-ss",str(time),"-i",str(path),"-t","0.12",
                                  "-vn","-ac","1","-ar","48000","-f","f32le","pipe:1"])
    samples=np.frombuffer(data,"<f4")
    def amplitude(freq):
        return abs(np.dot(samples*np.hanning(len(samples)),np.exp(-2j*np.pi*freq*np.arange(len(samples))/48000)))/len(samples)
    return {freq:amplitude(freq) for freq in (220,330,440,880)}
def active(path,time):
    return {f for f,a in levels(path,time).items() if a>.009}

with sync_playwright() as pw:
    proc,page=launch(pw,package)
    try:
        page.locator("#exportExcel").click()
        book=exported_file(proc)
    finally:stop(proc)
with zipfile.ZipFile(book) as z:
    rels=ET.fromstring(z.read("xl/worksheets/_rels/sheet1.xml.rels"))
    links={rel.get("Target") for rel in rels if rel.get("TargetMode")=="External"}
assert links=={"media/Region-1.mp4","media/Region-2.mp4","media/Region-3.gif","media/Mark-4.png",
               "media/Region-5.gif","media/Region-6.mp4","media/Region-7.gif","audio/1.mp3","audio/2.wav"},links
for number in (3,5,7):
    with Image.open(book.parent/f"media/Region-{number}.gif") as animation:
        assert animation.format=="GIF" and animation.n_frames>1
with Image.open(book.parent/"media/Mark-4.png") as excel_still:
    red,green,blue,alpha=excel_still.convert("RGBA").getpixel((310,40))
    assert blue>240 and max(red,green)<10 and alpha==255
assert (book.parent/"audio/2.wav").read_bytes()==(work/"cue.wav").read_bytes()
assert active(book.parent/"media/Region-1.mp4",.05)=={220,440}
assert active(book.parent/"media/Region-1.mp4",.3)=={220,440,880}
assert active(book.parent/"media/Region-2.mp4",.85)=={880}
assert active(book.parent/"media/Region-6.mp4",.05)=={880}
print("PASS Excel format, workbook links, original WAV and timed audio mix.",flush=True)

manifest=work/"manifest"
out=work/"FrameNote Media"
trimmed=work/"trimmed.framenote"
clips[0].update(sourceUs=3000000,durationUs=2000000)
notes[2]["endUs"]=4400000
with zipfile.ZipFile(trimmed,"w") as z:
    z.writestr("review.json",json.dumps(review));z.write(source,"media/source.mp4")
    for name,file in assets.items():z.write(file,name)
subprocess.run([str(app),"--prepare-reaper",str(trimmed),str(out),str(manifest),"--layers"],check=True)
lines=manifest.read_text(encoding="utf-8").splitlines()
video=Path(bytes.fromhex(lines[1]).decode())
assert active(video,.8)=={220} and active(video,1.2)==set()
assert int(lines[2])==2000000
duration=float(subprocess.check_output(["ffprobe","-v","error","-show_entries","format=duration","-of","csv=p=0",str(video)]))
assert abs(duration-2)<.001
pixel=subprocess.check_output(["ffmpeg","-v","error","-ss","1","-i",str(video),"-frames:v","1","-vf","scale=1:1","-pix_fmt","rgb24","-f","rawvideo","pipe:1"])
assert pixel[2]>240 and max(pixel[:2])<10,pixel
images=[line.split("\t") for line in lines if line.startswith("image\t")]
sounds=[line.split("\t") for line in lines if line.startswith("audio\t")]
assert len(images)==4 and len(sounds)==2 and sounds[1][1:4]==["750000","1000000","500000"]
assert [int(row[2]) for row in images]==[1000000,1000000,1400000,0]
for row in images:
    layer=Image.open(bytes.fromhex(row[4]).decode()).convert("RGBA")
    assert layer.size==(320,180) and layer.getpixel((310,40))[3]==0
    alpha=layer.getchannel("A").histogram()
    assert alpha[0]>320*180*.7 and sum(alpha[1:])>500
print("PASS edited source frames and length, transparent layers, Region spans, voice offsets.",work,flush=True)
subprocess.run([str(app),"--prepare-reaper",str(trimmed),str(out),str(manifest),"--items"],check=True)
legacy=manifest.read_text(encoding="utf8").splitlines()
assert legacy[2]=="2000000" and not any(row.startswith("title\t") for row in legacy)
assert all(int(row.split("\t")[2])>0 for row in legacy if row.startswith("image\t"))

# Unlinking can leave original sound beyond the last video clip, or remove the picture entirely.
tail=clip("audio",1500000,3250000,2500000)
for video_clips,expected in [([clips[0]],["2000000","2000000","3750000"]),([],["1500000","2500000","3250000"])]:
    edit=work/("audio-tail.framenote" if video_clips else "audio-only.framenote")
    with zipfile.ZipFile(edit,"w") as z:
        z.writestr("review.json",json.dumps(dict(review,notes=[],timeline=dict(review["timeline"],clips=video_clips+[tail]))))
        z.write(source,"media/source.mp4")
    subprocess.run([str(app),"--prepare-reaper",str(edit),str(out),str(manifest),"--layers"],check=True)
    lines=manifest.read_text(encoding="utf8").splitlines()
    row=next(line.split("\t") for line in lines if line.startswith("audio\t"))
    assert lines[2]==("2000000" if video_clips else "0") and row[1:4]==expected and row[6]==".flac"
    assert active(Path(bytes.fromhex(row[4]).decode()),int(row[3])/1e6)=={220}
print("PASS independent audio tails and audio-only edits.",flush=True)

# A legacy point can carry sound without a Region or an edited timeline.
silent=work/"silent.mp4"
ffmpeg("-i",source,"-map","0:v:0","-c","copy",silent)
point=note(1,500000,voice=mic)
overlap=note(2,750000,1250000)
plain=work/"points.framenote"
with zipfile.ZipFile(plain,"w") as z:
    z.writestr("review.json",json.dumps(dict(version=1,id=str(uuid.uuid4()),fileName=silent.name,
        durationUs=6000000,lastNoteNumber=2,notes=[point,overlap])))
    z.write(silent,"media/source.mp4")
    asset="voice/"+mic["asset"]+".mp3"
    z.write(assets[asset],asset)
with sync_playwright() as pw:
    proc,page=launch(pw,plain)
    try:
        page.locator("#exportExcel").click()
        point_book=exported_file(proc)
    finally:stop(proc)
assert active(point_book.parent/"media/Mark-1.mp4",.2)=={440}
assert active(point_book.parent/"media/Region-2.mp4",.2)=={440}
print("PASS sound attached to a point and a Region overlapping it on a silent source.",flush=True)
