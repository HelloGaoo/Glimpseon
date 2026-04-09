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

from PyQt5.QtCore import QEasingCurve, QPropertyAnimation, QRect, Qt
from PyQt5.QtGui import QColor, QPalette
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
    ComboBox,
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
    ToolButton,
)

from core.config import cfg

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
        self.isLeftSide = False  # 是否在左侧显示
        
        # 设置不透明背景 我靠这个比东西弄了一万天才解决。。
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
        self.countdownDisplayModeCombo.setEnabled(enabled)
        self.countdownColorCombo.setEnabled(enabled)
        self.countdownSizeSpin.setEnabled(enabled)
        self.countdownPositionCombo.setEnabled(enabled)
        self.countdownTitleColorCombo.setEnabled(enabled)
        self.countdownTitleBoldSwitch.setEnabled(enabled)
        self.countdownTitleSizeSpin.setEnabled(enabled)
        self.countdownCarouselIntervalSpin.setEnabled(enabled)
        self.countdownAddButton.setEnabled(enabled)
        self.countdownListWidget.setEnabled(enabled)
        self.countdownEditButton.setEnabled(enabled)
        self.countdownDeleteButton.setEnabled(enabled)
    
    def _connectConfigSignals(self):
        """连接配置变化信号到 UI 更新"""
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
        cfg.countdownColor.valueChanged.connect(self._updateCountdownColorCombo)
        cfg.countdownSize.valueChanged.connect(self._updateCountdownSizeSpin)
        cfg.countdownPosition.valueChanged.connect(self._updateCountdownPositionCombo)
        cfg.countdownTitleColor.valueChanged.connect(self._updateCountdownTitleColorCombo)
        cfg.countdownTitleBold.valueChanged.connect(self._updateCountdownTitleBoldSwitch)
        cfg.countdownTitleSize.valueChanged.connect(self._updateCountdownTitleSizeSpin)
        cfg.countdownCarouselInterval.valueChanged.connect(self._updateCountdownCarouselIntervalSpin)
        cfg.countdownList.valueChanged.connect(self._updateCountdownList)
    
    def __connectSignalToSlot(self):
        cfg.themeChanged.connect(self._onThemeChanged)
    
    def _onThemeChanged(self, theme):
        self._updateTheme()
    
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
        self.clockColorCombo.setCurrentText(self._getColorText(value))
    
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
        if value == 'https://api.imlcd.cn/yy/api.php':
            self.poetryApiCombo.setCurrentText('一言 API')
        elif value == 'https://www.ffapi.cn/int/v1/shici':
            self.poetryApiCombo.setCurrentText('诗词 API')
        else:
            self.poetryApiCombo.setCurrentText('一言 API')
    
    def _updatePoetrySizeSpin(self, value):
        """更新一言大小旋转框"""
        self.poetrySizeSpin.setValue(value)
    
    def _updatePoetryUpdateIntervalCombo(self, value):
        """更新一言更新间隔下拉框"""
        self.poetryUpdateIntervalCombo.setCurrentText(value)
    
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
        self.weatherUpdateIntervalCombo.setCurrentText(value)
    
    def _updateCityButton(self, value):
        """更新城市按钮"""
        self.cityButton.setText(value)
    
    def _updateClockPositionCombo(self, value):
        """更新时间位置下拉框"""
        self.clockPositionCombo.setCurrentText(value)
    
    def _updatePoetryPositionCombo(self, value):
        """更新一言位置下拉框"""
        self.poetryPositionCombo.setCurrentText(value)
    
    def _updateWeatherPositionCombo(self, value):
        """更新天气位置下拉框"""
        self.weatherPositionCombo.setCurrentText(value)
    
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
            qss_path = Path(__file__).parent.parent / 'resource' / 'qss' / 'dark' / 'main_interface.qss'
        else:
            qss_path = Path(__file__).parent.parent / 'resource' / 'qss' / 'light' / 'main_interface.qss'
        
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
    
    def _getColorText(self, color):
        """获取颜色文本表示"""
        if hasattr(color, 'name'):
            color_hex = color.name().upper()
            if color_hex == '#FFFFFF':
                return '白色'
            elif color_hex == '#000000':
                return '黑色'
        return '主要颜色'
    
    def _onClockColorChanged(self, text: str):
        """时钟颜色变化"""
        from PyQt5.QtGui import QColor
        
        if text == '白色':
            cfg.clockColor.value = QColor(255, 255, 255)
        elif text == '黑色':
            cfg.clockColor.value = QColor(0, 0, 0)
        else:  # 主要颜色
            cfg.clockColor.value = cfg.themeColor.value
        
        if hasattr(self.mainWindow, 'updateClockStyle'):
            self.mainWindow.updateClockStyle()
        logger.info(f"时间设置：时钟颜色={text}")
    
    def _onClockSizeChanged(self, value: int):
        """时钟大小变化"""
        cfg.clockSize.value = value
        if hasattr(self.mainWindow, 'updateClockStyle'):
            self.mainWindow.updateClockStyle()
        logger.info(f"时间设置：时钟大小={value}px")
    
    def _onDateSizeChanged(self, value: int):
        """日期大小变化"""
        cfg.dateSize.value = value
        if hasattr(self.mainWindow, 'updateClockStyle'):
            self.mainWindow.updateClockStyle()
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
        
        colorLayout = QHBoxLayout()
        colorLabel = BodyLabel('倒计时颜色', self)
        colorLabel.setFixedWidth(100)
        colorLayout.addWidget(colorLabel)
        self.countdownColorCombo = ComboBox(self)
        self.countdownColorCombo.addItems(['主要颜色', '白色', '黑色'])
        self.countdownColorCombo.setCurrentText(self._getColorText(cfg.countdownColor.value))
        self.countdownColorCombo.setFixedWidth(120)
        self.countdownColorCombo.currentTextChanged.connect(self._onCountdownColorChanged)
        colorLayout.addWidget(self.countdownColorCombo)
        layout.addLayout(colorLayout)
        
        sizeLayout = QHBoxLayout()
        sizeLabel = BodyLabel('倒计时大小', self)
        sizeLabel.setFixedWidth(100)
        sizeLayout.addWidget(sizeLabel)
        self.countdownSizeSpin = SpinBox(self)
        self.countdownSizeSpin.setRange(20, 120)
        self.countdownSizeSpin.setValue(cfg.countdownSize.value)
        self.countdownSizeSpin.setFixedWidth(120)
        self.countdownSizeSpin.valueChanged.connect(self._onCountdownSizeChanged)
        sizeLayout.addWidget(self.countdownSizeSpin)
        layout.addLayout(sizeLayout)
        
        titleColorLayout = QHBoxLayout()
        titleColorLabel = BodyLabel('标题颜色', self)
        titleColorLabel.setFixedWidth(100)
        titleColorLayout.addWidget(titleColorLabel)
        self.countdownTitleColorCombo = ComboBox(self)
        self.countdownTitleColorCombo.addItems(['主要颜色', '白色', '黑色'])
        self.countdownTitleColorCombo.setCurrentText(self._getColorText(cfg.countdownTitleColor.value))
        self.countdownTitleColorCombo.setFixedWidth(120)
        self.countdownTitleColorCombo.currentTextChanged.connect(self._onCountdownTitleColorChanged)
        titleColorLayout.addWidget(self.countdownTitleColorCombo)
        layout.addLayout(titleColorLayout)
        
        titleBoldLayout = QHBoxLayout()
        titleBoldLabel = BodyLabel('标题加粗', self)
        titleBoldLabel.setFixedWidth(100)
        titleBoldLayout.addWidget(titleBoldLabel)
        self.countdownTitleBoldSwitch = SwitchButton(self)
        self.countdownTitleBoldSwitch.setChecked(cfg.countdownTitleBold.value)
        self.countdownTitleBoldSwitch.checkedChanged.connect(self._onCountdownTitleBoldChanged)
        titleBoldLayout.addWidget(self.countdownTitleBoldSwitch)
        layout.addLayout(titleBoldLayout)
        
        titleSizeLayout = QHBoxLayout()
        titleSizeLabel = BodyLabel('标题大小', self)
        titleSizeLabel.setFixedWidth(100)
        titleSizeLayout.addWidget(titleSizeLabel)
        self.countdownTitleSizeSpin = SpinBox(self)
        self.countdownTitleSizeSpin.setRange(12, 60)
        self.countdownTitleSizeSpin.setValue(cfg.countdownTitleSize.value)
        self.countdownTitleSizeSpin.setFixedWidth(120)
        self.countdownTitleSizeSpin.valueChanged.connect(self._onCountdownTitleSizeChanged)
        titleSizeLayout.addWidget(self.countdownTitleSizeSpin)
        layout.addLayout(titleSizeLayout)
        
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
        self.countdownDisplayModeCombo.setCurrentText('同时显示' if value == 'simultaneous' else '轮播显示')
    
    def _updateCountdownColorCombo(self, value):
        self.countdownColorCombo.setCurrentText(self._getColorText(value))
    
    def _updateCountdownSizeSpin(self, value):
        self.countdownSizeSpin.setValue(value)
    
    def _updateCountdownPositionCombo(self, value):
        self.countdownPositionCombo.setCurrentText(value)
    
    def _updateCountdownTitleColorCombo(self, value):
        self.countdownTitleColorCombo.setCurrentText(self._getColorText(value))
    
    def _updateCountdownTitleBoldSwitch(self, value):
        self.countdownTitleBoldSwitch.setChecked(value)
    
    def _updateCountdownTitleSizeSpin(self, value):
        self.countdownTitleSizeSpin.setValue(value)
    
    def _updateCountdownCarouselIntervalSpin(self, value):
        self.countdownCarouselIntervalSpin.setValue(value)
    
    def _updateCountdownList(self):
        self.countdownListWidget.clear()
        countdown_list = cfg.countdownList.value or []
        for cd in countdown_list:
            title = cd.get('title', '')
            target_time = cd.get('target_time', '')
            if title and target_time:
                self.countdownListWidget.addItem(f"{title} - {target_time}")
    
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
    
    def _onCountdownColorChanged(self, text: str):
        from PyQt5.QtGui import QColor
        if text == '白色':
            cfg.countdownColor.value = QColor(255, 255, 255)
        elif text == '黑色':
            cfg.countdownColor.value = QColor(0, 0, 0)
        else:
            cfg.countdownColor.value = cfg.themeColor.value
        if hasattr(self.mainWindow, 'updateCountdownStyle'):
            self.mainWindow.updateCountdownStyle()
        logger.info(f"倒计时设置：倒计时颜色={text}")
    
    def _onCountdownSizeChanged(self, value: int):
        cfg.countdownSize.value = value
        if hasattr(self.mainWindow, 'updateCountdownStyle'):
            self.mainWindow.updateCountdownStyle()
        logger.info(f"倒计时设置：倒计时大小={value}px")
    
    def _onCountdownTitleColorChanged(self, text: str):
        from PyQt5.QtGui import QColor
        if text == '白色':
            cfg.countdownTitleColor.value = QColor(255, 255, 255)
        elif text == '黑色':
            cfg.countdownTitleColor.value = QColor(0, 0, 0)
        else:
            cfg.countdownTitleColor.value = cfg.themeColor.value
        if hasattr(self.mainWindow, 'updateCountdownStyle'):
            self.mainWindow.updateCountdownStyle()
        logger.info(f"倒计时设置：标题颜色={text}")
    
    def _onCountdownTitleBoldChanged(self, checked: bool):
        cfg.countdownTitleBold.value = checked
        if hasattr(self.mainWindow, 'updateCountdownStyle'):
            self.mainWindow.updateCountdownStyle()
        logger.info(f"倒计时设置：标题加粗={'开启' if checked else '关闭'}")
    
    def _onCountdownTitleSizeChanged(self, value: int):
        cfg.countdownTitleSize.value = value
        if hasattr(self.mainWindow, 'updateCountdownStyle'):
            self.mainWindow.updateCountdownStyle()
        logger.info(f"倒计时设置：标题大小={value}px")
    
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
            countdown_list = cfg.countdownList.value or []
            countdown_list.append(dialog.get_countdown())
            cfg.countdownList.value = countdown_list
            logger.info(f"倒计时设置：添加倒计时={dialog.get_countdown()}")
    
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
            countdown_list[current_row] = dialog.get_countdown()
            cfg.countdownList.value = countdown_list
            logger.info(f"倒计时设置：编辑倒计时={dialog.get_countdown()}")
    
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
        logger.info(f"倒计时设置：删除倒计时索引={current_row}")
    
    def _onCountdownAddClicked(self):
        dialog = CountdownEditDialog(self.mainWindow)
        if dialog.exec():
            countdown_data = dialog.get_countdown()
            if countdown_data:
                countdown_list = cfg.countdownList.value or []
                countdown_list.append(countdown_data)
                cfg.countdownList.value = countdown_list
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
                logger.info(f"倒计时设置：编辑倒计时={countdown_data}")


