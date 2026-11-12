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

// 课程表窗口
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

public partial class ScheduleWindow : GlimpseonWindow
{
    // 周日起列 DayOfWeek 同构
    private static readonly string[] DayOrder =
        ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

    private static readonly string[] WeekdayOrder =
        ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private static readonly string[] PeriodTypes = ["上课", "课间", "活动"];
    private static readonly string[] Parities = ["单周", "双周"];

    // 周视图列宽 行头 + 七日
    private const string WeekCols = "96,*,*,*,*,*,*,*";

    /// <summary>所在周周一</summary>
    private static DateOnly MondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>时段命令组</summary>
    private (Control Icon, string Label, Action OnClick)[] PeriodCommands() =>
    [
        (FAIcons.IconCenter(FASymbol.Add, 13), Tr("timetable.btn_class"), () => AddPeriod("上课")),
        (FAIcons.GlyphCenter(AppIcons.Clock, 13), Tr("timetable.btn_break"), () => AddPeriod("课间")),
        (FAIcons.IconCenter(FASymbol.Play, 13), Tr("timetable.btn_activity"), () => AddPeriod("活动")),
        (FAIcons.IconCenter(FASymbol.Delete, 13), Tr("timetable.btn_delete_row"), OnDeleteSelectedRow),
    ];

    /// <summary>计时器重启 防重复订阅</summary>
    private static void PulseTimer(ref DispatcherTimer? timer, TimeSpan interval, EventHandler onTick)
    {
        timer ??= new DispatcherTimer { Interval = interval };
        timer.Tick -= onTick;
        timer.Tick += onTick;
        timer.Stop();
        timer.Start();
    }

    // 落盘防抖 250ms
    private DispatcherTimer? _courseSaveDebounce;
    private DispatcherTimer? _dutySaveDebounce;
    private DispatcherTimer? _subjectSaveDebounce;
    private DispatcherTimer? _rosterSaveDebounce;

    private void QueueCourseSave() => PulseTimer(ref _courseSaveDebounce, TimeSpan.FromMilliseconds(250), (_, _) => _course.Save());
    private void QueueDutySave() => PulseTimer(ref _dutySaveDebounce, TimeSpan.FromMilliseconds(250), (_, _) => _duty.Save());
    private void QueueSubjectSave() => PulseTimer(ref _subjectSaveDebounce, TimeSpan.FromMilliseconds(250), (_, _) => _subject.Save());
    private void QueueRosterSave() => PulseTimer(ref _rosterSaveDebounce, TimeSpan.FromMilliseconds(250), (_, _) => _roster.Save());

    private readonly Dictionary<string, Control> _pages = new();
    private FAFrame? _navFrame;
    private string _currentTag = "course";
    private bool _blockSync;

    private static string Tr(string key, params (string Name, object? Value)[] args) => AppUtils.Tr(key, args);

    // 主题笔刷
    private static IBrush LineBrush => new SolidColorBrush(Colors.Gray, 0.24);
    private static IBrush HeaderBg => new SolidColorBrush(Colors.Gray, 0.10);
    private static IBrush CardBg => new SolidColorBrush(Colors.Gray, 0.05);
    private static IBrush RowHoverBg => new SolidColorBrush(Colors.Gray, 0.055);
    private static IBrush RowHeadBg => new SolidColorBrush(Colors.Gray, 0.035);
    private static IBrush AccentBrush => new SolidColorBrush(Common.ParseAccentColor(Config.ThemeColor.Value));
    private static IBrush SelCellBg
    {
        get
        {
            var c = Common.ParseAccentColor(Config.ThemeColor.Value);
            return new SolidColorBrush(Color.FromArgb(56, c.R, c.G, c.B));
        }
    }
    private static IBrush SelRowBg
    {
        get
        {
            var c = Common.ParseAccentColor(Config.ThemeColor.Value);
            return new SolidColorBrush(Color.FromArgb(22, c.R, c.G, c.B));
        }
    }

