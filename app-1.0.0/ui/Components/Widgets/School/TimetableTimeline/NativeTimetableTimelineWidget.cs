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

// NativeTimetableTimelineWidget
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
                    var nameFs = WidgetType.Headline * nameScale;
                    var nameFt = Ft(nameText, nameFs, WidgetWeight.Title,
                        new SolidColorBrush(isPast ? inkF : isCurrent ? Colors.White : inkF));
                    // 截断阈值按未缩放宽度判定(CSS max-width:118 作用于 scale 之前)
                    if (nameFt.WidthIncludingTrailingWhitespace / nameScale > NameMaxW - 30)
                    {
                        var trimmed = nameText.Length > 6 ? nameText[..5] + "…" : nameText;
                        nameFt = Ft(trimmed, nameFs, WidgetWeight.Title,
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
                    var timeFt = Ft(timeText, WidgetType.Label, WidgetWeight.Title,
                        new SolidColorBrush(isCurrent || isDoneNode && false ? inkF : isPast ? inkF : sub));
                    if (isCurrent)
                    {
                        timeFt = Ft(timeText, WidgetType.Label, WidgetWeight.Title, new SolidColorBrush(inkF));
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
                    var periodFt = Ft(pd, WidgetType.Body, WidgetWeight.Title,
                        new SolidColorBrush(isCurrent || isDoneNode ? inkF : isPast ? inkF : sub));
                    ctx.DrawText(periodFt, new Point(x - periodFt.WidthIncludingTrailingWhitespace / 2, y0 + 16));
                }
            }
        }
    }
}
