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

// SchoolInfoWidget
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

// 学校班级信息
// 规格: 400x200 边距(8,8,8,8) 间距6 上层背景3:口号1 上层边距(20,16,20,12) 间距4
// 班级行(班级左bold+人数右) 学校名 换行口号(边距20,4,20,4)
// 字号默认 班级48/学校25/人数19/口号23 缩放font_scale 人数"{n}人"

public sealed class SchoolInfoWidget : WidgetCardBase
{
    private readonly Grid _content;
    private readonly Grid _empty;
    private readonly TextBlock _classText = new();
    private readonly TextBlock _countText = new();
    private readonly TextBlock _schoolText = new();
    private readonly TextBlock _sloganText = new();
    private readonly TextBlock _watermark = new();
    private readonly Border _badge = new();
    private readonly TextBlock _badgeText = new();
    private readonly Border _countChip = new();
    private readonly Border _sloganBar = new();
    private readonly CardEmptyState _emptyView;

    private string _class = "";
    private string _school = "";
    private string _count = "";
    private string _slogan = "";
    private int _classSize = 48;
    private int _schoolSize = 25;
    private int _countSize = 19;
    private int _sloganSize = 23;
    private string _textColor = "";
    private int _fontScale = 100;

    public SchoolInfoWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        _content = BuildContent();
        _emptyView = new CardEmptyState(
            FAIcons.Glyph(AppIcons.People),
            AppUtils.Tr("school_info_component.empty_title"),
            AppUtils.Tr("school_info_component.click_to_config"));
        _empty = BuildEmpty();

        var host = new Grid();
        host.Children.Add(_content);
        host.Children.Add(_empty);
        Card.Child = host;
        Content = Card;

