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

// ComponentPreviewRenderer
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Glimpseon.Core;

namespace Glimpseon.UI;

/// <summary>离屏组件缩略图</summary>
internal sealed class ComponentPreviewRenderer : IDisposable
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static ThemeVariant? _cacheTheme;
    private static int _generation;
    private Window? _host;
    private Canvas? _stage;

    public async Task<Bitmap?> GetAsync(ComponentDefinition definition)
    {
        var theme = Application.Current?.ActualThemeVariant;
        if (theme != _cacheTheme)
        {
            Clear();
            _cacheTheme = theme;
        }
        if (Cache.TryGetValue(definition.Id, out var cached))
        {
            return cached;
        }
        var generation = _generation;
        RenderTargetBitmap? bitmap = null;
        Control? widget = null;
        using var preview = PreviewSamples.Begin();
        try
        {
            var width = Math.Max(1, definition.DefaultWidth);
            var height = Math.Max(1, definition.DefaultHeight);
            EnsureHost();
            widget = Build(definition);
            widget.Width = width;
            widget.Height = height;
            _stage!.Children.Add(widget);
            widget.Measure(new Size(width, height));
            widget.Arrange(new Rect(0, 0, width, height));
            ClearTransitions(widget);
            // 等布局稳定
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            widget.Measure(new Size(width, height));
            widget.Arrange(new Rect(0, 0, width, height));
            bitmap = new RenderTargetBitmap(
                new PixelSize(Math.Max(1, (int)Math.Ceiling(width)), Math.Max(1, (int)Math.Ceiling(height))),
                new Vector(96, 96));
            bitmap.Render(widget);
        }
        catch (Exception e)
        {
            bitmap?.Dispose();
            bitmap = null;
            Log.Warning($"[组件库] 缩略图生成失败 {definition.Id}: {e.Message}");
        }
        finally
        {
            if (widget is not null)
            {
                _stage?.Children.Remove(widget);
            }
        }
        if (generation != _generation)
        {
            bitmap?.Dispose();
            return null;
        }
        Cache[definition.Id] = bitmap;
        return bitmap;
    }

    public void Clear()
    {
        _generation++;
        foreach (var bitmap in Cache.Values)
        {
            bitmap?.Dispose();
        }
        Cache.Clear();
    }

    public void Dispose()
    {
        _stage?.Children.Clear();
        _stage = null;
        _host?.Close();
        _host = null;
    }

    private static void ClearTransitions(Control root)
    {
        root.Transitions = null;
        foreach (var visual in root.GetVisualDescendants().OfType<Control>())
        {
            visual.Transitions = null;
        }
    }

    private void EnsureHost()
    {
        if (_host is not null)
        {
            return;
        }
        _stage = new Canvas();
        _host = new Window
        {
            Width = 1,
            Height = 1,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-32000, -32000),
            Opacity = 0.01,
            Background = Brushes.Transparent,
            Content = _stage,
        };
        _host.Show();
    }

    private static Control Build(ComponentDefinition definition)
    {
        try
        {
            var config = PreviewSamples.ConfigFor(definition.Id) ?? definition.DefaultConfig;
            return StyleWidgetFactory.Create(definition.Id, definition, config)
                   ?? new PlaceholderWidget(definition, config);
        }
        catch (Exception e)
        {
            Log.Warning($"[组件库] 预览创建失败 {definition.Id}: {e.Message}");
        }

        var icon = FAIcons.IconByName(definition.Icon, 24);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.Foreground = new SolidColorBrush(Colors.White, 0.7);
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#232C39")),
            Child = new StackPanel
            {
                Spacing = WidgetSpace.GapXs,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    icon,
                    new TextBlock
                    {
                        Text = definition.DisplayName,
                        FontSize = WidgetType.Label,
                        Foreground = new SolidColorBrush(Colors.White, 0.8),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };
    }
}
