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

// 主界面
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using Glimpseon.UI.Views;

namespace Glimpseon.UI.Views;

public partial class HomeView : UserControl
{
    private static DateTime _lastPageOperation = DateTime.MinValue;

    public static void TouchPageOperation() => _lastPageOperation = DateTime.Now;

    public static bool WasPageOperationRecent(int milliseconds) =>
        (DateTime.Now - _lastPageOperation).TotalMilliseconds < milliseconds;

    public PageManager PageManager { get; }
    public ComponentRegistry ComponentRegistry { get; } = new();
    public ComponentManager ComponentManager { get; }

    private readonly GridSettings _gridSettings;
    private GridMetrics? _gridMetrics;
    private readonly Dictionary<int, Panel> _pageWidgets = new();
    private Canvas _pagesStack = new();
    private int _currentPageIndex;
    private bool _editModeActive;
    private string? _selectedPlacementId;
    private bool _swipeDragging;
    private bool _swipeMoved;
    private double _swipeStartX;
    private double _swipeStartY;
    private double _swipeLastDx;
    private Bitmap? _originalBitmap;
    private string? _draggingDefinitionId;

    private readonly GridOverlay _gridOverlay;

    public MainWindow MainWindow { get; }

    public HomeView(MainWindow mainWindow)
    {
        MainWindow = mainWindow;
        InitializeComponent();

        PageManager = new PageManager(Paths.DataConfig);
        ComponentRegistry.RegisterBatch(BuiltinComponentDefinitions.All);
        ComponentManager = new ComponentManager(this);

        var insetPercent = Config.GridInsetPercent.Value;
        _gridSettings = new GridSettings
        {
            ShortSideCells = Config.GridShortSideCells.Value,
            GapRatio = 0.12,
            InsetPercent = insetPercent,
        };

        _gridOverlay = new GridOverlay { IsVisible = false };
        InitBackground();
        InitPages();
        InitBottomBar();
        ComponentManager.LoadComponents();
        ApplyPageVisibility();

        Config.BackgroundBlurRadius.ValueChanged += _ => ApplyBackgroundEffects();
        Config.WallpaperBrightness.ValueChanged += _ => ApplyBackgroundEffects();
        Config.GridShortSideCells.ValueChanged += _ => UpdateGridMetrics();
        Config.GridInsetPercent.ValueChanged += _ => UpdateGridMetrics();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        AddHandler(PointerPressedEvent, OnRootPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnRootPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnRootPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnRootWheel, RoutingStrategies.Tunnel);

        Log.Info(AppUtils.Tr("home.init_complete"));
    }

    // 背景层

    private void InitBackground()
    {
        Log.Debug("[HOME] 背景层就绪");
    }

