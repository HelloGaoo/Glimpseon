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

// NativeSquareClock1Widget
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

/// <summary>
/// 方形钟表 I(模拟指针)
/// 400x400 视口: 三档刻度/数字/三针+阴影/秒针金 #C9A66B; 不自绘表盘底 走 WidgetCardBase 卡片表面
/// </summary>
public sealed class NativeSquareClock1Widget : WidgetCardBase
{
    private readonly ClockFaceControl _face;

    public NativeSquareClock1Widget(ComponentDefinition definition) : base(definition)
    {
        _face = new ClockFaceControl(this);
        Card.Child = _face;
        Content = Card;
        ActualThemeVariantChanged += (_, _) => _face.InvalidateVisual();
    }

    private bool Dark() => CardIsDark;

    private sealed class ClockFaceControl : Control
    {
        private readonly NativeSquareClock1Widget _owner;
        private double _hourAngle;
        private double _minuteAngle;
        private double _secondAngle;
        private readonly DispatcherTimer _timer;

        public ClockFaceControl(NativeSquareClock1Widget owner)
        {
            _owner = owner;
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => TickAngles();
            AttachedToVisualTree += (_, _) => _timer.Start();
            DetachedFromVisualTree += (_, _) => _timer.Stop();
        }

        private void TickAngles()
        {
            var now = DateTime.Now;
            var s = now.Second + now.Millisecond / 1000.0;
            var m = now.Minute + s / 60.0;
            var h = now.Hour % 12 + m / 60.0;
            _hourAngle = h * 30;
            _minuteAngle = m * 6;
            _secondAngle = s * 6;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) => new(
            double.IsFinite(availableSize.Width) ? availableSize.Width : 400,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 400);

        // 刻度(内径,外径,线宽) 逐值取自模板字面表
        private static (double R1, double R2, double W) TickSpec(int angle)
        {
            var rel = angle % 90;
            if (rel == 0)
            {
                return (137, 177, 3);
            }
            if (rel is 30 or 60)
            {
                return (157.5, 207.5, 3);
            }
            return rel switch
            {
                6 or 84 => (158.1, 178.1, 1.5),
                12 or 78 => (161.4, 181.4, 1.5),
                18 or 72 => (167.1, 187.1, 1.5),
                24 or 66 => (175.6, 195.6, 1.5),
                36 or 54 => (203.5, 223.5, 1.5),
                42 or 48 => (219.1, 239.1, 1.5),
                _ => (158.1, 178.1, 1.5),
            };
        }

        private static Point Rotate(double r, double deg)
        {
            var rad = deg * Math.PI / 180.0;
            return new Point(200 + Math.Sin(rad) * r, 200 - Math.Cos(rad) * r);
        }

        // 表盘(刻度+数字)离屏缓存 尺寸/主题变更才重建 每帧只重绘指针
        private RenderTargetBitmap? _dial;
        private PixelSize _dialPixels;
        private bool _dialDark;
        private bool _dialUnavailable;

