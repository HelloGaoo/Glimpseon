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

// TimetablePreviewWidget
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

public sealed class TimetablePreviewWidget : WidgetCardBase
{
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _dateText = new();
    private readonly TextBlock _countText = new();
    private readonly Border _countChip = new();
    // Grid 行宿主: 星号均分 课少时撑满整卡
    private readonly Grid _rowsHost = new() { RowSpacing = WidgetSpace.GapSm, ClipToBounds = true };
    private readonly List<(Border Row, ProgressBar Bar, bool IsCurrent)> _rows = new();
    private (string Start, string End)? _currentTimes;

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _progressTimer;
    private string _sig = "";

    public TimetablePreviewWidget(ComponentDefinition definition) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.PadCompact),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.Gap,
        };

        // 头部: 标题 + 日期, 右侧课程数胶囊
        var header = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto"),
            ColumnSpacing = WidgetSpace.Gap,
        };
        var titleStack = new StackPanel { Spacing = 2 };
        _titleText.Text = AppUtils.Tr("timetable.today_schedule");
        _titleText.FontSize = WidgetType.Headline;
        _titleText.FontWeight = WidgetWeight.Title;
        _dateText.FontSize = WidgetType.Label;
        titleStack.Children.Add(_titleText);
        titleStack.Children.Add(_dateText);

        _countText.FontSize = WidgetType.Label;
        _countChip.Padding = new Thickness(WidgetSpace.Gap, WidgetSpace.GapXs);
        _countChip.CornerRadius = new CornerRadius(WidgetSpace.RadiusInner);
        _countChip.VerticalAlignment = VerticalAlignment.Center;
        _countChip.Child = _countText;

        Grid.SetColumn(titleStack, 0);
        Grid.SetColumn(_countChip, 1);
        header.Children.Add(titleStack);
        header.Children.Add(_countChip);

        Grid.SetRow(header, 0);
        Grid.SetRow(_rowsHost, 1);
        root.Children.Add(header);
        root.Children.Add(_rowsHost);
        Card.Child = root;
        Content = Card;

        ApplyRowStyle();
        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyRowStyle();
            _sig = "";
            RefreshSchedule();
        };
        Config.ComponentCardOpacity.ValueChanged += _ => { _sig = ""; RefreshSchedule(); };
        Config.ComponentCardRadius.ValueChanged += _ => { _sig = ""; RefreshSchedule(); };

        // 原版 showEvent: 5s 刷新 + 1s 进度
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) => RefreshSchedule();
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _refreshTimer.Start();
        _progressTimer.Start();
        RefreshSchedule();

        // 展开层 单日弹整周
        EnableExpansion(AppUtils.Tr("timetable.week_schedule"), () => new WeekTimetablePanel());
    }

    private void RefreshSchedule()
    {
        var schedule = TimetableScheduleProvider.GetTodaySchedule();
        var now = AppUtils.PreciseNow();
        UpdateHeader(schedule, now);

        // 重建签名
        var parts = new List<string>();
        foreach (var row in schedule)
        {
            var isPast = !row.IsBreak && !row.IsCurrent && TimeOnly.TryParse(row.EndTime, out var et) && et <= TimeOnly.FromDateTime(now);
            if (row.IsBreak && !row.IsCurrent)
            {
                continue; // 非当前课间行跳过
            }
            parts.Add($"{row.Subject}|{row.StartTime}|{row.EndTime}|{row.Index}|{row.IsBreak}|{row.BreakName}|{isPast}");
        }
        var sig = string.Join(";", parts);
        if (sig == _sig && _rowsHost.Children.Count > 0)
        {
            UpdateProgress();
            return;
        }
        _sig = sig;
        RebuildRows(schedule, now);
    }

    private void UpdateHeader(List<ScheduleRow> schedule, DateTime now)
    {
        _dateText.Text = $"{now:MM/dd} · {AppUtils.Tr(AppUtils.WeekdayKey(now.DayOfWeek))}";
        var courses = schedule.Count(r => !r.IsBreak && r.Subject.Trim().Length > 0);
        _countText.Text = AppUtils.Tr("timetable.lessons_count", ("n", courses));
        _countChip.IsVisible = courses > 0;
    }

    private void RebuildRows(List<ScheduleRow> schedule, DateTime now)
    {
        _rowsHost.Children.Clear();
        _rowsHost.RowDefinitions.Clear();
        _rows.Clear();
        _currentTimes = null;
        ScheduleRow? currentRow = null;

        // 先找最后一个 current
        foreach (var row in schedule)
        {
            if (row.IsCurrent)
            {
                currentRow = row;
            }
        }

        // 没有课程时, 不再把唯一的空节次拉满整张卡, 改为完整空态
        var hasCourse = schedule.Any(r => !r.IsBreak && r.Subject.Trim().Length > 0);
        if (schedule.Count == 0 || !hasCourse)
        {
            _rowsHost.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            var empty = BuildEmptyState();
            Grid.SetRow(empty, 0);
            _rowsHost.Children.Add(empty);
            return;
        }

        // 行数少 -> 星号均分撑满; 多 -> 定量高避免压扁
        var visible = schedule.Count(r => !(r.IsBreak && !r.IsCurrent));
        var stretch = visible <= 9;
        var rowIdx = 0;

        foreach (var row in schedule)
        {
            if (row.IsBreak && !row.IsCurrent)
            {
                continue;
            }
            var isPast = !row.IsBreak && !row.IsCurrent &&
                TimeOnly.TryParse(row.EndTime, out var et) && et <= TimeOnly.FromDateTime(now);
            var isCurrentRow = ReferenceEquals(row, currentRow);
            if (isCurrentRow)
            {
                _currentTimes = (row.StartTime, row.EndTime);
            }

            var content = new Grid
            {
                Margin = new Thickness(WidgetSpace.Gap, WidgetSpace.GapSm, WidgetSpace.Gap, WidgetSpace.GapSm),
                ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("4,58,*,Auto"),
                ColumnSpacing = WidgetSpace.Gap,
            };
            var status = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            TextBlock idx;
            if (row.IsBreak)
            {
                idx = new TextBlock();
            }
            else
            {
                idx = new TextBlock
                {
                    Text = $"第{row.Index}节",
                    TextAlignment = TextAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            var subj = new TextBlock
            {
                Text = row.IsBreak ? (row.BreakName.Length > 0 ? row.BreakName : "课间") : (row.Subject.Length > 0 ? row.Subject : "—"),
                FontWeight = FontWeight.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var time = new TextBlock
            {
                Text = $"{row.StartTime}–{row.EndTime}",
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(status, 0);
            Grid.SetColumn(idx, 1);
            Grid.SetColumn(subj, 2);
            Grid.SetColumn(time, 3);
            content.Children.Add(status);
            content.Children.Add(idx);
            content.Children.Add(subj);
            content.Children.Add(time);

            var rowGrid = new Grid { RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto") };
            var bar = new ProgressBar
            {
                Height = 3,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                CornerRadius = new CornerRadius(2),
                IsVisible = isCurrentRow,
            };
            Grid.SetRow(content, 0);
            Grid.SetRow(bar, 1);
            rowGrid.Children.Add(content);
            rowGrid.Children.Add(bar);

            var border = new Border { Child = rowGrid };
            _rowsHost.RowDefinitions.Add(new RowDefinition(
                stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto));
            Grid.SetRow(border, rowIdx++);
            _rowsHost.Children.Add(border);
            _rows.Add((border, bar, isCurrentRow));
            StyleRow(border, subj, time, idx, status, bar, isCurrentRow, isPast);
        }
        UpdateProgress();
    }

    private Grid BuildEmptyState()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var root = new Grid { Margin = new Thickness(0, 2, 0, 2) };

        var empty = new CardEmptyState(
            FAIcons.Glyph(AppIcons.Calendar),
            AppUtils.Tr("timetable.day_empty"),
            AppUtils.Tr("component.click_to_settings"));
        empty.Apply(p);
        root.Children.Add(empty);
        return root;
    }

    // 行样式
    private void StyleRow(Border border, TextBlock subj, TextBlock time, TextBlock idx, Border status,
        ProgressBar bar, bool isCurrentRow, bool isPast)
    {
        var dark = CardIsDark;
        var p = WidgetSurfacePalette.Resolve(dark);
        var op = Math.Clamp(Config.ComponentCardOpacity.Value / 100.0, 0, 1);
        IBrush bg;
        if (isCurrentRow)
        {
            bg = CardAccent.Brush(dark ? 0.14 : 0.09);
        }
        else if (isPast)
        {
            bg = new SolidColorBrush(dark ? Colors.White : Colors.Black, op * (dark ? 0.025 : 0.018));
        }
        else
        {
            bg = new SolidColorBrush(dark ? Colors.White : Colors.Black, op * (dark ? 0.055 : 0.035));
        }
        border.Background = bg;
        border.CornerRadius = new CornerRadius(Math.Max(9, Config.ComponentCardRadius.Value - 2));

        var ink = isPast ? new SolidColorBrush(p.Text, WidgetInk.DimOpacity) : CardInk.Fg(p);
        subj.Foreground = ink;
        subj.FontSize = WidgetType.Body;
        subj.FontWeight = isCurrentRow ? WidgetWeight.Title : WidgetWeight.Emphasis;
        time.Foreground = isPast ? new SolidColorBrush(p.Text, WidgetInk.DimOpacity) : CardInk.Muted(p);
        time.FontSize = WidgetType.Label;
        idx.Foreground = isCurrentRow ? CardAccent.Brush() : new SolidColorBrush(p.Text, WidgetInk.DimOpacity);
        idx.FontSize = WidgetType.Micro;
        status.Background = isCurrentRow ? CardAccent.Brush() : new SolidColorBrush(p.Text, 0.12);
        bar.Foreground = CardAccent.Brush();
        bar.Background = new SolidColorBrush(dark ? Colors.White : Colors.Black, dark ? 0.06 : 0.05);
        bar.CornerRadius = new CornerRadius(2);
    }

    private void ApplyRowStyle()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _titleText.Foreground = CardInk.Fg(p);
        _dateText.Foreground = CardInk.Muted(p);
        _countText.Foreground = CardInk.Muted(p);
        _countChip.Background = CardAccent.Brush(0.10);
        _countChip.BorderBrush = CardAccent.Brush(0.16);
        _countChip.BorderThickness = new Thickness(1);
    }

    // 当前行进度
    private void UpdateProgress()
    {
        if (_currentTimes is null)
        {
            return;
        }
        var (startS, endS) = _currentTimes.Value;
        if (!TimeOnly.TryParse(startS, out var st) || !TimeOnly.TryParse(endS, out var et))
        {
            return;
        }
        var now = TimeOnly.FromDateTime(AppUtils.PreciseNow());
        var total = (et - st).TotalSeconds;
        if (total <= 0)
        {
            return;
        }
        var elapsed = (now - st).TotalSeconds;
        var pct = Math.Clamp(elapsed / total * 100, 0, 100);
        foreach (var (_, bar, isCurrent) in _rows)
        {
            if (isCurrent)
            {
                bar.Value = pct;
                return;
            }
        }
    }
}
