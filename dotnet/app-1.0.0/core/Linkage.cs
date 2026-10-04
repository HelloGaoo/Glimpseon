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

// 联动模块

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Glimpseon.Core;

public enum TimeState
{
    None = 0,
    PrepareOnClass = 1,
    OnClass = 2,
    Breaking = 3,
    AfterSchool = 4,
}

public static class TimeStateExtensions
{
    public static string DisplayName(this TimeState state) => state switch
    {
        TimeState.None => "今天没有课程",
        TimeState.PrepareOnClass => "准备上课",
        TimeState.OnClass => "上课中",
        TimeState.Breaking => "课间休息",
        TimeState.AfterSchool => "放学",
        _ => state.ToString(),
    };
}

public sealed class LessonInfo
{
    public string SubjectName { get; set; } = "";
    public string TeacherName { get; set; } = "";
    public string Initial { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public int Index { get; set; } = -1;

    public static LessonInfo FromSubjectData(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } obj)
        {
            return new LessonInfo();
        }
        return new LessonInfo
        {
            SubjectName = obj.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
            TeacherName = obj.TryGetProperty("TeacherName", out var t) ? t.GetString() ?? "" : "",
            Initial = obj.TryGetProperty("Initial", out var i) ? i.GetString() ?? "" : "",
        };
    }
}

public sealed class LinkageState
{
    public TimeState TimeState { get; set; } = TimeState.None;
    public string CurrentSubject { get; set; } = "";
    public LessonInfo? CurrentLesson { get; set; }
    public LessonInfo? NextLesson { get; set; }
    public bool IsConnected { get; set; }
    public DateTime? LastUpdate { get; set; }
    public string OnClassLeft { get; set; } = "";
    public string OnBreakingLeft { get; set; } = "";
    public int CurrentIndex { get; set; } = -1;
    public bool IsClassPlanLoaded { get; set; }

    public LinkageState Clone() => new()
    {
        TimeState = TimeState,
        CurrentSubject = CurrentSubject,
        CurrentLesson = CurrentLesson is null ? null : new LessonInfo
        {
            SubjectName = CurrentLesson.SubjectName,
            TeacherName = CurrentLesson.TeacherName,
            Initial = CurrentLesson.Initial,
            StartTime = CurrentLesson.StartTime,
            EndTime = CurrentLesson.EndTime,
            Index = CurrentLesson.Index,
        },
        NextLesson = NextLesson is null ? null : new LessonInfo
        {
            SubjectName = NextLesson.SubjectName,
            TeacherName = NextLesson.TeacherName,
            Initial = NextLesson.Initial,
            StartTime = NextLesson.StartTime,
            EndTime = NextLesson.EndTime,
            Index = NextLesson.Index,
        },
        IsConnected = IsConnected,
        LastUpdate = LastUpdate,
        OnClassLeft = OnClassLeft,
        OnBreakingLeft = OnBreakingLeft,
        CurrentIndex = CurrentIndex,
        IsClassPlanLoaded = IsClassPlanLoaded,
    };
}

// 今日课表行 (科目, 教师, 开始, 结束, 序号, 是否当前, 是否课间, 课间名) 
public sealed record ScheduleRow(string Subject, string Teacher, string StartTime, string EndTime, int Index, bool IsCurrent, bool IsBreak, string BreakName);

internal sealed record CiTimeSlot(TimeOnly StartTime, TimeOnly EndTime, int TimeType, string BreakName);

internal sealed record CiDayPlan(int WeekDay, string Name, List<string> ClassIds, string LayoutId);

internal sealed record CwTimeSlot(TimeOnly StartTime, TimeOnly EndTime, string Subject, string Teacher, int Index, bool IsBreak);

// 联动桥基类

public abstract class LinkageBridgeBase : IDisposable
{
    protected readonly string Tag;
    private readonly object _lock = new();
    private Thread? _thread;
    private LinkageState _state = new();
    private TimeState _prevState = TimeState.None;
    protected int _consecutiveFailures;
    private bool _redetectFailLogged;
    private volatile bool _running;

    public int PollIntervalSeconds { get; set; } = 5;
    public bool IsRunning => _running;
    protected string DataDir = "";

    public event Action<LinkageState>? StateChanged;
    public event Action<bool>? ConnectedChanged;
    public event Action<string>? ErrorOccurred;

    protected abstract string FindData();

    protected LinkageBridgeBase(string tag)
    {
        Tag = tag;
        Log.Debug($"[{Tag}] 联动桥初始化");
    }

    public void SetDataPath(string path)
    {
        var p = path.Trim();
        if (p.Length > 0 && p != DataDir)
        {
            Log.Info($"[{Tag}] 数据路径设置: {p}");
        }
        DataDir = p;
    }

    public string AutoDetect()
    {
        var path = FindData();
        if (path.Length > 0)
        {
            SetDataPath(path);
            Log.Debug($"[{Tag}] 自动检测命中 {path}");
        }
        else
        {
            Log.Debug($"[{Tag}] 自动检测未找到数据目录");
        }
        return path;
    }

