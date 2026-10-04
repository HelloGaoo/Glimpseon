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

// 组件数据模型
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Glimpseon.Core;

public enum ResizeMode
{
    Fixed,
    Horizontal,
    Vertical,
    Free,
}

public sealed class ComponentDefinition
{
    public double DefaultWidth => DefaultWidthPx > 0 ? DefaultWidthPx : DefaultWidthCells * 110.0;
    public double DefaultHeight => DefaultHeightPx > 0 ? DefaultHeightPx : DefaultHeightCells * 110.0;
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Category { get; init; } = "";
    public string Icon { get; init; } = "";
    public int MinWidthCells { get; init; } = 1;
    public int MinHeightCells { get; init; } = 1;
    public int DefaultWidthCells { get; init; } = 2;
    /// <summary>原版默认像素宽(0=用 cells*110)</summary>
    public int DefaultWidthPx { get; init; }
    /// <summary>原版默认像素高(0=用 cells*110)</summary>
    public int DefaultHeightPx { get; init; }
    public int DefaultHeightCells { get; init; } = 2;
    public ResizeMode ResizeMode { get; init; } = ResizeMode.Free;
    public JsonElement? DefaultConfig { get; init; }
}

public sealed class GridSettings
{
    public int ShortSideCells { get; init; } = 6;
    public double GapRatio { get; init; } = 0.12;
    public int InsetPercent { get; init; } = 5;
}

public sealed class GridMetrics
{
    public int ColumnCount { get; init; }
    public int RowCount { get; init; }
    public double CellSize { get; init; }
    public double GapPx { get; init; }
    public double EdgeInsetPx { get; init; }
    public double GridWidthPx { get; init; }
    public double GridHeightPx { get; init; }
    public double Pitch => CellSize + GapPx;
}

public static class GridLayoutService
{
    public static GridMetrics CalculateGridMetrics(double hostWidth, double hostHeight, GridSettings settings)
    {
        if (hostWidth <= 1 || hostHeight <= 1)
        {
            return new GridMetrics();
        }
        var shortSideCells = Math.Max(1, settings.ShortSideCells);
        var gapRatio = Math.Max(0, settings.GapRatio);
        var edgeInset = CalculateEdgeInset(hostWidth, hostHeight, shortSideCells, settings.InsetPercent);

        var availableWidth = Math.Max(1, hostWidth - edgeInset * 2);
        var availableHeight = Math.Max(1, hostHeight - edgeInset * 2);

        if (hostWidth >= hostHeight)
        {
            var rowCount = shortSideCells;
            var denominator = rowCount + Math.Max(0, rowCount - 1) * gapRatio;
            if (denominator <= 0)
            {
                return new GridMetrics();
            }
            var cellSize = availableHeight / denominator;
            var gap = cellSize * gapRatio;
            var pitch = cellSize + gap;
            var columnCount = Math.Max(1, (int)((availableWidth + gap) / pitch));
            return new GridMetrics
            {
                ColumnCount = columnCount,
                RowCount = rowCount,
                CellSize = cellSize,
                GapPx = gap,
                EdgeInsetPx = edgeInset,
                GridWidthPx = columnCount * cellSize + Math.Max(0, columnCount - 1) * gap,
                GridHeightPx = rowCount * cellSize + Math.Max(0, rowCount - 1) * gap,
            };
        }
        else
        {
            var columnCount = shortSideCells;
            var denominator = columnCount + Math.Max(0, columnCount - 1) * gapRatio;
            if (denominator <= 0)
            {
                return new GridMetrics();
            }
            var cellSize = availableWidth / denominator;
            var gap = cellSize * gapRatio;
            var pitch = cellSize + gap;
            var rowCount = Math.Max(1, (int)((availableHeight + gap) / pitch));
            return new GridMetrics
            {
                ColumnCount = columnCount,
                RowCount = rowCount,
                CellSize = cellSize,
                GapPx = gap,
                EdgeInsetPx = edgeInset,
                GridWidthPx = columnCount * cellSize + Math.Max(0, columnCount - 1) * gap,
                GridHeightPx = rowCount * cellSize + Math.Max(0, rowCount - 1) * gap,
            };
        }
    }

    private static double CalculateEdgeInset(double hostWidth, double hostHeight, int shortSideCells, int insetPercent)
    {
        if (hostWidth <= 1 || hostHeight <= 1)
        {
            return 0;
        }
        var cells = Math.Max(1, shortSideCells);
        var shortSide = Math.Max(1, Math.Min(hostWidth, hostHeight));
        var baseCell = shortSide / cells;
        var insetRatio = Math.Clamp(insetPercent, 0, 30) / 100.0;
        return Math.Max(0, Math.Min(80, baseCell * insetRatio));
    }
}

public sealed class ComponentRegistry
{
    private readonly Dictionary<string, ComponentDefinition> _definitions = new();

