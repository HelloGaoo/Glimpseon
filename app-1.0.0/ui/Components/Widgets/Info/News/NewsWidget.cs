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

// NewsWidget
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

// 新闻热榜
// 规格: 360x220 边距16 间距6 头部SVG logo高30 + 4条富文本(序号12px/标题15px) 点击开链接 5min刷新+过期缓存

public sealed class NewsWidget : WidgetCardBase
{
    private const int ItemCount = 4;

    private readonly TextBlock[] _itemLabels = new TextBlock[ItemCount];
    private readonly string[] _urls = new string[ItemCount];
    private readonly Avalonia.Svg.Skia.Svg _logo;
    private readonly string _source;
    private readonly string _iconKey;

    public NewsWidget(ComponentDefinition definition, string source) : base(definition)
    {
        _source = source;
        _iconKey = source == "cctv" ? "cctv"
            : source == "tenxunwang" ? "tencent"
            : source; // baidu/weibo/jinritoutiao

        _logo = MakeLogo(30);

        // 原版 addWidget(widget, 1) 每条 stretch 均分 顶部 header 后 4 条等高
        var grid = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,*,*,*"),
            RowSpacing = WidgetSpace.GapSm,
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        header.Children.Add(_logo);
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
            label.PointerReleased += (_, _) => OpenNews(idx);
            _itemLabels[i] = label;
            Grid.SetRow(label, i + 1);
            grid.Children.Add(label);
        }
        Card.Child = grid;
        Content = Card;

        for (var i = 0; i < ItemCount; i++)
        {
            RenderItem(i, "--");
        }
        // 原版 300000ms 定时刷新
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        timer.Tick += (_, _) => RefreshNews();
        timer.Start();
        RefreshNews();
    }

    private Avalonia.Svg.Skia.Svg MakeLogo(double height)
    {
        try
        {
            var path = Paths.GetResourcePath(System.IO.Path.Combine("Assets", "icons", "news", _iconKey + ".svg"));
            if (File.Exists(path))
            {
                return new Avalonia.Svg.Skia.Svg(new Uri(path))
                {
                    Height = height,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    SvgSource = Avalonia.Svg.Skia.SvgSource.Load(path, null),
                };
            }
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 图标缺失留空: {e.Message}");
            // 图标缺失留空
        }
        return new Avalonia.Svg.Skia.Svg(new Uri("file:///nonexistent.svg")) { Height = 0 };
    }

    private async void RefreshNews()
    {
        JsonElement? data = null;
        try
        {
            data = _source == "cctv"
                ? await NewsService.FetchCctvNewsAsync()
                : await NewsService.FetchDailyNewsAsync(_source);
        }
        catch (Exception e)
        {

            Log.Warning($"[SW] 在线获取失败 走缓存: {e.Message}");
            // 接口失败走缓存
        }
        // 原版: 接口无数据用过期缓存
        if (data is null)
        {
            data = AppUtils.LoadCache($"news_{_source}", ignoreExpiry: true);
        }
        UpdateDisplay(data);
    }

    private void UpdateDisplay(JsonElement? data)
    {
        var titles = new string[ItemCount];
        for (var i = 0; i < ItemCount; i++)
        {
            titles[i] = "--";
            _urls[i] = "";
        }
        if (data is { ValueKind: JsonValueKind.Array } arr)
        {
            for (var i = 0; i < ItemCount && i < arr.GetArrayLength(); i++)
            {
                var item = arr[i];
                titles[i] = item.TryGetProperty("title", out var t) ? t.GetString() ?? "--"
                    : item.TryGetProperty("name", out var n) ? n.GetString() ?? "--" : "--";
                _urls[i] = item.TryGetProperty("url", out var u) ? u.GetString() ?? ""
                    : item.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
            }
        }
        for (var i = 0; i < ItemCount; i++)
        {
            RenderItem(i, titles[i]);
        }
    }

    // 原版 _render_items: 序号 12px 半透明 + 标题 13px 主题对色
    private void RenderItem(int index, string title)
    {
        var dark = CardIsDark;
        var p = WidgetSurfacePalette.Resolve(dark);
        var label = _itemLabels[index];
        label.Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
            label.Inlines.Clear();
        var num = new Avalonia.Controls.Documents.Run($"{index + 1}. ")
        {
            FontSize = WidgetType.Label,
            Foreground = CardInk.Muted(p),
        };
        var text = new Avalonia.Controls.Documents.Run(title)
        {
            FontSize = WidgetType.Body,
            Foreground = CardInk.Fg(p),
        };
        label.Inlines.Add(num);
        label.Inlines.Add(text);
        label.LineHeight = WidgetType.LineHeight(WidgetType.Body);
        label.MaxLines = 2;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
    }

    private void OpenNews(int index)
    {
        if (index >= 0 && index < ItemCount && !string.IsNullOrEmpty(_urls[index]))
        {
            Log.Info($"[NewsComponent] 打开新闻: {_urls[index]}");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _urls[index],
                    UseShellExecute = true,
                });
            }
            catch (Exception e)
            {
                Log.Warning($"[NewsComponent] 打开失败: {e.Message}");
            }
        }
    }
}
