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

// 每日单词服务
using System.Text.Json;

namespace Glimpseon.Core.Services;

public static class WordService
{
    private const string ApiUrl = "https://uapis.cn/api/v1/daily/word";
    private const string CacheName = "daily_word";
    private const string CacheInterval = "12h";
    private const string WordCategory = "cet4";

    public static async Task<JsonElement?> FetchDailyWordAsync(bool useCache = true)
    {
        if (useCache)
        {
            var cached = AppUtils.GetCachedContent(CacheName);
            if (cached is not null)
            {
                Log.Debug("每日单词缓存命中");
                return cached;
            }
        }

        var data = await AppUtils.GetJsonAsync($"{ApiUrl}?category={WordCategory}");
        if (data is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("words", out var words)
            || words.ValueKind != JsonValueKind.Array
            || words.GetArrayLength() == 0
            || words[0].ValueKind != JsonValueKind.Object)
        {
            Log.Error("每日单词格式异常");
            return null;
        }
        var result = JsonSerializer.SerializeToElement(new
        {
            date = root.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "",
            word = words[0],
        });
        AppUtils.SaveCache(CacheName, result, CacheInterval);
        Log.Info("每日单词已获取");
        return result;
    }
}
