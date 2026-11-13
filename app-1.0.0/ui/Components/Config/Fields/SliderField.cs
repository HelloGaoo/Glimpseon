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

// SliderField
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

/// <summary>滑块字段</summary>
public sealed class SliderField : ConfigField
{
    private readonly int _default;
    private readonly double _min;
    private readonly double _max;
    private readonly string _suffix;
    private Slider? _w;
    private TextBlock? _val;

    public SliderField(string key, string label, int @default, double min, double max, string suffix = "") : base(key, label)
    {
        _default = @default;
        _min = min;
        _max = max;
        _suffix = suffix;
    }

    public override Control Build()
    {
        var top = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        top.Children.Add(Label());
        _val = new TextBlock
        {
            Text = $"{_default}{_suffix}",
            Width = 60,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(_val, 1);
        top.Children.Add(_val);

        _w = new Slider { Minimum = _min, Maximum = _max, Value = _default };
        _w.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && _val is not null)
            {
                _val.Text = $"{(int)Math.Round(_w.Value)}{_suffix}";
            }
        };

        var col = new StackPanel { Spacing = WidgetSpace.GapSm };
        col.Children.Add(top);
        col.Children.Add(_w);
        return col;
    }

    public override void Load(JsonObject c)
    {
        var v = GetInt(c, Key, _default);
        _w!.Value = v;
        if (_val is not null)
        {
            _val.Text = $"{v}{_suffix}";
        }
    }

    public override void Save(JsonObject r) => r[Key] = (int)Math.Round(_w!.Value);

    public override void Reset()
    {
        _w!.Value = _default;
        if (_val is not null)
        {
            _val.Text = $"{_default}{_suffix}";
        }
    }
}
