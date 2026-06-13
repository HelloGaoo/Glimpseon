using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ClassLively_UI.Pages;

public sealed partial class AboutPage : Page
{
    private static readonly HttpClient _httpClient = new();

    private const string RemoteVersionUrl =
        "https://ghfile.geekertao.top/https://raw.githubusercontent.com/HelloGaoo/ClassLively/main/version.py";

    private const string RemoteChangelogUrl =
        "https://ghfile.geekertao.top/https://raw.githubusercontent.com/HelloGaoo/ClassLively/main/changelog.md";

    private const string RemoteLicenseUrl =
        "https://ghfile.geekertao.top/https://raw.githubusercontent.com/HelloGaoo/ClassLively/main/LICENSE";

    public AboutPage()
    {
        InitializeComponent();
        LoadVersionInfo();
        _ = LoadChangelogAsync();
    }

    // ──── 版本信息 ────

    private void LoadVersionInfo()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.1.0";

            string buildDate;
            try
            {
                var modulePath = Process.GetCurrentProcess().MainModule?.FileName;
                buildDate = !string.IsNullOrEmpty(modulePath)
                    ? System.IO.File.GetLastWriteTime(modulePath).ToString("yyyy-MM-dd")
                    : "2026-03-14";
            }
            catch
            {
                buildDate = "2026-03-14";
            }

            var displayVersion = $"v{versionStr}  ·  {buildDate}";

            VersionLabel.Text = displayVersion;
            InfoVersionLabel.Text = versionStr;
            InfoDateLabel.Text = $"构建日期：{buildDate}";
            ChangelogVersionLabel.Text = $"v{versionStr}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AboutPage] 读取版本信息失败: {ex.Message}");
            VersionLabel.Text = "v0.1.0  ·  2026-03-14";
            InfoVersionLabel.Text = "0.1.0";
            InfoDateLabel.Text = "构建日期：2026-03-14";
        }
    }

    // ──── 更新日志 ────

    private async Task LoadChangelogAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(RemoteChangelogUrl);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            ChangelogTextBox.Text = content.Length > 2000 ? content[..2000] : content;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AboutPage] 加载更新日志失败: {ex.Message}");
            ChangelogTextBox.Text = "暂无更新日志";
        }
    }

    // ──── 链接导航 ────

    private async void GitHubVisit_Click(object sender, RoutedEventArgs e)
    {
        await global::Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/HelloGaoo/ClassLively"));
    }

    private async void AuthorHomepage_Click(object sender, RoutedEventArgs e)
    {
        await global::Windows.System.Launcher.LaunchUriAsync(new Uri("https://space.bilibili.com/1498602348"));
    }

    // ──── 许可证弹窗 ────

    private async void LicenseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var response = await _httpClient.GetAsync(RemoteLicenseUrl);
            response.EnsureSuccessStatusCode();
            var licenseContent = await response.Content.ReadAsStringAsync();

            var textBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Text = licenseContent,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                MinHeight = 360,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 12, 16, 12)
            };

            var scrollViewer = new ScrollViewer
            {
                Content = textBox,
                MaxHeight = 450
            };

            var dialog = new ContentDialog
            {
                Title = "GNU General Public License v3.0",
                Content = scrollViewer,
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AboutPage] 加载许可证失败: {ex.Message}");
        }
    }

    // ──── 鸣谢弹窗 ────

    private async void CreditsButton_Click(object sender, RoutedEventArgs e)
    {
        var credits = LoadCredits();

        var panel = new StackPanel { Spacing = 6 };
        foreach (var (name, _, licenseName, url) in credits)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBlock = new TextBlock
            {
                Text = string.IsNullOrEmpty(url) ? $"{name}  ({licenseName})" : name,
                FontSize = 13,
                Opacity = 0.85,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            row.Children.Add(textBlock);

            if (!string.IsNullOrEmpty(url))
            {
                var linkBtn = new Button
                {
                    Content = "\uE76C",
                    FontSize = 12,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4),
                    VerticalAlignment = VerticalAlignment.Center
                };
                linkBtn.Click += async (_, _) => await global::Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                Grid.SetColumn(linkBtn, 1);
                row.Children.Add(linkBtn);
            }

            panel.Children.Add(row);
        }

        var dialog = new ContentDialog
        {
            Title = "鸣谢",
            Content = panel,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private static List<(string Name, string Version, string License, string Url)> LoadCredits()
    {
        var creditsPath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "resource", "credits.json");
        if (!System.IO.File.Exists(creditsPath))
            creditsPath = System.IO.Path.Combine(AppContext.BaseDirectory, "resource", "credits.json");

        if (!System.IO.File.Exists(creditsPath))
        {
            return new List<(string, string, string, string)>
            {
                ("Microsoft WinUI 3", "", "MIT", "https://learn.microsoft.com/windows/apps/winui/"),
                ("Python / PyQt6", "", "GPL/BSD", ""),
                ("qfluentwidgets", "", "MIT", "https://github.com/zhiyiYo/PyQt-Fluent-Widgets"),
                ("Open Source Community", "", "-", "")
            };
        }

        try
        {
            var json = System.IO.File.ReadAllText(creditsPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var result = new List<(string, string, string, string)>();

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var type = entry.GetProperty("type").GetString() ?? "";
                var displayName = entry.GetProperty("display_name").GetString() ?? "";
                var licenseName = entry.GetProperty("license").GetString() ?? "";
                var url = entry.GetProperty("url").GetString() ?? "";

                result.Add((displayName, "", licenseName, url));
            }

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AboutPage] 解析 credits.json 失败: {ex.Message}");
            return new();
        }
    }
}
