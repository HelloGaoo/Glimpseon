using System;
using ClassLively_UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ClassLively_UI.Pages;

public sealed partial class UpdatePage : Page
{
    private readonly IApiService _api;

    public UpdatePage()
    {
        InitializeComponent();
        _api = new ApiService();
        LoadCurrentVersion();
    }

    private enum UpdateStatus
    {
        Checking,
        Error,
        Available,
        Latest,
        Downloading
    }

    private static SolidColorBrush GetStatusColor(UpdateStatus status) => status switch
    {
        UpdateStatus.Checking => new SolidColorBrush(Color.FromArgb(255, 30, 144, 255)),
        UpdateStatus.Error => new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)),
        UpdateStatus.Available => new SolidColorBrush(Color.FromArgb(255, 255, 140, 0)),
        UpdateStatus.Latest => new SolidColorBrush(Color.FromArgb(255, 16, 124, 16)),
        UpdateStatus.Downloading => new SolidColorBrush(Color.FromArgb(255, 30, 144, 255)),
        _ => new SolidColorBrush(Color.FromArgb(255, 153, 153, 153))
    };

    private static string GetStatusText(UpdateStatus status) => status switch
    {
        UpdateStatus.Checking => "正在检查...",
        UpdateStatus.Error => "检查失败",
        UpdateStatus.Available => "发现新版本",
        UpdateStatus.Latest => "已是最新版本",
        UpdateStatus.Downloading => "正在下载...",
        _ => "未知状态"
    };

    private void SetStatus(UpdateStatus status)
    {
        StatusDot.Fill = GetStatusColor(status);
        StatusText.Text = GetStatusText(status);
    }

    private void LoadCurrentVersion()
    {
        AppTitleText.Text = "ClassLively 1.0.0";
        BuildDateText.Text = "构建日期：2025-06-10";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        SetStatus(UpdateStatus.Checking);

        try
        {
            // TODO: 调用 API 检查更新  占位
            // var result = await _api.CheckUpdateAsync();
            await Task.Delay(1500);

            // 占位
            SetStatus(UpdateStatus.Latest);
            ChangelogTextBox.Text = "当前已是最新版本。\n\nv1.0.0 (2025-06-10)\n- ";
        }
        catch (Exception ex)
        {
            SetStatus(UpdateStatus.Error);
            ChangelogTextBox.Text = $"检查更新时发生错误：{ex.Message}";
        }
        finally
        {
            CheckUpdateBtn.IsEnabled = true;
        }
    }

    private async void AutoCheck_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            try
            {
                await _api.SetConfigAsync("auto_check_update", toggle.IsOn);
            }
            catch (Exception ex)
            {
                toggle.IsOn = !toggle.IsOn;
                ShowErrorToast($"保存设置失败：{ex.Message}");
            }
        }
    }

    private async void AutoUpdate_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            try
            {
                await _api.SetConfigAsync("auto_update", toggle.IsOn);
            }
            catch (Exception ex)
            {
                toggle.IsOn = !toggle.IsOn;
                ShowErrorToast($"保存设置失败：{ex.Message}");
            }
        }
    }

    private async void ShowErrorToast(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "错误",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
