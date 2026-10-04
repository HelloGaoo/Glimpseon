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

// 课程表档案
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Glimpseon.Core;

public sealed class TimetableProfile
{
    public string Name { get; set; } = "档案配置-1";
    public int DefaultClassDuration { get; set; } = 40;
    public int DefaultBreakDuration { get; set; } = 10;
    // periods: [{type, start, end}] courses: {"<periodIndex>": {...}}
    public List<JsonObject> Periods { get; set; } = new();
    public Dictionary<string, JsonObject> Courses { get; set; } = new();

    public JsonObject ToJson()
    {
        var periods = new JsonArray();
        foreach (var p in Periods)
        {
            periods.Add(p.DeepClone());
        }
        var courses = new JsonObject();
        foreach (var (key, value) in Courses)
        {
            courses[key] = value.DeepClone();
        }
        return new JsonObject
        {
            ["name"] = Name,
            ["defaultClassDuration"] = DefaultClassDuration,
            ["defaultBreakDuration"] = DefaultBreakDuration,
            ["periods"] = periods,
            ["courses"] = courses,
        };
    }

    public static TimetableProfile FromJson(JsonObject obj)
    {
        var profile = new TimetableProfile
        {
            Name = obj["name"]?.GetValue<string>() ?? "档案配置-1",
            DefaultClassDuration = obj["defaultClassDuration"]?.GetValue<int>() ?? 40,
            DefaultBreakDuration = obj["defaultBreakDuration"]?.GetValue<int>() ?? 10,
        };
        if (obj["periods"] is JsonArray periods)
        {
            foreach (var p in periods)
            {
                if (p is JsonObject po)
                {
                    profile.Periods.Add(JsonNode.Parse(po.ToJsonString())!.AsObject());
                }
            }
        }
        if (obj["courses"] is JsonObject courses)
        {
            foreach (var (key, value) in courses)
            {
                if (value is JsonObject vo)
                {
                    profile.Courses[key] = JsonNode.Parse(vo.ToJsonString())!.AsObject();
                }
            }
        }
        return profile;
    }

    public void Save(string? filepath = null)
    {
        filepath ??= Timetable.GetProfilePath(Name);
        Directory.CreateDirectory(Path.GetDirectoryName(filepath)!);
        File.WriteAllText(filepath, ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Log.Info($"档案已保存: {Name}");
    }

    public static TimetableProfile Load(string filepath)
    {
        var profile = FromJson(JsonNode.Parse(File.ReadAllText(filepath))!.AsObject());
        if (profile.Name != _lastLoggedName)
        {
            Log.Info($"档案已加载: {profile.Name}");
            _lastLoggedName = profile.Name;
        }
        else
        {
            Log.Debug($"档案已加载: {profile.Name}");
        }
        return profile;
    }

    private static string? _lastLoggedName;

    public void AddPeriod(string periodType, string start, string end)
    {
        Periods.Add(new JsonObject { ["type"] = periodType, ["start"] = start, ["end"] = end });
        var idx = Periods.Count - 1;
        Courses[idx.ToString()] = new JsonObject();
        Log.Debug($"添加时段: {periodType} {start}-{end}");
    }

    public void RemovePeriod(int index)
    {
        if (index < 0 || index >= Periods.Count)
        {
            Log.Warning($"移除时段失败: 索引越界 {index}");
            return;
        }
        Periods.RemoveAt(index);
        var newCourses = new Dictionary<string, JsonObject>();
        for (var i = 0; i < Periods.Count; i++)
        {
            var oldKey = i < index ? i.ToString() : (i + 1).ToString();
            newCourses[i.ToString()] = Courses.GetValueOrDefault(oldKey, new JsonObject());
        }
        Courses = newCourses;
        Log.Debug($"移除时段: index={index} 剩余{Periods.Count}个");
    }

    public string GetNextStartTime() =>
        Periods.Count == 0 ? "08:00" : Periods[^1]["end"]?.GetValue<string>() ?? "08:00";

    public int PeriodCount() => Periods.Count;
}

public static class Timetable
{
    public static string GetProfilePath(string name) => Path.Combine(Paths.DataProfile, $"{name}.json");

    private static readonly Regex ProfilePattern = new(@"^档案配置-\d+\.json$", RegexOptions.Compiled);

    public static List<string> ListProfiles()
    {
        Directory.CreateDirectory(Paths.DataProfile);
        var files = Directory.EnumerateFiles(Paths.DataProfile)
            .Select(Path.GetFileName)
            .Where(f => f is not null && ProfilePattern.IsMatch(f))
            .Select(f => f!)
            .OrderBy(f => int.Parse(Regex.Match(f, @"\d+").Value))
            .ToList();
        var names = files.Select(Path.GetFileNameWithoutExtension).ToList();
        Log.Debug($"档案列表: {string.Join(", ", names)}");
        return names;
    }

    public static string NextProfileName()
    {
        var names = ListProfiles();
        if (names.Count == 0)
        {
            Log.Debug("无现有档案 下一个名称: 档案配置-1");
            return "档案配置-1";
        }
        var max = names.Max(n => int.Parse(Regex.Match(n, @"\d+").Value));
        return $"档案配置-{max + 1}";
    }

    public static string EnsureDefaultProfile()
    {
        var names = ListProfiles();
        if (names.Count == 0)
        {
            var name = "档案配置-1";
            new TimetableProfile { Name = name }.Save();
            Log.Info($"已创建默认档案: {name}");
            return name;
        }
        Log.Debug($"用现有档案: {names[^1]}");
        return names[^1];
    }

    public static void RenameProfile(string oldName, string newName)
    {
        var oldPath = GetProfilePath(oldName);
        var newPath = GetProfilePath(newName);
        if (File.Exists(oldPath))
        {
            File.Move(oldPath, newPath);
            Log.Info($"档案重命名: {oldName} -> {newName}");
        }
        else
        {
            Log.Warning($"重命名失败 档案不存在: {oldName}");
        }
    }

    public static void DeleteProfile(string name)
    {
        var path = GetProfilePath(name);
        if (File.Exists(path))
        {
            File.Delete(path);
            Log.Info($"档案已删除: {name}");
        }
        else
        {
            Log.Warning($"删除失败 档案不存在: {name}");
        }
    }
}
