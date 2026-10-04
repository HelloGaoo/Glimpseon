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

// 软件下载页

using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

public class DownloadView : UserControl
{
    private const double CardMinWidth = 320;
    private const double GridGap = 12;

    private readonly Downloader _downloader = new();
    private readonly HashSet<string> _downloadingNames = new();
    private readonly Dictionary<string, double> _progress = new();
    private readonly Dictionary<string, double> _lastProgressLog = new();
    private readonly Dictionary<string, CardControls> _cards = new();
    private readonly List<(StackPanel SectionHost, Grid Grid, List<Control> Cards)> _sections = [];

    private readonly object _stateLock = new();
    private string _mode = "single";
    private StackPanel? _infoBarHost;
    private Button? _selectAllButton;

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    private sealed record CardControls(StackPanel StatusPanel, CheckBox SelectBox, Button DownloadButton,
        RingProgress Ring, TextBlock RingPercent, Panel RingHost)
    {
        public bool Checked;
    }

    public DownloadView()
    {
        DownloadSources.SetDownloadSrc(Config.DownloadSource.Value);

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(Common.MakeTitle(AppUtils.Tr("navigation.download")));

        _infoBarHost = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(_infoBarHost);

        // 模式行 .mode-row margin 8/8 min-height 36
        var startBatchButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FASymbolIcon { Symbol = FASymbol.Play, FontSize = 16 },
                    new TextBlock { Text = AppUtils.Tr("download.start_download"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                },
            },
            HorizontalAlignment = HorizontalAlignment.Right,
            Height = 36,
            Padding = new Thickness(0, 0, 16, 0),
            IsVisible = false,
            Background = new SolidColorBrush(Color.Parse("#00C853")),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var selectAllButton = new Button
        {
            IsVisible = false,
            Height = 36,
            Padding = new Thickness(16, 0, 16, 0),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
        };
        _selectAllButton = selectAllButton;
        selectAllButton.Click += (_, _) => ToggleSelectAll();

        var singleRadio = new RadioButton
        {
            Content = AppUtils.Tr("download.single_mode"),
            GroupName = "dl-mode",
            IsChecked = true,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var multiRadio = new RadioButton
        {
            Content = AppUtils.Tr("download.multi_mode"),
            GroupName = "dl-mode",
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        singleRadio.IsCheckedChanged += (_, _) => { if (singleRadio.IsChecked == true) SetMode("single"); };
        multiRadio.IsCheckedChanged += (_, _) => { if (multiRadio.IsChecked == true) SetMode("multi"); };

        var sourceCombo = new ComboBox
        {
            Width = 200,
            Height = 32,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ComboBoxItem? currentItem = null;
        foreach (var (key, (nameKey, _)) in DownloadSources.Sources)
        {
            var item = new ComboBoxItem { Content = AppUtils.Tr(nameKey), Tag = key };
            sourceCombo.Items.Add(item);
            if (key == DownloadSources.CurrentSource)
            {
                currentItem = item;
            }
        }
        sourceCombo.SelectedItem = currentItem;
        sourceCombo.SelectionChanged += (_, _) =>
        {
            if (sourceCombo.SelectedItem is ComboBoxItem { Tag: { } tag })
            {
                var key = tag.ToString()!;
                DownloadSources.SetDownloadSrc(key);
                Config.DownloadSource.Value = key;
                Log.Info($"[下载] 切换下载源: {key}");
            }
        };

        var modeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
            Children =
            {
                new TextBlock
                {
                    Text = AppUtils.Tr("download.select_mode") + ":",
                    FontSize = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                singleRadio,
                multiRadio,
                selectAllButton,
                new TextBlock
                {
                    Text = AppUtils.Tr("download.download_source") + ":",
                    FontSize = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0),
                },
                sourceCombo,
            },
        };

        var dock = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(startBatchButton, Dock.Right);
        dock.Children.Add(startBatchButton);
        dock.Children.Add(modeRow);
        stack.Children.Add(dock);

        // 分区 .section-title 20/600 + .section-grid 动态列
        foreach (var category in DownloadCatalog.Categories)
        {
            stack.Children.Add(new TextBlock
            {
                Text = AppUtils.Tr(category.NameKey),
                FontSize = 20,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 20, 0, 10),
            });

            var grid = new Grid
            {
                ColumnSpacing = GridGap,
                RowSpacing = GridGap,
                Margin = new Thickness(0, 0, 0, 16),
            };
            var sectionHost = new StackPanel();
            var cardList = new List<Control>();
            foreach (var software in category.Software)
            {
                var card = BuildSoftwareCard(software);
                cardList.Add(card);
            }
            _sections.Add((sectionHost, grid, cardList));
            sectionHost.Children.Add(grid);
            stack.Children.Add(sectionHost);
        }

        // 窗口尺寸变化重排列
        AttachedToLogicalTree += (_, _) => Dispatcher.UIThread.Post(Relayout);
        DetachedFromLogicalTree += (_, _) => { };
        SizeChanged += (_, _) => Relayout();

        Content = Common.MakePageScroll(stack);

        startBatchButton.Click += (_, _) => HandleStartDownload();

        void SetMode(string mode)
        {
            _mode = mode;
            selectAllButton.IsVisible = mode == "multi";
            startBatchButton.IsVisible = mode == "multi";
            foreach (var name in _cards.Keys)
            {
                ApplyCardModeUi(name);
            }
            UpdateSelectAllLabel();
            Log.Debug($"[下载] 模式切换: {mode}");
        }
    }

    // 列数计算
    private static int CalcCols(double avail)
    {
        if (avail <= CardMinWidth)
        {
            return 1;
        }
        return Math.Max(1, (int)Math.Floor((avail + GridGap) / (CardMinWidth + GridGap)));
    }

    // 重排列
    private void Relayout()
    {
        var avail = Math.Max(0, Bounds.Width - 120);
        if (avail <= 0)
        {
            return;
        }
        var cols = CalcCols(avail);
        foreach (var (_, grid, cards) in _sections)
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (var i = 0; i < cols; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            }
            for (var row = 0; row < (cards.Count + cols - 1) / cols; row++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }
            for (var i = 0; i < cards.Count; i++)
            {
                Grid.SetRow(cards[i], i / cols);
                Grid.SetColumn(cards[i], i % cols);
                grid.Children.Add(cards[i]);
            }
        }
    }

