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

// CardEmptyState
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

/// <summary>组件统一空态: 强调色徽标 + 标题 + 说明 + 可选操作行</summary>
internal sealed class CardEmptyState : StackPanel
{
    private readonly Border _badge = new();
    private readonly FAFontIcon _icon;
    private readonly TextBlock _title = new();
    private readonly TextBlock _hint = new();

    public StackPanel Actions { get; } = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = WidgetSpace.GapSm,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
        IsVisible = false,
    };

    public CardEmptyState(FAFontIcon icon, string title, string hint, double badgeSize = 42)
    {
        _icon = icon;
        // 只留一枚安静的字形图标 不再铺色块 避免绿底绿字
        _icon.FontSize = Math.Round(badgeSize * 0.62);
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _icon.VerticalAlignment = VerticalAlignment.Center;
        _badge.Width = badgeSize;
        _badge.Height = badgeSize;
        _badge.CornerRadius = new CornerRadius(0);
        _badge.HorizontalAlignment = HorizontalAlignment.Center;
        _badge.Child = _icon;

        _title.Text = title;
        _title.FontSize = WidgetType.Title;
        _title.FontWeight = WidgetWeight.Emphasis;
        _title.TextAlignment = TextAlignment.Center;
        _title.HorizontalAlignment = HorizontalAlignment.Center;

        _hint.Text = hint;
        _hint.FontSize = WidgetType.Label;
        _hint.TextAlignment = TextAlignment.Center;
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.MaxWidth = 240;
        _hint.HorizontalAlignment = HorizontalAlignment.Center;

        Spacing = WidgetSpace.GapSm;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_badge);
        Children.Add(_title);
        Children.Add(_hint);
        Children.Add(Actions);
    }

    public void AddAction(Control action)
    {
        Actions.Children.Add(action);
        Actions.IsVisible = true;
    }

    public void Apply(WidgetSurfacePalette p, Color? ink = null)
    {
        _badge.Background = null;
        _icon.Foreground = new SolidColorBrush(p.Text, CardInk.FaintOpacity);
        _title.Foreground = ink is { } c ? new SolidColorBrush(c) : CardInk.Fg(p);
        _hint.Foreground = ink is { } h ? new SolidColorBrush(h, CardInk.MutedOpacity) : CardInk.Muted(p);
    }
}
