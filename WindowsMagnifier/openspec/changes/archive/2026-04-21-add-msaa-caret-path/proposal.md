## Why

用户报告 QQ 和 Telegram 键盘跟随失效。

根因通过诊断日志锁定: QQ 的 UIA `FocusedElement` 返回整个聊天 WebView 容器（1060×797），不暴露 TextPattern；代码 fall-through 到方法 B `BoundingRectangle`，把容器中心点 `(683, 633)` 作为 caret 返回"假成功"。每次按键都定死到同一点，用户感知为"不跟随"。

初次诊断曾误判"Electron 拿不到 caret 是技术限制"。用户反驳: Windows 官方放大镜能跟 QQ。进一步研究发现: 官方放大镜走的是 **MSAA `OBJID_CARET`**（`oleacc.dll` 的 `AccessibleObjectFromWindow(hwnd, OBJID_CARET, IID_IAccessible, ...)`），Chromium 专门为此通道暴露精确 caret，**我们代码里完全缺这条路径**。

此外发现两个配套 bug：
- **UIA 方法 B 阈值过宽** — 2026-04-21 早些时候引入的 `IsElementNearlyFullScreen`（宽/高 > 95%）本意是挡"整屏控件"，但把 Chromium 的整容器（如 1060×797 = 占主屏 55%×74%）放行，进而产生假成功
- **`LogThrottled` 共享节流字段** — 所有消息共用 `_lastUiaFailLogTicks`，Win32 失败每 2 秒刷新一次字段，**把 UIA 失败日志 100% 饥饿吞掉**，故障时看不到真相（导致诊断成本极高）

## What Changes

- **新增 MSAA `OBJID_CARET` 路径**: 在 `TryGetCaretPosition` 中 Win32 失败后、UIA 之前插入 `TryGetCaretViaMsaa`。调用 `oleacc.dll AccessibleObjectFromWindow(hwnd, OBJID_CARET, IID_IAccessible, out obj)` 获取 caret 的 `IAccessible` COM 对象，通过 IDispatch late binding 调用 `accLocation(out l, out t, out w, out h, 0)` 得到精确 caret 矩形。成功时以 `(l, t + h/2)` 作为跟踪点。accLocation 的 late binding 调用避免了引入 `Accessibility.dll` COM Interop 依赖。
- **收紧 UIA 方法 B 判据**: `IsElementNearlyFullScreen` 重命名为 `LooksLikeInputControl`，判据从"宽/高任一 > 95%"改为"宽 < 60% **AND** 高 < 30%"。只有看起来像输入行/多行编辑框的矩形才被采纳为 caret，容器矩形（典型 Chromium WebView）直接拒绝。消除"假成功定死中点"现象。
- **`LogThrottled` 按 key 分桶**: 把共享字段 `_lastUiaFailLogTicks` 替换为 `ConcurrentDictionary<string, long>`。调用点签名从 `LogThrottled(message)` 改为 `LogThrottled(key, message)`，每类消息独立 2 秒节流。Win32 失败不再吞掉 UIA 失败日志。
- **补齐诊断日志**: `OnKeyPressed` 入口加 `kb_hit`（确认钩子工作）、`TryGetCaretViaUIAutomation` 入口加 `uia_enter`、各成功路径加 `uia_ok_sel` / `uia_ok_vis` / `uia_ok_bnd` / `msaa_ok`、新增 `uia_no_textpattern` 细分 UIA 方法 A 的两种失败情况。

**非目标**（明确不做）：
- 不改造为 UIA 事件订阅模式（`IUIAutomationFocusChangedEventHandler` + `TextEditTextChangedEventHandler`）——那是架构级重写，超出本 change 范围
- 不实现 TextPattern 空选区的 `DocumentRange.RangeFromPoint` 兜底——MSAA 路径已覆盖大多数 Chromium/Electron 场景，原旁支缺陷 #2 被大幅降级
- 不处理 Hook 健康自检（旁支缺陷 #3）——单独起 change

## Capabilities

### Modified Capabilities

- `keyboard-tracking`: 在位置获取优先级序列中插入 MSAA OBJID_CARET 作为第二路径；方法 B 的"合法输入框"判据从面积阈值改为维度比例；日志节流改为按 key 分桶。

## Impact

- **代码影响**: 仅 `Services/TrackingManager.cs`。+135 / -24 行。
- **行为影响**:
  - QQ / Telegram / VS Code 等 Chromium/Electron 类应用的键盘跟随从"假成功死定中点"变为精确跟随 caret
  - 对无 TextPattern 且 rect 过大的 UIA 焦点，放大镜保持鼠标最后位置（而不是跳到容器中心）
  - 生产环境日志量略增（`kb_hit` 等诊断日志每 2 秒最多一条/种类），但每类消息不再被 Win32 失败饥饿
- **API 影响**: 无公开 API 变化
- **依赖影响**: 新增对 `oleacc.dll` 的 P/Invoke（Windows 系统 DLL，无需额外引用）；不引入 `Accessibility.dll`（通过 IDispatch late binding 规避）
- **性能影响**: 每次按键在 Win32 失败后多一次 `AccessibleObjectFromWindow + accLocation` 调用。两者都是轻量 COM 调用（<1ms），远低于原 UIA 500ms 超时保护。
