using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using ClassLively_UI.Models;
using ClassLively_UI.Services;

namespace ClassLively_UI.Pages;

public sealed partial class DownloadPage : Page
{
    private readonly IApiService _api;

    public ObservableCollection<SoftwareItemModel> SoftwareList { get; } = new();
    private bool _isSingleMode = true;
    private bool _selectAllState;

    public DownloadPage()
    {
        InitializeComponent();
        _api = new ApiService();
        SoftwareListUI.ItemsSource = SoftwareList;
        _ = LoadSoftwareListAsync();
    }

    // ── 模式切换 ──

    private void SingleModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        SetSingleMode();
    }

    private void MultiModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        SetMultiMode();
    }

    private void SetSingleMode()
    {
        _isSingleMode = true;
        SelectAllBtn.Visibility = Visibility.Collapsed;
        UpdateItemVisibility();
    }

    private void SetMultiMode()
    {
        _isSingleMode = false;
        SelectAllBtn.Visibility = Visibility.Visible;
        _selectAllState = false;
        SelectAllBtn.Content = "全选";
        UpdateItemVisibility();
    }

    /// <summary>
    /// 遍历 ItemsControl 切换可见性
    /// </summary>
    private void UpdateItemVisibility()
    {
        for (int i = 0; i < SoftwareListUI.Items.Count; i++)
        {
            var container = SoftwareListUI.ContainerFromIndex(i) as FrameworkElement;
            if (container == null) continue;

            var downloadBtn = container.FindName("DownloadBtn") as Button;
            var itemCheck = container.FindName("ItemCheck") as CheckBox;

            if (downloadBtn != null)
                downloadBtn.Visibility = _isSingleMode ? Visibility.Visible : Visibility.Collapsed;

            if (itemCheck != null)
            {
                itemCheck.Visibility = _isSingleMode ? Visibility.Collapsed : Visibility.Visible;
                if (!_isSingleMode)
                    itemCheck.IsChecked = _selectAllState;
            }
        }
    }

    // ── 下载源切换 ──

    private async void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceCombo.SelectedItem is not ComboBoxItem item || item.Tag == null)
            return;

        string source = item.Tag.ToString()!;
        bool ok = await _api.SetConfigAsync("download_source", source);
        if (!ok)
        {
            ShowError("提示", "保存下载源配置失败");
        }
    }

    // ── 全选 ──

    private void SelectAllBtn_Click(object sender, RoutedEventArgs e)
    {
        _selectAllState = !_selectAllState;
        SelectAllBtn.Content = _selectAllState ? "取消全选" : "全选";

        for (int i = 0; i < SoftwareListUI.Items.Count; i++)
        {
            var container = SoftwareListUI.ContainerFromIndex(i) as FrameworkElement;
            if (container == null) continue;

            var checkBox = container.FindName("ItemCheck") as CheckBox;
            if (checkBox != null)
                checkBox.IsChecked = _selectAllState;
        }
    }

    // ── 单选点击──

    private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not SoftwareItemModel item)
            return;

        string name = item.Name ?? "未知软件";
        var confirmDialog = new ContentDialog
        {
            Title = "确认下载",
            Content = $"确定要下载「{name}」吗？",
            PrimaryButtonText = "下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await confirmDialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        await ExecuteDownloadAsync(btn, name);
    }

    // ── 执行单个下载 ──

    private async Task ExecuteDownloadAsync(Button btn, string name)
    {
        var container = FindParentContainer(btn);
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
                ShowSuccess("下载完成", $"「{name}」已开始下载，请查看下载目录。");
            else
                ShowError("下载失败", $"「{name}」下载请求失败，请检查网络或重试。");
        }
        catch (Exception ex)
        {
            ShowError("下载异常", ex.Message);
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

    // ── 链接图标点击 ──

    private async void LinkIcon_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FontIcon icon || icon.DataContext is not SoftwareItemModel item)
            return;

        string? url = item.Link;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch
        {
            ShowError("打开链接失败", "无法打开该链接，地址可能无效。");
        }
    }

    // ── 加载软件列表 ──

    private async Task LoadSoftwareListAsync()
    {
        try
        {
            List<SoftwareItemModel> items = await _api.ListSoftwareAsync(null);
            if (items != null && items.Count > 0)
            {
                foreach (var item in items)
                    SoftwareList.Add(item);

                EmptyTipText.Visibility = Visibility.Collapsed;
                SoftwareListUI.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyTipText.Visibility = Visibility.Visible;
                SoftwareListUI.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            ShowError("加载失败", $"获取软件列表时出错：{ex.Message}");
            EmptyTipText.Visibility = Visibility.Visible;
            SoftwareListUI.Visibility = Visibility.Collapsed;
        }
    }

    // ── 对话框辅助 ──

    private async void ShowSuccess(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "确定",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void ShowError(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "确定",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    // ── 辅助：从子元素向上查找 DataTemplate 容器 ──

    private FrameworkElement? FindParentContainer(FrameworkElement child)
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is ContentPresenter cp && cp.DataContext is SoftwareItemModel)
                return cp;
            if (parent is FrameworkElement fe && fe.DataContext is SoftwareItemModel)
                return fe;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
