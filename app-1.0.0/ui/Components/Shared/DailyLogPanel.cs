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

// DailyLogPanel
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

/// <summary>归档浏览器</summary>
public sealed class DailyLogPanel : Grid
{
    private static readonly AvColor[] Palette =
    [
        NativeTheme.Parse("#f59e0b"), NativeTheme.Parse("#3b82f6"), NativeTheme.Parse("#10b981"), NativeTheme.Parse("#ef4444"),
        NativeTheme.Parse("#8b5cf6"), NativeTheme.Parse("#ec4899"), NativeTheme.Parse("#06b6d4"), NativeTheme.Parse("#84cc16"),
    ];
    private const double FontTitle = WidgetType.Headline;
    private const double FontBody = WidgetType.Body;
    private const double FontMeta = WidgetType.Label;
    private const double FontMicro = WidgetType.Micro;
    private const double LineHeight = 19;
    private static FontFamily NumFont => Common.AppFontFamily;

    private readonly string _kind;
    private readonly ListBox _dates = new();
    private readonly ContentControl _pane = new();
    private readonly StackPanel _emptyStack;
    private readonly TextBlock _datesEmpty = new();
    private readonly Border _dateHost;
    private readonly Border _paneHost;
    private List<(DateOnly Day, JsonObject Root)> _days = new();

    /// <summary>作业板快照</summary>
    public static DailyLogPanel ForHomework(string componentId) =>
        new(DailyLog.HomeworkPrefix + componentId);

    /// <summary>公告快照</summary>
    public static DailyLogPanel ForAnnouncements() =>
        new("announcements");

    private static TextBlock Txt(string text, double size, AvColor color, FontWeight? weight = null, FontFamily? family = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = family ?? Common.AppFontFamily,
            Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (weight is { } w)
        {
            tb.FontWeight = w;
        }
        return tb;
    }

    private DailyLogPanel(string kind)
    {
        _kind = kind;
        ColumnDefinitions = new ColumnDefinitions("208,12,*");
        Avalonia.Controls.Documents.TextElement.SetFontFamily(this, Common.AppFontFamily);
        Avalonia.Controls.Documents.TextElement.SetFontSize(this, WidgetType.Body);

        _dates.SelectionChanged += (_, _) => ShowSelected();
        _dates.Background = Brushes.Transparent;
        _dates.BorderThickness = new Thickness(0);
        _dates.Padding = new Thickness(0, WidgetSpace.GapXs, 0, WidgetSpace.GapXs);
        var dateScroll = new ScrollViewer
        {
            Content = _dates,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _datesEmpty.FontSize = FontMeta;
        _datesEmpty.FontFamily = Common.AppFontFamily;
        _datesEmpty.TextAlignment = TextAlignment.Center;
        _datesEmpty.HorizontalAlignment = HorizontalAlignment.Center;
        _datesEmpty.VerticalAlignment = VerticalAlignment.Center;
        _datesEmpty.IsHitTestVisible = false;
        var dateGrid = new Grid();
        dateGrid.Children.Add(dateScroll);
        dateGrid.Children.Add(_datesEmpty);
        _dateHost = new Border
        {
            Child = dateGrid,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };

        // 空态
        var emptyIcon = FAIcons.Glyph(AppIcons.Calendar, 26);
        emptyIcon.Opacity = 0.22;
        emptyIcon.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyStack = new StackPanel
        {
            Spacing = WidgetSpace.Gap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            IsVisible = false,
            Children =
            {
                emptyIcon,
                new TextBlock
                {
                    Text = AppUtils.Tr("dailylog.empty"),
                    FontSize = FontMeta,
                    FontFamily = Common.AppFontFamily,
                    TextAlignment = TextAlignment.Center,
                    Opacity = 0.45,
                },
            },
        };

        var paneScroll = new ScrollViewer
        {
            Content = _pane,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var paneGrid = new Grid();
        paneGrid.Children.Add(paneScroll);
        paneGrid.Children.Add(_emptyStack);
        _paneHost = new Border
        {
            Child = paneGrid,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };

        Children.Add(_dateHost);
        Children.Add(_paneHost);
        SetColumn(_dateHost, 0);
        SetColumn(_paneHost, 2);

        ApplyTheme();
        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyTheme();
            RebuildDates();
        };
        RebuildDates();
    }

    // 弹层不随卡片底色 仍按窗口主题
    private bool IsDarkNow => ThemeSense.IsDark(this);

    private void ApplyTheme()
    {
        var hairline = new SolidColorBrush(IsDarkNow
            ? AvColor.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)
            : AvColor.FromArgb(0x12, 0x00, 0x00, 0x00));
        _dateHost.BorderBrush = hairline;
        _paneHost.BorderBrush = hairline;
        _datesEmpty.Foreground = new SolidColorBrush(CardTheme.T("empty", IsDarkNow));
        _datesEmpty.Text = AppUtils.Tr("dailylog.empty");
    }

