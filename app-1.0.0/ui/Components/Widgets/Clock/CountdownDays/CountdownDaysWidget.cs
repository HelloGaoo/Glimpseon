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

// CountdownDaysWidget
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

/// <summary>倒数日</summary>
public sealed class CountdownDaysWidget : WidgetCardBase
{
    private readonly Border _headerBorder = new();
    private readonly Border _footerBorder = new();
    private readonly TextBlock _headerText = new();
    private readonly TextBlock _numText = new();
    private readonly TextBlock _footerText = new();
    private readonly Grid _content;
    private readonly CardEmptyState _emptyView = new(
        FAIcons.Glyph(AppIcons.Calendar),
        AppUtils.Tr("days_matter.empty_title"),
        AppUtils.Tr("days_matter.empty_hint"));
    private string _eventName = "";
    private string _targetDate = "";
    private string _titleBg = "#F98E1B";

    private static readonly string[] WeekdayNames = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

    public CountdownDaysWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _headerText.TextAlignment = TextAlignment.Center;
        _headerText.VerticalAlignment = VerticalAlignment.Center;
        _headerBorder.Child = _headerText;
        _numText.TextAlignment = TextAlignment.Center;
        _numText.VerticalAlignment = VerticalAlignment.Center;
        _numText.HorizontalAlignment = HorizontalAlignment.Stretch;
        _footerText.TextAlignment = TextAlignment.Center;
        _footerText.VerticalAlignment = VerticalAlignment.Center;
        _footerBorder.Child = _footerText;

        // 平面三层: 事件名 / 大数字 / 日期 不再用色块
        _content = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = WidgetSpace.GapXs,
        };
        Grid.SetRow(_headerBorder, 0);
        Grid.SetRow(_numText, 1);
        Grid.SetRow(_footerBorder, 2);
        _content.Children.Add(_headerBorder);
        _content.Children.Add(_numText);
        _content.Children.Add(_footerBorder);

        var host = new Grid();
        host.Children.Add(_content);
        host.Children.Add(_emptyView);
        Card.Child = host;
        Content = Card;

        ReadConfig(config);
        ApplyCardStyle();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += (_, _) => UpdateDays();
        timer.Start();
        UpdateDays();
    }

    private void ReadConfig(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }
        _eventName = obj.TryGetProperty("event_name", out var n) ? n.GetString() ?? "" : "";
        _targetDate = obj.TryGetProperty("target_date", out var t) ? t.GetString() ?? "" : "";
        _titleBg = obj.TryGetProperty("title_bg_color", out var c) && !string.IsNullOrEmpty(c.GetString())
            ? c.GetString()! : "#F98E1B";
    }

    private void UpdateDays()
    {
        if (!DateTime.TryParse(_targetDate, out var target))
        {
            _content.IsVisible = false;
            _emptyView.IsVisible = true;
            return;
        }
        _content.IsVisible = true;
        _emptyView.IsVisible = false;
        var today = DateTime.Today;
        var diff = (target.Date - today).Days;
        var days = Math.Abs(diff);
        var dateStr = target.ToString("yyyy-MM-dd");
        var weekStr = WeekdayNames[(int)target.DayOfWeek];
        var footer = diff > 0
            ? $"{AppUtils.Tr("days_matter.target_date")}: {dateStr} {weekStr}"
            : diff == 0
                ? $"{AppUtils.Tr("days_matter.target_date")}: {dateStr} {weekStr}"
                : $"{AppUtils.Tr("days_matter.start_date")}: {dateStr} {weekStr}";
        var status = diff > 0 ? AppUtils.Tr("days_matter.still")
            : diff == 0 ? AppUtils.Tr("days_matter.today")
            : AppUtils.Tr("days_matter.past");
        var name = string.IsNullOrEmpty(_eventName) ? AppUtils.Tr("days_matter.default_title") : _eventName;
        _headerText.Text = $"{name} {status}";
        _footerText.Text = footer;

        // 原版 _fit_font_px: 72px 起步 逐减2 直到宽度可用
        var numberText = days.ToString();
        var px = 72;
        var available = Math.Max(40, Width - WidgetSpace.Pad * 2);
        while (px > 12)
        {
            var ft = new Avalonia.Media.FormattedText(
                numberText,
                System.Globalization.CultureInfo.CurrentCulture,
                Avalonia.Media.FlowDirection.LeftToRight,
                new Avalonia.Media.Typeface(Common.AppFontFamily),
                px,
                Brushes.Black);
            if (ft.WidthIncludingTrailingWhitespace <= available)
            {
                break;
            }
            px -= 2;
        }
        _numText.FontSize = px;
        _numText.FontWeight = WidgetWeight.Display;
        _numText.LetterSpacing = WidgetType.DisplaySpacing(px);
        _numText.Text = numberText;
        // 今天当天 数字染强调色 数据驱动
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _numText.Foreground = diff == 0 ? CardAccent.Brush() : CardInk.Fg(p);
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        // 平面化: 标题条/日期条透明 字色走令牌
        _headerBorder.Background = Brushes.Transparent;
        _headerBorder.CornerRadius = new CornerRadius(0);
        _footerBorder.Background = Brushes.Transparent;
        _footerBorder.CornerRadius = new CornerRadius(0);
        _headerText.Foreground = CardInk.Muted(p);
        _headerText.FontSize = WidgetType.Title;
        _headerText.FontWeight = WidgetWeight.Title;
        _headerText.LetterSpacing = 0.2;
        _footerText.Foreground = CardInk.Muted(p);
        _footerText.FontSize = WidgetType.Label;
        _emptyView.Apply(p);
    }
}
