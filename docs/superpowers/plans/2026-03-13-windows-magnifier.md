# Windows 桌面放大镜应用实施计划

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 开发一款 Windows 桌面放大镜应用，支持多显示器、多种跟随模式、可调节放大倍数。

**Architecture:** WPF 应用程序，每个显示器一个放大镜窗口实例。核心服务层处理屏幕捕获、放大渲染、跟踪逻辑。显示器焦点管理器控制活动/非活动状态切换。

**Tech Stack:** C#, WPF, .NET 8, System.Drawing, UI Automation

---

## 文件结构

```
WindowsMagnifier/
├── WindowsMagnifier.csproj                 # 项目文件
├── App.xaml                                # 应用入口 XAML
├── App.xaml.cs                             # 应用入口逻辑
├── MainWindow.xaml                         # 放大镜窗口 XAML
├── MainWindow.xaml.cs                      # 放大镜窗口逻辑
├── SettingsWindow.xaml                     # 设置窗口 XAML
├── SettingsWindow.xaml.cs                  # 设置窗口逻辑
├── Services/
│   ├── ConfigService.cs                    # 配置持久化服务
│   ├── DisplayManager.cs                   # 多显示器管理
│   ├── DisplayFocusManager.cs              # 显示器焦点管理
│   ├── ScreenCaptureService.cs             # 屏幕捕获服务
│   ├── MagnifierRenderer.cs                # 放大渲染服务
│   ├── TrackingManager.cs                  # 跟随模式管理
│   ├── MouseHook.cs                        # 全局鼠标钩子
│   └── KeyboardHook.cs                     # 全局键盘钩子
├── Models/
│   ├── AppSettings.cs                      # 配置数据模型
│   └── DisplayInfo.cs                      # 显示器信息模型
└── Resources/
    └── tray-icon.ico                       # 托盘图标
```

---

## Chunk 1: 项目初始化与基础模型

### Task 1: 创建 WPF 项目

**Files:**
- Create: `WindowsMagnifier/WindowsMagnifier.csproj`

- [ ] **Step 1: 创建项目目录**

```bash
mkdir -p WindowsMagnifier/Services WindowsMagnifier/Models WindowsMagnifier/Resources
```

- [ ] **Step 2: 创建项目文件**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <ApplicationIcon>Resources\tray-icon.ico</ApplicationIcon>
    <AssemblyName>WindowsMagnifier</AssemblyName>
    <RootNamespace>WindowsMagnifier</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.Text.Json" Version="8.0.0" />
    <PackageReference Include="UIAutomationClient" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="System.Windows.Automation" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: 验证项目可编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add WindowsMagnifier/WindowsMagnifier.csproj
git commit -m "feat: initialize WPF project structure"
```

---

### Task 2: 创建配置数据模型

**Files:**
- Create: `WindowsMagnifier/Models/AppSettings.cs`

- [ ] **Step 1: 创建 AppSettings 模型**

```csharp
namespace WindowsMagnifier.Models;

/// <summary>
/// 应用配置数据模型
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 放大倍数 (1-9)
    /// </summary>
    public int MagnificationLevel { get; set; } = 2;

    /// <summary>
    /// 放大镜窗口高度（像素）
    /// </summary>
    public int WindowHeight { get; set; } = 150;

    /// <summary>
    /// 是否跟随鼠标指针
    /// </summary>
    public bool FollowMouse { get; set; } = true;

    /// <summary>
    /// 是否跟随键盘输入
    /// </summary>
    public bool FollowKeyboardInput { get; set; } = true;

    /// <summary>
    /// 是否跟随键盘焦点
    /// </summary>
    public bool FollowKeyboardFocus { get; set; } = true;

    /// <summary>
    /// 键盘活动超时（秒），超时后恢复鼠标跟随
    /// </summary>
    public int KeyboardActivityTimeout { get; set; } = 2;

    /// <summary>
    /// 启动后是否最小化
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// 显示器切换延迟（毫秒）
    /// </summary>
    public int DisplaySwitchDelay { get; set; } = 100;

    /// <summary>
    /// 返回默认配置
    /// </summary>
    public static AppSettings CreateDefault() => new AppSettings();
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add WindowsMagnifier/Models/AppSettings.cs
git commit -m "feat: add AppSettings model"
```

---

### Task 3: 创建显示器信息模型

**Files:**
- Create: `WindowsMagnifier/Models/DisplayInfo.cs`

- [ ] **Step 1: 创建 DisplayInfo 模型**

```csharp
using System.Windows;

namespace WindowsMagnifier.Models;

/// <summary>
/// 显示器信息模型
/// </summary>
public class DisplayInfo
{
    /// <summary>
    /// 显示器设备名称
    /// </summary>
    public string DeviceName { get; }

    /// <summary>
    /// 显示器边界（屏幕坐标）
    /// </summary>
    public Rect Bounds { get; }

