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

// NavigationPage
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

public sealed class NavigationPage : Panel
{
    private readonly int _pageIndex;
    private readonly PageManager _pageManager;

    public NavigationPage(int pageIndex, PageManager pageManager)
    {
        _pageIndex = pageIndex;
        _pageManager = pageManager;
        RebuildItems();
    }

    public void RebuildItems()
    {
        Children.Clear();
        foreach (var itemElement in _pageManager.GetPageItems(_pageIndex))
        {
            try
            {
                var obj = itemElement.ValueKind == JsonValueKind.Object ? itemElement : default;
                var name = obj.ValueKind != default && obj.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var path = obj.ValueKind != default && obj.TryGetProperty("path", out var pathEl) ? pathEl.GetString() ?? "" : "";
                var cell = new Border
                {
                    CornerRadius = new CornerRadius(WidgetSpace.RadiusInner),
                    Width = 220,
                    Height = 72,
                    Padding = new Thickness(WidgetSpace.PadCompact, WidgetSpace.Gap),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = WidgetSpace.GapXs,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = name, FontWeight = WidgetWeight.Title, TextTrimming = TextTrimming.CharacterEllipsis },
                            new TextBlock { Text = path, FontSize = WidgetType.Label, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis },
                        },
                    },
                };
                cell.PointerReleased += (_, _) => LaunchItem(path);
                Children.Add(cell);
            }
            catch (Exception e)
            {
                Log.Warning($"[NavigationPage] 导航项解析失败: {e.Message}");
            }
        }
        InvalidateArrange();
    }

    private void LaunchItem(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        try
        {
            Log.Info($"[NavigationPage] 启动条目: {path}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception e)
        {
            Log.Warning($"[NavigationPage] 条目启动失败: {path} {e.Message}");
        }
    }

    // 返回尺寸必须有限(Canvas 父容器会给 Infinity 无穷可用尺寸 原样返回会触发 Invalid size 渲染崩溃)
    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsFinite(availableSize.Width) ? availableSize.Width : 1000;
        var columns = Math.Max(1, (int)(w / 240));
        foreach (var child in Children)
        {
            child.Measure(new Size(220, 72));
        }
        var rows = (Children.Count + columns - 1) / columns;
        return new Size(w, Math.Max(0, rows * 84));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var w = double.IsFinite(finalSize.Width) && finalSize.Width > 0 ? finalSize.Width : 1000;
        var h = double.IsFinite(finalSize.Height) && finalSize.Height > 0 ? finalSize.Height : 600;
        var columns = Math.Max(1, (int)(w / 240));
        for (var i = 0; i < Children.Count; i++)
        {
            var col = i % columns;
            var row = i / columns;
            Children[i].Arrange(new Rect(20 + col * 240, 20 + row * 84, 220, 72));
        }
        return new Size(w, h);
    }
}