    public void SetWallpaper(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }
            _originalBitmap?.Dispose();
            _originalBitmap = new Bitmap(path);
            ApplyBackgroundEffects();
            Log.Info($"[HOME] 壁纸已应用: {path}");
        }
        catch (Exception e)
        {
            Log.Error($"[HOME] 壁纸应用失败: {e.Message}");
        }
    }

    private void ApplyBackgroundEffects()
    {
        BackgroundImage.Source = _originalBitmap;
        var blur = Config.BackgroundBlurRadius.Value;
        BackgroundImage.Effect = blur > 0 ? new BlurEffect { Radius = blur } : null;
        DimOverlay.Opacity = Math.Clamp(-Config.WallpaperBrightness.Value / 100.0, 0, 1);
    }

    // 页面

    private void InitPages()
    {
        _pagesStack = new Canvas { ClipToBounds = true };
        PagesContainer.Children.Add(_pagesStack);

        for (var i = 0; i < PageManager.Count; i++)
        {
            _pageWidgets[i] = CreatePageWidget(i);
        }
        _currentPageIndex = PageManager.GetCurrentPage();
        if (_currentPageIndex < 0 || _currentPageIndex >= _pageWidgets.Count)
        {
            _currentPageIndex = 0;
        }
        LayoutPages();
        Log.Debug($"[HOME] 页面就绪 {Count}页 当前={_currentPageIndex}");
    }

    private int Count => PageManager.Count;

    private Panel CreatePageWidget(int pageIndex)
    {
        var meta = PageManager.GetPage(pageIndex);
        if (meta is { Type: "nav" })
        {
            return new NavigationPage(pageIndex, PageManager);
        }
        var canvas = new Canvas { ClipToBounds = true };
        return canvas;
    }

    public Panel? GetInfoPagePanel(int pageIndex)
    {
        if (PageManager.GetPage(pageIndex) is not { Type: "info" })
        {
            return null;
        }
        return _pageWidgets.GetValueOrDefault(pageIndex);
    }

    private void LayoutPages()
    {
        var w = PagesContainer.Bounds.Width;
        var h = PagesContainer.Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }
        foreach (var (i, page) in _pageWidgets)
        {
            page.Width = w;
            page.Height = h;
            Canvas.SetLeft(page, i * w);
        }
        _pagesStack.Width = Count * w;
        _pagesStack.Height = h;
        Canvas.SetLeft(_pagesStack, -_currentPageIndex * w);
    }

    private void ApplyPageVisibility(HashSet<int>? visiblePages = null)
    {
        visiblePages ??= new HashSet<int>();
        visiblePages.Add(_currentPageIndex);
        foreach (var (compId, instance) in ComponentManager.Components)
        {
            var pageIndex = ComponentManager.GetComponentPage(compId);
            if (visiblePages.Contains(pageIndex))
            {
                instance.IsVisible = true;
            }
            else
            {
                instance.IsVisible = false;
            }
        }
    }

    private void GoToPage(int index, bool animate = true)
    {
        if (index < 0 || index >= Count)
        {
            Log.Debug($"[HOME] 翻页目标越界 index={index} 页数={Count}");
            return;
        }
        DeselectAll();
        var oldIndex = _currentPageIndex;
        _currentPageIndex = index;
        PageManager.SetCurrentPage(index);
        if (oldIndex != index)
        {
            Log.Debug($"[HOME] 翻页: {oldIndex} -> {index}");
            TouchPageOperation();
        }

        var w = PagesContainer.Bounds.Width;
        if (animate && oldIndex != index && w > 0)
        {
            ApplyPageVisibility(new HashSet<int> { oldIndex, index });
            var targetX = -index * w;
            var animation = new Avalonia.Animation.Animation
            {
                Duration = TimeSpan.FromMilliseconds(250),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
                Children =
                {
                    new Avalonia.Animation.KeyFrame
                    {
                        Cue = new Avalonia.Animation.Cue(0.0),
                        Setters = { new Avalonia.Styling.Setter(Canvas.LeftProperty, Canvas.GetLeft(_pagesStack)) },
                    },
                    new Avalonia.Animation.KeyFrame
                    {
                        Cue = new Avalonia.Animation.Cue(1.0),
                        Setters = { new Avalonia.Styling.Setter(Canvas.LeftProperty, targetX) },
                    },
                },
            };
            _ = animation.RunAsync(_pagesStack);
        }
        else
        {
            Canvas.SetLeft(_pagesStack, -index * w);
        }
        Dispatcher.UIThread.Post(() =>
        {
            ApplyPageVisibility();
            PageIndicator.SetCurrent(_currentPageIndex);
        }, DispatcherPriority.Background);
        PageIndicator.SetCurrent(index);
    }

    // 手势翻页

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (_editModeActive)
            {
                DeselectAll();
            }
            var meta = PageManager.GetPage(_currentPageIndex);
            if (!_editModeActive || meta is { Type: "nav" })
            {
                var pos = e.GetPosition(this);
                _swipeStartX = pos.X;
                _swipeStartY = pos.Y;
                _swipeDragging = true;
                _swipeMoved = false;
                _swipeLastDx = 0;
                TouchPageOperation();
                e.Handled = false;
                return;
            }
        }
    }

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_swipeDragging)
        {
            return;
        }
        var pos = e.GetPosition(this);
        var dx = pos.X - _swipeStartX;
        var dy = pos.Y - _swipeStartY;
        if (Math.Abs(dx) > 10 && Math.Abs(dx) > Math.Abs(dy))
        {
            if (!_swipeMoved)
            {
                _swipeMoved = true;
                var pages = new HashSet<int> { _currentPageIndex };
                if (_currentPageIndex > 0)
                {
                    pages.Add(_currentPageIndex - 1);
                }
                if (_currentPageIndex < Count - 1)
                {
                    pages.Add(_currentPageIndex + 1);
                }
                ApplyPageVisibility(pages);
            }
            var w = PagesContainer.Bounds.Width;
            if ((_currentPageIndex == 0 && dx > 0) || (_currentPageIndex == Count - 1 && dx < 0))
            {
                dx *= 0.3;
            }
            _swipeLastDx = dx;
            Canvas.SetLeft(_pagesStack, -_currentPageIndex * w + dx);
        }
    }

    private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_swipeDragging)
        {
            return;
        }
        var dx = e.GetPosition(this).X - _swipeStartX;
        var w = PagesContainer.Bounds.Width;
        var threshold = Math.Max(60, w * 0.15);
        var target = _currentPageIndex;
        if (_swipeMoved)
        {
            if (dx > threshold && _currentPageIndex > 0)
            {
                target = _currentPageIndex - 1;
            }
            else if (dx < -threshold && _currentPageIndex < Count - 1)
            {
                target = _currentPageIndex + 1;
            }
        }
        _swipeDragging = false;
        _swipeMoved = false;
        TouchPageOperation();
        GoToPage(target, animate: true);
    }

    private void OnRootWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_editModeActive)
        {
            return;
        }
        var dx = e.Delta.X;
        var dy = e.Delta.Y;
        if (Math.Abs(dx) < 1 && Math.Abs(dy) != 0)
        {
            dx = -dy;
        }
        if (Math.Abs(dx) > 0)
        {
            TouchPageOperation();
            GoToPage(_currentPageIndex + (dx > 0 ? -1 : 1), animate: true);
            e.Handled = true;
        }
    }

    // 底栏

    private void InitBottomBar()
    {
        BottomBar.Width = double.NaN;
        BackToDesktopBtn.Content = AppUtils.Tr("home.back_to_desktop");
        AddPageBtn.Content = AppUtils.Tr("home.add_page_with_count", ("count", Count));
        RenamePageBtn.Content = AppUtils.Tr("home.rename_page");
        DelPageBtn.Content = AppUtils.Tr("home.delete_page");
        PageIndicator.SetCount(Count);
        PageIndicator.SetCurrent(_currentPageIndex);
        PageIndicator.PageClicked += index => GoToPage(index, animate: true);
    }

    private void OnBackToDesktopClicked(object? sender, RoutedEventArgs e) => MainWindow.MinimizeToDesktop();

    private void OnAddPageClicked(object? sender, RoutedEventArgs e)
    {
        var newIndex = PageManager.AddPage(pageType: "info");
        if (newIndex < 0)
        {
            return;
        }
        _pageWidgets[newIndex] = CreatePageWidget(newIndex);
        LayoutPages();
        PageIndicator.SetCount(Count);
        AddPageBtn.Content = AppUtils.Tr("home.add_page_with_count", ("count", Count));
        GoToPage(newIndex, animate: true);
    }

    private async void OnRenamePageClicked(object? sender, RoutedEventArgs e)
    {
        var meta = PageManager.GetPage(_currentPageIndex);
        if (meta is null)
        {
            return;
        }
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            return;
        }
        var name = await Common.PromptTextInput(owner, AppUtils.Tr("home.rename_page_title"), meta.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            PageManager.RenamePage(_currentPageIndex, name.Trim());
        }
    }

    private async void OnDeletePageClicked(object? sender, RoutedEventArgs e)
    {
        var meta = PageManager.GetPage(_currentPageIndex);
        if (meta is null or { Type: "nav" } || Count <= 1)
        {
            return;
        }
        var fallback = -1;
        for (var i = 0; i < Count; i++)
        {
            if (i != _currentPageIndex && PageManager.GetPage(i)?.Type == "info")
            {
                fallback = i;
                break;
            }
        }
        var content = fallback >= 0
            ? AppUtils.Tr("home.delete_page_confirm", ("name", meta.Name))
            : AppUtils.Tr("home.delete_page_no_fallback", ("name", meta.Name));
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null || !await Common.Confirm(owner, AppUtils.Tr("home.delete_page"), content))
        {
            return;
        }

        var toMigrate = ComponentManager.Components.Keys
            .Where(id => ComponentManager.GetComponentPage(id) == _currentPageIndex)
            .ToList();
        foreach (var id in toMigrate)
        {
            if (fallback >= 0)
            {
                ComponentManager.SetComponentPage(id, fallback);
            }
            else
            {
                ComponentManager.RemoveComponent(id);
            }
        }
        PageManager.DeletePage(_currentPageIndex);

        var oldWidget = _pageWidgets.GetValueOrDefault(_currentPageIndex);
        if (oldWidget is not null)
        {
            _pagesStack.Children.Remove(oldWidget);
        }
        var newMap = new Dictionary<int, Panel>();
        foreach (var i in _pageWidgets.Keys.OrderBy(i => i))
        {
            newMap[newMap.Count] = _pageWidgets[i];
        }
        _pageWidgets.Clear();
        foreach (var kv in newMap)
        {
            _pageWidgets[kv.Key] = kv.Value;
        }

        PageIndicator.SetCount(Count);
        AddPageBtn.Content = AppUtils.Tr("home.add_page_with_count", ("count", Count));
        var newCur = PageManager.GetCurrentPage();
        _currentPageIndex = newCur;
        LayoutPages();
        ApplyPageVisibility();
        PageIndicator.SetCurrent(newCur);
    }

    private void OnMenuClicked(object? sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        var settingsItem = new MenuItem { Header = AppUtils.Tr("home.menu_settings") };
        settingsItem.Click += (_, _) => MainWindow.OpenSettingsWindow();
        var editItem = new MenuItem { Header = AppUtils.Tr("home.menu_component_edit") };
        editItem.Click += (_, _) => OpenComponentEditWindow();
        var restartItem = new MenuItem { Header = AppUtils.Tr("home.menu_restart") };
        restartItem.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true });
        var shutdownItem = new MenuItem { Header = AppUtils.Tr("home.menu_shutdown") };
        shutdownItem.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true });
        menu.Items.Add(settingsItem);
        menu.Items.Add(editItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(restartItem);
        menu.Items.Add(shutdownItem);
        menu.ShowAt(MenuBtn);
    }

    public void OpenComponentEditWindow()
    {
        EnterEditMode();
        var library = new ComponentLibraryWindow(ComponentRegistry);
        library.Closed += (_, _) => ExitEditMode();
        library.Show();
    }

    // 编辑模式

    public void EnterEditMode()
    {
        _editModeActive = true;
        UpdateGridMetrics();
        var meta = PageManager.GetPage(_currentPageIndex);
        _gridOverlay.UpdateGridMetrics(_gridMetrics);
        _gridOverlay.ShowPreview(false);
        if (meta is null or { Type: "info" })
        {
            _gridOverlay.IsVisible = true;
        }
        else
        {
            _gridOverlay.IsVisible = false;
        }
        AddPageBtn.IsVisible = true;
        RenamePageBtn.IsVisible = true;
        DelPageBtn.IsVisible = true;
        SetAllDraggable(true);
        Log.Info("[HOME] 进入编辑模式");
    }

    public void ExitEditMode()
    {
        _editModeActive = false;
        DeselectAll();
        _gridOverlay.IsVisible = false;
        AddPageBtn.IsVisible = false;
        RenamePageBtn.IsVisible = false;
        DelPageBtn.IsVisible = false;
        SetAllDraggable(false);
        Log.Debug("[HOME] 退出编辑模式");
    }

    private void SetAllDraggable(bool enabled)
    {
        foreach (var container in ComponentManager.GetAllContainers())
        {
            container.IsDraggable = enabled;
            container.Selected -= SelectComponent;
            if (enabled)
            {
                container.Selected += SelectComponent;
            }
        }
    }

    private void SelectComponent(string componentId)
    {
        DeselectAll();
        _selectedPlacementId = componentId;
        Log.Debug($"[HOME] 选中组件: id={componentId}");
    }

    private void DeselectAll()
    {
        _selectedPlacementId = null;
    }

    public void DeleteSelectedComponent()
    {
        if (_selectedPlacementId is null)
        {
            return;
        }
        Log.Info($"[HOME] 删除选中组件: id={_selectedPlacementId}");
        ComponentManager.RemoveComponent(_selectedPlacementId);
        DeselectAll();
    }

    public bool IsEditModeActive => _editModeActive;

    // 网格与拖放

    private void UpdateGridMetrics()
    {
        _gridMetrics = GridLayoutService.CalculateGridMetrics(Bounds.Width, Bounds.Height, _gridSettings);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var dragText = GetDragText(e);
        if (dragText is null)
        {
            return;
        }
        var meta = PageManager.GetPage(_currentPageIndex);
        if (meta is { Type: "nav" })
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        _draggingDefinitionId = dragText;
        UpdateGridMetrics();
        var def = _draggingDefinitionId is null ? null : ComponentRegistry.GetDefinition(_draggingDefinitionId);
        var previewW = def?.DefaultWidthCells * 110.0 ?? 220;
        var previewH = def?.DefaultHeightCells * 110.0 ?? 220;
        var pos = e.GetPosition(this);
        var snapped = SnapToGrid(pos.X - previewW / 2, pos.Y - previewH / 2, previewW, previewH, 20);
        var collision = CheckPixelCollision(snapped.X, snapped.Y, previewW, previewH);
        _gridOverlay.UpdatePreviewPixel(snapped.X, snapped.Y, previewW, previewH, collision);
        _gridOverlay.IsVisible = true;
    }

    private static string? GetDragText(DragEventArgs e)
    {
        foreach (var item in e.DataTransfer.Items)
        {
            if (item.TryGetRaw(Avalonia.Input.DataFormat.Text) is string text)
            {
                return text;
            }
        }
        return null;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var data = GetDragText(e);
        if (data is null || data.Length == 0)
        {
            return;
        }
        string compType;
        string compStyle;
        if (data.Contains('|'))
        {
            var parts = data.Split('|', 2);
            compType = parts[0];
            compStyle = parts[1];
        }
        else if (ComponentRegistry.GetDefinition(data) is { } def)
        {
            // definition id 即 type_style 组合 解析 type 与 style
            var defId = def.Id;
            var underscore = defId.IndexOf('_', StringComparison.Ordinal);
            if (underscore > 0)
            {
                compType = defId[..underscore];
                compStyle = defId[(underscore + 1)..];
            }
            else
            {
                compType = defId;
                compStyle = "";
            }
        }
        else
        {
            var parts = data.Split('_', 2);
            compType = parts[0];
            compStyle = parts.Length > 1 ? parts[1] : "";
        }

        var compId = ComponentManager.AddComponent(compType, compStyle, _currentPageIndex);
        if (compId is not null)
        {
            var instance = ComponentManager.Components[compId];
            var pos = e.GetPosition(this);
            var availableW = Math.Max(1, Bounds.Width - instance.Width);
            var availableH = Math.Max(1, Bounds.Height - instance.Height);
            instance.SetPositionPercent(
                Math.Clamp((pos.X - instance.Width / 2) / availableW, 0, 1),
                Math.Clamp((pos.Y - instance.Height / 2) / availableH, 0, 1));
            if (_editModeActive)
            {
                instance.IsDraggable = true;
                instance.Selected += SelectComponent;
            }
            Log.Info($"dropEvent: 组件已创建 {compId}");
        }
        _gridOverlay.IsVisible = _editModeActive;
        _draggingDefinitionId = null;
    }

    private (double X, double Y) SnapToGrid(double x, double y, double width, double height, double threshold)
    {
        if (_gridMetrics is not { } metrics)
        {
            return (x, y);
        }
        var inset = metrics.EdgeInsetPx;
        var pitch = metrics.Pitch;

        double FindNearest(double pos, bool vertical)
        {
            var count = vertical ? metrics.ColumnCount : metrics.RowCount;
            for (var i = 0; i <= count; i++)
            {
                var line = inset + i * pitch;
                if (Math.Abs(pos - line) <= threshold)
                {
                    return line;
                }
            }
            return pos;
        }

        var snappedLeft = FindNearest(x, true);
        var snappedRight = FindNearest(x + width, true);
        var snappedTop = FindNearest(y, false);
        var snappedBottom = FindNearest(y + height, false);

        var finalX = Math.Abs(snappedLeft - x) > 0.01 ? snappedLeft
            : Math.Abs(snappedRight - (x + width)) > 0.01 ? snappedRight - width : x;
        var finalY = Math.Abs(snappedTop - y) > 0.01 ? snappedTop
            : Math.Abs(snappedBottom - (y + height)) > 0.01 ? snappedBottom - height : y;
        return (finalX, finalY);
    }

    private bool CheckPixelCollision(double x, double y, double width, double height)
    {
        foreach (var container in ComponentManager.GetAllContainers())
        {
            if (!container.IsVisible)
            {
                continue;
            }
            var cx = Canvas.GetLeft(container);
            var cy = Canvas.GetTop(container);
            var cw = container.Bounds.Width;
            var ch = container.Bounds.Height;
            if (!(x + width < cx || x > cx + cw || y + height < cy || y > cy + ch))
            {
                return true;
            }
        }
        return false;
    }

    // 布局

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateGridMetrics();
        if (!_gridOverlay.IsVisible)
        {
            _gridOverlay.UpdateGridMetrics(_gridMetrics);
        }
        _gridOverlay.Width = Bounds.Width;
        _gridOverlay.Height = Bounds.Height;
        LayoutPages();
        foreach (var widget in ComponentManager.GetAllContainers())
        {
            widget.OnParentResize();
        }
    }

    public void SaveComponentPositions()
    {
        PageManager.SetCurrentPage(_currentPageIndex);
        ComponentManager.SaveComponents();
        Log.Debug("[HOME] 组件位置已保存");
    }
}

