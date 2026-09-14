<#
    Registers the "restart if killed" reaction - see restart-on-kill.ps1 for what it does and
    why this shape (an event-triggered task, not a running program) was chosen.

    Run once by the installer, already elevated - the same reason apply-update.ps1's own
    -Register step has to be the installer and not the app (see that script's own comment).

    Two pieces of native Windows plumbing, both one-time and both idempotent (safe to run again
    on every upgrade without asking):

    1. Turns on auditing for "a process ended", a built-in Windows security feature that is off
       by default because most machines have no reason to log every process exit. Once on,
       Windows itself writes Event ID 4689 to the Security log for every process that ends -
       this app's own exits included, with no code of this app's own involved in producing it.

    2. Registers a Scheduled Task whose only trigger is that specific event, filtered to this
       one executable's own full path. Imported from XML because the event trigger it needs -
       "run when THIS EXACT event appears in the log" - has no equivalent in schtasks.exe's
       plain command-line switches.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$AppExePath
)

$ErrorActionPreference = 'Stop'
$TaskName = 'AppleEsports Restart On Kill'
$ScriptPath = Join-Path $PSScriptRoot 'restart-on-kill.ps1'

Write-Output "Enabling process-termination auditing..."
# By GUID, not the English name "Process Termination" - auditpol.exe matches subcategory names
# against whatever language Windows itself is displaying, so the literal English string quietly
# matches nothing on a non-English install with no error at all. The GUID is the one thing
# guaranteed to mean the same subcategory regardless of what language this PC is set to.
& auditpol.exe /set /subcategory:"{0CCE9226-69AE-11D9-BED3-505054503030}" /success:enable | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Output "Could not enable process-termination auditing (auditpol exit $LASTEXITCODE) - the task below will register but will never actually fire without it."
}

# Event 4689's own ProcessName field is the exe's full path, backslashes and all - escaped for
# XML (only '&' can legally appear in a Windows path-like string here, but escaped for
# correctness regardless of what a branch's own install directory happens to be named).
$escapedPath = $AppExePath -replace '&', '&amp;'
$escapedScript = $ScriptPath -replace '&', '&amp;'

$xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Restarts this PC if AppleEsports.exe ends without going through its own exit - see restart-on-kill.ps1.</Description>
  </RegistrationInfo>
  <Triggers>
    <EventTrigger>
      <Enabled>true</Enabled>
      <Subscription>&lt;QueryList&gt;&lt;Query Id="0" Path="Security"&gt;&lt;Select Path="Security"&gt;*[System[(EventID=4689)]] and *[EventData[Data[@Name='ProcessName']='$escapedPath']]&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;</Subscription>
    </EventTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT1M</ExecutionTimeLimit>
    <Hidden>true</Hidden>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>powershell.exe</Command>
      <Arguments>-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "$escapedScript"</Arguments>
    </Exec>
  </Actions>
</Task>
"@

$xmlPath = Join-Path $env:TEMP 'AppleEsportsRestartOnKill.xml'
# UTF-16 with BOM - schtasks.exe /XML silently fails to import a task with an event trigger
# from a plain UTF-8 file.
[System.IO.File]::WriteAllText($xmlPath, $xml, [System.Text.Encoding]::Unicode)

try {
    & schtasks.exe /Create /F /TN $TaskName /XML $xmlPath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Output "Could not register '$TaskName' (schtasks exit $LASTEXITCODE)."
        exit $LASTEXITCODE
    }
    Write-Output "Registered '$TaskName', watching $AppExePath."
}
finally {
    Remove-Item $xmlPath -Force -ErrorAction SilentlyContinue
}
