## Context

`TrackingManager` 为 UIA 调用实现了一个简化版 Circuit Breaker：

```csharp
private volatile int _consecutiveUiaTimeouts;     // 连续超时计数
private long _uiaDisabledUntilTicks;              // 降级窗口截止时间
private const int MaxConsecutiveTimeouts = 3;
private const long UiaBackoffTicks = 10 * TimeSpan.TicksPerSecond;
```

现有逻辑（`Services/TrackingManager.cs`）：

```csharp
// 超时计数（line 170-176）
catch (OperationCanceledException) {
    var timeouts = Interlocked.Increment(ref _consecutiveUiaTimeouts);
    if (timeouts >= MaxConsecutiveTimeouts)
        Interlocked.Exchange(ref _uiaDisabledUntilTicks, now + 10s);
}

// 降级判定（line 286-287）
if (DateTime.UtcNow.Ticks < _uiaDisabledUntilTicks)
    return false;

// 归零路径（line 159、221）
// 1) UIA 查找"真正成功"时归零
// 2) TryGetCaretViaWin32 成功时归零
```

**Bug 机制**：在 Win32 caret 路径从不命中的应用（浏览器、微信、QQ、VSCode 等 Electron/CEF/自绘控件）里持续打字，触发 3 次 UIA 超时后进入 10 秒降级。10 秒后的第一次重试若又超时（任何短暂的 UIA 波动都可能触发），计数器 `_consecutiveUiaTimeouts` 仍停留在 3 → 自增到 4 → 仍 `>= 3` → 立即再次降级。如此循环，键盘跟随**对所有走 UIA 路径的应用**全局失效，直到"某次 Win32 caret 成功"把计数器归零。用户的体感就是"时好时坏、去记事本打字又好了"。

现场验证（本次探索）：用户在微信失效时切到记事本打字一次，回到微信立即恢复跟随 —— 正是 Win32 路径成功归零后的结果。

## Goals / Non-Goals

**Goals**:
- 降级窗口过期时自动恢复到完整的 3 次容错预算，不再依赖"偶发 Win32 成功"作为唯一出口
- 失效-恢复-再失效路径在无用户干预时也能正常工作
- 生产构建下具备可观测性：用户和开发者都能通过 `debug.log` 验证故障发生与恢复

**Non-Goals**:
- 不实现标准三态（Closed/Open/Half-Open）状态机
- 不改变现有策略参数（500ms 超时、10s 降级、3 次阈值）
- 不修 `TrackingManager` 中其它已知缺陷（40000 面积阈值、TextPattern 空选区回退、Hook 健康自检）

## Decisions

### D1：Circuit Breaker 归零策略 —— 在"降级过期后首次放行"时归零

```csharp
// TryGetCaretViaUIAutomation 入口附近
var disabledUntil = Interlocked.Read(ref _uiaDisabledUntilTicks);
if (disabledUntil > 0) {
    if (DateTime.UtcNow.Ticks < disabledUntil) {
        return false;                              // 仍在降级窗口
    }
    // 窗口刚过期：清掉截止时间戳和超时计数，给 UIA 一个完整的重试预算
    Interlocked.Exchange(ref _uiaDisabledUntilTicks, 0);
    Interlocked.Exchange(ref _consecutiveUiaTimeouts, 0);
    _log.LogDebug("Tracking", "UIA backoff expired, counter reset");
}
```

**为什么这样而不是"每次降级时顺便归零"？**
方案备选：在触发降级的那一刻就把 `_consecutiveUiaTimeouts` 归零。
- 优点：代码更少
- 缺点：语义上把两个独立状态（"最近连续超时计数"和"是否在降级窗口"）耦合在一起，未来若想改成"降级内允许小概率探测"会需要重新拆分

本方案把归零放在"即将退出降级"的那一刻，两个状态仍然独立，可读性更强。归零点正是该归零的时机，不是"顺手做"。

**为什么在 `TryGetCaretViaUIAutomation` 入口做，不在 `DebouncedCaretLookup` 里做？**
- `TryGetCaretViaUIAutomation` 是降级判定的唯一检查点（line 286）。归零逻辑紧贴这个检查点，任何未来新增调用路径都会自动继承正确行为
- 避免在多个地方复制 `DateTime.UtcNow.Ticks < _uiaDisabledUntilTicks` 这段判断

