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

// DashedRule
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

// NativeCalendarAlmanac.cs

/// <summary>虚线分隔线(等价 CSS border dashed) horizontal 或 vertical</summary>
internal sealed class DashedRule : Control
{
    public static readonly StyledProperty<bool> VerticalProperty =
        AvaloniaProperty.Register<DashedRule, bool>(nameof(Vertical));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<DashedRule, IBrush?>(nameof(Stroke));

    public bool Vertical
    {
        get => GetValue(VerticalProperty);
        set => SetValue(VerticalProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Vertical ? new Size(1, double.IsFinite(availableSize.Height) ? availableSize.Height : 100)
                 : new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 100, 1);

    public override void Render(DrawingContext ctx)
    {
        if (Stroke is null)
        {
            return;
        }
        var pen = new Pen(Stroke, 1, new DashStyle(new double[] { 2, 2 }, 0));
        if (Vertical)
        {
            ctx.DrawLine(pen, new Point(0.5, 0), new Point(0.5, Bounds.Height));
        }
        else
        {
            ctx.DrawLine(pen, new Point(0, 0.5), new Point(Bounds.Width, 0.5));
        }
    }
}
