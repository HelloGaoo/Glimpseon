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

// 文件下载

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Glimpseon.Core.Services;

public sealed record DownloadTask(string SoftwareName, string Filename, string? Url, string? GithubPath, IReadOnlyDictionary<string, int>? PhaseAllocation = null);

public sealed record DownloadProgressInfo(string SoftwareName, int Percent, double SpeedBytesPerSec, long DownloadedBytes, long TotalBytes);

public static class DownloadSources
{
    public const string SourceGithub = "original";
    public const string SourceHk = "hk";
    public const string SourceCloudflare = "cloudflare";
    public const string SourceEdgeone = "edgeone";
    public const string SourceGeekertao = "geekertao";

    public static readonly IReadOnlyDictionary<string, (string NameKey, string Prefix)> Sources =
        new Dictionary<string, (string, string)>
        {
            ["original"] = ("download.source_github", "https://github.com"),
            ["hk"] = ("download.source_hk", "https://hk.gh-proxy.org/https://github.com"),
            ["cloudflare"] = ("download.source_cf", "https://gh-proxy.org/https://github.com"),
            ["edgeone"] = ("download.source_edgeone", "https://edgeone.gh-proxy.org/https://github.com"),
            ["geekertao"] = ("download.source_geekertao", "https://ghfile.geekertao.top/https://github.com"),
        };

    public const string DefaultSource = SourceHk;

    private static string _currentSource = DefaultSource;
    private static readonly object SourceLock = new();

    public static string CurrentSource
    {
        get { lock (SourceLock) { return _currentSource; } }
    }

    public static void SetDownloadSrc(string sourceKey)
    {
        lock (SourceLock)
        {
            if (Sources.ContainsKey(sourceKey))
            {
                Log.Info($"设置下载源: {sourceKey}");
                _currentSource = sourceKey;
            }
            else
            {
                Log.Warning($"无效下载源: {sourceKey} 用默认源: {DefaultSource}");
                _currentSource = DefaultSource;
            }
        }
    }
}

public class Downloader
{
    private const string SevenZipPassword = "zQt83iOY3xXLfDVg6SJ7ocnapy90I1d62w6jh79WlT0m1qPC8b55HU5Nk4ARZFBs";

    private static readonly string TempDir = Paths.DataTemp;
    private static readonly string CacheDir = Paths.DataCache;

    private static readonly Dictionary<string, int> DefaultPhaseAllocation = new()
    {
        ["download"] = 70,
        ["decompress"] = 20,
        ["install"] = 10,
    };

    private readonly Action<string, double>? _progressCallback;
    private readonly ConcurrentDictionary<string, double> _lastProgress = new();
    private readonly ConcurrentDictionary<string, int> _lastProgressTier = new();

    public Downloader(Action<string, double>? progressCallback = null)
    {
        _progressCallback = progressCallback;
        Log.Debug($"Downloader 初始化 progressCallback={(progressCallback is not null ? "已注入" : "未注入")}");
    }

    // 进度管理

    public void SetProgress(string softwareName, double percent)
    {
        var last = _lastProgress.TryGetValue(softwareName, out var l) ? l : -1;
        if (percent == 0 || percent >= last)
        {
            _lastProgress[softwareName] = percent;
            var tier = (int)(percent / 10);
            if (tier != (_lastProgressTier.TryGetValue(softwareName, out var t) ? t : -1) || percent is 0 or 100)
            {
                _lastProgressTier[softwareName] = tier;
                Log.Debug($"{softwareName}: 总进度 {tier * 10}% 当前 {percent}%");
            }
            try
            {
                _progressCallback?.Invoke(softwareName, percent);
            }
            catch (Exception e)
            {
                Log.Warning($"进度回调异常: {e.Message}");
            }
        }
    }

    private static Dictionary<string, int> CalcPhaseOffsets(IReadOnlyDictionary<string, int> allocation)
    {
        var offsets = new Dictionary<string, int>();
        var cur = 0;
        foreach (var p in new[] { "download", "decompress", "install" })
        {
            offsets[p] = cur;
            cur += allocation.TryGetValue(p, out var v) ? v : 0;
        }
        return offsets;
    }

