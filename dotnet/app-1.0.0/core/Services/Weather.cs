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

// 天气服务
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Glimpseon.Core.Services;

public static class WeatherApi
{
    public const string Url = "https://weatherapi.market.xiaomi.com/wtr-v3/weather/all";
    public const string AppKey = "weather20151024";
    public const string Sign = "zUFJoAR2ZVrDy1vF3D07";
}

public sealed class WeatherHour
{
    public object Temp { get; init; } = "--";
    public int WeatherCode { get; init; }
    public string Icon { get; init; } = "2.svg";
}

public sealed class WeatherHourly
{
    public List<WeatherHour> Hours { get; init; } = new();
    public string Unit { get; init; } = "℃";
    public string PubTime { get; init; } = "";
}

public sealed class WeatherDay
{
    public int WeatherCode { get; init; }
    public string Icon { get; init; } = "2.svg";
    public string High { get; init; } = "--";
    public string Low { get; init; } = "--";
}

public sealed class WeatherDaily
{
    public List<WeatherDay> Days { get; init; } = new();
}

public static class WeatherMaps
{
    // 天气代码 > 图标 
    public static readonly IReadOnlyDictionary<int, string> IconMap = new Dictionary<int, string>
    {
        [0] = "0.svg", [1] = "1.svg", [2] = "2.svg", [3] = "7.svg", [4] = "4.svg",
        [5] = "5.svg", [6] = "19.svg", [7] = "7.svg", [8] = "8.svg", [9] = "9.svg",
        [10] = "10.svg", [11] = "11.svg", [12] = "11.svg", [13] = "14.svg", [14] = "14.svg",
        [15] = "15.svg", [16] = "16.svg", [17] = "17.svg", [18] = "18.svg", [19] = "19.svg",
        [20] = "20.svg", [21] = "7.svg", [22] = "8.svg", [23] = "9.svg", [24] = "10.svg",
        [25] = "11.svg", [26] = "14.svg", [27] = "15.svg", [28] = "16.svg", [29] = "18.svg",
        [30] = "20.svg", [31] = "20.svg", [32] = "3.svg", [33] = "3.svg", [34] = "16.svg",
        [35] = "18.svg", [50] = "0.svg", [51] = "1.svg", [52] = "2.svg", [53] = "18.svg",
        [54] = "7.svg", [55] = "8.svg", [56] = "9.svg", [57] = "10.svg", [58] = "4.svg",
        [59] = "5.svg", [60] = "14.svg", [61] = "15.svg", [62] = "16.svg", [63] = "18.svg",
        [64] = "18.svg", [65] = "18.svg", [66] = "3.svg", [67] = "3.svg", [68] = "11.svg",
        [69] = "17.svg", [70] = "19.svg", [71] = "19.svg", [72] = "18.svg", [73] = "18.svg",
        [74] = "20.svg", [75] = "20.svg", [76] = "18.svg", [77] = "20.svg", [99] = "0.svg",
    };

    // 天气代码 > 翻译键
    public static readonly IReadOnlyDictionary<int, string> TextMap = new Dictionary<int, string>
    {
        [0] = "weather.sunny", [1] = "weather.cloudy", [2] = "weather.overcast", [3] = "weather.shower", [4] = "weather.thundershower",
        [5] = "weather.thundershower_with_hail", [6] = "weather.sleet", [7] = "weather.light_rain", [8] = "weather.moderate_rain",
        [9] = "weather.heavy_rain", [10] = "weather.rainstorm", [11] = "weather.heavy_rainstorm", [12] = "weather.extreme_rainstorm",
        [13] = "weather.snow_flurry", [14] = "weather.light_snow", [15] = "weather.moderate_snow", [16] = "weather.heavy_snow", [17] = "weather.snowstorm",
        [18] = "weather.fog", [19] = "weather.freezing_rain", [20] = "weather.sandstorm",
        [29] = "weather.dust", [30] = "weather.sand", [31] = "weather.strong_sandstorm",
        [32] = "weather.squall", [33] = "weather.tornado", [34] = "weather.weak_blowing_snow", [35] = "weather.light_fog",
        [53] = "weather.haze",
        [99] = "weather.unknown",
    };

