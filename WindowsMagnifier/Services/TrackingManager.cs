using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using WindowsMagnifier.Models;

namespace WindowsMagnifier.Services;

/// <summary>
/// 跟随模式
/// </summary>
public enum TrackingMode
{
    Mouse,
    KeyboardInput
}

/// <summary>
/// 跟随模式管理器 - 管理鼠标/键盘跟随优先级
/// </summary>
public class TrackingManager : IDisposable
{
    private static readonly LogService _log = LogService.Instance;

    private AppSettings _settings;
    private readonly MouseHook _mouseHook;
    private readonly KeyboardHook _keyboardHook;

    private volatile TrackingMode _currentMode = TrackingMode.Mouse;
    private Point _currentPosition;

    // 防抖：键盘光标位置获取（使用版本号替代 CTS 以减少对象分配）
    private volatile int _debounceVersion;
    private const int CaretDebounceMs = 30;
    private const int CaretTimeoutMs = 500;

    // UIA 连续超时降级
    private volatile int _consecutiveUiaTimeouts;
    private long _uiaDisabledUntilTicks;
    private const int MaxConsecutiveTimeouts = 3;
    private const long UiaBackoffTicks = 10 * TimeSpan.TicksPerSecond;

    // UIA 静默失败日志节流
    private long _lastUiaFailLogTicks;
    private const long UiaFailLogInterval = 2 * TimeSpan.TicksPerSecond;

    // 显示器变化标记
    private volatile bool _displayChanged;

    /// <summary>
    /// 当前跟踪位置
    /// </summary>
    public Point CurrentPosition => _currentPosition;

    /// <summary>
    /// 位置变化事件
    /// </summary>
    public event Action<Point, TrackingMode>? PositionChanged;

    public TrackingManager(AppSettings settings)
    {
        _settings = settings;
        _mouseHook = new MouseHook();
        _keyboardHook = new KeyboardHook();

        _mouseHook.MouseMoved += OnMouseMoved;
        _keyboardHook.KeyPressed += OnKeyPressed;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void Start()
    {
        if (_settings.FollowMouse)
        {
            _mouseHook.Start();
        }

        if (_settings.FollowKeyboardInput)
        {
            _keyboardHook.Start();
        }
    }

    public void Stop()
    {
        _mouseHook.Stop();
        _keyboardHook.Stop();
    }

    /// <summary>
    /// 更新设置并刷新钩子状态
    /// </summary>
    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;

        // 根据新设置启停鼠标钩子
        if (settings.FollowMouse)
            _mouseHook.Start();
        else
            _mouseHook.Stop();

        // 根据新设置启停键盘钩子
        if (settings.FollowKeyboardInput)
            _keyboardHook.Start();
        else
            _keyboardHook.Stop();
    }

    private void OnMouseMoved(int x, int y)
    {
        // 鼠标移动即切回鼠标模式
        if (_currentMode != TrackingMode.Mouse)
        {
            _currentMode = TrackingMode.Mouse;
        }

        _currentPosition = new Point(x, y);
        PositionChanged?.Invoke(_currentPosition, _currentMode);
    }

    private void OnKeyPressed(int vkCode)
    {
        if (!_settings.FollowKeyboardInput) return;

        // 钩子回调中只做最小工作：递增版本号并启动后台 caret 查找
        // 使用 volatile int 版本号替代 CTS 防抖，避免每次按键创建/销毁 CTS 对象
        // 注意：不在钩子线程中调用任何跨进程 API（包括 ImmGetContext），
        // 否则前台窗口忙时会阻塞钩子线程导致全系统输入冻结
        var version = Interlocked.Increment(ref _debounceVersion);

        _ = Task.Run(async () => await DebouncedCaretLookup(version));
    }

