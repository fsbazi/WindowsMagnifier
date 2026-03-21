using WindowsMagnifier.Services;
using Xunit;

namespace WindowsMagnifier.Tests.Services;

public class FullScreenDetectorTests
{
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_MAXIMIZE = 0x01000000;

    [Fact]
    public void IsFullScreenMatch_ExactMatch_ReturnsTrue()
    {
        // 无边框窗口精确匹配屏幕
        Assert.True(FullScreenDetector.IsFullScreenMatch(
            0, 0, 1920, 1080, 0, 0, 0, 1920, 1080));
    }

    [Fact]
    public void IsFullScreenMatch_CaptionCoversScreen_ReturnsTrue()
    {
        // Chrome F11：有 WS_CAPTION，窗口比屏幕稍大（负偏移隐藏边框）
        Assert.True(FullScreenDetector.IsFullScreenMatch(
            -8, -8, 1928, 1088, WS_CAPTION, 0, 0, 1920, 1080));
    }

    [Fact]
    public void IsFullScreenMatch_MaximizedWindow_ReturnsFalse()
    {
        // 最大化窗口：有 WS_CAPTION | WS_MAXIMIZE
        Assert.False(FullScreenDetector.IsFullScreenMatch(
            -7, -7, 1927, 1087, WS_CAPTION | WS_MAXIMIZE, 0, 0, 1920, 1080));
    }

    [Fact]
    public void IsFullScreenMatch_SmallWindow_ReturnsFalse()
    {
        Assert.False(FullScreenDetector.IsFullScreenMatch(
            100, 100, 800, 600, 0, 0, 0, 1920, 1080));
    }

    [Fact]
    public void IsFullScreenMatch_WithinTolerance_ReturnsTrue()
    {
        // 2px 误差内仍匹配
        Assert.True(FullScreenDetector.IsFullScreenMatch(
            1, 1, 1919, 1079, 0, 0, 0, 1920, 1080));
    }

    [Fact]
    public void IsFullScreenMatch_TooLarge_ReturnsFalse()
    {
        // 有 WS_CAPTION 但超过屏幕 20px 以上
        Assert.False(FullScreenDetector.IsFullScreenMatch(
            -30, -30, 1960, 1120, WS_CAPTION, 0, 0, 1920, 1080));
    }
}
