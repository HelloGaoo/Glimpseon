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

// FlipClockWidget
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;

namespace Glimpseon.UI;

/// <summary>
/// 翻页时钟
/// 400x200: 四位数字 + 日期秒
/// </summary>
public sealed class FlipClockWidget : WidgetCardBase
{
    private const double Pad = 12;

    private static readonly string[] WeekdayNames =
        { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

    private readonly FlipCard[] _digits = new FlipCard[4];
    private readonly Grid _cardRow;
    private readonly Grid _colonHost = new();
    private readonly StackPanel _colonDots = new()
    {
        Orientation = Orientation.Vertical,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = 12,
    };
    private readonly Border _colonTop = new()
    {
        Width = 6,
        Height = 6,
        CornerRadius = new CornerRadius(3),
    };
    private readonly Border _colonBottom = new()
    {
        Width = 6,
        Height = 6,
        CornerRadius = new CornerRadius(3),
    };
    private readonly TextBlock _title = CardChrome.Title("本地时间");
    private readonly TextBlock _zone = new()
    {
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _date = new()
    {
        FontSize = WidgetType.Label,
        FontWeight = WidgetWeight.Body,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
    };
    private readonly TextBlock _seconds = new()
    {
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Emphasis,
        FontFamily = WidgetFonts.Num,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Grid _head;
    private readonly Grid _foot;
    private readonly bool _showSeconds;
    private readonly DispatcherTimer _timer;

    private bool _blinkOn = true;

    public FlipClockWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _showSeconds = ReadBool(config, "show_seconds", true);

        _head = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            ColumnSpacing = WidgetSpace.GapSm,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _head.Children.Add(_title);
        Grid.SetColumn(_zone, 1);
        _head.Children.Add(_zone);

        // 四列等分
        _cardRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,*,Auto,*,*"),
            ColumnSpacing = WidgetSpace.GapXs,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (var i = 0; i < 4; i++)
        {
            if (i == 2)
            {
                _colonDots.Children.Add(_colonTop);
                _colonDots.Children.Add(_colonBottom);
                _colonHost.Children.Add(_colonDots);
                _colonHost.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetColumn(_colonHost, 2);
                _cardRow.Children.Add(_colonHost);
            }
            var card = new FlipCard();
            _digits[i] = card;
            Grid.SetColumn(card, i < 2 ? i : i + 1);
            _cardRow.Children.Add(card);
        }

        _foot = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            ColumnSpacing = WidgetSpace.GapSm,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _foot.Children.Add(_date);
        Grid.SetColumn(_seconds, 1);
        _foot.Children.Add(_seconds);

        var root = new Grid
        {
            Margin = new Thickness(Pad),
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = WidgetSpace.GapXs,
        };
        root.Children.Add(_head);
        Grid.SetRow(_cardRow, 1);
        root.Children.Add(_cardRow);
        Grid.SetRow(_foot, 2);
        root.Children.Add(_foot);
        Card.Child = root;
        Content = Card;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_showSeconds ? 500 : 1000),
        };
        _timer.Tick += (_, _) => Tick();

        ApplyPalette();
        Tick();
        Relayout();

        SizeChanged += (_, _) => Relayout();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
        Tick();
        Relayout();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    // 刷新调色
    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var baseColor = p.BandB;
        var top = Common.ShiftColor(baseColor, CardIsDark ? 0.18 : 0.05);
        var bottom = Common.ShiftColor(baseColor, CardIsDark ? -0.18 : -0.07);
        var seam = Common.ShiftColor(baseColor, CardIsDark ? -0.68 : -0.20);
        foreach (var card in _digits)
        {
            card.SetPalette(top, baseColor, bottom, seam, p.Line);
            card.Text.Foreground = CardInk.Fg(p);
        }
        _colonTop.Background = CardAccent.Brush();
        _colonBottom.Background = CardAccent.Brush();
        _title.Foreground = CardInk.Fg(p);
        _zone.Foreground = CardInk.Faint(p);
        _date.Foreground = CardInk.Muted(p);
        _seconds.Foreground = CardInk.Muted(p);
    }

