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

// RingProgress
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

// Views/RingProgress.cs

public class RingProgress : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<RingProgress, double>(nameof(Value), defaultValue: 0);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private FormattedText? _text;

    static RingProgress()
    {
        AffectsRender<RingProgress>(ValueProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var side = Math.Min(bounds.Width, bounds.Height);
        if (side <= 0)
        {
            return;
        }
        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        var radius = side / 2 - 3;
        var stroke = 4.0;

        // 背景圆环
        var trackColor = ThemeSense.IsDark(this)
            ? Color.Parse("#1FFFFFFF")
            : Color.Parse("#14000000");
        context.DrawEllipse(null, new Pen(new SolidColorBrush(trackColor), stroke), center, radius, radius);

        // 前景弧线 从 12 点方向起
        var percent = Math.Clamp(Value, 0, 100);
        if (percent > 0)
        {
            var sweep = percent / 100.0 * 360.0;
            var startAngle = -Math.PI / 2;
            var endAngle = startAngle + sweep * Math.PI / 180;
            var start = new Point(center.X + radius * Math.Cos(startAngle), center.Y + radius * Math.Sin(startAngle));
            var end = new Point(center.X + radius * Math.Cos(endAngle), center.Y + radius * Math.Sin(endAngle));
            var isLarge = sweep > 180;
            var size = new Size(radius, radius);
            var figure = new StreamGeometry();
            using (var ctx = figure.Open())
            {
                ctx.BeginFigure(start, isFilled: false);
                ctx.ArcTo(end, size, rotationAngle: 0, isLargeArc: isLarge,
                    SweepDirection.Clockwise, isStroked: true);
                ctx.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#00C853")), stroke)
            {
                LineCap = PenLineCap.Round,
            }, figure);
        }

        // 中心百分比文字
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        _text = new FormattedText(
            $"{Math.Round(percent)}%",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            13.0,
            new SolidColorBrush(Color.Parse("#00C853")));
        context.DrawText(_text, new Point(center.X - _text.WidthIncludingTrailingWhitespace / 2, center.Y - _text.Height / 2));
    }
}
