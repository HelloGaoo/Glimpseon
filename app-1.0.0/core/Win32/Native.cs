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

    // 强制置顶
    public static void ForceTopmost(IntPtr hwnd)
    {
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
}
