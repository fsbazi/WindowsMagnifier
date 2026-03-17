Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public class W32 {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    public static List<string> Results = new List<string>();
}
"@

$targetPids = New-Object System.Collections.ArrayList
Get-Process -Name WindowsMagnifier -ErrorAction SilentlyContinue | ForEach-Object { [void]$targetPids.Add($_.Id); Write-Output "Process PID=$($_.Id)" }

$callback = {
    param([IntPtr]$hwnd, [IntPtr]$lParam)
    [uint32]$wpid = 0
    try {
        [W32]::GetWindowThreadProcessId($hwnd, [ref]$wpid) | Out-Null
    } catch { return $true }
    if ($targetPids.Contains([int]$wpid)) {
        $rect = New-Object W32+RECT
        [W32]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
        $vis = [W32]::IsWindowVisible($hwnd)
        $titleBuf = New-Object System.Text.StringBuilder 256
        [W32]::GetWindowText($hwnd, $titleBuf, 256) | Out-Null
        $clsBuf = New-Object System.Text.StringBuilder 256
        [W32]::GetClassName($hwnd, $clsBuf, 256) | Out-Null
        $style = [W32]::GetWindowLong($hwnd, -16)
        $exstyle = [W32]::GetWindowLong($hwnd, -20)
        $w = $rect.Right - $rect.Left
        $h = $rect.Bottom - $rect.Top
        $line = "HWND=$hwnd PID=$wpid Vis=$vis Cls=$($clsBuf.ToString()) Title=$($titleBuf.ToString()) L=$($rect.Left) T=$($rect.Top) R=$($rect.Right) B=$($rect.Bottom) ${w}x${h} Style=0x$($style.ToString('X')) ExStyle=0x$($exstyle.ToString('X'))"
        [W32]::Results.Add($line)
    }
    return $true
}
[W32]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null

foreach ($r in [W32]::Results) { Write-Output $r }

Write-Output "---Screens---"
Add-Type -AssemblyName System.Windows.Forms
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
    Write-Output "$($s.DeviceName) Bounds=$($s.Bounds) WorkArea=$($s.WorkingArea) Primary=$($s.Primary)"
}

Write-Output "---Taskbar---"
$tbh = [W32]::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, "Shell_TrayWnd", $null)
if ($tbh -ne [IntPtr]::Zero) {
    $r = New-Object W32+RECT
    [W32]::GetWindowRect($tbh, [ref]$r) | Out-Null
    Write-Output "Taskbar L=$($r.Left) T=$($r.Top) R=$($r.Right) B=$($r.Bottom)"
}