    public void Start()
    {
        if (_running)
        {
            Log.Debug($"[{Tag}] 已在运行");
            return;
        }
        _consecutiveFailures = 0;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = $"{Tag}-poll" };
        _thread.Start();
        Log.Info($"[{Tag}] 启动 (路径: {(DataDir.Length > 0 ? DataDir : "未设置")})");
    }

    public void Stop()
    {
        _running = false;
        if (_thread is not null)
        {
            if (!_thread.Join(3000))
            {
                Log.Warning($"[{Tag}] 后台线程未在 3 秒内退出");
            }
            _thread = null;
        }
        Log.Info($"[{Tag}] 停止");
    }

    public LinkageState GetState()
    {
        lock (_lock)
        {
            return _state.Clone();
        }
    }

    private void Loop()
    {
        while (_running)
        {
            try
            {
                if (DataDir.Length == 0)
                {
                    AutoDetectSilent();
                }
                BeforeCompute();
                var st = ComputeState();
                Commit(st);
            }
            catch (Exception e)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures == 1)
                {
                    Log.Warning($"[{Tag}] 循环异常: {e.Message}");
                }
                else
                {
                    Log.Debug($"[{Tag}] 循环异常({_consecutiveFailures}次): {e.Message}");
                }
            }
            Thread.Sleep(TimeSpan.FromSeconds(PollIntervalSeconds));
        }
    }

    protected virtual void BeforeCompute()
    {
    }

    protected abstract LinkageState ComputeState();

    protected abstract void SaveDetectedPath(string path);

    protected void AutoDetectSilent()
    {
        var path = FindData();
        if (path.Length > 0)
        {
            SetDataPath(path);
            _consecutiveFailures = 0;
            _redetectFailLogged = false;
            try
            {
                SaveDetectedPath(path);
            }
            catch (Exception e)
            {
                Log.Warning($"[{Tag}] 检测路径保存失败: {e.Message}");
            }
            Log.Info($"[{Tag}] 数据目录 {path}");
        }
        else if (!_redetectFailLogged)
        {
            _redetectFailLogged = true;
            Log.Warning($"[{Tag}] 无匹配数据目录");
        }
    }

    protected void TryRedetect()
    {
        var newPath = FindData();
        if (newPath.Length > 0 && newPath != DataDir)
        {
            Log.Info($"[{Tag}] 路径变更: {newPath}");
            _redetectFailLogged = false;
            SetDataPath(newPath);
            _consecutiveFailures = 0;
            try
            {
                SaveDetectedPath(newPath);
            }
            catch (Exception e)
            {
                Log.Debug($"[{Tag}] 重检测路径保存失败: {e.Message}");
            }
            ErrorOccurred?.Invoke($"REDIRECT:{newPath}");
        }
        else if (newPath.Length == 0 && !_redetectFailLogged)
        {
            _redetectFailLogged = true;
            Log.Debug($"[{Tag}] 重检测未发现新路径 (当前: {(DataDir.Length > 0 ? DataDir : "未设置")})");
        }
    }

    protected bool Commit(LinkageState newState)
    {
        bool connectedChanged;
        bool changed;
        TimeState oldTs;
        lock (_lock)
        {
            var old = _state;
            oldTs = _prevState;
            _state = newState;
            connectedChanged = old.IsConnected != newState.IsConnected;
            changed = newState.TimeState != oldTs;
            if (changed)
            {
                _prevState = newState.TimeState;
            }
        }
        StateChanged?.Invoke(newState);
        if (connectedChanged)
        {
            ConnectedChanged?.Invoke(newState.IsConnected);
            Log.Info($"[{Tag}] 连接状态变化: {(newState.IsConnected ? "已连接" : "已断开")}");
        }
        if (changed)
        {
            Log.Info($"[{Tag}] {oldTs.DisplayName()} -> {newState.TimeState.DisplayName()}");
        }
        return changed;
    }

    protected void CountFailure() => _consecutiveFailures++;

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

// 进程查找

public static class LinkageLocator
{
    public static string? FindExeByProcessNames(params string[] processNames)
    {
        foreach (var name in processNames)
        {
            var bare = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
            foreach (var proc in Process.GetProcessesByName(bare))
            {
                try
                {
                    var exe = proc.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exe))
                    {
                        Log.Debug($"进程候选命中: {name} -> {exe}");
                        return exe;
                    }
                }
                catch (Exception e)
                {

                    Glimpseon.Core.Log.Warning($"[LINKAGE] 自动检测跳过(访问被拒): {e.Message}");
                    // 访问被拒等 跳过
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        return null;
    }

    public static string FindClassIslandData()
    {
        const string profileFile = @"Profiles\Default.json";
        var exePath = FindExeByProcessNames("ClassIsland.Desktop.exe", "ClassIsland.exe");
        if (exePath is not null)
        {
            var baseDir = Path.GetDirectoryName(exePath)!;
            for (var i = 0; i < 3; i++)
            {
                foreach (var candidate in new[] { baseDir, Path.Combine(baseDir, "data") })
                {
                    if (File.Exists(Path.Combine(candidate, profileFile)))
                    {
                        Log.Info($"从进程发现 ClassIsland: {candidate}");
                        return candidate;
                    }
                }
                var parent = Path.GetDirectoryName(baseDir);
                if (parent is null || parent == baseDir)
                {
                    break;
                }
                baseDir = parent;
            }
        }
        const string fallback = @"C:\ClassIsland2\data";
        if (File.Exists(Path.Combine(fallback, profileFile)))
        {
            Log.Debug($"用 ClassIsland 默认路径: {fallback}");
            return fallback;
        }
        return "";
    }

    public static string FindClassWidgetsData()
    {
        var exePath = FindExeByProcessNames("ClassWidgets.exe");
        if (exePath is not null)
        {
            var exeDir = Path.GetDirectoryName(exePath)!;
            if (File.Exists(Path.Combine(exeDir, "config", "config.ini")))
            {
                Log.Info($"[CW-Linkage] 从进程路径发现 ClassWidgets: {Path.Combine(exeDir, "config")}");
                return Path.Combine(exeDir, "config");
            }
        }
        const string fixedExe = @"C:\ClassWidgets\ClassWidgets.exe";
        if (File.Exists(fixedExe))
        {
            var exeDir = Path.GetDirectoryName(fixedExe)!;
            if (File.Exists(Path.Combine(exeDir, "config", "config.ini")))
            {
                Log.Info(@"[CW-Linkage] 从 C:\ClassWidgets 发现 ClassWidgets");
                return Path.Combine(exeDir, "config");
            }
            Log.Debug(@"[CW-Linkage] 固定路径缺config.ini");
        }
        return "";
    }
}

// ClassIsland 配置桥接

public sealed class LinkageBridge : LinkageBridgeBase
{
    private const string ProfileFile = @"Profiles\Default.json";
    private const string SettingsFile = "Settings.json";

    private readonly object _lock = new();
    private JsonElement _cachedRaw;
    private bool _hasCachedRaw;
    private DateTime _cachedMtime;
    private DateTime _settingsMtime;
    private JsonElement? _settingsCached;
    private List<CiTimeSlot> _slots = [];
    private Dictionary<int, CiDayPlan> _dayPlans = [];
    private Dictionary<string, JsonElement> _subjects = [];

    private bool _settingsMissingLogged;
    private bool _noPlanLogged;
    private bool _noSlotLogged;
    private bool _classIdxOobLogged;
    private bool _schedTruncLogged;
    private bool _schedTruncWdLogged;
    private bool _lessonBadDataLogged;

    public LinkageBridge() : base("Linkage")
    {
    }

