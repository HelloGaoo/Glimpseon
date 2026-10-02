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

// 工具函数
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;

namespace Glimpseon.Core;

public sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = "Glimpseon_SingleInstance_Mutex_{A7F3E2D1-8B4C-4F6A-9D0E-1C2B3A4F5E6D}";
    private Mutex? _mutex;
    private bool _isOwner;

    public bool TryAcquire()
    {
        if (_isOwner)
        {
            return true;
        }
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew && !_mutex.WaitOne(TimeSpan.Zero))
        {
            _mutex.Dispose();
            _mutex = null;
            _isOwner = false;
            return false;
        }
        _isOwner = true;
        return true;
    }

    public void Release()
    {
        if (_isOwner)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (Exception)
            {
                // 非所属线程释放 忽略
            }
            _isOwner = false;
        }
        _mutex?.Dispose();
        _mutex = null;
    }

    public bool IsOwner => _isOwner;
    public void Dispose() => Release();
}

public static class AppUtils
{
    private static readonly SingleInstanceManager InstanceManager = new();

    public static bool VerifySingleInstance()
    {
        if (Config.AllowMultipleInstances.Value || Config.DebugMode.Value)
        {
            Log.Debug($"跳过单实例 多开={Config.AllowMultipleInstances.Value} 调试={Config.DebugMode.Value}");
            return true;
        }
        var ok = InstanceManager.TryAcquire();
        Log.Debug($"单实例结果: {ok}");
        return ok;
    }

    public static void ReleaseSingleInstance() => InstanceManager.Release();

    // 翻译

    private static readonly string[] LanguageCodes = { "zh_CN", "zh_TW", "en_US" };
    private static Dictionary<string, Dictionary<string, string>>? _translations;
    private static string _currentLanguage = "zh_CN";