    public void RegisterBatch(IEnumerable<ComponentDefinition> definitions)
    {
        foreach (var d in definitions)
        {
            if (!string.IsNullOrEmpty(d.Id))
            {
                _definitions[d.Id] = d;
            }
        }
        Log.Debug($"[ComponentRegistry] 批量注册组件 当前总数 {Count}");
    }

    public int Count => _definitions.Count;

    public ComponentDefinition? GetDefinition(string componentId) =>
        _definitions.TryGetValue(componentId, out var d) ? d : null;

    public IEnumerable<ComponentDefinition> GetDefinitionsByCategory(string category) =>
        _definitions.Values.Where(d => d.Category == category);

    public List<string> GetCategories() =>
        _definitions.Values.Select(d => d.Category).Distinct().OrderBy(c => c).ToList();
}

public static class BuiltinComponentDefinitions
{
    private static ComponentDefinition Def(string id, string displayName, string category, string icon,
        int minW = 1, int minH = 1, int defW = 2, int defH = 2, ResizeMode mode = ResizeMode.Free, JsonElement? config = null,
        int wPx = 0, int hPx = 0) => new()
    {
        Id = id,
        DisplayName = displayName,
        Category = category,
        Icon = icon,
        MinWidthCells = minW,
        MinHeightCells = minH,
        DefaultWidthCells = defW,
        DefaultHeightCells = defH,
        ResizeMode = mode,
        DefaultConfig = config,
        DefaultWidthPx = wPx,
        DefaultHeightPx = hPx,
    };