    protected override string FindData() => LinkageLocator.FindClassIslandData();

    protected override void SaveDetectedPath(string path) => Config.LinkageDataPath.Value = path;

    public new void SetDataPath(string path)
    {
        base.SetDataPath(path);
        ClearCache();
    }

    protected override void BeforeCompute() => SyncTimeConfig();

    // 返回今日课表
    public List<ScheduleRow> GetTodaySchedule()
    {
        lock (_lock)
        {
            if (_slots.Count == 0 || _dayPlans.Count == 0)
            {
                Log.Debug("[Linkage] 今日课表未加载");
                return [];
            }
            var now = AppUtils.PreciseNow();
            var t = TimeOnly.FromDateTime(now);
            var dotnetWd = PythonWeekdayToDotnet((int)now.DayOfWeek == 0 ? 7 : (int)now.DayOfWeek);
            if (!_dayPlans.TryGetValue(dotnetWd, out var plan))
            {
                Log.Debug($"[Linkage] 今日(周{dotnetWd}) 无课程安排");
                return [];
            }
            var currentState = GetState();
            var currentIdx = currentState.CurrentIndex;
            var isBreaking = currentState.TimeState == TimeState.Breaking;
            var result = new List<ScheduleRow>();
            var classCounter = 0;
            foreach (var slot in _slots)
            {
                if (slot.TimeType == 0)
                {
                    classCounter++;
                    var ci = classCounter - 1;
                    if (ci >= plan.ClassIds.Count)
                    {
                        if (!_schedTruncLogged)
                        {
                            _schedTruncLogged = true;
                            Log.Debug($"[Linkage] 课程序号超出当日计划 (第{classCounter}节) 截断今日课表");
                        }
                        break;
                    }
                    var sid = plan.ClassIds[ci];
                    var subj = _subjects.TryGetValue(sid, out var s) ? s : default;
                    result.Add(new ScheduleRow(
                        subj.ValueKind == JsonValueKind.Object && subj.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                        subj.ValueKind == JsonValueKind.Object && subj.TryGetProperty("TeacherName", out var tn) ? tn.GetString() ?? "" : "",
                        slot.StartTime.ToString("HH:mm"),
                        slot.EndTime.ToString("HH:mm"),
                        classCounter,
                        classCounter == currentIdx && !isBreaking,
                        false, ""));
                }
                else
                {
                    var inThisBreak = isBreaking && slot.StartTime <= t && t < slot.EndTime;
                    result.Add(new ScheduleRow("", "",
                        slot.StartTime.ToString("HH:mm"),
                        slot.EndTime.ToString("HH:mm"),
                        0, inThisBreak, true,
                        slot.BreakName.Length > 0 ? slot.BreakName : "课间"));
                }
            }
            Log.Debug($"[Linkage] 今日课表{result.Count}条 (周{dotnetWd})");
            return result;
        }
    }

    // 取指定日的课表
    public List<ScheduleRow> GetScheduleByWeekday(int dotnetWeekday)
    {
        lock (_lock)
        {
            if (_slots.Count == 0 || _dayPlans.Count == 0)
            {
                Log.Debug($"[Linkage] 周{dotnetWeekday}课表未加载");
                return [];
            }
            if (!_dayPlans.TryGetValue(dotnetWeekday, out var plan))
            {
                Log.Debug($"[Linkage] 周{dotnetWeekday} 无课程安排");
                return [];
            }
            var result = new List<ScheduleRow>();
            var classCounter = 0;
            foreach (var slot in _slots)
            {
                if (slot.TimeType == 0)
                {
                    classCounter++;
                    var ci = classCounter - 1;
                    if (ci >= plan.ClassIds.Count)
                    {
                        if (!_schedTruncWdLogged)
                        {
                            _schedTruncWdLogged = true;
                            Log.Debug($"[Linkage] 课程序号超出周{dotnetWeekday}计划 (第{classCounter}节) 截断课表");
                        }
                        break;
                    }
                    var sid = plan.ClassIds[ci];
                    var subj = _subjects.TryGetValue(sid, out var s) ? s : default;
                    result.Add(new ScheduleRow(
                        subj.ValueKind == JsonValueKind.Object && subj.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                        subj.ValueKind == JsonValueKind.Object && subj.TryGetProperty("TeacherName", out var tn) ? tn.GetString() ?? "" : "",
                        slot.StartTime.ToString("HH:mm"),
                        slot.EndTime.ToString("HH:mm"),
                        classCounter, false, false, ""));
                }
                else
                {
                    result.Add(new ScheduleRow("", "",
                        slot.StartTime.ToString("HH:mm"),
                        slot.EndTime.ToString("HH:mm"),
                        0, false, true,
                        slot.BreakName.Length > 0 ? slot.BreakName : "课间"));
                }
            }
            Log.Debug($"[Linkage] 周{dotnetWeekday} 课表{result.Count}条");
            return result;
        }
    }

    // 返回一周课表 键 Mon=1..Sun=7 
    public Dictionary<int, List<ScheduleRow>> GetWeekSchedule()
    {
        var result = new Dictionary<int, List<ScheduleRow>>();
        for (var pyWd = 1; pyWd <= 7; pyWd++)
        {
            result[pyWd] = GetScheduleByWeekday(PythonWeekdayToDotnet(pyWd));
        }
        Log.Debug($"[Linkage] 一周课表{result.Values.Sum(v => v.Count)}条");
        return result;
    }

    internal static int PythonWeekdayToDotnet(int pythonWeekday) => pythonWeekday == 7 ? 0 : pythonWeekday;

    private void ClearCache()
    {
        lock (_lock)
        {
            _hasCachedRaw = false;
            _cachedMtime = DateTime.MinValue;
            _slots = [];
            _dayPlans = [];
            _subjects = [];
        }
        Log.Debug("[Linkage] 课表缓存已清空");
    }

