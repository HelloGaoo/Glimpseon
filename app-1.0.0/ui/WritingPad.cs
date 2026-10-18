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

// 书写覆盖层
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using Glimpseon.Core.Win32;

namespace Glimpseon.UI;

// 工具模式 0鼠标 1笔 2橡皮
internal enum WritingToolMode
{
    Mouse = 0,
    Pen = 1,
    Eraser = 2,
}

// 笔型 自由/直线/矩形/荧光
internal enum WritingShapeMode
{
    Free,
    Line,
    Rect,
    Highlight,
}

// 笔画
internal sealed class WritingStroke
{
    public List<Point> Points = [];
    public Color Color;
    public double Width = 3;
    public WritingShapeMode Shape = WritingShapeMode.Free;

    // 自由笔迹
    public bool IsFreehand => Shape is WritingShapeMode.Free or WritingShapeMode.Highlight;
    public StreamGeometry? Geometry;
    public Rect Bounds;
    public bool GeometryDirty = true;

    public void SetPoints(List<Point> points)
    {
        Points = points;
        GeometryDirty = true;
    }

    public void AddPoint(Point point)
    {
        Points.Add(point);
        GeometryDirty = true;
    }

    public void RebuildGeometry()
    {
        GeometryDirty = false;
        if (Points.Count == 0)
        {
            Geometry = null;
            Bounds = default;
            return;
        }
        var minX = Points[0].X;
        var minY = Points[0].Y;
        var maxX = minX;
        var maxY = minY;
        foreach (var p in Points)
        {
            if (p.X < minX) { minX = p.X; } else if (p.X > maxX) { maxX = p.X; }
            if (p.Y < minY) { minY = p.Y; } else if (p.Y > maxY) { maxY = p.Y; }
        }
        var pad = Width / 2 + 1;
        Bounds = new Rect(minX - pad, minY - pad, maxX - minX + pad * 2, maxY - minY + pad * 2);

        var geo = new StreamGeometry();
        if (Shape == WritingShapeMode.Rect && Points.Count >= 2)
        {
            var r = new Rect(Points[0], Points[^1]);
            using var g = geo.Open();
            g.BeginFigure(new Point(r.Left, r.Top), false);
            g.LineTo(new Point(r.Right, r.Top));
            g.LineTo(new Point(r.Right, r.Bottom));
            g.LineTo(new Point(r.Left, r.Bottom));
            g.LineTo(new Point(r.Left, r.Top));
            g.EndFigure(false);
        }
        else if (Points.Count >= 2)
        {
            using var g = geo.Open();
            g.BeginFigure(Points[0], false);
            for (var i = 1; i < Points.Count; i++)
            {
                g.LineTo(Points[i]);
            }
            g.EndFigure(false);
        }
        Geometry = geo;
    }
}

internal sealed class EraseUndoEntry
{
    public List<(WritingStroke Original, List<WritingStroke> Pieces)> Pairs = [];
}

// 全局入口
internal static class WritingPadService
{
    private static WritingPadOverlay? _overlay;
    public static event Action? StateChanged;
    // 工具栏收起请求
    public static event Action? CloseRequested;

    public static bool OverlayVisible => _overlay is { IsVisible: true };
    public static WritingToolMode Mode => _overlay?.Mode ?? WritingToolMode.Mouse;
    public static bool Whiteboard => _overlay?.Whiteboard ?? false;
    public static bool Freeze => _overlay?.FreezeOn ?? false;

    // 笔设置
    public static Color PenColor => _overlay?.PenColor ?? Colors.Red;
    public static double PenWidth => _overlay?.PenWidth ?? 3;
    public static WritingShapeMode ShapeMode => _overlay?.ShapeMode ?? WritingShapeMode.Free;

    public static void SetPen(Color color)
    {
        _overlay?.SetPen(color);
        NotifyStateChanged();
    }

    public static void SetPenWidth(double width)
    {
        _overlay?.SetPenWidth(width);
        NotifyStateChanged();
    }

    public static void SetShapeMode(WritingShapeMode mode)
    {
        _overlay?.SetShapeMode(mode);
        NotifyStateChanged();
    }

    // 球底中点 书写工具栏锚点
    public static PixelPoint BallAnchor { get; set; }

    // 悬浮球窗口句柄
    public static Func<nint>? BallHandleProvider { get; set; }

    internal static void NotifyStateChanged() => StateChanged?.Invoke();

    private static WritingPadOverlay EnsureOverlay() => _overlay ??= new WritingPadOverlay();

    public static void SetMode(WritingToolMode mode)
    {
        if (mode == WritingToolMode.Mouse)
        {
            _overlay?.CloseToMouse();
        }
        else
        {
            var overlay = EnsureOverlay();
            overlay.ShowOverlay(Whiteboard);
            overlay.SetToolMode(mode);
        }
        NotifyStateChanged();
    }

