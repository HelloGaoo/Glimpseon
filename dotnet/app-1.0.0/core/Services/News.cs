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

// 新闻服务
using System.Text.Json;

namespace Glimpseon.Core.Services;

public static class NewsService
{
    private const string CctvNewsApiUrl = "https://api.xcvts.cn/api/hotlist/ysxw?type=json";
    private const string DailyNewsApiUrl = "https://news.orz.ai/api/v1/dailynews/";
    private static readonly string[] SupportedPlatforms = { "baidu", "weibo", "jinritoutiao", "tenxunwang" };
    private const string CacheInterval = "30m";

    public static async Task<JsonElement?> FetchCctvNewsAsync(bool useCache = true)
    {
        const string cacheName = "news_cctv";
        if (useCache)
        {
            var cached = AppUtils.GetCachedContent(cacheName);
            if (cached is not null)
            {
                Log.Debug("央视新闻缓存命中");
                return cached;
            }
        }

        var data = await AppUtils.GetJsonAsync(CctvNewsApiUrl);
        if (data is null)
        {
            return null;
        }
        JsonElement list;
        if (data.Value.ValueKind == JsonValueKind.Object)
        {
            list = data.Value.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array
                ? d
                : data.Value.TryGetProperty("news", out var n) && n.ValueKind == JsonValueKind.Array ? n : JsonSerializer.SerializeToElement(Array.Empty<object>());
        }
        else if (data.Value.ValueKind == JsonValueKind.Array)
        {
            list = data.Value;
        }
        else
        {
            list = JsonSerializer.SerializeToElement(Array.Empty<object>());
        }
        AppUtils.SaveCache(cacheName, list, CacheInterval);
        Log.Info($"央视新闻已获取 {list.GetArrayLength()}条");
        return list;
    }

    public static async Task<JsonElement?> FetchDailyNewsAsync(string platform, bool useCache = true)
    {
        platform = platform.Trim().ToLowerInvariant();
        if (!SupportedPlatforms.Contains(platform))
        {
            Log.Warning($"不支持的平台 {platform}");
            return null;
        }
        var cacheName = $"news_{platform}";
        if (useCache)
        {
            var cached = AppUtils.GetCachedContent(cacheName);
            if (cached is not null)
            {
                Log.Debug($"每日新闻缓存命中: {platform}");
                return cached;
            }
        }

        var data = await AppUtils.GetJsonAsync($"{DailyNewsApiUrl}?platform={platform}");
        if (data is null)
        {
            return null;
        }
        JsonElement? list = null;
        if (data.Value.ValueKind == JsonValueKind.Object)
        {
            if (data.Value.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array)
            {
                list = d;
            }
            else if (data.Value.TryGetProperty("status", out var s) && ((s.ValueKind == JsonValueKind.Number && s.GetInt32() == 200) || (s.ValueKind == JsonValueKind.String && s.GetString() == "200")))
            {
                list = JsonSerializer.SerializeToElement(Array.Empty<object>());
            }
        }
        else if (data.Value.ValueKind == JsonValueKind.Array)
        {
            list = data.Value;
        }
        if (list is not { ValueKind: JsonValueKind.Array })
        {
            Log.Error("每日新闻获取失败");
            return null;
        }
        AppUtils.SaveCache(cacheName, list.Value, CacheInterval);
        Log.Info($"每日新闻已获取 {platform} {list.Value.GetArrayLength()}条");
        return list;
    }
}
