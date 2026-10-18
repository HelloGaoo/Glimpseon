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

// 悬浮球
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;

namespace Glimpseon.UI;

public class FloatingBallWindow : Window
{
    private const double WinW = 576;       // 展开画布宽
    private const double CollapsedW = 96;  // 收起画布宽
    private const double CanvasH = 96;
    private const double BallSize = 94;    // 球径
    private const double BarH = 94;
    private const double BarW = 536;       // 工具条宽
    private const double HiddenBarW = 30;  // 藏球后的条宽
    private const double BtnSize = 80;     // 按钮 80 步距 96
    private const double BtnStep = 96;
    private const double RightMargin = 18; // 初始 右缘留白
    private const double BallBottomOffset = 232; // 初始 球底距屏底
    private const double DragThreshold = 20;     // 单击位移阈值
    private const double InkeysFrameMs = 1000.0 / 24; // 24fps 换算
    private const int DebounceMs = 180;    // 操作防抖  
    private const double CollapseMaxMs = 380; // 收起动画上限 到点吸附缩窗
    private const double WritingBarH = 86;   // 书写条高(按钮64+边距18+描边4)
    private const double WritingBarW = 560;  // 书写条宽(8按钮64+间距28+边距16+描边4)
    private const double WbBtnSize = 64;     // 书写条按钮边长
    private const double WritingBarGap = 8;  // 与主条间距
    private const double BarRadius = 26;     // 条体圆角
    private const double MainBtnRadius = 18; // 主条按钮圆角
    private const double WbBtnRadius = 15;   // 书写条按钮圆角
    private const double MainIconSize = 38;
    private const double MainLabelSize = 16;
    private const double WbIconSize = 30;
    private const double WbLabelSize = 13;

    private enum ExpandDir { Left, Right }

    // 按钮 主窗口/画笔/音量
    private static readonly (FASymbol Icon, string Label, Action? Action)[] Actions =
    [
        (FASymbol.Home, "主窗口", ShowMainWindow),
        (FASymbol.Edit, "画笔", WritingPadService.TogglePen),
        (FASymbol.Volume, "音量", null),
    ];

    // 球底中点坐标
    private void UpdateBallAnchor()
    {
        var s = RenderScaling;
        var left = BallLeftPixel();
        WritingPadService.BallAnchor = new PixelPoint(
            left.X + (int)Math.Round(BallSize / 2 * s),
            left.Y + (int)Math.Round(BallSize * s));
    }