    // 悬浮球: 笔 未开>开 已是笔>关 其他>切笔
    public static void TogglePen()
    {
        if (OverlayVisible && Mode == WritingToolMode.Pen)
        {
            CloseOverlay();
        }
        else
        {
            SetMode(WritingToolMode.Pen);
        }
    }

    public static void ToggleEraser()
    {
        if (OverlayVisible && Mode == WritingToolMode.Eraser)
        {
            CloseOverlay();
        }
        else
        {
            SetMode(WritingToolMode.Eraser);
        }
    }

    public static void SetWhiteboard(bool on)
    {
        if (Freeze)
        {
            return;
        }
        if (_overlay is { } overlay)
        {
            overlay.ApplyWhiteboard(on);
        }
        NotifyStateChanged();
    }

    // 定格开关
    public static void ToggleFreeze()
    {
        _overlay?.ToggleFreeze();
        NotifyStateChanged();
    }

    public static void Undo()
    {
        _overlay?.UndoLast();
    }

    public static void ClearAll()
    {
        _overlay?.ClearAllStrokes();
    }

    // 关闭覆盖层
    public static void CloseOverlay()
    {
        _overlay?.HideOverlay();
        CloseRequested?.Invoke();
        NotifyStateChanged();
    }

    internal static void OnOverlayClosed()
    {
        _overlay = null;
        NotifyStateChanged();
    }
}

// 全屏书写覆盖层
internal sealed class WritingPadOverlay : Window
{
    private readonly InkSurface _surface;
    private readonly Border _whiteBg;
    private readonly Image _freezeBg;
    private readonly Panel _root;
    private PixelRect _unionPx;
    private bool _freezeCaptureInFlight;

    public WritingToolMode Mode { get; private set; } = WritingToolMode.Pen;
    public bool Whiteboard { get; private set; }
    public bool FreezeOn { get; private set; } = Config.WritingFreeze.Value;

    public WritingShapeMode ShapeMode => _surface.ShapeMode;

    public Color PenColor => _surface.PenColor;

    public double PenWidth => _surface.PenWidth;

    internal InkSurface Surface => _surface;

    // 白板底色切换
    public void ApplyWhiteboard(bool on)
    {
        Whiteboard = on;
        _whiteBg.IsVisible = on;
        if (on)
        {
            _freezeBg.IsVisible = false;
        }
        else
        {
            RefreshFreezeBg();
        }
        EndAllStrokes();
        InvalidateVisual();
    }

    // 定格开关
    public void SetFreeze(bool on)
    {
        FreezeOn = on;
        if (on && Whiteboard)
        {
            ApplyWhiteboard(false);
            return;
        }
        RefreshFreezeBg();
    }

    public void ToggleFreeze() => SetFreeze(!FreezeOn);

