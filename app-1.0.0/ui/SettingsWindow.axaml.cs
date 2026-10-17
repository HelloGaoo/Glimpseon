// Glimpseon
// Copyright (C) 2026 HelloGaoo
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// 设置窗口
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Glimpseon.Core;
using Glimpseon.Core.Services;
using Markdown.Avalonia;

namespace Glimpseon.UI;

public partial class SettingsWindow : GlimpseonWindow
{
    private readonly Dictionary<string, Control> _pages = new();
    private FAFrame? _navFrame;
    private string _currentTag = "general";

    private static string Tr(string key) => AppUtils.Tr(key);

    /// <summary>页签顺序与文案</summary>
    private static readonly (string Tag, string Key)[] NavItems =
    {
        ("general", "settings.general"),
        ("time", "settings.time"),
        ("appearance", "settings.appearance"),
        ("weather", "settings.weather"),
        ("grid", "settings.grid.title"),
        ("log", "settings.log"),
        ("reminder", "settings.reminder"),
        ("storage", "settings.storage.title"),
        ("privacy", "settings.privacy"),
        ("advanced", "settings.advanced"),
        ("plugins", "settings.plugins.title"),
    };

    public SettingsWindow()
    {
        InitializeComponent();
        Title = Tr("settings.title");
        TitleLabel.Text = Tr("settings.title");

        try
        {
            var iconPath = Paths.GetResourcePath(Constants.AppIcon);
            if (File.Exists(iconPath))
            {
                WindowIcon.Source = new Bitmap(iconPath);
            }
        }
        catch (Exception e)
        {

            Log.Debug($"[设置] 图标缺失留空: {e.Message}");
            // 图标缺失 留空
        }
        VersionText.Text = Paths.Version;

        // 标题栏待重启徽标
        Common.InitRestartBadge(RestartBadge, this);

        SetNavLabels();
        BuildPages();
        _navFrame = new FAFrame { IsNavigationStackEnabled = false, NavigationPageFactory = PassThroughNavigationPageFactory.Instance };
        Nav.Content = _navFrame;
        _navFrame.Loaded += (_, _) => Nav.SelectedItem = NavGeneral;
    }

    // 导航

