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
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

/// <summary>花名册分档案 行={no 编号, name 姓名}</summary>
public sealed class RosterDoc
{
    public string Name { get; set; } = "花名册-1";
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

    public static RosterDoc FromJson(JsonObject obj)
    {
        var doc = new RosterDoc
        {
            Name = obj["name"]?.GetValue<string>() ?? "花名册-1",
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
        filepath ??= ScheduleStore.SubPath("roster", Name);
        ScheduleStore.WriteJson(filepath, ToJson());
    }

    public static RosterDoc Load(string filepath) =>
        FromJson(JsonNode.Parse(File.ReadAllText(filepath))!.AsObject());

    public static RosterDoc LoadOrDefault(string name)
    {
        var path = ScheduleStore.SubPath("roster", name);
        return File.Exists(path) ? Load(path) : new RosterDoc { Name = name };
    }

    public void AddRow(string no = "", string name = "") =>
        Rows.Add(new JsonObject { ["no"] = no, ["name"] = name });

    public (string No, string Name) Entry(int idx) =>
        (Rows[idx]["no"]?.GetValue<string>() ?? "", Rows[idx]["name"]?.GetValue<string>() ?? "");
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
    public string Roster { get; set; } = "";

    public JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["course"] = Course,
        ["timetable"] = Timetable,
        ["duty"] = Duty,
        ["subject"] = Subject,
        ["roster"] = Roster,
    };

    public static ScheduleProfile FromJson(JsonObject obj) => new()
    {
        Name = obj["name"]?.GetValue<string>() ?? "总档案-1",
        Course = obj["course"]?.GetValue<string>() ?? "",
        Timetable = obj["timetable"]?.GetValue<string>() ?? "",
        Duty = obj["duty"]?.GetValue<string>() ?? "",
        Subject = obj["subject"]?.GetValue<string>() ?? "",
        Roster = obj["roster"]?.GetValue<string>() ?? "",
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
            "roster" when Roster.Length > 0 && File.Exists(ScheduleStore.SubPath("roster", Roster)) =>
                RosterDoc.Load(ScheduleStore.SubPath("roster", Roster)).ToJson(),
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
            master.Roster = inner.Roster;
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
                        case "roster": master.Roster = subName; break;
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
        if (Roster.Length == 0 || !File.Exists(ScheduleStore.SubPath("roster", Roster)))
        {
            Roster = ScheduleStore.NextSubName("roster");
            new RosterDoc { Name = Roster }.Save();
        }
    }
}

