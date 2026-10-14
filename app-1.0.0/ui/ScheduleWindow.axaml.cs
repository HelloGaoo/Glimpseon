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
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

public partial class ScheduleWindow : GlimpseonWindow
{
    private static readonly string[] DayOrder =
        ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

    private static readonly string[] WeekdayOrder =
        ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private static readonly string[] PeriodTypes = ["上课", "课间", "活动"];
    private static readonly string[] Parities = ["单周", "双周"];

    private static readonly string[] SubjectShort =
        ["语", "数", "英", "政", "史", "地", "生", "物", "化", "科", "通", "劳", "班", "社", "心", "信", "考", "体", "早", "自"];

    private readonly Dictionary<string, Control> _pages = new();
    private FAFrame? _navFrame;
    private string _currentTag = "course";
    private bool _blockSync;

    private static string Tr(string key, params (string Name, object? Value)[] args) => AppUtils.Tr(key, args);

    // 主题笔刷
    private static IBrush LineBrush => new SolidColorBrush(Colors.Gray, 0.38);
    private static IBrush HeaderBg => new SolidColorBrush(Colors.Gray, 0.08);
    private static IBrush CardBg => new SolidColorBrush(Colors.Gray, 0.05);
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
    private WrapPanel _subjectFlow = null!;
    private DispatcherTimer? _subjectFlowDebounce;
    private ComboBox _sourceCombo = null!;
    private Border _courseEditorCard = null!;
    private TextBlock _courseEditorEmpty = null!;
    private StackPanel _courseEditorHost = null!;
    private StackPanel _linkagePanel = null!;
    private TextBlock _linkagePathLabel = null!;
    private ListBox _linkageTable = null!;
    private TextBlock? _courseCaption;
    private FACommandBar? _courseCommandBar;
    private readonly LinkageBridge _ciBridge = new();
    private readonly ClassWidgetsBridge _cwBridge = new();
    private LinkageBridgeBase? _activeBridge;
    private bool _linkageMode;
    private DispatcherTimer? _linkageTimer;

    // 时间表页
    private StackPanel _timeRowsHost = null!;
    private readonly List<Border> _timeCells = [];
    private readonly List<ComboBox> _timeTypeCombos = [];
    private readonly List<TextBlock> _timeStartTexts = [];
    private readonly List<TextBlock> _timeEndTexts = [];
    private int _timeSelRow = -1;
    private TimePicker _startTimePicker = null!;
    private TimePicker _endTimePicker = null!;
    private NumericUpDown _classDurSpin = null!;
    private NumericUpDown _breakDurSpin = null!;
    private StackPanel _timeEditHost = null!;
    private TextBlock _timeEmpty = null!;
    private TextBlock? _timeCaption;
    private FACommandBar? _timeCommandBar;
    private Border? _timeEditorCard;
    private Border? _durCard;

    // 值周表页
    private StackPanel _dutyRowsHost = null!;
    private readonly List<Border> _dutyCells = [];
    private readonly List<ComboBox> _dutyParityCombos = [];
    private readonly List<ComboBox> _dutyWeekdayCombos = [];
    private readonly List<TextBox> _dutyGroupEditors = [];
    private readonly List<TextBox> _dutyNameEditors = [];
    private readonly List<TextBox> _dutyRoleEditors = [];
    private int _dutySelRow = -1;
    private int _dutySelCol = -1;
    private StackPanel _dutyEditHost = null!;
    private TextBlock _dutyEmpty = null!;
    private ComboBox _dutyParityCombo = null!;
    private ComboBox _dutyWeekdayCombo = null!;
    private TextBox _dutyGroupEdit = null!;
    private TextBox _dutyNameEdit = null!;
    private TextBox _dutyRoleEdit = null!;
    private TextBlock? _dutyCaption;
    private FACommandBar? _dutyCommandBar;
    private Border? _dutyEditorCard;

    // 学科页
    private StackPanel _subjectRowsHost = null!;
    private readonly List<Border> _subjectCells = [];
    private readonly List<TextBox> _subjectNameEditors = [];
    private readonly List<TextBox> _subjectShortEditors = [];
    private readonly List<TextBox> _subjectTeacherEditors = [];
    private readonly List<CheckBox> _subjectOutdoorChecks = [];
    private int _subjectSelRow = -1;
    private int _subjectSelCol = -1;
    private StackPanel _subjectEditHost = null!;
    private TextBlock _subjectEmpty = null!;
    private TextBox _subjectNameEdit = null!;
    private TextBox _subjectShortEdit = null!;
    private TextBox _subjectTeacherEdit = null!;
    private CheckBox _subjectOutdoorCheck = null!;
    private TextBlock? _subjectCaption;
    private FACommandBar? _subjectCommandBar;
    private Border? _subjectEditorCard;

    // 跟随外部时内存副本
    private List<ScheduleRow>? _followRows;
    private TimetableDoc _followTimetable = new();
    private CourseDoc _followCourse = new();

