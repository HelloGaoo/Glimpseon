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

// NativeDailyWordWidget
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
/// 每日单词
/// </summary>
public sealed class NativeDailyWordWidget : WidgetCardBase
{
    // 词性拆行
    private static readonly Regex PosSplitRegex = new(@"\s+(?=[a-zA-Z]{1,10}\.\s)");
    private static readonly Regex PosMarkRegex = new(@"^([a-zA-Z]{1,10}\.)\s*(.*)$");

    private static Color T(string key, bool dark) => dark ? key switch
    {
        "word1" => NativeTheme.Parse("#f5f5f5"),
        "word2" => NativeTheme.Parse("#9dbdae"),
        "zh" => NativeTheme.Rgba(255, 255, 255, 0.85),
        "zh_sub" => NativeTheme.Rgba(255, 255, 255, 0.62),
        "ex_en" => NativeTheme.Parse("#e8e8e8"),
        "date_c" => NativeTheme.Rgba(255, 255, 255, 0.45),
        "sep" => NativeTheme.Rgba(255, 255, 255, 0.25),
        _ => Colors.White,
    } : key switch
    {
        "word1" => NativeTheme.Parse("#1f2937"),
        "word2" => NativeTheme.Parse("#4f7a6b"),
        "zh" => NativeTheme.Rgba(40, 40, 40, 0.88),
        "zh_sub" => NativeTheme.Rgba(40, 40, 40, 0.65),
        "ex_en" => NativeTheme.Parse("#2d3a33"),
        "date_c" => NativeTheme.Rgba(60, 60, 60, 0.5),
        "sep" => NativeTheme.Rgba(0, 0, 0, 0.22),
        _ => Colors.Black,
    };

    private readonly TextBlock _bgLetter = new();
    private readonly TextBlock _date = new();
    private readonly TextBlock _word = new();
    private readonly Border _phoneticChip = new();
    private readonly TextBlock _phoneticText = new();
    private readonly StackPanel _trans = new();
    private readonly DashedRule _sep = new();
    private readonly StackPanel _example = new();
    private readonly TextBlock _exEn = new();
    private readonly TextBlock _exZh = new();

    // 最近一次数据 主题切换时重排
    private string _lastWord = "--", _lastPhonetic = "", _lastTrans = "", _lastExText = "", _lastExTrans = "", _lastDate = "";

    private string _packCursor = "";
    private DateTime _netAt;

