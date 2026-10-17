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

// 插件系统
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Glimpseon.Core.Services;
using Glimpseon.UI;

namespace Glimpseon.Core;

public static class PluginApi
{
    public const int HostApiVersion = 1;

    /// <summary>0=未登记 视为兼容</summary>
    public static bool IsCompatible(int apiVersion) => apiVersion == 0 || apiVersion == HostApiVersion;

    // 事件名
    public const string EventTimeTick = "TimeTick";
    public const string EventCourseChanged = "CourseChanged";
    public const string EventClassStateChanged = "ClassStateChanged";
    public const string EventAppClosing = "AppClosing";
}

internal static class PluginFs
{
    /// <summary>解析到 root 内 越界返回 false</summary>
    public static bool TryResolveInside(string root, string? relative, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
        {
            return false;
        }
        string rootFull;
        string candidate;
        try
        {
            rootFull = Path.GetFullPath(root);
            candidate = Path.GetFullPath(Path.Combine(rootFull, relative));
        }
        catch
        {
            return false;
        }
        var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        full = candidate;
        return true;
    }

    /// <summary>http/https 绝对地址</summary>
    public static bool IsHttpUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

/// <summary>manifest 依赖项</summary>
public sealed class PluginDependency
{
    public string Id { get; init; } = "";
    public bool Required { get; init; } = true; // 必选
}

public sealed class PluginManifest
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string Homepage { get; init; } = "";
    public string Entrance { get; init; } = "";
    public int ApiVersion { get; init; }
    public string Icon { get; init; } = "";
    public string Readme { get; init; } = "";
    public List<PluginDependency> Dependencies { get; init; } = [];

    public static (PluginManifest? Manifest, string Error) Load(string dir, bool checkDirName = true)
    {
        var path = Path.Combine(dir, "manifest.json");
        if (!File.Exists(path))
        {
            return (null, $"manifest.json 缺失: {path}");
        }
        PluginManifest? m;
        try
        {
            m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip });
        }
        catch (Exception e)
        {
            return (null, $"manifest.json 解析失败: {e.Message}");
        }
        if (m is null)
        {
            return (null, "manifest.json 内容为空");
        }
        if (string.IsNullOrWhiteSpace(m.Id))
        {
            return (null, "manifest.id 为空");
        }
        if (string.IsNullOrWhiteSpace(m.Name))
        {
            return (null, "manifest.name 为空");
        }
        if (string.IsNullOrWhiteSpace(m.Version))
        {
            // 不拒载, 但版本为空会让"可更新"判断失真
            Log.Warning($"[插件] {m.Id} manifest.version 为空 更新检测会不可靠");
        }
        if (!PluginService.IsSafePluginId(m.Id))
        {
            return (null, $"manifest.id 非法: {m.Id}");
        }
        if (checkDirName && m.Id != Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
        {
            return (null, $"manifest.id ({m.Id}) 与目录名 ({Path.GetFileName(dir)}) 不一致");
        }
        if (!PluginFs.TryResolveInside(dir, m.Entrance, out var entranceFull) || !File.Exists(entranceFull))
        {
            return (null, $"manifest.entrance 不在插件目录内或不存在: {m.Entrance}");
        }
        if (!PluginApi.IsCompatible(m.ApiVersion))
        {
            return (null, $"apiVersion {m.ApiVersion} 不受支持 宿主支持 {PluginApi.HostApiVersion}");
        }
        return (m, "");
    }
}

public abstract class GlimpseonPlugin
{
    public PluginContext Context { get; internal set; } = null!;
    public abstract void OnLoad();
    public virtual void OnUnload() { }
}

public sealed class PluginContext
{
    private readonly string _configPath;

    public string Id { get; }
    /// <summary>插件目录</summary>
    public string PluginDirectory { get; }
    public int ApiVersion { get; }

    internal PluginContext(PluginManifest manifest, string directory)
    {
        Id = manifest.Id;
        PluginDirectory = directory;
        ApiVersion = manifest.ApiVersion;
        _configPath = Path.Combine(directory, "config.json");
    }

    public void LogInfo(string message) => Log.Info($"[plugin:{Id}] {message}");
    public void LogWarning(string message) => Log.Warning($"[plugin:{Id}] {message}");
    public void LogError(string message) => Log.Error($"[plugin:{Id}] {message}");

    public JsonObject ReadConfig()
    {
        try
        {
            if (File.Exists(_configPath) && JsonNode.Parse(File.ReadAllText(_configPath)) is JsonObject obj)
            {
                return obj;
            }
        }
        catch (Exception e)
        {
            LogWarning($"config.json 读取失败 用空对象: {e.Message}");
        }
        return new JsonObject();
    }

    public void WriteConfig(JsonObject config)
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(_configPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            LogError($"config.json 写入失败: {e.Message}");
        }
    }

    public void RegisterComponent(ComponentDefinition definition, Func<JsonElement?, Control> factory)
        => PluginService.RegisterComponent(Id, definition, factory);

    public void RegisterScheduleProvider(string name, Func<List<ScheduleRow>> provider)
        => PluginService.RegisterScheduleProvider(Id, name, provider);

    public void PushAnnouncement(string text) => Announcements.Append(text);

    // 事件订阅

    public void SubscribeTimeTick(Action<DateTime> handler)
        => PluginEventBus.Add(new PluginSubscription { PluginId = Id, Event = PluginApi.EventTimeTick, Handler = p => handler((DateTime)p!) });

    public void SubscribeCourseChanged(Action<ScheduleRow?> handler)
        => PluginEventBus.Add(new PluginSubscription { PluginId = Id, Event = PluginApi.EventCourseChanged, Handler = p => handler(p as ScheduleRow) });

    public void SubscribeClassStateChanged(Action<PluginClassState> handler)
        => PluginEventBus.Add(new PluginSubscription { PluginId = Id, Event = PluginApi.EventClassStateChanged, Handler = p => handler((PluginClassState)p!) });

    public void SubscribeAppClosing(Action handler)
        => PluginEventBus.Add(new PluginSubscription { PluginId = Id, Event = PluginApi.EventAppClosing, Handler = _ => handler() });

    // 状态拉取

    public DateTime GetNow() => AppUtils.PreciseNow();

    public List<ScheduleRow> GetTodaySchedule() => TimetableScheduleProvider.GetTodaySchedule();

    public ScheduleRow? GetCurrentCourse()
        => TimetableScheduleProvider.GetTodaySchedule().FirstOrDefault(r => r.IsCurrent);

    public PluginClassState? GetClassState() => PluginEventBus.LatestClassState;

    public JsonElement? GetWeather() => AppUtils.GetCachedContent("weather");

    public string? GetPoetry()
    {
        if (AppUtils.GetCachedContent("poetry") is not { } poetry)
        {
            return null;
        }
        return poetry.ValueKind == JsonValueKind.String ? poetry.GetString()
            : poetry.ValueKind == JsonValueKind.Object && poetry.TryGetProperty("text", out var t) ? t.GetString()
            : poetry.ToString();
    }

    public string? GetWallpaperPath()
    {
        if (AppUtils.GetCachedContent("wallpaper", ignoreExpiry: true) is not { } wall
            || wall.ValueKind != JsonValueKind.Object
            || !wall.TryGetProperty("path", out var p)
            || p.GetString() is not { } path)
        {
            return null;
        }
        return File.Exists(path) ? path : null;
    }

    // 通知

    private static readonly NotificationManager NotifManager = new();
    private static readonly TimeSpan NotifyMinInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan NotifyDedupeWindow = TimeSpan.FromSeconds(3);
    private readonly object _notifyLock = new();
    private DateTime _lastNotifyAt = DateTime.MinValue;
    private string _lastNotifyContent = "";

    /// <summary>弹通知 style: corner/scroll/fullscreen</summary>
    /// <param name="speak">是否朗读</param>
    public void ShowNotification(string content, int durationSeconds = 5, string style = "corner", bool speak = false)
    {
        content = (content ?? "").Trim();
        if (content.Length == 0)
        {
            LogWarning("通知内容为空 跳过");
            return;
        }
        var now = AppUtils.PreciseNow();
        lock (_notifyLock)
        {
            if (now - _lastNotifyAt < NotifyMinInterval)
            {
                LogWarning("通知过于频繁 已丢弃");
                return;
            }
            if (content == _lastNotifyContent && now - _lastNotifyAt < NotifyDedupeWindow)
            {
                return;
            }
            _lastNotifyAt = now;
            _lastNotifyContent = content;
        }
        var type = (style ?? "").Trim().ToLowerInvariant() switch
        {
            NotifType.Scroll => NotifType.Scroll,
            NotifType.Fullscreen => NotifType.Fullscreen,
            _ => NotifType.Corner,
        };
        NotifManager.HandleNotification(new NotificationRequest
        {
            Type = type,
            Content = content,
            Duration = Math.Clamp(durationSeconds, 1, 120),
            // "done" = 关闭 TTS
            TtsVoice = speak ? "zh-CN-XiaoxiaoNeural" : "done",
        });
        // 按配置同步到公告板
        if (Config.NotificationSyncBoard.Value)
        {
            Announcements.Append(content);
        }
    }
}

