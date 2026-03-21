using System;
using System.IO;
using System.Text.Json;
using WindowsMagnifier.Models;

namespace WindowsMagnifier.Services;

/// <summary>
/// 配置持久化服务
/// </summary>
public class ConfigService
{
    private static readonly string DefaultConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowsMagnifier",
        "config.json"
    );

    private readonly string _configPath;
    private readonly object _saveLock = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 使用默认 AppData 路径
    /// </summary>
    public ConfigService() : this(DefaultConfigPath) { }

    /// <summary>
    /// 使用自定义配置文件路径（用于测试）
    /// </summary>
    internal ConfigService(string configPath)
    {
        _configPath = configPath;
    }

    /// <summary>
    /// 加载配置，如果不存在则返回默认配置
    /// </summary>
    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            if (!File.Exists(_configPath))
            {
                return AppSettings.CreateDefault();
            }

            var json = File.ReadAllText(_configPath);
            settings = JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.CreateDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Config] Load error: {ex.Message}");
            settings = AppSettings.CreateDefault();
        }

        // 校验所有数值字段，防止手动编辑 config.json 导致非法值
        settings.Sanitize();
        return settings;
    }

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    public void Save(AppSettings settings)
    {
        lock (_saveLock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                var tempPath = _configPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _configPath, overwrite: true);
            }
            catch (Exception ex)
            {
                LogService.Instance.LogError($"[Config] Save error: {ex.Message}");
            }
        }
    }
}
