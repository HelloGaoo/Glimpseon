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

// WeekTimetablePanel
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

// 今日课表预览
// 规格: 300x550 边距(10,10,10,10) 间距6 标题17px w600 行内容边距(10,7,10,7) 间距12
// 第X节15px(宽68) 课程名20px w500 时间15px 右对齐 当前行底部进度条3px #4cc2ff
// 行背景 深色 op*0.08/当前op*0.18/已过op*0.04 浅色 0.04/0.10/0.02 已过文字#999999

/// <summary>周课表 HTML 风 仅列色</summary>
public sealed class WeekTimetablePanel : UserControl
{
    private static readonly string[] DayOrder = TimetableScheduleProvider.DisplayDayOrder;

    public WeekTimetablePanel()
    {
        var week = TimetableScheduleProvider.GetWeekSchedule();
        var now = AppUtils.PreciseNow();
        var todayKey = now.DayOfWeek switch
        {
            DayOfWeek.Monday => "monday",
            DayOfWeek.Tuesday => "tuesday",
            DayOfWeek.Wednesday => "wednesday",
            DayOfWeek.Thursday => "thursday",
            DayOfWeek.Friday => "friday",
            DayOfWeek.Saturday => "saturday",
            _ => "sunday",
        };
        var dark = ThemeSense.IsDark(this);
        var accent = Common.ParseAccentColor(Config.ThemeColor.Value);
        var p = WidgetSurfacePalette.Resolve(dark);
        var textColor = p.Text;
        var subColor = p.Sub;
        var lineColor = p.Line;

        // 竖带按天交替 今日仅表头 accent
        var bandA = p.BandA;
        var bandB = p.BandB;
        var gutterBg = p.Gutter;

        // 时间线取最长日 位置对齐
        var reference = week.Values.OrderByDescending(r => r.Count).FirstOrDefault() ?? [];

        var cols = "96," + string.Join(",", Enumerable.Repeat("*", 7));
        var rowDefs = "Auto," + string.Join(",", Enumerable.Repeat("Auto", Math.Max(0, reference.Count)));

        // 无边框 底色成列带
        Border Cell(Control content, int r, int c, Color? bg)
        {
            var border = new Border
            {
                Background = bg is { } b ? new SolidColorBrush(b) : null,
                Padding = new Thickness(WidgetSpace.Gap),
                MinHeight = 56,
                Child = content,
            };
            Grid.SetRow(border, r);
            Grid.SetColumn(border, c);
            return border;
        }

        // 表头 文字+横杠
        StackPanel HeadCell(string title, bool today)
        {
            var text = new TextBlock
            {
                Text = title,
                FontSize = WidgetType.Headline,
                FontWeight = WidgetWeight.Title,
                Foreground = new SolidColorBrush(today ? accent : textColor),
                TextAlignment = TextAlignment.Center,
            };
            var bar = new Border
            {
                Height = 2,
                Margin = new Thickness(12, 8, 12, 0),
                Background = new SolidColorBrush(today ? accent : lineColor),
            };
            return new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { text, bar } };
        }

        // 科目主 教师次
        StackPanel SubjectCell(string subject, string teacher, bool current)
        {
            return new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = subject,
                        FontSize = WidgetType.Headline,
                        FontWeight = WidgetWeight.Title,
                        Foreground = new SolidColorBrush(current ? accent : textColor),
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                    new TextBlock
                    {
                        Text = teacher.Length > 0 ? teacher : "—",
                        FontSize = WidgetType.Label,
                        Foreground = new SolidColorBrush(current ? accent : subColor),
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
                    },
                },
            };
        }

        // 表头行
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(cols) };
        header.Children.Add(Cell(HeadCell(AppUtils.Tr("timetable.time_range"), false), 0, 0, gutterBg));
        for (var d = 0; d < 7; d++)
        {
            var dayKey = DayOrder[d];
            header.Children.Add(Cell(HeadCell(AppUtils.Tr($"timetable.{dayKey}"), dayKey == todayKey), 0, d + 1, d % 2 == 1 ? bandB : bandA));
        }

        // 节次行
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), RowDefinitions = new RowDefinitions(rowDefs) };
        for (var i = 0; i < reference.Count; i++)
        {
            var refRow = reference[i];
            var isBreak = refRow.IsBreak;
            var current = refRow.IsCurrent;

            var gutter = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = isBreak ? (refRow.BreakName.Length > 0 ? refRow.BreakName : "课间") : $"第{refRow.Index}节",
                        FontSize = WidgetType.Title,
                        FontWeight = WidgetWeight.Title,
                        Foreground = new SolidColorBrush(current ? accent : textColor),
                        TextAlignment = TextAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = $"{refRow.StartTime}~{refRow.EndTime}",
                        FontSize = WidgetType.Micro,
                        Foreground = new SolidColorBrush(subColor),
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
                    },
                },
            };
            body.Children.Add(Cell(gutter, i, 0, gutterBg));

            for (var d = 0; d < 7; d++)
            {
                var dayKey = DayOrder[d];
                var rows = week.GetValueOrDefault(dayKey) ?? [];
                var bg = d % 2 == 1 ? bandB : bandA;
                if (i < rows.Count && !rows[i].IsBreak)
                {
                    var row = rows[i];
                    body.Children.Add(Cell(SubjectCell(row.Subject, row.Teacher, current), i, d + 1, bg));
                }
                else
                {
                    // 课间空档 同列带
                    body.Children.Add(Cell(new TextBlock(), i, d + 1, bg));
                }
            }
        }

        if (reference.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = AppUtils.Tr("timetable.day_empty"),
                FontSize = WidgetType.Body,
                Opacity = WidgetInk.DimOpacity,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 24, 0, 0),
            });
            Grid.SetColumnSpan(body.Children[^1], 8);
        }

        // 表头固定 行区滚
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(header);
        var scroll = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
    }
}
