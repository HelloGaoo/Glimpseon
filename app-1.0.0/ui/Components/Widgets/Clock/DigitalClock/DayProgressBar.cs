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

// DayProgressBar
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

// 今日 24 格进度 已过整格半透明 当前格按分钟填充 未到走轨道色
public sealed class DayProgressBar : Grid
{
    private const int Cells = 24;
    private readonly Border[] _ticks = new Border[Cells];
    private readonly Grid _labelRow = new();
    private readonly TextBlock[] _labels = new TextBlock[5];
    private IBrush _past = Brushes.Transparent;
    private IBrush _now = Brushes.Transparent;
    private IBrush _rail = Brushes.Transparent;
    private bool _showLabels;

    public bool ShowLabels
    {
        get => _showLabels;
        set
        {
            _showLabels = value;
            _labelRow.IsVisible = value;
        }
    }

    public DayProgressBar()
    {
        IsHitTestVisible = false;
        RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("20,Auto");
        RowSpacing = 2;

        var tickHost = new Grid { ColumnSpacing = 3, VerticalAlignment = VerticalAlignment.Bottom };
        for (var i = 0; i < Cells; i++)
        {
            tickHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tick = new Border
            {
                Height = 9,
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
            };
            SetColumn(tick, i);
            tickHost.Children.Add(tick);
            _ticks[i] = tick;
        }
        Children.Add(tickHost);

        // 0 / 6 / 12 / 18 / 24 五个刻度 与 24 格等分对齐
        _labelRow.ColumnDefinitions =
            Avalonia.Controls.ColumnDefinitions.Parse("Auto,*,Auto,*,Auto,*,Auto,*,Auto");
        var texts = new[] { "0", "6", "12", "18", "24" };
        for (var i = 0; i < texts.Length; i++)
        {
            var label = new TextBlock
            {
                Text = texts[i],
                FontSize = WidgetType.Micro,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = i == 0
                    ? HorizontalAlignment.Left
                    : i == texts.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            };
            SetColumn(label, i * 2);
            _labelRow.Children.Add(label);
            _labels[i] = label;
        }
        _labelRow.IsVisible = false;
        SetRow(_labelRow, 1);
        Children.Add(_labelRow);
    }

    // 轨道/已过/当前 三色
    public void ApplyPalette(Color accent, Color track, Color ink)
    {
        _past = new SolidColorBrush(accent, 0.42);
        _now = new SolidColorBrush(accent);
        _rail = new SolidColorBrush(track, 0.55);
        foreach (var label in _labels)
        {
            label.Foreground = new SolidColorBrush(ink, CardInk.FaintOpacity);
        }
    }

    // 刷新到某时刻: 已过 9px 当前随时长长到 20px 未到轨道色
    public void Update(DateTime now)
    {
        var hour = now.Hour;
        var frac = (now.Minute * 60 + now.Second) / 3600.0;
        for (var i = 0; i < Cells; i++)
        {
            var current = i == hour;
            _ticks[i].Height = current ? 9 + 11 * frac : 9;
            _ticks[i].Background = i < hour ? _past : current ? _now : _rail;
        }
    }
}
