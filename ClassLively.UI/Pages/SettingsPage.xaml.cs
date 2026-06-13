using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ClassLively_UI.Helpers;
using ClassLively_UI.Services;

namespace ClassLively_UI.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly IApiService _api = new ApiService();

    public SettingsPage()
    {
        InitializeComponent();
        _ = AppSettings.LoadAsync();
        _ = LoadAllSettings();
    }

    // ──── 通用组 ────

    private async void AutoStart_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch sw)
        {
            AppSettings.Set("autoStart", sw.IsOn);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("autoStart", sw.IsOn);
        }
    }

    private async void AutoOpenIdle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch sw)
        {
            AppSettings.Set("autoOpenOnIdle", sw.IsOn);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("autoOpenOnIdle", sw.IsOn);
        }
    }

    private async void IdleMinutes_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider slider && slider.Value >= slider.Minimum && slider.Value <= slider.Maximum)
        {
            var value = (int)slider.Value;
            AppSettings.Set("idleMinutes", value);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("idleMinutes", value);
        }
    }

    private async void AutoOpenMaximize_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch sw)
        {
            AppSettings.Set("autoOpenMaximize", sw.IsOn);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("autoOpenMaximize", sw.IsOn);
        }
    }

    private async void AllowMultipleInstances_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch sw)
        {
            AppSettings.Set("allowMultipleInstances", sw.IsOn);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("allowMultipleInstances", sw.IsOn);
        }
    }

    private async void DebugMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch sw)
        {
            AppSettings.Set("debugMode", sw.IsOn);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("debugMode", sw.IsOn);
        }
    }

    private async void CloseAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
        {
            var action = item.Tag?.ToString() ?? "minimize";
            AppSettings.Set("closeAction", action);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("closeAction", action);
        }
    }

    // ──── 外观组 ────

    private async void ThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
        {
            var modeStr = item.Tag?.ToString() ?? "light";
            AppSettings.Set("themeMode", modeStr);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("themeMode", modeStr);

            var mode = ThemeHelper.ParseMode(modeStr);
            if (App.MainWindow != null)
                ThemeHelper.ApplyTheme(App.MainWindow, mode);
        }
    }

    private async void ThemeColor_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPicker();
        var dialog = new ContentDialog
        {
            Title = "选择主题色",
            Content = picker,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var color = picker.Color;
            var hexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

            ThemeColorButton.Background = new SolidColorBrush(color);

            AppSettings.Set("themeColor", hexColor);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("themeColor", hexColor);
        }
    }

    // ──── 日志组 ────

    private async void LogLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
        {
            var level = item.Tag?.ToString() ?? "info";
            AppSettings.Set("logLevel", level);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("logLevel", level);
        }
    }

    private async void DisableLog_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch sw) return;

        AppSettings.Set("disableLog", sw.IsOn);
        _ = AppSettings.SaveAsync();
        await _api.SetConfigAsync("disableLog", sw.IsOn);

        LogLevelCombo.IsEnabled = !sw.IsOn;
        LogMaxCountSlider.IsEnabled = !sw.IsOn;
        LogMaxDaysSlider.IsEnabled = !sw.IsOn;
    }

    private async void LogMaxCount_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider slider && slider.Value >= slider.Minimum && slider.Value <= slider.Maximum)
        {
            var value = (int)slider.Value;
            AppSettings.Set("logMaxCount", value);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("logMaxCount", value);
        }
    }

    private async void LogMaxDays_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider slider && slider.Value >= slider.Minimum && slider.Value <= slider.Maximum)
        {
            var value = (int)slider.Value;
            AppSettings.Set("logMaxDays", value);
            _ = AppSettings.SaveAsync();
            await _api.SetConfigAsync("logMaxDays", value);
        }
    }

    // ──── 数据管理组 ────

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清除缓存",
            Content = "确定要清除应用缓存数据吗？",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _api.SetConfigAsync("_action_clear_cache", true);
        }
    }

    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "重置所有设置",
            Content = "确定要将所有设置恢复到默认值吗？此操作不可撤销。",
            PrimaryButtonText = "重置",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _api.SetConfigAsync("_action_reset_default", true);
            _ = LoadAllSettings();
        }
    }

    private async void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var configDir = System.IO.Path.Combine(
                AppContext.BaseDirectory, "config");
            if (!System.IO.Directory.Exists(configDir))
                System.IO.Directory.CreateDirectory(configDir);

            var folder = await StorageFolder.GetFolderFromPathAsync(configDir);
            await Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] 打开配置文件夹失败: {ex.Message}");

            var dialog = new ContentDialog
            {
                Title = "打开失败",
                Content = $"无法打开配置文件夹：{ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
    }

    //   加载设置到 UI 控件

    private async Task LoadAllSettings()
    {
        try
        {
            var config = await _api.GetConfigAsync();
            if (config == null)
            {
                Debug.WriteLine("[Settings] 后端不可用，保持默认值");
                return;
            }

            ApplyConfig(config, "autoStart", v => AutoStartSwitch.IsOn = ToBool(v));
            ApplyConfig(config, "autoOpenOnIdle", v => AutoOpenIdleSwitch.IsOn = ToBool(v));
            ApplyConfig(config, "idleMinutes", v => IdleMinutesSlider.Value = ToInt(v, 5));
            ApplyConfig(config, "autoOpenMaximize", v => AutoOpenMaximizeSwitch.IsOn = ToBool(v));
            ApplyConfig(config, "allowMultipleInstances", v => AllowMultipleInstancesSwitch.IsOn = ToBool(v));
            ApplyConfig(config, "debugMode", v => DebugModeSwitch.IsOn = ToBool(v));
            ApplyConfig(config, "closeAction", v => SelectCombo(CloseActionCombo, Str(v)));

            ApplyConfig(config, "themeMode", v => SelectCombo(ThemeModeCombo, Str(v)));
            ApplyConfig(config, "themeColor", v => ApplyThemeColor(Str(v)));

            ApplyConfig(config, "logLevel", v => SelectCombo(LogLevelCombo, Str(v)));
            ApplyConfig(config, "disableLog", v =>
            {
                DisableLogSwitch.IsOn = ToBool(v);
                LogLevelCombo.IsEnabled = !ToBool(v);
                LogMaxCountSlider.IsEnabled = !ToBool(v);
                LogMaxDaysSlider.IsEnabled = !ToBool(v);
            });
            ApplyConfig(config, "logMaxCount", v => LogMaxCountSlider.Value = ToInt(v, 100));
            ApplyConfig(config, "logMaxDays", v => LogMaxDaysSlider.Value = ToInt(v, 30));

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] 后端加载失败: {ex.Message}");
        }
    }

    // ──── 辅助方法 ────

    private static void ApplyConfig(Dictionary<string, object> config, string key, Action<object> setter)
    {
        if (config.TryGetValue(key, out var val) && val != null)
        {
            try { setter(val); } catch { }
        }
    }

    private static void SelectCombo(ComboBox combo, string? tagValue)
    {
        if (string.IsNullOrEmpty(tagValue)) return;
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi &&
                string.Equals(cbi.Tag?.ToString(), tagValue, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void ApplyThemeColor(string? hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor)) return;
        try
        {
            ThemeColorButton.Background = new SolidColorBrush(ParseHexColor(hexColor));
        }
        catch { /* 保持默认色 */ }
    }

    private static global::Windows.UI.Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6 &&
            byte.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            return global::Windows.UI.Color.FromArgb(255, r, g, b);
        return global::Windows.UI.Color.FromArgb(255, 0x00, 0x78, 0xD4);
    }

    private static bool ToBool(object val) => Convert.ToBoolean(val);
    private static int ToInt(object val, int fallback) => Convert.ToInt32(val);
    private static string? Str(object val) => val?.ToString();
}
