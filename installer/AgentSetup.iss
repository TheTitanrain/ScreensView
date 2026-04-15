#define AppName "ScreensView Agent"
#define AppVersion "1.0.0"
#define ServiceName "ScreensViewAgent"
#define SourceDir "..\ScreensView.Viewer\bin\Release\net8.0-windows"

[Setup]
AppId={{1B2C3D4E-5F6A-7B8C-9D0E-1F2A3B4C5D6E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ScreensView
DefaultDirName={win}\ScreensViewAgent
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputBaseFilename=ScreensView.Agent-Setup
OutputDir=Output
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
; Modern agent (Win10+, .NET 8 + ASP.NET Core 8)
Source: "{#SourceDir}\AgentPayloads\Modern\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"; Check: IsWin10OrLater
; Legacy agent (Win7/8, .NET Framework 4.8)
Source: "{#SourceDir}\AgentPayloads\Legacy\*"; DestDir: "{app}"; Flags: ignoreversion; Excludes: "*.pdb"; Check: not IsWin10OrLater
; .NET 8 runtime prerequisites — copied to {tmp}, deleted after install (Win10+ only)
Source: "{#SourceDir}\Prerequisites\dotnet-runtime-8.*-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: IsWin10OrLater
Source: "{#SourceDir}\Prerequisites\aspnetcore-runtime-8.*-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: IsWin10OrLater

[Code]
var
  ApiKey: String;
  KeyPage: TWizardPage;
  KeyEdit: TEdit;

// ── OS detection ──────────────────────────────────────────────────────────────

function IsWin10OrLater: Boolean;
begin
  // GetWindowsVersion: bits 31-24 = major, 23-16 = minor
  Result := GetWindowsVersion >= $0A000000;
end;

// ── API key generation ────────────────────────────────────────────────────────

function GenerateApiKey: String;
var
  i: Integer;
  chars: String;
begin
  chars := '0123456789abcdef';
  Result := '';
  for i := 1 to 32 do
    Result := Result + chars[Random(16) + 1];
end;

// ── .NET 8 detection and install ─────────────────────────────────────────────

function IsDotNet8RuntimeInstalled: Boolean;
var
  SubkeyNames: TArrayOfString;
  i: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App',
    SubkeyNames) then
    for i := 0 to GetArrayLength(SubkeyNames) - 1 do
      if Pos('8.', SubkeyNames[i]) = 1 then
      begin
        Result := True;
        Break;
      end;
end;

function IsDotNet8AspNetCoreInstalled: Boolean;
var
  SubkeyNames: TArrayOfString;
  i: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.AspNetCore.App',
    SubkeyNames) then
    for i := 0 to GetArrayLength(SubkeyNames) - 1 do
      if Pos('8.', SubkeyNames[i]) = 1 then
      begin
        Result := True;
        Break;
      end;
end;

function FindFileInTmp(const Pattern: String): String;
var
  FindRec: TFindRec;
