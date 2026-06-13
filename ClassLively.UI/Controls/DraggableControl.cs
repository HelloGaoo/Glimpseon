using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace ClassLively_UI.Controls;

/// <summary>
/// 吸附算法
/// </summary>
public interface ISnapProvider
{
    (double X, double Y, List<(char Dir, double Pos)> Lines) ComputeSnap(
        double x, double y, double w, double h, object? draggingWidget);
    void ShowGuideLines(List<(char Direction, double Position)> lines);
    void ClearGuideLines();
}

/// <summary>
/// 拖拽控件
/// </summary>
public sealed class DraggableControl : UserControl
{
    // ── 公开事件 ──

    /// <summary>位置变化 (percentX, percentY)</summary>
    public event Action<string, double, double>? PositionChanged;

    /// <summary>双击请求打开设置</summary>
    public event Action<string>? SettingRequested;

    // ── 公开属性 ──

    /// <summary>组件唯一标识</summary>
    public string ComponentId { get; }

    /// <summary>百分比 X ∈ [0, 1]</summary>
    public double PercentX
    {
        get => _percentX;
        set { _percentX = Clamp01(value); UpdatePositionFromPercent(); }
    }

    /// <summary>百分比 Y ∈ [0, 1]</summary>
    public double PercentY
    {
        get => _percentY;
        set { _percentY = Clamp01(value); UpdatePositionFromPercent(); }
    }

    /// <summary>锚点模式</summary>
    public AnchorMode AnchorMode { get; set; } = AnchorMode.TopLeft;

    /// <summary>设置吸附算法提供者</summary>
    public ISnapProvider? SnapProvider { get; set; }

    // ── 内部状态 ──

    private double _percentX = 0.5;
    private double _percentY = 0.5;

    private bool _isDragging;
    private Point _dragStartPos;
    private Point _widgetStartPos;
    private Point _clickStartPos;

    private bool _isDraggable;
    private bool _isHovered;

    // UI
    private readonly Border _border;
    private readonly TextBlock _labelText;
    private readonly ContentPresenter _contentPresenter;

    // 主题色
    private Color _primaryColor = ColorHelper.FromRgb(48, 195, 97);

    // ── 构造 ──

