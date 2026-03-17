Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
}
"@

$procs = Get-Process -Name WindowsMagnifier -ErrorAction SilentlyContinue
foreach ($p in $procs) {
    $hwnd = $p.MainWindowHandle
    $rect = New-Object Win32+RECT
    [Win32]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    Write-Output "PID=$($p.Id) HWND=$hwnd Left=$($rect.Left) Top=$($rect.Top) Right=$($rect.Right) Bottom=$($rect.Bottom)"
}

# Also check taskbar position
$taskbar = Get-Process -Name explorer -ErrorAction SilentlyContinue
Write-Output "---"
Write-Output "Screen info:"
Add-Type -AssemblyName System.Windows.Forms
foreach ($screen in [System.Windows.Forms.Screen]::AllScreens) {
    Write-Output "  $($screen.DeviceName): Bounds=$($screen.Bounds) WorkingArea=$($screen.WorkingArea)"
}
