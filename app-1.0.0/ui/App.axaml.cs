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
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Glimpseon.UI;

public partial class App : Application
{
    public static IClassicDesktopStyleApplicationLifetime? Lifetime { get; private set; }

    public static bool CrashDialogMode { get; set; }
    public static string CrashDialogTitleText { get; set; } = "Glimpseon 意外崩溃";
    public static string CrashDialogSummary { get; set; } = "";
    public static string CrashDialogDetails { get; set; } = "";
    public static string CrashDialogAction { get; set; } = "";
    public static CrashWindowKind CrashDialogKind { get; set; } = CrashWindowKind.Handler;

    private static bool _crashDialogOpen;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            Lifetime = lifetime;
            if (CrashDialogMode)
            {
                lifetime.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var window = new CrashWindow();
                lifetime.MainWindow = window;
                window.Show();
                base.OnFrameworkInitializationCompleted();
                return;
            }
            lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += async (_, e) =>
            {
                var ex = e.Exception;
                Glimpseon.Core.Log.Critical($"[ui线程] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                e.Handled = true;
                await ShowCrashDialogAsync(ex);
            };
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = GlimpseonMain.RunStartupAsync((ClassicDesktopStyleApplicationLifetime)lifetime));
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowCrashDialogAsync(Exception ex)
    {
        if (_crashDialogOpen)
        {
            return;
        }
        var owner = Lifetime?.MainWindow;
        if (owner is null)
        {
            return;
        }
        _crashDialogOpen = true;
        try
        {
            var details = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            var dialog = new CrashWindow(
                CrashWindowKind.InApp,
                "Glimpseon 遇到错误",
                "发生严重错误 应用仍在运行 可忽略继续 或退出重启",
                details);
            await dialog.ShowDialog(owner);
        }
        catch
        {
            // 弹窗失败只保留日志
        }
        finally
        {
            _crashDialogOpen = false;
        }
    }
}
