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

// TimetableNowLessonWidget
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

// 当前课程
// 规格: 400x200 边距(16,10,16,8) 左右栏 间距14 左列(3:1:1) 课程名40px w600(上20下4)
// 倒计时18px accent 时间进度15px 右列(间距8) 老师/时间段19px w600 下节课19px sub色
// 底部进度条6px 配置 show_teacher/show_next/show_duration/show_countdown/prepare_minutes(3)

public sealed class TimetableNowLessonWidget : WidgetCardBase
{
    private readonly TextBlock _subjectText = new();
    // 主题前景 供科目色回落
    private IBrush? _subjectFg;
    private readonly TextBlock _countdownText = new();
    private readonly TextBlock _timeProgressText = new();
    private readonly TextBlock _teacherText = new();
    private readonly TextBlock _timeText = new();
    private readonly TextBlock _nextText = new();
    private readonly ProgressBar _bottomProgress = new();
    private readonly DispatcherTimer _timer;
    private readonly Grid _main = new() { ColumnSpacing = 16 };
    private readonly Grid _left = new() { RowSpacing = WidgetSpace.GapXs, MinWidth = 100 };
    private readonly Grid _right = new() { RowSpacing = WidgetSpace.Gap };

    private bool _showTeacher = true;
    private bool _showNext = true;
    private bool _showDuration = true;
    private bool _showCountdown = true;
    private int _prepareMinutes = 3;
    private bool _prepareFromConfig;
    private int _fontScale = 100;

    public TimetableNowLessonWidget(ComponentDefinition definition, JsonElement? config) : base(definition)
    {
        var root = new Grid
        {
            Margin = new Thickness(WidgetSpace.Pad, WidgetSpace.PadCompact, WidgetSpace.Pad, WidgetSpace.PadCompact),
            RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,Auto"),
        };
        _main.ColumnDefinitions = Avalonia.Controls.ColumnDefinitions.Parse("*,*");

        // 左: 课程名(3) 倒计时(1) 时间进度(1) 原版均 AlignLeft|AlignVCenter
        _left.RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("3*,*,*");
        _subjectText.Margin = new Thickness(0, 20, 0, 4);
        _subjectText.TextWrapping = TextWrapping.Wrap;
        _subjectText.VerticalAlignment = VerticalAlignment.Center;
        _countdownText.Margin = new Thickness(4, 2, 0, 2);
        _countdownText.VerticalAlignment = VerticalAlignment.Center;
        _timeProgressText.Margin = new Thickness(4, 2, 0, 2);
        _timeProgressText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_subjectText, 0);
        Grid.SetRow(_countdownText, 1);
        Grid.SetRow(_timeProgressText, 2);
        _left.Children.Add(_subjectText);
        _left.Children.Add(_countdownText);
        _left.Children.Add(_timeProgressText);

