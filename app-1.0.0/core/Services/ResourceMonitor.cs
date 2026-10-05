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

// 资源监控服务

using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Glimpseon.Core.Services;

public static class ResourceMonitor
{
    private const int SampleIntervalSeconds = 30; // 采样间隔
    private const int ConfirmSamples = 3;         // 连续超标次数确认
    private const int RepeatNotifyMinutes = 20;   // 提醒冷却间隔
    private const int NotifyDurationSeconds = 30; // 弹窗自动关闭时间

    private static PerformanceCounter? _cpu;
    private static bool _perfBroken;

    private static int _memOver;
    private static int _cpuOver;
    private static DateTime _memLastNotify = DateTime.MinValue;
    private static DateTime _cpuLastNotify = DateTime.MinValue;

    public static void Start()
    {
        _ = Task.Run(() =>
        {
            try
            {
                _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpu.NextValue();
            }
            catch (Exception e)
            {
                Log.Warning($"[资源] CPU 计数器不可用: {e.Message}");
                _perfBroken = true;
            }
        });

        // 后台循环采样
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(SampleIntervalSeconds));
            while (true)
            {
                try
                {
                    Sample();
                }
                catch (Exception e)
                {
                    Log.Warning($"[资源] 采样异常: {e.Message}");
                }
                await Task.Delay(TimeSpan.FromSeconds(SampleIntervalSeconds));
            }
        });
        Log.Info($"[资源] 监控已启动 采样{SampleIntervalSeconds}s 冷却{RepeatNotifyMinutes}分钟");
    }

    private static void Sample()
    {
        if (Config.ResourceMemoryNotify.Value)
        {
            var mem = MemoryUsagePercent();
            if (mem > 0)
            {
                Handle("memory", mem, Config.ResourceMemoryThreshold.Value, ref _memOver, ref _memLastNotify);
            }
        }
        if (!_perfBroken && Config.ResourceCpuNotify.Value && _cpu is not null)
        {
            var cpu = _cpu.NextValue();
            Handle("cpu", cpu, Config.ResourceCpuThreshold.Value, ref _cpuOver, ref _cpuLastNotify);
        }
    }

    // 连续超标 ConfirmSamples 次且超过冷却时间时弹窗提醒
    private static void Handle(string kind, double percent, int threshold, ref int over, ref DateTime lastNotify)
    {
        var now = DateTime.Now;
        if (percent >= threshold)
        {
            over++;
            if (over < ConfirmSamples || now - lastNotify < TimeSpan.FromMinutes(RepeatNotifyMinutes))
            {
                return;
            }
            lastNotify = now;
            over = 0;
            var content = AppUtils.Tr(kind == "memory" ? "settings.resource_memory_toast" : "settings.resource_cpu_toast",
                ("percent", (int)Math.Round(percent)));
            Dispatcher.UIThread.Post(() =>
            {
                var (bg, fg) = NotifPalette.FollowTheme();
                new NotificationManager().HandleNotification(new NotificationRequest
                {
                    Type = NotifType.Corner,
                    Content = content,
                    Duration = NotifyDurationSeconds,
                    BgColor = bg,
                    TextColor = fg,
                    TtsVoice = "done",
                });
            });
            Log.Info($"[资源] {kind} 提醒 阈值{threshold}% 当前{percent:F0}%");
        }
        else
        {
            // 恢复正常 重置连续计数
            over = 0;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    // 物理内存占用率 0-100 失败返回0
    private static double MemoryUsagePercent()
    {
        try
        {
            var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref st) ? st.dwMemoryLoad : 0;
        }
        catch (Exception e)
        {
            Log.Debug($"[资源] 内存查询失败: {e.Message}");
            return 0;
        }
    }
}
