using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WindowsMagnifier.Models;
using WindowsMagnifier.Services;

namespace WindowsMagnifier;

/// <summary>
/// 设置窗口
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ConfigService _configService;
    private readonly List<DisplayInfo> _displays;
    private bool _isLoading = true;

    // 备份原始设置值（用于取消时恢复）
    private readonly int _backupMagnificationLevel;
    private readonly Dictionary<string, int>? _backupDisplayMagnificationLevels;
    private readonly int _backupWindowHeight;
    private readonly bool _backupFollowMouse;
    private readonly bool _backupFollowKeyboardInput;
    private readonly bool _backupStartMinimized;
    private readonly bool _backupHideOnFullScreen;

    public SettingsWindow(AppSettings settings, ConfigService configService, List<DisplayInfo> displays)
    {
        InitializeComponent();

        _settings = settings;
        _configService = configService;
        _displays = displays;

        // 备份当前设置
        _backupMagnificationLevel = settings.MagnificationLevel;
        _backupDisplayMagnificationLevels = settings.DisplayMagnificationLevels != null
            ? new Dictionary<string, int>(settings.DisplayMagnificationLevels)
            : null;
        _backupWindowHeight = settings.WindowHeight;
        _backupFollowMouse = settings.FollowMouse;
        _backupFollowKeyboardInput = settings.FollowKeyboardInput;
        _backupStartMinimized = settings.StartMinimized;
        _backupHideOnFullScreen = settings.HideOnFullScreen;

        // 加载当前设置到 UI
        LoadSettings();
        _isLoading = false;
    }

    private void LoadSettings()
    {
        // 动态生成每显示器的放大倍数下拉框
        DisplayMagnificationPanel.Children.Clear();
        foreach (var display in _displays)
        {
            var label = display.IsPrimary
                ? $"显示器 {display.Index + 1} (主) - {(int)display.Bounds.Width}x{(int)display.Bounds.Height}"
                : $"显示器 {display.Index + 1} - {(int)display.Bounds.Width}x{(int)display.Bounds.Height}";

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBlock = new TextBlock
            {
                Text = label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(textBlock);

            var comboBox = new ComboBox
            {
                Width = 80,
                Tag = display.DeviceName,
                ItemsSource = Enumerable.Range(1, 16).ToList(),
                SelectedItem = _settings.GetMagnificationLevel(display.DeviceName)
            };
            comboBox.SelectionChanged += DisplayMagnification_Changed;
            Grid.SetColumn(comboBox, 1);
            grid.Children.Add(comboBox);

            DisplayMagnificationPanel.Children.Add(grid);
        }

        HeightSlider.Value = _settings.WindowHeight;
        FollowMouseCheckBox.IsChecked = _settings.FollowMouse;
        FollowKeyboardInputCheckBox.IsChecked = _settings.FollowKeyboardInput;
        StartMinimizedCheckBox.IsChecked = _settings.StartMinimized;
        HideOnFullScreenCheckBox.IsChecked = _settings.HideOnFullScreen;

        // 手动更新标签文本（_isLoading 阻止了 ValueChanged 事件）
        HeightValue.Text = _settings.WindowHeight.ToString();
    }

    private void DisplayMagnification_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        var comboBox = (ComboBox)sender;
        var deviceName = (string)comboBox.Tag;
        var level = (int)comboBox.SelectedItem;
        _settings.SetMagnificationLevel(deviceName, level);
    }

    private void HeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        var value = (int)e.NewValue;
        HeightValue.Text = value.ToString();
        _settings.WindowHeight = value;
    }

    private void FollowMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.FollowMouse = FollowMouseCheckBox.IsChecked ?? false;
        _settings.FollowKeyboardInput = FollowKeyboardInputCheckBox.IsChecked ?? false;
    }

    private void StartMinimized_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.StartMinimized = StartMinimizedCheckBox.IsChecked ?? false;
    }

    private void HideOnFullScreen_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _settings.HideOnFullScreen = HideOnFullScreenCheckBox.IsChecked ?? true;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _configService.Save(_settings);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // 还原备份值
        _settings.MagnificationLevel = _backupMagnificationLevel;
        _settings.DisplayMagnificationLevels = _backupDisplayMagnificationLevels != null
            ? new Dictionary<string, int>(_backupDisplayMagnificationLevels)
            : null;
        _settings.WindowHeight = _backupWindowHeight;
        _settings.FollowMouse = _backupFollowMouse;
        _settings.FollowKeyboardInput = _backupFollowKeyboardInput;
        _settings.StartMinimized = _backupStartMinimized;
        _settings.HideOnFullScreen = _backupHideOnFullScreen;

        DialogResult = false;
        Close();
    }
}
