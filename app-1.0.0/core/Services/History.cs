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

// 历史上的今天服务
using System.Text.Json;

namespace Glimpseon.Core.Services;

public static class HistoryService
{
    private const string ApiUrl = "https://tmini.net/api/today";
    private const string CacheName = "history_today";
    private const string CacheInterval = "12h";

    public static async Task<JsonElement?> FetchHistoryTodayAsync(bool useCache = true)
    {
        if (useCache)
        {
            var cached = AppUtils.GetCachedContent(CacheName);
            if (cached is not null)
            {
                Log.Debug("历史数据缓存命中");
                return cached;
            }
        }

        var data = await AppUtils.GetJsonAsync($"{ApiUrl}?type=json");
        if (data is not { ValueKind: JsonValueKind.Object } root)
        {
            return null;
        }
        var codeOk = root.TryGetProperty("code", out var c) && (c.ValueKind == JsonValueKind.Number && c.GetInt32() == 200 || c.ValueKind == JsonValueKind.String && c.GetString() == "200");
        if (!codeOk || !root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            Log.Error("历史数据格式异常");
            return null;
        }
        var result = JsonSerializer.SerializeToElement(new
        {
            date = root.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "",
            events,
        });
        AppUtils.SaveCache(CacheName, result, CacheInterval);
        Log.Info($"历史已获取 {events.GetArrayLength()}条");
        return result;
    }
}
