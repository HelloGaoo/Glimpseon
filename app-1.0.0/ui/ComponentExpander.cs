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

// 组件展开层 公用
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Glimpseon.Core;

namespace Glimpseon.UI;

public static class ComponentExpander
{
    public static void Show(Visual host, string title, Control content, double width = 1280, double height = 800)
    {
        var layer = OverlayLayer.GetOverlayLayer(host);
        if (layer is null)
        {
            Log.Warning($"[展开层] 宿主无 OverlayLayer: {title}");
            return;
        }

        var closed = false;
        var p = WidgetSurfacePalette.Resolve(ThemeSense.IsDark(host));
        var radius = Math.Max(8, Config.ComponentCardRadius.Value);
        var accent = Common.ParseAccentColor(Config.ThemeColor.Value);

        // 遮罩
        var scrim = new Border
        {
            Background = new SolidColorBrush(Colors.Black, 0.40),
            Opacity = 0,
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(160),
                    Easing = new CubicEaseOut(),
                },
            },
        };

        // 卡片
        var closeBtn = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(15),
            Content = FAIcons.IconGlyph("E8BB", 13),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        ToolTip.SetTip(closeBtn, AppUtils.Tr("common.close"));

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0),
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 48 };
        header.Children.Add(titleText);
        header.Children.Add(closeBtn);
        Grid.SetColumn(closeBtn, 1);

        var body = new ContentControl
        {
            Margin = new Thickness(14, 12, 14, 14),
            Content = content,
        };
        Grid.SetRow(body, 2);

        var divider = new Border
        {
            Background = new SolidColorBrush(p.Line),
        };
        Grid.SetRow(divider, 1);

        var card = new Border
        {
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1),
            Opacity = 0,
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = TransformOperations.Parse("scale(0.96)"),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,1,*"),
                Children = { header, divider, body },
            },
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(160),
                    Easing = new CubicEaseOut(),
                },
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(160),
                    Easing = new CubicEaseOut(),
                },
            },
        };

        card.Background = new SolidColorBrush(p.Card);
        card.BorderBrush = new SolidColorBrush(ThemeSense.IsDark(host) ? Colors.White : Colors.Black, ThemeSense.IsDark(host) ? 0.10 : 0.06);
        card.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty,
            new SolidColorBrush(p.Text));

        var root = new Panel { Children = { scrim, card } };
        root.Focusable = true;

        void SyncSize()
        {
            var b = layer.Bounds;
            scrim.Width = b.Width;
            scrim.Height = b.Height;
            card.Width = Math.Clamp(width, 320, Math.Max(320, b.Width - 48));
            card.Height = Math.Clamp(height, 240, Math.Max(240, b.Height - 48));
        }
        SyncSize();
        layer.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty)
            {
                SyncSize();
            }
        };

        async Task RemoveAsync()
        {
            await Task.Delay(180);
            try
            {
                layer.Children.Remove(root);
            }
            catch (Exception e)
            {
                Log.Debug($"[展开层] 移除失败: {e.Message}");
            }
        }

        void Close()
        {
            if (closed)
            {
                return;
            }
            closed = true;
            scrim.Opacity = 0;
            card.Opacity = 0;
            card.RenderTransform = TransformOperations.Parse("scale(0.96)");
            _ = RemoveAsync();
        }

        scrim.Tapped += (_, _) => Close();
        closeBtn.Click += (_, _) => Close();
        root.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };

        layer.Children.Add(root);
        root.Focus();
        Dispatcher.UIThread.Post(() =>
        {
            scrim.Opacity = 1;
            card.Opacity = 1;
            card.RenderTransform = TransformOperations.Parse("scale(1)");
        }, DispatcherPriority.Background);
        Log.Debug($"[展开层] 打开: {title} {width:F0}x{height:F0}");
    }
}
