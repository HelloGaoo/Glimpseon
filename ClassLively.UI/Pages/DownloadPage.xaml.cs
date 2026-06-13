using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ClassLively_UI.Models;
using ClassLively_UI.Services;
using ClassLively_UI.Helpers;

namespace ClassLively_UI.Pages
{
    /// <summary>软件分类分组</summary>
    public class SoftwareCategoryGroup
    {
        public string CategoryName { get; set; } = "";
        public List<SoftwareItemModel> Items { get; set; } = new();
    }

    public sealed partial class DownloadPage : Page
    {
    private readonly IApiService _api;

    public ObservableCollection<SoftwareCategoryGroup> CategoryGroups { get; } = new();
    private bool _isSingleMode = true;
    private bool _selectAllState;

    public DownloadPage()
    {
        InitializeComponent();
        SingleModeRadio.IsChecked = true;
        _api = new ApiService();
        CategoryListUI.ItemsSource = CategoryGroups;
        Unloaded += OnUnloaded;
        _ = LoadSoftwareListAsync();
        _ = AppSettings.LoadAsync();
        _ = RestoreSettingsAsync();
    }

    private async Task RestoreSettingsAsync()
    {
        try
        {
            var config = await _api.GetConfigAsync();
            if (config == null || config.Count == 0) return;

            if (config.TryGetValue("download_source", out var srcObj) && srcObj != null)
            {
                var src = srcObj.ToString();
                if (!string.IsNullOrEmpty(src))
                {
                    foreach (var item in SourceCombo.Items)
                    {
                        if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == src)
                        {
                            SourceCombo.SelectedItem = cbi;
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Download] 恢复设置失败: {ex.Message}");
        }
    }

    // ── 模式切换──

    private void OnSingleModeChecked(object sender, RoutedEventArgs e)
    {
        _isSingleMode = true;
        SelectAllBtn.Visibility = Visibility.Collapsed;
        StartDownloadBtn.Visibility = Visibility.Collapsed;
        SyncAllCardVisibility();
    }

    private void OnMultiModeChecked(object sender, RoutedEventArgs e)
    {
        _isSingleMode = false;
        SelectAllBtn.Visibility = Visibility.Visible;
        StartDownloadBtn.Visibility = Visibility.Visible;
        _selectAllState = false;
        SelectAllBtn.Content = "全选";
        SyncAllCardVisibility();
    }

    private void SyncAllCardVisibility()
    {
        for (int g = 0; g < CategoryGroups.Count; g++)
        {
            var groupContainer = CategoryListUI.ContainerFromIndex(g) as FrameworkElement;
            if (groupContainer == null) continue;

            var innerItemsControl = FindChild<ItemsControl>(groupContainer);
            if (innerItemsControl == null) continue;

            for (int i = 0; i < innerItemsControl.Items.Count; i++)
            {
                var itemContainer = innerItemsControl.ContainerFromIndex(i) as FrameworkElement;
                if (itemContainer == null) continue;

                var btn = itemContainer.FindName("DownloadBtn") as Button;
                var chk = itemContainer.FindName("ItemCheck") as CheckBox;

                if (btn != null)
                    btn.Visibility = _isSingleMode ? Visibility.Visible : Visibility.Collapsed;

                if (chk != null)
                {
                    chk.Visibility = _isSingleMode ? Visibility.Collapsed : Visibility.Visible;
                    chk.IsChecked = _isSingleMode ? false : _selectAllState;
                }
            }
        }
    }

    // ── 下载源切换 ──

    private async void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceCombo.SelectedItem is not ComboBoxItem item || item.Tag == null) return;

        string source = item.Tag.ToString()!;
        AppSettings.Set("download_source", source);
        _ = AppSettings.SaveAsync();

        try { await _api.SetConfigAsync("download_source", source); }
        catch { /* 后端不可用时仅保存本地 */ }

        await LoadSoftwareListAsync();
    }

    // ── 全选 / 取消全选 ──

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        _selectAllState = !_selectAllState;
        SelectAllBtn.Content = _selectAllState ? "取消全选" : "全选";

        foreach (var group in CategoryGroups)
            foreach (var item in group.Items)
                item.IsSelected = _selectAllState;

        for (int g = 0; g < CategoryGroups.Count; g++)
        {
            var groupContainer = CategoryListUI.ContainerFromIndex(g) as FrameworkElement;
            if (groupContainer == null) continue;

            var innerItemsControl = FindChild<ItemsControl>(groupContainer);
            if (innerItemsControl == null) continue;

            for (int i = 0; i < innerItemsControl.Items.Count; i++)
            {
                var itemContainer = innerItemsControl.ContainerFromIndex(i) as FrameworkElement;
                if (itemContainer == null) continue;

                var chk = itemContainer.FindName("ItemCheck") as CheckBox;
                if (chk != null) chk.IsChecked = _selectAllState;
            }
        }
    }

