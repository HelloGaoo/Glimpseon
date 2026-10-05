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

// 遥测
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sentry;

namespace Glimpseon.Core;

public static class Telemetry
{
    private const string PostHogHost = "https://us.i.posthog.com";
    private const int LogTailMaxLines = 200;
    private const int LogTailMaxChars = 32_768;

    private static readonly object Lock = new();
    private static IDisposable? _sentryHandle;
    private static bool _sentryActive;
    private static bool _exitSent;
    private static string _installId = "";
    private static string _telemetryId = "";
    private static string _dsn = "";
    private static string _posthogKey = "";

    private static string IdentityPath => Path.Combine(Paths.DataConfig, "telemetry.json");

    public static bool CrashEnabled => Config.CrashUpload.Value && !string.IsNullOrWhiteSpace(_dsn);
    public static bool UsageEnabled => Config.UsageUpload.Value && !string.IsNullOrWhiteSpace(_posthogKey);

    private static void LoadKeys()
    {
        try
        {
            var asm = typeof(Telemetry).Assembly;
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("telemetry.keys.json", StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                Log.Info("[遥测] 无key文件");
                return;
            }
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null)
            {
                return;
            }
            using var doc = JsonDocument.Parse(stream);
            _dsn = doc.RootElement.TryGetProperty("sentry_dsn", out var dsn) ? dsn.GetString() ?? "" : "";
            _posthogKey = doc.RootElement.TryGetProperty("posthog_key", out var key) ? key.GetString() ?? "" : "";
        }
        catch (Exception e)
        {
            Log.Warning($"[遥测] key读取失败: {e.Message}");
        }
    }

    public static void Init()
    {
        LoadKeys();
        try
        {
            LoadIdentity();
        }
        catch (Exception e)
        {
            Log.Warning($"[遥测] 身份读取失败: {e.Message}");
        }
        Config.CrashUpload.ValueChanged += _ => ApplyCrashState();
        Config.UsageUpload.ValueChanged += _ => ApplyCrashState();
        ApplyCrashState();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown("process_exit");
        CaptureUsage("app_start", new Dictionary<string, object?>
        {
            ["launch_source"] = AppUtils.AutoStartLaunch() ? "autostart" : "manual",
        });
        Log.Info($"[遥测] 就绪 崩溃上报={CrashEnabled} 用量上报={UsageEnabled}");
    }

    // 身份

    private static void LoadIdentity()
    {
        try
        {
            if (File.Exists(IdentityPath))
            {
                var node = JsonNode.Parse(File.ReadAllText(IdentityPath));
                _installId = node?["install_id"]?.GetValue<string>() ?? "";
                _telemetryId = node?["telemetry_id"]?.GetValue<string>() ?? "";
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[遥测] 身份文件损坏 重新生成: {e.Message}");
        }
        if (string.IsNullOrWhiteSpace(_installId))
        {
            _installId = Guid.NewGuid().ToString("N");
        }
        if (string.IsNullOrWhiteSpace(_telemetryId))
        {
            _telemetryId = Guid.NewGuid().ToString("N");
        }
        try
        {
            Directory.CreateDirectory(Paths.DataConfig);
            var obj = new JsonObject
            {
                ["install_id"] = _installId,
                ["telemetry_id"] = _telemetryId,
            };
            File.WriteAllText(IdentityPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Debug($"[遥测] 身份保存失败: {e.Message}");
        }
    }

    // Sentry 崩溃上报

    private static void ApplyCrashState()
    {
        lock (Lock)
        {
            if (CrashEnabled == _sentryActive)
            {
                return;
            }
            if (CrashEnabled)
            {
                try
                {
                    _sentryHandle?.Dispose();
                    _sentryHandle = SentrySdk.Init(options =>
                    {
                        options.Dsn = _dsn.Trim();
                        options.Release = Paths.Version;
                        options.Environment = "production";
                        options.AutoSessionTracking = true;
                        options.AttachStacktrace = true;
                        options.SendDefaultPii = false;
                        options.MaxBreadcrumbs = 100;
                        options.SetBeforeSend(e =>
                        {
                            try
                            {
                                e.User = new SentryUser { Id = _telemetryId };
                                e.SetTag("install_id", _installId);
                                e.SetTag("app_version", Paths.Version);
                                e.SetTag("os_version", Environment.OSVersion.VersionString);
                                var tail = ReadLogTail();
                                if (!string.IsNullOrWhiteSpace(tail))
                                {
                                    e.SetExtra("log_tail", tail);
                                }
                            }
                            catch
                            {
                                // 附带信息失败不阻断事件本身
                            }
                            return e;
                        });
                    });
                    _sentryActive = true;
                    Log.Info("[遥测] Sentry 已启用");
                }
                catch (Exception e)
                {
                    _sentryActive = false;
                    Log.Warning($"[遥测] Sentry 启用失败: {e.Message}");
                }
            }
            else if (_sentryActive)
            {
                _sentryActive = false;
                try
                {
                    SentrySdk.Flush(TimeSpan.FromSeconds(2));
                    _sentryHandle?.Dispose();
                }
                catch (Exception e)
                {
                    Log.Debug($"[遥测] Sentry 关闭失败: {e.Message}");
                }
                _sentryHandle = null;
                Log.Info("[遥测] Sentry 已停用");
            }
        }
    }

    // PostHog 

    public static void CaptureUsage(string eventName, Dictionary<string, object?>? extra = null)
    {
        if (!UsageEnabled)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await SendPostHog(eventName, extra);
            }
            catch (Exception e)
            {
                Log.Debug($"[遥测] PostHog 上报失败: {e.Message}");
            }
        });
    }

    private static async Task SendPostHog(string eventName, Dictionary<string, object?>? extra)
    {
        var properties = new Dictionary<string, object?>
        {
            ["install_id"] = _installId,
            ["app_version"] = Paths.Version,
            ["os_name"] = "Windows",
            ["os_version"] = Environment.OSVersion.VersionString,
            ["runtime"] = Environment.Version.ToString(),
            ["language"] = AppUtils.CurrentLanguageCode,
            ["$os"] = "Windows",
        };
        if (extra is not null)
        {
            foreach (var kv in extra)
            {
                properties[kv.Key] = kv.Value;
            }
        }
        var payload = new Dictionary<string, object?>
        {
            ["api_key"] = _posthogKey.Trim(),
            ["batch"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["event"] = eventName,
                    ["distinct_id"] = _telemetryId,
                    ["properties"] = properties,
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                },
            },
        };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await AppUtils.Http.PostAsync($"{PostHogHost}/batch/", content);
        if (!resp.IsSuccessStatusCode)
        {
            Log.Debug($"[遥测] PostHog 返回 {resp.StatusCode}");
        }
    }

    // 退出冲刷

    public static void Shutdown(string source)
    {
        lock (Lock)
        {
            if (_exitSent)
            {
                return;
            }
            _exitSent = true;
        }
        try
        {
            if (UsageEnabled)
            {
                SendPostHog("app_exit", new Dictionary<string, object?> { ["source"] = source })
                    .Wait(TimeSpan.FromSeconds(3));
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[遥测] 退出事件发送失败: {e.Message}");
        }
        if (_sentryActive)
        {
            try
            {
                SentrySdk.Flush(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // 失败忽略
            }
        }
    }

    // 读取当前日志尾部用于崩溃附带
    private static string ReadLogTail()
    {
        try
        {
            var path = Log.CurrentLogFilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return "";
            }
            var lines = new Queue<string>(LogTailMaxLines);
            using var reader = File.OpenText(path);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (lines.Count == LogTailMaxLines)
                {
                    lines.Dequeue();
                }
                lines.Enqueue(line);
            }
            var sb = new StringBuilder();
            foreach (var l in lines)
            {
                if (sb.Length + l.Length > LogTailMaxChars)
                {
                    break;
                }
                sb.AppendLine(l);
            }
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }
}
