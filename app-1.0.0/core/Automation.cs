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

// 自动化系统
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Glimpseon.Core;

// 模型

/// <summary>触发器/动作条目</summary>
public sealed class AutomationEntry
{
    public string Id { get; set; } = "";
    public JsonObject? Settings { get; set; }

    public AutomationEntry Clone() => new() { Id = Id, Settings = Settings is null ? null : JsonNode.Parse(Settings.ToJsonString()) as JsonObject };
}

/// <summary>条件树节点</summary>
public sealed class AutomationCondition
{
    public string Kind { get; set; } = "and";
    public bool Reversed { get; set; }
    public string Id { get; set; } = "";
    public JsonObject? Settings { get; set; }
    public List<AutomationCondition> Children { get; set; } = [];

    public bool Evaluate()
    {
        var result = Kind switch
        {
            "and" => Children.Count == 0 || Children.All(c => c.Evaluate()),
            "or" => Children.Any(c => c.Evaluate()),
            "not" => Children.Count == 0 || !Children[0].Evaluate(),
            "leaf" => AutomationRegistry.GetCondition(Id) is { } info && SafeEval(info, Settings),
            _ => true,
        };
        return Reversed ? !result : result;
    }

    private static bool SafeEval(ConditionInfo info, JsonObject? settings)
    {
        try
        {
            return info.Evaluate(settings);
        }
        catch (Exception e)
        {
            Log.Warning($"[自动化] 条件 {info.Id} 求值失败: {e.Message}");
            return false;
        }
    }

    public JsonObject ToJson()
    {
        var obj = new JsonObject
        {
            ["kind"] = Kind,
            ["reversed"] = Reversed,
        };
        if (Kind == "leaf")
        {
            obj["id"] = Id;
            if (Settings is { } s)
            {
                obj["settings"] = JsonNode.Parse(s.ToJsonString());
            }
        }
        else
        {
            var arr = new JsonArray();
            foreach (var c in Children)
            {
                arr.Add(c.ToJson());
            }
            obj["children"] = arr;
        }
        return obj;
    }

    public static AutomationCondition? FromJson(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }
        var c = new AutomationCondition
        {
            Kind = obj["kind"]?.GetValue<string>() ?? "and",
            Reversed = obj["reversed"]?.GetValue<bool>() ?? false,
            Id = obj["id"]?.GetValue<string>() ?? "",
        };
        if (obj["settings"] is JsonObject s)
        {
            c.Settings = s;
        }
        if (obj["children"] is JsonArray arr)
        {
            foreach (var child in arr)
            {
                if (FromJson(child) is { } parsed)
                {
                    c.Children.Add(parsed);
                }
            }
        }
        return c;
    }

    public AutomationCondition Clone() => FromJson(ToJson()) ?? new AutomationCondition();
}

/// <summary>自动化策略</summary>
public sealed class AutomationRecipe
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<AutomationEntry> Triggers { get; set; } = [];
    public AutomationCondition? Condition { get; set; }
    public List<AutomationEntry> Actions { get; set; } = [];

    public JsonObject ToJson()
    {
        var obj = new JsonObject
        {
            ["id"] = Id,
            ["name"] = Name,
            ["enabled"] = Enabled,
        };
        obj["triggers"] = EntriesToJson(Triggers);
        obj["condition"] = Condition?.ToJson();
        obj["actions"] = EntriesToJson(Actions);
        return obj;
    }

    private static JsonArray EntriesToJson(List<AutomationEntry> entries)
    {
        var arr = new JsonArray();
        foreach (var e in entries)
        {
            var o = new JsonObject { ["id"] = e.Id };
            if (e.Settings is { } s)
            {
                o["settings"] = JsonNode.Parse(s.ToJsonString());
            }
            arr.Add(o);
        }
        return arr;
    }

    public static AutomationRecipe FromJson(JsonObject obj)
    {
        var r = new AutomationRecipe
        {
            Id = obj["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N")[..12],
            Name = obj["name"]?.GetValue<string>() ?? "",
            Enabled = obj["enabled"]?.GetValue<bool>() ?? true,
        };
        r.Triggers = EntriesFromJson(obj["triggers"] as JsonArray);
        r.Condition = AutomationCondition.FromJson(obj["condition"]);
        r.Actions = EntriesFromJson(obj["actions"] as JsonArray);
        return r;
    }

    private static List<AutomationEntry> EntriesFromJson(JsonArray? arr)
    {
        var list = new List<AutomationEntry>();
        if (arr is null)
        {
            return list;
        }
        foreach (var node in arr)
        {
            if (node is not JsonObject o)
            {
                continue;
            }
            var e = new AutomationEntry { Id = o["id"]?.GetValue<string>() ?? "" };
            if (o["settings"] is JsonObject s)
            {
                e.Settings = s;
            }
            if (e.Id.Length > 0)
            {
                list.Add(e);
            }
        }
        return list;
    }
}

