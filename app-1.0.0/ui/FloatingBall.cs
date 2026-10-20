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
using Avalonia.Platform.Storage;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using Glimpseon.Core.Win32;

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
        (FASymbol.More, "更多", null),
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
            // 球刚压上去 覆盖层要重新压回来 否则会被盖住
            WritingPadService.RaiseOverlay();
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
    // 收起前各子条的开合状态 展开时一并还原
    private bool _restoreWb, _restoreCl, _restorePn, _restoreVol, _restoreCus;
    private bool _wbRetracting;
    private bool _pendingMouseCommit;

    private const double ClearBarH = 86;
    private const double ClearBarW = 280;
    private const double ClearKnobSize = 52;
    private const double ClearKnobInset = 5;
    private Border _clearBar = null!;
    private Border? _clearKnob;
    private TranslateTransform? _clearShift;
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

    private const double VolBarW = 280;
    private const double VolBarH = ClearBarH;
    private const double VolKnobSize = 44;
    private const double VolKnobInset = 5;
    private Border _volBar = null!;
    private Border? _volKnob;
    private TextBlock? _volText;
    private BarButton? _volBtn;
    private bool _volOpen;
    private bool _volDragging;
    private double _volDragX;
    private long _volSetMs;
    private int _volPct = -1;
    private TranslateTransform? _volShift;
    private readonly Interp _volH = new();
    private readonly Interp _volO = new();

    private static double VolTravel => VolBarW - 34 - VolKnobSize - VolKnobInset * 2;

    // 自定义按钮条 更多键唤出 行尾一枚加号小按钮进编辑窗
    private const double CusBarH = ClearBarH;
    private const double CusPlusSize = 32;   // 其他按钮一半边长 即 1/4 面积
    private Border _cusBar = null!;
    private StackPanel _cusRow = null!;
    private BarButton? _moreBtn;
    private BarButton? _cusPlus;
    private TextBlock? _cusHint;
    private bool _cusOpen;
    private readonly Interp _cusH = new();
    private readonly Interp _cusO = new();

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
            else if (label == "音量")
            {
                _volBtn = btn;
                btn.Clicked += OnVolumeClick;
            }
            else if (label == "更多")
            {
                _moreBtn = btn;
                btn.Clicked += OnMoreClick;
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

        // 音量条
        _volBar = BuildVolBar();
        _volBar.Height = 0;
        _volBar.Opacity = 0;
        _volBar.IsHitTestVisible = false;
        _volBar.ClipToBounds = true;
        _canvas.Children.Add(_volBar);

        // 自定义按钮条 更多键唤出
        _cusBar = BuildCusBar();
        _cusBar.Height = 0;
        _cusBar.Opacity = 0;
        _cusBar.IsHitTestVisible = false;
        _cusBar.ClipToBounds = true;
        Canvas.SetLeft(_cusBar, WritingBarX());
        Canvas.SetTop(_cusBar, CanvasH + WritingBarGap + WritingBarH + WritingBarGap);
        _canvas.Children.Add(_cusBar);
        CustomActions.Changed += OnCustomActionsChanged;
        Closed += (_, _) => CustomActions.Changed -= OnCustomActionsChanged;
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
        _clearShift = new TranslateTransform();
        _clearKnob.RenderTransform = _clearShift;
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

    // 音量条
    private Border BuildVolBar()
    {
        _volKnob = new Border
        {
            Width = VolKnobSize,
            Height = VolKnobSize,
            CornerRadius = new CornerRadius(VolKnobSize / 2),
            Background = ClearKnobBrush(),
            BoxShadow = BoxShadows.Parse("0 2 8 0 #33000000"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(VolKnobInset, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = VolKnobIcon(),
        };
        _volShift = new TranslateTransform();
        _volKnob.RenderTransform = _volShift;
        _volKnob.PointerPressed += OnVolKnobPressed;
        _volKnob.PointerMoved += OnVolKnobMoved;
        _volKnob.PointerReleased += (_, _) => EndVolDrag();
        _volKnob.PointerCaptureLost += (_, _) => EndVolDrag();

        _volText = new TextBlock
        {
            Text = "音量",
            FontSize = 14,
            FontWeight = FontWeight.Medium,
            Foreground = ClearTextBrush(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(VolKnobSize + 16, 0, 18, 0),
            IsHitTestVisible = false,
        };
        var track = new Border
        {
            CornerRadius = new CornerRadius((VolBarH - 20 - 2) / 2),
            Background = ClearTrackBrush(),
            BorderBrush = ClearTrackBorderBrush(),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(16, 10, 16, 10),
            Child = new Panel { Children = { _volText, _volKnob } },
        };
        var bar = MakeBar(VolBarW);
        bar.Child = track;
        return bar;
    }

    // 滑钮图标
    private Control VolKnobIcon()
    {
        var icon = FAIcons.Icon(FASymbol.Volume, 20);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.IsHitTestVisible = false;
        FAIcons.SetIconBrush(icon, new SolidColorBrush(Color.Parse(DarkMode ? "#E0E0E0" : "#5A5A5A")));
        return icon;
    }

    // 音量条开关
    private void OnVolumeClick()
    {
        if (_volOpen)
        {
            CloseVolumeBar();
        }
        else
        {
            OpenVolumeBar();
        }
    }

    private void OpenVolumeBar()
    {
        if (_volOpen)
        {
            return;
        }
        _volOpen = true;
        RefreshVolume();
        _volH.Set(VolBarH, 3);
        _volO.Set(1, 3);
        UpdateWindowHeight();
        StartAnim();
        SetChecked(_volBtn, true);
        SyncMainBarLock();
    }

    private void CloseVolumeBar()
    {
        if (!_volOpen)
        {
            return;
        }
        _volOpen = false;
        _volDragging = false;
        _volH.Set(0, 5);
        _volO.Set(0, 5);
        UpdateWindowHeight();
        StartAnim();
        SetChecked(_volBtn, false);
        SyncMainBarLock();
    }

    private void RefreshVolume() =>
        SetVolKnob(VolTravel * Math.Clamp(Audio.Volume, 0, 1));

    private void SetVolKnob(double travel)
    {
        if (_volKnob is null)
        {
            return;
        }
        var t = Math.Clamp(travel, 0, VolTravel);
        if (_volShift is not null)
        {
            _volShift.X = t;
        }
        var pct = (int)Math.Round(t / VolTravel * 100);
        if (_volText is not null && pct != _volPct)
        {
            _volPct = pct;
            _volText.Text = $"音量 {pct}%";
        }
    }

    private void OnVolKnobPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_volKnob).Properties.IsLeftButtonPressed)
        {
            return;
        }
        _volDragging = true;
        _volDragX = e.GetPosition(_volBar).X - (_volShift?.X ?? 0);
        e.Pointer.Capture(_volKnob);
        e.Handled = true;
    }

    private void OnVolKnobMoved(object? sender, PointerEventArgs e)
    {
        if (!_volDragging || !e.GetCurrentPoint(_volKnob).Properties.IsLeftButtonPressed)
        {
            return;
        }
        var travel = Math.Clamp(e.GetPosition(_volBar).X - _volDragX, 0, VolTravel);
        SetVolKnob(travel);
        PushVolume(travel);
    }

    private void EndVolDrag()
    {
        if (!_volDragging)
        {
            return;
        }
        _volDragging = false;
        PushVolume(_volShift?.X ?? 0, force: true);
    }

    private void PushVolume(double travel, bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && now - _volSetMs < 40)
        {
            return;
        }
        _volSetMs = now;
        Audio.SetVolume(travel / VolTravel);
    }

    private double VolBarX()
    {
        if (_volBtn?.TranslatePoint(new Point(_volBtn.Bounds.Width / 2, 0), _canvas) is { } center)
        {
            return Math.Clamp(center.X - VolBarW / 2, 2, Math.Max(2, WinW - 2 - VolBarW));
        }
        return WritingBarX();
    }

    private double VolBarTop()
    {
        var top = CanvasH + WritingBarGap;
        if (_wbOpen || _wbH.V > 1)
        {
            top += WritingBarH + WritingBarGap;
        }
        if (_clOpen || _clH.V > 1 || _pnOpen || _pnH.V > 1)
        {
            top += ClearBarH + WritingBarGap;
        }
        return top;
    }

    // 自定义按钮条
    private Border BuildCusBar()
    {
        _cusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            Margin = new Thickness(10, 9, 10, 9),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _cusPlus = MakeBarButton(FAIcons.Icon(FASymbol.Add, 16), "", CusPlusSize, 10, 12);
        _cusPlus.Margin = new Thickness(0, 0, 10, 0);
        _cusPlus.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(_cusPlus, AppUtils.Tr("custom_action.title"));
        _cusPlus.Clicked += OpenCustomEditor;

        _cusHint = new TextBlock
        {
            Text = AppUtils.Tr("custom_action.empty_row"),
            FontSize = 14,
            FontWeight = FontWeight.Medium,
            Foreground = ClearTextBrush(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            IsVisible = false,
        };

        var host = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        host.Children.Add(new Panel { Children = { _cusRow, _cusHint } });
        Grid.SetColumn(_cusPlus, 1);
        host.Children.Add(_cusPlus);

        var bar = MakeBar(WritingBarW);
        bar.Child = host;
        return bar;
    }

    // 只重建自定义键
    private void RebuildCusRow()
    {
        if (_cusRow is null)
        {
            return;
        }
        _cusRow.Children.Clear();
        foreach (var item in CustomActions.Items.Take(CustomActions.MaxItems))
        {
            var action = item;
            var btn = MakeBarButton(FAIcons.IconByName(action.Icon, WbIconSize), action.Label, WbBtnSize, WbBtnRadius, WbLabelSize);
            btn.Clicked += () =>
            {
                CloseCusBar();
                CustomActions.Run(action);
            };
            _cusRow.Children.Add(btn);
        }
        if (_cusHint is not null)
        {
            _cusHint.IsVisible = _cusRow.Children.Count == 0;
        }
    }

    private void OnCustomActionsChanged() => RebuildCusRow();

    // 更多键开关
    private void OnMoreClick()
    {
        if (_cusOpen)
        {
            CloseCusBar();
        }
        else
        {
            OpenCusBar();
        }
    }

    private void OpenCusBar()
    {
        if (_cusOpen)
        {
            return;
        }
        RebuildCusRow();
        _cusOpen = true;
        _cusH.Set(CusBarH, 3);
        _cusO.Set(1, 3);
        UpdateWindowHeight();
        StartAnim();
        SetChecked(_moreBtn, true);
        SyncMainBarLock();
    }

    private void CloseCusBar()
    {
        if (!_cusOpen)
        {
            return;
        }
        _cusOpen = false;
        _cusH.Set(0, 5);
        _cusO.Set(0, 5);
        UpdateWindowHeight();
        StartAnim();
        SetChecked(_moreBtn, false);
        SyncMainBarLock();
    }

    // 自定义条紧跟音量条
    private double CusBarTop()
    {
        var top = VolBarTop();
        if (_volOpen || _volH.V > 1)
        {
            top += VolBarH + WritingBarGap;
        }
        return top;
    }

    // 编辑窗
    private void OpenCustomEditor()
    {
        var window = new CustomActionWindow();
        window.Show();
        window.Activate();
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
        _restoreWb = _restoreCl = _restorePn = _restoreVol = _restoreCus = false;
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
        SyncMainBarLock();
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

    // 子条开合后同步顶层键 灰化+锁点击
    private void SyncMainBarLock()
    {
        LockMainBar(_wbOpen || _cusOpen || _volOpen);
        ApplyBarDim(BarReveal);
    }

    // 当前铺开子条对应的顶层键
    private BarButton? FocusButton =>
        _wbOpen ? _penBtn : _cusOpen ? _moreBtn : _volOpen ? _volBtn : null;

    // 灰化其他按键
    private void LockMainBar(bool locked)
    {
        var focus = FocusButton;
        foreach (var btn in _buttons)
        {
            btn.IsHitTestVisible = !locked || ReferenceEquals(btn, focus);
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
        if (_volOpen || _volH.V > 1)
        {
            h += WritingBarGap + VolBarH;
        }
        if (_cusOpen || _cusH.V > 1)
        {
            h += WritingBarGap + CusBarH;
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
        _clearDragX = e.GetPosition(_clearBar).X - (_clearShift?.X ?? 0);
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
        var current = _clearShift?.X ?? 0;
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
        if (_clearShift is not null)
        {
            _clearShift.X = travel;
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
        SyncMainBarLock();
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
        Canvas.SetLeft(_volBar, VolBarX());
        Canvas.SetTop(_volBar, VolBarTop());
        _volBar.Height = Math.Max(0, _volH.V);
        _volBar.Opacity = _volO.V;
        _volBar.IsHitTestVisible = _volH.V > 1;
        // 自定义按钮条 跟在音量条后面
        Canvas.SetLeft(_cusBar, WritingBarX());
        Canvas.SetTop(_cusBar, CusBarTop());
        _cusBar.Height = Math.Max(0, _cusH.V);
        _cusBar.Opacity = _cusO.V;
        _cusBar.IsHitTestVisible = _cusH.V > 1;
    }

    private double BarReveal => Math.Clamp((_barW.V - HiddenBarW) / (BarW - HiddenBarW), 0, 1);

    private void ApplyBarDim(double reveal)
    {
        var focus = FocusButton;
        for (var i = 0; i < _buttons.Count; i++)
        {
            var dim = focus is not null && !ReferenceEquals(_buttons[i], focus) ? 0.35 : 1.0;
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
            RememberOpenRows();
            CloseWritingBar();
            CloseVolumeBar();
            CloseCusBar();
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
    // 收起前记下子条状态
    private void RememberOpenRows()
    {
        var keep = !_pendingMouseCommit;
        _restoreWb = _wbOpen && keep;
        _restoreCl = _clOpen && keep;
        _restorePn = _pnOpen && keep;
        _restoreVol = _volOpen;
        _restoreCus = _cusOpen;
    }

    // 展开时还原 
    private void RestoreOpenRows()
    {
        if (_restoreWb)
        {
            OpenWritingBar();
            if (_restoreCl)
            {
                OpenClearBar();
            }
            else if (_restorePn)
            {
                OpenPenBar();
            }
        }
        if (_restoreVol)
        {
            OpenVolumeBar();
        }
        if (_restoreCus)
        {
            OpenCusBar();
        }
        _restoreWb = _restoreCl = _restorePn = _restoreVol = _restoreCus = false;
    }

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
            RestoreOpenRows();
        }
        else
        {
            _expanded = false;
            RememberOpenRows();
            CloseWritingBar();
            CloseVolumeBar();
            CloseCusBar();
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
        _volH.Step(dtMs);
        _volO.Step(dtMs);
        _cusH.Step(dtMs);
        _cusO.Step(dtMs);
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
            _volH.V = _volH.T;
            _volO.V = _volO.T;
            _cusH.V = _cusH.T;
            _cusO.V = _cusO.T;
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
            && _pnH.Done && _pnO.Done && _volH.Done && _volO.Done && _cusH.Done && _cusO.Done
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
        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = gap,
        };
        panel.Children.Add(icon);
        // 空标签的按钮只显示图标(自定义条的加号)
        if (!string.IsNullOrEmpty(label))
        {
            panel.Children.Add(_label);
        }
        Child = panel;
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


// 工具模式
internal enum WritingToolMode
{
    Mouse = 0,
    Pen = 1,
    Eraser = 2,
}

// 笔型
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

internal static class WritingPadService
{
    private static WritingPadOverlay? _overlay;
    public static event Action? StateChanged;
    public static event Action? CloseRequested;

    public static bool OverlayVisible => _overlay is { IsVisible: true };
    public static WritingToolMode Mode => _overlay?.Mode ?? WritingToolMode.Mouse;
    public static bool Whiteboard => _overlay?.Whiteboard ?? false;
    public static bool Freeze => _overlay?.FreezeOn ?? false;

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

    public static void RaiseOverlay() => _overlay?.RaiseAboveAll();

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
    private readonly DispatcherTimer _topGuard = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _captureExcluded;

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
        _topGuard.Tick += OnTopGuardTick;
        Closed += (_, _) =>
        {
            _topGuard.Stop();
            ApplyCaptureExclusion(false);
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
        ApplyCaptureExclusion(true);
        RaiseAboveAll();
        _topGuard.Start();
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

    private void ApplyCaptureExclusion(bool on)
    {
        if (_captureExcluded == on)
        {
            return;
        }
        _captureExcluded = on;
        Native.SetCaptureExclusion(TryGetPlatformHandle()?.Handle ?? nint.Zero, on);
    }

    private void OnTopGuardTick(object? sender, EventArgs e) => RaiseAboveAll();

    public void RaiseAboveAll()
    {
        Native.KeepTopmost(TryGetPlatformHandle()?.Handle ?? nint.Zero);
        Native.KeepTopmost(WritingPadService.BallHandleProvider?.Invoke() ?? nint.Zero);
    }

    private void RefreshFreezeBg()
    {
        if (!FreezeOn || Whiteboard || _unionPx == default)
        {
            SyncFreezeVisibility();
            return;
        }
        if (CaptureScreen(_unionPx) is { } capture)
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
    }

    private static (byte[] Pixels, PixelSize Size, int Stride)? CaptureScreen(PixelRect bounds)
    {
        try
        {
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
    }

    public void HideOverlay()
    {
        _topGuard.Stop();
        ApplyCaptureExclusion(false);
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
            // 补帧间被合并的点
            foreach (var p in e.GetIntermediatePoints(this))
            {
                stroke.AddPoint(p.Position);
            }
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


public sealed class CustomActionWindow : GlimpseonWindow
{
    // 这是dsf老师不知道什么意味写的。。 下次一定改
    private static readonly string[] Palette =
    [
        "Home", "Edit", "Volume", "Folder", "Download", "Clock", "Settings", "Search",
        "Play", "Save", "Undo", "Redo", "Copy", "Delete", "Globe", "View",
        "SlideShow", "AllApps", "Code", "People", "Page", "Ruler", "Switch", "Target",
        "WeatherSunny", "WeatherMoon", "Zoom", "Refresh", "Shop", "Sync", "Video", "TwoBars",
        "Scan", "World", "SaveAs", "SaveLocal", "ViewAll", "RotateClockwise", "ReportHacked", "Dismiss",
    ];

    private readonly List<CustomAction> _items;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private bool _saved;
    private Button? _addButton;

    public CustomActionWindow()
    {
        Title = AppUtils.Tr("custom_action.title");
        Width = 860;
        Height = 620;
        MinWidth = 720;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _items = CustomActions.Items
            .Select(x => new CustomAction { Icon = x.Icon, Label = x.Label, Kind = x.Kind, Target = x.Target })
            .ToList();
        Content = BuildRoot();
        Rebuild();
        Closing += (_, _) =>
        {
            if (!_saved)
            {
                Save();
            }
        };
    }

    private Control BuildRoot()
    {
        var hint = new TextBlock
        {
            Text = AppUtils.Tr("custom_action.hint"),
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 14),
        };

        var add = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    FAIcons.Icon(FASymbol.Add, 14),
                    new TextBlock { Text = AppUtils.Tr("custom_action.add"), VerticalAlignment = VerticalAlignment.Center },
                },
            },
            Padding = new Thickness(14, 6, 16, 6),
        };
        add.Click += (_, _) =>
        {
            if (_items.Count >= CustomActions.MaxItems)
            {
                return;
            }
            _items.Add(new CustomAction { Icon = "Edit", Label = "", Kind = CustomActionKind.App, Target = "" });
            Rebuild();
            if (_rows.Children.Count > 0)
            {
                _rows.Children[^1].Focus();
            }
        };
        ToolTip.SetTip(add, AppUtils.Tr("custom_action.max_tip"));

        var done = new Button
        {
            Content = AppUtils.Tr("custom_action.done"),
            Padding = new Thickness(22, 6, 22, 6),
            Background = new SolidColorBrush(Common.ParseAccentColor(Config.ThemeColor.Value)),
            Foreground = Brushes.White,
        };
        done.Click += (_, _) =>
        {
            Save();
            Close();
        };

        _addButton = add;

        var footer = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
            Margin = new Thickness(0, 14, 0, 0),
        };
        footer.Children.Add(add);
        Grid.SetColumn(done, 2);
        footer.Children.Add(done);

        var scroll = new ScrollViewer
        {
            Content = _rows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(hint);
        root.Children.Add(footer);
        root.Children.Add(scroll);
        return root;
    }

    private void Save()
    {
        _saved = true;
        CustomActions.Save(_items.Where(x => !string.IsNullOrWhiteSpace(x.Label) || !string.IsNullOrWhiteSpace(x.Target)).ToList());
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        if (_items.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = AppUtils.Tr("custom_action.empty"),
                FontSize = 13,
                Opacity = 0.6,
                Margin = new Thickness(4, 12, 4, 0),
            });
            return;
        }
        foreach (var item in _items)
        {
            _rows.Children.Add(BuildRow(item));
        }
        if (_addButton is not null)
        {
            _addButton.IsEnabled = _items.Count < CustomActions.MaxItems;
        }
    }

    private Control BuildRow(CustomAction item)
    {
        var iconButton = new Button
        {
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Gray, 0.28),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = FAIcons.IconByName(item.Icon, 18),
        };
        var flyout = BuildIconFlyout(item, iconButton);
        FlyoutBase.SetAttachedFlyout(iconButton, flyout);
        iconButton.Click += (_, _) => FlyoutBase.ShowAttachedFlyout(iconButton);

        var name = new TextBox
        {
            Text = item.Label,
            Width = 150,
            PlaceholderText = AppUtils.Tr("custom_action.name_hint"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        name.TextChanged += (_, _) => item.Label = name.Text ?? "";

        var kinds = new[]
        {
            AppUtils.Tr("custom_action.kind_app"),
            AppUtils.Tr("custom_action.kind_url"),
            AppUtils.Tr("custom_action.kind_builtin"),
        };
        var kind = new ComboBox
        {
            ItemsSource = kinds,
            SelectedIndex = (int)item.Kind,
            Width = 132,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var target = new TextBox
        {
            Text = item.Target,
            PlaceholderText = AppUtils.Tr("custom_action.target_hint"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        target.TextChanged += (_, _) => item.Target = target.Text ?? "";

        var browse = new Button
        {
            Content = AppUtils.Tr("custom_action.browse"),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 4, 12, 4),
        };
        browse.Click += async (_, _) =>
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null)
            {
                return;
            }
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = AppUtils.Tr("custom_action.browse"),
                AllowMultiple = false,
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            {
                target.Text = path;
            }
        };

        var builtins = new ComboBox
        {
            ItemsSource = CustomActions.Builtins.Select(x => AppUtils.Tr(x.Key)).ToList(),
            Width = 180,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var index = Array.FindIndex(CustomActions.Builtins, x => x.Value == item.Target);
        builtins.SelectedIndex = index < 0 ? 0 : index;
        builtins.SelectionChanged += (_, _) =>
        {
            if (builtins.SelectedIndex >= 0)
            {
                item.Target = CustomActions.Builtins[builtins.SelectedIndex].Value;
            }
        };

        var targetCell = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        var fileCell = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), ColumnSpacing = 6 };
        fileCell.Children.Add(target);
        Grid.SetColumn(browse, 1);
        fileCell.Children.Add(browse);

        var builtinCell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { builtins },
        };

        void Sync()
        {
            var isBuiltin = item.Kind == CustomActionKind.Builtin;
            fileCell.IsVisible = !isBuiltin;
            builtinCell.IsVisible = isBuiltin;
            browse.IsVisible = item.Kind == CustomActionKind.App;
            if (isBuiltin && string.IsNullOrWhiteSpace(item.Target))
            {
                item.Target = CustomActions.Builtins[0].Value;
            }
        }

        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex < 0)
            {
                return;
            }
            item.Kind = (CustomActionKind)kind.SelectedIndex;
            Sync();
        };

        fileCell.Children.Add(builtinCell);
        targetCell.Children.Add(fileCell);
        Sync();

        var remove = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = FAIcons.Icon(FASymbol.Delete, 15),
        };
        ToolTip.SetTip(remove, AppUtils.Tr("custom_action.delete"));
        remove.Click += (_, _) =>
        {
            _items.Remove(item);
            Rebuild();
        };

        var row = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,Auto,*,Auto"),
            ColumnSpacing = 8,
        };
        row.Children.Add(iconButton);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        Grid.SetColumn(kind, 2);
        row.Children.Add(kind);
        Grid.SetColumn(targetCell, 3);
        row.Children.Add(targetCell);
        Grid.SetColumn(remove, 4);
        row.Children.Add(remove);

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Gray, 0.22),
            Background = new SolidColorBrush(Colors.Gray, 0.06),
            Padding = new Thickness(10, 8, 10, 8),
            Child = row,
        };
    }

    // 图标选择浮层
    private Flyout BuildIconFlyout(CustomAction item, Button target)
    {
        var flyout = new Flyout { Placement = PlacementMode.Bottom };
        var grid = new UniformGrid { Columns = 8, Rows = 5 };
        foreach (var icon in Palette)
        {
            var choice = new Button
            {
                Width = 34,
                Height = 34,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = FAIcons.IconByName(icon, 16),
            };
            ToolTip.SetTip(choice, icon);
            choice.Click += (_, _) =>
            {
                item.Icon = icon;
                target.Content = FAIcons.IconByName(icon, 18);
                flyout.Hide();
            };
            grid.Children.Add(choice);
        }
        flyout.Content = grid;
        return flyout;
    }
}
