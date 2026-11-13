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

// CalendarDayCell
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

/// <summary>月历单格</summary>
public sealed class CalendarDayCell : Control
{
    // 归属卡片明暗 由宿主注入
    public Func<bool>? DarkProvider { get; set; }
    private int _day;
    private string _sub = "";
    private bool _isCurrentMonth = true;
    private bool _isToday;
    private bool _isWeekend;

    public void SetData(int day, string subText, bool isCurrentMonth, bool isToday, bool isWeekend)
    {
        _day = day;
        _sub = subText ?? "";
        _isCurrentMonth = isCurrentMonth;
        _isToday = isToday;
        _isWeekend = isWeekend;
        InvalidateVisual();
    }

    public void Clear()
    {
        _day = 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_day == 0)
        {
            return;
        }
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0 || !double.IsFinite(w) || !double.IsFinite(h))
        {
            return;
        }
        var dark = DarkProvider?.Invoke() ?? ThemeSense.IsDark(this);
        var hasSub = _isCurrentMonth && _sub.Length > 0;
        // 农历副文本在 168 级小卡上会掉到 6px 以下 变成灰糊 不够 7.5px 干脆不画
        var subSize = Math.Min(w * 0.30, h * 0.26);
        subSize = Math.Min(subSize, w * 0.94 / Math.Max(2, _sub.Length));
        if (subSize < 7.5)
        {
            hasSub = false;
        }
        var dayRect = hasSub
            ? new Rect(0, h * 0.02, w, h * 0.54)
            : new Rect(0, 0, w, h);
        var daySize = hasSub
            ? Math.Min(w * 0.46, dayRect.Height * 0.86)
            : Math.Min(w * 0.44, h * 0.58);

        if (_isToday)
        {
            // 今日圆只包住日期 农历行留在圆外
            var radius = Math.Min(w * 0.42, h * 0.25);
            var cy = dayRect.Y + dayRect.Height / 2;
            context.DrawEllipse(new SolidColorBrush(CardAccent.Value), null,
                new Point(w / 2, cy), radius, radius);
        }

        // 日期文字色
        IBrush dayBrush;
        if (_isToday)
        {
            var accent = CardAccent.Value;
            var luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
            dayBrush = new SolidColorBrush(luminance > 0.58 ? Color.FromRgb(20, 20, 20) : Colors.White);
        }
        else if (!_isCurrentMonth)
        {
            dayBrush = new SolidColorBrush(dark ? Color.Parse("#555555") : Color.FromArgb(60, 0, 0, 0));
        }
        else if (_isWeekend)
        {
            dayBrush = new SolidColorBrush(dark ? Color.Parse("#9a9a9a") : Color.FromArgb(140, 0, 0, 0));
        }
        else
        {
            dayBrush = new SolidColorBrush(dark ? Color.Parse("#e8e8e8") : Color.Parse("#1a1a1a"));
        }
        DrawCentered(context, _day.ToString(), daySize, FontWeight.Normal, dayBrush, dayRect);

        // 副文本(节日/节气/农历)
        if (hasSub)
        {
            var subBrush = _isToday
                ? (IBrush)new SolidColorBrush(Color.FromArgb(235, 255, 255, 255))
                : new SolidColorBrush(dark ? Color.Parse("#a8a8a8") : Color.FromArgb(150, 0, 0, 0));
            DrawCentered(context, _sub, subSize, FontWeight.Medium, subBrush,
                new Rect(0, h * 0.56, w, h * 0.40));
        }
    }

    private static void DrawCentered(DrawingContext context, string text, double size, FontWeight weight,
        IBrush brush, Rect rect)
    {
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(Common.AppFontFamily, FontStyle.Normal, weight),
            size,
            brush);
        var x = rect.X + (rect.Width - ft.WidthIncludingTrailingWhitespace) / 2;
        var y = rect.Y + (rect.Height - ft.Height) / 2;
        context.DrawText(ft, new Point(x, y));
    }
}
