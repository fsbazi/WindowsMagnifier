using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WindowsMagnifier.Services;

/// <summary>
/// 全局快捷键服务 - 管理 Win+Alt+M 快捷键的注册、消息处理和注销
/// </summary>
public class HotkeyService : IDisposable
{
    private const int HOTKEY_ID = 0x4D41; // 'MA'
    private const int MOD_ALT = 0x0001;
    private const int MOD_WIN = 0x0008;
    private const int MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312;
    private const int VK_M = 0x4D;

    private static readonly LogService _log = LogService.Instance;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _hotkeySource;
    private bool _disposed;

    /// <summary>
    /// 快捷键触发时引发的事件
    /// </summary>
    public event Action? HotkeyTriggered;

    /// <summary>
    /// 注册全局快捷键 Win+Alt+M
    /// </summary>
    public void Register()
    {
        try
        {
            var parameters = new HwndSourceParameters("HotkeyWindow")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0
            };
            _hotkeySource = new HwndSource(parameters);
            _hotkeySource.AddHook(HotkeyWndProc);

            if (!RegisterHotKey(_hotkeySource.Handle, HOTKEY_ID, MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_M))
            {
                _log.LogError($"RegisterHotKey failed, error: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                _log.LogError("Global hotkey Win+Alt+M registered");
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"RegisterGlobalHotkey exception: {ex.Message}");
        }
    }

    private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyTriggered?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hotkeySource != null)
        {
            UnregisterHotKey(_hotkeySource.Handle, HOTKEY_ID);
            _hotkeySource.Dispose();
            _hotkeySource = null;
        }
    }
}
