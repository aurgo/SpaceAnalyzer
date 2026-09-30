# Runs SpaceAnalyzer on a real Windows desktop (e.g. a GitHub Actions runner): checks that it starts and
# survives mouse and keyboard input, and saves a screenshot after each step.
#
#   pwsh ./build/smoke-windows.ps1 -Exe publish/SpaceAnalyzer.exe -Folder "C:\Program Files\dotnet\shared"
param(
    [string]$Exe = "publish/SpaceAnalyzer.exe",
    [string]$Folder = "C:\Program Files\dotnet\shared",
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

# The app keeps running after an unexpected error (it shows a message): it also writes it here.
$errorLog = Join-Path (Resolve-Path $Out) "windows-errors.log"
Remove-Item $errorLog -ErrorAction Ignore
$env:SPACEANALYZER_ERROR_LOG = $errorLog
$env:SPACEANALYZER_NO_UPDATE_CHECK = "1"

function Assert-Running([string]$Step) {
    if ($app.HasExited) { throw "SpaceAnalyzer exited $Step (exit code $($app.ExitCode))" }
    if (Test-Path $errorLog) {
        Save-Screen "windows-error.png"
        throw "SpaceAnalyzer reported an error $($Step):`n$(Get-Content -Raw $errorLog)"
    }
}

function Click([int]$X, [int]$Y, [int]$Times = 1, [switch]$Right) {
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($X, $Y)
    Start-Sleep -Milliseconds 150
    $down = if ($Right) { 0x0008 } else { 0x0002 }
    $up = if ($Right) { 0x0010 } else { 0x0004 }
    for ($i = 0; $i -lt $Times; $i++) {
        [SaInput]::mouse_event($down, 0, 0, 0, [UIntPtr]::Zero)
        [SaInput]::mouse_event($up, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
    }
}

$app = Start-Process -FilePath $Exe -ArgumentList "`"$Folder`"" -PassThru
Start-Sleep -Seconds 2
Assert-Running "while starting"
Save-Screen "windows-0-scanning.png"

# The title becomes "<folder> — SpaceAnalyzer" once the scan is done.
$dash = [char]0x2014
$deadline = (Get-Date).AddSeconds(180)
do {
    Start-Sleep -Milliseconds 500
    Assert-Running "while scanning"
    $app.Refresh()
} until ($app.MainWindowTitle.Contains($dash) -or (Get-Date) -gt $deadline)
if (-not $app.MainWindowTitle.Contains($dash)) {
    Save-Screen "windows-timeout.png"
    throw "The scan did not finish in time"
}
Start-Sleep -Milliseconds 800

$hwnd = $app.MainWindowHandle
[SaInput]::SetForegroundWindow($hwnd) | Out-Null
$r = New-Object SaInput+RECT
[SaInput]::GetWindowRect($hwnd, [ref]$r) | Out-Null
Write-Host "Window at $($r.Left),$($r.Top) - $($r.Right),$($r.Bottom); title: $($app.MainWindowTitle)"
Save-Screen "windows-1-treemap.png"

# Hover the treemap: the info card appears after a short pause.
$x = [int]($r.Left + ($r.Right - $r.Left) * 0.3)
$y = [int]($r.Top + ($r.Bottom - $r.Top) * 0.55)
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
Start-Sleep -Milliseconds 1500
Save-Screen "windows-2-hover.png"
Assert-Running "after moving the mouse"

# Double-click: zoom into the folder under the pointer (animated).
Click $x $y -Times 2
Start-Sleep -Milliseconds 1200
Save-Screen "windows-3-zoomed.png"
Assert-Running "after a double-click"

# Typing starts a search that highlights matches (and shows how many there are).
[System.Windows.Forms.SendKeys]::SendWait("dll")
Start-Sleep -Milliseconds 1200
Save-Screen "windows-4-search.png"
Assert-Running "after typing"

# Right-click opens the context menu (drawn by the app); Escape closes it.
[System.Windows.Forms.SendKeys]::SendWait("{ESC}{ESC}")
Click $x $y -Right
Start-Sleep -Milliseconds 800
Save-Screen "windows-5-menu.png"
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Assert-Running "after the context menu"

# Backspace goes back up a level.
[System.Windows.Forms.SendKeys]::SendWait("{BACKSPACE}")
Start-Sleep -Milliseconds 1200
Save-Screen "windows-6-back.png"
Assert-Running "after using the keyboard"

Stop-Process -Id $app.Id -Force
Write-Host "SpaceAnalyzer ran fine on Windows."
