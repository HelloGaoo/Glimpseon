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

// CalendarMonthWidget
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

// 月历
// 规格: 300x300 边距(12,10,12,8) 间距4 标题20px w600 + 上下按钮28x18 星期13px 6x7 格
// 格内: 日期占上62% 字号 sz*0.46 角标 sz*0.35 bold 今日 #00b7c3 圆 r=sz*0.40

public sealed class CalendarMonthWidget : WidgetCardBase
{
    private static readonly string[] MonthNamesCn =
        { "一月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "十一月", "十二月" };

    // 节日全称 > 简写 逐字对齐原版 HOLIDAY_SHORT_MAP
    private static readonly Dictionary<string, string> HolidayShortMap = new()
    {
        ["元旦节"] = "元旦", ["春节"] = "春节", ["清明节"] = "清明", ["国际劳动节"] = "劳动",
        ["端午节"] = "端午", ["中秋节"] = "中秋", ["国庆节"] = "国庆", ["元宵节"] = "元宵",
        ["小年"] = "小年", ["七夕-魁星诞"] = "七夕", ["中元节"] = "中元", ["重阳节-酆都大帝诞"] = "重阳",
        ["腊八节-释迦如来成佛之辰"] = "腊八", ["春龙节-福德土地正神诞"] = "龙抬头", ["情人节"] = "情人",
        ["国际劳动妇女节"] = "妇女", ["中国植树节"] = "植树", ["孙中山逝世纪念日,中国植树节"] = "植树",
        ["国际愚人节"] = "愚人", ["中国青年节"] = "青年", ["母亲节"] = "母亲", ["国际儿童节"] = "儿童",
        ["父亲节"] = "父亲", ["中国共产党诞生日,香港回归纪念日"] = "建党", ["中国人民解放军建军节"] = "建军",
        ["中国教师节"] = "教师", ["平安夜"] = "平安夜", ["圣诞节"] = "圣诞", ["国际和平日"] = "和平",
        ["中国人民抗日战争纪念日"] = "抗日", ["中国抗日战争胜利纪念日"] = "抗日", ["抗美援朝纪念日"] = "抗美",
        ["南京大屠杀纪念日"] = "公祭", ["上海解放日"] = "解放",
        ["小寒"] = "小寒", ["大寒"] = "大寒", ["立春"] = "立春", ["雨水"] = "雨水", ["惊蛰"] = "惊蛰",
        ["春分"] = "春分", ["清明"] = "清明", ["谷雨"] = "谷雨", ["立夏"] = "立夏", ["小满"] = "小满",
        ["芒种"] = "芒种", ["夏至"] = "夏至", ["小暑"] = "小暑", ["大暑"] = "大暑", ["立秋"] = "立秋",
        ["处暑"] = "处暑", ["白露"] = "白露", ["秋分"] = "秋分", ["寒露"] = "寒露", ["霜降"] = "霜降",
        ["立冬"] = "立冬", ["小雪"] = "小雪", ["大雪"] = "大雪", ["冬至"] = "冬至",
    };

    private static readonly Dictionary<(int Y, int M, int D), string> DayInfoCache = new();

    private readonly TextBlock _monthText = new();
    private readonly TextBlock _yearText = new();
    private readonly TextBlock[] _weekdayTexts = new TextBlock[7];
    private readonly List<TextBlock> _navGlyphs = new();
    private readonly Grid _daysGrid = new();
    private readonly List<RowDefinition> _weekRows = new();
    private readonly CalendarDayCell[,] _cells = new CalendarDayCell[6, 7];
    private readonly DispatcherTimer _timer;
    private int _displayYear;
    private int _displayMonth;
    private DateTime? _lastSeenDate;

    public CalendarMonthWidget(ComponentDefinition definition) : base(definition)
    {
        var today = DateTime.Today;
        _displayYear = today.Year;
        _displayMonth = today.Month;

        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.PadCompact),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,*"),
            RowSpacing = WidgetSpace.GapXs,
        };