public enum PluginLoadStatus
{
    NotLoaded,
    Loaded,
    Disabled,
    Error,
}

public sealed class LoadedPlugin
{
    public required PluginManifest Manifest { get; init; }
    public required string Directory { get; init; }
    public required bool Enabled { get; set; }
    public PluginLoadStatus Status { get; set; } = PluginLoadStatus.NotLoaded;
    public GlimpseonPlugin? Instance { get; set; }
    public string? Error { get; set; }
}

public sealed class PluginInfo
{
    public string Id { get; init; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Homepage { get; set; } = "";
    public string? IconSource { get; set; }
    public string? ReadmeSource { get; set; }
    public string? LocalVersion { get; set; }
    public string? MarketVersion { get; set; }

    /// <summary>元数据已就绪</summary>
    public bool MetadataReady { get; set; }
    /// <summary>等待回源</summary>
    public bool MetaPending { get; set; }
    public bool IsIncompatible { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public bool PendingRestart { get; set; }
    public bool Enabled { get; set; }
    public PluginLoadStatus Status { get; set; }
    public string? Error { get; set; }
    public string? Directory { get; set; }
    public List<PluginDependency> Dependencies { get; set; } = [];
    public LoadedPlugin? Local { get; set; }
    public MarketPlugin? Market { get; set; }

    public bool IsLocal => Local is not null;
    public bool IsMarket => Market is not null;
}

public static class PluginCatalog
{
    private static List<PluginInfo> _items = new();

    public static IReadOnlyList<PluginInfo> Items => _items;

    public static void Reload(IReadOnlyList<MarketPlugin>? market)
    {
        var map = new Dictionary<string, PluginInfo>(StringComparer.Ordinal);
        foreach (var lp in PluginService.Plugins)
        {
            map[lp.Manifest.Id] = BuildLocal(lp);
        }
        if (market is not null)
        {
            foreach (var mp in market)
            {
                if (!map.TryGetValue(mp.Id, out var info))
                {
                    info = new PluginInfo { Id = mp.Id };
                    map[info.Id] = info;
                }
                MergeMarket(info, mp);
            }
        }
        _items = map.Values
            .OrderByDescending(x => x.IsLocal)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static PluginInfo BuildLocal(LoadedPlugin lp)
    {
        var (icon, readme) = ResolveLocalAssets(lp);
        return new PluginInfo
        {
            Id = lp.Manifest.Id,
            Local = lp,
            Name = lp.Manifest.Name,
            Author = lp.Manifest.Author,
            Description = lp.Manifest.Description,
            Homepage = lp.Manifest.Homepage,
            LocalVersion = lp.Manifest.Version,
            Enabled = lp.Enabled,
            Status = lp.Status,
            Error = lp.Error,
            Directory = lp.Directory,
            Dependencies = lp.Manifest.Dependencies,
            IconSource = icon,
            ReadmeSource = readme,
            MetadataReady = true,
        };
    }

    private static void MergeMarket(PluginInfo info, MarketPlugin mp)
    {
        info.Market = mp;
        var meta = mp.Meta;
        // 0=未登记 视为兼容
        var marketApi = meta is { ApiVersion: > 0 } ? meta.ApiVersion : mp.LatestApiVersion;
        info.MarketVersion = !string.IsNullOrWhiteSpace(mp.LatestVersion) ? mp.LatestVersion : meta?.Version ?? "";
        info.IsIncompatible = !PluginApi.IsCompatible(marketApi);
        info.MetaPending = meta is null && !PluginMetaStore.IsNotFound(mp.Id) && !PluginMetaStore.EnrichmentFinished;
        if (info.IsLocal)
        {
            info.IsUpdateAvailable = !info.IsIncompatible
                && PluginMarket.CompareVersions(info.MarketVersion, info.LocalVersion ?? "0") > 0;
            info.PendingRestart |= PluginService.PendingRestartIds.Contains(info.Id);
            // 缺字段用索引/回源补
            if (string.IsNullOrEmpty(info.Homepage)) info.Homepage = mp.Homepage;
            if (string.IsNullOrEmpty(info.Author)) info.Author = mp.Author;
            if (string.IsNullOrEmpty(info.Description)) info.Description = mp.Description;
            if (string.IsNullOrEmpty(info.IconSource)) info.IconSource = meta?.IconUrl ?? mp.IconUrl;
            if (string.IsNullOrEmpty(info.ReadmeSource)) info.ReadmeSource = meta?.ReadmeUrl ?? mp.ReadmeUrl;
            return;
        }
        // 市场条目 以项目 manifest 为准
        info.Name = !string.IsNullOrWhiteSpace(meta?.Name) ? meta!.Name! : mp.Name;
        info.MetadataReady = !string.IsNullOrWhiteSpace(info.Name);
        info.Author = !string.IsNullOrWhiteSpace(meta?.Author) ? meta!.Author! : mp.Author;
        info.Description = !string.IsNullOrWhiteSpace(meta?.Description) ? meta!.Description! : mp.Description;
        info.Homepage = mp.Homepage;
        info.IconSource = !string.IsNullOrWhiteSpace(meta?.IconUrl) ? meta!.IconUrl : mp.IconUrl;
        info.ReadmeSource = !string.IsNullOrWhiteSpace(meta?.ReadmeUrl) ? meta!.ReadmeUrl : mp.ReadmeUrl;
        info.PendingRestart = PluginService.PendingRestartIds.Contains(info.Id) || PluginService.IsInstalledOnDisk(info.Id);
    }

    public static PluginInfo? Find(string id) => _items.FirstOrDefault(x => x.Id == id);

    // 本地图标/说明 按 mtime 缓存

    private static readonly string[] IconFileCandidates = { "icon.png", "icon.jpg", "icon.jpeg", "icon.ico", "logo.png" };
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, string? Icon, string? Readme)> LocalAssetCache = new(StringComparer.OrdinalIgnoreCase);

    private static (string? Icon, string? Readme) ResolveLocalAssets(LoadedPlugin lp)
    {
        DateTime stamp;
        try
        {
            stamp = Directory.GetLastWriteTimeUtc(lp.Directory);
        }
        catch
        {
            stamp = DateTime.MinValue;
        }
        if (LocalAssetCache.TryGetValue(lp.Directory, out var cached) && cached.Stamp == stamp)
        {
            return (cached.Icon, cached.Readme);
        }
        var icon = ResolveLocalAsset(lp, lp.Manifest.Icon, IconFileCandidates);
        var readme = ResolveLocalAsset(lp, lp.Manifest.Readme, ["README.md", "readme.md"]);
        LocalAssetCache[lp.Directory] = (stamp, icon, readme);
        return (icon, readme);
    }

    private static string? ResolveLocalAsset(LoadedPlugin lp, string? declared, string[] fallbacks)
    {
        if (!string.IsNullOrWhiteSpace(declared))
        {
            if (PluginFs.IsHttpUrl(declared))
            {
                return declared;
            }
            // 限定插件目录内
            if (PluginFs.TryResolveInside(lp.Directory, declared, out var path) && File.Exists(path))
            {
                return path;
            }
        }
        // 兜底常见文件名
        foreach (var candidate in fallbacks)
        {
            if (PluginFs.TryResolveInside(lp.Directory, candidate, out var path) && File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }
}

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly IReadOnlyList<PluginDependency> _dependencies;

    public PluginLoadContext(string pluginId, IReadOnlyList<PluginDependency> dependencies, string entranceDll)
        : base($"plugin:{pluginId}")
    {
        _dependencies = dependencies;
        _resolver = new AssemblyDependencyResolver(entranceDll);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        if (IsHostAssembly(name.Name))
        {
            // 回落 Default ALC 保证类型同一
            return null;
        }
        foreach (var dep in _dependencies)
        {
            if (!PluginService.LoadContexts.TryGetValue(dep.Id, out var context))
            {
                continue;
            }
            var shared = context.Load(name);
            if (shared is not null)
            {
                return shared;
            }
        }
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    /// <summary>宿主/框架/UI 栈程序集</summary>
    private static bool IsHostAssembly(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return true;
        }
        if (name == "GlimpseonMain" || name == "netstandard" || name == "mscorlib" || name == "Microsoft.CSharp")
        {
            return true;
        }
        // 框架 + Avalonia/Skia 生态
        return name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("System", StringComparison.Ordinal) && name.Length == 6
            || name.StartsWith("Avalonia", StringComparison.Ordinal)
            || name.StartsWith("FluentAvalonia", StringComparison.Ordinal)
            || name.StartsWith("SkiaSharp", StringComparison.Ordinal)
            || name.StartsWith("HarfBuzzSharp", StringComparison.Ordinal)
            || name is "MicroCom.Runtime" or "Tmds.DBus.Protocol" or "WinRT.Runtime"
            || name.StartsWith("Microsoft.Windows.SDK.NET", StringComparison.Ordinal)
            || name.StartsWith("ColorTextBlock.Avalonia", StringComparison.Ordinal)
            || name.StartsWith("Markdown.Avalonia", StringComparison.Ordinal)
            || name.StartsWith("Svg.", StringComparison.Ordinal);
    }
}

public enum PluginOperationCode
{
    Ok,
    Pending,
    ShaMismatch,
    Network,
    InvalidPackage,
    Io,
}

public sealed record PluginResult(bool Ok, string Message, PluginOperationCode Code)
{
    public static PluginResult Success(string message = "") => new(true, message, PluginOperationCode.Ok);
    public static PluginResult Pending(string message) => new(true, message, PluginOperationCode.Pending);
    public static PluginResult Fail(string message, PluginOperationCode code = PluginOperationCode.Io) => new(false, message, code);
}

public static class PluginService
{
    private static readonly List<LoadedPlugin> PluginList = new();
    private static readonly List<ComponentDefinition> ComponentDefinitions = new();
    private static readonly Dictionary<string, Func<JsonElement?, Control>> ComponentFactories = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (string PluginId, string Name, Func<List<ScheduleRow>> Provider)> Providers = new(StringComparer.Ordinal);

    /// <summary>已扫描插件 按目录名序</summary>
    public static IReadOnlyList<LoadedPlugin> Plugins => PluginList;

    /// <summary>插件组件定义</summary>
    public static IReadOnlyList<ComponentDefinition> PluginComponentDefinitions => ComponentDefinitions;

    /// <summary>课表提供者 providerKey → 条目</summary>
    public static IReadOnlyDictionary<string, (string PluginId, string Name, Func<List<ScheduleRow>> Provider)> ScheduleProviders => Providers;

    /// <summary>待重启生效的插件 id</summary>
    public static HashSet<string> PendingRestartIds { get; } = new(StringComparer.Ordinal);

    internal static Dictionary<string, PluginLoadContext> LoadContexts { get; } = new(StringComparer.Ordinal);

    internal static string EnabledMapPath => Path.Combine(Paths.DataConfig, "plugins.json");
    internal static string PackagesCacheDir => Path.Combine(Paths.DataCache, "plugin-packages");

    // 加载

    public static void InitializePlugins()
    {
        ResetRuntime();
        try
        {
            Directory.CreateDirectory(Paths.DataPlugins);
        }
        catch (Exception e)
        {
            Log.Error($"[插件] 插件目录创建失败: {e.Message}");
            return;
        }

        CleanupLeftovers();
        ProcessPendingPackages();
        ProcessUninstallMarkers();

        var enabledMap = ReadEnabledMap();
        List<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(Paths.DataPlugins).OrderBy(Path.GetFileName).ToList();
        }
        catch (Exception e)
        {
            Log.Error($"[插件] 插件目录扫描失败: {e.Message}");
            return;
        }
        foreach (var dir in dirs)
        {
            try
            {
                PluginList.Add(PreprocessPlugin(dir, enabledMap));
            }
            catch (Exception e)
            {
                Log.Error($"[插件] 预处理异常 {Path.GetFileName(dir)}: {e.Message}");
                PluginList.Add(new LoadedPlugin
                {
                    Manifest = new PluginManifest { Id = Path.GetFileName(dir), Name = Path.GetFileName(dir) },
                    Directory = dir,
                    Enabled = true,
                    Status = PluginLoadStatus.Error,
                    Error = $"预处理异常: {e.Message}",
                });
            }
        }

        var loadOrder = ResolveLoadOrder(PluginList.Where(x => x.Status == PluginLoadStatus.NotLoaded).ToList());
        Log.Info($"[插件] 加载顺序: {string.Join(" -> ", loadOrder)}");
        foreach (var id in loadOrder)
        {
            var plugin = PluginList.FirstOrDefault(x => x.Manifest.Id == id);
            if (plugin is null)
            {
                continue;
            }
            // 单插件异常不外溢
            try
            {
                LoadPlugin(plugin);
            }
            catch (Exception e)
            {
                plugin.Status = PluginLoadStatus.Error;
                plugin.Error = $"加载异常: {e.Message}";
                Log.Error($"[插件] {id} 加载异常 {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }

        var loaded = PluginList.Count(x => x.Status == PluginLoadStatus.Loaded);
        var failed = PluginList.Count(x => x.Status == PluginLoadStatus.Error);
        var disabled = PluginList.Count(x => x.Status == PluginLoadStatus.Disabled);
        Log.Info($"[插件] 扫描 {dirs.Count} 加载成功 {loaded} 失败 {failed} 未启用 {disabled}");
    }

    /// <summary>清空运行期注册物</summary>
    private static void ResetRuntime()
    {
        PluginList.Clear();
        ComponentDefinitions.Clear();
        ComponentFactories.Clear();
        Providers.Clear();
        LoadContexts.Clear();
        PendingRestartIds.Clear();
        PluginEventBus.RemoveAll();
    }

    /// <summary>退出收尾 AppClosing/OnUnload</summary>
    public static void ShutdownPlugins()
    {
        try
        {
            PluginEventBus.RaiseAppClosing();
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] AppClosing 派发失败: {e.Message}");
        }
        foreach (var lp in PluginList)
        {
            if (lp.Instance is not { } instance)
            {
                continue;
            }
            try
            {
                instance.OnUnload();
            }
            catch (Exception e)
            {
                Log.Warning($"[插件] {lp.Manifest.Id} OnUnload 失败: {e.Message}");
            }
            lp.Instance = null;
        }
        PluginEventBus.RemoveAll();
    }

    private static void ProcessPendingPackages()
    {
        try
        {
            foreach (var pkg in Directory.EnumerateFiles(Paths.DataPlugins, "*.gplugin").ToList())
            {
                var ok = false;
                try
                {
                    var result = InstallPackage(pkg);
                    ok = result.Ok;
                    Log.Info($"[插件] 启动期安装 {Path.GetFileName(pkg)}: {(ok ? $"成功 {result.Message}" : $"失败 {result.Message}")}");
                }
                catch (Exception e)
                {
                    Log.Warning($"[插件] 启动期安装异常 {Path.GetFileName(pkg)}: {e.Message}");
                }
                if (ok)
                {
                    TryDeleteFile(pkg);
                }
                else
                {
                    try
                    {
                        File.Move(pkg, pkg + ".failed", true);
                        Log.Warning($"[插件] 安装失败 已保留待排查: {Path.GetFileName(pkg)}.failed");
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] 待安装包扫描失败: {e.Message}");
        }
    }

    // .uninstall 
    private static void ProcessUninstallMarkers()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Paths.DataPlugins).ToList())
            {
                if (!File.Exists(Path.Combine(dir, ".uninstall")))
                {
                    continue;
                }
                try
                {
                    Directory.Delete(dir, true);
                    Log.Info($"[插件] 已处理卸载标记 {Path.GetFileName(dir)}");
                }
                catch (Exception e)
                {
                    Log.Warning($"[插件] 卸载标记目录删除失败 留待下次启动: {Path.GetFileName(dir)} {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] 卸载标记扫描失败: {e.Message}");
        }
    }

    private static LoadedPlugin PreprocessPlugin(string dir, Dictionary<string, bool> enabledMap)
    {
        var dirName = Path.GetFileName(dir);
        var (manifest, error) = PluginManifest.Load(dir);
        if (manifest is null)
        {
            Log.Warning($"[插件] {dirName} 拒载: {error}");
            return new LoadedPlugin
            {
                Manifest = new PluginManifest { Id = dirName, Name = dirName },
                Directory = dir,
                Enabled = true,
                Status = PluginLoadStatus.Error,
                Error = error,
            };
        }
        var enabled = !enabledMap.TryGetValue(manifest.Id, out var en) || en;
        if (!enabled)
        {
            Log.Info($"[插件] {manifest.Id} 已禁用 跳过加载");
            return new LoadedPlugin
            {
                Manifest = manifest,
                Directory = dir,
                Enabled = false,
                Status = PluginLoadStatus.Disabled,
            };
        }
        return new LoadedPlugin
        {
            Manifest = manifest,
            Directory = dir,
            Enabled = true,
            Status = PluginLoadStatus.NotLoaded,
        };
    }

    private static List<string> ResolveLoadOrder(List<LoadedPlugin> plugins)
    {
        var nodes = plugins.ToDictionary(x => x.Manifest.Id, x => new DependencyNode(x));
        foreach (var node in nodes.Values)
        {
            ResolveDependencyNode(nodes, node, []);
        }
        return nodes.Values
            .Where(x => x.Plugin.Status == PluginLoadStatus.NotLoaded)
            .OrderBy(x => x.Depth)
            .Select(x => x.Plugin.Manifest.Id)
            .ToList();
    }

    private sealed class DependencyNode(LoadedPlugin plugin)
    {
        public LoadedPlugin Plugin { get; } = plugin;
        public int Depth { get; set; }
        public bool Discovered { get; set; }
    }

    private static void ResolveDependencyNode(Dictionary<string, DependencyNode> nodes, DependencyNode node, List<DependencyNode> walking)
    {
        // 循环判断须在 Discovered 之前
        if (walking.Contains(node))
        {
            var chain = string.Join(" -> ", walking.Select(x => x.Plugin.Manifest.Id));
            node.Plugin.Status = PluginLoadStatus.Error;
            node.Plugin.Error = $"循环依赖: {chain} -> {node.Plugin.Manifest.Id}";
            Log.Warning($"[插件] {node.Plugin.Manifest.Id} {node.Plugin.Error}");
            return;
        }
        if (node.Discovered)
        {
            return;
        }
        node.Discovered = true;
        walking.Add(node);
        try
        {
            var depth = 0;
            foreach (var dep in node.Plugin.Manifest.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(dep.Id))
                {
                    continue;
                }
                if (!nodes.TryGetValue(dep.Id, out var dependency) || dependency.Plugin.Status != PluginLoadStatus.NotLoaded)
                {
                    if (dep.Required)
                    {
                        node.Plugin.Status = PluginLoadStatus.Error;
                        node.Plugin.Error = $"依赖的必选插件 {dep.Id} 不存在或不可加载";
                        Log.Warning($"[插件] {node.Plugin.Manifest.Id} {node.Plugin.Error}");
                        return;
                    }
                    continue;
                }
                ResolveDependencyNode(nodes, dependency, walking);
                if (dependency.Plugin.Status == PluginLoadStatus.NotLoaded)
                {
                    depth = Math.Max(depth, dependency.Depth);
                }
                else if (dep.Required)
                {
                    node.Plugin.Status = PluginLoadStatus.Error;
                    node.Plugin.Error = $"依赖的必选插件 {dep.Id} 加载失败";
                    Log.Warning($"[插件] {node.Plugin.Manifest.Id} {node.Plugin.Error}");
                    return;
                }
            }
            node.Depth = depth + 1;
        }
        finally
        {
            walking.Remove(node);
        }
    }

    private static void LoadPlugin(LoadedPlugin lp)
    {
        var manifest = lp.Manifest;
        if (!PluginFs.TryResolveInside(lp.Directory, manifest.Entrance, out var dllPath) || !File.Exists(dllPath))
        {
            lp.Status = PluginLoadStatus.Error;
            lp.Error = $"入口程序集不存在或不在插件目录内: {manifest.Entrance}";
            Log.Warning($"[插件] {manifest.Id} {lp.Error}");
            return;
        }
        PluginLoadContext alc;
        try
        {
            // deps.json 损坏会在此抛异常
            alc = new PluginLoadContext(manifest.Id, manifest.Dependencies, dllPath);
        }
        catch (Exception e)
        {
            lp.Status = PluginLoadStatus.Error;
            lp.Error = $"依赖解析器初始化失败 (deps.json 可能损坏): {e.Message}";
            Log.Warning($"[插件] {manifest.Id} {lp.Error}");
            return;
        }
        Assembly asm;
        try
        {
            asm = alc.LoadFromAssemblyPath(dllPath);
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] {manifest.Id} 程序集加载失败: {e.Message}");
            lp.Status = PluginLoadStatus.Error;
            lp.Error = $"程序集加载失败: {e.Message}";
            return;
        }
        Type? type;
        try
        {
            type = asm.GetTypes().FirstOrDefault(IsPluginType);
        }
        catch (ReflectionTypeLoadException rtle)
        {
            type = rtle.Types.Where(t => t is not null).Cast<Type>().FirstOrDefault(IsPluginType);
            if (type is null)
            {
                var reasons = string.Join(" | ", rtle.LoaderExceptions
                    .Where(e => e is not null)
                    .Select(e => e!.Message)
                    .Distinct()
                    .Take(3));
                Log.Warning($"[插件] {manifest.Id} 类型加载失败: {reasons}");
            }
        }
        catch (Exception e)
        {
            type = null;
            Log.Warning($"[插件] {manifest.Id} 类型扫描失败: {e.Message}");
        }
        if (type is null)
        {
            lp.Status = PluginLoadStatus.Error;
            lp.Error = "未找到 GlimpseonPlugin 派生类 (需要 public 无参构造)";
            return;
        }
        GlimpseonPlugin instance;
        try
        {
            instance = (GlimpseonPlugin)Activator.CreateInstance(type)!;
        }
        catch (Exception e)
        {
            lp.Status = PluginLoadStatus.Error;
            lp.Error = $"实例化失败: {e.Message}";
            return;
        }
        instance.Context = new PluginContext(manifest, lp.Directory);
        try
        {
            instance.OnLoad();
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] {manifest.Id} OnLoad 失败: {e}");
            // 回滚半初始化的注册物
            UnregisterPluginRuntime(manifest.Id);
            try
            {
                instance.OnUnload();
            }
            catch
            {
            }
            lp.Status = PluginLoadStatus.Error;
            lp.Instance = null;
            lp.Error = $"OnLoad 失败: {e.Message}";
            return;
        }
        lp.Status = PluginLoadStatus.Loaded;
        lp.Instance = instance;
        lp.Error = null;
        LoadContexts[manifest.Id] = alc;
    }

    private static bool IsPluginType(Type? t) =>
        t is not null && !t.IsAbstract && typeof(GlimpseonPlugin).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null;

    // 注册表

    private static readonly Dictionary<string, string> ComponentOwners = new(StringComparer.Ordinal);

    public static void RegisterComponent(string pluginId, ComponentDefinition definition, Func<JsonElement?, Control> factory)
    {
        var id = definition?.Id?.Trim() ?? "";
        if (id.Length == 0)
        {
            Log.Warning($"[plugin:{pluginId}] 组件定义 Id 为空 跳过注册");
            return;
        }
        // 存档按 type_style 还原 缺下划线会丢
        var underscore = id.IndexOf('_');
        if (underscore <= 0 || underscore == id.Length - 1)
        {
            Log.Warning($"[plugin:{pluginId}] 组件 Id \"{id}\" 不符合 <type>_<style> 命名 跳过注册");
            return;
        }
        if (factory is null)
        {
            Log.Warning($"[plugin:{pluginId}] 组件 {id} 工厂为空 跳过注册");
            return;
        }
        ComponentDefinitions.RemoveAll(d => d.Id == id);
        ComponentDefinitions.Add(definition!);
        ComponentFactories[id] = factory;
        ComponentOwners[id] = pluginId;
        Log.Info($"[plugin:{pluginId}] 注册组件 {id}");
    }

    public static void RegisterScheduleProvider(string pluginId, string name, Func<List<ScheduleRow>> provider)
    {
        if (provider is null)
        {
            Log.Warning($"[plugin:{pluginId}] 课表提供者委托为空 跳过注册");
            return;
        }
        name = string.IsNullOrWhiteSpace(name) ? pluginId : name.Trim();
        var key = BuildProviderKey(pluginId, name);
        Providers[key] = (pluginId, name, provider);
        Log.Info($"[plugin:{pluginId}] 注册课表提供者 {name} (key={key})");
    }

    /// <summary>providerKey = pluginId:name</summary>
    public static string BuildProviderKey(string pluginId, string name) => $"{pluginId}:{name}";

    /// <summary>注销插件的全部注册物</summary>
    public static void UnregisterPluginRuntime(string pluginId)
    {
        foreach (var key in Providers.Where(kv => kv.Value.PluginId == pluginId).Select(kv => kv.Key).ToList())
        {
            Providers.Remove(key);
        }
        var owned = ComponentOwners.Where(kv => kv.Value == pluginId).Select(kv => kv.Key).ToList();
        foreach (var id in owned)
        {
            ComponentFactories.Remove(id);
            ComponentDefinitions.RemoveAll(d => d.Id == id);
            ComponentOwners.Remove(id);
        }
        PluginEventBus.RemovePlugin(pluginId);
        Log.Info($"[插件] 已清理 {pluginId} 的运行期注册 (组件 {owned.Count} 个)");
    }

    public static DraggableContainer? CreatePluginWidget(string typeId, ComponentDefinition definition, JsonElement? config)
    {
        if (!ComponentFactories.TryGetValue(typeId, out var factory))
        {
            return null;
        }
        try
        {
            var content = factory(config);
            if (content is null)
            {
                return null;
            }
            return new DraggableContainer
            {
                Content = content,
                Width = definition.DefaultWidth,
                Height = definition.DefaultHeight,
            };
        }
        catch (Exception e)
        {
            Log.Warning($"[plugin] 组件工厂失败 {typeId}: {e.Message}");
            return null;
        }
    }

    // 启停 data/config/plugins.json

    public static Dictionary<string, bool> ReadEnabledMap()
    {
        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(EnabledMapPath) && JsonNode.Parse(File.ReadAllText(EnabledMapPath)) is { } root
                && root["enabled"] is JsonObject enabled)
            {
                foreach (var kv in enabled)
                {
                    if (kv.Key is not null && kv.Value is not null)
                    {
                        map[kv.Key] = kv.Value.GetValueKind() == JsonValueKind.True;
                    }
                }
            }
        }
        catch (Exception e)
        {
            // 损坏则备份 缺省启用
            Log.Error($"[插件] plugins.json 读取失败 已备份并视为全启用: {e.Message}");
            TryBackup(EnabledMapPath);
        }
        return map;
    }

    private static void TryBackup(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }
            var backup = $"{path}.corrupt_{DateTime.Now:yyyyMMdd_HHmmss}";
            File.Move(path, backup, true);
            Log.Warning($"[插件] 已备份损坏配置: {Path.GetFileName(backup)}");
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] 损坏配置备份失败: {e.Message}");
        }
    }

    private static void WriteEnabledMap(Dictionary<string, bool> map)
    {
        try
        {
            var enabled = new JsonObject();
            foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                enabled[kv.Key] = kv.Value;
            }
            var root = new JsonObject { ["enabled"] = enabled };
            Directory.CreateDirectory(Paths.DataConfig);
            File.WriteAllText(EnabledMapPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Error($"[插件] plugins.json 写入失败: {e.Message}");
        }
    }

    public static void SetEnabled(string pluginId, bool enabled)
    {
        var map = ReadEnabledMap();
        map[pluginId] = enabled;
        WriteEnabledMap(map);
        Log.Info($"[插件] {pluginId} 启停={enabled} 重启后生效");
    }

    // 安装卸载

    private const int MaxPackageEntries = 4096;
    private const long MaxEntryBytes = 128L * 1024 * 1024;
    private const long MaxTotalBytes = 512L * 1024 * 1024;
    private const long MaxCompressionRatio = 1000;

    public static PluginResult InstallPackage(string packagePath)
    {
        string? staging = null;
        long totalBytes = 0;
        try
        {
            using var zip = ZipFile.OpenRead(packagePath);
            if (zip.Entries.Count > MaxPackageEntries)
            {
                return PluginResult.Fail($"包内条目过多 ({zip.Entries.Count} > {MaxPackageEntries})", PluginOperationCode.InvalidPackage);
            }
            var entries = zip.Entries
                .Select(e => (Entry: e, Name: e.FullName.Replace('\\', '/')))
                .Where(x => x.Name.Length > 0)
                .ToList();
            // 解压炸弹防护
            foreach (var (entry, name) in entries)
            {
                if (entry.Length > MaxEntryBytes)
                {
                    return PluginResult.Fail($"包内文件过大: {name} ({entry.Length / 1024 / 1024}MB)", PluginOperationCode.InvalidPackage);
                }
                if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxCompressionRatio)
                {
                    return PluginResult.Fail($"包内文件压缩比异常: {name}", PluginOperationCode.InvalidPackage);
                }
                totalBytes += entry.Length;
                if (totalBytes > MaxTotalBytes)
                {
                    return PluginResult.Fail($"解压后体积超限 (> {MaxTotalBytes / 1024 / 1024}MB)", PluginOperationCode.InvalidPackage);
                }
            }
            var entryNames = entries.Select(x => x.Name).ToList();
            var prefix = "";
            var manifestEntry = entries.FirstOrDefault(x => x.Name == "manifest.json").Entry;
            if (manifestEntry is null)
            {
                var firstSegs = entryNames.Select(n => n.Split('/')[0]).Distinct().ToList();
                if (firstSegs.Count == 1 && entryNames.Any(n => n == firstSegs[0] + "/manifest.json"))
                {
                    prefix = firstSegs[0] + "/";
                    manifestEntry = entries.First(x => x.Name == prefix + "manifest.json").Entry;
                }
            }
            if (manifestEntry is null)
            {
                return PluginResult.Fail("包内缺少 manifest.json", PluginOperationCode.InvalidPackage);
            }
            Directory.CreateDirectory(PackagesCacheDir);
            staging = Path.Combine(PackagesCacheDir, "stage_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(staging);
            var stagingFull = Path.GetFullPath(staging);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length <= prefix.Length || name.EndsWith('/'))
                {
                    continue;
                }
                var target = Path.GetFullPath(Path.Combine(staging, name[prefix.Length..]));
                if (!target.StartsWith(stagingFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"包内非法路径: {name}");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
        }
        catch (Exception e)
        {
            CleanupStaging(staging);
            return PluginResult.Fail($"解压失败: {e.Message}", PluginOperationCode.InvalidPackage);
        }
        var (manifest, error) = PluginManifest.Load(staging!, checkDirName: false);
        if (manifest is null)
        {
            CleanupStaging(staging);
            return PluginResult.Fail(error, PluginOperationCode.InvalidPackage);
        }
        var targetDir = Path.Combine(Paths.DataPlugins, manifest.Id);
        string? backup = null;
        // 覆盖安装保留私有配置
        try
        {
            var oldConfig = Path.Combine(targetDir, "config.json");
            var newConfig = Path.Combine(staging!, "config.json");
            if (File.Exists(oldConfig) && !File.Exists(newConfig))
            {
                File.Copy(oldConfig, newConfig);
                Log.Info($"[插件] {manifest.Id} 覆盖安装 保留原 config.json");
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] {manifest.Id} 保留 config.json 失败: {e.Message}");
        }
        try
        {
            if (Directory.Exists(targetDir))
            {
                backup = targetDir + ".bak_" + Guid.NewGuid().ToString("N")[..6];
                Directory.Move(targetDir, backup);
            }
            Directory.Move(staging!, targetDir);
        }
        catch (Exception e)
        {
            // 回滚备份
            if (backup is not null && Directory.Exists(backup) && !Directory.Exists(targetDir))
            {
                try
                {
                    Directory.Move(backup, targetDir);
                }
                catch
                {
                }
            }
            CleanupStaging(staging);
            return PluginResult.Fail($"安装失败: {e.Message}");
        }
        if (backup is not null)
        {
            try
            {
                Directory.Delete(backup, true);
            }
            catch (Exception e)
            {
                Log.Warning($"[插件] 旧版本备份删除失败 留待下次启动: {Path.GetFileName(backup)} {e.Message}");
            }
        }
        var map = ReadEnabledMap();
        // 覆盖安装保留用户原有启停设置
        if (!map.ContainsKey(manifest.Id))
        {
            map[manifest.Id] = true;
        }
        WriteEnabledMap(map);
        // 本会话不生效 标记待重启
        PendingRestartIds.Add(manifest.Id);
        Log.Info($"[插件] 已安装 {manifest.Id} v{manifest.Version} 重启后加载");
        return PluginResult.Success(manifest.Name);
    }

    public static PluginResult Uninstall(string pluginId)
    {
        if (!IsSafePluginId(pluginId))
        {
            return PluginResult.Fail($"插件 id 非法: {pluginId}", PluginOperationCode.InvalidPackage);
        }
        var dir = Path.Combine(Paths.DataPlugins, pluginId);
        if (!Directory.Exists(dir))
        {
            return PluginResult.Fail("插件目录不存在");
        }
        try
        {
            Directory.Delete(dir, true);
        }
        catch
        {
            try
            {
                File.WriteAllText(Path.Combine(dir, ".uninstall"), "");
                // dll 占用中 先停用 重启后删目录
                UnregisterPluginRuntime(pluginId);
                ForgetPlugin(pluginId);
                PendingRestartIds.Add(pluginId);
                Log.Info($"[插件] {pluginId} 已标记卸载 重启后自动删除");
                return PluginResult.Pending("pending");
            }
            catch (Exception e)
            {
                return PluginResult.Fail($"标记卸载失败: {e.Message}");
            }
        }
        UnregisterPluginRuntime(pluginId);
        ForgetPlugin(pluginId);
        Log.Info($"[插件] 已卸载 {pluginId}");
        return PluginResult.Success(pluginId);
    }

    /// <summary>插件目录是否已落盘</summary>
    public static bool IsInstalledOnDisk(string pluginId)
    {
        if (!IsSafePluginId(pluginId))
        {
            return false;
        }
        try
        {
            return Directory.Exists(Path.Combine(Paths.DataPlugins, pluginId));
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsSafePluginId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 128
        && id.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) < 0
        && id.Trim() == id
        && !id.EndsWith('.')
        && !ReservedNames.Contains(id)
        && id != "." && id != "..";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static void ForgetPlugin(string pluginId)
    {
        PluginList.RemoveAll(p => p.Manifest.Id == pluginId);
        var map = ReadEnabledMap();
        if (map.Remove(pluginId))
        {
            WriteEnabledMap(map);
        }
    }

    private static void CleanupLeftovers()
    {
        try
        {
            if (Directory.Exists(PackagesCacheDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(PackagesCacheDir, "stage_*").ToList())
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch
                    {
                    }
                }
            }
            foreach (var dir in Directory.EnumerateDirectories(Paths.DataPlugins, "*.bak_*").ToList())
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                }
            }
            // 清理装失败的包
            foreach (var failed in Directory.EnumerateFiles(Paths.DataPlugins, "*.gplugin.failed").ToList())
            {
                Log.Warning($"[插件] 清理上次安装失败的包: {Path.GetFileName(failed)}");
                TryDeleteFile(failed);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] 残留清理失败: {e.Message}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] 文件删除失败 {Path.GetFileName(path)}: {e.Message}");
        }
    }

    private static void CleanupStaging(string? staging)
    {
        if (staging is null || !Directory.Exists(staging))
        {
            return;
        }
        try
        {
            Directory.Delete(staging, true);
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] 暂存清理失败: {e.Message}");
        }
    }
}

