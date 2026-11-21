param([string]$Compiler,[string]$CrtDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$art=Join-Path $repo 'artifacts'
$project=Join-Path $repo 'src/FrameNote/FrameNote.csproj'
$version=([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version
if(!$Compiler){$Compiler=@("$env:LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe","${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe") | Where-Object {Test-Path $_} | Select-Object -First 1}
if(!$Compiler){throw 'Install Inno Setup 6 or provide -Compiler.'}
if(!$CrtDirectory){
    $vswhere="${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    $vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $CrtDirectory=(Get-ChildItem "$vs/VC/Redist/MSVC/*/x64/Microsoft.VC143.CRT" -Directory | Sort-Object { [version]$_.Parent.Parent.Name } -Descending | Select-Object -First 1).FullName
}
if(!(Test-Path "$CrtDirectory/vcruntime140.dll")){throw 'Missing redistributable Visual C++ x64 runtime.'}
# A fresh staging directory prevents stale plug-ins or files from earlier builds entering Setup.
$stage=Join-Path $art ('setup-'+[guid]::NewGuid().ToString('N'))
dotnet publish $project -c Release -r win-x64 --self-contained true -o $stage -p:DebugType=None -p:DebugSymbols=false
if($LASTEXITCODE){throw 'Publish failed.'}
$deps=New-Item (Join-Path $art 'installer-deps') -ItemType Directory -Force
& (Join-Path $PSScriptRoot 'stage-ffmpeg.ps1') -Cache $deps.FullName -Destination (Join-Path $stage 'tools')
Copy-Item "$CrtDirectory/*.dll" "$stage/"
$cacheJob=Start-Process -FilePath (Join-Path $stage 'FrameNote.exe') -ArgumentList '--prepare-vlc-cache' -WindowStyle Hidden -Wait -PassThru
if($cacheJob.ExitCode -ne 0){throw 'Failed to prepare VLC plugin cache.'}
$bootstrap=Join-Path $deps 'MicrosoftEdgeWebview2Setup.exe'
if(!(Test-Path $bootstrap)){Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrap}
$sig=Get-AuthenticodeSignature $bootstrap
if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'CN=Microsoft Corporation,'){throw 'Invalid Microsoft WebView2 bootstrapper signature.'}
$buildOut=New-Item -ItemType Directory (Join-Path ([IO.Path]::GetTempPath()) ("FrameNote-setup-"+[guid]::NewGuid().ToString('N')))
& $Compiler /Q "/O$buildOut" "/DAppVer=$version" "/DStage=$stage" (Join-Path $repo 'installer/FrameNote.iss')
if($LASTEXITCODE){throw 'Setup compile failed.'}
$setup=Join-Path $art "release/FrameNote-$version-win-x64-Setup.exe"
New-Item (Split-Path $setup) -Force -ItemType Directory | Out-Null
Copy-Item -LiteralPath (Join-Path $buildOut (Split-Path $setup -Leaf)) -Destination $setup -Force
$hash=(Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content "$setup.sha256" "$hash  $(Split-Path $setup -Leaf)" -Encoding ascii
Get-Item $setup | Select-Object FullName,Length