    /// <summary>
    /// 是否为主显示器
    /// </summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// 显示器索引
    /// </summary>
    public int Index { get; }

    public DisplayInfo(string deviceName, Rect bounds, bool isPrimary, int index)
    {
        DeviceName = deviceName;
        Bounds = bounds;
        IsPrimary = isPrimary;
        Index = index;
    }

    /// <summary>
    /// 获取放大镜窗口应停靠的位置（显示器顶部）
    /// </summary>
    public Rect GetMagnifierWindowRect(int windowHeight)
    {
        return new Rect(
            Bounds.X,
            Bounds.Y,
            Bounds.Width,
            windowHeight
        );
    }
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add WindowsMagnifier/Models/DisplayInfo.cs
git commit -m "feat: add DisplayInfo model"
```

---

### Task 4: 创建配置服务

**Files:**
- Create: `WindowsMagnifier/Services/ConfigService.cs`

- [ ] **Step 1: 创建 ConfigService**

```csharp
using System.IO;
using System.Text.Json;
using WindowsMagnifier.Models;

namespace WindowsMagnifier.Services;

/// <summary>
/// 配置持久化服务
/// </summary>
public class ConfigService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowsMagnifier",
        "config.json"
    );

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 加载配置，如果不存在则返回默认配置
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return AppSettings.CreateDefault();
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.CreateDefault();
        }
        catch
        {
            return AppSettings.CreateDefault();
        }
    }

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    public void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // 忽略保存错误，不影响应用运行
        }
    }
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add WindowsMagnifier/Services/ConfigService.cs
git commit -m "feat: add ConfigService for settings persistence"
```

---

## Chunk 2: 核心服务层 - 输入钩子

### Task 5: 创建全局鼠标钩子

**Files:**
- Create: `WindowsMagnifier/Services/MouseHook.cs`

- [ ] **Step 1: 创建 MouseHook**

```csharp
using System.Runtime.InteropServices;

namespace WindowsMagnifier.Services;

