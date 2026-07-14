# Glimpseon
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
设置窗口
"""

import json
import os
from datetime import datetime

from PyQt6.QtCore import Qt, QTimer
from PyQt6.QtGui import QPainter, QColor, QPen, QBrush, QFont
from PyQt6.QtWidgets import QApplication, QFileDialog, QHBoxLayout, QLabel, QVBoxLayout, QWidget
from qfluentwidgets import (
    ComboBoxSettingCard,
    CustomColorSettingCard,
    FluentWindow,
    InfoBar,
    LineEdit,
    MessageBox,
    PushButton,
    ScrollArea,
    SettingCard,
    SpinBox,
    SwitchButton,
    SwitchSettingCard,
    Theme,
    qconfig,
    setTheme,
    setThemeColor,
)

from core.config import cfg, default_cfg, ConfigItem, CONFIG_PATH
from core.constants import BASE_DIR, DATA_CONFIG, load_qss
from core.utils import _load_app_fonts, apply_fonts, tr, get_time_sync_service, FUI
from core.logger import log_dir


class LineEditSettingCard(SettingCard):
    """带 QLineEdit 的设置卡片 """

    def __init__(self, configItem, icon, title, content=None, parent=None):
        super().__init__(icon, title, content, parent)
        self.configItem = configItem
        self.lineEdit = LineEdit(self)

        self.hBoxLayout.addWidget(self.lineEdit, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)

        value = qconfig.get(configItem)
        self.lineEdit.setText(str(value))
        self.lineEdit.textChanged.connect(self.__onTextChanged)
        configItem.valueChanged.connect(self.setValue)

    def __onTextChanged(self, text):
        try:
            value = float(text)
            qconfig.set(self.configItem, value)
        except ValueError:
            pass

    def setValue(self, value):
        self.lineEdit.setText(str(value))


class SpinBoxSettingCard(SettingCard):
    def __init__(self, configItem, icon, title, content=None, parent=None,
                 min_value=1, max_value=100):
        super().__init__(icon, title, content, parent)
        self.configItem = configItem
        self.spinBox = SpinBox(self)
        self.spinBox.setRange(min_value, max_value)
        self.spinBox.setValue(qconfig.get(configItem))
        self.spinBox.setFixedWidth(140)
        self.hBoxLayout.addWidget(self.spinBox, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)
        self.spinBox.valueChanged.connect(self.__onValueChanged)
        configItem.valueChanged.connect(self.setValue)

    def __onValueChanged(self, value):
        qconfig.set(self.configItem, value)

    def setValue(self, value):
        self.spinBox.setValue(value)


class TextLineSettingCard(SettingCard):
    """带文本框输入的设置卡片"""

    def __init__(self, configItem, icon, title, content=None, parent=None):
        super().__init__(icon, title, content, parent)
        self.configItem = configItem
        self.lineEdit = LineEdit(self)
        self.lineEdit.setMinimumWidth(200)

        self.hBoxLayout.addWidget(self.lineEdit, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)

        self.lineEdit.setText(str(qconfig.get(configItem)))
        self.lineEdit.textChanged.connect(self.__onTextChanged)
        configItem.valueChanged.connect(self.setValue)

    def __onTextChanged(self, text):
        qconfig.set(self.configItem, text)

    def setValue(self, value):
        self.lineEdit.setText(str(value))


class SyncStatusSettingCard(SettingCard):
    """同步状态卡片"""

    def __init__(self, icon, title, content=None, parent=None):
        super().__init__(icon, title, content, parent)
        self.statusLabel = QLabel(tr("settings.precise_time_not_synced"))
        self.statusLabel.setStyleSheet("color: #999;")
        self.syncBtn = PushButton(FUI.SYNC, tr("settings.precise_time_sync_now"))
        self.syncBtn.setFixedHeight(32)

        h = QHBoxLayout()
        h.addWidget(self.statusLabel, 1)
        h.addWidget(self.syncBtn)
        container = QWidget()
        container.setLayout(h)
        self.hBoxLayout.addWidget(container, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)

    def set_status(self, sync_time: str):
        if sync_time:
            self.statusLabel.setText(
                tr("settings.precise_time_synced_at").format(time=sync_time))
            self.statusLabel.setStyleSheet("color: #30c361;")
        else:
            self.statusLabel.setText(tr("settings.precise_time_not_synced"))
            self.statusLabel.setStyleSheet("color: #999;")


class AutoOffsetSettingCard(SettingCard):
    """带开关+数值框的时间偏移增量卡片"""

    def __init__(self, switchConfigItem, spinConfigItem, icon, title,
                 content=None, parent=None):
        super().__init__(icon, title, content, parent)
        self.switchConfigItem = switchConfigItem
        self.spinConfigItem = spinConfigItem

        self.switchBtn = SwitchButton(self)
        self.switchBtn.setOnText("")
        self.switchBtn.setOffText("")
        self.switchBtn.setChecked(qconfig.get(switchConfigItem))

        self.spinBox = SpinBox(self)
        self.spinBox.setRange(-9999, 9999)
        self.spinBox.setValue(qconfig.get(spinConfigItem))
        self.spinBox.setFixedWidth(140)

        self.switchBtn.checkedChanged.connect(self.__onSwitchChanged)
        self.spinBox.valueChanged.connect(self.__onSpinChanged)
        switchConfigItem.valueChanged.connect(
            lambda v: self.switchBtn.setChecked(v))
        spinConfigItem.valueChanged.connect(
            lambda v: self.spinBox.setValue(v))

        h = QHBoxLayout()
        h.addWidget(self.spinBox)
        h.addSpacing(8)
        h.addWidget(self.switchBtn)
        container = QWidget()
        container.setLayout(h)
        self.hBoxLayout.addWidget(container, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)

    def __onSwitchChanged(self, checked):
        qconfig.set(self.switchConfigItem, checked)

    def __onSpinChanged(self, value):
        qconfig.set(self.spinConfigItem, value)


class ButtonSettingCard(SettingCard):
    """按钮设置卡片"""

    def __init__(self, icon, title, content=None, parent=None):
        super().__init__(icon, title, content, parent)
        self.button = PushButton(FUI.EDIT, tr("common.execute"), self)
        self.button.setFixedHeight(36)
        self.hBoxLayout.addWidget(self.button, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)


class DualButtonSettingCard(SettingCard):
    def __init__(self, icon, title, content=None, parent=None):
        super().__init__(icon, title, content, parent)
        self.button1 = PushButton(FUI.SAVE, tr("settings.export_button"), self)
        self.button1.setFixedHeight(32)
        self.button2 = PushButton(FUI.DOWNLOAD, tr("settings.import_button"), self)
        self.button2.setFixedHeight(32)
        button_layout = QHBoxLayout()
        button_layout.addWidget(self.button1)
        button_layout.addWidget(self.button2)
        button_layout.setSpacing(8)
        container = QWidget()
        container.setLayout(button_layout)
        self.hBoxLayout.addWidget(container, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)



class SettingsSubPage(ScrollArea):
    """设置子页面基类"""

    def __init__(self, title: str, parent=None):
        super().__init__(parent=parent)
        self.setObjectName(title.replace(" ", "_"))

        self.scrollWidget = QWidget()
        self.scrollWidget.setObjectName("scrollWidget")
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setViewportMargins(0, 120, 0, 20)

        self.viewport().setAutoFillBackground(False)
        self.scrollWidget.setAutoFillBackground(False)

        self.vBoxLayout = QVBoxLayout(self.scrollWidget)
        self.vBoxLayout.setSpacing(28)
        self.vBoxLayout.setContentsMargins(60, 10, 60, 0)

        self.titleLabel = QLabel(title, self)
        self.titleLabel.setObjectName("settingLabel")
        self.titleLabel.move(60, 63)


# ─────────────────────────── 通用 ───────────────────────────

class GeneralPage(SettingsSubPage):
    """通用设置页面"""

    def __init__(self, main_window, parent=None):
        super().__init__(tr("settings.general"), parent)
        self.main_window = main_window

        self.autoStartCard = SwitchSettingCard(
            FUI.PLAY,
            tr("wizard.auto_start"),
            tr("wizard.auto_start_desc"),
            configItem=cfg.autoStart,
            parent=self.scrollWidget,
        )
        self.autoOpenOnIdleCard = SwitchSettingCard(
            FUI.VIEW,
            tr("wizard.auto_open_idle"),
            tr("wizard.auto_open_idle_desc"),
            configItem=cfg.autoOpenOnIdle,
            parent=self.scrollWidget,
        )
        self.idleMinutesCard = SpinBoxSettingCard(
            cfg.idleMinutes,
            FUI.HISTORY,
            tr("settings.idle_minutes"),
            tr("settings.idle_minutes_desc"),
            parent=self.scrollWidget,
            min_value=1,
            max_value=60,
        )
        self.autoOpenMaximizeCard = SwitchSettingCard(
            FUI.FULL_SCREEN,
            tr("wizard.auto_open_maximize"),
            tr("wizard.auto_open_maximize_desc"),
            configItem=cfg.autoOpenMaximize,
            parent=self.scrollWidget,
        )

        self.vBoxLayout.addWidget(self.autoStartCard)
        self.vBoxLayout.addWidget(self.autoOpenOnIdleCard)
        self.vBoxLayout.addWidget(self.idleMinutesCard)
        self.vBoxLayout.addWidget(self.autoOpenMaximizeCard)
        self.vBoxLayout.addStretch()


# ─────────────────────────── 时间 ───────────────────────────

class TimePage(SettingsSubPage):
    """时间设置页面"""

    def __init__(self, main_window, parent=None):
        super().__init__(tr("settings.time"), parent)
        self.main_window = main_window

        self.usePreciseTimeCard = SwitchSettingCard(
            FUI.DATE_TIME,
            tr("settings.use_precise_time"),
            tr("settings.use_precise_time_desc"),
            configItem=cfg.usePreciseTime,
            parent=self.scrollWidget,
        )
        self.timeServerCard = TextLineSettingCard(
            cfg.timeServer,
            FUI.CLOUD,
            tr("settings.time_server"),
            tr("settings.time_server_desc"),
            parent=self.scrollWidget,
        )
        self.timeSyncStatusCard = SyncStatusSettingCard(
            FUI.UPDATE,
            tr("settings.time_sync_status"),
            tr("settings.time_sync_status_desc"),
            parent=self.scrollWidget,
        )
        self.timeOffsetCard = SpinBoxSettingCard(
            cfg.timeOffset,
            FUI.ZOOM,
            tr("settings.time_offset"),
            tr("settings.time_offset_desc"),
            parent=self.scrollWidget,
            min_value=-9999,
            max_value=9999,
        )
        self.autoOffsetCard = AutoOffsetSettingCard(
            cfg.autoTimeOffsetEnabled,
            cfg.autoTimeOffsetIncrement,
            FUI.ADD,
            tr("settings.auto_time_offset"),
            tr("settings.auto_time_offset_desc"),
            parent=self.scrollWidget,
        )

        self.vBoxLayout.addWidget(self.usePreciseTimeCard)
        self.vBoxLayout.addWidget(self.timeServerCard)
        self.vBoxLayout.addWidget(self.timeSyncStatusCard)
        self.vBoxLayout.addWidget(self.timeOffsetCard)
        self.vBoxLayout.addWidget(self.autoOffsetCard)
        self.vBoxLayout.addStretch()

        self.__connectSignalToSlot()
        self.__initAutoSyncTimer()

    def __connectSignalToSlot(self):
        self.timeSyncStatusCard.syncBtn.clicked.connect(self.__onManualSync)
        cfg.usePreciseTime.valueChanged.connect(self.__onUsePreciseTimeChanged)
        self.__updateSyncStatus()

    def __initAutoSyncTimer(self):
        self._autoSyncTimer = QTimer(self)
        self._autoSyncTimer.setInterval(5 * 60 * 1000)
        self._autoSyncTimer.timeout.connect(self.__onManualSync)
        if cfg.usePreciseTime.value:
            self._autoSyncTimer.start()

    def __onManualSync(self):
        from PyQt6.QtCore import QThread, pyqtSignal

        class SyncWorker(QThread):
            finished = pyqtSignal(bool, str)

            def run(self):
                service = get_time_sync_service()
                server = cfg.timeServer.value
                ok = service.sync(server)
                sync_str = ""
                if ok and service.last_sync_time:
                    sync_str = service.last_sync_time.strftime("%H:%M:%S")
                    cfg.lastSyncTime.value = sync_str
                self.finished.emit(ok, sync_str)

        self._sync_worker = SyncWorker()
        self._sync_worker.finished.connect(self._onSyncFinished)
        self.timeSyncStatusCard.syncBtn.setEnabled(False)
        self.timeSyncStatusCard.syncBtn.setText(tr("settings.precise_time_syncing"))
        self._sync_worker.start()

    def _onSyncFinished(self, ok: bool, sync_str: str):
        self.timeSyncStatusCard.syncBtn.setEnabled(True)
        self.timeSyncStatusCard.syncBtn.setText(tr("settings.precise_time_sync_now"))
        self.__updateSyncStatus()
        if ok:
            InfoBar.success(
                tr("wizard.success_title"),
                tr("settings.precise_time_sync_success").format(time=sync_str),
                duration=3000,
                parent=self,
            )
        else:
            service = get_time_sync_service()
            err_msg = service.last_error or tr("settings.precise_time_sync_failed")
            InfoBar.error(tr("dialog.error"), err_msg, duration=5000, parent=self)

    def __onUsePreciseTimeChanged(self, enabled: bool):
        self.__updateSyncStatus()
        if enabled and cfg.usePreciseTime.value:
            self.__onManualSync()
            self._autoSyncTimer.start()
        else:
            self._autoSyncTimer.stop()

    def __updateSyncStatus(self):
        sync_time = cfg.lastSyncTime.value
        self.timeSyncStatusCard.set_status(sync_time)


# ─────────────────────────── 外观 ───────────────────────────

class AppearancePage(SettingsSubPage):
    """外观设置页面"""

    def __init__(self, main_window, parent=None):
        super().__init__(tr("settings.appearance"), parent)
        self.main_window = main_window

        self.themeCard = ComboBoxSettingCard(
            cfg.themeMode,
            FUI.BRUSH,
            tr("wizard.theme_mode"),
            tr("wizard.theme_mode_desc"),
            texts=[tr("wizard.theme_light"), tr("wizard.theme_dark"), tr("wizard.theme_system")],
            parent=self.scrollWidget,
        )
        self.themeColorCard = CustomColorSettingCard(
            cfg.themeColor,
            FUI.PALETTE,
            tr("wizard.primary_color"),
            tr("wizard.primary_color_desc"),
            parent=self.scrollWidget,
        )
        self.languageCard = ComboBoxSettingCard(
            cfg.language,
            FUI.LANGUAGE,
            tr("settings.language"),
            tr("settings.language_desc"),
            texts=[tr("settings.lang_zh_cn"), tr("settings.lang_zh_tw"), "English", "Auto"],
            parent=self.scrollWidget,
        )

        self.vBoxLayout.addWidget(self.themeCard)
        self.vBoxLayout.addWidget(self.themeColorCard)
        self.vBoxLayout.addWidget(self.languageCard)
        self.vBoxLayout.addStretch()

        self.__connectSignalToSlot()

    def __connectSignalToSlot(self):
        cfg.themeChanged.connect(self.__onThemeChanged)
        self.themeColorCard.colorChanged.connect(setThemeColor)
        cfg.appRestartSig.connect(self.__showRestartTooltip)

    def __onThemeChanged(self, theme: Theme):
        qss = load_qss('setting.qss')
        if qss:
            self.setStyleSheet(qss)

    def __showRestartTooltip(self):
        InfoBar.warning(
            "",
            tr("settings.restart_required"),
            duration=5000,
            parent=self.window(),
        )


# ─────────────────────────── 日志 ───────────────────────────

class LogPage(SettingsSubPage):
    """日志设置页面"""

    def __init__(self, main_window, parent=None):
        super().__init__(tr("settings.log"), parent)
        self.main_window = main_window

        self.disableLogCard = SwitchSettingCard(
            FUI.CLOSE,
            tr("settings.disable_log"),
            tr("settings.disable_log_desc"),
            configItem=cfg.disableLog,
            parent=self.scrollWidget,
        )
        self.logLevelCard = ComboBoxSettingCard(
            cfg.logLevel,
            FUI.INFO,
            tr("settings.log_level"),
            tr("settings.log_level_desc"),
            texts=["Debug", "Info", "Warning", "Error"],
            parent=self.scrollWidget,
        )
        self.logMaxCountCard = SpinBoxSettingCard(
            cfg.logMaxCount,
            FUI.INFO,
            tr("settings.log_max_count"),
            tr("settings.log_max_count_desc"),
            parent=self.scrollWidget,
            min_value=10,
            max_value=500,
        )
        self.logMaxDaysCard = SpinBoxSettingCard(
            cfg.logMaxDays,
            FUI.INFO,
            tr("settings.log_max_days"),
            tr("settings.log_max_days_desc"),
            parent=self.scrollWidget,
            min_value=30,
            max_value=365,
        )
        self.clearLogCard = ButtonSettingCard(
            FUI.DELETE,
            tr("settings.clear_log"),
            tr("settings.clear_log_desc"),
            parent=self.scrollWidget,
        )
        self.clearLogCard.button.setText(tr("settings.clear_log_button"))

        self.vBoxLayout.addWidget(self.disableLogCard)
        self.vBoxLayout.addWidget(self.logLevelCard)
        self.vBoxLayout.addWidget(self.logMaxCountCard)
        self.vBoxLayout.addWidget(self.logMaxDaysCard)
        self.vBoxLayout.addWidget(self.clearLogCard)
        self.vBoxLayout.addStretch()

        self.__connectSignalToSlot()

    def __connectSignalToSlot(self):
        self.disableLogCard.checkedChanged.connect(self.__onDisableLogChanged)
        self.clearLogCard.button.clicked.connect(self.__clearLog)
        self.__onDisableLogChanged(cfg.disableLog.value)

    def __onDisableLogChanged(self, disabled):
        self.logLevelCard.setEnabled(not disabled)
        self.logMaxCountCard.setEnabled(not disabled)
        self.logMaxDaysCard.setEnabled(not disabled)

    def __clearLog(self):
        msgBox = MessageBox(
            tr("settings.clear_log"),
            tr("settings.clear_log_confirm"),
            self.window(),
        )
        msgBox.yesButton.setText(tr("dialog.confirm"))
        msgBox.cancelButton.setText(tr("dialog.cancel"))
        if msgBox.exec():
            try:
                if os.path.exists(log_dir):
                    log_files = []
                    for file in os.listdir(log_dir):
                        if file.endswith(".log"):
                            file_path = os.path.join(log_dir, file)
                            mtime = os.path.getmtime(file_path)
                            log_files.append((mtime, file))
                    log_files.sort()
                    current_log_file = log_files[-1][1] if log_files else None
                    deleted_count = 0
                    for file in os.listdir(log_dir):
                        if file.endswith(".log") and file != current_log_file:
                            try:
                                os.remove(os.path.join(log_dir, file))
                                deleted_count += 1
                            except Exception:
                                pass
                    if deleted_count > 0:
                        InfoBar.success(
                            tr("wizard.success_title"),
                            tr("settings.clear_log_success").format(count=deleted_count),
                            duration=5000,
                            parent=self,
                        )
                    else:
                        InfoBar.info(
                            tr("common.tip"),
                            tr("settings.no_logs_to_clear"),
                            duration=5000,
                            parent=self,
                        )
                else:
                    InfoBar.info(
                        tr("common.tip"),
                        tr("settings.log_dir_not_exist"),
                        duration=5000,
                        parent=self,
                    )
            except Exception as e:
                InfoBar.error(
                    tr("dialog.error"),
                    tr("settings.clear_log_failed").format(error=str(e)),
                    duration=5000,
                    parent=self,
                )


# ─────────────────────────── 高级 ───────────────────────────

class AdvancedPage(SettingsSubPage):
    """高级设置页面"""

    def __init__(self, main_window, parent=None):
        super().__init__(tr("settings.advanced"), parent)
        self.main_window = main_window

        self.closeActionCard = ComboBoxSettingCard(
            cfg.closeAction,
            FUI.SETTING,
            tr("settings.close_action"),
            tr("settings.close_action_desc"),
            texts=[tr("settings.minimize_to_tray"), tr("settings.close_directly")],
            parent=self.scrollWidget,
        )

        self.allowMultipleInstancesCard = SwitchSettingCard(
            FUI.SYNC,
            tr("settings.allow_multiple_instances"),
            tr("settings.allow_multiple_instances_desc"),
            configItem=cfg.allowMultipleInstances,
            parent=self.scrollWidget,
        )

        self.enableGpuAccelerationCard = SwitchSettingCard(
            FUI.VIDEO,
            tr("settings.gpu_acceleration"),
            tr("settings.gpu_acceleration_desc"),
            configItem=cfg.enableGpuAcceleration,
            parent=self.scrollWidget,
        )

        self.configIOCard = DualButtonSettingCard(
            FUI.SYNC,
            tr("settings.config_import_export"),
            tr("settings.config_import_export_desc"),
            parent=self.scrollWidget,
        )

        self.resetDefaultCard = ButtonSettingCard(
            FUI.SETTING,
            tr("settings.reset_default"),
            tr("settings.reset_default_desc"),
            parent=self.scrollWidget,
        )
        self.resetDefaultCard.button.setText(tr("settings.reset_default_button"))

        self.debugModeCard = SwitchSettingCard(
            FUI.CODE,
            tr("settings.debug_mode"),
            tr("settings.debug_mode_desc"),
            configItem=cfg.debugMode,
            parent=self.scrollWidget,
        )

        self.vBoxLayout.addWidget(self.closeActionCard)
        self.vBoxLayout.addWidget(self.allowMultipleInstancesCard)
        self.vBoxLayout.addWidget(self.enableGpuAccelerationCard)
        self.vBoxLayout.addWidget(self.configIOCard)
        self.vBoxLayout.addWidget(self.resetDefaultCard)
        self.vBoxLayout.addWidget(self.debugModeCard)
        self.vBoxLayout.addStretch()

        self.__connectSignalToSlot()

    def __connectSignalToSlot(self):
        self.resetDefaultCard.button.clicked.connect(self.__resetDefaultSettings)
        self.configIOCard.button1.clicked.connect(self.__exportConfig)
        self.configIOCard.button2.clicked.connect(self.__importConfig)

    def __resetDefaultSettings(self):
        msgBox = MessageBox(
            tr("settings.reset_default"),
            tr("settings.reset_default_confirm"),
            self.window(),
        )
        msgBox.yesButton.setText(tr("dialog.confirm"))
        msgBox.cancelButton.setText(tr("dialog.cancel"))
        if msgBox.exec():
            try:
                config_path = CONFIG_PATH
                if os.path.exists(config_path):
                    os.remove(config_path)
                config_dir = DATA_CONFIG
                if not os.path.exists(config_dir):
                    os.makedirs(config_dir)
                default_config = default_cfg()
                with open(config_path, "w", encoding="utf-8") as f:
                    json.dump(default_config, f, ensure_ascii=False, indent=4)
                qconfig.load(config_path, cfg)
                self._refreshAllConfigUI()
                InfoBar.success(
                    tr("wizard.success_title"),
                    tr("settings.reset_success"),
                    duration=5000,
                    parent=self,
                )
            except Exception as e:
                InfoBar.error(
                    tr("dialog.error"),
                    tr("settings.reset_failed").format(error=str(e)),
                    duration=5000,
                    parent=self,
                )

    def __exportConfig(self):
        try:
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            default_filename = f"Glimpseon_Config_{timestamp}.json"
            file_path, _ = QFileDialog.getSaveFileName(
                self, tr("settings.export_config"), default_filename, tr("settings.json_filter")
            )
            if not file_path:
                return
            if os.path.exists(CONFIG_PATH):
                import shutil
                shutil.copy2(CONFIG_PATH, file_path)
                InfoBar.success(
                    tr("wizard.success_title"),
                    tr("settings.export_success").format(path=file_path),
                    duration=5000,
                    parent=self,
                )
            else:
                InfoBar.warning(
                    tr("common.tip"),
                    tr("settings.config_not_exist_export"),
                    duration=5000,
                    parent=self,
                )
        except Exception as e:
            InfoBar.error(
                tr("dialog.error"),
                tr("settings.export_failed").format(error=str(e)),
                duration=5000,
                parent=self,
            )

    def __importConfig(self):
        try:
            file_path, _ = QFileDialog.getOpenFileName(
                self, tr("settings.import_config"), "", tr("settings.json_filter")
            )
            if not file_path:
                return
            if not os.path.exists(file_path):
                InfoBar.warning(
                    tr("common.tip"),
                    tr("settings.selected_file_not_exist"),
                    duration=5000,
                    parent=self,
                )
                return
            with open(file_path, "r", encoding="utf-8") as f:
                imported_config = json.load(f)
            if not isinstance(imported_config, dict):
                InfoBar.error(
                    tr("dialog.error"),
                    tr("settings.config_format_error"),
                    duration=5000,
                    parent=self,
                )
                return
            msgBox = MessageBox(
                tr("settings.import_config"),
                tr("settings.import_config_confirm"),
                self.window(),
            )
            msgBox.yesButton.setText(tr("dialog.confirm"))
            msgBox.cancelButton.setText(tr("dialog.cancel"))
            if not msgBox.exec():
                return
            config_dir = DATA_CONFIG
            if not os.path.exists(config_dir):
                os.makedirs(config_dir)
            import shutil
            if os.path.exists(CONFIG_PATH):
                backup_path = CONFIG_PATH + ".backup"
                shutil.copy2(CONFIG_PATH, backup_path)
            with open(CONFIG_PATH, "w", encoding="utf-8") as f:
                json.dump(imported_config, f, ensure_ascii=False, indent=4)
            qconfig.load(CONFIG_PATH, cfg)
            self._refreshAllConfigUI()
            InfoBar.success(
                tr("wizard.success_title"),
                tr("settings.import_success").format(path=file_path),
                duration=5000,
                parent=self,
            )
        except json.JSONDecodeError:
            InfoBar.error(
                tr("dialog.error"),
                tr("settings.config_json_parse_error"),
                duration=5000,
                parent=self,
            )
        except Exception as e:
            InfoBar.error(
                tr("dialog.error"),
                tr("settings.import_failed").format(error=str(e)),
                duration=5000,
                parent=self,
            )

    def _refreshAllConfigUI(self):
        """刷新所有配置项的UI和主窗口组件"""
        for attr_name in dir(cfg):
            if not attr_name.startswith("_"):
                attr = getattr(cfg, attr_name)
                if isinstance(attr, ConfigItem) and hasattr(attr, "valueChanged"):
                    attr.valueChanged.emit(attr.value)

        mw = self.main_window
        mw.refresh_quick_launch()
        mw.refresh_clock()
        mw.refresh_poetry()
        mw.refresh_weather()
        mw.refresh_countdown()
        if hasattr(mw, "updateSchoolInfo"):
            mw.updateSchoolInfo()
        if hasattr(mw, "updateSchoolInfoStyle"):
            mw.updateSchoolInfoStyle()
        mw.refresh_school_info_position()
        mw.refresh_clock_position()
        mw.refresh_poetry_position()
        mw.refresh_weather_position()
        mw.refresh_countdown_position()

        app = QApplication.instance()
        if app:
            font_loaded = _load_app_fonts()
            apply_fonts(app, use_harmonyos=font_loaded)
        current_theme = cfg.themeMode.value
        setTheme(current_theme)
        cfg.themeChanged.emit(current_theme)


# ─────────────────────────── 网格 ───────────────────────────

class _GridPreviewWidget(QWidget):
    """网格预览组件"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setFixedSize(280, 120)
        self.short_side_cells = qconfig.get(cfg.gridShortSideCells)
        self.inset_percent = qconfig.get(cfg.gridInsetPercent)

        cfg.gridShortSideCells.valueChanged.connect(self._on_short_side_cells_changed)
        cfg.gridInsetPercent.valueChanged.connect(self._on_inset_percent_changed)

    def _on_short_side_cells_changed(self, value):
        self.short_side_cells = value
        self.update()

    def _on_inset_percent_changed(self, value):
        self.inset_percent = value
        self.update()

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        # 背景
        painter.fillRect(self.rect(), QColor(40, 40, 40))

        # 计算网格参数
        w, h = self.width(), self.height()
        cells = max(1, self.short_side_cells)
        short_side_px = max(1, min(w, h))
        base_cell = short_side_px / cells
        inset_ratio = max(0, min(30, self.inset_percent)) / 100.0
        inset = max(0, min(80, base_cell * inset_ratio))

        gap_ratio = 0.12

        # 短边格子数
        short_side = min(w, h) - 2 * inset
        cell_size = short_side / (self.short_side_cells + max(0, self.short_side_cells - 1) * gap_ratio)
        gap_px = cell_size * gap_ratio
        pitch = cell_size + gap_px

        # 右/下边缘位置 左对齐
        right_edge = w - inset
        bottom_edge = h - inset

        # 绘制网格线
        painter.setPen(QPen(QColor(100, 100, 100), 1))

        # x
        i = 0
        while True:
            x = inset + i * pitch
            if x > right_edge:
                break
            painter.drawLine(int(x), int(inset), int(x), int(bottom_edge))
            i += 1
        painter.drawLine(int(right_edge), int(inset), int(right_edge), int(bottom_edge))

        # y
        i = 0
        while True:
            y = inset + i * pitch
            if y > bottom_edge:
                break
            painter.drawLine(int(inset), int(y), int(right_edge), int(y))
            i += 1
        painter.drawLine(int(inset), int(bottom_edge), int(right_edge), int(bottom_edge))


