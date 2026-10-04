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
using System.IO;
using Avalonia.Media.Imaging;
using IOPath = System.IO.Path;
using Avalonia.Controls.Shapes;
using System.Text.RegularExpressions;
using Avalonia.Animation;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Glimpseon.Core.Services;
using System.Runtime.InteropServices;
using AvColor = Avalonia.Media.Color;
using System.Globalization;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Utilities;

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
        SetCanvasPosition(_startPosition.X + dx, _startPosition.Y + dy);
        e.Handled = true;
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
        content.Children.Add(new FASymbolIcon
        {
            Symbol = FASymbol.Home,
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
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
                    Log.Error($"创建组件失败: {e.Message}");
                }
            }
        }
        Log.Info($"[ComponentManager] 共加载{total}个组件");
    }

    private DraggableContainer? CreateWidget(ComponentData data)
    {
        var definition = _home.ComponentRegistry.GetDefinition($"{data.Type}_{data.Style}");
        if (definition is null)
        {
            Log.Warning($"组件定义未注册: {data.Type}_{data.Style}");
            return null;
        }
        // 真身工厂优先 占位兜底(M6 未完成的样式)
        var widget = StyleWidgetFactory.Create($"{data.Type}_{data.Style}", definition, data.Config)
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
            Background = new SolidColorBrush(Avalonia.Media.Colors.Gray, 0.14),
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
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 2),
        };

        var card = new Border
        {
            Width = 212,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(8, 8, 8, 6),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Avalonia.Media.Colors.Gray, 0.10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Avalonia.Media.Colors.Gray, 0.22),
            Child = new StackPanel { Children = { host, nameText } },
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
            widget = StyleWidgetFactory.Create(def.Id, def, def.DefaultConfig);
            widget ??= new PlaceholderWidget(def, def.DefaultConfig);
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


// ===== StyleWidgets.cs =====

/// <summary>
/// 样式组件工厂: 按 type_style 生成真身控件 返回 null 走占位兜底
/// </summary>
public static class StyleWidgetFactory
{
    public static DraggableContainer? Create(string typeId, ComponentDefinition definition, JsonElement? config)
    {
        DraggableContainer? widget = typeId switch
        {
            "clock_digital" => new DigitalClockWidget(definition),
            "clock_square_1" => new NativeSquareClock1Widget(definition),
            "clock_square_2" => new NativeSquareClock2Widget(definition),
            "clock_calendar_month" => new CalendarMonthWidget(definition),
            "clock_calendar_mini" => new NativeMiniCalendarWidget(definition),
            "clock_almanac" => new NativeAlmanacWidget(definition),
            "poetry_one_line" => new PoetryWidget(definition),
            "weather_icon_temp" => new WeatherCurrentWidget(definition),
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
            "quick_launch_dock" => new QuickLaunchDockWidget(definition),
            "quick_launch_grid" => new QuickLaunchGridWidget(definition),
            "timer_countdown" => new TimerCountdownWidget(definition),
            "system_performance" => new PerformanceMonitorWidget(definition),
            "system_netspeed" => new NativeNetworkSpeedWidget(definition),
            "study_meter" => new NativeDecibelMeterWidget(definition),
            "writing_pad" => new WritingPadWidget(definition),
            _ => null,
        };
        return widget;
    }
}

/// <summary>卡片底座 对齐原版 _card_bg_css: 深色主题 rgba(30,30,30) 浅色主题 rgba(255,255,255) 透明度绑配置</summary>
public abstract class WidgetCardBase : DraggableContainer
{
    protected readonly Border Card;

    // 组件级背景覆盖
    protected double? CfgBgOpacity;
    protected int? CfgRadius;

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
        // 控件级事件: 构造时未挂树变体可能是 Light 挂树继承窗口 Dark 后必须重算
        ActualThemeVariantChanged += (_, _) => ApplyCardStyle();
    }

    // 读取组件配置中的背景覆盖
    protected void ReadBgOverride(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }
        if (obj.TryGetProperty("bg_opacity", out var o) && o.ValueKind == JsonValueKind.Number)
        {
            CfgBgOpacity = o.GetDouble();
        }
        if (obj.TryGetProperty("corner_radius", out var r) && r.ValueKind == JsonValueKind.Number)
        {
            CfgRadius = r.GetInt32();
        }
    }

    protected double EffectiveOpacity =>
        CfgBgOpacity is { } o ? Math.Clamp(o / 100.0, 0, 1) : Math.Clamp(Config.ComponentCardOpacity.Value / 100.0, 0, 1);

    protected double EffectiveRadius =>
        CfgRadius is { } r ? Math.Max(8, r) : Math.Max(8, Config.ComponentCardRadius.Value);

    // 卡片背景与文字色随主题 对齐原版深色 rgba(30,30,30) 浅色 rgba(255,255,255) 文字 #e0e0e0/#1a1a1a
    protected virtual void ApplyCardStyle()
    {
        var dark = ThemeSense.IsDark(this);
        var op = EffectiveOpacity;
        Card.Background = dark
            ? new SolidColorBrush(Color.FromRgb(30, 30, 30), op)
            : new SolidColorBrush(Colors.White, op);
        Card.BorderBrush = new SolidColorBrush(dark ? Colors.White : Colors.Black, dark ? 0.10 : 0.06);
        Card.CornerRadius = new CornerRadius(EffectiveRadius);
        CardRadius = EffectiveRadius;
        // 未显式设色的子文本继承卡片文字色(原版 is_dark ? #e0e0e0 : #1a1a1a)
        Card.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty,
            new SolidColorBrush(Color.Parse(dark ? "#e0e0e0" : "#1a1a1a")));
    }
}

// ---------- 数字时钟
// 规格: 400x200 时钟80px bold/日期16px 色ClockColor 竖排3:1 边距(32,24,32,24) 间距8
// HH:mm:ss(ShowClockSeconds) 日期 {y}年{M}月{d}日 星期X + 农历(ShowLunarCalendar) ShowClock=false 整体隐藏

public sealed class DigitalClockWidget : WidgetCardBase
{
    private readonly TextBlock _timeText = new();
    private readonly TextBlock _dateText = new();
    private readonly DispatcherTimer _timer;

    private static readonly string[] Weekdays = { "日", "一", "二", "三", "四", "五", "六" };

    public DigitalClockWidget(ComponentDefinition definition) : base(definition)
    {
        // 原版 inner_layout: 时钟 stretch 3 + 日期 stretch 1
        var grid = new Grid
        {
            Margin = new Thickness(32, 24, 32, 24),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("3*,*"),
            RowSpacing = 8,
        };
        _timeText.HorizontalAlignment = HorizontalAlignment.Stretch;
        _timeText.TextAlignment = TextAlignment.Center;
        _timeText.VerticalAlignment = VerticalAlignment.Stretch;
        _dateText.HorizontalAlignment = HorizontalAlignment.Stretch;
        _dateText.TextAlignment = TextAlignment.Center;
        _dateText.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetRow(_timeText, 0);
        Grid.SetRow(_dateText, 1);
        grid.Children.Add(_timeText);
        grid.Children.Add(_dateText);
        Card.Child = grid;
        Content = Card;

        ApplyClockStyle();
        Config.ClockColor.ValueChanged += _ => ApplyClockStyle();
        Config.ClockSize.ValueChanged += _ => ApplyClockStyle();
        Config.DateSize.ValueChanged += _ => ApplyClockStyle();
        Config.ShowClockSeconds.ValueChanged += _ => UpdateTime();
        Config.ShowLunarCalendar.ValueChanged += _ => UpdateTime();
        Config.ShowClock.ValueChanged += _ => UpdateTime();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateTime();
        _timer.Start();
        UpdateTime();
    }

    // 原版 _apply_style: 时钟 bold 颜色 clockColor 字号 clockSize 日期 dateSize
    private void ApplyClockStyle()
    {
        IBrush brush;
        try
        {
            brush = new SolidColorBrush(Color.Parse(Config.ClockColor.Value));
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 颜色解析失败 用白色: {e.Message}");
            brush = Brushes.White;
        }
        _timeText.Foreground = brush;
        _timeText.FontSize = Config.ClockSize.Value;
        _timeText.FontWeight = FontWeight.Bold;
        _dateText.Foreground = brush;
        _dateText.FontSize = Config.DateSize.Value;
    }

    private void UpdateTime()
    {
        // 原版: showClock=false 组件整体隐藏(编辑模式显示占位)
        if (!Config.ShowClock.Value)
        {
            IsVisible = false;
            return;
        }
        IsVisible = true;

        var now = AppUtils.PreciseNow();
        _timeText.Text = Config.ShowClockSeconds.Value ? now.ToString("HH:mm:ss") : now.ToString("HH:mm");

        // 原版 tr("date.format", y, M, d, w) = "{y}年{M}月{d}日 {w}"
        var weekday = "星期" + Weekdays[(int)now.DayOfWeek];
        var date = $"{now.Year}年{now.Month}月{now.Day}日 {weekday}";
        if (Config.ShowLunarCalendar.Value)
        {
            var lunar = AlmanacService.GetToday(now);
            if (lunar is not null && !string.IsNullOrEmpty(lunar.LunarMonth))
            {
                date += $" {lunar.LunarMonth}{lunar.LunarDay}";
            }
        }
        _dateText.Text = date;
    }
}

// ---------- 一言
// 规格: 400x200 边距(24,20,24,20) 单label居中wrap 富文本(——分离出处/逗号换行) 字号PoetrySize(16) 色PoetryTextColor

public sealed class PoetryWidget : WidgetCardBase
{
    private readonly TextBlock _contentText = new();
    private readonly TextBlock _authorText = new();

    public PoetryWidget(ComponentDefinition definition) : base(definition)
    {
        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        // 原版 RichText: 正文 div + 出处 div(两行)
        _contentText.TextAlignment = TextAlignment.Center;
        _contentText.TextWrapping = TextWrapping.Wrap;
        _authorText.TextAlignment = TextAlignment.Center;
        // 原版两个 div 直接堆叠 无间距
        stack.Children.Add(_contentText);
        stack.Children.Add(_authorText);
        Card.Child = stack;
        Content = Card;

        ApplyPoetryStyle();
        Config.PoetryTextColor.ValueChanged += _ => ApplyPoetryStyle();
        Config.PoetrySize.ValueChanged += _ => ApplyPoetryStyle();
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

    // 原版 _format_poetry: —— / — 分离出处 逗号后换行
    private void FormatPoetry(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _contentText.Text = "";
            _authorText.Text = "";
            return;
        }
        var content = text.Trim();
        var attribution = "";
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
        content = System.Text.RegularExpressions.Regex.Replace(content, "([,，])", "$1\n").Trim();
        if (content.EndsWith("\n"))
        {
            content = content[..^1].TrimEnd();
        }
        _contentText.Text = content;
        _authorText.Text = attribution;
    }

    private void ApplyPoetryStyle()
    {
        IBrush brush;
        try
        {
            brush = new SolidColorBrush(Color.Parse(Config.PoetryTextColor.Value));
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 颜色解析失败 用白色: {e.Message}");
            brush = Brushes.White;
        }
        _contentText.Foreground = brush;
        _authorText.Foreground = brush;
        _contentText.FontSize = Config.PoetrySize.Value;
        _authorText.FontSize = Config.PoetrySize.Value;
    }
}

// ---------- 天气三件套

/// <summary>天气组件公共基座: 显隐开关/缓存读取/SVG 图标 对齐原版 WeatherComponentBase</summary>
public abstract class WeatherWidgetBase : WidgetCardBase
{
    protected readonly DispatcherTimer RefreshTimer;

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
    private readonly TextBlock _tempText = new();

    public WeatherCurrentWidget(ComponentDefinition definition) : base(definition)
    {
        // 原版 WeatherIconTempComponent: horizontal 布局 AlignCenter 图标:温度=1:1 边距(24,20,24,20) 间距12
        var grid = new Grid
        {
            Margin = new Thickness(24, 20, 24, 20),
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,*"),
            ColumnSpacing = 12,
        };
        _icon.VerticalAlignment = VerticalAlignment.Center;
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _tempText.VerticalAlignment = VerticalAlignment.Center;
        _tempText.HorizontalAlignment = HorizontalAlignment.Center;
        _tempText.TextAlignment = TextAlignment.Center;
        Grid.SetColumn(_icon, 0);
        Grid.SetColumn(_tempText, 1);
        grid.Children.Add(_icon);
        grid.Children.Add(_tempText);
        Card.Child = grid;
        Content = Card;

        ApplyWeatherStyle();
        Config.WeatherSize.ValueChanged += _ => ApplyWeatherStyle();
        Config.WeatherIconSize.ValueChanged += _ => RefreshFromCache();
        RefreshFromCache();
    }

    protected override void ApplyWeatherStyle()
    {
        IBrush brush;
        try
        {
            brush = new SolidColorBrush(Color.Parse(Config.WeatherTextColor.Value));
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 颜色解析失败 用白色: {e.Message}");
            brush = Brushes.White;
        }
        _tempText.Foreground = brush;
        _tempText.FontSize = Config.WeatherSize.Value;
    }

    protected override void UpdateDisplay(JsonElement? cached)
    {
        var (temp, code) = ParseCurrent(cached);
        _tempText.Text = temp is "--" ? "--°" : $"{temp}°";
        var icon = cached is { ValueKind: JsonValueKind.Object } ? WeatherMaps.GetIcon(code) : null;
        var svg = MakeSvgIcon(icon, Config.WeatherIconSize.Value);
        _icon.SvgSource = svg?.SvgSource;
        _icon.Width = svg?.Width ?? 0;
        _icon.Height = svg?.Height ?? 0;
    }
}

/// <summary>逐小时天气</summary>
public sealed class WeatherHourlyWidget : WeatherWidgetBase
{
    private readonly TextBlock _cityText = new();
    private readonly TextBlock _tempText = new();
    private readonly Avalonia.Svg.Skia.Svg _currentIcon = MakeSvgIcon("2.svg", 60)!;
    private readonly (TextBlock Time, Avalonia.Svg.Skia.Svg Icon, TextBlock Temp)[] _slots = new (TextBlock, Avalonia.Svg.Skia.Svg, TextBlock)[6];

    public WeatherHourlyWidget(ComponentDefinition definition) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(28, 18, 28, 18),
            // 原版 addWidget(top,3)+addWidget(bottom,1) 但 bottom 需容纳时间+图标+温度 用 Auto 防裁切
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto"),
            RowSpacing = 6,
        };

        // 上行: 左(城市+温度) 右(图标60) 原版 AlignTop + 两侧尾部 addStretch -> 内容贴顶
        var top = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto") };
        var left = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        _cityText.FontSize = 16;
        _cityText.Opacity = 0.7;
        _tempText.FontSize = 52;
        _tempText.FontWeight = FontWeight.Light;
        left.Children.Add(_cityText);
        left.Children.Add(_tempText);
        _currentIcon.Width = 60;
        _currentIcon.Height = 60;
        _currentIcon.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(left, 0);
        Grid.SetColumn(_currentIcon, 1);
        top.Children.Add(left);
        top.Children.Add(_currentIcon);

        // 下行: 6 槽 (时间/图标28/温度 均 12px) 原版 bottom 内容居中
        var bottom = new Avalonia.Controls.Primitives.UniformGrid { Rows = 1, Columns = 6 };
        for (var i = 0; i < 6; i++)
        {
            var col = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var time = new TextBlock { FontSize = 12, Opacity = 0.7, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var icon = MakeSvgIcon("2.svg", 28)!;
            var temp = new TextBlock { FontSize = 12, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(time);
            col.Children.Add(icon);
            col.Children.Add(temp);
            _slots[i] = (time, icon, temp);
            bottom.Children.Add(col);
        }

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
        IBrush brush;
        try
        {
            brush = new SolidColorBrush(Color.Parse(Config.WeatherTextColor.Value));
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 颜色解析失败 用白色: {e.Message}");
            brush = Brushes.White;
        }
        _cityText.Foreground = brush;
        _tempText.Foreground = brush;
        foreach (var (time, _, temp) in _slots)
        {
            time.Foreground = brush;
            temp.Foreground = brush;
        }
    }

    protected override void UpdateDisplay(JsonElement? cached)
    {
        var (temp, code) = ParseCurrent(cached);
        _cityText.Text = Config.City.Value;
        _tempText.Text = temp is "--" ? "--°" : $"{temp}°";
        var curSvg = MakeSvgIcon(cached is { ValueKind: JsonValueKind.Object } ? WeatherMaps.GetIcon(code) : null, 60);
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
        for (var i = 0; i < 6; i++)
        {
            if (i < hours.Count)
            {
                var h = hours[i];
                _slots[i].Time.Text = $"{(startHour + i) % 24:00}:00";
                var svg = MakeSvgIcon(h.Icon, 28);
                _slots[i].Icon.SvgSource = svg?.SvgSource;
                _slots[i].Temp.Text = h.Temp is "--" ? "--°" : $"{h.Temp}°";
            }
            else
            {
                _slots[i].Time.Text = "--:00";
                _slots[i].Icon.SvgSource = null;
                _slots[i].Temp.Text = "--°";
            }
        }
    }
}

