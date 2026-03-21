# 活跃上下文

**日期:** 2026-03-21
**项目:** 眼眸 (WindowsMagnifier) — 桌面放大辅助工具
**版本:** v1.2.1（未正式发布 Release，代码已推送 master）
**评分:** 9.0/10（代码质量），5.9/10（用户体验）
**测试:** 98/98 通过

## 当前状态

- master 分支最新提交 7b3ea22，已推送 GitHub
- 42→98 个单元测试（新增 HotkeyStringHelper 42 个 + FullScreenDetector 2 个 + AppSettings 12 个）
- 两轮 5 角色 10 代理全面审查完成，CRITICAL 从 5→0
- release/眼眸.exe 已更新（自包含单文件 68.5MB）

## 本次会话完成的工作

### Bug 修复（放大镜无响应）
- 移除异常处理器中的 MessageBox.Show，防止 Dispatcher 阻塞导致 UI 冻结
- 添加高频异常保护（5 秒 3 次强制退出）+ 致命异常(OOM/SOE/AVE)不吞掉
- OnRendering 添加 try-catch，异常时停止渲染并显示非活动遮罩
- RegisterHotKey/UnregisterHotKey 添加 SetLastError=true
- 全屏检测精确匹配路径增加 WS_MAXIMIZE 排除
- **全屏检测排除系统窗口（Progman/WorkerW/Shell_TrayWnd）** — 根因修复

### 新功能：可自定义快捷键
- 设置界面新增"快捷键"配置卡片，支持录制模式
- 全局切换快捷键（默认 Win+Alt+M）+ 当前屏幕切换快捷键（默认 Win+Alt+N）
- HotkeyStringHelper 纯工具类（解析/校验/格式化）
- HotkeyService 重写为多快捷键支持
- 快捷键录制即时验证 OS 注册，被占用时提示用户
- 录制时拒绝 NumPad 数字键

### 两轮全面审查修复
- P0: async void DebouncedCaretLookup → async Task
- P0: AppSettings 字典加锁保护并发访问
- P0: 致命异常不再被 e.Handled=true 吞掉
- P1: LogService 缓存目录和符号链接检查
- P1: DefaultDllImportSearchPaths(System32) 防 DLL 劫持
- P2: Mutex.ReleaseMutex 加持有者检查
- P2: 首次引导/关于窗口动态读取快捷键
- P2: NumPad 键拒绝

## 下一步（v2.0 方向）

### P0 — 所有用户都需要
1. 滚轮/快捷键调放大倍数（4/4 用户提出）
2. 设置界面字号提升到 13px+，对比度 >= 4.5:1
3. 系统托盘图标（NotifyIcon）

### P1 — 多数用户需要
4. 颜色反转/高对比度滤镜（MagSetColorEffect）
5. 放大区域位置可选（顶部/底部）
6. 焦点十字准星指示器

### 遗留审查建议（非阻塞）
- App.xaml.cs God Object（547行）→ 提取 WindowVisibilityManager
- 服务层接口抽象（当前测试覆盖率 18.2%）
- LogService 改为持久化文件句柄+缓冲写入
- 鼠标钩子冗余事件触发优化

## 技术债务（LOW 级别）
- DateTime.UtcNow.Ticks → 可替换为 Environment.TickCount64
- DisplayFocusManager 组合操作非原子（概率极低）
- FullScreenDetector 200ms 轮询可改用 ABN_FULLSCREENAPP 回调
