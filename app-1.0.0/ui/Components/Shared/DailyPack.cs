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

// DailyPack
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

internal static class DailyPack
{
    private static readonly object Gate = new();
    private static DispatcherTimer? _midnight;
    private static bool _generating;
    private static JsonObject? _cache;
    private static string _cacheDate = "";

    internal const double NetworkRetryMinutes = 10;

    private static string PackPath => IOPath.Combine(Paths.DataCache, "content_pack.json");

    private static readonly string[] BuiltinWords =
    [
        """{"word":"dawn","phonetic":"/dɔːn/","translation":"n. 黎明","example":"The dawn light filled the classroom.","example_trans":"晨光洒满教室。"}""",
        """{"word":"curious","phonetic":"/ˈkjʊəriəs/","translation":"adj. 好奇的","example":"Curious minds ask why.","example_trans":"好奇的心会追问为什么。"}""",
        """{"word":"effort","phonetic":"/ˈefət/","translation":"n. 努力","example":"Effort beats talent when talent stops trying.","example_trans":"当天才放弃时,努力胜过天赋。"}""",
        """{"word":"horizon","phonetic":"/həˈraɪzn/","translation":"n. 地平线","example":"The sun rose above the horizon.","example_trans":"太阳升上地平线。"}""",
        """{"word":"gather","phonetic":"/ˈɡæðə/","translation":"v. 聚集","example":"We gather here every Monday.","example_trans":"我们每周一在这里集合。"}""",
        """{"word":"steady","phonetic":"/ˈstedi/","translation":"adj. 稳定的","example":"Steady steps win the long race.","example_trans":"稳扎稳打赢得长跑。"}""",
        """{"word":"brilliant","phonetic":"/ˈbrɪliənt/","translation":"adj. 灿烂的","example":"A brilliant idea needs a brave start.","example_trans":"灿烂的想法需要勇敢的开始。"}""",
        """{"word":"journey","phonetic":"/ˈdʒɜːni/","translation":"n. 旅程","example":"Learning is a long journey.","example_trans":"学习是一段漫长的旅程。"}""",
    ];

    // 兜底句包
    private static readonly string[] BuiltinSentences =
    [
        """{"content":"The best time to plant a tree was ten years ago. The second best is now.","note":"种树最好的时间是十年前,其次是现在。"}""",
        """{"content":"Well begun is half done.","note":"好的开始是成功的一半。"}""",
        """{"content":"Little by little, one travels far.","note":"积跬步,至千里。"}""",
        """{"content":"Actions speak louder than words.","note":"行动胜于言语。"}""",
        """{"content":"A journey of a thousand miles begins with a single step.","note":"千里之行,始于足下。"}""",
        """{"content":"Practice makes perfect.","note":"熟能生巧。"}""",
        """{"content":"Where there is a will, there is a way.","note":"有志者事竟成。"}""",
        """{"content":"The early bird catches the worm.","note":"早起的鸟儿有虫吃。"}""",
    ];

    public static void Start()
    {
        Ensure();
        ArmMidnight();
    }

    public static (string Cursor, JsonObject Item)? Take(string kind)
    {
        var pack = Read(kind, todayOnly: true);
        if (pack is not { Count: > 0 })
        {
            Ensure();
            pack = Read(kind, todayOnly: false);
        }
        if (pack is not { Count: > 0 })
        {
            return null;
        }
        var index = Rotation(pack.Count);
        return pack[index] is JsonObject item ? ($"{TodayKey()}#{index}", item) : null;
    }

    private static int Rotation(int count)
    {
        if (count <= 0)
        {
            return 0;
        }
        var now = AppUtils.PreciseNow();
        var mode = Config.ContentPackRotate.Value;
        if (mode == "schedule")
        {
            var rows = TimetableScheduleProvider.GetTodaySchedule();
            if (rows.Count > 0)
            {
                var started = rows.Count(r => !r.IsBreak && TimeOnly.TryParse(r.StartTime, out var st) && st <= TimeOnly.FromDateTime(now));
                return started % count;
            }
        }
        var minutes = int.TryParse(mode.TrimEnd('分', '钟'), out var m) ? Math.Max(1, m) : 30;
        return (int)(now.TimeOfDay.TotalMinutes / minutes) % count;
    }

