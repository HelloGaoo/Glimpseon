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

// AI 服务
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Glimpseon.Core.Services;

/// <summary>单次调用结果</summary>
public sealed record AiResult(bool Ok, string Content, string Error, long LatencyMs)
{
    public static AiResult Fail(string error) => new(false, "", error, 0);
}

public static class AiService
{
    public static (ConfigItem<string> BaseUrl, ConfigItem<string> Key, ConfigItem<string> Model, ConfigItem<string> Prompt) Slot(string provider) => provider switch
    {
        "zhipu" => (Config.AiZhipuBaseUrl, Config.AiZhipuApiKey, Config.AiZhipuModel, Config.AiZhipuPrompt),
        "deepseek" => (Config.AiDeepseekBaseUrl, Config.AiDeepseekApiKey, Config.AiDeepseekModel, Config.AiDeepseekPrompt),
        "custom" => (Config.AiCustomBaseUrl, Config.AiCustomApiKey, Config.AiCustomModel, Config.AiCustomPrompt),
        _ => (Config.AiDefaultBaseUrl, Config.AiDefaultApiKey, Config.AiDefaultModel, Config.AiDefaultPrompt),
    };

    public static (string BaseUrl, string Key, string Model) Resolve()
    {
        var (b, k, m, _) = Slot(Config.AiProvider.Value);
        return (b.Value.Trim(), k.Value.Trim(), m.Value.Trim());
    }

    // 组件调用前置条件
    public static bool Ready => Config.AiEnabled.Value && Resolve().BaseUrl.Length > 0;

    // 单次对话 非流式
    public static async Task<AiResult> ChatAsync(string prompt, string? system = null)
    {
        var (url, key, model) = Resolve();
        if (url.Length == 0)
        {
            return AiResult.Fail("接口地址为空");
        }
        var sw = Stopwatch.StartNew();
        try
        {
            var messages = new JsonArray();
            if (!string.IsNullOrWhiteSpace(system))
            {
                messages.Add(new JsonObject { ["role"] = "system", ["content"] = system });
            }
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = prompt });
            var body = new JsonObject
            {
                ["model"] = model,
                ["messages"] = messages,
                ["temperature"] = Config.AiTemperature.Value,
                ["max_tokens"] = Config.AiMaxTokens.Value,
            };
            var json = body.ToJsonString();
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (key.Length > 0)
            {
                req.Headers.Authorization = new("Bearer", key);
            }
            if (url.Contains("pollinations.ai"))
            {
                // 匿名档带 Referer
                req.Headers.Referrer = new Uri("https://pollinations.ai");
            }
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Config.AiTimeout.Value));
            Log.Info($"[AI] {Config.AiProvider.Value} 请求 {url} model={model} temperature={Config.AiTemperature.Value} max_tokens={Config.AiMaxTokens.Value}");
            Log.Info($"[AI] 请求体 {json}");
            using var resp = await AppUtils.Http.SendAsync(req, cts.Token);
            var text = await resp.Content.ReadAsStringAsync(cts.Token);
            Log.Info($"[AI] 响应 {(int)resp.StatusCode} {sw.ElapsedMilliseconds}ms {text}");
            if (!resp.IsSuccessStatusCode)
            {
                var retry = resp.Headers.RetryAfter?.Delta?.TotalSeconds;
                var detail = text.Length > 120 ? text[..120] + "…" : text;
                return AiResult.Fail($"HTTP {(int)resp.StatusCode}{(retry is { } r ? $" Retry-After {r:0}s" : "")} {detail}".TrimEnd());
            }
            var content = ((JsonNode.Parse(text) as JsonObject)?["choices"] as JsonArray)?[0]?["message"]?["content"]?.ToString() ?? "";
            return string.IsNullOrWhiteSpace(content)
                ? AiResult.Fail("回复为空")
                : new AiResult(true, content.Trim(), "", sw.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            Log.Error($"[AI] 调用失败 {e.GetType().Name}: {e.Message}");
            return AiResult.Fail($"{e.GetType().Name}: {e.Message}");
        }
    }
}
