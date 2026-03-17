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
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowsMagnifier",
        "config.json"
    );

    private readonly object _saveLock = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 加载配置，如果不存在则返回默认配置
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return AppSettings.CreateDefault();
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.CreateDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Config] Load error: {ex.Message}");
            return AppSettings.CreateDefault();
        }
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
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Config] Save error: {ex.Message}");
            }
        }
    }
}
