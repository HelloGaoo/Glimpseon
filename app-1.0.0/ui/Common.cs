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

// 公共 ui 工具
using System.IO;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using FluentAvalonia.Styling;
using Glimpseon.Core;

namespace Glimpseon.UI;

// 深浅色判断
internal static class ThemeSense
{
    public static bool IsDark(StyledElement e)
    {
        if (e.ActualThemeVariant == ThemeVariant.Dark)
        {
            return true;
        }
        if (e.ActualThemeVariant == ThemeVariant.Light)
        {
            return false;
        }
        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    }
}

public static class FAIcons
{
    public static readonly FontFamily SymbolsFont = new("avares://FluentAvalonia/Fonts#Symbols");

    public static FAFontIcon Icon(FASymbol symbol, double fontSize = 16) => new()
    {
        FontFamily = SymbolsFont,
        Glyph = char.ConvertFromUtf32((int)symbol),
        FontSize = fontSize,
    };

    public static FAFontIcon IconByName(string? name, double fontSize = 16)
    {
        var symbol = Enum.TryParse<FASymbol>(name, out var parsed) ? parsed : FASymbol.Edit;
        return Icon(symbol, fontSize);
    }

    public static FAFontIcon IconCenter(FASymbol symbol, double fontSize = 16)
    {
        var icon = Icon(symbol, fontSize);
        icon.VerticalAlignment = VerticalAlignment.Center;
        return icon;
    }

    public static FAFontIconSource IconSource(FASymbol symbol) => new()
    {
        FontFamily = SymbolsFont,
        Glyph = char.ConvertFromUtf32((int)symbol),
    };

    // 码位直引 枚举缺字形时用
    public static FAFontIcon IconGlyph(string hexCode, double fontSize = 16) => new()
    {
        FontFamily = SymbolsFont,
        Glyph = char.ConvertFromUtf32(Convert.ToInt32(hexCode, 16)),
        FontSize = fontSize,
    };

    public static FAFontIconSource IconSourceGlyph(string hexCode) => new()
    {
        FontFamily = SymbolsFont,
        Glyph = char.ConvertFromUtf32(Convert.ToInt32(hexCode, 16)),
    };

    // 图标前景色 字体图标
    public static void SetIconBrush(Control icon, IBrush brush)
    {
        if (icon is FAFontIcon fi)
        {
            fi.Foreground = brush;
        }
        else if (icon is TextBlock tb)
        {
            tb.Foreground = brush;
        }
    }

    public static FAFontIcon CursorIcon(double fontSize = 16) => new()
    {
        FontFamily = SymbolsFont,
        Glyph = "\uE8B0",
        FontSize = fontSize,
    };
}