    // 组合翻译键
    public static readonly IReadOnlyDictionary<int, (string K1, string K2)> CombinedTextMap = new Dictionary<int, (string, string)>
    {
        [21] = ("weather.light_rain", "weather.moderate_rain"),
        [22] = ("weather.moderate_rain", "weather.heavy_rain"),
        [23] = ("weather.heavy_rain", "weather.rainstorm"),
        [24] = ("weather.rainstorm", "weather.heavy_rainstorm"),
        [25] = ("weather.heavy_rainstorm", "weather.extreme_rainstorm"),
        [26] = ("weather.light_snow", "weather.moderate_snow"),
        [27] = ("weather.moderate_snow", "weather.heavy_snow"),
        [28] = ("weather.heavy_snow", "weather.snowstorm"),
    };

    // 夜间代码 > 白天翻译键
    public static readonly IReadOnlyDictionary<int, string> NightMap = new Dictionary<int, string>
    {
        [50] = "weather.sunny", [51] = "weather.cloudy", [52] = "weather.overcast",
        [54] = "weather.light_rain", [55] = "weather.moderate_rain", [56] = "weather.heavy_rain", [57] = "weather.rainstorm",
        [58] = "weather.thundershower", [59] = "weather.hail", [60] = "weather.light_snow", [61] = "weather.moderate_snow",
        [62] = "weather.heavy_snow", [63] = "weather.fog", [64] = "weather.haze", [65] = "weather.sand_dust",
        [66] = "weather.strong_wind", [67] = "weather.typhoon", [68] = "weather.rainstorm", [69] = "weather.snowstorm",
        [70] = "weather.sleet", [71] = "weather.freezing_rain", [72] = "weather.rime", [73] = "weather.frost",
        [74] = "weather.sandstorm", [75] = "weather.sand", [76] = "weather.dust", [77] = "weather.strong_sandstorm",
    };

    public static string GetWeatherText(int code)
    {
        if (TextMap.TryGetValue(code, out var key))
        {
            return AppUtils.Tr(key);
        }
        if (CombinedTextMap.TryGetValue(code, out var pair))
        {
            return $"{AppUtils.Tr(pair.K1)} - {AppUtils.Tr(pair.K2)}";
        }
        if (NightMap.TryGetValue(code, out var nightKey))
        {
            return $"{AppUtils.Tr(nightKey)}({AppUtils.Tr("weather.night")})";
        }
        return AppUtils.Tr("weather.unknown");
    }

    public static string GetIcon(int code) => IconMap.GetValueOrDefault(code, "2.svg");

    public static string GetWeatherIconPath(string iconName) =>
        Paths.GetResourcePath(Path.Combine("Assets", "icons", "weather", iconName));
}

public static class WeatherService
{
    public static async Task<JsonElement?> FetchAllAsync()
    {
        try
        {
            var lat = Config.Latitude.Value != 0 ? Config.Latitude.Value : 39.9042;
            var lon = Config.Longitude.Value != 0 ? Config.Longitude.Value : 116.4074;
            if (Config.Latitude.Value == 0 || Config.Longitude.Value == 0)
            {
                Log.Info("经纬度未配置 用默认坐标 (北京)");
            }
            var url = $"{WeatherApi.Url}?appKey={WeatherApi.AppKey}&sign={WeatherApi.Sign}&isGlobal=False&locale=zh_cn&latitude={lat}&longitude={lon}";
            Log.Info($"请求天气API 经纬度 {lat} {lon}");
            var data = await AppUtils.GetJsonAsync(url);
            if (data is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("current", out _))
            {
                Log.Error("天气返回不完整");
                return null;
            }
            Log.Info("天气已获取");
            return root;
        }
        catch (Exception e)
        {
            Log.Error($"天气 api 请求异常 {e.Message}");
            return null;
        }
    }

