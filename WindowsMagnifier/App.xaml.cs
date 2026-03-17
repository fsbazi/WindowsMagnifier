using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using WindowsMagnifier.Models;
using WindowsMagnifier.Services;

namespace WindowsMagnifier;

public partial class App : System.Windows.Application
{
    private const string MutexName = "WindowsMagnifier_SingleInstance_B7F3A2E1";
    private const int HOTKEY_ID = 0x4D41; // 'MA'
    private const int MOD_ALT = 0x0001;
    private const int MOD_WIN = 0x0008;
    private const int MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312;
    private const int VK_M = 0x4D;
    private const long MaxLogSize = 1024 * 1024; // 1MB

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private Mutex? _mutex;
    private ConfigService? _configService;
    private AppSettings? _settings;
    private DisplayManager? _displayManager;
    private DisplayFocusManager? _displayFocusManager;
    private TrackingManager? _trackingManager;
    private FullScreenDetector? _fullScreenDetector;
    private readonly List<MainWindow> _magnifierWindows = new();
    private string _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsMagnifier", "error.log");
    private bool _magInitialized;
    private bool _windowsVisible = true;
    private volatile bool _wasKeyboardMode;
    private HwndSource? _hotkeySource;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // 单实例检查
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("放大镜已在运行中。", "Windows 放大镜", MessageBoxButton.OK, MessageBoxImage.Information);
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
            LogMessage("Application starting...");

            // 初始化配置
            _configService = new ConfigService();
            _settings = _configService.Load();
            LogMessage("Config loaded");

            // 初始化显示器管理
            _displayManager = new DisplayManager();
            _displayManager.DisplaysChanged += OnDisplaysChanged;
            LogMessage($"DisplayManager created, displays: {_displayManager.GetDisplays().Count}");

            // 初始化焦点管理
            _displayFocusManager = new DisplayFocusManager(_displayManager, _settings.DisplaySwitchDelay);
            _displayFocusManager.ActiveDisplayChanged += OnActiveDisplayChanged;
            _displayFocusManager.Initialize();
            LogMessage("DisplayFocusManager initialized");

            // 初始化跟踪管理
            _trackingManager = new TrackingManager(_settings);
            _trackingManager.PositionChanged += OnPositionChanged;
            _trackingManager.Start();
            LogMessage("TrackingManager started");

            // 初始化全屏检测（如果启用）
            if (_settings.HideOnFullScreen)
            {
                _fullScreenDetector = new FullScreenDetector(_displayManager);
                _fullScreenDetector.FullScreenStateChanged += OnFullScreenStateChanged;
                _fullScreenDetector.Start();
                LogMessage("FullScreenDetector started");
            }