    public double UpdateProgress(string softwareName, string phase, double phasePercent, IReadOnlyDictionary<string, int>? allocation = null)
    {
        allocation ??= DefaultPhaseAllocation;
        var offsets = CalcPhaseOffsets(allocation);
        var phaseAlloc = allocation.TryGetValue(phase, out var a) ? a : 0;
        var start = offsets.TryGetValue(phase, out var s) ? s : 0;
        var total = Math.Round(start + phasePercent / 100.0 * phaseAlloc, 1);
        SetProgress(softwareName, total);
        return total;
    }

    public void ResetProgress(string softwareName)
    {
        _lastProgress.TryRemove(softwareName, out _);
        _lastProgressTier.TryRemove(softwareName, out _);
        Log.Debug($"{softwareName}: 进度状态已重置");
    }

    // 进程工具

    // 等待进程出现
    private static async Task<bool> WaitProcessAsync(string softwareName, string processName, int timeout = 30, int checkInterval = 1)
    {
        Log.Info($"{softwareName}: 等待进程 {processName} 超时{timeout}秒");
        var startTime = DateTime.Now;
        var bareName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;
        while ((DateTime.Now - startTime).TotalSeconds < timeout)
        {
            try
            {
                if (Process.GetProcessesByName(bareName).Length > 0)
                {
                    Log.Info($"{softwareName}: 进程 {processName} 存在");
                    return true;
                }
            }
            catch (Exception err)
            {
                Log.Warning($"{softwareName}: 检查进程 {processName} 出错 {err.Message}");
            }
            await Task.Delay(checkInterval * 1000);
        }
        Log.Warning($"{softwareName}: 等待进程 {processName} 超时{timeout}秒");
        return false;
    }

