using WindowsMagnifier.Models;
using System.Windows;
using Xunit;

namespace WindowsMagnifier.Tests.Models;

public class DisplayInfoTests
{
    [Fact]
    public void GetMagnifierWindowRect_ReturnsCorrectRect()
    {
        var display = new DisplayInfo(@"\\.\DISPLAY1",
            new Rect(100, 200, 1920, 1080), true, 0);
        var result = display.GetMagnifierWindowRect(300);
        Assert.Equal(100, result.X);
        Assert.Equal(200, result.Y);
        Assert.Equal(1920, result.Width);
        Assert.Equal(300, result.Height);
    }

    [Fact]
    public void GetMagnifierWindowRect_NegativeCoords_ReturnsCorrect()
    {
        var display = new DisplayInfo(@"\\.\DISPLAY2",
            new Rect(-1920, 0, 1920, 1080), false, 1);
        var result = display.GetMagnifierWindowRect(300);
        Assert.Equal(-1920, result.X);
        Assert.Equal(0, result.Y);
        Assert.Equal(1920, result.Width);
        Assert.Equal(300, result.Height);
    }

    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var display = new DisplayInfo(@"\\.\DISPLAY1",
            new Rect(0, 0, 1920, 1080), true, 0);
        Assert.Equal(@"\\.\DISPLAY1", display.DeviceName);
        Assert.True(display.IsPrimary);
        Assert.Equal(0, display.Index);
        Assert.Equal(new Rect(0, 0, 1920, 1080), display.Bounds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(2000)]
    public void GetMagnifierWindowRect_VariousHeights(int height)
    {
        var display = new DisplayInfo(@"\\.\DISPLAY1",
            new Rect(0, 0, 1920, 1080), true, 0);
        var result = display.GetMagnifierWindowRect(height);
        Assert.Equal(height, result.Height);
        Assert.Equal(1920, result.Width);
    }
}
