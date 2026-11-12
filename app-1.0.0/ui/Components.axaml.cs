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

// 组件系统
// 组件基类 + 30+ 具体组件 + 组件库窗口 + 配置弹窗 + 导航页全部在此文件
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

public class DraggableContainer : ContentControl
{
    private bool _draggable;
    private bool _pressed;
    private Point _pressPoint;      // 按下点(父容器坐标)
    private Point _pressLocal;      // 按下点(自身坐标 用于点击/拖拽位移判定)
    private Point _startPosition;   // 按下时的 Canvas 位置
    private double _dpi = 100;

    // 选中 / 缩放(
    private bool _selected;
    private bool _resizing;
    private Point _resizeStartPoint;
    private Size _resizeBaseSize;
    private double _resizeStartDpi;

    public string ComponentId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Style { get; set; } = "";

    public event Action<string>? Selected;

    // 选中态/位置/尺寸变化 -> 宿主刷新选中装饰(外置 adorner/编辑按钮)
    public event Action? GeometryChanged;

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }
            _selected = value;
            GeometryChanged?.Invoke();
        }
    }

    public bool IsDraggable
    {
        get => _draggable;
        set
        {
            _draggable = value;
            if (value)
            {
                ZIndex = 100;
            }
        }
    }

    public double DpiScale
    {
        get => _dpi;
        set
        {
            var v = Math.Clamp(value, 1, 300);
            if (Math.Abs(v - _dpi) < 0.001)
            {
                return;
            }
            _dpi = v;
            // 组件缩放: Avalonia RenderTransform 为矢量变换(文字/图形按最终分辨率重栅格化, 与 DPI 缩放同样清晰)
            // 原点取左上 → 放大时向右下生长(与 Canvas 左上定位一致)
            RenderTransformOrigin = RelativePoint.TopLeft;
            RenderTransform = new ScaleTransform(_dpi / 100.0, _dpi / 100.0);
            GeometryChanged?.Invoke();
        }
    }

    // 视觉尺寸(含缩放) 供选中装饰/编辑按钮定位
    public double VisualWidth => Width * _dpi / 100.0;
    public double VisualHeight => Height * _dpi / 100.0;

    // 组件卡片圆角(选中装饰/手柄跟随此值) 由 WidgetCardBase.ApplyCardStyle 写入
    public double CardRadius { get; set; } = 12;

    // 右下缩放手柄命中(
    private bool HitResizeHandle(Point local)
    {
        if (!_selected)
        {
            return false;
        }
        const double zone = 24;
        return local.X >= Bounds.Width - zone && local.Y >= Bounds.Height - zone;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_draggable || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _pressLocal = e.GetPosition(this);
        _pressPoint = e.GetPosition(Parent as Visual);
        _startPosition = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));

        // 选中态命中缩放手柄 -> 进入缩放(不触发选中/拖拽)
        if (HitResizeHandle(_pressLocal))
        {
            _resizing = true;
            _resizeStartPoint = _pressPoint;
            _resizeStartDpi = _dpi;
            _resizeBaseSize = new Size(Width, Height);
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.BottomRightCorner);
            e.Handled = true;
            return;
        }

        _pressed = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(Parent as Visual);

        if (_resizing)
        {
            ResizeTo(pos);
            e.Handled = true;
            return;
        }

        if (!_pressed)
        {
            // 悬停光标反馈(
            if (_draggable)
            {
                Cursor = _selected && HitResizeHandle(e.GetPosition(this))
                    ? new Cursor(StandardCursorType.BottomRightCorner)
                    : new Cursor(StandardCursorType.Hand);
            }
            return;
        }

        var dx = pos.X - _pressPoint.X;
        var dy = pos.Y - _pressPoint.Y;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || Parent is not Panel host)
        {
            HideGuides();
            SetCanvasPosition(_startPosition.X + dx, _startPosition.Y + dy);
            e.Handled = true;
            return;
        }

        // 8 DIP 吸附 屏幕边 屏幕中线 兄弟组件边与中线 并排隔 12 DIP
        var wantX = _startPosition.X + dx;
        var wantY = _startPosition.Y + dy;
        var snap = ComputeSnap(host, wantX, wantY, 8, 12);
        SetCanvasPosition(snap.X, snap.Y);
        ShowGuides(host, snap.GuideX, snap.GuideY);
        e.Handled = true;
    }

    private readonly struct SnapResult
    {
        public readonly double X;
        public readonly double Y;
        public readonly double GuideX;
        public readonly double GuideY;

        public SnapResult(double x, double y, double guideX, double guideY)
        {
            X = x;
            Y = y;
            GuideX = guideX;
            GuideY = guideY;
        }
    }

    // 就近吸附: 边 / 中线 / 并排间隔 取 8 DIP 内最近候选
    private SnapResult ComputeSnap(Panel host, double wantX, double wantY, double threshold, double gap)
    {
        var hostW = host.Bounds.Width;
        var hostH = host.Bounds.Height;
        var w = VisualWidth;
        var h = VisualHeight;
        var maxX = Math.Max(0, hostW - w);
        var maxY = Math.Max(0, hostH - h);

        var bestX = wantX;
        var bestXd = threshold + 1;
        var guideX = double.NaN;
        void TryX(double candidate, double guide)
        {
            if (candidate < -0.5 || candidate > maxX + 0.5)
            {
                return;
            }
            var d = Math.Abs(candidate - wantX);
            if (d <= threshold && d < bestXd)
            {
                bestXd = d;
                bestX = Math.Clamp(candidate, 0, maxX);
                guideX = guide;
            }
        }

        TryX(0, 0);
        TryX(maxX, hostW);
        TryX((hostW - w) / 2, hostW / 2);

        var bestY = wantY;
        var bestYd = threshold + 1;
        var guideY = double.NaN;
        void TryY(double candidate, double guide)
        {
            if (candidate < -0.5 || candidate > maxY + 0.5)
            {
                return;
            }
            var d = Math.Abs(candidate - wantY);
            if (d <= threshold && d < bestYd)
            {
                bestYd = d;
                bestY = Math.Clamp(candidate, 0, maxY);
                guideY = guide;
            }
        }

        TryY(0, 0);
        TryY(maxY, hostH);
        TryY((hostH - h) / 2, hostH / 2);

        foreach (var child in host.Children)
        {
            if (child is not DraggableContainer other || ReferenceEquals(other, this) || !other.IsVisible)
            {
                continue;
            }
            var ox = Canvas.GetLeft(other);
            var oy = Canvas.GetTop(other);
            if (double.IsNaN(ox) || double.IsNaN(oy))
            {
                continue;
            }
            var ow = other.VisualWidth;
            var oh = other.VisualHeight;
            // 左边 / 右边 / 中线 / 并排 12 DIP
            TryX(ox, ox);
            TryX(ox + ow - w, ox + ow);
            TryX(ox + (ow - w) / 2, ox + ow / 2);
            TryX(ox - w - gap, ox);
            TryX(ox + ow + gap, ox + ow);
            TryY(oy, oy);
            TryY(oy + oh - h, oy + oh);
            TryY(oy + (oh - h) / 2, oy + oh / 2);
            TryY(oy - h - gap, oy);
            TryY(oy + oh + gap, oy + oh);
        }

        return new SnapResult(bestX, bestY, guideX, guideY);
    }

    private Border? _guideV;
    private Border? _guideH;

    private void EnsureGuides(Panel host)
    {
        if (_guideV is null)
        {
            var accent = CardAccent.Value;
            var glow = new BoxShadows(new BoxShadow
            {
                Blur = 6,
                Color = Color.FromArgb(120, accent.R, accent.G, accent.B),
            });
            _guideV = new Border
            {
                Width = 2,
                Background = new SolidColorBrush(accent, 0.95),
                BoxShadow = glow,
                IsHitTestVisible = false,
                ZIndex = 10000,
            };
            _guideH = new Border
            {
                Height = 2,
                Background = new SolidColorBrush(accent, 0.95),
                BoxShadow = glow,
                IsHitTestVisible = false,
                ZIndex = 10000,
            };
            host.Children.Add(_guideV);
            host.Children.Add(_guideH);
        }
    }

    private void ShowGuides(Panel host, double guideX, double guideY)
    {
        EnsureGuides(host);
        if (_guideV is null || _guideH is null)
        {
            return;
        }
        _guideV.IsVisible = !double.IsNaN(guideX);
        if (_guideV.IsVisible)
        {
            Canvas.SetLeft(_guideV, guideX);
            Canvas.SetTop(_guideV, 0);
            _guideV.Height = host.Bounds.Height;
        }
        _guideH.IsVisible = !double.IsNaN(guideY);
        if (_guideH.IsVisible)
        {
            Canvas.SetLeft(_guideH, 0);
            Canvas.SetTop(_guideH, guideY);
            _guideH.Width = host.Bounds.Width;
        }
    }

    private void HideGuides()
    {
        if (_guideV is not null)
        {
            _guideV.IsVisible = false;
        }
        if (_guideH is not null)
        {
            _guideH.IsVisible = false;
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_guideV?.Parent is Panel p1)
        {
            p1.Children.Remove(_guideV);
        }
        if (_guideH?.Parent is Panel p2)
        {
            p2.Children.Remove(_guideH);
        }
        _guideV = null;
        _guideH = null;
        base.OnDetachedFromVisualTree(e);
    }

    // 缩放: 按拖拽位移算 DPI(夹 1-300 且不超出父容器)
    private void ResizeTo(Point parentPos)
    {
        var dx = parentPos.X - _resizeStartPoint.X;
        var dy = parentPos.Y - _resizeStartPoint.Y;
        var startW = _resizeBaseSize.Width * _resizeStartDpi / 100.0;
        var startH = _resizeBaseSize.Height * _resizeStartDpi / 100.0;
        if (startW <= 0 || startH <= 0)
        {
            return;
        }
        var newW = Math.Max(40, startW + dx);
        var newH = Math.Max(40, startH + dy);
        var scale = Math.Max(newW / startW, newH / startH);
        var newDpi = Math.Max(1, Math.Min(300, (int)Math.Round(_resizeStartDpi * scale)));

        // 不超出父容器可用区域(
        if (Parent is Panel parent && _resizeBaseSize.Width > 0 && _resizeBaseSize.Height > 0)
        {
            var maxFw = (parent.Bounds.Width - Canvas.GetLeft(this)) / _resizeBaseSize.Width;
            var maxFh = (parent.Bounds.Height - Canvas.GetTop(this)) / _resizeBaseSize.Height;
            newDpi = Math.Min(newDpi, Math.Max(1, (int)(Math.Min(maxFw, maxFh) * 100)));
        }
        if (newDpi != (int)_dpi)
        {
            Log.Debug($"[DW] {ComponentId} 缩放至 {newDpi}%");
            DpiScale = newDpi;
        }
    }

    // 供外置"拖动手柄"(SelectionAdorner)驱动缩放
    public void BeginResize(Point parentPoint)
    {
        _resizing = true;
        _resizeStartPoint = parentPoint;
        _resizeStartDpi = _dpi;
        _resizeBaseSize = new Size(Width, Height);
    }

    public void UpdateResize(Point parentPoint) => ResizeTo(parentPoint);

    public void EndResize()
    {
        if (!_resizing)
        {
            return;
        }
        _resizing = false;
        GeometryChanged?.Invoke();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var wasPressed = _pressed;
        var wasResizing = _resizing;
        _pressed = false;
        _resizing = false;
        e.Pointer.Capture(null);
        HideGuides();

        if (wasResizing)
        {
            GeometryChanged?.Invoke();
            return;
        }
        if (!wasPressed)
        {
            return;
        }
        // 点击(位移<5px)才视为选中
        var local = e.GetPosition(this);
        if (Math.Abs(local.X - _pressLocal.X) < 5 && Math.Abs(local.Y - _pressLocal.Y) < 5)
        {
            Selected?.Invoke(ComponentId);
            e.Handled = true;
            return;
        }
        if (Parent is Panel parent)
        {
            Log.Debug($"[DRAG] {ComponentId} 释放 px=({Canvas.GetLeft(this):F0},{Canvas.GetTop(this):F0}) parent={parent.Bounds.Width:F0}x{parent.Bounds.Height:F0} maxX={Math.Max(0, parent.Bounds.Width - Width):F0}");
        }
    }

    // 百分比坐标为唯一真源: 页面 Canvas 未布局(Bounds=0)时只记住百分比 布局完成后 ApplyPercent 落位
    private double _percentX = 0.5;
    private double _percentY = 0.5;

    public void SetCanvasPosition(double x, double y)
    {
        if (Parent is not Panel parent)
        {
            return;
        }
        var maxX = Math.Max(0, parent.Bounds.Width - VisualWidth);
        var maxY = Math.Max(0, parent.Bounds.Height - VisualHeight);
        var cx = Math.Clamp(x, 0, maxX);
        var cy = Math.Clamp(y, 0, maxY);
        Canvas.SetLeft(this, cx);
        Canvas.SetTop(this, cy);
        if (maxX > 0 && maxY > 0)
        {
            _percentX = cx / maxX;
            _percentY = cy / maxY;
        }
        GeometryChanged?.Invoke();
    }

    public (double X, double Y) GetPositionPercent() => (_percentX, _percentY);

    public void SetPositionPercent(double x, double y)
    {
        _percentX = Math.Clamp(x, 0, 1);
        _percentY = Math.Clamp(y, 0, 1);
        ApplyPercent();
    }

    // 百分比落像素 布局未完成时不动作(等待页面 SizeChanged/ApplyPercent 重放)
    public void ApplyPercent()
    {
        if (Parent is not Panel parent)
        {
            return;
        }
        var availableW = parent.Bounds.Width - VisualWidth;
        var availableH = parent.Bounds.Height - VisualHeight;
        if (availableW <= 0 || availableH <= 0)
        {
            Log.Debug($"[POS] {ComponentId} 跳过 parent={parent.Bounds.Width:F0}x{parent.Bounds.Height:F0} percent=({_percentX:F2},{_percentY:F2})");
            return;
        }
        var lx = Math.Clamp(_percentX * availableW, 0, availableW);
        var ly = Math.Clamp(_percentY * availableH, 0, availableH);
        Canvas.SetLeft(this, lx);
        Canvas.SetTop(this, ly);
        GeometryChanged?.Invoke();
        Log.Debug($"[POS] {ComponentId} percent=({_percentX:F2},{_percentY:F2}) parent={parent.Bounds.Width:F0}x{parent.Bounds.Height:F0} -> px=({lx:F0},{ly:F0})");
    }

    public void OnParentResize() => ApplyPercent();
}

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

public sealed class PlaceholderWidget : DraggableContainer
{
    public PlaceholderWidget(ComponentDefinition definition, JsonElement? config)
    {
        Width = definition.DefaultWidth;
        Height = definition.DefaultHeight;

        var card = new Border
        {
            BorderThickness = new Thickness(1),
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6,
        };
        var homeIcon = FAIcons.Glyph(AppIcons.Home, 22);
        homeIcon.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(homeIcon);
        content.Children.Add(new TextBlock
        {
            Text = definition.DisplayName,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = "M6",
            FontSize = 10,
            Opacity = 0.4,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        Content = new Panel { Children = { card, content } };
        _card = card;
        BindOpacity(); // 必须在 _card 赋值后(此前在赋值前调用导致 NRE)
    }

    private readonly Border _card;

    // 控件级变体判定: 构造时未挂树变体可能是 Light 挂树继承窗口 Dark 后必须重算
    // 对齐原版深色 rgba(30,30,30) 浅色 rgba(255,255,255) 全局不透明度
    private void ApplyTheme()
    {
        var dark = ThemeSense.IsDark(this);
        var op = Math.Clamp(Config.ComponentCardOpacity.Value / 100.0, 0, 1);
        _card.Background = dark
            ? new SolidColorBrush(Color.FromRgb(30, 30, 30), op)
            : new SolidColorBrush(Colors.White, op);
        _card.BorderBrush = new SolidColorBrush(dark ? Colors.White : Colors.Black, dark ? 0.10 : 0.06);
        _card.CornerRadius = new CornerRadius(Math.Max(8, Config.ComponentCardRadius.Value));
    }

    private void BindOpacity()
    {
        ApplyTheme();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        Config.ComponentCardOpacity.ValueChanged += _ => ApplyTheme();
        Config.ComponentCardRadius.ValueChanged += _ => ApplyTheme();
    }
}

public sealed class ComponentData
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Style { get; set; } = "";
    public double PosX { get; set; } = 0.5;
    public double PosY { get; set; } = 0.5;
    public double Width { get; set; }
    public double Height { get; set; }
    public double Scale { get; set; } = 100;
    public bool Enabled { get; set; } = true;
    public int PageIndex { get; set; }
    public JsonElement? Config { get; set; }
}

public sealed class ComponentManager
{
    private const int MaxComponents = 100;

    private readonly HomeView _home;
    public Dictionary<string, DraggableContainer> Components { get; } = new();
    private readonly Dictionary<string, ComponentData> _componentData = new();

    public ComponentManager(HomeView home)
    {
        _home = home;
    }

    private PageManager? PageManager => _home.PageManager;

    public void LoadComponents()
    {
        var pm = PageManager;
        if (pm is null)
        {
            Log.Warning("[ComponentManager] PageManager 未初始化");
            return;
        }
        var total = 0;
        for (var pageIndex = 0; pageIndex < pm.Count; pageIndex++)
        {
            var meta = pm.GetPage(pageIndex);
            if (meta is not { Type: "info" })
            {
                continue;
            }
            foreach (var compElement in meta.Components)
            {
                if (total >= MaxComponents)
                {
                    break;
                }
                try
                {
                    var obj = compElement.ValueKind == JsonValueKind.Object ? compElement : default;
                    var id = obj.ValueKind != default && obj.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var type = obj.ValueKind != default && obj.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                    var style = obj.ValueKind != default && obj.TryGetProperty("style", out var styleEl) ? styleEl.GetString() : null;
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(type) || string.IsNullOrEmpty(style))
                    {
                        continue;
                    }
                    var data = new ComponentData
                    {
                        Id = id!,
                        Type = type!,
                        Style = style!,
                        PageIndex = pageIndex,
                        PosX = obj.TryGetProperty("position", out var pos) && pos.TryGetProperty("x", out var px) ? px.GetDouble() : 0.5,
                        PosY = obj.TryGetProperty("position", out var pos2) && pos2.TryGetProperty("y", out var py) ? py.GetDouble() : 0.5,
                        Width = obj.TryGetProperty("size", out var size) && size.TryGetProperty("w", out var w) ? w.GetDouble() : 220,
                        Height = obj.TryGetProperty("size", out var size2) && size2.TryGetProperty("h", out var h) ? h.GetDouble() : 220,
                        Scale = obj.TryGetProperty("scale", out var scale) ? scale.GetDouble() : 100,
                        Enabled = obj.TryGetProperty("enabled", out var en) ? (!en.ValueKind.Equals(JsonValueKind.False)) : true,
                        Config = obj.TryGetProperty("config", out var cfg) ? cfg.Clone() : null,
                    };
                    var instance = CreateWidget(data);
                    if (instance is null)
                    {
                        continue;
                    }
                    instance.IsVisible = false;
                    Components[id!] = instance;
                    _componentData[id!] = data;
                    total++;
                    Log.Info($"加载组件: {id} ({type}/{style}) page={pageIndex}");
                }
                catch (Exception e)
                {
                    Log.Error($"创建组件失败: {e}");
                }
            }
        }
        Log.Info($"[ComponentManager] 共加载{total}个组件");
    }

    private DraggableContainer? CreateWidget(ComponentData data)
    {
        // 还原 id 兼容无下划线旧存档
        var defId = string.IsNullOrEmpty(data.Style) ? data.Type : $"{data.Type}_{data.Style}";
        var definition = _home.ComponentRegistry.GetDefinition(defId)
            ?? _home.ComponentRegistry.GetDefinition(data.Type);
        if (definition is null)
        {
            Log.Warning($"组件定义未注册: {defId}");
            return null;
        }
        // 真身工厂优先 占位兜底(M6 未完成的样式)
        var widget = StyleWidgetFactory.Create(defId, definition, data.Config)
            ?? new PlaceholderWidget(definition, data.Config);
        widget.ComponentId = data.Id;
        widget.Type = data.Type;
        widget.Style = data.Style;
        widget.DpiScale = data.Scale;
        var parent = _home.GetInfoPagePanel(data.PageIndex);
        if (parent is null)
        {
            Log.Warning($"组件宿主页不存在: page={data.PageIndex}");
            return null;
        }
        parent.Children.Add(widget);
        // 保存的是百分比坐标 用百分比落位(原 Canvas.SetLeft(data.PosX) 把 0.5 当 0.5px)
        widget.SetPositionPercent(data.PosX, data.PosY);
        // 页面已布局完成时立即生效 未完成时等 SizeChanged 重放
        widget.ApplyPercent();
        return widget;
    }

    public void SaveComponents()
    {
        var pm = PageManager;
        if (pm is null)
        {
            return;
        }
        var pageComponents = new Dictionary<int, List<JsonElement>>();
        foreach (var (compId, instance) in Components)
        {
            if (!_componentData.TryGetValue(compId, out var stored))
            {
                continue;
            }
            var (x, y) = instance.GetPositionPercent();
            stored.PosX = x;
            stored.PosY = y;
            stored.Width = instance.Width;
            stored.Height = instance.Height;
            stored.Scale = instance.DpiScale;
            var obj = new JsonObject
            {
                ["id"] = stored.Id,
                ["type"] = stored.Type,
                ["style"] = stored.Style,
                ["position"] = new JsonObject { ["x"] = x, ["y"] = y },
                ["size"] = new JsonObject { ["w"] = instance.Width, ["h"] = instance.Height },
                ["scale"] = stored.Scale,
                ["enabled"] = stored.Enabled,
                ["page_index"] = stored.PageIndex,
                ["config"] = stored.Config is { } cfg ? JsonNode.Parse(cfg.GetRawText()) : new JsonObject(),
            };
            var list = pageComponents.TryGetValue(stored.PageIndex, out var l) ? l : pageComponents[stored.PageIndex] = new List<JsonElement>();
            list.Add(JsonSerializer.Deserialize<JsonElement>(obj.ToJsonString()));
        }

        for (var i = 0; i < pm.Count; i++)
        {
            var meta = pm.GetPage(i);
            if (meta is { Type: "info" })
            {
                meta.Components = pageComponents.TryGetValue(i, out var list) ? list : new List<JsonElement>();
            }
        }
        Log.Debug($"[CM] 保存明细: {Components.Count}个组件");
        pm.Save();
        Log.Info("[ComponentManager] 组件已保存");
    }

    // 当前页按最短列瀑布流排开 16px 边距与间隔 超出高度时整体微缩 清除重叠
    public void ArrangeCurrentPage(int pageIndex)
    {
        if (_home.GetInfoPagePanel(pageIndex) is not { } parent || parent.Bounds.Width <= 1)
        {
            return;
        }
        var hostW = parent.Bounds.Width;
        var hostH = parent.Bounds.Height;
        var list = Components
            .Where(kv => _componentData.TryGetValue(kv.Key, out var d) && d.PageIndex == pageIndex && d.Enabled)
            .Select(kv => kv.Value)
            .OrderBy(c =>
            {
                var t = Canvas.GetTop(c);
                return double.IsNaN(t) ? 0 : t;
            })
            .ThenBy(c =>
            {
                var l = Canvas.GetLeft(c);
                return double.IsNaN(l) ? 0 : l;
            })
            .ToList();
        if (list.Count == 0)
        {
            return;
        }

        const double inset = 16, gap = 16, bottomReserve = 96;
        var availableH = Math.Max(200, hostH - inset * 2 - bottomReserve);
        var maxVisualW = list.Max(c => Math.Max(1, c.VisualWidth));

        // 枚举列数 取"整体缩得最少"的方案 宽卡按列宽先收
        var bestCols = columnsFallback(hostW, inset, gap);
        var bestColumnW = (hostW - inset * 2 - gap * (bestCols - 1)) / bestCols;
        var bestShrink = 1.0;
        var bestScore = 0.0;
        for (var cols = 2; cols <= 8; cols++)
        {
            var cw = (hostW - inset * 2 - gap * (cols - 1)) / cols;
            if (cw < 120)
            {
                break;
            }
            var hs = new double[cols];
            for (var i = 0; i < cols; i++)
            {
                hs[i] = inset;
            }
            foreach (var c in list)
            {
                var fit = c.VisualWidth > cw ? cw / c.VisualWidth : 1.0;
                var h = c.VisualHeight * fit;
                var column = 0;
                for (var i = 1; i < cols; i++)
                {
                    if (hs[i] < hs[column])
                    {
                        column = i;
                    }
                }
                hs[column] += h + gap;
            }
            var totalH = hs.Max() - gap;
            var shrink = totalH > 0 ? Math.Min(1.0, availableH / totalH) : 1.0;
            var widthScale = Math.Min(1.0, cw / maxVisualW);
            var score = shrink * widthScale;
            if (score > bestScore)
            {
                bestScore = score;
                bestCols = cols;
                bestColumnW = cw;
                bestShrink = shrink;
            }
        }

        foreach (var c in list)
        {
            var fit = c.VisualWidth > bestColumnW ? bestColumnW / c.VisualWidth : 1.0;
            c.DpiScale = Math.Max(1, Math.Round(c.DpiScale * fit * bestShrink));
        }

        var columns = bestCols;
        var columnW = bestColumnW;
        var heights = new double[columns];
        for (var i = 0; i < columns; i++)
        {
            heights[i] = inset;
        }
        foreach (var c in list)
        {
            var column = 0;
            for (var i = 1; i < columns; i++)
            {
                if (heights[i] < heights[column])
                {
                    column = i;
                }
            }
            var x = inset + column * (columnW + gap) + Math.Max(0, (columnW - c.VisualWidth) / 2);
            var y = heights[column];
            c.SetCanvasPosition(x, y);
            heights[column] = y + c.VisualHeight + gap;
        }

        SaveComponents();
        Log.Info($"[CM] 布局整理 页={pageIndex} 组件={list.Count} 列={columns} 缩放={bestShrink:F2}");

        static int columnsFallback(double hostW, double inset, double gap)
        {
            var cols = Math.Clamp((int)Math.Round(hostW / 380.0), 2, 6);
            var columnW = (hostW - inset * 2 - gap * (cols - 1)) / cols;
            return columnW < 120 ? 2 : cols;
        }
    }

    // 组件类型(定义 ID) 供配置面板按类型出字段
    public string GetComponentType(string compId) =>
        Components.TryGetValue(compId, out var c) ? c.Type : "";

    // 组件配置读写
    public JsonObject GetComponentConfig(string compId)
    {
        if (_componentData.TryGetValue(compId, out var d) && d.Config is { ValueKind: JsonValueKind.Object } cfg)
        {
            return JsonNode.Parse(cfg.GetRawText())?.AsObject() ?? new JsonObject();
        }
        return new JsonObject();
    }

    public (string Type, string Style) GetComponentKey(string compId)
    {
        if (_componentData.TryGetValue(compId, out var d))
        {
            return (d.Type, d.Style);
        }
        return ("", "");
    }

    // 写回配置并立即生效(
    // C# 组件无统一 apply_config → 用新配置重建容器(保留位置/尺寸/缩放)
    public void UpdateComponentConfig(string compId, JsonObject config)
    {
        if (!_componentData.TryGetValue(compId, out var data) || !Components.TryGetValue(compId, out var old))
        {
            return;
        }
        data.Config = JsonSerializer.Deserialize<JsonElement>(config.ToJsonString());
        var (px, py) = old.GetPositionPercent();
        var scale = old.DpiScale;
        (old.Parent as Panel)?.Children.Remove(old);
        Components.Remove(compId);

        var fresh = CreateWidget(data);
        if (fresh is null)
        {
            Log.Warning($"[CM] 配置重建失败: {compId}");
            return;
        }
        Components[compId] = fresh;
        fresh.SetPositionPercent(px, py);
        fresh.ApplyPercent();
        fresh.DpiScale = scale;
        _home.AfterComponentRebuilt(fresh);
        SaveComponents();
        Log.Info($"[CM] 组件配置已更新: {compId}");
    }

    // 换肤后重建组件
    public void RebuildAllForAppearance()
    {
        var rebuilt = 0;
        foreach (var compId in Components.Keys.ToList())
        {
            if (!_componentData.TryGetValue(compId, out var data) || !Components.TryGetValue(compId, out var old))
            {
                continue;
            }
            try
            {
                var (px, py) = old.GetPositionPercent();
                var scale = old.DpiScale;
                var visible = old.IsVisible;
                // 先建后删，防丢卡
                var fresh = CreateWidget(data);
                if (fresh is null)
                {
                    Log.Warning($"[CM] 外观重建跳过 {compId}: 工厂返回空");
                    continue;
                }
                (old.Parent as Panel)?.Children.Remove(old);
                Components[compId] = fresh;
                fresh.SetPositionPercent(px, py);
                fresh.ApplyPercent();
                fresh.DpiScale = scale;
                fresh.IsVisible = visible;
                _home.AfterComponentRebuilt(fresh);
                rebuilt++;
            }
            catch (Exception e)
            {
                Log.Error($"[CM] 外观重建失败 {compId}: {e.GetType().Name}: {e.Message}");
            }
        }
        Log.Info($"[CM] 换肤重建 {rebuilt} 个组件");
    }

    public string? AddComponent(string compType, string compStyle, int pageIndex)
    {
        if (Components.Count >= MaxComponents)
        {
            Log.Warning($"组件数量上限: {MaxComponents}");
            return null;
        }
        var existingIds = Components.Keys.ToHashSet();
        var counter = 1;
        while (existingIds.Contains($"comp_{compType}_{counter}"))
        {
            counter++;
        }
        var compId = $"comp_{compType}_{counter}";
        var defId = $"{compType}_{compStyle}";
        var definition = _home.ComponentRegistry.GetDefinition(defId);
        var data = new ComponentData
        {
            Id = compId,
            Type = compType,
            Style = compStyle,
            PageIndex = pageIndex,
            // 对齐原版 config or style_info.default_config
            Config = definition?.DefaultConfig,
        };
        var instance = CreateWidget(data);
        if (instance is null)
        {
            return null;
        }
        instance.IsVisible = true;
        instance.SetPositionPercent(0.5, 0.5);
        Components[compId] = instance;
        _componentData[compId] = data;
        SaveComponents();
        Log.Info($"添加组件: {compId} ({compType}/{compStyle}) page={pageIndex}");
        return compId;
    }

    public void RemoveComponent(string compId)
    {
        if (!Components.TryGetValue(compId, out var instance))
        {
            return;
        }
        (instance.Parent as Panel)?.Children.Remove(instance);
        Components.Remove(compId);
        _componentData.Remove(compId);
        SaveComponents();
        Log.Info($"删除组件: {compId}");
    }

    public List<DraggableContainer> GetAllContainers() => Components.Values.ToList();

    public int GetComponentPage(string compId) => _componentData.TryGetValue(compId, out var d) ? d.PageIndex : 0;

    public bool IsComponentEnabled(string compId) =>
        !_componentData.TryGetValue(compId, out var d) || d.Enabled;

    public void SetComponentPage(string compId, int pageIndex)
    {
        if (!_componentData.TryGetValue(compId, out var data))
        {
            return;
        }
        data.PageIndex = pageIndex;
        if (Components.TryGetValue(compId, out var instance))
        {
            var newParent = _home.GetInfoPagePanel(pageIndex);
            if (instance.Parent is Panel old && newParent is not null)
            {
                old.Children.Remove(instance);
                newParent.Children.Add(instance);
            }
        }
        Log.Info($"[CM] 组件 {compId} 归属页改为 {pageIndex}");
        SaveComponents();
    }
}

public sealed class ComponentLibraryWindow : GlimpseonWindow
{
    private readonly ComponentRegistry _registry;
    private Point _pressPoint;
    private ComponentDefinition? _draggingDefinition;
    private bool _dragStarted;
    private PointerPressedEventArgs? _pressedArgs;
    private readonly List<(ComponentDefinition Def, Border Host)> _pendingPreviews = new();

    // 双击卡片直接添加(由 HomeView 注入
    public Action<string>? DefinitionActivated;

    public ComponentLibraryWindow(ComponentRegistry registry)
    {
        _registry = registry;
        Title = AppUtils.Tr("component_library.title");
        Width = 780;
        Height = 640;
        MinWidth = 560;
        MinHeight = 420;

        var scroll = new ScrollViewer { Padding = new Thickness(14, 10, 14, 14) };
        var stack = new StackPanel { Spacing = 4 };
        scroll.Content = stack;

        // 先铺骨架卡片(名称立即可见 预览区空) 窗口秒开 真身后台逐个填充
        foreach (var category in _registry.GetCategories())
        {
            var defs = _registry.GetDefinitionsByCategory(category).ToList();
            if (defs.Count == 0)
            {
                continue;
            }
            stack.Children.Add(new TextBlock
            {
                Text = $"{category} · {defs.Count}",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.85,
                Margin = new Thickness(4, 10, 0, 8),
            });
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var def in defs)
            {
                wrap.Children.Add(BuildSkeletonCard(def));
            }
            stack.Children.Add(wrap);
        }
        // 与主窗口一致: 48px 自绘标题栏行(图标+标题+版本) + 内容区 (GlimpseonWindow 已 ExtendsContentIntoTitleBar)
        var titleGrid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,*,Auto"),
            Margin = new Thickness(12, 0, 0, 0),
        };
        var titleIcon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 2, 0, 0) };
        var titleText = new TextBlock
        {
            Text = AppUtils.Tr("component_library.title"),
            FontSize = 12,
            Margin = new Thickness(10, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.85,
        };
        var titleVersion = new TextBlock
        {
            Text = Paths.Version,
            FontSize = 12,
            Margin = new Thickness(0, 2, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Opacity = 0.55,
        };
        var titleCaptionReserve = new Border { Width = 140 };
        Grid.SetColumn(titleIcon, 0);
        Grid.SetColumn(titleText, 1);
        Grid.SetColumn(titleVersion, 2);
        Grid.SetColumn(titleCaptionReserve, 3);
        titleGrid.Children.Add(titleIcon);
        titleGrid.Children.Add(titleText);
        titleGrid.Children.Add(titleVersion);
        titleGrid.Children.Add(titleCaptionReserve);
        try
        {
            var iconPath = Paths.GetResourcePath(Constants.AppIcon);
            if (File.Exists(iconPath))
            {
                var bitmap = new Bitmap(iconPath);
                Icon = bitmap;
                titleIcon.Source = bitmap;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[组件库] 窗口图标加载失败: {e.Message}");
        }
        var titleRow = new Border { Height = 48, Child = titleGrid, Margin = new Thickness(0, 0, 8, 0) };
        var root = new Grid { RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*") };
        root.Children.Add(titleRow);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
        _ = FillPreviewsAsync();
    }

    // 后台逐卡填充真身预览 每张卡之间让出 UI 线程(重型的 HTML 表盘排最后)
    private async Task FillPreviewsAsync()
    {
        var ordered = _pendingPreviews
            .OrderBy(pair => pair.Def.Id.StartsWith("clock_square") ? 1 : 0)
            .ToList();
        foreach (var (def, host) in ordered)
        {
            await Task.Delay(30);
            try
            {
                host.Child = BuildLivePreview(def);
            }
            catch (Exception e)
            {
                Log.Warning($"[组件库] 预览填充失败 {def.Id}: {e.Message}\n{e.StackTrace}");
                host.Child = MakeFallbackPreview(def);
            }
        }
        Log.Info($"[组件库] 预览填充完成 共{ordered.Count}张");
    }

    private static Control MakeFallbackPreview(ComponentDefinition def)
    {
        return new TextBlock
        {
            Text = def.DisplayName,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Opacity = 0.6,
        };
    }

    // 骨架卡片: 名称立即可见 预览区显示加载占位
    private Control BuildSkeletonCard(ComponentDefinition def)
    {
        const double boxW = 196;
        const double boxH = 136;

        var host = new Border
        {
            Width = boxW,
            Height = boxH,
            CornerRadius = new CornerRadius(8),
            Background = PreviewBackdrop(),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Avalonia.Media.Colors.White, 0.07),
            ClipToBounds = true,
            Child = new TextBlock
            {
                Text = "…",
                FontSize = 16,
                Opacity = 0.35,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        _pendingPreviews.Add((def, host));

        var nameText = new TextBlock
        {
            Text = def.DisplayName,
            FontSize = 12.5,
            FontWeight = FontWeight.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 7, 0, 2),
        };

        var metaText = new TextBlock
        {
            Text = $"{def.Category} · {def.DefaultWidth:0}×{def.DefaultHeight:0}",
            FontSize = WidgetType.Micro,
            Opacity = 0.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var card = new Border
        {
            Width = 212,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(8, 8, 8, 8),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Avalonia.Media.Colors.Gray, 0.10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Avalonia.Media.Colors.Gray, 0.22),
            Child = new StackPanel { Children = { host, nameText, metaText } },
        };

        card.PointerPressed += (_, e) =>
        {
            _pressPoint = e.GetPosition(this);
            _dragStarted = false;
            _pressedArgs = e;
            _draggingDefinition = def;
        };
        card.PointerMoved += async (_, e) =>
        {
            if (_dragStarted || _draggingDefinition is null || _pressedArgs is null)
            {
                return;
            }
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }
            var pos = e.GetPosition(this);
            // 任一轴越过阈值即启动拖拽(用 || 会要求双轴同时超 8px 单方向拖动永远无效)
            if (Math.Abs(pos.X - _pressPoint.X) < 8 && Math.Abs(pos.Y - _pressPoint.Y) < 8)
            {
                return;
            }
            _dragStarted = true;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(_draggingDefinition.Id));
            await DragDrop.DoDragDropAsync(_pressedArgs, transfer, DragDropEffects.Copy);
            _draggingDefinition = null;
            _pressedArgs = null;
        };
        card.PointerReleased += (_, _) =>
        {
            _pressedArgs = null;
            _draggingDefinition = null;
        };
        card.DoubleTapped += (_, _) => DefinitionActivated?.Invoke(def.Id);

        return card;
    }

    // 预览底色 中性深蓝渐变 不继承用户高饱和配色
    private static IBrush PreviewBackdrop() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#1E2836"), 0),
            new GradientStop(Color.Parse("#3A4C62"), 1),
        },
    };

    // 真控件实例缩放为主体(实时渲染 非图片)
    private Control BuildLivePreview(ComponentDefinition def)
    {
        var naturalW = def.DefaultWidth;
        var naturalH = def.DefaultHeight;
        const double boxW = 196;
        const double boxH = 136;
        var scale = Math.Min(Math.Min(boxW / naturalW, boxH / naturalH), 1.0);

        // 组件已全部原生化 真控件实例直接预览
        Control widget;
        try
        {
            widget = StyleWidgetFactory.Create(def.Id, def, def.DefaultConfig)
                     ?? new PlaceholderWidget(def, def.DefaultConfig);
        }
        catch (Exception e)
        {
            Log.Warning($"[组件库] 预览创建失败 {def.Id}: {e.Message}\n{e.StackTrace}");
            try
            {
                widget = new PlaceholderWidget(def, def.DefaultConfig);
            }
            catch (Exception e2)
            {
                Log.Warning($"[组件库] 占位预览也失败 {def.Id}: {e2.Message}");
                return MakeFallbackPreview(def);
            }
        }
        widget.IsHitTestVisible = false; // 预览不接交互 拖拽由卡片接管
        widget.Opacity = 0.98;
        // Avalonia 12 缩放经 LayoutTransformControl
        return new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = widget };
    }
}

public sealed class NavigationPage : Panel
{
    private readonly int _pageIndex;
    private readonly PageManager _pageManager;

    public NavigationPage(int pageIndex, PageManager pageManager)
    {
        _pageIndex = pageIndex;
        _pageManager = pageManager;
        RebuildItems();
    }

    public void RebuildItems()
    {
        Children.Clear();
        foreach (var itemElement in _pageManager.GetPageItems(_pageIndex))
        {
            try
            {
                var obj = itemElement.ValueKind == JsonValueKind.Object ? itemElement : default;
                var name = obj.ValueKind != default && obj.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var path = obj.ValueKind != default && obj.TryGetProperty("path", out var pathEl) ? pathEl.GetString() ?? "" : "";
                var cell = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Width = 220,
                    Height = 72,
                    Padding = new Thickness(14, 10),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 4,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                            new TextBlock { Text = path, FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis },
                        },
                    },
                };
                cell.PointerReleased += (_, _) => LaunchItem(path);
                Children.Add(cell);
            }
            catch (Exception e)
            {
                Log.Warning($"[NavigationPage] 导航项解析失败: {e.Message}");
            }
        }
        InvalidateArrange();
    }

    private void LaunchItem(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        try
        {
            Log.Info($"[NavigationPage] 启动条目: {path}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception e)
        {
            Log.Warning($"[NavigationPage] 条目启动失败: {path} {e.Message}");
        }
    }

    // 返回尺寸必须有限(Canvas 父容器会给 Infinity 无穷可用尺寸 原样返回会触发 Invalid size 渲染崩溃)
    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsFinite(availableSize.Width) ? availableSize.Width : 1000;
        var columns = Math.Max(1, (int)(w / 240));
        foreach (var child in Children)
        {
            child.Measure(new Size(220, 72));
        }
        var rows = (Children.Count + columns - 1) / columns;
        return new Size(w, Math.Max(0, rows * 84));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var w = double.IsFinite(finalSize.Width) && finalSize.Width > 0 ? finalSize.Width : 1000;
        var h = double.IsFinite(finalSize.Height) && finalSize.Height > 0 ? finalSize.Height : 600;
        var columns = Math.Max(1, (int)(w / 240));
        for (var i = 0; i < Children.Count; i++)
        {
            var col = i % columns;
            var row = i / columns;
            Children[i].Arrange(new Rect(20 + col * 240, 20 + row * 84, 220, 72));
        }
        return new Size(w, h);
    }
}


// StyleWidgets.cs

/// <summary>
/// 样式组件工厂: 按 type_style 生成真身控件 返回 null 走占位兜底
/// </summary>
public static class StyleWidgetFactory
{
    public static DraggableContainer? Create(string typeId, ComponentDefinition definition, JsonElement? config)
    {
        // 插件工厂前置
        var pluginWidget = PluginService.CreatePluginWidget(typeId, definition, config);
        if (pluginWidget is not null)
        {
            return pluginWidget;
        }
        DraggableContainer? widget = typeId switch
        {
            "clock_digital" => new DigitalClockWidget(definition, config),
            "clock_square_1" => new NativeSquareClock1Widget(definition),
            "clock_square_2" => new NativeSquareClock2Widget(definition, config),
            "clock_calendar_month" => new CalendarMonthWidget(definition),
            "clock_calendar_mini" => new NativeMiniCalendarWidget(definition),
            "clock_almanac" => new NativeAlmanacWidget(definition),
            "poetry_one_line" => new PoetryWidget(definition),
            "weather_icon_temp" => new WeatherCurrentWidget(definition, config),
            "weather_hourly" => new WeatherHourlyWidget(definition),
            "weather_weekly" => new WeatherWeeklyWidget(definition),
            "countdown_event" => new CountdownEventWidget(definition, config),
            "countdown_days" => new CountdownDaysWidget(definition, config),
            "school_info_class_info" => new SchoolInfoWidget(definition, config),
            "announcement_board" => new NativeAnnouncementWidget(definition),
            "media_player" => new MediaPlayerWidget(definition),
            "news_baidu" or "news_weibo" or "news_jinritoutiao" or "news_tenxunwang" =>
                new NewsWidget(definition, typeId["news_".Length..]),
            "news_xcvts" => new NewsWidget(definition, "cctv"),
            "history_today" => new HistoryTodayWidget(definition),
            "word_daily" => new NativeDailyWordWidget(definition),
            "sentence_daily" => new NativeDailySentenceWidget(definition),
            "linkage_timetable_preview" => new TimetablePreviewWidget(definition),
            "linkage_timetable_nowlesson" => new TimetableNowLessonWidget(definition, config),
            "linkage_timetable_timeline" => new NativeTimetableTimelineWidget(definition),
            "homework_board" => new NativeHomeworkBoardWidget(definition),
            "class_album_horizontal" => new ClassAlbumWidget(definition, vertical: false),
            "class_album_vertical" => new ClassAlbumWidget(definition, vertical: true),
            "Math_calculator" => new CalculatorWidget(definition),
            "sticky_note" => new StickyNoteWidget(definition, config),
            "ai_chat" => new AiChatWidget(definition, config),
            "quick_launch_dock" => new QuickLaunchDockWidget(definition),
            "quick_launch_grid" => new QuickLaunchGridWidget(definition),
            "timer_countdown" => new TimerCountdownWidget(definition),
            "system_performance" => new PerformanceMonitorWidget(definition),
            "system_netspeed" => new NativeNetworkSpeedWidget(definition),
            "study_meter" => new NativeDecibelMeterWidget(definition),
            _ => null,
        };
        // 组件私有配置注入 通用外观覆盖由此生效
        if (widget is WidgetCardBase cardWidget)
        {
            cardWidget.ApplyComponentConfig(config);
        }
        return widget;
    }
}

/// <summary>卡片底座 对齐原版 _card_bg_css: 深色主题 rgba(30,30,30) 浅色主题 rgba(255,255,255) 透明度绑配置</summary>
public abstract class WidgetCardBase : DraggableContainer
{
    protected readonly Border Card;

    // 组件级圆角/底色不透明度覆盖
    protected int? CfgRadius;
    private int? CfgOpacity;

    protected WidgetCardBase(ComponentDefinition definition)
    {
        Width = definition.DefaultWidth;
        Height = definition.DefaultHeight;

        Card = new Border
        {
            BorderThickness = new Thickness(1),
        };
        ApplyCardStyle();
        Config.ComponentCardOpacity.ValueChanged += _ => ApplyCardStyle();
        Config.ComponentCardRadius.ValueChanged += _ => ApplyCardStyle();
        // 全局外观项 改一处 全部卡片跟随
        Config.ComponentCardColor.ValueChanged += _ => ApplyCardStyle();
        Config.ComponentTextColor.ValueChanged += _ => ApplyCardStyle();
        Config.ComponentCardShadow.ValueChanged += _ => ApplyCardStyle();
        Config.ComponentCardBlur.ValueChanged += _ => ApplyCardStyle();
        Config.ComponentCardBorder.ValueChanged += _ => ApplyCardStyle();
        // 拖动/缩放后毛玻璃切片要跟着壁纸对齐
        GeometryChanged += RefreshGlass;
        // 控件级事件: 构造时未挂树变体可能是 Light 挂树继承窗口 Dark 后必须重算
        ActualThemeVariantChanged += (_, _) => ApplyCardStyle();
    }

    // 由工厂在构造完成后注入组件 JSON 让所有卡片共享外观覆盖项
    public void ApplyComponentConfig(JsonElement? config)
    {
        ReadBgOverride(config);
        ApplyCardStyle();
    }

    // 读圆角与不透明度覆盖
    protected void ReadBgOverride(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }
        if (obj.TryGetProperty("corner_radius", out var r) && r.ValueKind == JsonValueKind.Number)
        {
            CfgRadius = r.GetInt32();
        }
        if (obj.TryGetProperty("bg_opacity", out var o) && o.ValueKind == JsonValueKind.Number)
        {
            CfgOpacity = Math.Clamp(o.GetInt32(), 0, 100);
        }
    }

    protected double EffectiveRadius =>
        CfgRadius is { } r ? Math.Max(8, r) : Math.Max(8, Config.ComponentCardRadius.Value);

    // 卡片实际明暗: 自定义底色按亮度判定 否则随窗口主题
    internal bool CardIsDark { get; private set; }

    // 毛玻璃切片 brush 复用以免每次拖动重建
    private ImageBrush? _glassBrush;

    // 卡片纯色 公用表面色
    protected virtual void ApplyCardStyle()
    {
        var themeDark = WindowThemeDark();
        var custom = TryParseColor(Config.ComponentCardColor.Value);
        var alpha = Math.Clamp((CfgOpacity ?? Config.ComponentCardOpacity.Value) / 100.0, 0, 1);
        var surface = custom ?? WidgetSurfacePalette.Resolve(themeDark).Card;
        var blur = Config.ComponentCardBlur.Value;
        var glass = blur > 0 && HomeView.GlassBackdrop is not null;

        if (glass)
        {
            // 取壁纸模糊图对应切片 真背景模糊而非半透明
            _glassBrush ??= new ImageBrush { Stretch = Stretch.Fill };
            _glassBrush.Source = HomeView.GlassBackdrop;
            Card.Background = _glassBrush;
            CardIsDark = true;
            RefreshGlass();
        }
        else
        {
            // 全透明时看不见底色 仍按窗口主题取字色
            CardIsDark = custom is null || alpha < 0.15 ? themeDark : Luminance(surface) < 128;
            var a = (byte)Math.Round(alpha * 255);
            if (custom is null)
            {
                Card.Background = new SolidColorBrush(Color.FromArgb(a, surface.R, surface.G, surface.B));
            }
            else
            {
                // 自定义主题走同色系斜向渐变 比纯色平涂更有体积
                var top = Common.ShiftColor(surface, CardIsDark ? 0.07 : 0.10);
                var bottom = Common.ShiftColor(surface, CardIsDark ? -0.10 : -0.05);
                Card.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(a, top.R, top.G, top.B), 0),
                        new GradientStop(Color.FromArgb(a, bottom.R, bottom.G, bottom.B), 1),
                    },
                };
            }
        }

        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var borderMode = Config.ComponentCardBorder.Value;
        var edgeColor = CardIsDark ? Colors.White : Colors.Black;
        var accent = Common.ParseAccentColor(Config.ThemeColor.Value);

        var borderWidth = borderMode switch { 0 => 0.0, 2 => 1.6, 3 => 1.4, _ => 1 };
        Card.BorderThickness = new Thickness(borderWidth);
        Card.BorderBrush = borderMode switch
        {
            3 => new SolidColorBrush(accent, 0.90),
            4 => new SolidColorBrush(edgeColor, CardIsDark ? (glass ? 0.45 : 0.34) : 0.22),
            2 => new SolidColorBrush(edgeColor, CardIsDark ? (glass ? 0.30 : 0.24) : 0.16),
            _ => new SolidColorBrush(edgeColor, glass ? 0.10 : CardIsDark ? 0.07 : 0.06),
        };
        Card.CornerRadius = new CornerRadius(EffectiveRadius);
        CardRadius = EffectiveRadius;

        // 形态阴影
        Card.BoxShadow = borderMode switch
        {
            3 => new BoxShadows(
                new BoxShadow { Blur = 24, Spread = 3, Color = Color.FromArgb(84, accent.R, accent.G, accent.B) },
                new[] { new BoxShadow { Blur = 6, Color = Color.FromArgb(140, accent.R, accent.G, accent.B) } }),
            // 双框外线
            4 => new BoxShadows(
                new BoxShadow { OffsetY = 0, Blur = 0, Spread = 3, Color = Color.FromArgb(58, 0, 0, 0) },
                new[] { new BoxShadow { OffsetY = 6, Blur = 16, Color = Color.FromArgb(70, 0, 0, 0) } }),
            _ => Config.ComponentCardShadow.Value switch
            {
                2 => new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 2, Color = Color.FromArgb(150, 0, 0, 0) }),
                1 => new BoxShadows(new BoxShadow
                {
                    OffsetY = 10,
                    Blur = 26,
                    Color = Color.FromArgb(glass || CardIsDark ? (byte)87 : (byte)44, 0, 0, 0),
                }),
                _ => default,
            },
        };
        // 文字色继承
        Card.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty,
            new SolidColorBrush(TryParseColor(Config.ComponentTextColor.Value) ?? p.Text));

        // 通知子组件重算字色
        AppearanceChanged?.Invoke();
    }

    /// <summary>卡片外观重算完成</summary>
    public event Action? AppearanceChanged;

    // 纯白视为未设置
    protected static Color? ConfiguredInk(string? hex)
    {
        var c = TryParseColor(hex);
        return c is { R: 255, G: 255, B: 255 } ? null : c;
    }

    // 窗口明暗，避免卡片自判定递归
    private bool WindowThemeDark()
    {
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            return top.ActualThemeVariant == ThemeVariant.Dark;
        }
        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    }

    // 毛玻璃切片 按卡片在窗口中的位置对齐壁纸像素
    private void RefreshGlass()
    {
        if (_glassBrush?.Source is null)
        {
            return;
        }
        if (TopLevel.GetTopLevel(this) is not { } tl)
        {
            return;
        }
        var winW = tl.Bounds.Width;
        var winH = tl.Bounds.Height;
        if (winW <= 1 || winH <= 1)
        {
            return;
        }
        var origin = this.TranslatePoint(new Point(0, 0), tl);
        if (origin is null)
        {
            return;
        }
        var x = Math.Clamp(origin.Value.X / winW, 0, 0.999);
        var y = Math.Clamp(origin.Value.Y / winH, 0, 0.999);
        var w = Math.Clamp(Math.Max(1, VisualWidth) / winW, 0.001, 1 - x);
        var h = Math.Clamp(Math.Max(1, VisualHeight) / winH, 0.001, 1 - y);
        _glassBrush.SourceRect = new RelativeRect(x, y, w, h, RelativeUnit.Relative);
    }

    private static double Luminance(Color c) => c.R * 0.299 + c.G * 0.587 + c.B * 0.114;

    // 同色系提亮/压暗 供自定义卡片底色渐变
    private static Color ShiftColor(Color c, double k)
    {
        return Common.ShiftColor(c, k);
    }

    // 色串解析 空/非法 返回 null 由调用方走主题兜底
    protected static Color? TryParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }
        try
        {
            return Color.Parse(hex);
        }
        catch
        {
            return null;
        }
    }

    // 读取组件 JSON 布尔设置
    protected static bool ReadBool(JsonElement? config, string key, bool fallback)
    {
        if (config is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty(key, out var v))
        {
            return v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback,
            };
        }
        return fallback;
    }

    // 展开层支持

    /// <summary>启用展开层 右上角按钮</summary>
    protected void EnableExpansion(string title, Func<Control> buildContent)
    {
        var btn = new Button
        {
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(13),
            Content = FAIcons.IconGlyph("E8A7", 12),
            Opacity = 0.55,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
        };
        ToolTip.SetTip(btn, title);
        // 拦按下防拖拽
        btn.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble);
        btn.Click += (_, _) => ComponentExpander.Show(this, title, buildContent());

        // 先摘再包 防双重父级
        var original = Card.Child;
        Card.Child = null;
        var host = new Grid();
        if (original is not null)
        {
            host.Children.Add(original);
        }
        host.Children.Add(btn);
        Card.Child = host;
    }
}

// 数字时钟
// 400x200 日期左上 主时间左对齐 秒数降档共基线 24 格时间轴 ClockColor
// 断点 高<120 隐时间轴 高<170 隐刻度文字 高<96 隐日期; 整秒对齐 切页停表

public sealed class DigitalClockWidget : WidgetCardBase
{
    private const double PadX = WidgetSpace.Pad, PadY = 16;
    // 主时间按可用框拟合 秒数降档共基线
    private const double EmHm = 2.52, SecScale = 0.36;

    private readonly TextBlock _timeText = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Left,
        FontWeight = FontWeight.Light,
        FontFamily = WidgetFonts.Num,
    };
    private readonly TextBlock _dateText = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        TextAlignment = TextAlignment.Left,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = WidgetFonts.Ui,
    };
    private readonly DayProgressBar _dayBar = new();
    private readonly DispatcherTimer _timer;

    private IBrush _secBrush = Brushes.White;
    private IBrush _dateBrush = Brushes.White;
    private IBrush _lunarBrush = Brushes.White;
    private DateTime _now;
    private string _hm = "--:--", _ss = "00";
    private string _dateCore = "", _lunar = "";
    private double _dateSize = WidgetType.Label;
    private bool _showLunar;
    private bool _showLunarPref = true;
    private bool _showSeconds = true;
    private bool _hiddenByConfig;

    public DigitalClockWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _showSeconds = ReadBool(config, "show_seconds", true);
        _showLunarPref = ReadBool(config, "show_lunar", true);
        var root = new Grid
        {
            Margin = new Thickness(PadX, PadY, PadX, 14),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = WidgetSpace.GapSm,
        };
        Grid.SetRow(_dateText, 0);
        Grid.SetRow(_timeText, 1);
        Grid.SetRow(_dayBar, 2);
        root.Children.Add(_dateText);
        root.Children.Add(_timeText);
        root.Children.Add(_dayBar);
        Card.Child = root;
        Content = Card;

        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => UpdateTime();

        ApplyLook();
        SizeChanged += (_, _) => Relayout();
        ActualThemeVariantChanged += (_, _) => ApplyLook();

        Config.ClockColor.ValueChanged += _ => ApplyLook();
        // 全局换肤后重解 免浅底白字
        Config.ComponentTextColor.ValueChanged += _ => ApplyLook();
        Config.ComponentCardColor.ValueChanged += _ => ApplyLook();
        Config.ClockSize.ValueChanged += _ => Relayout();
        Config.DateSize.ValueChanged += _ => Relayout();
        Config.ShowClockSeconds.ValueChanged += _ => { UpdateTime(); Relayout(); };
        Config.ShowLunarCalendar.ValueChanged += _ => UpdateTime();
        Config.ShowClock.ValueChanged += _ => UpdateTime();

        UpdateTime();
    }

    // 时钟色 + 进度条配色 主题切换重解
    private void ApplyLook()
    {
        // 卡片明暗决定字色 与窗口主题解耦
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        // 空 = 跟随卡片文字色 全局换肤后仍保证对比度
        var ink = TryParseColor(Config.ClockColor.Value) ?? p.Text;
        _timeText.Foreground = new SolidColorBrush(ink);
        _dateBrush = new SolidColorBrush(ink, CardInk.MutedOpacity);
        _lunarBrush = new SolidColorBrush(ink, CardInk.FaintOpacity);
        _secBrush = new SolidColorBrush(ink, CardInk.MutedOpacity);
        _dayBar.ApplyPalette(ThemeAccent(), p.Line, p.Text);
        PaintDate();
        Relayout();
    }

    // 强调色 走统一入口
    private static Color ThemeAccent() => CardAccent.Value;

    // 断点 + 字号拟合: 主时间取 min(宽约束, 高约束) 秒数按比例降档
    private void Relayout()
    {
        var w = Bounds.Width - PadX * 2;
        var h = Bounds.Height - PadY - 14;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        var compact = h < 96;
        var showTicks = h >= 120;
        var roomy = h >= 170;

        _dateText.IsVisible = !compact;
        _dayBar.IsVisible = showTicks;
        _dayBar.ShowLabels = roomy;
        _dayBar.Height = roomy ? 38 : 20;

        var dateH = compact ? 0 : 20;
        var barH = showTicks ? (roomy ? 38 : 20) : 0;
        var gaps = (compact ? 0 : WidgetSpace.GapSm) + (showTicks ? WidgetSpace.GapSm : 0);
        var timeH = Math.Max(14, h - dateH - barH - gaps);

        var em = _showSeconds ? EmHm + SecScale * 0.62 + 0.30 : EmHm;
        // 收进 4% 光学余量 数字"1"侧承大 纯居中会让右端贴边
        var fitW = w * 0.96;
        var size = Math.Min(fitW / em, timeH / 0.95);
        // 字宽回退校正 真实字形测量后收敛一次
        var real = MeasureTime(size);
        if (real > 0 && real > fitW)
        {
            size *= fitW / real;
        }
        size = Math.Clamp(Math.Round(size, 1), 12, 240);
        _timeText.FontSize = size;
        // 大数字收紧字距 让整块更像一体
        _timeText.LetterSpacing = -Math.Round(size * 0.02, 1);

        FitDate(w);
        Paint();
    }

    // 主时间按固定宽度模板测量 数字跳变时不改字号 不位移
    private double MeasureTime(double size)
    {
        var width = Measure("00:00", size);
        if (_showSeconds)
        {
            width += Measure(" ", size * 0.30) + Measure("00", size * SecScale);
        }
        return width;
    }

    private double Measure(string text, double size, FontWeight weight = FontWeight.Light) =>
        CardText.Width(text, _timeText.FontFamily, weight, size);

    // 日期超宽 先舍农历 再降字号
    private void FitDate(double availW)
    {
        if (!_dateText.IsVisible || string.IsNullOrEmpty(_dateCore))
        {
            return;
        }
        var size = Math.Clamp(Config.DateSize.Value, 10.5, 12);
        while (size > 10.5 && Measure(_dateCore, size, FontWeight.Normal) > availW)
        {
            size -= 0.5;
        }
        _dateSize = Math.Round(size, 1);
        _showLunar = _lunar.Length > 0
                     && Bounds.Height >= 170
                     && Measure(_dateCore, _dateSize, FontWeight.Normal)
                     + Measure("  " + _lunar, _dateSize * 0.88, FontWeight.Normal) <= availW;
        PaintDate();
    }

    // 日期一行 农历更弱 同一基线
    private void PaintDate()
    {
        if (!_dateText.IsVisible)
        {
            return;
        }
        var inlines = _dateText.Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
        inlines.Clear();
        inlines.Add(new Avalonia.Controls.Documents.Run(_dateCore)
        {
            FontSize = _dateSize,
            Foreground = _dateBrush,
        });
        if (_showLunar)
        {
            inlines.Add(new Avalonia.Controls.Documents.Run("  ")
            {
                FontSize = _dateSize * 0.88,
            });
            inlines.Add(new Avalonia.Controls.Documents.Run(_lunar)
            {
                FontSize = _dateSize * 0.88,
                Foreground = _lunarBrush,
            });
        }
    }

    // 整秒刷新 隐藏即停表
    private void UpdateTime()
    {
        if (!Config.ShowClock.Value)
        {
            _hiddenByConfig = true;
            if (IsVisible)
            {
                IsVisible = false;
            }
            _timer.Stop();
            return;
        }
        if (_hiddenByConfig)
        {
            _hiddenByConfig = false;
            IsVisible = true;
        }

        _now = AppUtils.PreciseNow();
        _hm = _now.ToString("HH:mm");
        _ss = _now.ToString("ss");

        var weekday = AppUtils.Tr("weekday." + _now.DayOfWeek.ToString().ToLowerInvariant());
        _dateCore = $"{_now:yyyy年M月d日} {weekday}";
        _lunar = "";
        if (_showLunarPref)
        {
            var lunar = AlmanacService.GetToday(_now);
            if (lunar is not null && !string.IsNullOrEmpty(lunar.LunarMonth))
            {
                _lunar = $"{lunar.LunarMonth}{lunar.LunarDay}";
            }
        }

        FitDate(Math.Max(1, Bounds.Width - PadX * 2));
        Paint();
        Schedule();
    }

    // Run 混排 显式按基线对齐 主时间与秒数共线
    private void Paint()
    {
        var f = _timeText.FontSize;
        var inlines = _timeText.Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
        inlines.Clear();
        inlines.Add(new Avalonia.Controls.Documents.Run(_hm)
        {
            FontSize = f,
            BaselineAlignment = Avalonia.Media.BaselineAlignment.Baseline,
        });
        if (_showSeconds)
        {
            inlines.Add(new Avalonia.Controls.Documents.Run(" ")
            {
                FontSize = f * 0.30,
                BaselineAlignment = Avalonia.Media.BaselineAlignment.Baseline,
            });
            inlines.Add(new Avalonia.Controls.Documents.Run(_ss)
            {
                FontSize = f * SecScale,
                Foreground = _secBrush,
                BaselineAlignment = Avalonia.Media.BaselineAlignment.Baseline,
            });
        }
        _dayBar.Update(_now);
    }

    // 对齐整秒 按余数重排 避免 DispatcherTimer 累积漂移
    private void Schedule()
    {
        _timer.Stop();
        if (!Config.ShowClock.Value)
        {
            return;
        }
        var phase = _showSeconds ? _now.Millisecond : _now.Second * 1000 + _now.Millisecond;
        var period = _showSeconds ? 1000 : 60000;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(period - phase + 10, 20, 60100));
        _timer.Start();
    }

    // 宿主切页隐藏即停表 回到可见立刻追帧
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || _timer is null)
        {
            return;
        }
        if (!IsVisible)
        {
            _timer.Stop();
        }
        else if (Config.ShowClock.Value && !_timer.IsEnabled)
        {
            UpdateTime();
        }
    }
}

// 今日 24 格进度 已过整格半透明 当前格按分钟填充 未到走轨道色
public sealed class DayProgressBar : Grid
{
    private const int Cells = 24;
    private readonly Border[] _ticks = new Border[Cells];
    private readonly Grid _labelRow = new();
    private readonly TextBlock[] _labels = new TextBlock[5];
    private IBrush _past = Brushes.Transparent;
    private IBrush _now = Brushes.Transparent;
    private IBrush _rail = Brushes.Transparent;
    private bool _showLabels;

    public bool ShowLabels
    {
        get => _showLabels;
        set
        {
            _showLabels = value;
            _labelRow.IsVisible = value;
        }
    }

    public DayProgressBar()
    {
        IsHitTestVisible = false;
        RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("20,Auto");
        RowSpacing = 2;

        var tickHost = new Grid { ColumnSpacing = 3, VerticalAlignment = VerticalAlignment.Bottom };
        for (var i = 0; i < Cells; i++)
        {
            tickHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tick = new Border
            {
                Height = 9,
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
            };
            SetColumn(tick, i);
            tickHost.Children.Add(tick);
            _ticks[i] = tick;
        }
        Children.Add(tickHost);

        // 0 / 6 / 12 / 18 / 24 五个刻度 与 24 格等分对齐
        _labelRow.ColumnDefinitions =
            Avalonia.Controls.ColumnDefinitions.Parse("Auto,*,Auto,*,Auto,*,Auto,*,Auto");
        var texts = new[] { "0", "6", "12", "18", "24" };
        for (var i = 0; i < texts.Length; i++)
        {
            var label = new TextBlock
            {
                Text = texts[i],
                FontSize = WidgetType.Micro,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = i == 0
                    ? HorizontalAlignment.Left
                    : i == texts.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            };
            SetColumn(label, i * 2);
            _labelRow.Children.Add(label);
            _labels[i] = label;
        }
        _labelRow.IsVisible = false;
        SetRow(_labelRow, 1);
        Children.Add(_labelRow);
    }

    // 轨道/已过/当前 三色
    public void ApplyPalette(Color accent, Color track, Color ink)
    {
        _past = new SolidColorBrush(accent, 0.42);
        _now = new SolidColorBrush(accent);
        _rail = new SolidColorBrush(track, 0.55);
        foreach (var label in _labels)
        {
            label.Foreground = new SolidColorBrush(ink, CardInk.FaintOpacity);
        }
    }

    // 刷新到某时刻: 已过 9px 当前随时长长到 20px 未到轨道色
    public void Update(DateTime now)
    {
        var hour = now.Hour;
        var frac = (now.Minute * 60 + now.Second) / 3600.0;
        for (var i = 0; i < Cells; i++)
        {
            var current = i == hour;
            _ticks[i].Height = current ? 9 + 11 * frac : 9;
            _ticks[i].Background = i < hour ? _past : current ? _now : _rail;
        }
    }
}

// 一言
// 规格: 400x200 边距(24,20,24,20) 单label居中wrap 富文本(——分离出处/逗号换行) 字号PoetrySize(16) 色PoetryTextColor

public sealed class PoetryWidget : WidgetCardBase
{
    private const string FallbackContent = "山重水复疑无路，柳暗花明又一村。";
    private const string FallbackAuthor = "陆游《游山西村》";

    private readonly TextBlock _contentText = new();
    private readonly Border _authorBar = new();
    private readonly TextBlock _authorText = new();
    private readonly StackPanel _authorRow;
    private readonly StackPanel _stack;
    private readonly Border _accentBar = new();

    public PoetryWidget(ComponentDefinition definition) : base(definition)
    {
        _contentText.TextWrapping = TextWrapping.Wrap;
        _contentText.TextAlignment = TextAlignment.Left;
        _contentText.HorizontalAlignment = HorizontalAlignment.Stretch;
        _contentText.FontWeight = FontWeight.Light;
        _contentText.LetterSpacing = 0.6;
        _contentText.Margin = new Thickness(16, 0, 0, 0);

        _contentText.VerticalAlignment = VerticalAlignment.Center;

        _stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _stack.Children.Add(_contentText);
        _contentText.VerticalAlignment = VerticalAlignment.Center;
        _stack.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(280),
            },
        };

        _authorText.FontSize = 11.5;
        _authorText.VerticalAlignment = VerticalAlignment.Center;
        _authorBar.Width = 14;
        _authorBar.Height = 1;
        _authorBar.VerticalAlignment = VerticalAlignment.Center;
        _authorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _authorRow.Children.Add(_authorBar);
        _authorRow.Children.Add(_authorText);
        _authorRow.HorizontalAlignment = HorizontalAlignment.Left;
        _authorRow.Margin = new Thickness(16, 0, 0, 0);

        var root = new Grid
        {
            Margin = new Thickness(24, 20, 24, 20),
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = 8,
        };
        Grid.SetRow(_stack, 1);
        Grid.SetRow(_authorRow, 2);
        // 作者行固定在第二行
        root.Children.Add(_stack);
        root.Children.Add(_authorRow);
        // 两行布局 正文区与作者行
        _accentBar.Width = 3;
        _accentBar.CornerRadius = new CornerRadius(2);
        _accentBar.HorizontalAlignment = HorizontalAlignment.Left;
        _accentBar.VerticalAlignment = VerticalAlignment.Stretch;
        _accentBar.Margin = new Thickness(0, 2, 0, 2);
        Grid.SetRowSpan(_accentBar, 3);
        root.Children.Add(_accentBar);
        Card.Child = root;
        Content = Card;
        SizeChanged += (_, _) => FitText();

        ApplyPoetryStyle();
        ActualThemeVariantChanged += (_, _) => ApplyPoetryStyle();
        AppearanceChanged += ApplyPoetryStyle;
        Config.PoetryTextColor.ValueChanged += _ => ApplyPoetryStyle();
        Config.PoetrySize.ValueChanged += _ => FitText();
        Config.ShowPoetry.ValueChanged += _ => RefreshFromCache();

        // 原版 1s tick 读缓存
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => RefreshFromCache();
        timer.Start();

        // 后台拉取(原版由主界面广播/interval刷新)
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await PoetryService.GetPoetryWithCacheAsync();
            }
            catch (Exception e)
            {

                Log.Warning($"[SW] 在线获取失败 走缓存: {e.Message}");
                // 拉取失败走缓存
            }
        });
        RefreshFromCache();
    }

    private void RefreshFromCache()
    {
        IsVisible = Config.ShowPoetry.Value;
        var cache = AppUtils.LoadCache("poetry", ignoreExpiry: true);
        var text = cache is { ValueKind: JsonValueKind.String } cacheStr ? cacheStr.GetString() : null;
        FormatPoetry(text);
    }

    // 原版 _format_poetry: —— / — 分离出处 逗号后换行；无缓存时用本地名句兜底
    private void FormatPoetry(string? text)
    {
        var content = text?.Trim() ?? "";
        var attribution = "";
        if (string.IsNullOrWhiteSpace(content))
        {
            content = FallbackContent;
            attribution = FallbackAuthor;
        }
        else
        {
            foreach (var sep in new[] { "——", "—" })
            {
                var idx = content.IndexOf(sep, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    attribution = content[(idx + sep.Length)..].Trim();
                    content = content[..idx].Trim();
                    break;
                }
            }
        }
        content = System.Text.RegularExpressions.Regex.Replace(content, "([,，])", "$1\n").Trim();
        if (content.EndsWith("\n"))
        {
            content = content[..^1].TrimEnd();
        }
        var changed = _contentText.Text != content || _authorText.Text != attribution;
        if (changed)
        {
            _stack.Opacity = 0;
            _contentText.Text = content;
            _authorText.Text = attribution;
            _authorRow.IsVisible = attribution.Length > 0;
            FitText();
            Dispatcher.UIThread.Post(() => _stack.Opacity = 1);
        }
        else
        {
            FitText();
        }
    }

    // 字号同时受宽与高约束 出处保持小字
    private void FitText()
    {
        var w = Bounds.Width - 64;
        var h = Bounds.Height - 64;
        if (w <= 1 || h <= 1 || string.IsNullOrEmpty(_contentText.Text))
        {
            return;
        }
        var baseSize = Math.Clamp(Config.PoetrySize.Value * 1.6, 16, 30);
        var lines = _contentText.Text.Split('\n');
        double widest = 0;
        foreach (var line in lines)
        {
            widest = Math.Max(widest, CardText.Width(line, Common.AppFontFamily, FontWeight.Light, baseSize));
        }
        var byWidth = widest > w ? baseSize * w / widest : baseSize;
        var byHeight = h / (Math.Max(1, lines.Length) * 1.5);
        var size = Math.Clamp(Math.Min(baseSize, Math.Min(byWidth, byHeight)), 12, 30);
        _contentText.FontSize = Math.Round(size, 1);
        _contentText.LineHeight = Math.Round(size * 1.5, 1);
    }

    private void ApplyPoetryStyle()
    {
        // 自定义色优先 兜底走色板 浅色卡防白字
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var ink = ConfiguredInk(Config.PoetryTextColor.Value) ?? p.Text;
        var muted = new SolidColorBrush(ink, CardInk.MutedOpacity);
        _contentText.Foreground = new SolidColorBrush(ink);
        _authorText.Foreground = muted;
        // 作者行只留虚线短杆与出处
        _authorBar.Background = CardAccent.Brush(0.45);
        _accentBar.Background = CardAccent.Brush(0.75);
        FitText();
    }
}

// 天气三件套

/// <summary>天气组件公共基座: 显隐开关/缓存读取/SVG 图标 对齐原版 WeatherComponentBase</summary>
public abstract class WeatherWidgetBase : WidgetCardBase
{
    protected readonly DispatcherTimer RefreshTimer;

    // 图标随卡缩放 由各组件 Relayout 更新 刷新图标时沿用
    protected double LeadIconPx = 60;
    protected double SlotIconPx = 28;

    protected WeatherWidgetBase(ComponentDefinition definition) : base(definition)
    {
        // 原版 _update_interval: 10s/30s/1m/5m/10m/30m
        RefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ParseIntervalSeconds(Config.WeatherUpdateInterval.Value)) };
        RefreshTimer.Tick += (_, _) => RefreshFromCache();
        Config.WeatherUpdateInterval.ValueChanged += v =>
            RefreshTimer.Interval = TimeSpan.FromSeconds(ParseIntervalSeconds(v));
        Config.ShowWeather.ValueChanged += _ => RefreshFromCache();
        Config.WeatherTextColor.ValueChanged += _ => ApplyWeatherStyle();
        RefreshTimer.Start();
        ActualThemeVariantChanged += (_, _) => ApplyWeatherStyle();
        // 卡片底色/明暗变了 自有字色跟着重算
        AppearanceChanged += ApplyWeatherStyle;
    }

    // 原版 interval_map: "10s"=10s "30s"=30s "1m"=60s "5m"=300s "10m"=600s "30m"=1800s
    protected static double ParseIntervalSeconds(string value)
    {
        var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
        if (!double.TryParse(digits, out var num) || num <= 0)
        {
            return 300;
        }
        return value.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? num : num * 60;
    }

    protected void RefreshFromCache()
    {
        // 原版 _refresh_weather: showWeather=false 隐藏组件
        IsVisible = Config.ShowWeather.Value;
        if (!IsVisible)
        {
            return;
        }
        UpdateDisplay(AppUtils.GetCachedContent("weather", ignoreExpiry: true));
    }

    /// <summary>城市名: 保持顶行干净 不再拼接缓存标记</summary>
    protected string CityLabel() => Config.City.Value;

    protected abstract void UpdateDisplay(JsonElement? cached);

    // 原版 _parse_current: 温度四舍五入 + 天气码 全部经字符串转换 API 缓存里数值可能是字符串
    protected static (object Temp, int Code) ParseCurrent(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty("current", out var current))
        {
            return ("--", 0);
        }
        var raw = current.TryGetProperty("temperature", out var t) && t.TryGetProperty("value", out var tv)
            ? tv.ToString() : "--";
        object temp = double.TryParse(raw, out var d) ? (object)Math.Round(d) : (object)(raw.Length == 0 ? "--" : raw);
        var code = 0;
        if (current.TryGetProperty("weather", out var w) && !int.TryParse(w.ToString(), out code))
        {
            code = 0;
        }
        return (temp, code);
    }

    // 原版 _onCityLabelClicked: 点城市标签改名+坐标 立即重取天气
    protected void AttachCityClick(TextBlock cityText)
    {
        cityText.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        cityText.PointerReleased += async (_, _) =>
        {
            try
            {
                var owner = TopLevel.GetTopLevel(this) as Window;

                var name = await RegionSelectorDialog.ShowAsync(owner!, Config.City.Value);
                if (string.IsNullOrWhiteSpace(name) || name.Trim() == Config.City.Value)
                {
                    return;
                }
                name = name.Trim();
                Config.City.Value = name;
                var (lon, lat) = new RegionDatabase().GetCoordinates(name);
                if (lon is not null && lat is not null)
                {
                    Config.Longitude.Value = lon.Value;
                    Config.Latitude.Value = lat.Value;
                }
                Log.Info($"选择城市: {name} (经纬度: {lon} {lat})");
                cityText.Text = name;
                var data = await WeatherService.FetchAllAsync();
                if (data is { } d)
                {
                    AppUtils.SaveCache("weather", d, Config.WeatherUpdateInterval.Value);
                    UpdateDisplay(d);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[WX] 新城市天气获取失败: {e.Message}");
            }
        };
    }

    // SVG 天气图标 对齐原版 render_svg_icon
    // 注意: Svg 构造参数是 baseUri, 不会自动加载文件, 必须显式赋 SvgSource (探针实测 Path/加载行为)
    protected static Avalonia.Svg.Skia.SvgSource? MakeSvgSource(string? iconName)
    {
        if (string.IsNullOrEmpty(iconName))
        {
            return null;
        }
        try
        {
            var path = WeatherMaps.GetWeatherIconPath(iconName);
            if (!File.Exists(path))
            {
                return null;
            }
            return Avalonia.Svg.Skia.SvgSource.Load(path, null);
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 求值失败: {e.Message}");
            return null;
        }
    }

    protected static Avalonia.Svg.Skia.Svg? MakeSvgIcon(string? iconName, double size)
    {
        var source = MakeSvgSource(iconName);
        if (source is null)
        {
            return null;
        }
        var path = WeatherMaps.GetWeatherIconPath(iconName!);
        return new Avalonia.Svg.Skia.Svg(new Uri(path)) { Width = size, Height = size, SvgSource = source };
    }

    protected abstract void ApplyWeatherStyle();
}

/// <summary>极简天气</summary>
public sealed class WeatherCurrentWidget : WeatherWidgetBase
{
    private readonly Avalonia.Svg.Skia.Svg _icon = MakeSvgIcon("2.svg", 64)!;
    private readonly bool _showIcon = true;
    private readonly TextBlock _cityText = new();
    private readonly TextBlock _conditionText = new();
    private readonly TextBlock _tempText = new();
    private readonly TextBlock _rangeText = new();
    private readonly TextBlock _detailText = new();
    private readonly Grid _hero = new();

    public WeatherCurrentWidget(ComponentDefinition definition, JsonElement? config = null) : base(definition)
    {
        _showIcon = ReadBool(config, "show_icon", true);
        // 200x200 卡片: 城市/天气在上, 大温度+图标居中, 高低温/体感/湿度在底
        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad, 14, WidgetSpace.Pad, 14),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = WidgetSpace.GapXs,
        };

        // 顶行: 城市; 天气现象放到温度下方 单字在小字号下发糊
        var top = new Grid();
        _cityText.FontSize = WidgetType.Label;
        _cityText.VerticalAlignment = VerticalAlignment.Center;
        _cityText.TextTrimming = TextTrimming.CharacterEllipsis;
        _cityText.MaxLines = 1;
        top.Children.Add(_cityText);

        _conditionText.FontSize = 13.5;
        _conditionText.FontWeight = FontWeight.Medium;
        _conditionText.VerticalAlignment = VerticalAlignment.Center;
        _conditionText.TextTrimming = TextTrimming.CharacterEllipsis;
        _conditionText.MaxLines = 1;
        _conditionText.Margin = new Thickness(1, 0, 0, 2);

        // 主区: 温度 + 天气现象 + 天气图标
        _hero.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto");
        _hero.ColumnSpacing = 8;
        _tempText.VerticalAlignment = VerticalAlignment.Center;
        _tempText.HorizontalAlignment = HorizontalAlignment.Left;
        _tempText.TextAlignment = TextAlignment.Left;
        _tempText.FontWeight = FontWeight.Light;
        var tempStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        tempStack.Children.Add(_tempText);
        tempStack.Children.Add(_conditionText);
        _icon.VerticalAlignment = VerticalAlignment.Center;
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(tempStack, 0);
        Grid.SetColumn(_icon, 1);
        _hero.Children.Add(tempStack);
        _hero.Children.Add(_icon);
        _icon.IsVisible = _showIcon;

        // 底行: 最高/最低 + 体感/湿度
        var bottom = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Bottom };
        _rangeText.FontSize = 12;
        _rangeText.TextTrimming = TextTrimming.CharacterEllipsis;
        _rangeText.MaxLines = 1;
        _detailText.FontSize = 11;
        _detailText.TextTrimming = TextTrimming.CharacterEllipsis;
        _detailText.MaxLines = 1;
        bottom.Children.Add(_rangeText);
        bottom.Children.Add(_detailText);

        Grid.SetRow(top, 0);
        Grid.SetRow(_hero, 1);
        Grid.SetRow(bottom, 2);
        root.Children.Add(top);
        root.Children.Add(_hero);
        root.Children.Add(bottom);
        Card.Child = root;
        Content = Card;

        ApplyWeatherStyle();
        AttachCityClick(_cityText);
        Config.WeatherSize.ValueChanged += _ => ApplyWeatherStyle();
        Config.WeatherIconSize.ValueChanged += _ => Relayout();
        Config.ThemeColor.ValueChanged += _ => _icon.InvalidateVisual();
        SizeChanged += (_, _) => Relayout();
        Relayout();
        RefreshFromCache();
    }

    private void Relayout()
    {
        var h = Bounds.Height - 28;
        var w = Bounds.Width - 32;
        if (h <= 1 || w <= 1)
        {
            return;
        }

        // 主区吃掉剩余高度 温度与图标按主区归一
        var heroH = Math.Clamp(h * 0.56, 44, 112);
        _hero.Height = heroH;
        LeadIconPx = Math.Clamp(Math.Min(heroH * 0.78, w * 0.34), 20, 80);
        _icon.Width = LeadIconPx;
        _icon.Height = LeadIconPx;
        // WeatherSize 作比例系数 默认 24=1.0
        var temp = Math.Min(heroH * 0.76 * Config.WeatherSize.Value / 24.0, w * 0.52);
        _tempText.FontSize = Math.Clamp(Math.Round(temp, 1), 22, 96);
        _tempText.LetterSpacing = -Math.Round(_tempText.FontSize * 0.02, 1);
        _cityText.FontSize = 12.5;
        _conditionText.FontSize = 13.5;
        _rangeText.FontSize = 12;
        _detailText.FontSize = 11;
    }

    protected override void ApplyWeatherStyle()
    {
        // 自定义色优先 兜底走色板 浅色卡防白字
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var ink = ConfiguredInk(Config.WeatherTextColor.Value) ?? p.Text;
        var muted = new SolidColorBrush(ink, CardInk.MutedOpacity);
        var faint = new SolidColorBrush(ink, CardInk.FaintOpacity);
        _cityText.Foreground = muted;
        _conditionText.Foreground = new SolidColorBrush(ink, 0.74);
        _tempText.Foreground = new SolidColorBrush(ink);
        _rangeText.Foreground = muted;
        _detailText.Foreground = faint;
        // 字号只在 Relayout 定 避免双头赋值互踩
        Relayout();
    }

    protected override void UpdateDisplay(JsonElement? cached)
    {
        var (temp, code) = ParseCurrent(cached);
        var hasData = cached is { ValueKind: JsonValueKind.Object };
        _cityText.Text = CityLabel();
        _tempText.Text = temp is "--" ? "--°" : $"{temp}°";
        _conditionText.Text = hasData ? WeatherMaps.GetWeatherText(code) : "";
        _conditionText.IsVisible = _conditionText.Text.Length > 0;

        var (high, low) = ReadDailyRange(cached);
        _rangeText.Text = high == "--" && low == "--"
            ? ""
            : $"{AppUtils.Tr("weather.high")} {high}°   {AppUtils.Tr("weather.low")} {low}°";
        _rangeText.IsVisible = _rangeText.Text.Length > 0;

        var feels = ReadCurrentMetric(cached, "feelsLike");
        var humidity = ReadCurrentMetric(cached, "humidity");
        var details = new List<string>(2);
        if (feels != "--")
        {
            details.Add($"{AppUtils.Tr("settings.weather_feels")} {feels}°");
        }
        if (humidity != "--")
        {
            details.Add($"{AppUtils.Tr("settings.weather_humidity")} {humidity}%");
        }
        _detailText.Text = string.Join(" · ", details);
        _detailText.IsVisible = _detailText.Text.Length > 0;

        var icon = hasData ? WeatherMaps.GetIcon(code) : null;
        var svg = MakeSvgIcon(icon, LeadIconPx);
        _icon.SvgSource = svg?.SvgSource;
        _icon.Width = svg?.Width ?? 0;
        _icon.Height = svg?.Height ?? 0;
    }

    private static (string High, string Low) ReadDailyRange(JsonElement? cached)
    {
        if (cached is { ValueKind: JsonValueKind.Object } root
            && root.TryGetProperty("forecastDaily", out var daily)
            && WeatherService.ParseDaily(daily) is { Days.Count: > 0 } parsed)
        {
            return (parsed.Days[0].High, parsed.Days[0].Low);
        }
        return ("--", "--");
    }

    private static string ReadCurrentMetric(JsonElement? cached, string name)
    {
        if (cached is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("current", out var current)
            || !current.TryGetProperty(name, out var metric)
            || !metric.TryGetProperty("value", out var value))
        {
            return "--";
        }
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? "--" : text;
    }
}

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

        var labelBrush = new SolidColorBrush(p.Text, 0.8);
        var typeface = new Typeface(Common.AppFontFamily);
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
                FlowDirection.LeftToRight, typeface, 11.5, labelBrush);
            var ty = Math.Clamp(y - 4 - ft.Height, 0, Math.Max(0, h - ft.Height));
            ctx.DrawText(ft, new Point(Math.Clamp(x - ft.Width / 2, 0, Math.Max(0, w - ft.Width)), ty));
        }
    }
}

/// <summary>逐小时天气: 上区当前温度 / 中区温度曲线 / 下区时刻+图标</summary>
public sealed class WeatherHourlyWidget : WeatherWidgetBase
{
    private readonly TextBlock _cityText = new();
    private readonly TextBlock _tempText = new();
    private readonly Avalonia.Svg.Skia.Svg _currentIcon = MakeSvgIcon("2.svg", 60)!;
    private readonly TempCurve _curve = new();
    private readonly Grid _top = new();
    private readonly (TextBlock Time, Avalonia.Svg.Skia.Svg Icon)[] _slots = new (TextBlock, Avalonia.Svg.Skia.Svg)[6];

    public WeatherHourlyWidget(ComponentDefinition definition) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(28, 18, 28, 18),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = 4,
        };

        // 上行: 左(城市+温度) 右(图标) 原版 AlignTop
        _top.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto");
        var left = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Top };
        _cityText.FontSize = 16;
        _tempText.FontSize = 52;
        _tempText.FontWeight = FontWeight.Light;
        left.Children.Add(_cityText);
        left.Children.Add(_tempText);
        _currentIcon.Width = 60;
        _currentIcon.Height = 60;
        _currentIcon.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(left, 0);
        Grid.SetColumn(_currentIcon, 1);
        _top.Children.Add(left);
        _top.Children.Add(_currentIcon);

        // 中区: 6 小时温度曲线 点温标替代槽内温度 给曲线留出高度
        _curve.DarkProvider = () => CardIsDark;
        _curve.MinHeight = 28;

        // 下区: 6 槽 时刻 + 天气图标
        var bottom = new Avalonia.Controls.Primitives.UniformGrid { Rows = 1, Columns = 6 };
        for (var i = 0; i < 6; i++)
        {
            var col = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var time = new TextBlock { FontSize = 12, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var icon = MakeSvgIcon("2.svg", 28)!;
            col.Children.Add(time);
            col.Children.Add(icon);
            _slots[i] = (time, icon);
            bottom.Children.Add(col);
        }
        SizeChanged += (_, _) => Relayout();
        Config.ThemeColor.ValueChanged += _ => _curve.InvalidateVisual();

        Grid.SetRow(_top, 0);
        Grid.SetRow(_curve, 1);
        Grid.SetRow(bottom, 2);
        root.Children.Add(_top);
        root.Children.Add(_curve);
        root.Children.Add(bottom);
        Card.Child = root;
        Content = Card;

        ApplyWeatherStyle();
        AttachCityClick(_cityText);
        RefreshFromCache();
    }

    protected override void ApplyWeatherStyle()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var ink = ConfiguredInk(Config.WeatherTextColor.Value) ?? p.Text;
        var muted = new SolidColorBrush(ink, CardInk.MutedOpacity);
        _cityText.Foreground = muted;
        _tempText.Foreground = new SolidColorBrush(ink);
        foreach (var (time, _) in _slots)
        {
            time.Foreground = muted;
        }
        _curve.InvalidateVisual();
        Relayout();
    }

    // 上区 42% 高度 曲线吃剩余 下区图标随卡高
    private void Relayout()
    {
        var h = Bounds.Height - 36;
        var w = Bounds.Width - 56;
        if (h <= 1 || w <= 1)
        {
            return;
        }

        var topH = Math.Clamp(h * 0.46, 48, 116);
        _top.Height = topH;
        LeadIconPx = Math.Clamp(Math.Min(topH * 0.86, w * 0.2), 16, 88);
        SlotIconPx = CardText.ByHeight(h, 0.11, 14, 30);
        _currentIcon.Width = LeadIconPx;
        _currentIcon.Height = LeadIconPx;
        _tempText.FontSize = CardText.ByHeight(topH, 0.64, 20, 88);
        _cityText.FontSize = CardText.ByHeight(h, 0.075, 9, 14);

        var slotFont = CardText.ByHeight(h, 0.065, 11, 14);
        foreach (var (time, icon) in _slots)
        {
            time.FontSize = slotFont;
            icon.Width = SlotIconPx;
            icon.Height = SlotIconPx;
        }
    }

    protected override void UpdateDisplay(JsonElement? cached)
    {
        var (temp, code) = ParseCurrent(cached);
        _cityText.Text = CityLabel();
        _tempText.Text = temp is "--" ? "--°" : $"{temp}°";
        var curSvg = MakeSvgIcon(cached is { ValueKind: JsonValueKind.Object } ? WeatherMaps.GetIcon(code) : null, LeadIconPx);
        _currentIcon.SvgSource = curSvg?.SvgSource;

        // 原版: parse_hourly(forecastHourly) 6 槽 小时=(pubTime.hour+i)%24
        WeatherHourly? parsed = null;
        if (cached is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("forecastHourly", out var fh))
        {
            parsed = WeatherService.ParseHourly(fh);
        }
        var hours = parsed?.Hours ?? new List<WeatherHour>();
        var startHour = 0;
        if (!string.IsNullOrEmpty(parsed?.PubTime) && DateTime.TryParse(parsed!.PubTime, out var pt))
        {
            startHour = pt.Hour;
        }
        var temps = new double?[6];
        for (var i = 0; i < 6; i++)
        {
            if (i < hours.Count)
            {
                var h = hours[i];
                _slots[i].Time.Text = $"{(startHour + i) % 24:00}:00";
                var svg = MakeSvgIcon(h.Icon, SlotIconPx);
                _slots[i].Icon.SvgSource = svg?.SvgSource;
                if (double.TryParse(h.Temp?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var tv))
                {
                    temps[i] = tv;
                }
            }
            else
            {
                _slots[i].Time.Text = "--:00";
                _slots[i].Icon.SvgSource = null;
            }
        }
        _curve.SetPoints(temps);
    }
}

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

/// <summary>逐日天气</summary>
public sealed class WeatherWeeklyWidget : WeatherWidgetBase
{
    private readonly TextBlock _cityText = new();
    private readonly TextBlock _tempText = new();
    private readonly Avalonia.Svg.Skia.Svg _currentIcon = MakeSvgIcon("2.svg", 60)!;
    private readonly (TextBlock Day, Avalonia.Svg.Skia.Svg Icon, TextBlock Low, TextBlock High, TempRangeBar Bar)[] _rows =
        new (TextBlock, Avalonia.Svg.Skia.Svg, TextBlock, TextBlock, TempRangeBar)[4];

    // 星期名走 locale 不再硬编码

    public WeatherWeeklyWidget(ComponentDefinition definition) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.PadCompact, 12, WidgetSpace.PadCompact, 12),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto"),
            RowSpacing = WidgetSpace.GapXs,
        };

        var top = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto") };
        var left = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        _cityText.FontSize = WidgetType.Label;
        _tempText.FontSize = 42;
        _tempText.FontWeight = FontWeight.Light;
        _tempText.LetterSpacing = -0.8;
        left.Children.Add(_cityText);
        left.Children.Add(_tempText);
        _currentIcon.Width = 48;
        _currentIcon.Height = 48;
        _currentIcon.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(left, 0);
        Grid.SetColumn(_currentIcon, 1);
        top.Children.Add(left);
        top.Children.Add(_currentIcon);

        // 下行: day | icon | 区间条 | low | spacer | high 四行共享一条温度刻度
        var bottom = new Grid { RowSpacing = 2 };
        bottom.RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,*,*,*");
        for (var i = 0; i < 4; i++)
        {
            var row = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,*,Auto,6,Auto") };
            var day = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var icon = MakeSvgIcon("2.svg", 18)!;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(6, 0, 6, 0);
            var bar = new TempRangeBar { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) };
            var low = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var high = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(day, 0);
            Grid.SetColumn(icon, 1);
            Grid.SetColumn(bar, 2);
            Grid.SetColumn(low, 3);
            Grid.SetColumn(high, 5);
            row.Children.Add(day);
            row.Children.Add(icon);
            row.Children.Add(bar);
            row.Children.Add(low);
            row.Children.Add(high);
            Grid.SetRow(row, i);
            _rows[i] = (day, icon, low, high, bar);
            bottom.Children.Add(row);
        }
        SizeChanged += (_, _) => Relayout();
        Config.ThemeColor.ValueChanged += _ =>
        {
            foreach (var (_, _, _, _, bar) in _rows)
            {
                bar.InvalidateVisual();
            }
        };

        Grid.SetRow(top, 0);
        Grid.SetRow(bottom, 1);
        root.Children.Add(top);
        root.Children.Add(bottom);
        Card.Child = root;
        Content = Card;

        ApplyWeatherStyle();
        AttachCityClick(_cityText);
        RefreshFromCache();
    }

    protected override void ApplyWeatherStyle()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var ink = ConfiguredInk(Config.WeatherTextColor.Value) ?? p.Text;
        var muted = new SolidColorBrush(ink, CardInk.MutedOpacity);
        _cityText.Foreground = muted;
        _tempText.Foreground = new SolidColorBrush(ink);
        for (var i = 0; i < _rows.Length; i++)
        {
            var (day, _, low, high, bar) = _rows[i];
            day.Foreground = i == 0 ? new SolidColorBrush(ink) : muted;
            day.FontWeight = i == 0 ? FontWeight.SemiBold : FontWeight.Normal;
            low.Foreground = muted;
            high.Foreground = new SolidColorBrush(ink);
            bar.InvalidateVisual();
        }
        Relayout();
    }

    // 顶部区占 46% 其余 4 行均分 字号图标随行高
    private void Relayout()
    {
        var h = Bounds.Height - 24;
        var w = Bounds.Width - 24;
        if (h <= 1 || w <= 1)
        {
            return;
        }

        var topH = Math.Max(24, h * 0.46);
        _tempText.FontSize = Math.Clamp(Math.Round(Math.Min(topH * 0.66, w * 0.42), 1), 20, 48);
        _tempText.LetterSpacing = -Math.Round(_tempText.FontSize * 0.02, 1);
        _cityText.FontSize = WidgetType.Label;
        var curSize = Math.Max(16, _tempText.FontSize * 0.62);
        LeadIconPx = curSize;
        _currentIcon.Width = curSize;
        _currentIcon.Height = curSize;

        var rowH = Math.Max(10, (h - topH - WidgetSpace.GapXs) / 4);
        var rowFont = Math.Clamp(Math.Round(rowH * 0.48, 1), 10, 12);
        var rowIcon = Math.Clamp(Math.Round(rowH * 0.78, 1), 12, 20);
        SlotIconPx = rowIcon;
        var tempSlot = CardText.Width("-00°", WidgetFonts.Num, FontWeight.Normal, rowFont);
        foreach (var (day, svg, low, high, bar) in _rows)
        {
            day.FontSize = rowFont;
            low.FontSize = rowFont;
            high.FontSize = rowFont;
            low.FontFamily = WidgetFonts.Num;
            high.FontFamily = WidgetFonts.Num;
            low.MinWidth = tempSlot;
            high.MinWidth = tempSlot;
            svg.Width = rowIcon;
            svg.Height = rowIcon;
            bar.Height = Math.Clamp(rowH * 0.30, 4, 6);
        }
    }

    private static bool TryNum(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, out value);

    protected override void UpdateDisplay(JsonElement? cached)
    {
        var (temp, code) = ParseCurrent(cached);
        _cityText.Text = CityLabel();
        _tempText.Text = temp is "--" ? "--°" : $"{temp}°";
        var curSvg = MakeSvgIcon(cached is { ValueKind: JsonValueKind.Object } ? WeatherMaps.GetIcon(code) : null, LeadIconPx);
        _currentIcon.SvgSource = curSvg?.SvgSource;

        WeatherDaily? parsed = null;
        if (cached is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("forecastDaily", out var fd))
        {
            parsed = WeatherService.ParseDaily(fd);
        }
        var days = parsed?.Days ?? new List<WeatherDay>();
        var now = DateTime.Now;

        // 四行共用刻度 1° 余量 温度全缺时不画条
        var lows = new double[4];
        var highs = new double[4];
        var has = new bool[4];
        var scaleMin = double.MaxValue;
        var scaleMax = double.MinValue;
        for (var i = 0; i < 4 && i < days.Count; i++)
        {
            var lowOk = TryNum(days[i].Low, out lows[i]);
            var highOk = TryNum(days[i].High, out highs[i]);
            has[i] = lowOk && highOk;
            if (has[i])
            {
                scaleMin = Math.Min(scaleMin, Math.Min(lows[i], highs[i]));
                scaleMax = Math.Max(scaleMax, Math.Max(lows[i], highs[i]));
            }
        }
        if (scaleMin > scaleMax)
        {
            scaleMin = 0;
            scaleMax = 1;
        }
        scaleMin -= 1;
        scaleMax += 1;

        for (var i = 0; i < 4; i++)
        {
            var row = _rows[i];
            if (i < days.Count)
            {
                var d = now.AddDays(i);
                row.Day.Text = i == 0 ? AppUtils.Tr("common.today") : AppUtils.Tr(AppUtils.WeekdayKey(d.DayOfWeek));
                var svg = MakeSvgIcon(days[i].Icon, SlotIconPx);
                row.Icon.SvgSource = svg?.SvgSource;
                row.Low.Text = $"{days[i].Low ?? "--"}°";
                row.High.Text = $"{days[i].High ?? "--"}°";
                row.Bar.IsVisible = has[i];
                if (has[i])
                {
                    row.Bar.Set(lows[i], highs[i], scaleMin, scaleMax);
                }
            }
            else
            {
                row.Day.Text = "--";
                row.Icon.SvgSource = null;
                row.Low.Text = "--";
                row.High.Text = "--°";
                row.Bar.IsVisible = false;
            }
        }
    }
}

// 倒计时
// 事件倒计时: 三层 事件名(SemiBold) / 大读数(Light 自适应) / 目标时间(muted) 1s tick

public sealed class CountdownEventWidget : WidgetCardBase
{
    private readonly TextBlock _eventText = new();
    private readonly TextBlock _daysText = new();
    private readonly TextBlock _targetText = new();
    private readonly Grid _content;
    private readonly CardEmptyState _emptyView;
    private string _eventName = "";
    private string _targetTime = "";

    public CountdownEventWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _eventText.TextAlignment = TextAlignment.Center;
        _eventText.FontWeight = FontWeight.SemiBold;
        _daysText.TextAlignment = TextAlignment.Center;
        _daysText.FontWeight = FontWeight.Light;
        _daysText.VerticalAlignment = VerticalAlignment.Center;
        _targetText.TextAlignment = TextAlignment.Center;

        // 三层结构 大读数撑满中段
        _content = new Grid
        {
            Margin = new Thickness(20, 16, 20, 16),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = 4,
        };
        Grid.SetRow(_eventText, 0);
        Grid.SetRow(_daysText, 1);
        Grid.SetRow(_targetText, 2);
        _content.Children.Add(_eventText);
        _content.Children.Add(_daysText);
        _content.Children.Add(_targetText);

        _emptyView = new CardEmptyState(
            FAIcons.Glyph(AppIcons.Clock),
            AppUtils.Tr("countdown.no_events"),
            AppUtils.Tr("countdown.click_to_config"));

        var host = new Grid();
        host.Children.Add(_content);
        host.Children.Add(_emptyView);
        Card.Child = host;
        Content = Card;

        ReadConfig(config);
        ApplyStyle();
        Config.CountdownTextColor.ValueChanged += _ => ApplyStyle();
        Config.CountdownTextSize.ValueChanged += _ => FitDays();
        SizeChanged += (_, _) => FitDays();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => UpdateCountdown();
        timer.Start();
        UpdateCountdown();
        FitDays();
    }

    private void ReadConfig(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }
        _eventName = obj.TryGetProperty("event_name", out var n) ? n.GetString() ?? "" : "";
        _targetTime = obj.TryGetProperty("target_time", out var t) ? t.GetString() ?? "" : "";
    }

    private void ApplyStyle()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        // 旧默认红色弃用 走色板 自定义色优先
        var custom = TryParseColor(Config.CountdownTextColor.Value);
        var ink = custom is { } c && c != Color.FromRgb(255, 0, 0) ? c : p.Text;
        _eventText.Foreground = new SolidColorBrush(ink);
        _daysText.Foreground = new SolidColorBrush(ink);
        _targetText.Foreground = new SolidColorBrush(ink, CardInk.MutedOpacity);
        _eventText.FontSize = 13;
        _targetText.FontSize = 12;
        _emptyView.Apply(p);
    }

    // 大读数字号按最宽样本拟合 秒级刷新不重排
    private void FitDays()
    {
        var w = Bounds.Width - 40;
        if (w <= 1)
        {
            return;
        }
        var prefer = Math.Max(20, Config.CountdownTextSize.Value * 1.15);
        _daysText.FontSize = CardText.Fit("99天 23:59:59", Common.AppFontFamily, FontWeight.Light, w, prefer, 14, 120);
    }

    private void UpdateCountdown()
    {
        var hasTarget = DateTime.TryParse(_targetTime, out var target);
        if (string.IsNullOrEmpty(_eventName) || string.IsNullOrEmpty(_targetTime) || !hasTarget)
        {
            _content.IsVisible = false;
            _emptyView.IsVisible = true;
            return;
        }
        _content.IsVisible = true;
        _emptyView.IsVisible = false;
        _eventText.Text = _eventName;
        var delta = target - DateTime.Now;
        if (delta.TotalSeconds > 0)
        {
            _daysText.Text = $"{(int)delta.TotalDays}天 {delta.Hours:D2}:{delta.Minutes:D2}:{delta.Seconds:D2}";
            _targetText.Text = $"{AppUtils.Tr("days_matter.target_date")}: {target:yyyy-MM-dd HH:mm}";
        }
        else
        {
            _daysText.Text = AppUtils.Tr("countdown.expired");
            _targetText.Text = $"{target:yyyy-MM-dd HH:mm}";
        }
    }

}

/// <summary>倒数日</summary>
public sealed class CountdownDaysWidget : WidgetCardBase
{
    private readonly Border _headerBorder = new();
    private readonly Border _footerBorder = new();
    private readonly TextBlock _headerText = new();
    private readonly TextBlock _numText = new();
    private readonly TextBlock _footerText = new();
    private readonly Grid _content;
    private readonly CardEmptyState _emptyView = new(
        FAIcons.Glyph(AppIcons.Calendar),
        AppUtils.Tr("days_matter.empty_title"),
        AppUtils.Tr("days_matter.empty_hint"));
    private string _eventName = "";
    private string _targetDate = "";
    private string _titleBg = "#F98E1B";

    private static readonly string[] WeekdayNames = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

    public CountdownDaysWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _headerText.TextAlignment = TextAlignment.Center;
        _headerText.VerticalAlignment = VerticalAlignment.Center;
        _headerBorder.Child = _headerText;
        _numText.TextAlignment = TextAlignment.Center;
        _numText.VerticalAlignment = VerticalAlignment.Center;
        _numText.HorizontalAlignment = HorizontalAlignment.Stretch;
        _footerText.TextAlignment = TextAlignment.Center;
        _footerText.VerticalAlignment = VerticalAlignment.Center;
        _footerBorder.Child = _footerText;

        // 平面三层: 事件名 / 大数字 / 日期 不再用色块
        _content = new Grid
        {
            Margin = new Thickness(16, 14, 16, 12),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = 4,
        };
        Grid.SetRow(_headerBorder, 0);
        Grid.SetRow(_numText, 1);
        Grid.SetRow(_footerBorder, 2);
        _content.Children.Add(_headerBorder);
        _content.Children.Add(_numText);
        _content.Children.Add(_footerBorder);

        var host = new Grid();
        host.Children.Add(_content);
        host.Children.Add(_emptyView);
        Card.Child = host;
        Content = Card;

        ReadConfig(config);
        ApplyCardStyle();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += (_, _) => UpdateDays();
        timer.Start();
        UpdateDays();
    }

    private void ReadConfig(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }
        _eventName = obj.TryGetProperty("event_name", out var n) ? n.GetString() ?? "" : "";
        _targetDate = obj.TryGetProperty("target_date", out var t) ? t.GetString() ?? "" : "";
        _titleBg = obj.TryGetProperty("title_bg_color", out var c) && !string.IsNullOrEmpty(c.GetString())
            ? c.GetString()! : "#F98E1B";
    }

    private void UpdateDays()
    {
        if (!DateTime.TryParse(_targetDate, out var target))
        {
            _content.IsVisible = false;
            _emptyView.IsVisible = true;
            return;
        }
        _content.IsVisible = true;
        _emptyView.IsVisible = false;
        var today = DateTime.Today;
        var diff = (target.Date - today).Days;
        var days = Math.Abs(diff);
        var dateStr = target.ToString("yyyy-MM-dd");
        var weekStr = WeekdayNames[(int)target.DayOfWeek];
        var footer = diff > 0
            ? $"{AppUtils.Tr("days_matter.target_date")}: {dateStr} {weekStr}"
            : diff == 0
                ? $"{AppUtils.Tr("days_matter.target_date")}: {dateStr} {weekStr}"
                : $"{AppUtils.Tr("days_matter.start_date")}: {dateStr} {weekStr}";
        var status = diff > 0 ? AppUtils.Tr("days_matter.still")
            : diff == 0 ? AppUtils.Tr("days_matter.today")
            : AppUtils.Tr("days_matter.past");
        var name = string.IsNullOrEmpty(_eventName) ? AppUtils.Tr("days_matter.default_title") : _eventName;
        _headerText.Text = $"{name} {status}";
        _footerText.Text = footer;

        // 原版 _fit_font_px: 72px 起步 逐减2 直到宽度可用
        var numberText = days.ToString();
        var px = 72;
        var available = Math.Max(40, Width - 16);
        while (px > 12)
        {
            var ft = new Avalonia.Media.FormattedText(
                numberText,
                System.Globalization.CultureInfo.CurrentCulture,
                Avalonia.Media.FlowDirection.LeftToRight,
                new Avalonia.Media.Typeface(Common.AppFontFamily),
                px,
                Brushes.Black);
            if (ft.WidthIncludingTrailingWhitespace <= available)
            {
                break;
            }
            px -= 2;
        }
        _numText.FontSize = px;
        _numText.FontWeight = FontWeight.Light;
        _numText.Text = numberText;
        // 今天当天 数字染强调色 数据驱动
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _numText.Foreground = diff == 0 ? CardAccent.Brush() : CardInk.Fg(p);
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        // 平面化: 标题条/日期条透明 字色走令牌
        _headerBorder.Background = Brushes.Transparent;
        _headerBorder.CornerRadius = new CornerRadius(0);
        _footerBorder.Background = Brushes.Transparent;
        _footerBorder.CornerRadius = new CornerRadius(0);
        _headerText.Foreground = CardInk.Muted(p);
        _headerText.FontSize = 14;
        _headerText.FontWeight = FontWeight.SemiBold;
        _headerText.LetterSpacing = 0.5;
        _footerText.Foreground = CardInk.Muted(p);
        _footerText.FontSize = 12;
        _emptyView.Apply(p);
    }
}

// 学校班级信息
// 规格: 400x200 边距(8,8,8,8) 间距6 上层背景3:口号1 上层边距(20,16,20,12) 间距4
// 班级行(班级左bold+人数右) 学校名 换行口号(边距20,4,20,4)
// 字号默认 班级48/学校25/人数19/口号23 缩放font_scale 人数"{n}人"

public sealed class SchoolInfoWidget : WidgetCardBase
{
    private readonly Grid _content;
    private readonly Grid _empty;
    private readonly TextBlock _classText = new();
    private readonly TextBlock _countText = new();
    private readonly TextBlock _schoolText = new();
    private readonly TextBlock _sloganText = new();
    private readonly TextBlock _watermark = new();
    private readonly Border _badge = new();
    private readonly TextBlock _badgeText = new();
    private readonly Border _countChip = new();
    private readonly Border _sloganBar = new();
    private readonly CardEmptyState _emptyView;

    private string _class = "";
    private string _school = "";
    private string _count = "";
    private string _slogan = "";
    private int _classSize = 48;
    private int _schoolSize = 25;
    private int _countSize = 19;
    private int _sloganSize = 23;
    private string _textColor = "";
    private int _fontScale = 100;

    public SchoolInfoWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _content = BuildContent();
        _emptyView = new CardEmptyState(
            FAIcons.Glyph(AppIcons.People),
            AppUtils.Tr("school_info_component.empty_title"),
            AppUtils.Tr("school_info_component.click_to_config"));
        _empty = BuildEmpty();

        var host = new Grid();
        host.Children.Add(_content);
        host.Children.Add(_empty);
        Card.Child = host;
        Content = Card;

        ReadConfig(config);
        ApplyCardStyle();
        ApplyStyle();
        UpdateInfo();
        ActualThemeVariantChanged += (_, _) => ApplyStyle();
    }

    private Grid BuildContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(20, 16, 20, 14),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = 6,
        };

        // 超大班级首字水印 让空余区域有层次
        _watermark.FontSize = 96;
        _watermark.FontWeight = FontWeight.Bold;
        _watermark.HorizontalAlignment = HorizontalAlignment.Right;
        _watermark.VerticalAlignment = VerticalAlignment.Center;
        _watermark.IsHitTestVisible = false;
        Grid.SetRowSpan(_watermark, 3);

        var header = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*,Auto"),
            ColumnSpacing = 12,
        };
        _badge.Width = 54;
        _badge.Height = 54;
        _badge.CornerRadius = new CornerRadius(17);
        _badge.VerticalAlignment = VerticalAlignment.Center;
        _badge.ClipToBounds = true;
        _badgeText.FontSize = 24;
        _badgeText.FontWeight = FontWeight.SemiBold;
        _badgeText.HorizontalAlignment = HorizontalAlignment.Center;
        _badgeText.VerticalAlignment = VerticalAlignment.Center;
        _badge.Child = _badgeText;

        var identity = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        _classText.FontWeight = FontWeight.Light;
        _classText.TextTrimming = TextTrimming.CharacterEllipsis;
        _classText.MaxLines = 1;
        _schoolText.TextTrimming = TextTrimming.CharacterEllipsis;
        _schoolText.MaxLines = 1;
        identity.Children.Add(_classText);
        identity.Children.Add(_schoolText);

        _countChip.Padding = new Thickness(10, 4, 10, 4);
        _countChip.CornerRadius = new CornerRadius(12);
        _countChip.VerticalAlignment = VerticalAlignment.Center;
        _countChip.Child = _countText;

        Grid.SetColumn(_badge, 0);
        Grid.SetColumn(identity, 1);
        Grid.SetColumn(_countChip, 2);
        header.Children.Add(_badge);
        header.Children.Add(identity);
        header.Children.Add(_countChip);

        var slogan = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*"),
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _sloganBar.Width = 3;
        _sloganBar.CornerRadius = new CornerRadius(2);
        _sloganBar.VerticalAlignment = VerticalAlignment.Stretch;
        _sloganText.TextWrapping = TextWrapping.Wrap;
        _sloganText.TextTrimming = TextTrimming.CharacterEllipsis;
        _sloganText.MaxLines = 2;
        _sloganText.VerticalAlignment = VerticalAlignment.Center;
        slogan.Children.Add(_sloganBar);
        Grid.SetColumn(_sloganText, 1);
        slogan.Children.Add(_sloganText);

        Grid.SetRow(header, 0);
        Grid.SetRow(slogan, 2);
        root.Children.Add(_watermark);
        root.Children.Add(header);
        root.Children.Add(slogan);
        return root;
    }

    private Grid BuildEmpty()
    {
        var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        root.Children.Add(_emptyView);
        return root;
    }

    private void ReadConfig(JsonElement? config)
    {
        ReadBgOverride(config);
        if (config is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }
        if (obj.TryGetProperty("class", out var v) && v.ValueKind == JsonValueKind.String)
        {
            _class = v.GetString() ?? "";
        }
        if (obj.TryGetProperty("school", out var v2) && v2.ValueKind == JsonValueKind.String)
        {
            _school = v2.GetString() ?? "";
        }
        if (obj.TryGetProperty("count", out var v3) && v3.ValueKind == JsonValueKind.String)
        {
            _count = v3.GetString() ?? "";
        }
        if (obj.TryGetProperty("slogan", out var v4) && v4.ValueKind == JsonValueKind.String)
        {
            _slogan = v4.GetString() ?? "";
        }
        if (obj.TryGetProperty("class_size", out var s1) && s1.ValueKind == JsonValueKind.Number)
        {
            _classSize = s1.GetInt32();
        }
        if (obj.TryGetProperty("school_size", out var s2) && s2.ValueKind == JsonValueKind.Number)
        {
            _schoolSize = s2.GetInt32();
        }
        if (obj.TryGetProperty("count_size", out var s3) && s3.ValueKind == JsonValueKind.Number)
        {
            _countSize = s3.GetInt32();
        }
        if (obj.TryGetProperty("slogan_size", out var s4) && s4.ValueKind == JsonValueKind.Number)
        {
            _sloganSize = s4.GetInt32();
        }
        if (obj.TryGetProperty("text_color", out var c) && c.ValueKind == JsonValueKind.String)
        {
            _textColor = c.GetString() ?? "";
        }
        if (obj.TryGetProperty("font_scale", out var f) && f.ValueKind == JsonValueKind.Number)
        {
            _fontScale = f.GetInt32();
        }
    }

    private bool IsConfigured => _class.Length > 0 || _school.Length > 0 || _count.Length > 0 || _slogan.Length > 0;

    private void UpdateInfo()
    {
        var configured = IsConfigured;
        _content.IsVisible = configured;
        _empty.IsVisible = !configured;
        if (!configured)
        {
            return;
        }

        _classText.Text = _class;
        _schoolText.Text = _school;
        _sloganText.Text = _slogan;
        _sloganText.IsVisible = _slogan.Length > 0;
        _sloganBar.IsVisible = _slogan.Length > 0;
        _badge.IsVisible = _class.Length > 0 || _school.Length > 0;
        _badgeText.Text = _class.Length > 0 ? _class[..1] : _school.Length > 0 ? _school[..1] : "";
        _watermark.Text = _badgeText.Text;
        _countText.Text = _count.Length > 0
            ? AppUtils.Tr("school_info_component.student_count", ("count", _count))
            : "";
        _countChip.IsVisible = _count.Length > 0;
    }

    private void ApplyStyle()
    {
        var dark = CardIsDark;
        var scale = _fontScale / 100.0;
        double S(int px) => Math.Max(1, px * scale);
        var p = WidgetSurfacePalette.Resolve(dark);

        // 自定义色优先 次级走 62% 令牌
        var textColor = TryParseColor(_textColor) ?? p.Text;
        var muted = new SolidColorBrush(textColor, CardInk.MutedOpacity);
        var faint = new SolidColorBrush(textColor, 0.07);

        _classText.Foreground = new SolidColorBrush(textColor);
        _classText.FontSize = Math.Clamp(S(_classSize), 20, 52);
        _schoolText.Foreground = muted;
        _schoolText.FontSize = Math.Clamp(S(_schoolSize), 12, 22);
        _countText.Foreground = muted;
        _countText.FontSize = Math.Clamp(S(_countSize), 10, 16);
        _sloganText.Foreground = new SolidColorBrush(textColor, 0.84);
        _sloganText.FontSize = Math.Clamp(S(_sloganSize), 12, 20);
        _watermark.Foreground = faint;

        _badge.Background = CardAccent.Brush(0.16);
        _badgeText.Foreground = CardAccent.Brush();
        _countChip.Background = new SolidColorBrush(textColor, 0.055);
        _countChip.BorderBrush = new SolidColorBrush(textColor, 0.09);
        _countChip.BorderThickness = new Thickness(1);
        _sloganBar.Background = CardAccent.Brush(0.8);

        _emptyView.Apply(p, textColor);
    }
}

// 课表共享数据源

public static class TimetableScheduleProvider
{
    private static readonly LinkageBridge CiBridge = new();
    private static readonly ClassWidgetsBridge CwBridge = new();
    private static bool _bridgeStarted;

    /// <summary>当日课表 按来源分发 联动/插件/本地</summary>
    public static List<ScheduleRow> GetTodaySchedule()
    {
        var result = new List<ScheduleRow>();
        // 插件提供者来源
        var profileSource = Config.ProfileSource.Value ?? "";
        if (profileSource.StartsWith("plugin:", StringComparison.Ordinal))
        {
            var key = profileSource["plugin:".Length..];
            if (ResolvePluginProvider(key) is { } entry)
            {
                try
                {
                    return entry.Provider();
                }
                catch (Exception e)
                {
                    Log.Warning($"[课表组件] 插件课表读取失败 [{key}]: {e.Message}");
                    return result;
                }
            }
            // 来源失效 回落本地档案
            Log.Warning($"[课表组件] 插件课表来源不可用: {key} 回落本地档案");
        }
        if (!UseLocalScheduleData())
        {
            try
            {
                var bridge = EnsureBridge();
                var schedule = bridge switch
                {
                    LinkageBridge ci => ci.GetTodaySchedule(),
                    ClassWidgetsBridge cw => cw.GetTodaySchedule(),
                    _ => null,
                };
                if (schedule is not null)
                {
                    return schedule;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[课表组件] 联动数据读取失败: {e.Message}");
                return result;
            }
        }
        try
        {
            var (master, timetable) = LoadActiveMasterTimetable();
            var now = AppUtils.PreciseNow();
            var dayName = DayKey(((int)now.DayOfWeek + 6) % 7 + 1);
            var nowTime = TimeOnly.FromDateTime(now);
            result.AddRange(BuildDayRows(master, timetable, DateOnly.FromDateTime(now), dayName, true, nowTime, _ => ""));
        }
        catch (Exception e)
        {
            Log.Warning($"[课表组件] 本地课表读取失败: {e.Message}");
        }
        return result;
    }

    /// <summary>单日行 整日占位 / 课间名 / patch 标记</summary>
    private static List<ScheduleRow> BuildDayRows(ScheduleProfile master, TimetableDoc timetable, DateOnly date,
        string dayKey, bool isToday, TimeOnly nowTime, Func<string, string> teacherOf)
    {
        var rows = new List<ScheduleRow>();
        var (course, wholeDay) = ResolveDayCourse(master, date);
        if (wholeDay is not null)
        {
            // 整日占位 场次优先
            rows.AddRange(WholeDayRows(wholeDay, isToday, nowTime));
            return rows;
        }
        var patches = ScheduleRules.ResolvePatches(master, date, timetable);
        var classCounter = 0;
        for (var i = 0; i < timetable.Periods.Count; i++)
        {
            var p = timetable.Periods[i];
            var start = p["start"]?.GetValue<string>() ?? "";
            var end = p["end"]?.GetValue<string>() ?? "";
            var ptype = p["type"]?.GetValue<string>() ?? "";
            var isCurrent = isToday &&
                            TimeOnly.TryParse(start, out var st) && TimeOnly.TryParse(end, out var et) &&
                            st <= nowTime && nowTime < et;
            if (ptype is "课间" or "活动")
            {
                // BreakName ← name ?? type
                var bname = p["name"]?.GetValue<string>() ?? "";
                rows.Add(new ScheduleRow("", "", start, end, 0, isCurrent, true, bname.Length > 0 ? bname : ptype));
                continue;
            }
            classCounter++;
            var patch = patches.FirstOrDefault(pp => pp.Index == i);
            var subject = patch is not null && !patch.NeedsCheck ? patch.Subject : course.GetCell(i, dayKey);
            var mark = patch is null ? null : patch.NeedsCheck ? ScheduleMark.Check : ScheduleMark.Patch;
            rows.Add(new ScheduleRow(subject, teacherOf(subject), start, end, classCounter, isCurrent, false, "", mark));
        }
        return rows;
    }

    /// <summary>整周课表 键 monday..sunday</summary>
    public static Dictionary<string, List<ScheduleRow>> GetWeekSchedule()
    {
        var result = new Dictionary<string, List<ScheduleRow>>();
        if (Config.ProfileSource.Value is "classisland" or "classwidgets" && !UseLocalScheduleData())
        {
            try
            {
                var bridge = EnsureBridge();
                if (bridge is LinkageBridge ci)
                {
                    // CI 键 Mon=1..Sun=7
                    var week = ci.GetWeekSchedule();
                    for (var wd = 1; wd <= 7; wd++)
                    {
                        result[DayKey(wd)] = week.GetValueOrDefault(wd) ?? [];
                    }
                    return result;
                }
                if (bridge is ClassWidgetsBridge cw)
                {
                    // CW 键 Mon=0..Sun=6
                    var week = cw.GetWeekSchedule();
                    for (var wd = 1; wd <= 7; wd++)
                    {
                        result[DayKey(wd)] = week.GetValueOrDefault(wd - 1) ?? [];
                    }
                    return result;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[课表组件] 周课表联动读取失败: {e.Message}");
            }
        }
        try
        {
            var (master, timetable) = LoadActiveMasterTimetable();
            // 学科档补教师名
            var teachers = new Dictionary<string, string>();
            try
            {
                var subject = SubjectDoc.LoadOrDefault(master.Subject);
                foreach (var r in subject.Rows)
                {
                    var name = r["name"]?.GetValue<string>() ?? "";
                    var teacher = r["teacher"]?.GetValue<string>() ?? "";
                    if (name.Length > 0 && teacher.Length > 0)
                    {
                        teachers[name] = teacher;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Debug($"[课表组件] 学科教师读取失败: {e.Message}");
            }
            var now = AppUtils.PreciseNow();
            var nowTime = TimeOnly.FromDateTime(now);
            var todayKey = DayKey(((int)now.DayOfWeek + 6) % 7 + 1);
            var mondayDate = DateOnly.FromDateTime(now).AddDays(-(((int)now.DayOfWeek + 6) % 7));
            for (var wd = 1; wd <= 7; wd++)
            {
                var dayKey = DayKey(wd);
                var dayDate = mondayDate.AddDays(wd - 1);
                result[dayKey] = BuildDayRows(master, timetable, dayDate, dayKey, dayKey == todayKey, nowTime,
                    s => teachers.GetValueOrDefault(s, ""));
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[课表组件] 周课表本地读取失败: {e.Message}");
        }
        for (var wd = 1; wd <= 7; wd++)
        {
            result.TryAdd(DayKey(wd), []);
        }
        return result;
    }

    private static string DayKey(int pythonWeekday) => pythonWeekday switch
    {
        1 => "monday",
        2 => "tuesday",
        3 => "wednesday",
        4 => "thursday",
        5 => "friday",
        6 => "saturday",
        _ => "sunday",
    };

    /// <summary>周日首列展示列序</summary>
    public static readonly string[] DisplayDayOrder =
        ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

    /// <summary>按 key 找提供者 兼容裸 id</summary>
    private static (string PluginId, string Name, Func<List<ScheduleRow>> Provider)? ResolvePluginProvider(string key)
    {
        if (PluginService.ScheduleProviders.TryGetValue(key, out var direct))
        {
            return direct;
        }
        foreach (var kv in PluginService.ScheduleProviders)
        {
            if (string.Equals(kv.Value.PluginId, key, StringComparison.Ordinal))
            {
                return kv.Value;
            }
        }
        return null;
    }

    private static bool UseLocalScheduleData() => Config.ProfileSource.Value switch
    {
        "classisland" => !Config.ScheduleProfileCiFollow.Value,
        "classwidgets" => !Config.ScheduleProfileCwFollow.Value,
        _ => true,
    };

    /// <summary>当前课表来源对应的活动总档案配置项</summary>
    private static ConfigItem<string> ActiveScheduleItem() => Config.ProfileSource.Value switch
    {
        "classisland" => Config.ScheduleProfileCi,
        "classwidgets" => Config.ScheduleProfileCw,
        _ => Config.ScheduleProfile,
    };

    /// <summary>联动桥复用实例 Start 自带重入保护 路径每次同步</summary>
    private static LinkageBridgeBase? EnsureBridge()
    {
        LinkageBridgeBase? bridge;
        string path;
        if (Config.ProfileSource.Value == "classisland")
        {
            bridge = CiBridge;
            path = Config.LinkageDataPath.Value;
        }
        else if (Config.ProfileSource.Value == "classwidgets")
        {
            bridge = CwBridge;
            path = Config.ClassWidgetsDataPath.Value;
        }
        else
        {
            return null;
        }
        bridge.SetDataPath(path);
        if (!_bridgeStarted)
        {
            bridge.Start();
            _bridgeStarted = true;
        }
        return bridge;
    }

    /// <summary>活动总档案+时间表 读取失败返回空档</summary>
    private static (ScheduleProfile Master, TimetableDoc Timetable) LoadActiveMasterTimetable()
    {
        try
        {
            ScheduleStore.EnsureDefaults();
            var master = ScheduleStore.LoadMaster(ActiveScheduleItem().Value);
            // 过期覆盖回收
            if (master.CleanExpiredDateRules(AppUtils.PreciseNow()))
            {
                master.Save();
                Log.Info("[课表组件] 已清理过期日期覆盖");
            }
            return (master, TimetableDoc.LoadOrDefault(master.Timetable));
        }
        catch (Exception e)
        {
            Log.Warning($"[课表组件] 总档案读取失败: {e.Message}");
            return (new ScheduleProfile(), new TimetableDoc());
        }
    }

    /// <summary>当日课表 覆盖→轮换→回落</summary>
    private static (CourseDoc Course, ScheduleRules.DayOverride? WholeDay) ResolveDayCourse(ScheduleProfile master, DateOnly date)
    {
        var ov = ScheduleRules.ResolveOverride(master, date);
        if (ov is not null && ov.Kind != "plan")
        {
            // exam/holiday 占位 课程档回落仅作占位
            var fallback = ScheduleRules.ResolveCourse(master, date) ?? "";
            return (CourseDoc.LoadOrDefault(fallback), ov);
        }
        var courseName = (ov is not null && ov.CourseName.Length > 0 ? ov.CourseName : null)
                         ?? ScheduleRules.ResolveCourse(master, date)
                         ?? "";
        return (CourseDoc.LoadOrDefault(courseName), null);
    }

    /// <summary>整日占位行 sessions 优先</summary>
    private static List<ScheduleRow> WholeDayRows(ScheduleRules.DayOverride hit, bool isToday, TimeOnly nowTime)
    {
        var rows = new List<ScheduleRow>();
        if (hit.Kind == "exam" && hit.Entry["sessions"] is JsonArray sessions && sessions.Count > 0)
        {
            foreach (var s in sessions.OfType<JsonObject>())
            {
                var start = s["start"]?.GetValue<string>() ?? "";
                var end = s["end"]?.GetValue<string>() ?? "";
                var name = s["name"]?.GetValue<string>() ?? "";
                var cur = isToday && TimeOnly.TryParse(start, out var st) && TimeOnly.TryParse(end, out var et) &&
                          st <= nowTime && nowTime < et;
                rows.Add(new ScheduleRow("", "", start, end, 0, cur, true, name.Length > 0 ? name : hit.Name, ScheduleMark.Exam));
            }
            return rows;
        }
        // 整日占位 00:00~24:00
        rows.Add(new ScheduleRow("", "", "00:00", "24:00", 0, isToday, true, hit.Name, hit.Kind));
        return rows;
    }

    // 派生值 TTL 缓存 防每帧读盘
    private static readonly Dictionary<string, (object? Value, DateTime At)> DerivedCache = [];

    /// <summary>派生值缓存 5s 过期</summary>
    private static T? Cached<T>(string key, Func<T?> factory)
    {
        if (DerivedCache.TryGetValue(key, out var hit) && (AppUtils.PreciseNow() - hit.At).TotalSeconds < 5)
        {
            return (T?)hit.Value;
        }
        var value = factory();
        DerivedCache[key] = (value, AppUtils.PreciseNow());
        return value;
    }

    /// <summary>当前周次 + 奇偶</summary>
    public static (int Week, string? Parity)? GetWeekInfo() => Cached<(int Week, string? Parity)?>("week", () =>
    {
        try
        {
            ScheduleStore.EnsureDefaults();
            var master = ScheduleStore.LoadMaster(ActiveScheduleItem().Value);
            var today = DateOnly.FromDateTime(AppUtils.PreciseNow());
            var week = ScheduleRules.GetWeekNumber(today, master.WeekStart);
            return week is null ? null : (week.Value, ScheduleRules.WeekParityLabel(master, today));
        }
        catch (Exception e)
        {
            Log.Debug($"[课表组件] 周次读取失败: {e.Message}");
            return null;
        }
    });

    /// <summary>周次标签</summary>
    public static string? GetWeekLabel() => GetWeekInfo() is { } w
        ? AppUtils.Tr("schedule.rotation_week_chip", ("n", w.Week)) + (string.IsNullOrEmpty(w.Parity) ? "" : $" · {w.Parity}")
        : null;

    /// <summary>档案预备分钟 null=未配</summary>
    public static int? GetProfilePrepareMinutes() => Cached<int?>("prepare", () =>
    {
        try
        {
            ScheduleStore.EnsureDefaults();
            var master = ScheduleStore.LoadMaster(ActiveScheduleItem().Value);
            return master.PrepareMinutes > 0 ? master.PrepareMinutes : null;
        }
        catch (Exception e)
        {
            Log.Debug($"[课表组件] 预备分钟读取失败: {e.Message}");
            return null;
        }
    });

    /// <summary>科目色 hex 弱耦合</summary>
    public static string? GetSubjectColor(string subject)
    {
        if (subject.Length == 0)
        {
            return null;
        }
        return Cached($"color:{subject}", () =>
        {
            try
            {
                ScheduleStore.EnsureDefaults();
                var master = ScheduleStore.LoadMaster(ActiveScheduleItem().Value);
                var doc = SubjectDoc.LoadOrDefault(master.Subject);
                foreach (var r in doc.Rows)
                {
                    if ((r["name"]?.GetValue<string>() ?? "") == subject)
                    {
                        var color = r["color"]?.GetValue<string>() ?? "";
                        return color.Length > 0 ? color : null;
                    }
                }
                return null;
            }
            catch (Exception e)
            {
                Log.Debug($"[课表组件] 科目颜色读取失败: {e.Message}");
                return null;
            }
        });
    }

    // 时间轴节点
    public static List<Dictionary<string, object?>> BuildTimelineNodes()
    {
        var nodes = new List<Dictionary<string, object?>>();
        foreach (var row in GetTodaySchedule())
        {
            if (row.IsBreak)
            {
                if (nodes.Count > 0)
                {
                    nodes[^1]["break"] = new Dictionary<string, object?> { ["start"] = row.StartTime, ["end"] = row.EndTime };
                }
                continue;
            }
            // 连堂合并 仅渲染层
            if (nodes.Count > 0 &&
                row.Mark is null &&
                nodes[^1]["name"] is string prevName && prevName == row.Subject)
            {
                nodes[^1]["end"] = row.EndTime;
                continue;
            }
            nodes.Add(new Dictionary<string, object?>
            {
                ["name"] = row.Subject.Length > 0 ? row.Subject : "—",
                ["start"] = row.StartTime,
                ["end"] = row.EndTime,
                ["period"] = row.Index > 0 ? $"第{row.Index}节" : "—",
                ["break"] = null,
            });
        }
        return nodes;
    }
}

// 新闻热榜
// 规格: 360x220 边距16 间距6 头部SVG logo高30 + 4条富文本(序号12px/标题15px) 点击开链接 5min刷新+过期缓存

public sealed class NewsWidget : WidgetCardBase
{
    private const int ItemCount = 4;

    private readonly TextBlock[] _itemLabels = new TextBlock[ItemCount];
    private readonly string[] _urls = new string[ItemCount];
    private readonly Avalonia.Svg.Skia.Svg _logo;
    private readonly string _source;
    private readonly string _iconKey;

    public NewsWidget(ComponentDefinition definition, string source) : base(definition)
    {
        _source = source;
        _iconKey = source == "cctv" ? "cctv"
            : source == "tenxunwang" ? "tencent"
            : source; // baidu/weibo/jinritoutiao

        _logo = MakeLogo(30);

        // 原版 addWidget(widget, 1) 每条 stretch 均分 顶部 header 后 4 条等高
        var grid = new Grid
        {
            Margin = new Thickness(16, 16, 16, 16),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,*,*,*"),
            RowSpacing = 6,
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        header.Children.Add(_logo);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);
        for (var i = 0; i < ItemCount; i++)
        {
            var label = new SelectableTextBlock
            {
                Text = "--",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            var idx = i;
            label.PointerReleased += (_, _) => OpenNews(idx);
            _itemLabels[i] = label;
            Grid.SetRow(label, i + 1);
            grid.Children.Add(label);
        }
        Card.Child = grid;
        Content = Card;

        for (var i = 0; i < ItemCount; i++)
        {
            RenderItem(i, "--");
        }
        // 原版 300000ms 定时刷新
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        timer.Tick += (_, _) => RefreshNews();
        timer.Start();
        RefreshNews();
    }

    private Avalonia.Svg.Skia.Svg MakeLogo(double height)
    {
        try
        {
            var path = Paths.GetResourcePath(System.IO.Path.Combine("Assets", "icons", "news", _iconKey + ".svg"));
            if (File.Exists(path))
            {
                return new Avalonia.Svg.Skia.Svg(new Uri(path))
                {
                    Height = height,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    SvgSource = Avalonia.Svg.Skia.SvgSource.Load(path, null),
                };
            }
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 图标缺失留空: {e.Message}");
            // 图标缺失留空
        }
        return new Avalonia.Svg.Skia.Svg(new Uri("file:///nonexistent.svg")) { Height = 0 };
    }

    private async void RefreshNews()
    {
        JsonElement? data = null;
        try
        {
            data = _source == "cctv"
                ? await NewsService.FetchCctvNewsAsync()
                : await NewsService.FetchDailyNewsAsync(_source);
        }
        catch (Exception e)
        {

            Log.Warning($"[SW] 在线获取失败 走缓存: {e.Message}");
            // 接口失败走缓存
        }
        // 原版: 接口无数据用过期缓存
        if (data is null)
        {
            data = AppUtils.LoadCache($"news_{_source}", ignoreExpiry: true);
        }
        UpdateDisplay(data);
    }

    private void UpdateDisplay(JsonElement? data)
    {
        var titles = new string[ItemCount];
        for (var i = 0; i < ItemCount; i++)
        {
            titles[i] = "--";
            _urls[i] = "";
        }
        if (data is { ValueKind: JsonValueKind.Array } arr)
        {
            for (var i = 0; i < ItemCount && i < arr.GetArrayLength(); i++)
            {
                var item = arr[i];
                titles[i] = item.TryGetProperty("title", out var t) ? t.GetString() ?? "--"
                    : item.TryGetProperty("name", out var n) ? n.GetString() ?? "--" : "--";
                _urls[i] = item.TryGetProperty("url", out var u) ? u.GetString() ?? ""
                    : item.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
            }
        }
        for (var i = 0; i < ItemCount; i++)
        {
            RenderItem(i, titles[i]);
        }
    }

    // 原版 _render_items: 序号 12px 半透明 + 标题 15px 主题对色
    private void RenderItem(int index, string title)
    {
        var dark = CardIsDark;
        var label = _itemLabels[index];
        label.Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
            label.Inlines.Clear();
        var num = new Avalonia.Controls.Documents.Run($"{index + 1}. ")
        {
            FontSize = WidgetType.Label,
            Foreground = new SolidColorBrush(
                dark ? Colors.White : Colors.Black, CardInk.MutedOpacity),
        };
        var text = new Avalonia.Controls.Documents.Run(title)
        {
            FontSize = WidgetType.Body,
            Foreground = new SolidColorBrush(dark ? Color.Parse("#ffffff") : Color.Parse("#1a1a1a")),
        };
        label.Inlines.Add(num);
        label.Inlines.Add(text);
        label.LineHeight = 19;
        label.MaxLines = 2;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
    }

    private void OpenNews(int index)
    {
        if (index >= 0 && index < ItemCount && !string.IsNullOrEmpty(_urls[index]))
        {
            Log.Info($"[NewsComponent] 打开新闻: {_urls[index]}");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _urls[index],
                    UseShellExecute = true,
                });
            }
            catch (Exception e)
            {
                Log.Warning($"[NewsComponent] 打开失败: {e.Message}");
            }
        }
    }
}

// 今日课表预览
// 规格: 300x550 边距(10,10,10,10) 间距6 标题17px w600 行内容边距(10,7,10,7) 间距12
// 第X节15px(宽68) 课程名20px w500 时间15px 右对齐 当前行底部进度条3px #4cc2ff
// 行背景 深色 op*0.08/当前op*0.18/已过op*0.04 浅色 0.04/0.10/0.02 已过文字#999999

/// <summary>周课表 HTML 风 仅列色</summary>
public sealed class WeekTimetablePanel : UserControl
{
    private static readonly string[] DayOrder = TimetableScheduleProvider.DisplayDayOrder;

    public WeekTimetablePanel()
    {
        var week = TimetableScheduleProvider.GetWeekSchedule();
        var now = AppUtils.PreciseNow();
        var todayKey = now.DayOfWeek switch
        {
            DayOfWeek.Monday => "monday",
            DayOfWeek.Tuesday => "tuesday",
            DayOfWeek.Wednesday => "wednesday",
            DayOfWeek.Thursday => "thursday",
            DayOfWeek.Friday => "friday",
            DayOfWeek.Saturday => "saturday",
            _ => "sunday",
        };
        var dark = ThemeSense.IsDark(this);
        var accent = Common.ParseAccentColor(Config.ThemeColor.Value);
        var p = WidgetSurfacePalette.Resolve(dark);
        var textColor = p.Text;
        var subColor = p.Sub;
        var lineColor = p.Line;

        // 竖带按天交替 今日仅表头 accent
        var bandA = p.BandA;
        var bandB = p.BandB;
        var gutterBg = p.Gutter;

        // 时间线取最长日 位置对齐
        var reference = week.Values.OrderByDescending(r => r.Count).FirstOrDefault() ?? [];

        var cols = "96," + string.Join(",", Enumerable.Repeat("*", 7));
        var rowDefs = "Auto," + string.Join(",", Enumerable.Repeat("Auto", Math.Max(0, reference.Count)));

        // 无边框 底色成列带
        Border Cell(Control content, int r, int c, Color? bg)
        {
            var border = new Border
            {
                Background = bg is { } b ? new SolidColorBrush(b) : null,
                Padding = new Thickness(8, 10, 8, 10),
                MinHeight = 58,
                Child = content,
            };
            Grid.SetRow(border, r);
            Grid.SetColumn(border, c);
            return border;
        }

        // 表头 文字+横杠
        StackPanel HeadCell(string title, bool today)
        {
            var text = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(today ? accent : textColor),
                TextAlignment = TextAlignment.Center,
            };
            var bar = new Border
            {
                Height = 2,
                Margin = new Thickness(14, 9, 14, 0),
                Background = new SolidColorBrush(today ? accent : lineColor),
            };
            return new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { text, bar } };
        }

        // 科目主 教师次
        StackPanel SubjectCell(string subject, string teacher, bool current)
        {
            return new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = subject,
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(current ? accent : textColor),
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                    new TextBlock
                    {
                        Text = teacher.Length > 0 ? teacher : "—",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(current ? accent : subColor),
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 0),
                    },
                },
            };
        }

        // 表头行
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(cols) };
        header.Children.Add(Cell(HeadCell(AppUtils.Tr("timetable.time_range"), false), 0, 0, gutterBg));
        for (var d = 0; d < 7; d++)
        {
            var dayKey = DayOrder[d];
            header.Children.Add(Cell(HeadCell(AppUtils.Tr($"timetable.{dayKey}"), dayKey == todayKey), 0, d + 1, d % 2 == 1 ? bandB : bandA));
        }

        // 节次行
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), RowDefinitions = new RowDefinitions(rowDefs) };
        for (var i = 0; i < reference.Count; i++)
        {
            var refRow = reference[i];
            var isBreak = refRow.IsBreak;
            var current = refRow.IsCurrent;

            var gutter = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = isBreak ? (refRow.BreakName.Length > 0 ? refRow.BreakName : "课间") : $"第{refRow.Index}节",
                        FontSize = 15,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(current ? accent : textColor),
                        TextAlignment = TextAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = $"{refRow.StartTime}~{refRow.EndTime}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(subColor),
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 3, 0, 0),
                    },
                },
            };
            body.Children.Add(Cell(gutter, i, 0, gutterBg));

            for (var d = 0; d < 7; d++)
            {
                var dayKey = DayOrder[d];
                var rows = week.GetValueOrDefault(dayKey) ?? [];
                var bg = d % 2 == 1 ? bandB : bandA;
                if (i < rows.Count && !rows[i].IsBreak)
                {
                    var row = rows[i];
                    body.Children.Add(Cell(SubjectCell(row.Subject, row.Teacher, current), i, d + 1, bg));
                }
                else
                {
                    // 课间空档 同列带
                    body.Children.Add(Cell(new TextBlock(), i, d + 1, bg));
                }
            }
        }

        if (reference.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = AppUtils.Tr("timetable.day_empty"),
                FontSize = 13,
                Opacity = 0.45,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 24, 0, 0),
            });
            Grid.SetColumnSpan(body.Children[^1], 8);
        }

        // 表头固定 行区滚
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(header);
        var scroll = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
    }
}

public sealed class TimetablePreviewWidget : WidgetCardBase
{
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _dateText = new();
    private readonly TextBlock _countText = new();
    private readonly Border _countChip = new();
    // Grid 行宿主: 星号均分 课少时撑满整卡
    private readonly Grid _rowsHost = new() { RowSpacing = 6, ClipToBounds = true };
    private readonly List<(Border Row, ProgressBar Bar, bool IsCurrent)> _rows = new();
    private (string Start, string End)? _currentTimes;

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _progressTimer;
    private string _sig = "";

    public TimetablePreviewWidget(ComponentDefinition definition) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(12, 10, 12, 12),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            RowSpacing = 8,
        };

        // 头部: 标题 + 日期, 右侧课程数胶囊
        var header = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto"),
            ColumnSpacing = 10,
        };
        var titleStack = new StackPanel { Spacing = 1 };
        _titleText.Text = AppUtils.Tr("timetable.today_schedule");
        _titleText.FontSize = 17;
        _titleText.FontWeight = FontWeight.SemiBold;
        _dateText.FontSize = 11;
        titleStack.Children.Add(_titleText);
        titleStack.Children.Add(_dateText);

        _countText.FontSize = 11;
        _countChip.Padding = new Thickness(9, 4, 9, 4);
        _countChip.CornerRadius = new CornerRadius(10);
        _countChip.VerticalAlignment = VerticalAlignment.Center;
        _countChip.Child = _countText;

        Grid.SetColumn(titleStack, 0);
        Grid.SetColumn(_countChip, 1);
        header.Children.Add(titleStack);
        header.Children.Add(_countChip);

        Grid.SetRow(header, 0);
        Grid.SetRow(_rowsHost, 1);
        root.Children.Add(header);
        root.Children.Add(_rowsHost);
        Card.Child = root;
        Content = Card;

        ApplyRowStyle();
        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyRowStyle();
            _sig = "";
            RefreshSchedule();
        };
        Config.ComponentCardOpacity.ValueChanged += _ => { _sig = ""; RefreshSchedule(); };
        Config.ComponentCardRadius.ValueChanged += _ => { _sig = ""; RefreshSchedule(); };

        // 原版 showEvent: 5s 刷新 + 1s 进度
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) => RefreshSchedule();
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _refreshTimer.Start();
        _progressTimer.Start();
        RefreshSchedule();

        // 展开层 单日弹整周
        EnableExpansion(AppUtils.Tr("timetable.week_schedule"), () => new WeekTimetablePanel());
    }

    private void RefreshSchedule()
    {
        var schedule = TimetableScheduleProvider.GetTodaySchedule();
        var now = AppUtils.PreciseNow();
        UpdateHeader(schedule, now);

        // 重建签名
        var parts = new List<string>();
        foreach (var row in schedule)
        {
            var isPast = !row.IsBreak && !row.IsCurrent && TimeOnly.TryParse(row.EndTime, out var et) && et <= TimeOnly.FromDateTime(now);
            if (row.IsBreak && !row.IsCurrent)
            {
                continue; // 非当前课间行跳过
            }
            parts.Add($"{row.Subject}|{row.StartTime}|{row.EndTime}|{row.Index}|{row.IsBreak}|{row.BreakName}|{isPast}");
        }
        var sig = string.Join(";", parts);
        if (sig == _sig && _rowsHost.Children.Count > 0)
        {
            UpdateProgress();
            return;
        }
        _sig = sig;
        RebuildRows(schedule, now);
    }

    private void UpdateHeader(List<ScheduleRow> schedule, DateTime now)
    {
        _dateText.Text = $"{now:MM/dd} · {AppUtils.Tr(AppUtils.WeekdayKey(now.DayOfWeek))}";
        var courses = schedule.Count(r => !r.IsBreak && r.Subject.Trim().Length > 0);
        _countText.Text = AppUtils.Tr("timetable.lessons_count", ("n", courses));
        _countChip.IsVisible = courses > 0;
    }

    private void RebuildRows(List<ScheduleRow> schedule, DateTime now)
    {
        _rowsHost.Children.Clear();
        _rowsHost.RowDefinitions.Clear();
        _rows.Clear();
        _currentTimes = null;
        ScheduleRow? currentRow = null;

        // 先找最后一个 current
        foreach (var row in schedule)
        {
            if (row.IsCurrent)
            {
                currentRow = row;
            }
        }

        // 没有课程时, 不再把唯一的空节次拉满整张卡, 改为完整空态
        var hasCourse = schedule.Any(r => !r.IsBreak && r.Subject.Trim().Length > 0);
        if (schedule.Count == 0 || !hasCourse)
        {
            _rowsHost.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            var empty = BuildEmptyState();
            Grid.SetRow(empty, 0);
            _rowsHost.Children.Add(empty);
            return;
        }

        // 行数少 -> 星号均分撑满; 多 -> 定量高避免压扁
        var visible = schedule.Count(r => !(r.IsBreak && !r.IsCurrent));
        var stretch = visible <= 9;
        var rowIdx = 0;

        foreach (var row in schedule)
        {
            if (row.IsBreak && !row.IsCurrent)
            {
                continue;
            }
            var isPast = !row.IsBreak && !row.IsCurrent &&
                TimeOnly.TryParse(row.EndTime, out var et) && et <= TimeOnly.FromDateTime(now);
            var isCurrentRow = ReferenceEquals(row, currentRow);
            if (isCurrentRow)
            {
                _currentTimes = (row.StartTime, row.EndTime);
            }

            var content = new Grid
            {
                Margin = new Thickness(12, 8, 12, 8),
                ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("4,58,*,Auto"),
                ColumnSpacing = 10,
            };
            var status = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            TextBlock idx;
            if (row.IsBreak)
            {
                idx = new TextBlock();
            }
            else
            {
                idx = new TextBlock
                {
                    Text = $"第{row.Index}节",
                    TextAlignment = TextAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            var subj = new TextBlock
            {
                Text = row.IsBreak ? (row.BreakName.Length > 0 ? row.BreakName : "课间") : (row.Subject.Length > 0 ? row.Subject : "—"),
                FontWeight = FontWeight.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var time = new TextBlock
            {
                Text = $"{row.StartTime}–{row.EndTime}",
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(status, 0);
            Grid.SetColumn(idx, 1);
            Grid.SetColumn(subj, 2);
            Grid.SetColumn(time, 3);
            content.Children.Add(status);
            content.Children.Add(idx);
            content.Children.Add(subj);
            content.Children.Add(time);

            var rowGrid = new Grid { RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto") };
            var bar = new ProgressBar
            {
                Height = 3,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                CornerRadius = new CornerRadius(2),
                IsVisible = isCurrentRow,
            };
            Grid.SetRow(content, 0);
            Grid.SetRow(bar, 1);
            rowGrid.Children.Add(content);
            rowGrid.Children.Add(bar);

            var border = new Border { Child = rowGrid };
            _rowsHost.RowDefinitions.Add(new RowDefinition(
                stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto));
            Grid.SetRow(border, rowIdx++);
            _rowsHost.Children.Add(border);
            _rows.Add((border, bar, isCurrentRow));
            StyleRow(border, subj, time, idx, status, bar, isCurrentRow, isPast);
        }
        UpdateProgress();
    }

    private Grid BuildEmptyState()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var root = new Grid { Margin = new Thickness(0, 2, 0, 2) };

        var empty = new CardEmptyState(
            FAIcons.Glyph(AppIcons.Calendar),
            AppUtils.Tr("timetable.day_empty"),
            AppUtils.Tr("component.click_to_settings"));
        empty.Apply(p);
        root.Children.Add(empty);
        return root;
    }

    // 行样式
    private void StyleRow(Border border, TextBlock subj, TextBlock time, TextBlock idx, Border status,
        ProgressBar bar, bool isCurrentRow, bool isPast)
    {
        var dark = CardIsDark;
        var p = WidgetSurfacePalette.Resolve(dark);
        var op = Math.Clamp(Config.ComponentCardOpacity.Value / 100.0, 0, 1);
        IBrush bg;
        if (isCurrentRow)
        {
            bg = CardAccent.Brush(dark ? 0.14 : 0.09);
        }
        else if (isPast)
        {
            bg = new SolidColorBrush(dark ? Colors.White : Colors.Black, op * (dark ? 0.025 : 0.018));
        }
        else
        {
            bg = new SolidColorBrush(dark ? Colors.White : Colors.Black, op * (dark ? 0.055 : 0.035));
        }
        border.Background = bg;
        border.CornerRadius = new CornerRadius(Math.Max(9, Config.ComponentCardRadius.Value - 2));

        var ink = isPast ? new SolidColorBrush(p.Text, 0.42) : CardInk.Fg(p);
        subj.Foreground = ink;
        subj.FontSize = WidgetType.Body;
        subj.FontWeight = isCurrentRow ? FontWeight.SemiBold : FontWeight.Medium;
        time.Foreground = isPast ? new SolidColorBrush(p.Text, 0.38) : CardInk.Muted(p);
        time.FontSize = 12;
        idx.Foreground = isCurrentRow ? CardAccent.Brush() : new SolidColorBrush(p.Text, 0.45);
        idx.FontSize = 11;
        status.Background = isCurrentRow ? CardAccent.Brush() : new SolidColorBrush(p.Text, 0.12);
        bar.Foreground = CardAccent.Brush();
        bar.Background = new SolidColorBrush(dark ? Colors.White : Colors.Black, dark ? 0.06 : 0.05);
        bar.CornerRadius = new CornerRadius(2);
    }

    private void ApplyRowStyle()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _titleText.Foreground = CardInk.Fg(p);
        _dateText.Foreground = CardInk.Muted(p);
        _countText.Foreground = CardInk.Muted(p);
        _countChip.Background = CardAccent.Brush(0.10);
        _countChip.BorderBrush = CardAccent.Brush(0.16);
        _countChip.BorderThickness = new Thickness(1);
    }

    // 当前行进度
    private void UpdateProgress()
    {
        if (_currentTimes is null)
        {
            return;
        }
        var (startS, endS) = _currentTimes.Value;
        if (!TimeOnly.TryParse(startS, out var st) || !TimeOnly.TryParse(endS, out var et))
        {
            return;
        }
        var now = TimeOnly.FromDateTime(AppUtils.PreciseNow());
        var total = (et - st).TotalSeconds;
        if (total <= 0)
        {
            return;
        }
        var elapsed = (now - st).TotalSeconds;
        var pct = Math.Clamp(elapsed / total * 100, 0, 100);
        foreach (var (_, bar, isCurrent) in _rows)
        {
            if (isCurrent)
            {
                bar.Value = pct;
                return;
            }
        }
    }
}

// 当前课程
// 规格: 400x200 边距(16,10,16,8) 左右栏 间距14 左列(3:1:1) 课程名40px w600(上20下4)
// 倒计时18px accent 时间进度15px 右列(间距8) 老师/时间段19px w600 下节课19px sub色
// 底部进度条6px 配置 show_teacher/show_next/show_duration/show_countdown/prepare_minutes(3)

public sealed class TimetableNowLessonWidget : WidgetCardBase
{
    private readonly TextBlock _subjectText = new();
    // 主题前景 供科目色回落
    private IBrush? _subjectFg;
    private readonly TextBlock _countdownText = new();
    private readonly TextBlock _timeProgressText = new();
    private readonly TextBlock _teacherText = new();
    private readonly TextBlock _timeText = new();
    private readonly TextBlock _nextText = new();
    private readonly ProgressBar _bottomProgress = new();
    private readonly DispatcherTimer _timer;
    private readonly Grid _main = new() { ColumnSpacing = 14 };
    private readonly Grid _left = new() { RowSpacing = 4, MinWidth = 100 };
    private readonly Grid _right = new() { RowSpacing = 8 };

    private bool _showTeacher = true;
    private bool _showNext = true;
    private bool _showDuration = true;
    private bool _showCountdown = true;
    private int _prepareMinutes = 3;
    private bool _prepareFromConfig;
    private int _fontScale = 100;

    public TimetableNowLessonWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(16, 10, 16, 8),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto"),
        };
        _main.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,*");

        // 左: 课程名(3) 倒计时(1) 时间进度(1) 原版均 AlignLeft|AlignVCenter
        _left.RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("3*,*,*");
        _subjectText.Margin = new Thickness(0, 20, 0, 4);
        _subjectText.TextWrapping = TextWrapping.Wrap;
        _subjectText.VerticalAlignment = VerticalAlignment.Center;
        _countdownText.Margin = new Thickness(4, 2, 0, 2);
        _countdownText.VerticalAlignment = VerticalAlignment.Center;
        _timeProgressText.Margin = new Thickness(4, 2, 0, 2);
        _timeProgressText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_subjectText, 0);
        Grid.SetRow(_countdownText, 1);
        Grid.SetRow(_timeProgressText, 2);
        _left.Children.Add(_subjectText);
        _left.Children.Add(_countdownText);
        _left.Children.Add(_timeProgressText);

        // 右: 老师 时间段 下节课 + 尾部留白(原版 addStretch) -> 4 等分行 内容垂直居中
        _right.RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,*,*,*");
        _teacherText.Margin = new Thickness(0, 2, 0, 2);
        _teacherText.VerticalAlignment = VerticalAlignment.Center;
        _timeText.Margin = new Thickness(0, 2, 0, 2);
        _timeText.VerticalAlignment = VerticalAlignment.Center;
        _nextText.Margin = new Thickness(0, 2, 0, 2);
        _nextText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_teacherText, 0);
        Grid.SetRow(_timeText, 1);
        Grid.SetRow(_nextText, 2);
        _right.Children.Add(_teacherText);
        _right.Children.Add(_timeText);
        _right.Children.Add(_nextText);

        Grid.SetColumn(_left, 0);
        Grid.SetColumn(_right, 1);
        _main.Children.Add(_left);
        _main.Children.Add(_right);

        _bottomProgress.Height = 4;
        _bottomProgress.CornerRadius = new CornerRadius(2);
        _bottomProgress.Minimum = 0;
        _bottomProgress.Maximum = 100;
        _bottomProgress.Value = 0;
        Grid.SetRow(_main, 0);
        Grid.SetRow(_bottomProgress, 1);
        root.Children.Add(_main);
        root.Children.Add(_bottomProgress);
        Card.Child = root;
        Content = Card;

        ReadConfig(config);
        ApplyCardStyle();
        ApplyStyle();
        ActualThemeVariantChanged += (_, _) => ApplyStyle();
        SizeChanged += (_, _) => Relayout();

        // 原版 1s 起步 正常态 5s
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    private void ReadConfig(JsonElement? config)
    {
        ReadBgOverride(config);
        if (config is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }
        if (obj.TryGetProperty("show_teacher", out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _showTeacher = v.GetBoolean();
        }
        if (obj.TryGetProperty("show_next", out var v2) && v2.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _showNext = v2.GetBoolean();
        }
        if (obj.TryGetProperty("show_duration", out var v3) && v3.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _showDuration = v3.GetBoolean();
        }
        if (obj.TryGetProperty("show_countdown", out var v4) && v4.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _showCountdown = v4.GetBoolean();
        }
        if (obj.TryGetProperty("prepare_minutes", out var p) && p.ValueKind == JsonValueKind.Number)
        {
            _prepareMinutes = p.GetInt32();
            _prepareFromConfig = true;
        }
        if (obj.TryGetProperty("font_scale", out var f) && f.ValueKind == JsonValueKind.Number)
        {
            _fontScale = f.GetInt32();
        }
    }

    private void Refresh()
    {
        var schedule = TimetableScheduleProvider.GetTodaySchedule();
        var now = AppUtils.PreciseNow();
        // 控件值优先 回落档案
        if (!_prepareFromConfig && TimetableScheduleProvider.GetProfilePrepareMinutes() is { } fromProfile)
        {
            _prepareMinutes = fromProfile;
        }

        ScheduleRow? currentRow = null;
        ScheduleRow? nextRow = null;
        var foundCurrent = false;
        foreach (var row in schedule)
        {
            if (row.IsCurrent)
            {
                currentRow = row;
                foundCurrent = true;
            }
            else if (foundCurrent && !row.IsBreak)
            {
                nextRow = row;
                break;
            }
        }

        // 课前播报: 课间中 且 距下节课开始 <= prepare_minutes
        ScheduleRow? prepareRow = null;
        if (currentRow is { IsBreak: true })
        {
            prepareRow = nextRow;
        }
        var inPrepare = false;
        if (prepareRow is not null && TimeOnly.TryParse(prepareRow.StartTime, out var pst))
        {
            var nextStart = now.Date.AddHours(pst.Hour).AddMinutes(pst.Minute);
            var diff = (nextStart - now).TotalSeconds;
            if (diff > 0 && diff <= _prepareMinutes * 60)
            {
                inPrepare = true;
            }
        }

        // 原版: 课前播报 1s 正常 5s
        _timer.Interval = TimeSpan.FromSeconds(inPrepare ? 1 : 5);

        if (inPrepare)
        {
            RenderPrepare(prepareRow!, now);
        }
        else if (currentRow is not null)
        {
            RenderNormal(currentRow, nextRow, now);
        }
        else
        {
            _subjectText.Text = AppUtils.Tr("timetable.day_empty");
            _countdownText.Text = "";
            _teacherText.Text = "";
            _timeText.Text = "";
            _nextText.Text = "下节课 --";
            _timeProgressText.Text = "";
            _bottomProgress.Value = 0;
        }
        FitSubject();
    }

    // 断点 + 字号: 窄卡收右栏 科目按左列宽收敛 其余按卡高分档
    private void Relayout()
    {
        var w = Bounds.Width - 32;
        var h = Bounds.Height - 18;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        var narrow = w < 300;
        _right.IsVisible = !narrow;
        Grid.SetColumnSpan(_left, narrow ? 2 : 1);
        _timeProgressText.IsVisible = h >= 96;

        var scale = _fontScale / 100.0;
        _countdownText.FontSize = CardText.ByHeight(h, 0.11 * scale, 12, 30);
        _timeProgressText.FontSize = CardText.ByHeight(h, 0.095 * scale, 10, 26);
        var sideSize = CardText.ByHeight(h / 4.0, 0.48 * scale, 11, 24);
        _teacherText.FontSize = sideSize;
        _timeText.FontSize = sideSize;
        _nextText.FontSize = sideSize;
        _subjectText.Margin = new Thickness(0, h < 110 ? 2 : 20, 0, 4);

        FitSubject();
    }

    // 科目名单行收敛 优先占满左列
    private void FitSubject()
    {
        var w = Bounds.Width - 32;
        if (w <= 1)
        {
            return;
        }
        var leftW = (w < 300 ? w : (w - 14) / 2) - 8;
        var scale = _fontScale / 100.0;
        _subjectText.FontSize = CardText.Fit(_subjectText.Text, Common.AppFontFamily, FontWeight.SemiBold,
            Math.Max(40, leftW), 40 * scale, 14, 96);
    }

    // 切页隐藏即停表 回页补刷
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || _timer is null)
        {
            return;
        }
        if (!IsVisible)
        {
            _timer.Stop();
        }
        else if (!_timer.IsEnabled)
        {
            Refresh();
            _timer.Start();
        }
    }

    // 课间态
    private void RenderPrepare(ScheduleRow prepare, DateTime now)
    {
        _subjectText.Text = prepare.Subject.Length > 0 ? prepare.Subject : "下节课";
        ApplySubjectColor(_subjectText.Text);

        if (_showCountdown && TimeOnly.TryParse(prepare.StartTime, out var st))
        {
            var nextStart = now.Date.AddHours(st.Hour).AddMinutes(st.Minute);
            var totalSec = Math.Max(0, (int)(nextStart - now).TotalSeconds);
            _countdownText.Text = $"{totalSec / 60}:{totalSec % 60:D2} 后上课";
        }
        else
        {
            _countdownText.Text = "";
        }

        _timeProgressText.Text = "";
        _teacherText.Text = _showTeacher ? TeacherDisplay(prepare.Teacher) : "";
        _timeText.Text = $"{prepare.StartTime}~{prepare.EndTime}";

        if (_showDuration && TimeOnly.TryParse(prepare.StartTime, out var s) && TimeOnly.TryParse(prepare.EndTime, out var e))
        {
            var totalMin = (e.Hour * 60 + e.Minute) - (s.Hour * 60 + s.Minute);
            _nextText.Text = $"时长 {totalMin}分钟";
        }
        else
        {
            _nextText.Text = "";
        }

        if (TimeOnly.TryParse(prepare.StartTime, out var ps))
        {
            var diff = (now.Date.AddHours(ps.Hour).AddMinutes(ps.Minute) - now).TotalSeconds;
            var totalPrepare = _prepareMinutes * 60;
            _bottomProgress.Value = totalPrepare > 0
                ? Math.Clamp((1 - diff / totalPrepare) * 100, 0, 100)
                : 0;
        }
        else
        {
            _bottomProgress.Value = 0;
        }
    }

    // 命中覆色 否则还原主题刷子
    private void ApplySubjectColor(string subject)
    {
        if (TimetableScheduleProvider.GetSubjectColor(subject) is { } hex)
        {
            try
            {
                _subjectText.Foreground = new SolidColorBrush(Color.Parse(hex));
                return;
            }
            catch (Exception e)
            {
                Log.Debug($"[课表组件] 科目颜色解析失败: {hex} {e.Message}");
            }
        }
        if (_subjectFg is not null)
        {
            _subjectText.Foreground = _subjectFg;
        }
    }

    // 上课态
    private void RenderNormal(ScheduleRow current, ScheduleRow? nextRow, DateTime now)
    {
        _countdownText.Text = "";
        // 考试/假期 整日占位 不按课间渲染
        var wholeDay = current.Mark is ScheduleMark.Exam or ScheduleMark.Holiday;
        _subjectText.Text = current.IsBreak
            ? (current.BreakName.Length > 0 ? current.BreakName : "课间休息")
            : (current.Subject.Length > 0 ? current.Subject : "--");

        _teacherText.Text = _showTeacher
            ? (current.IsBreak && !wholeDay ? "课间休息" : TeacherDisplay(current.Teacher))
            : "";
        var week = TimetableScheduleProvider.GetWeekLabel();
        _timeText.Text = wholeDay
            ? week ?? ""
            : $"{current.StartTime}~{current.EndTime}" + (week is null ? "" : $" · {week}");
        if (!current.IsBreak)
        {
            ApplySubjectColor(_subjectText.Text);
        }

        if (_showNext)
        {
            _nextText.Text = nextRow is not null && nextRow.Subject.Length > 0
                ? $"下节课 {nextRow.Subject}"
                : "下节课 --";
        }
        else
        {
            _nextText.Text = "";
        }

        if (_showDuration && TimeOnly.TryParse(current.StartTime, out var s) && TimeOnly.TryParse(current.EndTime, out var e))
        {
            var startSec = s.Hour * 3600 + s.Minute * 60;
            var endSec = e.Hour * 3600 + e.Minute * 60;
            var nowSec = now.Hour * 3600 + now.Minute * 60 + now.Second;
            var total = endSec - startSec;
            var elapsed = Math.Max(0, nowSec - startSec);
            _bottomProgress.Value = total > 0 ? Math.Min(100, elapsed / (double)total * 100) : 0;
            _timeProgressText.Text = $"{elapsed / 60}min / {total / 60}min";
        }
        else
        {
            _timeProgressText.Text = "";
            _bottomProgress.Value = 0;
        }
    }

    // 原版 teacher[0]+"老师"
    private static string TeacherDisplay(string teacher) =>
        teacher.Length > 0 ? $"{teacher[0]}老师" : "";

    private void ApplyStyle()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var text = p.Text;
        var muted = new SolidColorBrush(text, CardInk.MutedOpacity);
        var scale = _fontScale / 100.0;
        double S(int px) => Math.Max(1, px * scale);

        // 课名是英雄字 Light 其余分级: 正文全值 / 次级 62%
        _subjectFg = new SolidColorBrush(text);
        _subjectText.Foreground = _subjectFg;
        _subjectText.FontSize = S(40);
        _subjectText.FontWeight = FontWeight.Light;
        _countdownText.Foreground = CardAccent.Brush();
        _countdownText.FontSize = S(18);
        _countdownText.FontWeight = FontWeight.SemiBold;
        _timeProgressText.Foreground = muted;
        _timeProgressText.FontSize = S(15);
        _teacherText.Foreground = muted;
        _teacherText.FontSize = S(19);
        _timeText.Foreground = muted;
        _timeText.FontSize = S(19);
        _nextText.Foreground = muted;
        _nextText.FontSize = S(19);
        _bottomProgress.Foreground = CardAccent.Brush();
        Relayout();
    }
}

// 媒体播放器
// 规格: 400x200 内容边距(16,20,12,20) 间距14 封面160x160 圆角10 淡入300ms
// 标题19px w700 h28 歌手11px w500 h16 歌词12px w700 进度条3px 时间10px 右对齐
// 按钮行间距16 居中 prev28x28(icon20) play32x32(icon24) next28x28
// 色板 深色 标题#F5F5FA 歌手rgba(230,230,235,200) 时间rgba(220,220,225,150) 歌词rgba(235,235,240,170)
//       浅色 标题#1F1F1F 歌手rgba(60,60,67,214) 时间rgba(60,60,67,138) 歌词rgba(44,44,46,153)
// 默认封面 深(40,40,48)/浅(230,230,235) 音符 手绘音符形状

/// <summary>媒体进度条: 3px 细轨 + 强调色已播放段 + 末端圆点</summary>
internal sealed class MediaProgressBar : Control
{
    private double _progress;

    public Func<bool>? DarkProvider { get; set; }

    public double Progress
    {
        get => _progress;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (Math.Abs(v - _progress) < 0.0001)
            {
                return;
            }
            _progress = v;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 20 || h < 3)
        {
            return;
        }

        var dark = DarkProvider?.Invoke() ?? ThemeSense.IsDark(this);
        var p = WidgetSurfacePalette.Resolve(dark);
        var accent = CardAccent.Value;
        var trackH = Math.Min(3, h);
        var y = (h - trackH) / 2;
        var radius = trackH / 2;

        ctx.DrawRectangle(new SolidColorBrush(p.Text, dark ? 0.12 : 0.09), null,
            new Rect(0, y, w, trackH), radius, radius);
        if (_progress <= 0)
        {
            return;
        }
        var fillW = Math.Max(trackH, w * _progress);
        ctx.DrawRectangle(new SolidColorBrush(accent), null,
            new Rect(0, y, fillW, trackH), radius, radius);
        var knob = trackH + 3;
        ctx.DrawEllipse(new SolidColorBrush(accent), null,
            new Point(Math.Clamp(fillW, knob / 2, w - knob / 2), h / 2), knob / 2, knob / 2);
    }
}

// 媒体控制圆键: 不继承 Fluent Button 模板, 避免悬停时绿色的播放键被主题灰底盖掉
internal sealed class MediaTransportButton : Border
{
    private readonly TextBlock _glyph = new();
    private readonly bool _accent;
    private bool _hover;
    private bool _pressed;
    private Color _accentColor = Color.Parse("#68C31F");
    private Color _glyphColor = Colors.White;

    public event Action? Clicked;

    public MediaTransportButton(bool accent)
    {
        _accent = accent;
        Child = _glyph;
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);
        _glyph.FontFamily = FAIcons.SymbolsFont;
        _glyph.HorizontalAlignment = HorizontalAlignment.Center;
        _glyph.VerticalAlignment = VerticalAlignment.Center;
        _glyph.TextAlignment = TextAlignment.Center;
        _glyph.IsHitTestVisible = false;
    }

    public string Glyph
    {
        get => _glyph.Text ?? "";
        set => _glyph.Text = value;
    }

    public void Configure(double size, double glyphSize, Color accent, Color glyphColor)
    {
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(size / 2);
        _glyph.FontSize = glyphSize;
        _accentColor = accent;
        _glyphColor = glyphColor;
        Refresh();
    }

    private void Refresh()
    {
        if (_accent)
        {
            Background = new SolidColorBrush(_accentColor, _pressed ? 0.82 : 1.0);
            _glyph.Foreground = new SolidColorBrush(_glyphColor);
            BoxShadow = _hover
                ? new BoxShadows(new BoxShadow
                {
                    Blur = 14,
                    Color = Color.FromArgb(110, _accentColor.R, _accentColor.G, _accentColor.B),
                })
                : default;
        }
        else
        {
            Background = _hover ? new SolidColorBrush(_glyphColor, 0.12) : Brushes.Transparent;
            _glyph.Foreground = new SolidColorBrush(_glyphColor, _pressed ? 1.0 : _hover ? 0.95 : 0.72);
            BoxShadow = default;
        }
        RenderTransformOrigin = RelativePoint.Center;
        RenderTransform = _pressed ? new ScaleTransform(0.92, 0.92) : null;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        Refresh();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        _pressed = false;
        e.Pointer.Capture(null);
        Refresh();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _pressed = true;
        e.Pointer.Capture(this);
        Refresh();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var fire = _pressed;
        _pressed = false;
        e.Pointer.Capture(null);
        Refresh();
        if (fire)
        {
            var p = e.GetPosition(this);
            if (p.X >= 0 && p.Y >= 0 && p.X <= Bounds.Width && p.Y <= Bounds.Height)
            {
                Clicked?.Invoke();
            }
        }
        e.Handled = true;
    }
}

public sealed class MediaPlayerWidget : WidgetCardBase
{
    private sealed record MediaDetail(Lyrics? Lyrics, byte[]? Cover, long Duration);

    private readonly Border _coverHost = new();
    private readonly StackPanel _appPanel = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 5,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border _appDot = new()
    {
        Width = 6,
        Height = 6,
        CornerRadius = new CornerRadius(3),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _appText = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _artistText = new();
    private readonly TextBlock _lyricsText = new();
    private readonly TextBlock _timeText = new();
    private readonly MediaProgressBar _wave = new();
    private readonly StackPanel _centerBlock = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 4,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Grid _bottomRow;
    private readonly MediaTransportButton _btnPrev;
    private readonly MediaTransportButton _btnPlay;
    private readonly MediaTransportButton _btnNext;

    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _progTimer;

    private MediaInfo? _media;
    private Lyrics? _lyrics;
    private string _lastTa = "";
    private bool _hasThumb;
    private bool _playing;
    private long _position;
    private long _duration;
    private bool _polling;
    private bool _controlBusy;
    private int _detailVersion;
    private long _stateHoldUntil;
    private readonly Dictionary<string, MediaDetail> _infoCache = new();

    public MediaPlayerWidget(ComponentDefinition definition) : base(definition)
    {
        // 左封面 + 右信息: 来源/标题/歌手/歌词/波形/控制键
        var root = new Grid
        {
            Margin = new Thickness(14),
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*"),
            ColumnSpacing = 14,
        };

        _coverHost.CornerRadius = new CornerRadius(12);
        _coverHost.BorderThickness = new Thickness(1);
        _coverHost.ClipToBounds = true;
        _coverHost.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_coverHost, 0);
        root.Children.Add(_coverHost);

        var right = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,Auto,*,Auto,Auto"),
            RowSpacing = 2,
            Margin = new Thickness(0, 1, 0, 1),
        };
        // 关键: 右侧信息必须放在第 2 列, 否则会和封面叠在同一格
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        _appText.FontSize = 10.5;
        _appText.FontWeight = FontWeight.Medium;
        _appPanel.Children.Add(_appDot);
        _appPanel.Children.Add(_appText);
        Grid.SetRow(_appPanel, 0);
        right.Children.Add(_appPanel);

        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _titleText.TextWrapping = TextWrapping.Wrap;
        _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        _titleText.MaxLines = 2;
        Grid.SetRow(_titleText, 1);
        right.Children.Add(_titleText);

        _artistText.VerticalAlignment = VerticalAlignment.Center;
        _artistText.TextWrapping = TextWrapping.NoWrap;
        _artistText.TextTrimming = TextTrimming.CharacterEllipsis;
        _artistText.MaxLines = 1;
        Grid.SetRow(_artistText, 2);
        right.Children.Add(_artistText);

        _lyricsText.TextWrapping = TextWrapping.NoWrap;
        _lyricsText.TextTrimming = TextTrimming.CharacterEllipsis;
        _lyricsText.MaxLines = 1;
        _lyricsText.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            },
        };
        _centerBlock.Children.Add(_lyricsText);
        Grid.SetRow(_centerBlock, 3);
        right.Children.Add(_centerBlock);

        // 进度条独立一行 紧贴控制键上方 比伪波形更安静
        _wave.Margin = new Thickness(0, 3, 0, 2);
        Grid.SetRow(_wave, 4);
        right.Children.Add(_wave);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _btnPrev = new MediaTransportButton(false);
        _btnPlay = new MediaTransportButton(true);
        _btnNext = new MediaTransportButton(false);
        btnRow.Children.Add(_btnPrev);
        btnRow.Children.Add(_btnPlay);
        btnRow.Children.Add(_btnNext);

        _timeText.TextAlignment = TextAlignment.Right;
        _timeText.HorizontalAlignment = HorizontalAlignment.Stretch;
        _timeText.VerticalAlignment = VerticalAlignment.Center;
        _timeText.TextTrimming = TextTrimming.CharacterEllipsis;
        _timeText.MaxLines = 1;
        _timeText.Margin = new Thickness(8, 0, 0, 0);

        _bottomRow = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(btnRow, 0);
        Grid.SetColumn(_timeText, 1);
        _bottomRow.Children.Add(btnRow);
        _bottomRow.Children.Add(_timeText);
        Grid.SetRow(_bottomRow, 5);
        right.Children.Add(_bottomRow);

        Card.Child = root;
        Content = Card;

        _btnPrev.Clicked += () => _ = ControlAsync("prev");
        _btnPlay.Clicked += () => _ = TogglePlayAsync();
        _btnNext.Clicked += () => _ = ControlAsync("next");

        // 轮询间隔 = cfg.mediaUpdateInterval 秒
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, Config.MediaUpdateInterval.Value)) };
        _pollTimer.Tick += (_, _) => _ = PollAsync();
        _progTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _progTimer.Tick += (_, _) => UpdateProgress();

        _wave.DarkProvider = () => CardIsDark;
        Config.ShowMediaInfo.ValueChanged += _ => IsVisible = Config.ShowMediaInfo.Value;
        Config.ShowMediaCover.ValueChanged += _ =>
        {
            _coverHost.IsVisible = Config.ShowMediaCover.Value;
            ApplyStyle();
        };
        Config.ShowMediaLyrics.ValueChanged += _ =>
        {
            UpdateLyrics(_position);
            UpdateProgressUi();
        };
        Config.MediaCoverBorderRadius.ValueChanged += _ => ApplyStyle();
        Config.MediaCoverBorderColor.ValueChanged += _ => ApplyStyle();
        Config.MediaUseCustomBg.ValueChanged += _ =>
        {
            if (!Config.MediaUseCustomBg.Value)
            {
                ApplyCardStyle();
            }
            ApplyStyle();
        };
        Config.MediaBgOpacity.ValueChanged += _ => ApplyStyle();
        Config.MediaBorderRadius.ValueChanged += _ => ApplyStyle();
        Config.MediaUpdateInterval.ValueChanged += _ =>
            _pollTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, Config.MediaUpdateInterval.Value));
        Config.ThemeColor.ValueChanged += _ =>
        {
            _wave.InvalidateVisual();
            if (!_hasThumb)
            {
                SetDefaultCover();
            }
            ApplyStyle();
        };
        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyStyle();
            if (!_hasThumb)
            {
                SetDefaultCover();
            }
        };
        SizeChanged += (_, _) => ApplyStyle();
        ApplyStyle();
        UpdatePlayState();
        IsVisible = Config.ShowMediaInfo.Value;
        _pollTimer.Start();
        _progTimer.Start();
        _ = PollAsync();
    }

    // 默认封面: 强调色薄雾 + 唱片环 + 音符
    private void SetDefaultCover(bool muted = false)
    {
        var dark = CardIsDark;
        var p = WidgetSurfacePalette.Resolve(dark);
        var accent = CardAccent.Value;
        var tintA = muted ? (byte)16 : (byte)44;
        var tintB = muted ? (byte)3 : (byte)8;
        var ink = new SolidColorBrush(p.Text, muted ? 0.22 : 0.42);
        var ringInk = new SolidColorBrush(p.Text, muted ? 0.10 : 0.16);
        var cover = _coverHost.Width > 1 ? _coverHost.Width : 140;

        var grid = new Grid();
        var background = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(tintA, accent.R, accent.G, accent.B), 0),
                    new GradientStop(Color.FromArgb(tintB, accent.R, accent.G, accent.B), 1),
                },
            },
        };
        var ring = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = cover * 0.66,
            Height = cover * 0.66,
            Stroke = ringInk,
            StrokeThickness = 1.2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var note = new TextBlock
        {
            Text = "♪",
            FontSize = cover * 0.40,
            Foreground = ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        grid.Children.Add(background);
        grid.Children.Add(ring);
        grid.Children.Add(note);
        _coverHost.Background = null;
        _coverHost.Child = grid;
    }

    private async Task LoadCoverAsync(byte[]? data)
    {
        if (data is not { Length: > 0 })
        {
            return;
        }
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Bitmap bitmap;
                try
                {
                    using var ms = new MemoryStream(data);
                    bitmap = new Bitmap(ms);
                }
                catch (Exception)
                {
                    Log.Warning($"[MP] 封面数据解码失败 ({data.Length}B)");
                    return;
                }
                _hasThumb = true;
                _coverHost.Transitions = null;
                _coverHost.Opacity = 0;
                _coverHost.Child = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                _coverHost.Transitions = new Transitions
                {
                    new DoubleTransition
                    {
                        Property = Visual.OpacityProperty,
                        Duration = TimeSpan.FromMilliseconds(300),
                        Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
                    },
                };
                _coverHost.Opacity = 1;
            });
        }
        catch (Exception e)
        {
            Log.Debug($"[MP] 应用封面失败 控件已卸载: {e.Message}");
        }
    }

    private static string DisplayAppName(string? app)
    {
        var raw = (app ?? "").Trim();
        var key = raw.ToLowerInvariant();
        if (key.Contains("cloudmusic") || key.Contains("netease"))
        {
            return AppUtils.Tr("media.source_netease");
        }
        if (key.Contains("qqmusic") || key is "qq音乐" or "qq音乐播放器")
        {
            return AppUtils.Tr("media.source_qq");
        }
        if (key.Contains("kugou") || key.Contains("酷狗"))
        {
            return AppUtils.Tr("media.source_kugou");
        }
        if (key is "gsmtc" or "smtc")
        {
            return AppUtils.Tr("media.source_system");
        }
        return raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
    }

    private static string FormatArtist(string? artist)
    {
        var raw = (artist ?? "").Trim();
        if (raw.Length == 0)
        {
            return "";
        }
        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? string.Join(" / ", parts) : raw;
    }

    private void ApplyStyle()
    {
        var dark = CardIsDark;
        var p = WidgetSurfacePalette.Resolve(dark);
        // 字号与封面按卡高等比 基准 200 高
        var h = Bounds.Height > 1 ? Bounds.Height : 200;
        var w = Bounds.Width > 1 ? Bounds.Width : 400;
        var k = Math.Clamp(h / 200.0, 0.62, 1.5);
        var compact = h < 150 || w < 340;
        var cover = Math.Clamp(Math.Min(h - (compact ? 24 : 34), w * (compact ? 0.28 : 0.32)), 56, 128);
        _coverHost.Width = cover;
        _coverHost.Height = cover;
        _coverHost.CornerRadius = new CornerRadius(Math.Clamp(Config.MediaCoverBorderRadius.Value, 8, 18));
        var coverBorder = ParseRgba(Config.MediaCoverBorderColor.Value);
        _coverHost.BorderBrush = coverBorder is { } cb
            ? new SolidColorBrush(cb)
            : new SolidColorBrush(p.Text, dark ? 0.10 : 0.08);
        _coverHost.BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetY = 4,
            Blur = 16,
            Color = Color.FromArgb(dark ? (byte)85 : (byte)36, 0, 0, 0),
        });
        _coverHost.IsVisible = Config.ShowMediaCover.Value;

        var ink = p.Text;
        var muted = new SolidColorBrush(ink, CardInk.MutedOpacity);
        var faint = new SolidColorBrush(ink, CardInk.FaintOpacity);
        _appText.Foreground = muted;
        _appText.FontSize = Math.Clamp(Math.Round(10 * k, 1), 9, 12);
        _appText.FontWeight = FontWeight.Medium;
        _titleText.Foreground = new SolidColorBrush(ink);
        var titleSize = Math.Clamp(Math.Round(15.5 * k, 1), 13, 22);
        _titleText.FontSize = titleSize;
        _titleText.LineHeight = Math.Round(titleSize * 1.25, 1);
        _titleText.FontWeight = FontWeight.SemiBold;
        _titleText.MaxLines = compact ? 1 : 2;
        _titleText.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        _artistText.Foreground = muted;
        _artistText.FontSize = Math.Clamp(Math.Round(11 * k, 1), 9.5, 14);
        _lyricsText.Foreground = new SolidColorBrush(ink, 0.80);
        _lyricsText.FontSize = Math.Clamp(Math.Round(11.5 * k, 1), 10, 15);
        _lyricsText.FontWeight = FontWeight.Normal;
        _timeText.Foreground = new SolidColorBrush(ink, 0.45);
        _timeText.FontSize = Math.Clamp(Math.Round(10.5 * k, 1), 9.5, 12.5);
        _timeText.FontWeight = FontWeight.Medium;
        _wave.Height = 6;
        _wave.InvalidateVisual();

        var playSize = Math.Clamp(32 * k, 26, 44);
        var sideSize = Math.Clamp(28 * k, 22, 36);
        _btnPlay.Configure(playSize, playSize * 0.44, CardAccent.Value, OnAccentColor(CardAccent.Value));
        _btnPrev.Configure(sideSize, sideSize * 0.64, CardAccent.Value, ink);
        _btnNext.Configure(sideSize, sideSize * 0.64, CardAccent.Value, ink);

        // 自定义背景模式
        if (Config.MediaUseCustomBg.Value)
        {
            var c = dark ? Color.FromRgb(30, 30, 30) : Colors.White;
            Card.Background = new SolidColorBrush(c, Math.Clamp(Config.MediaBgOpacity.Value / 100.0, 0, 1));
            Card.CornerRadius = new CornerRadius(Config.MediaBorderRadius.Value);
            Card.BorderThickness = new Thickness(0);
        }
        else
        {
            Card.BorderThickness = new Thickness(1);
        }

        if (!_hasThumb)
        {
            SetDefaultCover();
        }
        UpdatePlayState();
    }

    private static Color OnAccentColor(Color accent)
    {
        var luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
        return luminance > 0.62 ? Color.FromRgb(22, 22, 22) : Colors.White;
    }

    private void UpdatePlayState()
    {
        // FluentIcon PLAY/PAUSE_BOLD 等价字形
        _btnPlay.Glyph = _playing ? "\uE769" : "\uE768";
        _btnPrev.Glyph = "\uE892";
        _btnNext.Glyph = "\uE893";
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _appDot.Background = _media is null
            ? new SolidColorBrush(p.Text, 0.25)
            : _playing ? CardAccent.Brush() : new SolidColorBrush(p.Text, 0.35);
        var has = _media is not null;
        _btnPrev.IsHitTestVisible = has;
        _btnPlay.IsHitTestVisible = has;
        _btnNext.IsHitTestVisible = has;
        _btnPrev.Opacity = has ? 1 : 0.35;
        _btnNext.Opacity = has ? 1 : 0.35;
        _btnPlay.Opacity = has ? 1 : 0.45;
    }

    // 媒体配色沿用 #RRGGBBAA 书写习惯, 与 Avalonia 的 #AARRGGBB 区分开
    private static Color? ParseRgba(string? hex)
    {
        var s = (hex ?? "").Trim();
        if (s.StartsWith('#'))
        {
            s = s[1..];
        }
        if (s.Length == 8)
        {
            try
            {
                var r = Convert.ToByte(s[..2], 16);
                var g = Convert.ToByte(s[2..4], 16);
                var b = Convert.ToByte(s[4..6], 16);
                var a = Convert.ToByte(s[6..8], 16);
                return Color.FromArgb(a, r, g, b);
            }
            catch
            {
                return null;
            }
        }
        return TryParseColor(hex);
    }

    private async Task PollAsync()
    {
        if (_polling)
        {
            return;
        }
        _polling = true;
        MediaInfo? m = null;
        try
        {
            // 网易云首次读取要扫 40MB dll, 放后台线程避免卡 UI
            m = await Task.Run(async () =>
            {
                var info = await MediaServices.GetMediaInfoAsync();
                return info is not null && info.IsValid() ? info : null;
            });
        }
        catch (Exception e)
        {
            Log.Error($"媒体信息获取异常: {e.Message}");
        }
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => OnMedia(m));
        }
        finally
        {
            _polling = false;
        }
    }

    private void OnMedia(MediaInfo? m)
    {
        if (!Config.ShowMediaInfo.Value)
        {
            return;
        }
        if (m is null)
        {
            NoMedia();
            return;
        }
        _media = m;
        var newSong = m.TitleArtist != _lastTa;
        if (newSong)
        {
            DisplayNewSong(m);
        }
        else
        {
            SyncPosition(m.PositionMs, m.IsPlaying);
            // 刚点过播放/暂停时先保持本地乐观状态, 等系统会话追上再换图标
            if (Environment.TickCount64 >= _stateHoldUntil)
            {
                _playing = m.IsPlaying;
            }
        }
        if (m.DurationMs > 0)
        {
            _duration = m.DurationMs;
        }
        UpdatePlayState();
        UpdateProgressUi();
        if (_lyrics is not null)
        {
            UpdateLyrics(_position);
        }
        _coverHost.IsVisible = Config.ShowMediaCover.Value;
    }

    // 系统时间轴偶尔是旧快照: 小回跳直接忽略, 大跳变才认作拖动/切段
    private void SyncPosition(long reportedMs, bool playing)
    {
        var reported = Math.Max(0, reportedMs);
        if (!playing)
        {
            if (Math.Abs(reported - _position) > 2500)
            {
                _position = reported;
            }
            return;
        }
        if (reported > _position + 700 || reported < _position - 3000)
        {
            _position = reported;
        }
    }

    // 新歌
    private void DisplayNewSong(MediaInfo m)
    {
        _lastTa = m.TitleArtist;
        _detailVersion++;
        _stateHoldUntil = 0;
        _position = Math.Max(0, m.PositionMs);
        _playing = m.IsPlaying;
        _duration = Math.Max(0, m.DurationMs);
        _hasThumb = false;

        // 淡入复位 + 默认封面
        _coverHost.Transitions = null;
        _coverHost.Opacity = 1;
        SetDefaultCover();
        _lyrics = null;
        _lyricsText.Text = "";
        _lyricsText.Opacity = 1;
        _lyricsText.IsVisible = false;
        _centerBlock.IsVisible = true;
        _wave.Progress = 0;

        var appName = DisplayAppName(m.AppName);
        _appText.Text = appName;
        _appPanel.IsVisible = appName.Length > 0;

        var title = m.Title.Length > 0 ? m.Title : AppUtils.Tr("media.unknown_song");
        _titleText.Text = title;
        _artistText.Text = FormatArtist(m.Artist);
        _artistText.IsVisible = _artistText.Text.Length > 0;
        Log.Info($"[MP] 新曲 {appName} · {title} · {_artistText.Text} · song_id={m.SongId} · pos={_position}ms");

        UpdatePlayState();
        if (m.ThumbnailData is { Length: > 0 })
        {
            _ = LoadCoverAsync(m.ThumbnailData);
        }
        if (_infoCache.TryGetValue(m.TitleArtist, out var cached))
        {
            ApplyDetail(cached);
        }
        else
        {
            _ = FetchDetailAsync(m, _detailVersion);
        }
    }

    private void NoMedia()
    {
        if (_media is not null)
        {
            Log.Info("[MP] 无媒体播放 重置为空状态");
        }
        _appText.Text = "";
        _appPanel.IsVisible = false;
        _titleText.Text = AppUtils.Tr("media.not_playing");
        _artistText.Text = AppUtils.Tr("media.play_hint");
        _artistText.IsVisible = _artistText.Text.Length > 0;
        _lyricsText.Text = "";
        _lyricsText.IsVisible = false;
        _wave.Progress = 0;
        _wave.IsVisible = false;
        _timeText.Text = "";
        _timeText.IsVisible = false;
        _centerBlock.IsVisible = false;
        _media = null;
        _lyrics = null;
        _lastTa = "";
        _hasThumb = false;
        _stateHoldUntil = 0;
        _playing = false;
        _duration = 0;
        _position = 0;
        _detailVersion++;
        SetDefaultCover(muted: true);
        _coverHost.IsVisible = Config.ShowMediaCover.Value;
        UpdatePlayState();
    }

    // 进度秒推
    private void UpdateProgress()
    {
        if (_playing && _duration > 0)
        {
            _position = Math.Min(_position + 1000, _duration);
        }
        UpdateProgressUi();
        if (_lyrics is not null)
        {
            UpdateLyrics(_position);
        }
    }

    private void UpdateProgressUi()
    {
        var hasDuration = _duration > 0;
        var hasMedia = _media is not null;
        _wave.Progress = hasDuration ? Math.Min(1, _position / (double)_duration) : 0;
        _wave.IsVisible = hasMedia;
        _timeText.Text = hasMedia
            ? hasDuration ? $"{Fmt(_position)} / {Fmt(_duration)}" : Fmt(_position)
            : "";
        _timeText.IsVisible = hasMedia && _timeText.Text.Length > 0;
        _centerBlock.IsVisible = hasMedia;
    }

    private static string Fmt(long ms)
    {
        var total = Math.Max(0, ms / 1000);
        if (total >= 3600)
        {
            return $"{total / 3600}:{total % 3600 / 60:D2}:{total % 60:D2}";
        }
        return $"{total / 60}:{total % 60:D2}";
    }

    private void UpdateLyrics(long ms)
    {
        var text = "";
        if (Config.ShowMediaLyrics.Value && _lyrics is not null && !_lyrics.IsEmpty())
        {
            var advance = Config.MediaLyricsAdvance.Value;
            var (_, idx) = _lyrics.GetLineAtTime(ms + advance);
            if (idx >= 0 && idx < _lyrics.Lines.Count)
            {
                text = _lyrics.Lines[idx].Text;
            }
        }
        if (text != _lyricsText.Text)
        {
            _lyricsText.Opacity = 0;
            _lyricsText.Text = text;
            _lyricsText.IsVisible = text.Length > 0;
            Dispatcher.UIThread.Post(() => _lyricsText.Opacity = 1);
        }
        else
        {
            _lyricsText.IsVisible = text.Length > 0;
        }
        _centerBlock.IsVisible = _media is not null;
    }

    // 歌曲详情后台抓取
    private async Task FetchDetailAsync(MediaInfo m, int version)
    {
        MediaDetail result = new(null, null, 0);
        try
        {
            var svc = MediaServices.GetService(m.AppName);
            if (svc is not null)
            {
                var lyrics = Config.ShowMediaLyrics.Value ? await svc.LyricsAsync(m) : null;
                var cover = await svc.CoverAsync(m);
                var dur = await svc.DurationAsync(m);
                result = new MediaDetail(lyrics, cover is { Length: > 0 } ? cover : null, dur);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[MP] 获取歌曲信息失败: {e.Message}");
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (version != _detailVersion)
            {
                return;
            }
            _infoCache[m.TitleArtist] = result;
            if (_infoCache.Count > 60)
            {
                _infoCache.Remove(_infoCache.Keys.First());
            }
            Log.Info($"[MP] 详情完成 cover={(result.Cover is not null)} lyrics={(result.Lyrics is not null)} dur={result.Duration}ms");
            if (_media is not null && _media.TitleArtist == m.TitleArtist)
            {
                ApplyDetail(result);
            }
        });
    }

    private void ApplyDetail(MediaDetail result)
    {
        if (result.Duration > 0)
        {
            _duration = result.Duration;
        }
        if (Config.ShowMediaCover.Value && !_hasThumb && result.Cover is not null)
        {
            _ = LoadCoverAsync(result.Cover);
        }
        else if (result.Cover is not null && !_hasThumb)
        {
            _ = LoadCoverAsync(result.Cover);
        }
        if (result.Lyrics is not null)
        {
            _lyrics = result.Lyrics;
            UpdateLyrics(_position);
        }
        UpdateProgressUi();
    }

    // 播放/暂停
    private async Task TogglePlayAsync()
    {
        if (_controlBusy)
        {
            return;
        }
        _controlBusy = true;
        var targetPlaying = !_playing;
        _playing = targetPlaying;
        _stateHoldUntil = Environment.TickCount64 + 1600;
        UpdatePlayState();
        try
        {
            var ok = await MediaServices.MediaControlAsync(targetPlaying ? "play" : "pause", _media?.AppName);
            if (!ok)
            {
                Log.Warning($"[MP] 控制命令 '{(targetPlaying ? "play" : "pause")}' 未确认");
                // 命令没被系统接受时立刻回退图标, 不要让乐观状态停住
                _playing = !targetPlaying;
                _stateHoldUntil = 0;
                UpdatePlayState();
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[MP] 控制命令执行失败: {e.Message}");
        }
        finally
        {
            _controlBusy = false;
        }
        await Task.Delay(600);
        await PollAsync();
    }

    // 切上下曲
    private async Task ControlAsync(string action)
    {
        if (_controlBusy)
        {
            return;
        }
        _controlBusy = true;
        _coverHost.Transitions = null;
        _coverHost.Opacity = 1;
        try
        {
            var ok = action switch
            {
                "next" => await MediaServices.MediaNextAsync(_media?.AppName),
                "prev" => await MediaServices.MediaPrevAsync(_media?.AppName),
                _ => await MediaServices.MediaControlAsync(action, _media?.AppName),
            };
            if (!ok)
            {
                Log.Warning($"[MP] 控制命令 '{action}' 未确认");
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[MP] 控制命令 '{action}' 执行失败: {e.Message}");
        }
        finally
        {
            _controlBusy = false;
        }
        await Task.Delay(600);
        await PollAsync();
    }
}

// 历史上的今天
// 规格: 360x240 边距(16,16,16,16) 间距6 头部(图标28+标题15px w600+日期11px 右) 5条富文本
// 年份12px #30c361 w700 + 标题14px 点击开链接 30min 刷新+过期缓存

public sealed class HistoryTodayWidget : WidgetCardBase
{
    private const int ItemCount = 5;

    private readonly TextBlock _titleText = new();
    private readonly TextBlock _dateText = new();
    private readonly SelectableTextBlock[] _itemLabels = new SelectableTextBlock[ItemCount];
    private readonly string[] _titles = Enumerable.Repeat("", ItemCount).ToArray();
    private readonly string[] _years = Enumerable.Repeat("", ItemCount).ToArray();
    private readonly string[] _links = Enumerable.Repeat("", ItemCount).ToArray();

    public HistoryTodayWidget(ComponentDefinition definition) : base(definition)
    {
        // 原版 addWidget(widget, 1) 每条 stretch 均分 顶部 header 后 5 条等高
        var grid = new Grid
        {
            Margin = new Thickness(16, 16, 16, 16),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,*,*,*,*"),
            RowSpacing = 6,
        };

        var header = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*,Auto"), ColumnSpacing = 8 };
        // 原版 _render_header_icon 用 FUI.HISTORY SVG 非字形
        var icon = MakeHistoryIcon();
        _titleText.Text = AppUtils.Tr("history_today.title");
        _titleText.FontSize = 15;
        _titleText.FontWeight = FontWeight.SemiBold;
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _dateText.TextAlignment = TextAlignment.Right;
        _dateText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(_titleText, 1);
        Grid.SetColumn(_dateText, 2);
        header.Children.Add(icon);
        header.Children.Add(_titleText);
        header.Children.Add(_dateText);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        for (var i = 0; i < ItemCount; i++)
        {
            var label = new SelectableTextBlock
            {
                Text = "--",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            var idx = i;
            label.PointerReleased += (_, _) => OpenLink(idx);
            _itemLabels[i] = label;
            Grid.SetRow(label, i + 1);
            grid.Children.Add(label);
        }
        Card.Child = grid;
        Content = Card;

        ActualThemeVariantChanged += (_, _) => RenderItems();
        // 原版 _setup_periodic_refresh 30 分钟
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        timer.Tick += (_, _) => _ = RefreshAsync();
        timer.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        JsonElement? data = null;
        try
        {
            data = await HistoryService.FetchHistoryTodayAsync();
        }
        catch (Exception e)
        {

            Log.Warning($"[SW] 在线获取失败 走缓存: {e.Message}");
            // 接口失败走缓存
        }
        // 原版: 接口无数据用过期缓存
        if (data is null)
        {
            data = AppUtils.LoadCache("history_today", ignoreExpiry: true);
        }
        await Dispatcher.UIThread.InvokeAsync(() => UpdateDisplay(data));
    }

    private void UpdateDisplay(JsonElement? data)
    {
        for (var i = 0; i < ItemCount; i++)
        {
            _titles[i] = "--";
            _years[i] = "";
            _links[i] = "";
        }
        var dateText = "";
        if (data is { ValueKind: JsonValueKind.Object } root)
        {
            dateText = root.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
            if (root.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
            {
                for (var i = 0; i < ItemCount && i < events.GetArrayLength(); i++)
                {
                    var item = events[i];
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    _titles[i] = item.TryGetProperty("title", out var t) ? t.GetString() ?? "--" : "--";
                    _years[i] = item.TryGetProperty("year", out var y) ? y.ToString() : "";
                    _links[i] = item.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
                }
            }
        }
        _dateText.Text = dateText;
        RenderItems();
    }

    // 富文本
    private void RenderItems()
    {
        var dark = CardIsDark;
        var titleColor = dark ? "#ffffff" : "#1a1a1a";
        _titleText.Foreground = new SolidColorBrush(Color.Parse(titleColor));
        var p = WidgetSurfacePalette.Resolve(dark);
        _dateText.Foreground = CardInk.Muted(p);
        _dateText.FontSize = 11;
        for (var i = 0; i < ItemCount; i++)
        {
            var label = _itemLabels[i];
            label.Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
            label.Inlines.Clear();
            if (_years[i].Length > 0)
            {
                // 年份是锚点 大号 Light 强调色 数据驱动
                label.Inlines.Add(new Avalonia.Controls.Documents.Run($"{_years[i]} · ")
                {
                    FontSize = 16,
                    FontWeight = FontWeight.Light,
                    Foreground = CardAccent.Brush(),
                });
            }
            label.Inlines.Add(new Avalonia.Controls.Documents.Run(_titles[i])
            {
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.Parse(titleColor)),
            });
            label.LineHeight = 19;
            label.MaxLines = 2;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
        }
    }

    // 头部图标
    private Control MakeHistoryIcon()
    {
        return new FAFontIcon
        {
            FontFamily = FAIcons.SymbolsFont,
            Glyph = "\uE81C",
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void OpenLink(int index)
    {
        if (index < 0 || index >= ItemCount || _links[index].Length == 0)
        {
            return;
        }
        Log.Info($"[History] 打开历史事件: {_years[index]} {_titles[index]}");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _links[index],
                UseShellExecute = true,
            });
        }
        catch (Exception e)
        {
            Log.Warning($"[History] 打开失败: {e.Message}");
        }
    }
}

// 月历
// 规格: 300x300 边距(12,10,12,8) 间距4 标题20px w600 + 上下按钮28x18 星期13px 6x7 格
// 格内: 日期占上62% 字号 sz*0.46 角标 sz*0.35 bold 今日 #00b7c3 圆 r=sz*0.40

public sealed class CalendarMonthWidget : WidgetCardBase
{
    private static readonly string[] MonthNamesCn =
        { "一月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "十一月", "十二月" };

    // 节日全称 > 简写 逐字对齐原版 HOLIDAY_SHORT_MAP
    private static readonly Dictionary<string, string> HolidayShortMap = new()
    {
        ["元旦节"] = "元旦", ["春节"] = "春节", ["清明节"] = "清明", ["国际劳动节"] = "劳动",
        ["端午节"] = "端午", ["中秋节"] = "中秋", ["国庆节"] = "国庆", ["元宵节"] = "元宵",
        ["小年"] = "小年", ["七夕-魁星诞"] = "七夕", ["中元节"] = "中元", ["重阳节-酆都大帝诞"] = "重阳",
        ["腊八节-释迦如来成佛之辰"] = "腊八", ["春龙节-福德土地正神诞"] = "龙抬头", ["情人节"] = "情人",
        ["国际劳动妇女节"] = "妇女", ["中国植树节"] = "植树", ["孙中山逝世纪念日,中国植树节"] = "植树",
        ["国际愚人节"] = "愚人", ["中国青年节"] = "青年", ["母亲节"] = "母亲", ["国际儿童节"] = "儿童",
        ["父亲节"] = "父亲", ["中国共产党诞生日,香港回归纪念日"] = "建党", ["中国人民解放军建军节"] = "建军",
        ["中国教师节"] = "教师", ["平安夜"] = "平安夜", ["圣诞节"] = "圣诞", ["国际和平日"] = "和平",
        ["中国人民抗日战争纪念日"] = "抗日", ["中国抗日战争胜利纪念日"] = "抗日", ["抗美援朝纪念日"] = "抗美",
        ["南京大屠杀纪念日"] = "公祭", ["上海解放日"] = "解放",
        ["小寒"] = "小寒", ["大寒"] = "大寒", ["立春"] = "立春", ["雨水"] = "雨水", ["惊蛰"] = "惊蛰",
        ["春分"] = "春分", ["清明"] = "清明", ["谷雨"] = "谷雨", ["立夏"] = "立夏", ["小满"] = "小满",
        ["芒种"] = "芒种", ["夏至"] = "夏至", ["小暑"] = "小暑", ["大暑"] = "大暑", ["立秋"] = "立秋",
        ["处暑"] = "处暑", ["白露"] = "白露", ["秋分"] = "秋分", ["寒露"] = "寒露", ["霜降"] = "霜降",
        ["立冬"] = "立冬", ["小雪"] = "小雪", ["大雪"] = "大雪", ["冬至"] = "冬至",
    };

    private static readonly Dictionary<(int Y, int M, int D), string> DayInfoCache = new();

    private readonly TextBlock _monthText = new();
    private readonly TextBlock _yearText = new();
    private readonly TextBlock[] _weekdayTexts = new TextBlock[7];
    private readonly List<TextBlock> _navGlyphs = new();
    private readonly Grid _daysGrid = new();
    private readonly List<RowDefinition> _weekRows = new();
    private readonly CalendarDayCell[,] _cells = new CalendarDayCell[6, 7];
    private readonly DispatcherTimer _timer;
    private int _displayYear;
    private int _displayMonth;
    private DateTime? _lastSeenDate;

    public CalendarMonthWidget(ComponentDefinition definition) : base(definition)
    {
        var today = DateTime.Today;
        _displayYear = today.Year;
        _displayMonth = today.Month;

        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.PadCompact, 10, WidgetSpace.PadCompact, 10),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,*"),
            RowSpacing = 3,
        };

        // 标题行: 月份 + 年份 + 翻页
        var titleRow = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto") };
        var caption = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        caption.Children.Add(_monthText);
        caption.Children.Add(_yearText);
        titleRow.Children.Add(caption);

        var nav = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nav.Children.Add(MakeNavButton("\uE76B", -1));
        nav.Children.Add(MakeNavButton("\uE76C", 1));
        Grid.SetColumn(nav, 1);
        titleRow.Children.Add(nav);
        Grid.SetRow(titleRow, 0);
        root.Children.Add(titleRow);

        // 星期标题
        var wkNames = new[] { "日", "一", "二", "三", "四", "五", "六" };
        var wkRow = new Avalonia.Controls.Primitives.UniformGrid
        {
            Rows = 1,
            Columns = 7,
            Height = 14,
        };
        for (var i = 0; i < 7; i++)
        {
            var wk = new TextBlock
            {
                Text = wkNames[i],
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = WidgetType.Micro,
                FontWeight = FontWeight.Medium,
            };
            _weekdayTexts[i] = wk;
            wkRow.Children.Add(wk);
        }
        Grid.SetRow(wkRow, 1);
        root.Children.Add(wkRow);

        // 动态周数日期格
        for (var c = 0; c < 7; c++)
        {
            _daysGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        }
        for (var r = 0; r < 6; r++)
        {
            var row = new RowDefinition(new GridLength(1, GridUnitType.Star));
            _weekRows.Add(row);
            _daysGrid.RowDefinitions.Add(row);
            for (var c = 0; c < 7; c++)
            {
                var cell = new CalendarDayCell { DarkProvider = () => CardIsDark };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                _daysGrid.Children.Add(cell);
                _cells[r, c] = cell;
            }
        }
        Grid.SetRow(_daysGrid, 2);
        root.Children.Add(_daysGrid);
        Card.Child = root;
        Content = Card;

        ApplyStyle();
        ActualThemeVariantChanged += (_, _) => { ApplyStyle(); RefreshCellsVisual(); };
        // 原版 60s 跨日检查
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => CheckDayChange();
        _timer.Start();
        RefreshCalendar();
    }

    private Border MakeNavButton(string glyph, int delta)
    {
        var g = new TextBlock
        {
            Text = glyph,
            FontFamily = FAIcons.SymbolsFont,
            FontSize = 11.5,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _navGlyphs.Add(g);
        var btn = new Border
        {
            Width = 22,
            Height = 20,
            CornerRadius = new CornerRadius(5),
            Child = g,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        btn.PointerEntered += (_, _) =>
            btn.Background = new SolidColorBrush(CardTheme.T("hover", CardIsDark));
        btn.PointerExited += (_, _) => btn.Background = null;
        btn.PointerReleased += (_, _) => GoMonth(delta);
        return btn;
    }

    private void CheckDayChange()
    {
        var today = DateTime.Today;
        if (_lastSeenDate is { } last && today != last)
        {
            Log.Info($"[CAL] 跨日: {last:yyyy-MM-dd} -> {today:yyyy-MM-dd}");
        }
        _lastSeenDate = today;
        if (today.Year == _displayYear && today.Month == _displayMonth)
        {
            RefreshCalendar();
        }
    }

    private void GoMonth(int delta)
    {
        var total = _displayYear * 12 + (_displayMonth - 1) + delta;
        _displayYear = Math.DivRem(total, 12, out var m);
        _displayMonth = m + 1;
        Log.Debug($"[CalendarComponent] 切换月份: {_displayYear}-{_displayMonth:D2}");
        RefreshCalendar();
    }

    private static string GetDaySubText(int year, int month, int day)
    {
        var key = (year, month, day);
        if (DayInfoCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var sub = "";
        // 节日 > 节气 > 农历 保证每格都有副文本
        var info = AlmanacService.GetToday(new DateTime(year, month, day));
        if (info is not null)
        {
            sub = ShortHoliday(info.Holiday);
            if (sub.Length == 0)
            {
                sub = ShortHoliday(info.SolarTerm);
            }
            if (sub.Length == 0)
            {
                sub = info.LunarDay == "初一" ? info.LunarMonth : info.LunarDay;
            }
        }
        DayInfoCache[key] = sub;
        return sub;
    }

    private static string ShortHoliday(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }
        if (name.Contains(','))
        {
            foreach (var p in name.Split(','))
            {
                if (HolidayShortMap.TryGetValue(p.Trim(), out var hit))
                {
                    return hit;
                }
            }
            return "";
        }
        return HolidayShortMap.TryGetValue(name, out var v) ? v : "";
    }

    private void RefreshCalendar()
    {
        var year = _displayYear;
        var month = _displayMonth;
        var today = DateTime.Today;

        _monthText.Text = MonthNamesCn[month - 1];
        _yearText.Text = year.ToString();

        // 周日=0
        var firstWd = ((int)new DateTime(year, month, 1).DayOfWeek) % 7;
        var dim = DateTime.DaysInMonth(year, month);
        var weeks = (firstWd + dim + 6) / 7;
        var prevMonth = month > 1 ? month - 1 : 12;
        var prevYear = month > 1 ? year : year - 1;
        var prevDim = DateTime.DaysInMonth(prevYear, prevMonth);

        // 只保留实际周数 避免底部空行
        for (var r = 0; r < 6; r++)
        {
            _weekRows[r].Height = r < weeks
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }

        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                _cells[r, c].Clear();
            }
        }

        var lastIndex = weeks * 7;
        for (var i = 0; i < lastIndex; i++)
        {
            var r = i / 7;
            var c = i % 7;
            var weekend = c == 0 || c == 6;
            if (i < firstWd)
            {
                // 上月末尾
                _cells[r, c].SetData(prevDim - firstWd + 1 + i, "", false, false, weekend);
            }
            else if (i < firstWd + dim)
            {
                var day = i - firstWd + 1;
                var isToday = year == today.Year && month == today.Month && day == today.Day;
                _cells[r, c].SetData(day, GetDaySubText(year, month, day), true, isToday, weekend);
            }
            else
            {
                // 下月开头 补满最后一行
                var day = i - firstWd - dim + 1;
                _cells[r, c].SetData(day, "", false, false, weekend);
            }
        }
        Log.Debug($"[CAL] 月历已刷新 {year}-{month:D2} 周数 {weeks} 今日 {today:MM}-{today:dd}");
    }

    private void RefreshCellsVisual()
    {
        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                _cells[r, c].InvalidateVisual();
            }
        }
    }

    private void ApplyStyle()
    {
        var dark = CardIsDark;
        _monthText.Foreground = new SolidColorBrush(Color.Parse(dark ? "#f0f0f0" : "#1a1a1a"));
        _monthText.FontSize = 14;
        _monthText.FontWeight = FontWeight.SemiBold;
        _yearText.Foreground = new SolidColorBrush(
            dark ? Color.FromArgb(145, 255, 255, 255) : Color.FromArgb(125, 0, 0, 0));
        _yearText.FontSize = WidgetType.Micro;
        _yearText.Margin = new Thickness(0, 1, 0, 0);
        foreach (var g in _navGlyphs)
        {
            g.Foreground = new SolidColorBrush(
                dark ? Color.FromArgb(145, 255, 255, 255) : Color.FromArgb(125, 0, 0, 0));
        }
        for (var i = 0; i < _weekdayTexts.Length; i++)
        {
            var weekend = i == 0 || i == 6;
            _weekdayTexts[i].Foreground = new SolidColorBrush(
                weekend
                    ? (dark ? Color.FromArgb(115, 255, 255, 255) : Color.FromArgb(89, 0, 0, 0))
                    : (dark ? Color.FromArgb(191, 255, 255, 255) : Color.FromArgb(140, 0, 0, 0)));
        }
    }
}

/// <summary>月历单格</summary>
public sealed class CalendarDayCell : Control
{
    // 归属卡片明暗 由宿主注入
    public Func<bool>? DarkProvider { get; set; }
    private int _day;
    private string _sub = "";
    private bool _isCurrentMonth = true;
    private bool _isToday;
    private bool _isWeekend;

    public void SetData(int day, string subText, bool isCurrentMonth, bool isToday, bool isWeekend)
    {
        _day = day;
        _sub = subText ?? "";
        _isCurrentMonth = isCurrentMonth;
        _isToday = isToday;
        _isWeekend = isWeekend;
        InvalidateVisual();
    }

    public void Clear()
    {
        _day = 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_day == 0)
        {
            return;
        }
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0 || !double.IsFinite(w) || !double.IsFinite(h))
        {
            return;
        }
        var dark = DarkProvider?.Invoke() ?? ThemeSense.IsDark(this);
        var hasSub = _isCurrentMonth && _sub.Length > 0;
        // 农历副文本在 168 级小卡上会掉到 6px 以下 变成灰糊 不够 7.5px 干脆不画
        var subSize = Math.Min(w * 0.30, h * 0.26);
        subSize = Math.Min(subSize, w * 0.94 / Math.Max(2, _sub.Length));
        if (subSize < 7.5)
        {
            hasSub = false;
        }
        var dayRect = hasSub
            ? new Rect(0, h * 0.02, w, h * 0.54)
            : new Rect(0, 0, w, h);
        var daySize = hasSub
            ? Math.Min(w * 0.46, dayRect.Height * 0.86)
            : Math.Min(w * 0.44, h * 0.58);

        if (_isToday)
        {
            // 今日圆只包住日期 农历行留在圆外
            var radius = Math.Min(w * 0.42, h * 0.25);
            var cy = dayRect.Y + dayRect.Height / 2;
            context.DrawEllipse(new SolidColorBrush(CardAccent.Value), null,
                new Point(w / 2, cy), radius, radius);
        }

        // 日期文字色
        IBrush dayBrush;
        if (_isToday)
        {
            var accent = CardAccent.Value;
            var luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
            dayBrush = new SolidColorBrush(luminance > 0.58 ? Color.FromRgb(20, 20, 20) : Colors.White);
        }
        else if (!_isCurrentMonth)
        {
            dayBrush = new SolidColorBrush(dark ? Color.Parse("#555555") : Color.FromArgb(60, 0, 0, 0));
        }
        else if (_isWeekend)
        {
            dayBrush = new SolidColorBrush(dark ? Color.Parse("#9a9a9a") : Color.FromArgb(140, 0, 0, 0));
        }
        else
        {
            dayBrush = new SolidColorBrush(dark ? Color.Parse("#e8e8e8") : Color.Parse("#1a1a1a"));
        }
        DrawCentered(context, _day.ToString(), daySize, FontWeight.Normal, dayBrush, dayRect);

        // 副文本(节日/节气/农历)
        if (hasSub)
        {
            var subBrush = _isToday
                ? (IBrush)new SolidColorBrush(Color.FromArgb(235, 255, 255, 255))
                : new SolidColorBrush(dark ? Color.Parse("#a8a8a8") : Color.FromArgb(150, 0, 0, 0));
            DrawCentered(context, _sub, subSize, FontWeight.Medium, subBrush,
                new Rect(0, h * 0.56, w, h * 0.40));
        }
    }

    private static void DrawCentered(DrawingContext context, string text, double size, FontWeight weight,
        IBrush brush, Rect rect)
    {
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(Common.AppFontFamily, FontStyle.Normal, weight),
            size,
            brush);
        var x = rect.X + (rect.Width - ft.WidthIncludingTrailingWhitespace) / 2;
        var y = rect.Y + (rect.Height - ft.Height) / 2;
        context.DrawText(ft, new Point(x, y));
    }
}

internal enum CalcKeyKind
{
    Digit,
    Utility,
    Operator,
    Equals,
}

/// <summary>计算器按键 自绘表面 去掉 Fluent 按钮默认描边与厚底</summary>
internal sealed class CalcKey : Border
{
    private readonly TextBlock _label;
    private readonly CalcKeyKind _kind;
    private IBrush _idle = Brushes.Transparent;
    private IBrush _hover = Brushes.Transparent;
    private bool _pressed;

    public event Action? Invoked;

    public CalcKey(string text, CalcKeyKind kind)
    {
        _kind = kind;
        CornerRadius = new CornerRadius(12);
        Cursor = new Cursor(StandardCursorType.Hand);
        RenderTransformOrigin = RelativePoint.Center;
        _label = new TextBlock
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            FontFamily = WidgetFonts.Num,
        };
        Child = _label;

        PointerEntered += (_, _) =>
        {
            if (!_pressed)
            {
                Background = _hover;
            }
        };
        PointerExited += (_, _) =>
        {
            if (!_pressed)
            {
                Background = _idle;
            }
        };
        PointerPressed += (_, e) =>
        {
            _pressed = true;
            Background = _hover;
            RenderTransform = new ScaleTransform(0.96, 0.96);
            e.Pointer.Capture(this);
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (!_pressed)
            {
                return;
            }
            _pressed = false;
            e.Pointer.Capture(null);
            Background = _hover;
            RenderTransform = new ScaleTransform(1, 1);
            Invoked?.Invoke();
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) =>
        {
            _pressed = false;
            RenderTransform = new ScaleTransform(1, 1);
            Background = _idle;
        };
    }

    public void SetFontSize(double size) => _label.FontSize = size;

    public void Apply(WidgetSurfacePalette p, Color accent, bool dark)
    {
        var ink = p.Text;
        switch (_kind)
        {
            case CalcKeyKind.Digit:
                _idle = new SolidColorBrush(ink, dark ? 0.06 : 0.04);
                _hover = new SolidColorBrush(ink, dark ? 0.12 : 0.09);
                _label.Foreground = new SolidColorBrush(ink);
                _label.FontWeight = FontWeight.Normal;
                break;
            case CalcKeyKind.Utility:
                _idle = new SolidColorBrush(ink, dark ? 0.03 : 0.02);
                _hover = new SolidColorBrush(ink, dark ? 0.08 : 0.06);
                _label.Foreground = new SolidColorBrush(ink, CardInk.MutedOpacity);
                _label.FontWeight = FontWeight.Normal;
                break;
            case CalcKeyKind.Operator:
                _idle = new SolidColorBrush(accent, 0.12);
                _hover = new SolidColorBrush(accent, 0.20);
                _label.Foreground = new SolidColorBrush(ink);
                _label.FontWeight = FontWeight.Medium;
                break;
            case CalcKeyKind.Equals:
                _idle = new SolidColorBrush(accent);
                _hover = new SolidColorBrush(accent);
                _label.Foreground = CardAccent.Ink();
                _label.FontWeight = FontWeight.SemiBold;
                break;
        }
        if (!_pressed)
        {
            Background = _idle;
        }
    }
}

/// <summary>
/// 计算器
/// 语义对齐: 表达式串 数字<=15位 % -> (n/100) ± ⌫ 千分位/科学计数显示
/// </summary>
public sealed class CalculatorWidget : WidgetCardBase
{
    private readonly TextBlock _history = new()
    {
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        MaxHeight = 20,
        TextTrimming = TextTrimming.CharacterEllipsis,
        FontSize = WidgetType.Label,
        FontFamily = WidgetFonts.Num,
    };
    private readonly TextBlock _display = new()
    {
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Text = "0",
        FontWeight = FontWeight.Light,
        FontFamily = WidgetFonts.Num,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Border _screen = new()
    {
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(12, 10, 12, 10),
    };
    private readonly List<CalcKey> _keys = new();
    private string _expression = "";
    private bool _resultShown;

    private static readonly (string Text, int Row, int Col, CalcKeyKind Kind)[] ButtonSpecs =
    {
        ("⌫", 0, 0, CalcKeyKind.Utility), ("C", 0, 1, CalcKeyKind.Utility), ("%", 0, 2, CalcKeyKind.Utility), ("÷", 0, 3, CalcKeyKind.Operator),
        ("7", 1, 0, CalcKeyKind.Digit), ("8", 1, 1, CalcKeyKind.Digit), ("9", 1, 2, CalcKeyKind.Digit), ("×", 1, 3, CalcKeyKind.Operator),
        ("4", 2, 0, CalcKeyKind.Digit), ("5", 2, 1, CalcKeyKind.Digit), ("6", 2, 2, CalcKeyKind.Digit), ("−", 2, 3, CalcKeyKind.Operator),
        ("1", 3, 0, CalcKeyKind.Digit), ("2", 3, 1, CalcKeyKind.Digit), ("3", 3, 2, CalcKeyKind.Digit), ("+", 3, 3, CalcKeyKind.Operator),
        ("±", 4, 0, CalcKeyKind.Utility), ("0", 4, 1, CalcKeyKind.Digit), (".", 4, 2, CalcKeyKind.Digit), ("=", 4, 3, CalcKeyKind.Equals),
    };

    public CalculatorWidget(ComponentDefinition definition) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad, 14, WidgetSpace.Pad, WidgetSpace.Pad),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            RowSpacing = WidgetSpace.Gap,
        };

        _screen.Child = new StackPanel
        {
            Spacing = 2,
            Children = { _history, _display },
        };
        Grid.SetRow(_screen, 0);
        root.Children.Add(_screen);

        var buttons = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,*,*,*,*"),
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,*,*,*"),
            RowSpacing = WidgetSpace.GapSm,
            ColumnSpacing = WidgetSpace.GapSm,
            Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
        };
        Grid.SetRow(buttons, 1);
        foreach (var (text, row, col, kind) in ButtonSpecs)
        {
            var key = new CalcKey(text, kind);
            key.Invoked += () => OnButtonClick(text);
            Grid.SetRow(key, row);
            Grid.SetColumn(key, col);
            buttons.Children.Add(key);
            _keys.Add(key);
        }
        root.Children.Add(buttons);
        Card.Child = root;
        Content = Card;

        SizeChanged += (_, _) => Relayout();
        ApplyCardStyle();
    }

    private void Relayout()
    {
        var innerW = Bounds.Width - WidgetSpace.Pad * 2;
        if (innerW <= 1)
        {
            return;
        }
        var displayW = Math.Max(40, innerW - 24);
        var prefer = Math.Clamp(innerW * 0.20, 26, 48);
        _display.FontSize = CardText.Fit(_display.Text, WidgetFonts.Num, FontWeight.Light,
            displayW, prefer, 18, 48);
        _display.LetterSpacing = -Math.Round(_display.FontSize * 0.02, 1);
        var keySize = Math.Clamp(Math.Round(innerW / 14.5), 14, 22);
        foreach (var key in _keys)
        {
            key.SetFontSize(keySize);
        }
    }

    private void OnButtonClick(string key)
    {
        var calcKey = key switch { "÷" => "/", "×" => "*", "−" => "-", _ => key };
        var m = Regex.Match(_expression, @"(-?\d+\.?\d*)$");

        switch (key)
        {
            case "C":
                _expression = "";
                _resultShown = false;
                break;
            case "⌫":
                if (_resultShown)
                {
                    _expression = "";
                    _resultShown = false;
                }
                else if (_expression.Length > 0)
                {
                    _expression = _expression[..^1];
                }
                break;
            case "=":
                Calculate();
                UpdateDisplay();
                return;
            case "±":
                ToggleSign();
                UpdateDisplay();
                return;
            case "%":
                if (m.Success)
                {
                    _expression = _expression[..m.Index] + $"({m.Groups[1].Value}/100)";
                }
                break;
            case ".":
                if (_resultShown)
                {
                    _expression = "0.";
                    _resultShown = false;
                }
                else if (!(m.Success && m.Groups[1].Value.Contains('.')))
                {
                    _expression += _expression.Length == 0 || "+-*/".Contains(_expression[^1]) ? "0." : ".";
                }
                break;
            default:
                if (char.IsDigit(key[0]))
                {
                    if (_resultShown)
                    {
                        _expression = calcKey;
                        _resultShown = false;
                    }
                    else if (!m.Success
                        || m.Groups[1].Value.TrimStart('-').Replace(".", "").Length < 15)
                    {
                        _expression += calcKey;
                    }
                }
                else if ("+-*/".Contains(calcKey))
                {
                    AppendOperator(calcKey);
                    _resultShown = false;
                }
                break;
        }
        UpdateDisplay();
    }

    private void AppendOperator(string op)
    {
        if (_expression.Length == 0)
        {
            if (op == "-")
            {
                _expression = "-";
            }
            return;
        }
        if ("+-*/".Contains(_expression[^1]))
        {
            _expression = op == "-" && "+*/".Contains(_expression[^1])
                ? _expression + "-"
                : Regex.Replace(_expression, @"[+\-*/]+$", "") + op;
        }
        else
        {
            _expression += op;
        }
    }

    private void ToggleSign()
    {
        var m = Regex.Match(_expression, @"(-?\d+\.?\d*)$");
        if (!m.Success)
        {
            return;
        }
        var num = m.Groups[1].Value;
        var flipped = num.StartsWith('-') ? num[1..] : "-" + num;
        _expression = _expression[..m.Index] + flipped;
    }

    private void Calculate()
    {
        if (_expression.Length == 0)
        {
            return;
        }
        var expr = _expression.TrimEnd('+', '-', '*', '/');
        if (expr.EndsWith('.'))
        {
            expr = expr[..^1];
        }
        if (expr.Length == 0)
        {
            _display.Text = "0";
            _expression = "";
            _resultShown = false;
            return;
        }
        expr = Regex.Replace(expr, @"(-?\d+\.?\d*)%", "($1/100)");
        var result = Eval(expr);
        if (result is null)
        {
            _display.Text = AppUtils.Tr("common.error");
            _expression = "";
            _resultShown = false;
            Relayout();
            return;
        }
        var v = result.Value;
        if (v == Math.Floor(v) && !double.IsInfinity(v))
        {
            v = Math.Floor(v);
        }
        else
        {
            v = Math.Round(v, 10);
        }
        var resultStr = v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        _history.Text = DisplayExpr(expr) + " =";
        _display.Text = FormatNumber(resultStr);
        _expression = resultStr;
        _resultShown = true;
    }

    private static string DisplayExpr(string expression)
    {
        var s = Regex.Replace(expression, @"(^|[+\-*/])-(\d+\.?\d*)", m1 => $"{m1.Groups[1].Value}(-{m1.Groups[2].Value})");
        return s.Replace("/", " ÷ ").Replace("*", " × ").Replace("-", " − ").Replace("+", " + ");
    }

    private static string FormatNumber(string numStr)
    {
        if (numStr.Contains('e') || numStr.Contains('E'))
        {
            return numStr;
        }
        var sign = numStr.StartsWith('-') ? "-" : "";
        if (sign.Length > 0)
        {
            numStr = numStr[1..];
        }
        var dot = numStr.IndexOf('.');
        var integer = dot < 0 ? numStr : numStr[..dot];
        var dec = dot < 0 ? "" : numStr[(dot + 1)..];
        if (integer.Length > 15 || integer.Length + dec.Length > 18)
        {
            try
            {
                var sci = (dot < 0 ? int.Parse(numStr) : double.Parse(numStr))
                    .ToString("e15", System.Globalization.CultureInfo.InvariantCulture);
                return sign + sci.TrimEnd('0').TrimEnd('.');
            }
            catch (Exception e)
            {

                Log.Debug($"[计算器] 数字格式化失败: {e.Message}");
                return sign + numStr;
            }
        }
        var grouped = long.Parse(integer).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        return sign + grouped + (dot >= 0 ? "." + dec : "");
    }

    // 递归下降求值 + - * / 与括号(% 已在表达式里转成 (n/100))
    private sealed class ExprParser
    {
        private readonly string _s;
        private int _i;

        public ExprParser(string s) => _s = s;

        public bool AtEnd => _i >= _s.Length;

        public double ParseExpr()
        {
            var v = ParseTerm();
            while (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
            {
                var op = _s[_i++];
                var r = ParseTerm();
                v = op == '+' ? v + r : v - r;
            }
            return v;
        }

        private double ParseTerm()
        {
            var v = ParseFactor();
            while (_i < _s.Length && (_s[_i] == '*' || _s[_i] == '/'))
            {
                var op = _s[_i++];
                var r = ParseFactor();
                v = op == '*' ? v * r : v / r;
            }
            return v;
        }

        private double ParseFactor()
        {
            if (_i < _s.Length && _s[_i] == '(')
            {
                _i++;
                var v = ParseExpr();
                if (_i < _s.Length && _s[_i] == ')')
                {
                    _i++;
                }
                return v;
            }
            if (_i < _s.Length && _s[_i] == '-')
            {
                _i++;
                return -ParseFactor();
            }
            var start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
            {
                _i++;
            }
            return double.Parse(_s[start.._i], System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static double? Eval(string expr)
    {
        try
        {
            var p = new ExprParser(expr);
            var v = p.ParseExpr();
            // NaN 与 ±Infinity(如 1÷0) 都判为无效 否则 ∞ 走进 FormatNumber 的 long.Parse 抛异常
            return p.AtEnd && !double.IsNaN(v) && !double.IsInfinity(v) ? v : null;
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 求值失败: {e.Message}");
            return null;
        }
    }

    private void UpdateDisplay()
    {
        if (_expression.Length == 0)
        {
            _history.Text = "";
            _display.Text = "0";
            Relayout();
            return;
        }
        var head = SplitExpression(out var current);
        _history.Text = head.Length > 0 ? DisplayExpr(head) : "";
        _display.Text = current.Length > 0 ? DisplayExpr(current) : "0";
        Relayout();
    }

    private string SplitExpression(out string current)
    {
        current = "";
        if (_expression.Length == 0)
        {
            return "";
        }
        if ("+-*/".Contains(_expression[^1]))
        {
            return _expression;
        }
        var m = Regex.Match(_expression, @"(.+?[+\-*/])(-?\d*\.?\d*)$");
        if (m.Success)
        {
            current = m.Groups[2].Value;
            return m.Groups[1].Value;
        }
        current = _expression;
        return "";
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _history.Foreground = WidgetInk.Muted(p.Text);
        _display.Foreground = WidgetInk.Fg(p.Text);
        _screen.Background = new SolidColorBrush(p.Text, CardIsDark ? 0.05 : 0.04);
        foreach (var key in _keys)
        {
            key.Apply(p, CardAccent.Value, CardIsDark);
        }
        Relayout();
    }
}


// ExtraWidgets.cs

/// <summary>全局系统采样: CPU/RAM/GPU 网络 麦克风分贝</summary>
internal static class SystemSampler
{
    private static System.Diagnostics.PerformanceCounter? _cpu;
    private static bool _perfBroken;

    public static event Action<double, double, double>? PerfReady; // cpu ram gpu
    public static event Action<double, double>? NetReady;          // down up (bits/s)

    public static void EnsureStarted()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        // 性能计数器查询(GPU 实例枚举可达数百 ms)严禁占用 UI 线程 -> 后台循环采样 事件回 UI 线程
        Task.Run(async () =>
        {
            try
            {
                _cpu = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpu.NextValue();
            }
            catch (Exception e)
            {

                Log.Warning($"[PERF] CPU 计数器不可用: {e.Message}");
                _perfBroken = true;
            }
            RebuildGpuCounters();
            RebuildNetCounters();
            while (true)
            {
                try
                {
                    await Task.Delay(1000);
                    Sample();
                }
                catch (Exception e)
                {

                    Log.Debug($"[PERF] 采样异常: {e.Message}");
                    // 采样异常忽略
                }
            }
        });
        MicSampler.EnsureStarted();
    }

    private static bool _started;

    // Network Interface 计数器按网卡实例枚举求和(非单实例计数器) 实例随网卡启停变化 每 30 次重建
    private static List<System.Diagnostics.PerformanceCounter>? _netInList;
    private static List<System.Diagnostics.PerformanceCounter>? _netOutList;
    private static int _netAge;

    private static void RebuildNetCounters()
    {
        try
        {
            var cat = new System.Diagnostics.PerformanceCounterCategory("Network Interface");
            _netInList = new List<System.Diagnostics.PerformanceCounter>();
            _netOutList = new List<System.Diagnostics.PerformanceCounter>();
            foreach (var nic in cat.GetInstanceNames())
            {
                _netInList.Add(new System.Diagnostics.PerformanceCounter("Network Interface", "Bytes Received/sec", nic));
                _netOutList.Add(new System.Diagnostics.PerformanceCounter("Network Interface", "Bytes Sent/sec", nic));
            }
            foreach (var c in _netInList.Concat(_netOutList))
            {
                c.NextValue();
            }
            _netAge = 0;
        }
        catch (Exception e)
        {
            Log.Debug($"[PERF] 网卡计数器枚举失败: {e.Message}");
            _netInList = null;
            _netOutList = null;
        }
    }

    private static (double Down, double Up) SampleNet()
    {
        if (_netInList is null || _netOutList is null || _netInList.Count == 0)
        {
            RebuildNetCounters();
            return (0, 0);
        }
        _netAge++;
        if (_netAge > 30)
        {
            RebuildNetCounters();
        }
        double down = 0;
        double up = 0;
        foreach (var c in _netInList)
        {
            try { down += Math.Max(0, c.NextValue()); } catch { }
        }
        foreach (var c in _netOutList)
        {
            try { up += Math.Max(0, c.NextValue()); } catch { }
        }
        return (down * 8, up * 8);
    }


    // GPU Engine 计数器按实例枚举求和 实例随应用启停变化 每 30 次重建
    private static List<System.Diagnostics.PerformanceCounter>? _gpuCounters;
    private static int _gpuAge;

    private static void RebuildGpuCounters()
    {
        try
        {
            var cat = new System.Diagnostics.PerformanceCounterCategory("GPU Engine");
            _gpuCounters = cat.GetInstanceNames()
                .Select(n => new System.Diagnostics.PerformanceCounter("GPU Engine", "Utilization Percentage", n))
                .ToList();
            foreach (var c in _gpuCounters)
            {
                c.NextValue();
            }
            _gpuAge = 0;
        }
        catch (Exception e)
        {

            Log.Warning($"[PERF] GPU 计数器不可用: {e.Message}");
            _gpuCounters = null;
        }
    }

    private static double SampleGpu()
    {
        if (_gpuCounters is null || _gpuCounters.Count == 0)
        {
            RebuildGpuCounters();
            return 0;
        }
        _gpuAge++;
        if (_gpuAge > 30)
        {
            RebuildGpuCounters();
        }
        double sum = 0;
        foreach (var c in _gpuCounters)
        {
            try
            {
                sum += c.NextValue();
            }
            catch (Exception e)
            {

                Log.Debug($"[PERF] GPU 实例失效跳过: {e.Message}");
                // 实例失效跳过
            }
        }
        return Math.Min(100, sum);
    }

    private static void Sample()
    {
        try
        {
            var cpu = _perfBroken ? 0 : Math.Min(100, _cpu?.NextValue() ?? 0);
            var ram = MemoryUsagePercent();
            var gpu = SampleGpu();
            var (down, up) = SampleNet();
            // 订阅方(控件)要求 UI 线程
            Dispatcher.UIThread.Post(() =>
            {
                PerfReady?.Invoke(cpu, ram, gpu);
                NetReady?.Invoke(down, up);
            });
        }
        catch (Exception e)
        {

            Log.Debug($"[PERF] 采样异常: {e.Message}");
            // 采样异常忽略
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private static double MemoryUsagePercent()
    {
        var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref st) ? st.dwMemoryLoad : 0;
    }
}

/// <summary>麦克风采样 winmm waveIn RMS -> dB(约 30~90 语音区间)</summary>
internal static class MicSampler
{
    private static bool _started;
    private static WaveInProc? _proc;

    public static event Action<double>? DbReady;

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    private delegate void WaveInProc(IntPtr h, uint msg, IntPtr inst, IntPtr wavhdr, IntPtr p2);

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out IntPtr h, uint devId, ref WAVEFORMATEX fmt, WaveInProc proc, IntPtr inst, uint flags);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, uint size);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, uint size);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(IntPtr h);

    private const uint CallbackFunction = 0x30000;
    private const uint WimData = 0x3C0;

    private static IntPtr _handle;
    private static IntPtr _headerA;
    private static IntPtr _headerB;
    private static IntPtr _dataA;
    private static IntPtr _dataB;
    private const int MicBufBytes = 32000;
    private static GCHandle _procHandle;

    public static void EnsureStarted()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        try
        {
            _proc = OnWaveIn;
            _procHandle = GCHandle.Alloc(_proc);
            var fmt = new WAVEFORMATEX
            {
                wFormatTag = 1,
                nChannels = 1,
                nSamplesPerSec = 16000,
                nAvgBytesPerSec = 32000,
                nBlockAlign = 2,
                wBitsPerSample = 16,
                cbSize = 0,
            };
            var hr = waveInOpen(out _handle, 0xFFFFFFFFu, ref fmt, _proc, IntPtr.Zero, CallbackFunction);
            if (hr != 0)
            {
                Log.Warning($"[MIC] waveInOpen 失败 hr=0x{hr:X8}");
                return;
            }
            _dataA = Marshal.AllocHGlobal(MicBufBytes);
            _dataB = Marshal.AllocHGlobal(MicBufBytes);
            _headerA = AllocHeader(_dataA);
            _headerB = AllocHeader(_dataB);
            waveInAddBuffer(_handle, _headerA, (uint)Marshal.SizeOf<WAVEHDR>());
            waveInAddBuffer(_handle, _headerB, (uint)Marshal.SizeOf<WAVEHDR>());
            waveInStart(_handle);
            Log.Info("[MIC] 麦克风采样已启动");
        }
        catch (Exception e)
        {
            Log.Warning($"[MIC] 麦克风采样启动失败: {e.Message}");
        }
    }

    // 数据缓冲必须是非托管内存: 托管数组会被 GC 搬移 winmm 线程仍写旧地址 -> 堆损坏 -> 随机闪退
    private static IntPtr AllocHeader(IntPtr data)
    {
        var h = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
        var hdr = new WAVEHDR { lpData = data, dwBufferLength = MicBufBytes };
        Marshal.StructureToPtr(hdr, h, false);
        waveInPrepareHeader(_handle, h, (uint)Marshal.SizeOf<WAVEHDR>());
        return h;
    }

    private static void OnWaveIn(IntPtr h, uint msg, IntPtr inst, IntPtr wavhdr, IntPtr p2)
    {
        if (msg != WimData || wavhdr == IntPtr.Zero)
        {
            return;
        }
        try
        {
            var hdr = Marshal.PtrToStructure<WAVEHDR>(wavhdr);
            var recorded = (int)hdr.dwBytesRecorded;
            if (recorded > 0 && hdr.lpData != IntPtr.Zero)
            {
                var bytes = new byte[recorded];
                Marshal.Copy(hdr.lpData, bytes, 0, recorded);
                double sum = 0;
                var samples = recorded / 2;
                for (var i = 0; i + 1 < recorded; i += 2)
                {
                    var s = BitConverter.ToInt16(bytes, i);
                    sum += (double)s * s;
                }
                var rms = samples > 0 ? Math.Sqrt(sum / samples) : 0;
                var db = rms > 0 ? Math.Clamp(90 + 20 * Math.Log10(rms / 32768.0), 0, 120) : 0;
                Dispatcher.UIThread.Post(() => DbReady?.Invoke(db));
            }
            waveInAddBuffer(_handle, wavhdr, (uint)Marshal.SizeOf<WAVEHDR>());
        }
        catch (Exception e)
        {

            Log.Debug($"[PERF] 采样异常: {e.Message}");
            // 采样异常忽略
        }
    }
}

/// <summary>便签横线 纸感</summary>
internal sealed class NoteRules : Control
{
    public AvColor Line { get; set; } = AvColor.Parse("#E7C56A");
    public double Spacing { get; set; } = 21;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 2 || h < 2)
        {
            return;
        }
        var pen = new Pen(new SolidColorBrush(Line, 0.13), 1);
        for (var y = Spacing; y < h; y += Spacing)
        {
            ctx.DrawLine(pen, new Point(0, y), new Point(w, y));
        }
    }
}

/// <summary>便签</summary>
public sealed class StickyNoteWidget : WidgetCardBase
{
    // 便签只保留一个强调色 纸面走全局卡片皮肤 不再自绘纯色底
    private static readonly Dictionary<string, string> Colors = new()
    {
        ["yellow"] = "#E7C56A",
        ["green"] = "#A9C48A",
        ["blue"] = "#93B7D6",
        ["pink"] = "#DDA3AD",
        ["orange"] = "#E2B183",
        ["purple"] = "#B7A6D0",
    };

    private readonly Border _header = new() { Height = 38 };
    private readonly Border _dot = new() { Width = 18, Height = 3, CornerRadius = new CornerRadius(1.5) };
    private readonly TextBlock _date = new()
    {
        Text = DateTime.Today.ToString("yyyy-MM-dd"),
        FontSize = 11,
        LetterSpacing = 0.4,
        FontFamily = WidgetFonts.Ui,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBox _editor = new()
    {
        AcceptsReturn = true,
        AcceptsTab = false,
        TextWrapping = TextWrapping.Wrap,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        FontSize = 14,
        Padding = new Thickness(WidgetSpace.Pad, 10, WidgetSpace.Pad, 14),
        PlaceholderText = AppUtils.Tr("sticky_note.placeholder"),
        FontFamily = WidgetFonts.Ui,
        LineHeight = 23,
        VerticalContentAlignment = VerticalAlignment.Top,
        MinHeight = 0,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private string _c = "#E7C56A";
    private readonly NoteRules _rules = new()
    {
        IsHitTestVisible = false,
        Spacing = 23,
        Margin = new Thickness(WidgetSpace.Pad, 10, WidgetSpace.Pad, 14),
    };
    private readonly TextBlock _count = new()
    {
        Text = "0 字",
        FontSize = WidgetType.Micro,
        FontFamily = WidgetFonts.Ui,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, WidgetSpace.Pad, 7),
        IsHitTestVisible = false,
        Opacity = 0,
    };
    private string? _dataFile;
    private readonly DispatcherTimer _saveTimer;

    public StickyNoteWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        var colorKey = "yellow";
        if (config is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty("color", out var c)
            && c.ValueKind == JsonValueKind.String)
        {
            colorKey = c.GetString() ?? "yellow";
        }
        _c = Colors.GetValueOrDefault(colorKey, Colors["yellow"]);

        // 覆盖 Fluent TextBox 各状态底色 便签纸面必须透出卡片皮肤
        foreach (var key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver",
                     "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
                 })
        {
            _editor.Resources[key] = Brushes.Transparent;
        }
        foreach (var key in new[]
                 {
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver",
                     "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled",
                 })
        {
            _editor.Resources[key] = Brushes.Transparent;
        }

        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(WidgetSpace.Pad, 0, WidgetSpace.Pad, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = WidgetSpace.GapXs,
        };
        headerRow.Children.Add(_dot);
        headerRow.Children.Add(_date);
        _header.Child = headerRow;

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
        };
        Grid.SetRow(_header, 0);
        Grid.SetRow(_rules, 1);
        Grid.SetRow(_editor, 1);
        Grid.SetRow(_count, 1);
        root.Children.Add(_header);
        root.Children.Add(_rules);
        root.Children.Add(_editor);
        root.Children.Add(_count);
        Card.Child = root;
        Content = Card;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _editor.TextChanged += (_, _) =>
        {
            UpdateCount();
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        // 计数只在悬停或聚焦时出现 不打扰书写
        _editor.GotFocus += (_, _) => _count.Opacity = 1;
        _editor.LostFocus += (_, _) => _count.Opacity = 0;
        PointerEntered += (_, _) => _count.Opacity = 1;
        PointerExited += (_, _) =>
        {
            if (!_editor.IsFocused)
            {
                _count.Opacity = 0;
            }
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
        ApplyStickyStyle();
        UpdateCount();
    }

    private void UpdateCount()
    {
        var count = _editor.Text?.Length ?? 0;
        _count.Text = AppUtils.Tr("sticky_note.char_count", ("count", count));
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_dataFile is null && !string.IsNullOrEmpty(ComponentId))
        {
            _dataFile = IOPath.Combine(Paths.DataUser, "notes", $"{ComponentId}.json");
            try
            {
                if (File.Exists(_dataFile)
                    && JsonNode.Parse(File.ReadAllText(_dataFile)) is JsonObject obj)
                {
                    _editor.Text = (string?)obj["text"] ?? "";
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Note] 加载便签失败: {ex.Message}");
            }
        }
    }

    private void Save()
    {
        try
        {
            if (_dataFile is null)
            {
                return;
            }
            Directory.CreateDirectory(IOPath.GetDirectoryName(_dataFile)!);
            File.WriteAllText(_dataFile, new JsonObject
            {
                ["text"] = _editor.Text ?? "",
            }.ToJsonString());
        }
        catch (Exception e)
        {
            Log.Warning($"[Note] 保存便签失败: {e.Message}");
        }
    }

    // 纸面跟随全局卡片皮肤 便签色只做强调
    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var header = AvColor.Parse(_c);
        _dot.Background = new SolidColorBrush(header);
        _date.Foreground = WidgetInk.Muted(p.Text);
        _header.BorderBrush = new SolidColorBrush(p.Line);
        _header.BorderThickness = new Thickness(0, 0, 0, 1);
        _rules.Line = p.Text;
        _rules.IsVisible = !CardIsDark;
        _rules.InvalidateVisual();
        _editor.Foreground = new SolidColorBrush(p.Text);
        _editor.CaretBrush = new SolidColorBrush(p.Text);
        _editor.PlaceholderForeground = new SolidColorBrush(p.Text, CardInk.FaintOpacity);
        _editor.SelectionBrush = new SolidColorBrush(header, 0.30);
        _editor.SelectionForegroundBrush = new SolidColorBrush(p.Text);
        _count.Foreground = WidgetInk.Muted(p.Text);
    }

    private void ApplyStickyStyle() => ApplyCardStyle();
}

/// <summary>快捷启动图标项</summary>
internal sealed class QuickLaunchItem
{
    public string Name = "";
    public string Path = "";
    // 默认项存语言键 切换语言后名称仍跟随
    public string NameKey = "";

    public string DisplayName => Name.Length > 0
        ? Name
        : NameKey.Length > 0
            ? AppUtils.Tr(NameKey)
            : IOPath.GetFileNameWithoutExtension(Path);
}

/// <summary>AI 问答 气泡会话持久化</summary>
public sealed class AiChatWidget : WidgetCardBase
{
    private sealed record ChatMsg(string Role, string Text, string Reasoning = "", long ReasoningMs = 0);

    /// <summary>历史会话</summary>
    private sealed class ChatSession
    {
        public string Id = "";
        public string Title = "";
        public long Ts;
        public List<ChatMsg> Msgs = new();
    }

    private const int MaxHistory = 40;
    private const int ContextTurns = 8;

    private readonly ScrollViewer _scroll = new() { Padding = new Thickness(12, 6, 12, 6) };
    private readonly StackPanel _flow = new() { Spacing = 10 };
    private readonly TextBox _input = new()
    {
        FontSize = 13,
        Padding = new Thickness(2, 4),
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        TextWrapping = TextWrapping.Wrap,
        AcceptsReturn = true,
        MaxHeight = 132,
    };
    private readonly StackPanel _welcome = new()
    {
        Orientation = Orientation.Vertical,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = 10,
        IsVisible = false,
    };
    private readonly Border _composer = new()
    {
        CornerRadius = new CornerRadius(20),
        Padding = new Thickness(12, 8, 8, 8),
        Margin = new Thickness(10, 4, 10, 8),
    };
    private readonly Button _sendBtn = new()
    {
        Width = 32,
        Height = 32,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(16),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Content = FAIcons.IconGlyph("E724", 14),
    };
    private readonly List<ChatSession> _sessions = new();
    private string _currentId = "";
    private string? _dataFile;
    private bool _busy;
    private bool _cursorOn;
    private string _lastLive = "";
    private CancellationTokenSource? _streamCts;
    private Grid? _drawer;
    private Border? _drawerPanel;
    private StackPanel? _drawerList;
    private TranslateTransform? _drawerTranslate;

    // 历史会话制 恒有 current
    private ChatSession Current => _sessions.FirstOrDefault(s => s.Id == _currentId) ?? _sessions[0];
    private List<ChatMsg> _history => Current.Msgs;
    private TextBlock? _liveThink;
    private TextBlock? _liveBody;
    private Grid? _liveThinkBar;
    private TextBlock? _liveHead;
    private DispatcherTimer? _pulseTimer;
    private DispatcherTimer? _blinkTimer;

    public AiChatWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        ReadBgOverride(config);

        var title = new TextBlock
        {
            Text = AppUtils.Tr("ai_chat.title"),
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // header 键组
        var clearBtn = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(15),
            Content = FAIcons.IconGlyph("E74D", 14),
            Opacity = 0.6,
        };
        ToolTip.SetTip(clearBtn, AppUtils.Tr("ai_chat.clear_tip"));
        clearBtn.Click += (_, _) =>
        {
            if (_busy)
            {
                return;
            }
            _ = ConfirmThenClear();
        };

        var newChatBtn = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(15),
            Content = FAIcons.IconGlyph("E109", 14),
            Opacity = 0.6,
        };
        ToolTip.SetTip(newChatBtn, AppUtils.Tr("ai_chat.new_chat_tip"));
        newChatBtn.Click += (_, _) => StartNewChat();

        // 左滑抽屉 覆盖整卡
        var drawerList = new StackPanel();
        _drawerList = drawerList;
        var drawerScroll = new ScrollViewer { Content = drawerList, Padding = new Thickness(6, 0, 6, 10) };
        var drawerTitle = new TextBlock
        {
            Text = AppUtils.Tr("ai_chat.history_tip"),
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var drawerClose = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(15),
            Content = FAIcons.IconGlyph("E711", 13),
            Opacity = 0.6,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        drawerClose.Click += (_, _) => HideDrawer();
        var drawerHead = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Margin = new Thickness(14, 10, 8, 4),
        };
        drawerHead.Children.Add(drawerTitle);
        drawerHead.Children.Add(drawerClose);
        Grid.SetColumn(drawerClose, 1);

        var newChatPill = new Button
        {
            Height = 36,
            Margin = new Thickness(12, 4, 12, 8),
            CornerRadius = new CornerRadius(18),
            Content = AppUtils.Tr("ai_chat.new_chat_tip"),
            FontSize = 12,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        newChatPill.Click += (_, _) =>
        {
            StartNewChat();
            HideDrawer();
        };

        var drawerGrid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,Auto,*") };
        drawerGrid.Children.Add(drawerHead);
        drawerGrid.Children.Add(newChatPill);
        drawerGrid.Children.Add(drawerScroll);
        Grid.SetRow(newChatPill, 1);
        Grid.SetRow(drawerScroll, 2);

        _drawerPanel = new Border { Width = 280, HorizontalAlignment = HorizontalAlignment.Left, Child = drawerGrid };
        // X -280→0 Transitions 整体赋值
        _drawerTranslate = new TranslateTransform { X = -280 };
        _drawerTranslate.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            },
        };
        _drawerPanel.RenderTransform = _drawerTranslate;
        var scrim = new Border { Background = new SolidColorBrush(AvColor.FromArgb(110, 0, 0, 0)) };
        scrim.PointerReleased += (_, e) =>
        {
            HideDrawer();
            e.Handled = true;
        };
        _drawer = new Grid { IsVisible = false };
        _drawer.Children.Add(scrim);
        _drawer.Children.Add(_drawerPanel);

        var historyBtn = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(15),
            Content = FAIcons.IconGlyph("E81C", 14),
            Opacity = 0.6,
        };
        ToolTip.SetTip(historyBtn, AppUtils.Tr("ai_chat.history_tip"));
        historyBtn.Click += (_, _) => ShowDrawer();

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        btnRow.Children.Add(newChatBtn);
        btnRow.Children.Add(historyBtn);
        btnRow.Children.Add(clearBtn);

        var header = new Grid { Margin = new Thickness(12, 8, 12, 4) };
        header.Children.Add(title);
        header.Children.Add(btnRow);

        _scroll.Content = _flow;
        Grid.SetRow(_scroll, 1);

        // 空态欢迎页
        var logo = FAIcons.IconGlyph("E99A", 34);
        logo.Foreground = new SolidColorBrush(AvColor.Parse(Config.ThemeColor.Value));
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        var hi = new TextBlock
        {
            Text = AppUtils.Tr("ai_chat.welcome"),
            FontSize = 17,
            FontWeight = FontWeight.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _welcome.Children.Add(logo);
        _welcome.Children.Add(hi);
        var chatHost = new Grid();
        chatHost.Children.Add(_scroll);
        chatHost.Children.Add(_welcome);
        Grid.SetRow(chatHost, 1);

        _sendBtn.Click += (_, _) =>
        {
            // busy 即停生
            if (_busy)
            {
                _streamCts?.Cancel();
            }
            else
            {
                _ = SendAsync();
            }
        };
        if (_sendBtn.Content is FAFontIcon sendIcon)
        {
            sendIcon.Foreground = Brushes.White;
        }

        // 剥 PART_BorderElement 模板内框
        _input.TemplateApplied += (_, _) => KillInputChrome();

        var bottomRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
        };
        bottomRow.Children.Add(_sendBtn);
        Grid.SetColumn(_sendBtn, 1);
        var composerGrid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,Auto"), RowSpacing = 4 };
        composerGrid.Children.Add(_input);
        composerGrid.Children.Add(bottomRow);
        Grid.SetRow(bottomRow, 1);
        _composer.Child = composerGrid;
        Grid.SetRow(_composer, 2);

        _input.PlaceholderText = AppUtils.Tr("ai_chat.watermark");
        _input.KeyDown += (_, e) =>
        {
            // Enter 发送 Shift 换行
            if (e.Key == Key.Enter && e.KeyModifiers != KeyModifiers.Shift)
            {
                e.Handled = true;
                _ = SendAsync();
            }
        };

        var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*,Auto,Auto") };
        root.Children.Add(header);
        root.Children.Add(chatHost);
        root.Children.Add(_composer);

        // 模型名+AI 声明
        var footer = new TextBlock
        {
            FontSize = 10,
            Opacity = 0.55,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        // 抽屉置顶 跨 4 行
        root.Children.Add(_drawer);
        Grid.SetRowSpan(_drawer, 4);
        void UpdateModelLabel()
        {
            footer.Text = $"{AiService.Resolve().Model} · {AppUtils.Tr("ai_chat.ai_note")}";
        }
        UpdateModelLabel();
        Config.AiProvider.ValueChanged += _ => UpdateModelLabel();
        Config.AiDefaultModel.ValueChanged += _ => UpdateModelLabel();
        Config.AiZhipuModel.ValueChanged += _ => UpdateModelLabel();
        Config.AiDeepseekModel.ValueChanged += _ => UpdateModelLabel();
        Config.AiCustomModel.ValueChanged += _ => UpdateModelLabel();

        StyleComposer();
        Card.Child = root;
        Content = Card;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_dataFile is null && !string.IsNullOrEmpty(ComponentId))
        {
            _dataFile = IOPath.Combine(Paths.DataUser, "aichat", $"{ComponentId}.json");
            try
            {
                if (File.Exists(_dataFile) && JsonNode.Parse(File.ReadAllText(_dataFile)) is JsonNode root)
                {
                    LoadSessions(root);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AIChat] 读取记录失败: {ex.Message}");
            }
            if (_sessions.Count == 0)
            {
                CreateSession();
            }
            Rebuild();
        }
    }

    // 会话档 兼容旧数组
    private void LoadSessions(JsonNode root)
    {
        if (root is JsonArray arr)
        {
            var s = new ChatSession { Id = Guid.NewGuid().ToString("N"), Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds() };
            foreach (var n in arr)
            {
                if (n is JsonObject o)
                {
                    s.Msgs.Add(new ChatMsg(
                    (string?)o["role"] ?? "user",
                    (string?)o["text"] ?? "",
                    (string?)o["reasoning"] ?? "",
                    o["reasoning_ms"] is JsonValue jv && jv.TryGetValue<long>(out var msv) ? msv : 0));
                }
            }
            _sessions.Add(s);
            _currentId = s.Id;
            return;
        }
        if (root is JsonObject obj)
        {
            _currentId = (string?)obj["current"] ?? "";
            if (obj["sessions"] is JsonArray list)
            {
                foreach (var n in list)
                {
                    if (n is not JsonObject so)
                    {
                        continue;
                    }
                    var s = new ChatSession
                    {
                        Id = (string?)so["id"] ?? Guid.NewGuid().ToString("N"),
                        Title = (string?)so["title"] ?? "",
                        Ts = so["ts"] is JsonValue tv && tv.TryGetValue<long>(out var tsv) ? tsv : 0,
                    };
                    if (so["msgs"] is JsonArray ms)
                    {
                        foreach (var m in ms)
                        {
                            if (m is JsonObject o)
                            {
                                s.Msgs.Add(new ChatMsg(
                                (string?)o["role"] ?? "user",
                                (string?)o["text"] ?? "",
                                (string?)o["reasoning"] ?? "",
                                o["reasoning_ms"] is JsonValue jv && jv.TryGetValue<long>(out var msv) ? msv : 0));
                            }
                        }
                    }
                    _sessions.Add(s);
                }
            }
            if (_sessions.All(s => s.Id != _currentId))
            {
                _currentId = _sessions.FirstOrDefault()?.Id ?? "";
            }
        }
    }

    private void CreateSession()
    {
        var s = new ChatSession { Id = Guid.NewGuid().ToString("N"), Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds() };
        _sessions.Insert(0, s);
        _currentId = s.Id;
        // 上限 20 淘汰最旧
        while (_sessions.Count > 20)
        {
            var oldest = _sessions.LastOrDefault(x => x.Id != _currentId);
            if (oldest is null)
            {
                break;
            }
            _sessions.Remove(oldest);
        }
    }

    // 新会话 空会话不重复开
    private void StartNewChat()
    {
        if (_busy || Current.Msgs.Count == 0)
        {
            return;
        }
        CreateSession();
        Save();
        Rebuild();
    }

    // 主题重绘 busy 跳过
    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        StyleComposer();
        if (_busy)
        {
            return;
        }
        if (_flow.Children.Count > 0)
        {
            Rebuild();
        }
    }

    // busy 切停生方块
    private void SetSendBusy(bool busy)
    {
        var icon = FAIcons.IconGlyph(busy ? "E15B" : "E110", busy ? 12 : 14);
        icon.Foreground = Brushes.White;
        _sendBtn.Content = icon;
    }

    // 剥 PART_BorderElement 内框下划线
    private void KillInputChrome()
    {
        if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(_input).FirstOrDefault(v => v is Border { Name: "PART_BorderElement" }) is Border border)
        {
            border.BorderThickness = new Thickness(0);
            border.Background = Brushes.Transparent;
            border.BorderBrush = null;
        }
    }

    // composer 主题化
    private void StyleComposer()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var dark = CardIsDark;
        var accent = AvColor.Parse(Config.ThemeColor.Value);
        _composer.Background = new SolidColorBrush(p.BandB);
        var channel = dark ? (byte)255 : (byte)0;
        _composer.BorderBrush = new SolidColorBrush(AvColor.FromArgb(dark ? (byte)15 : (byte)26, channel, channel, channel));
        _composer.BorderThickness = new Thickness(1);
        _input.Foreground = new SolidColorBrush(p.Text);
        _input.CaretBrush = new SolidColorBrush(accent);
        _sendBtn.Background = new SolidColorBrush(accent);
        if (_drawerPanel is { } panel)
        {
            panel.Background = new SolidColorBrush(p.BandB);
        }
    }

    private Control BuildMsg(ChatMsg m, bool isLast = false, bool showActions = true)
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var textCol = p.Text;
        var userBg = p.Blend(AvColor.Parse(Config.ThemeColor.Value), 0.22);
        if (m.Role != "user")
        {
            // 助手通栏 思考折叠+md+操作行
            var stack = new StackPanel { Spacing = 2 };
            if (!string.IsNullOrWhiteSpace(m.Reasoning))
            {
                stack.Children.Add(BuildThinkBlock(AppUtils.Tr("ai_chat.thought", ("s", m.ReasoningMs / 1000.0)), m.Reasoning, expanded: false));
            }
            stack.Children.Add(MarkdownLook.Create(m.Text, compact: true));
            if (showActions)
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Opacity = 0.55 };
                var copyBtn = MiniAction("E898", "ai_chat.copy_tip");
                copyBtn.Click += (_, _) => _ = CopyTextAsync(m.Text);
                actions.Children.Add(copyBtn);
                if (isLast)
                {
                    var regenBtn = MiniAction("E72C", "ai_chat.regen_tip");
                    regenBtn.Click += (_, _) => _ = RegenerateAsync();
                    actions.Children.Add(regenBtn);
                }
                stack.Children.Add(actions);
            }
            return stack;
        }
        var border = new Border
        {
            Background = new SolidColorBrush(userBg),
            CornerRadius = new CornerRadius(12, 4, 12, 12),
            Padding = new Thickness(12, 7),
            MaxWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock { Text = m.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = new SolidColorBrush(textCol) },
        };
        if (!showActions)
        {
            return border;
        }
        // 用户泡下 复制/编辑
        var wrap = new StackPanel { Spacing = 1 };
        wrap.Children.Add(border);
        var uActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Opacity = 0.45, HorizontalAlignment = HorizontalAlignment.Right };
        var uCopy = MiniAction("E898", "ai_chat.copy_tip");
        uCopy.Click += (_, _) => _ = CopyTextAsync(m.Text);
        uActions.Children.Add(uCopy);
        var uEdit = MiniAction("E104", "ai_chat.edit_tip");
        uEdit.Click += (_, _) => EditUserMsg(m);
        uActions.Children.Add(uEdit);
        wrap.Children.Add(uActions);
        return wrap;
    }

    // 确认弹窗
    private async Task<bool> ConfirmAsync(string title, string content)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        return owner is not null && await Common.Confirm(owner, title, content);
    }

    // 清空当前会话
    private async Task ConfirmThenClear()
    {
        if (!await ConfirmAsync(AppUtils.Tr("ai_chat.confirm_clear_title"), AppUtils.Tr("ai_chat.confirm_clear_content")))
        {
            return;
        }
        _history.Clear();
        Rebuild();
        Save();
    }

    // 编辑回填+截断分支 record 值相等 按引用定位
    private void EditUserMsg(ChatMsg m)
    {
        if (_busy)
        {
            return;
        }
        var idx = -1;
        for (var i = 0; i < _history.Count; i++)
        {
            if (ReferenceEquals(_history[i], m))
            {
                idx = i;
                break;
            }
        }
        if (idx < 0)
        {
            return;
        }
        _input.Text = m.Text;
        for (var i = _history.Count - 1; i >= idx; i--)
        {
            _history.RemoveAt(i);
        }
        Save();
        Rebuild();
        _input.Focus();
    }

    // 思考块 官方规格 竖线+缩进
    private Control BuildThinkBlock(string title, string reasoning, bool expanded)
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var accent = AvColor.Parse(Config.ThemeColor.Value);
        var sub = new SolidColorBrush(p.Sub);
        var titleTb = new TextBlock { Text = title, FontSize = 12, Foreground = sub, VerticalAlignment = VerticalAlignment.Center };
        var chev = FAIcons.IconGlyph("E76C", 10);
        chev.VerticalAlignment = VerticalAlignment.Center;
        var icon = FAIcons.IconGlyph("E99A", 13);
        icon.Foreground = new SolidColorBrush(accent);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var headRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        headRow.Children.Add(icon);
        headRow.Children.Add(titleTb);
        headRow.Children.Add(chev);
        var head = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new Thickness(0, 2),
            Child = headRow,
        };

        var body = new TextBlock
        {
            Text = reasoning,
            FontSize = 13,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            Foreground = sub,
            Margin = new Thickness(14, 0, 0, 0),
        };
        var line = new Border
        {
            Width = 1,
            Background = new SolidColorBrush(p.Line),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(7, 0, 0, 0),
        };
        var content = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("8,*"),
            Margin = new Thickness(0, 2, 0, 0),
            IsVisible = expanded,
        };
        content.Children.Add(line);
        content.Children.Add(body);
        Grid.SetColumn(body, 1);

        var open = expanded;
        head.PointerEntered += (_, _) => titleTb.Foreground = new SolidColorBrush(p.Text);
        head.PointerExited += (_, _) => titleTb.Foreground = sub;
        head.PointerReleased += (_, e) =>
        {
            open = !open;
            content.IsVisible = open;
            chev.Glyph = char.ConvertFromUtf32(open ? 0xE70D : 0xE76C);
            e.Handled = true;
        };

        var root = new StackPanel();
        root.Children.Add(head);
        root.Children.Add(content);
        return root;
    }

    // 消息操作小按钮
    private Button MiniAction(string hex, string tipKey)
    {
        var btn = new Button
        {
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = FAIcons.IconGlyph(hex, 12),
        };
        ToolTip.SetTip(btn, AppUtils.Tr(tipKey));
        return btn;
    }

    private async Task CopyTextAsync(string text)
    {
        try
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip is not null)
            {
                await clip.SetTextAsync(text);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[AIChat] 复制失败: {e.Message}");
        }
    }

    private void Rebuild()
    {
        _welcome.IsVisible = _history.Count == 0 && !_busy;
        _flow.Children.Clear();
        for (var i = 0; i < _history.Count; i++)
        {
            var m = _history[i];
            _flow.Children.Add(BuildMsg(m, isLast: i == _history.Count - 1, showActions: !_busy));
        }
        if (_busy)
        {
            _flow.Children.Add(BuildMsg(new ChatMsg("assistant", AppUtils.Tr("ai_chat.thinking")), showActions: false));
        }
        _scroll.ScrollToEnd();
    }

    // live 行 流式渲染
    private void ShowLiveRow()
    {
        _welcome.IsVisible = false;
        _flow.Children.Clear();
        for (var i = 0; i < _history.Count; i++)
        {
            _flow.Children.Add(BuildMsg(_history[i], showActions: false));
        }
        SetSendBusy(true);
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var thinkIcon = FAIcons.IconGlyph("E99A", 13);
        thinkIcon.Foreground = new SolidColorBrush(AvColor.Parse(Config.ThemeColor.Value));
        thinkIcon.VerticalAlignment = VerticalAlignment.Center;
        _liveHead = new TextBlock
        {
            Text = AppUtils.Tr("ai_chat.thinking"),
            FontSize = 12,
            Foreground = new SolidColorBrush(p.Sub),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        headRow.Children.Add(thinkIcon);
        headRow.Children.Add(_liveHead);
        _liveThink = new TextBlock
        {
            Text = "",
            FontSize = 13,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(p.Sub),
        };
        var thinkScroll = new ScrollViewer
        {
            MaxHeight = 120,
            Margin = new Thickness(14, 0, 0, 0),
            Content = _liveThink,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var thinkLine = new Border
        {
            Width = 1,
            Background = new SolidColorBrush(p.Line),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(7, 0, 0, 0),
        };
        _liveThinkBar = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("8,*"),
            IsVisible = false,
        };
        _liveThinkBar.Children.Add(thinkLine);
        _liveThinkBar.Children.Add(thinkScroll);
        Grid.SetColumn(thinkScroll, 1);
        _liveBody = new TextBlock
        {
            Text = "",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(p.Text),
            IsVisible = false,
        };
        var live = new StackPanel { Spacing = 4 };
        live.Children.Add(headRow);
        live.Children.Add(_liveThinkBar);
        live.Children.Add(_liveBody);
        _flow.Children.Add(live);
        _lastLive = "";
        _scroll.ScrollToEnd();

        // 思考头呼吸动画
        _pulseTimer?.Stop();
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        var up = false;
        _pulseTimer.Tick += (_, _) =>
        {
            up = !up;
            if (_liveHead is { IsVisible: true } head)
            {
                head.Opacity = up ? 0.35 : 0.7;
            }
        };
        _pulseTimer.Start();

        // 光标闪烁
        _blinkTimer?.Stop();
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _blinkTimer.Tick += (_, _) =>
        {
            _cursorOn = !_cursorOn;
            if (_liveBody is { IsVisible: true } body)
            {
                body.Text = _lastLive + (_cursorOn ? "▍" : "");
            }
        };
        _blinkTimer.Start();
    }

    // delta 增量 正文折叠思考区
    private void AppendLive(string? reasoning, string? content)
    {
        if (reasoning is { Length: > 0 } && _liveThink is { } think)
        {
            think.Text = string.IsNullOrEmpty(think.Text) ? reasoning.TrimStart() : think.Text + reasoning;
            if (_liveThinkBar is { } bar)
            {
                bar.IsVisible = true;
            }
            FollowScroll();
        }
        if (content is { Length: > 0 } && _liveBody is { } body)
        {
            if (_pulseTimer is { } pt)
            {
                pt.Stop();
            }
            if (_liveThinkBar is { } bar2)
            {
                bar2.IsVisible = false;
            }
            if (_liveHead is { } head)
            {
                head.IsVisible = false;
            }
            _lastLive += content;
            body.IsVisible = true;
            body.Text = _lastLive + (_cursorOn ? "▍" : "");
            FollowScroll();
        }
    }

    // 贴底跟随
    private void FollowScroll()
    {
        if (_scroll.Extent.Height - _scroll.Offset.Y - _scroll.Viewport.Height < 40)
        {
            _scroll.ScrollToEnd();
        }
    }

    private void StopLiveTimers()
    {
        _pulseTimer?.Stop();
        _pulseTimer = null;
        _blinkTimer?.Stop();
        _blinkTimer = null;
    }

    private void Save()
    {
        try
        {
            if (_dataFile is null)
            {
                return;
            }
            // 自动标题+时间戳
            var cur = Current;
            var first = cur.Msgs.FirstOrDefault(m => m.Role == "user");
            cur.Title = first is null ? "" : (first.Text.Length > 16 ? first.Text[..16] + "…" : first.Text);
            cur.Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Directory.CreateDirectory(IOPath.GetDirectoryName(_dataFile)!);
            var list = new JsonArray();
            foreach (var s in _sessions)
            {
                var msgs = new JsonArray();
                foreach (var m in s.Msgs)
                {
                    msgs.Add(new JsonObject { ["role"] = m.Role, ["text"] = m.Text, ["reasoning"] = m.Reasoning, ["reasoning_ms"] = m.ReasoningMs });
                }
                list.Add(new JsonObject { ["id"] = s.Id, ["title"] = s.Title, ["ts"] = s.Ts, ["msgs"] = msgs });
            }
            File.WriteAllText(_dataFile, new JsonObject { ["current"] = _currentId, ["sessions"] = list }.ToJsonString());
        }
        catch (Exception e)
        {
            Log.Warning($"[AIChat] 保存记录失败: {e.Message}");
        }
    }

    // 抽屉开合
    private void ShowDrawer()
    {
        if (_drawer is null || _drawerList is null || _drawerTranslate is null)
        {
            return;
        }
        FillHistoryList(_drawerList);
        _drawer.IsVisible = true;
        _drawerTranslate.X = 0;
    }

    private void HideDrawer()
    {
        if (_drawer is null || _drawerTranslate is null)
        {
            return;
        }
        _drawerTranslate.X = -280;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (_drawerTranslate is { X: < 0 } && _drawer is not null)
            {
                _drawer.IsVisible = false;
            }
        };
        t.Start();
    }

    // 历史列表 日期分组
    private void FillHistoryList(StackPanel list)
    {
        list.Children.Clear();
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var ordered = _sessions.Where(s => s.Msgs.Count > 0).OrderByDescending(s => s.Ts).ToList();
        if (ordered.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = AppUtils.Tr("ai_chat.empty_history"),
                FontSize = 12,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16),
            });
            return;
        }
        string? lastGroup = null;
        foreach (var s in ordered)
        {
            var session = s;
            var group = GroupLabel(session.Ts);
            if (group != lastGroup)
            {
                list.Children.Add(new TextBlock
                {
                    Text = group,
                    FontSize = 11,
                    Opacity = 0.5,
                    Margin = new Thickness(8, 10, 0, 4),
                });
                lastGroup = group;
            }
            var titleTb = new TextBlock
            {
                Text = string.IsNullOrEmpty(session.Title) ? AppUtils.Tr("ai_chat.new_chat_tip") : session.Title,
                FontSize = 13,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var delBtn = MiniAction("E107", "ai_chat.delete_tip");
            delBtn.Opacity = 0.4;
            delBtn.Click += async (_, _) =>
            {
                if (!await ConfirmAsync(AppUtils.Tr("ai_chat.confirm_del_title"), AppUtils.Tr("ai_chat.confirm_del_content")))
                {
                    return;
                }
                _sessions.Remove(session);
                if (session.Id == _currentId)
                {
                    if (_sessions.Count == 0)
                    {
                        CreateSession();
                    }
                    else
                    {
                        _currentId = _sessions.OrderByDescending(x => x.Ts).First().Id;
                    }
                }
                Save();
                Rebuild();
                FillHistoryList(list);
            };
            var rowGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
            rowGrid.Children.Add(titleTb);
            rowGrid.Children.Add(delBtn);
            Grid.SetColumn(delBtn, 1);
            var row = new Border
            {
                Background = session.Id == _currentId ? new SolidColorBrush(p.BandB) : Brushes.Transparent,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8),
                Margin = new Thickness(0, 1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = rowGrid,
            };
            row.PointerReleased += (_, e) =>
            {
                if (_busy)
                {
                    return;
                }
                _currentId = session.Id;
                Save();
                Rebuild();
                HideDrawer();
                e.Handled = true;
            };
            list.Children.Add(row);
        }
    }

    // 日期分组名
    private static string GroupLabel(long ts)
    {
        var d = ts > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime.Date : DateTime.Today;
        var today = DateTime.Today;
        if (d == today)
        {
            return AppUtils.Tr("ai_chat.group_today");
        }
        if (d == today.AddDays(-1))
        {
            return AppUtils.Tr("ai_chat.group_yesterday");
        }
        if (d >= today.AddDays(-30))
        {
            return AppUtils.Tr("ai_chat.group_30");
        }
        return AppUtils.Tr("ai_chat.group_older");
    }

    private async Task SendAsync()
    {
        var text = _input.Text?.Trim() ?? "";
        if (_busy || text.Length == 0)
        {
            return;
        }
        if (!AiService.Ready)
        {
            _history.Add(new ChatMsg("assistant", AppUtils.Tr("ai_chat.not_ready")));
            Rebuild();
            Save();
            return;
        }
        _history.Add(new ChatMsg("user", text));
        TrimHistory();
        Save();
        _input.Clear();
        await RunCompletionAsync();
    }

    // 重新生成末条回复
    private async Task RegenerateAsync()
    {
        if (_busy || _history.Count == 0 || _history[^1].Role != "assistant")
        {
            return;
        }
        _history.RemoveAt(_history.Count - 1);
        if (_history.Count == 0 || _history[^1].Role != "user")
        {
            Save();
            Rebuild();
            return;
        }
        Save();
        await RunCompletionAsync();
    }

    // 流式补全 live 渲染
    private async Task RunCompletionAsync()
    {
        _busy = true;
        ShowLiveRow();
        var prompt = _history[^1].Role == "user" ? _history[^1].Text : "";
        if (prompt.Length == 0)
        {
            _busy = false;
            SetSendBusy(false);
            Rebuild();
            return;
        }

        // 近 8 轮上下文
        var recent = _history.Take(Math.Max(0, _history.Count - 1)).TakeLast(ContextTurns)
            .Select(m => (m.Role, m.Text)).ToList();
        var ui = Dispatcher.UIThread;
        _streamCts = new CancellationTokenSource();
        var r = await AiService.StreamChatAsync(prompt,
            new AiStreamHandlers(
                OnReasoning: d => ui.Post(() => AppendLive(d, null)),
                OnContent: d => ui.Post(() => AppendLive(null, d))),
            history: recent,
            ct: _streamCts.Token);
        _streamCts.Dispose();
        _streamCts = null;
        string reply;
        if (!r.Ok && r.Error == "已停止")
        {
            // 停生留 partial
            var partial = r.Content.Trim();
            reply = partial.Length > 0 ? partial + AppUtils.Tr("ai_chat.stopped") : AppUtils.Tr("ai_chat.stopped");
        }
        else
        {
            reply = r.Ok ? r.Content : AppUtils.Tr("ai_chat.err") + r.Error;
        }
        _history.Add(new ChatMsg("assistant", reply, r.Reasoning, r.LatencyMs));
        TrimHistory();
        Save();
        StopLiveTimers();
        _busy = false;
        SetSendBusy(false);
        Rebuild();
        _input.Focus();
    }

    private void TrimHistory()
    {
        while (_history.Count > MaxHistory)
        {
            _history.RemoveAt(0);
        }
    }
}

/// <summary>快捷启动条</summary>
public sealed class QuickLaunchDockWidget : WidgetCardBase
{
    private readonly StackPanel _dock = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly CardEmptyState _empty = new(
        FAIcons.IconGlyph("E8A1", 20),
        AppUtils.Tr("quick_launch_component.empty_title"),
        AppUtils.Tr("quick_launch_component.empty_hint"));

    public QuickLaunchDockWidget(ComponentDefinition definition) : base(definition)
    {
        var host = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(20, 16, 20, 16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        host.Children.Add(_dock);
        host.Children.Add(_empty);
        Card.Child = host;
        Content = Card;
        UpdateApps();
        Config.ShowQuickLaunch.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchApps.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchIconSize.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchIconSpacing.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchShowLabels.ValueChanged += _ => UpdateApps();
        Config.ThemeColor.ValueChanged += _ => UpdateApps();
        ActualThemeVariantChanged += (_, _) => UpdateApps();
        ApplyCardStyle();
    }

    private static List<QuickLaunchItem> ReadApps()
    {
        var list = new List<QuickLaunchItem>();
        var v = Config.QuickLaunchApps.Value;
        if (v.ValueKind != JsonValueKind.Array)
        {
            return list;
        }
        foreach (var item in v.EnumerateArray())
        {
            var app = new QuickLaunchItem
            {
                Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
            };
            if (app.Path.Length > 0)
            {
                list.Add(app);
            }
        }
        return list;
    }

    private void UpdateApps()
    {
        var apps = ReadApps();
        IsVisible = Config.ShowQuickLaunch.Value;
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _dock.Children.Clear();
        var size = (double)Config.QuickLaunchIconSize.Value;
        var spacing = Config.QuickLaunchIconSpacing.Value;
        var showLabels = Config.QuickLaunchShowLabels.Value;
        if (apps.Count == 0)
        {
            _dock.IsVisible = false;
            _empty.IsVisible = true;
            UpdateEmptyStyle(p);
            return;
        }
        _dock.IsVisible = true;
        _empty.IsVisible = false;
        foreach (var app in apps)
        {
            var cell = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(spacing / 2.0, 0, spacing / 2.0, 0),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            var iconBorder = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size * 0.2),
                Background = new SolidColorBrush(CardIsDark
                    ? AvColor.FromRgb(50, 50, 50)
                    : AvColor.FromRgb(240, 240, 240)),
                Child = new TextBlock
                {
                    Text = app.Name.Length > 0 ? app.Name[..1].ToUpperInvariant() : "?",
                    FontSize = size * 0.42,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = CardIsDark ? Brushes.White : Brushes.DimGray,
                },
            };
            TrySetExeIcon(app.Path, iconBorder);
            cell.Children.Add(iconBorder);
            if (showLabels)
            {
                cell.Children.Add(new TextBlock
                {
                    Text = app.Name,
                    FontSize = 10.5,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = size + 16,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = CardInk.Fg(p),
                });
            }
            ToolTip.SetTip(cell, app.Path);
            var path = app.Path;
            cell.PointerReleased += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Warning($"[QLC] 启动失败: {ex.Message}");
                }
            };
            _dock.Children.Add(cell);
        }
    }

    private void UpdateEmptyStyle(WidgetSurfacePalette p)
    {
        _empty.Apply(p);
    }

    private void TrySetExeIcon(string path, Border target)
    {
        try
        {
            if (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return;
            }
            using var stream = new MemoryStream();
            icon.ToBitmap().Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            target.Child = new Image { Source = new Bitmap(stream), Width = target.Width, Height = target.Height };
        }
        catch (Exception e)
        {

            Log.Debug($"[QL] 图标提取失败: {e.Message}");
            // 图标提取失败保留字母占位
        }
    }
}

/// <summary>快捷启动 真实图标 + 常用应用 + 编辑态</summary>
public sealed class QuickLaunchGridWidget : WidgetCardBase
{
    private const int MaxApps = 8;
    private const double RootPadX = 10;
    private const double RootPadY = 8;

    private readonly WrapPanel _tiles = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
    };
    private readonly ScrollViewer _scroll;
    private readonly Grid _tileArea = new();
    private readonly Border _empty = new();
    private readonly StackPanel _adder = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 5,
        Margin = new Thickness(0, 3, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly TextBlock _editLabel = new()
    {
        FontSize = 12,
        FontFamily = FAIcons.SymbolsFont,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly Border _editButton;
    private readonly Grid _footer = new();
    private readonly List<QuickLaunchItem> _apps = new();
    private readonly List<Border> _tileBorders = new();

    private string? _storeFile;
    private bool _loaded;
    private bool _editing;
    private bool _hover;
    private bool _ready;

    public QuickLaunchGridWidget(ComponentDefinition definition) : base(definition)
    {
        _scroll = new ScrollViewer
        {
            Content = _tiles,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        _empty.HorizontalAlignment = HorizontalAlignment.Stretch;
        _empty.VerticalAlignment = VerticalAlignment.Stretch;
        _tileArea.Children.Add(_scroll);
        _tileArea.Children.Add(_empty);

        _editButton = new Border
        {
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0),
            Child = _editLabel,
            Cursor = new Cursor(StandardCursorType.Hand),
            ZIndex = 20,
        };
        ToolTip.SetTip(_editButton, Tr("quick_launch.edit"));
        _editButton.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true,
            RoutingStrategies.Bubble);
        _editButton.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            _editing = !_editing;
            _hover = _editing;
            RebuildCells();
        };

        _footer.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto");
        Grid.SetColumn(_adder, 0);
        _footer.Children.Add(_adder);
        _footer.IsVisible = false;

        var root = new Grid
        {
            Margin = new Thickness(RootPadX, RootPadY, RootPadX, RootPadY),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto"),
        };
        Grid.SetRow(_tileArea, 0);
        Grid.SetRow(_footer, 1);
        root.Children.Add(_tileArea);
        root.Children.Add(_footer);
        Card.Child = root;
        Content = Card;

        PointerEntered += (_, _) =>
        {
            _hover = true;
            UpdateEditChrome(WidgetSurfacePalette.Resolve(CardIsDark));
        };
        PointerExited += (_, _) =>
        {
            _hover = false;
            UpdateEditChrome(WidgetSurfacePalette.Resolve(CardIsDark));
        };
        SizeChanged += (_, _) => UpdateMetrics();
        Config.ThemeColor.ValueChanged += _ =>
        {
            if (_ready)
            {
                RebuildCells();
            }
        };

        _ready = true;
        RebuildCells();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_loaded || string.IsNullOrEmpty(ComponentId))
        {
            return;
        }
        _loaded = true;
        _storeFile = IOPath.Combine(Paths.DataUser, $"qlgrid_{ComponentId}.json");
        try
        {
            if (File.Exists(_storeFile))
            {
                LoadStore();
            }
            else
            {
                _apps.AddRange(DefaultApps());
                Save();
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[QLG] 读取配置失败: {ex.Message}");
            _apps.Clear();
            _apps.AddRange(DefaultApps());
        }
        RebuildCells();
    }

    private void LoadStore()
    {
        if (_storeFile is null
            || JsonNode.Parse(File.ReadAllText(_storeFile)) is not JsonObject obj
            || obj["apps"] is not JsonArray arr)
        {
            return;
        }
        _apps.Clear();
        foreach (var n in arr)
        {
            if (n is not JsonObject o)
            {
                continue;
            }
            var path = (string?)o["path"] ?? "";
            if (path.Length == 0)
            {
                continue;
            }
            _apps.Add(new QuickLaunchItem
            {
                Name = (string?)o["name"] ?? "",
                NameKey = (string?)o["name_key"] ?? "",
                Path = path,
            });
        }
    }

    private static List<QuickLaunchItem> DefaultApps()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length == 0)
        {
            windows = @"C:\Windows";
        }
        return new List<QuickLaunchItem>
        {
            new()
            {
                NameKey = "custom_action.builtin_explorer",
                Path = IOPath.Combine(windows, "explorer.exe"),
            },
            new()
            {
                NameKey = "custom_action.builtin_downloads",
                Path = IOPath.Combine(home, "Downloads"),
            },
            new()
            {
                NameKey = "custom_action.builtin_documents",
                Path = IOPath.Combine(home, "Documents"),
            },
            new()
            {
                NameKey = "custom_action.builtin_notepad",
                Path = IOPath.Combine(windows, "notepad.exe"),
            },
        };
    }

    private void Save()
    {
        try
        {
            if (_storeFile is null)
            {
                return;
            }
            Directory.CreateDirectory(Paths.DataUser);
            var arr = new JsonArray();
            foreach (var a in _apps)
            {
                arr.Add(new JsonObject
                {
                    ["name"] = a.Name,
                    ["name_key"] = a.NameKey,
                    ["path"] = a.Path,
                });
            }
            File.WriteAllText(_storeFile, new JsonObject { ["apps"] = arr }.ToJsonString());
        }
        catch (Exception e)
        {
            Log.Warning($"[QLG] 保存失败: {e.Message}");
        }
    }

    private void RebuildCells()
    {
        if (!_ready)
        {
            return;
        }
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _tiles.Children.Clear();
        _tileBorders.Clear();
        var iconSize = _apps.Count > 4 ? 32 : 48;
        foreach (var app in _apps)
        {
            _tiles.Children.Add(BuildTile(app, p, iconSize));
        }

        _scroll.IsVisible = _apps.Count > 0;
        _empty.IsVisible = _apps.Count == 0;
        if (_apps.Count == 0)
        {
            _editing = false;
            _empty.Child = BuildEmptyState(p);
        }

        _adder.Children.Clear();
        _adder.IsVisible = _editing && _apps.Count > 0 && _apps.Count < MaxApps;
        if (_adder.IsVisible)
        {
            _adder.Children.Add(MakeMiniButton(Tr("quick_launch_ii.add_app"), true, PickApp, p));
            _adder.Children.Add(MakeMiniButton(Tr("quick_launch_ii.add_folder"), false, PickFolder, p));
            _adder.Children.Add(new TextBlock
            {
                Text = $"{_apps.Count}/{MaxApps}",
                FontSize = 10,
                Foreground = CardInk.Faint(p),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
            });
        }

        AttachEditButton();
        UpdateEditChrome(p);
        UpdateMetrics();
    }

    private void AttachEditButton()
    {
        _tileArea.Children.Remove(_editButton);
        _footer.Children.Remove(_editButton);
        if (_apps.Count == 0)
        {
            _footer.IsVisible = false;
            return;
        }
        if (_editing)
        {
            Grid.SetColumn(_editButton, 1);
            _editButton.HorizontalAlignment = HorizontalAlignment.Right;
            _editButton.VerticalAlignment = VerticalAlignment.Center;
            _editButton.Margin = new Thickness(0);
            _footer.Children.Add(_editButton);
        }
        else
        {
            _editButton.HorizontalAlignment = HorizontalAlignment.Right;
            _editButton.VerticalAlignment = _apps.Count is > 4 and < 8
                ? VerticalAlignment.Bottom
                : VerticalAlignment.Top;
            _editButton.Margin = _apps.Count is > 4 and < 8
                ? new Thickness(0, 0, 0, 2)
                : new Thickness(0, 2, 0, 0);
            _tileArea.Children.Add(_editButton);
        }
        _footer.IsVisible = _editing;
    }

    private Border BuildTile(QuickLaunchItem app, WidgetSurfacePalette p, double iconSize)
    {
        var icon = new Border
        {
            Width = iconSize,
            Height = iconSize,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = BuildFallbackIcon(app.DisplayName, iconSize, p),
        };
        var bitmap = FileIcon.For(app.Path);
        if (bitmap is not null)
        {
            icon.Child = new Image
            {
                Source = bitmap,
                Width = iconSize,
                Height = iconSize,
                Stretch = Stretch.Uniform,
            };
        }

        var label = new TextBlock
        {
            Text = app.DisplayName,
            FontSize = _apps.Count > 4 ? 11 : 12.5,
            Foreground = CardInk.Fg(p),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 96,
            MaxLines = 2,
            LineHeight = 14,
        };
        var content = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        };
        content.Children.Add(icon);
        content.Children.Add(label);

        var tile = new Grid();
        tile.Children.Add(content);
        var missing = !File.Exists(app.Path) && !Directory.Exists(app.Path);
        if (_editing)
        {
            var removeLabel = new TextBlock
            {
                Text = "×",
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var remove = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(p.Card, 0.96),
                BorderBrush = new SolidColorBrush(p.Line, 0.7),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = removeLabel,
                Cursor = new Cursor(StandardCursorType.Hand),
                ZIndex = 5,
            };
            ToolTip.SetTip(remove, Tr("quick_launch.delete"));
            remove.PointerEntered += (_, _) =>
            {
                remove.Background = CardInk.Danger();
                removeLabel.Foreground = Brushes.White;
            };
            remove.PointerExited += (_, _) =>
            {
                remove.Background = new SolidColorBrush(p.Card, 0.96);
                removeLabel.Foreground = CardInk.Muted(p);
            };
            remove.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true,
                RoutingStrategies.Bubble);
            remove.PointerReleased += (_, e) =>
            {
                e.Handled = true;
                _apps.Remove(app);
                Save();
                RebuildCells();
            };
            removeLabel.Foreground = CardInk.Muted(p);
            tile.Children.Add(remove);
        }

        var normalBg = Brushes.Transparent;
        var hoverBg = new SolidColorBrush(p.Text, 0.055);
        var tileBorder = new Border
        {
            Margin = new Thickness(2, 2),
            Padding = new Thickness(3, 3),
            CornerRadius = new CornerRadius(10),
            Background = normalBg,
            Child = tile,
            Cursor = new Cursor(_editing ? StandardCursorType.Arrow : StandardCursorType.Hand),
        };
        if (missing)
        {
            tileBorder.Opacity = 0.45;
        }
        ToolTip.SetTip(tileBorder, missing
            ? Tr("quick_launch.path_not_exist", ("path", app.Path))
            : app.Path);
        tileBorder.PointerEntered += (_, _) => tileBorder.Background = hoverBg;
        tileBorder.PointerExited += (_, _) => tileBorder.Background = normalBg;
        tileBorder.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true,
            RoutingStrategies.Bubble);
        tileBorder.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            if (_editing || missing)
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warning($"[QLG] 启动失败: {ex.Message}");
            }
        };
        _tileBorders.Add(tileBorder);
        return tileBorder;
    }

    private Control BuildFallbackIcon(string name, double size, WidgetSurfacePalette p)
    {
        var letter = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.24),
            Background = CardAccent.Brush(0.16),
            Child = new TextBlock
            {
                Text = letter,
                FontSize = size * 0.42,
                FontWeight = FontWeight.SemiBold,
                Foreground = CardAccent.Brush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private Control BuildEmptyState(WidgetSurfacePalette p)
    {
        var empty = new CardEmptyState(
            FAIcons.Glyph(AppIcons.Add),
            Tr("quick_launch_ii.empty_title"),
            Tr("quick_launch_ii.empty_hint"));
        empty.AddAction(MakeMiniButton(Tr("quick_launch_ii.add_app"), true, PickApp, p));
        empty.AddAction(MakeMiniButton(Tr("quick_launch_ii.add_folder"), false, PickFolder, p));
        empty.Apply(p);
        return empty;
    }

    private Border MakeMiniButton(string text, bool accent, Action onClick, WidgetSurfacePalette p)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = accent ? CardAccent.Brush() : CardInk.Muted(p),
        };
        var background = accent ? CardAccent.Brush(0.12) : new SolidColorBrush(p.Text, 0.055);
        var hover = accent ? CardAccent.Brush(0.20) : new SolidColorBrush(p.Text, 0.10);
        var button = new Border
        {
            Height = 25,
            Padding = new Thickness(9, 0),
            CornerRadius = new CornerRadius(8),
            Background = background,
            Child = label,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.PointerEntered += (_, _) => button.Background = hover;
        button.PointerExited += (_, _) => button.Background = background;
        button.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true,
            RoutingStrategies.Bubble);
        button.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return button;
    }

    private void UpdateEditChrome(WidgetSurfacePalette p)
    {
        _editLabel.Text = _editing ? AppIcons.Check : AppIcons.Edit;
        _editLabel.FontSize = _editing ? 11 : 11.5;
        _editLabel.Foreground = _editing ? CardAccent.Brush() : CardInk.Muted(p);
        _editButton.Background = _editing
            ? CardAccent.Brush(0.14)
            : new SolidColorBrush(p.Text, 0.055);
        _editButton.Opacity = _editing ? 1 : _hover ? 0.9 : 0.38;
        ToolTip.SetTip(_editButton, Tr(_editing ? "custom_action.done" : "quick_launch.edit"));
    }

    private void UpdateMetrics()
    {
        if (!_ready || Bounds.Width <= 1)
        {
            return;
        }
        var avail = Math.Max(90, Bounds.Width - RootPadX * 2 - 2);
        var columns = Math.Clamp((int)(avail / 88.0), 2, 6);
        _tiles.ItemWidth = Math.Clamp(avail / columns, 68, 110);
        _tiles.ItemHeight = _apps.Count > 4 ? 72 : 92;
        _tiles.VerticalAlignment = VerticalAlignment.Center;
        _scroll.VerticalAlignment = _apps.Count > 4 ? VerticalAlignment.Stretch : VerticalAlignment.Center;
    }

    private async void PickApp()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
            {
                return;
            }
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    FileTypeFilter = new List<Avalonia.Platform.Storage.FilePickerFileType>
                    {
                        new("程序或快捷方式") { Patterns = new[] { "*.exe", "*.lnk" } },
                        new("所有文件") { Patterns = new[] { "*" } },
                    },
                });
            if (files is { Count: > 0 })
            {
                var path = files[0].Path.LocalPath;
                AddApp(new QuickLaunchItem
                {
                    Name = IOPath.GetFileNameWithoutExtension(path),
                    Path = path,
                });
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[QLG] 选择失败: {e.Message}");
        }
    }

    private async void PickFolder()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
            {
                return;
            }
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    AllowMultiple = false,
                });
            if (folders is { Count: > 0 })
            {
                var path = folders[0].Path.LocalPath;
                AddApp(new QuickLaunchItem
                {
                    Name = new DirectoryInfo(path).Name,
                    Path = path,
                });
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[QLG] 选择文件夹失败: {e.Message}");
        }
    }

    private void AddApp(QuickLaunchItem item)
    {
        if (_apps.Count >= MaxApps)
        {
            Log.Warning("[QLG] 已达到 8 个上限");
            return;
        }
        if (_apps.Any(a => string.Equals(a.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        _apps.Add(item);
        Save();
        RebuildCells();
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        if (!_ready)
        {
            return;
        }
        RebuildCells();
    }

    private static string Tr(string key) => AppUtils.Tr(key);
    private static string Tr(string key, params (string Name, object? Value)[] args) => AppUtils.Tr(key, args);
}

/// <summary>倒计时 / 秒表: 分段标签 + 大读数 + 直接输入 + 计次</summary>
public sealed class TimerCountdownWidget : WidgetCardBase
{
    private static readonly int[] PresetMinutes = { 1, 3, 5, 10, 15, 30, 60 };
    private const int DefaultDuration = 5 * 60;
    private const int MaxDuration = 23 * 3600 + 59 * 60 + 59;

    private readonly TimerPill _tabCountdown;
    private readonly TimerPill _tabStopwatch;
    private readonly Border _modeBar;
    private readonly Grid _pageHost = new();

    // 倒计时
    private readonly TextBlock _cdTime = new()
    {
        Text = "05:00",
        FontSize = 58,
        FontWeight = FontWeight.Light,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Cursor = new Cursor(StandardCursorType.Hand),
    };
    private readonly TextBox _cdEdit = new()
    {
        FontSize = 58,
        FontWeight = FontWeight.Light,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(8, 0, 8, 0),
        MinWidth = 120,
        MaxLength = 8,
        IsVisible = false,
        VerticalAlignment = VerticalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _cdHint = new()
    {
        FontSize = 10.5,
        IsVisible = false,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock _cdEnds = new()
    {
        FontSize = 11,
        IsVisible = false,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Grid _cdMeta = new()
    {
        MinHeight = 14,
        Margin = new Thickness(0, 2, 0, 0),
    };
    private readonly CapsuleBar _cdProgress = new()
    {
        Height = 6,
        Segments = 20,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Margin = new Thickness(0, 7, 0, 0),
        IsVisible = false,
    };
    private readonly Grid _presetGrid = new()
    {
        VerticalAlignment = VerticalAlignment.Bottom,
    };
    private readonly List<TimerPill> _presetPills = new();
    private readonly TimerPill _customPill;
    private readonly TimerPill _cdMain;
    private readonly TimerPill _cdReset;
    private readonly StackPanel _cdButtons;
    private readonly Grid _cdReadout;
    private readonly Grid _cdPage;
    private readonly Grid _root;
    private readonly StackPanel _swButtons;
    private bool _portrait;
    private int _presetColumns;

    private int _cdDuration = DefaultDuration;
    private int _cdRemaining = DefaultDuration;
    private DateTime _cdEndAt;
    private bool _cdRunning;
    private bool _cdPaused;
    private bool _cdDone;
    private bool _cdEditing;
    private int _lastCdSecond = -1;

    // 秒表
    private readonly TextBlock _swTime = new()
    {
        Text = "00:00",
        FontSize = 54,
        FontWeight = FontWeight.Light,
        VerticalAlignment = VerticalAlignment.Bottom,
    };
    private readonly TextBlock _swTenths = new()
    {
        Text = ".0",
        FontSize = 22,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(2, 0, 0, 5),
    };
    private readonly StackPanel _swReadout = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel _lapStack = new() { Spacing = 0 };
    private readonly TextBlock _lapEmpty = new()
    {
        FontSize = 10.5,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,
    };
    private readonly Grid _lapHost = new();
    private readonly ScrollViewer _lapScroll;
    private readonly List<TimeSpan> _laps = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly TimerPill _swMain;
    private readonly TimerPill _swSecond;
    private readonly Grid _swPage;

    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _countdownMode = true;
    private bool _ready;
    private TopLevel? _topLevel;

    public TimerCountdownWidget(ComponentDefinition definition) : base(definition)
    {
        _tabCountdown = new TimerPill(Tr("timer_countdown.countdown"), TimerPill.PillKind.Tab, 26, 12, 11);
        _tabStopwatch = new TimerPill(Tr("timer_countdown.stopwatch"), TimerPill.PillKind.Tab, 26, 12, 11);
        _tabCountdown.DarkProvider = () => CardIsDark;
        _tabStopwatch.DarkProvider = () => CardIsDark;
        _tabCountdown.Clicked += () => SwitchMode(true);
        _tabStopwatch.Clicked += () => SwitchMode(false);

        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
        };
        tabs.Children.Add(_tabCountdown);
        tabs.Children.Add(_tabStopwatch);
        _modeBar = new Border
        {
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(13),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = tabs,
        };

        // ── 倒计时页 ──
        _cdTime.PointerPressed += (_, e) => e.Handled = true;
        _cdTime.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            BeginCountdownEdit();
        };
        _cdEdit.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true,
            RoutingStrategies.Bubble);
        _cdEdit.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitCountdownEdit(true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CommitCountdownEdit(false);
            }
        };
        _cdEdit.LostFocus += (_, _) =>
        {
            if (_cdEditing)
            {
                CommitCountdownEdit(true, leaving: true);
            }
        };
        _cdReadout = new Grid
        {
            MinHeight = 64,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        _cdReadout.Children.Add(_cdTime);
        _cdReadout.Children.Add(_cdEdit);
        _cdMeta.Children.Add(_cdEnds);
        _cdMeta.Children.Add(_cdHint);

        foreach (var minutes in PresetMinutes)
        {
            var pill = new TimerPill(PresetLabel(minutes), TimerPill.PillKind.Secondary, 25, 11, 6)
            {
                DarkProvider = () => CardIsDark,
            };
            var value = minutes * 60;
            pill.Clicked += () =>
            {
                if (!_cdRunning && !_cdPaused && !_cdDone)
                {
                    SetCountdownDuration(value);
                }
            };
            _presetPills.Add(pill);
            _presetGrid.Children.Add(pill);
        }

        _customPill = new TimerPill(Tr("timer_countdown.custom"), TimerPill.PillKind.Secondary, 25, 11, 6)
        {
            DarkProvider = () => CardIsDark,
        };
        _customPill.Clicked += () =>
        {
            if (!_cdRunning && !_cdPaused && !_cdDone)
            {
                BeginCountdownEdit();
            }
        };
        _presetGrid.Children.Add(_customPill);
        ConfigurePresetGrid(false);

        _cdMain = new TimerPill(Tr("timer_countdown.start"), TimerPill.PillKind.Primary, 34, 13, 16)
        {
            DarkProvider = () => CardIsDark,
        };
        _cdMain.Clicked += OnCountdownMain;
        _cdReset = new TimerPill(Tr("timer_countdown.reset"), TimerPill.PillKind.Secondary, 34, 12, 12)
        {
            DarkProvider = () => CardIsDark,
            IsVisible = false,
        };
        _cdReset.Clicked += OnCountdownReset;
        _cdButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 3, 0, 0),
        };
        _cdButtons.Children.Add(_cdMain);
        _cdButtons.Children.Add(_cdReset);

        _cdPage = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto,Auto,Auto,Auto"),
            RowSpacing = 2,
            ColumnSpacing = 12,
        };
        _cdPage.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        _cdPage.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Auto));
        Grid.SetRow(_cdReadout, 0);
        Grid.SetRow(_cdProgress, 1);
        Grid.SetRow(_cdMeta, 2);
        Grid.SetRow(_cdButtons, 3);
        Grid.SetRow(_presetGrid, 0);
        Grid.SetColumn(_presetGrid, 1);
        Grid.SetRowSpan(_presetGrid, 4);
        _cdPage.Children.Add(_cdReadout);
        _cdPage.Children.Add(_cdProgress);
        _cdPage.Children.Add(_cdMeta);
        _cdPage.Children.Add(_presetGrid);
        _cdPage.Children.Add(_cdButtons);

        // ── 秒表页 ──
        _swReadout.Children.Add(_swTime);
        _swReadout.Children.Add(_swTenths);
        _lapScroll = new ScrollViewer
        {
            Content = _lapStack,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Margin = new Thickness(0, 3, 0, 0),
        };
        _lapHost.Children.Add(_lapScroll);
        _lapHost.Children.Add(_lapEmpty);
        _swMain = new TimerPill(Tr("timer_countdown.start"), TimerPill.PillKind.Primary, 34, 13, 16)
        {
            DarkProvider = () => CardIsDark,
        };
        _swMain.Clicked += OnStopwatchMain;
        _swSecond = new TimerPill(Tr("timer_countdown.lap"), TimerPill.PillKind.Secondary, 34, 12, 12)
        {
            DarkProvider = () => CardIsDark,
            IsVisible = false,
        };
        _swSecond.Clicked += OnStopwatchSecond;
        _swButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 3, 0, 0),
        };
        _swButtons.Children.Add(_swMain);
        _swButtons.Children.Add(_swSecond);
        _swPage = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto,0"),
            RowSpacing = 2,
            ColumnSpacing = 12,
        };
        _swPage.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        _swPage.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Auto));
        Grid.SetRow(_swReadout, 0);
        Grid.SetColumn(_swReadout, 0);
        Grid.SetRow(_swButtons, 1);
        Grid.SetColumn(_swButtons, 0);
        Grid.SetRow(_lapHost, 0);
        Grid.SetColumn(_lapHost, 1);
        Grid.SetRowSpan(_lapHost, 2);
        _swPage.Children.Add(_swReadout);
        _swPage.Children.Add(_lapHost);
        _swPage.Children.Add(_swButtons);

        _pageHost.Children.Add(_cdPage);
        _pageHost.Children.Add(_swPage);

        _root = new Grid
        {
            Margin = new Thickness(18, 14, 18, 14),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            RowSpacing = 6,
        };
        Grid.SetRow(_modeBar, 0);
        Grid.SetRow(_pageHost, 1);
        _root.Children.Add(_modeBar);
        _root.Children.Add(_pageHost);
        Card.Child = _root;
        Content = Card;

        _ticker.Tick += (_, _) => Tick();
        SizeChanged += (_, _) => Relayout();
        Config.ThemeColor.ValueChanged += _ =>
        {
            if (_ready)
            {
                ApplyCardStyle();
            }
        };

        _ready = true;
        ApplyCardStyle();
        UpdateCountdownChrome();
        UpdateStopwatchDisplay();
        UpdateStopwatchChrome();
        RebuildLaps();
        SwitchMode(true);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureTicker();
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(PointerPressedEvent, OnTopLevelPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _ticker.Stop();
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(PointerPressedEvent, OnTopLevelPointerPressed);
            _topLevel = null;
        }
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_cdEditing)
        {
            return;
        }
        if (IsWithin(_cdEdit, e.Source) || IsWithin(_cdMain, e.Source) || IsWithin(_cdReset, e.Source))
        {
            return;
        }
        CommitCountdownEdit(true);
    }

    private static bool IsWithin(Visual ancestor, object? source)
    {
        var v = source as Visual;
        while (v is not null)
        {
            if (ReferenceEquals(v, ancestor))
            {
                return true;
            }
            v = v.GetVisualParent();
        }
        return false;
    }

    private static string PresetLabel(int minutes) => minutes >= 60
        ? Tr("timer_countdown.preset_hour")
        : Tr("timer_countdown.preset_min", ("n", minutes));

    private void SwitchMode(bool countdown)
    {
        if (_cdEditing)
        {
            CommitCountdownEdit(false);
        }
        _countdownMode = countdown;
        _cdPage.IsVisible = countdown;
        _swPage.IsVisible = !countdown;
        UpdateTabs();
        Relayout();
    }

    private void UpdateTabs()
    {
        _tabCountdown.Active = _countdownMode;
        _tabStopwatch.Active = !_countdownMode;
        _tabCountdown.ApplyTheme();
        _tabStopwatch.ApplyTheme();
    }

    private void OnCountdownMain()
    {
        if (_cdEditing)
        {
            CommitCountdownEdit(true);
            return;
        }
        if (_cdDone)
        {
            ResetCountdown();
            return;
        }
        if (_cdRunning)
        {
            PauseCountdown();
            return;
        }
        if (_cdPaused)
        {
            _cdRunning = true;
            _cdPaused = false;
            _cdEndAt = DateTime.Now.AddSeconds(_cdRemaining);
        }
        else
        {
            _cdRemaining = _cdDuration;
            _cdDone = false;
            _cdRunning = true;
            _cdPaused = false;
            _cdEndAt = DateTime.Now.AddSeconds(_cdRemaining);
        }
        _lastCdSecond = -1;
        _cdProgress.Value = _cdRemaining / (double)Math.Max(1, _cdDuration);
        EnsureTicker();
        UpdateCountdownDisplay();
        UpdateCountdownChrome();
    }

    private void PauseCountdown()
    {
        _cdRemaining = CountdownLeft();
        _cdRunning = false;
        _cdPaused = true;
        _cdProgress.Value = _cdRemaining / (double)Math.Max(1, _cdDuration);
        if (!_stopwatch.IsRunning)
        {
            _ticker.Stop();
        }
        UpdateCountdownChrome();
    }

    private void OnCountdownReset()
    {
        if (_cdEditing)
        {
            CommitCountdownEdit(false);
            return;
        }
        ResetCountdown();
    }

    private void ResetCountdown()
    {
        _cdRunning = false;
        _cdPaused = false;
        _cdDone = false;
        _cdRemaining = _cdDuration;
        _lastCdSecond = -1;
        _cdEditing = false;
        _cdEdit.IsVisible = false;
        _cdTime.IsVisible = true;
        _cdHint.IsVisible = false;
        _cdProgress.Value = _cdRemaining / (double)Math.Max(1, _cdDuration);
        if (!_stopwatch.IsRunning)
        {
            _ticker.Stop();
        }
        UpdateCountdownDisplay();
        UpdateCountdownChrome();
    }

    private void SetCountdownDuration(int seconds)
    {
        _cdDuration = Math.Clamp(seconds, 1, MaxDuration);
        _cdRemaining = _cdDuration;
        _cdDone = false;
        _cdRunning = false;
        _cdPaused = false;
        _lastCdSecond = -1;
        _cdProgress.Value = 1;
        UpdateCountdownDisplay();
        UpdateCountdownChrome();
    }

    private int CountdownLeft() => _cdRunning
        ? Math.Max(0, (int)Math.Ceiling((_cdEndAt - DateTime.Now).TotalSeconds))
        : _cdRemaining;

    private void Tick()
    {
        if (_cdRunning)
        {
            var left = CountdownLeft();
            if (left <= 0)
            {
                CompleteCountdown();
            }
            else
            {
                _cdRemaining = left;
                if (left != _lastCdSecond)
                {
                    _lastCdSecond = left;
                    UpdateCountdownDisplay();
                }
                _cdProgress.Value = left / (double)Math.Max(1, _cdDuration);
            }
        }
        if (_stopwatch.IsRunning)
        {
            UpdateStopwatchDisplay();
        }
        if (!_cdRunning && !_stopwatch.IsRunning)
        {
            _ticker.Stop();
        }
    }

    private void CompleteCountdown()
    {
        _cdRunning = false;
        _cdPaused = false;
        _cdDone = true;
        _cdRemaining = 0;
        _cdProgress.Value = 0;
        _lastCdSecond = 0;
        Log.Info("[TIMER] 倒计时结束");
        if (!_stopwatch.IsRunning)
        {
            _ticker.Stop();
        }
        UpdateCountdownDisplay();
        UpdateCountdownChrome();
    }

    private void UpdateCountdownDisplay()
    {
        if (_cdEditing)
        {
            return;
        }
        _cdTime.Text = _cdDone ? Tr("timer_countdown.time_up") : FormatClock(_cdRemaining);
        Relayout();
    }

    private void UpdateCountdownChrome()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var active = _cdRunning || _cdPaused;
        _cdMain.Text = _cdEditing
            ? Tr("timer_countdown.confirm")
            : _cdDone
                ? Tr("timer_countdown.ok")
                : _cdRunning
                    ? Tr("timer_countdown.pause")
                    : _cdPaused
                        ? Tr("timer_countdown.resume")
                        : Tr("timer_countdown.start");
        _cdReset.Text = _cdEditing ? Tr("timer_countdown.cancel") : Tr("timer_countdown.reset");
        _cdReset.IsVisible = active || _cdEditing;
        _cdProgress.IsVisible = active;
        _cdEnds.IsVisible = !_cdDone;
        _presetGrid.Opacity = active || _cdEditing ? 0.5 : 1;
        _cdTime.Foreground = _cdDone ? CardAccent.Brush() : CardInk.Fg(p);
        _cdTime.Cursor = new Cursor(active || _cdDone ? StandardCursorType.Arrow : StandardCursorType.Hand);
        if (_cdEditing)
        {
            _cdEnds.Text = Tr("timer_countdown.edit_confirm_hint");
        }
        else if (_cdRunning)
        {
            _cdEnds.Text = $"{_cdEndAt:HH:mm} {Tr("timer_countdown.ends_at")}";
        }
        else if (_cdPaused)
        {
            _cdEnds.Text = Tr("timer_countdown.paused");
        }
        else
        {
            _cdEnds.Text = Tr("timer_countdown.edit_time");
        }
        foreach (var pill in _presetPills)
        {
            var minutes = PresetMinutes[_presetPills.IndexOf(pill)];
            pill.Active = minutes * 60 == _cdDuration;
            pill.ApplyTheme();
        }
        _customPill.ApplyTheme();
        if (!_cdEditing)
        {
            _cdTime.Text = _cdDone ? Tr("timer_countdown.time_up") : FormatClock(_cdRemaining);
        }
        Relayout();
    }

    private void BeginCountdownEdit()
    {
        if (_cdRunning || _cdPaused || _cdDone || _cdEditing)
        {
            return;
        }
        _cdEditing = true;
        _cdHint.IsVisible = false;
        _cdEdit.Text = FormatClock(_cdDuration);
        _cdEdit.IsVisible = true;
        _cdTime.IsVisible = false;
        UpdateCountdownChrome();
        _cdEdit.Focus();
        _cdEdit.SelectAll();
    }

    private void CommitCountdownEdit(bool apply, bool leaving = false)
    {
        if (!_cdEditing)
        {
            return;
        }
        if (!apply)
        {
            EndCountdownEdit();
            return;
        }
        var value = ParseDuration(_cdEdit.Text, out var tooLong);
        if (value is null)
        {
            if (leaving)
            {
                EndCountdownEdit();
                return;
            }
            _cdHint.Text = tooLong
                ? Tr("timer_countdown.max_hint")
                : Tr("timer_countdown.edit_hint");
            _cdHint.IsVisible = true;
            _cdEdit.Focus();
            _cdEdit.SelectAll();
            return;
        }
        EndCountdownEdit();
        SetCountdownDuration(value.Value);
    }

    private void EndCountdownEdit()
    {
        _cdEditing = false;
        _cdEdit.IsVisible = false;
        _cdTime.IsVisible = true;
        _cdHint.IsVisible = false;
        UpdateCountdownChrome();
    }

    private static int? ParseDuration(string? text, out bool tooLong)
    {
        tooLong = false;
        var value = (text ?? "").Trim().Replace('：', ':').Replace('.', ':');
        if (value.Length == 0)
        {
            return null;
        }
        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 3 || parts.Any(p => p.Length == 0 || !p.All(char.IsDigit)))
        {
            return null;
        }
        var nums = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], out nums[i]))
            {
                return null;
            }
        }
        if (parts.Length > 1 && nums.Skip(1).Any(n => n >= 60))
        {
            return null;
        }
        var seconds = parts.Length switch
        {
            1 => nums[0] * 60,
            2 => nums[0] * 60 + nums[1],
            _ => nums[0] * 3600 + nums[1] * 60 + nums[2],
        };
        if (seconds <= 0)
        {
            return null;
        }
        if (seconds > MaxDuration)
        {
            tooLong = true;
            return null;
        }
        return (int)seconds;
    }

    private void OnStopwatchMain()
    {
        if (_stopwatch.IsRunning)
        {
            _stopwatch.Stop();
        }
        else
        {
            _stopwatch.Start();
        }
        EnsureTicker();
        UpdateStopwatchDisplay();
        UpdateStopwatchChrome();
    }

    private void OnStopwatchSecond()
    {
        if (_stopwatch.IsRunning)
        {
            _laps.Add(_stopwatch.Elapsed);
            RebuildLaps();
        }
        else
        {
            ResetStopwatch();
        }
    }

    private void ResetStopwatch()
    {
        _stopwatch.Reset();
        _laps.Clear();
        if (!_cdRunning)
        {
            _ticker.Stop();
        }
        UpdateStopwatchDisplay();
        UpdateStopwatchChrome();
        RebuildLaps();
    }

    private void UpdateStopwatchDisplay()
    {
        var elapsed = _stopwatch.Elapsed;
        var (main, tenths) = FormatStopwatch(elapsed);
        _swTime.Text = main;
        _swTenths.Text = tenths;
        Relayout();
    }

    private void UpdateStopwatchChrome()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _swMain.Text = _stopwatch.IsRunning
            ? Tr("timer_countdown.pause")
            : _stopwatch.Elapsed > TimeSpan.Zero
                ? Tr("timer_countdown.resume")
                : Tr("timer_countdown.start");
        _swSecond.Text = _stopwatch.IsRunning
            ? Tr("timer_countdown.lap")
            : Tr("timer_countdown.reset");
        _swSecond.IsVisible = _stopwatch.IsRunning || _stopwatch.Elapsed > TimeSpan.Zero;
        _swTime.Foreground = CardInk.Fg(p);
        _swTenths.Foreground = CardInk.Muted(p);
        _lapEmpty.Foreground = CardInk.Faint(p);
        // 按钮显隐会改变主按钮的剩余宽度，必须立即重排，避免先拉满再缩回
        Relayout();
    }

    private void RebuildLaps()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _lapStack.Children.Clear();
        _lapEmpty.IsVisible = _laps.Count == 0;
        if (_laps.Count == 0)
        {
            return;
        }

        for (var i = _laps.Count - 1; i >= 0; i--)
        {
            _lapStack.Children.Add(MakeLapRow(
                Tr("timer_countdown.lap_n", ("n", i + 1)),
                FormatStopwatch(_laps[i]),
                current: i == _laps.Count - 1,
                color: i == _laps.Count - 1 ? CardAccent.Brush() : null, p, out _));
        }
    }

    private Border MakeLapRow(string label, (string Main, string Tenths) time, bool current,
        IBrush? color, WidgetSurfacePalette p, out TextBlock? timeLabel)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = current ? FontWeight.Medium : FontWeight.Normal,
            Foreground = current ? CardInk.Fg(p) : CardInk.Muted(p),
            VerticalAlignment = VerticalAlignment.Center,
        };
        timeLabel = new TextBlock
        {
            Text = time.Main + time.Tenths,
            FontSize = 11,
            FontWeight = current ? FontWeight.Medium : FontWeight.Normal,
            Foreground = color ?? (current ? CardInk.Fg(p) : CardInk.Muted(p)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto"),
        };
        Grid.SetColumn(timeLabel, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(timeLabel);
        return new Border
        {
            Padding = new Thickness(0, 3),
            BorderBrush = new SolidColorBrush(p.Line, 0.32),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row,
        };
    }

    private void EnsureTicker()
    {
        if (_cdRunning || _stopwatch.IsRunning)
        {
            if (!_ticker.IsEnabled)
            {
                _ticker.Start();
            }
        }
    }

    private static string FormatClock(int seconds)
    {
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
    }

    private static (string Main, string Tenths) FormatStopwatch(TimeSpan elapsed)
    {
        var totalSeconds = (long)Math.Floor(elapsed.TotalSeconds);
        var h = totalSeconds / 3600;
        var m = totalSeconds % 3600 / 60;
        var s = totalSeconds % 60;
        var main = h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
        return (main, $".{elapsed.Milliseconds / 100}");
    }

    private void Relayout()
    {
        if (!_ready || Bounds.Width <= 1)
        {
            return;
        }
        var portrait = Bounds.Height >= Bounds.Width * 1.15;
        var padX = portrait ? 14 : 16;
        var width = Math.Max(80, Bounds.Width - padX * 2);
        var rightWidth = portrait ? 0 : Math.Clamp(width * 0.37, 132, 156);
        var leftWidth = Math.Max(96, width - (portrait ? 0 : rightWidth + 12));
        var lapWidth = portrait ? 0 : Math.Clamp(width * 0.45, 148, 190);
        var swLeftWidth = Math.Max(96, width - (portrait ? 0 : lapWidth + 12));
        if (_portrait != portrait)
        {
            _portrait = portrait;
            _root.Margin = new Thickness(padX, 14, padX, 14);
            _cdPage.RowSpacing = portrait ? 4 : 2;
            _swPage.RowSpacing = portrait ? 4 : 2;
            ConfigurePresetGrid(portrait);
            PlaceCountdownPage(portrait);
            PlaceStopwatchPage(portrait);
        }
        _cdPage.ColumnDefinitions[1].Width = new GridLength(portrait ? 0 : rightWidth);
        _cdPage.ColumnSpacing = portrait ? 0 : 12;
        _cdPage.RowDefinitions[4].Height = portrait ? GridLength.Auto : new GridLength(0);
        _swPage.ColumnDefinitions[1].Width = new GridLength(lapWidth);
        _swPage.ColumnSpacing = portrait ? 0 : 12;
        _swPage.RowDefinitions[0].Height = portrait ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        _swPage.RowDefinitions[1].Height = portrait ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        _swPage.RowDefinitions[2].Height = portrait ? GridLength.Auto : new GridLength(0);

        var cdPrefer = _cdDone
            ? 42
            : Math.Clamp(leftWidth * (portrait ? 0.36 : 0.3), 42, 58);
        _cdTime.FontSize = CardText.Fit(_cdTime.Text, _cdTime.FontFamily, FontWeight.Light,
            leftWidth, cdPrefer, 26, 70);
        _cdEdit.FontSize = _cdTime.FontSize;
        _cdEdit.Width = leftWidth;
        var swPrefer = portrait ? Math.Clamp(width * 0.32, 38, 54) : 58;
        _swTime.FontSize = CardText.Fit(_swTime.Text, _swTime.FontFamily, FontWeight.Light,
            swLeftWidth - 30, swPrefer, 26, 62);
        _swTenths.FontSize = Math.Max(14, _swTime.FontSize * 0.42);

        SetPillHeight(_tabCountdown, portrait ? 30 : 26);
        SetPillHeight(_tabStopwatch, portrait ? 30 : 26);
        SetPillHeight(_cdMain, portrait ? 40 : 34);
        SetPillHeight(_cdReset, portrait ? 40 : 34);
        SetPillHeight(_swMain, portrait ? 40 : 34);
        SetPillHeight(_swSecond, portrait ? 40 : 34);
        foreach (var pill in _presetPills)
        {
            SetPillHeight(pill, portrait ? 34 : 30);
        }
        SetPillHeight(_customPill, portrait ? 34 : 30);

        _modeBar.HorizontalAlignment = portrait ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        var tabWidth = (width - 6) / 2;
        _tabCountdown.Width = portrait ? tabWidth : double.NaN;
        _tabStopwatch.Width = portrait ? tabWidth : double.NaN;
        FitButtonWidths(_cdMain, _cdReset, leftWidth);
        FitButtonWidths(_swMain, _swSecond, swLeftWidth);
        _presetGrid.IsVisible = true;
    }

    private void PlaceStopwatchPage(bool portrait)
    {
        Grid.SetColumn(_swReadout, 0);
        Grid.SetRow(_swReadout, 0);
        Grid.SetColumn(_swButtons, 0);
        Grid.SetRow(_swButtons, portrait ? 2 : 1);
        Grid.SetColumn(_lapHost, 0);
        Grid.SetRow(_lapHost, 1);
        foreach (var control in new Control[] { _swReadout, _swButtons, _lapHost })
        {
            Grid.SetColumnSpan(control, portrait ? 2 : 1);
            Grid.SetRowSpan(control, 1);
        }
        if (portrait)
        {
            return;
        }
        Grid.SetColumn(_lapHost, 1);
        Grid.SetRow(_lapHost, 0);
        Grid.SetRowSpan(_lapHost, 2);
    }

    private void PlaceCountdownPage(bool portrait)
    {
        Grid.SetColumn(_cdReadout, 0);
        Grid.SetRow(_cdReadout, 0);
        Grid.SetColumn(_cdProgress, 0);
        Grid.SetRow(_cdProgress, 1);
        Grid.SetColumn(_cdMeta, 0);
        Grid.SetRow(_cdMeta, 2);
        Grid.SetColumn(_cdButtons, 0);
        Grid.SetRow(_cdButtons, portrait ? 4 : 3);
        Grid.SetColumn(_presetGrid, portrait ? 0 : 1);
        Grid.SetRow(_presetGrid, portrait ? 3 : 0);
        foreach (var control in new Control[] { _cdReadout, _cdProgress, _cdMeta, _cdButtons })
        {
            Grid.SetColumnSpan(control, portrait ? 2 : 1);
            Grid.SetRowSpan(control, 1);
        }
        Grid.SetColumnSpan(_presetGrid, portrait ? 2 : 1);
        Grid.SetRowSpan(_presetGrid, portrait ? 1 : 4);
    }

    private static void SetPillHeight(TimerPill pill, double height)
    {
        if (Math.Abs(pill.Height - height) < 0.1)
        {
            return;
        }
        pill.Height = height;
        pill.CornerRadius = new CornerRadius(height / 2);
    }

    private static void FitButtonWidths(TimerPill main, TimerPill second, double width)
    {
        if (!second.IsVisible)
        {
            main.Width = width;
            second.Width = double.NaN;
            return;
        }
        main.Width = (width - 6) / 2;
        second.Width = width - main.Width - 6;
    }

    private void ConfigurePresetGrid(bool portrait)
    {
        const int columns = 2;
        const int rows = 4;
        if (_presetColumns == columns && _presetGrid.RowDefinitions.Count == rows)
        {
            _presetGrid.ColumnSpacing = portrait ? 6 : 4;
            _presetGrid.RowSpacing = portrait ? 6 : 4;
            return;
        }
        _presetColumns = columns;
        _presetGrid.ColumnDefinitions.Clear();
        _presetGrid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++)
        {
            _presetGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        }
        for (var i = 0; i < rows; i++)
        {
            _presetGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        _presetGrid.ColumnSpacing = portrait ? 6 : 4;
        _presetGrid.RowSpacing = portrait ? 6 : 4;
        _presetGrid.VerticalAlignment = VerticalAlignment.Center;
        _customPill.IsVisible = true;
        var index = 0;
        foreach (var pill in _presetPills)
        {
            pill.Margin = default;
            Grid.SetColumn(pill, index % columns);
            Grid.SetRow(pill, index / columns);
            index++;
        }
        _customPill.Margin = default;
        Grid.SetColumn(_customPill, index % columns);
        Grid.SetRow(_customPill, index / columns);
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        if (!_ready)
        {
            return;
        }
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _modeBar.Background = new SolidColorBrush(p.Text, 0.045);
        _cdTime.Foreground = _cdDone ? CardAccent.Brush() : CardInk.Fg(p);
        _cdEdit.Foreground = CardInk.Fg(p);
        _cdEdit.Background = new SolidColorBrush(p.Text, 0.06);
        _cdEdit.BorderBrush = new SolidColorBrush(p.Text, 0.14);
        _cdEdit.SelectionBrush = CardAccent.Brush(0.28);
        _cdEdit.SelectionForegroundBrush = CardInk.Fg(p);
        _cdEdit.CaretBrush = CardAccent.Brush();
        _cdHint.Foreground = CardInk.Danger();
        _cdEnds.Foreground = CardInk.Muted(p);
        _lapEmpty.Foreground = CardInk.Faint(p);
        _cdProgress.DarkProvider = () => CardIsDark;
        _cdProgress.InvalidateVisual();
        foreach (var pill in _presetPills)
        {
            pill.ApplyTheme();
        }
        _customPill.ApplyTheme();
        _tabCountdown.ApplyTheme();
        _tabStopwatch.ApplyTheme();
        _cdMain.ApplyTheme();
        _cdReset.ApplyTheme();
        _swMain.ApplyTheme();
        _swSecond.ApplyTheme();
        _swTime.Foreground = CardInk.Fg(p);
        _swTenths.Foreground = CardInk.Muted(p);
        UpdateTabs();
        RebuildLaps();
        Relayout();
    }

    private static string Tr(string key) => AppUtils.Tr(key);
    private static string Tr(string key, params (string Name, object? Value)[] args) => AppUtils.Tr(key, args);
}

/// <summary>计时器按钮/分段标签 统一圆角胶囊</summary>
internal sealed class TimerPill : Border
{
    public enum PillKind
    {
        Primary,
        Secondary,
        Tab,
    }

    private readonly TextBlock _label;
    private readonly PillKind _kind;
    private bool _hover;

    public Func<bool>? DarkProvider { get; set; }
    public bool Active { get; set; }
    public event Action? Clicked;

    public string Text
    {
        get => _label.Text ?? "";
        set => _label.Text = value;
    }

    public TimerPill(string text, PillKind kind = PillKind.Secondary, double height = 32,
        double fontSize = 13, double paddingX = 12)
    {
        _kind = kind;
        Height = height;
        CornerRadius = new CornerRadius(height / 2);
        Padding = new Thickness(paddingX, 0);
        Cursor = new Cursor(StandardCursorType.Hand);
        _label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _label;
        AddHandler(PointerPressedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble);
        PointerEntered += (_, _) =>
        {
            _hover = true;
            ApplyTheme();
        };
        PointerExited += (_, _) =>
        {
            _hover = false;
            ApplyTheme();
        };
        PointerCaptureLost += (_, _) =>
        {
            _hover = false;
            Opacity = 1;
            ApplyTheme();
        };
        PointerReleased += (_, e) =>
        {
            e.Handled = true;
            Opacity = 1;
            Clicked?.Invoke();
        };
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var dark = DarkProvider?.Invoke() ?? ThemeSense.IsDark(this);
        var p = WidgetSurfacePalette.Resolve(dark);
        switch (_kind)
        {
            case PillKind.Primary:
                Background = CardAccent.Brush();
                _label.Foreground = CardAccent.Ink();
                _label.FontWeight = FontWeight.SemiBold;
                Opacity = _hover ? 0.86 : 1;
                break;
            case PillKind.Tab:
                Background = Active
                    ? new SolidColorBrush(p.Text, 0.09)
                    : _hover
                        ? new SolidColorBrush(p.Text, 0.055)
                        : Brushes.Transparent;
                _label.Foreground = Active || _hover ? CardInk.Fg(p) : CardInk.Muted(p);
                _label.FontWeight = Active ? FontWeight.SemiBold : FontWeight.Normal;
                break;
            default:
                Background = Active
                    ? new SolidColorBrush(p.Text, 0.12)
                    : _hover
                        ? new SolidColorBrush(p.Text, 0.085)
                        : new SolidColorBrush(p.Text, 0.055);
                _label.Foreground = Active || _hover ? CardInk.Fg(p) : CardInk.Muted(p);
                _label.FontWeight = Active ? FontWeight.Medium : FontWeight.Normal;
                break;
        }
    }
}

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

/// <summary>性能监测</summary>
public sealed class PerformanceMonitorWidget : WidgetCardBase
{
    private readonly UsageChart _chart = new();
    private readonly TextBlock _cpuValue = new();
    private readonly TextBlock _ramValue = new();
    private readonly TextBlock _gpuValue = new();
    private readonly Border _cpuDot = MakeDot();
    private readonly Border _ramDot = MakeDot();
    private readonly Border _gpuDot = MakeDot();
    private readonly TextBlock _cpuLabel = new();
    private readonly TextBlock _ramLabel = new();
    private readonly TextBlock _gpuLabel = new();

    private static Border MakeDot() => new()
    {
        Width = 6,
        Height = 6,
        CornerRadius = new CornerRadius(3),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public PerformanceMonitorWidget(ComponentDefinition definition) : base(definition)
    {
        var header = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,*,Auto"),
            ColumnSpacing = 16,
        };
        var cpuCell = MakeMetricCell(_cpuDot, _cpuLabel, _cpuValue, "CPU");
        var ramCell = MakeMetricCell(_ramDot, _ramLabel, _ramValue, "RAM");
        var gpuCell = MakeMetricCell(_gpuDot, _gpuLabel, _gpuValue, "GPU");
        gpuCell.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(cpuCell, 0);
        Grid.SetColumn(ramCell, 1);
        Grid.SetColumn(gpuCell, 3);
        header.Children.Add(cpuCell);
        header.Children.Add(ramCell);
        header.Children.Add(gpuCell);

        _chart.DarkProvider = () => CardIsDark;
        _chart.VerticalAlignment = VerticalAlignment.Stretch;
        _chart.MinHeight = 24;

        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad, 14, WidgetSpace.Pad, 14),
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = 10,
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(_chart, 1);
        root.Children.Add(header);
        root.Children.Add(_chart);
        Card.Child = root;
        Content = Card;

        _cpuValue.Text = "--";
        _ramValue.Text = "--";
        _gpuValue.Text = "--";
        ApplyCardStyle();
        SystemSampler.PerfReady += OnPerf;
        SystemSampler.EnsureStarted();
    }

    private StackPanel MakeMetricCell(Border dot, TextBlock label, TextBlock value, string text)
    {
        label.Text = text;
        label.FontSize = WidgetType.Label;
        label.VerticalAlignment = VerticalAlignment.Center;
        value.FontSize = 17;
        value.FontWeight = FontWeight.Light;
        value.FontFamily = WidgetFonts.Num;
        value.VerticalAlignment = VerticalAlignment.Center;
        var cell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        cell.Children.Add(dot);
        cell.Children.Add(label);
        cell.Children.Add(value);
        return cell;
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _cpuDot.Background = CardAccent.Brush();
        _ramDot.Background = new SolidColorBrush(p.Text, 0.45);
        _gpuDot.Background = new SolidColorBrush(p.Text, 0.28);
        _cpuLabel.Foreground = CardInk.Muted(p);
        _ramLabel.Foreground = CardInk.Muted(p);
        _gpuLabel.Foreground = CardInk.Muted(p);
        _cpuValue.Foreground = CardInk.Fg(p);
        _ramValue.Foreground = CardInk.Fg(p);
        _gpuValue.Foreground = CardInk.Muted(p);
        _chart.InvalidateVisual();
    }

    private void OnPerf(double cpu, double ram, double gpu)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var p = WidgetSurfacePalette.Resolve(CardIsDark);
            _cpuValue.Text = $"{Math.Round(cpu)}%";
            _ramValue.Text = $"{Math.Round(ram)}%";
            _gpuValue.Text = gpu > 0 ? $"{Math.Round(gpu)}%" : "--";
            // 85% 以上只有一处警示色
            var hot = cpu >= 85;
            var warn = CardInk.Warn().Color;
            _cpuDot.Background = hot ? new SolidColorBrush(warn) : CardAccent.Brush();
            _cpuValue.Foreground = hot ? new SolidColorBrush(warn) : CardInk.Fg(p);
            _chart.Push(cpu, ram);
        });
    }
}

/// <summary>
/// 班级相册
/// 图片源 data/classphotos/album_<id>/ 5s 自动轮播 支持拖放图片
/// </summary>
public sealed class ClassAlbumWidget : WidgetCardBase
{
    private readonly Image _image = new()
    {
        Stretch = Stretch.UniformToFill,
    };
    private readonly List<string> _photos = new();
    private int _index;
    private readonly DispatcherTimer _autoTimer;
    private readonly string _photosDir;

    public ClassAlbumWidget(ComponentDefinition definition, bool vertical) : base(definition)
    {
        Card.Child = _image;
        Content = Card;
        ClipToBounds = true;


        var id = string.IsNullOrEmpty(definition.Id) ? "default" : definition.Id;
        _photosDir = IOPath.Combine(Paths.DataRoot, "classphotos", $"album_{id}");
        LoadPhotos();

        // 5s 自动轮播
        _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoTimer.Tick += (_, _) => FlipNext();
        _autoTimer.Start();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);
        ApplyCardStyle();
    }

    private void LoadPhotos()
    {
        _photos.Clear();
        try
        {
            Directory.CreateDirectory(_photosDir);
            var exts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            foreach (var f in Directory.EnumerateFiles(_photosDir))
            {
                if (exts.Contains(IOPath.GetExtension(f).ToLowerInvariant()))
                {
                    _photos.Add(f);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[ALBUM] 读取相册失败: {e.Message}");
        }
        ShowCurrent();
    }

    private void FlipNext()
    {
        if (_photos.Count == 0)
        {
            return;
        }
        _index = (_index + 1) % _photos.Count;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        try
        {
            _image.Source = _photos.Count > 0 ? new Bitmap(_photos[_index]) : null;
        }
        catch (Exception e)
        {
            Log.Debug($"[相册] 图片解码失败 留空: {e.Message}");
            _image.Source = null;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            foreach (var item in e.DataTransfer.Items)
            {
                if (item.TryGetFile() is not { } file)
                {
                    continue;
                }
                var ext = IOPath.GetExtension(file.Path.LocalPath).ToLowerInvariant();
                if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(ext))
                {
                    var dest = IOPath.Combine(_photosDir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}{ext}");
                    File.Copy(file.Path.LocalPath, dest, true);
                    _photos.Add(dest);
                }
            }
            ShowCurrent();
        }
        catch (Exception ex)
        {
            Log.Warning($"[ALBUM] 拖放失败: {ex.Message}");
        }
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        Card.CornerRadius = new CornerRadius(Math.Max(8, Config.ComponentCardRadius.Value));
        Card.ClipToBounds = true;
    }
}

// NativeWidgets.cs

/// <summary>原版模板颜色/字体常量与取值助手</summary>
public static class NativeTheme
{
    // 自绘控件文字统一用 App 字体(内嵌 HarmonyOS Sans) 勿用系统字体名 否则与主程序不一致
    public static readonly FontFamily AppFont = Common.AppFontFamily;
    public const string SerifFont = "Georgia, Times New Roman, serif";
    public const string MonoFont = "Consolas, Courier New, monospace";

    public static Color Rgba(int r, int g, int b, double a) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), (byte)r, (byte)g, (byte)b);

    public static Color Parse(string hex) => Color.Parse(hex);

    public static IBrush Brush(Color c) => new SolidColorBrush(c);

    // 原版 accent 缺省 #30c361
    public static Color Accent()
    {
        try
        {
            return Color.Parse(Config.ThemeColor.Value);
        }
        catch (Exception e)
        {
            Log.Debug($"[Native] 主题色解析失败 用默认绿: {e.Message}");
            return Color.Parse("#30c361");
        }
    }

    // 等宽间距日期 "2026.10.03"(原版 replace("-","."))
    public static string DotDate(string iso) => iso.Replace("-", ".");

    public static bool Dark(StyledElement e) => ThemeSense.IsDark(e);
}

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

/// <summary>
/// 简约月历
/// 头部(‹ 年月 › 22x22 圆角4 hover) + 分隔线 + 星期行(9px 高16) + 6x7 日网格(11px)
/// </summary>
public sealed class NativeMiniCalendarWidget : WidgetCardBase
{
    // 主题对 逐值取自模板 ThemePairs
    private static Color T(string key, bool dark) => dark ? key switch
    {
        "border" => NativeTheme.Rgba(255, 255, 255, 0.06),
        "ink" => NativeTheme.Parse("#d8d8dc"),
        "title" => NativeTheme.Parse("#ececee"),
        "weekday" => NativeTheme.Parse("#8a8a90"),
        "other" => NativeTheme.Parse("#55555a"),
        "hover" => NativeTheme.Rgba(255, 255, 255, 0.08),
        "btn" => NativeTheme.Parse("#a0a0a6"),
        "btnhover" => NativeTheme.Rgba(255, 255, 255, 0.10),
        // 今日/选中 走主题强调色 选中格用底色区分
        "today" => CardAccent.Value,
        "selected" => CardAccent.Value,
        _ => Colors.White,
    } : key switch
    {
        "border" => NativeTheme.Parse("#eeeeee"),
        "ink" => NativeTheme.Parse("#444444"),
        "title" => NativeTheme.Parse("#333333"),
        "weekday" => NativeTheme.Parse("#999999"),
        "other" => NativeTheme.Parse("#cccccc"),
        "hover" => NativeTheme.Parse("#f2f2f2"),
        "btn" => NativeTheme.Parse("#666666"),
        "btnhover" => NativeTheme.Parse("#f0f0f0"),
        "today" => CardAccent.Value,
        "selected" => CardAccent.Value,
        _ => Colors.Black,
    };

    private readonly TextBlock _title = new();
    private readonly UniformGrid _daysGrid = new() { Columns = 7, Rows = 6 };
    private readonly List<Border> _cells = new();
    private readonly List<TextBlock> _dayTexts = new();
    private readonly List<Ellipse> _dayDots = new();
    private readonly List<TextBlock> _weekdayTexts = new();
    private readonly Border _headerBorder = new();
    private readonly Border _prevBtn = new();
    private readonly Border _nextBtn = new();
    private int _year;
    private int _month;
    private int? _selectedDay;

    public NativeMiniCalendarWidget(ComponentDefinition definition) : base(definition)
    {
        var today = DateTime.Today;
        _year = today.Year;
        _month = today.Month - 1;

        BuildHeader();
        var weekdayRow = new UniformGrid { Columns = 7 };
        foreach (var w in new[] { "日", "一", "二", "三", "四", "五", "六" })
        {
            var tb = new TextBlock
            {
                Text = w,
                FontSize = 10,
                FontWeight = FontWeight.Medium,
                TextAlignment = TextAlignment.Center,
                Height = 16,
            };
            _weekdayTexts.Add(tb);
            weekdayRow.Children.Add(tb);
        }
        var weekdayPad = new Border { Padding = new Thickness(4, 2, 4, 0), Child = weekdayRow };
        var daysPad = new Border { Padding = new Thickness(4, 0, 4, 4), Child = _daysGrid };

        for (var i = 0; i < 42; i++)
        {
            var dot = new Ellipse
            {
                Width = 20,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false,
                IsHitTestVisible = false,
            };
            var dayText = new TextBlock
            {
                FontSize = 12.5,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var cell = new Border
            {
                CornerRadius = new CornerRadius(4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Grid { Children = { dot, dayText } },
            };
            cell.PointerEntered += (_, _) =>
            {
                if (cell.Tag is int) // 本月日期才 hover
                {
                    cell.Background = new SolidColorBrush(T("hover", IsDarkNow));
                }
            };
            cell.PointerExited += (_, _) => cell.Background = null;
            cell.PointerReleased += (_, _) =>
            {
                if (cell.Tag is int day)
                {
                    _selectedDay = day;
                    RenderMonth();
                }
            };
            _cells.Add(cell);
            _dayTexts.Add(dayText);
            _dayDots.Add(dot);
            _daysGrid.Children.Add(cell);
        }

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,*"),
        };
        Grid.SetRow(_headerBorder, 0);
        Grid.SetRow(weekdayPad, 1);
        Grid.SetRow(daysPad, 2);
        root.Children.Add(_headerBorder);
        root.Children.Add(weekdayPad);
        root.Children.Add(daysPad);

        Card.Child = root;
        Content = Card;
        ActualThemeVariantChanged += (_, _) => { ApplyPalette(); RenderMonth(); };
        Config.ThemeColor.ValueChanged += _ => RenderMonth();
        SizeChanged += (_, _) => RenderMonth();
        ApplyPalette();
        RenderMonth();
    }

    private bool IsDarkNow => CardIsDark;

    private void BuildHeader()
    {
        _prevBtn.Width = 22;
        _prevBtn.Height = 22;
        _prevBtn.CornerRadius = new CornerRadius(4);
        _prevBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _prevBtn.Child = new TextBlock { Text = "‹", FontSize = 13, TextAlignment = TextAlignment.Center };
        _prevBtn.PointerEntered += (_, _) => _prevBtn.Background = new SolidColorBrush(T("btnhover", IsDarkNow));
        _prevBtn.PointerExited += (_, _) => _prevBtn.Background = null;
        _prevBtn.PointerReleased += (_, _) => { _month--; if (_month < 0) { _month = 11; _year--; } _selectedDay = null; RenderMonth(); };

        _nextBtn.Width = 22;
        _nextBtn.Height = 22;
        _nextBtn.CornerRadius = new CornerRadius(4);
        _nextBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _nextBtn.Child = new TextBlock { Text = "›", FontSize = 13, TextAlignment = TextAlignment.Center };
        _nextBtn.PointerEntered += (_, _) => _nextBtn.Background = new SolidColorBrush(T("btnhover", IsDarkNow));
        _nextBtn.PointerExited += (_, _) => _nextBtn.Background = null;
        _nextBtn.PointerReleased += (_, _) => { _month++; if (_month > 11) { _month = 0; _year++; } _selectedDay = null; RenderMonth(); };

        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.FontSize = 13;
        _title.FontWeight = FontWeight.SemiBold;

        var header = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("22,*,22"),
        };
        Grid.SetColumn(_prevBtn, 0);
        Grid.SetColumn(_title, 1);
        Grid.SetColumn(_nextBtn, 2);
        header.Children.Add(_prevBtn);
        header.Children.Add(_title);
        header.Children.Add(_nextBtn);

        _headerBorder.Child = header;
        _headerBorder.Padding = new Thickness(6, 4);
        _headerBorder.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    private void ApplyPalette()
    {
        var dark = IsDarkNow;
        _title.Foreground = new SolidColorBrush(T("title", dark));
        _headerBorder.BorderBrush = new SolidColorBrush(T("border", dark));
        ((TextBlock)_prevBtn.Child!).Foreground = new SolidColorBrush(T("btn", dark));
        ((TextBlock)_nextBtn.Child!).Foreground = new SolidColorBrush(T("btn", dark));
        foreach (var tb in _weekdayTexts)
        {
            tb.Foreground = new SolidColorBrush(T("weekday", dark));
        }
    }

    private void RenderMonth()
    {
        _title.Text = $"{_year}年{_month + 1}月";
        var today = DateTime.Today;
        var startWeekday = (int)new DateTime(_year, _month + 1, 1).DayOfWeek;
        var daysInMonth = DateTime.DaysInMonth(_year, _month + 1);
        var weeks = (startWeekday + daysInMonth + 6) / 7;
        var daysInPrev = (_month == 0 ? new DateTime(_year - 1, 12, 31) : new DateTime(_year, _month, 1).AddDays(-1)).Day;
        var accent = CardAccent.Value;
        var cellW = _daysGrid.Bounds.Width > 1 ? _daysGrid.Bounds.Width / 7 : 28;
        var cellH = _daysGrid.Bounds.Height > 1 ? _daysGrid.Bounds.Height / weeks : 24;
        var dotSize = Math.Clamp(Math.Min(cellW, cellH) * 0.82, 16, 30);

        // 只使用实际周数 避免底部空行
        _daysGrid.Rows = weeks;
        _daysGrid.Children.Clear();
        for (var i = 0; i < weeks * 7; i++)
        {
            _daysGrid.Children.Add(_cells[i]);
        }

        for (var i = 0; i < weeks * 7; i++)
        {
            var cell = _cells[i];
            var tb = _dayTexts[i];
            var dot = _dayDots[i];
            cell.Tag = null;
            cell.Background = null;
            dot.IsVisible = false;
            string cls;
            int day;
            if (i < startWeekday)
            {
                day = daysInPrev - (startWeekday - 1 - i);
                cls = "other";
            }
            else if (i < startWeekday + daysInMonth)
            {
                day = i - startWeekday + 1;
                cls = _year == today.Year && _month == today.Month - 1 && day == today.Day ? "today" : "day";
                if (cls == "day" && _selectedDay == day)
                {
                    cls = "selected";
                }
                cell.Tag = day;
            }
            else
            {
                day = i - startWeekday - daysInMonth + 1;
                cls = "other";
            }
            tb.Text = day.ToString();
            tb.FontWeight = cls is "today" or "selected" ? FontWeight.SemiBold : FontWeight.Normal;
            tb.Foreground = new SolidColorBrush(cls switch
            {
                "other" => T("other", IsDarkNow),
                "today" => OnAccentColor(accent),
                "selected" => accent,
                _ => T("ink", IsDarkNow),
            });
            if (cls == "today")
            {
                dot.Width = dotSize;
                dot.Height = dotSize;
                dot.Fill = new SolidColorBrush(accent);
                dot.Stroke = null;
                dot.IsVisible = true;
            }
            else if (cls == "selected")
            {
                dot.Width = dotSize;
                dot.Height = dotSize;
                dot.Fill = null;
                dot.Stroke = new SolidColorBrush(accent);
                dot.StrokeThickness = 1.25;
                dot.IsVisible = true;
            }
        }
    }

    // 今日圆点上的日期用对比色 浅强调色配深字 深强调色配白字
    private static Color OnAccentColor(Color accent)
    {
        var luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
        return luminance > 0.58 ? Color.FromRgb(20, 20, 20) : Colors.White;
    }
}

/// <summary>
/// 黄历
/// 左列: 农历 label + 大号农历日期(按宽自适应) + 干支两行 + 节气(accent)
/// 右列: 宜/忌小徽标 + 词条全量换行(字号随可用空间收敛, 实在放不下才整词截断)
/// 竖虚线 + 行间横虚线; 右上角 mono 日期; 30 分钟刷新; 窄卡减信息
/// </summary>
public sealed class NativeAlmanacWidget : WidgetCardBase
{
    private static Color T(string key, bool dark) => dark ? key switch
    {
        "fg" => NativeTheme.Rgba(255, 255, 255, 0.87),
        "muted" => NativeTheme.Rgba(255, 255, 255, 0.62),
        "faint" => NativeTheme.Rgba(255, 255, 255, 0.38),
        "term_c" => NativeTheme.Rgba(255, 255, 255, 0.80),
        "yi" => NativeTheme.Parse("#e0554b"),
        "ji" => NativeTheme.Parse("#6f6f6f"),
        "div" => NativeTheme.Rgba(255, 255, 255, 0.18),
        "accent" => NativeTheme.Parse("#e08a80"),
        _ => Colors.White,
    } : key switch
    {
        "fg" => NativeTheme.Rgba(40, 40, 40, 0.88),
        "muted" => NativeTheme.Rgba(40, 40, 40, 0.62),
        "faint" => NativeTheme.Rgba(40, 40, 40, 0.40),
        "term_c" => NativeTheme.Rgba(40, 40, 40, 0.80),
        "yi" => NativeTheme.Parse("#cf3838"),
        "ji" => NativeTheme.Parse("#3c3c3c"),
        "div" => NativeTheme.Rgba(0, 0, 0, 0.15),
        "accent" => NativeTheme.Parse("#b8483d"),
        _ => Colors.Black,
    };

    private readonly TextBlock _sdate = new();
    private readonly TextBlock _label = new();
    private readonly TextBlock _lunar = new();
    private readonly TextBlock _gzYear = new();
    private readonly TextBlock _gzRest = new();
    private readonly TextBlock _term = new();
    private readonly TextBlock _good = new();
    private readonly TextBlock _bad = new();
    private Border _yiBadge = null!;
    private Border _jiBadge = null!;
    private readonly DashedRule _vdiv = new() { Vertical = true };
    private readonly DashedRule _hdiv = new();
    private readonly Grid _leftCol = new() { MaxWidth = 150 };
    private readonly Grid _main = new();
    private readonly StackPanel _rightCol = new() { VerticalAlignment = VerticalAlignment.Center };
    private string[]? _goodTerms;
    private string[]? _badTerms;

    public NativeAlmanacWidget(ComponentDefinition definition) : base(definition)
    {
        BuildLayout();
        SizeChanged += (_, _) => FitText();
        ActualThemeVariantChanged += (_, _) => Rebuild();
        // 原版 30 分钟定期刷新
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        timer.Tick += (_, _) => Rebuild();
        timer.Start();
        Rebuild();
    }

    private bool IsDarkNow => CardIsDark;

    private static Border MakeBadge(string glyph, string key, bool dark)
    {
        return new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(T(key, dark)),
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private void BuildLayout()
    {
        var dark = IsDarkNow;
        _sdate.FontFamily = new FontFamily(NativeTheme.MonoFont);
        _sdate.FontSize = 11;
        _sdate.LetterSpacing = 2;
        _sdate.VerticalAlignment = VerticalAlignment.Top;
        _sdate.HorizontalAlignment = HorizontalAlignment.Right;
        _sdate.Margin = new Thickness(0, 13, 16, 0);

        _label.Text = "农历";
        _label.FontSize = 11;
        _label.LetterSpacing = 3;
        _label.Margin = new Thickness(2, 0, 0, 3);
        _lunar.FontWeight = FontWeight.Light;
        _lunar.LetterSpacing = 1;
        _lunar.Margin = new Thickness(0, 0, 0, 9);
        _gzYear.FontSize = 13;
        _gzYear.LetterSpacing = 1;
        _gzYear.Margin = new Thickness(0, 0, 0, 4);
        _gzRest.FontSize = 13;
        _gzRest.LetterSpacing = 1;
        _term.FontSize = 12;
        _term.LetterSpacing = 2;
        _term.Margin = new Thickness(0, 7, 0, 0);

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(_label);
        left.Children.Add(_lunar);
        left.Children.Add(_gzYear);
        left.Children.Add(_gzRest);
        left.Children.Add(_term);
        _leftCol.Children.Add(left);

        _good.TextWrapping = TextWrapping.Wrap;
        _bad.TextWrapping = TextWrapping.Wrap;

        _yiBadge = MakeBadge("宜", "yi", dark);
        _jiBadge = MakeBadge("忌", "ji", dark);

        var rowYi = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*") };
        rowYi.ColumnSpacing = 10;
        Grid.SetColumn(_yiBadge, 0);
        Grid.SetColumn(_good, 1);
        rowYi.Children.Add(_yiBadge);
        rowYi.Children.Add(_good);

        var rowJi = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*") };
        rowJi.ColumnSpacing = 10;
        Grid.SetColumn(_jiBadge, 0);
        Grid.SetColumn(_bad, 1);
        rowJi.Children.Add(_jiBadge);
        rowJi.Children.Add(_bad);

        _hdiv.Margin = new Thickness(0, 10);

        _rightCol.Children.Add(rowYi);
        _rightCol.Children.Add(_hdiv);
        _rightCol.Children.Add(rowJi);

        _main.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,10,*");
        _main.Margin = new Thickness(18, 14);
        Grid.SetColumn(_leftCol, 0);
        Grid.SetColumn(_vdiv, 1);
        _vdiv.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(_rightCol, 2);
        _rightCol.Margin = new Thickness(14, 0, 0, 0);
        _main.Children.Add(_leftCol);
        _main.Children.Add(_vdiv);
        _main.Children.Add(_rightCol);

        var root = new Grid();
        root.Children.Add(_main);
        root.Children.Add(_sdate);
        Card.Child = root;
        Content = Card;
    }

    // 不再按固定个数截断; 词条全量给出, 空间由字号与整词截断兜底
    // 词内插 word joiner(U+2060), 防止 "进人口" 在折行时被拆成 "进人 / 口"
    private static string TermsText(string[]? terms) =>
        terms is { Length: > 0 } ? JoinTerms(terms) : "--";

    private static string JoinTerms(IEnumerable<string> terms) =>
        string.Join(" ", terms.Select(Glue));

    private static string Glue(string term) =>
        term.Length <= 1 ? term : string.Join("\u2060", term.Select(c => c.ToString()));

    private void Rebuild()
    {
        var d = AlmanacService.GetToday();
        var lunar = "--";
        var gzYear = "--";
        var gzRest = "--";
        var date = DateTime.Today.ToString("yyyy-MM-dd");
        if (d is not null)
        {
            lunar = $"{d.LunarMonth}{d.LunarDay}".Trim();
            if (lunar.Length == 0)
            {
                lunar = "--";
            }
            // 干支压成两行: 丙午马年 / 己亥月 丙戌日
            gzYear = string.IsNullOrEmpty(d.YearGz) ? "--" : $"{d.YearGz}{d.Zodiac}年";
            gzRest = $"{d.MonthGz}月 {d.DayGz}日".Trim();
            if (gzRest.Length == 0)
            {
                gzRest = "--";
            }
            _goodTerms = d.Good;
            _badTerms = d.Bad;
            if (!string.IsNullOrEmpty(d.Date) && DateTime.TryParse(d.Date, out var pd))
            {
                date = pd.ToString("yyyy-MM-dd");
            }
        }
        _sdate.Text = NativeTheme.DotDate(date);
        _lunar.Text = lunar;
        _gzYear.Text = gzYear;
        _gzRest.Text = gzRest;
        _term.Text = string.IsNullOrEmpty(d?.SolarTerm) ? "" : d!.SolarTerm;
        ApplyPalette();
        FitText();
        InvalidateVisual();
    }

    private void ApplyPalette()
    {
        var dark = IsDarkNow;
        _sdate.Foreground = new SolidColorBrush(T("faint", dark));
        _label.Foreground = new SolidColorBrush(T("muted", dark));
        _lunar.Foreground = new SolidColorBrush(T("fg", dark));
        _gzYear.Foreground = new SolidColorBrush(T("muted", dark));
        _gzRest.Foreground = new SolidColorBrush(T("muted", dark));
        _term.Foreground = new SolidColorBrush(T("accent", dark));
        _good.Foreground = new SolidColorBrush(T("term_c", dark));
        _bad.Foreground = new SolidColorBrush(T("term_c", dark));
        _yiBadge.Background = new SolidColorBrush(T("yi", dark));
        _jiBadge.Background = new SolidColorBrush(T("ji", dark));
        var div = new SolidColorBrush(T("div", dark));
        _vdiv.Stroke = div;
        _hdiv.Stroke = div;
    }

    private void FitText()
    {
        var compact = Bounds.Width < 300;
        var tiny = Bounds.Height < 150;
        _label.IsVisible = !compact;
        _term.IsVisible = !string.IsNullOrEmpty(_term.Text) && !compact && !tiny;
        _main.Margin = compact ? new Thickness(14, 12) : new Thickness(18, 14);

        // 左列: 农历日期/干支按可用宽收敛, 长农历日不再截断
        var maxLeft = Math.Clamp(Bounds.Width - 36 - 10 - 14 - 120, 96, 150);
        if (double.IsNaN(maxLeft))
        {
            maxLeft = 148;
        }
        _lunar.FontSize = CardText.Fit(_lunar.Text, Common.AppFontFamily, FontWeight.Light, maxLeft, compact ? 26 : 30, 18, 30);
        var wide = Math.Max(
            CardText.Width(_gzYear.Text, Common.AppFontFamily, FontWeight.Normal, 13),
            CardText.Width(_gzRest.Text, Common.AppFontFamily, FontWeight.Normal, 13));
        var gzSize = wide > maxLeft ? Math.Max(10.5, Math.Round(13 * maxLeft / wide, 1)) : 13;
        _gzYear.FontSize = gzSize;
        _gzRest.FontSize = gzSize;

        // 右列: 词条全量换行; 字号从 13.5 收敛到 10.5, 仍放不下才整词截断
        var rightW = _rightCol.Bounds.Width;
        if (double.IsNaN(rightW) || rightW < 40)
        {
            rightW = Math.Max(60, Bounds.Width - _main.Margin.Left - _main.Margin.Right - maxLeft - 10 - 14);
        }
        var rightH = _rightCol.Bounds.Height;
        if (double.IsNaN(rightH) || rightH < 20)
        {
            rightH = Math.Max(60, Bounds.Height - _main.Margin.Top - _main.Margin.Bottom);
        }
        var rowBudget = Math.Max(24, (rightH - 21) / 2);
        var need = 0.0;
        var size = 13.5;
        foreach (var s in new[] { 13.5, 13, 12.5, 12, 11.5, 11, 10.5 })
        {
            size = s;
            need = Math.Max(RowNeed(_goodTerms, rightW, s), RowNeed(_badTerms, rightW, s));
            if (need <= rowBudget)
            {
                break;
            }
        }
        var lineHeight = size * 1.45;
        var allowLines = Math.Max(1, (int)Math.Floor(rowBudget / lineHeight));
        _good.Text = need <= rowBudget ? TermsText(_goodTerms) : FitTerms(_goodTerms, rightW, size, allowLines);
        _bad.Text = need <= rowBudget ? TermsText(_badTerms) : FitTerms(_badTerms, rightW, size, allowLines);
        _good.FontSize = size;
        _good.LineHeight = Math.Round(lineHeight, 1);
        _bad.FontSize = size;
        _bad.LineHeight = Math.Round(lineHeight, 1);
    }

    // 词条列在当前字号下的换行高度
    private static double RowNeed(string[]? terms, double colW, double size)
    {
        var lines = Lines(TermsText(terms), colW, size);
        return Math.Max(1, lines) * size * 1.45;
    }

    private static int Lines(string text, double colW, double size) =>
        Math.Max(1, (int)Math.Ceiling(CardText.Width(text, Common.AppFontFamily, FontWeight.Normal, size) / Math.Max(1, colW)));

    // 整词截断: 从尾部成词去掉, 不出现半词
    private static string FitTerms(string[]? terms, double colW, double size, int maxLines)
    {
        if (terms is not { Length: > 0 })
        {
            return "--";
        }
        var count = terms.Length;
        while (count > 1 && Lines(JoinTerms(terms[..count]), colW, size) > maxLines)
        {
            count--;
        }
        var text = JoinTerms(terms[..count]);
        return count == terms.Length ? text : text + " …";
    }
}


// NativeCharts.cs

/// <summary>图表控件基类: 填满父级 每帧按设备像素绘制(与原版 canvas 一致)</summary>
internal abstract class ChartControl : Control
{
    // 归属卡片明暗 由宿主注入
    public Func<bool>? DarkProvider { get; set; }
    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsFinite(availableSize.Width) ? availableSize.Width : 200,
        double.IsFinite(availableSize.Height) ? availableSize.Height : 100);

    public sealed override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 10 || h < 10)
        {
            return;
        }
        DrawChart(ctx, w, h);
    }

    protected abstract void DrawChart(DrawingContext ctx, double w, double h);
}

/// <summary>
/// 网速监控
/// 顶栏: 标题 12px w700 + ↓接收/↑发送 11.5px w600; 60 点环形缓冲 2 倍自适应纵轴(初值 50000)
/// 横网格 4 格 / 纵网格每 10 点 / 接收=20% 填充+1.6 实线 / 发送=1.3 虚线 3,3 / 右上刻度 11px
/// </summary>
public sealed class NativeNetworkSpeedWidget : WidgetCardBase
{
    private const int MaxPts = 60;

    internal static Color T(string key, bool dark) => dark ? key switch
    {
        "ink" => NativeTheme.Rgba(255, 255, 255, 0.95),
        "sub" => NativeTheme.Rgba(255, 255, 255, 0.60),
        "grid" => NativeTheme.Rgba(255, 255, 255, 0.08),
        "gridv" => NativeTheme.Rgba(255, 255, 255, 0.12),
        _ => Colors.White,
    } : key switch
    {
        "ink" => NativeTheme.Rgba(0, 0, 0, 0.89),
        "sub" => NativeTheme.Rgba(0, 0, 0, 0.60),
        "grid" => NativeTheme.Rgba(0, 0, 0, 0.07),
        "gridv" => NativeTheme.Rgba(0, 0, 0, 0.10),
        _ => Colors.Black,
    };

    private readonly TextBlock _title = new();
    private readonly TextBlock _downLabel = new();
    private readonly TextBlock _downNumber = new();
    private readonly TextBlock _downUnit = new();
    private readonly TextBlock _upLabel = new();
    private readonly TextBlock _upNumber = new();
    private readonly TextBlock _upUnit = new();
    private readonly TextBlock _totals = new();
    private readonly Border _downDot = new();
    private readonly Border _upDot = new();
    private readonly NetChart _chart;

    public NativeNetworkSpeedWidget(ComponentDefinition definition) : base(definition)
    {
        _title.Text = AppUtils.Tr("netspeed.title");
        _downLabel.Text = AppUtils.Tr("netspeed.recv");
        _upLabel.Text = AppUtils.Tr("netspeed.send");

        _chart = new NetChart { DarkProvider = () => CardIsDark };
        var downGroup = MakeRateGroup(_downDot, _downLabel, _downNumber, _downUnit);
        var upGroup = MakeRateGroup(_upDot, _upLabel, _upNumber, _upUnit);

        var rates = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,*"),
        };
        Grid.SetColumn(downGroup, 0);
        Grid.SetColumn(upGroup, 1);
        upGroup.Margin = new Thickness(22, 0, 0, 0);
        rates.Children.Add(downGroup);
        rates.Children.Add(upGroup);

        var topbar = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto"),
        };
        _title.VerticalAlignment = VerticalAlignment.Center;
        topbar.Children.Add(_title);

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,*,Auto"),
            RowSpacing = 5,
            Margin = new Thickness(14, 12, 14, 9),
        };
        Grid.SetRow(topbar, 0);
        Grid.SetRow(rates, 1);
        Grid.SetRow(_chart, 2);
        Grid.SetRow(_totals, 3);
        root.Children.Add(topbar);
        root.Children.Add(rates);
        root.Children.Add(_chart);
        root.Children.Add(_totals);
        Card.Child = root;
        Content = Card;

        SystemSampler.NetReady += OnNet;
        SystemSampler.EnsureStarted();
        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyPalette();
            _chart.InvalidateVisual();
        };
        ApplyPalette();
    }

    private static StackPanel MakeRateGroup(Border dot, TextBlock label, TextBlock number, TextBlock unit)
    {
        dot.Width = 7;
        dot.Height = 7;
        dot.CornerRadius = new CornerRadius(3.5);
        dot.VerticalAlignment = VerticalAlignment.Center;
        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children = { dot, label },
        };
        number.VerticalAlignment = VerticalAlignment.Bottom;
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        unit.Margin = new Thickness(0, 0, 0, 2);
        var value = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            Children = { number, unit },
        };
        return new StackPanel
        {
            Spacing = 1,
            Children = { head, value },
        };
    }

    private bool IsDarkNow => CardIsDark;

    private void ApplyPalette()
    {
        var dark = IsDarkNow;
        var p = WidgetSurfacePalette.Resolve(dark);
        var sub = new SolidColorBrush(p.Text, CardInk.MutedOpacity);
        var ink = new SolidColorBrush(p.Text);
        _title.Foreground = sub;
        _title.FontSize = 12.5;
        _title.FontWeight = FontWeight.SemiBold;
        _downLabel.Foreground = sub;
        _upLabel.Foreground = sub;
        _downLabel.FontSize = 11;
        _upLabel.FontSize = 11;
        _downNumber.Foreground = ink;
        _upNumber.Foreground = ink;
        _downNumber.FontSize = 22;
        _upNumber.FontSize = 22;
        _downNumber.FontWeight = FontWeight.Light;
        _upNumber.FontWeight = FontWeight.Light;
        _downUnit.Foreground = sub;
        _upUnit.Foreground = sub;
        _downUnit.FontSize = 10.5;
        _upUnit.FontSize = 10.5;
        _totals.Foreground = sub;
        _totals.FontSize = 10.5;
        _totals.TextTrimming = TextTrimming.CharacterEllipsis;
        _downDot.Background = CardAccent.Brush();
        _upDot.Background = new SolidColorBrush(p.Text, 0.4);
    }

    private void OnNet(double down, double up)
    {
        var totals = ReadTotals();
        var downBytes = down / 8;
        var upBytes = up / 8;
        Dispatcher.UIThread.Post(() =>
        {
            (_downNumber.Text, _downUnit.Text) = FormatRate(downBytes);
            (_upNumber.Text, _upUnit.Text) = FormatRate(upBytes);
            _chart.Push(downBytes, upBytes);
            _totals.Text = $"{AppUtils.Tr("netspeed.since_start")}  ↓ {FormatSize(totals.Down)}  ↑ {FormatSize(totals.Up)}";
        });
    }

    private static (double Down, double Up) ReadTotals()
    {
        double down = 0;
        double up = 0;
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                    ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                {
                    continue;
                }
                try
                {
                    var stats = ni.GetIPStatistics();
                    down += stats.BytesReceived;
                    up += stats.BytesSent;
                }
                catch
                {
                    // 个别虚拟网卡不提供统计 跳过
                }
            }
        }
        catch
        {
            // 整机统计不可用 保留 0
        }
        return (down, up);
    }

    private static (string Number, string Unit) FormatRate(double v)
    {
        if (v >= 1048576)
        {
            return ((v / 1048576).ToString("0.0", CultureInfo.CurrentCulture), "MB/s");
        }
        if (v >= 1024)
        {
            return ((v / 1024).ToString("0.0", CultureInfo.CurrentCulture), "KB/s");
        }
        return (Math.Round(v).ToString(CultureInfo.CurrentCulture), "B/s");
    }

    private static string FormatSize(double v)
    {
        if (v >= 1073741824)
        {
            return (v / 1073741824).ToString("0.0", CultureInfo.CurrentCulture) + " GB";
        }
        if (v >= 1048576)
        {
            return (v / 1048576).ToString("0.0", CultureInfo.CurrentCulture) + " MB";
        }
        if (v >= 1024)
        {
            return (v / 1024).ToString("0.0", CultureInfo.CurrentCulture) + " KB";
        }
        return Math.Round(v).ToString(CultureInfo.CurrentCulture) + " B";
    }

    /// <summary>网速图表: 最近 60 秒 下载向上/上传向下的双柱</summary>
    private sealed class NetChart : ChartControl
    {
        private readonly List<double> _down = new();
        private readonly List<double> _up = new();
        private double _scale = 100 * 1024;

        public void Push(double d, double u)
        {
            _down.Add(d);
            _up.Add(u);
            if (_down.Count > MaxPts)
            {
                _down.RemoveAt(0);
                _up.RemoveAt(0);
            }
            _scale = ComputeScale();
            InvalidateVisual();
        }

        // 1/2/5/10 圆整刻度 最低 32KB/s 低流量也看得见柱形
        private double ComputeScale()
        {
            var max = 32.0 * 1024;
            foreach (var v in _down)
            {
                max = Math.Max(max, v);
            }
            foreach (var v in _up)
            {
                max = Math.Max(max, v);
            }
            var p = Math.Pow(10, Math.Floor(Math.Log10(max)));
            foreach (var m in new[] { 1.0, 2, 5, 10 })
            {
                var top = m * p;
                if (top >= max)
                {
                    return top;
                }
            }
            return max;
        }

        protected override void DrawChart(DrawingContext ctx, double w, double h)
        {
            var dark = DarkProvider?.Invoke() ?? ThemeSense.IsDark(this);
            var p = WidgetSurfacePalette.Resolve(dark);
            var mid = h / 2;
            ctx.DrawLine(new Pen(new SolidColorBrush(p.Line, 0.55), 1), new Point(0, mid), new Point(w, mid));
            if (_down.Count == 0 || h < 8)
            {
                return;
            }

            const int slots = MaxPts;
            var step = w / slots;
            var bar = Math.Max(1, step * 0.68);
            var accent = CardAccent.Brush();
            var muted = new SolidColorBrush(p.Text, 0.4);
            var half = mid;
            for (var i = 0; i < slots; i++)
            {
                var idx = i - (slots - _down.Count);
                if (idx < 0)
                {
                    continue;
                }
                var x = i * step + (step - bar) / 2;
                var dh = Math.Min(1, _down[idx] / _scale) * half;
                if (dh >= 0.5)
                {
                    ctx.DrawRectangle(accent, null, new Rect(x, mid - dh, bar, dh), 1, 1);
                }
                var uh = Math.Min(1, _up[idx] / _scale) * half;
                if (uh >= 0.5)
                {
                    ctx.DrawRectangle(muted, null, new Rect(x, mid, bar, uh), 1, 1);
                }
            }
        }
    }
}

/// <summary>
/// 分贝仪
/// 顶栏: 标题 12.5px w700 + 当前/峰值 13px w600(数值按级别着色); 绘图区 PL8 PR4 PT20 PB16
/// 纵轴 30~120dB 每 10 横网格 每 20 标签+纵网格 吵线 65 虚线 区域 16% 填充 + 1.6 实线
/// </summary>
public sealed class NativeDecibelMeterWidget : WidgetCardBase
{
    private const int MaxPts = 60;
    private const double Floor = 30;
    private const double TopDb = 120;
    private const double WarnDb = 65;
    private static readonly Color OkColor = NativeTheme.Parse("#2e9e5b");
    private static readonly Color WarmColor = NativeTheme.Parse("#f09f33");
    private static readonly Color HotColor = NativeTheme.Parse("#e2543a");

    private readonly TextBlock _title = new();
    private readonly TextBlock _curLabel = new();
    private readonly TextBlock _curValue = new();
    private readonly TextBlock _peakLabel = new();
    private readonly TextBlock _peakValue = new();
    private readonly DecibelChart _chart;

    public NativeDecibelMeterWidget(ComponentDefinition definition) : base(definition)
    {
        _title.Text = AppUtils.Tr("dbmeter.title");
        _curLabel.Text = AppUtils.Tr("dbmeter.cur");
        _peakLabel.Text = AppUtils.Tr("dbmeter.peak");

        _chart = new DecibelChart { DarkProvider = () => CardIsDark };
        var curGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        curGroup.Children.Add(_curLabel);
        curGroup.Children.Add(_curValue);
        var peakGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        peakGroup.Children.Add(_peakLabel);
        peakGroup.Children.Add(_peakValue);

        var topbar = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*,Auto,Auto"),
            Margin = new Thickness(0, 3, 0, 6),
        };
        Grid.SetColumn(_title, 0);
        _title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(curGroup, 2);
        Grid.SetColumn(peakGroup, 3);
        peakGroup.Margin = new Thickness(10, 0, 0, 0);
        topbar.Children.Add(_title);
        topbar.Children.Add(curGroup);
        topbar.Children.Add(peakGroup);

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            Margin = new Thickness(12, 2, 12, 8),
        };
        Grid.SetRow(topbar, 0);
        Grid.SetRow(_chart, 1);
        root.Children.Add(topbar);
        root.Children.Add(_chart);
        Card.Child = root;
        Content = Card;

        MicSampler.DbReady += OnDb;
        SystemSampler.EnsureStarted();
        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyPalette();
            _chart.InvalidateVisual();
        };
        ApplyPalette();
        SizeChanged += (_, _) => Relayout();
        Relayout();
    }

    // 顶栏字号随卡高 数值加粗 原实现漏设字号
    private void Relayout()
    {
        var h = Bounds.Height;
        var k = Math.Clamp((h > 1 ? h : 200) / 200.0, 0.8, 1.5);
        _title.FontSize = Math.Round(12.5 * k, 1);
        _title.FontWeight = FontWeight.SemiBold;
        _curLabel.FontSize = Math.Round(12 * k, 1);
        _peakLabel.FontSize = Math.Round(12 * k, 1);
        _curValue.FontSize = Math.Round(17 * k, 1);
        _curValue.FontWeight = FontWeight.Light;
        _peakValue.FontSize = Math.Round(17 * k, 1);
        _peakValue.FontWeight = FontWeight.Light;
    }

    private bool IsDarkNow => CardIsDark;

    // T 定义在 NativeNetworkSpeedWidget 此处转发
    private static Color T(string key, bool dark) => NativeNetworkSpeedWidget.T(key, dark);

    internal static Color LevelColor(double db) =>
        db > 70 ? HotColor : db > 60 ? WarmColor : OkColor;

    private void ApplyPalette()
    {
        var dark = IsDarkNow;
        var sub = new SolidColorBrush(T("sub", dark));
        _title.Foreground = sub;
        _curLabel.Foreground = sub;
        _peakLabel.Foreground = sub;
        _curValue.Foreground = sub;
        _peakValue.Foreground = sub;
    }

    private void OnDb(double db)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _curValue.Text = db.ToString("0.0", CultureInfo.CurrentCulture) + " dB";
            _curValue.Foreground = new SolidColorBrush(LevelColor(db));
            var peak = _chart.Push(db);
            _peakValue.Text = peak >= 0 ? peak.ToString("0.0", CultureInfo.CurrentCulture) + " dB" : "--";
            _peakValue.Foreground = peak >= 0 ? new SolidColorBrush(LevelColor(peak)) : new SolidColorBrush(T("sub", IsDarkNow));
        });
    }

    /// <summary>分贝图表 绘制规格逐字取自模板 draw()</summary>
    private sealed class DecibelChart : ChartControl
    {
        private readonly List<double> _pts = new();
        private double _curDb = -1;

        /// <summary>推送新值 返回窗口内峰值</summary>
        public double Push(double db)
        {
            _curDb = db;
            _pts.Add(db);
            if (_pts.Count > MaxPts)
            {
                _pts.RemoveAt(0);
            }
            InvalidateVisual();
            var m = -1.0;
            foreach (var p in _pts)
            {
                if (p > m)
                {
                    m = p;
                }
            }
            return m;
        }

        protected override void DrawChart(DrawingContext ctx, double w, double h)
        {
            var dark = DarkProvider?.Invoke() ?? ThemeSense.IsDark(this);
            const double PL = 8, PR = 4, PT = 20, PB = 16;
            var pw = w - PL - PR;
            var ph = h - PT - PB;
            if (pw < 20 || ph < 20)
            {
                return;
            }
            var range = TopDb - Floor;
            var step = pw / (MaxPts - 1);
            var slotW = pw / MaxPts;
            double YOf(double db) => PT + ph * (1 - Math.Min(Math.Max((db - Floor) / range, 0), 1));

            var subBrush = new SolidColorBrush(T("sub", dark));
            var gridPen = new Pen(new SolidColorBrush(T("grid", dark)), 1);
            var gridVPen = new Pen(new SolidColorBrush(T("gridv", dark)), 1);

            // 横网格 每 10dB + 左标签(30..100 每 20) + dB 表头
            for (var dbv = Floor; dbv <= TopDb; dbv += 10)
            {
                var y = Math.Round(YOf(dbv)) + 0.5;
                ctx.DrawLine(gridPen, new Point(PL, y), new Point(PL + pw, y));
            }
            var smallTypeface = new Typeface("Segoe UI, Microsoft YaHei UI, sans-serif");
            for (var dbv = Floor; dbv <= TopDb - 20; dbv += 20)
            {
                var ft = new FormattedText(((int)dbv).ToString(), System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, smallTypeface, 10, subBrush);
                ctx.DrawText(ft, new Point(PL + 4, YOf(dbv) - 3 - ft.Height));
            }
            var dbLabel = new FormattedText("dB", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, smallTypeface, 10, subBrush);
            ctx.DrawText(dbLabel, new Point(2, 14 - dbLabel.Height));

            // 纵网格 每 20s + 底部标签 -20s/-40s + 0s
            for (var i = 20; i < MaxPts; i += 20)
            {
                var x = Math.Round(PL + pw - (i + 0.5) * slotW) + 0.5;
                ctx.DrawLine(gridPen, new Point(x, PT), new Point(x, PT + ph));
                var ft = new FormattedText("-" + i + "s", System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, smallTypeface, 10, subBrush);
                ctx.DrawText(ft, new Point(PL + pw - (i + 0.5) * slotW - ft.WidthIncludingTrailingWhitespace / 2, PT + ph + 3));
            }
            var zeroLabel = new FormattedText("0s", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, smallTypeface, 10, subBrush);
            ctx.DrawText(zeroLabel, new Point(PL + pw - zeroLabel.WidthIncludingTrailingWhitespace, PT + ph + 3));

            // 坐标轴
            ctx.DrawLine(gridVPen, new Point(PL + 0.5, PT), new Point(PL + 0.5, PT + ph + 0.5));
            ctx.DrawLine(gridVPen, new Point(PL + 0.5, PT + ph + 0.5), new Point(PL + pw, PT + ph + 0.5));

            // 吵线 65 虚线
            if (WarnDb > Floor && WarnDb < TopDb)
            {
                var y = Math.Round(YOf(WarnDb)) + 0.5;
                ctx.DrawLine(new Pen(new SolidColorBrush(HotColor), 1, new DashStyle(new double[] { 4, 4 }, 0)),
                    new Point(PL, y), new Point(PL + pw, y));
            }
            if (_pts.Count < 2)
            {
                return;
            }
            var col = LevelColor(_curDb >= 0 ? _curDb : Floor);

            // 区域 16% 填充
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(PL + pw - (_pts.Count - 1) * step, PT + ph), isFilled: true);
                for (var i = 0; i < _pts.Count; i++)
                {
                    g.LineTo(new Point(PL + pw - (_pts.Count - 1 - i) * step, YOf(_pts[i])));
                }
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(col, 0.16), null, area);

            // 1.6 实线
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(new Point(PL + pw - (_pts.Count - 1) * step, YOf(_pts[0])), isFilled: false);
                for (var i = 1; i < _pts.Count; i++)
                {
                    g.LineTo(new Point(PL + pw - (_pts.Count - 1 - i) * step, YOf(_pts[i])));
                }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(col), 1.6, null, PenLineCap.Round, PenLineJoin.Round), line);
        }
    }
}


internal static class DailyPack
{
    private static readonly object Gate = new();
    private static DispatcherTimer? _midnight;
    private static bool _generating;
    private static JsonObject? _cache;
    private static string _cacheDate = "";

    internal const double NetworkRetryMinutes = 10;

    private static string PackPath => IOPath.Combine(Paths.DataCache, "content_pack.json");

    private static readonly string[] BuiltinWords =
    [
        """{"word":"dawn","phonetic":"/dɔːn/","translation":"n. 黎明","example":"The dawn light filled the classroom.","example_trans":"晨光洒满教室。"}""",
        """{"word":"curious","phonetic":"/ˈkjʊəriəs/","translation":"adj. 好奇的","example":"Curious minds ask why.","example_trans":"好奇的心会追问为什么。"}""",
        """{"word":"effort","phonetic":"/ˈefət/","translation":"n. 努力","example":"Effort beats talent when talent stops trying.","example_trans":"当天才放弃时,努力胜过天赋。"}""",
        """{"word":"horizon","phonetic":"/həˈraɪzn/","translation":"n. 地平线","example":"The sun rose above the horizon.","example_trans":"太阳升上地平线。"}""",
        """{"word":"gather","phonetic":"/ˈɡæðə/","translation":"v. 聚集","example":"We gather here every Monday.","example_trans":"我们每周一在这里集合。"}""",
        """{"word":"steady","phonetic":"/ˈstedi/","translation":"adj. 稳定的","example":"Steady steps win the long race.","example_trans":"稳扎稳打赢得长跑。"}""",
        """{"word":"brilliant","phonetic":"/ˈbrɪliənt/","translation":"adj. 灿烂的","example":"A brilliant idea needs a brave start.","example_trans":"灿烂的想法需要勇敢的开始。"}""",
        """{"word":"journey","phonetic":"/ˈdʒɜːni/","translation":"n. 旅程","example":"Learning is a long journey.","example_trans":"学习是一段漫长的旅程。"}""",
    ];

    // 兜底句包
    private static readonly string[] BuiltinSentences =
    [
        """{"content":"The best time to plant a tree was ten years ago. The second best is now.","note":"种树最好的时间是十年前,其次是现在。"}""",
        """{"content":"Well begun is half done.","note":"好的开始是成功的一半。"}""",
        """{"content":"Little by little, one travels far.","note":"积跬步,至千里。"}""",
        """{"content":"Actions speak louder than words.","note":"行动胜于言语。"}""",
        """{"content":"A journey of a thousand miles begins with a single step.","note":"千里之行,始于足下。"}""",
        """{"content":"Practice makes perfect.","note":"熟能生巧。"}""",
        """{"content":"Where there is a will, there is a way.","note":"有志者事竟成。"}""",
        """{"content":"The early bird catches the worm.","note":"早起的鸟儿有虫吃。"}""",
    ];

    public static void Start()
    {
        Ensure();
        ArmMidnight();
    }

    public static (string Cursor, JsonObject Item)? Take(string kind)
    {
        var pack = Read(kind, todayOnly: true);
        if (pack is not { Count: > 0 })
        {
            Ensure();
            pack = Read(kind, todayOnly: false);
        }
        if (pack is not { Count: > 0 })
        {
            return null;
        }
        var index = Rotation(pack.Count);
        return pack[index] is JsonObject item ? ($"{TodayKey()}#{index}", item) : null;
    }

    private static int Rotation(int count)
    {
        if (count <= 0)
        {
            return 0;
        }
        var now = AppUtils.PreciseNow();
        var mode = Config.ContentPackRotate.Value;
        if (mode == "schedule")
        {
            var rows = TimetableScheduleProvider.GetTodaySchedule();
            if (rows.Count > 0)
            {
                var started = rows.Count(r => !r.IsBreak && TimeOnly.TryParse(r.StartTime, out var st) && st <= TimeOnly.FromDateTime(now));
                return started % count;
            }
        }
        var minutes = int.TryParse(mode.TrimEnd('分', '钟'), out var m) ? Math.Max(1, m) : 30;
        return (int)(now.TimeOfDay.TotalMinutes / minutes) % count;
    }

    private static void Ensure()
    {
        lock (Gate)
        {
            if (_generating || (Cached() is { } pack && pack["date"]?.GetValue<string>() == TodayKey()))
            {
                return;
            }
            _generating = true;
        }
        _ = GenerateAsync();
    }

    private static async Task GenerateAsync()
    {
        try
        {
            var date = TodayKey();
            var words = await FromAi("word") ?? Pool(BuiltinWords);
            var sentences = await FromAi("sentence") ?? Pool(BuiltinSentences);
            var root = new JsonObject { ["date"] = date, ["word"] = words, ["sentence"] = sentences };
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.DataCache);
                File.WriteAllText(PackPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                _cache = root;
                _cacheDate = date;
            }
            Log.Info($"[日更] 包就绪 words={words.Count} sentences={sentences.Count}");
        }
        catch (Exception e)
        {
            Log.Warning($"[日更] 生成失败: {e.Message}");
        }
        finally
        {
            lock (Gate)
            {
                _generating = false;
            }
        }
    }

    private static async Task<JsonArray?> FromAi(string kind)
    {
        if (!AiService.Ready)
        {
            return null;
        }
        const string system = "你是英语教学助手。只输出 JSON 数组,不要解释或代码块标记。";
        var prompt = kind == "word"
            ? "生成8个适合中学生的英语单词词条,JSON数组,每项含 word(小写)、phonetic(音标含//)、translation(词性+中文)、example(英文例句)、example_trans(中文翻译)。难度循序渐进,主题贴近校园生活。"
            : "生成8句适合中学生的英语名言或谚语,JSON数组,每项含 content(英文句子)、note(中文翻译或一句点评)。积极向上,适合教室大屏展示。";
        try
        {
            var result = await AiService.ChatAsync(prompt, system);
            if (!result.Ok)
            {
                Log.Warning($"[日更] AI {kind} 失败: {result.Error}");
                return null;
            }
            return Parse(result.Content);
        }
        catch (Exception e)
        {
            Log.Warning($"[日更] AI {kind} 异常: {e.Message}");
            return null;
        }
    }

    private static JsonArray? Parse(string text)
    {
        var s = Regex.Replace(text, "```(?:json)?", "").Trim();
        var start = s.IndexOf('[');
        var end = s.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(s[start..(end + 1)]) as JsonArray;
        }
        catch
        {
            return null;
        }
    }

    private static JsonArray Pool(string[] items)
    {
        var arr = new JsonArray();
        foreach (var item in items)
        {
            if (JsonNode.Parse(item) is { } node)
            {
                arr.Add(node);
            }
        }
        return arr;
    }

    private static void ArmMidnight()
    {
        if (_midnight is null)
        {
            _midnight = new DispatcherTimer();
            _midnight.Tick += OnMidnight;
        }
        _midnight.Stop();
        var now = AppUtils.PreciseNow();
        _midnight.Interval = now.Date.AddDays(1) - now + TimeSpan.FromSeconds(2);
        _midnight.Start();
    }

    private static void OnMidnight(object? sender, EventArgs e)
    {
        _midnight?.Stop();
        Ensure();
        ArmMidnight();
    }

    private static JsonObject? ReadPack()
    {
        try
        {
            return File.Exists(PackPath) ? JsonNode.Parse(File.ReadAllText(PackPath)) as JsonObject : null;
        }
        catch
        {
            return null;
        }
    }

    private static JsonArray? Read(string kind, bool todayOnly)
    {
        lock (Gate)
        {
            var pack = Cached();
            if (pack is null || (todayOnly && pack["date"]?.GetValue<string>() != TodayKey()))
            {
                return null;
            }
            return pack[kind] as JsonArray;
        }
    }

    private static JsonObject? Cached()
    {
        var date = TodayKey();
        if (_cacheDate == date)
        {
            return _cache;
        }
        _cache = ReadPack();
        _cacheDate = date;
        return _cache;
    }

    private static string TodayKey() => AppUtils.PreciseNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// 每日单词
/// </summary>
public sealed class NativeDailyWordWidget : WidgetCardBase
{
    // 词性拆行
    private static readonly Regex PosSplitRegex = new(@"\s+(?=[a-zA-Z]{1,10}\.\s)");
    private static readonly Regex PosMarkRegex = new(@"^([a-zA-Z]{1,10}\.)\s*(.*)$");

    private static Color T(string key, bool dark) => dark ? key switch
    {
        "word1" => NativeTheme.Parse("#f5f5f5"),
        "word2" => NativeTheme.Parse("#9dbdae"),
        "zh" => NativeTheme.Rgba(255, 255, 255, 0.85),
        "zh_sub" => NativeTheme.Rgba(255, 255, 255, 0.62),
        "ex_en" => NativeTheme.Parse("#e8e8e8"),
        "date_c" => NativeTheme.Rgba(255, 255, 255, 0.45),
        "sep" => NativeTheme.Rgba(255, 255, 255, 0.25),
        _ => Colors.White,
    } : key switch
    {
        "word1" => NativeTheme.Parse("#1f2937"),
        "word2" => NativeTheme.Parse("#4f7a6b"),
        "zh" => NativeTheme.Rgba(40, 40, 40, 0.88),
        "zh_sub" => NativeTheme.Rgba(40, 40, 40, 0.65),
        "ex_en" => NativeTheme.Parse("#2d3a33"),
        "date_c" => NativeTheme.Rgba(60, 60, 60, 0.5),
        "sep" => NativeTheme.Rgba(0, 0, 0, 0.22),
        _ => Colors.Black,
    };

    private readonly TextBlock _bgLetter = new();
    private readonly TextBlock _date = new();
    private readonly TextBlock _word = new();
    private readonly Border _phoneticChip = new();
    private readonly TextBlock _phoneticText = new();
    private readonly StackPanel _trans = new();
    private readonly DashedRule _sep = new();
    private readonly StackPanel _example = new();
    private readonly TextBlock _exEn = new();
    private readonly TextBlock _exZh = new();

    // 最近一次数据 主题切换时重排
    private string _lastWord = "--", _lastPhonetic = "", _lastTrans = "", _lastExText = "", _lastExTrans = "", _lastDate = "";

    private string _packCursor = "";
    private DateTime _netAt;

    public NativeDailyWordWidget(ComponentDefinition definition) : base(definition)
    {
        BuildLayout();
        ActualThemeVariantChanged += (_, _) => Apply(_lastWord, _lastPhonetic, _lastTrans, _lastExText, _lastExTrans, _lastDate);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += (_, _) => _ = RefreshAsync();
        timer.Start();
        _ = RefreshAsync();
    }

    private bool IsDarkNow => CardIsDark;

    private IBrush Gradient(Color c1, Color c2) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(c1, 0),
            new GradientStop(c2, 1),
        },
    };

    private IBrush WatermarkBrush(Color accent) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(accent, 0),
            new GradientStop(Colors.Transparent, 0.85),
        },
    };

    private void BuildLayout()
    {
        var serif = new FontFamily(NativeTheme.SerifFont);
        var accent = NativeTheme.Accent();

        _bgLetter.FontFamily = serif;
        _bgLetter.FontSize = 150;
        _bgLetter.FontWeight = FontWeight.Bold;
        _bgLetter.HorizontalAlignment = HorizontalAlignment.Right;
        _bgLetter.VerticalAlignment = VerticalAlignment.Top;
        _bgLetter.Margin = new Thickness(0, -20, 2, 0);
        _bgLetter.Opacity = 0.16;
        _bgLetter.IsHitTestVisible = false;

        _date.FontFamily = new FontFamily(NativeTheme.MonoFont);
        _date.FontSize = 12;
        _date.LetterSpacing = 2.5;
        _date.HorizontalAlignment = HorizontalAlignment.Right;
        _date.VerticalAlignment = VerticalAlignment.Top;
        _date.Margin = new Thickness(0, 15, 18, 0);

        _word.FontFamily = serif;
        _word.FontSize = 40;
        _word.FontWeight = FontWeight.Light;

        _phoneticText.FontSize = 14;
        _phoneticChip.Child = _phoneticText;
        _phoneticChip.Padding = new Thickness(10, 2);
        _phoneticChip.CornerRadius = new CornerRadius(999);
        _phoneticChip.VerticalAlignment = VerticalAlignment.Center;

        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
        };
        head.Children.Add(_word);
        head.Children.Add(_phoneticChip);

        _trans.Margin = new Thickness(0, 12, 0, 0);
        _trans.Spacing = 5;

        _sep.Margin = new Thickness(0, 13, 0, 0);
        _sep.HorizontalAlignment = HorizontalAlignment.Left;
        _sep.Width = 200; // 60% of 400 内容宽

        _exEn.FontFamily = serif;
        _exEn.FontStyle = FontStyle.Italic;
        _exEn.FontSize = 18;
        _exEn.TextWrapping = TextWrapping.Wrap;
        _exZh.Margin = new Thickness(0, 4, 0, 0);
        _exZh.FontSize = 15;
        _exZh.TextWrapping = TextWrapping.Wrap;
        _example.Margin = new Thickness(0, 12, 0, 0);
        _example.Children.Add(_exEn);
        _example.Children.Add(_exZh);

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(head);
        content.Children.Add(_trans);
        content.Children.Add(_sep);
        content.Children.Add(_example);

        var pad = new Border { Padding = new Thickness(26, 20, 24, 22), Child = content };

        var root = new Grid();
        root.Children.Add(pad);
        root.Children.Add(_bgLetter);
        root.Children.Add(_date);
        Card.Child = root;
        Content = Card;
    }

    private void ApplyFromPack(JsonObject o)
    {
        var w = o["word"]?.GetValue<string>() ?? "--";
        var ph = o["phonetic"]?.GetValue<string>() ?? "";
        var tr = o["translation"]?.GetValue<string>() ?? "";
        var ex = o["example"]?.GetValue<string>() ?? "";
        var exTr = o["example_trans"]?.GetValue<string>() ?? "";
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        Dispatcher.UIThread.Post(() => Apply(w, ph, tr, ex, exTr, day));
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        if (DailyPack.Take("word") is { } take)
        {
            if (take.Cursor != _packCursor)
            {
                _packCursor = take.Cursor;
                ApplyFromPack(take.Item);
            }
            return;
        }
        if ((AppUtils.PreciseNow() - _netAt).TotalMinutes < DailyPack.NetworkRetryMinutes)
        {
            return;
        }
        _netAt = AppUtils.PreciseNow();
        JsonElement? data = null;
        try
        {
            data = await WordService.FetchDailyWordAsync();
        }
        catch (Exception e)
        {
            Log.Warning($"[DWD] 在线获取失败 走缓存: {e.Message}");
        }
        // 原版: 接口无数据用过期缓存
        if (data is null)
        {
            data = AppUtils.LoadCache("daily_word", ignoreExpiry: true);
        }
        string word = "--", phonetic = "", translation = "", exText = "", exTrans = "";
        var date = DateTime.Today.ToString("yyyy-MM-dd");
        if (data is { ValueKind: JsonValueKind.Object } root)
        {
            date = root.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString() ?? date
                : date;
            if (root.TryGetProperty("word", out var w) && w.ValueKind == JsonValueKind.Object)
            {
                word = w.TryGetProperty("word", out var wp) ? wp.GetString() ?? "--" : "--";
                phonetic = w.TryGetProperty("phonetic", out var ph) ? ph.GetString() ?? "" : "";
                translation = w.TryGetProperty("translation", out var trEl) ? trEl.GetString() ?? "" : "";
                if (w.TryGetProperty("examples", out var exs) && exs.ValueKind == JsonValueKind.Array && exs.GetArrayLength() > 0 && exs[0].ValueKind == JsonValueKind.Object)
                {
                    var ex = exs[0];
                    exText = ex.TryGetProperty("text", out var et) ? et.GetString() ?? "" : "";
                    exTrans = ex.TryGetProperty("translation", out var etr) ? etr.GetString() ?? "" : "";
                }
            }
        }
        Dispatcher.UIThread.Post(() => Apply(word, phonetic, translation, exText, exTrans, date));
    }

    private void Apply(string word, string phonetic, string translation, string exText, string exTrans, string date)
    {
        _lastWord = word;
        _lastPhonetic = phonetic;
        _lastTrans = translation;
        _lastExText = exText;
        _lastExTrans = exTrans;
        _lastDate = date;
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();

        _date.Text = NativeTheme.DotDate(date);
        _word.Text = word;
        _bgLetter.Text = word != "--" && word.Length > 0 ? char.ToUpperInvariant(word[0]).ToString() : "";

        // 音标胶囊(空则隐藏)
        _phoneticChip.IsVisible = phonetic.Length > 0;
        _phoneticText.Text = phonetic;
        _phoneticText.Foreground = new SolidColorBrush(accent);
        _phoneticChip.BorderBrush = new SolidColorBrush(NativeTheme.Rgba(accent.R, accent.G, accent.B, 0.5));
        _phoneticChip.BorderThickness = new Thickness(1);

        // 词性拆行
        _trans.Children.Clear();
        foreach (var raw in PosSplitRegex.Split(translation.Trim()))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var m = PosMarkRegex.Match(line);
            if (m.Success)
            {
                var posChip = new Border
                {
                    Background = new SolidColorBrush(NativeTheme.Rgba(accent.R, accent.G, accent.B, 0.14)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = m.Groups[1].Value,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(accent),
                    },
                };
                row.Children.Add(posChip);
                row.Children.Add(new TextBlock
                {
                    Text = m.Groups[2].Value,
                    FontSize = 16.5,
                    Foreground = new SolidColorBrush(T("zh", dark)),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            else
            {
                row.Children.Add(new TextBlock
                {
                    Text = line,
                    FontSize = 16.5,
                    Foreground = new SolidColorBrush(T("zh", dark)),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            _trans.Children.Add(row);
        }

        // 例句区(空则整体隐藏)
        _example.IsVisible = exText.Length > 0;
        _sep.IsVisible = exText.Length > 0;
        _exEn.Text = "“" + exText + "”";
        _exZh.Text = exTrans;
        _exZh.IsVisible = exTrans.Length > 0;

        // 主题色
        _word.Foreground = Gradient(T("word1", dark), T("word2", dark));
        _bgLetter.Foreground = WatermarkBrush(accent);
        _date.Foreground = new SolidColorBrush(T("date_c", dark));
        _sep.Stroke = new SolidColorBrush(T("sep", dark));
        _exEn.Foreground = new SolidColorBrush(T("ex_en", dark));
        _exZh.Foreground = new SolidColorBrush(T("zh_sub", dark));
        _sep.Stroke = new SolidColorBrush(T("sep", dark));
        InvalidateVisual();
    }
}

/// <summary>
/// 每日英语
/// 大引号(Georgia 118px accent渐变) + 英句(Georgia italic w600 27px 双色渐变)
/// + 中文(左侧 3px accent 边线 17px) + 日期 + 左下渐变条(150x2)
/// </summary>
public sealed class NativeDailySentenceWidget : WidgetCardBase
{
    private static Color T(string key, bool dark) => dark ? key switch
    {
        "en1" => NativeTheme.Parse("#f5f5f5"),
        "en2" => NativeTheme.Parse("#9dbdae"),
        "zh" => NativeTheme.Rgba(255, 255, 255, 0.72),
        "date_c" => NativeTheme.Rgba(255, 255, 255, 0.45),
        _ => Colors.White,
    } : key switch
    {
        "en1" => NativeTheme.Parse("#1f2937"),
        "en2" => NativeTheme.Parse("#4f7a6b"),
        "zh" => NativeTheme.Rgba(40, 40, 40, 0.78),
        "date_c" => NativeTheme.Rgba(60, 60, 60, 0.5),
        _ => Colors.Black,
    };

    private readonly TextBlock _quote = new();
    private readonly TextBlock _en = new();
    private readonly TextBlock _zh = new();
    private readonly TextBlock _date = new();
    private readonly Rectangle _bar = new();

    // 最近一次数据 主题切换时重排
    private string _lastContent = "--", _lastNote = "", _lastDate = "";

    // 包游标 同游标不重绘
    private string _packCursor = "";
    private DateTime _netAt;

    public NativeDailySentenceWidget(ComponentDefinition definition) : base(definition)
    {
        BuildLayout();
        ActualThemeVariantChanged += (_, _) => Apply(_lastContent, _lastNote, _lastDate);
        // 60s tick 本地包零网络
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += (_, _) => _ = RefreshAsync();
        timer.Start();
        _ = RefreshAsync();
    }

    private bool IsDarkNow => CardIsDark;

    private IBrush Gradient(Color c1, Color c2) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(c1, 0),
            new GradientStop(c2, 1),
        },
    };

    private void BuildLayout()
    {
        var serif = new FontFamily(NativeTheme.SerifFont);

        _quote.Text = "“";
        _quote.FontFamily = serif;
        _quote.FontSize = 118;
        _quote.HorizontalAlignment = HorizontalAlignment.Left;
        _quote.VerticalAlignment = VerticalAlignment.Top;
        _quote.Margin = new Thickness(14, -4, 0, 0);
        _quote.IsHitTestVisible = false;

        _en.FontFamily = serif;
        _en.FontStyle = FontStyle.Italic;
        _en.FontWeight = FontWeight.SemiBold;
        _en.FontSize = 27;
        _en.TextWrapping = TextWrapping.Wrap;
        _en.LineHeight = 27 * 1.42;

        // 中文: 左侧 3px accent 边线
        _zh.FontSize = 17;
        _zh.TextWrapping = TextWrapping.Wrap;
        _zh.LineHeight = 17 * 1.5;
        var zhBorder = new Border
        {
            Child = _zh,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 0, 0, 0),
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _date.FontFamily = new FontFamily(NativeTheme.MonoFont);
        _date.FontSize = 12;
        _date.LetterSpacing = 2.5;
        _date.HorizontalAlignment = HorizontalAlignment.Right;
        _date.VerticalAlignment = VerticalAlignment.Top;
        _date.Margin = new Thickness(0, 15, 18, 0);

        _bar.Width = 150;
        _bar.Height = 2;
        _bar.HorizontalAlignment = HorizontalAlignment.Left;
        _bar.VerticalAlignment = VerticalAlignment.Bottom;
        _bar.Margin = new Thickness(26, 0, 0, 12);

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(_en);
        content.Children.Add(zhBorder);

        var pad = new Border { Padding = new Thickness(58, 24, 26, 26), Child = content };

        var root = new Grid();
        root.Children.Add(pad);
        root.Children.Add(_quote);
        root.Children.Add(_date);
        root.Children.Add(_bar);
        Card.Child = root;
        Content = Card;
    }

    // 包条目落版
    private void ApplyFromPack(JsonObject o)
    {
        var body = o["content"]?.GetValue<string>() ?? "--";
        var note = o["note"]?.GetValue<string>() ?? "";
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        Dispatcher.UIThread.Post(() => Apply(body, note, day));
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        // 包优先 同游标不重绘
        if (DailyPack.Take("sentence") is { } take)
        {
            if (take.Cursor != _packCursor)
            {
                _packCursor = take.Cursor;
                ApplyFromPack(take.Item);
            }
            return;
        }
        // 回落节流 免 60s 轮询
        if ((AppUtils.PreciseNow() - _netAt).TotalMinutes < DailyPack.NetworkRetryMinutes)
        {
            return;
        }
        _netAt = AppUtils.PreciseNow();
        JsonElement? data = null;
        try
        {
            data = await SentenceService.FetchDailySentenceAsync();
        }
        catch (Exception e)
        {
            Log.Warning($"[DSE] 在线获取失败 走缓存: {e.Message}");
        }
        if (data is null)
        {
            data = AppUtils.LoadCache("daily_sentence", ignoreExpiry: true);
        }
        string content = "--", note = "";
        var date = DateTime.Today.ToString("yyyy-MM-dd");
        if (data is { ValueKind: JsonValueKind.Object } root)
        {
            date = root.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString() ?? date
                : date;
            if (root.TryGetProperty("sentence", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                content = s.TryGetProperty("content", out var c) ? c.GetString() ?? "--" : "--";
                note = s.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "";
            }
        }
        Dispatcher.UIThread.Post(() => Apply(content, note, date));
    }

    private void Apply(string content, string note, string date)
    {
        _lastContent = content;
        _lastNote = note;
        _lastDate = date;
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();
        _en.Text = content;
        _zh.Text = note;
        _zh.IsVisible = note.Length > 0;
        _date.Text = NativeTheme.DotDate(date);

        _quote.Foreground = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(accent, 0),
                new GradientStop(Colors.Transparent, 0.92),
            },
        };
        _en.Foreground = Gradient(T("en1", dark), T("en2", dark));
        _zh.Foreground = new SolidColorBrush(T("zh", dark));
        _date.Foreground = new SolidColorBrush(T("date_c", dark));
        // 中文左边线跟随 accent
        if (_zh.Parent is Border b)
        {
            b.BorderBrush = new SolidColorBrush(accent);
        }
        _bar.Fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(accent, 0),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        InvalidateVisual();
    }
}


// NativeInteractive.cs

/// <summary>公告/作业板共用主题对 逐值取自模板 ThemePairs</summary>
internal static class CardTheme
{
    public static Color T(string key, bool dark) => dark ? key switch
    {
        "ink" => NativeTheme.Rgba(255, 255, 255, 0.95),
        "sub" => NativeTheme.Rgba(255, 255, 255, 0.62),
        "line" => NativeTheme.Rgba(255, 255, 255, 0.10),
        "hover" => NativeTheme.Rgba(255, 255, 255, 0.06),
        "chip" => NativeTheme.Rgba(255, 255, 255, 0.08),
        "bar" => NativeTheme.Rgba(255, 255, 255, 0.14),
        "empty" => NativeTheme.Rgba(255, 255, 255, 0.40),
        "card" => NativeTheme.Rgba(255, 255, 255, 0.055),
        "cardline" => NativeTheme.Rgba(255, 255, 255, 0.09),
        "dotbd" => NativeTheme.Rgba(255, 255, 255, 0.35),
        _ => Colors.White,
    } : key switch
    {
        "ink" => NativeTheme.Rgba(0, 0, 0, 0.89),
        "sub" => NativeTheme.Rgba(0, 0, 0, 0.62),
        "line" => NativeTheme.Rgba(0, 0, 0, 0.08),
        "hover" => NativeTheme.Rgba(0, 0, 0, 0.045),
        "chip" => NativeTheme.Rgba(0, 0, 0, 0.05),
        "bar" => NativeTheme.Rgba(0, 0, 0, 0.12),
        "empty" => NativeTheme.Rgba(0, 0, 0, 0.45),
        "card" => NativeTheme.Rgba(255, 255, 255, 0.55),
        "cardline" => NativeTheme.Rgba(0, 0, 0, 0.06),
        "dotbd" => NativeTheme.Rgba(0, 0, 0, 0.28),
        _ => Colors.Black,
    };

    internal static readonly Color Danger = NativeTheme.Parse("#e5484d");

    // 顶栏文字开关 字号驱动内距/圆角
    internal static Border TagButton(string text, double fontSize, bool dark)
    {
        var pad = Math.Round(fontSize * 0.87);
        return new Border
        {
            Child = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(T("sub", dark)),
            },
            Background = new SolidColorBrush(T("chip", dark)),
            Padding = new Thickness(pad, 3, pad, 3),
            CornerRadius = new CornerRadius(Math.Round(fontSize * 0.35)),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // 小号操作按钮(公告栏 ✎× 24x24 / 作业板 18x18)
    internal static Border IconButton(string glyph, double size, bool danger, Func<bool> dark)
    {
        var btn = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(5),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = size * 0.58,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(T("sub", dark())),
            },
        };
        var isDel = glyph == "×";
        btn.PointerEntered += (_, _) =>
        {
            if (isDel && danger)
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(31, 229, 72, 77));
                ((TextBlock)btn.Child!).Foreground = new SolidColorBrush(Danger);
            }
            else
            {
                btn.Background = new SolidColorBrush(T("chip", dark()));
                ((TextBlock)btn.Child!).Foreground = new SolidColorBrush(NativeTheme.Accent());
            }
        };
        btn.PointerExited += (_, _) =>
        {
            btn.Background = null;
            ((TextBlock)btn.Child!).Foreground = new SolidColorBrush(T("sub", dark()));
        };
        return btn;
    }
}

/// <summary>组件统一空态: 强调色徽标 + 标题 + 说明 + 可选操作行</summary>
internal sealed class CardEmptyState : StackPanel
{
    private readonly Border _badge = new();
    private readonly FAFontIcon _icon;
    private readonly TextBlock _title = new();
    private readonly TextBlock _hint = new();

    public StackPanel Actions { get; } = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 2, 0, 0),
        IsVisible = false,
    };

    public CardEmptyState(FAFontIcon icon, string title, string hint, double badgeSize = 42)
    {
        _icon = icon;
        // 只留一枚安静的字形图标 不再铺色块 避免绿底绿字
        _icon.FontSize = Math.Round(badgeSize * 0.62);
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _icon.VerticalAlignment = VerticalAlignment.Center;
        _badge.Width = badgeSize;
        _badge.Height = badgeSize;
        _badge.CornerRadius = new CornerRadius(0);
        _badge.HorizontalAlignment = HorizontalAlignment.Center;
        _badge.Child = _icon;

        _title.Text = title;
        _title.FontSize = WidgetType.Title;
        _title.FontWeight = FontWeight.Medium;
        _title.TextAlignment = TextAlignment.Center;
        _title.HorizontalAlignment = HorizontalAlignment.Center;

        _hint.Text = hint;
        _hint.FontSize = 11.5;
        _hint.TextAlignment = TextAlignment.Center;
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.MaxWidth = 240;
        _hint.HorizontalAlignment = HorizontalAlignment.Center;

        Spacing = 6;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_badge);
        Children.Add(_title);
        Children.Add(_hint);
        Children.Add(Actions);
    }

    public void AddAction(Control action)
    {
        Actions.Children.Add(action);
        Actions.IsVisible = true;
    }

    public void Apply(WidgetSurfacePalette p, Color? ink = null)
    {
        _badge.Background = null;
        _icon.Foreground = new SolidColorBrush(p.Text, CardInk.FaintOpacity);
        _title.Foreground = ink is { } c ? new SolidColorBrush(c) : CardInk.Fg(p);
        _hint.Foreground = ink is { } h ? new SolidColorBrush(h, CardInk.MutedOpacity) : CardInk.Muted(p);
    }
}

/// <summary>归档浏览器</summary>
public sealed class DailyLogPanel : Grid
{
    private static readonly AvColor[] Palette =
    [
        NativeTheme.Parse("#f59e0b"), NativeTheme.Parse("#3b82f6"), NativeTheme.Parse("#10b981"), NativeTheme.Parse("#ef4444"),
        NativeTheme.Parse("#8b5cf6"), NativeTheme.Parse("#ec4899"), NativeTheme.Parse("#06b6d4"), NativeTheme.Parse("#84cc16"),
    ];
    private const double FontTitle = 17;
    private const double FontBody = 15;
    private const double FontMeta = 13;
    private const double FontMicro = 12;
    private const double LineHeight = 24;
    private static FontFamily NumFont => Common.AppFontFamily;

    private readonly string _kind;
    private readonly ListBox _dates = new();
    private readonly ContentControl _pane = new();
    private readonly StackPanel _emptyStack;
    private readonly TextBlock _datesEmpty = new();
    private readonly Border _dateHost;
    private readonly Border _paneHost;
    private List<(DateOnly Day, JsonObject Root)> _days = new();

    /// <summary>作业板快照</summary>
    public static DailyLogPanel ForHomework(string componentId) =>
        new(DailyLog.HomeworkPrefix + componentId);

    /// <summary>公告快照</summary>
    public static DailyLogPanel ForAnnouncements() =>
        new("announcements");

    private static TextBlock Txt(string text, double size, AvColor color, FontWeight? weight = null, FontFamily? family = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = family ?? Common.AppFontFamily,
            Foreground = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (weight is { } w)
        {
            tb.FontWeight = w;
        }
        return tb;
    }

    private DailyLogPanel(string kind)
    {
        _kind = kind;
        ColumnDefinitions = new ColumnDefinitions("208,12,*");
        Avalonia.Controls.Documents.TextElement.SetFontFamily(this, Common.AppFontFamily);

        _dates.SelectionChanged += (_, _) => ShowSelected();
        _dates.Background = Brushes.Transparent;
        _dates.BorderThickness = new Thickness(0);
        _dates.Padding = new Thickness(0, 6, 0, 6);
        var dateScroll = new ScrollViewer
        {
            Content = _dates,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _datesEmpty.FontSize = FontMeta;
        _datesEmpty.FontFamily = Common.AppFontFamily;
        _datesEmpty.TextAlignment = TextAlignment.Center;
        _datesEmpty.HorizontalAlignment = HorizontalAlignment.Center;
        _datesEmpty.VerticalAlignment = VerticalAlignment.Center;
        _datesEmpty.IsHitTestVisible = false;
        var dateGrid = new Grid();
        dateGrid.Children.Add(dateScroll);
        dateGrid.Children.Add(_datesEmpty);
        _dateHost = new Border
        {
            Child = dateGrid,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };

        // 空态
        var emptyIcon = FAIcons.Glyph(AppIcons.Calendar, 26);
        emptyIcon.Opacity = 0.22;
        emptyIcon.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyStack = new StackPanel
        {
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            IsVisible = false,
            Children =
            {
                emptyIcon,
                new TextBlock
                {
                    Text = AppUtils.Tr("dailylog.empty"),
                    FontSize = FontMeta,
                    FontFamily = Common.AppFontFamily,
                    TextAlignment = TextAlignment.Center,
                    Opacity = 0.45,
                },
            },
        };

        var paneScroll = new ScrollViewer
        {
            Content = _pane,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var paneGrid = new Grid();
        paneGrid.Children.Add(paneScroll);
        paneGrid.Children.Add(_emptyStack);
        _paneHost = new Border
        {
            Child = paneGrid,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };

        Children.Add(_dateHost);
        Children.Add(_paneHost);
        SetColumn(_dateHost, 0);
        SetColumn(_paneHost, 2);

        ApplyTheme();
        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyTheme();
            RebuildDates();
        };
        RebuildDates();
    }

    // 弹层不随卡片底色 仍按窗口主题
    private bool IsDarkNow => ThemeSense.IsDark(this);

    private void ApplyTheme()
    {
        var hairline = new SolidColorBrush(IsDarkNow
            ? AvColor.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)
            : AvColor.FromArgb(0x12, 0x00, 0x00, 0x00));
        _dateHost.BorderBrush = hairline;
        _paneHost.BorderBrush = hairline;
        _datesEmpty.Foreground = new SolidColorBrush(CardTheme.T("empty", IsDarkNow));
        _datesEmpty.Text = AppUtils.Tr("dailylog.empty");
    }

    private void RebuildDates()
    {
        _days = DailyLog.LoadDays(_kind);
        _dates.Items.Clear();
        foreach (var entry in _days)
        {
            _dates.Items.Add(BuildDateItem(entry.Day, entry.Root));
        }
        _datesEmpty.IsVisible = _days.Count == 0;
        if (_days.Count > 0)
        {
            _dates.SelectedIndex = 0;
        }
        else
        {
            _pane.Content = null;
            _emptyStack.IsVisible = true;
        }
    }

    private Control BuildDateItem(DateOnly d, JsonObject root)
    {
        var dark = IsDarkNow;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 8,
            MinHeight = 46,
            Margin = new Thickness(16, 0, 16, 0),
        };
        var day = Txt(d.ToString("MM-dd"), FontTitle, CardTheme.T("ink", dark), FontWeight.Medium, NumFont);
        var rel = Txt(DayLabel(d), FontMeta, CardTheme.T("sub", dark));
        var count = CountEntries(root.TryGetPropertyValue(_kind, out var node) ? node : null);
        var chip = new Border
        {
            Child = Txt(count.ToString(), FontMicro, CardTheme.T("sub", dark), FontWeight.Medium),
            Background = new SolidColorBrush(CardTheme.T("chip", dark)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 1, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(day, 0);
        Grid.SetColumn(rel, 1);
        Grid.SetColumn(chip, 3);
        grid.Children.Add(day);
        grid.Children.Add(rel);
        grid.Children.Add(chip);
        return grid;
    }

    // 相对日
    private static string DayLabel(DateOnly d)
    {
        var today = DateOnly.FromDateTime(AppUtils.PreciseNow());
        if (d == today)
        {
            return AppUtils.Tr("dailylog.today");
        }
        if (d == today.AddDays(-1))
        {
            return AppUtils.Tr("dailylog.yesterday");
        }
        return AppUtils.Tr(AppUtils.WeekdayKey(d.DayOfWeek));
    }

    private static int CountEntries(JsonNode? payload)
    {
        if (payload is JsonArray an)
        {
            return an.Count;
        }
        if (payload is JsonObject obj && obj["sections"] is JsonArray secs)
        {
            var n = 0;
            foreach (var s in secs)
            {
                if (s is JsonObject sec && sec["items"] is JsonArray items)
                {
                    n += items.Count;
                }
            }
            return n;
        }
        return 0;
    }

    private void ShowSelected()
    {
        var index = _dates.SelectedIndex;
        if (index < 0 || index >= _days.Count)
        {
            return;
        }
        var (date, root) = _days[index];
        var payload = root.TryGetPropertyValue(_kind, out var node) ? node : null;
        _pane.Content = payload is null
            ? null
            : _kind.StartsWith(DailyLog.HomeworkPrefix, StringComparison.Ordinal)
                ? RenderHomework(date, payload)
                : RenderAnnouncements(date, payload);
        _emptyStack.IsVisible = payload is null;
    }

    private Control MakeHeader(DateOnly date, int count, string unit)
    {
        var dark = IsDarkNow;
        var head = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 12,
            Margin = new Thickness(22, 20, 22, 14),
        };
        var bar = new Border
        {
            Width = 3,
            Height = 34,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Common.ParseAccentColor(Config.ThemeColor.Value)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(Txt(date.ToString("yyyy-MM-dd"), FontTitle, CardTheme.T("ink", dark), FontWeight.Medium, NumFont));
        title.Children.Add(Txt(DayLabel(date), FontMeta, CardTheme.T("sub", dark)));
        stack.Children.Add(title);
        stack.Children.Add(Txt(AppUtils.Tr("dailylog.count", ("count", count), ("unit", unit)), FontMeta, CardTheme.T("sub", dark)));
        Grid.SetColumn(bar, 0);
        Grid.SetColumn(stack, 1);
        head.Children.Add(bar);
        head.Children.Add(stack);
        return head;
    }

    private Border MakeRule()
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(CardTheme.T("line", IsDarkNow)),
        };
    }

    private Control RenderHomework(DateOnly date, JsonNode payload)
    {
        var dark = IsDarkNow;
        var root = new StackPanel();
        var sections = payload is JsonObject obj && obj["sections"] is JsonArray arr ? arr : new JsonArray();
        var total = 0;
        foreach (var s in sections)
        {
            if (s is JsonObject sec && sec["items"] is JsonArray its)
            {
                total += its.Count;
            }
        }
        root.Children.Add(MakeHeader(date, total, AppUtils.Tr("dailylog.unit_item")));
        root.Children.Add(MakeRule());

        var columns = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,16,*"),
            Margin = new Thickness(20, 14, 20, 18),
        };
        var col0 = new StackPanel { Spacing = 18 };
        var col1 = new StackPanel { Spacing = 18 };
        double h0 = 0, h1 = 0;
        for (var si = 0; si < sections.Count; si++)
        {
            if (sections[si] is not JsonObject sec)
            {
                continue;
            }
            var items = sec["items"] as JsonArray ?? new JsonArray();
            var est = 28 + items.Count * LineHeight;
            if (h0 <= h1)
            {
                h0 += est;
                col0.Children.Add(BuildSectionCard(si, sec, dark));
            }
            else
            {
                h1 += est;
                col1.Children.Add(BuildSectionCard(si, sec, dark));
            }
        }
        Grid.SetColumn(col0, 0);
        Grid.SetColumn(col1, 2);
        columns.Children.Add(col0);
        columns.Children.Add(col1);
        root.Children.Add(columns);
        return root;
    }

    private Control BuildSectionCard(int si, JsonObject sec, bool dark)
    {
        var color = Palette[si % Palette.Length];
        var items = sec["items"] as JsonArray ?? new JsonArray();
        var doneCount = items.Count(o => o is JsonObject o2 && o2["done"]?.GetValue<bool>() == true);

        var card = new StackPanel { Spacing = 8 };
        var head = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"),
            ColumnSpacing = 7,
        };
        var dot = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var title = Txt(sec["title"]?.GetValue<string>() ?? "", FontBody, CardTheme.T("ink", dark), FontWeight.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var counter = Txt($"{doneCount}/{items.Count}", FontMicro, CardTheme.T("sub", dark), FontWeight.Medium, NumFont);
        Grid.SetColumn(dot, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(counter, 2);
        head.Children.Add(dot);
        head.Children.Add(title);
        head.Children.Add(counter);
        card.Children.Add(head);

        foreach (var it in items)
        {
            if (it is not JsonObject item)
            {
                continue;
            }
            var done = item["done"]?.GetValue<bool>() == true;
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 10,
                Margin = new Thickness(16, 3, 0, 3),
            };
            var box = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1.2),
                BorderBrush = new SolidColorBrush(done ? color : CardTheme.T("dotbd", dark)),
                Background = done ? new SolidColorBrush(color) : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0),
            };
            if (done)
            {
                var mark = FAIcons.IconGlyph("E73E", 9);
                mark.Foreground = Brushes.White;
                mark.HorizontalAlignment = HorizontalAlignment.Center;
                mark.VerticalAlignment = VerticalAlignment.Center;
                box.Child = mark;
            }
            var text = Txt(item["text"]?.GetValue<string>() ?? "", FontBody, CardTheme.T("ink", dark));
            text.TextWrapping = TextWrapping.Wrap;
            text.VerticalAlignment = VerticalAlignment.Top;
            text.LineHeight = LineHeight;
            text.Opacity = done ? 0.5 : 1;
            Grid.SetColumn(box, 0);
            Grid.SetColumn(text, 1);
            row.Children.Add(box);
            row.Children.Add(text);
            card.Children.Add(row);
        }
        return card;
    }

    private Control RenderAnnouncements(DateOnly date, JsonNode payload)
    {
        var dark = IsDarkNow;
        var root = new StackPanel();
        var arr = payload as JsonArray ?? new JsonArray();
        root.Children.Add(MakeHeader(date, arr.Count, AppUtils.Tr("dailylog.unit_item")));
        root.Children.Add(MakeRule());

        var list = new StackPanel { Margin = new Thickness(20, 4, 20, 18) };
        var rows = new List<Control>();
        foreach (var node in arr)
        {
            if (node is not JsonObject item)
            {
                continue;
            }
            var created = item["created"]?.GetValue<string>() ?? "";
            var text = item["text"]?.GetValue<string>() ?? "";
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 12,
                Margin = new Thickness(0, 10, 0, 10),
            };
            // 胶囊齐首行 高=行高
            var chip = new Border
            {
                Child = Txt(created.Length >= 16 ? created[11..16] : created, FontMicro,
                    CardTheme.T("sub", dark), FontWeight.Medium, NumFont),
                Background = new SolidColorBrush(CardTheme.T("chip", dark)),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 0),
                Height = LineHeight,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var body = Txt(text, FontBody, CardTheme.T("ink", dark));
            body.TextWrapping = TextWrapping.Wrap;
            body.VerticalAlignment = VerticalAlignment.Top;
            body.LineHeight = LineHeight;
            Grid.SetColumn(chip, 0);
            Grid.SetColumn(body, 1);
            row.Children.Add(chip);
            row.Children.Add(body);
            rows.Add(row);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            list.Children.Add(rows[i]);
            if (i < rows.Count - 1)
            {
                list.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(CardTheme.T("line", dark)),
                });
            }
        }
        root.Children.Add(list);
        return root;
    }
}

/// <summary>
/// 公告栏
/// 顶栏(标题 20px w700 + 计数胶囊 + ＋添加 + 两步确认清空) + 列表(点标记/正文 18px/时间 13px/
/// 24h 内最新条 accent 左边线+新徽标/悬停显编辑删除) + 内联输入(Enter 提交 Esc 取消)
/// 持久化 Announcements 服务
/// </summary>
public sealed class NativeAnnouncementWidget : WidgetCardBase
{
    private readonly TextBlock _title = new();
    private readonly TextBlock _countChip = new();
    private readonly Border _countBorder = new();
    private readonly Border _addBtn = new();
    private readonly Border _clearBtn = new();
    private readonly TextBlock _clearText = new();
    private readonly CardEmptyState _emptyView = new(
        FAIcons.IconGlyph("E789"),
        AppUtils.Tr("announcement.empty"),
        AppUtils.Tr("announcement.empty_hint"));
    private readonly StackPanel _list = new();
    private List<Announcement> _items = new();
    private string _lastSerialized = "";
    private bool _clearArmed;
    private readonly DispatcherTimer _clearTimer;
    private bool _suppressExternal;

    public NativeAnnouncementWidget(ComponentDefinition definition) : base(definition)
    {
        _clearTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
        _clearTimer.Tick += (_, _) =>
        {
            _clearTimer.Stop();
            _clearArmed = false;
            _clearText.Text = AppUtils.Tr("announcement.clear");
            _clearBtn.Background = new SolidColorBrush(CardTheme.T("chip", IsDarkNow));
        };

        BuildLayout();
        _items = Announcements.Load();
        ActualThemeVariantChanged += (_, _) => Render();
        Announcements.Changed += OnExternalChanged;
        Render();
        Log.Debug($"[AN] 公告栏组件初始化(原生) id={ComponentId}");
    }

    private bool IsDarkNow => CardIsDark;

    private void BuildLayout()
    {
        _title.Text = AppUtils.Tr("announcement.title");
        _title.FontSize = 14;
        _title.FontWeight = FontWeight.SemiBold;
        _title.VerticalAlignment = VerticalAlignment.Center;

        _countBorder.Child = _countChip;
        _countChip.FontSize = 14;
        _countChip.FontWeight = FontWeight.SemiBold;
        _countBorder.Padding = new Thickness(10, 2);
        _countBorder.CornerRadius = new CornerRadius(10);
        _countBorder.VerticalAlignment = VerticalAlignment.Center;

        _addBtn.Child = new TextBlock { Text = "+ " + AppUtils.Tr("announcement.add"), FontSize = 14, FontWeight = FontWeight.SemiBold };
        _addBtn.Padding = new Thickness(12, 4);
        _addBtn.CornerRadius = new CornerRadius(6);
        _addBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _addBtn.VerticalAlignment = VerticalAlignment.Center;
        _addBtn.PointerEntered += (_, _) =>
        {
            var a = NativeTheme.Accent();
            _addBtn.Background = new SolidColorBrush(Color.FromArgb(33, a.R, a.G, a.B));
            ((TextBlock)_addBtn.Child!).Foreground = new SolidColorBrush(a);
        };
        _addBtn.PointerExited += (_, _) =>
        {
            _addBtn.Background = new SolidColorBrush(CardTheme.T("chip", IsDarkNow));
            ((TextBlock)_addBtn.Child!).Foreground = new SolidColorBrush(CardTheme.T("sub", IsDarkNow));
        };
        _addBtn.PointerReleased += (_, _) => StartAdd();

        _clearBtn.Child = _clearText;
        _clearText.Text = AppUtils.Tr("announcement.clear");
        _clearText.FontSize = 14;
        _clearText.FontWeight = FontWeight.SemiBold;
        _clearBtn.Padding = new Thickness(12, 4);
        _clearBtn.CornerRadius = new CornerRadius(6);
        _clearBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _clearBtn.VerticalAlignment = VerticalAlignment.Center;
        _clearBtn.PointerReleased += (_, _) => OnClearClicked();

        var topGrid = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,*,Auto,Auto,Auto") };
        topGrid.ColumnSpacing = 10;
        var anHistoryBtn = CardTheme.TagButton(AppUtils.Tr("announcement.history"), 14, IsDarkNow);
        anHistoryBtn.PointerReleased += (_, _) => ComponentExpander.Show(this, AppUtils.Tr("announcement.history"),
            DailyLogPanel.ForAnnouncements());
        Grid.SetColumn(_title, 0);
        Grid.SetColumn(_countBorder, 1);
        Grid.SetColumn(anHistoryBtn, 3);
        Grid.SetColumn(_addBtn, 4);
        Grid.SetColumn(_clearBtn, 5);
        topGrid.Children.Add(_title);
        topGrid.Children.Add(_countBorder);
        topGrid.Children.Add(anHistoryBtn);
        topGrid.Children.Add(_addBtn);
        topGrid.Children.Add(_clearBtn);

        var scroll = new ScrollViewer
        {
            Content = _list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var body = new Grid();
        body.Children.Add(scroll);
        body.Children.Add(_emptyView);

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            Margin = new Thickness(12, 2, 12, 10),
        };
        Grid.SetRow(topGrid, 0);
        Grid.SetRow(body, 1);
        root.Children.Add(topGrid);
        root.Children.Add(body);
        Card.Child = root;
        Content = Card;
    }

    private static string NowStr() => DateTime.Now.ToString("yyyy-MM-dd HH:mm");

    // 原版 fmtTime: 当年 MM-DD HH:mm 跨年完整
    private static string FmtTime(string? created)
    {
        if (string.IsNullOrEmpty(created))
        {
            return "";
        }
        var m = System.Text.RegularExpressions.Regex.Match(created, @"^(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2})$");
        if (!m.Success)
        {
            return created;
        }
        var now = DateTime.Now;
        var hm = m.Groups[4].Value + ":" + m.Groups[5].Value;
        if (m.Groups[1].Value != now.Year.ToString())
        {
            return $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value} {hm}";
        }
        return $"{m.Groups[2].Value}-{m.Groups[3].Value} {hm}";
    }

    // 原版 isFresh: 24h 内
    private static bool IsFresh(string? created)
    {
        var m = System.Text.RegularExpressions.Regex.Match(created ?? "", @"^(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2})$");
        if (!m.Success)
        {
            return false;
        }
        var d = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
            int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), 0);
        return DateTime.Now - d < TimeSpan.FromHours(24);
    }

    private void Commit()
    {
        _suppressExternal = true;
        _lastSerialized = System.Text.Json.JsonSerializer.Serialize(
            _items.Select(a => new { text = a.Text, created = a.Created }));
        Announcements.Save(_items);
        _suppressExternal = false;
    }

    private void OnExternalChanged()
    {
        if (_suppressExternal)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            var json = System.Text.Json.JsonSerializer.Serialize(
                Announcements.Load().Select(a => new { text = a.Text, created = a.Created }));
            if (json == _lastSerialized)
            {
                return;
            }
            _items = Announcements.Load();
            Render();
        });
    }

    private void OnClearClicked()
    {
        if (_items.Count == 0)
        {
            return;
        }
        if (!_clearArmed)
        {
            _clearArmed = true;
            _clearText.Text = AppUtils.Tr("announcement.confirm_clear");
            _clearBtn.Background = new SolidColorBrush(Color.FromArgb(230, 229, 72, 77));
            _clearText.Foreground = Brushes.White;
            _clearTimer.Stop();
            _clearTimer.Start();
            return;
        }
        _clearArmed = false;
        _clearTimer.Stop();
        _clearText.Text = AppUtils.Tr("announcement.clear");
        _items.Clear();
        Commit();
        Render();
    }

    private TextBox MakeInlineInput(string? initial, string placeholder, double fontSize)
    {
        var input = new TextBox
        {
            Text = initial ?? "",
            PlaceholderText = placeholder,
            FontSize = fontSize,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(CardTheme.T("chip", IsDarkNow)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 8),
            AcceptsReturn = false,
        };
        input.Foreground = new SolidColorBrush(CardTheme.T("ink", IsDarkNow));
        return input;
    }

    private void StartAdd()
    {
        _addBtn.IsVisible = false;
        var underline = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = new SolidColorBrush(NativeTheme.Accent()),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = MakeInlineInput(null, AppUtils.Tr("announcement.item_ph"), 17),
        };
        var input = (TextBox)underline.Child!;
        var finished = false;
        var finish = (bool save) =>
        {
            if (finished)
            {
                return;
            }
            finished = true;
            _addBtn.IsVisible = true;
            var v = input.Text?.Trim() ?? "";
            if (save && v.Length > 0)
            {
                _items.Insert(0, new Announcement(v, NowStr()));
                Commit();
            }
            Render();
        };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                finish(true);
            }
            else if (e.Key == Key.Escape)
            {
                finish(false);
            }
        };
        input.LostFocus += (_, _) => finish(true);
        _list.Children.Insert(0, underline);
        input.Focus();
    }

    private void StartEdit(int index, Border? oldHost)
    {
        var it = _items[index];
        var finished = false;
        var underline = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = new SolidColorBrush(NativeTheme.Accent()),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = MakeInlineInput(it.Text, AppUtils.Tr("announcement.item_ph"), 17),
        };
        var input = (TextBox)underline.Child!;
        var finish = (bool save) =>
        {
            if (finished)
            {
                return;
            }
            finished = true;
            var v = input.Text?.Trim() ?? "";
            if (save && v.Length > 0 && v != it.Text)
            {
                _items[index] = it with { Text = v };
                Commit();
            }
            Render();
        };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                finish(true);
            }
            else if (e.Key == Key.Escape)
            {
                finish(false);
            }
        };
        input.LostFocus += (_, _) => finish(true);
        if (oldHost is not null && _list.Children.Contains(oldHost))
        {
            var pos = _list.Children.IndexOf(oldHost);
            _list.Children[pos] = underline;
        }
        else
        {
            _list.Children.Insert(0, underline);
        }
        input.Focus();
        input.SelectAll();
    }

    private void Render()
    {
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();
        var accent22 = Color.FromArgb(33, accent.R, accent.G, accent.B);
        _title.Foreground = new SolidColorBrush(CardTheme.T("ink", dark));
        _countChip.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        _countBorder.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        _countChip.Text = _items.Count > 0 ? _items.Count.ToString() : "";
        _countBorder.IsVisible = _items.Count > 0;
        _addBtn.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        ((TextBlock)_addBtn.Child!).Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        if (!_clearArmed)
        {
            _clearBtn.Background = new SolidColorBrush(CardTheme.T("chip", dark));
            _clearText.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        }
        _clearBtn.IsVisible = _items.Count > 0;
        _emptyView.Apply(WidgetSurfacePalette.Resolve(dark));
        _emptyView.IsVisible = _items.Count == 0;

        _list.Children.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var idx = i;
            var fresh = i == 0 && IsFresh(it.Created);

            var text = new TextBlock
            {
                Text = it.Text,
                FontSize = 15,
                LineHeight = 15 * 1.45,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 15 * 1.45 * 3,
                ClipToBounds = true,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            text.Foreground = new SolidColorBrush(CardTheme.T("ink", dark));
            var textHost = new Border { Child = text };
            text.DoubleTapped += (_, _) => StartEdit(idx, textHost);

            var time = new TextBlock
            {
                Text = FmtTime(it.Created),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            time.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));

            var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 5, 0, 0) };
            meta.Children.Add(time);
            if (fresh)
            {
                meta.Children.Add(new Border
                {
                    Background = new SolidColorBrush(accent22),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = AppUtils.Tr("announcement.new"),
                        FontSize = 12,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(accent),
                    },
                });
            }
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 0, 0), Opacity = 0.45 };
            var editBtn = CardTheme.IconButton("✎", 24, false, () => IsDarkNow);
            editBtn.PointerReleased += (_, _) => StartEdit(idx, textHost);
            var delBtn = CardTheme.IconButton("×", 24, false, () => IsDarkNow);
            delBtn.PointerReleased += (_, _) =>
            {
                _items.RemoveAt(idx);
                Commit();
                Render();
            };
            // 悬停整卡时按钮全显
            btns.Children.Add(editBtn);
            btns.Children.Add(delBtn);
            meta.Children.Add(btns);

            var body = new StackPanel();
            body.Children.Add(textHost);
            body.Children.Add(meta);

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(accent),
                Margin = new Thickness(0, 9, 0, 0),
            };

            var itemGrid = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*") };
            itemGrid.ColumnSpacing = 10;
            Grid.SetColumn(dot, 0);
            Grid.SetColumn(body, 1);
            itemGrid.Children.Add(dot);
            itemGrid.Children.Add(body);

            // 平面列表: 透明底 最新条 accent 左缘 条目间 1px 分隔线
            var itemBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = fresh ? CardAccent.Brush() : new SolidColorBrush(CardTheme.T("line", dark)),
                BorderThickness = fresh ? new Thickness(2, 0, 0, 0) : new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(fresh ? 10 : 0, 9, 0, 9),
                Margin = new Thickness(0, 0, 0, 0),
                Child = itemGrid,
            };
            itemBorder.PointerEntered += (_, _) => btns.Opacity = 1;
            itemBorder.PointerExited += (_, _) => btns.Opacity = 0.45;
            _list.Children.Add(itemBorder);
        }
        InvalidateVisual();
    }
}

/// <summary>
/// 作业板
/// 顶栏(进度 d/n + 72x3 进度条 + ＋添加科目 + 两步确认清除已完成) + 双栏科目卡
/// (色点/标题双击改名/计数/两步确认删除/条目圆形勾选/划线/内联增改) 400ms 防抖持久化
/// </summary>
public sealed class NativeHomeworkBoardWidget : WidgetCardBase
{

    private static readonly Color[] Palette =
    {
        NativeTheme.Parse("#f59e0b"), NativeTheme.Parse("#3b82f6"), NativeTheme.Parse("#10b981"), NativeTheme.Parse("#ef4444"),
        NativeTheme.Parse("#8b5cf6"), NativeTheme.Parse("#ec4899"), NativeTheme.Parse("#06b6d4"), NativeTheme.Parse("#84cc16"),
    };

    private readonly TextBlock _progText = new();
    private readonly ProgressBar _progBar = new();
    private readonly Border _addSubBtn = new();
    private readonly Border _clearDoneBtn = new();
    private readonly TextBlock _clearDoneText = new();
    private readonly CardEmptyState _emptyView = new(
        FAIcons.Glyph(AppIcons.Book),
        AppUtils.Tr("homework.empty_title"),
        AppUtils.Tr("homework.empty"));
    // Grid 栏: 科目少时按星号均分整列高度 免留白
    private readonly Grid _col0 = new();
    private readonly Grid _col1 = new();
    private JsonArray _sections = new();
    private string? _dataFile;
    private readonly DispatcherTimer _saveTimer;

    public NativeHomeworkBoardWidget(ComponentDefinition definition) : base(definition)
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
        BuildLayout();
        ActualThemeVariantChanged += (_, _) => Render();
        AttachedToVisualTree += (_, _) =>
        {
            if (_dataFile is null && !string.IsNullOrEmpty(ComponentId))
            {
                _dataFile = IOPath.Combine(Paths.DataUser, $"homework_{ComponentId}.json");
                _sections = LoadFrom(_dataFile);
                Render();
            }
        };
        Log.Debug("[HW] 作业板组件初始化(原生)");
    }

    private bool IsDarkNow => CardIsDark;

    private static JsonArray LoadFrom(string file)
    {
        try
        {
            if (File.Exists(file)
                && JsonNode.Parse(File.ReadAllText(file)) is JsonObject obj
                && obj["sections"] is JsonArray arr)
            {
                Log.Debug($"[HW] 作业板读取: {arr.Count}个科目");
                return (JsonArray)arr.DeepClone();
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[HW] 读取作业板失败: {e.Message}");
        }
        return new JsonArray();
    }

    private void Save()
    {
        try
        {
            if (_dataFile is null)
            {
                return;
            }
            Directory.CreateDirectory(Paths.DataUser);
            var payload = new JsonObject { ["sections"] = (_sections ?? new JsonArray()).DeepClone() };
            File.WriteAllText(_dataFile, payload.ToJsonString());
            DailyLog.Record(DailyLog.HomeworkPrefix + ComponentId, payload);
            Log.Debug($"[HW] 作业板已保存: {(_sections?.Count ?? 0)}个科目");
        }
        catch (Exception e)
        {
            Log.Warning($"[HW] 保存作业板失败: {e.Message}");
        }
    }


    private void Commit()
    {
        var clean = new JsonArray();
        foreach (var secNode in _sections!)
        {
            if (secNode is not JsonObject sec)
            {
                continue;
            }
            var title = ((string?)sec["title"] ?? "").Trim();
            if (title.Length > 30)
            {
                title = title[..30];
            }
            var items = new JsonArray();
            if (sec["items"] is JsonArray itemArr)
            {
                foreach (var itNode in itemArr)
                {
                    if (itNode is not JsonObject it)
                    {
                        continue;
                    }
                    var text = ((string?)it["text"] ?? "").Trim();
                    if (text.Length > 120)
                    {
                        text = text[..120];
                    }
                    if (text.Length > 0)
                    {
                        items.Add(new JsonObject { ["text"] = text, ["done"] = it["done"]?.GetValue<bool>() == true });
                    }
                }
            }
            if (title.Length > 0 || items.Count > 0)
            {
                clean.Add(new JsonObject { ["title"] = title, ["items"] = items });
            }
        }
        _sections = clean;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private (int Total, int Done) Progress()
    {
        var total = 0;
        var done = 0;
        foreach (var sec in _sections!)
        {
            if (sec is not JsonObject s || s["items"] is not JsonArray arr)
            {
                continue;
            }
            foreach (var it in arr)
            {
                if (it is not JsonObject o)
                {
                    continue;
                }
                total++;
                if (o["done"]?.GetValue<bool>() == true)
                {
                    done++;
                }
            }
        }
        return (total, done);
    }

    private void BuildLayout()
    {
        _progText.FontSize = 12;
        _progText.FontWeight = FontWeight.SemiBold;
        _progText.VerticalAlignment = VerticalAlignment.Center;

        _progBar.Height = 4;
        _progBar.CornerRadius = new CornerRadius(2);
        _progBar.Minimum = 0;
        _progBar.Maximum = 100;
        _progBar.Value = 0;
        _progBar.Margin = new Thickness(2, 0, 2, 6);

        _addSubBtn.Child = new TextBlock { Text = "+ " + AppUtils.Tr("homework.add_subject"), FontSize = 11.5, FontWeight = FontWeight.SemiBold };
        _addSubBtn.Padding = new Thickness(10, 3);
        _addSubBtn.CornerRadius = new CornerRadius(4);
        _addSubBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _addSubBtn.VerticalAlignment = VerticalAlignment.Center;
        _addSubBtn.PointerReleased += (_, _) =>
        {
            _sections.Add(new JsonObject { ["title"] = "", ["items"] = new JsonArray() });
            Render();
        };

        _clearDoneBtn.Child = _clearDoneText;
        _clearDoneText.Text = AppUtils.Tr("homework.clear_done");
        _clearDoneText.FontSize = 11.5;
        _clearDoneText.FontWeight = FontWeight.SemiBold;
        _clearDoneBtn.Padding = new Thickness(10, 3);
        _clearDoneBtn.CornerRadius = new CornerRadius(4);
        _clearDoneBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _clearDoneBtn.VerticalAlignment = VerticalAlignment.Center;
        _clearDoneBtn.PointerReleased += (_, _) =>
        {
            var changed = false;
            foreach (var sec in _sections!)
            {
                if (sec is JsonObject s && s["items"] is JsonArray arr)
                {
                    var keep = new JsonArray();
                    foreach (var it in arr)
                    {
                        if (it is JsonObject o && o["done"]?.GetValue<bool>() != true)
                        {
                            keep.Add(o.DeepClone());
                        }
                        else
                        {
                            changed = true;
                        }
                    }
                    s["items"] = keep;
                }
            }
            if (changed)
            {
                Commit();
                Render();
            }
        };

        var topGrid = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,*,Auto,Auto,Auto") };
        topGrid.ColumnSpacing = 8;
        var progGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        progGroup.Children.Add(_progText);
        var historyBtn = CardTheme.TagButton(AppUtils.Tr("homework.history"), 11.5, IsDarkNow);
        historyBtn.PointerReleased += (_, _) => ComponentExpander.Show(this, AppUtils.Tr("homework.history"),
            DailyLogPanel.ForHomework(ComponentId));
        Grid.SetColumn(progGroup, 0);
        Grid.SetColumn(historyBtn, 3);
        Grid.SetColumn(_addSubBtn, 4);
        Grid.SetColumn(_clearDoneBtn, 5);
        topGrid.Children.Add(progGroup);
        topGrid.Children.Add(historyBtn);
        topGrid.Children.Add(_addSubBtn);
        topGrid.Children.Add(_clearDoneBtn);
        topGrid.Margin = new Thickness(2, 3, 2, 8);

        var colsGrid = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,10,*") };
        Grid.SetColumn(_col0, 0);
        Grid.SetColumn(_col1, 2);
        colsGrid.Children.Add(_col0);
        colsGrid.Children.Add(_col1);

        var body = new Grid();
        var scroll = new ScrollViewer
        {
            Content = colsGrid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        body.Children.Add(scroll);
        body.Children.Add(_emptyView);

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,*"),
            Margin = new Thickness(12, 2, 12, 8),
            RowSpacing = 2,
        };
        Grid.SetRow(topGrid, 0);
        Grid.SetRow(_progBar, 1);
        Grid.SetRow(body, 2);
        root.Children.Add(topGrid);
        root.Children.Add(_progBar);
        root.Children.Add(body);
        Card.Child = root;
        Content = Card;
    }

    private TextBox MakeInlineInput(string? initial, string placeholder, double fontSize, bool bold = false)
    {
        var input = new TextBox
        {
            Text = initial ?? "",
            PlaceholderText = placeholder,
            FontSize = fontSize,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(CardTheme.T("chip", IsDarkNow)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(7, 2.5),
            MinHeight = 0,
        };
        input.Foreground = new SolidColorBrush(CardTheme.T("ink", IsDarkNow));
        return input;
    }

    // host 可为 Panel(StackPanel) 或 Border(文本宿主) 统一替换为下划线输入行
    private static void ReplaceHost(Control host, Control newChild)
    {
        switch (host)
        {
            case Panel p:
                p.Children.Clear();
                p.Children.Add(newChild);
                break;
            case Border b:
                b.Child = newChild;
                break;
        }
    }

    private TextBox WrapInlineInput(string? initial, string placeholder, double fontSize, Control host, bool bold = false)
    {
        var underline = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1.5),
            BorderBrush = new SolidColorBrush(NativeTheme.Accent()),
            Child = MakeInlineInput(initial, placeholder, fontSize, bold),
        };
        ReplaceHost(host, underline);
        var input = (TextBox)underline.Child!;
        input.Focus();
        if (!string.IsNullOrEmpty(initial))
        {
            input.SelectAll();
        }
        return input;
    }

    private void Render()
    {
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();
        _progText.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        _progText.Text = "";
        _progBar.Foreground = new SolidColorBrush(accent);
        _progBar.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        _addSubBtn.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        ((TextBlock)_addSubBtn.Child!).Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        _clearDoneBtn.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        _clearDoneText.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        _emptyView.Apply(WidgetSurfacePalette.Resolve(dark));

        var (total, done) = Progress();
        _progText.Text = $"{done}/{total}";
        _progBar.Value = total > 0 ? done * 100.0 / total : 0;
        _clearDoneBtn.IsVisible = done > 0;
        _emptyView.IsVisible = _sections.Count == 0;

        _col0.Children.Clear();
        _col1.Children.Clear();
        _col0.RowDefinitions.Clear();
        _col1.RowDefinitions.Clear();

        // 单科目横跨整卡 免右半留白
        var single = _sections.Count == 1;
        Grid.SetColumnSpan(_col0, single ? 3 : 1);

        // 双栏平衡分配: 按估算高 落列
        var h0 = 0.0;
        var h1 = 0.0;
        for (var si = 0; si < _sections.Count; si++)
        {
            if (_sections[si] is not JsonObject sec)
            {
                continue;
            }
            var itemCount = sec["items"] is JsonArray arr ? arr.Count : 0;
            var est = 30 + itemCount * 22;
            var target = single || h0 <= h1 ? _col0 : _col1;
            if (target == _col0)
            {
                h0 += est;
            }
            else
            {
                h1 += est;
            }
            target.Children.Add(BuildSection(si, sec));
        }

        // 该栏只放一个科目 -> 撑满列高; 多个则按内容高 由 ScrollViewer 接管
        FitColumn(_col0);
        FitColumn(_col1);
        InvalidateVisual();
    }

    // 单科目列 星号均分; 多科目 定量高
    private static void FitColumn(Grid col)
    {
        col.RowDefinitions.Clear();
        if (col.Children.Count == 0)
        {
            return;
        }
        var stretch = col.Children.Count == 1;
        for (var i = 0; i < col.Children.Count; i++)
        {
            col.RowDefinitions.Add(new RowDefinition(stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto));
            Grid.SetRow(col.Children[i], i);
        }
    }

    private Border BuildSection(int si, JsonObject sec)
    {
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();
        var color = Palette[si % Palette.Length];
        var halo = Color.FromArgb(0x22, color.R, color.G, color.B);

        var titleText = new TextBlock
        {
            Text = (string?)sec["title"] ?? "",
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        titleText.Foreground = new SolidColorBrush(CardTheme.T("ink", dark));
        var titleHost = new Border { Child = titleText, MinWidth = 30 };
        titleText.DoubleTapped += (_, _) =>
        {
            var finished = false;
            var input = WrapInlineInput((string?)sec["title"], AppUtils.Tr("homework.subject_ph"), 12.5, titleHost, bold: true);
            var finish = (bool save) =>
            {
                if (finished)
                {
                    return;
                }
                finished = true;
                var v = input.Text?.Trim() ?? "";
                if (save && v.Length > 0 && v != (string?)sec["title"])
                {
                    sec["title"] = v;
                    Commit();
                }
                Render();
            };
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    finish(true);
                }
                else if (e.Key == Key.Escape)
                {
                    finish(false);
                }
            };
            input.LostFocus += (_, _) => finish(true);
        };

        var items = sec["items"] as JsonArray ?? new JsonArray();
        var doneCount = items.Count(o => o is JsonObject o2 && o2["done"]?.GetValue<bool>() == true);
        var countText = new TextBlock
        {
            Text = items.Count > 0 ? $"{doneCount}/{items.Count}" : "",
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        countText.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));

        // 两步确认删除科目
        var delText = new TextBlock { Text = "×", FontSize = 11, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center };
        var delBtn = new Border
        {
            Child = delText,
            Padding = new Thickness(7, 1),
            CornerRadius = new CornerRadius(4),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
        };
        delText.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        var armed = false;
        DispatcherTimer? armTimer = null;
        delBtn.PointerEntered += (_, _) => delBtn.Opacity = 1;
        delBtn.PointerExited += (_, _) => { if (!armed) { delBtn.Opacity = 0; } };
        delBtn.PointerReleased += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                delBtn.Opacity = 1;
                delText.Text = AppUtils.Tr("homework.confirm_del");
                delBtn.Background = new SolidColorBrush(Color.FromArgb(230, 229, 72, 77));
                delText.Foreground = Brushes.White;
                armTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
                armTimer.Tick += (_, _) =>
                {
                    armTimer?.Stop();
                    armed = false;
                    delText.Text = "×";
                    delBtn.Background = null;
                    delText.Foreground = new SolidColorBrush(CardTheme.T("sub", IsDarkNow));
                };
                armTimer.Start();
            }
            else
            {
                armTimer?.Stop();
                _sections.RemoveAt(si);
                Commit();
                Render();
            }
        };

        var head = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,Auto,*,Auto") };
        head.ColumnSpacing = 7;
        var dotHalo = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(halo),
            Child = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(dotHalo, 0);
        Grid.SetColumn(titleHost, 1);
        Grid.SetColumn(countText, 2);
        Grid.SetColumn(delBtn, 4);
        head.Children.Add(dotHalo);
        head.Children.Add(titleHost);
        head.Children.Add(countText);
        head.Children.Add(delBtn);
        head.Margin = new Thickness(1, 0, 1, 5);

        // 三段: 标题顶格 / 条目居中吸收余高 / 添加行贴底
        var itemsHost = new StackPanel
        {
            VerticalAlignment = _sections.Count == 1 ? VerticalAlignment.Center : VerticalAlignment.Top,
        };
        for (var ii = 0; ii < items.Count; ii++)
        {
            if (items[ii] is JsonObject it)
            {
                itemsHost.Children.Add(BuildItemRow(si, ii, it));
            }
        }

        // 添加条目行
        var addRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Margin = new Thickness(3, 3, 3, 3),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        addRow.Children.Add(new TextBlock { Text = "+", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.55 });
        addRow.Children.Add(new TextBlock { Text = AppUtils.Tr("homework.add_item"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
        addRow.PointerReleased += (_, _) =>
        {
            var finished = false;
            var input = WrapInlineInput(null, AppUtils.Tr("homework.add_item"), 12, addRow);
            var finish = (bool save) =>
            {
                if (finished)
                {
                    return;
                }
                finished = true;
                var v = input.Text?.Trim() ?? "";
                if (save && v.Length > 0)
                {
                    ((sec["items"] ??= new JsonArray()) as JsonArray)!.Add(new JsonObject { ["text"] = v, ["done"] = false });
                    Commit();
                }
                Render();
            };
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    finish(true);
                }
                else if (e.Key == Key.Escape)
                {
                    finish(false);
                }
            };
            input.LostFocus += (_, _) => finish(true);
        };

        var body = new Grid { RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto") };
        Grid.SetRow(head, 0);
        Grid.SetRow(itemsHost, 1);
        Grid.SetRow(addRow, 2);
        body.Children.Add(head);
        body.Children.Add(itemsHost);
        body.Children.Add(addRow);

        // 平面分组: 色点+标题即分组 科目间 1px 分隔线
        var secBorder = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(CardTheme.T("line", dark)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(0, 8, 0, 8),
            Margin = new Thickness(2, 0, 2, 6),
            Child = body,
        };
        secBorder.PointerEntered += (_, _) => delBtn.Opacity = 1;
        secBorder.PointerExited += (_, _) => { if (!armed) { delBtn.Opacity = 0; } };
        return secBorder;
    }

    private Border BuildItemRow(int si, int ii, JsonObject it)
    {
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();
        var done = it["done"]?.GetValue<bool>() == true;
        var text = (string?)it["text"] ?? "";

        var row = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 0),
        };

        // 勾选圆 16x16 rx4.4
        var circle = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4.4),
            BorderThickness = new Thickness(1.4),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = "✓",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.White,
                IsVisible = done,
            },
        };
        circle.Background = done ? new SolidColorBrush(accent) : null;
        circle.BorderBrush = done ? new SolidColorBrush(accent) : new SolidColorBrush(CardTheme.T("dotbd", dark));
        circle.VerticalAlignment = VerticalAlignment.Center;
        circle.PointerReleased += (_, _) =>
        {
            it["done"] = !(it["done"]?.GetValue<bool>() ?? false);
            Commit();
            Render();
        };

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 12,
            LineHeight = 12 * 1.45,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        textBlock.Foreground = done
            ? new SolidColorBrush(CardTheme.T("sub", dark), 0.6)
            : new SolidColorBrush(CardTheme.T("ink", dark));
        if (done)
        {
            textBlock.TextDecorations = TextDecorations.Strikethrough;
        }
        var textHost = new Border { Child = textBlock };
        textBlock.DoubleTapped += (_, _) =>
        {
            var finished = false;
            var input = WrapInlineInput(text, AppUtils.Tr("homework.add_item"), 12, textHost);
            var finish = (bool save) =>
            {
                if (finished)
                {
                    return;
                }
                finished = true;
                var v = input.Text?.Trim() ?? "";
                if (save && v.Length > 0 && v != text)
                {
                    it["text"] = v;
                    Commit();
                }
                Render();
            };
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    finish(true);
                }
                else if (e.Key == Key.Escape)
                {
                    finish(false);
                }
            };
            input.LostFocus += (_, _) => finish(true);
        };

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Opacity = 0 };
        var editBtn = CardTheme.IconButton("✎", 18, false, () => IsDarkNow);
        editBtn.PointerReleased += (_, _) =>
        {
            textBlock.RaiseEvent(new RoutedEventArgs(Control.DoubleTappedEvent));
        };
        var delBtn = CardTheme.IconButton("×", 18, false, () => IsDarkNow);
        delBtn.PointerReleased += (_, _) =>
        {
            if (_sections[si] is JsonObject s && s["items"] is JsonArray arr)
            {
                arr.RemoveAt(ii);
                Commit();
                Render();
            }
        };
        btns.Children.Add(editBtn);
        btns.Children.Add(delBtn);

        Grid.SetColumn(circle, 0);
        Grid.SetColumn(textHost, 1);
        Grid.SetColumn(btns, 2);
        row.Children.Add(circle);
        row.Children.Add(textHost);
        row.Children.Add(btns);

        var rowBorder = new Border
        {
            Child = row,
            Padding = new Thickness(3, 3, 5, 3),
            CornerRadius = new CornerRadius(5),
        };
        rowBorder.PointerEntered += (_, _) =>
        {
            rowBorder.Background = new SolidColorBrush(CardTheme.T("hover", dark));
            btns.Opacity = 1;
        };
        rowBorder.PointerExited += (_, _) =>
        {
            rowBorder.Background = null;
            btns.Opacity = 0;
        };
        return rowBorder;
    }
}

/// <summary>
/// 课程时间轴
/// SPACING=120 PAD_L=60 PAD_R=95 轨道 3px@58% 当前节点渐变胶囊+呼吸/课间/放学/倒计时
/// 状态机 compute() 逐字: before/in class/in break/done 自动居中跟随
/// </summary>
public sealed class NativeTimetableTimelineWidget : WidgetCardBase
{
    private const double Spacing = 120;
    private const double PadL = 60;
    private const double PadR = 95;
    private const double NameMaxW = 118;

    private List<Dictionary<string, object?>> _nodes = new();
    private readonly TimelineCanvas _canvas;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _renderTimer;
    private readonly CardEmptyState _emptyView = new(
        FAIcons.Glyph(AppIcons.Clock),
        AppUtils.Tr("timetable.day_empty"),
        AppUtils.Tr("component.click_to_settings"));

    public NativeTimetableTimelineWidget(ComponentDefinition definition) : base(definition)
    {
        _canvas = new TimelineCanvas(this);
        _emptyView.Apply(WidgetSurfacePalette.Resolve(CardIsDark));

        var host = new Border
        {
            Child = _canvas,
            // 两端 26px 渐隐(
            OpacityMask = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Colors.Black, 0.08),
                    new GradientStop(Colors.Black, 0.92),
                    new GradientStop(Colors.Transparent, 1),
                },
            },
        };
        var root = new Grid();
        root.Children.Add(host);
        root.Children.Add(_emptyView);
        Card.Child = root;
        Content = Card;
        ActualThemeVariantChanged += (_, _) =>
        {
            _emptyView.Apply(WidgetSurfacePalette.Resolve(CardIsDark));
            _canvas.InvalidateVisual();
        };

        // 原版 1s 起步 空数据指数退避到 30s
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();

        // 渲染驱动(倒计时逐秒)
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _renderTimer.Tick += (_, _) => _canvas.InvalidateVisual();
        _renderTimer.Start();
        Log.Debug("[TTT] 时间轴组件初始化(原生)");
    }

    private void Refresh()
    {
        var nodes = TimetableScheduleProvider.BuildTimelineNodes();
        _nodes = nodes;
        // 空态提示必须在渲染过程外切换: Render 内改 IsVisible 会触发渲染期失效异常(crash)
        _emptyView.IsVisible = nodes.Count == 0;
        _canvas.InvalidateVisual();
        if (nodes.Count == 0)
        {
            var seconds = Math.Min(Math.Max(_refreshTimer.Interval.TotalSeconds, 0.5) * 2, 30);
            _refreshTimer.Interval = TimeSpan.FromSeconds(seconds);
            return;
        }
        _refreshTimer.Interval = TimeSpan.FromSeconds(30);
        Log.Debug($"[TTT] 时间线刷新: 获取到{nodes.Count}个节点");
    }

    private sealed class TimelineCanvas : Control
    {
        private readonly NativeTimetableTimelineWidget _owner;
        private double _offset;

        public TimelineCanvas(NativeTimetableTimelineWidget owner)
        {
            _owner = owner;
        }

        protected override Size MeasureOverride(Size availableSize) => new(
            double.IsFinite(availableSize.Width) ? availableSize.Width : 400,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 200);

        private static int Pt(string s)
        {
            var p = s.Split(':');
            return (int.Parse(p[0])) * 3600 + (int.Parse(p[1])) * 60;
        }

        private static double NodeX(int i) => PadL + i * Spacing;

        private (int Idx, string Phase, double Prog, bool Before, bool Done) Compute()
        {
            var nodes = _owner._nodes;
            if (nodes.Count == 0)
            {
                return (-1, "", 0, false, false);
            }
            var now = DateTime.Now;
            var t = now.Hour * 3600 + now.Minute * 60 + now.Second;
            var last = nodes.Count - 1;
            if (t < Pt((string)nodes[0]["start"]!))
            {
                return (0, "class", 0, true, false);
            }
            for (var i = 0; i < nodes.Count; i++)
            {
                var s = Pt((string)nodes[i]["start"]!);
                var e = Pt((string)nodes[i]["end"]!);
                if (t >= s && t < e)
                {
                    return (i, "class", (double)(t - s) / (e - s), false, false);
                }
                if (nodes[i]["break"] is Dictionary<string, object?> b)
                {
                    var bs = Pt((string)b["start"]!);
                    var be = Pt((string)b["end"]!);
                    if (t >= bs && t < be)
                    {
                        return (i, "break", (double)(t - bs) / (be - bs), false, false);
                    }
                }
            }
            var lastEnd = nodes[last]["break"] is Dictionary<string, object?> lb
                ? Pt((string)lb["end"]!)
                : Pt((string)nodes[last]["end"]!);
            if (t >= lastEnd)
            {
                return (last, "done", 1, false, true);
            }
            for (var k = last; k >= 0; k--)
            {
                if (t >= Pt((string)nodes[k]["start"]!))
                {
                    return (k, nodes[k]["break"] is not null && t < Pt((string)((Dictionary<string, object?>)nodes[k]["break"]!)["end"]!) ? "break" : "class", 1, false, false);
                }
            }
            return (0, "class", 0, true, false);
        }

        private static string Fmt(int sec)
        {
            var m = sec / 60;
            var s = sec % 60;
            return m + ":" + (s < 10 ? "0" : "") + s;
        }

        private static FormattedText Ft(string text, double size, FontWeight weight, IBrush brush)
        {
            return new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(Common.AppFontFamily, FontStyle.Normal, weight), size, brush);
        }

        public override void Render(DrawingContext ctx)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 10 || h < 10)
            {
                return;
            }
            // 自绘取色跟随卡片底色 非窗口主题
            var dark = _owner.CardIsDark;
            var nodes = _owner._nodes;
            var st = Compute();
            if (nodes.Count == 0)
            {
                return;
            }

            var accent = NativeTheme.Accent();
            var accent2 = NativeTheme.Parse(dark ? "#5fd98c" : "#7ce8a4");
            var track1 = NativeTheme.Parse(dark ? "#3a3a41" : "#ededf1");
            var track2 = NativeTheme.Parse(dark ? "#323238" : "#e2e2e8");
            var inkF = NativeTheme.Parse(dark ? "#ffffff" : "#000000");
            var sub = NativeTheme.Parse(dark ? "#ffffff" : "#000000");
            var nameBg = dark ? NativeTheme.Rgba(255, 255, 255, 0.08) : NativeTheme.Parse("#f5f5f7");
            var nameBd = dark ? NativeTheme.Rgba(255, 255, 255, 0.14) : NativeTheme.Parse("#e9e9ee");
            var dotBg = dark ? NativeTheme.Rgba(255, 255, 255, 0.05) : NativeTheme.Parse("#ffffff");
            var dotBd = NativeTheme.Parse(dark ? "#52525a" : "#c6c6ce");
            var brk = NativeTheme.Parse("#00b7c3");
            var brk2 = NativeTheme.Parse(dark ? "#00d5e0" : "#fbbf24");
            var dotPast = Color.FromArgb((byte)Math.Round(0.45 * 255), accent.R, accent.G, accent.B);

            var laneW = NodeX(nodes.Count - 1) + PadR;
            var y0 = h * 0.58;
            var curIdx = st.Before ? -1 : st.Idx;

            // 自动居中(逐字): before=0 done=贴底 其余 nodeX-视口半宽
            double target;
            if (st.Before)
            {
                target = 0;
            }
            else if (st.Done)
            {
                target = Math.Max(0, laneW - w);
            }
            else
            {
                target = Math.Max(0, Math.Min(NodeX(st.Idx) - w / 2, laneW - w));
            }
            _offset = target;

            using (ctx.PushTransform(Matrix.CreateTranslation((float)-_offset, 0)))
            {
                // 轨道 3px 圆角2
                var trackRect = new Rect(0, y0 - 1.5, laneW, 3);
                var trackBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(track1, 0), new GradientStop(track2, 1) },
                };
                ctx.FillRectangle(trackBrush, trackRect, 2);

                // 进度填充
                double right;
                if (st.Before)
                {
                    right = 0;
                }
                else if (st.Done)
                {
                    right = laneW - 26;
                }
                else
                {
                    right = NodeX(st.Idx) + Math.Max(0, Math.Min(1, st.Prog)) * Spacing;
                }
                var fillW = Math.Min(right, laneW - 26);
                if (fillW > 0.5)
                {
                    var isBreakFill = st.Phase == "break" && curIdx >= 0 && !st.Done;
                    var tail = isBreakFill
                        ? NativeTheme.Rgba(0, 183, 195, dark ? 0.20 : 0.25)
                        : Color.FromArgb((byte)Math.Round(0.20 * 255), accent.R, accent.G, accent.B);
                    var head = isBreakFill ? brk : accent;
                    var fillBrush = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(tail, 0), new GradientStop(head, 1) },
                    };
                    ctx.FillRectangle(fillBrush, new Rect(0, y0 - 1.5, fillW, 3), 2);
                    // 头部白点 + 主题色光晕(w<8 隐藏 对应 .zero)
                    if (fillW >= 8)
                    {
                        var glowColor = isBreakFill ? brk : accent;
                        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, glowColor.R, glowColor.G, glowColor.B)), null,
                            new Point(fillW, y0), 9, 9);
                        ctx.DrawEllipse(Brushes.White, null, new Point(fillW, y0), 3.5, 3.5);
                    }
                }

                // 节点
                for (var i = 0; i < nodes.Count; i++)
                {
                    var n = nodes[i];
                    var x = NodeX(i);
                    var isPast = i < st.Idx || (st.Done && i < st.Idx);
                    var isCurrent = !st.Done && i == curIdx;
                    var isBreakNode = isCurrent && st.Phase == "break" && n["break"] is not null;
                    var isDoneNode = st.Done && i == st.Idx;

                    // 名称胶囊: top=y0-68 高29 圆角14.5 最大宽118
                    var nameText = isDoneNode ? "放学"
                        : isBreakNode ? "课间"
                        : (string)n["name"]!;
                    var timeText = isBreakNode
                        ? $"{((Dictionary<string, object?>)n["break"]!)["start"]}-{((Dictionary<string, object?>)n["break"]!)["end"]}"
                        : $"{n["start"]}-{n["end"]}";
                    var periodText = (string)n["period"]!;

                    var pillBrush = isPast
                        ? default(IBrush?)
                        : isCurrent
                            ? new LinearGradientBrush
                            {
                                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                                GradientStops =
                                {
                                    new GradientStop(isBreakNode ? brk : accent, 0),
                                    new GradientStop(isBreakNode ? brk2 : accent2, 1),
                                },
                            }
                            : new SolidColorBrush(nameBg);
                    // current 节点胶囊 scale(1.1) 围绕中心放大(对应 CSS .node.current .name transform)
                    var nameScale = isCurrent ? 1.1 : 1.0;
                    var nameFs = 16.0 * nameScale;
                    var nameFt = Ft(nameText, nameFs, isPast ? FontWeight.SemiBold : FontWeight.Bold,
                        new SolidColorBrush(isPast ? inkF : isCurrent ? Colors.White : inkF));
                    // 截断阈值按未缩放宽度判定(CSS max-width:118 作用于 scale 之前)
                    if (nameFt.WidthIncludingTrailingWhitespace / nameScale > NameMaxW - 30)
                    {
                        var trimmed = nameText.Length > 6 ? nameText[..5] + "…" : nameText;
                        nameFt = Ft(trimmed, nameFs, isPast ? FontWeight.SemiBold : FontWeight.Bold,
                            new SolidColorBrush(isPast ? inkF : isCurrent ? Colors.White : inkF));
                    }
                    var pillUnscaledW = Math.Min(nameFt.WidthIncludingTrailingWhitespace / nameScale + 30, NameMaxW);
                    var pillW = pillUnscaledW * nameScale;
                    const double pillBaseH = 29;
                    var pillH = pillBaseH * nameScale;
                    // 围绕原始中心(y0-68 + 14.5)缩放
                    var pillRect = new Rect(x - pillW / 2, (y0 - 68) + pillBaseH / 2 - pillH / 2, pillW, pillH);
                    var pillRadius = (float)(pillH / 2);
                    if (pillBrush is not null)
                    {
                        ctx.FillRectangle(pillBrush, pillRect, pillRadius);
                    }
                    if (!isPast && !isCurrent)
                    {
                        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(nameBd), 1), pillRect, pillRadius, pillRadius);
                    }
                    // 名称文字垂直居中于胶囊(对应 CSS padding 5px + 行高 上下居中)
                    var drawX = x - nameFt.WidthIncludingTrailingWhitespace / 2;
                    ctx.DrawText(nameFt, new Point(drawX, pillRect.Y + (pillRect.Height - nameFt.Height) / 2));

                    // 时间 12px w600 top=y0-32
                    var timeFt = Ft(timeText, 12, FontWeight.SemiBold,
                        new SolidColorBrush(isCurrent || isDoneNode && false ? inkF : isPast ? inkF : sub));
                    if (isCurrent)
                    {
                        timeFt = Ft(timeText, 12, FontWeight.SemiBold, new SolidColorBrush(inkF));
                    }
                    ctx.DrawText(timeFt, new Point(x - timeFt.WidthIncludingTrailingWhitespace / 2, y0 - 32));

                    // 圆点: 普通 10px 边2 当前 16px
                    if (isCurrent)
                    {
                        var dotColor = isBreakNode ? brk : accent;
                        var glow = Color.FromArgb(70, dotColor.R, dotColor.G, dotColor.B);
                        ctx.DrawEllipse(new SolidColorBrush(glow), null, new Point(x, y0), 12, 12);
                        ctx.DrawEllipse(new SolidColorBrush(dotColor), null, new Point(x, y0), 8, 8);
                    }
                    else if (isPast)
                    {
                        ctx.DrawEllipse(new SolidColorBrush(dotPast), null, new Point(x, y0), 5, 5);
                    }
                    else
                    {
                        ctx.DrawEllipse(new SolidColorBrush(dotBg), new Pen(new SolidColorBrush(dotBd), 2), new Point(x, y0), 5, 5);
                    }

                    // 期间 13px w600 top=y0+16 当前加倒计时 " · m:ss"
                    var pd = periodText;
                    if (isCurrent)
                    {
                        var total = isBreakNode && n["break"] is Dictionary<string, object?> b
                            ? Pt((string)b["end"]!) - Pt((string)b["start"]!)
                            : Pt((string)n["end"]!) - Pt((string)n["start"]!);
                        var left = (int)(total * (1 - Math.Max(0, Math.Min(1, st.Prog))));
                        pd = periodText + " · " + Fmt(left);
                    }
                    var periodFt = Ft(pd, 13, isCurrent ? FontWeight.Bold : FontWeight.SemiBold,
                        new SolidColorBrush(isCurrent || isDoneNode ? inkF : isPast ? inkF : sub));
                    ctx.DrawText(periodFt, new Point(x - periodFt.WidthIncludingTrailingWhitespace / 2, y0 + 16));
                }
            }
        }
    }
}


// 组件配置面板
// 字段体系对齐原版: _SwitchField/_SpinField/_ComboField/_TextField/_SliderField/_ColorField/_DateField/_PlainColorField
// 各组件专属字段定义(_basic_defs)按用户要求暂留空; 进阶页沿用原版默认(外观 + 字体)

/// <summary>配置字段基类</summary>
public abstract class ConfigField
{
    protected readonly string Key;
    protected readonly string LabelText;

    protected ConfigField(string key, string labelText)
    {
        Key = key;
        LabelText = labelText;
    }

    public abstract Control Build();
    public abstract void Load(JsonObject config);
    public abstract void Save(JsonObject result);
    public abstract void Reset();

    protected static JsonValue? Node(JsonObject o, string k) =>
        o.TryGetPropertyValue(k, out var n) ? n as JsonValue : null;

    protected static bool GetBool(JsonObject o, string k, bool def) =>
        Node(o, k) is { } v && v.TryGetValue(out bool b) ? b : def;

    protected static int GetInt(JsonObject o, string k, int def)
    {
        if (Node(o, k) is not { } v)
        {
            return def;
        }
        if (v.TryGetValue(out int i))
        {
            return i;
        }
        return v.TryGetValue(out double d) ? (int)Math.Round(d) : def;
    }

    protected static double GetNum(JsonObject o, string k, double def)
    {
        if (Node(o, k) is not { } v)
        {
            return def;
        }
        if (v.TryGetValue(out double d))
        {
            return d;
        }
        return v.TryGetValue(out int i) ? i : def;
    }

    protected static string GetStr(JsonObject o, string k, string def) =>
        Node(o, k) is { } v && v.TryGetValue(out string? s) && s is not null ? s : def;

    protected TextBlock Label() => new()
    {
        Text = LabelText,
        VerticalAlignment = VerticalAlignment.Center,
    };
}

/// <summary>开关字段</summary>
public sealed class SwitchField : ConfigField
{
    private readonly bool _default;
    private ToggleSwitch? _w;

    public SwitchField(string key, string label, bool @default) : base(key, label) => _default = @default;

    public override Control Build()
    {
        _w = new ToggleSwitch { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        row.Children.Add(Label());
        Grid.SetColumn(_w, 1);
        row.Children.Add(_w);
        return row;
    }

    public override void Load(JsonObject c) => _w!.IsChecked = GetBool(c, Key, _default);
    public override void Save(JsonObject r) => r[Key] = _w!.IsChecked == true;
    public override void Reset() => _w!.IsChecked = _default;
}

/// <summary>数值输入字段</summary>
public sealed class SpinField : ConfigField
{
    private readonly int _default;
    private readonly int _min;
    private readonly int _max;
    private NumericUpDown? _w;

    public SpinField(string key, string label, int @default, int min, int max) : base(key, label)
    {
        _default = @default;
        _min = min;
        _max = max;
    }

    public override Control Build()
    {
        _w = new NumericUpDown
        {
            Minimum = _min,
            Maximum = _max,
            Value = _default,
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        row.Children.Add(Label());
        Grid.SetColumn(_w, 1);
        row.Children.Add(_w);
        return row;
    }

    public override void Load(JsonObject c) => _w!.Value = GetInt(c, Key, _default);
    public override void Save(JsonObject r) => r[Key] = (int)(_w!.Value ?? _default);
    public override void Reset() => _w!.Value = _default;
}

/// <summary>下拉选择字段 options: [(存值, 显示文本)]</summary>
public sealed class ComboField : ConfigField
{
    private readonly (string Value, string Text)[] _options;
    private readonly string _default;
    private ComboBox? _w;

    public ComboField(string key, string label, (string Value, string Text)[] options, string @default) : base(key, label)
    {
        _options = options;
        _default = @default;
    }

    public override Control Build()
    {
        _w = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (val, text) in _options)
        {
            _w.Items.Add(new ComboBoxItem { Content = text, Tag = val });
        }
        var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        row.Children.Add(Label());
        Grid.SetColumn(_w, 1);
        row.Children.Add(_w);
        return row;
    }

    private static string? ItemValue(object? item) => (item as ComboBoxItem)?.Tag as string;

    public override void Load(JsonObject c)
    {
        var val = GetStr(c, Key, _default);
        var idx = Array.FindIndex(_options, o => o.Value == val);
        if (idx < 0)
        {
            idx = Array.FindIndex(_options, o => o.Value == _default);
        }
        _w!.SelectedIndex = Math.Max(0, idx);
    }

    public override void Save(JsonObject r) => r[Key] = ItemValue(_w!.SelectedItem) ?? _default;

    public override void Reset()
    {
        var idx = Array.FindIndex(_options, o => o.Value == _default);
        _w!.SelectedIndex = Math.Max(0, idx);
    }
}

/// <summary>文本字段(inline=true 时标签与输入框同行)</summary>
public sealed class TextField : ConfigField
{
    private readonly string _default;
    private readonly string _placeholder;
    private readonly bool _inline;
    private TextBox? _w;

    public TextField(string key, string label, string @default, string placeholder = "", bool inline = false) : base(key, label)
    {
        _default = @default;
        _placeholder = placeholder;
        _inline = inline;
    }

    public override Control Build()
    {
        _w = new TextBox { Text = _default, PlaceholderText = _placeholder };
        if (_inline)
        {
            var lbl = Label();
            lbl.Width = 80;
            var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("80,*"), ColumnSpacing = 8 };
            row.Children.Add(lbl);
            Grid.SetColumn(_w, 1);
            _w.MinWidth = 200;
            row.Children.Add(_w);
            return row;
        }
        var col = new StackPanel { Spacing = 4 };
        col.Children.Add(Label());
        col.Children.Add(_w);
        return col;
    }

    public override void Load(JsonObject c) => _w!.Text = GetStr(c, Key, _default);
    public override void Save(JsonObject r) => r[Key] = _w!.Text ?? "";
    public override void Reset() => _w!.Text = _default;
}

/// <summary>滑块字段</summary>
public sealed class SliderField : ConfigField
{
    private readonly int _default;
    private readonly double _min;
    private readonly double _max;
    private readonly string _suffix;
    private Slider? _w;
    private TextBlock? _val;

    public SliderField(string key, string label, int @default, double min, double max, string suffix = "") : base(key, label)
    {
        _default = @default;
        _min = min;
        _max = max;
        _suffix = suffix;
    }

    public override Control Build()
    {
        var top = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        top.Children.Add(Label());
        _val = new TextBlock
        {
            Text = $"{_default}{_suffix}",
            Width = 60,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(_val, 1);
        top.Children.Add(_val);

        _w = new Slider { Minimum = _min, Maximum = _max, Value = _default };
        _w.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && _val is not null)
            {
                _val.Text = $"{(int)Math.Round(_w.Value)}{_suffix}";
            }
        };

        var col = new StackPanel { Spacing = 4 };
        col.Children.Add(top);
        col.Children.Add(_w);
        return col;
    }

    public override void Load(JsonObject c)
    {
        var v = GetInt(c, Key, _default);
        _w!.Value = v;
        if (_val is not null)
        {
            _val.Text = $"{v}{_suffix}";
        }
    }

    public override void Save(JsonObject r) => r[Key] = (int)Math.Round(_w!.Value);

    public override void Reset()
    {
        _w!.Value = _default;
        if (_val is not null)
        {
            _val.Text = $"{_default}{_suffix}";
        }
    }
}

/// <summary>纯颜色字段(十六进制输入 + 色块预览; 未引 ColorPicker 包故不做取色器)</summary>
public sealed class PlainColorField : ConfigField
{
    private readonly string _default;
    private TextBox? _w;
    private Border? _swatch;

    public PlainColorField(string key, string label, string @default) : base(key, label) => _default = @default;

    public override Control Build()
    {
        _w = new TextBox { Text = _default, Width = 140 };
        _swatch = new Border
        {
            Width = 36,
            Height = 26,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Black, 0.2),
        };
        void Sync()
        {
            try
            {
                _swatch.Background = new SolidColorBrush(Color.Parse(_w.Text ?? _default));
            }
            catch (Exception e)
            {
                Log.Debug($"[CFG] 颜色解析失败: {e.Message}");
            }
        }
        _w.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Sync(); };

        var lbl = Label();
        lbl.Width = 80;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        sp.Children.Add(_w);
        sp.Children.Add(_swatch);
        var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("80,*,Auto") };
        row.Children.Add(lbl);
        Grid.SetColumn(sp, 2);
        row.Children.Add(sp);
        Sync();
        return row;
    }

    public override void Load(JsonObject c) => _w!.Text = GetStr(c, Key, _default);
    public override void Save(JsonObject r) => r[Key] = _w!.Text ?? _default;
    public override void Reset() => _w!.Text = _default;
}

/// <summary>颜色字段(跟随不透明度/自定义颜色 + 颜色值)</summary>
public sealed class ColorField : ConfigField
{
    private readonly string _defaultMode;
    private readonly string _defaultColor;
    private ComboBox? _combo;
    private PlainColorField? _picker;
    private Control? _pickerRow;

    public ColorField(string key, string label, string defaultMode, string defaultColor) : base(key, label)
    {
        _defaultMode = defaultMode;
        _defaultColor = defaultColor;
    }

    public override Control Build()
    {
        _combo = new ComboBox { Width = 130 };
        _combo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("component_edit.bg_mode_opacity"), Tag = "opacity" });
        _combo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("component_edit.bg_mode_custom"), Tag = "custom" });
        _combo.SelectedIndex = _defaultMode == "custom" ? 1 : 0;

        _picker = new PlainColorField(Key + "_color", "", _defaultColor);
        _pickerRow = _picker.Build();
        _pickerRow.IsVisible = _combo.SelectedIndex == 1;
        _combo.SelectionChanged += (_, _) => _pickerRow.IsVisible = _combo.SelectedIndex == 1;

        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sp.Children.Add(_combo);
        var col = new StackPanel { Spacing = 6 };
        col.Children.Add(sp);
        col.Children.Add(_pickerRow);
        return col;
    }

    public override void Load(JsonObject c)
    {
        _combo!.SelectedIndex = GetStr(c, Key + "_mode", _defaultMode) == "custom" ? 1 : 0;
        _picker!.Load(c);
    }

    public override void Save(JsonObject r)
    {
        r[Key + "_mode"] = _combo!.SelectedIndex == 1 ? "custom" : "opacity";
        _picker!.Save(r);
    }

    public override void Reset()
    {
        _combo!.SelectedIndex = _defaultMode == "custom" ? 1 : 0;
        _picker!.Reset();
    }
}

/// <summary>日期字段 yyyy-MM-dd</summary>
public sealed class DateField : ConfigField
{
    private readonly string _default;
    private DatePicker? _w;

    public DateField(string key, string label, string @default) : base(key, label) => _default = @default;

    public override Control Build()
    {
        _w = new DatePicker { Width = 240, HorizontalAlignment = HorizontalAlignment.Right };
        if (DateTime.TryParse(_default, out var d))
        {
            _w.SelectedDate = new DateTimeOffset(d);
        }
        var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        row.Children.Add(Label());
        Grid.SetColumn(_w, 1);
        row.Children.Add(_w);
        return row;
    }

    public override void Load(JsonObject c)
    {
        var val = GetStr(c, Key, _default);
        if (DateTime.TryParse(val, out var d))
        {
            _w!.SelectedDate = new DateTimeOffset(d);
        }
    }

    public override void Save(JsonObject r) =>
        r[Key] = _w!.SelectedDate?.ToString("yyyy-MM-dd") ?? _default;

    public override void Reset()
    {
        if (DateTime.TryParse(_default, out var d))
        {
            _w!.SelectedDate = new DateTimeOffset(d);
        }
    }
}

/// <summary>组件配置面板</summary>
public sealed class ComponentConfigDialog
{
    private readonly JsonObject _config;
    private readonly string _componentId;
    private readonly List<ConfigField> _fields = new();
    private readonly StackPanel _basicHost = new() { Spacing = 14, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _advancedHost = new() { Spacing = 14, Margin = new Thickness(0, 8, 0, 0) };

    public Control Content { get; }

    private ComponentConfigDialog(JsonObject config, string componentId)
    {
        _config = config;
        _componentId = componentId;

        var tabBasic = MakeTab("component_edit.config_basic");
        var tabAdvanced = MakeTab("component_edit.config_advanced");

        var basicScroll = new ScrollViewer { Content = _basicHost, Padding = new Thickness(2, 0, 12, 0) };
        var advancedScroll = new ScrollViewer { Content = _advancedHost, Padding = new Thickness(2, 0, 12, 0) };

        BuildPage(_basicHost, BasicDefs(_componentId));
        BuildPage(_advancedHost, AdvancedDefs(_componentId));

        void Select(bool basic)
        {
            basicScroll.IsVisible = basic;
            advancedScroll.IsVisible = !basic;
            tabBasic.Foreground = new SolidColorBrush(basic ? AccentColor() : FadedColor());
            tabAdvanced.Foreground = new SolidColorBrush(!basic ? AccentColor() : FadedColor());
        }
        tabBasic.PointerReleased += (_, e) => { e.Handled = true; Select(true); };
        tabAdvanced.PointerReleased += (_, e) => { e.Handled = true; Select(false); };
        Select(true);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(0, 0, 0, 8) };
        tabs.Children.Add(tabBasic);
        tabs.Children.Add(tabAdvanced);

        var pages = new Grid();
        pages.Children.Add(basicScroll);
        pages.Children.Add(advancedScroll);

        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            MinWidth = 460,
            MinHeight = 340,
        };
        Grid.SetRow(tabs, 0);
        Grid.SetRow(pages, 1);
        root.Children.Add(tabs);
        root.Children.Add(pages);

        // 恢复默认 只重置字段 仍需点保存生效
        var resetBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    FAIcons.Glyph(AppIcons.Refresh, 14),
                    new TextBlock
                    {
                        Text = AppUtils.Tr("component_edit.config_reset"),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        resetBtn.Click += (_, _) =>
        {
            foreach (var f in _fields)
            {
                f.Reset();
            }
        };
        Grid.SetRow(resetBtn, 2);
        root.Children.Add(resetBtn);
        Content = root;

        foreach (var f in _fields)
        {
            f.Load(_config);
        }
        Log.Debug($"[CFG] 配置面板构建: 字段数={_fields.Count}");
    }

    /// <summary>弹出配置面板; 保存返回写回后的配置, 取消返回 null</summary>
    public static async Task<JsonObject?> ShowAsync(Window owner, JsonObject config, string componentId)
    {
        var dlg = new ComponentConfigDialog(config, componentId);
        var cd = new FAContentDialog
        {
            Title = AppUtils.Tr("component_edit.config_title"),
            Content = dlg.Content,
            PrimaryButtonText = AppUtils.Tr("component_edit.config_save"),
            CloseButtonText = AppUtils.Tr("component_edit.config_cancel"),
            DefaultButton = FAContentDialogButton.Primary,
        };
        var result = await cd.ShowAsync(owner);
        if (result != FAContentDialogResult.Primary)
        {
            return null;
        }
        dlg.Collect();
        return config;
    }

    private static TextBlock MakeTab(string key) => new()
    {
        Text = AppUtils.Tr(key),
        FontSize = 16,
        FontWeight = FontWeight.SemiBold,
        Cursor = new Cursor(StandardCursorType.Hand),
    };

    private static Color AccentColor()
    {
        try
        {
            return Color.Parse(Config.ThemeColor.Value);
        }
        catch
        {
            return Color.Parse("#30c361");
        }
    }

    private static Color FadedColor() => Color.FromArgb(120, 128, 128, 128);

    /// <summary>基础页字段: 按组件类型出各自业务设置</summary>
    private static List<(string? Title, List<ConfigField> Fields)> BasicDefs(string type)
    {
        var groups = new List<(string? Title, List<ConfigField> Fields)>();
        switch (type)
        {
            case "clock_digital":
                groups.Add((AppUtils.Tr("component_edit.group_display"), new List<ConfigField>
                {
                    new SwitchField("show_seconds", AppUtils.Tr("component_edit.config_show_seconds"), true),
                    new SwitchField("show_lunar", AppUtils.Tr("component_edit.config_show_lunar"), true),
                }));
                break;
            case "clock_square_2":
                groups.Add((AppUtils.Tr("component_edit.group_display"), new List<ConfigField>
                {
                    new SwitchField("white_face", AppUtils.Tr("component_edit.config_white_face"), true),
                }));
                break;
            case "weather_icon_temp":
                groups.Add((AppUtils.Tr("component_edit.group_display"), new List<ConfigField>
                {
                    new SwitchField("show_icon", AppUtils.Tr("component_edit.config_show_icon"), true),
                }));
                break;
            case "countdown_event":
                groups.Add((AppUtils.Tr("component_edit.group_countdown"), new List<ConfigField>
                {
                    new TextField("event_name", AppUtils.Tr("component_edit.config_event_name"), "",
                        AppUtils.Tr("component_edit.config_event_name"), true),
                    new TextField("target_time", AppUtils.Tr("component_edit.config_target_time"), "",
                        "2026-12-31 08:00", true),
                }));
                break;
            case "countdown_days":
                groups.Add((AppUtils.Tr("component_edit.group_countdown"), new List<ConfigField>
                {
                    new TextField("event_name", AppUtils.Tr("component_edit.config_event_name"), "",
                        AppUtils.Tr("component_edit.config_event_name"), true),
                    new TextField("target_date", AppUtils.Tr("component_edit.config_target_date"), "",
                        "2026-12-31", true),
                    new PlainColorField("title_bg_color", AppUtils.Tr("component_edit.config_title_bg"), "#F98E1B"),
                }));
                break;
            case "school_info_class_info":
                groups.Add((AppUtils.Tr("component_edit.group_info"), new List<ConfigField>
                {
                    new TextField("school", AppUtils.Tr("component_edit.config_school"), "", "", true),
                    new TextField("class", AppUtils.Tr("component_edit.config_class"), "", "", true),
                }));
                break;
            case "linkage_timetable_nowlesson":
                groups.Add((AppUtils.Tr("component_edit.group_display"), new List<ConfigField>
                {
                    new SwitchField("show_teacher", AppUtils.Tr("component_edit.config_show_teacher"), true),
                    new SwitchField("show_next", AppUtils.Tr("component_edit.config_show_next"), true),
                    new SwitchField("show_duration", AppUtils.Tr("component_edit.config_show_duration"), true),
                    new SwitchField("show_countdown", AppUtils.Tr("component_edit.config_show_countdown"), true),
                }));
                break;
            case "sticky_note":
                groups.Add((AppUtils.Tr("component_edit.group_content"), new List<ConfigField>
                {
                    new ComboField("color", AppUtils.Tr("component_edit.config_note_color"),
                        new[]
                        {
                            ("yellow", AppUtils.Tr("component_edit.note_yellow")),
                            ("green", AppUtils.Tr("component_edit.note_green")),
                            ("blue", AppUtils.Tr("component_edit.note_blue")),
                            ("pink", AppUtils.Tr("component_edit.note_pink")),
                            ("orange", AppUtils.Tr("component_edit.note_orange")),
                            ("purple", AppUtils.Tr("component_edit.note_purple")),
                        }, "yellow"),
                }));
                break;
        }
        return groups;
    }

    /// <summary>进阶页字段: 通用外观 + 仅读取该值的组件才出字号缩放</summary>
    private static List<(string? Title, List<ConfigField> Fields)> AdvancedDefs(string type)
    {
        var groups = new List<(string? Title, List<ConfigField> Fields)>
        {
            (AppUtils.Tr("component_edit.group_appearance"), new List<ConfigField>
            {
                new SliderField("bg_opacity", AppUtils.Tr("component_edit.config_bg_opacity"),
                    Config.ComponentCardOpacity.Value, 0, 100, "%"),
                new SliderField("corner_radius", AppUtils.Tr("component_edit.config_corner_radius"),
                    Config.ComponentCardRadius.Value, 0, 29, "px"),
            }),
        };
        if (type is "school_info_class_info" or "linkage_timetable_nowlesson")
        {
            groups.Add((AppUtils.Tr("component_edit.group_font"), new List<ConfigField>
            {
                new SliderField("font_scale", AppUtils.Tr("component_edit.config_font_scale"), 100, 50, 200, "%"),
            }));
        }
        return groups;
    }


    private void BuildPage(StackPanel host, List<(string? Title, List<ConfigField> Fields)> groups)
    {
        if (groups.Count == 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = AppUtils.Tr("component_edit.feature_pending_desc"),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 24, 0, 0),
                Opacity = 0.7,
            });
            return;
        }
        foreach (var (title, fields) in groups)
        {
            var group = new StackPanel { Spacing = 8 };
            if (!string.IsNullOrEmpty(title))
            {
                group.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
            }
            var content = new StackPanel { Spacing = 10, Margin = new Thickness(12, 4, 0, 4) };
            foreach (var f in fields)
            {
                content.Children.Add(f.Build());
                _fields.Add(f);
            }
            group.Children.Add(content);
            host.Children.Add(group);
        }
    }


    private void Collect()
    {
        foreach (var f in _fields)
        {
            f.Save(_config);
        }
        Log.Debug($"[CFG] 配置已收集 键={string.Join(',', _config.Select(kv => kv.Key))}");
    }
}


/// <summary>
/// 地区选择器
/// 搜索框 + 地区列表(city.db) 实时搜索 双击确认 打开时定位当前城市 用应用内 FAContentDialog 承载
/// </summary>
public static class RegionSelectorDialog
{
    public static async Task<string?> ShowAsync(Window owner, string current)
    {
        var db = new RegionDatabase();
        var search = new TextBox
        {
            PlaceholderText = AppUtils.Tr("weather_service.region_placeholder"),
            MinWidth = 460,
        };
        // 固定高度: 让 ListBox 内部滚动(而不是外层对话框整页滚动)
        var list = new ListBox
        {
            MinWidth = 460,
            SelectionMode = SelectionMode.Single,
            // 列表自身可滚 撑满 root 的 * 行
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        void Refresh(string? kw)
        {
            list.ItemsSource = db.Search(string.IsNullOrWhiteSpace(kw) ? null : kw);
        }
        search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Refresh(search.Text);
            }
        };
        Refresh(null);

        // 固定内容高度(含标题/按钮后整窗 <=600) 列表占满 * 行并在其内部滚动
        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            RowSpacing = 10,
            MinWidth = 460,
            Height = 440,
        };
        Grid.SetRow(search, 0);
        Grid.SetRow(list, 1);
        root.Children.Add(search);
        root.Children.Add(list);

        var dlg = new FAContentDialog
        {
            Title = AppUtils.Tr("weather_service.select_region"),
            Content = root,
            PrimaryButtonText = AppUtils.Tr("common.confirm"),
            CloseButtonText = AppUtils.Tr("common.cancel"),
            DefaultButton = FAContentDialogButton.Primary,
        };

        // 双击 = 确认(
        list.DoubleTapped += (_, e) =>
        {
            if (list.SelectedItem is string s && s.Length > 0)
            {
                e.Handled = true;
                dlg.Hide(FAContentDialogResult.Primary);
            }
        };

        // 定位当前城市(
        if (!string.IsNullOrEmpty(current) && list.ItemsSource is IEnumerable<string> items)
        {
            var idx = 0;
            var found = -1;
            foreach (var it in items)
            {
                if (it == current)
                {
                    found = idx;
                    break;
                }
                idx++;
            }
            if (found >= 0)
            {
                list.SelectedIndex = found;
                list.ScrollIntoView(found);
            }
        }

        var result = await dlg.ShowAsync(owner);
        return result == FAContentDialogResult.Primary && list.SelectedItem is string sel && sel.Length > 0
            ? sel
            : null;
    }
}


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