        // 标题行: 月份 + 年份 + 翻页
        var titleRow = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto") };
        var caption = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = WidgetSpace.GapXs,
            VerticalAlignment = VerticalAlignment.Center,
        };
        caption.Children.Add(_monthText);
        caption.Children.Add(_yearText);
        titleRow.Children.Add(caption);

        var nav = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = WidgetSpace.GapXs,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nav.Children.Add(MakeNavButton("\uE76B", -1));
        nav.Children.Add(MakeNavButton("\uE76C", 1));
        Grid.SetColumn(nav, 1);
        titleRow.Children.Add(nav);
        Grid.SetRow(titleRow, 0);
        root.Children.Add(titleRow);

        // 星期标题
        var wkNames = new[] { "日", "一", "二", "三", "四", "五", "六" };
        var wkRow = new Avalonia.Controls.Primitives.UniformGrid
        {
            Rows = 1,
            Columns = 7,
            Height = 16,
        };
        for (var i = 0; i < 7; i++)
        {
            var wk = new TextBlock
            {
                Text = wkNames[i],
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = WidgetType.Micro,
                FontWeight = WidgetWeight.Emphasis,
            };
            _weekdayTexts[i] = wk;
            wkRow.Children.Add(wk);
        }
        Grid.SetRow(wkRow, 1);
        root.Children.Add(wkRow);

        // 动态周数日期格
        for (var c = 0; c < 7; c++)
        {
            _daysGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        }
        for (var r = 0; r < 6; r++)
        {
            var row = new RowDefinition(new GridLength(1, GridUnitType.Star));
            _weekRows.Add(row);
            _daysGrid.RowDefinitions.Add(row);
            for (var c = 0; c < 7; c++)
            {
                var cell = new CalendarDayCell { DarkProvider = () => CardIsDark };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                _daysGrid.Children.Add(cell);
                _cells[r, c] = cell;
            }
        }
        Grid.SetRow(_daysGrid, 2);
        root.Children.Add(_daysGrid);
        Card.Child = root;
        Content = Card;

        ApplyStyle();
        ActualThemeVariantChanged += (_, _) => { ApplyStyle(); RefreshCellsVisual(); };
        // 原版 60s 跨日检查
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => CheckDayChange();
        _timer.Start();
        RefreshCalendar();
    }

    private Border MakeNavButton(string glyph, int delta)
    {
        var g = new TextBlock
        {
            Text = glyph,
            FontFamily = FAIcons.SymbolsFont,
            FontSize = WidgetType.Label,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _navGlyphs.Add(g);
        var btn = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Child = g,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        btn.PointerEntered += (_, _) =>
            btn.Background = new SolidColorBrush(CardTheme.T("hover", CardIsDark));
        btn.PointerExited += (_, _) => btn.Background = null;
        btn.PointerReleased += (_, _) => GoMonth(delta);
        return btn;
    }

    private void CheckDayChange()
    {
        var today = DateTime.Today;
        if (_lastSeenDate is { } last && today != last)
        {
            Log.Info($"[CAL] 跨日: {last:yyyy-MM-dd} -> {today:yyyy-MM-dd}");
        }
        _lastSeenDate = today;
        if (today.Year == _displayYear && today.Month == _displayMonth)
        {
            RefreshCalendar();
        }
    }

    private void GoMonth(int delta)
    {
        var total = _displayYear * 12 + (_displayMonth - 1) + delta;
        _displayYear = Math.DivRem(total, 12, out var m);
        _displayMonth = m + 1;
        Log.Debug($"[CalendarComponent] 切换月份: {_displayYear}-{_displayMonth:D2}");
        RefreshCalendar();
    }

    private static string GetDaySubText(int year, int month, int day)
    {
        var key = (year, month, day);
        if (DayInfoCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var sub = "";
        // 节日 > 节气 > 农历 保证每格都有副文本
        var info = AlmanacService.GetToday(new DateTime(year, month, day));
        if (info is not null)
        {
            sub = ShortHoliday(info.Holiday);
            if (sub.Length == 0)
            {
                sub = ShortHoliday(info.SolarTerm);
            }
            if (sub.Length == 0)
            {
                sub = info.LunarDay == "初一" ? info.LunarMonth : info.LunarDay;
            }
        }
        DayInfoCache[key] = sub;
        return sub;
    }

    private static string ShortHoliday(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }
        if (name.Contains(','))
        {
            foreach (var p in name.Split(','))
            {
                if (HolidayShortMap.TryGetValue(p.Trim(), out var hit))
                {
                    return hit;
                }
            }
            return "";
        }
        return HolidayShortMap.TryGetValue(name, out var v) ? v : "";
    }

    private void RefreshCalendar()
    {
        var year = _displayYear;
        var month = _displayMonth;
        var today = DateTime.Today;

        _monthText.Text = MonthNamesCn[month - 1];
        _yearText.Text = year.ToString();

        // 周日=0
        var firstWd = ((int)new DateTime(year, month, 1).DayOfWeek) % 7;
        var dim = DateTime.DaysInMonth(year, month);
        var weeks = (firstWd + dim + 6) / 7;
        var prevMonth = month > 1 ? month - 1 : 12;
        var prevYear = month > 1 ? year : year - 1;
        var prevDim = DateTime.DaysInMonth(prevYear, prevMonth);

        // 只保留实际周数 避免底部空行
        for (var r = 0; r < 6; r++)
        {
            _weekRows[r].Height = r < weeks
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }

        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                _cells[r, c].Clear();
            }
        }

        var lastIndex = weeks * 7;
        for (var i = 0; i < lastIndex; i++)
        {
            var r = i / 7;
            var c = i % 7;
            var weekend = c == 0 || c == 6;
            if (i < firstWd)
            {
                // 上月末尾
                _cells[r, c].SetData(prevDim - firstWd + 1 + i, "", false, false, weekend);
            }
            else if (i < firstWd + dim)
            {
                var day = i - firstWd + 1;
                var isToday = year == today.Year && month == today.Month && day == today.Day;
                _cells[r, c].SetData(day, GetDaySubText(year, month, day), true, isToday, weekend);
            }
            else
            {
                // 下月开头 补满最后一行
                var day = i - firstWd - dim + 1;
                _cells[r, c].SetData(day, "", false, false, weekend);
            }
        }
        Log.Debug($"[CAL] 月历已刷新 {year}-{month:D2} 周数 {weeks} 今日 {today:MM}-{today:dd}");
    }

    private void RefreshCellsVisual()
    {
        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                _cells[r, c].InvalidateVisual();
            }
        }
    }

    private void ApplyStyle()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _monthText.Foreground = CardInk.Fg(p);
        _monthText.FontSize = WidgetType.Title;
        _monthText.FontWeight = WidgetWeight.Title;
        _yearText.Foreground = CardInk.Muted(p);
        _yearText.FontSize = WidgetType.Micro;
        _yearText.Margin = new Thickness(0, 0, 0, 2);
        foreach (var g in _navGlyphs)
        {
            g.Foreground = CardInk.Muted(p);
        }
        for (var i = 0; i < _weekdayTexts.Length; i++)
        {
            var weekend = i == 0 || i == 6;
            _weekdayTexts[i].Foreground = weekend ? CardInk.Faint(p) : CardInk.Muted(p);
        }
    }
}
