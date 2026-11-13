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

// TimetableScheduleProvider
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using Avalonia.Media.Imaging;
using IOPath = System.IO.Path;
using Avalonia.Controls.Shapes;
using System.Text.RegularExpressions;
using Avalonia.Animation;
using Avalonia.Threading;
using Glimpseon.Core.Services;
using Markdown.Avalonia;
using Avalonia.Input.Platform;
using System.Runtime.InteropServices;
using AvColor = Avalonia.Media.Color;
using System.Globalization;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Utilities;
using Avalonia.VisualTree;


namespace Glimpseon.UI;

// 课表共享数据源

public static class TimetableScheduleProvider
{
    private static readonly LinkageBridge CiBridge = new();
    private static readonly ClassWidgetsBridge CwBridge = new();
    private static bool _bridgeStarted;

    /// <summary>当日课表 按来源分发 联动/插件/本地</summary>
    public static List<ScheduleRow> GetTodaySchedule()
    {
        var result = new List<ScheduleRow>();
        // 插件提供者来源
        var profileSource = Config.ProfileSource.Value ?? "";
        if (profileSource.StartsWith("plugin:", StringComparison.Ordinal))
        {
            var key = profileSource["plugin:".Length..];
            if (ResolvePluginProvider(key) is { } entry)
            {
                try
                {
                    return entry.Provider();
                }
                catch (Exception e)
                {
                    Log.Warning($"[课表组件] 插件课表读取失败 [{key}]: {e.Message}");
                    return result;
                }
            }
            // 来源失效 回落本地档案
            Log.Warning($"[课表组件] 插件课表来源不可用: {key} 回落本地档案");
        }
        if (!UseLocalScheduleData())
        {
            try
            {
                var bridge = EnsureBridge();
                var schedule = bridge switch
                {
                    LinkageBridge ci => ci.GetTodaySchedule(),
                    ClassWidgetsBridge cw => cw.GetTodaySchedule(),
                    _ => null,
                };
                if (schedule is not null)
                {
                    return schedule;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[课表组件] 联动数据读取失败: {e.Message}");
                return result;
            }
        }
        try
        {
            var (master, timetable) = LoadActiveMasterTimetable();
            var now = AppUtils.PreciseNow();
            var dayName = DayKey(((int)now.DayOfWeek + 6) % 7 + 1);
            var nowTime = TimeOnly.FromDateTime(now);
            result.AddRange(BuildDayRows(master, timetable, DateOnly.FromDateTime(now), dayName, true, nowTime, _ => ""));
        }
        catch (Exception e)
        {
            Log.Warning($"[课表组件] 本地课表读取失败: {e.Message}");
        }
        return result;
    }

    /// <summary>单日行 整日占位 / 课间名 / patch 标记</summary>
    private static List<ScheduleRow> BuildDayRows(ScheduleProfile master, TimetableDoc timetable, DateOnly date,
        string dayKey, bool isToday, TimeOnly nowTime, Func<string, string> teacherOf)
    {
        var rows = new List<ScheduleRow>();
        var (course, wholeDay) = ResolveDayCourse(master, date);
        if (wholeDay is not null)
        {
            // 整日占位 场次优先
            rows.AddRange(WholeDayRows(wholeDay, isToday, nowTime));
            return rows;
        }
        var patches = ScheduleRules.ResolvePatches(master, date, timetable);
        var classCounter = 0;
        for (var i = 0; i < timetable.Periods.Count; i++)
        {
            var p = timetable.Periods[i];
            var start = p["start"]?.GetValue<string>() ?? "";
            var end = p["end"]?.GetValue<string>() ?? "";
            var ptype = p["type"]?.GetValue<string>() ?? "";
            var isCurrent = isToday &&
                            TimeOnly.TryParse(start, out var st) && TimeOnly.TryParse(end, out var et) &&
                            st <= nowTime && nowTime < et;
            if (ptype is "课间" or "活动")
            {
                // BreakName ← name ?? type
                var bname = p["name"]?.GetValue<string>() ?? "";
                rows.Add(new ScheduleRow("", "", start, end, 0, isCurrent, true, bname.Length > 0 ? bname : ptype));
                continue;
            }
            classCounter++;
            var patch = patches.FirstOrDefault(pp => pp.Index == i);
            var subject = patch is not null && !patch.NeedsCheck ? patch.Subject : course.GetCell(i, dayKey);
            var mark = patch is null ? null : patch.NeedsCheck ? ScheduleMark.Check : ScheduleMark.Patch;
            rows.Add(new ScheduleRow(subject, teacherOf(subject), start, end, classCounter, isCurrent, false, "", mark));
        }
        return rows;
    }

    /// <summary>整周课表 键 monday..sunday</summary>
    public static Dictionary<string, List<ScheduleRow>> GetWeekSchedule()
    {
        var result = new Dictionary<string, List<ScheduleRow>>();
        if (Config.ProfileSource.Value is "classisland" or "classwidgets" && !UseLocalScheduleData())
        {
            try
            {
                var bridge = EnsureBridge();
                if (bridge is LinkageBridge ci)
                {
                    // CI 键 Mon=1..Sun=7
                    var week = ci.GetWeekSchedule();
                    for (var wd = 1; wd <= 7; wd++)
                    {
                        result[DayKey(wd)] = week.GetValueOrDefault(wd) ?? [];
                    }
                    return result;
                }
                if (bridge is ClassWidgetsBridge cw)
                {
                    // CW 键 Mon=0..Sun=6
                    var week = cw.GetWeekSchedule();
                    for (var wd = 1; wd <= 7; wd++)
                    {
                        result[DayKey(wd)] = week.GetValueOrDefault(wd - 1) ?? [];
                    }
                    return result;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[课表组件] 周课表联动读取失败: {e.Message}");
            }
        }
        try
        {
            var (master, timetable) = LoadActiveMasterTimetable();
            // 学科档补教师名
            var teachers = new Dictionary<string, string>();
            try
            {
                var subject = SubjectDoc.LoadOrDefault(master.Subject);
                foreach (var r in subject.Rows)
                {
                    var name = r["name"]?.GetValue<string>() ?? "";
                    var teacher = r["teacher"]?.GetValue<string>() ?? "";
                    if (name.Length > 0 && teacher.Length > 0)
                    {
                        teachers[name] = teacher;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Debug($"[课表组件] 学科教师读取失败: {e.Message}");
            }
            var now = AppUtils.PreciseNow();
            var nowTime = TimeOnly.FromDateTime(now);
            var todayKey = DayKey(((int)now.DayOfWeek + 6) % 7 + 1);
            var mondayDate = DateOnly.FromDateTime(now).AddDays(-(((int)now.DayOfWeek + 6) % 7));
            for (var wd = 1; wd <= 7; wd++)
            {
                var dayKey = DayKey(wd);
                var dayDate = mondayDate.AddDays(wd - 1);
                result[dayKey] = BuildDayRows(master, timetable, dayDate, dayKey, dayKey == todayKey, nowTime,
                    s => teachers.GetValueOrDefault(s, ""));
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[课表组件] 周课表本地读取失败: {e.Message}");
        }
        for (var wd = 1; wd <= 7; wd++)
        {
            result.TryAdd(DayKey(wd), []);
        }
        return result;
    }

    private static string DayKey(int pythonWeekday) => pythonWeekday switch
    {
        1 => "monday",
        2 => "tuesday",
        3 => "wednesday",
        4 => "thursday",
        5 => "friday",
        6 => "saturday",
        _ => "sunday",
    };

    /// <summary>周日首列展示列序</summary>
    public static readonly string[] DisplayDayOrder =
        ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

    /// <summary>按 key 找提供者 兼容裸 id</summary>
    private static (string PluginId, string Name, Func<List<ScheduleRow>> Provider)? ResolvePluginProvider(string key)
    {
        if (PluginService.ScheduleProviders.TryGetValue(key, out var direct))
        {
            return direct;
        }
        foreach (var kv in PluginService.ScheduleProviders)
        {
            if (string.Equals(kv.Value.PluginId, key, StringComparison.Ordinal))
            {
                return kv.Value;
            }
        }
        return null;
    }

    private static bool UseLocalScheduleData() => Config.ProfileSource.Value switch
    {
        "classisland" => !Config.ScheduleProfileCiFollow.Value,
        "classwidgets" => !Config.ScheduleProfileCwFollow.Value,
        _ => true,
    };

    /// <summary>当前课表来源对应的活动总档案配置项</summary>
    private static ConfigItem<string> ActiveScheduleItem() => Config.ProfileSource.Value switch
    {
        "classisland" => Config.ScheduleProfileCi,
        "classwidgets" => Config.ScheduleProfileCw,
        _ => Config.ScheduleProfile,
    };

    /// <summary>联动桥复用实例 Start 自带重入保护 路径每次同步</summary>
    private static LinkageBridgeBase? EnsureBridge()
    {
        LinkageBridgeBase? bridge;
        string path;
        if (Config.ProfileSource.Value == "classisland")
        {
            bridge = CiBridge;
            path = Config.LinkageDataPath.Value;
        }
        else if (Config.ProfileSource.Value == "classwidgets")
        {
            bridge = CwBridge;
            path = Config.ClassWidgetsDataPath.Value;
        }
        else
        {
            return null;
        }
        bridge.SetDataPath(path);
        if (!_bridgeStarted)
        {
            bridge.Start();
            _bridgeStarted = true;
        }
        return bridge;
    }

    /// <summary>活动总档案+时间表 读取失败返回空档</summary>
    private static (ScheduleProfile Master, TimetableDoc Timetable) LoadActiveMasterTimetable()
    {
        try
        {
            ScheduleStore.EnsureDefaults();
            var master = ScheduleStore.LoadMaster(ActiveScheduleItem().Value);
            // 过期覆盖回收
            if (master.CleanExpiredDateRules(AppUtils.PreciseNow()))
            {
                master.Save();
                Log.Info("[课表组件] 已清理过期日期覆盖");
            }
            return (master, TimetableDoc.LoadOrDefault(master.Timetable));
        }
        catch (Exception e)
        {
            Log.Warning($"[课表组件] 总档案读取失败: {e.Message}");
            return (new ScheduleProfile(), new TimetableDoc());
        }
    }

    /// <summary>当日课表 覆盖→轮换→回落</summary>
    private static (CourseDoc Course, ScheduleRules.DayOverride? WholeDay) ResolveDayCourse(ScheduleProfile master, DateOnly date)
    {
        var ov = ScheduleRules.ResolveOverride(master, date);
        if (ov is not null && ov.Kind != "plan")
        {
            // exam/holiday 占位 课程档回落仅作占位
            var fallback = ScheduleRules.ResolveCourse(master, date) ?? "";
            return (CourseDoc.LoadOrDefault(fallback), ov);
        }
        var courseName = (ov is not null && ov.CourseName.Length > 0 ? ov.CourseName : null)
                         ?? ScheduleRules.ResolveCourse(master, date)
                         ?? "";
        return (CourseDoc.LoadOrDefault(courseName), null);
    }

    /// <summary>整日占位行 sessions 优先</summary>
    private static List<ScheduleRow> WholeDayRows(ScheduleRules.DayOverride hit, bool isToday, TimeOnly nowTime)
    {
        var rows = new List<ScheduleRow>();
        if (hit.Kind == "exam" && hit.Entry["sessions"] is JsonArray sessions && sessions.Count > 0)
        {
            foreach (var s in sessions.OfType<JsonObject>())
            {
                var start = s["start"]?.GetValue<string>() ?? "";
                var end = s["end"]?.GetValue<string>() ?? "";
                var name = s["name"]?.GetValue<string>() ?? "";
                var cur = isToday && TimeOnly.TryParse(start, out var st) && TimeOnly.TryParse(end, out var et) &&
                          st <= nowTime && nowTime < et;
                rows.Add(new ScheduleRow("", "", start, end, 0, cur, true, name.Length > 0 ? name : hit.Name, ScheduleMark.Exam));
            }
            return rows;
        }
        // 整日占位 00:00~24:00
        rows.Add(new ScheduleRow("", "", "00:00", "24:00", 0, isToday, true, hit.Name, hit.Kind));
        return rows;
    }

    // 派生值 TTL 缓存 防每帧读盘
    private static readonly Dictionary<string, (object? Value, DateTime At)> DerivedCache = [];

    /// <summary>派生值缓存 5s 过期</summary>
    private static T? Cached<T>(string key, Func<T?> factory)
    {
        if (DerivedCache.TryGetValue(key, out var hit) && (AppUtils.PreciseNow() - hit.At).TotalSeconds < 5)
        {
            return (T?)hit.Value;
        }
        var value = factory();
        DerivedCache[key] = (value, AppUtils.PreciseNow());
        return value;
    }

    /// <summary>当前周次 + 奇偶</summary>
    public static (int Week, string? Parity)? GetWeekInfo() => Cached<(int Week, string? Parity)?>("week", () =>
    {
        try
        {
            ScheduleStore.EnsureDefaults();
            var master = ScheduleStore.LoadMaster(ActiveScheduleItem().Value);
            var today = DateOnly.FromDateTime(AppUtils.PreciseNow());
            var week = ScheduleRules.GetWeekNumber(today, master.WeekStart);
            return week is null ? null : (week.Value, ScheduleRules.WeekParityLabel(master, today));
        }
        catch (Exception e)
        {
            Log.Debug($"[课表组件] 周次读取失败: {e.Message}");
            return null;
        }
    });

    /// <summary>周次标签</summary>
    public static string? GetWeekLabel() => GetWeekInfo() is { } w
        ? AppUtils.Tr("schedule.rotation_week_chip", ("n", w.Week)) + (string.IsNullOrEmpty(w.Parity) ? "" : $" · {w.Parity}")
        : null;

    /// <summary>档案预备分钟 null=未配</summary>
    public static int? GetProfilePrepareMinutes() => Cached<int?>("prepare", () =>
    {
        try
        {
            ScheduleStore.EnsureDefaults();
            var master = ScheduleStore.LoadMaster(ActiveScheduleItem().Value);
            return master.PrepareMinutes > 0 ? master.PrepareMinutes : null;
        }
        catch (Exception e)
        {
            Log.Debug($"[课表组件] 预备分钟读取失败: {e.Message}");
            return null;
        }
    });

    /// <summary>科目色 hex 弱耦合</summary>
    public static string? GetSubjectColor(string subject)
    {
        if (subject.Length == 0)
        {
            return null;
        }
        return Cached($"color:{subject}", () =>
        {
            try
            {
                ScheduleStore.EnsureDefaults();
                var master = ScheduleStore.LoadMaster(ActiveScheduleItem().Value);
                var doc = SubjectDoc.LoadOrDefault(master.Subject);
                foreach (var r in doc.Rows)
                {
                    if ((r["name"]?.GetValue<string>() ?? "") == subject)
                    {
                        var color = r["color"]?.GetValue<string>() ?? "";
                        return color.Length > 0 ? color : null;
                    }
                }
                return null;
            }
            catch (Exception e)
            {
                Log.Debug($"[课表组件] 科目颜色读取失败: {e.Message}");
                return null;
            }
        });
    }

    // 时间轴节点
    public static List<Dictionary<string, object?>> BuildTimelineNodes()
    {
        var nodes = new List<Dictionary<string, object?>>();
        foreach (var row in GetTodaySchedule())
        {
            if (row.IsBreak)
            {
                if (nodes.Count > 0)
                {
                    nodes[^1]["break"] = new Dictionary<string, object?> { ["start"] = row.StartTime, ["end"] = row.EndTime };
                }
                continue;
            }
            // 连堂合并 仅渲染层
            if (nodes.Count > 0 &&
                row.Mark is null &&
                nodes[^1]["name"] is string prevName && prevName == row.Subject)
            {
                nodes[^1]["end"] = row.EndTime;
                continue;
            }
            nodes.Add(new Dictionary<string, object?>
            {
                ["name"] = row.Subject.Length > 0 ? row.Subject : "—",
                ["start"] = row.StartTime,
                ["end"] = row.EndTime,
                ["period"] = row.Index > 0 ? $"第{row.Index}节" : "—",
                ["break"] = null,
            });
        }
        return nodes;
    }
}