// 页面指示器

public sealed class PageIndicator : Control
{
    public event Action<int>? PageClicked;

    private int _count;
    private int _current;
    private const double DotRadius = 4;
    private const double DotRadiusActive = 6;
    private const double DotGap = 10;

    public void SetCount(int count)
    {
        _count = Math.Max(0, count);
        if (_current >= _count)
        {
            _current = Math.Max(0, _count - 1);
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetCurrent(int index)
    {
        if (index < 0 || index >= _count)
        {
            return;
        }
        _current = index;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = _count * DotRadius * 2 + Math.Max(0, _count - 1) * DotGap;
        var h = DotRadiusActive * 2 + 4;
        return new Size(Math.Max(w + 8, 16), h);
    }

    public override void Render(DrawingContext context)
    {
        var inactive = new SolidColorBrush(Color.FromArgb(200, 180, 180, 180));
        for (var i = 0; i < _count; i++)
        {
            var radius = i == _current ? DotRadiusActive : DotRadius;
            var centerX = (Width - (_count * DotRadius * 2 + Math.Max(0, _count - 1) * DotGap)) / 2
                          + i * (DotRadius * 2 + DotGap) + DotRadius;
            var centerY = Height / 2;
            var brush = i == _current
                ? new SolidColorBrush(Color.Parse(Config.ThemeColor.Value))
                : inactive;
            context.DrawEllipse(brush, null, new Point(centerX, centerY), radius, radius);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        var totalW = _count * DotRadius * 2 + Math.Max(0, _count - 1) * DotGap;
        var startX = (Width - totalW) / 2;
        for (var i = 0; i < _count; i++)
        {
            var centerX = startX + i * (DotRadius * 2 + DotGap) + DotRadius;
            if (Math.Abs(pos.X - centerX) <= DotRadiusActive && Math.Abs(pos.Y - Height / 2) <= DotRadiusActive)
            {
                PageClicked?.Invoke(i);
                e.Handled = true;
                return;
            }
        }
    }
}

// 网格覆盖层

public sealed class GridOverlay : Control
{
    private GridMetrics? _metrics;
    private bool _previewVisible;
    private double _previewX;
    private double _previewY;
    private double _previewW;
    private double _previewH;
    private bool _previewCollision;

    public void UpdateGridMetrics(GridMetrics? metrics)
    {
        _metrics = metrics;
        InvalidateVisual();
    }

    public void ShowPreview(bool visible)
    {
        _previewVisible = visible;
        if (!visible)
        {
            _previewX = 0;
            _previewY = 0;
        }
        InvalidateVisual();
    }

    public void UpdatePreviewPixel(double x, double y, double w, double h, bool collision)
    {
        _previewVisible = true;
        _previewX = x;
        _previewY = y;
        _previewW = w;
        _previewH = h;
        _previewCollision = collision;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_metrics is not { } metrics || metrics.CellSize <= 0)
        {
            return;
        }
        var inset = metrics.EdgeInsetPx;
        var pitch = metrics.Pitch;
        var cellSize = metrics.CellSize;
        var dash = Math.Max(2, cellSize * 0.25);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 200, 200, 200)), 1, new DashStyle(new double[] { dash, dash }, 0));