    private static void ShowMainWindow()
    {
        if (App.Lifetime?.MainWindow is not { } window)
        {
            return;
        }
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Maximized;
        }
        window.Activate();
    }

    // 覆盖层显示时把球重新提到最上
    private void OnPadStateChanged()
    {
        if (WritingPadService.OverlayVisible)
        {
            Topmost = false;
            Topmost = true;
        }
        RefreshPadButtons();
    }

    private sealed class Interp
    {
        public double V, T;
        public double S = 3, E = 0.5;
        public void Snap(double v) { V = v; T = v; }
        public void Set(double target, double speed) { T = target; S = speed; }
        public void Step(double dtMs)
        {
            var d = T - V;
            if (Math.Abs(d) <= E)
            {
                V = T;
                return;
            }
            var tau = InkeysFrameMs / Math.Log(S / (S - 2));
            V += d * (1 - Math.Exp(-dtMs / tau));
        }
        public bool Done => Math.Abs(T - V) <= E;
    }

    private readonly Canvas _canvas = new();
    private readonly Border _ball = new();
    private readonly Border _bar = new();
    private readonly Border _writingBar = new();
    private readonly List<BarButton> _buttons = [];
    private readonly List<BarButton> _allButtons = [];
    private readonly List<Interp> _btnX = [];
    private readonly List<Interp> _btnO = [];
    private readonly Interp _barX = new();
    private readonly Interp _barW = new();
    private readonly Interp _barO = new();
    private readonly Interp _wbH = new();
    private readonly Interp _wbO = new();
    private bool _wbOpen;
    private bool _wbRestoreOnExpand;
    private bool _wbRetracting;
    private bool _pendingMouseCommit;

    private const double ClearBarH = 86;
    private const double ClearBarW = 280;
    private const double ClearKnobSize = 52;
    private const double ClearKnobInset = 5;
    private Border _clearBar = null!;
    private Border? _clearKnob;
    private DispatcherTimer? _clearBackTimer;
    private bool _clOpen;
    private bool _clearDragging;
    private double _clearDragX;
    private readonly Interp _clH = new();
    private readonly Interp _clO = new();

    private static double ClearTravel => ClearBarW - 34 - ClearKnobSize - ClearKnobInset * 2;

    private const double PenBarH = ClearBarH;
    private Border _penBar = null!;
    private bool _pnOpen;
    private readonly Interp _pnH = new();
    private readonly Interp _pnO = new();
    private BarButton? _penBtn;
    private BarButton? _wbMouse, _wbPenTool, _wbEraser, _wbTrans, _wbWhite, _wbFreeze;
    private readonly Interp _fillA = new();
    private readonly Interp _strokeR = new(), _strokeG = new(), _strokeB = new(), _strokeA = new();
    private Color _accent;
    private bool _animating;
    private double _animElapsed;
    private long _lastFrame;

    private bool _expanded;
    private bool _pendingShrink;
    private ExpandDir _dir = ExpandDir.Left;

    private bool _pressed;
    private bool _dragCollapsed;
    private double _dragDist;
    private PixelPoint _dragAnchorBall;
    private PixelPoint _pressScreen;
    private DateTime _lastManip = DateTime.MinValue;

    public FloatingBallWindow()
    {
        _accent = Common.ParseAccentColor(Config.ThemeColor.Value);

        // 无边框透明置顶
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        CanResize = false;
        FontFamily = Common.AppFontFamily;

        Width = CollapsedW;
        Height = CanvasH;

        // 主题色
        Config.ThemeColor.ValueChanged += OnThemeColorChanged;
        Closed += (_, _) => Config.ThemeColor.ValueChanged -= OnThemeColorChanged;


        WritingPadService.StateChanged += OnPadStateChanged;
        WritingPadService.CloseRequested += OnCloseRequested;
        WritingPadService.BallHandleProvider = BallHandle;
        Closed += (_, _) =>
        {
            WritingPadService.StateChanged -= OnPadStateChanged;
            WritingPadService.CloseRequested -= OnCloseRequested;
            WritingPadService.BallHandleProvider = null;
        };

        Content = BuildContent();
        SnapCollapsedLayout();
        PlaceInitial();

        Opened += (_, _) => ClampToScreen();
    }

    private double BallX => _dir == ExpandDir.Left ? WinW - 2 - BallSize : 2;

    private Canvas BuildContent()
    {
        // 工具条底板
        StyleBar(_bar);
        _bar.Height = BarH;
        _bar.Opacity = 0;
        _bar.IsHitTestVisible = false;
        Canvas.SetTop(_bar, 1);
        _canvas.Children.Add(_bar);

        // 按钮 图标
        foreach (var (symbol, label, action) in Actions)
        {
            var btn = MakeBarButton(FAIcons.Icon(symbol, MainIconSize), label, BtnSize, MainBtnRadius, MainLabelSize);
            btn.Opacity = 0;
            btn.IsHitTestVisible = false;
            if (label == "画笔")
            {
                _penBtn = btn;
                btn.Clicked += OnPenClick;
            }
            else if (action is not null)
            {
                btn.Clicked += action;
            }
            _buttons.Add(btn);
            _btnX.Add(new Interp());
            _btnO.Add(new Interp());
            // 竖向居中 (条高 - 钮高) / 2
            Canvas.SetTop(btn, (BarH - BtnSize) / 2);
            _canvas.Children.Add(btn);
        }

        // 球
        var logo = LoadLogo();
        _ball.Width = BallSize;
        _ball.Height = BallSize;
        _ball.CornerRadius = new CornerRadius(BallSize / 2);
        _ball.BorderThickness = new Thickness(2);
        _ball.BorderBrush = new SolidColorBrush(Colors.White, 0.9);
        _ball.Background = new SolidColorBrush(_accent, 0.25);
        _ball.BoxShadow = BallShade;
        _ball.Cursor = new Cursor(StandardCursorType.Hand);
        _ball.Child = logo;
        Canvas.SetTop(_ball, 1);
        _canvas.Children.Add(_ball);

        _ball.PointerPressed += OnBallPressed;
        _ball.PointerMoved += OnBallMoved;
        _ball.PointerReleased += OnBallReleased;
        _ball.PointerCaptureLost += (_, _) => _pressed = false;

        // 书写工具栏
        _wbMouse = MakeWbButton(FAIcons.CursorIcon(34), "鼠标");
        _wbMouse.Clicked += () => WritingPadService.SetMode(WritingToolMode.Mouse);
        _wbPenTool = MakeWbButton(FASymbol.Edit, "画笔");
        _wbPenTool.Clicked += () =>
        {
            if (WritingPadService.Mode == WritingToolMode.Pen)
            {
                TogglePenBar();
            }
            else
            {
                WritingPadService.SetMode(WritingToolMode.Pen);
            }
        };
        _wbEraser = MakeWbButton(FASymbol.ClearFormatting, "擦除");
        _wbEraser.Clicked += () =>
        {
            if (WritingPadService.Mode == WritingToolMode.Eraser)
            {
                ToggleClearBar();
                return;
            }
            WritingPadService.SetMode(WritingToolMode.Eraser);
        };
        var wbUndo = MakeWbButton(FASymbol.Undo, "撤回");
        wbUndo.Clicked += WritingPadService.Undo;
        _wbTrans = MakeWbButton(FASymbol.View, "透明");
        _wbTrans.Clicked += () => WritingPadService.SetWhiteboard(false);
        _wbWhite = MakeWbButton(FASymbol.SlideShow, "白板");
        _wbWhite.Clicked += () => WritingPadService.SetWhiteboard(true);
        _wbFreeze = MakeWbButton(FASymbol.CameraFilled, "定格");
        _wbFreeze.Clicked += WritingPadService.ToggleFreeze;
        var wbClose = MakeWbButton(FASymbol.Cancel, "关闭");
        wbClose.Clicked += WritingPadService.CloseOverlay;

        var wbRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 9, 10, 9),
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        wbRow.Children.Add(_wbMouse);
        wbRow.Children.Add(_wbPenTool);
        wbRow.Children.Add(_wbEraser);
        wbRow.Children.Add(wbUndo);
        wbRow.Children.Add(_wbTrans);
        wbRow.Children.Add(_wbWhite);
        wbRow.Children.Add(_wbFreeze);
        wbRow.Children.Add(wbClose);

        _writingBar.Child = wbRow;
        StyleBar(_writingBar);
        _writingBar.Width = WritingBarW;
        _writingBar.Height = 0;
        _writingBar.Opacity = 0;
        _writingBar.IsHitTestVisible = false;
        _writingBar.ClipToBounds = true;
        Canvas.SetTop(_writingBar, CanvasH + WritingBarGap);
        _canvas.Children.Add(_writingBar);

        // 滑动清屏条
        _clearBar = BuildClearBar();
        _clearBar.Height = 0;
        _clearBar.Opacity = 0;
        _clearBar.IsHitTestVisible = false;
        _clearBar.ClipToBounds = true;
        Canvas.SetTop(_clearBar, CanvasH + WritingBarGap + WritingBarH + WritingBarGap);
        _canvas.Children.Add(_clearBar);

        // 笔设置条
        _penBar = BuildPenBar();
        _penBar.Height = 0;
        _penBar.Opacity = 0;
        _penBar.IsHitTestVisible = false;
        _penBar.ClipToBounds = true;
        Canvas.SetTop(_penBar, CanvasH + WritingBarGap + WritingBarH + WritingBarGap);
        _canvas.Children.Add(_penBar);
        return _canvas;
    }

    // 滑动清屏条
    private Border BuildClearBar()
    {
        _clearBackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _clearBackTimer.Tick += (_, _) => StepClearKnobBack();
        _clearKnob = new Border
        {
            Width = ClearKnobSize,
            Height = ClearKnobSize,
            CornerRadius = new CornerRadius(ClearKnobSize / 2),
            Background = ClearKnobBrush(),
            BoxShadow = BoxShadows.Parse("0 2 8 0 #33000000"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(ClearKnobInset, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = ClearKnobIcon(),
        };
        _clearKnob.PointerPressed += OnClearKnobPressed;
        _clearKnob.PointerMoved += OnClearKnobMoved;
        _clearKnob.PointerReleased += (_, _) => EndClearDrag();
        _clearKnob.PointerCaptureLost += (_, _) => EndClearDrag();

        var text = new TextBlock
        {
            Text = "滑动清除笔迹",
            FontSize = 14,
            FontWeight = FontWeight.Medium,
            Foreground = ClearTextBrush(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(ClearKnobSize + 16, 0, 18, 0),
            IsHitTestVisible = false,
        };
        var track = new Border
        {
            CornerRadius = new CornerRadius((ClearBarH - 20 - 2) / 2),
            Background = ClearTrackBrush(),
            BorderBrush = ClearTrackBorderBrush(),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(16, 10, 16, 10),
            Child = new Panel { Children = { text, _clearKnob } },
        };
        var bar = MakeBar(ClearBarW);
        bar.Child = track;
        return bar;
    }

    // 笔设置条
    private Border BuildPenBar()
    {
        var bar = MakeBar(WritingBarW);
        bar.Child = new PenSettingsRow();
        return bar;
    }

    // 滑钮图标
    private static Control ClearKnobIcon()
    {
        var bars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        foreach (var h in new[] { 14.0, 22.0, 14.0 })
        {
            bars.Children.Add(new Border
            {
                Width = 3,
                Height = h,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        return bars;
    }

    private IBrush ClearTrackBrush() => DarkMode
        ? new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A), 0.98)
        : new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7), 0.98);

    private IBrush ClearTrackBorderBrush() => DarkMode
        ? new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
        : new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));

    private IBrush ClearKnobBrush() => DarkMode
        ? new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A))
        : Brushes.White;

    private IBrush ClearTextBrush() => DarkMode
        ? new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8))
        : new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));

    private bool DarkMode => ThemeSense.IsDark(this);

    private static readonly BoxShadows BarShade = BoxShadows.Parse("0 10 26 0 #38000000");
    private static readonly BoxShadows BallShade = BoxShadows.Parse("0 5 16 0 #4D000000");

    // 主文字色
    private IBrush TextBrush() => new SolidColorBrush(Color.Parse(DarkMode ? "#EAEAEA" : "#1C1C1C"));

    // 选中底色
    private IBrush AccentBrush() => new SolidColorBrush(_accent, 0.95);

    // 条体描边
    private IBrush BarBorderBrush() => DarkMode
        ? new SolidColorBrush(Colors.White, 0.12)
        : new SolidColorBrush(Colors.Black, 0.10);

    // 条体外观
    private void StyleBar(Border bar)
    {
        bar.CornerRadius = new CornerRadius(BarRadius);
        bar.BorderThickness = new Thickness(1);
        bar.BorderBrush = BarBorderBrush();
        bar.BoxShadow = BarShade;
        bar.Background = ResolveBrush("ApplicationBackgroundBrush", Color.FromRgb(0x1E, 0x21, 0x29), 0.98);
    }

    private Border MakeBar(double width)
    {
        var bar = new Border { Width = width };
        StyleBar(bar);
        return bar;
    }

    // 图标文字按钮
    private BarButton MakeBarButton(Control icon, string label, double size, double radius, double labelSize)
    {
        var btn = new BarButton(icon, label, size, radius, labelSize, size > 70 ? 3 : 2);
        btn.SetPalette(TextBrush(), AccentBrush());
        _allButtons.Add(btn);
        return btn;
    }

    // 书写条按钮
    private BarButton MakeWbButton(FASymbol symbol, string label) =>
        MakeWbButton(FAIcons.Icon(symbol, WbIconSize), label);

    private BarButton MakeWbButton(Control icon, string label) =>
        MakeBarButton(icon, label, WbBtnSize, WbBtnRadius, WbLabelSize);

    // 选中态
    private static void SetChecked(BarButton? btn, bool on)
    {
        if (btn is not null)
        {
            btn.Selected = on;
        }
    }

    // 书写条 x 右对齐主横条右缘
    private double WritingBarX()
    {
        var mainBarRight = (_dir == ExpandDir.Left ? 1 : WinW - 1 - BarW) + BarW;
        return Math.Clamp(mainBarRight - WritingBarW, 2, WinW - 2 - WritingBarW);
    }


    // 窗口句柄
    private nint BallHandle() => TryGetPlatformHandle()?.Handle ?? nint.Zero;

    // 主动关闭
    private void OnCloseRequested()
    {
        _wbRestoreOnExpand = false;
        _pendingMouseCommit = true;
        if (!CloseWritingBar())
        {
            CommitPendingMouse();
        }
    }

    private void CommitPendingMouse()
    {
        if (!_pendingMouseCommit)
        {
            return;
        }
        _pendingMouseCommit = false;
        WritingPadService.SetMode(WritingToolMode.Mouse);
    }

    private void OnPenClick()
    {
        if (_wbOpen && !_wbRetracting)
        {
            WritingPadService.CloseOverlay();
        }
        else
        {
            WritingPadService.SetMode(WritingToolMode.Pen);
            OpenWritingBar();
        }
    }

    private void OpenWritingBar()
    {
        _wbRetracting = false;
        _pendingMouseCommit = false;
        _wbOpen = true;
        _wbH.Set(WritingBarH, 3);
        _wbO.Set(1, 3);
        UpdateWindowHeight();
        StartAnim();
        LockMainBar(true);
        RefreshPadButtons();
    }

    private bool CloseWritingBar()
    {
        if (!_wbOpen || _wbRetracting)
        {
            return false;
        }
        _wbRetracting = true;
        CloseClearBar();
        ClosePenBar();
        _wbH.Set(0, 5);
        _wbO.Set(0, 5);
        StartAnim();
        return true;
    }

    private void LockMainBar(bool locked)
    {
        foreach (var btn in _buttons)
        {
            if (ReferenceEquals(btn, _penBtn))
            {
                continue;
            }
            btn.IsHitTestVisible = !locked;
        }
    }

    private void ToggleClearBar()
    {
        if (_clOpen)
        {
            CloseClearBar();
        }
        else
        {
            OpenClearBar();
        }
    }

    private void OpenClearBar()
    {
        if (!_wbOpen || _wbRetracting || _clOpen)
        {
            return;
        }
        ClosePenBar();
        _clOpen = true;
        ResetClearKnob();
        _clH.Set(ClearBarH, 3);
        _clO.Set(1, 3);
        UpdateWindowHeight();
        StartAnim();
    }

    private void CloseClearBar()
    {
        if (!_clOpen)
        {
            return;
        }
        _clOpen = false;
        _clearDragging = false;
        _clearBackTimer?.Stop();
        _clH.Set(0, 5);
        _clO.Set(0, 5);
        UpdateWindowHeight();
        StartAnim();
    }

    private void TogglePenBar()
    {
        if (_pnOpen)
        {
            ClosePenBar();
        }
        else
        {
            OpenPenBar();
        }
    }

    private void OpenPenBar()
    {
        if (!_wbOpen || _wbRetracting || _pnOpen)
        {
            return;
        }
        CloseClearBar();
        _pnOpen = true;
        _pnH.Set(PenBarH, 3);
        _pnO.Set(1, 3);
        UpdateWindowHeight();
        StartAnim();
    }

    private void ClosePenBar()
    {
        if (!_pnOpen)
        {
            return;
        }
        _pnOpen = false;
        _pnH.Set(0, 5);
        _pnO.Set(0, 5);
        UpdateWindowHeight();
        StartAnim();
    }

    private void UpdateWindowHeight()
    {
        var h = CanvasH;
        if (_wbOpen || _wbH.V > 1)
        {
            h += WritingBarGap + WritingBarH;
        }
        if (_clOpen || _clH.V > 1 || _pnOpen || _pnH.V > 1)
        {
            h += WritingBarGap + ClearBarH;
        }
        if (Math.Abs(Height - h) > 0.5)
        {
            Height = h;
            ClampToScreen();
        }
    }

    private double ClearBarX()
    {
        if (_wbEraser?.TranslatePoint(new Point(_wbEraser.Bounds.Width / 2, 0), _canvas) is { } center)
        {
            return Math.Clamp(center.X - ClearBarW / 2, 2, Math.Max(2, WinW - 2 - ClearBarW));
        }
        var fallback = WritingBarX() + 8 + 2 * (WbBtnSize + 4) + WbBtnSize / 2.0 - ClearBarW / 2;
        return Math.Clamp(fallback, 2, Math.Max(2, WinW - 2 - ClearBarW));
    }

    private void OnClearKnobPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_clearKnob).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _clearDragging = true;
        _clearBackTimer?.Stop();
        _clearDragX = e.GetPosition(_clearBar).X;
        e.Pointer.Capture(_clearKnob);
        e.Handled = true;
    }

    private void OnClearKnobMoved(object? sender, PointerEventArgs e)
    {
        if (!_clearDragging || !e.GetCurrentPoint(_clearKnob).Properties.IsLeftButtonPressed)
        {
            return;
        }
        var dx = e.GetPosition(_clearBar).X - _clearDragX;
        var travel = Math.Clamp(dx, 0, ClearTravel);
        SetClearKnob(travel);
        if (travel >= ClearTravel - 0.5)
        {
            _clearDragging = false;
            WritingPadService.ClearAll();
            CloseClearBar();
        }
    }

    private void EndClearDrag()
    {
        if (!_clearDragging)
        {
            return;
        }
        _clearDragging = false;
        _clearBackTimer?.Start();
    }

    private void StepClearKnobBack()
    {
        if (_clearKnob is null)
        {
            _clearBackTimer?.Stop();
            return;
        }
        var current = _clearKnob.Margin.Left - ClearKnobInset;
        var next = Math.Max(0, current - Math.Max(6, current * 0.35));
        SetClearKnob(next);
        if (next <= 0)
        {
            _clearBackTimer?.Stop();
        }
    }

    private void ResetClearKnob()
    {
        _clearBackTimer?.Stop();
        SetClearKnob(0);
    }

    private void SetClearKnob(double travel)
    {
        if (_clearKnob is not null)
        {
            _clearKnob.Margin = new Thickness(ClearKnobInset + travel, 0, 0, 0);
        }
    }

    private void RefreshPadButtons()
    {
        SetChecked(_penBtn, _wbOpen);
        var mode = WritingPadService.Mode;
        var wb = WritingPadService.Whiteboard;
        var frozen = WritingPadService.Freeze;
        if (_clOpen && mode != WritingToolMode.Eraser)
        {
            CloseClearBar();
        }
        if (_pnOpen && mode != WritingToolMode.Pen)
        {
            ClosePenBar();
        }
        SetChecked(_wbMouse, mode == WritingToolMode.Mouse);
        SetChecked(_wbPenTool, mode == WritingToolMode.Pen);
        SetChecked(_wbEraser, mode == WritingToolMode.Eraser);
        SetChecked(_wbTrans, !wb);
        SetChecked(_wbWhite, wb);
        SetChecked(_wbFreeze, frozen);
        if (_wbTrans is not null)
        {
            _wbTrans.Opacity = frozen ? 0.35 : 1;
        }
        if (_wbWhite is not null)
        {
            _wbWhite.Opacity = frozen ? 0.35 : 1;
        }
    }

    private void OnThemeColorChanged(string color)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _accent = Common.ParseAccentColor(color);
            foreach (var btn in _allButtons)
            {
                btn.SetPalette(TextBrush(), AccentBrush());
            }
            if (_expanded || _animating)
            {
                _fillA.Set(255, 3);
                _strokeR.Set(_accent.R, 3);
                _strokeG.Set(_accent.G, 3);
                _strokeB.Set(_accent.B, 3);
                _strokeA.Set(255, 3);
            }
            ApplyInterp();
        });
    }

    private Image LoadLogo()
    {
        var image = new Image
        {
            Width = 40,
            Height = 40,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        try
        {
            var path = Paths.GetResourcePath(Constants.AppIcon);
            if (File.Exists(path))
            {
                image.Source = new Bitmap(path);
            }
            else
            {
                Log.Warning("悬浮球图标不存在");
            }
        }
        catch (Exception e)
        {
            Log.Warning($"悬浮球图标加载失败: {e.Message}");
        }
        return image;
    }

    private IBrush ResolveBrush(string key, Color fallback, double alpha)
    {
        if (this.TryFindResource(key, out var value) && value is IBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(fallback, alpha);
    }

    private void PlaceInitial()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }
        var b = screen.Bounds;
        var s = screen.Scaling;
        Position = new PixelPoint(
            b.Right - (int)((WinW + RightMargin) * s),
            b.Bottom - (int)((1 + BallSize + BallBottomOffset) * s));
        UpdateBallAnchor();
    }

    private PixelRect ScreenBounds() =>
        (Screens.ScreenFromPoint(Position) ?? Screens.Primary)?.Bounds ?? new PixelRect(0, 0, 1920, 1080);

    private PixelPoint ToScreen(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var s = RenderScaling;
        return new PixelPoint(Position.X + (int)Math.Round(p.X * s), Position.Y + (int)Math.Round(p.Y * s));
    }

    private PixelPoint BallLeftPixel()
    {
        var s = RenderScaling;
        return new PixelPoint(Position.X + (int)Math.Round(Canvas.GetLeft(_ball) * s), Position.Y);
    }

    private void SnapCollapsedLayout()
    {
        _dir = ExpandDir.Left;
        Canvas.SetLeft(_ball, 1);
        Canvas.SetLeft(_bar, 1 + 26);
        _barX.Snap(1 + 26);
        _barW.Snap(30);
        _barO.Snap(0);
        for (var i = 0; i < _buttons.Count; i++)
        {
            Canvas.SetLeft(_buttons[i], 1 + 26);
            _btnX[i].Snap(1 + 26);
            _btnO[i].Snap(0);
        }
        _fillA.Snap(64);
        _strokeR.Snap(255);
        _strokeG.Snap(255);
        _strokeB.Snap(255);
        _strokeA.Snap(230);
        ApplyInterp();
    }

    private void SnapExpandedLayout()
    {
        Canvas.SetLeft(_ball, BallX);
        var hiddenX = BallX + 26;
        Canvas.SetLeft(_bar, hiddenX);
        _barX.Snap(hiddenX);
        _barW.Snap(30);
        _barO.Snap(0);
        for (var i = 0; i < _buttons.Count; i++)
        {
            Canvas.SetLeft(_buttons[i], hiddenX);
            _btnX[i].Snap(hiddenX);
            _btnO[i].Snap(0);
        }
        ApplyInterp();
    }

    private void SetExpandTargets()
    {
        _barX.Set(_dir == ExpandDir.Left ? 1 : WinW - 1 - BarW, 3);
        _barW.Set(BarW, 3);
        _barO.Set(1, 3);
        _fillA.Set(255, 3);
        _strokeR.Set(_accent.R, 3);
        _strokeG.Set(_accent.G, 3);
        _strokeB.Set(_accent.B, 3);
        _strokeA.Set(255, 3);
        for (var i = 0; i < _buttons.Count; i++)
        {
            var slot = _dir == ExpandDir.Left
                ? 8 + i * BtnStep
                : WinW - 8 - BtnSize - (Actions.Length - 1 - i) * BtnStep;
            _btnX[i].Set(slot, 3);
            _btnO[i].Set(1, 3);
        }
        _buttons.ForEach(b => b.IsHitTestVisible = true);
        LockMainBar(_wbOpen);
    }

    private void SetCollapseTargets()
    {
        var hiddenX = BallX + 26;
        _barX.Set(hiddenX, 5);
        _barW.Set(30, 5);
        _barO.Set(0, 5);
        _fillA.Set(64, 5);
        _strokeR.Set(255, 5);
        _strokeG.Set(255, 5);
        _strokeB.Set(255, 5);
        _strokeA.Set(230, 5);
        for (var i = 0; i < _buttons.Count; i++)
        {
            _btnX[i].Set(hiddenX, 5);
            _btnO[i].Set(0, 5);
        }
        _buttons.ForEach(b => b.IsHitTestVisible = false);
    }

    private void ApplyInterp()
    {
        Canvas.SetLeft(_bar, _barX.V);
        _bar.Width = _barW.V;
        var reveal = BarReveal;
        _bar.Opacity = _barO.V * reveal;
        for (var i = 0; i < _buttons.Count; i++)
        {
            Canvas.SetLeft(_buttons[i], _btnX[i].V);
        }
        ApplyBarDim(reveal);
        _ball.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_fillA.V), _accent.R, _accent.G, _accent.B));
        _ball.BorderBrush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(_strokeA.V),
            (byte)Math.Round(_strokeR.V),
            (byte)Math.Round(_strokeG.V),
            (byte)Math.Round(_strokeB.V)));
        Canvas.SetLeft(_writingBar, WritingBarX());
        _writingBar.Height = Math.Max(0, _wbH.V);
        _writingBar.Opacity = _wbO.V;
        _writingBar.IsHitTestVisible = _wbH.V > 1;
        Canvas.SetLeft(_clearBar, ClearBarX());
        _clearBar.Height = Math.Max(0, _clH.V);
        _clearBar.Opacity = _clO.V;
        _clearBar.IsHitTestVisible = _clH.V > 1;
        Canvas.SetLeft(_penBar, WritingBarX());
        _penBar.Height = Math.Max(0, _pnH.V);
        _penBar.Opacity = _pnO.V;
        _penBar.IsHitTestVisible = _pnH.V > 1;
    }

    private double BarReveal => Math.Clamp((_barW.V - HiddenBarW) / (BarW - HiddenBarW), 0, 1);

    private void ApplyBarDim(double reveal)
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var dim = _wbOpen && !ReferenceEquals(_buttons[i], _penBtn) ? 0.35 : 1.0;
            _buttons[i].Opacity = _btnO[i].V * reveal * dim;
        }
    }

    // 左右可用空间决定展开方向
    private ExpandDir PickDirection()
    {
        var b = ScreenBounds();
        var s = RenderScaling;
        var ballLeft = BallLeftPixel();
        var need = (int)((WinW - 2 - BallSize) * s);
        var right = b.Right - (ballLeft.X + (int)Math.Round(BallSize * s));
        var left = ballLeft.X - b.X;
        if (right >= need)
        {
            return ExpandDir.Right;
        }
        if (left >= need)
        {
            return ExpandDir.Left;
        }
        return right >= left ? ExpandDir.Right : ExpandDir.Left;
    }

    private void OnBallPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((DateTime.Now - _lastManip).TotalMilliseconds < DebounceMs)
        {
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _pressed = true;
        _dragCollapsed = false;
        _dragDist = 0;
        _dragAnchorBall = BallLeftPixel();
        _pressScreen = ToScreen(e);
        e.Pointer.Capture(_ball);
    }

    private void OnBallMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }
        var screen = ToScreen(e);
        var dx = screen.X - _pressScreen.X;
        var dy = screen.Y - _pressScreen.Y;
        _dragDist = Math.Sqrt(dx * dx + dy * dy);
        var off = (int)Math.Round(Canvas.GetLeft(_ball) * RenderScaling);
        Position = new PixelPoint(_dragAnchorBall.X - off + dx, _dragAnchorBall.Y + dy);
        ClampToScreen();
        UpdateBallAnchor();
        if (!_dragCollapsed && _expanded && _dragDist > DragThreshold * RenderScaling)
        {
            _dragCollapsed = true;
            _expanded = false;
            _wbRestoreOnExpand = _wbOpen && !_pendingMouseCommit;
            CloseWritingBar();
            SetCollapseTargets();
            _pendingShrink = true;
            StartAnim();
        }
    }

    private void OnBallReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }
        _pressed = false;
        e.Pointer.Capture(null);
        _lastManip = DateTime.Now;
        ClampToScreen();
        if (_dragDist <= DragThreshold * RenderScaling)
        {
            ToggleExpand();
        }
    }

    private void ClampToScreen()
    {
        var b = ScreenBounds();
        var s = RenderScaling;
        var w = (int)Math.Round(Width * s);
        var h = (int)Math.Round(Height * s);
        var x = Math.Clamp(Position.X, b.X + 1, Math.Max(b.X + 1, b.Right - w - 1));
        var y = Math.Clamp(Position.Y, b.Y + 1, Math.Max(b.Y + 1, b.Bottom - h - 1));
        Position = new PixelPoint(x, y);
        UpdateBallAnchor();
    }

    // 单击 展开/收起
    private void ToggleExpand()
    {
        var ballLeft = BallLeftPixel();
        if (!_expanded)
        {
            _dir = PickDirection();
            if (!_pendingShrink)
            {
                SnapExpandedLayout();
            }
            else
            {
                Canvas.SetLeft(_ball, BallX);
            }
            _expanded = true;
            _pendingShrink = false;
            SetExpandTargets();
            Width = WinW;
            var off = _dir == ExpandDir.Left ? BallX : 2;
            Position = new PixelPoint(ballLeft.X - (int)Math.Round(off * RenderScaling), ballLeft.Y);
            StartAnim();
            if (_wbRestoreOnExpand)
            {
                _wbRestoreOnExpand = false;
                OpenWritingBar();
            }
        }
        else
        {
            _expanded = false;
            _wbRestoreOnExpand = _wbOpen && !_pendingMouseCommit;
            CloseWritingBar();
            SetCollapseTargets();
            _pendingShrink = true;
            StartAnim();
        }
    }

    private void StartAnim()
    {
        if (_animating)
        {
            return;
        }
        _animating = true;
        _animElapsed = 0;
        _lastFrame = Stopwatch.GetTimestamp();
        RequestAnimationFrame(OnFrame);
    }

    // 渲染循环
    private void OnFrame(TimeSpan time)
    {
        if (!_animating)
        {
            return;
        }
        var now = Stopwatch.GetTimestamp();
        var dtMs = Math.Min((now - _lastFrame) * 1000.0 / Stopwatch.Frequency, 100);
        _lastFrame = now;
        _animElapsed += dtMs;

        _barX.Step(dtMs);
        _barW.Step(dtMs);
        _barO.Step(dtMs);
        _wbH.Step(dtMs);
        _wbO.Step(dtMs);
        _clH.Step(dtMs);
        _clO.Step(dtMs);
        _pnH.Step(dtMs);
        _pnO.Step(dtMs);
        _fillA.Step(dtMs);
        _strokeR.Step(dtMs);
        _strokeG.Step(dtMs);
        _strokeB.Step(dtMs);
        _strokeA.Step(dtMs);
        foreach (var t in _btnX)
        {
            t.Step(dtMs);
        }
        foreach (var t in _btnO)
        {
            t.Step(dtMs);
        }
        if (_pendingShrink && !_expanded && _animElapsed >= CollapseMaxMs)
        {
            _barX.V = _barX.T;
            _barW.V = _barW.T;
            _barO.V = _barO.T;
            _wbH.V = _wbH.T;
            _wbO.V = _wbO.T;
            _clH.V = _clH.T;
            _clO.V = _clO.T;
            _pnH.V = _pnH.T;
            _pnO.V = _pnO.T;
            _fillA.V = _fillA.T;
            _strokeR.V = _strokeR.T;
            _strokeG.V = _strokeG.T;
            _strokeB.V = _strokeB.T;
            _strokeA.V = _strokeA.T;
            for (var i = 0; i < _btnX.Count; i++)
            {
                _btnX[i].V = _btnX[i].T;
                _btnO[i].V = _btnO[i].T;
            }
        }
        ApplyInterp();

        var done = _barX.Done && _barW.Done && _barO.Done
            && _wbH.Done && _wbO.Done && _clH.Done && _clO.Done
            && _pnH.Done && _pnO.Done
            && _fillA.Done && _strokeR.Done && _strokeG.Done && _strokeB.Done && _strokeA.Done;
        foreach (var t in _btnX)
        {
            done &= t.Done;
        }
        foreach (var t in _btnO)
        {
            done &= t.Done;
        }
        if (!done)
        {
            RequestAnimationFrame(OnFrame);
            return;
        }
        _animating = false;
        if (_wbRetracting)
        {
            _wbRetracting = false;
            _wbOpen = false;
            if (_expanded)
            {
                LockMainBar(false);
            }
            RefreshPadButtons();
            CommitPendingMouse();
            ApplyBarDim(BarReveal);
        }
        UpdateWindowHeight();
        if (_pendingShrink && !_expanded)
        {
            _pendingShrink = false;
            var ballLeft = BallLeftPixel();
            SnapCollapsedLayout();
            Width = CollapsedW;
            Position = new PixelPoint(ballLeft.X - (int)Math.Round(1 * RenderScaling), ballLeft.Y);
            UpdateBallAnchor();
        }
    }
}

