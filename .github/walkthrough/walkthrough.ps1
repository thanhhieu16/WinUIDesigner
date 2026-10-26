# Drives WinUIDesigner.Shell.App with synthetic input and captures what it shows, so the manual
# walkthrough items no test process can see can be checked without a person at the machine.
#
# Uses only what CLAUDE.md records as working against this app: SetCursorPos + mouse_event for
# the mouse and keybd_event for keys. UI Automation kills the app and SendInput is silently
# dropped, so neither is used.
#
# Each line of the steps file is one command; blank lines and lines starting with # are skipped.
# Coordinates are relative to the window's client area, which is also what every screenshot
# shows, so a point read off a screenshot is the point a click takes.
#
#   shot <name>                 capture the client area
#   screen <name>               capture the whole primary screen (menu flyouts, dialogs)
#   wait <ms>
#   click <x> <y>
#   dblclick <x> <y>
#   drag <x1> <y1> <x2> <y2>    press, move in steps, release
#   key <name>[+<name>...]      e.g. key ctrl+z, key escape
#   type <text>
#   launch [<path>...]          end the app and start it again with these arguments
#   copy <from> <to>            copy a file
#   dropfile <x1> <y1> <x2> <y2> <path>...
#                               drag files from a helper window at (x1,y1) and drop them at
#                               (x2,y2), the way a drag from Explorer does (drop-helper.ps1)
#
# In a path, {docs} stands for the Documents folder.
#
# Screenshots are written to the output directory and also printed to the log as base64
# between "BEGIN-SHOT <name>" and "END-SHOT" lines.

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Steps,
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[Native]::SetProcessDPIAware() | Out-Null

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Write-Host "primary screen: $($screen.Width)x$($screen.Height)"
$docs = [Environment]::GetFolderPath('MyDocuments')
$Exe = (Resolve-Path $Exe).Path

function Start-App([string[]]$arguments) {
    if ($arguments -and $arguments.Count -gt 0) {
        $quoted = $arguments | ForEach-Object { '"' + $_ + '"' }
        $script:process = Start-Process -FilePath $Exe -ArgumentList $quoted -PassThru
    } else {
        $script:process = Start-Process -FilePath $Exe -PassThru
    }
    $deadline = (Get-Date).AddSeconds(60)
    while ($script:process.MainWindowHandle -eq [IntPtr]::Zero) {
        if ($script:process.HasExited) { throw "the app exited during startup with code $($script:process.ExitCode)" }
        if ((Get-Date) -gt $deadline) { throw 'no main window within 60s' }
        Start-Sleep -Milliseconds 250
        $script:process.Refresh()
    }
    $script:hwnd = $script:process.MainWindowHandle
    [Native]::MoveWindow($script:hwnd, 0, 0, [Math]::Min(1400, $screen.Width), [Math]::Min(900, $screen.Height), $true) | Out-Null
    [Native]::SetForegroundWindow($script:hwnd) | Out-Null
    Start-Sleep -Seconds 3
}

function Expand-Path([string]$path) { return $path.Replace('{docs}', $docs) }

Start-App @()

function Get-ClientOrigin {
    $p = New-Object Native+POINT
    [Native]::ClientToScreen($hwnd, [ref]$p) | Out-Null
    return $p
}

function Save-Shot([string]$name, [int]$x, [int]$y, [int]$w, [int]$h) {
    $bitmap = New-Object System.Drawing.Bitmap $w, $h
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $path = Join-Path $OutDir "$name.png"
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))
    Write-Host "BEGIN-SHOT $name ${w}x${h}"
    for ($i = 0; $i -lt $b64.Length; $i += 1000) { Write-Host $b64.Substring($i, [Math]::Min(1000, $b64.Length - $i)) }
    Write-Host 'END-SHOT'
}

function To-Screen([int]$x, [int]$y) {
    $origin = Get-ClientOrigin
    return @(($origin.X + $x), ($origin.Y + $y))
}

