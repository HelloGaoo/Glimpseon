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

// HistoryTodayWidget
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

// 历史上的今天
// 规格: 360x240 边距(16,16,16,16) 间距6 头部(图标28+标题15px w600+日期11px 右) 5条富文本
// 年份12px #30c361 w700 + 标题14px 点击开链接 30min 刷新+过期缓存

public sealed class HistoryTodayWidget : WidgetCardBase
{
    private const int ItemCount = 5;

    private readonly TextBlock _titleText = new();
    private readonly TextBlock _dateText = new();
    private readonly SelectableTextBlock[] _itemLabels = new SelectableTextBlock[ItemCount];
    private readonly string[] _titles = Enumerable.Repeat("", ItemCount).ToArray();
    private readonly string[] _years = Enumerable.Repeat("", ItemCount).ToArray();
    private readonly string[] _links = Enumerable.Repeat("", ItemCount).ToArray();

    public HistoryTodayWidget(ComponentDefinition definition) : base(definition)
    {
        // 原版 addWidget(widget, 1) 每条 stretch 均分 顶部 header 后 5 条等高
        var grid = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,*,*,*,*"),
            RowSpacing = WidgetSpace.GapSm,
        };

        var header = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*,Auto"), ColumnSpacing = WidgetSpace.Gap };
        // 原版 _render_header_icon 用 FUI.HISTORY SVG 非字形
        var icon = MakeHistoryIcon();
        _titleText.Text = AppUtils.Tr("history_today.title");
        _titleText.FontSize = WidgetType.Title;
        _titleText.FontWeight = WidgetWeight.Title;
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _dateText.TextAlignment = TextAlignment.Right;
        _dateText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(_titleText, 1);
        Grid.SetColumn(_dateText, 2);
        header.Children.Add(icon);
        header.Children.Add(_titleText);
        header.Children.Add(_dateText);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        for (var i = 0; i < ItemCount; i++)
        {
            var label = new SelectableTextBlock
            {
                Text = "--",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            var idx = i;
            label.PointerReleased += (_, _) => OpenLink(idx);
            _itemLabels[i] = label;
            Grid.SetRow(label, i + 1);
            grid.Children.Add(label);
        }
        Card.Child = grid;
        Content = Card;

        ActualThemeVariantChanged += (_, _) => RenderItems();
        // 原版 _setup_periodic_refresh 30 分钟
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        timer.Tick += (_, _) => _ = RefreshAsync();
        timer.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        JsonElement? data = null;
        try
        {
            data = await HistoryService.FetchHistoryTodayAsync();
        }
        catch (Exception e)
        {

            Log.Warning($"[SW] 在线获取失败 走缓存: {e.Message}");
            // 接口失败走缓存
        }
        // 原版: 接口无数据用过期缓存
        if (data is null)
        {
            data = AppUtils.LoadCache("history_today", ignoreExpiry: true);
        }
        await Dispatcher.UIThread.InvokeAsync(() => UpdateDisplay(data));
    }

    private void UpdateDisplay(JsonElement? data)
    {
        for (var i = 0; i < ItemCount; i++)
        {
            _titles[i] = "--";
            _years[i] = "";
            _links[i] = "";
        }
        var dateText = "";
        if (data is { ValueKind: JsonValueKind.Object } root)
        {
            dateText = root.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
            if (root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
            {
                for (var i = 0; i < ItemCount && i < events.GetArrayLength(); i++)
                {
                    var item = events[i];
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    _titles[i] = item.TryGetProperty("title", out var t) ? t.GetString() ?? "--" : "--";
                    _years[i] = item.TryGetProperty("year", out var y) ? y.ToString() : "";
                    _links[i] = item.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
                }
            }
        }
        _dateText.Text = dateText;
        RenderItems();
    }

    // 富文本
    private void RenderItems()
    {
        var dark = CardIsDark;
        var p = WidgetSurfacePalette.Resolve(dark);
        _titleText.Foreground = CardInk.Fg(p);
        _dateText.Foreground = CardInk.Muted(p);
        _dateText.FontSize = WidgetType.Label;
        for (var i = 0; i < ItemCount; i++)
        {
            var label = _itemLabels[i];
            label.Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
            label.Inlines.Clear();
            if (_years[i].Length > 0)
            {
                // 年份是锚点 大号 Light 强调色 数据驱动
                label.Inlines.Add(new Avalonia.Controls.Documents.Run($"{_years[i]} · ")
                {
                    FontSize = WidgetType.Headline,
                    FontWeight = WidgetWeight.Display,
                    Foreground = CardAccent.Brush(),
                });
            }
            label.Inlines.Add(new Avalonia.Controls.Documents.Run(_titles[i])
            {
                FontSize = WidgetType.Body,
                Foreground = CardInk.Fg(p),
            });
            label.LineHeight = WidgetType.LineHeight(WidgetType.Body);
            label.MaxLines = 2;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
        }
    }

    // 头部图标
    private Control MakeHistoryIcon()
    {
        return new FAFontIcon
        {
            FontFamily = FAIcons.SymbolsFont,
            Glyph = "\uE81C",
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void OpenLink(int index)
    {
        if (index < 0 || index >= ItemCount || _links[index].Length == 0)
        {
            return;
        }
        Log.Info($"[History] 打开历史事件: {_years[index]} {_titles[index]}");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _links[index],
                UseShellExecute = true,
            });
        }
        catch (Exception e)
        {
            Log.Warning($"[History] 打开失败: {e.Message}");
        }
    }
}
