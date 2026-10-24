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

// 悬浮球
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Glimpseon.Core.Win32;
using Glimpseon.UI;

namespace Glimpseon.Core;

internal enum CustomActionKind
{
    App = 0,
    Url = 1,
    Builtin = 2,
}

internal sealed class CustomAction
{
    // 存 FASymbol 成员名 渲染时解析
    public string Icon { get; set; } = "Edit";
    public string Label { get; set; } = "";
    public CustomActionKind Kind { get; set; }
    public string Target { get; set; } = "";
}

internal static class CustomActions
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    // 懒加载
    private static List<CustomAction>? _items;

    public static event Action? Changed;

    public static IReadOnlyList<CustomAction> Items => _items ??= Load();


    public const int MaxItems = 7;

    // 按键码
    private const byte VkWin = 0x5B;
    private const byte VkShift = 0x10;
    private const byte VkTab = 0x09;
    private const byte VkV = 0x56;
    private const byte VkD = 0x44;
    private const byte VkS = 0x53;
    private const byte VkP = 0x50;
    private const byte VkF11 = 0x7A;
    private const byte VkMediaPlay = 0xB3;
    private const byte VkMediaMute = 0xAD;
    private const byte VkUp = 0x26;

    // 内置动作
    public static readonly IReadOnlyList<BuiltinAction> Builtins =
    [
        // 窗口 屏幕
        new("topmost", "Target", ToggleTopmost),
        new("fullscreen", "View", () => Native.SendKeys(VkF11)),
        new("closewindow", "Dismiss", Native.CloseForegroundWindow),
        new("snipping", "Scan", () => Native.SendKeys(VkWin, VkShift, VkS)),
        new("project", "Switch", () => Native.SendKeys(VkWin, VkP)),
        new("magnifier", "Zoom", () => Launch("magnify.exe")),
        new("osk", "Edit", () => Launch("osk.exe")),
        new("taskview", "ViewAll", () => Native.SendKeys(VkWin, VkTab)),
        new("showdesktop", "TwoBars", () => Native.SendKeys(VkWin, VkD)),
        new("clipboard", "Copy", () => Native.SendKeys(VkWin, VkV)),
        new("maximize", "FullScreenMaximize", () => Native.SendKeys(VkWin, VkUp)),

        // 音量 画面
        new("mute", "Volume", () => Native.SendKeys(VkMediaMute)),
        new("volume", "Volume", () => ToggleVolume?.Invoke(), true),
        new("brightness", "WeatherSunny", () => ToggleBrightness?.Invoke(), true),
        new("playpause", "Play", () => Native.SendKeys(VkMediaPlay)),

        // 文件夹
        new("explorer", "Folder", () => Launch("explorer.exe")),
        new("documents", "Page", () => Launch("shell:Personal")),
        new("downloads", "Download", OpenDownloads),
        new("pictures", "SaveLocal", () => Launch("shell:MyPictures")),
        new("recyclebin", "Sync", () => Launch("shell:RecycleBinFolder")),

        // 程序与工具
        new("notepad", "Page", () => Launch("notepad.exe")),
        new("calc", "Ruler", () => Launch("calc.exe")),
        new("paint", "SaveAs", () => Launch("mspaint.exe")),
        new("taskmgr", "TwoBars", () => Launch("taskmgr.exe")),

        // 课程表与设置
        new("schedule", "Clock", ShowSchedule),
        new("settings", "Settings", () => Launch("ms-settings:")),

        // 电源
        new("lock", "ReportHacked", Native.LockScreen),
        new("sleep", "Clock", Native.SleepSystem),
        new("restart", "RotateClockwise", () => Launch("shutdown.exe", "/r /t 0")),
        new("shutdown", "Dismiss", () => Launch("shutdown.exe", "/s /t 0")),
    ];

    public static Action? ToggleVolume { get; set; }
    public static Action? ToggleBrightness { get; set; }

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["volumeup"] = "volume",
        ["volumedown"] = "volume",
        ["brightnessup"] = "brightness",
        ["brightnessdown"] = "brightness",
    };

    public static BuiltinAction? FindBuiltin(string? key)
    {
        if (key is not null && Aliases.TryGetValue(key, out var alias))
        {
            key = alias;
        }
        return Builtins.FirstOrDefault(x => x.Key == key);
    }

    // 保留更多行
    public static bool IsPersistent(CustomAction action) =>
        action.Kind == CustomActionKind.Builtin && FindBuiltin(action.Target)?.Stays == true;

    // 按钮文案
    public static string LabelOf(CustomAction action) =>
        action.Kind == CustomActionKind.Builtin
            ? AppUtils.Tr(FindBuiltin(action.Target)?.LabelKey ?? action.Label)
            : action.Label;

    // 图标名
    public static string IconNameOf(CustomAction action) =>
        action.Kind switch
        {
            CustomActionKind.Builtin => FindBuiltin(action.Target)?.Icon ?? "Edit",
            CustomActionKind.Url => "Globe",
            _ => "Page",
        };

    public static List<CustomAction> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<List<CustomAction>>(Config.BallCustomActions.Value, Json) ?? [];
        }
        catch (Exception e)
        {
            Log.Warning($"自定义按钮解析失败: {e.Message}");
            return [];
        }
    }

    public static void Save(List<CustomAction> items)
    {
        _items = items;
        Config.BallCustomActions.Value = JsonSerializer.Serialize(items, Json);
        Changed?.Invoke();
    }

    public static void Run(CustomAction action)
    {
        try
        {
            if (action.Kind == CustomActionKind.Builtin)
            {
                RunBuiltin(action.Target);
                return;
            }
            if (string.IsNullOrWhiteSpace(action.Target))
            {
                return;
            }
            Process.Start(new ProcessStartInfo(action.Target.Trim()) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Warning($"自定义按钮执行失败: {e.Message}");
        }
    }

    private static void RunBuiltin(string value) => FindBuiltin(value)?.Run();

    private static void Launch(string target) => Launch(target, null);

    private static void Launch(string target, string? args)
    {
        try
        {
            var info = args is null ? new ProcessStartInfo(target) : new ProcessStartInfo(target, args);
            info.UseShellExecute = true;
            Process.Start(info);
        }
        catch (Exception e)
        {
            Log.Warning($"打开 {target} 失败: {e.Message}");
        }
    }

    // 课程表
    private static void ShowSchedule()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            { MainWindow: MainWindow main })
        {
            main.OpenScheduleWindow();
        }
    }

    private static void ToggleTopmost()
    {
        Native.ToggleForegroundTopmost();
        var ball = WritingPadService.BallHandleProvider?.Invoke() ?? nint.Zero;
        if (ball != nint.Zero)
        {
            Native.KeepTopmost(ball);
        }
    }

    private static void OpenDownloads()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(home, "Downloads");
        Launch(Directory.Exists(path) ? path : home);
    }
}