        public override void Render(DrawingContext ctx)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 10 || h < 10)
            {
                return;
            }
            var dark = _owner.Dark();
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            var dial = _dialUnavailable ? null : DialLayer(w, h, dark, scale);
            var s = Math.Min(w, h) / 400.0;
            var matrix = Matrix.CreateTranslation(-200, -200)
                         * Matrix.CreateScale((float)s, (float)s)
                         * Matrix.CreateTranslation((float)(w / 2), (float)(h / 2));
            using (ctx.PushTransform(matrix))
            {
                if (dial is null)
                {
                    RenderFace(ctx);
                    return;
                }
                ctx.DrawImage(dial, new Rect(0, 0, 400, 400));
                RenderHands(ctx, dark);
            }
        }

        private RenderTargetBitmap? DialLayer(double w, double h, bool dark, double scale)
        {
            // 2x 超采样 数字刻度缩放后不留彩边
            var sample = Math.Max(1.0, Math.Round(scale * 2));
            var px = new PixelSize(
                Math.Max(1, (int)Math.Round(w * sample)),
                Math.Max(1, (int)Math.Round(h * sample)));
            if (_dial is not null && _dialPixels == px && _dialDark == dark)
            {
                return _dial;
            }
            try
            {
                _dial?.Dispose();
                var layer = new RenderTargetBitmap(px, new Vector(96, 96));
                var ps = Math.Min(px.Width, px.Height) / 400.0;
                var matrix = Matrix.CreateTranslation(-200, -200)
                             * Matrix.CreateScale((float)ps, (float)ps)
                             * Matrix.CreateTranslation((float)(px.Width / 2.0), (float)(px.Height / 2.0));
                using (var dc = layer.CreateDrawingContext(true))
                using (dc.PushTransform(matrix))
                {
                    RenderDial(dc, dark);
                }
                _dial = layer;
                _dialPixels = px;
                _dialDark = dark;
                return _dial;
            }
            catch (Exception e)
            {
                Glimpseon.Core.Log.Warning($"表盘缓存层创建失败 回退逐帧重绘: {e.Message}");
                _dial?.Dispose();
                _dial = null;
                _dialUnavailable = true;
                return null;
            }
        }

        private void RenderFace(DrawingContext ctx)
        {
            var dark = _owner.Dark();
            RenderDial(ctx, dark);
            RenderHands(ctx, dark);
        }

        // 静态部分
        private void RenderDial(DrawingContext ctx, bool dark)
        {
            var tickMajor = NativeTheme.Parse(dark ? "#ffffff" : "#1d1d1f");
            var tickMinor = NativeTheme.Parse(dark ? "#888888" : "#777777");
            var ink = NativeTheme.Parse(dark ? "#ffffff" : "#1d1d1f");

            // 不自绘整块表盘底色 交给 WidgetCardBase 的卡片表面(毛玻璃/纯色/透明度)
            // 刻度
            for (var a = 0; a < 360; a += 6)
            {
                var (r1, r2, w) = TickSpec(a);
                var major = a % 90 == 0 || a % 90 is 30 or 60;
                var pen = new Pen(NativeTheme.Brush(major ? tickMajor : tickMinor), w);
                ctx.DrawLine(pen, Rotate(r1, a), Rotate(r2, a));
            }

            // 数字 12/3/6/9: 50px w500 基线 y+0.35em
            var typeface = new Typeface(Common.AppFontFamily, FontStyle.Normal, FontWeight.Medium);
            DrawNumeral(ctx, typeface, "12", 200, 80, ink);
            DrawNumeral(ctx, typeface, "3", 320, 200, ink);
            DrawNumeral(ctx, typeface, "6", 200, 320, ink);
            DrawNumeral(ctx, typeface, "9", 80, 200, ink);
        }

        // 每帧重绘部分: 时/分/秒针 + 中轴
        private void RenderHands(DrawingContext ctx, bool dark)
        {
            var ink = dark ? InkDarkBrush : InkLightBrush;

            DrawHand(ctx, ink, HourHandGeo, _hourAngle, shadow: true);
            DrawHand(ctx, ink, MinuteHandGeo, _minuteAngle, shadow: true);

            // 秒针: (0,28)-(0,-158) + 尾 (0,0)-(0,32) 圆帽
            using (ctx.PushTransform(Matrix.CreateRotation(_secondAngle * Math.PI / 180.0) * Matrix.CreateTranslation(200, 200)))
            {
                ctx.DrawLine(SecondPen, new Point(0, 28), new Point(0, -158));
                ctx.DrawLine(SecondPen, new Point(0, 0), new Point(0, 32));
            }

            // 中轴: 金色圆环 中心留空透出卡片表面 不再画纯色圆点
            ctx.DrawEllipse(null, SecondHubPen, new Point(200, 200), 6, 6);
        }

        private static void DrawNumeral(DrawingContext ctx, Typeface typeface, string text, int cx, int cy, Color color)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, 50, NativeTheme.Brush(color));
            ctx.DrawText(ft, new Point(cx - ft.WidthIncludingTrailingWhitespace / 2, cy + 17.5 - ft.Baseline));
        }

        // 固定几何与刷子提为静态 每帧只做旋转变换 免逐帧新建 PathGeometry/刷子
        // 尾端只留 2~3 单位 收进中轴金环内 透明表盘下不会露出白色小旗
        private static readonly PathGeometry HourHandGeo = BuildHand(new Point(-4, 2.5),
            [new Point(-3, -72), new Point(0, -82), new Point(3, -72), new Point(4, 2.5)]);

        private static readonly PathGeometry MinuteHandGeo = BuildHand(new Point(-3, 3),
            [new Point(-2.5, -118), new Point(0, -128), new Point(2.5, -118), new Point(3, 3)]);

        private static readonly IBrush ShadowDarkBrush = new SolidColorBrush(Colors.Black, 0.28);
        private static readonly IBrush ShadowLightBrush = new SolidColorBrush(Colors.Black, 0.12);
        private static readonly IBrush InkDarkBrush = NativeTheme.Brush(NativeTheme.Parse("#ffffff"));
        private static readonly IBrush InkLightBrush = NativeTheme.Brush(NativeTheme.Parse("#1d1d1f"));
        private static readonly Pen SecondPen = new(NativeTheme.Brush(NativeTheme.Parse("#C9A66B")), 2, null,
            PenLineCap.Round, PenLineJoin.Round);
        private static readonly Pen SecondHubPen = new(NativeTheme.Brush(NativeTheme.Parse("#C9A66B")), 2.6, null,
            PenLineCap.Round, PenLineJoin.Round);

        private static PathGeometry BuildHand(Point start, Point[] pts)
        {
            var fig = new PathFigure { StartPoint = start, IsClosed = true, IsFilled = true };
            foreach (var p in pts)
            {
                fig.Segments!.Add(new LineSegment { Point = p });
            }
            var geo = new PathGeometry();
            geo.Figures!.Add(fig);
            return geo;
        }

        private void DrawHand(DrawingContext ctx, IBrush brush, PathGeometry geo, double angle, bool shadow)
        {
            var matrix = Matrix.CreateRotation(angle * Math.PI / 180.0) * Matrix.CreateTranslation(200, 200);
            using (ctx.PushTransform(matrix))
            {
                if (shadow)
                {
                    // 原版 feDropShadow dy=1.5 blur=2 opacity .5/.18: 半透明偏移形近似
                    using (ctx.PushTransform(Matrix.CreateTranslation(0, 1.5)))
                    {
                        ctx.DrawGeometry(_owner.Dark() ? ShadowDarkBrush : ShadowLightBrush, null, geo);
                    }
                }
                ctx.DrawGeometry(brush, null, geo);
            }
        }
    }
}