/// <summary>
/// 全局鼠标钩子
/// </summary>
public class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int X;
        public int Y;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelMouseProc _proc;

    public event Action<int, int>? MouseMoved;

    public MouseHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule?.ModuleName ?? string.Empty), 0);
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_MOUSEMOVE)
        {
            var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            MouseMoved?.Invoke(hookStruct.X, hookStruct.Y);
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add WindowsMagnifier/Services/MouseHook.cs
git commit -m "feat: add global mouse hook"
```

---

### Task 6: 创建全局键盘钩子

**Files:**
- Create: `WindowsMagnifier/Services/KeyboardHook.cs`

- [ ] **Step 1: 创建 KeyboardHook**

```csharp
using System.Runtime.InteropServices;

namespace WindowsMagnifier.Services;

/// <summary>
/// 全局键盘钩子
/// </summary>
public class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;

    /// <summary>
    /// 键盘按键事件
    /// </summary>
    public event Action<int>? KeyPressed;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule?.ModuleName ?? string.Empty), 0);
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam.ToInt32() == WM_KEYDOWN || wParam.ToInt32() == WM_SYSKEYDOWN))
        {
            var vkCode = Marshal.ReadInt32(lParam);
            KeyPressed?.Invoke(vkCode);
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add WindowsMagnifier/Services/KeyboardHook.cs
git commit -m "feat: add global keyboard hook"
```

---

## Chunk 3: 核心服务层 - 屏幕捕获与渲染

### Task 7: 创建屏幕捕获服务

**Files:**
- Create: `WindowsMagnifier/Services/ScreenCaptureService.cs`

- [ ] **Step 1: 创建 ScreenCaptureService**

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WindowsMagnifier.Services;

/// <summary>
/// 屏幕捕获服务
/// </summary>
public class ScreenCaptureService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr BitBlt(IntPtr hDestDC, int x, int y, int nWidth, int nHeight, IntPtr hSrcDC, int xSrc, int ySrc, int dwRop);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    private const int SRCCOPY = 0x00CC0020;

    private Bitmap? _captureBitmap;
    private Graphics? _captureGraphics;

    /// <summary>
    /// 捕获指定区域的屏幕内容
    /// </summary>
    /// <param name="region">捕获区域</param>
    /// <returns>BitmapSource 用于 WPF 显示</returns>
    public BitmapSource? CaptureRegion(Rect region)
    {
        try
        {
            var width = (int)region.Width;
            var height = (int)region.Height;

            if (width <= 0 || height <= 0)
                return null;

            // 确保位图大小正确
            if (_captureBitmap == null || _captureBitmap.Width != width || _captureBitmap.Height != height)
            {
                _captureBitmap?.Dispose();
                _captureGraphics?.Dispose();

                _captureBitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                _captureGraphics = Graphics.FromImage(_captureBitmap);
            }

            // 使用 BitBlt 快速复制屏幕内容
            var hdcDest = _captureGraphics.GetHdc();
            var hdcSrc = GetDC(GetDesktopWindow());

            BitBlt(hdcDest, 0, 0, width, height, hdcSrc, (int)region.X, (int)region.Y, SRCCOPY);

            ReleaseDC(GetDesktopWindow(), hdcSrc);
            _captureGraphics.ReleaseHdc(hdcDest);

            // 转换为 BitmapSource
            var hBitmap = _captureBitmap.GetHbitmap();
            try
            {
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(width, height)
                );
                bitmapSource.Freeze();
                return bitmapSource;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public void Dispose()
    {
        _captureGraphics?.Dispose();
        _captureBitmap?.Dispose();
        GC.SuppressFinalize(this);
    }
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add WindowsMagnifier/Services/ScreenCaptureService.cs
git commit -m "feat: add screen capture service"
```

---

### Task 8: 创建放大渲染服务

**Files:**
- Create: `WindowsMagnifier/Services/MagnifierRenderer.cs`

- [ ] **Step 1: 创建 MagnifierRenderer**

```csharp
using System.Windows;

namespace WindowsMagnifier.Services;

/// <summary>
/// 放大渲染服务 - 计算捕获区域
/// </summary>
public class MagnifierRenderer
{
    /// <summary>
    /// 计算屏幕捕获区域
    /// </summary>
    /// <param name="focusPoint">焦点位置（鼠标/键盘位置）</param>
    /// <param name="windowWidth">放大镜窗口宽度</param>
    /// <param name="windowHeight">放大镜窗口高度</param>
    /// <param name="magnification">放大倍数</param>
    /// <returns>应该捕获的屏幕区域</returns>
    public Rect CalculateCaptureRegion(Point focusPoint, double windowWidth, double windowHeight, int magnification)
    {
        // 计算捕获区域大小（窗口大小 / 放大倍数）
        var captureWidth = windowWidth / magnification;
        var captureHeight = windowHeight / magnification;

        // 以焦点为中心计算捕获区域位置
        var captureX = focusPoint.X - captureWidth / 2;
        var captureY = focusPoint.Y - captureHeight / 2;

        return new Rect(captureX, captureY, captureWidth, captureHeight);
    }

    /// <summary>
    /// 根据显示器边界裁剪捕获区域，防止越界
    /// </summary>
    public Rect ClampCaptureRegion(Rect captureRegion, Rect displayBounds)
    {
        var x = Math.Max(displayBounds.Left, Math.Min(displayBounds.Right - captureRegion.Width, captureRegion.X));
        var y = Math.Max(displayBounds.Top, Math.Min(displayBounds.Bottom - captureRegion.Height, captureRegion.Y));

        return new Rect(x, y, captureRegion.Width, captureRegion.Height);
    }
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add WindowsMagnifier/Services/MagnifierRenderer.cs
git commit -m "feat: add magnifier renderer service"
```

---

## Chunk 4: 核心服务层 - 管理器

### Task 9: 创建多显示器管理服务

**Files:**
- Create: `WindowsMagnifier/Services/DisplayManager.cs`

- [ ] **Step 1: 创建 DisplayManager**

```csharp
using System.Windows;
using System.Windows.Forms;
using WindowsMagnifier.Models;

namespace WindowsMagnifier.Services;

/// <summary>
/// 多显示器管理服务
/// </summary>
public class DisplayManager
{
    /// <summary>
    /// 获取所有显示器信息
    /// </summary>
    public List<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();
        var screens = Screen.AllScreens;

        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            displays.Add(new DisplayInfo(
                screen.DeviceName,
                new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height),
                screen.Primary,
                i
            ));
        }

        return displays;
    }

    /// <summary>
    /// 根据坐标判断鼠标在哪个显示器上
    /// </summary>
    public DisplayInfo? GetDisplayFromPoint(Point point)
    {
        var displays = GetDisplays();

        foreach (var display in displays)
        {
            if (display.Bounds.Contains(point))
            {
                return display;
            }
        }

        return displays.FirstOrDefault();
    }

    /// <summary>
    /// 监听显示器变化事件
    /// </summary>
    public event Action? DisplaysChanged;

    public DisplayManager()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        DisplaysChanged?.Invoke();
    }
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add WindowsMagnifier/Services/DisplayManager.cs
git commit -m "feat: add display manager service"
```

---

### Task 10: 创建显示器焦点管理器

**Files:**
- Create: `WindowsMagnifier/Services/DisplayFocusManager.cs`

- [ ] **Step 1: 创建 DisplayFocusManager**

```csharp
using System.Windows;
using WindowsMagnifier.Models;

namespace WindowsMagnifier.Services;

/// <summary>
/// 显示器焦点管理器 - 管理活动/非活动显示器状态
/// </summary>
public class DisplayFocusManager
{
    private readonly DisplayManager _displayManager;
    private readonly int _switchDelayMs;

    private DisplayInfo? _activeDisplay;
    private System.Timers.Timer? _switchTimer;
    private DisplayInfo? _pendingDisplay;

    /// <summary>
    /// 当前活动的显示器
    /// </summary>
    public DisplayInfo? ActiveDisplay => _activeDisplay;

    /// <summary>
    /// 活动显示器变化事件
    /// </summary>
    public event Action<DisplayInfo?>? ActiveDisplayChanged;

    public DisplayFocusManager(DisplayManager displayManager, int switchDelayMs = 100)
    {
        _displayManager = displayManager;
        _switchDelayMs = switchDelayMs;
    }

    /// <summary>
    /// 根据鼠标位置更新活动显示器
    /// </summary>
    public void UpdateFromMousePosition(Point mousePosition)
    {
        var display = _displayManager.GetDisplayFromPoint(mousePosition);

        if (display == null) return;

        // 如果鼠标在同一个显示器上，不做处理
        if (_activeDisplay != null && _activeDisplay.DeviceName == display.DeviceName)
        {
            _pendingDisplay = null;
            _switchTimer?.Stop();
            return;
        }

        // 如果已经有切换计时器在运行，检查是否是同一个待切换显示器
        if (_switchTimer != null && _switchTimer.Enabled)
        {
            if (_pendingDisplay?.DeviceName == display.DeviceName)
                return;
        }

        // 开始延迟切换
        _pendingDisplay = display;
        StartSwitchTimer();
    }

    private void StartSwitchTimer()
    {
        _switchTimer?.Stop();
        _switchTimer = new System.Timers.Timer(_switchDelayMs);
        _switchTimer.Elapsed += OnSwitchTimerElapsed;
        _switchTimer.AutoReset = false;
        _switchTimer.Start();
    }

    private void OnSwitchTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_pendingDisplay == null) return;

        var previousDisplay = _activeDisplay;
        _activeDisplay = _pendingDisplay;
        _pendingDisplay = null;

        // 在 UI 线程上触发事件
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            ActiveDisplayChanged?.Invoke(_activeDisplay);
        });
    }

    /// <summary>
    /// 初始化，设置默认活动显示器
    /// </summary>
    public void Initialize()
    {
        var displays = _displayManager.GetDisplays();
        _activeDisplay = displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault();
    }
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add WindowsMagnifier/Services/DisplayFocusManager.cs
git commit -m "feat: add display focus manager"
```

---

### Task 11: 创建跟随模式管理器

**Files:**
- Create: `WindowsMagnifier/Services/TrackingManager.cs`

- [ ] **Step 1: 创建 TrackingManager**

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using WindowsMagnifier.Models;

namespace WindowsMagnifier.Services;

/// <summary>
/// 跟随模式管理器 - 管理鼠标/键盘跟随优先级
/// </summary>
public class TrackingManager : IDisposable
{
    private readonly AppSettings _settings;
    private readonly MouseHook _mouseHook;
    private readonly KeyboardHook _keyboardHook;
    private readonly System.Timers.Timer _keyboardIdleTimer;

    private TrackingMode _currentMode = TrackingMode.Mouse;
    private Point _currentPosition;

    /// <summary>
    /// 当前跟踪位置
    /// </summary>
    public Point CurrentPosition => _currentPosition;

    /// <summary>
    /// 当前跟踪模式
    /// </summary>
    public TrackingMode CurrentMode => _currentMode;

    /// <summary>
    /// 位置变化事件
    /// </summary>
    public event Action<Point, TrackingMode>? PositionChanged;

    public TrackingManager(AppSettings settings)
    {
        _settings = settings;
        _mouseHook = new MouseHook();
        _keyboardHook = new KeyboardHook();
        _keyboardIdleTimer = new System.Timers.Timer(settings.KeyboardActivityTimeout * 1000);
        _keyboardIdleTimer.AutoReset = false;
        _keyboardIdleTimer.Elapsed += OnKeyboardIdle;

        _mouseHook.MouseMoved += OnMouseMoved;
        _keyboardHook.KeyPressed += OnKeyPressed;
    }

    public void Start()
    {
        if (_settings.FollowMouse)
        {
            _mouseHook.Start();
        }

        if (_settings.FollowKeyboardInput || _settings.FollowKeyboardFocus)
        {
            _keyboardHook.Start();
        }
    }

    public void Stop()
    {
        _mouseHook.Stop();
        _keyboardHook.Stop();
        _keyboardIdleTimer.Stop();
    }

    private void OnMouseMoved(int x, int y)
    {
        // 如果当前是键盘模式且计时器还在运行，忽略鼠标移动
        if (_currentMode == TrackingMode.KeyboardInput && _keyboardIdleTimer.Enabled)
        {
            return;
        }

        // 切换回鼠标模式
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

        // 切换到键盘输入模式
        _currentMode = TrackingMode.KeyboardInput;

        // 获取当前光标位置作为焦点
        if (TryGetCaretPosition(out var caretPos))
        {
            _currentPosition = caretPos;
        }
        else
        {
            // 使用当前鼠标位置作为后备
            _currentPosition = new Point(System.Windows.Forms.Control.MousePosition.X, System.Windows.Forms.Control.MousePosition.Y);
        }

        PositionChanged?.Invoke(_currentPosition, _currentMode);

        // 重置空闲计时器
        _keyboardIdleTimer.Stop();
        _keyboardIdleTimer.Start();
    }

    private void OnKeyboardIdle(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // 键盘空闲超时，切换回鼠标模式
        _currentMode = TrackingMode.Mouse;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCaretPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private bool TryGetCaretPosition(out Point position)
    {
        position = new Point();

        try
        {
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
                            position = new Point(rects[0].Left, rects[0].Top);
                            return true;
                        }
                    }
                }
            }

            // 后备方案：使用 GetCaretPos
            if (GetCaretPos(out var caretPos))
            {
                position = new Point(caretPos.X, caretPos.Y);
                return true;
            }
        }
        catch
        {
            // 忽略错误
        }

        return false;
    }

    public void Dispose()
    {
        _mouseHook.Dispose();
        _keyboardHook.Dispose();
        _keyboardIdleTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 跟踪模式
/// </summary>
public enum TrackingMode
{
    Mouse,
    KeyboardInput,
    KeyboardFocus
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add WindowsMagnifier/Services/TrackingManager.cs
git commit -m "feat: add tracking manager with priority-based mode switching"
```

---

## Chunk 5: UI 层

### Task 12: 创建应用入口

**Files:**
- Create: `WindowsMagnifier/App.xaml`
- Create: `WindowsMagnifier/App.xaml.cs`

- [ ] **Step 1: 创建 App.xaml**

```xml
<Application x:Class="WindowsMagnifier.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: 创建 App.xaml.cs**

```csharp
using System.Windows;

namespace WindowsMagnifier;

public partial class App : Application
{
}
```

- [ ] **Step 3: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add WindowsMagnifier/App.xaml WindowsMagnifier/App.xaml.cs
git commit -m "feat: add app entry point"
```

---

### Task 13: 创建放大镜窗口

**Files:**
- Create: `WindowsMagnifier/MainWindow.xaml`
- Create: `WindowsMagnifier/MainWindow.xaml.cs`

- [ ] **Step 1: 创建 MainWindow.xaml**

```xml
<Window x:Class="WindowsMagnifier.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="放大镜"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Width="800" Height="150">

    <Grid x:Name="MainGrid" Background="Black">
        <!-- 放大内容显示区域 -->
        <Image x:Name="MagnifiedImage"
               Stretch="Fill"
               HorizontalAlignment="Stretch"
               VerticalAlignment="Stretch"/>

        <!-- 非活动状态遮罩 -->
        <Grid x:Name="InactiveOverlay" Background="Black" Visibility="Collapsed"/>

        <!-- 高度调整手柄 -->
        <Grid x:Name="ResizeHandle"
              Height="5"
              VerticalAlignment="Bottom"
              Background="Transparent"
              Cursor="SizeNS"
              MouseLeftButtonDown="ResizeHandle_MouseLeftButtonDown"
              MouseMove="ResizeHandle_MouseMove"
              MouseLeftButtonUp="ResizeHandle_MouseLeftButtonUp">
            <Grid.Background>
                <SolidColorBrush Color="Gray" Opacity="0.3"/>
            </Grid.Background>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: 创建 MainWindow.xaml.cs**

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WindowsMagnifier.Models;
using WindowsMagnifier.Services;

namespace WindowsMagnifier;

/// <summary>
/// 放大镜窗口 - 每个显示器一个实例
/// </summary>
public partial class MainWindow : Window
{
    private readonly DisplayInfo _display;
    private readonly AppSettings _settings;
    private readonly ScreenCaptureService _captureService;
    private readonly MagnifierRenderer _renderer;
    private readonly System.Windows.Threading.DispatcherTimer _renderTimer;

    private bool _isActive;
    private bool _isResizing;
    private double _resizeStartY;

    public DisplayInfo Display => _display;

    public MainWindow(DisplayInfo display, AppSettings settings)
    {
        InitializeComponent();

        _display = display;
        _settings = settings;
        _captureService = new ScreenCaptureService();
        _renderer = new MagnifierRenderer();

        // 设置窗口位置
        UpdateWindowPosition();

        // 渲染计时器
        _renderTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS
        };
        _renderTimer.Tick += RenderTimer_Tick;

        // 初始状态为非活动
        SetActive(false);
    }

    private void UpdateWindowPosition()
    {
        var rect = _display.GetMagnifierWindowRect(_settings.WindowHeight);
        Left = rect.X;
        Top = rect.Y;
        Width = rect.Width;
        Height = rect.Height;
    }

    public void SetActive(bool active)
    {
        _isActive = active;

        if (active)
        {
            InactiveOverlay.Visibility = Visibility.Collapsed;
            _renderTimer.Start();
        }
        else
        {
            InactiveOverlay.Visibility = Visibility.Visible;
            MagnifiedImage.Source = null;
            _renderTimer.Stop();
        }
    }

    public void UpdatePosition(Point focusPoint)
    {
        if (!_isActive) return;

        var captureRegion = _renderer.CalculateCaptureRegion(
            focusPoint,
            Width,
            Height,
            _settings.MagnificationLevel
        );

        var clampedRegion = _renderer.ClampCaptureRegion(captureRegion, _display.Bounds);

        var bitmap = _captureService.CaptureRegion(clampedRegion);
        if (bitmap != null)
        {
            MagnifiedImage.Source = bitmap;
        }
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        // 由外部 TrackingManager 驱动位置更新
        // 这里只处理定时刷新（如果需要）
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

        // 限制高度范围
        newHeight = Math.Max(50, Math.Min(300, newHeight));

        Height = newHeight;
        _resizeStartY = currentY;
    }

    private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isResizing = false;
        ResizeHandle.ReleaseMouseCapture();

        // 通知设置更新
        _settings.WindowHeight = (int)Height;
    }

    public new void Close()
    {
        _renderTimer.Stop();
        _captureService.Dispose();
        base.Close();
    }
}
```

- [ ] **Step 3: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add WindowsMagnifier/MainWindow.xaml WindowsMagnifier/MainWindow.xaml.cs
git commit -m "feat: add magnifier main window"
```

