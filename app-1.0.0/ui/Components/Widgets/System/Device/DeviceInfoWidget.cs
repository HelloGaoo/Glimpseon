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

// DeviceInfoWidget
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

/// <summary>
/// 设备信息
/// 400x200: 两列三行
/// 200x200: 单列六行
/// </summary>
public sealed class DeviceInfoWidget : WidgetCardBase
{
    private static readonly string[] Captions =
        { "计算机名", "系统", "用户", "处理器", "内存", "已运行" };

    private readonly TextBlock _title;
    private readonly TextBlock[] _labels = new TextBlock[6];
    private readonly TextBlock[] _values = new TextBlock[6];
    private readonly Grid[] _cells = new Grid[6];
    private readonly Grid _grid;
    private readonly DispatcherTimer _tick;
    private bool _compact;
    private bool _laidOut;

    public DeviceInfoWidget(ComponentDefinition definition) : base(definition)
    {
        _title = CardChrome.Title("设备信息");

        _grid = new Grid
        {
            ColumnSpacing = WidgetSpace.Gap,
            RowSpacing = WidgetSpace.Gap,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        for (var i = 0; i < Captions.Length; i++)
        {
            var cell = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
                RowDefinitions = RowDefinitions.Parse("Auto,Auto"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var label = new TextBlock
            {
                Text = Captions[i],
                FontSize = WidgetType.Micro,
                FontWeight = WidgetWeight.Body,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var value = new TextBlock
            {
                Text = "—",
                FontSize = WidgetType.Label,
                FontWeight = WidgetWeight.Emphasis,
                LineHeight = 14,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            cell.Children.Add(label);
            cell.Children.Add(value);
            _grid.Children.Add(cell);
            _cells[i] = cell;
            _labels[i] = label;
            _values[i] = value;
        }

        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.Gap,
            Margin = new Thickness(WidgetSpace.PadCompact),
        };
        root.Children.Add(_title);
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);
        Card.Child = root;
        Content = Card;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => UpdateUptime();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
        SizeChanged += (_, e) => ApplyResponsive(e.NewSize.Width);
        ApplyResponsive(Width);
        RefreshStatic();
        ApplyPalette();
    }

    // 响应式布局
    private void ApplyResponsive(double width)
    {
        var compact = width > 0 && width < 300;
        if (_laidOut && compact == _compact)
        {
            return;
        }
        _laidOut = true;
        _compact = compact;

        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        if (compact)
        {
            _grid.ColumnSpacing = 0;
            _grid.RowSpacing = WidgetSpace.GapXs;
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (var i = 0; i < _cells.Length; i++)
            {
                _grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            }
            for (var i = 0; i < _cells.Length; i++)
            {
                var cell = _cells[i];
                cell.ColumnSpacing = WidgetSpace.Gap;
                cell.RowSpacing = 0;
                Grid.SetRow(cell, i);
                Grid.SetColumn(cell, 0);
                Grid.SetRow(_labels[i], 0);
                Grid.SetColumn(_labels[i], 0);
                Grid.SetColumnSpan(_labels[i], 1);
                Grid.SetRow(_values[i], 0);
                Grid.SetColumn(_values[i], 1);
                Grid.SetColumnSpan(_values[i], 1);
                _values[i].FontSize = WidgetType.Micro;
                _values[i].LineHeight = double.NaN;
                _values[i].TextWrapping = TextWrapping.NoWrap;
                _values[i].MaxLines = 1;
            }
        }
        else
        {
            _grid.ColumnSpacing = 16;
            _grid.RowSpacing = WidgetSpace.Gap;
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (var i = 0; i < 3; i++)
            {
                _grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            }
            for (var i = 0; i < _cells.Length; i++)
            {
                var cell = _cells[i];
                cell.ColumnSpacing = 0;
                cell.RowSpacing = 2;
                Grid.SetRow(cell, i / 2);
                Grid.SetColumn(cell, i % 2);
                Grid.SetRow(_labels[i], 0);
                Grid.SetColumn(_labels[i], 0);
                Grid.SetColumnSpan(_labels[i], 2);
                Grid.SetRow(_values[i], 1);
                Grid.SetColumn(_values[i], 0);
                Grid.SetColumnSpan(_values[i], 2);
                _values[i].FontSize = WidgetType.Label;
                _values[i].LineHeight = 14;
                _values[i].TextWrapping = TextWrapping.Wrap;
                _values[i].MaxLines = 2;
            }
        }
    }

    private void RefreshStatic()
    {
        var pc = PcInfoService.Read();
        SetValue(0, string.IsNullOrWhiteSpace(pc.Machine) ? "—" : pc.Machine);
        SetValue(1, string.IsNullOrWhiteSpace(pc.Os) ? "—" : pc.Os);
        SetValue(2, string.IsNullOrWhiteSpace(pc.User) ? "—" : pc.User);
        SetValue(3, string.IsNullOrWhiteSpace(pc.Cpu) ? "未知处理器" : pc.Cpu);
        SetValue(4, $"{pc.RamGb.ToString("0.#", CultureInfo.CurrentCulture)} GB · {pc.Cores} 核");
        UpdateUptime();
    }

    private void SetValue(int index, string text)
    {
        _values[index].Text = text;
        ToolTip.SetTip(_values[index], text);
    }

    // 每秒刷新
    private void UpdateUptime()
    {
        _values[5].Text = UptimeText(TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    private static string UptimeText(TimeSpan uptime)
    {
        var days = (int)uptime.TotalDays;
        var hours = uptime.Hours;
        if (days > 0)
        {
            return $"{days}天 {hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
        }
        return $"{hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _tick.Start();
        UpdateUptime();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _tick.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _title.Foreground = CardInk.Fg(p);
        foreach (var label in _labels)
        {
            label.Foreground = CardInk.Faint(p);
        }
        foreach (var value in _values)
        {
            value.Foreground = CardInk.Fg(p);
        }
    }
}
