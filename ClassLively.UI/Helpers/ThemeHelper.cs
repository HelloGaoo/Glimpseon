//  ThemeHelper.cs — 主题管理
using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace ClassLively_UI.Helpers;

public static class ThemeHelper
    public enum ThemeMode { Light, Dark, System }

    /// <summary>应用主题到窗口</summary>
    public static void ApplyTheme(Window window, ThemeMode mode)
    {
        // 设置到根元素
        var theme = mode switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default // System = Default
        };
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = theme;
        Debug.WriteLine($"[ThemeHelper] 已应用主题: {mode}");
    }

    /// <summary>解析主题模式</summary>
    public static ThemeMode ParseMode(string? value) => value?.ToLower() switch
    {
        "dark" => ThemeMode.Dark,
        "light" => ThemeMode.Light,
        "system" or null => ThemeMode.System,
        _ => ThemeMode.System
    };

    /// <summary>获取当前应用级主题</summary>
    public static ElementTheme GetCurrentTheme()
    {
        // Application.Current.RequestedTheme 返回 ApplicationTheme 要映射为 ElementTheme
        return Application.Current.RequestedTheme switch
        {
            ApplicationTheme.Dark => ElementTheme.Dark,
            ApplicationTheme.Light => ElementTheme.Light,
            _ => ElementTheme.Default
        };
    }
}
