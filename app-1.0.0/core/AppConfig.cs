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

// 配置管理
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Glimpseon.Core;

public enum ThemeMode
{
    Light,
    Dark,
    Auto,
}

public enum LanguageOption
{
    ChineseSimplified,
    ChineseTraditional,
    English,
    Auto,
}

public enum LogVerbosity
{
    Debug,
    Info,
    Warning,
    Error,
}

public interface IConfigItem
{
    string Group { get; }
    string Name { get; }
    bool RestartRequired { get; }
    void LoadFrom(JsonNode root);
    void WriteTo(JsonNode root);
    void WriteDefaultTo(JsonNode root);
    void RaiseValueChanged();
    string? SelfCheckRoundTrip();
}

public sealed class ConfigItem<T> : IConfigItem
{
    private readonly Func<T, object?> _serialize;
    private readonly Func<object?, T> _deserialize;
    private readonly T?[]? _options;
    private readonly double? _min;
    private readonly double? _max;
    private T _value;

    public string Group { get; }
    public string Name { get; }
    public T DefaultValue { get; }
    public bool RestartRequired { get; }

    public event Action<T>? ValueChanged;

    public ConfigItem(string group, string name, T defaultValue,
        Func<T, object?>? serialize = null, Func<object?, T>? deserialize = null,
        T?[]? options = null, double? min = null, double? max = null, bool restartRequired = false)
    {
        Group = group;
        Name = name;
        DefaultValue = defaultValue;
        _value = defaultValue;
        RestartRequired = restartRequired;
        _serialize = serialize ?? (v => v);
        _deserialize = deserialize ?? (v =>
        {
            if (v is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.String)
                {
                    return (T)Convert.ChangeType(je.GetString()!, typeof(T));
                }
                return (T)je.Deserialize<T>(new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString })!;
            }
            if (v is T typed)
            {
                return typed;
            }
            return (T)Convert.ChangeType(v, typeof(T));
        });
        _options = options;
        _min = min;
        _max = max;
        Config.Register(this);
    }

    public T Value
    {
        get => _value;
        set
        {
            var candidate = Clamp(value);
            if (EqualityComparer<T>.Default.Equals(_value, candidate))
            {
                return;
            }
            _value = candidate;
            Log.Info($"配置变更: {Group}/{Name} = {candidate}");
            ValueChanged?.Invoke(candidate);
            Config.Save();
        }
    }

    private T Clamp(T value)
    {
        if (value is IComparable comparable)
        {
            if (_min is { } min && comparable.CompareTo(Convert.ChangeType(min, typeof(T))) < 0)
            {
                return (T)Convert.ChangeType(min, typeof(T));
            }
            if (_max is { } max && comparable.CompareTo(Convert.ChangeType(max, typeof(T))) > 0)
            {
                return (T)Convert.ChangeType(max, typeof(T));
            }
        }
        if (_options is { Length: > 0 } options)
        {
            foreach (var opt in options)
            {
                if (EqualityComparer<T>.Default.Equals(value, opt!))
                {
                    return value;
                }
            }
            Log.Warning($"配置值 {value} 不在允许范围 用默认值");
            return DefaultValue;
        }
        return value;
    }

    void IConfigItem.LoadFrom(JsonNode root)
    {
        if (root[Group] is not JsonObject group || group[Name] is not { } node)
        {
            return;
        }
        try
        {
            var raw = JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
            _value = Clamp(_deserialize(raw));
        }
        catch (Exception e)
        {
            Log.Warning($"配置项 {Group}/{Name} 读取失败 默认值: {e.Message}");
        }
    }

    void IConfigItem.WriteTo(JsonNode root)
    {
        var obj = root[Group] as JsonObject ?? new JsonObject();
        obj[Name] = JsonSerializer.SerializeToNode(_serialize(_value));
        root[Group] = obj;
    }

    void IConfigItem.WriteDefaultTo(JsonNode root)
    {
        var obj = root[Group] as JsonObject ?? new JsonObject();
        obj[Name] = JsonSerializer.SerializeToNode(_serialize(DefaultValue));
        root[Group] = obj;
    }

    void IConfigItem.RaiseValueChanged() => ValueChanged?.Invoke(_value);

    // 往返自检
    string? IConfigItem.SelfCheckRoundTrip()
    {
        var failures = new List<string>();
        var seen = new HashSet<string>();
        foreach (var boxed in EnumerateProbes())
        {
            if (boxed is not T value)
            {
                continue;
            }
            var json = SerializeJson(value);
            if (!seen.Add(json))
            {
                continue;
            }
            try
            {
                var raw = JsonSerializer.Deserialize<JsonElement>(json);
                var back = Clamp(_deserialize(raw));
                var json2 = SerializeJson(back);
                if (json != json2)
                {
                    failures.Add($"{json} → {json2}");
                }
            }
            catch (Exception e)
            {
                failures.Add($"{json} → 异常 {e.GetType().Name}: {e.Message}");
            }
        }
        return failures.Count == 0 ? null : string.Join(" | ", failures);
    }

    private string SerializeJson(T value)
    {
        var node = JsonSerializer.SerializeToNode(_serialize(value));
        return node?.ToJsonString() ?? "null";
    }

    // 探测值: 当前值 / 默认值 / 所有选项 / 布尔取反 / 范围内的数值
    private IEnumerable<object?> EnumerateProbes()
    {
        yield return _value;
        yield return DefaultValue;
        if (_options is { Length: > 0 })
        {
            foreach (var o in _options)
            {
                if (o is not null)
                {
                    yield return o;
                }
            }
        }
        switch (_value)
        {
            case bool b:
                yield return !b;
                break;
            case int i:
            {
                var hasLo = _min is not null;
                var hasHi = _max is not null;
                var lo = hasLo ? (int)Math.Ceiling(_min!.Value) : int.MinValue;
                var hi = hasHi ? (int)Math.Floor(_max!.Value) : int.MaxValue;
                var candidates = new List<int> { i + 1, i - 1 };
                if (hasLo)
                {
                    candidates.Add(lo);
                }
                if (hasHi)
                {
                    candidates.Add(hi);
                }
                if (hasLo && hasHi)
                {
                    candidates.Add(lo + (hi - lo) / 2);
                }
                foreach (var c in candidates)
                {
                    if (c >= lo && c <= hi)
                    {
                        yield return c;
                    }
                }
                break;
            }
            case double d:
            {
                var lo = _min ?? double.MinValue;
                var hi = _max ?? double.MaxValue;
                foreach (var c in new[] { d + 1.5, d - 1.5, lo, hi })
                {
                    if (c >= lo && c <= hi && !double.IsInfinity(c))
                    {
                        yield return c;
                    }
                }
                break;
            }
            case string s when _options is null or { Length: 0 }:
                yield return s + "_probe";
                break;
        }
    }
}

