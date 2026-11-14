param([string]$OutputDirectory='artifacts/app')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $projectRoot 'src/FrameNote') -c Release -o $OutputDirectory
if ($LASTEXITCODE) { throw 'FrameNote build failed.' }
foreach ($tool in 'ffprobe','ffmpeg') {
    if (!(Get-Command $tool -ErrorAction SilentlyContinue) -and !(Test-Path (Join-Path $OutputDirectory "tools/$tool.exe"))) {
        Write-Warning "$tool is missing. Install FFmpeg or place the executable in tools/ beside FrameNote.exe."
    }
}