    public WritingPadOverlay()
    {
        // 无边框透明置顶
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        CanResize = false;
        FontFamily = Common.AppFontFamily;

        _whiteBg = new Border
        {
            Background = Brushes.White,
            IsHitTestVisible = false,
            IsVisible = false,
        };
        _freezeBg = new Image
        {
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            IsVisible = false,
        };
        _surface = new InkSurface(this);
        _root = new Panel();
        _root.Children.Add(_freezeBg);
        _root.Children.Add(_whiteBg);
        _root.Children.Add(_surface);
        Content = _root;

        Closed += (_, _) => WritingPadService.OnOverlayClosed();
        Opened += (_, _) =>
        {
            FitScreens();
            Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                WritingPadService.CloseOverlay();
            }
        };
    }

    // 覆盖全部屏幕
    private void FitScreens()
    {
        var union = default(PixelRect);
        foreach (var screen in Screens.All)
        {
            union = union == default ? screen.Bounds : union.Union(screen.Bounds);
        }
        if (union == default)
        {
            return;
        }
        _unionPx = union;
        var s = Screens.Primary?.Scaling ?? RenderScaling;
        Position = union.TopLeft;
        Width = union.Width / s;
        Height = union.Height / s;
        _surface.FitBounds(Width, Height);
    }

    public void ShowOverlay(bool whiteboard)
    {
        Whiteboard = whiteboard;
        _whiteBg.IsVisible = whiteboard;
        var wasHidden = !IsVisible;
        if (wasHidden)
        {
            Show();
        }
        FitScreens();
        if (wasHidden)
        {
            RefreshFreezeBg();
        }
        else
        {
            SyncFreezeVisibility();
        }
    }

    private void SyncFreezeVisibility() =>
        _freezeBg.IsVisible = FreezeOn && !Whiteboard && _freezeBg.Source is not null;

    private void RefreshFreezeBg()
    {
        if (!FreezeOn || Whiteboard || _unionPx == default)
        {
            SyncFreezeVisibility();
            return;
        }
        if (_freezeCaptureInFlight)
        {
            return;
        }
        _freezeCaptureInFlight = true;
        var bounds = _unionPx;
        var self = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        var ball = WritingPadService.BallHandleProvider?.Invoke() ?? nint.Zero;
        Task.Run(() =>
        {
            var shot = CaptureScreen(bounds, self, ball);
            Dispatcher.UIThread.Post(() =>
            {
                _freezeCaptureInFlight = false;
                if (shot is { } capture)
                {
                    try
                    {
                        var handle = GCHandle.Alloc(capture.Pixels, GCHandleType.Pinned);
                        try
                        {
                            _freezeBg.Source = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul,
                                handle.AddrOfPinnedObject(), capture.Size, new Vector(96, 96), capture.Stride);
                        }
                        finally
                        {
                            handle.Free();
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"定格位图创建失败: {e.Message}");
                    }
                }
                SyncFreezeVisibility();
            });
        });
    }

    private static (byte[] Pixels, PixelSize Size, int Stride)? CaptureScreen(PixelRect bounds, nint self, nint ball)
    {
        try
        {
            Native.SetCaptureExclusion(self, true);
            Native.SetCaptureExclusion(ball, true);
            using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0,
                    new System.Drawing.Size(bounds.Width, bounds.Height));
            }
            var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bounds.Width, bounds.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var stride = data.Stride;
            var pixels = new byte[stride * bounds.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            bmp.UnlockBits(data);
            return (pixels, new PixelSize(bounds.Width, bounds.Height), stride);
        }
        catch (Exception e)
        {
            Log.Warning($"定格截屏失败: {e.Message}");
            return null;
        }
        finally
        {
            Native.SetCaptureExclusion(self, false);
            Native.SetCaptureExclusion(ball, false);
        }
    }

    public void HideOverlay()
    {
        _surface.CleanupState();
        Hide();
    }

    public void CloseToMouse()
    {
        SetToolMode(WritingToolMode.Mouse);
        HideOverlay();
        WritingPadService.NotifyStateChanged();
    }

    public void SetToolMode(WritingToolMode mode)
    {
        _surface.EndAllStrokes();
        Mode = mode;
        if (mode != WritingToolMode.Eraser)
        {
            _surface.ResetEraseState();
        }
        WritingPadService.NotifyStateChanged();
    }

    public void SetWhiteboardMode(bool on)
    {
        EndAllStrokes();
        ShowOverlay(on);
        WritingPadService.NotifyStateChanged();
    }

    public void UndoLast() => _surface.UndoLast();

    public void ClearAllStrokes() => _surface.ClearAllStrokes();

    public void SetPen(Color color) => _surface.SetPenColor(color);

    public void SetPenWidth(double w) => _surface.SetPenWidth(w);

    public void SetShapeMode(WritingShapeMode mode) => _surface.SetShapeMode(mode);

    private void EndAllStrokes() => _surface.EndAllStrokes();

}

internal sealed class InkSurface : Control, ICustomHitTest
{
    private const double EraseBaseMin = 25.0;
    private const double EraseBaseMax = 200.0;
    private const double EraseInit = 20.0;
    private const int HighlightAlpha = 92;
    private const double HighlightScale = 4;
    private const double HighlightMinW = 9;

    private readonly WritingPadOverlay _owner;
    private readonly List<WritingStroke> _strokes = [];
    private readonly List<Action> _history = [];
    private readonly Dictionary<IPointer, StrokeState> _active = [];
    private readonly Dictionary<IPointer, EraseState> _erase = [];

    private RenderTargetBitmap? _layer;
    private bool _layerDirty = true;
    private PixelSize _layerPixelSize;

    private static readonly Dictionary<(uint Color, double Width), Pen> PenCache = [];
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Pen EraserPen = new(new SolidColorBrush(Color.FromRgb(130, 130, 130), 200), 3);

    private Color _penColor = Colors.Red;
    private double _penWidth = 3;
    private WritingShapeMode _shapeMode = WritingShapeMode.Free;

    private double _drawingScale = 1.0;
    private Size _surfaceSize;

    private readonly DispatcherTimer _speedTimer;
    private readonly DispatcherTimer _eraseTimer;

    public InkSurface(WritingPadOverlay owner)
    {
        _owner = owner;
        ClipToBounds = true;
        _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _speedTimer.Tick += SampleEraseSpeed;
        _eraseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _eraseTimer.Tick += EraseLoopTick;
    }

    public WritingShapeMode ShapeMode => _shapeMode;

    public double PenWidth => _penWidth;

    public Color PenColor => _penColor;

