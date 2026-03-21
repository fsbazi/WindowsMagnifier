# 活跃上下文

**日期:** 2026-03-21
**项目:** 眼眸 (WindowsMagnifier) — 桌面放大辅助工具
**版本:** v1.2.0（已发布到 GitHub）
**评分:** 9.0/10（代码质量），5.9/10（用户体验，四位视障用户视角）

## 当前状态

- v1.2.0 已发布到 GitHub Release，旧版本（v1.0.0, v1.1.0）已清理
- 42 个单元测试全部通过
- App.xaml.cs 461 行（从 523 减少）
- 零 CRITICAL/HIGH 问题
- 多显示器支持完善（8 场景全部通过）

## 本次会话完成的工作

### v1.1 迭代（代码修复与加固，11 个提交）
- IME 合成检查移除，修复中文输入时放大镜不跟随
- 鼠标光标漂移修复（InvalidateRect 分离刷新）
- UI Automation 回退支持浏览器等现代应用键盘跟踪
- 全屏检测放宽 WS_CAPTION、排除 cloaked 窗口和最大化窗口
- 统一 LogService（符号链接防护 + 文件轮转 + 独占锁）
- 配置值除零保护（Sanitize + Math.Clamp 双层防御）
- 显示器热插拔 500ms 防抖
- MouseHook/KeyboardHook 异常保护和空指针检查
- captureRect 溢出屏幕边界保护
- Airspace 修复（ShowWindow Hide/Show Magnifier 子窗口）

### v1.2 迭代（架构优化 + 测试，10 个提交）
- 热插拔后重新初始化 DisplayFocusManager 和 FullScreenDetector
- 日志级别修正 + ShowFirstRunGuide 版本号动态化
- Magnifier 子窗口 resize 代码去重（ResizeMagnifierChild）
- UIA 连续超时降级机制（3 次超时退避 10 秒）
- ConfigService.Save 原子写入（tmp + File.Move）
- 提取 HotkeyService（App 减少约 40 行）
- 统一 RECT 定义到 NativeTypes（消除 4 处重复）
- HotkeyService + AppBarService 64 位安全加固（wParam.ToInt32）
- 42 个单元测试（AppSettings、DisplayInfo、ConfigService、FullScreenDetector）

### 修复的实际运行 Bug
- Airspace 导致非活动窗口 Magnifier 子窗口未正确隐藏
- 日志级别、防抖泄漏、冗余回调、UIA 计数器 4 个小问题

### 多轮审查（6 轮技术审查 + 1 轮用户视角审查）
- 5 角色技术审查：代码审查、安全审计、性能工程、QA、架构
- 4 位视障用户视角审查：老年患者、程序员、特教老师、设计师

## 下一步（v2.0 方向）

### P0 — 所有用户都需要
1. 滚轮/快捷键调放大倍数（4/4 用户提出）
2. 设置界面字号提升到 13px+，对比度 >= 4.5:1
3. 系统托盘图标（NotifyIcon）

### P1 — 多数用户需要
4. 颜色反转/高对比度滤镜（MagSetColorEffect）
5. 放大区域位置可选（顶部/底部）
6. 焦点十字准星指示器

### P2 — 特定场景
7. 全屏应用覆盖模式（教育场景）
8. 学生预设配置管理
9. 镜头模式（跟随鼠标浮动窗口）

## 技术债务（LOW 级别）
- async void DebouncedCaretLookup → 应改为 async Task
- DateTime.UtcNow.Ticks → 可替换为 Environment.TickCount64
- DisplayFocusManager 组合操作非原子（概率极低）
