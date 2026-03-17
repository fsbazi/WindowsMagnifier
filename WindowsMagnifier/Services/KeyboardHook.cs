using System;
using System.Runtime.InteropServices;

namespace WindowsMagnifier.Services;

/// <summary>
/// 全局键盘钩子
/// </summary>
public class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    // 修饰键虚拟键码
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;    // Alt
    private const int VK_CAPITAL = 0x14; // CapsLock
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_NUMLOCK = 0x90;
    private const int VK_SCROLL = 0x91;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;

    /// <summary>
    /// 键盘按键事件
    /// </summary>
    public event Action<int>? KeyPressed;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule?.ModuleName ?? string.Empty), 0);
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
        if (nCode >= 0 && (wParam.ToInt32() == WM_KEYDOWN || wParam.ToInt32() == WM_SYSKEYDOWN))
        {
            try
            {
                var vkCode = Marshal.ReadInt32(lParam);
                // 过滤修饰键，只响应实际字符输入键
                if (vkCode < 255 && !IsModifierKey(vkCode))
                {
                    KeyPressed?.Invoke(vkCode);
                }
            }
            catch
            {
                // 忽略键盘处理错误，避免影响系统
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsModifierKey(int vkCode)
    {
        return vkCode == VK_SHIFT || vkCode == VK_CONTROL || vkCode == VK_MENU ||
               vkCode == VK_CAPITAL || vkCode == VK_LWIN || vkCode == VK_RWIN ||
               vkCode == VK_LSHIFT || vkCode == VK_RSHIFT ||
               vkCode == VK_LCONTROL || vkCode == VK_RCONTROL ||
               vkCode == VK_LMENU || vkCode == VK_RMENU ||
               vkCode == VK_NUMLOCK || vkCode == VK_SCROLL;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
