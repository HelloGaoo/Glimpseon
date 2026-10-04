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

// record.json 管理

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Glimpseon.Core;

public static class Record
{
    public static Dictionary<string, FileMeta> ScanFiles(string directory)
    {
        var files = new Dictionary<string, FileMeta>();
        if (!Directory.Exists(directory))
        {
            Log.Debug($"扫描目录为空或不存在可记录文件: {directory}");
            return files;
        }
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name == "record.json")
            {
                continue;
            }
            try
            {
                var relPath = Path.GetRelativePath(directory, file);
                var info = new FileInfo(file);
                files[relPath] = new FileMeta(Sha256OfFile(file), info.Length);
            }
            catch (Exception e)
            {
                Log.Warning($"扫描文件失败 {file}: {e.Message}");
            }
        }

        var totalSize = files.Values.Sum(f => f.Size);
        if (files.Count > 1000)
        {
            Log.Warning($"扫描目录文件数偏大: {directory} ({files.Count}个文件 共{totalSize}字节)");
        }
        else if (files.Count == 0)
        {
            Log.Debug($"扫描目录为空或不存在可记录文件: {directory}");
        }
        Log.Debug($"已扫描 {directory} {files.Count}文件 {totalSize}字节");
        return files;
    }

    private static string Sha256OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // 创建版本记录
    public static JsonObject CreateRecord(string version, string appDir, int current = 1, bool partial = false)
    {
        Log.Debug($"创建版本记录: {version} (current={current} partial={partial})");
        var files = new JsonObject();
        foreach (var (path, meta) in ScanFiles(appDir))
        {
            files[path] = new JsonObject { ["hash"] = meta.Hash, ["size"] = meta.Size };
        }
        return new JsonObject
        {
            ["current"] = current,
            ["partial"] = partial,
            ["version"] = version,
            ["files"] = files,
            ["variables"] = new JsonObject
            {
                ["install_time"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
            },
        };
    }

    public static bool SaveRecord(JsonObject record, string recordPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
            File.WriteAllText(recordPath, record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Log.Info($"record.json 已保存: {recordPath}");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"保存 record.json 失败: {e.Message}");
            return false;
        }
    }

    public static JsonObject? LoadRecord(string recordPath)
    {
        try
        {
            if (!File.Exists(recordPath))
            {
                Log.Debug($"record.json 不存在: {recordPath}");
                return null;
            }
            if (JsonNode.Parse(File.ReadAllText(recordPath)) is not JsonObject record)
            {
                Log.Warning($"record.json缺files字段 {recordPath}");
                return null;
            }
            if (!record.ContainsKey("files"))
            {
                Log.Warning($"record.json缺files字段 {recordPath}");
            }
            Log.Debug($"record.json 已加载 {recordPath} 版本 {record["version"]}");
            return record;
        }
        catch (Exception e)
        {
            Log.Error($"加载 record.json 失败: {e.Message}");
            return null;
        }
    }

    public static void DeactivateVersion(string versionDir)
    {
        var recordPath = Path.Combine(versionDir, "record.json");
        if (LoadRecord(recordPath) is { } record)
        {
            record["current"] = 0;
            SaveRecord(record, recordPath);
            Log.Info($"版本已取消激活: {versionDir}");
        }
        else
        {
            Log.Debug($"record.json 缺失 {versionDir}");
        }
    }
}

public sealed record FileMeta(string Hash, long Size);
