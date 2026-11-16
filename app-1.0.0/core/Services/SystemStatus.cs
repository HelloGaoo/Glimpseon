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

// SystemStatus
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Glimpseon.Core.Services;

/// <summary>电池快照</summary>
public sealed record BatterySnapshot(bool Present, int Percent, bool Charging, bool OnAc, int MinutesLeft);

/// <summary>电池状态</summary>
public static class BatteryService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static BatterySnapshot Read()
    {
        try
        {
            if (!GetSystemPowerStatus(out var s))
            {
                return new BatterySnapshot(false, 0, false, false, -1);
            }
            var onAc = s.AcLineStatus == 1;
            // 无电池标记
            if ((s.BatteryFlag & 128) != 0)
            {
                return new BatterySnapshot(false, 0, false, onAc, -1);
            }
            var percent = s.BatteryLifePercent == 255 ? -1 : s.BatteryLifePercent;
            var minutes = s.BatteryLifeTime < 0 ? -1 : s.BatteryLifeTime / 60;
            return new BatterySnapshot(true, percent, (s.BatteryFlag & 8) != 0, onAc, minutes);
        }
        catch (Exception e)
        {
            Log.Warning($"[BAT] 电池状态读取失败: {e.Message}");
            return new BatterySnapshot(false, 0, false, false, -1);
        }
    }
}

/// <summary>网卡快照</summary>
public sealed record LinkSnapshot(string Name, string Kind, string Address, string Gateway, double Mbps, bool Up);

/// <summary>联网状态</summary>
public static class LinkService
{
    public static List<LinkSnapshot> Read()
    {
        var list = new List<(LinkSnapshot Snapshot, bool Virtual)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }
                var up = ni.OperationalStatus == OperationalStatus.Up;
                var address = "";
                var gateway = "";
                try
                {
                    var props = ni.GetIPProperties();
                    var unicast = props.UnicastAddresses.Select(a => a.Address).ToList();
                    address = unicast.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString()
                        ?? unicast.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6
                                                      && !a.IsIPv6LinkLocal)?.ToString()
                        ?? "";
                    // 网关优先 IPv4
                    var gateways = props.GatewayAddresses.Select(g => g.Address).ToList();
                    gateway = gateways.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString()
                        ?? gateways.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6
                                                        && !a.IsIPv6LinkLocal)?.ToString()
                        ?? gateways.FirstOrDefault()?.ToString()
                        ?? "";
                }
                catch (Exception e)
                {
                    Log.Debug($"[LINK] {ni.Name} 地址读取失败: {e.Message}");
                }
                var kind = ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => "有线",
                    NetworkInterfaceType.Ppp => "拨号",
                    _ => "其他",
                };
                double mbps = 0;
                try
                {
                    mbps = ni.Speed > 0 ? ni.Speed / 1_000_000.0 : 0;
                }
                catch (Exception e)
                {
                    Log.Debug($"[LINK] {ni.Name} 速率读取失败: {e.Message}");
                }
                list.Add((new LinkSnapshot(ni.Name, kind, address, gateway, mbps, up), IsVirtual(ni)));
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[LINK] 网卡枚举失败: {e.Message}");
        }
        return list
            .OrderByDescending(x => x.Snapshot.Up)
            .ThenByDescending(x => !x.Virtual)
            .ThenByDescending(x => x.Snapshot.Address.Length > 0)
            .ThenBy(x => x.Snapshot.Kind)
            .Select(x => x.Snapshot)
            .ToList();
    }

    // 虚拟网卡识别
    private static bool IsVirtual(NetworkInterface ni)
    {
        var text = $"{ni.Name} {ni.Description}";
        return text.Contains("virtual", StringComparison.OrdinalIgnoreCase)
            || text.Contains("hyper-v", StringComparison.OrdinalIgnoreCase)
            || text.Contains("vmware", StringComparison.OrdinalIgnoreCase)
            || text.Contains("virtualbox", StringComparison.OrdinalIgnoreCase)
            || text.Contains("vethernet", StringComparison.OrdinalIgnoreCase)
            || text.Contains("vpn", StringComparison.OrdinalIgnoreCase)
            || text.Contains("tunnel", StringComparison.OrdinalIgnoreCase)
            || text.Contains("tap-", StringComparison.OrdinalIgnoreCase)
            || text.Contains("wintun", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>首选在线网卡</summary>
    public static LinkSnapshot? Primary()
    {
        var all = Read();
        // 首选有网关网卡
        return all.FirstOrDefault(l => l.Up && l.Address.Length > 0 && l.Gateway.Length > 0)
            ?? all.FirstOrDefault(l => l.Up && l.Address.Length > 0)
            ?? all.FirstOrDefault(l => l.Up)
            ?? all.FirstOrDefault();
    }
}

/// <summary>设备静态信息</summary>
public sealed record PcSnapshot(string Machine, string User, string Os, string Cpu, int Cores, double RamGb);

/// <summary>设备信息</summary>
public static class PcInfoService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    public static PcSnapshot Read()
    {
        var machine = Environment.MachineName;
        var user = Environment.UserName;
        var version = Environment.OSVersion.Version;
        var os = $"Windows {version.Major}.{version.Minor} (Build {version.Build})";
        var cpu = ReadCpuName();
        var ram = 0.0;
        try
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                ram = Math.Round(status.TotalPhys / 1073741824.0, 1);
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[PC] 内存读取失败: {e.Message}");
        }
        return new PcSnapshot(machine, user, os, cpu, Environment.ProcessorCount, ram);
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "未知处理器";
        }
        catch (Exception e)
        {
            Log.Debug($"[PC] CPU 名称读取失败: {e.Message}");
            return "未知处理器";
        }
    }
}