    // 等待进程退出
    private static async Task<bool> WaitProcessExitAsync(string softwareName, Process process, int? timeout = null, int checkInterval = 2)
    {
        if (timeout.HasValue)
        {
            Log.Info($"{softwareName}: 等待进程超时{timeout}s");
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalSeconds < timeout.Value)
            {
                if (process.HasExited)
                {
                    Log.Info($"{softwareName}: 进程已退出");
                    return true;
                }
                await Task.Delay(checkInterval * 1000);
            }
            Log.Warning($"{softwareName}: 等待进程退出超时 {timeout}s");
            return false;
        }
        await process.WaitForExitAsync();
        Log.Info($"{softwareName}: 进程已退出");
        return true;
    }

    // 终止进程
    private static void KillProcess(string softwareName, string processName)
    {
        Log.Info($"{softwareName}: 终止进程 {processName}");
        try
        {
            var bareName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName[..^4] : processName;
            var killed = 0;
            foreach (var proc in Process.GetProcessesByName(bareName))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    killed++;
                }
                catch (Exception e)
                {
                    Log.Warning($"{softwareName}: 终止 {processName} 单个实例失败 {e.Message}");
                }
                finally
                {
                    proc.Dispose();
                }
            }
            if (killed > 0)
            {
                Log.Info($"{softwareName}: 进程 {processName} 已终止");
            }
            else
            {
                Log.Warning($"{softwareName}: 终止进程 {processName} 失败: 未找到进程");
            }
        }
        catch (Exception err)
        {
            Log.Error($"{softwareName}: 终止进程出错 {err.Message}");
        }
    }

    // 低优先级启动
    private static Process PopLowPriority(string fileName, string arguments = "")
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var process = Process.Start(psi)!;
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception e)
        {

            Glimpseon.Core.Log.Warning($"[下载] 进程优先级设置失败: {e.Message}");
            // 优先级设置失败忽略
        }
        Log.Debug($"低优先级子进程 pid={process.Id}");
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        return process;
    }

    // URL 与下载

    private static string? GetDownloadUrl(DownloadTask task)
    {
        if (!string.IsNullOrEmpty(task.Url))
        {
            Log.Debug($"URL解析: 命中直链 url={task.Url}");
            return task.Url;
        }
        if (!string.IsNullOrEmpty(task.GithubPath))
        {
            var src = DownloadSources.CurrentSource;
            var prefix = DownloadSources.Sources[src].Prefix;
            Log.Debug($"URL解析: github_path={task.GithubPath} 镜像源={src} 前缀={prefix}");
            return $"{prefix}{task.GithubPath}";
        }
        Log.Warning($"URL解析失败: 无 url/github_path 字段 filename={task.Filename}");
        return null;
    }

    // 下载文件
    public async Task<string> DownloadFileAsync(DownloadTask task, string downloadLocation = "Temporary",
        Action<string, double>? progressCallback = null, Action<string>? downloadCompleteCallback = null,
        double downloadRateLimit = 0, double progressUpdateInterval = 0.5)
    {
        var softwareName = task.SoftwareName;
        var url = GetDownloadUrl(task)
            ?? throw new InvalidOperationException(AppUtils.Tr("download.error_no_url"));

        var savePath = downloadLocation == "Temporary"
            ? Path.Combine(TempDir, task.Filename)
            : Path.Combine(CacheDir, task.Filename);
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        Log.Debug($"{softwareName}: 保存路径: {savePath} (位置模式: {downloadLocation})");
        Log.Info($"{softwareName}: 下载 {url}");

        var allocation = task.PhaseAllocation ?? DefaultPhaseAllocation;
        var extCbWarned = false;

        void InternalProgress(double p)
        {
            double total;
            try
            {
                total = UpdateProgress(softwareName, "download", p, allocation);
            }
            catch (Exception e)
            {

                Glimpseon.Core.Log.Debug($"[下载] 进度读取失败: {e.Message}");
                total = p;
            }
            if (progressCallback is not null)
            {
                try
                {
                    progressCallback(softwareName, total);
                }
                catch (Exception e)
                {
                    if (!extCbWarned)
                    {
                        extCbWarned = true;
                        Log.Warning($"{softwareName}: 外部下载进度回调异常 - {e.Message}");
                    }
                }
            }
        }

        const int maxRetries = 3;
        for (var retryCount = 0; retryCount < maxRetries; retryCount++)
        {
            try
            {
                Log.Info($"{softwareName}: 请求下载 (重试 {retryCount + 1}/{maxRetries})");

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
                request.Headers.TryAddWithoutValidation("Referer", "https://www.seewo.com/");
                request.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");

                // 跳过证书校验
                using var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                };
                using var session = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
                var response = await session.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                // 手动跟随重定向
                if ((int)response.StatusCode is 301 or 302)
                {
                    if (response.Headers.Location is { } redirectUrl)
                    {
                        Log.Info($"{softwareName}: 跟随重定向到: {redirectUrl}");
                        response.Dispose();
                        response = await session.GetAsync(redirectUrl, HttpCompletionOption.ResponseHeadersRead);
                    }
                    else
                    {
                        Log.Warning($"{softwareName}: 重定向缺Location头 状态码={(int)response.StatusCode}");
                    }
                }

                using (response)
                {
                    response.EnsureSuccessStatusCode();

                var totalSize = response.Content.Headers.ContentLength ?? 0;
                if (totalSize <= 0)
                {
                    Log.Warning($"{softwareName}: 响应缺Content-Length 不校验大小");
                }
                Log.Info($"{softwareName}: 文件大小: {totalSize} bytes");

                long downloadedSize = 0;
                var startTime = DateTime.Now;
                var windowStart = startTime;
                var windowDownloaded = 0L;
                var lastUpdateTime = 0.0;

                await using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                await using var stream = await response.Content.ReadAsStreamAsync();
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read));
                    downloadedSize += read;

                    // 限速
                    if (downloadRateLimit > 0)
                    {
                        windowDownloaded += read;
                        var nowT = (DateTime.Now - windowStart).TotalSeconds;
                        if (nowT >= 1.0)
                        {
                            windowStart = DateTime.Now;
                            windowDownloaded = 0;
                        }
                        else
                        {
                            var expectedTime = windowDownloaded / downloadRateLimit;
                            if (expectedTime > nowT)
                            {
                                await Task.Delay((int)((expectedTime - nowT) * 1000));
                            }
                        }
                    }

                    var now = (DateTime.Now - startTime).TotalSeconds;
                    if (lastUpdateTime == 0 || now - lastUpdateTime >= progressUpdateInterval)
                    {
                        var elapsed = Math.Max(now, 1e-6);
                        var speed = downloadedSize / elapsed;
                        Log.Debug($"{softwareName}: 下载速度 {FormatSpeed(speed)}");
                        if (totalSize > 0)
                        {
                            InternalProgress(Math.Floor(downloadedSize * 100.0 / totalSize));
                        }
                        lastUpdateTime = now;
                    }
                }

                InternalProgress(100);
                Log.Info($"{softwareName}: 已下载 {savePath}");
                    downloadCompleteCallback?.Invoke(softwareName);
                    return savePath;
                }
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
            {
                if (e is TaskCanceledException)
                {
                    Log.Warning($"{softwareName}: 下载超时");
                }
                Log.Warning($"{softwareName}: 下载失败 将重试 ({retryCount + 1}/{maxRetries}) - {e.Message}");
                await Task.Delay(5000);
                if (retryCount >= maxRetries - 1)
                {
                    Log.Error($"{softwareName}: 下载失败 - {e.Message}");
                    throw new InvalidOperationException(e.Message, e);
                }
            }
        }
        throw new InvalidOperationException("unreachable");
    }

    private static string FormatSpeed(double speed) => speed switch
    {
        < 1024 => $"{speed:F2} B/s",
        < 1024 * 1024 => $"{speed / 1024:F2} KB/s",
        _ => $"{speed / (1024 * 1024):F2} MB/s",
    };

    // 静默安装
    public async Task SilentInstallationAsync(string softwareName, string installerPath)
    {
        Log.Info($"{softwareName}: 静默安装");
        Log.Debug($"{softwareName}: 安装包路径: {installerPath}");
        try
        {
            if (installerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Log.Info($"{softwareName}: 安装方式 EXE 静默安装 /S 低优先级启动 标准输出丢弃");
                var process = PopLowPriority(installerPath, "/S");
                await WaitProcessExitAsync(softwareName, process, timeout: null);
                if (process.ExitCode == 0)
                {
                    Log.Info($"{softwareName}: 已静默安装");
                }
                else
                {
                    Log.Error($"{softwareName}: 静默安装失败 返回码: {process.ExitCode}");
                    throw new InvalidOperationException($"安装失败，返回码: {process.ExitCode}");
                }
            }
            else
            {
                Log.Warning($"{softwareName}: 不支持的安装程序类型");
                throw new InvalidOperationException(AppUtils.Tr("download.unsupported_type"));
            }
        }
        catch (Exception err)
        {
            Log.Error($"{softwareName}: 安装失败 - {err.Message}");
            throw;
        }
    }

    // 解压 7z
    public void Decompress7Z(string softwareName, string archivePath, string outputDir)
    {
        Log.Info($"{softwareName}: 解压到 {outputDir}");
        try
        {
            Directory.CreateDirectory(outputDir);
            using var archive = SharpCompress.Archives.SevenZip.SevenZipArchive.Open(archivePath,
                new SharpCompress.Readers.ReaderOptions { Password = SevenZipPassword });
            archive.ExtractToDirectory(outputDir);
            Log.Info($"{softwareName}: 已解压");
        }
        catch (Exception err)
        {
            Log.Error($"{softwareName}: 解压失败 - {err.Message}");
            throw;
        }
    }

    // 解压 zip
    public static void ExtractZip(string archivePath, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, outputDir, overwriteFiles: true);
    }

    // 创建快捷方式
    public static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell 不可用");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.IconLocation = targetPath;
        shortcut.Save();
    }

    // 清理单个临时文件
    public static async Task CleanTempdirAsync(string filename, string softwareName, int maxRetries = 3, double retryDelay = 1.0)
    {
        var filePath = Path.Combine(TempDir, filename);
        if (!File.Exists(filePath))
        {
            Log.Debug($"{softwareName}: 无需清理 {filePath}");
            return;
        }
        Log.Debug($"{softwareName}: 清理安装包 {filePath} 最多重试{maxRetries}次");
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                File.Delete(filePath);
                Log.Info($"{softwareName}: 已清理 {filePath}");
                return;
            }
            catch (IOException err) when (attempt < maxRetries - 1)
            {
                Log.Warning($"{softwareName}: 文件被占用 {retryDelay}秒后重试 ({attempt + 1}/{maxRetries})");
                await Task.Delay((int)(retryDelay * 1000));
            }
            catch (Exception err)
            {
                Log.Warning($"{softwareName}: 清理 {filePath} 失败:{err.Message}");
                break;
            }
        }
    }

    // temp 全部删除
    public static void CleanupTempDirectory(string? tempDir = null)
    {
        tempDir ??= TempDir;
        if (!Directory.Exists(tempDir))
        {
            Log.Debug($"跳过清理 {tempDir}");
            return;
        }
        try
        {
            Log.Info($"清理临时目录 {tempDir}");
            var count = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(tempDir))
            {
                try
                {
                    if (File.Exists(entry))
                    {
                        File.Delete(entry);
                        count++;
                    }
                    else if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, recursive: true);
                        count++;
                    }
                }
                catch (Exception err)
                {
                    Log.Warning($"清理临时文件失败 {entry} - {err.Message}");
                }
            }
            Log.Info($"临时目录已清理 {count}项");
        }
        catch (Exception err)
        {
            Log.Error($"清理临时目录失败 {err.Message}");
        }
    }

    // 安装分发

    public Task InstallSoftwareAsync(DownloadTask task,
        Action<string, double>? progressCallback = null, Action<string>? downloadCompleteCallback = null)
    {
        var method = GetType().GetMethod($"Install_{task.SoftwareName}",
            BindingFlags.Public | BindingFlags.Instance);
        if (method is null)
        {
            Log.Error($"{task.SoftwareName}: 无安装方法 Install_{task.SoftwareName}");
            throw new InvalidOperationException(AppUtils.Tr("download.error_no_install_method"));
        }
        return (Task)method.Invoke(this, [task, progressCallback, downloadCompleteCallback])!;
    }

    private async Task RunStandardInstallAsync(DownloadTask task,
        Action<string, double>? progressCallback, Action<string>? downloadCompleteCallback)
    {
        // 标准流程体 下载 > 安装 > 清理
        try
        {
            var softwareName = task.SoftwareName;
            Log.Info($"{softwareName}: 安装");
            var installerPath = await DownloadFileAsync(task, "Temporary", progressCallback, downloadCompleteCallback);
            await SilentInstallationAsync(softwareName, installerPath);
            await CleanTempdirAsync(task.Filename, softwareName);
            UpdateProgress(softwareName, "install", 100);
        }
        catch (Exception err)
        {
            Log.Error($"{task.SoftwareName}: 安装失败 - {err.Message}");
            SetProgress(task.SoftwareName, 0);
            throw;
        }
    }

    // 安装方法

    public Task Install_剪辑师(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_知识胶囊(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_掌上看班(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public async Task Install_激活工具(DownloadTask task, Action<string, double>? progressCallback = null, Action<string>? downloadCompleteCallback = null)
    {
        try
        {
            var softwareName = task.SoftwareName;
            Log.Info($"{softwareName}: 下载");
            var installerPath = await DownloadFileAsync(task, "Temporary", progressCallback, downloadCompleteCallback);

            var outputDir = @"C:\Program Files (x86)\Seewo";
            Decompress7Z(softwareName, installerPath, outputDir);

            var sourceShortcut = Path.Combine(outputDir, "激活工具-WHYOS-Gaoo", "激活工具.lnk");
            var destShortcut = Path.Combine(@"C:\Users\Public\Desktop", "激活工具.lnk");

            if (File.Exists(sourceShortcut))
            {
                Log.Info($"{softwareName}: 复制快捷方式到桌面");
                File.Copy(sourceShortcut, destShortcut, overwrite: true);
                Log.Info($"{softwareName}: 快捷方式已复制到桌面");
            }
            else
            {
                Log.Warning($"{softwareName}: 未找到快捷方式: {sourceShortcut}");
            }

            await CleanTempdirAsync(task.Filename, softwareName);
            UpdateProgress(softwareName, "install", 100);
        }
        catch (Exception err)
        {
            Log.Error($"{task.SoftwareName}: 安装失败 - {err.Message}");
            SetProgress(task.SoftwareName, 0);
            throw;
        }
    }

    public async Task Install_希沃壁纸(DownloadTask task, Action<string, double>? progressCallback = null, Action<string>? downloadCompleteCallback = null)
    {
        try
        {
            var softwareName = task.SoftwareName;
            Log.Info($"{softwareName}: 下载");
            var installerPath = await DownloadFileAsync(task, "Temporary", progressCallback, downloadCompleteCallback);
            var allocation = task.PhaseAllocation ?? DefaultPhaseAllocation;

            var outputDir = @"C:\Windows\Web";
            Decompress7Z(softwareName, installerPath, outputDir);
            UpdateProgress(softwareName, "decompress", 100, allocation);

            var wallpaperPath = Path.Combine(outputDir, "img0.jpg");
            if (File.Exists(wallpaperPath))
            {
                Log.Info($"{softwareName}: 更改桌面背景");
                Win32.Native.SetDesktopWallpaper(wallpaperPath);
                Log.Info($"{softwareName}: 桌面背景已更改");
                try
                {
                    UpdateProgress(softwareName, "install", 100, allocation);
                }
                catch (Exception e)
                {
                    Log.Warning($"{softwareName}: 更新壁纸安装阶段进度失败 - {e.Message}");
                }
            }
            else
            {
                Log.Warning($"{softwareName}: 未找到壁纸文件: {wallpaperPath}");
            }

            UpdateProgress(softwareName, "install", 100);
            Log.Info($"{softwareName}: 已安装");

            await CleanTempdirAsync(task.Filename, softwareName);
        }
        catch (Exception err)
        {
            Log.Error($"{task.SoftwareName}: 安装失败 - {err.Message}");
            SetProgress(task.SoftwareName, 0);
            throw;
        }
    }

    public Task Install_希沃管家(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃快传(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃集控(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃智能笔(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃易课堂(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃输入法(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_PPT小工具(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃轻白板(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃白板5(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃课堂助手(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃电脑助手(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃导播助手(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃视频展台(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_希沃物联校园(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_远程互动课堂(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public async Task Install_省平台登录插件(DownloadTask task, Action<string, double>? progressCallback = null, Action<string>? downloadCompleteCallback = null)
    {
        try
        {
            var softwareName = task.SoftwareName;
            Log.Info($"{softwareName}: 下载");
            var installerPath = await DownloadFileAsync(task, "Temporary", progressCallback, downloadCompleteCallback);

            Log.Info($"{softwareName}: 安装");
            var process = PopLowPriority(installerPath);

            await WaitProcessAsync(softwareName, "省平台登录插件.exe", timeout: 15, checkInterval: 2);
            KillProcess(softwareName, "省平台登录插件.exe");

            await CleanTempdirAsync(task.Filename, softwareName);

            SetProgress(softwareName, 100);
            Log.Info($"{softwareName}: 已安装");
        }
        catch (Exception err)
        {
            Log.Error($"{task.SoftwareName}: 安装失败 - {err.Message}");
            SetProgress(task.SoftwareName, 0);
            throw;
        }
    }

    public Task Install_希象传屏发送端(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public async Task Install_希沃品课小组端(DownloadTask task, Action<string, double>? progressCallback = null, Action<string>? downloadCompleteCallback = null)
    {
        try
        {
            var softwareName = task.SoftwareName;
            Log.Info($"{softwareName}: 下载");
            var installDir = @"C:\Program Files (x86)\Seewo\SeewoPinK";
            Log.Info($"{softwareName}: 建目录 {installDir}");
            Directory.CreateDirectory(installDir);

            var installerPath = await DownloadFileAsync(task, "Temporary", progressCallback, downloadCompleteCallback);

            Log.Info($"{softwareName}: 静默安装");
            var process = PopLowPriority(installerPath, "/S");

            await WaitProcessAsync(softwareName, "seewoPincoGroup.exe", timeout: 20, checkInterval: 3);

            await WaitProcessExitAsync(softwareName, process, timeout: 45, checkInterval: 5);

            KillProcess(softwareName, "seewoPincoGroup.exe");

            await CleanTempdirAsync(task.Filename, softwareName);

            SetProgress(softwareName, 100);
            Log.Info($"{softwareName}: 已安装");
        }
        catch (Exception err)
        {
            Log.Error($"{task.SoftwareName}: 安装失败 - {err.Message}");
            SetProgress(task.SoftwareName, 0);
            throw;
        }
    }

    public async Task Install_希沃品课教师端(DownloadTask task, Action<string, double>? progressCallback = null, Action<string>? downloadCompleteCallback = null)
    {
        try
        {
            var softwareName = task.SoftwareName;
            Log.Info($"{softwareName}: 下载");
            var installerPath = await DownloadFileAsync(task, "Temporary", progressCallback, downloadCompleteCallback);

            Log.Info($"{softwareName}: 静默安装");
            var process = PopLowPriority(installerPath, "/S");

            await WaitProcessAsync(softwareName, "seewoPincoTeacher.exe", timeout: 20, checkInterval: 3);

            await WaitProcessExitAsync(softwareName, process, timeout: 45, checkInterval: 5);

            KillProcess(softwareName, "seewoPincoTeacher.exe");

            await CleanTempdirAsync(task.Filename, softwareName);

            SetProgress(softwareName, 100);
            Log.Info($"{softwareName}: 已安装");
        }
        catch (Exception err)
        {
            Log.Error($"{task.SoftwareName}: 安装失败 - {err.Message}");
            SetProgress(task.SoftwareName, 0);
            throw;
        }
    }

    public Task Install_微信(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_QQ(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_UU远程(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public Task Install_网易云音乐(DownloadTask task, Action<string, double>? p = null, Action<string>? d = null) => RunStandardInstallAsync(task, p, d);

    public async Task Install_office2021(DownloadTask task, Action<string, double>? progressCallback = null, Action<string>? downloadCompleteCallback = null)
    {
        try
        {
            var softwareName = task.SoftwareName;
            Log.Info($"{softwareName}: 下载");
            var installerPath = await DownloadFileAsync(task, "Temporary", progressCallback, downloadCompleteCallback);
            Log.Info($"{softwareName}: 解压");

            var allocation = task.PhaseAllocation ?? DefaultPhaseAllocation;
            var outputDir = TempDir;
            Decompress7Z(softwareName, installerPath, outputDir);
            try
            {
                UpdateProgress(softwareName, "decompress", 100, allocation);
            }
            catch (Exception e)
            {
                Log.Warning($"{softwareName}: 更新解压阶段进度失败 - {e.Message}");
            }
            Log.Info($"{softwareName}: 安装");

            var setupExe = Path.Combine(outputDir, "setup.exe");
            var configXml = Path.Combine(outputDir, "config.xml");

            Log.Info($"{softwareName}: 运行 setup.exe /configure config.xml");
            var officeProcess = PopLowPriority(setupExe, $"/configure \"{configXml}\"");

            Log.Info($"{softwareName}: 等待安装进程结束");
            await officeProcess.WaitForExitAsync();
            if (officeProcess.ExitCode != 0)
            {
                Log.Warning($"{softwareName}: Office 安装进程返回码非零: {officeProcess.ExitCode}");
            }
            Log.Info($"{softwareName}: 安装进程已结束");

            SetProgress(softwareName, 100);
            Log.Info($"{softwareName}: 已安装");

            await CleanTempdirAsync(task.Filename, softwareName);
        }
        catch (Exception err)
        {
            Log.Error($"{task.SoftwareName}: 安装失败 - {err.Message}");
            SetProgress(task.SoftwareName, 0);
            throw;
        }
    }

    public async Task Install_ClassIsland2(DownloadTask task, Action<string, double>? progressCallback = null, Action<string>? downloadCompleteCallback = null)
    {
        try
        {
            var softwareName = task.SoftwareName;
            Log.Info($"{softwareName}: 下载");
            var installerPath = await DownloadFileAsync(task, "Temporary", progressCallback, downloadCompleteCallback);

            Log.Info($"{softwareName}: 安装");

            var installDir = @"C:\ClassIsland2";
            Log.Info($"{softwareName}: 建目录 {installDir}");
            Directory.CreateDirectory(installDir);

            Log.Info($"{softwareName}: 解压文件到: {installDir}");
            ExtractZip(installerPath, installDir);

            var shortcutName = "ClassIsland2";
            var targetPath = Path.Combine(installDir, "ClassIsland.exe");
            var publicDesktop = Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? @"C://Users//Public", "Desktop");
            var shortcutPath = Path.Combine(publicDesktop, $"{shortcutName}.lnk");

            Log.Info($"{softwareName}: 创建快捷方式到公用桌面: {shortcutPath}");
            try
            {
                CreateShortcut(shortcutPath, targetPath, installDir);
                Log.Info($"{softwareName}: 快捷方式已创建");
            }
            catch (Exception e)
            {
                Log.Warning($"{softwareName}: 创建快捷方式失败 - {e.Message}");
            }

            await CleanTempdirAsync(task.Filename, softwareName);

            SetProgress(softwareName, 100);
            Log.Info($"{softwareName}: 已安装");
        }
        catch (Exception err)
        {
            Log.Error($"{task.SoftwareName}: 安装失败 - {err.Message}");
            SetProgress(task.SoftwareName, 0);
            throw;
        }
    }

    public async Task Install_ClassWidgets(DownloadTask task, Action<string, double>? progressCallback = null, Action<string>? downloadCompleteCallback = null)
    {
        try
        {
            var softwareName = task.SoftwareName;
            Log.Info($"{softwareName}: 下载");
            var installerPath = await DownloadFileAsync(task, "Temporary", progressCallback, downloadCompleteCallback);

            Log.Info($"{softwareName}: 安装");

            var installDir = @"C:\ClassWidgets";
            Log.Info($"{softwareName}: 建目录 {installDir}");
            Directory.CreateDirectory(installDir);

            Log.Info($"{softwareName}: 解压文件到: {installDir}");
            ExtractZip(installerPath, installDir);

            var shortcutName = "ClassWidgets";
            var targetPath = @"C:\ClassWidgets\ClassWidgets.exe";

            var publicDesktop = Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? @"C://Users//Public", "Desktop");
            var shortcutPath = Path.Combine(publicDesktop, $"{shortcutName}.lnk");

            Log.Info($"{softwareName}: 创建快捷方式到公用桌面: {shortcutPath}");
            try
            {
                CreateShortcut(shortcutPath, targetPath, @"C:\ClassWidgets");
                Log.Info($"{softwareName}: 快捷方式已创建");
            }
            catch (Exception e)
            {
                Log.Warning($"{softwareName}: 创建快捷方式失败 - {e.Message}");
            }

            await CleanTempdirAsync(task.Filename, softwareName);

            SetProgress(softwareName, 100);
            Log.Info($"{softwareName}: 已安装");
        }
        catch (Exception err)
        {
            Log.Error($"{task.SoftwareName}: 安装失败 - {err.Message}");
            SetProgress(task.SoftwareName, 0);
            throw;
        }
    }
}
