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

// NativeDailySentenceWidget
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
/// 每日英语
/// 大引号(Georgia 118px accent渐变) + 英句(Georgia italic w600 27px 双色渐变)
/// + 中文(左侧 3px accent 边线 17px) + 日期 + 左下渐变条(150x2)
/// </summary>
public sealed class NativeDailySentenceWidget : WidgetCardBase
{
    private static Color T(string key, bool dark) => dark ? key switch
    {
        "en1" => NativeTheme.Parse("#f5f5f5"),
        "en2" => NativeTheme.Parse("#9dbdae"),
        "zh" => NativeTheme.Rgba(255, 255, 255, 0.72),
        "date_c" => NativeTheme.Rgba(255, 255, 255, 0.45),
        _ => Colors.White,
    } : key switch
    {
        "en1" => NativeTheme.Parse("#1f2937"),
        "en2" => NativeTheme.Parse("#4f7a6b"),
        "zh" => NativeTheme.Rgba(40, 40, 40, 0.78),
        "date_c" => NativeTheme.Rgba(60, 60, 60, 0.5),
        _ => Colors.Black,
    };

    private readonly TextBlock _quote = new();
    private readonly TextBlock _en = new();
    private readonly TextBlock _zh = new();
    private readonly TextBlock _date = new();
    private readonly Rectangle _bar = new();

    // 最近一次数据 主题切换时重排
    private string _lastContent = "--", _lastNote = "", _lastDate = "";

    // 包游标 同游标不重绘
    private string _packCursor = "";
    private DateTime _netAt;

    public NativeDailySentenceWidget(ComponentDefinition definition) : base(definition)
    {
        BuildLayout();
        ActualThemeVariantChanged += (_, _) => Apply(_lastContent, _lastNote, _lastDate);
        // 60s tick 本地包零网络
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += (_, _) => _ = RefreshAsync();
        timer.Start();
        _ = RefreshAsync();
    }

    private bool IsDarkNow => CardIsDark;

    private IBrush Gradient(Color c1, Color c2) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(c1, 0),
            new GradientStop(c2, 1),
        },
    };

    private void BuildLayout()
    {
        var serif = new FontFamily(NativeTheme.SerifFont);

        _quote.Text = "“";
        _quote.FontFamily = serif;
        _quote.FontSize = 118;
        _quote.HorizontalAlignment = HorizontalAlignment.Left;
        _quote.VerticalAlignment = VerticalAlignment.Top;
        _quote.Margin = new Thickness(14, -4, 0, 0);
        _quote.IsHitTestVisible = false;

        _en.FontFamily = serif;
        _en.FontStyle = FontStyle.Italic;
        _en.FontWeight = WidgetWeight.Title;
        _en.FontSize = 27;
        _en.TextWrapping = TextWrapping.Wrap;
        _en.LineHeight = 27 * 1.42;

        // 中文: 左侧 3px accent 边线
        _zh.FontSize = WidgetType.Headline;
        _zh.TextWrapping = TextWrapping.Wrap;
        _zh.LineHeight = WidgetType.LineHeight(WidgetType.Headline, 1.5);
        var zhBorder = new Border
        {
            Child = _zh,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 0, 0, 0),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _date.FontFamily = new FontFamily(NativeTheme.MonoFont);
        _date.FontSize = WidgetType.Label;
        _date.LetterSpacing = 1.5;
        _date.HorizontalAlignment = HorizontalAlignment.Right;
        _date.VerticalAlignment = VerticalAlignment.Top;
        _date.Margin = new Thickness(0, 16, 18, 0);

        _bar.Width = 150;
        _bar.Height = 2;
        _bar.HorizontalAlignment = HorizontalAlignment.Left;
        _bar.VerticalAlignment = VerticalAlignment.Bottom;
        _bar.Margin = new Thickness(24, 0, 0, 12);

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(_en);
        content.Children.Add(zhBorder);

        var pad = new Border { Padding = new Thickness(56, 20, 24, 24), Child = content };

        var root = new Grid();
        root.Children.Add(pad);
        root.Children.Add(_quote);
        root.Children.Add(_date);
        root.Children.Add(_bar);
        Card.Child = root;
        Content = Card;
    }

    // 包条目落版
    private void ApplyFromPack(JsonObject o)
    {
        var body = o["content"]?.GetValue<string>() ?? "--";
        var note = o["note"]?.GetValue<string>() ?? "";
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        Dispatcher.UIThread.Post(() => Apply(body, note, day));
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        // 包优先 同游标不重绘
        if (DailyPack.Take("sentence") is { } take)
        {
            if (take.Cursor != _packCursor)
            {
                _packCursor = take.Cursor;
                ApplyFromPack(take.Item);
            }
            return;
        }
        // 回落节流 免 60s 轮询
        if ((AppUtils.PreciseNow() - _netAt).TotalMinutes < DailyPack.NetworkRetryMinutes)
        {
            return;
        }
        _netAt = AppUtils.PreciseNow();
        JsonElement? data = null;
        try
        {
            data = await SentenceService.FetchDailySentenceAsync();
        }
        catch (Exception e)
        {
            Log.Warning($"[DSE] 在线获取失败 走缓存: {e.Message}");
        }
        if (data is null)
        {
            data = AppUtils.LoadCache("daily_sentence", ignoreExpiry: true);
        }
        string content = "--", note = "";
        var date = DateTime.Today.ToString("yyyy-MM-dd");
        if (data is { ValueKind: JsonValueKind.Object } root)
        {
            date = root.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString() ?? date
                : date;
            if (root.TryGetProperty("sentence", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                content = s.TryGetProperty("content", out var c) ? c.GetString() ?? "--" : "--";
                note = s.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "";
            }
        }
        Dispatcher.UIThread.Post(() => Apply(content, note, date));
    }

    private void Apply(string content, string note, string date)
    {
        _lastContent = content;
        _lastNote = note;
        _lastDate = date;
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();
        _en.Text = content;
        _zh.Text = note;
        _zh.IsVisible = note.Length > 0;
        _date.Text = NativeTheme.DotDate(date);

        _quote.Foreground = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(accent, 0),
                new GradientStop(Colors.Transparent, 0.92),
            },
        };
        _en.Foreground = Gradient(T("en1", dark), T("en2", dark));
        _zh.Foreground = new SolidColorBrush(T("zh", dark));
        _date.Foreground = new SolidColorBrush(T("date_c", dark));
        // 中文左边线跟随 accent
        if (_zh.Parent is Border b)
        {
            b.BorderBrush = new SolidColorBrush(accent);
        }
        _bar.Fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(accent, 0),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        InvalidateVisual();
    }
}
