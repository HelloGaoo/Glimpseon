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

// SunCycleWidget
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

/// <summary>
/// 日照时刻
/// 400x200: 日出/日落与弧线
/// </summary>
public sealed class SunCycleWidget : WidgetCardBase
{
    private readonly TextBlock _title;
    private readonly TextBlock _riseLabel = new()
    {
        Text = "日出",
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly TextBlock _riseText = new()
    {
        Text = "--:--",
        FontSize = 20,
        FontWeight = WidgetWeight.Emphasis,
        FontFamily = WidgetFonts.Num,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly TextBlock _setLabel = new()
    {
        Text = "日落",
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
        HorizontalAlignment = HorizontalAlignment.Right,
    };
    private readonly TextBlock _setText = new()
    {
        Text = "--:--",
        FontSize = 20,
        FontWeight = WidgetWeight.Emphasis,
        FontFamily = WidgetFonts.Num,
        HorizontalAlignment = HorizontalAlignment.Right,
    };
    private readonly TextBlock _foot = new()
    {
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
    };
    private readonly TextBlock _nightText = new()
    {
        Text = "夜间",
        FontSize = WidgetType.Micro,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };
    private readonly SunArc _arc = new();
    private readonly DispatcherTimer _timer;

    public SunCycleWidget(ComponentDefinition definition) : base(definition)
    {
        _title = CardChrome.Title("日照时刻");

        var head = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
            ColumnSpacing = WidgetSpace.Gap,
        };
        head.Children.Add(_title);
        Grid.SetColumn(_foot, 1);
        head.Children.Add(_foot);

        var rise = new StackPanel
        {
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _riseLabel, _riseText },
        };
        var set = new StackPanel
        {
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _setLabel, _setText },
        };

        var arcHost = new Grid
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(WidgetSpace.GapSm, 0, WidgetSpace.GapSm, 0),
        };
        arcHost.Children.Add(_arc);
        arcHost.Children.Add(_nightText);

        var body = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
            ColumnSpacing = WidgetSpace.GapXs,
        };
        body.Children.Add(rise);
        Grid.SetColumn(arcHost, 1);
        body.Children.Add(arcHost);
        Grid.SetColumn(set, 2);
        body.Children.Add(set);

        var root = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.GapXs,
        };
        root.Children.Add(head);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Card.Child = root;
        Content = Card;

        ApplyPalette();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
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

    private void Refresh()
    {
        var (rise, set) = ReadTimes();
        var now = DateTime.Now;

        _riseText.Text = rise is { } r ? r.ToString("HH:mm") : "--:--";
        _setText.Text = set is { } s ? s.ToString("HH:mm") : "--:--";

        if (rise is { } riseTime && set is { } setTime && setTime > riseTime)
        {
            var isNight = now < riseTime || now > setTime;
            var total = (setTime - riseTime).TotalSeconds;
            var progress = now < riseTime
                ? 0
                : now > setTime
                    ? 1
                    : Math.Clamp((now - riseTime).TotalSeconds / total, 0, 1);
            _arc.SetProgress(progress);
            _nightText.IsVisible = isNight;

            var dayLength = $"昼长 {HoursMinutes(setTime - riseTime)}";
            var status = now < riseTime
                ? $"距日出 {HoursMinutes(riseTime - now)}"
                : now <= setTime
                    ? $"距日落 {HoursMinutes(setTime - now)}"
                    : $"距日出 {HoursMinutes(riseTime.AddDays(1) - now)}";
            _foot.Text = $"{dayLength} · {status}";
        }
        else
        {
            _arc.SetProgress(1);
            _nightText.IsVisible = false;
            _foot.Text = "暂无日照数据";
        }
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _title.Foreground = CardInk.Fg(p);
        _riseLabel.Foreground = CardInk.Muted(p);
        _riseText.Foreground = CardInk.Fg(p);
        _setLabel.Foreground = CardInk.Muted(p);
        _setText.Foreground = CardInk.Fg(p);
        _nightText.Foreground = CardInk.Faint(p);
        _foot.Foreground = CardInk.Muted(p);
        _arc.SetColors(p.Text, CardAccent.Value);
    }

    private static string HoursMinutes(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }
        return $"{(int)span.TotalHours} 小时 {span.Minutes} 分";
    }

    // 读取缓存
    private static (DateTime? Rise, DateTime? Set) ReadTimes()
    {
        var cached = AppUtils.GetCachedContent("weather", ignoreExpiry: true);
        if (cached is { ValueKind: JsonValueKind.Object } root
            && root.TryGetProperty("forecastDaily", out var daily)
            && daily.ValueKind == JsonValueKind.Object
            && daily.TryGetProperty("sunRiseSet", out var sun)
            && sun.ValueKind == JsonValueKind.Object
            && sun.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() > 0)
        {
            var first = value[0];
            if (first.ValueKind == JsonValueKind.Object)
            {
                var rise = ParseChildTime(first, "from");
                var set = ParseChildTime(first, "to");
                if (rise is not null || set is not null)
                {
                    return (rise, set);
                }
            }
            else if (first.ValueKind == JsonValueKind.Array && first.GetArrayLength() >= 2)
            {
                var rise = ParseTime(first[0]);
                var set = ParseTime(first[1]);
                if (rise is not null || set is not null)
                {
                    return (rise, set);
                }
            }
        }
        return SunTimes.Compute(DateTime.Today, Config.Latitude.Value, Config.Longitude.Value);
    }

    private static DateTime? ParseChildTime(JsonElement element, string key) =>
        element.TryGetProperty(key, out var child) ? ParseTime(child) : null;

    private static DateTime? ParseTime(JsonElement element)
    {
        var raw = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset))
        {
            return offset.ToLocalTime().DateTime;
        }
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) ? local : null;
    }

    /// <summary>日照弧</summary>
    private sealed class SunArc : Control
    {
        private double _progress = 1;
        private Color _line = Color.Parse("#8a8a8a");
        private Color _sun = Color.Parse("#f0a33a");

        public void SetProgress(double progress)
        {
            _progress = Math.Clamp(progress, 0, 1);
            InvalidateVisual();
        }

        public void SetColors(Color line, Color sun)
        {
            _line = Color.FromArgb(110, line.R, line.G, line.B);
            _sun = sun;
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 24 || h < 14)
            {
                return;
            }
            const double pad = 6;
            var radius = Math.Min(w / 2 - pad, h - pad);
            if (radius <= 4)
            {
                return;
            }
            var cx = w / 2;
            var cy = h - pad;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(cx - radius, cy), isFilled: false);
                g.ArcTo(new Point(cx + radius, cy), new Size(radius, radius), 0, false, SweepDirection.Clockwise, isStroked: true);
                g.EndFigure(false);
            }
            var pen = new Pen(new SolidColorBrush(_line), 1.6, null, PenLineCap.Round, PenLineJoin.Round);
            ctx.DrawGeometry(null, pen, geometry);

            var angle = Math.PI * (1 - _progress);
            var sunX = cx + radius * Math.Cos(angle);
            var sunY = cy - radius * Math.Sin(angle);
            ctx.DrawEllipse(new SolidColorBrush(_sun), null, new Point(sunX, sunY), 3.4, 3.4);
        }
    }
}
