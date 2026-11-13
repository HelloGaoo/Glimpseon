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

// NativeSquareClock2Widget
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
/// 方形钟表 II(数字式 渐隐刻度)
/// 200x200 视口: 60 刻度按秒龄渐隐(R=90/max(|dx|,|dy|) 42/48°-5) HH:mm 52px Light; 不自绘表盘底
/// </summary>
public sealed class NativeSquareClock2Widget : WidgetCardBase
{
    private readonly DigitalFaceControl _face;
    private readonly bool _whiteFace;

    public NativeSquareClock2Widget(ComponentDefinition definition, JsonElement? config = null) : base(definition)
    {
        _whiteFace = ReadBool(config, "white_face", true);
        _face = new DigitalFaceControl(this);
        Card.Child = _face;
        Content = Card;
        ApplyCardStyle();
    }

    public bool WhiteFace => _whiteFace;

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        if (!_whiteFace)
        {
            return;
        }
        // 白色表盘皮肤 圆角与阴影仍由卡片表面提供
        Card.Background = new SolidColorBrush(Color.Parse("#FFFFFF"));
        Card.BorderBrush = new SolidColorBrush(Color.Parse("#E2E2E2"));
        Card.BorderThickness = new Thickness(1);
    }

    private bool Dark() => CardIsDark;

    private sealed class DigitalFaceControl : Control
    {
        private readonly NativeSquareClock2Widget _owner;
        private readonly DispatcherTimer _timer;

        public DigitalFaceControl(NativeSquareClock2Widget owner)
        {
            _owner = owner;
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
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
            if (w < 10 || h < 10)
            {
                return;
            }
            var s = Math.Min(w, h) / 200.0;
            var matrix = Matrix.CreateTranslation(-100, -100)
                         * Matrix.CreateScale((float)s, (float)s)
                         * Matrix.CreateTranslation((float)(w / 2), (float)(h / 2));
            using (ctx.PushTransform(matrix))
            {
                RenderFace(ctx);
            }
        }

        private void RenderFace(DrawingContext ctx)
        {
            var dark = _owner.Dark();
            var p = WidgetSurfacePalette.Resolve(dark);
            var ink = _owner.WhiteFace ? Color.Parse("#111111") : p.Text;

            // 60 刻度: 整点最长 5 分档次之 其余短刻度 同色不同明度
            for (var i = 0; i < 60; i++)
            {
                var a = i * 6 * Math.PI / 180.0;
                var dx = Math.Sin(a);
                var dy = -Math.Cos(a);
                var r = 94.0 / Math.Max(Math.Abs(dx), Math.Abs(dy));
                var hour = i % 15 == 0;
                var five = i % 5 == 0;
                var len = hour ? 16.0 : five ? 11.0 : 7.5;
                var width = hour ? 3.4 : five ? 2.8 : 2.2;
                var alpha = hour ? 0.90 : five ? 0.65 : 0.45;
                var pen = new Pen(new SolidColorBrush(ink, alpha), width, null, PenLineCap.Round, PenLineJoin.Round);
                ctx.DrawLine(pen,
                    new Point(100 + dx * r, 100 + dy * r),
                    new Point(100 + dx * (r - len), 100 + dy * (r - len)));
            }

            // HH:mm 窄体大数字 使用表盘专用 Bebas Neue 与竞品比例一致
            var text = DateTime.Now.ToString("HH:mm");
            var typeface = new Typeface(WidgetFonts.Display, FontStyle.Normal, FontWeight.Normal);
            var size = FitText(text, typeface, 138, 122);
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, size, new SolidColorBrush(ink));
            ctx.DrawText(ft, new Point(100 - ft.Width / 2, 100 - ft.Height / 2));
        }

        private static double FitText(string text, Typeface typeface, double maxW, double maxH)
        {
            var lo = 12.0;
            var hi = 160.0;
            for (var i = 0; i < 14; i++)
            {
                var mid = (lo + hi) / 2;
                var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, mid, Brushes.Black);
                if (ft.Width <= maxW && ft.Height <= maxH)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            return Math.Floor(lo * 10) / 10;
        }
    }
}
