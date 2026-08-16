<#
    Triggers Apple Esports' own Ctrl+Alt+Q shortcut from a script, for when physically
    pressing the key combination does not register - some keyboard layouts treat Ctrl+Alt
    as the AltGr modifier, which can stop the physical Q key from being recognised.

    This does NOT force-kill the app. It simulates the real key combination using raw
    virtual-key codes (VK_CONTROL, VK_MENU, VK_Q) rather than SendKeys, which looks a
    character up per the CURRENT keyboard layout and can hit the exact same AltGr problem
    this script exists to route around. Virtual-key codes address the physical key position
    instead, the same fix already applied inside the app itself (see MainForm.cs).

    Because it is the real shortcut, it goes through the app's own PIN prompt if one is
    configured on this machine - this is not a back door around that.

    Usage:  pwsh quit-app.ps1
#>

$ErrorAction = 'Stop'

$proc = Get-Process -Name 'AppleEsports' -ErrorAction SilentlyContinue
if (-not $proc) {
    Write-Host 'Apple Esports is not running.'
    exit
}

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class KeySim
{
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@

# The keys go to whichever window currently has focus, so it has to be brought to the
# front first.
[KeySim]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300

$KEYEVENTF_KEYUP = 0x0002
$VK_CONTROL      = 0x11
$VK_MENU         = 0x12   # Alt
$VK_Q            = 0x51   # Same code on every layout - it names the physical key, not a character

[KeySim]::keybd_event($VK_CONTROL, 0, 0, [UIntPtr]::Zero)
[KeySim]::keybd_event($VK_MENU,    0, 0, [UIntPtr]::Zero)
[KeySim]::keybd_event($VK_Q,       0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[KeySim]::keybd_event($VK_Q,       0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero)
[KeySim]::keybd_event($VK_MENU,    0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero)
[KeySim]::keybd_event($VK_CONTROL, 0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero)

Write-Host 'Sent Ctrl+Alt+Q to Apple Esports.'
