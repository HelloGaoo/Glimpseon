using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ClassLively_UI.Helpers;
using ClassLively_UI.Services;

namespace ClassLively_UI.Pages;

public sealed partial class UpdatePage : Page
{
    private readonly IApiService _api = new ApiService();
    private static readonly HttpClient _httpClient = CreateHttpClient();

    private const string VersionUrl =
        "https://ghfile.geekertao.top/https://raw.githubusercontent.com/HelloGaoo/ClassLively/main/version.py";
    private const string ChangelogUrl =
        "https://ghfile.geekertao.top/https://raw.githubusercontent.com/HelloGaoo/ClassLively/main/changelog.md";

    private string _currentVersion = "0.1.0";
    private string _currentBuildDate = "2026-03-14";
    private bool _hasNewVersion;

    public UpdatePage()
    {
        InitializeComponent();
        LoadCurrentVersion();
        LoadSettings();
    }

    // ──── 初始化 ────

    private void LoadCurrentVersion()
    {
        try
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
                _currentVersion = $"{version.Major}.{version.Minor}.{version.Build}";

            var modulePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(modulePath))
                _currentBuildDate = System.IO.File.GetLastWriteTime(modulePath).ToString("yyyy-MM-dd");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdatePage] 读取版本信息失败: {ex.Message}");
        }

        AppTitleText.Text = $"ClassLively {_currentVersion}";
        BuildDateText.Text = $"构建日期：{_currentBuildDate}";
    }

    private async void LoadSettings()
    {
        await AppSettings.LoadAsync();

        AutoCheckSwitch.IsOn = AppSettings.GetBool("auto_check_update", true);
        AutoUpdateSwitch.IsOn = AppSettings.GetBool("auto_update", false);

        try
        {
            var config = await _api.GetConfigAsync();
            if (config != null)
            {
                ApplyConfig(config, "autoCheckUpdate", v => AutoCheckSwitch.IsOn = ToBool(v));
                ApplyConfig(config, "autoUpdate", v => AutoUpdateSwitch.IsOn = ToBool(v));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdatePage] 加载设置失败: {ex.Message}");
        }
    }

    // ──── 状态管理 ────

    private enum UpdateStatus
    {
        Checking,
        Error,
        UpdateAvailable,
        Latest,
        Downloading
    }

    private static readonly Color CheckingColor = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
    private static readonly Color ErrorColor = Color.FromArgb(0xFF, 0xFF, 0x00, 0x00);
    private static readonly Color UpdateAvailableColor = Color.FromArgb(0xFF, 0xFF, 0x8C, 0x00);
    private static readonly Color LatestColor = Color.FromArgb(0xFF, 0x10, 0x7C, 0x10);
    private static readonly Color DownloadingColor = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);

    private static SolidColorBrush StatusBrush(UpdateStatus status) => status switch
    {
        UpdateStatus.Checking => new(CheckingColor),
        UpdateStatus.Error => new(ErrorColor),
        UpdateStatus.UpdateAvailable => new(UpdateAvailableColor),
        UpdateStatus.Latest => new(LatestColor),
        UpdateStatus.Downloading => new(DownloadingColor),
        _ => new(Color.FromArgb(0xFF, 0x99, 0x99, 0x99))
    };

    private void SetUpdateStatus(UpdateStatus status, string? text = null)
    {
        StatusDot.Fill = StatusBrush(status);
        if (text != null)
            StatusLabel.Text = text;
        StatusLabel.Foreground = StatusBrush(status);
    }

    // ──── 检查更新 ────

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_hasNewVersion)
        {
            StartDownload();
            return;
        }

        CheckUpdateBtn.IsEnabled = false;
        SetUpdateStatus(UpdateStatus.Checking, "检查中...");
        ChangelogTextBox.Text = "正在连接 GitHub...";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var versionContent = await _httpClient.GetStringAsync(VersionUrl, cts.Token);

            var versionMatch = ExtractRegex(versionContent, @"VERSION\s*=\s*[""']([^""']+)[""']");
            var buildDateMatch = ExtractRegex(versionContent, @"BUILD_DATE\s*=\s*[""']([^""']+)[""']");

            if (string.IsNullOrEmpty(versionMatch) || string.IsNullOrEmpty(buildDateMatch))
            {
                SetUpdateStatus(UpdateStatus.Error, "无法解析版本信息");
                ChangelogTextBox.Text = "无法解析版本信息";
                return;
            }

            var remoteVersion = versionMatch!;
            var changelog = await FetchChangelog();

            if (remoteVersion != _currentVersion)
            {
                _hasNewVersion = true;
                SetUpdateStatus(UpdateStatus.UpdateAvailable, $"发现新版本: v{remoteVersion}");
                CheckUpdateBtn.Content = "\uE896 下载更新";
                ChangelogTextBox.Text = changelog ?? $"# 版本 {remoteVersion}\n\n暂无详细更新日志";
            }
            else
            {
                SetUpdateStatus(UpdateStatus.Latest, "已是最新版本");
                CheckUpdateBtn.Content = "\uE72C 检查更新";
                ChangelogTextBox.Text = changelog ?? "当前版本已是最新。";
            }
        }
        catch (HttpRequestException ex) when (
            ex.Message.Contains("refused") || ex.Message.Contains("unreachable") || ex.Message.Contains("network"))
        {
            SetUpdateStatus(UpdateStatus.Error, "网络连接失败");
            ChangelogTextBox.Text =
                "无法连接到 GitHub。\n\n可能原因：\n• 网络连接不可用\n• 需要开启代理/VPN 访问 GitHub\n• GitHub 服务暂时不可用\n\n请检查网络后重试。";
        }
        catch (TaskCanceledException)
        {
            SetUpdateStatus(UpdateStatus.Error, "请求超时");
            ChangelogTextBox.Text = "请求超时（15秒），请稍后重试。。";
        }
        catch (Exception ex)
        {
            SetUpdateStatus(UpdateStatus.Error, "检查失败");
            ChangelogTextBox.Text = $"检查更新时发生错误：{ex.Message}";
        }
        finally
        {
            CheckUpdateBtn.IsEnabled = true;
        }
    }

    // ──── 更新日志 ────

    private async Task<string?> FetchChangelog()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return await _httpClient.GetStringAsync(ChangelogUrl, cts.Token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdatePage] 获取更新日志失败: {ex.Message}");
            return null;
        }
    }

    // ──── 下载更新 ────

    private async void StartDownload()
    {
        CheckUpdateBtn.IsEnabled = false;
        SetUpdateStatus(UpdateStatus.Downloading, "正在下载更新...");

        try
        {
            ChangelogTextBox.Text = "114514。";
            await Task.Delay(1500);

            await global::Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/HelloGaoo/ClassLively/releases/latest"));

            SetUpdateStatus(UpdateStatus.Latest, "已就绪");
            CheckUpdateBtn.Content = "\uE72C 检查更新";
        }
        catch (Exception ex)
        {
            SetUpdateStatus(UpdateStatus.Error, "下载失败");
            ChangelogTextBox.Text = $"操作失败：{ex.Message}";
        }
        finally
        {
            CheckUpdateBtn.IsEnabled = true;
            _hasNewVersion = false;
        }
    }

    // ──── 设置开关 ────

    private async void AutoCheck_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch sw) return;

        AppSettings.Set("auto_check_update", sw.IsOn);
        _ = AppSettings.SaveAsync();
        try { await _api.SetConfigAsync("autoCheckUpdate", sw.IsOn); } catch { }

        if (sw.IsOn)
            CheckUpdate_Click(sender, e);
    }

    private async void AutoUpdate_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch sw) return;

        AppSettings.Set("auto_update", sw.IsOn);
        _ = AppSettings.SaveAsync();
        try { await _api.SetConfigAsync("autoUpdate", sw.IsOn); } catch { }
    }

    // ──── 辅助方法 ────

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClassLively-WinUI3/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        return client;
    }

    private static string? ExtractRegex(string input, string pattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(input, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void ApplyConfig(System.Collections.Generic.Dictionary<string, object> config,
        string key, Action<object> setter)
    {
        if (config.TryGetValue(key, out var val) && val != null)
        {
            try { setter(val); } catch { }
        }
    }

    private static bool ToBool(object val) => Convert.ToBoolean(val);
}
