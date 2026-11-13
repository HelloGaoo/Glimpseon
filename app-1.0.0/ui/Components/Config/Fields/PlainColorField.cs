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

// PlainColorField
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

/// <summary>纯颜色字段(十六进制输入 + 色块预览; 未引 ColorPicker 包故不做取色器)</summary>
public sealed class PlainColorField : ConfigField
{
    private readonly string _default;
    private TextBox? _w;
    private Border? _swatch;

    public PlainColorField(string key, string label, string @default) : base(key, label) => _default = @default;

    public override Control Build()
    {
        _w = new TextBox { Text = _default, Width = 140 };
        _swatch = new Border
        {
            Width = 36,
            Height = 26,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Black, 0.2),
        };
        void Sync()
        {
            try
            {
                _swatch.Background = new SolidColorBrush(Color.Parse(_w.Text ?? _default));
            }
            catch (Exception e)
            {
                Log.Debug($"[CFG] 颜色解析失败: {e.Message}");
            }
        }
        _w.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Sync(); };

        var lbl = Label();
        lbl.Width = 80;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WidgetSpace.Gap, HorizontalAlignment = HorizontalAlignment.Right };
        sp.Children.Add(_w);
        sp.Children.Add(_swatch);
        var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("80,*,Auto") };
        row.Children.Add(lbl);
        Grid.SetColumn(sp, 2);
        row.Children.Add(sp);
        Sync();
        return row;
    }

    public override void Load(JsonObject c) => _w!.Text = GetStr(c, Key, _default);
    public override void Save(JsonObject r) => r[Key] = _w!.Text ?? _default;
    public override void Reset() => _w!.Text = _default;
}
