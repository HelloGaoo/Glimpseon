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

// QuickSearchWidget
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Glimpseon.Core;

namespace Glimpseon.UI;

/// <summary>
/// 快捷搜索
/// 400x100: 搜索框 + 引擎
/// </summary>
public sealed class QuickSearchWidget : WidgetCardBase
{
    internal static readonly (string Key, string Text)[] EngineOptions =
    {
        ("bing", "必应"),
        ("baidu", "百度"),
        ("google", "谷歌"),
        ("zhihu", "知乎"),
        ("bilibili", "B站"),
    };

    private static readonly Dictionary<string, string> EngineUrls = new()
    {
        ["bing"] = "https://www.bing.com/search?q=",
        ["baidu"] = "https://www.baidu.com/s?wd=",
        ["google"] = "https://www.google.com/search?q=",
        ["zhihu"] = "https://www.zhihu.com/search?type=content&q=",
        ["bilibili"] = "https://search.bilibili.com/all?keyword=",
    };

    private readonly TextBlock _icon = AppIcons.Create(AppIcons.Search, 15);
    private readonly TextBox _input = new()
    {
        FontSize = WidgetType.Body,
        Padding = new Thickness(0),
        BorderThickness = new Thickness(0),
        MinHeight = 32,
        Background = Brushes.Transparent,
        VerticalAlignment = VerticalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly ComboBox _engine = new()
    {
        Width = 78,
        Height = 32,
        FontSize = WidgetType.Label,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
    };
    private readonly Border _field;
    private readonly Border _clear = new()
    {
        Width = 22,
        Height = 22,
        CornerRadius = new CornerRadius(11),
        Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Cursor = new Cursor(StandardCursorType.Hand),
        IsVisible = false,
    };
    private readonly TextBlock _clearIcon = AppIcons.Create(AppIcons.Close, 10);
    private bool _clearHover;
    private bool _clearPressed;
    private string _engineKey = "bing";

    public QuickSearchWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _engineKey = ReadString(config, "engine", "bing");
        if (!EngineUrls.ContainsKey(_engineKey))
        {
            _engineKey = "bing";
        }
        _input.PlaceholderText = ReadString(config, "placeholder",
            AppUtils.Tr("component_edit.search_placeholder"));

        // TextBox 透明
        foreach (var key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver",
                     "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
                 })
        {
            _input.Resources[key] = Brushes.Transparent;
        }
        foreach (var key in new[]
                 {
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver",
                     "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled",
                 })
        {
            _input.Resources[key] = Brushes.Transparent;
        }

        foreach (var (key, text) in EngineOptions)
        {
            _engine.Items.Add(new ComboBoxItem { Content = text, Tag = key });
        }
        _engine.SelectedIndex = Math.Max(0, Array.FindIndex(EngineOptions, e => e.Key == _engineKey));
        _engine.SelectionChanged += (_, _) =>
        {
            if (_engine.SelectedItem is ComboBoxItem { Tag: string key })
            {
                _engineKey = key;
            }
        };

        _icon.VerticalAlignment = VerticalAlignment.Center;
        _icon.HorizontalAlignment = HorizontalAlignment.Center;

        _clearIcon.HorizontalAlignment = HorizontalAlignment.Center;
        _clearIcon.VerticalAlignment = VerticalAlignment.Center;
        _clear.Child = _clearIcon;
        ToolTip.SetTip(_clear, "清空（Esc）");
        _clear.PointerEntered += (_, _) =>
        {
            _clearHover = true;
            ApplyPalette();
        };
        _clear.PointerExited += (_, _) =>
        {
            _clearHover = false;
            ApplyPalette();
        };
        _clear.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_clear).Properties.IsLeftButtonPressed)
            {
                return;
            }
            _clearPressed = true;
            e.Pointer.Capture(_clear);
            e.Handled = true;
        };
        _clear.PointerCaptureLost += (_, _) => _clearPressed = false;
        _clear.PointerReleased += (_, e) =>
        {
            if (!_clearPressed)
            {
                return;
            }
            _clearPressed = false;
            e.Pointer.Capture(null);
            var local = e.GetPosition(_clear);
            if (local.X >= 0 && local.Y >= 0 && local.X <= _clear.Bounds.Width && local.Y <= _clear.Bounds.Height)
            {
                _input.Clear();
                _input.Focus();
            }
            e.Handled = true;
        };

        var fieldBody = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
            ColumnSpacing = WidgetSpace.GapSm,
        };
        fieldBody.Children.Add(_icon);
        Grid.SetColumn(_input, 1);
        fieldBody.Children.Add(_input);
        Grid.SetColumn(_clear, 2);
        fieldBody.Children.Add(_clear);

        _field = new Border
        {
            Height = 36,
            CornerRadius = new CornerRadius(WidgetSpace.RadiusInner),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(WidgetSpace.PadCompact, 0),
            Child = fieldBody,
        };

        _input.TextChanged += (_, _) =>
        {
            _clear.IsVisible = !string.IsNullOrEmpty(_input.Text);
        };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Search();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _input.Clear();
            }
        };

        var main = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            ColumnSpacing = WidgetSpace.GapSm,
            VerticalAlignment = VerticalAlignment.Center,
        };
        main.Children.Add(_field);
        Grid.SetColumn(_engine, 1);
        main.Children.Add(_engine);

        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.PadCompact),
        };
        root.Children.Add(main);
        Card.Child = root;
        Content = Card;

        ApplyPalette();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
    }

    private void Search()
    {
        var text = _input.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        var url = EngineUrls.GetValueOrDefault(_engineKey, EngineUrls["bing"]) + Uri.EscapeDataString(text);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Warning($"[Search] 打开浏览器失败: {e.Message}");
        }
        _input.Clear();
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var accent = CardAccent.Value;
        _icon.Foreground = new SolidColorBrush(accent);
        _field.Background = new SolidColorBrush(p.Text, CardIsDark ? 0.055 : 0.04);
        _field.BorderBrush = new SolidColorBrush(p.Text, CardIsDark ? 0.10 : 0.08);
        _input.Foreground = new SolidColorBrush(p.Text);
        _input.CaretBrush = new SolidColorBrush(accent);
        _input.PlaceholderForeground = CardInk.Faint(p);
        _input.SelectionBrush = new SolidColorBrush(accent, 0.30);
        _input.SelectionForegroundBrush = new SolidColorBrush(p.Text);
        _clearIcon.Foreground = _clearHover ? CardInk.Fg(p) : CardInk.Faint(p);
        _clear.Background = _clearHover
            ? new SolidColorBrush(p.Text, CardIsDark ? 0.12 : 0.09)
            : Brushes.Transparent;
        _engine.Foreground = CardInk.Fg(p);
        _engine.Background = Brushes.Transparent;
        _engine.BorderBrush = new SolidColorBrush(p.Text, CardIsDark ? 0.10 : 0.08);
        _engine.Resources["ComboBoxBackground"] = Brushes.Transparent;
        _engine.Resources["ComboBoxBackgroundPointerOver"] = new SolidColorBrush(p.Text, 0.05);
        _engine.Resources["ComboBoxBackgroundPressed"] = new SolidColorBrush(p.Text, 0.08);
        _engine.Resources["ComboBoxBorderBrush"] = new SolidColorBrush(p.Text, CardIsDark ? 0.10 : 0.08);
    }
}
