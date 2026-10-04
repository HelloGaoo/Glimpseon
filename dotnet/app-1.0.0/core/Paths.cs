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

// 路径处理
using System.Text.Json;

namespace Glimpseon.Core;

public static class Paths
{
    public static string PackageRoot { get; }
    public static string AppDir { get; }
    public static string DataRoot { get; }
    public static string DataConfig { get; }
    public static string DataLog { get; }
    public static string DataCache { get; }
    public static string DataTemp { get; }
    public static string DataProfile { get; }
    public static string DataUser { get; }
    public static string DataIcon { get; }
    public static string DataWallpaper { get; }
    public static string DataClassPhotos { get; }
    public static string DataNotes { get; }
    public static string WallpaperDir => DataWallpaper;
    public static string Version { get; }
    public static string BuildDate { get; }

    static Paths()
    {
        PackageRoot = DetectPackageRoot();
        AppDir = DetectAppDir(PackageRoot);

        DataRoot = Path.Combine(PackageRoot, "data");
        DataConfig = Path.Combine(DataRoot, "config");
        DataLog = Path.Combine(DataRoot, "log");
        DataCache = Path.Combine(DataRoot, "cache");
        DataTemp = Path.Combine(DataRoot, "temp");
        DataProfile = Path.Combine(DataRoot, "profile");
        DataUser = Path.Combine(DataRoot, "user");
        DataIcon = Path.Combine(DataRoot, "icon");
        DataWallpaper = Path.Combine(DataRoot, "wallpaper");
        DataClassPhotos = Path.Combine(DataRoot, "classphotos");
        DataNotes = Path.Combine(DataRoot, "notes");

        (Version, BuildDate) = ReadVersion();
    }

    private static string DetectPackageRoot()
    {
        var envRoot = Environment.GetEnvironmentVariable("Glimpseon_PackageRoot");
        if (!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot))
        {
            return Path.GetFullPath(envRoot);
        }

        var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirName = Path.GetFileName(exeDir);
        if (dirName.StartsWith("app-", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(exeDir);
            if (!string.IsNullOrEmpty(parent))
            {
                return parent;
            }
        }
        return exeDir;
    }

    private static string DetectAppDir(string packageRoot)
    {
        var envApp = Environment.GetEnvironmentVariable("Glimpseon_AppDir");
        if (!string.IsNullOrEmpty(envApp) && Directory.Exists(envApp))
        {
            return Path.GetFullPath(envApp);
        }

        try
        {
            foreach (var entry in Directory.EnumerateDirectories(packageRoot, "app-*"))
            {
                var recordPath = Path.Combine(entry, "record.json");
                if (!File.Exists(recordPath))
                {
                    continue;
                }
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(recordPath));
                    var element = doc.RootElement;
                    var isCurrent = element.TryGetProperty("current", out var cur) && cur.ValueKind == JsonValueKind.Number && cur.GetInt32() == 1;
                    var isPartial = element.TryGetProperty("partial", out var partial) && partial.ValueKind == JsonValueKind.True;
                    if (isCurrent && !isPartial)
                    {
                        return entry;
                    }
                }
                catch (Exception e)
                {

                    Glimpseon.Core.Log.Warning($"[PATHS] 目录扫描失败跳过: {e.Message}");
                    continue;
                }
            }
        }
        catch (Exception e)
        {

            Glimpseon.Core.Log.Warning($"[PATHS] 扫描失败 用包根目录: {e.Message}");
            // 扫描失败 最后用包根目录
        }

        return packageRoot;
    }

    private static (string Version, string BuildDate) ReadVersion()
    {
        try
        {
            var recordPath = Path.Combine(AppDir, "record.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(recordPath));
            var version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "1.0.0" : "1.0.0";
            var buildDate = doc.RootElement.TryGetProperty("build_date", out var b) ? b.GetString() ?? "" : "";
            return (version, buildDate);
        }
        catch (Exception e)
        {

            Glimpseon.Core.Log.Warning($"[PATHS] 版本读取失败 用 1.0.0: {e.Message}");
            return ("1.0.0", "");
        }
    }

    public static void EnsureDataDirs()
    {
        var dirs = new[]
        {
            DataRoot, DataConfig, DataLog, DataCache, DataTemp,
            DataProfile, DataUser, DataIcon, DataWallpaper, DataClassPhotos, DataNotes,
        };
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch (Exception e)
                {

                    Glimpseon.Core.Log.Warning($"[PATHS] 目录创建失败: {e.Message}");
                    // 创建失败留待后续重试
                }
            }
        }
    }

    public static string GetResourcePath(string relativePath)
    {
        var appPath = Path.Combine(AppDir, relativePath);
        if (File.Exists(appPath) || Directory.Exists(appPath))
        {
            return appPath;
        }
        return appPath;
    }
}