    private void SetNavLabels()
    {
        NavGeneral.Content = Tr("settings.general");
        NavTime.Content = Tr("settings.time");
        NavAppearance.Content = Tr("settings.appearance");
        NavWeather.Content = Tr("settings.weather");
        NavGrid.Content = Tr("settings.grid.title");
        NavLog.Content = Tr("settings.log");
        NavReminder.Content = Tr("settings.reminder");
        NavStorage.Content = Tr("settings.storage.title");
        NavPrivacy.Content = Tr("settings.privacy");
        NavAdvanced.Content = Tr("settings.advanced");
        NavPlugins.Content = Tr("settings.plugins.title");
        var pluginsIcon = FAIcons.IconSource(FASymbol.Shop);
        pluginsIcon.FontSize = 20;
        NavPlugins.IconSource = pluginsIcon;
    }

    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is FANavigationViewItem item && item.Tag is string tag && _navFrame is { IsLoaded: true } && _pages.TryGetValue(tag, out var page))
        {
            _currentTag = tag;
            // FUI 切一次页面有两遍动画 断第二遍的
            if (ReferenceEquals(_navFrame.Content, page))
            {
                return;
            }
            _navFrame.NavigateFromObject(page, new FAFrameNavigationOptions());
        }
    }

    // 卡片构建器

    private static Control SectionHeader(string text, FASymbol symbol)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 4),
            Children =
            {
                FAIcons.IconCenter(symbol, 16),
                new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }

    private static FASettingsExpander Card(FASymbol icon, string header, string? description, Control? footer)
    {
        var card = new FASettingsExpander
        {
            IconSource = FAIcons.IconSource(icon),
            Header = header,
        };
        if (!string.IsNullOrEmpty(description))
        {
            card.Description = description;
        }
        if (footer is not null)
        {
            card.Footer = footer;
        }
        return card;
    }

    /// <summary>开关卡片</summary>
    private static FASettingsExpander CardSwitch(FASymbol icon, string header, string description, ConfigItem<bool> item)
    {
        var toggle = new ToggleSwitch { IsChecked = item.Value, OnContent = null, OffContent = null };
        toggle.IsCheckedChanged += (_, _) => item.Value = toggle.IsChecked == true;
        return Card(icon, header, description, toggle);
    }

    /// <summary>数值卡片</summary>
    private static FASettingsExpander CardSpin(FASymbol icon, string header, string description, ConfigItem<int> item, double min, double max)
    {
        var num = new NumericUpDown
        {
            Value = item.Value,
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = 1,
            Width = 150,
            FormatString = "0",
        };
        num.ValueChanged += (_, e) => item.Value = (int)(e.NewValue ?? 0);
        return Card(icon, header, description, num);
    }

    /// <summary>文本行卡片</summary>
    private static FASettingsExpander CardText(FASymbol icon, string header, string description, ConfigItem<string> item)
    {
        var box = new TextBox { Text = item.Value, Width = 260 };
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                item.Value = box.Text ?? "";
            }
        };
        item.ValueChanged += v => Dispatcher.UIThread.Post(() =>
        {
            if (box.Text != v)
            {
                box.Text = v;
            }
        });
        return Card(icon, header, description, box);
    }

    /// <summary>下拉卡片</summary>
    private static FASettingsExpander CardCombo<T>(FASymbol icon, string header, string description, ConfigItem<T> item, (string Text, T Value)[] options)
    {
        var combo = new ComboBox { MinWidth = 180 };
        combo.ItemsSource = options.Select(o => o.Text).ToList();
        var idx = Array.FindIndex(options, o => EqualityComparer<T>.Default.Equals(o.Value, item.Value));
        combo.SelectedIndex = idx < 0 ? 0 : idx;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < options.Length)
            {
                item.Value = options[combo.SelectedIndex].Value;
            }
        };
        item.ValueChanged += v => Dispatcher.UIThread.Post(() =>
        {
            var i = Array.FindIndex(options, o => EqualityComparer<T>.Default.Equals(o.Value, v));
            if (i >= 0 && combo.SelectedIndex != i)
            {
                combo.SelectedIndex = i;
            }
        });
        return Card(icon, header, description, combo);
    }

    /// <summary>按钮卡片</summary>
    private static FASettingsExpander CardButton(FASymbol icon, string header, string description, string buttonText, Action onClick)
    {
        var btn = new Button { Content = buttonText, MinWidth = 110 };
        btn.Click += (_, _) => onClick();
        return Card(icon, header, description, btn);
    }

    /// <summary>颜色卡片</summary>
    private static FASettingsExpander CardColor(FASymbol icon, string header, string description, ConfigItem<string> item)
    {
        // 直接用控件默认形态: 页签 光谱/调色板/分量 + 预览 + Hex 全都有, 无需逐个开关
        var picker = new ColorPicker
        {
            Color = ParseColor(item.Value),
            IsAlphaEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        picker.ColorChanged += (_, e) => item.Value = ToHex(e.NewColor);
        item.ValueChanged += v => Dispatcher.UIThread.Post(() =>
        {
            var c = ParseColor(v);
            if (picker.Color != c)
            {
                picker.Color = c;
            }
        });
        return Card(icon, header, description, picker);
    }

    /// <summary>Color → 配置用十六进制串(不透明输出 #RRGGBB, 否则 #AARRGGBB)</summary>
    private static string ToHex(Color c) => c.A == 255
        ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
        : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static StackPanel MakePageStack(string pageTitle)
    {
        return new StackPanel
        {
            Margin = new Thickness(28, 4, 28, 24),
            MaxWidth = 1000,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                new TextBlock { Text = pageTitle, FontSize = 26, FontWeight = FontWeight.Normal, Margin = new Thickness(0, 0, 0, 12) },
            },
        };
    }

    private static ScrollViewer Wrap(Control content) => new()
    {
        Content = content,
    };

    /// <summary>页内提示条</summary>
    private static void Info(Panel host, FAInfoBarSeverity severity, string message)
    {
        var bar = new FAInfoBar
        {
            IsOpen = true,
            IsClosable = true,
            Severity = severity,
            Message = message,
            Margin = new Thickness(0, 0, 0, 8),
        };
        host.Children.Insert(0, bar);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            bar.IsOpen = false;
            host.Children.Remove(bar);
        };
        timer.Start();
    }

    private static Color ParseColor(string? text) => TryParseColor(text, out var c) ? c : Colors.Gray;

    private static bool TryParseColor(string? text, out Color color)
    {
        if (!string.IsNullOrWhiteSpace(text) && Color.TryParse(text.Trim(), out color))
        {
            return true;
        }
        color = Colors.Gray;
        return false;
    }

    // 各子页

    private void BuildPages()
    {
        _pages.Clear();
        _pages["general"] = Wrap(BuildGeneralPage());
        _pages["time"] = Wrap(BuildTimePage());
        _pages["appearance"] = Wrap(BuildAppearancePage());
        _pages["weather"] = Wrap(BuildWeatherPage());
        _pages["grid"] = Wrap(BuildGridPage());
        _pages["log"] = Wrap(BuildLogPage());
        _pages["reminder"] = Wrap(BuildReminderPage());
        _pages["storage"] = Wrap(BuildStoragePage());
        _pages["privacy"] = Wrap(BuildPrivacyPage());
        _pages["advanced"] = Wrap(BuildAdvancedPage());
        _pages["plugins"] = Wrap(BuildPluginsPage());
    }

    /// <summary>重建全部页面(重置/导入配置后) 并切回当前页</summary>
    private void RebuildPages()
    {
        BuildPages();
        if (_navFrame is not null && _pages.TryGetValue(_currentTag, out var page))
        {
            _navFrame.NavigateFromObject(page, new FAFrameNavigationOptions());
        }
    }

    /// <summary>
    /// 重置/导入配置后把新配置下发到全部界面:
    /// 重建设置页 + 广播所有配置项(主窗口主题/强调色/空闲/自启、首页背景/网格/卡片样式、
    /// 组件样式等各自订阅了 ValueChanged, 广播后即整体重刷).
    /// 期间抑制"需重启"弹窗, 避免连续弹窗.
    /// </summary>
    private void ApplyConfigToAllUi()
    {
        RebuildPages();
        var suppressed = AppUtils.RestartPromptSuppressed;
        AppUtils.RestartPromptSuppressed = true;
        try
        {
            Config.BroadcastAll();
        }
        finally
        {
            AppUtils.RestartPromptSuppressed = suppressed;
        }
        // 强调色/主题不是纯 ValueChanged 驱动, 这里再确保一次
        Common.ApplyAccentColor(Common.ParseAccentColor(Config.ThemeColor.Value));
        Log.Info("[设置] 配置已下发到全部界面");
    }

    // 通用

    private Control BuildGeneralPage()
    {
        var stack = MakePageStack(Tr("settings.general"));
        stack.Children.Add(CardSwitch(FASymbol.Play, Tr("wizard.auto_start"), Tr("wizard.auto_start_desc"), Config.AutoStart));
        stack.Children.Add(CardSwitch(FASymbol.View, Tr("wizard.auto_open_idle"), Tr("wizard.auto_open_idle_desc"), Config.AutoOpenOnIdle));
        stack.Children.Add(CardSpin(FASymbol.ClockFilled, Tr("settings.idle_minutes"), Tr("settings.idle_minutes_desc"), Config.IdleMinutes, 1, 60));
        stack.Children.Add(CardSwitch(FASymbol.FullScreenMaximize, Tr("wizard.auto_open_maximize"), Tr("wizard.auto_open_maximize_desc"), Config.AutoOpenMaximize));
        return stack;
    }

    // 时间

    private Control BuildTimePage()
    {
        var stack = MakePageStack(Tr("settings.time"));
        stack.Children.Add(CardSwitch(FASymbol.Calendar, Tr("settings.use_precise_time"), Tr("settings.use_precise_time_desc"), Config.UsePreciseTime));
        stack.Children.Add(CardText(FASymbol.Earth, Tr("settings.time_server"), Tr("settings.time_server_desc"), Config.TimeServer));

        // 同步状态 + 手动同步(
        var statusText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
        void UpdateStatus()
        {
            statusText.Text = string.IsNullOrEmpty(Config.LastSyncTime.Value)
                ? Tr("settings.precise_time_not_synced")
                : AppUtils.Tr("settings.precise_time_synced_at", ("time", Config.LastSyncTime.Value));
        }
        UpdateStatus();
        Config.LastSyncTime.ValueChanged += _ => Dispatcher.UIThread.Post(UpdateStatus);

        var syncBtn = new Button { Content = Tr("settings.precise_time_sync_now"), MinWidth = 110 };
        syncBtn.Click += (_, _) =>
        {
            syncBtn.IsEnabled = false;
            syncBtn.Content = Tr("settings.precise_time_syncing");
            var server = Config.TimeServer.Value;
            Task.Run(() =>
            {
                var ok = AppUtils.SyncNtp(server);
                Dispatcher.UIThread.Post(() =>
                {
                    syncBtn.IsEnabled = true;
                    syncBtn.Content = Tr("settings.precise_time_sync_now");
                    if (ok && AppUtils.LastSyncTime is { } t)
                    {
                        Config.LastSyncTime.Value = t.ToString("HH:mm:ss");
                        Info(stack, FAInfoBarSeverity.Success,
                            AppUtils.Tr("settings.precise_time_sync_success", ("time", t.ToString("HH:mm:ss"))));
                    }
                    else
                    {
                        Info(stack, FAInfoBarSeverity.Error, Tr("settings.precise_time_sync_failed"));
                    }
                    UpdateStatus();
                });
            });
        };
        var syncRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { statusText, syncBtn },
        };
        stack.Children.Add(Card(FASymbol.Sync, Tr("settings.time_sync_status"), Tr("settings.time_sync_status_desc"), syncRow));

        stack.Children.Add(CardSpin(FASymbol.Zoom, Tr("settings.time_offset"), Tr("settings.time_offset_desc"), Config.TimeOffset, -9999, 9999));

        // 自动偏移: 开关 + 增量(
        var autoToggle = new ToggleSwitch { IsChecked = Config.AutoTimeOffsetEnabled.Value, OnContent = null, OffContent = null };
        autoToggle.IsCheckedChanged += (_, _) => Config.AutoTimeOffsetEnabled.Value = autoToggle.IsChecked == true;
        var inc = new NumericUpDown
        {
            Value = Config.AutoTimeOffsetIncrement.Value,
            Minimum = -9999,
            Maximum = 9999,
            Increment = 1,
            Width = 130,
            FormatString = "0",
        };
        inc.ValueChanged += (_, e) => Config.AutoTimeOffsetIncrement.Value = (int)(e.NewValue ?? 0);
        var autoRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { autoToggle, inc },
        };
        stack.Children.Add(Card(FASymbol.Add, Tr("settings.auto_time_offset"), Tr("settings.auto_time_offset_desc"), autoRow));
        return stack;
    }

    // 外观

    private Control BuildAppearancePage()
    {
        var stack = MakePageStack(Tr("settings.appearance"));
        stack.Children.Add(CardCombo(FASymbol.WeatherMoon, Tr("wizard.theme_mode"), Tr("wizard.theme_mode_desc"), Config.ThemeMode,
            new (string, ThemeMode)[]
            {
                (Tr("wizard.theme_light"), ThemeMode.Light),
                (Tr("wizard.theme_dark"), ThemeMode.Dark),
                (Tr("wizard.theme_system"), ThemeMode.Auto),
            }));
        stack.Children.Add(CardColor(FASymbol.ColorFill, Tr("wizard.primary_color"), Tr("wizard.primary_color_desc"), Config.ThemeColor));
        stack.Children.Add(CardCombo(FASymbol.World, Tr("settings.language"), Tr("settings.language_desc"), Config.Language,
            new (string, LanguageOption)[]
            {
                (Tr("settings.lang_zh_cn"), LanguageOption.ChineseSimplified),
                (Tr("settings.lang_zh_tw"), LanguageOption.ChineseTraditional),
                ("English", LanguageOption.English),
                ("Auto", LanguageOption.Auto),
            }));
        return stack;
    }

    // 天气

    private Control BuildWeatherPage()
    {
        var stack = MakePageStack(Tr("settings.weather"));

        // 顶部: 城市 + 刷新
        var cityLabel = new TextBlock
        {
            Text = Tr("settings.weather_city_unset"),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var refreshBtn = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = FAIcons.Icon(FASymbol.Redo, 16),
        };
        ToolTip.SetTip(refreshBtn, Tr("settings.weather_refresh"));
        var headerRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto") };
        Grid.SetColumn(cityLabel, 0);
        Grid.SetColumn(refreshBtn, 2);
        headerRow.Children.Add(cityLabel);
        headerRow.Children.Add(refreshBtn);
        stack.Children.Add(headerRow);

        var updateLabel = new TextBlock { Text = Tr("settings.weather_never_updated"), FontSize = 12, Opacity = 0.7 };
        stack.Children.Add(updateLabel);
        stack.Children.Add(new Border { Height = 6 });

        // 当前天气: 图标 + 温度
        var iconHost = new Border { Width = 72, Height = 72 };
        var tempLabel = new TextBlock
        {
            Text = "--°",
            FontSize = 40,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var currentRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children = { iconHost, tempLabel },
        };
        stack.Children.Add(currentRow);
        stack.Children.Add(new Border { Height = 6 });

        // 四项指标
        var (windCard, windValue) = MakeMetricCard(Tr("settings.weather_wind"));
        var (aqiCard, aqiValue) = MakeMetricCard(Tr("settings.weather_aqi"));
        var (humidityCard, humidityValue) = MakeMetricCard(Tr("settings.weather_humidity"));
        var (feelsCard, feelsValue) = MakeMetricCard(Tr("settings.weather_feels"));
        var metricRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,*,*,*"), ColumnSpacing = 10 };
        var metrics = new[] { windCard, aqiCard, humidityCard, feelsCard };
        for (var i = 0; i < metrics.Length; i++)
        {
            Grid.SetColumn(metrics[i], i);
            metricRow.Children.Add(metrics[i]);
        }
        stack.Children.Add(metricRow);
        stack.Children.Add(new Border { Height = 12 });

        // 数据来源
        var cityCard = new FASettingsExpander
        {
            IconSource = FAIcons.IconSource(FASymbol.MapPin),
            Header = Tr("settings.weather_city"),
            Description = Tr("settings.weather_city_desc"),
        };
        var pickCityBtn = new Button { Content = Tr("settings.weather_select_city"), MinWidth = 110 };
        cityCard.Footer = pickCityBtn;

        // 经纬度卡片(
        var latBox = new TextBox { Text = Config.Latitude.Value.ToString(CultureInfo.InvariantCulture), Width = 110, Watermark = Tr("settings.weather_latitude") };
        var lonBox = new TextBox { Text = Config.Longitude.Value.ToString(CultureInfo.InvariantCulture), Width = 110, Watermark = Tr("settings.weather_longitude") };
        foreach (var (box, item) in new (TextBox, ConfigItem<double>)[] { (latBox, Config.Latitude), (lonBox, Config.Longitude) })
        {
            box.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty && double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    item.Value = v;
                }
            };
            var captured = box;
            item.ValueChanged += v => Dispatcher.UIThread.Post(() =>
            {
                var text = v.ToString(CultureInfo.InvariantCulture);
                if (captured.Text != text)
                {
                    captured.Text = text;
                }
            });
        }
        var latLonCard = Card(FASymbol.Globe, Tr("settings.weather_latlon"), Tr("settings.weather_latlon_desc"), new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { latBox, lonBox },
        });

        var sourceCard = CardCombo(FASymbol.Map, Tr("settings.weather_source"), Tr("settings.weather_source_desc"), Config.WeatherSource,
            new (string, string)[]
            {
                (Tr("settings.weather_source_city"), "city"),
                (Tr("settings.weather_source_coords"), "coords"),
            });

        stack.Children.Add(sourceCard);
        stack.Children.Add(cityCard);
        stack.Children.Add(latLonCard);
        stack.Children.Add(CardText(FASymbol.Filter, Tr("settings.weather_alert_exclude"), Tr("settings.weather_alert_exclude_desc"), Config.WeatherAlertExcluded));

        // 设定: 刷新间隔 + 单位
        stack.Children.Add(CardCombo(FASymbol.RotateClockwise, Tr("settings.weather_refresh_interval"), Tr("settings.weather_refresh_interval_desc"), Config.WeatherUpdateInterval,
            new (string, string)[]
            {
                (Tr("settings.weather_interval_never"), "never"),
                (Tr("settings.weather_interval_5m"), "5m"),
                (Tr("settings.weather_interval_15m"), "15m"),
                (Tr("settings.weather_interval_30m"), "30m"),
                (Tr("settings.weather_interval_1h"), "1h"),
                (Tr("settings.weather_interval_3h"), "3h"),
                (Tr("settings.weather_interval_6h"), "6h"),
                (Tr("settings.weather_interval_12h"), "12h"),
                (Tr("settings.weather_interval_24h"), "24h"),
            }));
        stack.Children.Add(CardCombo(FASymbol.WeatherSunny, Tr("settings.weather_unit"), Tr("settings.weather_unit_desc"), Config.WeatherUnit,
            new (string, string)[]
            {
                (Tr("settings.weather_unit_c"), "c"),
                (Tr("settings.weather_unit_f"), "f"),
            }));

        // 可见性: 城市 / 经纬度
        void ApplySourceVisibility()
        {
            var byCity = Config.WeatherSource.Value == "city";
            cityCard.IsVisible = byCity;
            latLonCard.IsVisible = !byCity;
            UpdateCityLabel();
        }

        void UpdateCityLabel()
        {
            if (Config.WeatherSource.Value == "city")
            {
                cityLabel.Text = string.IsNullOrEmpty(Config.City.Value) ? Tr("settings.weather_city_unset") : Config.City.Value;
            }
            else
            {
                cityLabel.Text = string.Format(CultureInfo.InvariantCulture, "{0:F4}, {1:F4}", Config.Latitude.Value, Config.Longitude.Value);
            }
        }

        void ApplyData(JsonElement data)
        {
            var current = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.Object
                ? c
                : default;

            // 更新时间 MM/DD HH:MM
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty("pubTime", out var pub))
            {
                var raw = pub.GetString() ?? "";
                if (raw.Length >= 16 &&
                    DateTime.TryParse(raw[..16].Replace('T', ' '), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    updateLabel.Text = $"{dt:MM/dd HH:mm} {Tr("settings.weather_updated")}";
                }
            }

            // 当前温度
            var temp = "--";
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty("temperature", out var t) && t.TryGetProperty("value", out var tv))
            {
                temp = FmtTemp(tv);
            }
            tempLabel.Text = temp + "°";

            // 图标
            var code = 0;
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty("weather", out var w))
            {
                _ = int.TryParse(w.ToString(), out code);
            }
            iconHost.Child = MakeWeatherIcon(WeatherMaps.GetIcon(code), 72);

            // 四指标
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty("wind", out var wind) &&
                wind.TryGetProperty("speed", out var speed))
            {
                windValue.Text = FmtMetric(speed);
            }
            var aqi = "--";
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("aqi", out var aqiObj) && aqiObj.TryGetProperty("aqi", out var aqiVal))
            {
                var s = aqiVal.ToString();
                aqi = string.IsNullOrEmpty(s) ? "--" : s;
            }
            aqiValue.Text = aqi;
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty("humidity", out var hum))
            {
                humidityValue.Text = FmtMetric(hum);
            }
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty("feelsLike", out var feels))
            {
                var unit = Config.WeatherUnit.Value == "f"
                    ? "°F"
                    : (feels.TryGetProperty("unit", out var u) ? u.GetString() : null) ?? "℃";
                feelsValue.Text = FmtTemp(feels.TryGetProperty("value", out var fv) ? fv : default) + unit;
            }

            if (Config.WeatherSource.Value == "city" && !string.IsNullOrEmpty(Config.City.Value))
            {
                cityLabel.Text = Config.City.Value;
            }
        }

        void LoadCached()
        {
            var data = AppUtils.GetCachedContent("weather", ignoreExpiry: true);
            if (data is { } d)
            {
                try
                {
                    ApplyData(d);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[设置/天气] 缓存应用失败: {ex.Message}");
                    UpdateCityLabel();
                }
            }
            else
            {
                UpdateCityLabel();
            }
        }

        async void Refresh()
        {
            var data = await WeatherService.FetchAllAsync();
            if (data is { ValueKind: JsonValueKind.Object } d)
            {
                AppUtils.SaveCache("weather", d, Config.WeatherUpdateInterval.Value);
                ApplyData(d);
            }
            else
            {
                Info(stack, FAInfoBarSeverity.Warning, Tr("settings.weather_fetch_failed"));
            }
        }

        refreshBtn.Click += (_, _) => Refresh();
        pickCityBtn.Click += async (_, _) =>
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            var region = await RegionSelectorDialog.ShowAsync(owner!, Config.City.Value);
            if (string.IsNullOrEmpty(region))
            {
                return;
            }
            Config.City.Value = region;
            Config.WeatherSource.Value = "city";
            var (lon, lat) = new RegionDatabase().GetCoordinates(region);
            if (lon is not null && lat is not null)
            {
                Config.Longitude.Value = lon.Value;
                Config.Latitude.Value = lat.Value;
            }
            else
            {
                Log.Warning($"[设置/天气] 未查询到城市坐标: {region}");
            }
            UpdateCityLabel();
            Refresh();
        };
        Config.WeatherSource.ValueChanged += _ => Dispatcher.UIThread.Post(ApplySourceVisibility);
        Config.City.ValueChanged += _ => Dispatcher.UIThread.Post(UpdateCityLabel);
        Config.WeatherUnit.ValueChanged += _ => Dispatcher.UIThread.Post(LoadCached);

        ApplySourceVisibility();
        LoadCached();
        return stack;
    }

    private static (Border Card, TextBlock Value) MakeMetricCard(string title)
    {
        var titleLabel = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var dash = new Border
        {
            Width = 24,
            Height = 2,
            CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2),
            Background = new SolidColorBrush(Color.FromArgb(140, 128, 128, 128)),
        };
        var value = new TextBlock
        {
            Text = "--",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var card = new Border
        {
            Height = 108,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
            Padding = new Thickness(14, 12, 14, 12),
            Child = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { titleLabel, dash, value },
            },
        };
        return (card, value);
    }

    private static string FmtTemp(JsonElement raw)
    {
        var text = raw.ValueKind == JsonValueKind.Undefined ? "" : (raw.ValueKind == JsonValueKind.String ? raw.GetString() : raw.ToString());
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            return string.IsNullOrEmpty(text) ? "--" : text;
        }
        if (Config.WeatherUnit.Value == "f")
        {
            v = v * 9.0 / 5.0 + 32.0;
        }
        return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
    }

    private static string FmtMetric(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return "--";
        }
        var v = obj.TryGetProperty("value", out var vv) ? vv.ToString() : "--";
        var u = obj.TryGetProperty("unit", out var uu) ? uu.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(v))
        {
            v = "--";
        }
        return $"{v} {u}".Trim();
    }

    private static Control? MakeWeatherIcon(string iconName, double size)
    {
        try
        {
            var path = WeatherMaps.GetWeatherIconPath(iconName);
            return File.Exists(path)
                ? new Avalonia.Svg.Skia.Svg(new Uri(path))
                {
                    Width = size,
                    Height = size,
                    SvgSource = Avalonia.Svg.Skia.SvgSource.Load(path, null),
                }
                : null;
        }
        catch (Exception e)
        {
            Log.Debug($"[设置/天气] 图标渲染失败 {iconName}: {e.Message}");
            return null;
        }
    }

    // 网格

    private Control BuildGridPage()
    {
        var stack = MakePageStack(Tr("settings.grid.title"));

        // 两个预览(
        var gridPreview = new GridPreviewControl
        {
            ShortSideCells = Config.GridShortSideCells.Value,
            InsetPercent = Config.GridInsetPercent.Value,
        };
        var radiusPreview = new CornerRadiusPreviewControl
        {
            CardRadius = Config.ComponentCardRadius.Value,
            CardOpacity = Config.ComponentCardOpacity.Value,
        };
        var previewRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,*,Auto,*") };
        var gridItem = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                gridPreview,
                new TextBlock { Text = Tr("settings.grid.preview"), FontSize = 12, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center },
            },
        };
        var cardItem = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                radiusPreview,
                new TextBlock { Text = Tr("settings.grid.cornerRadius_preview"), FontSize = 12, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center },
            },
        };
        Grid.SetColumn(gridItem, 1);
        Grid.SetColumn(cardItem, 3);
        previewRow.Children.Add(gridItem);
        previewRow.Children.Add(cardItem);
        stack.Children.Add(new Border { Padding = new Thickness(0, 10, 0, 10), Child = previewRow });

        Config.GridShortSideCells.ValueChanged += v => Dispatcher.UIThread.Post(() => gridPreview.ShortSideCells = v);
        Config.GridInsetPercent.ValueChanged += v => Dispatcher.UIThread.Post(() => gridPreview.InsetPercent = v);
        Config.ComponentCardRadius.ValueChanged += v => Dispatcher.UIThread.Post(() => radiusPreview.CardRadius = v);
        Config.ComponentCardOpacity.ValueChanged += v => Dispatcher.UIThread.Post(() => radiusPreview.CardOpacity = v);

        stack.Children.Add(CardSpin(FASymbol.ViewAll, Tr("settings.grid.short_side_cells"), Tr("settings.grid.short_side_cells_desc"), Config.GridShortSideCells, 6, 96));
        stack.Children.Add(CardSpin(FASymbol.AllApps, Tr("settings.grid.inset_percent"), Tr("settings.grid.inset_percent_desc"), Config.GridInsetPercent, 0, 30));
        stack.Children.Add(CardSpin(FASymbol.ColorLine, Tr("settings.grid.component_card_opacity"), Tr("settings.grid.component_card_opacity_desc"), Config.ComponentCardOpacity, 0, 100));
        stack.Children.Add(CardSpin(FASymbol.Crop, Tr("settings.grid.component_card_radius"), Tr("settings.grid.component_card_radius_desc"), Config.ComponentCardRadius, 0, 29));
        return stack;
    }

    // 日志

    private Control BuildLogPage()
    {
        var stack = MakePageStack(Tr("settings.log"));
        var disableCard = CardSwitch(FASymbol.Dismiss, Tr("settings.disable_log"), Tr("settings.disable_log_desc"), Config.DisableLog);
        var levelCard = CardCombo(FASymbol.Code, Tr("settings.log_level"), Tr("settings.log_level_desc"), Config.LogVerbosity,
            new (string, LogVerbosity)[]
            {
                ("Debug", LogVerbosity.Debug),
                ("Info", LogVerbosity.Info),
                ("Warning", LogVerbosity.Warning),
                ("Error", LogVerbosity.Error),
            });
        var maxCountCard = CardSpin(FASymbol.List, Tr("settings.log_max_count"), Tr("settings.log_max_count_desc"), Config.LogMaxCount, 10, 500);
        var maxDaysCard = CardSpin(FASymbol.CalendarWeek, Tr("settings.log_max_days"), Tr("settings.log_max_days_desc"), Config.LogMaxDays, 30, 365);

        void ApplyEnabled()
        {
            var enabled = !Config.DisableLog.Value;
            levelCard.IsEnabled = enabled;
            maxCountCard.IsEnabled = enabled;
            maxDaysCard.IsEnabled = enabled;
        }
        ApplyEnabled();
        Config.DisableLog.ValueChanged += _ => Dispatcher.UIThread.Post(ApplyEnabled);

        stack.Children.Add(disableCard);
        stack.Children.Add(levelCard);
        stack.Children.Add(maxCountCard);
        stack.Children.Add(maxDaysCard);
        stack.Children.Add(CardButton(FASymbol.Delete, Tr("settings.clear_log"), Tr("settings.clear_log_desc"), Tr("settings.clear_log_button"),
            () => _ = ClearLogsAsync(stack)));
        return stack;
    }

    /// <summary>清空日志</summary>
    private async Task ClearLogsAsync(Panel host)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            return;
        }
        if (!await Common.Confirm(owner, Tr("settings.clear_log"), Tr("settings.clear_log_confirm")))
        {
            Log.Debug("[设置/日志] 清理日志已取消");
            return;
        }
        try
        {
            if (!Directory.Exists(Paths.DataLog))
            {
                Info(host, FAInfoBarSeverity.Informational, Tr("settings.log_dir_not_exist"));
                return;
            }
            var files = Directory.GetFiles(Paths.DataLog, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
            var current = files.FirstOrDefault();
            var deleted = 0;
            foreach (var file in files)
            {
                if (file == current)
                {
                    continue;
                }
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (IOException e)
                {
                    Log.Warning($"[设置/日志] 删除失败 {file}: {e.Message}");
                }
            }
            if (deleted > 0)
            {
                Info(host, FAInfoBarSeverity.Success, AppUtils.Tr("settings.clear_log_success", ("count", deleted)));
            }
            else
            {
                Info(host, FAInfoBarSeverity.Informational, Tr("settings.no_logs_to_clear"));
            }
        }
        catch (Exception e)
        {
            Log.Error($"[设置/日志] 清理日志失败: {e.Message}");
            Info(host, FAInfoBarSeverity.Error, AppUtils.Tr("settings.clear_log_failed", ("error", e.Message)));
        }
    }

    // 提醒

    private Control BuildReminderPage()
    {
        var stack = MakePageStack(Tr("settings.reminder"));

        // 储存
        var notifyCard = CardSwitch(FASymbol.SaveLocal, Tr("settings.storage_full_notify"), Tr("settings.storage_full_notify_desc"), Config.StorageFullNotify);
        var thresholdCard = CardSpin(FASymbol.Ruler, Tr("settings.storage_full_threshold"), Tr("settings.storage_full_threshold_desc"), Config.StorageFullThreshold, 5, 50);
        var checkCard = CardButton(FASymbol.Refresh, Tr("settings.storage_full_checknow_title"), Tr("settings.storage_full_checknow_desc"), Tr("settings.storage_full_checknow_button"), StorageMonitor.Check);

        // 内存
        var memNotifyCard = CardSwitch(FASymbol.Scan, Tr("settings.resource_memory_notify"), Tr("settings.resource_memory_notify_desc"), Config.ResourceMemoryNotify);
        var memThresholdCard = CardSpin(FASymbol.Calculator, Tr("settings.resource_memory_threshold"), Tr("settings.resource_memory_threshold_desc"), Config.ResourceMemoryThreshold, 50, 100);

        // CPU
        var cpuNotifyCard = CardSwitch(FASymbol.Target, Tr("settings.resource_cpu_notify"), Tr("settings.resource_cpu_notify_desc"), Config.ResourceCpuNotify);
        var cpuThresholdCard = CardSpin(FASymbol.TwoBars, Tr("settings.resource_cpu_threshold"), Tr("settings.resource_cpu_threshold_desc"), Config.ResourceCpuThreshold, 50, 100);

        // 开关关闭时禁用对应阈值与立即检测
        void ApplyEnabled()
        {
            var storage = Config.StorageFullNotify.Value;
            var memory = Config.ResourceMemoryNotify.Value;
            var cpu = Config.ResourceCpuNotify.Value;
            thresholdCard.IsEnabled = storage;
            checkCard.IsEnabled = storage;
            memThresholdCard.IsEnabled = memory;
            cpuThresholdCard.IsEnabled = cpu;
        }
        ApplyEnabled();
        Config.StorageFullNotify.ValueChanged += _ => Dispatcher.UIThread.Post(ApplyEnabled);
        Config.ResourceMemoryNotify.ValueChanged += _ => Dispatcher.UIThread.Post(ApplyEnabled);
        Config.ResourceCpuNotify.ValueChanged += _ => Dispatcher.UIThread.Post(ApplyEnabled);

        stack.Children.Add(notifyCard);
        stack.Children.Add(thresholdCard);
        stack.Children.Add(checkCard);
        stack.Children.Add(memNotifyCard);
        stack.Children.Add(memThresholdCard);
        stack.Children.Add(cpuNotifyCard);
        stack.Children.Add(cpuThresholdCard);
        return stack;
    }

    // 空间

    private Control BuildStoragePage()
    {
        var stack = MakePageStack(Tr("settings.storage.title"));
        stack.Spacing = 8;

        // 本地磁盘
        stack.Children.Add(SectionHeader(Tr("settings.storage.drives"), FASymbol.Folder));
        var drivesPanel = new StackPanel();
        stack.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
            Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
            Padding = new Thickness(14, 4, 14, 4),
            Child = drivesPanel,
        });

        // 应用数据
        stack.Children.Add(SectionHeader(Tr("settings.storage.app_data"), FASymbol.Page));

        var totalValue = MakeMetricValue();
        var ratioValue = MakeMetricValue();
        var freeValue = MakeMetricValue();
        var overview = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,*,*") };
        overview.Children.Add(MakeMetricColumn(Tr("settings.storage.total"), totalValue, 0));
        overview.Children.Add(MakeMetricColumn(Tr("settings.storage.used_ratio"), ratioValue, 1));
        overview.Children.Add(MakeMetricColumn(Tr("settings.storage.free"), freeValue, 2));

        var dataBarGrid = new Grid();
        var dataBar = new Border
        {
            Height = 16,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128)),
            Child = dataBarGrid,
        };

        var legendPanel = new WrapPanel { Margin = new Thickness(0, -4, 0, 0) };

        var detailPanel = new StackPanel();
        var sizeTexts = new Dictionary<string, TextBlock>();
        for (var i = 0; i < DataUsageService.Categories.Length; i++)
        {
            var cat = DataUsageService.Categories[i];
            var size = new TextBlock
            {
                Text = "-",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.8,
            };
            sizeTexts[cat.Key] = size;
            var row = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
                MinHeight = 36,
            };
            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = ParseBrush(cat.ColorHex),
            };
            var name = new TextBlock
            {
                Text = Tr(cat.NameKey),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };
            Grid.SetColumn(dot, 0);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(size, 2);
            row.Children.Add(dot);
            row.Children.Add(name);
            row.Children.Add(size);
            detailPanel.Children.Add(row);
            if (i < DataUsageService.Categories.Length - 1)
            {
                detailPanel.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128)),
                });
            }
        }

        var cleanBtn = new Button
        {
            Content = Tr("settings.storage.clean_all"),
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        cleanBtn.Click += (_, _) => _ = CleanAllAsync(stack);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)),
            Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
            Padding = new Thickness(20, 16, 20, 16),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    overview,
                    dataBar,
                    legendPanel,
                    new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128)),
                        Margin = new Thickness(0, 2, 0, 2),
                    },
                    detailPanel,
                    cleanBtn,
                },
            },
        };
        stack.Children.Add(card);

        async Task ScanStorageAsync()
        {
            cleanBtn.IsEnabled = false;
            try
            {
                var drives = await Task.Run(DataUsageService.ScanDrives);
                var sizes = await Task.Run(DataUsageService.ScanSizes);
                var appDrive = DataUsageService.GetAppDrive();
                var threshold = Config.StorageFullThreshold.Value;
                var accent = Common.ParseAccentColor(Config.ThemeColor.Value);

                drivesPanel.Children.Clear();
                if (drives.Count == 0)
                {
                    drivesPanel.Children.Add(new TextBlock
                    {
                        Text = Tr("settings.storage.no_drives"),
                        Opacity = 0.6,
                        Margin = new Thickness(0, 10, 0, 10),
                    });
                }
                for (var i = 0; i < drives.Count; i++)
                {
                    drivesPanel.Children.Add(MakeDriveRow(drives[i], accent, threshold));
                    if (i < drives.Count - 1)
                    {
                        drivesPanel.Children.Add(new Border
                        {
                            Height = 1,
                            Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128)),
                            Margin = new Thickness(34, 0, 0, 0),
                        });
                    }
                }

                var appTotal = sizes.Values.Sum();
                totalValue.Text = DataUsageService.FormatBytes(appTotal);
                if (appDrive is not null)
                {
                    ratioValue.Text = $"{appTotal * 100.0 / appDrive.TotalBytes:F2}%";
                    freeValue.Text = DataUsageService.FormatBytes(appDrive.FreeBytes);
                }
                else
                {
                    ratioValue.Text = "-";
                    freeValue.Text = "-";
                }

                // 构成条
                dataBarGrid.Children.Clear();
                dataBarGrid.ColumnDefinitions = new ColumnDefinitions();
                foreach (var cat in DataUsageService.Categories)
                {
                    var size = sizes.TryGetValue(cat.Key, out var s) ? s : 0;
                    sizeTexts[cat.Key].Text = DataUsageService.FormatBytes(size);
                    if (size <= 0)
                    {
                        continue;
                    }
                    dataBarGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(size, GridUnitType.Star)));
                    var seg = new Border { Background = ParseBrush(cat.ColorHex) };
                    Grid.SetColumn(seg, dataBarGrid.Children.Count);
                    dataBarGrid.Children.Add(seg);
                }

                legendPanel.Children.Clear();
                foreach (var cat in DataUsageService.Categories)
                {
                    var item = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Margin = new Thickness(0, 2, 16, 2),
                    };
                    item.Children.Add(new Border
                    {
                        Width = 10,
                        Height = 10,
                        CornerRadius = new CornerRadius(3),
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = ParseBrush(cat.ColorHex),
                    });
                    item.Children.Add(new TextBlock
                    {
                        Text = Tr(cat.NameKey),
                        FontSize = 12,
                        Opacity = 0.75,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                    legendPanel.Children.Add(item);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[设置/空间] 扫描失败: {e.Message}");
                Info(stack, FAInfoBarSeverity.Error, Tr("settings.storage.scan_failed"));
            }
            finally
            {
                cleanBtn.IsEnabled = true;
            }
        }

        async Task CleanAllAsync(Panel host)
        {
            if (!await Common.Confirm(this, Tr("settings.storage.clean_all"), Tr("settings.storage.clean_all_confirm")))
            {
                return;
            }
            cleanBtn.IsEnabled = false;
            var deleted = await Task.Run(DataUsageService.CleanAll);
            Info(host, FAInfoBarSeverity.Success, AppUtils.Tr("settings.storage.clean_done", ("count", deleted)));
            await ScanStorageAsync();
        }

        /// <summary>单盘一行</summary>
        Control MakeDriveRow(DriveUsage d, Color accent, double threshold)
        {
            var low = d.FreePercent < threshold;
            var usedBrush = low ? new SolidColorBrush(Color.Parse("#E81123")) : new SolidColorBrush(accent);
            var trackBrush = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128));

            var barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(d.UsedBytes, GridUnitType.Star)));
            barGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(1, d.FreeBytes), GridUnitType.Star)));
            var usedSeg = new Border { Background = usedBrush };
            Grid.SetColumn(usedSeg, 0);
            barGrid.Children.Add(usedSeg);
            var freeSeg = new Border { Background = trackBrush };
            Grid.SetColumn(freeSeg, 1);
            barGrid.Children.Add(freeSeg);
            var bar = new Border
            {
                Height = 8,
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                Background = trackBrush,
                Child = barGrid,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0),
            };

            var badge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(34, 128, 128, 128)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = d.Letter,
                    FontSize = 12,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            // 卷标与盘符同名时退回文件系统类型 避免重复
            var name = new TextBlock
            {
                Text = string.Equals(d.Label, d.Letter, StringComparison.OrdinalIgnoreCase) ? d.Format : d.Label,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                MaxWidth = 190,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var stats = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.78,
                VerticalAlignment = VerticalAlignment.Center,
            };
            stats.Inlines ??= new InlineCollection();
            stats.Inlines.Add(new Run(
                $"{Tr("settings.storage.used")} {DataUsageService.FormatBytes(d.UsedBytes)} / {DataUsageService.FormatBytes(d.TotalBytes)} · "));
            var freeRun = new Run($"{Tr("settings.storage.free")} {DataUsageService.FormatBytes(d.FreeBytes)}");
            if (low)
            {
                freeRun.Foreground = new SolidColorBrush(Color.Parse("#E81123"));
            }
            stats.Inlines.Add(freeRun);

            var row = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,*,Auto"),
                MinHeight = 34,
            };
            Grid.SetColumn(badge, 0);
            Grid.SetColumn(name, 1);
            Grid.SetColumn(bar, 2);
            Grid.SetColumn(stats, 3);
            row.Children.Add(badge);
            row.Children.Add(name);
            row.Children.Add(bar);
            row.Children.Add(stats);
            return row;
        }

        _ = ScanStorageAsync();
        return stack;
    }

    private static TextBlock MakeMetricValue() => new()
    {
        Text = "-",
        FontSize = 20,
        FontWeight = FontWeight.SemiBold,
    };

    private static StackPanel MakeMetricColumn(string title, TextBlock value, int column)
    {
        var col = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock { Text = title, FontSize = 13, Opacity = 0.65 },
                value,
            },
        };
        Grid.SetColumn(col, column);
        return col;
    }

    private static IBrush ParseBrush(string hex) => new SolidColorBrush(Color.Parse(hex));

    // 隐私

    private Control BuildPrivacyPage()
    {
        var stack = MakePageStack(Tr("settings.privacy"));
        stack.Children.Add(new TextBlock
        {
            Text = Tr("settings.privacy_note"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var crashCard = CardSwitch(FASymbol.ReportHacked, Tr("settings.crash_upload"), Tr("settings.crash_upload_desc"), Config.CrashUpload);
        var usageCard = CardSwitch(FASymbol.Account, Tr("settings.usage_upload"), Tr("settings.usage_upload_desc"), Config.UsageUpload);

        stack.Children.Add(crashCard);
        stack.Children.Add(usageCard);
        // TID 只读小字 可选中复制 用户反馈问题时提供此ID定位上报数据
        stack.Children.Add(new SelectableTextBlock
        {
            Text = $"{Tr("settings.anonymous_id")} {Telemetry.TelemetryId}",
            FontSize = 12,
            Opacity = 0.7,
            Margin = new Thickness(0, 0, 0, 8),
        });
        return stack;
    }

    // 高级

    private Control BuildAdvancedPage()
    {
        var stack = MakePageStack(Tr("settings.advanced"));
        stack.Children.Add(CardCombo(FASymbol.ClosePane, Tr("settings.close_action"), Tr("settings.close_action_desc"), Config.CloseAction,
            new (string, string)[]
            {
                (Tr("settings.minimize_to_tray"), "minimize"),
                (Tr("settings.close_directly"), "close"),
            }));
        stack.Children.Add(CardSwitch(FASymbol.Switch, Tr("settings.allow_multiple_instances"), Tr("settings.allow_multiple_instances_desc"), Config.AllowMultipleInstances));
        stack.Children.Add(CardSwitch(FASymbol.Video, Tr("settings.gpu_acceleration"), Tr("settings.gpu_acceleration_desc"), Config.EnableGpuAcceleration));
        // GitHub 加速源
        stack.Children.Add(CardCombo(FASymbol.Globe, Tr("settings.github_source"), Tr("settings.github_source_desc"), Config.DownloadSource,
            DownloadSources.Sources
                .Select(kv => (Tr(kv.Value.NameKey), kv.Key))
                .ToArray()));

        // 配置导入/导出(
        var exportBtn = new Button { Content = Tr("settings.export_button"), MinWidth = 90 };
        var importBtn = new Button { Content = Tr("settings.import_button"), MinWidth = 90 };
        exportBtn.Click += (_, _) => _ = ExportConfigAsync(stack);
        importBtn.Click += (_, _) => _ = ImportConfigAsync(stack);
        var ioRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { exportBtn, importBtn },
        };
        stack.Children.Add(Card(FASymbol.SaveAs, Tr("settings.config_import_export"), Tr("settings.config_import_export_desc"), ioRow));

        stack.Children.Add(CardButton(FASymbol.Undo, Tr("settings.reset_default"), Tr("settings.reset_default_desc"), Tr("settings.reset_default_button"),
            () => _ = ResetDefaultAsync(stack)));
        stack.Children.Add(CardSwitch(FASymbol.Flag, Tr("settings.debug_mode"), Tr("settings.debug_mode_desc"), Config.DebugMode));
        return stack;
    }

    // 插件

    private Panel? _pluginsPageHost;
    private List<MarketPlugin>? _lastMarketPlugins;
    private bool _marketFetched;
    private bool _marketLoading;
    private bool _isMarketTab;
    private string? _downloadingPluginId;
    private double _downloadPercent;
    private TextBlock? _detailProgressText;

    private TextBox? _pluginsSearchBox;
    private StackPanel? _installedListHost;
    private StackPanel? _marketListHost;
    private Panel? _pluginsDetailHost;
    private string? _selectedLocalId;
    private string? _selectedMarketId;

    private readonly Dictionary<string, PluginListItemUi> _localItems = new();
    private readonly Dictionary<string, PluginListItemUi> _marketItems = new();

    private readonly Dictionary<string, bool> _enabledOverrides = new();

    private bool IsEnabledEffective(PluginInfo info) =>
        _enabledOverrides.TryGetValue(info.Id, out var v) ? v : info.Enabled;

    private Control BuildPluginsPage()
    {
        var stack = MakePageStack(Tr("settings.plugins.title"));
        _pluginsPageHost = stack;
        stack.Children.Add(BuildPluginsBody());
        if (!_marketFetched && !_marketLoading)
        {
            _ = FetchMarketIndexAsync();
        }
        return stack;
    }

    // 左列表右详情
    private Control BuildPluginsBody()
    {
        _installedListHost = new StackPanel { Spacing = 2 };
        _marketListHost = new StackPanel { Spacing = 2, IsVisible = false };
        _pluginsDetailHost = new Panel { MinHeight = 360 };

        // 搜索框
        _pluginsSearchBox = new TextBox
        {
            Watermark = Tr("settings.plugins.search"),
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        };
        var searchIcon = GlyphIcon('\uE721', 14);
        searchIcon.Opacity = 0.6;
        searchIcon.Margin = new Thickness(8, 0, 0, 0);
        searchIcon.VerticalAlignment = VerticalAlignment.Center;
        _pluginsSearchBox.InnerLeftContent = searchIcon;
        _pluginsSearchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(TextBox.Text))
            {
                if (_isMarketTab)
                {
                    RenderMarketList();
                }
                else
                {
                    RebuildPluginList();
                }
            }
        };

        // 市场 / 已安装
        var marketTab = new Avalonia.Controls.Primitives.TabStripItem { Content = Tr("settings.plugins.tab_market"), FontSize = 15 };
        var installedTab = new Avalonia.Controls.Primitives.TabStripItem { Content = Tr("settings.plugins.tab_installed"), FontSize = 15 };
        var tabStrip = new Avalonia.Controls.Primitives.TabStrip { VerticalAlignment = VerticalAlignment.Center };
        tabStrip.Items.Add(marketTab);
        tabStrip.Items.Add(installedTab);
        tabStrip.SelectedItem = marketTab;
        _isMarketTab = true;
        tabStrip.SelectionChanged += (_, _) =>
        {
            _isMarketTab = tabStrip.SelectedItem == marketTab;
            _installedListHost.IsVisible = !_isMarketTab;
            _marketListHost.IsVisible = _isMarketTab;
            if (_isMarketTab)
            {
                RenderMarketList();
            }
            else
            {
                RebuildPluginList();
            }
        };

        // 工具行
        var refreshBtn = MakeToolButton(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                FAIcons.IconCenter(FASymbol.Sync, 13),
                new TextBlock { Text = Tr("settings.plugins.market_refresh"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
            },
        }, null);
        refreshBtn.Click += (_, _) =>
        {
            if (_isMarketTab)
            {
                _ = FetchMarketIndexAsync(refreshMeta: true);
            }
            else
            {
                RefreshCatalog();
                RebuildPluginList();
            }
        };
        var moreBtn = MakeToolButton(GlyphIcon('\uE712', 14), Tr("settings.plugins.more"));
        moreBtn.Flyout = BuildPluginsMoreFlyout();
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 0, 0, 4) };
        toolbar.Children.Add(tabStrip);
        Grid.SetColumn(refreshBtn, 1);
        refreshBtn.VerticalAlignment = VerticalAlignment.Center;
        toolbar.Children.Add(refreshBtn);
        Grid.SetColumn(moreBtn, 2);
        moreBtn.VerticalAlignment = VerticalAlignment.Center;
        toolbar.Children.Add(moreBtn);

        var lists = new Panel();
        lists.Children.Add(_installedListHost);
        lists.Children.Add(_marketListHost);
        _installedListHost.IsVisible = !_isMarketTab;
        _marketListHost.IsVisible = _isMarketTab;

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        left.Children.Add(_pluginsSearchBox);
        Grid.SetRow(toolbar, 1);
        left.Children.Add(toolbar);
        Grid.SetRow(lists, 2);
        left.Children.Add(lists);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        root.Children.Add(left);
        _pluginsDetailHost.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(_pluginsDetailHost, 1);
        root.Children.Add(_pluginsDetailHost);

        RefreshCatalog();
        RebuildPluginList();
        RenderMarketList();
        return root;
    }

    // 目录重建
    private void RefreshCatalog() => PluginCatalog.Reload(_lastMarketPlugins);

    // 菜单
    private MenuFlyout BuildPluginsMoreFlyout()
    {
        var importItem = new MenuItem { Header = Tr("settings.plugins.install_from_local"), Icon = FAIcons.Icon(FASymbol.Download, 14) };
        importItem.Click += (_, _) => _ = ImportPluginAsync();
        var autoUpdateItem = new MenuItem
        {
            Header = Tr("settings.plugins.auto_update"),
            Icon = FAIcons.Icon(FASymbol.Sync, 14),
            ToggleType = Avalonia.Controls.MenuItemToggleType.CheckBox,
            IsChecked = Config.PluginsAutoUpdate.Value,
        };
        autoUpdateItem.Click += (_, _) => Config.PluginsAutoUpdate.Value = autoUpdateItem.IsChecked == true;
        var openFolderItem = new MenuItem { Header = Tr("settings.plugins.open_plugins_folder"), Icon = FAIcons.Icon(FASymbol.Folder, 14) };
        openFolderItem.Click += (_, _) => OpenPluginsFolder();
        var flyout = new MenuFlyout();
        flyout.Items.Add(importItem);
        flyout.Items.Add(autoUpdateItem);
        flyout.Items.Add(new Separator());
        flyout.Items.Add(openFolderItem);
        return flyout;
    }

    private void OpenPluginsFolder()
    {
        try
        {
            Directory.CreateDirectory(Paths.DataPlugins);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = Paths.DataPlugins, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Warning($"[设置/插件] 打开插件目录失败: {e.Message}");
        }
    }

    // 已安装列表

    private void RebuildPluginList()
    {
        if (_installedListHost is null)
        {
            return;
        }
        _installedListHost.Children.Clear();
        _localItems.Clear();
        var filter = _pluginsSearchBox?.Text?.Trim();
        var items = PluginCatalog.Items
            .Where(x => x.IsLocal)
            .Where(x => string.IsNullOrEmpty(filter) || MatchFilter(x.Id, x.Name, x.Description, filter))
            .ToList();
        if (items.Count == 0)
        {
            _selectedLocalId = null;
            _installedListHost.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(filter) ? Tr("settings.plugins.empty") : Tr("settings.plugins.filter_empty"),
                FontSize = 13,
                Opacity = 0.55,
                Margin = new Thickness(6, 12, 0, 0),
            });
            if (!_isMarketTab)
            {
                RenderPluginDetail(null, false);
            }
            return;
        }
        if (_selectedLocalId is null || items.All(x => x.Id != _selectedLocalId))
        {
            _selectedLocalId = items[0].Id;
        }
        foreach (var info in items)
        {
            var item = BuildPluginItem(info, marketTab: false, selected: _selectedLocalId == info.Id);
            _localItems[info.Id] = item;
            _installedListHost.Children.Add(item.Root);
        }
        if (!_isMarketTab)
        {
            RenderPluginDetail(PluginCatalog.Find(_selectedLocalId), false);
        }
    }

    // 市场列表

    private async Task FetchMarketIndexAsync(bool refreshMeta = false)
    {
        if (_marketListHost is null || _marketLoading)
        {
            return;
        }
        if (refreshMeta)
        {
            PluginMetaStore.Clear();
        }
        _marketLoading = true;
        RenderMarketList();
        List<MarketPlugin>? plugins;
        string? error;
        try
        {
            (plugins, error) = await PluginMarket.FetchIndexAsync(force: true);
        }
        catch (Exception e)
        {
            Log.Error($"[设置/插件市场] 索引拉取异常: {e.Message}");
            plugins = null;
            error = "network";
        }
        _marketLoading = false;
        _lastMarketPlugins = plugins;
        _marketFetched = error is null;
        RefreshCatalog();
        RenderMarketList();
        RebuildPluginList();
        if (_lastMarketPlugins is { Count: > 0 })
        {
            _ = EnrichMarketMetadataAsync(_lastMarketPlugins);
        }
    }

    private async Task EnrichMarketMetadataAsync(List<MarketPlugin> list)
    {
        await Task.WhenAll(list.Select(async mp =>
        {
            try
            {
                mp.Meta = await PluginMetaStore.GetAsync(mp);
            }
            catch (Exception e)
            {
                Log.Warning($"[设置/插件市场] {mp.Id} 回源失败: {e.Message}");
            }
        }));
        RefreshCatalog();
        RenderMarketList();
        RebuildPluginList();
    }

    private void RenderMarketList()
    {
        if (_marketListHost is null)
        {
            return;
        }
        _marketListHost.Children.Clear();
        _marketItems.Clear();
        if (_marketLoading)
        {
            // 进度条
            _marketListHost.Children.Add(new ProgressBar
            {
                IsIndeterminate = true,
                Margin = new Thickness(0, 2, 0, 8),
            });
            if (_isMarketTab)
            {
                RenderPluginDetail(null, true);
            }
            return;
        }
        if (_lastMarketPlugins is null)
        {
            var retry = MakeToolButton(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    FAIcons.IconCenter(FASymbol.Sync, 13),
                    new TextBlock { Text = Tr("settings.plugins.market_retry"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                },
            }, null);
            retry.Click += (_, _) => _ = FetchMarketIndexAsync(refreshMeta: true);
            _marketListHost.Children.Add(new FAInfoBar
            {
                Severity = FAInfoBarSeverity.Error,
                Message = Tr("settings.plugins.market_network_error"),
                IsOpen = true,
                IsClosable = false,
                Margin = new Thickness(0, 0, 0, 8),
            });
            _marketListHost.Children.Add(retry);
            if (_isMarketTab)
            {
                RenderPluginDetail(null, true);
            }
            return;
        }
        var filter = _pluginsSearchBox?.Text?.Trim();
        var items = PluginCatalog.Items
            .Where(x => x.IsMarket)
            .Where(x => string.IsNullOrEmpty(filter) || MatchFilter(x.Id, x.Name, x.Description, filter))
            .ToList();
        if (items.Count == 0)
        {
            _selectedMarketId = null;
            _marketListHost.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(filter) ? Tr("settings.plugins.market_empty") : Tr("settings.plugins.filter_empty"),
                FontSize = 13,
                Opacity = 0.55,
                Margin = new Thickness(6, 12, 0, 0),
            });
            if (_isMarketTab)
            {
                RenderPluginDetail(null, true);
            }
            return;
        }
        if (_selectedMarketId is null || items.All(x => x.Id != _selectedMarketId))
        {
            _selectedMarketId = items[0].Id;
        }
        foreach (var info in items)
        {
            var item = BuildPluginItem(info, marketTab: true, selected: _selectedMarketId == info.Id);
            _marketItems[info.Id] = item;
            _marketListHost.Children.Add(item.Root);
        }
        if (_isMarketTab)
        {
            RenderPluginDetail(PluginCatalog.Find(_selectedMarketId), true);
        }
    }

    // 条目
    private PluginListItemUi BuildPluginItem(PluginInfo info, bool marketTab, bool selected)
    {
        var statusIcons = new List<Control>();
        if (info.PendingRestart)
        {
            statusIcons.Add(StatusGlyph('\uE823', null, Tr("settings.plugins.restart_hint")));
        }
        if (info.IsLocal && info.Status == PluginLoadStatus.Error)
        {
            statusIcons.Add(StatusGlyph('\uE783', Color.Parse("#E81123"), Tr("settings.plugins.state_error")));
        }
        if (info.IsUpdateAvailable)
        {
            statusIcons.Add(StatusGlyph('\uE1A0', null, Tr("settings.plugins.market_updatable")));
        }
        if (info.IsIncompatible)
        {
            statusIcons.Add(StatusGlyph('\uE783', Color.Parse("#E81123"), Tr("settings.plugins.market_incompatible")));
        }

        return BuildPluginListItem(
            info.Id,
            info.IconSource,
            info.IsLocal ? FASymbol.AllApps : FASymbol.Shop,
            info.Name,
            info.Description,
            dimmed: info.IsLocal && info.Status != PluginLoadStatus.Loaded,
            statusIcons,
            BuildPluginItemActions(info, marketTab),
            selected,
            marketTab ? () => SelectMarketPlugin(info) : () => SelectLocalPlugin(info));
    }

    // 条目右下角操作
    private Control BuildPluginItemActions(PluginInfo info, bool marketTab)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom };
        if (info.IsLocal)
        {
            if (info.IsUpdateAvailable && info.Market is not null)
            {
                var market = info.Market;
                var updateBtn = MakeItemIconButton('\uE1A0', Tr("settings.plugins.market_update"), accent: true);
                updateBtn.Click += (_, _) => _ = InstallMarketPluginAsync(market);
                row.Children.Add(updateBtn);
            }
            var removeBtn = MakeItemIconButton('\uE74D', Tr("settings.plugins.uninstall"));
            removeBtn.Click += (_, _) => _ = UninstallPluginAsync(info);
            row.Children.Add(removeBtn);

            var toggle = new CheckBox
            {
                IsChecked = IsEnabledEffective(info),
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            ToolTip.SetTip(toggle, Tr("settings.plugins.enabled"));
            toggle.IsCheckedChanged += async (_, _) =>
            {
                var enabled = toggle.IsChecked == true;
                if (enabled == IsEnabledEffective(info))
                {
                    return;
                }
                _enabledOverrides[info.Id] = enabled;
                PluginService.SetEnabled(info.Id, enabled);
                if (info.Local is not null)
                {
                    info.Local.Enabled = enabled;
                }
                await Common.ConfirmPendingRestartAsync(Common.ResolveActiveWindow(this),
                    AppUtils.Tr("settings.plugins.restart_hint"), AppUtils.Tr("settings.restart_required_desc"));
                RefreshCatalog();
                RebuildPluginList();
                RenderMarketList();
            };
            row.Children.Add(toggle);
            return row;
        }
        if (info.PendingRestart)
        {
            row.Children.Add(new TextBlock
            {
                Text = Tr("settings.plugins.restart_hint"),
                FontSize = 11,
                Opacity = 0.65,
                VerticalAlignment = VerticalAlignment.Bottom,
            });
            return row;
        }
        if (info.Market is not null && !info.IsIncompatible)
        {
            var market = info.Market;
            var installBtn = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = FAIcons.Icon(info.IsUpdateAvailable ? FASymbol.Sync : FASymbol.Download, 14),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            installBtn.Classes.Add("accent");
            ToolTip.SetTip(installBtn, Tr("settings.plugins.market_install"));
            installBtn.Click += (_, _) => _ = InstallMarketPluginAsync(market);
            row.Children.Add(installBtn);
        }
        return row;
    }

    // 选中切换
    private void SelectLocalPlugin(PluginInfo info)
    {
        if (_selectedLocalId == info.Id)
        {
            return;
        }
        if (_selectedLocalId is not null && _localItems.TryGetValue(_selectedLocalId, out var prev))
        {
            prev.SetSelected(false);
        }
        _selectedLocalId = info.Id;
        if (_localItems.TryGetValue(info.Id, out var cur))
        {
            cur.SetSelected(true);
        }
        RenderPluginDetail(info, false);
    }

    private void SelectMarketPlugin(PluginInfo info)
    {
        if (_selectedMarketId == info.Id)
        {
            return;
        }
        if (_selectedMarketId is not null && _marketItems.TryGetValue(_selectedMarketId, out var prev))
        {
            prev.SetSelected(false);
        }
        _selectedMarketId = info.Id;
        if (_marketItems.TryGetValue(info.Id, out var cur))
        {
            cur.SetSelected(true);
        }
        RenderPluginDetail(info, true);
    }

    // 详情
    private void RenderPluginDetail(PluginInfo? info, bool marketTab)
    {
        var host = _pluginsDetailHost;
        if (host is null)
        {
            return;
        }
        host.Children.Clear();
        _detailProgressText = null;
        if (info is null)
        {
            host.Children.Add(MakeDetailEmptyState());
            return;
        }
        host.Children.Add(BuildPluginDetail(info));
    }

    private Control BuildPluginDetail(PluginInfo info)
    {
        var root = new StackPanel { Spacing = 12 };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(4, 8, 4, 0) };
        var chip = MakeIconChip(info.IconSource, info.IsLocal ? FASymbol.AllApps : FASymbol.Shop, 60);
        chip.VerticalAlignment = VerticalAlignment.Top;
        chip.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(chip);

        var textCol = new StackPanel { Spacing = 4 };
        textCol.Children.Add(new TextBlock { Text = info.Name, FontSize = 24, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
        // 元信息行
        var meta = new WrapPanel { Orientation = Orientation.Horizontal };
        void AddMetaText(string? t)
        {
            if (string.IsNullOrEmpty(t))
            {
                return;
            }
            meta.Children.Add(new TextBlock { Text = t, FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
        }
        void AddMetaSep()
        {
            if (meta.Children.Count > 0)
            {
                meta.Children.Add(new TextBlock { Text = "|", FontSize = 12, Opacity = 0.35, Margin = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            }
        }
        var version = info.IsLocal ? info.LocalVersion : info.MarketVersion;
        AddMetaText(string.IsNullOrEmpty(version) ? null : version);
        if (!string.IsNullOrEmpty(info.Homepage))
        {
            AddMetaSep();
            var homepage = info.Homepage;
            var link = new Button
            {
                Content = new TextBlock { Text = Tr("settings.plugins.homepage_link"), FontSize = 12, Foreground = AccentBrush() },
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            link.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = homepage, UseShellExecute = true });
                }
                catch (Exception e)
                {
                    Log.Warning($"[设置/插件] 打开主页失败: {e.Message}");
                }
            };
            meta.Children.Add(link);
        }
        if (!string.IsNullOrEmpty(info.Author))
        {
            AddMetaSep();
            AddMetaText(info.Author);
        }
        textCol.Children.Add(meta);
        Grid.SetColumn(textCol, 1);
        header.Children.Add(textCol);

        // 头部菜单
        if (info.IsLocal && info.Directory is not null)
        {
            var moreBtn = MakeToolButton(GlyphIcon('\uE712', 14), Tr("settings.plugins.more"));
            moreBtn.VerticalAlignment = VerticalAlignment.Top;
            var openItem = new MenuItem { Header = Tr("settings.plugins.open_folder"), Icon = FAIcons.Icon(FASymbol.Folder, 14) };
            openItem.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = info.Directory, UseShellExecute = true });
                }
                catch (Exception e)
                {
                    Log.Warning($"[设置/插件] 打开目录失败: {e.Message}");
                }
            };
            var uninstallItem = new MenuItem { Header = Tr("settings.plugins.uninstall"), Icon = FAIcons.Icon(FASymbol.Dismiss, 14) };
            uninstallItem.Click += (_, _) => _ = UninstallPluginAsync(info);
            var flyout = new MenuFlyout();
            flyout.Items.Add(openItem);
            flyout.Items.Add(uninstallItem);
            moreBtn.Flyout = flyout;
            Grid.SetColumn(moreBtn, 2);
            header.Children.Add(moreBtn);
        }
        root.Children.Add(header);

        // 下载进度
        if (_downloadingPluginId == info.Id)
        {
            var dlRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4, 0, 0, 0) };
            dlRow.Children.Add(new FAProgressRing { Width = 16, Height = 16, IsIndeterminate = true });
            _detailProgressText = new TextBlock
            {
                Text = AppUtils.Tr("settings.plugins.market_downloading", ("percent", ((int)_downloadPercent).ToString())),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            dlRow.Children.Add(_detailProgressText);
            root.Children.Add(dlRow);
        }

        root.Children.Add(BuildReadmeContent(info));
        return root;
    }

    private Control BuildReadmeContent(PluginInfo info)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(4, 4, 4, 0) };
        if (info.IsLocal && info.Status == PluginLoadStatus.Error && !string.IsNullOrEmpty(info.Error))
        {
            panel.Children.Add(new FAInfoBar
            {
                Severity = FAInfoBarSeverity.Error,
                Message = info.Error,
                IsOpen = true,
                IsClosable = false,
            });
        }
        if (string.IsNullOrEmpty(info.ReadmeSource))
        {
            panel.Children.Add(new TextBlock { Text = Tr("settings.plugins.no_readme"), FontSize = 13, Opacity = 0.6 });
            return panel;
        }
        // 已缓存
        if (ReadmeCache.TryGetValue(info.ReadmeSource, out var cached))
        {
            panel.Children.Add(new MarkdownScrollViewer { Markdown = cached });
            return panel;
        }
        // 未缓存
        var holder = new StackPanel { Spacing = 10 };
        holder.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 3, HorizontalAlignment = HorizontalAlignment.Stretch });
        var skeleton = BuildReadmeSkeleton();
        holder.Children.Add(skeleton);
        panel.Children.Add(holder);
        var source = info.ReadmeSource;
        _ = Task.Run(async () =>
        {
            var doc = await GetMarkdownAsync(source);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    StopPulse(skeleton);
                    holder.Children.Clear();
                    holder.Children.Add(doc is null
                        ? new TextBlock { Text = Tr("settings.plugins.no_readme"), FontSize = 13, Opacity = 0.6 }
                        : new MarkdownScrollViewer { Markdown = doc });
                }
                catch
                {
                }
            });
        });
        return panel;
    }

    // 详情空态
    private Control MakeDetailEmptyState()
    {
        var icon = FAIcons.Icon(FASymbol.AllApps, 40);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        return new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Opacity = 0.5,
            Children =
            {
                icon,
                new TextBlock { Text = Tr("settings.plugins.detail_empty"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center },
            },
        };
    }

    private async Task InstallMarketPluginAsync(MarketPlugin mp)
    {
        if (_pluginsPageHost is null || _downloadingPluginId is not null)
        {
            return;
        }
        _downloadingPluginId = mp.Id;
        _downloadPercent = 0;
        RenderMarketList();
        if (_isMarketTab && _selectedMarketId == mp.Id && PluginCatalog.Find(mp.Id) is { } selected)
        {
            RenderPluginDetail(selected, true);
        }
        try
        {
            var (ok, msg) = await PluginMarket.InstallFromMarketAsync(mp, pct =>
                Dispatcher.UIThread.Post(() =>
                {
                    _downloadPercent = pct;
                    if (_detailProgressText is not null)
                    {
                        _detailProgressText.Text = AppUtils.Tr("settings.plugins.market_downloading",
                            ("percent", ((int)Math.Clamp(pct, 0, 100)).ToString()));
                    }
                }));
            _downloadingPluginId = null;
            if (ok)
            {
                await Common.ConfirmPendingRestartAsync(Common.ResolveActiveWindow(this),
                    AppUtils.Tr("settings.plugins.import_success", ("name", msg)),
                    AppUtils.Tr("settings.restart_required_desc"));
            }
            else if (msg == "sha")
            {
                Info(_pluginsPageHost, FAInfoBarSeverity.Error, Tr("settings.plugins.market_sha_mismatch"));
            }
            else
            {
                Info(_pluginsPageHost, FAInfoBarSeverity.Error, AppUtils.Tr("settings.plugins.import_failed", ("error", msg)));
            }
        }
        catch (Exception e)
        {
            _downloadingPluginId = null;
            Info(_pluginsPageHost, FAInfoBarSeverity.Error, AppUtils.Tr("settings.plugins.import_failed", ("error", e.Message)));
        }
        RefreshCatalog();
        RenderMarketList();
        RebuildPluginList();
    }

    // 通用小件

    private static Color AccentColor() => Common.ParseAccentColor(Config.ThemeColor.Value);

    private static IBrush AccentBrush() => new SolidColorBrush(AccentColor());

    // Segoe Fluent 字图标
    private static FAFontIcon GlyphIcon(char glyph, double fontSize = 16) => new()
    {
        FontFamily = FAIcons.SymbolsFont,
        Glyph = glyph.ToString(),
        FontSize = fontSize,
    };

    // 透明工具小按钮
    private static Button MakeToolButton(object content, string? tooltip)
    {
        var btn = new Button
        {
            Content = content,
            Padding = new Thickness(8, 5, 8, 5),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(tooltip))
        {
            ToolTip.SetTip(btn, tooltip);
        }
        return btn;
    }

    // 条目右下角小按钮
    private static Button MakeItemIconButton(char glyph, string tooltip, bool accent = false)
    {
        var btn = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = GlyphIcon(glyph, 14),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        if (accent)
        {
            btn.Classes.Add("accent");
        }
        ToolTip.SetTip(btn, tooltip);
        return btn;
    }

    // 右上角状态小图标
    private static FAFontIcon StatusGlyph(char glyph, Color? foreground = null, string? tooltip = null)
    {
        var icon = GlyphIcon(glyph, 16);
        if (foreground is not null)
        {
            icon.Foreground = new SolidColorBrush(foreground.Value);
        }
        if (!string.IsNullOrEmpty(tooltip))
        {
            ToolTip.SetTip(icon, tooltip);
        }
        icon.VerticalAlignment = VerticalAlignment.Center;
        return icon;
    }

    // 列表条目
    private sealed class PluginListItemUi
    {
        public Border Root = null!;
        public Border Bar = null!;
        public Control Content = null!;
        public Color Accent;

        public void SetSelected(bool selected)
        {
            Bar.IsVisible = selected;
            Root.Background = selected ? new SolidColorBrush(Accent, 0.10) : Brushes.Transparent;
            Content.Margin = new Thickness(selected ? 8 : 4, 0, 0, 0);
        }
    }

    private PluginListItemUi MakeListItemRoot(bool selected, Control content)
    {
        var accent = AccentColor();
        var host = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var bar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 2),
            IsVisible = selected,
        };
        host.Children.Add(bar);
        content.Margin = new Thickness(selected ? 8 : 4, 0, 0, 0);
        Grid.SetColumn(content, 1);
        host.Children.Add(content);
        var root = new Border
        {
            Background = selected ? new SolidColorBrush(accent, 0.10) : Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Child = host,
        };
        return new PluginListItemUi { Root = root, Bar = bar, Content = content, Accent = accent };
    }

    // 条目主体
    private PluginListItemUi BuildPluginListItem(
        string pluginId,
        string? iconSource,
        FASymbol fallbackIcon,
        string name,
        string description,
        bool dimmed,
        List<Control> statusIcons,
        Control? bottomActions,
        bool selected,
        Action activate)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("*,*"),
        };
        var chip = MakeIconChip(iconSource, fallbackIcon, 48);
        chip.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetRowSpan(chip, 2);
        grid.Children.Add(chip);

        var nameText = new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(nameText, 1);
        grid.Children.Add(nameText);

        var descText = new TextBlock
        {
            Text = string.IsNullOrEmpty(description) ? pluginId : description,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(descText, 1);
        Grid.SetRow(descText, 1);
        grid.Children.Add(descText);

        if (statusIcons.Count > 0)
        {
            var badges = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            foreach (var b in statusIcons)
            {
                badges.Children.Add(b);
            }
            Grid.SetColumn(badges, 2);
            grid.Children.Add(badges);
        }

        if (bottomActions is not null)
        {
            bottomActions.VerticalAlignment = VerticalAlignment.Bottom;
            bottomActions.HorizontalAlignment = HorizontalAlignment.Right;
            bottomActions.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(bottomActions, 2);
            Grid.SetRow(bottomActions, 0);
            Grid.SetRowSpan(bottomActions, 2);
            grid.Children.Add(bottomActions);
        }

        var item = MakeListItemRoot(selected, grid);
        if (dimmed)
        {
            item.Root.Opacity = 0.75;
        }
        item.Root.PointerReleased += (_, _) => activate();
        return item;
    }

    // 图标 chip
    private Border MakeIconChip(string? source, FASymbol fallback, double size)
    {
        var chip = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Math.Max(6, size / 6)),
            Background = new SolidColorBrush(Colors.Gray, 0.14),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
            Child = FAIcons.IconCenter(fallback, size * 0.45),
        };
        if (string.IsNullOrWhiteSpace(source))
        {
            return chip;
        }
        if (IconBitmapCache.TryGetValue(source, out var cached))
        {
            SetChipImage(chip, cached, size);
            return chip;
        }
        StartPulse(chip);
        LoadIconIntoChipAsync(chip, source, size);
        return chip;
    }

    // 骨架
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, DispatcherTimer> PulseTimers = new();

    private static void StartPulse(Control target)
    {
        target.Transitions ??= new Transitions();
        target.Transitions.Add(new DoubleTransition
        {
            Property = Visual.OpacityProperty,
            Duration = TimeSpan.FromMilliseconds(650),
            Easing = new SineEaseInOut(),
        });
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(720) };
        timer.Tick += (_, _) => target.Opacity = target.Opacity > 0.6 ? 0.35 : 1.0;
        timer.Start();
        PulseTimers.AddOrUpdate(target, timer);
    }

    private static void StopPulse(Control target)
    {
        if (PulseTimers.TryGetValue(target, out var timer))
        {
            timer.Stop();
            PulseTimers.Remove(target);
        }
        target.Opacity = 1;
    }

    // 说明骨架
    private static StackPanel BuildReadmeSkeleton()
    {
        var panel = new StackPanel { Spacing = 10 };
        StartPulse(panel);
        foreach (var width in new[] { 1.0, 0.92, 0.75, 0.86, 0.5 })
        {
            panel.Children.Add(new Border
            {
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Colors.Gray, 0.18),
                Width = 320 * width,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        }
        return panel;
    }

    private static void SetChipImage(Border chip, Bitmap bitmap, double size)
    {
        StopPulse(chip);
        chip.Child = new Image
        {
            Source = bitmap,
            Width = size,
            Height = size,
            Stretch = Stretch.UniformToFill,
        };
    }

    // 图标内存缓存 去重
    private static readonly Dictionary<string, Bitmap> IconBitmapCache = new();
    private static readonly Dictionary<string, Task<byte[]?>> IconFetchTasks = new();

    private static async void LoadIconIntoChipAsync(Border chip, string source, double size)
    {
        var bitmap = await GetIconBitmapAsync(source);
        if (bitmap is null)
        {
            Dispatcher.UIThread.Post(() => StopPulse(chip));
            return;
        }
        Dispatcher.UIThread.Post(() => SetChipImage(chip, bitmap, size));
    }

    private static async Task<Bitmap?> GetIconBitmapAsync(string source)
    {
        if (IconBitmapCache.TryGetValue(source, out var cached))
        {
            return cached;
        }
        if (!IconFetchTasks.TryGetValue(source, out var task))
        {
            task = LoadIconBytesAsync(source);
            IconFetchTasks[source] = task;
        }
        var bytes = await task;
        IconFetchTasks.Remove(source);
        if (bytes is null)
        {
            return null;
        }
        try
        {
            var bitmap = new Bitmap(new MemoryStream(bytes));
            IconBitmapCache[source] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string IconDiskCachePath(string url)
    {
        var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(url)))[..24] + ".img";
        return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Glimpseon", "iconcache", name);
    }

    // 文件直读
    private static async Task<byte[]?> LoadIconBytesAsync(string? source)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }
            if (System.IO.File.Exists(source))
            {
                return await File.ReadAllBytesAsync(source);
            }
            if (!source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            var diskPath = IconDiskCachePath(source);
            if (File.Exists(diskPath))
            {
                try
                {
                    return await File.ReadAllBytesAsync(diskPath);
                }
                catch
                {
                }
            }
            foreach (var url in DownloadSources.Candidates(source))
            {
                try
                {
                    using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                    var bytes = await http.GetByteArrayAsync(url);
                    if (bytes is { Length: > 0 })
                    {
                        try
                        {
                            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(diskPath)!);
                            await File.WriteAllBytesAsync(diskPath, bytes);
                        }
                        catch
                        {
                        }
                        return bytes;
                    }
                }
                catch
                {
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    // 加载 readme 文档
    private static readonly Dictionary<string, string> ReadmeCache = new();
    private static readonly Dictionary<string, Task<string?>> ReadmeTasks = new();

    private static Task<string?> GetMarkdownAsync(string source)
    {
        if (ReadmeCache.TryGetValue(source, out var cached))
        {
            return Task.FromResult<string?>(cached);
        }
        if (ReadmeTasks.TryGetValue(source, out var running))
        {
            return running;
        }
        var task = LoadMarkdownAsync(source);
        ReadmeTasks[source] = task;
        return task;
    }

    private static async Task<string?> LoadMarkdownAsync(string? source)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }
            string? doc = null;
            if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var url in DownloadSources.Candidates(source))
                {
                    try
                    {
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                        doc = await http.GetStringAsync(url);
                        if (!string.IsNullOrWhiteSpace(doc))
                        {
                            break;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            else if (File.Exists(source))
            {
                doc = await File.ReadAllTextAsync(source);
            }
            ReadmeTasks.Remove(source);
            if (string.IsNullOrWhiteSpace(doc))
            {
                Log.Debug($"[设置/插件] readme 取不到: {source}");
                return null;
            }
            ReadmeCache[source] = doc;
            return doc;
        }
        catch (Exception e)
        {
            ReadmeTasks.Remove(source);
            Log.Warning($"[设置/插件] readme 加载失败 {source}: {e.Message}");
            return null;
        }
    }

    private static bool MatchFilter(string id, string name, string description, string filter) =>
        name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        description.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        id.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private async Task ImportPluginAsync()
    {
        if (_pluginsPageHost is null)
        {
            return;
        }
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Glimpseon Plugin") { Patterns = ["*.gplugin", "*.zip"] }],
            });
            if (files.Count == 0)
            {
                return;
            }
            var path = files[0].Path.LocalPath;
            var (ok, msg) = await Task.Run(() => PluginService.InstallPackage(path));
            if (ok)
            {
                await Common.ConfirmPendingRestartAsync(this,
                    AppUtils.Tr("settings.plugins.import_success", ("name", msg)),
                    AppUtils.Tr("settings.restart_required_desc"));
            }
            else
            {
                Info(_pluginsPageHost, FAInfoBarSeverity.Error, AppUtils.Tr("settings.plugins.import_failed", ("error", msg)));
            }
            RefreshCatalog();
            RebuildPluginList();
            RenderMarketList();
        }
        catch (Exception e)
        {
            Info(_pluginsPageHost, FAInfoBarSeverity.Error, AppUtils.Tr("settings.plugins.import_failed", ("error", e.Message)));
        }
    }

    private async Task UninstallPluginAsync(PluginInfo info)
    {
        if (_pluginsPageHost is null)
        {
            return;
        }
        if (!await Common.Confirm(this, Tr("settings.plugins.uninstall"),
                AppUtils.Tr("settings.plugins.uninstall_confirm", ("name", info.Name))))
        {
            return;
        }
        var (ok, msg) = await Task.Run(() => PluginService.Uninstall(info.Id));
        if (ok && msg == "pending")
        {
            Info(_pluginsPageHost, FAInfoBarSeverity.Warning,
                AppUtils.Tr("settings.plugins.uninstall_pending", ("name", info.Name)));
        }
        else if (ok)
        {
            Info(_pluginsPageHost, FAInfoBarSeverity.Success, AppUtils.Tr("settings.plugins.uninstall_success", ("name", info.Name)));
        }
        else
        {
            Info(_pluginsPageHost, FAInfoBarSeverity.Error, AppUtils.Tr("settings.plugins.uninstall_failed", ("error", msg)));
        }
        RefreshCatalog();
        RebuildPluginList();
        RenderMarketList();
    }

    private async Task ExportConfigAsync(Panel host)
    {
        try
        {
            if (!File.Exists(Config.ConfigPath))
            {
                Info(host, FAInfoBarSeverity.Warning, Tr("settings.config_not_exist_export"));
                return;
            }
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Tr("settings.export_config"),
                SuggestedFileName = $"Glimpseon_Config_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
            });
            if (file is null)
            {
                Log.Debug("[设置/高级] 配置导出已取消");
                return;
            }
            File.Copy(Config.ConfigPath, file.Path.LocalPath, true);
            Log.Info($"[设置/高级] 配置已导出: {file.Path.LocalPath}");
            Info(host, FAInfoBarSeverity.Success, AppUtils.Tr("settings.export_success", ("path", file.Path.LocalPath)));
        }
        catch (Exception e)
        {
            Log.Error($"[设置/高级] 配置导出失败: {e.Message}");
            Info(host, FAInfoBarSeverity.Error, AppUtils.Tr("settings.export_failed", ("error", e.Message)));
        }
    }

    private async Task ImportConfigAsync(Panel host)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Tr("settings.import_config"),
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
            });
            if (files.Count == 0)
            {
                Log.Debug("[设置/高级] 配置导入已取消");
                return;
            }
            var path = files[0].Path.LocalPath;
            if (!File.Exists(path))
            {
                Info(host, FAInfoBarSeverity.Warning, Tr("settings.selected_file_not_exist"));
                return;
            }
            JsonObject? imported;
            try
            {
                imported = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            }
            catch (JsonException)
            {
                Info(host, FAInfoBarSeverity.Error, Tr("settings.config_json_parse_error"));
                return;
            }
            if (imported is null)
            {
                Info(host, FAInfoBarSeverity.Error, Tr("settings.config_format_error"));
                return;
            }
            if (!await Common.Confirm(this, Tr("settings.import_config"), Tr("settings.import_config_confirm")))
            {
                Log.Debug($"[设置/高级] 导入已取消: {path}");
                return;
            }
            if (File.Exists(Config.ConfigPath))
            {
                File.Copy(Config.ConfigPath, Config.ConfigPath + ".backup", true);
            }
            File.WriteAllText(Config.ConfigPath, imported.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Config.Load();
            ApplyConfigToAllUi();
            Log.Info($"[设置/高级] 配置已导入: {path}");
            Info(host, FAInfoBarSeverity.Success, AppUtils.Tr("settings.import_success", ("path", path)));
            var owner = Common.ResolveActiveWindow(this);
            if (await Common.ConfirmRestartAsync(owner))
            {
                Log.Info("[设置] 用户确认立即重启以应用导入的配置");
                AppUtils.RequestRestart();
            }
            else
            {
                Log.Debug("[设置] 用户选择稍后重启");
            }
        }
        catch (Exception e)
        {
            Log.Error($"[设置/高级] 配置导入失败: {e.Message}");
            Info(host, FAInfoBarSeverity.Error, AppUtils.Tr("settings.import_failed", ("error", e.Message)));
        }
    }

    private async Task ResetDefaultAsync(Panel host)
    {
        try
        {
            var owner = Common.ResolveActiveWindow(this);
            if (!await Common.ConfirmRestartWithAsync(owner,
                    Tr("settings.restart_title"), Tr("settings.reset_default_confirm")))
            {
                Log.Debug("[设置/高级] 配置重置已取消");
                return;
            }
            if (File.Exists(Config.ConfigPath))
            {
                File.Delete(Config.ConfigPath);
            }
            Paths.EnsureDataDirs();
            File.WriteAllText(Config.ConfigPath, Config.DefaultCfg().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Config.Load();
            ApplyConfigToAllUi();
            Log.Info("[设置/高级] 配置已重置为默认, 立即重启以生效");
            AppUtils.RequestRestart();
        }
        catch (Exception e)
        {
            Log.Error($"[设置/高级] 配置重置失败: {e.Message}");
            Info(host, FAInfoBarSeverity.Error, AppUtils.Tr("settings.reset_failed", ("error", e.Message)));
        }
    }
}

