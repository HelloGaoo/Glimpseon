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

// AirQualityWidget
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;

namespace Glimpseon.UI;

/// <summary>
/// 空气指数
/// 400x200: 今日 AQI 与七日柱
/// </summary>
public sealed class AirQualityWidget : WidgetCardBase
{
    private static readonly Color ColGood = Color.Parse("#36b37e");
    private static readonly Color ColModerate = Color.Parse("#9acd32");
    private static readonly Color ColLight = Color.Parse("#f0a33a");
    private static readonly Color ColMedium = Color.Parse("#e5484d");
    private static readonly Color ColHeavy = Color.Parse("#8e44ad");
    private static readonly Color ColSevere = Color.Parse("#6d1b1b");
    private static readonly Color ColNoData = Color.Parse("#8a8a8a");

    private static readonly string WeekdayNames = "日一二三四五六";

    private readonly TextBlock _title;
    private readonly TextBlock _big = new()
    {
        FontSize = 30,
        FontWeight = WidgetWeight.Display,
        FontFamily = WidgetFonts.Num,
        VerticalAlignment = VerticalAlignment.Bottom,
    };
    private readonly TextBlock _level = new()
    {
        FontSize = WidgetType.Label,
        FontWeight = WidgetWeight.Emphasis,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 4),
    };
    private readonly StackPanel _hero = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = WidgetSpace.GapSm,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Grid _bars = new() { ColumnDefinitions = ColumnDefinitions.Parse("*,*,*,*,*,*,*") };
    private readonly Border[] _barBodies = new Border[7];
    private readonly TextBlock[] _barLabels = new TextBlock[7];
    private readonly TextBlock _empty = new()
    {
        Text = "暂无空气质量数据",
        FontSize = WidgetType.Label,
        FontWeight = WidgetWeight.Body,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };
    private readonly List<int> _values = new();
    private readonly DispatcherTimer _timer;

    public AirQualityWidget(ComponentDefinition definition) : base(definition)
    {
        _title = CardChrome.Title("空气指数");
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.MaxLines = 1;

        _hero.Children.Add(_big);
        _hero.Children.Add(_level);

        BuildBars();

        var body = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
            ColumnSpacing = WidgetSpace.PadCompact,
        };
        body.Children.Add(_hero);
        Grid.SetColumn(_bars, 1);
        body.Children.Add(_bars);

        var root = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.GapXs,
        };
        root.Children.Add(_title);
        Grid.SetRow(body, 1);
        Grid.SetRow(_empty, 1);
        root.Children.Add(body);
        root.Children.Add(_empty);
        Card.Child = root;
        Content = Card;

        ApplyPalette();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
        _bars.SizeChanged += (_, _) => LayoutBars();

        // 只读天气缓存
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => Refresh();
        Refresh();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void BuildBars()
    {
        for (var i = 0; i < 7; i++)
        {
            var cell = new Grid { RowDefinitions = RowDefinitions.Parse("*,Auto") };
            var bar = new Border
            {
                Width = 6,
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(ColNoData),
            };
            var label = new TextBlock
            {
                FontSize = 9.5,
                FontWeight = WidgetWeight.Body,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
            };
            Grid.SetRow(bar, 0);
            Grid.SetRow(label, 1);
            cell.Children.Add(bar);
            cell.Children.Add(label);
            Grid.SetColumn(cell, i);
            _bars.Children.Add(cell);
            _barBodies[i] = bar;
            _barLabels[i] = label;
        }
    }

    private void Refresh()
    {
        var cached = AppUtils.GetCachedContent("weather", ignoreExpiry: true);
        _values.Clear();
        _values.AddRange(ReadAqi(cached));

        var has = _values.Count > 0;
        _hero.IsVisible = has;
        _bars.IsVisible = has;
        _empty.IsVisible = !has;

        if (has)
        {
            var today = Math.Max(0, _values[0]);
            _big.Text = today.ToString(CultureInfo.InvariantCulture);
            _level.Text = LevelWord(today);
        }
        else
        {
            _big.Text = "--";
            _level.Text = "";
        }

        for (var i = 0; i < 7; i++)
        {
            var show = i < _values.Count;
            _barBodies[i].IsVisible = show;
            _barLabels[i].IsVisible = show;
            _barLabels[i].Text = WeekLabel(i);
        }
        UpdateColors();
        LayoutBars();
    }

    private void LayoutBars()
    {
        var w = _bars.Bounds.Width;
        var h = _bars.Bounds.Height;
        if (w <= 1 || h <= 1 || _values.Count == 0)
        {
            return;
        }
        var barArea = Math.Max(4, h - 9.5 * WidgetType.TightLineHeight - WidgetSpace.GapXs);
        var max = Math.Max(1, _values.Take(7).Max());
        var barWidth = Math.Clamp(w / 7 * 0.30, 4, 8);
        for (var i = 0; i < 7 && i < _values.Count; i++)
        {
            var ratio = Math.Clamp(_values[i] / (double)max, 0.10, 1);
            _barBodies[i].Height = Math.Max(3, barArea * ratio);
            _barBodies[i].Width = barWidth;
        }
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _title.Foreground = CardInk.Fg(p);
        _empty.Foreground = CardInk.Muted(p);
        foreach (var label in _barLabels)
        {
            label.Foreground = CardInk.Faint(p);
        }
        UpdateColors();
    }

    private void UpdateColors()
    {
        var color = _values.Count > 0 ? LevelColor(_values[0]) : ColNoData;
        _big.Foreground = new SolidColorBrush(color);
        _level.Foreground = new SolidColorBrush(color);
        for (var i = 0; i < 7; i++)
        {
            _barBodies[i].Background = new SolidColorBrush(i < _values.Count ? LevelColor(_values[i]) : ColNoData);
        }
    }

    private static string WeekLabel(int index) =>
        WeekdayNames[(int)DateTime.Today.AddDays(index).DayOfWeek].ToString();

    private static string LevelWord(int value) => value switch
    {
        <= 50 => "优",
        <= 100 => "良",
        <= 150 => "轻度污染",
        <= 200 => "中度污染",
        <= 300 => "重度污染",
        _ => "严重污染",
    };

    private static Color LevelColor(int value) => value switch
    {
        <= 50 => ColGood,
        <= 100 => ColModerate,
        <= 150 => ColLight,
        <= 200 => ColMedium,
        <= 300 => ColHeavy,
        _ => ColSevere,
    };

    private static List<int> ReadAqi(JsonElement? cached)
    {
        var list = new List<int>();
        if (cached is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("forecastDaily", out var daily)
            || daily.ValueKind != JsonValueKind.Object
            || !daily.TryGetProperty("aqi", out var aqi)
            || aqi.ValueKind != JsonValueKind.Object
            || !aqi.TryGetProperty("value", out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return list;
        }
        foreach (var item in value.EnumerateArray())
        {
            if (TryNumber(item, out var number))
            {
                list.Add(Math.Max(0, (int)Math.Round(number)));
            }
        }
        return list;
    }

    private static bool TryNumber(JsonElement element, out double value)
    {
        value = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out value);
            case JsonValueKind.String:
                return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            case JsonValueKind.Array:
                foreach (var inner in element.EnumerateArray())
                {
                    if (TryNumber(inner, out value))
                    {
                        return true;
                    }
                }
                return false;
            case JsonValueKind.Object:
                foreach (var key in new[] { "value", "from", "to" })
                {
                    if (element.TryGetProperty(key, out var inner) && TryNumber(inner, out value))
                    {
                        return true;
                    }
                }
                return false;
            default:
                return false;
        }
    }
}
