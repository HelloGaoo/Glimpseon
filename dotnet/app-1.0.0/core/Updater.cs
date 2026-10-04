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

// 软件更新模块
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Glimpseon.Core;

public static class Updater
{
    private const string GithubApi = "https://api.github.com/repos/HelloGaoo/Glimpseon/releases/latest";
    private const string GithubReleases = "https://github.com/HelloGaoo/Glimpseon/releases";

    public sealed record UpdateCheckResult
    {
        public bool Success { get; init; }
        public string? Version { get; init; }
        public string? DownloadUrl { get; init; }
        public string? Changelog { get; init; }
        public string? Error { get; init; }
    }

    // 获取更新日志
    public static async Task<string?> GetGithubChangelogAsync(int maxRetries = 3)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    Log.Info("重试获取更新日志");
                }
                Log.Info($"获取更新日志 {GithubApi}");
                using var response = await AppUtils.Http.GetAsync(GithubApi);
                Log.Debug($"更新日志请求返回 {(int)response.StatusCode}");
                response.EnsureSuccessStatusCode();
                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                var body = json?["body"]?.GetValue<string>();
                Log.Debug($"更新日志内容长度 {(body?.Length ?? 0)}");
                return body;
            }
            catch (TaskCanceledException)
            {
                Log.Warning("请求超时");
                if (attempt == maxRetries - 1)
                {
                    Log.Error("获取更新日志失败");
                    return null;
                }
            }
            catch (Exception e)
            {
                Log.Error($"获取更新日志失败 {e.Message}");
                return null;
            }
        }
        return null;
    }

    // 检查 GitHub 最新版本
    public static async Task<UpdateCheckResult> CheckGithubVersionAsync(int maxRetries = 3)
    {
        string? lastErr = null;
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                Log.Info($"获取版本信息 {GithubApi}");
                response = await AppUtils.Http.GetAsync(GithubApi);
                response.EnsureSuccessStatusCode();
                break;
            }
            catch (TaskCanceledException)
            {
                lastErr = "请求超时";
                Log.Warning($"{lastErr} ({attempt + 1}/{maxRetries})");
                response?.Dispose();
                response = null;
            }
            catch (Exception e)
            {
                lastErr = $"网络错误 {e.Message}";
                Log.Warning($"{lastErr} ({attempt + 1}/{maxRetries})");
                response?.Dispose();
                response = null;
            }
        }

        if (response is null)
        {
            return new UpdateCheckResult { Error = lastErr ?? "请求失败" };
        }

        try
        {
            using (response)
            {
                var releaseInfo = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                var latestVersion = releaseInfo?["tag_name"]?.GetValue<string>() ?? "";

                if (latestVersion.StartsWith('v'))
                {
                    latestVersion = latestVersion[1..];
                }
                Log.Debug($"接口返回版本号: {latestVersion}");

                string? downloadUrl = null;
                if (releaseInfo?["assets"] is JsonArray assets)
                {
                    foreach (var asset in assets)
                    {
                        var name = asset?["name"]?.GetValue<string>() ?? "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset["browser_download_url"]?.GetValue<string>();
                            break;
                        }
                    }
                }

                if (downloadUrl is not null)
                {
                    Log.Info($"找到更新包: {downloadUrl}");
                }
                else
                {
                    Log.Warning($"最新版本 {latestVersion} 无 .zip 更新包");
                }

                var changelog = releaseInfo?["body"]?.GetValue<string>();
                if (string.IsNullOrEmpty(changelog))
                {
                    Log.Warning("Release 无描述 用默认内容");
                    changelog = $"# 版本 {latestVersion}\n\n请访问 [Releases 页]({GithubReleases}) 查看更新详情";
                }

                Log.Info($"最新版本 {latestVersion}");
                Log.Info($"版本 {Paths.Version} vs 远端 {latestVersion} -> {(latestVersion == Paths.Version ? "已是最新" : "有可用更新")}");

                return new UpdateCheckResult
                {
                    Success = true,
                    Version = latestVersion,
                    DownloadUrl = downloadUrl,
                    Changelog = changelog,
                };
            }
        }
        catch (Exception e)
        {
            return new UpdateCheckResult { Error = $"解析错误 {e.Message}" };
        }
    }

    // 下载更新包
    public static async Task<string?> DownloadUpdateAsync(string downloadUrl, Action<long, long>? progressCallback = null, int maxRetries = 3)
    {
        Paths.EnsureDataDirs();
        var tempDir = Path.Combine(Paths.DataTemp, "update");
        Directory.CreateDirectory(tempDir);
        var downloadPath = Path.Combine(tempDir, "update.zip");
        Log.Info($"更新包下载路径: {downloadPath}");

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    Log.Info($"第{attempt + 1}次重试下载更新");
                }
                Log.Info($"下载更新 {downloadUrl}");
                using var response = await AppUtils.Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalSize = response.Content.Headers.ContentLength ?? 0;
                if (totalSize <= 0)
                {
                    Log.Warning("更新包响应缺Content-Length 无下载进度");
                }
                long downloadedSize = 0;

                await using var fs = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                await using var stream = await response.Content.ReadAsStreamAsync();
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read));
                    downloadedSize += read;
                    if (progressCallback is not null && totalSize > 0)
                    {
                        progressCallback(downloadedSize, totalSize);
                    }
                }

                Log.Info($"已下载 {downloadedSize}/{totalSize}字节");
                Log.Info($"更新已下载 {downloadPath}");
                return downloadPath;
            }
            catch (TaskCanceledException)
            {
                Log.Warning("下载超时");
                if (attempt == maxRetries - 1)
                {
                    Log.Error("下载更新失败");
                    return null;
                }
            }
            catch (Exception e)
            {
                Log.Error($"下载更新失败 {e.Message}");
                return null;
            }
        }
        return null;
    }

    // 解压更新到新版本目录
    public static string? ExtractUpdate(string archivePath, string targetVersion)
    {
        var packageRoot = Paths.PackageRoot;
        var newVersionDir = Path.Combine(packageRoot, $"app-{targetVersion}");
        try
        {
            Log.Info($"解压更新 {archivePath} -> {newVersionDir}");

            if (Directory.Exists(newVersionDir))
            {
                Log.Debug($"移除已存在的新版本目录: {newVersionDir}");
                Directory.Delete(newVersionDir, recursive: true);
            }

            var tempDir = Path.Combine(Paths.DataTemp, "extract");
            if (Directory.Exists(tempDir))
            {
                Log.Debug($"移除残留解压临时目录: {tempDir}");
                Directory.Delete(tempDir, recursive: true);
            }
            Directory.CreateDirectory(tempDir);

            System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, tempDir, overwriteFiles: true);

            var entries = Directory.GetFileSystemEntries(tempDir);
            Log.Debug($"解压得到{entries.Length}个顶层条目");
            var extractedDir = entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : tempDir;

            MoveDirectory(extractedDir, newVersionDir);

            var record = Record.CreateRecord(targetVersion, newVersionDir, current: 0, partial: true);
            Record.SaveRecord(record, Path.Combine(newVersionDir, "record.json"));

            Log.Info($"更新已解压 {newVersionDir}");
            return newVersionDir;
        }
        catch (Exception e)
        {
            Log.Error($"解压更新失败 {e.Message}");
            return null;
        }
    }

    // 跨盘移动降级
    private static void MoveDirectory(string src, string dst)
    {
        try
        {
            Directory.Move(src, dst);
        }
        catch (IOException e)
        {

            Glimpseon.Core.Log.Warning($"[UPDATER] 跨卷移动失败 改复制: {e.Message}");
            // 跨卷 move 复制后删源
            CopyDirectory(src, dst);
            Directory.Delete(src, recursive: true);
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    // 部署更新 停用旧版本 激活新版本
    public static bool DeployUpdate(string newVersionDir)
    {
        var packageRoot = Paths.PackageRoot;
        try
        {
            Log.Info($"更新 {newVersionDir}");

            foreach (var appDir in Directory.GetDirectories(packageRoot, "app-*"))
            {
                if (Path.GetFullPath(appDir) != Path.GetFullPath(newVersionDir) &&
                    File.Exists(Path.Combine(appDir, "record.json")))
                {
                    Log.Debug($"停用旧版本目录: {appDir}");
                    Record.DeactivateVersion(appDir);
                }
            }

            if (Record.LoadRecord(Path.Combine(newVersionDir, "record.json")) is { } record)
            {
                Log.Debug($"新版本记录已加载 {record["version"]}");
                record["current"] = 1;
                record["partial"] = false;
                Record.SaveRecord(record, Path.Combine(newVersionDir, "record.json"));
            }
            else
            {
                Log.Warning($"新版本目录缺record.json {newVersionDir}");
            }

            Log.Info("已更新");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"更新失败 {e.Message}");
            return false;
        }
    }

    public static void CleanupUpdateFiles()
    {
        var tempDir = Path.Combine(Paths.DataTemp, "update");
        var extractDir = Path.Combine(Paths.DataTemp, "extract");
        Log.Debug($"清理更新临时文件: {tempDir} {extractDir}");
        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }
            Log.Info("已清理更新临时文件");
        }
        catch (Exception e)
        {
            Log.Warning($"清理临时文件失败 {e.Message}");
        }
    }

    // 生成更新提示脚本
    public static string CreateUpdateScript(string newVersionDir)
    {
        var packageRoot = Paths.PackageRoot;
        Log.Debug($"准备生成更新脚本 新版本目录: {Path.GetFileName(newVersionDir)}");

        var scriptContent = $""""
@echo off
chcp 65001 >nul
echo Glimpseon 更新
echo.

echo 新版本已准备好 {Path.GetFileName(newVersionDir)}
echo 下次启动时将自动使用新版本
echo.

echo 按任意键退出...
pause >nul
"""";
        var scriptPath = Path.Combine(packageRoot, "update_ready.bat");
        File.WriteAllText(scriptPath, scriptContent);
        Log.Info($"更新脚本已生成: {scriptPath}");
        return scriptPath;
    }
}
