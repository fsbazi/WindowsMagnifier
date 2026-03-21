using System;
using System.Collections.Generic;

namespace WindowsMagnifier.Models;

/// <summary>
/// 应用配置数据模型
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 保护 DisplayMagnificationLevels 字典的并发访问锁
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 全局默认放大倍数 (1-16)，向后兼容旧配置
    /// </summary>
    public int MagnificationLevel { get; set; } = 3;

    /// <summary>
    /// 每显示器独立的放大倍数，key 为 DeviceName
    /// </summary>
    public Dictionary<string, int>? DisplayMagnificationLevels { get; set; }

    /// <summary>
    /// 放大镜窗口高度（像素）
    /// </summary>
    public int WindowHeight { get; set; } = 300;

    /// <summary>
    /// 是否跟随鼠标指针
    /// </summary>
    public bool FollowMouse { get; set; } = true;

    /// <summary>
    /// 是否跟随键盘输入
    /// </summary>
    public bool FollowKeyboardInput { get; set; } = true;

    /// <summary>
    /// 启动后是否最小化
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// 全屏应用时自动隐藏放大镜
    /// </summary>
    public bool HideOnFullScreen { get; set; } = true;

    /// <summary>
    /// 显示器切换延迟（毫秒）
    /// </summary>
    public int DisplaySwitchDelay { get; set; } = 100;

    /// <summary>
    /// 全局切换快捷键（默认: Win+Alt+M）
    /// </summary>
    public string ToggleAllHotkey { get; set; } = "Win+Alt+M";

    /// <summary>
    /// 当前屏幕切换快捷键（默认: Win+Alt+N）
    /// </summary>
    public string ToggleCurrentHotkey { get; set; } = "Win+Alt+N";

    /// <summary>
    /// 获取指定显示器的放大倍数，找不到则返回全局默认值。
    /// 返回值始终 Clamp 到 [1, 16]，防止除零或溢出。
    /// </summary>
    public int GetMagnificationLevel(string deviceName)
    {
        int raw;
        lock (_lock)
        {
            if (DisplayMagnificationLevels != null &&
                DisplayMagnificationLevels.TryGetValue(deviceName, out var level))
            {
                raw = level;
            }
            else
            {
                raw = MagnificationLevel;
            }
        }
        return Math.Clamp(raw, 1, 16);
    }

    /// <summary>
    /// 设置指定显示器的放大倍数
    /// </summary>
    public void SetMagnificationLevel(string deviceName, int level)
    {
        lock (_lock)
        {
            DisplayMagnificationLevels ??= new Dictionary<string, int>();
            DisplayMagnificationLevels[deviceName] = Math.Clamp(level, 1, 16);
        }
    }

    /// <summary>
    /// 校验并修正所有数值字段到合法范围，防止手动编辑 config.json 导致异常。
    /// 应在 ConfigService.Load 后立即调用。
    /// </summary>
    public void Sanitize()
    {
        MagnificationLevel = Math.Clamp(MagnificationLevel, 1, 16);
        WindowHeight = Math.Clamp(WindowHeight, 50, 1200);
        DisplaySwitchDelay = Math.Clamp(DisplaySwitchDelay, 1, 5000);

        lock (_lock)
        {
            if (DisplayMagnificationLevels != null)
            {
                var keys = new List<string>(DisplayMagnificationLevels.Keys);
                foreach (var key in keys)
                {
                    DisplayMagnificationLevels[key] = Math.Clamp(DisplayMagnificationLevels[key], 1, 16);
                }
            }
        }

        // 校验快捷键格式，无效时恢复默认值
        if (string.IsNullOrWhiteSpace(ToggleAllHotkey) || !HotkeyStringHelper.IsValid(ToggleAllHotkey))
            ToggleAllHotkey = "Win+Alt+M";
        if (string.IsNullOrWhiteSpace(ToggleCurrentHotkey) || !HotkeyStringHelper.IsValid(ToggleCurrentHotkey))
            ToggleCurrentHotkey = "Win+Alt+N";
    }

    /// <summary>
    /// 返回默认配置
    /// </summary>
    public static AppSettings CreateDefault() => new AppSettings();
}
