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

// 日志
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

namespace Glimpseon.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public static class Log
{
    private const long MaxBytes = 1 * 1024 * 1024;
    private static readonly object LockObj = new();
    private static readonly string Timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

    private static bool _disabled;
    private static LogLevel _level = LogLevel.Info;
    private static int _maxCount = 50;
    private static int _maxDays = 30;
    private static string? _logFilePath;
    private static bool _hooksInstalled;

    public static void Configure(bool disableLog, LogLevel level, int maxCount, int maxDays)
    {
        lock (LockObj)
        {
            _disabled = disableLog;
            _level = level;
            _maxCount = Math.Max(10, maxCount);
            _maxDays = Math.Max(30, maxDays);
            if (!_disabled && _logFilePath is null)
            {
                Paths.EnsureDataDirs();
                _logFilePath = Path.Combine(Paths.DataLog, $"app_{Timestamp}.log");
                Info($"日志装载 级别={level} 路径={_logFilePath}");
            }
            Task.Run(CleanOldLogs);
        }
    }

    public static string? CurrentLogFilePath => _logFilePath;

    private static bool ShouldWrite(LogLevel level) => !_disabled && level >= _level;

    public static void Debug(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Write(LogLevel.Debug, message, member, file, line);

    public static void Info(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Write(LogLevel.Info, message, member, file, line);

    public static void Warning(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Write(LogLevel.Warning, message, member, file, line);

    public static void Error(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Write(LogLevel.Error, message, member, file, line);

    public static void Critical(string message, [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) => Write(LogLevel.Error, message, member, file, line, critical: true);

    private static void Write(LogLevel level, string message, string member, string file, int line, bool critical = false)
    {
        if (!ShouldWrite(level))
        {
            return;
        }
        var caller = $"Glimpseon.{member}";
        var module = Path.GetFileNameWithoutExtension(file);
        var preciseTime = AppUtils.PreciseNow().ToString("yyyy-MM-dd HH:mm:ss");
        var levelName = critical ? "CRITICAL" : level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARNING",
            _ => "ERROR",
        };
        var text = $"{preciseTime}|{levelName}|{caller}|{module}:{line}|{message}";

        lock (LockObj)
        {
            try
            {
                Console.WriteLine(text);
                if (_logFilePath is not null)
                {
                    RotateIfNeeded();
                    File.AppendAllText(_logFilePath, text + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    private static void RotateIfNeeded()
    {
        if (_logFilePath is null || !File.Exists(_logFilePath))
        {
            return;
        }
        var info = new FileInfo(_logFilePath);
        if (info.Length < MaxBytes)
        {
            return;
        }
        var backup = _logFilePath + ".1";
        if (File.Exists(backup))
        {
            File.Delete(backup);
        }
        File.Move(_logFilePath, backup);
    }

    private static void CleanOldLogs()
    {
        try
        {
            if (!Directory.Exists(Paths.DataLog))
            {
                return;
            }
            CompressOldLogs();
            var files = Directory.EnumerateFiles(Paths.DataLog)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return (name.StartsWith("app_") || name.StartsWith("crash_")) && (name.EndsWith(".log") || name.EndsWith(".zip"));
                })
                .Select(f => (Path: f, Mtime: File.GetLastWriteTime(f)))
                .OrderByDescending(x => x.Mtime)
                .ToList();

            if (files.Count > _maxCount)
            {
                foreach (var f in files.Skip(_maxCount))
                {
                    File.Delete(f.Path);
                }
                files = files.Take(_maxCount).ToList();
            }
            var cutoff = DateTime.Now - TimeSpan.FromDays(_maxDays);
            foreach (var f in files)
            {
                if (f.Mtime < cutoff)
                {
                    File.Delete(f.Path);
                }
            }
        }
        catch (Exception)
        {
            // 清理失败忽略
        }
    }

    // >24h 的 .log 压成 .zip 删原件
    private static void CompressOldLogs()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Paths.DataLog, "*.log"))
            {
                var mtime = File.GetLastWriteTime(file);
                if (DateTime.Now - mtime <= TimeSpan.FromHours(24))
                {
                    continue;
                }
                var zipPath = file + ".zip";
                try
                {
                    using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                    archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                    File.Delete(file);
                    Log.Debug($"旧日志已压缩 {Path.GetFileName(file)}");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"[logger] 压缩失败 {Path.GetFileName(file)}: {e.Message}");
                }
            }
        }
        catch (Exception)
        {
            // 压缩失败忽略
        }
    }

    public static void InitExceptionHooks()
    {
        lock (LockObj)
        {
            if (_hooksInstalled)
            {
                return;
            }
            _hooksInstalled = true;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Critical($"[主线程问题] {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            var inner = e.Exception?.InnerExceptions?.Count > 0 ? e.Exception.InnerExceptions[0] : e.Exception?.InnerException;
            Critical($"[Task问题] {inner?.GetType().Name}: {inner?.Message}\n{inner?.StackTrace}");
            e.SetObserved();
        };
    }
}
