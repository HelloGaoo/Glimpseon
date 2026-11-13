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

// UsageChart
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

/// <summary>CPU 与内存走势 60 秒窗口 虚线网格 单强调色</summary>
internal sealed class UsageChart : Control
{
    private const int Capacity = 60;
    private readonly List<double> _cpu = new();
    private readonly List<double> _ram = new();

    public Func<bool>? DarkProvider { get; set; }

    public void Push(double cpu, double ram)
    {
        _cpu.Add(Math.Clamp(cpu, 0, 100));
        _ram.Add(Math.Clamp(ram, 0, 100));
        if (_cpu.Count > Capacity)
        {
            _cpu.RemoveAt(0);
        }
        if (_ram.Count > Capacity)
        {
            _ram.RemoveAt(0);
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 8 || h < 8)
        {
            return;
        }
        var p = WidgetSurfacePalette.Resolve(DarkProvider?.Invoke() ?? ThemeSense.IsDark(this));
        var gridPen = new Pen(new SolidColorBrush(p.Line, 0.55), 1)
        {
            DashStyle = new DashStyle(new double[] { 2, 4 }, 0),
        };
        for (var i = 1; i <= 3; i++)
        {
            var y = 6 + (h - 12) * i / 4.0;
            ctx.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }
        if (_cpu.Count < 2)
        {
            return;
        }

        var hot = _cpu[^1] >= 85;
        var accent = hot ? CardInk.Warn().Color : CardAccent.Value;
        var n = _cpu.Count;
        double X(int i) => n <= 1 ? 0 : w * i / (n - 1);
        double Y(double v) => 6 + (h - 12) * (1 - Math.Clamp(v, 0, 100) / 100.0);
        double Smoothed(List<double> v, int i) =>
            (v[Math.Max(0, i - 1)] + v[i] * 2 + v[Math.Min(v.Count - 1, i + 1)]) / 4.0;

        // CPU 面积
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(0, h - 6), false);
            for (var i = 0; i < n; i++)
            {
                g.LineTo(new Point(X(i), Y(Smoothed(_cpu, i))));
            }
            g.LineTo(new Point(w, h - 6));
            g.EndFigure(true);
        }
        var areaBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(accent, 0.24),
                new GradientStop(accent, 0.02),
            },
        };
        ctx.DrawGeometry(areaBrush, null, area);

        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 1.9)
        {
            LineJoin = PenLineJoin.Round,
        }, Polyline(n, X, Y, _cpu));
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(p.Text, 0.40), 1.3)
        {
            DashStyle = new DashStyle(new double[] { 3, 3 }, 0),
            LineJoin = PenLineJoin.Round,
        }, Polyline(n, X, Y, _ram));

        // 末端点 提示当前值
        ctx.DrawEllipse(new SolidColorBrush(accent), null, new Point(X(n - 1), Y(Smoothed(_cpu, n - 1))), 2.4, 2.4);
    }

    private static StreamGeometry Polyline(int n, Func<int, double> x, Func<double, double> y, List<double> values)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        double V(int i) => (values[Math.Max(0, i - 1)] + values[i] * 2 + values[Math.Min(values.Count - 1, i + 1)]) / 4.0;
        g.BeginFigure(new Point(x(0), y(V(0))), false);
        for (var i = 1; i < n; i++)
        {
            g.LineTo(new Point(x(i), y(V(i))));
        }
        g.EndFigure(false);
        return geo;
    }
}
