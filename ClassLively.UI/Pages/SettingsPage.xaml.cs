using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using ClassLively_UI.Services;

namespace ClassLively_UI.Pages;

/// <summary>
/// 设置页面
/// 原 PyQt6: ui/settings.py -> SettingInterface
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly IApiService _api;

    public SettingsPage()
    {
        InitializeComponent();
        _api = new ApiService();
    }

    /// <summary>
    /// 获取窗口句柄
    /// </summary>
    private nint GetWindowHandle()
    {
        return WinRT.Interop.WindowNative.GetWindowHandle(this);
    }

    //   通用组 (General)---------------------------
    private async void AutoStart_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch sw) await _api.SetConfigAsync("autoStart", sw.IsOn);
    }

    private async void AutoOpenIdle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch sw) await _api.SetConfigAsync("autoOpenOnIdle", sw.IsOn);
    }

    private async void IdleMinutes_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (sender.Value >= sender.Minimum && sender.Value <= sender.Maximum)
            await _api.SetConfigAsync("idleMinutes", (int)sender.Value);
    }

    private async void AutoOpenMaximize_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch sw) await _api.SetConfigAsync("autoOpenMaximize", sw.IsOn);
    }

    //   外观组 (Appearance)-------------------------------------------

    private async void ThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
            await _api.SetConfigAsync("themeMode", item.Tag?.ToString() ?? "light");
    }

    private async void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
            await _api.SetConfigAsync("language", item.Tag?.ToString() ?? "zh_CN");
    }

    //   日志组 (Log)------------------------------------------------

    private async void DisableLog_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch sw) return;
        await _api.SetConfigAsync("disableLog", sw.IsOn);

        // 联动禁用日志级别/数量/天数控件（原 __onDisableLogChanged）
        LogLevelCombo.IsEnabled = !sw.IsOn;
        LogMaxCountBox.IsEnabled = !sw.IsOn;
        LogMaxDaysBox.IsEnabled = !sw.IsOn;
    }

    private async void LogLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
            await _api.SetConfigAsync("logLevel", item.Tag?.ToString() ?? "info");
    }

    private async void LogMaxCount_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (sender.Value >= sender.Minimum && sender.Value <= sender.Maximum)
            await _api.SetConfigAsync("logMaxCount", (int)sender.Value);
    }

    private async void LogMaxDays_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (sender.Value >= sender.Minimum && sender.Value <= sender.Maximum)
            await _api.SetConfigAsync("logMaxDays", (int)sender.Value);
    }

    private async void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清空日志",
            Content = "确定要清空所有日志文件吗？",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = this.Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // 调用 Python API 清空日志
            await _api.SetConfigAsync("_action_clear_log", true);
        }
    }

    //   其他组 (Other)-----------------------

    private async void CloseAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
            await _api.SetConfigAsync("closeAction", item.Tag?.ToString() ?? "minimize");
    }

    private async void AllowMultiInstance_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch sw) await _api.SetConfigAsync("allowMultipleInstances", sw.IsOn);
    }

    private async void GpuAcceleration_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch sw) await _api.SetConfigAsync("enableGpuAcceleration", sw.IsOn);
    }

    private async void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "ClassLively_config",
            FileTypeChoices = { { "JSON 文件", new[] { ".json" } } }
        };

        var hwnd = GetWindowHandle();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        try
        {
            var config = await _api.GetConfigAsync();
            if (config == null) return;

            var json = System.Text.Json.JsonSerializer.Serialize(config,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await Windows.Storage.FileIO.WriteTextAsync(file, json);

            var dialog = new ContentDialog
            {
                Title = "导出成功",
                Content = $"配置已导出到：{file.Path}",
                CloseButtonText = "确定",
                XamlRoot = this.Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "导出失败",
                Content = ex.Message,
                CloseButtonText = "确定",
                XamlRoot = this.Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
    }

    private async void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            FileTypeFilter = { ".json" }
        };

        var hwnd = GetWindowHandle();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        var confirm = new ContentDialog
        {
            Title = "确认导入",
            Content = $"将从以下文件导入配置，当前设置将被覆盖：\n{file.Name}",
            PrimaryButtonText = "导入",
            CloseButtonText = "取消",
            XamlRoot = this.Content.XamlRoot,
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var json = await Windows.Storage.FileIO.ReadTextAsync(file);
            var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            if (config == null) return;

            foreach (var kv in config)
            {
                await _api.SetConfigAsync(kv.Key, kv.Value);
            }

            var dialog = new ContentDialog
            {
                Title = "导入成功",
                Content = $"配置已从 {file.Name} 导入，部分设置可能需要重启应用后生效。",
                CloseButtonText = "确定",
                XamlRoot = this.Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "导入失败",
                Content = ex.Message,
                CloseButtonText = "确定",
                XamlRoot = this.Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
    }

    private async void ResetDefault_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "恢复默认设置",
            Content = "确定要将所有设置恢复到默认值吗？",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = this.Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await _api.SetConfigAsync("_action_reset_default", true);
        }
    }

    private async void DebugMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch sw) await _api.SetConfigAsync("debugMode", sw.IsOn);
    }
}