    public NativeDailyWordWidget(ComponentDefinition definition) : base(definition)
    {
        BuildLayout();
        ActualThemeVariantChanged += (_, _) => Apply(_lastWord, _lastPhonetic, _lastTrans, _lastExText, _lastExTrans, _lastDate);
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

    private IBrush WatermarkBrush(Color accent) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(accent, 0),
            new GradientStop(Colors.Transparent, 0.85),
        },
    };

    private void BuildLayout()
    {
        var serif = new FontFamily(NativeTheme.SerifFont);
        var accent = NativeTheme.Accent();

        _bgLetter.FontFamily = serif;
        _bgLetter.FontSize = 150;
        _bgLetter.FontWeight = FontWeight.Bold;
        _bgLetter.HorizontalAlignment = HorizontalAlignment.Right;
        _bgLetter.VerticalAlignment = VerticalAlignment.Top;
        _bgLetter.Margin = new Thickness(0, -20, 2, 0);
        _bgLetter.Opacity = 0.16;
        _bgLetter.IsHitTestVisible = false;

        _date.FontFamily = new FontFamily(NativeTheme.MonoFont);
        _date.FontSize = WidgetType.Label;
        _date.LetterSpacing = 1.5;
        _date.HorizontalAlignment = HorizontalAlignment.Right;
        _date.VerticalAlignment = VerticalAlignment.Top;
        _date.Margin = new Thickness(0, 16, 18, 0);

        _word.FontFamily = serif;
        _word.FontSize = 40;
        _word.FontWeight = WidgetWeight.Display;

        _phoneticText.FontSize = WidgetType.Title;
        _phoneticChip.Child = _phoneticText;
        _phoneticChip.Padding = new Thickness(10, 2);
        _phoneticChip.CornerRadius = new CornerRadius(999);
        _phoneticChip.VerticalAlignment = VerticalAlignment.Center;

        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
        };
        head.Children.Add(_word);
        head.Children.Add(_phoneticChip);

        _trans.Margin = new Thickness(0, WidgetSpace.Gap, 0, 0);
        _trans.Spacing = WidgetSpace.GapXs;

        _sep.Margin = new Thickness(0, 12, 0, 0);
        _sep.HorizontalAlignment = HorizontalAlignment.Left;
        _sep.Width = 200; // 60% of 400 内容宽

        _exEn.FontFamily = serif;
        _exEn.FontStyle = FontStyle.Italic;
        _exEn.FontSize = WidgetType.Headline;
        _exEn.LineHeight = WidgetType.LineHeight(WidgetType.Headline);
        _exEn.TextWrapping = TextWrapping.Wrap;
        _exZh.Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0);
        _exZh.FontSize = WidgetType.Body;
        _exZh.LineHeight = WidgetType.LineHeight(WidgetType.Body);
        _exZh.TextWrapping = TextWrapping.Wrap;
        _example.Margin = new Thickness(0, 12, 0, 0);
        _example.Children.Add(_exEn);
        _example.Children.Add(_exZh);

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(head);
        content.Children.Add(_trans);
        content.Children.Add(_sep);
        content.Children.Add(_example);

        var pad = new Border { Padding = new Thickness(24, 16, 24, 16), Child = content };

        var root = new Grid();
        root.Children.Add(pad);
        root.Children.Add(_bgLetter);
        root.Children.Add(_date);
        Card.Child = root;
        Content = Card;
    }

    private void ApplyFromPack(JsonObject o)
    {
        var w = o["word"]?.GetValue<string>() ?? "--";
        var ph = o["phonetic"]?.GetValue<string>() ?? "";
        var tr = o["translation"]?.GetValue<string>() ?? "";
        var ex = o["example"]?.GetValue<string>() ?? "";
        var exTr = o["example_trans"]?.GetValue<string>() ?? "";
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        Dispatcher.UIThread.Post(() => Apply(w, ph, tr, ex, exTr, day));
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        if (DailyPack.Take("word") is { } take)
        {
            if (take.Cursor != _packCursor)
            {
                _packCursor = take.Cursor;
                ApplyFromPack(take.Item);
            }
            return;
        }
        if ((AppUtils.PreciseNow() - _netAt).TotalMinutes < DailyPack.NetworkRetryMinutes)
        {
            return;
        }
        _netAt = AppUtils.PreciseNow();
        JsonElement? data = null;
        try
        {
            data = await WordService.FetchDailyWordAsync();
        }
        catch (Exception e)
        {
            Log.Warning($"[DWD] 在线获取失败 走缓存: {e.Message}");
        }
        // 原版: 接口无数据用过期缓存
        if (data is null)
        {
            data = AppUtils.LoadCache("daily_word", ignoreExpiry: true);
        }
        string word = "--", phonetic = "", translation = "", exText = "", exTrans = "";
        var date = DateTime.Today.ToString("yyyy-MM-dd");
        if (data is { ValueKind: JsonValueKind.Object } root)
        {
            date = root.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString() ?? date
                : date;
            if (root.TryGetProperty("word", out var w) && w.ValueKind == JsonValueKind.Object)
            {
                word = w.TryGetProperty("word", out var wp) ? wp.GetString() ?? "--" : "--";
                phonetic = w.TryGetProperty("phonetic", out var ph) ? ph.GetString() ?? "" : "";
                translation = w.TryGetProperty("translation", out var trEl) ? trEl.GetString() ?? "" : "";
                if (w.TryGetProperty("examples", out var exs) && exs.ValueKind == JsonValueKind.Array && exs.GetArrayLength() > 0 && exs[0].ValueKind == JsonValueKind.Object)
                {
                    var ex = exs[0];
                    exText = ex.TryGetProperty("text", out var et) ? et.GetString() ?? "" : "";
                    exTrans = ex.TryGetProperty("translation", out var etr) ? etr.GetString() ?? "" : "";
                }
            }
        }
        Dispatcher.UIThread.Post(() => Apply(word, phonetic, translation, exText, exTrans, date));
    }

    private void Apply(string word, string phonetic, string translation, string exText, string exTrans, string date)
    {
        _lastWord = word;
        _lastPhonetic = phonetic;
        _lastTrans = translation;
        _lastExText = exText;
        _lastExTrans = exTrans;
        _lastDate = date;
        var dark = IsDarkNow;
        var accent = NativeTheme.Accent();

        _date.Text = NativeTheme.DotDate(date);
        _word.Text = word;
        _bgLetter.Text = word != "--" && word.Length > 0 ? char.ToUpperInvariant(word[0]).ToString() : "";

        // 音标胶囊(空则隐藏)
        _phoneticChip.IsVisible = phonetic.Length > 0;
        _phoneticText.Text = phonetic;
        _phoneticText.Foreground = new SolidColorBrush(accent);
        _phoneticChip.BorderBrush = new SolidColorBrush(NativeTheme.Rgba(accent.R, accent.G, accent.B, 0.5));
        _phoneticChip.BorderThickness = new Thickness(1);

        // 词性拆行
        _trans.Children.Clear();
        foreach (var raw in PosSplitRegex.Split(translation.Trim()))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WidgetSpace.Gap };
            var m = PosMarkRegex.Match(line);
            if (m.Success)
            {
                var posChip = new Border
                {
                    Background = new SolidColorBrush(NativeTheme.Rgba(accent.R, accent.G, accent.B, 0.14)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = m.Groups[1].Value,
                        FontSize = WidgetType.Label,
                        Foreground = new SolidColorBrush(accent),
                    },
                };
                row.Children.Add(posChip);
                row.Children.Add(new TextBlock
                {
                    Text = m.Groups[2].Value,
                    FontSize = WidgetType.Headline,
                    Foreground = new SolidColorBrush(T("zh", dark)),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            else
            {
                row.Children.Add(new TextBlock
                {
                    Text = line,
                    FontSize = WidgetType.Headline,
                    Foreground = new SolidColorBrush(T("zh", dark)),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            _trans.Children.Add(row);
        }

        // 例句区(空则整体隐藏)
        _example.IsVisible = exText.Length > 0;
        _sep.IsVisible = exText.Length > 0;
        _exEn.Text = "“" + exText + "”";
        _exZh.Text = exTrans;
        _exZh.IsVisible = exTrans.Length > 0;

        // 主题色
        _word.Foreground = Gradient(T("word1", dark), T("word2", dark));
        _bgLetter.Foreground = WatermarkBrush(accent);
        _date.Foreground = new SolidColorBrush(T("date_c", dark));
        _sep.Stroke = new SolidColorBrush(T("sep", dark));
        _exEn.Foreground = new SolidColorBrush(T("ex_en", dark));
        _exZh.Foreground = new SolidColorBrush(T("zh_sub", dark));
        _sep.Stroke = new SolidColorBrush(T("sep", dark));
        InvalidateVisual();
    }
}
