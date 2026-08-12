; ============================================================================
;  Apple Esports ERP — Windows installer
;
;  Produces AppleEsports-Setup-<version>.exe: the thing you double-click, which
;  installs the program, registers it in Programs and Features, creates the Start
;  Menu and desktop icons, and provides a working uninstall.
;
;  Build:  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\AppleEsports.iss
;  (build-installer.ps1 publishes the app first, then calls this.)
; ============================================================================

#define AppName        "Apple Esports"
#define AppVersion     "2.0.0"
#define AppPublisher   "Apple Esports"
#define AppExeName     "AppleEsports.exe"
#define SourceDir      "..\desktop-client\publish"

[Setup]
; A stable, unique id. Windows uses it to recognise an existing install, so
; upgrades replace rather than pile up a second entry in Programs and Features.
; It must never change between versions.
AppId={{7C4F1E62-9A3B-4D58-8E11-2F6A0B93C4D7}

AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; Per-machine install: a gaming café PC is shared by staff and customers, so the
; program must exist for whoever logs in, not just the account that installed it.
PrivilegesRequired=admin

OutputDir=..\dist
OutputBaseFilename=AppleEsports-Setup-{#AppVersion}
SetupIconFile=..\desktop-client\appicon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

; Shown on the welcome page — states plainly what happens after install, because
; the setup wizard appearing on first launch surprises people otherwise.
AppComments=Gaming cafe management system

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startupicon"; Description: "Start Apple Esports automatically when Windows starts"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Deployment default, dropped in only when absent so an upgrade never overwrites a
; branch's own server address and PIN. Per-machine settings live in %APPDATA%.
Source: "..\desktop-client\AppleEsports.config.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

Source: "..\SHORTCUT_KEYS.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Keyboard shortcuts"; Filename: "{app}\SHORTCUT_KEYS.md"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Set up this PC now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The install folder itself is removed by Inno. These are written at runtime and
; would otherwise be left behind: the WebView2 browser profile, and the machine's
; own configuration.
Type: filesandordirs; Name: "{localappdata}\AppleEsports"
Type: filesandordirs; Name: "{userappdata}\AppleEsports"

[Code]
{ WebView2 is what draws the dashboard. It ships with Windows 11 and current
  Windows 10, but a stripped or older image can lack it — and without it the
  program installs cleanly and then refuses to open, which looks like a broken
  installer. Better to say so during setup, while someone is still watching. }
function WebView2Installed(): Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if not WebView2Installed() then
  begin
    if MsgBox(
      'Apple Esports needs the Microsoft Edge WebView2 Runtime, which is not installed on this PC.' + #13#10#13#10 +
      'It is a free Microsoft component and takes about a minute to install.' + #13#10#13#10 +
      'Open the download page now? (Choose No to carry on installing anyway.)',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://developer.microsoft.com/microsoft-edge/webview2/#download', '', '', SW_SHOW, ewNoWait, ErrorCode);
      Result := False;
    end;
  end;
end;
