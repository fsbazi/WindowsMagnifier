using System;
using System.Runtime.InteropServices;

namespace WindowsMagnifier.Services;

/// <summary>
/// Windows Magnification API (magnification.dll) 封装
/// 这是 Windows 自带放大镜使用的同一套 API，支持硬件加速
/// </summary>
public static class MagnificationApi
{
    private const string MAG_DLL = "magnification.dll";

    [DllImport(MAG_DLL, SetLastError = true)]
    public static extern bool MagInitialize();

    [DllImport(MAG_DLL)]
    public static extern bool MagUninitialize();

    [DllImport(MAG_DLL)]
    public static extern bool MagSetWindowSource(IntPtr hwnd, RECT rect);

    [DllImport(MAG_DLL)]
    public static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM pTransform);

    [DllImport(MAG_DLL)]
    public static extern bool MagSetWindowFilterList(IntPtr hwnd, uint dwFilterMode, int count, IntPtr[] pHWND);

    public const string WC_MAGNIFIER = "Magnifier";
    public const uint MS_SHOWMAGNIFIEDCURSOR = 0x0001;
    public const uint MW_FILTERMODE_EXCLUDE = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public RECT(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MAGTRANSFORM
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public float[] v;

        public static MAGTRANSFORM CreateIdentity(float scale)
        {
            return new MAGTRANSFORM
            {
                v = new float[]
                {
                    scale, 0, 0,
                    0, scale, 0,
                    0, 0, 1
                }
            };
        }
    }

    // Win32 helpers for creating the magnifier child window
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool InvalidateRect(IntPtr hwnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Window styles
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
}