        ReadConfig(config);
        ApplyCardStyle();
        ApplyStyle();
        UpdateInfo();
        ActualThemeVariantChanged += (_, _) => ApplyStyle();
    }

    private Grid BuildContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*,Auto"),
            RowSpacing = WidgetSpace.GapSm,
        };

        // 超大班级首字水印 让空余区域有层次
        _watermark.FontSize = 96;
        _watermark.FontWeight = FontWeight.Bold;
        _watermark.HorizontalAlignment = HorizontalAlignment.Right;
        _watermark.VerticalAlignment = VerticalAlignment.Center;
        _watermark.IsHitTestVisible = false;
        Grid.SetRowSpan(_watermark, 3);

        var header = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*,Auto"),
            ColumnSpacing = 12,
        };
        _badge.Width = 54;
        _badge.Height = 54;
        _badge.CornerRadius = new CornerRadius(WidgetSpace.Radius);
        _badge.VerticalAlignment = VerticalAlignment.Center;
        _badge.ClipToBounds = true;
        _badgeText.FontSize = 24;
        _badgeText.FontWeight = WidgetWeight.Title;
        _badgeText.HorizontalAlignment = HorizontalAlignment.Center;
        _badgeText.VerticalAlignment = VerticalAlignment.Center;
        _badge.Child = _badgeText;

        var identity = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        _classText.FontWeight = WidgetWeight.Display;
        _classText.TextTrimming = TextTrimming.CharacterEllipsis;
        _classText.MaxLines = 1;
        _schoolText.TextTrimming = TextTrimming.CharacterEllipsis;
        _schoolText.MaxLines = 1;
        identity.Children.Add(_classText);
        identity.Children.Add(_schoolText);

        _countChip.Padding = new Thickness(WidgetSpace.Gap, WidgetSpace.GapXs);
        _countChip.CornerRadius = new CornerRadius(WidgetSpace.RadiusPill);
        _countChip.VerticalAlignment = VerticalAlignment.Center;
        _countChip.Child = _countText;

        Grid.SetColumn(_badge, 0);
        Grid.SetColumn(identity, 1);
        Grid.SetColumn(_countChip, 2);
        header.Children.Add(_badge);
        header.Children.Add(identity);
        header.Children.Add(_countChip);

        var slogan = new Grid
        {
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,*"),
            ColumnSpacing = WidgetSpace.Gap,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _sloganBar.Width = 3;
        _sloganBar.CornerRadius = new CornerRadius(2);
        _sloganBar.VerticalAlignment = VerticalAlignment.Stretch;
        _sloganText.TextWrapping = TextWrapping.Wrap;
        _sloganText.TextTrimming = TextTrimming.CharacterEllipsis;
        _sloganText.MaxLines = 2;
        _sloganText.VerticalAlignment = VerticalAlignment.Center;
        slogan.Children.Add(_sloganBar);
        Grid.SetColumn(_sloganText, 1);
        slogan.Children.Add(_sloganText);

        Grid.SetRow(header, 0);
        Grid.SetRow(slogan, 2);
        root.Children.Add(_watermark);
        root.Children.Add(header);
        root.Children.Add(slogan);
        return root;
    }

    private Grid BuildEmpty()
    {
        var root = new Grid { Margin = new Thickness(WidgetSpace.Pad) };
        root.Children.Add(_emptyView);
        return root;
    }

    private void ReadConfig(JsonElement? config)
    {
        ReadBgOverride(config);
        if (config is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }
        if (obj.TryGetProperty("class", out var v) && v.ValueKind == JsonValueKind.String)
        {
            _class = v.GetString() ?? "";
        }
        if (obj.TryGetProperty("school", out var v2) && v2.ValueKind == JsonValueKind.String)
        {
            _school = v2.GetString() ?? "";
        }
        if (obj.TryGetProperty("count", out var v3) && v3.ValueKind == JsonValueKind.String)
        {
            _count = v3.GetString() ?? "";
        }
        if (obj.TryGetProperty("slogan", out var v4) && v4.ValueKind == JsonValueKind.String)
        {
            _slogan = v4.GetString() ?? "";
        }
        if (obj.TryGetProperty("class_size", out var s1) && s1.ValueKind == JsonValueKind.Number)
        {
            _classSize = s1.GetInt32();
        }
        if (obj.TryGetProperty("school_size", out var s2) && s2.ValueKind == JsonValueKind.Number)
        {
            _schoolSize = s2.GetInt32();
        }
        if (obj.TryGetProperty("count_size", out var s3) && s3.ValueKind == JsonValueKind.Number)
        {
            _countSize = s3.GetInt32();
        }
        if (obj.TryGetProperty("slogan_size", out var s4) && s4.ValueKind == JsonValueKind.Number)
        {
            _sloganSize = s4.GetInt32();
        }
        if (obj.TryGetProperty("text_color", out var c) && c.ValueKind == JsonValueKind.String)
        {
            _textColor = c.GetString() ?? "";
        }
        if (obj.TryGetProperty("font_scale", out var f) && f.ValueKind == JsonValueKind.Number)
        {
            _fontScale = f.GetInt32();
        }
    }

    private bool IsConfigured => _class.Length > 0 || _school.Length > 0 || _count.Length > 0 || _slogan.Length > 0;

    private void UpdateInfo()
    {
        var configured = IsConfigured;
        _content.IsVisible = configured;
        _empty.IsVisible = !configured;
        if (!configured)
        {
            return;
        }

        _classText.Text = _class;
        _schoolText.Text = _school;
        _sloganText.Text = _slogan;
        _sloganText.IsVisible = _slogan.Length > 0;
        _sloganBar.IsVisible = _slogan.Length > 0;
        _badge.IsVisible = _class.Length > 0 || _school.Length > 0;
        _badgeText.Text = _class.Length > 0 ? _class[..1] : _school.Length > 0 ? _school[..1] : "";
        _watermark.Text = _badgeText.Text;
        _countText.Text = _count.Length > 0
            ? AppUtils.Tr("school_info_component.student_count", ("count", _count))
            : "";
        _countChip.IsVisible = _count.Length > 0;
    }

    private void ApplyStyle()
    {
        var dark = CardIsDark;
        var scale = _fontScale / 100.0;
        double S(double px) => Math.Max(1, px * scale);
        var p = WidgetSurfacePalette.Resolve(dark);

        // 自定义色优先 次级走 62% 令牌
        var textColor = TryParseColor(_textColor) ?? p.Text;
        var muted = new SolidColorBrush(textColor, CardInk.MutedOpacity);
        var faint = new SolidColorBrush(textColor, 0.07);

        _classText.Foreground = new SolidColorBrush(textColor);
        _classText.FontSize = Math.Clamp(S(_classSize), 20, 52);
        _classText.LetterSpacing = WidgetType.DisplaySpacing(_classText.FontSize);
        _schoolText.Foreground = muted;
        _schoolText.FontSize = Math.Clamp(S(_schoolSize), 12, 22);
        _countText.Foreground = muted;
        _countText.FontSize = Math.Clamp(S(_countSize), WidgetType.Micro, WidgetType.Headline);
        _sloganText.Foreground = new SolidColorBrush(textColor);
        _sloganText.FontSize = Math.Clamp(S(_sloganSize), 12, 20);
        _watermark.Foreground = faint;

        _badge.Background = CardAccent.Brush(0.16);
        _badgeText.Foreground = CardAccent.Brush();
        _countChip.Background = new SolidColorBrush(textColor, 0.055);
        _countChip.BorderBrush = new SolidColorBrush(textColor, 0.09);
        _countChip.BorderThickness = new Thickness(1);
        _sloganBar.Background = CardAccent.Brush(0.8);

        _emptyView.Apply(p, textColor);
    }
}
