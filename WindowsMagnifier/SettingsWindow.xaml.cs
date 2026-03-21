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
    private readonly List<DisplayInfo> _displays;
    private bool _isLoading = true;
    private readonly DispatcherTimer _saveDebounce;

    public SettingsWindow(AppSettings settings, ConfigService configService, List<DisplayInfo> displays)
    {
        InitializeComponent();

        _settings = settings;
        _configService = configService;
        _displays = displays;

        // 无论如何关闭窗口都应通知调用方刷新（因为设置已即时保存）
        Closing += (_, _) =>
        {
            _saveDebounce.Stop();
            _configService.Save(_settings);
            if (DialogResult == null)
                DialogResult = true;
        };

        // Escape 键关闭窗口
        KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Escape)
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