public static class Config
{
    private static readonly List<IConfigItem> Items = new();
    private static readonly object SaveLock = new();

    public static string ConfigPath { get; } = Path.Combine(Paths.DataConfig, "config.json");

    internal static void Register<T>(ConfigItem<T> item) => Items.Add(item);

    internal static void MarkRestartPending() => RestartPending = true;

    public static bool RestartPending { get; private set; }

    private static string? AsString(object? v) => v switch
    {
        string s => s,
        JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString(),
        null => null,
        _ => v.ToString(),
    };

    // MainWindow
    public static readonly ConfigItem<ThemeMode> ThemeMode = new("MainWindow", "ThemeMode", Glimpseon.Core.ThemeMode.Auto,
        v => v switch
        {
            Glimpseon.Core.ThemeMode.Light => "light",
            Glimpseon.Core.ThemeMode.Dark => "dark",
            _ => "auto",
        },
        v => AsString(v) switch
        {
            "light" => Glimpseon.Core.ThemeMode.Light,
            "dark" => Glimpseon.Core.ThemeMode.Dark,
            _ => Glimpseon.Core.ThemeMode.Auto,
        },
        new ThemeMode[] { Glimpseon.Core.ThemeMode.Light, Glimpseon.Core.ThemeMode.Dark, Glimpseon.Core.ThemeMode.Auto });