    // 档案页
    private StackPanel _mastersHost = null!;
    private Button _addMasterBtn = null!;
    private Button _importMasterBtn = null!;

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    public ScheduleWindow()
    {
        InitializeComponent();
        Title = Tr("schedule.title");
        TitleLabel.Text = Tr("schedule.title");

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
        NavDuty.Content = Tr("schedule.tab_duty");
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

    /// <summary>当前课表来源对应的活动总档案配置项</summary>
    private static ConfigItem<string> ActiveMasterItem() => Config.ProfileSource.Value switch
    {
        "classisland" => Config.ScheduleProfileCi,
        "classwidgets" => Config.ScheduleProfileCw,
        _ => Config.ScheduleProfile,
    };

    /// <summary>CI/CW 跟随外部配置模式 true=分档案只读实时跟随 false=自编辑</summary>
    private static bool SourceFollowMode() => Config.ProfileSource.Value switch
    {
        "classisland" => Config.ScheduleProfileCiFollow.Value,
        "classwidgets" => Config.ScheduleProfileCwFollow.Value,
        _ => false,
    };

    /// <summary>总档案对应来源的跟随状态 null=非来源专属档案</summary>
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
        Log.Info($"[课表窗口] 切到总档案: {_master.Name} 来源={Config.ProfileSource.Value} 课表={_master.Course} 时间表={_master.Timetable} 值周表={_master.Duty} 学科={_master.Subject}");
    }

    // 页面构建

    private void BuildPages()
    {
        _pages.Clear();
        _pages["course"] = BuildCoursePage();
        _pages["time"] = BuildTimePage();
        _pages["duty"] = BuildDutyPage();
        _pages["subject"] = BuildSubjectPage();
        _pages["profile"] = BuildProfilePage();
    }

