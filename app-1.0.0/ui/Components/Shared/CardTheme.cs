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

// CardTheme
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

// NativeInteractive.cs

/// <summary>公告/作业板共用主题对 逐值取自模板 ThemePairs</summary>
internal static class CardTheme
{
    public static Color T(string key, bool dark) => dark ? key switch
    {
        "ink" => NativeTheme.Rgba(255, 255, 255, 0.95),
        "sub" => NativeTheme.Rgba(255, 255, 255, 0.62),
        "line" => NativeTheme.Rgba(255, 255, 255, 0.10),
        "hover" => NativeTheme.Rgba(255, 255, 255, 0.06),
        "chip" => NativeTheme.Rgba(255, 255, 255, 0.08),
        "bar" => NativeTheme.Rgba(255, 255, 255, 0.14),
        "empty" => NativeTheme.Rgba(255, 255, 255, 0.40),
        "card" => NativeTheme.Rgba(255, 255, 255, 0.055),
        "cardline" => NativeTheme.Rgba(255, 255, 255, 0.09),
        "dotbd" => NativeTheme.Rgba(255, 255, 255, 0.35),
        _ => Colors.White,
    } : key switch
    {
        "ink" => NativeTheme.Rgba(0, 0, 0, 0.89),
        "sub" => NativeTheme.Rgba(0, 0, 0, 0.62),
        "line" => NativeTheme.Rgba(0, 0, 0, 0.08),
        "hover" => NativeTheme.Rgba(0, 0, 0, 0.045),
        "chip" => NativeTheme.Rgba(0, 0, 0, 0.05),
        "bar" => NativeTheme.Rgba(0, 0, 0, 0.12),
        "empty" => NativeTheme.Rgba(0, 0, 0, 0.45),
        "card" => NativeTheme.Rgba(255, 255, 255, 0.55),
        "cardline" => NativeTheme.Rgba(0, 0, 0, 0.06),
        "dotbd" => NativeTheme.Rgba(0, 0, 0, 0.28),
        _ => Colors.Black,
    };

    internal static readonly Color Danger = NativeTheme.Parse("#e5484d");

    // 顶栏文字开关 字号驱动内距/圆角
    internal static Border TagButton(string text, double fontSize, bool dark)
    {
        var pad = Math.Round(fontSize * 0.87);
        return new Border
        {
            Child = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = WidgetWeight.Title,
                Foreground = new SolidColorBrush(T("sub", dark)),
            },
            Background = new SolidColorBrush(T("chip", dark)),
            Padding = new Thickness(pad, 3, pad, 3),
            CornerRadius = new CornerRadius(Math.Round(fontSize * 0.35)),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // 小号操作按钮(公告栏 ✎× 24x24 / 作业板 18x18)
    internal static Border IconButton(string glyph, double size, bool danger, Func<bool> dark)
    {
        var btn = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(5),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = size * 0.58,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(T("sub", dark())),
            },
        };
        var isDel = glyph == "×";
        btn.PointerEntered += (_, _) =>
        {
            if (isDel && danger)
            {
                btn.Background = new SolidColorBrush(Color.FromArgb(31, 229, 72, 77));
                ((TextBlock)btn.Child!).Foreground = new SolidColorBrush(Danger);
            }
            else
            {
                btn.Background = new SolidColorBrush(T("chip", dark()));
                ((TextBlock)btn.Child!).Foreground = new SolidColorBrush(NativeTheme.Accent());
            }
        };
        btn.PointerExited += (_, _) =>
        {
            btn.Background = null;
            ((TextBlock)btn.Child!).Foreground = new SolidColorBrush(T("sub", dark()));
        };
        return btn;
    }
}
