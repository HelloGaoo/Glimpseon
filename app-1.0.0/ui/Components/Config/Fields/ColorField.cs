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

// ColorField
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

/// <summary>颜色字段(跟随不透明度/自定义颜色 + 颜色值)</summary>
public sealed class ColorField : ConfigField
{
    private readonly string _defaultMode;
    private readonly string _defaultColor;
    private ComboBox? _combo;
    private PlainColorField? _picker;
    private Control? _pickerRow;

    public ColorField(string key, string label, string defaultMode, string defaultColor) : base(key, label)
    {
        _defaultMode = defaultMode;
        _defaultColor = defaultColor;
    }

    public override Control Build()
    {
        _combo = new ComboBox { Width = 130 };
        _combo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("component_edit.bg_mode_opacity"), Tag = "opacity" });
        _combo.Items.Add(new ComboBoxItem { Content = AppUtils.Tr("component_edit.bg_mode_custom"), Tag = "custom" });
        _combo.SelectedIndex = _defaultMode == "custom" ? 1 : 0;

        _picker = new PlainColorField(Key + "_color", "", _defaultColor);
        _pickerRow = _picker.Build();
        _pickerRow.IsVisible = _combo.SelectedIndex == 1;
        _combo.SelectionChanged += (_, _) => _pickerRow.IsVisible = _combo.SelectedIndex == 1;

        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WidgetSpace.Gap };
        sp.Children.Add(_combo);
        var col = new StackPanel { Spacing = WidgetSpace.GapSm };
        col.Children.Add(sp);
        col.Children.Add(_pickerRow);
        return col;
    }

    public override void Load(JsonObject c)
    {
        _combo!.SelectedIndex = GetStr(c, Key + "_mode", _defaultMode) == "custom" ? 1 : 0;
        _picker!.Load(c);
    }

    public override void Save(JsonObject r)
    {
        r[Key + "_mode"] = _combo!.SelectedIndex == 1 ? "custom" : "opacity";
        _picker!.Save(r);
    }

    public override void Reset()
    {
        _combo!.SelectedIndex = _defaultMode == "custom" ? 1 : 0;
        _picker!.Reset();
    }
}