/// <summary>逐日天气</summary>
public sealed class WeatherWeeklyWidget : WeatherWidgetBase
{
    private readonly TextBlock _cityText = new();
    private readonly TextBlock _tempText = new();
    private readonly Avalonia.Svg.Skia.Svg _currentIcon = MakeSvgIcon("2.svg", 60)!;
    private readonly (TextBlock Day, Avalonia.Svg.Skia.Svg Icon, TextBlock Low, TextBlock High)[] _rows =
        new (TextBlock, Avalonia.Svg.Skia.Svg, TextBlock, TextBlock)[4];

    private static readonly string[] WeekdayNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    public WeatherWeeklyWidget(ComponentDefinition definition) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(14, 12, 14, 12),
            // 原版 addWidget(top,3)+addWidget(bottom,1) 4行×20=80 用 Auto 防第4行裁切
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto"),
            RowSpacing = 4,
        };

        var top = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto") };
        var left = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        _cityText.FontSize = 14;
        _cityText.Opacity = 0.7;
        _tempText.FontSize = 48;
        _tempText.FontWeight = FontWeight.Light;
        left.Children.Add(_cityText);
        left.Children.Add(_tempText);
        _currentIcon.Width = 48;
        _currentIcon.Height = 48;
        _currentIcon.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(left, 0);
        Grid.SetColumn(_currentIcon, 1);
        top.Children.Add(left);
        top.Children.Add(_currentIcon);

        // 下行: 4 行 行高20 原版 row: day(40) | stretch | icon(18) | stretch | low | spacer(8) | high(28)
        var bottom = new StackPanel { Spacing = 0 };
        for (var i = 0; i < 4; i++)
        {
            var row = new Grid { Height = 20, ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("40,*,Auto,*,Auto,8,28") };
            var day = new TextBlock { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
            var icon = MakeSvgIcon("2.svg", 18)!;
            icon.VerticalAlignment = VerticalAlignment.Center;
            var low = new TextBlock { FontSize = 11, Opacity = 0.6, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var high = new TextBlock { FontSize = 11, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(day, 0);
            Grid.SetColumn(icon, 2);
            Grid.SetColumn(low, 4);
            Grid.SetColumn(high, 6);
            row.Children.Add(day);
            row.Children.Add(icon);
            row.Children.Add(low);
            row.Children.Add(high);
            _rows[i] = (day, icon, low, high);
            bottom.Children.Add(row);
        }

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
        IBrush brush;
        try
        {
            brush = new SolidColorBrush(Color.Parse(Config.WeatherTextColor.Value));
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 颜色解析失败 用白色: {e.Message}");
            brush = Brushes.White;
        }
        _cityText.Foreground = brush;
        _tempText.Foreground = brush;
        foreach (var (day, _, low, high) in _rows)
        {
            day.Foreground = brush;
            low.Foreground = brush;
            high.Foreground = brush;
        }
    }

    protected override void UpdateDisplay(JsonElement? cached)
    {
        var (temp, code) = ParseCurrent(cached);
        _cityText.Text = Config.City.Value;
        _tempText.Text = temp is "--" ? "--°" : $"{temp}°";
        var curSvg = MakeSvgIcon(cached is { ValueKind: JsonValueKind.Object } ? WeatherMaps.GetIcon(code) : null, 48);
        _currentIcon.SvgSource = curSvg?.SvgSource;

        WeatherDaily? parsed = null;
        if (cached is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("forecastDaily", out var fd))
        {
            parsed = WeatherService.ParseDaily(fd);
        }
        var days = parsed?.Days ?? new List<WeatherDay>();
        var now = DateTime.Now;
        for (var i = 0; i < 4; i++)
        {
            if (i < days.Count)
            {
                var d = now.AddDays(i);
                _rows[i].Day.Text = i == 0 ? "今日" : WeekdayNames[((int)d.DayOfWeek + 6) % 7];
                var svg = MakeSvgIcon(days[i].Icon, 18);
                _rows[i].Icon.SvgSource = svg?.SvgSource;
                _rows[i].Low.Text = days[i].Low ?? "--";
                _rows[i].High.Text = $"{days[i].High ?? "--"}°";
            }
            else
            {
                _rows[i].Day.Text = "--";
                _rows[i].Icon.SvgSource = null;
                _rows[i].Low.Text = "--";
                _rows[i].High.Text = "--°";
            }
        }
    }
}

// ---------- 倒计时
// 事件倒计时: 200x200 边距(24,20,24,20) 单label 居中wrap 1s tick 文本"{name}\n{d}天 {h}时 {m}分 {s}秒"
// 倒数日: 三段式 顶标题条(title_bg #F98E1B 高44 上圆角 白17px 600) 中数字(72px bold 自适应缩字号) 底日期条(高34 下圆角)

public sealed class CountdownEventWidget : WidgetCardBase
{
    private readonly TextBlock _label = new();
    private string _eventName = "";
    private string _targetTime = "";

    public CountdownEventWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _label.TextAlignment = TextAlignment.Center;
        _label.TextWrapping = TextWrapping.Wrap;
        _label.VerticalAlignment = VerticalAlignment.Center;
        Card.Child = new ScrollViewer
        {
            Content = _label,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        Content = Card;

        ReadConfig(config);
        ApplyStyle();
        Config.CountdownTextColor.ValueChanged += _ => ApplyStyle();
        Config.CountdownTextSize.ValueChanged += _ => ApplyStyle();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => UpdateCountdown();
        timer.Start();
        UpdateCountdown();
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
        IBrush brush;
        try
        {
            brush = new SolidColorBrush(Color.Parse(Config.CountdownTextColor.Value));
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 颜色解析失败 用白色: {e.Message}");
            brush = Brushes.White;
        }
        _label.Foreground = brush;
        _label.FontSize = Config.CountdownTextSize.Value;
    }

    private void UpdateCountdown()
    {
        if (string.IsNullOrEmpty(_eventName) || string.IsNullOrEmpty(_targetTime))
        {
            _label.Text = AppUtils.Tr("countdown.click_to_config");
            return;
        }
        if (!DateTime.TryParse(_targetTime, out var target))
        {
            _label.Text = _eventName;
            return;
        }
        var delta = target - DateTime.Now;
        if (delta.TotalSeconds > 0)
        {
            var days = delta.Days;
            var hours = delta.Hours;
            var minutes = delta.Minutes;
            var seconds = delta.Seconds;
            _label.Text = $"{_eventName}\n{days}天 {hours}时 {minutes}分 {seconds}秒";
        }
        else
        {
            _label.Text = $"{_eventName}\n{AppUtils.Tr("countdown.expired")}";
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

        // 原版 headerWidget / numLabel(Expanding) / footerWidget 三段 中段撑满居中
        var grid = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("44,*,34"),
        };
        Grid.SetRow(_headerBorder, 0);
        Grid.SetRow(_numText, 1);
        Grid.SetRow(_footerBorder, 2);
        grid.Children.Add(_headerBorder);
        grid.Children.Add(_numText);
        grid.Children.Add(_footerBorder);
        Card.Child = grid;
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
            _headerText.Text = AppUtils.Tr("days_matter.default_title");
            _numText.Text = "· · ·";
            _footerText.Text = AppUtils.Tr("countdown.click_to_config");
            return;
        }
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
        _numText.FontWeight = FontWeight.Bold;
        _numText.Text = numberText;
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        // 原版: 倒数日卡片 opacity 强制 100
        var dark = ThemeSense.IsDark(this);
        Card.Background = dark
            ? new SolidColorBrush(Color.FromRgb(30, 30, 30), 1.0)
            : new SolidColorBrush(Colors.White, 1.0);
        _headerBorder.Background = new SolidColorBrush(Color.Parse(_titleBg));
        _headerBorder.CornerRadius = new CornerRadius(Math.Max(8, Config.ComponentCardRadius.Value), Math.Max(8, Config.ComponentCardRadius.Value), 0, 0);
        // header/footer 固定高 顶/底对齐 保证中间 * 行充分撑开
        _headerBorder.VerticalAlignment = VerticalAlignment.Top;
        _headerBorder.Height = 44;
        _footerBorder.VerticalAlignment = VerticalAlignment.Bottom;
        _footerBorder.Height = 34;
        _headerText.Foreground = Brushes.White;
        _headerText.FontSize = 17;
        _headerText.FontWeight = FontWeight.SemiBold;
        _headerText.LetterSpacing = 1;
        _numText.Foreground = new SolidColorBrush(dark ? Color.Parse("#f0f0f0") : Color.Parse("#111111"));
        _footerBorder.Background = new SolidColorBrush(dark ? Color.Parse("#2d2d2d") : Color.Parse("#f5f5f5"));
        _footerBorder.CornerRadius = new CornerRadius(0, 0, Math.Max(8, Config.ComponentCardRadius.Value), Math.Max(8, Config.ComponentCardRadius.Value));
        _footerText.Foreground = new SolidColorBrush(dark ? Color.Parse("#9a9a9a") : Color.Parse("#8e8e93"));
        _footerText.FontSize = Math.Max(8, 11.5);
    }
}

// ---------- 学校班级信息
// 规格: 400x200 边距(8,8,8,8) 间距6 上层背景3:口号1 上层边距(20,16,20,12) 间距4
// 班级行(班级左bold+人数右) 学校名 换行口号(边距20,4,20,4)
// 字号默认 班级48/学校25/人数19/口号23 缩放font_scale 人数"{n}人"

public sealed class SchoolInfoWidget : WidgetCardBase
{
    private readonly Border _topBg = new();
    private readonly TextBlock _classText = new();
    private readonly TextBlock _countText = new();
    private readonly TextBlock _schoolText = new();
    private readonly TextBlock _sloganText = new();

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
        // 原版 inner_layout: topWidget stretch 3 + slogan stretch 1
        var root = new Grid
        {
            Margin = new Thickness(8, 8, 8, 8),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("3*,*"),
            RowSpacing = 6,
        };

        // 上层背景: 班级 人数 / 学校名
        var topInner = new StackPanel { Margin = new Thickness(20, 16, 20, 12), Spacing = 4 };
        var classRow = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto") };
        Grid.SetColumn(_classText, 0);
        Grid.SetColumn(_countText, 1);
        classRow.Children.Add(_classText);
        classRow.Children.Add(_countText);
        topInner.Children.Add(classRow);
        topInner.Children.Add(_schoolText);
        _topBg.Child = topInner;

        // 口号 独立边距(20,4,20,4) 换行 拉伸
        _sloganText.Margin = new Thickness(20, 4, 20, 4);
        _sloganText.TextWrapping = TextWrapping.Wrap;
        _sloganText.VerticalAlignment = VerticalAlignment.Top;

        Grid.SetRow(_topBg, 0);
        Grid.SetRow(_sloganText, 1);
        root.Children.Add(_topBg);
        root.Children.Add(_sloganText);
        Card.Child = root;
        Content = Card;

        ReadConfig(config);
        ApplyCardStyle();
        ApplyStyle();
        UpdateInfo();
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
        if (!IsConfigured)
        {
            // 未配置 提示文本居中 其余空
            _classText.Text = AppUtils.Tr("school_info_component.click_to_config");
            _classText.TextAlignment = TextAlignment.Center;
            _countText.Text = "";
            _schoolText.Text = "";
            _sloganText.Text = "";
            return;
        }
        _classText.TextAlignment = TextAlignment.Left;
        _classText.Text = _class;
        _schoolText.Text = _school;
        _sloganText.Text = _slogan;
        _countText.Text = _count.Length > 0 ? $"{_count}人" : "";
    }

    private void ApplyStyle()
    {
        var dark = ThemeSense.IsDark(this);
        var scale = _fontScale / 100.0;
        double S(int px) => Math.Max(1, px * scale);

        var textColor = _textColor.Length > 0 ? _textColor : dark ? "#e0e0e0" : "#1a1a1a";
        var subColor = dark ? "#aaaaaa" : "#666666";
        var countColor = dark ? "#777777" : "#999999";

        IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

        _classText.Foreground = B(textColor);
        _classText.FontSize = S(_classSize);
        _classText.FontWeight = FontWeight.Bold;
        _schoolText.Foreground = B(subColor);
        _schoolText.FontSize = S(_schoolSize);
        _countText.Foreground = B(countColor);
        _countText.FontSize = S(_countSize);
        _sloganText.Foreground = B(subColor);
        _sloganText.FontSize = S(_sloganSize);

        // 上层背景 默认 opacity 模式 与卡片同色再叠一层
        var op = Math.Clamp(Config.ComponentCardOpacity.Value / 100.0, 0, 1);
        _topBg.Background = dark
            ? new SolidColorBrush(Color.FromRgb(30, 30, 30), op)
            : new SolidColorBrush(Colors.White, op);
        _topBg.CornerRadius = new CornerRadius(Math.Max(8, Config.ComponentCardRadius.Value));
    }
}

// ---------- 课表共享数据源

