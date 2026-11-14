"""Deterministic mux regression: known clock skew and a packet gap, plus two endpoints.
No Windows audio settings or recording devices are needed for this check."""
import array, json, statistics, subprocess, sys, tempfile
from pathlib import Path

exe=Path(sys.argv[1]).resolve()
def cmd(*args):subprocess.run([str(x) for x in args],check=True)
def media(*args):cmd('ffmpeg','-v','error','-nostdin',*args)
def edges(file,channel):
    data=array.array('h')
    data.frombytes(subprocess.check_output(['ffmpeg','-v','error','-i',str(file),'-vn','-af',f'pan=mono|c0=c{channel}','-ar','8000','-f','s16le','pipe:1']))
    events=[];last=-8000
    for i,sample in enumerate(data):
        if i-last>4000 and sample>1200:last=i;events.append(i/8000)
    return events
with tempfile.TemporaryDirectory(prefix='framenote-clock-') as tmp:
    d=Path(tmp);video=d/'video.mp4'; media('-f','lavfi','-i','color=s=64x64:r=30:d=35','-c:v','libx264',video)
    # Each endpoint has a distinct initial position (no sound until the first full second).
    # The left device runs 0.3% slow relative to the common timestamps. Right loses 200 ms at t=14.
    for name,pan,pts in [('left','c0=c0|c1=0*c0','PTS*1.003+0.7/TB'),('right','c0=0*c0|c1=c0','PTS+0.9/TB+gte(T\\,14)*0.2/TB')]:
        media('-f','lavfi','-i','aevalsrc=if(lt(mod(t\\,1)\\,0.06)\\,0.45*sin(2*PI*1000*t)\\,0):s=48000:d=33',
              '-af',f'pan=stereo|{pan},asetpts={pts}','-c:a','pcm_s16le',d/(name+'.mka'))
    result=d/'result.mp4'; cmd(exe,'--check-mux',video,result,'0.5',d/'left.mka',d/'right.mka')
    for ch in [0,1]:
        times=edges(result,ch)
        assert len(times)==33,(ch,len(times))
        err=[t-(.2+i*1.003 if ch==0 else .4+i+(.2 if i>=15 else 0)) for i,t in enumerate(times) if i!=14]
        head=statistics.median(err[:5]);tail=statistics.median(err[-5:])
        assert abs(tail-head)<.0167 and max(abs(head),abs(tail))<.0167,(ch,head,tail)
        print(f'Channel {ch}: first/last error {head*1000:.1f}/{tail*1000:.1f} ms')
    silent=d/'silent.mp4';cmd(exe,'--check-mux',video,silent,'0.5')
    assert not edges(silent,0)
    def video_hash(p):
        return subprocess.check_output(['ffmpeg','-v','error','-i',str(p),'-map','0:v','-c','copy','-f','hash','-']).strip()
    assert video_hash(video)==video_hash(result)==video_hash(silent)
    print('Skew, missing packets, independent starting times, silence and video stream copy passed.')

