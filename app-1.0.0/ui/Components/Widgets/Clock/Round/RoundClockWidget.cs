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

// RoundClockWidget
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Glimpseon.Core;


namespace Glimpseon.UI;

/// <summary>圆盘时钟</summary>
public sealed class RoundClockWidget : WidgetCardBase
{
    private readonly Dial _dial;
    private readonly bool _showSeconds;

    public RoundClockWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _showSeconds = ReadBool(config, "show_seconds", true);
        _dial = new Dial(this);
        Card.Child = _dial;
        Content = Card;

        ActualThemeVariantChanged += (_, _) => _dial.InvalidateVisual();
        AppearanceChanged += () => _dial.InvalidateVisual();
    }

    internal bool IsDark => CardIsDark;

    internal bool ShowSeconds => _showSeconds;

    // 自绘表盘
    private sealed class Dial : Control
    {
        private readonly RoundClockWidget _owner;
        private readonly DispatcherTimer _timer;

        public Dial(RoundClockWidget owner)
        {
            _owner = owner;
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(owner.ShowSeconds ? 250 : 1000),
            };
            _timer.Tick += (_, _) => InvalidateVisual();
            AttachedToVisualTree += (_, _) => _timer.Start();
            DetachedFromVisualTree += (_, _) => _timer.Stop();
        }

        protected override Size MeasureOverride(Size availableSize) => new(
            double.IsFinite(availableSize.Width) ? availableSize.Width : 200,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 200);

        public override void Render(DrawingContext ctx)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 20 || h < 20)
            {
                return;
            }

            var p = WidgetSurfacePalette.Resolve(_owner.IsDark);
            var center = new Point(w / 2, h / 2);
            var side = Math.Min(w, h);
            var radius = side / 2 - Math.Max(2, side * 0.03);

            // 刻度
            var majorBrush = new SolidColorBrush(p.Text, CardInk.MutedOpacity);
            var minorBrush = new SolidColorBrush(p.Text, CardInk.FaintOpacity);
            for (var i = 0; i < 60; i++)
            {
                var major = i % 5 == 0;
                var angle = i * 6 * Math.PI / 180.0;
                var sin = Math.Sin(angle);
                var cos = Math.Cos(angle);
                var outer = radius * 0.97;
                var inner = radius * (major ? 0.83 : 0.90);
                var width = major ? Math.Max(1.8, side * 0.007) : Math.Max(0.9, side * 0.0035);
                var pen = new Pen(major ? majorBrush : minorBrush, width, null, PenLineCap.Round);
                ctx.DrawLine(pen,
                    new Point(center.X + sin * inner, center.Y - cos * inner),
                    new Point(center.X + sin * outer, center.Y - cos * outer));
            }

            // 12/3/6/9 数字
            if (side >= 90)
            {
                var typeface = new Typeface(WidgetFonts.Ui, FontStyle.Normal, WidgetWeight.Emphasis);
                var numBrush = new SolidColorBrush(p.Text, CardInk.MutedOpacity);
                var numSize = Math.Clamp(side * 0.082, 9, 26);
                var labelR = radius * 0.70;
                DrawNumeral(ctx, "12", new Point(center.X, center.Y - labelR), typeface, numSize, numBrush);
                DrawNumeral(ctx, "3", new Point(center.X + labelR, center.Y), typeface, numSize, numBrush);
                DrawNumeral(ctx, "6", new Point(center.X, center.Y + labelR), typeface, numSize, numBrush);
                DrawNumeral(ctx, "9", new Point(center.X - labelR, center.Y), typeface, numSize, numBrush);
            }

            var now = DateTime.Now;
            var second = now.Second + now.Millisecond / 1000.0;
            var minute = now.Minute + second / 60.0;
            var hour = now.Hour % 12 + minute / 60.0;

            var ink = CardInk.Fg(p);
            DrawHand(ctx, center, hour * 30, radius * 0.50, radius * 0.14,
                Math.Max(2.4, side * 0.017), ink);
            DrawHand(ctx, center, minute * 6, radius * 0.74, radius * 0.16,
                Math.Max(1.8, side * 0.012), ink);
            if (_owner.ShowSeconds)
            {
                DrawHand(ctx, center, second * 6, radius * 0.84, radius * 0.16,
                    Math.Max(1, side * 0.006), CardAccent.Brush());
            }

            // 中心点
            var hub = Math.Max(3, side * 0.018);
            ctx.DrawEllipse(ink, null, center, hub, hub);
            if (_owner.ShowSeconds)
            {
                ctx.DrawEllipse(CardAccent.Brush(), null, center, hub * 0.45, hub * 0.45);
            }
        }

        private static void DrawNumeral(DrawingContext ctx, string text, Point at, Typeface typeface,
            double size, IBrush brush)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, size, brush);
            ctx.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
        }

        // 指针带配重
        private static void DrawHand(DrawingContext ctx, Point center, double degrees, double length,
            double tail, double width, IBrush brush)
        {
            var angle = degrees * Math.PI / 180.0;
            var sin = Math.Sin(angle);
            var cos = Math.Cos(angle);
            var start = new Point(center.X - sin * tail, center.Y + cos * tail);
            var end = new Point(center.X + sin * length, center.Y - cos * length);
            ctx.DrawLine(new Pen(brush, width, null, PenLineCap.Round), start, end);
        }
    }
}
