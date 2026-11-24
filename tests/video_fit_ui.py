"""Check the four corners of a decoded frame, annotation bounds and controls after native window resizes."""
import io, os, subprocess
from PIL import Image
from playwright.sync_api import sync_playwright
from pywinauto import Desktop
from review_ui import launch, stop, work

profile=work/"profile";profile.mkdir();(profile/"updates.json").write_text('{"automatic":false}')
os.environ["FRAMENOTE_DATA_DIR"]=str(profile)
with sync_playwright() as pw:
    for width,height in [(1052,896),(720,1280),(1920,1080)]:
        video=work/f"{width}x{height}.mp4"
        filters=",".join(f"drawbox=x={x}:y={y}:w=iw/6:h=ih/6:color={c}:t=fill" for x,y,c in
                         [("0","0","red"),("iw*5/6","0","lime"),("0","ih*5/6","blue"),("iw*5/6","ih*5/6","white")])
        subprocess.run(["ffmpeg","-loglevel","error","-f","lavfi","-i",f"color=black:s={width}x{height}:r=5",
                        "-t","1","-vf",filters,"-c:v","libx264","-preset","ultrafast",str(video)],check=True)
        proc,page=launch(pw,video)
        try:
            win=Desktop(backend="win32").window(process=proc.pid,class_name_re="HwndWrapper.*")
            for ww,wh in [(1980,900),(900,600),(1330,820)]:
                win.move_window(10,10,ww,wh)
                page.evaluate("()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))")
                m=page.evaluate("""()=>{let r=s=>{let t=document.querySelector(s).getBoundingClientRect();return [t.x,t.y,t.width,t.height,t.bottom]};
                    return {viewport:[innerWidth,innerHeight],scroll:document.scrollingElement.scrollHeight,s:r('#stage'),o:r('#overlay'),
                    transport:r('#transport'),timeline:r('#scroll'),workspace:r('main'),footer:!!document.querySelector('footer')}}""")
                w,h=m["viewport"];x,y,sw,sh,sb=m["s"];ox,oy,ow,oh,_=m["o"]
                assert not m["footer"] and m["scroll"]==h and sb<=m["transport"][1]+1 and m["timeline"][4]<=h+1 and abs(m["workspace"][4]-h)<=1,m
                scale=min(sw/width,sh/height);fw,fh=width*scale,height*scale
                assert abs(ow-fw)<1 and abs(oh-fh)<1 and abs(ox-x-(sw-fw)/2)<1 and abs(oy-y-(sh-fh)/2)<1,m
                corners=[(.08,.08,(255,0,0)),(.92,.08,(0,255,0)),(.08,.92,(0,0,255)),(.92,.92,(255,255,255))]
                # A native resize can reach the video compositor after the DOM resize.
                for attempt in range(8):
                    image=Image.open(io.BytesIO(page.screenshot(scale="css"))).convert("RGB")
                    pixels=[image.getpixel((round(ox+fw*fx),round(oy+fh*fy))) for fx,fy,_ in corners]
                    if all(all(v>160 if t else v<80 for v,t in zip(p,c[2])) for p,c in zip(pixels,corners)):break
                else:raise AssertionError((m,pixels))
                page.mouse.move(x+sw/2,y+sh/2);page.mouse.wheel(0,600)
                assert page.evaluate("window.scrollY") == 0
                print(f"{width}x{height}, viewport {w}x{h}: full frame and controls visible",flush=True)
        finally:stop(proc)
