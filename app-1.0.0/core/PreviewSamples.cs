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

// PreviewSamples
using System.Text.Json;
using System.Text.Json.Nodes;
using Glimpseon.Core.Services;

namespace Glimpseon.Core;

public static class PreviewSamples
{
    private static readonly AsyncLocal<int> Depth = new();

    public static bool Active => Depth.Value > 0;

    public static IDisposable Begin()
    {
        Depth.Value++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
            {
                return;
            }
            _done = true;
            Depth.Value = Math.Max(0, Depth.Value - 1);
        }
    }

    public static JsonElement? ConfigFor(string componentId)
    {
        if (!Active)
        {
            return null;
        }
        var today = DateTime.Today;
        return componentId switch
        {
            "countdown_event" => JsonSerializer.SerializeToElement(new
            {
                event_name = "期末考试",
                target_time = today.AddDays(40).ToString("yyyy-MM-dd") + " 09:00",
            }),
            "countdown_days" => JsonSerializer.SerializeToElement(new
            {
                event_name = "高考",
                target_date = today.AddDays(190).ToString("yyyy-MM-dd"),
                title_bg_color = "#F98E1B",
            }),
            "school_info_class_info" => Json("""
                {"school":"阳光中学","class":"高二 (3) 班","count":"42 人","slogan":"无限进步 向阳生长",
                 "class_size":20,"school_size":14,"count_size":14,"slogan_size":11}
                """),
            _ => null,
        };
    }

    public static bool TryGetCache(string name, out JsonElement content)
    {
        if (!Active)
        {
            content = default;
            return false;
        }
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        switch (name)
        {
            case "weather":
                content = Weather;
                return true;
            case "daily_word":
                content = JsonSerializer.SerializeToElement(new
                {
                    date = today,
                    word = new
                    {
                        word = "perseverance",
                        phonetic = "/ˌpɜːsəˈvɪərəns/",
                        translation = "n. 毅力 坚持不懈",
                        examples = new[]
                        {
                            new
                            {
                                text = "Perseverance is the key to success.",
                                translation = "毅力是成功的关键。",
                            },
                        },
                    },
                });
                return true;
            case "daily_sentence":
                content = JsonSerializer.SerializeToElement(new
                {
                    date = today,
                    sentence = new
                    {
                        content = "A journey of a thousand miles begins with a single step.",
                        note = "千里之行 始于足下。",
                        translation = "千里之行 始于足下。",
                    },
                });
                return true;
            case "poetry":
                content = JsonSerializer.SerializeToElement("行到水穷处，坐看云起时。——王维《终南别业》");
                return true;
            case "history_today":
                content = JsonSerializer.SerializeToElement(new
                {
                    date = DateTime.Today.ToString("M月d日"),
                    events = new object[]
                    {
                        new { year = "1783", title = "人类首次乘热气球升空", link = "" },
                        new { year = "1877", title = "爱迪生宣布发明留声机", link = "" },
                        new { year = "1962", title = "中国对印自卫反击战结束", link = "" },
                        new { year = "1984", title = "中国第一座南极科考站奠基", link = "" },
                        new { year = "2013", title = "中国成功发射遥感卫星", link = "" },
                    },
                });
                return true;
            case "exchange_rates":
                content = Json("""
                    {"rates":{"USD":0.1412,"EUR":0.1305,"JPY":21.65,"HKD":1.0987,"GBP":0.1123}}
                    """);
                return true;
            case "news_baidu":
                content = News("百度热搜", "今日多地气温回升", "国产大模型发布新版本", "冬季用电负荷创新高", "城市地铁新线开通运营");
                return true;
            case "news_weibo":
                content = News("微博热搜", "校园歌手大赛决赛", "冷空气将影响北方", "年度电影票房揭晓", "高校寒假时间公布");
                return true;
            case "news_jinritoutiao":
                content = News("今日头条", "高校发布招生新政策", "新能源汽车销量增长", "冬季运动装备走俏", "人工智能应用加速落地");
                return true;
            case "news_tenxunwang":
                content = News("腾讯新闻", "城市更新项目启动", "数字人民币试点扩容", "全国羽毛球联赛开赛", "冬季旅游市场升温");
                return true;
            case "news_cctv":
                content = News("央视要闻", "推动高质量发展取得新成效", "冬春农田水利建设加快推进", "国际友好城市交流活动举行", "全国消费市场持续回暖");
                return true;
            default:
                content = default;
                return false;
        }
    }

    public const string StickyNoteText = "周五前交物理实验报告\n记得带《红楼梦》和画笔";

    public static JsonArray HomeworkSections() => (JsonArray)JsonNode.Parse("""
        [{"title":"语文","items":[
           {"text":"背诵《赤壁赋》全文","done":false},
           {"text":"完成文言文练习册 P42","done":true}]},
         {"title":"数学","items":[
           {"text":"订正周测卷 第 18-22 题","done":false},
           {"text":"预习导数应用","done":false}]},
         {"title":"英语","items":[
           {"text":"背诵 Unit 5 单词","done":true},
           {"text":"完成阅读训练两篇","done":false}]}]
        """)!;

    public static List<Announcement> AnnouncementItems() =>
    [
        new("本周五下午举行校园歌手大赛决赛 请各班准时到场", "2026-11-21 08:30"),
        new("冬季长跑活动下周开始 请同学们准备好运动装备", "2026-11-20 15:10"),
        new("图书馆新到一批科普读物 欢迎借阅", "2026-11-19 09:40"),
    ];

    public static MediaInfo Media => new()
    {
        Title = "晴天",
        Artist = "周杰伦",
        Album = "叶惠美",
        PlaybackStatus = "playing",
        PositionMs = 76000,
        DurationMs = 269000,
        IsPlaying = true,
        AppName = "网易云音乐",
        SongId = "186016",
    };

    private static JsonElement News(string prefix, params string[] titles)
    {
        var arr = new JsonArray();
        foreach (var title in titles)
        {
            arr.Add(new JsonObject
            {
                ["title"] = $"{prefix} · {title}",
                ["url"] = "https://example.com/news",
            });
        }
        return JsonSerializer.SerializeToElement(arr);
    }

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static readonly JsonElement Weather = Json("""
        {"current":{"temperature":{"value":23},"weather":1,"feelsLike":{"value":24},"humidity":{"value":56}},
         "forecastDaily":{
           "weather":{"value":[{"from":1},{"from":2},{"from":3},{"from":61},{"from":80}]},
           "aqi":{"value":[28,35,42,39,31]},
           "temperature":{"value":[{"from":26,"to":17},{"from":25,"to":16},{"from":24,"to":15},
                                  {"from":22,"to":14},{"from":21,"to":13}]}},
         "forecastHourly":{
           "temperature":{"value":[18,19,20,22,23,23,21,20],"unit":"℃"},
           "weather":{"value":[{"day":1},{"day":1},{"day":2},{"day":2},{"day":3},{"day":3},{"day":61},{"day":61}]}}}
        """);
}