public static class TimetableScheduleProvider
{
    public static List<ScheduleRow> GetTodaySchedule()
    {
        var result = new List<ScheduleRow>();
        try
        {
            var profileName = LatestProfileName();
            var profilePath = System.IO.Path.Combine(Paths.DataProfile, $"{profileName}.json");
            if (!File.Exists(profilePath))
            {
                return result;
            }
            var profile = TimetableProfile.Load(profilePath);
            var now = AppUtils.PreciseNow();
            var dayName = now.DayOfWeek switch
            {
                DayOfWeek.Monday => "monday",
                DayOfWeek.Tuesday => "tuesday",
                DayOfWeek.Wednesday => "wednesday",
                DayOfWeek.Thursday => "thursday",
                DayOfWeek.Friday => "friday",
                DayOfWeek.Saturday => "saturday",
                _ => "sunday",
            };
            var nowTime = TimeOnly.FromDateTime(now);
            var classCounter = 0;
            for (var i = 0; i < profile.Periods.Count; i++)
            {
                var p = profile.Periods[i];
                var start = p["start"]?.GetValue<string>() ?? "";
                var end = p["end"]?.GetValue<string>() ?? "";
                var ptype = p["type"]?.GetValue<string>() ?? "";
                var isCurrent = TimeOnly.TryParse(start, out var st) && TimeOnly.TryParse(end, out var et) &&
                                st <= nowTime && nowTime < et;

                if (ptype is "课间" or "活动")
                {
                    result.Add(new ScheduleRow("", "", start, end, 0, isCurrent, true, ptype));
                }
                else
                {
                    classCounter++;
                    var subject = profile.Courses.TryGetValue(i.ToString(), out var c) ? c[dayName]?.GetValue<string>() ?? "" : "";
                    result.Add(new ScheduleRow(subject, "", start, end, classCounter, isCurrent, false, ""));
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[课表组件] 本地课表读取失败: {e.Message}");
        }
        return result;
    }

    public static string LatestProfileName()
    {
        var names = Timetable.ListProfiles();
        return names.Count > 0 ? names[^1] : "档案配置-1";
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

// ---------- 新闻热榜
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
        var dark = ThemeSense.IsDark(this);
        var label = _itemLabels[index];
        label.Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
            label.Inlines.Clear();
        var num = new Avalonia.Controls.Documents.Run($"{index + 1}. ")
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(dark
                ? Color.FromArgb(140, 255, 255, 255)
                : Color.FromArgb(115, 0, 0, 0)),
        };
        var text = new Avalonia.Controls.Documents.Run(title)
        {
            FontSize = 15,
            Foreground = new SolidColorBrush(dark ? Color.Parse("#ffffff") : Color.Parse("#1a1a1a")),
        };
        label.Inlines.Add(num);
        label.Inlines.Add(text);
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

// ---------- 今日课表预览
// 规格: 300x550 边距(10,10,10,10) 间距6 标题17px w600 行内容边距(10,7,10,7) 间距12
// 第X节15px(宽68) 课程名20px w500 时间15px 右对齐 当前行底部进度条3px #4cc2ff
// 行背景 深色 op*0.08/当前op*0.18/已过op*0.04 浅色 0.04/0.10/0.02 已过文字#999999

public sealed class TimetablePreviewWidget : WidgetCardBase
{
    private readonly TextBlock _titleText = new();
    private readonly StackPanel _rowsHost = new() { Spacing = 4 };
    private readonly List<(Border Row, ProgressBar Bar, bool IsCurrent)> _rows = new();
    private (string Start, string End)? _currentTimes;

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _progressTimer;
    private string _sig = "";

    public TimetablePreviewWidget(ComponentDefinition definition) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(10, 10, 10, 10),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            RowSpacing = 6,
        };
        _titleText.Text = AppUtils.Tr("timetable.today_schedule");
        _titleText.FontSize = 17;
        _titleText.FontWeight = FontWeight.SemiBold;
        var scroll = new ScrollViewer
        {
            Content = _rowsHost,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        Grid.SetRow(_titleText, 0);
        Grid.SetRow(scroll, 1);
        root.Children.Add(_titleText);
        root.Children.Add(scroll);
        Card.Child = root;
        Content = Card;

        ApplyRowStyle();
        ActualThemeVariantChanged += (_, _) => ApplyRowStyle();
        Config.ComponentCardOpacity.ValueChanged += _ => ApplyRowStyle();
        Config.ComponentCardRadius.ValueChanged += _ => ApplyRowStyle();

        // 原版 showEvent: 5s 刷新 + 1s 进度
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) => RefreshSchedule();
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _refreshTimer.Start();
        _progressTimer.Start();
        RefreshSchedule();
    }

    private void RefreshSchedule()
    {
        var schedule = TimetableScheduleProvider.GetTodaySchedule();
        var now = AppUtils.PreciseNow();

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
        if (sig == _sig)
        {
            UpdateProgress();
            return;
        }
        _sig = sig;
        RebuildRows(schedule, now);
    }

    private void RebuildRows(List<ScheduleRow> schedule, DateTime now)
    {
        _rowsHost.Children.Clear();
        _rows.Clear();
        _currentTimes = null;
        ScheduleRow? currentRow = null;

        // 先找最后一个 current(
        foreach (var row in schedule)
        {
            if (row.IsCurrent)
            {
                currentRow = row;
            }
        }

        if (schedule.Count == 0)
        {
            _rowsHost.Children.Add(new TextBlock
            {
                Text = "今天没有课程",
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.Parse("#888888")),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 8),
            });
            return;
        }

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
                Margin = new Thickness(10, 7, 10, 7),
                ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("68,*,Auto"),
                ColumnSpacing = 12,
            };
            TextBlock idx;
            if (row.IsBreak && isCurrentRow)
            {
                // 课间当前行 空占位对齐原版 pad
                idx = new TextBlock();
            }
            else if (row.IsBreak)
            {
                idx = new TextBlock();
            }
            else
            {
                idx = new TextBlock { Text = $"第{row.Index}节", TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
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
                Text = $"{row.StartTime}~{row.EndTime}",
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(idx, 0);
            Grid.SetColumn(subj, 1);
            Grid.SetColumn(time, 2);
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
                IsVisible = isCurrentRow,
            };
            Grid.SetRow(content, 0);
            Grid.SetRow(bar, 1);
            rowGrid.Children.Add(content);
            rowGrid.Children.Add(bar);

            var border = new Border { Child = rowGrid };
            _rowsHost.Children.Add(border);
            _rows.Add((border, bar, isCurrentRow));
            StyleRow(border, subj, time, idx, bar, isCurrentRow, isPast);
        }
        UpdateProgress();
    }

    // 行样式
    private void StyleRow(Border border, TextBlock subj, TextBlock time, TextBlock idx, ProgressBar bar,
        bool isCurrentRow, bool isPast)
    {
        var dark = ThemeSense.IsDark(this);
        var op = Math.Clamp(Config.ComponentCardOpacity.Value / 100.0, 0, 1);
        double bg;
        if (isCurrentRow)
        {
            bg = op * (dark ? 0.18 : 0.10);
        }
        else if (isPast)
        {
            bg = op * (dark ? 0.04 : 0.02);
        }
        else
        {
            bg = op * (dark ? 0.08 : 0.04);
        }
        border.Background = new SolidColorBrush(dark ? Colors.White : Colors.Black, bg);
        border.CornerRadius = new CornerRadius(Math.Max(8, Config.ComponentCardRadius.Value));

        var textColor = dark ? "#e0e0e0" : "#1a1a1a";
        var pastColor = "#999999";
        var c = isPast ? pastColor : textColor;
        subj.Foreground = new SolidColorBrush(Color.Parse(c));
        subj.FontSize = 20;
        time.Foreground = new SolidColorBrush(Color.Parse(textColor));
        time.FontSize = 15;
        idx.Foreground = new SolidColorBrush(Color.Parse(c));
        idx.FontSize = 15;
        bar.Foreground = new SolidColorBrush(Color.Parse("#4cc2ff"));
        bar.Background = new SolidColorBrush(dark ? Colors.White : Colors.Black, dark ? 0.06 : 0.05);
        bar.CornerRadius = new CornerRadius(1);
    }

    private void ApplyRowStyle()
    {
        var dark = ThemeSense.IsDark(this);
        _titleText.Foreground = new SolidColorBrush(Color.Parse(dark ? "#e0e0e0" : "#1a1a1a"));
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

// ---------- 当前课程
// 规格: 400x200 边距(16,10,16,8) 左右栏 间距14 左列(3:1:1) 课程名40px w600(上20下4)
// 倒计时18px accent 时间进度15px 右列(间距8) 老师/时间段19px w600 下节课19px sub色
// 底部进度条6px 配置 show_teacher/show_next/show_duration/show_countdown/prepare_minutes(3)

public sealed class TimetableNowLessonWidget : WidgetCardBase
{
    private readonly TextBlock _subjectText = new();
    private readonly TextBlock _countdownText = new();
    private readonly TextBlock _timeProgressText = new();
    private readonly TextBlock _teacherText = new();
    private readonly TextBlock _timeText = new();
    private readonly TextBlock _nextText = new();
    private readonly ProgressBar _bottomProgress = new();
    private readonly DispatcherTimer _timer;

    private bool _showTeacher = true;
    private bool _showNext = true;
    private bool _showDuration = true;
    private bool _showCountdown = true;
    private int _prepareMinutes = 3;
    private int _fontScale = 100;

    public TimetableNowLessonWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(16, 10, 16, 8),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto"),
        };
        var main = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,*"),
            ColumnSpacing = 14,
        };

        // 左: 课程名(3) 倒计时(1) 时间进度(1) 原版均 AlignLeft|AlignVCenter
        var left = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("3*,*,*"),
            RowSpacing = 4,
            MinWidth = 100,
        };
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
        left.Children.Add(_subjectText);
        left.Children.Add(_countdownText);
        left.Children.Add(_timeProgressText);

        // 右: 老师 时间段 下节课 + 尾部留白(原版 addStretch) -> 4 等分行 内容垂直居中
        var right = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,*,*,*"),
            RowSpacing = 8,
        };
        _teacherText.Margin = new Thickness(0, 2, 0, 2);
        _teacherText.VerticalAlignment = VerticalAlignment.Center;
        _timeText.Margin = new Thickness(0, 2, 0, 2);
        _timeText.VerticalAlignment = VerticalAlignment.Center;
        _nextText.Margin = new Thickness(0, 2, 0, 2);
        _nextText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_teacherText, 0);
        Grid.SetRow(_timeText, 1);
        Grid.SetRow(_nextText, 2);
        right.Children.Add(_teacherText);
        right.Children.Add(_timeText);
        right.Children.Add(_nextText);

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        main.Children.Add(left);
        main.Children.Add(right);

        _bottomProgress.Height = 6;
        _bottomProgress.Minimum = 0;
        _bottomProgress.Maximum = 100;
        _bottomProgress.Value = 0;
        Grid.SetRow(main, 0);
        Grid.SetRow(_bottomProgress, 1);
        root.Children.Add(main);
        root.Children.Add(_bottomProgress);
        Card.Child = root;
        Content = Card;

        ReadConfig(config);
        ApplyCardStyle();
        ApplyStyle();
        ActualThemeVariantChanged += (_, _) => ApplyStyle();

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
            _subjectText.Text = "--";
            _countdownText.Text = "";
            _teacherText.Text = "--";
            _timeText.Text = "--:-- ~ --:--";
            _nextText.Text = "下节课 --";
            _timeProgressText.Text = "-- min / -- min";
            _bottomProgress.Value = 0;
        }
    }

    // 课间态
    private void RenderPrepare(ScheduleRow prepare, DateTime now)
    {
        _subjectText.Text = prepare.Subject.Length > 0 ? prepare.Subject : "下节课";

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

    // 上课态
    private void RenderNormal(ScheduleRow current, ScheduleRow? nextRow, DateTime now)
    {
        _countdownText.Text = "";
        _subjectText.Text = current.IsBreak
            ? (current.BreakName.Length > 0 ? current.BreakName : "课间休息")
            : (current.Subject.Length > 0 ? current.Subject : "--");

        _teacherText.Text = _showTeacher
            ? (current.IsBreak ? "课间休息" : TeacherDisplay(current.Teacher))
            : "";
        _timeText.Text = $"{current.StartTime}~{current.EndTime}";

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
        var dark = ThemeSense.IsDark(this);
        var text = dark ? "#e0e0e0" : "#1a1a1a";
        var subText = dark ? "#aaaaaa" : "#777777";
        var scale = _fontScale / 100.0;
        double S(int px) => Math.Max(1, px * scale);
        IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

        _subjectText.Foreground = B(text);
        _subjectText.FontSize = S(40);
        _subjectText.FontWeight = FontWeight.SemiBold;
        _countdownText.Foreground = B("#4cc2ff");
        _countdownText.FontSize = S(18);
        _countdownText.FontWeight = FontWeight.SemiBold;
        _timeProgressText.Foreground = B(text);
        _timeProgressText.FontSize = S(15);
        _timeProgressText.FontWeight = FontWeight.SemiBold;
        _teacherText.Foreground = B(text);
        _teacherText.FontSize = S(19);
        _teacherText.FontWeight = FontWeight.SemiBold;
        _timeText.Foreground = B(text);
        _timeText.FontSize = S(19);
        _timeText.FontWeight = FontWeight.SemiBold;
        _nextText.Foreground = B(subText);
        _nextText.FontSize = S(19);
        _nextText.FontWeight = FontWeight.SemiBold;
        _bottomProgress.Foreground = B("#4cc2ff");
    }
}

// ---------- 媒体播放器
// 规格: 400x200 内容边距(16,20,12,20) 间距14 封面160x160 圆角10 淡入300ms
// 标题19px w700 h28 歌手11px w500 h16 歌词12px w700 进度条3px 时间10px 右对齐
// 按钮行间距16 居中 prev28x28(icon20) play32x32(icon24) next28x28
// 色板 深色 标题#F5F5FA 歌手rgba(230,230,235,200) 时间rgba(220,220,225,150) 歌词rgba(235,235,240,170)
//       浅色 标题#1F1F1F 歌手rgba(60,60,67,214) 时间rgba(60,60,67,138) 歌词rgba(44,44,46,153)
// 默认封面 深(40,40,48)/浅(230,230,235) 音符 手绘音符形状

public sealed class MediaPlayerWidget : WidgetCardBase
{
    private sealed record MediaDetail(Lyrics? Lyrics, byte[]? Cover, long Duration);

    private readonly Border _coverHost = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _artistText = new();
    private readonly TextBlock _lyricsText = new();
    private readonly TextBlock _timeText = new();
    private readonly ProgressBar _bar = new();
    private readonly Button _btnPrev;
    private readonly Button _btnPlay;
    private readonly Button _btnNext;
    private readonly TextBlock _playGlyph = new();
    private readonly TextBlock _prevGlyph = new();
    private readonly TextBlock _nextGlyph = new();

    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _progTimer;

    private MediaInfo? _media;
    private Lyrics? _lyrics;
    private string _lastTa = "";
    private bool _hasThumb;
    private bool _playing;
    private long _position;
    private long _duration;
    private bool _detailFetching;
    private readonly Dictionary<string, MediaDetail> _infoCache = new();

    public MediaPlayerWidget(ComponentDefinition definition) : base(definition)
    {
        // 内容层: 边框对齐原版 1px rgba(255,255,255,0.06)/rgba(0,0,0,0.06)
        var root = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(16, 20, 12, 20),
            Spacing = 14,
        };

        // 左侧封面 160x160 圆角10
        _coverHost.Width = 160;
        _coverHost.Height = 160;
        _coverHost.CornerRadius = new CornerRadius(10);
        _coverHost.ClipToBounds = true;
        _coverHost.VerticalAlignment = VerticalAlignment.Center;
        root.Children.Add(_coverHost);

