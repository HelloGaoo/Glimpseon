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

// PlaceholderWidget
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

public sealed class PlaceholderWidget : DraggableContainer
{
    public PlaceholderWidget(ComponentDefinition definition, JsonElement? config)
    {
        Width = definition.DefaultWidth;
        Height = definition.DefaultHeight;

        var card = new Border
        {
            BorderThickness = new Thickness(1),
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = WidgetSpace.GapSm,
        };
        var homeIcon = FAIcons.Glyph(AppIcons.Home, 22);
        homeIcon.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(homeIcon);
        content.Children.Add(new TextBlock
        {
            Text = definition.DisplayName,
            FontSize = WidgetType.Body,
            FontWeight = WidgetWeight.Emphasis,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = "M6",
            FontSize = WidgetType.Micro,
            Opacity = 0.4,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        Content = new Panel { Children = { card, content } };
        _card = card;
        BindOpacity(); // 必须在 _card 赋值后(此前在赋值前调用导致 NRE)
    }

    private readonly Border _card;

    // 控件级变体判定: 构造时未挂树变体可能是 Light 挂树继承窗口 Dark 后必须重算
    // 对齐原版深色 rgba(30,30,30) 浅色 rgba(255,255,255) 全局不透明度
    private void ApplyTheme()
    {
        var dark = ThemeSense.IsDark(this);
        var op = Math.Clamp(Config.ComponentCardOpacity.Value / 100.0, 0, 1);
        _card.Background = dark
            ? new SolidColorBrush(Color.FromRgb(30, 30, 30), op)
            : new SolidColorBrush(Colors.White, op);
        _card.BorderBrush = new SolidColorBrush(dark ? Colors.White : Colors.Black, dark ? 0.10 : 0.06);
        _card.CornerRadius = new CornerRadius(Math.Max(8, Config.ComponentCardRadius.Value));
    }

    private void BindOpacity()
    {
        ApplyTheme();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        Config.ComponentCardOpacity.ValueChanged += _ => ApplyTheme();
        Config.ComponentCardRadius.ValueChanged += _ => ApplyTheme();
    }
}
