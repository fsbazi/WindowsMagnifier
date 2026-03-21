using System;
using System.Collections.Generic;

namespace WindowsMagnifier.Models;

/// <summary>
/// 快捷键字符串的解析与校验工具。
/// 格式示例: "Win+Alt+M", "Ctrl+Shift+F1"
/// </summary>
public static class HotkeyStringHelper
{
    // Win32 修饰键标志
    public const int MOD_ALT = 0x0001;
    public const int MOD_CTRL = 0x0002;
    public const int MOD_SHIFT = 0x0004;
    public const int MOD_WIN = 0x0008;
    public const int MOD_NOREPEAT = 0x4000;

    /// <summary>
    /// 支持的修饰键名称到 Win32 标志的映射
    /// </summary>
    private static readonly Dictionary<string, int> ModifierMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Win", MOD_WIN },
        { "Alt", MOD_ALT },
        { "Ctrl", MOD_CTRL },
        { "Shift", MOD_SHIFT },
    };

    /// <summary>
    /// 支持的按键名称到 Win32 虚拟键码的映射
    /// </summary>
    private static readonly Dictionary<string, int> KeyMap = BuildKeyMap();

    private static Dictionary<string, int> BuildKeyMap()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // A-Z: VK_A(0x41) - VK_Z(0x5A)
        for (char c = 'A'; c <= 'Z'; c++)
            map[c.ToString()] = c; // ASCII 与 VK 码一致

        // 0-9: VK_0(0x30) - VK_9(0x39)
        for (char c = '0'; c <= '9'; c++)
            map[c.ToString()] = c;

        // F1-F12: VK_F1(0x70) - VK_F12(0x7B)
        for (int i = 1; i <= 12; i++)
            map[$"F{i}"] = 0x6F + i; // F1=0x70

        return map;
    }

    /// <summary>
    /// 校验快捷键字符串是否合法（至少一个修饰键 + 一个按键）
    /// </summary>
    public static bool IsValid(string hotkey)
    {
        return TryParse(hotkey, out _, out _);
    }

    /// <summary>
    /// 解析快捷键字符串为 Win32 修饰键标志和虚拟键码。
    /// 自动添加 MOD_NOREPEAT 标志。
    /// </summary>
    public static bool TryParse(string hotkey, out int modifiers, out int virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(hotkey))
            return false;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        bool hasModifier = false;
        bool hasKey = false;

        foreach (var part in parts)
        {
            if (ModifierMap.TryGetValue(part, out var mod))
            {
                modifiers |= mod;
                hasModifier = true;
            }
            else if (KeyMap.TryGetValue(part, out var vk))
            {
                if (hasKey) return false; // 不支持多个非修饰键
                virtualKey = vk;
                hasKey = true;
            }
            else
            {
                return false; // 未识别的部分
            }
        }

        if (hasModifier && hasKey)
        {
            modifiers |= MOD_NOREPEAT;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 将 WPF 的 ModifierKeys + Key 格式化为标准字符串，如 "Win+Alt+M"。
    /// 修饰键顺序: Win > Ctrl > Alt > Shift
    /// </summary>
    public static string Format(System.Windows.Input.ModifierKeys modifiers, System.Windows.Input.Key key)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows))
            parts.Add("Win");
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
            parts.Add("Shift");

        var keyName = ConvertKeyToName(key);
        if (keyName == null)
            return string.Empty;

        parts.Add(keyName);
        return string.Join("+", parts);
    }

    /// <summary>
    /// 将 WPF Key 转为显示名称（A-Z, 0-9, F1-F12），不支持的返回 null
    /// </summary>
    private static string? ConvertKeyToName(System.Windows.Input.Key key)
    {
        // A-Z
        if (key >= System.Windows.Input.Key.A && key <= System.Windows.Input.Key.Z)
            return key.ToString();

        // 0-9（主键盘区）
        if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
            return ((char)('0' + (key - System.Windows.Input.Key.D0))).ToString();

        // 0-9（数字小键盘）
        if (key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9)
            return ((char)('0' + (key - System.Windows.Input.Key.NumPad0))).ToString();

        // F1-F12
        if (key >= System.Windows.Input.Key.F1 && key <= System.Windows.Input.Key.F12)
            return $"F{1 + (key - System.Windows.Input.Key.F1)}";

        return null;
    }
}
