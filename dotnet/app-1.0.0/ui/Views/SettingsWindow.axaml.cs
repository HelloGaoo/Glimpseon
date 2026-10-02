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
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Glimpseon.Core;

namespace Glimpseon.UI.Views;

public partial class SettingsWindow : GlimpseonWindow
{
    private readonly Dictionary<string, Control> _pages = new();
    private FAFrame? _navFrame;

    public SettingsWindow()
    {
        InitializeComponent();
        Title = AppUtils.Tr("settings.title");

        try
        {
            var iconPath = Paths.GetResourcePath(Constants.AppIcon);
            if (File.Exists(iconPath))
            {
                WindowIcon.Source = new Bitmap(iconPath);
            }
        }
        catch (Exception)
        {
            // 图标缺失 留空
        }
        VersionText.Text = Paths.Version;

        BuildPages();
        _navFrame = new FAFrame { IsNavigationStackEnabled = false, NavigationPageFactory = PassThroughNavigationPageFactory.Instance };
        Nav.Content = _navFrame;
        _navFrame.Loaded += (_, _) => Nav.SelectedItem = NavGeneral;
    }

    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is FANavigationViewItem item && item.Tag is string tag && _navFrame is { IsLoaded: true } && _pages.TryGetValue(tag, out var page))
        {
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
                new FASymbolIcon { Symbol = symbol, FontSize = 16, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }

    private static FASettingsExpander Card(FASymbol icon, string header, string? description, Control? footer)
    {
        var card = new FASettingsExpander
        {
            IconSource = new FASymbolIconSource { Symbol = icon },
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

    private static FASettingsExpander CardBind(FASymbol icon, string header, string description, ConfigItem<bool> item)
    {
        var toggle = new ToggleSwitch { IsChecked = item.Value };
        toggle.IsCheckedChanged += (_, _) => item.Value = toggle.IsChecked == true;
        return Card(icon, header, description, toggle);
    }

    private static FASettingsExpander CardSlider(FASymbol icon, string header, ConfigItem<int> item, double min, double max)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Width = 260 };
        var value = new TextBlock { Text = item.Value.ToString(), VerticalAlignment = VerticalAlignment.Center, MinWidth = 40, TextAlignment = TextAlignment.Right, Opacity = 0.75 };
        slider.Value = item.Value;
        slider.ValueChanged += (_, e) =>
        {
            value.Text = ((int)e.NewValue).ToString();
            item.Value = (int)e.NewValue;
        };
        return Card(icon, header, null, new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { slider, value },
        });
    }

    private static FASettingsExpander CardCombo(FASymbol icon, string header, string description, ComboBox combo)
    {
        return Card(icon, header, description, combo);
    }

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

    // 各子页

    private void BuildPages()
    {
        _pages["general"] = Wrap(BuildGeneralPage());
        _pages["time"] = Wrap(BuildTimePage());
        _pages["weather"] = Wrap(BuildWeatherPage());
        _pages["appearance"] = Wrap(BuildAppearancePage());
        _pages["log"] = Wrap(BuildLogPage());
        _pages["advanced"] = Wrap(BuildAdvancedPage());
        _pages["grid"] = Wrap(BuildGridPage());
    }

    private Control BuildGeneralPage()
    {
        var stack = MakePageStack(AppUtils.Tr("settings.subpage_general"));
        stack.Children.Add(SectionHeader("行为", FASymbol.Setting));
        stack.Children.Add(CardBind(FASymbol.Switch, "开机自启动", "在您的系统启动时自动运行本应用", Config.AutoStart));
        stack.Children.Add(CardBind(FASymbol.View, "空闲时自动打开", "电脑空闲时自动从最小化打开界面", Config.AutoOpenOnIdle));
        stack.Children.Add(CardBind(FASymbol.FullScreenMaximize, "自动打开时最大化", "空闲自动打开界面时是否最大化窗口", Config.AutoOpenMaximize));
        stack.Children.Add(CardSlider(FASymbol.Clock, "空闲判定时间 (分钟)", Config.IdleMinutes, 1, 60));

        stack.Children.Add(SectionHeader("窗口", FASymbol.OpenWith));
        var closeCombo = new ComboBox { MinWidth = 140 };
        closeCombo.Items.Add("minimize");
        closeCombo.Items.Add("close");
        closeCombo.SelectedIndex = Config.CloseAction.Value == "minimize" ? 0 : 1;
        closeCombo.SelectionChanged += (_, _) => Config.CloseAction.Value = closeCombo.SelectedIndex == 0 ? "minimize" : "close";
        stack.Children.Add(CardCombo(FASymbol.Dismiss, "关闭按钮行为", "点击窗口关闭按钮时的动作", closeCombo));
        stack.Children.Add(CardBind(FASymbol.AllApps, "允许多开", "允许同时运行多个应用实例", Config.AllowMultipleInstances));
        stack.Children.Add(CardBind(FASymbol.Code, "调试模式", "显示调试导航页与更短日志保留期", Config.DebugMode));
        return stack;
    }

    private Control BuildTimePage()
    {
        var stack = MakePageStack(AppUtils.Tr("settings.subpage_time"));
        stack.Children.Add(SectionHeader("时钟", FASymbol.Clock));
        stack.Children.Add(CardBind(FASymbol.Clock, "显示时钟", "", Config.ShowClock));
        stack.Children.Add(CardBind(FASymbol.Clock, "显示秒", "", Config.ShowClockSeconds));
        stack.Children.Add(CardBind(FASymbol.Calendar, "显示农历", "", Config.ShowLunarCalendar));
        stack.Children.Add(SectionHeader("校准", FASymbol.Sync));
        stack.Children.Add(CardSlider(FASymbol.Sync, "手动时间偏移 (秒)", Config.TimeOffset, -9999, 9999));
        stack.Children.Add(CardBind(FASymbol.Sync, "自动时间偏移", "每天自动累加偏移量", Config.AutoTimeOffsetEnabled));
        stack.Children.Add(CardBind(FASymbol.Globe, "NTP 精确时间", "从时间服务器校准系统时间偏差", Config.UsePreciseTime));
        return stack;
    }

    private Control BuildWeatherPage()
    {
        var stack = MakePageStack(AppUtils.Tr("settings.subpage_weather"));
        stack.Children.Add(SectionHeader("城市", FASymbol.MapPin));
        var cityEdit = new TextBox { Text = Config.City.Value, MinWidth = 200, Watermark = "城市名称" };
        cityEdit.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Config.City.Value = cityEdit.Text ?? "";
            }
        };
        stack.Children.Add(Card(FASymbol.MapPin, "天气城市", "用于获取天气数据", cityEdit));
        stack.Children.Add(SectionHeader("来源", FASymbol.Cloud));
        var unitCombo = new ComboBox { MinWidth = 120 };
        unitCombo.Items.Add("c");
        unitCombo.Items.Add("f");
        unitCombo.SelectedIndex = Config.WeatherUnit.Value == "c" ? 0 : 1;
        unitCombo.SelectionChanged += (_, _) => Config.WeatherUnit.Value = unitCombo.SelectedIndex == 0 ? "c" : "f";
        stack.Children.Add(CardCombo(FASymbol.WeatherSunny, "温度单位", "", unitCombo));
        stack.Children.Add(new FAInfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = FAInfoBarSeverity.Informational,
            Message = "天气服务内核随 services/weather 移植接入",
        });
        return stack;
    }

    private Control BuildAppearancePage()
    {
        var stack = MakePageStack(AppUtils.Tr("settings.subpage_appearance"));
        stack.Children.Add(SectionHeader("主题", FASymbol.DarkTheme));
        var themeCombo = new ComboBox { MinWidth = 160 };
        themeCombo.Items.Add(AppUtils.Tr("wizard.theme_light"));
        themeCombo.Items.Add(AppUtils.Tr("wizard.theme_dark"));
        themeCombo.Items.Add(AppUtils.Tr("wizard.theme_system"));
        themeCombo.SelectedIndex = Config.ThemeMode.Value switch
        {
            ThemeMode.Light => 0,
            ThemeMode.Dark => 1,
            _ => 2,
        };
        themeCombo.SelectionChanged += (_, _) =>
        {
            Config.ThemeMode.Value = themeCombo.SelectedIndex switch
            {
                0 => ThemeMode.Light,
                1 => ThemeMode.Dark,
                _ => ThemeMode.Auto,
            };
        };
        stack.Children.Add(CardCombo(FASymbol.DarkTheme, "应用颜色主题", "更改应用程序的颜色外观", themeCombo));
        stack.Children.Add(SectionHeader("背景", FASymbol.Image));
        stack.Children.Add(CardSlider(FASymbol.Filter, "背景模糊半径", Config.BackgroundBlurRadius, 0, 30));
        stack.Children.Add(CardSlider(FASymbol.Image, "壁纸亮度", Config.WallpaperBrightness, -100, 0));
        return stack;
    }

    private Control BuildLogPage()
    {
        var stack = MakePageStack(AppUtils.Tr("settings.subpage_log"));
        stack.Children.Add(SectionHeader("日志", FASymbol.Page));
        var levelCombo = new ComboBox { MinWidth = 140 };
        foreach (var name in new[] { "Debug", "Info", "Warning", "Error" })
        {
            levelCombo.Items.Add(name);
        }
        levelCombo.SelectedIndex = (int)Config.LogVerbosity.Value;
        levelCombo.SelectionChanged += (_, _) => Config.LogVerbosity.Value = (LogVerbosity)levelCombo.SelectedIndex;
        stack.Children.Add(CardCombo(FASymbol.Page, "日志级别", "", levelCombo));
        stack.Children.Add(CardSlider(FASymbol.Document, "保留条数", Config.LogMaxCount, 10, 500));
        stack.Children.Add(CardSlider(FASymbol.Clock, "保留天数", Config.LogMaxDays, 30, 365));
        stack.Children.Add(CardBind(FASymbol.Dismiss, "禁用日志", "", Config.DisableLog));
        return stack;
    }

    private Control BuildAdvancedPage()
    {
        var stack = MakePageStack(AppUtils.Tr("settings.subpage_advanced"));
        stack.Children.Add(SectionHeader("性能", FASymbol.Clock));
        stack.Children.Add(CardBind(FASymbol.Globe, "GPU 加速", "", Config.EnableGpuAcceleration));
        stack.Children.Add(SectionHeader("更新", FASymbol.CloudDownload));
        stack.Children.Add(CardBind(FASymbol.Sync, "自动检查更新", "", Config.AutoCheckUpdate));
        stack.Children.Add(CardBind(FASymbol.CloudDownload, "自动更新", "", Config.AutoUpdate));
        return stack;
    }

    private Control BuildGridPage()
    {
        var stack = MakePageStack(AppUtils.Tr("settings.subpage_grid"));
        stack.Children.Add(SectionHeader("网格", FASymbol.List));
        stack.Children.Add(CardSlider(FASymbol.AllApps, "短边格数", Config.GridShortSideCells, 6, 96));
        stack.Children.Add(CardSlider(FASymbol.List, "边距百分比", Config.GridInsetPercent, 0, 30));
        stack.Children.Add(SectionHeader("组件卡片", FASymbol.Bullets));
        stack.Children.Add(CardSlider(FASymbol.Bullets, "卡片不透明度 (%)", Config.ComponentCardOpacity, 0, 100));
        stack.Children.Add(CardSlider(FASymbol.Bullets, "卡片圆角 (px)", Config.ComponentCardRadius, 0, 29));
        return stack;
    }
}
