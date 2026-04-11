# 活跃上下文

**日期:** 2026-04-12
**项目:** 眼眸 (WindowsMagnifier) — 桌面放大辅助工具
**版本:** v1.2.1（含一个 in-flight 热修复, 未发布）
**评分:** 9.0/10（代码质量），5.9/10（用户体验）
**测试:** 98/98 通过（本次未新增）

## 当前状态

- master 分支最新提交 0acd100（2026-03-21 会话收尾），自那以后仅有本次 in-flight 修复
- 存在一个活跃的 OpenSpec change: **`fix-keyboard-tracking-latching-bug`**，状态 **13/22 任务完成**
- 代码改动只触及 `WindowsMagnifier/Services/TrackingManager.cs`（+24/-6 行）
- 新构建的单文件发布在 `release/patched/眼眸.exe`（71,807,048 bytes, 2026-04-12 00:21）
- 稳定参考构建 `release/眼眸.exe`（3/21，未覆盖，作为回滚保险）
- **未 commit 任何代码**，等待用户现场回归验证后再闭环

## 本次会话完成的工作

### 探索阶段（/opsx:explore）
- 用户报告"键盘跟随时好时坏"
- 先排查假设：ThreadPool 饥饿（进程 57h 运行, 34 线程稳定, 无证据）→ 排除
- 排查 UIA 永久阻塞（`LpcReply` 从 1 自动恢复 0）→ 排除
- 排查 Hook 被 Windows 踢出（从 QQ 切回微信立即恢复跟随）→ 排除
- 用户纠正"坏的时候所有应用都不跟随"→ 根因从特定应用拉回到全局 latching
- 用 PowerShell + `System.Windows.Automation.AutomationElement.FocusedElement` 写探针, 复现代码同款 UIA 查询
- 发现 Windows Terminal `TermControl` 面积 723290 被 40000 阈值过滤, `GetSelection()` 光标空闲时返回空集
- **根因锁定**: `TrackingManager.TryGetCaretViaUIAutomation` 的 Circuit Breaker `_consecutiveUiaTimeouts` 在 10 秒降级窗口过期后不归零, 导致"首次重试失败立即再合闸"死循环
- 用户现场验证: 失效时切到记事本打字一次 → `TryGetCaretViaWin32` 成功归零 counter → 回微信立即恢复, 证据链完整
- `debug.log` 与 `error.log` 零 Tracking 记录, 证实"所有诊断路径走 `Debug.WriteLine` 在 Release 被剪掉"的可观测性盲区

### OpenSpec change 创建
- `openspec/changes/fix-keyboard-tracking-latching-bug/`
  - `proposal.md` — Why/What Changes/Capabilities/Impact，显式排除 P2/P3
  - `design.md` — D1/D2/D3/D4 四个决策 + 风险权衡
  - `specs/keyboard-tracking/spec.md` — 4 个 Requirement, 10 个 WHEN/THEN 场景
  - `tasks.md` — 22 项任务分 5 组
- `openspec validate --strict` 通过

### 代码实施（P1 可观测性 + P0 修复）
- 添加 `private static readonly LogService _log = LogService.Instance;` 字段
- 4 处 `Debug.WriteLine` → `_log.LogDebug("Tracking", ...)`
- 新增 2 个日志点: `UIA enter backoff (10s)` / `UIA backoff expired, counter reset`
- `TryGetCaretViaUIAutomation` 入口重构为三段式 Circuit Breaker 判定, 降级窗口刚过期时 `Interlocked.Exchange` 同时清零 `_uiaDisabledUntilTicks` 和 `_consecutiveUiaTimeouts`
- Release + Debug 构建 0 error, 2 个遗留 warning 与本 change 无关

### 构建环境修复
- 发现 `DOTNET_ROOT=C:\Users\49011\.dotnet` 环境变量指向用户本地 SDK 8.0.419, 默认 `C:\Program Files\dotnet\dotnet.exe` 只含 .NET 6 Runtime
- 找到正确的构建命令, 记录在 memory 和 tasks 里

## 下一步（本 change）

等用户回归验证后回来：

- ✅ 验证通过（`UIA backoff expired, counter reset` 出现 + 跟随自恢复无需切记事本）
  → `openspec validate --strict` → 单 commit `fix(magnifier): 修复 UIA 熔断器归零 bug 使键盘跟随自恢复 + 补齐日志` → `openspec archive fix-keyboard-tracking-latching-bug`
- ❌ 仍有问题
  → 读 `%APPDATA%\WindowsMagnifier\debug.log` 里带 `[Tracking]` 的行重新进入 explore

## 探索中发现的 3 个独立问题（本次不做）

按"一次修复只做一件事"原则明确排除, 各自应起独立 change：

1. **40000 面积阈值过严** — `Services/TrackingManager.cs` line ~332。200×200 在 4K 屏上连正常编辑区都殃及。Windows Terminal / VSCode / Google Docs 受影响。
2. **TextPattern 空选区回退路径缺失** — line ~314。`GetSelection()` 空数组时未用 `DocumentRange.GetChildren()` 或 degenerate range 的 bounding rect 兜底, 直接 fallback 到 BoundingRectangle 再被 40000 过滤。
3. **Hook 健康自检（低优先兜底）** — `Services/KeyboardHook.cs`。Windows `LowLevelHooksTimeout` 踢出 hook 后 `_hookId` 仍非零, `Start()` 短路认为还活着。本次未复现, 但机制存在。建议加"重启键盘钩子"按钮或周期性间接检测。

## v2.0 功能方向（未动）

见 2026-03-21 会话尾部的 P0/P1 列表: 滚轮调倍数、设置界面字号/对比度、系统托盘、颜色反转、位置可选、焦点十字准星。

## 阻塞项

- `fix-keyboard-tracking-latching-bug` 的 commit + archive 被用户现场回归阻塞（§4.4 无干预自恢复验证是唯一硬门槛）

## 重要决策（本次）

1. **最小改动 vs 标准三态 CB** — 选最小改动（1 行 `Interlocked.Exchange` 归零 + 注释解释不变量）。三态 CB 会把代码量扩大 10 倍, 对个人工具过度工程。
2. **归零位置** — 放在 `TryGetCaretViaUIAutomation` 入口的"刚过期"分支, 而不是"进入降级时顺手归零"。理由: 前者保持"连续超时计数"与"降级窗口"两个状态独立, 可读性更强, 未来若想改成"降级内允许小概率探测"时不需要拆耦合。
3. **P1 日志先行 P0 修复后做** — 有日志才能在实施 P0 时看到计数器行为是否符合预期, 而不是"感觉修好了"。
4. **不写自动化测试** — bug 核心是 1 行 Interlocked 操作, 真实故障依赖跨进程 RPC 超时无法在内存中复现, 且项目没有现成的测试基础设施。改用可执行的手工回归脚本（tasks §4.4）。
5. **P2/P3 排除在本 change 之外** — 40000 阈值 / 空选区回退 / Hook 自检, 各自应起单独 change。打包会把范围扩大 3 倍, 引入的风险超过它们解决的。

## 技术债务（承自 2026-03-21）

- App.xaml.cs God Object（547 行）→ 提取 WindowVisibilityManager
- 服务层接口抽象（当前测试覆盖率 18.2%）
- LogService 持久化文件句柄 + 缓冲写入
- 鼠标钩子冗余事件触发优化
- DateTime.UtcNow.Ticks → Environment.TickCount64
- DisplayFocusManager 组合操作非原子
- FullScreenDetector 200ms 轮询 → ABN_FULLSCREENAPP 回调
- **本次新增**: `TrackingManager` 三个独立缺陷（见上）
