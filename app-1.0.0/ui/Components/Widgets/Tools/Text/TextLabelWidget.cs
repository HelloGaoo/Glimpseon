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

// TextLabelWidget
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Glimpseon.Core;


namespace Glimpseon.UI;

/// <summary>文字标签</summary>
public sealed class TextLabelWidget : WidgetCardBase
{
    private readonly TextBlock _text = new();
    private readonly bool _empty;
    private readonly double _preferred;

    public TextLabelWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        var content = ReadRawString(config, "text", ComponentDefaults.Text);
        _preferred = Math.Clamp(ReadNumber(config, "size", ComponentDefaults.TextSize), 12, 72);
        _empty = string.IsNullOrWhiteSpace(content);

        _text.Text = _empty ? "在组件设置里填写文字" : content;
        _text.FontWeight = _empty ? WidgetWeight.Body : WidgetWeight.Emphasis;
        _text.TextWrapping = TextWrapping.Wrap;
        _text.TextAlignment = ReadString(config, "align", "center") switch
        {
            "left" => TextAlignment.Left,
            "right" => TextAlignment.Right,
            _ => TextAlignment.Center,
        };
        _text.HorizontalAlignment = HorizontalAlignment.Stretch;
        _text.VerticalAlignment = VerticalAlignment.Center;

        var root = new Grid { Margin = new Thickness(WidgetSpace.Pad) };
        root.Children.Add(_text);
        Card.Child = root;
        Content = Card;

        ApplyPalette();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        AppearanceChanged += ApplyPalette;
        SizeChanged += (_, _) => FitSize();
        FitSize();
    }

    private void FitSize()
    {
        if (_empty)
        {
            _text.FontSize = WidgetType.Body;
            _text.LineHeight = WidgetType.LineHeight(WidgetType.Body);
            return;
        }
        var width = Bounds.Width - WidgetSpace.Pad * 2;
        var height = Bounds.Height - WidgetSpace.Pad * 2;
        if (width <= 1)
        {
            width = Width - WidgetSpace.Pad * 2;
        }
        if (height <= 1)
        {
            height = Height - WidgetSpace.Pad * 2;
        }
        var size = CardText.Fit(_text.Text, WidgetFonts.Ui, WidgetWeight.Emphasis, width, _preferred, 12, _preferred);
        // 按高度收敛
        while (size > 12 && height > 1)
        {
            var textWidth = CardText.Width(_text.Text, WidgetFonts.Ui, WidgetWeight.Emphasis, size);
            var lines = Math.Max(1, Math.Ceiling(textWidth / Math.Max(1, width)));
            if (lines * WidgetType.LineHeight(size) <= height)
            {
                break;
            }
            size = Math.Max(12, size - 1);
        }
        _text.FontSize = size;
        _text.LineHeight = WidgetType.LineHeight(size);
    }

    private void ApplyPalette()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _text.Foreground = _empty ? CardInk.Faint(p) : CardInk.Fg(p);
    }
}