    private bool LoadFileIfChanged()
    {
        if (DataDir.Length == 0)
        {
            return false;
        }
        var profile = Path.Combine(DataDir, ProfileFile);
        try
        {
            var mtime = File.GetLastWriteTime(profile);
            if (mtime <= _cachedMtime && _hasCachedRaw)
            {
                return true;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(profile));
            var raw = doc.RootElement.Clone();
            _cachedRaw = raw;
            _hasCachedRaw = true;
            _cachedMtime = mtime;
            ParseAll(raw);
            _consecutiveFailures = 0;
            Log.Info($"[Linkage] 已加载课表文件 ({mtime:HH:mm:ss})");
            return true;
        }
        catch (FileNotFoundException)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures == 1)
            {
                Log.Warning($"[Linkage] 课表文件不存在: {profile}");
            }
            if (_consecutiveFailures >= 2)
            {
                TryRedetect();
            }
            return false;
        }
        catch (Exception e)
        {
            _consecutiveFailures++;
            Log.Warning($"[Linkage] 读取文件失败: {e.Message}");
            if (_consecutiveFailures >= 3)
            {
                TryRedetect();
            }
            return false;
        }
    }

    private void ParseAll(JsonElement raw)
    {
        var subjects = raw.TryGetProperty("Subjects", out var sub) && sub.ValueKind == JsonValueKind.Object ? sub : default;
        var layouts = raw.TryGetProperty("TimeLayouts", out var lay) && lay.ValueKind == JsonValueKind.Object ? lay : default;
        if (subjects.ValueKind == JsonValueKind.Undefined)
        {
            Log.Warning("[Linkage] 课表缺Subjects");
        }
        if (layouts.ValueKind == JsonValueKind.Undefined)
        {
            Log.Warning("[Linkage] 课表缺TimeLayouts");
        }

        var slots = new List<CiTimeSlot>();
        if (layouts.ValueKind == JsonValueKind.Object)
        {
            string? firstId = null;
            foreach (var p in layouts.EnumerateObject())
            {
                firstId = p.Name;
                break;
            }
            if (firstId is not null && layouts.GetProperty(firstId).TryGetProperty("Layouts", out var layoutArr) &&
                layoutArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in layoutArr.EnumerateArray())
                {
                    var s = TimeFromStr(item.TryGetProperty("StartTime", out var st) ? st.GetString() : null);
                    var e = TimeFromStr(item.TryGetProperty("EndTime", out var et) ? et.GetString() : null);
                    if (s.HasValue && e.HasValue)
                    {
                        slots.Add(new CiTimeSlot(s.Value, e.Value,
                            item.TryGetProperty("TimeType", out var tt) ? tt.GetInt32() : 1,
                            item.TryGetProperty("BreakName", out var bn) ? bn.GetString() ?? "" : ""));
                    }
                }
            }
        }

        var dayPlans = new Dictionary<int, CiDayPlan>();
        if (raw.TryGetProperty("ClassPlans", out var plans) && plans.ValueKind == JsonValueKind.Object)
        {
            foreach (var planProp in plans.EnumerateObject())
            {
                var plan = planProp.Value;
                var timeRule = plan.TryGetProperty("TimeRule", out var tr) && tr.ValueKind == JsonValueKind.Object ? tr : default;
                var wd = timeRule.ValueKind == JsonValueKind.Object && timeRule.TryGetProperty("WeekDay", out var wdEl) ? wdEl.GetInt32() : 0;
                var classes = new List<string>();
                if (plan.TryGetProperty("Classes", out var cls) && cls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in cls.EnumerateArray())
                    {
                        var enabled = !c.TryGetProperty("IsEnabled", out var en) || en.ValueKind == JsonValueKind.True;
                        if (enabled && c.TryGetProperty("SubjectId", out var sid))
                        {
                            classes.Add(sid.GetString() ?? "");
                        }
                    }
                }
                dayPlans[wd] = new CiDayPlan(wd,
                    plan.TryGetProperty("Name", out var pn) ? pn.GetString() ?? "" : "",
                    classes,
                    plan.TryGetProperty("TimeLayoutId", out var tl) ? tl.GetString() ?? "" : "");
            }
        }
        if (dayPlans.Count == 0)
        {
            Log.Warning("[Linkage] 课表缺ClassPlans");
        }

        var subjMap = new Dictionary<string, JsonElement>();
        if (subjects.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in subjects.EnumerateObject())
            {
                subjMap[p.Name] = p.Value.Clone();
            }
        }

        lock (_lock)
        {
            _subjects = subjMap;
            _slots = slots;
            _dayPlans = dayPlans;
        }
        Log.Info($"[Linkage] 课表已解析 {slots.Count}时段 {subjMap.Count}科目 {dayPlans.Count}天");
    }

    // 同步 ClassIsland 时间配置
    private void SyncTimeConfig()
    {
        if (!Config.LinkageSyncTimeConfig.Value || DataDir.Length == 0)
        {
            return;
        }
        var settingsPath = Path.Combine(DataDir, SettingsFile);
        try
        {
            var mtime = File.GetLastWriteTime(settingsPath);
            if (mtime <= _settingsMtime && _settingsCached.HasValue)
            {
                return;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            var raw = doc.RootElement.Clone();
            _settingsMtime = mtime;
            _settingsCached = raw;
            var synced = new List<string>();
            if (raw.TryGetProperty("TimeOffsetSeconds", out var off))
            {
                Config.TimeOffset.Value = off.GetInt32();
                synced.Add("TimeOffsetSeconds");
            }
            if (raw.TryGetProperty("IsTimeAutoAdjustEnabled", out var auto))
            {
                Config.AutoTimeOffsetEnabled.Value = auto.GetBoolean();
                synced.Add("IsTimeAutoAdjustEnabled");
            }
            if (raw.TryGetProperty("TimeAutoAdjustSeconds", out var inc))
            {
                Config.AutoTimeOffsetIncrement.Value = inc.GetInt32();
                synced.Add("TimeAutoAdjustSeconds");
            }
            if (synced.Count > 0)
            {
                Log.Info($"[Linkage] 已同步 ClassIsland 时间配置: {string.Join(", ", synced)}");
            }
            else
            {
                Log.Debug("[Linkage] Settings.json 无可同步的时间键");
            }
        }
        catch (FileNotFoundException)
        {
            if (!_settingsMissingLogged)
            {
                _settingsMissingLogged = true;
                Log.Debug($"[Linkage] Settings.json 不存在: {settingsPath}");
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[Linkage] 同步时间配置失败 ({settingsPath}): {e.Message}");
        }
    }

    protected override LinkageState ComputeState()
    {
        var now = AppUtils.PreciseNow();
        var today = now.Date;
        var weekday = (int)now.DayOfWeek == 0 ? 7 : (int)now.DayOfWeek;
        var dotnetWd = PythonWeekdayToDotnet((int)now.DayOfWeek == 0 ? 7 : (int)now.DayOfWeek);
        var t = TimeOnly.FromDateTime(now);

        if (!LoadFileIfChanged())
        {
            return new LinkageState { IsConnected = false };
        }

        List<CiTimeSlot> slots;
        Dictionary<int, CiDayPlan> dayPlans;
        Dictionary<string, JsonElement> subjects;
        lock (_lock)
        {
            slots = _slots;
            dayPlans = _dayPlans;
            subjects = _subjects;
        }

        var st = new LinkageState { IsConnected = true, LastUpdate = now };
        var (slotIdx, slot) = FindSlot(slots, t);
        if (!dayPlans.TryGetValue(dotnetWd, out var plan))
        {
            st.TimeState = TimeState.None;
            if (!_noPlanLogged)
            {
                _noPlanLogged = true;
                Log.Debug($"[Linkage] 周{dotnetWd} 无课程计划");
            }
            return st;
        }
        if (slot is null)
        {
            st.TimeState = slots.Count > 0 && t >= slots[^1].EndTime ? TimeState.AfterSchool : TimeState.None;
            if (!_noSlotLogged)
            {
                _noSlotLogged = true;
                Log.Debug($"[Linkage] 当前时间不在任何时段内 ({t})");
            }
            return st;
        }
        if (slot.TimeType == 0)
        {
            st.TimeState = TimeState.OnClass;
        }
        else
        {
            st.TimeState = TimeState.Breaking;
            st.CurrentSubject = slot.BreakName;
        }
        var classIndex = SlotToClassIndex(slots, slotIdx);
        if (classIndex >= 0 && classIndex < plan.ClassIds.Count)
        {
            var sid = plan.ClassIds[classIndex];
            subjects.TryGetValue(sid, out var subjEl);
            var subj = subjEl.ValueKind == JsonValueKind.Object ? subjEl : (JsonElement?)null;
            if (slot.TimeType == 0)
            {
                st.CurrentSubject = subj.HasValue && subj.Value.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
            }
            var lesson = LessonInfo.FromSubjectData(subj);
            lesson.StartTime = slot.StartTime.ToString("HH:mm");
            lesson.EndTime = slot.EndTime.ToString("HH:mm");
            lesson.Index = classIndex + 1;
            st.CurrentLesson = lesson;
            st.CurrentIndex = classIndex + 1;
        }
        else if (!_classIdxOobLogged)
        {
            _classIdxOobLogged = true;
            Log.Warning($"[Linkage] 课程序号 {classIndex + 1} 超出当日计划 ({plan.ClassIds.Count}节) 当前课程信息缺失");
        }

        var nextIdx = SlotToClassIndex(slots, slotIdx, offset: 1);
        if (nextIdx >= 0 && nextIdx < plan.ClassIds.Count)
        {
            var nextSid = plan.ClassIds[nextIdx];
            subjects.TryGetValue(nextSid, out var nextSubj);
            st.NextLesson = LessonInfo.FromSubjectData(nextSubj.ValueKind == JsonValueKind.Object ? nextSubj : (JsonElement?)null);
        }

        var left = today.Add(slot.EndTime.ToTimeSpan()) - now;
        if (slot.TimeType == 0)
        {
            st.OnClassLeft = FmtDelta(left);
        }
        else
        {
            st.OnBreakingLeft = FmtDelta(left);
        }
        Log.Debug($"[Linkage] {st.TimeState.DisplayName()}|{(st.CurrentSubject.Length > 0 ? st.CurrentSubject : "-")}|" +
                  $"{slot.StartTime:HH:mm}-{slot.EndTime:HH:mm}|下节:{(st.NextLesson is { } nl ? nl.SubjectName : "-")}|" +
                  $"{plan.Name}(周{weekday}) 第{slotIdx + 1}/{slots.Count}段|剩余{FmtDelta(left)}");
        return st;
    }

    private static (int, CiTimeSlot?) FindSlot(List<CiTimeSlot> slots, TimeOnly t)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i].StartTime <= t && t < slots[i].EndTime)
            {
                return (i, slots[i]);
            }
        }
        return (-1, null);
    }

    private static int SlotToClassIndex(List<CiTimeSlot> slots, int slotIdx, int offset = 0)
    {
        var nClasses = 0;
        for (var i = 0; i <= slotIdx && i < slots.Count; i++)
        {
            if (slots[i].TimeType == 0)
            {
                nClasses++;
            }
        }
        return Math.Max(0, nClasses - 1 + offset);
    }

    internal static TimeOnly? TimeFromStr(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }
        var parts = s.Trim().Split(':');
        try
        {
            var h = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var m = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
            var sec = parts.Length > 2 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
            return new TimeOnly(h, m, sec);
        }
        catch (Exception e)
        {
            Log.Debug($"时间字符串解析失败: '{s}' ({e.Message})");
            return null;
        }
    }

    internal static string FmtDelta(TimeSpan td)
    {
        var totalSec = Math.Max(0, (int)td.TotalSeconds);
        var h = totalSec / 3600;
        var m = totalSec % 3600 / 60;
        var s = totalSec % 60;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
    }
}