    public DraggableControl(string componentId)
    {
        ComponentId = componentId;

        var rootGrid = new Grid();
        rootGrid.Children.Add(_border = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(ColorHelper.Transparent),
            CornerRadius = new CornerRadius(4),
            Child = _contentPresenter = new ContentPresenter()
        });
        rootGrid.Children.Add(_labelText = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            FontSize = 12,
            Margin = new Thickness(8, 4, 0, 0),
            Foreground = new SolidColorBrush(ColorHelper.FromRgba(255, 255, 255, 100))
        });

        Content = rootGrid;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;  // ★ 关键：窗口切换时系统自动释放指针捕获
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        DoubleTapped += OnDoubleTapped;
    }

    // ── 公开方法 ──

    public void SetDraggable(bool enabled)
    {
        _isDraggable = enabled;
        if (enabled)
        {
            UpdateBorder(true, isHover: false);
            Canvas.SetZIndex(this, 100);
        }
        else
        {
            UpdateBorder(false, isHover: false);
            Canvas.SetZIndex(this, 0);
        }
    }

    public void SetPositionPercent(double x, double y)
    {
        _percentX = Clamp01(x);
        _percentY = Clamp01(y);
        UpdatePositionFromPercent();
    }

    public (double X, double Y) GetPositionPercent() => (_percentX, _percentY);

    public void OnParentResize() => UpdatePositionFromPercent();

    public void UpdateThemeColor(Color primaryColor)
    {
        _primaryColor = primaryColor;
        if (_isHovered) UpdateBorder(show: _isDraggable, isHover: true);
    }

    public void SetContent(UIElement content)
    {
        _contentPresenter.Content = content;
    }

    // ── 百分比定位 ──

    private void UpdatePositionFromPercent()
    {
        var parent = Parent as FrameworkElement;
        if (parent == null) return;

        var parentW = parent.ActualWidth;
        var parentH = parent.ActualHeight;
        if (parentW <= 0 || parentH <= 0) return;

        var widgetW = ActualWidth;
        var widgetH = ActualHeight;
        var availableW = parentW - widgetW;
        var availableH = parentH - widgetH;

        if (availableW <= 0 || availableH <= 0) return;

        double x = availableW * _percentX;
        double y = availableH * _percentY;

        switch (AnchorMode)
        {
            case AnchorMode.TopRight:
                x = parentW - widgetW * (1 + (1 - _percentX));
                break;
            case AnchorMode.BottomLeft:
                y = parentH - widgetH * (1 + (1 - _percentY));
                break;
            case AnchorMode.BottomRight:
                x = parentW - widgetW * (1 + (1 - _percentX));
                y = parentH - widgetH * (1 + (1 - _percentY));
                break;
            case AnchorMode.Center:
                x = (parentW - widgetW) / 2;
                y = (parentH - widgetH) / 2;
                break;
        }

        Canvas.SetLeft(this, x);
        Canvas.SetTop(this, y);
    }

    private (double px, double py) CalculatePercentFromPosition()
    {
        var parent = Parent as FrameworkElement;
        if (parent == null) return (_percentX, _percentY);

        var parentW = parent.ActualWidth;
        var parentH = parent.ActualHeight;
        var widgetW = ActualWidth;
        var widgetH = ActualHeight;
        var availableW = parentW - widgetW;
        var availableH = parentH - widgetH;

        double px = availableW > 0 ? Canvas.GetLeft(this) / availableW : 0.5;
        double py = availableH > 0 ? Canvas.GetTop(this) / availableH : 0.5;

        return (Clamp01(px), Clamp01(py));
    }

    // ── 拖拽交互 ──

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggable) return;

        var pt = e.GetCurrentPoint(null);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStartPos = pt.Position;
            _widgetStartPos = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
            _clickStartPos = pt.Position;

            CapturePointer(e.Pointer);
            Canvas.SetZIndex(this, 200);
            UpdateBorder(true, false);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || !_isDraggable) return;

        var pt = e.GetCurrentPoint(null);
        var deltaX = pt.Position.X - _dragStartPos.X;
        var deltaY = pt.Position.Y - _dragStartPos.Y;
        var newPos = new Point(
            _widgetStartPos.X + deltaX,
            _widgetStartPos.Y + deltaY
        );

        // 边界约束
        var parent = Parent as FrameworkElement;
        if (parent != null)
        {
            newPos.X = Math.Max(0, Math.Min(newPos.X, parent.ActualWidth - ActualWidth));
            newPos.Y = Math.Max(0, Math.Min(newPos.Y, parent.ActualHeight - ActualHeight));
        }

        // 调用吸附算法
        var snap = SnapProvider;
        if (snap != null)
        {
            var result = snap.ComputeSnap(newPos.X, newPos.Y, ActualWidth, ActualHeight, this);
            newPos.X = result.X;
            newPos.Y = result.Y;
            snap.ShowGuideLines(result.Lines);
        }

        Canvas.SetLeft(this, newPos.X);
        Canvas.SetTop(this, newPos.Y);

        (_percentX, _percentY) = CalculatePercentFromPosition();
        PositionChanged?.Invoke(ComponentId, _percentX, _percentY);

        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        ReleasePointerCaptures();

        Canvas.SetZIndex(this, 100);
        UpdateBorder(_isDraggable, false);

        // 清除辅助线
        SnapProvider?.ClearGuideLines();

        // 移动距离 < 5px 视为单击 → 打开设置
        var pt = e.GetCurrentPoint(null);
        var dx = pt.Position.X - _clickStartPos.X;
        var dy = pt.Position.Y - _clickStartPos.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 5)
        {
            SettingRequested?.Invoke(ComponentId);
        }

        e.Handled = true;
    }

    /// <summary>
    /// 指针捕获丢失情况处理
    /// </summary>
    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            Canvas.SetZIndex(this, 100);
            UpdateBorder(_isDraggable, false);
            SnapProvider?.ClearGuideLines();
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_isDraggable)
        {
            SettingRequested?.Invoke(ComponentId);
            e.Handled = true;
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggable)
        {
            _isHovered = true;
            UpdateBorder(_isDraggable, true);
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isHovered = false;
        if (!_isDragging)
            UpdateBorder(_isDraggable, false);
    }

    // ── 边框绘制 ──

    private void UpdateBorder(bool show, bool isHover)
    {
        if (_isDragging) return;

        if (show || isHover)
        {
            Color borderColor;
            if (isHover)
            {
                borderColor = ColorHelper.FromRgba(
                    _primaryColor.R, _primaryColor.G, _primaryColor.B, 160);
            }
            else
            {
                borderColor = ColorHelper.FromRgba(255, 255, 255, 30);
            }

            _border.BorderBrush = new SolidColorBrush(borderColor);
            _border.BorderThickness = new Thickness(1);

            if (show)
            {
                _labelText.Text = GetComponentDisplayName(ComponentId);
                _labelText.Visibility = Visibility.Visible;
            }
            else
            {
                _labelText.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            _border.BorderBrush = new SolidColorBrush(ColorHelper.Transparent);
            _labelText.Visibility = Visibility.Collapsed;
        }
    }

    // ── 工具方法 ──

    private static double Clamp01(double v) => Math.Max(0.0, Math.Min(1.0, v));

    private static string GetComponentDisplayName(string id) => id switch
    {
        "clock" => "时钟",
        "weather" => "天气",
        "poetry" => "一言",
        "countdown" => "倒计时",
        "school_info" => "学校信息",
        "quick_launch" => "快捷启动",
        "media" => "媒体信息",
        _ => id
    };
}

// ── 锚点枚举 ──

public enum AnchorMode
{
    TopLeft, Top, TopRight,
    Left, Center, Right,
    BottomLeft, Bottom, BottomRight
}

// ── 颜色辅助 ──

internal static class ColorHelper
{
    public static Color Transparent => Color.FromArgb(0, 0, 0, 0);
    public static Color White => Color.FromArgb(255, 255, 255, 255);
    public static Color FromRgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
    public static Color FromRgba(byte r, byte g, byte b, byte a) => Color.FromArgb(a, r, g, b);
}
