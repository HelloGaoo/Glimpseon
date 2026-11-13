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

// StickyNoteWidget
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

/// <summary>便签</summary>
public sealed class StickyNoteWidget : WidgetCardBase
{
    // 便签只保留一个强调色 纸面走全局卡片皮肤 不再自绘纯色底
    private static readonly Dictionary<string, string> Colors = new()
    {
        ["yellow"] = "#E7C56A",
        ["green"] = "#A9C48A",
        ["blue"] = "#93B7D6",
        ["pink"] = "#DDA3AD",
        ["orange"] = "#E2B183",
        ["purple"] = "#B7A6D0",
    };

    private readonly Border _header = new() { Height = 40 };
    private readonly Border _dot = new() { Width = 18, Height = 3, CornerRadius = new CornerRadius(1.5) };
    private readonly TextBlock _date = new()
    {
        Text = DateTime.Today.ToString("yyyy-MM-dd"),
        FontSize = WidgetType.Label,
        LetterSpacing = 0.2,
        FontFamily = WidgetFonts.Ui,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBox _editor = new()
    {
        AcceptsReturn = true,
        AcceptsTab = false,
        TextWrapping = TextWrapping.Wrap,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        FontSize = WidgetType.Title,
        Padding = new Thickness(WidgetSpace.Pad, WidgetSpace.Gap, WidgetSpace.Pad, WidgetSpace.PadCompact),
        PlaceholderText = AppUtils.Tr("sticky_note.placeholder"),
        FontFamily = WidgetFonts.Ui,
        LineHeight = 22,
        VerticalContentAlignment = VerticalAlignment.Top,
        MinHeight = 0,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private string _c = "#E7C56A";
    private readonly NoteRules _rules = new()
    {
        IsHitTestVisible = false,
        Spacing = 22,
        Margin = new Thickness(WidgetSpace.Pad, WidgetSpace.Gap, WidgetSpace.Pad, WidgetSpace.PadCompact),
    };
    private readonly TextBlock _count = new()
    {
        Text = "0 字",
        FontSize = WidgetType.Micro,
        FontFamily = WidgetFonts.Ui,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, WidgetSpace.Pad, WidgetSpace.GapSm),
        IsHitTestVisible = false,
        Opacity = 0,
    };
    private string? _dataFile;
    private readonly DispatcherTimer _saveTimer;

    public StickyNoteWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        var colorKey = "yellow";
        if (config is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty("color", out var c)
            && c.ValueKind == JsonValueKind.String)
        {
            colorKey = c.GetString() ?? "yellow";
        }
        _c = Colors.GetValueOrDefault(colorKey, Colors["yellow"]);

        // 覆盖 Fluent TextBox 各状态底色 便签纸面必须透出卡片皮肤
        foreach (var key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver",
                     "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
                 })
        {
            _editor.Resources[key] = Brushes.Transparent;
        }
        foreach (var key in new[]
                 {
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver",
                     "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled",
                 })
        {
            _editor.Resources[key] = Brushes.Transparent;
        }

        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(WidgetSpace.Pad, 0, WidgetSpace.Pad, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = WidgetSpace.GapXs,
        };
        headerRow.Children.Add(_dot);
        headerRow.Children.Add(_date);
        _header.Child = headerRow;

        var root = new Grid
        {
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*"),
        };
        Grid.SetRow(_header, 0);
        Grid.SetRow(_rules, 1);
        Grid.SetRow(_editor, 1);
        Grid.SetRow(_count, 1);
        root.Children.Add(_header);
        root.Children.Add(_rules);
        root.Children.Add(_editor);
        root.Children.Add(_count);
        Card.Child = root;
        Content = Card;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _editor.TextChanged += (_, _) =>
        {
            UpdateCount();
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        // 计数只在悬停或聚焦时出现 不打扰书写
        _editor.GotFocus += (_, _) => _count.Opacity = 1;
        _editor.LostFocus += (_, _) => _count.Opacity = 0;
        PointerEntered += (_, _) => _count.Opacity = 1;
        PointerExited += (_, _) =>
        {
            if (!_editor.IsFocused)
            {
                _count.Opacity = 0;
            }
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Save();
        };
        ApplyStickyStyle();
        UpdateCount();
    }

    private void UpdateCount()
    {
        var count = _editor.Text?.Length ?? 0;
        _count.Text = AppUtils.Tr("sticky_note.char_count", ("count", count));
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_dataFile is null && !string.IsNullOrEmpty(ComponentId))
        {
            _dataFile = IOPath.Combine(Paths.DataUser, "notes", $"{ComponentId}.json");
            try
            {
                if (File.Exists(_dataFile)
                    && JsonNode.Parse(File.ReadAllText(_dataFile)) is JsonObject obj)
                {
                    _editor.Text = (string?)obj["text"] ?? "";
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Note] 加载便签失败: {ex.Message}");
            }
        }
    }

    private void Save()
    {
        try
        {
            if (_dataFile is null)
            {
                return;
            }
            Directory.CreateDirectory(IOPath.GetDirectoryName(_dataFile)!);
            File.WriteAllText(_dataFile, new JsonObject
            {
                ["text"] = _editor.Text ?? "",
            }.ToJsonString());
        }
        catch (Exception e)
        {
            Log.Warning($"[Note] 保存便签失败: {e.Message}");
        }
    }

    // 纸面跟随全局卡片皮肤 便签色只做强调
    protected override void ApplyCardStyle()
    {
        base.ApplyCardStyle();
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var header = AvColor.Parse(_c);
        _dot.Background = new SolidColorBrush(header);
        _date.Foreground = WidgetInk.Muted(p.Text);
        _header.BorderBrush = new SolidColorBrush(p.Line);
        _header.BorderThickness = new Thickness(0, 0, 0, 1);
        _rules.Line = p.Text;
        _rules.IsVisible = !CardIsDark;
        _rules.InvalidateVisual();
        _editor.Foreground = new SolidColorBrush(p.Text);
        _editor.CaretBrush = new SolidColorBrush(p.Text);
        _editor.PlaceholderForeground = new SolidColorBrush(p.Text, CardInk.FaintOpacity);
        _editor.SelectionBrush = new SolidColorBrush(header, 0.30);
        _editor.SelectionForegroundBrush = new SolidColorBrush(p.Text);
        _count.Foreground = WidgetInk.Muted(p.Text);
    }

    private void ApplyStickyStyle() => ApplyCardStyle();
}