// ClassWidgets 配置桥接

public sealed class ClassWidgetsBridge : LinkageBridgeBase
{
    private readonly object _lock = new();
    private string _cwCacheFile = "";
    private DateTime _cwCacheMtime = DateTime.MinValue;
    private JsonElement? _cwCacheData;
    private bool _hasCacheData;
    private string? _lastResolved;
    private string? _lastReadFail;
    private (DateTime, string, int)? _lastParseSig;
    private bool _weekTypeErrLogged;
    private bool _noStartDatedLogged;
    private bool _fpMissingLogged;
    private bool _badUnitLogged;
    private bool _cfgFailLogged;
    private bool _cfgNameMissingLogged;
    private bool _noSlotsLogged;
    private bool _breakLeftErrLogged;
    private bool _cwFixedMissLogged;

    public ClassWidgetsBridge() : base("CW-Linkage")
    {
    }

    protected override string FindData()
    {
        var exePath = LinkageLocator.FindExeByProcessNames("ClassWidgets.exe");
        if (exePath is not null)
        {
            var exeDir = Path.GetDirectoryName(exePath)!;
            if (File.Exists(Path.Combine(exeDir, "config", "config.ini")))
            {
                Log.Info($"[CW-Linkage] 从进程路径发现 ClassWidgets: {Path.Combine(exeDir, "config")}");
                return Path.Combine(exeDir, "config");
            }
        }
        const string fixedExe = @"C:\ClassWidgets\ClassWidgets.exe";
        if (File.Exists(fixedExe))
        {
            var exeDir = Path.GetDirectoryName(fixedExe)!;
            if (File.Exists(Path.Combine(exeDir, "config", "config.ini")))
            {
                Log.Info(@"[CW-Linkage] 从 C:\ClassWidgets 发现 ClassWidgets");
                return Path.Combine(exeDir, "config");
            }
            if (!_cwFixedMissLogged)
            {
                _cwFixedMissLogged = true;
                Log.Debug(@"[CW-Linkage] 固定路径缺config.ini");
            }
        }
        return "";
    }