### D2：可观测性 —— 复用现有 `LogService.Instance`

`LogService` 已有：
- 线程安全的文件写入（`FileStream` + 独占锁）
- 1MB 自动轮转
- 路径注入防御
- 每行带毫秒时间戳 + 标签（`LogDebug(string tag, string message)`）

**决定**：直接在 `TrackingManager` 构造函数持有 `LogService.Instance` 引用，替换所有 `System.Diagnostics.Debug.WriteLine("[Tracking] ...")` 为 `_log.LogDebug("Tracking", "...")`。不引入日志抽象层、不做级别控制（`LogDebug` 本身已是 debug 级）。

**为什么不引入 ILogger？** —— 项目已有一个简单可靠的 LogService，没必要为一个 10 行的 bug 修复引入抽象层。

### D3：新增日志点的粒度

仅在"有决策价值的"事件上打点，避免高频日志污染：

| 事件 | 频率 | 是否新增 |
|------|------|---------|
| UIA 超时 | 偶发 | ✓ 新增（计数后） |
| 进入降级 | 偶发 | ✓ 新增（设置截止时间后） |
| 退出降级（首次放行） | 偶发 | ✓ 新增（D1 的归零点） |
| UIA 非超时异常 | 偶发 | ✓ 替换原 `Debug.WriteLine` |
| Win32 caret 失败 | 高频（每次按键在 Web/Electron 应用里） | ✗ 不打点 |
| UIA 成功 | 高频（每次按键在 Web/Electron 应用里） | ✗ 不打点 |
| 按键事件本身 | 超高频 | ✗ 不打点 |

日志文件已有 1MB 轮转，但即便如此高频路径仍会很快盖过有价值的事件。

### D4：验证策略 —— 手工复现原现场

**不写自动化测试**。理由：
- Circuit Breaker 归零逻辑本身是 1 行 `Interlocked.Exchange`，单元测试会比被测代码长 10 倍
- 真实故障依赖 UIA 跨进程 RPC 超时，无法在纯内存单元测试里可靠复现
- 项目目前没有单元测试基础设施（`WindowsMagnifier.csproj` 下无 test 项目）

**替代方案**：定义一个可执行的手工回归脚本（见 `tasks.md`），用户可在修复后重复验证：
1. 打开微信连续打字直到出现失效
2. **不去记事本**，继续在微信里打字 20 秒
3. 观察是否自恢复
4. 同时查看 `%APPDATA%\WindowsMagnifier\debug.log` 是否包含 `[Tracking] UIA backoff expired, counter reset` 事件

## Risks / Trade-offs

**[风险] 降级窗口过期立即归零，等于每 10 秒给 UIA 一次"完整 3 次"的机会 —— 若目标进程真的永久有问题，会反复触发 3 次 UIA 调用后才降级**
→ 缓解：`MaxConsecutiveTimeouts = 3` + `500ms` 超时 = 最多 1.5 秒"失控"时间，每 10 秒一次，对用户感知影响可接受。相比当前"一次锁死、被动等用户去记事本解锁"是绝对好转。

**[风险] 新增日志点可能掩盖未来新缺陷的真正事件**
→ 缓解：D3 的白名单策略只打点"低频、高价值"事件；日志文件 1MB 轮转仍会保留最近窗口；真正的高频路径（每次按键）继续静默。

**[风险] `Interlocked.Exchange` 归零与"另一个线程刚刚递增计数"之间存在 race**
→ 分析：即使发生 race，最坏情况是"归零被覆盖成 1"或"即将发生的降级判定读到陈旧 0"。这两种都是**暂态**，且不会造成比现有 bug 更糟的状态 —— 现在的 bug 是"永久锁死"，新的 race 最坏是"慢 1 次重试"。可接受。

**[风险] 日志路径被符号链接劫持**
→ `LogService` 已内建符号链接检测（line 72-74），无需额外处理。

## Migration Plan

纯代码变更，无数据迁移、无配置迁移、无跨进程协议变化。发布即生效。

回滚策略：若发现新 bug，`git revert` 单个 commit 即可；`TrackingManager.cs` 之外无任何文件被触碰。

## Open Questions

无。全部决策已在 D1-D4 中确定。
