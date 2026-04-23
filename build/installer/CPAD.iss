#define AppName "CPAD"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\installer"
#endif

[Setup]
AppId={{D4B4E4CC-A163-4CF0-BE99-7BDF7CF18E0C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Blackblock-inc
DefaultDirName={localappdata}\Programs\CPAD
DefaultGroupName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=CPAD-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableDirPage=no
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\DesktopHost.exe

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\CPAD"; Filename: "{app}\DesktopHost.exe"
Name: "{group}\CPAD"; Filename: "{app}\DesktopHost.exe"
Name: "{group}\卸载 CPAD"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\assets\webview2\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "正在安装 WebView2 Runtime..."; Flags: waituntilterminated runhidden skipifdoesntexist; Check: not IsWebView2RuntimeInstalled
Filename: "{app}\DesktopHost.exe"; Description: "启动 CPAD"; Flags: nowait postinstall skipifsilent

[Code]
const
  WebView2RuntimeGuid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

var
  CleanAppData: Boolean;

function HasValidWebView2Runtime(const RootKey: Integer; const SubKey: string): Boolean;
var
  VersionText: string;
begin
  Result :=
    RegQueryStringValue(RootKey, SubKey, 'pv', VersionText) and
    (VersionText <> '') and
    (VersionText <> '0.0.0.0');
end;

function IsWebView2RuntimeInstalled: Boolean;
begin
  if IsWin64 then
    Result := HasValidWebView2Runtime(HKLM64, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + WebView2RuntimeGuid)
  else
    Result := HasValidWebView2Runtime(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2RuntimeGuid);

  if not Result then
    Result := HasValidWebView2Runtime(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\' + WebView2RuntimeGuid);
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  CleanAppData := Pos('/CLEANAPPDATA', Uppercase(GetCmdTail)) > 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataPath: string;
begin
  if CurUninstallStep <> usPostUninstall then
    exit;

  AppDataPath := ExpandConstant('{localappdata}\CliProxyApiDesktop');

  if CleanAppData then
  begin
    DelTree(AppDataPath, True, True, True);
    exit;
  end;

  if MsgBox('是否同时删除本机桌面运行数据（日志、桌面配置和后端运行目录）？', mbConfirmation, MB_YESNO) = IDYES then
    DelTree(AppDataPath, True, True, True);
end;