    public static readonly ComponentDefinition[] All =
    {
        Def("clock_digital", "数字时钟", "Clock", "Clock", 2, 2, 2, 2, wPx: 400, hPx: 200, config: Cfg("""{"show_seconds":true,"show_lunar":true}""")),
        Def("clock_square_1", "方形钟表I", "Clock", "Clock", wPx: 200, hPx: 200),
        Def("clock_square_2", "方形钟表II", "Clock", "Clock", wPx: 200, hPx: 200),
        Def("clock_calendar_month", "月历", "Clock", "Calendar", 2, 2, 2, 3, wPx: 200, hPx: 200),
        Def("clock_calendar_mini", "简约月历", "Clock", "Calendar", wPx: 200, hPx: 200),
        Def("clock_almanac", "黄历", "Clock", "Calendar", wPx: 400, hPx: 200),
        Def("countdown_event", "倒计时", "Clock", "Calendar", wPx: 200, hPx: 200, config: Cfg("""{"target_name":"","target_date":""}""")),
        Def("countdown_days", "倒数日", "Clock", "Calendar", wPx: 200, hPx: 200, config: Cfg("""{"event_name":"","target_date":"","title_bg_color":"#F98E1B"}""")),
        Def("timer_countdown", "计时与倒计时", "Clock", "StopWatch", wPx: 360, hPx: 320),
        Def("weather_icon_temp", "天气", "Weather", "WeatherSunny", 2, 1, 2, 1, ResizeMode.Horizontal, wPx: 200, hPx: 200, config: Cfg("""{"show_icon":true}""")),
        Def("weather_hourly", "逐小时天气", "Weather", "WeatherSunny", 4, 2, 4, 2, ResizeMode.Horizontal, wPx: 400, hPx: 200),
        Def("weather_weekly", "逐日天气", "Weather", "WeatherSunny", 2, 2, 2, 2, ResizeMode.Fixed, wPx: 200, hPx: 200),
        Def("poetry_one_line", "一言", "Info", "Book", 4, 1, 4, 1, ResizeMode.Horizontal, wPx: 400, hPx: 200),
        Def("news_baidu", "百度新闻", "Info", "News", 4, 2, 4, 2, ResizeMode.Horizontal, wPx: 360, hPx: 220),
        Def("news_weibo", "微博新闻", "Info", "News", 4, 2, 4, 2, ResizeMode.Horizontal, wPx: 360, hPx: 220),
        Def("news_jinritoutiao", "今日头条新闻", "Info", "News", 4, 2, 4, 2, ResizeMode.Horizontal, wPx: 360, hPx: 220),
        Def("news_tenxunwang", "腾讯网新闻", "Info", "News", 4, 2, 4, 2, ResizeMode.Horizontal, wPx: 360, hPx: 220),
        Def("news_xcvts", "央视新闻", "Info", "News", 4, 2, 4, 2, ResizeMode.Horizontal, wPx: 360, hPx: 220),
        Def("history_today", "历史上的今天", "Info", "History", 4, 2, 4, 2, ResizeMode.Horizontal, wPx: 360, hPx: 240),
        Def("word_daily", "每日单词", "Info", "LocalLanguage", 4, 2, 5, 2, ResizeMode.Horizontal, wPx: 400, hPx: 200),
        Def("sentence_daily", "每日英语", "Info", "ChatBubblesQuestion", 4, 2, 5, 2, ResizeMode.Horizontal, wPx: 400, hPx: 200),
        Def("school_info_class_info", "班级卡片", "School", "Education", 2, 1, 2, 1, ResizeMode.Horizontal, wPx: 400, hPx: 200, config: Cfg("""{"school":"","class":""}""")),
        Def("linkage_timetable_preview", "今日课表", "School", "Education", 2, 3, 2, 5, wPx: 300, hPx: 550),
        Def("linkage_timetable_nowlesson", "当前课程", "School", "Education", 2, 2, 2, 2, ResizeMode.Fixed,
            wPx: 400, hPx: 200, config: Cfg("""{"show_teacher":true,"show_next":true,"show_duration":true,"show_countdown":true,"prepare_minutes":3}""")),
        Def("linkage_timetable_timeline", "课程时间轴", "School", "Education", wPx: 400, hPx: 200),
        Def("class_album_horizontal", "横向相册", "School", "Photo", 2, 1, 2, 1, wPx: 400, hPx: 200),
        Def("class_album_vertical", "纵向相册", "School", "Photo", 1, 2, 1, 2, wPx: 200, hPx: 400),
        Def("homework_board", "作业板", "School", "Education", 2, 2, 3, 2, wPx: 430, hPx: 236),
        Def("announcement_board", "公告栏", "School", "Education", 2, 2, 3, 3, wPx: 420, hPx: 400),
        Def("media_player", "媒体播放器", "Media", "Music", 2, 1, 2, 1, ResizeMode.Horizontal, wPx: 400, hPx: 200, config: Cfg("""{"show_progress":true}""")),
        Def("quick_launch_dock", "快捷启动", "Launcher", "App", 4, 1, 4, 1, ResizeMode.Horizontal, wPx: 400, hPx: 200, config: Cfg("""{"icon_size":64}""")),
        Def("quick_launch_grid", "快捷启动II", "Launcher", "App", 4, 2, 4, 2, wPx: 400, hPx: 200, config: Cfg("""{"apps":[]}""")),
        Def("Math_calculator", "计算器", "Tools", "Calculator", 2, 2, 2, 2, ResizeMode.Fixed, wPx: 280, hPx: 420),
        Def("writing_pad", "书写板", "Tools", "Edit", 4, 1, 4, 1, ResizeMode.Fixed, wPx: 400, hPx: 100),
        Def("sticky_note", "便签", "Tools", "Edit", 1, 1, 2, 2, wPx: 280, hPx: 280, config: Cfg("""{"color":"yellow"}""")),
        Def("system_performance", "性能监测", "System", "Gauge", 4, 2, 4, 2, wPx: 400, hPx: 200),
        Def("system_netspeed", "网速监控", "System", "Globe", 3, 2, 4, 2, wPx: 400, hPx: 200),
        Def("study_meter", "分贝仪", "Study", "Microphone", 3, 2, 4, 2, wPx: 400, hPx: 200),
    };
    private static JsonElement? Cfg(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}

public sealed class PageMeta
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "info";
    public List<JsonElement> Components { get; set; } = new();
    public List<JsonElement> Items { get; set; } = new();

    public JsonObject ToJson()
    {
        var obj = new JsonObject
        {
            ["name"] = Name,
            ["type"] = Type,
        };
        if (Type == "info")
        {
            var arr = new JsonArray();
            foreach (var c in Components)
            {
                arr.Add(JsonNode.Parse(c.GetRawText()));
            }
            obj["components"] = arr;
        }
        else
        {
            var arr = new JsonArray();
            foreach (var i in Items)
            {
                arr.Add(JsonNode.Parse(i.GetRawText()));
            }
            obj["items"] = arr;
        }
        return obj;
    }

    public static PageMeta FromJson(JsonObject obj)
    {
        var meta = new PageMeta
        {
            Name = obj["name"]?.GetValue<string>() ?? "",
            Type = obj["type"]?.GetValue<string>() ?? "info",
        };
        if (meta.Type == "info" && obj["components"] is JsonArray comps)
        {
            foreach (var c in comps)
            {
                if (c is not null)
                {
                    meta.Components.Add(JsonSerializer.Deserialize<JsonElement>(c.ToJsonString()));
                }
            }
        }
        if (meta.Type == "nav" && obj["items"] is JsonArray items)
        {
            foreach (var i in items)
            {
                if (i is not null)
                {
                    meta.Items.Add(JsonSerializer.Deserialize<JsonElement>(i.ToJsonString()));
                }
            }
        }
        return meta;
    }
}

public sealed class PageManager
{
    private static readonly string[][] DefaultPages =
    {
        new[] { "信息页", "info" },
        new[] { "导航页", "nav" },
    };

