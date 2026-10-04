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

// 一言服务
using System.Text.Json;

namespace Glimpseon.Core.Services;

public static class PoetryService
{
    private static string Fallback => AppUtils.Tr("poetry.default");

    public static async Task<string?> GetPoetryAsync(string? apiUrl = null)
    {
        apiUrl ??= Config.PoetryApiUrl.Value;
        try
        {
            Log.Debug($"一言 api {apiUrl}");
            var text = (await AppUtils.Http.GetStringAsync(apiUrl)).Trim();
            if (string.IsNullOrEmpty(text))
            {
                Log.Warning("一言 返回空内容");
                return null;
            }
            try
            {
                using var doc = JsonDocument.Parse(text);
                var hitokoto = doc.RootElement.TryGetProperty("hitokoto", out var h) ? h.GetString()?.Trim() : null;
                if (!string.IsNullOrEmpty(hitokoto))
                {
                    var source = doc.RootElement.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String
                        ? f.GetString()
                        : doc.RootElement.TryGetProperty("from_who", out var fw) && fw.ValueKind == JsonValueKind.String ? fw.GetString() : null;
                    return string.IsNullOrEmpty(source) ? hitokoto : $"{hitokoto}——{source}";
                }
            }
            catch (Exception e)
            {
                Log.Debug($"一言 json 解析失败 用原始文本: {e.Message}");
            }
            Log.Debug($"一言返回非 json 文本 长度 {text.Length}");
            return text;
        }
        catch (Exception e)
        {
            Log.Error($"获取一言失败 {e.Message}");
            return null;
        }
    }

    public static async Task<string> GetPoetryWithCacheAsync()
    {
        var cached = AppUtils.GetCachedContent("poetry");
        if (cached is { } root && root.ValueKind == JsonValueKind.String)
        {
            Log.Debug("一言缓存命中");
            return root.GetString() ?? Fallback;
        }
        var text = await GetPoetryAsync();
        if (text is null)
        {
            // 请求失败保留旧值
            return Fallback;
        }
        AppUtils.SaveCache("poetry", JsonSerializer.SerializeToElement(text), Config.PoetryUpdateInterval.Value);
        return text;
    }
}
