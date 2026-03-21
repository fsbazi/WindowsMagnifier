using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using WindowsMagnifier.Models;
using WindowsMagnifier.Services;

namespace WindowsMagnifier;

public partial class App : System.Windows.Application
{
    private const string MutexName = "WindowsMagnifier_SingleInstance_B7F3A2E1";
    private static readonly LogService _log = LogService.Instance;

    private Mutex? _mutex;
    private ConfigService? _configService;
    private AppSettings? _settings;
    private DisplayManager? _displayManager;
    private DisplayFocusManager? _displayFocusManager;
    private TrackingManager? _trackingManager;
    private FullScreenDetector? _fullScreenDetector;
    private HotkeyService? _hotkeyService;
    private readonly List<MainWindow> _magnifierWindows = new();
    /// <summary>
    /// 因全屏而被隐藏的显示器设备名集合（与用户手动隐藏区分）
    /// </summary>
    private readonly HashSet<string> _fullScreenHiddenDisplays = new();
    private readonly string _logPath = LogService.Instance.ErrorLogPath;
    private bool _magInitialized;
    private bool _windowsVisible = true;
    private volatile bool _wasKeyboardMode;

    // 异常计数器：5 秒内达到 3 次未处理异常则强制退出
    private int _unhandledExceptionCount;
    private long _firstExceptionTicks;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // 单实例检查
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("眼眸已在运行中。", "眼眸", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 设置全局异常处理
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += OnThreadException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _log.LogDebug("Application starting...");

            // 初始化配置
            _configService = new ConfigService();
            _settings = _configService.Load();
            _log.LogDebug("Config loaded");

            // 初始化显示器管理
            _displayManager = new DisplayManager();
            _displayManager.DisplaysChanged += OnDisplaysChanged;
            _log.LogDebug($"DisplayManager created, displays: {_displayManager.GetDisplays().Count}");

            // 初始化焦点管理
            _displayFocusManager = new DisplayFocusManager(_displayManager, _settings.DisplaySwitchDelay);
            _displayFocusManager.ActiveDisplayChanged += OnActiveDisplayChanged;
            _displayFocusManager.Initialize();
            _log.LogDebug("DisplayFocusManager initialized");

            // 初始化跟踪管理
            _trackingManager = new TrackingManager(_settings);
            _trackingManager.PositionChanged += OnPositionChanged;
            _trackingManager.Start();
            _log.LogDebug("TrackingManager started");

            // 初始化全屏检测（如果启用）
            if (_settings.HideOnFullScreen)
            {
                _fullScreenDetector = new FullScreenDetector(_displayManager);
                _fullScreenDetector.DisplayFullScreenStateChanged += OnDisplayFullScreenStateChanged;
                _fullScreenDetector.Start();
                _log.LogDebug("FullScreenDetector started");
            }

            // 初始化 Magnification API（应用级别，所有窗口共享）
            _magInitialized = MagnificationApi.MagInitialize();
            if (!_magInitialized)
            {
                _log.LogError($"MagInitialize failed, error: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                _log.LogDebug("MagInitialize succeeded");
            }

            // 创建放大镜窗口
            CreateMagnifierWindows();
            _log.LogDebug("Magnifier windows created");

            // 显示窗口（如果不是最小化启动）
            if (!_settings.StartMinimized)
            {
                ShowAllWindows();
                _log.LogDebug("Windows shown");
            }
            else
            {
                _windowsVisible = false;
            }

            // 注册全局快捷键 Win+Alt+M
            _hotkeyService = new HotkeyService();
            _hotkeyService.HotkeyTriggered += ToggleVisibility;
            _hotkeyService.Register();

            // 首次启动引导
            ShowFirstRunGuide();
        }
        catch (Exception ex)
        {
            _log.LogError($"Startup error: {ex}");
            System.Windows.MessageBox.Show($"启动错误: {ex.Message}\n\n详细信息请查看: {_logPath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    #region 全局快捷键

    private void ToggleVisibility()
    {
        if (_windowsVisible)
        {
            // 用户手动隐藏：全部隐藏
            foreach (var window in _magnifierWindows)
            {
                window.Hide();
            }
            _windowsVisible = false;
        }
        else
        {
            // 用户手动显示：恢复非全屏显示器的窗口，仍在全屏的不恢复
            _windowsVisible = true;
            foreach (var window in _magnifierWindows)
            {
                if (!_fullScreenHiddenDisplays.Contains(window.Display.DeviceName))
                {
                    window.Show();
                }
            }
        }
    }

    #endregion

    #region 首次启动引导

    private void ShowFirstRunGuide()
    {
        var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsMagnifier");
        var guideFlagPath = Path.Combine(configDir, ".first_run_done");

        if (File.Exists(guideFlagPath)) return;

        try
        {
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);
            File.WriteAllText(guideFlagPath, "");
        }
        catch (Exception ex)
        {
            _log.LogDebug($"首次运行标志文件创建失败: {ex.Message}");
        }

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";

        System.Windows.MessageBox.Show(
            "欢迎使用眼眸！\n\n" +
            "使用提示：\n" +
            "  - 右键点击窗口可打开设置或退出\n" +
            "  - Win + Alt + M 快捷键可切换显示/隐藏\n" +
            "  - 拖拽底部边框可调整窗口高度\n" +
            "  - 每个显示器可独立设置放大倍数",
            $"眼眸 v{versionStr}",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #endregion

    // 日志通过 LogService.Instance 统一管理

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        _log.LogError($"UnhandledException: {ex}");
    }

    private void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
    {
        _log.LogError($"ThreadException: {e.Exception}");
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _log.LogError($"DispatcherUnhandledException: {e.Exception}");
        e.Handled = true;

        // 高频异常保护：5 秒内达到 3 次则强制退出，防止渲染循环异常导致 UI 冻结
        var now = Environment.TickCount64;
        if (now - _firstExceptionTicks > 5000)
        {
            _firstExceptionTicks = now;
            _unhandledExceptionCount = 1;
        }
        else
        {
            _unhandledExceptionCount++;
            if (_unhandledExceptionCount > 2)
            {
                _log.LogError("5 秒内发生 3 次未处理异常，强制退出应用");
                Shutdown();
            }
        }
    }

    private void CreateMagnifierWindows()
    {
        var displays = _displayManager!.GetDisplays();

        // 只让主显示器的窗口显示在任务栏，避免多个任务栏图标
        bool firstWindow = true;
        foreach (var display in displays)
        {
            var window = new MainWindow(display, _settings!, firstWindow,
                pos => _displayFocusManager?.UpdateFromMousePosition(pos));
            window.SettingsRequested += ShowSettingsWindow;
            window.ExitRequested += OnWindowExitRequested;
            window.SettingsModified += OnSettingsModified;
            _magnifierWindows.Add(window);
            firstWindow = false;
        }

        // 设置初始活动状态
        if (_displayFocusManager?.ActiveDisplay != null)
        {
            UpdateActiveDisplay(_displayFocusManager.ActiveDisplay);
        }
    }

    private void ShowAllWindows()
    {
        foreach (var window in _magnifierWindows)
        {
            window.Show();
        }
    }

    private void OnWindowExitRequested() => Shutdown();
    private void OnSettingsModified() => _configService?.Save(_settings!);

    private bool _isShowingSettings;

    private void ShowSettingsWindow()
    {
        if (_isShowingSettings) return;
        _isShowingSettings = true;

        try
        {
            // 先确保窗口已显示（跳过因全屏而隐藏的显示器）
            _windowsVisible = true;
            foreach (var window in _magnifierWindows)
            {
                if (!_fullScreenHiddenDisplays.Contains(window.Display.DeviceName))
                {
                    window.Show();
                }
            }

            var displays = _displayManager!.GetDisplays();
            var settingsWindow = new SettingsWindow(_settings!, _configService!, displays);
            var owner = _magnifierWindows.FirstOrDefault(w => w.IsVisible);
            if (owner != null)
                settingsWindow.Owner = owner;
            settingsWindow.WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen;
            if (settingsWindow.ShowDialog() == true)
            {
                // 设置已保存，更新窗口
                ApplySettingsToWindows();
            }
        }
        finally
        {
            _isShowingSettings = false;
        }
    }

    private void ApplySettingsToWindows()
    {
        // 更新 TrackingManager 的设置（计时器间隔、钩子启停）
        _trackingManager?.UpdateSettings(_settings!);

        // 更新全屏检测设置
        UpdateFullScreenDetectorState();

        foreach (var window in _magnifierWindows)
        {
            window.UpdateSettings(_settings!);
        }
    }

    private void UpdateFullScreenDetectorState()
    {
        if (_settings!.HideOnFullScreen)
        {
            if (_fullScreenDetector == null)
            {
                _fullScreenDetector = new FullScreenDetector(_displayManager!);
                _fullScreenDetector.DisplayFullScreenStateChanged += OnDisplayFullScreenStateChanged;
            }
            _fullScreenDetector.Start();
        }
        else
        {
            _fullScreenDetector?.Stop();
            // 如果之前因全屏隐藏了窗口，恢复显示
            if (_fullScreenHiddenDisplays.Count > 0)
            {
                if (_windowsVisible)
                {
                    foreach (var displayName in _fullScreenHiddenDisplays)
                    {
                        var window = _magnifierWindows.FirstOrDefault(w => w.Display.DeviceName == displayName);
                        window?.Show();
                    }
                }
                _fullScreenHiddenDisplays.Clear();
            }
        }
    }

    private void OnDisplayFullScreenStateChanged(string displayName, bool isFullScreen)
    {
        if (isFullScreen)
        {
            // 该显示器全屏，隐藏对应的放大镜窗口
            _fullScreenHiddenDisplays.Add(displayName);
            var window = _magnifierWindows.FirstOrDefault(w => w.Display.DeviceName == displayName);
            window?.Hide();
        }
        else
        {
            // 该显示器退出全屏，恢复对应的放大镜窗口（仅当用户未手动隐藏时）
            _fullScreenHiddenDisplays.Remove(displayName);
            if (_windowsVisible)
            {
                var window = _magnifierWindows.FirstOrDefault(w => w.Display.DeviceName == displayName);
                window?.Show();
            }
        }
    }

    private void OnDisplaysChanged()
    {
        // 显示器配置变化，重建窗口
        Current?.Dispatcher?.BeginInvoke(() =>
        {
            foreach (var window in _magnifierWindows)
            {
                window.SettingsRequested -= ShowSettingsWindow;
                window.ExitRequested -= OnWindowExitRequested;
                window.SettingsModified -= OnSettingsModified;
                window.Close();
            }
            _magnifierWindows.Clear();
            _fullScreenHiddenDisplays.Clear();

            CreateMagnifierWindows();

            // 重新初始化显示器焦点管理（旧的 _activeDisplay 可能指向已不存在的显示器）
            _displayFocusManager?.Initialize();

            // 重置全屏检测状态（旧的 displayName 可能已失效）
            if (_fullScreenDetector != null)
            {
                _fullScreenDetector.Stop();
                _fullScreenDetector.Start();
            }

            ShowAllWindows();
            _windowsVisible = true;
        });
    }

    private void OnActiveDisplayChanged(DisplayInfo? activeDisplay)
    {
        UpdateActiveDisplay(activeDisplay);
    }

    private void UpdateActiveDisplay(DisplayInfo? activeDisplay)
    {
        foreach (var window in _magnifierWindows)
        {
            var isActive = window.Display.DeviceName == activeDisplay?.DeviceName;
            window.SetActive(isActive);
        }
    }

    private void OnPositionChanged(System.Windows.Point position, TrackingMode mode)
    {
        // 鼠标模式下：钩子回调仅处理模式切换，显示器焦点更新已移至 OnRendering 渲染循环
        if (mode == TrackingMode.Mouse)
        {
            // 只在从键盘模式切换回鼠标模式时清除键盘跟踪标记（避免每次鼠标移动都调度）
            if (_wasKeyboardMode)
            {
                _wasKeyboardMode = false;
                Current?.Dispatcher?.BeginInvoke(() =>
                {
                    foreach (var window in _magnifierWindows)
                    {
                        window.ClearKeyboardTracking();
                    }
                });
            }
            return;
        }

        // 键盘模式
        _wasKeyboardMode = true;
        Current?.Dispatcher?.BeginInvoke(() =>
        {
            // 根据 caret 位置确定所在显示器，而不是用鼠标所在显示器
            var caretDisplay = _displayManager?.GetDisplayFromPoint(position);
            var targetDeviceName = caretDisplay?.DeviceName ?? _displayFocusManager?.ActiveDisplay?.DeviceName;

            foreach (var window in _magnifierWindows)
            {
                if (window.Display.DeviceName == targetDeviceName)
                {
                    window.SetPosition(position);
                }
            }
        });
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        // 注销全局快捷键
        _hotkeyService?.Dispose();

        // 保存配置
        if (_settings != null && _configService != null)
        {
            _configService.Save(_settings);
        }

        // 清理资源
        _trackingManager?.Dispose();
        _displayFocusManager?.Dispose();
        _fullScreenDetector?.Dispose();

        foreach (var window in _magnifierWindows)
        {
            window.SettingsRequested -= ShowSettingsWindow;
            window.ExitRequested -= OnWindowExitRequested;
            window.SettingsModified -= OnSettingsModified;
            window.Close();
        }

        _displayManager?.Dispose();

        // 最后反初始化 Magnification API（所有窗口关闭后）
        if (_magInitialized)
        {
            MagnificationApi.MagUninitialize();
            _magInitialized = false;
        }

        // 释放 Mutex
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