    private void RebuildDates()
    {
        _days = DailyLog.LoadDays(_kind);
        _dates.Items.Clear();
        foreach (var entry in _days)
        {
            _dates.Items.Add(BuildDateItem(entry.Day, entry.Root));
        }
        _datesEmpty.IsVisible = _days.Count == 0;
        if (_days.Count > 0)
        {
            _dates.SelectedIndex = 0;
        }
        else
        {
            _pane.Content = null;
            _emptyStack.IsVisible = true;
        }
    }

    private Control BuildDateItem(DateOnly d, JsonObject root)
    {
        var dark = IsDarkNow;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = WidgetSpace.Gap,
            MinHeight = 46,
            Margin = new Thickness(16, 0, 16, 0),
        };
        var day = Txt(d.ToString("MM-dd"), FontTitle, CardTheme.T("ink", dark), FontWeight.Medium, NumFont);
        var rel = Txt(DayLabel(d), FontMeta, CardTheme.T("sub", dark));
        var count = CountEntries(root.TryGetPropertyValue(_kind, out var node) ? node : null);
        var chip = new Border
        {
            Child = Txt(count.ToString(), FontMicro, CardTheme.T("sub", dark), FontWeight.Medium),
            Background = new SolidColorBrush(CardTheme.T("chip", dark)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 1, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(day, 0);
        Grid.SetColumn(rel, 1);
        Grid.SetColumn(chip, 3);
        grid.Children.Add(day);
        grid.Children.Add(rel);
        grid.Children.Add(chip);
        return grid;
    }

    // 相对日
    private static string DayLabel(DateOnly d)
    {
        var today = DateOnly.FromDateTime(AppUtils.PreciseNow());
        if (d == today)
        {
            return AppUtils.Tr("dailylog.today");
        }
        if (d == today.AddDays(-1))
        {
            return AppUtils.Tr("dailylog.yesterday");
        }
        return AppUtils.Tr(AppUtils.WeekdayKey(d.DayOfWeek));
    }

    private static int CountEntries(JsonNode? payload)
    {
        if (payload is JsonArray an)
        {
            return an.Count;
        }
        if (payload is JsonObject obj && obj["sections"] is JsonArray secs)
        {
            var n = 0;
            foreach (var s in secs)
            {
                if (s is JsonObject sec && sec["items"] is JsonArray items)
                {
                    n += items.Count;
                }
            }
            return n;
        }
        return 0;
    }

    private void ShowSelected()
    {
        var index = _dates.SelectedIndex;
        if (index < 0 || index >= _days.Count)
        {
            return;
        }
        var (date, root) = _days[index];
        var payload = root.TryGetPropertyValue(_kind, out var node) ? node : null;
        _pane.Content = payload is null
            ? null
            : _kind.StartsWith(DailyLog.HomeworkPrefix, StringComparison.Ordinal)
                ? RenderHomework(date, payload)
                : RenderAnnouncements(date, payload);
        _emptyStack.IsVisible = payload is null;
    }

    private Control MakeHeader(DateOnly date, int count, string unit)
    {
        var dark = IsDarkNow;
        var head = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 12,
            Margin = new Thickness(20, 16, 20, 12),
        };
        var bar = new Border
        {
            Width = 3,
            Height = 34,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Common.ParseAccentColor(Config.ThemeColor.Value)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(Txt(date.ToString("yyyy-MM-dd"), FontTitle, CardTheme.T("ink", dark), FontWeight.Medium, NumFont));
        title.Children.Add(Txt(DayLabel(date), FontMeta, CardTheme.T("sub", dark)));
        stack.Children.Add(title);
        stack.Children.Add(Txt(AppUtils.Tr("dailylog.count", ("count", count), ("unit", unit)), FontMeta, CardTheme.T("sub", dark)));
        Grid.SetColumn(bar, 0);
        Grid.SetColumn(stack, 1);
        head.Children.Add(bar);
        head.Children.Add(stack);
        return head;
    }

    private Border MakeRule()
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(CardTheme.T("line", IsDarkNow)),
        };
    }

    private Control RenderHomework(DateOnly date, JsonNode payload)
    {
        var dark = IsDarkNow;
        var root = new StackPanel();
        var sections = payload is JsonObject obj && obj["sections"] is JsonArray arr ? arr : new JsonArray();
        var total = 0;
        foreach (var s in sections)
        {
            if (s is JsonObject sec && sec["items"] is JsonArray its)
            {
                total += its.Count;
            }
        }
        root.Children.Add(MakeHeader(date, total, AppUtils.Tr("dailylog.unit_item")));
        root.Children.Add(MakeRule());

        var columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,16,*"),
            Margin = new Thickness(20, 16, 20, 16),
        };
        var col0 = new StackPanel { Spacing = 16 };
        var col1 = new StackPanel { Spacing = 16 };
        double h0 = 0, h1 = 0;
        for (var si = 0; si < sections.Count; si++)
        {
            if (sections[si] is not JsonObject sec)
            {
                continue;
            }
            var items = sec["items"] as JsonArray ?? new JsonArray();
            var est = 28 + items.Count * LineHeight;
            if (h0 <= h1)
            {
                h0 += est;
                col0.Children.Add(BuildSectionCard(si, sec, dark));
            }
            else
            {
                h1 += est;
                col1.Children.Add(BuildSectionCard(si, sec, dark));
            }
        }
        Grid.SetColumn(col0, 0);
        Grid.SetColumn(col1, 2);
        columns.Children.Add(col0);
        columns.Children.Add(col1);
        root.Children.Add(columns);
        return root;
    }

    private Control BuildSectionCard(int si, JsonObject sec, bool dark)
    {
        var color = Palette[si % Palette.Length];
        var items = sec["items"] as JsonArray ?? new JsonArray();
        var doneCount = items.Count(o => o is JsonObject o2 && o2["done"]?.GetValue<bool>() == true);

        var card = new StackPanel { Spacing = WidgetSpace.Gap };
        var head = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"),
            ColumnSpacing = WidgetSpace.GapSm,
        };
        var dot = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var title = Txt(sec["title"]?.GetValue<string>() ?? "", FontBody, CardTheme.T("ink", dark), FontWeight.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var counter = Txt($"{doneCount}/{items.Count}", FontMicro, CardTheme.T("sub", dark), FontWeight.Medium, NumFont);
        Grid.SetColumn(dot, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(counter, 2);
        head.Children.Add(dot);
        head.Children.Add(title);
        head.Children.Add(counter);
        card.Children.Add(head);

        foreach (var it in items)
        {
            if (it is not JsonObject item)
            {
                continue;
            }
            var done = item["done"]?.GetValue<bool>() == true;
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = WidgetSpace.Gap,
                Margin = new Thickness(16, WidgetSpace.GapXs, 0, WidgetSpace.GapXs),
            };
            var box = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1.2),
                BorderBrush = new SolidColorBrush(done ? color : CardTheme.T("dotbd", dark)),
                Background = done ? new SolidColorBrush(color) : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0),
            };
            if (done)
            {
                var mark = FAIcons.IconGlyph("E73E", 9);
                mark.Foreground = Brushes.White;
                mark.HorizontalAlignment = HorizontalAlignment.Center;
                mark.VerticalAlignment = VerticalAlignment.Center;
                box.Child = mark;
            }
            var text = Txt(item["text"]?.GetValue<string>() ?? "", FontBody, CardTheme.T("ink", dark));
            text.TextWrapping = TextWrapping.Wrap;
            text.VerticalAlignment = VerticalAlignment.Top;
            text.LineHeight = LineHeight;
            text.Opacity = done ? 0.5 : 1;
            Grid.SetColumn(box, 0);
            Grid.SetColumn(text, 1);
            row.Children.Add(box);
            row.Children.Add(text);
            card.Children.Add(row);
        }
        return card;
    }

    private Control RenderAnnouncements(DateOnly date, JsonNode payload)
    {
        var dark = IsDarkNow;
        var root = new StackPanel();
        var arr = payload as JsonArray ?? new JsonArray();
        root.Children.Add(MakeHeader(date, arr.Count, AppUtils.Tr("dailylog.unit_item")));
        root.Children.Add(MakeRule());

        var list = new StackPanel { Margin = new Thickness(20, WidgetSpace.GapXs, 20, 16) };
        var rows = new List<Control>();
        foreach (var node in arr)
        {
            if (node is not JsonObject item)
            {
                continue;
            }
            var created = item["created"]?.GetValue<string>() ?? "";
            var text = item["text"]?.GetValue<string>() ?? "";
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 12,
                Margin = new Thickness(0, 10, 0, 10),
            };
            // 胶囊齐首行 高=行高
            var chip = new Border
            {
                Child = Txt(created.Length >= 16 ? created[11..16] : created, FontMicro,
                    CardTheme.T("sub", dark), FontWeight.Medium, NumFont),
                Background = new SolidColorBrush(CardTheme.T("chip", dark)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 0),
                Height = LineHeight,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var body = Txt(text, FontBody, CardTheme.T("ink", dark));
            body.TextWrapping = TextWrapping.Wrap;
            body.VerticalAlignment = VerticalAlignment.Top;
            body.LineHeight = LineHeight;
            Grid.SetColumn(chip, 0);
            Grid.SetColumn(body, 1);
            row.Children.Add(chip);
            row.Children.Add(body);
            rows.Add(row);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            list.Children.Add(rows[i]);
            if (i < rows.Count - 1)
            {
                list.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(CardTheme.T("line", dark)),
                });
            }
        }
        root.Children.Add(list);
        return root;
    }
}
