; ============================================================================
;  Apple Esports ERP — branch installer
;
;  One file, two kinds of machine:
;
;    Operator counter PC  — database, API and dashboard, all local. The branch
;                           trades with no internet at all.
;    Customer gaming PC   — the agent that locks and unlocks the screen.
;
;  Build:  pwsh installer\build-branch-installer.ps1
;  (stages everything first, then compiles this)
; ============================================================================

#define AppName        "Apple Esports"
#define AppVersion     "2.1.0"
#define AppPublisher   "Apple Esports"
#define Staging        "branch\staging"

[Setup]
; Same AppId as the client-only build on purpose: a branch that already has the
; thin client installed upgrades in place rather than ending up with two entries
; in Programs and Features and two icons that do different things.
AppId={{7C4F1E62-9A3B-4D58-8E11-2F6A0B93C4D7}

AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; Registers Windows services and writes to Program Files.
PrivilegesRequired=admin

OutputDir=..\dist
OutputBaseFilename=AppleEsports-Branch-Setup-{#AppVersion}
SetupIconFile=..\desktop-client\appicon.ico
UninstallDisplayIcon={app}\AppleEsports.exe
UninstallDisplayName={#AppName} {#AppVersion}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "operator"; Description: "Operator counter PC  —  runs the branch (database + dashboard)"
Name: "gaming";   Description: "Customer gaming PC  —  locked screen only"

[Components]
Name: "core";   Description: "Apple Esports dashboard";     Types: operator gaming; Flags: fixed
Name: "server"; Description: "Branch database and services"; Types: operator
Name: "agent";  Description: "Gaming PC screen lock";        Types: gaming

[Files]
; ── Always ──
Source: "..\desktop-client\publish\AppleEsports.exe"; DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "..\SHORTCUT_KEYS.md";                        DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "..\desktop-client\AppleEsports.config.json"; DestDir: "{app}"; Components: core; Flags: onlyifdoesntexist

; ── Operator counter PC: the whole branch ──
Source: "{#Staging}\api\*";   DestDir: "{app}\api";   Components: server; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Staging}\pgsql\*"; DestDir: "{app}\pgsql"; Components: server; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "branch\setup-database.ps1"; DestDir: "{app}"; Components: server; Flags: ignoreversion
Source: "branch\setup-api.ps1";      DestDir: "{app}"; Components: server; Flags: ignoreversion

; ── Customer gaming PC ──
Source: "..\AppleEsportsErp\src\AppleEsportsErp.ClientAgent\publish\AppleEsportsAgent.exe"; DestDir: "{app}"; Components: agent; Flags: ignoreversion

[Dirs]
; Created up front so the setup scripts are never the first thing to touch them.
Name: "{app}\data";   Components: server
Name: "{app}\logs";   Components: server
Name: "{app}\backups"; Components: server

[Icons]
Name: "{group}\{#AppName}";              Filename: "{app}\AppleEsports.exe"
Name: "{group}\Keyboard shortcuts";      Filename: "{app}\SHORTCUT_KEYS.md"
Name: "{group}\Uninstall {#AppName}";    Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";        Filename: "{app}\AppleEsports.exe"

[Run]
; Database first — setup-api.ps1 depends on the connection string it writes.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\setup-database.ps1"" -InstallDir ""{app}"""; \
  StatusMsg: "Setting up the branch database (this takes a minute)…"; \
  Components: server; Flags: runhidden waituntilterminated

Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\setup-api.ps1"" -InstallDir ""{app}"""; \
  StatusMsg: "Starting the branch system…"; \
  Components: server; Flags: runhidden waituntilterminated

Filename: "{app}\AppleEsports.exe"; Description: "Set up this PC now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Services must go before the files they point at, or Windows is left with entries
; referencing an executable that no longer exists and the names cannot be reused.
Filename: "sc.exe"; Parameters: "stop AppleEsportsApi";   Flags: runhidden; RunOnceId: "StopApi"
Filename: "sc.exe"; Parameters: "delete AppleEsportsApi"; Flags: runhidden; RunOnceId: "DelApi"
Filename: "sc.exe"; Parameters: "stop AppleEsportsDb";    Flags: runhidden; RunOnceId: "StopDb"
Filename: "sc.exe"; Parameters: "delete AppleEsportsDb";  Flags: runhidden; RunOnceId: "DelDb"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\AppleEsports"
Type: filesandordirs; Name: "{userappdata}\AppleEsports"
; NOTE: {app}\data is deliberately NOT listed. That folder is the branch's takings,
; sessions and members. Uninstalling the software must never destroy the business
; records — if the machine is genuinely being retired, someone deletes it knowingly.

[Code]
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

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and IsComponentSelected('server') then
  begin
    MsgBox(
      'This PC now runs the branch itself.' + #13#10#13#10 +
      'The database and dashboard start automatically with Windows, so the shop works ' +
      'even with no internet. The internet is only used to report to Head Office and to ' +
      'receive updates.' + #13#10#13#10 +
      'Point the gaming PCs at this machine when you set them up.',
      mbInformation, MB_OK);
  end;
end;
