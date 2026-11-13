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

// WeatherWeeklyWidget
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

/// <summary>逐日天气</summary>
public sealed class WeatherWeeklyWidget : WeatherWidgetBase
{
    private readonly TextBlock _cityText = new();
    private readonly TextBlock _tempText = new();
    private readonly Avalonia.Svg.Skia.Svg _currentIcon = MakeSvgIcon("2.svg", 60)!;
    private readonly (TextBlock Day, Avalonia.Svg.Skia.Svg Icon, TextBlock Low, TextBlock High, TempRangeBar Bar)[] _rows =
        new (TextBlock, Avalonia.Svg.Skia.Svg, TextBlock, TextBlock, TempRangeBar)[4];

    // 星期名走 locale 不再硬编码

    public WeatherWeeklyWidget(ComponentDefinition definition) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.PadCompact),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto"),
            RowSpacing = WidgetSpace.GapXs,
        };

        var top = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto") };
        var left = new StackPanel { Spacing = WidgetSpace.GapXs, VerticalAlignment = VerticalAlignment.Top };
        _cityText.FontSize = WidgetType.Label;
        _tempText.FontSize = 42;
        _tempText.FontWeight = WidgetWeight.Display;
        _tempText.LetterSpacing = WidgetType.DisplaySpacing(42);
        left.Children.Add(_cityText);
        left.Children.Add(_tempText);
        _currentIcon.Width = 48;
        _currentIcon.Height = 48;
        _currentIcon.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(left, 0);
        Grid.SetColumn(_currentIcon, 1);
        top.Children.Add(left);
        top.Children.Add(_currentIcon);

        // 下行: day | icon | 区间条 | low | spacer | high 四行共享一条温度刻度
        var bottom = new Grid { RowSpacing = WidgetSpace.GapXs };
        bottom.RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,*,*,*");
        for (var i = 0; i < 4; i++)
        {
            var row = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,*,Auto,6,Auto") };
            var day = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var icon = MakeSvgIcon("2.svg", 18)!;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(WidgetSpace.GapSm, 0, WidgetSpace.GapSm, 0);
            var bar = new TempRangeBar { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(WidgetSpace.GapXs, 0, WidgetSpace.GapSm, 0) };
            var low = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var high = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(day, 0);
            Grid.SetColumn(icon, 1);
            Grid.SetColumn(bar, 2);
            Grid.SetColumn(low, 3);
            Grid.SetColumn(high, 5);
            row.Children.Add(day);
            row.Children.Add(icon);
            row.Children.Add(bar);
            row.Children.Add(low);
            row.Children.Add(high);
            Grid.SetRow(row, i);
            _rows[i] = (day, icon, low, high, bar);
            bottom.Children.Add(row);
        }
        SizeChanged += (_, _) => Relayout();
        Config.ThemeColor.ValueChanged += _ =>
        {
            foreach (var (_, _, _, _, bar) in _rows)
            {
                bar.InvalidateVisual();
            }
        };

        Grid.SetRow(top, 0);
        Grid.SetRow(bottom, 1);
        root.Children.Add(top);
        root.Children.Add(bottom);
        Card.Child = root;
        Content = Card;

        ApplyWeatherStyle();
        AttachCityClick(_cityText);
        RefreshFromCache();
    }

    protected override void ApplyWeatherStyle()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var ink = ConfiguredInk(Config.WeatherTextColor.Value) ?? p.Text;
        var muted = new SolidColorBrush(ink, CardInk.MutedOpacity);
        _cityText.Foreground = muted;
        _tempText.Foreground = new SolidColorBrush(ink);
        for (var i = 0; i < _rows.Length; i++)
        {
            var (day, _, low, high, bar) = _rows[i];
            day.Foreground = i == 0 ? new SolidColorBrush(ink) : muted;
            day.FontWeight = i == 0 ? WidgetWeight.Title : WidgetWeight.Body;
            low.Foreground = muted;
            high.Foreground = new SolidColorBrush(ink);
            bar.InvalidateVisual();
        }
        Relayout();
    }

    // 顶部区占 46% 其余 4 行均分 字号图标随行高
    private void Relayout()
    {
        var h = Bounds.Height - 24;
        var w = Bounds.Width - 24;
        if (h <= 1 || w <= 1)
        {
            return;
        }

        var topH = Math.Max(24, h * 0.46);
        _tempText.FontSize = Math.Clamp(Math.Round(Math.Min(topH * 0.66, w * 0.42), 1), 20, 48);
        _tempText.LetterSpacing = WidgetType.DisplaySpacing(_tempText.FontSize);
        _cityText.FontSize = WidgetType.Label;
        var curSize = Math.Max(16, _tempText.FontSize * 0.62);
        LeadIconPx = curSize;
        _currentIcon.Width = curSize;
        _currentIcon.Height = curSize;

        var rowH = Math.Max(10, (h - topH - WidgetSpace.GapXs) / 4);
        var rowFont = Math.Clamp(Math.Round(rowH * 0.48, 1), WidgetType.Micro, WidgetType.Label);
        var rowIcon = Math.Clamp(Math.Round(rowH * 0.78, 1), 12, 20);
        SlotIconPx = rowIcon;
        var tempSlot = CardText.Width("-00°", WidgetFonts.Num, FontWeight.Normal, rowFont);
        foreach (var (day, svg, low, high, bar) in _rows)
        {
            day.FontSize = rowFont;
            low.FontSize = rowFont;
            high.FontSize = rowFont;
            low.FontFamily = WidgetFonts.Num;
            high.FontFamily = WidgetFonts.Num;
            low.MinWidth = tempSlot;
            high.MinWidth = tempSlot;
            svg.Width = rowIcon;
            svg.Height = rowIcon;
            bar.Height = Math.Clamp(rowH * 0.30, 4, 6);
        }
    }

    private static bool TryNum(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, out value);

    protected override void UpdateDisplay(JsonElement? cached)
    {
        var (temp, code) = ParseCurrent(cached);
        _cityText.Text = CityLabel();
        _tempText.Text = temp is "--" ? "--°" : $"{temp}°";
        var curSvg = MakeSvgIcon(cached is { ValueKind: JsonValueKind.Object } ? WeatherMaps.GetIcon(code) : null, LeadIconPx);
        _currentIcon.SvgSource = curSvg?.SvgSource;

        WeatherDaily? parsed = null;
        if (cached is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("forecastDaily", out var fd))
        {
            parsed = WeatherService.ParseDaily(fd);
        }
        var days = parsed?.Days ?? new List<WeatherDay>();
        var now = DateTime.Now;

        // 四行共用刻度 1° 余量 温度全缺时不画条
        var lows = new double[4];
        var highs = new double[4];
        var has = new bool[4];
        var scaleMin = double.MaxValue;
        var scaleMax = double.MinValue;
        for (var i = 0; i < 4 && i < days.Count; i++)
        {
            var lowOk = TryNum(days[i].Low, out lows[i]);
            var highOk = TryNum(days[i].High, out highs[i]);
            has[i] = lowOk && highOk;
            if (has[i])
            {
                scaleMin = Math.Min(scaleMin, Math.Min(lows[i], highs[i]));
                scaleMax = Math.Max(scaleMax, Math.Max(lows[i], highs[i]));
            }
        }
        if (scaleMin > scaleMax)
        {
            scaleMin = 0;
            scaleMax = 1;
        }
        scaleMin -= 1;
        scaleMax += 1;

        for (var i = 0; i < 4; i++)
        {
            var row = _rows[i];
            if (i < days.Count)
            {
                var d = now.AddDays(i);
                row.Day.Text = i == 0 ? AppUtils.Tr("common.today") : AppUtils.Tr(AppUtils.WeekdayKey(d.DayOfWeek));
                var svg = MakeSvgIcon(days[i].Icon, SlotIconPx);
                row.Icon.SvgSource = svg?.SvgSource;
                row.Low.Text = $"{days[i].Low ?? "--"}°";
                row.High.Text = $"{days[i].High ?? "--"}°";
                row.Bar.IsVisible = has[i];
                if (has[i])
                {
                    row.Bar.Set(lows[i], highs[i], scaleMin, scaleMax);
                }
            }
            else
            {
                row.Day.Text = "--";
                row.Icon.SvgSource = null;
                row.Low.Text = "--";
                row.High.Text = "--°";
                row.Bar.IsVisible = false;
            }
        }
    }
}
