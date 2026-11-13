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

// NativeTheme
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

// NativeWidgets.cs

/// <summary>原版模板颜色/字体常量与取值助手</summary>
public static class NativeTheme
{
    // 自绘控件文字统一用 App 字体(内嵌 HarmonyOS Sans) 勿用系统字体名 否则与主程序不一致
    public static readonly FontFamily AppFont = Common.AppFontFamily;
    public const string SerifFont = "Georgia, Times New Roman, serif";
    public const string MonoFont = "Consolas, Courier New, monospace";

    public static Color Rgba(int r, int g, int b, double a) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), (byte)r, (byte)g, (byte)b);

    public static Color Parse(string hex) => Color.Parse(hex);

    public static IBrush Brush(Color c) => new SolidColorBrush(c);

    // 原版 accent 缺省 #30c361
    public static Color Accent()
    {
        try
        {
            return Color.Parse(Config.ThemeColor.Value);
        }
        catch (Exception e)
        {
            Log.Debug($"[Native] 主题色解析失败 用默认绿: {e.Message}");
            return Color.Parse("#30c361");
        }
    }

    // 等宽间距日期 "2026.10.03"(原版 replace("-","."))
    public static string DotDate(string iso) => iso.Replace("-", ".");

    public static bool Dark(StyledElement e) => ThemeSense.IsDark(e);
}
