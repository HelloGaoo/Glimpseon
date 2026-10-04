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

// 课程表

using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Glimpseon.Core;
using Glimpseon.Core.Services;

namespace Glimpseon.UI;

public class TimetableView : UserControl
{
    private static readonly string[] Days =
        ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];


    private static readonly string[] SubjectShort =
        ["语", "数", "英", "政", "史", "地", "生", "物", "化", "科", "通", "劳", "班", "社", "心", "信", "考", "体", "早", "自"];

    private static readonly Dictionary<string, string> SubjectFull = new()
    {
        ["语"] = "语文", ["数"] = "数学", ["英"] = "英语", ["政"] = "政治", ["史"] = "历史",
        ["地"] = "地理", ["生"] = "生物", ["物"] = "物理", ["化"] = "化学", ["科"] = "科学",
        ["通"] = "通用技术", ["劳"] = "劳动", ["班"] = "班会", ["社"] = "社团", ["心"] = "心理",
        ["信"] = "信息技术", ["考"] = "考试", ["体"] = "体育", ["早"] = "早读", ["自"] = "自习",
    };

    private readonly LinkageBridge _ciBridge = new LinkageBridge();
    private readonly ClassWidgetsBridge _cwBridge = new ClassWidgetsBridge();
    private LinkageBridgeBase? _activeBridge;
    private bool _linkageMode;
    private DispatcherTimer? _linkageTimer;

    private TimetableProfile? _profile;
    private string _profileName = "";
    private bool _blockSave;
    private bool _blockPicker;
    private string? _activeTable;
    private int _selectedTimeRow = -1;
    private int _selectedCourseRow = -1;
    private int _selectedCourseCol = -1;
    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    // 控件
    private TextBlock _profileLabel = null!;
    private StackPanel _courseRows = null!;
    private StackPanel _timeRows = null!;
    private StackPanel _emptyState = null!;
    private StackPanel _timeSettings = null!;
    private StackPanel _courseSettings = null!;
    private TextBlock _csInfoLabel = null!;
    private TextBox _csSubjectEdit = null!;
    private TimePicker _startTimePicker = null!;
    private TimePicker _endTimePicker = null!;
    private NumericUpDown _classDurSpin = null!;
    private NumericUpDown _breakDurSpin = null!;
    private ComboBox _sourceCombo = null!;
    private StackPanel _editModeHost = null!;
    private StackPanel _linkagePanel = null!;
    private TextBlock _linkagePathLabel = null!;
    private ListBox _linkageTable = null!;
    private readonly List<Button> _subjectButtons = [];
    private readonly List<List<Control>> _courseRowCells = [];

    public TimetableView(MainWindow mainWindow)
    {
        var root = new StackPanel { Spacing = 6 };
        root.Children.Add(Common.MakeTitle(AppUtils.Tr("navigation.timetable")));

        var split = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("7*,3*"),
            ColumnSpacing = 12,
        };

        var left = BuildLeftPanel();
        var right = BuildRightPanel();
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        split.Children.Add(left);
        split.Children.Add(right);
        root.Children.Add(split);

        Content = Common.MakePageScroll(root);

        // 联动 1 秒刷新
        _linkageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _linkageTimer.Tick += (_, _) => RefreshLinkageTable();

        // 加载档案
        var currentSource = Config.ProfileSource.Value;
        LoadProfile(Timetable.EnsureDefaultProfile());
        if (currentSource != "Glimpseon")
        {
            var index = currentSource switch
            {
                "classisland" => 1,
                "classwidgets" => 2,
                _ => 0,
            };
            _sourceCombo.SelectedIndex = index;
            ApplySourceMode(currentSource);
        }
        Log.Debug($"[课表页] 就绪 档案={_profileName} 时段={_profile?.PeriodCount() ?? 0} 源={currentSource}");
    }

    // 左栏

    private Control BuildLeftPanel()
    {
        var panel = new StackPanel { Spacing = 8 };

        // 课程表卡
        var courseCard = Common.MakeCard();
        var courseStack = new StackPanel { Spacing = 2, Margin = new Thickness(8, 4, 8, 4) };
        courseStack.Children.Add(new TextBlock
        {
            Text = AppUtils.Tr("timetable.course_table"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 4, 0, 2),
        });

        // 表头
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("110,*,*,*,*,*,*,*"),
            Margin = new Thickness(0, 0, 0, 2),
        };
        AddCell(header, AppUtils.Tr("timetable.time_range"), 0, true);
        for (var d = 0; d < 7; d++)
        {
            AddCell(header, AppUtils.Tr($"timetable.{Days[d]}"), d + 1, true);
        }
        courseStack.Children.Add(header);

        _courseRows = new StackPanel();
        courseStack.Children.Add(_courseRows);
        courseCard.Child = courseStack;
        panel.Children.Add(courseCard);

        // 时间表卡
        var timeCard = Common.MakeCard();
        var timeStack = new StackPanel { Spacing = 2, Margin = new Thickness(8, 4, 8, 4) };
        timeStack.Children.Add(new TextBlock
        {
            Text = AppUtils.Tr("timetable.time_schedule"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 4, 0, 2),
        });
        var timeHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,110,110"),
            Margin = new Thickness(0, 0, 0, 2),
        };
        AddCell(timeHeader, AppUtils.Tr("timetable.type"), 0, true);
        AddCell(timeHeader, AppUtils.Tr("timetable.start_time"), 1, true);
        AddCell(timeHeader, AppUtils.Tr("timetable.end_time"), 2, true);
        timeStack.Children.Add(timeHeader);

        _timeRows = new StackPanel();
        timeStack.Children.Add(_timeRows);
        timeCard.Child = timeStack;
        panel.Children.Add(timeCard);
        return panel;
    }

    private static void AddCell(Grid grid, string text, int col, bool isHeader)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
            Opacity = isHeader ? 0.75 : 1,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 2, 0),
        };
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    // 右栏

    private Control BuildRightPanel()
    {
        var panel = new StackPanel { Spacing = 8 };

        var configCard = Common.MakeCard();
        var config = new StackPanel { Spacing = 10, Margin = new Thickness(16, 12, 16, 12) };

        // 档案来源
        _sourceCombo = new ComboBox { MinWidth = 150, SelectedIndex = 0 };
        _sourceCombo.Items.Add("Glimpseon");
        _sourceCombo.Items.Add("ClassIsland");
        _sourceCombo.Items.Add("ClassWidgets");
        _sourceCombo.SelectionChanged += (_, _) =>
        {
            var source = _sourceCombo.SelectedIndex switch
            {
                1 => "classisland",
                2 => "classwidgets",
                _ => "Glimpseon",
            };
            Log.Info($"[课表页] 切换档案来源: {source}");
            Config.ProfileSource.Value = source;
            ApplySourceMode(source);
        };
        config.Children.Add(MakeLabeled(AppUtils.Tr("timetable.profile_source"), _sourceCombo));

        config.Children.Add(new TextBlock
        {
            Text = AppUtils.Tr("timetable.profile_config"),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
        });

        // 档案名 管理按钮
        _profileLabel = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var profileHeader = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(_profileLabel, Dock.Left);
        profileHeader.Children.Add(_profileLabel);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        btns.Children.Add(MakeToolButton(FASymbol.Add, "timetable.profile_add", OnAddProfile));
        btns.Children.Add(MakeToolButton(FASymbol.Edit, "timetable.profile_rename", OnRenameProfile));
        btns.Children.Add(MakeToolButton(FASymbol.Delete, "timetable.profile_delete", OnDeleteProfile));
        btns.Children.Add(MakeToolButton(FASymbol.Download, "timetable.profile_import", OnImportProfile));
        btns.Children.Add(MakeToolButton(FASymbol.Save, "timetable.profile_export", OnExportProfile));
        btns.Children.Add(MakeToolButton(FASymbol.OpenFolder, "timetable.profile_open_folder", OnOpenFolder));
        profileHeader.Children.Add(btns);
        config.Children.Add(profileHeader);

        config.Children.Add(new Separator { Margin = new Thickness(0, 2, 0, 2) });

        // 操作按钮行
        _editModeHost = new StackPanel { Spacing = 10 };
        var editHost = _editModeHost;
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        btnRow.Children.Add(MakeSmallButton(AppUtils.Tr("timetable.btn_class"), () => AddPeriod("上课")));
        btnRow.Children.Add(MakeSmallButton(AppUtils.Tr("timetable.btn_break"), () => AddPeriod("课间")));
        btnRow.Children.Add(MakeSmallButton(AppUtils.Tr("timetable.btn_activity"), () => AddPeriod("活动")));
        var delRowButton = MakeSmallButton(AppUtils.Tr("timetable.btn_delete_row"), OnDeleteSelectedRow);
        btnRow.Children.Add(new Panel { Width = 12 });
        btnRow.Children.Add(delRowButton);
        editHost.Children.Add(btnRow);

        // 设置栈
        _emptyState = new StackPanel { Spacing = 8, Margin = new Thickness(0, 16, 0, 16) };
        _emptyState.Children.Add(new TextBlock
        {
            Text = AppUtils.Tr("timetable.empty_state"),
            TextAlignment = TextAlignment.Center,
            FontSize = 14,
            Opacity = 0.6,
        });
        _emptyState.Children.Add(new TextBlock
        {
            Text = AppUtils.Tr("timetable.empty_hint"),
            TextAlignment = TextAlignment.Center,
            FontSize = 12,
            Opacity = 0.45,
            TextWrapping = TextWrapping.Wrap,
        });

        _timeSettings = new StackPanel { Spacing = 6, IsVisible = false };
        _startTimePicker = new TimePicker { HorizontalAlignment = HorizontalAlignment.Stretch };
        _startTimePicker.SelectedTimeChanged += (_, _) => OnPickerChanged();
        _endTimePicker = new TimePicker { HorizontalAlignment = HorizontalAlignment.Stretch };
        _endTimePicker.SelectedTimeChanged += (_, _) => OnPickerChanged();
        _timeSettings.Children.Add(MakeLabeled(AppUtils.Tr("timetable.chs_start_time"), _startTimePicker));
        _timeSettings.Children.Add(MakeLabeled(AppUtils.Tr("timetable.chs_end_time"), _endTimePicker));

        _courseSettings = new StackPanel { Spacing = 6, IsVisible = false };
        _csInfoLabel = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold };
        _courseSettings.Children.Add(_csInfoLabel);

        var subjectFlow = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var subj in SubjectShort)
        {
            var btn = new Button
            {
                Content = subj,
                MinWidth = 34,
                Height = 28,
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = 12,
            };
            var subject = subj;
            btn.Click += (_, _) => OnSubjectButtonClicked(subject);
            subjectFlow.Children.Add(btn);
            _subjectButtons.Add(btn);
        }
        _courseSettings.Children.Add(subjectFlow);

        _csSubjectEdit = new TextBox { Watermark = AppUtils.Tr("timetable.subject_placeholder") };
        _csSubjectEdit.TextChanged += (_, _) => OnCourseSubjectChanged();
        _courseSettings.Children.Add(MakeLabeled(AppUtils.Tr("timetable.custom_subject"), _csSubjectEdit));

        _editModeHost.Children.Add(_emptyState);
        _editModeHost.Children.Add(_timeSettings);
        _editModeHost.Children.Add(_courseSettings);
        config.Children.Add(_editModeHost);

        // 联动只读面板
        _linkagePanel = new StackPanel { Spacing = 8, IsVisible = false };
        _linkagePathLabel = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        var autoDetectButton = MakeSmallButton(AppUtils.Tr("timetable.btn_auto_detect"), OnAutoDetect);
        var openDirButton = MakeSmallButton(AppUtils.Tr("timetable.btn_select_dir"), OnSelectDataDir);
        var linkageBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        linkageBtnRow.Children.Add(autoDetectButton);
        linkageBtnRow.Children.Add(openDirButton);
        _linkagePanel.Children.Add(_linkagePathLabel);
        _linkagePanel.Children.Add(linkageBtnRow);
        _linkagePanel.Children.Add(new TextBlock
        {
            Text = AppUtils.Tr("timetable.linkage_readonly"),
            FontSize = 11,
            Opacity = 0.5,
            TextWrapping = TextWrapping.Wrap,
        });
        _linkageTable = new ListBox { MaxHeight = 420 };
        _linkagePanel.Children.Add(_linkageTable);
        config.Children.Add(_linkagePanel);

        configCard.Child = config;
        panel.Children.Add(configCard);

        // 底部默认时长卡
        var bottomCard = Common.MakeCard();
        var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(16, 8, 16, 8) };
        _classDurSpin = new NumericUpDown { Minimum = 1, Maximum = 120, Value = 40, Width = 120 };
        _classDurSpin.ValueChanged += (_, _) => OnDurationChanged();
        _breakDurSpin = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 10, Width = 120 };
        _breakDurSpin.ValueChanged += (_, _) => OnDurationChanged();
        bottom.Children.Add(MakeRightRow(AppUtils.Tr("timetable.default_class_duration"), _classDurSpin));
        bottom.Children.Add(MakeRightRow(AppUtils.Tr("timetable.default_break_duration"), _breakDurSpin));
        bottomCard.Child = bottom;
        panel.Children.Add(bottomCard);
        return panel;
    }

    private static Button MakeToolButton(FASymbol symbol, string tipKey, Action action)
    {
        var btn = new Button
        {
            Content = new FASymbolIcon { Symbol = symbol, FontSize = 14 },
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(btn, AppUtils.Tr(tipKey));
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

    private static StackPanel MakeLabeled(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = 88,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
        });
        row.Children.Add(control);
        return row;
    }

    private static Grid MakeRightRow(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        host.Children.Add(new TextBlock
        {
            Text = AppUtils.Tr("timetable.minutes"),
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
        });
        host.Children.Add(control);
        Grid.SetColumn(host, 1);
        grid.Children.Add(host);
        return grid;
    }

    // 档案加载/保存

    private void LoadProfile(string name)
    {
        _profileName = name;
        _profile = TimetableProfile.Load(Timetable.GetProfilePath(name));
        Log.Info($"切到档案: {name}");
        _profileLabel.Text = name;
        _blockSave = true;
        _classDurSpin.Value = _profile.DefaultClassDuration;
        _breakDurSpin.Value = _profile.DefaultBreakDuration;
        _blockSave = false;
        RefreshTables();
        SyncTimePickers();
    }

    private void SaveProfile()
    {
        if (_blockSave || _profile is null)
        {
            return;
        }
        _profile.Save();
        Log.Debug($"[课表页] 档案已保存: {_profileName}");
    }

    // 非课间活动行索引
    private static List<int> NonBreakIndices(TimetableProfile profile) =>
        Enumerable.Range(0, profile.Periods.Count)
            .Where(i => PeriodField(profile.Periods[i], "type") is not ("课间" or "活动"))
            .ToList();

    private static string PeriodField(JsonObject p, string key) =>
        p[key]?.GetValue<string>() ?? "";

    // 表格刷新

    private void RefreshTables()
    {
        if (_profile is null)
        {
            return;
        }
        _blockSave = true;
        _courseRows.Children.Clear();
        _courseRowCells.Clear();
        _timeRows.Children.Clear();

        // 课程表行: 时间段 7 天编辑格
        var nb = NonBreakIndices(_profile);
        for (var tableRow = 0; tableRow < nb.Count; tableRow++)
        {
            var periodIdx = nb[tableRow];
            var p = _profile.Periods[periodIdx];
            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("110,*,*,*,*,*,*,*"),
                MinHeight = 36,
            };
            var rowBorder = new Border
            {
                Background = Brushes.Transparent,
                Child = rowGrid,
            };
            var cells = new List<Control>();

            var rangeText = new TextBlock
            {
                Text = $"{PeriodField(p, "start")}~{PeriodField(p, "end")}",
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(rangeText, 0);
            rowGrid.Children.Add(rangeText);
            cells.Add(rangeText);

            var courses = _profile.Courses.TryGetValue(periodIdx.ToString(), out var c) ? c : new JsonObject();
            for (var d = 0; d < 7; d++)
            {
                var text = courses[Days[d]]?.GetValue<string>() ?? "";
                var edit = new TextBox
                {
                    Text = text,
                    FontSize = 12,
                    MinHeight = 30,
                    Padding = new Thickness(4, 2, 4, 2),
                };
                var row = tableRow;
                var col = d + 1;
                edit.GotFocus += (_, _) => SelectCourse(row, col);
                edit.TextChanged += (_, _) => OnCourseCellChanged(row, col, edit.Text ?? "");
                Grid.SetColumn(edit, d + 1);
                rowGrid.Children.Add(edit);
                cells.Add(edit);
            }
            var rowIndex = tableRow;
            rowBorder.PointerPressed += (_, _) => SelectCourse(rowIndex, _selectedCourseCol);
            _courseRows.Children.Add(rowBorder);
            _courseRowCells.Add(cells);
        }

        // 时间表行: 类型/开始/结束 只读
        for (var i = 0; i < _profile.Periods.Count; i++)
        {
            var p = _profile.Periods[i];
            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,110,110"),
                MinHeight = 36,
            };
            AddCell(rowGrid, PeriodField(p, "type"), 0, false);
            AddCell(rowGrid, PeriodField(p, "start"), 1, false);
            AddCell(rowGrid, PeriodField(p, "end"), 2, false);
            var rowBorder = new Border
            {
                Background = Brushes.Transparent,
                Child = rowGrid,
            };
            var row = i;
            rowBorder.PointerPressed += (_, _) => SelectTime(row);
            _timeRows.Children.Add(rowBorder);
        }

        _blockSave = false;

        // 恢复选中
        if (_selectedTimeRow >= 0 && _selectedTimeRow < _profile.Periods.Count)
        {
            SelectTime(_selectedTimeRow);
        }
        else if (_selectedCourseRow >= 0)
        {
            SelectCourse(_selectedCourseRow, _selectedCourseCol);
        }
        else
        {
            ShowEmptyState();
        }
    }

    // 选中与设置面板

    private void ShowEmptyState()
    {
        _activeTable = null;
        _emptyState.IsVisible = true;
        _timeSettings.IsVisible = false;
        _courseSettings.IsVisible = false;
    }

    private void SelectTime(int row)
    {
        if (_profile is null || row >= _profile.Periods.Count)
        {
            ShowEmptyState();
            return;
        }
        _activeTable = "time";
        _selectedTimeRow = row;
        _emptyState.IsVisible = false;
        _timeSettings.IsVisible = true;
        _courseSettings.IsVisible = false;
        PickersFromRow(row);
    }

    private void SelectCourse(int row, int col)
    {
        if (_profile is null)
        {
            return;
        }
        var nb = NonBreakIndices(_profile);
        if (row >= nb.Count)
        {
            ShowEmptyState();
            return;
        }
        _activeTable = "course";
        _selectedCourseRow = row;
        _selectedCourseCol = col < 0 ? 0 : col;
        _emptyState.IsVisible = false;
        _timeSettings.IsVisible = false;
        _courseSettings.IsVisible = true;
        UpdateCourseSettings();
    }

    // 课程设置面板
    private void UpdateCourseSettings()
    {
        if (_profile is null)
        {
            return;
        }
        var nb = NonBreakIndices(_profile);
        if (_selectedCourseRow >= nb.Count)
        {
            return;
        }
        var periodIdx = nb[_selectedCourseRow];
        var p = _profile.Periods[periodIdx];
        var timeRange = $"{PeriodField(p, "start")}~{PeriodField(p, "end")}";

        if (_selectedCourseCol == 0)
        {
            _csInfoLabel.Text = $"第{_selectedCourseRow + 1}节  ·  {timeRange}";
            _blockSave = true;
            _csSubjectEdit.Clear();
            _blockSave = false;
            HighlightSubjectButton(null);
            return;
        }
        var day = AppUtils.Tr($"timetable.{Days[_selectedCourseCol - 1]}");
        _csInfoLabel.Text = $"第{_selectedCourseRow + 1}节  ·  {timeRange}  ·  {day}";
        var courses = _profile.Courses.TryGetValue(periodIdx.ToString(), out var c) ? c : new JsonObject();
        var current = courses[Days[_selectedCourseCol - 1]]?.GetValue<string>() ?? "";
        _blockSave = true;
        _csSubjectEdit.Text = current;
        _blockSave = false;
        HighlightSubjectButton(current);
    }

    // 科目按钮高亮
    private void HighlightSubjectButton(string? subject)
    {
        string? shortName = null;
        if (!string.IsNullOrEmpty(subject))
        {
            foreach (var (short_, full) in SubjectFull)
            {
                if (full == subject)
                {
                    shortName = short_;
                    break;
                }
            }
        }
        foreach (var btn in _subjectButtons)
        {
            var selected = btn.Content as string == shortName;
            btn.Background = selected
                ? new SolidColorBrush(Color.Parse("#30c361"))
                : Brushes.Transparent;
            btn.Foreground = selected ? Brushes.White : Brushes.Gray;
        }
    }

    // 科目按钮点击 写入并跳下一格
    private void OnSubjectButtonClicked(string subject)
    {
        if (_profile is null || _selectedCourseCol <= 0)
        {
            return;
        }
        var fullName = SubjectFull.GetValueOrDefault(subject, subject);
        if (!WriteCourseCell(_selectedCourseRow, _selectedCourseCol, fullName))
        {
            return;
        }
        Log.Debug($"[课表页] 科目按钮: {subject} -> {fullName} @ 行{_selectedCourseRow} 列{_selectedCourseCol}");
        // 跳到当天的下一时间段 录完当天跳下一天
        var nb = NonBreakIndices(_profile);
        var nextRow = _selectedCourseRow + 1;
        var nextCol = _selectedCourseCol;
        if (nextRow >= nb.Count)
        {
            nextRow = 0;
            nextCol++;
            if (nextCol > 7)
            {
                Log.Debug($"[课表页] 科目录入跳转结束: 已到末列");
                RefreshTables();
                return;
            }
        }
        _selectedCourseRow = nextRow;
        _selectedCourseCol = nextCol;
        RefreshTables();
        UpdateCourseSettings();
    }

    // 自定义科目输入
    private void OnCourseSubjectChanged()
    {
        if (_blockSave || _profile is null || _selectedCourseCol <= 0)
        {
            return;
        }
        var text = _csSubjectEdit.Text ?? "";
        if (!WriteCourseCell(_selectedCourseRow, _selectedCourseCol, text))
        {
            return;
        }
        HighlightSubjectButton(text);
    }

    // 单元格编辑
    private void OnCourseCellChanged(int row, int col, string text)
    {
        if (_blockSave || _profile is null || col == 0)
        {
            return;
        }
        if (!WriteCourseCell(row, col, text))
        {
            return;
        }
        if (_activeTable == "course" && row == _selectedCourseRow && col == _selectedCourseCol)
        {
            HighlightSubjectButton(text);
        }
    }

    // 写入课程单元格并保存
    private bool WriteCourseCell(int row, int col, string text)
    {
        if (col == 0 || _profile is null)
        {
            return false;
        }
        var nb = NonBreakIndices(_profile);
        if (row >= nb.Count)
        {
            return false;
        }
        var key = nb[row].ToString();
        if (!_profile.Courses.TryGetValue(key, out var courses))
        {
            courses = new JsonObject();
            _profile.Courses[key] = courses;
        }
        courses[Days[col - 1]] = text;
        SaveProfile();
        return true;
    }

    // 时间选择器
    private void PickersFromRow(int row)
    {
        if (_profile is null || row >= _profile.Periods.Count)
        {
            return;
        }
        _blockPicker = true;
        var p = _profile.Periods[row];
        _startTimePicker.SelectedTime = ParseTime(PeriodField(p, "start"));
        _endTimePicker.SelectedTime = ParseTime(PeriodField(p, "end"));
        _blockPicker = false;
    }

    private void OnPickerChanged()
    {
        if (_blockPicker || _profile is null || _selectedTimeRow < 0 || _selectedTimeRow >= _profile.Periods.Count)
        {
            return;
        }
        var start = _startTimePicker.SelectedTime?.ToString(@"hh\:mm") ?? "08:00";
        var end = _endTimePicker.SelectedTime?.ToString(@"hh\:mm") ?? "08:40";
        _profile.Periods[_selectedTimeRow]["start"] = start;
        _profile.Periods[_selectedTimeRow]["end"] = end;
        Log.Info($"[课表页] 修改时间行: 行{_selectedTimeRow} -> {start}~{end}");
        RefreshTables();
        SaveProfile();
    }

    private static TimeSpan? ParseTime(string s)
    {
        if (TimeOnly.TryParse(s, out var t))
        {
            return t.ToTimeSpan();
        }
        return new TimeSpan(8, 0, 0);
    }

    // 时间选择器同步到下一节开始
    private void SyncTimePickers()
    {
        if (_profile is null)
        {
            return;
        }
        _blockPicker = true;
        var next = _profile.GetNextStartTime();
        var parts = next.Split(':');
        var h = int.Parse(parts[0]);
        var m = int.Parse(parts[1]);
        _startTimePicker.SelectedTime = new TimeSpan(h, m, 0);
        var dur = _profile.DefaultClassDuration;
        var total = h * 60 + m + dur;
        _endTimePicker.SelectedTime = new TimeSpan(total / 60 % 24, total % 60, 0);
        _blockPicker = false;
    }

    // 时段增删

    private void AddPeriod(string periodType)
    {
        if (_profile is null)
        {
            return;
        }
        var start = _startTimePicker.SelectedTime?.ToString(@"hh\:mm") ?? "08:00";
        var end = _endTimePicker.SelectedTime?.ToString(@"hh\:mm") ?? "08:40";
        Log.Info($"[课表页] 添加时段: {periodType} {start}~{end}");
        _profile.AddPeriod(periodType, start, end);
        RefreshTables();
        SaveProfile();
        SyncTimePickers();
    }

    private void OnDeleteSelectedRow()
    {
        if (_profile is null || _profile.Periods.Count == 0)
        {
            return;
        }
        var nb = NonBreakIndices(_profile);
        int periodIdx;
        if (_activeTable == "course" && _selectedCourseRow >= 0)
        {
            if (_selectedCourseRow >= nb.Count)
            {
                return;
            }
            periodIdx = nb[_selectedCourseRow];
        }
        else if (_selectedTimeRow >= 0)
        {
            periodIdx = _selectedTimeRow;
        }
        else
        {
            return;
        }
        if (periodIdx < 0 || periodIdx >= _profile.Periods.Count)
        {
            return;
        }
        Log.Info($"[课表页] 删除时段: 索引={periodIdx}");
        _profile.RemovePeriod(periodIdx);
        RefreshTables();
        SaveProfile();
        SyncTimePickers();
    }

    // 默认时长

    private void OnDurationChanged()
    {
        if (_blockSave || _profile is null)
        {
            return;
        }
        _profile.DefaultClassDuration = (int)(_classDurSpin.Value ?? 40);
        _profile.DefaultBreakDuration = (int)(_breakDurSpin.Value ?? 10);
        SaveProfile();
        SyncTimePickers();
    }

    // 档案管理

    private void OnAddProfile()
    {
        var name = Timetable.NextProfileName();
        var profile = new TimetableProfile { Name = name };
        profile.Save();
        Log.Info($"新建档案: {name}");
        LoadProfile(name);
    }

    private async void OnDeleteProfile()
    {
        var names = Timetable.ListProfiles();
        if (names.Count <= 1)
        {
            if (OwnerWindow is not null)
            {
                await Common.Confirm(OwnerWindow, AppUtils.Tr("timetable.warning_title"),
                    AppUtils.Tr("timetable.cannot_delete_last"));
            }
            return;
        }
        if (OwnerWindow is null)
        {
            return;
        }
        var ok = await Common.Confirm(OwnerWindow, AppUtils.Tr("timetable.confirm_delete_title"),
            AppUtils.Tr("timetable.confirm_delete_body", ("name", _profileName)));
        if (!ok)
        {
            return;
        }
        Timetable.DeleteProfile(_profileName);
        Log.Info($"删除档案: {_profileName}");
        var remaining = Timetable.ListProfiles();
        if (remaining.Count > 0)
        {
            LoadProfile(remaining[^1]);
        }
    }

    private async void OnRenameProfile()
    {
        if (OwnerWindow is null)
        {
            return;
        }
        var newName = await Common.PromptTextInput(OwnerWindow, AppUtils.Tr("timetable.rename_title"), _profileName);
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == _profileName)
        {
            return;
        }
        newName = newName.Trim();
        if (Timetable.ListProfiles().Contains(newName))
        {
            await Common.Confirm(OwnerWindow, AppUtils.Tr("timetable.warning_title"),
                AppUtils.Tr("timetable.name_exists"));
            return;
        }
        Timetable.RenameProfile(_profileName, newName);
        Log.Info($"重命名档案: {_profileName} -> {newName}");
        LoadProfile(newName);
    }

    private async void OnImportProfile()
    {
        if (OwnerWindow is null)
        {
            return;
        }
        var files = await OwnerWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppUtils.Tr("timetable.profile_import"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (files.Count == 0)
        {
            return;
        }
        await ImportProfileFrom(files[0].Path.LocalPath);
    }

    private async void OnExportProfile()
    {
        if (OwnerWindow is null || _profile is null)
        {
            return;
        }
        var file = await OwnerWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = AppUtils.Tr("timetable.profile_export"),
            SuggestedFileName = $"{_profileName}.json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (file is null)
        {
            return;
        }
        File.Copy(Timetable.GetProfilePath(_profileName), file.Path.LocalPath, overwrite: true);
        Log.Info($"导出档案: {_profileName} -> {file.Path.LocalPath}");
    }

    private void OnOpenFolder()
    {
        Directory.CreateDirectory(Paths.DataProfile);
        Process.Start(new ProcessStartInfo { FileName = Paths.DataProfile, UseShellExecute = true });
    }

    private async Task ImportProfileFrom(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (!string.Equals(Path.GetDirectoryName(filePath), Paths.DataProfile, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(filePath, Path.Combine(Paths.DataProfile, Path.GetFileName(filePath)), overwrite: true);
        }
        Log.Info($"导入档案: {name}");
        LoadProfile(name);
        await Task.CompletedTask;
    }

    // 联动模式

    private void ApplySourceMode(string source)
    {
        if (source == "Glimpseon")
        {
            _linkageMode = false;
            _activeBridge = null;
            _ciBridge.Stop();
            _cwBridge.Stop();
            _linkageTimer?.Stop();
            _editModeHost.IsVisible = true;
            _linkagePanel.IsVisible = false;
            _profileLabel.Text = _profileName;
            RefreshTables();
        }
        else
        {
            _linkageMode = true;
            _editModeHost.IsVisible = false;
            _linkagePanel.IsVisible = true;
            if (source == "classisland")
            {
                _activeBridge = _ciBridge;
                _ciBridge.SetDataPath(Config.LinkageDataPath.Value);
                _ciBridge.Start();
                _linkagePathLabel.Text = $"{AppUtils.Tr("timetable.profile_source")}: ClassIsland  {Config.LinkageDataPath.Value}";
            }
            else
            {
                _activeBridge = _cwBridge;
                _cwBridge.SetDataPath(Config.ClassWidgetsDataPath.Value);
                _cwBridge.Start();
                _linkagePathLabel.Text = $"{AppUtils.Tr("timetable.profile_source")}: ClassWidgets  {Config.ClassWidgetsDataPath.Value}";
            }
            _linkageTimer?.Start();
            RefreshLinkageTable();
        }
    }

    // 联动只读周课表刷新
    private void RefreshLinkageTable()
    {
        if (!_linkageMode || _activeBridge is null)
        {
            return;
        }
        try
        {
            var schedule = _activeBridge switch
            {
                LinkageBridge ci => ci.GetTodaySchedule(),
                ClassWidgetsBridge cw => cw.GetTodaySchedule(),
                _ => [],
            };
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
            });
        }
        catch (Exception e)
        {
            Log.Warning($"[课表页] 联动获取今日课表失败: {e.Message}");
        }
    }

    private void OnAutoDetect()
    {
        if (_activeBridge is null)
        {
            return;
        }
        var path = _activeBridge.AutoDetect();
        // 检测失败(空串)时不得覆盖用户已配置的路径 否则联动直接失效
        if (path.Length == 0)
        {
            ShowLinkageTip(AppUtils.Tr("timetable.auto_detect_failed"));
            return;
        }
        ShowLinkageTip(AppUtils.Tr("timetable.auto_detect_success", ("path", path)));
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
        if (OwnerWindow is null)
        {
            return;
        }
        var dirs = await OwnerWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = AppUtils.Tr("timetable.select_data_dir"),
            AllowMultiple = false,
        });
        if (dirs.Count == 0)
        {
            return;
        }
        var dir = dirs[0].Path.LocalPath;
        _activeBridge?.SetDataPath(dir);
        _activeBridge?.Start();
        if (_activeBridge is LinkageBridge)
        {
            Config.LinkageDataPath.Value = dir;
        }
        else
        {
            Config.ClassWidgetsDataPath.Value = dir;
        }
        UpdateLinkagePathLabel();
        Log.Info($"[课表页] 联动数据目录已设置: {dir}");
    }

    private void UpdateLinkagePathLabel()
    {
        var path = _activeBridge is LinkageBridge
            ? Config.LinkageDataPath.Value
            : Config.ClassWidgetsDataPath.Value;
        _linkagePathLabel.Text = $"{AppUtils.Tr("timetable.profile_source")}: " +
                                 $"{(_activeBridge is LinkageBridge ? "ClassIsland" : "ClassWidgets")}  {path}";
    }

    private void ShowLinkageTip(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _linkagePathLabel.Text = message;
        });
    }
}
