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

// TempCurve
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

/// <summary>逐小时温度曲线 对齐 FullWidget weather 的 .chart: accent 2px 圆头 + 30% 渐隐面积 + 点温标 + 当前点实心</summary>
internal sealed class TempCurve : Control
{
    private double?[] _points = Array.Empty<double?>();

    public Func<bool>? DarkProvider { get; set; }

    public void SetPoints(IReadOnlyList<double?> points)
    {
        _points = points.ToArray();
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 24 || h < 24 || _points.Length == 0)
        {
            return;
        }
        var valid = new List<(int Index, double Value)>();
        for (var i = 0; i < _points.Length; i++)
        {
            if (_points[i] is { } v)
            {
                valid.Add((i, v));
            }
        }
        if (valid.Count == 0)
        {
            return;
        }

        var dark = DarkProvider?.Invoke() ?? ThemeSense.IsDark(this);
        var p = WidgetSurfacePalette.Resolve(dark);
        var accent = CardAccent.Value;
        var min = valid.Min(x => x.Value);
        var max = valid.Max(x => x.Value);
        var span = Math.Max(1, max - min);
        const double PadX = 7, Top = 16, Bottom = 6;

        double X(int i) => PadX + i * (w - PadX * 2) / Math.Max(1, _points.Length - 1);
        double Y(double v) => Top + (1 - (v - min) / span) * (h - Top - Bottom);

        if (valid.Count >= 2)
        {
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(X(valid[0].Index), h - Bottom), isFilled: true);
                foreach (var (index, value) in valid)
                {
                    g.LineTo(new Point(X(index), Y(value)));
                }
                g.LineTo(new Point(X(valid[^1].Index), h - Bottom));
                g.EndFigure(true);
            }
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(72, accent.R, accent.G, accent.B), 0),
                    new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1),
                },
            };
            ctx.DrawGeometry(fill, null, area);

            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(new Point(X(valid[0].Index), Y(valid[0].Value)), isFilled: false);
                foreach (var (index, value) in valid.Skip(1))
                {
                    g.LineTo(new Point(X(index), Y(value)));
                }
                g.EndFigure(false);
            }
            var pen = new Pen(new SolidColorBrush(accent), 2, null, PenLineCap.Round, PenLineJoin.Round);
            ctx.DrawGeometry(null, pen, line);
        }

        var font = WidgetFonts.Ui;
        var ink = new SolidColorBrush(p.Text, WidgetInk.MutedOpacity);
        var typeface = new Typeface(font);
        foreach (var (index, value) in valid)
        {
            var x = X(index);
            var y = Y(value);
            if (index == 0)
            {
                ctx.DrawEllipse(new SolidColorBrush(accent), null, new Point(x, y), 4, 4);
            }
            else
            {
                ctx.DrawEllipse(new SolidColorBrush(p.Card), new Pen(new SolidColorBrush(accent), 2),
                    new Point(x, y), 2.5, 2.5);
            }
            var ft = new FormattedText($"{Math.Round(value)}°", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, WidgetType.Micro, ink);
            var ty = Math.Clamp(y - 4 - ft.Height, 0, Math.Max(0, h - ft.Height));
            ctx.DrawText(ft, new Point(Math.Clamp(x - ft.Width / 2, 0, Math.Max(0, w - ft.Width)), ty));
        }
    }
}
