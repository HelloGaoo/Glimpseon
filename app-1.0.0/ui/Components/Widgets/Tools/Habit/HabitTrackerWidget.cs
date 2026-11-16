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

// HabitTrackerWidget
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;
using Glimpseon.Core.Services;
using IOPath = System.IO.Path;

namespace Glimpseon.UI;

/// <summary>
/// 习惯打卡
/// 400x200: 周一至周日点选
/// </summary>
public sealed class HabitTrackerWidget : WidgetCardBase
{
    private static readonly string[] WeekLabels = { "一", "二", "三", "四", "五", "六", "日" };
    private const double CellSize = 20;
    private const double RowHeight = 22;
    private const double NameColumn = 72;

    private readonly TextBlock _title = new();
    private readonly TextBlock _range = new();
    private readonly StackPanel _rows = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<TextBlock> _dayLabels = new();
    private readonly TextBlock _footer = new();
    private readonly List<string> _habits = new();
    private readonly HashSet<string> _marks = new();
    private string? _dataFile;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _dateTimer;
    private DateTime _renderedDate;
    private Border? _pressedCell;

    // 周一为本周起点
    private static DateTime WeekStart =>
        DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));

    private static int TodayIndex => ((int)DateTime.Today.DayOfWeek + 6) % 7;

    public HabitTrackerWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _habits.AddRange(ReadHabits(config));

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };

        _renderedDate = DateTime.Today;
        _dateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _dateTimer.Tick += (_, _) => RefreshDate();

        BuildLayout();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
        SizeChanged += (_, _) => ApplyPalette();
        ApplyPalette();
    }

    private static List<string> ReadHabits(JsonElement? config) =>
        SplitHabits(ReadString(config, "habits", ComponentDefaults.Habits));

    // 逗号分隔 6 个
    private static List<string> SplitHabits(string raw)
    {
        var list = new List<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (list.Count >= 6)
            {
                break;
            }
            if (part.Length > 0 && !list.Contains(part))
            {
                list.Add(part);
            }
        }
        return list;
    }

    private void BuildLayout()
    {
        _title.Text = "习惯打卡";
        _title.FontSize = WidgetType.Title;
        _title.FontWeight = WidgetWeight.Title;
        _title.VerticalAlignment = VerticalAlignment.Center;

        _range.FontSize = WidgetType.Micro;
        _range.FontWeight = WidgetWeight.Body;
        _range.VerticalAlignment = VerticalAlignment.Center;
        _range.HorizontalAlignment = HorizontalAlignment.Right;
        _range.MaxLines = 1;
        _range.TextTrimming = TextTrimming.CharacterEllipsis;

        var head = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = WidgetSpace.Gap };
        head.Children.Add(_title);
        Grid.SetColumn(_range, 1);
        head.Children.Add(_range);

        var weekHead = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse($"{NameColumn},*,*,*,*,*,*,*"),
            ColumnSpacing = WidgetSpace.GapXs,
        };
        for (var i = 0; i < WeekLabels.Length; i++)
        {
            var label = new TextBlock
            {
                Text = WeekLabels[i],
                FontSize = 9.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            _dayLabels.Add(label);
            Grid.SetColumn(label, i + 1);
            weekHead.Children.Add(label);
        }

        _footer.FontSize = WidgetType.Micro;
        _footer.FontWeight = WidgetWeight.Body;

        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,*,Auto"),
            RowSpacing = 2,
            Margin = new Thickness(10),
        };
        Grid.SetRow(head, 0);
        Grid.SetRow(weekHead, 1);
        Grid.SetRow(_rows, 2);
        Grid.SetRow(_footer, 3);
        root.Children.Add(head);
        root.Children.Add(weekHead);
        root.Children.Add(_rows);
        root.Children.Add(_footer);
        Card.Child = root;
        Content = Card;
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _title.Foreground = CardInk.Fg(p);
        _range.Foreground = CardInk.Faint(p);
        _footer.Foreground = CardInk.Muted(p);
        for (var i = 0; i < _dayLabels.Count; i++)
        {
            var today = i == TodayIndex;
            _dayLabels[i].Foreground = today ? CardAccent.Brush() : CardInk.Muted(p);
            _dayLabels[i].FontWeight = today ? WidgetWeight.Title : WidgetWeight.Body;
        }
        RenderRows();
    }

    // 可见行数
    private int VisibleRowCount()
    {
        var budget = (Bounds.Height > 20 ? Bounds.Height : Height) - 74;
        return Math.Clamp((int)(budget / RowHeight), 1, _habits.Count);
    }

    private void RenderRows()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var weekStart = WeekStart;
        var todayIndex = TodayIndex;
        _range.Text = $"{weekStart:MM.dd}-{weekStart.AddDays(6):MM.dd}";
        _rows.Children.Clear();
        var visible = Math.Min(_habits.Count, VisibleRowCount());
        for (var i = 0; i < visible; i++)
        {
            _rows.Children.Add(BuildRow(_habits[i], weekStart, todayIndex, p));
        }
        var more = _habits.Count > visible ? $" · 另有 {_habits.Count - visible} 项" : "";
        _footer.Text = $"连续 {MaxStreak()} 天{more}";
    }

    private Control BuildRow(string habit, DateTime weekStart, int todayIndex, WidgetSurfacePalette p)
    {
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse($"{NameColumn},*,*,*,*,*,*,*"),
            ColumnSpacing = WidgetSpace.GapXs,
            Height = CellSize,
        };

        var name = new TextBlock
        {
            Text = habit,
            FontSize = WidgetType.Label,
            FontWeight = WidgetWeight.Emphasis,
            Foreground = CardInk.Fg(p),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        grid.Children.Add(name);

        for (var i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i);
            var marked = _marks.Contains(MarkKey(habit, date));
            var today = i == todayIndex;
            var cell = new Border
            {
                Width = CellSize,
                Height = CellSize,
                CornerRadius = new CornerRadius(5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = marked ? CardAccent.Brush() : new SolidColorBrush(p.Gutter),
                BorderThickness = new Thickness(marked ? 0 : 1),
                BorderBrush = today
                    ? CardAccent.Brush(0.55)
                    : new SolidColorBrush(p.Line),
                Child = new TextBlock
                {
                    Text = AppIcons.Check,
                    FontFamily = AppIcons.Family,
                    FontSize = 10,
                    Foreground = CardAccent.Ink(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsVisible = marked,
                    IsHitTestVisible = false,
                },
            };
            cell.PointerPressed += (_, e) =>
            {
                if (IsDraggable || !e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed)
                {
                    return;
                }
                _pressedCell = cell;
                e.Pointer.Capture(cell);
                e.Handled = true;
            };
            cell.PointerCaptureLost += (_, _) =>
            {
                if (ReferenceEquals(_pressedCell, cell))
                {
                    _pressedCell = null;
                }
            };
            cell.PointerReleased += (_, e) =>
            {
                if (!ReferenceEquals(_pressedCell, cell))
                {
                    return;
                }
                _pressedCell = null;
                e.Pointer.Capture(null);
                var local = e.GetPosition(cell);
                if (local.X >= 0 && local.Y >= 0 && local.X <= cell.Bounds.Width && local.Y <= cell.Bounds.Height)
                {
                    Toggle(habit, date);
                }
                e.Handled = true;
            };
            Grid.SetColumn(cell, i + 1);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private void Toggle(string habit, DateTime date)
    {
        if (IsDraggable)
        {
            return;
        }
        var key = MarkKey(habit, date);
        if (!_marks.Remove(key))
        {
            _marks.Add(key);
        }
        RenderRows();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static string MarkKey(string habit, DateTime date) => $"{habit}|{date:yyyy-MM-dd}";

    // 最长连续
    private int MaxStreak()
    {
        var today = DateTime.Today;
        var best = 0;
        foreach (var habit in _habits)
        {
            var day = _marks.Contains(MarkKey(habit, today)) ? today : today.AddDays(-1);
            var count = 0;
            while (_marks.Contains(MarkKey(habit, day)))
            {
                count++;
                day = day.AddDays(-1);
            }
            best = Math.Max(best, count);
        }
        return best;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _dateTimer.Start();
        if (_dataFile is null && !string.IsNullOrEmpty(ComponentId))
        {
            _dataFile = IOPath.Combine(Paths.DataUser, "habit", $"{ComponentId}.json");
            Load();
            RenderRows();
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _dateTimer.Stop();
        _saveTimer.Stop();
        Save();
        base.OnDetachedFromVisualTree(e);
    }

    // 跨日刷新
    private void RefreshDate()
    {
        if (DateTime.Today == _renderedDate)
        {
            return;
        }
        _renderedDate = DateTime.Today;
        ApplyPalette();
    }

    private void Load()
    {
        try
        {
            if (_dataFile is null || !File.Exists(_dataFile))
            {
                return;
            }
            if (JsonNode.Parse(File.ReadAllText(_dataFile)) is not JsonObject obj || obj["marks"] is not JsonArray arr)
            {
                return;
            }
            _marks.Clear();
            foreach (var node in arr)
            {
                var mark = (string?)node;
                if (!string.IsNullOrEmpty(mark))
                {
                    _marks.Add(mark);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[Habit] 加载打卡记录失败: {e.Message}");
        }
    }

    private void Save()
    {
        try
        {
            if (_dataFile is null)
            {
                return;
            }
            var arr = new JsonArray();
            foreach (var mark in _marks)
            {
                arr.Add(mark);
            }
            Directory.CreateDirectory(IOPath.GetDirectoryName(_dataFile)!);
            File.WriteAllText(_dataFile, new JsonObject { ["marks"] = arr }.ToJsonString());
        }
        catch (Exception e)
        {
            Log.Warning($"[Habit] 保存打卡记录失败: {e.Message}");
        }
    }
}
