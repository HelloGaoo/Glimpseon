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
    public string Entrance { get; init; } = "";
    public int ApiVersion { get; init; }
    public string Icon { get; init; } = "";
    public string Readme { get; init; } = "";
    public List<PluginDependency> Dependencies { get; init; } = [];

    public static PluginManifest? Load(string dir, out string error, bool checkDirName = true)
    {
        error = "";
        var path = Path.Combine(dir, "manifest.json");
        if (!File.Exists(path))
        {
            error = "manifest.json 缺失";
            return null;
        }
        PluginManifest? m;
        try
        {
            m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip });
        }
        catch (Exception e)
        {
            error = $"manifest.json 解析失败: {e.Message}";
            return null;
        }
        if (m is null)
        {
            error = "manifest.json 内容为空";
            return null;
        }
        if (string.IsNullOrWhiteSpace(m.Id))
        {
            error = "manifest.id 为空";
            return null;
        }
        if (checkDirName && m.Id != Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
        {
            error = $"manifest.id ({m.Id}) 与目录名 ({Path.GetFileName(dir)}) 不一致";
            return null;
        }
        if (string.IsNullOrWhiteSpace(m.Entrance) || !File.Exists(Path.Combine(dir, m.Entrance)))
        {
            error = $"manifest.entrance 文件不存在: {m.Entrance}";
            return null;
        }
        if (m.ApiVersion != PluginApi.HostApiVersion)
        {
            error = $"apiVersion {m.ApiVersion} 不受支持 宿主支持 {PluginApi.HostApiVersion}";
            return null;
        }
        return m;
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
    public string Directory { get; }
    public int ApiVersion { get; }

    internal PluginContext(PluginManifest manifest, string directory)
    {
        Id = manifest.Id;
        Directory = directory;
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
        => PluginEventBus.Add(new PluginSubscription { PluginId = Id, Event = "TimeTick", Handler = p => handler((DateTime)p!) });

    public void SubscribeCourseChanged(Action<ScheduleRow?> handler)
        => PluginEventBus.Add(new PluginSubscription { PluginId = Id, Event = "CourseChanged", Handler = p => handler(p as ScheduleRow) });

    public void SubscribeClassStateChanged(Action<PluginClassState> handler)
        => PluginEventBus.Add(new PluginSubscription { PluginId = Id, Event = "ClassStateChanged", Handler = p => handler((PluginClassState)p!) });

    public void SubscribeAppClosing(Action handler)
        => PluginEventBus.Add(new PluginSubscription { PluginId = Id, Event = "AppClosing", Handler = _ => handler() });

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

    /// <summary>弹通知 style: corner/scroll/fullscreen</summary>
    public void ShowNotification(string content, int durationSeconds = 5, string style = "corner")
    {
        content = (content ?? "").Trim();
        if (content.Length == 0)
        {
            LogWarning("通知内容为空 跳过");
            return;
        }
        var type = style switch
        {
            "scroll" => NotifType.Scroll,
            "fullscreen" => NotifType.Fullscreen,
            _ => NotifType.Corner,
        };
        NotifManager.HandleNotification(new NotificationRequest
        {
            Type = type,
            Content = content,
            Duration = Math.Clamp(durationSeconds, 1, 120),
        });
        Announcements.Append(content);
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
    public bool IsLocal { get; set; }
    public bool IsMarket { get; set; }
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
            var info = new PluginInfo
            {
                Id = lp.Manifest.Id,
                Local = lp,
                IsLocal = true,
                Name = lp.Manifest.Name,
                Author = lp.Manifest.Author,
                Description = lp.Manifest.Description,
                LocalVersion = lp.Manifest.Version,
                Enabled = lp.Enabled,
                Status = lp.Status,
                Error = lp.Error,
                Directory = lp.Directory,
                Dependencies = lp.Manifest.Dependencies,
                IconSource = ResolveLocalIcon(lp),
                ReadmeSource = ResolveLocalReadme(lp),
            };
            map[info.Id] = info;
        }
        if (market is not null)
        {
            foreach (var mp in market)
            {
                if (!map.TryGetValue(mp.Id, out var info))
                {
                    info = new PluginInfo
                    {
                        Id = mp.Id,
                        IsMarket = true,
                        PendingRestart = Directory.Exists(Path.Combine(Paths.DataPlugins, mp.Id)),
                    };
                    map[info.Id] = info;
                }
                info.Market = mp;
                info.IsMarket = true;
                var meta = mp.Meta;
                // 版本看索引
                var marketVersion = !string.IsNullOrWhiteSpace(mp.LatestVersion)
                    ? mp.LatestVersion
                    : meta?.Version ?? "";
                var marketApi = meta is { ApiVersion: > 0 } ? meta.ApiVersion : mp.LatestApiVersion;
                info.MarketVersion = marketVersion;
                info.IsIncompatible = marketApi != 0 && marketApi != PluginApi.HostApiVersion;
                if (!info.IsLocal)
                {
                    // 名称/作者/描述/图标/说明以插件项目看 manifest
                    info.Name = !string.IsNullOrWhiteSpace(meta?.Name) ? meta!.Name! : mp.Name;
                    info.Author = !string.IsNullOrWhiteSpace(meta?.Author) ? meta!.Author! : mp.Author;
                    info.Description = !string.IsNullOrWhiteSpace(meta?.Description) ? meta!.Description! : mp.Description;
                    info.Homepage = mp.Homepage;
                    info.IconSource = !string.IsNullOrWhiteSpace(meta?.IconUrl) ? meta!.IconUrl : mp.IconUrl;
                    info.ReadmeSource = !string.IsNullOrWhiteSpace(meta?.ReadmeUrl) ? meta!.ReadmeUrl : mp.ReadmeUrl;
                }
                else
                {
                    info.IsUpdateAvailable = !info.IsIncompatible
                        && PluginMarket.CompareVersions(marketVersion, info.LocalVersion ?? "0") > 0;
                    // 本地 manifest 没有主页字段 用索引补全
                    if (string.IsNullOrEmpty(info.Homepage))
                    {
                        info.Homepage = mp.Homepage;
                    }
                    if (string.IsNullOrEmpty(info.Author))
                    {
                        info.Author = mp.Author;
                    }
                }
            }
        }
        _items = map.Values
            .OrderByDescending(x => x.IsLocal)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static PluginInfo? Find(string id) => _items.FirstOrDefault(x => x.Id == id);

    // 本地图标/说明解析
    private static string? ResolveLocalIcon(LoadedPlugin lp)
    {
        var name = lp.Manifest.Icon;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        if (name.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }
        var path = Path.Combine(lp.Directory, name);
        return File.Exists(path) ? path : null;
    }

    private static string? ResolveLocalReadme(LoadedPlugin lp)
    {
        foreach (var name in new[] { lp.Manifest.Readme, "README.md" })
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            if (name.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
            var path = Path.Combine(lp.Directory, name);
            if (File.Exists(path))
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
        if (name.Name is "GlimpseonMain" or "Avalonia.Base" or "Avalonia.Controls"
            or "Avalonia.Skia" or "Avalonia.Markup" or "FluentAvalonia")
        {
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
}

public static class PluginService
{
    public static List<LoadedPlugin> Plugins { get; } = new();
    public static List<ComponentDefinition> PluginComponentDefinitions { get; } = new();
    private static readonly Dictionary<string, Func<JsonElement?, Control>> ComponentFactories = new();
    public static Dictionary<string, (string Name, Func<List<ScheduleRow>> Provider)> ScheduleProviders { get; } = new();
    internal static Dictionary<string, PluginLoadContext> LoadContexts { get; } = new();

    private static string EnabledMapPath => Path.Combine(Paths.DataConfig, "plugins.json");
    private static string PackagesCacheDir => Path.Combine(Paths.DataCache, "plugin-packages");

    // 加载

    public static void InitializePlugins()
    {
        Plugins.Clear();
        PluginComponentDefinitions.Clear();
        ComponentFactories.Clear();
        ScheduleProviders.Clear();
        LoadContexts.Clear();
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
                Plugins.Add(PreprocessPlugin(dir, enabledMap));
            }
            catch (Exception e)
            {
                Log.Error($"[插件] 预处理异常 {Path.GetFileName(dir)}: {e.Message}");
                Plugins.Add(new LoadedPlugin
                {
                    Manifest = new PluginManifest { Id = Path.GetFileName(dir), Name = Path.GetFileName(dir) },
                    Directory = dir,
                    Enabled = true,
                    Status = PluginLoadStatus.Error,
                    Error = $"预处理异常: {e.Message}",
                });
            }
        }

        var loadOrder = ResolveLoadOrder(Plugins.Where(x => x.Status == PluginLoadStatus.NotLoaded).ToList());
        Log.Info($"[插件] 加载顺序: {string.Join(" -> ", loadOrder)}");
        foreach (var id in loadOrder)
        {
            LoadPlugin(Plugins.First(x => x.Manifest.Id == id));
        }

        var loaded = Plugins.Count(x => x.Status == PluginLoadStatus.Loaded);
        var failed = Plugins.Count(x => x.Status == PluginLoadStatus.Error);
        var disabled = Plugins.Count(x => x.Status == PluginLoadStatus.Disabled);
        Log.Info($"[插件] 扫描 {dirs.Count} 加载成功 {loaded} 失败 {failed} 未启用 {disabled}");

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                PluginEventBus.RaiseAppClosing();
            }
            catch
            {
            }
        };
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
                    string message;
                    (ok, message) = InstallPackage(pkg);
                    Log.Info($"[插件] 启动期安装 {Path.GetFileName(pkg)}: {(ok ? $"成功 {message}" : $"失败 {message}")}");
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
        var manifest = PluginManifest.Load(dir, out var error);
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
        if (node.Discovered)
        {
            return;
        }
        if (walking.Contains(node))
        {
            node.Plugin.Status = PluginLoadStatus.Error;
            node.Plugin.Error = $"循环依赖: {string.Join(" -> ", walking.Select(x => x.Plugin.Manifest.Id))} -> {node.Plugin.Manifest.Id}";
            Log.Warning($"[插件] {node.Plugin.Manifest.Id} {node.Plugin.Error}");
            return;
        }
        node.Discovered = true;
        walking.Add(node);
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
        walking.Remove(node);
    }

    private static void LoadPlugin(LoadedPlugin lp)
    {
        var manifest = lp.Manifest;
        var dllPath = Path.GetFullPath(Path.Combine(lp.Directory, manifest.Entrance));
        var alc = new PluginLoadContext(manifest.Id, manifest.Dependencies, dllPath);
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
        }
        catch (Exception e)
        {
            type = null;
            Log.Warning($"[插件] {manifest.Id} 类型扫描失败: {e.Message}");
        }
        if (type is null)
        {
            lp.Status = PluginLoadStatus.Error;
            lp.Error = "未找到 GlimpseonPlugin 派生类";
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
            lp.Status = PluginLoadStatus.Error;
            lp.Instance = instance;
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

    public static void RegisterComponent(string pluginId, ComponentDefinition definition, Func<JsonElement?, Control> factory)
    {
        if (definition is null || string.IsNullOrWhiteSpace(definition.Id))
        {
            Log.Warning($"[plugin:{pluginId}] 组件定义 Id 为空 跳过注册");
            return;
        }
        PluginComponentDefinitions.RemoveAll(d => d.Id == definition.Id);
        PluginComponentDefinitions.Add(definition);
        ComponentFactories[definition.Id] = factory;
        Log.Info($"[plugin:{pluginId}] 注册组件 {definition.Id}");
    }

    public static void RegisterScheduleProvider(string pluginId, string name, Func<List<ScheduleRow>> provider)
    {
        ScheduleProviders[pluginId] = (name, provider);
        Log.Info($"[plugin:{pluginId}] 注册课表提供者 {name}");
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
        var map = new Dictionary<string, bool>();
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
            Log.Warning($"[插件] plugins.json 读取失败 视为全启用: {e.Message}");
        }
        return map;
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

    public static (bool Ok, string Message) InstallPackage(string packagePath)
    {
        string? staging = null;
        try
        {
            using var zip = ZipFile.OpenRead(packagePath);
            var entries = zip.Entries
                .Select(e => (Entry: e, Name: e.FullName.Replace('\\', '/')))
                .Where(x => x.Name.Length > 0)
                .ToList();
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
                return (false, "包内缺少 manifest.json");
            }
            Directory.CreateDirectory(PackagesCacheDir);
            staging = Path.Combine(PackagesCacheDir, "stage_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(staging);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length <= prefix.Length || name.EndsWith('/'))
                {
                    continue;
                }
                var target = Path.GetFullPath(Path.Combine(staging, name[prefix.Length..]));
                if (!target.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
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
            return (false, $"解压失败: {e.Message}");
        }
        var manifest = PluginManifest.Load(staging, out var error, checkDirName: false);
        if (manifest is null)
        {
            CleanupStaging(staging);
            return (false, error);
        }
        if (!IsSafePluginId(manifest.Id))
        {
            CleanupStaging(staging);
            return (false, $"manifest.id 非法: {manifest.Id}");
        }
        var targetDir = Path.Combine(Paths.DataPlugins, manifest.Id);
        string? backup = null;
        try
        {
            if (Directory.Exists(targetDir))
            {
                backup = targetDir + ".bak_" + Guid.NewGuid().ToString("N")[..6];
                Directory.Move(targetDir, backup);
            }
            Directory.Move(staging, targetDir);
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
            return (false, $"安装失败: {e.Message}");
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
        Log.Info($"[插件] 已安装 {manifest.Id} v{manifest.Version} 重启后加载");
        return (true, manifest.Name);
    }

    public static (bool Ok, string Message) Uninstall(string pluginId)
    {
        var dir = Path.Combine(Paths.DataPlugins, pluginId);
        if (!Directory.Exists(dir))
        {
            return (false, "插件目录不存在");
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
                ForgetPlugin(pluginId);
                Log.Info($"[插件] {pluginId} 已标记卸载 重启后自动删除");
                return (true, "pending");
            }
            catch (Exception e)
            {
                return (false, $"标记卸载失败: {e.Message}");
            }
        }
        ForgetPlugin(pluginId);
        Log.Info($"[插件] 已卸载 {pluginId}");
        return (true, pluginId);
    }

    private static bool IsSafePluginId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 128
        && id.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) < 0
        && id != "." && id != "..";

    private static void ForgetPlugin(string pluginId)
    {
        Plugins.RemoveAll(p => p.Manifest.Id == pluginId);
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

    public string EffectiveDownloadUrl =>
        !string.IsNullOrWhiteSpace(DownloadUrl) ? DownloadUrl
        : string.IsNullOrWhiteSpace(Repo) ? "" 
        : $"https://github.com/{Repo}/releases/latest/download/{(string.IsNullOrWhiteSpace(Asset) ? Id + ".gplugin" : Asset)}";
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

    public static int CompareVersions(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
        {
            return va.CompareTo(vb);
        }
        return string.CompareOrdinal(a, b);
    }

    // 索引缓存
    private static readonly TimeSpan IndexSessionTtl = TimeSpan.FromMinutes(5);
    private static List<MarketPlugin>? _indexCache;
    private static DateTime _indexCacheTime = DateTime.MinValue;

    // 源顺序
    public static async Task<(List<MarketPlugin>? Plugins, string? Error)> FetchIndexAsync(bool force = false)
    {
        if (!force && _indexCache is not null && AppUtils.PreciseNow() - _indexCacheTime < IndexSessionTtl)
        {
            return (_indexCache, null);
        }
        var configured = Config.MarketSource.Value;
        var sources = DownloadSources.Candidates(configured);
        foreach (var source in sources)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"Glimpseon/{Paths.Version}");
                var text = await http.GetStringAsync(source);
                var element = JsonSerializer.Deserialize<JsonElement>(text);
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("apiVersion", out var rootApi)
                    && rootApi.ValueKind == JsonValueKind.Number
                    && rootApi.GetInt32() != PluginApi.HostApiVersion)
                {
                    Log.Warning($"[插件市场] 索引 apiVersion={rootApi.GetInt32()} 宿主={PluginApi.HostApiVersion} 兼容性未知");
                }
                var plugins = ParseIndex(element, source);
                _indexCache = plugins;
                _indexCacheTime = AppUtils.PreciseNow();
                AppUtils.SaveCache("plugin-market", JsonSerializer.SerializeToElement(element), "1天");
                Log.Info($"[插件市场] 索引拉取成功 {plugins.Count}项 源={source}");
                return (plugins, null);
            }
            catch (Exception e)
            {
                Log.Warning($"[插件市场] 索引源失败 {source}: {e.Message}");
            }
        }
        if (_indexCache is not null)
        {
            Log.Info("[插件市场] 索引源全部失败 用本会话缓存");
            return (_indexCache, null);
        }
        if (AppUtils.GetCachedContent("plugin-market", ignoreExpiry: true) is { } cache)
        {
            Log.Info("[插件市场] 索引源全部失败 用磁盘旧缓存");
            return (ParseIndex(cache, null), null);
        }
        return (null, "network");
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
            var dir = indexSource[..(indexSource.LastIndexOf('/') + 1)];
            return dir + url["{root}".Length..].TrimStart('/');
        }
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }
        if (!string.IsNullOrEmpty(indexSource) && Uri.TryCreate(indexSource, UriKind.Absolute, out var baseUri))
        {
            if (Uri.TryCreate(baseUri, url, out var abs))
            {
                return abs.ToString();
            }
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
            int I(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
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
            list.Add(new MarketPlugin
            {
                Id = id,
                Name = S("name") is { Length: > 0 } n ? n : id,
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
    private static async Task<(bool Ok, string Message)> DownloadAsync(MarketPlugin plugin, string dest, Action<double>? progress = null)
    {
        var lastError = "";
        var downloaded = false;
        foreach (var url in DownloadSources.Candidates(plugin.EffectiveDownloadUrl))
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"Glimpseon/{Paths.Version}");
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? -1;
                await using var stream = await resp.Content.ReadAsStreamAsync();
                await using var fs = File.Create(dest);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await stream.ReadAsync(buffer)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    if (total > 0)
                    {
                        progress?.Invoke(read * 100.0 / total);
                    }
                }
                downloaded = true;
                break;
            }
            catch (Exception e)
            {
                lastError = e.Message;
                TryDelete(dest);
                Log.Warning($"[插件市场] 下载源失败 {url}: {e.Message}");
            }
        }
        if (!downloaded)
        {
            return (false, $"下载失败: {lastError}");
        }
        if (!string.IsNullOrWhiteSpace(plugin.Sha256))
        {
            var actual = await ComputeSha256Async(dest);
            if (!string.Equals(actual, plugin.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(dest);
                Log.Warning($"[插件市场] sha256 不符 {plugin.Id} 期望 {plugin.Sha256} 实际 {actual}");
                return (false, "sha");
            }
        }
        return (true, "");
    }

    public static async Task<(bool Ok, string Message)> InstallFromMarketAsync(MarketPlugin plugin, Action<double>? progress = null)
    {
        Directory.CreateDirectory(Path.Combine(Paths.DataCache, "plugin-packages"));
        var dest = Path.Combine(Paths.DataCache, "plugin-packages", plugin.Id + ".gplugin");
        var (ok, message) = await DownloadAsync(plugin, dest, progress);
        if (!ok)
        {
            return (false, message);
        }
        var (installed, msg) = PluginService.InstallPackage(dest);
        TryDelete(dest);
        return (installed, msg);
    }

    // 自动更新
    public static async Task AutoUpdateAsync()
    {
        if (!Config.PluginsAutoUpdate.Value)
        {
            return;
        }
        var (plugins, _) = await FetchIndexAsync();
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
            if (mp.LatestApiVersion != PluginApi.HostApiVersion
                || CompareVersions(mp.LatestVersion, local.Manifest.Version) <= 0)
            {
                continue;
            }
            var dest = Path.Combine(Paths.DataPlugins, mp.Id + ".gplugin");
            var (ok, message) = await DownloadAsync(mp, dest);
            if (ok)
            {
                Log.Info($"[插件市场] 自动更新已下载 {mp.Id} v{mp.LatestVersion}");
                AppUtils.MarkRestartPending();
                Announcements.Append($"插件已自动更新: {mp.Name}");
            }
            else
            {
                Log.Warning($"[插件市场] 自动更新失败 {mp.Id}: {message}");
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
    private static readonly ConcurrentDictionary<string, MarketMetadata> Cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Task<MarketMetadata?>> Inflight = new(StringComparer.Ordinal);

    private static string MetaDir => Path.Combine(Paths.DataCache, "plugin-meta");

    public static void Clear()
    {
        Cache.Clear();
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

    public static async Task<MarketMetadata?> GetAsync(MarketPlugin mp, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(mp.Repo))
        {
            return null;
        }
        if (!force && Cache.TryGetValue(mp.Id, out var cached))
        {
            return cached;
        }
        if (!force && TryLoadDisk(mp) is { } disk)
        {
            Cache[mp.Id] = disk;
            return disk;
        }
        if (Inflight.TryGetValue(mp.Id, out var running))
        {
            return await running;
        }
        var task = FetchAsync(mp);
        Inflight[mp.Id] = task;
        try
        {
            var meta = await task;
            if (meta is not null)
            {
                Cache[mp.Id] = meta;
                SaveDisk(mp, meta);
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
        var text = await HttpGetStringAsync(manifestUrl);
        if (string.IsNullOrWhiteSpace(text))
        {
            Log.Warning($"[插件市场] {mp.Id} 回源失败 manifest 取不到 {manifestUrl}");
            return null;
        }
        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(text);
        }
        catch (Exception e)
        {
            Log.Warning($"[插件市场] {mp.Id} manifest 解析失败: {e.Message}");
            return null;
        }
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int Num(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        string Url(string path) => string.IsNullOrWhiteSpace(path)
            ? ""
            : path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : assetBase + path.TrimStart('/');

        var manifestId = Str("id");
        if (!string.IsNullOrWhiteSpace(manifestId) && !string.Equals(manifestId, mp.Id, StringComparison.Ordinal))
        {
            Log.Warning($"[插件市场] {mp.Id} 回源 manifest.id={manifestId} 与索引登记不一致, 以插件项目为准");
        }
        var icon = Url(Str("icon"));
        var readme = Url(string.IsNullOrWhiteSpace(Str("readme")) ? "README.md" : Str("readme"));
        return new MarketMetadata
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
    }

    private static async Task<string?> HttpGetStringAsync(string url)
    {
        foreach (var candidate in DownloadSources.Candidates(url))
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"Glimpseon/{Paths.Version}");
                var text = await http.GetStringAsync(candidate);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch
            {
            }
        }
        return null;
    }

    private sealed class DiskEntry
    {
        public string Version { get; set; } = "";
        public string? Name { get; set; }
        public string? Author { get; set; }
        public string? Description { get; set; }
        public int ApiVersion { get; set; }
        public string? IconUrl { get; set; }
        public string? ReadmeUrl { get; set; }
        public string Source { get; set; } = "";
    }

    private static string DiskPath(MarketPlugin mp) => Path.Combine(MetaDir, mp.Id + ".json");

    private static MarketMetadata? TryLoadDisk(MarketPlugin mp)
    {
        try
        {
            var path = DiskPath(mp);
            if (!File.Exists(path))
            {
                return null;
            }
            var entry = JsonSerializer.Deserialize<DiskEntry>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            // 版本变了就重取
            if (entry is null || (!string.IsNullOrEmpty(mp.LatestVersion) && entry.Version != mp.LatestVersion))
            {
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
            Directory.CreateDirectory(MetaDir);
            var entry = new DiskEntry
            {
                Version = meta.Version ?? "",
                Name = meta.Name,
                Author = meta.Author,
                Description = meta.Description,
                ApiVersion = meta.ApiVersion,
                IconUrl = meta.IconUrl,
                ReadmeUrl = meta.ReadmeUrl,
                Source = meta.Source,
            };
            File.WriteAllText(DiskPath(mp), JsonSerializer.Serialize(entry,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Warning($"[插件市场] {mp.Id} 元数据缓存写入失败: {e.Message}");
        }
    }
}
