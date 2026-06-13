//  AppSettings.cs — JSON 配置
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClassLively_UI.Helpers;

public static class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        AppContext.BaseDirectory, "settings.json");

    private static Dictionary<string, object>? _cache;
    private static readonly object _lock = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>确保缓存已初始化</summary>
    private static Dictionary<string, object> Cache
    {
        get
        {
            lock (_lock)
            {
                return _cache ??= new Dictionary<string, object>();
            }
        }
    }

    /// <summary>获取配置值</summary>
    public static T? Get<T>(string key, T? defaultValue = default)
    {
        lock (_lock)
        {
            if (_cache == null) return defaultValue;
            if (!_cache.TryGetValue(key, out var raw)) return defaultValue;
            try
            {
                if (raw is JsonElement je)
                    return JsonSerializer.Deserialize<T>(je.GetRawText(), _jsonOptions);
                return (T?)Convert.ChangeType(raw, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    /// <summary>设置配置值写入缓存</summary>
    public static void Set(string key, object value)
    {
        lock (_lock)
        {
            Cache[key] = value;
        }
    }

    /// <summary>将当前缓存写入文件</summary>
    public static async Task SaveAsync()
    {
        try
        {
            Dictionary<string, object> snapshot;
            lock (_lock)
            {
                snapshot = new Dictionary<string, object>(_cache ?? new());
            }
            var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
            await File.WriteAllTextAsync(SettingsPath, json);
            Debug.WriteLine($"[AppSettings] 已保存 ({snapshot.Count} 项)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettings] 保存失败: {ex.Message}");
        }
    }

    /// <summary>从文件加载到缓存</summary>
    public static async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Debug.WriteLine("[AppSettings] 文件不存在");
                return;
            }

            var json = await File.ReadAllTextAsync(SettingsPath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions);

            lock (_lock)
            {
                _cache = dict ?? new Dictionary<string, object>();
            }
            Debug.WriteLine($"[AppSettings] 已加载 ({_cache.Count} 项)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppSettings] 加载失败: {ex.Message}");
            lock (_lock)
            {
                _cache ??= new Dictionary<string, object>();
            }
        }
    }

    public static bool GetBool(string key, bool defaultValue = false)
        => Get(key, defaultValue);

    public static int GetInt(string key, int defaultValue = 0)
        => Get(key, defaultValue);

    public static string GetString(string key, string defaultValue = "")
        => Get(key, defaultValue);

    /// <summary>获取列表类型配置值</summary>
    public static List<T> GetList<T>(string key)
    {
        lock (_lock)
        {
            if (_cache == null || !_cache.TryGetValue(key, out var raw))
                return new List<T>();

            try
            {
                if (raw is JsonElement je && je.ValueKind == JsonValueKind.Array)
                    return JsonSerializer.Deserialize<List<T>>(je.GetRawText(), _jsonOptions) ?? new();
                if (raw is IList<object> list)
                {
                    var result = new List<T>();
                    foreach (var item in list)
                    {
                        if (item is T t) result.Add(t);
                    }
                    return result;
                }
            }
            catch { }

            return new List<T>();
        }
    }

    /// <summary>获取所有配置的浅拷贝</summary>
    public static Dictionary<string, object> GetAll()
    {
        lock (_lock)
        {
            return new Dictionary<string, object>(_cache ?? new());
        }
    }

    /// <summary>检查键是否存在</summary>
    public static bool ContainsKey(string key)
    {
        lock (_lock)
        {
            return _cache?.ContainsKey(key) ?? false;
        }
    }
}
