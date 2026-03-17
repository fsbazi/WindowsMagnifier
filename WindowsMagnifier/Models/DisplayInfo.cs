using System.Windows;

namespace WindowsMagnifier.Models;

/// <summary>
/// 显示器信息模型
/// </summary>
public class DisplayInfo
{
    /// <summary>
    /// 显示器设备名称
    /// </summary>
    public string DeviceName { get; }

    /// <summary>
    /// 显示器边界（屏幕坐标）
    /// </summary>
    public Rect Bounds { get; }

    /// <summary>
    /// 是否为主显示器
    /// </summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// 显示器索引
    /// </summary>
    public int Index { get; }

    public DisplayInfo(string deviceName, Rect bounds, bool isPrimary, int index)
    {
        DeviceName = deviceName;
        Bounds = bounds;
        IsPrimary = isPrimary;
        Index = index;
    }

    /// <summary>
    /// 获取放大镜窗口应停靠的位置（显示器顶部）
    /// </summary>
    public Rect GetMagnifierWindowRect(int windowHeight)
    {
        return new Rect(
            Bounds.X,
            Bounds.Y,
            Bounds.Width,
            windowHeight
        );
    }
}
