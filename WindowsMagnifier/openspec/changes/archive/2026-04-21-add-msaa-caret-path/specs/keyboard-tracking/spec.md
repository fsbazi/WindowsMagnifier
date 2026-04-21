## MODIFIED Requirements

### Requirement: 键盘输入位置跟随

当用户在前台应用中键入字符时，放大镜 SHALL 尝试获取该应用当前光标/插入点的屏幕位置，并以该位置作为键盘跟随模式的跟踪点。

获取位置的策略 SHALL 遵循以下优先级：
1. **Win32 `GetGUIThreadInfo`** — 适用于使用 `CreateCaret` 的传统控件（记事本、WinForms `TextBox` 等）
2. **MSAA `OBJID_CARET`**（`oleacc.dll AccessibleObjectFromWindow`）— 适用于 Chromium/Electron 应用（QQ、Telegram、VS Code、Discord、Slack、Teams、Electron apps）。该路径返回精确 caret 矩形（典型 `1×18` 像素），是 Windows 官方放大镜跟随此类应用的主要机制
3. **UI Automation**（`System.Windows.Automation`）— 适用于 UWP / WinUI 等使用原生 UIA 树的现代应用
4. 三条路径都失败则不更新位置，保持当前跟踪点不变

#### Scenario: 记事本中键入字符
- **WHEN** 用户在记事本中按下字母键
- **THEN** `TryGetCaretViaWin32` SHALL 成功返回光标屏幕坐标
- **AND** 放大镜跟踪点 SHALL 切换到该坐标所在位置

#### Scenario: QQ / Telegram 等 Chromium/Electron 应用键入字符
- **WHEN** 用户在此类应用的输入框按下字母键
- **AND** Win32 caret 路径返回失败（Chromium 不使用系统 caret）
- **THEN** 系统 SHALL 走 MSAA `OBJID_CARET` 路径尝试获取精确光标矩形
- **AND** 若该路径返回非零矩形 `(l, t, w, h)`, 跟踪点 SHALL 设为 `(l, t + h/2)`
- **AND** `debug.log` SHALL 出现 `MSAA: OK @l,t size=WxH` 节流记录

#### Scenario: UWP / WinUI 应用键入字符
- **WHEN** 用户在此类应用的文本输入区按下字母键
- **AND** Win32 caret 路径返回失败
- **AND** MSAA `OBJID_CARET` 路径也返回失败（应用不暴露 MSAA caret）
- **THEN** 系统 SHALL 走 UI Automation 路径尝试通过 TextPattern 或 BoundingRect 获取位置

#### Scenario: 三条路径均失败
- **WHEN** 前台应用 Win32 caret 查不到、MSAA OBJID_CARET 返回零矩形、UIA 也未能返回有效位置
- **THEN** 放大镜 SHALL 保持当前跟踪点不变（既不切到键盘模式也不回到鼠标模式）


### Requirement: UIA BoundingRect 方法的"输入框形态"判据

当 UIA 方法 A（TextPattern）失败、fall through 到方法 B（`BoundingRectangle`）时，系统 SHALL 以"输入框形态判据"过滤 BoundingRect：只有看起来像输入行 / 多行编辑框的矩形才被采纳为 caret，容器级矩形 SHALL 被拒绝。

判据: `rect.Width / primaryScreenWidth < 0.60` **AND** `rect.Height / primaryScreenHeight < 0.30`

此要求替代了原先的"宽或高 > 主屏 95%"判据（更早版本为"面积 > 主屏面积 × 0.5"）。原判据对 Chromium 的 WebView 容器（典型 `974×804` 或 `1060×797`，占主屏 50–55% × 74–75%）放行，产生"假成功 定死容器中心"现象。新判据将其识别为容器并拒绝。

#### Scenario: Chromium WebView 容器被拒绝
- **WHEN** UIA 方法 B 的 `BoundingRectangle` 返回 `1060 × 797`（QQ 聊天 WebView）
- **AND** 主屏为 `1920 × 1080`
- **THEN** 系统 SHALL 视其为容器（55% × 74%, 高比例 > 30%），拒绝采纳为 caret
- **AND** `debug.log` SHALL 记录 `UIA: BoundingRect too large (1060x797) - not input-shaped`
- **AND** `TryGetCaretViaUIAutomation` SHALL 返回 `false`

#### Scenario: 典型单行输入框被接受
- **WHEN** UIA 方法 B 的 `BoundingRectangle` 返回 `400 × 50`
- **AND** 主屏为 `1920 × 1080`
- **THEN** 系统 SHALL 判定为"输入框形态"（21% × 4.6%），采纳 `(rect.Left, rect.Top + rect.Height / 2)` 为跟踪点
- **AND** `debug.log` SHALL 记录 `UIA: OK via BoundingRect @... size=400x50`


### Requirement: 跟踪路径的可观测性

`TrackingManager` 中所有异常路径、状态转移和成功路径分支 SHALL 通过 `LogService.LogDebug("Tracking", ...)` 写入 `%APPDATA%\WindowsMagnifier\debug.log`, 而 NOT 使用 `System.Diagnostics.Debug.WriteLine`。

节流 SHALL 按**消息类别 key** 独立分桶（`ConcurrentDictionary<string, long>`），每个 key 独立享受 2 秒节流窗口。**不同类别的消息不得互相饥饿**。此要求直接针对 2026-04-21 发现的 bug: 原实现所有消息共用 `_lastUiaFailLogTicks` 字段，高频的 `Win32: no caret` 每秒 2–3 次刷新字段，导致 UIA 失败日志 100% 被吞，故障诊断不可能。

#### Scenario: 不同类别消息并行节流
- **WHEN** `Win32: no caret` 和 `UIA: TextPattern no selection` 在同一秒内触发
- **THEN** 两条消息 SHALL 各自独立写入 `debug.log`（只要各自 key 未在节流窗口内）
- **AND** 不因为某一类消息频繁触发而阻止另一类消息的记录

#### Scenario: OnKeyPressed 入口可追溯
- **WHEN** 键盘钩子回调 `OnKeyPressed` 被触发（任意前台应用、任意按键）
- **THEN** `debug.log` SHALL 出现 `Key pressed (hook OK)` 节流记录（key=`kb_hit`, 最多每 2 秒一次）
- 此日志用于故障诊断时快速确认"全局键盘钩子是否在工作"

#### Scenario: 各路径成功分支可追溯
- **WHEN** Win32 / MSAA / UIA 任一路径成功返回 caret 位置
- **THEN** `debug.log` SHALL 出现相应路径的 `OK` 记录（`msaa_ok` / `uia_ok_sel` / `uia_ok_vis` / `uia_ok_bnd`），包含获取到的坐标和矩形尺寸
- 此日志用于故障诊断时快速区分"哪一条路径正在工作"和"回退链是否健康"
