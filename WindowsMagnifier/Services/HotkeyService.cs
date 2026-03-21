using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using WindowsMagnifier.Models;

namespace WindowsMagnifier.Services;

/// <summary>
/// 全局快捷键服务 - 管理多个自定义快捷键的注册、消息处理和注销。
/// 支持的快捷键：
///   - ToggleAll: 全局切换所有放大镜窗口
///   - ToggleCurrent: 切换鼠标所在屏幕的放大镜窗口
/// </summary>
public class HotkeyService : IDisposable
{
    private const int HOTKEY_ID_TOGGLE_ALL = 0x4D41;     // 'MA' - 全局切换
    private const int HOTKEY_ID_TOGGLE_CURRENT = 0x4D42; // 'MB' - 当前屏幕切换
    private const int WM_HOTKEY = 0x0312;

    private static readonly LogService _log = LogService.Instance;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _hotkeySource;
    private volatile bool _disposed;

    // 当前注册的快捷键信息（用于重新注册时对比）
    private string _currentToggleAllHotkey = string.Empty;
    private string _currentToggleCurrentHotkey = string.Empty;

    /// <summary>
    /// 全局切换（显示/隐藏所有窗口）快捷键触发时引发的事件
    /// </summary>
    public event Action? ToggleAllTriggered;

    /// <summary>
    /// 当前屏幕切换（显示/隐藏鼠标所在屏幕窗口）快捷键触发时引发的事件
    /// </summary>
    public event Action? ToggleCurrentTriggered;

    /// <summary>
    /// 根据配置注册全局快捷键
    /// </summary>
    public void Register(string toggleAllHotkey, string toggleCurrentHotkey)
    {
        try
        {
            // 如果 HwndSource 还未创建，先创建
            if (_hotkeySource == null)
            {
                var parameters = new HwndSourceParameters("HotkeyWindow")
                {
                    Width = 0,
                    Height = 0,
                    WindowStyle = 0
                };
                _hotkeySource = new HwndSource(parameters);
                _hotkeySource.AddHook(HotkeyWndProc);
            }

            // 注册全局切换快捷键
            RegisterSingleHotkey(HOTKEY_ID_TOGGLE_ALL, toggleAllHotkey, ref _currentToggleAllHotkey, "ToggleAll");

            // 注册当前屏幕切换快捷键
            RegisterSingleHotkey(HOTKEY_ID_TOGGLE_CURRENT, toggleCurrentHotkey, ref _currentToggleCurrentHotkey, "ToggleCurrent");
        }
        catch (Exception ex)
        {
            _log.LogError($"RegisterGlobalHotkeys exception: {ex.Message}");
        }
    }

    /// <summary>
    /// 注销所有快捷键并重新注册（快捷键设置变更时调用）
    /// </summary>
    public void ReRegister(string toggleAllHotkey, string toggleCurrentHotkey)
    {
        UnregisterAll();
        Register(toggleAllHotkey, toggleCurrentHotkey);
    }

    /// <summary>
    /// 注销所有快捷键
    /// </summary>
    public void UnregisterAll()
    {
        if (_hotkeySource == null) return;

        if (!string.IsNullOrEmpty(_currentToggleAllHotkey))
        {
            UnregisterHotKey(_hotkeySource.Handle, HOTKEY_ID_TOGGLE_ALL);
            _currentToggleAllHotkey = string.Empty;
        }

        if (!string.IsNullOrEmpty(_currentToggleCurrentHotkey))
        {
            UnregisterHotKey(_hotkeySource.Handle, HOTKEY_ID_TOGGLE_CURRENT);
            _currentToggleCurrentHotkey = string.Empty;
        }
    }

    /// <summary>
    /// 测试快捷键是否可注册（临时注册后立即注销）
    /// </summary>
    public bool TestRegister(string hotkeyString)
    {
        if (_hotkeySource == null) return false;

        if (!HotkeyStringHelper.TryParse(hotkeyString, out var modifiers, out var virtualKey))
            return false;

        const int HOTKEY_ID_TEST = 0x4D43; // 'MC' - 临时测试用 ID
        var result = RegisterHotKey(_hotkeySource.Handle, HOTKEY_ID_TEST, modifiers, virtualKey);
        if (result)
        {
            UnregisterHotKey(_hotkeySource.Handle, HOTKEY_ID_TEST);
        }
        return result;
    }

    private void RegisterSingleHotkey(int id, string hotkeyString, ref string currentHotkey, string name)
    {
        if (_hotkeySource == null) return;

        // 如果已注册相同快捷键，跳过
        if (currentHotkey == hotkeyString && !string.IsNullOrEmpty(currentHotkey))
            return;

        // 先注销旧快捷键
        if (!string.IsNullOrEmpty(currentHotkey))
        {
            UnregisterHotKey(_hotkeySource.Handle, id);
            currentHotkey = string.Empty;
        }

        if (!HotkeyStringHelper.TryParse(hotkeyString, out var modifiers, out var virtualKey))
        {
            _log.LogError($"无法解析快捷键 {name}: {hotkeyString}");
            return;
        }

        if (!RegisterHotKey(_hotkeySource.Handle, id, modifiers, virtualKey))
        {
            _log.LogError($"RegisterHotKey {name} ({hotkeyString}) failed, error: {Marshal.GetLastWin32Error()}");
        }
        else
        {
            currentHotkey = hotkeyString;
            _log.LogDebug($"Global hotkey {name} ({hotkeyString}) registered");
        }
    }

    private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = (int)(wParam.ToInt64() & 0xFFFF);
            if (id == HOTKEY_ID_TOGGLE_ALL)
            {
                ToggleAllTriggered?.Invoke();
                handled = true;
            }
            else if (id == HOTKEY_ID_TOGGLE_CURRENT)
            {
                ToggleCurrentTriggered?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hotkeySource != null)
        {
            UnregisterAll();
            _hotkeySource.Dispose();
            _hotkeySource = null;
        }

        GC.SuppressFinalize(this);
    }
}
