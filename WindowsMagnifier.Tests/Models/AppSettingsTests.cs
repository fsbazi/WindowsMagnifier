using WindowsMagnifier.Models;
using Xunit;

namespace WindowsMagnifier.Tests.Models;

public class AppSettingsTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(16, 16)]
    [InlineData(17, 16)]
    [InlineData(int.MaxValue, 16)]
    [InlineData(int.MinValue, 1)]
    public void Sanitize_ClampsMagnificationLevel(int input, int expected)
    {
        var settings = new AppSettings { MagnificationLevel = input };
        settings.Sanitize();
        Assert.Equal(expected, settings.MagnificationLevel);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(0, 50)]
    [InlineData(50, 50)]
    [InlineData(1200, 1200)]
    [InlineData(9999, 1200)]
    [InlineData(300, 300)]
    public void Sanitize_ClampsWindowHeight(int input, int expected)
    {
        var settings = new AppSettings { WindowHeight = input };
        settings.Sanitize();
        Assert.Equal(expected, settings.WindowHeight);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5000, 5000)]
    [InlineData(6000, 5000)]
    [InlineData(100, 100)]
    public void Sanitize_ClampsDisplaySwitchDelay(int input, int expected)
    {
        var settings = new AppSettings { DisplaySwitchDelay = input };
        settings.Sanitize();
        Assert.Equal(expected, settings.DisplaySwitchDelay);
    }

    [Fact]
    public void Sanitize_ClampsDisplayMagnificationLevels()
    {
        var settings = new AppSettings
        {
            DisplayMagnificationLevels = new Dictionary<string, int>
            {
                { @"\\.\DISPLAY1", 0 },
                { @"\\.\DISPLAY2", 20 },
                { @"\\.\DISPLAY3", 5 }
            }
        };
        settings.Sanitize();
        Assert.Equal(1, settings.DisplayMagnificationLevels[@"\\.\DISPLAY1"]);
        Assert.Equal(16, settings.DisplayMagnificationLevels[@"\\.\DISPLAY2"]);
        Assert.Equal(5, settings.DisplayMagnificationLevels[@"\\.\DISPLAY3"]);
    }

    [Fact]
    public void Sanitize_NullDictionary_DoesNotThrow()
    {
        var settings = new AppSettings { DisplayMagnificationLevels = null };
        settings.Sanitize();
        Assert.Null(settings.DisplayMagnificationLevels);
    }

    [Fact]
    public void GetMagnificationLevel_AlwaysClampedTo1_16()
    {
        var settings = new AppSettings();
        settings.SetMagnificationLevel("test", 0);
        Assert.Equal(1, settings.GetMagnificationLevel("test"));

        settings.SetMagnificationLevel("test", 999);
        Assert.Equal(16, settings.GetMagnificationLevel("test"));
    }

    [Fact]
    public void GetMagnificationLevel_FallbackToDefault()
    {
        var settings = new AppSettings { MagnificationLevel = 5 };
        settings.Sanitize();
        Assert.Equal(5, settings.GetMagnificationLevel("nonexistent"));
    }

    [Fact]
    public void CreateDefault_ReturnsCorrectDefaults()
    {
        var settings = AppSettings.CreateDefault();
        Assert.Equal(3, settings.MagnificationLevel);
        Assert.Equal(300, settings.WindowHeight);
        Assert.True(settings.FollowMouse);
        Assert.True(settings.FollowKeyboardInput);
        Assert.False(settings.StartMinimized);
        Assert.True(settings.HideOnFullScreen);
        Assert.Equal(100, settings.DisplaySwitchDelay);
    }
}
