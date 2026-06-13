using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace ClassLively_UI.Helpers;

/// <summary>
/// 系统托盘
/// </summary>
public static class TrayIconHelper
{
    //  Win32 常量与结构体

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETFOCUS = 0x00000003;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint WM_USER = 0x0400;
    private const uint TRAY_CALLBACK_MSG = WM_USER + 1;
    private const int ID_TRAY_ICON = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public hBalloonIcon hBalloon;
    }

    private enum hBalloonIcon { NONE }

    //  Win32 API 导入

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    //  状态

    private static NOTIFYICONDATA _nid;
    private static bool _isCreated;
    private static Window? _window;
    private static Action? _onShowWindow;
    private static Action? _onExit;
    private static bool _debugMode;
    private static int _minimizeNotificationCount;
    private const int MaxMinimizeNotifications = 5;

    /// <summary>创建托盘图标</summary>
    public static void Create(IntPtr hwnd, string iconPath, string tooltip, Action onShowWindow, Action onExit, bool debugMode = false)
    {
        if (_isCreated) return;

        _window = null; // WinUI 3 窗口通过 hwnd 传递
        _onShowWindow = onShowWindow;
        _onExit = onExit;
        _debugMode = debugMode;
        _minimizeNotificationCount = 0;

        var iconHandle = LoadIcon(iconPath);

        _nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = ID_TRAY_ICON,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = TRAY_CALLBACK_MSG,
            hIcon = iconHandle,
            szTip = tooltip ?? "ClassLively"
        };

        if (!Shell_NotifyIconW(NIM_ADD, ref _nid))
            Debug.WriteLine("[TrayIcon] Shell_NotifyIconW(NIM_ADD) 失败");

        _isCreated = true;
        Debug.WriteLine("[TrayIcon] 托盘图标已创建");
    }

    /// <summary>更新提示文字</summary>
    public static void UpdateTooltip(string tooltip)
    {
        if (!_isCreated) return;
        _nid.szTip = tooltip ?? "ClassLively";
        _nid.uFlags = NIF_TIP;
        Shell_NotifyIconW(NIM_MODIFY, ref _nid);
    }

    /// <summary>显示通知</summary>
    public static void ShowNotification(string title, string message)
    {
        if (!_isCreated) return;
        var oldNid = _nid;
        _nid.szInfo = message ?? "";
        _nid.szInfoTitle = title ?? "";
        _nid.dwInfoFlags = 0x00000001; // NIIF_INFO
        _nid.uFlags |= NIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, ref _nid);
        _nid = oldNid; // 恢复
    }

    /// <summary>处理托盘回调</summary>
    public static bool HandleMessage(uint msg, IntPtr wParam, IntPtr lParam, out bool shouldToggleWindow)
    {
        shouldToggleWindow = false;
        if (!_isCreated || msg != TRAY_CALLBACK_MSG) return false;

        var loWord = (uint)(lParam.ToInt64() & 0xFFFF);
        switch (loWord)
        {
            case 0x0202: // WM_LBUTTONUP — 单击左键 切换显隐
                shouldToggleWindow = true;
                return true;
            case 0x0203: // WM_LBUTTONDBLCLK — 双击左键 切换显隐
                shouldToggleWindow = true;
                return true;
            case 0x0205: // WM_RBUTTONUP — 右键 显示菜单
                ShowContextMenu(wParam);
                return true;
        }
        return false;
    }

    /// <summary>显示右键菜单todo</summary>
    private static void ShowContextMenu(IntPtr hwnd)
    {
        Debug.WriteLine("[TrayIcon] 右键菜单被点击");
    }

    /// <summary>触发菜单项</summary>
    public static void OnMenuShow()
    {
        _onShowWindow?.Invoke();
    }

    public static void OnMenuExit()
    {
        Remove();
        _onExit?.Invoke();
    }

    /// <summary>最小化到托盘</summary>
    public static void MinimizeToTray()
    {
        if (_minimizeNotificationCount < MaxMinimizeNotifications)
        {
            ShowNotification("ClassLively", "应用已最小化到系统托盘");
            _minimizeNotificationCount++;
        }
    }

    /// <summary>移除托盘图标</summary>
    public static void Remove()
    {
        if (!_isCreated) return;
        Shell_NotifyIconW(NIM_DELETE, ref _nid);

        if (_nid.hIcon != IntPtr.Zero)
            DestroyIcon(_nid.hIcon);

        _isCreated = false;
    }

    /// <summary>是否已创建</summary>
    public static bool IsCreated => _isCreated;

    // ── 内部方法 ──

    private static IntPtr LoadIcon(string? path)
    {
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            try { return LoadImageW(IntPtr.Zero, path, IMAGE_ICON, 48, 48, LR_LOADFROMFILE); }
            catch { }
        }
        // 回退：默认应用图标
        try { return LoadImageW(IntPtr.Zero, null, IMAGE_ICON, 48, 48, 0); }
        catch { return IntPtr.Zero; }
    }
}