    public static readonly ConfigItem<string> ThemeColor = new("MainWindow", "ThemeColor", "#30c361");
    public static readonly ConfigItem<string> DpiScale = new("MainWindow", "DpiScale", "Auto",
        v => v,
        v => AsString(v) ?? "Auto",
        options: new string?[] { "Auto", "1", "1.25", "1.5", "1.75", "2" }, restartRequired: true);
    public static readonly ConfigItem<LanguageOption> Language = new("MainWindow", "Language", LanguageOption.Auto,
        v => v switch
        {
            LanguageOption.ChineseSimplified => "zh_CN",
            LanguageOption.ChineseTraditional => "zh_TW",
            LanguageOption.English => "en_US",
            _ => "Auto",
        },
        v => AsString(v) switch
        {
            "zh_CN" => LanguageOption.ChineseSimplified,
            "zh_TW" => LanguageOption.ChineseTraditional,
            "en_US" => LanguageOption.English,
            _ => LanguageOption.Auto,
        },
        restartRequired: true);

    // Log
    public static readonly ConfigItem<LogVerbosity> LogVerbosity = new("Log", "LogLevel", Glimpseon.Core.LogVerbosity.Info,
        v => v.ToString(),
        v => Enum.TryParse<LogVerbosity>(AsString(v), out var parsed) ? parsed : Glimpseon.Core.LogVerbosity.Info,
        restartRequired: true);
    public static readonly ConfigItem<bool> DisableLog = new("Log", "DisableLog", false, restartRequired: true);
    public static readonly ConfigItem<int> LogMaxCount = new("Log", "MaxCount", 50, min: 10, max: 500);
    public static readonly ConfigItem<int> LogMaxDays = new("Log", "MaxDays", 30, min: 30, max: 365);

    // Other
    public static readonly ConfigItem<string> CloseAction = new("Other", "CloseAction", "minimize",
        options: new string?[] { "minimize", "close" });
    public static readonly ConfigItem<bool> AllowMultipleInstances = new("Other", "AllowMultipleInstances", false);
    public static readonly ConfigItem<bool> DebugMode = new("Other", "DebugMode", false);
    public static readonly ConfigItem<bool> EnableGpuAcceleration = new("Other", "EnableGpuAcceleration", true, restartRequired: true);
    public static readonly ConfigItem<bool> AutoStart = new("Other", "AutoStart", false);
    public static readonly ConfigItem<bool> AutoOpenOnIdle = new("Other", "AutoOpenOnIdle", false);
    public static readonly ConfigItem<int> IdleMinutes = new("Other", "IdleMinutes", 5, min: 1, max: 60);
    public static readonly ConfigItem<bool> AutoOpenMaximize = new("Other", "AutoOpenMaximize", false);
    public static readonly ConfigItem<bool> AutoCheckUpdate = new("Other", "AutoCheckUpdate", true);
    public static readonly ConfigItem<bool> AutoUpdate = new("Other", "AutoUpdate", false);
    public static readonly ConfigItem<string> UpdateChannel = new("Other", "UpdateChannel", "stable",
        options: new string?[] { "stable", "beta" });
    public static readonly ConfigItem<int> MinimizeNotificationCount = new("Other", "MinimizeNotificationCount", 0);
    public static readonly ConfigItem<int> ScrollBannerBgHeight = new("Other", "ScrollBannerBgHeight", 80, min: 40, max: 300);
    public static readonly ConfigItem<bool> ScrollBannerMouseThrough = new("Other", "ScrollBannerMouseThrough", true);

