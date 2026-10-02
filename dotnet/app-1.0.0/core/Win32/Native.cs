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
        catch (Exception)
        {
            return false;
        }
    }

    public static void BroadcastFontChange()
    {
        try
        {
            SendMessageTimeoutW(HwndBroadcast, WmFontChange, UIntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, 500, out _);
        }
        catch (Exception)
        {
            // 广播失败忽略
        }
    }

    public static void ShowMessageBox(string text, string caption)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, text, caption, 0x40);
        }
        catch (Exception)
        {
            // 弹窗失败忽略
        }
    }
}
