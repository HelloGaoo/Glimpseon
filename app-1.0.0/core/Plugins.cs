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
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Glimpseon.UI;

namespace Glimpseon.Core;

/// <summary>宿主插件 API</summary>
public static class PluginApi
{
    public const int HostApiVersion = 1;
}

/// <summary>manifest.json</summary>
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

    public static PluginManifest? Load(string dir, out string error)
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
        if (m.Id != Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
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

/// <summary>插件生命周期</summary>
public abstract class GlimpseonPlugin
{
    public PluginContext Context { get; internal set; } = null!;
    public abstract void OnLoad();
    public virtual void OnUnload() { }
}

/// <summary>插件面 SDK</summary>
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

    /// <summary>读插件 config.json</summary>
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

    /// <summary>写插件 config.json</summary>
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

    /// <summary>注册主界面组件定义</summary>
    public void RegisterComponent(ComponentDefinition definition, Func<JsonElement?, Control> factory)
        => PluginService.RegisterComponent(Id, definition, factory);

    /// <summary>注册课表数据提供</summary>
    public void RegisterScheduleProvider(string name, Func<List<ScheduleRow>> provider)
        => PluginService.RegisterScheduleProvider(Id, name, provider);

    /// <summary>追加右下角公告</summary>
    public void PushAnnouncement(string text) => Announcements.Append(text);
}

/// <summary>插件加载状态</summary>
public sealed class LoadedPlugin
{
    public required PluginManifest Manifest { get; init; }
    public required string Directory { get; init; }
    public required bool Enabled { get; init; }
    public GlimpseonPlugin? Instance { get; set; }
    public string? Error { get; set; }
}

/// <summary>每插件独立 ALC</summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string entranceDll)
        : base($"plugin:{Path.GetFileNameWithoutExtension(entranceDll)}")
        => _resolver = new AssemblyDependencyResolver(entranceDll);

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name is "GlimpseonMain" or "Avalonia.Base" or "Avalonia.Controls"
            or "Avalonia.Skia" or "Avalonia.Markup" or "FluentAvalonia")
        {
            return null;
        }
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }
}

public static class PluginService
{
    /// <summary>目录名排序</summary>
    public static List<LoadedPlugin> Plugins { get; } = new();
    public static List<ComponentDefinition> PluginComponentDefinitions { get; } = new();
    private static readonly Dictionary<string, Func<JsonElement?, Control>> ComponentFactories = new();
    /// <summary>pluginId > (显示名, 提供者)</summary>
    public static Dictionary<string, (string Name, Func<List<ScheduleRow>> Provider)> ScheduleProviders { get; } = new();

    private static string EnabledMapPath => Path.Combine(Paths.DataConfig, "plugins.json");
    private static string PackagesCacheDir => Path.Combine(Paths.DataCache, "plugin-packages");

    // 加载

    public static void InitializePlugins()
    {
        Plugins.Clear();
        PluginComponentDefinitions.Clear();
        ComponentFactories.Clear();
        ScheduleProviders.Clear();
        try
        {
            Directory.CreateDirectory(Paths.DataPlugins);
        }
        catch (Exception e)
        {
            Log.Error($"[插件] 插件目录创建失败: {e.Message}");
            return;
        }
        var enabledMap = ReadEnabledMap();
        var scan = 0;
        var loaded = 0;
        var failed = 0;
        var disabled = 0;
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
            scan++;
            try
            {
                var lp = TryLoad(dir, enabledMap);
                Plugins.Add(lp);
                if (!lp.Enabled)
                {
                    disabled++;
                }
                else if (lp.Instance is not null)
                {
                    loaded++;
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception e)
            {
                failed++;
                Log.Error($"[插件] 加载异常 {Path.GetFileName(dir)}: {e.Message}");
                Plugins.Add(new LoadedPlugin
                {
                    Manifest = new PluginManifest { Id = Path.GetFileName(dir), Name = Path.GetFileName(dir) },
                    Directory = dir,
                    Enabled = true,
                    Error = $"加载异常: {e.Message}",
                });
            }
        }
        Log.Info($"[插件] 扫描 {scan} 启用 {scan - disabled} 加载成功 {loaded} 失败 {failed} 未启用 {disabled}");
    }

