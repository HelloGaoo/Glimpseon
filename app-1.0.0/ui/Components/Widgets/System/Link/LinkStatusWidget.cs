﻿// Glimpseon
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

// LinkStatusWidget
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

/// <summary>
/// 联网状态
/// 400x200: 网卡/类型/IP/网关
/// 10 秒轮询
/// </summary>
public sealed class LinkStatusWidget : WidgetCardBase
{
    private readonly TextBlock _title;
    private readonly Border _dot;
    private readonly TextBlock _stateText;
    private readonly Grid _rows;
    private readonly TextBlock[] _labels = new TextBlock[4];
    private readonly TextBlock[] _values = new TextBlock[4];
    private readonly StackPanel _offline;
    private readonly TextBlock _offlineTitle;
    private readonly TextBlock _offlineHint;
    private readonly DispatcherTimer _timer;

    private bool _online;

    public LinkStatusWidget(ComponentDefinition definition) : base(definition)
    {
        _title = CardChrome.Title("联网状态");
        _dot = CardChrome.Dot(6);
        _stateText = new TextBlock
        {
            FontSize = WidgetType.Micro,
            FontWeight = WidgetWeight.Body,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var state = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = WidgetSpace.GapXs,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _dot, _stateText },
        };
        var head = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        head.Children.Add(_title);
        Grid.SetColumn(state, 1);
        head.Children.Add(state);

        string[] captions = { "网卡", "类型", "IP 地址", "网关" };
        _rows = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,*"),
            RowDefinitions = RowDefinitions.Parse("*,*"),
            ColumnSpacing = WidgetSpace.Pad,
            RowSpacing = WidgetSpace.GapSm,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (var i = 0; i < captions.Length; i++)
        {
            var cell = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
                ColumnSpacing = WidgetSpace.GapXs,
            };
            var label = new TextBlock
            {
                Text = captions[i],
                FontSize = WidgetType.Micro,
                FontWeight = WidgetWeight.Body,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var value = new TextBlock
            {
                Text = "—",
                FontSize = WidgetType.Label,
                FontWeight = WidgetWeight.Emphasis,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            };
            cell.Children.Add(label);
            Grid.SetColumn(value, 1);
            cell.Children.Add(value);
            Grid.SetColumn(cell, i % 2);
            Grid.SetRow(cell, i / 2);
            _rows.Children.Add(cell);
            _labels[i] = label;
            _values[i] = value;
        }

        _offlineTitle = new TextBlock
        {
            Text = "未连接网络",
            FontSize = WidgetType.Body,
            FontWeight = WidgetWeight.Emphasis,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _offlineHint = new TextBlock
        {
            Text = "请检查网线或 Wi-Fi",
            FontSize = WidgetType.Micro,
            FontWeight = WidgetWeight.Body,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        _offline = new StackPanel
        {
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Children = { _offlineTitle, _offlineHint },
        };

        var body = new Grid();
        body.Children.Add(_rows);
        body.Children.Add(_offline);

        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.GapXs,
            Margin = new Thickness(WidgetSpace.PadCompact),
        };
        root.Children.Add(head);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Card.Child = root;
        Content = Card;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => Refresh();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
        Refresh();
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

    private void Refresh()
    {
        var link = LinkService.Primary();
        if (link is not { Up: true } || string.IsNullOrWhiteSpace(link.Address))
        {
            _online = false;
            _rows.IsVisible = false;
            _offline.IsVisible = true;
        }
        else
        {
            _online = true;
            _rows.IsVisible = true;
            _offline.IsVisible = false;
            _values[0].Text = string.IsNullOrWhiteSpace(link.Name) ? "未知网卡" : link.Name;
            _values[1].Text = FormatLink(link);
            _values[2].Text = link.Address;
            _values[3].Text = string.IsNullOrWhiteSpace(link.Gateway) ? "—" : link.Gateway;
        }
        ApplyPalette();
    }

    private static string FormatLink(LinkSnapshot link)
    {
        var kind = string.IsNullOrWhiteSpace(link.Kind) ? "未知" : link.Kind;
        return link.Mbps >= 1
            ? $"{kind} · {link.Mbps.ToString("0", CultureInfo.CurrentCulture)} Mbps"
            : kind;
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _title.Foreground = CardInk.Fg(p);
        _stateText.Text = _online ? "已连接" : "离线";
        _stateText.Foreground = _online ? CardAccent.Brush() : CardInk.Faint(p);
        _dot.Background = _online
            ? CardAccent.Brush()
            : new SolidColorBrush(p.Text, 0.30);
        foreach (var label in _labels)
        {
            label.Foreground = CardInk.Faint(p);
        }
        foreach (var value in _values)
        {
            value.Foreground = CardInk.Fg(p);
        }
        _offlineTitle.Foreground = CardInk.Muted(p);
        _offlineHint.Foreground = CardInk.Faint(p);
    }
}