        // 右: 老师 时间段 下节课 + 尾部留白(原版 addStretch) -> 4 等分行 内容垂直居中
        _right.RowDefinitions = Avalonia.Controls.RowDefinitions.Parse("*,*,*,*");
        _teacherText.Margin = new Thickness(0, 2, 0, 2);
        _teacherText.VerticalAlignment = VerticalAlignment.Center;
        _timeText.Margin = new Thickness(0, 2, 0, 2);
        _timeText.VerticalAlignment = VerticalAlignment.Center;
        _nextText.Margin = new Thickness(0, 2, 0, 2);
        _nextText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_teacherText, 0);
        Grid.SetRow(_timeText, 1);
        Grid.SetRow(_nextText, 2);
        _right.Children.Add(_teacherText);
        _right.Children.Add(_timeText);
        _right.Children.Add(_nextText);

        Grid.SetColumn(_left, 0);
        Grid.SetColumn(_right, 1);
        _main.Children.Add(_left);
        _main.Children.Add(_right);

        _bottomProgress.Height = 4;
        _bottomProgress.CornerRadius = new CornerRadius(2);
        _bottomProgress.Minimum = 0;
        _bottomProgress.Maximum = 100;
        _bottomProgress.Value = 0;
        Grid.SetRow(_main, 0);
        Grid.SetRow(_bottomProgress, 1);
        root.Children.Add(_main);
        root.Children.Add(_bottomProgress);
        Card.Child = root;
        Content = Card;

        ReadConfig(config);
        ApplyCardStyle();
        ApplyStyle();
        ActualThemeVariantChanged += (_, _) => ApplyStyle();
        SizeChanged += (_, _) => Relayout();

        // 原版 1s 起步 正常态 5s
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    private void ReadConfig(JsonElement? config)
    {
        ReadBgOverride(config);
        if (config is not { ValueKind: JsonValueKind.Object } obj)
        {
            return;
        }
        if (obj.TryGetProperty("show_teacher", out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _showTeacher = v.GetBoolean();
        }
        if (obj.TryGetProperty("show_next", out var v2) && v2.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _showNext = v2.GetBoolean();
        }
        if (obj.TryGetProperty("show_duration", out var v3) && v3.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _showDuration = v3.GetBoolean();
        }
        if (obj.TryGetProperty("show_countdown", out var v4) && v4.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            _showCountdown = v4.GetBoolean();
        }
        if (obj.TryGetProperty("prepare_minutes", out var p) && p.ValueKind == JsonValueKind.Number)
        {
            _prepareMinutes = p.GetInt32();
            _prepareFromConfig = true;
        }
        if (obj.TryGetProperty("font_scale", out var f) && f.ValueKind == JsonValueKind.Number)
        {
            _fontScale = f.GetInt32();
        }
    }

    private void Refresh()
    {
        var schedule = TimetableScheduleProvider.GetTodaySchedule();
        var now = AppUtils.PreciseNow();
        // 控件值优先 回落档案
        if (!_prepareFromConfig && TimetableScheduleProvider.GetProfilePrepareMinutes() is { } fromProfile)
        {
            _prepareMinutes = fromProfile;
        }

        ScheduleRow? currentRow = null;
        ScheduleRow? nextRow = null;
        var foundCurrent = false;
        foreach (var row in schedule)
        {
            if (row.IsCurrent)
            {
                currentRow = row;
                foundCurrent = true;
            }
            else if (foundCurrent && !row.IsBreak)
            {
                nextRow = row;
                break;
            }
        }

        // 课前播报: 课间中 且 距下节课开始 <= prepare_minutes
        ScheduleRow? prepareRow = null;
        if (currentRow is { IsBreak: true })
        {
            prepareRow = nextRow;
        }
        var inPrepare = false;
        if (prepareRow is not null && TimeOnly.TryParse(prepareRow.StartTime, out var pst))
        {
            var nextStart = now.Date.AddHours(pst.Hour).AddMinutes(pst.Minute);
            var diff = (nextStart - now).TotalSeconds;
            if (diff > 0 && diff <= _prepareMinutes * 60)
            {
                inPrepare = true;
            }
        }

        // 原版: 课前播报 1s 正常 5s
        _timer.Interval = TimeSpan.FromSeconds(inPrepare ? 1 : 5);

        if (inPrepare)
        {
            RenderPrepare(prepareRow!, now);
        }
        else if (currentRow is not null)
        {
            RenderNormal(currentRow, nextRow, now);
        }
        else
        {
            _subjectText.Text = AppUtils.Tr("timetable.day_empty");
            _countdownText.Text = "";
            _teacherText.Text = "";
            _timeText.Text = "";
            _nextText.Text = "下节课 --";
            _timeProgressText.Text = "";
            _bottomProgress.Value = 0;
        }
        FitSubject();
    }

    // 断点 + 字号: 窄卡收右栏 科目按左列宽收敛 其余按卡高分档
    private void Relayout()
    {
        var w = Bounds.Width - 32;
        var h = Bounds.Height - 18;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        var narrow = w < 300;
        _right.IsVisible = !narrow;
        Grid.SetColumnSpan(_left, narrow ? 2 : 1);
        _timeProgressText.IsVisible = h >= 96;

        var scale = _fontScale / 100.0;
        _countdownText.FontSize = CardText.ByHeight(h, 0.11 * scale, WidgetType.Label, 18);
        _timeProgressText.FontSize = CardText.ByHeight(h, 0.095 * scale, WidgetType.Micro, WidgetType.Title);
        var sideSize = CardText.ByHeight(h / 4.0, 0.48 * scale, WidgetType.Micro, WidgetType.Title);
        _teacherText.FontSize = sideSize;
        _timeText.FontSize = sideSize;
        _nextText.FontSize = sideSize;
        _subjectText.Margin = new Thickness(0, h < 110 ? 0 : WidgetSpace.Gap, 0, WidgetSpace.GapXs);

        FitSubject();
    }

    // 科目名单行收敛 优先占满左列
    private void FitSubject()
    {
        var w = Bounds.Width - 32;
        if (w <= 1)
        {
            return;
        }
        var leftW = (w < 300 ? w : (w - 14) / 2) - 8;
        var scale = _fontScale / 100.0;
        _subjectText.FontSize = CardText.Fit(_subjectText.Text, Common.AppFontFamily, WidgetWeight.Title,
            Math.Max(40, leftW), 40 * scale, 14, 96);
    }

    // 切页隐藏即停表 回页补刷
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || _timer is null)
        {
            return;
        }
        if (!IsVisible)
        {
            _timer.Stop();
        }
        else if (!_timer.IsEnabled)
        {
            Refresh();
            _timer.Start();
        }
    }

    // 课间态
    private void RenderPrepare(ScheduleRow prepare, DateTime now)
    {
        _subjectText.Text = prepare.Subject.Length > 0 ? prepare.Subject : "下节课";
        ApplySubjectColor(_subjectText.Text);

        if (_showCountdown && TimeOnly.TryParse(prepare.StartTime, out var st))
        {
            var nextStart = now.Date.AddHours(st.Hour).AddMinutes(st.Minute);
            var totalSec = Math.Max(0, (int)(nextStart - now).TotalSeconds);
            _countdownText.Text = $"{totalSec / 60}:{totalSec % 60:D2} 后上课";
        }
        else
        {
            _countdownText.Text = "";
        }

        _timeProgressText.Text = "";
        _teacherText.Text = _showTeacher ? TeacherDisplay(prepare.Teacher) : "";
        _timeText.Text = $"{prepare.StartTime}~{prepare.EndTime}";

        if (_showDuration && TimeOnly.TryParse(prepare.StartTime, out var s) && TimeOnly.TryParse(prepare.EndTime, out var e))
        {
            var totalMin = (e.Hour * 60 + e.Minute) - (s.Hour * 60 + s.Minute);
            _nextText.Text = $"时长 {totalMin}分钟";
        }
        else
        {
            _nextText.Text = "";
        }

        if (TimeOnly.TryParse(prepare.StartTime, out var ps))
        {
            var diff = (now.Date.AddHours(ps.Hour).AddMinutes(ps.Minute) - now).TotalSeconds;
            var totalPrepare = _prepareMinutes * 60;
            _bottomProgress.Value = totalPrepare > 0
                ? Math.Clamp((1 - diff / totalPrepare) * 100, 0, 100)
                : 0;
        }
        else
        {
            _bottomProgress.Value = 0;
        }
    }

    // 命中覆色 否则还原主题刷子
    private void ApplySubjectColor(string subject)
    {
        if (TimetableScheduleProvider.GetSubjectColor(subject) is { } hex)
        {
            try
            {
                _subjectText.Foreground = new SolidColorBrush(Color.Parse(hex));
                return;
            }
            catch (Exception e)
            {
                Log.Debug($"[课表组件] 科目颜色解析失败: {hex} {e.Message}");
            }
        }
        if (_subjectFg is not null)
        {
            _subjectText.Foreground = _subjectFg;
        }
    }

    // 上课态
    private void RenderNormal(ScheduleRow current, ScheduleRow? nextRow, DateTime now)
    {
        _countdownText.Text = "";
        // 考试/假期 整日占位 不按课间渲染
        var wholeDay = current.Mark is ScheduleMark.Exam or ScheduleMark.Holiday;
        _subjectText.Text = current.IsBreak
            ? (current.BreakName.Length > 0 ? current.BreakName : "课间休息")
            : (current.Subject.Length > 0 ? current.Subject : "--");

        _teacherText.Text = _showTeacher
            ? (current.IsBreak && !wholeDay ? "课间休息" : TeacherDisplay(current.Teacher))
            : "";
        var week = TimetableScheduleProvider.GetWeekLabel();
        _timeText.Text = wholeDay
            ? week ?? ""
            : $"{current.StartTime}~{current.EndTime}" + (week is null ? "" : $" · {week}");
        if (!current.IsBreak)
        {
            ApplySubjectColor(_subjectText.Text);
        }

        if (_showNext)
        {
            _nextText.Text = nextRow is not null && nextRow.Subject.Length > 0
                ? $"下节课 {nextRow.Subject}"
                : "下节课 --";
        }
        else
        {
            _nextText.Text = "";
        }

        if (_showDuration && TimeOnly.TryParse(current.StartTime, out var s) && TimeOnly.TryParse(current.EndTime, out var e))
        {
            var startSec = s.Hour * 3600 + s.Minute * 60;
            var endSec = e.Hour * 3600 + e.Minute * 60;
            var nowSec = now.Hour * 3600 + now.Minute * 60 + now.Second;
            var total = endSec - startSec;
            var elapsed = Math.Max(0, nowSec - startSec);
            _bottomProgress.Value = total > 0 ? Math.Min(100, elapsed / (double)total * 100) : 0;
            _timeProgressText.Text = $"{elapsed / 60}min / {total / 60}min";
        }
        else
        {
            _timeProgressText.Text = "";
            _bottomProgress.Value = 0;
        }
    }

    // 原版 teacher[0]+"老师"
    private static string TeacherDisplay(string teacher) =>
        teacher.Length > 0 ? $"{teacher[0]}老师" : "";

    private void ApplyStyle()
    {
        var p = WidgetSurfacePalette.Resolve(CardIsDark);
        var text = p.Text;
        var muted = new SolidColorBrush(text, CardInk.MutedOpacity);
        var scale = _fontScale / 100.0;
        double S(double px) => Math.Max(1, px * scale);

        // 课名是英雄字 其余分级: 正文全值 / 次级 62%
        _subjectFg = new SolidColorBrush(text);
        _subjectText.Foreground = _subjectFg;
        _subjectText.FontSize = S(40);
        _subjectText.FontWeight = WidgetWeight.Title;
        _countdownText.Foreground = CardAccent.Brush();
        _countdownText.FontSize = S(WidgetType.Headline);
        _countdownText.FontWeight = WidgetWeight.Title;
        _timeProgressText.Foreground = muted;
        _timeProgressText.FontSize = S(WidgetType.Body);
        _teacherText.Foreground = muted;
        _teacherText.FontSize = S(WidgetType.Label);
        _timeText.Foreground = muted;
        _timeText.FontSize = S(WidgetType.Label);
        _nextText.Foreground = muted;
        _nextText.FontSize = S(WidgetType.Label);
        _bottomProgress.Foreground = CardAccent.Brush();
        Relayout();
    }
}
