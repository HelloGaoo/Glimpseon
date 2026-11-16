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

// DayProgressWidget
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;

namespace Glimpseon.UI;

/// <summary>
/// 今日进度
/// 400x100: 百分比与刻度条
/// </summary>
public sealed class DayProgressWidget : WidgetCardBase
{
    private const double BarHeight = 6;

    private readonly TextBlock _percent = new()
    {
        FontFamily = WidgetFonts.Num,
        FontWeight = WidgetWeight.Display,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Left,
        FontSize = 30,
    };
    private readonly MeterBar _bar = new();
    private readonly TextBlock[] _ticks = new TextBlock[5];
    private readonly TextBlock _foot = new()
    {
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
    };
    private readonly bool _showLeft;
    private readonly DispatcherTimer _timer;

    private double _value;

    public DayProgressWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _showLeft = ReadBool(config, "show_left", true);

        var title = CardChrome.Title("今日进度");
        var head = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
            ColumnSpacing = WidgetSpace.Gap,
        };
        head.Children.Add(title);
        Grid.SetColumn(_foot, 1);
        head.Children.Add(_foot);

        _bar.Height = BarHeight;

        // 刻度对齐
        var tickRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto,*,Auto,*,Auto,*,Auto"),
        };
        var labels = new[] { "0", "6", "12", "18", "24" };
        for (var i = 0; i < labels.Length; i++)
        {
            var tick = new TextBlock
            {
                Text = labels[i],
                FontSize = WidgetType.Micro,
                FontWeight = WidgetWeight.Body,
                HorizontalAlignment = i == 0
                    ? HorizontalAlignment.Left
                    : i == labels.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(tick, i * 2);
            tickRow.Children.Add(tick);
            _ticks[i] = tick;
        }

        var barSection = new StackPanel
        {
            Spacing = WidgetSpace.GapXs,
            VerticalAlignment = VerticalAlignment.Center,
        };
        barSection.Children.Add(_bar);
        barSection.Children.Add(tickRow);

        var body = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
            ColumnSpacing = WidgetSpace.PadCompact,
        };
        body.Children.Add(_percent);
        Grid.SetColumn(barSection, 1);
        body.Children.Add(barSection);

        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.PadCompact),
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.GapXs,
        };
        root.Children.Add(head);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Card.Child = root;
        Content = Card;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => UpdateProgress();

        UpdateProgress();
        Relayout();

        SizeChanged += (_, _) => Relayout();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
        UpdateProgress();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateProgress()
    {
        var now = DateTime.Now;
        var seconds = now.Hour * 3600 + now.Minute * 60 + now.Second + now.Millisecond / 1000.0;
        _value = Math.Clamp(seconds / 86400.0 * 100.0, 0, 100);
        // 向下取整
        _percent.Text = $"{(int)_value}%";

        var elapsedMinutes = (int)(seconds / 60);
        var passed = $"已过 {elapsedMinutes / 60:00}:{elapsedMinutes % 60:00}";

        var remainMinutes = Math.Max(0, 1440 - elapsedMinutes);
        var remain = $"剩 {remainMinutes / 60:00}:{remainMinutes % 60:00}";
        _foot.Text = _showLeft ? $"{passed} · {remain}" : passed;

        ApplyPalette();
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _percent.Foreground = CardInk.Fg(p);
        _foot.Foreground = CardInk.Muted(p);
        foreach (var tick in _ticks)
        {
            tick.Foreground = CardInk.Faint(p);
        }
        _bar.Set(_value, CardAccent.Value, Alpha(p.Line, 0.5));
    }

    private void Relayout()
    {
        var h = Bounds.Height - WidgetSpace.PadCompact * 2;
        if (h <= 1)
        {
            return;
        }
        var size = Math.Clamp(h * 0.42, 22, 34);
        _percent.FontSize = Math.Round(size, 1);
        _percent.LetterSpacing = WidgetType.DisplaySpacing(size);
    }

    private static Color Alpha(Color color, double alpha) => Color.FromArgb(
        (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);
}
