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
}
