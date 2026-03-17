using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace WindowsMagnifier.Services;

/// <summary>
/// 全屏应用检测服务 - 轮询检测全屏窗口
/// </summary>
public class FullScreenDetector : IDisposable
{
    private readonly DisplayManager _displayManager;
    private readonly int _pollIntervalMs;
    private System.Timers.Timer? _pollTimer;
    private bool _isFullScreenActive;

    /// <summary>
    /// 当前是否检测到全屏应用
    /// </summary>
    public bool IsFullScreenActive => _isFullScreenActive;

    /// <summary>
    /// 全屏状态变化事件
    /// </summary>
    public event Action<bool>? FullScreenStateChanged;

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
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        bool isFullScreen = CheckFullScreen(hwnd);

        if (isFullScreen != _isFullScreenActive)
        {
            _isFullScreenActive = isFullScreen;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                FullScreenStateChanged?.Invoke(isFullScreen);
            });
        }
    }

    private bool CheckFullScreen(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd)) return false;
        if (!GetWindowRect(hwnd, out var rect)) return false;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        var displays = _displayManager.GetDisplays();

        foreach (var display in displays)
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

                if (isBorderless) return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}