# ClassLively
# Copyright (C) 2026 HelloGaoo & WHYOS
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
import json
from PyQt5.QtCore import Qt
from PyQt5.QtWidgets import QWidget, QLabel
from qfluentwidgets import (
    SettingCardGroup, OptionsSettingCard, ScrollArea, ExpandLayout, 
    Theme, setTheme, isDarkTheme, FluentIcon as FIF, CustomColorSettingCard, setThemeColor,
    SwitchSettingCard, RangeSettingCard, InfoBar, LineEdit, SettingCard, qconfig, ComboBoxSettingCard,
    SpinBox, PushButton, MessageBox, NavigationItemPosition
)
from core.config import cfg, get_default_config_dict
from core.logger import log_dir
from ui.city_selector import RegionSelectorDialog


class LineEditSettingCard(SettingCard):
    """ 带 QLineEdit 的设置卡片 """

    def __init__(self, configItem, icon, title, content=None, parent=None):
        super().__init__(icon, title, content, parent)
        self.configItem = configItem
        self.lineEdit = LineEdit(self)

        self.hBoxLayout.addWidget(self.lineEdit, 0, Qt.AlignRight)
        self.hBoxLayout.addSpacing(16)

        value = qconfig.get(configItem)
        self.lineEdit.setText(str(value))
        self.lineEdit.textChanged.connect(self.__onTextChanged)
        configItem.valueChanged.connect(self.setValue)

    def __onTextChanged(self, text):
        try:
            # 将文本转换为浮点数
            value = float(text)
            qconfig.set(self.configItem, value)
        except ValueError:
            pass

    def setValue(self, value):
        self.lineEdit.setText(str(value))


class SpinBoxSettingCard(SettingCard):
    def __init__(self, configItem, icon, title, content=None, parent=None, min_value=1, max_value=100):
        super().__init__(icon, title, content, parent)
        self.configItem = configItem
        self.spinBox = SpinBox(self)
        self.spinBox.setRange(min_value, max_value)
        self.spinBox.setValue(qconfig.get(configItem))

        self.hBoxLayout.addWidget(self.spinBox, 0, Qt.AlignRight)
        self.hBoxLayout.addSpacing(16)

        self.spinBox.valueChanged.connect(self.__onValueChanged)
        configItem.valueChanged.connect(self.setValue)

    def __onValueChanged(self, value):
        qconfig.set(self.configItem, value)

    def setValue(self, value):
        self.spinBox.setValue(value)


class ButtonSettingCard(SettingCard):
    """ 带按钮的设置卡片 """

    def __init__(self, icon, title, content=None, parent=None):
        super().__init__(icon, title, content, parent)
        self.button = PushButton(FIF.EDIT, "执行", self)

        self.hBoxLayout.addWidget(self.button, 0, Qt.AlignRight)
        self.hBoxLayout.addSpacing(16)

# 路径设置
if getattr(sys, 'frozen', False):
    # 打包为 exe 时
    BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
    MEIPASS_DIR = sys._MEIPASS
else:
    # 脚本运行时
    BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    MEIPASS_DIR = None

def get_resource_path(relative_path):
    """获取绝对路径"""
    # 先检查 BASE_DIR 中的资源文件
    base_path = os.path.join(BASE_DIR, relative_path)
    if os.path.exists(base_path):
        return base_path
    # 如果 BASE_DIR 中不存在，检查 MEIPASS_DIR
    if MEIPASS_DIR:
        meipass_path = os.path.join(MEIPASS_DIR, relative_path)
        if os.path.exists(meipass_path):
            return meipass_path
    # 如果都不存在，返回 BASE_DIR 中的路径
    return base_path