    // Wallpaper
    public static readonly ConfigItem<int> WallpaperSaveLimit = new("Wallpaper", "SaveLimit", 50, min: 10, max: 100);
    public static readonly ConfigItem<string> AutoGetInterval = new("Wallpaper", "AutoGetInterval", "30m",
        options: new string?[] { "never", "10m", "30m", "1h", "3h", "6h", "12h", "1d", "3d", "5d", "7d" });
    public static readonly ConfigItem<bool> AutoSyncToDesktop = new("Wallpaper", "AutoSyncToDesktop", true);
    public static readonly ConfigItem<string> WallpaperApi = new("Wallpaper", "WallpaperApi", "wp.upx8.com",
        options: new string?[] { "wp.upx8.com", "api.ltyuanfang.cn", "imlcd.cn_bg_high", "imlcd.cn_bg_mc", "imlcd.cn_bg_gq" });
    public static readonly ConfigItem<int> WallpaperBrightness = new("Wallpaper", "Brightness", 0, min: -100, max: 0);

    // Appearance
    public static readonly ConfigItem<int> BackgroundBlurRadius = new("Appearance", "BackgroundBlurRadius", 0, min: 0, max: 30);

    // Time
    public static readonly ConfigItem<bool> ShowClock = new("Time", "ShowClock", true);
    public static readonly ConfigItem<bool> ShowClockSeconds = new("Time", "ShowClockSeconds", true);
    public static readonly ConfigItem<bool> ShowLunarCalendar = new("Time", "ShowLunarCalendar", true);
    public static readonly ConfigItem<string> ClockColor = new("Time", "ClockColor", "#FFFFFF");
    public static readonly ConfigItem<int> ClockSize = new("Time", "ClockSize", 80, min: 40, max: 120);
    public static readonly ConfigItem<int> DateSize = new("Time", "DateSize", 16, min: 10, max: 40);
    public static readonly ConfigItem<int> TimeOffset = new("Time", "TimeOffset", 0, min: -9999, max: 9999);
    public static readonly ConfigItem<bool> AutoTimeOffsetEnabled = new("Time", "AutoTimeOffsetEnabled", false);
    public static readonly ConfigItem<int> AutoTimeOffsetIncrement = new("Time", "AutoTimeOffsetIncrement", 1, min: -9999, max: 9999);

    // Poetry
    public static readonly ConfigItem<bool> ShowPoetry = new("Poetry", "ShowPoetry", true);
    public static readonly ConfigItem<string> PoetryApiUrl = new("Poetry", "PoetryApiUrl", "https://v1.hitokoto.cn/");
    public static readonly ConfigItem<string> PoetryUpdateInterval = new("Poetry", "PoetryUpdateInterval", "10m",
        options: new string?[] { "never", "5m", "10m", "30m", "1h", "3h", "6h", "12h", "1d" });
    public static readonly ConfigItem<int> PoetrySize = new("Poetry", "PoetrySize", 16, min: 12, max: 50);
    public static readonly ConfigItem<string> PoetryTextColor = new("Poetry", "PoetryTextColor", "#FFFFFF");

    // Weather
    public static readonly ConfigItem<bool> ShowWeather = new("Weather", "ShowWeather", true);
    public static readonly ConfigItem<int> WeatherSize = new("Weather", "WeatherSize", 24, min: 5, max: 50);
    public static readonly ConfigItem<string> WeatherTextColor = new("Weather", "WeatherTextColor", "#FFFFFF");
    public static readonly ConfigItem<int> WeatherIconSize = new("Weather", "WeatherIconSize", 64, min: 32, max: 200);
    public static readonly ConfigItem<string> WeatherUpdateInterval = new("Weather", "UpdateInterval", "5m",
        options: new string?[] { "never", "5m", "15m", "30m", "1h", "3h", "6h", "12h", "24h" });
    public static readonly ConfigItem<string> City = new("Weather", "City", "北京市");
    public static readonly ConfigItem<string> WeatherSource = new("Weather", "Source", "city",
        options: new string?[] { "city", "coords" });
    public static readonly ConfigItem<double> Latitude = new("Weather", "Latitude", 39.9042);
    public static readonly ConfigItem<double> Longitude = new("Weather", "Longitude", 116.4074);
    public static readonly ConfigItem<string> WeatherUnit = new("Weather", "Unit", "c", options: new string?[] { "c", "f" });
    public static readonly ConfigItem<string> WeatherAlertExcluded = new("Weather", "AlertExcluded", "");

