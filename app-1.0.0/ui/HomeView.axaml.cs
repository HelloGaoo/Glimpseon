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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;

namespace Glimpseon.UI;

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
    private SelectionAdorner? _adorner;
    private Button? _configBtn;
    private Button? _deleteBtn;
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
        if (!File.Exists(path))
        {
            return;
        }
        try
        {
            _originalBitmap?.Dispose();
            _originalBitmap = new Bitmap(path);
            _wallpaperPath = path;
            ApplyBackgroundEffects();
            Log.Info($"[HOME] 壁纸已应用: {path}");
        }
        catch (Exception e)
        {
            Log.Error($"[HOME] 壁纸应用失败: {e.Message}");
        }
    }

    private string? _wallpaperPath;

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
        PagesContainer.SizeChanged += (_, _) => LayoutPages();

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
        ApplyPageVisibility();
        Log.Debug($"[HOME] 页面就绪 {Count}页 当前={_currentPageIndex}");
    }

    private int Count => PageManager.Count;

    private Panel CreatePageWidget(int pageIndex)
    {
        Panel panel;
        var meta = PageManager.GetPage(pageIndex);
        if (meta is { Type: "nav" })
        {
            panel = new NavigationPage(pageIndex, PageManager);
        }
        else
        {
        var canvas = new Canvas { ClipToBounds = true };
            canvas.SizeChanged += (_, _) =>
        {
            foreach (var child in canvas.Children)
            {
                if (child is DraggableContainer container)
                {
                    container.ApplyPercent();
                }
            }
        };
            panel = canvas;
        }
        _pagesStack.Children.Add(panel);
        return panel;
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
            var enabled = ComponentManager.IsComponentEnabled(compId);
            instance.IsVisible = visiblePages.Contains(pageIndex) && enabled;
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
        var targetX = -index * w;
        _pageAnimStopwatch?.Stop();

        if (animate && oldIndex != index && w > 0)
        {
            ApplyPageVisibility(new HashSet<int> { oldIndex, index });
            AnimatePagesStackTo(targetX, () =>
            {
                ApplyPageVisibility();
                PageIndicator.SetCurrent(_currentPageIndex);
            });
        }
        else
        {
            Canvas.SetLeft(_pagesStack, targetX);
            ApplyPageVisibility();
            PageIndicator.SetCurrent(_currentPageIndex);
        }
        PageIndicator.SetCurrent(index);
    }

    // 滑动动画
    private System.Diagnostics.Stopwatch? _pageAnimStopwatch;
    private DispatcherTimer? _pageAnimTimer;
    private double _animFromX;
    private double _animToX;

    private void AnimatePagesStackTo(double targetX, Action finished)
    {
        _animFromX = Canvas.GetLeft(_pagesStack);
        if (double.IsNaN(_animFromX))
        {
            _animFromX = 0;
        }
        _animToX = targetX;
        if (Math.Abs(_animToX - _animFromX) < 0.5)
        {
            Canvas.SetLeft(_pagesStack, targetX);
            finished();
            return;
        }

        Log.Debug($"[ANIM] 开始 from={_animFromX:F0} to={_animToX:F0}");
        _pageAnimStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _pageAnimTimer?.Stop();
        _pageAnimTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        const double durationMs = 250;
        _pageAnimTimer.Tick += (_, _) =>
        {
            var t = Math.Min(1.0, _pageAnimStopwatch.Elapsed.TotalMilliseconds / durationMs);
            // OutCubic: 1 - (1-t)^3
            var eased = 1 - Math.Pow(1 - t, 3);
            Canvas.SetLeft(_pagesStack, _animFromX + (_animToX - _animFromX) * eased);
            if (t >= 1.0)
            {
                _pageAnimTimer?.Stop();
                _pageAnimTimer = null;
                Canvas.SetLeft(_pagesStack, _animToX);
                Log.Debug($"[ANIM] 完成 to={_animToX:F0}");
                finished();
            }
        };
        _pageAnimTimer.Start();
    }

    // 手势翻页
    private bool IsWithinDraggable(Visual? v)
    {
        while (v is not null)
        {
            if (v is DraggableContainer
                || ReferenceEquals(v, _configBtn)
                || ReferenceEquals(v, _deleteBtn)
                || ReferenceEquals(v, _adorner))
            {
                return true;
            }
            v = v.GetVisualParent();
        }
        return false;
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (_editModeActive)
            {
                // 点在组件上不取消选中
                if (!IsWithinDraggable(e.Source as Visual))
                {
                    DeselectAll();
                }
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
        BackToDesktopBtn.Content = AppUtils.Tr("home.back_to_desktop");
        AddPageBtn.Content = AppUtils.Tr("home.add_page_with_count", ("count", Count));
        RenamePageBtn.Content = AppUtils.Tr("home.rename_page");
        DelPageBtn.Content = AppUtils.Tr("home.delete_page");
        PageIndicator.SetCount(Count);
        PageIndicator.SetCurrent(_currentPageIndex);
        PageIndicator.PageClicked += index => GoToPage(index, animate: true);

        // 岛样式跟随组件不透明度/圆角/主题
        ApplyIslandStyle();
        Config.ComponentCardOpacity.ValueChanged += _ => ApplyIslandStyle();
        Config.ComponentCardRadius.ValueChanged += _ => ApplyIslandStyle();
        ActualThemeVariantChanged += (_, _) => ApplyIslandStyle();

        InitProfileMenu();
    }

    // 底部岛
    private void ApplyIslandStyle()
    {
        var dark = ThemeSense.IsDark(this);
        var op = Math.Clamp(Config.ComponentCardOpacity.Value / 100.0, 0, 1);
        BottomBar.Background = dark
            ? new SolidColorBrush(Color.FromRgb(30, 30, 30), op)
            : new SolidColorBrush(Colors.White, op);
        BottomBar.BorderBrush = new SolidColorBrush(dark ? Colors.White : Colors.Black, dark ? 0.10 : 0.06);
        BottomBar.BorderThickness = new Thickness(1);
        BottomBar.CornerRadius = new CornerRadius(Math.Max(8, Config.ComponentCardRadius.Value));

        // 回到桌面按钮
        BackToDesktopBtn.Background = dark
            ? new SolidColorBrush(Colors.White, 0.08)
            : new SolidColorBrush(Colors.Black, 0.06);
        BackToDesktopBtn.Foreground = dark
            ? new SolidColorBrush(Colors.White)
            : new SolidColorBrush(Color.Parse("#333333"));
        BackToDesktopBtn.CornerRadius = new CornerRadius(8);
        BackToDesktopBtn.Padding = new Thickness(20, 8, 20, 8);

        MenuBtn.Foreground = dark
            ? new SolidColorBrush(Colors.White)
            : new SolidColorBrush(Color.Parse("#333333"));
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

    // 右下角菜单

    private bool _isPowerMenuOpen;
    private bool _isPowerMenuAnimating;
    private DateTime _popupDismissedAt = DateTime.MinValue;
    private static Bitmap? _avatarBitmap;
    private static bool _avatarLoaded;

    private void InitProfileMenu()
    {
        ProfileMenuPopup.PlacementTarget = MenuBtn;
        ProfileMenuPopup.Closed += (_, _) =>
        {
            _popupDismissedAt = DateTime.Now;
            ResetPowerMenuState();
        };
    }

    private void OnMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (ProfileMenuPopup.IsOpen)
        {
            ProfileMenuPopup.IsOpen = false;
            return;
        }
        if ((DateTime.Now - _popupDismissedAt).TotalMilliseconds < 250)
        {
            return;
        }
        ResetPowerMenuState();
        RefreshProfileMenu();
        ProfileMenuPopup.IsOpen = true;
    }

    private void CloseProfileMenu()
    {
        if (ProfileMenuPopup.IsOpen)
        {
            ProfileMenuPopup.IsOpen = false;
        }
    }

    // 打开时刷新文案/头像/配色
    private void RefreshProfileMenu()
    {
        var name = ResolveDisplayName();
        ProfileMenuNameText.Text = name;

        if (!_avatarLoaded)
        {
            _avatarBitmap = TryLoadAvatarBitmap();
            _avatarLoaded = true;
        }
        ProfileMenuAvatarImage.Source = _avatarBitmap;
        ProfileMenuAvatarImage.IsVisible = _avatarBitmap is not null;
        ProfileMenuAvatarFallbackText.Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "U";
        ProfileMenuAvatarFallbackText.IsVisible = _avatarBitmap is null;

        ProfileMenuSettingsText.Text = AppUtils.Tr("home.menu_settings");
        ProfileMenuEditText.Text = AppUtils.Tr("home.menu_component_edit");
        ProfileMenuPowerText.Text = AppUtils.Tr("home.menu_power");
        ProfileMenuPowerTitle.Text = AppUtils.Tr("home.menu_power");
        ProfileMenuBackText.Text = AppUtils.Tr("home.menu_back");
        ProfileMenuShutdownText.Text = AppUtils.Tr("home.menu_shutdown");
        ProfileMenuRestartText.Text = AppUtils.Tr("home.menu_restart");
        ProfileMenuLogoutText.Text = AppUtils.Tr("home.menu_logout");
        ProfileMenuSleepText.Text = AppUtils.Tr("home.menu_sleep");
        ProfileMenuLockText.Text = AppUtils.Tr("home.menu_lock");

        ApplyProfileMenuTheme();
    }

    private static string ResolveDisplayName()
    {
        var userName = Environment.UserName?.Trim();
        return string.IsNullOrWhiteSpace(userName) ? "User" : userName;
    }

    // 读取 Windows 账户头像
    private static Bitmap? TryLoadAvatarBitmap()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }
            foreach (var dir in new[]
                     {
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "AccountPictures"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "AccountPictures"),
                     })
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }
                var file = new DirectoryInfo(dir).EnumerateFiles("*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.Extension is ".png" or ".jpg" or ".jpeg" or ".bmp")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (file is not null)
                {
                    return new Bitmap(file.FullName);
                }
            }
            var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "User Account Pictures");
            foreach (var fileName in new[] { "user-448.png", "user-240.png", "user-192.png", "user-96.png", "user-48.png", "user.png" })
            {
                var path = Path.Combine(common, fileName);
                if (File.Exists(path))
                {
                    return new Bitmap(path);
                }
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[菜单] 头像加载失败: {e.Message}");
        }
        return null;
    }

    // 菜单配色
    private void ApplyProfileMenuTheme()
    {
        var dark = ThemeSense.IsDark(this);
        var accent = Common.ParseAccentColor(Config.ThemeColor.Value);

        void Set(string key, Color color) => ProfileMenuPanel.Resources[key] = new SolidColorBrush(color);

        if (dark)
        {
            Set("ProfileMenuSurfaceBrush", Color.FromRgb(30, 30, 30));
            Set("ProfileMenuOutlineBrush", Color.FromArgb(26, 255, 255, 255));
            Set("ProfileMenuAvatarSurfaceBrush", Color.FromArgb(28, 255, 255, 255));
            Set("ProfileMenuTextBrush", Colors.White);
            Set("ProfileMenuAccentBrush", accent);
            Set("ProfileMenuHoverBrush", Color.FromArgb(20, 255, 255, 255));
            Set("ProfileMenuPressedBrush", Color.FromArgb(40, 255, 255, 255));
            Set("ProfileMenuDividerBrush", Color.FromArgb(30, 255, 255, 255));
        }
        else
        {
            Set("ProfileMenuSurfaceBrush", Color.FromRgb(252, 252, 255));
            Set("ProfileMenuOutlineBrush", Color.FromArgb(24, 17, 24, 39));
            Set("ProfileMenuAvatarSurfaceBrush", Color.FromArgb(12, 0, 0, 0));
            Set("ProfileMenuTextBrush", Color.Parse("#333333"));
            Set("ProfileMenuAccentBrush", accent);
            Set("ProfileMenuHoverBrush", Color.FromArgb(15, 0, 0, 0));
            Set("ProfileMenuPressedBrush", Color.FromArgb(30, 0, 0, 0));
            Set("ProfileMenuDividerBrush", Color.FromArgb(24, 17, 24, 39));
        }
    }

    private void ResetPowerMenuState()
    {
        _isPowerMenuOpen = false;
        _isPowerMenuAnimating = false;

        ProfileMenuMainPanel.IsVisible = true;
        ProfileMenuMainPanel.Opacity = 1;
        ProfileMenuPowerPanel.IsVisible = false;
        ProfileMenuPowerPanel.Opacity = 0;
        if (ProfileMenuPowerPanel.RenderTransform is TranslateTransform transform)
        {
            transform.X = 255;
        }
    }

    private async void EnterPowerMenu()
    {
        if (_isPowerMenuAnimating || _isPowerMenuOpen)
        {
            return;
        }
        _isPowerMenuAnimating = true;

        ProfileMenuPowerPanel.IsVisible = true;
        ProfileMenuPowerPanel.Opacity = 0;
        if (ProfileMenuPowerPanel.RenderTransform is TranslateTransform transform)
        {
            transform.X = 255;
        }

        await Task.Delay(16);

        ProfileMenuMainPanel.Opacity = 0;
        ProfileMenuPowerPanel.Opacity = 1;
        if (ProfileMenuPowerPanel.RenderTransform is TranslateTransform slide)
        {
            slide.X = 0;
        }

        await Task.Delay(280);

        ProfileMenuMainPanel.IsVisible = false;
        _isPowerMenuOpen = true;
        _isPowerMenuAnimating = false;
    }

    private async void ExitPowerMenu()
    {
        if (_isPowerMenuAnimating || !_isPowerMenuOpen)
        {
            return;
        }
        _isPowerMenuAnimating = true;

        ProfileMenuMainPanel.IsVisible = true;
        ProfileMenuMainPanel.Opacity = 0;
        if (ProfileMenuPowerPanel.RenderTransform is TranslateTransform transform)
        {
            transform.X = 0;
        }

        await Task.Delay(16);

        ProfileMenuMainPanel.Opacity = 1;
        ProfileMenuPowerPanel.Opacity = 0;
        if (ProfileMenuPowerPanel.RenderTransform is TranslateTransform slide)
        {
            slide.X = 255;
        }

        await Task.Delay(280);

        ProfileMenuPowerPanel.IsVisible = false;
        _isPowerMenuOpen = false;
        _isPowerMenuAnimating = false;
    }

    private void OnProfileMenuSettingsClick(object? sender, RoutedEventArgs e)
    {
        CloseProfileMenu();
        MainWindow.OpenSettingsWindow();
    }

    private void OnProfileMenuEditClick(object? sender, RoutedEventArgs e)
    {
        CloseProfileMenu();
        OpenComponentEditWindow();
    }

    private void OnProfileMenuPowerEnterClick(object? sender, RoutedEventArgs e) => EnterPowerMenu();

    private void OnProfileMenuPowerBackClick(object? sender, RoutedEventArgs e) => ExitPowerMenu();

    private void OnProfileMenuShutdownClick(object? sender, RoutedEventArgs e)
    {
        CloseProfileMenu();
        Log.Info("[菜单] 关机");
        RunPowerCommand("/s /t 0");
    }

    private void OnProfileMenuRestartClick(object? sender, RoutedEventArgs e)
    {
        CloseProfileMenu();
        Log.Info("[菜单] 重启");
        RunPowerCommand("/r /t 0");
    }

    private void OnProfileMenuLogoutClick(object? sender, RoutedEventArgs e)
    {
        CloseProfileMenu();
        Log.Info("[菜单] 注销");
        RunPowerCommand("/l");
    }

    private void OnProfileMenuSleepClick(object? sender, RoutedEventArgs e)
    {
        CloseProfileMenu();
        Log.Info("[菜单] 睡眠");
        Glimpseon.Core.Win32.Native.SleepSystem();
    }

    private void OnProfileMenuLockClick(object? sender, RoutedEventArgs e)
    {
        CloseProfileMenu();
        Log.Info("[菜单] 锁定");
        Glimpseon.Core.Win32.Native.LockScreen();
    }

    private static void RunPowerCommand(string arguments)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", arguments) { CreateNoWindow = true });
        }
        catch (Exception ex)
        {
            Log.Error($"[菜单] 电源命令执行失败 args={arguments} {ex.Message}");
        }
    }

    public void OpenComponentEditWindow()
    {
        EnterEditMode();
        var library = new ComponentLibraryWindow(ComponentRegistry);
        library.DefinitionActivated = AddComponentById;
        library.Closed += (_, _) => ExitEditMode();
        library.Show();
    }

    // 双击组件库卡片添加到当前页中心
    public void AddComponentById(string defId)
    {
        var def = ComponentRegistry.GetDefinition(defId);
        if (def is null)
        {
            return;
        }
        var underscore = defId.IndexOf('_', StringComparison.Ordinal);
        var compType = underscore > 0 ? defId[..underscore] : defId;
        var compStyle = underscore > 0 ? defId[(underscore + 1)..] : "";
        var compId = ComponentManager.AddComponent(compType, compStyle, _currentPageIndex);
        if (compId is not null)
        {
            var instance = ComponentManager.Components[compId];
            var n = ComponentManager.Components.Count;
            var offset = (n % 5) * 0.04;
            instance.SetPositionPercent(Math.Clamp(0.42 + offset, 0, 1), Math.Clamp(0.40 + offset, 0, 1));
            if (_editModeActive)
            {
                instance.IsDraggable = true;
                instance.Selected += SelectComponent;
            }
            Log.Info($"[组件库] 双击添加 {compId}");
        }
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
        if (!ComponentManager.Components.TryGetValue(componentId, out var container))
        {
            Log.Debug($"[HOME] 选中组件: id={componentId} 容器未找到");
            return;
        }
        container.IsSelected = true;
        container.GeometryChanged -= OnSelectedGeometryChanged;
        container.GeometryChanged += OnSelectedGeometryChanged;
        ShowSelection(container);
        Log.Debug($"[HOME] 选中组件: id={componentId}");
    }

    private void DeselectAll()
    {
        if (_selectedPlacementId is not null &&
            ComponentManager.Components.TryGetValue(_selectedPlacementId, out var old))
        {
            old.IsSelected = false;
            old.GeometryChanged -= OnSelectedGeometryChanged;
        }
        _selectedPlacementId = null;
        HideSelection();
    }

    private void OnSelectedGeometryChanged()
    {
        if (_selectedPlacementId is not null &&
            ComponentManager.Components.TryGetValue(_selectedPlacementId, out var c))
        {
            PositionSelection(c);
        }
    }

    // 选中装饰 编辑按钮 

    private void EnsureSelectionVisuals()
    {
        if (_adorner is not null)
        {
            return;
        }
        _adorner = new SelectionAdorner();
        _configBtn = MakeEditButton(FASymbol.Settings, _ConfigHover, isDelete: false);
        _deleteBtn = MakeEditButton(FASymbol.Delete, _DeleteHover, isDelete: true);
        _configBtn.Click += (_, _) => OpenSelectedComponentConfig();
        _deleteBtn.Click += (_, _) => DeleteSelectedComponent();
    }

    private static readonly Color _ConfigHover = Color.FromRgb(0, 120, 212);
    private static readonly Color _DeleteHover = Color.FromRgb(220, 80, 80);

    private const double EditBtnLeftShift = 16;
    private const double EditBtnDownShift = 16;

    private Button MakeEditButton(FASymbol symbol, Color hover, bool isDelete)
    {
        var btn = new Button
        {
            Width = 48,
            Height = 48,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new FASymbolIcon { Symbol = symbol, FontSize = 22 },
            ZIndex = 5001,
        };
        void Rest() => ApplyEditButtonStyle(btn, hover, isDelete, hovered: false);
        void Hov() => ApplyEditButtonStyle(btn, hover, isDelete, hovered: true);
        btn.PointerEntered += (_, _) => Hov();
        btn.PointerExited += (_, _) => Rest();
        Rest();
        return btn;
    }

    private void ApplyEditButtonStyle(Button btn, Color hover, bool isDelete, bool hovered)
    {
        var dark = ThemeSense.IsDark(this);
        var op = Math.Clamp(Config.ComponentCardOpacity.Value / 100.0, 0, 1);
        var radius = Math.Max(0, Config.ComponentCardRadius.Value);
        btn.CornerRadius = new CornerRadius(radius);
        btn.BorderThickness = new Thickness(1);
        if (hovered)
        {
            btn.Background = new SolidColorBrush(hover, 0.85);
            btn.BorderBrush = new SolidColorBrush(hover, 0.9);
        }
        else
        {
            var bg = dark ? Color.FromRgb(40, 40, 40) : Colors.White;
            btn.Background = new SolidColorBrush(bg, op);
            btn.BorderBrush = new SolidColorBrush(dark ? Colors.White : Colors.Black, dark ? 0.10 : 0.08);
        }
    }

    private void ShowSelection(DraggableContainer container)
    {
        EnsureSelectionVisuals();
        if (container.Parent is not Panel panel || _adorner is null || _configBtn is null || _deleteBtn is null)
        {
            return;
        }
        (_adorner.Parent as Panel)?.Children.Remove(_adorner);
        (_configBtn.Parent as Panel)?.Children.Remove(_configBtn);
        (_deleteBtn.Parent as Panel)?.Children.Remove(_deleteBtn);
        panel.Children.Add(_adorner);
        panel.Children.Add(_deleteBtn);
        panel.Children.Add(_configBtn);

        _adorner.Target = container;
        _adorner.CardRadius = container.CardRadius;
        _adorner.SetAccent(AccentColor());
        ApplyEditButtonStyle(_configBtn, _ConfigHover, isDelete: false, hovered: false);
        ApplyEditButtonStyle(_deleteBtn, _DeleteHover, isDelete: true, hovered: false);
        PositionSelection(container);
        _adorner.IsVisible = true;
        _configBtn.IsVisible = true;
        _deleteBtn.IsVisible = true;
    }

    private void PositionSelection(DraggableContainer container)
    {
        if (_adorner is null || _configBtn is null || _deleteBtn is null)
        {
            return;
        }
        var left = Canvas.GetLeft(container);
        var top = Canvas.GetTop(container);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
        var w = container.VisualWidth;
        var h = container.VisualHeight;

        // 圆角跟随组件配置
        _adorner.CardRadius = container.CardRadius;

        Canvas.SetLeft(_adorner, left - SelectionAdorner.PadL);
        Canvas.SetTop(_adorner, top - SelectionAdorner.PadT);
        _adorner.Width = w + SelectionAdorner.PadL + SelectionAdorner.PadR;
        _adorner.Height = h + SelectionAdorner.PadT + SelectionAdorner.PadB;

        var delX = left + w - 48 + 4 - EditBtnLeftShift;
        var delY = top + h + 4 + EditBtnDownShift;
        Canvas.SetLeft(_deleteBtn, delX);
        Canvas.SetTop(_deleteBtn, delY);
        Canvas.SetLeft(_configBtn, delX - 48 - 8);
        Canvas.SetTop(_configBtn, delY);
        _adorner.InvalidateVisual();
    }

    private void HideSelection()
    {
        if (_adorner is not null)
        {
            _adorner.IsVisible = false;
        }
        if (_configBtn is not null)
        {
            _configBtn.IsVisible = false;
        }
        if (_deleteBtn is not null)
        {
            _deleteBtn.IsVisible = false;
        }
    }

    private static Color AccentColor()
    {
        try
        {
            return Color.Parse(Config.ThemeColor.Value);
        }
        catch
        {
            return Color.Parse("#30c361");
        }
    }

    private async void OpenSelectedComponentConfig()
    {
        if (_selectedPlacementId is null)
        {
            return;
        }
        var id = _selectedPlacementId;
        var cfg = ComponentManager.GetComponentConfig(id);
        var saved = await ComponentConfigDialog.ShowAsync(MainWindow, cfg);
        if (saved is null)
        {
            return;
        }
        ComponentManager.UpdateComponentConfig(id, saved);
        SelectComponent(id);
        Log.Info($"[HOME] 组件配置已保存: {id}");
    }

    public void AfterComponentRebuilt(DraggableContainer container)
    {
        if (!_editModeActive)
        {
            return;
        }
        container.IsDraggable = true;
        container.Selected -= SelectComponent;
        container.Selected += SelectComponent;
    }

    public void DeleteSelectedComponent()
    {
        if (_selectedPlacementId is null)
        {
            return;
        }
        Log.Info($"[HOME] 删除选中组件: id={_selectedPlacementId}");
        var id = _selectedPlacementId;
        DeselectAll();
        ComponentManager.RemoveComponent(id);
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
        var previewW = def?.DefaultWidth ?? 220;
        var previewH = def?.DefaultHeight ?? 220;
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
        BottomBar.Width = Math.Clamp(Bounds.Width - 48, 460, 1400);
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
    private Color _activeColor = Color.Parse("#30c361");
    private const double DotRadius = 4;
    private const double DotRadiusActive = 6;
    private const double DotGap = 10;

    public PageIndicator()
    {
        try
        {
            _activeColor = Color.Parse(Config.ThemeColor.Value);
        }
        catch (Exception e)
        {

            Log.Debug($"[HOME] 配置色解析失败: {e.Message}");
        }
        Config.ThemeColor.ValueChanged += c =>
        {
            try
            {
                _activeColor = Color.Parse(c);
            }
            catch (Exception e)
            {

                Log.Debug($"[HOME] 处理失败: {e.Message}");
            }
            InvalidateVisual();
        };
    }

    // 点击圆点切页
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        var totalW = _count * DotRadius * 2 + Math.Max(0, _count - 1) * DotGap;
        var startX = (Width - totalW) / 2;
        for (var i = 0; i < _count; i++)
        {
            var cx = startX + i * (DotRadius * 2 + DotGap) + DotRadius;
            if (Math.Abs(pos.X - cx) <= DotRadius + 6)
            {
                PageClicked?.Invoke(i);
                e.Handled = true;
                return;
            }
        }
    }

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
        var active = new SolidColorBrush(_activeColor);
        for (var i = 0; i < _count; i++)
        {
            var radius = i == _current ? DotRadiusActive : DotRadius;
            var centerX = (Width - (_count * DotRadius * 2 + Math.Max(0, _count - 1) * DotGap)) / 2
                          + i * (DotRadius * 2 + DotGap) + DotRadius;
            var centerY = Height / 2;
            var brush = i == _current ? active : inactive;
            context.DrawEllipse(brush, null, new Point(centerX, centerY), radius, radius);
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
