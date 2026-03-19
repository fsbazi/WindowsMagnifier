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

    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;

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
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void OnPoll(object? sender, System.Timers.ElapsedEventArgs e)
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

        // 手动重启（AutoReset = false，防止重入）
        _pollTimer?.Start();
    }

    /// <summary>
    /// 找到指定窗口全屏所在的显示器，如果不是全屏则返回 null
    /// </summary>
    private DisplayInfo? FindFullScreenDisplay(IntPtr hwnd, List<DisplayInfo> displays)
    {
        if (!IsWindowVisible(hwnd)) return null;
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
        if (!GetWindowRect(hwnd, out var rect)) return false;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        return CheckWindowMatchesDisplay(rect, width, height, hwnd, display);
    }

    /// <summary>
    /// 检查窗口的尺寸和位置是否匹配指定显示器（全屏无边框）
    /// </summary>
    private bool CheckWindowMatchesDisplay(RECT rect, int width, int height, IntPtr hwnd, DisplayInfo display)
    {
        var bounds = display.Bounds;

        // 窗口尺寸匹配屏幕（允许 2px 误差）
        bool sizeMatches = Math.Abs(width - bounds.Width) <= 2 &&
                           Math.Abs(height - bounds.Height) <= 2;

        // 窗口位置在屏幕原点（允许 2px 误差）
        bool positionMatches = Math.Abs(rect.Left - bounds.Left) <= 2 &&
                                Math.Abs(rect.Top - bounds.Top) <= 2;

        if (sizeMatches && positionMatches)
        {
            // 检查是否无边框（游戏/视频播放器特征）
            var style = GetWindowLong(hwnd, GWL_STYLE);
            bool isBorderless = (style & WS_CAPTION) == 0;

            return isBorderless;
        }

        return false;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
