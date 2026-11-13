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

// SelectionAdorner
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
/// 选中组件装饰
/// 只有柔和光晕(无描边) + 右下"拖动手柄"(与卡片右下角同心 半径跟随组件圆角)
/// 通过 ICustomHitTest 仅让手柄区域响应指针, 其余区域穿透(可直接拖动卡片)
/// </summary>
public sealed class SelectionAdorner : Control, Avalonia.Rendering.ICustomHitTest
{
    // 卡片四周留白: 左/上=外扩6+最粗发光半宽4 → 取12; 右/下额外容纳手柄弧 → 取26
    public const double PadL = 12;
    public const double PadT = 12;
    public const double PadR = 26;
    public const double PadB = 26;
    public const double SelOutset = 6;

    // 手柄弧相对光晕环再往外让开的距离(避免压在光晕线上)
    private const double HandleGap = 8;

    private Color _accent = Color.Parse("#30c361");
    private bool _dragging;

    /// <summary>当前选中的组件容器(手柄拖动即调整它的缩放)</summary>
    public DraggableContainer? Target { get; set; }

    /// <summary>组件卡片圆角(光晕/手柄都跟随)</summary>
    public double CardRadius { get; set; } = 12;

    public SelectionAdorner()
    {
        ZIndex = 5000;
        Cursor = new Cursor(StandardCursorType.BottomRightCorner);
    }

    public void SetAccent(Color accent)
    {
        _accent = accent;
        InvalidateVisual();
    }

    private Rect CardRect => new(
        PadL, PadT,
        Math.Max(0, Bounds.Width - PadL - PadR),
        Math.Max(0, Bounds.Height - PadT - PadB));

    // 手柄弧半径 = 组件圆角 + 外扩 + 间隙(在光晕环外侧, 与卡片右下角同心)
    private double HandleRadius => Math.Max(CardRadius, 6) + SelOutset + HandleGap;

    // 命中区 = 沿手柄弧的一条环带(右下角方向) 精准且不与下方编辑按钮抢事件
    bool Avalonia.Rendering.ICustomHitTest.HitTest(Point point)
    {
        if (Target is not { IsSelected: true })
        {
            return false;
        }
        var card = CardRect;
        var hc = new Point(card.Right - CardRadius, card.Bottom - CardRadius);
        var dx = point.X - hc.X;
        var dy = point.Y - hc.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        return dx > -6 && dy > -6 && Math.Abs(dist - HandleRadius) <= 14;
    }

    public override void Render(DrawingContext ctx)
    {
        var card = CardRect;
        if (card.Width <= 1 || card.Height <= 1)
        {
            return;
        }

        // 光晕路径 = 卡片外扩 SelOutset; 圆角 + 外扩量 → 与卡片圆角同心平行
        var o = SelOutset;
        var ringRadius = CardRadius + o;
        var ring = new Rect(card.X - o, card.Y - o, card.Width + 2 * o, card.Height + 2 * o);

        // 柔和光晕(4 层叠加 无硬描边)
        var glow = new SolidColorBrush(_accent, 60 / 255.0);
        for (var i = 4; i >= 1; i--)
        {
            ctx.DrawRectangle(null, new Pen(glow, i * 2), ring, ringRadius, ringRadius);
        }

        // 右下拖动手柄: 与卡片右下角同心 半径=组件圆角+外扩 0°~90°(屏幕 y 向下) 四分之一弧
        var hc = new Point(card.Right - CardRadius, card.Bottom - CardRadius);
        var hr = HandleRadius;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            const int div = 24;
            g.BeginFigure(new Point(hc.X + hr, hc.Y), false);
            for (var t = 1; t <= div; t++)
            {
                var a = Math.PI / 2 * t / div;
                g.LineTo(new Point(hc.X + hr * Math.Cos(a), hc.Y + hr * Math.Sin(a)));
            }
        }
        ctx.DrawGeometry(null,
            new Pen(new SolidColorBrush(_accent, 240 / 255.0), 6, null, PenLineCap.Round, PenLineJoin.Round), geo);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Target is not { IsSelected: true } t || Parent is not Visual parent)
        {
            return;
        }
        if (this.TranslatePoint(e.GetPosition(this), parent) is { } p)
        {
            t.BeginResize(p);
        }
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || Target is not { } t || Parent is not Visual parent)
        {
            return;
        }
        if (this.TranslatePoint(e.GetPosition(this), parent) is { } p)
        {
            t.UpdateResize(p);
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        Target?.EndResize();
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}
