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

// 组件设计令牌 字号 字重 间距 字色 字体集合 单一出口
// 规范对齐 FullWidget sdk/base.css: 14 基准 / 1.4 行高 / 12 次要 / 10.5 微字
// 大数字统一 Light + 负字距 正文统一 Regular 标题统一 SemiBold
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

/// <summary>字号五档 一张卡最多用到其中三档</summary>
public static class WidgetType
{
    /// <summary>内容头条 新闻标题 / 课表科目 / 单词例句</summary>
    public const double Headline = 16;

    /// <summary>卡片标题 / 主数值</summary>
    public const double Title = 14;

    /// <summary>正文 / 常规数值</summary>
    public const double Body = 13;

    /// <summary>次要说明 / 元信息</summary>
    public const double Label = 12;

    /// <summary>脚注 / 坐标 / 轴刻度</summary>
    public const double Micro = 10.5;

    /// <summary>正文行高倍率</summary>
    public const double BodyLineHeight = 1.4;

    /// <summary>密集行高倍率 列表 / 课表</summary>
    public const double TightLineHeight = 1.3;

    /// <summary>大数字统一字距 负 0.02em</summary>
    public const double DisplayTracking = -0.02;

    /// <summary>大数字按可用宽高双约束取字号 保留 8% 光学余量</summary>
    public static double Display(double width, double height, double emWidth = 2.6, double min = 12, double max = 240)
    {
        var byHeight = height * 0.92;
        var byWidth = width > 1 ? width / Math.Max(0.5, emWidth) : byHeight;
        return Math.Clamp(Math.Round(Math.Min(byHeight, byWidth), 1), min, max);
    }

    /// <summary>按字号取行高 行高不是字号的整数倍时补 0.5 的整数档 保证多行文本节奏一致</summary>
    public static double LineHeight(double size, double ratio = BodyLineHeight) =>
        Math.Round(size * ratio * 2, MidpointRounding.AwayFromZero) / 2;

    /// <summary>大数字字距 按字号折算 负 0.02em</summary>
    public static double DisplaySpacing(double size) => Math.Round(size * DisplayTracking, 1);
}

/// <summary>字重四档 全部组件统一</summary>
public static class WidgetWeight
{
    /// <summary>大数字 / 表盘</summary>
    public const FontWeight Display = FontWeight.Light;

    /// <summary>标题 / 强调</summary>
    public const FontWeight Title = FontWeight.SemiBold;

    /// <summary>次级强调 / 控件文字</summary>
    public const FontWeight Emphasis = FontWeight.Medium;

    /// <summary>正文</summary>
    public const FontWeight Body = FontWeight.Normal;
}

/// <summary>间距档位 只允许 4 / 6 / 8 / 12 / 16</summary>
public static class WidgetSpace
{
    /// <summary>常规卡片内边距</summary>
    public const double Pad = 16;

    /// <summary>紧凑卡片内边距</summary>
    public const double PadCompact = 12;

    /// <summary>区块间距</summary>
    public const double Gap = 8;

    /// <summary>行间距</summary>
    public const double GapSm = 6;

    /// <summary>行内间距</summary>
    public const double GapXs = 4;

    /// <summary>卡片圆角 与外观默认值一致</summary>
    public const double Radius = 16;

    /// <summary>卡内控件圆角</summary>
    public const double RadiusInner = 8;

    /// <summary>胶囊圆角</summary>
    public const double RadiusPill = 999;
}

/// <summary>字色三档 同源派生 不各自硬编码透明度</summary>
public static class WidgetInk
{
    /// <summary>次要文字</summary>
    public const double MutedOpacity = 0.62;

    /// <summary>脚注 / 轴刻度</summary>
    public const double FaintOpacity = 0.30;

    /// <summary>已过 / 禁用态</summary>
    public const double DimOpacity = 0.45;

    public static SolidColorBrush Fg(Color ink) => new(ink);

    public static SolidColorBrush Muted(Color ink) => new(ink, MutedOpacity);

    public static SolidColorBrush Faint(Color ink) => new(ink, FaintOpacity);
}
