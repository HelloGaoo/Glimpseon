"""
通知页面 - 左右布局：输入 + 预览 + 样式配置 + 队列管理
"""

import os
import logging
import sys
import uuid

from PyQt6.QtCore import Qt, QTimer, pyqtSignal, QDateTime, QDate, QTime
from PyQt6.QtWidgets import (
    QHBoxLayout,
    QHeaderView,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
    QColorDialog
)
from PyQt6.QtGui import QColor, QFont, QIntValidator
from qfluentwidgets import (
    BodyLabel,
    CardWidget,
    ColorDialog,
    PushButton,
    ScrollArea,
    StrongBodyLabel,
    Theme,
    ComboBox,
    PrimaryPushButton,
    SpinBox,
    TextEdit,
    CalendarPicker,
    TimePicker,
    InfoBar,
    InfoBarPosition,
    Dialog,
    LineEdit,
    TableWidget,
    SwitchButton,
)

from core.config import cfg
from core.constants import load_qss
from core.utils import tr, TranslatableWidget, FUI
from core.notification import NotifType
from qfluentwidgets import MessageBox

logger = logging.getLogger("Glimpseon.ui.notification")

# 获取通知类型文本

def _type_label(t: str) -> str:
    m = {
        NotifType.SCROLL: tr("notification.type_scroll"),
        NotifType.CORNER: tr("notification.type_corner"),
        NotifType.FULLSCREEN: tr("notification.type_fullscreen"),
    }
    return m.get(t, t)