/// <summary>档案目录管理与迁移</summary>
public static class ScheduleStore
{
    public const string DefaultMasterName = "总档案-1";
    public static readonly string[] SubTypes = ["course", "timetable", "duty", "subject", "roster"];

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
            "roster" => "花名册",
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
            if (type == "roster" && master.Roster == oldName) { master.Roster = newName; changed = true; }
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
            if (type == "roster" && master.Roster == name) { master.Roster = ""; changed = true; }
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
        new RosterDoc { Name = "花名册-1" }.Save();
        var master = new ScheduleProfile
        {
            Name = "总档案-1",
            Course = course.Name,
            Timetable = timetable.Name,
            Duty = "值周表-1",
            Subject = "学科-1",
            Roster = "花名册-1",
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

// 花名册 xlsx/csv 导入导出
/// <summary>花名册导入结果</summary>
public sealed class RosterImportResult
{
    public List<JsonObject> Rows { get; } = new();
    public int Skipped { get; set; }
}

public static class RosterExcel
{
    private static readonly string[] NoKeys =
        ["学号", "编号", "序号", "号码", "考号", "座号", "座位号", "no.", "no", "id", "number"];

    private static readonly string[] NameKeys = ["姓名", "名字", "学生", "学员", "name", "student"];

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>按扩展名分派导入</summary>
    public static RosterImportResult Import(string path)
    {
        var table = path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadXlsx(path) : ReadCsv(path);
        var (noCol, nameCol, skip) = MapColumns(table);
        var result = new RosterImportResult();
        for (var i = skip; i < table.Length; i++)
        {
            var row = table[i];
            var name = CellAt(row, nameCol);
            var no = noCol >= 0 ? CellAt(row, noCol) : "";
            if (name.Length == 0 && no.Length == 0)
            {
                continue;
            }
            if (name.Length == 0)
            {
                result.Skipped++;
                continue;
            }
            result.Rows.Add(new JsonObject { ["no"] = no, ["name"] = name });
        }
        return result;
    }

    /// <summary>表头定位 前8行内找</summary>
    private static (int NoCol, int NameCol, int Skip) MapColumns(string[][] table)
    {
        for (var r = 0; r < Math.Min(8, table.Length); r++)
        {
            var row = table[r];
            int? noCol = null, nameCol = null;
            for (var c = 0; c < row.Length; c++)
            {
                var text = Normalize(row[c]).ToLowerInvariant();
                if (text.Length == 0)
                {
                    continue;
                }
                if (nameCol is null && NameKeys.Any(k => text.Contains(k)))
                {
                    nameCol = c;
                    continue;
                }
                if (noCol is null && NoKeys.Any(k => MatchKey(text, k)))
                {
                    noCol = c;
                }
            }
            if (nameCol is not null)
            {
                return (noCol ?? -1, nameCol.Value, r + 1);
            }
        }
        // 无表头启发 数值列=编号
        if (table.Length > 0)
        {
            var width = table.Max(r => r.Length);
            if (width >= 2)
            {
                var leftNumeric = ColumnNumericish(table, 0);
                var rightNumeric = ColumnNumericish(table, 1);
                if (leftNumeric && !rightNumeric)
                {
                    return (0, 1, 0);
                }
                if (rightNumeric && !leftNumeric)
                {
                    return (1, 0, 0);
                }
                return (-1, 0, 0);
            }
            return (-1, 0, 0);
        }
        return (-1, 0, 0);
    }

    /// <summary>数值主导列=编号</summary>
    private static bool ColumnNumericish(string[][] table, int col)
    {
        var hits = 0;
        var total = 0;
        for (var r = 0; r < Math.Min(20, table.Length); r++)
        {
            var text = CellAt(table[r], col);
            if (text.Length == 0)
            {
                continue;
            }
            total++;
            if (text.All(char.IsDigit) || (text.Length > 0 && char.IsDigit(text[0]) && text.All(c => char.IsDigit(c) || c == '.')))
            {
                hits++;
            }
        }
        return total > 0 && hits * 2 >= total;
    }

    /// <summary>英文短键全等 中文键子串</summary>
    private static bool MatchKey(string text, string key) =>
        key is "no" or "id" ? text == key : text.Contains(key);

    private static string CellAt(string[] row, int col) =>
        col >= 0 && col < row.Length ? Normalize(row[col]) : "";

    /// <summary>去首尾 全角空格 空白折叠</summary>
    private static string Normalize(string text)
    {
        var trimmed = text.Trim().Trim('\u3000', '\u00A0').Trim();
        return trimmed.Length == 0 ? "" : string.Join(' ', trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>xlsx sheet 解析为文本矩阵</summary>
    private static string[][] ReadXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        string[]? shared = null;
        var sharedEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedEntry is not null)
        {
            using var s = sharedEntry.Open();
            var doc = XDocument.Load(s);
            shared = doc.Root!.Elements(Main + "si")
                .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))
                .ToArray();
        }

        // 首个 sheet rid->target
        XElement wb;
        using (var s = EntryStream(zip, "xl/workbook.xml"))
        {
            wb = XDocument.Load(s).Root!;
        }
        var firstSheet = wb.Element(Main + "sheets")?.Elements(Main + "sheet").FirstOrDefault()
            ?? throw new InvalidDataException("xlsx 无工作表");
        var rid = firstSheet.Attribute(Rel + "id")?.Value ?? "rId1";
        string target;
        using (var s = EntryStream(zip, "xl/_rels/workbook.xml.rels"))
        {
            var rels = XDocument.Load(s).Root!.Elements(PkgRel + "Relationship");
            target = rels.First(r => r.Attribute("Id")?.Value == rid).Attribute("Target")!.Value;
        }
        if (target.StartsWith('/'))
        {
            target = target[1..];
        }
        else if (!target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
        {
            target = "xl/" + target;
        }

        XElement sheet;
        using (var s = EntryStream(zip, target))
        {
            sheet = XDocument.Load(s).Root!;
        }
        var rows = new List<string[]>();
        foreach (var row in sheet.Descendants(Main + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            foreach (var c in row.Elements(Main + "c"))
            {
                var col = ColumnIndex(c.Attribute("r")?.Value ?? "");
                var type = c.Attribute("t")?.Value ?? "n";
                string text = type switch
                {
                    "s" when shared is not null => c.Element(Main + "v") is { } v
                        && int.TryParse(v.Value, out var si) && si >= 0 && si < shared.Length ? shared[si] : "",
                    "inlineStr" => string.Concat(c.Element(Main + "is")?.Descendants(Main + "t").Select(t => t.Value) ?? []),
                    "b" => c.Element(Main + "v")?.Value == "1" ? "TRUE" : "FALSE",
                    _ => c.Element(Main + "v")?.Value ?? "",
                };
                if (text.Length > 0)
                {
                    cells[col] = text;
                }
            }
            rows.Add(cells.Count == 0 ? [] : cells.Keys.Max() + 1 <= cells.Count
                ? [.. cells.Values]
                : Expand(cells));
        }
        return [.. rows];
    }

    private static string[] Expand(SortedDictionary<int, string> cells)
    {
        var arr = new string[cells.Keys.Max() + 1];
        for (var i = 0; i < arr.Length; i++)
        {
            arr[i] = "";
        }
        foreach (var (k, v) in cells)
        {
            arr[k] = v;
        }
        return arr;
    }

    private static Stream EntryStream(ZipArchive zip, string name)
    {
        var e = zip.GetEntry(name) ?? zip.GetEntry(name.Replace('/', '\\'))
            ?? throw new InvalidDataException($"xlsx 缺少 {name}");
        return e.Open();
    }

    /// <summary>A1 引用转列下标</summary>
    private static int ColumnIndex(string reference)
    {
        var col = 0;
        foreach (var ch in reference)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                col = col * 26 + (ch - 'A' + 1);
            }
            else if (ch is >= 'a' and <= 'z')
            {
                col = col * 26 + (ch - 'a' + 1);
            }
            else
            {
                break;
            }
        }
        return Math.Max(0, col - 1);
    }