public static class Common
{
    // 圆角卡片容器
    public static Border MakeCard()
    {
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Avalonia.Media.Colors.Gray, 0.25),
            Background = new SolidColorBrush(Avalonia.Media.Colors.Gray, 0.08),
        };
    }

    public static readonly FontFamily AppFontFamily = new(
        "avares://GlimpseonMain/Assets/font/HarmonyOS_Sans/HarmonyOS_Sans_Regular.ttf, avares://GlimpseonMain/Assets/font/HarmonyOS_Sans/HarmonyOS_Sans_Bold.ttf, HarmonyOS Sans, HarmonyOS Sans SC, Microsoft YaHei UI, Microsoft YaHei, Segoe UI");

    public static TextBlock MakeTitle(string text) => new()
    {
        Text = text,
        FontSize = 26,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(0, 0, 0, 12),
    };

    public static ScrollViewer MakePageScroll(Control content) => new()
    {
        Content = new StackPanel
        {
            Margin = new Thickness(32, 24, 32, 24),
            Children = { content },
        },
    };

    public static async void ShowTextFile(string title, string intro, string? filePath, Window? owner)
    {
        var contentText = intro;
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                contentText = await File.ReadAllTextAsync(filePath);
            }
            catch (Exception e)
            {
                Log.Warning($"协议文件读取失败: {filePath} {e.Message}");
            }
        }

        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBox
            {
                Text = contentText,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 320,
                MinWidth = 480,
                FontFamily = new FontFamily("Consolas"),
            },
            CloseButtonText = AppUtils.Tr("common.close"),
            DefaultButton = FAContentDialogButton.Close,
        };
        if (owner is not null)
        {
            await dialog.ShowAsync(owner);
        }
    }

    public static async Task Alert(Window owner, string title, string content, string buttonText)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = buttonText,
            DefaultButton = FAContentDialogButton.Close,
        };
        await dialog.ShowAsync(owner);
    }

    public static void ApplyAccentColor(Color color)
    {
        if (Application.Current?.Styles is not { } styles)
        {
            return;
        }
        foreach (var style in styles)
        {
            if (style is FluentAvaloniaTheme theme)
            {
                theme.PreferUserAccentColor = false;
                theme.CustomAccentColor = color;
            }
        }
    }

    public static Color ParseAccentColor(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Color.TryParse(text.Trim(), out var c) ? c : Color.Parse("#30c361");

    public static Window ResolveActiveWindow(Window fallback)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var active = desktop.Windows.FirstOrDefault(w => w.IsVisible && w.IsActive);
            if (active is not null)
            {
                return active;
            }
            if (fallback.IsVisible)
            {
                return fallback;
            }
            var any = desktop.Windows.LastOrDefault(w => w.IsVisible);
            if (any is not null)
            {
                return any;
            }
        }
        return fallback;
    }

    public static async Task<bool> ConfirmRestartAsync(Window owner)
    {
        var dialog = new FAContentDialog
        {
            Title = AppUtils.Tr("settings.restart_title"),
            Content = AppUtils.Tr("settings.restart_required_desc"),
            PrimaryButtonText = AppUtils.Tr("common.restart_now"),
            CloseButtonText = AppUtils.Tr("common.restart_later"),
            DefaultButton = FAContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync(owner);
        return result == FAContentDialogResult.Primary;
    }

    /// <summary>待重启状态确认</summary>
    private static bool _restartDialogShowing;

    /// <summary>待重启弹窗</summary>
    public static async Task ConfirmPendingRestartAsync(Window? owner, string? title = null, string? content = null)
    {
        if (_restartDialogShowing)
        {
            return;
        }
        _restartDialogShowing = true;
        try
        {
            if (owner is null)
            {
                AppUtils.MarkRestartPending();
                return;
            }
            var dialog = new FAContentDialog
            {
                Title = title ?? AppUtils.Tr("settings.restart_title"),
                Content = content ?? AppUtils.Tr("settings.restart_required_desc"),
                PrimaryButtonText = AppUtils.Tr("common.restart_now"),
                CloseButtonText = AppUtils.Tr("common.cancel"),
                DefaultButton = FAContentDialogButton.Primary,
            };
            var restart = await dialog.ShowAsync(owner) == FAContentDialogResult.Primary;
            if (restart)
            {
                AppUtils.RequestRestart();
            }
            else
            {
                AppUtils.MarkRestartPending();
            }
        }
        finally
        {
            _restartDialogShowing = false;
        }
    }

    /// <summary>
    /// 标题栏重启徽标
    /// </summary>
    public static void InitRestartBadge(Button badge, Window owner)
    {
        badge.Classes.Add("accent");
        badge.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                FAIcons.IconCenter(FASymbol.Sync, 14),
                new TextBlock { Text = AppUtils.Tr("settings.restart_title"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        badge.Height = 30;
        badge.CornerRadius = new CornerRadius(15);
        badge.Padding = new Thickness(12, 0, 12, 0);
        ToolTip.SetTip(badge, AppUtils.Tr("settings.restart_badge_tip"));
        badge.IsVisible = Config.RestartPending;
        badge.Click += (_, _) => _ = ConfirmPendingRestartAsync(owner);
        Config.RestartPendingChanged += () => Dispatcher.UIThread.Post(() =>
        {
            var visible = Config.RestartPending;
            if (badge.IsVisible != visible)
            {
                badge.IsVisible = visible;
            }
        });
    }

    public static async Task<bool> ConfirmRestartWithAsync(Window owner, string title, string content)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = AppUtils.Tr("common.restart_now"),
            CloseButtonText = AppUtils.Tr("common.cancel"),
            DefaultButton = FAContentDialogButton.Primary,
        };
        return await dialog.ShowAsync(owner) == FAContentDialogResult.Primary;
    }

    public static async Task<bool> Confirm(Window owner, string title, string content)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = AppUtils.Tr("common.confirm"),
            CloseButtonText = AppUtils.Tr("common.cancel"),
            DefaultButton = FAContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync(owner);
        return result == FAContentDialogResult.Primary;
    }

    public static async Task<string?> PromptTextInput(Window owner, string title, string initialText)
    {
        var edit = new TextBox { Text = initialText, MinWidth = 320 };
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = edit,
            PrimaryButtonText = AppUtils.Tr("common.confirm"),
            CloseButtonText = AppUtils.Tr("common.cancel"),
            DefaultButton = FAContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync(owner);
        return result == FAContentDialogResult.Primary ? edit.Text : null;
    }
}

