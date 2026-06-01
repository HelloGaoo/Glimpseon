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
设置界面模块
"""

import os
import sys
if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:sys.path.insert(0, _BASE_DIR)

import json
import os
from datetime import datetime

from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import QApplication, QFileDialog, QLabel, QWidget
from qfluentwidgets import (
    ComboBoxSettingCard,
    CustomColorSettingCard,
    ExpandLayout,
    FluentIcon as FIF,
    InfoBar,
    LineEdit,
    MessageBox,
    OptionsSettingCard,
    PushButton,
    RangeSettingCard,
    ScrollArea,
    SettingCard,
    SettingCardGroup,
    SpinBox,
    SwitchSettingCard,
    Theme,
    qconfig,
    setTheme,
    setThemeColor,
)

from core.config import cfg, default_cfg, ConfigItem, CONFIG_PATH
from core.constants import BASE_DIR, get_resPath, load_qss
from core.utils import _load_app_fonts, apply_fonts, tr, get_translation_manager, LanguageCode, switch_language, TranslatableWidget
from core.logger import log_dir, logger


class LineEditSettingCard(SettingCard):
    """ 带 QLineEdit 的设置卡片 """

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
        except ValueError:pass

    def setValue(self, value):
        self.lineEdit.setText(str(value))


class SpinBoxSettingCard(SettingCard):
    def __init__(self, configItem, icon, title, content=None, parent=None, min_value=1, max_value=100):
        super().__init__(icon, title, content, parent)
        self.configItem = configItem
        self.spinBox = SpinBox(self)
        self.spinBox.setRange(min_value, max_value)
        self.spinBox.setValue(qconfig.get(configItem))
        self.hBoxLayout.addWidget(self.spinBox, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)
        self.spinBox.valueChanged.connect(self.__onValueChanged)
        configItem.valueChanged.connect(self.setValue)

    def __onValueChanged(self, value):
        qconfig.set(self.configItem, value)

    def setValue(self, value):
        self.spinBox.setValue(value)


class ButtonSettingCard(SettingCard):
    """ 按钮设置卡片 """

    def __init__(self, icon, title, content=None, parent=None):
        super().__init__(icon, title, content, parent)
        self.button = PushButton(FIF.EDIT, "执行", self)
        self.button.setFixedHeight(36)
        self.hBoxLayout.addWidget(self.button, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)


class DualButtonSettingCard(SettingCard):
    def __init__(self, icon, title, content=None, parent=None):
        super().__init__(icon, title, content, parent)
        self.button1 = PushButton(FIF.SAVE, "导出", self)
        self.button1.setFixedSize(80, 32)
        self.button2 = PushButton(FIF.DOWNLOAD, "导入", self)
        self.button2.setFixedSize(80, 32)
        from PyQt6.QtWidgets import QHBoxLayout
        button_layout = QHBoxLayout()
        button_layout.addWidget(self.button1)
        button_layout.addWidget(self.button2)
        button_layout.setSpacing(8)
        container = QWidget()
        container.setLayout(button_layout)
        self.hBoxLayout.addWidget(container, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)


class SettingInterface(ScrollArea, TranslatableWidget):
    """ 设置界面 """
    def __init__(self, parent=None):
        super().__init__(parent=parent)
        self.setObjectName("setting")
        self.scrollWidget = QWidget()
        self.expandLayout = ExpandLayout(self.scrollWidget)
        self.settingLabel = QLabel(tr("settings.title"), self)
        self.basicGroup = SettingCardGroup(tr("settings.general"), self.scrollWidget)
        self.autoStartCard = SwitchSettingCard(
            FIF.PLAY,
            tr("wizard.auto_start"),
            tr("wizard.auto_start_desc"),
            configItem=cfg.autoStart,
            parent=self.basicGroup
        )
        self.autoOpenOnIdleCard = SwitchSettingCard(
            FIF.VIEW,
            tr("wizard.auto_open_idle"),
            tr("wizard.auto_open_idle_desc"),
            configItem=cfg.autoOpenOnIdle,
            parent=self.basicGroup
        )
        self.idleMinutesCard = SpinBoxSettingCard(
            cfg.idleMinutes,
            FIF.HISTORY,
            tr("settings.idle_minutes"),
            tr("settings.idle_minutes_desc"),
            parent=self.basicGroup,
            min_value=1,
            max_value=60
        )
        self.autoOpenMaximizeCard = SwitchSettingCard(
            FIF.FULL_SCREEN,
            tr("wizard.auto_open_maximize"),
            tr("wizard.auto_open_maximize_desc"),
            configItem=cfg.autoOpenMaximize,
            parent=self.basicGroup
        )
        self.appearanceGroup = SettingCardGroup(tr("settings.appearance"), self.scrollWidget)
        self.themeCard = ComboBoxSettingCard(
            cfg.themeMode,
            FIF.BRUSH,
            tr("wizard.theme_mode"),
            tr("wizard.theme_mode_desc"),
            texts=[tr("wizard.theme_light"), tr("wizard.theme_dark"), tr("wizard.theme_system")],
            parent=self.appearanceGroup
        )
        self.themeColorCard = CustomColorSettingCard(
            cfg.themeColor,
            FIF.PALETTE,
            tr("wizard.primary_color"),
            tr("wizard.primary_color_desc"),
            parent=self.appearanceGroup
        )
        self.languageCard = ComboBoxSettingCard(
            cfg.language,
            FIF.LANGUAGE,
            tr("settings.language"),
            tr("settings.language_desc"),
            texts=["简体中文", "繁體中文", "English", "Auto"],
            parent=self.appearanceGroup
        )
        self.appearanceGroup.addSettingCard(self.themeCard)
        self.appearanceGroup.addSettingCard(self.themeColorCard)
        self.appearanceGroup.addSettingCard(self.languageCard)
        self.logGroup = SettingCardGroup(tr("settings.advanced"), self.scrollWidget)
        self.disableLogCard = SwitchSettingCard(
            FIF.CLOSE,
            tr("settings.disable_log"),
            tr("settings.disable_log_desc"),
            configItem=cfg.disableLog,
            parent=self.logGroup
        )
        self.logLevelCard = ComboBoxSettingCard(
            cfg.logLevel,
            FIF.INFO,
            tr("settings.log_level"),
            tr("settings.log_level_desc"),
            texts=["Debug", "Info", "Warning", "Error"],
            parent=self.logGroup
        )
        self.logMaxCountCard = SpinBoxSettingCard(
            cfg.logMaxCount,
            FIF.INFO,
            tr("settings.log_max_count"),
            tr("settings.log_max_count_desc"),
            parent=self.logGroup,
            min_value=10,
            max_value=500
        )
        self.logMaxDaysCard = SpinBoxSettingCard(
            cfg.logMaxDays,
            FIF.INFO,
            tr("settings.log_max_days"),
            tr("settings.log_max_days_desc"),
            parent=self.logGroup,
            min_value=30,
            max_value=365
        )
        self.clearLogCard = ButtonSettingCard(
            FIF.DELETE,
            tr("settings.clear_log"),
            tr("settings.clear_log_desc"),
            parent=self.logGroup
        )
        self.logGroup.addSettingCard(self.clearLogCard)
        self.clearLogCard.button.setText(tr("settings.clear_log_button"))
        self.__initWidget()

    def __initWidget(self):
        """ 初始化界面 """
        self.resize(1000, 800)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.setViewportMargins(0, 120, 0, 20)
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)
        self.__setQss()
        self.__initLayout()
        self.__connectSignalToSlot()
        self.setup_translatable_ui()

    def __initLayout(self):
        """ 初始化布局 """
        self.settingLabel.move(60, 63)
        self.basicGroup.addSettingCard(self.autoStartCard)
        self.basicGroup.addSettingCard(self.autoOpenOnIdleCard)
        self.basicGroup.addSettingCard(self.idleMinutesCard)
        self.basicGroup.addSettingCard(self.autoOpenMaximizeCard)
        self.appearanceGroup.addSettingCard(self.themeCard)
        self.appearanceGroup.addSettingCard(self.themeColorCard)
        self.logGroup.addSettingCard(self.disableLogCard)
        self.logGroup.addSettingCard(self.logLevelCard)
        self.logGroup.addSettingCard(self.logMaxCountCard)
        self.logGroup.addSettingCard(self.logMaxDaysCard)
        self.otherGroup = SettingCardGroup(tr("settings.data_management"), self.scrollWidget)
        self.closeActionCard = ComboBoxSettingCard(
            cfg.closeAction,
            FIF.SETTING,
            tr("settings.close_action"),
            tr("settings.close_action_desc"),
            texts=[tr("settings.minimize_to_tray"), tr("settings.close_directly")],
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.closeActionCard)
        self.allowMultipleInstancesCard = SwitchSettingCard(
            FIF.SYNC,
            tr("settings.allow_multiple_instances"),
            tr("settings.allow_multiple_instances_desc"),
            configItem=cfg.allowMultipleInstances,
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.allowMultipleInstancesCard)
        self.enableGpuAccelerationCard = SwitchSettingCard(
            FIF.VIDEO,
            tr("settings.gpu_acceleration"),
            tr("settings.gpu_acceleration_desc"),
            configItem=cfg.enableGpuAcceleration,
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.enableGpuAccelerationCard)
        self.configIOCard = DualButtonSettingCard(
            FIF.SYNC,
            tr("settings.config_import_export"),
            tr("settings.config_import_export_desc"),
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.configIOCard)
        self.resetDefaultCard = ButtonSettingCard(
            FIF.SETTING,
            tr("settings.reset_default"),
            tr("settings.reset_default_desc"),
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.resetDefaultCard)
        self.resetDefaultCard.button.setText(tr("settings.reset_default_button"))
        self.debugModeCard = SwitchSettingCard(
            FIF.CODE,
            tr("settings.debug_mode"),
            tr("settings.debug_mode_desc"),
            configItem=cfg.debugMode,
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.debugModeCard)
        self.expandLayout.setSpacing(28)
        self.expandLayout.setContentsMargins(60, 10, 60, 0)
        self.expandLayout.addWidget(self.basicGroup)
        self.expandLayout.addWidget(self.appearanceGroup)
        self.expandLayout.addWidget(self.logGroup)
        self.expandLayout.addWidget(self.otherGroup)

    def __setQss(self):
        """ 设置样式表 """
        self.scrollWidget.setObjectName('scrollWidget')
        self.settingLabel.setObjectName('settingLabel')
        self.setStyleSheet(load_qss('setting.qss'))

    def __onThemeChanged(self, theme: Theme):
        """ 主题变更 """
        setTheme(theme)
        self.__setQss()

    def __showRestartTooltip(self):
        InfoBar.warning(
            '',
            tr("settings.restart_required"),
            duration=5000,
            parent=self.window()
        )

    def __onDisableLogChanged(self, disabled):
        """ 日志禁用状态变更 """
        self.logLevelCard.setEnabled(not disabled)
        self.logMaxCountCard.setEnabled(not disabled)
        self.logMaxDaysCard.setEnabled(not disabled)
    
    def __connectSignalToSlot(self):
        cfg.themeChanged.connect(self.__onThemeChanged)
        self.themeColorCard.colorChanged.connect(setThemeColor)
        cfg.appRestartSig.connect(self.__showRestartTooltip)
        self.disableLogCard.checkedChanged.connect(self.__onDisableLogChanged)
        self.resetDefaultCard.button.clicked.connect(self.__resetDefaultSettings)
        self.clearLogCard.button.clicked.connect(self.__clearLog)
        self.configIOCard.button1.clicked.connect(self.__exportConfig)
        self.configIOCard.button2.clicked.connect(self.__importConfig)
        self.__onDisableLogChanged(cfg.disableLog.value)
    
    def __resetDefaultSettings(self):
        """ 恢复默认设置 """
        msgBox = MessageBox(
            tr("settings.reset_default"),
            tr("settings.reset_default_confirm"),
            self.window()
        )
        msgBox.yesButton.setText(tr("dialog.confirm"))
        msgBox.cancelButton.setText(tr("dialog.cancel"))
        if msgBox.exec(): 
            try:
                config_path = CONFIG_PATH
                if os.path.exists(config_path):
                    os.remove(config_path)
                config_dir = os.path.join(BASE_DIR, 'config')
                if not os.path.exists(config_dir):os.makedirs(config_dir)
                default_config = default_cfg()
                with open(config_path, 'w', encoding='utf-8') as f:json.dump(default_config, f, ensure_ascii=False, indent=4)
                qconfig.load(config_path, cfg)
                
                for attr_name in dir(cfg):
                    if not attr_name.startswith('_'):
                        attr = getattr(cfg, attr_name)
                        if isinstance(attr, ConfigItem) and hasattr(attr, 'valueChanged'):
                            attr.valueChanged.emit(attr.value)
                
                main_window = self.window()
                
                if hasattr(main_window, '_MainWindow__updateQuickLaunch'):
                    main_window._MainWindow__updateQuickLaunch()
                
                if hasattr(main_window, '_MainWindow__updateClock'):
                    main_window._MainWindow__updateClock()
                if hasattr(main_window, '_MainWindow__updatePoetry'):
                    main_window._MainWindow__updatePoetry()
                if hasattr(main_window, '_MainWindow__updateWeather'):
                    main_window._MainWindow__updateWeather()
                if hasattr(main_window, '_MainWindow__updateCountdown'):
                    main_window._MainWindow__updateCountdown()
                if hasattr(main_window, 'updateSchoolInfo'):
                    main_window.updateSchoolInfo()
                if hasattr(main_window, 'updateSchoolInfoStyle'):
                    main_window.updateSchoolInfoStyle()
                if hasattr(main_window, '_MainWindow__updateSchoolInfoPosition'):
                    main_window._MainWindow__updateSchoolInfoPosition()
                if hasattr(main_window, '_MainWindow__updateClockPosition'):
                    main_window._MainWindow__updateClockPosition()
                if hasattr(main_window, '_MainWindow__updatePoetryPosition'):
                    main_window._MainWindow__updatePoetryPosition()
                if hasattr(main_window, '_MainWindow__updateWeatherPosition'):
                    main_window._MainWindow__updateWeatherPosition()
                if hasattr(main_window, '_MainWindow__updateCountdownPosition'):
                    main_window._MainWindow__updateCountdownPosition()
                
                app = QApplication.instance()
                if app:
                    font_loaded = _load_app_fonts()
                    apply_fonts(app, use_harmonyos=font_loaded)
                current_theme = cfg.themeMode.value
                setTheme(current_theme)
                cfg.themeChanged.emit(current_theme)
                
                InfoBar.success(
                    "成功",
                    "所有设置已恢复到默认值",
                    duration=5000,
                    parent=self
                )
            except Exception as e:
                InfoBar.error(
                    "错误",
                    f"恢复默认设置失败: {str(e)}",
                    duration=5000,
                    parent=self
                )
    
    def __clearLog(self):
        """ 清空日志 """
        msgBox = MessageBox(
            tr("settings.clear_log"),
            tr("settings.clear_log_confirm"),
            self.window()
        )
        msgBox.yesButton.setText(tr("dialog.confirm"))
        msgBox.cancelButton.setText(tr("dialog.cancel"))
        
        if msgBox.exec():
            try:
                if os.path.exists(log_dir):
                    log_files = []
                    for file in os.listdir(log_dir):
                        if file.endswith('.log'):
                            file_path = os.path.join(log_dir, file)
                            mtime = os.path.getmtime(file_path)
                            log_files.append((mtime, file))
                    log_files.sort()
                    
                    # 排除当前日志
                    current_log_file = None
                    if log_files:current_log_file = log_files[-1][1]
                    deleted_count = 0
                    for file in os.listdir(log_dir):
                        if file.endswith('.log') and file != current_log_file:
                            try:
                                os.remove(os.path.join(log_dir, file))
                                deleted_count += 1
                            except Exception: pass
                    if deleted_count > 0:
                        InfoBar.success(
                            "成功",
                            f"已清空 {deleted_count} 个日志文件",
                            duration=5000,
                            parent=self
                        )
                    else:
                        InfoBar.info(
                            "提示",
                            "没有可清空的日志文件",
                            duration=5000,
                            parent=self
                        )
                else:
                    InfoBar.info(
                        "提示",
                        "日志目录不存在",
                        duration=5000,
                        parent=self
                    )
            except Exception as e:
                InfoBar.error(
                    "错误",
                    f"清空日志失败: {str(e)}",
                    duration=5000,
                    parent=self
                )
    
    def __exportConfig(self):
        """导出配置"""
        try:
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            default_filename = f"ClassLively_Config_{timestamp}.json"
            file_path, _ = QFileDialog.getSaveFileName(
                self, "导出配置", default_filename, "JSON 文件 (*.json);;所有文件 (*.*)"
            )
            if not file_path: return
            
            if os.path.exists(CONFIG_PATH):
                import shutil
                shutil.copy2(CONFIG_PATH, file_path)
                InfoBar.success("成功", f"配置已导出到：{file_path}", duration=5000, parent=self)
            else:
                InfoBar.warning("提示", "配置文件不存在，无法导出", duration=5000, parent=self)
        except Exception as e:
            InfoBar.error("错误", f"导出配置失败: {str(e)}", duration=5000, parent=self)
    
    def __importConfig(self):
        """导入配置"""
        try:
            file_path, _ = QFileDialog.getOpenFileName(
                self, "导入配置", "", "JSON 文件 (*.json);;所有文件 (*.*)"
            )
            if not file_path: return
            if not os.path.exists(file_path):
                InfoBar.warning("提示", "选择的文件不存在", duration=5000, parent=self)
                return
            
            with open(file_path, 'r', encoding='utf-8') as f:
                imported_config = json.load(f)
            if not isinstance(imported_config, dict):
                InfoBar.error(tr("dialog.error"), tr("settings.config_format_error"), duration=5000, parent=self)
                return
            
            msgBox = MessageBox(tr("settings.import_config"), tr("settings.import_config_confirm"), self.window())
            msgBox.yesButton.setText(tr("dialog.confirm"))
            msgBox.cancelButton.setText(tr("dialog.cancel"))
            if not msgBox.exec(): return
            
            config_dir = os.path.join(BASE_DIR, 'config')
            if not os.path.exists(config_dir): os.makedirs(config_dir)
            
            import shutil
            if os.path.exists(CONFIG_PATH):
                backup_path = CONFIG_PATH + '.backup'
                shutil.copy2(CONFIG_PATH, backup_path)
            
            with open(CONFIG_PATH, 'w', encoding='utf-8') as f:
                json.dump(imported_config, f, ensure_ascii=False, indent=4)
            qconfig.load(CONFIG_PATH, cfg)
            
            for attr_name in dir(cfg):
                if not attr_name.startswith('_'):
                    attr = getattr(cfg, attr_name)
                    if isinstance(attr, ConfigItem) and hasattr(attr, 'valueChanged'):
                        attr.valueChanged.emit(attr.value)
            
            main_window = self.window()
            if hasattr(main_window, '_MainWindow__updateQuickLaunch'): main_window._MainWindow__updateQuickLaunch()
            if hasattr(main_window, '_MainWindow__updateClock'): main_window._MainWindow__updateClock()
            if hasattr(main_window, '_MainWindow__updatePoetry'): main_window._MainWindow__updatePoetry()
            if hasattr(main_window, '_MainWindow__updateWeather'): main_window._MainWindow__updateWeather()
            if hasattr(main_window, '_MainWindow__updateCountdown'): main_window._MainWindow__updateCountdown()
            if hasattr(main_window, 'updateSchoolInfo'): main_window.updateSchoolInfo()
            if hasattr(main_window, 'updateSchoolInfoStyle'): main_window.updateSchoolInfoStyle()
            if hasattr(main_window, '_MainWindow__updateSchoolInfoPosition'): main_window._MainWindow__updateSchoolInfoPosition()
            if hasattr(main_window, '_MainWindow__updateClockPosition'): main_window._MainWindow__updateClockPosition()
            if hasattr(main_window, '_MainWindow__updatePoetryPosition'): main_window._MainWindow__updatePoetryPosition()
            if hasattr(main_window, '_MainWindow__updateWeatherPosition'): main_window._MainWindow__updateWeatherPosition()
            if hasattr(main_window, '_MainWindow__updateCountdownPosition'): main_window._MainWindow__updateCountdownPosition()
            
            app = QApplication.instance()
            if app:
                font_loaded = _load_app_fonts()
                apply_fonts(app, use_harmonyos=font_loaded)
            
            current_theme = cfg.themeMode.value
            setTheme(current_theme)
            cfg.themeChanged.emit(current_theme)
            InfoBar.success("成功", f"配置已导入：{file_path}", duration=5000, parent=self)
        except json.JSONDecodeError:
            InfoBar.error("错误", "配置文件格式错误，无法解析 JSON", duration=5000, parent=self)
        except Exception as e:
            InfoBar.error("错误", f"导入配置失败: {str(e)}", duration=5000, parent=self)

    def retranslateUi(self):
        try:
            if hasattr(self, 'settingLabel'):
                self.settingLabel.setText(tr("settings.title"))

            if hasattr(self, 'basicGroup') and hasattr(self.basicGroup, 'titleLabel'):
                self.basicGroup.titleLabel.setText(tr("settings.general"))

            if hasattr(self, 'appearanceGroup') and hasattr(self.appearanceGroup, 'titleLabel'):
                self.appearanceGroup.titleLabel.setText(tr("settings.appearance"))

            if hasattr(self, 'logGroup') and hasattr(self.logGroup, 'titleLabel'):
                self.logGroup.titleLabel.setText(tr("settings.advanced"))

            if hasattr(self, 'otherGroup') and hasattr(self.otherGroup, 'titleLabel'):
                self.otherGroup.titleLabel.setText(tr("settings.data_management"))

            card_translations = [
                # 通用设置
                (self.autoStartCard, tr("wizard.auto_start"), tr("wizard.auto_start_desc")),
                (self.autoOpenOnIdleCard, tr("wizard.auto_open_idle"), tr("wizard.auto_open_idle_desc")),
                (self.idleMinutesCard, tr("settings.idle_minutes"), tr("settings.idle_minutes_desc")),
                (self.autoOpenMaximizeCard, tr("wizard.auto_open_maximize"), tr("wizard.auto_open_maximize_desc")),
                # 外观设置
                (self.themeCard, tr("wizard.theme_mode"), tr("wizard.theme_mode_desc")),
                (self.themeColorCard, tr("wizard.primary_color"), tr("wizard.primary_color_desc")),
                (self.languageCard, tr("settings.language"), tr("settings.language_desc")),
                # 高级设置
                (self.disableLogCard, tr("settings.disable_log"), tr("settings.disable_log_desc")),
                (self.logLevelCard, tr("settings.log_level"), tr("settings.log_level_desc")),
                (self.logMaxCountCard, tr("settings.log_max_count"), tr("settings.log_max_count_desc")),
                (self.logMaxDaysCard, tr("settings.log_max_days"), tr("settings.log_max_days_desc")),
                (self.clearLogCard, tr("settings.clear_log"), tr("settings.clear_log_desc")),
                # 数据管理
                (self.closeActionCard, tr("settings.close_action"), tr("settings.close_action_desc")),
                (self.allowMultipleInstancesCard, tr("settings.allow_multiple_instances"), tr("settings.allow_multiple_instances_desc")),
                (self.enableGpuAccelerationCard, tr("settings.gpu_acceleration"), tr("settings.gpu_acceleration_desc")),
                (self.configIOCard, tr("settings.config_import_export"), tr("settings.config_import_export_desc")),
                (self.resetDefaultCard, tr("settings.reset_default"), tr("settings.reset_default_desc")),
                (self.debugModeCard, tr("settings.debug_mode"), tr("settings.debug_mode_desc")),
            ]

            for card, title, content in card_translations:
                if card:
                    # CustomColorSettingCard 没有 setTitle/setContent 方法
                    if hasattr(card, 'setTitle'):
                        card.setTitle(title)
                        card.setContent(content)
                    elif hasattr(card, 'titleLabel'):
                        # 对于 CustomColorSettingCard 更新标题
                        card.titleLabel.setText(title)

            if hasattr(self, 'clearLogCard'):
                self.clearLogCard.button.setText(tr("settings.clear_log_button"))

            if hasattr(self, 'resetDefaultCard'):
                self.resetDefaultCard.button.setText(tr("settings.reset_default_button"))

            logger.info("设置界面翻译已更新")
        except Exception as e:
            logger.error(f"更新设置界面翻译失败: {e}")


