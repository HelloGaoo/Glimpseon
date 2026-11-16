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

// WidgetCardBase
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;


namespace Glimpseon.UI;

/// <summary>卡片底座</summary>
public abstract class WidgetCardBase : DraggableContainer
{
    protected readonly Border Card;

    // 外观覆盖
    protected int? CfgRadius;
    private int? CfgOpacity;

    protected WidgetCardBase(ComponentDefinition definition)
    {
        Width = definition.DefaultWidth;
        Height = definition.DefaultHeight;
        // 像素对齐
        UseLayoutRounding = true;

        Card = new Border
        {
            BorderThickness = new Thickness(1),
            // 裁剪溢出
            ClipToBounds = true,
        };
        ApplyTypography();
        ApplyCardStyle();
        // 毛玻璃跟随几何
        GeometryChanged += RefreshGlass;
        // 主题切换重算
        ActualThemeVariantChanged += (_, _) => ApplyCardStyle();
    }

    private bool _configHooked;

    private void OnIntConfigChanged(int _) => ApplyCardStyle();

    private void OnStringConfigChanged(string _) => ApplyCardStyle();

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_configHooked)
        {
            return;
        }
        Config.ComponentCardOpacity.ValueChanged += OnIntConfigChanged;
        Config.ComponentCardRadius.ValueChanged += OnIntConfigChanged;
        Config.ComponentCardColor.ValueChanged += OnStringConfigChanged;
        Config.ComponentTextColor.ValueChanged += OnStringConfigChanged;
        Config.ComponentCardShadow.ValueChanged += OnIntConfigChanged;
        Config.ComponentCardBlur.ValueChanged += OnIntConfigChanged;
        Config.ComponentCardBorder.ValueChanged += OnIntConfigChanged;
        _configHooked = true;
        ApplyCardStyle();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_configHooked)
        {
            Config.ComponentCardOpacity.ValueChanged -= OnIntConfigChanged;
            Config.ComponentCardRadius.ValueChanged -= OnIntConfigChanged;
            Config.ComponentCardColor.ValueChanged -= OnStringConfigChanged;
            Config.ComponentTextColor.ValueChanged -= OnStringConfigChanged;
            Config.ComponentCardShadow.ValueChanged -= OnIntConfigChanged;
            Config.ComponentCardBlur.ValueChanged -= OnIntConfigChanged;
            Config.ComponentCardBorder.ValueChanged -= OnIntConfigChanged;
            _configHooked = false;
        }
        base.OnDetachedFromVisualTree(e);
    }

    // 文本基线
    private void ApplyTypography()
    {
        Card.SetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty, WidgetFonts.Ui);
        Card.SetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty, WidgetType.Body);
        Card.SetValue(Avalonia.Controls.Documents.TextElement.FontWeightProperty, WidgetWeight.Body);
        // 等宽数字
        Card.SetValue(Avalonia.Controls.Documents.TextElement.FontFeaturesProperty,
            new FontFeatureCollection(new[] { FontFeature.Parse("tnum") }));
    }

    // 注入组件配置
    public void ApplyComponentConfig(JsonElement? config)
    {
        ReadBgOverride(config);
        ApplyCardStyle();
    }

    // 读取外观覆盖
    protected void ReadBgOverride(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }
        if (obj.TryGetProperty("corner_radius", out var r) && r.ValueKind == JsonValueKind.Number)
        {
            CfgRadius = r.GetInt32();
        }
        if (obj.TryGetProperty("bg_opacity", out var o) && o.ValueKind == JsonValueKind.Number)
        {
            CfgOpacity = Math.Clamp(o.GetInt32(), 0, 100);
        }
    }

    protected double EffectiveRadius =>
        CfgRadius is { } r ? Math.Max(8, r) : Math.Max(8, Config.ComponentCardRadius.Value);

    // 卡片明暗
    internal bool CardIsDark { get; private set; }

    // 毛玻璃画刷复用
    private ImageBrush? _glassBrush;

    // 卡片表面
    protected virtual void ApplyCardStyle()
    {
        var themeDark = WindowThemeDark();
        var custom = TryParseColor(Config.ComponentCardColor.Value);
        var alpha = Math.Clamp((CfgOpacity ?? Config.ComponentCardOpacity.Value) / 100.0, 0, 1);
        var surface = custom ?? WidgetSurfacePalette.Resolve(themeDark).Card;
        var blur = Config.ComponentCardBlur.Value;
        var glass = blur > 0 && HomeView.GlassBackdrop is not null;

        if (glass)
        {
            // 壁纸切片
            _glassBrush ??= new ImageBrush { Stretch = Stretch.Fill };
            _glassBrush.Source = HomeView.GlassBackdrop;
            Card.Background = _glassBrush;
            CardIsDark = true;
            RefreshGlass();
        }
        else
        {
            // 透明兜底
            CardIsDark = custom is null || alpha < 0.15 ? themeDark : Luminance(surface) < 128;
            var a = (byte)Math.Round(alpha * 255);
            if (custom is null)
            {
                Card.Background = new SolidColorBrush(Color.FromArgb(a, surface.R, surface.G, surface.B));
            }
            else
            {
                // 同色渐变
                var top = Common.ShiftColor(surface, CardIsDark ? 0.07 : 0.10);
                var bottom = Common.ShiftColor(surface, CardIsDark ? -0.10 : -0.05);
                Card.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(a, top.R, top.G, top.B), 0),
                        new GradientStop(Color.FromArgb(a, bottom.R, bottom.G, bottom.B), 1),
                    },
                };
            }
        }

        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var borderMode = Config.ComponentCardBorder.Value;
        var edgeColor = CardIsDark ? Colors.White : Colors.Black;
        var accent = Common.ParseAccentColor(Config.ThemeColor.Value);

        var borderWidth = borderMode switch { 0 => 0.0, 2 => 1.6, 3 => 1.4, _ => 1 };
        Card.BorderThickness = new Thickness(borderWidth);
        Card.BorderBrush = borderMode switch
        {
            3 => new SolidColorBrush(accent, 0.90),
            4 => new SolidColorBrush(edgeColor, CardIsDark ? (glass ? 0.45 : 0.34) : 0.22),
            2 => new SolidColorBrush(edgeColor, CardIsDark ? (glass ? 0.30 : 0.24) : 0.16),
            _ => new SolidColorBrush(edgeColor, glass ? 0.10 : CardIsDark ? 0.09 : 0.07),
        };
        Card.CornerRadius = new CornerRadius(EffectiveRadius);
        CardRadius = EffectiveRadius;

        // 形态阴影
        Card.BoxShadow = borderMode switch
        {
            3 => new BoxShadows(
                new BoxShadow { Blur = 24, Spread = 3, Color = Color.FromArgb(84, accent.R, accent.G, accent.B) },
                new[] { new BoxShadow { Blur = 6, Color = Color.FromArgb(140, accent.R, accent.G, accent.B) } }),
            // 双框外线
            4 => new BoxShadows(
                new BoxShadow { OffsetY = 0, Blur = 0, Spread = 3, Color = Color.FromArgb(58, 0, 0, 0) },
                new[] { new BoxShadow { OffsetY = 6, Blur = 16, Color = Color.FromArgb(70, 0, 0, 0) } }),
            _ => Config.ComponentCardShadow.Value switch
            {
                2 => new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 2, Color = Color.FromArgb(150, 0, 0, 0) }),
                1 => new BoxShadows(new BoxShadow
                {
                    OffsetY = 6,
                    Blur = 20,
                    Color = Color.FromArgb(glass || CardIsDark ? (byte)78 : (byte)40, 0, 0, 0),
                }),
                _ => default,
            },
        };
        // 文字色
        Card.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty,
            new SolidColorBrush(TryParseColor(Config.ComponentTextColor.Value) ?? p.Text));

        // 通知子组件
        AppearanceChanged?.Invoke();
    }

    /// <summary>外观变更</summary>
    public event Action? AppearanceChanged;

    // 纯白视为未设置
    protected static Color? ConfiguredInk(string? hex)
    {
        var c = TryParseColor(hex);
        return c is { R: 255, G: 255, B: 255 } ? null : c;
    }

    // 窗口主题
    private bool WindowThemeDark()
    {
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            return top.ActualThemeVariant == ThemeVariant.Dark;
        }
        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    }

    // 对齐壁纸切片
    private void RefreshGlass()
    {
        if (_glassBrush?.Source is null)
        {
            return;
        }
        if (TopLevel.GetTopLevel(this) is not { } tl)
        {
            return;
        }
        var winW = tl.Bounds.Width;
        var winH = tl.Bounds.Height;
        if (winW <= 1 || winH <= 1)
        {
            return;
        }
        var origin = this.TranslatePoint(new Point(0, 0), tl);
        if (origin is null)
        {
            return;
        }
        var x = Math.Clamp(origin.Value.X / winW, 0, 0.999);
        var y = Math.Clamp(origin.Value.Y / winH, 0, 0.999);
        var w = Math.Clamp(Math.Max(1, VisualWidth) / winW, 0.001, 1 - x);
        var h = Math.Clamp(Math.Max(1, VisualHeight) / winH, 0.001, 1 - y);
        _glassBrush.SourceRect = new RelativeRect(x, y, w, h, RelativeUnit.Relative);
    }

    private static double Luminance(Color c) => c.R * 0.299 + c.G * 0.587 + c.B * 0.114;

    // 解析色值
    protected static Color? TryParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }
        try
        {
            return Color.Parse(hex);
        }
        catch
        {
            return null;
        }
    }

    // 读取布尔配置
    protected static bool ReadBool(JsonElement? config, string key, bool fallback)
    {
        if (config is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty(key, out var v))
        {
            return v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback,
            };
        }
        return fallback;
    }

    // 读取字符串配置
    protected static string ReadString(JsonElement? config, string key, string fallback)
    {
        if (config is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.String
            && v.GetString() is { } text
            && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }
        return fallback;
    }

    // 读取原始字符串
    protected static string ReadRawString(JsonElement? config, string key, string fallback)
    {
        if (config is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.String)
        {
            return v.GetString() ?? fallback;
        }
        return fallback;
    }

    // 读取数值配置
    protected static double ReadNumber(JsonElement? config, string key, double fallback)
    {
        if (config is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetDouble(out var number))
        {
            return number;
        }
        return fallback;
    }

    // 展开层

    /// <summary>启用展开层</summary>
    protected void EnableExpansion(string title, Func<Control> buildContent)
    {
        var btn = new Button
        {
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(13),
            Content = FAIcons.IconGlyph("E8A7", 12),
            Opacity = 0.55,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
        };
        ToolTip.SetTip(btn, title);
        // 拦截拖拽
        btn.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble);
        btn.Click += (_, _) => ComponentExpander.Show(this, title, buildContent());

        // 防双父级
        var original = Card.Child;
        Card.Child = null;
        var host = new Grid();
        if (original is not null)
        {
            host.Children.Add(original);
        }
        host.Children.Add(btn);
        Card.Child = host;
    }
}
