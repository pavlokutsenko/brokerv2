param(
    [Parameter(Mandatory)][int]$ClientPid,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CaptureWindowNative {
    public delegate bool EnumWindowProc(IntPtr window, IntPtr state);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowProc callback, IntPtr state);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
}
'@

$chosen = [IntPtr]::Zero
$rectangle = New-Object CaptureWindowNative+Rect
$area = [long]0
$callback = [CaptureWindowNative+EnumWindowProc]{
    param($window, $state)
    [uint32]$owner = 0
    [void][CaptureWindowNative]::GetWindowThreadProcessId($window, [ref]$owner)
    if ($owner -ne $ClientPid -or -not [CaptureWindowNative]::IsWindowVisible($window)) { return $true }
    $current = New-Object CaptureWindowNative+Rect
    if (-not [CaptureWindowNative]::GetWindowRect($window, [ref]$current)) { return $true }
    $size = [long][Math]::Max(0, $current.Right - $current.Left) * [Math]::Max(0, $current.Bottom - $current.Top)
    if ($size -gt $area) {
        $script:area = $size
        $script:chosen = $window
        $script:rectangle = $current
    }
    return $true
}
[void][CaptureWindowNative]::EnumWindows($callback, [IntPtr]::Zero)
if ($chosen -eq [IntPtr]::Zero) { throw "No visible window for PID $ClientPid" }
[void][CaptureWindowNative]::SetForegroundWindow($chosen)
Start-Sleep -Milliseconds 400
$width = $rectangle.Right - $rectangle.Left
$height = $rectangle.Bottom - $rectangle.Top
$bitmap = [System.Drawing.Bitmap]::new($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen($rectangle.Left, $rectangle.Top, 0, 0, $bitmap.Size)
    $directory = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally { $graphics.Dispose(); $bitmap.Dispose() }
Get-Item -LiteralPath $OutputPath | Select-Object FullName,Length