class _PreviewWidget(QWidget):
    """通知文本样式预览控件"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("previewWidget")
        self.setMinimumHeight(140)
        self._text = ""
        self._bg_color = QColor(0, 0, 0, 180)
        self._text_color = QColor(255, 255, 255)
        self._font_size = 24
        self._font_weight = QFont.Weight.Bold

    def set_preview_text(self, text: str):
        self._text = text
        self.update()

    def set_bg_color(self, color: QColor):
        self._bg_color = color
        self.update()

    def set_text_color(self, color: QColor):
        self._text_color = color
        self.update()

    def set_font_size(self, size: int):
        self._font_size = size
        self.update()

    def set_font_weight(self, weight: QFont.Weight):
        self._font_weight = weight
        self.update()

    def paintEvent(self, event):
        from PyQt6.QtGui import QPainter
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        r = self.rect().adjusted(2, 2, -2, -2)
        painter.setPen(Qt.PenStyle.NoPen)
        painter.setBrush(self._bg_color)
        painter.drawRoundedRect(r, 12, 12)

        if not self._text:
            painter.setPen(QColor(180, 180, 180))
            font = QFont("HarmonyOS Sans", 14)
            painter.setFont(font)
            painter.drawText(r, Qt.AlignmentFlag.AlignCenter, tr("notification.preview_hint"))
            return

        painter.setPen(self._text_color)
        font = QFont("HarmonyOS Sans", self._font_size)
        font.setWeight(self._font_weight)
        painter.setFont(font)
        flags = Qt.AlignmentFlag.AlignCenter | Qt.TextFlag.TextWordWrap
        painter.drawText(r.adjusted(20, 10, -20, -10), flags, self._text)


# 队列配置详情对话框

class _ConfigEditDialog(Dialog):
    """配置对话框"""
    def __init__(self, data: dict, parent=None):
        super().__init__(tr("notification.config_detail_title"), "", parent)
        self.setMinimumWidth(620)

        self._result_data = dict(data)
        w = QWidget()
        layout = QVBoxLayout(w)
        layout.setSpacing(10)
        layout.setContentsMargins(12, 8, 12, 8)

        # SpinBox / ComboBox / ColorPickButton
        _spin_style = """
        SpinBox, ComboBox {
            background-color: rgba(40,40,40,0.9);
            color: #e0e0e0;
            border: 1px solid rgba(255,255,255,0.12);
            border-radius: 6px;
            padding: 4px 8px;
            font-size: 13px;
            min-width: 100px;
        }
        SpinBox:hover, ComboBox:hover { border-color: #0078d4; }
        SpinBox::up-button, SpinBox::down-button {
            border: none; width: 20px; background: transparent;
        }
        """

        row_style = "margin-top: 2px; margin-bottom: 2px;"

        # 内容
        layout.addWidget(BodyLabel(tr("notification.content_label") + ":"))
        self._content_edit = TextEdit(w)
        self._content_edit.setPlainText(data.get("content", ""))
        self._content_edit.setMinimumHeight(50)
        self._content_edit.setMaximumHeight(90)
        layout.addWidget(self._content_edit)

        # 类型
        r1 = QHBoxLayout()
        r1.setContentsMargins(0, 0, 0, 0)
        r1.addWidget(BodyLabel(tr("notification.type_label") + ":"))
        self._type_combo = ComboBox(w)
        self._type_combo.addItem(tr("notification.type_scroll"), userData=NotifType.SCROLL)
        self._type_combo.addItem(tr("notification.type_corner"), userData=NotifType.CORNER)
        self._type_combo.addItem(tr("notification.type_fullscreen"), userData=NotifType.FULLSCREEN)
        type_map = {NotifType.SCROLL: 0, NotifType.CORNER: 1, NotifType.FULLSCREEN: 2}
        self._type_combo.setCurrentIndex(type_map.get(data.get("type", NotifType.SCROLL), 0))
        self._type_combo.setMinimumWidth(160)
        self._type_combo.setStyleSheet(_spin_style)
        r1.addWidget(self._type_combo)
        r1.addStretch()
        layout.addLayout(r1)

        # 背景色 文字色
        color_row = QHBoxLayout()
        color_row.setSpacing(20)
        color_row.setContentsMargins(0, 0, 0, 0)

        def _color_group(label, default_color):
            g = QHBoxLayout()
            g.setSpacing(6)
            g.addWidget(BodyLabel(label + ":"))
            btn = PushButton(w)
            btn.setFixedSize(32, 28)
            btn.setStyleSheet("border-radius:4px;")
            return g, btn

        g1, self._bg_btn = _color_group(tr("notification.bg_color"), QColor(0, 0, 0, 180))
        g2, self._fg_btn = _color_group(tr("notification.text_color"), QColor(255, 255, 255))

        # 初始颜色
        self._edit_bg = QColor(
            data.get("bg_color", "#000000")
        ) if data.get("bg_color") else QColor(0, 0, 0)
        self._edit_bg.setAlpha(data.get("bg_alpha", 180))
        self._edit_fg = QColor(data.get("text_color", "#ffffff"))

        self._update_edit_color_btn(self._bg_btn, self._edit_bg)
        self._update_edit_color_btn(self._fg_btn, self._edit_fg)
        self._bg_btn.clicked.connect(lambda: self._edit_pick_color("bg"))
        self._fg_btn.clicked.connect(lambda: self._edit_pick_color("fg"))

        g1.addWidget(self._bg_btn)
        g2.addWidget(self._fg_btn)
        color_row.addLayout(g1)
        color_row.addLayout(g2)
        color_row.addStretch()
        layout.addLayout(color_row)

        # 字号 字重
        frow = QHBoxLayout()
        frow.setSpacing(20)
        frow.setContentsMargins(0, 0, 0, 0)

        fg1 = QHBoxLayout()
        fg1.setSpacing(6)
        fg1.addWidget(BodyLabel(tr("notification.font_size") + ":"))
        self._size_spin = SpinBox(w)
        self._size_spin.setRange(10, 100)
        self._size_spin.setValue(data.get("font_size", 24))
        self._size_spin.setFixedWidth(120)
        self._size_spin.setStyleSheet(_spin_style)
        fg1.addWidget(self._size_spin)
        frow.addLayout(fg1)

        fg2 = QHBoxLayout()
        fg2.setSpacing(6)
        fg2.addWidget(BodyLabel(tr("notification.font_weight") + ":"))
        self._weight_combo = ComboBox(w)
        self._weight_combo.addItem(tr("common.normal"), userData=QFont.Weight.Normal)
        self._weight_combo.addItem(tr("common.bold"), userData=QFont.Weight.Bold)
        self._weight_combo.addItem(tr("common.black"), userData=QFont.Weight.Black)
        wmap = {QFont.Weight.Normal: 0, QFont.Weight.Bold: 1, QFont.Weight.Black: 2}
        self._weight_combo.setCurrentIndex(wmap.get(data.get("font_weight", QFont.Weight.Bold), 1))
        self._weight_combo.setMinimumWidth(130)
        self._weight_combo.setStyleSheet(_spin_style)
        fg2.addWidget(self._weight_combo)
        frow.addLayout(fg2)
        frow.addStretch()
        layout.addLayout(frow)

        # 速度 时长
        sdrow = QHBoxLayout()
        sdrow.setSpacing(20)
        sdrow.setContentsMargins(0, 0, 0, 0)

        sg1 = QHBoxLayout()
        sg1.setSpacing(6)
        sg1.addWidget(BodyLabel(tr("notification.settings_speed") + ":"))
        self._speed_spin = SpinBox(w)
        self._speed_spin.setRange(1, 20)
        self._speed_spin.setValue(data.get("speed", 5))
        self._speed_spin.setFixedWidth(120)
        self._speed_spin.setStyleSheet(_spin_style)
        sg1.addWidget(self._speed_spin)
        sdrow.addLayout(sg1)

        sg2 = QHBoxLayout()
        sg2.setSpacing(6)
        sg2.addWidget(BodyLabel(tr("notification.settings_duration") + ":"))
        self._dur_spin = SpinBox(w)
        self._dur_spin.setRange(1, 60)
        self._dur_spin.setValue(data.get("duration", 10))
        self._dur_spin.setFixedWidth(120)
        self._dur_spin.setStyleSheet(_spin_style)
        sg2.addWidget(self._dur_spin)
        sdrow.addLayout(sg2)
        sdrow.addStretch()
        layout.addLayout(sdrow)

        # 横幅背景高度 鼠标穿透
        brrow = QHBoxLayout()
        brrow.setSpacing(20)
        brrow.setContentsMargins(0, 0, 0, 0)

        bg1 = QHBoxLayout()
        bg1.setSpacing(6)
        bg1.addWidget(BodyLabel(tr("notification.banner_bg_height") + ":"))
        self._bg_height_spin = SpinBox(w)
        self._bg_height_spin.setRange(40, 300)
        self._bg_height_spin.setValue(cfg.scrollBannerBgHeight.value)
        self._bg_height_spin.setFixedWidth(120)
        self._bg_height_spin.setStyleSheet(_spin_style)
        self._bg_height_spin.valueChanged.connect(lambda v: cfg.set(cfg.scrollBannerBgHeight, v))
        bg1.addWidget(self._bg_height_spin)
        brrow.addLayout(bg1)

        bg2 = QHBoxLayout()
        bg2.setSpacing(6)
        bg2.addWidget(BodyLabel(tr("notification.banner_mouse_through") + ":"))
        self._mouse_switch = SwitchButton(w)
        self._mouse_switch.setOnText(tr("common.on"))
        self._mouse_switch.setOffText(tr("common.off"))
        self._mouse_switch.setChecked(cfg.scrollBannerMouseThrough.value)
        self._mouse_switch.checkedChanged.connect(
            lambda v: cfg.set(cfg.scrollBannerMouseThrough, v)
        )
        bg2.addWidget(self._mouse_switch)
        brrow.addLayout(bg2)
        brrow.addStretch()
        layout.addLayout(brrow)

        # 定时(日期+时间)
        is_scheduled = data.get("_scheduled", False)
        if is_scheduled:
            ft = data.get("_fire_time", QDateTime.currentDateTime())
            sched_row = QHBoxLayout()
            sched_row.setSpacing(20)
            sched_row.setContentsMargins(0, 0, 0, 0)

            sched_row.addWidget(BodyLabel(tr("notification.schedule_label") + ":"))
            self._date_picker = CalendarPicker(w)
            self._date_picker.setDate(ft.date())
            self._date_picker.setMinimumWidth(180)
            sched_row.addWidget(self._date_picker)

            self._time_picker = TimePicker(w)
            self._time_picker.setTime(ft.time())
            self._time_picker.setMinimumWidth(160)
            sched_row.addWidget(self._time_picker)

            sched_row.addStretch()
            layout.addLayout(sched_row)

        self.textLayout.addWidget(w)
        self.yesButton.setText(tr("common.confirm"))
        self.cancelButton.setText(tr("common.cancel"))

    def _update_edit_color_btn(self, btn, color):
        btn.setStyleSheet(f"background-color:{color.name()};border-radius:4px;"
                          if color.alpha() >= 255
                          else f"background-color:{color.name()};border-radius:4px;")

    def _edit_pick_color(self, tag):
        old = self._edit_bg if tag == "bg" else self._edit_fg
        dlg = QColorDialog(old, self)
        dlg.setWindowTitle(self.tr("common.color_pick") if hasattr(self, "tr") else tr("common.color_pick"))
        if tag == "bg":
            dlg.setOption(QColorDialog.ColorDialogOption.ShowAlphaChannel, True)
        if dlg.exec():
            c = dlg.selectedColor()
            if tag == "bg":
                self._edit_bg = c
            else:
                self._edit_fg = c
            btn = self._bg_btn if tag == "bg" else self._fg_btn
            self._update_edit_color_btn(btn, c)

    def get_data(self) -> dict:
        data = dict(self._result_data)
        data["type"] = self._type_combo.currentData()
        data["content"] = self._content_edit.toPlainText().strip()
        data["bg_color"] = self._edit_bg.name()
        data["bg_alpha"] = self._edit_bg.alpha()
        data["text_color"] = self._edit_fg.name()
        data["font_size"] = self._size_spin.value()
        data["font_weight"] = self._weight_combo.currentData()
        data["speed"] = self._speed_spin.value()
        data["duration"] = self._dur_spin.value()
        if hasattr(self, "_date_picker"):
            data["_fire_time"] = QDateTime(
                self._date_picker.date, self._time_picker.time
            )
        return data


# 主页面

class NotificationPage(ScrollArea, TranslatableWidget):
    """通知页面"""

    send_notification = pyqtSignal(dict)

    def __init__(self, parent=None):
        super().__init__(parent=parent)
        self.setObjectName("notification")

        # 任务队列
        self._queue: list[dict] = []
        self._is_showing = False
        self._showing_notif_uid = ""

        self.scrollWidget = QWidget()
        self.scrollWidget.setObjectName("scrollWidget")
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)

        self.mainLayout = QVBoxLayout(self.scrollWidget)
        self.mainLayout.setContentsMargins(40, 20, 40, 20)
        self.mainLayout.setSpacing(16)

        # 标题
        self.titleLabel = StrongBodyLabel(tr("notification.title"), self.scrollWidget)
        self.titleLabel.setObjectName("notificationTitle")
        self.mainLayout.addWidget(self.titleLabel)

        # 左右两栏
        split_layout = QHBoxLayout()
        split_layout.setSpacing(20)

        # 左栏
        left_widget = QWidget()
        left_layout = QVBoxLayout(left_widget)
        left_layout.setContentsMargins(0, 0, 0, 0)
        left_layout.setSpacing(16)

        # 左上：内容输入框
        content_card = CardWidget(left_widget)
        content_card.setObjectName("contentCard")
        content_card_layout = QVBoxLayout(content_card)
        content_card_layout.setContentsMargins(24, 20, 24, 20)
        content_card_layout.setSpacing(10)

        content_title = StrongBodyLabel(tr("notification.content_label"), content_card)
        content_card_layout.addWidget(content_title)

        self.contentEdit = TextEdit(content_card)
        self.contentEdit.setPlaceholderText(tr("notification.content_placeholder"))
        self.contentEdit.setMinimumHeight(160)
        content_card_layout.addWidget(self.contentEdit)

        self.charCountLabel = BodyLabel("0", content_card)
        self.charCountLabel.setObjectName("charCount")
        self.charCountLabel.setAlignment(Qt.AlignmentFlag.AlignRight)
        self.contentEdit.textChanged.connect(
            lambda: self.charCountLabel.setText(str(len(self.contentEdit.toPlainText())))
        )
        content_card_layout.addWidget(self.charCountLabel)

        left_layout.addWidget(content_card)

        # 左下：队列管理
        queue_card = CardWidget(left_widget)
        queue_card.setObjectName("queueCard")
        queue_layout = QVBoxLayout(queue_card)
        queue_layout.setContentsMargins(24, 20, 24, 20)
        queue_layout.setSpacing(10)

        queue_header = QHBoxLayout()
        queue_header.setSpacing(8)
        queue_title = StrongBodyLabel(tr("notification.queue_label"), queue_card)
        queue_header.addWidget(queue_title)
        queue_header.addStretch()
        self.queueCountLabel = BodyLabel("0", queue_card)
        self.queueCountLabel.setObjectName("queueCount")
        queue_header.addWidget(self.queueCountLabel)
        queue_layout.addLayout(queue_header)

        self.queueTable = TableWidget(queue_card)
        self.queueTable.setObjectName("queueTable")
        self.queueTable.setColumnCount(4)
        self.queueTable.setHorizontalHeaderLabels([
            "#", tr("notification.type_label"), tr("notification.content_label"), tr("notification.queue_status")
        ])
        self.queueTable.horizontalHeader().setSectionResizeMode(0, QHeaderView.ResizeMode.ResizeToContents)
        self.queueTable.horizontalHeader().setSectionResizeMode(1, QHeaderView.ResizeMode.ResizeToContents)
        self.queueTable.horizontalHeader().setSectionResizeMode(2, QHeaderView.ResizeMode.Stretch)
        self.queueTable.horizontalHeader().setSectionResizeMode(3, QHeaderView.ResizeMode.ResizeToContents)
        self.queueTable.verticalHeader().hide()
        self.queueTable.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.queueTable.setSelectionMode(QTableWidget.SelectionMode.SingleSelection)
        self.queueTable.setEditTriggers(QTableWidget.EditTrigger.NoEditTriggers)
        self.queueTable.setAlternatingRowColors(True)
        self.queueTable.setMinimumHeight(180)
        queue_layout.addWidget(self.queueTable)

        left_layout.addWidget(queue_card, stretch=1)

        # 右栏
        right_widget = QWidget()
        right_layout = QVBoxLayout(right_widget)
        right_layout.setContentsMargins(0, 0, 0, 0)
        right_layout.setSpacing(16)

        # 右上：预览
        preview_card = CardWidget(right_widget)
        preview_card.setObjectName("previewCard")
        preview_layout = QVBoxLayout(preview_card)
        preview_layout.setContentsMargins(24, 20, 24, 20)
        preview_layout.setSpacing(10)

        preview_title = StrongBodyLabel(tr("notification.preview_label"), preview_card)
        preview_layout.addWidget(preview_title)

        self.previewWidget = _PreviewWidget(preview_card)
        preview_layout.addWidget(self.previewWidget, stretch=1)

        right_layout.addWidget(preview_card, stretch=1)

        # 右下：样式 发送配置
        config_card = CardWidget(right_widget)
        config_card.setObjectName("configCard")
        config_layout = QVBoxLayout(config_card)
        config_layout.setContentsMargins(24, 20, 24, 20)
        config_layout.setSpacing(12)

        config_title = StrongBodyLabel(tr("notification.config_label"), config_card)
        config_layout.addWidget(config_title)

        # 类型
        type_row = QHBoxLayout()
        type_row.setSpacing(8)
        type_label = BodyLabel(tr("notification.type_label"), config_card)
        type_label.setFixedWidth(95)
        self.typeCombo = ComboBox(config_card)
        self.typeCombo.addItem(tr("notification.type_scroll"), userData=NotifType.SCROLL)
        self.typeCombo.addItem(tr("notification.type_corner"), userData=NotifType.CORNER)
        self.typeCombo.addItem(tr("notification.type_fullscreen"), userData=NotifType.FULLSCREEN)
        self.typeCombo.setCurrentIndex(0)
        self.typeCombo.setMinimumWidth(150)
        type_row.addWidget(type_label)
        type_row.addWidget(self.typeCombo)
        type_row.addStretch()
        config_layout.addLayout(type_row)

        # 背景色 文字色
        color_row = QHBoxLayout()
        color_row.setSpacing(16)

        for tag, label_text, attr in [
            ("bg", tr("notification.bg_color"), "_bg_color"),
            ("fg", tr("notification.text_color"), "_fg_color"),
        ]:
            group = QHBoxLayout()
            group.setSpacing(6)
            lb = BodyLabel(label_text, config_card)
            lb.setFixedWidth(95)
            btn = PushButton(config_card)
            btn.setObjectName("colorBtn")
            btn.setFixedSize(32, 28)
            setattr(self, f"{tag}ColorBtn", btn)
            color = QColor(0, 0, 0, 180) if tag == "bg" else QColor(255, 255, 255)
            setattr(self, attr, color)
            self._update_color_btn(btn, color)
            btn.clicked.connect(lambda checked, t=tag: self._pick_color(t))
            group.addWidget(lb)
            group.addWidget(btn)
            color_row.addLayout(group)

        color_row.addStretch()
        config_layout.addLayout(color_row)

        # 字号 字重
        font_row = QHBoxLayout()
        font_row.setSpacing(16)

        size_group = QHBoxLayout()
        size_group.setSpacing(6)
        size_label = BodyLabel(tr("notification.font_size"), config_card)
        size_label.setFixedWidth(95)
        self.fontSizeSpin = SpinBox(config_card)
        self.fontSizeSpin.setRange(12, 72)
        self.fontSizeSpin.setValue(24)
        self.fontSizeSpin.setFixedWidth(140)
        self.fontSizeSpin.valueChanged.connect(self._update_preview)
        size_group.addWidget(size_label)
        size_group.addWidget(self.fontSizeSpin)
        font_row.addLayout(size_group)

        weight_group = QHBoxLayout()
        weight_group.setSpacing(6)
        weight_label = BodyLabel(tr("notification.font_weight"), config_card)
        weight_label.setFixedWidth(95)
        self.fontWeightCombo = ComboBox(config_card)
        self.fontWeightCombo.addItem(tr("common.normal"), userData=QFont.Weight.Normal)
        self.fontWeightCombo.addItem(tr("common.bold"), userData=QFont.Weight.Bold)
        self.fontWeightCombo.addItem(tr("common.black"), userData=QFont.Weight.Black)
        self.fontWeightCombo.setCurrentIndex(1)
        self.fontWeightCombo.setMinimumWidth(140)
        self.fontWeightCombo.currentIndexChanged.connect(self._update_preview)
        weight_group.addWidget(weight_label)
        weight_group.addWidget(self.fontWeightCombo)
        font_row.addLayout(weight_group)

        font_row.addStretch()
        config_layout.addLayout(font_row)

        # 分隔线
        sep = QWidget()
        sep.setFixedHeight(1)
        sep.setObjectName("separator")
        config_layout.addWidget(sep)

        # 速度 时长
        sd_row = QHBoxLayout()
        sd_row.setSpacing(16)

        speed_group = QHBoxLayout()
        speed_group.setSpacing(6)
        speed_label = BodyLabel(tr("notification.settings_speed"), config_card)
        speed_label.setFixedWidth(95)
        self.speedSpin = SpinBox(config_card)
        self.speedSpin.setRange(1, 20)
        self.speedSpin.setValue(5)
        self.speedSpin.setFixedWidth(140)
        speed_group.addWidget(speed_label)
        speed_group.addWidget(self.speedSpin)
        sd_row.addLayout(speed_group)

        duration_group = QHBoxLayout()
        duration_group.setSpacing(6)
        duration_label = BodyLabel(tr("notification.settings_duration"), config_card)
        duration_label.setFixedWidth(95)
        self.durationSpin = SpinBox(config_card)
        self.durationSpin.setRange(1, 60)
        self.durationSpin.setValue(10)
        self.durationSpin.setFixedWidth(140)
        duration_group.addWidget(duration_label)
        duration_group.addWidget(self.durationSpin)
        sd_row.addLayout(duration_group)

        sd_row.addStretch()
        config_layout.addLayout(sd_row)

        # 滚动横幅背景 鼠标穿透
        banner_row = QHBoxLayout()
        banner_row.setSpacing(16)

        bg_height_group = QHBoxLayout()
        bg_height_group.setSpacing(6)
        bg_height_label = BodyLabel(tr("notification.banner_bg_height"), config_card)
        bg_height_label.setFixedWidth(95)
        self.bannerBgHeightSpin = SpinBox(config_card)
        self.bannerBgHeightSpin.setRange(40, 300)
        self.bannerBgHeightSpin.setValue(cfg.scrollBannerBgHeight.value)
        self.bannerBgHeightSpin.setFixedWidth(140)
        self.bannerBgHeightSpin.valueChanged.connect(lambda v: cfg.set(cfg.scrollBannerBgHeight, v))
        bg_height_group.addWidget(bg_height_label)
        bg_height_group.addWidget(self.bannerBgHeightSpin)
        banner_row.addLayout(bg_height_group)

        mouse_label = BodyLabel(tr("notification.banner_mouse_through"), config_card)
        self.mouseThroughSwitch = SwitchButton(config_card)
        self.mouseThroughSwitch.setOnText(tr("common.on"))
        self.mouseThroughSwitch.setOffText(tr("common.off"))
        self.mouseThroughSwitch.setChecked(cfg.scrollBannerMouseThrough.value)
        self.mouseThroughSwitch.checkedChanged.connect(
            lambda v: cfg.set(cfg.scrollBannerMouseThrough, v)
        )
        banner_row.addWidget(mouse_label)
        banner_row.addWidget(self.mouseThroughSwitch)

        banner_row.addStretch()
        config_layout.addLayout(banner_row)

        # 发送按钮
        action_row = QHBoxLayout()
        action_row.setSpacing(10)

        self.addQueueBtn = PrimaryPushButton(FUI.ADD, tr("notification.add_queue"), config_card)
        self.addQueueBtn.setMinimumHeight(34)
        self.addQueueBtn.clicked.connect(self._onAddToQueue)
        action_row.addWidget(self.addQueueBtn)

        self.scheduleBtn = PushButton(FUI.CALENDAR, tr("notification.schedule"), config_card)
        self.scheduleBtn.setMinimumHeight(34)
        self.scheduleBtn.clicked.connect(self._onScheduleClicked)
        action_row.addWidget(self.scheduleBtn)

        action_row.addStretch()
        config_layout.addLayout(action_row)

        # 分隔线
        sep2 = QWidget()
        sep2.setFixedHeight(1)
        sep2.setObjectName("separator")
        config_layout.addWidget(sep2)

        # 队列操作按钮
        queue_ops_label = BodyLabel(tr("notification.queue_ops"), config_card)
        queue_ops_label.setObjectName("queueOpsLabel")
        config_layout.addWidget(queue_ops_label)

        btn_row = QHBoxLayout()
        btn_row.setSpacing(8)

        self.nextBtn = PrimaryPushButton(FUI.PLAY, tr("notification.next_display"), config_card)
        self.nextBtn.setMinimumHeight(32)
        self.nextBtn.clicked.connect(self._onNextDisplay)
        btn_row.addWidget(self.nextBtn)

        self.upBtn = PushButton(FUI.UP, tr("common.up"), config_card)
        self.upBtn.setMinimumHeight(32)
        self.upBtn.clicked.connect(self._move_up)
        btn_row.addWidget(self.upBtn)

        self.downBtn = PushButton(FUI.DOWN, tr("common.down"), config_card)
        self.downBtn.setMinimumHeight(32)
        self.downBtn.clicked.connect(self._move_down)
        btn_row.addWidget(self.downBtn)

        self.delBtn = PushButton(FUI.DELETE, tr("notification.delete_task"), config_card)
        self.delBtn.setMinimumHeight(32)
        self.delBtn.clicked.connect(self._onDeleteTask)
        btn_row.addWidget(self.delBtn)

        self.showCfgBtn = PushButton(FUI.INFO, tr("notification.edit_config"), config_card)
        self.showCfgBtn.setMinimumHeight(32)
        self.showCfgBtn.clicked.connect(self._onShowConfig)
        btn_row.addWidget(self.showCfgBtn)

        btn_row.addStretch()
        config_layout.addLayout(btn_row)

        right_layout.addWidget(config_card)

        # 组装左右
        split_layout.addWidget(left_widget, stretch=1)
        split_layout.addWidget(right_widget, stretch=1)
        self.mainLayout.addLayout(split_layout, stretch=1)

        self.setStyleSheet(load_qss("notification.qss"))

        # 定时发送检查
        self._schedule_check_timer = QTimer(self)
        self._schedule_check_timer.setInterval(10000)
        self._schedule_check_timer.timeout.connect(self._check_scheduled)
        self._schedule_check_timer.start()

        # 内容变更同步预览
        self.contentEdit.textChanged.connect(self._update_preview)
        self._update_preview()

    # 队列管理

    def _add_to_queue(self, data: dict):
        self._queue.append(data)
        self._refresh_queue_table()
        InfoBar.success(
            title=tr("common.tip"),
            content=tr("notification.added_to_queue"),
            parent=self.window(),
            position=InfoBarPosition.TOP,
            duration=2000,
        )
        # 自动播放
        if not self._is_showing:
            self._send_next()

    def _remove_from_queue(self, index: int):
        if 0 <= index < len(self._queue):
            item_uid = self._queue[index].get("_uid", "")
            if item_uid and item_uid == self._showing_notif_uid:
                InfoBar.warning(
                    title=tr("common.tip"),
                    content=tr("notification.cannot_delete_showing"),
                    parent=self.window(),
                    position=InfoBarPosition.TOP,
                    duration=2000,
                )
                return
            self._queue.pop(index)
            self._refresh_queue_table()

    def _refresh_queue_table(self):
        self.queueTable.setRowCount(len(self._queue))
        for i, item in enumerate(self._queue):
            self.queueTable.setItem(i, 0, QTableWidgetItem(str(i + 1)))
            # 类型
            self.queueTable.setItem(i, 1, QTableWidgetItem(_type_label(item.get("type", ""))))
            # 内容
            content = item.get("content", "")
            if item.get("_scheduled"):
                ft = item.get("_fire_time")
                if ft:
                    content += f"  ({ft.toString('yyyy-MM-dd HH:mm')})"
            self.queueTable.setItem(i, 2, QTableWidgetItem(content))
            # 状态
            item_uid = item.get("_uid", "")
            if item_uid and item_uid == self._showing_notif_uid:
                st = QTableWidgetItem(tr("notification.status_showing"))
                st.setForeground(QColor("#27c26e"))
            elif item.get("_scheduled"):
                st = QTableWidgetItem(tr("notification.status_scheduled"))
                st.setForeground(QColor("#f0ad4e"))
            else:
                st = QTableWidgetItem(tr("notification.status_queued"))
            st.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            self.queueTable.setItem(i, 3, st)
        self.queueCountLabel.setText(str(len(self._queue)))

    def _send_next(self):
        """从队列取第一个发送"""
        if not self._queue:
            self._is_showing = False
            self._showing_notif_uid = ""
            return
        data = self._queue[0]
        if "_uid" not in data:
            data["_uid"] = str(uuid.uuid4())
        self._is_showing = True
        self._showing_notif_uid = data["_uid"]
        self._refresh_queue_table()
        logger.info(f"队列发送通知: {data}")
        self.send_notification.emit(data)

    def _on_notification_shown(self):
        """通知显示完回调"""
        for i, item in enumerate(self._queue):
            if item.get("_uid", "") == self._showing_notif_uid:
                self._queue.pop(i)
                break
        self._is_showing = False
        self._showing_notif_uid = ""
        self._refresh_queue_table()
        if self._queue:
            self._send_next()

    def _q_showing_notif_uid(self):
        if self._queue and "_uid" in self._queue[0]:
            return self._queue[0]["_uid"]
        return ""

    # 上下移动
    def _move_up(self):
        row = self.queueTable.currentRow()
        if row <= 0 or row >= len(self._queue):
            return
        self._queue[row], self._queue[row - 1] = self._queue[row - 1], self._queue[row]
        self._refresh_queue_table()
        self.queueTable.setCurrentCell(row - 1, 0)

    def _move_down(self):
        row = self.queueTable.currentRow()
        if row < 0 or row >= len(self._queue) - 1:
            return
        self._queue[row], self._queue[row + 1] = self._queue[row + 1], self._queue[row]
        self._refresh_queue_table()
        self.queueTable.setCurrentCell(row + 1, 0)

    # 颜色按钮

    def _update_color_btn(self, btn: PushButton, color: QColor):
        btn.setStyleSheet(
            f"#colorBtn {{ background-color: {color.name()}; border: 1px solid rgba(128,128,128,0.3); border-radius: 4px; }}"
        )

    def _pick_color(self, target: str):
        attr = "_bg_color" if target == "bg" else "_fg_color"
        init = getattr(self, attr)
        title = tr("notification.bg_color") if target == "bg" else tr("notification.text_color")
        dlg = ColorDialog(init, title, self.window(), enableAlpha=True)
        dlg.colorChanged.connect(lambda c: self._on_color_preview(c, target))
        if dlg.exec():
            color = dlg.color
            setattr(self, attr, color)
            btn = self.bgColorBtn if target == "bg" else self.fgColorBtn
            self._update_color_btn(btn, color)
            self.previewWidget.set_bg_color(color) if target == "bg" else self.previewWidget.set_text_color(color)
        else:
            if target == "bg":
                self.previewWidget.set_bg_color(self._bg_color)
            else:
                self.previewWidget.set_text_color(self._fg_color)

    def _on_color_preview(self, color: QColor, target: str):
        if target == "bg":
            self.previewWidget.set_bg_color(color)
        else:
            self.previewWidget.set_text_color(color)

    # 预览更新

    def _update_preview(self):
        text = self.contentEdit.toPlainText().strip()
        self.previewWidget._text = text
        self.previewWidget._font_size = self.fontSizeSpin.value()
        self.previewWidget._font_weight = self.fontWeightCombo.currentData()
        self.previewWidget.update()

    # 构建数据

    def _build_notif_data(self) -> dict:
        return {
            "type": self.typeCombo.currentData(),
            "content": self.contentEdit.toPlainText().strip(),
            "speed": self.speedSpin.value(),
            "duration": self.durationSpin.value(),
            "bg_color": self._bg_color.name(),
            "bg_alpha": self._bg_color.alpha(),
            "text_color": self._fg_color.name(),
            "font_size": self.fontSizeSpin.value(),
            "font_weight": self.fontWeightCombo.currentIndex(),
        }

    # 按钮动作

    def _onAddToQueue(self):
        """加入队列"""
        content = self.contentEdit.toPlainText().strip()
        if not content:
            w = MessageBox(tr("common.tip"), tr("notification.empty_content"), self.window())
            w.exec()
            return
        self._add_to_queue(self._build_notif_data())

    def _onNextDisplay(self):
        """提高优先级"""
        row = self.queueTable.currentRow()
        if row < 0 or row >= len(self._queue):
            InfoBar.warning(
                title=tr("common.tip"),
                content=tr("notification.select_task_first"),
                parent=self.window(),
                position=InfoBarPosition.TOP,
                duration=2000,
            )
            return
        # 移到队首
        item = self._queue.pop(row)
        self._queue.insert(0, item)
        self._refresh_queue_table()
        self.queueTable.setCurrentCell(0, 0)
        # 如果没有在显示 立即发送
        if not self._is_showing:
            self._send_next()
        else:
            InfoBar.info(
                title=tr("common.tip"),
                content=tr("notification.will_display_next"),
                parent=self.window(),
                position=InfoBarPosition.TOP,
                duration=2000,
            )

    def _onDeleteTask(self):
        """删除选中任务"""
        row = self.queueTable.currentRow()
        if row < 0 or row >= len(self._queue):
            InfoBar.warning(
                title=tr("common.tip"),
                content=tr("notification.select_task_first"),
                parent=self.window(),
                position=InfoBarPosition.TOP,
                duration=2000,
            )
            return
        w = MessageBox(tr("common.confirm_delete"), tr("notification.confirm_delete_task"), self.window())
        w.yesButton.setText(tr("common.confirm"))
        w.cancelButton.setText(tr("common.cancel"))
        if w.exec():
            self._remove_from_queue(row)

    def _onShowConfig(self):
        """编辑选中任务配置"""
        row = self.queueTable.currentRow()
        if row < 0 or row >= len(self._queue):
            InfoBar.warning(
                title=tr("common.tip"),
                content=tr("notification.select_task_first"),
                parent=self.window(),
                position=InfoBarPosition.TOP,
                duration=2000,
            )
            return
        dlg = _ConfigEditDialog(self._queue[row], self.window())
        if dlg.exec():
            self._queue[row] = dlg.get_data()
            self._refresh_queue_table()
            InfoBar.success(
                title=tr("common.tip"),
                content=tr("notification.config_updated"),
                parent=self.window(),
                position=InfoBarPosition.TOP,
                duration=2000,
            )

    def _onScheduleClicked(self):
        """定时发送"""
        w = MessageBox(tr("notification.schedule_title"), "", self.window())
        w.setMinimumWidth(440)
        w.setMinimumHeight(320)

        content_widget = QWidget()
        content_layout = QVBoxLayout(content_widget)
        content_layout.setSpacing(16)
        content_layout.setContentsMargins(8, 8, 8, 8)

        date_label = BodyLabel(tr("notification.date_label"), content_widget)
        date_label.setStyleSheet("font-weight: 600;")
        date_picker = CalendarPicker(content_widget)
        date_picker.setDate(QDate.currentDate())
        date_picker.setMinimumWidth(360)
        content_layout.addWidget(date_label)
        content_layout.addWidget(date_picker)

        time_label = BodyLabel(tr("notification.time_label"), content_widget)
        time_label.setStyleSheet("font-weight: 600;")
        time_picker = TimePicker(content_widget)
        time_picker.setTime(QTime.currentTime().addSecs(60))
        time_picker.setMinimumWidth(360)
        content_layout.addWidget(time_label)
        content_layout.addWidget(time_picker)

        content_layout.addStretch()
        w.textLayout.addWidget(content_widget)
        w.yesButton.setText(tr("common.confirm"))
        w.cancelButton.setText(tr("common.cancel"))

        if not w.exec():
            return

        # 检查内容是否为空
        content = self.contentEdit.toPlainText().strip()
        if not content:
            InfoBar.warning(
                title=tr("common.tip"),
                content=tr("notification.empty_content"),
                parent=self.window(),
                position=InfoBarPosition.TOP,
                duration=2000,
            )
            return

        sel_date = date_picker.date
        sel_time = time_picker.time
        selected_datetime = QDateTime(sel_date, sel_time)
        current_datetime = QDateTime.currentDateTime()

        if selected_datetime <= current_datetime:
            InfoBar.error(
                title=tr("common.tip"),
                content=tr("notification.invalid_schedule_time"),
                parent=self.window(),
                position=InfoBarPosition.TOP,
                duration=3000,
            )
            return

        data = self._build_notif_data()
        data["_scheduled"] = True
        data["_fire_time"] = selected_datetime
        data["_uid"] = str(uuid.uuid4())
        self._queue.append(data)
        self._refresh_queue_table()

        InfoBar.success(
            title=tr("common.tip"),
            content=tr("notification.schedule_set").format(
                time=selected_datetime.toString("yyyy-MM-dd hh:mm:ss")
            ),
            parent=self.window(),
            position=InfoBarPosition.TOP,
            duration=3000,
        )
        logger.info(tr("notification.schedule_set_log").format(
            time=selected_datetime.toString("yyyy-MM-dd hh:mm:ss")
        ))

    def _check_scheduled(self):
        """10秒检查否有定时任务"""
        now = QDateTime.currentDateTime()
        fired = False
        for i, item in enumerate(self._queue[:]):
            if item.get("_scheduled"):
                fire_time = item.get("_fire_time")
                if fire_time and fire_time <= now:
                    item.pop("_scheduled", None)
                    item.pop("_fire_time", None)
                    self._queue.pop(i)
                    self._queue.insert(0, item)
                    self._refresh_queue_table()
                    fired = True
                    logger.info(f"定时任务到期: {item.get('content', '')}")
        if fired and not self._is_showing:
            self._send_next()

    def _onThemeChanged(self, theme: Theme):
        self.setStyleSheet(load_qss("notification.qss"))
