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
    private const int CaretDebounceMs = 100;
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

        // 钩子回调中只做最小工作：切换模式 + 获取鼠标位置作为即时响应
        // 注意：不在钩子线程中调用任何跨进程 API（包括 ImmGetContext），
        // 否则前台窗口忙时会阻塞钩子线程导致全系统输入冻结
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
    private async void DebouncedCaretLookup(CancellationToken token)
    {
        try
        {
            await Task.Delay(CaretDebounceMs, token);
            if (_currentMode != TrackingMode.KeyboardInput) return;

            // IME 合成期间再次检测，防止防抖延迟期间开始合成
            if (IsImeComposing()) return;

            // 超时保护：避免 TryGetCaretPosition 在异常情况下长时间阻塞
            var caretTask = Task.Run(() =>
            {
                if (TryGetCaretPosition(out var pos))
                    return (Success: true, Position: pos);
                return (Success: false, Position: new Point());
            }, token);

            var result = await caretTask.WaitAsync(TimeSpan.FromMilliseconds(CaretTimeoutMs), token);

            if (result.Success &&
                !double.IsInfinity(result.Position.X) && !double.IsInfinity(result.Position.Y) &&
                !double.IsNaN(result.Position.X) && !double.IsNaN(result.Position.Y))
            {
                if (token.IsCancellationRequested) return;
                _currentPosition = result.Position;
                PositionChanged?.Invoke(_currentPosition, TrackingMode.KeyboardInput);
            }
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException)
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

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    private static extern int ImmGetCompositionString(IntPtr hIMC, uint dwIndex, IntPtr lpBuf, uint dwBufLen);

    private const uint GCS_COMPSTR = 0x0008;

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

    /// <summary>
    /// 检测当前前台窗口是否正在进行 IME 合成（输入法组合输入中）
    /// </summary>
    private bool IsImeComposing()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        var hIMC = ImmGetContext(hwnd);
        if (hIMC == IntPtr.Zero) return false;

        try
        {
            // 获取合成字符串长度，>0 表示正在合成中
            int len = ImmGetCompositionString(hIMC, GCS_COMPSTR, IntPtr.Zero, 0);
            return len > 0;
        }
        finally
        {
            ImmReleaseContext(hwnd, hIMC);
        }
    }

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