    protected override void SaveDetectedPath(string path) => Config.ClassWidgetsDataPath.Value = path;

    // 返回今日课表
    public List<ScheduleRow> GetTodaySchedule()
    {
        var now = AppUtils.PreciseNow();
        var t = TimeOnly.FromDateTime(now);
        var data = ReadSchedule();
        if (data is not { } todayData)
        {
            Log.Debug("[CW-Linkage] 今日无课表数据");
            return [];
        }
        var slots = ParseSchedule(todayData, now);
        int currentIdx;
        bool isBreaking;
        lock (_lock)
        {
            currentIdx = GetState().CurrentIndex;
            isBreaking = GetState().TimeState == TimeState.Breaking;
        }
        var result = new List<ScheduleRow>();
        foreach (var slot in slots)
        {
            if (slot.IsBreak)
            {
                var inThisBreak = isBreaking && slot.StartTime <= t && t < slot.EndTime;
                result.Add(new ScheduleRow("", "",
                    slot.StartTime.ToString("HH:mm"),
                    slot.EndTime.ToString("HH:mm"),
                    0, inThisBreak, true,
                    slot.Subject.Length > 0 ? slot.Subject : "课间"));
            }
            else
            {
                result.Add(new ScheduleRow(slot.Subject, slot.Teacher,
                    slot.StartTime.ToString("HH:mm"),
                    slot.EndTime.ToString("HH:mm"),
                    slot.Index, slot.Index == currentIdx, false, ""));
            }
        }
        Log.Debug($"[CW-Linkage] 今日课表{result.Count}条");
        return result;
    }

    // 取指定日的课表 键 Mon=0..Sun=6
    public List<ScheduleRow> GetScheduleByWeekday(int pythonWeekday)
    {
        var data = ReadSchedule();
        if (data is not { } weekData)
        {
            Log.Debug($"[CW-Linkage] 周{pythonWeekday}无课表数据");
            return [];
        }
        var now = AppUtils.PreciseNow();
        var todayWd = ((int)now.DayOfWeek + 6) % 7;
        var delta = pythonWeekday - todayWd;
        var targetDt = now.Date.AddDays(delta);
        var slots = ParseSchedule(weekData, targetDt);
        var result = new List<ScheduleRow>();
        foreach (var slot in slots)
        {
            if (slot.IsBreak)
            {
                result.Add(new ScheduleRow("", "",
                    slot.StartTime.ToString("HH:mm"),
                    slot.EndTime.ToString("HH:mm"),
                    0, false, true,
                    slot.Subject.Length > 0 ? slot.Subject : "课间"));
            }
            else
            {
                result.Add(new ScheduleRow(slot.Subject, slot.Teacher,
                    slot.StartTime.ToString("HH:mm"),
                    slot.EndTime.ToString("HH:mm"),
                    slot.Index, false, false, ""));
            }
        }
        Log.Debug($"[CW-Linkage] 周{pythonWeekday} 课表{result.Count}条");
        return result;
    }

    // 返回一周课表
    public Dictionary<int, List<ScheduleRow>> GetWeekSchedule()
    {
        var result = new Dictionary<int, List<ScheduleRow>>();
        for (var pyWd = 0; pyWd < 7; pyWd++)
        {
            result[pyWd] = GetScheduleByWeekday(pyWd);
        }
        Log.Debug($"[CW-Linkage] 一周课表{result.Values.Sum(v => v.Count)}条");
        return result;
    }

    // config.ini 读课表名 去 schedule 目录找
    private string? ResolveSchedulePath()
    {
        var configPath = Path.Combine(DataDir, "config.ini");
        var name = "";
        if (File.Exists(configPath))
        {
            var parsed = TryReadIni(configPath, Encoding.UTF8) ?? TryReadIni(configPath, Encoding.GetEncoding("gbk"));
            if (parsed is { } values)
            {
                values.TryGetValue("General|schedule", out var v);
                name = v?.Trim() ?? "";
            }
            else if (!_cfgFailLogged)
            {
                _cfgFailLogged = true;
                Log.Debug($"[CW-Linkage] config.ini 读取失败 ({configPath})");
            }
        }
        if (name.Length == 0 && !_cfgNameMissingLogged)
        {
            _cfgNameMissingLogged = true;
            Log.Debug($"[CW-Linkage] config.ini 缺失或未读到 schedule 名: {configPath}");
        }
        var schedDir = Path.Combine(DataDir, "schedule");
        if (!Directory.Exists(schedDir))
        {
            if (_lastResolved != "")
            {
                Log.Warning($"[CW-Linkage] schedule 目录不存在: {schedDir}");
                _lastResolved = "";
            }
            return null;
        }
        if (name.Length > 0)
        {
            var direct = Path.Combine(schedDir, name);
            if (File.Exists(direct))
            {
                return TrackResolved(direct);
            }
            var withExt = Path.Combine(schedDir, $"{name}.json");
            if (File.Exists(withExt))
            {
                return TrackResolved(withExt);
            }
        }
        Log.Warning($"[CW-Linkage] '{name}' 不存在 扫描目录");
        foreach (var f in Directory.EnumerateFiles(schedDir, "*.json").OrderBy(f => f))
        {
            return TrackResolved(f);
        }
        if (_lastResolved != "")
        {
            Log.Warning($"[CW-Linkage] schedule 目录无可用课表文件: {schedDir}");
            _lastResolved = "";
        }
        return null;
    }

