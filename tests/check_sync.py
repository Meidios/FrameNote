"""Measure repeated flash/tone alignment from a CaptureCheck recording."""
import array, json, statistics, subprocess, sys
from pathlib import Path
for fn in sys.argv[1:]:
    meta=json.loads(subprocess.check_output(["ffprobe","-v","error","-show_streams","-show_format","-of","json",fn]))
    v,a=meta["streams"][:2]; num,den=map(int,v["avg_frame_rate"].split("/"));fps=num/den
    def decode(*options):
        return subprocess.check_output(["ffmpeg","-v","error","-i",fn,*options,"pipe:1"])
    pixels=decode("-an","-vf","crop=48:48:20:20,scale=1:1,format=gray","-fps_mode","passthrough","-f","rawvideo")
    samples=array.array("h");samples.frombytes(decode("-vn","-ac","1","-ar","8000","-f","s16le"))
    visual=[i/fps for i in range(1,len(pixels)) if pixels[i]>200 and pixels[i-1]<=200]
    energy=[max(abs(x) for x in samples[i:i+40]) for i in range(0,len(samples),40)]
    auditory=[i*.005 for i in range(1,len(energy)) if energy[i]>300 and energy[i-1]<=300]
    print(Path(fn).name,'duration',meta["format"]["duration"],'bytes',meta["format"]["size"],'frame count',v["nb_frames"],'fps',fps,'peak',max(energy),'flashes',len(visual),'tone onsets',len(auditory),flush=True)
    if not auditory or not visual:sys.exit("Missing decoded audio or visual pulses")
    offsets=[(min(auditory,key=lambda x:abs(x-t))-t)*1000 for t in visual if 2<t<float(v["duration"])-2]
    print('Audio minus visual ms: median',round(statistics.median(offsets),1),'first 10 median',round(statistics.median(offsets[:10]),1),'last 10 median',round(statistics.median(offsets[-10:]),1),'95% absolute',round(sorted(abs(t) for t in offsets)[int(.95*len(offsets))],1))

