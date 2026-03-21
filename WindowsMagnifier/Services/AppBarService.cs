using System;
using System.Runtime.InteropServices;

namespace WindowsMagnifier.Services;

/// <summary>
/// AppBar 服务 - 将放大镜窗口注册为系统应用栏，预留屏幕区域
/// </summary>
public class AppBarService : IDisposable
{
    #region Win32 API

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public NativeTypes.RECT rc;
        public int lParam;
    }

    private const uint ABM_NEW = 0x00000000;
    private const uint ABM_REMOVE = 0x00000001;
    private const uint ABM_QUERYPOS = 0x00000002;
    private const uint ABM_SETPOS = 0x00000003;

    private const uint ABE_TOP = 1;

    public const uint ABN_POSCHANGED = 0x00000001;
    public const uint ABN_FULLSCREENAPP = 0x00000002;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    #endregion

    private IntPtr _hwnd;
    private bool _isRegistered;
    private uint _callbackMessage;
    private int _screenLeft;
    private int _screenTop;
    private int _screenRight;
    private int _height;
    private bool _isUpdatingPosition;

    public uint CallbackMessage => _callbackMessage;
    public bool IsRegistered => _isRegistered;

    /// <summary>
    /// 注册窗口为顶部 AppBar，为该显示器预留工作区。
    /// </summary>
    public bool Register(IntPtr hwnd, int height, int screenLeft, int screenTop, int screenRight)
    {
        if (_isRegistered) return true;

        _hwnd = hwnd;
        _screenLeft = screenLeft;
        _screenTop = screenTop;
        _screenRight = screenRight;
        _height = height;

        _callbackMessage = RegisterWindowMessage("WindowsMagnifier_AppBar_" + hwnd.ToInt64());

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uCallbackMessage = _callbackMessage
        };

        var result = SHAppBarMessage(ABM_NEW, ref abd);
        if (result == 0)
        {
            Log($"ABM_NEW failed, falling back to direct positioning");
            DirectPosition(height, screenLeft, screenTop, screenRight);
            return false;
        }

        _isRegistered = true;

        // 设置位置并预留工作区
        SetPosition(height, screenLeft, screenTop, screenRight);

        return true;
    }

    /// <summary>
    /// 更新 AppBar 高度
    /// </summary>
    public void UpdateHeight(int height)
    {
        if (_isRegistered)
        {
            SetPosition(height, _screenLeft, _screenTop, _screenRight);
        }
        else
        {
            DirectPosition(height, _screenLeft, _screenTop, _screenRight);
        }
    }

    /// <summary>
    /// AppBar 注册失败时的回退定位
    /// </summary>
    private void DirectPosition(int height, int screenLeft, int screenTop, int screenRight)
    {
        _height = height;
        _screenLeft = screenLeft;
        _screenTop = screenTop;
        _screenRight = screenRight;

        // 副屏使用 SetWindowPos + HWND_TOPMOST 确保窗口在顶部且不被覆盖
        SetWindowPos(_hwnd, HWND_TOPMOST, screenLeft, screenTop,
                     screenRight - screenLeft, height, SWP_NOACTIVATE);
        Log($"DirectPosition: X={screenLeft}, Y={screenTop}, W={screenRight - screenLeft}, H={height}");
    }

    private static void Log(string message)
    {
        LogService.Instance.LogDebug("AppBar", message);
    }

    private void SetPosition(int height, int screenLeft, int screenTop, int screenRight)
    {
        _height = height;
        _screenLeft = screenLeft;
        _screenTop = screenTop;
        _screenRight = screenRight;

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = _hwnd,
            uEdge = ABE_TOP,
            rc = new NativeTypes.RECT
            {
                Left = screenLeft,
                Top = screenTop,
                Right = screenRight,
                Bottom = screenTop + height
            }
        };

        Log($"Before QUERYPOS: Top={abd.rc.Top}, Bottom={abd.rc.Bottom}, Left={abd.rc.Left}, Right={abd.rc.Right}");

        // 查询可用位置
        SHAppBarMessage(ABM_QUERYPOS, ref abd);

        Log($"After QUERYPOS: Top={abd.rc.Top}, Bottom={abd.rc.Bottom}, Left={abd.rc.Left}, Right={abd.rc.Right}");

        // 强制恢复到该显示器的正确坐标（QUERYPOS 可能将坐标偏移到其他显示器范围）
        abd.rc.Left = screenLeft;
        abd.rc.Top = screenTop;
        abd.rc.Right = screenRight;
        abd.rc.Bottom = screenTop + height;

        // 设置最终位置
        SHAppBarMessage(ABM_SETPOS, ref abd);

        Log($"After SETPOS: Top={abd.rc.Top}, Bottom={abd.rc.Bottom}");

        // 只用 SetWindowPos 定位（不用 MoveWindow 避免双重调用导致位置乒乓）
        SetWindowPos(_hwnd, HWND_TOPMOST, abd.rc.Left, abd.rc.Top,
                     abd.rc.Right - abd.rc.Left, abd.rc.Bottom - abd.rc.Top,
                     SWP_NOACTIVATE);

        Log($"SetWindowPos: X={abd.rc.Left}, Y={abd.rc.Top}, W={abd.rc.Right - abd.rc.Left}, H={abd.rc.Bottom - abd.rc.Top}");
    }

    /// <summary>
    /// 处理 AppBar 回调消息
    /// </summary>
    public void HandleCallback(IntPtr wParam)
    {
        if (!_isRegistered) return;

        // 防重入：避免多个 AppBar 之间互相触发位置更新导致死循环
        if (_isUpdatingPosition) return;

        var notification = (uint)wParam.ToInt32();
        switch (notification)
        {
            case ABN_POSCHANGED:
                // 其他 AppBar 位置变化，重新设置自己的位置
                _isUpdatingPosition = true;
                try
                {
                    SetPosition(_height, _screenLeft, _screenTop, _screenRight);
                }
                finally
                {
                    _isUpdatingPosition = false;
                }
                break;

            case ABN_FULLSCREENAPP:
                break;
        }
    }

    public void Unregister()
    {
        if (!_isRegistered) return;

        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = _hwnd
        };

        SHAppBarMessage(ABM_REMOVE, ref abd);
        _isRegistered = false;
    }

    public void Dispose()
    {
        Unregister();
        GC.SuppressFinalize(this);
    }
}
