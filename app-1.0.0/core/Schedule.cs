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

// 课程表总档案与分档案
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Glimpseon.Core;

/// <summary>时间表分档案 时段线与默认时长</summary>
public sealed class TimetableDoc
{
    public string Name { get; set; } = "时间表-1";
    public int DefaultClassDuration { get; set; } = 40;
    public int DefaultBreakDuration { get; set; } = 10;
    // periods: [{type, start, end}]
    public List<JsonObject> Periods { get; set; } = new();

    public JsonObject ToJson()
    {
        var periods = new JsonArray();
        foreach (var p in Periods)
        {
            periods.Add(p.DeepClone());
        }
        return new JsonObject
        {
            ["name"] = Name,
            ["defaultClassDuration"] = DefaultClassDuration,
            ["defaultBreakDuration"] = DefaultBreakDuration,
            ["periods"] = periods,
        };
    }

    public static TimetableDoc FromJson(JsonObject obj)
    {
        var doc = new TimetableDoc
        {
            Name = obj["name"]?.GetValue<string>() ?? "时间表-1",
            DefaultClassDuration = obj["defaultClassDuration"]?.GetValue<int>() ?? 40,
            DefaultBreakDuration = obj["defaultBreakDuration"]?.GetValue<int>() ?? 10,
        };
        if (obj["periods"] is JsonArray periods)
        {
            foreach (var p in periods)
            {
                if (p is JsonObject po)
                {
                    doc.Periods.Add(JsonNode.Parse(po.ToJsonString())!.AsObject());
                }
            }
        }
        return doc;
    }

    public void Save(string? filepath = null)
    {
        filepath ??= ScheduleStore.SubPath("timetable", Name);
        ScheduleStore.WriteJson(filepath, ToJson());
    }

    public static TimetableDoc Load(string filepath) =>
        FromJson(JsonNode.Parse(File.ReadAllText(filepath))!.AsObject());

    public static TimetableDoc LoadOrDefault(string name)
    {
        var path = ScheduleStore.SubPath("timetable", name);
        return File.Exists(path) ? Load(path) : new TimetableDoc { Name = name };
    }

    public void AddPeriod(string periodType, string start, string end)
    {
        Periods.Add(new JsonObject { ["type"] = periodType, ["start"] = start, ["end"] = end });
        Log.Debug($"[时间表] 添加时段: {periodType} {start}-{end}");
    }

    public void RemovePeriod(int index)
    {
        if (index < 0 || index >= Periods.Count)
        {
            Log.Warning($"[时间表] 移除时段失败: 索引越界 {index}");
            return;
        }
        Periods.RemoveAt(index);
        Log.Debug($"[时间表] 移除时段: index={index} 剩余{Periods.Count}个");
    }

    public string GetNextStartTime() =>
        Periods.Count == 0 ? "08:00" : Periods[^1]["end"]?.GetValue<string>() ?? "08:00";

    public int PeriodCount() => Periods.Count;
}

/// <summary>课表分档案 时段索引>星期>科目</summary>
public sealed class CourseDoc
{
    public string Name { get; set; } = "课表-1";
    // key = 时段索引 value = {monday..sunday}
    public Dictionary<string, JsonObject> Courses { get; set; } = new();

    public JsonObject ToJson()
    {
        var courses = new JsonObject();
        foreach (var (key, value) in Courses)
        {
            courses[key] = value.DeepClone();
        }
        return new JsonObject
        {
            ["name"] = Name,
            ["courses"] = courses,
        };
    }

    public static CourseDoc FromJson(JsonObject obj)
    {
        var doc = new CourseDoc
        {
            Name = obj["name"]?.GetValue<string>() ?? "课表-1",
        };
        if (obj["courses"] is JsonObject courses)
        {
            foreach (var (key, value) in courses)
            {
                if (value is JsonObject vo)
                {
                    doc.Courses[key] = JsonNode.Parse(vo.ToJsonString())!.AsObject();
                }
            }
        }
        return doc;
    }