/// <summary>运行记录 环形缓冲</summary>
public sealed record AutomationRunLog(DateTime Time, string RecipeName, string Source, bool Ok, string Detail, long Ms);

// 注册表

/// <summary>触发器定义</summary>
public sealed record TriggerInfo(string Id, string Glyph, string NameKey,
    Func<JsonObject?, IAutomationTrigger> Create, Func<JsonObject?, string> Summarize, JsonObject? Defaults = null);

/// <summary>条件定义 纯谓词无生命周期</summary>
public sealed record ConditionInfo(string Id, string Glyph, string NameKey,
    Func<JsonObject?, bool> Evaluate, Func<JsonObject?, string> Summarize, JsonObject? Defaults = null);

/// <summary>动作定义</summary>
public sealed record ActionInfo(string Id, string Glyph, string NameKey,
    Func<JsonObject?, Task> Run, Func<JsonObject?, string> Summarize, JsonObject? Defaults = null);

/// <summary>提供者注册表 插件可扩展</summary>
public static class AutomationRegistry
{
    private static readonly List<TriggerInfo> Triggers = [];
    private static readonly List<ConditionInfo> Conditions = [];
    private static readonly List<ActionInfo> Actions = [];
    private static bool _builtinsLoaded;

    public static IReadOnlyList<TriggerInfo> TriggerInfos => Triggers;
    public static IReadOnlyList<ConditionInfo> ConditionInfos => Conditions;
    public static IReadOnlyList<ActionInfo> ActionInfos => Actions;

    public static TriggerInfo? GetTrigger(string id) => Triggers.FirstOrDefault(t => t.Id == id);
    public static ConditionInfo? GetCondition(string id) => Conditions.FirstOrDefault(c => c.Id == id);
    public static ActionInfo? GetAction(string id) => Actions.FirstOrDefault(a => a.Id == id);

    public static void Register(TriggerInfo info)
    {
        Triggers.RemoveAll(t => t.Id == info.Id);
        Triggers.Add(info);
    }

    public static void Register(ConditionInfo info)
    {
        Conditions.RemoveAll(c => c.Id == info.Id);
        Conditions.Add(info);
    }

    public static void Register(ActionInfo info)
    {
        Actions.RemoveAll(a => a.Id == info.Id);
        Actions.Add(info);
    }

    // 内置
    internal static void EnsureBuiltins()
    {
        if (_builtinsLoaded)
        {
            return;
        }
        _builtinsLoaded = true;
        RegisterBuiltins();
    }