    public static void InitTranslation()
    {
        _translations = new Dictionary<string, Dictionary<string, string>>();
        var localeDir = Path.Combine(Paths.AppDir, "Locales");
        foreach (var code in LanguageCodes)
        {
            var file = Path.Combine(localeDir, $"{code}.json");
            if (!File.Exists(file))
            {
                Log.Warning($"翻译文件缺失 {file}");
                continue;
            }
            try
            {
                var flat = new Dictionary<string, string>();
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                Flatten(doc.RootElement, string.Empty, flat);
                _translations[code] = flat;
            }
            catch (Exception e)
            {
                Log.Error($"翻译加载失败 {code}: {e.Message}");
            }
        }
        Log.Info($"翻译就绪 语言={_currentLanguage} 加载={_translations.Count}");
    }

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> target)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var key = prefix.Length == 0 ? prop.Name : $"{prefix}.{prop.Name}";
                Flatten(prop.Value, key, target);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            target[prefix] = element.GetString() ?? prefix;
        }
    }

    public static void SetLanguage(string code)
    {
        if (LanguageCodes.Contains(code))
        {
            _currentLanguage = code;
            Log.Debug($"语言已切换: {code}");
        }
    }

    public static string CurrentLanguageCode => _currentLanguage;

    public static string Tr(string key, Dictionary<string, object?>? args = null)
    {
        var text = _translations is not null && _translations.TryGetValue(_currentLanguage, out var dict) && dict.TryGetValue(key, out var value)
            ? value
            : key;
        if (args is { Count: > 0 })
        {
            foreach (var (name, arg) in args)
            {
                text = text.Replace($"{{{name}}}", arg?.ToString() ?? string.Empty);
            }
        }
        return text;
    }

    public static string Tr(string key, params (string Name, object? Value)[] args)
    {
        var dict = args.ToDictionary(a => a.Name, a => a.Value);
        return Tr(key, dict);
    }

    public static void ApplyLanguageFromConfig()
    {
        var code = Config.Language.Value switch
        {
            LanguageOption.ChineseSimplified => "zh_CN",
            LanguageOption.ChineseTraditional => "zh_TW",
            LanguageOption.English => "en_US",
            _ => DetectSystemLanguage(),
        };
        SetLanguage(code);
    }

    private static string DetectSystemLanguage()
    {
        try
        {
            var culture = CultureInfo.CurrentUICulture;
            if (culture.TwoLetterISOLanguageName == "zh")
            {
                var region = culture.Name;
                return region is "zh-HK" or "zh-TW" or "zh-MO" ? "zh_TW" : "zh_CN";
            }
            if (culture.TwoLetterISOLanguageName == "en")
            {
                return "en_US";
            }
        }
        catch (Exception)
        {
            // 检测失败 用简中
        }
        return "zh_CN";
    }

    // 缓存

    private static readonly Dictionary<string, int> IntervalMap = new()
    {
        ["never"] = 0, ["5m"] = 300, ["10m"] = 600, ["15m"] = 900, ["30m"] = 1800,
        ["1h"] = 3600, ["3h"] = 10800, ["6h"] = 21600, ["12h"] = 43200, ["24h"] = 86400,
        ["1d"] = 86400, ["3d"] = 259200, ["5d"] = 432000, ["7d"] = 604800,
        ["从不"] = 0, ["5 分钟"] = 300, ["10 分钟"] = 600, ["15 分钟"] = 900, ["30 分钟"] = 1800,
        ["1 小时"] = 3600, ["3 小时"] = 10800, ["6 小时"] = 21600, ["12 小时"] = 43200,
        ["1 天"] = 86400, ["3 天"] = 259200, ["5 天"] = 432000, ["7 天"] = 604800,
    };

    public static int ParseInterval(string interval)
    {
        var key = interval.Trim();
        if (IntervalMap.TryGetValue(key, out var seconds))
        {
            return seconds;
        }
        if (key.Length > 0)
        {
            Log.Warning($"未知间隔 '{interval}' 按从不");
        }
        return 0;
    }

    private static string GetCachePath(string cacheName) => Path.Combine(Paths.DataCache, $"{cacheName}.json");

    public static bool SaveCache(string cacheName, JsonElement content, string interval = "30分钟")
    {
        try
        {
            Paths.EnsureDataDirs();
            var seconds = ParseInterval(interval);
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds() / 1000.0;
            var payload = new
            {
                content,
                timestamp = now,
                expires_at = seconds > 0 ? now + seconds : 1e18,
                interval,
            };
            File.WriteAllText(GetCachePath(cacheName), JsonSerializer.Serialize(payload));
            Log.Info($"缓存写入: {cacheName} (有效期: {interval})");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"保存缓存失败 {cacheName}: {e.Message}");
            return false;
        }
    }

    public static JsonElement? LoadCache(string cacheName, bool ignoreExpiry = false)
    {
        var path = GetCachePath(cacheName);
        if (!File.Exists(path))
        {
            Log.Debug($"缓存不存在: {cacheName}");
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement.Clone();
            var expiresAt = root.TryGetProperty("expires_at", out var exp) ? exp.GetDouble() : 0;
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds() / 1000.0;
            if (expiresAt < 1e17 && now >= expiresAt && !ignoreExpiry)
            {
                Log.Debug($"缓存过期: {cacheName} (过期{now - expiresAt:F0}s)");
                return null;
            }
            return root;
        }
        catch (Exception e)
        {
            Log.Error($"读取缓存失败 {cacheName}: {e.Message}");
            return null;
        }
    }

    public static JsonElement? GetCachedContent(string cacheName, bool ignoreExpiry = false)
    {
        var data = LoadCache(cacheName, ignoreExpiry);
        if (data is { } root && root.ValueKind == JsonValueKind.Object && root.TryGetProperty("content", out var content))
        {
            return content.Clone();
        }
        Log.Debug($"缓存内容不可用: {cacheName}");
        return null;
    }

    // 自启动

    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static (bool Exists, string? Value) CheckAutostart()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            var value = key?.GetValue(Constants.AppName) as string;
            return (value is not null, value);
        }
        catch (Exception e)
        {
            Log.Error($"开机自启动检查: {e.Message}");
            return (false, null);
        }
    }

    public static bool SetAutostart(bool enabled, int delaySeconds = 5)
    {
        Log.Info($"开机自启 enabled={enabled} 延迟{delaySeconds}秒");
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (!enabled)
            {
                key.DeleteValue(Constants.AppName, false);
                Log.Info("开机自启项已从注册表删除");
                return true;
            }

            // 自启指向启动器 由启动器选最新版本
            var launcher = Path.Combine(Paths.PackageRoot, "Glimpseon.exe");
            var exePath = File.Exists(launcher) ? launcher : Environment.ProcessPath ?? launcher;
            string command;
            if (delaySeconds > 0)
            {
                command = $"cmd /c \"timeout /t {delaySeconds} /nobreak >nul && start \\\"\\\" \\\"{exePath}\\\" --autostart\"";
            }
            else
            {
                command = $"\"{exePath}\" --autostart";
            }
            key.SetValue(Constants.AppName, command);
            Log.Info($"开机自启已写入注册表: {command}");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"设置开机自启动失败: {e.Message}");
            return false;
        }
    }

    public static bool SyncAutostartCfg()
    {
        try
        {
            var configAutoStart = Config.AutoStart.Value;
            var (actual, _) = CheckAutostart();
            Log.Info($"同步自启动状态 - 配置: {configAutoStart} 实际: {actual}");
            if (configAutoStart != actual)
            {
                return SetAutostart(configAutoStart);
            }
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"同步自启动状态失败: {e.Message}");
            return false;
        }
    }

    public static bool AutoStartLaunch() =>
        Environment.GetCommandLineArgs().Any(a => a is "--autostart" or "/autostart");

    // NTP

    private static TimeSpan _ntpOffset = TimeSpan.Zero;
    private static DateTime? _lastSyncTime;

    public static TimeSpan NtpOffset => _ntpOffset;
    public static DateTime? LastSyncTime => _lastSyncTime;

    public static bool SyncNtp(string server = "ntp.aliyun.com")
    {
        const long delta = 2208988800;
        try
        {
            using var client = new UdpClient();
            client.Client.ReceiveTimeout = 5000;
            var packet = new byte[48];
            packet[0] = 0x1b;
            client.Connect(server, 123);
            client.Send(packet, packet.Length);
            var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
            var data = client.Receive(ref endpoint);
            if (data.Length < 48)
            {
                throw new InvalidDataException($"NTP 响应数据不足: {data.Length} bytes");
            }
            var seconds = (long)((uint)data[40] << 24 | (uint)data[41] << 16 | (uint)data[42] << 8 | data[43]);
            var fraction = (double)((uint)data[44] << 24 | (uint)data[45] << 16 | (uint)data[46] << 8 | data[47]);
            var ntpTime = seconds - delta + fraction / Math.Pow(2, 32);
            var offset = TimeSpan.FromSeconds(ntpTime - DateTimeOffset.Now.ToUnixTimeMilliseconds() / 1000.0);
            _ntpOffset = offset;
            _lastSyncTime = DateTime.Now;
            Log.Info($"[TimeSync] 同步成功: {server} 偏移 {offset.TotalSeconds:+0.000;-0.000}s");
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"[TimeSync] 同步失败: {e.Message}");
            return false;
        }
    }

    private static DateTime? _autoOffsetLastCheck;

    private static DateTime CheckAutoTimeOffset(DateTime now)
    {
        if (!Config.AutoTimeOffsetEnabled.Value)
        {
            return now;
        }
        var today = now.Date;
        if (_autoOffsetLastCheck is null)
        {
            _autoOffsetLastCheck = today;
            return now;
        }
        if (_autoOffsetLastCheck != today)
        {
            var current = Config.TimeOffset.Value;
            var increment = Config.AutoTimeOffsetIncrement.Value;
            Config.TimeOffset.Value = current + increment;
            Log.Info($"自动时间偏移调整: {current} -> {current + increment}");
            _autoOffsetLastCheck = today;
        }
        return now;
    }

    public static DateTime PreciseNow()
    {
        var now = DateTime.Now;
        if (Config.UsePreciseTime.Value)
        {
            now += _ntpOffset;
        }
        now = CheckAutoTimeOffset(now);
        now += TimeSpan.FromSeconds(Config.TimeOffset.Value);
        return now;
    }

    public static string PreciseTimeStr() => PreciseNow().ToString("yyyy-MM-dd HH:mm:ss");

    // 重启

    public static bool IsRestartPending() => Config.RestartPending;

    public static void RequestRestart()
    {
        Config.MarkRestartPending();
        Log.Info("请求重启应用: 关闭所有窗口并退出");
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IControlledApplicationLifetime)?.Shutdown();
    }

    public static void RestartSelf()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
        });
    }

    // FUI 图标

    private static readonly Dictionary<string, string> IconNameMap = new()
    {
        ["RIGHT_ARROW"] = "arrow_right", ["LEFT_ARROW"] = "arrow_left",
        ["UP"] = "chevron_up", ["DOWN"] = "chevron_down",
        ["ACCEPT"] = "checkmark", ["ADD"] = "add", ["DELETE"] = "delete",
        ["EDIT"] = "edit", ["SAVE"] = "save", ["CLOSE"] = "dismiss",
        ["PLAY"] = "play", ["PAUSE"] = "pause",
        ["HOME"] = "home", ["SETTING"] = "settings", ["INFO"] = "info",
        ["FOLDER"] = "folder", ["ALBUM"] = "album", ["DOWNLOAD"] = "arrow_download",
        ["PHOTO"] = "image", ["MESSAGE"] = "chat", ["LINK"] = "link",
        ["DATE_TIME"] = "clock", ["STOP_WATCH"] = "timer", ["HISTORY"] = "history",
        ["SYNC"] = "arrow_sync", ["VIDEO"] = "video", ["MUSIC"] = "music_note_2",
        ["APPLICATION"] = "apps", ["BRUSH"] = "paint_brush", ["PALETTE"] = "color",
        ["CLOUD"] = "cloud", ["BOOK_SHELF"] = "book", ["EDUCATION"] = "class",
        ["LANGUAGE"] = "local_language", ["TILES"] = "grid", ["LAYOUT"] = "layout_column_two",
        ["UPDATE"] = "arrow_sync", ["DEVELOPER_TOOLS"] = "window_dev_tools", ["CODE"] = "code",
        ["GAUGE"] = "gauge", ["GLOBE"] = "globe", ["SEARCH"] = "search",
        ["PEOPLE"] = "people", ["DOCUMENT"] = "document", ["HEART"] = "heart",
        ["CALENDAR"] = "calendar", ["PEN"] = "pen", ["ERASER"] = "eraser",
        ["UNDO"] = "arrow_undo", ["MICROPHONE"] = "microphone",
    };

    public static string GetFluentIconPath(string iconName, bool isDark)
    {
        var mapped = IconNameMap.TryGetValue(iconName, out var name) ? name : iconName.ToLowerInvariant();
        var themeDir = isDark ? "dark" : "light";
        var path32 = Paths.GetResourcePath(Path.Combine("Assets", "fluent", themeDir, $"ic_fluent_{mapped}_32_regular.svg"));
        if (File.Exists(path32))
        {
            return path32;
        }
        return Paths.GetResourcePath(Path.Combine("Assets", "fluent", themeDir, $"ic_fluent_{mapped}_24_regular.svg"));
    }
}
