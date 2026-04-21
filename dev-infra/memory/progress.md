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

## 热修复合并提交（2026-04-13, c4e4d3f）

三项修复合并为单次提交，用户验证通过：

- [x] **熔断器归零** — backoff 过期后清零计数，防止死循环
- [x] **显示器变化监听** — DisplaySettingsChanged + UIA 状态重置
- [x] **终端窗口排除** — 跳过 ConsoleWindowClass / Windows Terminal 的光标查询，避免 UIA 状态污染
- [x] **诊断日志** — 5 条 UIA 静默失败路径 + Win32 caret 失败路径（节流 2 秒/条）
- [x] 用户回归验证通过（终端→微信切换正常）

## 热修复合并提交（2026-04-21, 439a3e3）

恢复 Chromium/Electron (QQ / Telegram) 键盘跟随。三项联合修复：

- [x] **新增 MSAA `OBJID_CARET` 路径**（`oleacc.dll`）— 仿 Windows 官方放大镜，Chromium/Electron 类应用的精确 caret 通道，返回 `size=1×18` 像素级矩形。`accLocation` 经 IDispatch late binding 调用，避免 Accessibility.dll 依赖
- [x] **UIA 方法 B 判据收紧** — `IsElementNearlyFullScreen`（95% 阈值，DPI 虚拟化下易误拒）→ `LooksLikeInputControl`（宽<60% AND 高<30%）。消除 WebView 容器假成功"死定中点"现象
- [x] **日志节流按 key 分桶** — 共享 `_lastUiaFailLogTicks` → `ConcurrentDictionary<key, ticks>`。此前 Win32 失败每 2 秒刷新节流字段导致 UIA 失败日志 100% 被吞（诊断不可能），现各类消息独立节流
- [x] **配套诊断日志** — `kb_hit`（OnKeyPressed 入口）+ `uia_enter`（UIA 入口）+ `msaa_ok`/`msaa_fail`/`uia_ok_sel`/`uia_ok_vis`/`uia_ok_bnd` 等成功分支
- [x] 用户 QQ + Telegram 回归验证通过（日志 `MSAA: OK @1037,887 size=1x18` → `@1113,887` 证实 caret 随打字位移）
- [x] 诊断迭代：v2（仅阈值）→ v3（诊断日志揭示 BoundingRect 假成功）→ v4（MSAA + 判据收紧）

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
