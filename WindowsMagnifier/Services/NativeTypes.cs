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
}