    // 覆盖层尺寸与缩放
    public void FitBounds(double w, double h)
    {
        _surfaceSize = new Size(w, h);
        Width = w;
        Height = h;
        var prim = _owner.Screens.Primary;
        var pw = prim?.Bounds.Width ?? 1920;
        var ph = prim?.Bounds.Height ?? 1080;
        _drawingScale = Math.Min(pw / 1920.0, ph / 1080.0);
        EnsureLayer();
    }

    private void EnsureLayer()
    {
        var s = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var px = new PixelSize(
            Math.Max(1, (int)Math.Round(_surfaceSize.Width * s)),
            Math.Max(1, (int)Math.Round(_surfaceSize.Height * s)));
        if (_layer is not null && _layerPixelSize == px)
        {
            return;
        }
        _layer?.Dispose();
        _layer = new RenderTargetBitmap(px, new Vector(96 * s, 96 * s));
        _layerPixelSize = px;
        _layerDirty = true;
    }

    public void SetPenColor(Color color)
    {
        _penColor = color;
        EndAllStrokes();
    }

    public void SetPenWidth(double w)
    {
        _penWidth = Math.Max(1, w);
        EndAllStrokes();
    }

    public void SetShapeMode(WritingShapeMode mode)
    {
        _shapeMode = mode;
        EndAllStrokes();
    }

    // 结束进行中的笔画 入历史
    public void EndAllStrokes()
    {
        foreach (var state in _active.Values)
        {
            if (state.Stroke is { Points.Count: > 0 } stroke && stroke.IsFreehand)
            {
                stroke.RebuildGeometry();
                _strokes.Add(stroke);
                PushHistory(() => _strokes.Remove(stroke));
                AppendStrokeToLayer(stroke);
            }
        }
        if (_active.Count > 0)
        {
            _active.Clear();
            InvalidateVisual();
        }
    }

    public void UndoLast()
    {
        if (_history.Count == 0)
        {
            return;
        }
        _history[^1].Invoke();
        _history.RemoveAt(_history.Count - 1);
        _layerDirty = true;
        InvalidateVisual();
    }

    public void ClearAllStrokes()
    {
        if (_strokes.Count == 0 && _active.Count == 0)
        {
            return;
        }
        var snapshot = new List<WritingStroke>(_strokes);
        _strokes.Clear();
        _active.Clear();
        ResetEraseState();
        _layerDirty = true;
        PushHistory(() =>
        {
            _strokes.Clear();
            _strokes.AddRange(snapshot);
            _layerDirty = true;
        });
        InvalidateVisual();
    }

    public void CleanupState()
    {
        EndAllStrokes();
        _speedTimer.Stop();
        _eraseTimer.Stop();
        _active.Clear();
        ResetEraseState();
        InvalidateVisual();
    }

    public void ResetEraseState()
    {
        _erase.Clear();
        _eraseTimer.Stop();
        if (_erase.Count == 0)
        {
            InvalidateVisual();
        }
    }

    // 单指状态
    private sealed class StrokeState
    {
        public WritingStroke? Stroke;
    }

    // 单指擦除状态
    private sealed class EraseState
    {
        public Point PrevPos;
        public Point LivePos;
        public Point PrevSample;
        public double Speed = 1.0;
        public double Rubber;
        public bool IsTouch;
        public EraseUndoEntry? Undo;
    }

    public bool HitTest(Point point)
    {
        if (VisualRoot is not Visual root)
        {
            return false;
        }
        return root.TranslatePoint(point, this) is { } local
            && local.X >= 0 && local.Y >= 0
            && local.X <= Bounds.Width && local.Y <= Bounds.Height;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_owner.Mode == WritingToolMode.Mouse)
        {
            return;
        }
        var pt = e.GetCurrentPoint(this);
        var pos = pt.Position;
        e.Pointer.Capture(this);
        e.Handled = true;