    public static WeatherHourly? ParseHourly(JsonElement? hourlyElement)
    {
        if (hourlyElement is not { ValueKind: JsonValueKind.Object } hourly)
        {
            Log.Warning("小时预报缺失字段 forecastHourly");
            return null;
        }
        var temps = hourly.TryGetProperty("temperature", out var t) ? t : default;
        var weathers = hourly.TryGetProperty("weather", out var w) ? w : default;
        var tempValues = temps.ValueKind == JsonValueKind.Object && temps.TryGetProperty("value", out var tv) && tv.ValueKind == JsonValueKind.Array ? tv : default;
        var weatherValues = weathers.ValueKind == JsonValueKind.Object && weathers.TryGetProperty("value", out var wv) && wv.ValueKind == JsonValueKind.Array ? wv : default;
        var unit = temps.ValueKind == JsonValueKind.Object && temps.TryGetProperty("unit", out var u) ? u.GetString() ?? "℃" : "℃";
        var pubTime = temps.ValueKind == JsonValueKind.Object && temps.TryGetProperty("pubTime", out var p) ? p.GetString() ?? "" : "";

        var hours = new List<WeatherHour>();
        if (tempValues.ValueKind == JsonValueKind.Array)
        {
            var count = Math.Min(24, tempValues.GetArrayLength());
            for (var i = 0; i < count; i++)
            {
                var code = 0;
                if (weatherValues.ValueKind == JsonValueKind.Array && i < weatherValues.GetArrayLength())
                {
                    var val = weatherValues[i];
                    if (val.ValueKind == JsonValueKind.Object)
                    {
                        var dayVal = val.TryGetProperty("day", out var day) ? day : val.TryGetProperty("night", out var night) ? night : default;
                        int.TryParse(dayVal.ToString(), out code);
                    }
                    else if (val.ValueKind == JsonValueKind.Array && val.GetArrayLength() > 0)
                    {
                        int.TryParse(val[0].ToString(), out code);
                    }
                    else if (!int.TryParse(val.ToString(), out code))
                    {
                        Log.Debug($"小时天气代码类型转换失败 第{i}小时 按 0 处理");
                        code = 0;
                    }
                }
                hours.Add(new WeatherHour
                {
                    Temp = tempValues[i].ToString(),
                    WeatherCode = code,
                    Icon = WeatherMaps.GetIcon(code),
                });
            }
        }
        else
        {
            Log.Warning("小时温度数据为空 解析结果将为空列表");
        }
        Log.Info($"小时预报已解析 {hours.Count}小时 单位 {unit} 发布 {pubTime}");
        return new WeatherHourly { Hours = hours, Unit = unit, PubTime = pubTime };
    }

