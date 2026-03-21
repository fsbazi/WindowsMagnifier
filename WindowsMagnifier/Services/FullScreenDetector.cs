using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using WindowsMagnifier.Models;

namespace WindowsMagnifier.Services;

/// <summary>
/// 全屏应用检测服务 - 轮询检测全屏窗口，按显示器独立跟踪
/// </summary>
public class FullScreenDetector : IDisposable
{
    private readonly DisplayManager _displayManager;
    private readonly int _pollIntervalMs;
    private System.Timers.Timer? _pollTimer;

    /// <summary>
    /// 每个显示器上当前的全屏窗口句柄（key = DeviceName）
    /// </summary>
    private readonly Dictionary<string, IntPtr> _fullScreenWindows = new();
    private readonly object _lock = new();
    private volatile bool _stopped;

    /// <summary>
    /// 当前是否有任一显示器处于全屏状态
    /// </summary>
    public bool IsFullScreenActive
    {
        get
        {
            lock (_lock)
            {
                return _fullScreenWindows.Count > 0;
            }
        }
    }

    /// <summary>
    /// 获取当前全屏的显示器设备名称集合（快照）
    /// </summary>
    public HashSet<string> GetFullScreenDisplays()
    {
        lock (_lock)
        {
            return new HashSet<string>(_fullScreenWindows.Keys);
        }
    }

    /// <summary>
    /// 单个显示器全屏状态变化事件（displayDeviceName, isFullScreen）
    /// </summary>
    public event Action<string, bool>? DisplayFullScreenStateChanged;

    #region P/Invoke

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_MAXIMIZE = 0x01000000;
    private const int DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    #endregion

    public FullScreenDetector(DisplayManager displayManager, int pollIntervalMs = 200)
    {
        _displayManager = displayManager;
        _pollIntervalMs = pollIntervalMs;
    }

    /// <summary>
    /// 启动检测
    /// </summary>
    public void Start()
    {
        if (_pollTimer != null) return;
        _stopped = false;

        _pollTimer = new System.Timers.Timer(_pollIntervalMs);
        _pollTimer.AutoReset = false; // 防止重入
        _pollTimer.Elapsed += OnPoll;
        _pollTimer.Start();
    }

    /// <summary>
    /// 停止检测
    /// </summary>
    public void Stop()
    {
        _stopped = true;
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void OnPoll(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            var displays = _displayManager.GetDisplays();
            var changes = new List<(string displayName, bool isFullScreen)>();

            lock (_lock)
            {
                // 1. 检查前台窗口是否是全屏的
                var fgHwnd = GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    var fgDisplay = FindFullScreenDisplay(fgHwnd, displays);
                    if (fgDisplay != null && !_fullScreenWindows.ContainsKey(fgDisplay.DeviceName))
                    {
                        // 前台窗口在某个显示器上全屏，加入跟踪
                        _fullScreenWindows[fgDisplay.DeviceName] = fgHwnd;
                        changes.Add((fgDisplay.DeviceName, true));
                    }
                    else if (fgDisplay != null && _fullScreenWindows.ContainsKey(fgDisplay.DeviceName)
                             && _fullScreenWindows[fgDisplay.DeviceName] != fgHwnd)
                    {
                        // 同一显示器上换了一个全屏窗口，更新句柄（不触发事件，仍然是全屏）
                        _fullScreenWindows[fgDisplay.DeviceName] = fgHwnd;
                    }
                }

                // 2. 重新验证已跟踪的全屏窗口是否仍然全屏
                var keysToRemove = new List<string>();
                foreach (var kvp in _fullScreenWindows)
                {
                    var displayName = kvp.Key;
                    var hwnd = kvp.Value;

                    // 如果这个窗口刚刚在上面的步骤中被加入或更新过，跳过检查
                    // （因为已确认它是全屏的）
                    if (changes.Any(c => c.displayName == displayName && c.isFullScreen))
                        continue;

                    var display = displays.FirstOrDefault(d => d.DeviceName == displayName);
                    if (display == null || !CheckFullScreenForDisplay(hwnd, display))
                    {
                        keysToRemove.Add(displayName);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _fullScreenWindows.Remove(key);
                    changes.Add((key, false));
                }
            }

            // 3. 触发变化事件
            if (changes.Count > 0)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    foreach (var (displayName, isFullScreen) in changes)
                    {
                        DisplayFullScreenStateChanged?.Invoke(displayName, isFullScreen);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FullScreenDetector] OnPoll exception: {ex.Message}");
        }
        finally
        {
            // 无论是否异常，始终重启 timer（AutoReset=false 防重入）
            // 确保全屏检测不会因异常而永久停止
            if (!_stopped) _pollTimer?.Start();
        }
    }

