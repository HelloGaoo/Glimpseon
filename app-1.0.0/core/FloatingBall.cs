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
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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

    public static readonly (string Value, string Key)[] Builtins =
    [
        ("main", "custom_action.builtin_main"),
        ("pen", "custom_action.builtin_pen"),
        ("undo", "custom_action.builtin_undo"),
        ("clear", "custom_action.builtin_clear"),
        ("whiteboard", "custom_action.builtin_whiteboard"),
        ("freeze", "custom_action.builtin_freeze"),
    ];

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

    private static void RunBuiltin(string value)
    {
        switch (value)
        {
            case "main":
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
                {
                    window.Show();
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Maximized;
                    }
                    window.Activate();
                }
                break;
            case "pen":
                WritingPadService.TogglePen();
                break;
            case "undo":
                WritingPadService.Undo();
                break;
            case "clear":
                WritingPadService.ClearAll();
                break;
            case "whiteboard":
                WritingPadService.SetWhiteboard(!WritingPadService.Whiteboard);
                break;
            case "freeze":
                WritingPadService.ToggleFreeze();
                break;
        }
    }
}


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
