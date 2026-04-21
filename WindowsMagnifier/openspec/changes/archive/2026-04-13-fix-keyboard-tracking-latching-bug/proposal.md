## Why

键盘跟随功能"时好时坏"：用户在微信/QQ/浏览器等走 UIA 路径的应用中打字时，偶发出现**全局键盘跟随失效**，且状态会被卡住，只有"不经意地去了一个走 Win32 caret 的应用"（如记事本）才能解锁恢复。

根因已现场验证：`TrackingManager` 的 UIA Circuit Breaker 实现存在**归零时机 bug** —— 连续 3 次超时触发 10 秒降级后，`_consecutiveUiaTimeouts` 计数器**不会随降级窗口过期而重置**。结果是 10 秒后第一次重试若再次超时（timeouts=4，仍 ≥ 3），立即再次降级，循环无法自恢复。唯一能归零计数器的路径是 `TryGetCaretViaWin32` 成功 —— 这就是"去记事本打字就好了"的机制。

此外还发现一个次要问题：`TrackingManager` 中所有诊断路径均使用 `System.Diagnostics.Debug.WriteLine`，在 Release 编译下被完全移除，导致生产环境**零可观测性**，用户和开发者都无法判断故障是否再次发生。

## What Changes

- **修复 Circuit Breaker 归零逻辑**：在降级窗口过期、UIA 即将被重新允许调用时，重置 `_consecutiveUiaTimeouts = 0`。确保每一轮"半开"尝试都有完整的 3 次容错预算，而不是只有 1 次。
- **补齐跟踪路径的可观测性**：将 `TrackingManager` 中的 `Debug.WriteLine` 全部替换为 `LogService.LogDebug`，并附加统一 `[Tracking]` 标签。新增关键事件日志（UIA 超时计数、降级进入/退出、Win32/UIA 成功路径选择）。
- **新增回归验证**：手工回归用例——复现"微信失效 → 切记事本 → 回微信"完整路径，验证修复后无需"切到记事本"这一动作也能自动恢复。

**非目标**（明确不做）：
- 不重写为标准三态 Circuit Breaker（过度工程）
- 不修改 40000 面积阈值、TextPattern 空选区处理、Hook 健康自检等旁支问题 —— 这些是探索过程中发现的独立缺陷，应各自起单独的 change
- 不改变 UIA 超时时长（500ms）、降级时长（10s）、容错次数（3）等策略参数

## Capabilities

### New Capabilities
- `keyboard-tracking`: 键盘输入位置跟随能力。定义放大镜如何从前台应用读取光标/插入点位置，并在读取失败时的降级与恢复契约。

### Modified Capabilities
（无 —— 尚无现存 spec）

## Impact

- **代码影响**：仅 `Services/TrackingManager.cs`。修改约 10 行以内（Circuit Breaker 归零逻辑 3-5 行 + 日志替换若干行 + 少量新增日志点）。
- **行为影响**：用户感知的键盘跟随将从"偶发锁死需手动解锁"变为"短暂波动后自恢复"。
- **API 影响**：无。`TrackingManager` 公开接口（`Start`/`Stop`/`UpdateSettings`/`PositionChanged`）不变。
- **依赖影响**：无新增依赖。`LogService` 已存在，`Debug.WriteLine` 调用会被移除。
- **性能影响**：日志写入会增加少量 I/O，但只在异常路径上发生（超时、降级、错误），正常稳态无额外开销。