    // ── 单个下载 ──

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not SoftwareItemModel item) return;

        string name = item.Name ?? "未知软件";
        var dialog = new ContentDialog
        {
            Title = "确认下载",
            Content = $"确定要下载「{name}」吗？",
            PrimaryButtonText = "下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var container = FindParentContentPresenter(btn);
        var progressRing = container?.FindName("ItemProgress") as ProgressRing;

        if (progressRing != null)
        {
            progressRing.IsActive = true;
            progressRing.Visibility = Visibility.Visible;
        }
        btn.IsEnabled = false;
        btn.Visibility = Visibility.Collapsed;

        try
        {
            bool success = await _api.StartDownloadAsync(name);
            if (success)
                ShowInfo("下载完成", $"「{name}」已开始下载，请查看下载目录。");
            else
            {
                btn.Content = "需后端";
                ShowInfo("下载失败", $"「{name}」下载请求失败。");
            }
        }
        catch
        {
            btn.Content = "需后端";
            ShowInfo("下载异常", $"无法下载「{name}」。");
        }
        finally
        {
            if (progressRing != null)
            {
                progressRing.IsActive = false;
                progressRing.Visibility = Visibility.Collapsed;
            }
            btn.IsEnabled = true;
            btn.Visibility = Visibility.Visible;
        }
    }

    // ── 批量下载 ──

    private async void OnStartBatchDownloadClick(object sender, RoutedEventArgs e)
    {
        var selected = new List<string>();
        foreach (var group in CategoryGroups)
            selected.AddRange(group.Items.Where(x => x.IsSelected).Select(x => x.Name ?? "").Where(n => !string.IsNullOrEmpty(n)));

        if (selected.Count == 0)
        {
            ShowInfo("提示", "请至少选择一个软件进行下载。");
            return;
        }

        var nameList = string.Join("\n• ", selected);
        var dialog = new ContentDialog
        {
            Title = "确认批量下载",
            Content = $"确定要下载以下 {selected.Count} 个软件吗？\n\n• {nameList}",
            PrimaryButtonText = "开始下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await ExecuteBatchDownloadAsync(selected);
    }

    private async Task ExecuteBatchDownloadAsync(List<string> names)
    {
        StartDownloadBtn.IsEnabled = false;
        BatchProgressPanel.Visibility = Visibility.Visible;
        BatchProgressRing.IsActive = true;
        int total = names.Count;
        int completed = 0;

        try
        {
            for (int i = 0; i < names.Count; i++)
            {
                var name = names[i];
                BatchStatusText.Text = $"正在下载 ({i + 1}/{total}): {name}";
                BatchProgressBar.Value = (double)i / total * 100;

                try { await _api.StartDownloadAsync(name); }
                catch (Exception ex) { Debug.WriteLine($"[Download] 批量异常: {name} - {ex.Message}"); }

                completed++;
            }

            BatchStatusText.Text = $"下载完成！共 {completed}/{total} 个";
            BatchProgressBar.Value = 100;
            ShowInfo("批量下载完成", $"已提交 {completed}/{total} 个下载请求，请查看下载目录。");

            await Task.Delay(3000);
            BatchProgressPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowInfo("批量下载异常", ex.Message);
        }
        finally
        {
            BatchProgressRing.IsActive = false;
            StartDownloadBtn.IsEnabled = true;
        }
    }

    // ── 加载软件列表）──

    private async Task LoadSoftwareListAsync()
    {
        CategoryGroups.Clear();

        bool loadedFromBackend = false;
        try
        {
            var list = await _api.ListSoftwareAsync(null);
            if (list != null && list.Count > 0)
            {
                var grouped = list.GroupBy(x => x.Category ?? "其他")
                                  .Select(g => new SoftwareCategoryGroup
                                  {
                                      CategoryName = g.Key,
                                      Items = g.ToList()
                                  });
                foreach (var g in grouped) CategoryGroups.Add(g);

                EmptyTipPanel.Visibility = Visibility.Collapsed;
                Debug.WriteLine($"[Download] 从后端加载了 {list.Count} 个软件，{CategoryGroups.Count} 个分类");
                loadedFromBackend = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Download] 后端加载失败: {ex.Message}");
        }

        if (!loadedFromBackend)
        {
            EmptyTipLabel.Text = "暂无软件列表（后端未启动）";
            EmptyTipPanel.Visibility = Visibility.Visible;
        }

        await Task.Delay(50);
        BindCardEventsAndSyncVisibility();
    }

    // ── 卡片事件绑定与初始同步──

    private void BindCardEventsAndSyncVisibility()
    {
        for (int g = 0; g < CategoryGroups.Count; g++)
        {
            var groupContainer = CategoryListUI.ContainerFromIndex(g) as FrameworkElement;
            if (groupContainer == null) continue;

            var innerItemsControl = FindChild<ItemsControl>(groupContainer);
            if (innerItemsControl == null) continue;

            for (int i = 0; i < innerItemsControl.Items.Count; i++)
            {
                var itemContainer = innerItemsControl.ContainerFromIndex(i) as FrameworkElement;
                if (itemContainer == null) continue;

                var btn = itemContainer.FindName("DownloadBtn") as Button;
                var chk = itemContainer.FindName("ItemCheck") as CheckBox;

                if (btn != null)
                {
                    btn.Click -= OnDownloadClick;
                    btn.Click += OnDownloadClick;
                    btn.Visibility = _isSingleMode ? Visibility.Visible : Visibility.Collapsed;
                }

                if (chk != null)
                {
                    chk.Visibility = _isSingleMode ? Visibility.Collapsed : Visibility.Visible;
                    chk.IsChecked = _isSingleMode ? false : _selectAllState;
                }
            }
        }
    }

    // ── 对话框辅助 ──

    private async void ShowInfo(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "确定",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    // ── 资源清理 ──

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        CategoryGroups.Clear();
        (_api as IDisposable)?.Dispose();
    }

    // ── 树辅助方法 ──

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private static ContentPresenter? FindParentContentPresenter(DependencyObject child)
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is ContentPresenter cp) return cp;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
    }
}

namespace ClassLively_UI.Models
{
    public partial class SoftwareItemModel
    {
        public Visibility HasLink => string.IsNullOrWhiteSpace(Link) ? Visibility.Collapsed : Visibility.Visible;
    }
}
