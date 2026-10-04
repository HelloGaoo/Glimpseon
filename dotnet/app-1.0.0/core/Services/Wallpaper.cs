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

// 壁纸服务

using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Glimpseon.Core;

namespace Glimpseon.Core.Services;

public sealed class WallpaperRecord
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Source { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public string AddedTime { get; set; } = "";
    public long FileSize { get; set; }
    public string Resolution { get; set; } = "未知";

    public bool Exists() => File.Exists(Path);

    public JsonObject ToJson() => new()
    {
        ["id"] = Id,
        ["path"] = Path,
        ["source"] = Source,
        ["api_url"] = ApiUrl,
        ["added_time"] = AddedTime,
        ["file_size"] = FileSize,
        ["resolution"] = Resolution,
    };

    public static WallpaperRecord FromJson(JsonObject obj) => new()
    {
        Id = obj["id"]?.GetValue<string>() ?? "",
        Path = obj["path"]?.GetValue<string>() ?? "",
        Source = obj["source"]?.GetValue<string>() ?? "",
        ApiUrl = obj["api_url"]?.GetValue<string>() ?? "",
        AddedTime = obj["added_time"]?.GetValue<string>() ?? "",
        FileSize = obj["file_size"]?.GetValue<long>() ?? 0,
        Resolution = obj["resolution"]?.GetValue<string>() ?? "未知",
    };
}

public sealed class WallpaperHistory
{
    public const string HistoryFileName = "history.json";
    public const int HistoryVersion = 1;
    public const int MaxHistoryRecords = 100;

    private readonly List<WallpaperRecord> _history = new();
    private readonly string _historyFile;

    public WallpaperHistory()
    {
        Directory.CreateDirectory(Paths.WallpaperDir);
        _historyFile = Path.Combine(Paths.WallpaperDir, HistoryFileName);
        Load();
        ClearInvalid();
        Log.Debug($"壁纸历史就绪 {Count}条 {_historyFile}");
    }

    public int Count => _history.Count;
    public IReadOnlyList<WallpaperRecord> Records => _history;

    private void Load()
    {
        if (!File.Exists(_historyFile))
        {
            Log.Debug($"跳过加载 {_historyFile}");
            return;
        }
        try
        {
            var data = JsonNode.Parse(File.ReadAllText(_historyFile)) as JsonObject;
            if (data?["version"]?.GetValue<int>() != HistoryVersion)
            {
                Log.Warning($"壁纸历史文件版本不匹配 重置历史");
                return;
            }
            if (data["history"] is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JsonObject obj)
                    {
                        _history.Add(WallpaperRecord.FromJson(obj));
                    }
                }
            }
            Log.Debug($"壁纸历史文件已加载: {Count}条");
        }
        catch (Exception e)
        {
            Log.Error($"加载壁纸历史记录失败 {e.Message}");
            _history.Clear();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.WallpaperDir);
            var arr = new JsonArray(_history.Select(r => r.ToJson()).ToArray());
            var data = new JsonObject
            {
                ["version"] = HistoryVersion,
                ["history"] = arr,
            };
            File.WriteAllText(_historyFile, data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Log.Debug($"壁纸历史已写入: {_historyFile} 共 {Count} 条");
        }
        catch (Exception e)
        {
            Log.Error($"保存壁纸历史记录失败 {e.Message}");
        }
    }

    public WallpaperRecord? Add(string path, string source, string apiUrl)
    {
        if (!File.Exists(path))
        {
            Log.Warning($"跳过写入历史 {path}");
            return null;
        }
        var recordId = System.IO.Path.GetFileNameWithoutExtension(path);
        var existing = _history.FirstOrDefault(r => r.Id == recordId);
        if (existing is not null)
        {
            Log.Debug($"壁纸记录已存在 移到历史首位: {recordId}");
            _history.Remove(existing);
            _history.Insert(0, existing);
            Save();
            return existing;
        }
        long fileSize = 0;
        try
        {
            fileSize = new FileInfo(path).Length;
        }
        catch (Exception e)
        {

            Log.Debug($"[壁纸] 大小读取失败 按 0: {e.Message}");
            // 大小读取失败 按 0
        }
        var resolution = "未知";
        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            resolution = $"{bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}";
        }
        catch (Exception e)
        {
            Log.Warning($"壁纸分辨率读取失败: {path} - {e.Message}");
        }
        var record = new WallpaperRecord
        {
            Id = recordId,
            Path = path,
            Source = source,
            ApiUrl = apiUrl,
            AddedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            FileSize = fileSize,
            Resolution = resolution,
        };
        _history.Insert(0, record);
        while (_history.Count > MaxHistoryRecords)
        {
            _history.RemoveAt(_history.Count - 1);
        }
        Save();
        Log.Info($"壁纸入历史: {record.Id} ({source} {resolution} {fileSize / 1024}KB 共{Count}条)");
        return record;
    }

    public void Remove(string recordId)
    {
        var record = _history.FirstOrDefault(r => r.Id == recordId);
        if (record is not null)
        {
            _history.Remove(record);
            Save();
        }
    }

    public void ClearInvalid()
    {
        var removed = _history.RemoveAll(r => !r.Exists());
        if (removed > 0)
        {
            Save();
            Log.Debug($"已清理无效壁纸记录 {removed}条");
        }
    }

    // 文件数超上限时按最旧删除
    public int SyncCleanup(int? maxFiles = null)
    {
        var limit = maxFiles ?? Config.WallpaperSaveLimit.Value;
        if (!Directory.Exists(Paths.WallpaperDir))
        {
            Log.Debug($"跳过清理 {Paths.WallpaperDir}");
            return 0;
        }
        var wallpapers = Directory.EnumerateFiles(Paths.WallpaperDir)
            .Where(f => f.EndsWith(".jpg") && System.IO.Path.GetFileName(f).StartsWith("wallpaper_"))
            .Select(f => (Path: f, Mtime: File.GetLastWriteTime(f)))
            .OrderBy(x => x.Mtime)
            .ToList();
        var deleted = 0;
        while (wallpapers.Count > limit)
        {
            var old = wallpapers[0];
            wallpapers.RemoveAt(0);
            try
            {
                File.Delete(old.Path);
                Remove(System.IO.Path.GetFileNameWithoutExtension(old.Path));
                deleted++;
                Log.Debug($"已删除过期壁纸文件: {old.Path}");
            }
            catch (Exception e)
            {
                Log.Warning($"删除壁纸失败 {old.Path}: {e.Message}");
            }
        }
        if (deleted > 0)
        {
            Log.Info($"共删除{deleted}个壁纸");
        }
        else
        {
            Log.Debug("壁纸目录无需清理: 文件数在保留上限内");
        }
        return deleted;
    }
}