$keys = @{ ctrl = 0x11; shift = 0x10; alt = 0x12; escape = 0x1B; enter = 0x0D; delete = 0x2E; tab = 0x09; f5 = 0x74; home = 0x24; end = 0x23 }
function Press-Key([string]$combo) {
    $codes = foreach ($part in $combo.ToLower().Split('+')) {
        if ($keys.ContainsKey($part)) { $keys[$part] } else { [Native]::VkKeyScan($part[0]) -band 0xFF }
    }
    foreach ($c in $codes) { [Native]::keybd_event([byte]$c, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }
    [array]::Reverse($codes)
    foreach ($c in $codes) { [Native]::keybd_event([byte]$c, 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }
}

try {
foreach ($raw in Get-Content $Steps) {
    $line = $raw.Trim()
    if ($line -eq '' -or $line.StartsWith('#')) { continue }
    if ($process.HasExited) { throw "the app exited with code $($process.ExitCode) before: $line" }
    Write-Host "> $line"
    $parts = $line -split '\s+', 2
    $args1 = if ($parts.Count -gt 1) { $parts[1] } else { '' }
    $n = @($args1 -split '\s+' | Where-Object { $_ -match '^-?\d+$' } | ForEach-Object { [int]$_ })
    switch ($parts[0]) {
        'shot' {
            $r = New-Object Native+RECT
            [Native]::GetClientRect($hwnd, [ref]$r) | Out-Null
            $o = Get-ClientOrigin
            Save-Shot $args1 $o.X $o.Y $r.Right $r.Bottom
        }
        'screen' { Save-Shot $args1 0 0 $screen.Width $screen.Height }
        'wait' { Start-Sleep -Milliseconds $n[0] }
        'click' {
            $s = To-Screen $n[0] $n[1]
            [Native]::SetCursorPos($s[0], $s[1]) | Out-Null; Start-Sleep -Milliseconds 100
            [Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
            [Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 400
        }
        'dblclick' {
            $s = To-Screen $n[0] $n[1]
            [Native]::SetCursorPos($s[0], $s[1]) | Out-Null; Start-Sleep -Milliseconds 100
            foreach ($i in 1..2) {
                [Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40
                [Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60
            }
            Start-Sleep -Milliseconds 400
        }
        'drag' {
            $from = To-Screen $n[0] $n[1]
            $to = To-Screen $n[2] $n[3]
            function Move-Abs([int]$x, [int]$y) {
                $nx = [uint32][Math]::Round($x * 65535 / ($screen.Width - 1))
                $ny = [uint32][Math]::Round($y * 65535 / ($screen.Height - 1))
                [Native]::mouse_event(0x8001, $nx, $ny, 0, [UIntPtr]::Zero)
            }
            # Absolute moves (MOVE | ABSOLUTE, normalised to 0..65535) rather than SetCursorPos:
            # a drag source needs real mouse-move input while the button is down.
            Move-Abs $from[0] $from[1]; Start-Sleep -Milliseconds 200
            [Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300
            # Cross the drag threshold, then hold still while the system drag session starts:
            # moves sent before it is running are not seen by the drop target.
            foreach ($i in 1..5) { Move-Abs ($from[0] + 3 * $i) $from[1]; Start-Sleep -Milliseconds 40 }
            Start-Sleep -Milliseconds 1000
            foreach ($i in 1..60) {
                $x = [int]($from[0] + 15 + ($to[0] - $from[0] - 15) * $i / 60)
                $y = [int]($from[1] + ($to[1] - $from[1]) * $i / 60)
                Move-Abs $x $y
                Start-Sleep -Milliseconds 50
            }
            Start-Sleep -Milliseconds 600
            [Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 1500
        }
        'key' { Press-Key $args1; Start-Sleep -Milliseconds 400 }
        'type' {
            foreach ($ch in $args1.ToCharArray()) {
                $vk = [Native]::VkKeyScan($ch)
                $shift = ($vk -band 0x100) -ne 0
                if ($shift) { [Native]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero) }
                [Native]::keybd_event([byte]($vk -band 0xFF), 0, 0, [UIntPtr]::Zero)
                [Native]::keybd_event([byte]($vk -band 0xFF), 0, 2, [UIntPtr]::Zero)
                if ($shift) { [Native]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero) }
                Start-Sleep -Milliseconds 30
            }
        }
        'launch' {
            if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
            $paths = @($args1 -split '\s+' | Where-Object { $_ -ne '' } | ForEach-Object { Expand-Path $_ })
            Start-App $paths
        }
        'copy' {
            $p = @($args1 -split '\s+' | ForEach-Object { Expand-Path $_ })
            Copy-Item -Force $p[0] $p[1]
        }
        'dropfile' {
            $words = @($args1 -split '\s+')
            $from = To-Screen ([int]$words[0]) ([int]$words[1])
            $to = To-Screen ([int]$words[2]) ([int]$words[3])
            $paths = ($words[4..($words.Count - 1)] | ForEach-Object { Expand-Path $_ }) -join '|'
            # Windows PowerShell, not pwsh: OLE drag and drop needs a single-threaded apartment,
            # and -STA gives one.
            $helper = Join-Path $PSScriptRoot 'drop-helper.ps1'
            Remove-Item -ErrorAction SilentlyContinue (Join-Path $env:TEMP 'wd-drop.log')
            $out = & powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File $helper `
                -FromX $from[0] -FromY $from[1] -ToX $to[0] -ToY $to[1] -Paths $paths `
                -ScreenWidth $screen.Width -ScreenHeight $screen.Height 2>&1
            Write-Host "drop helper: $out"
            $trace = Join-Path $env:TEMP 'wd-drop.log'
            if (Test-Path $trace) { Get-Content $trace | Select-Object -Last 20 | ForEach-Object { Write-Host "app trace: $_" } } else { Write-Host 'app trace: none' }
            [Native]::SetForegroundWindow($hwnd) | Out-Null
        }
        default { throw "unknown step: $line" }
    }
}

if ($process.HasExited) { throw "the app exited with code $($process.ExitCode)" }
Write-Host 'walkthrough finished; the app is still running'
} catch {
    # Written next to the screenshots so it travels to refs/walkthrough/latest with them.
    "$_" | Out-File (Join-Path $OutDir 'error.txt')
    throw
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