        var rightEdge = Width - inset;
        var bottomEdge = Height - inset;

        for (var col = 0; ; col++)
        {
            var x = inset + col * pitch;
            if (x > rightEdge)
            {
                break;
            }
            context.DrawLine(gridPen, new Point(x, inset), new Point(x, bottomEdge));
        }
        context.DrawLine(gridPen, new Point(rightEdge, inset), new Point(rightEdge, bottomEdge));

        for (var row = 0; ; row++)
        {
            var y = inset + row * pitch;
            if (y > bottomEdge)
            {
                break;
            }
            context.DrawLine(gridPen, new Point(inset, y), new Point(rightEdge, y));
        }
        context.DrawLine(gridPen, new Point(inset, bottomEdge), new Point(rightEdge, bottomEdge));

        if (_previewVisible)
        {
            var borderColor = new SolidColorBrush(_previewCollision ? Color.Parse("#FF3B30") : Color.Parse("#0A84FF"));
            var fillColor = new SolidColorBrush(_previewCollision ? Color.FromArgb(100, 255, 59, 48) : Color.FromArgb(100, 10, 132, 255));
            var rect = new Rect(_previewX, _previewY, _previewW, _previewH);
            var minSide = Math.Min(rect.Width, rect.Height);
            var corner = Math.Clamp(minSide * 0.11, 14, 26);
            context.FillRectangle(fillColor, rect, (float)corner);
            context.DrawRectangle(new Pen(borderColor, 2), rect, (float)corner);
        }
    }
}