        if (_owner.Mode == WritingToolMode.Pen)
        {
            _active[e.Pointer] = new StrokeState { Stroke = NewStroke(pos) };
            InvalidateVisual();
        }
        else
        {
            var es = new EraseState
            {
                PrevPos = pos,
                LivePos = pos,
                PrevSample = pos,
                Speed = 1.0,
                Rubber = EraseInit * _drawingScale,
                IsTouch = e.Pointer.Type == PointerType.Touch,
                Undo = new EraseUndoEntry(),
            };
            _erase[e.Pointer] = es;
            EraseSweep(pos, pos, es);
            if (!_eraseTimer.IsEnabled)
            {
                _eraseTimer.Start();
            }
            if (!_speedTimer.IsEnabled)
            {
                _speedTimer.Start();
            }
            InvalidateVisual();
        }
    }

    private WritingStroke NewStroke(Point p)
    {
        var hi = _shapeMode == WritingShapeMode.Highlight;
        var stroke = new WritingStroke
        {
            Color = hi ? Color.FromArgb(HighlightAlpha, _penColor.R, _penColor.G, _penColor.B) : _penColor,
            Width = hi ? Math.Max(HighlightMinW, _penWidth * HighlightScale) : _penWidth,
            Shape = _shapeMode,
        };
        stroke.SetPoints([p]);
        return stroke;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (_active.TryGetValue(e.Pointer, out var strokeState))
        {
            if (!pt.Properties.IsLeftButtonPressed && e.Pointer.Type == PointerType.Mouse)
            {
                return;
            }
            var stroke = strokeState.Stroke!;
            stroke.AddPoint(pt.Position);
            InvalidateVisual();
        }
        else if (_erase.TryGetValue(e.Pointer, out var es))
        {
            es.LivePos = pt.Position;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        FinishPointer(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        FinishPointer(e.Pointer);
    }

    private void FinishPointer(IPointer? pointer)
    {
        if (pointer is not null && _active.Remove(pointer, out var strokeState))
        {
            var stroke = strokeState.Stroke!;
            if (stroke.IsFreehand)
            {
                stroke.RebuildGeometry();
                _strokes.Add(stroke);
                PushHistory(() => _strokes.Remove(stroke));
                AppendStrokeToLayer(stroke);
            }
            else if (stroke.Points.Count >= 2)
            {
                if (stroke.Shape == WritingShapeMode.Rect)
                {
                    var r = new Rect(stroke.Points[0], stroke.Points[^1]);
                    stroke.SetPoints(
                    [
                        new Point(r.Left, r.Top),
                        new Point(r.Right, r.Top),
                        new Point(r.Right, r.Bottom),
                        new Point(r.Left, r.Bottom),
                        new Point(r.Left, r.Top),
                    ]);
                    stroke.Shape = WritingShapeMode.Free;
                }
                else
                {
                    stroke.SetPoints([stroke.Points[0], stroke.Points[^1]]);
                }
                stroke.RebuildGeometry();
                _strokes.Add(stroke);
                PushHistory(() => _strokes.Remove(stroke));
                AppendStrokeToLayer(stroke);
            }
            InvalidateVisual();
        }
        if (pointer is not null && _erase.Remove(pointer, out var es))
        {
            if (es.Undo is { Pairs.Count: > 0 })
            {
                PushHistory(MakeEraseUndo(es.Undo));
            }
            if (_erase.Count == 0)
            {
                _eraseTimer.Stop();
            }
            InvalidateVisual();
        }
    }

    private Action MakeEraseUndo(EraseUndoEntry? entry)
    {
        if (entry is null || entry.Pairs.Count == 0)
        {
            return static () => { };
        }
        return () =>
        {
            foreach (var (original, pieces) in entry.Pairs)
            {
                var idx = -1;
                foreach (var piece in pieces)
                {
                    var at = _IndexOf(_strokes, piece);
                    if (at >= 0)
                    {
                        if (idx < 0)
                        {
                            idx = at;
                        }
                        _strokes.RemoveAt(at);
                    }
                }
                if (idx >= 0)
                {
                    _strokes.Insert(Math.Min(idx, _strokes.Count), original);
                }
            }
        };
    }

    private void PushHistory(Action undo)
    {
        _history.Add(undo);
        if (_history.Count > 300)
        {
            _history.RemoveAt(0);
        }
    }

    private void SampleEraseSpeed(object? sender, EventArgs e)
    {
        foreach (var (pointer, es) in _erase)
        {
            var dist = Distance(es.PrevSample, es.LivePos);
            es.Speed = (es.Speed + dist) * 0.5;
            es.PrevSample = es.LivePos;
        }
        if (_erase.Count == 0)
        {
            _speedTimer.Stop();
        }
    }

    private void EraseLoopTick(object? sender, EventArgs e)
    {
        if (_owner.Mode != WritingToolMode.Eraser || _erase.Count == 0)
        {
            _eraseTimer.Stop();
            return;
        }
        foreach (var (pointer, es) in _erase)
        {
            var speed = es.Speed;
            double tSize;
            if (!es.IsTouch)
            {
                tSize = speed <= 30
                    ? Math.Max(EraseBaseMin, speed * 2.33 + 2.33)
                    : Math.Min(EraseBaseMax, speed + 30);
            }
            else
            {
                tSize = speed <= 20
                    ? Math.Max(EraseBaseMin, speed * 2.33 + 13.33)
                    : Math.Min(EraseBaseMax, 3.0 * speed);
            }
            var trubber = tSize * _drawingScale;
            if (es.Rubber < trubber)
            {
                es.Rubber += Math.Max(0.1, (trubber - es.Rubber) / 50.0);
            }
            else if (es.Rubber > trubber)
            {
                es.Rubber += Math.Min(-0.1, (trubber - es.Rubber) / 50.0);
            }
            EraseSweep(es.PrevPos, es.LivePos, es);
            es.PrevPos = es.LivePos;
        }
        InvalidateVisual();
    }

    private void EraseSweep(Point from, Point to, EraseState es)
    {
        var r = es.Rubber / 2.0;
        if (r < 0.5)
        {
            return;
        }
        var steps = Math.Max(1, (int)Math.Ceiling(Distance(from, to) / Math.Max(1.0, r / 2.0)));
        for (var i = 0; i <= steps; i++)
        {
            var t = steps == 0 ? 0 : (double)i / steps;
            var c = new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
            ClipStrokesAt(c, r, es);
        }
    }

    private void ClipStrokesAt(Point c, double r, EraseState es)
    {
        if (_strokes.Count == 0)
        {
            return;
        }
        var hit = new Rect(c.X - r, c.Y - r, r * 2, r * 2);
        for (var i = _strokes.Count - 1; i >= 0; i--)
        {
            var stroke = _strokes[i];
            if (stroke.Points.Count == 0 || !stroke.Bounds.Intersects(hit))
            {
                continue;
            }
            if (!TryClipPolyline(stroke.Points, c, r, out var pieces))
            {
                continue;
            }
            var replacements = new List<WritingStroke>(pieces.Count);
            foreach (var piece in pieces)
            {
                var span = new WritingStroke { Color = stroke.Color, Width = stroke.Width };
                span.SetPoints(piece);
                span.RebuildGeometry();
                replacements.Add(span);
            }
            es.Undo?.Pairs.Add((stroke, replacements));
            _strokes.RemoveAt(i);
            _strokes.InsertRange(i, replacements);
            _layerDirty = true;
        }
    }

    private static bool TryClipPolyline(List<Point> pts, Point c, double r, out List<List<Point>> pieces)
    {
        var result = new List<List<Point>>();
        var clipped = false;
        var cur = new List<Point>();
        void Append(Point p)
        {
            if (Distance(p, c) <= r)
            {
                clipped = true;
                if (cur.Count >= 2)
                {
                    result.Add(cur);
                }
                cur = [];
            }
            else
            {
                cur.Add(p);
            }
        }
        for (var i = 0; i < pts.Count; i++)
        {
            if (i == 0)
            {
                Append(pts[0]);
                continue;
            }
            var q = pts[i - 1];
            var p = pts[i];
            var d = Distance(q, p);
            var steps = Math.Max(1, (int)Math.Ceiling(d / 4.0));
            for (var sN = 1; sN <= steps; sN++)
            {
                var t = (double)sN / steps;
                Append(new Point(q.X + (p.X - q.X) * t, q.Y + (p.Y - q.Y) * t));
            }
        }
        if (cur.Count >= 2)
        {
            result.Add(cur);
        }
        pieces = result;
        return clipped;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static int _IndexOf(List<WritingStroke> list, WritingStroke stroke)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], stroke))
            {
                return i;
            }
        }
        return -1;
    }

    public override void Render(DrawingContext ctx)
    {
        var layerReady = false;
        if (_layer is not null)
        {
            if (_layerDirty && _erase.Count == 0)
            {
                try
                {
                    using (var dc = _layer.CreateDrawingContext(true))
                    {
                        foreach (var stroke in _strokes)
                        {
                            DrawStroke(dc, stroke);
                        }
                    }
                    _layerDirty = false;
                }
                catch (Exception e)
                {
                    Log.Warning($"笔迹缓存层绘制失败 回退矢量绘制: {e.Message}");
                    _layer.Dispose();
                    _layer = null;
                }
            }
            layerReady = _layer is not null && !_layerDirty;
        }
        if (layerReady)
        {
            ctx.DrawImage(_layer!, new Rect(0, 0, _surfaceSize.Width, _surfaceSize.Height));
        }
        else
        {
            foreach (var stroke in _strokes)
            {
                DrawStroke(ctx, stroke);
            }
        }
        foreach (var state in _active.Values)
        {
            var s = state.Stroke!;
            if (s.IsFreehand)
            {
                if (s.Points.Count == 1)
                {
                    var r = s.Width / 2;
                    ctx.DrawEllipse(BrushFor(s.Color), null, s.Points[0], r, r);
                }
                else
                {
                    if (s.GeometryDirty)
                    {
                        s.RebuildGeometry();
                    }
                    DrawStroke(ctx, s);
                }
            }
            else if (s.Points.Count >= 2)
            {
                DrawShape(ctx, s, s.Points[0], s.Points[^1]);
            }
        }
        foreach (var es in _erase.Values)
        {
            ctx.DrawEllipse(null, EraserPen, es.LivePos, es.Rubber / 2.0, es.Rubber / 2.0);
        }
    }

    private void AppendStrokeToLayer(WritingStroke stroke)
    {
        if (_layer is null || _layerDirty)
        {
            return;
        }
        try
        {
            using var dc = _layer.CreateDrawingContext(false);
            DrawStroke(dc, stroke);
        }
        catch (Exception e)
        {
            Log.Warning($"笔迹落入缓存层失败 回退整层重绘: {e.Message}");
            _layerDirty = true;
        }
    }

    private static void DrawStroke(DrawingContext ctx, WritingStroke stroke)
    {
        if (stroke.Points.Count == 0 || stroke.Geometry is null)
        {
            return;
        }
        ctx.DrawGeometry(null, PenFor(stroke.Color, stroke.Width), stroke.Geometry);
    }

    private static void DrawShape(DrawingContext ctx, WritingStroke stroke, Point p0, Point p1)
    {
        var geo = new StreamGeometry();
        if (stroke.Shape == WritingShapeMode.Rect)
        {
            var r = new Rect(p0, p1);
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(r.Left, r.Top), false);
                g.LineTo(new Point(r.Right, r.Top));
                g.LineTo(new Point(r.Right, r.Bottom));
                g.LineTo(new Point(r.Left, r.Bottom));
                g.LineTo(new Point(r.Left, r.Top));
                g.EndFigure(false);
            }
        }
        else
        {
            using (var g = geo.Open())
            {
                g.BeginFigure(p0, false);
                g.LineTo(p1);
                g.EndFigure(false);
            }
        }
        ctx.DrawGeometry(null, PenFor(stroke.Color, stroke.Width), geo);
    }

    private static Pen PenFor(Color color, double width)
    {
        var key = (color.ToUInt32(), width);
        if (PenCache.TryGetValue(key, out var pen))
        {
            return pen;
        }
        if (PenCache.Count > 64)
        {
            PenCache.Clear();
        }
        pen = new Pen(BrushFor(color), width)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        PenCache[key] = pen;
        return pen;
    }

    private static IBrush BrushFor(Color color)
    {
        var key = color.ToUInt32();
        if (BrushCache.TryGetValue(key, out var brush))
        {
            return brush;
        }
        if (BrushCache.Count > 64)
        {
            BrushCache.Clear();
        }
        brush = new SolidColorBrush(color);
        BrushCache[key] = brush;
        return brush;
    }
}