        // 右列
        var right = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,Auto,*"),
            Margin = new Thickness(0, 0, 2, 0),
        };
        var topBlock = new StackPanel { Spacing = 2 };
        _titleText.Height = 28;
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _artistText.Height = 16;
        _artistText.VerticalAlignment = VerticalAlignment.Center;
        topBlock.Children.Add(_titleText);
        topBlock.Children.Add(_artistText);
        Grid.SetRow(topBlock, 0);
        right.Children.Add(topBlock);

        _lyricsText.VerticalAlignment = VerticalAlignment.Bottom;
        _lyricsText.TextWrapping = TextWrapping.Wrap;
        _lyricsText.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(_lyricsText, 1);
        right.Children.Add(_lyricsText);

        var bottomBlock = new StackPanel { Spacing = 4 };
        _bar.Height = 3;
        _bar.Minimum = 0;
        _bar.Maximum = 100;
        bottomBlock.Children.Add(_bar);
        _timeText.TextAlignment = TextAlignment.Right;
        _timeText.MinHeight = 14;
        bottomBlock.Children.Add(_timeText);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 2, 0, 0) };
        _btnPrev = MakeButton(_prevGlyph, 28, 20);
        _btnPlay = MakeButton(_playGlyph, 32, 24);
        _btnNext = MakeButton(_nextGlyph, 28, 20);
        // 两侧拉伸居中
        var leftStretch = new StackPanel();
        var rightStretch = new StackPanel();
        btnRow.Children.Add(leftStretch);
        btnRow.Children.Add(_btnPrev);
        btnRow.Children.Add(_btnPlay);
        btnRow.Children.Add(_btnNext);
        btnRow.Children.Add(rightStretch);
        bottomBlock.Children.Add(btnRow);
        Grid.SetRow(bottomBlock, 3);
        right.Children.Add(bottomBlock);

        root.Children.Add(right);
        Card.Child = root;
        Content = Card;

        _btnPrev.Click += (_, _) => _ = ControlAsync("prev");
        _btnPlay.Click += (_, _) => _ = TogglePlayAsync();
        _btnNext.Click += (_, _) => _ = ControlAsync("next");

        // 轮询间隔 = cfg.mediaUpdateInterval 秒
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, Config.MediaUpdateInterval.Value)) };
        _pollTimer.Tick += (_, _) => _ = PollAsync();
        _progTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _progTimer.Tick += (_, _) => UpdateProgress();

        Config.ShowMediaInfo.ValueChanged += _ => IsVisible = Config.ShowMediaInfo.Value;
        Config.ShowMediaCover.ValueChanged += _ => _coverHost.IsVisible = Config.ShowMediaCover.Value;
        ActualThemeVariantChanged += (_, _) => { ApplyStyle(); UpdatePlayGlyph(); };
        ApplyStyle();
        SetDefaultCover();
        IsVisible = Config.ShowMediaInfo.Value;
        _pollTimer.Start();
        _progTimer.Start();
        _ = PollAsync();
    }

    private static Button MakeButton(TextBlock glyph, double size, double icon)
    {
        glyph.Text = "";
        glyph.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        glyph.FontSize = icon;
        return new Button
        {
            Content = glyph,
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
    }

    // 手绘默认封面
    private void SetDefaultCover()
    {
        var dark = ThemeSense.IsDark(this);
        var bg = dark ? Color.FromRgb(40, 40, 48) : Color.FromRgb(230, 230, 235);
        var iconC = dark ? Color.FromArgb(160, 180, 180, 190) : Color.FromArgb(140, 120, 120, 130);
        var brush = new SolidColorBrush(iconC);

        var grid = new Grid { Width = 100, Height = 100 };
        var head = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 18, Height = 13, Fill = brush,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(27, 56, 0, 0),
        };
        var stem = new Avalonia.Controls.Shapes.Rectangle
        {
            Width = 2.5, Height = 38, Fill = brush,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(58.7, 27.2, 0, 0),
        };
        var flag = new Avalonia.Controls.Shapes.Path
        {
            Stroke = brush,
            StrokeThickness = 2.5,
            StrokeLineCap = PenLineCap.Round,
            Data = Avalonia.Media.Geometry.Parse("M 61.2 27.2 C 73 31 75 41 68 50"),
        };
        grid.Children.Add(head);
        grid.Children.Add(stem);
        grid.Children.Add(flag);
        _coverHost.Background = new SolidColorBrush(bg);
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
                _coverHost.Transitions = null;
                _coverHost.Opacity = 0;
                _coverHost.Child = new Image
                {
                    Source = bitmap,
                    Width = 160,
                    Height = 160,
                    Stretch = Stretch.UniformToFill,
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

            Log.Debug($"[SW] 应用失败 控件已卸载: {e.Message}");
            // 控件已卸载
        }
    }

    private void UpdatePlayGlyph()
    {
        // FluentIcon PLAY/PAUSE_BOLD 等价字形
        _playGlyph.Text = _playing ? "\uE769" : "\uE768";
        _prevGlyph.Text = "\uE892";
        _nextGlyph.Text = "\uE893";
    }

    private void ApplyStyle()
    {
        var dark = ThemeSense.IsDark(this);
        _titleText.Foreground = new SolidColorBrush(dark ? Color.Parse("#F5F5FA") : Color.Parse("#1F1F1F"));
        _titleText.FontSize = 19;
        _titleText.FontWeight = FontWeight.Bold;
        _artistText.Foreground = new SolidColorBrush(dark ? Color.FromArgb(200, 230, 230, 235) : Color.FromArgb(214, 60, 60, 67));
        _artistText.FontSize = 11;
        _artistText.FontWeight = FontWeight.Medium;
        _lyricsText.Foreground = new SolidColorBrush(dark ? Color.FromArgb(170, 235, 235, 240) : Color.FromArgb(153, 44, 44, 46));
        _lyricsText.FontSize = 12;
        _lyricsText.FontWeight = FontWeight.Bold;
        _timeText.Foreground = new SolidColorBrush(dark ? Color.FromArgb(150, 220, 220, 225) : Color.FromArgb(138, 60, 60, 67));
        _timeText.FontSize = 10;
        _timeText.FontWeight = FontWeight.Medium;
        var btnFg = new SolidColorBrush(dark ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(220, 0, 0, 0));
        _prevGlyph.Foreground = btnFg;
        _playGlyph.Foreground = btnFg;
        _nextGlyph.Foreground = btnFg;
        _bar.Foreground = new SolidColorBrush(Color.Parse("#4cc2ff"));
        _bar.Background = new SolidColorBrush(dark ? Colors.White : Colors.Black, dark ? 0.06 : 0.05);
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
            Card.BorderBrush = new SolidColorBrush(dark ? Colors.White : Colors.Black, dark ? 0.06 : 0.06);
        }
    }

    private async Task PollAsync()
    {
        MediaInfo? m = null;
        try
        {
            m = await MediaServices.GetMediaInfoAsync();
            if (m is not null && !m.IsValid())
            {
                m = null;
            }
        }
        catch (Exception e)
        {
            Log.Error($"媒体信息获取异常: {e.Message}");
        }
        await Dispatcher.UIThread.InvokeAsync(() => OnMedia(m));
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
        if (m.TitleArtist != _lastTa)
        {
            DisplayNewSong(m);
        }
        if (m.PositionMs > 0)
        {
            _position = m.PositionMs;
        }
        _playing = m.IsPlaying;
        if (m.DurationMs > 0)
        {
            _duration = m.DurationMs;
        }
        UpdatePlayGlyph();
        if (m.ThumbnailData is { Length: > 0 } && !_hasThumb)
        {
            _hasThumb = true;
            _ = LoadCoverAsync(m.ThumbnailData);
        }
        UpdateProgressUi();
        if (_playing && _lyrics is not null && !_lyrics.IsEmpty())
        {
            UpdateLyrics(_position);
        }
        _coverHost.IsVisible = Config.ShowMediaCover.Value;
    }

    // 新歌
    private void DisplayNewSong(MediaInfo m)
    {
        _lastTa = m.TitleArtist;
        _position = m.PositionMs;
        _playing = m.IsPlaying;
        if (m.DurationMs > 0)
        {
            _duration = m.DurationMs;
        }
        _hasThumb = false;

        var appName = (m.AppName ?? "").ToLowerInvariant();
        var isWebBrowser = appName.Contains("chrome") || appName.Contains("edge")
            || appName.Contains("firefox") || appName.Contains("msedge");

        // 淡入复位 + 默认封面
        _coverHost.Transitions = null;
        _coverHost.Opacity = 1;
        SetDefaultCover();
        _lyrics = null;
        _lyricsText.Text = "";

        var title = m.Title.Length > 0 ? m.Title : AppUtils.Tr("media.unknown_song");
        _titleText.Text = title;
        if (isWebBrowser && m.Artist.Length == 0)
        {
            // 浏览器无歌手 标题换行 隐藏歌手/歌词
            _titleText.TextWrapping = TextWrapping.Wrap;
            _artistText.IsVisible = false;
            _lyricsText.IsVisible = false;
        }
        else
        {
            _titleText.TextWrapping = TextWrapping.NoWrap;
            _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
            _artistText.Text = m.Artist;
            _artistText.IsVisible = true;
            _lyricsText.IsVisible = true;
        }

        if (m.ThumbnailData is { Length: > 0 })
        {
            _hasThumb = true;
            _ = LoadCoverAsync(m.ThumbnailData);
        }
        else if (isWebBrowser)
        {
            _hasThumb = true;
        }
        else
        {
            _ = FetchDetailAsync(m);
        }
    }

    private void NoMedia()
    {
        if (_media is not null)
        {
            Log.Info("[MP] 无媒体播放 重置为空状态");
        }
        _titleText.Text = AppUtils.Tr("media.not_playing");
        _titleText.TextWrapping = TextWrapping.NoWrap;
        _artistText.Text = "";
        _artistText.IsVisible = true;
        _lyricsText.Text = "";
        _lyricsText.IsVisible = true;
        _bar.Value = 0;
        _timeText.Text = "00:00 / 00:00";
        SetDefaultCover();
        _media = null;
        _lyrics = null;
        _lastTa = "";
        _hasThumb = false;
        _playing = false;
        _duration = 0;
        _position = 0;
        UpdatePlayGlyph();
    }

    // 进度秒推
    private void UpdateProgress()
    {
        if (_playing && _duration > 0)
        {
            _position = Math.Min(_position + 1000, _duration);
        }
        UpdateProgressUi();
        if (_playing && _lyrics is not null && !_lyrics.IsEmpty())
        {
            UpdateLyrics(_position);
        }
    }

    private void UpdateProgressUi()
    {
        if (_duration > 0)
        {
            _bar.Value = Math.Min(100, _position / (double)_duration * 100);
            _timeText.Text = $"{Fmt(_position)} / {Fmt(_duration)}";
        }
        else
        {
            _timeText.Text = $"{Fmt(_position)} / --:--";
        }
    }

    private static string Fmt(long ms)
    {
        var s = Math.Max(0, ms / 1000);
        return $"{s / 60}:{s % 60:D2}";
    }

    private void UpdateLyrics(long ms)
    {
        if (_lyrics is null || _lyrics.IsEmpty())
        {
            return;
        }
        var advance = Config.MediaLyricsAdvance.Value;
        var (_, idx) = _lyrics.GetLineAtTime(ms + advance);
        _lyricsText.Text = idx >= 0 && idx < _lyrics.Lines.Count ? _lyrics.Lines[idx].Text : "";
    }

    // 歌曲详情后台抓取
    private async Task FetchDetailAsync(MediaInfo m)
    {
        if (_detailFetching)
        {
            return;
        }
        _detailFetching = true;
        MediaDetail result = new(null, null, 0);
        try
        {
            var svc = MediaServices.GetService(m.AppName);
            if (svc is not null)
            {
                var lyrics = await svc.LyricsAsync(m);
                var cover = await svc.CoverAsync(m);
                var dur = await svc.DurationAsync(m);
                result = new MediaDetail(lyrics, cover is { Length: > 0 } ? cover : null, dur);
            }
        }
        catch (Exception e)
        {
            Log.Debug($"获取歌曲信息失败: {e.Message}");
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _detailFetching = false;
            _infoCache[m.TitleArtist] = result;
            if (_infoCache.Count > 50)
            {
                _infoCache.Remove(_infoCache.Keys.First());
            }
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
            _hasThumb = true;
            _ = LoadCoverAsync(result.Cover);
        }
        if (result.Lyrics is not null)
        {
            _lyrics = result.Lyrics;
            UpdateLyrics(_position);
        }
    }

    // 播放/暂停
    private async Task TogglePlayAsync()
    {
        _playing = !_playing;
        UpdatePlayGlyph();
        try
        {
            await MediaServices.MediaControlAsync(_playing ? "play" : "pause");
        }
        catch (Exception e)
        {
            Log.Warning($"[MP] 控制命令执行失败: {e.Message}");
        }
        await Task.Delay(800);
        await PollAsync();
    }

    // 切上下曲
    private async Task ControlAsync(string action)
    {
        _coverHost.Transitions = null;
        _coverHost.Opacity = 1;
        try
        {
            var ok = action switch
            {
                "next" => await MediaServices.MediaNextAsync(),
                "prev" => await MediaServices.MediaPrevAsync(),
                _ => await MediaServices.MediaControlAsync(action),
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
        await Task.Delay(800);
        await PollAsync();
    }
}

// ---------- 历史上的今天
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
        var dark = ThemeSense.IsDark(this);
        var titleColor = dark ? "#ffffff" : "#1a1a1a";
        var dateColor = dark ? Color.FromArgb(153, 255, 255, 255) : Color.FromArgb(179, 40, 40, 40);
        _titleText.Foreground = new SolidColorBrush(Color.Parse(titleColor));
        _dateText.Foreground = new SolidColorBrush(dateColor);
        _dateText.FontSize = 11;
        for (var i = 0; i < ItemCount; i++)
        {
            var label = _itemLabels[i];
            label.Inlines ??= new Avalonia.Controls.Documents.InlineCollection();
            label.Inlines.Clear();
            if (_years[i].Length > 0)
            {
                label.Inlines.Add(new Avalonia.Controls.Documents.Run($"{_years[i]} · ")
                {
                    FontSize = 12,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#30c361")),
                });
            }
            label.Inlines.Add(new Avalonia.Controls.Documents.Run(_titles[i])
            {
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.Parse(titleColor)),
            });
        }
    }

    // 头部图标
    private Control MakeHistoryIcon()
    {
        try
        {
            var iconPath = Paths.GetResourcePath(System.IO.Path.Combine("Assets", "fluent",
                ThemeSense.IsDark(this) ? "dark" : "light", "ic_fluent_history_24_regular.svg"));
            if (File.Exists(iconPath))
            {
                return new Avalonia.Svg.Skia.Svg(new Uri(iconPath))
                {
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    SvgSource = Avalonia.Svg.Skia.SvgSource.Load(iconPath, null),
                };
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[HIST] 图标缺失留空: {e.Message}");
        }
        // 图标缺失留空占位 保持列宽
        return new Border { Width = 0, Height = 16 };
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

// ---------- 月历
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

    private readonly TextBlock _titleText = new();
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
            Margin = new Thickness(12, 10, 12, 8),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,*"),
            RowSpacing = 4,
        };

        // 标题行
        var titleRow = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,Auto") };
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        titleRow.Children.Add(_titleText);
        var btnCol = new StackPanel();
        btnCol.Children.Add(MakeNavButton("\uE70E", -1));
        btnCol.Children.Add(MakeNavButton("\uE70F", 1));
        Grid.SetColumn(btnCol, 1);
        titleRow.Children.Add(btnCol);
        Grid.SetRow(titleRow, 0);
        root.Children.Add(titleRow);

        // 星期标题
        var wkNames = new[] { "日", "一", "二", "三", "四", "五", "六" };
        var wkRow = new Avalonia.Controls.Primitives.UniformGrid { Rows = 1, Columns = 7 };
        for (var i = 0; i < 7; i++)
        {
            wkRow.Children.Add(new TextBlock
            {
                Text = wkNames[i],
                TextAlignment = TextAlignment.Center,
                FontSize = 13,
            });
        }
        Grid.SetRow(wkRow, 1);
        root.Children.Add(wkRow);

        // 6x7 日期格
        var grid = new Grid();
        for (var c = 0; c < 7; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        }
        for (var r = 0; r < 6; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        }
        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                var cell = new CalendarDayCell();
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
                _cells[r, c] = cell;
            }
        }
        Grid.SetRow(grid, 2);
        root.Children.Add(grid);
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

    private Button MakeNavButton(string glyph, int delta)
    {
        var g = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 10,
        };
        var btn = new Button
        {
            Content = g,
            Width = 28,
            Height = 18,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        btn.Click += (_, _) => GoMonth(delta);
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
        // 对齐原版 _get_day_info 节日优先 节气兜底
        var info = AlmanacService.GetToday(new DateTime(year, month, day));
        if (info is not null)
        {
            sub = ShortHoliday(string.IsNullOrEmpty(info.Holiday) ? info.SolarTerm : info.Holiday);
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

        _titleText.Text = $"{MonthNamesCn[month - 1]} {year}";

        // 周日=0
        var firstWd = ((int)new DateTime(year, month, 1).DayOfWeek) % 7;
        var dim = DateTime.DaysInMonth(year, month);
        var prevMonth = month > 1 ? month - 1 : 12;
        var prevYear = month > 1 ? year : year - 1;
        var prevDim = DateTime.DaysInMonth(prevYear, prevMonth);

        for (var r = 0; r < 6; r++)
        {
            for (var c = 0; c < 7; c++)
            {
                _cells[r, c].Clear();
            }
        }

        // 上月末尾
        for (var i = 0; i < firstWd; i++)
        {
            _cells[0, i].SetData(prevDim - firstWd + 1 + i, "", false, false, i == 0 || i == 6);
        }

        // 本月
        var day = 1;
        for (var r = 0; r < 6 && day <= dim; r++)
        {
            for (var c = 0; c < 7 && day <= dim; c++)
            {
                if (r == 0 && c < firstWd)
                {
                    continue;
                }
                var isToday = year == today.Year && month == today.Month && day == today.Day;
                _cells[r, c].SetData(day, GetDaySubText(year, month, day), true, isToday, c == 0 || c == 6);
                day++;
            }
        }
        Log.Debug($"[CAL] 月历已刷新 {year}-{month:D2} 今日 {today:MM}-{today:dd}");
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
        var dark = ThemeSense.IsDark(this);
        _titleText.Foreground = new SolidColorBrush(Color.Parse(dark ? "#f0f0f0" : "#1a1a1a"));
        _titleText.FontSize = 20;
        _titleText.FontWeight = FontWeight.SemiBold;
        // 星期行颜色
        var idx = 0;
        foreach (var child in ((Avalonia.Controls.Primitives.UniformGrid)((Grid)Card.Child!).Children[1]).Children)
        {
            if (child is TextBlock wk)
            {
                var weekend = idx == 0 || idx == 6;
                wk.Foreground = new SolidColorBrush(
                    weekend
                        ? (dark ? Color.FromArgb(115, 255, 255, 255) : Color.FromArgb(89, 0, 0, 0))
                        : (dark ? Color.FromArgb(191, 255, 255, 255) : Color.FromArgb(140, 0, 0, 0)));
                idx++;
            }
        }
    }
}

/// <summary>月历单格</summary>
public sealed class CalendarDayCell : Control
{
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
        var sz = Math.Min(w, h);
        var mid = h * 0.62; // 62% 给日期
        var dark = ThemeSense.IsDark(this);

