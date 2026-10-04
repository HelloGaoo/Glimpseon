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


// 每日一句服务
using System.Text.Json;

namespace Glimpseon.Core.Services;

public static class SentenceService
{
    private const string ApiUrl = "https://api.timelessq.com/english-sentence";
    private const string CacheName = "daily_sentence";
    private const string CacheInterval = "12h";

    public static async Task<JsonElement?> FetchDailySentenceAsync(bool useCache = true)
    {
        if (useCache)
        {
            var cached = AppUtils.GetCachedContent(CacheName);
            if (cached is not null)
            {
                Log.Debug("每日一句缓存命中");
                return cached;
            }
        }

        var data = await AppUtils.GetJsonAsync(ApiUrl);
        if (data is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("data", out var item)
            || item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("content", out var content)
            || string.IsNullOrEmpty(content.GetString()))
        {
            Log.Error("每日一句数据格式异常");
            return null;
        }
        var result = JsonSerializer.SerializeToElement(new
        {
            date = item.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "",
            sentence = new
            {
                content = content.GetString() ?? "",
                note = item.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "",
                translation = item.TryGetProperty("translation", out var t) ? t.GetString() ?? "" : "",
            },
        });
        AppUtils.SaveCache(CacheName, result, CacheInterval);
        Log.Info("每日一句已获取");
        return result;
    }
}