class SettingInterface(ScrollArea):
    """ 设置界面 """

    def __init__(self, parent=None):
        super().__init__(parent=parent)
        self.scrollWidget = QWidget()
        self.expandLayout = ExpandLayout(self.scrollWidget)

        self.settingLabel = QLabel("设置", self)

        self.basicGroup = SettingCardGroup("基本", self.scrollWidget)
        self.autoStartCard = SwitchSettingCard(
            FIF.PLAY, 
            "开机自启动",
            "设置应用在系统启动时自动运行",
            configItem=cfg.autoStart,
            parent=self.basicGroup
        )
        self.autoOpenOnIdleCard = SwitchSettingCard(
            FIF.VIEW,
            "空闲时自动打开",
            "电脑空闲时自动从最小化打开界面",
            configItem=cfg.autoOpenOnIdle,
            parent=self.basicGroup
        )
        self.idleMinutesCard = SpinBoxSettingCard(
            cfg.idleMinutes,
            FIF.HISTORY,
            "空闲检测时间",
            "设置电脑空闲多少分钟后触发自动打开（1-60 分钟）",
            parent=self.basicGroup,
            min_value=1,
            max_value=60
        )
        self.autoOpenMaximizeCard = SwitchSettingCard(
            FIF.FULL_SCREEN,
            "自动打开时最大化",
            "空闲自动打开界面时是否最大化窗口",
            configItem=cfg.autoOpenMaximize,
            parent=self.basicGroup
        )

        self.appearanceGroup = SettingCardGroup("外观", self.scrollWidget)
        self.themeCard = ComboBoxSettingCard(
            cfg.themeMode,
            FIF.BRUSH,
            "应用颜色主题",
            "更改应用程序的颜色外观",
            texts=["浅色", "深色", "使用系统设置"],
            parent=self.appearanceGroup
        )
        self.themeColorCard = CustomColorSettingCard(
            cfg.themeColor,
            FIF.PALETTE,
            "主要颜色",
            "更改应用程序的主要颜色",
            parent=self.appearanceGroup
        )
        self.backgroundBlurCard = SpinBoxSettingCard(
            cfg.backgroundBlurRadius,
            FIF.PHOTO,
            "主界面背景模糊",
            "设置主界面背景图片的模糊强度（0-30）",
            parent=self.appearanceGroup,
            min_value=0,
            max_value=30
        )
        self.wallpaperGroup = SettingCardGroup("壁纸", self.scrollWidget)
        self.wallpaperSaveLimitCard = SpinBoxSettingCard(
            cfg.wallpaperSaveLimit,
            FIF.PHOTO,
            "壁纸保存量",
            "设置本地保存的壁纸数量，超过限制时会自动删除最旧的壁纸",
            parent=self.wallpaperGroup,
            min_value=10,
            max_value=100
        )
        self.autoGetIntervalCard = ComboBoxSettingCard(
            cfg.autoGetInterval,
            FIF.ALBUM,
            "自动获取间隔",
            "设置自动获取新壁纸的时间间隔，选择'从不'则禁用自动获取",
            texts=["从不", "10 分钟", "30 分钟", "1 小时", "3 小时", "6 小时", "12 小时", "1 天", "3 天", "5 天", "7 天"],
            parent=self.wallpaperGroup
        )
        self.wallpaperApiCard = ComboBoxSettingCard(
            cfg.wallpaperApi,
            FIF.LINK,
            "壁纸 API",
            "选择获取壁纸的 API 源",
            texts=["wp.upx8.com", "api.ltyuanfang.cn"],
            parent=self.wallpaperGroup
        )
        self.autoSyncToDesktopCard = SwitchSettingCard(
            FIF.HOME,
            "自动同步至桌面",
            "当获取新壁纸时，自动将其设置为桌面背景",
            configItem=cfg.autoSyncToDesktop,
            parent=self.wallpaperGroup
        )
        
        self.logGroup = SettingCardGroup("日志", self.scrollWidget)
        self.disableLogCard = SwitchSettingCard(
            FIF.CLOSE, 
            "禁用日志",
            "完全禁用日志输出",
            configItem=cfg.disableLog,
            parent=self.logGroup
        )
        self.logLevelCard = ComboBoxSettingCard(
            cfg.logLevel,
            FIF.INFO,
            "日志级别",
            "设置日志的输出级别",
            texts=["Debug", "Info", "Warning", "Error"],
            parent=self.logGroup
        )
        self.logMaxCountCard = SpinBoxSettingCard(
            cfg.logMaxCount,
            FIF.INFO,
            "日志数量上限",
            "设置日志文件的最大条目数",
            parent=self.logGroup,
            min_value=10,
            max_value=500
        )
        self.logMaxDaysCard = SpinBoxSettingCard(
            cfg.logMaxDays,
            FIF.INFO,
            "日志时间上限",
            "设置日志文件的最大保存天数",
            parent=self.logGroup,
            min_value=30,
            max_value=365
        )
        self.clearLogCard = ButtonSettingCard(
            FIF.DELETE,
            "清空日志",
            "清空所有日志文件",
            parent=self.logGroup
        )
        self.logGroup.addSettingCard(self.clearLogCard)
        self.clearLogCard.button.setText("清空日志")

        self.__initWidget()

    def __initWidget(self):
        """ 初始化界面 """
        self.resize(1000, 800)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        self.setViewportMargins(0, 120, 0, 20)
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)

        self.__setQss()
        self.__initLayout()
        self.__connectSignalToSlot()

    def __initLayout(self):
        """ 初始化布局 """
        self.settingLabel.move(60, 63)

        self.basicGroup.addSettingCard(self.autoStartCard)
        self.basicGroup.addSettingCard(self.autoOpenOnIdleCard)
        self.basicGroup.addSettingCard(self.idleMinutesCard)
        self.basicGroup.addSettingCard(self.autoOpenMaximizeCard)
        
        self.appearanceGroup.addSettingCard(self.themeCard)
        self.appearanceGroup.addSettingCard(self.themeColorCard)
        self.appearanceGroup.addSettingCard(self.backgroundBlurCard)
        
        self.wallpaperGroup.addSettingCard(self.wallpaperSaveLimitCard)
        self.wallpaperGroup.addSettingCard(self.autoGetIntervalCard)
        self.wallpaperGroup.addSettingCard(self.wallpaperApiCard)
        self.wallpaperGroup.addSettingCard(self.autoSyncToDesktopCard)
        
        self.logGroup.addSettingCard(self.disableLogCard)
        self.logGroup.addSettingCard(self.logLevelCard)
        self.logGroup.addSettingCard(self.logMaxCountCard)
        self.logGroup.addSettingCard(self.logMaxDaysCard)

        self.otherGroup = SettingCardGroup("其他", self.scrollWidget)
        self.closeActionCard = ComboBoxSettingCard(
            cfg.closeAction,
            FIF.SETTING,
            "关闭事件行为",
            "设置点击关闭按钮时的行为",
            texts=["最小化到任务栏", "直接关闭"],
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.closeActionCard)
        self.allowMultipleInstancesCard = SwitchSettingCard(
            FIF.SYNC,
            "允许重复启动",
            "允许同时运行多个应用实例",
            configItem=cfg.allowMultipleInstances,
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.allowMultipleInstancesCard)
        self.resetDefaultCard = ButtonSettingCard(
            FIF.SETTING,
            "恢复默认设置",
            "将所有设置恢复到默认值",
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.resetDefaultCard)
        self.resetDefaultCard.button.setText("恢复默认")
        self.developerModeCard = SwitchSettingCard(
            FIF.CODE,
            "开发者模式",
            "启用开发者模式以进行测试和调试",
            configItem=cfg.developerMode,
            parent=self.otherGroup
        )
        self.otherGroup.addSettingCard(self.developerModeCard)

        self.expandLayout.setSpacing(28)
        self.expandLayout.setContentsMargins(60, 10, 60, 0)
        self.expandLayout.addWidget(self.basicGroup)
        self.expandLayout.addWidget(self.appearanceGroup)
        self.expandLayout.addWidget(self.wallpaperGroup)
        self.expandLayout.addWidget(self.logGroup)
        self.expandLayout.addWidget(self.otherGroup)

    def __setQss(self):
        """ 设置样式表 """
        self.scrollWidget.setObjectName('scrollWidget')
        self.settingLabel.setObjectName('settingLabel')

        theme = 'dark' if isDarkTheme() else 'light'
        try:
            qss_path = get_resource_path(os.path.join('resource', 'qss', theme, 'setting_interface.qss'))
            with open(qss_path, encoding='utf-8') as f:
                self.setStyleSheet(f.read())
        except Exception:
            pass

    def __onThemeChanged(self, theme: Theme):
        """ 主题变更槽函数 """
        setTheme(theme)
        self.__setQss()

    def __showRestartTooltip(self):
        """ 显示重启提示 """
        InfoBar.warning(
            '',
            "配置需要重启应用程序才能生效",
            duration=5000,
            parent=self.window()
        )

    def __onDisableLogChanged(self, disabled):
        """ 日志禁用状态变更槽函数 """
        # 当禁用日志时，禁用其他日志相关设置
        self.logLevelCard.setEnabled(not disabled)
        self.logMaxCountCard.setEnabled(not disabled)
        self.logMaxDaysCard.setEnabled(not disabled)
    
    def __connectSignalToSlot(self):
        """ 连接信号与槽 """
        cfg.themeChanged.connect(self.__onThemeChanged)
        self.themeColorCard.colorChanged.connect(setThemeColor)
        cfg.appRestartSig.connect(self.__showRestartTooltip)
        
        # 连接日志禁用信号
        self.disableLogCard.checkedChanged.connect(self.__onDisableLogChanged)
        
        # 连接恢复默认设置按钮
        self.resetDefaultCard.button.clicked.connect(self.__resetDefaultSettings)
        
        # 连接清空日志按钮
        self.clearLogCard.button.clicked.connect(self.__clearLog)
    
        # 初始状态设置
        self.__onDisableLogChanged(cfg.disableLog.value)
    
    def __resetDefaultSettings(self):
        """ 恢复默认设置 """
        
        msgBox = MessageBox(
            "恢复默认设置",
            "确定要将所有设置恢复到默认值吗？",
            self.window()
        )
        msgBox.yesButton.setText("确定")
        msgBox.cancelButton.setText("取消")
        
        if msgBox.exec() == 1: 
            try:
                config_path = os.path.join(BASE_DIR, 'config', 'config.json')
                if os.path.exists(config_path):
                    os.remove(config_path)
                
                config_dir = os.path.join(BASE_DIR, 'config')
                if not os.path.exists(config_dir):
                    os.makedirs(config_dir)
                default_config = get_default_config_dict()
                
                with open(config_path, 'w', encoding='utf-8') as f:
                    json.dump(default_config, f, ensure_ascii=False, indent=4)
                
                # 重新加载配置
                qconfig.load(config_path, cfg)
                
                # 触发主题刷新
                current_theme = cfg.theme.value
                setTheme(current_theme)
                cfg.themeChanged.emit(Theme(current_theme))
                
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
            "清空日志",
            "确定要清空所有日志文件吗？",
            self.window()
        )
        msgBox.yesButton.setText("确定")
        msgBox.cancelButton.setText("取消")
        
        if msgBox.exec() == 1:
            try:
                # 清空日志文件
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
                    if log_files:
                        current_log_file = log_files[-1][1]
                    
                    deleted_count = 0
                    for file in os.listdir(log_dir):
                        if file.endswith('.log') and file != current_log_file:
                            try:
                                os.remove(os.path.join(log_dir, file))
                                deleted_count += 1
                            except:
                                pass
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
    

