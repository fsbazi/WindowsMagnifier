using System.Runtime.InteropServices;

namespace WindowsMagnifier.Services;

/// <summary>
/// 共享的 Win32 原生类型定义，供多个服务共用
/// </summary>
internal static class NativeTypes
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;

        public RECT(int left, int top, int right, int bottom)
        {
            Left = left; Top = top; Right = right; Bottom = bottom;
        }
    }
}