/// <summary>网格预览</summary>
public sealed class GridPreviewControl : Control
{
    private int _shortSideCells = 6;
    private int _insetPercent = 5;

    public GridPreviewControl()
    {
        Width = 280;
        Height = 120;
    }

    public int ShortSideCells
    {
        get => _shortSideCells;
        set
        {
            _shortSideCells = Math.Max(1, value);
            InvalidateVisual();
        }
    }

    public int InsetPercent
    {
        get => _insetPercent;
        set
        {
            _insetPercent = Math.Clamp(value, 0, 30);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1)
        {
            return;
        }
        var dark = ThemeSense.IsDark(this);
        var bg = new SolidColorBrush(dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0));
        ctx.FillRectangle(bg, new Rect(0, 0, w, h), 6);

        // 内缩后区域
        var insetX = w * _insetPercent / 100.0;
        var insetY = h * _insetPercent / 100.0;
        var area = new Rect(insetX, insetY, Math.Max(0, w - insetX * 2), Math.Max(0, h - insetY * 2));
        if (area.Width <= 1 || area.Height <= 1)
        {
            return;
        }
        // 短边格子数 → 格子边长
        var cell = Math.Min(area.Height, area.Width) / _shortSideCells;
        if (cell <= 0.5)
        {
            return;
        }
        var line = new Pen(new SolidColorBrush(dark ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(110, 0, 0, 0)), 1);
        for (var x = area.X; x <= area.Right + 0.01; x += cell)
        {
            ctx.DrawLine(line, new Point(x, area.Y), new Point(x, area.Bottom));
        }
        for (var y = area.Y; y <= area.Bottom + 0.01; y += cell)
        {
            ctx.DrawLine(line, new Point(area.X, y), new Point(area.Right, y));
        }
    }
}

/// <summary>卡片圆角预览</summary>
public sealed class CornerRadiusPreviewControl : Control
{
    private int _cardRadius = 16;
    private int _cardOpacity = 55;

    public CornerRadiusPreviewControl()
    {
        Width = 280;
        Height = 120;
    }

    public int CardRadius
    {
        get => _cardRadius;
        set
        {
            _cardRadius = Math.Clamp(value, 0, 29);
            InvalidateVisual();
        }
    }

    public int CardOpacity
    {
        get => _cardOpacity;
        set
        {
            _cardOpacity = Math.Clamp(value, 0, 100);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1)
        {
            return;
        }
        var dark = ThemeSense.IsDark(this);
        var bg = new SolidColorBrush(dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0));
        ctx.FillRectangle(bg, new Rect(0, 0, w, h), 6);

        var cardRect = new Rect(w * 0.18, h * 0.18, w * 0.64, h * 0.64);
        var alpha = (byte)Math.Clamp(_cardOpacity * 255 / 100, 0, 255);
        var cardColor = dark ? Color.FromArgb(alpha, 255, 255, 255) : Color.FromArgb(alpha, 0, 0, 0);
        ctx.FillRectangle(new SolidColorBrush(cardColor), cardRect, _cardRadius);
    }
}
