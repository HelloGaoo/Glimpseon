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

// DraggableContainer
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