// 内置动作
internal sealed class BuiltinAction(string key, string icon, Action run, bool stays = false)
{
    public string Key { get; } = key;
    public string Icon { get; } = icon;
    public Action Run { get; } = run;
    public bool Stays { get; } = stays;
    public string LabelKey => $"custom_action.builtin_{Key}";
}

// 文件图标
internal static class FileIcon
{
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;

    private static readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info,
        uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    public static Avalonia.Media.Imaging.Bitmap? For(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        if (Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }
        var bitmap = Load(path);
        Cache[path] = bitmap;
        return bitmap;
    }

    private static Avalonia.Media.Imaging.Bitmap? Load(string path)
    {
        var info = new SHFILEINFO();
        var ok = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            ShgfiIcon | ShgfiLargeIcon);
        if (ok == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            using var drawing = System.Drawing.Icon.FromHandle(info.hIcon).ToBitmap();
            using var stream = new MemoryStream();
            drawing.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            return new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch (Exception e)
        {
            Log.Warning($"文件图标读取失败: {e.Message}");
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}

// 系统音量
internal static class Audio
{
    private static Guid _ctx = Guid.Empty;
    private static IAudioEndpointVolume? _endpoint;

    public static double Volume
    {
        get
        {
            var ep = Endpoint();
            if (ep is null || ep.GetMasterVolumeLevelScalar(out var v) != 0)
            {
                return 0;
            }
            return Math.Clamp(v, 0, 1);
        }
    }

    public static void SetVolume(double volume)
    {
        var ep = Endpoint();
        ep?.SetMasterVolumeLevelScalar((float)Math.Clamp(volume, 0, 1), ref _ctx);
    }

    private static IAudioEndpointVolume? Endpoint()
    {
        if (_endpoint is not null)
        {
            return _endpoint;
        }
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(0, 0, out var device);
            var iid = typeof(IAudioEndpointVolume).GUID;
            // CLSCTX_ALL
            device.Activate(ref iid, 0x17, IntPtr.Zero, out var iface);
            _endpoint = (IAudioEndpointVolume)iface;
        }
        catch (Exception e)
        {
            Log.Warning($"音量设备打不开: {e.Message}");
        }
        return _endpoint;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        int GetDevice(string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        int OpenPropertyStore(int access, out IntPtr store);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float level, ref Guid ctx);
        int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
    }
}
