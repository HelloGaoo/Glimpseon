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

// 主程序
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System.Text.Json;
using Glimpseon.Core;
using Glimpseon.Core.Services;
using Glimpseon.Core.Win32;
using Glimpseon.UI;

namespace Glimpseon;

internal static class GlimpseonMain
{
    [STAThread]
    public static int Main(string[] args)
    {
        Paths.EnsureDataDirs();
        Config.Load();
        var ret = BuildAvaloniaApp(Config.EnableGpuAcceleration.Value).StartWithClassicDesktopLifetime(args);
        if (AppUtils.IsRestartPending())
        {
            Log.Info($"[重启] 待重启标记 exit_code={ret}");
            AppUtils.RestartSelf();
        }
        return ret;
    }

    public static AppBuilder BuildAvaloniaApp(bool gpuEnabled = true) => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new Win32PlatformOptions
        {
            RenderingMode = gpuEnabled
                ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
                : [Win32RenderingMode.Software],
        })
        .LogToTrace();

    public static async Task RunStartupAsync(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        try
        {
            var bootT0 = DateTime.Now;

            Paths.EnsureDataDirs();
            Log.InitExceptionHooks();

            // 崩溃退出释放互斥体
            AppDomain.CurrentDomain.ProcessExit += (_, _) => AppUtils.ReleaseSingleInstance();
            // 退出时清理媒体源
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    MediaServices.Close();
                }
                catch (Exception e)
                {
                    Log.Debug($"媒体源退出清理失败: {e.Message}");
                }
            };

            Log.Info($"APP_DIR={Paths.AppDir} 图标={Constants.AppIcon}");
            Log.Info($"版本号 {Paths.Version} 构建日期 {Paths.BuildDate}");
            Log.Info($"系统版本 {Environment.OSVersion.VersionString} 运行时 {Environment.Version}");
            Log.Info($"软件运行路径 {Paths.PackageRoot}");

            var logMaxCount = Config.DebugMode.Value ? 3 : Config.LogMaxCount.Value;
            var logMaxDays = Config.DebugMode.Value ? 1 : Config.LogMaxDays.Value;
            Log.Configure(Config.DisableLog.Value, AppUtils.ToLogLevel(Config.LogVerbosity.Value), logMaxCount, logMaxDays);

            AppUtils.InitTranslation();
            AppUtils.ApplyLanguageFromConfig();

            Common.ApplyAccentColor(Common.ParseAccentColor(Config.ThemeColor.Value));

            if (Config.DebugMode.Value)
            {
                var failures = Config.SelfCheckAll();
                if (failures.Count == 0)
                {
                    Log.Info($"[配置] {Config.ItemCount} 项 序列化正常");
                }
                else
                {
                    Log.Error($"[配置] {failures.Count} 项异常:\n  " + string.Join("\n  ", failures));
                }
            }

        // 单实例检查
        if (!AppUtils.VerifySingleInstance())
        {
            Log.Warning($"{Constants.AppName} 已运行 本实例退出");
            var host = new Window
            {
                WindowDecorations = WindowDecorations.None,
                WindowState = WindowState.Maximized,
                ShowInTaskbar = false,
                CanResize = false,
                Topmost = true,
                Background = Avalonia.Media.Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            };
            host.Show();
            await Common.Alert(
                host,
                AppUtils.Tr("dialog.instance_running", ("app", Constants.AppName)),
                AppUtils.Tr("dialog.instance_running_detail", ("app", Constants.AppName)),
                AppUtils.Tr("common.cancel"));
            host.Close();
            lifetime.Shutdown();
            return;
        }
        Log.Info("单实例可启动");
        Log.Debug($"自启动启动参数检测: {AppUtils.AutoStartLaunch()}");

        // 向导
        if (WizardWindow.IsNeeded())
        {
            Log.Info("首次运行 打开设置向导");
            var wizard = new WizardWindow();
            wizard.Show();
            await WaitForWindowCloseAsync(wizard);
        }

        // Splash
        var splash = new SplashWindow(Paths.Version, Paths.GetResourcePath(Constants.AppIcon));
        splash.Show();
        splash.SetProgress(0);

        splash.UpdateStatus(AppUtils.Tr("splash.loading_translation"));
        splash.SetProgress(15);

        splash.UpdateStatus(AppUtils.Tr("splash.initializing_fonts"));
        splash.SetProgress(30);
        AppUtils.InitializeFonts();
        await Task.Delay(60);

        splash.UpdateStatus(AppUtils.Tr("splash.cleaning_temp"));
        splash.SetProgress(40);
        _ = Task.Run(() => Downloader.CleanupTempDirectory());
        await Task.Delay(60);

        splash.UpdateStatus(AppUtils.Tr("splash.configuring_log"));
        splash.SetProgress(45);
        await Task.Delay(60);

        splash.UpdateStatus(AppUtils.Tr("splash.loading_config"));
        splash.SetProgress(55);
        await Task.Delay(60);

        LogConfigSummary();

        // 主窗口
        splash.UpdateStatus(AppUtils.Tr("splash.creating_main_window"));
        splash.SetProgress(70);
        var window = new MainWindow();
        Log.Info("创建主窗口 完成");

        // 预加载
        splash.UpdateStatus(AppUtils.Tr("splash.preloading"));
        splash.SetProgress(75);
        var preloadTask = PreloadWallpaperAsync(window);
        RegisterCacheRefreshers(window);
        AppUtils.CacheRefresher.Start();
        StorageMonitor.Start();
        ResourceMonitor.Start();
        var finished = await Task.WhenAny(preloadTask, Task.Delay(600));
        if (finished != preloadTask)
        {
            Log.Warning($"预加载超时0.6s 转后台");
        }

        splash.SetProgress(95);
        await Task.Delay(60);
        splash.SetProgress(100);
        await Task.Delay(100);
        splash.Close();
        Log.Info($"总启动耗时{(DateTime.Now - bootT0).TotalSeconds:F2}s");

        if (Config.AutoCheckUpdate.Value)
        {
            window.AboutView.CheckUpdateAuto();
        }

        // --navtest 导航自测
        if (Environment.GetCommandLineArgs().Contains("--navtest"))
        {
            _ = window.RunNavTestAsync();
        }

        window.Show();
        window.WindowState = WindowState.Maximized;
        window.SwitchToHome();

        AppUtils.SyncAutostartCfg();

        lifetime.ShutdownMode = ShutdownMode.OnMainWindowClose;
        lifetime.MainWindow = window;
        }
        catch (Exception e)
        {
            Log.Critical($"[启动异常] {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            Native.ShowMessageBox($"{e.GetType().Name}: {e.Message}\n\n详见 data/log/ 日志", "Glimpseon 启动失败");
            lifetime.Shutdown(1);
        }
    }

    private static void LogConfigSummary()
    {
        Log.Info($"主窗口 主题={Config.ThemeMode.Value} 颜色={Config.ThemeColor.Value} DPI={Config.DpiScale.Value} 语言={Config.Language.Value}");
        Log.Info($"日志配置 禁用={Config.DisableLog.Value} 级别={Config.LogVerbosity.Value} 条目={Config.LogMaxCount.Value} 保留={Config.LogMaxDays.Value}");
        Log.Info($"其他 关闭={Config.CloseAction.Value} 多实例={Config.AllowMultipleInstances.Value} 调试={Config.DebugMode.Value} 自启={Config.AutoStart.Value}");
        Log.Info($"下载配置 下载源={Config.DownloadSource.Value}");
        Log.Info($"壁纸配置 保存={Config.WallpaperSaveLimit.Value} 间隔={Config.AutoGetInterval.Value} 同步桌面={Config.AutoSyncToDesktop.Value} api={Config.WallpaperApi.Value}");
        Log.Info($"外观配置 背景模糊半径={Config.BackgroundBlurRadius.Value}");
        Log.Info($"时间配置 显示秒={Config.ShowClockSeconds.Value} 显示农历={Config.ShowLunarCalendar.Value}");
        Log.Info($"天气配置 城市={Config.City.Value} 间隔={Config.WeatherUpdateInterval.Value}");
        Log.Info($"学校信息配置 启用={Config.ShowSchoolInfo.Value} 学校={Config.School.Value} 班级={Config.SchoolClass.Value}");
        Log.Info($"自动配置 空闲开={Config.AutoOpenOnIdle.Value} 空闲={Config.IdleMinutes.Value} 检查更新={Config.AutoCheckUpdate.Value}");
    }

    private static async Task PreloadWallpaperAsync(MainWindow window)
    {
        try
        {
            var cityName = Config.City.Value;
            if (!string.IsNullOrWhiteSpace(cityName))
            {
                var (lon, lat) = new RegionDatabase().GetCoordinates(cityName);
                if (lon is not null && lat is not null
                    && (Math.Abs(Config.Longitude.Value - lon.Value) > 1e-6 || Math.Abs(Config.Latitude.Value - lat.Value) > 1e-6))
                {
                    Config.Longitude.Value = lon.Value;
                    Config.Latitude.Value = lat.Value;
                    Log.Info($"[PRELOAD] 城市>坐标回填 {cityName} ({lon:F4}, {lat:F4})");
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[PRELOAD] 城市>坐标回填失败: {e.Message}");
        }

        await Task.Run(async () =>
        {
            try
            {
                var path = WallpaperService.LoadCachePath();
                if (path is not null)
                {
                    Log.Info($"[PRELOAD] 壁纸预取 缓存 {path}");
                    await Dispatcher.UIThread.InvokeAsync(() => ApplyWallpaperPreload(window, path));
                    return;
                }

                // 缓存缺失 壁纸
                var defaultWallpaper = Paths.GetResourcePath(Constants.ResourceDefaultWallpaper);
                if (File.Exists(defaultWallpaper))
                {
                    Log.Info($"[PRELOAD] 壁纸预取 默认 {defaultWallpaper}");
                    await Dispatcher.UIThread.InvokeAsync(() => ApplyWallpaperPreload(window, defaultWallpaper));
                    return;
                }

                // 默认壁纸缺失回退历史壁纸
                var history = Directory.EnumerateFiles(Paths.WallpaperDir, "wallpaper_*.jpg")
                    .Select(f => (Path: f, Mtime: File.GetLastWriteTime(f)))
                    .OrderByDescending(x => x.Mtime)
                    .FirstOrDefault();
                if (history.Path is not null)
                {
                    Log.Info($"[PRELOAD] 壁纸预取 历史 {history.Path}");
                    await Dispatcher.UIThread.InvokeAsync(() => ApplyWallpaperPreload(window, history.Path));
                    return;
                }

                Log.Warning("[PRELOAD] 壁纸预取失败");
            }
            catch (Exception e)
            {
                Log.Error($"[PRELOAD] {e.Message}");
            }
        });
    }

    private static void ApplyWallpaperPreload(MainWindow window, string path)
    {
        window.HomeView.SetWallpaper(path);
        window.WallpaperView.NotifyWallpaperFetched(path, AppUtils.Tr("wallpaper.source_cache"));
    }

    // 缓存
    private static void RegisterCacheRefreshers(MainWindow window)
    {
        // 天气
        AppUtils.CacheRefresher.Register(new AppUtils.CacheRefresher.Entry
        {
            Name = "weather",
            Interval = () => Config.WeatherUpdateInterval.Value,
            Enabled = () => Config.ShowWeather.Value,
            Fetch = async () =>
            {
                var data = await WeatherService.FetchAllAsync();
                return data;
            },
        });
        // 一言
        AppUtils.CacheRefresher.Register(new AppUtils.CacheRefresher.Entry
        {
            Name = "poetry",
            Interval = () => Config.PoetryUpdateInterval.Value,
            Enabled = () => Config.ShowPoetry.Value,
            Fetch = async () =>
            {
                var text = await PoetryService.GetPoetryAsync();
                return text is null ? null : JsonSerializer.SerializeToElement(text);
            },
        });
        // 壁纸
        AppUtils.CacheRefresher.Register(new AppUtils.CacheRefresher.Entry
        {
            Name = "wallpaper",
            Interval = () => Config.AutoGetInterval.Value,
            AutoEnabled = () => Config.AutoGetInterval.Value != "never",
            SkipSave = true,
            Fetch = async () =>
            {
                var path = await WallpaperService.FetchAsync();
                if (path is null)
                {
                    return null;
                }
                return AppUtils.GetCachedContent("wallpaper", ignoreExpiry: true);
            },
            Apply = data =>
            {
                if (data is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("path", out var p) && p.GetString() is { } path)
                {
                    var source = root.TryGetProperty("source", out var sEl) ? sEl.GetString() ?? "" : "";
                    window.HomeView.SetWallpaper(path);
                    window.WallpaperView.NotifyWallpaperFetched(path, source);
                    if (Config.AutoSyncToDesktop.Value)
                    {
                        _ = Task.Run(() => WallpaperService.SetDesktop(path));
                    }
                }
            },
        });
        // 配置变更
        Config.City.ValueChanged += _ => AppUtils.CacheRefresher.Invalidate("weather");
        Config.WeatherUpdateInterval.ValueChanged += _ => AppUtils.CacheRefresher.Invalidate("weather");
        Config.ShowWeather.ValueChanged += v => { if (v) { AppUtils.CacheRefresher.Invalidate("weather"); } };
        Config.PoetryApiUrl.ValueChanged += _ => AppUtils.CacheRefresher.Invalidate("poetry");
        Config.WallpaperApi.ValueChanged += _ => AppUtils.CacheRefresher.Invalidate("wallpaper");
        Config.AutoGetInterval.ValueChanged += _ => AppUtils.CacheRefresher.Invalidate("wallpaper");
    }

    private static async Task WaitForWindowCloseAsync(Window window)
    {
        var tcs = new TaskCompletionSource();
        window.Closed += (_, _) => tcs.TrySetResult();
        await tcs.Task;
    }
}
