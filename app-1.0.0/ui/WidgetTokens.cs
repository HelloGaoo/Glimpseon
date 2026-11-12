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

// 组件设计令牌 字号 间距 字色 字体集合 单一出口
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Glimpseon.UI;

/// <summary>HarmonyOS Sans 六档字重集合 按 Weight 精确命中 Thin/Light/Regular/Medium/Bold/Black</summary>
public sealed class HarmonyFontCollection : EmbeddedFontCollection
{
    public HarmonyFontCollection() : base(
        new Uri("fonts:HarmonyOS", UriKind.Absolute),
        new Uri("avares://GlimpseonMain/Assets/font/HarmonyOS_Sans", UriKind.Absolute))
    {
    }
}

/// <summary>Bebas Neue 表盘数字集合 只用于大号时钟数字</summary>
public sealed class ClockFontCollection : EmbeddedFontCollection
{
    public ClockFontCollection() : base(
        new Uri("fonts:Clock", UriKind.Absolute),
        new Uri("avares://GlimpseonMain/Assets/font/clock", UriKind.Absolute))
    {
    }
}

/// <summary>全组件字族 唯一来源</summary>
public static class WidgetFonts
{
    /// <summary>正文字族 自带中文回退</summary>
    public static readonly FontFamily Ui =
        new("fonts:HarmonyOS#HarmonyOS Sans, Microsoft YaHei UI, Microsoft YaHei, Segoe UI");

    /// <summary>数字字族 与正文同族 保留独立入口方便换等宽字体</summary>
    public static readonly FontFamily Num = Ui;

    /// <summary>表盘大数字 窄体 仅用于方形钟表</summary>
    public static readonly FontFamily Display =
        new("fonts:Clock#Bebas Neue, fonts:HarmonyOS#HarmonyOS Sans");
}

/// <summary>字号档位 一张卡最多三档</summary>
public static class WidgetType
{
    public const double Title = 13;
    public const double Body = 13;
    public const double Label = 12;
    public const double Micro = 10.5;
    public const double BodyLineHeight = 1.4;

    /// <summary>大数字按可用宽高双约束取字号 保留 8% 光学余量</summary>
    public static double Display(double width, double height, double emWidth = 2.6, double min = 12, double max = 240)
    {
        var byHeight = height * 0.92;
        var byWidth = width > 1 ? width / Math.Max(0.5, emWidth) : byHeight;
        return Math.Clamp(Math.Round(Math.Min(byHeight, byWidth), 1), min, max);
    }
}

/// <summary>间距档位 只允许 4 / 6 / 8 / 16</summary>
public static class WidgetSpace
{
    public const double Pad = 16;
    public const double PadCompact = 12;
    public const double Gap = 8;
    public const double GapSm = 6;
    public const double GapXs = 4;
}

/// <summary>字色三档 同源派生 不各自硬编码透明度</summary>
public static class WidgetInk
{
    public const double MutedOpacity = 0.62;
    public const double FaintOpacity = 0.30;

    public static SolidColorBrush Fg(Color ink) => new(ink);

    public static SolidColorBrush Muted(Color ink) => new(ink, MutedOpacity);

    public static SolidColorBrush Faint(Color ink) => new(ink, FaintOpacity);
}
