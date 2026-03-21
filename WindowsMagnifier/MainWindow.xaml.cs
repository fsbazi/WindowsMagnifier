using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsMagnifier.Models;
using WindowsMagnifier.Services;

namespace WindowsMagnifier;

/// <summary>
/// 放大镜窗口 - 使用 Windows Magnification API 实现硬件加速放大
/// </summary>
public partial class MainWindow : Window
{
    private readonly DisplayInfo _display;
    private readonly AppSettings _settings;
    private readonly AppBarService _appBarService;
    private readonly bool _showInTaskbar;
    private readonly Action<Point>? _onDisplayFocusUpdate;

    private bool _isActive;
    private bool _isResizing;
    private double _resizeStartY;
    private Point _lastPosition;
    private bool _hasPosition;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;
    private volatile bool _isKeyboardTracking;
    private int _lastCaptureX = int.MinValue;
    private int _lastCaptureY = int.MinValue;
    private int _lastCursorX = int.MinValue;
    private int _lastCursorY = int.MinValue;
    private long _lastRenderTick;
    private const long ForceRefreshIntervalTicks = 500 * TimeSpan.TicksPerMillisecond; // 500ms
    private int _cachedMagLevel; // 缓存放大倍数，避免每帧字典查找

    // Magnification API
    private IntPtr _hwndMag;
    private IntPtr _hwndHost;

    // 回退模式
    private ScreenCaptureService? _captureService;
    private bool _useFallback;

    /// <summary>
    /// 请求打开设置窗口
    /// </summary>
    public event Action? SettingsRequested;

    /// <summary>
    /// 请求退出应用
    /// </summary>
    public event Action? ExitRequested;

    /// <summary>
    /// 设置被修改（如拖拽调整高度）
    /// </summary>
    public event Action? SettingsModified;

    public DisplayInfo Display => _display;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativeTypes.POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8; // 显示但不激活

    public MainWindow(DisplayInfo display, AppSettings settings, bool showInTaskbar = false, Action<Point>? onDisplayFocusUpdate = null)
    {
        InitializeComponent();

        _display = display;
        _settings = settings;
        _cachedMagLevel = settings.GetMagnificationLevel(display.DeviceName);
        _appBarService = new AppBarService();
        _showInTaskbar = showInTaskbar;
        _onDisplayFocusUpdate = onDisplayFocusUpdate;

        ShowInTaskbar = showInTaskbar;

        // 只设置初始尺寸，不设位置（位置完全由 AppBar 在 Loaded 后控制）
        UpdateWindowSize();
        CreateContextMenu();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        SetActive(false);
    }

    private void CreateContextMenu()
    {
        var menu = new ContextMenu();

        var settingsItem = new MenuItem { Header = "设置(_S)" };
        settingsItem.Click += (s, e) => SettingsRequested?.Invoke();
        menu.Items.Add(settingsItem);

        var aboutItem = new MenuItem { Header = "关于(_A)" };
        aboutItem.Click += (s, e) => ShowAbout();
        menu.Items.Add(aboutItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "退出(_X)" };
        exitItem.Click += (s, e) => ExitRequested?.Invoke();
        menu.Items.Add(exitItem);

        ContextMenu = menu;
    }

    private void ShowAbout()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        System.Windows.MessageBox.Show(
            $"眼眸 v{versionStr}\n\n" +
            "眼眸 - 桌面放大辅助工具\n\n" +
            "快捷键：Win + Alt + M 切换显示/隐藏\n" +
            "右键菜单：设置 / 关于 / 退出",
            "关于",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        var hwndSource = source as HwndSource;
        if (hwndSource != null)
        {
            _hwndHost = hwndSource.Handle;
            hwndSource.AddHook(WndProc);

            Log($"Loaded: Display={_display.DeviceName}, IsPrimary={_display.IsPrimary}, Bounds={_display.Bounds}, DPI={_dpiScaleX}x{_dpiScaleY}");

            // 注册 AppBar - 这会通过 MoveWindow 将窗口移到正确位置
            var bounds = _display.Bounds;
            _appBarService.Register(
                _hwndHost,
                _settings.WindowHeight,
                (int)bounds.Left,
                (int)bounds.Top,
                (int)bounds.Right
            );

            Log($"After Register: HWND position set by AppBarService");

            // DPI 已确认，重新计算窗口尺寸（修正构造时 DPI=1.0 的初始值）
            UpdateWindowSize();

            // 初始化 Magnification API（MagInitialize 由 App 级别管理）
            InitializeMagnifier(_hwndHost);
        }

        if (GetCursorPos(out var pt))
        {
            _lastPosition = new Point(pt.X, pt.Y);
            _hasPosition = true;
        }
    }