        if (_isToday)
        {
            var cx = w / 2 + w * 0.02;
            var cy = mid / 2;
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#00b7c3")), null,
                new Point(cx, cy), sz * 0.40, sz * 0.40);
        }

        // 日期文字色
        IBrush dayBrush;
        if (_isToday)
        {
            dayBrush = Brushes.White;
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
        // 原版 day_rect = QRect(r.left() + int(w*0.04), 0, w, int(mid)) 左侧留 4% 偏移
        DrawCentered(context, _day.ToString(), sz * 0.46, FontWeight.Normal, dayBrush, new Rect(w * 0.04, 0, w, mid));

        // 角标(节日/节气)
        if (_sub.Length > 0 && _isCurrentMonth)
        {
            var subBrush = _isToday
                ? (IBrush)new SolidColorBrush(Colors.White)
                : new SolidColorBrush(dark ? Color.Parse("#c0c0c0") : Color.FromArgb(170, 0, 0, 0));
            // 原版 sub_rect = QRect(r.left(), int(mid), w, int(h - mid))
            DrawCentered(context, _sub, sz * 0.35, FontWeight.Bold, subBrush, new Rect(0, mid, w, h - mid));
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
        MaxHeight = 36,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock _display = new()
    {
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Text = "0",
        FontWeight = FontWeight.Light,
    };
    private string _expression = "";
    private bool _resultShown;

    private static readonly (string Text, int Row, int Col)[] ButtonSpecs =
    {
        ("⌫", 0, 0), ("C", 0, 1), ("%", 0, 2), ("÷", 0, 3),
        ("7", 1, 0), ("8", 1, 1), ("9", 1, 2), ("×", 1, 3),
        ("4", 2, 0), ("5", 2, 1), ("6", 2, 2), ("−", 2, 3),
        ("1", 3, 0), ("2", 3, 1), ("3", 3, 2), ("+", 3, 3),
        ("±", 4, 0), ("0", 4, 1), (".", 4, 2), ("=", 4, 3),
    };

    public CalculatorWidget(ComponentDefinition definition) : base(definition)
    {
        var grid = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,Auto,*,Auto"),
            Margin = new Thickness(12, 20, 12, 12),
        };
        grid.Children.Add(_history);
        Grid.SetRow(_history, 0);
        grid.Children.Add(_display);
        Grid.SetRow(_display, 1);
        var buttons = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,*,*,*,*"),
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,*,*,*"),
        };
        Grid.SetRow(buttons, 3);
        foreach (var (text, row, col) in ButtonSpecs)
        {
            var btn = new Button
            {
                Content = text,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0),
                Margin = new Thickness(2),
            };
            btn.Click += (_, _) => OnButtonClick(text);
            Grid.SetRow(btn, row);
            Grid.SetColumn(btn, col);
            buttons.Children.Add(btn);
        }
        grid.Children.Add(buttons);
        Card.Child = grid;
        Content = Card;
        ApplyCardStyle();
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
            _display.Text = "错误";
            _expression = "";
            _resultShown = false;
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
            return;
        }
        var head = SplitExpression(out var current);
        _history.Text = head.Length > 0 ? DisplayExpr(head) : "";
        _display.Text = current.Length > 0 ? DisplayExpr(current) : "0";
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
        var dark = ThemeSense.IsDark(this);
        _history.Foreground = new SolidColorBrush(Color.Parse(dark ? "#99FFFFFF" : "#99000000"));
        _display.Foreground = new SolidColorBrush(Color.Parse(dark ? "#F2FFFFFF" : "#E6000000"));
        _history.FontSize = 14;
        _display.FontSize = 28;
    }
}


// ===== ExtraWidgets.cs =====

/// <summary>全局系统采样: CPU/RAM/GPU 网络 麦克风分贝</summary>
internal static class SystemSampler
{
    private static System.Diagnostics.PerformanceCounter? _cpu;
    private static System.Diagnostics.PerformanceCounter? _netIn;
    private static System.Diagnostics.PerformanceCounter? _netOut;
    private static bool _perfBroken;

    public static event Action<double, double, double>? PerfReady; // cpu ram gpu
    public static event Action<double, double>? NetReady;          // down up (bits/s)
    public static event Action<double>? DbReady;                   // db

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

/// <summary>进度圆环</summary>
internal sealed class PerfRing : Control
{
    private double _value;

    public string Label { get; set; } = "";
    public string Color { get; set; } = "#0078d4";

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side < 8)
        {
            return;
        }
        var stroke = Math.Max(4, side * 0.087); // 92px 时 8px
        var r = (side - stroke) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var trackPen = new Pen(new SolidColorBrush(ThemeSense.IsDark(this)
            ? AvColor.FromRgb(60, 60, 60)
            : AvColor.FromRgb(225, 225, 225)), stroke);
        ctx.DrawEllipse(null, trackPen, center, r, r);
        if (_value > 0.5)
        {
            var sweep = _value / 100 * 360 - 0.5;
            var start = -90;
            var pen = new Pen(new SolidColorBrush(AvColor.Parse(Color)), stroke)
            {
                LineCap = PenLineCap.Round,
            };
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                var a1 = start * Math.PI / 180;
                var a2 = (start + sweep) * Math.PI / 180;
                var p1 = new Point(center.X + r * Math.Cos(a1), center.Y + r * Math.Sin(a1));
                var p2 = new Point(center.X + r * Math.Cos(a2), center.Y + r * Math.Sin(a2));
                g.BeginFigure(p1, false);
                g.ArcTo(p2, new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);
        }
        var ft = new FormattedText(
            $"{(int)Math.Round(_value)}%",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(Common.AppFontFamily, FontStyle.Normal, FontWeight.Bold),
            side * 0.21,
            ThemeSense.IsDark(this) ? Brushes.White : Brushes.Black);
        ctx.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }
}

/// <summary>便签</summary>
public sealed class StickyNoteWidget : WidgetCardBase
{
    private static readonly Dictionary<string, (string Bg, string Header, string Text)> Colors = new()
    {
        ["yellow"] = ("#FFF9C4", "#FFF176", "#5D4037"),
        ["green"] = ("#C8E6C9", "#A5D6A7", "#2E7D32"),
        ["blue"] = ("#BBDEFB", "#90CAF9", "#1565C0"),
        ["pink"] = ("#F8BBD0", "#F48FB1", "#880E4F"),
        ["orange"] = ("#FFE0B2", "#FFCC80", "#E65100"),
        ["purple"] = ("#E1BEE7", "#CE93D8", "#4A148C"),
    };

    private readonly Border _header = new() { Height = 36 };
    private readonly Border _dot = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5) };
    private readonly TextBlock _date = new() { Text = DateTime.Today.ToString("yyyy-MM-dd"), FontSize = 11 };
    private readonly TextBox _editor = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        FontSize = 13,
        Padding = new Thickness(12, 4, 12, 12),
        Watermark = "记录内容...",
    };
    private (string Bg, string Header, string Text) _c = ("#FFF9C4", "#FFF176", "#5D4037");
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

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        headerRow.Children.Add(_dot);
        headerRow.Children.Add(new Border { Width = 6 });
        headerRow.Children.Add(_date);
        _header.Child = headerRow;

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(_header);
        stack.Children.Add(_editor);
        Card.Child = stack;
        Content = Card;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _editor.TextChanged += (_, _) =>
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
        ApplyStickyStyle();
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

    // 自定义底色(不随主题)
    protected override void ApplyCardStyle()
    {
        _dot.Background = new SolidColorBrush(AvColor.Parse(_c.Header));
        _date.Foreground = new SolidColorBrush(AvColor.Parse(_c.Text));
        _editor.Foreground = new SolidColorBrush(AvColor.Parse(_c.Text));
        _editor.CaretBrush = new SolidColorBrush(AvColor.Parse(_c.Text));
        Card.Background = new SolidColorBrush(AvColor.Parse(_c.Bg));
        Card.BorderBrush = new SolidColorBrush(AvColor.Parse(_c.Header));
        Card.BorderThickness = new Thickness(1);
        Card.CornerRadius = new CornerRadius(Math.Max(8, Config.ComponentCardRadius.Value));
    }

    private void ApplyStickyStyle() => ApplyCardStyle();
}

/// <summary>快捷启动图标项</summary>
internal sealed class QuickLaunchItem
{
    public string Name = "";
    public string Path = "";
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
    private readonly TextBlock _placeholder = new()
    {
        Text = AppUtils.Tr("quick_launch_component.click_to_config"),
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

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
        host.Children.Add(_placeholder);
        Card.Child = host;
        Content = Card;
        UpdateApps();
        Config.ShowQuickLaunch.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchApps.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchIconSize.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchIconSpacing.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchShowLabels.ValueChanged += _ => UpdateApps();
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
        _dock.Children.Clear();
        var size = (double)Config.QuickLaunchIconSize.Value;
        var spacing = Config.QuickLaunchIconSpacing.Value;
        var showLabels = Config.QuickLaunchShowLabels.Value;
        if (apps.Count == 0)
        {
            _dock.IsVisible = false;
            _placeholder.IsVisible = true;
            return;
        }
        _dock.IsVisible = true;
        _placeholder.IsVisible = false;
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
                Background = new SolidColorBrush(ThemeSense.IsDark(this)
                    ? AvColor.FromRgb(50, 50, 50)
                    : AvColor.FromRgb(240, 240, 240)),
                Child = new TextBlock
                {
                    Text = app.Name.Length > 0 ? app.Name[..1].ToUpperInvariant() : "?",
                    FontSize = size * 0.42,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = ThemeSense.IsDark(this) ? Brushes.White : Brushes.DimGray,
                },
            };
            TrySetExeIcon(app.Path, iconBorder);
            cell.Children.Add(iconBorder);
            if (showLabels)
            {
                cell.Children.Add(new TextBlock
                {
                    Text = app.Name,
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = size + 16,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }
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

/// <summary>快捷启动八宫格</summary>
public sealed class QuickLaunchGridWidget : WidgetCardBase
{
    private const int CellCount = 8;
    private const int ColumnCount = 4;

    private readonly Avalonia.Controls.Primitives.UniformGrid _grid = new() { Columns = ColumnCount, Rows = 2 };
    private readonly List<QuickLaunchItem?> _apps = new(new QuickLaunchItem?[CellCount]);
    private string? _storeFile;

    public QuickLaunchGridWidget(ComponentDefinition definition) : base(definition)
    {
        Card.Child = _grid;
        Content = Card;
        RebuildCells();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_storeFile is null && !string.IsNullOrEmpty(ComponentId))
        {
            _storeFile = IOPath.Combine(Paths.DataUser, $"qlgrid_{ComponentId}.json");
            try
            {
                if (File.Exists(_storeFile)
                    && JsonNode.Parse(File.ReadAllText(_storeFile)) is JsonObject obj
                    && obj["apps"] is JsonArray arr)
                {
                    _apps.Clear();
                    foreach (var n in arr)
                    {
                        if (n is JsonObject o)
                        {
                            _apps.Add(new QuickLaunchItem
                            {
                                Name = (string?)o["name"] ?? "",
                                Path = (string?)o["path"] ?? "",
                            });
                        }
                        else
                        {
                            _apps.Add(null);
                        }
                    }
                    RebuildCells();
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[QLG] 读取配置失败: {ex.Message}");
            }
        }
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
                arr.Add(a is null
                    ? null
                    : new JsonObject { ["name"] = a.Name, ["path"] = a.Path });
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
        _grid.Children.Clear();
        var dark = ThemeSense.IsDark(this);
        for (var i = 0; i < CellCount; i++)
        {
            var app = i < _apps.Count ? _apps[i] : null;
            var border = new Border
            {
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(dark
                    ? AvColor.FromRgb(255, 255, 255)
                    : AvColor.FromRgb(0, 0, 0), app is null ? 0.04 : 0.06),
                Child = new TextBlock
                {
                    Text = app is null ? "+" : (app.Name.Length > 0 ? app.Name[..1].ToUpperInvariant() : "?"),
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = app is null ? 0.4 : 1,
                },
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            var index = i;
            var path = app?.Path ?? "";
            border.PointerReleased += (_, ev) =>
            {
                ev.Handled = true;
                if (app is not null && path.Length > 0)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[QLG] 启动失败: {ex.Message}");
                    }
                }
                else
                {
                    PickAndAssign(index);
                }
            };
            _grid.Children.Add(border);
        }
    }

    private async void PickAndAssign(int index)
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
                        new("程序") { Patterns = new[] { "*.exe", "*.lnk" } },
                    },
                });
            if (files is { Count: > 0 })
            {
                var path = files[0].Path.LocalPath;
                _apps[index] = new QuickLaunchItem
                {
                    Name = IOPath.GetFileNameWithoutExtension(path),
                    Path = path,
                };
                Save();
                RebuildCells();
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[QLG] 选择失败: {e.Message}");
        }
    }
}

/// <summary>计时与倒计时</summary>
public sealed class TimerCountdownWidget : WidgetCardBase
{
    private readonly TextBlock _tabTimer = MakeTab("timer_countdown.timer");
    private readonly TextBlock _tabCountdown = MakeTab("timer_countdown.countdown");

