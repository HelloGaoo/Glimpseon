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

// ComponentLibraryWindow
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

public sealed class ComponentLibraryWindow : GlimpseonWindow
{
    private readonly ComponentRegistry _registry;
    private Point _pressPoint;
    private ComponentDefinition? _draggingDefinition;
    private bool _dragStarted;
    private PointerPressedEventArgs? _pressedArgs;
    private readonly List<(ComponentDefinition Def, Border Host)> _pendingPreviews = new();

    // 双击卡片直接添加(由 HomeView 注入
    public Action<string>? DefinitionActivated;

    public ComponentLibraryWindow(ComponentRegistry registry)
    {
        _registry = registry;
        Title = AppUtils.Tr("component_library.title");
        Width = 780;
        Height = 640;
        MinWidth = 560;
        MinHeight = 420;

        var scroll = new ScrollViewer { Padding = new Thickness(WidgetSpace.Pad, WidgetSpace.PadCompact, WidgetSpace.Pad, WidgetSpace.Pad) };
        var stack = new StackPanel { Spacing = WidgetSpace.Gap };
        scroll.Content = stack;

        // 先铺骨架卡片(名称立即可见 预览区空) 窗口秒开 真身后台逐个填充
        foreach (var category in _registry.GetCategories())
        {
            var defs = _registry.GetDefinitionsByCategory(category).ToList();
            if (defs.Count == 0)
            {
                continue;
            }
            stack.Children.Add(new TextBlock
            {
                Text = $"{category} · {defs.Count}",
                FontSize = WidgetType.Title,
                FontWeight = WidgetWeight.Title,
                Opacity = 0.88,
                Margin = new Thickness(4, 8, 0, 8),
            });
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var def in defs)
            {
                wrap.Children.Add(BuildSkeletonCard(def));
            }
            stack.Children.Add(wrap);
        }
        // 与主窗口一致: 48px 自绘标题栏行(图标+标题+版本) + 内容区 (GlimpseonWindow 已 ExtendsContentIntoTitleBar)
        var titleGrid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("Auto,Auto,*,Auto"),
            Margin = new Thickness(12, 0, 0, 0),
        };
        var titleIcon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 2, 0, 0) };
        var titleText = new TextBlock
        {
            Text = AppUtils.Tr("component_library.title"),
            FontSize = WidgetType.Label,
            Margin = new Thickness(10, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.85,
        };
        var titleVersion = new TextBlock
        {
            Text = Paths.Version,
            FontSize = WidgetType.Label,
            Margin = new Thickness(0, 2, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Opacity = 0.55,
        };
        var titleCaptionReserve = new Border { Width = 140 };
        Grid.SetColumn(titleIcon, 0);
        Grid.SetColumn(titleText, 1);
        Grid.SetColumn(titleVersion, 2);
        Grid.SetColumn(titleCaptionReserve, 3);
        titleGrid.Children.Add(titleIcon);
        titleGrid.Children.Add(titleText);
        titleGrid.Children.Add(titleVersion);
        titleGrid.Children.Add(titleCaptionReserve);
        try
        {
            var iconPath = Paths.GetResourcePath(Constants.AppIcon);
            if (File.Exists(iconPath))
            {
                var bitmap = new Bitmap(iconPath);
                Icon = bitmap;
                titleIcon.Source = bitmap;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[组件库] 窗口图标加载失败: {e.Message}");
        }
        var titleRow = new Border { Height = 48, Child = titleGrid, Margin = new Thickness(0, 0, 8, 0) };
        var root = new Grid { RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("Auto,*") };
        root.Children.Add(titleRow);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
        _ = FillPreviewsAsync();
    }

    // 后台逐卡填充真身预览 每张卡之间让出 UI 线程(重型的 HTML 表盘排最后)
    private async Task FillPreviewsAsync()
    {
        var ordered = _pendingPreviews
            .OrderBy(pair => pair.Def.Id.StartsWith("clock_square") ? 1 : 0)
            .ToList();
        foreach (var (def, host) in ordered)
        {
            await Task.Delay(30);
            try
            {
                host.Child = BuildLivePreview(def);
            }
            catch (Exception e)
            {
                Log.Warning($"[组件库] 预览填充失败 {def.Id}: {e.Message}\n{e.StackTrace}");
                host.Child = MakeFallbackPreview(def);
            }
        }
        Log.Info($"[组件库] 预览填充完成 共{ordered.Count}张");
    }

    private static Control MakeFallbackPreview(ComponentDefinition def)
    {
        return new TextBlock
        {
            Text = def.DisplayName,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = WidgetType.Label,
            Opacity = 0.6,
        };
    }

    // 骨架卡片: 名称立即可见 预览区显示加载占位
    private Control BuildSkeletonCard(ComponentDefinition def)
    {
        const double boxW = 196;
        const double boxH = 136;

        var host = new Border
        {
            Width = boxW,
            Height = boxH,
            CornerRadius = new CornerRadius(8),
            Background = PreviewBackdrop(),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Avalonia.Media.Colors.White, 0.07),
            ClipToBounds = true,
            Child = new TextBlock
            {
                Text = "…",
                FontSize = WidgetType.Headline,
                Opacity = 0.35,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        _pendingPreviews.Add((def, host));

        var nameText = new TextBlock
        {
            Text = def.DisplayName,
            FontSize = WidgetType.Label,
            FontWeight = WidgetWeight.Emphasis,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 2),
        };

        var metaText = new TextBlock
        {
            Text = $"{def.Category} · {def.DefaultWidth:0}×{def.DefaultHeight:0}",
            FontSize = WidgetType.Micro,
            Opacity = 0.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var card = new Border
        {
            Width = 212,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(WidgetSpace.Gap),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Avalonia.Media.Colors.Gray, 0.10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Avalonia.Media.Colors.Gray, 0.22),
            Child = new StackPanel { Children = { host, nameText, metaText } },
        };

        card.PointerPressed += (_, e) =>
        {
            _pressPoint = e.GetPosition(this);
            _dragStarted = false;
            _pressedArgs = e;
            _draggingDefinition = def;
        };
        card.PointerMoved += async (_, e) =>
        {
            if (_dragStarted || _draggingDefinition is null || _pressedArgs is null)
            {
                return;
            }
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }
            var pos = e.GetPosition(this);
            // 任一轴越过阈值即启动拖拽(用 || 会要求双轴同时超 8px 单方向拖动永远无效)
            if (Math.Abs(pos.X - _pressPoint.X) < 8 && Math.Abs(pos.Y - _pressPoint.Y) < 8)
            {
                return;
            }
            _dragStarted = true;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(_draggingDefinition.Id));
            await DragDrop.DoDragDropAsync(_pressedArgs, transfer, DragDropEffects.Copy);
            _draggingDefinition = null;
            _pressedArgs = null;
        };
        card.PointerReleased += (_, _) =>
        {
            _pressedArgs = null;
            _draggingDefinition = null;
        };
        card.DoubleTapped += (_, _) => DefinitionActivated?.Invoke(def.Id);

        return card;
    }

    // 预览底色 中性深蓝渐变 不继承用户高饱和配色
    private static IBrush PreviewBackdrop() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#1E2836"), 0),
            new GradientStop(Color.Parse("#3A4C62"), 1),
        },
    };

    // 真控件实例缩放为主体(实时渲染 非图片)
    private Control BuildLivePreview(ComponentDefinition def)
    {
        var naturalW = def.DefaultWidth;
        var naturalH = def.DefaultHeight;
        const double boxW = 196;
        const double boxH = 136;
        var scale = Math.Min(Math.Min(boxW / naturalW, boxH / naturalH), 1.0);

        // 组件已全部原生化 真控件实例直接预览
        Control widget;
        try
        {
            widget = StyleWidgetFactory.Create(def.Id, def, def.DefaultConfig)
                     ?? new PlaceholderWidget(def, def.DefaultConfig);
        }
        catch (Exception e)
        {
            Log.Warning($"[组件库] 预览创建失败 {def.Id}: {e.Message}\n{e.StackTrace}");
            try
            {
                widget = new PlaceholderWidget(def, def.DefaultConfig);
            }
            catch (Exception e2)
            {
                Log.Warning($"[组件库] 占位预览也失败 {def.Id}: {e2.Message}");
                return MakeFallbackPreview(def);
            }
        }
        widget.IsHitTestVisible = false; // 预览不接交互 拖拽由卡片接管
        widget.Opacity = 0.98;
        // Avalonia 12 缩放经 LayoutTransformControl
        return new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = widget };
    }
}
