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

// TimerPill
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

/// <summary>计时器按钮/分段标签 统一圆角胶囊</summary>
internal sealed class TimerPill : Border
{
    public enum PillKind
    {
        Primary,
        Secondary,
        Tab,
    }

    private readonly TextBlock _label;
    private readonly PillKind _kind;
    private bool _hover;

    public Func<bool>? DarkProvider { get; set; }
    public bool Active { get; set; }
    public event Action? Clicked;

    public string Text
    {
        get => _label.Text ?? "";
        set => _label.Text = value;
    }

    public TimerPill(string text, PillKind kind = PillKind.Secondary, double height = 32,
        double fontSize = WidgetType.Body, double paddingX = 12)
    {
        _kind = kind;
        Height = height;
        CornerRadius = new CornerRadius(height / 2);
        Padding = new Thickness(paddingX, 0);
        Cursor = new Cursor(StandardCursorType.Hand);
        _label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _label;
        AddHandler(PointerPressedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble);
        PointerEntered += (_, _) =>
        {
            _hover = true;
            ApplyTheme();
        };
        PointerExited += (_, _) =>
        {
            _hover = false;
            ApplyTheme();
        };
        PointerCaptureLost += (_, _) =>
        {
            _hover = false;
            Opacity = 1;
            ApplyTheme();
        };
        PointerReleased += (_, e) =>
        {
            e.Handled = true;
            Opacity = 1;
            Clicked?.Invoke();
        };
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var dark = DarkProvider?.Invoke() ?? ThemeSense.IsDark(this);
        var p = WidgetSurfacePalette.Resolve(dark);
        switch (_kind)
        {
            case PillKind.Primary:
                Background = CardAccent.Brush();
                _label.Foreground = CardAccent.Ink();
                _label.FontWeight = WidgetWeight.Title;
                Opacity = _hover ? 0.86 : 1;
                break;
            case PillKind.Tab:
                Background = Active
                    ? new SolidColorBrush(p.Text, 0.09)
                    : _hover
                        ? new SolidColorBrush(p.Text, 0.055)
                        : Brushes.Transparent;
                _label.Foreground = Active || _hover ? CardInk.Fg(p) : CardInk.Muted(p);
                _label.FontWeight = Active ? WidgetWeight.Title : WidgetWeight.Body;
                break;
            default:
                Background = Active
                    ? new SolidColorBrush(p.Text, 0.12)
                    : _hover
                        ? new SolidColorBrush(p.Text, 0.085)
                        : new SolidColorBrush(p.Text, 0.055);
                _label.Foreground = Active || _hover ? CardInk.Fg(p) : CardInk.Muted(p);
                _label.FontWeight = Active ? WidgetWeight.Emphasis : WidgetWeight.Body;
                break;
        }
    }
}
