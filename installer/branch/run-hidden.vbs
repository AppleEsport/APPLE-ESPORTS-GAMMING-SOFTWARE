' Runs a PowerShell script with no window at all, ever.
'
' Needed because Task Scheduler launching powershell.exe as the logged-in user flashes a console
' window every single time, and -WindowStyle Hidden does not prevent it: the console is created
' before PowerShell can hide itself. The kiosk watchdog runs every two minutes, so on a gaming PC
' that was a black box blinking over the top of whatever the customer was playing, all evening.
'
' WScript.Shell's Run with a window style of 0 never creates one in the first place. The update
' task does not need this because it runs as SYSTEM, which has no desktop to draw on.
'
' Usage:  wscript.exe //B //Nologo run-hidden.vbs "C:\...\kiosk-guard.ps1"

Dim shell, script, i, args
Set shell = CreateObject("WScript.Shell")

If WScript.Arguments.Count = 0 Then WScript.Quit 1

script = WScript.Arguments(0)

args = ""
For i = 1 To WScript.Arguments.Count - 1
    args = args & " " & WScript.Arguments(i)
Next

' 0 = hidden window, False = do not wait. Quoted because the path contains spaces.
shell.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -File """ & script & """" & args, 0, False
