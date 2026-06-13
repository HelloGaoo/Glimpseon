using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using ClassLively_UI.Helpers;
using ClassLively_UI.Models;
using ClassLively_UI.Services;

namespace ClassLively_UI.Pages;

public sealed partial class WallpaperPage : Page
{
    private readonly IApiService _api;
    private readonly HttpClient _httpClient = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    // 定时器
    private DispatcherTimer? _autoGetTimer;
    private DispatcherTimer? _autoSyncTimer;

    // 状态
    private string? _currentPath;
    private string? _currentSource;
    private string? _lastSyncPath;
    private List<HistoryItem> _historyItems = new();
    private int _currentPage;
    private bool _isLoadingMore;
    private const int PageSize = 20;
    private const int MaxHistoryRecords = 100;
    private const string HistoryFileName = "history.json";
    private const int HistoryVersion = 1;

    // API URL 映射
    private static readonly Dictionary<string, (string Url, string Name)> ApiUrlMap = new()
    {
        ["api.ltyuanfang.cn"] = ("https://tu.ltyuanfang.cn/api/fengjing.php", "api.ltyuanfang.cn"),
        ["imlcd.cn_bg_high"]   = ("https://api.imlcd.cn/bg/high.php",     "imlcd.cn_bg_high"),
        ["imlcd.cn_bg_mc"]     = ("https://api.imlcd.cn/bg/mc.php",       "imlcd.cn_bg_mc"),
        ["imlcd.cn_bg_gq"]     = ("https://api.imlcd.cn/bg/gq.php",       "imlcd.cn_bg_gq"),
    };

    // 自动间隔 Tag → 秒数
    private static int IntervalToSeconds(string tag) => tag switch
    {
        "10m"  => 600,
        "30m"  => 1800,
        "1h"   => 3600,
        "3h"   => 10800,
        "6h"   => 21600,
        "12h"  => 43200,
        "1d"   => 86400,
        "3d"   => 259200,
        "7d"   => 604800,
        _      => 0
    };