---

### Task 14: 创建设置窗口

**Files:**
- Create: `WindowsMagnifier/SettingsWindow.xaml`
- Create: `WindowsMagnifier/SettingsWindow.xaml.cs`

- [ ] **Step 1: 创建 SettingsWindow.xaml**

```xml
<Window x:Class="WindowsMagnifier.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="放大镜设置"
        Width="400" Height="450"
        WindowStartupLocation="CenterScreen"
        ResizeMode="NoResize">

    <Grid Margin="20">
        <StackPanel>
            <TextBlock Text="放大镜设置" FontSize="18" FontWeight="Bold" Margin="0,0,0,20"/>

            <!-- 放大倍数 -->
            <TextBlock Text="放大倍数" FontWeight="SemiBold" Margin="0,0,0,5"/>
            <Grid Margin="0,0,0,15">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <Slider x:Name="MagnificationSlider"
                        Minimum="1" Maximum="9"
                        TickFrequency="1"
                        IsSnapToTickEnabled="True"
                        ValueChanged="MagnificationSlider_ValueChanged"/>
                <TextBlock x:Name="MagnificationValue"
                           Grid.Column="1"
                           Text="2x"
                           Width="40"
                           TextAlignment="Right"
                           VerticalAlignment="Center"
                           Margin="10,0,0,0"/>
            </Grid>

            <!-- 窗口高度 -->
            <TextBlock Text="窗口高度（像素）" FontWeight="SemiBold" Margin="0,0,0,5"/>
            <Grid Margin="0,0,0,15">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <Slider x:Name="HeightSlider"
                        Minimum="50" Maximum="300"
                        TickFrequency="10"
                        IsSnapToTickEnabled="True"
                        ValueChanged="HeightSlider_ValueChanged"/>
                <TextBlock x:Name="HeightValue"
                           Grid.Column="1"
                           Text="150"
                           Width="40"
                           TextAlignment="Right"
                           VerticalAlignment="Center"
                           Margin="10,0,0,0"/>
            </Grid>

            <!-- 跟随模式 -->
            <TextBlock Text="跟随模式" FontWeight="SemiBold" Margin="0,0,0,5"/>
            <StackPanel Margin="0,0,0,15">
                <CheckBox x:Name="FollowMouseCheckBox"
                          Content="跟随鼠标指针"
                          Checked="FollowMode_Changed"
                          Unchecked="FollowMode_Changed"/>
                <CheckBox x:Name="FollowKeyboardInputCheckBox"
                          Content="跟随键盘输入"
                          Checked="FollowMode_Changed"
                          Unchecked="FollowMode_Changed"/>
                <CheckBox x:Name="FollowKeyboardFocusCheckBox"
                          Content="跟随键盘焦点"
                          Checked="FollowMode_Changed"
                          Unchecked="FollowMode_Changed"/>
            </StackPanel>

            <!-- 键盘活动超时 -->
            <TextBlock Text="键盘活动超时（秒）" FontWeight="SemiBold" Margin="0,0,0,5"/>
            <Grid Margin="0,0,0,15">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <Slider x:Name="KeyboardTimeoutSlider"
                        Minimum="1" Maximum="10"
                        TickFrequency="1"
                        IsSnapToTickEnabled="True"
                        ValueChanged="KeyboardTimeoutSlider_ValueChanged"/>
                <TextBlock x:Name="KeyboardTimeoutValue"
                           Grid.Column="1"
                           Text="2"
                           Width="40"
                           TextAlignment="Right"
                           VerticalAlignment="Center"
                           Margin="10,0,0,0"/>
            </Grid>

            <!-- 启动后最小化 -->
            <CheckBox x:Name="StartMinimizedCheckBox"
                      Content="启动后最小化"
                      Checked="StartMinimized_Changed"
                      Unchecked="StartMinimized_Changed"
                      Margin="0,0,0,20"/>

            <!-- 按钮 -->
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Content="确定"
                        Width="80"
                        Click="OkButton_Click"
                        Margin="0,0,10,0"/>
                <Button Content="取消"
                        Width="80"
                        Click="CancelButton_Click"/>
            </StackPanel>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 2: 创建 SettingsWindow.xaml.cs**

```csharp
using System.Windows;
using WindowsMagnifier.Models;
using WindowsMagnifier.Services;

