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

// CardChrome
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Glimpseon.UI;

/// <summary>条形进度</summary>
internal sealed class MeterBar : Control
{
    private double _value;
    private Color _fill = Colors.Gray;
    private Color _track = Color.FromArgb(36, 128, 128, 128);

    public MeterBar()
    {
        Height = 6;
        IsHitTestVisible = false;
    }

    public void Set(double value, Color fill, Color track)
    {
        _value = Math.Clamp(value, 0, 100);
        _fill = fill;
        _track = track;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0.5 || h <= 0.5)
        {
            return;
        }
        var radius = h / 2;
        context.DrawRectangle(new SolidColorBrush(_track), null, new Rect(0, 0, w, h), radius, radius);
        var filled = w * _value / 100.0;
        if (filled > 0.5)
        {
            context.DrawRectangle(new SolidColorBrush(_fill), null, new Rect(0, 0, filled, h), radius, radius);
        }
    }
}

/// <summary>环形进度</summary>
internal sealed class ArcMeter : Control
{
    private double _value;
    private double _thickness = 6;
    private Color _fill = Colors.Gray;
    private Color _track = Color.FromArgb(36, 128, 128, 128);

    public void Set(double value, Color fill, Color track, double thickness = 6)
    {
        _value = Math.Clamp(value, 0, 100);
        _fill = fill;
        _track = track;
        _thickness = Math.Max(2, thickness);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 8)
        {
            return;
        }
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = side / 2 - _thickness / 2 - 1;
        context.DrawEllipse(null, new Pen(new SolidColorBrush(_track), _thickness), center, radius, radius);

        var percent = Math.Clamp(_value, 0, 100);
        if (percent <= 0.2)
        {
            return;
        }
        var sweep = Math.Min(359.9, percent / 100.0 * 360.0);
        var startAngle = -Math.PI / 2;
        var endAngle = startAngle + sweep * Math.PI / 180.0;
        var start = new Point(center.X + radius * Math.Cos(startAngle), center.Y + radius * Math.Sin(startAngle));
        var end = new Point(center.X + radius * Math.Cos(endAngle), center.Y + radius * Math.Sin(endAngle));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false);
            ctx.ArcTo(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, isStroked: true);
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(_fill), _thickness)
        {
            LineCap = PenLineCap.Round,
        }, geometry);
    }
}

/// <summary>卡片公共排版</summary>
internal static class CardChrome
{
    public static Border Dot(double size = 7, Color? color = null) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = new SolidColorBrush(color ?? CardAccent.Value),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Title(string text) => new()
    {
        Text = text,
        FontSize = WidgetType.Title,
        FontWeight = WidgetWeight.Title,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>占用越高越警示</summary>
    public static Color Level(double percent) =>
        percent >= 90 ? Color.Parse("#e5484d") : percent >= 70 ? Color.Parse("#f0a33a") : CardAccent.Value;
}
