#define AppName "ScreensView Viewer"
#define AppVersion "1.0.0"
#define AppExeName "ScreensView.Viewer.exe"
#define SourceDir "..\ScreensView.Viewer\bin\Release\net8.0-windows"

[Setup]
AppId={{FA7B8C9D-0E1F-2A3B-4C5D-6E7F8A9B0C1D}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ScreensView
DefaultDirName={autopf}\ScreensView\Viewer
DefaultGroupName=ScreensView
DisableProgramGroupPage=yes
OutputBaseFilename=ScreensView.Viewer-Setup-{#AppVersion}
OutputDir=Output
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main Viewer files — exclude agent payloads, prerequisites, debug symbols
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,AgentPayloads\*,Prerequisites\*"
; Agent payloads for remote install
Source: "{#SourceDir}\AgentPayloads\*"; DestDir: "{app}\AgentPayloads"; Flags: ignoreversion recursesubdirs createallsubdirs
; Prerequisites (.NET runtime installers) for remote agent install
Source: "{#SourceDir}\Prerequisites\*"; DestDir: "{app}\Prerequisites"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.md"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet8DesktopInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not IsDotNet8DesktopInstalled then
  begin
    if MsgBox(
      '.NET 8 Desktop Runtime не установлен.' + #13#10 +
      'Viewer не запустится без него.' + #13#10 + #13#10 +
      'Скачайте windowsdesktop-runtime-8.x-win-x64.exe с' + #13#10 +
      'https://dotnet.microsoft.com/download/dotnet/8.0' + #13#10 + #13#10 +
      'Продолжить установку?',
      mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
