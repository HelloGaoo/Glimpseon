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

// 关于页

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using Markdown.Avalonia;

namespace Glimpseon.UI;

public class AboutView : UserControl
{
    // 更新状态色 
    private static readonly Dictionary<string, Color> StatusColors = new()
    {
        ["checking"] = Color.Parse("#0078D4"),
        ["error"] = Color.Parse("#FF0000"),
        ["update_available"] = Color.Parse("#FF8C00"),
        ["latest"] = Color.Parse("#107C10"),
        ["downloading"] = Color.Parse("#0078D4"),
    };

    private TextBlock? _updateStatusLabel;
    private Border? _updateStatusDot;
    private Border? _versionCard;
    private Button? _checkUpdateButton;
    private MarkdownScrollViewer? _changelogViewer;
    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    private bool _hasNewVersion;
    private string _newVersion = "";
    private string? _updateUrl;

    public AboutView()
    {
        // 固定视口布局 禁用页面滚动
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(32, 24, 32, 24),
        };

        var title = Common.MakeTitle(AppUtils.Tr("navigation.about"));
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var split = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 20,
            Margin = new Thickness(0, 12),
        };
        var left = BuildLeftPanel();
        var right = BuildRightPanel();
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        split.Children.Add(left);
        split.Children.Add(right);
        Grid.SetRow(split, 1);
        root.Children.Add(split);

        var copyright = new TextBlock
        {
            Text = "© 2025 HelloGaoo. All rights reserved.",
            TextAlignment = TextAlignment.Center,
            FontSize = 12,
            Opacity = 0.6,
        };
        Grid.SetRow(copyright, 2);
        root.Children.Add(copyright);

        Content = root;
        Log.Debug($"[关于] 关于页就绪 {Paths.Version} {Paths.BuildDate}");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _outerScrollSave.Clear();
        foreach (var sv in this.GetVisualAncestors().OfType<ScrollViewer>())
        {
            if (sv.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled &&
                sv.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled)
            {
                continue;
            }
            _outerScrollSave.Add((sv, sv.VerticalScrollBarVisibility, sv.HorizontalScrollBarVisibility));
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var (sv, v, h) in _outerScrollSave)
        {
            sv.VerticalScrollBarVisibility = v;
            sv.HorizontalScrollBarVisibility = h;
        }
        _outerScrollSave.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private readonly List<(ScrollViewer sv, ScrollBarVisibility v, ScrollBarVisibility h)> _outerScrollSave = new();

    // 左栏

    private Control BuildLeftPanel()
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 12,
        };

        var headerCard = Common.MakeCard();
        var headerGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Margin = new Thickness(0, 4, 0, 4),
        };
        try
        {
            var iconPath = Paths.GetResourcePath(Constants.AppIcon);
            if (File.Exists(iconPath))
            {
                headerGrid.Children.Add(new Image
                {
                    Source = new Bitmap(iconPath),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8),
                });
            }
        }
        catch (Exception e)
        {

            Log.Debug($"[关于] 图标缺失留空: {e.Message}");
            // 图标缺失 留空
        }
        var nameText = new TextBlock
        {
            Text = "Glimpseon",
            FontSize = 26,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetRow(nameText, 1);
        headerGrid.Children.Add(nameText);
        var descText = new TextBlock
        {
            Text = AppUtils.Tr("about.description"),
            FontSize = 14,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        Grid.SetRow(descText, 2);
        headerGrid.Children.Add(descText);
        headerCard.Child = headerGrid;
        panel.Children.Add(headerCard);

        // 作者卡
        var authorCard = Common.MakeCard();
        authorCard.Child = new TextBlock
        {
            Text = $"{AppUtils.Tr("about.author")}: HelloGaoo",
            FontSize = 13,
            Opacity = 0.85,
            Margin = new Thickness(16, 10, 16, 10),
        };
        Grid.SetRow(authorCard, 1);
        panel.Children.Add(authorCard);

        // 链接卡
        var linksCard = Common.MakeCard();
        var linksStack = new StackPanel { Spacing = 8, Margin = new Thickness(16, 10, 16, 10) };
        linksStack.Children.Add(MakeLinkRow(FASymbol.Code, AppUtils.Tr("about.github_repo"),
            "github.com/HelloGaoo/Glimpseon", "https://github.com/HelloGaoo/Glimpseon", null));
        linksStack.Children.Add(MakeLinkRow(FASymbol.People, AppUtils.Tr("about.author_homepage"),
            "space.bilibili.com/1498602348", "https://space.bilibili.com/1498602348", null));
        linksStack.Children.Add(MakeLinkRow(FASymbol.Document, "GPL v3.0",
            "GNU General Public License v3.0", null, ViewLicense));
        linksStack.Children.Add(MakeLinkRow(FASymbol.Like, AppUtils.Tr("about.thanks"),
            AppUtils.Tr("about.thanks_desc"), null, ShowTechDialog));
        linksCard.Child = linksStack;
        Grid.SetRow(linksCard, 2);
        panel.Children.Add(linksCard);

        // 更新设置卡: 自动检查/自动更新/更新通道
        var autoCheckRow = MakeSwitchRow(FASymbol.Sync, AppUtils.Tr("update.auto_check"),
            AppUtils.Tr("update.auto_check_desc"), Config.AutoCheckUpdate);
        Grid.SetRow(autoCheckRow, 3);
        panel.Children.Add(autoCheckRow);
        var autoUpdateRow = MakeSwitchRow(FASymbol.Download, AppUtils.Tr("update.auto_update"),
            AppUtils.Tr("update.auto_update_desc"), Config.AutoUpdate);
        Grid.SetRow(autoUpdateRow, 4);
        panel.Children.Add(autoUpdateRow);
        var channelRow = MakeChannelRow();
        Grid.SetRow(channelRow, 5);
        panel.Children.Add(channelRow);
        return panel;
    }

    private static Control MakeLinkRow(FASymbol icon, string title, string desc, string? url, Action? callback)
    {
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2) };
        var visitButton = new Button
        {
            Content = AppUtils.Tr("about.visit"),
            Height = 30,
            Padding = new Thickness(14, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(visitButton, Dock.Right);
        row.Children.Add(visitButton);

        if (url is not null)
        {
            visitButton.Click += (_, _) => OpenUrl(url);
        }
        else if (callback is not null)
        {
            visitButton.Click += (_, _) => callback();
        }

        row.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new FASymbolIcon { Symbol = icon, FontSize = 20, VerticalAlignment = VerticalAlignment.Center },
                new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.Medium },
                        new TextBlock { Text = desc, FontSize = 12, Opacity = 0.6 },
                    },
                },
            },
        });
        return row;
    }

    // 右栏

    private Control BuildRightPanel()
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 12,
        };

        // 版本卡
        var versionCard = Common.MakeCard();
        _versionCard = versionCard;
        var versionDock = new DockPanel { LastChildFill = true, Margin = new Thickness(20, 14, 20, 14) };

        _checkUpdateButton = new Button
        {
            Content = AppUtils.Tr("update.check_update"),
            Height = 36,
            Padding = new Thickness(16, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _checkUpdateButton.Click += (_, _) => CheckUpdateManual();
        DockPanel.SetDock(_checkUpdateButton, Dock.Right);

        // 状态点 状态文字
        _updateStatusDot = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.Parse("#999999")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _updateStatusLabel = new TextBlock
        {
            Text = AppUtils.Tr("update.status_ready"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var statusStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        statusStack.Children.Add(_updateStatusDot);
        statusStack.Children.Add(_updateStatusLabel);
        DockPanel.SetDock(statusStack, Dock.Right);

        var leftInfo = new StackPanel { Spacing = 2 };
        leftInfo.Children.Add(new TextBlock
        {
            Text = $"Glimpseon {Paths.Version}",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
        });
        leftInfo.Children.Add(new TextBlock
        {
            Text = $"{AppUtils.Tr("update.build_date")}: {Paths.BuildDate}",
            FontSize = 12,
            Opacity = 0.6,
        });

        versionDock.Children.Add(_checkUpdateButton);
        versionDock.Children.Add(statusStack);
        versionDock.Children.Add(leftInfo);
        versionCard.Child = versionDock;
        panel.Children.Add(versionCard);

        // 更新日志卡
        var changelogCard = Common.MakeCard();
        var changelogGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 8,
            Margin = new Thickness(20, 14, 20, 14),
        };
        changelogGrid.Children.Add(new TextBlock
        {
            Text = AppUtils.Tr("update.changelog"),
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
        });
        // Markdown.Avalonia ClassIsland fork 12.0.0 适配 Avalonia 12 11.x 版本二进制不兼容勿回退
        // 填满卡片剩余高度 溢出在日志区内部滚动
        _changelogViewer = new MarkdownScrollViewer
        {
            Markdown = AppUtils.Tr("update.changelog_auto_load"),
        };
        Grid.SetRow(_changelogViewer, 1);
        changelogGrid.Children.Add(_changelogViewer);
        changelogCard.Child = changelogGrid;
        Grid.SetRow(changelogCard, 1);
        panel.Children.Add(changelogCard);

        return panel;
    }

    private static Control MakeSwitchRow(FASymbol icon, string title, string desc, ConfigItem<bool> item)
    {
        var card = Common.MakeCard();
        var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(16, 10, 16, 10) };
        var toggle = new ToggleSwitch
        {
            IsChecked = item.Value,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.IsCheckedChanged += (_, _) => item.Value = toggle.IsChecked == true;
        DockPanel.SetDock(toggle, Dock.Right);
        dock.Children.Add(toggle);
        dock.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new FASymbolIcon { Symbol = icon, FontSize = 20, VerticalAlignment = VerticalAlignment.Center },
                new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.Medium },
                        new TextBlock { Text = desc, FontSize = 12, Opacity = 0.6 },
                    },
                },
            },
        });
        card.Child = dock;
        return card;
    }

    // 更新通道选择 仅为设置项占位 尚无实际功能
    private static Control MakeChannelRow()
    {
        var card = Common.MakeCard();
        var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(16, 10, 16, 10) };

        var combo = new ComboBox
        {
            MinWidth = 120,
            VerticalAlignment = VerticalAlignment.Center,
        };
        combo.Items.Add(AppUtils.Tr("update.channel_stable"));
        combo.Items.Add(AppUtils.Tr("update.channel_beta"));
        combo.SelectedIndex = Config.UpdateChannel.Value == "beta" ? 1 : 0;
        combo.SelectionChanged += (_, _) =>
            Config.UpdateChannel.Value = combo.SelectedIndex == 1 ? "beta" : "stable";
        DockPanel.SetDock(combo, Dock.Right);

        dock.Children.Add(combo);
        dock.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new FASymbolIcon { Symbol = FASymbol.Globe, FontSize = 20, VerticalAlignment = VerticalAlignment.Center },
                new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = AppUtils.Tr("update.channel"), FontSize = 14, FontWeight = FontWeight.Medium },
                        new TextBlock { Text = AppUtils.Tr("update.channel_desc"), FontSize = 12, Opacity = 0.6 },
                    },
                },
            },
        });
        card.Child = dock;
        return card;
    }

    // 更新状态

    private void SetUpdateStatus(string status)
    {
        var color = StatusColors.GetValueOrDefault(status, Color.Parse("#999999"));
        Dispatcher.UIThread.Post(() =>
        {
            if (_updateStatusDot is not null)
            {
                _updateStatusDot.Background = new SolidColorBrush(color);
            }
            if (_updateStatusLabel is not null)
            {
                _updateStatusLabel.Foreground = new SolidColorBrush(color);
            }
        });
        Log.Debug($"[关于] 更新状态切换: {status}");
    }

    // 更新日志

    private void LoadChangelog(bool autoLoad = false)
    {
        if (autoLoad && !Config.AutoCheckUpdate.Value)
        {
            Log.Debug("[关于] 自动加载更新日志已关闭");
            return;
        }
        _ = Task.Run(async () =>
        {
            string text;
            try
            {
                var changelog = await Updater.GetGithubChangelogAsync();
                text = string.IsNullOrEmpty(changelog) ? AppUtils.Tr("update.no_changelog") : changelog!;
                Log.Info($"{(autoLoad ? "自动" : "手动")}加载更新日志");
            }
            catch (Exception e)
            {
                Log.Error($"加载更新日志失败 {e.Message}");
                text = AppUtils.Tr("update.load_failed");
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (_changelogViewer is not null)
                {
                    _changelogViewer.Markdown = text;
                }
            });
        });
    }

    // 检查更新

    public void CheckUpdateManual() => RunCheck(autoCheck: false);

    public void CheckUpdateAuto() => RunCheck(autoCheck: true);

    private void RunCheck(bool autoCheck)
    {
        if (_hasNewVersion)
        {
            Log.Debug($"[关于] 已知新版本 {_newVersion} 直接下载");
            _ = DownloadUpdateAsync(autoCheck);
            return;
        }

        Log.Info($"{(autoCheck ? "自动" : "手动")}检查版本");
        if (!autoCheck)
        {
            SetButtonEnabled(false);
            SetStatusText(AppUtils.Tr("update.checking"));
            SetUpdateStatus("checking");
        }

        _ = Task.Run(async () =>
        {
            Updater.UpdateCheckResult result;
            try
            {
                result = await Updater.CheckGithubVersionAsync();
            }
            catch (Exception e)
            {
                Log.Error($"检查更新出错 - {e.Message}");
                result = new Updater.UpdateCheckResult { Success = false, Error = e.Message };
            }
            Dispatcher.UIThread.Post(() => OnCheckResult(result, autoCheck));
        });
    }

    private void OnCheckResult(Updater.UpdateCheckResult result, bool autoCheck)
    {
        try
        {
            if (!result.Success)
            {
                Log.Warning($"检查版本失败 - {result.Error}");
                if (!autoCheck)
                {
                    SetButtonEnabled(true);
                    SetStatusText(AppUtils.Tr("update.check_failed",
                        ("error", result.Error ?? AppUtils.Tr("update.unknown_error"))));
                    SetUpdateStatus("error");
                }
                return;
            }

            var githubVersion = result.Version ?? "";
            Log.Info($"检查结果 最新版本 {githubVersion}");

            var hasUpdate = IsNewerVersion(githubVersion, Paths.Version);

            if (hasUpdate)
            {
                Log.Info($"[关于] 发现新版本: {githubVersion} (当前 {Paths.Version})");
                _hasNewVersion = true;
                _newVersion = githubVersion;
                _updateUrl = result.DownloadUrl;

                SetStatusText(AppUtils.Tr("update.new_version_found", ("version", githubVersion)));
                SetUpdateStatus("update_available");

                if (!autoCheck)
                {
                    SetButtonEnabled(true);
                    if (_checkUpdateButton is not null)
                    {
                        _checkUpdateButton.Content = AppUtils.Tr("update.download");
                    }
                }

                if (!string.IsNullOrEmpty(result.Changelog))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_changelogViewer is not null)
                        {
                            _changelogViewer.Markdown = result.Changelog!;
                        }
                    });
                }

                if (autoCheck && Config.AutoUpdate.Value)
                {
                    Log.Info("自动检查 启用自动更新 下载");
                    _ = Task.Delay(2000).ContinueWith(_ => Dispatcher.UIThread.Post(
                        () => _ = DownloadUpdateAsync(autoUpdate: true)), TaskScheduler.Default);
                }
            }
            else
            {
                Log.Info("已是最新版本");
                SetStatusText(AppUtils.Tr("update.latest"));
                SetUpdateStatus("latest");
                if (!autoCheck)
                {
                    SetButtonEnabled(true);
                }
                if (!string.IsNullOrEmpty(result.Changelog))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_changelogViewer is not null)
                        {
                            _changelogViewer.Markdown = result.Changelog!;
                        }
                    });
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"更新 ui 失败 {e.Message}");
        }
    }

    // 版本比较
    private static bool IsNewerVersion(string remote, string current)
    {
        try
        {
            static int[] Parse(string v) => v.Trim().TrimStart('v', 'V')
                .Split('.').Select(p => int.Parse(p)).ToArray();
            var r = Parse(remote);
            var c = Parse(current);
            for (var i = 0; i < Math.Max(r.Length, c.Length); i++)
            {
                var rv = i < r.Length ? r[i] : 0;
                var cv = i < c.Length ? c[i] : 0;
                if (rv != cv)
                {
                    return rv > cv;
                }
            }
            Log.Debug($"[关于] 版本比较: 远端 {remote} vs 当前 {current} -> 无更新");
            return false;
        }
        catch (Exception)
        {
            Log.Debug($"[关于] 版本号不可解析 remote={remote} current={current}");
            return false;
        }
    }

    // 下载更新

    private async Task DownloadUpdateAsync(bool autoUpdate = false)
    {
        Log.Info($"[关于] 下载更新 自动={autoUpdate} 目标版本={_newVersion}");
        SetButtonEnabled(false);
        SetStatusText(AppUtils.Tr("update.downloading"));
        SetUpdateStatus("downloading");

        var backupFolder = Path.Combine(Paths.PackageRoot, "update_backup");
        try
        {
            if (string.IsNullOrEmpty(_updateUrl))
            {
                throw new InvalidOperationException("下载更新失败");
            }

            var milestone = -1;
            var downloadPath = await Updater.DownloadUpdateAsync(_updateUrl, (current, total) =>
            {
                var percent = current * 100.0 / total;
                SetStatusText(AppUtils.Tr("update.downloading_progress", ("percent", percent.ToString("F1"))));
                if ((int)(percent / 25) > milestone)
                {
                    milestone = (int)(percent / 25);
                    Log.Info($"[关于] 下载进度: {percent:F1}% ({current}/{total}字节)");
                }
            });
            if (downloadPath is null)
            {
                throw new InvalidOperationException("下载更新失败");
            }

            SetStatusText(AppUtils.Tr("update.extracting"));

            // 按新版本号解压到 app-<version> 写入 record.json
            var newVersionDir = Updater.ExtractUpdate(downloadPath, _newVersion);
            if (newVersionDir is null)
            {
                throw new InvalidOperationException("解压更新失败");
            }

            Updater.CleanupUpdateFiles();
            Log.Info($"[关于] 更新包已解压 {newVersionDir}");

            if (autoUpdate)
            {
                SetStatusText(AppUtils.Tr("update.backing_up"));
                try
                {
                    if (Directory.Exists(backupFolder))
                    {
                        Directory.Delete(backupFolder, recursive: true);
                    }
                    CopyDirectory(Paths.AppDir, backupFolder);
                    Log.Info("已创建版本备份");
                }
                catch (Exception e)
                {
                    Log.Warning($"创建备份失败 {e.Message}");
                }
            }

            if (!Updater.DeployUpdate(newVersionDir))
            {
                throw new InvalidOperationException("激活新版本失败");
            }

            var scriptPath = Updater.CreateUpdateScript(newVersionDir);
            if (string.IsNullOrEmpty(scriptPath))
            {
                throw new InvalidOperationException("创建更新脚本失败");
            }
            Log.Info($"[关于] 更新脚本已创建 即将重启应用: {scriptPath}");
            SetStatusText(AppUtils.Tr("update.preparing"));

            // 最小化窗口启动更新提示脚本后退出
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"Glimpseon Update\" /MIN \"{scriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
            };
            Process.Start(psi);

            SetStatusText(AppUtils.Tr("update.complete"));
            Log.Info("[关于] 更新完成 退出应用");
            Dispatcher.UIThread.Post(() => App.Lifetime?.Shutdown());
        }
        catch (Exception e)
        {
            Log.Error($"更新失败 {e.Message}");
            Updater.CleanupUpdateFiles();
            UpdateErrorState(e.Message);
            _hasNewVersion = false;
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var name = Path.GetFileName(rel);
            if (rel.Contains("update_temp") || rel.Contains("update_backup") || rel.Contains("logs") ||
                name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private void UpdateErrorState(string msg)
    {
        Log.Warning($"[关于] 更新流程失败 已切换到重试状态: {msg}");
        if (_checkUpdateButton is not null)
        {
            _checkUpdateButton.Content = AppUtils.Tr("update.retry");
            _checkUpdateButton.IsEnabled = true;
        }
        SetStatusText(AppUtils.Tr("update.failed", ("error", msg)));
        SetUpdateStatus("error");
    }

    private void SetButtonEnabled(bool enabled)
    {
        if (_checkUpdateButton is not null)
        {
            _checkUpdateButton.IsEnabled = enabled;
        }
    }

    private void SetStatusText(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_updateStatusLabel is not null)
            {
                _updateStatusLabel.Text = text;
            }
        });
    }

    // 许可证/鸣谢

    private void ViewLicense()
    {
        Log.Info("[关于] 打开许可证信息");
        var licensePath = Paths.GetResourcePath("LICENSE");
        if (!File.Exists(licensePath))
        {
            licensePath = Path.Combine(Paths.AppDir, "LICENSE");
        }
        ShowTextWindow(AppUtils.Tr("about.license_title"),
            AppUtils.Tr("about.license_intro"),
            File.Exists(licensePath) ? File.ReadAllText(licensePath) : "LICENSE not found");
    }

    private void ShowTechDialog()
    {
        Log.Info("[关于] 打开鸣谢列表");
        var deps = GetDependencies();
        var list = new ListBox { MaxHeight = 360, MinWidth = 480 };
        foreach (var (display, url) in deps)
        {
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(4) };
            var itemButton = new Button
            {
                Content = AppUtils.Tr("common.link"),
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(itemButton, Dock.Right);
            if (string.IsNullOrEmpty(url))
            {
                itemButton.IsVisible = false;
            }
            else
            {
                itemButton.Click += (_, _) => OpenUrl(url);
            }
            row.Children.Add(itemButton);
            row.Children.Add(new TextBlock
            {
                Text = display,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            });
            list.Items.Add(row);
        }
        var dialog = new Window
        {
            Title = AppUtils.Tr("about.thanks"),
            Width = 540,
            Height = 480,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = AppUtils.Tr("about.thanks"),
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold,
                    },
                    list,
                },
            },
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.Show(OwnerWindow);
    }

    // 读 credits.json
    private static List<(string Display, string Url)> GetDependencies()
    {
        var result = new List<(string, string)>();
        var path = Paths.GetResourcePath(Path.Combine("Assets", "credits.json"));
        if (!File.Exists(path))
        {
            Log.Debug($"[关于] credits.json 不存在: {path}");
            return result;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var display = entry.TryGetProperty("display_name", out var d) ? d.GetString() ?? "" : "";
                var license = entry.TryGetProperty("license", out var l) ? l.GetString() ?? "" : "";
                var url = entry.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                result.Add(($"{display}  ({license})", url));
            }
            Log.Debug($"[关于] 鸣谢已加载 {result.Count}项");
        }
        catch (Exception e)
        {
            Log.Debug($"[关于] credits.json 解析失败: {e.Message}");
        }
        return result;
    }

    private void ShowTextWindow(string title, string intro, string content)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 640,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var stack = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = intro, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBox
        {
            Text = content,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 420,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas, monospace"),
        });
        dialog.Content = stack;
        dialog.Show(OwnerWindow);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Debug($"[关于] 打开链接失败 {url}: {e.Message}");
        }
    }
}