class CountdownEditDialog(MessageBoxBase):
    """倒计时编辑对话框"""
    
    def __init__(self, parent=None, countdown_data=None):
        super().__init__(parent)
        self._countdown_data = countdown_data
        self._result = None
        self._init_ui()
    
    def _init_ui(self):
        title = SubtitleLabel('编辑倒计时' if self._countdown_data else '添加倒计时')
        self.viewLayout.addWidget(title)
        
        spacer = QWidget()
        spacer.setFixedHeight(10)
        self.viewLayout.addWidget(spacer)
        
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
        dateLayout = QHBoxLayout()
        self.yearEdit = LineEdit()
        self.yearEdit.setPlaceholderText('年')
        self.yearEdit.setFixedWidth(70)
        self.monthEdit = LineEdit()
        self.monthEdit.setPlaceholderText('月')
        self.monthEdit.setFixedWidth(50)
        self.dayEdit = LineEdit()
        self.dayEdit.setPlaceholderText('日')
        self.dayEdit.setFixedWidth(50)
        dateLayout.addWidget(self.yearEdit)
        dateLayout.addWidget(QLabel('-'))
        dateLayout.addWidget(self.monthEdit)
        dateLayout.addWidget(QLabel('-'))
        dateLayout.addWidget(self.dayEdit)
        self.viewLayout.addLayout(dateLayout)
        
        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)
        
        timeLabel = BodyLabel('目标时间')
        self.viewLayout.addWidget(timeLabel)
        timeLayout = QHBoxLayout()
        self.hourEdit = LineEdit()
        self.hourEdit.setPlaceholderText('时')
        self.hourEdit.setFixedWidth(50)
        self.minuteEdit = LineEdit()
        self.minuteEdit.setPlaceholderText('分')
        self.minuteEdit.setFixedWidth(50)
        timeLayout.addWidget(self.hourEdit)
        timeLayout.addWidget(QLabel(':'))
        timeLayout.addWidget(self.minuteEdit)
        self.viewLayout.addLayout(timeLayout)
        
        if self._countdown_data:
            target_time = self._countdown_data.get('target_time', '')
            if target_time:
                try:
                    dt = datetime.datetime.strptime(target_time, '%Y-%m-%d %H:%M')
                    self.yearEdit.setText(str(dt.year))
                    self.monthEdit.setText(str(dt.month))
                    self.dayEdit.setText(str(dt.day))
                    self.hourEdit.setText(str(dt.hour))
                    self.minuteEdit.setText(str(dt.minute))
                except:
                    pass
        
        self.yesButton.setText('确定')
        self.cancelButton.setText('取消')
        
        self.widget.setMinimumWidth(360)
        
        self.yesButton.clicked.disconnect()
        self.yesButton.clicked.connect(self._on_ok)
    
    def _on_ok(self):
        try:
            year = int(self.yearEdit.text())
            month = int(self.monthEdit.text())
            day = int(self.dayEdit.text())
            hour = int(self.hourEdit.text())
            minute = int(self.minuteEdit.text())
            dt = datetime.datetime(year, month, day, hour, minute)
            self._result = {
                'title': self.titleEdit.text(),
                'target_time': dt.strftime('%Y-%m-%d %H:%M')
            }
            self.accept()
        except:
            InfoBar.error('错误', '请输入有效的日期和时间', parent=self, duration=3000)
    
    def get_countdown(self):
        return self._result