    public void Save(string? filepath = null)
    {
        filepath ??= ScheduleStore.SubPath("course", Name);
        ScheduleStore.WriteJson(filepath, ToJson());
    }

    public static CourseDoc Load(string filepath) =>
        FromJson(JsonNode.Parse(File.ReadAllText(filepath))!.AsObject());

    public static CourseDoc LoadOrDefault(string name)
    {
        var path = ScheduleStore.SubPath("course", name);
        return File.Exists(path) ? Load(path) : new CourseDoc { Name = name };
    }

    public string GetCell(int periodIdx, string day) =>
        Courses.TryGetValue(periodIdx.ToString(), out var c) ? c[day]?.GetValue<string>() ?? "" : "";

    public void SetCell(int periodIdx, string day, string text)
    {
        var key = periodIdx.ToString();
        if (!Courses.TryGetValue(key, out var row))
        {
            row = new JsonObject();
            Courses[key] = row;
        }
        row[day] = text;
    }

    /// <summary>时段删除后课程键重排 删除位丢弃 后续键前移</summary>
    public void RemovePeriodIndex(int removedIdx)
    {
        var rebased = new Dictionary<string, JsonObject>();
        foreach (var (key, value) in Courses)
        {
            if (!int.TryParse(key, out var i))
            {
                rebased[key] = value;
                continue;
            }
            if (i == removedIdx)
            {
                continue;
            }
            rebased[(i < removedIdx ? i : i - 1).ToString()] = value;
        }
        Courses = rebased;
    }
}

/// <summary>值周表分档案 行={parity 单双周, weekday 星期几, group 小组, name 姓名, role 职责}</summary>
public sealed class DutyDoc
{
    public string Name { get; set; } = "值周表-1";
    public List<JsonObject> Rows { get; set; } = new();

    public JsonObject ToJson()
    {
        var rows = new JsonArray();
        foreach (var r in Rows)
        {
            rows.Add(r.DeepClone());
        }
        return new JsonObject
        {
            ["name"] = Name,
            ["rows"] = rows,
        };
    }

    public static DutyDoc FromJson(JsonObject obj)
    {
        var doc = new DutyDoc
        {
            Name = obj["name"]?.GetValue<string>() ?? "值周表-1",
        };
        if (obj["rows"] is JsonArray rows)
        {
            foreach (var r in rows)
            {
                if (r is JsonObject ro)
                {
                    doc.Rows.Add(JsonNode.Parse(ro.ToJsonString())!.AsObject());
                }
            }
        }
        return doc;
    }

    public void Save(string? filepath = null)
    {
        filepath ??= ScheduleStore.SubPath("duty", Name);
        ScheduleStore.WriteJson(filepath, ToJson());
    }

    public static DutyDoc Load(string filepath) =>
        FromJson(JsonNode.Parse(File.ReadAllText(filepath))!.AsObject());

    public static DutyDoc LoadOrDefault(string name)
    {
        var path = ScheduleStore.SubPath("duty", name);
        return File.Exists(path) ? Load(path) : new DutyDoc { Name = name };
    }

    public void AddRow(string parity = "单周", string weekday = "monday", string group = "", string name = "", string role = "") =>
        Rows.Add(new JsonObject { ["parity"] = parity, ["weekday"] = weekday, ["group"] = group, ["name"] = name, ["role"] = role });
}

/// <summary>学科分档案 行={name 科目, short 简写, teacher 老师, outdoor 室外课程}</summary>
public sealed class SubjectDoc
{
    public string Name { get; set; } = "学科-1";
    public List<JsonObject> Rows { get; set; } = new();

    public JsonObject ToJson()
    {
        var rows = new JsonArray();
        foreach (var r in Rows)
        {
            rows.Add(r.DeepClone());
        }
        return new JsonObject
        {
            ["name"] = Name,
            ["rows"] = rows,
        };
    }

