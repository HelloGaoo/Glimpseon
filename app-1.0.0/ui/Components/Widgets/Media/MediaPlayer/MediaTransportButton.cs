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

// MediaTransportButton
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

// 媒体控制圆键: 不继承 Fluent Button 模板, 避免悬停时绿色的播放键被主题灰底盖掉
internal sealed class MediaTransportButton : Border
{
    private readonly TextBlock _glyph = new();
    private readonly bool _accent;
    private bool _hover;
    private bool _pressed;
    private Color _accentColor = Color.Parse("#68C31F");
    private Color _glyphColor = Colors.White;

    public event Action? Clicked;

    public MediaTransportButton(bool accent)
    {
        _accent = accent;
        Child = _glyph;
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);
        _glyph.FontFamily = FAIcons.SymbolsFont;
        _glyph.HorizontalAlignment = HorizontalAlignment.Center;
        _glyph.VerticalAlignment = VerticalAlignment.Center;
        _glyph.TextAlignment = TextAlignment.Center;
        _glyph.IsHitTestVisible = false;
    }

    public string Glyph
    {
        get => _glyph.Text ?? "";
        set => _glyph.Text = value;
    }

    public void Configure(double size, double glyphSize, Color accent, Color glyphColor)
    {
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(size / 2);
        _glyph.FontSize = glyphSize;
        _accentColor = accent;
        _glyphColor = glyphColor;
        Refresh();
    }

    private void Refresh()
    {
        if (_accent)
        {
            Background = new SolidColorBrush(_accentColor, _pressed ? 0.82 : 1.0);
            _glyph.Foreground = new SolidColorBrush(_glyphColor);
            BoxShadow = _hover
                ? new BoxShadows(new BoxShadow
                {
                    Blur = 14,
                    Color = Color.FromArgb(110, _accentColor.R, _accentColor.G, _accentColor.B),
                })
                : default;
        }
        else
        {
            Background = _hover ? new SolidColorBrush(_glyphColor, 0.12) : Brushes.Transparent;
            _glyph.Foreground = new SolidColorBrush(_glyphColor, _pressed ? 1.0 : _hover ? 0.95 : 0.72);
            BoxShadow = default;
        }
        RenderTransformOrigin = RelativePoint.Center;
        RenderTransform = _pressed ? new ScaleTransform(0.92, 0.92) : null;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        Refresh();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        _pressed = false;
        e.Pointer.Capture(null);
        Refresh();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _pressed = true;
        e.Pointer.Capture(this);
        Refresh();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var fire = _pressed;
        _pressed = false;
        e.Pointer.Capture(null);
        Refresh();
        if (fire)
        {
            var p = e.GetPosition(this);
            if (p.X >= 0 && p.Y >= 0 && p.X <= Bounds.Width && p.Y <= Bounds.Height)
            {
                Clicked?.Invoke();
            }
        }
        e.Handled = true;
    }
}
