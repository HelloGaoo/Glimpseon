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
ClassIsland 联动页面
"""

import logging
import os
import sys

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

from PyQt6.QtCore import Qt
from PyQt6.QtGui import QPalette, QColor
from PyQt6.QtWidgets import QHBoxLayout, QLabel, QWidget
from qfluentwidgets import (
    BodyLabel, CardWidget, ExpandLayout,
    InfoBar, InfoBarPosition, isDarkTheme,
    PushButton, SettingCard,
    SettingCardGroup, SpinBox,
    StrongBodyLabel, SwitchSettingCard, Theme,
)
from core.config import cfg
from core.constants import load_qss
from core.utils import tr, TranslatableWidget, FUI
from core.linkage import LinkageBridge, ClassWidgetsBridge, LinkageState, TimeState
from .common import BaseScrollAreaInterface

logger = logging.getLogger("ClassLively.ui.linkage")


class SpinBoxSettingCard(SettingCard):
    """带 SpinBox 的设置卡片"""

    def __init__(self, configItem, icon, title, content=None, parent=None, min_value=1, max_value=100):
        super().__init__(icon, title, content, parent)

        self.configItem = configItem
        self.spinBox = SpinBox(self)
        self.spinBox.setRange(min_value, max_value)
        self.spinBox.setValue(configItem.value)
        self.hBoxLayout.addWidget(self.spinBox, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)
        self.spinBox.valueChanged.connect(self._onValueChanged)
        configItem.valueChanged.connect(self.setValue)

    def _onValueChanged(self, value):
        setattr(self.configItem, 'value', value)

    def setValue(self, value):
        self.spinBox.setValue(value)


class PathSettingCard(SettingCard):
    """路径预览卡片"""

    def __init__(self, configItem, icon, title, content=None, parent=None):
        super().__init__(icon, title, content, parent)

        self.configItem = configItem
        self.pathLabel = BodyLabel("")
        self.pathLabel.setWordWrap(True)
        self.pathLabel.setMinimumWidth(200)
        self.autoBtn = PushButton(FUI.FOLDER, tr("linkage.btn_auto_detect"))
        self.browseBtn = PushButton(FUI.FOLDER, tr("linkage.btn_browse"))
        for btn in (self.autoBtn, self.browseBtn):
            btn.setFixedHeight(32)

        h = QHBoxLayout()
        h.addWidget(self.pathLabel, 1)
        h.addWidget(self.autoBtn)
        h.addWidget(self.browseBtn)
        container = QWidget()
        container.setLayout(h)
        self.hBoxLayout.addWidget(container, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)

        value = cfg.get(configItem) if hasattr(cfg, 'get') else configItem.value
        self._set_path(str(value))
        configItem.valueChanged.connect(self.setValue)

    def set_auto_callback(self, callback):
        self.autoBtn.clicked.connect(callback)

    def setValue(self, value):
        self._set_path(value)

    def _set_path(self, path: str):
        if not path:
            self.pathLabel.setText(tr("linkage.path_empty"))
            self.pathLabel.setStyleSheet("color: #999;")
        elif os.path.isdir(path):
            self.pathLabel.setText(path)
            self.pathLabel.setStyleSheet("color: #30c361;")
        else:
            self.pathLabel.setText(path)
            self.pathLabel.setStyleSheet("color: #d13438;")


class PreviewCard(CardWidget):
    """实时状态预览卡片"""

    FIELDS = [
        ("state", "linkage.preview_state"),
        ("subject", "linkage.preview_subject"),
        ("teacher", "linkage.preview_teacher"),
        ("time_range", "linkage.preview_time_range"),
        ("left", "linkage.preview_left"),
        ("lesson_index", "linkage.preview_lesson_index"),
        ("next_class", "linkage.preview_next_class"),
        ("updated", "linkage.preview_updated"),
    ]

    def __init__(self, labels_dict, parent=None):
        super().__init__(parent)

        self.labels_dict = labels_dict

        self.setFixedHeight(260)
        layout = self.layout() if self.layout() else None
        if layout is None:
            from PyQt6.QtWidgets import QGridLayout
            layout = QGridLayout(self)
        layout.setContentsMargins(16, 12, 16, 12)
        layout.setVerticalSpacing(6)

        for i, (key, tr_key) in enumerate(self.FIELDS):
            lbl_key = BodyLabel(f"{tr(tr_key)}: ")
            lbl_key.setStyleSheet("font-weight: bold;")
            lbl_val = BodyLabel(tr("linkage.preview_waiting"))
            labels_dict[key] = lbl_val
            layout.addWidget(lbl_key, i, 0)
            layout.addWidget(lbl_val, i, 1)



class LinkagePage(BaseScrollAreaInterface, TranslatableWidget):
    """ClassIsland 联动设置页面"""

    def __init__(self, parent=None):
        super().__init__(tr("linkage.title"), parent, width=900, height=700)
        self.setObjectName("linkage")

        self._bridge = LinkageBridge(self)
        self.expandLayout = ExpandLayout(self.scrollWidget)

        # 连接状态组
        self.connGroup = SettingCardGroup(tr("linkage.group_connection"), self.scrollWidget)
        self.connStatusCard = SettingCard(
            FUI.LINK, tr("linkage.connection_status"), '', parent=self.connGroup
        )
        self._connStatusLabel = StrongBodyLabel(tr("linkage.status_disconnected"))
        self._connStatusLabel.setStyleSheet("color: #d13438; font-weight: bold;")
        status_widget = QWidget()
        status_layout = QHBoxLayout(status_widget)
        status_layout.setContentsMargins(0, 0, 0, 0)
        status_layout.addWidget(self._connStatusLabel)
        status_layout.addStretch()
        self.connStatusCard.hBoxLayout.addWidget(status_widget, 1)

        # 基本设置组
        self.basicGroup = SettingCardGroup(tr("linkage.group_basic"), self.scrollWidget)
        self.enableCard = SwitchSettingCard(
            FUI.PLAY, tr("linkage.enable"), '',
            configItem=cfg.linkageEnabled, parent=self.basicGroup
        )
        self.dataPathCard = PathSettingCard(
            cfg.linkageDataPath, FUI.FOLDER,
            tr("linkage.data_path"), tr("linkage.data_path_tip"),
            parent=self.basicGroup
        )
        self.pollIntervalCard = SpinBoxSettingCard(
            cfg.linkagePollInterval, FUI.HISTORY,
            tr("linkage.poll_interval"), '',
            parent=self.basicGroup, min_value=1, max_value=30
        )
        self.syncTimeCard = SwitchSettingCard(
            FUI.DATE_TIME, tr("linkage.sync_time_config"),
            tr("linkage.sync_time_config_tip"),
            configItem=cfg.linkageSyncTimeConfig, parent=self.basicGroup
        )

        # 实时预览组
        self.previewGroup = SettingCardGroup(tr("linkage.group_preview"), self.scrollWidget)
        self.previewLabels = {}
        self._previewCard = PreviewCard(self.previewLabels, parent=self.previewGroup)

        self.__initWidget()
        self.__connectSignalToSlot()

    # 初始化ui

    def __initWidget(self):
        self.__setQss()
        self.__initLayout()
        self.setup_translatable_ui()

    def __setQss(self):
        self.viewport().setObjectName("linkageViewport")
        self.setStyleSheet(load_qss('linkage.qss'))

    def _onThemeChanged(self, theme: Theme):
        """主题切换时重新加载样式"""
        self.__setQss()

    def __initLayout(self):
        self.titleLabel.move(60, 63)

        self.connGroup.addSettingCard(self.connStatusCard)
        self.basicGroup.addSettingCard(self.enableCard)
        self.basicGroup.addSettingCard(self.dataPathCard)
        self.basicGroup.addSettingCard(self.pollIntervalCard)
        self.basicGroup.addSettingCard(self.syncTimeCard)
        self.previewGroup.addSettingCard(self._previewCard)

        self.expandLayout.setSpacing(28)
        self.expandLayout.setContentsMargins(60, 10, 60, 0)
        self.expandLayout.addWidget(self.connGroup)
        self.expandLayout.addWidget(self.basicGroup)
        self.expandLayout.addWidget(self.previewGroup)


    def __connectSignalToSlot(self):
        # 控件信号
        self.dataPathCard.set_auto_callback(self._onAutoDetect)
        self.dataPathCard.browseBtn.clicked.connect(self._onBrowseDone)

        # Bridge 信号
        self._bridge.stateChanged.connect(self._updatePreview)
        self._bridge.connectedChanged.connect(self._onConnectedChanged)
        self._bridge.errorOccurred.connect(self._onBridgeError)
        self.enableCard.checkedChanged.connect(self._onEnableToggled)

        cfg.classWidgetsPollInterval.valueChanged.connect(
            lambda v: setattr(self._bridge, 'poll_interval', v)
        )

        cfg.linkagePollInterval.valueChanged.connect(
            lambda v: setattr(self._bridge, 'poll_interval', v)
        )

        # 主题切换
        cfg.themeChanged.connect(self._onThemeChanged)

        # 启动时的初始化（启用联动时）
        if cfg.linkageEnabled.value:
            self._bridge.set_data_path(cfg.linkageDataPath.value)
            self._bridge.poll_interval = cfg.linkagePollInterval.value
            if not cfg.linkageDataPath.value:
                self._onAutoDetect() 
            else:
                self._bridge.start()


    def _onAutoDetect(self):
        """自动检测按钮回调"""
        path = self._bridge.auto_detect()
        if path:
            cfg.linkageDataPath.value = path
            self.dataPathCard.setValue(path)
            InfoBar.success(
                title=tr("linkage.auto_detect_success"), content=path,
                parent=self, position=InfoBarPosition.TOP, duration=3000,
            )
            if cfg.linkageEnabled.value and not self._bridge.is_running:
                self._bridge.start()
        else:
            InfoBar.error(
                title=tr("linkage.auto_detect_failed"),
                content=tr("linkage.auto_detect_failed_tip"),
                parent=self, position=InfoBarPosition.TOP, duration=3000,
            )

    def _onBrowseDone(self):
        """手动选择目录后的回调"""
        path = cfg.linkageDataPath.value
        if path and os.path.isdir(os.path.join(path, "Profiles")):
            if not self._bridge.is_running and cfg.linkageEnabled.value:
                self._bridge.start()

    def _onBridgeError(self, msg: str):
        """Bridge 给的错误 / 路径重定向消息"""
        if msg.startswith("REDIRECT:"):
            new_path = msg[len("REDIRECT:"):]
            cfg.linkageDataPath.value = new_path
            self.dataPathCard.setValue(new_path)
            InfoBar.info(
                title=tr("linkage.title"),
                content=f"路径已切换: {new_path}",
                parent=self, position=InfoBarPosition.TOP, duration=3000,
            )

    def _onEnableToggled(self, enabled):
        """联动开关切换"""
        if enabled:
            self._bridge.set_data_path(cfg.linkageDataPath.value)
            self._bridge.poll_interval = cfg.linkagePollInterval.value
            if not cfg.linkageDataPath.value:
                self._onAutoDetect()
            else:
                self._bridge.start()
        else:
            self._bridge.stop()

    def _onConnectedChanged(self, connected):
        """更新连接状态显示"""
        if connected:
            self._connStatusLabel.setText(tr("linkage.status_connected"))
            self._connStatusLabel.setStyleSheet("color: #30c361; font-weight: bold;")
        else:
            self._connStatusLabel.setText(tr("linkage.status_disconnected"))
            self._connStatusLabel.setStyleSheet("color: #d13438; font-weight: bold;")

    def _updatePreview(self, state: LinkageState):
        """更新实时预览数据"""
        labels = self.previewLabels
        labels["state"].setText(TimeState.display_name(state.time_state))
        labels["subject"].setText(state.current_subject or "-")
        # 教师
        teacher = state.current_lesson.teacher_name if state.current_lesson else ""
        labels["teacher"].setText(teacher or "-")
        # 时间范围
        if state.current_lesson and state.current_lesson.start_time and state.current_lesson.end_time:
            labels["time_range"].setText(f"{state.current_lesson.start_time} - {state.current_lesson.end_time}")
        else:
            labels["time_range"].setText("-")
        # 剩余时间
        left = state.on_class_left or state.on_breaking_left or ""
        labels["left"].setText(left if left else "-")
        labels["lesson_index"].setText(
            str(state.current_lesson.index) if state.current_lesson and state.current_lesson.index >= 0 else "-"
        )
        if state.next_lesson:
            parts = [state.next_lesson.subject_name]
            if state.next_lesson.start_time:
                parts.append(state.next_lesson.start_time)
            if state.next_lesson.end_time:
                parts.append(state.next_lesson.end_time)
            labels["next_class"].setText(" - ".join(parts))
        else:
            labels["next_class"].setText("-")
        labels["updated"].setText(
            state.last_update.strftime("%H:%M:%S") if state.last_update else "-"
        )


    @property
    def bridge(self) -> LinkageBridge:
        return self._bridge


class ClassWidgetsPage(BaseScrollAreaInterface, TranslatableWidget):
    """ClassWidgets 联动设置页面"""

    def __init__(self, parent=None):
        super().__init__(tr("cw_linkage.title"), parent, width=900, height=700)
        self.setObjectName("cw_linkage")

        self._bridge = ClassWidgetsBridge(self)
        self.expandLayout = ExpandLayout(self.scrollWidget)

        # 连接状态组
        self.connGroup = SettingCardGroup(tr("linkage.group_connection"), self.scrollWidget)
        self.connStatusCard = SettingCard(
            FUI.LINK, tr("linkage.connection_status"), '', parent=self.connGroup
        )
        self._connStatusLabel = StrongBodyLabel(tr("linkage.status_disconnected"))
        self._connStatusLabel.setStyleSheet("color: #d13438; font-weight: bold;")
        status_widget = QWidget()
        status_layout = QHBoxLayout(status_widget)
        status_layout.setContentsMargins(0, 0, 0, 0)
        status_layout.addWidget(self._connStatusLabel)
        status_layout.addStretch()
        self.connStatusCard.hBoxLayout.addWidget(status_widget, 1)

        # 基本设置组
        self.basicGroup = SettingCardGroup(tr("linkage.group_basic"), self.scrollWidget)
        self.enableCard = SwitchSettingCard(
            FUI.PLAY, tr("cw_linkage.enable"), '',
            configItem=cfg.classWidgetsEnabled, parent=self.basicGroup
        )
        self.dataPathCard = PathSettingCard(
            cfg.classWidgetsDataPath, FUI.FOLDER,
            tr("cw_linkage.data_path"), tr("cw_linkage.data_path_tip"),
            parent=self.basicGroup
        )
        self.pollIntervalCard = SpinBoxSettingCard(
            cfg.classWidgetsPollInterval, FUI.HISTORY,
            tr("linkage.poll_interval"), '',
            parent=self.basicGroup, min_value=1, max_value=30
        )

        # 实时预览组
        self.previewGroup = SettingCardGroup(tr("linkage.group_preview"), self.scrollWidget)
        self.previewLabels = {}
        self._previewCard = PreviewCard(self.previewLabels, parent=self.previewGroup)

        self.__initWidget()
        self.__connectSignalToSlot()

    def __initWidget(self):
        self.__setQss()
        self.__initLayout()
        self.setup_translatable_ui()

    def __setQss(self):
        self.viewport().setObjectName("cwLinkageViewport")
        self.setStyleSheet(load_qss('cw_linkage.qss'))

    def _onThemeChanged(self, theme: Theme):
        """主题切换时重新加载样式"""
        self.__setQss()

    def __initLayout(self):
        self.titleLabel.move(60, 63)

        self.connGroup.addSettingCard(self.connStatusCard)
        self.basicGroup.addSettingCard(self.enableCard)
        self.basicGroup.addSettingCard(self.dataPathCard)
        self.basicGroup.addSettingCard(self.pollIntervalCard)
        self.previewGroup.addSettingCard(self._previewCard)

        self.expandLayout.setSpacing(28)
        self.expandLayout.setContentsMargins(60, 10, 60, 0)
        self.expandLayout.addWidget(self.connGroup)
        self.expandLayout.addWidget(self.basicGroup)
        self.expandLayout.addWidget(self.previewGroup)

    def __connectSignalToSlot(self):
        self.dataPathCard.set_auto_callback(self._onAutoDetect)
        self.dataPathCard.browseBtn.clicked.connect(self._onBrowseDone)

        self._bridge.stateChanged.connect(self._updatePreview)
        self._bridge.connectedChanged.connect(self._onConnectedChanged)
        self._bridge.errorOccurred.connect(self._onBridgeError)
        self.enableCard.checkedChanged.connect(self._onEnableToggled)

        cfg.themeChanged.connect(self._onThemeChanged)

        if cfg.classWidgetsEnabled.value:
            self._bridge.set_data_path(cfg.classWidgetsDataPath.value)
            self._bridge.poll_interval = cfg.classWidgetsPollInterval.value
            if not cfg.classWidgetsDataPath.value:
                self._onAutoDetect()
            else:
                self._bridge.start()

    def _onAutoDetect(self):
        path = self._bridge.auto_detect()
        if path:
            cfg.classWidgetsDataPath.value = path
            self.dataPathCard.setValue(path)
            InfoBar.success(
                title=tr("cw_linkage.auto_detect_success"), content=path,
                parent=self, position=InfoBarPosition.TOP, duration=3000,
            )
            if cfg.classWidgetsEnabled.value and not self._bridge.is_running:
                self._bridge.start()
        else:
            InfoBar.error(
                title=tr("cw_linkage.auto_detect_failed"),
                content=tr("cw_linkage.auto_detect_failed_tip"),
                parent=self, position=InfoBarPosition.TOP, duration=3000,
            )

    def _onBrowseDone(self):
        path = cfg.classWidgetsDataPath.value
        if path and os.path.isdir(os.path.join(path, "schedule")):
            if not self._bridge.is_running and cfg.classWidgetsEnabled.value:
                self._bridge.start()

    def _onBridgeError(self, msg: str):
        if msg.startswith("REDIRECT:"):
            new_path = msg[len("REDIRECT:"):]
            cfg.classWidgetsDataPath.value = new_path
            self.dataPathCard.setValue(new_path)
            InfoBar.info(
                title=tr("cw_linkage.title"),
                content=f"路径已切换: {new_path}",
                parent=self, position=InfoBarPosition.TOP, duration=3000,
            )

    def _onEnableToggled(self, enabled):
        if enabled:
            self._bridge.set_data_path(cfg.classWidgetsDataPath.value)
            self._bridge.poll_interval = cfg.classWidgetsPollInterval.value
            if not cfg.classWidgetsDataPath.value:
                self._onAutoDetect()
            else:
                self._bridge.start()
        else:
            self._bridge.stop()

    def _onConnectedChanged(self, connected):
        if connected:
            self._connStatusLabel.setText(tr("linkage.status_connected"))
            self._connStatusLabel.setStyleSheet("color: #30c361; font-weight: bold;")
        else:
            self._connStatusLabel.setText(tr("linkage.status_disconnected"))
            self._connStatusLabel.setStyleSheet("color: #d13438; font-weight: bold;")

    def _updatePreview(self, state: LinkageState):
        labels = self.previewLabels
        labels["state"].setText(TimeState.display_name(state.time_state))
        labels["subject"].setText(state.current_subject or "-")
        teacher = state.current_lesson.teacher_name if state.current_lesson else ""
        labels["teacher"].setText(teacher or "-")
        if state.current_lesson and state.current_lesson.start_time and state.current_lesson.end_time:
            labels["time_range"].setText(f"{state.current_lesson.start_time} - {state.current_lesson.end_time}")
        else:
            labels["time_range"].setText("-")
        left = state.on_class_left or state.on_breaking_left or ""
        labels["left"].setText(left if left else "-")
        labels["lesson_index"].setText(
            str(state.current_lesson.index) if state.current_lesson and state.current_lesson.index >= 0 else "-"
        )
        if state.next_lesson:
            parts = [state.next_lesson.subject_name]
            if state.next_lesson.start_time:
                parts.append(state.next_lesson.start_time)
            if state.next_lesson.end_time:
                parts.append(state.next_lesson.end_time)
            labels["next_class"].setText(" - ".join(parts))
        else:
            labels["next_class"].setText("-")
        labels["updated"].setText(
            state.last_update.strftime("%H:%M:%S") if state.last_update else "-"
        )

    @property
    def bridge(self) -> ClassWidgetsBridge:
        return self._bridge