    public static SubjectDoc FromJson(JsonObject obj)
    {
        var doc = new SubjectDoc
        {
            Name = obj["name"]?.GetValue<string>() ?? "学科-1",
        };
        if (obj["rows"] is JsonArray rows)
        {
            foreach (var r in rows)
            {
                if (r is JsonObject ro)
                {
                    doc.Rows.Add(JsonNode.Parse(ro.ToJsonString())!.AsObject());
                }
            }
        }
        return doc;
    }

    public void Save(string? filepath = null)
    {
        filepath ??= ScheduleStore.SubPath("subject", Name);
        ScheduleStore.WriteJson(filepath, ToJson());
    }

    public static SubjectDoc Load(string filepath) =>
        FromJson(JsonNode.Parse(File.ReadAllText(filepath))!.AsObject());

    public static SubjectDoc LoadOrDefault(string name)
    {
        var path = ScheduleStore.SubPath("subject", name);
        return File.Exists(path) ? Load(path) : new SubjectDoc { Name = name };
    }

    public void AddRow(string name = "", string shortName = "", string teacher = "", bool outdoor = false) =>
        Rows.Add(new JsonObject { ["name"] = name, ["short"] = shortName, ["teacher"] = teacher, ["outdoor"] = outdoor });

    public List<string> SubjectNames() =>
        Rows.Select(r => r["name"]?.GetValue<string>() ?? "").Where(n => n.Length > 0).ToList();
}

/// <summary>总档案 绑定四类分档案</summary>
public sealed class ScheduleProfile
{
    public string Name { get; set; } = "总档案-1";
    public string Course { get; set; } = "";
    public string Timetable { get; set; } = "";
    public string Duty { get; set; } = "";
    public string Subject { get; set; } = "";

