param([Parameter(Mandatory)][string]$Cache,[Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference='Stop'
$build='ffmpeg-8.0.1-full_build-shared'
$archive=Join-Path $Cache "$build.zip"
$sha256='e4a40b46e3a3f3e5f2ed28352bd1fd6c733ae3808a8743682ef7f0d4e20c7d51'
if(!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash -ne $sha256){
    $download=Join-Path $Cache ("$build-"+[guid]::NewGuid().ToString('N')+'.tmp')
    try{
        $uri='https://api.github.com/repos/GyanD/codexffmpeg/releases/assets/318698641'
        Invoke-WebRequest -Uri $uri -Headers @{Accept='application/octet-stream'} -OutFile $download
        if((Get-FileHash -LiteralPath $download).Hash -ne $sha256){throw 'FFmpeg archive SHA-256 mismatch.'}
        Move-Item -LiteralPath $download -Destination $archive -Force
    }finally{if(Test-Path -LiteralPath $download){Remove-Item -LiteralPath $download}}
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    $entries=@('LICENSE','README.txt','bin/ffmpeg.exe','bin/ffprobe.exe')
    $libraries=$zip.Entries | Where-Object {$_.FullName -like "$build/bin/*.dll"}
    foreach($path in $entries){
        $entry=$zip.GetEntry("$build/$path")
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $Destination $entry.Name))
    }
    foreach($entry in $libraries){
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $Destination $entry.Name))
    }
}finally{$zip.Dispose()}
