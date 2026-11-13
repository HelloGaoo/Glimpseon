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

// PerformanceMonitorWidget
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

/// <summary>性能监测</summary>
public sealed class PerformanceMonitorWidget : WidgetCardBase
{
    private readonly UsageChart _chart = new();
    private readonly TextBlock _cpuValue = new();
    private readonly TextBlock _ramValue = new();
    private readonly TextBlock _gpuValue = new();
    private readonly Border _cpuDot = MakeDot();
    private readonly Border _ramDot = MakeDot();
    private readonly Border _gpuDot = MakeDot();
    private readonly TextBlock _cpuLabel = new();
    private readonly TextBlock _ramLabel = new();
    private readonly TextBlock _gpuLabel = new();

    private static Border MakeDot() => new()
    {
        Width = 6,
        Height = 6,
        CornerRadius = new CornerRadius(3),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public PerformanceMonitorWidget(ComponentDefinition definition) : base(definition)
    {
        var header = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,*,Auto"),
            ColumnSpacing = 16,
        };
        var cpuCell = MakeMetricCell(_cpuDot, _cpuLabel, _cpuValue, "CPU");
        var ramCell = MakeMetricCell(_ramDot, _ramLabel, _ramValue, "RAM");
        var gpuCell = MakeMetricCell(_gpuDot, _gpuLabel, _gpuValue, "GPU");
        gpuCell.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(cpuCell, 0);
        Grid.SetColumn(ramCell, 1);
        Grid.SetColumn(gpuCell, 3);
        header.Children.Add(cpuCell);
        header.Children.Add(ramCell);
        header.Children.Add(gpuCell);

        _chart.DarkProvider = () => CardIsDark;
        _chart.VerticalAlignment = VerticalAlignment.Stretch;
        _chart.MinHeight = 24;

        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad),
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.Gap,
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(_chart, 1);
        root.Children.Add(header);
        root.Children.Add(_chart);
        Card.Child = root;
        Content = Card;

        _cpuValue.Text = "--";
        _ramValue.Text = "--";
        _gpuValue.Text = "--";
        ApplyCardStyle();
        SystemSampler.PerfReady += OnPerf;
        SystemSampler.EnsureStarted();
    }

    private StackPanel MakeMetricCell(Border dot, TextBlock label, TextBlock value, string text)
    {
        label.Text = text;
        label.FontSize = WidgetType.Label;
        label.VerticalAlignment = VerticalAlignment.Center;
        value.FontSize = WidgetType.Headline;
        value.FontWeight = WidgetWeight.Display;
        value.FontFamily = WidgetFonts.Num;
        value.VerticalAlignment = VerticalAlignment.Center;
        var cell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = WidgetSpace.GapSm,
            VerticalAlignment = VerticalAlignment.Center,
        };
        cell.Children.Add(dot);
        cell.Children.Add(label);
        cell.Children.Add(value);
        return cell;
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _cpuDot.Background = CardAccent.Brush();
        _ramDot.Background = new SolidColorBrush(p.Text, 0.45);
        _gpuDot.Background = new SolidColorBrush(p.Text, 0.28);
        _cpuLabel.Foreground = CardInk.Muted(p);
        _ramLabel.Foreground = CardInk.Muted(p);
        _gpuLabel.Foreground = CardInk.Muted(p);
        _cpuValue.Foreground = CardInk.Fg(p);
        _ramValue.Foreground = CardInk.Fg(p);
        _gpuValue.Foreground = CardInk.Muted(p);
        _chart.InvalidateVisual();
    }

    private void OnPerf(double cpu, double ram, double gpu)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var p = WidgetSurfacePalette.Resolve(CardIsDark);
            _cpuValue.Text = $"{Math.Round(cpu)}%";
            _ramValue.Text = $"{Math.Round(ram)}%";
            _gpuValue.Text = gpu > 0 ? $"{Math.Round(gpu)}%" : "--";
            // 85% 以上只有一处警示色
            var hot = cpu >= 85;
            var warn = CardInk.Warn().Color;
            _cpuDot.Background = hot ? new SolidColorBrush(warn) : CardAccent.Brush();
            _cpuValue.Foreground = hot ? new SolidColorBrush(warn) : CardInk.Fg(p);
            _chart.Push(cpu, ram);
        });
    }
}
