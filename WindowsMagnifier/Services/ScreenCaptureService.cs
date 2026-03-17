using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowsMagnifier.Services;

/// <summary>
/// 屏幕捕获服务 - 使用 WriteableBitmap 避免每帧 GDI 对象分配
/// </summary>
public class ScreenCaptureService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr hDestDC, int x, int y, int nWidth, int nHeight, IntPtr hSrcDC, int xSrc, int ySrc, int dwRop);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, IntPtr lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        // bmiColors is not needed for 32bpp
    }

    private const int SRCCOPY = 0x00CC0020;
    private const int BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    private WriteableBitmap? _writeableBitmap;
    private int _cachedWidth;
    private int _cachedHeight;

    // 持久化的 GDI 资源，避免每帧分配
    private IntPtr _memDC;
    private IntPtr _memBitmap;
    private IntPtr _oldBitmap;
    private int _memWidth;
    private int _memHeight;

    /// <summary>
    /// 捕获指定区域的屏幕内容
    /// </summary>
    public BitmapSource? CaptureRegion(Rect region)
    {
        try
        {
            var width = (int)region.Width;
            var height = (int)region.Height;

            if (width <= 0 || height <= 0)
                return null;

            var x = (int)region.X;
            var y = (int)region.Y;

            // 确保 WriteableBitmap 大小正确
            if (_writeableBitmap == null || _cachedWidth != width || _cachedHeight != height)
            {
                _writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                _cachedWidth = width;
                _cachedHeight = height;

                // 重建 GDI 资源
                CleanupGdiResources();
            }

            // 懒创建持久化 GDI 资源
            if (_memDC == IntPtr.Zero || _memWidth != width || _memHeight != height)
            {
                CleanupGdiResources();
                var screenDC = GetDC(IntPtr.Zero);
                _memDC = CreateCompatibleDC(screenDC);
                _memBitmap = CreateCompatibleBitmap(screenDC, width, height);
                _oldBitmap = SelectObject(_memDC, _memBitmap);
                _memWidth = width;
                _memHeight = height;
                ReleaseDC(IntPtr.Zero, screenDC);
            }

            // BitBlt 从屏幕到内存 DC
            var hdcSrc = GetDC(IntPtr.Zero);
            BitBlt(_memDC, 0, 0, width, height, hdcSrc, x, y, SRCCOPY);
            ReleaseDC(IntPtr.Zero, hdcSrc);

            // 直接用 GetDIBits 将像素写入 WriteableBitmap
            _writeableBitmap.Lock();
            try
            {
                var bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height, // top-down
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = BI_RGB
                    }
                };

                GetDIBits(_memDC, _memBitmap, 0, (uint)height, _writeableBitmap.BackBuffer, ref bmi, DIB_RGB_COLORS);
                _writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                _writeableBitmap.Unlock();
            }

            return _writeableBitmap;
        }
        catch
        {
            return null;
        }
    }

    private void CleanupGdiResources()
    {
        if (_memDC != IntPtr.Zero)
        {
            if (_oldBitmap != IntPtr.Zero)
                SelectObject(_memDC, _oldBitmap);
            if (_memBitmap != IntPtr.Zero)
                DeleteObject(_memBitmap);
            DeleteDC(_memDC);
            _memDC = IntPtr.Zero;
            _memBitmap = IntPtr.Zero;
            _oldBitmap = IntPtr.Zero;
            _memWidth = 0;
            _memHeight = 0;
        }
    }

    public void Dispose()
    {
        CleanupGdiResources();
        GC.SuppressFinalize(this);
    }
}
