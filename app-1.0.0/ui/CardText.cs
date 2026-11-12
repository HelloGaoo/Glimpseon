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

// 卡片排字 真实字形测量 + 按可用框收敛字号 免逐组件硬编码
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Glimpseon.Core;

namespace Glimpseon.UI;

internal static class CardText
{
    /// <summary>单行字宽(px) 失败回退经验值</summary>
    public static double Width(string? text, FontFamily family, FontWeight weight, double size, FontStyle style = FontStyle.Normal)
    {
        if (string.IsNullOrEmpty(text) || size <= 0)
        {
            return 0;
        }
        try
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(family, style, weight), size, Brushes.White);
            return ft.Width;
        }
        catch (Exception e)
        {
            Log.Debug($"[CARD] 测量失败 回退估算: {e.Message}");
            return text.Length * size * 0.62;
        }
    }

    /// <summary>超宽按比例回缩 返回钳制后字号</summary>
    public static double Fit(string? text, FontFamily family, FontWeight weight, double maxWidth, double prefer,
        double min = 10, double max = 240)
    {
        if (maxWidth <= 1)
        {
            return Math.Clamp(prefer, min, max);
        }
        var width = Width(text, family, weight, prefer);
        var size = width > maxWidth ? prefer * maxWidth / width : prefer;
        return Math.Clamp(Math.Round(size, 1), min, max);
    }

    /// <summary>按高度取一档 行高换算字面高</summary>
    public static double ByHeight(double height, double ratio, double min, double max) =>
        Math.Clamp(Math.Round(height * ratio, 1), min, max);
}