    public static WeatherDaily? ParseDaily(JsonElement? dailyElement)
    {
        if (dailyElement is not { ValueKind: JsonValueKind.Object } daily)
        {
            Log.Warning("每日预报缺失字段 forecastDaily");
            return null;
        }
        var weatherValues = daily.TryGetProperty("weather", out var w) && w.TryGetProperty("value", out var wv) && wv.ValueKind == JsonValueKind.Array ? wv : default;
        var tempValues = daily.TryGetProperty("temperature", out var t) && t.TryGetProperty("value", out var tv) && tv.ValueKind == JsonValueKind.Array ? tv : default;

        var days = new List<WeatherDay>();
        if (tempValues.ValueKind == JsonValueKind.Array)
        {
            var count = Math.Min(15, tempValues.GetArrayLength());
            for (var i = 0; i < count; i++)
            {
                var code = 0;
                if (weatherValues.ValueKind == JsonValueKind.Array && i < weatherValues.GetArrayLength())
                {
                    var val = weatherValues[i];
                    if (val.ValueKind == JsonValueKind.Object)
                    {
                        var fromVal = val.TryGetProperty("from", out var from) ? from : val.TryGetProperty("day", out var day) ? day : val.TryGetProperty("night", out var night) ? night : default;
                        int.TryParse(fromVal.ToString(), out code);
                    }
                    else if (val.ValueKind == JsonValueKind.Array && val.GetArrayLength() > 0)
                    {
                        int.TryParse(val[0].ToString(), out code);
                    }
                    else if (!int.TryParse(val.ToString(), out code))
                    {
                        Log.Debug($"每日天气代码类型转换失败 第{i}天 按 0 处理");
                        code = 0;
                    }
                }

                var high = "--";
                var low = "--";
                if (tempValues.ValueKind == JsonValueKind.Array && i < tempValues.GetArrayLength())
                {
                    var val = tempValues[i];
                    if (val.ValueKind == JsonValueKind.Object)
                    {
                        var fromVal = val.TryGetProperty("from", out var from) ? from.ToString() : val.TryGetProperty("value", out var v) ? v.ToString() : "--";
                        var toVal = val.TryGetProperty("to", out var to) ? to.ToString() : val.TryGetProperty("value", out var v2) ? v2.ToString() : "--";
                        if (double.TryParse(fromVal, out var fn) && double.TryParse(toVal, out var tn))
                        {
                            high = Math.Max(fn, tn).ToString();
                            low = Math.Min(fn, tn).ToString();
                        }
                        else
                        {
                            Log.Warning($"每日温度转换失败 第{i}天 用原始文本");
                            high = fromVal;
                            low = toVal;
                        }
                    }
                    else if (val.ValueKind == JsonValueKind.Array)
                    {
                        low = val.GetArrayLength() > 0 ? val[0].ToString() : "--";
                        high = val.GetArrayLength() > 1 ? val[1].ToString() : val.GetArrayLength() > 0 ? val[0].ToString() : "--";
                    }
                    else
                    {
                        high = val.ToString();
                        low = val.ToString();
                    }
                }
                days.Add(new WeatherDay { WeatherCode = code, Icon = WeatherMaps.GetIcon(code), High = high, Low = low });
            }
        }
        else
        {
            Log.Warning("每日温度数据为空");
        }
        Log.Info($"每日预报已解析 {days.Count}天");
        return new WeatherDaily { Days = days };
    }
}

// 地区数据库 city.db SQLite
public sealed class RegionDatabase
{
    private readonly string _dbPath = Paths.GetResourcePath(Constants.ResourceCityDb);

    public List<string> Search(string? keyword)
    {
        var names = new List<string>();
        try
        {
            if (!File.Exists(_dbPath))
            {
                Log.Warning($"地区数据库文件不存在 {_dbPath}");
                return names;
            }
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            if (string.IsNullOrWhiteSpace(keyword))
            {
                cmd.CommandText = "SELECT name FROM regions";
            }
            else
            {
                cmd.CommandText = "SELECT name FROM regions WHERE name LIKE $kw";
                cmd.Parameters.AddWithValue("$kw", "%" + keyword.Trim() + "%");
            }
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            Log.Debug($"搜索地区 关键词={keyword} 命中{names.Count}条");
        }
        catch (Exception e)
        {
            Log.Error($"搜索地区出错 {e.Message}");
        }
        return names;
    }

    public (double? Lon, double? Lat) GetCoordinates(string regionName)
    {
        try
        {
            if (!File.Exists(_dbPath))
            {
                Log.Warning($"地区数据库文件不存在 {_dbPath}");
                return (null, null);
            }
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT longitude, latitude FROM regions WHERE name = $name";
            cmd.Parameters.AddWithValue("$name", regionName);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var lon = reader.IsDBNull(0) ? (double?)null : reader.GetDouble(0);
                var lat = reader.IsDBNull(1) ? (double?)null : reader.GetDouble(1);
                Log.Debug($"区域坐标查询命中 {regionName} -> 经度 {lon} 纬度 {lat}");
                return (lon, lat);
            }
            Log.Debug($"区域坐标查询未命中 {regionName}");
            return (null, null);
        }
        catch (Exception e)
        {
            Log.Error($"获取经纬度失败 {e.Message}");
            return (null, null);
        }
    }
}