    public JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["course"] = Course,
        ["timetable"] = Timetable,
        ["duty"] = Duty,
        ["subject"] = Subject,
    };

    public static ScheduleProfile FromJson(JsonObject obj) => new()
    {
        Name = obj["name"]?.GetValue<string>() ?? "总档案-1",
        Course = obj["course"]?.GetValue<string>() ?? "",
        Timetable = obj["timetable"]?.GetValue<string>() ?? "",
        Duty = obj["duty"]?.GetValue<string>() ?? "",
        Subject = obj["subject"]?.GetValue<string>() ?? "",
    };

    public void Save(string? filepath = null)
    {
        filepath ??= ScheduleStore.MasterPath(Name);
        ScheduleStore.WriteJson(filepath, ToJson());
    }

    public static ScheduleProfile Load(string filepath) =>
        FromJson(JsonNode.Parse(File.ReadAllText(filepath))!.AsObject());

    /// <summary>总档案导出 捆绑全部已绑定分档案</summary>
    public JsonObject ExportBundle()
    {
        var bundle = new JsonObject
        {
            ["kind"] = "glimpseon-schedule-bundle",
            ["master"] = ToJson(),
        };
        var subOf = new Func<string, JsonObject?>(type => type switch
        {
            "course" when Course.Length > 0 && File.Exists(ScheduleStore.SubPath("course", Course)) =>
                CourseDoc.Load(ScheduleStore.SubPath("course", Course)).ToJson(),
            "timetable" when Timetable.Length > 0 && File.Exists(ScheduleStore.SubPath("timetable", Timetable)) =>
                TimetableDoc.Load(ScheduleStore.SubPath("timetable", Timetable)).ToJson(),
            "duty" when Duty.Length > 0 && File.Exists(ScheduleStore.SubPath("duty", Duty)) =>
                DutyDoc.Load(ScheduleStore.SubPath("duty", Duty)).ToJson(),
            "subject" when Subject.Length > 0 && File.Exists(ScheduleStore.SubPath("subject", Subject)) =>
                SubjectDoc.Load(ScheduleStore.SubPath("subject", Subject)).ToJson(),
            _ => null,
        });
        var subs = new JsonObject();
        foreach (var type in ScheduleStore.SubTypes)
        {
            var doc = subOf(type);
            if (doc is not null)
            {
                subs[type] = doc;
            }
        }
        bundle["subs"] = subs;
        return bundle;
    }

    /// <summary>总档案导入 解包写入分档案并绑定</summary>
    public static ScheduleProfile ImportBundle(JsonObject bundle, string name)
    {
        var master = new ScheduleProfile { Name = name };
        if (bundle["master"] is JsonObject mo)
        {
            var inner = FromJson(mo);
            master.Course = inner.Course;
            master.Timetable = inner.Timetable;
            master.Duty = inner.Duty;
            master.Subject = inner.Subject;
        }
        if (bundle["subs"] is JsonObject subs)
        {
            foreach (var type in ScheduleStore.SubTypes)
            {
                if (subs[type] is JsonObject doc)
                {
                    var subName = ScheduleStore.NextSubName(type);
                    var renamed = (JsonObject)JsonNode.Parse(doc.ToJsonString())!.AsObject();
                    renamed["name"] = subName;
                    ScheduleStore.WriteJson(ScheduleStore.SubPath(type, subName), renamed);
                    switch (type)
                    {
                        case "course": master.Course = subName; break;
                        case "timetable": master.Timetable = subName; break;
                        case "duty": master.Duty = subName; break;
                        case "subject": master.Subject = subName; break;
                    }
                }
            }
        }
        master.RepairBindings();
        master.Save();
        return master;
    }

    /// <summary>补齐缺失绑定 绑定名无文件时新建空档</summary>
    public void RepairBindings()
    {
        if (Course.Length == 0 || !File.Exists(ScheduleStore.SubPath("course", Course)))
        {
            Course = ScheduleStore.NextSubName("course");
            new CourseDoc { Name = Course }.Save();
        }
        if (Timetable.Length == 0 || !File.Exists(ScheduleStore.SubPath("timetable", Timetable)))
        {
            Timetable = ScheduleStore.NextSubName("timetable");
            new TimetableDoc { Name = Timetable }.Save();
        }
        if (Duty.Length == 0 || !File.Exists(ScheduleStore.SubPath("duty", Duty)))
        {
            Duty = ScheduleStore.NextSubName("duty");
            new DutyDoc { Name = Duty }.Save();
        }
        if (Subject.Length == 0 || !File.Exists(ScheduleStore.SubPath("subject", Subject)))
        {
            Subject = ScheduleStore.NextSubName("subject");
            // 新总档案学科分档案默认带全科目种子
            new SubjectDoc { Name = Subject, Rows = ScheduleStore.DefaultSubjectRows() }.Save();
        }
    }
}

/// <summary>档案目录管理与迁移</summary>
public static class ScheduleStore
{
    public const string DefaultMasterName = "总档案-1";
    public static readonly string[] SubTypes = ["course", "timetable", "duty", "subject"];

    public static string MasterDir => Path.Combine(Paths.DataProfile, "schedule");

    public static string SubDir(string type) => Path.Combine(Paths.DataProfile, type);

    public static string MasterPath(string name) => Path.Combine(MasterDir, $"{name}.json");

    public static string SubPath(string type, string name) => Path.Combine(SubDir(type), $"{name}.json");

    private static readonly Regex InvalidChars = new(@"[\\/:*?""<>|]", RegexOptions.Compiled);

    public static string SanitizeName(string name) => InvalidChars.Replace(name.Trim(), "").Trim();