internal sealed class BarButton : Border
{
    private readonly Control _icon;
    private readonly TextBlock _label;
    private IBrush _normalFg = Brushes.White;
    private IBrush _selectedBg = Brushes.Transparent;
    private bool _selected;
    private bool _pressed;

    public event Action? Clicked;

    public BarButton(Control icon, string label, double size, double radius, double labelSize, double gap)
    {
        Width = size;
        Height = size;
        CornerRadius = new CornerRadius(radius);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Cursor = new Cursor(StandardCursorType.Hand);
        _icon = icon;
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        _label = new TextBlock
        {
            Text = label,
            FontSize = labelSize,
            FontWeight = FontWeight.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Child = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = gap,
            Children = { icon, _label },
        };
        PointerPressed += (_, e) => _pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => _pressed = false;
        PointerExited += (_, _) => _pressed = false;
    }

    public void SetPalette(IBrush normalFg, IBrush selectedBg)
    {
        _normalFg = normalFg;
        _selectedBg = selectedBg;
        Apply();
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }
            _selected = value;
            Apply();
        }
    }

    private void Apply()
    {
        Background = _selected ? _selectedBg : Brushes.Transparent;
        var fg = _selected ? Brushes.White : _normalFg;
        _label.Foreground = fg;
        FAIcons.SetIconBrush(_icon, fg);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var hit = _pressed;
        _pressed = false;
        if (!hit || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }
        Clicked?.Invoke();
        e.Handled = true;
    }
}