    public const int MaxPages = 10;

    private readonly string _layoutFile;
    private List<PageMeta> _pages = new();
    private int _currentPage;

    public PageManager(string configDir)
    {
        _layoutFile = Path.Combine(configDir, "home_layout.json");
        Load();
    }

    public void Load()
    {
        if (File.Exists(_layoutFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_layoutFile));
                var root = doc.RootElement;
                var pagesData = root.TryGetProperty("pages", out var p) && p.ValueKind == JsonValueKind.Array
                    ? p
                    : (JsonElement?)null;
                _pages = new List<PageMeta>();
                if (pagesData is { } pagesArray)
                {
                    foreach (var page in pagesArray.EnumerateArray())
                    {
                        _pages.Add(PageMeta.FromJson(JsonSerializer.Deserialize<JsonObject>(page.GetRawText())!));
                    }
                }
                if (_pages.Count == 0)
                {
                    _pages = DefaultPages.Select(d => new PageMeta { Name = d[0], Type = d[1] }).ToList();
                }
                var current = root.TryGetProperty("current_page", out var cp) ? cp.GetInt32() : 0;
                _currentPage = Math.Clamp(current, 0, _pages.Count - 1);
                Log.Info($"[PageManager] 已加载布局: {Count}页 当前页={_currentPage}");
                return;
            }
            catch (Exception e)
            {
                Log.Error($"[PageManager] 加载失败: {e.Message}");
            }
        }
        else
        {
            Log.Info($"[PageManager] 布局缺失 {_layoutFile} 页面");
        }
        _pages = DefaultPages.Select(d => new PageMeta { Name = d[0], Type = d[1] }).ToList();
        _currentPage = 0;
        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_layoutFile)!);
            var root = new JsonObject
            {
                ["current_page"] = _currentPage,
                ["pages"] = new JsonArray(_pages.Select(p => p.ToJson()).ToArray()),
            };
            File.WriteAllText(_layoutFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Log.Debug($"[PageManager] 布局已保存 ({Count}页)");
        }
        catch (Exception e)
        {
            Log.Error($"[PageManager] 保存失败: {e.Message}");
        }
    }

    public int Count => _pages.Count;
    public List<PageMeta> Pages() => new(_pages);
    public PageMeta? GetPage(int index) => index >= 0 && index < _pages.Count ? _pages[index] : null;
    public int GetCurrentPage() => _currentPage;

    public void SetCurrentPage(int index)
    {
        if (index >= 0 && index < _pages.Count)
        {
            _currentPage = index;
        }
    }

    public int AddPage(string name = "", string pageType = "info")
    {
        if (_pages.Count >= MaxPages)
        {
            Log.Warning($"[PageManager] 页面已达上限 {MaxPages}");
            return -1;
        }
        if (string.IsNullOrEmpty(name))
        {
            var count = _pages.Count(p => p.Type == pageType) + 1;
            name = pageType == "nav" ? $"导航页 {count}" : $"信息页 {count}";
        }
        _pages.Add(new PageMeta { Name = name, Type = pageType });
        Save();
        var newIndex = _pages.Count - 1;
        Log.Info($"[PageManager] 新增页面: name='{name}' type={pageType} index={newIndex}");
        return newIndex;
    }

    public void RenamePage(int index, string name)
    {
        if (index >= 0 && index < _pages.Count)
        {
            Log.Info($"[PageManager] 重命名页面: index={index} '{_pages[index].Name}' -> '{name}'");
            _pages[index].Name = name;
            Save();
        }
    }

    public bool DeletePage(int index)
    {
        if (_pages.Count <= 1)
        {
            Log.Warning("[PageManager] 删除失败: 至少保留一页");
            return false;
        }
        if (index < 0 || index >= _pages.Count)
        {
            return false;
        }
        if (_pages[index].Type == "nav")
        {
            Log.Warning($"[PageManager] 删除失败: 导航页不可删除 (index={index})");
            return false;
        }
        _pages.RemoveAt(index);
        if (_currentPage >= _pages.Count)
        {
            _currentPage = _pages.Count - 1;
        }
        else if (_currentPage > index)
        {
            _currentPage--;
        }
        Save();
        Log.Info($"[PageManager] 已删除页面 index={index} 剩余{Count}页 当前页={_currentPage}");
        return true;
    }

    public List<JsonElement> GetPageItems(int index)
    {
        var page = GetPage(index);
        return page is { Type: "nav" } ? page.Items : new List<JsonElement>();
    }

    public void SetPageItems(int index, List<JsonElement> items)
    {
        if (index >= 0 && index < _pages.Count && _pages[index].Type == "nav")
        {
            Log.Info($"[PageManager] 更新导航项: index={index} {items.Count}项");
            _pages[index].Items = items;
            Save();
        }
    }
}
