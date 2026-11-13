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

// ConfigField
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

// 组件配置面板
// 字段体系对齐原版: _SwitchField/_SpinField/_ComboField/_TextField/_SliderField/_ColorField/_DateField/_PlainColorField
// 各组件专属字段定义(_basic_defs)按用户要求暂留空; 进阶页沿用原版默认(外观 + 字体)

/// <summary>配置字段基类</summary>
public abstract class ConfigField
{
    protected readonly string Key;
    protected readonly string LabelText;

    protected ConfigField(string key, string labelText)
    {
        Key = key;
        LabelText = labelText;
    }

    public abstract Control Build();
    public abstract void Load(JsonObject config);
    public abstract void Save(JsonObject result);
    public abstract void Reset();

    protected static JsonValue? Node(JsonObject o, string k) =>
        o.TryGetPropertyValue(k, out var n) ? n as JsonValue : null;

    protected static bool GetBool(JsonObject o, string k, bool def) =>
        Node(o, k) is { } v && v.TryGetValue(out bool b) ? b : def;

    protected static int GetInt(JsonObject o, string k, int def)
    {
        if (Node(o, k) is not { } v)
        {
            return def;
        }
        if (v.TryGetValue(out int i))
        {
            return i;
        }
        return v.TryGetValue(out double d) ? (int)Math.Round(d) : def;
    }

    protected static double GetNum(JsonObject o, string k, double def)
    {
        if (Node(o, k) is not { } v)
        {
            return def;
        }
        if (v.TryGetValue(out double d))
        {
            return d;
        }
        return v.TryGetValue(out int i) ? i : def;
    }

    protected static string GetStr(JsonObject o, string k, string def) =>
        Node(o, k) is { } v && v.TryGetValue(out string? s) && s is not null ? s : def;

    protected TextBlock Label() => new()
    {
        Text = LabelText,
        FontSize = WidgetType.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