// 通用窗口基类
public class GlimpseonWindow : FAAppWindow
{
    public static readonly StyledProperty<bool> EnableMicaWindowProperty =
        AvaloniaProperty.Register<GlimpseonWindow, bool>(nameof(EnableMicaWindow), defaultValue: true);

    public bool EnableMicaWindow
    {
        get => GetValue(EnableMicaWindowProperty);
        set => SetValue(EnableMicaWindowProperty, value);
    }

    private static bool IsMicaSupported =>
        OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build >= 22000;

    public GlimpseonWindow()
    {
        FontFamily = Common.AppFontFamily;
        TransparencyBackgroundFallback = Brushes.Transparent;
        // 标题栏
        try
        {
            TitleBar.ExtendsContentIntoTitleBar = true;
            TitleBar.Height = 48;
        }
        catch (Exception e)
        {

            Log.Debug($"[UI] 默认标题栏: {e.Message}");
            // TitleBar 未就绪时忽略 走 FUI 默认标题栏
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (EnableMicaWindow && IsMicaSupported)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.Mica];
            Background = Brushes.Transparent;
        }
    }
}

public sealed class PassThroughNavigationPageFactory : IFANavigationPageFactory
{
    public static readonly PassThroughNavigationPageFactory Instance = new();

    public Control? GetPage(Type srcType) => null;

    public Control GetPageFromObject(object target) => (Control)target;
}

// 组件展开层 公用
/// <summary>组件展开层 遮罩+Fluent 卡片</summary>
public static class ComponentExpander
{
    /// <summary>弹出展开层 对齐课程表窗口尺寸</summary>
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

        // 公用表面色 纯色
        card.Background = new SolidColorBrush(p.Card);
        card.BorderBrush = new SolidColorBrush(ThemeSense.IsDark(host) ? Colors.White : Colors.Black, ThemeSense.IsDark(host) ? 0.10 : 0.06);
        card.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty,
            new SolidColorBrush(p.Text));

        var root = new Panel { Children = { scrim, card } };
        root.Focusable = true;

        // 尺寸随层重算 防首帧未就绪
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

// 组件表面主题色 公用
/// <summary>组件表面主题色 深浅两套实色</summary>
public sealed class WidgetSurfacePalette
{
    public Color Card { get; private init; }
    public Color Text { get; private init; }
    public Color Sub { get; private init; }
    public Color Line { get; private init; }
    public Color BandA { get; private init; }
    public Color BandB { get; private init; }
    public Color Gutter { get; private init; }

    public static WidgetSurfacePalette Resolve(bool dark) => dark
        ? new()
        {
            Card = Color.FromRgb(30, 30, 30),
            Text = Color.Parse("#ececec"),
            Sub = Color.Parse("#8f8f8f"),
            Line = Color.FromRgb(61, 61, 61),
            BandA = Color.FromRgb(30, 30, 30),
            BandB = Color.FromRgb(37, 37, 37),
            Gutter = Color.FromRgb(43, 43, 43),
        }
        : new()
        {
            Card = Colors.White,
            Text = Color.Parse("#1c1c1c"),
            Sub = Color.Parse("#8a8a8a"),
            Line = Color.FromRgb(208, 208, 208),
            BandA = Colors.White,
            BandB = Color.FromRgb(246, 246, 246),
            Gutter = Color.FromRgb(240, 240, 240),
        };

    /// <summary>底色向 tint 预混</summary>
    public Color Blend(Color tint, double t) => Color.FromArgb(255,
        (byte)(Card.R + (tint.R - Card.R) * t),
        (byte)(Card.G + (tint.G - Card.G) * t),
        (byte)(Card.B + (tint.B - Card.B) * t));
}