    private static void RegisterBuiltins()
    {
        // 触发器
        Register(new TriggerInfo("time_at", "E823", "automation.trigger_time_at",
            s => new TimeAtTrigger(s),
            s => $"{s?.TryGet("time") ?? "08:00"} {SummarizeDays(s)}",
            new JsonObject { ["time"] = "08:00", ["days"] = new JsonArray(1, 2, 3, 4, 5) }));
        Register(new TriggerInfo("interval", "E916", "automation.trigger_interval",
            s => new IntervalTrigger(s),
            s => string.Format(AppUtils.Tr("automation.sum_interval"), s?.TryGet("minutes") ?? "30"),
            new JsonObject { ["minutes"] = 30 }));
        Register(new TriggerInfo("startup", "E7C8", "automation.trigger_startup",
            s => new StartupTrigger(s),
            s => string.Format(AppUtils.Tr("automation.sum_startup"), s?.TryGet("delay") ?? "10"),
            new JsonObject { ["delay"] = 10 }));
        Register(new TriggerInfo("lesson_start", "E7E8", "automation.trigger_lesson_start",
            s => new LessonStartTrigger(s),
            s => SummarizeLessonIndex(s),
            new JsonObject { ["index"] = 0 }));
        Register(new TriggerInfo("lesson_end", "E112", "automation.trigger_lesson_end",
            s => new LessonEndTrigger(s),
            s => SummarizeLessonIndex(s),
            new JsonObject { ["index"] = 0 }));
        Register(new TriggerInfo("pre_lesson", "EA92", "automation.trigger_pre_lesson",
            s => new PreLessonTrigger(s),
            s => string.Format(AppUtils.Tr("automation.sum_pre_lesson"), s?.TryGet("minutes") ?? "2") + " " + SummarizeLessonIndex(s),
            new JsonObject { ["minutes"] = 2, ["index"] = 0 }));
        Register(new TriggerInfo("idle", "E916", "automation.trigger_idle",
            s => new IdleTrigger(s),
            s => string.Format(AppUtils.Tr("automation.sum_idle"), s?.TryGet("minutes") ?? "10"),
            new JsonObject { ["minutes"] = 10 }));
        Register(new TriggerInfo("network", "E968", "automation.trigger_network",
            s => new NetworkTrigger(s),
            s => (s?.TryGet("state") ?? "up") == "down" ? AppUtils.Tr("automation.state_down") : AppUtils.Tr("automation.state_up"),
            new JsonObject { ["state"] = "up" }));

        // 条件
        Register(new ConditionInfo("time_range", "E823", "automation.cond_time_range",
            s => EvalTimeRange(s),
            s => $"{s?.TryGet("start") ?? "08:00"} ~ {s?.TryGet("end") ?? "17:00"}",
            new JsonObject { ["start"] = "08:00", ["end"] = "17:00" }));
        Register(new ConditionInfo("weekday", "E787", "automation.cond_weekday",
            s => EvalWeekday(s),
            s => SummarizeDays(s),
            new JsonObject { ["days"] = new JsonArray(1, 2, 3, 4, 5) }));
        Register(new ConditionInfo("date_range", "E787", "automation.cond_date_range",
            s => EvalDateRange(s),
            s => $"{s?.TryGet("start") ?? "09-01"} ~ {s?.TryGet("end") ?? "01-20"}",
            new JsonObject { ["start"] = "09-01", ["end"] = "01-20" }));
        Register(new ConditionInfo("in_class", "E7E8", "automation.cond_in_class",
            _ => AutomationService.CurrentLesson is not null,
            _ => "", new JsonObject()));
        Register(new ConditionInfo("in_break", "E1CD", "automation.cond_in_break",
            _ => AutomationService.CurrentBreakRow is not null,
            _ => "", new JsonObject()));
        Register(new ConditionInfo("subject_now", "E82D", "automation.cond_subject_now",
            s => AutomationService.CurrentLesson?.Subject?.Contains((string?)s?.TryGet("subject") ?? "") == true,
            s => (string?)s?.TryGet("subject") ?? "",
            new JsonObject { ["subject"] = "" }));
        Register(new ConditionInfo("network_state", "E968", "automation.cond_network_state",
            s => AutomationService.IsNetworkUp == (((string?)s?.TryGet("state") ?? "up") == "up"),
            s => ((string?)s?.TryGet("state")) == "down" ? AppUtils.Tr("automation.state_down") : AppUtils.Tr("automation.state_up"),
            new JsonObject { ["state"] = "up" }));

        // 动作
        Register(new ActionInfo("notification", "E789", "automation.action_notification",
            s => DoNotification(s),
            s => $"{(string?)s?.TryGet("content") ?? ""}",
            new JsonObject { ["content"] = "", ["type"] = "corner", ["duration"] = 5, ["speak"] = false, ["font_size"] = 24 }));
        Register(new ActionInfo("speak", "E720", "automation.action_speak",
            s => DoSpeak(s),
            s => (string?)s?.TryGet("text") ?? "",
            new JsonObject { ["text"] = "", ["voice"] = "zh-CN-XiaoxiaoNeural", ["rate"] = 100 }));
        Register(new ActionInfo("open", "E8A7", "automation.action_open",
            s => DoOpen(s),
            s => (string?)s?.TryGet("target") ?? "",
            new JsonObject { ["target"] = "" }));
        Register(new ActionInfo("builtin", "E71D", "automation.action_builtin",
            s => DoBuiltin(s),
            s => (string?)s?.TryGet("key") ?? "",
            new JsonObject { ["key"] = "schedule" }));
        Register(new ActionInfo("volume", "E767", "automation.action_volume",
            s => DoVolume(s),
            s => string.Format(AppUtils.Tr("automation.sum_volume"), s?.TryGet("level") ?? "50"),
            new JsonObject { ["level"] = 50 }));
        Register(new ActionInfo("show_main", "E80F", "automation.action_show_main",
            s => DoShowMain(s),
            s => (string?)s?.TryGet("page") is { Length: > 0 } p ? p : AppUtils.Tr("automation.sum_page_home"),
            new JsonObject { ["page"] = "" }));
        Register(new ActionInfo("minimize_main", "E921", "automation.action_minimize_main",
            _ => DoMinimizeMain(),
            _ => "", new JsonObject()));
        Register(new ActionInfo("recipe", "E931", "automation.action_recipe",
            s => DoRecipe(s),
            s => (string?)s?.TryGet("id") ?? "",
            new JsonObject { ["id"] = "" }));
        Register(new ActionInfo("log", "E946", "automation.action_log",
            s => DoLog(s),
            s => (string?)s?.TryGet("text") ?? "",
            new JsonObject { ["text"] = "" }));
    }

