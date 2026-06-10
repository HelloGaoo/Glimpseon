using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;
using ClassLively_UI.Services;
using ClassLively_UI.Models;

namespace ClassLively_UI.Pages;

public sealed partial class WallpaperPage : Page
{
    private readonly IApiService _api;

    // 状态
    private string? _currentPath;
    private string? _currentSource;
    private List<HistoryItem> _historyItems = new();
    private int _currentPage = 0;
    private int _totalHistoryCount = 0;
    private const int PageSize = 20;
    private bool _isLoadingMore = false;

    public WallpaperPage()
    {
        InitializeComponent();
        _api = new ApiService();
        _ = LoadInitialData();
    }

    // ── 初始化加载 ──
    private async Task LoadInitialData()
    {
        try { await LoadCurrentWallpaper(); } catch { }
        try { await LoadSettings(); } catch { }
        try { await LoadHistory(); } catch { }
    }

    // ── 获取壁纸 ──
    private async void GetWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn) btn.IsEnabled = false;

        try
        {
            var source = GetSelectedApi();
            var result = await _api.FetchWallpaperAsync(source);
            if (result?.Path != null)
            {
                _currentPath = result.Path;
                _currentSource = source;
                UpdatePreview(_currentPath);
                UpdateInfoCard(result);

                if (AutoSyncToggle.IsOn)
                    await _api.SetDesktopWallpaperAsync(_currentPath);

                await LoadHistory();
            }
            else
            {
                ShowWarning("提示", "未获取到壁纸，请检查 API 连接");
            }
        }
        catch (Exception ex)
        {
            ShowError("获取失败", ex.Message);
        }
        finally
        {
            if (sender is Button btn2) btn2.IsEnabled = true;
        }
    }

    // ── 另存为 ──
    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == null || !File.Exists(_currentPath))
        {
            ShowWarning("提示", "请先获取或选择一张壁纸");
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = $"wallpaper_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        picker.FileTypeChoices.Add("JPEG 图片", new[] { ".jpg" });
        picker.FileTypeChoices.Add("PNG 图片", new[] { ".png" });

        var hwnd = GetWindowHandle();
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            try
            {
                var sourceFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(_currentPath);
                await sourceFile.CopyAndReplaceAsync(file);
                ShowSuccess("成功", $"壁纸已保存至：{file.Path}");
            }
            catch (Exception ex)
            {
                ShowError("保存失败", ex.Message);
            }
        }
    }

    // ── 手动选择 ──
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

        var hwnd = GetWindowHandle();
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _currentPath = file.Path;
            _currentSource = "本地文件";
            UpdatePreview(_currentPath);

            // 获取图片尺寸
            try
            {
                var info = new FileInfo(_currentPath);
                UpdateInfoCard(new WallpaperInfoModel
                {
                    Path = _currentPath,
                    Width = 0,
                    Height = 0
                }, _currentSource, info.Length);
            }
            catch
            {
                UpdateInfoCard(new WallpaperInfoModel { Path = _currentPath }, _currentSource, 0);
            }
        }
    }

    // ── 设为桌面 ──
    private async void SetDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == null || !File.Exists(_currentPath))
        {
            ShowWarning("提示", "请先获取或选择一张壁纸");
            return;
        }

        try
        {
            var success = await _api.SetDesktopWallpaperAsync(_currentPath);
            if (success)
                ShowSuccess("成功", "已设为桌面壁纸");
            else
                ShowError("失败", "设置壁纸失败，请重试");
        }
        catch (Exception ex)
        {
            ShowError("设置失败", ex.Message);
        }
    }

    // ── 设置项变更：保存数量上限 ──
    private async void SaveLimit_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!sender.IsLoaded) return;
        try { await _api.SetConfigAsync("wallpaper_save_limit", (int)sender.Value); } catch { }
    }

    // ── 设置项变更：API 来源 ──
    private async void ApiSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ApiSourceCombo.IsLoaded || ApiSourceCombo.SelectedItem == null) return;
        var item = ApiSourceCombo.SelectedItem as ComboBoxItem;
        if (item != null)
        {
            try { await _api.SetConfigAsync("wallpaper_api_source", item.Content.ToString()); } catch { }
        }
    }

    // ── 设置项变更：自动获取间隔 ──
    private async void AutoInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!AutoIntervalCombo.IsLoaded || AutoIntervalCombo.SelectedItem == null) return;
        var item = AutoIntervalCombo.SelectedItem as ComboBoxItem;
        if (item != null)
        {
            try { await _api.SetConfigAsync("wallpaper_auto_interval", item.Tag.ToString()); } catch { }
        }
    }

    // ── 设置项变更：自动同步桌面 ──
    private async void AutoSync_Toggled(object sender, RoutedEventArgs e)
    {
        if (!AutoSyncToggle.IsLoaded) return;
        try { await _api.SetConfigAsync("wallpaper_auto_sync", AutoSyncToggle.IsOn); } catch { }
    }

    // ── 模糊程度滑块 ──
    private void BlurSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        BlurValueLabel.Text = ((int)e.NewValue).ToString();
        if (BlurSlider.IsLoaded)
        {
            _ = _api.SetConfigAsync("wallpaper_blur", (int)e.NewValue);
        }
    }

    // ── 亮度调节滑块 ──
    private void BrightnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        BrightnessValueLabel.Text = ((int)e.NewValue).ToString();
        if (BrightnessSlider.IsLoaded)
        {
            _ = _api.SetConfigAsync("wallpaper_brightness", (int)e.NewValue);
        }
    }

    // ── 历史记录加载 ──
    private async Task LoadHistory(bool append = false)
    {
        if (!append)
        {
            _currentPage = 0;
            _historyItems.Clear();
        }

        _currentPage++;
        var items = await _api.GetHistoryAsync(_currentPage, PageSize);

        foreach (var wp in items)
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

        RefreshHistoryUI(items.Count < PageSize);
    }

    // ── 刷新历史记录 ui ──
    private void RefreshHistoryUI(bool allLoaded)
    {
        _totalHistoryCount = _historyItems.Count;
        HistoryCountLabel.Text = $"({_totalHistoryCount})";

        if (_historyItems.Count == 0)
        {
            EmptyHistoryText.Visibility = Visibility.Visible;
            HistoryGridView.Visibility = Visibility.Collapsed;
            LoadMoreBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyHistoryText.Visibility = Visibility.Collapsed;
            HistoryGridView.Visibility = Visibility.Visible;
            HistoryGridView.ItemsSource = _historyItems;
            LoadMoreBtn.Visibility = allLoaded ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // ── 加载更多 ──
    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoadingMore) return;
        _isLoadingMore = true;
        LoadMoreBtn.IsEnabled = false;
        LoadMoreBtn.Content = "加载中...";

        try
        {
            await LoadHistory(append: true);
        }
        finally
        {
            _isLoadingMore = false;
            LoadMoreBtn.IsEnabled = true;
            LoadMoreBtn.Content = "加载更多";
        }
    }

    // ── 清空历史记录 ──
    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "确认清空",
            Content = "确定要清空所有历史记录吗？此操作不可撤销。",
            PrimaryButtonText = "清空全部",
            CloseButtonText="取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                await _api.SetConfigAsync("wallpaper_clear_history", true);
                _historyItems.Clear();
                RefreshHistoryUI(true);
                ShowSuccess("成功", "历史记录已清空");
            }
            catch (Exception ex)
            {
                ShowError("清空失败", ex.Message);
            }
        }
    }

    // ── 历史记录项点击 ──
    private void HistoryItem_Click(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is HistoryItem item)
        {
            if (!string.IsNullOrEmpty(item.Path) && File.Exists(item.Path))
            {
                _currentPath = item.Path;
                _currentSource = "历史记录";
                UpdatePreview(_currentPath);

                var info = new FileInfo(item.Path);
                UpdateInfoCard(new WallpaperInfoModel
                {
                    Path = item.Path,
                    Width = item.Width,
                    Height = item.Height
                }, _currentSource, info.Length);
            }
            else
            {
                ShowWarning("提示", "该壁纸文件不存在，可能已被删除");
            }
        }
    }

    // ── 加载当前壁纸 ──
    private async Task LoadCurrentWallpaper()
    {
        var info = await _api.GetCurrentWallpaperAsync();
        if (info?.Path != null && File.Exists(info.Path))
        {
            _currentPath = info.Path;
            _currentSource = "当前桌面";
            UpdatePreview(_currentPath);
            UpdateInfoCard(info);
        }
    }

    // ── 加载设置 ──
    private async Task LoadSettings()
    {
        try
        {
            var config = await _api.GetConfigAsync();
            if (config == null) return;

            // 保存数量上限
            if (config.TryGetValue("wallpaper_save_limit", out var limitObj) && limitObj != null)
            {
                if (int.TryParse(limitObj.ToString(), out var limit))
                    SaveLimitBox.Value = Math.Clamp(limit, 10, 500);
            }

            // 自动获取间隔
            if (config.TryGetValue("wallpaper_auto_interval", out var intervalObj) && intervalObj != null)
            {
                var intervalStr = intervalObj.ToString();
                SelectComboBoxByTag(AutoIntervalCombo, intervalStr ?? "off");
            }

            // API 来源
            if (config.TryGetValue("wallpaper_api_source", out var apiSrcObj) && apiSrcObj != null)
            {
                var apiStr = apiSrcObj.ToString();
                SelectComboBoxByContent(ApiSourceCombo, apiStr ?? "wp.upx8.com");
            }

            // 自动同步桌面
            if (config.TryGetValue("wallpaper_auto_sync", out var syncObj) && syncObj != null)
            {
                if (bool.TryParse(syncObj.ToString(), out var syncOn))
                    AutoSyncToggle.IsOn = syncOn;
            }

            // 模糊程度
            if (config.TryGetValue("wallpaper_blur", out var blurObj) && blurObj != null)
            {
                if (int.TryParse(blurObj.ToString(), out var blur))
                {
                    BlurSlider.Value = Math.Clamp(blur, 0, 50);
                    BlurValueLabel.Text = blur.ToString();
                }
            }

            // 亮度
            if (config.TryGetValue("wallpaper_brightness", out var brightObj) && brightObj != null)
            {
                if (int.TryParse(brightObj.ToString(), out var bright))
                {
                    BrightnessSlider.Value = Math.Clamp(bright, -100, 0);
                    BrightnessValueLabel.Text = bright.ToString();
                }
            }
        }
        catch { }
    }

    // ── 辅助：更新预览图 ──
    private void UpdatePreview(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var bitmap = new BitmapImage();
            bitmap.UriSource = new Uri($"file:///{path.Replace('\\', '/')}");
            PreviewImage.Source = bitmap;
        }
        else
        {
            PreviewImage.Source = null;
        }
    }

    // ── 辅助：更新信息卡片 ──
    private void UpdateInfoCard(WallpaperInfoModel info)
    {
        long fileSize = 0;
        try { if (info.Path != null) fileSize = new FileInfo(info.Path).Length; } catch { }

        UpdateInfoCard(info, _currentSource ?? "--", fileSize);
    }

    // ── 辅助：更新信息卡片 ──
    private void UpdateInfoCard(WallpaperInfoModel info, string source, long fileSizeBytes)
    {
        // 分辨率
        if (info.Width > 0 && info.Height > 0)
            ResolutionLabel.Text = $"{info.Width} × {info.Height}";
        else
            ResolutionLabel.Text = "--";

        // 大小
        FileSizeLabel.Text = FormatFileSize(fileSizeBytes);

        // 来源
        SourceLabel.Text = source;

        // 路径
        PathLabel.Text = info.Path ?? "--";
    }

    // ── 辅助：格式化文件大小 ──
    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "--";
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024.0;
        }
        return $"{size:0.#} {sizes[order]}";
    }

    // ── 辅助：获取选中的 API 来源 ──
    private string GetSelectedApi()
    {
        if (ApiSourceCombo.SelectedItem is ComboBoxItem item)
            return item.Content.ToString() ?? "wp.upx8.com";
        return "wp.upx8.com";
    }

    // ── 辅助：按 Tag 选择 ComboBox 项 ──
    private static void SelectComboBoxByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == tag)
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    // ── 辅助：按 Content 选择 ComboBox 项 ──
    private static void SelectComboBoxByContent(ComboBox combo, string content)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Content?.ToString() == content)
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    // ── 辅助：获取窗口句柄 ──
    private nint GetWindowHandle() => WindowNative.GetWindowHandle(this);

    // ── 对话框：成功 ──
    private async void ShowSuccess(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "确定",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    // ── 对话框：错误 ──
    private async void ShowError(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "确定",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    // ── 对话框：警告 ──
    private async void ShowWarning(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "确定",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}

// ── 历史记录数据项 ──
public class HistoryItem
{
    public string Path { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public Uri? ThumbnailUri { get; set; }
    public string ResolutionText { get; set; } = "--";
}
