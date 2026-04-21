## ADDED Requirements

### Requirement: 键盘输入位置跟随

当用户在前台应用中键入字符时，放大镜 SHALL 尝试获取该应用当前光标/插入点的屏幕位置，并以该位置作为键盘跟随模式的跟踪点。

获取位置的策略 SHALL 遵循以下优先级：
1. 先走 Win32 `GetGUIThreadInfo` 路径（适用于使用 `CreateCaret` 的传统控件，如记事本、WinForms `TextBox` 等）
2. 失败后走 UI Automation 路径（适用于 Electron、WebView2、UWP/WinUI、自绘编辑器等现代应用）
3. 两条路径都失败则不更新位置，保持当前跟踪点不变

#### Scenario: 记事本中键入字符
- **WHEN** 用户在记事本中按下字母键
- **THEN** `TryGetCaretViaWin32` SHALL 成功返回光标屏幕坐标
- **AND** 放大镜跟踪点 SHALL 切换到该坐标所在位置

#### Scenario: Electron 应用（如 VSCode、微信）中键入字符
- **WHEN** 用户在此类应用的文本输入区按下字母键
- **AND** Win32 caret 路径返回失败
- **THEN** 系统 SHALL 走 UI Automation 路径尝试获取插入点位置

#### Scenario: 两条路径均失败
- **WHEN** 前台应用既不支持 Win32 caret, UIA 查询也未能返回有效位置
- **THEN** 放大镜 SHALL 保持当前跟踪点不变（既不切到键盘模式也不回到鼠标模式）


### Requirement: UIA 超时降级（Circuit Breaker）

为防止前台应用无响应时 UIA 跨进程 RPC 阻塞键盘跟随路径，系统 SHALL 实现基于超时的熔断机制：

- 每次 UIA 查询受 500 毫秒超时保护
- 连续 3 次 UIA 超时 SHALL 触发 10 秒降级窗口
- 降级窗口内，所有 UIA 查询 SHALL 立即返回失败（不发起实际 RPC 调用）
- 降级窗口过期后，UIA 查询 SHALL 被重新允许

#### Scenario: 连续超时触发降级
- **WHEN** `TryGetCaretViaUIAutomation` 在 500ms 内未返回
- **AND** 此前已连续 2 次 UIA 超时
- **THEN** 系统 SHALL 将降级窗口的截止时间设置为当前时间 + 10 秒
- **AND** SHALL 写入 `debug.log` 一条 `[Tracking]` 标签的进入降级事件

#### Scenario: 降级窗口内的请求被拦截
- **WHEN** 处于降级窗口内，发生新的按键事件
- **THEN** `TryGetCaretViaUIAutomation` SHALL 不调用任何 UIA API
- **AND** SHALL 直接返回 false


### Requirement: 降级窗口过期后的重试预算恢复

当降级窗口过期、下一次 UIA 查询即将被放行时，系统 SHALL 将连续超时计数器 `_consecutiveUiaTimeouts` 重置为 0，并清除降级截止时间戳。该重置保证每一轮恢复尝试都拥有完整的 3 次容错预算，而不是仅有 1 次。

此要求直接针对 latching bug：在修复前，计数器永不归零会导致任何一次"恢复后的首次超时"立即触发新的 10 秒降级循环，键盘跟随对所有走 UIA 路径的应用全局锁死，只能靠"意外触发一次 Win32 caret 成功"解锁。

#### Scenario: 降级过期后 UIA 再次成功
- **WHEN** 当前时间超过 `_uiaDisabledUntilTicks`
- **AND** 新的按键事件触发 `TryGetCaretViaUIAutomation`
- **THEN** 系统 SHALL 在调用 UIA API 之前将 `_consecutiveUiaTimeouts` 重置为 0
- **AND** 将 `_uiaDisabledUntilTicks` 清零
- **AND** 写入 `debug.log` 一条 "UIA backoff expired, counter reset" 事件
- **AND** 若此次 UIA 调用成功，后续调用 SHALL 正常工作，无需用户干预

#### Scenario: 降级过期后 UIA 再次失败但未到阈值
- **WHEN** 降级窗口过期后的首次 UIA 调用再次超时
- **THEN** 归零后的计数器 SHALL 从 0 递增到 1（而非从 3 递增到 4）
- **AND** 系统 SHALL 继续放行后续的 UIA 调用
- **AND** 必须再连续 2 次超时才会触发新的降级窗口

#### Scenario: 无用户干预的自恢复路径
- **WHEN** 用户在微信输入框中持续键入, UIA 触发了一次完整的"3 次超时 → 10 秒降级"循环
- **AND** 用户在整个过程中 **未** 切换到记事本或任何走 Win32 caret 的应用
- **THEN** 10 秒降级窗口过期后，放大镜 SHALL 在下一次可成功的 UIA 调用时自动恢复键盘跟随
- **AND** 用户可观察到的失效时间 SHALL 被限制在 10 秒级而非永久锁死


### Requirement: 跟踪路径的可观测性

`TrackingManager` 中所有异常路径和状态转移 SHALL 通过 `LogService.LogDebug("Tracking", ...)` 写入 `%APPDATA%\WindowsMagnifier\debug.log`，而 NOT 使用 `System.Diagnostics.Debug.WriteLine`（后者在 Release 编译下会被完全移除，导致生产环境零可观测性）。

打点粒度受以下约束：
- 仅记录**低频、高价值**事件（超时、进入降级、退出降级、非超时异常）
- 不记录高频事件（每次按键成功取值、Win32 路径失败后回退等）

#### Scenario: UIA 超时事件可追溯
- **WHEN** 一次 UIA 调用超时
- **THEN** `debug.log` SHALL 出现一条包含 `[Tracking]` 标签和超时计数的记录

#### Scenario: 降级进入事件可追溯
- **WHEN** 连续超时次数达到 3 次、触发 10 秒降级
- **THEN** `debug.log` SHALL 出现一条标记"enter backoff"的记录

#### Scenario: 降级退出事件可追溯
- **WHEN** 降级窗口过期, 首次 UIA 调用被放行
- **THEN** `debug.log` SHALL 出现一条标记 "UIA backoff expired, counter reset" 的记录

#### Scenario: Release 构建下日志仍然写入
- **WHEN** 应用以 Release 配置编译和运行
- **AND** 触发任一上述日志事件
- **THEN** 事件 SHALL 实际写入磁盘上的 `debug.log`（不因为 `[Conditional("DEBUG")]` 而丢失）
