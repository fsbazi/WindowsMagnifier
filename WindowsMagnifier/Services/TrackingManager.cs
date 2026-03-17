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

    // 防抖：键盘光标位置获取
    private CancellationTokenSource? _caretCts;
    private const int CaretDebounceMs = 50;

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

        // 钩子回调中只做最小工作：切换模式 + 获取鼠标位置作为即时响应
        _currentMode = TrackingMode.KeyboardInput;
        _currentPosition = new Point(
            System.Windows.Forms.Control.MousePosition.X,
            System.Windows.Forms.Control.MousePosition.Y);
        PositionChanged?.Invoke(_currentPosition, _currentMode);

        // 防抖获取 caret 位置（原子替换，避免竞态）
        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _caretCts, newCts);
        oldCts?.Cancel();
        oldCts?.Dispose();
        var token = newCts.Token;

        Task.Run(() => DebouncedCaretLookup(token), token);
    }

    /// <summary>
    /// 防抖获取光标位置 - 在后台线程中延迟执行，避免阻塞键盘钩子线程
    /// </summary>
    private void DebouncedCaretLookup(CancellationToken token)
    {
        try
        {
            Thread.Sleep(CaretDebounceMs);
            if (token.IsCancellationRequested) return;
            if (_currentMode != TrackingMode.KeyboardInput) return;

            if (TryGetCaretPosition(out var caretPos) &&
                !double.IsInfinity(caretPos.X) && !double.IsInfinity(caretPos.Y) &&
                !double.IsNaN(caretPos.X) && !double.IsNaN(caretPos.Y))
            {
                if (token.IsCancellationRequested) return;
                _currentPosition = caretPos;
                PositionChanged?.Invoke(_currentPosition, TrackingMode.KeyboardInput);
            }
        }
        catch (OperationCanceledException) { }
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
            // 方案1：UI Automation（首选）
            var focusedElement = System.Windows.Automation.AutomationElement.FocusedElement;
            if (focusedElement != null)
            {
                var pattern = focusedElement.GetCurrentPattern(System.Windows.Automation.TextPattern.Pattern) as System.Windows.Automation.TextPattern;
                if (pattern != null)
                {
                    var selection = pattern.GetSelection();
                    if (selection != null && selection.Length > 0)
                    {
                        var range = selection[0];
                        var rects = range.GetBoundingRectangles();
                        if (rects != null && rects.Length > 0)
                        {
                            var rect = rects[0];
                            if (!double.IsInfinity(rect.Left) && !double.IsInfinity(rect.Top) &&
                                !double.IsNaN(rect.Left) && !double.IsNaN(rect.Top))
                            {
                                position = new Point(rect.Left, rect.Top);
                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // UI Automation 失败，继续尝试后备方案
            System.Diagnostics.Debug.WriteLine($"[Tracking] UI Automation error: {ex.Message}");
        }

        try
        {
            // 方案2：GetGUIThreadInfo（跨进程获取光标位置）
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                var threadId = GetWindowThreadProcessId(hwnd, out _);
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
        var cts = Interlocked.Exchange(ref _caretCts, null);
        cts?.Cancel();
        cts?.Dispose();
        _mouseHook.Dispose();
        _keyboardHook.Dispose();
        GC.SuppressFinalize(this);
    }
}
