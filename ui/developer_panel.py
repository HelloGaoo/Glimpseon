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
调试面板
"""

import os
import time

import psutil
import requests
from PyQt5.QtCore import QEvent, QTimer, Qt
from PyQt5.QtWidgets import (
    QApplication,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QTextEdit,
    QVBoxLayout,
    QWidget,
)
from qfluentwidgets import (
    BodyLabel,
    CardWidget,
    ComboBox,
    FluentIcon as FIF,
    ImageLabel,
    InfoBar,
    LineEdit,
    PrimaryPushButton,
    PushButton,
    ScrollArea,
    StrongBodyLabel,
    SubtitleLabel,
    ToggleButton,
    ToolTipFilter,
    ToolTipPosition,
)

from core.config import cfg
from core.constants import load_qss
from core.logger import logger
from services.weather import WeatherService
from ui.city_selector import RegionDatabase


from .base_scroll import BaseScrollAreaInterface
class DeveloperPanel(BaseScrollAreaInterface):
    """调试面板"""
    
    def __init__(self, mainWindow):
        super().__init__("调试", parent=None)
        self.mainWindow = mainWindow
        self.setObjectName('developerPanel')
        
        self.frameCount = 0
        self.lastFpsTime = time.time()
        self.currentFps = 0
        self.process = psutil.Process(os.getpid())
        
        self.lastGeometry = None
        self.lastCurrentWidget = None
        
        try:
            cpu_times = self.process.cpu_times()
            self.last_cpu_usage = cpu_times.user + cpu_times.system
            self.last_cpu_time = time.time()
        except Exception:
            self.last_cpu_usage = 0
            self.last_cpu_time = time.time()
        
        self.elementCheckEnabled = False
        self.elementCheckOverlay = None
        self._popOutWindow = None
        
        self._initUI()
        self._setupTimers()
        
    def _initUI(self):
        """初始化界面"""
        scrollLayout = QVBoxLayout(self.scrollWidget)
        scrollLayout.setSpacing(15)
        scrollLayout.setContentsMargins(60, 10, 60, 20)

        debugCard = self._createDebugCard()
        scrollLayout.addWidget(debugCard)
        
        apiCard = self._createAPITestCard()
        scrollLayout.addWidget(apiCard)
        
        weatherDebugCard = self._createWeatherDebugCard()
        scrollLayout.addWidget(weatherDebugCard)
        
        resourceCard = self._createResourceMonitorCard()
        scrollLayout.addWidget(resourceCard)
        
        windowCard = self._createWindowDebugCard()
        scrollLayout.addWidget(windowCard)
        
        elementCard = self._createElementCheckCard()
        scrollLayout.addWidget(elementCard)
        
        self._loadStyleSheet()
        QTimer.singleShot(500, self._refreshComponentTree)
        
        QTimer.singleShot(1000, self._installEventFilter)
    def _installEventFilter(self):
        if self.mainWindow:self.mainWindow.installEventFilter(self)
        
    def _createDebugCard(self):
        """创建调试信息显示卡片"""
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(16)
        layout.setContentsMargins(16, 16, 16, 16)
        titleLayout = QHBoxLayout()
        iconLabel = QLabel()
        iconLabelPixmap = FIF.DEVELOPER_TOOLS.icon().pixmap(24, 24)
        iconLabel.setPixmap(iconLabelPixmap)
        titleLayout.addWidget(iconLabel)
        title = SubtitleLabel("调试信息", self)
        titleLayout.addWidget(title)
        titleLayout.addStretch()
        layout.addLayout(titleLayout)
        gridLayout = QGridLayout()
        gridLayout.setSpacing(12)
        gridLayout.setHorizontalSpacing(24)
        
        # FPS
        gridLayout.addWidget(StrongBodyLabel("FPS", self), 0, 0)
        self.fpsLabel = BodyLabel("0", self)
        self.fpsLabel.setObjectName("debugValueLabel")
        gridLayout.addWidget(self.fpsLabel, 0, 1)
        # 内存占用
        gridLayout.addWidget(StrongBodyLabel("内存", self), 0, 2)
        self.memoryLabel = BodyLabel("0 MB", self)
        self.memoryLabel.setObjectName("debugValueLabel")
        gridLayout.addWidget(self.memoryLabel, 0, 3)
        # CPU 使用率
        gridLayout.addWidget(StrongBodyLabel("CPU", self), 1, 0)
        self.cpuLabel = BodyLabel("0%", self)
        self.cpuLabel.setObjectName("debugValueLabel")
        gridLayout.addWidget(self.cpuLabel, 1, 1)
        # 窗口状态
        gridLayout.addWidget(StrongBodyLabel("窗口状态", self), 1, 2)
        self.windowStateLabel = BodyLabel("正常", self)
        gridLayout.addWidget(self.windowStateLabel, 1, 3)
        # 界面状态
        gridLayout.addWidget(StrongBodyLabel("界面状态", self), 2, 0)
        self.interfaceStateLabel = BodyLabel("-", self)
        gridLayout.addWidget(self.interfaceStateLabel, 2, 1)
        # 组件数量
        gridLayout.addWidget(StrongBodyLabel("组件数量", self), 2, 2)
        self.componentCountLabel = BodyLabel("0", self)
        gridLayout.addWidget(self.componentCountLabel, 2, 3)
        layout.addLayout(gridLayout)
        # 分隔线
        line = QLabel(self)
        line.setObjectName("debugSeparator")
        line.setFixedHeight(1)
        layout.addWidget(line)
        # 实时更新按钮
        self.debugUpdateToggle = ToggleButton("实时更新", self)
        self.debugUpdateToggle.setChecked(True)
        self.debugUpdateToggle.setIcon(FIF.SYNC)
        layout.addWidget(self.debugUpdateToggle)
        
        # 弹出窗口按钮
        self.popOutButton = PushButton("弹出窗口", self)
        self.popOutButton.clicked.connect(self._togglePopOut)
        layout.addWidget(self.popOutButton)
        
        return card
    
    def _createAPITestCard(self):
        """创建 API 测试工具卡片"""
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(16)
        layout.setContentsMargins(16, 16, 16, 16)
        titleLayout = QHBoxLayout()
        iconLabel = QLabel()
        iconLabelPixmap = FIF.SETTING.icon().pixmap(24, 24)
        iconLabel.setPixmap(iconLabelPixmap)
        titleLayout.addWidget(iconLabel)
        title = SubtitleLabel("API 测试工具", self)
        titleLayout.addWidget(title)
        titleLayout.addStretch()
        layout.addLayout(titleLayout)
        poetryLayout = QHBoxLayout()
        poetryLayout.addWidget(StrongBodyLabel("一言 API", self))
        poetryLayout.addStretch()
        self.testPoetryButton = PrimaryPushButton("测试", self)
        self.testPoetryButton.setIcon(FIF.PLAY)
        self.testPoetryButton.setFixedWidth(120)
        self.testPoetryButton.clicked.connect(self._testPoetryAPI)
        poetryLayout.addWidget(self.testPoetryButton)
        layout.addLayout(poetryLayout)
        self.poetryResultLabel = BodyLabel("结果：-", self)
        self.poetryResultLabel.setWordWrap(True)
        layout.addWidget(self.poetryResultLabel)
        weatherLayout = QHBoxLayout()
        weatherLayout.addWidget(StrongBodyLabel("天气 API", self))
        weatherLayout.addStretch()
        self.testWeatherButton = PrimaryPushButton("测试", self)
        self.testWeatherButton.setIcon(FIF.PLAY)
        self.testWeatherButton.setFixedWidth(120)
        self.testWeatherButton.clicked.connect(self._testWeatherAPI)
        weatherLayout.addWidget(self.testWeatherButton)
        layout.addLayout(weatherLayout)
        self.weatherResultLabel = BodyLabel("结果：-", self)
        self.weatherResultLabel.setWordWrap(True)
        layout.addWidget(self.weatherResultLabel)
        
        # 分隔线
        line = QLabel(self)
        line.setObjectName("debugSeparator")
        line.setFixedHeight(1)
        layout.addWidget(line)
        
        self.rawDataEdit = QTextEdit(self)
        self.rawDataEdit.setPlaceholderText("API 原始响应数据...")
        self.rawDataEdit.setMaximumHeight(150)
        layout.addWidget(self.rawDataEdit)
        
        return card
    
    def _createWeatherDebugCard(self):
        """天气调试"""
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(12)
        layout.setContentsMargins(16, 16, 16, 16)
        titleLayout = QHBoxLayout()
        iconLabel = QLabel()
        iconLabelPixmap = FIF.CLOUD.icon().pixmap(24, 24)
        iconLabel.setPixmap(iconLabelPixmap)
        titleLayout.addWidget(iconLabel)
        title = SubtitleLabel("天气模拟", self)
        titleLayout.addWidget(title)
        titleLayout.addStretch()
        layout.addLayout(titleLayout)

        self.weatherCodeMap = {
            0: "晴", 1: "多云", 2: "阴", 3: "阵雨", 4: "雷阵雨",
            5: "雷阵雨并伴有冰雹", 6: "雨夹雪", 7: "小雨", 8: "中雨",
            9: "大雨", 10: "暴雨", 11: "大暴雨", 12: "特大暴雨",
            13: "阵雪", 14: "小雪", 15: "中雪", 16: "大雪", 17: "暴雪",
            18: "雾", 19: "冻雨", 20: "沙尘暴", 21: "小雨 - 中雨",
            22: "中雨 - 大雨", 23: "大雨 - 暴雨", 24: "暴雨 - 大暴雨",
            25: "大暴雨 - 特大暴雨", 26: "小雪 - 中雪", 27: "中雪 - 大雪",
            28: "大雪 - 暴雪", 29: "浮尘", 30: "扬沙", 31: "强沙尘暴",
            32: "飑", 33: "龙卷风", 34: "弱高吹雪", 35: "轻雾",
            50: "晴(夜)", 51: "多云(夜)", 52: "阴(夜)", 53: "霾",
            54: "小雨(夜)", 55: "中雨(夜)", 56: "大雨(夜)", 57: "暴雨(夜)",
            58: "雷阵雨(夜)", 59: "冰雹(夜)", 60: "小雪(夜)", 61: "中雪(夜)",
            62: "大雪(夜)", 63: "雾(夜)", 64: "霾(夜)", 65: "沙尘(夜)",
            66: "大风(夜)", 67: "台风(夜)", 68: "暴雨(夜)", 69: "暴雪(夜)",
            70: "雨夹雪(夜)", 71: "冻雨(夜)", 72: "雾凇(夜)", 73: "霜冻(夜)",
            74: "沙尘暴(夜)", 75: "扬沙(夜)", 76: "浮尘(夜)", 77: "强沙尘暴(夜)",
            99: "未知",
        }

        selectRow = QHBoxLayout()
        selectRow.addWidget(BodyLabel("选择天气:", self))
        self.weatherCodeCombo = ComboBox(self)
        for code, name in sorted(self.weatherCodeMap.items()):
            self.weatherCodeCombo.addItem(f"{code} - {name}", userData=code)
        self.weatherCodeCombo.currentIndexChanged.connect(self._onWeatherCodeChanged)
        self.weatherCodeCombo.setMinimumWidth(280)
        selectRow.addWidget(self.weatherCodeCombo)
        selectRow.addStretch()
        layout.addLayout(selectRow)

        previewRow = QHBoxLayout()
        previewRow.addWidget(BodyLabel("图标预览:", self))
        self.weatherIconPreviewLabel = ImageLabel(self)
        self.weatherIconPreviewLabel.setFixedSize(48, 48)
        previewRow.addWidget(self.weatherIconPreviewLabel)
        self.weatherNamePreviewLabel = BodyLabel("-", self)
        previewRow.addWidget(self.weatherNamePreviewLabel)
        previewRow.addStretch()
        layout.addLayout(previewRow)

        tempRow = QHBoxLayout()
        tempRow.addWidget(BodyLabel("温度显示:", self))
        self.weatherTempInput = LineEdit(self)
        self.weatherTempInput.setPlaceholderText("例如: 25°C")
        self.weatherTempInput.setMaximumWidth(150)
        tempRow.addWidget(self.weatherTempInput)
        tempRow.addStretch()
        layout.addLayout(tempRow)

        buttonRow = QHBoxLayout()
        self.applyWeatherButton = PrimaryPushButton("应用到主界面", self)
        self.applyWeatherButton.setIcon(FIF.PLAY)
        self.applyWeatherButton.clicked.connect(self._applyWeatherToMain)
        buttonRow.addWidget(self.applyWeatherButton)
        self.resetWeatherButton = PushButton("重置", self)
        self.resetWeatherButton.clicked.connect(self._resetWeatherDebug)
        buttonRow.addWidget(self.resetWeatherButton)
        buttonRow.addStretch()
        layout.addLayout(buttonRow)

        line = QLabel(self)
        line.setObjectName("debugSeparator")
        line.setFixedHeight(1)
        layout.addWidget(line)

        iconGridLabel = BodyLabel("图标列表 (点击快速选择):", self)
        layout.addWidget(iconGridLabel)
        
        self.weatherIconGrid = QWidget(self)
        self.weatherIconGridLayout = QGridLayout(self.weatherIconGrid)
        self.weatherIconGridLayout.setSpacing(8)
        self.weatherIconGrid.setObjectName("weatherIconGrid")
        
        icon_map = {
            0: "0.svg", 1: "1.svg", 2: "2.svg", 3: "7.svg", 4: "4.svg",
            5: "5.svg", 6: "19.svg", 7: "7.svg", 8: "8.svg", 9: "9.svg",
            10: "10.svg", 11: "11.svg", 12: "11.svg", 13: "14.svg", 14: "14.svg",
            15: "15.svg", 16: "16.svg", 17: "17.svg", 18: "18.svg", 19: "19.svg",
            20: "20.svg", 21: "7.svg", 22: "8.svg", 23: "9.svg", 24: "10.svg",
            25: "11.svg", 26: "14.svg", 27: "15.svg", 28: "16.svg", 29: "18.svg",
            30: "20.svg", 31: "20.svg", 32: "3.svg", 33: "3.svg", 34: "16.svg",
            35: "18.svg", 50: "0.svg", 51: "1.svg", 52: "2.svg", 53: "18.svg",
            54: "7.svg", 55: "8.svg", 56: "9.svg", 57: "10.svg", 58: "4.svg",
            59: "5.svg", 60: "14.svg", 61: "15.svg", 62: "16.svg", 63: "18.svg",
            64: "18.svg", 65: "18.svg", 66: "3.svg", 67: "3.svg", 68: "11.svg",
            69: "17.svg", 70: "19.svg", 71: "19.svg", 72: "18.svg", 73: "18.svg",
            74: "20.svg", 75: "20.svg", 76: "18.svg", 77: "20.svg", 99: "0.svg",
        }
        
        col = 0
        row = 0
        for code, name in sorted(self.weatherCodeMap.items()):
            item = self._createWeatherIconItem(code, name, icon_map.get(code, "0.svg"))
            self.weatherIconGridLayout.addWidget(item, row, col)
            col += 1
            if col >= 6:
                col = 0
                row += 1
        
        gridScroll = ScrollArea(self)
        gridScroll.setWidget(self.weatherIconGrid)
        gridScroll.setWidgetResizable(True)
        gridScroll.setMinimumHeight(200)
        gridScroll.setMaximumHeight(280)
        layout.addWidget(gridScroll)

        self.weatherCodeCombo.setCurrentIndex(0)
        self._onWeatherCodeChanged(0)

        return card

    def _createWeatherIconItem(self, code, name, icon_file):
        """气图标网格项"""
        from core.constants import get_resPath
        from PyQt5.QtGui import QPixmap
        
        item = CardWidget()
        item.setFixedSize(115, 80)
        item.setCursor(Qt.PointingHandCursor)
        item._weatherCode = code
        
        layout = QVBoxLayout(item)
        layout.setContentsMargins(4, 6, 4, 4)
        layout.setSpacing(2)
        layout.setAlignment(Qt.AlignCenter)
        
        imgLabel = ImageLabel(self)
        imgLabel.setFixedSize(32, 32)
        icon_path = get_resPath(os.path.join("resource", "icons", "weather", icon_file))
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path).scaled(28, 28, Qt.KeepAspectRatio, Qt.SmoothTransformation)
            imgLabel.setImage(pixmap)
        else:
            imgLabel.setImage(QPixmap(28, 28))
        layout.addWidget(imgLabel, alignment=Qt.AlignHCenter)
        
        codeLabel = BodyLabel(f"{code}", self)
        codeLabel.setStyleSheet("font-size: 11px; font-weight: bold;")
        codeLabel.setAlignment(Qt.AlignHCenter)
        layout.addWidget(codeLabel)
        
        nameLabel = BodyLabel(name[:5], self)
        nameLabel.setStyleSheet("font-size: 10px;")
        nameLabel.setAlignment(Qt.AlignHCenter)
        layout.addWidget(nameLabel)
        
        item.mousePressEvent = lambda e, c=code: self._onGridItemClick(c)
        
        return item
    
    def _onGridItemClick(self, code):
        """选中"""
        for i in range(self.weatherCodeCombo.count()):
            if self.weatherCodeCombo.itemData(i) == code:
                self.weatherCodeCombo.setCurrentIndex(i)
                break

    def _onWeatherCodeChanged(self, index):
        """更新预览"""
        code = self.weatherCodeCombo.currentData()
        name = self.weatherCodeMap.get(code, "未知")
        self.weatherNamePreviewLabel.setText(name)
        self._previewWeatherIcon(code)
    
    def _previewWeatherIcon(self, code):
        """预览天气图标"""
        from core.constants import get_resPath
        import os
        icon_map = {
            0: "0.svg", 1: "1.svg", 2: "2.svg", 3: "7.svg", 4: "4.svg",
            5: "5.svg", 6: "19.svg", 7: "7.svg", 8: "8.svg", 9: "9.svg",
            10: "10.svg", 11: "11.svg", 12: "11.svg", 13: "14.svg", 14: "14.svg",
            15: "15.svg", 16: "16.svg", 17: "17.svg", 18: "18.svg", 19: "19.svg",
            20: "20.svg", 21: "7.svg", 22: "8.svg", 23: "9.svg", 24: "10.svg",
            25: "11.svg", 26: "14.svg", 27: "15.svg", 28: "16.svg", 29: "18.svg",
            30: "20.svg", 31: "20.svg", 32: "3.svg", 33: "3.svg", 34: "16.svg",
            35: "18.svg", 50: "0.svg", 51: "1.svg", 52: "2.svg", 53: "18.svg",
            54: "7.svg", 55: "8.svg", 56: "9.svg", 57: "10.svg", 58: "4.svg",
            59: "5.svg", 60: "14.svg", 61: "15.svg", 62: "16.svg", 63: "18.svg",
            64: "18.svg", 65: "18.svg", 66: "3.svg", 67: "3.svg", 68: "11.svg",
            69: "17.svg", 70: "19.svg", 71: "19.svg", 72: "18.svg", 73: "18.svg",
            74: "20.svg", 75: "20.svg", 76: "18.svg", 77: "20.svg", 99: "0.svg",
        }
        icon_file = icon_map.get(code, "0.svg")
        icon_path = get_resPath(os.path.join("resource", "icons", "weather", icon_file))
        if os.path.exists(icon_path):
            from PyQt5.QtGui import QPixmap
            pixmap = QPixmap(icon_path).scaled(40, 40, Qt.KeepAspectRatio, Qt.SmoothTransformation)
            self.weatherIconPreviewLabel.setImage(pixmap)
    
    def _applyWeatherToMain(self):
        """应用到主界面"""
        code = self.weatherCodeCombo.currentData()
        if code is None:return
        mw = self.mainWindow
        
        self._savedWeatherCode = getattr(mw, 'current_weather_code', None)
        self._savedWeatherTemp = mw.weatherTempLabel.text() if hasattr(mw, 'weatherTempLabel') else ""
        
        mw.current_weather_code = code
        
        temp_text = self.weatherTempInput.text().strip()
        if temp_text:
            mw.weatherTempLabel.setText(temp_text)
        elif hasattr(mw, 'weatherTempLabel'):
            name = self.weatherCodeMap.get(code, "")
            mw.weatherTempLabel.setText(f"模拟: {name}")
        
        mw._MainWindow__updateWeatherIcon()
        
        InfoBar.success(
            title="天气模拟",
            content=f"已应用天气代码 {code} ({self.weatherCodeMap.get(code, '')}) 到主界面",
            parent=self,
            duration=2500
        )
    
    def _resetWeatherDebug(self):
        """重置天气模拟"""
        self.weatherCodeCombo.setCurrentIndex(0)
        self._onWeatherCodeChanged(0)
        self.weatherTempInput.clear()
        
        if not hasattr(self, '_savedWeatherCode'):return
        mw = self.mainWindow
        if self._savedWeatherCode is not None:
            mw.current_weather_code = self._savedWeatherCode
        else:
            mw.current_weather_code = None
        
        if hasattr(mw, 'weatherTempLabel'):
            mw.weatherTempLabel.setText(self._savedWeatherTemp)
        
        if mw.current_weather_code is not None:
            mw._MainWindow__updateWeatherIcon()
        else:
            if hasattr(mw, 'weatherIconLabel'):
                mw.weatherIconLabel.clear()
        
        del self._savedWeatherCode
        del self._savedWeatherTemp

    def _createResourceMonitorCard(self):
        """创建资源监控卡片"""
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(10)
        title = SubtitleLabel("资源监控", self)
        layout.addWidget(title)
        gridLayout = QGridLayout()
        gridLayout.setSpacing(10)
        
        # 壁纸文件夹大小
        gridLayout.addWidget(BodyLabel("壁纸文件夹:", self), 0, 0)
        self.wallpaperSizeLabel = StrongBodyLabel("0 MB", self)
        gridLayout.addWidget(self.wallpaperSizeLabel, 0, 1)
        # 壁纸文件数量
        gridLayout.addWidget(BodyLabel("壁纸数量:", self), 0, 2)
        self.wallpaperCountLabel = StrongBodyLabel("0", self)
        gridLayout.addWidget(self.wallpaperCountLabel, 0, 3)
        # 日志文件数量
        gridLayout.addWidget(BodyLabel("日志文件:", self), 1, 0)
        self.logFileCountLabel = StrongBodyLabel("0", self)
        gridLayout.addWidget(self.logFileCountLabel, 1, 1)
        # 日志文件夹大小
        gridLayout.addWidget(BodyLabel("日志大小:", self), 1, 2)
        self.logSizeLabel = StrongBodyLabel("0 MB", self)
        gridLayout.addWidget(self.logSizeLabel, 1, 3)
        
        layout.addLayout(gridLayout)
        
        # 清理按钮
        buttonLayout = QHBoxLayout()
        self.clearCacheButton = PushButton("清理缓存", self)
        self.clearCacheButton.clicked.connect(self._clearCache)
        buttonLayout.addWidget(self.clearCacheButton)
        
        self.clearLogsButton = PushButton("清理日志", self)
        self.clearLogsButton.clicked.connect(self._clearLogs)
        buttonLayout.addWidget(self.clearLogsButton)
        
        layout.addLayout(buttonLayout)
        return card
    
    def _createWindowDebugCard(self):
        """创建窗口调试卡片"""
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(10)
        title = SubtitleLabel("窗口调试", self)
        layout.addWidget(title)
        gridLayout = QGridLayout()
        gridLayout.setSpacing(10)
        
        # 窗口尺寸
        gridLayout.addWidget(BodyLabel("窗口尺寸:", self), 0, 0)
        self.windowSizeLabel = StrongBodyLabel("0x0", self)
        gridLayout.addWidget(self.windowSizeLabel, 0, 1)
        # 窗口位置
        gridLayout.addWidget(BodyLabel("窗口位置:", self), 0, 2)
        self.windowPosLabel = StrongBodyLabel("(0, 0)", self)
        gridLayout.addWidget(self.windowPosLabel, 0, 3)
        # DPI 缩放
        gridLayout.addWidget(BodyLabel("DPI 缩放:", self), 1, 0)
        self.dpiScaleLabel = StrongBodyLabel("1.0", self)
        gridLayout.addWidget(self.dpiScaleLabel, 1, 1)
        # 活动窗口
        gridLayout.addWidget(BodyLabel("活动窗口:", self), 1, 2)
        self.activeWindowLabel = StrongBodyLabel("-", self)
        gridLayout.addWidget(self.activeWindowLabel, 1, 3)
        layout.addLayout(gridLayout)
        # 操作按钮
        buttonLayout = QHBoxLayout()
        self.refreshWindowButton = PushButton("刷新信息", self)
        self.refreshWindowButton.clicked.connect(self._updateWindowDebug)
        buttonLayout.addWidget(self.refreshWindowButton)
        self.forceRepaintButton = PushButton("强制重绘", self)
        self.forceRepaintButton.clicked.connect(self._forceRepaint)
        buttonLayout.addWidget(self.forceRepaintButton)
        layout.addLayout(buttonLayout)
        # 窗口信息详情
        self.windowInfoEdit = QTextEdit(self)
        self.windowInfoEdit.setPlaceholderText("窗口详细信息")
        self.windowInfoEdit.setMaximumHeight(100)
        self.windowInfoEdit.setReadOnly(True)
        layout.addWidget(self.windowInfoEdit)
        
        return card
    
    def _createElementCheckCard(self):
        """创建界面元素检查卡片"""
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(10)
        title = SubtitleLabel("界面元素", self)
        layout.addWidget(title)
        
        # 启用检查
        enableLayout = QHBoxLayout()
        enableLayout.addWidget(BodyLabel("启用元素检查:", self))
        self.elementCheckToggle = ToggleButton("启用", self)
        self.elementCheckToggle.toggled.connect(self._toggleElementCheck)
        enableLayout.addWidget(self.elementCheckToggle)
        layout.addLayout(enableLayout)
        # 元素信息
        infoLayout = QVBoxLayout()
        infoLayout.addWidget(BodyLabel("当前元素:", self))
        self.elementInfoEdit = QTextEdit(self)
        self.elementInfoEdit.setPlaceholderText("鼠标悬停在元素上查看信息")
        self.elementInfoEdit.setMaximumHeight(150)
        self.elementInfoEdit.setReadOnly(True)
        infoLayout.addWidget(self.elementInfoEdit)
        layout.addLayout(infoLayout)
        # 组件树
        treeLayout = QVBoxLayout()
        treeLayout.addWidget(BodyLabel("组件树:", self))
        self.componentTreeEdit = QTextEdit(self)
        self.componentTreeEdit.setPlaceholderText("组件层级结构")
        self.componentTreeEdit.setMaximumHeight(200)
        self.componentTreeEdit.setReadOnly(True)
        treeLayout.addWidget(self.componentTreeEdit)
        layout.addLayout(treeLayout)
    
        return card
    
    def _setupTimers(self):
        """设置定时器"""
        self.fpsTimer = QTimer(self)
        self.fpsTimer.timeout.connect(self._updateDebugInfo)
        self.fpsTimer.start(100)
        self.resourceTimer = QTimer(self)
        self.resourceTimer.timeout.connect(self._updateResourceMonitor)
        self.resourceTimer.start(10000)
        self.windowTimer = QTimer(self)
        self.windowTimer.timeout.connect(self._updateWindowDebug)
        self.windowTimer.start(2000)
        self.fpsCheckTimer = QTimer(self)
        self.fpsCheckTimer.timeout.connect(self._updateFPS)
        self.fpsCheckTimer.start(16)
        self.changeTimer = QTimer(self)
        self.changeTimer.timeout.connect(self._checkWindowChanges)
        self.changeTimer.start(50)
        
    def _loadStyleSheet(self):
        """加载样式表"""
        self.setStyleSheet(load_qss('developer_panel.qss'))
    
    def _updateTheme(self):
        """更新主题"""
        self._loadStyleSheet()
    
    
    def eventFilter(self, obj, event):
        """事件过滤器"""
        if not hasattr(self, 'elementCheckEnabled'):return super().eventFilter(obj, event)
        if obj == self.mainWindow and event.type() == QEvent.Paint and hasattr(self, 'debugUpdateToggle') and self.debugUpdateToggle.isChecked():
            self.frameCount += 1
            currentTime = time.time()
            if currentTime - self.lastFpsTime >= 0.5:
                self.currentFps = self.frameCount / (currentTime - self.lastFpsTime)
                self.fpsLabel.setText(f"{self.currentFps:.1f}")
                self.frameCount = 0
                self.lastFpsTime = currentTime
        
        if not self.elementCheckEnabled:
            return super().eventFilter(obj, event)
        
        if event.type() == QEvent.Enter:
            element_info = []
            element_info.append(f"对象名称：{obj.objectName()}")
            element_info.append(f"类    型：{obj.__class__.__name__}")
            element_info.append(f"可    见：{obj.isVisible()}")
            if isinstance(obj, QWidget):element_info.append(f"启    用：{obj.isEnabled()}")
            
            if hasattr(obj, 'geometry'):
                geom = obj.geometry()
                element_info.append(f"位    置：({geom.x()}, {geom.y()})")
                element_info.append(f"大    小：{geom.width()}x{geom.height()}")
            
            self.elementInfoEdit.setText("\n".join(element_info))
        
        return super().eventFilter(obj, event)
    
    def _updateFPS(self):
        """更新FPS"""
        if not self.debugUpdateToggle.isChecked():
            return
        if self.mainWindow.isVisible():
            self.frameCount += 1
            currentTime = time.time()
            if currentTime - self.lastFpsTime >= 0.5:
                self.currentFps = self.frameCount / (currentTime - self.lastFpsTime)
                self.fpsLabel.setText(f"{self.currentFps:.1f}")
                self.frameCount = 0
                self.lastFpsTime = currentTime
    
    def _checkWindowChanges(self):
        """检测窗口变化来估算 FPS"""
        if not self.debugUpdateToggle.isChecked():
            return
        current_widget = self.mainWindow.stackedWidget.currentWidget()
        widget_changed = False
        if current_widget != self.lastCurrentWidget:
            self.lastCurrentWidget = current_widget
            widget_changed = True
        current_geometry = self.mainWindow.geometry()
        geometry_changed = False
        if current_geometry != self.lastGeometry:
            self.lastGeometry = current_geometry
            geometry_changed = True
        if widget_changed or geometry_changed:
            self.frameCount += 1
            currentTime = time.time()
            if currentTime - self.lastFpsTime >= 0.5:
                self.currentFps = self.frameCount / (currentTime - self.lastFpsTime)
                self.fpsLabel.setText(f"{self.currentFps:.1f}")
                self.frameCount = 0
                self.lastFpsTime = currentTime
    
    def _updateDebugInfo(self):
        """更新信息"""
        if not self.debugUpdateToggle.isChecked():
            return
        
        currentTime = time.time()
        
        # 内存占用
        try:
            mem_info = self.process.memory_info()
            mem_mb = mem_info.rss / 1024 / 1024
            self.memoryLabel.setText(f"{mem_mb:.1f} MB")
        except Exception:
            self.memoryLabel.setText("N/A")
        
        # CPU 使用
        try:
            cpu_times = self.process.cpu_times()
            process_cpu = cpu_times.user + cpu_times.system
            current_time = time.time()
            time_delta = current_time - self.last_cpu_time
            cpu_delta = process_cpu - self.last_cpu_usage
            if time_delta > 0:
                cpu_count = psutil.cpu_count() or 1
                cpu_percent = (cpu_delta / time_delta) * 100 / cpu_count
                cpu_percent = min(cpu_percent, 100.0)
                self.cpuLabel.setText(f"{cpu_percent:.1f}%")
            else:
                self.cpuLabel.setText("0.0%")
            self.last_cpu_time = current_time
            self.last_cpu_usage = process_cpu
        except Exception:
            self.cpuLabel.setText("N/A")
        
        if self.mainWindow.isVisible():
            self.windowStateLabel.setText("可见")
        else:
            self.windowStateLabel.setText("隐藏")
        
        # 界面状态
        current_widget = self.mainWindow.stackedWidget.currentWidget()
        if current_widget:
            self.interfaceStateLabel.setText(current_widget.objectName())
        
        # 组件数量
        component_count = len(self.mainWindow.findChildren(QWidget))
        self.componentCountLabel.setText(str(component_count))
    
    def _testPoetryAPI(self):
        """测试一言 API"""
        start_time = time.time()
        try:
            timeout = 10
            
            api_url = cfg.poetryApiUrl.value
            response = requests.get(api_url, timeout=timeout)
            elapsed = (time.time() - start_time) * 1000
            
            self.poetryResultLabel.setText(f"✓ 成功 ({elapsed:.0f}ms): {response.text[:50]}")
            self.rawDataEdit.setText(response.text)
            
            InfoBar.success(
                title="API 测试",
                content="一言 API 测试成功",
                parent=self,
                duration=2000
            )
        except Exception as e:
            elapsed = (time.time() - start_time) * 1000
            self.poetryResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): {str(e)}")
            logger.error(f"一言 API 测试失败：{e}")
            
            InfoBar.error(
                title="API 测试",
                content=f"一言 API 测试失败：{str(e)}",
                parent=self,
                duration=3000
            )
    
    def _testWeatherAPI(self):
        """测试天气 API"""
        
        start_time = time.time()
        try:
            city_name = cfg.city.value if hasattr(cfg, 'city') and cfg.city.value else "北京"
            city_db = RegionDatabase()
            city_code = city_db.get_code(city_name)
            if not city_code:city_code = "101010100"
            
            weather_service = WeatherService(city_code)
            
            weather_data = weather_service.get_weather()
            elapsed = (time.time() - start_time) * 1000
            if weather_data:
                self.weatherResultLabel.setText(f"✓ 成功 ({elapsed:.0f}ms): {weather_data['weather_text']} {weather_data['temperature']}")
                self.rawDataEdit.setText(f"天气数据:\n温度：{weather_data['temperature']}\n天气：{weather_data['weather_text']}\n天气代码：{weather_data['weather_code']}\n图标：{weather_data['weather_icon']}")
                InfoBar.success(
                    title="API 测试",
                    content=f"天气 API 测试成功 - {weather_data['weather_text']} {weather_data['temperature']}",
                    parent=self,
                    duration=2000
                )
            else:
                self.weatherResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): 未获取到天气数据")
                self.rawDataEdit.setText("天气 API 返回数据为空")
                InfoBar.warning(
                    title="API 测试",
                    content="天气 API 测试失败 - 未获取到天气数据",
                    parent=self,
                    duration=3000
                )
        except Exception as e:
            elapsed = (time.time() - start_time) * 1000
            self.weatherResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): {str(e)}")
            logger.error(f"天气 API 测试失败：{e}")
            InfoBar.error(
                title="API 测试",
                content=f"天气 API 测试失败：{str(e)}",
                parent=self,
                duration=3000
            )
    
    
    def _updateResourceMonitor(self):
        """更新资源监控"""
        try:
            wallpaper_dir = os.path.join(os.path.dirname(__file__), '..', 'wallpaper')
            wallpaper_dir = os.path.normpath(wallpaper_dir)
     
            if os.path.exists(wallpaper_dir):
                total_size = 0
                file_count = 0
                for root, dirs, files in os.walk(wallpaper_dir):
                    for f in files:
                        fp = os.path.join(root, f)
                        try:
                            total_size += os.path.getsize(fp)
                            file_count += 1
                        except Exception:
                            pass
                
                self.wallpaperSizeLabel.setText(f"{total_size / 1024 / 1024:.1f} MB")
                self.wallpaperCountLabel.setText(str(file_count))
            

            log_dir = os.path.join(os.path.dirname(__file__), '..', 'logs')
            log_dir = os.path.normpath(log_dir)
            
            if os.path.exists(log_dir):
                total_size = 0
                file_count = 0
                for root, dirs, files in os.walk(log_dir):
                    for f in files:
                        fp = os.path.join(root, f)
                        try:
                            total_size += os.path.getsize(fp)
                            file_count += 1
                        except Exception:
                            pass
                
                self.logFileCountLabel.setText(str(file_count))
                self.logSizeLabel.setText(f"{total_size / 1024 / 1024:.1f} MB")
        except Exception as e:
            logger.error(f"更新资源监控失败：{e}")
    
    def _clearCache(self):
        """清理缓存"""
        try:
            wallpaper_dir = os.path.join(os.path.dirname(__file__), '..', 'wallpaper')
            wallpaper_dir = os.path.normpath(wallpaper_dir)

            if os.path.exists(wallpaper_dir):
                deleted_count = 0
                deleted_size = 0
                for root, dirs, files in os.walk(wallpaper_dir):
                    for f in files:
                        fp = os.path.join(root, f)
                        try:
                            # 获取文件大小
                            deleted_size += os.path.getsize(fp)
                            os.remove(fp)
                            deleted_count += 1
                        except Exception as e:
                            logger.warning(f"删除壁纸文件失败：{fp}, {e}")
                
                # 更新监控数据
                self._updateResourceMonitor()
                
                InfoBar.success(
                    title="清理缓存",
                    content=f"已清理 {deleted_count} 个文件，释放 {deleted_size / 1024:.1f} KB 空间",
                    parent=self,
                    duration=3000
                )
            else:
                InfoBar.info(
                    title="清理缓存",
                    content="壁纸文件夹不存在",
                    parent=self,
                    duration=2000
                )
        except Exception as e:
            logger.error(f"清理缓存失败：{e}")
            InfoBar.error(
                title="清理缓存",
                content=f"清理缓存失败：{str(e)}",
                parent=self,
                duration=3000
            )
    
    def _clearLogs(self):
        """清理日志"""
        try:
            log_dir = os.path.join(os.path.dirname(__file__), '..', 'logs')
            log_dir = os.path.normpath(log_dir)
            
            if os.path.exists(log_dir):
                for root, dirs, files in os.walk(log_dir):
                    for f in files:
                        if f.endswith('.log'):
                            fp = os.path.join(root, f)
                            try:
                                os.remove(fp)
                            except Exception:
                                pass
                
                InfoBar.success(
                    title="清理日志",
                    content="日志已清理",
                    parent=self,
                    duration=2000
                )
                self._updateResourceMonitor()
        except Exception as e:
            logger.error(f"清理日志失败：{e}")
            InfoBar.error(
                title="清理日志",
                content=f"清理日志失败：{str(e)}",
                parent=self,
                duration=3000
            )
    
    def _updateWindowDebug(self):
        """更新窗口调试信息"""
        try:
            # 窗口尺寸
            size = self.mainWindow.size()
            self.windowSizeLabel.setText(f"{size.width()}x{size.height()}")
            # 窗口位置
            pos = self.mainWindow.pos()
            self.windowPosLabel.setText(f"({pos.x()}, {pos.y()})")
            # DPI缩放
            dpi = self.mainWindow.logicalDpiX()
            dpi_scale = dpi / 96.0
            self.dpiScaleLabel.setText(f"{dpi_scale:.2f}")
            # 活动窗口
            active_window = QApplication.activeWindow()
            if active_window:
                self.activeWindowLabel.setText(active_window.objectName())
            else:
                self.activeWindowLabel.setText("无")
            info = []
            info.append(f"窗口标题：{self.mainWindow.windowTitle()}")
            info.append(f"窗口状态：{'正常' if self.mainWindow.isVisible() else '隐藏'}")
            info.append(f"窗口激活：{'是' if self.mainWindow.isActiveWindow() else '否'}")
            info.append(f"窗口焦点：{'是' if self.mainWindow.hasFocus() else '否'}")
            info.append(f"窗口置顶：{'是' if self.mainWindow.windowFlags() & Qt.WindowStaysOnTopHint else '否'}")
            
            self.windowInfoEdit.setText("\n".join(info))
        except Exception as e:
            logger.error(f"更新窗口调试信息失败：{e}")
    
    def _forceRepaint(self):
        """强制重绘"""
        try:
            self.mainWindow.update()
            self.mainWindow.repaint()
            
            InfoBar.success(
                title="重绘",
                content="窗口已强制重绘",
                parent=self,
                duration=1500
            )
        except Exception as e:
            logger.error(f"强制重绘失败：{e}")
            InfoBar.error(
                title="重绘",
                content=f"强制重绘失败：{str(e)}",
                parent=self,
                duration=3000
            )
    
    def _toggleElementCheck(self, enabled):
        """切换元素检查"""
        self.elementCheckEnabled = enabled
        
        if enabled:
            QApplication.instance().installEventFilter(self)
            InfoBar.success(
                title="元素检查",
                content="元素检查已启用，鼠标悬停在元素上查看信息",
                parent=self,
                duration=3000
            )
        else:
            QApplication.instance().removeEventFilter(self)
            InfoBar.info(
                title="元素检查",
                content="元素检查已禁用",
                parent=self,
                duration=2000
            )
    
    def _refreshComponentTree(self):
        """刷新组件树"""
        try:
            tree_lines = []
            def get_widget_tree(widget, indent=0):
                lines = []
                prefix = "  " * indent
                widget_name = widget.objectName() or widget.__class__.__name__
                lines.append(f"{prefix}├─ {widget_name}")
                for child in widget.children():
                    if isinstance(child, QWidget):
                        lines.extend(get_widget_tree(child, indent + 1))
                return lines
            
            tree_lines = get_widget_tree(self.mainWindow)
            self.componentTreeEdit.setText("\n".join(tree_lines))
        except Exception as e:
            logger.error(f"刷新组件树失败：{e}")
    
    def _togglePopOut(self):
        """切换弹出/恢复窗口"""
        if hasattr(self, '_popOutWindow') and self._popOutWindow is not None:
            self._restoreFromPopOut()
        else:
            self._popOut()
    
    def _saveWidgetRefs(self):
        self._savedWidgetRefs = {}
        for attr in list(vars(self)):
            obj = getattr(self, attr)
            if isinstance(obj, QWidget):self._savedWidgetRefs[attr] = obj
    
    def _restoreWidgetRefs(self):
        if not hasattr(self, '_savedWidgetRefs'):return
        for attr, value in self._savedWidgetRefs.items():
            setattr(self, attr, value)
        del self._savedWidgetRefs
    
    def _stopTimers(self):
        self.fpsTimer.stop()
        self.resourceTimer.stop()
        self.windowTimer.stop()
        self.fpsCheckTimer.stop()
        self.changeTimer.stop()
    
    def _startTimers(self):
        self.fpsTimer.start(100)
        self.resourceTimer.start(10000)
        self.windowTimer.start(2000)
        self.fpsCheckTimer.start(16)
        self.changeTimer.start(50)
    
    def _popOut(self):
        """将调试面板弹出到独立窗口"""
        try:
            self._savedViewportMargins = self.viewportMargins()
            self.setViewportMargins(0, 0, 0, 0)
            self._saveWidgetRefs()
            
            class _PopOutWindow(QWidget):
                def __init__(self, panel):
                    super().__init__()
                    self._panel_ref = panel
                def closeEvent(self, event):
                    panel = self._panel_ref
                    self._panel_ref = None
                    if panel:panel._restoreFromPopOut()
                    event.accept()
            
            self._popOutWindow = _PopOutWindow(self)
            self._popOutWindow.setObjectName('developerPanel')
            self._popOutWindow.setWindowTitle("调试面板 - ClassLively")
            self._popOutWindow.setFixedSize(850, 750)
            
            qss = load_qss('developer_panel.qss')
            self._popOutWindow.setStyleSheet(qss)
            
            outer_layout = QVBoxLayout(self._popOutWindow)
            outer_layout.setContentsMargins(0, 0, 0, 0)
            outer_layout.setSpacing(0)
            
            container = QWidget()
            container.setObjectName('scrollWidget')
            container.setStyleSheet("background-color: transparent;")
            content_layout = QVBoxLayout(container)
            content_layout.setContentsMargins(36, 20, 36, 20)
            content_layout.setSpacing(15)
            
            debugCard = self._createDebugCard()
            apiCard = self._createAPITestCard()
            weatherDebugCard = self._createWeatherDebugCard()
            resourceCard = self._createResourceMonitorCard()
            windowCard = self._createWindowDebugCard()
            elementCard = self._createElementCheckCard()
            
            content_layout.addWidget(debugCard)
            content_layout.addWidget(apiCard)
            content_layout.addWidget(weatherDebugCard)
            content_layout.addWidget(resourceCard)
            content_layout.addWidget(windowCard)
            content_layout.addWidget(elementCard)
            
            scroll = ScrollArea(self._popOutWindow)
            scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
            scroll.setWidgetResizable(True)
            scroll.setStyleSheet("background-color: transparent; border: none;")
            scroll.setWidget(container)
            self._popOutContentContainer = container
            outer_layout.addWidget(scroll)
            
            screen = QApplication.primaryScreen().availableGeometry()
            x = (screen.width() - self._popOutWindow.width()) // 2
            y = (screen.height() - self._popOutWindow.height()) // 2
            self._popOutWindow.move(x, y)
            
            self._popOutWindow.show()
            self.popOutButton.setText("恢复面板")
            
            mw = self.mainWindow
            if hasattr(mw, 'developerNavItem'): mw.developerNavItem.setVisible(False)
            if hasattr(mw, 'home'): mw.switchTo(mw.home)
            
            QTimer.singleShot(300, self._refreshComponentTree)
        except Exception as e:
            logger.error(f"弹出调试面板失败: {e}")
            self._safeCleanupPopOut()
    
    def _restoreFromPopOut(self):
        """从独立窗口恢复调试面板"""
        pop_win = getattr(self, '_popOutWindow', None)
        if pop_win is None:return
        self._stopTimers()
        self._popOutWindow = None
        self._popOutContentContainer = None
        self._restoreWidgetRefs()
        if hasattr(self, '_savedViewportMargins'): self.setViewportMargins(self._savedViewportMargins)
        self.popOutButton.setText("弹出窗口")
        mw = self.mainWindow
        if hasattr(mw, 'developerNavItem') and cfg.developerMode.value: mw.developerNavItem.setVisible(True)
        self._startTimers()
    
    def _safeCleanupPopOut(self):
        """安全清理弹出窗口"""
        self._stopTimers()
        pop_win = getattr(self, '_popOutWindow', None)
        if pop_win is not None:
            self._popOutWindow = None
            self._popOutContentContainer = None
            if hasattr(pop_win, '_panel_ref'): pop_win._panel_ref = None
            pop_win.hide()
            pop_win.deleteLater()
        self._restoreWidgetRefs()
        if hasattr(self, '_savedViewportMargins'): self.setViewportMargins(self._savedViewportMargins)
        self.popOutButton.setText("弹出窗口")
        mw = self.mainWindow
        if hasattr(mw, 'developerNavItem') and cfg.developerMode.value: mw.developerNavItem.setVisible(True)
        self._startTimers()
    
    def _findComponent(self):
        """查找组件"""
        pass