    // 语义色 exam/holiday/patch/check
    private static readonly IBrush ExamBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x9F, 0x27));
    private static readonly IBrush HolidayBrush = new SolidColorBrush(Color.FromRgb(0x63, 0x99, 0x22));
    private static readonly IBrush PatchBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x8A, 0xDD));
    private static readonly IBrush CheckBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0x4B, 0x4A));

    // 活动总档案与分档案
    private ScheduleProfile _master = new();
    private TimetableDoc _timetable = new();
    private CourseDoc _course = new();
    private DutyDoc _duty = new();
    private SubjectDoc _subject = new();

    // 课表页
    private StackPanel _courseRowsHost = null!;
    private readonly List<Border> _courseCells = [];
    private readonly List<TextBlock> _courseHeadRangeTexts = [];
    private readonly List<TextBox?[]> _courseDayEditors = [];
    private int _courseSelRow = -1;
    private int _courseSelCol = -1;
    private TextBlock _courseInfo = null!;
    private TextBlock _courseStatus = null!;
    private Control _courseTableEmpty = null!;
    private WrapPanel _subjectFlow = null!;
    private DispatcherTimer? _subjectFlowDebounce;
    private ComboBox _sourceCombo = null!;
    private Border _courseEditorCard = null!;
    private Control _courseEditorEmpty = null!;
    private StackPanel _courseEditorHost = null!;
    private StackPanel _linkagePanel = null!;
    private TextBlock _linkagePathLabel = null!;
    private ListBox _linkageTable = null!;
    private TextBlock? _courseCaption;
    // 单双周切换组
    private StackPanel? _courseWeekSwitch;
    private Control? _courseCommandBar;
    private readonly LinkageBridge _ciBridge = new();
    private readonly ClassWidgetsBridge _cwBridge = new();
    private LinkageBridgeBase? _activeBridge;
    private bool _linkageMode;
    private DispatcherTimer? _linkageTimer;

    // 时间表页
    private StackPanel _timeRowsHost = null!;
    private readonly List<Border> _timeBlocks = [];
    private readonly List<TextBlock> _timeBlockTexts = [];
    private StackPanel? _typeRadios;
    private TextBox _titleBox = null!;
    private DispatcherTimer? _titleDebounce;
    private Canvas? _timeCanvas;
    private ScrollViewer? _timeScroll;
    private DispatcherTimer? _zoomTimer;
    private int _timeSelRow = -1;
    private TimePicker _startPicker = null!;
    private TimePicker _endPicker = null!;
    private NumericUpDown _classDurSpin = null!;
    private NumericUpDown _breakDurSpin = null!;
    private StackPanel _timeEditHost = null!;
    private Control _timeEmpty = null!;
    private TextBlock? _timeCaption;
    private Control? _timeCommandBar;
    private Border? _timeEditorCard;
    private Border? _durCard;
    private TextBlock _timeMsg = null!;
    private int _dragIdx = -1;
    private double _dragY0;
    private int _dragOrigB;
    private DispatcherTimer? _timeMsgTimer;
    // 时间轴 min→px 1.8 留白62
    private double _timeScale = 1.8;
    private const double TimeScaleMax = 3.0;
    private const double TimeScaleMin = 0.6;
    private const double TimelineLeft = 62;
    private const int DayMinutes = 24 * 60;
    // 顶部留白 防刻度裁切
    private const double TimelinePadTop = 8;
    // 胶囊尺寸 随 scale 联动
    private double _dotSize = 36;
    private double _dotH = 4;
    // 缩放锚点 视口中心分钟
    private double _zoomAnchorMin;
    // 胶囊 宽度变更重排
    private readonly List<Border> _timeDots = [];
    // 首渲染滚动锚定
    private bool _autoScrollPending = true;

    // 调课页
    // 整日类型索引 对应 day_mode_* 标签序
    private const int DayModeNormal = 0;
    private const int DayModePlan = 1;
    private const int DayModeExam = 2;
    private const int DayModeHoliday = 3;
    private DateOnly _adjustMonday;
    private DateOnly _adjustSelDate;
    private int _adjustSelPeriod = -1;
    private (DateOnly Date, int Period, string Subject)? _adjustSwap;
    private (DateOnly Date, int Period)? _adjustEditorKey;
    private readonly Dictionary<(DateOnly Date, int Period), Border> _adjustCells = [];
    private StackPanel _adjustGridHost = null!;
    private TextBlock _adjustWeekLabel = null!;
    private TextBlock _adjustCellLabel = null!;
    private TextBlock _adjustSourceLabel = null!;
    private TextBlock _adjustMsg = null!;
    private DispatcherTimer? _adjustMsgTimer;
    private Border _adjustSwapBanner = null!;
    private TextBox _adjustSubjectBox = null!;
    private TextBox _adjustRoomBox = null!;
    private TextBox? _adjustDayNameBox;
    private ComboBox? _adjustPlanCombo;
    private ComboBox _adjustDayModeCombo = null!;
    private StackPanel _dayModeHost = null!;
    private StackPanel _dayOps = null!;
    private TextBlock _adjustSummary = null!;
    private Control _adjustEmpty = null!;
    private StackPanel _adjustEditHost = null!;
    private Button _adjustSwapBtn = null!;
    private Button _adjustSaveCellBtn = null!;
    private FAMenuFlyoutItem _adjustSetDefaultBtn = null!;

    // 值周表页
    private StackPanel _dutyRowsHost = null!;
    private Control _dutyTableEmpty = null!;
    private readonly List<Border> _dutyCells = [];
    private readonly List<ComboBox> _dutyParityCombos = [];
    private readonly List<ComboBox> _dutyWeekdayCombos = [];
    private readonly List<TextBox> _dutyGroupEditors = [];
    private readonly List<TextBox> _dutyNameEditors = [];
    private readonly List<TextBox> _dutyRoleEditors = [];
    private int _dutySelRow = -1;
    private int _dutySelCol = -1;
    private StackPanel _dutyEditHost = null!;
    private Control _dutyEmpty = null!;
    private ComboBox _dutyParityCombo = null!;
    private ComboBox _dutyWeekdayCombo = null!;
    private TextBox _dutyGroupEdit = null!;
    private TextBox _dutyNameEdit = null!;
    private TextBox _dutyRoleEdit = null!;
    private TextBlock? _dutyCaption;
    private Control? _dutyCommandBar;
    private Border? _dutyEditorCard;

    // 花名册页
    private RosterDoc _roster = new();
    private StackPanel _rosterRowsHost = null!;
    private Control _rosterTableEmpty = null!;
    private readonly List<Border> _rosterCells = [];
    private int _rosterSelRow = -1;
    private int _rosterSelCol = -1;
    private StackPanel _rosterEditHost = null!;
    private Control _rosterEmpty = null!;
    private TextBox _rosterNoEdit = null!;
    private TextBox _rosterNameEdit = null!;
    private TextBox _rosterSearch = null!;
    private TextBlock _rosterResult = null!;
    private TextBlock _rosterCount = null!;
    private TextBlock? _rosterCaption;
    private Control? _rosterCommandBar;
    private Border? _rosterEditorCard;
    private int _rosterFlashRow = -1;
    private bool _drawing;
    private DispatcherTimer? _drawTimer;

    // 学科页
    private StackPanel _subjectRowsHost = null!;
    private Control _subjectTableEmpty = null!;
    private readonly List<Border> _subjectCells = [];
    private readonly List<TextBox> _subjectNameEditors = [];
    private readonly List<TextBox> _subjectShortEditors = [];
    private readonly List<TextBox> _subjectTeacherEditors = [];
    private readonly List<CheckBox> _subjectOutdoorChecks = [];
    private int _subjectSelRow = -1;
    private int _subjectSelCol = -1;
    private StackPanel _subjectEditHost = null!;
    private Control _subjectEmpty = null!;
    private TextBox _subjectNameEdit = null!;
    private TextBox _subjectShortEdit = null!;
    private TextBox _subjectTeacherEdit = null!;
    private CheckBox _subjectOutdoorCheck = null!;
    private ComboBox _subjectColorCombo = null!;

    /// <summary>科目预设色板 0=自动</summary>
    private static readonly List<(string Label, string Hex)> SubjectColors =
    [
        ("自动", ""),
        ("红 #E24B4A", "#E24B4A"),
        ("橙 #EF9F27", "#EF9F27"),
        ("黄 #BA7517", "#BA7517"),
        ("绿 #639922", "#639922"),
        ("青 #1D9E75", "#1D9E75"),
        ("蓝 #378ADD", "#378ADD"),
        ("紫 #7F77DD", "#7F77DD"),
        ("粉 #D4537E", "#D4537E"),
        ("灰 #888780", "#888780"),
    ];
    private TextBlock? _subjectCaption;
    private TextBlock? _adjustCaption;
    private Control? _subjectCommandBar;
    private Border? _subjectEditorCard;

    // 跟随外部时内存副本
    private List<ScheduleRow>? _followRows;
    private TimetableDoc _followTimetable = new();
    private CourseDoc _followCourse = new();

    // 档案页
    private StackPanel _mastersHost = null!;
    private StackPanel _profileSummary = null!;
    private Button _addMasterBtn = null!;
    private MenuItem _importMasterBtn = null!;

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    public ScheduleWindow()
    {
        InitializeComponent();
        Title = Tr("schedule.title");
        TitleLabel.Text = Tr("schedule.title");

        // 标题栏待重启徽标
        Common.InitRestartBadge(RestartBadge, this);

        try
        {
            var iconPath = Paths.GetResourcePath(Constants.AppIcon);
            if (File.Exists(iconPath))
            {
                WindowIcon.Source = new Avalonia.Media.Imaging.Bitmap(iconPath);
            }
        }
        catch (Exception e)
        {
            Log.Debug($"[课表窗口] 图标缺失留空: {e.Message}");
        }
        VersionText.Text = Paths.Version;

        SetNavLabels();
        LoadActive();
        BuildPages();
        UpdateCaptions();
        _navFrame = new FAFrame { IsNavigationStackEnabled = false, NavigationPageFactory = PassThroughNavigationPageFactory.Instance };
        Nav.Content = _navFrame;
        if (_pages.TryGetValue("course", out var firstPage))
        {
            _navFrame.Content = firstPage;
        }
        Nav.SelectedItem = NavCourse;

        _linkageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _linkageTimer.Tick += (_, _) => RefreshLinkageTable();

        var currentSource = Config.ProfileSource.Value;
        if (currentSource != "Glimpseon")
        {
            ApplySourceMode(currentSource);
        }
        Log.Debug($"[课表窗口] 就绪 档案={_master.Name} 时段={_timetable.PeriodCount()} 学科={_subject.Rows.Count}");
    }

    private void SetNavLabels()
    {
        NavCourse.Content = Tr("schedule.tab_course");
        NavTime.Content = Tr("schedule.tab_time");
        NavAdjust.Content = Tr("schedule.tab_adjust");
        NavDuty.Content = Tr("schedule.tab_duty");
        NavRoster.Content = Tr("schedule.tab_roster");
        NavSubject.Content = Tr("schedule.tab_subject");
        NavProfile.Content = Tr("schedule.tab_profile");
    }

    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is FANavigationViewItem item && item.Tag is string tag && _navFrame is { IsLoaded: true } && _pages.TryGetValue(tag, out var page))
        {
            _currentTag = tag;
            if (ReferenceEquals(_navFrame.Content, page))
            {
                return;
            }
            _navFrame.NavigateFromObject(page, new FAFrameNavigationOptions());
        }
    }

    // 活动档案装载

    /// <summary>活动总档案配置项</summary>
    private static ConfigItem<string> ActiveMasterItem() => Config.ProfileSource.Value switch
    {
        "classisland" => Config.ScheduleProfileCi,
        "classwidgets" => Config.ScheduleProfileCw,
        _ => Config.ScheduleProfile,
    };

    /// <summary>CI/CW 跟随外部标志</summary>
    private static bool SourceFollowMode() => Config.ProfileSource.Value switch
    {
        "classisland" => Config.ScheduleProfileCiFollow.Value,
        "classwidgets" => Config.ScheduleProfileCwFollow.Value,
        _ => false,
    };

    /// <summary>来源跟随项 无则null</summary>
    private static ConfigItem<bool>? FollowItemOf(string masterName)
    {
        if (Config.ScheduleProfileCi.Value == masterName)
        {
            return Config.ScheduleProfileCiFollow;
        }
        if (Config.ScheduleProfileCw.Value == masterName)
        {
            return Config.ScheduleProfileCwFollow;
        }
        return null;
    }

    private void LoadActive()
    {
        ScheduleStore.EnsureDefaults();
        var item = ActiveMasterItem();
        _master = ScheduleStore.LoadMaster(item.Value);
        _master.RepairBindings();
        _timetable = TimetableDoc.LoadOrDefault(_master.Timetable);
        _course = CourseDoc.LoadOrDefault(_master.Course);
        _duty = DutyDoc.LoadOrDefault(_master.Duty);
        _subject = SubjectDoc.LoadOrDefault(_master.Subject);
        _roster = RosterDoc.LoadOrDefault(_master.Roster);
        Log.Info($"[课表窗口] 切到总档案: {_master.Name} 来源={Config.ProfileSource.Value} 课表={_master.Course} 时间表={_master.Timetable} 值周表={_master.Duty} 学科={_master.Subject} 花名册={_master.Roster}");
    }

    // 页面构建

    private void BuildPages()
    {
        _pages.Clear();
        _pages["course"] = BuildCoursePage();
        _pages["time"] = BuildTimePage();
        _pages["adjust"] = BuildAdjustPage();
        _pages["duty"] = BuildDutyPage();
        _pages["roster"] = BuildRosterPage();
        _pages["subject"] = BuildSubjectPage();
        _pages["profile"] = BuildProfilePage();
    }

    /// <summary>页标题</summary>
    private static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        FontSize = 22,
        FontWeight = FontWeight.Medium,
    };

    private static Border MakeCard(Control content, Thickness? padding = null)
    {
        var card = MakeCardFrame(content);
        card.Padding = padding ?? new Thickness(10, 8, 10, 8);
        card.Margin = new Thickness(0, 0, 0, 8);
        return card;
    }

    /// <summary>禁null背景 保命中测试</summary>
    private static Border MakeCell(Control content, int row, int col, bool header = false, Action? onClick = null)
    {
        var border = new Border
        {
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = header ? HeaderBg : Brushes.Transparent,
            Padding = new Thickness(8, 4, 8, 4),
            MinHeight = 38,
            Child = content,
            Tag = (row, col),
        };
        if (col > 0)
        {
            Grid.SetColumn(border, col);
        }
        if (onClick is not null)
        {
            // handledEventsToo 穿透子控件
            border.AddHandler(InputElement.PointerPressedEvent, (_, _) => onClick(), RoutingStrategies.Bubble, true);
        }
        return border;
    }

    private static TextBlock CellText(string text, bool header = false) => new()
    {
        Text = text,
        FontSize = 12.5,
        FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
        Opacity = header ? 0.85 : 1,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static TextBox CellEdit(string text, string? watermark = null) => new()
    {
        Text = text,
        FontSize = 12.5,
        MinHeight = 28,
        Padding = new Thickness(6, 2, 6, 2),
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        PlaceholderText = watermark,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>行容器 hover 命中选中</summary>
    private static Border MakeRowHost(Grid rowGrid, Action? onClick = null)
    {
        var border = new Border { Child = rowGrid, Background = Brushes.Transparent };
        border.PointerEntered += (_, _) => border.Background = RowHoverBg;
        border.PointerExited += (_, _) => border.Background = Brushes.Transparent;
        if (onClick is not null)
        {
            border.AddHandler(InputElement.PointerPressedEvent, (_, _) => onClick(), RoutingStrategies.Bubble, true);
        }
        return border;
    }

    /// <summary>选中高亮 首列行头</summary>
    private static void PaintCells(List<Border> cells, int selRow, int selCol)
    {
        foreach (var b in cells)
        {
            var (r, c) = ((int, int))b.Tag!;
            b.Background = r == selRow
                ? (c == selCol ? SelCellBg : SelRowBg)
                : (c == 0 ? RowHeadBg : Brushes.Transparent);
            b.BorderBrush = LineBrush;
            b.BorderThickness = new Thickness(0, 0, 1, 1);
        }
    }

    private static Button MakeToolButton(FASymbol symbol, string tip, Action action)
    {
        var btn = new Button
        {
            Content = FAIcons.Icon(symbol, 14),
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(btn, tip);
        btn.Click += (_, _) => action();
        return btn;
    }

    private static Button MakeToolButton(string glyph, string tip, Action action)
    {
        var btn = new Button
        {
            Content = FAIcons.Glyph(glyph, 14),
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(btn, tip);
        btn.Click += (_, _) => action();
        return btn;
    }

    private static Button MakeSmallButton(string text, Action? action)
    {
        var btn = new Button
        {
            Content = text,
            Height = 30,
            Padding = new Thickness(12, 0, 12, 0),
            FontSize = 12,
        };
        if (action is not null)
        {
            btn.Click += (_, _) => action();
        }
        return btn;
    }

    /// <summary>全页刷新</summary>
    private void RefreshAllPages()
    {
        RefreshCourseTable();
        RefreshTimeTable();
        RefreshDutyTable();
        RefreshRosterTable();
        RefreshSubjectTable();
        RebuildSubjectFlow();
        if (_adjustGridHost is not null)
        {
            RefreshAdjustGrid();
        }
        UpdateCaptions();
    }

    // 页面布局

    /// <summary>文本工具钮 透明底</summary>
    private static Button MakeTextToolButton(Control? icon, string label, Action onClick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (icon is not null)
        {
            content.Children.Add(icon);
        }
        content.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var btn = new Button
        {
            Content = content,
            Padding = new Thickness(8, 5, 8, 5),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    /// <summary>图标工具钮 透明底</summary>
    private static Button MakeIconToolButton(FASymbol symbol, string tip, Action onClick)
    {
        var btn = new Button
        {
            Content = FAIcons.IconCenter(symbol, 13),
            Padding = new Thickness(7, 5, 7, 5),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(btn, tip);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    /// <summary>下拉菜单 图标 + 文本</summary>
    private static MenuFlyout MakeMenu(params (Control Icon, string Label, Action OnClick)[] items)
    {
        var menu = new MenuFlyout();
        foreach (var (icon, label, onClick) in items)
        {
            var item = new MenuItem { Header = label, Icon = icon };
            item.Click += (_, _) => onClick();
            menu.Items.Add(item);
        }
        return menu;
    }

    /// <summary>工具行 左命令右更多</summary>
    private static Grid MakeToolRow(
        (Control Icon, string Label, Action OnClick)[] primary,
        Control? trailing = null,
        MenuFlyout? more = null,
        Control? first = null)
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        if (first is not null)
        {
            left.Children.Add(first);
        }
        foreach (var (icon, label, onClick) in primary)
        {
            left.Children.Add(MakeTextToolButton(icon, label, onClick));
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        row.Children.Add(left);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            trailing.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(trailing, 2);
            row.Children.Add(trailing);
        }
        if (more is not null)
        {
            // 右对齐展开 防越界
            more.Placement = PlacementMode.BottomEdgeAlignedRight;
            var moreBtn = new Button
            {
                Content = FAIcons.Icon(FASymbol.More, 14),
                Padding = new Thickness(8, 5, 8, 5),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Flyout = more,
            };
            ToolTip.SetTip(moreBtn, Tr("schedule.sub_more"));
            Grid.SetColumn(moreBtn, 3);
            row.Children.Add(moreBtn);
        }
        return row;
    }

    /// <summary>空态叠层</summary>
    private static StackPanel MakeEmptyState(string text, FASymbol symbol)
    {
        var icon = FAIcons.Icon(symbol, 26);
        icon.Opacity = 0.22;
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        return new StackPanel
        {
            Spacing = 10,
            // 不拦截滚动
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24, 0, 24, 24),
            IsHitTestVisible = false,
            Children =
            {
                icon,
                new TextBlock
                {
                    Text = text,
                    FontSize = 12.5,
                    Opacity = 0.45,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 240,
                },
            },
        };
    }

    /// <summary>表格区 表+底栏</summary>
    private static Grid MakeTableArea(Control table, Control? footer = null)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        grid.Children.Add(table);
        if (footer is not null)
        {
            Grid.SetRow(footer, 1);
            grid.Children.Add(footer);
        }
        return grid;
    }

    /// <summary>表格 + 空态叠层</summary>
    private static Grid MakeTableStack(Control table, Control empty) => new() { Children = { table, empty } };

    /// <summary>整页骨架</summary>
    private static Grid MakePage(string title, Control? subtitle, Control? commands, string rightTitle, Control left, Control? right = null)
    {
        var head = new StackPanel { Spacing = 2 };
        head.Children.Add(PageTitle(title));
        if (subtitle is not null)
        {
            head.Children.Add(subtitle);
        }
        Grid.SetColumnSpan(head, 3);
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,20,320"),
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Margin = new Thickness(24, 4, 24, 16),
            Children = { head },
        };
        if (commands is not null)
        {
            // 光学对齐
            commands.Margin = new Thickness(-8, 8, 0, 8);
            Grid.SetRow(commands, 1);
            root.Children.Add(commands);
        }
        var rightHead = new TextBlock
        {
            Text = rightTitle,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };
        Grid.SetRow(rightHead, 1);
        Grid.SetColumn(rightHead, 2);
        root.Children.Add(rightHead);
        // 分栏竖线 仅贯正文
        var rule = new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Colors.Gray, 0.16),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetColumn(rule, 1);
        Grid.SetRow(rule, 2);
        root.Children.Add(rule);
        Grid.SetRow(left, 2);
        root.Children.Add(left);
        if (right is not null)
        {
            Grid.SetRow(right, 2);
            Grid.SetColumn(right, 2);
            root.Children.Add(right);
        }
        return root;
    }

    /// <summary>卡片外框</summary>
    private static Border MakeCardFrame(Control content) => new()
    {
        CornerRadius = new CornerRadius(0),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Colors.Gray, 0.20),
        Background = Common.MakeCard().Background,
        Child = content,
    };

    /// <summary>滚动表格 四边线收口</summary>
    private static Border MakeScrollableTable(Grid header, StackPanel rowsHost)
    {
        var scroll = new ScrollViewer
        {
            Content = rowsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(header);
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        return new Border
        {
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            Background = CardBg,
            Child = grid,
        };
    }

    /// <summary>副标题 配置名</summary>
    private static TextBlock MakeCaption(string text = "") => new()
    {
        Text = text,
        FontSize = 12,
        Opacity = 0.6,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>操作卡 空态/表单</summary>
    private (Border Card, Control Empty, StackPanel Host, StackPanel Inner) MakeEditorCard()
    {
        var emptyIcon = FAIcons.Icon(FASymbol.Edit, 24);
        emptyIcon.Opacity = 0.18;
        emptyIcon.HorizontalAlignment = HorizontalAlignment.Center;
        var empty = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 16, 0),
            Children =
            {
                emptyIcon,
                new TextBlock
                {
                    Text = Tr("schedule.editor_empty"),
                    FontSize = 12.5,
                    Opacity = 0.42,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 220,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };
        // 叠层垂直居中
        var emptyLayer = new Grid { Children = { empty } };
        var host = new StackPanel { Spacing = 12, IsVisible = false };
        var scroll = new ScrollViewer
        {
            Content = host,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16, 14, 16, 14),
        };
        var card = MakeCardFrame(new Grid { Children = { scroll, emptyLayer } });
        return (card, emptyLayer, host, host);
    }

    private static void ShowEditor(Control empty, StackPanel host, bool hasSelection)
    {
        empty.IsVisible = !hasSelection;
        host.IsVisible = hasSelection;
    }

    /// <summary>Field 式表单行</summary>
    private static StackPanel MakeField(string label, Control control, string? suffix = null)
    {
        var contentRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        contentRow.Children.Add(control);
        if (!string.IsNullOrEmpty(suffix))
        {
            var suffixText = new TextBlock
            {
                Text = suffix,
                FontSize = 12,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };
            Grid.SetColumn(suffixText, 1);
            contentRow.Children.Add(suffixText);
        }
        return new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = label, FontSize = 12, Opacity = 0.65 },
                contentRow,
            },
        };
    }

    // 课表页

    private Control BuildCoursePage()
    {
        // 左: 周课表
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(WeekCols) };
        header.Children.Add(MakeCell(CellText(Tr("timetable.time_range"), true), 0, 0, header: true));
        for (var d = 0; d < 7; d++)
        {
            header.Children.Add(MakeCell(CellText(Tr($"timetable.{DayOrder[d]}"), true), 0, d + 1, header: true));
        }
        _courseRowsHost = new StackPanel();
        _courseTableEmpty = MakeEmptyState(Tr("schedule.course_empty_hint"), FASymbol.Add);
        var table = MakeTableStack(MakeScrollableTable(header, _courseRowsHost), _courseTableEmpty);

        _courseCaption = MakeCaption();
        // 状态行 空折叠
        _courseStatus = new TextBlock { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 0), IsVisible = false };
        _courseStatus.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty)
            {
                _courseStatus.IsVisible = !string.IsNullOrWhiteSpace(_courseStatus.Text);
            }
        };
        // 单双周切换
        _courseWeekSwitch = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Center,
            // 右留白 避右栏
            Margin = new Thickness(8, 0, 16, 0),
            IsVisible = false,
        };
        var cmdRow = MakeToolRow(PeriodCommands(), _courseWeekSwitch,
            MakeMenu(
                (FAIcons.GlyphCenter(AppIcons.Save, 13), Tr("schedule.course_export_png"), OnExportPng),
                (FAIcons.IconCenter(FASymbol.Download, 13), Tr("schedule.course_ai_import"), OnAiImport)));
        _courseCommandBar = cmdRow;
        var left = MakeTableArea(table, _courseStatus);

        // 右: 选中格编辑
        var (editorCard, editorEmpty, editorHost, editorInner) = MakeEditorCard();
        _courseEditorCard = editorCard;
        _courseEditorEmpty = editorEmpty;
        _courseEditorHost = editorHost;
        _courseInfo = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        editorHost.Children.Add(_courseInfo);
        _subjectFlow = new WrapPanel { Orientation = Orientation.Horizontal };
        editorHost.Children.Add(MakeField(Tr("schedule.quick_subjects"), _subjectFlow));
        editorHost.Children.Add(new Separator { Margin = new Thickness(0, 2, 0, 0) });
        editorHost.Children.Add(new TextBlock
        {
            Text = Tr("schedule.course_editor_hint"),
            FontSize = 11,
            Opacity = 0.45,
            TextWrapping = TextWrapping.Wrap,
        });
        ShowEditor(editorEmpty, editorHost, false);

        // 联动只读面板
        _linkagePanel = new StackPanel { Spacing = 8, IsVisible = false };
        _linkagePathLabel = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        var linkageBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        linkageBtnRow.Children.Add(MakeSmallButton(Tr("timetable.btn_auto_detect"), OnAutoDetect));
        linkageBtnRow.Children.Add(MakeSmallButton(Tr("timetable.btn_select_dir"), OnSelectDataDir));
        _linkagePanel.Children.Add(_linkagePathLabel);
        _linkagePanel.Children.Add(linkageBtnRow);
        _linkagePanel.Children.Add(new TextBlock
        {
            Text = Tr("timetable.linkage_readonly"),
            FontSize = 11,
            Opacity = 0.5,
            TextWrapping = TextWrapping.Wrap,
        });
        _linkageTable = new ListBox { MaxHeight = 460 };
        _linkagePanel.Children.Add(_linkageTable);
        editorInner.Children.Add(_linkagePanel);

        var root = MakePage(Tr("schedule.tab_course"), _courseCaption, cmdRow, Tr("schedule.course_editor_title"), left, editorCard);
        RebuildSubjectFlow();
        RefreshCourseTable();
        RebuildCourseWeekSwitch();
        return root;
    }

    /// <summary>非课间/活动行索引</summary>
    private List<int> NonBreakIndices() =>
        Enumerable.Range(0, _timetable.Periods.Count)
            .Where(i => PeriodField(_timetable.Periods[i], "type") is not ("课间" or "活动"))
            .ToList();

    // 导出 PNG / AI 导入

    /// <summary>离屏 RenderTargetBitmap</summary>
    private async void OnExportPng()
    {
        if (_linkageMode)
        {
            return;
        }
        try
        {
            var grid = BuildPngWeekGrid();
            grid.Measure(new Size(1400, 1800));
            grid.Arrange(new Rect(default, grid.DesiredSize));
            var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(grid.DesiredSize.Width)), Math.Max(1, (int)Math.Ceiling(grid.DesiredSize.Height)));
            using var bmp = new RenderTargetBitmap(size, new Vector(96, 96));
            bmp.Render(grid);
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = $"课表-{AppUtils.PreciseNow():yyyyMMdd}.png",
                FileTypeChoices = [new FilePickerFileType(Tr("schedule.png_type")) { Patterns = ["*.png"] }],
            });
            if (file is null)
            {
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            bmp.Save(stream, new PngBitmapEncoderOptions());
            _courseStatus.Text = Tr("schedule.png_done");
            Log.Info("[课表窗口] 已导出课表 PNG");
        }
        catch (Exception e)
        {
            _courseStatus.Text = Tr("schedule.png_failed", ("msg", e.Message));
            Log.Warning($"[课表窗口] 导出 PNG 失败: {e.Message}");
        }
    }

    /// <summary>白底周视图 离屏用</summary>
    private Grid BuildPngWeekGrid()
    {
        var week = TimetableScheduleProvider.GetWeekSchedule();
        var nb = NonBreakIndices();
        var grid = new Grid { Background = Brushes.White };
        for (var r = 0; r <= nb.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        var cols = new ColumnDefinitions("110,140,140,140,140,140,140,140");
        grid.ColumnDefinitions = cols;
        var headCell = (string text, int c) =>
        {
            var b = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new Thickness(0.5),
                Padding = new Thickness(4, 6, 4, 6),
                Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
                Child = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.Black, TextAlignment = TextAlignment.Center },
            };
            Grid.SetColumn(b, c);
            return b;
        };
        grid.Children.Add(headCell(Tr("timetable.period"), 0));
        for (var d = 0; d < 7; d++)
        {
            grid.Children.Add(headCell(Tr($"timetable.{DayOrder[d]}"), d + 1));
        }
        for (var r = 0; r < nb.Count; r++)
        {
            var periodIdx = nb[r];
            var p = _timetable.Periods[periodIdx];
            var rowHead = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new Thickness(0.5),
                Padding = new Thickness(4, 6, 4, 6),
                Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
                Child = new TextBlock { Text = $"{Tr("schedule.period_n", ("n", r + 1))}\n{PeriodField(p, "start")}", FontSize = 11, Foreground = Brushes.Black, TextAlignment = TextAlignment.Center },
            };
            Grid.SetRow(rowHead, r + 1);
            grid.Children.Add(rowHead);
            for (var d = 0; d < 7; d++)
            {
                var dayKey = DayOrder[d];
                var rows = week.GetValueOrDefault(dayKey) ?? [];
                var subjects = rows.Where(x => !x.IsBreak).ToList();
                var subject = r < subjects.Count ? subjects[r].Subject : "";
                var cell = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(4, 6, 4, 6),
                    Child = new TextBlock { Text = subject, FontSize = 13, Foreground = Brushes.Black, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                };
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, d + 1);
                grid.Children.Add(cell);
            }
        }
        return grid;
    }

    /// <summary>剪贴板→LLM JSON→填格</summary>
    private async void OnAiImport()
    {
        if (_linkageMode)
        {
            return;
        }
        if (!AiService.Ready)
        {
            _courseStatus.Text = Tr("schedule.ai_no_config");
            return;
        }
        try
        {
            var top = TopLevel.GetTopLevel(this);
            var text = "";
            if (top?.Clipboard is { } clip && await clip.TryGetDataAsync() is { } data)
            {
                using (data)
                {
                    text = await data.TryGetTextAsync() ?? "";
                }
            }
            if (text.Trim().Length == 0)
            {
                _courseStatus.Text = Tr("schedule.ai_clip_empty");
                return;
            }
            if (!await Common.Confirm(this, Tr("schedule.ai_confirm_title"), Tr("schedule.ai_confirm_desc")))
            {
                return;
            }
            _courseStatus.Text = Tr("schedule.ai_parsing");
            const string system = "你是课程表解析器。把用户提供的课表文本解析为 JSON 对象。键为 monday/tuesday/wednesday/thursday/friday/saturday/sunday 中出现的星期; 值为字符串数组, 顺序对应该天逐节科目名 (空节用 \"\")。只输出 JSON 本体, 不要解释或代码块标记。";
            const int maxChars = 6000;
            var result = await AiService.ChatAsync(text.Length > maxChars ? text[..maxChars] : text, system);
            if (!result.Ok)
            {
                _courseStatus.Text = Tr("schedule.ai_failed", ("msg", result.Error));
                return;
            }
            var content = result.Content.Trim();
            if (content.StartsWith("```"))
            {
                content = content.Trim('`').Trim();
                if (content.StartsWith("json"))
                {
                    content = content[4..];
                }
            }
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start < 0 || end <= start || JsonNode.Parse(content[start..(end + 1)]) is not JsonObject obj)
            {
                _courseStatus.Text = Tr("schedule.ai_bad_json");
                return;
            }
            var nb = NonBreakIndices();
            var filled = 0;
            foreach (var (dayKey, node) in obj)
            {
                if (node is not JsonArray arr || !DayOrder.Contains(dayKey))
                {
                    continue;
                }
                for (var i = 0; i < arr.Count && i < nb.Count; i++)
                {
                    var subject = arr[i]?.ToString()?.Trim() ?? "";
                    _course.SetCell(nb[i], dayKey, subject);
                    if (subject.Length > 0)
                    {
                        filled++;
                    }
                }
            }
            _course.Save();
            RefreshCourseTable();
            _courseStatus.Text = Tr("schedule.ai_done", ("n", filled));
            Log.Info($"[课表窗口] AI 导入: {filled} 格");
        }
        catch (Exception e)
        {
            _courseStatus.Text = Tr("schedule.ai_exception", ("msg", e.Message));
            Log.Warning($"[课表窗口] AI 导入失败: {e.Message}");
        }
    }

    private static string PeriodField(JsonObject p, string key) =>
        p[key]?.GetValue<string>() ?? "";

    /// <summary>标题行单双周切换</summary>
    private void RebuildCourseWeekSwitch()
    {
        if (_courseWeekSwitch is null)
        {
            return;
        }
        _courseWeekSwitch.Children.Clear();
        var single = WeekCourseOf(_master, 1);
        var dual = WeekCourseOf(_master, 2);
        var doubleWeek = dual.Length > 0;
        _courseWeekSwitch.IsVisible = doubleWeek && !_linkageMode;
        if (!_courseWeekSwitch.IsVisible)
        {
            return;
        }

        var viewingDual = _course.Name == dual;
        // 勾=双周表
        var chkDual = new CheckBox
        {
            // 基线回正 抵消内容框下移
            Content = new TextBlock { Text = Tr("schedule.week_switch_double"), FontSize = 12, Margin = new Thickness(0, -2, 0, 0) },
            IsChecked = viewingDual,
            MinWidth = 0,
            MinHeight = 0,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        chkDual.IsCheckedChanged += (_, _) =>
        {
            if (chkDual.IsChecked != viewingDual)
            {
                SwitchCourseView(chkDual.IsChecked == true ? dual : single);
            }
        };
        _courseWeekSwitch.Children.Add(chkDual);
    }

    /// <summary>切换配置课表</summary>
    private void SwitchCourseView(string name)
    {
        if (name.Length == 0 || _course.Name == name)
        {
            return;
        }
        _course = CourseDoc.LoadOrDefault(name);
        Log.Info($"[课表窗口] 切换配置课表: {name}");
        RefreshCourseTable();
        UpdateCaptions();
    }

    /// <summary>同步单周条目</summary>
    private static void SyncSingleWeek(ScheduleProfile master, string course)
    {
        var doubleWeek = master.Courses.Any(c =>
            (c["weekTotal"]?.GetValue<int>() ?? 0) == 2 && (c["weekDiv"]?.GetValue<int>() ?? 0) == 2);
        if (!doubleWeek)
        {
            return;
        }
        foreach (var c in master.Courses)
        {
            if ((c["weekTotal"]?.GetValue<int>() ?? 0) == 2 && (c["weekDiv"]?.GetValue<int>() ?? 0) == 1)
            {
                c["course"] = course;
                return;
            }
        }
    }

    private void RefreshCourseTable()
    {
        var readOnly = _linkageMode;
        _blockSync = true;
        _courseRowsHost.Children.Clear();
        _courseCells.Clear();
        _courseHeadRangeTexts.Clear();
        _courseDayEditors.Clear();
        var nb = NonBreakIndices();
        _courseTableEmpty.IsVisible = nb.Count == 0;
        for (var tableRow = 0; tableRow < nb.Count; tableRow++)
        {
            var periodIdx = nb[tableRow];
            var p = _timetable.Periods[periodIdx];
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions(WeekCols), MinHeight = 34 };

            var head = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(new TextBlock
            {
                Text = Tr("schedule.period_n", ("n", tableRow + 1)),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
            });
            var rangeText = new TextBlock
            {
                Text = $"{PeriodField(p, "start")}~{PeriodField(p, "end")}",
                FontSize = 11,
                Opacity = 0.7,
                TextAlignment = TextAlignment.Center,
            };
            head.Children.Add(rangeText);
            _courseHeadRangeTexts.Add(rangeText);
            var headCell = MakeCell(head, tableRow, 0, onClick: () => SelectCourseCell(tableRow, 0));
            rowGrid.Children.Add(headCell);
            _courseCells.Add(headCell);

            var courses = _course.Courses.TryGetValue(periodIdx.ToString(), out var c) ? c : new JsonObject();
            var dayEditors = new TextBox?[7];
            for (var d = 0; d < 7; d++)
            {
                var text = courses[DayOrder[d]]?.GetValue<string>() ?? "";
                var edit = CellEdit(text);
                edit.IsReadOnly = readOnly;
                var row = tableRow;
                var col = d + 1;
                edit.GotFocus += (_, _) => SelectCourseCell(row, col);
                edit.TextChanged += (_, _) => OnCourseCellChanged(row, col, edit.Text ?? "");
                var cell = MakeCell(edit, row, col, onClick: () => SelectCourseCell(row, col));
                rowGrid.Children.Add(cell);
                _courseCells.Add(cell);
                dayEditors[d] = edit;
            }
            _courseDayEditors.Add(dayEditors);

            var r = tableRow;
            _courseRowsHost.Children.Add(MakeRowHost(rowGrid,
                () => SelectCourseCell(r, _courseSelCol < 0 ? 0 : Math.Min(_courseSelCol, 7))));
        }
        _blockSync = false;
        PaintCells(_courseCells, _courseSelRow, _courseSelCol);
        UpdateCourseInfo();
    }

    /// <summary>差分同步课表行</summary>
    private void SyncCourseTable()
    {
        var nb = NonBreakIndices();
        _blockSync = true;
        for (var tableRow = 0; tableRow < _courseDayEditors.Count && tableRow < nb.Count; tableRow++)
        {
            var periodIdx = nb[tableRow];
            var p = _timetable.Periods[periodIdx];
            _courseHeadRangeTexts[tableRow].Text = $"{PeriodField(p, "start")}~{PeriodField(p, "end")}";
            for (var d = 0; d < 7; d++)
            {
                var editor = _courseDayEditors[tableRow][d];
                if (editor is null)
                {
                    continue;
                }
                var text = _course.GetCell(periodIdx, DayOrder[d]);
                if (editor.Text != text)
                {
                    editor.Text = text;
                }
            }
        }
        _blockSync = false;
    }

    /// <summary>差分同步单个课程格</summary>
    private void SyncCourseCell(int row, int col)
    {
        var nb = NonBreakIndices();
        if (row < 0 || row >= _courseDayEditors.Count || row >= nb.Count || col < 1 || col > 7)
        {
            return;
        }
        var editor = _courseDayEditors[row][col - 1];
        if (editor is null)
        {
            return;
        }
        var text = _course.GetCell(nb[row], DayOrder[col - 1]);
        _blockSync = true;
        editor.Text = text;
        _blockSync = false;
    }

    private void SelectCourseCell(int row, int col)
    {
        var nb = NonBreakIndices();
        if (row >= nb.Count)
        {
            return;
        }
        _courseSelRow = row;
        _courseSelCol = col;
        PaintCells(_courseCells, row, col);
        UpdateCourseInfo();
    }

    private void UpdateCourseInfo()
    {
        var nb = NonBreakIndices();
        var hasSel = _courseSelRow >= 0 && _courseSelRow < nb.Count;
        ShowEditor(_courseEditorEmpty, _courseEditorHost, hasSel);
        if (!hasSel)
        {
            return;
        }
        var periodIdx = nb[_courseSelRow];
        var p = _timetable.Periods[periodIdx];
        var range = $"{PeriodField(p, "start")}~{PeriodField(p, "end")}";
        if (_courseSelCol <= 0)
        {
            _courseInfo.Text = $"{Tr("schedule.period_n", ("n", _courseSelRow + 1))} · {range}";
            HighlightSubjectButton(null);
            return;
        }
        var day = Tr($"timetable.{DayOrder[_courseSelCol - 1]}");
        _courseInfo.Text = $"{Tr("schedule.period_n", ("n", _courseSelRow + 1))} · {range} · {day}";
        HighlightSubjectButton(_course.GetCell(periodIdx, DayOrder[_courseSelCol - 1]));
    }

    private void RebuildSubjectFlow()
    {
        if (_subjectFlow is null)
        {
            return;
        }
        _subjectFlowDebounce?.Stop();
        _subjectFlow.Children.Clear();
        var names = _subject.SubjectNames();
        if (names.Count == 0)
        {
            // 空表回退 默认学科
            names = ScheduleStore.DefaultSubjectRows().Select(r => r["name"]?.GetValue<string>() ?? "").ToList();
        }
        foreach (var name in names)
        {
            var btn = new Button
            {
                Content = name,
                MinWidth = 56,
                Height = 36,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 8),
                FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            var subject = name;
            btn.Click += (_, _) => OnSubjectButtonClicked(subject);
            _subjectFlow.Children.Add(btn);
        }
        HighlightSubjectButton(CurrentCourseCellText());
    }

    /// <summary>防抖重建</summary>
    private void QueueSubjectFlowRebuild()
    {
        PulseTimer(ref _subjectFlowDebounce, TimeSpan.FromMilliseconds(300), (_, _) => RebuildSubjectFlow());
    }

    private string? CurrentCourseCellText()
    {
        var nb = NonBreakIndices();
        if (_courseSelRow < 0 || _courseSelRow >= nb.Count || _courseSelCol <= 0)
        {
            return null;
        }
        return _course.GetCell(nb[_courseSelRow], DayOrder[_courseSelCol - 1]);
    }

    private void HighlightSubjectButton(string? subject)
    {
        var accent = AccentBrush;
        foreach (var child in _subjectFlow.Children)
        {
            if (child is Button btn)
            {
                var selected = subject is { Length: > 0 } && (string)btn.Content! == subject;
                btn.Background = selected ? accent : Brushes.Transparent;
                btn.Foreground = selected ? Brushes.White : Brushes.Gray;
            }
        }
    }

    private void OnSubjectButtonClicked(string subject)
    {
        if (_courseSelCol <= 0 || _courseSelRow < 0)
        {
            return;
        }
        if (!WriteCourseCell(_courseSelRow, _courseSelCol, subject))
        {
            return;
        }
        Log.Debug($"[课表窗口] 科目按钮: {subject} @ 行{_courseSelRow} 列{_courseSelCol}");
        SyncCourseCell(_courseSelRow, _courseSelCol);
        HighlightSubjectButton(subject);
        var nb = NonBreakIndices();
        var nextRow = _courseSelRow + 1;
        var nextCol = _courseSelCol;
        if (nextRow >= nb.Count)
        {
            nextRow = 0;
            nextCol++;
            if (nextCol > 7)
            {
                Log.Debug("[课表窗口] 科目录入跳转结束: 已到末列");
                UpdateCourseInfo();
                return;
            }
        }
        SelectCourseCell(nextRow, nextCol);
    }

    private void OnCourseCellChanged(int row, int col, string text)
    {
        if (_blockSync || col == 0)
        {
            return;
        }
        if (!WriteCourseCell(row, col, text))
        {
            return;
        }
        if (row == _courseSelRow && col == _courseSelCol)
        {
            HighlightSubjectButton(text);
        }
    }

    private bool WriteCourseCell(int row, int col, string text)
    {
        if (_linkageMode)
        {
            return false;
        }
        var nb = NonBreakIndices();
        if (col <= 0 || row < 0 || row >= nb.Count)
        {
            return false;
        }
        _course.SetCell(nb[row], DayOrder[col - 1], text);
        QueueCourseSave();
        return true;
    }

    // 时间表页

    private Control BuildTimePage()
    {
        // 左: 时间轴
        _timeRowsHost = new StackPanel();
        _timeScroll = new ScrollViewer
        {
            Content = _timeRowsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 6, 0, 6),
        };
        // 表框叠层 滚动+缩放条
        var tableStack = new Grid();
        tableStack.Children.Add(_timeScroll);

        var commandBar = MakeToolRow(PeriodCommands());
        _timeCommandBar = commandBar;

        _timeCaption = MakeCaption();
        // 缩放防抖 140ms
        _zoomTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _zoomTimer.Tick += (_, _) =>
        {
            _zoomTimer?.Stop();
            RefreshTimeTable();
            // 视口中心锚定 防跳
            if (_timeScroll is { Bounds.Height: > 0 })
            {
                var target = Math.Max(0, _zoomAnchorMin * _timeScale + TimelinePadTop - _timeScroll.Bounds.Height / 2);
                var sc = _timeScroll;
                Dispatcher.UIThread.Post(() => sc.Offset = new Vector(0, target));
            }
        };
        var pctText = new TextBlock
        {
            Text = $"{_timeScale * 100:F0}%",
            FontSize = 11.5,
            MinWidth = 40,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.75,
        };
        void ApplyScale(double v)
        {
            // 取视口中心为锚
            if (_timeScroll is { Bounds.Height: > 0 })
            {
                _zoomAnchorMin = Math.Max(0, (_timeScroll.Offset.Y + _timeScroll.Bounds.Height / 2 - TimelinePadTop) / _timeScale);
            }
            _timeScale = Math.Clamp(v, TimeScaleMin, TimeScaleMax);
            pctText.Text = $"{_timeScale * 100:F0}%";
            _zoomTimer?.Stop();
            _zoomTimer?.Start();
        }
        Button MiniBtn(string label, double width, Action onClick)
        {
            var b = new Button
            {
                Content = label,
                Width = width,
                Height = 26,
                Padding = new Thickness(0),
                FontSize = 12.5,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            b.Click += (_, _) => onClick();
            return b;
        }
        var zoomBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 10, 10),
        };
        zoomBar.Children.Add(MiniBtn(Tr("timetable.zoom_fit"), 44, () =>
        {
            if (_timetable.Periods.Count == 0)
            {
                return;
            }
            // 24h 压进视口
            var viewH = _timeScroll?.Bounds.Height ?? 560;
            ApplyScale(viewH / (24 * 60));
        }));
        zoomBar.Children.Add(MiniBtn("−", 26, () => ApplyScale(_timeScale - 0.1)));
        zoomBar.Children.Add(pctText);
        zoomBar.Children.Add(MiniBtn("+", 26, () => ApplyScale(_timeScale + 0.1)));
        tableStack.Children.Add(zoomBar);
        var table = new Border
        {
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            Background = CardBg,
            ClipToBounds = true,
            Child = tableStack,
        };

        // 右: 时段编辑
        var (editorCard, editorEmpty, editorHost, _) = MakeEditorCard();
        _timeEditorCard = editorCard;
        _timeEmpty = editorEmpty;
        _timeEditHost = editorHost;
        // 类型 单选圆环
        _typeRadios = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var t in PeriodTypes)
        {
            var rb = new RadioButton
            {
                Content = t,
                GroupName = "periodType",
                FontSize = 12.5,
                MinWidth = 0,
                MinHeight = 0,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            var typeName = t;
            rb.IsCheckedChanged += (_, _) =>
            {
                if (rb.IsChecked == true)
                {
                    OnTypeSelected(typeName);
                }
            };
            _typeRadios.Children.Add(rb);
        }
        editorHost.Children.Add(MakeFieldRow(Tr("timetable.type"), _typeRadios));
        // 标题 防抖250ms 落盘
        _titleBox = new TextBox { FontSize = 12.5, Height = 32 };
        _titleBox.TextChanged += (_, _) =>
        {
            if (_blockSync || _linkageMode || _timeSelRow < 0 || _timeSelRow >= _timetable.Periods.Count)
            {
                return;
            }
            var pp = _timetable.Periods[_timeSelRow];
            if (string.IsNullOrWhiteSpace(_titleBox.Text))
            {
                pp.Remove("name");
            }
            else
            {
                pp["name"] = _titleBox.Text!.Trim();
            }
            PulseTimer(ref _titleDebounce, TimeSpan.FromMilliseconds(250), OnTitleDebounceTick);
        };
        editorHost.Children.Add(MakeFieldRow(Tr("timetable.title"), _titleBox));
        // 开始 / 结束 时分选择器
        _startPicker = MakeTimePicker();
        _endPicker = MakeTimePicker();
        _startPicker.SelectedTimeChanged += (_, _) => OnPickerTimeChanged();
        _endPicker.SelectedTimeChanged += (_, _) => OnPickerTimeChanged();
        editorHost.Children.Add(MakeFieldRow(Tr("timetable.start_time"), _startPicker));
        editorHost.Children.Add(MakeFieldRow(Tr("timetable.end_time"), _endPicker));
        // 确认 / 移除
        var confirmBtn = MakeSmallButton(Tr("timetable.btn_apply"), OnConfirmTime);
        confirmBtn.Classes.Add("accent");
        confirmBtn.Height = 34;
        confirmBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        var removeBtn = MakeSmallButton(Tr("timetable.btn_remove"), OnDeleteSelectedRow);
        removeBtn.Height = 34;
        var btnRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
        btnRow.Children.Add(confirmBtn);
        Grid.SetColumn(removeBtn, 2);
        btnRow.Children.Add(removeBtn);
        editorHost.Children.Add(btnRow);
        // 拒绝提示
        _timeMsg = new TextBlock { FontSize = 12, Foreground = Brushes.OrangeRed, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        editorHost.Children.Add(_timeMsg);
        ShowEditor(editorEmpty, editorHost, false);

        // 单位入标签 免后缀
        _classDurSpin = new NumericUpDown { Minimum = 1, Maximum = 120, Value = _timetable.DefaultClassDuration, HorizontalAlignment = HorizontalAlignment.Stretch };
        _classDurSpin.ValueChanged += (_, _) => OnDurationChanged();
        _breakDurSpin = new NumericUpDown { Minimum = 1, Maximum = 60, Value = _timetable.DefaultBreakDuration, HorizontalAlignment = HorizontalAlignment.Stretch };
        _breakDurSpin.ValueChanged += (_, _) => OnDurationChanged();
        var durRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        durRow.Children.Add(MakeField(Tr("timetable.default_class_duration"), _classDurSpin));
        var breakField = MakeField(Tr("timetable.default_break_duration"), _breakDurSpin);
        Grid.SetColumn(breakField, 2);
        durRow.Children.Add(breakField);
        var durCard = MakeCard(durRow, new Thickness(12, 10, 12, 10));
        durCard.Margin = new Thickness(0, 8, 0, 0);
        _durCard = durCard;
        var left = MakeTableArea(table, durCard);

        var root = MakePage(Tr("schedule.tab_time"), _timeCaption, commandBar, Tr("schedule.time_editor_title"), left, editorCard);
        RefreshTimeTable();
        return root;
    }

    /// <summary>时间轴重建</summary>
    private void RefreshTimeTable()
    {
        var readOnly = _linkageMode;
        _blockSync = true;
        _timeRowsHost.Children.Clear();
        _timeBlocks.Clear();
        _timeBlockTexts.Clear();
        _timeDots.Clear();
        var ps = _timetable.Periods;
        // 全轴 00:00-24:00
        var canvas = new Canvas { Height = DayMinutes * _timeScale + TimelinePadTop * 2, ClipToBounds = true };
        _timeCanvas = canvas;
        // 胶囊尺寸 ∝ scale
        _dotSize = Math.Clamp(36 * (_timeScale / 1.8), 24, 48);
        _dotH = Math.Clamp(4 * (_timeScale / 1.8), 3, 6);
        // 整点刻度 + 贯穿淡线
        for (var m = 0; m <= DayMinutes; m += 60)
        {
            var y = m * _timeScale;
            var hour = m / 60;
            var zone = hour switch
            {
                >= 6 and < 12 => Tr("timetable.am"),
                >= 12 and < 18 => Tr("timetable.pm"),
                >= 18 => Tr("timetable.evening"),
                _ => Tr("timetable.dawn"),
            };
            var label = new TextBlock
            {
                Text = $"{hour} {zone}",
                FontSize = 11,
                Opacity = 0.45,
            };
            Canvas.SetLeft(label, 6);
            Canvas.SetTop(label, y + TimelinePadTop - 8);
            canvas.Children.Add(label);
            var rule = new Border { Width = 3000, Height = 1, Background = new SolidColorBrush(Colors.Gray, 0.10) };
            Canvas.SetLeft(rule, 48);
            Canvas.SetTop(rule, y + TimelinePadTop);
            canvas.Children.Add(rule);
        }
        // 块分层 选中提亮
        for (var i = 0; i < ps.Count; i++)
        {
            var p = ps[i];
            var startMin = ToMin(PeriodField(p, "start"));
            var endMin = Math.Max(startMin, ToMin(PeriodField(p, "end")));
            var span = endMin - startMin;
            var type = PeriodField(p, "type");
            var isSel = i == _timeSelRow;
            var (fill, ink, inkOpacity) = PeriodPalette(type, isSel);
            var h = span * _timeScale;
            var ratio = _timeScale / 1.8;
            var fs = Math.Clamp(12.5 * ratio, 10, 22);
            // 高度不足 退化为色条
            var show = h >= fs * 1.44 + 4;
            var block = new Border
            {
                CornerRadius = new CornerRadius(0),
                Background = new SolidColorBrush(fill),
                ClipToBounds = true,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            var text = new TextBlock
            {
                Text = FormatPeriod(type, PeriodField(p, "start"), PeriodField(p, "end"), span),
                FontSize = fs,
                LineHeight = fs * 1.44,
                Foreground = new SolidColorBrush(ink),
                Opacity = inkOpacity,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 10, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (show)
            {
                block.Child = text;
            }
            else
            {
                ToolTip.SetTip(block, $"{type} {PeriodField(p, "start")}-{PeriodField(p, "end")} · {span}{Tr("timetable.minutes")}");
            }
            var idx = i;
            block.AddHandler(InputElement.PointerPressedEvent, (_, _) => SelectTimeRow(idx), RoutingStrategies.Bubble, true);
            Canvas.SetLeft(block, TimelineLeft);
            Canvas.SetTop(block, startMin * _timeScale + TimelinePadTop);
            block.Height = h;
            block.Width = BlockWidth(canvas);
            canvas.Children.Add(block);
            _timeBlocks.Add(block);
            _timeBlockTexts.Add(text);
        }
        // 分界胶囊 可拖
        for (var i = 0; i + 1 < ps.Count; i++)
        {
            if (readOnly || IsFixed(ps[i]) || IsFixed(ps[i + 1]))
            {
                continue;
            }
            var b = ToMin(PeriodField(ps[i], "end"));
            if (b != ToMin(PeriodField(ps[i + 1], "start")))
            {
                continue;
            }
            // 胶囊骑线
            var dot = new Border
            {
                Width = _dotSize,
                Height = _dotH,
                CornerRadius = new CornerRadius(_dotH / 2),
                Background = new SolidColorBrush(Colors.White, 0.55),
                Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                Tag = i,
            };
            ToolTip.SetTip(dot, Tr("schedule.drag_hint"));
            Canvas.SetLeft(dot, TimelineLeft + BlockWidth(canvas) / 2 - _dotSize / 2);
            Canvas.SetTop(dot, b * _timeScale + TimelinePadTop - _dotH / 2);
            dot.PointerPressed += OnBoundaryPressed;
            dot.PointerMoved += OnBoundaryMoved;
            dot.PointerReleased += OnBoundaryReleased;
            canvas.Children.Add(dot);
            _timeDots.Add(dot);
        }
        _timeRowsHost.Children.Add(canvas);
        canvas.SizeChanged += (_, _) =>
        {
            var w = BlockWidth(canvas);
            foreach (var b in _timeBlocks)
            {
                b.Width = w;
            }
            var dotX = TimelineLeft + w / 2 - _dotSize / 2;
            foreach (var d in _timeDots)
            {
                Canvas.SetLeft(d, dotX);
            }
        };
        _blockSync = false;
        // 首渲染滚动锚定
        if (_autoScrollPending && ps.Count > 0 && _timeScroll is not null)
        {
            var firstMin = ToMin(PeriodField(ps[0], "start"));
            var target = Math.Max(0, (firstMin - 60) * _timeScale);
            _autoScrollPending = false;
            var scroller = _timeScroll;
            Dispatcher.UIThread.Post(() => scroller.Offset = new Vector(0, target));
        }
        if (_timeSelRow >= ps.Count)
        {
            _timeSelRow = ps.Count - 1;
        }
        SyncPickers();
    }

    /// <summary>块宽 兜底640</summary>
    private static double BlockWidth(Canvas canvas) =>
        Math.Max(80, (canvas.Bounds.Width > 0 ? canvas.Bounds.Width : 640) - TimelineLeft - 10);

    /// <summary>块配色 选中实色</summary>
    private (Color Fill, Color Ink, double InkOpacity) PeriodPalette(string type, bool selected)
    {
        // 课程蓝/课间绿/活动黄
        var baseColor = type switch
        {
            "课间" => Color.FromRgb(0x63, 0x99, 0x22),
            "活动" => Color.FromRgb(0xBA, 0x75, 0x17),
            _ => Color.FromRgb(0x37, 0x8A, 0xDD),
        };
        if (selected)
        {
            return (baseColor, Colors.White, 1);
        }
        // 暗压亮 亮混白
        return ThemeSense.IsDark(this)
            ? (Shade(baseColor, 0.45), Colors.White, 0.86)
            : (Tint(baseColor, 0.78), Shade(baseColor, 0.62), 1);
    }

    /// <summary>线性压暗</summary>
    private static Color Shade(Color c, double k) => Color.FromRgb(
        (byte)Math.Min(255, c.R * k),
        (byte)Math.Min(255, c.G * k),
        (byte)Math.Min(255, c.B * k));

    /// <summary>白混提亮</summary>
    private static Color Tint(Color c, double k) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * k),
        (byte)(c.G + (255 - c.G) * k),
        (byte)(c.B + (255 - c.B) * k));

    /// <summary>块文案</summary>
    private static string FormatPeriod(string type, string start, string end, int span) =>
        $"{type}  {start} - {end}  ({span} {Tr("timetable.minutes")})";

    // 分界拖拽 5min 量化

    private void OnBoundaryPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_linkageMode || sender is not Border handle || handle.Tag is not int idx)
        {
            return;
        }
        var ps = _timetable.Periods;
        if (idx < 0 || idx + 1 >= ps.Count)
        {
            return;
        }
        var lo = ToMin(PeriodField(ps[idx], "start")) + 5;
        var hi = ToMin(PeriodField(ps[idx + 1], "end")) - 5;
        if (lo > hi)
        {
            return;
        }
        _dragIdx = idx;
        _dragY0 = e.GetPosition(null).Y;
        _dragOrigB = ToMin(PeriodField(ps[idx], "end"));
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OnBoundaryMoved(object? sender, PointerEventArgs e)
    {
        if (_dragIdx < 0 || _dragIdx + 1 >= _timetable.Periods.Count)
        {
            return;
        }
        var ps = _timetable.Periods;
        var lo = ToMin(PeriodField(ps[_dragIdx], "start")) + 5;
        var hi = ToMin(PeriodField(ps[_dragIdx + 1], "end")) - 5;
        var b = Math.Clamp(_dragOrigB + (int)Math.Round((e.GetPosition(null).Y - _dragY0) / _timeScale / 5) * 5, lo, hi);
        ps[_dragIdx]["end"] = MinStr(b);
        ps[_dragIdx + 1]["start"] = MinStr(b);
        // 就地改几何 保Capture
        var prevStart = ToMin(PeriodField(ps[_dragIdx], "start"));
        if (_dragIdx < _timeBlocks.Count)
        {
            _timeBlocks[_dragIdx].Height = (b - prevStart) * _timeScale;
            _timeBlockTexts[_dragIdx].Text =
                FormatPeriod(PeriodField(ps[_dragIdx], "type"), PeriodField(ps[_dragIdx], "start"), MinStr(b), b - prevStart);
        }
        if (_dragIdx + 1 < _timeBlocks.Count)
        {
            var nextEnd = ToMin(PeriodField(ps[_dragIdx + 1], "end"));
            Canvas.SetTop(_timeBlocks[_dragIdx + 1], b * _timeScale + TimelinePadTop);
            _timeBlocks[_dragIdx + 1].Height = (nextEnd - b) * _timeScale;
            _timeBlockTexts[_dragIdx + 1].Text =
                FormatPeriod(PeriodField(ps[_dragIdx + 1], "type"), MinStr(b), PeriodField(ps[_dragIdx + 1], "end"), nextEnd - b);
        }
        if (sender is Border dot)
        {
            Canvas.SetTop(dot, b * _timeScale + TimelinePadTop - _dotH / 2);
        }
    }

    private void OnBoundaryReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragIdx < 0)
        {
            return;
        }
        e.Pointer.Capture(null);
        _timetable.Save();
        Log.Info($"[课表窗口] 拖分界线: 段{_dragIdx} -> {PeriodField(_timetable.Periods[_dragIdx], "start")}~{PeriodField(_timetable.Periods[_dragIdx], "end")}");
        RefreshCourseTable();
        _dragIdx = -1;
    }

    /// <summary>确认保存</summary>
    private void OnConfirmTime()
    {
        if (_timeSelRow < 0 || _timeSelRow >= _timetable.Periods.Count)
        {
            return;
        }
        _timetable.Save();
        RefreshCourseTable();
        ShowTimeMsg(Tr("timetable.saved"));
    }
    /// <summary>改写当前段类型</summary>
    private void OnTypeSelected(string type)
    {
        if (_linkageMode || _timeSelRow < 0 || _timeSelRow >= _timetable.Periods.Count)
        {
            return;
        }
        _timetable.Periods[_timeSelRow]["type"] = type;
        _timetable.Save();
        Log.Info($"[课表窗口] 改类型: 行{_timeSelRow} -> {type}");
        RefreshTimeTable();
        RefreshCourseTable();
    }

    private void SelectTimeRow(int row)
    {
        if (row < 0 || row >= _timetable.Periods.Count)
        {
            return;
        }
        _timeSelRow = row;
        RefreshTimeTable();
    }

    private void SyncPickers()
    {
        var hasSel = _timeSelRow >= 0 && _timeSelRow < _timetable.Periods.Count;
        ShowEditor(_timeEmpty, _timeEditHost, hasSel);
        // 先上闸 防回填触发
        _blockSync = true;
        if (_typeRadios is not null && hasSel)
        {
            var period = _timetable.Periods[_timeSelRow];
            var cur = PeriodField(period, "type");
            for (var i = 0; i < _typeRadios.Children.Count && i < PeriodTypes.Length; i++)
            {
                if (_typeRadios.Children[i] is RadioButton rb)
                {
                    rb.IsChecked = PeriodTypes[i] == cur;
                }
            }
            _titleBox.Text = period["name"]?.GetValue<string>() ?? "";
        }
        if (hasSel)
        {
            var p = _timetable.Periods[_timeSelRow];
            _startPicker.SelectedTime = ToSpan(PeriodField(p, "start"));
            _endPicker.SelectedTime = ToSpan(PeriodField(p, "end"));
        }
        else
        {
            var next = _timetable.GetNextStartTime();
            _startPicker.SelectedTime = ToSpan(next);
            _endPicker.SelectedTime = ToSpan(MinStr(ToMin(next) + _timetable.DefaultClassDuration));
        }
        _blockSync = false;
    }

    // 时间编辑 工具
    private static bool IsFixed(JsonObject p) => p["fixed"]?.GetValue<bool>() == true;

    /// <summary>表单行 左标签右控件</summary>
    private static Grid MakeFieldRow(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*"), Margin = new Thickness(0, 1, 0, 1) };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static int ToMin(string t)
    {
        var parts = t.Split(':');
        return int.TryParse(parts[0], out var h) && int.TryParse(parts.Length > 1 ? parts[1] : "", out var m)
            ? h * 60 + m
            : 480;
    }

    /// <summary>分钟→HH:mm 保24:00</summary>
    private static string MinStr(int m) =>
        m >= DayMinutes ? "24:00" : $"{m / 60:00}:{m % 60:00}";

    /// <summary>落位校验 邻段不叠</summary>
    private bool CanPlace(int idx, string start, string end)
    {
        var s = ToMin(start);
        var e = ToMin(end);
        if (e > DayMinutes || s >= e)
        {
            return false;
        }
        var ps = _timetable.Periods;
        if (idx > 0 && ToMin(PeriodField(ps[idx - 1], "end")) > s)
        {
            return false;
        }
        if (idx + 1 < ps.Count && e > ToMin(PeriodField(ps[idx + 1], "start")))
        {
            return false;
        }
        return true;
    }

    private void ShowTimeMsg(string text)
    {
        _timeMsg.Text = text;
        _timeMsg.IsVisible = true;
        PulseTimer(ref _timeMsgTimer, TimeSpan.FromSeconds(3), OnTimeMsgTick);
    }

    private void OnTimeMsgTick(object? sender, EventArgs e)
    {
        _timeMsg.IsVisible = false;
        _timeMsgTimer?.Stop();
    }

    // 标题防抖落盘
    private void OnTitleDebounceTick(object? sender, EventArgs e)
    {
        _titleDebounce?.Stop();
        _timetable.Save();
        RefreshCourseTable();
    }

    /// <summary>TimePicker提交 校验回滚</summary>
    private void OnPickerTimeChanged()
    {
        if (_blockSync || _linkageMode || _timeSelRow < 0 || _timeSelRow >= _timetable.Periods.Count)
        {
            return;
        }
        OnTimeCommitted(FromSpan(_startPicker.SelectedTime), FromSpan(_endPicker.SelectedTime));
    }

    /// <summary>HH:mm → TimeSpan</summary>
    private static TimeSpan ToSpan(string hhmm) =>
        TimeSpan.TryParse(hhmm, out var t) ? t : new TimeSpan(8, 0, 0);

    /// <summary>TimeSpan → HH:mm</summary>
    private static string FromSpan(TimeSpan? t) =>
        t is { } v ? $"{v.Hours:00}:{v.Minutes:00}" : "08:00";

    /// <summary>TimePicker 24h 5min步进</summary>
    private static TimePicker MakeTimePicker() => new()
    {
        ClockIdentifier = "24HourClock",
        MinuteIncrement = 5,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        FontSize = 13,
    };

    /// <summary>提交起止时间 校验 + 写盘</summary>
    private void OnTimeCommitted(string start, string end)
    {
        if (!CanPlace(_timeSelRow, start, end))
        {
            ShowTimeMsg(Tr("schedule.msg_rejected"));
            SyncPickers();
            return;
        }
        var p = _timetable.Periods[_timeSelRow];
        p["start"] = start;
        p["end"] = end;
        Log.Info($"[课表窗口] 修改时间行: 行{_timeSelRow} -> {start}~{end}");
        _timetable.Save();
        // RefreshTimeTable 内含SyncPickers
        RefreshTimeTable();
        RefreshCourseTable();
    }

    /// <summary>新增时段</summary>
    private void AddPeriod(string periodType)
    {
        if (_linkageMode)
        {
            return;
        }
        var start = _timetable.GetNextStartTime();
        var dur = periodType == "课间" ? _timetable.DefaultBreakDuration : _timetable.DefaultClassDuration;
        var end = MinStr(ToMin(start) + Math.Max(1, dur));
        Log.Info($"[课表窗口] 添加时段: {periodType} {start}~{end}");
        _timetable.AddPeriod(periodType, start, end);
        _course.Courses[(_timetable.Periods.Count - 1).ToString()] = new JsonObject();
        _timetable.Save();
        _course.Save();
        RefreshCourseTable();
        // SelectTimeRow 内含重建
        SelectTimeRow(_timetable.Periods.Count - 1);
    }

    private void OnDeleteSelectedRow()
    {
        if (_linkageMode || _timetable.Periods.Count == 0)
        {
            return;
        }
        // 按页取选中 防stale索引
        int periodIdx;
        if (_currentTag == "time")
        {
            if (_timeSelRow < 0)
            {
                return;
            }
            periodIdx = _timeSelRow;
        }
        else if (_currentTag == "course")
        {
            var nb = NonBreakIndices();
            if (_courseSelRow < 0 || _courseSelRow >= nb.Count)
            {
                return;
            }
            periodIdx = nb[_courseSelRow];
        }
        else
        {
            return;
        }
        if (periodIdx < 0 || periodIdx >= _timetable.Periods.Count)
        {
            return;
        }
        Log.Info($"[课表窗口] 删除时段: 索引={periodIdx}");
        _timetable.RemovePeriod(periodIdx);
        _course.RemovePeriodIndex(periodIdx);
        _timeSelRow = Math.Min(periodIdx, _timetable.Periods.Count - 1);
        _courseSelRow = -1;
        _timetable.Save();
        _course.Save();
        RefreshTimeTable();
        RefreshCourseTable();
    }

    private void OnDurationChanged()
    {
        if (_blockSync || _linkageMode)
        {
            return;
        }
        _timetable.DefaultClassDuration = (int)(_classDurSpin.Value ?? 40);
        _timetable.DefaultBreakDuration = (int)(_breakDurSpin.Value ?? 10);
        _timetable.Save();
        SyncPickers();
    }

    // 调课页

    private Control BuildAdjustPage()
    {
        var today = DateOnly.FromDateTime(AppUtils.PreciseNow());
        _adjustMonday = MondayOf(today);

        _adjustCaption = MakeCaption();
        // 周导航命令区
        var navRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        navRow.Children.Add(MakeIconToolButton(FASymbol.ChevronLeft, Tr("schedule.adjust_prev"), () => { _adjustMonday = _adjustMonday.AddDays(-7); RefreshAdjustGrid(); }));
        _adjustWeekLabel = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 150,
            TextAlignment = TextAlignment.Center,
        };
        navRow.Children.Add(_adjustWeekLabel);
        navRow.Children.Add(MakeIconToolButton(FASymbol.ChevronRight, Tr("schedule.adjust_next"), () => { _adjustMonday = _adjustMonday.AddDays(7); RefreshAdjustGrid(); }));
        navRow.Children.Add(MakeTextToolButton(null, Tr("schedule.adjust_today"), () =>
        {
            _adjustMonday = MondayOf(DateOnly.FromDateTime(AppUtils.PreciseNow()));
            RefreshAdjustGrid();
        }));

        // 左: 周视图
        _adjustGridHost = new StackPanel();
        var table = new Border
        {
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            Background = CardBg,
            CornerRadius = new CornerRadius(0),
            ClipToBounds = true,
            Child = new ScrollViewer
            {
                Content = _adjustGridHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
        var left = MakeTableArea(table);

        // 右: 紧凑编辑卡
        var (editorCard, editorEmpty, editorHost, _) = MakeEditorCard();
        _adjustEmpty = editorEmpty;
        _adjustEditHost = editorHost;
        _adjustSwapBanner = new Border
        {
            Background = SelCellBg,
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(10, 6, 10, 6),
            IsVisible = false,
            Child = new TextBlock { Text = Tr("schedule.adjust_swap_banner"), FontSize = 12, TextWrapping = TextWrapping.Wrap },
        };
        _adjustCellLabel = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _adjustSourceLabel = new TextBlock { FontSize = 11, Opacity = 0.6, IsVisible = false, Margin = new Thickness(0, 2, 0, 0) };
        // 整日类型内联标题行
        _adjustDayModeCombo = new ComboBox
        {
            FontSize = 12,
            Height = 30,
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var label in new[] { Tr("schedule.day_mode_normal"), Tr("schedule.day_mode_plan"), Tr("schedule.day_mode_exam"), Tr("schedule.day_mode_holiday") })
        {
            _adjustDayModeCombo.Items.Add(label);
        }
        _adjustDayModeCombo.SelectedIndex = DayModeNormal;
        _adjustDayModeCombo.SelectionChanged += (_, _) => RebuildDayModeForm();
        ToolTip.SetTip(_adjustDayModeCombo, Tr("schedule.day_mode"));
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 0 };
        titleStack.Children.Add(_adjustCellLabel);
        titleStack.Children.Add(_adjustSourceLabel);
        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
        titleRow.Children.Add(titleStack);
        Grid.SetColumn(_adjustDayModeCombo, 2);
        titleRow.Children.Add(_adjustDayModeCombo);
        editorHost.Children.Add(_adjustSwapBanner);
        editorHost.Children.Add(titleRow);
        _adjustSubjectBox = new TextBox { PlaceholderText = Tr("schedule.sub_course"), FontSize = 13, Height = 34 };
        _adjustSubjectBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OnAdjustSaveCell();
            }
        };
        _adjustRoomBox = new TextBox { PlaceholderText = Tr("schedule.adjust_room"), FontSize = 13, Height = 34 };
        var cellRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        cellRow.Children.Add(_adjustSubjectBox);
        Grid.SetColumn(_adjustRoomBox, 1);
        cellRow.Children.Add(_adjustRoomBox);
        // 主操作 低频入菜单
        _adjustSaveCellBtn = MakeSmallButton(Tr("schedule.adjust_save_cell"), OnAdjustSaveCell);
        _adjustSaveCellBtn.Classes.Add("accent");
        _adjustSaveCellBtn.Height = 34;
        _adjustSaveCellBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        _adjustSwapBtn = MakeSmallButton(Tr("schedule.adjust_swap"), OnAdjustSwapMode);
        _adjustSwapBtn.Height = 34;
        _adjustSetDefaultBtn = new FAMenuFlyoutItem { Text = Tr("schedule.adjust_set_default") };
        _adjustSetDefaultBtn.Click += (_, _) => OnAdjustSetDefault();
        var clearMenuItem = new FAMenuFlyoutItem { Text = Tr("schedule.adjust_clear_patch") };
        clearMenuItem.Click += (_, _) => OnAdjustClearPatch();
        var moreMenu = new FAMenuFlyout { Items = { _adjustSetDefaultBtn, clearMenuItem } };
        var moreBtn = new Button
        {
            Content = "⋯",
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            FontSize = 15,
        };
        moreBtn.Flyout = moreMenu;
        ToolTip.SetTip(moreBtn, Tr("schedule.sub_more"));
        var cellOps = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,Auto,6,Auto") };
        cellOps.Children.Add(_adjustSaveCellBtn);
        Grid.SetColumn(_adjustSwapBtn, 2);
        cellOps.Children.Add(_adjustSwapBtn);
        Grid.SetColumn(moreBtn, 4);
        cellOps.Children.Add(moreBtn);
        editorHost.Children.Add(cellRow);
        editorHost.Children.Add(cellOps);

        // 整日表单 非常规类型
        _dayModeHost = new StackPanel { Spacing = 8 };
        _dayOps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = false };
        _dayOps.Children.Add(MakeSmallButton(Tr("schedule.day_apply"), OnApplyDayMode));
        _dayOps.Children.Add(MakeSmallButton(Tr("schedule.adjust_clear_day"), OnAdjustClearDay));
        editorHost.Children.Add(_dayModeHost);
        editorHost.Children.Add(_dayOps);

        // 提示 3s 自动消失
        _adjustMsg = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Brushes.OrangeRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        editorHost.Children.Add(_adjustMsg);

        // 摘要 空即隐藏
        _adjustSummary = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 16,
            IsVisible = false,
        };
        editorHost.Children.Add(_adjustSummary);
        ShowEditor(editorEmpty, editorHost, false);
        RebuildDayModeForm();

        var root = MakePage(Tr("schedule.tab_adjust"), _adjustCaption, navRow, Tr("schedule.adjust_editor_title"), left, editorCard);
        RefreshAdjustGrid();
        return root;
    }

    /// <summary>课表分档缓存</summary>
    private static CourseDoc LoadCourseCached(string name, Dictionary<string, CourseDoc>? cache)
    {
        if (cache is null)
        {
            return CourseDoc.LoadOrDefault(name);
        }
        if (!cache.TryGetValue(name, out var doc))
        {
            doc = CourseDoc.LoadOrDefault(name);
            cache[name] = doc;
        }
        return doc;
    }

    /// <summary>周视图重建</summary>
    private void RefreshAdjustGrid()
    {
        if (_adjustGridHost is null)
        {
            return;
        }
        _adjustGridHost.Children.Clear();
        _adjustCells.Clear();
        var monday = _adjustMonday;
        var weekInfo = TimetableScheduleProvider.GetWeekInfo();
        var weekNum = ScheduleRules.GetWeekNumber(monday, _master.WeekStart);
        _adjustWeekLabel.Text = $"{monday:MM-dd} ~ {monday.AddDays(6):MM-dd}" +
                                (weekNum is null ? "" : $" · {Tr("schedule.rotation_week_chip", ("n", weekNum.Value))}") +
                                (weekInfo is { Parity: { } p } && weekNum == weekInfo.Value.Week ? $" · {p}" : "");

        var nb = NonBreakIndices();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(WeekCols) };
        header.Children.Add(MakeCell(CellText(Tr("timetable.period"), true), 0, 0, header: true));
        var todayDate = DateOnly.FromDateTime(AppUtils.PreciseNow());
        for (var c = 0; c < 7; c++)
        {
            // 周日起列 对位
            var key = DayOrder[c];
            var date = monday.AddDays((c + 6) % 7);
            var isToday = date == todayDate;
            var head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var dayText = new TextBlock
            {
                Text = Tr($"timetable.{key}"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                Opacity = isToday ? 1 : 0.8,
            };
            var dateText = new TextBlock
            {
                Text = $"{date:MM-dd}",
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                Opacity = isToday ? 0.9 : 0.5,
            };
            // null盖主题刷 仅今日设色
            if (isToday)
            {
                dayText.Foreground = AccentBrush;
                dateText.Foreground = AccentBrush;
            }
            head.Children.Add(dayText);
            head.Children.Add(dateText);
            header.Children.Add(MakeCell(head, 0, c + 1, header: true));
        }
        _adjustGridHost.Children.Add(header);

        var rowsHost = new StackPanel();
        var courseCache = new Dictionary<string, CourseDoc>();
        for (var r = 0; r < nb.Count; r++)
        {
            var periodIdx = nb[r];
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions(WeekCols), MinHeight = 42 };
            var pd = _timetable.Periods[periodIdx];
            var headStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            headStack.Children.Add(new TextBlock { Text = Tr("schedule.period_n", ("n", r + 1)), FontSize = 12, FontWeight = FontWeight.Medium, TextAlignment = TextAlignment.Center });
            headStack.Children.Add(new TextBlock { Text = $"{PeriodField(pd, "start")}~{PeriodField(pd, "end")}", FontSize = 10, Opacity = 0.55, TextAlignment = TextAlignment.Center });
            rowGrid.Children.Add(MakeCell(headStack, r, 0));
            for (var c = 0; c < 7; c++)
            {
                var date = monday.AddDays((c + 6) % 7);
                var key = DayOrder[c];
                var content = AdjustCellContent(date, periodIdx, key, courseCache);
                var cell = MakeCell(content.View, r, c + 1, onClick: () => SelectAdjustCell(date, periodIdx));
                _adjustCells[(date, periodIdx)] = cell;
                rowGrid.Children.Add(cell);
            }
            rowsHost.Children.Add(rowGrid);
        }
        _adjustGridHost.Children.Add(rowsHost);
        PaintAdjustSelection();
        RefreshAdjustEditor();
    }

    /// <summary>选中/源/今日 只改背景</summary>
    private void PaintAdjustSelection()
    {
        var today = DateOnly.FromDateTime(AppUtils.PreciseNow());
        foreach (var (key, cell) in _adjustCells)
        {
            var isSel = key.Date == _adjustSelDate && key.Period == _adjustSelPeriod;
            var isSwapSrc = _adjustSwap is { } s && s.Date == key.Date && s.Period == key.Period;
            cell.Background = isSel || isSwapSrc ? SelCellBg : key.Date == today ? SelRowBg : Brushes.Transparent;
        }
    }

    /// <summary>格内容 覆盖出胶囊</summary>
    private (StackPanel View, string Source, string Subject, bool WholeDay) AdjustCellContent(DateOnly date, int periodIdx, string dayKey,
        Dictionary<string, CourseDoc>? cache = null)
    {
        var view = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var ov = ScheduleRules.ResolveOverride(_master, date);
        var patches = ScheduleRules.ResolvePatches(_master, date, _timetable);
        var patch = patches.FirstOrDefault(pp => pp.Index == periodIdx);
        string subject;
        string source;
        if (ov is not null && ov.Kind != "plan")
        {
            subject = ov.Name;
            source = ov.Kind == "exam" ? Tr("schedule.src_exam") : Tr("schedule.src_holiday");
            view.Children.Add(new TextBlock
            {
                Text = subject,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Opacity = 0.75,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            view.Children.Add(MakeBadge(source, ov.Kind == "exam" ? ExamBrush : HolidayBrush));
            return (view, source, subject, true);
        }
        var courseName = (ov is not null ? ov.CourseName : null)
                         ?? ScheduleRules.ResolveCourse(_master, date)
                         ?? "";
        var course = LoadCourseCached(courseName, cache);
        subject = patch is not null && !patch.NeedsCheck ? patch.Subject : course.GetCell(periodIdx, dayKey);
        if (subject.Length > 0)
        {
            view.Children.Add(new TextBlock
            {
                Text = subject,
                FontSize = 12.5,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        if (patch is not null && patch.NeedsCheck)
        {
            source = Tr("schedule.src_check");
            view.Children.Add(MakeBadge(source, CheckBrush));
        }
        else if (patch is not null)
        {
            source = Tr("schedule.src_patch");
            view.Children.Add(MakeBadge(source, PatchBrush));
        }
        else
        {
            source = Tr("schedule.src_normal");
        }
        return (view, source, subject, false);
    }

    /// <summary>角标胶囊</summary>
    private static Border MakeBadge(string text, IBrush brush)
    {
        var rgb = brush is SolidColorBrush sc ? sc.Color : Colors.Gray;
        return new Border
        {
            Background = new SolidColorBrush(rgb, 0.20),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 1, 7, 1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                Foreground = brush,
                TextAlignment = TextAlignment.Center,
            },
        };
    }

    private void SelectAdjustCell(DateOnly date, int periodIdx)
    {
        // 源格取消 目标格完成
        if (_adjustSwap is { } from)
        {
            if (from.Date == date && from.Period == periodIdx)
            {
                CancelAdjustSwap();
            }
            else
            {
                DoAdjustSwap(from, date, periodIdx);
                _adjustSwap = null;
                EndSwapUi();
            }
            RefreshAdjustGrid();
            return;
        }
        var changed = _adjustSelDate != date || _adjustSelPeriod != periodIdx;
        _adjustSelDate = date;
        _adjustSelPeriod = periodIdx;
        if (changed)
        {
            // 换格才回填 防吞输入
            _adjustEditorKey = null;
        }
        PaintAdjustSelection();
        RefreshAdjustEditor();
    }

    private void CancelAdjustSwap()
    {
        _adjustSwap = null;
        EndSwapUi();
        Log.Info("[课表窗口] 调换已取消");
    }

    private void EndSwapUi()
    {
        _adjustSwapBtn.Content = Tr("schedule.adjust_swap");
        _adjustSwapBanner.IsVisible = false;
    }

    /// <summary>对调=互换单节覆盖</summary>
    private void DoAdjustSwap((DateOnly Date, int Period, string Subject) from, DateOnly toDate, int toPeriod)
    {
        var toContent = AdjustCellContent(toDate, toPeriod, DayKeyOf(toDate));
        var fromSubject = from.Subject;
        var toSubject = toContent.Subject;
        WritePatch(from.Date, from.Period, toSubject, "", PeriodField(_timetable.Periods[from.Period], "start"), Tr("schedule.adjust_swap_note"));
        WritePatch(toDate, toPeriod, fromSubject, "", PeriodField(_timetable.Periods[toPeriod], "start"), Tr("schedule.adjust_swap_note"));
        Log.Info($"[课表窗口] 调课对调: {from.Date:MM-dd}第{from.Period}节 <-> {toDate:MM-dd}第{toPeriod}节");
    }

    private static string DayKeyOf(DateOnly date) => DayOrder[(int)date.DayOfWeek];

    /// <summary>删单节覆盖</summary>
    private void RemovePatchAt(DateOnly date, int periodIdx) =>
        _master.Patches.RemoveAll(p =>
            DateOnly.TryParse(p["date"]?.GetValue<string>(), out var d) && d == date &&
            ScheduleRules.ReadPeriod(p) == periodIdx);

    /// <summary>写单节覆盖 去重</summary>
    private void WritePatch(DateOnly date, int periodIdx, string subject, string room, string expectStart, string note = "")
    {
        RemovePatchAt(date, periodIdx);
        _master.Patches.Add(new JsonObject
        {
            ["date"] = date.ToString("yyyy-MM-dd"),
            ["period"] = periodIdx,
            ["subject"] = subject,
            ["room"] = room,
            ["expectStart"] = expectStart,
            ["note"] = note,
        });
        _master.Save();
        Log.Info($"[课表窗口] 单节覆盖: {date:yyyy-MM-dd} 第{periodIdx}节 -> {subject}");
    }

    /// <summary>写整日覆盖 同日去重</summary>
    private bool WriteOverride(DateOnly date, JsonObject entry)
    {
        var before = _master.Overrides.Count;
        _master.Overrides.RemoveAll(o =>
            DateOnly.TryParse(o["start"]?.GetValue<string>(), out var s) && s <= date &&
            DateOnly.TryParse(o["end"]?.GetValue<string>(), out var e) && e >= date);
        var replaced = _master.Overrides.Count != before;
        _master.Overrides.Add(entry);
        _master.Save();
        Log.Info($"[课表窗口] 整日覆盖: {date:yyyy-MM-dd} kind={entry["kind"]}{(replaced ? " (已替换原有覆盖)" : "")}");
        return replaced;
    }

    private void RefreshAdjustEditor()
    {
        var nb = NonBreakIndices();
        var hasSel = _adjustSelPeriod >= 0 && _adjustSelDate != default && nb.Contains(_adjustSelPeriod);
        var hasSwap = _adjustSwap is not null;
        if (!hasSel && !hasSwap)
        {
            ShowEditor(_adjustEmpty, _adjustEditHost, false);
            return;
        }
        ShowEditor(_adjustEmpty, _adjustEditHost, true);
        var date = hasSwap ? _adjustSwap!.Value.Date : _adjustSelDate;
        var period = hasSwap ? _adjustSwap!.Value.Period : _adjustSelPeriod;
        var key = (date, period);
        var content = AdjustCellContent(date, period, DayKeyOf(date));
        _adjustCellLabel.Text = $"{date:MM-dd} {Tr($"timetable.{DayKeyOf(date)}")} · {Tr("schedule.period_n", ("n", nb.IndexOf(period) + 1))}";
        // 仅覆盖来源显示
        if (hasSwap)
        {
            _adjustSourceLabel.Text = string.Format(Tr("schedule.adjust_swap_pending"), _adjustSwap!.Value.Date.ToString("MM-dd"), _adjustSwap.Value.Subject);
            _adjustSourceLabel.IsVisible = true;
        }
        else
        {
            _adjustSourceLabel.Text = content.Source;
            _adjustSourceLabel.IsVisible = content.Source != Tr("schedule.src_normal");
        }
        // 换目标才回填 防吞输入
        if (_adjustEditorKey != key)
        {
            _adjustSubjectBox.Text = content.Subject;
            _adjustRoomBox.Text = "";
            _adjustEditorKey = key;
            // 整日类型随既有覆盖
            var ovKind = ScheduleRules.ResolveOverride(_master, date)?.Kind;
            _adjustDayModeCombo.SelectedIndex = ovKind switch
            {
                "plan" => DayModePlan,
                "exam" => DayModeExam,
                "holiday" => DayModeHoliday,
                _ => DayModeNormal,
            };
            RebuildDayModeForm();
        }
        var wholeDay = content.WholeDay;
        // 先全启 再按态禁
        EnableAdjustEditor();
        _adjustSaveCellBtn.IsEnabled = !wholeDay && !hasSwap;
        _adjustSwapBtn.IsEnabled = !wholeDay;
        _adjustSetDefaultBtn.IsEnabled = !wholeDay && !hasSwap;
        _adjustSubjectBox.IsEnabled = !wholeDay && !hasSwap;
        _adjustRoomBox.IsEnabled = !wholeDay && !hasSwap;
        _adjustSubjectBox.PlaceholderText = wholeDay ? Tr("schedule.hint_day_locked") : Tr("schedule.sub_course");
        var summary = BuildAdjustSummary(date);
        _adjustSummary.Text = summary;
        _adjustSummary.IsVisible = summary.Length > 0;
    }

    private void EnableAdjustEditor()
    {
        _adjustSubjectBox.IsEnabled = true;
        _adjustRoomBox.IsEnabled = true;
        _adjustSaveCellBtn.IsEnabled = true;
        _adjustSwapBtn.IsEnabled = true;
        _adjustSetDefaultBtn.IsEnabled = true;
        _adjustDayModeCombo.IsEnabled = true;
        if (_adjustDayNameBox is { } nameBox)
        {
            nameBox.IsEnabled = true;
        }
        if (_adjustPlanCombo is { } planCombo)
        {
            planCombo.IsEnabled = true;
        }
    }

    /// <summary>当日摘要</summary>
    private string BuildAdjustSummary(DateOnly date)
    {
        var lines = new List<string>();
        var ov = ScheduleRules.ResolveOverride(_master, date);
        if (ov is not null)
        {
            var kindLabel = ov.Kind switch
            {
                "plan" => Tr("schedule.day_mode_plan"),
                "exam" => Tr("schedule.day_mode_exam"),
                _ => Tr("schedule.day_mode_holiday"),
            };
            lines.Add($"{Tr("schedule.adjust_day_title")} · {kindLabel} → {(ov.Kind == "plan" ? ov.CourseName : ov.Name)}");
        }
        else
        {
            // 仅轮换命中提示
            var hit = ScheduleRules.ResolveCourse(_master, date);
            var parity = ScheduleRules.WeekParityLabel(_master, date);
            if (hit is not null && parity is not null)
            {
                var mark = CourseFilledCount(CourseDoc.LoadOrDefault(hit)) == 0 ? $" ({Tr("schedule.rotation_empty_mark")})" : "";
                lines.Add($"{parity} → {hit}{mark}");
            }
            else if (hit is null && _master.Courses.Count > 0)
            {
                lines.Add(Tr("schedule.rotation_none_week"));
            }
        }
        var patches = ScheduleRules.ResolvePatches(_master, date, _timetable);
        foreach (var pp in patches)
        {
            var r = NonBreakIndices().IndexOf(pp.Index) + 1;
            lines.Add($"{Tr("schedule.src_patch")} · {Tr("timetable.period")} {r} → {pp.Subject}{(pp.NeedsCheck ? $" ({Tr("schedule.src_check")})" : "")}{(pp.Note.Length > 0 ? $" [{pp.Note}]" : "")}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>课表非空格数 判空表</summary>
    private static int CourseFilledCount(CourseDoc course)
    {
        var n = 0;
        foreach (var kv in course.Courses)
        {
            foreach (var pair in kv.Value)
            {
                if (pair.Value is JsonValue jv && jv.TryGetValue<string>(out var s) && s.Length > 0)
                {
                    n++;
                }
            }
        }
        return n;
    }

    private void OnAdjustSaveCell()
    {
        if (_adjustSelPeriod < 0 || _adjustSelDate == default)
        {
            return;
        }
        var subject = _adjustSubjectBox.Text?.Trim() ?? "";
        var room = _adjustRoomBox.Text?.Trim() ?? "";
        if (subject.Length == 0)
        {
            return;
        }
        WritePatch(_adjustSelDate, _adjustSelPeriod, subject, room, PeriodField(_timetable.Periods[_adjustSelPeriod], "start"));
        _adjustEditorKey = null;
        RefreshAdjustGrid();
    }

    private void OnAdjustSwapMode()
    {
        // 切换式: 再点一次取消
        if (_adjustSwap is not null)
        {
            CancelAdjustSwap();
            PaintAdjustSelection();
            RefreshAdjustEditor();
            return;
        }
        if (_adjustSelPeriod < 0 || _adjustSelDate == default)
        {
            return;
        }
        var content = AdjustCellContent(_adjustSelDate, _adjustSelPeriod, DayKeyOf(_adjustSelDate));
        _adjustSwap = (_adjustSelDate, _adjustSelPeriod, content.Subject);
        _adjustSwapBtn.Content = Tr("schedule.adjust_swap_cancel");
        _adjustSwapBanner.IsVisible = true;
        PaintAdjustSelection();
        RefreshAdjustEditor();
    }

    /// <summary>设为默认写命中课表</summary>
    private void OnAdjustSetDefault()
    {
        if (_adjustSelPeriod < 0 || _adjustSelDate == default)
        {
            return;
        }
        var ov = ScheduleRules.ResolveOverride(_master, _adjustSelDate);
        if (ov is not null && ov.Kind != "plan")
        {
            ShowAdjustMsg(Tr("schedule.msg_rejected"));
            return;
        }
        var target = (ov is not null ? ov.CourseName : null)
                     ?? ScheduleRules.ResolveCourse(_master, _adjustSelDate)
                     ?? _master.Course;
        if (target.Length == 0)
        {
            return;
        }
        var patch = ScheduleRules.ResolvePatches(_master, _adjustSelDate, _timetable).FirstOrDefault(pp => pp.Index == _adjustSelPeriod);
        if (patch is null || patch.NeedsCheck)
        {
            return;
        }
        var course = CourseDoc.LoadOrDefault(target);
        course.SetCell(_adjustSelPeriod, DayKeyOf(_adjustSelDate), patch.Subject);
        course.Save();
        RemovePatchAt(_adjustSelDate, _adjustSelPeriod);
        _master.Save();
        Log.Info($"[课表窗口] 设为默认: {target} {_adjustSelDate:MM-dd}第{_adjustSelPeriod}节 = {patch.Subject}");
        _adjustEditorKey = null;
        RefreshAdjustGrid();
    }

    private void OnAdjustClearPatch()
    {
        if (_adjustSelPeriod < 0 || _adjustSelDate == default)
        {
            return;
        }
        // 考试/假期 清整日覆盖
        var ov = ScheduleRules.ResolveOverride(_master, _adjustSelDate);
        if (ov is not null && ov.Kind != "plan")
        {
            _master.Overrides.RemoveAll(o => ReferenceEquals(o, ov.Entry));
            _master.Save();
            Log.Info($"[课表窗口] 已清除整日覆盖: {_adjustSelDate:yyyy-MM-dd}");
            _adjustEditorKey = null;
            RefreshAdjustGrid();
            return;
        }
        var before = _master.Patches.Count;
        RemovePatchAt(_adjustSelDate, _adjustSelPeriod);
        if (_master.Patches.Count != before)
        {
            _master.Save();
        }
        _adjustEditorKey = null;
        RefreshAdjustGrid();
    }

    /// <summary>整日类型动态表单</summary>
    private void RebuildDayModeForm()
    {
        if (_dayModeHost is null)
        {
            return;
        }
        _dayModeHost.Children.Clear();
        _adjustPlanCombo = null;
        _adjustDayNameBox = null;
        var mode = _adjustDayModeCombo.SelectedIndex;
        // 常规不展开表单
        if (_dayOps is not null)
        {
            _dayOps.IsVisible = mode != DayModeNormal;
        }
        var ov = _adjustSelDate == default ? null : ScheduleRules.ResolveOverride(_master, _adjustSelDate);
        switch (mode)
        {
            case DayModePlan:
                var subs = ScheduleStore.ListSubs("course");
                var combo = new ComboBox { MinWidth = 0, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var s in subs)
                {
                    combo.Items.Add(s);
                }
                if (combo.Items.Count > 0)
                {
                    combo.SelectedIndex = 0;
                }
                // 已有调课覆盖时预选
                if (ov is { Kind: "plan" })
                {
                    var idx = subs.IndexOf(ov.CourseName);
                    if (idx >= 0)
                    {
                        combo.SelectedIndex = idx;
                    }
                }
                _adjustPlanCombo = combo;
                _dayModeHost.Children.Add(combo);
                break;
            case DayModeExam:
            case DayModeHoliday:
                var box = new TextBox
                {
                    PlaceholderText = mode == 2 ? Tr("schedule.day_name_exam") : Tr("schedule.day_name_holiday"),
                    FontSize = 12,
                    Height = 32,
                    Text = ov is not null && ov.Kind == (mode == 2 ? "exam" : "holiday") ? ov.Name : "",
                };
                _adjustDayNameBox = box;
                _dayModeHost.Children.Add(box);
                break;
        }
    }

    /// <summary>应用整日类型</summary>
    private void OnApplyDayMode()
    {
        if (_adjustSelDate == default)
        {
            return;
        }
        switch (_adjustDayModeCombo.SelectedIndex)
        {
            case DayModePlan:
                if (_adjustPlanCombo?.SelectedItem is not string course || course.Length == 0)
                {
                    ShowAdjustMsg(Tr("schedule.msg_need_name"));
                    return;
                }
                ApplyDayOverride("plan", course);
                break;
            case DayModeExam:
                var exam = _adjustDayNameBox?.Text?.Trim();
                if (string.IsNullOrEmpty(exam))
                {
                    ShowAdjustMsg(Tr("schedule.msg_need_name"));
                    return;
                }
                ApplyDayOverride("exam", exam);
                break;
            case DayModeHoliday:
                var holiday = _adjustDayNameBox?.Text?.Trim();
                ApplyDayOverride("holiday", string.IsNullOrEmpty(holiday) ? Tr("schedule.src_holiday") : holiday);
                break;
            default:
                OnAdjustClearDay();
                return;
        }
        _adjustEditorKey = null;
        RebuildDayModeForm();
        RefreshAdjustGrid();
    }

    /// <summary>写单日覆盖</summary>
    private void ApplyDayOverride(string kind, string name)
    {
        var entry = new JsonObject
        {
            ["start"] = _adjustSelDate.ToString("yyyy-MM-dd"),
            ["end"] = _adjustSelDate.ToString("yyyy-MM-dd"),
            ["kind"] = kind,
        };
        if (kind == "plan")
        {
            entry["course"] = name;
        }
        else
        {
            entry["name"] = name;
        }
        var replaced = WriteOverride(_adjustSelDate, entry);
        if (replaced)
        {
            ShowAdjustMsg(Tr("schedule.msg_override_replaced"));
        }
    }

    private void OnAdjustClearDay()
    {
        if (_adjustSelDate == default)
        {
            return;
        }
        var before = _master.Overrides.Count;
        _master.Overrides.RemoveAll(o =>
            DateOnly.TryParse(o["start"]?.GetValue<string>(), out var s) && s <= _adjustSelDate &&
            DateOnly.TryParse(o["end"]?.GetValue<string>(), out var e) && e >= _adjustSelDate);
        if (_master.Overrides.Count != before)
        {
            _master.Save();
        }
        _adjustEditorKey = null;
        RefreshAdjustGrid();
    }

    /// <summary>提示条 3s 自动收</summary>
    private void ShowAdjustMsg(string text)
    {
        _adjustMsg.Text = text;
        _adjustMsg.IsVisible = true;
        PulseTimer(ref _adjustMsgTimer, TimeSpan.FromSeconds(3), OnAdjustMsgTick);
    }

    private void OnAdjustMsgTick(object? sender, EventArgs e)
    {
        _adjustMsg.IsVisible = false;
        _adjustMsgTimer?.Stop();
    }

    // 值周表页

    private Control BuildDutyPage()
    {
        // 左: 值周表格
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("110,110,*,*,*") };
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_parity"), true), 0, 0, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_weekday"), true), 0, 1, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_group"), true), 0, 2, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_name"), true), 0, 3, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_role"), true), 0, 4, header: true));
        _dutyRowsHost = new StackPanel();
        _dutyTableEmpty = MakeEmptyState(Tr("schedule.duty_empty"), FASymbol.Add);
        var table = MakeTableStack(MakeScrollableTable(header, _dutyRowsHost), _dutyTableEmpty);

        var commandBar = MakeToolRow(
            [
                (FAIcons.IconCenter(FASymbol.Add, 13), Tr("schedule.add_row"), OnDutyAddRow),
                (FAIcons.IconCenter(FASymbol.Delete, 13), Tr("schedule.delete_row"), OnDutyDeleteRow),
            ]);
        _dutyCommandBar = commandBar;

        _dutyCaption = MakeCaption();
        var left = MakeTableArea(table);

        // 右: 选中行编辑
        var (editorCard, editorEmpty, editorHost, _) = MakeEditorCard();
        _dutyEditorCard = editorCard;
        _dutyEmpty = editorEmpty;
        _dutyEditHost = editorHost;
        _dutyParityCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var p in Parities)
        {
            _dutyParityCombo.Items.Add(p);
        }
        _dutyParityCombo.SelectionChanged += (_, _) => OnDutyEditorChanged("parity");
        _dutyWeekdayCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var d in WeekdayOrder)
        {
            _dutyWeekdayCombo.Items.Add(Tr($"timetable.{d}"));
        }
        _dutyWeekdayCombo.SelectionChanged += (_, _) => OnDutyEditorChanged("weekday");
        _dutyGroupEdit = new TextBox();
        _dutyGroupEdit.TextChanged += (_, _) => OnDutyEditorChanged("group");
        _dutyNameEdit = new TextBox();
        _dutyNameEdit.TextChanged += (_, _) => OnDutyEditorChanged("name");
        _dutyRoleEdit = new TextBox();
        _dutyRoleEdit.TextChanged += (_, _) => OnDutyEditorChanged("role");
        editorHost.Children.Add(MakeField(Tr("schedule.col_parity"), _dutyParityCombo));
        editorHost.Children.Add(MakeField(Tr("schedule.col_weekday"), _dutyWeekdayCombo));
        editorHost.Children.Add(MakeField(Tr("schedule.col_group"), _dutyGroupEdit));
        editorHost.Children.Add(MakeField(Tr("schedule.col_name"), _dutyNameEdit));
        editorHost.Children.Add(MakeField(Tr("schedule.col_role"), _dutyRoleEdit));
        ShowEditor(editorEmpty, editorHost, false);

        var root = MakePage(Tr("schedule.tab_duty"), _dutyCaption, commandBar, Tr("schedule.duty_editor_title"), left, editorCard);
        RefreshDutyTable();
        return root;
    }

    private void RefreshDutyTable()
    {
        var readOnly = _linkageMode;
        _blockSync = true;
        _dutyRowsHost.Children.Clear();
        _dutyCells.Clear();
        _dutyParityCombos.Clear();
        _dutyWeekdayCombos.Clear();
        _dutyGroupEditors.Clear();
        _dutyNameEditors.Clear();
        _dutyRoleEditors.Clear();
        _dutyTableEmpty.IsVisible = _duty.Rows.Count == 0;
        for (var i = 0; i < _duty.Rows.Count; i++)
        {
            var r = _duty.Rows[i];
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,110,*,*,*"), MinHeight = 34 };
            var row = i;

            var parityCombo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                IsEnabled = !readOnly,
            };
            foreach (var p in Parities)
            {
                parityCombo.Items.Add(p);
            }
            var parity = r["parity"]?.GetValue<string>() ?? "单周";
            parityCombo.SelectedIndex = Math.Max(0, Array.IndexOf(Parities, parity));
            parityCombo.SelectionChanged += (_, _) =>
            {
                if (_blockSync || row >= _duty.Rows.Count)
                {
                    return;
                }
                _duty.Rows[row]["parity"] = Parities[Math.Max(0, parityCombo.SelectedIndex)];
                _duty.Save();
            };
            var parityCell = MakeCell(parityCombo, row, 0, onClick: () => SelectDutyCell(row, 0));
            rowGrid.Children.Add(parityCell);
            _dutyCells.Add(parityCell);
            _dutyParityCombos.Add(parityCombo);

            var weekdayCombo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                IsEnabled = !readOnly,
            };
            foreach (var d in WeekdayOrder)
            {
                weekdayCombo.Items.Add(Tr($"timetable.{d}"));
            }
            var weekday = r["weekday"]?.GetValue<string>() ?? "monday";
            weekdayCombo.SelectedIndex = Math.Max(0, Array.IndexOf(WeekdayOrder, weekday));
            weekdayCombo.SelectionChanged += (_, _) =>
            {
                if (_blockSync || row >= _duty.Rows.Count)
                {
                    return;
                }
                _duty.Rows[row]["weekday"] = WeekdayOrder[Math.Max(0, weekdayCombo.SelectedIndex)];
                _duty.Save();
            };
            var weekdayCell = MakeCell(weekdayCombo, row, 1, onClick: () => SelectDutyCell(row, 1));
            rowGrid.Children.Add(weekdayCell);
            _dutyCells.Add(weekdayCell);
            _dutyWeekdayCombos.Add(weekdayCombo);

            var groupEdit = CellEdit(r["group"]?.GetValue<string>() ?? "");
            groupEdit.IsReadOnly = readOnly;
            groupEdit.TextChanged += (_, _) =>
            {
                if (!_blockSync && row < _duty.Rows.Count)
                {
                    _duty.Rows[row]["group"] = groupEdit.Text ?? "";
                    QueueDutySave();
                }
            };
            var groupCell = MakeCell(groupEdit, row, 2, onClick: () => SelectDutyCell(row, 2));
            rowGrid.Children.Add(groupCell);
            _dutyCells.Add(groupCell);
            _dutyGroupEditors.Add(groupEdit);

            var nameEdit = CellEdit(r["name"]?.GetValue<string>() ?? "");
            nameEdit.IsReadOnly = readOnly;
            nameEdit.TextChanged += (_, _) =>
            {
                if (!_blockSync && row < _duty.Rows.Count)
                {
                    _duty.Rows[row]["name"] = nameEdit.Text ?? "";
                    QueueDutySave();
                }
            };
            var nameCell = MakeCell(nameEdit, row, 3, onClick: () => SelectDutyCell(row, 3));
            rowGrid.Children.Add(nameCell);
            _dutyCells.Add(nameCell);
            _dutyNameEditors.Add(nameEdit);

            var roleEdit = CellEdit(r["role"]?.GetValue<string>() ?? "");
            roleEdit.IsReadOnly = readOnly;
            roleEdit.TextChanged += (_, _) =>
            {
                if (!_blockSync && row < _duty.Rows.Count)
                {
                    _duty.Rows[row]["role"] = roleEdit.Text ?? "";
                    QueueDutySave();
                }
            };
            var roleCell = MakeCell(roleEdit, row, 4, onClick: () => SelectDutyCell(row, 4));
            rowGrid.Children.Add(roleCell);
            _dutyCells.Add(roleCell);
            _dutyRoleEditors.Add(roleEdit);

            var rowIdx = i;
            _dutyRowsHost.Children.Add(MakeRowHost(rowGrid,
                () => SelectDutyCell(rowIdx, _dutySelCol < 0 ? 0 : Math.Min(_dutySelCol, 4))));
        }
        _blockSync = false;
        if (_dutySelRow >= _duty.Rows.Count)
        {
            _dutySelRow = -1;
            _dutySelCol = -1;
        }
        PaintCells(_dutyCells, _dutySelRow, _dutySelCol);
        SyncDutyEditor();
    }

    private void SelectDutyCell(int row, int col)
    {
        _dutySelRow = row;
        _dutySelCol = col;
        PaintCells(_dutyCells, row, col);
        SyncDutyEditor();
    }

    /// <summary>表单回填选中行</summary>
    private void SyncDutyEditor()
    {
        var hasSel = _dutySelRow >= 0 && _dutySelRow < _duty.Rows.Count;
        ShowEditor(_dutyEmpty, _dutyEditHost, hasSel);
        if (!hasSel)
        {
            return;
        }
        var r = _duty.Rows[_dutySelRow];
        _blockSync = true;
        var parityIdx = Array.IndexOf(Parities, r["parity"]?.GetValue<string>() ?? "单周");
        _dutyParityCombo.SelectedIndex = Math.Max(0, parityIdx);
        var weekdayIdx = Array.IndexOf(WeekdayOrder, r["weekday"]?.GetValue<string>() ?? "monday");
        _dutyWeekdayCombo.SelectedIndex = Math.Max(0, weekdayIdx);
        _dutyGroupEdit.Text = r["group"]?.GetValue<string>() ?? "";
        _dutyNameEdit.Text = r["name"]?.GetValue<string>() ?? "";
        _dutyRoleEdit.Text = r["role"]?.GetValue<string>() ?? "";
        _blockSync = false;
    }

    /// <summary>表单写回选中行</summary>
    private void OnDutyEditorChanged(string field)
    {
        if (_blockSync || _dutySelRow < 0 || _dutySelRow >= _duty.Rows.Count)
        {
            return;
        }
        var r = _duty.Rows[_dutySelRow];
        var row = _dutySelRow;
        switch (field)
        {
            case "parity":
                r["parity"] = Parities[Math.Max(0, _dutyParityCombo.SelectedIndex)];
                if (row < _dutyParityCombos.Count && _dutyParityCombos[row].SelectedIndex != _dutyParityCombo.SelectedIndex)
                {
                    _blockSync = true;
                    _dutyParityCombos[row].SelectedIndex = _dutyParityCombo.SelectedIndex;
                    _blockSync = false;
                }
                break;
            case "weekday":
                r["weekday"] = WeekdayOrder[Math.Max(0, _dutyWeekdayCombo.SelectedIndex)];
                if (row < _dutyWeekdayCombos.Count && _dutyWeekdayCombos[row].SelectedIndex != _dutyWeekdayCombo.SelectedIndex)
                {
                    _blockSync = true;
                    _dutyWeekdayCombos[row].SelectedIndex = _dutyWeekdayCombo.SelectedIndex;
                    _blockSync = false;
                }
                break;
            case "group":
                r["group"] = _dutyGroupEdit.Text ?? "";
                SyncEditorField(row, _dutyGroupEditors, _dutyGroupEdit.Text);
                break;
            case "name":
                r["name"] = _dutyNameEdit.Text ?? "";
                SyncEditorField(row, _dutyNameEditors, _dutyNameEdit.Text);
                break;
            case "role":
                r["role"] = _dutyRoleEdit.Text ?? "";
                SyncEditorField(row, _dutyRoleEditors, _dutyRoleEdit.Text);
                break;
        }
        QueueDutySave();
    }

    private void SyncEditorField(int row, List<TextBox> editors, string? text)
    {
        if (row >= editors.Count)
        {
            return;
        }
        var editor = editors[row];
        if (editor.Text != text)
        {
            _blockSync = true;
            editor.Text = text;
            _blockSync = false;
        }
    }

    private void OnDutyAddRow()
    {
        _duty.AddRow();
        _duty.Save();
        RefreshDutyTable();
        SelectDutyCell(_duty.Rows.Count - 1, 1);
    }

    private void OnDutyDeleteRow()
    {
        if (_dutySelRow < 0 || _dutySelRow >= _duty.Rows.Count)
        {
            return;
        }
        _duty.Rows.RemoveAt(_dutySelRow);
        _duty.Save();
        _dutySelRow = Math.Min(_dutySelRow, _duty.Rows.Count - 1);
        RefreshDutyTable();
    }

    // 花名册页

    private Control BuildRosterPage()
    {
        // 左: 花名册表格
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") };
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_no"), true), 0, 0, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_name"), true), 0, 1, header: true));
        _rosterRowsHost = new StackPanel();
        _rosterTableEmpty = MakeEmptyState(Tr("schedule.roster_empty"), FASymbol.Add);
        var table = MakeTableStack(MakeScrollableTable(header, _rosterRowsHost), _rosterTableEmpty);

        // 点名增删常驻 低频收菜单
        var commandBar = MakeToolRow(
            [
                (FAIcons.IconCenter(FASymbol.Shuffle, 13), Tr("schedule.roster_draw"), OnRosterDraw),
                (FAIcons.IconCenter(FASymbol.Add, 13), Tr("schedule.add_row"), OnRosterAddRow),
                (FAIcons.IconCenter(FASymbol.Delete, 13), Tr("schedule.delete_row"), OnRosterDeleteRow),
            ],
            null,
            MakeMenu(
                (FAIcons.IconCenter(FASymbol.Import, 13), Tr("schedule.roster_import"), OnRosterImport),
                (FAIcons.IconCenter(FASymbol.ImportAll, 13), Tr("schedule.roster_export"), OnRosterExport)));
        _rosterCommandBar = commandBar;
        _rosterCaption = MakeCaption();
        var left = MakeTableArea(table);

        // 右: 选中行编辑
        var (editorCard, editorEmpty, editorHost, _) = MakeEditorCard();
        _rosterEditorCard = editorCard;
        _rosterEmpty = editorEmpty;
        _rosterEditHost = editorHost;
        _rosterNoEdit = new TextBox();
        _rosterNoEdit.TextChanged += (_, _) => OnRosterEditorChanged("no");
        _rosterNameEdit = new TextBox();
        _rosterNameEdit.TextChanged += (_, _) => OnRosterEditorChanged("name");
        editorHost.Children.Add(MakeField(Tr("schedule.col_no"), _rosterNoEdit));
        editorHost.Children.Add(MakeField(Tr("schedule.col_name"), _rosterNameEdit));
        ShowEditor(editorEmpty, editorHost, false);

        // 点名卡 结果 + 检索
        _rosterResult = new TextBlock
        {
            Text = "—",
            FontSize = 18,
            FontWeight = FontWeight.Medium,
            TextWrapping = TextWrapping.Wrap,
        };
        _rosterCount = new TextBlock { FontSize = 12, Opacity = 0.6 };
        _rosterSearch = new TextBox
        {
            PlaceholderText = Tr("schedule.roster_search"),
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _rosterSearch.TextChanged += (_, _) => RefreshRosterTable();
        var drawInner = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(16, 14, 16, 14),
            Children =
            {
                new TextBlock { Text = Tr("schedule.roster_editor_title"), FontSize = 12, Opacity = 0.65 },
                _rosterResult,
                new Separator { Margin = new Thickness(0, 4, 0, 4) },
                _rosterSearch,
                _rosterCount,
            },
        };
        var rightColumn = new Grid { RowDefinitions = new RowDefinitions("*,8,Auto") };
        Grid.SetRow(editorCard, 0);
        rightColumn.Children.Add(editorCard);
        var drawCard = MakeCardFrame(drawInner);
        Grid.SetRow(drawCard, 2);
        rightColumn.Children.Add(drawCard);

        var root = MakePage(Tr("schedule.tab_roster"), _rosterCaption, commandBar, Tr("schedule.roster_edit_title"), left, rightColumn);
        RefreshRosterTable();
        return root;
    }

    private static bool RosterMatch(string no, string name, string filter) =>
        filter.Length == 0 ||
        no.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void RefreshRosterTable()
    {
        var readOnly = _linkageMode;
        var filter = _rosterSearch.Text?.Trim() ?? "";
        _blockSync = true;
        _rosterRowsHost.Children.Clear();
        _rosterCells.Clear();
        _rosterTableEmpty.IsVisible = _roster.Rows.Count == 0;
        for (var i = 0; i < _roster.Rows.Count; i++)
        {
            var (no, name) = _roster.Entry(i);
            if (!RosterMatch(no, name, filter))
            {
                continue;
            }
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*"), MinHeight = 34 };
            var row = i;

            var noEdit = CellEdit(no);
            noEdit.IsReadOnly = readOnly;
            noEdit.TextChanged += (_, _) =>
            {
                if (!_blockSync && row < _roster.Rows.Count)
                {
                    _roster.Rows[row]["no"] = noEdit.Text ?? "";
                    QueueRosterSave();
                }
            };
            var noCell = MakeCell(noEdit, row, 0, onClick: () => SelectRosterCell(row, 0));
            rowGrid.Children.Add(noCell);
            _rosterCells.Add(noCell);

            var nameEdit = CellEdit(name);
            nameEdit.IsReadOnly = readOnly;
            nameEdit.TextChanged += (_, _) =>
            {
                if (!_blockSync && row < _roster.Rows.Count)
                {
                    _roster.Rows[row]["name"] = nameEdit.Text ?? "";
                    QueueRosterSave();
                }
            };
            var nameCell = MakeCell(nameEdit, row, 1, onClick: () => SelectRosterCell(row, 1));
            rowGrid.Children.Add(nameCell);
            _rosterCells.Add(nameCell);

            var rowIdx = i;
            _rosterRowsHost.Children.Add(MakeRowHost(rowGrid,
                () => SelectRosterCell(rowIdx, _rosterSelCol < 0 ? 0 : Math.Min(_rosterSelCol, 1))));
        }
        _blockSync = false;
        if (_rosterSelRow >= _roster.Rows.Count)
        {
            _rosterSelRow = -1;
            _rosterSelCol = -1;
        }
        PaintCells(_rosterCells, _rosterSelRow, _rosterSelCol);
        SyncRosterEditor();
        _rosterCount.Text = Tr("schedule.roster_count", ("n", _roster.Rows.Count));
    }

    private void SelectRosterCell(int row, int col)
    {
        _rosterSelRow = row;
        _rosterSelCol = col;
        PaintCells(_rosterCells, row, col);
        SyncRosterEditor();
    }

    /// <summary>表单回填选中行</summary>
    private void SyncRosterEditor()
    {
        var hasSel = _rosterSelRow >= 0 && _rosterSelRow < _roster.Rows.Count;
        ShowEditor(_rosterEmpty, _rosterEditHost, hasSel);
        if (!hasSel)
        {
            return;
        }
        var r = _roster.Rows[_rosterSelRow];
        _blockSync = true;
        _rosterNoEdit.Text = r["no"]?.GetValue<string>() ?? "";
        _rosterNameEdit.Text = r["name"]?.GetValue<string>() ?? "";
        _blockSync = false;
    }

    /// <summary>表单写回选中行</summary>
    private void OnRosterEditorChanged(string field)
    {
        if (_blockSync || _rosterSelRow < 0 || _rosterSelRow >= _roster.Rows.Count)
        {
            return;
        }
        var r = _roster.Rows[_rosterSelRow];
        var text = field == "no" ? _rosterNoEdit.Text : _rosterNameEdit.Text;
        r[field] = text ?? "";
        SyncRosterCell(_rosterSelRow, field == "no" ? 0 : 1, text);
        QueueRosterSave();
    }

    /// <summary>格文本回写 Tag 定位</summary>
    private void SyncRosterCell(int row, int col, string? text)
    {
        foreach (var b in _rosterCells)
        {
            if (((int, int))b.Tag! == (row, col) && b.Child is TextBox edit && edit.Text != text)
            {
                _blockSync = true;
                edit.Text = text;
                _blockSync = false;
                return;
            }
        }
    }

    private void OnRosterAddRow()
    {
        _roster.AddRow();
        _roster.Save();
        RefreshRosterTable();
        SelectRosterCell(_roster.Rows.Count - 1, 1);
    }

    private void OnRosterDeleteRow()
    {
        if (_rosterSelRow < 0 || _rosterSelRow >= _roster.Rows.Count)
        {
            return;
        }
        _roster.Rows.RemoveAt(_rosterSelRow);
        _roster.Save();
        _rosterSelRow = Math.Min(_rosterSelRow, _roster.Rows.Count - 1);
        RefreshRosterTable();
    }

    /// <summary>随机点名 减速扫动</summary>
    private void OnRosterDraw()
    {
        if (_drawing)
        {
            return;
        }
        var filter = _rosterSearch.Text?.Trim() ?? "";
        var candidates = new List<int>();
        for (var i = 0; i < _roster.Rows.Count; i++)
        {
            var (no, name) = _roster.Entry(i);
            if (name.Length > 0 && RosterMatch(no, name, filter))
            {
                candidates.Add(i);
            }
        }
        if (candidates.Count == 0)
        {
            _rosterResult.Text = Tr("schedule.roster_none");
            return;
        }
        var winner = candidates[Random.Shared.Next(candidates.Count)];
        _drawing = true;
        _rosterSelRow = -1;
        _rosterSelCol = -1;
        PaintCells(_rosterCells, -1, -1);

        _drawTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _drawTimer = timer;
        var ticks = 0;
        var prev = -1;
        timer.Tick += (_, _) =>
        {
            ticks++;
            // 步进减速 扫动渐停
            timer.Interval = TimeSpan.FromMilliseconds(45 + ticks * 16);
            int pick;
            do
            {
                pick = candidates[Random.Shared.Next(candidates.Count)];
            } while (candidates.Count > 1 && pick == prev);
            prev = pick;
            _rosterFlashRow = pick;
            PaintCells(_rosterCells, _rosterFlashRow, -1);
            if (ticks >= 18)
            {
                timer.Stop();
                _drawing = false;
                _rosterFlashRow = -1;
                SelectRosterCell(winner, 0);
                if (_rosterCells.FirstOrDefault(b => b.Tag is (int r, int) && r == winner) is { } winnerCell)
                {
                    winnerCell.BringIntoView();
                }
                var (wno, wname) = _roster.Entry(winner);
                _rosterResult.Text = Tr("schedule.roster_result", ("no", wno.Length > 0 ? $"{wno} " : ""), ("name", wname));
                Log.Info($"[课表窗口] 随机点名: {wno} {wname}");
            }
        };
        timer.Start();
    }

    private async void OnRosterImport()
    {
        if (OwnerWindow is null)
        {
            return;
        }
        var files = await OwnerWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Tr("schedule.roster_import"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Excel/CSV") { Patterns = ["*.xlsx", "*.csv"] }],
        });
        if (files.Count == 0)
        {
            return;
        }
        RosterImportResult result;
        try
        {
            result = RosterExcel.Import(files[0].Path.LocalPath);
        }
        catch (Exception e)
        {
            Log.Error($"[课表窗口] 花名册导入失败: {e.Message}");
            _rosterResult.Text = Tr("schedule.roster_import_failed", ("msg", e.Message));
            return;
        }
        if (result.Rows.Count == 0)
        {
            _rosterResult.Text = Tr("schedule.roster_import_empty");
            return;
        }
        var dialog = new FAContentDialog
        {
            Title = Tr("schedule.roster_import_title"),
            Content = Tr("schedule.roster_import_ask", ("n", result.Rows.Count.ToString()), ("m", result.Skipped.ToString())),
            PrimaryButtonText = Tr("schedule.roster_replace"),
            SecondaryButtonText = Tr("schedule.roster_append"),
            CloseButtonText = Tr("common.cancel"),
            DefaultButton = FAContentDialogButton.Primary,
        };
        var choice = await dialog.ShowAsync(OwnerWindow);
        if (choice == FAContentDialogResult.None)
        {
            return;
        }
        if (choice == FAContentDialogResult.Primary)
        {
            _roster.Rows.Clear();
        }
        foreach (var r in result.Rows)
        {
            _roster.Rows.Add(r);
        }
        _roster.Save();
        _rosterSelRow = -1;
        _rosterSelCol = -1;
        RefreshRosterTable();
        Log.Info($"[课表窗口] 花名册导入: {files[0].Path.LocalPath} +{result.Rows.Count} 跳过{result.Skipped}");
    }

    private async void OnRosterExport()
    {
        if (OwnerWindow is null)
        {
            return;
        }
        try
        {
            var file = await OwnerWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Tr("schedule.roster_export"),
                SuggestedFileName = $"{_roster.Name}.xlsx",
                FileTypeChoices =
                [
                    new FilePickerFileType("XLSX") { Patterns = ["*.xlsx"] },
                    new FilePickerFileType("CSV") { Patterns = ["*.csv"] },
                ],
            });
            if (file is null)
            {
                return;
            }
            var path = file.Path.LocalPath;
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                RosterExcel.ExportCsv(path, _roster.Rows);
            }
            else
            {
                RosterExcel.ExportXlsx(path, _roster.Rows);
            }
            Log.Info($"[课表窗口] 花名册导出: {_roster.Name} -> {path}");
        }
        catch (Exception e)
        {
            Log.Error($"[课表窗口] 花名册导出失败: {e.Message}");
        }
    }

    // 学科页

    private Control BuildSubjectPage()
    {
        // 左: 学科表格
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70,220,110") };
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_subject"), true), 0, 0, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_short"), true), 0, 1, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_teacher"), true), 0, 2, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_outdoor"), true), 0, 3, header: true));
        _subjectRowsHost = new StackPanel();
        _subjectTableEmpty = MakeEmptyState(Tr("schedule.subject_empty"), FASymbol.Add);
        var table = MakeTableStack(MakeScrollableTable(header, _subjectRowsHost), _subjectTableEmpty);

        var commandBar = MakeToolRow(
            [
                (FAIcons.IconCenter(FASymbol.Add, 13), Tr("schedule.add_row"), OnSubjectAddRow),
                (FAIcons.IconCenter(FASymbol.Delete, 13), Tr("schedule.delete_row"), OnSubjectDeleteRow),
            ]);
        _subjectCommandBar = commandBar;

        _subjectCaption = MakeCaption();
        var left = MakeTableArea(table);

        // 右: 选中科目编辑
        var (editorCard, editorEmpty, editorHost, _) = MakeEditorCard();
        _subjectEditorCard = editorCard;
        _subjectEmpty = editorEmpty;
        _subjectEditHost = editorHost;
        _subjectNameEdit = new TextBox();
        _subjectNameEdit.TextChanged += (_, _) => OnSubjectEditorChanged("name");
        _subjectShortEdit = new TextBox();
        _subjectShortEdit.TextChanged += (_, _) => OnSubjectEditorChanged("short");
        _subjectTeacherEdit = new TextBox();
        _subjectTeacherEdit.TextChanged += (_, _) => OnSubjectEditorChanged("teacher");
        _subjectOutdoorCheck = new CheckBox { Content = Tr("schedule.col_outdoor") };
        _subjectOutdoorCheck.IsCheckedChanged += (_, _) => OnSubjectEditorChanged("outdoor");
        _subjectColorCombo = new ComboBox { MinWidth = 140, FontSize = 12 };
        foreach (var (label, _) in SubjectColors)
        {
            _subjectColorCombo.Items.Add(label);
        }
        _subjectColorCombo.SelectionChanged += (_, _) =>
        {
            if (_blockSync || _subjectSelRow < 0 || _subjectSelRow >= _subject.Rows.Count)
            {
                return;
            }
            var pick = _subjectColorCombo.SelectedIndex;
            var r = _subject.Rows[_subjectSelRow];
            if (pick > 0 && pick < SubjectColors.Count)
            {
                r["color"] = SubjectColors[pick].Hex;
            }
            else
            {
                r.Remove("color");
            }
            _subject.Save();
        };
        var nameShortRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        nameShortRow.Children.Add(MakeField(Tr("schedule.col_subject"), _subjectNameEdit));
        var shortField = MakeField(Tr("schedule.col_short"), _subjectShortEdit);
        Grid.SetColumn(shortField, 1);
        nameShortRow.Children.Add(shortField);
        editorHost.Children.Add(nameShortRow);
        editorHost.Children.Add(MakeField(Tr("schedule.col_teacher"), _subjectTeacherEdit));
        editorHost.Children.Add(_subjectOutdoorCheck);
        editorHost.Children.Add(MakeField(Tr("schedule.col_color"), _subjectColorCombo));
        ShowEditor(editorEmpty, editorHost, false);

        var root = MakePage(Tr("schedule.tab_subject"), _subjectCaption, commandBar, Tr("schedule.subject_editor_title"), left, editorCard);
        RefreshSubjectTable();
        return root;
    }

    private void RefreshSubjectTable()
    {
        var readOnly = _linkageMode;
        _blockSync = true;
        _subjectRowsHost.Children.Clear();
        _subjectCells.Clear();
        _subjectNameEditors.Clear();
        _subjectShortEditors.Clear();
        _subjectTeacherEditors.Clear();
        _subjectOutdoorChecks.Clear();
        _subjectTableEmpty.IsVisible = _subject.Rows.Count == 0;
        for (var i = 0; i < _subject.Rows.Count; i++)
        {
            var r = _subject.Rows[i];
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70,220,110"), MinHeight = 34 };
            var row = i;

            var nameEdit = CellEdit(r["name"]?.GetValue<string>() ?? "");
            nameEdit.IsReadOnly = readOnly;
            nameEdit.TextChanged += (_, _) =>
            {
                if (!_blockSync && row < _subject.Rows.Count)
                {
                    _subject.Rows[row]["name"] = nameEdit.Text ?? "";
                    QueueSubjectSave();
                    QueueSubjectFlowRebuild();
                }
            };
            var nameCell = MakeCell(nameEdit, row, 0, onClick: () => SelectSubjectCell(row, 0));
            rowGrid.Children.Add(nameCell);
            _subjectCells.Add(nameCell);
            _subjectNameEditors.Add(nameEdit);

            var shortEdit = CellEdit(r["short"]?.GetValue<string>() ?? "");
            shortEdit.IsReadOnly = readOnly;
            shortEdit.TextChanged += (_, _) =>
            {
                if (!_blockSync && row < _subject.Rows.Count)
                {
                    _subject.Rows[row]["short"] = shortEdit.Text ?? "";
                    QueueSubjectSave();
                }
            };
            var shortCell = MakeCell(shortEdit, row, 1, onClick: () => SelectSubjectCell(row, 1));
            rowGrid.Children.Add(shortCell);
            _subjectCells.Add(shortCell);
            _subjectShortEditors.Add(shortEdit);

            var teacherEdit = CellEdit(r["teacher"]?.GetValue<string>() ?? "");
            teacherEdit.IsReadOnly = readOnly;
            teacherEdit.TextChanged += (_, _) =>
            {
                if (!_blockSync && row < _subject.Rows.Count)
                {
                    _subject.Rows[row]["teacher"] = teacherEdit.Text ?? "";
                    QueueSubjectSave();
                }
            };
            var teacherCell = MakeCell(teacherEdit, row, 2, onClick: () => SelectSubjectCell(row, 2));
            rowGrid.Children.Add(teacherCell);
            _subjectCells.Add(teacherCell);
            _subjectTeacherEditors.Add(teacherEdit);

            var outdoorCheck = new CheckBox
            {
                IsChecked = r["outdoor"]?.GetValue<bool>() ?? false,
                IsEnabled = !readOnly,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            outdoorCheck.IsCheckedChanged += (_, _) =>
            {
                if (!_blockSync && row < _subject.Rows.Count)
                {
                    _subject.Rows[row]["outdoor"] = outdoorCheck.IsChecked == true;
                    _subject.Save();
                }
            };
            var outdoorCell = MakeCell(outdoorCheck, row, 3, onClick: () => SelectSubjectCell(row, 3));
            rowGrid.Children.Add(outdoorCell);
            _subjectCells.Add(outdoorCell);
            _subjectOutdoorChecks.Add(outdoorCheck);

            var rowIdx = i;
            _subjectRowsHost.Children.Add(MakeRowHost(rowGrid,
                () => SelectSubjectCell(rowIdx, _subjectSelCol < 0 ? 0 : Math.Min(_subjectSelCol, 3))));
        }
        _blockSync = false;
        if (_subjectSelRow >= _subject.Rows.Count)
        {
            _subjectSelRow = -1;
            _subjectSelCol = -1;
        }
        PaintCells(_subjectCells, _subjectSelRow, _subjectSelCol);
        SyncSubjectEditor();
    }

    private void SelectSubjectCell(int row, int col)
    {
        _subjectSelRow = row;
        _subjectSelCol = col;
        PaintCells(_subjectCells, row, col);
        SyncSubjectEditor();
    }

    /// <summary>表单回填选中行</summary>
    private void SyncSubjectEditor()
    {
        var hasSel = _subjectSelRow >= 0 && _subjectSelRow < _subject.Rows.Count;
        ShowEditor(_subjectEmpty, _subjectEditHost, hasSel);
        if (!hasSel)
        {
            return;
        }
        var r = _subject.Rows[_subjectSelRow];
        _blockSync = true;
        _subjectNameEdit.Text = r["name"]?.GetValue<string>() ?? "";
        _subjectShortEdit.Text = r["short"]?.GetValue<string>() ?? "";
        _subjectTeacherEdit.Text = r["teacher"]?.GetValue<string>() ?? "";
        _subjectOutdoorCheck.IsChecked = r["outdoor"]?.GetValue<bool>() ?? false;
        var color = r["color"]?.GetValue<string>() ?? "";
        var cIdx = SubjectColors.FindIndex(c => c.Hex.Length > 0 && string.Equals(c.Hex, color, StringComparison.OrdinalIgnoreCase));
        _subjectColorCombo.SelectedIndex = color.Length == 0 ? 0 : Math.Max(0, cIdx);
        _blockSync = false;
    }

    /// <summary>表单写回选中行</summary>
    private void OnSubjectEditorChanged(string field)
    {
        if (_blockSync || _subjectSelRow < 0 || _subjectSelRow >= _subject.Rows.Count)
        {
            return;
        }
        var r = _subject.Rows[_subjectSelRow];
        var row = _subjectSelRow;
        switch (field)
        {
            case "name":
                r["name"] = _subjectNameEdit.Text ?? "";
                SyncEditorField(row, _subjectNameEditors, _subjectNameEdit.Text);
                QueueSubjectFlowRebuild();
                break;
            case "short":
                r["short"] = _subjectShortEdit.Text ?? "";
                SyncEditorField(row, _subjectShortEditors, _subjectShortEdit.Text);
                break;
            case "teacher":
                r["teacher"] = _subjectTeacherEdit.Text ?? "";
                SyncEditorField(row, _subjectTeacherEditors, _subjectTeacherEdit.Text);
                break;
            case "outdoor":
                r["outdoor"] = _subjectOutdoorCheck.IsChecked == true;
                if (row < _subjectOutdoorChecks.Count && _subjectOutdoorChecks[row].IsChecked != _subjectOutdoorCheck.IsChecked)
                {
                    _blockSync = true;
                    _subjectOutdoorChecks[row].IsChecked = _subjectOutdoorCheck.IsChecked;
                    _blockSync = false;
                }
                break;
        }
        QueueSubjectSave();
    }

    private void OnSubjectAddRow()
    {
        _subject.AddRow();
        _subject.Save();
        RefreshSubjectTable();
        RebuildSubjectFlow();
        SelectSubjectCell(_subject.Rows.Count - 1, 0);
    }

    private void OnSubjectDeleteRow()
    {
        if (_subjectSelRow < 0 || _subjectSelRow >= _subject.Rows.Count)
        {
            return;
        }
        _subject.Rows.RemoveAt(_subjectSelRow);
        _subject.Save();
        _subjectSelRow = Math.Min(_subjectSelRow, _subject.Rows.Count - 1);
        RefreshSubjectTable();
        RebuildSubjectFlow();
    }

    // 档案页

    private Control BuildProfilePage()
    {
        // 新建常驻 低频收菜单
        var isGlimpseonSource = Config.ProfileSource.Value == "Glimpseon";
        _addMasterBtn = MakeTextToolButton(FAIcons.IconCenter(FASymbol.Add, 13), Tr("schedule.profile_add"), OnAddMaster);
        _addMasterBtn.IsVisible = isGlimpseonSource;
        var moreMenu = MakeMenu(
            (FAIcons.IconCenter(FASymbol.Download, 14), Tr("schedule.profile_import"), OnImportMaster),
            (FAIcons.IconCenter(FASymbol.Folder, 14), Tr("schedule.open_folder"), OnOpenFolder));
        _importMasterBtn = (MenuItem)moreMenu.Items[0]!;
        _importMasterBtn.IsVisible = isGlimpseonSource;
        var cmdRow = MakeToolRow([], null, moreMenu, _addMasterBtn);

        // 课表来源选择
        _sourceCombo = new ComboBox { MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Stretch };
        _sourceCombo.Items.Add("Glimpseon");
        _sourceCombo.Items.Add("ClassIsland");
        _sourceCombo.Items.Add("ClassWidgets");
        // 提供者显示名 重名补 id
        var pluginProviders = PluginService.ScheduleProviders.ToList();
        var duplicatedNames = pluginProviders
            .GroupBy(x => x.Value.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var p in pluginProviders)
        {
            var label = duplicatedNames.Contains(p.Value.Name)
                ? $"{p.Value.Name} ({p.Value.PluginId})"
                : p.Value.Name;
            _sourceCombo.Items.Add(label);
        }
        var buildSource = Config.ProfileSource.Value ?? "";
        if (buildSource.StartsWith("plugin:", StringComparison.Ordinal))
        {
            var key = buildSource["plugin:".Length..];
            var pIdx = pluginProviders.FindIndex(x => x.Key == key || x.Value.PluginId == key);
            _sourceCombo.SelectedIndex = pIdx >= 0 ? 3 + pIdx : 0;
        }
        else
        {
            _sourceCombo.SelectedIndex = buildSource switch
            {
                "classisland" => 1,
                "classwidgets" => 2,
                _ => 0,
            };
        }
        _sourceCombo.SelectionChanged += (_, _) =>
        {
            if (_blockSync)
            {
                return;
            }
            string source;
            var sel = _sourceCombo.SelectedIndex;
            if (sel >= 3 && sel - 3 < pluginProviders.Count)
            {
                source = "plugin:" + pluginProviders[sel - 3].Key;
            }
            else
            {
                source = sel switch
                {
                    1 => "classisland",
                    2 => "classwidgets",
                    _ => "Glimpseon",
                };
            }
            Log.Info($"[课表窗口] 切换档案来源: {source}");
            Config.ProfileSource.Value = source;
            SwitchSource(source);
        };
        ToolTip.SetTip(_sourceCombo, Tr("timetable.profile_source"));

        // 右: 来源+绑定
        _profileSummary = new StackPanel { Spacing = 5 };
        var rightInner = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(16, 14, 16, 14),
            Children =
            {
                new TextBlock { Text = Tr("timetable.profile_source"), FontSize = 12, Opacity = 0.65 },
                _sourceCombo,
                new Separator { Margin = new Thickness(0, 6, 0, 4) },
                _profileSummary,
            },
        };
        var right = MakeCardFrame(rightInner);

        // 左: 总档案卡片列表
        _mastersHost = new StackPanel { Margin = new Thickness(12, 12, 12, 12) };
        var left = MakeCardFrame(new ScrollViewer
        {
            Content = _mastersHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        var root = MakePage(Tr("schedule.tab_profile"), MakeCaption(Tr("schedule.profile_sub")), cmdRow, Tr("schedule.profile_editor_title"), left, right);
        RebuildMasters();
        return root;
    }

    /// <summary>右栏档案绑定</summary>
    private void RebuildProfileSummary()
    {
        if (_profileSummary is null)
        {
            return;
        }
        _profileSummary.Children.Clear();
        _profileSummary.Children.Add(new TextBlock
        {
            Text = _master.Name,
            FontSize = 13.5,
            FontWeight = FontWeight.Medium,
            Margin = new Thickness(0, 0, 0, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        (string Type, string Name)[] rows =
        [
            (Tr("schedule.sub_course"), _master.Course),
            (Tr("schedule.sub_timetable"), _master.Timetable),
            (Tr("schedule.sub_duty"), _master.Duty),
            (Tr("schedule.sub_subject"), _master.Subject),
            (Tr("schedule.sub_roster"), _master.Roster),
        ];
        foreach (var (type, name) in rows)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("68,*") };
            row.Children.Add(new TextBlock { Text = type, FontSize = 12, Opacity = 0.5 });
            var value = new TextBlock
            {
                Text = name.Length > 0 ? name : Tr("schedule.sub_unbound"),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            _profileSummary.Children.Add(row);
        }
    }

    private void RebuildMasters()
    {
        if (_mastersHost is null)
        {
            return;
        }
        _mastersHost.Children.Clear();
        foreach (var name in VisibleMasters())
        {
            _mastersHost.Children.Add(BuildMasterCard(name));
        }
        RebuildProfileSummary();
    }

    /// <summary>按来源过滤可见总档案</summary>
    private List<string> VisibleMasters()
    {
        var all = ScheduleStore.ListMasters();
        return Config.ProfileSource.Value switch
        {
            "classisland" => all.Where(n => n == Config.ScheduleProfileCi.Value).ToList(),
            "classwidgets" => all.Where(n => n == Config.ScheduleProfileCw.Value).ToList(),
            _ => all.Where(n => n != Config.ScheduleProfileCi.Value && n != Config.ScheduleProfileCw.Value).ToList(),
        };
    }

    private Control BuildMasterCard(string name)
    {
        var currentActive = ActiveMasterItem().Value == name;
        var usedBy = new List<string>();
        if (Config.ScheduleProfile.Value == name)
        {
            usedBy.Add("Glimpseon");
        }
        if (Config.ScheduleProfileCi.Value == name)
        {
            usedBy.Add("ClassIsland");
        }
        if (Config.ScheduleProfileCw.Value == name)
        {
            usedBy.Add("ClassWidgets");
        }
        var master = name == _master.Name ? _master : ScheduleStore.LoadMaster(name);
        var followItem = FollowItemOf(name);
        var followMode = followItem is { Value: true };
        var isSourceOwned = followItem is not null;

        var card = new StackPanel { Spacing = 10 };
        card.Margin = new Thickness(0);

        // 头行
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var src in usedBy)
        {
            var srcKey = src == "Glimpseon" ? "Glimpseon" : src == "ClassIsland" ? "classisland" : "classwidgets";
            var isCurrentSource = currentActive && Config.ProfileSource.Value == srcKey;
            var accent = Common.ParseAccentColor(Config.ThemeColor.Value);
            header.Children.Add(new Border
            {
                Background = isCurrentSource ? new SolidColorBrush(Color.FromArgb(38, accent.R, accent.G, accent.B)) : new SolidColorBrush(Colors.Gray, 0.12),
                BorderBrush = isCurrentSource ? new SolidColorBrush(Color.FromArgb(110, accent.R, accent.G, accent.B)) : new SolidColorBrush(Colors.Gray, 0.25),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 1, 8, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{src} · {Tr("schedule.profile_in_use")}",
                    FontSize = 11,
                    Foreground = isCurrentSource ? AccentBrush : new SolidColorBrush(Colors.Gray, 0.8),
                },
            });
        }
        header.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (!currentActive && !isSourceOwned)
        {
            ops.Children.Add(MakeSmallButton(Tr("schedule.profile_use"), () => UseMaster(name)));
        }
        ops.Children.Add(MakeToolButton(FASymbol.Edit, Tr("schedule.profile_rename"), () => RenameMaster(name)));
        ops.Children.Add(MakeToolButton(FASymbol.Copy, Tr("schedule.profile_copy"), () => { ScheduleStore.CopyMaster(name); RebuildMasters(); }));
        if (!isSourceOwned)
        {
            ops.Children.Add(MakeToolButton(FASymbol.Delete, Tr("schedule.profile_delete"), () => DeleteMaster(name)));
        }
        ops.Children.Add(MakeToolButton(AppIcons.Save, Tr("schedule.profile_export"), () => ExportMaster(name)));
        header.Children.Add(ops);

        // CI/CW 模式选择器
        if (followItem is not null)
        {
            var modeCombo = new ComboBox
            {
                MinWidth = 160,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            };
            modeCombo.Items.Add(Tr("schedule.follow_mode_follow"));
            modeCombo.Items.Add(Tr("schedule.follow_mode_edit"));
            modeCombo.SelectedIndex = followItem.Value ? 0 : 1;
            ToolTip.SetTip(modeCombo, Tr("schedule.follow_mode"));
            var follow = followItem;
            modeCombo.SelectionChanged += (_, _) =>
            {
                var wantEdit = modeCombo.SelectedIndex == 1;
                if (follow.Value == !wantEdit)
                {
                    return;
                }
                follow.Value = !wantEdit;
                if (wantEdit)
                {
                    // 自编辑起点=外部快照
                    PersistFollowSnapshot();
                }
                AfterSubChanged();
                ApplySourceMode(Config.ProfileSource.Value);
            };
            header.Children.Add(modeCombo);
        }
        card.Children.Add(header);

        if (followMode)
        {
            var srcName = Config.ScheduleProfileCi.Value == name ? "ClassIsland" : "ClassWidgets";
            card.Children.Add(new TextBlock
            {
                Text = Tr("schedule.follow_hint", ("src", srcName)),
                FontSize = 11,
                Opacity = 0.5,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // 分档案行
        var subHost = new StackPanel { Spacing = 2, IsEnabled = !followMode };
        foreach (var type in ScheduleStore.SubTypes)
        {
            // 课表多行 余为单绑定
            subHost.Children.Add(type == "course" ? BuildCourseRow(master) : BuildSubRow(master, type));
        }
        card.Children.Add(subHost);

        // 周期空缺
        var gaps = ScheduleRules.RotationGaps(master);
        if (gaps.Count > 0)
        {
            var names = string.Join(" / ", gaps.Select(g => g.Total == 2
                ? (g.Div == 1 ? Tr("schedule.rotation_div_odd") : Tr("schedule.rotation_div_even"))
                : Tr("schedule.rotation_week_chip", ("n", g.Div))));
            card.Children.Add(new TextBlock
            {
                Text = Tr("schedule.rotation_gap", ("weeks", names)),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x9F, 0x27)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        return MakeCard(card, new Thickness(16, 12, 16, 12));
    }

    /// <summary>课表单双周行</summary>
    private Control BuildCourseRow(ScheduleProfile master)
    {
        var subs = ScheduleStore.ListSubs("course");
        var doubleWeek = WeekCourseOf(master, 2).Length > 0;

        // 按钮贴右
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Grid.SetColumn(left, 0);
        row.Children.Add(left);
        left.Children.Add(new TextBlock
        {
            Text = Tr("schedule.sub_course"),
            Width = 90,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // 勾=双周
        var chkDouble = new CheckBox
        {
            Content = Tr("schedule.week_mode_double"),
            IsChecked = doubleWeek,
            MinWidth = 0,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chkDouble.IsCheckedChanged += (_, _) =>
        {
            if (chkDouble.IsChecked != doubleWeek)
            {
                SwitchWeekMode(master, chkDouble.IsChecked == true);
            }
        };
        left.Children.Add(chkDouble);

        if (!doubleWeek)
        {
            left.Children.Add(MakeCourseCombo(subs, master.Course, pick => BindSub(master.Name, "course", pick)));
        }
        else
        {
            // 单前双后
            var single = WeekCourseOf(master, 1);
            var dual = WeekCourseOf(master, 2);
            left.Children.Add(WeekCaption(Tr("schedule.week_mode_single")));
            left.Children.Add(MakeCourseCombo(subs, single, pick => SetWeekCourse(master, 1, pick)));
            left.Children.Add(WeekCaption(Tr("schedule.week_mode_double")));
            left.Children.Add(MakeCourseCombo(subs, dual, pick => SetWeekCourse(master, 2, pick)));
        }

        var ops = BuildSubOps(master.Name, "course", master.Course);
        Grid.SetColumn(ops, 1);
        row.Children.Add(ops);
        return row;
    }

    /// <summary>行内标注</summary>
    private static TextBlock WeekCaption(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Opacity = 0.7,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private ComboBox MakeCourseCombo(List<string> subs, string bound, Action<string> changed)
    {
        var combo = new ComboBox
        {
            MinWidth = 200,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        foreach (var sub in subs)
        {
            combo.Items.Add(sub);
        }
        combo.SelectedIndex = Math.Max(0, subs.IndexOf(bound));
        combo.SelectionChanged += (_, _) =>
        {
            if (_blockSync || combo.SelectedIndex < 0 || combo.SelectedIndex >= subs.Count)
            {
                return;
            }
            changed(subs[combo.SelectedIndex]);
        };
        return combo;
    }

    /// <summary>周次对应课表</summary>
    private static string WeekCourseOf(ScheduleProfile master, int div)
    {
        foreach (var c in master.Courses)
        {
            if ((c["weekTotal"]?.GetValue<int>() ?? 0) == 2 && (c["weekDiv"]?.GetValue<int>() ?? 0) == div)
            {
                return c["course"]?.GetValue<string>() ?? "";
            }
        }
        return div == 1 ? master.Course : "";
    }

    /// <summary>写入周次课表</summary>
    private void SetWeekCourse(ScheduleProfile master, int div, string course)
    {
        JsonObject? entry = null;
        foreach (var c in master.Courses)
        {
            if ((c["weekTotal"]?.GetValue<int>() ?? 0) == 2 && (c["weekDiv"]?.GetValue<int>() ?? 0) == div)
            {
                entry = c;
                break;
            }
        }
        if (entry is null)
        {
            entry = new JsonObject { ["course"] = course, ["weekDiv"] = div, ["weekTotal"] = 2 };
            master.Courses.Add(entry);
        }
        else
        {
            entry["course"] = course;
        }
        if (div == 1 && course.Length > 0)
        {
            master.Course = course;
        }
        master.Save();
        Log.Info($"[课表窗口] 第 {div} 周课表: {course}");
        RebuildMasters();
    }

    /// <summary>单双周切换</summary>
    private void SwitchWeekMode(ScheduleProfile master, bool toDouble)
    {
        if (toDouble)
        {
            // 只动2周轮换 保3/4周
            master.Courses.RemoveAll(c => (c["weekTotal"]?.GetValue<int>() ?? 0) == 2);
            var first = master.Course;
            // 优先选有内容的表
            var second = ScheduleStore.ListSubs("course")
                .Where(s => s != first)
                .OrderByDescending(s => CourseFilledCount(CourseDoc.LoadOrDefault(s)))
                .FirstOrDefault() ?? "";
            if (second.Length == 0)
            {
                // 无余表 新建空表
                second = ScheduleStore.NextSubName("course");
                new CourseDoc { Name = second }.Save();
            }
            master.Courses.Add(new JsonObject { ["course"] = first, ["weekDiv"] = 1, ["weekTotal"] = 2 });
            master.Courses.Add(new JsonObject { ["course"] = second, ["weekDiv"] = 2, ["weekTotal"] = 2 });
            Log.Info($"[课表窗口] 切换双周: 单周={first} 双周={second}");
        }
        else
        {
            // 清2周轮换 保3/4周
            var keep = WeekCourseOf(master, 1);
            master.Courses.RemoveAll(c => (c["weekTotal"]?.GetValue<int>() ?? 0) == 2);
            if (keep.Length > 0)
            {
                master.Course = keep;
            }
            Log.Info($"[课表窗口] 切换单周: {master.Course}");
        }
        master.Save();
        RebuildMasters();
    }

    /// <summary>分档案行操作</summary>
    private Control BuildSubOps(string masterName, string type, string bound)
    {
        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        ops.Children.Add(MakeToolButton(FASymbol.Add, Tr("schedule.sub_new"), () => CreateSubFor(masterName, type)));
        ops.Children.Add(MakeToolButton(FASymbol.Edit, Tr("schedule.profile_rename"), () => RenameSub(type, bound)));
        ops.Children.Add(MakeToolButton(FASymbol.Delete, Tr("schedule.profile_delete"), () => DeleteSub(type, bound)));
        var moreBtn = new Button { Content = "⋯", Width = 30, Height = 30, Padding = new Thickness(0), FontSize = 14 };
        ToolTip.SetTip(moreBtn, Tr("schedule.sub_more"));
        var menu = new FAMenuFlyout();
        var copyItem = new FAMenuFlyoutItem { Text = Tr("schedule.profile_copy"), IconSource = FAIcons.IconSource(FASymbol.Copy) };
        copyItem.Click += (_, _) => { if (bound.Length > 0) { ScheduleStore.CopySub(type, bound); } AfterSubChanged(); };
        var importItem = new FAMenuFlyoutItem { Text = Tr("schedule.sub_import"), IconSource = FAIcons.IconSource(FASymbol.Download) };
        importItem.Click += (_, _) => ImportSubTo(masterName, type);
        var exportItem = new FAMenuFlyoutItem { Text = Tr("schedule.profile_export"), IconSource = FAIcons.IconSourceGlyph("E74E") };
        exportItem.Click += (_, _) => ExportSub(type, bound);
        menu.Items.Add(copyItem);
        menu.Items.Add(importItem);
        menu.Items.Add(exportItem);
        moreBtn.Flyout = menu;
        ops.Children.Add(moreBtn);
        return ops;
    }

    private Control BuildSubRow(ScheduleProfile master, string type)
    {
        var label = Tr($"schedule.sub_{type}");
        var bound = type switch
        {
            "timetable" => master.Timetable,
            "duty" => master.Duty,
            "roster" => master.Roster,
            _ => master.Subject,
        };
        var subs = ScheduleStore.ListSubs(type);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,220,*,Auto"), MinHeight = 34 };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var combo = new ComboBox
        {
            MinWidth = 210,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 12,
        };
        combo.Items.Add(Tr("schedule.sub_unbound"));
        foreach (var s in subs)
        {
            combo.Items.Add(s);
        }
        var idx = bound.Length == 0 ? 0 : subs.IndexOf(bound) + 1;
        combo.SelectedIndex = idx;
        var masterName = master.Name;
        combo.SelectionChanged += (_, _) =>
        {
            if (_blockSync)
            {
                return;
            }
            var pick = combo.SelectedIndex <= 0 ? "" : subs[combo.SelectedIndex - 1];
            BindSub(masterName, type, pick);
        };
        Grid.SetColumn(combo, 1);
        row.Children.Add(combo);

        // 低频操作收进 Flyout
        var ops = BuildSubOps(masterName, type, bound);
        Grid.SetColumn(ops, 3);
        row.Children.Add(ops);
        return row;
    }

    private void AfterSubChanged()
    {
        LoadActive();
        RefreshAllPages();
        RebuildMasters();
    }

    private void UseMaster(string name)
    {
        // 启用为活动总档案
        ActiveMasterItem().Value = name;
        LoadActive();
        RefreshAllPages();
        RebuildMasters();
        Log.Info($"[课表窗口] 已启用总档案: {name} 来源={Config.ProfileSource.Value}");
    }

    /// <summary>切换课表来源</summary>
    private void SwitchSource(string source)
    {
        LoadActive();
        RefreshAllPages();
        ApplySourceMode(source);
        var isGlimpseon = source == "Glimpseon";
        if (_addMasterBtn is not null)
        {
            _addMasterBtn.IsVisible = isGlimpseon;
        }
        if (_importMasterBtn is not null)
        {
            _importMasterBtn.IsVisible = isGlimpseon;
        }
        RebuildMasters();
    }

    private void OnAddMaster()
    {
        var name = ScheduleStore.NextMasterName();
        var master = new ScheduleProfile { Name = name };
        master.RepairBindings();
        master.Save();
        Log.Info($"[课表窗口] 已新建总档案: {name}");
        RebuildMasters();
    }

    private void BindSub(string masterName, string type, string pick)
    {
        var master = ScheduleStore.LoadMaster(masterName);
        switch (type)
        {
            case "course":
                master.Course = pick;
                // 同步 div=1 条目
                SyncSingleWeek(master, pick);
                break;
            case "timetable": master.Timetable = pick; break;
            case "duty": master.Duty = pick; break;
            case "roster": master.Roster = pick; break;
            default: master.Subject = pick; break;
        }
        master.Save();
        if (masterName == ActiveMasterItem().Value)
        {
            AfterSubChanged();
        }
        Log.Info($"[课表窗口] 绑定分档案: {masterName}.{type} = {pick}");
    }

    private void CreateSubFor(string masterName, string type)
    {
        var name = ScheduleStore.NextSubName(type);
        switch (type)
        {
            case "course": new CourseDoc { Name = name }.Save(); break;
            case "timetable": new TimetableDoc { Name = name }.Save(); break;
            case "duty": new DutyDoc { Name = name }.Save(); break;
            case "roster": new RosterDoc { Name = name }.Save(); break;
            default: new SubjectDoc { Name = name }.Save(); break;
        }
        BindSub(masterName, type, name);
    }

    private async void RenameMaster(string name)
    {
        if (OwnerWindow is null)
        {
            return;
        }
        var newName = await Common.PromptTextInput(OwnerWindow, Tr("schedule.rename_master_title"), name);
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == name)
        {
            return;
        }
        newName = ScheduleStore.SanitizeName(newName);
        if (newName.Length == 0 || ScheduleStore.ListMasters().Contains(newName))
        {
            if (OwnerWindow is not null)
            {
                await Common.Confirm(OwnerWindow, Tr("timetable.warning_title"), Tr("schedule.name_exists"));
            }
            return;
        }
        ScheduleStore.RenameMaster(name, newName);
        if (Config.ScheduleProfile.Value == name)
        {
            Config.ScheduleProfile.Value = newName;
        }
        if (Config.ScheduleProfileCi.Value == name)
        {
            Config.ScheduleProfileCi.Value = newName;
        }
        if (Config.ScheduleProfileCw.Value == name)
        {
            Config.ScheduleProfileCw.Value = newName;
        }
        LoadActive();
        RebuildMasters();
    }

    private async void DeleteMaster(string name)
    {
        if (OwnerWindow is null)
        {
            return;
        }
        // 占用中禁删
        if (Config.ScheduleProfile.Value == name || Config.ScheduleProfileCi.Value == name || Config.ScheduleProfileCw.Value == name)
        {
            await Common.Confirm(OwnerWindow, Tr("timetable.warning_title"), Tr("schedule.master_in_use"));
            return;
        }
        var masters = ScheduleStore.ListMasters();
        if (masters.Count <= 1)
        {
            await Common.Confirm(OwnerWindow, Tr("timetable.warning_title"), Tr("schedule.delete_last_warning"));
            return;
        }
        if (!await Common.Confirm(OwnerWindow, Tr("timetable.confirm_delete_title"), Tr("schedule.confirm_delete_master", ("name", name))))
        {
            return;
        }
        ScheduleStore.DeleteMaster(name);
        if (ActiveMasterItem().Value == name)
        {
            LoadActive();
            RefreshAllPages();
        }
        RebuildMasters();
    }

    private async void RenameSub(string type, string oldName)
    {
        if (OwnerWindow is null || oldName.Length == 0)
        {
            return;
        }
        var newName = await Common.PromptTextInput(OwnerWindow, Tr("schedule.rename_sub_title"), oldName);
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == oldName)
        {
            return;
        }
        newName = ScheduleStore.SanitizeName(newName);
        if (newName.Length == 0 || ScheduleStore.ListSubs(type).Contains(newName))
        {
            if (OwnerWindow is not null)
            {
                await Common.Confirm(OwnerWindow, Tr("timetable.warning_title"), Tr("schedule.name_exists"));
            }
            return;
        }
        ScheduleStore.RenameSub(type, oldName, newName);
        AfterSubChanged();
    }

    private async void DeleteSub(string type, string name)
    {
        if (OwnerWindow is null || name.Length == 0)
        {
            return;
        }
        if (!await Common.Confirm(OwnerWindow, Tr("timetable.confirm_delete_title"), Tr("schedule.confirm_delete_sub", ("type", Tr($"schedule.sub_{type}")), ("name", name))))
        {
            return;
        }
        ScheduleStore.DeleteSub(type, name);
        AfterSubChanged();
    }

    private async void ExportMaster(string name)
    {
        if (OwnerWindow is null)
        {
            return;
        }
        try
        {
            var master = ScheduleStore.LoadMaster(name);
            var file = await OwnerWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Tr("schedule.profile_export"),
                SuggestedFileName = $"{name}.json",
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
            });
            if (file is null)
            {
                return;
            }
            File.WriteAllText(file.Path.LocalPath, master.ExportBundle().ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Log.Info($"[课表窗口] 导出总档案: {name} -> {file.Path.LocalPath}");
        }
        catch (Exception e)
        {
            Log.Error($"[课表窗口] 导出总档案失败: {e.Message}");
        }
    }

    private async void OnImportMaster()
    {
        if (OwnerWindow is null)
        {
            return;
        }
        try
        {
            var files = await OwnerWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Tr("schedule.profile_import"),
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
            });
            if (files.Count == 0)
            {
                return;
            }
            var node = JsonNode.Parse(await File.ReadAllTextAsync(files[0].Path.LocalPath));
            if (node is not JsonObject bundle || bundle["master"] is null)
            {
                Log.Warning("[课表窗口] 总档案导入失败: 格式不正确");
                return;
            }
            ScheduleProfile.ImportBundle(bundle, ScheduleStore.NextMasterName());
            RebuildMasters();
            Log.Info($"[课表窗口] 已导入总档案: {files[0].Path.LocalPath}");
        }
        catch (Exception e)
        {
            Log.Error($"[课表窗口] 导入总档案失败: {e.Message}");
        }
    }

    private async void ExportSub(string type, string name)
    {
        if (OwnerWindow is null || name.Length == 0)
        {
            return;
        }
        try
        {
            var file = await OwnerWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Tr("schedule.profile_export"),
                SuggestedFileName = $"{name}.json",
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
            });
            if (file is null)
            {
                return;
            }
            File.Copy(ScheduleStore.SubPath(type, name), file.Path.LocalPath, true);
            Log.Info($"[课表窗口] 导出分档案: {type}/{name} -> {file.Path.LocalPath}");
        }
        catch (Exception e)
        {
            Log.Error($"[课表窗口] 导出分档案失败: {e.Message}");
        }
    }

    private async void ImportSubTo(string masterName, string type)
    {
        if (OwnerWindow is null)
        {
            return;
        }
        try
        {
            var files = await OwnerWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Tr("schedule.sub_import"),
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
            });
            if (files.Count == 0)
            {
                return;
            }
            var newName = ScheduleStore.ImportSub(type, files[0].Path.LocalPath);
            BindSub(masterName, type, newName);
        }
        catch (Exception e)
        {
            Log.Error($"[课表窗口] 导入分档案失败: {e.Message}");
        }
    }

    private void OnOpenFolder()
    {
        Directory.CreateDirectory(Paths.DataProfile);
        Process.Start(new ProcessStartInfo { FileName = Paths.DataProfile, UseShellExecute = true });
    }

    // 联动模式

    private void ApplySourceMode(string source)
    {
        var follow = source != "Glimpseon" && SourceFollowMode();
        if (!follow)
        {
            _linkageMode = false;
            _activeBridge = null;
            _ciBridge.Stop();
            _cwBridge.Stop();
            _linkageTimer?.Stop();
            _courseEditorCard.IsVisible = true;
            _linkagePanel.IsVisible = false;
        }
        else
        {
            _linkageMode = true;
            _courseEditorCard.IsVisible = false;
            _linkagePanel.IsVisible = true;
            if (source == "classisland")
            {
                _activeBridge = _ciBridge;
                _ciBridge.SetDataPath(Config.LinkageDataPath.Value);
                _ciBridge.Start();
                _linkagePathLabel.Text = $"{Tr("timetable.profile_source")}: ClassIsland  {Config.LinkageDataPath.Value}";
            }
            else
            {
                _activeBridge = _cwBridge;
                _cwBridge.SetDataPath(Config.ClassWidgetsDataPath.Value);
                _cwBridge.Start();
                _linkagePathLabel.Text = $"{Tr("timetable.profile_source")}: ClassWidgets  {Config.ClassWidgetsDataPath.Value}";
            }
            _linkageTimer?.Start();
        }

        var interactive = !follow;
        // 可空引用 防御
        if (_courseCommandBar is { } cb) cb.IsEnabled = interactive;
        if (_timeCommandBar is { } tb) tb.IsEnabled = interactive;
        if (_dutyCommandBar is { } db) db.IsEnabled = interactive;
        if (_subjectCommandBar is { } sb) sb.IsEnabled = interactive;
        if (_rosterCommandBar is { } rb) rb.IsEnabled = interactive;
        if (_timeEditorCard is { } tec) tec.IsEnabled = interactive;
        if (_durCard is { } dc) dc.IsEnabled = interactive;
        if (_dutyEditorCard is { } dec) dec.IsEnabled = interactive;
        if (_subjectEditorCard is { } sec) sec.IsEnabled = interactive;
        if (_rosterEditorCard is { } rec) rec.IsEnabled = interactive;
        UpdateCaptions();

        if (follow)
        {
            RefreshLinkageTable();
        }
        else
        {
            RefreshCourseTable();
            RefreshTimeTable();
        }
    }

    /// <summary>页首小标题</summary>
    private void UpdateCaptions()
    {
        var followName = Tr("schedule.follow_data_name");
        if (_courseCaption is not null)
        {
            _courseCaption.Text = _linkageMode
                ? $"{Tr("schedule.course_title")} · {followName}"
                : $"{Tr("schedule.course_title")} · {_course.Name}";
            RebuildCourseWeekSwitch();
        }
        if (_timeCaption is not null)
        {
            _timeCaption.Text = _linkageMode
                ? $"{Tr("schedule.time_title")} · {followName}"
                : $"{Tr("schedule.time_title")} · {_timetable.Name}";
        }
        if (_dutyCaption is not null)
        {
            _dutyCaption.Text = $"{Tr("schedule.duty_title")} · {_duty.Name}";
        }
        if (_subjectCaption is not null)
        {
            _subjectCaption.Text = $"{Tr("schedule.subject_title")} · {_subject.Name}";
        }
        if (_rosterCaption is not null)
        {
            _rosterCaption.Text = $"{Tr("schedule.roster_title")} · {_roster.Name}";
        }
        if (_adjustCaption is not null)
        {
            _adjustCaption.Text = $"{Tr("schedule.adjust_title")} · {_master.Name}";
        }
    }

    /// <summary>外部数据映射副本</summary>
    private void RebuildFollowDocs()
    {
        var follow = new TimetableDoc { Name = Tr("schedule.follow_data_name") };
        var course = new CourseDoc { Name = Tr("schedule.follow_data_name") };

        Dictionary<int, List<ScheduleRow>>? week = _activeBridge switch
        {
            LinkageBridge ci => ci.GetWeekSchedule(),
            ClassWidgetsBridge cw => cw.GetWeekSchedule().ToDictionary(kv => kv.Key + 1, kv => kv.Value),
            _ => null,
        };

        if (week is not null)
        {
            var monday = week.GetValueOrDefault(1) ?? [];
            for (var i = 0; i < monday.Count; i++)
            {
                var r = monday[i];
                follow.Periods.Add(new JsonObject
                {
                    ["type"] = r.IsBreak ? "课间" : "上课",
                    ["start"] = r.StartTime,
                    ["end"] = r.EndTime,
                });
            }
            // wd Mon=1..Sun=7 → DayOrder 周日起列
            for (var wd = 1; wd <= 7; wd++)
            {
                var rows = week.GetValueOrDefault(wd) ?? [];
                for (var i = 0; i < rows.Count && i < follow.Periods.Count; i++)
                {
                    if (!rows[i].IsBreak && rows[i].Subject.Length > 0)
                    {
                        course.SetCell(i, DayOrder[wd % 7], rows[i].Subject);
                    }
                }
            }
        }
        else
        {
            var dayKey = DayKeyOf(DateOnly.FromDateTime(AppUtils.PreciseNow()));
            var rows = _followRows ?? [];
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                follow.Periods.Add(new JsonObject
                {
                    ["type"] = r.IsBreak ? "课间" : "上课",
                    ["start"] = r.StartTime,
                    ["end"] = r.EndTime,
                });
                if (!r.IsBreak)
                {
                    course.SetCell(i, dayKey, r.Subject);
                }
            }
        }
        _followTimetable = follow;
        _followCourse = course;
        _timetable = follow;
        _course = course;
    }

    /// <summary>跟随转自编辑</summary>
    private void PersistFollowSnapshot()
    {
        var rows = _followRows;
        if (rows is null || rows.Count == 0)
        {
            Log.Warning("[课表窗口] 无外部配置快照 自编辑保持原档案");
            return;
        }
        RebuildFollowDocs();
        try
        {
            var timetable = TimetableDoc.LoadOrDefault(_master.Timetable);
            timetable.Periods = ClonePeriods(_followTimetable.Periods);
            timetable.Save();
            var course = CourseDoc.LoadOrDefault(_master.Course);
            course.Courses = CloneCourses(_followCourse.Courses);
            course.Save();
            Log.Info($"[课表窗口] 外部配置快照已固化: {_master.Name} 时段={timetable.Periods.Count}");
        }
        catch (Exception e)
        {
            Log.Error($"[课表窗口] 外部配置快照固化失败: {e.Message}");
        }
    }

    private static List<JsonObject> ClonePeriods(IEnumerable<JsonObject> periods) =>
        periods.Select(p => (JsonObject)p.DeepClone()).ToList();

    private static Dictionary<string, JsonObject> CloneCourses(Dictionary<string, JsonObject> courses) =>
        courses.ToDictionary(kv => kv.Key, kv => (JsonObject)kv.Value.DeepClone());

    private void RefreshLinkageTable()
    {
        if (!_linkageMode || _activeBridge is null)
        {
            return;
        }
        List<ScheduleRow> schedule;
        try
        {
            schedule = _activeBridge switch
            {
                LinkageBridge ci => ci.GetTodaySchedule(),
                ClassWidgetsBridge cw => cw.GetTodaySchedule(),
                _ => [],
            };
        }
        catch (Exception e)
        {
            Log.Warning($"[课表窗口] 联动获取今日课表失败: {e.Message}");
            return;
        }

        // 结构变化全量重建
        var structureChanged = _followRows is null || _followRows.Count != schedule.Count;
        if (!structureChanged)
        {
            for (var i = 0; i < schedule.Count; i++)
            {
                if (_followRows![i].StartTime != schedule[i].StartTime ||
                    _followRows[i].EndTime != schedule[i].EndTime ||
                    _followRows[i].IsBreak != schedule[i].IsBreak)
                {
                    structureChanged = true;
                    break;
                }
            }
        }
        _followRows = schedule;
        if (structureChanged)
        {
            RebuildFollowDocs();
        }

        Dispatcher.UIThread.Post(() =>
        {
            _linkageTable.Items.Clear();
            foreach (var rowItem in schedule)
            {
                var subject = rowItem.IsBreak ? rowItem.BreakName : rowItem.Subject;
                var line = $"{rowItem.StartTime}~{rowItem.EndTime}  {(rowItem.IsBreak ? $"[{Tr("timetable.btn_break")}] " : "")}{subject}" +
                           (rowItem.IsCurrent ? "  ●" : "");
                _linkageTable.Items.Add(new TextBlock
                {
                    Text = line,
                    FontSize = 12,
                    Margin = new Thickness(4),
                    Foreground = rowItem.IsCurrent ? new SolidColorBrush(Color.Parse("#30c361")) : Brushes.Gray,
                    FontWeight = rowItem.IsCurrent ? FontWeight.SemiBold : FontWeight.Normal,
                });
            }
            // 同步主表格
            RefreshTimeTable();
            if (structureChanged)
            {
                RefreshCourseTable();
            }
            else
            {
                SyncCourseTable();
            }
        });
    }

    private void OnAutoDetect()
    {
        if (_activeBridge is null)
        {
            return;
        }
        var path = _activeBridge.AutoDetect();
        // 空串不覆盖既有路径
        if (path.Length == 0)
        {
            _linkagePathLabel.Text = Tr("timetable.auto_detect_failed");
            return;
        }
        _linkagePathLabel.Text = Tr("timetable.auto_detect_success", ("path", path));
        if (_activeBridge is LinkageBridge)
        {
            Config.LinkageDataPath.Value = path;
        }
        else
        {
            Config.ClassWidgetsDataPath.Value = path;
        }
        UpdateLinkagePathLabel();
    }

    private async void OnSelectDataDir()
    {
        if (OwnerWindow is null || _activeBridge is null)
        {
            return;
        }
        var dirs = await OwnerWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Tr("timetable.select_data_dir"),
            AllowMultiple = false,
        });
        if (dirs.Count == 0)
        {
            return;
        }
        var dir = dirs[0].Path.LocalPath;
        _activeBridge.SetDataPath(dir);
        _activeBridge.Start();
        if (_activeBridge is LinkageBridge)
        {
            Config.LinkageDataPath.Value = dir;
        }
        else
        {
            Config.ClassWidgetsDataPath.Value = dir;
        }
        UpdateLinkagePathLabel();
        Log.Info($"[课表窗口] 联动数据目录已设置: {dir}");
    }

    private void UpdateLinkagePathLabel()
    {
        var path = _activeBridge is LinkageBridge
            ? Config.LinkageDataPath.Value
            : Config.ClassWidgetsDataPath.Value;
        _linkagePathLabel.Text = $"{Tr("timetable.profile_source")}: " +
                                 $"{(_activeBridge is LinkageBridge ? "ClassIsland" : "ClassWidgets")}  {path}";
    }
}