    private static LoadedPlugin TryLoad(string dir, Dictionary<string, bool> enabledMap)
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
                Error = null,
            };
        }
        var dllPath = Path.GetFullPath(Path.Combine(dir, manifest.Entrance));
        var alc = new PluginLoadContext(dllPath);
        Assembly asm;
        try
        {
            asm = alc.LoadFromAssemblyPath(dllPath);
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] {manifest.Id} 程序集加载失败: {e.Message}");
            return new LoadedPlugin
            {
                Manifest = manifest,
                Directory = dir,
                Enabled = true,
                Error = $"程序集加载失败: {e.Message}",
            };
        }
        Type? type;
        try
        {
            type = asm.GetTypes().FirstOrDefault(t => IsPluginType(t));
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
            return new LoadedPlugin
            {
                Manifest = manifest,
                Directory = dir,
                Enabled = true,
                Error = "未找到 GlimpseonPlugin 派生类",
            };
        }
        GlimpseonPlugin instance;
        try
        {
            instance = (GlimpseonPlugin)Activator.CreateInstance(type)!;
        }
        catch (Exception e)
        {
            return new LoadedPlugin
            {
                Manifest = manifest,
                Directory = dir,
                Enabled = true,
                Error = $"实例化失败: {e.Message}",
            };
        }
        instance.Context = new PluginContext(manifest, dir);
        try
        {
            instance.OnLoad();
        }
        catch (Exception e)
        {
            Log.Warning($"[插件] {manifest.Id} OnLoad 失败: {e}");
            return new LoadedPlugin
            {
                Manifest = manifest,
                Directory = dir,
                Enabled = true,
                Instance = instance,
                Error = $"OnLoad 失败: {e.Message}",
            };
        }
        return new LoadedPlugin
        {
            Manifest = manifest,
            Directory = dir,
            Enabled = true,
            Instance = instance,
            Error = null,
        };
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

    /// <summary>StyleWidgetFactory 前置查询</summary>
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

    // 启停 (data/config/plugins.json)

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

    /// <summary>启停开关</summary>
    public static void SetEnabled(string pluginId, bool enabled)
    {
        var map = ReadEnabledMap();
        map[pluginId] = enabled;
        WriteEnabledMap(map);
        Log.Info($"[插件] {pluginId} 启停={enabled} 重启后生效");
    }

    // 安装卸载

    /// <summary>安装 .gplugin 包</summary>
    public static (bool Ok, string Message) InstallPackage(string packagePath)
    {
        string? staging = null;
        try
        {
            using var zip = ZipFile.OpenRead(packagePath);
            var prefix = "";
            var entryNames = zip.Entries.Select(e => e.FullName).Where(n => n.Length > 0).ToList();
            var manifestEntry = zip.Entries.FirstOrDefault(e => e.FullName == "manifest.json");
            if (manifestEntry is null)
            {
                var firstSegs = entryNames.Select(n => n.Replace('\\', '/').Split('/')[0]).Distinct().ToList();
                if (firstSegs.Count == 1 && entryNames.Any(n => n == firstSegs[0] + "/manifest.json"))
                {
                    prefix = firstSegs[0] + "/";
                    manifestEntry = zip.Entries.First(e => e.FullName == prefix + "manifest.json");
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
                var target = Path.Combine(staging, name[prefix.Length..]);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
        }
        catch (Exception e)
        {
            CleanupStaging(staging);
            return (false, $"解压失败: {e.Message}");
        }
        var manifest = PluginManifest.Load(staging, out var error);
        if (manifest is null)
        {
            CleanupStaging(staging);
            return (false, error);
        }
        var targetDir = Path.Combine(Paths.DataPlugins, manifest.Id);
        try
        {
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
            Directory.Move(staging, targetDir);
        }
        catch (Exception e)
        {
            CleanupStaging(staging);
            return (false, $"落位失败 (旧版本目录可能被锁定): {e.Message}");
        }
        var map = ReadEnabledMap();
        map[manifest.Id] = true;
        WriteEnabledMap(map);
        Log.Info($"[插件] 已安装 {manifest.Id} v{manifest.Version} 重启后加载");
        return (true, manifest.Name);
    }

    /// <summary>卸载</summary>
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
            return (false, "插件已加载被锁定 请先禁用并重启后再卸载");
        }
        var map = ReadEnabledMap();
        if (map.Remove(pluginId))
        {
            WriteEnabledMap(map);
        }
        Log.Info($"[插件] 已卸载 {pluginId}");
        return (true, pluginId);
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

/// <summary>市场索引条目</summary>
public sealed class MarketPlugin
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Homepage { get; init; } = "";
    public string LatestVersion { get; init; } = "";
    public int LatestApiVersion { get; init; }
    public string DownloadUrl { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string PublishedAt { get; init; } = "";
}

/// <summary>市场 索引/下载/sha256/安装</summary>
public static class PluginMarket
{
    /// <summary>语义化版本比较</summary>
    public static int CompareVersions(string a, string b)
    {
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
        {
            return va.CompareTo(vb);
        }
        return string.CompareOrdinal(a, b);
    }

    private static string? ToJsDelivr(string? url)
    {
        // raw.githubusercontent.com/Owner/Repo/branch/path > cdn.jsdelivr.net/gh/Owner/Repo@branch/path
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Host != "raw.githubusercontent.com")
        {
            return null;
        }
        var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length < 4)
        {
            return null;
        }
        return $"https://cdn.jsdelivr.net/gh/{segs[0]}/{segs[1]}@{segs[2]}/{string.Join('/', segs[3..])}";
    }

    /// <summary>拉索引 源顺序: 配置源 > jsDelivr 镜像 > 旧缓存 (TTL 1天) </summary>
    public static async Task<(List<MarketPlugin>? Plugins, string? Error)> FetchIndexAsync()
    {
        var configured = Config.MarketSource.Value;
        var sources = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            sources.Add(configured);
        }
        var mirror = ToJsDelivr(configured);
        if (mirror is not null && mirror != configured)
        {
            sources.Add(mirror);
        }
        foreach (var source in sources)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"Glimpseon/{Paths.Version}");
                var text = await http.GetStringAsync(source);
                var element = JsonSerializer.Deserialize<JsonElement>(text);
                var plugins = ParseIndex(element);
                AppUtils.SaveCache("plugin-market", JsonSerializer.SerializeToElement(element), "1天");
                Log.Info($"[插件市场] 索引拉取成功 {plugins.Count}项 源={source}");
                return (plugins, null);
            }
            catch (Exception e)
            {
                Log.Warning($"[插件市场] 索引源失败 {source}: {e.Message}");
            }
        }
        var cached = AppUtils.GetCachedContent("plugin-market");
        if (cached is { } cache)
        {
            Log.Info("[插件市场] 索引源全部失败 用旧缓存");
            return (ParseIndex(cache), null);
        }
        return (null, "network");
    }

    private static List<MarketPlugin> ParseIndex(JsonElement root)
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
            string LS(string name) => latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            int LI(string name) => latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
            var id = S("id");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(LS("downloadUrl")))
            {
                continue;
            }
            list.Add(new MarketPlugin
            {
                Id = id,
                Name = S("name") is { Length: > 0 } n ? n : id,
                Author = S("author"),
                Description = S("description"),
                IconUrl = S("iconUrl"),
                Homepage = S("homepage"),
                LatestVersion = LS("version"),
                LatestApiVersion = LI("apiVersion"),
                DownloadUrl = LS("downloadUrl"),
                Sha256 = LS("sha256"),
                PublishedAt = LS("publishedAt"),
            });
        }
        return list;
    }

    /// <summary>下载 downloadUrl 到包缓存 > sha256 校验(索引为空则跳过) > InstallPackage</summary>
    /// <returns>(成功, 成功时为插件名 失败时错误码: "sha" 或具体错误)</returns>
    public static async Task<(bool Ok, string Message)> InstallFromMarketAsync(MarketPlugin plugin, Action<double>? progress = null)
    {
        Directory.CreateDirectory(Path.Combine(Paths.DataCache, "plugin-packages"));
        var dest = Path.Combine(Paths.DataCache, "plugin-packages", plugin.Id + ".gplugin");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Glimpseon/{Paths.Version}");
            using var resp = await http.GetAsync(plugin.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
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
        }
        catch (Exception e)
        {
            TryDelete(dest);
            return (false, $"下载失败: {e.Message}");
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
        var (ok, message) = PluginService.InstallPackage(dest);
        TryDelete(dest);
        return (ok, message);
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
            // 清理失败不影响主流程
        }
    }
}
