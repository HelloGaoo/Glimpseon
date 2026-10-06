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

// 崩溃接管进程
using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Glimpseon.Core;
using Glimpseon.Core.Win32;
using Glimpseon.UI;

namespace Glimpseon;

public static class CrashHandler
{
    private static readonly object ExitLock = new();
    private static bool _exitMarked;
    private static bool _terminatedByUser;

    private const int HungPollIntervalMs = 2000;
    private const int HungThresholdTicks = 15;

    private static string ExitSignalPath => Path.Combine(Paths.DataTemp, $"telemetry_exit_{Environment.ProcessId}.json");

    private static string LogPointerPath => Path.Combine(Paths.DataTemp, "main_log_path.txt");

    // -- 主进程侧 --

    public static void Init()
    {
        try
        {
            File.Delete(LogPointerPath);
        }
        catch
        {
            // 指针清理失败忽略
        }
        try
        {
            var cutoff = DateTime.Now - TimeSpan.FromHours(24);
            foreach (var f in Directory.EnumerateFiles(Paths.DataTemp, "telemetry_exit_*.json"))
            {
                if (File.GetLastWriteTime(f) < cutoff)
                {
                    File.Delete(f);
                }
            }
        }
        catch
        {
            // 信号清理失败忽略
        }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => MarkExit("process_exit");
    }

