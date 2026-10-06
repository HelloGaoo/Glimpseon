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

// 崩溃报告窗口: 接管进程崩溃弹窗 主进程内异常对话框 主进程未响应提示
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Glimpseon.Core;

namespace Glimpseon.UI;

// 弹窗形态: 按钮行为随形态分流
public enum CrashWindowKind
{
    // 接管进程: 主进程已崩溃退出
    Handler,
    // 主进程内: 严重异常但进程存活 可忽略继续
    InApp,
    // 接管进程: 主进程长时间未响应 动作作用于主进程
    Hung,
}

public partial class CrashWindow : GlimpseonWindow
{
    private readonly CrashWindowKind _kind;

    // 接管进程弹窗模式: 文案与形态来自 App 静态属性
    public CrashWindow()
        : this(App.CrashDialogKind, App.CrashDialogTitleText, App.CrashDialogSummary, App.CrashDialogDetails)
    {
    }

    public CrashWindow(CrashWindowKind kind, string titleText, string summary, string details)
    {
        _kind = kind;
        EnableMicaWindow = false;
        InitializeComponent();
        // 显示标准标题栏 默认扩展式标题栏不渲染标题文本
        try
        {
            TitleBar.ExtendsContentIntoTitleBar = false;
        }
        catch (Exception e)
        {
            Log.Debug($"[UI] 崩溃窗口标题栏: {e.Message}");
        }
        // 崩溃窗口不走翻译 直接显示中文
        Title = "Glimpseon 崩溃报告";
        TitleText.Text = titleText;
        SummaryText.Text = summary;
        IdText.Text = $"TID {Telemetry.TelemetryId}";
        DetailsBox.Text = details;
        // 日志定位到末行 最新内容直接可见
        Opened += (_, _) =>
        {
            DetailsBox.CaretIndex = DetailsBox.Text?.Length ?? 0;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var scroll = DetailsBox.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().FirstOrDefault();
                scroll?.ScrollToEnd();
            }, Avalonia.Threading.DispatcherPriority.Background);
        };
        try
        {
            var iconPath = Paths.GetResourcePath(Constants.AppIcon);
            if (File.Exists(iconPath))
            {
                Icon = new Bitmap(iconPath);
            }
        }
        catch
        {
            // 图标缺失留空
        }
        CopyBtn.Click += async (_, _) =>
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.Clipboard is not null)
                {
                    var transfer = new Avalonia.Input.DataTransfer();
                    transfer.Add(Avalonia.Input.DataTransferItem.CreateText(DetailsBox.Text ?? ""));
                    await topLevel.Clipboard.SetDataAsync(transfer);
                    CopyText.Text = "已复制";
                }
            }
            catch
            {
                // 剪贴板失败忽略
            }
        };
        IgnoreBtn.Click += (_, _) => Close();
        ExitBtn.Click += (_, _) => ExitApp();
        RestartBtn.Click += (_, _) => RestartApp();
    }

    private void ExitApp()
    {
        switch (_kind)
        {
            case CrashWindowKind.Handler:
                // 接管进程: 主进程已死 直接退出接管进程
                Environment.Exit(0);
                break;
            case CrashWindowKind.InApp:
                // 主进程内: 有序退出 不触发崩溃判定
                CrashHandler.MarkExit("in_app");
                AppUtils.ReleaseSingleInstance();
                (App.Lifetime as IControlledApplicationLifetime)?.Shutdown();
                break;
            case CrashWindowKind.Hung:
                // 未响应: 动作交回接管进程作用于主进程
                App.CrashDialogAction = "exit";
                Close();
                break;
        }
    }

    private void RestartApp()
    {
        switch (_kind)
        {
            case CrashWindowKind.Handler:
                LaunchSelf();
                Close();
                break;
            case CrashWindowKind.InApp:
                // 走请求重启链路 有序退出后由 Main 尾部拉起
                AppUtils.RequestRestart();
                break;
            case CrashWindowKind.Hung:
                App.CrashDialogAction = "restart";
                Close();
                break;
        }
    }

    private static void LaunchSelf()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
        }
        catch
        {
            // 重启失败忽略
        }
    }
}