            // 初始化 Magnification API（应用级别，所有窗口共享）
            _magInitialized = MagnificationApi.MagInitialize();
            if (!_magInitialized)
            {
                LogMessage($"MagInitialize failed, error: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                LogMessage("MagInitialize succeeded");
            }

            // 创建放大镜窗口
            CreateMagnifierWindows();
            LogMessage("Magnifier windows created");

            // 显示窗口（如果不是最小化启动）
            if (!_settings.StartMinimized)
            {
                ShowAllWindows();
                LogMessage("Windows shown");
            }
            else
            {
                _windowsVisible = false;
            }

            // 注册全局快捷键 Win+Alt+M
            RegisterGlobalHotkey();

            // 首次启动引导
            ShowFirstRunGuide();
        }
        catch (Exception ex)
        {
            LogMessage($"Startup error: {ex}");
            System.Windows.MessageBox.Show($"启动错误: {ex.Message}\n\n详细信息请查看: {_logPath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    #region 全局快捷键

    private void RegisterGlobalHotkey()
    {
        try
        {
            // 创建一个隐藏窗口接收快捷键消息
            var parameters = new HwndSourceParameters("HotkeyWindow")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0
            };
            _hotkeySource = new HwndSource(parameters);
            _hotkeySource.AddHook(HotkeyWndProc);

            if (!RegisterHotKey(_hotkeySource.Handle, HOTKEY_ID, MOD_WIN | MOD_ALT | MOD_NOREPEAT, VK_M))
            {
                LogMessage($"RegisterHotKey failed, error: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                LogMessage("Global hotkey Win+Alt+M registered");
            }
        }
        catch (Exception ex)
        {
            LogMessage($"RegisterGlobalHotkey exception: {ex.Message}");
        }
    }

    private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            ToggleVisibility();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleVisibility()
    {
        if (_windowsVisible)
        {
            foreach (var window in _magnifierWindows)
            {
                window.Hide();
            }
            _windowsVisible = false;
        }
        else
        {
            ShowAllWindows();
            _windowsVisible = true;
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
        catch { }

        System.Windows.MessageBox.Show(
            "欢迎使用 Windows 放大镜！\n\n" +
            "使用提示：\n" +
            "  - 右键点击放大镜窗口可打开设置或退出\n" +
            "  - Win + Alt + M 快捷键可切换显示/隐藏\n" +
            "  - 拖拽底部边框可调整窗口高度\n" +
            "  - 每个显示器可独立设置放大倍数",
            "Windows 放大镜 v1.0",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #endregion

    #region 日志

    private void LogMessage(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // 日志文件超过 1MB 时截断保留后半部分
            TruncateLogIfNeeded(_logPath);

            File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    private static void TruncateLogIfNeeded(string logPath)
    {
        try
        {
            if (!File.Exists(logPath)) return;
            var info = new FileInfo(logPath);
            if (info.Length <= MaxLogSize) return;

            var lines = File.ReadAllLines(logPath);
            var keepFrom = lines.Length / 2;
            File.WriteAllLines(logPath, lines[keepFrom..]);
        }
        catch { }
    }

    #endregion

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        LogMessage($"UnhandledException: {ex}");
        System.Windows.MessageBox.Show($"未处理的异常: {ex?.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
    {
        LogMessage($"ThreadException: {e.Exception}");
        System.Windows.MessageBox.Show($"线程异常: {e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogMessage($"DispatcherUnhandledException: {e.Exception}");
        System.Windows.MessageBox.Show($"调度器异常: {e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CreateMagnifierWindows()
    {
        var displays = _displayManager!.GetDisplays();

        // 只让主显示器的窗口显示在任务栏，避免多个任务栏图标
        bool firstWindow = true;
        foreach (var display in displays)
        {
            var window = new MainWindow(display, _settings!, firstWindow);
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
            // 先确保窗口已显示
            ShowAllWindows();
            _windowsVisible = true;

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
                _fullScreenDetector.FullScreenStateChanged += OnFullScreenStateChanged;
            }
            _fullScreenDetector.Start();
        }
        else
        {
            _fullScreenDetector?.Stop();
            // 如果之前因全屏隐藏了窗口，恢复显示
            if (!_windowsVisible && _fullScreenDetector?.IsFullScreenActive == true)
            {
                ShowAllWindows();
                _windowsVisible = true;
            }
        }
    }

    private void OnFullScreenStateChanged(bool isFullScreen)
    {
        if (isFullScreen)
        {
            // 全屏应用激活，隐藏放大镜窗口
            foreach (var window in _magnifierWindows)
            {
                window.Hide();
            }
        }
        else
        {
            // 全屏应用退出，恢复放大镜窗口
            if (_windowsVisible)
            {
                ShowAllWindows();
            }
        }
    }

    private void OnDisplaysChanged()
    {
        // 显示器配置变化，重建窗口
        Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var window in _magnifierWindows)
            {
                window.SettingsRequested -= ShowSettingsWindow;
                window.ExitRequested -= OnWindowExitRequested;
                window.SettingsModified -= OnSettingsModified;
                window.Close();
            }
            _magnifierWindows.Clear();

            CreateMagnifierWindows();
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
        // 只在鼠标模式下更新显示器焦点
        if (mode == TrackingMode.Mouse)
        {
            _displayFocusManager?.UpdateFromMousePosition(position);

            // 只在从键盘模式切换回鼠标模式时清除键盘跟踪标记（避免每次鼠标移动都调度）
            if (_wasKeyboardMode)
            {
                _wasKeyboardMode = false;
                Current.Dispatcher.BeginInvoke(() =>
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
        Current.Dispatcher.BeginInvoke(() =>
        {
            var activeDeviceName = _displayFocusManager?.ActiveDisplay?.DeviceName;
            foreach (var window in _magnifierWindows)
            {
                if (window.Display.DeviceName == activeDeviceName)
                    window.SetPosition(position);
            }
        });
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        // 注销全局快捷键
        if (_hotkeySource != null)
        {
            UnregisterHotKey(_hotkeySource.Handle, HOTKEY_ID);
            _hotkeySource.Dispose();
            _hotkeySource = null;
        }

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