internal sealed class PenSettingsRow : StackPanel
{
    private static readonly (string Hex, string Name)[] Colors =
    [
        ("#FF0000", "红"), ("#FFA500", "橙"), ("#FFFF00", "黄"),
        ("#00AA00", "绿"), ("#0000FF", "蓝"), ("#800080", "紫"),
        ("#000000", "黑"), ("#FFFFFF", "白"),
    ];

    private static readonly (WritingShapeMode Mode, string Name)[] Modes =
    [
        (WritingShapeMode.Free, "自由"),
        (WritingShapeMode.Line, "直线"),
        (WritingShapeMode.Rect, "矩形"),
        (WritingShapeMode.Highlight, "荧光"),
    ];

    private const double TrackW = 104;
    private const double ThumbSize = 14;
    private const double PenMinW = 1;
    private const double PenMaxW = 12;

    private readonly Dictionary<Color, Border> _swatches = [];
    private readonly Dictionary<WritingShapeMode, Border> _modes = [];
    private readonly IBrush _textFg;
    private readonly IBrush _pillBg;
    private readonly Border _thumb;
    private readonly TextBlock _widthText;
    private Color _accent;
    private bool _dragging;

    public PenSettingsRow()
    {
        var dark = ThemeSense.IsDark(this);
        _accent = Common.ParseAccentColor(Config.ThemeColor.Value);
        _textFg = new SolidColorBrush(Color.Parse(dark ? "#E6E6E6" : "#1C1C1C"));
        _pillBg = new SolidColorBrush(Color.Parse(dark ? "#333333" : "#EAEAEA"));

        Orientation = Orientation.Horizontal;
        Spacing = 10;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;

        var colorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var (hex, name) in Colors)
        {
            var box = MakeColorSwatch(hex, name);
            colorRow.Children.Add(box);
            _swatches[Color.Parse(hex)] = box;
        }
        Children.Add(colorRow);
        Children.Add(MakeDivider());

