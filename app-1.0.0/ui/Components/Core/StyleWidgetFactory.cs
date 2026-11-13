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

// StyleWidgetFactory
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

// StyleWidgets.cs

/// <summary>
/// 样式组件工厂: 按 type_style 生成真身控件 返回 null 走占位兜底
/// </summary>
public static class StyleWidgetFactory
{
    public static DraggableContainer? Create(string typeId, ComponentDefinition definition, JsonElement? config)
    {
        // 插件工厂前置
        var pluginWidget = PluginService.CreatePluginWidget(typeId, definition, config);
        if (pluginWidget is not null)
        {
            return pluginWidget;
        }
        DraggableContainer? widget = typeId switch
        {
            "clock_digital" => new DigitalClockWidget(definition, config),
            "clock_square_1" => new NativeSquareClock1Widget(definition),
            "clock_square_2" => new NativeSquareClock2Widget(definition, config),
            "clock_calendar_month" => new CalendarMonthWidget(definition),
            "clock_calendar_mini" => new NativeMiniCalendarWidget(definition),
            "clock_almanac" => new NativeAlmanacWidget(definition),
            "poetry_one_line" => new PoetryWidget(definition),
            "weather_icon_temp" => new WeatherCurrentWidget(definition, config),
            "weather_hourly" => new WeatherHourlyWidget(definition),
            "weather_weekly" => new WeatherWeeklyWidget(definition),
            "countdown_event" => new CountdownEventWidget(definition, config),
            "countdown_days" => new CountdownDaysWidget(definition, config),
            "school_info_class_info" => new SchoolInfoWidget(definition, config),
            "announcement_board" => new NativeAnnouncementWidget(definition),
            "media_player" => new MediaPlayerWidget(definition),
            "news_baidu" or "news_weibo" or "news_jinritoutiao" or "news_tenxunwang" =>
                new NewsWidget(definition, typeId["news_".Length..]),
            "news_xcvts" => new NewsWidget(definition, "cctv"),
            "history_today" => new HistoryTodayWidget(definition),
            "word_daily" => new NativeDailyWordWidget(definition),
            "sentence_daily" => new NativeDailySentenceWidget(definition),
            "linkage_timetable_preview" => new TimetablePreviewWidget(definition),
            "linkage_timetable_nowlesson" => new TimetableNowLessonWidget(definition, config),
            "linkage_timetable_timeline" => new NativeTimetableTimelineWidget(definition),
            "homework_board" => new NativeHomeworkBoardWidget(definition),
            "class_album_horizontal" => new ClassAlbumWidget(definition, vertical: false),
            "class_album_vertical" => new ClassAlbumWidget(definition, vertical: true),
            "Math_calculator" => new CalculatorWidget(definition),
            "sticky_note" => new StickyNoteWidget(definition, config),
            "ai_chat" => new AiChatWidget(definition, config),
            "quick_launch_dock" => new QuickLaunchDockWidget(definition),
            "quick_launch_grid" => new QuickLaunchGridWidget(definition),
            "timer_countdown" => new TimerCountdownWidget(definition),
            "system_performance" => new PerformanceMonitorWidget(definition),
            "system_netspeed" => new NativeNetworkSpeedWidget(definition),
            "study_meter" => new NativeDecibelMeterWidget(definition),
            _ => null,
        };
        // 组件私有配置注入 通用外观覆盖由此生效
        if (widget is WidgetCardBase cardWidget)
        {
            cardWidget.ApplyComponentConfig(config);
        }
        return widget;
    }
}