/// <summary>
/// 索引仓库条目
/// </summary>
public sealed class MarketPlugin
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Homepage { get; init; } = "";
    public string ReadmeUrl { get; init; } = "";
    public string LatestVersion { get; init; } = "";
    public int LatestApiVersion { get; init; }
    public string DownloadUrl { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string PublishedAt { get; init; } = "";

    public string Asset { get; init; } = "";

    public string Repo { get; init; } = "";
    public string Branch { get; init; } = "main";
    public string ManifestPath { get; init; } = "manifest.json";

    public MarketMetadata? Meta { get; set; }

    private string AssetName => string.IsNullOrWhiteSpace(Asset) ? Id + ".gplugin" : Asset;

    /// <summary>下载地址候选</summary>
    public IReadOnlyList<string> DownloadCandidates()
    {
        var list = new List<string>();
        void Add(string url)
        {
            if (!string.IsNullOrWhiteSpace(url) && !list.Contains(url))
            {
                list.Add(url);
            }
        }
        if (!string.IsNullOrWhiteSpace(DownloadUrl))
        {
            Add(DownloadUrl);
            return list;
        }
        if (string.IsNullOrWhiteSpace(Repo))
        {
            return list;
        }
        // 按登记版本取 tag
        if (!string.IsNullOrWhiteSpace(LatestVersion))
        {
            Add($"https://github.com/{Repo}/releases/download/v{LatestVersion.TrimStart('v', 'V')}/{AssetName}");
        }
        // 作者可能没打 v 前缀
        if (!string.IsNullOrWhiteSpace(LatestVersion))
        {
            Add($"https://github.com/{Repo}/releases/download/{LatestVersion.TrimStart('v', 'V')}/{AssetName}");
        }
        Add($"https://github.com/{Repo}/releases/latest/download/{AssetName}");
        return list;
    }

    public string EffectiveDownloadUrl
    {
        get
        {
            var candidates = DownloadCandidates();
            return candidates.Count > 0 ? candidates[0] : "";
        }
    }
}

