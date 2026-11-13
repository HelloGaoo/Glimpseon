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

// NativeMiniCalendarWidget
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

/// <summary>
/// 简约月历
/// 头部(‹ 年月 › 22x22 圆角4 hover) + 分隔线 + 星期行(9px 高16) + 6x7 日网格(11px)
/// </summary>
public sealed class NativeMiniCalendarWidget : WidgetCardBase
{
    // 主题对 逐值取自模板 ThemePairs
    private static Color T(string key, bool dark) => dark ? key switch
    {
        "border" => NativeTheme.Rgba(255, 255, 255, 0.06),
        "ink" => NativeTheme.Parse("#d8d8dc"),
        "title" => NativeTheme.Parse("#ececee"),
        "weekday" => NativeTheme.Parse("#8a8a90"),
        "other" => NativeTheme.Parse("#55555a"),
        "hover" => NativeTheme.Rgba(255, 255, 255, 0.08),
        "btn" => NativeTheme.Parse("#a0a0a6"),
        "btnhover" => NativeTheme.Rgba(255, 255, 255, 0.10),
        // 今日/选中 走主题强调色 选中格用底色区分
        "today" => CardAccent.Value,
        "selected" => CardAccent.Value,
        _ => Colors.White,
    } : key switch
    {
        "border" => NativeTheme.Parse("#eeeeee"),
        "ink" => NativeTheme.Parse("#444444"),
        "title" => NativeTheme.Parse("#333333"),
        "weekday" => NativeTheme.Parse("#999999"),
        "other" => NativeTheme.Parse("#cccccc"),
        "hover" => NativeTheme.Parse("#f2f2f2"),
        "btn" => NativeTheme.Parse("#666666"),
        "btnhover" => NativeTheme.Parse("#f0f0f0"),
        "today" => CardAccent.Value,
        "selected" => CardAccent.Value,
        _ => Colors.Black,
    };

    private readonly TextBlock _title = new();
    private readonly UniformGrid _daysGrid = new() { Columns = 7, Rows = 6 };
    private readonly List<Border> _cells = new();
    private readonly List<TextBlock> _dayTexts = new();
    private readonly List<Ellipse> _dayDots = new();
    private readonly List<TextBlock> _weekdayTexts = new();
    private readonly Border _headerBorder = new();
    private readonly Border _prevBtn = new();
    private readonly Border _nextBtn = new();
    private int _year;
    private int _month;
    private int? _selectedDay;

