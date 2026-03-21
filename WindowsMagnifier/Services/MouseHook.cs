using System;
using System.Runtime.InteropServices;

namespace WindowsMagnifier.Services;

/// <summary>
/// 全局鼠标钩子
/// </summary>
public class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelMouseProc _proc;

    public event Action<int, int>? MouseMoved;

    public MouseHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule?.ModuleName ?? string.Empty), 0);
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_MOUSEMOVE && lParam != IntPtr.Zero)
        {
            // 直接从内存读取 X/Y（MSLLHOOKSTRUCT 的前两个字段），避免堆分配
            int x = Marshal.ReadInt32(lParam);
            int y = Marshal.ReadInt32(lParam, 4);
            // 先调用 CallNextHookEx 释放钩子链，再触发事件
            var result = CallNextHookEx(_hookId, nCode, wParam, lParam);
            try
            {
                MouseMoved?.Invoke(x, y);
            }
            catch (Exception ex)
            {
                // 钩子回调中绝不能让异常逃逸，否则 Windows 会静默移除钩子
                System.Diagnostics.Debug.WriteLine($"[MouseHook] MouseMoved handler exception: {ex.Message}");
            }
            return result;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