    // Countdown
    public static readonly ConfigItem<bool> ShowCountdown = new("Countdown", "ShowCountdown", true);
    public static readonly ConfigItem<string> CountdownDisplayMode = new("Countdown", "DisplayMode", "simultaneous",
        options: new string?[] { "simultaneous", "carousel" });
    public static readonly ConfigItem<string> CountdownTextColor = new("Countdown", "TextColor", "#FF0000");
    public static readonly ConfigItem<int> CountdownTextSize = new("Countdown", "TextSize", 35, min: 12, max: 120);
    public static readonly ConfigItem<string> CountdownConnectorColor = new("Countdown", "ConnectorColor", "#FFFFFF");
    public static readonly ConfigItem<int> CountdownConnectorSize = new("Countdown", "ConnectorSize", 35, min: 12, max: 60);
    public static readonly ConfigItem<int> CountdownCarouselInterval = new("Countdown", "CarouselInterval", 5, min: 1, max: 60);
    public static readonly ConfigItem<JsonElement> CountdownList = new("Countdown", "CountdownList", default,
        v => v.ValueKind == JsonValueKind.Undefined ? JsonSerializer.SerializeToElement(Array.Empty<object>()) : v,
        v => v is JsonElement je && je.ValueKind == JsonValueKind.Array ? je : JsonSerializer.SerializeToElement(Array.Empty<object>()));

    // School
    public static readonly ConfigItem<string> School = new("School", "School", "");
    public static readonly ConfigItem<string> SchoolClass = new("School", "Class", "");
    public static readonly ConfigItem<bool> ShowSchoolInfo = new("School", "ShowSchoolInfo", false);
    public static readonly ConfigItem<string> SchoolInfoTextColor = new("School", "SchoolInfoTextColor", "#FFFFFF");
    public static readonly ConfigItem<int> SchoolInfoTextSize = new("School", "SchoolInfoTextSize", 34, min: 12, max: 60);

    // QuickLaunch
    public static readonly ConfigItem<bool> ShowQuickLaunch = new("QuickLaunch", "ShowQuickLaunch", true);
    public static readonly ConfigItem<JsonElement> QuickLaunchApps = new("QuickLaunch", "QuickLaunchApps", default,
        v => v.ValueKind == JsonValueKind.Undefined ? JsonSerializer.SerializeToElement(Array.Empty<object>()) : v,
        v => v is JsonElement je && je.ValueKind == JsonValueKind.Array ? je : JsonSerializer.SerializeToElement(Array.Empty<object>()));
    public static readonly ConfigItem<int> QuickLaunchIconSize = new("QuickLaunch", "IconSize", 64, min: 32, max: 96);
    public static readonly ConfigItem<int> QuickLaunchIconSpacing = new("QuickLaunch", "IconSpacing", 12, min: 4, max: 40);
    public static readonly ConfigItem<bool> QuickLaunchShowLabels = new("QuickLaunch", "ShowLabels", true);
    public static readonly ConfigItem<int> QuickLaunchOffsetY = new("QuickLaunch", "OffsetY", 60, min: 0, max: 120);

