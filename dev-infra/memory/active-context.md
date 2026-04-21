# 活跃上下文

**日期:** 2026-04-21
**项目:** 眼眸 (WindowsMagnifier) — 桌面放大辅助工具
**版本:** v1.2.1（hotfix #3 已提交，用户验证通过）
**评分:** 9.0/10（代码质量），5.9/10（用户体验）
**测试:** 98/98 通过（本次未新增）

## 当前状态

- master 分支最新提交 `439a3e3`（2026-04-21，恢复 Chromium/Electron 键盘跟随）
- 代码已 commit，用户 QQ + Telegram 验证通过（日志确证 MSAA 返回精确 caret `size=1×18`）
- 新构建固化在 `release/patched/眼眸.exe`（原 `patched-v4` 升级，v2/v3 已删）
- PID 31864 运行于新 exe

## 本次会话完成的工作（2026-04-21）

1. **诊断** — 用户报告 QQ/TG 键盘跟随失效。日志显示 `BoundingRect too large (974x804)` 假成功 + Win32 失败淹没 UIA 失败日志
2. **初次误判 → 自我反驳** — 曾假设"Electron caret 无法获取"，被用户反例（官方 Windows Magnifier 能跟 QQ）推翻，重新检索权威路径
3. **真相** — 官方 Magnifier 走 **MSAA `OBJID_CARET`**（`oleacc.dll`），Chromium 专为此通道暴露精确 caret，而我们代码里缺这条路径
4. **代码改动**（`TrackingManager.cs` +135/-24）：
   - 新增 `TryGetCaretViaMsaa` 作为 Win32 失败后第二路径
   - `IsElementNearlyFullScreen` → `LooksLikeInputControl`（宽<60% **AND** 高<30%），消除 WebView 容器的假成功死定
   - `LogThrottled` 改为 `ConcurrentDictionary<key, ticks>` 按消息分桶，Win32/UIA 互不饥饿
   - 配套诊断日志：`kb_hit`、`uia_enter`、各成功路径 `msaa_ok`/`uia_ok_*`
5. **验证** — MSAA 返回 `size=1×18` 像素级 caret，x 坐标随打字从 1037 → 1113 变化（真跟随而非死点）
6. **清理** — `release/patched-v2/v3/` 删除，`patched-v4/` 升为正式 `patched/`

## 遗留问题（更新）

此前列的"3 个独立问题"更新：

| 原问题 | 现状 |
|---|---|
| 40000 面积阈值过严（Services/TrackingManager.cs） | **已解决**。`LooksLikeInputControl` 用宽/高比例而非面积，彻底消除 DPI 虚拟化引起的误拒 |
| TextPattern 空选区回退路径缺失 | **大幅降级**。MSAA `OBJID_CARET` 覆盖大多数 Chromium/Electron 场景，原计划的 TextPattern 兜底改造可延后 |
| Hook 健康自检（低优先兜底） | **未动**。仍留作下一轮 change |

## 未归档的 openspec change

- `WindowsMagnifier/openspec/changes/fix-keyboard-tracking-latching-bug/` 自 2026-04-12 起未归档，`tasks.md` 第 4、5 节未勾选
- 本次又新增一轮未 change 化的修复。建议下一会话统一归档/或补一个 `fix-msaa-caret-path` 的 change 作为轨迹

## v2.0 功能方向（未动）

滚轮调倍数、设置界面字号/对比度、系统托盘、颜色反转、位置可选、焦点十字准星。

## 阻塞项

- 无

## 重要决策（本次）

1. **MSAA `OBJID_CARET` 是 Chromium 类应用 caret 的权威通道** — 官方 Magnifier 用它，Chromium 专门暴露它。`accLocation` 用 IDispatch late binding 调用避免引入 Accessibility.dll
2. **`LooksLikeInputControl` 用 AND 判据** — "宽<60% **AND** 高<30%"。OR 太严（输入框可能跨大半屏），AND 刚好过滤 WebView 容器而放行合法输入行
3. **不起正式 openspec change，直接迭代修复** — 用户要求快速循环（沿用 4-13 模式）
4. **保留诊断日志到生产版本** — `kb_hit`、`uia_enter`、`msaa_*`、`uia_ok_*`。日志量略增，但对未来类似问题无价

## 重要决策（承继）

1. **静默失败根因判断** — 缺日志时假设不成立。优先补诊断后再修根因
2. **先加诊断日志再修根因** — 同一次构建中完成以减少迭代（本次为此付出 3 轮构建 v2/v3/v4，教训：诊断日志应作为**默认配置**长期在位）
3. **归零位置** — `_uiaDisabledUntilTicks` 在 `TryGetCaretViaUIAutomation` 入口归零（4-11 修）
4. **不写自动化测试** — 人工回归
5. **最小改动 vs 标准架构** — 选最小改动

## 技术债务（承自 2026-03-21 + 更新）

- `App.xaml.cs` God Object（547 行）→ 提取 `WindowVisibilityManager`
- 服务层接口抽象（当前测试覆盖率 18.2%）
- `LogService` 持久化文件句柄 + 缓冲写入
- 鼠标钩子冗余事件触发优化
- `DateTime.UtcNow.Ticks` → `Environment.TickCount64`
- `DisplayFocusManager` 组合操作非原子
- `FullScreenDetector` 200ms 轮询 → `ABN_FULLSCREENAPP` 回调
- `TrackingManager` 剩余：`TextPattern` 空选区兜底（已弱化，低优）、Hook 健康自检（低优）
- **本次新增**：openspec change 归档流程悬挂（4-12 起未归档），需补流程