        _widthText = new TextBlock
        {
            Text = "3",
            FontSize = 12,
            Width = 16,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = _textFg,
        };
        var track = new Border
        {
            Width = TrackW,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.Parse(dark ? "#4A4A4A" : "#D6D6D6")),
            IsHitTestVisible = false,
        };
        _thumb = new Border
        {
            Width = ThumbSize,
            Height = ThumbSize,
            CornerRadius = new CornerRadius(ThumbSize / 2),
            Background = new SolidColorBrush(_accent),
            IsHitTestVisible = false,
        };
        var pad = new Canvas
        {
            Width = TrackW,
            Height = 18,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Canvas.SetTop(track, 7);
        Canvas.SetTop(_thumb, 2);
        pad.Children.Add(track);
        pad.Children.Add(_thumb);
        pad.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(pad).Properties.IsLeftButtonPressed)
            {
                return;
            }
            _dragging = true;
            e.Pointer.Capture(pad);
            SetWidthByX(e.GetPosition(pad).X);
            e.Handled = true;
        };
        pad.PointerMoved += (_, e) =>
        {
            if (!_dragging)
            {
                return;
            }
            SetWidthByX(e.GetPosition(pad).X);
            e.Handled = true;
        };
        pad.PointerReleased += (_, e) =>
        {
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        };
        pad.PointerCaptureLost += (_, _) => _dragging = false;
        Children.Add(pad);
        Children.Add(_widthText);
        Children.Add(MakeDivider());

        var modeGrid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto"),
            RowDefinitions = RowDefinitions.Parse("Auto,Auto"),
            ColumnSpacing = 4,
            RowSpacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (var i = 0; i < Modes.Length; i++)
        {
            var pill = MakeModePill(Modes[i].Mode, Modes[i].Name);
            Grid.SetColumn(pill, i % 2);
            Grid.SetRow(pill, i / 2);
            modeGrid.Children.Add(pill);
            _modes[Modes[i].Mode] = pill;
        }
        Children.Add(modeGrid);

        WritingPadService.StateChanged += Refresh;
        Config.ThemeColor.ValueChanged += OnThemeChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            WritingPadService.StateChanged -= Refresh;
            Config.ThemeColor.ValueChanged -= OnThemeChanged;
        };
        Refresh();
    }

    private void OnThemeChanged(string color)
    {
        _accent = Common.ParseAccentColor(color);
        Dispatcher.UIThread.Post(Refresh);
    }

    public void Refresh()
    {
        var accent = new SolidColorBrush(_accent);
        var ring = new BoxShadows(new BoxShadow { Spread = 2, Color = _accent });
        var color = WritingPadService.PenColor;
        foreach (var (c, box) in _swatches)
        {
            box.BoxShadow = c == color ? ring : default;
        }
        var width = Math.Clamp((int)Math.Round(WritingPadService.PenWidth), (int)PenMinW, (int)PenMaxW);
        _widthText.Text = width.ToString();
        Canvas.SetLeft(_thumb, ThumbX(width));
        _thumb.Background = accent;
        var mode = WritingPadService.ShapeMode;
        foreach (var (m, pill) in _modes)
        {
            var on = m == mode;
            pill.Background = on ? accent : _pillBg;
            if (pill.Child is TextBlock text)
            {
                text.Foreground = on ? Brushes.White : _textFg;
            }
        }
    }

    private static double ThumbX(double width)
    {
        var t = (width - PenMinW) / (PenMaxW - PenMinW);
        return t * (TrackW - ThumbSize);
    }

    private void SetWidthByX(double x)
    {
        var t = Math.Clamp((x - ThumbSize / 2) / (TrackW - ThumbSize), 0, 1);
        WritingPadService.SetPenWidth(Math.Round(PenMinW + t * (PenMaxW - PenMinW)));
    }

    private Border MakeColorSwatch(string hex, string name)
    {
        var c = Color.Parse(hex);
        var lightness = (Math.Max(c.R, Math.Max(c.G, c.B)) + Math.Min(c.R, Math.Min(c.G, c.B))) / 2.0;
        var normal = new SolidColorBrush(lightness > 200 ? Color.FromRgb(0x55, 0x55, 0x55) : Color.FromRgb(0x88, 0x88, 0x88));
        var box = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(c),
            BorderBrush = normal,
            BorderThickness = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(box, name);
        box.PointerPressed += (_, e) =>
        {
            WritingPadService.SetPen(c);
            e.Handled = true;
        };
        return box;
    }

    private Border MakeModePill(WritingShapeMode mode, string name)
    {
        var pill = new Border
        {
            Width = 46,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            Background = _pillBg,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = name,
                FontSize = 12.5,
                Foreground = _textFg,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        pill.PointerPressed += (_, e) =>
        {
            WritingPadService.SetShapeMode(mode);
            e.Handled = true;
        };
        return pill;
    }

    private static Control MakeDivider() => new Border
    {
        Width = 1,
        Height = 22,
        Background = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88), 0.5),
        VerticalAlignment = VerticalAlignment.Center,
    };
}
