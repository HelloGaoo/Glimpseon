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

using Markdown.Avalonia;
using ColorTextBlock.Avalonia;

namespace Glimpseon.UI;

// 深浅色判断
internal static class ThemeSense
{
    public static bool IsDark(StyledElement e)
    {
        // 卡片内取色以卡片明暗为准
        if (e is WidgetCardBase card)
        {
            return card.CardIsDark;
        }
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

// 图标唯一来源 Segoe Fluent Icons 码位全部对照字形表核对 语义不得混用
public static class AppIcons
{
    public static readonly FontFamily Family = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public const string Menu = "\uE700";
    public const string Edit = "\uE70F";
    public const string Settings = "\uE713";
    public const string Search = "\uE721";
    public const string Attach = "\uE723";
    public const string Send = "\uE724";
    public const string Refresh = "\uE72C";
    public const string Delete = "\uE74D";
    public const string Save = "\uE74E";
    public const string Play = "\uE768";
    public const string Pause = "\uE769";
    public const string ChevronLeft = "\uE76B";
    public const string ChevronRight = "\uE76C";
    public const string ChevronDown = "\uE70D";
    public const string ChevronUp = "\uE70E";
    public const string Add = "\uE710";
    public const string Close = "\uE711";
    public const string Check = "\uE73E";
    public const string Calendar = "\uE787";
    public const string Clock = "\uE823";
    public const string Stopwatch = "\uE916";
    public const string History = "\uE81C";
    public const string Calculator = "\uE8EF";
    public const string Music = "\uE8D6";
    public const string Mouse = "\uE8B0";
    public const string Globe = "\uE774";
    public const string Home = "\uE80F";
    public const string Power = "\uE7E8";
    public const string Lock = "\uE72E";
    public const string Unlock = "\uE785";
    public const string Volume = "\uE767";
    public const string Mute = "\uE74F";
    public const string People = "\uE716";
    public const string Mail = "\uE715";
    public const string Pin = "\uE718";
    public const string More = "\uE712";
    public const string OpenInNew = "\uE8A7";
    public const string Chat = "\uE8BD";
    public const string Robot = "\uE99A";
    public const string Photo = "\uE91B";
    public const string Camera = "\uE722";
    public const string Library = "\uE1D3";
    public const string Books = "\uE8F1";
    public const string Book = "\uE82D";
    public const string Diagnostic = "\uE9D9";
    public const string Sync = "\uE895";
    public const string Page = "\uE7C3";
    public const string Document = "\uE8A5";
    public const string List = "\uE71D";
    public const string ListLines = "\uE8FD";
    public const string Flash = "\uE945";
    public const string Stop = "\uE15B";
    public const string Sun = "\uE706";
    public const string Moon = "\uE708";
    public const string Wifi = "\uE701";
    public const string Education = "\uE7BE";
    public const string Megaphone = "\uE789";
    public const string Previous = "\uE100";
    public const string Next = "\uE101";
    public const string Wrench = "\uE90F";
    public const string Blocked = "\uE92C";
    public const string Info = "\uE946";
    public const string Warning = "\uE7BA";
    public const string Logout = "\uE748";
    public const string Grid = "\uE8A1";
    public const string Download = "\uE896";
    public const string Copy = "\uE77F";
    public const string Desktop = "\uE7F4";
    public const string Laptop = "\uE7F8";
    public const string Tag = "\uE8EC";
    public const string Font = "\uE8D2";
    public const string Keyboard = "\uE92E";
    public const string Palette = "\uE790";
    public const string Bell = "\uE791";
    public const string Folder = "\uE8B7";
    public const string Contact = "\uE888";
    public const string Devices = "\uE975";
    public const string Undo = "\uE10E";
    public const string Redo = "\uE10D";
    public const string Zoom = "\uE71E";
    public const string Code = "\uE943";
    public const string Image = "\uE91B";

    public static TextBlock Create(string glyph, double fontSize = 16) => new()
    {
        Text = glyph,
        FontFamily = Family,
        FontSize = fontSize,
    };

    public static TextBlock Center(string glyph, double fontSize = 16)
    {
        var icon = Create(glyph, fontSize);
        icon.VerticalAlignment = VerticalAlignment.Center;
        return icon;
    }
}

public static class FAIcons
{
    // 统一走 Segoe Fluent Icons 不再使用 FluentAvalonia 自带精简字体
    public static readonly FontFamily SymbolsFont = AppIcons.Family;

    public static FAFontIcon Icon(FASymbol symbol, double fontSize = 16) => new()
    {
        FontFamily = SymbolsFont,
        Glyph = char.ConvertFromUtf32((int)symbol),
        FontSize = fontSize,
    };

    // 语义码位直引 与 AppIcons 常量配合使用
    public static FAFontIcon Glyph(string glyph, double fontSize = 16) => new()
    {
        FontFamily = SymbolsFont,
        Glyph = glyph,
        FontSize = fontSize,
    };

    public static FAFontIcon GlyphCenter(string glyph, double fontSize = 16)
    {
        var icon = Glyph(glyph, fontSize);
        icon.VerticalAlignment = VerticalAlignment.Center;
        return icon;
    }

    public static FAFontIcon IconByName(string? name, double fontSize = 16)
    {
        // 语义名优先 避免落到 F8 段旧码位或无关字形
        var glyph = name switch
        {
            "WeatherSunny" or "Brightness" => AppIcons.Sun,
            "WeatherMoon" or "Moon" => AppIcons.Moon,
            "Clock" => AppIcons.Clock,
            "Calculator" => AppIcons.Calculator,
            "Lock" => AppIcons.Lock,
            "Power" => AppIcons.Power,
            "Photo" or "Pictures" => AppIcons.Photo,
            "Diagnostic" => AppIcons.Diagnostic,
            "Mute" => AppIcons.Mute,
            "Delete" => AppIcons.Delete,
            "Edit" => AppIcons.Edit,
            "Settings" => AppIcons.Settings,
            "Refresh" => AppIcons.Refresh,
            _ => null,
        };
        if (glyph is not null)
        {
            return Glyph(glyph, fontSize);
        }
        if (Enum.TryParse<FASymbol>(name, out var parsed) && (int)parsed < 0xF0000)
        {
            return Glyph(char.ConvertFromUtf32((int)parsed), fontSize);
        }
        return Glyph(AppIcons.Edit, fontSize);
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

    public static readonly FontFamily AppFontFamily = WidgetFonts.Ui;

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

    /// <summary>同色系提亮/压暗 k∈[-1,1]</summary>
    public static Color ShiftColor(Color c, double k)
    {
        var t = Math.Clamp(k, -1, 1);
        var target = t >= 0 ? Colors.White : Colors.Black;
        var m = Math.Abs(t);
        return Color.FromArgb(c.A,
            (byte)Math.Round(c.R + (target.R - c.R) * m),
            (byte)Math.Round(c.G + (target.G - c.G) * m),
            (byte)Math.Round(c.B + (target.B - c.B) * m));
    }

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

    /// <summary>确认框 owner 空返回 false</summary>
    public static async Task<bool> Confirm(Window? owner, string title, string content)
    {
        if (owner is null)
        {
            return false;
        }
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
    /// <summary>弹层尺寸</summary>
    public const double PanelWidth = 1200;
    public const double PanelHeight = 750;

    /// <summary>弹出展开层 返回关闭句柄</summary>
    public static Action Show(Visual host, string title, Control content)
    {
        var layer = OverlayLayer.GetOverlayLayer(host);
        if (layer is null)
        {
            Log.Warning($"[展开层] 宿主无 OverlayLayer: {title}");
            return () => { };
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
            Content = FAIcons.IconGlyph("E711", 13),
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
            card.Width = Math.Clamp(PanelWidth, 320, Math.Max(320, b.Width - 48));
            card.Height = Math.Clamp(PanelHeight, 240, Math.Max(240, b.Height - 48));
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
        Log.Debug($"[展开层] 打开: {title} {PanelWidth:F0}x{PanelHeight:F0}");
        return Close;
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

// 组件字色令牌 四档弱化 全库统一
/// <summary>字色四档: Fg 全值 / Muted 0.62 / Faint 0.30 / Sub 走色板</summary>
public static class CardInk
{
    // 与 WidgetInk 同源 避免两套弱化档位
    public const double MutedOpacity = WidgetInk.MutedOpacity;
    public const double FaintOpacity = WidgetInk.FaintOpacity;

    public static SolidColorBrush Fg(WidgetSurfacePalette p) => new(p.Text);

    // 次级文本 62% 弱化
    public static SolidColorBrush Muted(WidgetSurfacePalette p) => new(p.Text, MutedOpacity);

    // 极弱 占位/脚注 38%
    public static SolidColorBrush Faint(WidgetSurfacePalette p) => new(p.Text, FaintOpacity);

    // 语义色: 警示/危险 阈值触发
    public static SolidColorBrush Warn() => new(Color.Parse("#f0a33a"));
    public static SolidColorBrush Danger() => new(Color.Parse("#e5484d"));
}

// 组件统一强调色 用户主题色兜底
/// <summary>全组件唯一强调色入口 替换散落的硬编码蓝绿</summary>
public static class CardAccent
{
    public static Color Value => Common.ParseAccentColor(Config.ThemeColor.Value);
    public static SolidColorBrush Brush() => new(Value);
    public static SolidColorBrush Brush(double opacity) => new(Value, opacity);

    // 强调色实底上的字色 亮底用深字 暗底用白字
    public static SolidColorBrush Ink()
    {
        var c = Value;
        var luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return new SolidColorBrush(luminance > 0.62 ? Color.FromRgb(22, 22, 22) : Colors.White);
    }
}

/// <summary>Markdown 渲染 主题化后处理 插件详情/AI 气泡共用</summary>
public static class MarkdownLook
{
    public static MarkdownScrollViewer Create(string markdown, bool compact = false)
    {
        var viewer = new MarkdownScrollViewer { Markdown = markdown };
        viewer.AttachedToVisualTree += (_, _) => ScheduleMarkdownLook(viewer, compact);
        return viewer;
    }

    // 挂载后按时间点重复挂样式
    private static readonly int[] LookSteps = { 0, 80, 200, 400, 800, 1500 };
    private const int LookDeadlineMs = 1600;

    private static void ScheduleMarkdownLook(MarkdownScrollViewer viewer, bool compact)
    {
        viewer.Opacity = 0;
        var index = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var ms = (int)sw.ElapsedMilliseconds;
            if (viewer.IsLoaded)
            {
                while (index < LookSteps.Length && ms >= LookSteps[index])
                {
                    if (ApplyMarkdownLook(viewer, compact))
                    {
                        viewer.Opacity = 1;
                    }
                    index++;
                }
            }
            // 到点强制显示
            if (index >= LookSteps.Length || ms >= LookDeadlineMs)
            {
                viewer.Opacity = 1;
                return;
            }
            var next = viewer.IsLoaded ? Math.Min(LookSteps[index], LookDeadlineMs) : 250;
            timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, next - ms));
            timer.Start();
        };
        timer.Start();
    }

    private static bool _mdTreeLogged;

    private static bool ApplyMarkdownLook(MarkdownScrollViewer viewer, bool compact = false)
    {
        try
        {
            var dark = viewer.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            var primary = new SolidColorBrush(dark ? Color.Parse("#FFFFFFFF") : Color.Parse("#E4000000"));
            var secondary = new SolidColorBrush(dark ? Color.Parse("#C4FFFFFF") : Color.Parse("#B0000000"));
            var tertiary = new SolidColorBrush(dark ? Color.Parse("#59FFFFFF") : Color.Parse("#59000000"));
            var codeBg = new SolidColorBrush(dark ? Color.Parse("#1AFFFFFF") : Color.Parse("#0F000000"));
            var spliceBg = new SolidColorBrush(dark ? Color.Parse("#24FFFFFF") : Color.Parse("#12000000"));
            var tableHead = new SolidColorBrush(dark ? Color.Parse("#12FFFFFF") : Color.Parse("#0A000000"));
            var mono = new FontFamily("Cascadia Mono, Consolas, monospace");
            var count = 0;
            foreach (var v in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(viewer))
            {
                switch (v)
                {
                    case ColorTextBlock.Avalonia.CTextBlock tb:
                        var isCodeBlock = tb.Classes.Contains("CodeBlock");
                        if (isCodeBlock)
                        {
                            tb.Foreground = secondary;
                            tb.FontFamily = mono;
                            tb.FontSize = 13;
                        }
                        else
                        {
                            tb.Foreground = primary;
                            // compact 档 DeepSeek 规格: 标题=14px 加粗 无大字号
                            if (tb.Classes.Contains("Heading1") || tb.Classes.Contains("Heading2"))
                            {
                                tb.FontSize = compact ? 14 : 26;
                                tb.FontWeight = FontWeight.SemiBold;
                                tb.LineHeight = compact ? 20 : double.NaN;
                                tb.Margin = compact ? new Thickness(0, 10, 0, 6) : new Thickness(0, 0, 0, 14);
                            }
                            else if (tb.Classes.Contains("Heading3") || tb.Classes.Contains("Heading4"))
                            {
                                tb.FontSize = compact ? 14 : 16;
                                tb.FontWeight = FontWeight.Medium;
                                tb.LineHeight = compact ? 20 : double.NaN;
                                tb.Margin = compact ? new Thickness(0, 8, 0, 4) : new Thickness(0, 14, 0, 8);
                            }
                            else
                            {
                                tb.FontSize = compact ? 14 : 15;
                                tb.LineHeight = compact ? 24 : double.NaN;
                                tb.Margin = new Thickness(0, 0, 0, compact ? 8 : 10);
                            }
                        }

                        if (tb.Content is { } content)
                        {
                            foreach (var item in content)
                            {
                                switch (item)
                                {
                                    case ColorTextBlock.Avalonia.CHyperlink _:
                                        break;
                                    case ColorTextBlock.Avalonia.CCode code:
                                        code.Foreground = secondary;
                                        code.FontFamily = mono;
                                        code.IsUnderline = false;
                                        code.Background = isCodeBlock ? null : spliceBg;
                                        break;
                                    case ColorTextBlock.Avalonia.CSpan span:
                                        span.Foreground = isCodeBlock ? secondary : primary;
                                        if (isCodeBlock)
                                        {
                                            span.FontFamily = mono;
                                        }
                                        span.IsUnderline = false;
                                        break;
                                }
                            }
                        }
                        count++;
                        break;
                    case TextBlock t when t.Classes.Contains("CodeBlock"):
                        t.Foreground = secondary;
                        t.FontFamily = mono;
                        t.FontSize = 13;
                        count++;
                        break;
                    case Border b when b.Classes.Contains("CodeBlock"):
                        b.Background = codeBg;
                        b.CornerRadius = new CornerRadius(8);
                        b.Padding = new Thickness(compact ? 8 : 10);
                        b.Margin = new Thickness(0, 0, 0, compact ? 8 : 14);
                        b.BorderThickness = new Thickness(0);
                        count++;
                        break;
                    case Border b when b.Classes.Contains("TableHeader"):
                        b.Background = tableHead;
                        b.Padding = new Thickness(8);
                        b.BorderThickness = new Thickness(1, 1, 0, 0);
                        b.BorderBrush = tertiary;
                        count++;
                        break;
                    case Border b when b.Classes.Contains("OddTableRow") || b.Classes.Contains("EvenTableRow"):
                        b.Padding = new Thickness(8);
                        b.BorderThickness = new Thickness(1, 1, 0, 0);
                        b.BorderBrush = tertiary;
                        b.Background = Brushes.Transparent;
                        count++;
                        break;
                }
            }
            if (count > 0)
            {
                Log.Debug($"[说明] Markdown 样式已应用 {count} 个元素 dark={dark}");
                if (!_mdTreeLogged)
                {
                    _mdTreeLogged = true;
                    var dump = new List<string>();
                    foreach (var vv in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(viewer).Take(40))
                    {
                        var cls = vv.Classes.Count > 0 ? "[" + string.Join(",", vv.Classes) + "]" : "";
                        dump.Add($"{vv.GetType().Name}{cls}");
                    }
                    Log.Debug($"[说明] 文档结构: {string.Join(" | ", dump)}");
                }
            }
            return count > 0;
        }
        catch (Exception e)
        {
            Log.Warning($"[说明] Markdown 样式后处理失败: {e.Message}");
            // 本轮未生效
            return false;
        }
    }

}

// 公共 ui 工具
