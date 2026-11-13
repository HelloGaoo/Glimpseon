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

// CapsuleBar
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

/// <summary>胶囊进度条 段数大于 1 时改为刻度点 全库统一形态</summary>
internal sealed class CapsuleBar : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CapsuleBar, double>(nameof(Value));

    public Func<bool>? DarkProvider { get; set; }

    // 0 或 1 为整条 大于 1 按段绘制
    public int Segments { get; set; }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, 0, 1));
    }

    static CapsuleBar()
    {
        AffectsRender<CapsuleBar>(ValueProperty);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 4 || h < 1)
        {
            return;
        }
        var p = WidgetSurfacePalette.Resolve(DarkProvider?.Invoke() ?? ThemeSense.IsDark(this));
        var track = new SolidColorBrush(p.Line, 0.55);
        var accent = new SolidColorBrush(CardAccent.Value);
        var value = Math.Clamp(Value, 0, 1);

        // 20 段刻度轨迹: 段间留 3px 已过段整段填充 当前段按比例填充
        if (Segments > 1 && w >= Segments * 3)
        {
            const double gap = 3;
            var segW = (w - gap * (Segments - 1)) / Segments;
            var passed = value * Segments;
            var radius = Math.Min(h / 2, segW / 2);
            for (var i = 0; i < Segments; i++)
            {
                var x = i * (segW + gap);
                ctx.DrawRectangle(track, null, new Rect(x, 0, segW, h), radius, radius);
                var fill = Math.Clamp(passed - i, 0, 1);
                if (fill > 0)
                {
                    ctx.DrawRectangle(accent, null, new Rect(x, 0, Math.Max(1, segW * fill), h), radius, radius);
                }
            }
            return;
        }

        var r = h / 2;
        ctx.DrawRectangle(track, null, new Rect(0, 0, w, h), r, r);
        var width = value * w;
        if (width > 0)
        {
            ctx.DrawRectangle(accent, null, new Rect(0, 0, Math.Max(h, width), h), r, r);
        }
    }
}
