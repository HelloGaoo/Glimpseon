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

// Win32
using System.Management;
using System.Runtime.InteropServices;

namespace Glimpseon.Core.Win32;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AddFontResourceW(string lpFileName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SmShuttingDown = 0x2000;

    public static bool IsSystemShuttingDown()
    {
        try
        {
            return GetSystemMetrics(SmShuttingDown) != 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(IntPtr hWnd);

    public static bool IsWindowHung(IntPtr windowHandle)
    {
        try
        {
            return IsHungAppWindow(windowHandle);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    // 设桌面壁纸
    public static bool SetDesktopWallpaper(string path)
    {
        try
        {
            var ok = SystemParametersInfoW(SpiSetDeskWallpaper, 0, path, SpifUpdateIniFile | SpifSendChange);
            if (!ok)
            {
                Log.Warning($"SystemParametersInfoW 设壁纸失败 path={path}");
            }
            return ok;
        }
        catch (Exception e)
        {
            Log.Error($"设置桌面壁纸异常: {e.Message}");
            return false;
        }
    }

    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private const uint WmFontChange = 0x001D;

    public static double GetIdleMilliseconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
        {
            return -1;
        }
        var uptime = (uint)Environment.TickCount;
        var idle = uptime - info.dwTime;
        return idle >= 0 ? idle : -1;
    }

    public static bool RegisterFontFile(string fontPath)
    {
        try
        {
            return AddFontResourceW(fontPath);
        }
        catch (Exception e)
        {

            Glimpseon.Core.Log.Debug($"[WIN32] 调用失败: {e.Message}");
            return false;
        }
    }

    public static void BroadcastFontChange()
    {
        try
        {
            SendMessageTimeoutW(HwndBroadcast, WmFontChange, UIntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, 500, out _);
        }
        catch (Exception e)
        {

            Glimpseon.Core.Log.Debug($"[WIN32] 广播失败: {e.Message}");
            // 广播失败忽略
        }
    }

    public static void ShowMessageBox(string text, string caption)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, text, caption, 0x40);
        }
        catch (Exception e)
        {

            Glimpseon.Core.Log.Debug($"[WIN32] 弹窗失败: {e.Message}");
            // 弹窗失败忽略
        }
    }

    // 电源

    [DllImport("user32.dll", SetLastError = false)]
    private static extern void LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public static void LockScreen()
    {
        try
        {
            LockWorkStation();
        }
        catch (Exception e)
        {
            Glimpseon.Core.Log.Error($"[WIN32] 锁定失败: {e.Message}");
        }
    }

    public static void SleepSystem()
    {
        try
        {
            if (!SetSuspendState(false, false, false))
            {
                Glimpseon.Core.Log.Warning("[WIN32] SetSuspendState 返回 false");
            }
        }
        catch (Exception e)
        {
            Glimpseon.Core.Log.Error($"[WIN32] 睡眠失败: {e.Message}");
        }
    }

    // 媒体内存读取

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, nint lpBaseAddress,
        byte[] lpBuffer, nint nSize, out nint lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;

    // 进程句柄 缓存由调用方管理
    public sealed class ProcessMemory : IDisposable
    {
        private IntPtr _handle;

        public bool Open(int pid)
        {
            Close();
            _handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, pid);
            return _handle != IntPtr.Zero;
        }

        public byte[]? ReadBytes(nint address, int size)
        {
            if (_handle == IntPtr.Zero || size <= 0)
            {
                return null;
            }
            var buffer = new byte[size];
            if (!ReadProcessMemory(_handle, address, buffer, size, out var read) || read != size)
            {
                return null;
            }
            return buffer;
        }

        public double ReadF64(nint address)
        {
            var raw = ReadBytes(address, 8);
            return raw is null ? 0.0 : BitConverter.ToDouble(raw, 0);
        }

        public ulong ReadU64(nint address)
        {
            var raw = ReadBytes(address, 8);
            return raw is null ? 0UL : BitConverter.ToUInt64(raw, 0);
        }

        public int ReadI32(nint address)
        {
            var raw = ReadBytes(address, 4);
            return raw is null ? 0 : BitConverter.ToInt32(raw, 0);
        }

        public void Dispose()
        {
            Close();
        }

        private void Close()
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    // 窗口枚举/标题

    public delegate bool EnumWindowsProc(IntPtr hWnd, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    public static bool TryGetWindowRect(IntPtr hwnd, out (int Left, int Top, int Right, int Bottom) rect)
    {
        if (GetWindowRect(hwnd, out var r))
        {
            rect = (r.Left, r.Top, r.Right, r.Bottom);
            return true;
        }
        rect = default;
        return false;
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpShowwindow = 0x0040;
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;

    public static IntPtr FindWindowByClass(string className) => FindWindowW(className, null);

    public static string? GetWindowText(IntPtr hWnd)
    {
        var length = GetWindowTextLengthW(hWnd);
        if (length <= 0)
        {
            return null;
        }
        var sb = new System.Text.StringBuilder(length + 1);
        GetWindowTextW(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowClassName(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(256);
        GetClassNameW(hWnd, sb, 256);
        return sb.ToString();
    }

    // 枚举全部顶层窗口 回调返回 false 停止
    public static void EnumTopWindows(EnumWindowsProc callback)
    {
        EnumWindows(callback, nint.Zero);
    }

    // 保持置顶 不抢激活 定时刷新用
    public static void KeepTopmost(IntPtr hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
    }

    // 强制置顶
    public static void ForceTopmost(IntPtr hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpShowwindow);
    }

    // 鼠标穿透
    public static void SetMouseThrough(IntPtr hwnd, bool enabled)
    {
        var style = GetWindowLongW(hwnd, GwlExstyle);
        var updated = enabled ? style | WsExTransparent : style & ~WsExTransparent;
        if (updated != style)
        {
            SetWindowLongW(hwnd, GwlExstyle, updated);
        }
    }

    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    public static void HideWindow(IntPtr hwnd)
    {
        if (hwnd != nint.Zero)
        {
            ShowWindow(hwnd, SwHide);
        }
    }

    public static void ShowWindowNoActivate(IntPtr hwnd)
    {
        if (hwnd != nint.Zero)
        {
            ShowWindow(hwnd, SwShowNoActivate);
        }
    }

    // DwmFlush 后截屏
    public static void FlushDwm() => DwmFlush();

    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmncrpDisabled = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // 关 DWM 窗口阴影
    public static void DisableShadow(IntPtr hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }
        var policy = DwmncrpDisabled;
        DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, ref policy, sizeof(int));
    }

    // NOACTIVATE 不抢焦点
    private const int WsExNoActivate = 0x08000000;

    public static void SetNoActivate(IntPtr hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }
        var ex = GetWindowLongW(hwnd, GwlExstyle);
        if ((ex & WsExNoActivate) == 0)
        {
            SetWindowLongW(hwnd, GwlExstyle, ex | WsExNoActivate);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    private const uint WdaNone = 0x0;
    private const uint WdaExcludeFromCapture = 0x11;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    // 屏幕捕获排除
    public static void SetCaptureExclusion(IntPtr hwnd, bool excluded)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }
        var affinity = excluded ? WdaExcludeFromCapture : WdaNone;
        if (!SetWindowDisplayAffinity(hwnd, affinity) && excluded)
        {
            Log.Warning($"捕获排除失败: {Marshal.GetLastWin32Error()}");
        }
    }

    // 系统亮度 WMI(内置屏)/ DDC/CI(外接屏)
    private static ManagementObject? _brightnessMethod;
    private static bool _wmiUnsupported;
    private static IntPtr[]? _ddcMonitors;
    private static readonly object DdcLock = new();

    public static int GetBrightness()
    {
        if (!_wmiUnsupported && TryWmiGet(out var value))
        {
            return value;
        }
        if (TryDdcGet(out value))
        {
            return value;
        }
        return -1;
    }

    private static int _pendingPercent = -1;
    private static int _applying;

    public static void SetBrightness(int percent)
    {
        Volatile.Write(ref _pendingPercent, Math.Clamp(percent, 0, 100));
        if (Interlocked.Exchange(ref _applying, 1) == 1)
        {
            return;
        }
        Task.Run(DrainBrightness);
    }

    private static void DrainBrightness()
    {
        try
        {
            while (true)
            {
                var value = Interlocked.Exchange(ref _pendingPercent, -1);
                if (value < 0)
                {
                    break;
                }
                ApplyBrightness(value);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _applying, 0);
            if (Volatile.Read(ref _pendingPercent) >= 0 && Interlocked.Exchange(ref _applying, 1) == 0)
            {
                Task.Run(DrainBrightness);
            }
        }
    }

    private static void ApplyBrightness(int value)
    {
        if (!_wmiUnsupported && TryWmiSet(value))
        {
            return;
        }
        TryDdcSet(value);
    }

    private static bool TryWmiGet(out int percent)
    {
        percent = -1;
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI",
                "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject item in searcher.Get())
            {
                percent = Convert.ToInt32(item["CurrentBrightness"]);
                return true;
            }
        }
        catch (Exception e)
        {
            _wmiUnsupported = true;
            Log.Info($"WMI 无亮度: {e.Message}");
        }
        return false;
    }

    private static bool TryWmiSet(int value)
    {
        try
        {
            _brightnessMethod ??= FindBrightnessMethod();
            if (_brightnessMethod is null)
            {
                return false;
            }
            using var args = _brightnessMethod.GetMethodParameters("WmiSetBrightness");
            args["Timeout"] = 0u;
            args["Brightness"] = (byte)value;
            _brightnessMethod.InvokeMethod("WmiSetBrightness", args, null);
            return true;
        }
        catch (Exception e)
        {
            _brightnessMethod = null;
            if (!_wmiUnsupported)
            {
                _wmiUnsupported = true;
                Log.Info($"WMI 写亮度失败: {e.Message}");
            }
        }
        return false;
    }

    private static ManagementObject? FindBrightnessMethod()
    {
        using var searcher = new ManagementObjectSearcher("root\\WMI",
            "SELECT * FROM WmiMonitorBrightnessMethods");
        foreach (ManagementObject item in searcher.Get())
        {
            return item;
        }
        return null;
    }

    // DDC/CI 显示器 句柄取句柄
    private static IntPtr[] DdcMonitors()
    {
        lock (DdcLock)
        {
            if (_ddcMonitors is not null)
            {
                return _ddcMonitors;
            }
            var found = new List<IntPtr>();
            EnumDisplayMonitors(nint.Zero, nint.Zero, (monitor, _, _, _) =>
            {
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
                {
                    return true;
                }
                var physical = new PHYSICAL_MONITOR[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical))
                {
                    return true;
                }
                foreach (var item in physical)
                {
                    if (GetMonitorBrightness(item.Handle, out _, out _, out _))
                    {
                        found.Add(item.Handle);
                    }
                }
                return true;
            }, nint.Zero);
            _ddcMonitors = [.. found];
            return _ddcMonitors;
        }
    }

    private static bool TryDdcGet(out int percent)
    {
        percent = -1;
        foreach (var handle in DdcMonitors())
        {
            if (GetMonitorBrightness(handle, out var min, out var cur, out var max) && max > min)
            {
                percent = (int)Math.Round((cur - min) * 100.0 / (max - min));
                return true;
            }
        }
        return false;
    }

    private static bool TryDdcSet(int value)
    {
        var ok = false;
        foreach (var handle in DdcMonitors())
        {
            if (!GetMonitorBrightness(handle, out var min, out _, out var max) || max <= min)
            {
                continue;
            }
            var target = (uint)Math.Round(min + (max - min) * value / 100.0);
            ok |= SetMonitorBrightness(handle, target);
        }
        return ok;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count,
        [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    public static void SendKeys(params byte[] keys)
    {
        foreach (var key in keys)
        {
            keybd_event(key, 0, 0, IntPtr.Zero);
        }
        for (var i = keys.Length - 1; i >= 0; i--)
        {
            keybd_event(keys[i], 0, 2, IntPtr.Zero);
        }
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WmClose = 0x0010;
    private const int WsExTopmost = 0x0008;

    private static readonly IntPtr HwndNotTopmost = new(-2);

    // 关闭前台窗口
    public static void CloseForegroundWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd != nint.Zero)
        {
            PostMessage(hwnd, WmClose, nint.Zero, nint.Zero);
        }
    }

    // 前台窗口切 Topmost
    public static void ToggleForegroundTopmost()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == nint.Zero)
        {
            return;
        }
        var topmost = (GetWindowLongW(hwnd, GwlExstyle) & WsExTopmost) != 0;
        SetWindowPos(hwnd, topmost ? HwndNotTopmost : HwndTopmost, 0, 0, 0, 0,
            SwpNomove | SwpNosize | SwpNoactivate);
    }
}