    private static void Ensure()
    {
        lock (Gate)
        {
            if (_generating || (Cached() is { } pack && pack["date"]?.GetValue<string>() == TodayKey()))
            {
                return;
            }
            _generating = true;
        }
        _ = GenerateAsync();
    }

    private static async Task GenerateAsync()
    {
        try
        {
            var date = TodayKey();
            var words = await FromAi("word") ?? Pool(BuiltinWords);
            var sentences = await FromAi("sentence") ?? Pool(BuiltinSentences);
            var root = new JsonObject { ["date"] = date, ["word"] = words, ["sentence"] = sentences };
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.DataCache);
                File.WriteAllText(PackPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                _cache = root;
                _cacheDate = date;
            }
            Log.Info($"[日更] 包就绪 words={words.Count} sentences={sentences.Count}");
        }
        catch (Exception e)
        {
            Log.Warning($"[日更] 生成失败: {e.Message}");
        }
        finally
        {
            lock (Gate)
            {
                _generating = false;
            }
        }
    }

    private static async Task<JsonArray?> FromAi(string kind)
    {
        if (!AiService.Ready)
        {
            return null;
        }
        const string system = "你是英语教学助手。只输出 JSON 数组,不要解释或代码块标记。";
        var prompt = kind == "word"
            ? "生成8个适合中学生的英语单词词条,JSON数组,每项含 word(小写)、phonetic(音标含//)、translation(词性+中文)、example(英文例句)、example_trans(中文翻译)。难度循序渐进,主题贴近校园生活。"
            : "生成8句适合中学生的英语名言或谚语,JSON数组,每项含 content(英文句子)、note(中文翻译或一句点评)。积极向上,适合教室大屏展示。";
        try
        {
            var result = await AiService.ChatAsync(prompt, system);
            if (!result.Ok)
            {
                Log.Warning($"[日更] AI {kind} 失败: {result.Error}");
                return null;
            }
            return Parse(result.Content);
        }
        catch (Exception e)
        {
            Log.Warning($"[日更] AI {kind} 异常: {e.Message}");
            return null;
        }
    }

    private static JsonArray? Parse(string text)
    {
        var s = Regex.Replace(text, "```(?:json)?", "").Trim();
        var start = s.IndexOf('[');
        var end = s.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(s[start..(end + 1)]) as JsonArray;
        }
        catch
        {
            return null;
        }
    }

    private static JsonArray Pool(string[] items)
    {
        var arr = new JsonArray();
        foreach (var item in items)
        {
            if (JsonNode.Parse(item) is { } node)
            {
                arr.Add(node);
            }
        }
        return arr;
    }

    private static void ArmMidnight()
    {
        if (_midnight is null)
        {
            _midnight = new DispatcherTimer();
            _midnight.Tick += OnMidnight;
        }
        _midnight.Stop();
        var now = AppUtils.PreciseNow();
        _midnight.Interval = now.Date.AddDays(1) - now + TimeSpan.FromSeconds(2);
        _midnight.Start();
    }

    private static void OnMidnight(object? sender, EventArgs e)
    {
        _midnight?.Stop();
        Ensure();
        ArmMidnight();
    }

    private static JsonObject? ReadPack()
    {
        try
        {
            return File.Exists(PackPath) ? JsonNode.Parse(File.ReadAllText(PackPath)) as JsonObject : null;
        }
        catch
        {
            return null;
        }
    }

    private static JsonArray? Read(string kind, bool todayOnly)
    {
        lock (Gate)
        {
            var pack = Cached();
            if (pack is null || (todayOnly && pack["date"]?.GetValue<string>() != TodayKey()))
            {
                return null;
            }
            return pack[kind] as JsonArray;
        }
    }

    private static JsonObject? Cached()
    {
        var date = TodayKey();
        if (_cacheDate == date)
        {
            return _cache;
        }
        _cache = ReadPack();
        _cacheDate = date;
        return _cache;
    }

    private static string TodayKey() => AppUtils.PreciseNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