    /// <summary>csv 解析 引号转义</summary>
    private static string[][] ReadCsv(string path)
    {
        var text = File.ReadAllText(path);
        var table = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuote = false;
        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
        }
        void EndRow()
        {
            EndField();
            table.Add([.. fields]);
            fields.Clear();
        }
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuote)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuote = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuote = true;
            }
            else if (ch == ',')
            {
                EndField();
            }
            else if (ch == '\n')
            {
                EndRow();
            }
            else if (ch == '\r')
            {
                // \r\n 与裸 \r 都收行
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                EndRow();
            }
            else
            {
                field.Append(ch);
            }
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            EndRow();
        }
        return [.. table.Where(r => r.Any(f => f.Trim().Length > 0))];
    }

    /// <summary>导出 csv UTF-8 BOM</summary>
    public static void ExportCsv(string path, IReadOnlyList<JsonObject> rows)
    {
        var sb = new StringBuilder();
        sb.Append("编号,姓名\r\n");
        foreach (var r in rows)
        {
            sb.Append(CsvField(r["no"]?.GetValue<string>() ?? ""));
            sb.Append(',');
            sb.Append(CsvField(r["name"]?.GetValue<string>() ?? ""));
            sb.Append("\r\n");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string CsvField(string text) =>
        text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;

    /// <summary>导出 xlsx inlineStr</summary>
    public static void ExportXlsx(string path, IReadOnlyList<JsonObject> rows)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(zip, "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);
        WriteEntry(zip, "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        WriteEntry(zip, "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="花名册" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);
        WriteEntry(zip, "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);

        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.Append($"""<worksheet xmlns="{Main}"><sheetData>""");
        sb.Append(RowXml(1, "编号", "姓名"));
        for (var i = 0; i < rows.Count; i++)
        {
            var no = rows[i]["no"]?.GetValue<string>() ?? "";
            var name = rows[i]["name"]?.GetValue<string>() ?? "";
            sb.Append(RowXml(i + 2, no, name));
        }
        sb.Append("</sheetData></worksheet>");
        WriteEntry(zip, "xl/worksheets/sheet1.xml", sb.ToString());
    }

    private static string RowXml(int idx, string no, string name) =>
        $"<row r=\"{idx}\">" +
        $"<c r=\"A{idx}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(no)}</t></is></c>" +
        $"<c r=\"B{idx}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(name)}</t></is></c>" +
        "</row>";

    private static string Esc(string text) => text
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
