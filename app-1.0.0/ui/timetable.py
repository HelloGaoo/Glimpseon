"""
课程表
"""

import logging
import os
import shutil
import datetime as _dt
from datetime import time as _time

from PyQt6.QtCore import Qt, QTime, QTimer, pyqtSignal
from PyQt6.QtGui import QFontMetrics
from PyQt6.QtWidgets import (
    QFileDialog,
    QHBoxLayout,
    QHeaderView,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
)
from qfluentwidgets import (
    BodyLabel,
    CardWidget,
    ComboBox,
    FlowLayout,
    InfoBar,
    InfoBarPosition,
    LineEdit,
    MessageBox,
    PushButton,
    ScrollArea,
    SpinBox,
    StrongBodyLabel,
    TableWidget,
    TimePicker,
    ToolButton,
)

from core.config import cfg
from core.constants import load_qss, TIMETABLE_SOURCES
from core.linkage import LinkageBridge, ClassWidgetsBridge
from core.timetable import (
    TimetableProfile,
    delete_profile,
    ensure_default_profile,
    get_profile_path,
    list_profiles,
    next_profile_name,
    PROFILES_DIR,
    rename_profile,
)
from core.utils import tr, TranslatableWidget, FUI

DAYS = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"]

logger = logging.getLogger("Glimpseon.ui.timetable")
SUBJECT_SHORT = ["语", "数", "英", "政", "史", "地", "生", "物", "化", "科", "通", "劳", "班", "社", "心", "信", "考", "体", "早", "自"]
SUBJECT_FULL = {
    "语": "语文", "数": "数学", "英": "英语", "政": "政治", "史": "历史",
    "地": "地理", "生": "生物", "物": "物理", "化": "化学", "科": "科学", "通": "通用技术", "劳": "劳动",
    "班": "班会", "社": "社团", "心": "心理", "信": "信息技术", "考": "考试", "体": "体育",
    "早": "早读", "自": "自习"
}


