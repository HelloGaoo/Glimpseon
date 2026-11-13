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

// CountdownEventWidget
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

// 倒计时
// 事件倒计时: 三层 事件名(SemiBold) / 大读数(Light 自适应) / 目标时间(muted) 1s tick

public sealed class CountdownEventWidget : WidgetCardBase
{
    private readonly TextBlock _eventText = new();
    private readonly TextBlock _daysText = new();
    private readonly TextBlock _targetText = new();
    private readonly Grid _content;
    private readonly CardEmptyState _emptyView;
    private string _eventName = "";
    private string _targetTime = "";

    public CountdownEventWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _eventText.TextAlignment = TextAlignment.Center;
        _eventText.FontWeight = WidgetWeight.Title;
        _daysText.TextAlignment = TextAlignment.Center;
        _daysText.FontWeight = WidgetWeight.Display;
        _daysText.VerticalAlignment = VerticalAlignment.Center;
        _targetText.TextAlignment = TextAlignment.Center;

        // 三层结构 大读数撑满中段
        _content = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = WidgetSpace.GapXs,
        };
        Grid.SetRow(_eventText, 0);
        Grid.SetRow(_daysText, 1);
        Grid.SetRow(_targetText, 2);
        _content.Children.Add(_eventText);
        _content.Children.Add(_daysText);
        _content.Children.Add(_targetText);

        _emptyView = new CardEmptyState(
            FAIcons.Glyph(AppIcons.Clock),
            AppUtils.Tr("countdown.no_events"),
            AppUtils.Tr("countdown.click_to_config"));

        var host = new Grid();
        host.Children.Add(_content);
        host.Children.Add(_emptyView);
        Card.Child = host;
        Content = Card;

        ReadConfig(config);
        ApplyStyle();
        Config.CountdownTextColor.ValueChanged += _ => ApplyStyle();
        Config.CountdownTextSize.ValueChanged += _ => FitDays();
        SizeChanged += (_, _) => FitDays();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => UpdateCountdown();
        timer.Start();
        UpdateCountdown();
        FitDays();
    }

    private void ReadConfig(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }
        _eventName = obj.TryGetProperty("event_name", out var n) ? n.GetString() ?? "" : "";
        _targetTime = obj.TryGetProperty("target_time", out var t) ? t.GetString() ?? "" : "";
    }

    private void ApplyStyle()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        // 旧默认红色弃用 走色板 自定义色优先
        var custom = TryParseColor(Config.CountdownTextColor.Value);
        var ink = custom is { } c && c != Color.FromRgb(255, 0, 0) ? c : p.Text;
        _eventText.Foreground = new SolidColorBrush(ink);
        _daysText.Foreground = new SolidColorBrush(ink);
        _targetText.Foreground = new SolidColorBrush(ink, CardInk.MutedOpacity);
        _eventText.FontSize = WidgetType.Body;
        _targetText.FontSize = WidgetType.Label;
        _emptyView.Apply(p);
    }

    // 大读数字号按最宽样本拟合 秒级刷新不重排
    private void FitDays()
    {
        var w = Bounds.Width - 40;
        if (w <= 1)
        {
            return;
        }
        var prefer = Math.Max(20, Config.CountdownTextSize.Value * 1.15);
        _daysText.FontSize = CardText.Fit("99天 23:59:59", Common.AppFontFamily, WidgetWeight.Display, w, prefer, 14, 120);
    }

    private void UpdateCountdown()
    {
        var hasTarget = DateTime.TryParse(_targetTime, out var target);
        if (string.IsNullOrEmpty(_eventName) || string.IsNullOrEmpty(_targetTime) || !hasTarget)
        {
            _content.IsVisible = false;
            _emptyView.IsVisible = true;
            return;
        }
        _content.IsVisible = true;
        _emptyView.IsVisible = false;
        _eventText.Text = _eventName;
        var delta = target - DateTime.Now;
        if (delta.TotalSeconds > 0)
        {
            _daysText.Text = $"{(int)delta.TotalDays}天 {delta.Hours:D2}:{delta.Minutes:D2}:{delta.Seconds:D2}";
            _targetText.Text = $"{AppUtils.Tr("days_matter.target_date")}: {target:yyyy-MM-dd HH:mm}";
        }
        else
        {
            _daysText.Text = AppUtils.Tr("countdown.expired");
            _targetText.Text = $"{target:yyyy-MM-dd HH:mm}";
        }
    }

}
