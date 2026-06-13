using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ClassLively_UI.Helpers;
using ClassLively_UI.Windows;

namespace ClassLively_UI;

public partial class App : Application
{
    private Window? _window;
    private SplashScreen? _splash;

    private const string MutexName = "ClassLively_SingleInstance_Mutex_{A7F3E2D1-8B4C-4F6A-9D0E-1C2B3A4F5E6D}";
    private static Mutex? _appMutex;
    private static bool _isSingleInstanceOwner;

    public static Window? MainWindow => (Application.Current as App)?._window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            if (!CheckSingleInstance()) return;

            if (SetupWizard.IsWizardNeeded())
            {
                var wizardTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var wizard = new SetupWizard();
                wizard.Closed += (s, e) => wizardTcs.TrySetResult(true);
                wizard.Activate();
                await wizardTcs.Task;
            }

            _ = CheckBackendOnlineAsync();

            _splash = new SplashScreen();
            _splash.Activate();
            _splash.SetProgress(0, "正在初始化...");

            await InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LAUNCH CRASH] {ex}");
            try { File.AppendAllText("crash_log.txt", $"{DateTime.Now} {ex}\n\n"); } catch { }
        }
    }

    private async Task InitializeAsync()
    {
        if (_splash == null) return;

        _splash.SetProgress(10, "正在清理临时文件...");
        await Task.Delay(100);

        _splash.SetProgress(15, "正在加载配置...");
        await Task.Delay(200);

        _splash.SetProgress(40, "正在配置日志...");
        await Task.Delay(400);

        _splash.SetProgress(70, "正在创建主窗口...");
        await Task.Delay(100);

        _window = new MainWindow();
        _window.Closed += OnWindowClosed;

        _splash.SetProgress(90, "即将完成...");
        await Task.Delay(300);

        _splash.Complete();
        _splash = null;
        _window?.Activate();
    }

    // ── 后端检测 ──

    private static async Task CheckBackendOnlineAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await client.GetAsync("http://127.0.0.1:19856/api/health");
            if (resp.IsSuccessStatusCode)
            {
                Debug.WriteLine("[App] python在线");
                AppSettings.Set("backend_online", true);
                return;
            }
        }
        catch { }

        Debug.WriteLine("[App] python离线");
        AppSettings.Set("backend_online", false);
    }

    // ── 单例 ──

    private bool CheckSingleInstance()
    {
        try
        {
            _appMutex = new Mutex(true, MutexName, out _isSingleInstanceOwner);
            if (_isSingleInstanceOwner) return true;

            try
            {
                if (NativeBindings.acquire_mutex(MutexName))
                {
                    _isSingleInstanceOwner = true;
                    return true;
                }
            }
            catch { }

            ShowInstanceRunningDialog();
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] 单例检查异常: {ex.Message}");
            return true;
        }
    }

    private static void ShowInstanceRunningDialog()
    {
        Debug.WriteLine("[App] 已有实例运行");
        try { File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] 已有实例运行\n"); } catch { }
    }

    public static void ReleaseSingleton()
    {
        if (_isSingleInstanceOwner)
        {
            try { NativeBindings.release_mutex(); } catch { }
            _isSingleInstanceOwner = false;
        }
        _appMutex?.ReleaseMutex();
        _appMutex?.Dispose();
        _appMutex = null;
    }

    // ── 事件处理 ──

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var log = $"[CRASH] {DateTime.Now}\n{e.Exception}\n{e.Exception.InnerException}\n";
        Debug.WriteLine(log);
        try { File.AppendAllText("crash_log.txt", log + "\n\n"); } catch { }
        e.Handled = true;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        ReleaseSingleton();
        TrayIconHelper.Remove();
    }
}