    // Media
    public static readonly ConfigItem<bool> ShowMediaInfo = new("Media", "ShowMediaInfo", true);
    public static readonly ConfigItem<bool> ShowMediaCover = new("Media", "ShowMediaCover", true);
    public static readonly ConfigItem<bool> ShowMediaLyrics = new("Media", "ShowMediaLyrics", true);
    public static readonly ConfigItem<int> MediaUpdateInterval = new("Media", "UpdateInterval", 1, min: 1, max: 5);
    public static readonly ConfigItem<int> MediaTextSize = new("Media", "TextSize", 14, min: 10, max: 28);
    public static readonly ConfigItem<int> MediaCoverSize = new("Media", "CoverSize", 56, min: 32, max: 128);
    public static readonly ConfigItem<int> MediaLyricsSize = new("Media", "LyricsSize", 12, min: 8, max: 24);
    public static readonly ConfigItem<int> MediaLyricsLines = new("Media", "LyricsLines", 3, min: 1, max: 7);
    public static readonly ConfigItem<int> MediaWidth = new("Media", "Width", 360, min: 200, max: 800);
    public static readonly ConfigItem<int> MediaHeight = new("Media", "Height", 160, min: 100, max: 300);
    public static readonly ConfigItem<int> MediaLyricsAdvance = new("Media", "LyricsAdvance", 300, min: 0, max: 2000);
    public static readonly ConfigItem<bool> MediaUseCustomBg = new("Media", "UseCustomBg", false);
    public static readonly ConfigItem<int> MediaBgOpacity = new("Media", "BgOpacity", 60, min: 0, max: 100);
    public static readonly ConfigItem<int> MediaBorderRadius = new("Media", "BorderRadius", 12, min: 0, max: 30);
    public static readonly ConfigItem<string> MediaTitleColor = new("Media", "TitleColor", "#FFFFFF");
    public static readonly ConfigItem<string> MediaArtistColor = new("Media", "ArtistColor", "#FFFFFF99");
    public static readonly ConfigItem<string> MediaTimeColor = new("Media", "TimeColor", "#FFFFFF80");
    public static readonly ConfigItem<string> MediaLyricsColor = new("Media", "LyricsColor", "#FFFFFFB3");
    public static readonly ConfigItem<int> MediaCoverBorderRadius = new("Media", "CoverBorderRadius", 10, min: 0, max: 20);
    public static readonly ConfigItem<string> MediaCoverBorderColor = new("Media", "CoverBorderColor", "#FFFFFF20");

    // Linkage
    public static readonly ConfigItem<bool> LinkageEnabled = new("Linkage", "Enabled", false);
    public static readonly ConfigItem<string> LinkageDataPath = new("Linkage", "DataPath", "");
    public static readonly ConfigItem<int> LinkagePollInterval = new("Linkage", "PollInterval", 5, min: 1, max: 30);
    public static readonly ConfigItem<bool> LinkageSyncTimeConfig = new("Linkage", "SyncTimeConfig", false);

    // ClassWidgets
    public static readonly ConfigItem<bool> ClassWidgetsEnabled = new("ClassWidgets", "Enabled", false);
    public static readonly ConfigItem<string> ClassWidgetsDataPath = new("ClassWidgets", "DataPath", "");
    public static readonly ConfigItem<int> ClassWidgetsPollInterval = new("ClassWidgets", "PollInterval", 5, min: 1, max: 30);

    // Timetable
    public static readonly ConfigItem<string> ProfileSource = new("Timetable", "ProfileSource", "Glimpseon",
        options: new string?[] { "Glimpseon", "classisland", "classwidgets" });

    // PreciseTime
    public static readonly ConfigItem<bool> UsePreciseTime = new("PreciseTime", "UsePreciseTime", true);
    public static readonly ConfigItem<string> TimeServer = new("PreciseTime", "TimeServer", "ntp.aliyun.com");
    public static readonly ConfigItem<string> LastSyncTime = new("PreciseTime", "LastSyncTime", "");

    // Grid
    public static readonly ConfigItem<int> GridShortSideCells = new("Grid", "ShortSideCells", 6, min: 6, max: 96);
    public static readonly ConfigItem<int> GridInsetPercent = new("Grid", "InsetPercent", 5, min: 0, max: 30);