    private static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        FontSize = 22,
        FontWeight = FontWeight.Normal,
        Margin = new Thickness(24, 4, 24, 10),
    };

    private static Border MakeCard(Control content, Thickness? padding = null) => new()
    {
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Colors.Gray, 0.25),
        Background = Common.MakeCard().Background,
        Padding = padding ?? new Thickness(10, 8, 10, 8),
        Child = content,
        Margin = new Thickness(0, 0, 0, 8),
    };

    /// <summary>表格单元格容器</summary>
    private static Border MakeCell(Control content, int row, int col, bool header = false, Action? onClick = null)
    {
        var border = new Border
        {
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = header ? HeaderBg : null,
            Padding = new Thickness(2),
            MinHeight = 32,
            Child = content,
            Tag = (row, col),
        };
        if (col > 0)
        {
            Grid.SetColumn(border, col);
        }
        if (onClick is not null)
        {
            // 格内控件(ComboBox/CheckBox等)会吃掉 PointerPressed 使用 handledEventsToo 保证选中始终触发
            border.AddHandler(InputElement.PointerPressedEvent, (_, _) => onClick(), RoutingStrategies.Bubble, true);
        }
        return border;
    }

    private static TextBlock CellText(string text, bool header = false) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
        Opacity = header ? 0.8 : 1,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static TextBox CellEdit(string text, string? watermark = null) => new()
    {
        Text = text,
        FontSize = 12,
        MinHeight = 26,
        Padding = new Thickness(2, 1, 2, 1),
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Watermark = watermark,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>选中高亮</summary>
    private static void PaintCells(List<Border> cells, int selRow, int selCol)
    {
        foreach (var b in cells)
        {
            var (r, c) = ((int, int))b.Tag!;
            b.Background = r == selRow
                ? (c == selCol ? SelCellBg : SelRowBg)
                : null;
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

    private static Button MakeSmallButton(string text, Action action)
    {
        var btn = new Button
        {
            Content = text,
            Height = 30,
            Padding = new Thickness(12, 0, 12, 0),
            FontSize = 12,
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    /// <summary>全部页刷新 总档案切换后调用</summary>
    private void RefreshAllPages()
    {
        RefreshCourseTable();
        RefreshTimeTable();
        RefreshDutyTable();
        RefreshSubjectTable();
        RebuildSubjectFlow();
        UpdateCaptions();
    }

    // 页面布局

    /// <summary>区域命令条</summary>
    private static FACommandBar MakeCommandBar(params (FASymbol Icon, string Label, Action OnClick)[] commands)
    {
        var bar = new FACommandBar { DefaultLabelPosition = FACommandBarDefaultLabelPosition.Right };
        foreach (var (icon, label, onClick) in commands)
        {
            var btn = new FACommandBarButton { IconSource = FAIcons.IconSource(icon), Label = label };
            btn.Click += (_, _) => onClick();
            bar.PrimaryCommands.Add(btn);
        }
        return bar;
    }

    private static Grid MakeSplit(Control left, Control right) => new()
    {
        ColumnDefinitions = new ColumnDefinitions("*,12,320"),
        Margin = new Thickness(24, 0, 24, 16),
        Children = { left, right },
    };

    /// <summary>卡片外框</summary>
    private static Border MakeCardFrame(Control content) => new()
    {
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Colors.Gray, 0.25),
        Background = Common.MakeCard().Background,
        Child = content,
    };

    /// <summary>滚动表格</summary>
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
            BorderThickness = new Thickness(1, 1, 0, 0),
            Background = CardBg,
            Child = grid,
        };
    }

    /// <summary>左侧内容区</summary>
    private static Grid MakeLeftPanel(TextBlock caption, Control? header, Control table, Control? footer = null)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        grid.Children.Add(caption);
        if (header is not null)
        {
            Grid.SetRow(header, 1);
            grid.Children.Add(header);
        }
        Grid.SetRow(table, 2);
        grid.Children.Add(table);
        if (footer is not null)
        {
            Grid.SetRow(footer, 3);
            grid.Children.Add(footer);
        }
        return grid;
    }

    private static TextBlock MakeCaption() => new()
    {
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(2, 0, 0, 0),
    };

    /// <summary>右侧编辑区卡片</summary>
    private (Border Card, TextBlock Empty, StackPanel Host, StackPanel Inner) MakeEditorCard(string title)
    {
        var empty = new TextBlock
        {
            Text = Tr("schedule.editor_empty"),
            FontSize = 12,
            Opacity = 0.45,
            TextWrapping = TextWrapping.Wrap,
        };
        var host = new StackPanel { Spacing = 10, IsVisible = false };
        var inner = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold },
                empty,
                host,
            },
        };
        var card = MakeCardFrame(new ScrollViewer
        {
            Content = inner,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16, 12, 16, 12),
        });
        return (card, empty, host, inner);
    }

    private static void ShowEditor(TextBlock empty, StackPanel host, bool hasSelection)
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
        // 根布局
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(PageTitle(Tr("schedule.tab_course")));

        // 左: 周课表
        var cols = "96," + string.Join(",", Enumerable.Repeat("*", 7));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(cols) };
        header.Children.Add(MakeCell(CellText(Tr("timetable.time_range"), true), 0, 0, header: true));
        for (var d = 0; d < 7; d++)
        {
            header.Children.Add(MakeCell(CellText(Tr($"timetable.{DayOrder[d]}"), true), 0, d + 1, header: true));
        }
        _courseRowsHost = new StackPanel();
        var table = MakeScrollableTable(header, _courseRowsHost);

        var commandBar = MakeCommandBar(
            (FASymbol.Add, Tr("timetable.btn_class"), () => AddPeriod("上课")),
            (FASymbol.Clock, Tr("timetable.btn_break"), () => AddPeriod("课间")),
            (FASymbol.Play, Tr("timetable.btn_activity"), () => AddPeriod("活动")),
            (FASymbol.Delete, Tr("timetable.btn_delete_row"), OnDeleteSelectedRow));
        _courseCommandBar = commandBar;

        _courseCaption = MakeCaption();
        var left = MakeLeftPanel(_courseCaption, commandBar, table);
        Grid.SetColumn(left, 0);

        // 右: 选中格编辑
        var (editorCard, editorEmpty, editorHost, editorInner) = MakeEditorCard(Tr("schedule.course_editor_title"));
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
        Grid.SetColumn(editorCard, 2);

        var split = MakeSplit(left, editorCard);
        Grid.SetRow(split, 1);
        root.Children.Add(split);
        RebuildSubjectFlow();
        RefreshCourseTable();
        return root;
    }

    /// <summary>非课间活动行索引 参与课程表行</summary>
    private List<int> NonBreakIndices() =>
        Enumerable.Range(0, _timetable.Periods.Count)
            .Where(i => PeriodField(_timetable.Periods[i], "type") is not ("课间" or "活动"))
            .ToList();

    private static string PeriodField(JsonObject p, string key) =>
        p[key]?.GetValue<string>() ?? "";

    private void RefreshCourseTable()
    {
        var readOnly = _linkageMode;
        _blockSync = true;
        _courseRowsHost.Children.Clear();
        _courseCells.Clear();
        _courseHeadRangeTexts.Clear();
        _courseDayEditors.Clear();

        var cols = "96," + string.Join(",", Enumerable.Repeat("*", 7));
        var nb = NonBreakIndices();
        for (var tableRow = 0; tableRow < nb.Count; tableRow++)
        {
            var periodIdx = nb[tableRow];
            var p = _timetable.Periods[periodIdx];
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), MinHeight = 34 };

            var head = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(new TextBlock
            {
                Text = $"第{tableRow + 1}节",
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
                Grid.SetColumn(cell, d + 1);
                rowGrid.Children.Add(cell);
                _courseCells.Add(cell);
                dayEditors[d] = edit;
            }
            _courseDayEditors.Add(dayEditors);

            // 行容器
            var rowBorder = new Border { Child = rowGrid, Background = Brushes.Transparent };
            var r = tableRow;
            rowBorder.AddHandler(InputElement.PointerPressedEvent,
                (_, _) => SelectCourseCell(r, _courseSelCol < 0 ? 0 : Math.Min(_courseSelCol, 7)),
                RoutingStrategies.Bubble, true);
            _courseRowsHost.Children.Add(rowBorder);
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
            _courseInfo.Text = $"第{_courseSelRow + 1}节 · {range}";
            HighlightSubjectButton(null);
            return;
        }
        var day = Tr($"timetable.{DayOrder[_courseSelCol - 1]}");
        _courseInfo.Text = $"第{_courseSelRow + 1}节 · {range} · {day}";
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
            names = [.. SubjectShort];
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
        if (_subjectFlowDebounce is null)
        {
            _subjectFlowDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _subjectFlowDebounce.Tick += (_, _) => RebuildSubjectFlow();
        }
        _subjectFlowDebounce.Stop();
        _subjectFlowDebounce.Start();
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
        // 差分更新当前格
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
        _course.Save();
        return true;
    }

    // 时间表页

    private Control BuildTimePage()
    {
        // 根布局
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(PageTitle(Tr("schedule.tab_time")));

        // 左: 时间线表格
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110,110") };
        header.Children.Add(MakeCell(CellText(Tr("timetable.type"), true), 0, 0, header: true));
        header.Children.Add(MakeCell(CellText(Tr("timetable.start_time"), true), 0, 1, header: true));
        header.Children.Add(MakeCell(CellText(Tr("timetable.end_time"), true), 0, 2, header: true));
        _timeRowsHost = new StackPanel();
        var table = MakeScrollableTable(header, _timeRowsHost);

        var commandBar = MakeCommandBar(
            (FASymbol.Add, Tr("timetable.btn_class"), () => AddPeriod("上课")),
            (FASymbol.Clock, Tr("timetable.btn_break"), () => AddPeriod("课间")),
            (FASymbol.Play, Tr("timetable.btn_activity"), () => AddPeriod("活动")),
            (FASymbol.Delete, Tr("timetable.btn_delete_row"), OnDeleteSelectedRow));
        _timeCommandBar = commandBar;

        _timeCaption = MakeCaption();
        var left = MakeLeftPanel(_timeCaption, commandBar, table);
        Grid.SetColumn(left, 0);

        // 右: 选中时段编辑
        var (editorCard, editorEmpty, editorHost, _) = MakeEditorCard(Tr("schedule.time_editor_title"));
        _timeEditorCard = editorCard;
        _timeEmpty = editorEmpty;
        _timeEditHost = editorHost;
        _startTimePicker = new TimePicker { HorizontalAlignment = HorizontalAlignment.Stretch };
        _startTimePicker.SelectedTimeChanged += (_, _) => OnPickerChanged();
        _endTimePicker = new TimePicker { HorizontalAlignment = HorizontalAlignment.Stretch };
        _endTimePicker.SelectedTimeChanged += (_, _) => OnPickerChanged();
        editorHost.Children.Add(MakeField(Tr("timetable.chs_start_time"), _startTimePicker));
        editorHost.Children.Add(MakeField(Tr("timetable.chs_end_time"), _endTimePicker));
        ShowEditor(editorEmpty, editorHost, false);

        // 默认时长
        _classDurSpin = new NumericUpDown { Minimum = 1, Maximum = 120, Value = _timetable.DefaultClassDuration, HorizontalAlignment = HorizontalAlignment.Stretch };
        _classDurSpin.ValueChanged += (_, _) => OnDurationChanged();
        _breakDurSpin = new NumericUpDown { Minimum = 1, Maximum = 60, Value = _timetable.DefaultBreakDuration, HorizontalAlignment = HorizontalAlignment.Stretch };
        _breakDurSpin.ValueChanged += (_, _) => OnDurationChanged();
        var durRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        durRow.Children.Add(MakeField(Tr("timetable.default_class_duration"), _classDurSpin, Tr("timetable.minutes")));
        var breakField = MakeField(Tr("timetable.default_break_duration"), _breakDurSpin, Tr("timetable.minutes"));
        Grid.SetColumn(breakField, 2);
        durRow.Children.Add(breakField);
        var durCard = MakeCard(durRow, new Thickness(12, 8, 12, 10));
        _durCard = durCard;

        var rightColumn = new Grid { RowDefinitions = new RowDefinitions("*,8,Auto") };
        Grid.SetRow(editorCard, 0);
        rightColumn.Children.Add(editorCard);
        Grid.SetRow(durCard, 2);
        rightColumn.Children.Add(durCard);
        Grid.SetColumn(rightColumn, 2);

        var split = MakeSplit(left, rightColumn);
        Grid.SetRow(split, 1);
        root.Children.Add(split);
        RefreshTimeTable();
        return root;
    }

    private void RefreshTimeTable()
    {
        var readOnly = _linkageMode;
        _blockSync = true;
        _timeRowsHost.Children.Clear();
        _timeCells.Clear();
        _timeTypeCombos.Clear();
        _timeStartTexts.Clear();
        _timeEndTexts.Clear();
        for (var i = 0; i < _timetable.Periods.Count; i++)
        {
            var p = _timetable.Periods[i];
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110,110"), MinHeight = 34 };

            var typeCombo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                IsEnabled = !readOnly,
            };
            foreach (var t in PeriodTypes)
            {
                typeCombo.Items.Add(t);
            }
            var type = PeriodField(p, "type");
            var typeIdx = Array.IndexOf(PeriodTypes, type);
            typeCombo.SelectedIndex = typeIdx >= 0 ? typeIdx : 0;
            var row = i;
            typeCombo.SelectionChanged += (_, _) =>
            {
                if (_blockSync || row >= _timetable.Periods.Count)
                {
                    return;
                }
                _timetable.Periods[row]["type"] = PeriodTypes[Math.Max(0, typeCombo.SelectedIndex)];
                _timetable.Save();
                // 类型变化改变课程表行构成
                RefreshCourseTable();
            };
            var typeCell = MakeCell(typeCombo, row, 0, onClick: () => SelectTimeRow(row));
            rowGrid.Children.Add(typeCell);
            _timeCells.Add(typeCell);
            _timeTypeCombos.Add(typeCombo);

            var startText = CellText(PeriodField(p, "start"));
            var endText = CellText(PeriodField(p, "end"));
            var startCell = MakeCell(startText, row, 1, onClick: () => SelectTimeRow(row));
            var endCell = MakeCell(endText, row, 2, onClick: () => SelectTimeRow(row));
            rowGrid.Children.Add(startCell);
            rowGrid.Children.Add(endCell);
            _timeCells.Add(startCell);
            _timeCells.Add(endCell);
            _timeStartTexts.Add(startText);
            _timeEndTexts.Add(endText);

            // 行容器
            var rowBorder = new Border { Child = rowGrid, Background = Brushes.Transparent };
            var r = i;
            rowBorder.AddHandler(InputElement.PointerPressedEvent,
                (_, _) => SelectTimeRow(r), RoutingStrategies.Bubble, true);
            _timeRowsHost.Children.Add(rowBorder);
        }
        _blockSync = false;
        if (_timeSelRow >= _timetable.Periods.Count)
        {
            _timeSelRow = _timetable.Periods.Count - 1;
        }
        PaintCells(_timeCells, _timeSelRow, -1);
        SyncPickers();
    }

    /// <summary>差分同步时间行</summary>
    private void SyncTimeTable()
    {
        _blockSync = true;
        for (var i = 0; i < _timetable.Periods.Count && i < _timeTypeCombos.Count; i++)
        {
            var p = _timetable.Periods[i];
            _timeStartTexts[i].Text = PeriodField(p, "start");
            _timeEndTexts[i].Text = PeriodField(p, "end");
            var idx = Array.IndexOf(PeriodTypes, PeriodField(p, "type"));
            var want = idx >= 0 ? idx : 0;
            if (_timeTypeCombos[i].SelectedIndex != want)
            {
                _timeTypeCombos[i].SelectedIndex = want;
            }
        }
        _blockSync = false;
    }

    private void SelectTimeRow(int row)
    {
        if (row < 0 || row >= _timetable.Periods.Count)
        {
            return;
        }
        _timeSelRow = row;
        PaintCells(_timeCells, row, -1);
        SyncPickers();
    }

    private void SyncPickers()
    {
        var hasSel = _timeSelRow >= 0 && _timeSelRow < _timetable.Periods.Count;
        ShowEditor(_timeEmpty, _timeEditHost, hasSel);
        _blockSync = true;
        if (hasSel)
        {
            var p = _timetable.Periods[_timeSelRow];
            _startTimePicker.SelectedTime = ParseTime(PeriodField(p, "start"));
            _endTimePicker.SelectedTime = ParseTime(PeriodField(p, "end"));
        }
        else
        {
            var next = _timetable.GetNextStartTime();
            var parts = next.Split(':');
            var h = parts.Length > 0 && int.TryParse(parts[0], out var hh) ? hh : 8;
            var m = parts.Length > 1 && int.TryParse(parts[1], out var mm) ? mm : 0;
            _startTimePicker.SelectedTime = new TimeSpan(h, m, 0);
            var dur = _timetable.DefaultClassDuration;
            _endTimePicker.SelectedTime = new TimeSpan((h * 60 + m + dur) / 60 % 24, (h * 60 + m + dur) % 60, 0);
        }
        _blockSync = false;
    }

    private static TimeSpan? ParseTime(string s)
    {
        if (TimeOnly.TryParse(s, out var t))
        {
            return t.ToTimeSpan();
        }
        return new TimeSpan(8, 0, 0);
    }

    private void OnPickerChanged()
    {
        if (_blockSync || _linkageMode || _timeSelRow < 0 || _timeSelRow >= _timetable.Periods.Count)
        {
            return;
        }
        var start = _startTimePicker.SelectedTime?.ToString(@"hh\:mm") ?? "08:00";
        var end = _endTimePicker.SelectedTime?.ToString(@"hh\:mm") ?? "08:40";
        _timetable.Periods[_timeSelRow]["start"] = start;
        _timetable.Periods[_timeSelRow]["end"] = end;
        Log.Info($"[课表窗口] 修改时间行: 行{_timeSelRow} -> {start}~{end}");
        _timetable.Save();
        SyncTimeTable();
        SyncCourseTable();
    }

    /// <summary>新增时段</summary>
    private void AddPeriod(string periodType)
    {
        if (_linkageMode)
        {
            return;
        }
        var start = _timetable.GetNextStartTime();
        var parts = start.Split(':');
        var h = parts.Length > 0 && int.TryParse(parts[0], out var hh) ? hh : 8;
        var m = parts.Length > 1 && int.TryParse(parts[1], out var mm) ? mm : 0;
        var dur = periodType == "课间" ? _timetable.DefaultBreakDuration : _timetable.DefaultClassDuration;
        var total = h * 60 + m + Math.Max(1, dur);
        var end = $"{total / 60 % 24:00}:{total % 60:00}";
        Log.Info($"[课表窗口] 添加时段: {periodType} {start}~{end}");
        _timetable.AddPeriod(periodType, start, end);
        _course.Courses[(_timetable.Periods.Count - 1).ToString()] = new JsonObject();
        _timetable.Save();
        _course.Save();
        RefreshTimeTable();
        RefreshCourseTable();
        _timeSelRow = _timetable.Periods.Count - 1;
        SelectTimeRow(_timeSelRow);
    }

    private void OnDeleteSelectedRow()
    {
        if (_linkageMode || _timetable.Periods.Count == 0)
        {
            return;
        }
        int periodIdx;
        if (_timeSelRow >= 0)
        {
            periodIdx = _timeSelRow;
        }
        else if (_courseSelRow >= 0)
        {
            var nb = NonBreakIndices();
            if (_courseSelRow >= nb.Count)
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
        SyncPickers();
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

    // 值周表页

    private Control BuildDutyPage()
    {
        // 根布局
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(PageTitle(Tr("schedule.tab_duty")));

        // 左: 值周表格
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("110,110,*,*,*") };
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_parity"), true), 0, 0, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_weekday"), true), 0, 1, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_group"), true), 0, 2, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_name"), true), 0, 3, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_role"), true), 0, 4, header: true));
        _dutyRowsHost = new StackPanel();
        var table = MakeScrollableTable(header, _dutyRowsHost);

        var commandBar = MakeCommandBar(
            (FASymbol.Add, Tr("schedule.add_row"), OnDutyAddRow),
            (FASymbol.Delete, Tr("schedule.delete_row"), OnDutyDeleteRow));
        _dutyCommandBar = commandBar;

        _dutyCaption = MakeCaption();
        var left = MakeLeftPanel(_dutyCaption, commandBar, table);
        Grid.SetColumn(left, 0);

        // 右: 选中行编辑
        var (editorCard, editorEmpty, editorHost, _) = MakeEditorCard(Tr("schedule.duty_editor_title"));
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
        Grid.SetColumn(editorCard, 2);

        var split = MakeSplit(left, editorCard);
        Grid.SetRow(split, 1);
        root.Children.Add(split);
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
            Grid.SetColumn(weekdayCell, 1);
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
                    _duty.Save();
                }
            };
            var groupCell = MakeCell(groupEdit, row, 2, onClick: () => SelectDutyCell(row, 2));
            Grid.SetColumn(groupCell, 2);
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
                    _duty.Save();
                }
            };
            var nameCell = MakeCell(nameEdit, row, 3, onClick: () => SelectDutyCell(row, 3));
            Grid.SetColumn(nameCell, 3);
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
                    _duty.Save();
                }
            };
            var roleCell = MakeCell(roleEdit, row, 4, onClick: () => SelectDutyCell(row, 4));
            Grid.SetColumn(roleCell, 4);
            rowGrid.Children.Add(roleCell);
            _dutyCells.Add(roleCell);
            _dutyRoleEditors.Add(roleEdit);

            // 行容器
            var rowBorder = new Border { Child = rowGrid, Background = Brushes.Transparent };
            var rowIdx = i;
            rowBorder.AddHandler(InputElement.PointerPressedEvent,
                (_, _) => SelectDutyCell(rowIdx, _dutySelCol < 0 ? 0 : Math.Min(_dutySelCol, 4)),
                RoutingStrategies.Bubble, true);
            _dutyRowsHost.Children.Add(rowBorder);
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

    /// <summary>右侧编辑表单回填选中行</summary>
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

    /// <summary>右侧编辑表单写回选中行</summary>
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
                SyncDutyEditorField(row, _dutyGroupEditors, _dutyGroupEdit.Text);
                break;
            case "name":
                r["name"] = _dutyNameEdit.Text ?? "";
                SyncDutyEditorField(row, _dutyNameEditors, _dutyNameEdit.Text);
                break;
            case "role":
                r["role"] = _dutyRoleEdit.Text ?? "";
                SyncDutyEditorField(row, _dutyRoleEditors, _dutyRoleEdit.Text);
                break;
        }
        _duty.Save();
    }

    private void SyncDutyEditorField(int row, List<TextBox> editors, string? text)
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

    // 学科页

    private Control BuildSubjectPage()
    {
        // 根布局
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(PageTitle(Tr("schedule.tab_subject")));

        // 左: 学科表格
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,*,110") };
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_subject"), true), 0, 0, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_short"), true), 0, 1, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_teacher"), true), 0, 2, header: true));
        header.Children.Add(MakeCell(CellText(Tr("schedule.col_outdoor"), true), 0, 3, header: true));
        _subjectRowsHost = new StackPanel();
        var table = MakeScrollableTable(header, _subjectRowsHost);

        var commandBar = MakeCommandBar(
            (FASymbol.Add, Tr("schedule.add_row"), OnSubjectAddRow),
            (FASymbol.Delete, Tr("schedule.delete_row"), OnSubjectDeleteRow));
        _subjectCommandBar = commandBar;

        _subjectCaption = MakeCaption();
        var left = MakeLeftPanel(_subjectCaption, commandBar, table);
        Grid.SetColumn(left, 0);

        // 右: 选中科目编辑
        var (editorCard, editorEmpty, editorHost, _) = MakeEditorCard(Tr("schedule.subject_editor_title"));
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
        var nameShortRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        nameShortRow.Children.Add(MakeField(Tr("schedule.col_subject"), _subjectNameEdit));
        var shortField = MakeField(Tr("schedule.col_short"), _subjectShortEdit);
        Grid.SetColumn(shortField, 1);
        nameShortRow.Children.Add(shortField);
        editorHost.Children.Add(nameShortRow);
        editorHost.Children.Add(MakeField(Tr("schedule.col_teacher"), _subjectTeacherEdit));
        editorHost.Children.Add(_subjectOutdoorCheck);
        ShowEditor(editorEmpty, editorHost, false);
        Grid.SetColumn(editorCard, 2);

        var split = MakeSplit(left, editorCard);
        Grid.SetRow(split, 1);
        root.Children.Add(split);
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
        for (var i = 0; i < _subject.Rows.Count; i++)
        {
            var r = _subject.Rows[i];
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80,*,110"), MinHeight = 34 };
            var row = i;

            var nameEdit = CellEdit(r["name"]?.GetValue<string>() ?? "");
            nameEdit.IsReadOnly = readOnly;
            nameEdit.TextChanged += (_, _) =>
            {
                if (!_blockSync && row < _subject.Rows.Count)
                {
                    _subject.Rows[row]["name"] = nameEdit.Text ?? "";
                    _subject.Save();
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
                    _subject.Save();
                }
            };
            var shortCell = MakeCell(shortEdit, row, 1, onClick: () => SelectSubjectCell(row, 1));
            Grid.SetColumn(shortCell, 1);
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
                    _subject.Save();
                }
            };
            var teacherCell = MakeCell(teacherEdit, row, 2, onClick: () => SelectSubjectCell(row, 2));
            Grid.SetColumn(teacherCell, 2);
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
            Grid.SetColumn(outdoorCell, 3);
            rowGrid.Children.Add(outdoorCell);
            _subjectCells.Add(outdoorCell);
            _subjectOutdoorChecks.Add(outdoorCheck);

            // 行容器
            var rowBorder = new Border { Child = rowGrid, Background = Brushes.Transparent };
            var rowIdx = i;
            rowBorder.AddHandler(InputElement.PointerPressedEvent,
                (_, _) => SelectSubjectCell(rowIdx, _subjectSelCol < 0 ? 0 : Math.Min(_subjectSelCol, 3)),
                RoutingStrategies.Bubble, true);
            _subjectRowsHost.Children.Add(rowBorder);
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

    /// <summary>右侧编辑表单回填选中行</summary>
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
        _blockSync = false;
    }

    /// <summary>右侧编辑表单写回选中行</summary>
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
                SyncSubjectEditorField(row, _subjectNameEditors, _subjectNameEdit.Text);
                QueueSubjectFlowRebuild();
                break;
            case "short":
                r["short"] = _subjectShortEdit.Text ?? "";
                SyncSubjectEditorField(row, _subjectShortEditors, _subjectShortEdit.Text);
                break;
            case "teacher":
                r["teacher"] = _subjectTeacherEdit.Text ?? "";
                SyncSubjectEditorField(row, _subjectTeacherEditors, _subjectTeacherEdit.Text);
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
        _subject.Save();
    }

    private void SyncSubjectEditorField(int row, List<TextBox> editors, string? text)
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
        // 卡片列表
        var root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Content = new StackPanel();
        var page = (StackPanel)root.Content;
        page.Children.Add(PageTitle(Tr("schedule.tab_profile")));

        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 16), Spacing = 8 };
        var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        // 课表来源选择
        _sourceCombo = new ComboBox { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
        _sourceCombo.Items.Add("Glimpseon");
        _sourceCombo.Items.Add("ClassIsland");
        _sourceCombo.Items.Add("ClassWidgets");
        _sourceCombo.SelectedIndex = Config.ProfileSource.Value switch
        {
            "classisland" => 1,
            "classwidgets" => 2,
            _ => 0,
        };
        _sourceCombo.SelectionChanged += (_, _) =>
        {
            if (_blockSync)
            {
                return;
            }
            var source = _sourceCombo.SelectedIndex switch
            {
                1 => "classisland",
                2 => "classwidgets",
                _ => "Glimpseon",
            };
            Log.Info($"[课表窗口] 切换档案来源: {source}");
            Config.ProfileSource.Value = source;
            SwitchSource(source);
        };
        ToolTip.SetTip(_sourceCombo, Tr("timetable.profile_source"));
        topRow.Children.Add(_sourceCombo);

        // 总档案增删管理
        var isGlimpseonSource = Config.ProfileSource.Value == "Glimpseon";
        _addMasterBtn = MakeSmallButton(Tr("schedule.profile_add"), OnAddMaster);
        _importMasterBtn = MakeSmallButton(Tr("schedule.profile_import"), OnImportMaster);
        _addMasterBtn.IsVisible = isGlimpseonSource;
        _importMasterBtn.IsVisible = isGlimpseonSource;
        topRow.Children.Add(_addMasterBtn);
        topRow.Children.Add(_importMasterBtn);
        topRow.Children.Add(MakeSmallButton(Tr("schedule.open_folder"), OnOpenFolder));
        stack.Children.Add(topRow);
        stack.Children.Add(new TextBlock
        {
            Text = Tr("schedule.profile_hint"),
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
        });

        _mastersHost = new StackPanel { Spacing = 8 };
        stack.Children.Add(_mastersHost);
        page.Children.Add(stack);

        RebuildMasters();
        return root;
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
            header.Children.Add(new Border
            {
                Background = isCurrentSource ? AccentBrush : new SolidColorBrush(Colors.Gray, 0.35),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = isCurrentSource ? 1 : 0.7,
                Child = new TextBlock
                {
                    Text = $"{src} · {Tr("schedule.profile_in_use")}",
                    FontSize = 11,
                    Foreground = Brushes.White,
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
        ops.Children.Add(MakeToolButton(FASymbol.Save, Tr("schedule.profile_export"), () => ExportMaster(name)));
        header.Children.Add(ops);

        // CI/CW 档案状态选择器
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
                    // 自编辑起点 = 最后一刻的外部配置快照
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
            subHost.Children.Add(BuildSubRow(master, type));
        }
        card.Children.Add(subHost);

        return MakeCard(card, new Thickness(16, 12, 16, 12));
    }

    private Control BuildSubRow(ScheduleProfile master, string type)
    {
        var label = Tr($"schedule.sub_{type}");
        var bound = type switch
        {
            "course" => master.Course,
            "timetable" => master.Timetable,
            "duty" => master.Duty,
            _ => master.Subject,
        };
        var subs = ScheduleStore.ListSubs(type);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("70,220,*,Auto"), MinHeight = 34 };
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
        _blockSync = true;
        combo.SelectedIndex = idx;
        _blockSync = false;
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

        var ops = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        ops.Children.Add(MakeToolButton(FASymbol.Add, Tr("schedule.sub_new"), () => CreateSubFor(masterName, type)));
        ops.Children.Add(MakeToolButton(FASymbol.Edit, Tr("schedule.profile_rename"), () => RenameSub(type, bound)));
        ops.Children.Add(MakeToolButton(FASymbol.Copy, Tr("schedule.profile_copy"), () => { if (bound.Length > 0) { ScheduleStore.CopySub(type, bound); } AfterSubChanged(); }));
        ops.Children.Add(MakeToolButton(FASymbol.Delete, Tr("schedule.profile_delete"), () => DeleteSub(type, bound)));
        ops.Children.Add(MakeToolButton(FASymbol.Download, Tr("schedule.sub_import"), () => ImportSubTo(masterName, type)));
        ops.Children.Add(MakeToolButton(FASymbol.Save, Tr("schedule.profile_export"), () => ExportSub(type, bound)));
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
        // 使用绑定到当前课表来源的活动总档案
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
            case "course": master.Course = pick; break;
            case "timetable": master.Timetable = pick; break;
            case "duty": master.Duty = pick; break;
            default: master.Subject = pick; break;
        }
        master.Save();
        if (masterName == Config.ScheduleProfile.Value)
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
        // 被任一来源使用的总档案禁止删除
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
            var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(files[0].Path.LocalPath));
            if (node is not System.Text.Json.Nodes.JsonObject bundle || bundle["master"] is null)
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
        _courseCommandBar.IsEnabled = interactive;
        _timeCommandBar.IsEnabled = interactive;
        _dutyCommandBar.IsEnabled = interactive;
        _subjectCommandBar.IsEnabled = interactive;
        _timeEditorCard.IsEnabled = interactive;
        _durCard.IsEnabled = interactive;
        _dutyEditorCard.IsEnabled = interactive;
        _subjectEditorCard.IsEnabled = interactive;
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
    }

    /// <summary>外部数据映射为内存文档副本</summary>
    private void RebuildFollowDocs()
    {
        var follow = new TimetableDoc { Name = Tr("schedule.follow_data_name") };
        var course = new CourseDoc { Name = Tr("schedule.follow_data_name") };

        Dictionary<int, List<ScheduleRow>>? week = _activeBridge switch
        {
            LinkageBridge => _ciBridge.GetWeekSchedule(),
            ClassWidgetsBridge => _cwBridge.GetWeekSchedule().ToDictionary(kv => kv.Key + 1, kv => kv.Value),
            _ => null,
        };

        if (week is not null)
        {
            string[] dayKeys = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];
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
            for (var wd = 1; wd <= 7; wd++)
            {
                var rows = week.GetValueOrDefault(wd) ?? [];
                for (var i = 0; i < rows.Count && i < follow.Periods.Count; i++)
                {
                    if (!rows[i].IsBreak && rows[i].Subject.Length > 0)
                    {
                        course.SetCell(i, dayKeys[wd - 1], rows[i].Subject);
                    }
                }
            }
        }
        else
        {
            var dayKey = AppUtils.PreciseNow().DayOfWeek switch
            {
                DayOfWeek.Monday => "monday",
                DayOfWeek.Tuesday => "tuesday",
                DayOfWeek.Wednesday => "wednesday",
                DayOfWeek.Thursday => "thursday",
                DayOfWeek.Friday => "friday",
                DayOfWeek.Saturday => "saturday",
                _ => "sunday",
            };
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
        periods.Select(p => (JsonObject)JsonNode.Parse(p.ToJsonString())!).ToList();

    private static Dictionary<string, JsonObject> CloneCourses(Dictionary<string, JsonObject> courses) =>
        courses.ToDictionary(kv => kv.Key, kv => (JsonObject)JsonNode.Parse(kv.Value.ToJsonString())!);

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
                var line = $"{rowItem.StartTime}~{rowItem.EndTime}  {(rowItem.IsBreak ? "[课间] " : "")}{subject}" +
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
            // 外部数据同步到主表格展示
            if (structureChanged)
            {
                RefreshTimeTable();
                RefreshCourseTable();
            }
            else
            {
                SyncTimeTable();
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
        // 检测失败(空串)时不得覆盖用户已配置的路径
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
