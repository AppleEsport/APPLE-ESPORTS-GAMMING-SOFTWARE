; Apple Esports ERP - NSIS Installer
; Creates a single .exe that installs either Server or Client mode

!include "MUI2.nsh"
!include "LogicLib.nsh"

; Basic Configuration
Name "Apple Esports ERP v2.0"
OutFile "AppleEsports-v2.0.exe"
InstallDir "$PROGRAMFILES\AppleEsports"
InstallDirRegKey HKCU "Software\AppleEsports" "InstallDir"

; Require admin rights for Server Mode
RequestExecutionLevel admin

; UI Settings
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_LANGUAGE "English"

; Installer Sections
Section "!Server Mode (Operator PC)" SEC_SERVER
  ; This is the default and required for branches

  SectionIn RO  ; Read-only (always installed)

  SetOutPath "$INSTDIR"

  ; Copy Docker Compose and configuration
  File "docker-compose.yml"
  File ".env.example"
  File "setup-server.bat"
  File ".dockerignore"

  ; Create data directories
  CreateDirectory "$INSTDIR\data"
  CreateDirectory "$INSTDIR\backups"
  CreateDirectory "$INSTDIR\logs"

  ; Store installation directory
  WriteRegStr HKCU "Software\AppleEsports" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\AppleEsports" "Mode" "Server"

  ; Run setup script
  ExecWait "$INSTDIR\setup-server.bat"

SectionEnd

Section "Client App (Gaming PC - Optional)" SEC_CLIENT
  ; Optional: Install client app on gaming PC

  SetOutPath "$INSTDIR\client"
  File /r "client\dist\*.*"
  File "setup-client.bat"

  ; Create shortcuts
  CreateDirectory "$SMPROGRAMS\Apple Esports"
  CreateShortcut "$SMPROGRAMS\Apple Esports\Apple Esports Client.lnk" "$INSTDIR\client\index.html" "" "$INSTDIR\client\icon.ico"
  CreateShortcut "$DESKTOP\Apple Esports Client.lnk" "$INSTDIR\client\index.html"

  ; Register for Add/Remove Programs
  WriteRegStr HKCU "Software\AppleEsports" "ClientMode" "true"

SectionEnd

Section "Documentation" SEC_DOCS
  ; Optional: Install user guides

  SetOutPath "$INSTDIR\docs"
  File "README.md"
  File "SETUP_GUIDE.md"

SectionEnd

; Uninstaller Section
Section "Uninstall"

  ; Stop Docker containers if running
  ExecWait "docker compose down" $0
  SetOutPath "$TEMP"

  ; Remove installation directory
  RMDir /r "$INSTDIR"

  ; Remove shortcuts
  Delete "$DESKTOP\Apple Esports Client.lnk"
  RMDir /r "$SMPROGRAMS\Apple Esports"

  ; Remove registry entries
  DeleteRegKey HKCU "Software\AppleEsports"

SectionEnd

; Section Descriptions
LangString DESC_SERVER ${LANG_ENGLISH} "Install Docker containers and backend services. Required for branch operation."
LangString DESC_CLIENT ${LANG_ENGLISH} "Install the gaming PC client application (optional, for testing)."
LangString DESC_DOCS ${LANG_ENGLISH} "Install documentation and user guides."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_SERVER} $(DESC_SERVER)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_CLIENT} $(DESC_CLIENT)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DOCS} $(DESC_DOCS)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; Custom Install Function
Function .onInstSuccess
  ${If} ${SectionIsSelected} ${SEC_SERVER}
    MessageBox MB_YESNO "Server installation complete!$\n$\nWould you like to open the dashboard now?" IDYES OpenDashboard IDNO SkipDashboard

    OpenDashboard:
      ExecShell "open" "http://localhost:3000"

    SkipDashboard:
  ${EndIf}
FunctionEnd

; Pre-Installation Checks
Function .onInit
  ; Check if Docker is installed
  ExecWait "docker --version" $0
  ${If} $0 != 0
    MessageBox MB_YESNO "Docker Desktop is required but not installed.$\n$\nWould you like to install it now?" IDYES InstallDocker IDNO ContinueAnyway

    InstallDocker:
      ExecShell "open" "https://www.docker.com/products/docker-desktop"
      Abort "Please install Docker Desktop and run the installer again."

    ContinueAnyway:
      MessageBox MB_OK "Continuing without Docker. Server Mode will not work without it."
  ${EndIf}
FunctionEnd
