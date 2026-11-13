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

// DigitalClockWidget
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

// 数字时钟
// 400x200 日期左上 主时间左对齐 秒数降档共基线 24 格时间轴 ClockColor
// 断点 高<120 隐时间轴 高<170 隐刻度文字 高<96 隐日期; 整秒对齐 切页停表

public sealed class DigitalClockWidget : WidgetCardBase
{
    private const double PadX = WidgetSpace.Pad, PadY = WidgetSpace.Pad;
    // 主时间按可用框拟合 秒数降档共基线
    private const double EmHm = 2.52, SecScale = 0.36;

    private readonly TextBlock _timeText = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Left,
        FontWeight = WidgetWeight.Display,
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
            Margin = new Thickness(PadX, PadY, PadX, PadY),
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
        var h = Bounds.Height - PadY * 2;
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
        _timeText.LetterSpacing = WidgetType.DisplaySpacing(size);

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

    private double Measure(string text, double size, FontWeight weight = WidgetWeight.Display) =>
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