    public WallpaperPage()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
        _api = new ApiService();
        BlurSlider.Value = 0;
        BrightnessSlider.Value = 0;
        _ = LoadInitialDataAsync();
    }

    //  生命周期

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _autoGetTimer?.Stop();
        _autoGetTimer = null;
        _autoSyncTimer?.Stop();
        _autoSyncTimer = null;
        _httpClient.Dispose();
    }

    private async Task LoadInitialDataAsync()
    {
        LoadCurrentWallpaperFromRegistry();
        try { await AppSettings.LoadAsync(); } catch { }
        try { await LoadSettingsAsync(); } catch { }
        try { await LoadHistoryAsync(); } catch { }
        try { await LoadCurrentWallpaperFromBackendAsync(); } catch { }
        ApplyBrightnessEffect((int)BrightnessSlider.Value);
    }

    //  获取壁纸

    private async void GetWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.IsEnabled = false;
            btn.Content = "获取中";
        }

        try
        {
            var source = GetSelectedApiSource();

            if (await _api.HealthCheckAsync())
            {
                var result = await _api.FetchWallpaperAsync(source);
                if (result?.Path != null && File.Exists(result.Path))
                {
                    _currentPath = result.Path;
                    _currentSource = source;
                    ApplyWallpaper(_currentPath, _currentSource);
                    AddToHistoryLocal(_currentPath, _currentSource, "");

                    if (AutoSyncToggle.IsOn)
                        SetDesktopWallpaper(_currentPath);

                    await LoadHistoryAsync();
                }
                else
                {
                    ShowInfoCardOffline();
                }
            }
            else
            {
                ShowInfoCardOffline();
            }
        }
        finally
        {
            if (sender is Button btn2)
            {
                btn2.IsEnabled = true;
                btn2.Content = "获取壁纸";
            }
        }
    }

    private (string Url, string Name) ResolveApiUrl(string source)
    {
        return ApiUrlMap.TryGetValue(source, out var pair)
            ? pair
            : ("https://wp.upx8.com/api.php?content=风景", "wp.upx8.com");
    }

    //  另存为 / 手动选择 / 设为桌面

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == null || !File.Exists(_currentPath))
        {
            ShowDialog("提示", "请先获取或选择一张壁纸");
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = $"wallpaper_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        picker.FileTypeChoices.Add("JPEG 图片", new[] { ".jpg" });
        picker.FileTypeChoices.Add("PNG 图片", new[] { ".png" });
        InitializeWithWindow.Initialize(picker, GetWindowHandle());

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            try
            {
                var src = await StorageFile.GetFileFromPathAsync(_currentPath);
                await src.CopyAndReplaceAsync(file);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Wallpaper] 另存为失败: {ex.Message}");
            }
        }
    }

    private async void SelectWallpaper_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail
        };
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".webp");
        InitializeWithWindow.Initialize(picker, GetWindowHandle());

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _currentPath = file.Path;
            _currentSource = "本地文件";
            ApplyWallpaper(_currentPath, _currentSource);
            if (_currentSource != "历史记录")
            {
                var (_, apiName) = ResolveApiUrl(GetSelectedApiSource());
                AddToHistoryLocal(_currentPath, _currentSource == "本地文件" ? apiName : _currentSource, "");
            }
        }
    }

    private void SetDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == null || !File.Exists(_currentPath))
        {
            ShowDialog("提示", "请先获取或选择一张壁纸");
            return;
        }
        SetDesktopWallpaper(_currentPath);
    }

    private async void SetDesktopWallpaper(string path)
    {
        await _api.SetDesktopWallpaperAsync(path);
    }

    //  设置项变更

    private async void SaveLimit_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!sender.IsLoaded) return;
        var value = (int)sender.Value;
        AppSettings.Set("wallpaper_save_limit", value);
        _ = AppSettings.SaveAsync();
        try { await _api.SetConfigAsync("wallpaper_save_limit", value); } catch { }
    }

    private async void ApiSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ApiSourceCombo.IsLoaded || ApiSourceCombo.SelectedItem is not ComboBoxItem item) return;
        var source = item.Content.ToString() ?? "wp.upx8.com";
        AppSettings.Set("wallpaper_api_source", source);
        _ = AppSettings.SaveAsync();
        try { await _api.SetConfigAsync("wallpaper_api_source", source); } catch { }
    }

    private async void AutoInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!AutoIntervalCombo.IsLoaded || AutoIntervalCombo.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString() ?? "never";

        AppSettings.Set("wallpaper_auto_interval", tag);
        _ = AppSettings.SaveAsync();
        try { await _api.SetConfigAsync("wallpaper_auto_interval", tag); } catch { }

        RestartAutoGetTimer(tag);
    }

    private async void AutoSync_Toggled(object sender, RoutedEventArgs e)
    {
        if (!AutoSyncToggle.IsLoaded) return;
        AppSettings.Set("wallpaper_auto_sync", AutoSyncToggle.IsOn);
        _ = AppSettings.SaveAsync();
        try { await _api.SetConfigAsync("wallpaper_auto_sync", AutoSyncToggle.IsOn); } catch { }

        RestartAutoSyncTimer(AutoSyncToggle.IsOn);
    }

    private void BlurSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var value = (int)e.NewValue;
        BlurValueLabel.Text = value.ToString();
        if (BlurSlider.IsLoaded)
        {
            AppSettings.Set("wallpaper_blur", value);
            _ = AppSettings.SaveAsync();
            _ = _api.SetConfigAsync("wallpaper_blur", value);
        }
    }

    private void BrightnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var value = (int)e.NewValue;
        BrightnessValueLabel.Text = value.ToString();
        if (BrightnessSlider.IsLoaded)
        {
            ApplyBrightnessEffect(value);
            AppSettings.Set("wallpaper_brightness", value);
            _ = AppSettings.SaveAsync();
            _ = _api.SetConfigAsync("wallpaper_brightness", value);
        }
    }


    private void ApplyBrightnessEffect(int dimValue)
    {
    }

    //  定时器管理

    private void RestartAutoGetTimer(string tag)
    {
        _autoGetTimer?.Stop();
        _autoGetTimer = null;

        if (tag != "never")
        {
            var seconds = IntervalToSeconds(tag);
            if (seconds > 0)
            {
                _autoGetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                _autoGetTimer.Tick += (_, _) => GetWallpaper_Click(GetWallpaperBtn, new RoutedEventArgs());
                _autoGetTimer.Start();
            }
        }
    }

    private void RestartAutoSyncTimer(bool enabled)
    {
        _autoSyncTimer?.Stop();
        _autoSyncTimer = null;

        if (enabled)
        {
            _autoSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _autoSyncTimer.Tick += (_, _) => CheckAutoSync();
            _autoSyncTimer.Start();
        }
    }

    private void CheckAutoSync()
    {
        if (_currentPath == null || !File.Exists(_currentPath)) return;
        if (_lastSyncPath != _currentPath)
        {
            SetDesktopWallpaper(_currentPath);
            _lastSyncPath = _currentPath;
        }
    }

    //  壁纸应用与预览更新

    private void ApplyWallpaper(string path, string source)
    {
        _currentPath = path;
        _currentSource = source;
        UpdatePreviewImage(path);
        UpdateInfoCard(path, source);
    }

    private void UpdatePreviewImage(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var bmp = new BitmapImage(new Uri($"file:///{path.Replace('\\', '/')}"));
            PreviewImage.Source = bmp;
        }
        else
        {
            PreviewImage.Source = null;
        }
    }

    private void UpdateInfoCard(string path, string source)
    {
        long fileSize = 0;
        try { if (path != null) fileSize = new FileInfo(path).Length; } catch { }

        // 分辨率
        string resolution = "--";
        try
        {
            if (File.Exists(path))
            {
                var bmp = new BitmapImage(new Uri(path));
                if (bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
                    resolution = $"{bmp.PixelWidth}×{bmp.PixelHeight}";
            }
        }
        catch { }

        ResolutionLabel.Text = resolution;
        FileSizeLabel.Text = FormatFileSize(fileSize);
        SourceLabel.Text = source;
        PathLabel.Text = path ?? "--";
    }

    private void ShowInfoCardOffline()
    {
        ResolutionLabel.Text = "--";
        FileSizeLabel.Text = "--";
        PathLabel.Text = "后端未连接";
        SourceLabel.Text = "提示";
        PreviewImage.Source = null;
    }

    //  历史记录

    private string HistoryFilePath => Path.Combine(
        Path.Combine(AppContext.BaseDirectory, "wallpaper"), HistoryFileName);

    private string WallpaperDir => Path.Combine(AppContext.BaseDirectory, "wallpaper");

    private async Task LoadHistoryAsync(bool append = false)
    {
        if (!append)
        {
            _currentPage = 0;
            _historyItems.Clear();
        }

        try
        {
            // 优先从后端加载
            List<WallpaperInfoModel>? backendItems = null;
            try
            {
                if (await _api.HealthCheckAsync())
                {
                    _currentPage++;
                    backendItems = await _api.GetHistoryAsync(_currentPage, PageSize);
                }
            }
            catch { }

            // 后端无数据则从本地 JSON 加载
            if ((backendItems == null || backendItems.Count == 0) && !append)
            {
                var localRecords = LoadHistoryFromFile();
                foreach (var r in localRecords)
                {
                    _historyItems.Add(new HistoryItem
                    {
                        Path = r.Path,
                        Width = 0,
                        Height = 0,
                        ThumbnailUri = File.Exists(r.Path) ? new Uri($"file:///{r.Path.Replace('\\', '/')}") : null,
                        ResolutionText = r.Resolution
                    });
                }
            }
            else if (backendItems != null)
            {
                foreach (var wp in backendItems)
                {
                    _historyItems.Add(new HistoryItem
                    {
                        Path = wp.Path ?? "",
                        Width = wp.Width,
                        Height = wp.Height,
                        ThumbnailUri = wp.Path != null ? new Uri($"file:///{wp.Path.Replace('\\', '/')}") : null,
                        ResolutionText = wp.Width > 0 && wp.Height > 0 ? $"{wp.Width}×{wp.Height}" : "--"
                    });
                }
            }

            RefreshHistoryUI(backendItems != null && backendItems.Count < PageSize);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Wallpaper] 加载历史记录失败: {ex.Message}");
            if (!append) RefreshHistoryUI(true);
        }
    }

    private List<HistoryRecord> LoadHistoryFromFile()
    {
        var file = HistoryFilePath;
        if (!File.Exists(file)) return new();

        try
        {
            var json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("version").GetInt32() != HistoryVersion)
                return new();

            var result = new List<HistoryRecord>();
            foreach (var item in doc.RootElement.GetProperty("history").EnumerateArray())
            {
                result.Add(new HistoryRecord
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
                    Source = item.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "",
                    ApiUrl = item.TryGetProperty("api_url", out var u) ? u.GetString() ?? "" : "",
                    AddedTime = item.TryGetProperty("added_time", out var t) ? t.GetString() ?? "" : "",
                    FileSize = item.TryGetProperty("file_size", out var fs) ? fs.GetInt64() : 0,
                    Resolution = item.TryGetProperty("resolution", out var r) ? r.GetString() ?? "--" : "--"
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Wallpaper] 读取历史JSON失败: {ex.Message}");
            return new();
        }
    }

    private void SaveHistoryToFile(List<HistoryRecord> records)
    {
        try
        {
            Directory.CreateDirectory(WallpaperDir);
            var data = new Dictionary<string, object>
            {
                ["version"] = HistoryVersion,
                ["history"] = records.Select(r => new Dictionary<string, object>
                {
                    ["id"] = r.Id,
                    ["path"] = r.Path,
                    ["source"] = r.Source,
                    ["api_url"] = r.ApiUrl,
                    ["added_time"] = r.AddedTime,
                    ["file_size"] = r.FileSize,
                    ["resolution"] = r.Resolution
                }).ToList()
            };
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            File.WriteAllText(HistoryFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Wallpaper] 保存历史JSON失败: {ex.Message}");
        }
    }

    private void AddToHistoryLocal(string path, string source, string apiUrl)
    {
        if (!File.Exists(path)) return;

        var recordId = Path.GetFileNameWithoutExtension(path);
        var records = LoadHistoryFromFile();

        // 去重：已存在则移到最前
        var existing = records.FirstOrDefault(r => r.Id == recordId);
        if (existing != null)
        {
            records.Remove(existing);
            records.Insert(0, existing);
            SaveHistoryToFile(records);
            return;
        }

        long fileSize = 0;
        try { fileSize = new FileInfo(path).Length; } catch { }

        string resolution = "--";
        try
        {
            var bmp = new BitmapImage(new Uri(path));
            if (bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
                resolution = $"{bmp.PixelWidth}×{bmp.PixelHeight}";
        }
        catch { }

        records.Insert(0, new HistoryRecord
        {
            Id = recordId,
            Path = path,
            Source = source,
            ApiUrl = apiUrl,
            AddedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            FileSize = fileSize,
            Resolution = resolution
        });

        while (records.Count > MaxHistoryRecords)
            records.RemoveAt(records.Count - 1);

        SaveHistoryToFile(records);
    }

    private void EnforceSaveLimit(string wallpaperDir)
    {
        if (!Directory.Exists(wallpaperDir)) return;
        var limit = (int)SaveLimitBox.Value;

        var files = Directory.GetFiles(wallpaperDir, "wallpaper_*.jpg")
            .Select(f => new { Path = f, Time = File.GetLastWriteTimeUtc(f) })
            .OrderBy(x => x.Time)
            .ToList();

        while (files.Count > limit)
        {
            var oldest = files[0];
            try
            {
                File.Delete(oldest.Path);
                var recordId = Path.GetFileNameWithoutExtension(oldest.Path);
                RemoveHistoryRecord(recordId);
            }
            catch { }
            files.RemoveAt(0);
        }
    }

    private bool RemoveHistoryRecord(string recordId)
    {
        var records = LoadHistoryFromFile();
        var removed = records.RemoveAll(r => r.Id == recordId) > 0;
        if (removed) SaveHistoryToFile(records);
        return removed;
    }

    private void RefreshHistoryUI(bool allLoaded)
    {
        HistoryCountLabel.Text = $"({_historyItems.Count})";

        if (_historyItems.Count == 0)
        {
            EmptyHistoryPanel.Visibility = Visibility.Visible;
            HistoryGridView.Visibility = Visibility.Collapsed;
            LoadMoreBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyHistoryPanel.Visibility = Visibility.Collapsed;
            HistoryGridView.Visibility = Visibility.Visible;
            HistoryGridView.ItemsSource = _historyItems;
            LoadMoreBtn.Visibility = allLoaded ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoadingMore) return;
        _isLoadingMore = true;
        LoadMoreBtn.IsEnabled = false;
        LoadMoreBtn.Content = "加载中...";

        try { await LoadHistoryAsync(append: true); }
        finally
        {
            _isLoadingMore = false;
            LoadMoreBtn.IsEnabled = true;
            LoadMoreBtn.Content = "加载更多";
        }
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "确认清空",
            Content = "确定要清空所有历史记录吗？此操作不可撤销。",
            PrimaryButtonText = "清空全部",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                SaveHistoryToFile(new List<HistoryRecord>());
                _historyItems.Clear();
                RefreshHistoryUI(true);
                try { await _api.SetConfigAsync("wallpaper_clear_history", true); } catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Wallpaper] 清空历史失败: {ex.Message}");
            }
        }
    }

    //  历史记录项点击 → 预览弹窗
    private async void HistoryItem_Click(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is HistoryItem item)
        {
            if (string.IsNullOrEmpty(item.Path) || !File.Exists(item.Path))
            {
                ShowDialog("提示", "该壁纸文件不存在，可能已被删除");
                return;
            }

            try
            {
                var info = new FileInfo(item.Path);
                var fileSize = FormatFileSize(info.Length);
                var resolution = item.Width > 0 && item.Height > 0 ? $"{item.Width}×{item.Height}" : "--";
                var fileTime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");

                // 预览弹窗内容640×400 
                const int maxW = 640, maxH = 400;
                var rootPanel = new StackPanel { Spacing = 12 };

                var previewBorder = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxWidth = maxW,
                    MaxHeight = maxH
                };

                var previewImg = new Image
                {
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                    MaxHeight = maxH,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                try { previewImg.Source = new BitmapImage(new Uri($"file:///{item.Path.Replace('\\', '/')}")); }
                catch { /* 图片加载失败 */ }

                previewBorder.Child = previewImg;
                rootPanel.Children.Add(previewBorder);

                var infoText = $"分辨率: {resolution}  |  大小: {fileSize}  |  来源: 历史记录  |  时间: {fileTime}";
                rootPanel.Children.Add(new TextBlock
                {
                    Text = infoText,
                    FontSize = 13,
                    Opacity = 0.75,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                var btnPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var useBtn = new Button
                {
                    Content = "使用此壁纸",
                    Style = Application.Current.Resources["AccentButtonStyle"] as Style
                };
                var deleteBtn = new Button { Content = "删除" };

                btnPanel.Children.Add(useBtn);
                btnPanel.Children.Add(deleteBtn);
                rootPanel.Children.Add(btnPanel);

                var dialog = new ContentDialog
                {
                    Title = Path.GetFileNameWithoutExtension(item.Path),
                    Content = rootPanel,
                    PrimaryButtonText = "",
                    CloseButtonText = "关闭",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot
                };

                useBtn.Click += (_, _) =>
                {
                    _currentPath = item.Path;
                    _currentSource = "历史记录";
                    ApplyWallpaper(_currentPath, _currentSource);
                    if (AutoSyncToggle.IsOn)
                        SetDesktopWallpaper(_currentPath);
                    dialog.Hide();
                };

                deleteBtn.Click += (_, _) =>
                {
                    try
                    {
                        if (File.Exists(item.Path)) File.Delete(item.Path);
                        RemoveHistoryRecord(Path.GetFileNameWithoutExtension(item.Path));
                        _historyItems.Remove(item);
                        RefreshHistoryUI(true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Wallpaper] 删除壁纸失败: {ex.Message}");
                    }
                    dialog.Hide();
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Wallpaper] 预览弹窗异常: {ex}");
            }
        }
    }

    //  设置加载

    private async Task LoadSettingsAsync()
    {
        Dictionary<string, object>? config = null;

        try
        {
            config = await _api.GetConfigAsync();
        }
        catch { }

        if (config == null || config.Count == 0)
        {
            config = AppSettings.GetAll();
        }

        ApplySettings(config);
    }

    private void ApplySettings(Dictionary<string, object> config)
    {
        if (config.TryGetValue("wallpaper_save_limit", out var limitObj) &&
            int.TryParse(limitObj?.ToString(), out var limit))
            SaveLimitBox.Value = Math.Clamp(limit, 10, 100);

        if (config.TryGetValue("wallpaper_auto_interval", out var intervalObj))
            SelectComboBoxByTag(AutoIntervalCombo, intervalObj?.ToString() ?? "never");

        if (config.TryGetValue("wallpaper_api_source", out var apiObj))
            SelectComboBoxByContent(ApiSourceCombo, apiObj?.ToString() ?? "wp.upx8.com");

        if (config.TryGetValue("wallpaper_auto_sync", out var syncObj) &&
            bool.TryParse(syncObj?.ToString(), out var syncOn))
            AutoSyncToggle.IsOn = syncOn;

        if (config.TryGetValue("wallpaper_blur", out var blurObj) &&
            int.TryParse(blurObj?.ToString(), out var blur))
        {
            BlurSlider.Value = Math.Clamp(blur, 0, 30);
            BlurValueLabel.Text = blur.ToString();
        }

        if (config.TryGetValue("wallpaper_brightness", out var brightObj) &&
            int.TryParse(brightObj?.ToString(), out var bright))
        {
            BrightnessSlider.Value = Math.Clamp(bright, -100, 0);
            BrightnessValueLabel.Text = bright.ToString();
        }
    }

    //  初始加载当前壁纸

    private void LoadCurrentWallpaperFromRegistry()
    {
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var path = key?.GetValue("WallPaper")?.ToString();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _currentPath = path;
                _currentSource = "当前桌面";
                ApplyWallpaper(_currentPath, _currentSource);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Wallpaper] 从注册表读取壁纸失败: {ex.Message}");
        }
    }

    private async Task LoadCurrentWallpaperFromBackendAsync()
    {
        try
        {
            var info = await _api.GetCurrentWallpaperAsync();
            if (info?.Path != null && File.Exists(info.Path))
            {
                _currentPath = info.Path;
                _currentSource = "当前桌面";
                ApplyWallpaper(_currentPath, _currentSource);
            }
        }
        catch { }
    }

    //  辅助方法

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "--";
        string[] units = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < units.Length - 1) { order++; size /= 1024.0; }
        return $"{size:0.#} {units[order]}";
    }

    private string GetSelectedApiSource()
    {
        return ApiSourceCombo.SelectedItem is ComboBoxItem item
            ? item.Content.ToString() ?? "wp.upx8.com"
            : "wp.upx8.com";
    }

    private static void SelectComboBoxByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == tag)
            { combo.SelectedItem = cbi; return; }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static void SelectComboBoxByContent(ComboBox combo, string content)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Content?.ToString() == content)
            { combo.SelectedItem = cbi; return; }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private nint GetWindowHandle() => WindowNative.GetWindowHandle(this);

    private async void ShowDialog(string title, string content)
    {
        try
        {
            await new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "确定",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            }.ShowAsync();
        }
        catch { }
    }
}

//  数据模型

/// <summary>历史记录数据项</summary>
public class HistoryItem
{
    public string Path { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public Uri? ThumbnailUri { get; set; }
    public string ResolutionText { get; set; } = "--";
}

/// <summary>历史记录json模型</summary>
public class HistoryRecord
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Source { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public string AddedTime { get; set; } = "";
    public long FileSize { get; set; }
    public string Resolution { get; set; } = "--";
}

//  Win32 设壁纸

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    internal static extern int SystemParametersInfo(int uiAction, int uiParam, string pvParam, int fWinIni);
}