    // 摘要工具
    /// <summary>设置取串</summary>
    internal static string? TryGet(this JsonObject? obj, string key)
    {
        if (obj?[key] is { } node)
        {
            try
            {
                return node.GetValue<string>();
            }
            catch
            {
                return node.ToJsonString().Trim('"');
            }
        }
        return null;
    }

    /// <summary>设置取整 失败回落</summary>
    internal static int ReadInt(JsonObject? s, string key, int fallback)
    {
        try
        {
            return s?[key]?.GetValue<int>() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string SummarizeDays(JsonObject? s)
    {
        var days = ParseDays(s);
        if (days.Count == 0 || days.Count == 7)
        {
            return AppUtils.Tr("automation.every_day");
        }
        var keys = new[] { "", "automation.day_1", "automation.day_2", "automation.day_3", "automation.day_4", "automation.day_5", "automation.day_6", "automation.day_7" };
        return string.Join(" ", days.OrderBy(d => d).Select(d => AppUtils.Tr(keys[d])));
    }

    /// <summary>星期集合</summary>
    internal static HashSet<int> ParseDays(JsonObject? s)
    {
        var set = new HashSet<int>();
        if (s?["days"] is JsonArray arr)
        {
            foreach (var n in arr)
            {
                try
                {
                    if (n?.GetValue<int>() is int v and >= 1 and <= 7)
                    {
                        set.Add(v);
                    }
                }
                catch
                {
                    // 忽略
                }
            }
        }
        return set;
    }

    private static string SummarizeLessonIndex(JsonObject? s)
    {
        var index = ReadInt(s, "index", 0);
        return index <= 0 ? AppUtils.Tr("automation.lesson_any") : string.Format(AppUtils.Tr("automation.lesson_no"), index);
    }

    // 条件求值
    private static bool EvalTimeRange(JsonObject? s)
    {
        var now = TimeOnly.FromDateTime(AppUtils.PreciseNow());
        if (!TimeOnly.TryParse((string?)s?.TryGet("start"), out var start))
        {
            return true;
        }
        if (!TimeOnly.TryParse((string?)s?.TryGet("end"), out var end))
        {
            return true;
        }
        return start <= end ? now >= start && now <= end : now >= start || now <= end;
    }

    private static bool EvalWeekday(JsonObject? s)
    {
        var days = ParseDays(s);
        if (days.Count == 0)
        {
            return true;
        }
        var dow = (int)AppUtils.PreciseNow().DayOfWeek;
        if (dow == 0)
        {
            dow = 7;
        }
        return days.Contains(dow);
    }

    private static bool EvalDateRange(JsonObject? s)
    {
        var today = AppUtils.PreciseNow().ToString("MM-dd");
        var start = (string?)s?.TryGet("start") ?? "";
        var end = (string?)s?.TryGet("end") ?? "";
        if (start.Length == 0 || end.Length == 0)
        {
            return true;
        }
        return string.CompareOrdinal(today, start) >= 0 && string.CompareOrdinal(today, end) <= 0;
    }

    // 动作执行
    private static Task DoNotification(JsonObject? s)
    {
        var content = (string?)s?.TryGet("content") ?? "";
        if (content.Length == 0)
        {
            return Task.CompletedTask;
        }
        var type = (string?)s?.TryGet("type") ?? NotifType.Corner;
        var speak = (string?)s?.TryGet("speak") == "true";
        int.TryParse((string?)s?.TryGet("duration"), out var duration);
        int.TryParse((string?)s?.TryGet("font_size"), out var fontSize);
        new NotificationManager().HandleNotification(new NotificationRequest
        {
            Type = type is NotifType.Scroll or NotifType.Fullscreen ? type : NotifType.Corner,
            Content = content,
            Duration = duration is > 0 and <= 120 ? duration : 5,
            FontSize = fontSize is > 8 and <= 96 ? fontSize : 24,
            TtsVoice = speak ? "zh-CN-XiaoxiaoNeural" : "done",
        });
        return Task.CompletedTask;
    }

    private static Task DoSpeak(JsonObject? s)
    {
        var text = (string?)s?.TryGet("text") ?? "";
        if (text.Length == 0)
        {
            return Task.CompletedTask;
        }
        int.TryParse((string?)s?.TryGet("rate"), out var rate);
        new NotificationManager().SpeakOnly(text, (string?)s?.TryGet("voice") ?? "zh-CN-XiaoxiaoNeural", rate);
        return Task.CompletedTask;
    }

    private static Task DoOpen(JsonObject? s)
    {
        var target = (string?)s?.TryGet("target") ?? "";
        if (target.Length == 0)
        {
            return Task.CompletedTask;
        }
        Process.Start(new ProcessStartInfo(target.Trim()) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private static Task DoBuiltin(JsonObject? s)
    {
        var key = (string?)s?.TryGet("key") ?? "";
        CustomActions.FindBuiltin(key)?.Run();
        return Task.CompletedTask;
    }

    private static Task DoVolume(JsonObject? s)
    {
        int.TryParse((string?)s?.TryGet("level"), out var level);
        Audio.SetVolume(Math.Clamp(level, 0, 100) / 100.0);
        return Task.CompletedTask;
    }

    private static Task DoShowMain(JsonObject? s)
    {
        var page = (string?)s?.TryGet("page") ?? "";
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            { MainWindow: UI.MainWindow main })
        {
            main.RestoreFromTray();
            switch (page)
            {
                case "wallpaper":
                    main.Nav.SelectedItem = main.NavItemWallpaper;
                    break;
                case "notification":
                    main.Nav.SelectedItem = main.NavItemNotification;
                    break;
                case "automation":
                    main.Nav.SelectedItem = main.NavItemAutomation;
                    break;
                default:
                    main.SwitchToHome();
                    break;
            }
        }
        return Task.CompletedTask;
    }

    private static Task DoMinimizeMain()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            { MainWindow: UI.MainWindow main })
        {
            main.MinimizeToDesktop();
        }
        return Task.CompletedTask;
    }

    private static async Task DoRecipe(JsonObject? s)
    {
        var id = (string?)s?.TryGet("id") ?? "";
        if (id.Length > 0)
        {
            await AutomationService.InvokeRecipeById(id, "automation.source_chained");
        }
    }

    private static Task DoLog(JsonObject? s)
    {
        Log.Info($"[自动化] {s?.TryGet("text") ?? ""}");
        return Task.CompletedTask;
    }
}

// 触发器

/// <summary>触发器实例 Tick 驱动</summary>
public interface IAutomationTrigger : IDisposable
{
    event Action? Fired;
    void Start();
    void Tick(DateTime now);
}

/// <summary>每日指定时刻 星期过滤</summary>
public sealed class TimeAtTrigger : IAutomationTrigger
{
    private readonly TimeOnly _time;
    private readonly HashSet<int> _days;
    private DateOnly _lastFire;

