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

// SystemSampler
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Media.Imaging;
using IOPath = System.IO.Path;
using Avalonia.Controls.Shapes;
using System.Text.RegularExpressions;
using Avalonia.Animation;
using Avalonia.Threading;
using Glimpseon.Core.Services;
using Markdown.Avalonia;
using Avalonia.Input.Platform;
using System.Runtime.InteropServices;
using AvColor = Avalonia.Media.Color;
using System.Globalization;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Utilities;
using Avalonia.VisualTree;


namespace Glimpseon.UI;

// ExtraWidgets.cs

/// <summary>全局系统采样: CPU/RAM/GPU 网络 麦克风分贝</summary>
internal static class SystemSampler
{
    private static System.Diagnostics.PerformanceCounter? _cpu;
    private static bool _perfBroken;

    public static event Action<double, double, double>? PerfReady; // cpu ram gpu
    public static event Action<double, double>? NetReady;          // down up (bits/s)

    public static void EnsureStarted()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        // 性能计数器查询(GPU 实例枚举可达数百 ms)严禁占用 UI 线程 -> 后台循环采样 事件回 UI 线程
        Task.Run(async () =>
        {
            try
            {
                _cpu = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpu.NextValue();
            }
            catch (Exception e)
            {

                Log.Warning($"[PERF] CPU 计数器不可用: {e.Message}");
                _perfBroken = true;
            }
            RebuildGpuCounters();
            RebuildNetCounters();
            while (true)
            {
                try
                {
                    await Task.Delay(1000);
                    Sample();
                }
                catch (Exception e)
                {

                    Log.Debug($"[PERF] 采样异常: {e.Message}");
                    // 采样异常忽略
                }
            }
        });
        MicSampler.EnsureStarted();
    }

    private static bool _started;

    // Network Interface 计数器按网卡实例枚举求和(非单实例计数器) 实例随网卡启停变化 每 30 次重建
    private static List<System.Diagnostics.PerformanceCounter>? _netInList;
    private static List<System.Diagnostics.PerformanceCounter>? _netOutList;
    private static int _netAge;

    private static void RebuildNetCounters()
    {
        try
        {
            var cat = new System.Diagnostics.PerformanceCounterCategory("Network Interface");
            _netInList = new List<System.Diagnostics.PerformanceCounter>();
            _netOutList = new List<System.Diagnostics.PerformanceCounter>();
            foreach (var nic in cat.GetInstanceNames())
            {
                _netInList.Add(new System.Diagnostics.PerformanceCounter("Network Interface", "Bytes Received/sec", nic));
                _netOutList.Add(new System.Diagnostics.PerformanceCounter("Network Interface", "Bytes Sent/sec", nic));
            }
            foreach (var c in _netInList.Concat(_netOutList))
            {
                c.NextValue();
            }
            _netAge = 0;
        }
        catch (Exception e)
        {
            Log.Debug($"[PERF] 网卡计数器枚举失败: {e.Message}");
            _netInList = null;
            _netOutList = null;
        }
    }

    private static (double Down, double Up) SampleNet()
    {
        if (_netInList is null || _netOutList is null || _netInList.Count == 0)
        {
            RebuildNetCounters();
            return (0, 0);
        }
        _netAge++;
        if (_netAge > 30)
        {
            RebuildNetCounters();
        }
        double down = 0;
        double up = 0;
        foreach (var c in _netInList)
        {
            try { down += Math.Max(0, c.NextValue()); } catch { }
        }
        foreach (var c in _netOutList)
        {
            try { up += Math.Max(0, c.NextValue()); } catch { }
        }
        return (down * 8, up * 8);
    }


    // GPU Engine 计数器按实例枚举求和 实例随应用启停变化 每 30 次重建
    private static List<System.Diagnostics.PerformanceCounter>? _gpuCounters;
    private static int _gpuAge;

    private static void RebuildGpuCounters()
    {
        try
        {
            var cat = new System.Diagnostics.PerformanceCounterCategory("GPU Engine");
            _gpuCounters = cat.GetInstanceNames()
                .Select(n => new System.Diagnostics.PerformanceCounter("GPU Engine", "Utilization Percentage", n))
                .ToList();
            foreach (var c in _gpuCounters)
            {
                c.NextValue();
            }
            _gpuAge = 0;
        }
        catch (Exception e)
        {

            Log.Warning($"[PERF] GPU 计数器不可用: {e.Message}");
            _gpuCounters = null;
        }
    }

    private static double SampleGpu()
    {
        if (_gpuCounters is null || _gpuCounters.Count == 0)
        {
            RebuildGpuCounters();
            return 0;
        }
        _gpuAge++;
        if (_gpuAge > 30)
        {
            RebuildGpuCounters();
        }
        double sum = 0;
        foreach (var c in _gpuCounters)
        {
            try
            {
                sum += c.NextValue();
            }
            catch (Exception e)
            {

                Log.Debug($"[PERF] GPU 实例失效跳过: {e.Message}");
                // 实例失效跳过
            }
        }
        return Math.Min(100, sum);
    }

    private static void Sample()
    {
        try
        {
            var cpu = _perfBroken ? 0 : Math.Min(100, _cpu?.NextValue() ?? 0);
            var ram = MemoryUsagePercent();
            var gpu = SampleGpu();
            var (down, up) = SampleNet();
            // 订阅方(控件)要求 UI 线程
            Dispatcher.UIThread.Post(() =>
            {
                PerfReady?.Invoke(cpu, ram, gpu);
                NetReady?.Invoke(down, up);
            });
        }
        catch (Exception e)
        {

            Log.Debug($"[PERF] 采样异常: {e.Message}");
            // 采样异常忽略
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

    private static double MemoryUsagePercent()
    {
        var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref st) ? st.dwMemoryLoad : 0;
    }
}
