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

// WeatherCurrentWidget
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Media.Imaging;
using IOPath = System.IO.Path;
using Avalonia.Controls.Shapes;
using System.Text.RegularExpressions;
using Avalonia.Animation;
using Avalonia.Threading;
using Glimpseon.Core.Services;
using Markdown.Avalonia;
using Avalonia.Input.Platform;
using System.Runtime.InteropServices;
using AvColor = Avalonia.Media.Color;
using System.Globalization;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Utilities;
using Avalonia.VisualTree;


namespace Glimpseon.UI;

/// <summary>极简天气</summary>
public sealed class WeatherCurrentWidget : WeatherWidgetBase
{
    private readonly Avalonia.Svg.Skia.Svg _icon = MakeSvgIcon("2.svg", 64)!;
    private readonly bool _showIcon = true;
    private readonly TextBlock _cityText = new();
    private readonly TextBlock _conditionText = new();
    private readonly TextBlock _tempText = new();
    private readonly TextBlock _rangeText = new();
    private readonly TextBlock _detailText = new();
    private readonly Grid _hero = new();

    public WeatherCurrentWidget(ComponentDefinition definition, JsonElement? config = null) : base(definition)
    {
        _showIcon = ReadBool(config, "show_icon", true);
        // 200x200 卡片: 城市/天气在上, 大温度+图标居中, 高低温/体感/湿度在底
        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = WidgetSpace.GapXs,
        };

        // 顶行: 城市; 天气现象放到温度下方 单字在小字号下发糊
        var top = new Grid();
        _cityText.FontSize = WidgetType.Label;
        _cityText.VerticalAlignment = VerticalAlignment.Center;
        _cityText.TextTrimming = TextTrimming.CharacterEllipsis;
        _cityText.MaxLines = 1;
        top.Children.Add(_cityText);

        _conditionText.FontSize = WidgetType.Body;
        _conditionText.FontWeight = WidgetWeight.Emphasis;
        _conditionText.VerticalAlignment = VerticalAlignment.Center;
        _conditionText.TextTrimming = TextTrimming.CharacterEllipsis;
        _conditionText.MaxLines = 1;
        _conditionText.Margin = new Thickness(1, 0, 0, 2);

        // 主区: 温度 + 天气现象 + 天气图标
        _hero.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto");
        _hero.ColumnSpacing = 8;
        _tempText.VerticalAlignment = VerticalAlignment.Center;
        _tempText.HorizontalAlignment = HorizontalAlignment.Left;
        _tempText.TextAlignment = TextAlignment.Left;
        _tempText.FontWeight = WidgetWeight.Display;
        var tempStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        tempStack.Children.Add(_tempText);
        tempStack.Children.Add(_conditionText);
        _icon.VerticalAlignment = VerticalAlignment.Center;
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(tempStack, 0);
        Grid.SetColumn(_icon, 1);
        _hero.Children.Add(tempStack);
        _hero.Children.Add(_icon);
        _icon.IsVisible = _showIcon;

        // 底行: 最高/最低 + 体感/湿度
        var bottom = new StackPanel { Spacing = WidgetSpace.GapXs, VerticalAlignment = VerticalAlignment.Bottom };
        _rangeText.FontSize = WidgetType.Label;
        _rangeText.TextTrimming = TextTrimming.CharacterEllipsis;
        _rangeText.MaxLines = 1;
        _detailText.FontSize = WidgetType.Micro;
        _detailText.TextTrimming = TextTrimming.CharacterEllipsis;
        _detailText.MaxLines = 1;
        bottom.Children.Add(_rangeText);
        bottom.Children.Add(_detailText);

        Grid.SetRow(top, 0);
        Grid.SetRow(_hero, 1);
        Grid.SetRow(bottom, 2);
        root.Children.Add(top);
        root.Children.Add(_hero);
        root.Children.Add(bottom);
        Card.Child = root;
        Content = Card;