class _CornerRadiusPreviewWidget(QWidget):
    """卡片预览组"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setFixedSize(280, 120)
        self.card_radius = qconfig.get(cfg.componentCardRadius)
        self.card_opacity = qconfig.get(cfg.componentCardOpacity)

        cfg.componentCardRadius.valueChanged.connect(self._on_card_radius_changed)
        cfg.componentCardOpacity.valueChanged.connect(self._on_card_opacity_changed)

    def _on_card_radius_changed(self, value):
        self.card_radius = value
        self.update()

    def _on_card_opacity_changed(self, value):
        self.card_opacity = value
        self.update()

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        # 背景
        painter.fillRect(self.rect(), QColor(40, 40, 40))

        # 绘制条纹背景
        w, h = self.width(), self.height()
        card_w, card_h = 120, 80
        card_x = (w - card_w) // 2
        card_y = (h - card_h) // 2

        stripe_pen = QPen(QColor(60, 60, 60), 6)
        painter.setPen(stripe_pen)
        for i in range(-card_h, card_w + card_h, 12):
            painter.drawLine(card_x + i, card_y, card_x + i + card_h, card_y + card_h)

        # 绘制卡片背景
        alpha = int(self.card_opacity / 100.0 * 255)
        painter.setBrush(QBrush(QColor(60, 60, 60, alpha)))
        painter.setPen(QPen(QColor(80, 80, 80), 1))
        painter.drawRoundedRect(card_x, card_y, card_w, card_h, self.card_radius, self.card_radius)

        # 绘制示例文字
        painter.setPen(QColor(200, 200, 200))
        font = QFont("Arial", 10)
        painter.setFont(font)
        painter.drawText(card_x, card_y, card_w, card_h, Qt.AlignmentFlag.AlignCenter, "Preview")


class GridPage(SettingsSubPage):
    """网格设置页面"""

    def __init__(self, main_window, parent=None):
        super().__init__(tr("settings.grid.title"), parent)
        self.main_window = main_window

        # 网格预览  卡片预览
        self.gridPreviewWidget = _GridPreviewWidget(self.scrollWidget)
        self.cornerRadiusPreviewWidget = _CornerRadiusPreviewWidget(self.scrollWidget)

        preview_row = QWidget(self.scrollWidget)
        preview_layout = QHBoxLayout(preview_row)
        preview_layout.setContentsMargins(0, 10, 0, 10)
        preview_layout.addStretch()

        # 网格预览项
        grid_item = QWidget(self.scrollWidget)
        grid_item_layout = QVBoxLayout(grid_item)
        grid_item_layout.setContentsMargins(0, 0, 0, 0)
        grid_item_layout.setSpacing(4)
        grid_item_layout.addWidget(self.gridPreviewWidget, alignment=Qt.AlignmentFlag.AlignCenter)
        grid_label = QLabel(tr("settings.grid.preview"), self.scrollWidget)
        grid_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        grid_item_layout.addWidget(grid_label)
        preview_layout.addWidget(grid_item)

        preview_layout.addSpacing(24)

        # 卡片预览项
        card_item = QWidget(self.scrollWidget)
        card_item_layout = QVBoxLayout(card_item)
        card_item_layout.setContentsMargins(0, 0, 0, 0)
        card_item_layout.setSpacing(4)
        card_item_layout.addWidget(self.cornerRadiusPreviewWidget, alignment=Qt.AlignmentFlag.AlignCenter)
        card_label = QLabel(tr("settings.grid.cornerRadius_preview"), self.scrollWidget)
        card_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        card_item_layout.addWidget(card_label)
        preview_layout.addWidget(card_item)

        preview_layout.addStretch()

        # 设置卡片
        self.shortSideCellsCard = SpinBoxSettingCard(
            cfg.gridShortSideCells,
            FUI.TILES,
            tr("settings.grid.short_side_cells"),
            tr("settings.grid.short_side_cells_desc"),
            parent=self.scrollWidget,
            min_value=6,
            max_value=96,
        )
        self.insetPercentCard = SpinBoxSettingCard(
            cfg.gridInsetPercent,
            FUI.LAYOUT,
            tr("settings.grid.inset_percent"),
            tr("settings.grid.inset_percent_desc"),
            parent=self.scrollWidget,
            min_value=0,
            max_value=30,
        )
        self.componentCardOpacityCard = SpinBoxSettingCard(
            cfg.componentCardOpacity,
            FUI.PALETTE,
            tr("settings.grid.component_card_opacity"),
            tr("settings.grid.component_card_opacity_desc"),
            parent=self.scrollWidget,
            min_value=0,
            max_value=100,
        )
        self.componentCardRadiusCard = SpinBoxSettingCard(
            cfg.componentCardRadius,
            FUI.EDIT,
            tr("settings.grid.component_card_radius"),
            tr("settings.grid.component_card_radius_desc"),
            parent=self.scrollWidget,
            min_value=0,
            max_value=29,
        )

        self.vBoxLayout.addWidget(preview_row)
        self.vBoxLayout.addWidget(self.shortSideCellsCard)
        self.vBoxLayout.addWidget(self.insetPercentCard)
        self.vBoxLayout.addWidget(self.componentCardOpacityCard)
        self.vBoxLayout.addWidget(self.componentCardRadiusCard)
        self.vBoxLayout.addStretch()


class SettingsWindow(FluentWindow):
    """设置窗口"""

    def __init__(self, main_window):
        super().__init__()
        self.main_window = main_window
        self.setObjectName("setting")
        self.resize(1150, 750)

        # 窗口置顶，隐藏最小化和最大化按钮
        self.setWindowFlags(
            (self.windowFlags() | Qt.WindowType.WindowStaysOnTopHint)
            & ~Qt.WindowType.WindowMinimizeButtonHint
            & ~Qt.WindowType.WindowMaximizeButtonHint
        )

        self._initPages()
        self._initNavigation()
        self._applyTheme()

        # 显示在屏幕中央
        self._centerOnScreen()

    def _centerOnScreen(self):
        screen = QApplication.primaryScreen()
        if screen:
            center = screen.availableGeometry().center()
            self.move(center.x() - self.width() // 2, center.y() - self.height() // 2)

    def _initPages(self):
        self.generalPage = GeneralPage(self.main_window, self)
        self.generalPage.setObjectName("generalPage")

        self.logPage = LogPage(self.main_window, self)
        self.logPage.setObjectName("logPage")

        self.timePage = TimePage(self.main_window, self)
        self.timePage.setObjectName("timePage")

        self.appearancePage = AppearancePage(self.main_window, self)
        self.appearancePage.setObjectName("appearancePage")

        self.gridPage = GridPage(self.main_window, self)
        self.gridPage.setObjectName("gridPage")

        self.advancedPage = AdvancedPage(self.main_window, self)
        self.advancedPage.setObjectName("advancedPage")

    def _initNavigation(self):
        self.addSubInterface(self.generalPage, FUI.SETTING, tr("settings.general"))
        self.addSubInterface(self.timePage, FUI.DATE_TIME, tr("settings.time"))
        self.addSubInterface(self.appearancePage, FUI.PALETTE, tr("settings.appearance"))
        self.addSubInterface(self.gridPage, FUI.TABLE, tr("settings.grid.title"))
        self.addSubInterface(self.logPage, FUI.INFO, tr("settings.log"))
        self.addSubInterface(self.advancedPage, FUI.LIBRARY, tr("settings.advanced"))

        # 展开导航栏
        self.navigationInterface.expand()
        self.navigationInterface.setReturnButtonVisible(False)

    def _applyTheme(self):
        """应用当前主题"""
        theme = cfg.themeMode.value
        setTheme(theme)
        qss = load_qss('setting.qss')
        if qss:
            self.setStyleSheet(qss)

    def closeEvent(self, event):
        """关闭时释放资源"""
        event.accept()