    public NativeMiniCalendarWidget(ComponentDefinition definition) : base(definition)
    {
        var today = DateTime.Today;
        _year = today.Year;
        _month = today.Month - 1;

        BuildHeader();
        var weekdayRow = new UniformGrid { Columns = 7 };
        foreach (var w in new[] { "日", "一", "二", "三", "四", "五", "六" })
        {
            var tb = new TextBlock
            {
                Text = w,
                FontSize = WidgetType.Micro,
                FontWeight = WidgetWeight.Emphasis,
                TextAlignment = TextAlignment.Center,
                Height = 16,
            };
            _weekdayTexts.Add(tb);
            weekdayRow.Children.Add(tb);
        }
        var weekdayPad = new Border { Padding = new Thickness(4, 2, 4, 0), Child = weekdayRow };
        var daysPad = new Border { Padding = new Thickness(4, 0, 4, 4), Child = _daysGrid };

        for (var i = 0; i < 42; i++)
        {
            var dot = new Ellipse
            {
                Width = 20,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false,
                IsHitTestVisible = false,
            };
            var dayText = new TextBlock
            {
                FontSize = WidgetType.Label,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var cell = new Border
            {
                CornerRadius = new CornerRadius(4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Grid { Children = { dot, dayText } },
            };
            cell.PointerEntered += (_, _) =>
            {
                if (cell.Tag is int) // 本月日期才 hover
                {
                    cell.Background = new SolidColorBrush(T("hover", IsDarkNow));
                }
            };
            cell.PointerExited += (_, _) => cell.Background = null;
            cell.PointerReleased += (_, _) =>
            {
                if (cell.Tag is int day)
                {
                    _selectedDay = day;
                    RenderMonth();
                }
            };
            _cells.Add(cell);
            _dayTexts.Add(dayText);
            _dayDots.Add(dot);
            _daysGrid.Children.Add(cell);
        }

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,*"),
        };
        Grid.SetRow(_headerBorder, 0);
        Grid.SetRow(weekdayPad, 1);
        Grid.SetRow(daysPad, 2);
        root.Children.Add(_headerBorder);
        root.Children.Add(weekdayPad);
        root.Children.Add(daysPad);

        Card.Child = root;
        Content = Card;
        ActualThemeVariantChanged += (_, _) => { ApplyPalette(); RenderMonth(); };
        Config.ThemeColor.ValueChanged += _ => RenderMonth();
        SizeChanged += (_, _) => RenderMonth();
        ApplyPalette();
        RenderMonth();
    }

    private bool IsDarkNow => CardIsDark;

    private void BuildHeader()
    {
        _prevBtn.Width = 24;
        _prevBtn.Height = 24;
        _prevBtn.CornerRadius = new CornerRadius(6);
        _prevBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _prevBtn.Child = new TextBlock { Text = "‹", FontSize = WidgetType.Body, TextAlignment = TextAlignment.Center };
        _prevBtn.PointerEntered += (_, _) => _prevBtn.Background = new SolidColorBrush(T("btnhover", IsDarkNow));
        _prevBtn.PointerExited += (_, _) => _prevBtn.Background = null;
        _prevBtn.PointerReleased += (_, _) => { _month--; if (_month < 0) { _month = 11; _year--; } _selectedDay = null; RenderMonth(); };

        _nextBtn.Width = 24;
        _nextBtn.Height = 24;
        _nextBtn.CornerRadius = new CornerRadius(6);
        _nextBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _nextBtn.Child = new TextBlock { Text = "›", FontSize = WidgetType.Body, TextAlignment = TextAlignment.Center };
        _nextBtn.PointerEntered += (_, _) => _nextBtn.Background = new SolidColorBrush(T("btnhover", IsDarkNow));
        _nextBtn.PointerExited += (_, _) => _nextBtn.Background = null;
        _nextBtn.PointerReleased += (_, _) => { _month++; if (_month > 11) { _month = 0; _year++; } _selectedDay = null; RenderMonth(); };

        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.FontSize = WidgetType.Title;
        _title.FontWeight = WidgetWeight.Title;

        var header = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("24,*,24"),
        };
        Grid.SetColumn(_prevBtn, 0);
        Grid.SetColumn(_title, 1);
        Grid.SetColumn(_nextBtn, 2);
        header.Children.Add(_prevBtn);
        header.Children.Add(_title);
        header.Children.Add(_nextBtn);

        _headerBorder.Child = header;
        _headerBorder.Padding = new Thickness(WidgetSpace.GapSm, WidgetSpace.GapXs);
        _headerBorder.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    private void ApplyPalette()
    {
        var dark = IsDarkNow;
        _title.Foreground = new SolidColorBrush(T("title", dark));
        _headerBorder.BorderBrush = new SolidColorBrush(T("border", dark));
        ((TextBlock)_prevBtn.Child!).Foreground = new SolidColorBrush(T("btn", dark));
        ((TextBlock)_nextBtn.Child!).Foreground = new SolidColorBrush(T("btn", dark));
        foreach (var tb in _weekdayTexts)
        {
            tb.Foreground = new SolidColorBrush(T("weekday", dark));
        }
    }

    private void RenderMonth()
    {
        _title.Text = $"{_year}年{_month + 1}月";
        var today = DateTime.Today;
        var startWeekday = (int)new DateTime(_year, _month + 1, 1).DayOfWeek;
        var daysInMonth = DateTime.DaysInMonth(_year, _month + 1);
        var weeks = (startWeekday + daysInMonth + 6) / 7;
        var daysInPrev = (_month == 0 ? new DateTime(_year - 1, 12, 31) : new DateTime(_year, _month, 1).AddDays(-1)).Day;
        var accent = CardAccent.Value;
        var cellW = _daysGrid.Bounds.Width > 1 ? _daysGrid.Bounds.Width / 7 : 28;
        var cellH = _daysGrid.Bounds.Height > 1 ? _daysGrid.Bounds.Height / weeks : 24;
        var dotSize = Math.Clamp(Math.Min(cellW, cellH) * 0.82, 16, 30);

        // 只使用实际周数 避免底部空行
        _daysGrid.Rows = weeks;
        _daysGrid.Children.Clear();
        for (var i = 0; i < weeks * 7; i++)
        {
            _daysGrid.Children.Add(_cells[i]);
        }

        for (var i = 0; i < weeks * 7; i++)
        {
            var cell = _cells[i];
            var tb = _dayTexts[i];
            var dot = _dayDots[i];
            cell.Tag = null;
            cell.Background = null;
            dot.IsVisible = false;
            string cls;
            int day;
            if (i < startWeekday)
            {
                day = daysInPrev - (startWeekday - 1 - i);
                cls = "other";
            }
            else if (i < startWeekday + daysInMonth)
            {
                day = i - startWeekday + 1;
                cls = _year == today.Year && _month == today.Month - 1 && day == today.Day ? "today" : "day";
                if (cls == "day" && _selectedDay == day)
                {
                    cls = "selected";
                }
                cell.Tag = day;
            }
            else
            {
                day = i - startWeekday - daysInMonth + 1;
                cls = "other";
            }
            tb.Text = day.ToString();
            tb.FontWeight = cls is "today" or "selected" ? FontWeight.SemiBold : FontWeight.Normal;
            tb.Foreground = new SolidColorBrush(cls switch
            {
                "other" => T("other", IsDarkNow),
                "today" => OnAccentColor(accent),
                "selected" => accent,
                _ => T("ink", IsDarkNow),
            });
            if (cls == "today")
            {
                dot.Width = dotSize;
                dot.Height = dotSize;
                dot.Fill = new SolidColorBrush(accent);
                dot.Stroke = null;
                dot.IsVisible = true;
            }
            else if (cls == "selected")
            {
                dot.Width = dotSize;
                dot.Height = dotSize;
                dot.Fill = null;
                dot.Stroke = new SolidColorBrush(accent);
                dot.StrokeThickness = 1.25;
                dot.IsVisible = true;
            }
        }
    }

    // 今日圆点上的日期用对比色 浅强调色配深字 深强调色配白字
    private static Color OnAccentColor(Color accent)
    {
        var luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
        return luminance > 0.58 ? Color.FromRgb(20, 20, 20) : Colors.White;
    }
}
