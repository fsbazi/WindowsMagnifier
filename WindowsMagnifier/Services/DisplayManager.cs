using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using WindowsMagnifier.Models;

namespace WindowsMagnifier.Services;

/// <summary>
/// 多显示器管理服务
/// </summary>
public class DisplayManager : IDisposable
{
    private List<DisplayInfo>? _cachedDisplays;

    /// <summary>
    /// 获取所有显示器信息（带缓存，显示器变化时自动刷新）
    /// </summary>
    public List<DisplayInfo> GetDisplays()
    {
        if (_cachedDisplays != null)
            return _cachedDisplays;

        var displays = new List<DisplayInfo>();
        var screens = Screen.AllScreens;

        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            displays.Add(new DisplayInfo(
                screen.DeviceName,
                new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height),
                screen.Primary,
                i
            ));
        }

        _cachedDisplays = displays;
        return displays;
    }

    /// <summary>
    /// 根据坐标判断鼠标在哪个显示器上
    /// </summary>
    public DisplayInfo? GetDisplayFromPoint(Point point)
    {
        var displays = GetDisplays();

        foreach (var display in displays)
        {
            if (display.Bounds.Contains(point))
            {
                return display;
            }
        }

        return displays.FirstOrDefault();
    }

    /// <summary>
    /// 监听显示器变化事件
    /// </summary>
    public event Action? DisplaysChanged;

    public DisplayManager()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _cachedDisplays = null; // 清除缓存，下次获取时重新读取
        DisplaysChanged?.Invoke();
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        GC.SuppressFinalize(this);
    }
}