public static class WallpaperService
{
    private static WallpaperHistory? _history;
    public static WallpaperHistory History => _history ??= new WallpaperHistory();

    // API 表
    public static (string Url, string Source) GetApiUrl()
    {
        var api = Config.WallpaperApi.Value;
        var map = new Dictionary<string, (string, string)>
        {
            ["api.ltyuanfang.cn"] = ("https://tu.ltyuanfang.cn/api/fengjing.php", "api.ltyuanfang.cn"),
            ["imlcd.cn_bg_high"] = ("https://api.imlcd.cn/bg/high.php", "imlcd.cn_bg_high"),
            ["imlcd.cn_bg_mc"] = ("https://api.imlcd.cn/bg/mc.php", "imlcd.cn_bg_mc"),
            ["imlcd.cn_bg_gq"] = ("https://api.imlcd.cn/bg/gq.php", "imlcd.cn_bg_gq"),
        };
        Log.Info($"用壁纸 api: {api}");
        if (map.TryGetValue(api, out var hit))
        {
            return hit;
        }
        Log.Debug($"api {api} 无映射 用默认: wp.upx8.com");
        return ("https://wp.upx8.com/api.php?content=风景", "wp.upx8.com");
    }

    // 获取壁纸 下载到本地 写缓存 入历史 清理
    public static async Task<string?> FetchAsync()
    {
        var (url, source) = GetApiUrl();
        Log.Info($"请求壁纸 url: {url}");
        try
        {
            // 手动重定向
            HttpResponseMessage? response = null;
            var currentUrl = url;
            for (var hop = 0; hop < 5; hop++)
            {
                response?.Dispose();
                response = await AppUtils.Http.GetAsync(currentUrl, HttpCompletionOption.ResponseHeadersRead);
                var status = (int)response.StatusCode;
                if (status is 301 or 302 or 307 or 308 && response.Headers.Location is { } loc)
                {
                    currentUrl = loc.IsAbsoluteUri ? loc.AbsoluteUri : new Uri(new Uri(currentUrl), loc).AbsoluteUri;
                    Log.Debug($"壁纸重定向 {status} -> {currentUrl}");
                    response.Dispose();
                    response = null;
                    continue;
                }
                break;
            }
            using (response)
            {
                if (response is null || !response.IsSuccessStatusCode)
                {
                    Log.Error($"获取壁纸失败 状态码: {response?.StatusCode}");
                    return null;
                }
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length == 0)
                {
                    Log.Error("壁纸响应为空");
                    return null;
                }
                Directory.CreateDirectory(Paths.WallpaperDir);
                var path = Path.Combine(Paths.WallpaperDir, $"wallpaper_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                await File.WriteAllBytesAsync(path, bytes);
                Log.Info($"壁纸已保存 {path}");
                SaveCacheFor(path, source, url);
                History.SyncCleanup();
                History.Add(path, source, url);
                return path;
            }
        }
        catch (Exception e)
        {
            Log.Error($"获取壁纸失败 {e.Message}");
            return null;
        }
    }

    public static void SaveCacheFor(string path, string source, string url)
    {
        var payload = JsonSerializer.SerializeToElement(new { path, source, url });
        AppUtils.SaveCache("wallpaper", payload, Config.AutoGetInterval.Value);
    }

    public static string? LoadCachePath()
    {
        var cached = AppUtils.GetCachedContent("wallpaper", ignoreExpiry: true);
        if (cached is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("path", out var pathEl))
        {
            var path = pathEl.GetString();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    public static bool SetDesktop(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warning($"设置桌面壁纸 文件不存在: {path}");
            return false;
        }
        Log.Info($"设置壁纸路径: {path}");
        var ok = Win32.Native.SetDesktopWallpaper(path);
        if (ok)
        {
            Log.Info("壁纸已同步桌面");
        }
        return ok;
    }
}
