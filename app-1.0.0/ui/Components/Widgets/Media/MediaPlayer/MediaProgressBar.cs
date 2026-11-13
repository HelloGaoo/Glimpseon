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

// MediaProgressBar
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

// 媒体播放器
// 规格: 400x200 内容边距(16,20,12,20) 间距14 封面160x160 圆角10 淡入300ms
// 标题19px w700 h28 歌手11px w500 h16 歌词12px w700 进度条3px 时间10px 右对齐
// 按钮行间距16 居中 prev28x28(icon20) play32x32(icon24) next28x28
// 色板 深色 标题#F5F5FA 歌手rgba(230,230,235,200) 时间rgba(220,220,225,150) 歌词rgba(235,235,240,170)
//       浅色 标题#1F1F1F 歌手rgba(60,60,67,214) 时间rgba(60,60,67,138) 歌词rgba(44,44,46,153)
// 默认封面 深(40,40,48)/浅(230,230,235) 音符 手绘音符形状

/// <summary>媒体进度条: 3px 细轨 + 强调色已播放段 + 末端圆点</summary>
internal sealed class MediaProgressBar : Control
{
    private double _progress;

    public Func<bool>? DarkProvider { get; set; }

    public double Progress
    {
        get => _progress;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (Math.Abs(v - _progress) < 0.0001)
            {
                return;
            }
            _progress = v;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 20 || h < 3)
        {
            return;
        }

        var dark = DarkProvider?.Invoke() ?? ThemeSense.IsDark(this);
        var p = WidgetSurfacePalette.Resolve(dark);
        var accent = CardAccent.Value;
        var trackH = Math.Min(3, h);
        var y = (h - trackH) / 2;
        var radius = trackH / 2;

        ctx.DrawRectangle(new SolidColorBrush(p.Text, dark ? 0.12 : 0.09), null,
            new Rect(0, y, w, trackH), radius, radius);
        if (_progress <= 0)
        {
            return;
        }
        var fillW = Math.Max(trackH, w * _progress);
        ctx.DrawRectangle(new SolidColorBrush(accent), null,
            new Rect(0, y, fillW, trackH), radius, radius);
        var knob = trackH + 3;
        ctx.DrawEllipse(new SolidColorBrush(accent), null,
            new Point(Math.Clamp(fillW, knob / 2, w - knob / 2), h / 2), knob / 2, knob / 2);
    }
}
