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

// 主窗口
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Glimpseon.Core;
using Glimpseon.Core.Win32;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Avalonia.VisualTree;

namespace Glimpseon.UI;

public partial class MainWindow : GlimpseonWindow
{
    public HomeView HomeView { get; private set; } = null!;
    public WallpaperView WallpaperView { get; private set; } = null!;
    public NotificationView NotificationView { get; private set; } = null!;
    public TimetableView TimetableView { get; private set; } = null!;
    public DownloadView DownloadView { get; private set; } = null!;
    public AboutView AboutView { get; private set; } = null!;
    public DebugView DebugView { get; private set; } = null!;

    private TrayIcon? _trayIcon;
    private FAFrame? _navFrame;
    private DispatcherTimer? _idleTimer;
    private DispatcherTimer? _ntpTimer;
    private DispatcherTimer? _themeCheckTimer;
    private DispatcherTimer? _restartDebounce;
    private bool _restartPrompting;
    private bool _hasTriggeredAutoOpen;

    public MainWindow()
    {
        InitializeComponent();
        Title = Constants.AppName;
        LoadWindowIcon();

        InitTranslation();
        InitNavigation();

        Nav.SelectedItem = NavItemHome;

        InitTray();
        InitIdleDetection();
        InitTimeSync();
        InitThemeMonitor();

        Config.DebugMode.ValueChanged += OnDebugModeChanged;
        NavItemDebug.IsVisible = Config.DebugMode.Value;

        InitSettingsWatch();

        Log.Info("主窗口就绪");
    }

    private void LoadWindowIcon()
    {
        try
        {
            var iconPath = Paths.GetResourcePath(Constants.AppIcon);
            if (File.Exists(iconPath))
            {
                var bitmap = new Bitmap(iconPath);
                Icon = bitmap;
                WindowIcon.Source = bitmap;
            }
            else
            {
                Log.Warning("窗口图标文件不存在");
            }
            VersionText.Text = Paths.Version;
        }
        catch (Exception e)
        {
            Log.Warning($"窗口图标加载失败: {e.Message}");
        }
    }

    private void InitTranslation()
    {
        NavItemHome.Content = AppUtils.Tr("navigation.home");
        NavItemWallpaper.Content = AppUtils.Tr("navigation.wallpaper");
        NavItemNotification.Content = AppUtils.Tr("navigation.notification");
        NavItemTimetable.Content = AppUtils.Tr("navigation.timetable");
        NavItemDownload.Content = AppUtils.Tr("navigation.download");
        NavItemAbout.Content = AppUtils.Tr("navigation.about");
        NavItemDebug.Content = AppUtils.Tr("navigation.debug");
        Log.Debug("翻译就绪");
    }

    private void InitNavigation()
    {
        HomeView = new HomeView(this);
        WallpaperView = new WallpaperView(this);
        NotificationView = new NotificationView(this);
        TimetableView = new TimetableView(this);
        DownloadView = new DownloadView();
        AboutView = new AboutView();
        DebugView = new DebugView();

        _navFrame = new FAFrame { IsNavigationStackEnabled = false, NavigationPageFactory = PassThroughNavigationPageFactory.Instance };
        _navFrame.NavigationFailed += (_, e) =>
            Log.Error($"[导航] NavigateFailed 目标={e.SourcePageType?.Name} 异常={e.Exception.GetType().Name}: {e.Exception.Message}\n{e.Exception.StackTrace}");
        Nav.Content = _navFrame;
        _navFrame.Loaded += (_, _) => _navFrame.NavigateFromObject(HomeView, new FAFrameNavigationOptions());
    }