    // 计时页: 显示 + 按钮栈(开始 / 暂停+取消)
    private readonly TextBlock _timerDisplay = MakeDisplay();
    private readonly StackPanel _timerStartRow = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly StackPanel _timerRunRow = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        Spacing = 12,
    };
    private readonly Grid _timerBtnStack = new() { Height = 40 };

    // 倒计时页: 设置(三列 + 开始) / 运行(显示 + 暂停·取消)
    private readonly TextBlock _cdDisplay = MakeDisplay();
    private readonly StackPanel _cdSetup = new()
    {
        Orientation = Orientation.Vertical,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = 8,
    };
    private readonly StackPanel _cdRun = new()
    {
        Orientation = Orientation.Vertical,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = 8,
    };
    private readonly Grid _cdContentStack = new();
    private readonly StackPanel _cdRunBtns = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        Spacing = 12,
    };

    // 三列时间输入
    private readonly TimeColumnWidget _hhCol = new("timer_countdown.hours", 0, 99, 0);
    private readonly TimeColumnWidget _mmCol = new("timer_countdown.minutes", 0, 59, 0);
    private readonly TimeColumnWidget _ssCol = new("timer_countdown.seconds", 0, 59, 0);

    private readonly Grid _modeStack = new();

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _countdownMode;
    private bool _running;
    private bool _paused;
    private int _elapsed;
    private int _remaining;

    public TimerCountdownWidget(ComponentDefinition definition) : base(definition)
    {
        Width = 220;
        Height = 170;

        // ---- 顶部 Pivot 32px ----
        var pivot = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Height = 32,
            Spacing = 24,
            VerticalAlignment = VerticalAlignment.Center,
        };
        pivot.Children.Add(_tabTimer);
        pivot.Children.Add(_tabCountdown);

        // ---- 计时页 ----
        _timerPage = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 12, 0, 0),
            Spacing = 4,
        };
        _timerPage.Children.Add(_timerDisplay);
        _timerDisplay.HorizontalAlignment = HorizontalAlignment.Center;

        // 计时按钮栈: 页0=开始(120x32) 页1=暂停+取消(80x30)
        _timerStartRow.Children.Add(MakeButton("timer_countdown.start", 120, 32, OnTimerStart, primary: true));
        _tsPause = MakeButton("timer_countdown.pause", 80, 30, () => TogglePause(_tsPause));
        _timerRunRow.Children.Add(_tsPause);
        _timerRunRow.Children.Add(MakeButton("timer_countdown.cancel", 80, 30, OnTimerCancel));
        _timerBtnStack.Children.Add(_timerStartRow);
        _timerBtnStack.Children.Add(_timerRunRow);
        _timerPage.Children.Add(_timerBtnStack);

        // ---- 倒计时页 ----
        _cdPage = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 12, 0, 0),
            Spacing = 4,
        };

        // 设置页: 三列 + 开始
        var cols = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
        };
        cols.Children.Add(_hhCol);
        cols.Children.Add(_mmCol);
        cols.Children.Add(_ssCol);
        _cdSetup.Children.Add(cols);
        _cdSetup.Children.Add(MakeButton("timer_countdown.start", 120, 32, OnCountdownStart, primary: true));

        // 运行页: 显示 + 暂停/取消
        _cdRun.Children.Add(_cdDisplay);
        _cdRun.Children.Add(_cdRunBtns);
        _cdPause = MakeButton("timer_countdown.pause", 80, 30, () => TogglePause(_cdPause));
        _cdRunBtns.Children.Add(_cdPause);
        _cdRunBtns.Children.Add(MakeButton("timer_countdown.cancel", 80, 30, OnCountdownCancel));

        _cdContentStack.Children.Add(_cdSetup);
        _cdContentStack.Children.Add(_cdRun);
        _cdPage.Children.Add(_cdContentStack);

        // 外层: pivot(固定在顶) + modeStack(撑满)
        var root = new Grid { RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*") };
        Grid.SetRow(pivot, 0);
        Grid.SetRow(_modeStack, 1);
        root.Children.Add(pivot);
        root.Children.Add(_modeStack);
        Card.Child = root;
        Content = Card;

        _tabTimer.PointerReleased += (_, e) => { e.Handled = true; SwitchMode(false); };
        _tabCountdown.PointerReleased += (_, e) => { e.Handled = true; SwitchMode(true); };
        _timer.Tick += (_, _) => Tick();
        SwitchMode(false);
    }

    private Button? _tsPause;
    private Button? _cdPause;
    private StackPanel? _timerPage;
    private StackPanel? _cdPage;

    private static TextBlock MakeTab(string key)
    {
        return new TextBlock
        {
            Text = AppUtils.Tr(key),
            FontSize = 14,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
    }

    // 数字显示 48pt bold
    private static TextBlock MakeDisplay()
    {
        return new TextBlock
        {
            Text = "00:00:00",
            FontSize = 48,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
    }

    private static Button MakeButton(string key, double width, double height, Action onClick, bool primary = false)
    {
        var btn = new Button
        {
            Content = AppUtils.Tr(key),
            Width = width,
            Height = height,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (primary)
        {
            // 对齐原版 PrimaryPushButton 强调色
            TryApplyAccentForeground(btn);
        }
        btn.Click += (_, _) => onClick();
        return btn;
    }

    // 应用主题强调色背景
    private static void TryApplyAccentForeground(Button btn)
    {
        void Apply()
        {
            if (btn.TryFindResource("SystemAccentColor", out var accent) && accent is Color c)
            {
                btn.Background = new SolidColorBrush(c);
            }
            if (btn.TryFindResource("TextOnAccentFillColorPrimaryBrush", out var fg) && fg is IBrush b)
            {
                btn.Foreground = b;
            }
        }
        Apply();
        btn.ActualThemeVariantChanged += (_, _) => Apply();
    }

    private void SwitchMode(bool countdown)
    {
        // 只有停止时能切 切后重置
        if (_running)
        {
            ResetState();
        }
        _countdownMode = countdown;
        UpdateTabs();
        // 用可见性切换代替重建(原版 QStackedWidget) 避免重复挂父
        _modeStack.Children.Clear();
        if (_countdownMode)
        {
            if (_cdPage is not null)
            {
                _modeStack.Children.Add(_cdPage);
            }
            SetCdContent(0);
            SetDisplay(_cdDisplay, 0, 0, 0);
        }
        else
        {
            if (_timerPage is not null)
            {
                _modeStack.Children.Add(_timerPage);
            }
            SetTimerBtn(0);
            SetDisplay(_timerDisplay, 0, 0, 0);
        }
    }

    private void SetTimerBtn(int idx)
    {
        _timerStartRow.IsVisible = idx == 0;
        _timerRunRow.IsVisible = idx == 1;
    }

    private void SetCdContent(int idx)
    {
        _cdSetup.IsVisible = idx == 0;
        _cdRun.IsVisible = idx == 1;
    }

    private void UpdateTabs()
    {
        var active = new SolidColorBrush(ThemeSense.IsDark(this) ? AvColor.FromRgb(255, 255, 255) : AvColor.FromRgb(0, 0, 0));
        var idle = new SolidColorBrush(ThemeSense.IsDark(this)
            ? AvColor.FromRgb(255, 255, 255)
            : AvColor.FromRgb(0, 0, 0), 0.45);
        _tabTimer.Foreground = !_countdownMode ? active : idle;
        _tabCountdown.Foreground = _countdownMode ? active : idle;
    }

    private void OnTimerStart()
    {
        Log.Debug("[TIMER] 正计时启动");
        _elapsed = 0;
        SetTimerBtn(1);
        if (_tsPause is not null)
        {
            _tsPause.Content = Tr("timer_countdown.pause");
        }
        _running = true;
        _paused = false;
        _timer.Start();
    }

    private void OnTimerCancel()
    {
        Log.Debug($"[TIMER] 正计时复位: 已计 {_elapsed}s");
        ResetState();
        SetTimerBtn(0);
        SetDisplay(_timerDisplay, 0, 0, 0);
    }

    private void OnCountdownStart()
    {
        var total = _hhCol.Value * 3600 + _mmCol.Value * 60 + _ssCol.Value;
        if (total <= 0)
        {
            Log.Warning("[TIMER] 未设置时长");
            _cdDisplay.Text = Tr("timer_countdown.set_time_hint");
            return;
        }
        Log.Debug($"[TIMER] 启动 {total}s");
        _remaining = total;
        if (_cdPause is not null)
        {
            _cdPause.Content = Tr("timer_countdown.pause");
        }
        SetCdContent(1);
        UpdateDisplay();
        _running = true;
        _paused = false;
        _timer.Start();
    }

    private void OnCountdownCancel()
    {
        Log.Debug($"[TIMER] 倒计时取消: 剩余 {_remaining}s");
        ResetState();
        SetCdContent(0);
        SetDisplay(_cdDisplay, 0, 0, 0);
    }

    private void TogglePause(Button? btn)
    {
        if (!_running)
        {
            return;
        }
        _paused = !_paused;
        Log.Debug($"[TIMER] {(_paused ? "暂停" : "恢复")}");
        if (btn is not null)
        {
            btn.Content = Tr(_paused ? "timer_countdown.resume" : "timer_countdown.pause");
        }
        if (_paused)
        {
            _timer.Stop();
        }
        else
        {
            _timer.Start();
        }
    }

    private static string Tr(string key) => AppUtils.Tr(key);

    private void ResetState()
    {
        _timer.Stop();
        _running = false;
        _paused = false;
        _elapsed = 0;
        _remaining = 0;
    }

    private void Tick()
    {
        if (!_running || _paused)
        {
            return;
        }
        if (_countdownMode)
        {
            _remaining--;
            if (_remaining <= 0)
            {
                _remaining = 0;
                UpdateDisplay();
                _timer.Stop();
                _running = false;
                Log.Info("[TIMER] 结束");
                SetCdContent(0);
                SetDisplay(_cdDisplay, 0, 0, 0);
                return;
            }
        }
        else
        {
            _elapsed++;
        }
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        var secs = _countdownMode ? _remaining : _elapsed;
        var target = _countdownMode ? _cdDisplay : _timerDisplay;
        SetDisplay(target, secs / 3600, secs % 3600 / 60, secs % 60);
    }

    private static void SetDisplay(TextBlock label, int h, int m, int s)
    {
        label.Text = $"{h:D2}:{m:D2}:{s:D2}";
    }

    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        var brush = ThemeSense.IsDark(this) ? Brushes.White : Brushes.Black;
        _timerDisplay.Foreground = brush;
        _cdDisplay.Foreground = brush;
        UpdateTabs();
    }
}

/// <summary>计时器时间输入列</summary>
public sealed class TimeColumnWidget : StackPanel
{
    private readonly int _min;
    private readonly int _max;
    private int _value;
    private readonly TextBlock _valueLabel;

    public int Value => _value;

    public TimeColumnWidget(string labelKey, int minVal, int maxVal, int defaultValue)
    {
        _min = minVal;
        _max = maxVal;
        _value = Math.Clamp(defaultValue, minVal, maxVal);

        Width = 80;
        Orientation = Orientation.Vertical;
        Spacing = 2;
        Margin = new Thickness(2);
        HorizontalAlignment = HorizontalAlignment.Center;

        Children.Add(new TextBlock
        {
            Text = AppUtils.Tr(labelKey),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        });

        Children.Add(MakeChevron(true));
        _valueLabel = new TextBlock
        {
            Text = $"{_value:D2}",
            FontSize = 28,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Height = 44,
        };
        Children.Add(_valueLabel);
        Children.Add(MakeChevron(false));
    }

    private Button MakeChevron(bool up)
    {
        var glyph = new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse(up ? "M 4,10 L 10,4 L 16,10" : "M 4,4 L 10,10 L 16,4"),
            Stroke = new SolidColorBrush(ThemeSense.IsDark(this) ? Colors.White : Colors.Black),
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var btn = new Button
        {
            Content = glyph,
            Width = 40,
            Height = 28,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        btn.Click += (_, _) => SetValue(_value + (up ? 1 : -1));
        return btn;
    }

    public void SetValue(int val)
    {
        var old = _value;
        _value = Math.Clamp(val, _min, _max);
        if (_value != old)
        {
            _valueLabel.Text = $"{_value:D2}";
        }
    }
}

/// <summary>性能监测</summary>
public sealed class PerformanceMonitorWidget : WidgetCardBase
{
    private static readonly (string Key, string Color, string Label)[] Metrics =
    {
        ("cpu", "#0078d4", "CPU"),
        ("ram", "#8b5cf6", "RAM"),
        ("gpu", "#10b981", "GPU"),
    };

    private readonly Dictionary<string, PerfRing> _rings = new();

    public PerformanceMonitorWidget(ComponentDefinition definition) : base(definition)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var (key, color, label) in Metrics)
        {
            var ring = new PerfRing { Label = label, Color = color, Width = 92, Height = 92 };
            _rings[key] = ring;
            var box = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 6,
                Width = 110,
            };
            box.Children.Add(ring);
            box.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.75,
            });
            row.Children.Add(box);
        }
        Card.Child = row;
        Content = Card;

        SystemSampler.PerfReady += OnPerf;
        SystemSampler.EnsureStarted();
    }

    private void OnPerf(double cpu, double ram, double gpu)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_rings.TryGetValue("cpu", out var c))
            {
                c.Value = cpu;
            }
            if (_rings.TryGetValue("ram", out var r))
            {
                r.Value = ram;
            }
            if (_rings.TryGetValue("gpu", out var g))
            {
                g.Value = gpu;
            }
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

/// <summary>
/// 书写板
/// 卡片点击开启全屏透明书写层 笔/橡皮/撤销/清除 等效 Inkeys 分层渲染
/// </summary>
public sealed class WritingPadWidget : WidgetCardBase
{
    private static WritingPadOverlay? _overlay;

    public WritingPadWidget(ComponentDefinition definition) : base(definition)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
        };
        var btn = new Button
        {
            Content = AppUtils.Tr("writing_pad.open"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinWidth = 120,
        };
        btn.Click += (_, _) => ToggleOverlay();
        stack.Children.Add(btn);
        Card.Child = stack;
        Content = Card;
        ApplyCardStyle();
    }

    public static void ToggleOverlay()
    {
        if (_overlay is { IsVisible: true })
        {
            _overlay.Close();
            _overlay = null;
            return;
        }
        _overlay = new WritingPadOverlay();
        _overlay.Show();
    }
}

/// <summary>全屏书写层</summary>
public sealed class WritingPadOverlay : Window
{
    private readonly InkPadControl _pad = new();

    public WritingPadOverlay()
    {
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        SystemDecorations = Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        WindowState = WindowState.Maximized;

        var padHost = new Border { Child = _pad };
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 24),
            Spacing = 8,
        };
        AddTool(toolbar, "🖊", () => _pad.Tool.Mode = ToolMode.Pen);
        AddTool(toolbar, "🧽", () => _pad.Tool.Mode = ToolMode.Eraser);
        AddTool(toolbar, "↩", () => _pad.Undo());
        AddTool(toolbar, "🗑", () => _pad.ClearAll());
        AddTool(toolbar, "✕", () => Close());

        var root = new Panel();
        root.Children.Add(padHost);
        root.Children.Add(toolbar);
        Content = root;
    }

    private void AddTool(StackPanel bar, string glyph, Action action)
    {
        var btn = new Button
        {
            Content = glyph,
            FontSize = 18,
            Width = 44,
            Height = 40,
            Padding = new Thickness(0),
        };
        btn.Click += (_, _) => action();
        bar.Children.Add(btn);
    }
}

internal enum ToolMode
{
    Pen,
    Eraser,
}

internal sealed class WritingTool
{
    public ToolMode Mode = ToolMode.Pen;
    public double Width = 3;
    public Color Color = Colors.Red;
}

/// <summary>墨迹渲染控件: 笔画列表 直绘 橡皮=删除命中笔画 撤销=弹栈</summary>
internal sealed class InkPadControl : Control
{
    private readonly List<List<Point>> _strokes = new();
    private readonly WritingTool _tool;
    private List<Point>? _current;

    /// <summary>渲染与指针处理实际使用的工具实例 工具栏必须改这一个(此前 Overlay 自持实例导致橡皮失效)</summary>
    public WritingTool Tool => _tool;

    public InkPadControl()
    {
        _tool = new WritingTool();
        ClipToBounds = true;
    }

    public void Undo()
    {
        if (_strokes.Count > 0)
        {
            _strokes.RemoveAt(_strokes.Count - 1);
            InvalidateVisual();
        }
    }

    public void ClearAll()
    {
        _strokes.Clear();
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _current = new List<Point> { pt.Position };
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_current is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        var pt = e.GetCurrentPoint(this).Position;
        if (_tool.Mode == ToolMode.Eraser)
        {
            // 橡皮: 删除经过的笔画
            for (var i = _strokes.Count - 1; i >= 0; i--)
            {
                foreach (var p in _strokes[i])
                {
                    if (Math.Abs(p.X - pt.X) < 12 && Math.Abs(p.Y - pt.Y) < 12)
                    {
                        _strokes.RemoveAt(i);
                        break;
                    }
                }
            }
        }
        else
        {
            _current.Add(pt);
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_current is { Count: > 0 } && _tool.Mode == ToolMode.Pen)
        {
            _strokes.Add(_current);
        }
        _current = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var dark = ThemeSense.IsDark(this);
        var pen = new Pen(new SolidColorBrush(dark ? Colors.White : Colors.Black), _tool.Width)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        foreach (var stroke in _strokes)
        {
            if (stroke.Count < 2)
            {
                continue;
            }
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(stroke[0], false);
                for (var i = 1; i < stroke.Count; i++)
                {
                    g.LineTo(stroke[i]);
                }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);
        }
        if (_current is { Count: > 1 } cur && _tool.Mode == ToolMode.Pen)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(cur[0], false);
                for (var i = 1; i < cur.Count; i++)
                {
                    g.LineTo(cur[i]);
                }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);
        }
    }
}


// ===== NativeWidgets.cs =====

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
/// 400x400 视口: 表盘/三档刻度/数字/三针+阴影/秒针金 #C9A66B
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

    private bool Dark() => ThemeSense.IsDark(this);
    private int FaceRadius => Config.ComponentCardRadius.Value * 2;

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
            return new Point(Math.Sin(rad) * r, -Math.Cos(rad) * r);
        }

        private bool _renderLogged;

        public override void Render(DrawingContext ctx)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (!_renderLogged)
            {
                _renderLogged = true;
                Glimpseon.Core.Log.Debug($"[SQ1] Render bounds={w:F0}x{h:F0} dark={_owner.Dark()}");
            }
            if (w < 10 || h < 10)
            {
                return;
            }
            var s = Math.Min(w, h) / 400.0;
            var matrix = Matrix.CreateTranslation((float)(w / 2), (float)(h / 2))
                         * Matrix.CreateScale((float)s, (float)s)
                         * Matrix.CreateTranslation(-200, -200);
            using (ctx.PushTransform(matrix))
            {
                RenderFace(ctx);
            }
        }

        private void RenderFace(DrawingContext ctx)
        {
            var dark = _owner.Dark();
            var face = NativeTheme.Parse(dark ? "#000000" : "#ffffff");
            var tickMajor = NativeTheme.Parse(dark ? "#ffffff" : "#1d1d1f");
            var tickMinor = NativeTheme.Parse(dark ? "#888888" : "#777777");
            var ink = NativeTheme.Parse(dark ? "#ffffff" : "#1d1d1f");
            var second = NativeTheme.Parse("#C9A66B");

            // 表盘 rx=$radius(卡片圆角 x2)
            ctx.FillRectangle(NativeTheme.Brush(face), new Rect(0, 0, 400, 400), (float)_owner.FaceRadius);

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

            // 时针 M -5 18 L -3 -72 L 0 -82 L 3 -72 L 5 18 Z
            DrawHand(ctx, ink, _hourAngle, new Point(-5, 18),
                new[] { new Point(-3, -72), new Point(0, -82), new Point(3, -72), new Point(5, 18) }, shadow: true);
            // 分针 M -3.5 22 L -2.5 -118 L 0 -128 L 2.5 -118 L 3.5 22 Z
            DrawHand(ctx, ink, _minuteAngle, new Point(-3.5, 22),
                new[] { new Point(-2.5, -118), new Point(0, -128), new Point(2.5, -118), new Point(3.5, 22) }, shadow: true);

            // 秒针: (0,28)-(0,-158) + 尾 (0,0)-(0,32) 圆帽
            using (ctx.PushTransform(Matrix.CreateRotation(_secondAngle * Math.PI / 180.0) * Matrix.CreateTranslation(200, 200)))
            {
                var pen = new Pen(NativeTheme.Brush(second), 2, null, PenLineCap.Round, PenLineJoin.Round);
                ctx.DrawLine(pen, new Point(0, 28), new Point(0, -158));
                ctx.DrawLine(pen, new Point(0, 0), new Point(0, 32));
            }

            // 中轴: r9 秒针色 + r4.5 表盘色
            ctx.DrawEllipse(NativeTheme.Brush(second), null, new Point(200, 200), 9, 9);
            ctx.DrawEllipse(NativeTheme.Brush(face), null, new Point(200, 200), 4.5, 4.5);
        }

        private static void DrawNumeral(DrawingContext ctx, Typeface typeface, string text, int cx, int cy, Color color)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, 50, NativeTheme.Brush(color));
            ctx.DrawText(ft, new Point(cx - ft.WidthIncludingTrailingWhitespace / 2, cy + 17.5 - ft.Baseline));
        }

        private void DrawHand(DrawingContext ctx, Color ink, double angle, Point start, Point[] pts, bool shadow)
        {
            var fig = new PathFigure { StartPoint = start, IsClosed = true, IsFilled = true };
            foreach (var p in pts)
            {
                fig.Segments!.Add(new LineSegment { Point = p });
            }
            var geo = new PathGeometry { Figures = { fig } };
            var matrix = Matrix.CreateRotation(angle * Math.PI / 180.0) * Matrix.CreateTranslation(200, 200);
            using (ctx.PushTransform(matrix))
            {
                if (shadow)
                {
                    // 原版 feDropShadow dy=1.5 blur=2 opacity .5/.18: 半透明偏移形近似
                    var op = _owner.Dark() ? 0.28 : 0.12;
                    using (ctx.PushTransform(Matrix.CreateTranslation(0, 1.5)))
                    {
                        ctx.DrawGeometry(new SolidColorBrush(Colors.Black, op), null, geo);
                    }
                }
                ctx.DrawGeometry(NativeTheme.Brush(ink), null, geo);
            }
        }
    }
}

