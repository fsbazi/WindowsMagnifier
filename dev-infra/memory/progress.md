# 项目进度

## 版本历史

| 版本 | 日期 | 评分 | 测试 | 提交数 |
|------|------|------|------|--------|
| v1.0.0 | 2026-03-14 | 6.5/10 | 0 | — |
| v1.1.0 | 2026-03-18 | 8.6/10 | 30 | 11 |
| v1.2.0 | 2026-03-21 | 9.0/10 | 42 | 27 |
| v1.2.1-dev | 2026-03-21 | 9.0/10 | 98 | 30 |

## v1.2.1-dev 验收状态

- [x] dotnet build 零错误
- [x] dotnet test 98/98 通过
- [x] 零 CRITICAL 问题（两轮审查确认）
- [x] 放大镜无响应 bug 修复并验证
- [x] 可自定义快捷键功能完成
- [x] 全屏检测排除系统窗口（Progman 误判根因修复）
- [ ] GitHub Release 未发布

## In-flight 热修复 #1（2026-04-12 00:21）

**Change**: `fix-keyboard-tracking-latching-bug`（OpenSpec, 13/22 任务完成）

- [x] §1 可观测性 — Debug.WriteLine → LogService, 新增 backoff 进入/退出日志点
- [x] §2 Circuit Breaker 归零 — `TryGetCaretViaUIAutomation` 入口三段式重构
- [x] §3 构建 / 发布 — Release + Debug 0 error, 单文件 publish 到 `release/patched/眼眸.exe`
- [ ] §4 手工回归（等用户现场验证, 关键门槛: §4.4 无干预自恢复）
- [ ] §5 validate / commit / archive（依赖 §4 通过）

根因: `TrackingManager._consecutiveUiaTimeouts` 在 10 秒降级窗口过期后不归零, 导致"首次重试失败立即再合闸"死循环。

## In-flight 热修复 #2（2026-04-12 12:35）

**问题**: 显示器关闭/打开后 UIA 静默失败（debug.log 零 Tracking 条目）

- [x] 根因分析 — UIA 失败走静默路径（return false 无日志），不经过超时/熔断器
- [x] 显示器变化监听 — `SystemEvents.DisplaySettingsChanged` + UIA 状态重置
- [x] 静默失败路径补齐诊断日志（5 个路径, 节流 2 秒/条）
- [x] 构建 / 发布 — `release/patched/眼眸.exe`（71,807,374 bytes, 2026-04-12 12:35）
- [ ] 用户回归验证 — 关显示器 → 开显示器 → 在微信/QQ 打字 → 检查 debug.log

根因: 显示器开关后 UIA 元素变陈旧（TextPattern 丢失选区或 BoundingRectangle 过大），5 条静默失败路径全部无日志，导致问题完全不可见。

## 审查记录

| 轮次 | 类型 | 发现 | 结果 |
|------|------|------|------|
| 第 1 轮 | 5 角色技术审查 | 40+ 问题 | TOP 10 修复清单 |
| 第 2 轮 | 3 工程师修复方案 | 12 项修复 | 交叉验证通过 |
| 第 3 轮 | 5 角色复审 | 8.2/10 | 17/17 QA 通过 |
| 第 4 轮 | 3 角色全量复查 | API 假设 bug | 4 项底层修复 |
| 第 5 轮 | 3 角色终审 | 4 个小问题 | 9.0/10 可以发布 |
| 第 6 轮 | 2 角色最终确认 | 无新 CRITICAL/HIGH | 确认发布 |
| 用户轮 | 4 位视障用户 | UX 评分 5.9 | v2.0 功能路线图 |
| 多显示器轮 | 深度审查 | 8 场景全通过 | 功能完善 |
| **v1.2.1 R1** | **5 角色全面审查** | **5 CRITICAL / 16 HIGH** | **P0 全部修复** |
| **v1.2.1 R2** | **5 角色复审** | **0 CRITICAL / 5 HIGH** | **全部修复，验证通过** |