    /// <summary>
    /// 找到指定窗口全屏所在的显示器，如果不是全屏则返回 null
    /// </summary>
    private DisplayInfo? FindFullScreenDisplay(IntPtr hwnd, List<DisplayInfo> displays)
    {
        if (!IsWindowVisible(hwnd)) return null;
        // 排除虚拟桌面上被 cloaked 的窗口（在其他桌面上不可见但 IsWindowVisible 仍返回 TRUE）
        if (IsWindowCloaked(hwnd)) return null;
        if (!GetWindowRect(hwnd, out var rect)) return null;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        foreach (var display in displays)
        {
            if (CheckWindowMatchesDisplay(rect, width, height, hwnd, display))
            {
                return display;
            }
        }

        return null;
    }

    /// <summary>
    /// 检查指定窗口是否在指定显示器上全屏
    /// </summary>
    private bool CheckFullScreenForDisplay(IntPtr hwnd, DisplayInfo display)
    {
        if (!IsWindowVisible(hwnd)) return false;
        if (IsWindowCloaked(hwnd)) return false;
        if (!GetWindowRect(hwnd, out var rect)) return false;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        return CheckWindowMatchesDisplay(rect, width, height, hwnd, display);
    }

    /// <summary>
    /// 检查窗口的尺寸和位置是否匹配指定显示器（全屏检测）。
    /// 无边框窗口精确匹配屏幕尺寸即判定为全屏；
    /// 有边框窗口（如 Chrome F11、VLC）允许窗口覆盖或超出屏幕边界也判定为全屏。
    /// </summary>
    private bool CheckWindowMatchesDisplay(RECT rect, int width, int height, IntPtr hwnd, DisplayInfo display)
    {
        var bounds = display.Bounds;
        var style = GetWindowLong(hwnd, GWL_STYLE);
        bool hasCaptionStyle = (style & WS_CAPTION) != 0;

        // 严格匹配：窗口尺寸和位置精确覆盖屏幕（允许 2px 误差）
        bool sizeMatchesExact = Math.Abs(width - bounds.Width) <= 2 &&
                                Math.Abs(height - bounds.Height) <= 2;
        bool positionMatchesExact = Math.Abs(rect.Left - bounds.Left) <= 2 &&
                                     Math.Abs(rect.Top - bounds.Top) <= 2;

        if (sizeMatchesExact && positionMatchesExact)
        {
            // 尺寸位置精确匹配，无论有无边框都判定为全屏
            return true;
        }

        if (hasCaptionStyle)
        {
            // 排除最大化窗口（有 WS_CAPTION + WS_MAXIMIZE 的是普通最大化，非全屏）
            bool isMaximized = (style & WS_MAXIMIZE) != 0;
            if (isMaximized) return false;

            // 有边框的全屏窗口（如 Chrome F11）可能比屏幕稍大（负偏移隐藏边框），
            // 检查窗口是否覆盖了整个屏幕区域
            bool coversScreen = rect.Left <= (int)bounds.Left &&
                                rect.Top <= (int)bounds.Top &&
                                rect.Right >= (int)bounds.Right &&
                                rect.Bottom >= (int)bounds.Bottom;
            // 但窗口不应比屏幕大太多（超过 20px 说明不是全屏，排除普通最大化窗口）
            bool notTooLarge = width <= bounds.Width + 20 &&
                               height <= bounds.Height + 20;
            return coversScreen && notTooLarge;
        }

        return false;
    }

    /// <summary>
    /// 检查窗口是否被 cloaked（在其他虚拟桌面上不可见）
    /// </summary>
    private static bool IsWindowCloaked(IntPtr hwnd)
    {
        var hr = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