/// <summary>
/// 方形钟表 II(数字式 渐隐刻度)
/// 200x200 视口: 60 刻度按秒龄渐隐(R=90/max(|dx|,|dy|) 42/48°-5) HH:mm 52px w600
/// </summary>
public sealed class NativeSquareClock2Widget : WidgetCardBase
{
    private readonly DigitalFaceControl _face;

    public NativeSquareClock2Widget(ComponentDefinition definition) : base(definition)
    {
        _face = new DigitalFaceControl(this);
        Card.Child = _face;
        Content = Card;
    }

    private bool Dark() => ThemeSense.IsDark(this);
    private int FaceRadius => Config.ComponentCardRadius.Value * 2;

    private sealed class DigitalFaceControl : Control
    {
        private const int GradN = 35;
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
            var matrix = Matrix.CreateTranslation((float)(w / 2), (float)(h / 2))
                         * Matrix.CreateScale((float)s, (float)s)
                         * Matrix.CreateTranslation(-100, -100);
            using (ctx.PushTransform(matrix))
            {
                RenderFace(ctx);
            }
        }

        private void RenderFace(DrawingContext ctx)
        {
            var dark = _owner.Dark();
            var face = NativeTheme.Parse(dark ? "#000000" : "#ffffff");
            var ink = NativeTheme.Parse(dark ? "#ffffff" : "#000000");
            var oldC = NativeTheme.Parse(dark ? "#333333" : "#dddddd");
            var newC = NativeTheme.Parse(dark ? "#999999" : "#555555");

            ctx.FillRectangle(NativeTheme.Brush(face), new Rect(0, 0, 200, 200), (float)_owner.FaceRadius);

            var now = DateTime.Now;
            var sec = now.Second;
            for (var i = 0; i < 60; i++)
            {
                var a = i * 6 * Math.PI / 180.0;
                var dx = Math.Sin(a);
                var dy = -Math.Cos(a);
                var r = 90.0 / Math.Max(Math.Abs(dx), Math.Abs(dy));
                var deg = (i * 6) % 90;
                if (deg == 42 || deg == 48)
                {
                    r -= 5;
                }
                var age = (sec - i + 60) % 60;
                Color c;
                if (age <= GradN)
                {
                    var t = Math.Sqrt(age / (double)GradN);
                    c = Color.FromRgb(
                        (byte)Math.Round(newC.R + (oldC.R - newC.R) * t),
                        (byte)Math.Round(newC.G + (oldC.G - newC.G) * t),
                        (byte)Math.Round(newC.B + (oldC.B - newC.B) * t));
                }
                else
                {
                    c = oldC;
                }
                var pen = new Pen(NativeTheme.Brush(c), 2, null, PenLineCap.Round, PenLineJoin.Round);
                ctx.DrawLine(pen, new Point(100 + dx * r, 100 + dy * r), new Point(100 + dx * (r - 8), 100 + dy * (r - 8)));
            }

            // HH:mm 52px w600 letter-spacing -1
            var typeface = new Typeface(Common.AppFontFamily, FontStyle.Normal, FontWeight.SemiBold);
            var text = now.ToString("HH:mm");
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, 52, NativeTheme.Brush(ink));
            ctx.DrawText(ft, new Point(100 - ft.WidthIncludingTrailingWhitespace / 2 - 0.5, 100 - ft.Baseline));
        }
    }
}


// ===== NativeCalendarAlmanac.cs =====

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
        "today" => NativeTheme.Parse("#ff6b6b"),
        "selected" => NativeTheme.Parse("#4ade80"),
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
        "today" => NativeTheme.Parse("#e5484d"),
        "selected" => NativeTheme.Parse("#30c361"),
        _ => Colors.Black,
    };

    private readonly TextBlock _title = new();
    private readonly UniformGrid _daysGrid = new() { Columns = 7, Rows = 6 };
    private readonly List<Border> _cells = new();
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
                FontSize = 9,
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
            var cell = new Border
            {
                CornerRadius = new CornerRadius(4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
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
        ApplyPalette();
        RenderMonth();
    }

    private bool IsDarkNow => ThemeSense.IsDark(this);

    private void BuildHeader()
    {
        _prevBtn.Width = 22;
        _prevBtn.Height = 22;
        _prevBtn.CornerRadius = new CornerRadius(4);
        _prevBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _prevBtn.Child = new TextBlock { Text = "‹", FontSize = 13, TextAlignment = TextAlignment.Center };
        _prevBtn.PointerEntered += (_, _) => _prevBtn.Background = new SolidColorBrush(T("btnhover", IsDarkNow));
        _prevBtn.PointerExited += (_, _) => _prevBtn.Background = null;
        _prevBtn.PointerReleased += (_, _) => { _month--; if (_month < 0) { _month = 11; _year--; } RenderMonth(); };

        _nextBtn.Width = 22;
        _nextBtn.Height = 22;
        _nextBtn.CornerRadius = new CornerRadius(4);
        _nextBtn.Cursor = new Cursor(StandardCursorType.Hand);
        _nextBtn.Child = new TextBlock { Text = "›", FontSize = 13, TextAlignment = TextAlignment.Center };
        _nextBtn.PointerEntered += (_, _) => _nextBtn.Background = new SolidColorBrush(T("btnhover", IsDarkNow));
        _nextBtn.PointerExited += (_, _) => _nextBtn.Background = null;
        _nextBtn.PointerReleased += (_, _) => { _month++; if (_month > 11) { _month = 0; _year++; } RenderMonth(); };

        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.FontSize = 12;
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
        var maxDay = Math.Min(daysInMonth, 42 - startWeekday);
        var daysInPrev = (_month == 0 ? new DateTime(_year - 1, 12, 31) : new DateTime(_year, _month, 1).AddDays(-1)).Day;

        for (var i = 0; i < 42; i++)
        {
            var cell = _cells[i];
            var tb = (TextBlock)cell.Child!;
            cell.Tag = null;
            cell.Background = null;
            string cls;
            int day;
            if (i < startWeekday)
            {
                day = daysInPrev - (startWeekday - 1 - i);
                cls = "other";
            }
            else if (i < startWeekday + maxDay)
            {
                day = i - startWeekday + 1;
                cls = _year == today.Year && _month == today.Month && day == today.Day ? "today" : "day";
                if (cls == "day" && _selectedDay == day && _year == today.Year && _month == today.Month)
                {
                    cls = "selected";
                }
                cell.Tag = day;
            }
            else
            {
                tb.Text = "";
                continue;
            }
            tb.Text = day.ToString();
            tb.Foreground = new SolidColorBrush(T(cls switch
            {
                "other" => "other",
                "today" => "today",
                "selected" => "selected",
                _ => "ink",
            }, IsDarkNow));
            tb.FontWeight = cls is "today" or "selected" ? FontWeight.Medium : FontWeight.Normal;
        }
    }
}

/// <summary>
/// 黄历
/// 左列(农历 label 12px ls4 / lunar 30px bold / 干支 13px / 节气) + 竖虚线 + 右列(宜/忌 27x27 badge + 词条 13px)
/// 右上角 Consolas 11px ls2 日期 30 分钟刷新
/// </summary>
public sealed class NativeAlmanacWidget : WidgetCardBase
{
    private static Color T(string key, bool dark) => dark ? key switch
    {
        "fg" => NativeTheme.Rgba(255, 255, 255, 0.87),
        "muted" => NativeTheme.Rgba(255, 255, 255, 0.52),
        "faint" => NativeTheme.Rgba(255, 255, 255, 0.38),
        "term_c" => NativeTheme.Rgba(255, 255, 255, 0.78),
        "yi" => NativeTheme.Parse("#e0554b"),
        "ji" => NativeTheme.Parse("#6f6f6f"),
        "div" => NativeTheme.Rgba(255, 255, 255, 0.18),
        "accent" => NativeTheme.Parse("#e08a80"),
        _ => Colors.White,
    } : key switch
    {
        "fg" => NativeTheme.Rgba(40, 40, 40, 0.88),
        "muted" => NativeTheme.Rgba(40, 40, 40, 0.52),
        "faint" => NativeTheme.Rgba(40, 40, 40, 0.4),
        "term_c" => NativeTheme.Rgba(40, 40, 40, 0.8),
        "yi" => NativeTheme.Parse("#cf3838"),
        "ji" => NativeTheme.Parse("#3c3c3c"),
        "div" => NativeTheme.Rgba(0, 0, 0, 0.15),
        "accent" => NativeTheme.Parse("#b8483d"),
        _ => Colors.Black,
    };

    private readonly TextBlock _sdate = new();
    private readonly TextBlock _lunar = new();
    private readonly TextBlock _gzYear = new();
    private readonly TextBlock _gzMonth = new();
    private readonly TextBlock _gzDay = new();
    private readonly TextBlock _term = new();
    private readonly TextBlock _good = new();
    private readonly TextBlock _bad = new();
    private Border _yiBadge;
    private Border _jiBadge;
    private readonly DashedRule _vdiv = new() { Vertical = true };
    private readonly DashedRule _hdiv = new();

    public NativeAlmanacWidget(ComponentDefinition definition) : base(definition)
    {
        BuildLayout();
        ActualThemeVariantChanged += (_, _) => Rebuild();
        // 原版 30 分钟定期刷新
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        timer.Tick += (_, _) => Rebuild();
        timer.Start();
        Rebuild();
    }

    private bool IsDarkNow => ThemeSense.IsDark(this);

    private Border MakeBadge(string glyph, string key, bool dark)
    {
        return new Border
        {
            Width = 27,
            Height = 27,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(T(key, dark)),
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
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

        _lunar.Margin = new Thickness(0, 4, 0, 0);
        _lunar.FontSize = 30;
        _lunar.FontWeight = FontWeight.Bold;
        _lunar.TextTrimming = TextTrimming.CharacterEllipsis;
        foreach (var gz in new[] { _gzYear, _gzMonth, _gzDay })
        {
            gz.Margin = gz == _gzYear ? new Thickness(0, 14, 0, 0) : new Thickness(0, 5, 0, 0);
            gz.FontSize = 13;
            gz.LetterSpacing = 1;
            gz.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        _term.Margin = new Thickness(0, 8, 0, 0);
        _term.FontSize = 12;
        _term.LetterSpacing = 2;

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = "农历",
            FontSize = 12,
            LetterSpacing = 4,
            Name = "almanacLabel",
        });
        left.Children.Add(_lunar);
        left.Children.Add(_gzYear);
        left.Children.Add(_gzMonth);
        left.Children.Add(_gzDay);
        left.Children.Add(_term);

        _good.FontSize = 13;
        _good.LineHeight = 13 * 1.85;
        _good.TextWrapping = TextWrapping.Wrap;
        _bad.FontSize = 13;
        _bad.LineHeight = 13 * 1.85;
        _bad.TextWrapping = TextWrapping.Wrap;

        _yiBadge = MakeBadge("宜", "yi", dark);
        _jiBadge = MakeBadge("忌", "ji", dark);
        _yiBadge.Margin = new Thickness(0, 2, 0, 0);
        _jiBadge.Margin = new Thickness(0, 2, 0, 0);

        var rowYi = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*") };
        rowYi.ColumnSpacing = 11;
        Grid.SetColumn(_yiBadge, 0);
        Grid.SetColumn(_good, 1);
        rowYi.Children.Add(_yiBadge);
        rowYi.Children.Add(_good);

        var rowJi = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*") };
        rowJi.ColumnSpacing = 11;
        Grid.SetColumn(_jiBadge, 0);
        Grid.SetColumn(_bad, 1);
        rowJi.Children.Add(_jiBadge);
        rowJi.Children.Add(_bad);

        _hdiv.Margin = new Thickness(0, 11);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(rowYi);
        right.Children.Add(_hdiv);
        right.Children.Add(rowJi);

        var main = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("132,Auto,*"),
            Margin = new Thickness(20, 16),
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(_vdiv, 1);
        Grid.SetColumn(right, 2);
        right.Margin = new Thickness(16, 0, 0, 0);
        main.Children.Add(left);
        main.Children.Add(_vdiv);
        main.Children.Add(right);

        var root = new Grid();
        root.Children.Add(main);
        root.Children.Add(_sdate);
        Card.Child = root;
        Content = Card;
    }

    private static string TermsText(string[]? terms, int limit)
    {
        if (terms is null || terms.Length == 0)
        {
            return "--";
        }
        var joined = string.Join("  ", terms.Take(limit));
        return terms.Length > limit ? joined + " …" : joined;
    }

    private void Rebuild()
    {
        var dark = IsDarkNow;
        var d = AlmanacService.GetToday();
        var lunar = "--";
        var gzYear = "--";
        var gzMonth = "--";
        var gzDay = "--";
        var good = "--";
        var bad = "--";
        var date = DateTime.Today.ToString("yyyy-MM-dd");
        if (d is not null)
        {
            lunar = $"{d.LunarMonth}{d.LunarDay}".Trim();
            if (lunar.Length == 0)
            {
                lunar = "--";
            }
            gzYear = $"{d.YearGz}【{d.Zodiac}】年";
            gzMonth = $"{d.MonthGz}月";
            gzDay = $"{d.DayGz}日";
            good = TermsText(d.Good, 10);
            bad = TermsText(d.Bad, 8);
            if (!string.IsNullOrEmpty(d.Date) && DateTime.TryParse(d.Date, out var pd))
            {
                date = pd.ToString("yyyy-MM-dd");
            }
        }
        _sdate.Text = NativeTheme.DotDate(date);
        _lunar.Text = lunar;
        _gzYear.Text = gzYear;
        _gzMonth.Text = gzMonth;
        _gzDay.Text = gzDay;
        _term.Text = string.IsNullOrEmpty(d?.SolarTerm) ? "" : d!.SolarTerm;
        _term.IsVisible = !string.IsNullOrEmpty(d?.SolarTerm);
        _good.Text = good;
        _bad.Text = bad;

        // 主题色
        _sdate.Foreground = new SolidColorBrush(T("faint", dark));
        _lunar.Foreground = new SolidColorBrush(T("fg", dark));
        foreach (var gz in new[] { _gzYear, _gzMonth, _gzDay })
        {
            gz.Foreground = new SolidColorBrush(T("muted", dark));
        }
        _term.Foreground = new SolidColorBrush(T("accent", dark));
        _good.Foreground = new SolidColorBrush(T("term_c", dark));
        _bad.Foreground = new SolidColorBrush(T("term_c", dark));
        _yiBadge.Background = new SolidColorBrush(T("yi", dark));
        _jiBadge.Background = new SolidColorBrush(T("ji", dark));
        var div = new SolidColorBrush(T("div", dark));
        _vdiv.Stroke = div;
        _hdiv.Stroke = div;
        // 农历 label 色
        if (((StackPanel)_lunar.Parent!).Children[0] is TextBlock label)
        {
            label.Foreground = new SolidColorBrush(T("muted", dark));
        }
        InvalidateVisual();
    }
}


// ===== NativeCharts.cs =====

/// <summary>图表控件基类: 填满父级 每帧按设备像素绘制(与原版 canvas 一致)</summary>
internal abstract class ChartControl : Control
{
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
    private static readonly Color LineColor = NativeTheme.Parse("#e2543a");
    private static readonly Color FillColor = NativeTheme.Rgba(226, 84, 58, 0.20);

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
    private readonly TextBlock _downValue = new();
    private readonly TextBlock _upLabel = new();
    private readonly TextBlock _upValue = new();
    private readonly NetChart _chart;

