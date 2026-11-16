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

// DailyPhoto
using System.Text.Json;

namespace Glimpseon.Core.Services;

/// <summary>每日图片快照</summary>
public sealed record DailyPhotoInfo(string Path, string Title, string Copyright, string Date);

/// <summary>每日图片结果</summary>
public sealed record DailyPhotoFetch(DailyPhotoInfo Info, bool Stale);

/// <summary>每日一图</summary>
public static class DailyPhotoService
{
    private const string CacheName = "daily_photo";
    private const string Api = "https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=zh-CN";

    private static string ImagePath => Path.Combine(Paths.DataCache, "daily_photo.jpg");

    public static DailyPhotoInfo? FromCache()
    {
        if (AppUtils.GetCachedContent(CacheName, ignoreExpiry: true) is not { ValueKind: JsonValueKind.Object } content)
        {
            return null;
        }
        var path = content.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
        if (path.Length == 0 || !File.Exists(path))
        {
            return null;
        }
        return new DailyPhotoInfo(
            path,
            ReadString(content, "title"),
            ReadString(content, "copyright"),
            ReadString(content, "date"));
    }

    public static async Task<DailyPhotoFetch?> FetchAsync()
    {
        var today = DateTime.Now.ToString("yyyyMMdd");
        var cached = FromCache();
        if (cached is not null && cached.Date == today)
        {
            return new DailyPhotoFetch(cached, false);
        }
        try
        {
            var data = await AppUtils.GetJsonAsync(Api);
            if (data is not { ValueKind: JsonValueKind.Object } root ||
                !root.TryGetProperty("images", out var images) ||
                images.ValueKind != JsonValueKind.Array ||
                images.GetArrayLength() == 0)
            {
                Log.Warning("[PIC] 每日图片返回不完整");
                return Stale(cached);
            }
            var image = images[0];
            var urlBase = ReadString(image, "urlbase");
            if (urlBase.Length == 0)
            {
                return Stale(cached);
            }
            var url = $"https://www.bing.com{urlBase}_1920x1080.jpg";
            var bytes = await AppUtils.Http.GetByteArrayAsync(url);
            if (bytes.Length == 0)
            {
                return Stale(cached);
            }
            Directory.CreateDirectory(Paths.DataCache);
            await File.WriteAllBytesAsync(ImagePath, bytes);

            var info = new DailyPhotoInfo(
                ImagePath,
                ReadString(image, "title"),
                ReadString(image, "copyright"),
                today);
            var payload = JsonSerializer.SerializeToElement(new
            {
                path = info.Path,
                title = info.Title,
                copyright = info.Copyright,
                date = info.Date,
            });
            AppUtils.SaveCache(CacheName, payload, "12h");
            Log.Info($"[PIC] 每日图片已更新: {info.Path}");
            return new DailyPhotoFetch(info, false);
        }
        catch (Exception e)
        {
            Log.Warning($"[PIC] 每日图片获取失败: {e.Message}");
            return Stale(cached);
        }
    }

    private static DailyPhotoFetch? Stale(DailyPhotoInfo? cached) =>
        cached is null ? null : new DailyPhotoFetch(cached, true);

    private static string ReadString(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
}
