[Setup]
AppId={{798D3800-50B8-422F-990F-39EA016163C2}
AppName=FrameNote
AppVersion={#AppVer}
AppPublisher=Meidios
AppPublisherURL=https://github.com/Meidios/FrameNote
AppUpdatesURL=https://github.com/Meidios/FrameNote/releases
VersionInfoVersion={#AppVer}.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\FrameNote
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\FrameNote.exe
SetupIconFile=..\src\FrameNote\Assets\FrameNote.ico
WizardStyle=modern
Compression=lzma2/fast
SolidCompression=yes
OutputBaseFilename=FrameNote-{#AppVer}-win-x64-Setup
OutputDir=..\artifacts\release
ChangesAssociations=yes
AppMutex=Local\FrameNote.Installed
CloseApplications=no
RestartApplications=no
MinVersion=10.0

[Languages]
Name: "zh"; MessagesFile: "ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#Stage}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.lib,*.xml"
Source: "..\artifacts\installer-deps\MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{userprograms}\FrameNote"; Filename: "{app}\FrameNote.exe"

[Registry]
Root: HKCU; Subkey: "Software\Meidios\FrameNote"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "FrameNote"
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "FrameNote 视频审阅与批注"
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: """{app}\FrameNote.exe"",0"
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".framenote"; ValueData: "FrameNote.Review"
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "FrameNote"; ValueData: "Software\Meidios\FrameNote\Capabilities"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\FrameNote.Review"; ValueType: string; ValueData: "FrameNote 审阅包"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\FrameNote.Review\DefaultIcon"; ValueType: string; ValueData: """{app}\Assets\FrameNote_File.ico"",0"
Root: HKCU; Subkey: "Software\Classes\FrameNote.Review\shell\open\command"; ValueType: string; ValueData: """{app}\FrameNote.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\.framenote\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Review"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\.framenote"; ValueType: string; ValueData: "FrameNote.Review"; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\shell\open\command"; ValueType: string; ValueData: """{app}\FrameNote.exe"" ""%1"""; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\DefaultIcon"; ValueType: string; ValueData: """{app}\Assets\FrameNote_File.ico"",0"; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".framenote"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty

Root: HKCU; Subkey: "Software\Classes\FrameNote.Image"; ValueType: string; ValueData: "FrameNote 图片"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\FrameNote.Image\DefaultIcon"; ValueType: string; ValueData: """{app}\FrameNote.exe"",0"
Root: HKCU; Subkey: "Software\Classes\FrameNote.Image\shell\open\command"; ValueType: string; ValueData: """{app}\FrameNote.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".png"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.png\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".png"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".apng"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.apng\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".apng"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jpg"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.jpg\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".jpg"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jpeg"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.jpeg\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".jpeg"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".jfif"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.jfif\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".jfif"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".bmp"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.bmp\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".bmp"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".gif"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.gif\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".gif"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".webp"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.webp\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".webp"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".avif"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.avif\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".avif"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ico"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.ico\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".ico"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tif"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.tif\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".tif"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Meidios\FrameNote\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tiff"; ValueData: "FrameNote.Image"
Root: HKCU; Subkey: "Software\Classes\.tiff\OpenWithProgids"; ValueType: string; ValueName: "FrameNote.Image"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Applications\FrameNote.exe\SupportedTypes"; ValueType: string; ValueName: ".tiff"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty

[Run]
Filename: "{app}\FrameNote.exe"; Description: "启动 FrameNote"; Flags: postinstall nowait skipifsilent

[Code]
const
  StateKey='Software\Meidios\FrameNote';
  ExtKey='Software\Classes\.framenote';
  CommandKey='Software\Classes\Applications\FrameNote.exe\shell\open\command';
  IconKey='Software\Classes\Applications\FrameNote.exe\DefaultIcon';
  LegacyKey='Software\Classes\framenote_auto_file';
  EdgeKey='SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

procedure Remember(Key, Slot: String);
var S: String;
begin
  if RegValueExists(HKCU,StateKey,Slot+'Known') then exit;
  if RegQueryStringValue(HKCU,Key,'',S) then RegWriteStringValue(HKCU,StateKey,Slot,S);
  RegWriteDWordValue(HKCU,StateKey,Slot+'Known',1);
end;

procedure Restore(Key, Slot, Ours: String);
var S, Old: String;
begin
  if RegQueryStringValue(HKCU,Key,'',S) and (CompareText(S,Ours)=0) then
    if RegQueryStringValue(HKCU,StateKey,Slot,Old) then RegWriteStringValue(HKCU,Key,'',Old)
    else RegDeleteValue(HKCU,Key,'');
end;

function HasWebView: Boolean;
var S: String;
begin
  Result := (RegQueryStringValue(HKLM32,EdgeKey,'pv',S) or RegQueryStringValue(HKCU,EdgeKey,'pv',S)) and (S<>'') and (S<>'0.0.0.0');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  if not HasWebView then begin
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'),'/silent /install','',SW_HIDE,ewWaitUntilTerminated,Code) or not HasWebView then
      Result := 'WebView2 未能安装。请连接网络，或先安装 Microsoft Edge WebView2 Evergreen Runtime 后重试。';
  end;
end;

procedure CurStepChanged(Step: TSetupStep);
var Old, Legacy, Exe: String;
begin
  if Step=ssInstall then begin
    Remember(ExtKey,'Extension'); Remember(CommandKey,'Command'); Remember(IconKey,'Icon');
    { Explorer may already have assigned the portable FrameNote binary to this file type. }
    if RegQueryStringValue(HKCU,LegacyKey+'\shell\open\command','',Legacy) and
       (Copy(Legacy,1,1)='"') and (Pos('" "',Legacy)>1) then begin
      Exe:=Copy(Legacy,2,Pos('" "',Legacy)-2);
      if CompareText(ExtractFileName(Exe),'FrameNote.exe')=0 then begin
        Remember(LegacyKey+'\shell\open\command','LegacyCommand');
        Remember(LegacyKey+'\DefaultIcon','LegacyIcon');
        RegWriteStringValue(HKCU,LegacyKey+'\shell\open\command','',ExpandConstant('"{app}\FrameNote.exe" "%1"'));
        RegWriteStringValue(HKCU,LegacyKey+'\DefaultIcon','',ExpandConstant('"{app}\Assets\FrameNote_File.ico",0'));
      end;
    end;
    if GetVersionNumbersString(ExpandConstant('{app}\FrameNote.exe'),Old) then
      RegWriteStringValue(HKCU,StateKey,'PreviousVersion',Old);
  end;
end;

procedure CurUninstallStepChanged(Step: TUninstallStep);
begin
  if Step=usUninstall then begin
    if RegValueExists(HKCU,StateKey,'LegacyCommandKnown') then begin
      Restore(LegacyKey+'\shell\open\command','LegacyCommand',ExpandConstant('"{app}\FrameNote.exe" "%1"'));
      Restore(LegacyKey+'\DefaultIcon','LegacyIcon',ExpandConstant('"{app}\Assets\FrameNote_File.ico",0'));
      RegDeleteKeyIfEmpty(HKCU,LegacyKey+'\DefaultIcon');
    end;
    Restore(ExtKey,'Extension','FrameNote.Review');
    Restore(CommandKey,'Command',ExpandConstant('"{app}\FrameNote.exe" "%1"'));
    Restore(IconKey,'Icon',ExpandConstant('"{app}\Assets\FrameNote_File.ico",0'));
  end;
end;