    private void InitializeMagnifier(IntPtr hwndHost)
    {
        try
        {
            var physicalWidth = (int)(Width * _dpiScaleX);
            var physicalHeight = (int)(Height * _dpiScaleY);

            // 减去底部边框高度（1 逻辑像素 * DPI 缩放），确保 Magnifier 子窗口不遮盖 WPF 边框
            var borderHeight = (int)(1 * _dpiScaleY);
            var magHeight = physicalHeight - borderHeight;

            // 创建 Magnifier 子窗口，使用 MS_SHOWMAGNIFIEDCURSOR 显示放大后的鼠标指针
            _hwndMag = MagnificationApi.CreateWindowEx(
                0,
                MagnificationApi.WC_MAGNIFIER,
                "MagnifierWindow",
                MagnificationApi.WS_CHILD | MagnificationApi.WS_VISIBLE | MagnificationApi.MS_SHOWMAGNIFIEDCURSOR,
                0, 0, physicalWidth, magHeight,
                hwndHost,
                IntPtr.Zero,
                MagnificationApi.GetModuleHandle(null),
                IntPtr.Zero
            );

            if (_hwndMag == IntPtr.Zero)
            {
                Log($"CreateWindowEx Magnifier failed, error: {Marshal.GetLastWin32Error()}");
                // 回退到 BitBlt 方案
                InitializeFallback();
                return;
            }

            // 设置放大倍数
            var transform = MagnificationApi.MAGTRANSFORM.CreateIdentity(_settings.GetMagnificationLevel(_display.DeviceName));
            if (!MagnificationApi.MagSetWindowTransform(_hwndMag, ref transform))
            {
                Log($"MagSetWindowTransform failed, error: {Marshal.GetLastWin32Error()}");
            }

            // 排除宿主窗口，避免递归放大
            var filterList = new IntPtr[] { hwndHost };
            if (!MagnificationApi.MagSetWindowFilterList(_hwndMag, MagnificationApi.MW_FILTERMODE_EXCLUDE, 1, filterList))
            {
                Log($"MagSetWindowFilterList failed, error: {Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex)
        {
            Log($"InitializeMagnifier exception: {ex}");
            // 回退到 BitBlt 方案
            InitializeFallback();
        }
    }

    private void InitializeFallback()
    {
        _captureService = new ScreenCaptureService();
        _useFallback = true;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 停止渲染
        CompositionTarget.Rendering -= OnRendering;

        // 清理 Magnifier 子窗口
        if (_hwndMag != IntPtr.Zero)
        {
            MagnificationApi.DestroyWindow(_hwndMag);
            _hwndMag = IntPtr.Zero;
        }

        // 清理回退模式资源
        _captureService?.Dispose();
        _captureService = null;

        // 注销 AppBar
        _appBarService.Dispose();

        // 如果是任务栏窗口被关闭（用户从任务栏关闭），触发应用退出
        if (_showInTaskbar)
        {
            ExitRequested?.Invoke();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_appBarService.IsRegistered && (uint)msg == _appBarService.CallbackMessage)
        {
            _appBarService.HandleCallback(wParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 只更新窗口尺寸，位置完全由 AppBarService 通过 Win32 API 控制
    /// </summary>
    private void UpdateWindowSize()
    {
        var rect = _display.GetMagnifierWindowRect(_settings.WindowHeight);
        Width = rect.Width / _dpiScaleX;
        Height = rect.Height / _dpiScaleY;
    }

    public void SetActive(bool active)
    {
        _isActive = active;

        if (active)
        {
            _cachedMagLevel = _settings.GetMagnificationLevel(_display.DeviceName);
            InactiveOverlay.Visibility = Visibility.Collapsed;
            _lastCaptureX = int.MinValue;
            _lastCaptureY = int.MinValue;
            if (_useFallback)
            {
                MagnifiedImage.Visibility = Visibility.Visible;
                PointerCanvas.Visibility = Visibility.Visible;
                PointerScale.ScaleX = _cachedMagLevel;
                PointerScale.ScaleY = _cachedMagLevel;
            }
            // 显示 Magnifier 子窗口（解决 WPF Airspace 问题：InactiveOverlay 无法遮盖 Win32 子窗口）
            if (_hwndMag != IntPtr.Zero)
                ShowWindow(_hwndMag, SW_SHOWNA);
            CompositionTarget.Rendering -= OnRendering;
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            // 隐藏 Magnifier 子窗口，让 WPF 的 InactiveOverlay 遮罩生效
            if (_hwndMag != IntPtr.Zero)
                ShowWindow(_hwndMag, SW_HIDE);
            InactiveOverlay.Visibility = Visibility.Visible;
            if (_useFallback)
            {
                MagnifiedImage.Source = null;
            }
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    /// <summary>
    /// 只记录位置（由键盘钩子通过 Dispatcher 调用，在 UI 线程上）
    /// </summary>
    public void SetPosition(Point focusPoint)
    {
        if (double.IsNaN(focusPoint.X) || double.IsNaN(focusPoint.Y) ||
            double.IsInfinity(focusPoint.X) || double.IsInfinity(focusPoint.Y))
            return;

        _isKeyboardTracking = true;
        _lastPosition = focusPoint;
        _hasPosition = true;
        _lastCaptureX = int.MinValue;  // 强制下一帧渲染
    }

    /// <summary>
    /// 退出键盘跟踪模式，恢复鼠标跟踪
    /// </summary>
    public void ClearKeyboardTracking()
    {
        _isKeyboardTracking = false;
    }

    private static void Log(string message)
    {
        LogService.Instance.LogDebug(message);
    }

    /// <summary>
    /// 每一帧渲染时更新放大源区域
    /// </summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_isActive) return;

        // 键盘跟踪模式下不覆盖 _lastPosition，保留 caret 位置
        if (!_isKeyboardTracking)
        {
            // 直接获取最新鼠标位置
            if (GetCursorPos(out var pt))
            {
                _lastPosition = new Point(pt.X, pt.Y);
                _hasPosition = true;

                // 将显示器焦点更新从钩子线程移到渲染循环中执行
                _onDisplayFocusUpdate?.Invoke(_lastPosition);
            }
        }

        if (!_hasPosition) return;

        var focusPoint = _lastPosition;

        // 计算捕获区域（物理像素）
        var physicalWidth = Width * _dpiScaleX;
        var physicalHeight = Height * _dpiScaleY;
        var magLevel = _cachedMagLevel;
        var captureWidth = Math.Max(1, (int)(physicalWidth / magLevel));
        var captureHeight = Math.Max(1, (int)(physicalHeight / magLevel));

        var captureX = (int)(focusPoint.X - captureWidth / 2.0);
        var captureY = (int)(focusPoint.Y - captureHeight / 2.0);

        // 裁剪到屏幕边界（排除放大镜自身区域）
        var bounds = _display.Bounds;
        var effectiveTop = (int)(bounds.Top + _settings.WindowHeight);
        var screenRight = (int)bounds.Right;
        var screenBottom = (int)bounds.Bottom;
        var screenLeft = (int)bounds.Left;

        // 限制捕获尺寸不超过可用屏幕区域，防止溢出导致坐标计算异常
        var availableWidth = screenRight - screenLeft;
        var availableHeight = screenBottom - effectiveTop;
        if (availableWidth < 1) availableWidth = 1;
        if (availableHeight < 1) availableHeight = 1;
        captureWidth = Math.Min(captureWidth, availableWidth);
        captureHeight = Math.Min(captureHeight, availableHeight);

        captureX = Math.Max(screenLeft, Math.Min(screenRight - captureWidth, captureX));
        captureY = Math.Max(effectiveTop, Math.Min(screenBottom - captureHeight, captureY));

        // 检查源区域和光标位置变化
        var currentCursorX = (int)focusPoint.X;
        var currentCursorY = (int)focusPoint.Y;
        var sourceChanged = (captureX != _lastCaptureX || captureY != _lastCaptureY);
        var cursorMoved = (currentCursorX != _lastCursorX || currentCursorY != _lastCursorY);

        var now = DateTime.UtcNow.Ticks;
        if (!sourceChanged && !cursorMoved)
        {
            // 源区域和光标都未变化，检查强制刷新间隔（支持动态内容如视频/滚动）
            if (now - _lastRenderTick < ForceRefreshIntervalTicks)
                return;
        }

        if (sourceChanged)
        {
            _lastCaptureX = captureX;
            _lastCaptureY = captureY;
        }
        _lastCursorX = currentCursorX;
        _lastCursorY = currentCursorY;
        _lastRenderTick = now;

        if (_useFallback)
        {
            // BitBlt 回退模式
            var clampedRegion = new Rect(captureX, captureY, captureWidth, captureHeight);
            var bitmap = _captureService?.CaptureRegion(clampedRegion);
            if (bitmap != null)
            {
                MagnifiedImage.Source = bitmap;
            }

            // 更新鼠标指针位置
            var relativeX = focusPoint.X - captureX;
            var relativeY = focusPoint.Y - captureY;
            var magnifiedX = (relativeX * magLevel) / _dpiScaleX;
            var magnifiedY = (relativeY * magLevel) / _dpiScaleY;

            if (!double.IsInfinity(magnifiedX) && !double.IsNaN(magnifiedX))
            {
                Canvas.SetLeft(MousePointer, magnifiedX);
                Canvas.SetTop(MousePointer, magnifiedY);
            }
        }
        else if (_hwndMag != IntPtr.Zero)
        {
            if (sourceChanged)
            {
                // 源区域变化时更新源
                var sourceRect = new NativeTypes.RECT(captureX, captureY, captureX + captureWidth, captureY + captureHeight);
                MagnificationApi.MagSetWindowSource(_hwndMag, sourceRect);
            }
            // 总是重绘以更新光标位置
            MagnificationApi.InvalidateRect(_hwndMag, IntPtr.Zero, false);
        }
    }

    private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isResizing = true;
        _resizeStartY = e.GetPosition(this).Y;
        ResizeHandle.CaptureMouse();
    }

    private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizing) return;

        var currentY = e.GetPosition(this).Y;
        var delta = currentY - _resizeStartY;
        var newHeight = Height + delta;
        newHeight = Math.Max(100, Math.Min(600, newHeight));

        Height = newHeight;
        _resizeStartY = currentY;
    }

    private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isResizing = false;
        ResizeHandle.ReleaseMouseCapture();
        _settings.WindowHeight = (int)(Height * _dpiScaleY);
        _appBarService.UpdateHeight(_settings.WindowHeight);
        SettingsModified?.Invoke();

        // 调整 Magnifier 子窗口大小
        _cachedMagLevel = _settings.GetMagnificationLevel(_display.DeviceName);
        ResizeMagnifierChild();
    }

    public void UpdateSettings(AppSettings settings)
    {
        // 刷新缓存的放大倍数
        _cachedMagLevel = settings.GetMagnificationLevel(_display.DeviceName);

        // 只更新 WPF 尺寸，位置交给 AppBarService
        UpdateWindowSize();
        _appBarService.UpdateHeight(settings.WindowHeight);

        ResizeMagnifierChild();
    }

    private void ResizeMagnifierChild()
    {
        if (_hwndMag == IntPtr.Zero) return;

        var physicalWidth = (int)(Width * _dpiScaleX);
        var physicalHeight = (int)(Height * _dpiScaleY);
        var borderHeight = (int)(1 * _dpiScaleY);
        var magHeight = physicalHeight - borderHeight;
        MagnificationApi.SetWindowPos(_hwndMag, IntPtr.Zero, 0, 0, physicalWidth, magHeight, 0);

        var transform = MagnificationApi.MAGTRANSFORM.CreateIdentity(_cachedMagLevel);
        MagnificationApi.MagSetWindowTransform(_hwndMag, ref transform);
    }
}
