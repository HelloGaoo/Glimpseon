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

// WeatherHourlyWidget
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

/// <summary>逐小时天气: 上区当前温度 / 中区温度曲线 / 下区时刻+图标</summary>
public sealed class WeatherHourlyWidget : WeatherWidgetBase
{
    private readonly TextBlock _cityText = new();
    private readonly TextBlock _tempText = new();
    private readonly Avalonia.Svg.Skia.Svg _currentIcon = MakeSvgIcon("2.svg", 60)!;
    private readonly TempCurve _curve = new();
    private readonly Grid _top = new();
    private readonly (TextBlock Time, Avalonia.Svg.Skia.Svg Icon)[] _slots = new (TextBlock, Avalonia.Svg.Skia.Svg)[6];

    public WeatherHourlyWidget(ComponentDefinition definition) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = WidgetSpace.GapXs,
        };

        // 上行: 左(城市+温度) 右(图标) 原版 AlignTop
        _top.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto");
        var left = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Top };
        _cityText.FontSize = WidgetType.Title;
        _tempText.FontSize = 52;
        _tempText.FontWeight = WidgetWeight.Display;
        left.Children.Add(_cityText);
        left.Children.Add(_tempText);
        _currentIcon.Width = 60;
        _currentIcon.Height = 60;
        _currentIcon.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(left, 0);
        Grid.SetColumn(_currentIcon, 1);
        _top.Children.Add(left);
        _top.Children.Add(_currentIcon);

        // 中区: 6 小时温度曲线 点温标替代槽内温度 给曲线留出高度
        _curve.DarkProvider = () => CardIsDark;
        _curve.MinHeight = 28;

        // 下区: 6 槽 时刻 + 天气图标
        var bottom = new Avalonia.Controls.Primitives.UniformGrid { Rows = 1, Columns = 6 };
        for (var i = 0; i < 6; i++)
        {
            var col = new StackPanel { Spacing = WidgetSpace.GapXs, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var time = new TextBlock { FontSize = WidgetType.Label, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var icon = MakeSvgIcon("2.svg", 28)!;
            col.Children.Add(time);
            col.Children.Add(icon);
            _slots[i] = (time, icon);
            bottom.Children.Add(col);
        }
        SizeChanged += (_, _) => Relayout();
        Config.ThemeColor.ValueChanged += _ => _curve.InvalidateVisual();

        Grid.SetRow(_top, 0);
        Grid.SetRow(_curve, 1);
        Grid.SetRow(bottom, 2);
        root.Children.Add(_top);
        root.Children.Add(_curve);
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
        foreach (var (time, _) in _slots)
        {
            time.Foreground = muted;
        }
        _curve.InvalidateVisual();
        Relayout();
    }

    // 上区 42% 高度 曲线吃剩余 下区图标随卡高
    private void Relayout()
    {
        var h = Bounds.Height - WidgetSpace.Pad * 2;
        var w = Bounds.Width - WidgetSpace.Pad * 2;
        if (h <= 1 || w <= 1)
        {
            return;
        }

        var topH = Math.Clamp(h * 0.46, 48, 116);
        _top.Height = topH;
        LeadIconPx = Math.Clamp(Math.Min(topH * 0.86, w * 0.2), 16, 88);
        SlotIconPx = CardText.ByHeight(h, 0.11, 14, 30);
        _currentIcon.Width = LeadIconPx;
        _currentIcon.Height = LeadIconPx;
        _tempText.FontSize = CardText.ByHeight(topH, 0.64, 20, 88);
        _cityText.FontSize = CardText.ByHeight(h, 0.075, 11, WidgetType.Title);

        var slotFont = CardText.ByHeight(h, 0.065, WidgetType.Micro, WidgetType.Title);
        foreach (var (time, icon) in _slots)
        {
            time.FontSize = slotFont;
            icon.Width = SlotIconPx;
            icon.Height = SlotIconPx;
        }
    }

    protected override void UpdateDisplay(JsonElement? cached)
    {
        var (temp, code) = ParseCurrent(cached);
        _cityText.Text = CityLabel();
        _tempText.Text = temp is "--" ? "--°" : $"{temp}°";
        var curSvg = MakeSvgIcon(cached is { ValueKind: JsonValueKind.Object } ? WeatherMaps.GetIcon(code) : null, LeadIconPx);
        _currentIcon.SvgSource = curSvg?.SvgSource;

        // 原版: parse_hourly(forecastHourly) 6 槽 小时=(pubTime.hour+i)%24
        WeatherHourly? parsed = null;
        if (cached is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("forecastHourly", out var fh))
        {
            parsed = WeatherService.ParseHourly(fh);
        }
        var hours = parsed?.Hours ?? new List<WeatherHour>();
        var startHour = 0;
        if (!string.IsNullOrEmpty(parsed?.PubTime) && DateTime.TryParse(parsed!.PubTime, out var pt))
        {
            startHour = pt.Hour;
        }
        var temps = new double?[6];
        for (var i = 0; i < 6; i++)
        {
            if (i < hours.Count)
            {
                var h = hours[i];
                _slots[i].Time.Text = $"{(startHour + i) % 24:00}:00";
                var svg = MakeSvgIcon(h.Icon, SlotIconPx);
                _slots[i].Icon.SvgSource = svg?.SvgSource;
                if (double.TryParse(h.Temp?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var tv))
                {
                    temps[i] = tv;
                }
            }
            else
            {
                _slots[i].Time.Text = "--:00";
                _slots[i].Icon.SvgSource = null;
            }
        }
        _curve.SetPoints(temps);
    }
}