    // 卡片右列按当前模式与下载状态重建子项集合
    private void ApplyCardModeUi(string name)
    {
        if (!_cards.TryGetValue(name, out var c))
        {
            return;
        }
        c.StatusPanel.Children.Clear();
        if (_downloadingNames.Contains(name))
        {
            // 下载中 仅进度环
            c.Ring.IsVisible = true;
            c.RingPercent.IsVisible = true;
            c.StatusPanel.Children.Add(c.RingHost);
        }
        else if (_mode == "multi")
        {
            c.SelectBox.IsVisible = true;
            c.StatusPanel.Children.Add(c.SelectBox);
        }
        else
        {
            c.DownloadButton.IsVisible = true;
            c.StatusPanel.Children.Add(c.DownloadButton);
        }
    }

    private Control BuildSoftwareCard(SoftwareEntry software)
    {
        Color accent;
        try
        {
            accent = Color.Parse(Config.ThemeColor.Value);
        }
        catch (Exception e)
        {

            Log.Debug($"[下载] 主题色解析失败 用默认: {e.Message}");
            accent = Color.Parse("#30c361");
        }

        // .card img.icon 64x64
        var icon = new Image
        {
            Width = 64,
            Height = 64,
            VerticalAlignment = VerticalAlignment.Center,
        };
        try
        {
            var iconPath = DownloadCatalog.GetSoftwareIconPath(software.Icon);
            if (File.Exists(iconPath))
            {
                icon.Source = new Bitmap(iconPath);
            }
        }
        catch (Exception e)
        {

            Log.Debug($"[下载] 图标缺失留空: {e.Message}");
            // 图标缺失 留占位
        }

        // .name-row gap 6: .name 18/600 单行省略 + .link-btn 20x20
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        nameRow.Children.Add(new TextBlock
        {
            Text = software.Name,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrEmpty(software.Link))
        {
            var linkButton = new Button
            {
                Content = new FASymbolIcon { Symbol = FASymbol.Link, FontSize = 14, Opacity = 0.75 },
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(linkButton, AppUtils.Tr("download.open_official_website"));
            linkButton.Click += (_, _) => OpenLink(software.Link!);
            nameRow.Children.Add(linkButton);
        }

        // .desc 14px height 40 line-height 20 两行截断 color sub
        var infoPanel = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(nameRow);
        infoPanel.Children.Add(new TextBlock
        {
            Text = software.Description,
            FontSize = 14,
            Opacity = 0.55,
            MaxHeight = 40,
            LineHeight = 20,
            MaxLines = 2,
            TextTrimming = TextTrimming.WordEllipsis,
            TextWrapping = TextWrapping.Wrap,
        });

        // .ring 60x60 中心 pct 13/600 绿色
        var ringPercent = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var ring = new RingProgress
        {
            Width = 60,
            Height = 60,
            Value = 0,
            IsVisible = false,
        };
        var ringHost = new Panel
        {
            Width = 60,
            Height = 60,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { ring, ringPercent },
        };

        // .btn-download 高36 padding 0/14 accent 白字
        var downloadButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FASymbolIcon { Symbol = FASymbol.Download, FontSize = 16 },
                    new TextBlock { Text = AppUtils.Tr("download.download_btn"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                },
            },
            Height = 36,
            Padding = new Thickness(14, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(accent),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(6),
        };

        // .checkbox 40x40 (.box 20x20 radius 6 checked 填 accent)
        var selectBox = new CheckBox
        {
            IsVisible = false,
            Width = 40,
            Height = 40,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // .card 高100 radius8 padding 16/20 gap 16 bg card_bd
        var card = new Border
        {
            Height = 100,
            Padding = new Thickness(20, 0, 20, 0),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#0DFFFFFF")),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#14FFFFFF")),
            VerticalAlignment = VerticalAlignment.Top,
        };
        // Grid[*,Auto] 右列水平排
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // 内层 Grid[Auto,*]:
        var infoGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 16,
            Children = { icon, infoPanel },
        };
        Grid.SetColumn(infoPanel, 1);
        var cardGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { infoGrid, statusPanel },
        };
        Grid.SetColumn(statusPanel, 1);
        card.Child = cardGrid;

        downloadButton.Click += async (_, _) =>
        {
            if (_downloadingNames.Contains(software.Name))
            {
                return;
            }
            await HandleDownloadAsync(software);
        };
        selectBox.IsCheckedChanged += (_, _) =>
        {
            var c = _cards[software.Name];
            c.Checked = selectBox.IsChecked == true;
            UpdateSelectAllLabel();
        };

        var controls = new CardControls(statusPanel, selectBox, downloadButton, ring, ringPercent, ringHost);
        _cards[software.Name] = controls;
        ApplyCardModeUi(software.Name);
        return card;
    }

    private void ToggleSelectAll()
    {
        var avail = _cards.Where(kv => !_downloadingNames.Contains(kv.Key)).ToList();
        var allChecked = avail.Count > 0 && avail.All(kv => kv.Value.Checked);
        foreach (var (_, c) in avail)
        {
            c.Checked = !allChecked;
            c.SelectBox.IsChecked = !allChecked;
        }
        UpdateSelectAllLabel();
    }

    private void UpdateSelectAllLabel()
    {
        if (_selectAllButton is null)
        {
            return;
        }
        var avail = _cards.Where(kv => !_downloadingNames.Contains(kv.Key)).ToList();
        var allChecked = avail.Count > 0 && avail.All(kv => kv.Value.Checked);
        _selectAllButton.Content = allChecked
            ? AppUtils.Tr("download.deselect_all")
            : AppUtils.Tr("download.select_all");
    }

    // 下载流程

    private async Task HandleDownloadAsync(SoftwareEntry software)
    {
        Log.Info($"[下载] 请求下载: {software.Name}");
        var ok = await Common.Confirm(OwnerWindow, AppUtils.Tr("download.confirm_download"),
            AppUtils.Tr("download.confirm_download_single", ("name", software.Name)));
        if (!ok)
        {
            return;
        }
        ShowToast(FAInfoBarSeverity.Success,
            AppUtils.Tr("download.starting_download"),
            AppUtils.Tr("download.downloading_single", ("name", software.Name)));
        BeginDownloadUi(software.Name);
        _ = Task.Run(() => PrepareAndInstallAsync(software.Name, delayComplete: true));
    }

    private async void HandleStartDownload()
    {
        var selected = _cards
            .Where(kv => kv.Value.Checked && !_downloadingNames.Contains(kv.Key))
            .Select(kv => kv.Key)
            .ToList();
        if (selected.Count == 0)
        {
            Log.Info("[下载] 批量下载: 未选择任何软件");
            ShowToast(FAInfoBarSeverity.Warning,
                AppUtils.Tr("download.no_selection"), AppUtils.Tr("download.please_select_first"));
            return;
        }
        Log.Info($"[下载] 批量下载确认: 共{selected.Count}个 - {string.Join(", ", selected)}");
        var softwareList = string.Join("\n", selected);
        var ok = await Common.Confirm(OwnerWindow, AppUtils.Tr("download.confirm_download"),
            AppUtils.Tr("download.confirm_batch", ("list", softwareList)));
        if (!ok)
        {
            return;
        }
        ShowToast(FAInfoBarSeverity.Success,
            AppUtils.Tr("download.starting_download"),
            AppUtils.Tr("download.downloading_batch", ("count", selected.Count.ToString())));

        foreach (var name in selected)
        {
            BeginDownloadUi(name);
        }

        // 线程池并发
        var maxWorkers = Math.Max(1, Environment.ProcessorCount);
        Log.Info($"[下载] 批量下载启动: {selected.Count}个任务 最大并发{maxWorkers}");
        var gate = new SemaphoreSlim(maxWorkers);
        var tasks = selected.Select(async name =>
        {
            await gate.WaitAsync();
            try
            {
                await PrepareAndInstallAsync(name, delayComplete: false);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        _ = Task.WhenAll(tasks).ContinueWith(_ =>
        {
            gate.Dispose();
            Log.Info("[下载] 批量下载全部任务结束");
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var (name, c) in _cards)
                {
                    if (!_downloadingNames.Contains(name))
                    {
                        c.Checked = false;
                        c.SelectBox.IsChecked = false;
                    }
                }
                UpdateSelectAllLabel();
            });
        }, TaskScheduler.Default);
    }

    // 消息条
    private void ShowToast(FAInfoBarSeverity severity, string title, string content)
    {
        var host = _infoBarHost;
        if (host is null)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            var bar = new FAInfoBar
            {
                Severity = severity,
                Title = title,
                Message = content,
                IsOpen = true,
                IsClosable = true,
                Margin = new Thickness(0, 0, 0, 4),
            };
            bar.Closed += (_, _) => host.Children.Remove(bar);
            host.Children.Add(bar);
            _ = Task.Delay(5000).ContinueWith(_ =>
                Dispatcher.UIThread.Post(() => bar.IsOpen = false), TaskScheduler.Default);
        });
    }

    private void BeginDownloadUi(string softwareName)
    {
        Log.Debug($"[下载] 界面进入下载中状态: {softwareName}");
        lock (_stateLock)
        {
            _downloadingNames.Add(softwareName);
            _progress[softwareName] = 0;
        }
        SetCardState(softwareName, downloading: true, percent: 0);
        UpdateSelectAllLabel();
    }

    private void SetCardState(string softwareName, bool downloading, int percent = 0)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_cards.TryGetValue(softwareName, out var c))
            {
                return;
            }
            if (downloading)
            {
                c.Ring.Value = percent;
                c.RingPercent.Text = $"{percent}%";
            }
            ApplyCardModeUi(softwareName);
        });
    }

    // 名称规范化 去空格与方括号
    private static string NormalizeName(string name) => name.Replace(" ", "").Replace("[", "").Replace("]", "");

    // 查缓存配置 filename 前缀匹配
    private static DownloadTask? FindCacheFile(string softwareName)
    {
        var matchName = NormalizeName(softwareName);
        foreach (var entry in DownloadCatalog.UrlDir)
        {
            var matchFilename = NormalizeName(entry.Filename);
            if (matchFilename.StartsWith(matchName, StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug($"[下载] 缓存配置命中: {softwareName} -> {entry.Filename}");
                return new DownloadTask(softwareName, entry.Filename, entry.Url, entry.GithubPath);
            }
        }
        Log.Debug($"[下载] 缓存配置未命中: {softwareName} (匹配键: {matchName})");
        return null;
    }

    // 查缓存链接后调安装
    private async Task PrepareAndInstallAsync(string softwareName, bool delayComplete)
    {
        try
        {
            var cacheFile = FindCacheFile(softwareName);
            if (cacheFile is null)
            {
                Log.Warning($"{softwareName}: 未找到对应下载配置");
                NotifyError(softwareName, AppUtils.Tr("download.error_no_url"));
                return;
            }
            Log.Debug($"{softwareName}: 下载链接就绪: {cacheFile.Url ?? cacheFile.GithubPath}");
            await RunInstallAsync(softwareName, cacheFile, delayComplete);
        }
        catch (Exception e)
        {
            Log.Error($"{softwareName}: 安装前准备异常 - {e.Message}");
            NotifyError(softwareName, e.Message);
        }
    }

    // 子调用安装
    private async Task RunInstallAsync(string softwareName, DownloadTask cacheFile, bool delayComplete)
    {
        // 进度路由
        void UpdateProgress(string name, double percent)
        {
            var val = (int)Math.Round(percent);
            lock (_stateLock)
            {
                _progress[softwareName] = val;
                if (!_lastProgressLog.TryGetValue(softwareName, out var last) || val - last >= 25)
                {
                    _lastProgressLog[softwareName] = val;
                    Log.Debug($"[下载] {softwareName}: 进度里程碑 {val}%");
                }
            }
            SetCardState(softwareName, downloading: true, percent: val);
        }

        void OnDownloadDone(string name) => Log.Info($"{softwareName}: 已下载");

        try
        {
            Log.Info($"{softwareName}: 调用安装方法");
            _downloader.ResetProgress(softwareName);
            await _downloader.InstallSoftwareAsync(cacheFile, UpdateProgress, OnDownloadDone);
            _progress[softwareName] = 100;
            Log.Debug($"[下载] {softwareName}: 安装流程走完 进度置 100%");
            if (delayComplete)
            {
                await Task.Delay(500);
            }
            ShowComplete(softwareName);
        }
        catch (Exception e)
        {
            Log.Error($"{softwareName}: 安装函数异常 - {e.Message}");
            NotifyError(softwareName, e.Message);
        }
    }

    private void ShowComplete(string softwareName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            lock (_stateLock)
            {
                _downloadingNames.Remove(softwareName);
                _progress.Remove(softwareName);
            }
            Log.Info($"[下载] {softwareName}: 已安装 复位卡片");
            ApplyCardModeUi(softwareName);
            ShowToast(FAInfoBarSeverity.Success,
                AppUtils.Tr("download.install_complete"),
                AppUtils.Tr("download.install_success", ("name", softwareName)));
        });
    }

    private void NotifyError(string softwareName, string errorMsg)
    {
        Log.Warning($"{softwareName}: 任务失败通知 - {errorMsg}");
        Dispatcher.UIThread.Post(() =>
        {
            lock (_stateLock)
            {
                _downloadingNames.Remove(softwareName);
                _progress.Remove(softwareName);
            }
            ApplyCardModeUi(softwareName);
            ShowToast(FAInfoBarSeverity.Error,
                AppUtils.Tr("download.install_failed"),
                AppUtils.Tr("download.install_error", ("name", softwareName), ("error", errorMsg)));
        });
    }

    private static void OpenLink(string url)
    {
        try
        {
            if (url.Length > 0)
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[下载] 打开链接失败 {url}: {e.Message}");
        }
    }
}
