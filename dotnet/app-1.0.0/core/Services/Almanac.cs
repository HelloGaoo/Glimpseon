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

// 黄历服务
using System.Globalization;
using Lunar;

namespace Glimpseon.Core.Services;

public sealed class AlmanacData
{
    public string Date { get; init; } = "";
    public string LunarMonth { get; init; } = "";
    public string LunarDay { get; init; } = "";
    public string YearGz { get; init; } = "";
    public string MonthGz { get; init; } = "";
    public string DayGz { get; init; } = "";
    public string Zodiac { get; init; } = "";
    public string SolarTerm { get; init; } = "";
    public string Holiday { get; init; } = "";
    public string[] Good { get; init; } = Array.Empty<string>();
    public string[] Bad { get; init; } = Array.Empty<string>();
}

public static class AlmanacService
{
    private static readonly Dictionary<string, AlmanacData?> Cache = new();
    private static readonly string[] LunarMonths =
    {
        "正月", "二月", "三月", "四月", "五月", "六月",
        "七月", "八月", "九月", "十月", "冬月", "腊月",
    };
    private static readonly string[] LunarDays =
    {
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十",
    };
    private static readonly string[] Zodiacs =
    {
        "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪",
    };

    public static AlmanacData? GetToday(DateTime? day = null)
    {
        var date = day?.Date ?? DateTime.Today;
        var key = date.ToString("yyyy-MM-dd");
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        AlmanacData? data = null;
        try
        {
            var solar = Solar.FromYmdHms(date.Year, date.Month, date.Day);
            var lunar = solar.Lunar;
            var holidays = new List<string>();
            holidays.AddRange(solar.Festivals);
            holidays.AddRange(lunar.Festivals);
            holidays.AddRange(lunar.OtherFestivals);
            data = new AlmanacData
            {
                Date = key,
                LunarMonth = lunar.MonthInChinese + "月",
                LunarDay = lunar.DayInChinese,
                YearGz = lunar.YearInGanZhi,
                MonthGz = lunar.MonthInGanZhi,
                DayGz = lunar.DayInGanZhi,
                Zodiac = lunar.YearShengXiao,
                SolarTerm = lunar.JieQi,
                Holiday = string.Join(",", holidays.Where(h => h.Length > 0)),
                Good = lunar.DayYi.ToArray(),
                Bad = lunar.DayJi.ToArray(),
            };
            Log.Debug($"黄历数据 {key} 宜{data.Good.Length} 忌{data.Bad.Length}");
        }
        catch (Exception e)
        {
            Log.Error($"黄历数据生成失败 {e.Message}");
        }
        Cache[key] = data;
        return data;
    }
}
