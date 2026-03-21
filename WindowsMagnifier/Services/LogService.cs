using System;
using System.IO;

namespace WindowsMagnifier.Services;

/// <summary>
/// 统一日志服务（线程安全单例）
/// - 使用 FileStream 独占锁写入，防止多线程竞态
/// - 使用文件轮转替代 ReadAllLines 截断，避免 OOM
/// - 写入前检查符号链接，防止路径劫持
/// </summary>
public sealed class LogService
{
    private static readonly Lazy<LogService> _instance = new(() => new LogService());
    public static LogService Instance => _instance.Value;

    private const long MaxLogSize = 1024 * 1024; // 1MB
    private readonly string _logDir;
    private readonly string _errorLogPath;
    private readonly string _debugLogPath;
    private readonly object _writeLock = new();

    private LogService()
    {
        _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsMagnifier");
        _errorLogPath = Path.Combine(_logDir, "error.log");
        _debugLogPath = Path.Combine(_logDir, "debug.log");
    }

    /// <summary>
    /// 获取 error.log 路径（供 UI 显示）
    /// </summary>
    public string ErrorLogPath => _errorLogPath;

    /// <summary>
    /// 写入 error.log（应用级日志）
    /// </summary>
    public void LogError(string message)
    {
        WriteLog(_errorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
    }

    /// <summary>
    /// 写入 debug.log（调试级日志）
    /// </summary>
    public void LogDebug(string message)
    {
        WriteLog(_debugLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    /// <summary>
    /// 写入 debug.log 并附加来源标签
    /// </summary>
    public void LogDebug(string tag, string message)
    {
        WriteLog(_debugLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] {message}");
    }

    private void WriteLog(string logPath, string formattedMessage)
    {
        lock (_writeLock)
        {
            try
            {
                EnsureDirectory();

                // 安全检查：拒绝写入符号链接目标（防止路径劫持攻击）
                if (IsSymbolicLink(logPath))
                    return;

                // 文件轮转：超过 MaxLogSize 时重命名为 .bak 并创建新文件
                RotateIfNeeded(logPath);

                // 使用 FileStream 以独占写锁追加
                using var stream = new FileStream(
                    logPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read, // 允许其他进程读取，但不允许同时写入
                    bufferSize: 4096,
                    FileOptions.None);

                using var writer = new StreamWriter(stream);
                writer.WriteLine(formattedMessage);
            }
            catch
            {
                // 日志写入失败不应影响应用运行
            }
        }
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_logDir))
            Directory.CreateDirectory(_logDir);
    }

    /// <summary>
    /// 检查路径是否为符号链接/重解析点
    /// </summary>
    private static bool IsSymbolicLink(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            // 无法检查属性时保守拒绝
            return true;
        }
    }

    /// <summary>
    /// 文件轮转：超过阈值时将当前文件重命名为 .bak，创建空的新文件。
    /// 避免 File.ReadAllLines 将整个大文件读入内存的 OOM 风险。
    /// </summary>
    private static void RotateIfNeeded(string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
                return;

            var info = new FileInfo(logPath);
            if (info.Length <= MaxLogSize)
                return;

            var bakPath = logPath + ".bak";

            // 删除旧的 .bak（如果存在）
            if (File.Exists(bakPath))
                File.Delete(bakPath);

            // 当前日志重命名为 .bak
            File.Move(logPath, bakPath);

            // 新日志文件会由后续的 FileStream Append 自动创建
        }
        catch
        {
            // 轮转失败不阻止写入
        }
    }
}