    public static readonly ConfigItem<int> ComponentCardOpacity = new("Grid", "ComponentCardOpacity", 55, min: 0, max: 100);
    public static readonly ConfigItem<int> ComponentCardRadius = new("Grid", "ComponentCardRadius", 16, min: 0, max: 29);

    // Download
    public static readonly ConfigItem<string> DownloadSource = new("Download", "Source", "hk",
        options: new string?[] { "original", "hk", "cloudflare", "edgeone", "geekertao" });
    public static readonly ConfigItem<int> DownloadItemsPerPage = new("Download", "ItemsPerPage", 8, min: 4, max: 40);

    // Notification
    public static readonly ConfigItem<bool> NotificationSyncBoard = new("Notification", "SyncToBoard", false);
    public static readonly ConfigItem<bool> StorageFullNotify = new("Notification", "StorageFullNotify", true);
    public static readonly ConfigItem<int> StorageFullThreshold = new("Notification", "StorageFullThreshold", 10, min: 5, max: 50);
    public static readonly ConfigItem<bool> ResourceMemoryNotify = new("Notification", "MemoryNotify", true);
    public static readonly ConfigItem<int> ResourceMemoryThreshold = new("Notification", "MemoryNotifyThreshold", 90, min: 50, max: 100);
    public static readonly ConfigItem<bool> ResourceCpuNotify = new("Notification", "CpuNotify", true);
    public static readonly ConfigItem<int> ResourceCpuThreshold = new("Notification", "CpuNotifyThreshold", 90, min: 50, max: 100);

    // Telemetry 
    public static readonly ConfigItem<bool> CrashUpload = new("Telemetry", "CrashUpload", false);
    public static readonly ConfigItem<bool> UsageUpload = new("Telemetry", "UsageUpload", false);

    private static bool _loaded;

    public static void Load()
    {
        Paths.EnsureDataDirs();
        if (File.Exists(ConfigPath))
        {
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject;
                if (root is not null)
                {
                    foreach (var item in Items)
                    {
                        item.LoadFrom(root);
                    }
                    Log.Info($"从 {ConfigPath} 加载配置");
                }
            }
            catch (Exception e)
            {
                Log.Error($"配置读取失败 用默认值: {e.Message}");
            }
        }
        else
        {
            Log.Info($"配置不存在 用默认: {ConfigPath}");
        }
        _loaded = true;
        Save();
    }

    public static void Save()
    {
        if (!_loaded)
        {
            return;
        }
        lock (SaveLock)
        {
            try
            {
                var root = new JsonObject();
                foreach (var item in Items)
                {
                    item.WriteTo(root);
                }
                root["QFluentWidgets"] = new JsonObject
                {
                    ["FontFamilies"] = new JsonArray(
                        "HarmonyOS Sans", "HarmonyOS Sans SC", "HarmonyOS Sans TC", "HarmonyOS Sans HC",
                        "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Source Han Sans SC", "Segoe UI"),
                };
                Directory.CreateDirectory(Paths.DataConfig);
                File.WriteAllText(ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e)
            {
                Log.Error($"保存配置失败 {e.Message}");
            }
        }
    }

    public static JsonObject DefaultCfg()
    {
        var root = new JsonObject();
        foreach (var item in Items.OfType<IConfigItem>())
        {
            item.WriteDefaultTo(root);
        }
        return root;
    }

    public static void BroadcastAll()
    {
        foreach (var item in Items.OfType<IConfigItem>())
        {
            item.RaiseValueChanged();
        }
    }

    public static int ItemCount => Items.Count;

    public static List<string> SelfCheckAll()
    {
        var failures = new List<string>();
        foreach (var item in Items.OfType<IConfigItem>())
        {
            var error = item.SelfCheckRoundTrip();
            if (error is not null)
            {
                failures.Add($"{item.Group}/{item.Name}: {error}");
            }
        }
        return failures;
    }
}
