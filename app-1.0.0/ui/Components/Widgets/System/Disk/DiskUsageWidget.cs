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

// DiskUsageWidget
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

/// <summary>
/// 磁盘容量
/// 400x200: 分区列表
/// 60 秒刷新
/// </summary>
public sealed class DiskUsageWidget : WidgetCardBase
{
    private readonly StackPanel _rows = new() { Spacing = 6 };
    private readonly List<DriveUsage> _drives = new();
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _count = new()
    {
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public DiskUsageWidget(ComponentDefinition definition) : base(definition)
    {
        var top = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        top.Children.Add(CardChrome.Title("磁盘容量"));
        Grid.SetColumn(_count, 1);
        top.Children.Add(_count);

        var scroll = new ScrollViewer
        {
            Content = _rows,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };

        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.GapSm,
            Margin = new Thickness(WidgetSpace.PadCompact),
        };
        root.Children.Add(top);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Card.Child = root;
        Content = Card;

        ActualThemeVariantChanged += (_, _) => RenderRows();
        AppearanceChanged += RenderRows;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => Refresh();
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
        _drives.Clear();
        _drives.AddRange(DataUsageService.ScanDrives());
        RenderRows();
    }

    private void RenderRows()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _count.Foreground = CardInk.Muted(p);
        _count.Text = $"{_drives.Count} 个分区";
        _rows.Children.Clear();

        if (_drives.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = "未检测到磁盘",
                Foreground = CardInk.Muted(p),
                FontSize = WidgetType.Label,
                Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
            });
            return;
        }

        foreach (var drive in _drives)
        {
            _rows.Children.Add(BuildRow(drive, p));
        }
    }

    private static Control BuildRow(DriveUsage drive, WidgetSurfacePalette p)
    {
        var percent = drive.UsedPercent;
        var top = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            ColumnSpacing = WidgetSpace.GapSm,
        };
        var label = string.IsNullOrWhiteSpace(drive.Label)
                    || string.Equals(drive.Label, drive.Letter, StringComparison.OrdinalIgnoreCase)
            ? $"{drive.Letter}:"
            : $"{drive.Letter}:  {drive.Label}";
        top.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = WidgetType.Label,
            FontWeight = WidgetWeight.Emphasis,
            Foreground = CardInk.Fg(p),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var info = new TextBlock
        {
            Text = $"{percent:0}% · 可用 {DataUsageService.FormatBytes(drive.FreeBytes)}",
            FontSize = WidgetType.Micro,
            FontWeight = WidgetWeight.Body,
            Foreground = CardInk.Muted(p),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        Grid.SetColumn(info, 1);
        top.Children.Add(info);

        var bar = new MeterBar { Height = 4 };
        bar.Set(percent, CardChrome.Level(percent), Color.FromArgb(36, p.Text.R, p.Text.G, p.Text.B));
        return new StackPanel
        {
            Spacing = 3,
            Children = { top, bar },
        };
    }
}
