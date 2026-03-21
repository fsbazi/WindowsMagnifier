using System;
using System.IO;
using WindowsMagnifier.Models;
using WindowsMagnifier.Services;
using Xunit;

namespace WindowsMagnifier.Tests.Services;

public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempConfigPath;
    private readonly ConfigService _service;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "EyemoTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempConfigPath = Path.Combine(_tempDir, "config.json");
        _service = new ConfigService(_tempConfigPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Load_FileNotExists_ReturnsDefault()
    {
        var settings = _service.Load();
        Assert.Equal(3, settings.MagnificationLevel);
        Assert.Equal(300, settings.WindowHeight);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_ValuesMatch()
    {
        var original = new AppSettings { MagnificationLevel = 8, WindowHeight = 500 };
        _service.Save(original);
        var loaded = _service.Load();
        Assert.Equal(8, loaded.MagnificationLevel);
        Assert.Equal(500, loaded.WindowHeight);
    }

    [Fact]
    public void Load_CorruptedJson_ReturnsDefault()
    {
        File.WriteAllText(_tempConfigPath, "not valid json {{{");
        var settings = _service.Load();
        Assert.Equal(3, settings.MagnificationLevel);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsDefault()
    {
        File.WriteAllText(_tempConfigPath, "");
        var settings = _service.Load();
        Assert.Equal(3, settings.MagnificationLevel);
    }

    [Fact]
    public void Load_IllegalValues_SanitizedAfterLoad()
    {
        File.WriteAllText(_tempConfigPath, @"{""MagnificationLevel"": 0, ""WindowHeight"": -5}");
        var settings = _service.Load();
        Assert.Equal(1, settings.MagnificationLevel);
        Assert.Equal(50, settings.WindowHeight);
    }

    [Fact]
    public void Load_PartialJson_MissingFieldsUseDefaults()
    {
        File.WriteAllText(_tempConfigPath, @"{""MagnificationLevel"": 7}");
        var settings = _service.Load();
        Assert.Equal(7, settings.MagnificationLevel);
        Assert.Equal(300, settings.WindowHeight);
    }

    [Fact]
    public void SaveAndLoad_WithDisplayMagnificationLevels_PreservesValues()
    {
        var original = new AppSettings();
        original.SetMagnificationLevel(@"\\.\DISPLAY1", 5);
        original.SetMagnificationLevel(@"\\.\DISPLAY2", 10);
        _service.Save(original);

        var loaded = _service.Load();
        Assert.Equal(5, loaded.GetMagnificationLevel(@"\\.\DISPLAY1"));
        Assert.Equal(10, loaded.GetMagnificationLevel(@"\\.\DISPLAY2"));
    }
}
