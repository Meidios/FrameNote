param([string]$OutDir = 'artifacts/checks')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
foreach ($label in '2997','60','vfr') {
    $fps = if ($label -eq '2997') {'30000/1001'} else {'60'}
    $vf = if ($label -eq 'vfr') {"select='if(lt(t,1),not(mod(n,4)),not(mod(n,2)))',"} else {''}
    $vf += "drawtext=fontfile='C\:/Windows/Fonts/consola.ttf':text='%{n}':fontcolor=white:fontsize=60:box=1:boxcolor=black:x=8:y=8"
    ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=480x270:rate=$fps" -f lavfi -i 'aevalsrc=if(lt(mod(t\,1)\,0.02)\,0.5*sin(2*PI*1000*t)\,0):s=48000' -t 3 -vf $vf -fps_mode vfr -c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart (Join-Path $OutDir "$label.mp4")
    if ($LASTEXITCODE) {throw "FFmpeg failed for $label"}
}
