# 眼眸 / EyeMo

[中文](#中文) | [English](#english)

---

## 中文

### 简介

眼眸是一款专为**弱视人群**设计的桌面放大辅助工具，将屏幕内容实时放大显示在屏幕顶部，帮助视力障碍用户更轻松地使用电脑。

与 Windows 系统自带放大镜相比，本软件最大的优势在于**原生支持多显示器**——每个显示器拥有独立的放大镜窗口，并且可以为每个显示器单独设置放大倍数，满足不同使用场景的需求。

### 设置界面

![眼眸设置界面](WindowsMagnifier/settings-screenshot.png)

### 主要特性

- **多显示器支持** — 每个显示器顶部独立显示，互不干扰
- **独立放大倍数** — 每个显示器可单独设置 1x–16x 放大倍数
- **硬件加速渲染** — 基于 Windows Magnification API，流畅无延迟
- **跟随鼠标指针** — 放大区域实时跟随鼠标位置
- **跟随键盘输入** — 打字时自动跟随光标位置
- **可调节窗口高度** — 拖拽底部边框自由调整放大区域高度
- **全局快捷键** — `Win + Alt + M` 一键切换显示/隐藏
- **全屏自动隐藏** — 检测到全屏应用时自动隐藏
- **系统托盘集成** — 支持最小化启动，不占用任务栏空间

### 系统要求

- Windows 10（1607 及以上版本）或 Windows 11
- 32 位（x86）或 64 位（x64）处理器均支持

### 下载安装

前往 [Releases](../../releases) 页面，根据你的操作系统位数选择对应文件：

| 文件名 | 适用系统 |
|--------|---------|
| `眼眸-x64.exe` | 64 位 Windows 10 / 11（主流电脑） |
| `眼眸-x86.exe` | 32 位 Windows 10（老旧电脑） |

**不知道自己是 32 位还是 64 位？**
右键点击「此电脑」→「属性」，查看「系统类型」一栏：
- 显示「64 位操作系统」→ 下载 x64 版本
- 显示「32 位操作系统」→ 下载 x86 版本

下载后**无需安装**，双击 `.exe` 文件即可直接运行。

### 使用方法

1. 运行 `眼眸.exe`，窗口自动出现在每个显示器顶部
2. 移动鼠标，放大区域实时跟随
3. 右键点击窗口，选择**设置**可调整放大倍数和其他选项
4. 拖拽窗口底部边框可调整高度
5. 按 `Win + Alt + M` 可随时隐藏或显示

### 设置说明

| 选项 | 说明 |
|------|------|
| 放大倍数 | 每个显示器独立设置，范围 1x–16x |
| 窗口高度 | 放大区域的像素高度（100–600） |
| 跟随鼠标指针 | 启用后放大区域跟随鼠标移动 |
| 跟随键盘输入 | 启用后打字时跟随文字光标位置 |
| 启动后最小化 | 启动时不显示窗口，仅显示托盘图标 |
| 全屏时自动隐藏 | 检测到全屏应用时自动隐藏 |

### 开源协议

MIT License

---

## English

### Introduction

EyeMo is a desktop accessibility tool designed for people with **low vision**. It magnifies screen content in real time and displays it at the top of each monitor, making it easier for users with visual impairments to use their computer.

Compared to the built-in Windows Magnifier, the key advantage of this application is **native multi-monitor support** — each monitor has its own independent magnifier window, and the magnification level can be configured separately for each display.

### Settings UI

![EyeMo Settings](WindowsMagnifier/settings-screenshot.png)

### Key Features

- **Multi-monitor support** — Independent magnifier window on each monitor
- **Per-monitor zoom level** — Set different magnification (1x–16x) for each display
- **Hardware-accelerated rendering** — Built on the Windows Magnification API for smooth, lag-free performance
- **Mouse tracking** — Magnified area follows the mouse cursor in real time
- **Keyboard/caret tracking** — Automatically follows the text cursor while typing
- **Adjustable window height** — Drag the bottom edge to resize the magnifier area
- **Global hotkey** — `Win + Alt + M` to instantly show or hide
- **Fullscreen auto-hide** — Automatically hides when a fullscreen app is detected
- **System tray support** — Can start minimized without occupying the taskbar

### System Requirements

- Windows 10 (version 1607 or later) or Windows 11
- 32-bit (x86) or 64-bit (x64) processor supported

### Download

Go to the [Releases](../../releases) page and download the file matching your system:

| File | System |
|------|--------|
| `眼眸-x64.exe` | 64-bit Windows 10 / 11 (most modern PCs) |
| `眼眸-x86.exe` | 32-bit Windows 10 (older PCs) |

**Not sure which version you need?**
Right-click "This PC" → "Properties" and check the "System type" field:
- "64-bit operating system" → download the x64 version
- "32-bit operating system" → download the x86 version

No installation required — just double-click the `.exe` file to run.

### How to Use

1. Run `眼眸.exe` — the magnifier window appears at the top of each monitor automatically
2. Move your mouse and the magnified area follows in real time
3. Right-click the window and select **Settings** to adjust zoom level and other options
4. Drag the bottom edge of the window to change the height
5. Press `Win + Alt + M` at any time to hide or show

### Settings

| Option | Description |
|--------|-------------|
| Magnification level | Set independently per monitor, range 1x–16x |
| Window height | Height of the magnifier area in pixels (100–600) |
| Follow mouse cursor | Magnified area tracks the mouse position |
| Follow keyboard input | Magnified area tracks the text caret while typing |
| Start minimized | Launch without showing the window |
| Fullscreen auto-hide | Automatically hide when a fullscreen app is detected |

### License

MIT License
