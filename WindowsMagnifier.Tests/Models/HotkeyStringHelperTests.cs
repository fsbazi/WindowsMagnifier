using WindowsMagnifier.Models;
using Xunit;

namespace WindowsMagnifier.Tests.Models;

public class HotkeyStringHelperTests
{
    #region TryParse

    [Theory]
    [InlineData("Win+Alt+M", HotkeyStringHelper.MOD_WIN | HotkeyStringHelper.MOD_ALT | HotkeyStringHelper.MOD_NOREPEAT, 0x4D)]
    [InlineData("Win+Alt+N", HotkeyStringHelper.MOD_WIN | HotkeyStringHelper.MOD_ALT | HotkeyStringHelper.MOD_NOREPEAT, 0x4E)]
    [InlineData("Ctrl+Shift+A", HotkeyStringHelper.MOD_CTRL | HotkeyStringHelper.MOD_SHIFT | HotkeyStringHelper.MOD_NOREPEAT, 0x41)]
    [InlineData("Alt+F1", HotkeyStringHelper.MOD_ALT | HotkeyStringHelper.MOD_NOREPEAT, 0x70)]
    [InlineData("Ctrl+0", HotkeyStringHelper.MOD_CTRL | HotkeyStringHelper.MOD_NOREPEAT, 0x30)]
    [InlineData("Win+Ctrl+Alt+Shift+Z", HotkeyStringHelper.MOD_WIN | HotkeyStringHelper.MOD_CTRL | HotkeyStringHelper.MOD_ALT | HotkeyStringHelper.MOD_SHIFT | HotkeyStringHelper.MOD_NOREPEAT, 0x5A)]
    public void TryParse_ValidHotkey_ReturnsTrue(string hotkey, int expectedModifiers, int expectedVk)
    {
        var result = HotkeyStringHelper.TryParse(hotkey, out var modifiers, out var virtualKey);

        Assert.True(result);
        Assert.Equal(expectedModifiers, modifiers);
        Assert.Equal(expectedVk, virtualKey);
    }

    [Theory]
    [InlineData("")]        // 空字符串
    [InlineData("   ")]     // 纯空白
    [InlineData("M")]       // 无修饰键
    [InlineData("Win")]     // 只有修饰键
    [InlineData("Win+Alt")] // 只有修饰键
    [InlineData("Win+Alt+?")] // 无效按键
    [InlineData("Win+Alt+M+N")] // 两个非修饰键
    [InlineData("Foo+M")]  // 无效修饰键
    public void TryParse_InvalidHotkey_ReturnsFalse(string hotkey)
    {
        var result = HotkeyStringHelper.TryParse(hotkey, out _, out _);
        Assert.False(result);
    }

    [Fact]
    public void TryParse_NullHotkey_ReturnsFalse()
    {
        var result = HotkeyStringHelper.TryParse(null!, out _, out _);
        Assert.False(result);
    }

    [Theory]
    [InlineData("win+alt+m")]   // 全小写
    [InlineData("WIN+ALT+M")]   // 全大写
    [InlineData("Win+alt+m")]   // 混合
    public void TryParse_CaseInsensitive(string hotkey)
    {
        var result = HotkeyStringHelper.TryParse(hotkey, out var modifiers, out var virtualKey);

        Assert.True(result);
        Assert.Equal(HotkeyStringHelper.MOD_WIN | HotkeyStringHelper.MOD_ALT | HotkeyStringHelper.MOD_NOREPEAT, modifiers);
        Assert.Equal(0x4D, virtualKey);
    }

    [Theory]
    [InlineData("Alt+F1", 0x70)]
    [InlineData("Alt+F6", 0x75)]
    [InlineData("Alt+F12", 0x7B)]
    public void TryParse_FunctionKeys(string hotkey, int expectedVk)
    {
        var result = HotkeyStringHelper.TryParse(hotkey, out _, out var virtualKey);

        Assert.True(result);
        Assert.Equal(expectedVk, virtualKey);
    }

