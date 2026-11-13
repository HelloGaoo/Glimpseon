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

// CalcKey
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

/// <summary>计算器按键 自绘表面 去掉 Fluent 按钮默认描边与厚底</summary>
internal sealed class CalcKey : Border
{
    private readonly TextBlock _label;
    private readonly CalcKeyKind _kind;
    private IBrush _idle = Brushes.Transparent;
    private IBrush _hover = Brushes.Transparent;
    private bool _pressed;

    public event Action? Invoked;

    public CalcKey(string text, CalcKeyKind kind)
    {
        _kind = kind;
        CornerRadius = new CornerRadius(12);
        Cursor = new Cursor(StandardCursorType.Hand);
        RenderTransformOrigin = RelativePoint.Center;
        _label = new TextBlock
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontFamily = WidgetFonts.Num,
        };
        Child = _label;

        PointerEntered += (_, _) =>
        {
            if (!_pressed)
            {
                Background = _hover;
            }
        };
        PointerExited += (_, _) =>
        {
            if (!_pressed)
            {
                Background = _idle;
            }
        };
        PointerPressed += (_, e) =>
        {
            _pressed = true;
            Background = _hover;
            RenderTransform = new ScaleTransform(0.96, 0.96);
            e.Pointer.Capture(this);
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (!_pressed)
            {
                return;
            }
            _pressed = false;
            e.Pointer.Capture(null);
            Background = _hover;
            RenderTransform = new ScaleTransform(1, 1);
            Invoked?.Invoke();
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) =>
        {
            _pressed = false;
            RenderTransform = new ScaleTransform(1, 1);
            Background = _idle;
        };
    }

    public void SetFontSize(double size) => _label.FontSize = size;

    public void Apply(WidgetSurfacePalette p, Color accent, bool dark)
    {
        var ink = p.Text;
        switch (_kind)
        {
            case CalcKeyKind.Digit:
                _idle = new SolidColorBrush(ink, dark ? 0.06 : 0.04);
                _hover = new SolidColorBrush(ink, dark ? 0.12 : 0.09);
                _label.Foreground = new SolidColorBrush(ink);
                _label.FontWeight = FontWeight.Normal;
                break;
            case CalcKeyKind.Utility:
                _idle = new SolidColorBrush(ink, dark ? 0.03 : 0.02);
                _hover = new SolidColorBrush(ink, dark ? 0.08 : 0.06);
                _label.Foreground = new SolidColorBrush(ink, CardInk.MutedOpacity);
                _label.FontWeight = FontWeight.Normal;
                break;
            case CalcKeyKind.Operator:
                _idle = new SolidColorBrush(accent, 0.12);
                _hover = new SolidColorBrush(accent, 0.20);
                _label.Foreground = new SolidColorBrush(ink);
                _label.FontWeight = FontWeight.Medium;
                break;
            case CalcKeyKind.Equals:
                _idle = new SolidColorBrush(accent);
                _hover = new SolidColorBrush(accent);
                _label.Foreground = CardAccent.Ink();
                _label.FontWeight = WidgetWeight.Title;
                break;
        }
        if (!_pressed)
        {
            Background = _idle;
        }
    }
}
