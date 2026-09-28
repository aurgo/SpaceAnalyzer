# Runs SpaceAnalyzer on a real Windows desktop (e.g. a GitHub Actions runner): checks that it starts and
# survives mouse and keyboard input, and saves a screenshot after each step.
#
#   pwsh ./build/smoke-windows.ps1 -Exe publish/SpaceAnalyzer.exe -Folder "C:\Program Files\dotnet"
param(
    [string]$Exe = "publish/SpaceAnalyzer.exe",
    [string]$Folder = "C:\Program Files\dotnet",
    [string]$Out = "screenshots"
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $Out | Out-Null
try { Set-DisplayResolution -Width 1600 -Height 1000 -Force } catch { Write-Host "Keeping the default screen resolution." }

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class SaInput {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
}
"@

function Save-Screen([string]$Name) {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $bmp.Save((Join-Path $Out $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
}

function Assert-Running([string]$Step) {
    if ($app.HasExited) { throw "SpaceAnalyzer exited $Step (exit code $($app.ExitCode))" }
}

$app = Start-Process -FilePath $Exe -ArgumentList "`"$Folder`"" -PassThru
Start-Sleep -Seconds 15
Assert-Running "while starting and scanning"
$app.Refresh()
$hwnd = $app.MainWindowHandle
[SaInput]::SetForegroundWindow($hwnd) | Out-Null
$r = New-Object SaInput+RECT
[SaInput]::GetWindowRect($hwnd, [ref]$r) | Out-Null
Write-Host "Window at $($r.Left),$($r.Top) - $($r.Right),$($r.Bottom); title: $($app.MainWindowTitle)"
Save-Screen "windows-1-treemap.png"

# Hover the treemap: the info card appears after a short pause.
$x = [int]($r.Left + ($r.Right - $r.Left) * 0.3)
$y = [int]($r.Top + ($r.Bottom - $r.Top) * 0.5)
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
Start-Sleep -Milliseconds 1500
Save-Screen "windows-2-hover.png"
Assert-Running "after moving the mouse"

# Double-click: zoom into the folder under the pointer (animated).
for ($i = 0; $i -lt 2; $i++) {
    [SaInput]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero) # left button down
    [SaInput]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero) # left button up
    Start-Sleep -Milliseconds 60
}
Start-Sleep -Milliseconds 1200
Save-Screen "windows-3-zoomed.png"
Assert-Running "after a double-click"

# Typing starts a search that highlights matches.
[System.Windows.Forms.SendKeys]::SendWait("dll")
Start-Sleep -Milliseconds 1000
Save-Screen "windows-4-search.png"
Assert-Running "after typing"

# Backspace x3 clears the search, then Backspace goes back up a level.
[System.Windows.Forms.SendKeys]::SendWait("{BACKSPACE}{BACKSPACE}{BACKSPACE}{ESC}{BACKSPACE}")
Start-Sleep -Milliseconds 1200
Save-Screen "windows-5-back.png"
Assert-Running "after using the keyboard"

Stop-Process -Id $app.Id -Force
Write-Host "SpaceAnalyzer ran fine on Windows."
