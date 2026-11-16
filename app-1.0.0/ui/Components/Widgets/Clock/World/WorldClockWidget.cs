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

// WorldClockWidget
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;

namespace Glimpseon.UI;

/// <summary>
/// 多时区
/// 400x200: 2x2 时区面板
/// 30 秒刷新
/// </summary>
public sealed class WorldClockWidget : WidgetCardBase
{
    private static readonly string[] WeekNames = { "日", "一", "二", "三", "四", "五", "六" };

    private static readonly Dictionary<string, string> CityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia/Shanghai"] = "北京",
        ["Asia/Tokyo"] = "东京",
        ["Europe/London"] = "伦敦",
        ["America/New_York"] = "纽约",
        ["Europe/Paris"] = "巴黎",
        ["America/Los_Angeles"] = "洛杉矶",
        ["Australia/Sydney"] = "悉尼",
        ["Asia/Singapore"] = "新加坡",
        ["Asia/Seoul"] = "首尔",
        ["Asia/Dubai"] = "迪拜",
    };

    private readonly List<(string Id, TimeZoneInfo Zone)> _zones = new();
    private readonly List<ZoneCell> _rows = new();
    private readonly Grid _grid = new();
    private readonly TextBlock _meta = new()
    {
        FontSize = WidgetType.Micro,
        FontWeight = WidgetWeight.Body,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _empty = new()
    {
        Text = "时区不可用",
        FontSize = WidgetType.Label,
        FontWeight = WidgetWeight.Body,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly DispatcherTimer _timer;

    public WorldClockWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        ParseZones(ReadZones(config));

        var head = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            ColumnSpacing = WidgetSpace.GapSm,
            VerticalAlignment = VerticalAlignment.Center,
        };
        head.Children.Add(CardChrome.Title("多时区"));
        Grid.SetColumn(_meta, 1);
        head.Children.Add(_meta);

        _grid.ColumnDefinitions = ColumnDefinitions.Parse("*,*");
        var rowCount = Math.Max(1, (_zones.Count + 1) / 2);
        _grid.RowDefinitions = RowDefinitions.Parse(string.Join(",", Enumerable.Repeat("*", rowCount)));
        _grid.ColumnSpacing = WidgetSpace.Gap;
        _grid.RowSpacing = WidgetSpace.Gap;
        _grid.VerticalAlignment = VerticalAlignment.Stretch;

        for (var i = 0; i < _zones.Count; i++)
        {
            var (id, zone) = _zones[i];
            var cell = new ZoneCell(CityName(id), zone, IsLocal(zone));
            _rows.Add(cell);
            Grid.SetColumn(cell.Root, i % 2);
            Grid.SetRow(cell.Root, i / 2);
            _grid.Children.Add(cell.Root);
        }

        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.PadCompact),
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.GapSm,
        };
        root.Children.Add(head);
        Grid.SetRow(_grid, 1);
        Grid.SetRow(_empty, 1);
        root.Children.Add(_grid);
        root.Children.Add(_empty);
        Card.Child = root;
        Content = Card;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Refresh();

        ApplyPalette();
        Refresh();

        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
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

    private static string ReadZones(JsonElement? config) =>
        ReadString(config, "zones", ComponentDefaults.TimeZones);

    // 时区最多 4 个
    private void ParseZones(string raw)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(','))
        {
            if (_zones.Count >= 4)
            {
                break;
            }
            var id = part.Trim();
            if (id.Length == 0 || !seen.Add(id))
            {
                continue;
            }
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                _zones.Add((id, zone));
            }
            catch (Exception e)
            {
                Log.Debug($"[WORLD_CLOCK] 时区不可用 {id}: {e.Message}");
            }
        }
    }

    private static string CityName(string id) =>
        CityNames.TryGetValue(id, out var name) ? name : id.Split('/').Last();

    private static bool IsLocal(TimeZoneInfo zone) =>
        string.Equals(zone.Id, TimeZoneInfo.Local.Id, StringComparison.OrdinalIgnoreCase)
        || TimeZoneInfo.Local.HasSameRules(zone);

    // 刷新时间
    private void Refresh()
    {
        var now = DateTimeOffset.Now;
        var today = now.LocalDateTime.Date;
        foreach (var row in _rows)
        {
            var local = TimeZoneInfo.ConvertTime(now, row.Zone);
            row.Time.Text = local.ToString("HH:mm");
            var diff = (local.Date - today).Days;
            var week = WeekNames[(int)local.DayOfWeek];
            row.Date.Text = diff switch
            {
                > 0 => $"明天 {local:M/d}",
                < 0 => $"昨天 {local:M/d}",
                _ when row.Local => $"今天 {local:M/d}",
                _ => $"{local:M/d} 周{week}",
            };
        }

        var offset = TimeZoneInfo.Local.GetUtcOffset(now);
        var abs = offset.Duration();
        _meta.Text = $"本地 UTC{(offset < TimeSpan.Zero ? "-" : "+")}{abs.Hours:00}:{abs.Minutes:00}";

        _grid.IsVisible = _zones.Count > 0;
        _empty.IsVisible = _zones.Count == 0;
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _meta.Foreground = CardInk.Faint(p);
        foreach (var row in _rows)
        {
            row.Root.Background = row.Local
                ? CardAccent.Brush(0.10)
                : new SolidColorBrush(p.Text, CardIsDark ? 0.035 : 0.025);
            row.Root.BorderBrush = row.Local
                ? CardAccent.Brush(0.26)
                : new SolidColorBrush(p.Line, 0.55);
            row.City.Foreground = row.Local ? CardAccent.Brush() : CardInk.Muted(p);
            row.Date.Foreground = CardInk.Faint(p);
            row.Time.Foreground = CardInk.Fg(p);
        }
        _empty.Foreground = CardInk.Muted(p);
    }

    // 时区面板
    private sealed class ZoneCell
    {
        public Border Root { get; }
        public TextBlock City { get; }
        public TextBlock Time { get; }
        public TextBlock Date { get; }
        public TimeZoneInfo Zone { get; }
        public bool Local { get; }

        public ZoneCell(string name, TimeZoneInfo zone, bool local)
        {
            Zone = zone;
            Local = local;

            City = new TextBlock
            {
                Text = name,
                FontSize = WidgetType.Label,
                FontWeight = WidgetWeight.Emphasis,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            };
            Date = new TextBlock
            {
                FontSize = WidgetType.Micro,
                FontWeight = WidgetWeight.Body,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                MaxLines = 1,
            };
            Time = new TextBlock
            {
                FontSize = 31,
                FontWeight = WidgetWeight.Display,
                FontFamily = WidgetFonts.Num,
                LetterSpacing = WidgetType.DisplaySpacing(31),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            var top = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
                ColumnSpacing = WidgetSpace.GapXs,
            };
            top.Children.Add(City);
            Grid.SetColumn(Date, 2);
            top.Children.Add(Date);

            var stack = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("Auto,*"),
                RowSpacing = 2,
            };
            stack.Children.Add(top);
            Grid.SetRow(Time, 1);
            stack.Children.Add(Time);

            Root = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8),
                BorderThickness = new Thickness(1),
                Child = stack,
            };
        }
    }
}