namespace WindowsMagnifier;

/// <summary>
/// 设置窗口
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ConfigService _configService;

    public SettingsWindow(AppSettings settings, ConfigService configService)
    {
        InitializeComponent();

        _settings = settings;
        _configService = configService;

        // 加载当前设置到 UI
        LoadSettings();
    }

    private void LoadSettings()
    {
        MagnificationSlider.Value = _settings.MagnificationLevel;
        HeightSlider.Value = _settings.WindowHeight;
        FollowMouseCheckBox.IsChecked = _settings.FollowMouse;
        FollowKeyboardInputCheckBox.IsChecked = _settings.FollowKeyboardInput;
        FollowKeyboardFocusCheckBox.IsChecked = _settings.FollowKeyboardFocus;
        KeyboardTimeoutSlider.Value = _settings.KeyboardActivityTimeout;
        StartMinimizedCheckBox.IsChecked = _settings.StartMinimized;
    }

    private void MagnificationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var value = (int)e.NewValue;
        MagnificationValue.Text = $"{value}x";
        _settings.MagnificationLevel = value;
    }

    private void HeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var value = (int)e.NewValue;
        HeightValue.Text = value.ToString();
        _settings.WindowHeight = value;
    }

    private void FollowMode_Changed(object sender, RoutedEventArgs e)
    {
        _settings.FollowMouse = FollowMouseCheckBox.IsChecked ?? false;
        _settings.FollowKeyboardInput = FollowKeyboardInputCheckBox.IsChecked ?? false;
        _settings.FollowKeyboardFocus = FollowKeyboardFocusCheckBox.IsChecked ?? false;
    }

    private void KeyboardTimeoutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var value = (int)e.NewValue;
        KeyboardTimeoutValue.Text = value.ToString();
        _settings.KeyboardActivityTimeout = value;
    }

    private void StartMinimized_Changed(object sender, RoutedEventArgs e)
    {
        _settings.StartMinimized = StartMinimizedCheckBox.IsChecked ?? false;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _configService.Save(_settings);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

- [ ] **Step 3: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add WindowsMagnifier/SettingsWindow.xaml WindowsMagnifier/SettingsWindow.xaml.cs
git commit -m "feat: add settings window"
```

---

## Chunk 6: 应用集成与托盘图标

### Task 15: 集成所有服务到 App

**Files:**
- Modify: `WindowsMagnifier/App.xaml`
- Modify: `WindowsMagnifier/App.xaml.cs`

- [ ] **Step 1: 更新 App.xaml（移除 StartupUri，改为代码启动）**

```xml
<Application x:Class="WindowsMagnifier.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Startup="Application_Startup"
             Exit="Application_Exit">
    <Application.Resources>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: 更新 App.xaml.cs（完整应用逻辑）**

```csharp
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using WindowsMagnifier.Models;
using WindowsMagnifier.Services;

namespace WindowsMagnifier;

public partial class App : Application
{
    private ConfigService? _configService;
    private AppSettings? _settings;
    private DisplayManager? _displayManager;
    private DisplayFocusManager? _displayFocusManager;
    private TrackingManager? _trackingManager;
    private readonly List<MainWindow> _magnifierWindows = new();
    private NotifyIcon? _notifyIcon;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // 初始化配置
        _configService = new ConfigService();
        _settings = _configService.Load();

        // 初始化显示器管理
        _displayManager = new DisplayManager();
        _displayManager.DisplaysChanged += OnDisplaysChanged;

        // 初始化焦点管理
        _displayFocusManager = new DisplayFocusManager(_displayManager, _settings.DisplaySwitchDelay);
        _displayFocusManager.ActiveDisplayChanged += OnActiveDisplayChanged;
        _displayFocusManager.Initialize();

        // 初始化跟踪管理
        _trackingManager = new TrackingManager(_settings);
        _trackingManager.PositionChanged += OnPositionChanged;
        _trackingManager.Start();

        // 创建托盘图标
        CreateNotifyIcon();

        // 创建放大镜窗口
        CreateMagnifierWindows();

        // 显示设置窗口（如果不是最小化启动）
        if (!_settings.StartMinimized)
        {
            ShowAllWindows();
        }
    }

    private void CreateNotifyIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Windows 放大镜",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };

        _notifyIcon.ContextMenuStrip.Items.Add("显示", null, (s, e) => ShowAllWindows());
        _notifyIcon.ContextMenuStrip.Items.Add("设置", null, (s, e) => ShowSettingsWindow());
        _notifyIcon.ContextMenuStrip.Items.Add("-");
        _notifyIcon.ContextMenuStrip.Items.Add("退出", null, (s, e) => Shutdown());

        _notifyIcon.DoubleClick += (s, e) => ShowAllWindows();
    }

    private void CreateMagnifierWindows()
    {
        var displays = _displayManager.GetDisplays();

        foreach (var display in displays)
        {
            var window = new MainWindow(display, _settings!);
            _magnifierWindows.Add(window);
        }

        // 设置初始活动状态
        if (_displayFocusManager?.ActiveDisplay != null)
        {
            UpdateActiveDisplay(_displayFocusManager.ActiveDisplay);
        }
    }

    private void ShowAllWindows()
    {
        foreach (var window in _magnifierWindows)
        {
            window.Show();
        }
    }

    private void ShowSettingsWindow()
    {
        var settingsWindow = new SettingsWindow(_settings!, _configService!);
        settingsWindow.Owner = _magnifierWindows.FirstOrDefault();
        settingsWindow.ShowDialog();
    }

    private void OnDisplaysChanged()
    {
        // 显示器配置变化，重建窗口
        Current.Dispatcher.Invoke(() =>
        {
            foreach (var window in _magnifierWindows)
            {
                window.Close();
            }
            _magnifierWindows.Clear();

            CreateMagnifierWindows();
            ShowAllWindows();
        });
    }

    private void OnActiveDisplayChanged(DisplayInfo? activeDisplay)
    {
        Current.Dispatcher.Invoke(() =>
        {
            UpdateActiveDisplay(activeDisplay);
        });
    }

    private void UpdateActiveDisplay(DisplayInfo? activeDisplay)
    {
        foreach (var window in _magnifierWindows)
        {
            var isActive = window.Display.DeviceName == activeDisplay?.DeviceName;
            window.SetActive(isActive);
        }
    }

    private void OnPositionChanged(System.Windows.Point position, TrackingMode mode)
    {
        _displayFocusManager?.UpdateFromMousePosition(position);

        Current.Dispatcher.Invoke(() =>
        {
            foreach (var window in _magnifierWindows.Where(w => w.Display.DeviceName == _displayFocusManager?.ActiveDisplay?.DeviceName))
            {
                window.UpdatePosition(position);
            }
        });
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        // 保存配置
        if (_settings != null && _configService != null)
        {
            _configService.Save(_settings);
        }

        // 清理资源
        _trackingManager?.Dispose();
        _displayManager?.Dispose();

        foreach (var window in _magnifierWindows)
        {
            window.Close();
        }

        _notifyIcon?.Dispose();
    }
}
```

- [ ] **Step 3: 验证编译**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add WindowsMagnifier/App.xaml WindowsMagnifier/App.xaml.cs
git commit -m "feat: integrate all services in App with tray icon support"
```

---

### Task 16: 添加托盘图标资源

**Files:**
- Create: `WindowsMagnifier/Resources/tray-icon.ico` (placeholder)

- [ ] **Step 1: 创建占位图标文件说明**

由于无法直接创建二进制图标文件，暂时使用系统图标。在 App.xaml.cs 中已使用 `SystemIcons.Application`。

如需自定义图标，可后续添加 `.ico` 文件到 `Resources` 目录。

- [ ] **Step 2: Commit**

```bash
git add -A
git commit -m "docs: add note about tray icon resource"
```

---

### Task 17: 最终验证与打包

- [ ] **Step 1: 完整编译验证**

Run: `dotnet build WindowsMagnifier/WindowsMagnifier.csproj --configuration Release`
Expected: Build succeeded

- [ ] **Step 2: 发布为独立应用**

Run: `dotnet publish WindowsMagnifier/WindowsMagnifier.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true`
Expected: Publish succeeded

- [ ] **Step 3: 验证输出**

Run: `ls WindowsMagnifier/bin/Release/net8.0-windows/win-x64/publish/`
Expected: WindowsMagnifier.exe exists

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: complete Windows Magnifier application"
```

---

## 完成检查清单

- [ ] 所有代码编译通过
- [ ] 发布成功生成可执行文件
- [ ] 功能验证：
  - [ ] 多显示器检测正常
  - [ ] 放大镜窗口正确停靠在各显示器顶部
  - [ ] 鼠标跟随正常工作
  - [ ] 活动显示器切换正常
  - [ ] 非活动显示器显示黑色
  - [ ] 设置窗口可正常保存配置
  - [ ] 托盘图标正常工作