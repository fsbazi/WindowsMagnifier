using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
    private AppSettings _settings;
    private readonly MouseHook _mouseHook;
    private readonly KeyboardHook _keyboardHook;

    private volatile TrackingMode _currentMode = TrackingMode.Mouse;
    private Point _currentPosition;

    // 防抖：键盘光标位置获取（使用版本号替代 CTS 以减少对象分配）
    private volatile int _debounceVersion;
    private const int CaretDebounceMs = 30;
    private const int CaretTimeoutMs = 500;

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

        Task.Run(() => DebouncedCaretLookup(version));
    }

    /// <summary>
    /// 防抖获取光标位置 - 在后台线程中延迟执行，避免阻塞键盘钩子线程。
    /// 使用版本号进行防抖：延迟后检查版本号是否仍匹配，不匹配则说明有更新的按键，放弃本次。
    /// </summary>
    private async void DebouncedCaretLookup(int version)
    {
        try
        {
            await Task.Delay(CaretDebounceMs);

            // 防抖检查：如果版本号已被更新，说明有更新的按键事件，放弃本次
            if (_debounceVersion != version) return;

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
                // 只在成功获取 caret 位置后才切换到键盘模式
                _currentMode = TrackingMode.KeyboardInput;
                _currentPosition = result.Position;
                PositionChanged?.Invoke(_currentPosition, TrackingMode.KeyboardInput);
            }
        }
        catch (OperationCanceledException)
        {
            // 超时：光标获取耗时过长（可能 IME 阻塞），静默跳过
            System.Diagnostics.Debug.WriteLine("[Tracking] TryGetCaretPosition timed out");
        }
        catch
        {
            // 光标位置获取失败时静默忽略，保持之前的鼠标位置
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

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
        public RECT rcCaret;
    }

    #endregion

    private bool TryGetCaretPosition(out Point position)
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
            System.Diagnostics.Debug.WriteLine($"[Tracking] GetGUIThreadInfo error: {ex.Message}");
        }

        return false;
    }

    public void Dispose()
    {
        // 递增版本号使所有进行中的防抖任务自动放弃
        Interlocked.Increment(ref _debounceVersion);
        _mouseHook.Dispose();
        _keyboardHook.Dispose();
        GC.SuppressFinalize(this);
    }
}
