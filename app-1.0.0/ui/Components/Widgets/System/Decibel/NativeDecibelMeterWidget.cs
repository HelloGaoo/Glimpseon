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

// NativeDecibelMeterWidget
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
        var curGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WidgetSpace.GapXs, VerticalAlignment = VerticalAlignment.Center };
        curGroup.Children.Add(_curLabel);
        curGroup.Children.Add(_curValue);
        var peakGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WidgetSpace.GapXs, VerticalAlignment = VerticalAlignment.Center };
        peakGroup.Children.Add(_peakLabel);
        peakGroup.Children.Add(_peakValue);

        var topbar = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*,Auto,Auto"),
            Margin = new Thickness(0, WidgetSpace.GapXs, 0, WidgetSpace.Gap),
        };
        Grid.SetColumn(_title, 0);
        _title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(curGroup, 2);
        Grid.SetColumn(peakGroup, 3);
        peakGroup.Margin = new Thickness(12, 0, 0, 0);
        topbar.Children.Add(_title);
        topbar.Children.Add(curGroup);
        topbar.Children.Add(peakGroup);

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
            Margin = new Thickness(WidgetSpace.PadCompact, WidgetSpace.GapXs, WidgetSpace.PadCompact, WidgetSpace.PadCompact),
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
        _title.FontSize = Math.Round(WidgetType.Title * k, 1);
        _title.FontWeight = WidgetWeight.Title;
        _curLabel.FontSize = Math.Round(WidgetType.Label * k, 1);
        _peakLabel.FontSize = Math.Round(WidgetType.Label * k, 1);
        _curValue.FontSize = Math.Round(WidgetType.Headline * k, 1);
        _curValue.FontWeight = WidgetWeight.Display;
        _peakValue.FontSize = Math.Round(WidgetType.Headline * k, 1);
        _peakValue.FontWeight = WidgetWeight.Display;
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
            var smallTypeface = new Typeface(Common.AppFontFamily);
            for (var dbv = Floor; dbv <= TopDb - 20; dbv += 20)
            {
                var ft = new FormattedText(((int)dbv).ToString(), System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, smallTypeface, WidgetType.Micro, subBrush);
                ctx.DrawText(ft, new Point(PL + 4, YOf(dbv) - 3 - ft.Height));
            }
            var dbLabel = new FormattedText("dB", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, smallTypeface, WidgetType.Micro, subBrush);
            ctx.DrawText(dbLabel, new Point(2, 14 - dbLabel.Height));

            // 纵网格 每 20s + 底部标签 -20s/-40s + 0s
            for (var i = 20; i < MaxPts; i += 20)
            {
                var x = Math.Round(PL + pw - (i + 0.5) * slotW) + 0.5;
                ctx.DrawLine(gridPen, new Point(x, PT), new Point(x, PT + ph));
                var ft = new FormattedText("-" + i + "s", System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, smallTypeface, WidgetType.Micro, subBrush);
                ctx.DrawText(ft, new Point(PL + pw - (i + 0.5) * slotW - ft.WidthIncludingTrailingWhitespace / 2, PT + ph + 3));
            }
            var zeroLabel = new FormattedText("0s", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, smallTypeface, WidgetType.Micro, subBrush);
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
