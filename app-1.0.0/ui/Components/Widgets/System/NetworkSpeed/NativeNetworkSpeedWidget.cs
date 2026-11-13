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

// NativeNetworkSpeedWidget
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
        upGroup.Margin = new Thickness(16, 0, 0, 0);
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
            RowSpacing = WidgetSpace.GapSm,
            Margin = new Thickness(WidgetSpace.Pad, WidgetSpace.PadCompact, WidgetSpace.Pad, WidgetSpace.PadCompact),
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
            Spacing = WidgetSpace.GapSm,
            Children = { dot, label },
        };
        number.VerticalAlignment = VerticalAlignment.Bottom;
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        unit.Margin = new Thickness(0, 0, 0, 2);
        var value = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = WidgetSpace.GapXs,
            Children = { number, unit },
        };
        return new StackPanel
        {
            Spacing = 2,
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
        _title.FontSize = WidgetType.Title;
        _title.FontWeight = WidgetWeight.Title;
        _downLabel.Foreground = sub;
        _upLabel.Foreground = sub;
        _downLabel.FontSize = WidgetType.Label;
        _upLabel.FontSize = WidgetType.Label;
        _downNumber.Foreground = ink;
        _upNumber.Foreground = ink;
        _downNumber.FontSize = 22;
        _upNumber.FontSize = 22;
        _downNumber.FontWeight = WidgetWeight.Display;
        _upNumber.FontWeight = WidgetWeight.Display;
        _downNumber.LetterSpacing = WidgetType.DisplaySpacing(22);
        _upNumber.LetterSpacing = WidgetType.DisplaySpacing(22);
        _downUnit.Foreground = sub;
        _upUnit.Foreground = sub;
        _downUnit.FontSize = WidgetType.Micro;
        _upUnit.FontSize = WidgetType.Micro;
        _totals.Foreground = sub;
        _totals.FontSize = WidgetType.Micro;
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