    /// <summary>
    /// 显示器配置变化时的处理：重置 UIA 状态，确保下次按键时重新尝试。
    /// 显示器开关后 UIA 元素可能变陈旧（TextPattern 丢失选区、BoundingRectangle 失效），
    /// 重置内部计数器让后续请求以全新状态重试。
    /// </summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _displayChanged = true;
        Interlocked.Exchange(ref _consecutiveUiaTimeouts, 0);
        Interlocked.Exchange(ref _uiaDisabledUntilTicks, 0);
        _log.LogDebug("Tracking", "Display changed, UIA state reset");
    }

    /// <summary>
    /// 节流日志：同一类 UIA 失败最多每 2 秒记一次，避免高频按键灌满日志。
    /// </summary>
    private void LogThrottled(string message)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastUiaFailLogTicks);
        if (now - last >= UiaFailLogInterval)
        {
            Interlocked.Exchange(ref _lastUiaFailLogTicks, now);
            _log.LogDebug("Tracking", message);
        }
    }

    /// <summary>
    /// 屏幕面积的一半作为 BoundingRect 面积阈值。
    /// 允许大多数输入区域通过（如微信 974x804），拒绝全屏级矩形（如 1920x1080）。
    /// </summary>
    private static double GetMaxBoundingArea()
    {
        return SystemParameters.PrimaryScreenWidth * SystemParameters.PrimaryScreenHeight * 0.5;
    }

    /// <summary>
    /// 防抖获取光标位置 - 在后台线程中延迟执行，避免阻塞键盘钩子线程。
    /// 使用版本号进行防抖：延迟后检查版本号是否仍匹配，不匹配则说明有更新的按键，放弃本次。
    /// </summary>
    private async Task DebouncedCaretLookup(int version)
    {
        try
        {
            await Task.Delay(CaretDebounceMs);

            // 防抖检查：如果版本号已被更新，说明有更新的按键事件，放弃本次
            if (_debounceVersion != version) return;

            // 控制台窗口（cmd/PowerShell）的 UIA 查询会失败并污染状态，直接跳过
            if (IsConsoleWindow())
            {
                LogThrottled("Skipping caret lookup: console window");
                return;
            }

            // 超时保护：使用临时 CTS 仅用于 WaitAsync 超时控制
            using var timeoutCts = new CancellationTokenSource(CaretTimeoutMs);
            var token = timeoutCts.Token;

            var caretTask = Task.Run(() =>
            {
                if (TryGetCaretPosition(out var pos))
                    return (Success: true, Position: pos);
                return (Success: false, Position: new Point());
            }, token);

            var result = await caretTask.WaitAsync(token);

            // 再次检查版本号：超时等待期间可能有新按键
            if (_debounceVersion != version) return;

            if (result.Success &&
                !double.IsInfinity(result.Position.X) && !double.IsInfinity(result.Position.Y) &&
                !double.IsNaN(result.Position.X) && !double.IsNaN(result.Position.Y))
            {
                Interlocked.Exchange(ref _consecutiveUiaTimeouts, 0);
                // 只在成功获取 caret 位置后才切换到键盘模式
                _currentMode = TrackingMode.KeyboardInput;
                _currentPosition = result.Position;
                PositionChanged?.Invoke(_currentPosition, TrackingMode.KeyboardInput);
            }
        }
        catch (OperationCanceledException)
        {
            // 超时：光标获取耗时过长（可能 IME 阻塞或前台进程无响应）
            var timeouts = Interlocked.Increment(ref _consecutiveUiaTimeouts);
            _log.LogDebug("Tracking", $"UIA timeout (consecutive={timeouts})");
            if (timeouts >= MaxConsecutiveTimeouts)
            {
                Interlocked.Exchange(ref _uiaDisabledUntilTicks,
                    DateTime.UtcNow.Ticks + UiaBackoffTicks);
                _log.LogDebug("Tracking", "UIA enter backoff (10s)");
            }
        }
        catch (Exception ex)
        {
            // 光标位置获取失败时记录日志，保持之前的鼠标位置
            _log.LogDebug("Tracking", $"DebouncedCaretLookup error: {ex.Message}");
        }
    }

    #region P/Invoke for caret position

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativeTypes.POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public NativeTypes.RECT rcCaret;
    }

    #endregion

    /// <summary>
    /// 判断前台窗口是否为终端窗口（cmd / PowerShell / Windows Terminal）。
    /// 终端窗口的 UIA 光标查询会失败并可能污染 UIA 状态。
    /// </summary>
    private static bool IsConsoleWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        var sb = new StringBuilder(256);
        var len = GetClassName(hwnd, sb, sb.Capacity);
        if (len <= 0) return false;

        var className = sb.ToString();
        return className == "ConsoleWindowClass"              // 传统 cmd / PowerShell
            || className == "CASCADIA_HOSTING_WINDOW_CLASS";  // Windows Terminal
    }

    private bool TryGetCaretPosition(out Point position)
    {
        position = new Point();

        // 快速路径：Win32 GetGUIThreadInfo（适用于记事本等传统应用）
        if (TryGetCaretViaWin32(out position))
        {
            Interlocked.Exchange(ref _consecutiveUiaTimeouts, 0);
            return true;
        }

        // 回退路径：UI Automation（适用于 Chrome、Edge、VS Code 等现代应用）
        if (TryGetCaretViaUIAutomation(out position))
            return true;

        return false;
    }

    /// <summary>
    /// 通过 Win32 GetGUIThreadInfo 获取光标位置。
    /// 仅对使用系统 Caret（CreateCaret）的传统应用有效，如记事本。
    /// </summary>
    private bool TryGetCaretViaWin32(out Point position)
    {
        position = new Point();

        try
        {
            // 使用 GetGUIThreadInfo 获取光标位置（跨进程，不会阻塞 IME）
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                var threadId = GetWindowThreadProcessId(hwnd, out _);
                if (threadId == 0)
                    return false;

                var guiInfo = new GUITHREADINFO();
                guiInfo.cbSize = Marshal.SizeOf<GUITHREADINFO>();

                if (GetGUIThreadInfo(threadId, ref guiInfo) && guiInfo.hwndCaret != IntPtr.Zero)
                {
                    var pt = new NativeTypes.POINT
                    {
                        X = guiInfo.rcCaret.Left,
                        Y = guiInfo.rcCaret.Top
                    };

                    if (ClientToScreen(guiInfo.hwndCaret, ref pt))
                    {
                        position = new Point(pt.X, pt.Y);
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug("Tracking", $"Win32 caret error: {ex.Message}");
            return false;
        }

        LogThrottled("Win32: no caret (hwndCaret=0 or threadId=0)");
        return false;
    }

    /// <summary>
    /// 通过 UI Automation 获取光标位置。
    /// 适用于 Chrome、Edge、Firefox、VS Code 等不使用 Win32 Caret 的现代应用。
    /// 优先使用 TextPattern 获取精确光标位置，失败时回退到焦点元素的边界矩形。
    /// </summary>
    private bool TryGetCaretViaUIAutomation(out Point position)
    {
        position = new Point();

        // Circuit Breaker 判定：
        // - 窗口未过期 → 直接跳过 UIA 调用
        // - 窗口刚过期 → 清零截止时间戳和连续超时计数，让下一轮重试拥有完整的 3 次容错预算
        //   (若不归零, 首次重试失败就会 timeouts 从 3→4, 立即再次合闸, 导致永久锁死)
        var disabledUntil = Interlocked.Read(ref _uiaDisabledUntilTicks);
        if (disabledUntil > 0)
        {
            if (DateTime.UtcNow.Ticks < disabledUntil)
                return false;
            Interlocked.Exchange(ref _uiaDisabledUntilTicks, 0);
            Interlocked.Exchange(ref _consecutiveUiaTimeouts, 0);
            _log.LogDebug("Tracking", "UIA backoff expired, counter reset");
        }

        try
        {
            var focused = System.Windows.Automation.AutomationElement.FocusedElement;
            if (focused == null)
            {
                LogThrottled("UIA: FocusedElement null");
                return false;
            }

            // 方法 A：通过 TextPattern 获取选区/光标的精确位置
            if (focused.TryGetCurrentPattern(
                System.Windows.Automation.TextPattern.Pattern, out var patternObj))
            {
                var textPattern = (System.Windows.Automation.TextPattern)patternObj;
                var selections = textPattern.GetSelection();
                if (selections != null && selections.Length > 0)
                {
                    var rects = selections[0].GetBoundingRectangles();
                    if (rects != null && rects.Length >= 1)
                    {
                        // 每个 Rect 包含一行文字的边界矩形
                        position = new Point(rects[0].X, rects[0].Y);
                        return true;
                    }
                }

                // 选区为空时，回退到可见文本范围的最后位置作为近似光标
                var visibleRanges = textPattern.GetVisibleRanges();
                if (visibleRanges != null && visibleRanges.Length > 0)
                {
                    var lastRange = visibleRanges[visibleRanges.Length - 1];
                    var lastRects = lastRange.GetBoundingRectangles();
                    if (lastRects != null && lastRects.Length > 0)
                    {
                        // 取最后一行文本的右端作为近似光标位置
                        var lastRect = lastRects[lastRects.Length - 1];
                        position = new Point(lastRect.Right, lastRect.Y);
                        return true;
                    }
                }
                LogThrottled("UIA: TextPattern no selection");
            }

            // 方法 B：使用焦点元素的边界矩形作为近似位置
            var rect = focused.Current.BoundingRectangle;
            if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
            {
                var area = rect.Width * rect.Height;
                // 面积过大说明返回的是整个控件/面板区域（如 Terminal、Google Docs），
                // 这种情况下位置不可靠，放弃切换到键盘模式
                if (area > GetMaxBoundingArea())
                {
                    LogThrottled($"UIA: BoundingRect too large ({(int)rect.Width}x{(int)rect.Height})");
                    return false;
                }

                // 使用焦点元素的左侧中间作为近似光标位置
                position = new Point(rect.Left, rect.Top + rect.Height / 2);
                return true;
            }

            LogThrottled("UIA: BoundingRect empty");
        }
        catch (System.Windows.Automation.ElementNotAvailableException)
        {
            LogThrottled("UIA: ElementNotAvailable");
        }
        catch (Exception ex)
        {
            _log.LogDebug("Tracking", $"UIA error: {ex.Message}");
        }
        return false;
    }

    public void Dispose()
    {
        // 递增版本号使所有进行中的防抖任务自动放弃
        Interlocked.Increment(ref _debounceVersion);
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _mouseHook.Dispose();
        _keyboardHook.Dispose();
        GC.SuppressFinalize(this);
    }
}