    // 重算尺寸
    private void Relayout()
    {
        var w = Bounds.Width - Pad * 2;
        var h = Bounds.Height - Pad * 2;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        // 100 高收起标题
        var showChrome = h >= 118;
        _head.IsVisible = showChrome;
        _foot.IsVisible = showChrome;
        _seconds.IsVisible = _showSeconds;

        var gap = WidgetSpace.GapXs;
        var headH = showChrome
            ? _title.FontSize * WidgetType.TightLineHeight + gap
            : 0;
        var footH = showChrome
            ? _date.FontSize * WidgetType.TightLineHeight + gap
            : 0;
        var availH = Math.Max(24, h - headH - footH);

        var colonW = Math.Clamp(availH * 0.16, 6, 30);
        var estCardW = Math.Max(8, (w - gap * 4 - colonW) / 4);
        // 对卡比例
        var cardH = Math.Min(availH, estCardW * 1.58);
        var digitSize = WidgetType.Display(estCardW * 0.92, cardH * 0.80, emWidth: 0.60, min: 14, max: 200);

        foreach (var card in _digits)
        {
            // 宽度随星列
            card.Width = double.NaN;
            card.Height = Math.Round(cardH, 1);
            card.Text.FontSize = digitSize;
            card.Text.LetterSpacing = WidgetType.DisplaySpacing(digitSize);
        }
        _colonHost.Width = Math.Round(colonW, 1);
        _colonHost.Height = Math.Round(cardH, 1);
        var dot = Math.Round(Math.Clamp(cardH * 0.055, 4, 9), 1);
        _colonTop.Width = dot;
        _colonTop.Height = dot;
        _colonTop.CornerRadius = new CornerRadius(dot / 2);
        _colonBottom.Width = dot;
        _colonBottom.Height = dot;
        _colonBottom.CornerRadius = new CornerRadius(dot / 2);
        _colonDots.Spacing = Math.Round(Math.Clamp(dot * 2.4, 6, 18), 1);
    }

    // 每秒刷新
    private void Tick()
    {
        var now = DateTime.Now;
        // 四位数字
        var hm = now.ToString("HHmm");
        for (var i = 0; i < 4; i++)
        {
            _digits[i].Text.Text = hm[i].ToString();
        }

        var offset = TimeZoneInfo.Local.GetUtcOffset(now);
        var abs = offset.Duration();
        _zone.Text = $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{abs.Hours:00}:{abs.Minutes:00}";
        _date.Text = $"{now:yyyy年M月d日} {WeekdayNames[(int)now.DayOfWeek]}";
        _seconds.Text = $"{now:ss} 秒";

        _blinkOn = !_blinkOn;
        _colonDots.Opacity = _blinkOn ? 1 : 0.28;
    }

    // 翻页对卡
    private sealed class FlipCard : Border
    {
        public TextBlock Text { get; } = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = WidgetFonts.Display,
            FontWeight = WidgetWeight.Body,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            IsHitTestVisible = false,
        };

        private readonly Border _top = new() { CornerRadius = new CornerRadius(9, 9, 0, 0) };
        private readonly Border _bottom = new() { CornerRadius = new CornerRadius(0, 0, 9, 9) };
        private readonly Border _seam = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        public FlipCard()
        {
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(10);
            ClipToBounds = true;
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetY = 2,
                Blur = 8,
                Color = Color.FromArgb(70, 0, 0, 0),
            });

            var host = new Grid { RowDefinitions = RowDefinitions.Parse("*,3,*") };
            host.Children.Add(_top);
            Grid.SetRow(_seam, 1);
            host.Children.Add(_seam);
            Grid.SetRow(_bottom, 2);
            host.Children.Add(_bottom);
            Grid.SetRowSpan(Text, 3);
            host.Children.Add(Text);
            Child = host;
        }

        public void SetPalette(Color top, Color baseColor, Color bottom, Color seam, Color line)
        {
            _top.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(top, 0),
                    new GradientStop(baseColor, 1),
                },
            };
            _bottom.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(baseColor, 0),
                    new GradientStop(bottom, 1),
                },
            };
            _seam.Background = new SolidColorBrush(seam);
            Background = new SolidColorBrush(seam);
            BorderBrush = new SolidColorBrush(line);
        }
    }
}