        ApplyWeatherStyle();
        AttachCityClick(_cityText);
        Config.WeatherSize.ValueChanged += _ => ApplyWeatherStyle();
        Config.WeatherIconSize.ValueChanged += _ => Relayout();
        Config.ThemeColor.ValueChanged += _ => _icon.InvalidateVisual();
        SizeChanged += (_, _) => Relayout();
        Relayout();
        RefreshFromCache();
    }

    private void Relayout()
    {
        var h = Bounds.Height - 28;
        var w = Bounds.Width - 32;
        if (h <= 1 || w <= 1)
        {
            return;
        }

        // 主区吃掉剩余高度 温度与图标按主区归一
        var heroH = Math.Clamp(h * 0.56, 44, 112);
        _hero.Height = heroH;
        LeadIconPx = Math.Clamp(Math.Min(heroH * 0.78, w * 0.34), 20, 80);
        _icon.Width = LeadIconPx;
        _icon.Height = LeadIconPx;
        // WeatherSize 作比例系数 默认 24=1.0
        var temp = Math.Min(heroH * 0.76 * Config.WeatherSize.Value / 24.0, w * 0.52);
        _tempText.FontSize = Math.Clamp(Math.Round(temp, 1), 22, 96);
        _tempText.LetterSpacing = WidgetType.DisplaySpacing(_tempText.FontSize);
        _cityText.FontSize = WidgetType.Label;
        _conditionText.FontSize = WidgetType.Body;
        _rangeText.FontSize = WidgetType.Label;
        _detailText.FontSize = WidgetType.Micro;
    }

    protected override void ApplyWeatherStyle()
    {
        // 自定义色优先 兜底走色板 浅色卡防白字
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var ink = ConfiguredInk(Config.WeatherTextColor.Value) ?? p.Text;
        var muted = new SolidColorBrush(ink, CardInk.MutedOpacity);
        var faint = new SolidColorBrush(ink, CardInk.FaintOpacity);
        _cityText.Foreground = muted;
        _conditionText.Foreground = muted;
        _tempText.Foreground = new SolidColorBrush(ink);
        _rangeText.Foreground = muted;
        _detailText.Foreground = faint;
        // 字号只在 Relayout 定 避免双头赋值互踩
        Relayout();
    }

    protected override void UpdateDisplay(JsonElement? cached)
    {
        var (temp, code) = ParseCurrent(cached);
        var hasData = cached is { ValueKind: JsonValueKind.Object };
        _cityText.Text = CityLabel();
        _tempText.Text = temp is "--" ? "--°" : $"{temp}°";
        _conditionText.Text = hasData ? WeatherMaps.GetWeatherText(code) : "";
        _conditionText.IsVisible = _conditionText.Text.Length > 0;

        var (high, low) = ReadDailyRange(cached);
        _rangeText.Text = high == "--" && low == "--"
            ? ""
            : $"{AppUtils.Tr("weather.high")} {high}°   {AppUtils.Tr("weather.low")} {low}°";
        _rangeText.IsVisible = _rangeText.Text.Length > 0;

        var feels = ReadCurrentMetric(cached, "feelsLike");
        var humidity = ReadCurrentMetric(cached, "humidity");
        var details = new List<string>(2);
        if (feels != "--")
        {
            details.Add($"{AppUtils.Tr("settings.weather_feels")} {feels}°");
        }
        if (humidity != "--")
        {
            details.Add($"{AppUtils.Tr("settings.weather_humidity")} {humidity}%");
        }
        _detailText.Text = string.Join(" · ", details);
        _detailText.IsVisible = _detailText.Text.Length > 0;

        var icon = hasData ? WeatherMaps.GetIcon(code) : null;
        var svg = MakeSvgIcon(icon, LeadIconPx);
        _icon.SvgSource = svg?.SvgSource;
        _icon.Width = svg?.Width ?? 0;
        _icon.Height = svg?.Height ?? 0;
    }

    private static (string High, string Low) ReadDailyRange(JsonElement? cached)
    {
        if (cached is { ValueKind: JsonValueKind.Object } root
            && root.TryGetProperty("forecastDaily", out var daily)
            && WeatherService.ParseDaily(daily) is { Days.Count: > 0 } parsed)
        {
            return (parsed.Days[0].High, parsed.Days[0].Low);
        }
        return ("--", "--");
    }

    private static string ReadCurrentMetric(JsonElement? cached, string name)
    {
        if (cached is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("current", out var current)
            || !current.TryGetProperty(name, out var metric)
            || !metric.TryGetProperty("value", out var value))
        {
            return "--";
        }
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? "--" : text;
    }
}