public sealed class MarketMetadata
{
    public string? Name { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
    public int ApiVersion { get; init; }
    public string? IconUrl { get; init; }
    public string? ReadmeUrl { get; init; }
    public string Source { get; init; } = "";
}

public static class PluginMarket
{
    private static string NormalizeRepo(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }
        var t = raw.Trim();
        if (t.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(t, UriKind.Absolute, out var uri))
        {
            var segs = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length >= 2)
            {
                return $"{segs[0]}/{segs[1].Replace(".git", "", StringComparison.OrdinalIgnoreCase)}";
            }
            return "";
        }
        t = t.Trim('/');
        if (t.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            t = t[..^4];
        }
        var parts = t.Split('/');
        return parts.Length == 2 && parts.All(x => x.Length > 0) ? t : "";
    }

    /// <summary>语义化版本比较</summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = ParseSemver(a);
        var pb = ParseSemver(b);
        if (pa is not { } va || pb is not { } vb)
        {
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
        var len = Math.Max(va.Numbers.Length, vb.Numbers.Length);
        for (var i = 0; i < len; i++)
        {
            var na = i < va.Numbers.Length ? va.Numbers[i] : 0;
            var nb = i < vb.Numbers.Length ? vb.Numbers[i] : 0;
            if (na != nb)
            {
                return na.CompareTo(nb);
            }
        }
        // 正式版 > 预发布
        if (va.Pre.Length == 0 && vb.Pre.Length > 0)
        {
            return 1;
        }
        if (va.Pre.Length > 0 && vb.Pre.Length == 0)
        {
            return -1;
        }
        return string.Compare(va.Pre, vb.Pre, StringComparison.OrdinalIgnoreCase);
    }

    private static (long[] Numbers, string Pre)? ParseSemver(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var text = raw.Trim().TrimStart('v', 'V');
        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }
        var dash = text.IndexOf('-');
        var pre = "";
        if (dash >= 0)
        {
            pre = text[(dash + 1)..];
            text = text[..dash];
        }
        var segs = text.Split('.');
        var numbers = new long[segs.Length];
        for (var i = 0; i < segs.Length; i++)
        {
            if (!long.TryParse(segs[i], out numbers[i]))
            {
                return null;
            }
        }
        return (numbers, pre);
    }

    // 索引缓存
    private static readonly TimeSpan IndexSessionTtl = TimeSpan.FromMinutes(5);
    private static List<MarketPlugin>? _indexCache;
    private static DateTime _indexCacheTime = DateTime.MinValue;

    /// <summary>拉索引 Stale=旧缓存</summary>
    public static async Task<(List<MarketPlugin>? Plugins, string? Error, bool Stale)> FetchIndexAsync(bool force = false)
    {
        var startedAt = AppUtils.PreciseNow();
        Log.Info($"[市场] 拉取索引 force={force} 源={Config.MarketSource.Value}");
        if (!force && _indexCache is not null && AppUtils.PreciseNow() - _indexCacheTime < IndexSessionTtl)
        {
            Log.Debug($"[市场] 命中会话缓存 {_indexCache.Count} 项 ({(AppUtils.PreciseNow() - _indexCacheTime).TotalSeconds:F0}s 前)");
            return (_indexCache, null, false);
        }
        var configured = Config.MarketSource.Value;
        var element = await GitHubFetch.GetJsonAsync(configured);
        if (element is { } el)
        {
            try
            {
                if (el.TryGetProperty("apiVersion", out var rootApi) && rootApi.ValueKind == JsonValueKind.Number)
                {
                    var api = rootApi.GetInt32();
                    if (!PluginApi.IsCompatible(api))
                    {
                        Log.Warning($"[插件市场] 索引 apiVersion={api} 宿主={PluginApi.HostApiVersion} 不兼容");
                    }
                }
                var plugins = ParseIndex(el, configured);
                _indexCache = plugins;
                _indexCacheTime = AppUtils.PreciseNow();
                AppUtils.SaveCache("plugin-market", JsonSerializer.SerializeToElement(el), "1天");
                Log.Info($"[市场] 索引成功 {plugins.Count} 项 耗时 {(AppUtils.PreciseNow() - startedAt).TotalSeconds:F2}s");
                foreach (var sp in plugins)
                {
                    Log.Debug($"[市场] 条目 id={sp.Id} repo={sp.Repo}@{sp.Branch} path={sp.ManifestPath} " +
                              $"version={sp.LatestVersion} asset={sp.Asset} dl={sp.EffectiveDownloadUrl} sha={sp.Sha256}");
                }
                return (plugins, null, false);
            }
            catch (Exception e)
            {
                Log.Warning($"[插件市场] 索引解析失败: {e.Message}");
            }
        }
        else
        {
            Log.Warning("[插件市场] 索引源全部失败");
        }
        if (_indexCache is not null)
        {
            Log.Warning($"[市场] 索引全部失败 回退会话缓存 {_indexCache.Count} 项 (耗时 {(AppUtils.PreciseNow() - startedAt).TotalSeconds:F2}s)");
            return (_indexCache, null, true);
        }
        if (AppUtils.GetCachedContent("plugin-market", ignoreExpiry: true) is { } cache)
        {
            Log.Warning($"[市场] 索引全部失败 回退磁盘旧缓存 (耗时 {(AppUtils.PreciseNow() - startedAt).TotalSeconds:F2}s)");
            return (ParseIndex(cache, null), null, true);
        }
        Log.Error($"[市场] 索引拉取失败, 无任何可用缓存 (耗时 {(AppUtils.PreciseNow() - startedAt).TotalSeconds:F2}s)");
        return (null, "network", false);
    }

    /// <summary>先拿旧索引</summary>
    public static List<MarketPlugin>? TryGetCachedIndex()
    {
        if (_indexCache is not null)
        {
            return _indexCache;
        }
        try
        {
            if (AppUtils.GetCachedContent("plugin-market", ignoreExpiry: true) is { } cache)
            {
                var list = ParseIndex(cache, null);
                return list.Count > 0 ? list : null;
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[插件市场] 旧索引读取失败: {e.Message}");
        }
        return null;
    }

    // 相对路径按索引地址补全
    private static string ResolveUrl(string url, string? indexSource)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "";
        }
        if (url.StartsWith("{root}", StringComparison.Ordinal) && !string.IsNullOrEmpty(indexSource))
        {
            var slash = indexSource.LastIndexOf('/');
            var dir = slash >= 0 ? indexSource[..(slash + 1)] : indexSource;
            return dir + url["{root}".Length..].TrimStart('/');
        }
        if (PluginFs.IsHttpUrl(url))
        {
            return url;
        }
        if (!string.IsNullOrEmpty(indexSource) && Uri.TryCreate(indexSource, UriKind.Absolute, out var baseUri)
            && Uri.TryCreate(baseUri, url, out var abs))
        {
            return abs.ToString();
        }
        return url;
    }

    private static List<MarketPlugin> ParseIndex(JsonElement root, string? indexSource)
    {
        var list = new List<MarketPlugin>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("plugins", out var arr)
            || arr.ValueKind != JsonValueKind.Array)
        {
            return list;
        }
        foreach (var p in arr.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            string S(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var latest = p.TryGetProperty("latest", out var l) && l.ValueKind == JsonValueKind.Object ? l : default;
            string LS(string name) => latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? ""
                : (p.TryGetProperty(name, out var top) && top.ValueKind == JsonValueKind.String ? top.GetString() ?? "" : "");
            int LI(string name) => latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
            var id = S("id");
            var repo = NormalizeRepo(S("repo") is { Length: > 0 } r ? r : (S("repository") is { Length: > 0 } r2 ? r2 : S("repoUrl")));
            var downloadUrl = LS("downloadUrl");
            if (string.IsNullOrWhiteSpace(id) || (string.IsNullOrWhiteSpace(downloadUrl) && string.IsNullOrWhiteSpace(repo)))
            {
                continue;
            }
            // id 参与路径拼接 需白名单
            if (!PluginService.IsSafePluginId(id))
            {
                Log.Warning($"[插件市场] 索引条目 id 非法 已跳过: {id}");
                continue;
            }
            list.Add(new MarketPlugin
            {
                Id = id,
                Name = S("name"),
                Author = S("author"),
                Description = S("description"),
                IconUrl = ResolveUrl(S("iconUrl"), indexSource),
                Homepage = ResolveUrl(S("homepage"), indexSource),
                ReadmeUrl = ResolveUrl(S("readmeUrl"), indexSource),
                LatestVersion = LS("version"),
                LatestApiVersion = LI("apiVersion"),
                DownloadUrl = ResolveUrl(downloadUrl, indexSource),
                Sha256 = LS("sha256"),
                PublishedAt = LS("publishedAt"),
                Repo = repo,
                Asset = LS("asset"),
                Branch = string.IsNullOrWhiteSpace(LS("branch")) ? "main" : LS("branch"),
                ManifestPath = string.IsNullOrWhiteSpace(LS("manifestPath")) ? "manifest.json" : LS("manifestPath"),
            });
        }
        return list;
    }

    // sha256 校验
    private static async Task<PluginResult> DownloadAsync(MarketPlugin plugin, string dest, Action<double>? progress = null, CancellationToken ct = default)
    {
        var lastError = "";
        var downloaded = false;
        // 加速改写 + 多 tag 候选
        var candidates = new List<string>();
        foreach (var raw in plugin.DownloadCandidates())
        {
            foreach (var candidate in DownloadSources.Candidates(raw))
            {
                if (!candidates.Contains(candidate))
                {
                    candidates.Add(candidate);
                }
            }
        }
        if (candidates.Count == 0)
        {
            return PluginResult.Fail("索引未提供可下载地址", PluginOperationCode.InvalidPackage);
        }
        Log.Info($"[下载] {plugin.Id} v{plugin.LatestVersion} 目标 {dest}");
        Log.Debug($"[下载] {plugin.Id} 候选 {candidates.Count} 个: {string.Join(" | ", candidates)}");
        foreach (var url in candidates)
        {
            for (var attempt = 1; attempt <= 2 && !downloaded; attempt++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("User-Agent", $"Glimpseon/{Paths.Version}");
                    req.Headers.TryAddWithoutValidation("Accept", "application/octet-stream, */*");
                    Log.Debug($"[下载] {plugin.Id} → GET {url} 第 {attempt} 次");
                    using var resp = await GitHubFetch.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    var status = (int)resp.StatusCode;
                    var total = resp.Content.Headers.ContentLength ?? -1;
                    Log.Debug($"[下载] {plugin.Id} ← {status} 大小={total}B type={resp.Content.Headers.ContentType} {url}");
                    resp.EnsureSuccessStatusCode();
                    await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    await using var fs = File.Create(dest);
                    var buffer = new byte[81920];
                    long read = 0;
                    var lastTier = -1;
                    // -1 = 长度未知
                    progress?.Invoke(total > 0 ? 0 : -1);
                    int n;
                    while ((n = await stream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                        read += n;
                        var pct = total > 0 ? read * 100.0 / total : -1;
                        progress?.Invoke(pct);
                        if (pct >= 0)
                        {
                            var tier = (int)(pct / 10);
                            if (tier > lastTier)
                            {
                                lastTier = tier;
                                Log.Debug($"[下载] {plugin.Id} {pct:F0}% ({read}/{total}B)");
                            }
                        }
                    }
                    sw.Stop();
                    Log.Info($"[下载] {plugin.Id} 完成 {read}B 耗时 {sw.Elapsed.TotalSeconds:F2}s 源={url}");
                    downloaded = true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    TryDelete(dest);
                    throw;
                }
                catch (Exception e)
                {
                    sw.Stop();
                    lastError = e.Message;
                    TryDelete(dest);
                    Log.Warning($"[下载] {plugin.Id} 源失败 {sw.ElapsedMilliseconds}ms {url}: {e.GetType().Name}: {e.Message}");
                    if (attempt < 2)
                    {
                        try
                        {
                            await Task.Delay(300, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                    }
                }
            }
            if (downloaded)
            {
                break;
            }
        }
        if (!downloaded)
        {
            return PluginResult.Fail($"下载失败: {lastError}", PluginOperationCode.Network);
        }
        if (!string.IsNullOrWhiteSpace(plugin.Sha256))
        {
            var actual = await ComputeSha256Async(dest);
            Log.Debug($"[下载] {plugin.Id} sha256 期望={plugin.Sha256} 实际={actual}");
            if (!string.Equals(actual, plugin.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(dest);
                Log.Error($"[下载] {plugin.Id} sha256 校验失败, 已删除临时包");
                return PluginResult.Fail("sha256 校验未通过", PluginOperationCode.ShaMismatch);
            }
            Log.Info($"[下载] {plugin.Id} sha256 校验通过");
        }
        else
        {
            Log.Warning($"[下载] {plugin.Id} 索引未提供 sha256, 跳过校验");
        }
        return PluginResult.Success();
    }

    public static async Task<PluginResult> InstallFromMarketAsync(MarketPlugin plugin, Action<double>? progress = null, CancellationToken ct = default)
    {
        if (!PluginService.IsSafePluginId(plugin.Id))
        {
            return PluginResult.Fail($"索引条目 id 非法: {plugin.Id}", PluginOperationCode.InvalidPackage);
        }
        Directory.CreateDirectory(PluginService.PackagesCacheDir);
        var dest = Path.Combine(PluginService.PackagesCacheDir, plugin.Id + ".gplugin");
        var download = await DownloadAsync(plugin, dest, progress, ct);
        if (!download.Ok)
        {
            return download;
        }
        var installed = PluginService.InstallPackage(dest);
        TryDelete(dest);
        return installed;
    }

    // 自动更新
    public static async Task AutoUpdateAsync()
    {
        if (!Config.PluginsAutoUpdate.Value)
        {
            return;
        }
        var (plugins, _, _) = await FetchIndexAsync();
        if (plugins is null)
        {
            return;
        }
        foreach (var mp in plugins)
        {
            var local = PluginService.Plugins.FirstOrDefault(p => p.Manifest.Id == mp.Id);
            if (local is null || !local.Enabled || local.Status != PluginLoadStatus.Loaded)
            {
                continue;
            }
            // 已下载待重启的不重复下
            if (PluginService.PendingRestartIds.Contains(mp.Id))
            {
                continue;
            }
            // 0=未登记 视为兼容
            if (!PluginApi.IsCompatible(mp.LatestApiVersion)
                || CompareVersions(mp.LatestVersion, local.Manifest.Version) <= 0)
            {
                continue;
            }
            var dest = Path.Combine(Paths.DataPlugins, mp.Id + ".gplugin");
            var result = await DownloadAsync(mp, dest);
            if (result.Ok)
            {
                Log.Info($"[插件市场] 自动更新已下载 {mp.Id} v{mp.LatestVersion}");
                PluginService.PendingRestartIds.Add(mp.Id);
                AppUtils.MarkRestartPending();
                Announcements.Append($"插件已自动更新: {mp.Name}");
            }
            else
            {
                Log.Warning($"[插件市场] 自动更新失败 {mp.Id}: {result.Message}");
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs);
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}

/// <summary>
/// 回源取插件自身项目的 manifest/README/图标
/// </summary>
public static class PluginMetaStore
{
    private static readonly ConcurrentDictionary<string, (MarketMetadata Meta, DateTime At)> Cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Task<MarketMetadata?>> Inflight = new(StringComparer.Ordinal);

    /// <summary>已确认 404 的插件</summary>
    private static readonly ConcurrentDictionary<string, bool> NotFound = new(StringComparer.Ordinal);

    /// <summary>回源批次已结束</summary>
    public static bool EnrichmentFinished { get; set; }

    public static bool IsNotFound(string id) => NotFound.ContainsKey(id);

    private static string MetaDir => Path.Combine(Paths.DataCache, "plugin-meta");

    /// <summary>缓存有效期</summary>
    private static readonly TimeSpan MetaCacheTtl = TimeSpan.FromHours(6);

    public static void Clear()
    {
        Cache.Clear();
        Inflight.Clear();
        NotFound.Clear();
        EnrichmentFinished = false;
        try
        {
            if (Directory.Exists(MetaDir))
            {
                Directory.Delete(MetaDir, true);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[插件市场] 元数据缓存清理失败: {e.Message}");
        }
    }

    /// <summary>丢弃单个插件的缓存</summary>
    public static void Remove(string id)
    {
        if (!PluginService.IsSafePluginId(id))
        {
            return;
        }
        Cache.TryRemove(id, out _);
        NotFound.TryRemove(id, out _);
        try
        {
            var path = DiskPath(id);
            if (path is not null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[回源] 删除缓存失败 {id}: {e.Message}");
        }
    }

    public static async Task<MarketMetadata?> GetAsync(MarketPlugin mp, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(mp.Repo))
        {
            Log.Debug($"[回源] {mp.Id} 未登记 repo, 跳过回源");
            return null;
        }
        if (NotFound.ContainsKey(mp.Id))
        {
            Log.Debug($"[回源] {mp.Id} 之前已确认 404, 跳过");
            return null;
        }
        Log.Debug($"[回源] {mp.Id} 请求元数据 repo={mp.Repo}@{mp.Branch} path={mp.ManifestPath} force={force} " +
                  $"indexVersion={mp.LatestVersion}");
        if (!force && Cache.TryGetValue(mp.Id, out var entry))
        {
            if (AppUtils.PreciseNow() - entry.At < MetaCacheTtl)
            {
                Log.Debug($"[回源] {mp.Id} 命中内存缓存 name={entry.Meta.Name} version={entry.Meta.Version}");
                return entry.Meta;
            }
            Cache.TryRemove(mp.Id, out _);
        }
        if (!force && TryLoadDisk(mp) is { } disk)
        {
            Cache[mp.Id] = (disk, AppUtils.PreciseNow());
            Log.Debug($"[回源] {mp.Id} 命中磁盘缓存 name={disk.Name} version={disk.Version}");
            return disk;
        }
        var task = Inflight.GetOrAdd(mp.Id, _ => FetchAsync(mp));
        try
        {
            var meta = await task;
            if (meta is not null)
            {
                Cache[mp.Id] = (meta, AppUtils.PreciseNow());
                SaveDisk(mp, meta);
            }
            else
            {
                Log.Warning($"[回源] {mp.Id} 失败: manifest 取不到或解析不了 (repo={mp.Repo})");
            }
            return meta;
        }
        finally
        {
            Inflight.TryRemove(mp.Id, out _);
        }
    }

    private static async Task<MarketMetadata?> FetchAsync(MarketPlugin mp)
    {
        var rawBase = $"https://raw.githubusercontent.com/{mp.Repo}/{mp.Branch}/";
        var manifestPath = mp.ManifestPath.TrimStart('/');
        // icon / readme 相对 manifest 所在目录解析
        var manifestDir = manifestPath.Contains('/')
            ? manifestPath[..(manifestPath.LastIndexOf('/') + 1)]
            : "";
        var assetBase = rawBase + manifestDir;
        var manifestUrl = rawBase + manifestPath;
        var fetched = await GitHubFetch.GetJsonAsync(manifestUrl);
        if (fetched is not { } root)
        {
            var status = GitHubFetch.LastStatusFor(manifestUrl);
            if (status is 404 or 410)
            {
                NotFound[mp.Id] = true;
                Log.Warning($"[回源] {mp.Id} manifest 404 (仓库或路径不对), 不再重试 {manifestUrl}");
            }
            else
            {
                Log.Warning($"[回源] {mp.Id} manifest 取不到 (status={status}), 稍后重试 {manifestUrl}");
            }
            return null;
        }
        string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int Num(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        string Url(string path) => string.IsNullOrWhiteSpace(path)
            ? ""
            : PluginFs.IsHttpUrl(path) ? path : assetBase + path.TrimStart('/');

        var manifestId = Str("id");
        if (!string.IsNullOrWhiteSpace(manifestId) && !string.Equals(manifestId, mp.Id, StringComparison.Ordinal))
        {
            Log.Warning($"[插件市场] {mp.Id} 回源 manifest.id={manifestId} 与索引登记不一致, 以插件项目为准");
        }
        var icon = Url(Str("icon"));
        var readme = Url(string.IsNullOrWhiteSpace(Str("readme")) ? "README.md" : Str("readme"));
        var meta = new MarketMetadata
        {
            Name = Str("name"),
            Author = Str("author"),
            Description = Str("description"),
            Version = Str("version"),
            ApiVersion = Num("apiVersion"),
            IconUrl = string.IsNullOrWhiteSpace(icon) ? null : icon,
            ReadmeUrl = string.IsNullOrWhiteSpace(readme) ? null : readme,
            Source = manifestUrl,
        };
        Log.Info($"[回源] {mp.Id} manifest 成功 name={meta.Name} version={meta.Version} api={meta.ApiVersion} " +
                 $"icon={meta.IconUrl ?? "(无)"} readme={meta.ReadmeUrl ?? "(无)"} 来源={manifestUrl}");
        return meta;
    }

    private sealed class DiskEntry
    {
        public string Version { get; set; } = "";
        public DateTime FetchedAt { get; set; }
        public string? Name { get; set; }
        public string? Author { get; set; }
        public string? Description { get; set; }
        public int ApiVersion { get; set; }
        public string? IconUrl { get; set; }
        public string? ReadmeUrl { get; set; }
        public string Source { get; set; } = "";
    }

    /// <summary>磁盘缓存路径</summary>
    private static string? DiskPath(string id)
        => PluginService.IsSafePluginId(id) ? Path.Combine(MetaDir, id + ".json") : null;

    private static string? DiskPath(MarketPlugin mp) => DiskPath(mp.Id);

    private static MarketMetadata? TryLoadDisk(MarketPlugin mp)
    {
        try
        {
            var path = DiskPath(mp);
            if (path is null || !File.Exists(path))
            {
                return null;
            }
            var entry = JsonSerializer.Deserialize<DiskEntry>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (entry is null)
            {
                return null;
            }
            // 索引版本更高才重取
            if (!string.IsNullOrEmpty(mp.LatestVersion)
                && !string.IsNullOrEmpty(entry.Version)
                && PluginMarket.CompareVersions(mp.LatestVersion, entry.Version) > 0)
            {
                Log.Debug($"[回源] {mp.Id} 磁盘缓存版本落后 (缓存 {entry.Version} 索引 {mp.LatestVersion}), 重新获取");
                return null;
            }
            var age = DateTime.UtcNow - entry.FetchedAt;
            if (entry.FetchedAt == default || age > MetaCacheTtl)
            {
                Log.Debug($"[回源] {mp.Id} 磁盘缓存过期 ({age.TotalHours:F1} 小时), 重新获取");
                return null;
            }
            return new MarketMetadata
            {
                Name = entry.Name,
                Author = entry.Author,
                Description = entry.Description,
                Version = entry.Version,
                ApiVersion = entry.ApiVersion,
                IconUrl = entry.IconUrl,
                ReadmeUrl = entry.ReadmeUrl,
                Source = entry.Source,
            };
        }
        catch
        {
            return null;
        }
    }

    private static void SaveDisk(MarketPlugin mp, MarketMetadata meta)
    {
        try
        {
            var path = DiskPath(mp);
            if (path is null)
            {
                Log.Warning($"[插件市场] {mp.Id} id 非法, 跳过元数据缓存");
                return;
            }
            Directory.CreateDirectory(MetaDir);
            var entry = new DiskEntry
            {
                Version = meta.Version ?? "",
                FetchedAt = DateTime.UtcNow,
                Name = meta.Name,
                Author = meta.Author,
                Description = meta.Description,
                ApiVersion = meta.ApiVersion,
                IconUrl = meta.IconUrl,
                ReadmeUrl = meta.ReadmeUrl,
                Source = meta.Source,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(entry,
                new JsonSerializerOptions { WriteIndented = true }));
            Log.Debug($"[回源] {mp.Id} 写入缓存 {path}");
        }
        catch (Exception e)
        {
            Log.Warning($"[插件市场] {mp.Id} 元数据缓存写入失败: {e.Message}");
        }
    }
}
