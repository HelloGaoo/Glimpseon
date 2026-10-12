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

// 空间页

using System.Globalization;
using System.IO;

namespace Glimpseon.Core.Services;

/// <summary>单个磁盘用量快照</summary>
public sealed record DriveUsage(string Letter, string Label, string Format, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedPercent => TotalBytes > 0 ? UsedBytes * 100.0 / TotalBytes : 0;
    public double FreePercent => TotalBytes > 0 ? FreeBytes * 100.0 / TotalBytes : 0;
}

/// <summary>应用数据占用类别</summary>
public sealed record DataCategory(string Key, string NameKey, string[] Dirs, string ColorHex, bool Cleanable);

public static class DataUsageService
{
    /// <summary>类别表 配色固定 顺序即明细行顺序</summary>
    public static readonly DataCategory[] Categories =
    [
        new("log", "settings.storage.cat_log", [Paths.DataLog], "#4C9AFF", true),
        new("cache", "settings.storage.cat_cache", [Paths.DataCache], "#36B37E", true),
        new("temp", "settings.storage.cat_temp", [Paths.DataTemp], "#FF8B00", true),
        new("wallpaper", "settings.storage.cat_wallpaper", [Paths.DataWallpaper], "#6554C0", false),
        new("user", "settings.storage.cat_user",
            [Paths.DataUser, Paths.DataProfile, Paths.DataNotes, Paths.DataClassPhotos, Paths.DataIcon], "#00BCD4", false),
        new("config", "settings.storage.cat_config", [Paths.DataConfig], "#F04E98", false),
    ];

    /// <summary>扫描真实数据分区 与提醒口径一致</summary>
    public static List<DriveUsage> ScanDrives()
    {
        var list = new List<DriveUsage>();
        foreach (var drive in StorageMonitor.EnumDataDrives())
        {
            var letter = drive.Name.TrimEnd(':', '\\', '/');
            var label = "";
            try
            {
                label = drive.VolumeLabel ?? "";
            }
            catch (Exception e)
            {
                Log.Debug($"[空间] {drive.Name} 卷标读取失败: {e.Message}");
            }
            if (string.IsNullOrWhiteSpace(label))
            {
                label = letter;
            }
            list.Add(new DriveUsage(letter, label, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Letter, b.Letter));
        return list;
    }

    /// <summary>各类别目录占用字节 后台线程调用</summary>
    public static Dictionary<string, long> ScanSizes()
    {
        var result = new Dictionary<string, long>();
        foreach (var cat in Categories)
        {
            long sum = 0;
            foreach (var dir in cat.Dirs)
            {
                sum += GetDirectorySize(dir);
            }
            result[cat.Key] = sum;
        }
        return result;
    }

    /// <summary>清理指定类别 返回删除文件数 仅可清理类别 后台线程调用</summary>
    public static int Clean(string key)
    {
        var cat = Array.Find(Categories, c => c.Key == key);
        if (cat is null || !cat.Cleanable)
        {
            return 0;
        }
        var deleted = 0;
        foreach (var dir in cat.Dirs)
        {
            deleted += CleanDirectory(dir, key == "log");
        }
        Log.Info($"[空间] 清理 {key} 删除 {deleted} 个文件");
        return deleted;
    }

    /// <summary>清理全部可清理类别 返回删除文件数</summary>
    public static int CleanAll()
    {
        var deleted = 0;
        foreach (var cat in Categories)
        {
            if (cat.Cleanable)
            {
                deleted += Clean(cat.Key);
            }
        }
        return deleted;
    }

    /// <summary>应用数据所在盘（DataRoot 所在卷）</summary>
    public static DriveUsage? GetAppDrive()
    {
        var root = Path.GetPathRoot(Paths.DataRoot);
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }
        foreach (var d in ScanDrives())
        {
            if (string.Equals(d.Letter, root.TrimEnd(':', '\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                return d;
            }
        }
        return null;
    }

    private static long GetDirectorySize(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
            {
                return 0;
            }
            long sum = 0;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    sum += new FileInfo(file).Length;
                }
                catch (Exception e)
                {
                    Log.Debug($"[空间] 文件跳过 {file}: {e.Message}");
                }
            }
            return sum;
        }
        catch (Exception e)
        {
            Log.Debug($"[空间] 目录统计失败 {dir}: {e.Message}");
            return 0;
        }
    }

    private static int CleanDirectory(string dir, bool keepLatestLog)
    {
        var deleted = 0;
        try
        {
            if (!Directory.Exists(dir))
            {
                return 0;
            }
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            // 日志类别保留最新一个 .log 其余删除
            if (keepLatestLog)
            {
                var current = files.Where(f => f.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                files = files.Where(f => f != current).ToArray();
            }
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (IOException e)
                {
                    Log.Debug($"[空间] 删除失败 {file}: {e.Message}");
                }
            }
            // 清完的空子目录一并移除（保留根目录本身）
            foreach (var sub in Directory.GetDirectories(dir))
            {
                try
                {
                    if (Directory.GetFileSystemEntries(sub, "*", SearchOption.AllDirectories).Length == 0)
                    {
                        Directory.Delete(sub, true);
                    }
                }
                catch (IOException e)
                {
                    Log.Debug($"[空间] 空目录移除失败 {sub}: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[空间] 清理目录失败 {dir}: {e.Message}");
        }
        return deleted;
    }

    /// <summary>字节数格式化 TB/GB/MB/KB 自适应两位小数</summary>
    public static string FormatBytes(long bytes)
    {
        const long kb = 1024, mb = kb * 1024, gb = mb * 1024, tb = gb * 1024;
        return bytes switch
        {
            >= tb => (bytes / (double)tb).ToString("F2", CultureInfo.InvariantCulture) + " TB",
            >= gb => (bytes / (double)gb).ToString("F2", CultureInfo.InvariantCulture) + " GB",
            >= mb => (bytes / (double)mb).ToString("F2", CultureInfo.InvariantCulture) + " MB",
            >= kb => (bytes / (double)kb).ToString("F2", CultureInfo.InvariantCulture) + " KB",
            _ => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        };
    }
}