    public event Action? Fired;

    public TimeAtTrigger(JsonObject? s)
    {
        var raw = AutomationRegistry.TryGet(s, "time") ?? "";
        _time = TimeOnly.TryParse(raw, out var t) ? t : new TimeOnly(8, 0);
        _days = AutomationRegistry.ParseDays(s);
    }

    public void Start() { }

    public void Tick(DateTime now)
    {
        if (_days.Count > 0 && !_days.Contains(DayIndex(now)))
        {
            return;
        }
        var t = TimeOnly.FromDateTime(now);
        var d = DateOnly.FromDateTime(now);
        if (t.Hour == _time.Hour && t.Minute == _time.Minute && _lastFire != d)
        {
            _lastFire = d;
            Fired?.Invoke();
        }
    }

    public void Dispose() { }

    internal static int DayIndex(DateTime now) => (int)now.DayOfWeek == 0 ? 7 : (int)now.DayOfWeek;
}

/// <summary>固定间隔触发</summary>
public sealed class IntervalTrigger : IAutomationTrigger
{
    private readonly int _minutes;
    private DateTime _next;

    public event Action? Fired;

    public IntervalTrigger(JsonObject? s) => _minutes = Math.Clamp(AutomationRegistry.ReadInt(s, "minutes", 30), 1, 1440);

    public void Start() => _next = AppUtils.PreciseNow().AddMinutes(_minutes);

    public void Tick(DateTime now)
    {
        if (now >= _next)
        {
            _next = now.AddMinutes(_minutes);
            Fired?.Invoke();
        }
    }

    public void Dispose() { }
}

/// <summary>启动后延时触发一次</summary>
public sealed class StartupTrigger : IAutomationTrigger
{
    private readonly int _delay;
    private DateTime _fireAt;
    private bool _done;

    public event Action? Fired;

    public StartupTrigger(JsonObject? s) => _delay = Math.Clamp(AutomationRegistry.ReadInt(s, "delay", 10), 0, 600);

    public void Start() => _fireAt = AppUtils.PreciseNow().AddSeconds(_delay);

    public void Tick(DateTime now)
    {
        if (!_done && now >= _fireAt)
        {
            _done = true;
            Fired?.Invoke();
        }
    }

    public void Dispose() { }
}

/// <summary>上课开始 边沿检测 0=任意节</summary>
public sealed class LessonStartTrigger : IAutomationTrigger
{
    private readonly int _index;
    private string? _lastSig;

    public event Action? Fired;