/// <summary>回收站快照</summary>
public sealed record RecycleSnapshot(long Count, long Bytes);

/// <summary>回收站服务</summary>
public static class RecycleBinService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct QueryRbInfo
    {
        public int Size;
        public long Bytes;
        public long Items;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? path, ref QueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? path, uint flags);

    // NOCONFIRMATION | NOPROGRESSUI | NOSOUND
    private const uint EmptyFlags = 0x1 | 0x2 | 0x4;

    public static RecycleSnapshot Query()
    {
        try
        {
            var info = new QueryRbInfo { Size = Marshal.SizeOf<QueryRbInfo>() };
            return SHQueryRecycleBin(null, ref info) == 0
                ? new RecycleSnapshot(info.Items, info.Bytes)
                : new RecycleSnapshot(0, 0);
        }
        catch (Exception e)
        {
            Log.Warning($"[BIN] 回收站查询失败: {e.Message}");
            return new RecycleSnapshot(0, 0);
        }
    }

    public static bool Empty()
    {
        try
        {
            var code = SHEmptyRecycleBin(IntPtr.Zero, null, EmptyFlags);
            Log.Info($"[BIN] 清空回收站 返回={code}");
            return code == 0;
        }
        catch (Exception e)
        {
            Log.Warning($"[BIN] 清空回收站失败: {e.Message}");
            return false;
        }
    }

    public static void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Warning($"[BIN] 打开回收站失败: {e.Message}");
        }
    }
}

/// <summary>电源操作</summary>
public static class PowerActionService
{
    public static void Lock() => Win32.Native.LockScreen();

    public static void Sleep() => Win32.Native.SleepSystem();

    public static void Restart() => Run("/r /t 0", "重启");

    public static void Shutdown() => Run("/s /t 0", "关机");

    private static void Run(string args, string action)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", args) { UseShellExecute = false, CreateNoWindow = true });
            Log.Info($"[PWR] 执行{action}命令 {args}");
        }
        catch (Exception e)
        {
            Log.Warning($"[PWR] 电源命令失败: {e.Message}");
        }
    }
}
