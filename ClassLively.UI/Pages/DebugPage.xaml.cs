//  DebugPage.xaml.cs — 调试面板
using ClassLively_UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Text;

namespace ClassLively_UI.Pages;

/// <summary>调试面板</summary>
public sealed partial class DebugPage : Page
{
    private readonly IApiService _apiService = new ApiService();
    private static readonly string ConfigDir = System.IO.Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "config");
    private static readonly string DataDir = System.IO.Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "data");
    private static readonly string CrashLogPath = System.IO.Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "crash_log.txt");

    public DebugPage()
    {
        InitializeComponent();
        RefreshSystemInfo();
    }

    // 系统信息

    /// <summary>刷新系统信息显示</summary>
    private void RefreshSystemInfo()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"操作系统：{Environment.OSVersion}");
            sb.AppendLine($".NET 版本：{Environment.Version}");
            sb.AppendLine($"计算机名：{Environment.MachineName}");
            sb.AppendLine($"CPU 核心数：{Environment.ProcessorCount}");
            sb.AppendLine($"内存占用：{Environment.WorkingSet / 1024 / 1024} MB");

            SystemInfoPanel.Children.Clear();
            var lines = sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                SystemInfoPanel.Children.Add(new TextBlock
                {
                    Text = line.Trim(),
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 13,
                    Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"]
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DebugPage] 刷新系统信息失败: {ex.Message}");
        }
    }

    // API 测试

    /// <summary>请求按钮</summary>
    private async void OnSendApiRequestClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (EndpointSelector.SelectedItem is not ComboBoxItem item)
            {
                ApiResultText.Text = "请选择端点";
                return;
            }

            var endpoint = item.Tag?.ToString() ?? "";
            ApiResultText.Text = "请求中";

            object? result = endpoint switch
            {
                "config/get" => await _apiService.GetConfigAsync(),
                "config/set" => await _apiService.SetConfigAsync("_debug_test", true),
                "wallpaper/current" => await _apiService.GetCurrentWallpaperAsync(),
                "weather" => await _apiService.GetWeatherAsync(),
                "poetry" => await _apiService.GetPoetryAsync(),
                "media/info" => await _apiService.GetMediaInfoAsync(),
                _ => null
            };

            if (result != null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(result,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                ApiResultText.Text = $"[{endpoint}] →\n{json}";
            }
            else
            {
                ApiResultText.Text = $"[{endpoint}] → 返回null";
            }

            Debug.WriteLine($"[DebugPage] API 测试完成: {endpoint}");
        }
        catch (Exception ex)
        {
            ApiResultText.Text = $"请求异常:\n{ex}";
            Debug.WriteLine($"[DebugPage] API 测试异常: {ex.Message}");
        }
    }

    //  日志查看器

    /// <summary>刷新日志按钮</summary>
    private void OnRefreshLogClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.IO.File.Exists(CrashLogPath))
            {
                LogContentText.Text = "日志不存在";
                return;
            }

            var content = System.IO.File.ReadAllText(CrashLogPath);
            if (string.IsNullOrWhiteSpace(content))
            {
                LogContentText.Text = "日志为空";
            }
            else
            {
                LogContentText.Text = content.Length > 50000
                    ? content[^50000..] + "\n... (已截断，仅显示最后 50KB)"
                    : content;
            }
        }
        catch (Exception ex)
        {
            LogContentText.Text = $"读取日志失败: {ex.Message}";
            Debug.WriteLine($"[DebugPage] 刷新日志异常: {ex.Message}");
        }
    }

    /// <summary>清除日志按钮</summary>
    private void OnClearLogClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (System.IO.File.Exists(CrashLogPath))
            {
                System.IO.File.Delete(CrashLogPath);
                LogContentText.Text = "日志已清除";
            }
            else
            {
                LogContentText.Text = "文件不存在";
            }
        }
        catch (Exception ex)
        {
            LogContentText.Text = $"清除失败: {ex.Message}";
            Debug.WriteLine($"[DebugPage] 清除日志异常: {ex.Message}");
        }
    }

    //  操作按钮

    /// <summary>GC 回收 刷新内存显示</summary>
    private void OnGcCollectClick(object sender, RoutedEventArgs e)
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // 更新内存占用
            RefreshSystemInfo();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DebugPage] GC 异常: {ex.Message}");
        }
    }

    /// <summary>重新加载当前页面</summary>
    private void OnReloadPageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (this.Frame != null)
            {
                this.Frame.Navigate(typeof(DebugPage));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DebugPage] 重新加载页面异常: {ex.Message}");
        }
    }
}