    public NativeNetworkSpeedWidget(ComponentDefinition definition) : base(definition)
    {
        _title.Text = AppUtils.Tr("netspeed.title");
        _downLabel.Text = "↓" + AppUtils.Tr("netspeed.recv");
        _upLabel.Text = "↑" + AppUtils.Tr("netspeed.send");

        _chart = new NetChart();
        var downGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        downGroup.Children.Add(_downLabel);
        downGroup.Children.Add(_downValue);
        var upGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        upGroup.Children.Add(_upLabel);
        upGroup.Children.Add(_upValue);

        var topbar = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*,Auto,Auto"),
            Margin = new Thickness(0, 3, 0, 6),
        };
        Grid.SetColumn(_title, 0);
        _title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(downGroup, 2);
        Grid.SetColumn(upGroup, 3);
        upGroup.Margin = new Thickness(10, 0, 0, 0);
        topbar.Children.Add(_title);
        topbar.Children.Add(downGroup);
        topbar.Children.Add(upGroup);

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

        SystemSampler.NetReady += OnNet;
        SystemSampler.EnsureStarted();
        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyPalette();
            _chart.InvalidateVisual();
        };
        ApplyPalette();
    }

    private bool IsDarkNow => ThemeSense.IsDark(this);

    private void ApplyPalette()
    {
        var dark = IsDarkNow;
        var sub = new SolidColorBrush(T("sub", dark));
        var ink = new SolidColorBrush(T("ink", dark));
        _title.Foreground = sub;
        _downLabel.Foreground = sub;
        _upLabel.Foreground = sub;
        _downValue.Foreground = ink;
        _upValue.Foreground = ink;
    }

    private void OnNet(double down, double up)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _downValue.Text = FormatBytes(down / 8);
            _upValue.Text = FormatBytes(up / 8);
            _chart.Push(down, up);
        });
    }

    internal static string FormatBytes(double v)
    {
        if (v >= 1048576)
        {
            return (v / 1048576).ToString("0.0", CultureInfo.CurrentCulture) + " MB/s";
        }
        if (v >= 1024)
        {
            return (v / 1024).ToString("0.0", CultureInfo.CurrentCulture) + " KB/s";
        }
        return Math.Round(v).ToString(CultureInfo.CurrentCulture) + " B/s";
    }

    /// <summary>网速图表 绘制规格逐字取自模板 draw()/adaptScale()</summary>
    private sealed class NetChart : ChartControl
    {
        private readonly List<double> _down = new();
        private readonly List<double> _up = new();
        private double _scale = 50000;
        private int _lowCount;

        public void Push(double d, double u)
        {
            _down.Add(d);
            _up.Add(u);
            if (_down.Count > MaxPts)
            {
                _down.RemoveAt(0);
                _up.RemoveAt(0);
            }
            // adaptScale 逐字: 超界翻倍 连续 4 次低于 1/4 且 >50000 才减半
            var m = 1.0;
            foreach (var v in _down.Concat(_up))
            {
                if (v > m)
                {
                    m = v;
                }
            }
            while (m > _scale)
            {
                _scale *= 2;
                _lowCount = 0;
            }
            if (m < _scale / 4)
            {
                if (_lowCount >= 4 && _scale > 50000)
                {
                    _scale /= 2;
                    _lowCount = 0;
                }
                else
                {
                    _lowCount++;
                }
            }
            else
            {
                _lowCount = 0;
            }
            InvalidateVisual();
        }

        protected override void DrawChart(DrawingContext ctx, double w, double h)
        {
            var dark = ThemeSense.IsDark(this);
            var gridPen = new Pen(new SolidColorBrush(T("grid", dark)), 1);
            var gridVPen = new Pen(new SolidColorBrush(T("gridv", dark)), 1);
            // 横向 4 格
            for (var i = 0; i <= 4; i++)
            {
                var y = Math.Round(h * i / 4.0) + 0.5;
                ctx.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            }
            // 纵向每 10 点
            var step = w / (MaxPts - 1);
            for (var x = w; x > 0; x -= step * 10)
            {
                var gx = Math.Round(x) + 0.5;
                ctx.DrawLine(gridVPen, new Point(gx, 0), new Point(gx, h));
            }
            if (_down.Count < 2)
            {
                return;
            }

            // 接收: 区域填充(下边闭合) + 1.6 实线
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(w - (_down.Count - 1) * step, h), isFilled: true);
                for (var i = 0; i < _down.Count; i++)
                {
                    var x = w - (_down.Count - 1 - i) * step;
                    var y = h - Math.Min(_down[i] / _scale, 1) * h;
                    g.LineTo(new Point(x, y));
                }
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(FillColor), null, area);

            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(new Point(w - (_down.Count - 1) * step, h - Math.Min(_down[0] / _scale, 1) * h), isFilled: false);
                for (var i = 1; i < _down.Count; i++)
                {
                    var x = w - (_down.Count - 1 - i) * step;
                    var y = h - Math.Min(_down[i] / _scale, 1) * h;
                    g.LineTo(new Point(x, y));
                }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(LineColor), 1.6, null, PenLineCap.Round, PenLineJoin.Round), line);

            // 发送: 1.3 虚线 3,3
            var up = new StreamGeometry();
            using (var g = up.Open())
            {
                g.BeginFigure(new Point(w - (_up.Count - 1) * step, h - Math.Min(_up[0] / _scale, 1) * h), isFilled: false);
                for (var i = 1; i < _up.Count; i++)
                {
                    var x = w - (_up.Count - 1 - i) * step;
                    var y = h - Math.Min(_up[i] / _scale, 1) * h;
                    g.LineTo(new Point(x, y));
                }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(LineColor), 1.3, new DashStyle(new double[] { 3, 3 }, 0), PenLineCap.Round, PenLineJoin.Round), up);

            // 右上刻度 11px
            var ft = new FormattedText(FormatBits(_scale), System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(Common.AppFontFamily), 11, new SolidColorBrush(T("sub", dark)));
            ctx.DrawText(ft, new Point(w - 4 - ft.WidthIncludingTrailingWhitespace, 3));
        }

        private static string FormatBits(double v)
        {
            if (v >= 1e9)
            {
                return (v / 1e9).ToString("0.0", CultureInfo.CurrentCulture) + " Gbps";
            }
            if (v >= 1e6)
            {
                return (v / 1e6).ToString("0.0", CultureInfo.CurrentCulture) + " Mbps";
            }
            if (v >= 1e3)
            {
                return Math.Round(v / 1e3).ToString(CultureInfo.CurrentCulture) + " Kbps";
            }
            return Math.Round(v).ToString(CultureInfo.CurrentCulture) + " bps";
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

        _chart = new DecibelChart();
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
    }

    private bool IsDarkNow => ThemeSense.IsDark(this);

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
            var dark = ThemeSense.IsDark(this);
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


// ===== NativeDaily.cs =====

/// <summary>
/// 每日单词
/// 背景水印字母(Georgia 150px accent渐变 op.16) + 日期(Consolas 12) + 单词(Georgia 40px bold 双色渐变)
/// + 音标胶囊 + 词性拆行(pos 胶囊 12px) + 60% 虚线分隔 + 例句(Georgia italic 18/15)
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

    public NativeDailyWordWidget(ComponentDefinition definition) : base(definition)
    {
        BuildLayout();
        ActualThemeVariantChanged += (_, _) => Apply(_lastWord, _lastPhonetic, _lastTrans, _lastExText, _lastExTrans, _lastDate);
        // 原版 30 分钟定期刷新
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        timer.Tick += (_, _) => _ = RefreshAsync();
        timer.Start();
        _ = RefreshAsync();
    }

    private bool IsDarkNow => ThemeSense.IsDark(this);

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
        _word.FontWeight = FontWeight.Bold;

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

    private async System.Threading.Tasks.Task RefreshAsync()
    {
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

    public NativeDailySentenceWidget(ComponentDefinition definition) : base(definition)
    {
        BuildLayout();
        ActualThemeVariantChanged += (_, _) => Apply(_lastContent, _lastNote, _lastDate);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        timer.Tick += (_, _) => _ = RefreshAsync();
        timer.Start();
        _ = RefreshAsync();
    }

    private bool IsDarkNow => ThemeSense.IsDark(this);

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

    private async System.Threading.Tasks.Task RefreshAsync()
    {
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


// ===== NativeInteractive.cs =====

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
    private readonly TextBlock _emptyHint = new();
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

    private bool IsDarkNow => ThemeSense.IsDark(this);

    private void BuildLayout()
    {
        _title.FontSize = 20;
        _title.FontWeight = FontWeight.Bold;
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

        var topGrid = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,*,Auto,Auto") };
        topGrid.ColumnSpacing = 10;
        Grid.SetColumn(_title, 0);
        Grid.SetColumn(_countBorder, 1);
        Grid.SetColumn(_addBtn, 3);
        Grid.SetColumn(_clearBtn, 4);
        topGrid.Children.Add(_title);
        topGrid.Children.Add(_countBorder);
        topGrid.Children.Add(_addBtn);
        topGrid.Children.Add(_clearBtn);

        _emptyHint.Text = AppUtils.Tr("announcement.empty");
        _emptyHint.FontSize = 15;
        _emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyHint.VerticalAlignment = VerticalAlignment.Center;

        var scroll = new ScrollViewer
        {
            Content = _list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var body = new Grid();
        body.Children.Add(scroll);
        body.Children.Add(_emptyHint);

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
        _emptyHint.Foreground = new SolidColorBrush(CardTheme.T("empty", dark));
        _emptyHint.IsVisible = _items.Count == 0;

        _list.Children.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var idx = i;
            var fresh = i == 0 && IsFresh(it.Created);

            var text = new TextBlock
            {
                Text = it.Text,
                FontSize = 18,
                LineHeight = 18 * 1.45,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 18 * 1.45 * 3,
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

            var itemBorder = new Border
            {
                Background = new SolidColorBrush(CardTheme.T("card", dark)),
                BorderBrush = new SolidColorBrush(CardTheme.T("cardline", dark)),
                BorderThickness = fresh ? new Thickness(4, 1, 1, 1) : new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(11, 9, 11, 8),
                Margin = new Thickness(0, 0, 0, 8),
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
    private readonly Border _progFill = new();
    private readonly Border _addSubBtn = new();
    private readonly Border _clearDoneBtn = new();
    private readonly TextBlock _clearDoneText = new();
    private readonly TextBlock _emptyHint = new();
    private readonly StackPanel _col0 = new();
    private readonly StackPanel _col1 = new();
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

    private bool IsDarkNow => ThemeSense.IsDark(this);

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
            File.WriteAllText(_dataFile, new JsonObject { ["sections"] = (_sections ?? new JsonArray()).DeepClone() }.ToJsonString());
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

        _progFill.Height = 3;
        _progFill.CornerRadius = new CornerRadius(2);
        _progFill.Width = 0;

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

        var topGrid = new Grid { ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,*,Auto,Auto") };
        topGrid.ColumnSpacing = 8;
        var progTrack = new Border
        {
            Width = 72,
            Height = 3,
            CornerRadius = new CornerRadius(2),
            Child = _progFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var progGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        progGroup.Children.Add(_progText);
        progGroup.Children.Add(progTrack);
        Grid.SetColumn(progGroup, 0);
        Grid.SetColumn(_addSubBtn, 3);
        Grid.SetColumn(_clearDoneBtn, 4);
        topGrid.Children.Add(progGroup);
        topGrid.Children.Add(_addSubBtn);
        topGrid.Children.Add(_clearDoneBtn);
        topGrid.Margin = new Thickness(2, 3, 2, 8);

        _emptyHint.Text = AppUtils.Tr("homework.empty");
        _emptyHint.FontSize = 12;
        _emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyHint.VerticalAlignment = VerticalAlignment.Center;

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
        body.Children.Add(_emptyHint);

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            Margin = new Thickness(12, 2, 12, 8),
        };
        Grid.SetRow(topGrid, 0);
        Grid.SetRow(body, 1);
        root.Children.Add(topGrid);
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
        _progFill.Background = new SolidColorBrush(accent);
        _addSubBtn.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        ((TextBlock)_addSubBtn.Child!).Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        _clearDoneBtn.Background = new SolidColorBrush(CardTheme.T("chip", dark));
        _clearDoneText.Foreground = new SolidColorBrush(CardTheme.T("sub", dark));
        _emptyHint.Foreground = new SolidColorBrush(CardTheme.T("empty", dark));

        var (total, done) = Progress();
        _progText.Text = $"{done}/{total}";
        _progFill.Width = total > 0 ? 72.0 * done / total : 0;
        _clearDoneBtn.IsVisible = done > 0;
        _emptyHint.IsVisible = _sections.Count == 0;

        _col0.Children.Clear();
        _col1.Children.Clear();

        // 双栏平衡分配(
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
            var target = h0 <= h1 ? _col0 : _col1;
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
        InvalidateVisual();
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

        var body = new StackPanel();
        body.Children.Add(head);
        for (var ii = 0; ii < items.Count; ii++)
        {
            if (items[ii] is JsonObject it)
            {
                body.Children.Add(BuildItemRow(si, ii, it));
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

        body.Children.Add(addRow);

        var secBorder = new Border
        {
            Background = new SolidColorBrush(CardTheme.T("card", dark)),
            BorderBrush = new SolidColorBrush(CardTheme.T("cardline", dark)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 8, 9, 7),
            Margin = new Thickness(0, 0, 0, 10),
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
    private readonly TextBlock _emptyHint = new();

    public NativeTimetableTimelineWidget(ComponentDefinition definition) : base(definition)
    {
        _canvas = new TimelineCanvas(this);
        _emptyHint.Text = "今日课表未配置";
        _emptyHint.FontSize = 15;
        _emptyHint.FontWeight = FontWeight.SemiBold;
        _emptyHint.LetterSpacing = 4;
        _emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyHint.VerticalAlignment = VerticalAlignment.Center;

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
        root.Children.Add(_emptyHint);
        Card.Child = root;
        Content = Card;
        ActualThemeVariantChanged += (_, _) => _canvas.InvalidateVisual();

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
        _emptyHint.IsVisible = nodes.Count == 0;
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
            var dark = ThemeSense.IsDark(this);
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


// ===== 组件配置面板
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
        _w = new TextBox { Text = _default, Watermark = _placeholder };
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
}

/// <summary>组件配置面板</summary>
public sealed class ComponentConfigDialog
{
    private readonly JsonObject _config;
    private readonly List<ConfigField> _fields = new();
    private readonly StackPanel _basicHost = new() { Spacing = 14, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _advancedHost = new() { Spacing = 14, Margin = new Thickness(0, 8, 0, 0) };

    public Control Content { get; }

    private ComponentConfigDialog(JsonObject config)
    {
        _config = config;

        var tabBasic = MakeTab("component_edit.config_basic");
        var tabAdvanced = MakeTab("component_edit.config_advanced");

        var basicScroll = new ScrollViewer { Content = _basicHost, Padding = new Thickness(2, 0, 12, 0) };
        var advancedScroll = new ScrollViewer { Content = _advancedHost, Padding = new Thickness(2, 0, 12, 0) };

        BuildPage(_basicHost, BasicDefs());
        BuildPage(_advancedHost, AdvancedDefs());

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
            RowDefinitions = RowDefinitions.Parse("Auto,*"),
            MinWidth = 460,
            MinHeight = 340,
        };
        Grid.SetRow(tabs, 0);
        Grid.SetRow(pages, 1);
        root.Children.Add(tabs);
        root.Children.Add(pages);
        Content = root;

        foreach (var f in _fields)
        {
            f.Load(_config);
        }
        Log.Debug($"[CFG] 配置面板构建: 字段数={_fields.Count}");
    }

    /// <summary>弹出配置面板; 保存返回写回后的配置, 取消返回 null</summary>
    public static async Task<JsonObject?> ShowAsync(Window owner, JsonObject config)
    {
        var dlg = new ComponentConfigDialog(config);
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

    /// <summary>基础页字段定义 各组件专属: 按用户要求暂留空</summary>
    private static List<(string? Title, List<ConfigField> Fields)> BasicDefs() => new();

    /// <summary>进阶页字段定义 对齐原版 _advanced_defs 默认: 外观 + 字体</summary>
    private static List<(string? Title, List<ConfigField> Fields)> AdvancedDefs() => new()
    {
        (AppUtils.Tr("component_edit.group_appearance"), new List<ConfigField>
        {
            new SliderField("bg_opacity", AppUtils.Tr("component_edit.config_bg_opacity"), 55, 0, 100, "%"),
            new SliderField("corner_radius", AppUtils.Tr("component_edit.config_corner_radius"), 16, 0, 29, "px"),
        }),
        (AppUtils.Tr("component_edit.group_font"), new List<ConfigField>
        {
            new SliderField("font_scale", AppUtils.Tr("component_edit.config_font_scale"), 100, 50, 200, "%"),
        }),
    };


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
            Watermark = AppUtils.Tr("weather_service.region_placeholder"),
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


// ===== Views/RingProgress.cs =====

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
