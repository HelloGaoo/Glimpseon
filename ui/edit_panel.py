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
import logging
from PyQt5.QtCore import QPropertyAnimation, QRect, Qt
from PyQt5.QtGui import QColor, QPalette
from PyQt5.QtWidgets import (
    QHBoxLayout, QLabel, QLineEdit, QListWidget, 
    QPushButton, QVBoxLayout, QWidget
)
from qfluentwidgets import (
    BodyLabel, FluentIcon as FIF, isDarkTheme, LineEdit, ListWidget, 
    PrimaryPushButton, PushButton, StrongBodyLabel, ToolButton,
    SwitchButton, ComboBox, SpinBox, SmoothScrollArea
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
        self._addSeparator(v)
        self._createPoetrySettings(v)
        self._addSeparator(v)
        self._createWeatherSettings(v)
        self._connectConfigSignals()
    
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
        
        self.hide()
        self.setVisible(False)
    
    def _addSeparator(self, layout):
        """添加分隔线"""
        separator = QWidget()
        separator.setFixedHeight(1)
        separator.setObjectName('separator')
        layout.addWidget(separator)
    
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
        """更新一言 API 地址编辑框"""
        self.poetryApiEdit.setText(value)
    
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
        pass
    
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
        self.poetryApiEdit = LineEdit(self)
        self.poetryApiEdit.setText(cfg.poetryApiUrl.value)
        self.poetryApiEdit.setFixedWidth(150)
        self.poetryApiEdit.textChanged.connect(self._onPoetryApiChanged)
        apiLayout.addWidget(self.poetryApiEdit)
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
        self.cityButton = PushButton('选择城市', self)
        self.cityButton.setFixedWidth(100)
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
        
        self._updateTheme()
        
        self.show()

        pr = parent.rect()
        if self.isLeftSide:
            end_rect = QRect(0, 0, self._width, pr.height())
            start_rect = QRect(-self._width, 0, self._width, pr.height())
        else:
            end_rect = QRect(pr.width() - self._width, 0, self._width, pr.height())
            start_rect = QRect(pr.width(), 0, self._width, pr.height())
        
        self.setGeometry(start_rect)
        
        try:
            self.anim.finished.disconnect(self._onShowFinished)
        except Exception:
            pass
        self.anim.stop()
        self.anim.setDuration(220)
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
        self.anim.setDuration(180)
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
        if hasattr(self.mainWindow, 'homeContent'):
            # 更新所有一言组件的可见性
            for widget in self.mainWindow.homeContent.findChildren(QWidget):
                if widget.objectName() == 'poetryWidget':
                    widget.setVisible(checked)
        logger.info(f"一言设置：启用一言={'开启' if checked else '关闭'}")
    
    def _onPoetryApiChanged(self, text: str):
        """一言 API 地址变化"""
        cfg.poetryApiUrl.value = text
        logger.info(f"一言设置：API 地址={text}")
    
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
