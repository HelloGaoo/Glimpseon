using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using ClassLively_UI.Helpers;

namespace ClassLively_UI;

public sealed partial class MainWindow : Window
{
    #region 定时器

    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    #endregion

    #region 状态

    private bool _autoOpenEnabled = true;
    private int _idleThresholdMinutes = 5;
    private bool _hasTriggeredAutoOpen;
    private bool _isWindowVisible = true;
    private string _windowPositionsPath = System.IO.Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "config", "window_state.json");
    private bool _closeToTray = true;

    #endregion

    #region P/Invoke

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    #endregion

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        RestoreWindowState();

        _idleTimer.Tick += OnIdleCheck;
        InitSystemTray();
        AppWindow.Closing += OnAppWindowClosing;
        Closed += OnMainWindowClosed;
        Activated += OnWindowActivated;

        // 首页导航延迟到窗口激活后
        Activated += OnFirstActivated;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        NavFrame.Navigate(typeof(Pages.HomePage));
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.CodeActivated ||
            e.WindowActivationState == WindowActivationState.PointerActivated)
        {
            if (!_isWindowVisible)
            {
                _isWindowVisible = true;
                _idleTimer.Stop();
            }
        }
        else if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (_isWindowVisible)
            {
                _isWindowVisible = false;
                _hasTriggeredAutoOpen = false;
                UpdateIdleTimerState();
            }
        }
    }

    private void OnAppWindowClosing(object sender, AppWindowClosingEventArgs args)
    {
        if (_closeToTray)
        {
            args.Cancel = true;
            HideToTray();
        }
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        SaveWindowState();
        _idleTimer.Stop();
        TrayIconHelper.Remove();
        try { NativeBindings.uninstall_hook(); } catch { }
    }

    /// <summary>隐藏窗口到系统托盘</summary>
    public void HideToTray()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hWnd, SW_HIDE);
        TrayIconHelper.MinimizeToTray();
        Debug.WriteLine("[MainWindow] 窗口已隐藏到托盘");
    }

    /// <summary>从系统托盘恢复窗口显示</summary>
    public void RestoreFromTray()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hWnd, SW_SHOW);
        Activate();
        Debug.WriteLine("[MainWindow] 窗口已从托盘恢复");
    }

    private void InitSystemTray()
    {
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

            TrayIconHelper.Create(hWnd, iconPath, "ClassLively",
                RestoreFromTray,
                () => Application.Current.Exit(),
                false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] 系统托盘初始化失败: {ex.Message}");
        }
    }

    /// <summary>启动/停止空闲检测</summary>
    public void SetIdleDetection(bool enabled, int idleMinutes = 5)
    {
        _autoOpenEnabled = enabled;
        _idleThresholdMinutes = Math.Max(1, idleMinutes);
        UpdateIdleTimerState();
    }

    private void UpdateIdleTimerState()
    {
        if (_autoOpenEnabled && !_isWindowVisible)
        {
            if (!_idleTimer.IsEnabled)
                _idleTimer.Start();
        }
        else
        {
            _idleTimer.Stop();
            _hasTriggeredAutoOpen = false;
        }
    }

    private async void OnIdleCheck(object sender, object e)
    {
        try
        {
            if (!_autoOpenEnabled) return;
            if (_isWindowVisible) { _hasTriggeredAutoOpen = false; return; }

            int idleMs = NativeBindings.SafeGetIdleMs();
            if (idleMs < 0) return;

            try { if (NativeBindings.was_page_operation_recent(5000)) return; } catch { }

            var thresholdMs = _idleThresholdMinutes * 60 * 1000;
            if (idleMs <= thresholdMs || _hasTriggeredAutoOpen) return;

            Debug.WriteLine($"[MainWindow] 检测到电脑空闲超过{_idleThresholdMinutes}分钟，自动打开界面");
            AutoOpenFromMinimized();
            _hasTriggeredAutoOpen = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] 空闲检测异常: {ex.Message}");
        }
    }

    private void AutoOpenFromMinimized()
    {
        NavFrame.Navigate(typeof(Pages.HomePage));
        _isWindowVisible = true;
        Activate();
    }

    #region NavigationView 事件

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
            NavFrame.GoBack();
    }

    private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        var tag = item.Tag as string;
        Type? pageType = tag switch
        {
            "home" => typeof(Pages.HomePage),
            "wallpaper" => typeof(Pages.WallpaperPage),
            "download" => typeof(Pages.DownloadPage),
            "settings" => typeof(Pages.SettingsPage),
            "update" => typeof(Pages.UpdatePage),
            "about" => typeof(Pages.AboutPage),
            "debug" => typeof(Pages.DebugPage),
            _ => null
        };

        if (pageType != null)
        {
            try
            {
                Debug.WriteLine($"[Nav] 正在导航到: {tag} -> {pageType.Name}");
                NavFrame.Navigate(pageType);
                Debug.WriteLine($"[Nav] 导航成功: {tag}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Nav] 导航到 {tag} 失败: {ex}");
                var dialog = new ContentDialog
                {
                    Title = "页面加载失败",
                    Content = $"无法打开「{item.Content}」页面\n\n错误信息:\n{ex.Message}\n\n{ex.GetType().Name}",
                    CloseButtonText = "确定",
                    XamlRoot = NavFrame.XamlRoot
                };
                _ = dialog.ShowAsync();
            }
        }
    }

    #endregion

    #region 窗口状态管理

    private void SaveWindowState()
    {
        try
        {
            var appWindow = AppWindow;
            var pos = appWindow.Position;
            var size = appWindow.ClientSize;

            var state = new
            {
                X = pos.X,
                Y = pos.Y,
                Width = size.Width,
                Height = size.Height,
                Maximized = appWindow.Presenter is OverlappedPresenter olp
                    && olp.State == OverlappedPresenterState.Maximized
            };

            var dir = System.IO.Path.GetDirectoryName(_windowPositionsPath);
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            System.IO.File.WriteAllText(_windowPositionsPath,
                System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Debug.WriteLine($"[MainWindow] 窗口状态已保存: {_windowPositionsPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] 保存窗口状态失败: {ex.Message}");
        }
    }

    private void RestoreWindowState()
    {
        try
        {
            if (!System.IO.File.Exists(_windowPositionsPath)) return;

            var json = System.IO.File.ReadAllText(_windowPositionsPath);
            var state = System.Text.Json.JsonSerializer.Deserialize<WindowStateData>(json);
            if (state == null) return;

            var appWindow = AppWindow;
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                if (state.Maximized)
                    presenter.Maximize();
                else
                    appWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(state.X, state.Y, state.Width, state.Height));
            }

            Debug.WriteLine("[MainWindow] 窗口状态已恢复");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] 恢复窗口状态失败: {ex.Message}");
        }
    }

    /// <summary>设置关闭行为</summary>
    public void SetCloseBehavior(bool minimizeToTray)
    {
        _closeToTray = minimizeToTray;
        Debug.WriteLine($"[MainWindow] 关闭行为已设置: {(minimizeToTray ? "最小化到托盘" : "直接退出")}");
    }

    private class WindowStateData
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Maximized { get; set; }
    }

    #endregion
}
