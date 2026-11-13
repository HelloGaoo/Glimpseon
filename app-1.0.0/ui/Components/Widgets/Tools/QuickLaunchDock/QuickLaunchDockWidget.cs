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

// QuickLaunchDockWidget
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

/// <summary>快捷启动条</summary>
public sealed class QuickLaunchDockWidget : WidgetCardBase
{
    private readonly StackPanel _dock = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly CardEmptyState _empty = new(
        FAIcons.IconGlyph("E8A1", 20),
        AppUtils.Tr("quick_launch_component.empty_title"),
        AppUtils.Tr("quick_launch_component.empty_hint"));

    public QuickLaunchDockWidget(ComponentDefinition definition) : base(definition)
    {
        var host = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(WidgetSpace.Pad),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        host.Children.Add(_dock);
        host.Children.Add(_empty);
        Card.Child = host;
        Content = Card;
        UpdateApps();
        Config.ShowQuickLaunch.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchApps.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchIconSize.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchIconSpacing.ValueChanged += _ => UpdateApps();
        Config.QuickLaunchShowLabels.ValueChanged += _ => UpdateApps();
        Config.ThemeColor.ValueChanged += _ => UpdateApps();
        ActualThemeVariantChanged += (_, _) => UpdateApps();
        ApplyCardStyle();
    }

    private static List<QuickLaunchItem> ReadApps()
    {
        var list = new List<QuickLaunchItem>();
        var v = Config.QuickLaunchApps.Value;
        if (v.ValueKind != JsonValueKind.Array)
        {
            return list;
        }
        foreach (var item in v.EnumerateArray())
        {
            var app = new QuickLaunchItem
            {
                Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
            };
            if (app.Path.Length > 0)
            {
                list.Add(app);
            }
        }
        return list;
    }

    private void UpdateApps()
    {
        var apps = ReadApps();
        IsVisible = Config.ShowQuickLaunch.Value;
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        _dock.Children.Clear();
        var size = (double)Config.QuickLaunchIconSize.Value;
        var spacing = Config.QuickLaunchIconSpacing.Value;
        var showLabels = Config.QuickLaunchShowLabels.Value;
        if (apps.Count == 0)
        {
            _dock.IsVisible = false;
            _empty.IsVisible = true;
            UpdateEmptyStyle(p);
            return;
        }
        _dock.IsVisible = true;
        _empty.IsVisible = false;
        foreach (var app in apps)
        {
            var cell = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(spacing / 2.0, 0, spacing / 2.0, 0),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            var iconBorder = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size * 0.2),
                Background = new SolidColorBrush(CardIsDark
                    ? AvColor.FromRgb(50, 50, 50)
                    : AvColor.FromRgb(240, 240, 240)),
                Child = new TextBlock
                {
                    Text = app.Name.Length > 0 ? app.Name[..1].ToUpperInvariant() : "?",
                    FontSize = size * 0.42,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = CardIsDark ? Brushes.White : Brushes.DimGray,
                },
            };
            TrySetExeIcon(app.Path, iconBorder);
            cell.Children.Add(iconBorder);
            if (showLabels)
            {
                cell.Children.Add(new TextBlock
                {
                    Text = app.Name,
                    FontSize = WidgetType.Micro,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = size + 16,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, WidgetSpace.GapXs, 0, 0),
                    Foreground = CardInk.Fg(p),
                });
            }
            ToolTip.SetTip(cell, app.Path);
            var path = app.Path;
            cell.PointerReleased += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Warning($"[QLC] 启动失败: {ex.Message}");
                }
            };
            _dock.Children.Add(cell);
        }
    }

    private void UpdateEmptyStyle(WidgetSurfacePalette p)
    {
        _empty.Apply(p);
    }

    private void TrySetExeIcon(string path, Border target)
    {
        try
        {
            if (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return;
            }
            using var stream = new MemoryStream();
            icon.ToBitmap().Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            target.Child = new Image { Source = new Bitmap(stream), Width = target.Width, Height = target.Height };
        }
        catch (Exception e)
        {

            Log.Debug($"[QL] 图标提取失败: {e.Message}");
            // 图标提取失败保留字母占位
        }
    }
}
