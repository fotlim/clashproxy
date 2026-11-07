; ClashProxy 安装包脚本（Inno Setup 6）
; 编译：ISCC.exe /DAppVersion=1.0.0 installer\ClashProxy.iss
; 或直接跑 tools\build-release.ps1

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "ClashProxy"
#define AppPublisher "fotlim"
#define AppUrl "https://github.com/fotlim/clashproxy"

[Setup]
AppId={{9C13A2E4-6F2B-4A31-9B7E-2E4C6D51A8B3}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/fotlim
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
InfoBeforeFile=
OutputDir=..\release
OutputBaseFilename={#AppName}-{#AppVersion}-setup-x64
SetupIconFile=..\src\ClashProxy\Assets\app.ico
UninstallDisplayIcon={app}\{#AppName}.exe
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
AllowNoIcons=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (C) 2026 {#AppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\{#AppName}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppName}.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppName}.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppName}.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 只删程序目录里由安装产生的残留，用户数据在 %LOCALAPPDATA%，卸载时会单独询问
Type: filesandordirs; Name: "{app}\logs"

[Code]
const
  // 版本号是键下的「值名」，不是子键名
  DotNetDesktopKey64 = 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  DotNetDesktopKey32 = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

function HasDotNet8Desktop(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  Found: Boolean;
begin
  Result := False;
  Found := RegGetValueNames(HKLM, DotNetDesktopKey64, Names);
  if not Found then
    Found := RegGetValueNames(HKLM32, DotNetDesktopKey32, Names);
  if Found then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Pos('8.', Names[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  Answer: Integer;
begin
  Result := True;
  if not HasDotNet8Desktop() then
  begin
    Answer := MsgBox('ClashProxy needs the .NET 8 Desktop Runtime, which was not detected on this machine.' + #13#10#13#10 +
      'You can still install ClashProxy now, but it will not start until that runtime is installed.' + #13#10 +
      'Download: https://dotnet.microsoft.com/download/dotnet/8.0' + #13#10#13#10 +
      'Also required: Windows App Runtime 1.8 (https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)' + #13#10#13#10 +
      'Continue with the installation?', mbConfirmation, MB_YESNO);
    Result := Answer = IDYES;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\ClashProxy');
    if DirExists(DataDir) then
    begin
      if MsgBox('Also delete your settings, subscription copy and logs?' + #13#10 + DataDir, mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;