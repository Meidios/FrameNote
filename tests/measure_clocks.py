"""Compare physical loopback pulses vs source UI and decoded presentation timestamps.
No raw-stream sample-number or average-frame-rate assumptions."""
import argparse, array, bisect, csv, json, statistics, subprocess
from pathlib import Path

def run(*args):
    return subprocess.check_output(args)

def probe(file, stream):
    info=json.loads(run('ffprobe','-v','error','-select_streams',stream,'-show_frames',
        '-show_entries','frame=best_effort_timestamp_time,nb_samples:stream=sample_rate,width,height','-show_streams','-of','json',str(file)))
    return info['streams'][0],info['frames']

def measure(directory,fps):
    file=directory/f'capture-{fps}.mp4'
    video,vframes=probe(file,'v:0')
    audio,aframes=probe(file,'a:0')
    gray=run('ffmpeg','-v','error','-i',str(file),'-an','-vf','crop=48:48:20:20,scale=1:1,format=gray','-fps_mode','passthrough','-f','rawvideo','pipe:1')
    assert len(vframes)==len(gray)
    visual=[float(vframes[i]['best_effort_timestamp_time']) for i in range(1,len(gray)) if gray[i]>200>=gray[i-1]]
    # Decode at the native rate; map each pulse to the presentation time of its PCM block.
    raw=array.array('h');raw.frombytes(run('ffmpeg','-v','error','-i',str(file),'-vn','-ac','1','-f','s16le','pipe:1'))
    rate=int(audio['sample_rate']);edges=[];p=0;last=-rate
    for frame in aframes:
        n=int(frame['nb_samples'])
        if p+n-last>rate//2:
            block=raw[p:p+n]
            if max(block,default=0)>1200:
                i=next(i for i,x in enumerate(block) if x>1200)
                if p+i-last>rate//2:
                    last=p+i;edges.append(float(frame['best_effort_timestamp_time'])+i/rate)
        p+=n
    assert abs(p-len(raw))<=1,(p,len(raw))
    def read(stem):
        with (directory/f'{stem}-{fps}.csv').open() as f:return list(csv.DictReader(f))
    source=read('source');endpoint=read('endpoint')
    src=[float(r['qpc_s']) for r in source]; ep=[float(r['qpc_s']) for r in endpoint]
    def match(series,value):
        i=bisect.bisect_left(series,value);best=min(series[max(i-1,0):i+1],key=lambda s:abs(s-value))
        if abs(best-value)>.45:raise ValueError(f'Unmatched pulse near {value}: {best}')
        return best
    # Map the first recorded interior flash to its source-time label via recording's QPC start.
    epoch_file=directory/f'video-epoch-{fps}.txt'
    if epoch_file.exists():
        origin=float(epoch_file.read_text())
    else:
        # Older measurements had no first-frame callback log. Use first full input
        # pulse after startup; this only identifies the cycle, not the measured offset.
        origin=ep[1]-edges[0]
    rows=[]
    for v in visual:
        if v<2 or v>float(vframes[-1]['best_effort_timestamp_time'])-2:continue
        s=match(src,origin+v)
        a=match(edges,v);e=match(ep,s)
        rows.append([v,s,e,a,(e-s)*1000,(a-v)*1000,((a-v)-(e-s))*1000])
    if len(rows)<20:raise ValueError('Too few matched pulses')
    for k in [0,1,2,3]:
        if not all(.5<y[k]-x[k]<1.5 for x,y in zip(rows,rows[1:])):raise ValueError('Skipped or double-counted pulse')
    def summary(k):
        s=[r[k] for r in rows];first=statistics.median(s[:10]);end=statistics.median(s[-10:])
        return dict(first_ms=round(first,2),last_ms=round(end,2),change_ms=round(end-first,2))
    with (directory/f'matched-{fps}.csv').open('w',newline='') as f:
        w=csv.writer(f);w.writerow(['video_pts_s','source_qpc_s','endpoint_qpc_s','audio_pts_s','input_ms','file_ms','residual_ms']);w.writerows(rows)
    return dict(fps=fps,dimensions=[video['width'],video['height']],frames=len(gray),audio_rate=rate,pulses=len(rows),
        reference=summary(4),file=summary(5),capture_residual=summary(6))
if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('dir',type=Path);ap.add_argument('fps',type=int,nargs='+');args=ap.parse_args()
    print(json.dumps([measure(args.dir,f) for f in args.fps],indent=2))

