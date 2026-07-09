"""
通知页面
"""

import os
import logging
import sys

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

from PyQt6.QtCore import Qt, QTimer, pyqtSignal, QDateTime, QDate, QTime
from PyQt6.QtWidgets import (
    QDialog,
    QDialogButtonBox,
    QDateTimeEdit,
    QHBoxLayout,
    QLabel,
    QStackedWidget,
    QVBoxLayout,
    QWidget,
    QMessageBox,
)
from qfluentwidgets import (
    BodyLabel,
    CardWidget,
    Pivot,
    PushButton,
    ScrollArea,
    StrongBodyLabel,
    Theme,
    ComboBox,
    PrimaryPushButton,
    SpinBox,
    SwitchButton,
    TextEdit,
    CalendarPicker,
    TimePicker,
    InfoBar,
    InfoBarPosition,
)

from core.constants import load_qss
from core.utils import tr, TranslatableWidget, FUI
from core.notification import NotifType
from qfluentwidgets import MessageBox

logger = logging.getLogger("ClassLively.ui.notification")


class NotificationPage(ScrollArea, TranslatableWidget):
    """通知页面"""

    send_notification = pyqtSignal(dict)

    def __init__(self, parent=None):
        super().__init__(parent=parent)
        self.setObjectName("notification")

        self.scrollWidget = QWidget()
        self.scrollWidget.setObjectName("scrollWidget")
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)

        self.mainLayout = QVBoxLayout(self.scrollWidget)
        self.mainLayout.setContentsMargins(40, 20, 40, 20)
        self.mainLayout.setSpacing(16)

        self.titleLabel = StrongBodyLabel(tr("notification.title"), self.scrollWidget)
        self.titleLabel.setObjectName("notificationTitle")
        self.mainLayout.addWidget(self.titleLabel)

        self.pivot = Pivot(self)
        self.stackedWidget = QStackedWidget(self)

        self.notificationTab = QWidget()
        self._build_notification_tab()

        self.settingsTab = QWidget()
        self._build_settings_tab()

        self.stackedWidget.addWidget(self.notificationTab)
        self.stackedWidget.addWidget(self.settingsTab)

        self.pivot.addItem(
            routeKey="notificationTab",
            text=tr("notification.tab_notifications"),
            onClick=lambda: self.stackedWidget.setCurrentWidget(self.notificationTab),
        )
        self.pivot.addItem(
            routeKey="settingsTab",
            text=tr("notification.tab_settings"),
            onClick=lambda: self.stackedWidget.setCurrentWidget(self.settingsTab),
        )

        self.stackedWidget.setCurrentWidget(self.notificationTab)
        self.pivot.setCurrentItem("notificationTab")

        self.mainLayout.addWidget(self.pivot)
        self.mainLayout.addWidget(self.stackedWidget)
        self.mainLayout.addStretch()

        self.setStyleSheet(load_qss("notification.qss"))

        self._scheduled_timer = QTimer(self)
        self._scheduled_timer.setSingleShot(True)
        self._scheduled_timer.timeout.connect(self._sendScheduledNotification)

    def _build_notification_tab(self):
        layout = QVBoxLayout(self.notificationTab)
        layout.setContentsMargins(0, 12, 0, 0)
        layout.setSpacing(16)
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)

        # 通知类型选择
        type_card = CardWidget(self.notificationTab)
        type_layout = QVBoxLayout(type_card)
        type_layout.setContentsMargins(20, 16, 20, 16)
        type_layout.setSpacing(8)

        type_title = StrongBodyLabel(tr("notification.type_label"), type_card)
        type_layout.addWidget(type_title)

        type_row = QHBoxLayout()
        type_row.setSpacing(12)
        self.typeCombo = ComboBox(type_card)
        self.typeCombo.addItem(tr("notification.type_scroll"), userData=NotifType.SCROLL)
        self.typeCombo.addItem(tr("notification.type_corner"), userData=NotifType.CORNER)
        self.typeCombo.addItem(tr("notification.type_fullscreen"), userData=NotifType.FULLSCREEN)
        self.typeCombo.setCurrentIndex(0)
        self.typeCombo.setMinimumWidth(200)
        type_row.addWidget(self.typeCombo)
        type_row.addStretch()
        type_layout.addLayout(type_row)
        layout.addWidget(type_card)

        # 通知内容
        content_card = CardWidget(self.notificationTab)
        content_layout = QVBoxLayout(content_card)
        content_layout.setContentsMargins(20, 16, 20, 16)
        content_layout.setSpacing(8)

        content_title = StrongBodyLabel(tr("notification.content_label"), content_card)
        content_layout.addWidget(content_title)

        self.contentEdit = TextEdit(content_card)
        self.contentEdit.setPlaceholderText(tr("notification.content_placeholder"))
        self.contentEdit.setMaximumHeight(120)
        content_layout.addWidget(self.contentEdit)
        layout.addWidget(content_card)

        # 发送按钮
        time_card = CardWidget(self.notificationTab)
        time_layout = QVBoxLayout(time_card)
        time_layout.setContentsMargins(20, 16, 20, 16)
        time_layout.setSpacing(8)

        time_title = StrongBodyLabel(tr("notification.time_label"), time_card)
        time_layout.addWidget(time_title)

        time_row = QHBoxLayout()
        time_row.setSpacing(12)

        self.sendNowBtn = PrimaryPushButton(FUI.SEND, tr("notification.send_now"), time_card)
        self.sendNowBtn.clicked.connect(self._onSendNow)
        time_row.addWidget(self.sendNowBtn)

        self.scheduleBtn = PushButton(FUI.CALENDAR, tr("notification.schedule"), time_card)
        self.scheduleBtn.clicked.connect(self._onScheduleClicked)
        time_row.addWidget(self.scheduleBtn)
        time_row.addStretch()

        time_layout.addLayout(time_row)
        layout.addWidget(time_card)

        layout.addStretch()

    def _build_settings_tab(self):
        layout = QVBoxLayout(self.settingsTab)
        layout.setContentsMargins(0, 12, 0, 0)
        layout.setSpacing(16)
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)

        general_card = CardWidget(self.settingsTab)
        general_layout = QVBoxLayout(general_card)
        general_layout.setContentsMargins(20, 16, 20, 16)
        general_layout.setSpacing(12)

        general_title = StrongBodyLabel(tr("notification.settings_general"), general_card)
        general_layout.addWidget(general_title)

        enable_row = QHBoxLayout()
        enable_row.setSpacing(12)
        enable_label = BodyLabel(tr("notification.settings_enable"), general_card)
        self.enableSwitch = SwitchButton(general_card)
        self.enableSwitch.setOnText(tr("common.on"))
        self.enableSwitch.setOffText(tr("common.off"))
        enable_row.addWidget(enable_label)
        enable_row.addStretch()
        enable_row.addWidget(self.enableSwitch)
        general_layout.addLayout(enable_row)

        speed_row = QHBoxLayout()
        speed_row.setSpacing(12)
        speed_label = BodyLabel(tr("notification.settings_speed"), general_card)
        self.speedSpin = SpinBox(general_card)
        self.speedSpin.setRange(1, 20)
        self.speedSpin.setValue(5)
        speed_row.addWidget(speed_label)
        speed_row.addStretch()
        speed_row.addWidget(self.speedSpin)
        general_layout.addLayout(speed_row)

        duration_row = QHBoxLayout()
        duration_row.setSpacing(12)
        duration_label = BodyLabel(tr("notification.settings_duration"), general_card)
        self.durationSpin = SpinBox(general_card)
        self.durationSpin.setRange(1, 60)
        self.durationSpin.setValue(10)
        duration_row.addWidget(duration_label)
        duration_row.addStretch()
        duration_row.addWidget(self.durationSpin)
        general_layout.addLayout(duration_row)

        layout.addWidget(general_card)
        layout.addStretch()

    def _onSendNow(self):
        """立即发送通知"""
        if not self.enableSwitch.isChecked():
            w = MessageBox(tr("common.tip"), tr("notification.not_enabled"), self.window())
            w.exec()
            return

        content = self.contentEdit.toPlainText().strip()
        if not content:
            w = MessageBox(tr("common.tip"), tr("notification.empty_content"), self.window())
            w.exec()
            return
        notif_data = {
            "type": self.typeCombo.currentData(),
            "content": content,
            "speed": self.speedSpin.value(),
            "duration": self.durationSpin.value(),
        }
        logger.info(f"立即发送通知: {notif_data}")
        self.send_notification.emit(notif_data)

    def _onScheduleClicked(self):
        """定时发送通知"""
        w = MessageBox(tr("notification.schedule_title"), tr("notification.select_time"), self.window())
        w.setMinimumWidth(400)
        w.setMinimumHeight(300)
        
        content_widget = QWidget()
        content_layout = QVBoxLayout(content_widget)
        content_layout.setSpacing(16)
        content_layout.setContentsMargins(8, 8, 8, 8)
        
        # 日期
        date_label = BodyLabel(tr("notification.date_label"), content_widget)
        date_picker = CalendarPicker(content_widget)
        date_picker.setDate(QDate.currentDate())
        date_picker.setMinimumWidth(300)
        content_layout.addWidget(date_label)
        content_layout.addWidget(date_picker)
        
        # 时间
        time_label = BodyLabel(tr("notification.time_label"), content_widget)
        time_picker = TimePicker(content_widget)
        time_picker.setTime(QTime.currentTime().addSecs(60))
        time_picker.setMinimumWidth(300)
        content_layout.addWidget(time_label)
        content_layout.addWidget(time_picker)
        
        content_layout.addStretch()
        
        # 添加到 MessageBox
        w.textLayout.addWidget(content_widget)
        
        # 按钮
        w.yesButton.setText(tr("common.confirm"))
        w.cancelButton.setText(tr("common.cancel"))
        
        if not w.exec():
            return
        
        sel_date = date_picker.date()
        sel_time = time_picker.time()
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
        
        self._scheduled_notif_data = {
            "type": self.typeCombo.currentData(),
            "content": self.contentEdit.toPlainText().strip(),
            "speed": self.speedSpin.value(),
            "duration": self.durationSpin.value(),
        }
        
        msecs = current_datetime.msecsTo(selected_datetime)
        self._scheduled_timer.start(msecs)
        
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

    def _sendScheduledNotification(self):
        """到点发送通知"""
        if not self.enableSwitch.isChecked():
            logger.info(tr("notification.schedule_cancelled_disabled"))
            return

        notif_data = getattr(self, "_scheduled_notif_data", None)
        if not notif_data or not notif_data.get("content"):
            logger.warning(tr("notification.schedule_data_missing"))
            return

        logger.info(tr("notification.sending_scheduled").format(data=notif_data))
        self.send_notification.emit(notif_data)

    def _onThemeChanged(self, theme: Theme):
        self.setStyleSheet(load_qss("notification.qss"))