    public static void WriteLogPointer()
    {
        try
        {
            if (Log.CurrentLogFilePath is not null)
            {
                Directory.CreateDirectory(Paths.DataTemp);
                File.WriteAllText(LogPointerPath, Log.CurrentLogFilePath);
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[接管] 日志指针写入失败: {e.Message}");
        }
    }

    // 写退出信号 幂等
    public static void MarkExit(string source)
    {
        lock (ExitLock)
        {
            if (_exitMarked)
            {
                return;
            }
            _exitMarked = true;
        }
        try
        {
            Directory.CreateDirectory(Paths.DataTemp);
            var obj = new JsonObject
            {
                ["source"] = source,
                ["time"] = DateTime.Now.ToString("o"),
            };
            File.WriteAllText(ExitSignalPath, obj.ToJsonString());
        }
        catch (Exception e)
        {
            Log.Debug($"[接管] 写退出信号失败: {e.Message}");
        }
    }

    public static void Spawn()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                return;
            }
            Process.Start(new ProcessStartInfo(exe, $"--crash-handler {Environment.ProcessId}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch (Exception e)
        {
            Log.Debug($"[接管] 崩溃接管进程启动失败: {e.Message}");
        }
    }

    // -- 接管进程侧 --

    public static int Run(string pidArg)
    {
        try
        {
            Telemetry.PrepareCrashHandler();
            AppUtils.InitTranslation();
            var watchedPid = int.TryParse(pidArg, out var pid) ? pid : 0;
            var logPath = ReadLogPointer() ?? ResolveNewestLog();
            try
            {
                File.Delete(LogPointerPath);
            }
            catch
            {
                // 指针清理失败忽略
            }
            Log.Configure(false, LogLevel.Info, Config.LogMaxCount.Value, Config.LogMaxDays.Value, logPath);
            Log.Info($"[接管] 启动 等待主进程 pid={pidArg} 主日志={logPath ?? "无"}");
            var exitCode = -1;
            try
            {
                using var main = Process.GetProcessById(watchedPid);
                // 轮询等待退出 期间检测主进程窗口未响应 连续30秒弹窗确认
                var hungTicks = 0;
                while (!main.WaitForExit(HungPollIntervalMs))
                {
                    main.Refresh();
                    var handle = main.MainWindowHandle;
                    if (handle != IntPtr.Zero && Native.IsWindowHung(handle))
                    {
                        hungTicks++;
                        if (hungTicks >= HungThresholdTicks)
                        {
                            hungTicks = 0;
                            Log.Warning("[接管] 主进程未响应超过30s 弹窗确认");
                            var tail = logPath is null ? "（未能读取日志）" : Log.ReadTail(logPath);
                            var action = RunCrashDialog(
                                CrashWindowKind.Hung,
                                "Glimpseon 未响应",
                                "Glimpseon 主程序长时间没有响应 可能已卡死",
                                tail);
                            if (action == "exit")
                            {
                                _terminatedByUser = true;
                                TryKillMain(main);
                            }
                            else if (action == "restart")
                            {
                                _terminatedByUser = true;
                                TryKillMain(main);
                                AppUtils.RestartSelf();
                                return 0;
                            }
                        }
                    }
                    else
                    {
                        hungTicks = 0;
                    }
                }
                exitCode = main.ExitCode;
            }
            catch
            {
                // 主进程已退出或无法附加 按崩溃处理
            }
            Log.Info($"[接管] 主进程已退出 code={exitCode}");
            var (crashed, source) = ReadExitSignal(watchedPid);
            Log.Info($"[接管] 判定 crashed={crashed} source={source}");
            if (crashed)
            {
                if (Native.IsSystemShuttingDown())
                {
                    Log.Info("[接管] 系统正在关机 跳过");
                    return 0;
                }
                Thread.Sleep(3000);
                if (Native.IsSystemShuttingDown())
                {
                    Log.Info("[接管] 系统正在关机 跳过");
                    return 0;
                }
            }
            var uploaded = false;
            if (logPath is not null)
            {
                uploaded = Telemetry.UploadLogZip(logPath, crashed ? "app_crash_log" : "app_exit_log", crashed ? null : source);
            }
            Log.Info($"[接管] 日志上传={uploaded}");
            try
            {
                Telemetry.SendExitEventAsync(crashed, source).Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // 上报失败忽略
            }
            if (crashed && !_terminatedByUser)
            {
                // 上传耗时内关机流程可能推进 弹窗前终检
                if (Native.IsSystemShuttingDown())
                {
                    Log.Info("[接管] 系统正在关机 跳过弹窗");
                    return 0;
                }
                var tail = logPath is null ? "（未能读取日志）" : Log.ReadTail(logPath);
                var details = $"退出代码: {(exitCode == -1 ? "未知" : exitCode.ToString())}\n\n{tail}";
                Log.Info("[接管] 显示崩溃弹窗");
                RunCrashDialog(
                    CrashWindowKind.Handler,
                    "Glimpseon 意外崩溃",
                    uploaded ? "Glimpseon 意外退出 日志已自动上传" : "Glimpseon 意外退出",
                    details);
                Log.Info("[接管] 崩溃弹窗已关闭");
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[接管] 接管进程失败: {e.Message}");
        }
        return 0;
    }

    private static (bool Crashed, string Source) ReadExitSignal(int watchedPid)
    {
        var path = Path.Combine(Paths.DataTemp, $"telemetry_exit_{watchedPid}.json");
        try
        {
            if (File.Exists(path))
            {
                var node = JsonNode.Parse(File.ReadAllText(path));
                var source = node?["source"]?.GetValue<string>() ?? "unknown";
                File.Delete(path);
                return (false, source);
            }
        }
        catch
        {
            // 信号损坏按崩溃处理
        }
        return (true, "unknown");
    }

    private static string RunCrashDialog(CrashWindowKind kind, string titleText, string summary, string details)
    {
        try
        {
            App.CrashDialogMode = true;
            App.CrashDialogKind = kind;
            App.CrashDialogTitleText = titleText;
            App.CrashDialogSummary = summary;
            App.CrashDialogDetails = details;
            App.CrashDialogAction = "";
            GlimpseonMain.BuildAvaloniaApp(Config.EnableGpuAcceleration.Value).StartWithClassicDesktopLifetime(Array.Empty<string>());
        }
        catch (Exception e)
        {
            Log.Warning($"[接管] Avalonia 崩溃弹窗失败: {e.Message}");
            try
            {
                Native.ShowMessageBox(details, summary);
            }
            catch
            {
                // 彻底失败忽略
            }
        }
        return App.CrashDialogAction;
    }

    private static void TryKillMain(Process main)
    {
        try
        {
            main.Kill(true);
            Log.Info("[接管] 已结束主进程");
        }
        catch (Exception e)
        {
            Log.Warning($"[接管] 结束主进程失败: {e.Message}");
        }
    }

    private static string? ReadLogPointer()
    {
        try
        {
            var path = File.ReadAllText(LogPointerPath).Trim();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveNewestLog()
    {
        try
        {
            Directory.CreateDirectory(Paths.DataLog);
            return new DirectoryInfo(Paths.DataLog).EnumerateFiles("app_*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }
}
