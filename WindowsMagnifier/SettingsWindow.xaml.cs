using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsMagnifier.Models;
using WindowsMagnifier.Services;

namespace WindowsMagnifier;

/// <summary>
/// 设置窗口 — 橙红渐变毛玻璃发光风格，所有修改即时生效并自动保存
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ConfigService _configService;
    private readonly HotkeyService _hotkeyService;
    private readonly List<DisplayInfo> _displays;
    private bool _isLoading = true;
    private readonly DispatcherTimer _saveDebounce;

    /// <summary>
    /// 当前正在录制快捷键的目标: null=不录制, "ToggleAll", "ToggleCurrent"
    /// </summary>
    private string? _recordingTarget;

    /// <summary>
    /// 录制状态下的渐变边框画刷（橙红色脉冲）
    /// </summary>
    private static readonly Brush RecordingBorderBrush = new LinearGradientBrush(
        System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x35),
        System.Windows.Media.Color.FromRgb(0xE8, 0x31, 0x3A), 0);

    private static readonly Brush NormalBorderBrush = new SolidColorBrush(
        System.Windows.Media.Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF));

    public SettingsWindow(AppSettings settings, ConfigService configService, List<DisplayInfo> displays, HotkeyService hotkeyService)
    {
        InitializeComponent();

        _settings = settings;
        _configService = configService;
        _hotkeyService = hotkeyService;
        _displays = displays;

        // 无论如何关闭窗口都应通知调用方刷新（因为设置已即时保存）
        Closing += (_, _) =>
        {
            _saveDebounce.Stop();
            _configService.Save(_settings);
            if (DialogResult == null)
                DialogResult = true;
        };

        // Escape 键关闭窗口（不在快捷键录制状态时）
        KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Escape && _recordingTarget == null)
            {
                DialogResult = true;
                Close();
            }
        };

        // Slider 保存防抖：拖拽期间只更新设置值，300ms 无变化后再写磁盘
        _saveDebounce = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(300) };
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); _configService.Save(_settings); };

        // 加载当前设置到 UI
        LoadSettings();
        _isLoading = false;
    }

    private void LoadSettings()
    {
        // 动态生成每显示器的放大倍数行
        DisplayMagnificationPanel.Children.Clear();
        foreach (var display in _displays)
        {
            var label = display.IsPrimary
                ? $"显示器 {display.Index + 1} (主) - {(int)display.Bounds.Width}x{(int)display.Bounds.Height}"
                : $"显示器 {display.Index + 1} - {(int)display.Bounds.Width}x{(int)display.Bounds.Height}";

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBlock = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(textBlock);

            // 渐变 Badge 显示倍数
            var currentLevel = _settings.GetMagnificationLevel(display.DeviceName);

            var badgeBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 4, 12, 4),
                Cursor = Cursors.Hand,
                Background = new LinearGradientBrush(
                    Color.FromRgb(0xFF, 0x6B, 0x35),
                    Color.FromRgb(0xE8, 0x31, 0x3A),
                    0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x6B, 0x35),
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.4
                }
            };

            var badgeText = new TextBlock
            {
                Text = $"{currentLevel}x",
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            badgeBorder.Child = badgeText;

            // ComboBox 隐藏在 Badge 后面
            var comboBox = new ComboBox
            {
                Width = 80,
                Tag = display.DeviceName,
                ItemsSource = Enumerable.Range(1, 16).ToList(),
                SelectedItem = currentLevel,
                Opacity = 0,
                IsHitTestVisible = false
            };
            comboBox.SelectionChanged += (s, e) =>
            {
                DisplayMagnification_Changed(s, e);
                if (comboBox.SelectedItem is int level)
                {
                    badgeText.Text = $"{level}x";
                }
            };

            // 点击 Badge 打开下拉
            badgeBorder.MouseLeftButtonDown += (_, _) =>
            {
                comboBox.IsDropDownOpen = true;
            };

            // 使用一个 Grid 叠加 badge 和 combo
            var badgeContainer = new Grid();
            badgeContainer.Children.Add(comboBox);
            badgeContainer.Children.Add(badgeBorder);

            Grid.SetColumn(badgeContainer, 1);
            grid.Children.Add(badgeContainer);

            DisplayMagnificationPanel.Children.Add(grid);
        }

        HeightSlider.Value = _settings.WindowHeight;
        FollowMouseButton.IsChecked = _settings.FollowMouse;
        FollowKeyboardButton.IsChecked = _settings.FollowKeyboardInput;
        StartMinimizedCheckBox.IsChecked = _settings.StartMinimized;
        HideOnFullScreenCheckBox.IsChecked = _settings.HideOnFullScreen;

        // 手动更新标签文本
        HeightValue.Text = _settings.WindowHeight.ToString();

        // 快捷键
        ToggleAllHotkeyText.Text = _settings.ToggleAllHotkey;
        ToggleCurrentHotkeyText.Text = _settings.ToggleCurrentHotkey;
    }

    /// <summary>
    /// 即时保存当前设置
    /// </summary>
    private void SaveNow()
    {
        _configService.Save(_settings);
    }

    private void DisplayMagnification_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        var comboBox = (ComboBox)sender;
        var deviceName = (string)comboBox.Tag;
        var level = (int)comboBox.SelectedItem;
        _settings.SetMagnificationLevel(deviceName, level);
        SaveNow();
    }

    private void HeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        var value = (int)e.NewValue;
        HeightValue.Text = value.ToString();
        _settings.WindowHeight = value;
        // 防抖保存：拖拽期间只更新设置值，停止拖拽后 300ms 才写磁盘
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void FollowMouseButton_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.FollowMouse = FollowMouseButton.IsChecked ?? false;
        SaveNow();
    }

    private void FollowKeyboardButton_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.FollowKeyboardInput = FollowKeyboardButton.IsChecked ?? false;
        SaveNow();
    }

    private void StartMinimized_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.StartMinimized = StartMinimizedCheckBox.IsChecked ?? false;
        SaveNow();
    }

    private void HideOnFullScreen_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.HideOnFullScreen = HideOnFullScreenCheckBox.IsChecked ?? true;
        SaveNow();
    }

    #region 快捷键录制

    private void ToggleAllHotkeyBox_Click(object sender, MouseButtonEventArgs e)
    {
        StartRecording("ToggleAll");
        e.Handled = true;
    }

    private void ToggleCurrentHotkeyBox_Click(object sender, MouseButtonEventArgs e)
    {
        StartRecording("ToggleCurrent");
        e.Handled = true;
    }

    private void StartRecording(string target)
    {
        // 如果已在录制另一个，先停止
        StopRecording();

        _recordingTarget = target;

        if (target == "ToggleAll")
        {
            ToggleAllHotkeyText.Text = "按下快捷键...";
            ToggleAllHotkeyBox.BorderBrush = RecordingBorderBrush;
            ToggleAllHotkeyBox.Background = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x20, 0xFF, 0x6B, 0x35));
        }
        else
        {
            ToggleCurrentHotkeyText.Text = "按下快捷键...";
            ToggleCurrentHotkeyBox.BorderBrush = RecordingBorderBrush;
            ToggleCurrentHotkeyBox.Background = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x20, 0xFF, 0x6B, 0x35));
        }

        // 订阅键盘事件（使用 PreviewKeyDown 以捕获系统键如 Alt）
        PreviewKeyDown += OnHotkeyRecordKeyDown;

        // 点击窗口其他区域取消录制
        PreviewMouseDown += OnRecordingMouseDown;
    }

    private void StopRecording()
    {
        if (_recordingTarget == null) return;

        PreviewKeyDown -= OnHotkeyRecordKeyDown;
        PreviewMouseDown -= OnRecordingMouseDown;

        // 恢复样式
        if (_recordingTarget == "ToggleAll")
        {
            ToggleAllHotkeyText.Text = _settings.ToggleAllHotkey;
            ToggleAllHotkeyBox.BorderBrush = NormalBorderBrush;
            ToggleAllHotkeyBox.Background = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
        }
        else if (_recordingTarget == "ToggleCurrent")
        {
            ToggleCurrentHotkeyText.Text = _settings.ToggleCurrentHotkey;
            ToggleCurrentHotkeyBox.BorderBrush = NormalBorderBrush;
            ToggleCurrentHotkeyBox.Background = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
        }

        _recordingTarget = null;
    }

    private void OnRecordingMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 检查点击是否在快捷键框上（如果是，由框自己的 Click 事件处理）
        var source = e.OriginalSource as DependencyObject;
        while (source != null)
        {
            if (source == ToggleAllHotkeyBox || source == ToggleCurrentHotkeyBox)
                return;
            source = VisualTreeHelper.GetParent(source);
        }

        StopRecording();
    }

    private void OnHotkeyRecordKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // 获取实际按键（处理 Alt 等系统键）
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 忽略纯修饰键按下
        if (key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin)
            return;

        // 拒绝小键盘数字键（NumPad 的 VK 码与主键盘不同，会导致快捷键不生效）
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return;

        // Escape 取消录制
        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }

        // 获取当前修饰键
        var modifiers = Keyboard.Modifiers;

        // 至少需要一个修饰键
        if (modifiers == ModifierKeys.None)
            return;

        // 格式化快捷键字符串
        var hotkeyStr = HotkeyStringHelper.Format(modifiers, key);
        if (string.IsNullOrEmpty(hotkeyStr))
            return; // 不支持的按键

        // 校验合法性
        if (!HotkeyStringHelper.IsValid(hotkeyStr))
            return;

        // 检查冲突
        var target = _recordingTarget!;
        var otherHotkey = target == "ToggleAll" ? _settings.ToggleCurrentHotkey : _settings.ToggleAllHotkey;
        if (string.Equals(hotkeyStr, otherHotkey, StringComparison.OrdinalIgnoreCase))
        {
            HotkeyConflictWarning.Text = "两个快捷键不能相同";
            HotkeyConflictWarning.Visibility = Visibility.Visible;
            return;
        }

        // 验证快捷键是否可被操作系统注册（跳过与当前值相同的情况，因为应用自身已注册）
        var currentHotkey = target == "ToggleAll" ? _settings.ToggleAllHotkey : _settings.ToggleCurrentHotkey;
        if (!string.Equals(hotkeyStr, currentHotkey, StringComparison.OrdinalIgnoreCase)
            && !_hotkeyService.TestRegister(hotkeyStr))
        {
            HotkeyConflictWarning.Text = "该快捷键已被其他程序占用";
            HotkeyConflictWarning.Visibility = Visibility.Visible;
            return;
        }

        HotkeyConflictWarning.Visibility = Visibility.Collapsed;

        // 保存并更新 UI
        PreviewKeyDown -= OnHotkeyRecordKeyDown;
        PreviewMouseDown -= OnRecordingMouseDown;

        if (target == "ToggleAll")
        {
            _settings.ToggleAllHotkey = hotkeyStr;
            ToggleAllHotkeyText.Text = hotkeyStr;
            ToggleAllHotkeyBox.BorderBrush = NormalBorderBrush;
            ToggleAllHotkeyBox.Background = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
        }
        else
        {
            _settings.ToggleCurrentHotkey = hotkeyStr;
            ToggleCurrentHotkeyText.Text = hotkeyStr;
            ToggleCurrentHotkeyBox.BorderBrush = NormalBorderBrush;
            ToggleCurrentHotkeyBox.Background = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
        }

        _recordingTarget = null;
        SaveNow();
    }

    #endregion

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // 设置已即时保存，直接关闭并通知调用方刷新
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// 无边框窗口拖拽移动
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
