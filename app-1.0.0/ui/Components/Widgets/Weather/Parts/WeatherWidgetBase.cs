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

// WeatherWidgetBase
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

// 天气三件套

/// <summary>天气组件公共基座: 显隐开关/缓存读取/SVG 图标 对齐原版 WeatherComponentBase</summary>
public abstract class WeatherWidgetBase : WidgetCardBase
{
    protected readonly DispatcherTimer RefreshTimer;

    // 图标随卡缩放 由各组件 Relayout 更新 刷新图标时沿用
    protected double LeadIconPx = 60;
    protected double SlotIconPx = 28;

    protected WeatherWidgetBase(ComponentDefinition definition) : base(definition)
    {
        // 原版 _update_interval: 10s/30s/1m/5m/10m/30m
        RefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ParseIntervalSeconds(Config.WeatherUpdateInterval.Value)) };
        RefreshTimer.Tick += (_, _) => RefreshFromCache();
        Config.WeatherUpdateInterval.ValueChanged += v =>
            RefreshTimer.Interval = TimeSpan.FromSeconds(ParseIntervalSeconds(v));
        Config.ShowWeather.ValueChanged += _ => RefreshFromCache();
        Config.WeatherTextColor.ValueChanged += _ => ApplyWeatherStyle();
        RefreshTimer.Start();
        ActualThemeVariantChanged += (_, _) => ApplyWeatherStyle();
        // 卡片底色/明暗变了 自有字色跟着重算
        AppearanceChanged += ApplyWeatherStyle;
    }

    // 原版 interval_map: "10s"=10s "30s"=30s "1m"=60s "5m"=300s "10m"=600s "30m"=1800s
    protected static double ParseIntervalSeconds(string value)
    {
        var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
        if (!double.TryParse(digits, out var num) || num <= 0)
        {
            return 300;
        }
        return value.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? num : num * 60;
    }

    protected void RefreshFromCache()
    {
        // 原版 _refresh_weather: showWeather=false 隐藏组件
        IsVisible = Config.ShowWeather.Value;
        if (!IsVisible)
        {
            return;
        }
        UpdateDisplay(AppUtils.GetCachedContent("weather", ignoreExpiry: true));
    }

    /// <summary>城市名: 保持顶行干净 不再拼接缓存标记</summary>
    protected string CityLabel() => Config.City.Value;

    protected abstract void UpdateDisplay(JsonElement? cached);

    // 原版 _parse_current: 温度四舍五入 + 天气码 全部经字符串转换 API 缓存里数值可能是字符串
    protected static (object Temp, int Code) ParseCurrent(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty("current", out var current))
        {
            return ("--", 0);
        }
        var raw = current.TryGetProperty("temperature", out var t) && t.TryGetProperty("value", out var tv)
            ? tv.ToString() : "--";
        object temp = double.TryParse(raw, out var d) ? (object)Math.Round(d) : (object)(raw.Length == 0 ? "--" : raw);
        var code = 0;
        if (current.TryGetProperty("weather", out var w) && !int.TryParse(w.ToString(), out code))
        {
            code = 0;
        }
        return (temp, code);
    }

    // 原版 _onCityLabelClicked: 点城市标签改名+坐标 立即重取天气
    protected void AttachCityClick(TextBlock cityText)
    {
        cityText.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        cityText.PointerReleased += async (_, _) =>
        {
            try
            {
                var owner = TopLevel.GetTopLevel(this) as Window;

                var name = await RegionSelectorDialog.ShowAsync(owner!, Config.City.Value);
                if (string.IsNullOrWhiteSpace(name) || name.Trim() == Config.City.Value)
                {
                    return;
                }
                name = name.Trim();
                Config.City.Value = name;
                var (lon, lat) = new RegionDatabase().GetCoordinates(name);
                if (lon is not null && lat is not null)
                {
                    Config.Longitude.Value = lon.Value;
                    Config.Latitude.Value = lat.Value;
                }
                Log.Info($"选择城市: {name} (经纬度: {lon} {lat})");
                cityText.Text = name;
                var data = await WeatherService.FetchAllAsync();
                if (data is { } d)
                {
                    AppUtils.SaveCache("weather", d, Config.WeatherUpdateInterval.Value);
                    UpdateDisplay(d);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[WX] 新城市天气获取失败: {e.Message}");
            }
        };
    }

    // SVG 天气图标 对齐原版 render_svg_icon
    // 注意: Svg 构造参数是 baseUri, 不会自动加载文件, 必须显式赋 SvgSource (探针实测 Path/加载行为)
    protected static Avalonia.Svg.Skia.SvgSource? MakeSvgSource(string? iconName)
    {
        if (string.IsNullOrEmpty(iconName))
        {
            return null;
        }
        try
        {
            var path = WeatherMaps.GetWeatherIconPath(iconName);
            if (!File.Exists(path))
            {
                return null;
            }
            return Avalonia.Svg.Skia.SvgSource.Load(path, null);
        }
        catch (Exception e)
        {

            Log.Debug($"[SW] 求值失败: {e.Message}");
            return null;
        }
    }

    protected static Avalonia.Svg.Skia.Svg? MakeSvgIcon(string? iconName, double size)
    {
        var source = MakeSvgSource(iconName);
        if (source is null)
        {
            return null;
        }
        var path = WeatherMaps.GetWeatherIconPath(iconName!);
        return new Avalonia.Svg.Skia.Svg(new Uri(path)) { Width = size, Height = size, SvgSource = source };
    }

    protected abstract void ApplyWeatherStyle();
}