    public LessonStartTrigger(JsonObject? s) => _index = AutomationRegistry.ReadInt(s, "index", 0);

    public void Start() => _lastSig = AutomationService.CurrentLesson?.Signature();

    public void Tick(DateTime now)
    {
        var cur = AutomationService.CurrentLesson;
        var sig = cur?.Signature();
        if (cur is not null && sig != _lastSig && (_index <= 0 || cur.Index == _index))
        {
            Fired?.Invoke();
        }
        _lastSig = sig;
    }

    public void Dispose() { }
}

/// <summary>下课 边沿检测 含连堂直换 0=任意节</summary>
public sealed class LessonEndTrigger : IAutomationTrigger
{
    private readonly int _index;
    private ScheduleRow? _last;

    public event Action? Fired;

    public LessonEndTrigger(JsonObject? s) => _index = AutomationRegistry.ReadInt(s, "index", 0);

    public void Start() => _last = AutomationService.CurrentLesson;

    public void Tick(DateTime now)
    {
        var cur = AutomationService.CurrentLesson;
        if (_last is not null && _last.Signature() != cur?.Signature()
            && (_index <= 0 || _last.Index == _index))
        {
            Fired?.Invoke();
        }
        _last = cur;
    }

    public void Dispose() { }
}

/// <summary>课前预备 预备窗口内每节一次 0=任意节</summary>
public sealed class PreLessonTrigger : IAutomationTrigger
{
    private readonly int _minutes;
    private readonly int _index;
    private string? _lastSig;

    public event Action? Fired;

    public PreLessonTrigger(JsonObject? s)
    {
        _minutes = Math.Clamp(AutomationRegistry.ReadInt(s, "minutes", 2), 1, 30);
        _index = AutomationRegistry.ReadInt(s, "index", 0);
    }

    public void Start() => _lastSig = AutomationService.NextLesson?.Signature();

    public void Tick(DateTime now)
    {
        var next = AutomationService.NextLesson;
        var sig = next?.Signature();
        if (next is not null && sig != _lastSig
            && TimeOnly.TryParse(next.StartTime, out var start))
        {
            var delta = start - TimeOnly.FromDateTime(now);
            // 预备窗口
            if (delta > TimeSpan.Zero && delta <= TimeSpan.FromMinutes(_minutes)
                && (_index <= 0 || next.Index == _index))
            {
                Fired?.Invoke();
            }
        }
        _lastSig = sig;
    }

    public void Dispose() { }
}

/// <summary>系统空闲 边沿触发</summary>
public sealed class IdleTrigger : IAutomationTrigger
{
    private readonly int _minutes;
    private bool _wasIdle;

    public event Action? Fired;

    public IdleTrigger(JsonObject? s) => _minutes = Math.Clamp(AutomationRegistry.ReadInt(s, "minutes", 10), 1, 240);

    public void Start() { }

    public void Tick(DateTime now)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var idleMs = Win32.Native.GetIdleMilliseconds();
        if (idleMs < 0)
        {
            return;
        }
        var isIdle = idleMs >= _minutes * 60_000L;
        if (isIdle && !_wasIdle)
        {
            Fired?.Invoke();
        }
        _wasIdle = isIdle;
    }

    public void Dispose() { }
}

/// <summary>网络可用性变化 事件驱动</summary>
public sealed class NetworkTrigger : IAutomationTrigger
{
    private readonly bool _wantUp;
    private bool? _lastState;

    public event Action? Fired;

    public NetworkTrigger(JsonObject? s) => _wantUp = (string?)s?["state"] != "down";

    public void Start()
    {
        NetworkChange.NetworkAvailabilityChanged += OnChanged;
    }

    public void Tick(DateTime now) { }

    private void OnChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        var up = e.IsAvailable;
        if (_lastState == up)
        {
            return;
        }
        _lastState = up;
        if (up == _wantUp)
        {
            Dispatcher.UIThread.Post(() => Fired?.Invoke());
        }
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnChanged;
    }
}

// 服务

public static class AutomationService
{
    private const string FileVersion = "1";
    private static readonly object SaveLock = new();
    private static readonly List<AutomationRecipe> _recipes = [];
    private static readonly List<(AutomationRecipe Recipe, IAutomationTrigger Trigger)> _instances = [];
    private static readonly HashSet<string> _running = new();
    private static readonly Queue<AutomationRunLog> _logs = new();

    private static DispatcherTimer? _timer;
    private static ScheduleRow? _nextLesson;
    private static DateTime _rowsAt = DateTime.MinValue;
    private static bool _initialized;
    private static bool _lastEnabled = true;

    /// <summary>策略列表变化 UI 订阅刷新</summary>
    public static event Action? RecipesChanged;