    private string TrackResolved(string path)
    {
        if (path != _lastResolved)
        {
            Log.Debug($"[CW-Linkage] 用课表文件: {path}");
            _lastResolved = path;
        }
        return path;
    }

    // ini 解析 (section|key > value)
    private static Dictionary<string, string>? TryReadIni(string path, Encoding encoding)
    {
        try
        {
            var result = new Dictionary<string, string>();
            var section = "";
            foreach (var rawLine in File.ReadAllLines(path, encoding))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                {
                    continue;
                }
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1].Trim();
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq > 0)
                {
                    result[$"{section}|{line[..eq].Trim()}"] = line[(eq + 1)..].Trim();
                }
            }
            return result;
        }
        catch (Exception e)
        {

            Glimpseon.Core.Log.Debug($"[LINKAGE] 读取失败: {e.Message}");
            return null;
        }
    }

    private JsonElement? ReadSchedule()
    {
        var schedFile = ResolveSchedulePath();
        if (schedFile is null)
        {
            return null;
        }
        DateTime mtime;
        try
        {
            mtime = File.GetLastWriteTime(schedFile);
        }
        catch (Exception e)
        {
            if (_lastReadFail != schedFile)
            {
                _lastReadFail = schedFile;
                Log.Debug($"[CW-Linkage] 获取课表文件信息失败: {e.Message} ({schedFile})");
            }
            return null;
        }
        lock (_lock)
        {
            if (_cwCacheFile == schedFile && _cwCacheMtime == mtime && _hasCacheData)
            {
                return _cwCacheData;
            }
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(schedFile));
            var data = doc.RootElement.Clone();
            lock (_lock)
            {
                _cwCacheFile = schedFile;
                _cwCacheMtime = mtime;
                _cwCacheData = data;
                _hasCacheData = true;
            }
            _lastReadFail = null;
            Log.Debug($"[CW-Linkage] 已读取课表: {Path.GetFileName(schedFile)}");
            return data;
        }
        catch (Exception e)
        {
            if (_lastReadFail != schedFile)
            {
                _lastReadFail = schedFile;
                Log.Warning($"[CW-Linkage] 课表文件读取/解析失败: {e.Message} ({schedFile})");
            }
            return null;
        }
    }

    protected override LinkageState ComputeState()
    {
        var now = AppUtils.PreciseNow();
        var today = now.Date;
        var t = TimeOnly.FromDateTime(now);
        var data = ReadSchedule();
        if (data is not { } stateData)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures == 1)
            {
                Log.Warning($"[CW-Linkage] 课表数据为空 (路径: {(DataDir.Length > 0 ? DataDir : "未设置")})");
            }
            if (_consecutiveFailures >= 2)
            {
                TryRedetect();
            }
            return new LinkageState { IsConnected = false };
        }
        _consecutiveFailures = 0;
        var st = new LinkageState { IsConnected = true, LastUpdate = now };
        var slots = ParseSchedule(stateData, now);
        if (slots.Count == 0)
        {
            st.TimeState = TimeState.None;
            if (!_noSlotsLogged)
            {
                _noSlotsLogged = true;
                Log.Debug("[CW-Linkage] 今日未解析出任何时间段");
            }
            return st;
        }

        // 查找当前时间段
        CwTimeSlot? currentSlot = null;
        foreach (var slot in slots)
        {
            if (slot.StartTime <= t && t < slot.EndTime)
            {
                currentSlot = slot;
                break;
            }
        }

        // 当前不在任何时间段内
        if (currentSlot is null)
        {
            if (slots.Count > 0 && t >= slots[^1].EndTime)
            {
                st.TimeState = TimeState.AfterSchool;
                return st;
            }
            st.TimeState = TimeState.Breaking;
            st.CurrentSubject = "课间";
            foreach (var nextSlot in slots)
            {
                if (!nextSlot.IsBreak && nextSlot.StartTime > t)
                {
                    st.NextLesson = new LessonInfo
                    {
                        SubjectName = nextSlot.Subject,
                        TeacherName = nextSlot.Teacher,
                        StartTime = nextSlot.StartTime.ToString("HH:mm"),
                        EndTime = nextSlot.EndTime.ToString("HH:mm"),
                        Index = nextSlot.Index,
                    };
                    break;
                }
            }
            if (st.NextLesson is { } nl)
            {
                try
                {
                    var parts = nl.StartTime.Split(':');
                    var ns = new TimeOnly(int.Parse(parts[0]), int.Parse(parts[1]));
                    var leftToNext = today.Add(ns.ToTimeSpan()) - now;
                    if (leftToNext.TotalSeconds > 0)
                    {
                        st.OnBreakingLeft = LinkageBridge.FmtDelta(leftToNext);
                    }
                }
                catch (Exception e)
                {
                    if (!_breakLeftErrLogged)
                    {
                        _breakLeftErrLogged = true;
                        Log.Debug($"[CW-Linkage] 课间剩余时间计算失败: {e.Message}");
                    }
                }
            }
            return st;
        }

        // 当前在某个时间段内
        if (currentSlot.IsBreak)
        {
            st.TimeState = TimeState.Breaking;
            st.CurrentSubject = currentSlot.Subject.Length > 0 ? currentSlot.Subject : "课间";
        }
        else
        {
            st.TimeState = TimeState.OnClass;
            st.CurrentSubject = currentSlot.Subject;
            st.CurrentLesson = new LessonInfo
            {
                SubjectName = currentSlot.Subject,
                TeacherName = currentSlot.Teacher,
                StartTime = currentSlot.StartTime.ToString("HH:mm"),
                EndTime = currentSlot.EndTime.ToString("HH:mm"),
                Index = currentSlot.Index,
            };
            st.CurrentIndex = currentSlot.Index;
        }

        var left = today.Add(currentSlot.EndTime.ToTimeSpan()) - now;
        if (currentSlot.IsBreak)
        {
            st.OnBreakingLeft = LinkageBridge.FmtDelta(left);
        }
        else
        {
            st.OnClassLeft = LinkageBridge.FmtDelta(left);
        }

        // 下一节课
        var currentIdx = slots.IndexOf(currentSlot);
        for (var i = currentIdx + 1; i < slots.Count; i++)
        {
            if (!slots[i].IsBreak)
            {
                st.NextLesson = new LessonInfo
                {
                    SubjectName = slots[i].Subject,
                    TeacherName = slots[i].Teacher,
                    StartTime = slots[i].StartTime.ToString("HH:mm"),
                    EndTime = slots[i].EndTime.ToString("HH:mm"),
                    Index = slots[i].Index,
                };
                break;
            }
        }

        Log.Debug($"[CW-Linkage] {st.TimeState.DisplayName()}|{(st.CurrentSubject.Length > 0 ? st.CurrentSubject : "-")}|" +
                  $"{currentSlot.StartTime:HH:mm}-{currentSlot.EndTime:HH:mm}|下节:{(st.NextLesson is { } nx ? nx.SubjectName : "-")}");
        return st;
    }

    // 解析 cw 课表 json 顺序衔接时段
    private List<CwTimeSlot> ParseSchedule(JsonElement data, DateTime now)
    {
        var slots = new List<CwTimeSlot>();
        var weekType = GetWeekType(data, now);
        var wdKey = (((int)now.DayOfWeek + 6) % 7).ToString(CultureInfo.InvariantCulture); // Mon=0..Sun=6
        var timelineKey = weekType == 1 ? "timeline_even" : "timeline";
        var timeline = data.TryGetProperty(timelineKey, out var tl) && tl.ValueKind == JsonValueKind.Object ? tl : default;
        var dayTimeline = default(JsonElement);
        if (timeline.ValueKind == JsonValueKind.Object)
        {
            if (timeline.TryGetProperty(wdKey, out var dt) && dt.ValueKind == JsonValueKind.Array)
            {
                dayTimeline = dt;
            }
            else if (timeline.TryGetProperty("default", out var dft) && dft.ValueKind == JsonValueKind.Array)
            {
                dayTimeline = dft;
            }
        }
        if (dayTimeline.ValueKind == JsonValueKind.Undefined || dayTimeline.GetArrayLength() == 0)
        {
            if (_lastParseSig is null || _lastParseSig.Value.Item2 != "empty" || _lastParseSig.Value.Item3 != int.Parse(wdKey))
            {
                _lastParseSig = (DateTime.MinValue, "empty", int.Parse(wdKey));
                Log.Debug($"[CW-Linkage] 周{wdKey} 无时间线数据");
            }
            return slots;
        }
        var schedKey = weekType == 1 ? "schedule_even" : "schedule";
        var schedule = data.TryGetProperty(schedKey, out var sc) && sc.ValueKind == JsonValueKind.Object ? sc : default;
        JsonElement daySchedule = default;
        if (schedule.ValueKind == JsonValueKind.Object && schedule.TryGetProperty(wdKey, out var ds) && ds.ValueKind == JsonValueKind.Array)
        {
            daySchedule = ds;
        }
        var parts = data.TryGetProperty("part", out var pt) && pt.ValueKind == JsonValueKind.Object ? pt : default;

        // 首条 timeline 的 part 基准时间初始化
        var currentTime = new TimeOnly(0, 0);
        if (dayTimeline.GetArrayLength() > 0)
        {
            var first = dayTimeline[0];
            if (first.ValueKind == JsonValueKind.Array && first.GetArrayLength() >= 2)
            {
                var fpKey = first[1].ToString();
                if (parts.ValueKind == JsonValueKind.Object && parts.TryGetProperty(fpKey, out var fp) &&
                    fp.ValueKind == JsonValueKind.Array && fp.GetArrayLength() >= 2)
                {
                    currentTime = new TimeOnly(fp[0].GetInt32(), fp[1].GetInt32());
                }
                else if (!_fpMissingLogged)
                {
                    _fpMissingLogged = true;
                    Log.Warning("[CW-Linkage] 首条时段缺part基准 按00:00起算");
                }
            }
        }

        // 顺序衔接
        var classCounter = 0;
        foreach (var unit in dayTimeline.EnumerateArray())
        {
            if (unit.ValueKind != JsonValueKind.Array || unit.GetArrayLength() < 4)
            {
                if (!_badUnitLogged)
                {
                    _badUnitLogged = true;
                    Log.Debug($"[CW-Linkage] 时间线单元格式异常 {unit}");
                }
                continue;
            }
            var unitType = unit[0].GetInt32();
            var durationMin = unit[3].GetInt32();
            var startT = currentTime;
            var endH = startT.Hour + (startT.Minute + durationMin) / 60;
            var endM = (startT.Minute + durationMin) % 60;
            // 跨天时段钳到 23:59 保持 end>=start
            var endT = endH >= 24 ? new TimeOnly(23, 59) : new TimeOnly(endH, endM);
            currentTime = endT;
            if (unitType == 0)
            {
                classCounter++;
                var cidx = unit[2].GetInt32();
                var idx = cidx - 1;
                var subjectName = daySchedule.ValueKind == JsonValueKind.Array && idx >= 0 && idx < daySchedule.GetArrayLength()
                    ? daySchedule[idx].GetString() ?? "" : "";
                slots.Add(new CwTimeSlot(startT, endT, subjectName, "", classCounter, false));
            }
            else
            {
                slots.Add(new CwTimeSlot(startT, endT, "课间", "", 0, true));
            }
        }
        var sig = (_cwCacheMtime, wdKey, weekType);
        if (_lastParseSig != sig)
        {
            _lastParseSig = sig;
            var classN = slots.Count(s => !s.IsBreak);
            Log.Debug($"[CW-Linkage] 解析{slots.Count}时段 周{wdKey} {(weekType == 1 ? "双周" : "单周")} {classN}节)");
        }
        return slots;
    }

    // 0=单周 1=双周
    private int GetWeekType(JsonElement data, DateTime now)
    {
        try
        {
            var startDateStr = data.TryGetProperty("start_date", out var sd) ? sd.GetString() : null;
            if (!string.IsNullOrEmpty(startDateStr))
            {
                var startDate = DateTime.ParseExact(startDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture).Date;
                var weekNum = (now.Date - startDate).Days / 7 + 1;
                return weekNum % 2 == 0 ? 1 : 0;
            }
            if (!_noStartDatedLogged)
            {
                _noStartDatedLogged = true;
                Log.Debug("课表缺start_date 按单周");
            }
        }
        catch (Exception e)
        {
            if (!_weekTypeErrLogged)
            {
                _weekTypeErrLogged = true;
                Log.Debug($"[CW-Linkage] 单双周计算失败: {e.Message}");
            }
        }
        return 0;
    }
}
