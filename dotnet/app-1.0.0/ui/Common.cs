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
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Glimpseon.Core;

namespace Glimpseon.UI;

public static class Common
{
    public static readonly FontFamily AppFontFamily = new(
        "avares://GlimpseonMain/Assets/font/HarmonyOS_Sans/HarmonyOS_Sans_Regular.ttf, avares://GlimpseonMain/Assets/font/HarmonyOS_Sans/HarmonyOS_Sans_Bold.ttf, HarmonyOS Sans, HarmonyOS Sans SC, Microsoft YaHei UI, Microsoft YaHei, Segoe UI");    public static TextBlock MakeTitle(string text) => new()
    {
        Text = text,
        FontSize = 26,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(0, 0, 0, 12),
    };

    public static TextBlock MakeBody(string text) => new()
    {
        Text = text,
        FontSize = 14,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.85,
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
        catch (Exception)
        {
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
