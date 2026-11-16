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

// ExchangeRates
using System.Text.Json;

namespace Glimpseon.Core.Services;

/// <summary>汇率快照</summary>
public sealed record ExchangeSnapshot(DateTimeOffset Updated, Dictionary<string, double> Rates);

/// <summary>汇率结果</summary>
public sealed record ExchangeFetch(ExchangeSnapshot Snapshot, bool Fallback);

/// <summary>汇率拉取</summary>
public static class ExchangeRateService
{
    private const string CacheName = "exchange_rates";
    private const string Api = "https://open.er-api.com/v6/latest/CNY";

    public static ExchangeSnapshot? FromCache(bool ignoreExpiry = false) =>
        Parse(AppUtils.GetCachedContent(CacheName, ignoreExpiry));

    public static async Task<ExchangeFetch?> FetchAsync(bool useCache = true)
    {
        if (useCache && FromCache() is { } cached)
        {
            return new ExchangeFetch(cached, false);
        }
        try
        {
            var data = await AppUtils.GetJsonAsync(Api);
            if (data is not { } raw || Parse(raw) is not { } snapshot)
            {
                Log.Warning("[FX] 汇率返回不完整");
                return Fallback();
            }
            AppUtils.SaveCache(CacheName, raw, "1h");
            return new ExchangeFetch(snapshot, false);
        }
        catch (Exception e)
        {
            Log.Warning($"[FX] 汇率获取失败: {e.Message}");
            return Fallback();
        }
    }

    private static ExchangeFetch? Fallback() =>
        FromCache(ignoreExpiry: true) is { } snapshot ? new ExchangeFetch(snapshot, true) : null;

    private static ExchangeSnapshot? Parse(JsonElement? content)
    {
        if (content is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty("rates", out var rates) ||
            rates.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var map = new Dictionary<string, double>();
        foreach (var p in rates.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Number)
            {
                map[p.Name] = p.Value.GetDouble();
            }
        }
        var updated = DateTimeOffset.Now;
        if (root.TryGetProperty("time_last_update_unix", out var u) && u.ValueKind == JsonValueKind.Number)
        {
            try
            {
                updated = DateTimeOffset.FromUnixTimeSeconds(u.GetInt64());
            }
            catch (Exception e)
            {
                Log.Debug($"[FX] 更新时间解析失败: {e.Message}");
            }
        }
        return map.Count > 0 ? new ExchangeSnapshot(updated, map) : null;
    }
}
