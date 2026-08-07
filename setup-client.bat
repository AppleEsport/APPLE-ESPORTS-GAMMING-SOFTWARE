@echo off
REM ================================================================
REM Apple Esports ERP - Client Mode Setup (Gaming PC)
REM Installs the client app and connects to branch server
REM ================================================================

setlocal enabledelayedexpansion

echo.
echo ================================================================
echo  Apple Esports ERP - Client Mode Installation
echo ================================================================
echo.

REM Create app directory
set "APPDIR=%APPDATA%\AppleEsports"
if not exist "%APPDIR%" mkdir "%APPDIR%"

echo [1/3] Creating client configuration directory...
if not exist "%APPDIR%\config" mkdir "%APPDIR%\config"

echo [2/3] Setting up client shortcuts...
REM Create desktop shortcut
set "DESKTOP=%USERPROFILE%\Desktop"
powershell -Command ^
    "$WshShell = New-Object -ComObject WScript.Shell; " ^
    "$Shortcut = $WshShell.CreateShortcut('%DESKTOP%\Apple Esports Client.lnk'); " ^
    "$Shortcut.TargetPath = '%CD%\client\index.html'; " ^
    "$Shortcut.Description = 'Apple Esports Gaming PC Client'; " ^
    "$Shortcut.Save()"

echo [3/3] Creating startup batch file...
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
(
    echo @echo off
    echo cd /d "%CD%\client"
    echo start http://localhost:3001
    echo npm run preview -- --host 0.0.0.0 --port 3001
) > "%STARTUP%\AppleEsportsClient.bat"

echo.
echo ================================================================
echo  Client Installation Complete!
echo ================================================================
echo.
echo Your Apple Esports Client is installed and ready to use.
echo.
echo IMPORTANT: On first run, you will be asked to enter:
echo   - Your branch's Operator PC IP address
echo   - Your server name (e.g., 192.168.29.100:5016)
echo.
echo A shortcut has been created on your desktop:
echo   "Apple Esports Client.lnk"
echo.
echo The client will also start automatically with Windows.
echo.
echo To start the client manually:
echo   1. Click the desktop shortcut, OR
echo   2. Run: npm run preview (from client folder)
echo.
echo To find your Operator PC IP address:
echo   - Ask your branch manager
echo   - It's usually printed on a label on the Operator PC
echo   - Format: 192.168.x.x:5016
echo.
echo ================================================================
echo.
pause
