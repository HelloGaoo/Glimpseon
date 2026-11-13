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

// SwitchField
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

/// <summary>开关字段</summary>
public sealed class SwitchField : ConfigField
{
    private readonly bool _default;
    private ToggleSwitch? _w;

    public SwitchField(string key, string label, bool @default) : base(key, label) => _default = @default;

    public override Control Build()
    {
        _w = new ToggleSwitch { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        row.Children.Add(Label());
        Grid.SetColumn(_w, 1);
        row.Children.Add(_w);
        return row;
    }

    public override void Load(JsonObject c) => _w!.IsChecked = GetBool(c, Key, _default);
    public override void Save(JsonObject r) => r[Key] = _w!.IsChecked == true;
    public override void Reset() => _w!.IsChecked = _default;
}
