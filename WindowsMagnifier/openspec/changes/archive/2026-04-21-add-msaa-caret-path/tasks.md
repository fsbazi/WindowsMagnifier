## 1. 诊断与根因定位

- [x] 1.1 核对 live 进程和实际运行的 exe 路径
- [x] 1.2 读取 `%APPDATA%\WindowsMagnifier\debug.log` 最近活动, 统计 `[Tracking]` 消息频次
- [x] 1.3 用 PowerShell 查询 QQ / Telegram / Windows Terminal 的 className, 排除 `IsConsoleWindow` 误报（QQ = `Chrome_WidgetWin_1`, TG = `Qt51518QWindowIcon`）
- [x] 1.4 测试 Notepad 打字跟随（Win32 路径对照组）→ 确认问题局限于 UIA 路径
- [x] 1.5 发布诊断版 `patched-v3`（加 `kb_hit` / `uia_enter` / 各成功路径日志）→ 锁定 QQ `FocusedElement has no TextPattern` + `BoundingRect OK @683,633 size=1060x797` 假成功

## 2. `LogThrottled` 分桶修复（P0 诊断基础）

- [x] 2.1 把 `private long _lastUiaFailLogTicks;` 替换为 `private readonly ConcurrentDictionary<string, long> _throttleBuckets = new();`
- [x] 2.2 `LogThrottled(string message)` 签名改为 `LogThrottled(string throttleKey, string message)`, 内部用 `_throttleBuckets.GetOrAdd / TryUpdate` 实现按 key 节流
- [x] 2.3 更新所有 8 个调用点, 为每条消息分配稳定 key: `console_skip`, `win32_no_caret`, `uia_focused_null`, `uia_no_textpattern`, `uia_textpattern_empty`, `uia_rect_toolarge`, `uia_rect_empty`, `uia_element_unavail`

## 3. UIA 方法 B 判据重构（P0 消除假成功）

- [x] 3.1 `GetMaxBoundingArea()` → `LooksLikeInputControl(System.Windows.Rect rect)`, 判据: `widthRatio < 0.60 && heightRatio < 0.30`
- [x] 3.2 UIA 方法 B 调用点: `if (area > GetMaxBoundingArea())` → `if (!LooksLikeInputControl(rect))`
- [x] 3.3 删除中间临时的 `var area = rect.Width * rect.Height;` 局部变量
- [x] 3.4 更新注释, 记录历史教训: "宽/高 95% 判据被 Chromium 容器绕过" 的前因后果

## 4. MSAA OBJID_CARET 路径（P0 核心新增）

- [x] 4.1 在 P/Invoke 区段新增 `[DllImport("oleacc.dll")] AccessibleObjectFromWindow(hwnd, dwObjectID, ref guid, out object)`, 定义 `OBJID_CARET = 0xFFFFFFF8` 和 `IID_IAccessible = {618736E0-3C3D-11CF-810C-00AA00389B71}`
- [x] 4.2 实现 `TryGetCaretViaMsaa(out Point position)`:
  - 调 `GetForegroundWindow()` 拿 hwnd
  - `AccessibleObjectFromWindow(hwnd, OBJID_CARET, IID_IAccessible, out accObj)` 获取 IAccessible COM 对象
  - 通过 `Type.InvokeMember("accLocation", ...)` IDispatch late binding 调用 `accLocation(out l, out t, out w, out h, 0)`, 用 `ParameterModifier` 标记前 4 个参数为 out
  - 过滤 (0,0,0,0) 的无效返回
  - 成功时以 `(l, t + h/2)` 为跟踪点, 写 `msaa_ok` 节流日志
  - `finally` 块中 `Marshal.ReleaseComObject` 避免 COM 泄漏
- [x] 4.3 在 `TryGetCaretPosition` 中 Win32 失败后、UIA 之前插入 `TryGetCaretViaMsaa` 调用; 成功时同样重置 `_consecutiveUiaTimeouts`
- [x] 4.4 失败分支的日志 key: `msaa_fail` (AccessibleObjectFromWindow HRESULT 非零), `msaa_zero` (返回 0,0,0,0), `msaa_err` (异常)

## 5. UIA 成功路径补日志

- [x] 5.1 `UIA: OK via TextPattern.Selection @x,y` (key=`uia_ok_sel`)
- [x] 5.2 `UIA: OK via VisibleRange @x,y` (key=`uia_ok_vis`)
- [x] 5.3 `UIA: OK via BoundingRect @x,y size=WxH` (key=`uia_ok_bnd`)
- [x] 5.4 新增 `UIA: FocusedElement has no TextPattern` (key=`uia_no_textpattern`) 区分 UIA 方法 A 的两种失败情况（pattern 不存在 vs pattern 有但选区空）

## 6. OnKeyPressed 入口诊断

- [x] 6.1 在 `OnKeyPressed` 首行加 `LogThrottled("kb_hit", "Key pressed (hook OK)")`, 用于未来故障时快速确认键盘钩子在任意前台应用下都工作

## 7. 构建与手工回归

- [x] 7.1 `dotnet build -c Release` 通过（0 error, 3 legacy warning 与本 change 无关）
- [x] 7.2 `dotnet publish -c Release -r win-x64 --self-contained true -o release\patched-v4` 单文件成功
- [x] 7.3 用户关闭旧进程启动 v4
- [x] 7.4 QQ 聊天窗口输入 10 秒 → 日志出现 `MSAA: OK @1037,887 size=1x18` 和 `@1113,887 size=1x18`, x 坐标随打字位移变化
- [x] 7.5 Telegram 输入 → 跟随正常
- [x] 7.6 用户确认"QQ 和 TG 都能跟随了"

## 8. 完成与清理

- [x] 8.1 `release/patched-v4/` 升级为 `release/patched/`（v2/v3 删除, 旧 patched/ 被替换）
- [x] 8.2 启动最终 `release/patched/眼眸.exe`（PID 31864）
- [x] 8.3 提交 commit: `fix(magnifier): 恢复 Chromium/Electron (QQ/TG) 键盘跟随` (`439a3e3`)
- [x] 8.4 更新 `dev-infra/memory/active-context.md` + `progress.md` + session 文件, commit `9fa5e64`
- [x] 8.5 更新 auto-memory: 新增 `reference_msaa_caret.md` 和 `feedback_official_tools_parity.md`, 刷新 `project_fix_keyboard_tracking_in_flight.md` 和 `project_magnifier_deferred_issues.md`
- [x] 8.6 归档本 change 到 `openspec/changes/archive/2026-04-21-add-msaa-caret-path/`（与 `2026-04-13-fix-keyboard-tracking-latching-bug` 一起归档）