    /// <summary>运行记录变化 UI 订阅刷新</summary>
    public static event Action? LogsChanged;

    public static bool IsEnabled => Config.AutomationEnabled.Value;
    public static IReadOnlyList<AutomationRecipe> Recipes => _recipes;
    public static IReadOnlyCollection<AutomationRunLog> Logs => _logs;

    /// <summary>当前节 课表快照</summary>
    public static ScheduleRow? CurrentLesson { get; private set; }
    public static ScheduleRow? CurrentBreakRow { get; private set; }
    public static ScheduleRow? NextLesson => _nextLesson;

    public static bool IsNetworkUp => NetworkInterface.GetIsNetworkAvailable();

    private static string ConfigPath => Path.Combine(Paths.DataConfig, "automation.json");

    // 启动 UI 线程调用
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        AutomationRegistry.EnsureBuiltins();
        Load();
        Rebuild();
        Config.AutomationEnabled.ValueChanged += _ => Log.Info($"[自动化] 总开关 {IsEnabled}");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => SafeTick();
        _timer.Start();
        Log.Info($"[自动化] 就绪 策略 {_recipes.Count}");
    }

    // 策略管理
    public static void Add(AutomationRecipe recipe)
    {
        _recipes.Add(recipe);
        Attach(recipe);
        Save();
        RecipesChanged?.Invoke();
    }

    public static void Update(AutomationRecipe recipe)
    {
        Detach(recipe);
        if (recipe.Enabled && IsEnabled)
        {
            Attach(recipe);
        }
        Save();
        RecipesChanged?.Invoke();
    }

    public static void Remove(AutomationRecipe recipe)
    {
        Detach(recipe);
        _recipes.Remove(recipe);
        Save();
        RecipesChanged?.Invoke();
    }

    public static AutomationRecipe? Find(string id) => _recipes.FirstOrDefault(r => r.Id == id);

    // 手动运行 跳过条件
    public static Task RunNow(AutomationRecipe recipe) => InvokeRecipe(recipe, "automation.source_manual", manual: true, depth: 0);

    internal static Task InvokeRecipeById(string id, string source)
    {
        var recipe = Find(id);
        return recipe is null ? Task.CompletedTask : InvokeRecipe(recipe, source, manual: false, depth: 1);
    }

    private static async Task InvokeRecipe(AutomationRecipe recipe, string source, bool manual, int depth)
    {
        if (depth > 3)
        {
            Log.Warning($"[自动化] 策略链深度超限 {recipe.Name}");
            return;
        }
        if (!manual)
        {
            if (!IsEnabled || !recipe.Enabled)
            {
                return;
            }
            if (recipe.Condition is { } cond && !cond.Evaluate())
            {
                Log.Debug($"[自动化] {recipe.Name} 条件不满足");
                return;
            }
        }
        if (_running.Contains(recipe.Id))
        {
            Log.Debug($"[自动化] {recipe.Name} 上一次动作尚未结束 跳过");
            return;
        }

        _running.Add(recipe.Id);
        var sw = Stopwatch.StartNew();
        var ok = true;
        var detail = "";
        try
        {
            foreach (var entry in recipe.Actions)
            {
                var info = AutomationRegistry.GetAction(entry.Id);
                if (info is null)
                {
                    Log.Warning($"[自动化] 未知动作 {entry.Id}");
                    ok = false;
                    detail = $"未知动作 {entry.Id}";
                    continue;
                }
                try
                {
                    await info.Run(entry.Settings);
                }
                catch (Exception e)
                {
                    ok = false;
                    detail = $"{info.Id}: {e.Message}";
                    Log.Error($"[自动化] 动作 {info.Id} 执行失败: {e.Message}");
                }
            }
        }
        finally
        {
            _running.Remove(recipe.Id);
            PushLog(new AutomationRunLog(DateTime.Now, recipe.Name, source, ok, detail, sw.ElapsedMilliseconds));
        }
    }

    private static void PushLog(AutomationRunLog entry)
    {
        _logs.Enqueue(entry);
        while (_logs.Count > 100)
        {
            _logs.Dequeue();
        }
        LogsChanged?.Invoke();
    }

    // 秒级驱动
    private static void SafeTick()
    {
        try
        {
            Tick();
        }
        catch (Exception e)
        {
            Log.Error($"[自动化] Tick 异常 {e.GetType().Name}: {e.Message}");
        }
    }

    private static void Tick()
    {
        var now = AppUtils.PreciseNow();
        RefreshSnapshot(now);
        if (!IsEnabled)
        {
            _lastEnabled = false;
            return;
        }
        if (!_lastEnabled)
        {
            // 重开重置触发器状态 防 interval 过期连发
            _lastEnabled = true;
            Rebuild();
        }
        foreach (var (_, trigger) in _instances)
        {
            try
            {
                trigger.Tick(now);
            }
            catch (Exception e)
            {
                Log.Warning($"[自动化] 触发器 Tick 异常: {e.Message}");
            }
        }
    }

    private static void RefreshSnapshot(DateTime now)
    {
        if ((now - _rowsAt).TotalSeconds < 5)
        {
            return;
        }
        try
        {
            var rows = UI.TimetableScheduleProvider.GetTodaySchedule();
            _rowsAt = now;
            var nowTime = TimeOnly.FromDateTime(now);
            CurrentLesson = rows.FirstOrDefault(r => r.IsCurrent && !r.IsBreak);
            CurrentBreakRow = rows.FirstOrDefault(r => r.IsCurrent && r.IsBreak);
            ScheduleRow? next = null;
            TimeSpan? best = null;
            foreach (var r in rows)
            {
                if (r.IsBreak || r.IsCurrent)
                {
                    continue;
                }
                if (TimeOnly.TryParse(r.StartTime, out var st))
                {
                    var delta = st - nowTime;
                    if (delta > TimeSpan.Zero && (best is null || delta < best))
                    {
                        best = delta;
                        next = r;
                    }
                }
            }
            _nextLesson = next;
        }
        catch (Exception e)
        {
            // 失败保留旧快照
            _rowsAt = now;
            Log.Warning($"[自动化] 课表快照读取失败: {e.Message}");
        }
    }

    // 实例挂载
    private static void Attach(AutomationRecipe recipe)
    {
        foreach (var entry in recipe.Triggers)
        {
            var info = AutomationRegistry.GetTrigger(entry.Id);
            if (info is null)
            {
                Log.Warning($"[自动化] 未知触发器 {entry.Id}");
                continue;
            }
            try
            {
                var trigger = info.Create(entry.Settings);
                trigger.Fired += () => Dispatcher.UIThread.Post(() =>
                    _ = InvokeRecipe(recipe, "automation.source_trigger", manual: false, depth: 0));
                trigger.Start();
                _instances.Add((recipe, trigger));
            }
            catch (Exception e)
            {
                Log.Error($"[自动化] 触发器 {entry.Id} 挂载失败: {e.Message}");
            }
        }
    }

    private static void Detach(AutomationRecipe recipe)
    {
        for (var i = _instances.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_instances[i].Recipe, recipe))
            {
                try
                {
                    _instances[i].Trigger.Dispose();
                }
                catch
                {
                    // 忽略
                }
                _instances.RemoveAt(i);
            }
        }
    }

    private static void Rebuild()
    {
        foreach (var (_, trigger) in _instances)
        {
            try
            {
                trigger.Dispose();
            }
            catch
            {
                // 忽略
            }
        }
        _instances.Clear();
        foreach (var recipe in _recipes)
        {
            if (recipe.Enabled)
            {
                Attach(recipe);
            }
        }
    }

    // 持久化
    public static void Save()
    {
        lock (SaveLock)
        {
            try
            {
                var root = new JsonObject
                {
                    ["version"] = FileVersion,
                };
                var arr = new JsonArray();
                foreach (var r in _recipes)
                {
                    arr.Add(r.ToJson());
                }
                root["recipes"] = arr;
                Directory.CreateDirectory(Paths.DataConfig);
                File.WriteAllText(ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e)
            {
                Log.Error($"[自动化] 配置写入失败: {e.Message}");
            }
        }
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                CreateSample();
                Save();
                return;
            }
            var root = JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject;
            if (root?["recipes"] is not JsonArray arr)
            {
                return;
            }
            foreach (var node in arr)
            {
                if (node is JsonObject obj)
                {
                    _recipes.Add(AutomationRecipe.FromJson(obj));
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"[自动化] 配置读取失败: {e.Message}");
        }
    }

    private static void CreateSample()
    {
        _recipes.Add(new AutomationRecipe
        {
            Name = AppUtils.Tr("automation.sample_name"),
            Enabled = false,
            Triggers = [new AutomationEntry { Id = "time_at", Settings = new JsonObject { ["time"] = "08:00", ["days"] = new JsonArray(1, 2, 3, 4, 5) } }],
            Condition = null,
            Actions = [new AutomationEntry { Id = "notification", Settings = new JsonObject { ["content"] = AppUtils.Tr("automation.sample_content"), ["type"] = "corner", ["duration"] = 5, ["speak"] = false, ["font_size"] = 24 } }],
        });
    }
}

// 快照扩展
public static class AutomationExtensions
{
    /// <summary>节次签名 防重触发</summary>
    public static string Signature(this ScheduleRow row) => $"{row.Subject}|{row.StartTime}|{row.Index}";
}
