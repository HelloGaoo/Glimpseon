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
from core.utils import _load_app_fonts, apply_fonts, tr, TranslatableWidget
from core.logger import log_dir, logger


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
        self.button = PushButton(FIF.EDIT, tr("common.execute"), self)
        self.button.setFixedHeight(36)
        self.hBoxLayout.addWidget(self.button, 0, Qt.AlignmentFlag.AlignRight)
        self.hBoxLayout.addSpacing(16)


class DualButtonSettingCard(SettingCard):
    def __init__(self, icon, title, content=None, parent=None):
        super().__init__(icon, title, content, parent)
        self.button1 = PushButton(FIF.SAVE, tr("settings.export_button"), self)
        self.button1.setFixedHeight(32)
        self.button2 = PushButton(FIF.DOWNLOAD, tr("settings.import_button"), self)
        self.button2.setFixedHeight(32)
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
        self.settingLabel = QLabel(tr("settings.title"), self)  # 设置
        self.basicGroup = SettingCardGroup(tr("settings.general"), self.scrollWidget)  # 通用
        self.autoStartCard = SwitchSettingCard(
            FIF.PLAY,
            tr("wizard.auto_start"),  # 开机自启动
            tr("wizard.auto_start_desc"),  # 设置应用在系统启动时自动运行
            configItem=cfg.autoStart,
            parent=self.basicGroup
        )
        self.autoOpenOnIdleCard = SwitchSettingCard(
            FIF.VIEW,
            tr("wizard.auto_open_idle"),  # 空闲时自动打开
            tr("wizard.auto_open_idle_desc"),  # 电脑空闲时自动从最小化打开界面
            configItem=cfg.autoOpenOnIdle,
            parent=self.basicGroup
        )
        self.idleMinutesCard = SpinBoxSettingCard(
            cfg.idleMinutes,
            FIF.HISTORY,
            tr("settings.idle_minutes"),  # 空闲检测时间
            tr("settings.idle_minutes_desc"),  # 设置电脑空闲多少分钟后触发自动打开（1-60 分钟）
            parent=self.basicGroup,
            min_value=1,
            max_value=60
        )
        self.autoOpenMaximizeCard = SwitchSettingCard(
            FIF.FULL_SCREEN,
            tr("wizard.auto_open_maximize"),  # 自动打开时最大化
            tr("wizard.auto_open_maximize_desc"),  # 空闲自动打开界面时是否最大化窗口
            configItem=cfg.autoOpenMaximize,
            parent=self.basicGroup
        )
        self.appearanceGroup = SettingCardGroup(tr("settings.appearance"), self.scrollWidget)  # 外观
        self.themeCard = ComboBoxSettingCard(
            cfg.themeMode,
            FIF.BRUSH,
            tr("wizard.theme_mode"),  # 应用颜色主题
            tr("wizard.theme_mode_desc"),  # 更改应用程序的颜色外观
            texts=[tr("wizard.theme_light"), tr("wizard.theme_dark"), tr("wizard.theme_system")],  # 浅色 / 深色 / 使用系统设置
            parent=self.appearanceGroup
        )
        self.themeColorCard = CustomColorSettingCard(
            cfg.themeColor,
            FIF.PALETTE,
            tr("wizard.primary_color"),  # 主要颜色
            tr("wizard.primary_color_desc"),  # 更改应用程序的主要颜色
            parent=self.appearanceGroup
        )
        self.componentCardOpacityCard = SpinBoxSettingCard(
            cfg.componentCardOpacity,
            FIF.PALETTE,
            tr("settings.component_card_opacity"),  # 组件卡片不透明度
            tr("settings.component_card_opacity_desc"),  # 调整主界面组件背景的不透明度
            parent=self.appearanceGroup,
            min_value=0,
            max_value=100
        )
        self.componentCardRadiusCard = SpinBoxSettingCard(
            cfg.componentCardRadius,
            FIF.EDIT,
            tr("settings.component_card_radius"),  # 组件卡片圆角
            tr("settings.component_card_radius_desc"),  # 调整主界面组件背景的圆角大小
            parent=self.appearanceGroup,
            min_value=0,
            max_value=30
        )
        self.languageCard = ComboBoxSettingCard(
            cfg.language,
            FIF.LANGUAGE,
            tr("settings.language"),  # 语言
            tr("settings.language_desc"),  # 切换应用显示语言 / Switch application display language
            texts=[tr("settings.lang_zh_cn"), tr("settings.lang_zh_tw"), "English", "Auto"],
            parent=self.appearanceGroup
        )
        self.appearanceGroup.addSettingCard(self.themeCard)
        self.appearanceGroup.addSettingCard(self.themeColorCard)
        self.appearanceGroup.addSettingCard(self.languageCard)
        self.logGroup = SettingCardGroup(tr("settings.advanced"), self.scrollWidget)  # 高级
        self.disableLogCard = SwitchSettingCard(
            FIF.CLOSE,
            tr("settings.disable_log"),  # 禁用日志
            tr("settings.disable_log_desc"),  # 完全禁用日志输出
            configItem=cfg.disableLog,
            parent=self.logGroup
        )
        self.logLevelCard = ComboBoxSettingCard(
            cfg.logLevel,
            FIF.INFO,
            tr("settings.log_level"),  # 日志级别
            tr("settings.log_level_desc"),  # 设置日志的输出级别
            texts=["Debug", "Info", "Warning", "Error"],
            parent=self.logGroup
        )
        self.logMaxCountCard = SpinBoxSettingCard(
            cfg.logMaxCount,
            FIF.INFO,
            tr("settings.log_max_count"),  # 日志数量上限
            tr("settings.log_max_count_desc"),  # 设置日志文件的最大条目数
            parent=self.logGroup,
            min_value=10,
            max_value=500
        )
        self.logMaxDaysCard = SpinBoxSettingCard(
            cfg.logMaxDays,
            FIF.INFO,
            tr("settings.log_max_days"),  # 日志时间上限
            tr("settings.log_max_days_desc"),  # 设置日志文件的最大保存天数
            parent=self.logGroup,
            min_value=30,
            max_value=365
        )
        self.clearLogCard = ButtonSettingCard(
            FIF.DELETE,
            tr("settings.clear_log"),  # 清空日志
            tr("settings.clear_log_desc"),  # 清空所有日志文件
            parent=self.logGroup
        )
        self.logGroup.addSettingCard(self.clearLogCard)
        self.clearLogCard.button.setText(tr("settings.clear_log_button"))  # 清空日志
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
        self.appearanceGroup.addSettingCard(self.componentCardOpacityCard)
        self.appearanceGroup.addSettingCard(self.componentCardRadiusCard)
        self.logGroup.addSettingCard(self.disableLogCard)
        self.logGroup.addSettingCard(self.logLevelCard)
        self.logGroup.addSettingCard(self.logMaxCountCard)
        self.logGroup.addSettingCard(self.logMaxDaysCard)
        self.otherGroup = SettingCardGroup(tr("settings.data_management"), self.scrollWidget)  # 数据管理
        self.closeActionCard = ComboBoxSettingCard(
            cfg.closeAction,
            FIF.SETTING,
            tr("settings.close_action"),  # 关闭事件行为
            tr("settings.close_action_desc"),  # 设置点击关闭按钮时的行为
            texts=[tr("settings.minimize_to_tray"), tr("settings.close_directly")],  # 最小化到任务栏 / 直接关闭
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.closeActionCard)
        self.allowMultipleInstancesCard = SwitchSettingCard(
            FIF.SYNC,
            tr("settings.allow_multiple_instances"),  # 允许重复启动
            tr("settings.allow_multiple_instances_desc"),  # 允许同时运行多个应用实例
            configItem=cfg.allowMultipleInstances,
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.allowMultipleInstancesCard)
        self.enableGpuAccelerationCard = SwitchSettingCard(
            FIF.VIDEO,
            tr("settings.gpu_acceleration"),  # GPU 加速
            tr("settings.gpu_acceleration_desc"),  # 使用 OpenGL ES 作为图形渲染后端（重启生效）
            configItem=cfg.enableGpuAcceleration,
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.enableGpuAccelerationCard)
        self.configIOCard = DualButtonSettingCard(
            FIF.SYNC,
            tr("settings.config_import_export"),  # 配置导入/导出
            tr("settings.config_import_export_desc"),  # 导出或导入配置文件（JSON）
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.configIOCard)
        self.resetDefaultCard = ButtonSettingCard(
            FIF.SETTING,
            tr("settings.reset_default"),  # 恢复默认设置
            tr("settings.reset_default_desc"),  # 将所有设置恢复到默认值
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.resetDefaultCard)
        self.resetDefaultCard.button.setText(tr("settings.reset_default_button"))  # 恢复默认
        self.debugModeCard = SwitchSettingCard(
            FIF.CODE,
            tr("settings.debug_mode"),  # 调试模式
            tr("settings.debug_mode_desc"),  # 启用调试模式以进行测试和调试
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
            tr("settings.restart_required"),  # 配置需要重启应用程序才能生效
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
            tr("settings.reset_default"),  # 恢复默认设置
            tr("settings.reset_default_confirm"),  # 确定要将所有设置恢复到默认值吗？
            self.window()
        )
        msgBox.yesButton.setText(tr("dialog.confirm"))  # 确定
        msgBox.cancelButton.setText(tr("dialog.cancel"))  # 取消
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
                
                self._refreshAllConfigUI()
                # for attr_name in dir(cfg):
                #     if not attr_name.startswith('_'):
                #         attr = getattr(cfg, attr_name)
                #         if isinstance(attr, ConfigItem) and hasattr(attr, 'valueChanged'):
                #             attr.valueChanged.emit(attr.value)
                #
                # main_window = self.window()
                #
                # main_window.refresh_quick_launch()
                #
                # main_window.refresh_clock()
                # main_window.refresh_poetry()
                # main_window.refresh_weather()
                # main_window.refresh_countdown()
                # if hasattr(main_window, 'updateSchoolInfo'):
                #     main_window.updateSchoolInfo()
                # if hasattr(main_window, 'updateSchoolInfoStyle'):
                #     main_window.updateSchoolInfoStyle()
                # main_window.refresh_school_info_position()
                # main_window.refresh_clock_position()
                # main_window.refresh_poetry_position()
                # main_window.refresh_weather_position()
                # main_window.refresh_countdown_position()
                #
                # app = QApplication.instance()
                # if app:
                #     font_loaded = _load_app_fonts()
                #     apply_fonts(app, use_harmonyos=font_loaded)
                # current_theme = cfg.themeMode.value
                # setTheme(current_theme)
                # cfg.themeChanged.emit(current_theme)
                
                InfoBar.success(
                    tr("wizard.success_title"),
                    tr("settings.reset_success"),
                    duration=5000,
                    parent=self
                )
            except Exception as e:
                InfoBar.error(
                    tr("dialog.error"),
                    tr("settings.reset_failed").format(error=str(e)),
                    duration=5000,
                    parent=self
                )
    
    def __clearLog(self):
        """ 清空日志 """
        msgBox = MessageBox(
            tr("settings.clear_log"),  # 清空日志
            tr("settings.clear_log_confirm"),  # 确定要清空所有日志文件吗？
            self.window()
        )
        msgBox.yesButton.setText(tr("dialog.confirm"))  # 确定
        msgBox.cancelButton.setText(tr("dialog.cancel"))  # 取消
        
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
                            tr("wizard.success_title"),
                            tr("settings.clear_log_success").format(count=deleted_count),
                            duration=5000,
                            parent=self
                        )
                    else:
                        InfoBar.info(
                            tr("common.tip"),
                            tr("settings.no_logs_to_clear"),
                            duration=5000,
                            parent=self
                        )
                else:
                    InfoBar.info(
                        tr("common.tip"),
                        tr("settings.log_dir_not_exist"),
                        duration=5000,
                        parent=self
                    )
            except Exception as e:
                InfoBar.error(
                    tr("dialog.error"),
                    tr("settings.clear_log_failed").format(error=str(e)),
                    duration=5000,
                    parent=self
                )
    
    def __exportConfig(self):
        """导出配置"""
        try:
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            default_filename = f"ClassLively_Config_{timestamp}.json"
            file_path, _ = QFileDialog.getSaveFileName(
                self, tr("settings.export_config"), default_filename, tr("settings.json_filter")
            )
            if not file_path: return
            
            if os.path.exists(CONFIG_PATH):
                import shutil
                shutil.copy2(CONFIG_PATH, file_path)
                InfoBar.success(tr("wizard.success_title"), tr("settings.export_success").format(path=file_path), duration=5000, parent=self)
            else:
                InfoBar.warning(tr("common.tip"), tr("settings.config_not_exist_export"), duration=5000, parent=self)
        except Exception as e:
            InfoBar.error(tr("dialog.error"), tr("settings.export_failed").format(error=str(e)), duration=5000, parent=self)
    
    def __importConfig(self):
        """导入配置"""
        try:
            file_path, _ = QFileDialog.getOpenFileName(
                self, tr("settings.import_config"), "", tr("settings.json_filter")
            )
            if not file_path: return
            if not os.path.exists(file_path):
                InfoBar.warning(tr("common.tip"), tr("settings.selected_file_not_exist"), duration=5000, parent=self)
                return
            
            with open(file_path, 'r', encoding='utf-8') as f:
                imported_config = json.load(f)
            if not isinstance(imported_config, dict):
                InfoBar.error(tr("dialog.error"), tr("settings.config_format_error"), duration=5000, parent=self)  # 错误 / 配置文件格式不正确
                return
            
            msgBox = MessageBox(tr("settings.import_config"), tr("settings.import_config_confirm"), self.window())  # 导入配置 / 确定要导入配置吗？这将覆盖当前的所有设置。
            msgBox.yesButton.setText(tr("dialog.confirm"))  # 确定
            msgBox.cancelButton.setText(tr("dialog.cancel"))  # 取消
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
            
            self._refreshAllConfigUI()
            # for attr_name in dir(cfg):
            #     if not attr_name.startswith('_'):
            #         attr = getattr(cfg, attr_name)
            #         if isinstance(attr, ConfigItem) and hasattr(attr, 'valueChanged'):
            #             attr.valueChanged.emit(attr.value)
            #
            # main_window = self.window()
            # main_window.refresh_quick_launch()
            # main_window.refresh_clock()
            # main_window.refresh_poetry()
            # main_window.refresh_weather()
            # main_window.refresh_countdown()
            # if hasattr(main_window, 'updateSchoolInfo'): main_window.updateSchoolInfo()
            # if hasattr(main_window, 'updateSchoolInfoStyle'): main_window.updateSchoolInfoStyle()
            # main_window.refresh_school_info_position()
            # main_window.refresh_clock_position()
            # main_window.refresh_poetry_position()
            # main_window.refresh_weather_position()
            # main_window.refresh_countdown_position()
            #
            # app = QApplication.instance()
            # if app:
            #     font_loaded = _load_app_fonts()
            #     apply_fonts(app, use_harmonyos=font_loaded)
            #
            # current_theme = cfg.themeMode.value
            # setTheme(current_theme)
            # cfg.themeChanged.emit(current_theme)
            InfoBar.success(tr("wizard.success_title"), tr("settings.import_success").format(path=file_path), duration=5000, parent=self)
        except json.JSONDecodeError:
            InfoBar.error(tr("dialog.error"), tr("settings.config_json_parse_error"), duration=5000, parent=self)
        except Exception as e:
            InfoBar.error(tr("dialog.error"), tr("settings.import_failed").format(error=str(e)), duration=5000, parent=self)

    def _refreshAllConfigUI(self):
        """刷新所有配置项的UI显示，包括信号通知和主窗口各组件更新"""
        for attr_name in dir(cfg):
            if not attr_name.startswith('_'):
                attr = getattr(cfg, attr_name)
                if isinstance(attr, ConfigItem) and hasattr(attr, 'valueChanged'):
                    attr.valueChanged.emit(attr.value)

        main_window = self.window()
        main_window.refresh_quick_launch()
        main_window.refresh_clock()
        main_window.refresh_poetry()
        main_window.refresh_weather()
        main_window.refresh_countdown()
        if hasattr(main_window, 'updateSchoolInfo'):
            main_window.updateSchoolInfo()
        if hasattr(main_window, 'updateSchoolInfoStyle'):
            main_window.updateSchoolInfoStyle()
        main_window.refresh_school_info_position()
        main_window.refresh_clock_position()
        main_window.refresh_poetry_position()
        main_window.refresh_weather_position()
        main_window.refresh_countdown_position()

        app = QApplication.instance()
        if app:
            font_loaded = _load_app_fonts()
            apply_fonts(app, use_harmonyos=font_loaded)
        current_theme = cfg.themeMode.value
        setTheme(current_theme)
        cfg.themeChanged.emit(current_theme)


