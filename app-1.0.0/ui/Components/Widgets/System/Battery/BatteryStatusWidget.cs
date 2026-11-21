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

// BatteryStatusWidget
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

/// <summary>
/// 电池状态
/// 200x200: 环形电量与状态
/// 30 秒轮询
/// </summary>
public sealed class BatteryStatusWidget : WidgetCardBase
{
    private readonly ArcMeter _arc = new();
    private readonly TextBlock _percentText = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        FontSize = 19,
        FontWeight = WidgetWeight.Emphasis,
    };
    private readonly TextBlock _icon = AppIcons.Create(AppIcons.Flash, 24);
    private readonly TextBlock _title;
    private readonly TextBlock _state = new()
    {
        FontSize = WidgetType.Label,
        FontWeight = WidgetWeight.Emphasis,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
    };
    private readonly TextBlock _detail = new()
    {
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
        TextWrapping = TextWrapping.Wrap,
        MaxLines = 2,
    };
    private readonly DispatcherTimer _timer;

    private int _percent;
    private bool _present;
    private bool _charging;
    private bool _known;

    public BatteryStatusWidget(ComponentDefinition definition) : base(definition)
    {
        _title = CardChrome.Title("电池状态");
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _icon.VerticalAlignment = VerticalAlignment.Center;

        var arcHost = new Grid
        {
            Width = 84,
            Height = 84,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        arcHost.Children.Add(_arc);
        arcHost.Children.Add(_percentText);
        arcHost.Children.Add(_icon);

        var info = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _state, _detail },
        };

        var body = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
            ColumnSpacing = WidgetSpace.Gap,
        };
        body.Children.Add(arcHost);
        Grid.SetColumn(info, 1);
        body.Children.Add(info);

        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.GapXs,
            Margin = new Thickness(10),
        };
        root.Children.Add(_title);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Card.Child = root;
        Content = Card;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Refresh();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
        Refresh();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!PreviewSamples.Active)
        {
            _timer.Start();
        }
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        var snap = BatteryService.Read();
        _present = snap.Present;
        _charging = snap.Charging;
        _known = snap.Percent >= 0;
        _percent = _known ? Math.Clamp(snap.Percent, 0, 100) : 0;

        if (!_present)
        {
            _state.Text = "未检测到电池";
            _detail.Text = snap.OnAc ? "使用交流电源" : "";
            _percentText.Text = "--";
        }
        else
        {
            _state.Text = _charging ? "充电中" : snap.OnAc ? "已接通电源" : "电池供电";
            _detail.Text = _charging
                ? "接通电源"
                : snap.MinutesLeft >= 0
                    ? TimeText(snap.MinutesLeft)
                    : snap.OnAc ? "接通电源" : "剩余时间未知";
            _percentText.Text = _known ? $"{_percent}%" : "--";
        }
        ApplyPalette();
    }

    private static string TimeText(int minutes) => minutes >= 60
        ? $"剩余 {minutes / 60} 小时 {minutes % 60} 分"
        : $"剩余 {minutes} 分钟";

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var track = Color.FromArgb(38, p.Text.R, p.Text.G, p.Text.B);
        var fill = !_present
            ? Color.FromArgb(90, p.Text.R, p.Text.G, p.Text.B)
            : _known && _percent <= 20
                ? CardInk.Danger().Color
                : _charging ? CardAccent.Value : p.Text;

        _arc.Set(_present && _known ? _percent : 0, fill, track, 5);
        _percentText.IsVisible = _present;
        _percentText.Foreground = new SolidColorBrush(fill);
        _icon.IsVisible = !_present;
        _icon.Foreground = new SolidColorBrush(p.Text, 0.32);
        _title.Foreground = CardInk.Fg(p);
        _state.Foreground = !_present
            ? CardInk.Muted(p)
            : _known && _percent <= 20
                ? CardInk.Danger()
                : _charging ? CardAccent.Brush() : CardInk.Fg(p);
        _detail.Foreground = CardInk.Muted(p);
    }
}
