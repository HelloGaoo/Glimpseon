using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.ViewManagement;
using ClassLively_UI.Services;
using ClassLively_UI.Controls;

using AppColorHelper = ClassLively_UI.Controls.ColorHelper;

namespace ClassLively_UI.Dialogs;

/// <summary>
/// 组件设置弹窗
/// </summary>
public static class ComponentSettingDialog
{
    //  公开方法入口
    /// <summary>
    /// 创建指定组件的设置弹窗
    /// </summary>
    public static ContentDialog Create(string componentId, IApiService api, XamlRoot xamlRoot)
    {
        var dialog = new ContentDialog
        {
            Title = $"{DisplayName(componentId)}设置",
            PrimaryButtonText = "完成",
            XamlRoot = xamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        var rootPanel = new StackPanel { Spacing = 16 };
        var allControls = new List<Control>(); // Enable 开关联动

        switch (componentId)
        {
            case "clock":
                BuildClockSettings(rootPanel, api, allControls);
                break;
            case "weather":
                BuildWeatherSettings(rootPanel, api, allControls);
                break;
            case "poetry":
                BuildPoetrySettings(rootPanel, api, allControls);
                break;
            case "countdown":
                BuildCountdownSettings(rootPanel, api, allControls);
                break;
            case "school_info":
                BuildSchoolInfoSettings(rootPanel, api, allControls);
                break;
            case "media":
                BuildMediaSettings(rootPanel, api, allControls);
                break;
            case "quick_launch":
                BuildQuickLaunchSettings(rootPanel, api, allControls);
                break;
            default:
                rootPanel.Children.Add(new TextBlock { Text = $"未知组件: {componentId}" });
                break;
        }

        dialog.Content = new ScrollViewer
        {
            Padding = new Thickness(24),
            Content = rootPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        return dialog;
    }


    //  组件名称映射
    private static string DisplayName(string id) => id switch
    {
        "clock" => "时钟",
        "weather" => "天气",
        "poetry" => "一言",
        "countdown" => "倒计时",
        "school_info" => "学校信息",
        "media" => "媒体信息",
        "quick_launch" => "快捷启动",
        _ => id
    };

    //  辅助构建方法
    /// <summary>创建设置行：左侧标签 右侧控件</summary>
    private static StackPanel CreateSettingRow(string label, UIElement control)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Width = 100,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = new FontWeight { Weight = 500 }
                },
                control
            }
        };
    }

    /// <summary>创建颜色选择器行：预设按钮 自定义按钮</summary>
    private static StackPanel CreateColorPickerRow(
        string label,
        string defaultHex,
        IApiService api,
        string configKey,
        Action<string>? onColorChanged = null)
    {
        var currentColorHex = defaultHex;

        // 先声明自定义颜色按钮
        Button? customBtn = null;

        // 预设颜色按钮
        var whiteBtn = CreateColorPresetButton("#FFFFFF", () =>
        {
            currentColorHex = "#FFFFFF";
            UpdateColorButtonBackground(customBtn, currentColorHex);
            _ = api.SetConfigAsync(configKey, "#FFFFFF");
            onColorChanged?.Invoke("#FFFFFF");
        });

        var blackBtn = CreateColorPresetButton("#000000", () =>
        {
            currentColorHex = "#000000";
            UpdateColorButtonBackground(customBtn, currentColorHex);
            _ = api.SetConfigAsync(configKey, "#000000");
            onColorChanged?.Invoke("#000000");
        });

        var greenBtn = CreateColorPresetButton("#30C361", () =>
        {
            currentColorHex = "#30C361";
            UpdateColorButtonBackground(customBtn, currentColorHex);
            _ = api.SetConfigAsync(configKey, "#30C361");
            onColorChanged?.Invoke("#30C361");
        });

        // 壁纸色按钮（暂用主题绿）todo
        var wallpaperBtn = new Button
        {
            Content = "壁",
            Width = 40,
            Height = 24,
            Padding = new Thickness(0, 0, 0, 0),
            FontSize = 11,
            Background = new SolidColorBrush(AppColorHelper.FromRgb(48, 195, 97)),
            Foreground = new SolidColorBrush(AppColorHelper.White),
            CornerRadius = new CornerRadius(4)
        };
        ToolTipService.SetToolTip(wallpaperBtn, "壁纸取色");
        wallpaperBtn.Click += (_, _) =>
        {
            System.Diagnostics.Debug.WriteLine($"[ComponentSettingDialog] todo壁纸取色(key: {configKey})");
        };

        // 系统强调色按钮
        var systemAccentBtn = new Button
        {
            Content = "系",
            Width = 40,
            Height = 24,
            Padding = new Thickness(0, 0, 0, 0),
            FontSize = 11,
            Background = new SolidColorBrush(UIColorFromSystemAccent()),
            Foreground = new SolidColorBrush(AppColorHelper.White),
            CornerRadius = new CornerRadius(4)
        };
        ToolTipService.SetToolTip(systemAccentBtn, "系统强调色");
        systemAccentBtn.Click += (_, _) =>
        {
            try
            {
                var accentColor = UIColorFromSystemAccent();
                var hex = $"#{accentColor.R:X2}{accentColor.G:X2}{accentColor.B:X2}";
                currentColorHex = hex;
                UpdateColorButtonBackground(customBtn, hex);
                _ = api.SetConfigAsync(configKey, hex);
                onColorChanged?.Invoke(hex);
            }
            catch { /* 静默 */ }
        };

        // 自定义颜色按钮
        customBtn = new Button
        {
            Width = 40,
            Height = 24,
            Padding = new Thickness(0, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(HexToColor(defaultHex)),
            BorderThickness = new Thickness(1, 1, 1, 1),
            BorderBrush = new SolidColorBrush(AppColorHelper.FromRgba(200, 200, 200, 150))
        };
        ToolTipService.SetToolTip(customBtn, "自定义颜色");
        customBtn.Click += async (_, _) =>
        {
            try
            {
                // ColorPicker 弹窗选择自定义颜色
                var colorPickerDialog = new ContentDialog
                {
                    Title = "选择颜色",
                    XamlRoot = customBtn.XamlRoot,
                    CloseButtonText = "取消",
                    PrimaryButtonText = "确定"
                };

                var colorPicker = new ColorPicker();
                colorPickerDialog.Content = colorPicker;

                var result = await colorPickerDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var selectedColor = colorPicker.Color;
                    var hex = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
                    currentColorHex = hex;
                    UpdateColorButtonBackground(customBtn, hex);
                    _ = api.SetConfigAsync(configKey, hex);
                    onColorChanged?.Invoke(hex);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ComponentSettingDialog] ColorPicker 异常: {ex.Message}");
            }
        };

        var colorButtonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { whiteBtn, blackBtn, greenBtn, wallpaperBtn, systemAccentBtn, customBtn }
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Width = 100,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = new FontWeight { Weight = 500 }
                },
                colorButtonsPanel
            }
        };
    }

    /// <summary>创建折叠分组</summary>
    private static Expander CreateExpanderGroup(string headerText, bool isExpanded, params UIElement[] children)
    {
        var contentPanel = new StackPanel { Spacing = 12 };
        foreach (var child in children)
            contentPanel.Children.Add(child);

        return new Expander
        {
            Header = new TextBlock
            {
                Text = headerText,
                FontSize = 15,
                FontWeight = new FontWeight { Weight = 600 },
                Foreground = new SolidColorBrush(AppColorHelper.FromRgb(50, 50, 50))
            },
            IsExpanded = isExpanded,
            Content = contentPanel
        };
    }

    /// <summary>创建预设颜色按钮</summary>
    private static Button CreateColorPresetButton(string hex, Action onClick)
    {
        var btn = new Button
        {
            Width = 40,
            Height = 24,
            Padding = new Thickness(0, 0, 0, 0),
            Background = new SolidColorBrush(HexToColor(hex)),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1, 1, 1, 1),
            BorderBrush = new SolidColorBrush(AppColorHelper.FromRgba(180, 180, 180, 120))
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    /// <summary>更新自定义颜色按钮背景</summary>
    private static void UpdateColorButtonBackground(Button btn, string hex)
    {
        if (btn.Background is SolidColorBrush brush)
            brush.Color = HexToColor(hex);
    }

    /// <summary>HEX → Color</summary>
    private static Color HexToColor(string hex)
    {
        if (!hex.StartsWith("#") || hex.Length < 7) return AppColorHelper.White;
        var r = Convert.ToByte(hex.Substring(1, 2), 16);
        var g = Convert.ToByte(hex.Substring(3, 2), 16);
        var b = Convert.ToByte(hex.Substring(5, 2), 16);
        byte a = 255;
        if (hex.Length >= 9)
            a = Convert.ToByte(hex.Substring(7, 2), 16);
        return Color.FromArgb(a, r, g, b);
    }

    /// <summary>获取系统强调色</summary>
    private static Color UIColorFromSystemAccent()
    {
        try
        {
            var uiSettings = new UISettings();
            var accentColor = uiSettings.GetColorValue(UIColorType.Accent);
            return accentColor;
        }
        catch
        {
            return AppColorHelper.FromRgb(48, 195, 97); // 默认主题绿
        }
    }

    /// <summary>创建范围 NumberBox</summary>
    private static NumberBox CreateNumberBox(double min, double max, double defaultValue, double step = 1)
    {
        return new NumberBox
        {
            Minimum = min,
            Maximum = max,
            Value = defaultValue,
            SmallChange = step,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 120
        };
    }

    /// <summary>创建选项 ComboBox</summary>
    private static ComboBox CreateComboBox(params (string Display, object Value)[] items)
    {
        var comboBox = new ComboBox { Width = 160 };
        foreach (var (display, value) in items)
        {
            comboBox.Items.Add(new ComboBoxItem { Content = display, Tag = value });
        }
        if (comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
        return comboBox;
    }

    /// <summary>创建 ToggleSwitch 绑定到配置键</summary>
    private static ToggleSwitch CreateToggleSwitch(bool defaultOn, IApiService api, string key, List<Control>? linkedControls = null)
    {
        var toggle = new ToggleSwitch { IsOn = defaultOn };
        toggle.Toggled += async (_, _) =>
        {
            try
            {
                await api.SetConfigAsync(key, toggle.IsOn);

                // 联动：ToggleSwitch 关闭时禁用其他控件    
                if (linkedControls != null && !toggle.IsOn)
                {
                    foreach (var ctrl in linkedControls)
                        ctrl.IsEnabled = false;
                }
                else if (linkedControls != null && toggle.IsOn)
                {
                    foreach (var ctrl in linkedControls)
                        ctrl.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ComponentSettingDialog] 设置 {key} 失败: {ex.Message}");
            }
        };
        return toggle;
    }

    //  时钟 (clock)

    private static void BuildClockSettings(StackPanel root, IApiService api, List<Control> allControls)
    {
        var basicChildren = new List<UIElement>();
        var advancedChildren = new List<UIElement>();

        // ── 基本 ──
        var enableClock = CreateToggleSwitch(true, api, "showClock", allControls);
        basicChildren.Add(CreateSettingRow("启用时钟", enableClock));
        allControls.Add(enableClock);

        var showSeconds = CreateToggleSwitch(true, api, "showClockSeconds");
        basicChildren.Add(CreateSettingRow("显示秒数", showSeconds));
        allControls.Add(showSeconds);

        var showLunar = CreateToggleSwitch(false, api, "showLunarCalendar");
        basicChildren.Add(CreateSettingRow("显示农历", showLunar));
        allControls.Add(showLunar);

        // ── 高级 ──
        advancedChildren.Add(CreateColorPickerRow("时钟颜色", "#FFFFFF", api, "clockColor"));

        var clockSizeBox = CreateNumberBox(80, 200, 96);
        clockSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("clockSize", clockSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("时钟大小", clockSizeBox));
        allControls.Add(clockSizeBox);

        var dateSizeBox = CreateNumberBox(12, 50, 18);
        dateSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("dateSize", dateSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("日期字号", dateSizeBox));
        allControls.Add(dateSizeBox);

        root.Children.Add(CreateExpanderGroup("基本设置", true, basicChildren.ToArray()));
        root.Children.Add(CreateExpanderGroup("高级设置", false, advancedChildren.ToArray()));
    }

    //  天气 (weather)
    private static void BuildWeatherSettings(StackPanel root, IApiService api, List<Control> allControls)
    {
        var basicChildren = new List<UIElement>();
        var advancedChildren = new List<UIElement>();

        // ── 基本 ──
        var enableWeather = CreateToggleSwitch(true, api, "showWeather", allControls);
        basicChildren.Add(CreateSettingRow("启用天气", enableWeather));
        allControls.Add(enableWeather);

        var cityTextBox = new TextBox
        {
            PlaceholderText = "输入城市名",
            Width = 160,
            MaxLength = 20
        };
        cityTextBox.TextChanged += async (_, _) =>
        {
            try { await api.SetConfigAsync("city", cityTextBox.Text); } catch { }
        };
        basicChildren.Add(CreateSettingRow("所在城市", cityTextBox));
        allControls.Add(cityTextBox);

        // ── 高级 ──
        var textSizeBox = CreateNumberBox(5, 50, 28);
        textSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("weatherSize", textSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("文字大小", textSizeBox));
        allControls.Add(textSizeBox);

        advancedChildren.Add(CreateColorPickerRow("文字颜色", "#FFFFFF", api, "weatherTextColor"));

        var iconSizeBox = CreateNumberBox(32, 128, 48);
        iconSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("weatherIconSize", iconSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("图标大小", iconSizeBox));
        allControls.Add(iconSizeBox);

        var updateIntervalCombo = CreateComboBox(
            ("关闭", -1), ("5分钟", 5), ("15分钟", 15), ("30分钟", 30),
            ("1小时", 60), ("3小时", 180), ("6小时", 360), ("12小时", 720), ("24小时", 1440)
        );
        updateIntervalCombo.SelectionChanged += async (_, _) =>
        {
            if (updateIntervalCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
                try { await api.SetConfigAsync("weatherUpdateInterval", item.Tag); } catch { }
        };
        advancedChildren.Add(CreateSettingRow("刷新间隔", updateIntervalCombo));
        allControls.Add(updateIntervalCombo);

        root.Children.Add(CreateExpanderGroup("基本设置", true, basicChildren.ToArray()));
        root.Children.Add(CreateExpanderGroup("高级设置", false, advancedChildren.ToArray()));
    }


    //  一言/诗词 (poetry)
    private static void BuildPoetrySettings(StackPanel root, IApiService api, List<Control> allControls)
    {
        var basicChildren = new List<UIElement>();
        var advancedChildren = new List<UIElement>();

        // ── 基本 ──
        var enablePoetry = CreateToggleSwitch(true, api, "showPoetry", allControls);
        basicChildren.Add(CreateSettingRow("启用一言", enablePoetry));
        allControls.Add(enablePoetry);

        var apiSourceCombo = CreateComboBox(
            ("一言API", "https://v1.hitokoto.cn/"),
            ("诗词API", "https://api.xingzhige.com/API/poemy/")
        );
        apiSourceCombo.SelectionChanged += async (_, _) =>
        {
            if (apiSourceCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
                try { await api.SetConfigAsync("poetryApiUrl", item.Tag.ToString()); } catch { }
        };
        basicChildren.Add(CreateSettingRow("API来源", apiSourceCombo));
        allControls.Add(apiSourceCombo);

        // ── 高级 ──
        var textSizeBox = CreateNumberBox(12, 50, 16);
        textSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("poetrySize", textSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("文字大小", textSizeBox));
        allControls.Add(textSizeBox);

        advancedChildren.Add(CreateColorPickerRow("文字颜色", "#FFFFFF", api, "poetryTextColor"));

        var updateIntervalCombo = CreateComboBox(
            ("关闭", -1), ("5分钟", 5), ("10分钟", 10), ("30分钟", 30),
            ("1小时", 60), ("3小时", 180), ("6小时", 360), ("12小时", 720), ("1天", 1440)
        );
        updateIntervalCombo.SelectionChanged += async (_, _) =>
        {
            if (updateIntervalCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
                try { await api.SetConfigAsync("poetryUpdateInterval", item.Tag); } catch { }
        };
        advancedChildren.Add(CreateSettingRow("刷新间隔", updateIntervalCombo));
        allControls.Add(updateIntervalCombo);

        root.Children.Add(CreateExpanderGroup("基本设置", true, basicChildren.ToArray()));
        root.Children.Add(CreateExpanderGroup("高级设置", false, advancedChildren.ToArray()));
    }

    //  倒计时 (countdown)
    private static void BuildCountdownSettings(StackPanel root, IApiService api, List<Control> allControls)
    {
        var basicChildren = new List<UIElement>();
        var advancedChildren = new List<UIElement>();

        // ── 基本 ──
        var enableCountdown = CreateToggleSwitch(true, api, "showCountdown", allControls);
        basicChildren.Add(CreateSettingRow("启用倒计时", enableCountdown));
        allControls.Add(enableCountdown);

        var displayModeCombo = CreateComboBox(
            ("同时显示", "simultaneous"),
            ("轮播模式", "carousel")
        );
        displayModeCombo.SelectionChanged += async (_, _) =>
        {
            if (displayModeCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
                try { await api.SetConfigAsync("countdownDisplayMode", item.Tag.ToString()); } catch { }
        };
        basicChildren.Add(CreateSettingRow("显示模式", displayModeCombo));
        allControls.Add(displayModeCombo);

        var carouselIntervalBox = CreateNumberBox(1, 60, 5);
        carouselIntervalBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("countdownCarouselInterval", carouselIntervalBox.Value);
        basicChildren.Add(CreateSettingRow("轮播间隔(秒)", carouselIntervalBox));
        allControls.Add(carouselIntervalBox);

        // ── 高级 ──
        advancedChildren.Add(CreateColorPickerRow("文字颜色", "#FFFFFF", api, "countdownTextColor"));
        advancedChildren.Add(CreateColorPickerRow("连接符颜色", "#30C361", api, "countdownConnectorColor"));

        var textSizeBox = CreateNumberBox(12, 120, 32);
        textSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("countdownTextSize", textSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("文字大小", textSizeBox));
        allControls.Add(textSizeBox);

        var connectorSizeBox = CreateNumberBox(12, 60, 18);
        connectorSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("countdownConnectorSize", connectorSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("连接符大小", connectorSizeBox));
        allControls.Add(connectorSizeBox);

        root.Children.Add(CreateExpanderGroup("基本设置", true, basicChildren.ToArray()));
        root.Children.Add(CreateExpanderGroup("高级设置", false, advancedChildren.ToArray()));

        // ── 倒计时列表管理区（占位）──
        var listSectionHeader = new TextBlock
        {
            Text = "倒计时事件管理",
            FontSize = 15,
            FontWeight = new FontWeight { Weight = 600 },
            Margin = new Thickness(0, 8, 0, 4),
            Foreground = new SolidColorBrush(AppColorHelper.FromRgb(50, 50, 50))
        };
        root.Children.Add(listSectionHeader);

        var addBtn = new Button { Content = "+ 添加", Padding = new Thickness(16, 6, 16, 6) };
        var editBtn = new Button { Content = "编辑", Padding = new Thickness(16, 6, 16, 6) };
        var deleteBtn = new Button { Content = "删除", Padding = new Thickness(16, 6, 16, 6) };

        var listPlaceholder = new Border
        {
            Background = new SolidColorBrush(AppColorHelper.FromRgba(245, 245, 245, 255)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 16, 16, 16),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "暂无倒计时事件",
                        FontSize = 13,
                        Opacity = 0.55,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 8,
                        Children = { addBtn, editBtn, deleteBtn }
                    }
                }
            }
        };
        root.Children.Add(listPlaceholder);
    }

    //  学校信息 (school_info)
    private static void BuildSchoolInfoSettings(StackPanel root, IApiService api, List<Control> allControls)
    {
        var basicChildren = new List<UIElement>();
        var advancedChildren = new List<UIElement>();

        // ── 基本 ──
        var enableSchoolInfo = CreateToggleSwitch(true, api, "showSchoolInfo", allControls);
        basicChildren.Add(CreateSettingRow("启用学校信息", enableSchoolInfo));
        allControls.Add(enableSchoolInfo);

        var schoolClassTextBox = new TextBox
        {
            PlaceholderText = "例如: 高三(1)班",
            Width = 160,
            MaxLength = 30
        };
        schoolClassTextBox.TextChanged += async (_, _) =>
        {
            try { await api.SetConfigAsync("schoolClass", schoolClassTextBox.Text); } catch { }
        };
        basicChildren.Add(CreateSettingRow("班级名称", schoolClassTextBox));
        allControls.Add(schoolClassTextBox);

        var schoolNameTextBox = new TextBox
        {
            PlaceholderText = "例如: XX中学",
            Width = 160,
            MaxLength = 40
        };
        schoolNameTextBox.TextChanged += async (_, _) =>
        {
            try { await api.SetConfigAsync("school", schoolNameTextBox.Text); } catch { }
        };
        basicChildren.Add(CreateSettingRow("学校名称", schoolNameTextBox));
        allControls.Add(schoolNameTextBox);

        // ── 高级 ──
        advancedChildren.Add(CreateColorPickerRow("文字颜色", "#FFFFFF", api, "schoolInfoTextColor"));

        var textSizeBox = CreateNumberBox(12, 60, 20);
        textSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("schoolInfoTextSize", textSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("文字大小", textSizeBox));
        allControls.Add(textSizeBox);

        root.Children.Add(CreateExpanderGroup("基本设置", true, basicChildren.ToArray()));
        root.Children.Add(CreateExpanderGroup("高级设置", false, advancedChildren.ToArray()));
    }

    //  媒体信息 (media)

    private static void BuildMediaSettings(StackPanel root, IApiService api, List<Control> allControls)
    {
        var basicChildren = new List<UIElement>();
        var advancedChildren = new List<UIElement>();

        // ── 基本 ──
        var enableMedia = CreateToggleSwitch(true, api, "showMediaInfo", allControls);
        basicChildren.Add(CreateSettingRow("启用媒体信息", enableMedia));
        allControls.Add(enableMedia);

        var showCover = CreateToggleSwitch(true, api, "showMediaCover");
        basicChildren.Add(CreateSettingRow("显示封面", showCover));
        allControls.Add(showCover);

        var showProgress = CreateToggleSwitch(true, api, "showMediaProgress");
        basicChildren.Add(CreateSettingRow("显示进度条", showProgress));
        allControls.Add(showProgress);

        var showLyrics = CreateToggleSwitch(false, api, "showMediaLyrics");
        basicChildren.Add(CreateSettingRow("显示歌词", showLyrics));
        allControls.Add(showLyrics);

        // ── 高级: 尺寸 ──
        var sizeSectionLabel = new TextBlock
        {
            Text = "— 尺寸设置 —",
            FontSize = 13,
            Opacity = 0.6,
            Margin = new Thickness(0, 4, 0, 0)
        };
        advancedChildren.Add(sizeSectionLabel);

        var widthBox = CreateNumberBox(200, 800, 400);
        widthBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("mediaWidth", widthBox.Value);
        advancedChildren.Add(CreateSettingRow("宽度", widthBox));
        allControls.Add(widthBox);

        var heightBox = CreateNumberBox(80, 300, 140);
        heightBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("mediaHeight", heightBox.Value);
        advancedChildren.Add(CreateSettingRow("高度", heightBox));
        allControls.Add(heightBox);

        var mediaTextSizeBox = CreateNumberBox(12, 32, 16);
        mediaTextSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("mediaTextSize", mediaTextSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("文字大小", mediaTextSizeBox));
        allControls.Add(mediaTextSizeBox);

        var coverSizeBox = CreateNumberBox(32, 128, 64);
        coverSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("mediaCoverSize", coverSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("封面大小", coverSizeBox));
        allControls.Add(coverSizeBox);

        // ── 高级: 背景 ──
        var bgSectionLabel = new TextBlock
        {
            Text = "— 背景样式 —",
            FontSize = 13,
            Opacity = 0.6,
            Margin = new Thickness(0, 8, 0, 0)
        };
        advancedChildren.Add(bgSectionLabel);

        advancedChildren.Add(CreateColorPickerRow("背景颜色", "#000000", api, "mediaBgColor"));

        var bgOpacityBox = CreateNumberBox(0, 100, 60);
        bgOpacityBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("mediaBgOpacity", bgOpacityBox.Value);
        advancedChildren.Add(CreateSettingRow("背景透明度", bgOpacityBox));
        allControls.Add(bgOpacityBox);

        var borderRadiusBox = CreateNumberBox(0, 30, 12);
        borderRadiusBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("mediaBorderRadius", borderRadiusBox.Value);
        advancedChildren.Add(CreateSettingRow("圆角半径", borderRadiusBox));
        allControls.Add(borderRadiusBox);

        var borderWidthBox = CreateNumberBox(0, 5, 1);
        borderWidthBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("mediaBorderWidth", borderWidthBox.Value);
        advancedChildren.Add(CreateSettingRow("边框粗细", borderWidthBox));
        allControls.Add(borderWidthBox);

        advancedChildren.Add(CreateColorPickerRow("边框颜色", "#333333", api, "mediaBorderColor"));

        // ── 高级: 文字颜色 ──
        var textColorSectionLabel = new TextBlock
        {
            Text = "— 文字颜色 —",
            FontSize = 13,
            Opacity = 0.6,
            Margin = new Thickness(0, 8, 0, 0)
        };
        advancedChildren.Add(textColorSectionLabel);

        advancedChildren.Add(CreateColorPickerRow("标题颜色", "#FFFFFF", api, "mediaTitleColor"));
        advancedChildren.Add(CreateColorPickerRow("歌手颜色", "#CCCCCC", api, "mediaArtistColor"));
        advancedChildren.Add(CreateColorPickerRow("时间颜色", "#999999", api, "mediaTimeColor"));

        // ── 高级: 歌词 ──
        var lyricsSectionLabel = new TextBlock
        {
            Text = "— 歌词设置 —",
            FontSize = 13,
            Opacity = 0.6,
            Margin = new Thickness(0, 8, 0, 0)
        };
        advancedChildren.Add(lyricsSectionLabel);

        advancedChildren.Add(CreateColorPickerRow("歌词颜色", "#AAAAAA", api, "mediaLyricsColor"));

        var lyricsSizeBox = CreateNumberBox(10, 24, 14);
        lyricsSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("mediaLyricsSize", lyricsSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("歌词大小", lyricsSizeBox));
        allControls.Add(lyricsSizeBox);

        var lyricsAdvanceBox = CreateNumberBox(0, 2000, 500);
        lyricsAdvanceBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("mediaLyricsAdvance", lyricsAdvanceBox.Value);
        advancedChildren.Add(CreateSettingRow("提前量(ms)", lyricsAdvanceBox));
        allControls.Add(lyricsAdvanceBox);

        // ── 高级: 进度条 ──
        var progressSectionLabel = new TextBlock
        {
            Text = "— 进度条 —",
            FontSize = 13,
            Opacity = 0.6,
            Margin = new Thickness(0, 8, 0, 0)
        };
        advancedChildren.Add(progressSectionLabel);

        advancedChildren.Add(CreateColorPickerRow("进度颜色", "#30C361", api, "mediaProgressColor"));
        advancedChildren.Add(CreateColorPickerRow("轨道颜色", "#444444", api, "mediaTrackColor"));

        var progressHeightBox = CreateNumberBox(2, 8, 4);
        progressHeightBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("mediaProgressHeight", progressHeightBox.Value);
        advancedChildren.Add(CreateSettingRow("进度条高度", progressHeightBox));
        allControls.Add(progressHeightBox);

        // ── 高级: 封面边框 ──
        var coverBorderSectionLabel = new TextBlock
        {
            Text = "— 封面边框 —",
            FontSize = 13,
            Opacity = 0.6,
            Margin = new Thickness(0, 8, 0, 0)
        };
        advancedChildren.Add(coverBorderSectionLabel);

        var coverBorderRadiusBox = CreateNumberBox(0, 20, 8);
        coverBorderRadiusBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("mediaCoverBorderRadius", coverBorderRadiusBox.Value);
        advancedChildren.Add(CreateSettingRow("封面圆角", coverBorderRadiusBox));
        allControls.Add(coverBorderRadiusBox);

        advancedChildren.Add(CreateColorPickerRow("封面边框色", "#666666", api, "mediaCoverBorderColor"));

        root.Children.Add(CreateExpanderGroup("基本设置", true, basicChildren.ToArray()));
        root.Children.Add(CreateExpanderGroup("高级设置", false, advancedChildren.ToArray()));
    }

    //  快捷启动 (quick_launch)
    private static void BuildQuickLaunchSettings(StackPanel root, IApiService api, List<Control> allControls)
    {
        var basicChildren = new List<UIElement>();
        var advancedChildren = new List<UIElement>();

        // ── 基本 ──
        var enableQL = CreateToggleSwitch(true, api, "showQuickLaunch", allControls);
        basicChildren.Add(CreateSettingRow("启用快捷启动", enableQL));
        allControls.Add(enableQL);

        var editAppsBtn = new Button
        {
            Content = "编辑应用列表",
            Padding = new Thickness(20, 6, 20, 6)
        };
        editAppsBtn.Click += (_, _) =>
        {
            System.Diagnostics.Debug.WriteLine("[ComponentSettingDialog] 编辑应用列表功能待实现");
        };
        basicChildren.Add(CreateSettingRow("应用管理", editAppsBtn));

        // ── 高级 ──
        var iconSizeBox = CreateNumberBox(32, 96, 48);
        iconSizeBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("quickLaunchIconSize", iconSizeBox.Value);
        advancedChildren.Add(CreateSettingRow("图标大小", iconSizeBox));
        allControls.Add(iconSizeBox);

        var iconSpacingBox = CreateNumberBox(4, 40, 12);
        iconSpacingBox.ValueChanged += (_, _) => _ = api.SetConfigAsync("quickLaunchIconSpacing", iconSpacingBox.Value);
        advancedChildren.Add(CreateSettingRow("图标间距", iconSpacingBox));
        allControls.Add(iconSpacingBox);

        var showLabelsToggle = CreateToggleSwitch(true, api, "quickLaunchShowLabels");
        advancedChildren.Add(CreateSettingRow("显示名称", showLabelsToggle));
        allControls.Add(showLabelsToggle);

        root.Children.Add(CreateExpanderGroup("基本设置", true, basicChildren.ToArray()));
        root.Children.Add(CreateExpanderGroup("高级设置", false, advancedChildren.ToArray()));
    }
}
