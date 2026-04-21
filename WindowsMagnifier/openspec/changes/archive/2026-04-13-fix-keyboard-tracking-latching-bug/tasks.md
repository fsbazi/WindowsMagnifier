## 1. 可观测性先行（P1，必须在 P0 之前完成）

> 先做日志是为了在实施 P0 修复时能立刻看到计数器的实际行为，不是盲改。

- [x] 1.1 在 `Services/TrackingManager.cs` 顶部引入 `private static readonly LogService _log = LogService.Instance;`
- [x] 1.2 将 `DebouncedCaretLookup` 中超时分支的 `Debug.WriteLine("[Tracking] TryGetCaretPosition timed out")` 替换为 `_log.LogDebug("Tracking", $"UIA timeout (consecutive={timeouts})")`
- [x] 1.3 将 `DebouncedCaretLookup` 中通用异常分支的 `Debug.WriteLine($"[Tracking] DebouncedCaretLookup error: {ex.Message}")` 替换为 `_log.LogDebug("Tracking", $"DebouncedCaretLookup error: {ex.Message}")`
- [x] 1.4 将 `TryGetCaretViaWin32` 中的 `Debug.WriteLine($"[Tracking] GetGUIThreadInfo error: {ex.Message}")` 替换为 `_log.LogDebug("Tracking", $"Win32 caret error: {ex.Message}")`
- [x] 1.5 将 `TryGetCaretViaUIAutomation` 中的 `Debug.WriteLine($"[Tracking] UIAutomation error: {ex.Message}")` 替换为 `_log.LogDebug("Tracking", $"UIA error: {ex.Message}")`
- [x] 1.6 在连续超时达到阈值、设置 `_uiaDisabledUntilTicks` 的位置新增一条日志：`_log.LogDebug("Tracking", "UIA enter backoff (10s)")`
- [x] 1.7 搜索 `TrackingManager.cs` 全文确认已无遗漏的 `System.Diagnostics.Debug.WriteLine` 调用

## 2. Circuit Breaker 归零修复（P0）

- [x] 2.1 在 `TryGetCaretViaUIAutomation` 方法入口, 保持原降级判定的同时重构为"读取截止时间 → 判断 → 过期则归零"的三段式：
  ```csharp
  var disabledUntil = Interlocked.Read(ref _uiaDisabledUntilTicks);
  if (disabledUntil > 0)
  {
      if (DateTime.UtcNow.Ticks < disabledUntil)
          return false;
      Interlocked.Exchange(ref _uiaDisabledUntilTicks, 0);
      Interlocked.Exchange(ref _consecutiveUiaTimeouts, 0);
      _log.LogDebug("Tracking", "UIA backoff expired, counter reset");
  }
  ```
- [x] 2.2 检查 `TryGetCaretViaUIAutomation` 内部 `position = new Point();` 初始化仍在归零逻辑之前或不冲突
- [x] 2.3 确认 `_uiaDisabledUntilTicks` 的类型和访问模式（`long` + `Interlocked.Read/Exchange`）未被改动

## 3. 构建与静态检查

- [x] 3.1 `dotnet build -c Release` 通过（0 error, 2 个遗留 warning 与本 change 无关: SettingsWindow.xaml.cs CS8602 + app.manifest WFAC010）
- [x] 3.2 `dotnet build -c Debug` 通过
- [x] 3.3 单文件 publish 已完成: `release\patched\眼眸.exe` (71,807,048 bytes, 2026-04-12 00:21)

## 4. 手工回归验证

**前置**：关闭所有现存的眼眸进程, 启动新构建的版本, 确认 `%APPDATA%\WindowsMagnifier\debug.log` 可被当前用户读取。

- [ ] 4.1 **基础路径验证**：在记事本中打字 10 秒, 放大镜跟随正常
- [ ] 4.2 **UIA 路径验证**：在微信/QQ/Edge 浏览器地址栏打字 10 秒, 放大镜跟随正常
- [ ] 4.3 **降级触发验证**：持续在 UIA 路径应用中打字, 查看 `debug.log` 是否出现 `[Tracking] UIA timeout` 条目
  - 若从未出现: 说明当前环境 UIA 很稳定, 无法复现。可跳过 4.4–4.5, 将本次 change 标记为"无法现场复现但机制已修复"
  - 若出现但未达到 3 次连续: 继续观察
  - 若达到 3 次连续, `debug.log` 应同时出现 `UIA enter backoff (10s)` 条目
- [ ] 4.4 **关键验证 · 无干预自恢复**：触发降级后, **不去记事本、不切换应用**, 继续在失效的 UIA 应用中打字 20 秒
  - 预期: 10 秒内 `debug.log` 出现 `UIA backoff expired, counter reset` 条目
  - 预期: 该条目出现后, 放大镜键盘跟随应在当前应用中自动恢复（无需切到记事本）
  - 若未恢复: 记录日志内容, 回到 2.1 检查逻辑
- [ ] 4.5 **长时运行验证**：让应用运行 ≥ 30 分钟, 正常使用浏览器/微信/QQ, 观察 `debug.log` 是否有异常日志堆积或不合理事件频率
- [ ] 4.6 查看 `debug.log` 总行数和文件大小, 确认日志粒度未失控（高频按键路径无日志）

## 5. 完成与清理

- [ ] 5.1 运行 `openspec validate fix-keyboard-tracking-latching-bug --strict` 确保 proposal/design/specs/tasks 一致
- [ ] 5.2 提交单个 commit, 消息格式: `fix(magnifier): 修复 UIA 熔断器归零 bug 使键盘跟随自恢复 + 补齐日志`
- [ ] 5.3 归档此 change: `openspec archive fix-keyboard-tracking-latching-bug`
