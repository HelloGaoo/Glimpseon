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

// TempRangeBar
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

/// <summary>温度区间条 共享刻度的 low→high 段 对齐 FullWidget forecast 的 .range</summary>
internal sealed class TempRangeBar : Control
{
    private double _low = double.NaN;
    private double _high = double.NaN;
    private double _min;
    private double _max;

    public void Set(double low, double high, double min, double max)
    {
        _low = low;
        _high = high;
        _min = min;
        _max = max;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 8 || h < 3 || double.IsNaN(_low) || double.IsNaN(_high))
        {
            return;
        }
        var p = WidgetSurfacePalette.Resolve(ThemeSense.IsDark(this));
        var thickness = Math.Min(h, 4);
        var y = (h - thickness) / 2;
        ctx.DrawRectangle(new SolidColorBrush(p.Line, 0.55), null, new Rect(0, y, w, thickness), thickness / 2, thickness / 2);

        var span = Math.Max(1, _max - _min);
        var x1 = Math.Clamp((_low - _min) / span, 0, 1) * w;
        var x2 = Math.Clamp((_high - _min) / span, 0, 1) * w;
        if (x2 < x1)
        {
            (x1, x2) = (x2, x1);
        }
        var segW = Math.Max(3, x2 - x1);
        if (x1 + segW > w)
        {
            x1 = Math.Max(0, w - segW);
        }
        // 温度色带: 冷端青 中段绿 暖端黄 区间按绝对温度取渐变切片
        static Color TempColor(double t)
        {
            (double P, Color C)[] stops =
            {
                (0.0, Color.Parse("#6FCFD9")),
                (0.4, Color.Parse("#9FD9B0")),
                (0.7, Color.Parse("#D4DE90")),
                (1.0, Color.Parse("#EDDF7C")),
            };
            t = Math.Clamp(t, 0, 1);
            for (var i = 1; i < stops.Length; i++)
            {
                if (t > stops[i].P)
                {
                    continue;
                }
                var a = stops[i - 1];
                var b = stops[i];
                var k = (t - a.P) / Math.Max(0.0001, b.P - a.P);
                return Color.FromRgb(
                    (byte)Math.Round(a.C.R + (b.C.R - a.C.R) * k),
                    (byte)Math.Round(a.C.G + (b.C.G - a.C.G) * k),
                    (byte)Math.Round(a.C.B + (b.C.B - a.C.B) * k));
            }
            return stops[^1].C;
        }
        var t1 = Math.Clamp((_low - _min) / span, 0, 1);
        var t2 = Math.Clamp((_high - _min) / span, 0, 1);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(TempColor(t1), 0),
                new GradientStop(TempColor((t1 + t2) / 2), 0.5),
                new GradientStop(TempColor(t2), 1),
            },
        };
        ctx.DrawRectangle(brush, null, new Rect(x1, y, segW, thickness), thickness / 2, thickness / 2);
    }
}
