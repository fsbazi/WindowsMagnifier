using System;
using System.Linq;
using System.Windows;
using WindowsMagnifier.Models;

namespace WindowsMagnifier.Services;

/// <summary>
/// 显示器焦点管理器 - 管理活动/非活动显示器状态
/// </summary>
public class DisplayFocusManager : IDisposable
{
    private readonly DisplayManager _displayManager;
    private readonly int _switchDelayMs;

    private DisplayInfo? _activeDisplay;
    private readonly System.Timers.Timer _switchTimer;
    private DisplayInfo? _pendingDisplay;

    /// <summary>
    /// 当前活动的显示器
    /// </summary>
    public DisplayInfo? ActiveDisplay => _activeDisplay;

    /// <summary>
    /// 活动显示器变化事件
    /// </summary>
    public event Action<DisplayInfo?>? ActiveDisplayChanged;

    public DisplayFocusManager(DisplayManager displayManager, int switchDelayMs = 100)
    {
        _displayManager = displayManager;
        _switchDelayMs = switchDelayMs;
        _switchTimer = new System.Timers.Timer(switchDelayMs);
        _switchTimer.AutoReset = false;
        _switchTimer.Elapsed += OnSwitchTimerElapsed;
    }

    /// <summary>
    /// 根据鼠标位置更新活动显示器
    /// </summary>
    public void UpdateFromMousePosition(Point mousePosition)
    {
        var display = _displayManager.GetDisplayFromPoint(mousePosition);

        if (display == null) return;

        // 如果鼠标在同一个显示器上，不做处理
        if (_activeDisplay != null && _activeDisplay.DeviceName == display.DeviceName)
        {
            _pendingDisplay = null;
            _switchTimer.Stop();
            return;
        }

        // 如果已经有切换计时器在运行，检查是否是同一个待切换显示器
        if (_switchTimer.Enabled)
        {
            if (_pendingDisplay?.DeviceName == display.DeviceName)
                return;
        }

        // 开始延迟切换
        _pendingDisplay = display;
        StartSwitchTimer();
    }

    private void StartSwitchTimer()
    {
        _switchTimer.Stop();
        _switchTimer.Start();
    }

    private void OnSwitchTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_pendingDisplay == null) return;

        _activeDisplay = _pendingDisplay;
        _pendingDisplay = null;

        // 在 UI 线程上触发事件
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            ActiveDisplayChanged?.Invoke(_activeDisplay);
        });
    }

    /// <summary>
    /// 初始化，设置默认活动显示器
    /// </summary>
    public void Initialize()
    {
        var displays = _displayManager.GetDisplays();
        _activeDisplay = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
    }

    public void Dispose()
    {
        _switchTimer.Stop();
        _switchTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