begin
  Result := '';
  if FindFirst(ExpandConstant('{tmp}\') + Pattern, FindRec) then
  begin
    Result := ExpandConstant('{tmp}\') + FindRec.Name;
    FindClose(FindRec);
  end;
end;

procedure InstallDotNetIfNeeded;
var
  RC: Integer;
  InstallerPath: String;
begin
  if not IsDotNet8RuntimeInstalled then
  begin
    InstallerPath := FindFileInTmp('dotnet-runtime-8.*-win-x64.exe');
    if InstallerPath <> '' then
      Exec(InstallerPath, '/quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, RC);
  end;
  if not IsDotNet8AspNetCoreInstalled then
  begin
    InstallerPath := FindFileInTmp('aspnetcore-runtime-8.*-win-x64.exe');
    if InstallerPath <> '' then
      Exec(InstallerPath, '/quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, RC);
  end;
end;

// ── Service helpers ───────────────────────────────────────────────────────────

function ServiceExists: Boolean;
var
  ResultCode: Integer;
begin
  // sc qc returns 0 if service config found, 1060 if not found
  Exec(ExpandConstant('{sys}\sc.exe'), 'qc {#ServiceName}',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

procedure StopService;
var ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(2500);
end;

procedure CreateService;
var
  ExeName: String;
  RC: Integer;
begin
  if IsWin10OrLater then
    ExeName := 'ScreensView.Agent.exe'
  else
    ExeName := 'ScreensView.Agent.Legacy.exe';

  Exec(ExpandConstant('{sys}\sc.exe'),
    'create {#ServiceName} binPath= "' + ExpandConstant('{app}\') + ExeName + '"' +
    ' start= auto obj= LocalSystem DisplayName= "ScreensView Agent"',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
end;

procedure StartService;
var
  RC: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#ServiceName}',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
  if RC <> 0 then
    MsgBox(
      'Сервис установлен, но не запустился (код ' + IntToStr(RC) + ').' + #13#10 +
      'Проверьте журнал событий Windows для диагностики.',
      mbError, MB_OK);
end;

// ── Wizard pages ──────────────────────────────────────────────────────────────

procedure CopyKeyClick(Sender: TObject);
var
  TempFile: String;
  Lines: TArrayOfString;
  RC: Integer;
begin
  TempFile := ExpandConstant('{tmp}\screensview_key.txt');
  SetArrayLength(Lines, 1);
  Lines[0] := ApiKey;
  SaveStringsToFile(TempFile, Lines, False);
  Exec(ExpandConstant('{sys}\cmd.exe'),
    '/c clip < "' + TempFile + '"',
    '', SW_HIDE, ewWaitUntilTerminated, RC);
  DeleteFile(TempFile);
end;

procedure InitializeWizard;
var
  Lbl: TLabel;
  Btn: TButton;
begin
  // Custom page shown after install — displays generated API key
  KeyPage := CreateCustomPage(wpInstalling,
    'API Ключ',
    'Скопируйте ключ и введите его в Viewer при добавлении компьютера.');

  Lbl := TLabel.Create(KeyPage);
  Lbl.Parent := KeyPage.Surface;
  Lbl.Caption := 'API ключ:';
  Lbl.Left := 0;
  Lbl.Top := 0;

  KeyEdit := TEdit.Create(KeyPage);
  KeyEdit.Parent := KeyPage.Surface;
  KeyEdit.ReadOnly := True;
  KeyEdit.Left := 0;
  KeyEdit.Top := 20;
  KeyEdit.Width := KeyPage.SurfaceWidth - 100;

  Btn := TButton.Create(KeyPage);
  Btn.Parent := KeyPage.Surface;
  Btn.Caption := 'Копировать';
  Btn.Left := KeyEdit.Left + KeyEdit.Width + 8;
  Btn.Top := KeyEdit.Top;
  Btn.Width := 88;
  Btn.OnClick := @CopyKeyClick;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  // Skip key page on upgrade (key was preserved, no need to show it)
  if PageID = KeyPage.ID then
    Result := (ApiKey = '');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = KeyPage.ID then
    KeyEdit.Text := ApiKey;
end;

// ── Installation steps ────────────────────────────────────────────────────────

procedure CurStepChanged(CurStep: TSetupStep);
var
  SettingsFile: String;
  Lines: TArrayOfString;
begin
  case CurStep of

    ssInstall:
      begin
        // Stop service before files are overwritten (prevents file-in-use errors)
        if ServiceExists then
          StopService;
      end;

    ssPostInstall:
      begin
        SettingsFile := ExpandConstant('{app}\appsettings.json');

        if IsWin10OrLater then
          InstallDotNetIfNeeded;

        if not FileExists(SettingsFile) then
        begin
          // Fresh install: generate key, write config, create and start service
          ApiKey := GenerateApiKey();
          SetArrayLength(Lines, 1);
          Lines[0] := '{"Agent":{"ApiKey":"' + ApiKey + '"}}';
          SaveStringsToFile(SettingsFile, Lines, False);
          CreateService;
          StartService;
        end else begin
          // Upgrade: existing key preserved, just restart service
          StartService;
        end;
      end;

  end;
end;

// ── Uninstall ─────────────────────────────────────────────────────────────────

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var RC: Integer;
begin
  case CurUninstallStep of
    usUninstall:
      begin
        Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}',
          '', SW_HIDE, ewWaitUntilTerminated, RC);
        Sleep(2500);
        Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}',
          '', SW_HIDE, ewWaitUntilTerminated, RC);
      end;
  end;
end;