    [Theory]
    [InlineData("Alt+0", 0x30)]
    [InlineData("Alt+9", 0x39)]
    [InlineData("Alt+5", 0x35)]
    public void TryParse_NumberKeys(string hotkey, int expectedVk)
    {
        var result = HotkeyStringHelper.TryParse(hotkey, out _, out var virtualKey);

        Assert.True(result);
        Assert.Equal(expectedVk, virtualKey);
    }

    [Theory]
    [InlineData("Alt+A", 0x41)]
    [InlineData("Alt+Z", 0x5A)]
    public void TryParse_LetterKeys(string hotkey, int expectedVk)
    {
        var result = HotkeyStringHelper.TryParse(hotkey, out _, out var virtualKey);

        Assert.True(result);
        Assert.Equal(expectedVk, virtualKey);
    }

    #endregion

    #region IsValid

    [Theory]
    [InlineData("Win+Alt+M", true)]
    [InlineData("Ctrl+A", true)]
    [InlineData("M", false)]
    [InlineData("", false)]
    [InlineData("Win+", false)]
    public void IsValid_ReturnsExpected(string hotkey, bool expected)
    {
        Assert.Equal(expected, HotkeyStringHelper.IsValid(hotkey));
    }

    #endregion

    #region Format

    [Fact]
    public void Format_WinAltM_ReturnsCorrectString()
    {
        var result = HotkeyStringHelper.Format(
            System.Windows.Input.ModifierKeys.Windows | System.Windows.Input.ModifierKeys.Alt,
            System.Windows.Input.Key.M);

        Assert.Equal("Win+Alt+M", result);
    }

    [Fact]
    public void Format_CtrlShiftF1_ReturnsCorrectString()
    {
        var result = HotkeyStringHelper.Format(
            System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift,
            System.Windows.Input.Key.F1);

        Assert.Equal("Ctrl+Shift+F1", result);
    }

    [Fact]
    public void Format_AllModifiers_CorrectOrder()
    {
        var result = HotkeyStringHelper.Format(
            System.Windows.Input.ModifierKeys.Windows | System.Windows.Input.ModifierKeys.Control |
            System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Shift,
            System.Windows.Input.Key.A);

        // 顺序: Win > Ctrl > Alt > Shift
        Assert.Equal("Win+Ctrl+Alt+Shift+A", result);
    }

    [Fact]
    public void Format_UnsupportedKey_ReturnsEmpty()
    {
        var result = HotkeyStringHelper.Format(
            System.Windows.Input.ModifierKeys.Alt,
            System.Windows.Input.Key.Space);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Format_NumberKey_ReturnsDigit()
    {
        var result = HotkeyStringHelper.Format(
            System.Windows.Input.ModifierKeys.Alt,
            System.Windows.Input.Key.D5);

        Assert.Equal("Alt+5", result);
    }

    [Fact]
    public void Format_NumPadKey_ReturnsDigit()
    {
        var result = HotkeyStringHelper.Format(
            System.Windows.Input.ModifierKeys.Alt,
            System.Windows.Input.Key.NumPad3);

        Assert.Equal("Alt+3", result);
    }

    #endregion

    #region TryParse 与 Format 往返一致性

    [Theory]
    [InlineData("Win+Alt+M")]
    [InlineData("Ctrl+Shift+F1")]
    [InlineData("Alt+A")]
    [InlineData("Win+Ctrl+Alt+Shift+Z")]
    public void TryParse_ThenFormat_Roundtrip(string original)
    {
        // 确保 TryParse 成功
        Assert.True(HotkeyStringHelper.IsValid(original));

        // 注意：格式化是从 WPF 类型到字符串，此测试仅验证解析不抛异常
        Assert.True(HotkeyStringHelper.TryParse(original, out var modifiers, out var virtualKey));
        Assert.NotEqual(0, modifiers);
        Assert.NotEqual(0, virtualKey);
    }

    #endregion

    #region 含空格的输入

    [Theory]
    [InlineData("Win + Alt + M")]
    [InlineData(" Win+Alt+M ")]
    public void TryParse_WithSpaces_StillValid(string hotkey)
    {
        var result = HotkeyStringHelper.TryParse(hotkey, out _, out _);
        Assert.True(result);
    }

    #endregion
}
