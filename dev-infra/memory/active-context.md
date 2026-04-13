# 活跃上下文

**日期:** 2026-04-13
**项目:** 眼眸 (WindowsMagnifier) — 桌面放大辅助工具
**版本:** v1.2.1（三项热修复已提交, 用户验证通过）
**评分:** 9.0/10（代码质量），5.9/10（用户体验）
**测试:** 98/98 通过（本次未新增）

## 当前状态

- master 分支最新提交 c4e4d3f（2026-04-13 键盘跟随三项修复）
- 代码已 commit，用户验证通过
- 新构建在 `release/patched/眼眸.exe`

## 本次会话完成的工作（2026-04-13）

1. **日志分析** — 发现整个 Tracking 日志 100% 是失败，无熔断器触发记录
2. **根因定位** — 终端窗口（cmd/PowerShell/Windows Terminal）的 UIA 查询污染全局 UIA 状态，导致切回微信后跟随失效
3. **终端窗口排除** — 检测 ConsoleWindowClass + CASCADIA_HOSTING_WINDOW_CLASS，跳过光标查询
4. **Win32 诊断日志** — 补齐 Win32 caret 失败路径日志
5. **用户验证通过** — 终端→微信切换后键盘跟随正常

## 探索中发现的 3 个独立问题（本次不做）

按"一次修复只做一件事"原则明确排除, 各自应起独立 change：

1. **40000 面积阈值过严** — `Services/TrackingManager.cs`。200×200 在 4K 屏上连正常编辑区都殃及。
2. **TextPattern 空选区回退路径缺失** — `GetSelection()` 空数组时未兜底。
3. **Hook 健康自检（低优先兜底）** — `Services/KeyboardHook.cs`。

## v2.0 功能方向（未动）

滚轮调倍数、设置界面字号/对比度、系统托盘、颜色反转、位置可选、焦点十字准星。

## 阻塞项

- 无当前阻塞项

## 重要决策（本次）

1. **静默失败根因判断** — 日志零 Tracking 条目 → 确认失败不经过超时路径 → 熔断器修复与此场景无关
2. **先加诊断日志再修根因** — 同一次构建同时做，避免多轮构建
3. **日志节流 2 秒/条** — 避免高频按键灌满日志文件（每秒 30+ 按键 × 500ms 超时）
4. **`_displayChanged` 字段保留** — 虽然当前只用于标记，后续可能用于切换 UIA 查询策略

## 重要决策（上一轮，承继）

1. **最小改动 vs 标准三态 CB** — 选最小改动
2. **归零位置** — 放在 `TryGetCaretViaUIAutomation` 入口的"刚过期"分支
3. **P1 日志先行 P0 修复后做**
4. **不写自动化测试**
5. **P2/P3 排除在本 change 之外**

## 技术债务（承自 2026-03-21 + 新增）

- App.xaml.cs God Object（547 行）→ 提取 WindowVisibilityManager
- 服务层接口抽象（当前测试覆盖率 18.2%）
- LogService 持久化文件句柄 + 缓冲写入
- 鼠标钩子冗余事件触发优化
- DateTime.UtcNow.Ticks → Environment.TickCount64
- DisplayFocusManager 组合操作非原子
- FullScreenDetector 200ms 轮询 → ABN_FULLSCREENAPP 回调
- TrackingManager 三个独立缺陷（40000 阈值 / 空选区回退 / Hook 自检）
- **本次新增**: 显示器变化后 UIA 刷新机制可能需要更深层修复（FromHandle 替代 FocusedElement）
