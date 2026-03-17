$exePath = Join-Path $PSScriptRoot "bin\publish\WindowsMagnifier.exe"
Start-Process $exePath
Start-Sleep -Seconds 3

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public class Win32Check {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
"@

$targetPids = @()
$procs = Get-Process -Name WindowsMagnifier -ErrorAction SilentlyContinue
foreach ($p in $procs) {
    $targetPids += $p.Id
    Write-Output "Found process PID=$($p.Id)"
}

$results = @()
[Win32Check]::EnumWindows({
    param($hwnd, $lParam)
    $pid = 0
    [Win32Check]::GetWindowThreadProcessId($hwnd, [ref]$pid) | Out-Null
    if ($script:targetPids -contains $pid) {
        $rect = New-Object Win32Check+RECT
        [Win32Check]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
        $visible = [Win32Check]::IsWindowVisible($hwnd)
        $sb = New-Object System.Text.StringBuilder 256
        [Win32Check]::GetWindowText($hwnd, $sb, 256) | Out-Null
        $title = $sb.ToString()
        $cb = New-Object System.Text.StringBuilder 256
        [Win32Check]::GetClassName($hwnd, $cb, 256) | Out-Null
        $className = $cb.ToString()
        Write-Output "  HWND=$hwnd Visible=$visible Class=$className Title='$title' Rect=($($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom)) Size=$($rect.Right-$rect.Left)x$($rect.Bottom-$rect.Top)"
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
