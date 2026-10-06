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
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Glimpseon.Core;
using Glimpseon.Core.Services;

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
        ("privacy", "settings.privacy"),
        ("advanced", "settings.advanced"),
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
        NavPrivacy.Content = Tr("settings.privacy");
        NavAdvanced.Content = Tr("settings.advanced");
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
        _pages["privacy"] = Wrap(BuildPrivacyPage());
        _pages["advanced"] = Wrap(BuildAdvancedPage());
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

        // ---- 顶部: 城市 + 刷新 ----
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

        // ---- 当前天气: 图标 + 温度 ----
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

        // ---- 四项指标 ----
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

        // ---- 数据来源 ----
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

        // ---- 设定: 刷新间隔 + 单位 ----
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

        // ---- 可见性: 城市 / 经纬度 ----
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
                    Tr("settings.restart_required"), Tr("settings.reset_default_confirm")))
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