    private static List<string> ListJson(string dir)
    {
        Directory.CreateDirectory(dir);
        return Directory.EnumerateFiles(dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> ListMasters() => ListJson(MasterDir);

    public static List<string> ListSubs(string type) => ListJson(SubDir(type));

    public static void WriteJson(string filepath, JsonObject obj)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filepath)!);
        File.WriteAllText(filepath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string NextMasterName()
    {
        var names = ListMasters();
        for (var i = 1; i < 10000; i++)
        {
            if (!names.Contains($"总档案-{i}"))
            {
                return $"总档案-{i}";
            }
        }
        return $"总档案-{DateTime.Now:HHmmss}";
    }

    public static string NextSubName(string type)
    {
        var prefix = type switch
        {
            "course" => "课表",
            "timetable" => "时间表",
            "duty" => "值周表",
            _ => "学科",
        };
        var names = ListSubs(type);
        for (var i = 1; i < 10000; i++)
        {
            if (!names.Contains($"{prefix}-{i}"))
            {
                return $"{prefix}-{i}";
            }
        }
        return $"{prefix}-{DateTime.Now:HHmmss}";
    }

    public static ScheduleProfile LoadMaster(string name) => ScheduleProfile.Load(MasterPath(name));

    public static void RenameMaster(string oldName, string newName)
    {
        newName = SanitizeName(newName);
        if (newName.Length == 0 || File.Exists(MasterPath(newName)))
        {
            Log.Warning($"[档案] 总档案重命名放弃: {oldName} -> {newName}");
            return;
        }
        File.Move(MasterPath(oldName), MasterPath(newName));
        Log.Info($"[档案] 总档案重命名: {oldName} -> {newName}");
    }

    public static void DeleteMaster(string name)
    {
        if (File.Exists(MasterPath(name)))
        {
            File.Delete(MasterPath(name));
            Log.Info($"[档案] 总档案已删除: {name}");
        }
    }

    public static void CopyMaster(string name)
    {
        var target = NextMasterName();
        var profile = LoadMaster(name);
        profile.Name = target;
        profile.Save();
        Log.Info($"[档案] 总档案已复制: {name} -> {target}");
    }

    /// <summary>分档案重命名 同步更新全部总档案引用</summary>
    public static void RenameSub(string type, string oldName, string newName)
    {
        newName = SanitizeName(newName);
        if (newName.Length == 0 || File.Exists(SubPath(type, newName)))
        {
            Log.Warning($"[档案] {type} 分档案重命名放弃: {oldName} -> {newName}");
            return;
        }
        File.Move(SubPath(type, oldName), SubPath(type, newName));
        foreach (var masterName in ListMasters())
        {
            var master = LoadMaster(masterName);
            var changed = false;
            if (type == "course" && master.Course == oldName) { master.Course = newName; changed = true; }
            if (type == "timetable" && master.Timetable == oldName) { master.Timetable = newName; changed = true; }
            if (type == "duty" && master.Duty == oldName) { master.Duty = newName; changed = true; }
            if (type == "subject" && master.Subject == oldName) { master.Subject = newName; changed = true; }
            if (changed)
            {
                master.Save();
            }
        }
        Log.Info($"[档案] {type} 分档案重命名: {oldName} -> {newName}");
    }

    /// <summary>分档案删除 同步清空全部总档案引用</summary>
    public static void DeleteSub(string type, string name)
    {
        if (File.Exists(SubPath(type, name)))
        {
            File.Delete(SubPath(type, name));
        }
        foreach (var masterName in ListMasters())
        {
            var master = LoadMaster(masterName);
            var changed = false;
            if (type == "course" && master.Course == name) { master.Course = ""; changed = true; }
            if (type == "timetable" && master.Timetable == name) { master.Timetable = ""; changed = true; }
            if (type == "duty" && master.Duty == name) { master.Duty = ""; changed = true; }
            if (type == "subject" && master.Subject == name) { master.Subject = ""; changed = true; }
            if (changed)
            {
                master.Save();
            }
        }
        Log.Info($"[档案] {type} 分档案已删除: {name}");
    }

    public static void CopySub(string type, string name)
    {
        var target = NextSubName(type);
        var src = SubPath(type, name);
        if (!File.Exists(src))
        {
            return;
        }
        var doc = JsonNode.Parse(File.ReadAllText(src))!.AsObject();
        doc["name"] = target;
        WriteJson(SubPath(type, target), doc);
        Log.Info($"[档案] {type} 分档案已复制: {name} -> {target}");
    }

    /// <summary>分档案导入 文件复制为该类型新分档案 返回新名</summary>
    public static string ImportSub(string type, string filePath)
    {
        var target = NextSubName(type);
        var doc = JsonNode.Parse(File.ReadAllText(filePath))!.AsObject();
        doc["name"] = target;
        WriteJson(SubPath(type, target), doc);
        Log.Info($"[档案] {type} 分档案已导入: {filePath} -> {target}");
        return target;
    }

    /// <summary>确保至少一个总档案与全部绑定齐备 空库建默认结构 每个课表来源各自保底活动总档案</summary>
    public static void EnsureDefaults()
    {
        Directory.CreateDirectory(MasterDir);
        foreach (var type in SubTypes)
        {
            Directory.CreateDirectory(SubDir(type));
        }
        if (ListMasters().Count == 0)
        {
            CreateDefault();
        }
        // 每来源活动总档案有效性 CI/CW 单开专属总档案
        EnsureActiveMaster(Config.ScheduleProfile);
        EnsureActiveMaster(Config.ScheduleProfileCi, "总档案-CI");
        EnsureActiveMaster(Config.ScheduleProfileCw, "总档案-CW");
        var profile = LoadMaster(Config.ScheduleProfile.Value);
        var before = (profile.Course, profile.Timetable, profile.Duty, profile.Subject);
        profile.RepairBindings();
        if (before != (profile.Course, profile.Timetable, profile.Duty, profile.Subject))
        {
            profile.Save();
        }
    }

    /// <summary>活动总档案失效时校正 优先落到 preferredName 缺档则补建</summary>
    private static void EnsureActiveMaster(ConfigItem<string> item, string? preferredName = null)
    {
        var masters = ListMasters();
        if (masters.Contains(item.Value))
        {
            return;
        }
        if (preferredName is not null && !masters.Contains(preferredName))
        {
            var master = new ScheduleProfile { Name = preferredName };
            master.RepairBindings();
            master.Save();
            masters = ListMasters();
        }
        if (preferredName is not null && masters.Contains(preferredName))
        {
            item.Value = preferredName;
        }
        else
        {
            item.Value = masters[0];
        }
        Log.Info($"[档案] 活动总档案校正: {item.Group}/{item.Name} = {item.Value}");
    }

    /// <summary>空库时创建默认总档案与四类分档案</summary>
    private static void CreateDefault()
    {
        var timetable = new TimetableDoc { Name = "时间表-1" };
        timetable.Save();
        var course = new CourseDoc { Name = "课表-1" };
        course.Save();
        new DutyDoc { Name = "值周表-1" }.Save();
        new SubjectDoc { Name = "学科-1", Rows = DefaultSubjectRows() }.Save();
        var master = new ScheduleProfile
        {
            Name = "总档案-1",
            Course = course.Name,
            Timetable = timetable.Name,
            Duty = "值周表-1",
            Subject = "学科-1",
        };
        master.Save();
        Log.Info("[档案] 已创建默认总档案: 总档案-1");
    }

    internal static List<JsonObject> DefaultSubjectRows()
    {
        (string Name, string Short)[] seeds =
        [
            ("语文", "语"), ("数学", "数"), ("英语", "英"), ("政治", "政"), ("历史", "史"),
            ("地理", "地"), ("生物", "生"), ("物理", "物"), ("化学", "化"), ("音乐", "音"),
            ("体育", "体"), ("美术", "美"), ("信息技术", "信"), ("心理", "心"), ("劳动", "劳"),
            ("科学", "科"), ("自习", "自"), ("考试", "考"), ("早读", "早"), ("班会", "班"), 
            ("社团", "社"), ("选修", "选")
                   ];
        var rows = new List<JsonObject>();
        foreach (var (name, shortName) in seeds)
        {
            // 户外课程
            var outdoor = name is "音乐" or "信息技术" or "体育" or "社团";
            rows.Add(new JsonObject { ["name"] = name, ["short"] = shortName, ["teacher"] = "", ["outdoor"] = outdoor });
        }
        return rows;
    }
}