    private Control PageFor(string tag) => tag switch
    {
        "wallpaper" => WallpaperView,
        "notification" => NotificationView,
        "timetable" => TimetableView,
        "download" => DownloadView,
        "about" => AboutView,
        "debug" => DebugView,
        _ => HomeView,
    };

    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is FluentAvalonia.UI.Controls.FANavigationViewItem item && item.Tag is string tag && _navFrame is { IsLoaded: true })
        {
            var page = PageFor(tag);
            if (ReferenceEquals(_navFrame.Content, page))
            {
                Log.Debug($"[导航] {tag} 已是当前页 跳过");
                return;
            }
            Log.Info($"[导航] {_navFrame.Content?.GetType().Name ?? "null"} -> {tag}");
            // 剥离输入手势: 点击在 PointerReleased 管线内同步选中导航,
            // 若在事件内同步拆旧页(关于页 markdown 子树巨大)会卡死渲染导致画面滞留
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var ok = _navFrame.NavigateFromObject(page, new FAFrameNavigationOptions());
                    Log.Debug($"[导航] NavigateFromObject({tag}) -> {ok}");
                }
                catch (Exception ex)
                {
                    Log.Error($"[导航] {tag} 导航失败 {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }
        else
        {
            var faItem = e.SelectedItem as FluentAvalonia.UI.Controls.FANavigationViewItem;
            Log.Debug($"[导航] 选择变更未处理 item={e.SelectedItem?.GetType().Name ?? "null"} tag={faItem?.Tag} frameLoaded={_navFrame?.IsLoaded}");
        }
    }

    public void SwitchToHome()
    {
        Nav.SelectedItem = NavItemHome;
    }

    // --navtest 导航自测 验证关于页切出后页面不刷新问题
    public async Task RunNavTestAsync()
    {
        try
        {
            Avalonia.Controls.Presenters.ContentPresenter Presenter() => _navFrame!
                .GetVisualDescendants()
                .OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                .FirstOrDefault(p => p.Name == "ContentPresenter");

            void Dump(string step, long ms)
            {
                var p = Presenter();
                Log.Info($"[NavTest] {step} 耗时={ms}ms Content={_navFrame?.Content?.GetType().Name} " +
                         $"PresenterOpacity={p?.Opacity.ToString("F2") ?? "null"} Child={p?.Child?.GetType().Name ?? "null"}");
            }

            var sw = new System.Diagnostics.Stopwatch();

            await Task.Delay(1500);
            sw.Restart();
            Nav.SelectedItem = NavItemAbout;
            await Task.Delay(1800);
            Dump("进入关于页", sw.ElapsedMilliseconds);

            sw.Restart();
            Nav.SelectedItem = NavItemHome;
            await Task.Delay(1800);
            Dump("切主页第1次", sw.ElapsedMilliseconds);

            sw.Restart();
            Nav.SelectedItem = NavItemHome;
            await Task.Delay(1800);
            Dump("切主页第2次", sw.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            Log.Error($"[NavTest] 异常 {e}");
        }
        finally
        {
            App.Lifetime?.Shutdown();
        }
    }

    public void SwitchToDebug()
    {
        Nav.SelectedItem = NavItemDebug;
    }

    // 设置项策略: 即时应用的即时应用 必须重启的弹确认框

    private void InitSettingsWatch()
    {
        // 必须重启的项
        Config.Language.ValueChanged += _ => QueueRestartPrompt();
        Config.LogVerbosity.ValueChanged += _ => QueueRestartPrompt();
        Config.DisableLog.ValueChanged += _ => QueueRestartPrompt();
        Config.EnableGpuAcceleration.ValueChanged += _ => QueueRestartPrompt();
        Config.DpiScale.ValueChanged += _ => QueueRestartPrompt();

        // 立即应用
        Config.AutoStart.ValueChanged += _ => AppUtils.SyncAutostartCfg();
        void ReapplyLogPolicy()
        {
            Log.Configure(Config.DisableLog.Value, AppUtils.ToLogLevel(Config.LogVerbosity.Value),
                Config.LogMaxCount.Value, Config.LogMaxDays.Value);
        }
        Config.LogMaxCount.ValueChanged += _ => ReapplyLogPolicy();
        Config.LogMaxDays.ValueChanged += _ => ReapplyLogPolicy();
        Config.ThemeColor.ValueChanged += _ => Common.ApplyAccentColor(Common.ParseAccentColor(Config.ThemeColor.Value));

        _restartDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _restartDebounce.Tick += (_, _) =>
        {
            _restartDebounce.Stop();
            _ = PromptRestartAsync();
        };
    }

    /// <summary>短时间多次触发只弹一次</summary>
    private void QueueRestartPrompt()
    {
        _restartDebounce?.Stop();
        _restartDebounce?.Start();
    }

    private async Task PromptRestartAsync()
    {
        if (_restartPrompting || AppUtils.RestartPromptSuppressed)
        {
            return;
        }
        _restartPrompting = true;
        try
        {
            var owner = Common.ResolveActiveWindow(this);
            if (await Common.ConfirmRestartAsync(owner))
            {
                Log.Info("[设置] 用户确认立即重启以应用更改");
                AppUtils.RequestRestart();
            }
            else
            {
                Log.Debug("[设置] 用户选择稍后重启");
            }
        }
        catch (Exception e)
        {
            Log.Error($"[设置] 重启确认弹窗失败: {e.Message}");
        }
        finally
        {
            _restartPrompting = false;
        }
    }

    // 托盘

    private void InitTray()
    {
        try
        {
            var iconPath = Paths.GetResourcePath(Constants.AppIcon);
            var menu = new NativeMenu();

            var showItem = new NativeMenuItem { Header = AppUtils.Tr("tray.show_window") };
            showItem.Click += (_, _) => RestoreFromTray();
            menu.Add(showItem);

            var exitItem = new NativeMenuItem { Header = AppUtils.Tr("tray.exit") };
            exitItem.Click += (_, _) => ExitApplication();
            menu.Add(exitItem);

            _trayIcon = new TrayIcon
            {
                ToolTipText = Constants.AppName,
                Menu = menu,
                IsVisible = true,
            };
            if (File.Exists(iconPath))
            {
                _trayIcon.Icon = new WindowIcon(new Bitmap(iconPath));
            }
            _trayIcon.Clicked += (_, _) =>
            {
                if (WindowState == WindowState.Minimized || !IsVisible)
                {
                    RestoreFromTray();
                }
                else
                {
                    Log.Info("[托盘] 图标激活 窗口可见 隐藏到托盘");
                    Hide();
                }
            };
            TrayIcon.SetIcons(Avalonia.Application.Current!, new TrayIcons { _trayIcon });
            Log.Debug("系统托盘就绪");
        }
        catch (Exception e)
        {
            Log.Warning($"系统托盘初始化失败: {e.Message}");
        }
    }

    public void RestoreFromTray()
    {
        Log.Info("[托盘] 菜单动作: 显示主界面");
        Show();
        WindowState = WindowState.Maximized;
        Activate();
    }

    public void MinimizeToDesktop() => WindowState = WindowState.Minimized;

    private bool _exiting;

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }
        _exiting = true;
        Log.Info("[托盘] 菜单动作: 退出应用");
        HomeView.SaveComponentPositions();
        CrashHandler.MarkExit("exit");
        _trayIcon?.Dispose();
        AppUtils.ReleaseSingleInstance();
        (Avalonia.Application.Current?.ApplicationLifetime as IControlledApplicationLifetime)?.Shutdown();
    }

    // 空闲检测

    private void InitIdleDetection()
    {
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _idleTimer.Tick += CheckIdle;
        Config.AutoOpenOnIdle.ValueChanged += _ => UpdateIdleTimer();
        Config.IdleMinutes.ValueChanged += _ => UpdateIdleTimer();
        UpdateIdleTimer();
    }

    private void UpdateIdleTimer()
    {
        _idleTimer?.Stop();
        if (Config.AutoOpenOnIdle.Value)
        {
            _idleTimer?.Start();
            Log.Info($"[空闲检测] 已启用 阈值={Config.IdleMinutes.Value}分钟");
        }
        else
        {
            Log.Info("空闲检测已禁用");
        }
    }

    private async void CheckIdle(object? sender, EventArgs e)
    {
        if (!Config.AutoOpenOnIdle.Value)
        {
            _hasTriggeredAutoOpen = false;
            return;
        }
        if (IsVisible)
        {
            _hasTriggeredAutoOpen = false;
            return;
        }
        var idleMs = Native.GetIdleMilliseconds();
        if (idleMs < 0)
        {
            return;
        }
        var threshold = Config.IdleMinutes.Value * 60 * 1000;
        if (idleMs > threshold && !_hasTriggeredAutoOpen && !HomeView.WasPageOperationRecent(5000))
        {
            if (await IsMediaPlayingAsync())
            {
                return;
            }
            Log.Info($"空闲{Config.IdleMinutes.Value}分钟 自动打开界面");
            RestoreFromTray();
            _hasTriggeredAutoOpen = true;
        }
    }

    private static bool _mediaCheckErrLogged;

    // 会话在播放且进程属于浏览器/播放器名单
    private static readonly string[] MediaBrowserNames =
        { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore", "edge" };
    private static readonly string[] MediaPlayerNames =
        { "music", "vlc", "potplayer", "spotify", "netflix" };

    private static async Task<bool> IsMediaPlayingAsync()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }
            var manager = await Glimpseon.Core.Services.GsmTcSource.GetManagerAsync();
            if (manager is null)
            {
                return false;
            }
            foreach (var session in manager.GetSessions())
            {
                if (session.GetPlaybackInfo().PlaybackStatus
                    != Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    continue;
                }
                var appId = (session.SourceAppUserModelId ?? "").ToLowerInvariant();
                if (MediaBrowserNames.Any(b => appId.Contains(b)) || MediaPlayerNames.Any(p => appId.Contains(p)))
                {
                    Log.Debug($"媒体播放中 会话={appId}");
                    return true;
                }
            }
            return false;
        }
        catch (Exception e)
        {
            if (!_mediaCheckErrLogged)
            {
                Log.Warning($"检查媒体播放状态失败(仅记录一次): {e.Message}");
                _mediaCheckErrLogged = true;
            }
            return false;
        }
    }

    // NTP

    private void InitTimeSync()
    {
        _ntpTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _ntpTimer.Tick += (_, _) => SyncNtpBackground();
        _ntpTimer.Start();
        if (Config.UsePreciseTime.Value)
        {
            SyncNtpBackground();
        }
    }

    private void SyncNtpBackground()
    {
        if (!Config.UsePreciseTime.Value)
        {
            return;
        }
        Task.Run(() =>
        {
            try
            {
                if (AppUtils.SyncNtp(Config.TimeServer.Value) && AppUtils.LastSyncTime is { } t)
                {
                    Config.LastSyncTime.Value = t.ToString("HH:mm:ss");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[NTP] 后台同步失败: {e.Message}");
            }
        });
    }

    // 主题

    private void InitThemeMonitor()
    {
        _themeCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _themeCheckTimer.Tick += CheckSystemTheme;
        if (Config.ThemeMode.Value == ThemeMode.Auto)
        {
            _themeCheckTimer.Start();
            Log.Debug("主题检测定时器启动 跟随系统");
        }
        Config.ThemeMode.ValueChanged += mode =>
        {
            if (mode == ThemeMode.Auto)
            {
                _themeCheckTimer?.Start();
            }
            else
            {
                _themeCheckTimer?.Stop();
                ApplyThemeVariant(mode);
            }
        };
        ApplyThemeVariant(Config.ThemeMode.Value);
    }

    private void ApplyThemeVariant(ThemeMode mode)
    {
        if (Avalonia.Application.Current is not { } app)
        {
            return;
        }
        app.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => Avalonia.Styling.ThemeVariant.Light,
            ThemeMode.Dark => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
    }

    private void CheckSystemTheme(object? sender, EventArgs e)
    {
        if (Config.ThemeMode.Value == ThemeMode.Auto)
        {
            OnThemeChanged();
        }
    }

    private void OnThemeChanged()
    {
        // 页面主题刷新入口
    }

    private void OnDebugModeChanged(bool value)
    {
        Log.Debug($"调试模式变更: {value}");
        NavItemDebug.IsVisible = value;
        if (!value)
        {
            SwitchToHome();
        }
    }

    // 窗口行为

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F12 && Config.DebugMode.Value)
        {
            Log.Debug("F12: 切调试面板");
            SwitchToDebug();
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.E)
        {
            Log.Debug("Ctrl+E: 进入组件编辑模式");
            HomeView.EnterEditMode();
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        HomeView.SaveComponentPositions();

        if (Config.DebugMode.Value)
        {
            Log.Info("[closeEvent] 退出应用");
            ExitApplication();
            base.OnClosing(e);
            return;
        }

        if (Config.CloseAction.Value == "minimize")
        {
            if (Native.IsSystemShuttingDown())
            {
                Log.Info("[closeEvent] 系统关机 放行关闭");
                CrashHandler.MarkExit("shutdown");
                AppUtils.ReleaseSingleInstance();
                base.OnClosing(e);
                return;
            }
            Log.Info($"[closeEvent] 最小化到托盘 计数 {Config.MinimizeNotificationCount.Value}/5");
            e.Cancel = true;
            Hide();
            if (Config.MinimizeNotificationCount.Value < 5)
            {
                Config.MinimizeNotificationCount.Value++;
            }
            return;
        }

        Log.Info("[closeEvent] closeAction=close 退出应用");
        CrashHandler.MarkExit("close");
        AppUtils.ReleaseSingleInstance();
        base.OnClosing(e);
    }

    public void OpenSettingsWindow()
    {
        var settings = new SettingsWindow();
        settings.Show(this);
    }
}