class TimetablePage(ScrollArea, TranslatableWidget):
    """课程表页面"""

    scheduleChanged = pyqtSignal()

    def __init__(self, parent=None):
        super().__init__(parent=parent)
        self.setObjectName("timetable")

        self._profile = None
        self._current_profile_name = ""
        self._block_save = False
        self._block_picker_change = False
        self._activeTable = None  # None | "time" | "course"
        self._block_course_edit = False

        # 联动 Bridge
        self._ciBridge = LinkageBridge(self)
        self._cwBridge = ClassWidgetsBridge(self)
        self._activeBridge = None
        self._linkageMode = False  # 是否联动模式

        # 布局
        self.scrollWidget = QWidget()
        self.scrollWidget.setObjectName("scrollWidget")
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)

        self.viewport().setAutoFillBackground(False)
        self.scrollWidget.setAutoFillBackground(False)

        self.mainLayout = QVBoxLayout(self.scrollWidget)
        self.mainLayout.setContentsMargins(30, 8, 30, 8)
        self.mainLayout.setSpacing(6)

        # 标题
        self.titleLabel = BodyLabel(tr("navigation.timetable"))
        self.titleLabel.setObjectName("timetableTitle")
        self.mainLayout.addWidget(self.titleLabel)

        # 左右分栏
        split_layout = QHBoxLayout()
        split_layout.setSpacing(12)

        # 左侧
        left_widget = QWidget()
        left_widget.setObjectName("timetableLeft")
        left_layout = QVBoxLayout(left_widget)
        left_layout.setContentsMargins(0, 0, 0, 0)
        left_layout.setSpacing(8)

        # 课程表
        course_card = CardWidget(left_widget)
        course_card.setObjectName("courseCard")
        course_layout = QVBoxLayout(course_card)
        course_layout.setContentsMargins(8, 4, 8, 4)
        course_layout.setSpacing(2)

        course_title = BodyLabel(tr("timetable.course_table"))
        course_layout.addWidget(course_title)

        self.courseTable = TableWidget(course_card)
        self.courseTable.setObjectName("courseTable")
        self.courseTable.setColumnCount(8)
        self.courseTable.setHorizontalHeaderLabels([
            tr("timetable.time_range"),
            tr("timetable.monday"),
            tr("timetable.tuesday"),
            tr("timetable.wednesday"),
            tr("timetable.thursday"),
            tr("timetable.friday"),
            tr("timetable.saturday"),
            tr("timetable.sunday"),
        ])
        self.courseTable.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.courseTable.verticalHeader().setDefaultSectionSize(36)
        self.courseTable.verticalHeader().setMinimumSectionSize(28)
        self.courseTable.setSelectionMode(TableWidget.SelectionMode.SingleSelection)
        self.courseTable.setSelectionBehavior(TableWidget.SelectionBehavior.SelectItems)
        self.courseTable.setShowGrid(True)
        self.courseTable.cellChanged.connect(self._onCourseCellChanged)
        self.courseTable.itemSelectionChanged.connect(self._onCourseTableSelection)
        course_layout.addWidget(self.courseTable)

        left_layout.addWidget(course_card, stretch=1)

        # 时间表
        time_card = CardWidget(left_widget)
        time_card.setObjectName("timeCard")
        time_layout = QVBoxLayout(time_card)
        time_layout.setContentsMargins(8, 4, 8, 4)
        time_layout.setSpacing(2)

        time_title = BodyLabel(tr("timetable.time_schedule"))
        time_layout.addWidget(time_title)

        self.timeTable = TableWidget(time_card)
        self.timeTable.setObjectName("timeTable")
        self.timeTable.setColumnCount(3)
        self.timeTable.setHorizontalHeaderLabels([
            tr("timetable.type"),
            tr("timetable.start_time"),
            tr("timetable.end_time"),
        ])
        self.timeTable.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.timeTable.verticalHeader().setDefaultSectionSize(36)
        self.timeTable.verticalHeader().setMinimumSectionSize(28)
        self.timeTable.setEditTriggers(TableWidget.EditTrigger.NoEditTriggers)
        self.timeTable.setSelectionMode(TableWidget.SelectionMode.SingleSelection)
        self.timeTable.setSelectionBehavior(TableWidget.SelectionBehavior.SelectRows)
        self.timeTable.setShowGrid(True)
        self.timeTable.itemSelectionChanged.connect(self._onTimeTableSelection)
        time_layout.addWidget(self.timeTable)

        left_layout.addWidget(time_card, stretch=1)

        # 右侧
        right_widget = QWidget()
        right_widget.setObjectName("timetableRight")
        right_layout = QVBoxLayout(right_widget)
        right_layout.setContentsMargins(0, 0, 0, 0)
        right_layout.setSpacing(0)

        config_card = CardWidget(right_widget)
        config_card.setObjectName("configCard")
        self._config_layout = QVBoxLayout(config_card)
        self._config_layout.setContentsMargins(16, 12, 16, 12)
        self._config_layout.setSpacing(10)

        # 档案来源选择
        source_row = QWidget()
        source_row.setObjectName("sourceRow")
        sr_layout = QHBoxLayout(source_row)
        sr_layout.setContentsMargins(0, 0, 0, 0)
        sr_layout.setSpacing(8)
        source_label = BodyLabel(tr("timetable.profile_source"))
        self._sourceCombo = ComboBox()
        self._sourceCombo.setObjectName("sourceCombo")
        self._sourceCombo.addItems(TIMETABLE_SOURCES)
        self._sourceCombo.currentIndexChanged.connect(self._onSourceChanged)
        sr_layout.addWidget(source_label)
        sr_layout.addWidget(self._sourceCombo, stretch=1)
        self._config_layout.insertWidget(0, source_row)

        # 档案配置标题
        config_title = BodyLabel(tr("timetable.profile_config"))
        config_title.setObjectName("configTitle")
        self._config_layout.addWidget(config_title)

        # 档案名称 管理按钮
        profile_header = QWidget()
        profile_header.setObjectName("profileHeader")
        ph_layout = QHBoxLayout(profile_header)
        ph_layout.setContentsMargins(0, 0, 0, 0)
        ph_layout.setSpacing(6)

        self._profileLabel = StrongBodyLabel("")
        self._profileLabel.setObjectName("profileNameLabel")
        ph_layout.addWidget(self._profileLabel, stretch=1)

        self._addBtn = ToolButton(FUI.ADD, config_card)
        self._addBtn.setToolTip(tr("timetable.profile_add"))
        self._addBtn.clicked.connect(self._onAddProfile)
        ph_layout.addWidget(self._addBtn)

        self._renameBtn = ToolButton(FUI.EDIT, config_card)
        self._renameBtn.setToolTip(tr("timetable.profile_rename"))
        self._renameBtn.clicked.connect(self._onRenameProfile)
        ph_layout.addWidget(self._renameBtn)

        self._delBtn = ToolButton(FUI.DELETE, config_card)
        self._delBtn.setToolTip(tr("timetable.profile_delete"))
        self._delBtn.clicked.connect(self._onDeleteProfile)
        ph_layout.addWidget(self._delBtn)

        self._importBtn = ToolButton(FUI.DOWNLOAD, config_card)
        self._importBtn.setToolTip(tr("timetable.profile_import"))
        self._importBtn.clicked.connect(self._onImportProfile)
        ph_layout.addWidget(self._importBtn)

        self._exportBtn = ToolButton(FUI.SAVE, config_card)
        self._exportBtn.setToolTip(tr("timetable.profile_export"))
        self._exportBtn.clicked.connect(self._onExportProfile)
        ph_layout.addWidget(self._exportBtn)

        self._openFolderBtn = ToolButton(FUI.FOLDER, config_card)
        self._openFolderBtn.setToolTip(tr("timetable.profile_open_folder"))
        self._openFolderBtn.clicked.connect(self._onOpenFolder)
        ph_layout.addWidget(self._openFolderBtn)

        self._config_layout.addWidget(profile_header)

        # 分隔线
        sep1 = CardWidget(config_card)
        sep1.setObjectName("profileSeparator")
        sep1.setFixedHeight(1)
        self._config_layout.addWidget(sep1)

        # 操作按钮区
        action_section = QWidget()
        action_section.setObjectName("actionSection")
        act_layout = QVBoxLayout(action_section)
        act_layout.setContentsMargins(0, 4, 0, 4)
        act_layout.setSpacing(6)

        btn_row = QHBoxLayout()
        btn_row.setSpacing(6)
        self._btnClass = PushButton(tr("timetable.btn_class"))
        self._btnClass.clicked.connect(lambda: self._addPeriod("上课"))
        self._btnBreak = PushButton(tr("timetable.btn_break"))
        self._btnBreak.clicked.connect(lambda: self._addPeriod("课间"))
        self._btnActivity = PushButton(tr("timetable.btn_activity"))
        self._btnActivity.clicked.connect(lambda: self._addPeriod("活动"))
        self._btnDeleteRow = PushButton(tr("timetable.btn_delete_row"))
        self._btnDeleteRow.clicked.connect(self._onDeleteSelectedRow)
        btn_row.addWidget(self._btnClass)
        btn_row.addWidget(self._btnBreak)
        btn_row.addWidget(self._btnActivity)
        btn_row.addStretch()
        btn_row.addWidget(self._btnDeleteRow)
        act_layout.addLayout(btn_row)

        self._config_layout.addWidget(action_section)

        # 分隔线
        self._settingsSep1 = CardWidget(config_card)
        self._settingsSep1.setObjectName("profileSeparator")
        self._settingsSep1.setFixedHeight(1)
        self._config_layout.addWidget(self._settingsSep1)

        # 设置区
        self._settingsStack = QWidget()
        self._settingsStack.setObjectName("settingsStack")
        ss_layout = QVBoxLayout(self._settingsStack)
        ss_layout.setContentsMargins(0, 0, 0, 0)
        ss_layout.setSpacing(0)

        # 空状态占位
        self._emptyState = QWidget()
        self._emptyState.setObjectName("emptyState")
        empty_layout = QVBoxLayout(self._emptyState)
        empty_layout.setContentsMargins(0, 16, 0, 16)
        empty_layout.setSpacing(8)
        empty_icon = BodyLabel(None)
        empty_icon.setObjectName("emptyIcon")
        empty_icon.setAlignment(Qt.AlignmentFlag.AlignCenter)
        empty_layout.addWidget(empty_icon)
        empty_text = BodyLabel(tr("timetable.empty_state"))
        empty_text.setObjectName("emptyText")
        empty_text.setAlignment(Qt.AlignmentFlag.AlignCenter)
        empty_layout.addWidget(empty_text)
        empty_hint = BodyLabel(tr("timetable.empty_hint"))
        empty_hint.setObjectName("emptyHint")
        empty_hint.setAlignment(Qt.AlignmentFlag.AlignCenter)
        empty_layout.addWidget(empty_hint)
        empty_layout.addStretch()
        ss_layout.addWidget(self._emptyState)

        # 时间表设置
        self._timeSettings = QWidget()
        self._timeSettings.setObjectName("timeSettings")
        self._timeSettings.hide()
        ts_layout = QVBoxLayout(self._timeSettings)
        ts_layout.setContentsMargins(0, 0, 0, 0)
        ts_layout.setSpacing(6)

        start_row = QHBoxLayout()
        start_row.setSpacing(8)
        start_label = BodyLabel(tr("timetable.chs_start_time"))
        self._startTimePicker = TimePicker()
        self._startTimePicker.setObjectName("startTimePicker")
        self._startTimePicker.timeChanged.connect(self._onPickerChanged)
        start_row.addWidget(start_label)
        start_row.addWidget(self._startTimePicker, stretch=1)
        ts_layout.addLayout(start_row)

        end_row = QHBoxLayout()
        end_row.setSpacing(8)
        end_label = BodyLabel(tr("timetable.chs_end_time"))
        self._endTimePicker = TimePicker()
        self._endTimePicker.setObjectName("endTimePicker")
        self._endTimePicker.timeChanged.connect(self._onPickerChanged)
        end_row.addWidget(end_label)
        end_row.addWidget(self._endTimePicker, stretch=1)
        ts_layout.addLayout(end_row)

        ss_layout.addWidget(self._timeSettings)

        # 课程表设置
        self._courseSettings = QWidget()
        self._courseSettings.setObjectName("courseSettings")
        self._courseSettings.hide()
        cs_layout = QVBoxLayout(self._courseSettings)
        cs_layout.setContentsMargins(0, 0, 0, 0)
        cs_layout.setSpacing(6)

        # 时间段 星期 
        self._csInfoLabel = StrongBodyLabel("")
        self._csInfoLabel.setObjectName("csInfoLabel")
        cs_layout.addWidget(self._csInfoLabel)

        # 科目选择按钮
        self._subjectBtnWidget = QWidget()
        self._subjectBtnWidget.setObjectName("subjectBtnWidget")
        flow_layout = FlowLayout(self._subjectBtnWidget, needAni=False)
        flow_layout.setContentsMargins(0, 0, 0, 0)
        self._subjectBtns = []
        for subj in SUBJECT_SHORT:
            btn = PushButton(subj)
            btn.setObjectName("subjectBtn")
            btn.clicked.connect(lambda checked, s=subj: self._onSubjectBtnClicked(s))
            flow_layout.addWidget(btn)
            self._subjectBtns.append(btn)
        cs_layout.addWidget(self._subjectBtnWidget)

        # 自定义科目输入
        custom_row = QHBoxLayout()
        custom_row.setSpacing(8)
        custom_label = BodyLabel(tr("timetable.custom_subject"))
        self._csSubjectEdit = LineEdit()
        self._csSubjectEdit.setObjectName("csSubjectEdit")
        self._csSubjectEdit.setPlaceholderText(tr("timetable.subject_placeholder"))
        self._csSubjectEdit.textChanged.connect(self._onCourseSubjectChanged)
        custom_row.addWidget(custom_label)
        custom_row.addWidget(self._csSubjectEdit, stretch=1)
        cs_layout.addLayout(custom_row)

        ss_layout.addWidget(self._courseSettings)
        self._config_layout.addWidget(self._settingsStack)
        self._config_layout.addStretch()

        right_layout.addWidget(config_card, stretch=1)

        # 默认时间配置项
        bottom_card = CardWidget(right_widget)
        bottom_card.setObjectName("bottomCard")
        bottom_layout = QVBoxLayout(bottom_card)
        bottom_layout.setContentsMargins(16, 8, 16, 8)
        bottom_layout.setSpacing(6)

        # 默认上课时间
        class_dur_row = QHBoxLayout()
        class_dur_row.setSpacing(8)
        self._classDurSpin = SpinBox()
        self._classDurSpin.setRange(1, 120)
        self._classDurSpin.setValue(40)
        self._classDurSpin.setSuffix(tr("timetable.minutes"))
        self._classDurSpin.valueChanged.connect(self._onDurationChanged)
        class_dur_row.addWidget(BodyLabel(tr("timetable.default_class_duration")))
        class_dur_row.addStretch()
        class_dur_row.addWidget(self._classDurSpin)
        bottom_layout.addLayout(class_dur_row)

        # 默认课间时间
        break_dur_row = QHBoxLayout()
        break_dur_row.setSpacing(8)
        self._breakDurSpin = SpinBox()
        self._breakDurSpin.setRange(1, 60)
        self._breakDurSpin.setValue(10)
        self._breakDurSpin.setSuffix(tr("timetable.minutes"))
        self._breakDurSpin.valueChanged.connect(self._onDurationChanged)
        break_dur_row.addWidget(BodyLabel(tr("timetable.default_break_duration")))
        break_dur_row.addStretch()
        break_dur_row.addWidget(self._breakDurSpin)
        bottom_layout.addLayout(break_dur_row)

        right_layout.addWidget(bottom_card)

        # 组装分栏
        split_layout.addWidget(left_widget, 7)
        split_layout.addWidget(right_widget, 3)
        self.mainLayout.addLayout(split_layout, stretch=1)

        self.setStyleSheet(load_qss("timetable.qss"))

        self._ciBridge.stateChanged.connect(self._onLinkageStateChanged)
        self._ciBridge.connectedChanged.connect(self._onLinkageConnectedChanged)
        self._ciBridge.errorOccurred.connect(self._onLinkageError)
        self._cwBridge.stateChanged.connect(self._onLinkageStateChanged)
        self._cwBridge.connectedChanged.connect(self._onLinkageConnectedChanged)
        self._cwBridge.errorOccurred.connect(self._onLinkageError)

        self._linkageRefreshTimer = QTimer(self)
        self._linkageRefreshTimer.setInterval(1000)
        self._linkageRefreshTimer.timeout.connect(self._onLinkageRefreshTimer)

        self._scheduleChangedTimer = QTimer(self)
        self._scheduleChangedTimer.setSingleShot(True)
        self._scheduleChangedTimer.setInterval(50)
        self._scheduleChangedTimer.timeout.connect(self.scheduleChanged.emit)

        current_source = cfg.profileSource.value
        self._loadProfile(ensure_default_profile())
        if current_source != "Glimpseon":
            self._sourceCombo.blockSignals(True)
            self._sourceCombo.setCurrentIndex(
                {"classisland": 1, "classwidgets": 2}.get(current_source, 0)
            )
            self._sourceCombo.blockSignals(False)
            self._applySourceMode(current_source)

        logger.debug(f"[TimetablePage] 就绪 档案={self._current_profile_name}时段={self._profile.period_count()} 源={current_source}")

    def _loadProfile(self, name: str):
        """加载档案"""
        self._current_profile_name = name
        path = get_profile_path(name)
        self._profile = TimetableProfile.load(path)
        logger.info(f"切到档案: {name}")
        logger.debug(f"[TimetablePage] 档案已加载 {self._profile.period_count()} "
                     f"上课{self._profile.default_class_duration}min/课间{self._profile.default_break_duration}min")
        self._profileLabel.setText(name)
        self._block_save = True
        self._classDurSpin.setValue(self._profile.default_class_duration)
        self._breakDurSpin.setValue(self._profile.default_break_duration)
        self._block_save = False
        self._refreshTables()
        self._syncTimePickers()

    def _saveProfile(self):
        """保存档案"""
        if self._block_save or self._profile is None:
            logger.debug(f"[TimetablePage] 档案保存 暂存锁={self._block_save} 档案缺失={self._profile is None}")
            return
        self._profile.save()
        logger.debug(f"[TimetablePage] 档案已保存: {self._current_profile_name}")
        self._scheduleChangedTimer.start()


    @staticmethod
    def _nonBreakIndices(periods):
        """返回课程"""
        return [i for i, p in enumerate(periods) if p["type"] not in ("课间", "活动")]

    def _refreshTables(self):
        """刷新两个表格"""
        self._block_save = True

        # 保存当前选中状态
        saved_time_row = -1
        saved_course_row = -1
        saved_course_col = -1
        time_sel = self.timeTable.selectedItems()
        if time_sel:
            saved_time_row = time_sel[0].row()
        course_sel = self.courseTable.selectedItems()
        if course_sel:
            saved_course_row = course_sel[0].row()
            saved_course_col = course_sel[0].column()

        # 时间表
        self.timeTable.setRowCount(0)
        self.timeTable.setRowCount(self._profile.period_count())
        for i, p in enumerate(self._profile.periods):
            self._setTimeRow(i, p)

        # 课程表
        nb_indices = self._nonBreakIndices(self._profile.periods)
        self.courseTable.setRowCount(0)
        self.courseTable.setRowCount(len(nb_indices))
        for table_row, period_idx in enumerate(nb_indices):
            self._setCourseRow(table_row, period_idx)

        # 恢复选中状态
        if saved_time_row >= 0 and saved_time_row < self._profile.period_count():
            self.timeTable.selectRow(saved_time_row)
        if saved_course_row >= 0 and saved_course_row < len(nb_indices):
            self.courseTable.setCurrentCell(saved_course_row, saved_course_col)

        self._block_save = False
        logger.debug(f"[TimetablePage] 表格已刷新 时间行={self._profile.period_count()} 课程行={len(nb_indices)}")
        self._scheduleChangedTimer.start()

    def _setTimeRow(self, row: int, period: dict):
        """设置时间表的某一行"""
        start = period.get("start", "")
        end = period.get("end", "")
        ptype = period.get("type", "")
        logger.debug(f"[TimetablePage] 填充时间行 {row}: {ptype} {start}~{end}")

        # 第0列类型
        type_item = QTableWidgetItem(ptype)
        type_item.setFlags(type_item.flags() & ~Qt.ItemFlag.ItemIsEditable)
        self.timeTable.setItem(row, 0, type_item)

        # 第1列开始时间
        start_item = QTableWidgetItem(start)
        start_item.setFlags(start_item.flags() & ~Qt.ItemFlag.ItemIsEditable)
        self.timeTable.setItem(row, 1, start_item)

        # 第2列结束时间
        end_item = QTableWidgetItem(end)
        end_item.setFlags(end_item.flags() & ~Qt.ItemFlag.ItemIsEditable)
        self.timeTable.setItem(row, 2, end_item)

    def _setCourseRow(self, table_row: int, period_idx: int):
        """设置课程表的某一行"""
        p = self._profile.periods[period_idx]
        start = p.get("start", "")
        end = p.get("end", "")

        # 第0列时间段
        range_item = QTableWidgetItem(f"{start}~{end}")
        range_item.setFlags(range_item.flags() & ~Qt.ItemFlag.ItemIsEditable)
        self.courseTable.setItem(table_row, 0, range_item)

        courses = self._profile.courses.get(str(period_idx), {})
        for col, day in enumerate(DAYS, start=1):
            text = courses.get(day, "")
            item = QTableWidgetItem(text)
            self.courseTable.setItem(table_row, col, item)
        logger.debug(f"[TimetablePage] 填充课程行 {table_row}: 时段{period_idx} '{start}~{end}'")


    def _addPeriod(self, period_type: str):
        """添加一个时间段"""
        if self._profile is None:
            logger.debug("[TimetablePage] 添加时段取消: 无档案")
            return

        start_qtime = self._startTimePicker.time
        start = f"{start_qtime.hour():02d}:{start_qtime.minute():02d}"
        end_qtime = self._endTimePicker.time
        end = f"{end_qtime.hour():02d}:{end_qtime.minute():02d}"

        logger.info(f"[TimetablePage] 添加时段: {period_type} {start}~{end}")
        self._profile.add_period(period_type, start, end)
        self._refreshTables()
        self._saveProfile()
        self._syncTimePickers()

    def _onDeleteSelectedRow(self):
        """删除选中的行"""
        if self._profile is None or self._profile.period_count() == 0:
            return

        # 要删除的 periods 和来源表格
        period_idx = -1
        source_table = None  # "time" or "course"
        original_row = -1

        sel_time = self.timeTable.selectedItems()
        sel_course = self.courseTable.selectedItems()
        nb = self._nonBreakIndices(self._profile.periods)
        if self._activeTable == "course" and sel_course:
            source_table = "course"
            original_row = sel_course[0].row()
            if original_row < len(nb):
                period_idx = nb[original_row]
        elif sel_time:
            source_table = "time"
            original_row = sel_time[0].row()
            period_idx = original_row
        elif sel_course:
            source_table = "course"
            original_row = sel_course[0].row()
            if original_row < len(nb):
                period_idx = nb[original_row]

        if period_idx < 0 or period_idx >= self._profile.period_count():
            logger.debug(f"[TimetablePage] 删除取消: 无有效选中行 (period_idx={period_idx})")
            return

        # 删除
        logger.info(f"[TimetablePage] 删除时段: 索引={period_idx} 来源表={source_table}行={original_row}")
        self._profile.remove_period(period_idx)
        self._refreshTables()
        self._saveProfile()
        self._syncTimePickers()

        # 选中下一行 行数量纲按来源表取(课程表按非课间行数)
        if source_table == "time":
            total_rows = self._profile.period_count()
            next_row = original_row if original_row < total_rows - 1 else total_rows - 2
            if next_row >= 0:
                self.timeTable.selectRow(next_row)
        elif source_table == "course":
            nb = self._nonBreakIndices(self._profile.periods)
            next_row = original_row if original_row < len(nb) - 1 else len(nb) - 2
            if 0 <= next_row < len(nb):
                self.courseTable.setCurrentCell(next_row, 1)


    def _syncTimePickers(self):
        """时间选择器更新"""
        if self._profile is None:
            logger.debug("[TimetablePage] 时间选择器同步 无档案")
            return
        self._block_picker_change = True
        nxt = self._profile.get_next_start_time()
        h, m = map(int, nxt.split(":"))
        self._startTimePicker.setTime(QTime(h, m))

        dur = self._profile.default_class_duration
        total = h * 60 + m + dur
        end_h, end_m = total // 60, total % 60
        self._endTimePicker.setTime(QTime(end_h % 24, end_m))
        logger.debug(f"[TimetablePage] 时间选择器同步 下一节 {nxt} 结束 {end_h % 24:02d}:{end_m:02d} 时长{dur}min")
        self._block_picker_change = False


    def _onCourseCellChanged(self, row: int, col: int):
        """保存课程表单元格变更"""
        if self._block_save or self._profile is None:
            return
        item = self.courseTable.item(row, col)
        text = item.text() if item else ""
        logger.debug(f"[TimetablePage] 单元格编辑: 行{row} 列{col} -> '{text[:20]}'")
        if not self._write_course_cell(row, col, text):
            return
        # 同步到右侧面板
        if self._activeTable == "course":
            self._updateCourseSettings()

    def _write_course_cell(self, row: int, col: int, text: str) -> bool:
        """写入课程表单元格并保存"""
        if col == 0 or self._profile is None:  # 时间段列不可编辑
            logger.debug(f"[TimetablePage] 课程单元格写入拒绝: 列={col} 有档案={self._profile is not None}")
            return False
        nb = self._nonBreakIndices(self._profile.periods)
        if row >= len(nb):
            logger.debug(f"[TimetablePage] 课程单元格写入拒绝: 行越界 row={row} 非课间行数={len(nb)}")
            return False
        self._profile.courses.setdefault(str(nb[row]), {})[DAYS[col - 1]] = text
        self._saveProfile()
        return True

    # 右侧专栏在选中后改变显示内容
    def _onTimeTableSelection(self):
        """时间表选中"""
        sel = self.timeTable.selectedItems()
        if not sel or self._profile is None or self._profile.period_count() == 0:
            logger.debug("[TimetablePage] 时间表选中为空 显示空状态")
            self._activeTable = None
            self._emptyState.show()
            self._timeSettings.hide()
            self._courseSettings.hide()
            return
        self._activeTable = "time"
        logger.debug(f"[TimetablePage] 时间表选中行={sel[0].row()} 类型={sel[0].text()}")
        self._emptyState.hide()
        self._timeSettings.show()
        self._courseSettings.hide()
        self._pickersFromRow(sel[0].row())

    def _onCourseTableSelection(self):
        """课程表选中"""
        sel = self.courseTable.selectedItems()
        if not sel or self._profile is None or self._profile.period_count() == 0:
            logger.debug("[TimetablePage] 课程表选中为空 显示空状态")
            self._activeTable = None
            self._emptyState.show()
            self._timeSettings.hide()
            self._courseSettings.hide()
            return
        self._activeTable = "course"
        logger.debug(f"[TimetablePage] 课程表选中: 行={sel[0].row()} 列={sel[0].column()}")
        self._emptyState.hide()
        self._timeSettings.hide()
        self._courseSettings.show()
        QTimer.singleShot(0, self._updateCourseSettings)

    def _updateCourseSettings(self):
        """更新课程设置面板内容"""
        if self._profile is None:
            return
        sel = self.courseTable.selectedItems()
        if not sel:
            logger.debug("[TimetablePage] 课程面板清空: 无选中单元格")
            self._csInfoLabel.setText("")
            self._block_course_edit = True
            self._csSubjectEdit.clear()
            self._block_course_edit = False
            self._highlightSubjectBtn(None)
            return
        row, col = sel[0].row(), sel[0].column()
        nb = self._nonBreakIndices(self._profile.periods)
        if row >= len(nb):
            logger.debug(f"[TimetablePage] 课程面板 行越界 row={row} 非课间行数={len(nb)}")
            return
        period_idx = nb[row]
        p = self._profile.periods[period_idx]
        time_range = f"{p['start']}~{p['end']}"

        if col == 0:
            logger.debug(f"[TimetablePage] 课程面板显示时间段: 行{row} {time_range}")
            self._csInfoLabel.setText(f"第{row + 1}节  ·  {time_range}")
            self._block_course_edit = True
            self._csSubjectEdit.clear()
            self._block_course_edit = False
            self._highlightSubjectBtn(None)
            return
        day = tr(f"timetable.{DAYS[col - 1]}")
        self._csInfoLabel.setText(f"第{row + 1}节  ·  {time_range}  ·  {day}")
        courses = self._profile.courses.get(str(period_idx), {})
        current = courses.get(DAYS[col - 1], "")
        logger.debug(f"[TimetablePage] 课程面板更新: 行{row} 列{col} {day} {time_range} 当前='{current[:20]}'")
        self._block_course_edit = True
        self._csSubjectEdit.setText(current)
        self._block_course_edit = False
        self._highlightSubjectBtn(current)

    def _highlightSubjectBtn(self, subject: str | None):
        """高亮选中的科目按钮"""
        short_name = None
        if subject:
            for short, full in SUBJECT_FULL.items():
                if full == subject:
                    short_name = short
                    break
        for btn in self._subjectBtns:
            if btn.text() == short_name:
                btn.setProperty("selected", True)
            else:
                btn.setProperty("selected", False)
            btn.style().unpolish(btn)
            btn.style().polish(btn)
        logger.debug(f"[TimetablePage] 科目按钮高亮: {short_name or '(无)'}")

    def _onSubjectBtnClicked(self, subject: str):
        """科目按钮点击设置"""
        if self._profile is None:
            return
        sel = self.courseTable.selectedItems()
        if not sel:
            return
        row, col = sel[0].row(), sel[0].column()
        nb = self._nonBreakIndices(self._profile.periods)

        # 录入科目
        full_name = SUBJECT_FULL.get(subject, subject)
        if not self._write_course_cell(row, col, full_name):
            return

        # 同步到表格
        self._block_save = True
        logger.debug(f"[TimetablePage] 科目按钮: {subject} -> {full_name} @ 行{row} 列{col}")
        item = self.courseTable.item(row, col)
        if item:
            item.setText(full_name)
        self._block_save = False

        # 跳到当天的下一时间段 录完当天跳下一天
        next_row = row + 1
        next_col = col
        if next_row >= len(nb):
            next_row = 0
            next_col = col + 1
            if next_col > 7:
                logger.debug(f"[TimetablePage] 科目录入跳转结束: 已到末列 col={col}")
                return
        # 选中下一格(setCurrentCell 自身会触发右侧面板刷新)
        self.courseTable.setCurrentCell(next_row, next_col)

    def _onCourseSubjectChanged(self, text: str):
        """课程设置的科目输入变更"""
        if self._block_course_edit or self._profile is None:
            return
        sel = self.courseTable.selectedItems()
        if not sel:
            return
        row, col = sel[0].row(), sel[0].column()

        # 更新 profile
        logger.debug(f"[TimetablePage] 科目输入: '{text}' @ 行{row} 列{col}")
        if not self._write_course_cell(row, col, text):
            return

        # 同步到表格
        self._block_save = True
        item = self.courseTable.item(row, col)
        if item:
            item.setText(text)
        self._block_save = False

        # 更新高亮
        self._highlightSubjectBtn(text)


    def _pickersFromRow(self, time_row: int):
        """选中的时间段更改到右侧设置时间组件"""
        if self._profile is None or time_row >= self._profile.period_count():
            return
        self._block_picker_change = True
        p = self._profile.periods[time_row]
        h, m = map(int, p["start"].split(":"))
        self._startTimePicker.setTime(QTime(h, m))
        h, m = map(int, p["end"].split(":"))
        self._endTimePicker.setTime(QTime(h, m))
        logger.debug(f"[TimetablePage] 选择器载入时间行 {time_row}: {p['start']}~{p['end']}")
        self._block_picker_change = False

    def _onPickerChanged(self, _):
        """时间选择器变更"""
        if self._block_picker_change or self._profile is None:
            return
        sel = self.timeTable.selectedItems()
        if not sel:
            return
        row = sel[0].row()
        if row >= self._profile.period_count():
            logger.debug(f"[TimetablePage] 时间选择器变更 行越界 row={row}")
            return

        qs = self._startTimePicker.time
        qe = self._endTimePicker.time
        start = f"{qs.hour():02d}:{qs.minute():02d}"
        end = f"{qe.hour():02d}:{qe.minute():02d}"

        self._profile.periods[row]["start"] = start
        self._profile.periods[row]["end"] = end
        logger.info(f"[TimetablePage] 修改时间行: 行{row} -> {start}~{end}")
        self._refreshTables()
        self._saveProfile()


    def _onAddProfile(self):
        """添加档案"""
        name = next_profile_name()
        profile = TimetableProfile(name)
        profile.save()
        logger.info(f"新建档案: {name}")
        self._loadProfile(name)

    def _onDeleteProfile(self):
        """删除档案"""
        names = list_profiles()
        if len(names) <= 1:
            logger.debug("[TimetablePage] 删除档案取消: 仅剩最后一个档案")
            w = MessageBox(
                tr("timetable.warning_title"),
                tr("timetable.cannot_delete_last"),
                self
            )
            w.exec()
            return

        w = MessageBox(
            tr("timetable.confirm_delete_title"),
            tr("timetable.confirm_delete_body").replace("{name}", self._current_profile_name),
            self
        )
        if not w.exec():
            logger.debug("[TimetablePage] 用户放弃确认")
            return

        delete_profile(self._current_profile_name)
        logger.info(f"删除档案: {self._current_profile_name}")
        names = list_profiles()
        if names:
            self._loadProfile(names[-1])

    def _onRenameProfile(self):
        """重命名档案"""
        le = LineEdit(self)
        le.setText(self._current_profile_name)
        le.selectAll()

        w = MessageBox(
            tr("timetable.rename_title"),
            "",
            self
        )
        w.textLayout.insertWidget(1, le)
        le.setMinimumWidth(200)

        if not w.exec():
            logger.debug("[TimetablePage] 用户放弃确认")
            return

        new_name = le.text().strip()
        if not new_name or new_name == self._current_profile_name:
            logger.debug(f"[TimetablePage] 重命名取消: 新名称='{new_name}'")
            return
        if new_name in list_profiles():
            logger.warning(f"[TimetablePage] 重命名失败: 名称已存在 '{new_name}'")
            w2 = MessageBox(
                tr("timetable.warning_title"),
                tr("timetable.name_exists"),
                self
            )
            w2.exec()
            return

        rename_profile(self._current_profile_name, new_name)
        logger.info(f"重命名档案: {self._current_profile_name} -> {new_name}")
        self._loadProfile(new_name)

    def _onImportProfile(self):
        """导入档案 / 自动检测"""
        if self._linkageMode:
            source = "classisland" if self._activeBridge is self._ciBridge else "classwidgets"
            logger.debug(f"[TimetablePage] 联动模式下导入按钮 -> 自动检测: {source}")
            self._onAutoDetect(source)
            return
        file_path, _ = QFileDialog.getOpenFileName(
            self,
            tr("timetable.profile_import"),
            "",
            "JSON (*.json)"
        )
        if not file_path:
            logger.debug("[TimetablePage] 导入取消: 未选择文件")
            return
        self._importProfileFrom(file_path)

    def _onExportProfile(self):
        """导出档案 / 打开数据目录"""
        if self._linkageMode:
            if self._activeBridge is self._ciBridge:
                path = cfg.linkageDataPath.value
            elif self._activeBridge is self._cwBridge:
                path = cfg.classWidgetsDataPath.value
            else:
                logger.warning("[TimetablePage] 打开数据目录 未识别的联动桥")
                return
            if path and os.path.isdir(path):
                logger.debug(f"[TimetablePage] 联动模式打开数据目录: {path}")
                os.startfile(path)
            return
        if self._profile is None:
            logger.debug("[TimetablePage] 导出档案 无当前档案")
            return
        file_path, _ = QFileDialog.getSaveFileName(
            self,
            tr("timetable.profile_export"),
            f"{self._current_profile_name}.json",
            "JSON (*.json)"
        )
        if not file_path:
            logger.debug("[TimetablePage] 导出取消: 未选择保存路径")
            return
        logger.info(f"导出档案: {self._current_profile_name} -> {file_path}")
        shutil.copy(get_profile_path(self._current_profile_name), file_path)

    def _onOpenFolder(self):
        """选择档案 / 手动选择数据目录"""
        if self._linkageMode:
            dir_path = QFileDialog.getExistingDirectory(
                self,
                tr("timetable.select_data_dir"),
                ""
            )
            if not dir_path:
                logger.debug("[TimetablePage] 联动选择数据目录取消: 未选择目录")
                return
            if self._activeBridge is self._ciBridge:
                cfg.linkageDataPath.value = dir_path
                self._ciBridge.set_data_path(dir_path)
                self._ciBridge.start()
            elif self._activeBridge is self._cwBridge:
                cfg.classWidgetsDataPath.value = dir_path
                self._cwBridge.set_data_path(dir_path)
                self._cwBridge.start()
            self._updateLinkagePathLabel()
            logger.info(f"[TimetablePage] 联动数据目录已设置: {dir_path}")
            return
        os.makedirs(PROFILES_DIR, exist_ok=True)
        file_path, _ = QFileDialog.getOpenFileName(
            self,
            tr("timetable.profile_select"),
            PROFILES_DIR,
            "JSON (*.json)"
        )
        if not file_path:
            logger.debug("[TimetablePage] 选择档案取消: 未选择文件")
            return
        self._importProfileFrom(file_path)

    def _importProfileFrom(self, file_path: str):
        """外部档案拷入档案目录并加载"""
        filename = os.path.basename(file_path)
        name = os.path.splitext(filename)[0]
        if os.path.normcase(os.path.dirname(file_path)) != os.path.normcase(PROFILES_DIR):
            shutil.copy(file_path, os.path.join(PROFILES_DIR, filename))
            logger.debug(f"[TimetablePage] 外部档案已拷入: {file_path}")
        logger.info(f"导入档案: {filename}")
        self._loadProfile(name)

    def _onDurationChanged(self):
        """默认时长变更"""
        if self._profile is None:
            return
        logger.debug(f"[TimetablePage] 默认时长 上课={self._classDurSpin.value()}min 课间={self._breakDurSpin.value()}min")
        self._profile.default_class_duration = self._classDurSpin.value()
        self._profile.default_break_duration = self._breakDurSpin.value()
        self._saveProfile()
        self._syncTimePickers()


    # 联动方法
    @property
    def bridge(self):
        return self._activeBridge

    def get_today_schedule(self) -> list:
        """返回今日课表"""
        if self._activeBridge:
            try:
                result = self._activeBridge.get_today_schedule()
                self._log_schedule_once("联动", result)
                return result
            except Exception as e:
                logger.warning(f"[TimetablePage] 联动获取今日课表失败: {e}")
                return []

        if not self._profile or not self._profile.periods:
            return []

        now = _dt.datetime.now()
        today_wd = now.weekday()
        day_name = DAYS[today_wd]
        now_time = now.time()

        result = []
        class_counter = 0
        for i, p in enumerate(self._profile.periods):
            start = p.get("start", "")
            end = p.get("end", "")
            ptype = p.get("type", "")
            try:
                sh, sm = map(int, start.split(":"))
                eh, em = map(int, end.split(":"))
                is_current = _time(sh, sm) <= now_time < _time(eh, em)
            except Exception as e:
                logger.debug(f"[TimetablePage] 课表时间解析失败 这节不算当前: start={start!r} end={end!r} {e}")
                is_current = False

            if ptype in ("课间", "活动"):
                result.append(("", "", start, end, 0, is_current, True, ptype))
            else:
                class_counter += 1
                courses = self._profile.courses.get(str(i), {})
                subject = courses.get(day_name, "")
                result.append((subject, "", start, end, class_counter, is_current, False, ""))

        self._log_schedule_once("本地档案", result)
        return result

    def _log_schedule_once(self, origin: str, result: list):
        """课表内容变化时才记日志, 避免轮询刷屏"""
        sig = tuple((r[0], r[2], r[3], r[5]) for r in result)
        if getattr(self, "_last_schedule_sig", None) != sig:
            self._last_schedule_sig = sig
            logger.debug(f"[TimetablePage] 今日课表({origin}) {len(result)}节 当前 {[r[0] or r[7] for r in result if r[5]]}")

    def get_schedule_by_weekday(self, weekday: int) -> list:
        """返回指定星期课表"""
        if self._activeBridge:
            try:
                # ci 0=周日..6=周六  cw 0=周一..6=周日
                if self._activeBridge is self._ciBridge:
                    dotnet_wd = (weekday + 1) % 7  # Mon=1..Sun=7 -> Sun=0..Sat=6
                else:
                    dotnet_wd = weekday
                result = self._activeBridge.get_schedule_by_weekday(dotnet_wd)
                logger.debug(f"[TimetablePage] 联动按星期取课表: weekday={weekday} -> dotnet_wd={dotnet_wd} {len(result)}条")
                return result
            except Exception as e:
                logger.warning(f"[TimetablePage] 联动获取星期课表失败: weekday={weekday} - {e}")
                return []

        if not self._profile or not self._profile.periods:
            return []

        day_name = DAYS[weekday]  # weekday: 0=monday..6=sunday

        result = []
        class_counter = 0
        for i, p in enumerate(self._profile.periods):
            start = p.get("start", "")
            end = p.get("end", "")
            ptype = p.get("type", "")

            if ptype in ("课间", "活动"):
                result.append(("", "", start, end, 0, False, True, ptype))
            else:
                class_counter += 1
                courses = self._profile.courses.get(str(i), {})
                subject = courses.get(day_name, "")
                result.append((subject, "", start, end, class_counter, False, False, ""))

        logger.debug(f"[TimetablePage] 按星期取课表: weekday={weekday}({day_name}) {len(result)}条")
        return result

    def _onSourceChanged(self, index: int):
        """档案来源切换"""
        source_map = {0: "Glimpseon", 1: "classisland", 2: "classwidgets"}
        source = source_map.get(index, "Glimpseon")
        if index not in source_map:
            logger.debug(f"[TimetablePage] 未识别的来源下标: {index} 用 Glimpseon")
        logger.info(f"[TimetablePage] 切换档案来源: 下标={index} -> {source}")
        cfg.profileSource.value = source
        self._applySourceMode(source)

    def _applySourceMode(self, source: str):
        """根据来源模式切换 ui  Bridge"""
        if source == "Glimpseon":
            self._linkageMode = False
            self._activeBridge = None
            self._ciBridge.stop()
            self._cwBridge.stop()
            self._linkageRefreshTimer.stop()
            self._setEditModeEnabled(True)
            self._profileLabel.setText(self._current_profile_name)
            self._profileLabel.setStyleSheet("")
            self._importBtn.setToolTip(tr("timetable.profile_import"))
            self._exportBtn.setToolTip(tr("timetable.profile_export"))
            self._openFolderBtn.setToolTip(tr("timetable.profile_open_folder"))
            self._refreshTables()
        elif source == "classisland":
            self._enterLinkage(self._ciBridge, "linkageDataPath")
        elif source == "classwidgets":
            self._enterLinkage(self._cwBridge, "classWidgetsDataPath")
        logger.info(f"[TimetablePage] 来源模式: {source} 编辑区{'禁用' if self._linkageMode else '启用'}")

    def _enterLinkage(self, bridge, path_attr: str):
        """进入联动模式并启动指定桥"""
        self._linkageMode = True
        self._activeBridge = bridge
        (self._cwBridge if bridge is self._ciBridge else self._ciBridge).stop()
        path = getattr(cfg, path_attr).value
        logger.info(f"[TimetablePage] 进入联动模式: bridge={type(bridge).__name__} 数据路径={path or '(空)'}")
        bridge.set_data_path(path)
        bridge.poll_interval = 1
        if path:
            bridge.start()
        else:
            self._onAutoDetect("classisland" if bridge is self._ciBridge else "classwidgets")
        self._setEditModeEnabled(False)
        self._updateLinkagePathLabel()
        self._refreshLinkageTables()
        self._linkageRefreshTimer.start()
        self._importBtn.setToolTip(tr("timetable.btn_auto_detect"))
        self._exportBtn.setToolTip(tr("timetable.btn_open_data_dir"))
        self._openFolderBtn.setToolTip(tr("timetable.btn_select_dir"))

    def _setEditModeEnabled(self, enabled: bool):
        """禁用/启用手动编辑控件"""
        logger.debug(f"[TimetablePage] 编辑控件{'启用' if enabled else '禁用'}")
        triggers = (TableWidget.EditTrigger.DoubleClicked | TableWidget.EditTrigger.EditKeyPressed) if enabled else TableWidget.EditTrigger.NoEditTriggers
        self.courseTable.setEditTriggers(triggers)
        for btn in [self._btnClass, self._btnBreak, self._btnActivity, self._btnDeleteRow,
                     self._addBtn, self._renameBtn, self._delBtn]:
            btn.setEnabled(enabled)
        self._subjectBtnWidget.setEnabled(enabled)
        self._csSubjectEdit.setEnabled(enabled)
        self._startTimePicker.setEnabled(enabled)
        self._endTimePicker.setEnabled(enabled)
        self._classDurSpin.setEnabled(enabled)
        self._breakDurSpin.setEnabled(enabled)

    def _updateLinkagePathLabel(self):
        """联动模式更新档案名称为路径"""
        if self._activeBridge is self._ciBridge:
            path = cfg.linkageDataPath.value
        elif self._activeBridge is self._cwBridge:
            path = cfg.classWidgetsDataPath.value
        else:
            path = ""
        if not path:
            logger.debug("[TimetablePage] 联动路径标签: 路径为空 显示占位")
            self._profileLabel.setText(tr("linkage.path_empty"))
            self._profileLabel.setStyleSheet("color: #999; font-size: 11px;")
            self._profileLabel.setToolTip("")
            return

        color = "#30c361" if os.path.isdir(path) else "#d13438"
        max_width = max(self._profileLabel.width(), 200)
        fm = QFontMetrics(self._profileLabel.font())
        elided = fm.elidedText(path, Qt.TextElideMode.ElideMiddle, max_width)
        self._profileLabel.setText(elided)
        self._profileLabel.setStyleSheet(f"color: {color}; font-size: 11px;")
        self._profileLabel.setToolTip(path)

    def _onAutoDetect(self, source: str):
        """自动检测联动路径"""
        if source == "classisland":
            bridge, path_attr, missing = self._ciBridge, "linkageDataPath", "ClassIsland not found"
        else:
            bridge, path_attr, missing = self._cwBridge, "classWidgetsDataPath", "ClassWidgets not found"
        logger.info(f"[TimetablePage] 自动检测联动路径 source={source}")
        path = bridge.auto_detect()
        if path:
            logger.info(f"[TimetablePage] 自动检测命中 {path}")
            getattr(cfg, path_attr).value = path
            bridge.start()
            self._updateLinkagePathLabel()
            InfoBar.success(
                title=tr("timetable.auto_detect"), content=path,
                parent=self, position=InfoBarPosition.TOP, duration=3000,
            )
        else:
            logger.warning(f"[TimetablePage] 自动检测失败: {missing}")
            InfoBar.error(
                title=tr("timetable.auto_detect"), content=missing,
                parent=self, position=InfoBarPosition.TOP, duration=3000,
            )

    def _refreshLinkageTables(self):
        """获取一周数据"""
        if not self._activeBridge:
            return
        self._block_save = True

        week_data = self._activeBridge.get_week_schedule()

        # ci 返回 {1=Mon..7=Sun} cw 返回 {0=Mon..6=Sun}
        # 统一为 0=Mon..6=Sun
        if self._activeBridge is self._ciBridge:
            # ci
            schedules = [week_data.get(i + 1, []) for i in range(7)]
        else:
            # cw
            schedules = [week_data.get(i, []) for i in range(7)]

        # 构建时间表
        today_wd = _dt.date.today().weekday()  # 0=周一..6=周日
        today_schedule = schedules[today_wd] if today_wd < len(schedules) else []
        self.timeTable.setRowCount(0)
        self.timeTable.setRowCount(len(today_schedule))
        for i, row in enumerate(today_schedule):
            subject, teacher, start, end, idx, is_current, is_break, break_name = row
            ptype = break_name if is_break else "上课"
            self._setTimeRow(i, {"type": ptype, "start": start, "end": end})

        # 课程表
        # 找出最大行数
        non_break_counts = []
        for day_sched in schedules:
            non_break = [r for r in day_sched if not r[6]]  # r[6]=is_break
            non_break_counts.append(non_break)
        max_rows = max((len(c) for c in non_break_counts), default=0)

        self.courseTable.setRowCount(0)
        self.courseTable.setRowCount(max_rows)
        for table_row in range(max_rows):
            # 第0列时间段：用今天的数据
            if table_row < len(non_break_counts[today_wd]):
                _, _, start, end, _, _, _, _ = non_break_counts[today_wd][table_row]
                range_item = QTableWidgetItem(f"{start}~{end}")
                range_item.setFlags(range_item.flags() & ~Qt.ItemFlag.ItemIsEditable)
                self.courseTable.setItem(table_row, 0, range_item)
            else:
                range_item = QTableWidgetItem("")
                range_item.setFlags(range_item.flags() & ~Qt.ItemFlag.ItemIsEditable)
                self.courseTable.setItem(table_row, 0, range_item)

            # 第1-7列：每天对应科目
            for col_idx, day_sched in enumerate(non_break_counts):
                if table_row < len(day_sched):
                    subject = day_sched[table_row][0]
                    item = QTableWidgetItem(subject)
                    item.setFlags(item.flags() & ~Qt.ItemFlag.ItemIsEditable)
                    self.courseTable.setItem(table_row, col_idx + 1, item)
                else:
                    item = QTableWidgetItem("")
                    item.setFlags(item.flags() & ~Qt.ItemFlag.ItemIsEditable)
                    self.courseTable.setItem(table_row, col_idx + 1, item)

        self._block_save = False
        logger.debug(f"[TimetablePage] 联动表格已刷新 今日{len(today_schedule)}行 最大{max_rows}行")
        self._scheduleChangedTimer.start()

    def _onLinkageStateChanged(self, state):
        if self._activeBridge is not self.sender():
            logger.debug(f"[TimetablePage] 联动状态变更 非当前桥 sender={self.sender()}")
            return
        logger.info(f"[TimetablePage] 联动状态变更: state={state}")
        self._refreshLinkageTables()

    def _onLinkageConnectedChanged(self, connected):
        if self._activeBridge is not self.sender():
            logger.debug(f"[TimetablePage] 联动连接状态 非当前桥 sender={self.sender()}")
            return
        logger.info(f"[TimetablePage] 联动连接状态: connected={connected}")
        if connected and not self._linkageRefreshTimer.isActive():
            self._linkageRefreshTimer.start()
        self._updateLinkagePathLabel()

    def _onLinkageError(self, msg: str):
        logger.error(f"[TimetablePage] 联动错误: {msg}")
        if msg.startswith("REDIRECT:"):
            new_path = msg[len("REDIRECT:"):]
            if self._activeBridge is self._ciBridge:
                cfg.linkageDataPath.value = new_path
            elif self._activeBridge is self._cwBridge:
                cfg.classWidgetsDataPath.value = new_path
            self._updateLinkagePathLabel()

    def _onLinkageRefreshTimer(self):
        """定时刷新联动表格"""
        if self._activeBridge and self._linkageMode:
            self._refreshLinkageTables()

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if self._linkageMode:
            self._updateLinkagePathLabel()

    def _onThemeChanged(self, theme):
        """主题变更"""
        logger.debug(f"[TimetablePage] 主题变更: {theme}")
        self.setStyleSheet(load_qss("timetable.qss"))
