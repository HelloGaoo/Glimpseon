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
编辑面板模块
"""

import datetime
import logging
import os
import re

import win32api
import win32con
import win32gui
from PyQt5.QtCore import QEasingCurve, QPropertyAnimation, QRect, Qt, QTimer
from PyQt5.QtGui import QColor, QIcon, QPalette, QPixmap
from PyQt5.QtWidgets import (
    QApplication,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QListWidget,
    QPushButton,
    QVBoxLayout,
    QWidget,
)
from qfluentwidgets import (
    BodyLabel,
    CalendarPicker,
    ComboBox,
    CustomColorSettingCard,
    ExpandGroupSettingCard,
    FluentIcon as FIF,
    InfoBar,
    isDarkTheme,
    LineEdit,
    ListWidget,
    MessageBoxBase,
    PrimaryPushButton,
    PushButton,
    SmoothScrollArea,
    SpinBox,
    StrongBodyLabel,
    SubtitleLabel,
    SwitchButton,
    TimePicker,
    ToolButton,
)

from core.config import cfg, save_cfg
from core.quick_launch_config import ql_cfg

logger = logging.getLogger(__name__)


class EditPanel(QWidget):
    """编辑面板"""
    
    def __init__(self, mainWindow, width=300):
        """初始化编辑面板"""
        super().__init__(parent=mainWindow)
        self.mainWindow = mainWindow
        self._width = width
        self.setFixedWidth(self._width)
        self.setObjectName('EditPanel')
        self.isLeftSide = False
        self.updateTimer = QTimer()
        self.updateTimer.timeout.connect(self._updateCountdownList)
        self.updateTimer.start(1000)
        
        # 设置不透明背景！！！！！！！
        self.setAttribute(Qt.WA_StyledBackground, True)
        self.setAutoFillBackground(True)
        self._updateTheme()
        
        scroll = SmoothScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        scroll.setFrameShape(SmoothScrollArea.NoFrame)
        content = QWidget()
        scroll.setWidget(content)
        v = QVBoxLayout(content)
        v.setContentsMargins(16, 16, 16, 16)
        v.setSpacing(12)
        titleLayout = QHBoxLayout()
        titleLabel = StrongBodyLabel('编辑面板', self)
        titleLayout.addWidget(titleLabel)
        titleLayout.addStretch()
        
        self.positionButton = ToolButton(parent=self)
        self.positionButton.setFixedSize(32, 32)
        self.positionButton.setToolTip('切换到左侧')
        self.positionButton.setIcon(FIF.CARE_LEFT_SOLID)
        self.positionButton.clicked.connect(self._togglePosition)
        titleLayout.addWidget(self.positionButton)
        v.addLayout(titleLayout)
        
        self._addSeparator(v)
        self._createTimeSettings(v)
        self._updateTimeSettingsEnabled(cfg.showClock.value)
        self._addSeparator(v)
        self._createPoetrySettings(v)
        self._updatePoetrySettingsEnabled(cfg.showPoetry.value)
        self._addSeparator(v)
        self._createWeatherSettings(v)
        self._updateWeatherSettingsEnabled(cfg.showWeather.value)
        self._addSeparator(v)
        self._createCountdownSettings(v)
        self._updateCountdownSettingsEnabled(cfg.showCountdown.value)
        self._addSeparator(v)
        self._createSchoolInfoSettings(v)
        self._updateSchoolInfoSettingsEnabled(cfg.showSchoolInfo.value)
        self._addSeparator(v)
        self._createQuickLaunchSettings(v)
        self._updateQuickLaunchSettingsEnabled(ql_cfg.show_quick_launch)
        self._connectConfigSignals()
        self.__connectSignalToSlot()
    
        v.addStretch()
        
        self.closeButton = PushButton('关闭', self, icon=FIF.CLOSE)
        self.closeButton.setFixedHeight(32)
        v.addWidget(self.closeButton)
        self.closeButton.clicked.connect(self.hidePanel)
        
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.addWidget(scroll)
        
        # 动画
        self.anim = QPropertyAnimation(self, b'geometry')
        self.anim.setEasingCurve(QEasingCurve.OutCubic)
        self._updateTheme()
        
        self.hide()
        self.setVisible(False)
    
    def _addSeparator(self, layout):
        """添加分隔线"""
        separator = QWidget()
        separator.setFixedHeight(1)
        separator.setObjectName('separator')
        layout.addWidget(separator)
    
    def _updateTimeSettingsEnabled(self, enabled):
        self.showSecondsSwitch.setEnabled(enabled)
        self.showLunarSwitch.setEnabled(enabled)
        self.clockColorCombo.setEnabled(enabled)
        self.clockSizeSpin.setEnabled(enabled)
        self.dateSizeSpin.setEnabled(enabled)
        self.clockPositionCombo.setEnabled(enabled)
    
    def _updatePoetrySettingsEnabled(self, enabled):
        self.poetryApiCombo.setEnabled(enabled)
        self.poetrySizeSpin.setEnabled(enabled)
        self.poetryUpdateIntervalCombo.setEnabled(enabled)
        self.poetryPositionCombo.setEnabled(enabled)
    
    def _updateWeatherSettingsEnabled(self, enabled):
        self.cityButton.setEnabled(enabled)
        self.weatherSizeSpin.setEnabled(enabled)
        self.weatherIconSizeSpin.setEnabled(enabled)
        self.weatherUpdateIntervalCombo.setEnabled(enabled)
        self.weatherPositionCombo.setEnabled(enabled)
    
    def _updateCountdownSettingsEnabled(self, enabled):
        self.countdownTextColorCombo.setEnabled(enabled)
        self.countdownConnectorColorCombo.setEnabled(enabled)
        self.countdownAddButton.setEnabled(enabled)
        self.countdownListWidget.setEnabled(enabled)
        self.countdownEditButton.setEnabled(enabled)
        self.countdownDeleteButton.setEnabled(enabled)
        self.countdownTextSizeSpin.setEnabled(enabled)
        self.countdownConnectorSizeSpin.setEnabled(enabled)
        self.countdownDisplayModeCombo.setEnabled(enabled)
        self.countdownCarouselIntervalSpin.setEnabled(enabled)
        self.countdownPositionCombo.setEnabled(enabled)
    
    def _updateSchoolInfoSettingsEnabled(self, enabled):
        self.schoolEdit.setEnabled(enabled)
        self.schoolClassEdit.setEnabled(enabled)
        self.schoolInfoPositionCombo.setEnabled(enabled)
        self.schoolInfoTextColorCombo.setEnabled(enabled)
        self.schoolInfoTextSizeSpin.setEnabled(enabled)
    
    def _updateQuickLaunchSettingsEnabled(self, enabled):
        self.showQuickLaunchSwitch.setEnabled(enabled)
        self.quickLaunchEditButton.setEnabled(enabled)
    
    def _connectConfigSignals(self):
        """连接配置变化信到 UI 更新"""
        # 时间设置
        cfg.showClock.valueChanged.connect(self._updateShowClockSwitch)
        cfg.showClockSeconds.valueChanged.connect(self._updateShowSecondsSwitch)
        cfg.showLunarCalendar.valueChanged.connect(self._updateShowLunarSwitch)
        cfg.clockColor.valueChanged.connect(self._updateClockColorCombo)
        cfg.clockSize.valueChanged.connect(self._updateClockSizeSpin)
        cfg.dateSize.valueChanged.connect(self._updateDateSizeSpin)
        cfg.clockPosition.valueChanged.connect(self._updateClockPositionCombo)
        
        # 一言设置
        cfg.showPoetry.valueChanged.connect(self._updateShowPoetrySwitch)
        cfg.poetryApiUrl.valueChanged.connect(self._updatePoetryApiEdit)
        cfg.poetrySize.valueChanged.connect(self._updatePoetrySizeSpin)
        cfg.poetryUpdateInterval.valueChanged.connect(self._updatePoetryUpdateIntervalCombo)
        cfg.poetryPosition.valueChanged.connect(self._updatePoetryPositionCombo)
        
        # 天气设置
        cfg.showWeather.valueChanged.connect(self._updateShowWeatherSwitch)
        cfg.weatherSize.valueChanged.connect(self._updateWeatherSizeSpin)
        cfg.weatherIconSize.valueChanged.connect(self._updateWeatherIconSizeSpin)
        cfg.weatherUpdateInterval.valueChanged.connect(self._updateWeatherUpdateIntervalCombo)
        cfg.city.valueChanged.connect(self._updateCityButton)
        cfg.weatherPosition.valueChanged.connect(self._updateWeatherPositionCombo)
        
        # 倒计时设置
        cfg.showCountdown.valueChanged.connect(self._updateShowCountdownSwitch)
        cfg.countdownDisplayMode.valueChanged.connect(self._updateCountdownDisplayModeCombo)
        cfg.countdownPosition.valueChanged.connect(self._updateCountdownPositionCombo)
        cfg.countdownTextSize.valueChanged.connect(self._updateCountdownTextSizeSpin)
        cfg.countdownConnectorSize.valueChanged.connect(self._updateCountdownConnectorSizeSpin)
        cfg.countdownCarouselInterval.valueChanged.connect(self._updateCountdownCarouselIntervalSpin)
        cfg.countdownList.valueChanged.connect(self._updateCountdownList)
        cfg.countdownTextColor.valueChanged.connect(self._updateCountdownTextColorCombo)
        cfg.countdownConnectorColor.valueChanged.connect(self._updateCountdownConnectorColorCombo)
        
        # 学校信息设置
        cfg.showSchoolInfo.valueChanged.connect(self._updateShowSchoolInfoSwitch)
        cfg.schoolInfoPosition.valueChanged.connect(self._updateSchoolInfoPositionCombo)
        cfg.schoolInfoTextColor.valueChanged.connect(self._updateSchoolInfoTextColorCombo)
        cfg.schoolInfoTextSize.valueChanged.connect(self._updateSchoolInfoTextSizeSpin)
        cfg.school.valueChanged.connect(self._updateSchoolEdit)
        cfg.schoolClass.valueChanged.connect(self._updateSchoolClassEdit)
    
    def __connectSignalToSlot(self):
        cfg.themeChanged.connect(self._onThemeChanged)
        cfg.themeColor.valueChanged.connect(self._onThemeColorChanged)
    
    def _onThemeChanged(self, theme):
        self._updateTheme()
    
    def _onThemeColorChanged(self, value):
        self._updateCountdownTextColorCombo(cfg.countdownTextColor.value)
        self._updateCountdownConnectorColorCombo(cfg.countdownConnectorColor.value)
    
    def _updateShowClockSwitch(self, value):
        """更新启用时钟开关"""
        self.showClockSwitch.setChecked(value)
    
    def _updateShowSecondsSwitch(self, value):
        """更新显示秒针开关"""
        self.showSecondsSwitch.setChecked(value)
    
    def _updateShowLunarSwitch(self, value):
        """更新显示农历开关"""
        self.showLunarSwitch.setChecked(value)
    
    def _updateClockColorCombo(self, value):
        """更新时钟颜色下拉框"""
        self.clockColorCombo.currentTextChanged.disconnect(self._onClockColorChanged)
        self.clockColorCombo.setCurrentText(self._getColorText(value))
        self.clockColorCombo.currentTextChanged.connect(self._onClockColorChanged)
    
    def _updateClockSizeSpin(self, value):
        """更新时钟大小旋转框"""
        self.clockSizeSpin.setValue(value)
    
    def _updateDateSizeSpin(self, value):
        """更新日期大小旋转框"""
        self.dateSizeSpin.setValue(value)
    
    def _updateShowPoetrySwitch(self, value):
        """更新启用一言开关"""
        self.showPoetrySwitch.setChecked(value)
    
    def _updatePoetryApiEdit(self, value):
        """更新一言 API 地址下拉框"""
        self.poetryApiCombo.currentTextChanged.disconnect(self._onPoetryApiChanged)
        if value == 'https://api.imlcd.cn/yy/api.php':
            self.poetryApiCombo.setCurrentText('一言 API')
        elif value == 'https://www.ffapi.cn/int/v1/shici':
            self.poetryApiCombo.setCurrentText('诗词 API')
        else:
            self.poetryApiCombo.setCurrentText('一言 API')
        self.poetryApiCombo.currentTextChanged.connect(self._onPoetryApiChanged)
    
    def _updatePoetrySizeSpin(self, value):
        """更新一言大小旋转框"""
        self.poetrySizeSpin.setValue(value)
    
    def _updatePoetryUpdateIntervalCombo(self, value):
        """更新一言更新间隔下拉框"""
        self.poetryUpdateIntervalCombo.currentTextChanged.disconnect(self._onPoetryUpdateIntervalChanged)
        self.poetryUpdateIntervalCombo.setCurrentText(value)
        self.poetryUpdateIntervalCombo.currentTextChanged.connect(self._onPoetryUpdateIntervalChanged)
    
    def _updateShowWeatherSwitch(self, value):
        """更新启用天气开关"""
        self.showWeatherSwitch.setChecked(value)
    
    def _updateWeatherSizeSpin(self, value):
        """更新天气文字大小旋转框"""
        self.weatherSizeSpin.setValue(value)
    
    def _updateWeatherIconSizeSpin(self, value):
        """更新天气图标大小旋转框"""
        self.weatherIconSizeSpin.setValue(value)
    
    def _updateWeatherUpdateIntervalCombo(self, value):
        """更新天气更新间隔下拉框"""
        self.weatherUpdateIntervalCombo.currentTextChanged.disconnect(self._onWeatherUpdateIntervalChanged)
        self.weatherUpdateIntervalCombo.setCurrentText(value)
        self.weatherUpdateIntervalCombo.currentTextChanged.connect(self._onWeatherUpdateIntervalChanged)
    
    def _updateCityButton(self, value):
        """更新城市按钮"""
        self.cityButton.setText(value)
    
    def _updateClockPositionCombo(self, value):
        """更新时间位置下拉框"""
        self.clockPositionCombo.currentTextChanged.disconnect(self._onClockPositionChanged)
        self.clockPositionCombo.setCurrentText(value)
        self.clockPositionCombo.currentTextChanged.connect(self._onClockPositionChanged)
    
    def _updatePoetryPositionCombo(self, value):
        """更新一言位置下拉框"""
        self.poetryPositionCombo.currentTextChanged.disconnect(self._onPoetryPositionChanged)
        self.poetryPositionCombo.setCurrentText(value)
        self.poetryPositionCombo.currentTextChanged.connect(self._onPoetryPositionChanged)
    
    def _updateWeatherPositionCombo(self, value):
        """更新天气位置下拉框"""
        self.weatherPositionCombo.currentTextChanged.disconnect(self._onWeatherPositionChanged)
        self.weatherPositionCombo.setCurrentText(value)
        self.weatherPositionCombo.currentTextChanged.connect(self._onWeatherPositionChanged)
    
    def _createTimeSettings(self, layout):
        titleLabel = StrongBodyLabel('时间设置', self)
        layout.addWidget(titleLabel)
        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel('启用时钟', self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.showClockSwitch = SwitchButton(self)
        self.showClockSwitch.setChecked(cfg.showClock.value)
        self.showClockSwitch.checkedChanged.connect(self._onShowClockChanged)
        enableLayout.addWidget(self.showClockSwitch)
        layout.addLayout(enableLayout)
        secondsLayout = QHBoxLayout()
        secondsLabel = BodyLabel('显示秒针', self)
        secondsLabel.setFixedWidth(100)
        secondsLayout.addWidget(secondsLabel)
        self.showSecondsSwitch = SwitchButton(self)
        self.showSecondsSwitch.setChecked(cfg.showClockSeconds.value)
        self.showSecondsSwitch.checkedChanged.connect(self._onShowSecondsChanged)
        secondsLayout.addWidget(self.showSecondsSwitch)
        layout.addLayout(secondsLayout)
        lunarLayout = QHBoxLayout()
        lunarLabel = BodyLabel('显示农历', self)
        lunarLabel.setFixedWidth(100)
        lunarLayout.addWidget(lunarLabel)
        self.showLunarSwitch = SwitchButton(self)
        self.showLunarSwitch.setChecked(cfg.showLunarCalendar.value)
        self.showLunarSwitch.checkedChanged.connect(self._onShowLunarChanged)
        lunarLayout.addWidget(self.showLunarSwitch)
        layout.addLayout(lunarLayout)
        colorLayout = QHBoxLayout()
        colorLabel = BodyLabel('时钟颜色', self)
        colorLabel.setFixedWidth(100)
        colorLayout.addWidget(colorLabel)
        self.clockColorCombo = ComboBox(self)
        self.clockColorCombo.addItems(['主要颜色', '白色', '黑色'])
        self.clockColorCombo.setCurrentText(self._getColorText(cfg.clockColor.value))
        self.clockColorCombo.setFixedWidth(120)
        self.clockColorCombo.currentTextChanged.connect(self._onClockColorChanged)
        colorLayout.addWidget(self.clockColorCombo)
        layout.addLayout(colorLayout)
        clockSizeLayout = QHBoxLayout()
        clockSizeLabel = BodyLabel('时钟大小', self)
        clockSizeLabel.setFixedWidth(100)
        clockSizeLayout.addWidget(clockSizeLabel)
        self.clockSizeSpin = SpinBox(self)
        self.clockSizeSpin.setRange(80, 200)
        self.clockSizeSpin.setValue(cfg.clockSize.value)
        self.clockSizeSpin.setFixedWidth(120)
        self.clockSizeSpin.valueChanged.connect(self._onClockSizeChanged)
        clockSizeLayout.addWidget(self.clockSizeSpin)
        layout.addLayout(clockSizeLayout)
        dateSizeLayout = QHBoxLayout()
        dateSizeLabel = BodyLabel('日期大小', self)
        dateSizeLabel.setFixedWidth(100)
        dateSizeLayout.addWidget(dateSizeLabel)
        self.dateSizeSpin = SpinBox(self)
        self.dateSizeSpin.setRange(12, 50)
        self.dateSizeSpin.setValue(cfg.dateSize.value)
        self.dateSizeSpin.setFixedWidth(120)
        self.dateSizeSpin.valueChanged.connect(self._onDateSizeChanged)
        dateSizeLayout.addWidget(self.dateSizeSpin)
        layout.addLayout(dateSizeLayout)
        positionLayout = QHBoxLayout()
        positionLabel = BodyLabel('时间位置', self)
        positionLabel.setFixedWidth(100)
        positionLayout.addWidget(positionLabel)
        self.clockPositionCombo = ComboBox(self)
        self.clockPositionCombo.addItems(['左上预留', '左上', '右上预留', '右上', '左下预留', '左下', '右下预留', '右下', '中部', '顶部', '顶部偏下', '底部偏上', '底部'])
        self.clockPositionCombo.setCurrentText(cfg.clockPosition.value)
        self.clockPositionCombo.setFixedWidth(120)
        self.clockPositionCombo.currentTextChanged.connect(self._onClockPositionChanged)
        positionLayout.addWidget(self.clockPositionCombo)
        layout.addLayout(positionLayout)

    def _createPoetrySettings(self, layout):
        titleLabel = StrongBodyLabel('一言设置', self)
        layout.addWidget(titleLabel)
        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel('启用一言', self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.showPoetrySwitch = SwitchButton(self)
        self.showPoetrySwitch.setChecked(cfg.showPoetry.value)
        self.showPoetrySwitch.checkedChanged.connect(self._onShowPoetryChanged)
        enableLayout.addWidget(self.showPoetrySwitch)
        layout.addLayout(enableLayout)
        apiLayout = QHBoxLayout()
        apiLabel = BodyLabel('一言 API 地址', self)
        apiLabel.setFixedWidth(100)
        apiLayout.addWidget(apiLabel)
        self.poetryApiCombo = ComboBox(self)
        self.poetryApiCombo.addItems([
            '一言 API',
            '诗词 API'
        ])
        if cfg.poetryApiUrl.value == 'https://www.ffapi.cn/int/v1/shici':
            self.poetryApiCombo.setCurrentText('诗词 API')
        else:
            self.poetryApiCombo.setCurrentText('一言 API')
        self.poetryApiCombo.setFixedWidth(120)
        self.poetryApiCombo.currentTextChanged.connect(self._onPoetryApiChanged)
        apiLayout.addWidget(self.poetryApiCombo)
        layout.addLayout(apiLayout)
        poetrySizeLayout = QHBoxLayout()
        poetrySizeLabel = BodyLabel('一言大小', self)
        poetrySizeLabel.setFixedWidth(100)
        poetrySizeLayout.addWidget(poetrySizeLabel)
        self.poetrySizeSpin = SpinBox(self)
        self.poetrySizeSpin.setRange(12, 50)
        self.poetrySizeSpin.setValue(cfg.poetrySize.value)
        self.poetrySizeSpin.setFixedWidth(120)
        self.poetrySizeSpin.valueChanged.connect(self._onPoetrySizeChanged)
        poetrySizeLayout.addWidget(self.poetrySizeSpin)
        layout.addLayout(poetrySizeLayout)
        poetryIntervalLayout = QHBoxLayout()
        poetryIntervalLabel = BodyLabel('一言更新间隔', self)
        poetryIntervalLabel.setFixedWidth(100)
        poetryIntervalLayout.addWidget(poetryIntervalLabel)
        self.poetryUpdateIntervalCombo = ComboBox(self)
        self.poetryUpdateIntervalCombo.addItems(['从不', '5 分钟', '10 分钟', '30 分钟', '1 小时', '3 小时', '6 小时', '12 小时', '1 天'])
        self.poetryUpdateIntervalCombo.setCurrentText(cfg.poetryUpdateInterval.value)
        self.poetryUpdateIntervalCombo.setFixedWidth(120)
        self.poetryUpdateIntervalCombo.currentTextChanged.connect(self._onPoetryUpdateIntervalChanged)
        poetryIntervalLayout.addWidget(self.poetryUpdateIntervalCombo)
        layout.addLayout(poetryIntervalLayout)
        poetryPositionLayout = QHBoxLayout()
        poetryPositionLabel = BodyLabel('一言位置', self)
        poetryPositionLabel.setFixedWidth(100)
        poetryPositionLayout.addWidget(poetryPositionLabel)
        self.poetryPositionCombo = ComboBox(self)
        self.poetryPositionCombo.addItems(['顶部预留', '底部预留'])
        self.poetryPositionCombo.setCurrentText(cfg.poetryPosition.value)
        self.poetryPositionCombo.setFixedWidth(120)
        self.poetryPositionCombo.currentTextChanged.connect(self._onPoetryPositionChanged)
        poetryPositionLayout.addWidget(self.poetryPositionCombo)
        layout.addLayout(poetryPositionLayout)
    
    def _createWeatherSettings(self, layout):
        """创建天气设置部分"""
        titleLabel = StrongBodyLabel('天气设置', self)
        layout.addWidget(titleLabel)
        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel('启用天气', self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.showWeatherSwitch = SwitchButton(self)
        self.showWeatherSwitch.setChecked(cfg.showWeather.value)
        self.showWeatherSwitch.checkedChanged.connect(self._onShowWeatherChanged)
        enableLayout.addWidget(self.showWeatherSwitch)
        layout.addLayout(enableLayout)
        cityLayout = QHBoxLayout()
        cityLabel = BodyLabel('城市', self)
        cityLabel.setFixedWidth(100)
        cityLayout.addWidget(cityLabel)
        self.cityButton = PushButton(cfg.city.value, self)
        self.cityButton.setFixedWidth(120)
        self.cityButton.clicked.connect(self._onCityButtonClicked)
        cityLayout.addWidget(self.cityButton)
        layout.addLayout(cityLayout)
        weatherSizeLayout = QHBoxLayout()
        weatherSizeLabel = BodyLabel('天气文字大小', self)
        weatherSizeLabel.setFixedWidth(100)
        weatherSizeLayout.addWidget(weatherSizeLabel)
        self.weatherSizeSpin = SpinBox(self)
        self.weatherSizeSpin.setRange(5, 50)
        self.weatherSizeSpin.setValue(cfg.weatherSize.value)
        self.weatherSizeSpin.setFixedWidth(120)
        self.weatherSizeSpin.valueChanged.connect(self._onWeatherSizeChanged)
        weatherSizeLayout.addWidget(self.weatherSizeSpin)
        layout.addLayout(weatherSizeLayout)
        iconSizeLayout = QHBoxLayout()
        iconSizeLabel = BodyLabel('天气图标大小', self)
        iconSizeLabel.setFixedWidth(100)
        iconSizeLayout.addWidget(iconSizeLabel)
        self.weatherIconSizeSpin = SpinBox(self)
        self.weatherIconSizeSpin.setRange(32, 128)
        self.weatherIconSizeSpin.setValue(cfg.weatherIconSize.value)
        self.weatherIconSizeSpin.setFixedWidth(120)
        self.weatherIconSizeSpin.valueChanged.connect(self._onWeatherIconSizeChanged)
        iconSizeLayout.addWidget(self.weatherIconSizeSpin)
        layout.addLayout(iconSizeLayout)
        weatherIntervalLayout = QHBoxLayout()
        weatherIntervalLabel = BodyLabel('天气更新间隔', self)
        weatherIntervalLabel.setFixedWidth(100)
        weatherIntervalLayout.addWidget(weatherIntervalLabel)
        self.weatherUpdateIntervalCombo = ComboBox(self)
        self.weatherUpdateIntervalCombo.addItems(['从不', '5 分钟', '15 分钟', '30 分钟', '1 小时', '3 小时', '6 小时', '12 小时', '24 小时'])
        self.weatherUpdateIntervalCombo.setCurrentText(cfg.weatherUpdateInterval.value)
        self.weatherUpdateIntervalCombo.setFixedWidth(120)
        self.weatherUpdateIntervalCombo.currentTextChanged.connect(self._onWeatherUpdateIntervalChanged)
        weatherIntervalLayout.addWidget(self.weatherUpdateIntervalCombo)
        layout.addLayout(weatherIntervalLayout)
        weatherPositionLayout = QHBoxLayout()
        weatherPositionLabel = BodyLabel('天气位置', self)
        weatherPositionLabel.setFixedWidth(100)
        weatherPositionLayout.addWidget(weatherPositionLabel)
        self.weatherPositionCombo = ComboBox(self)
        self.weatherPositionCombo.addItems(['左上预留', '右上预留', '左下预留', '右下预留'])
        self.weatherPositionCombo.setCurrentText(cfg.weatherPosition.value)
        self.weatherPositionCombo.setFixedWidth(120)
        self.weatherPositionCombo.currentTextChanged.connect(self._onWeatherPositionChanged)
        weatherPositionLayout.addWidget(self.weatherPositionCombo)
        layout.addLayout(weatherPositionLayout)
    
    def _updateTheme(self):
        """更新主题"""
        from pathlib import Path
        if isDarkTheme():
            qss_path = Path(__file__).parent.parent / 'resource' / 'qss' / 'dark' / 'edit_panel.qss'
        else:
            qss_path = Path(__file__).parent.parent / 'resource' / 'qss' / 'light' / 'edit_panel.qss'
        
        if qss_path.exists():
            try:
                with open(qss_path, 'r', encoding='utf-8') as f:
                    qss_content = f.read()
                self.setStyleSheet(qss_content)
            except Exception as e:
                logger.error(f"加载编辑面板样式失败：{e}")
        else:
            logger.warning(f"编辑面板样式文件不存在：{qss_path}")
    
    def showPanel(self):
        """显示编辑面板"""
        parent = self.parent()
        if not parent:
            return
        
        pr = parent.rect()
        if self.isLeftSide:
            end_rect = QRect(0, 0, self._width, pr.height())
            start_rect = QRect(-self._width, 0, self._width, pr.height())
        else:
            end_rect = QRect(pr.width() - self._width, 0, self._width, pr.height())
            start_rect = QRect(pr.width(), 0, self._width, pr.height())
        
        self.setGeometry(start_rect)
        self.show()
        self.updateTimer.start(1000)
        
        try:
            self.anim.finished.disconnect(self._onShowFinished)
        except Exception:
            pass
        self.anim.stop()
        self.anim.setDuration(300)
        self.anim.setStartValue(start_rect)
        self.anim.setEndValue(end_rect)
        self.anim.start()
    
    def _onShowFinished(self):
        """显示动画完成"""
        try:
            self.anim.finished.disconnect(self._onShowFinished)
        except Exception:
            pass

    def hidePanel(self):
        """退出"""
        parent = self.parent()
        if not parent:
            return
        
        if hasattr(parent, 'isEditMode'):
            parent.isEditMode = False
        
        # 启用导航栏
        if hasattr(parent, 'navigationInterface'):
            parent.navigationInterface.setEnabled(True)
        
        pr = parent.rect()
        if self.isLeftSide:
            start_rect = QRect(0, 0, self._width, pr.height())
            end_rect = QRect(-self._width, 0, self._width, pr.height())
        else:
            start_rect = QRect(pr.width() - self._width, 0, self._width, pr.height())
            end_rect = QRect(pr.width(), 0, self._width, pr.height())
        
        # 滑出动画
        self.anim.stop()
        self.anim.setDuration(250)
        self.anim.setStartValue(start_rect)
        self.anim.setEndValue(end_rect)
        
        try:
            self.anim.finished.disconnect(self._onHideFinished)
        except Exception:
            pass
        self.anim.finished.connect(self._onHideFinished)
        self.anim.start()

    def _onHideFinished(self):
        """隐藏动画完成"""
        try:
            self.updateTimer.stop()
            self.hide()
        finally:
            try:
                self.anim.finished.disconnect(self._onHideFinished)
            except Exception:
                pass

    def _onDelete(self):
        """删除组件"""
        if hasattr(self.mainWindow, 'deleteSelectedComponent'):
            self.mainWindow.deleteSelectedComponent()
    
    def _onShowClockChanged(self, checked: bool):
        """启用时钟开关变化"""
        cfg.showClock.value = checked
        self._updateTimeSettingsEnabled(checked)
        if hasattr(self.mainWindow, '_MainWindow__updateClock'):
            self.mainWindow._MainWindow__updateClock()
        logger.info(f"时间设置：启用时钟={'开启' if checked else '关闭'}")
    
    def _onShowSecondsChanged(self, checked: bool):
        """显示秒针开关变化"""
        cfg.showClockSeconds.value = checked
        if hasattr(self.mainWindow, '_MainWindow__updateClock'):
            self.mainWindow._MainWindow__updateClock()
        logger.info(f"时间设置：显示秒针={'开启' if checked else '关闭'}")
    
    def _onShowLunarChanged(self, checked: bool):
        """显示农历开关变化"""
        cfg.showLunarCalendar.value = checked
        if hasattr(self.mainWindow, '_MainWindow__updateClock'):
            self.mainWindow._MainWindow__updateClock()
        logger.info(f"时间设置：显示农历={'开启' if checked else '关闭'}")
    
    def _getColorText(self, color, default='main'):
        """获取颜色文本表示"""
        if not hasattr(color, 'name'):
            if default == 'red':return '红色'
            elif default == 'white':return '白色'
            return '主要颜色'
        color_hex = color.name().upper()
        try:
            theme_color = cfg.themeColor.value
            if hasattr(theme_color, 'name'):
                theme_hex = theme_color.name().upper()
                if theme_hex == color_hex:return '主要颜色'
        except Exception:pass
        if color_hex == '#FF0000':return '红色'
        elif color_hex == '#FFFFFF':return '白色'
        elif color_hex == '#000000':return '黑色'
        return '主要颜色'
    
    def _onClockColorChanged(self, text: str):
        """时钟颜色变化"""
        from PyQt5.QtGui import QColor
        
        if text == '白色':cfg.clockColor.value = QColor(255, 255, 255)
        elif text == '黑色':cfg.clockColor.value = QColor(0, 0, 0)
        else:cfg.clockColor.value = cfg.themeColor.value
        
        if hasattr(self.mainWindow, 'updateClockStyle'):
            self.mainWindow.updateClockStyle()
        logger.info(f"时间设置：时钟颜色={text}")
    
    def _onClockSizeChanged(self, value: int):
        """时钟大小变化"""
        cfg.clockSize.value = value
        if hasattr(self.mainWindow, 'updateClockStyle'):self.mainWindow.updateClockStyle()
        logger.info(f"时间设置：时钟大小={value}px")
    
    def _onDateSizeChanged(self, value: int):
        """日期大小变化"""
        cfg.dateSize.value = value
        if hasattr(self.mainWindow, 'updateClockStyle'):self.mainWindow.updateClockStyle()
        logger.info(f"时间设置：日期大小={value}px")
    
    def _onShowPoetryChanged(self, checked: bool):
        """启用一言开关变化"""
        cfg.showPoetry.value = checked
        self._updatePoetrySettingsEnabled(checked)
        if hasattr(self.mainWindow, 'homeContent'):
            # 更新所有一言组件的可见性
            for widget in self.mainWindow.homeContent.findChildren(QWidget):
                if widget.objectName() == 'poetryWidget':
                    widget.setVisible(checked)
        logger.info(f"一言设置：启用一言={'开启' if checked else '关闭'}")
    
    def _onPoetryApiChanged(self, text: str):
        """一言 API 地址变化"""
        if text == '一言 API':
            cfg.poetryApiUrl.value = 'https://api.imlcd.cn/yy/api.php'
        elif text == '诗词 API':
            cfg.poetryApiUrl.value = 'https://www.ffapi.cn/int/v1/shici'
        else:
            cfg.poetryApiUrl.value = 'https://api.imlcd.cn/yy/api.php'
        if hasattr(self.mainWindow, '_MainWindow__updatePoetry'):
            self.mainWindow._MainWindow__updatePoetry()
        logger.info(f"一言设置：API 地址={cfg.poetryApiUrl.value}")
    
    def _onPoetryUpdateIntervalChanged(self, text: str):
        """一言更新间隔变化"""
        cfg.poetryUpdateInterval.value = text
        if hasattr(self.mainWindow, '_MainWindow__updatePoetryInterval'):
            self.mainWindow._MainWindow__updatePoetryInterval()
        logger.info(f"一言设置：更新间隔={text}")
    
    def _onPoetrySizeChanged(self, value: int):
        """一言大小变化"""
        cfg.poetrySize.value = value
        if hasattr(self.mainWindow, 'updateClockStyle'):
            self.mainWindow.updateClockStyle()
        logger.info(f"一言设置：一言大小={value}px")

    def _onShowWeatherChanged(self, checked: bool):
        """启用天气开关变化"""
        cfg.showWeather.value = checked
        self._updateWeatherSettingsEnabled(checked)
        if hasattr(self.mainWindow, '_MainWindow__updateWeather'):
            self.mainWindow._MainWindow__updateWeather()
        logger.info(f"天气设置：启用天气={'开启' if checked else '关闭'}")
    
    def _onWeatherSizeChanged(self, value: int):
        """天气文字大小变化"""
        cfg.weatherSize.value = value
        if hasattr(self.mainWindow, 'updateClockStyle'):
            self.mainWindow.updateClockStyle()
        logger.info(f"天气设置：天气文字大小={value}px")
    
    def _onWeatherIconSizeChanged(self, value: int):
        """天气图标大小变化"""
        cfg.weatherIconSize.value = value
        if hasattr(self.mainWindow, '_MainWindow__updateWeatherIcon'):
            self.mainWindow._MainWindow__updateWeatherIcon()
        logger.info(f"天气设置：天气图标大小={value}px")
    
    def _onWeatherUpdateIntervalChanged(self, text: str):
        """天气更新间隔变化"""
        cfg.weatherUpdateInterval.value = text
        if hasattr(self.mainWindow, '_MainWindow__updateWeatherInterval'):
            self.mainWindow._MainWindow__updateWeatherInterval()
        logger.info(f"天气设置：更新间隔={text}")
    
    def _onClockPositionChanged(self, text: str):
        """时间位置变化"""
        cfg.clockPosition.value = text
        if hasattr(self.mainWindow, '_MainWindow__updateClockPosition'):
            self.mainWindow._MainWindow__updateClockPosition()
        logger.info(f"时间设置：位置={text}")
    
    def _onPoetryPositionChanged(self, text: str):
        """一言位置变化"""
        cfg.poetryPosition.value = text
        if hasattr(self.mainWindow, '_MainWindow__updatePoetryPosition'):
            self.mainWindow._MainWindow__updatePoetryPosition()
        logger.info(f"一言设置：位置={text}")
    
    def _onWeatherPositionChanged(self, text: str):
        """天气位置变化"""
        cfg.weatherPosition.value = text
        if hasattr(self.mainWindow, '_MainWindow__updateWeatherPosition'):
            self.mainWindow._MainWindow__updateWeatherPosition()
        logger.info(f"天气设置：位置={text}")
    
    def _onCityButtonClicked(self):
        """城市选择按钮点击"""
        from ui.city_selector import RegionSelectorDialog
        dialog = RegionSelectorDialog(self.mainWindow)
        if dialog.exec():
            selected_region = dialog.get_selected_region()
            if selected_region:
                cfg.city.value = selected_region
                logger.info(f"天气设置：城市={selected_region}")
                if hasattr(self.mainWindow, '_MainWindow__updateWeather'):
                    self.mainWindow._MainWindow__updateWeather()
    
    def _togglePosition(self):
        """切换编辑面板位置"""
        self.isLeftSide = not self.isLeftSide
        if self.isLeftSide:
            self.positionButton.setIcon(FIF.CARE_RIGHT_SOLID)
            self.positionButton.setToolTip('切换到右侧')
        else:
            self.positionButton.setIcon(FIF.CARE_LEFT_SOLID)
            self.positionButton.setToolTip('切换到左侧')
        if hasattr(self.mainWindow, '_MainWindow__updateEditButtonPosition'):
            self.mainWindow._MainWindow__updateEditButtonPosition()
        
        if self.isVisible():
            self.showPanel()
    
    def updatePositionOnResize(self):
        if not self.isVisible():return
        parent = self.parent()
        pr = parent.rect()
        if self.isLeftSide:new_rect = QRect(0, 0, self._width, pr.height())
        else:new_rect = QRect(pr.width() - self._width, 0, self._width, pr.height())
        self.anim.stop()
        self.setGeometry(new_rect)
    
    def _createCountdownSettings(self, layout):
        """创建倒计时设置"""
        layout.setSpacing(8)
        
        titleLabel = StrongBodyLabel('倒计时设置', self)
        layout.addWidget(titleLabel)
        
        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel('启用倒计时', self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.showCountdownSwitch = SwitchButton(self)
        self.showCountdownSwitch.setChecked(cfg.showCountdown.value)
        self.showCountdownSwitch.checkedChanged.connect(self._onShowCountdownChanged)
        enableLayout.addWidget(self.showCountdownSwitch)
        layout.addLayout(enableLayout)
        
        # 文字颜色
        textColorLayout = QHBoxLayout()
        textColorLabel = BodyLabel('文字颜色', self)
        textColorLabel.setFixedWidth(100)
        textColorLayout.addWidget(textColorLabel)
        self.countdownTextColorCombo = ComboBox(self)
        self.countdownTextColorCombo.addItems(['红色', '白色', '黑色', '主要颜色'])
        self.countdownTextColorCombo.setCurrentText(self._getColorText(cfg.countdownTextColor.value, 'red'))
        self.countdownTextColorCombo.setFixedWidth(120)
        self.countdownTextColorCombo.currentTextChanged.connect(self._onCountdownTextColorChanged)
        textColorLayout.addWidget(self.countdownTextColorCombo)
        layout.addLayout(textColorLayout)
        
        # 连接词颜色
        connectorColorLayout = QHBoxLayout()
        connectorColorLabel = BodyLabel('连接词颜色', self)
        connectorColorLabel.setFixedWidth(100)
        connectorColorLayout.addWidget(connectorColorLabel)
        self.countdownConnectorColorCombo = ComboBox(self)
        self.countdownConnectorColorCombo.addItems(['红色', '白色', '黑色', '主要颜色'])
        self.countdownConnectorColorCombo.setCurrentText(self._getColorText(cfg.countdownConnectorColor.value, 'white'))
        self.countdownConnectorColorCombo.setFixedWidth(120)
        self.countdownConnectorColorCombo.currentTextChanged.connect(self._onCountdownConnectorColorChanged)
        connectorColorLayout.addWidget(self.countdownConnectorColorCombo)
        layout.addLayout(connectorColorLayout)
        
        # 文字大小
        textSizeLayout = QHBoxLayout()
        textSizeLabel = BodyLabel('文字大小', self)
        textSizeLabel.setFixedWidth(100)
        textSizeLayout.addWidget(textSizeLabel)
        self.countdownTextSizeSpin = SpinBox(self)
        self.countdownTextSizeSpin.setRange(12, 120)
        self.countdownTextSizeSpin.setValue(cfg.countdownTextSize.value)
        self.countdownTextSizeSpin.setFixedWidth(120)
        self.countdownTextSizeSpin.valueChanged.connect(self._onCountdownTextSizeChanged)
        textSizeLayout.addWidget(self.countdownTextSizeSpin)
        layout.addLayout(textSizeLayout)
        
        # 连接词大小
        connectorSizeLayout = QHBoxLayout()
        connectorSizeLabel = BodyLabel('连接词大小', self)
        connectorSizeLabel.setFixedWidth(100)
        connectorSizeLayout.addWidget(connectorSizeLabel)
        self.countdownConnectorSizeSpin = SpinBox(self)
        self.countdownConnectorSizeSpin.setRange(12, 60)
        self.countdownConnectorSizeSpin.setValue(cfg.countdownConnectorSize.value)
        self.countdownConnectorSizeSpin.setFixedWidth(120)
        self.countdownConnectorSizeSpin.valueChanged.connect(self._onCountdownConnectorSizeChanged)
        connectorSizeLayout.addWidget(self.countdownConnectorSizeSpin)
        layout.addLayout(connectorSizeLayout)
        
        # 显示模式
        displayModeLayout = QHBoxLayout()
        displayModeLabel = BodyLabel('显示模式', self)
        displayModeLabel.setFixedWidth(100)
        displayModeLayout.addWidget(displayModeLabel)
        self.countdownDisplayModeCombo = ComboBox(self)
        self.countdownDisplayModeCombo.addItems(['同时显示', '轮播显示'])
        self.countdownDisplayModeCombo.setCurrentText('同时显示' if cfg.countdownDisplayMode.value == 'simultaneous' else '轮播显示')
        self.countdownDisplayModeCombo.setFixedWidth(120)
        self.countdownDisplayModeCombo.currentTextChanged.connect(self._onCountdownDisplayModeChanged)
        displayModeLayout.addWidget(self.countdownDisplayModeCombo)
        layout.addLayout(displayModeLayout)
        
        # 轮播间隔
        carouselIntervalLayout = QHBoxLayout()
        carouselIntervalLabel = BodyLabel('轮播间隔', self)
        carouselIntervalLabel.setFixedWidth(100)
        carouselIntervalLayout.addWidget(carouselIntervalLabel)
        self.countdownCarouselIntervalSpin = SpinBox(self)
        self.countdownCarouselIntervalSpin.setRange(1, 60)
        self.countdownCarouselIntervalSpin.setValue(cfg.countdownCarouselInterval.value)
        self.countdownCarouselIntervalSpin.setFixedWidth(120)
        self.countdownCarouselIntervalSpin.valueChanged.connect(self._onCountdownCarouselIntervalChanged)
        carouselIntervalLayout.addWidget(self.countdownCarouselIntervalSpin)
        layout.addLayout(carouselIntervalLayout)
        
        # 倒计时位置
        positionLayout = QHBoxLayout()
        positionLabel = BodyLabel('倒计时位置', self)
        positionLabel.setFixedWidth(100)
        positionLayout.addWidget(positionLabel)
        self.countdownPositionCombo = ComboBox(self)
        self.countdownPositionCombo.addItems(['左上预留', '左上', '右上预留', '右上', '左下预留', '左下', '右下预留', '右下', '中部', '顶部', '顶部偏下', '底部偏上', '底部'])
        self.countdownPositionCombo.setCurrentText(cfg.countdownPosition.value)
        self.countdownPositionCombo.setFixedWidth(120)
        self.countdownPositionCombo.currentTextChanged.connect(self._onCountdownPositionChanged)
        positionLayout.addWidget(self.countdownPositionCombo)
        layout.addLayout(positionLayout)
        
        listLabel = BodyLabel('倒计时列表', self)
        layout.addWidget(listLabel)
        
        self.countdownListWidget = ListWidget(self)
        self.countdownListWidget.setFixedHeight(120)
        self._updateCountdownList()
        layout.addWidget(self.countdownListWidget)
        
        buttonLayout = QHBoxLayout()
        self.countdownAddButton = PushButton('添加', self)
        self.countdownAddButton.clicked.connect(self._onCountdownAddClicked)
        buttonLayout.addWidget(self.countdownAddButton)
        self.countdownEditButton = PushButton('编辑', self)
        self.countdownEditButton.clicked.connect(self._onCountdownEditClicked)
        buttonLayout.addWidget(self.countdownEditButton)
        self.countdownDeleteButton = PushButton('删除', self)
        self.countdownDeleteButton.clicked.connect(self._onCountdownDeleteClicked)
        buttonLayout.addWidget(self.countdownDeleteButton)
        layout.addLayout(buttonLayout)
    
    def _updateShowCountdownSwitch(self, value):
        self.showCountdownSwitch.setChecked(value)
        self._updateCountdownSettingsEnabled(value)
    
    def _updateCountdownDisplayModeCombo(self, value):
        self.countdownDisplayModeCombo.currentTextChanged.disconnect(self._onCountdownDisplayModeChanged)
        self.countdownDisplayModeCombo.setCurrentText('同时显示' if value == 'simultaneous' else '轮播显示')
        self.countdownDisplayModeCombo.currentTextChanged.connect(self._onCountdownDisplayModeChanged)
    
    def _updateCountdownPositionCombo(self, value):
        self.countdownPositionCombo.currentTextChanged.disconnect(self._onCountdownPositionChanged)
        self.countdownPositionCombo.setCurrentText(value)
        self.countdownPositionCombo.currentTextChanged.connect(self._onCountdownPositionChanged)
    
    def _updateCountdownTextSizeSpin(self, value):
        self.countdownTextSizeSpin.setValue(value)
    
    def _updateCountdownConnectorSizeSpin(self, value):
        self.countdownConnectorSizeSpin.setValue(value)
    
    def _updateCountdownCarouselIntervalSpin(self, value):
        self.countdownCarouselIntervalSpin.setValue(value)
    
    def _updateCountdownTextColorCombo(self, value):
        self.countdownTextColorCombo.currentTextChanged.disconnect(self._onCountdownTextColorChanged)
        self.countdownTextColorCombo.setCurrentText(self._getColorText(value, 'red'))
        self.countdownTextColorCombo.currentTextChanged.connect(self._onCountdownTextColorChanged)
    
    def _updateCountdownConnectorColorCombo(self, value):
        self.countdownConnectorColorCombo.currentTextChanged.disconnect(self._onCountdownConnectorColorChanged)
        self.countdownConnectorColorCombo.setCurrentText(self._getColorText(value, 'white'))
        self.countdownConnectorColorCombo.currentTextChanged.connect(self._onCountdownConnectorColorChanged)
    
    def _updateShowSchoolInfoSwitch(self, value):
        self.schoolInfoSwitch.setChecked(value)
        self._updateSchoolInfoSettingsEnabled(value)
    
    def _updateSchoolInfoPositionCombo(self, value):
        self.schoolInfoPositionCombo.currentTextChanged.disconnect(self._onSchoolInfoPositionChanged)
        self.schoolInfoPositionCombo.setCurrentText(value)
        self.schoolInfoPositionCombo.currentTextChanged.connect(self._onSchoolInfoPositionChanged)
    
    def _updateSchoolInfoTextColorCombo(self, value):
        self.schoolInfoTextColorCombo.currentTextChanged.disconnect(self._onSchoolInfoTextColorChanged)
        self.schoolInfoTextColorCombo.setCurrentText(self._getColorText(value, 'white'))
        self.schoolInfoTextColorCombo.currentTextChanged.connect(self._onSchoolInfoTextColorChanged)
    
    def _updateSchoolInfoTextSizeSpin(self, value):
        self.schoolInfoTextSizeSpin.setValue(value)
    
    def _updateSchoolEdit(self, value):
        self.schoolEdit.setText(value)
    
    def _updateSchoolClassEdit(self, value):
        self.schoolClassEdit.setText(value)
    
    def _formatRemainingTime(self, target_time_str):
        try:
            target = datetime.datetime.strptime(target_time_str, '%Y-%m-%d %H:%M')
            now = datetime.datetime.now()
            delta = target - now
            total_seconds = int(delta.total_seconds())
            target_date = target.date()
            now_date = now.date()
            if target_date == now_date and total_seconds < 0:
                return "就在今天"
            elif total_seconds > 0:
                days = total_seconds // 86400
                hours = (total_seconds % 86400) // 3600
                minutes = (total_seconds % 3600) // 60
                seconds = total_seconds % 60
                if days >= 3:
                    return f"{days}天"
                elif days >= 1:
                    return f"{days}天{hours}时"
                elif hours >= 1:
                    return f"{hours}时"
                elif minutes >= 1:
                    return f"{minutes}分{seconds}秒"
                else:
                    return f"{seconds}秒"
            else:
                return f"已过去{abs(total_seconds) // 86400}天"
        except:
            return ""
    
    def _updateCountdownList(self):
        current_row = self.countdownListWidget.currentRow()
        self.countdownListWidget.clear()
        countdown_list = cfg.countdownList.value or []
        for cd in countdown_list:
            title = cd.get('title', '')
            target_time = cd.get('target_time', '')
            if title and target_time:
                remaining = self._formatRemainingTime(target_time)
                if remaining:
                    self.countdownListWidget.addItem(f"{title} {remaining}")
        if 0 <= current_row < self.countdownListWidget.count():
            self.countdownListWidget.setCurrentRow(current_row)
    
    def _onShowCountdownChanged(self, checked: bool):
        cfg.showCountdown.value = checked
        self._updateCountdownSettingsEnabled(checked)
        if hasattr(self.mainWindow, '_MainWindow__updateCountdown'):
            self.mainWindow._MainWindow__updateCountdown()
        logger.info(f"倒计时设置：启用倒计时={'开启' if checked else '关闭'}")
    
    def _onCountdownDisplayModeChanged(self, text: str):
        cfg.countdownDisplayMode.value = 'simultaneous' if text == '同时显示' else 'carousel'
        if hasattr(self.mainWindow, '_MainWindow__updateCountdown'):
            self.mainWindow._MainWindow__updateCountdown()
        logger.info(f"倒计时设置：显示模式={text}")
    
    def _onCountdownTextSizeChanged(self, value: int):
        cfg.countdownTextSize.value = value
        if hasattr(self.mainWindow, 'updateCountdownStyle'):
            self.mainWindow.updateCountdownStyle()
        logger.info(f"倒计时设置：文字大小={value}px")
    
    def _onCountdownConnectorSizeChanged(self, value: int):
        cfg.countdownConnectorSize.value = value
        if hasattr(self.mainWindow, 'updateCountdownStyle'):
            self.mainWindow.updateCountdownStyle()
        logger.info(f"倒计时设置：连接词大小={value}px")
    
    def _onCountdownCarouselIntervalChanged(self, value: int):
        cfg.countdownCarouselInterval.value = value
        if hasattr(self.mainWindow, '_MainWindow__updateCountdownCarouselInterval'):
            self.mainWindow._MainWindow__updateCountdownCarouselInterval()
        logger.info(f"倒计时设置：轮播间隔={value}秒")
    
    def _onCountdownPositionChanged(self, text: str):
        cfg.countdownPosition.value = text
        if hasattr(self.mainWindow, '_MainWindow__updateCountdownPosition'):
            self.mainWindow._MainWindow__updateCountdownPosition()
        logger.info(f"倒计时设置：位置={text}")
    
    def _onCountdownAddClicked(self):
        dialog = CountdownEditDialog(self.mainWindow)
        if dialog.exec():
            countdown_data = dialog.get_countdown()
            if countdown_data:
                countdown_list = cfg.countdownList.value or []
                countdown_list.append(countdown_data)
                cfg.countdownList.value = countdown_list
                save_cfg()
                current_row = self.countdownListWidget.currentRow()
                self._updateCountdownList()
                if self.countdownListWidget.count() > 0:
                    self.countdownListWidget.setCurrentRow(self.countdownListWidget.count() - 1)
                if hasattr(self.mainWindow, '_MainWindow__updateCountdown'):
                    self.mainWindow._MainWindow__updateCountdown()
                logger.info(f"倒计时设置：添加倒计时={countdown_data}")
    
    def _onCountdownEditClicked(self):
        current_row = self.countdownListWidget.currentRow()
        if current_row < 0:
            InfoBar.warning('编辑倒计时', '请先选择一个倒计时', parent=self, duration=3000)
            return
        countdown_list = cfg.countdownList.value or []
        if current_row >= len(countdown_list):
            return
        
        dialog = CountdownEditDialog(self.mainWindow, countdown_list[current_row])
        if dialog.exec():
            countdown_data = dialog.get_countdown()
            if countdown_data:
                countdown_list[current_row] = countdown_data
                cfg.countdownList.value = countdown_list
                save_cfg()
                self._updateCountdownList()
                if 0 <= current_row < self.countdownListWidget.count():
                    self.countdownListWidget.setCurrentRow(current_row)
                if hasattr(self.mainWindow, '_MainWindow__updateCountdown'):
                    self.mainWindow._MainWindow__updateCountdown()
                logger.info(f"倒计时设置：编辑倒计时={countdown_data}")
    
    def _onCountdownDeleteClicked(self):
        current_row = self.countdownListWidget.currentRow()
        if current_row < 0:
            InfoBar.warning('删除倒计时', '请先选择一个倒计时', parent=self, duration=3000)
            return
        countdown_list = cfg.countdownList.value or []
        if current_row >= len(countdown_list):
            return
        countdown_list.pop(current_row)
        cfg.countdownList.value = countdown_list
        save_cfg()
        self._updateCountdownList()
        if self.countdownListWidget.count() > 0:
            new_row = min(current_row, self.countdownListWidget.count() - 1)
            self.countdownListWidget.setCurrentRow(new_row)
        if hasattr(self.mainWindow, '_MainWindow__updateCountdown'):
            self.mainWindow._MainWindow__updateCountdown()
        logger.info(f"倒计时设置：删除倒计时索引={current_row}")
    
    def _onCountdownTextColorChanged(self, text: str):
        """倒计时文字颜色变化"""
        from PyQt5.QtGui import QColor
        
        if text == '红色':
            cfg.countdownTextColor.value = QColor(255, 0, 0)
        elif text == '白色':
            cfg.countdownTextColor.value = QColor(255, 255, 255)
        elif text == '黑色':
            cfg.countdownTextColor.value = QColor(0, 0, 0)
        else:  # 主要颜色
            cfg.countdownTextColor.value = cfg.themeColor.value
        
        if hasattr(self.mainWindow, 'updateCountdownStyle'):
            self.mainWindow.updateCountdownStyle()
        logger.info(f"倒计时设置：文字颜色={text}")
    
    def _onCountdownConnectorColorChanged(self, text: str):
        """倒计时连接词颜色变化"""
        from PyQt5.QtGui import QColor
        
        if text == '红色':
            cfg.countdownConnectorColor.value = QColor(255, 0, 0)
        elif text == '白色':
            cfg.countdownConnectorColor.value = QColor(255, 255, 255)
        elif text == '黑色':
            cfg.countdownConnectorColor.value = QColor(0, 0, 0)
        else:  # 主要颜色
            cfg.countdownConnectorColor.value = cfg.themeColor.value
        
        if hasattr(self.mainWindow, 'updateCountdownStyle'):
            self.mainWindow.updateCountdownStyle()
        logger.info(f"倒计时设置：连接词颜色={text}")
    
    def _onShowSchoolInfoChanged(self, checked: bool):
        cfg.showSchoolInfo.value = checked
        self._updateSchoolInfoSettingsEnabled(checked)
        if hasattr(self.mainWindow, 'updateSchoolInfo'):
            self.mainWindow.updateSchoolInfo()
        logger.info(f"学校信息：启用学校信息={'开启' if checked else '关闭'}")
    
    def _onSchoolClassChanged(self, text: str):
        cfg.schoolClass.value = text
        if hasattr(self.mainWindow, 'updateSchoolInfo'):
            self.mainWindow.updateSchoolInfo()
        logger.info(f"学校信息：班级={text}")
    
    def _onSchoolChanged(self, text: str):
        cfg.school.value = text
        if hasattr(self.mainWindow, 'updateSchoolInfo'):
            self.mainWindow.updateSchoolInfo()
        logger.info(f"学校信息：学校={text}")
    
    def _onSchoolInfoPositionChanged(self, text: str):
        cfg.schoolInfoPosition.value = text
        if hasattr(self.mainWindow, '_MainWindow__updateSchoolInfoPosition'):
            self.mainWindow._MainWindow__updateSchoolInfoPosition()
        logger.info(f"学校信息：位置={text}")
    
    def _onSchoolInfoTextColorChanged(self, text: str):
        from PyQt5.QtGui import QColor
        
        if text == '白色':
            cfg.schoolInfoTextColor.value = QColor(255, 255, 255)
        elif text == '黑色':
            cfg.schoolInfoTextColor.value = QColor(0, 0, 0)
        elif text == '红色':
            cfg.schoolInfoTextColor.value = QColor(255, 0, 0)
        else:
            cfg.schoolInfoTextColor.value = cfg.themeColor.value
        
        if hasattr(self.mainWindow, 'updateSchoolInfoStyle'):
            self.mainWindow.updateSchoolInfoStyle()
        logger.info(f"学校信息：文字颜色={text}")
    
    def _onSchoolInfoTextSizeChanged(self, value: int):
        cfg.schoolInfoTextSize.value = value
        if hasattr(self.mainWindow, 'updateSchoolInfoStyle'):
            self.mainWindow.updateSchoolInfoStyle()
        logger.info(f"学校信息：文字大小={value}px")
    
    def _onShowQuickLaunchChanged(self, checked: bool):
        ql_cfg.show_quick_launch = checked
        ql_cfg.save()
        if hasattr(self.mainWindow, '_MainWindow__updateQuickLaunch'):
            self.mainWindow._MainWindow__updateQuickLaunch()
        logger.info(f"快捷启动栏：启用={'开启' if checked else '关闭'}")
    
    def _onQuickLaunchEditClicked(self):
        dialog = QuickLaunchEditDialog(self.mainWindow)
        dialog.exec_()
        if hasattr(self.mainWindow, '_MainWindow__updateQuickLaunch'):
            self.mainWindow._MainWindow__updateQuickLaunch()
    
    def _createSchoolInfoSettings(self, layout):
        """创建学校信息设置"""
        titleLabel = StrongBodyLabel('学校信息', self)
        layout.addWidget(titleLabel)
        
        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel('启用学校信息', self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.schoolInfoSwitch = SwitchButton(self)
        self.schoolInfoSwitch.setChecked(cfg.showSchoolInfo.value)
        self.schoolInfoSwitch.checkedChanged.connect(self._onShowSchoolInfoChanged)
        enableLayout.addWidget(self.schoolInfoSwitch)
        layout.addLayout(enableLayout)
        
        schoolClassLayout = QHBoxLayout()
        schoolClassLabel = BodyLabel('班级', self)
        schoolClassLabel.setFixedWidth(100)
        schoolClassLayout.addWidget(schoolClassLabel)
        self.schoolClassEdit = LineEdit(self)
        self.schoolClassEdit.setText(cfg.schoolClass.value)
        self.schoolClassEdit.setPlaceholderText('例如：高三 (1) 班')
        self.schoolClassEdit.setFixedWidth(120)
        self.schoolClassEdit.textChanged.connect(self._onSchoolClassChanged)
        schoolClassLayout.addWidget(self.schoolClassEdit)
        layout.addLayout(schoolClassLayout)
        
        schoolLayout = QHBoxLayout()
        schoolLabel = BodyLabel('学校', self)
        schoolLabel.setFixedWidth(100)
        schoolLayout.addWidget(schoolLabel)
        self.schoolEdit = LineEdit(self)
        self.schoolEdit.setText(cfg.school.value)
        self.schoolEdit.setPlaceholderText('例如：XX 中学')
        self.schoolEdit.setFixedWidth(120)
        self.schoolEdit.textChanged.connect(self._onSchoolChanged)
        schoolLayout.addWidget(self.schoolEdit)
        layout.addLayout(schoolLayout)
        
        positionLayout = QHBoxLayout()
        positionLabel = BodyLabel('位置', self)
        positionLabel.setFixedWidth(100)
        positionLayout.addWidget(positionLabel)
        self.schoolInfoPositionCombo = ComboBox(self)
        self.schoolInfoPositionCombo.addItems(['左上', '右上', '左下', '右下', '左上预留', '右上预留', '左下预留', '右下预留'])
        self.schoolInfoPositionCombo.setCurrentText(cfg.schoolInfoPosition.value)
        self.schoolInfoPositionCombo.setFixedWidth(120)
        self.schoolInfoPositionCombo.currentTextChanged.connect(self._onSchoolInfoPositionChanged)
        positionLayout.addWidget(self.schoolInfoPositionCombo)
        layout.addLayout(positionLayout)
        
        textColorLayout = QHBoxLayout()
        textColorLabel = BodyLabel('文字颜色', self)
        textColorLabel.setFixedWidth(100)
        textColorLayout.addWidget(textColorLabel)
        self.schoolInfoTextColorCombo = ComboBox(self)
        self.schoolInfoTextColorCombo.addItems(['白色', '黑色', '红色', '主要颜色'])
        self.schoolInfoTextColorCombo.setCurrentText(self._getColorText(cfg.schoolInfoTextColor.value, 'white'))
        self.schoolInfoTextColorCombo.setFixedWidth(120)
        self.schoolInfoTextColorCombo.currentTextChanged.connect(self._onSchoolInfoTextColorChanged)
        textColorLayout.addWidget(self.schoolInfoTextColorCombo)
        layout.addLayout(textColorLayout)
        
        textSizeLayout = QHBoxLayout()
        textSizeLabel = BodyLabel('文字大小', self)
        textSizeLabel.setFixedWidth(100)
        textSizeLayout.addWidget(textSizeLabel)
        self.schoolInfoTextSizeSpin = SpinBox(self)
        self.schoolInfoTextSizeSpin.setRange(12, 60)
        self.schoolInfoTextSizeSpin.setValue(cfg.schoolInfoTextSize.value)
        self.schoolInfoTextSizeSpin.setFixedWidth(120)
        self.schoolInfoTextSizeSpin.valueChanged.connect(self._onSchoolInfoTextSizeChanged)
        textSizeLayout.addWidget(self.schoolInfoTextSizeSpin)
        layout.addLayout(textSizeLayout)
    
    def _createQuickLaunchSettings(self, layout):
        """创建快捷启动栏设置"""
        titleLabel = StrongBodyLabel('快捷启动栏', self)
        layout.addWidget(titleLabel)
        
        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel('启用快捷启动栏', self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.showQuickLaunchSwitch = SwitchButton(self)
        self.showQuickLaunchSwitch.setChecked(ql_cfg.show_quick_launch)
        self.showQuickLaunchSwitch.checkedChanged.connect(self._onShowQuickLaunchChanged)
        enableLayout.addWidget(self.showQuickLaunchSwitch)
        layout.addLayout(enableLayout)

        iconSizeLayout = QHBoxLayout()
        iconSizeLabel = BodyLabel('图标大小', self)
        iconSizeLabel.setFixedWidth(100)
        iconSizeLayout.addWidget(iconSizeLabel)
        self.quickLaunchIconSizeSpin = SpinBox(self)
        self.quickLaunchIconSizeSpin.setRange(32, 96)
        self.quickLaunchIconSizeSpin.setValue(ql_cfg.icon_size)
        self.quickLaunchIconSizeSpin.setFixedWidth(120)
        self.quickLaunchIconSizeSpin.valueChanged.connect(ql_cfg.set_icon_size)
        iconSizeLayout.addWidget(self.quickLaunchIconSizeSpin)
        layout.addLayout(iconSizeLayout)

        iconSpacingLayout = QHBoxLayout()
        iconSpacingLabel = BodyLabel('图标间距', self)
        iconSpacingLabel.setFixedWidth(100)
        iconSpacingLayout.addWidget(iconSpacingLabel)
        self.quickLaunchIconSpacingSpin = SpinBox(self)
        self.quickLaunchIconSpacingSpin.setRange(4, 40)
        self.quickLaunchIconSpacingSpin.setValue(ql_cfg.icon_spacing)
        self.quickLaunchIconSpacingSpin.setFixedWidth(120)
        self.quickLaunchIconSpacingSpin.valueChanged.connect(ql_cfg.set_icon_spacing)
        iconSpacingLayout.addWidget(self.quickLaunchIconSpacingSpin)
        layout.addLayout(iconSpacingLayout)

        displayRowsLayout = QHBoxLayout()
        displayRowsLabel = BodyLabel('显示行数', self)
        displayRowsLabel.setFixedWidth(100)
        displayRowsLayout.addWidget(displayRowsLabel)
        self.quickLaunchDisplayRowsSpin = SpinBox(self)
        self.quickLaunchDisplayRowsSpin.setRange(1, 2)
        self.quickLaunchDisplayRowsSpin.setValue(ql_cfg.display_rows)
        self.quickLaunchDisplayRowsSpin.setFixedWidth(120)
        self.quickLaunchDisplayRowsSpin.valueChanged.connect(ql_cfg.set_display_rows)
        displayRowsLayout.addWidget(self.quickLaunchDisplayRowsSpin)
        layout.addLayout(displayRowsLayout)

        showLabelsLayout = QHBoxLayout()
        showLabelsLabel = BodyLabel('显示名称', self)
        showLabelsLabel.setFixedWidth(100)
        showLabelsLayout.addWidget(showLabelsLabel)
        self.quickLaunchShowLabelsSwitch = SwitchButton(self)
        self.quickLaunchShowLabelsSwitch.setChecked(ql_cfg.show_labels)
        self.quickLaunchShowLabelsSwitch.setOffText('关')
        self.quickLaunchShowLabelsSwitch.setOnText('开')
        self.quickLaunchShowLabelsSwitch.checkedChanged.connect(ql_cfg.set_show_labels)
        showLabelsLayout.addWidget(self.quickLaunchShowLabelsSwitch)
        layout.addLayout(showLabelsLayout)
        
        appsLayout = QHBoxLayout()
        appsLabel = BodyLabel('应用管理', self)
        appsLabel.setFixedWidth(100)
        appsLayout.addWidget(appsLabel)
        self.quickLaunchEditButton = PushButton('编辑应用', self)
        self.quickLaunchEditButton.setFixedWidth(120)
        self.quickLaunchEditButton.clicked.connect(self._onQuickLaunchEditClicked)
        appsLayout.addWidget(self.quickLaunchEditButton)
        layout.addLayout(appsLayout)


class CountdownEditDialog(MessageBoxBase):
    """倒计时编辑对话框"""
    
    def __init__(self, parent=None, countdown_data=None):
        super().__init__(parent)
        self._countdown_data = countdown_data
        self._result = None
        self._init_ui()
    
    def _init_ui(self):
        from PyQt5.QtCore import QDate, QTime

        self.viewLayout.setSpacing(8)

        title = SubtitleLabel('编辑倒计时' if self._countdown_data else '添加倒计时')
        self.viewLayout.addWidget(title)
        infoLabel = BodyLabel('设置倒计时的目标名称和日期')
        self.viewLayout.addWidget(infoLabel)

        titleLabel = BodyLabel('目标名称')
        self.viewLayout.addWidget(titleLabel)
        self.titleEdit = LineEdit()
        self.titleEdit.setPlaceholderText('例如：中考')
        if self._countdown_data:
            self.titleEdit.setText(self._countdown_data.get('title', ''))
        self.viewLayout.addWidget(self.titleEdit)
        
        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)
        
        dateLabel = BodyLabel('目标日期')
        self.viewLayout.addWidget(dateLabel)
        self.datePicker = CalendarPicker()
        if self._countdown_data:
            target_time = self._countdown_data.get('target_time', '')
            if target_time:
                try:
                    dt = datetime.datetime.strptime(target_time, '%Y-%m-%d %H:%M')
                    self.datePicker.setDate(QDate(dt.year, dt.month, dt.day))
                except:
                    pass
        else:
            now = datetime.datetime.now()
            self.datePicker.setDate(QDate(now.year, now.month, now.day))
        self.viewLayout.addWidget(self.datePicker)
        
        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)
        
        timeLabel = BodyLabel('目标时间')
        self.viewLayout.addWidget(timeLabel)
        self.timePicker = TimePicker()
        if self._countdown_data:
            target_time = self._countdown_data.get('target_time', '')
            if target_time:
                try:
                    dt = datetime.datetime.strptime(target_time, '%Y-%m-%d %H:%M')
                    self.timePicker.setTime(QTime(dt.hour, dt.minute))
                except:
                    pass
        else:
            self.timePicker.setTime(QTime(0, 0))
        self.viewLayout.addWidget(self.timePicker)
        
        self.yesButton.setText('确定')
        self.cancelButton.setText('取消')
        
        self.widget.setMinimumWidth(360)
        
        self.yesButton.clicked.disconnect()
        self.yesButton.clicked.connect(self._on_ok)
    
    def _on_ok(self):
        try:
            title_text = self.titleEdit.text().strip()
            if not title_text:
                InfoBar.error('错误', '请输入目标名称', parent=self, duration=3000)
                return
            
            qdate = self.datePicker.date
            qtime = self.timePicker.time
            if not qdate.isValid() or not qtime.isValid():
                InfoBar.error('错误', '请输入有效的日期和时间', parent=self, duration=3000)
                return
            dt = datetime.datetime(qdate.year(), qdate.month(), qdate.day(), qtime.hour(), qtime.minute())
            self._result = {
                'title': title_text,
                'target_time': dt.strftime('%Y-%m-%d %H:%M')
            }
            self.accept()
        except Exception as e:
            logger.error(f'保存倒计时失败：{e}')
            InfoBar.error('错误', f'请输入有效的日期和时间：{e}', parent=self, duration=5000)
    
    def get_countdown(self):
        return self._result


class QuickLaunchEditDialog(MessageBoxBase):
    """快捷启动栏编辑对话框"""
    
    def __init__(self, parent=None):
        super().__init__(parent)
        if ql_cfg.quick_launch_apps is None:
            self._apps = [
                {"name": "1", "path": "", "icon": "1.ico"},
                {"name": "2", "path": "", "icon": "2.ico"},
                {"name": "3", "path": "", "icon": "3.ico"},
                {"name": "4", "path": "", "icon": "4.ico"},
                {"name": "5", "path": "", "icon": "5.ico"}
            ]
        else:
            self._apps = list(ql_cfg.quick_launch_apps)
        self._init_ui()
    
    def _init_ui(self):
        self.viewLayout.setSpacing(8)
        title = SubtitleLabel('编辑快捷启动栏')
        self.viewLayout.addWidget(title)
        infoLabel = BodyLabel('管理快捷启动栏中的应用')
        self.viewLayout.addWidget(infoLabel)

        self.appListWidget = ListWidget(self)
        self.appListWidget.setFixedHeight(200)
        self.appListWidget.setSelectionMode(ListWidget.SingleSelection)
        self.appListWidget.itemClicked.connect(self._on_item_clicked)
        self._update_app_list()
        self.viewLayout.addWidget(self.appListWidget)
        
        buttonLayout = QHBoxLayout()
        self.addButton = PushButton('添加应用', self)
        self.addButton.clicked.connect(self._on_add_app)
        buttonLayout.addWidget(self.addButton)
        
        self.editButton = PushButton('编辑', self)
        self.editButton.clicked.connect(self._on_edit_app)
        buttonLayout.addWidget(self.editButton)
        
        self.deleteButton = PushButton('删除', self)
        self.deleteButton.clicked.connect(self._on_delete_app)
        buttonLayout.addWidget(self.deleteButton)
        
        self.viewLayout.addLayout(buttonLayout)
        
        self.yesButton.setText('完成')
        self.cancelButton.setText('取消')
        self.widget.setMinimumWidth(400)
        
        self._selected_row = -1
    
    def _on_item_clicked(self, item):
        self._selected_row = self.appListWidget.row(item)
    
    def _update_app_list(self):
        self.appListWidget.clear()
        for app in self._apps:
            name = app.get('name', '未知')
            path = app.get('path', '')
            display_text = f"{name} - {path if path else '未配置路径'}"
            self.appListWidget.addItem(display_text)
    
    def _on_add_app(self):
        dialog = AppEditDialog(self.parent())
        if dialog.exec_():
            app_data = dialog.get_app_data()
            if app_data:
                self._apps.append(app_data)
                self._update_app_list()
    
    def _on_edit_app(self):
        if self._selected_row < 0 or self._selected_row >= len(self._apps):
            InfoBar.warning('提示', '请先选择一个应用', parent=self, duration=2000)
            return
        
        dialog = AppEditDialog(self.parent(), self._apps[self._selected_row])
        if dialog.exec_():
            app_data = dialog.get_app_data()
            if app_data:
                self._apps[self._selected_row] = app_data
                self._update_app_list()
                if 0 <= self._selected_row < self.appListWidget.count():
                    self.appListWidget.setCurrentRow(self._selected_row)
    
    def _on_delete_app(self):
        if self._selected_row < 0 or self._selected_row >= len(self._apps):
            InfoBar.warning('提示', '请先选择一个应用', parent=self, duration=2000)
            return
        
        deleted_app = self._apps.pop(self._selected_row)
        self._delete_app_icon(deleted_app)
        self._update_app_list()
        if self.appListWidget.count() > 0:
            new_row = min(self._selected_row, self.appListWidget.count() - 1)
            self.appListWidget.setCurrentRow(new_row)
            self._selected_row = new_row
    
    def _delete_app_icon(self, app_data):
        if not app_data:return
        icon_filename = app_data.get('icon', '')
        if not icon_filename or icon_filename in ('exe.ico', 'default.ico'):return
        from data.software_list import get_software_icon_path
        icon_path = get_software_icon_path(icon_filename)
        if icon_path and os.path.exists(icon_path):
            try:
                os.remove(icon_path)
                logger.info(f"已删除图标文件：{icon_path}")
            except Exception as e:
                logger.warning(f"删除图标文件失败：{e}")
    
    def accept(self):
        ql_cfg.set_apps(self._apps)
        super().accept()
    
    def get_apps(self):
        return self._apps


class AppEditDialog(MessageBoxBase):
    """应用编辑对话框"""
    
    def __init__(self, parent=None, app_data=None):
        super().__init__(parent)
        self._app_data = app_data
        self._result = None
        self._init_ui()
    
    def _init_ui(self):
        self.viewLayout.setSpacing(8)

        title = SubtitleLabel('编辑应用' if self._app_data else '添加应用')
        self.viewLayout.addWidget(title)

        descLabel = BodyLabel('配置快捷启动栏中的应用')
        self.viewLayout.addWidget(descLabel)

        nameLabel = BodyLabel('应用名称')
        self.viewLayout.addWidget(nameLabel)
        self.nameEdit = LineEdit(self)
        self.nameEdit.setPlaceholderText('例如：微信')
        if self._app_data:self.nameEdit.setText(self._app_data.get('name', ''))
        self.viewLayout.addWidget(self.nameEdit)
        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)
        
        pathLabel = BodyLabel('应用路径')
        self.viewLayout.addWidget(pathLabel)
        pathLayout = QHBoxLayout()
        self.pathEdit = LineEdit(self)
        self.pathEdit.setPlaceholderText('例如：C:\\Program Files\\WeChat\\WeChat.exe')
        if self._app_data:self.pathEdit.setText(self._app_data.get('path', ''))
        self.pathEdit.textChanged.connect(self._on_path_changed)
        pathLayout.addWidget(self.pathEdit)
        self.browseButton = PushButton('浏览', self)
        self.browseButton.setFixedWidth(60)
        self.browseButton.clicked.connect(self._on_browse)
        pathLayout.addWidget(self.browseButton)
        self.viewLayout.addLayout(pathLayout)
        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)
        
        iconPathLabel = BodyLabel('图标路径')
        self.viewLayout.addWidget(iconPathLabel)
        iconInputLayout = QHBoxLayout()
        self.iconPreviewLabel = QLabel(self)
        self.iconPreviewLabel.setObjectName("iconPreviewLabel")
        self.iconPreviewLabel.setFixedSize(48, 48)
        self.iconPreviewLabel.setAlignment(Qt.AlignCenter)
        self._set_default_icon()
        iconInputLayout.addWidget(self.iconPreviewLabel)
        self.iconPathEdit = LineEdit(self)
        self.iconPathEdit.setPlaceholderText('自定义图标路径')
        if self._app_data:self.iconPathEdit.setText(self._app_data.get('icon', ''))
        self.iconPathEdit.textChanged.connect(self._on_icon_path_changed)
        iconInputLayout.addWidget(self.iconPathEdit)
        self.iconBrowseButton = PushButton('浏览', self)
        self.iconBrowseButton.setFixedWidth(60)
        self.iconBrowseButton.clicked.connect(self._on_icon_browse)
        iconInputLayout.addWidget(self.iconBrowseButton)
        self.viewLayout.addLayout(iconInputLayout)
        
        self.yesButton.setText('确定')
        self.cancelButton.setText('取消')
        self.widget.setMinimumWidth(400)
        
        self.yesButton.clicked.disconnect()
        self.yesButton.clicked.connect(self._on_ok)
        
        self._icon_filename = self._app_data.get('icon', '') if self._app_data else ''
        if self._icon_filename:
            self._load_icon_preview(self._icon_filename)
    
    def _set_default_icon(self):
        default_icon = QIcon.fromTheme('application-x-executable')
        if default_icon.isNull():
            pixmap = QPixmap(48, 48)
            pixmap.fill(QColor(100, 100, 100))
            self.iconPreviewLabel.setPixmap(pixmap)
        else:
            self.iconPreviewLabel.setPixmap(default_icon.pixmap(48, 48))
    
    def _load_icon_preview(self, icon_filename):
        from data.software_list import get_software_icon_path
        icon_path = get_software_icon_path(icon_filename)
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path)
            if not pixmap.isNull():
                scaled = pixmap.scaled(36, 36, Qt.KeepAspectRatio, Qt.SmoothTransformation)
                self.iconPreviewLabel.setPixmap(scaled)
            else:
                self._set_default_icon()
        else:
            self._set_default_icon()
    
    def _extract_icon(self, exe_path):
        try:
            from PyQt5.QtWidgets import QFileIconProvider
            from PyQt5.QtGui import QIcon
            from PyQt5.QtCore import QFileInfo

            provider = QFileIconProvider()
            fi = QFileInfo(exe_path)
            icon = provider.icon(fi)
            
            sizes = icon.availableSizes()
            if not sizes:
                return 'exe.ico'
            
            best_size = max(sizes, key=lambda s: s.width() * s.height())
            pixmap = icon.pixmap(best_size)
            
            if pixmap.isNull():
                return 'exe.ico'
            
            target_size = 256
            if pixmap.width() < target_size:
                pixmap = pixmap.scaled(target_size, target_size, Qt.KeepAspectRatio, Qt.SmoothTransformation)
            
            icon_filename = self._get_icon_name()
            icon_dir = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'data', 'software_icon')
            os.makedirs(icon_dir, exist_ok=True)
            icon_save_path = os.path.join(icon_dir, icon_filename)
            pixmap.save(icon_save_path, 'PNG')
            
            return icon_filename
        except Exception as e:
            logger.error(f"提取图标失败：{e}")
            return 'exe.ico'
    
    def _get_icon_name(self):
        name_text = self.nameEdit.text().strip()
        if name_text:
            cleaned_name = re.sub(r'[^\w\u4e00-\u9fff]', '', name_text)
            if cleaned_name:
                return cleaned_name + '.ico'
        return 'default.ico'
    
    def _on_path_changed(self, path):
        if path.lower().endswith('.exe') and os.path.exists(path):
            base_name = os.path.splitext(os.path.basename(path))[0]
            self.nameEdit.setText(base_name)
            self._do_extract_icon(path)
    
    def _do_extract_icon(self, exe_path):
        icon_path = self._extract_icon(exe_path)
        if icon_path:
            self._icon_filename = icon_path
            self.iconPathEdit.setText('')
            self._load_icon_preview(icon_path)
    
    def _on_extract_icon(self):
        path_text = self.pathEdit.text().strip()
        if not path_text:
            InfoBar.warning('提示', '请先选择应用程序路径', parent=self, duration=2000)
            return
        
        if not os.path.exists(path_text):
            InfoBar.error('错误', '文件路径不存在', parent=self, duration=2000)
            return
        
        self._do_extract_icon(path_text)
    
    def _on_icon_path_changed(self, path):
        if path:
            self._icon_filename = path
            self._load_icon_preview(path)
    
    def _on_icon_browse(self):
        from PyQt5.QtWidgets import QFileDialog
        
        file_path, _ = QFileDialog.getOpenFileName(
            self,
            '选择图标',
            '',
            'Image Files (*.ico *.png *.jpg *.jpeg *.bmp);;All Files (*)'
        )
        
        if file_path:
            self.iconPathEdit.setText(file_path)
    
    def _on_browse(self):
        from PyQt5.QtWidgets import QFileDialog
        
        file_path, _ = QFileDialog.getOpenFileName(
            self,
            '选择应用程序',
            '',
            'Executable Files (*.exe);;All Files (*)'
        )
        
        if file_path:
            self.pathEdit.setText(file_path)
    
    def _on_ok(self):
        name_text = self.nameEdit.text().strip()
        if not name_text:
            InfoBar.error('错误', '请输入应用名称', parent=self, duration=2000)
            return
        
        path_text = self.pathEdit.text().strip()
        icon_text = self.iconPathEdit.text().strip()
        
        if icon_text:
            icon_val = icon_text
        elif self._icon_filename:
            icon_val = self._icon_filename
        else:
            icon_val = self._get_icon_name()
        
        self._result = {
            'name': name_text,
            'path': path_text,
            'icon': icon_val
        }
        self.accept()
    
    def get_app_data(self):
        return self._result
