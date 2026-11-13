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

// PoetryWidget
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

// 一言
// 规格: 400x200 边距(24,20,24,20) 单label居中wrap 富文本(——分离出处/逗号换行) 字号PoetrySize(16) 色PoetryTextColor

public sealed class PoetryWidget : WidgetCardBase
{
    private const string FallbackContent = "山重水复疑无路，柳暗花明又一村。";
    private const string FallbackAuthor = "陆游《游山西村》";

    private readonly TextBlock _contentText = new();
    private readonly Border _authorBar = new();
    private readonly TextBlock _authorText = new();
    private readonly StackPanel _authorRow;
    private readonly StackPanel _stack;
    private readonly Border _accentBar = new();

    public PoetryWidget(ComponentDefinition definition) : base(definition)
    {
        _contentText.TextWrapping = TextWrapping.Wrap;
        _contentText.TextAlignment = TextAlignment.Left;
        _contentText.HorizontalAlignment = HorizontalAlignment.Stretch;
        _contentText.FontWeight = FontWeight.Light;
        _contentText.LetterSpacing = 0.6;
        _contentText.Margin = new Thickness(16, 0, 0, 0);

        _contentText.VerticalAlignment = VerticalAlignment.Center;

        _stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _stack.Children.Add(_contentText);
        _contentText.VerticalAlignment = VerticalAlignment.Center;
        _stack.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(280),
            },
        };

        _authorText.FontSize = WidgetType.Label;
        _authorText.VerticalAlignment = VerticalAlignment.Center;
        _authorBar.Width = 14;
        _authorBar.Height = 1;
        _authorBar.VerticalAlignment = VerticalAlignment.Center;
        _authorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = WidgetSpace.Gap,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _authorRow.Children.Add(_authorBar);
        _authorRow.Children.Add(_authorText);
        _authorRow.HorizontalAlignment = HorizontalAlignment.Left;
        _authorRow.Margin = new Thickness(16, 0, 0, 0);

        var root = new Grid
        {
            Margin = new Thickness(20, 16, 20, 16),
            RowDefinitions = RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = WidgetSpace.Gap,
        };
        Grid.SetRow(_stack, 1);
        Grid.SetRow(_authorRow, 2);
        // 作者行固定在第二行
        root.Children.Add(_stack);
        root.Children.Add(_authorRow);
        // 两行布局 正文区与作者行
        _accentBar.Width = 3;
        _accentBar.CornerRadius = new CornerRadius(2);
        _accentBar.HorizontalAlignment = HorizontalAlignment.Left;
        _accentBar.VerticalAlignment = VerticalAlignment.Stretch;
        _accentBar.Margin = new Thickness(0, 2, 0, 2);
        Grid.SetRowSpan(_accentBar, 3);
        root.Children.Add(_accentBar);
        Card.Child = root;
        Content = Card;
        SizeChanged += (_, _) => FitText();

        ApplyPoetryStyle();
        ActualThemeVariantChanged += (_, _) => ApplyPoetryStyle();
        AppearanceChanged += ApplyPoetryStyle;
        Config.PoetryTextColor.ValueChanged += _ => ApplyPoetryStyle();
        Config.PoetrySize.ValueChanged += _ => FitText();
        Config.ShowPoetry.ValueChanged += _ => RefreshFromCache();

        // 原版 1s tick 读缓存
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => RefreshFromCache();
        timer.Start();

        // 后台拉取(原版由主界面广播/interval刷新)
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await PoetryService.GetPoetryWithCacheAsync();
            }
            catch (Exception e)
            {

                Log.Warning($"[SW] 在线获取失败 走缓存: {e.Message}");
                // 拉取失败走缓存
            }
        });
        RefreshFromCache();
    }

    private void RefreshFromCache()
    {
        IsVisible = Config.ShowPoetry.Value;
        var cache = AppUtils.LoadCache("poetry", ignoreExpiry: true);
        var text = cache is { ValueKind: JsonValueKind.String } cacheStr ? cacheStr.GetString() : null;
        FormatPoetry(text);
    }

    // 原版 _format_poetry: —— / — 分离出处 逗号后换行；无缓存时用本地名句兜底
    private void FormatPoetry(string? text)
    {
        var content = text?.Trim() ?? "";
        var attribution = "";
        if (string.IsNullOrWhiteSpace(content))
        {
            content = FallbackContent;
            attribution = FallbackAuthor;
        }
        else
        {
            foreach (var sep in new[] { "——", "—" })
            {
                var idx = content.IndexOf(sep, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    attribution = content[(idx + sep.Length)..].Trim();
                    content = content[..idx].Trim();
                    break;
                }
            }
        }
        content = System.Text.RegularExpressions.Regex.Replace(content, "([,，])", "$1\n").Trim();
        if (content.EndsWith("\n"))
        {
            content = content[..^1].TrimEnd();
        }
        var changed = _contentText.Text != content || _authorText.Text != attribution;
        if (changed)
        {
            _stack.Opacity = 0;
            _contentText.Text = content;
            _authorText.Text = attribution;
            _authorRow.IsVisible = attribution.Length > 0;
            FitText();
            Dispatcher.UIThread.Post(() => _stack.Opacity = 1);
        }
        else
        {
            FitText();
        }
    }

    // 字号同时受宽与高约束 出处保持小字
    private void FitText()
    {
        var w = Bounds.Width - 64;
        var h = Bounds.Height - 64;
        if (w <= 1 || h <= 1 || string.IsNullOrEmpty(_contentText.Text))
        {
            return;
        }
        var baseSize = Math.Clamp(Config.PoetrySize.Value * 1.6, 16, 30);
        var lines = _contentText.Text.Split('\n');
        double widest = 0;
        foreach (var line in lines)
        {
            widest = Math.Max(widest, CardText.Width(line, Common.AppFontFamily, FontWeight.Light, baseSize));
        }
        var byWidth = widest > w ? baseSize * w / widest : baseSize;
        var byHeight = h / (Math.Max(1, lines.Length) * 1.5);
        var size = Math.Clamp(Math.Min(baseSize, Math.Min(byWidth, byHeight)), 12, 30);
        _contentText.FontSize = Math.Round(size, 1);
        _contentText.LineHeight = Math.Round(size * 1.45, 1);
    }

    private void ApplyPoetryStyle()
    {
        // 自定义色优先 兜底走色板 浅色卡防白字
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var ink = ConfiguredInk(Config.PoetryTextColor.Value) ?? p.Text;
        var muted = new SolidColorBrush(ink, CardInk.MutedOpacity);
        _contentText.Foreground = new SolidColorBrush(ink);
        _authorText.Foreground = muted;
        // 作者行只留虚线短杆与出处
        _authorBar.Background = CardAccent.Brush(0.45);
        _accentBar.Background = CardAccent.Brush(0.75);
        FitText();
    }
}
