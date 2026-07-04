# ClassLively
# Copyright (C) 2026 HelloGaoo
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

"""
通知页面模块
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

import os

from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import (
    QHBoxLayout,
    QLabel,
    QStackedWidget,
    QVBoxLayout,
    QWidget,
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
    LineEdit,
    PrimaryPushButton,
    SpinBox,
    SwitchButton,
    TextEdit,
)

from core.constants import load_qss
from core.utils import tr, TranslatableWidget, FUI

logger = logging.getLogger("ClassLively.ui.notification")


class NotificationPage(ScrollArea, TranslatableWidget):
    """通知页面"""

    def __init__(self, parent=None):
        super().__init__(parent=parent)
        self.setObjectName("notification")

        self.scrollWidget = QWidget()
        self.scrollWidget.setObjectName("scrollWidget")
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)

        # 主布局
        self.mainLayout = QVBoxLayout(self.scrollWidget)
        self.mainLayout.setContentsMargins(40, 20, 40, 20)
        self.mainLayout.setSpacing(16)

        # 页面标题
        self.titleLabel = StrongBodyLabel(tr("notification.title"), self.scrollWidget)
        self.titleLabel.setObjectName("notificationTitle")
        self.mainLayout.addWidget(self.titleLabel)

        # ---- 顶部标签导航栏 ----
        self.pivot = Pivot(self)
        self.stackedWidget = QStackedWidget(self)

        # 通知标签页
        self.notificationTab = QWidget()
        self._build_notification_tab()

        # 设置标签页
        self.settingsTab = QWidget()
        self._build_settings_tab()

        self.stackedWidget.addWidget(self.notificationTab)
        self.stackedWidget.addWidget(self.settingsTab)

        # 注册 Pivot 路由
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

        # 默认选中通知标签
        self.stackedWidget.setCurrentWidget(self.notificationTab)
        self.pivot.setCurrentItem("notificationTab")

        # Pivot + StackedWidget 放入布局
        self.mainLayout.addWidget(self.pivot)
        self.mainLayout.addWidget(self.stackedWidget)
        self.mainLayout.addStretch()

        self.setStyleSheet(load_qss("notification.qss"))

    def _build_notification_tab(self):
        """构建「通知」标签页内容"""
        layout = QVBoxLayout(self.notificationTab)
        layout.setContentsMargins(0, 12, 0, 0)
        layout.setSpacing(16)
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)

        # --- 通知类型选择 ---
        type_card = CardWidget(self.notificationTab)
        type_layout = QVBoxLayout(type_card)
        type_layout.setContentsMargins(20, 16, 20, 16)
        type_layout.setSpacing(8)

        type_title = StrongBodyLabel(tr("notification.type_label"), type_card)
        type_layout.addWidget(type_title)

        type_row = QHBoxLayout()
        type_row.setSpacing(12)
        self.typeCombo = ComboBox(type_card)
        self.typeCombo.addItems([
            tr("notification.type_scroll"),
            tr("notification.type_corner"),
            tr("notification.type_fullscreen"),
        ])
        self.typeCombo.setMinimumWidth(200)
        type_row.addWidget(self.typeCombo)
        type_row.addStretch()
        type_layout.addLayout(type_row)
        layout.addWidget(type_card)

        # --- 通知内容 ---
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

        # --- 发送时间和发送按钮 ---
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
        time_row.addWidget(self.scheduleBtn)
        time_row.addStretch()

        time_layout.addLayout(time_row)
        layout.addWidget(time_card)

        # 底部预留
        layout.addStretch()

    def _build_settings_tab(self):
        """构建「设置」标签页内容"""
        layout = QVBoxLayout(self.settingsTab)
        layout.setContentsMargins(0, 12, 0, 0)
        layout.setSpacing(16)
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)

        # --- 通知开关 ---
        general_card = CardWidget(self.settingsTab)
        general_layout = QVBoxLayout(general_card)
        general_layout.setContentsMargins(20, 16, 20, 16)
        general_layout.setSpacing(12)

        general_title = StrongBodyLabel(tr("notification.settings_general"), general_card)
        general_layout.addWidget(general_title)

        # 启用通知
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

        # 滚动速度
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

        # 显示时长
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

        # 底部预留
        layout.addStretch()

    @staticmethod
    def _onSendNow():
        logger.info("发送通知 (未实现)")

    def _onThemeChanged(self, theme: Theme):
        self.setStyleSheet(load_qss("notification.qss"))
