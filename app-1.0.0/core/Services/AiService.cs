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

/// <summary>单次调用结果 Reasoning=模型思考过程</summary>
public sealed record AiResult(bool Ok, string Content, string Reasoning, string Error, long LatencyMs)
{
    public static AiResult Fail(string error) => new(false, "", "", error, 0);
}

/// <summary>流式回调 IO 线程触发</summary>
public sealed record AiStreamHandlers(Action<string>? OnReasoning = null, Action<string>? OnContent = null);

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

    // 组请求体
    private static JsonObject BuildBody(string model, string prompt, string? system, IReadOnlyList<(string Role, string Content)>? history, bool stream)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrWhiteSpace(system))
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = system });
        }
        if (history is not null)
        {
            foreach (var (role, hc) in history)
            {
                if ((role == "user" || role == "assistant") && !string.IsNullOrWhiteSpace(hc))
                {
                    messages.Add(new JsonObject { ["role"] = role, ["content"] = hc });
                }
            }
        }
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = prompt });
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = Config.AiTemperature.Value,
            ["max_tokens"] = Config.AiMaxTokens.Value,
        };
        if (stream)
        {
            body["stream"] = true;
        }
        return body;
    }

    // 单次对话 非流式 history 附上下文
    public static async Task<AiResult> ChatAsync(string prompt, string? system = null, IReadOnlyList<(string Role, string Content)>? history = null)
    {
        var (url, key, model) = Resolve();
        if (url.Length == 0)
        {
            return AiResult.Fail("接口地址为空");
        }
        var sw = Stopwatch.StartNew();
        try
        {
            var body = BuildBody(model, prompt, system, history, stream: false);
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
            var msgObj = ((JsonNode.Parse(text) as JsonObject)?["choices"] as JsonArray)?[0]?["message"];
            var content = msgObj?["content"]?.ToString() ?? "";
            var reasoning = msgObj?["reasoning_content"]?.ToString() ?? "";
            return string.IsNullOrWhiteSpace(content)
                ? AiResult.Fail("回复为空")
                : new AiResult(true, content.Trim(), reasoning.Trim(), "", sw.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            Log.Error($"[AI] 调用失败 {e.GetType().Name}: {e.Message}");
            return AiResult.Fail($"{e.GetType().Name}: {e.Message}");
        }
    }

    // SSE 流式 非流式回退 取消留 partial
    public static async Task<AiResult> StreamChatAsync(string prompt, AiStreamHandlers handlers, string? system = null, IReadOnlyList<(string Role, string Content)>? history = null, CancellationToken ct = default)
    {
        var (url, key, model) = Resolve();
        if (url.Length == 0)
        {
            return AiResult.Fail("接口地址为空");
        }
        var sw = Stopwatch.StartNew();
        var contentBuf = new StringBuilder();
        var reasoningBuf = new StringBuilder();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(BuildBody(model, prompt, system, history, stream: true).ToJsonString(), Encoding.UTF8, "application/json"),
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
            // 超时仅握手 token 联动停生
            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sendCts.CancelAfter(TimeSpan.FromSeconds(Config.AiTimeout.Value));
            Log.Info($"[AI] 流式 {Config.AiProvider.Value} 请求 {url} model={model} temperature={Config.AiTemperature.Value} max_tokens={Config.AiMaxTokens.Value}");
            using var resp = await AppUtils.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, sendCts.Token);
            Log.Info($"[AI] 流式响应 {(int)resp.StatusCode} {sw.ElapsedMilliseconds}ms");
            if (!resp.IsSuccessStatusCode)
            {
                using var errCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var errText = await resp.Content.ReadAsStringAsync(errCts.Token);
                var retry = resp.Headers.RetryAfter?.Delta?.TotalSeconds;
                var detail = errText.Length > 120 ? errText[..120] + "…" : errText;
                Log.Error($"[AI] 流式失败 HTTP {(int)resp.StatusCode} {errText}");
                return AiResult.Fail($"HTTP {(int)resp.StatusCode}{(retry is { } r ? $" Retry-After {r:0}s" : "")} {detail}".TrimEnd());
            }
            if (!(resp.Content.Headers.ContentType?.MediaType?.Contains("event-stream") ?? true))
            {
                // 非 SSE 回退整段解析
                using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                fallbackCts.CancelAfter(TimeSpan.FromSeconds(Config.AiTimeout.Value));
                var text = await resp.Content.ReadAsStringAsync(fallbackCts.Token);
                var msgObj = ((JsonNode.Parse(text) as JsonObject)?["choices"] as JsonArray)?[0]?["message"];
                var content = msgObj?["content"]?.ToString() ?? "";
                var reasoning = msgObj?["reasoning_content"]?.ToString() ?? "";
                Log.Info($"[AI] 非流式回退 {sw.ElapsedMilliseconds}ms {text}");
                return string.IsNullOrWhiteSpace(content)
                    ? AiResult.Fail("回复为空")
                    : new AiResult(true, content.Trim(), reasoning.Trim(), "", sw.ElapsedMilliseconds);
            }
            using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync());
            while (await reader.ReadLineAsync() is { } line)
            {
                if (ct.IsCancellationRequested)
                {
                    return new AiResult(false, contentBuf.ToString(), reasoningBuf.ToString(), "已停止", sw.ElapsedMilliseconds);
                }
                if (line.Length == 0 || !line.StartsWith("data:"))
                {
                    continue;
                }
                var payload = line["data:".Length..].Trim();
                if (payload == "[DONE]")
                {
                    break;
                }
                if (JsonNode.Parse(payload) is not JsonObject obj)
                {
                    continue;
                }
                if (obj["error"] is { } errNode)
                {
                    Log.Error($"[AI] 流式错误帧 {errNode}");
                    return new AiResult(false, contentBuf.ToString(), reasoningBuf.ToString(), errNode.ToString(), sw.ElapsedMilliseconds);
                }
                var delta = ((obj["choices"] as JsonArray)?[0] as JsonObject)?["delta"] as JsonObject;
                if (delta?["reasoning_content"]?.ToString() is { } rd && rd.Length > 0)
                {
                    reasoningBuf.Append(rd);
                    handlers.OnReasoning?.Invoke(rd);
                }
                if (delta?["content"]?.ToString() is { } cd && cd.Length > 0)
                {
                    contentBuf.Append(cd);
                    handlers.OnContent?.Invoke(cd);
                }
            }
            return contentBuf.Length == 0
                ? AiResult.Fail("回复为空")
                : new AiResult(true, contentBuf.ToString().Trim(), reasoningBuf.ToString().Trim(), "", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 停生留 partial
            return new AiResult(false, contentBuf.ToString(), reasoningBuf.ToString(), "已停止", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 握手超时
            Log.Error("[AI] 流式握手超时");
            return AiResult.Fail("连接超时");
        }
        catch (Exception e)
        {
            Log.Error($"[AI] 流式调用失败 {e.GetType().Name}: {e.Message}");
            return AiResult.Fail($"{e.GetType().Name}: {e.Message}");
        }
    }
}
