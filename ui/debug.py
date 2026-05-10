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
调试面板
"""

import os
import sys
if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:sys.path.insert(0, _BASE_DIR)

import socket
import subprocess
import time
import datetime
import psutil
import requests
from PyQt6.QtCore import QEvent, QTimer, Qt
from PyQt6.QtGui import QPixmap
from PyQt6.QtWidgets import (
    QApplication,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QProgressBar,
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
    setTheme,
    SpinBox,
    StrongBodyLabel,
    SubtitleLabel,
    ToggleButton,
)

from core.config import cfg
from core.constants import BASE_DIR, get_resPath, load_qss
from core.logger import logger
from services.weather import WeatherService, RegionDatabase

from .base_scroll import BaseScrollAreaInterface


class DebugPanel(BaseScrollAreaInterface):

    def __init__(self, mainWindow):
        super().__init__("调试", parent=None)
        self.mainWindow = mainWindow
        self.setObjectName('debugPanel')

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
        scrollLayout = QVBoxLayout(self.scrollWidget)
        scrollLayout.setSpacing(15)
        scrollLayout.setContentsMargins(60, 10, 60, 20)
        scrollLayout.addWidget(self._createSystemMonitorCard())
        scrollLayout.addWidget(self._createQuickActionsCard())
        scrollLayout.addWidget(self._createNetworkDiagCard())
        scrollLayout.addWidget(self._createAPITestCard())
        scrollLayout.addWidget(self._createWeatherDebugCard())
        scrollLayout.addWidget(self._createElementCheckCard())
        scrollLayout.addWidget(self._createBatchWallpaperCard())
        self._loadStyleSheet()
        QTimer.singleShot(500, self._refreshComponentTree)
        QTimer.singleShot(1000, self._installEventFilter)

    def _installEventFilter(self):
        if self.mainWindow: self.mainWindow.installEventFilter(self)

    def _cardTitle(self, icon, text, parent=None):
        layout = QHBoxLayout()
        layout.setContentsMargins(0, 0, 0, 0)
        iconLabel = QLabel(parent)
        iconLabel.setPixmap(icon.icon().pixmap(22, 22))
        layout.addWidget(iconLabel)
        layout.addWidget(SubtitleLabel(text, parent))
        layout.addStretch()
        return layout

    def _createSystemMonitorCard(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(14)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FIF.APPLICATION, "系统监控", card))

        grid = QGridLayout()
        grid.setSpacing(12)
        grid.setHorizontalSpacing(20)

        grid.addWidget(StrongBodyLabel("FPS", card), 0, 0)
        self.fpsLabel = BodyLabel("0", card)
        self.fpsLabel.setObjectName("debugValueLabel")
        grid.addWidget(self.fpsLabel, 0, 1)

        grid.addWidget(StrongBodyLabel("内存", card), 0, 2)
        self.memoryLabel = BodyLabel("0 MB", card)
        self.memoryLabel.setObjectName("debugValueLabel")
        grid.addWidget(self.memoryLabel, 0, 3)

        grid.addWidget(StrongBodyLabel("CPU", card), 1, 0)
        self.cpuLabel = BodyLabel("0%", card)
        self.cpuLabel.setObjectName("debugValueLabel")
        grid.addWidget(self.cpuLabel, 1, 1)

        grid.addWidget(StrongBodyLabel("窗口状态", card), 1, 2)
        self.windowStateLabel = BodyLabel("正常", card)
        grid.addWidget(self.windowStateLabel, 1, 3)

        grid.addWidget(StrongBodyLabel("壁纸文件夹", card), 2, 0)
        self.wallpaperSizeLabel = StrongBodyLabel("-", card)
        grid.addWidget(self.wallpaperSizeLabel, 2, 1)

        grid.addWidget(StrongBodyLabel("壁纸数量", card), 2, 2)
        self.wallpaperCountLabel = StrongBodyLabel("0", card)
        grid.addWidget(self.wallpaperCountLabel, 2, 3)

        layout.addLayout(grid)

        line = QLabel(card)
        line.setObjectName("debugSeparator")
        line.setFixedHeight(1)
        layout.addWidget(line)

        btnRow = QHBoxLayout()
        self.debugUpdateToggle = ToggleButton("启用监测", card)
        self.debugUpdateToggle.setChecked(True)
        self.debugUpdateToggle.setIcon(FIF.SYNC)
        btnRow.addWidget(self.debugUpdateToggle)
        self.popOutButton = PushButton(FIF.FULL_SCREEN, "弹出窗口", card)
        self.popOutButton.clicked.connect(self._togglePopOut)
        btnRow.addWidget(self.popOutButton)
        btnRow.addStretch()
        layout.addLayout(btnRow)

        return card

    def _createQuickActionsCard(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(14)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FIF.MENU, "快捷操作", card))

        row1 = QHBoxLayout()
        self.reloadThemeBtn = PrimaryPushButton(FIF.PALETTE, "刷新主题", card)
        self.reloadThemeBtn.clicked.connect(self._reloadTheme)
        row1.addWidget(self.reloadThemeBtn)
        self.clearCacheBtn = PushButton(FIF.DELETE, "清理壁纸缓存", card)
        self.clearCacheBtn.clicked.connect(self._clearCache)
        row1.addWidget(self.clearCacheBtn)
        self.clearLogsBtn = PushButton(FIF.BROOM, "清理日志", card)
        self.clearLogsBtn.clicked.connect(self._clearLogs)
        row1.addWidget(self.clearLogsBtn)
        layout.addLayout(row1)

        row2 = QHBoxLayout()
        self.openLogDirBtn = PushButton(FIF.FOLDER, "打开日志目录", card)
        self.openLogDirBtn.clicked.connect(lambda: os.startfile(os.path.join(BASE_DIR, 'logs')))
        row2.addWidget(self.openLogDirBtn)
        self.openWallpaperDirBtn = PushButton(FIF.FOLDER_ADD, "打开壁纸目录", card)
        self.openWallpaperDirBtn.clicked.connect(lambda: os.startfile(os.path.join(BASE_DIR, 'wallpaper')))
        row2.addWidget(self.openWallpaperDirBtn)
        self.openConfigDirBtn = PushButton(FIF.SETTING, "打开配置目录", card)
        self.openConfigDirBtn.clicked.connect(lambda: os.startfile(os.path.join(BASE_DIR, 'config')))
        row2.addWidget(self.openConfigDirBtn)
        layout.addLayout(row2)

        row3 = QHBoxLayout()
        self.forceRepaintBtn = PushButton(FIF.SYNC, "强制重绘界面", card)
        self.forceRepaintBtn.clicked.connect(self._forceRepaint)
        row3.addWidget(self.forceRepaintBtn)
        self.restartAppBtn = PushButton(FIF.UPDATE, "重启应用", card)
        self.restartAppBtn.clicked.connect(self._restartApp)
        row3.addWidget(self.restartAppBtn)
        row3.addStretch()
        layout.addLayout(row3)

        return card

    def _createNetworkDiagCard(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(14)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FIF.GLOBE, "网络诊断", card))

        targetRow = QHBoxLayout()
        targetRow.addWidget(BodyLabel("测试目标:", card))
        self.networkTargetCombo = ComboBox(card)
        self.networkTargetCombo.addItems(["www.baidu.com", "www.qq.com", "www.aliyun.com", "www.bilibili.com"])
        self.networkTargetCombo.setMinimumWidth(200)
        targetRow.addWidget(self.networkTargetCombo)
        targetRow.addStretch()
        layout.addLayout(targetRow)

        resultGrid = QGridLayout()
        resultGrid.setSpacing(8)

        resultGrid.addWidget(BodyLabel("连通性:", card), 0, 0)
        self.networkConnectLabel = StrongBodyLabel("-", card)
        resultGrid.addWidget(self.networkConnectLabel, 0, 1)

        resultGrid.addWidget(BodyLabel("延迟:", card), 0, 2)
        self.networkLatencyLabel = StrongBodyLabel("-", card)
        resultGrid.addWidget(self.networkLatencyLabel, 0, 3)

        resultGrid.addWidget(BodyLabel("DNS 解析:", card), 1, 0)
        self.networkDnsLabel = StrongBodyLabel("-", card)
        resultGrid.addWidget(self.networkDnsLabel, 1, 1)

        resultGrid.addWidget(BodyLabel("一言 API:", card), 1, 2)
        self.networkPoetryLabel = StrongBodyLabel("-", card)
        resultGrid.addWidget(self.networkPoetryLabel, 1, 3)

        layout.addLayout(resultGrid)

        btnRow = QHBoxLayout()
        self.networkTestBtn = PrimaryPushButton(FIF.PLAY, "开始诊断", card)
        self.networkTestBtn.clicked.connect(self._runNetworkDiag)
        btnRow.addWidget(self.networkTestBtn)
        self.networkTestAllBtn = PushButton("全部测试", card)
        self.networkTestAllBtn.clicked.connect(self._runNetworkDiagAll)
        btnRow.addWidget(self.networkTestAllBtn)
        btnRow.addStretch()
        layout.addLayout(btnRow)

        self.networkLogEdit = QTextEdit(card)
        self.networkLogEdit.setPlaceholderText("诊断日志...")
        self.networkLogEdit.setMaximumHeight(100)
        self.networkLogEdit.setReadOnly(True)
        layout.addWidget(self.networkLogEdit)

        return card

    def _createAPITestCard(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(14)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FIF.CODE, "API 测试", card))

        poetryRow = QHBoxLayout()
        poetryRow.addWidget(StrongBodyLabel("一言 API", card))
        poetryRow.addStretch()
        self.testPoetryButton = PrimaryPushButton(FIF.PLAY, "测试", card)
        self.testPoetryButton.setFixedWidth(100)
        self.testPoetryButton.clicked.connect(self._testPoetryAPI)
        poetryRow.addWidget(self.testPoetryButton)
        layout.addLayout(poetryRow)
        self.poetryResultLabel = BodyLabel("结果：-", card)
        self.poetryResultLabel.setWordWrap(True)
        layout.addWidget(self.poetryResultLabel)

        weatherRow = QHBoxLayout()
        weatherRow.addWidget(StrongBodyLabel("天气 API", card))
        weatherRow.addStretch()
        self.testWeatherButton = PrimaryPushButton(FIF.PLAY, "测试", card)
        self.testWeatherButton.setFixedWidth(100)
        self.testWeatherButton.clicked.connect(self._testWeatherAPI)
        weatherRow.addWidget(self.testWeatherButton)
        layout.addLayout(weatherRow)
        self.weatherResultLabel = BodyLabel("结果：-", card)
        self.weatherResultLabel.setWordWrap(True)
        layout.addWidget(self.weatherResultLabel)

        line = QLabel(card)
        line.setObjectName("debugSeparator")
        line.setFixedHeight(1)
        layout.addWidget(line)

        self.rawDataEdit = QTextEdit(card)
        self.rawDataEdit.setPlaceholderText("API 原始响应数据...")
        self.rawDataEdit.setMaximumHeight(120)
        layout.addWidget(self.rawDataEdit)

        return card

    def _createWeatherDebugCard(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(12)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FIF.CLOUD, "天气模拟", card))

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
        selectRow.addWidget(BodyLabel("选择天气:", card))
        self.weatherCodeCombo = ComboBox(card)
        for code, name in sorted(self.weatherCodeMap.items()):
            self.weatherCodeCombo.addItem(f"{code} - {name}", userData=code)
        self.weatherCodeCombo.currentIndexChanged.connect(self._onWeatherCodeChanged)
        self.weatherCodeCombo.setMinimumWidth(280)
        selectRow.addWidget(self.weatherCodeCombo)
        selectRow.addStretch()
        layout.addLayout(selectRow)

        previewRow = QHBoxLayout()
        previewRow.addWidget(BodyLabel("图标预览:", card))
        self.weatherIconPreviewLabel = ImageLabel(card)
        self.weatherIconPreviewLabel.setFixedSize(48, 48)
        previewRow.addWidget(self.weatherIconPreviewLabel)
        self.weatherNamePreviewLabel = BodyLabel("-", card)
        previewRow.addWidget(self.weatherNamePreviewLabel)
        previewRow.addStretch()
        layout.addLayout(previewRow)

        tempRow = QHBoxLayout()
        tempRow.addWidget(BodyLabel("温度显示:", card))
        self.weatherTempInput = LineEdit(card)
        self.weatherTempInput.setPlaceholderText("例如: 25°C")
        self.weatherTempInput.setMaximumWidth(150)
        tempRow.addWidget(self.weatherTempInput)
        tempRow.addStretch()
        layout.addLayout(tempRow)

        buttonRow = QHBoxLayout()
        self.applyWeatherButton = PrimaryPushButton(FIF.PLAY, "应用到主界面", card)
        self.applyWeatherButton.clicked.connect(self._applyWeatherToMain)
        buttonRow.addWidget(self.applyWeatherButton)
        self.resetWeatherButton = PushButton("重置", card)
        self.resetWeatherButton.clicked.connect(self._resetWeatherDebug)
        buttonRow.addWidget(self.resetWeatherButton)
        buttonRow.addStretch()
        layout.addLayout(buttonRow)

        line = QLabel(card)
        line.setObjectName("debugSeparator")
        line.setFixedHeight(1)
        layout.addWidget(line)

        iconGridLabel = BodyLabel("图标列表 (点击快速选择):", card)
        layout.addWidget(iconGridLabel)

        self.weatherIconGrid = QWidget(card)
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
            item = self._createWeatherIconItem(code, name, icon_map.get(code, "0.svg"), card)
            self.weatherIconGridLayout.addWidget(item, row, col)
            col += 1
            if col >= 6:
                col = 0
                row += 1

        gridScroll = ScrollArea(card)
        gridScroll.setWidget(self.weatherIconGrid)
        gridScroll.setWidgetResizable(True)
        gridScroll.setMinimumHeight(200)
        gridScroll.setMaximumHeight(280)
        layout.addWidget(gridScroll)

        self.weatherCodeCombo.setCurrentIndex(0)
        self._onWeatherCodeChanged(0)

        return card

    def _createWeatherIconItem(self, code, name, icon_file, parent_card):
        item = CardWidget()
        item.setFixedSize(115, 80)
        item.setCursor(Qt.CursorShape.PointingHandCursor)
        item._weatherCode = code
        layout = QVBoxLayout(item)
        layout.setContentsMargins(4, 6, 4, 4)
        layout.setSpacing(2)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        imgLabel = ImageLabel(parent_card)
        imgLabel.setFixedSize(32, 32)
        icon_path = get_resPath(os.path.join("resource", "icons", "weather", icon_file))
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path).scaled(28, 28, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)
            imgLabel.setImage(pixmap)
        else:
            imgLabel.setImage(QPixmap(28, 28))
        layout.addWidget(imgLabel, alignment=Qt.AlignmentFlag.AlignHCenter)
        codeLabel = BodyLabel(f"{code}", parent_card)
        codeLabel.setStyleSheet("font-size: 11px; font-weight: bold; font-family: 'HarmonyOS Sans', 'Microsoft YaHei', 'SimHei', sans-serif;")
        codeLabel.setAlignment(Qt.AlignmentFlag.AlignHCenter)
        layout.addWidget(codeLabel)
        nameLabel = BodyLabel(name[:5], parent_card)
        nameLabel.setStyleSheet("font-size: 10px; font-family: 'HarmonyOS Sans', 'Microsoft YaHei', 'SimHei', sans-serif;")
        nameLabel.setAlignment(Qt.AlignmentFlag.AlignHCenter)
        layout.addWidget(nameLabel)
        item.mousePressEvent = lambda e, c=code: self._onGridItemClick(c)
        return item

    def _onGridItemClick(self, code):
        for i in range(self.weatherCodeCombo.count()):
            if self.weatherCodeCombo.itemData(i) == code:
                self.weatherCodeCombo.setCurrentIndex(i)
                break

    def _onWeatherCodeChanged(self, index):
        code = self.weatherCodeCombo.currentData()
        name = self.weatherCodeMap.get(code, "未知")
        self.weatherNamePreviewLabel.setText(name)
        self._previewWeatherIcon(code)

    def _previewWeatherIcon(self, code):
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
            pixmap = QPixmap(icon_path).scaled(40, 40, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)
            self.weatherIconPreviewLabel.setImage(pixmap)

    def _applyWeatherToMain(self):
        code = self.weatherCodeCombo.currentData()
        if code is None: return
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
        InfoBar.success(title="天气模拟", content=f"已应用天气代码 {code} ({self.weatherCodeMap.get(code, '')}) 到主界面", parent=self, duration=2500)

    def _resetWeatherDebug(self):
        self.weatherCodeCombo.setCurrentIndex(0)
        self._onWeatherCodeChanged(0)
        self.weatherTempInput.clear()
        if hasattr(self, '_savedWeatherCode'): del self._savedWeatherCode
        if hasattr(self, '_savedWeatherTemp'): del self._savedWeatherTemp
        mw = self.mainWindow
        mw._MainWindow__updateWeather()

    def _createElementCheckCard(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(12)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FIF.SEARCH, "元素检查", card))

        enableRow = QHBoxLayout()
        enableRow.addWidget(BodyLabel("启用悬停检查:", card))
        self.elementCheckToggle = ToggleButton("启用", card)
        self.elementCheckToggle.toggled.connect(self._toggleElementCheck)
        enableRow.addWidget(self.elementCheckToggle)
        enableRow.addStretch()
        layout.addLayout(enableRow)

        self.elementInfoEdit = QTextEdit(card)
        self.elementInfoEdit.setPlaceholderText("鼠标悬停在组件上查看信息（对象名 / 类型 / 位置 / 大小）")
        self.elementInfoEdit.setMaximumHeight(130)
        self.elementInfoEdit.setReadOnly(True)
        layout.addWidget(self.elementInfoEdit)

        return card

    def _createBatchWallpaperCard(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(12)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FIF.DOWNLOAD, "获取壁纸", card))

        row = QHBoxLayout()
        row.addWidget(BodyLabel("获取数量:", card))
        self.batchWallpaperSpin = SpinBox(card)
        self.batchWallpaperSpin.setRange(1, 100)
        self.batchWallpaperSpin.setValue(5)
        self.batchWallpaperSpin.setFixedWidth(120)
        row.addWidget(self.batchWallpaperSpin)
        row.addSpacing(16)
        self.batchWallpaperBtn = PrimaryPushButton(FIF.DOWNLOAD, "开始获取", card)
        self.batchWallpaperBtn.setFixedSize(110, 32)
        self.batchWallpaperBtn.clicked.connect(self._batchGetWallpaper)
        row.addWidget(self.batchWallpaperBtn)
        self.batchWallpaperStopBtn = PushButton("停止", card)
        self.batchWallpaperStopBtn.setFixedSize(70, 32)
        self.batchWallpaperStopBtn.clicked.connect(self._stopBatchWallpaper)
        self.batchWallpaperStopBtn.setEnabled(False)
        row.addWidget(self.batchWallpaperStopBtn)
        row.addStretch(1)
        layout.addLayout(row)

        self.batchWallpaperProgress = QProgressBar(card)
        self.batchWallpaperProgress.setRange(0, 100)
        self.batchWallpaperProgress.setValue(0)
        self.batchWallpaperProgress.setFixedHeight(6)
        self.batchWallpaperProgress.setTextVisible(False)
        layout.addWidget(self.batchWallpaperProgress)

        self.batchWallpaperLog = QTextEdit(card)
        self.batchWallpaperLog.setPlaceholderText("获取日志")
        self.batchWallpaperLog.setMaximumHeight(120)
        self.batchWallpaperLog.setReadOnly(True)
        layout.addWidget(self.batchWallpaperLog)

        self._batchRunning = False
        return card

    def _batchGetWallpaper(self):
        if self._batchRunning: return
        count = self.batchWallpaperSpin.value()
        self._batchRunning = True
        self._batchSuccess = 0
        self._batchFail = 0
        self.batchWallpaperBtn.setEnabled(False)
        self.batchWallpaperStopBtn.setEnabled(True)
        self.batchWallpaperProgress.setValue(0)
        self.batchWallpaperLog.clear()
        self.batchWallpaperLog.append(f"获取 {count} 张壁纸")
        self._batchWallpaperCount = count
        self._batchWallpaperIndex = 0
        QTimer.singleShot(100, self._batchGetNextWallpaper)

    def _batchGetNextWallpaper(self):
        if not self._batchRunning or self._batchWallpaperIndex >= self._batchWallpaperCount:
            self._finishBatchWallpaper()
            return
        idx = self._batchWallpaperIndex + 1
        total = self._batchWallpaperCount
        self.batchWallpaperLog.append(f"[{idx}/{total}] 正在获取")
        mw = self.mainWindow
        try:
            wallpaper = mw.wallpaper
            url, source = wallpaper._getApiUrl()
            response = requests.get(url, stream=True, timeout=10)
            if response.status_code == 200:
                wallpaper_dir = os.path.join(BASE_DIR, 'wallpaper')
                if not os.path.exists(wallpaper_dir): os.makedirs(wallpaper_dir)
                current_date = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
                wallpaper_path = os.path.join(wallpaper_dir, f'wallpaper_{current_date}.jpg')
                with open(wallpaper_path, 'wb') as f: f.write(response.content)
                wallpaper.current_pixmap = QPixmap(wallpaper_path)
                wallpaper.current_wallpaper_path = wallpaper_path
                wallpaper.current_wallpaper_source = source
                if not wallpaper.current_pixmap.isNull():
                    wallpaper._updateBackground()
                    wallpaper._updateMainWindowBackground()
                    wallpaper.historyManager.add(wallpaper_path, source, url)
                    wallpaper.historyWidget.refresh()
                wallpaper.infoCard.updateInfo(wallpaper_path, source)
                self._batchSuccess += 1
                self.batchWallpaperLog.append(f"[{idx}/{total}] ✓ 成功 - {source}")
            else:
                self._batchFail += 1
                self.batchWallpaperLog.append(f"[{idx}/{total}] ✗ 失败 - HTTP {response.status_code}")
        except Exception as e:
            self._batchFail += 1
            self.batchWallpaperLog.append(f"[{idx}/{total}] ✗ 错误 - {str(e)}")
        self._batchWallpaperIndex += 1
        self._updateBatchProgress()
        QTimer.singleShot(800, self._batchGetNextWallpaper)

    def _updateBatchProgress(self):
        total = self._batchWallpaperCount
        done = self._batchWallpaperIndex
        self.batchWallpaperProgress.setValue(int(done / total * 100))

    def _stopBatchWallpaper(self):
        self._batchRunning = False
        self.batchWallpaperLog.append("已停止")
        self._finishBatchWallpaper()

    def _finishBatchWallpaper(self):
        self._batchRunning = False
        self.batchWallpaperBtn.setEnabled(True)
        self.batchWallpaperStopBtn.setEnabled(False)
        self.batchWallpaperProgress.setValue(100)
        s = getattr(self, '_batchSuccess', 0)
        f = getattr(self, '_batchFail', 0)
        self.batchWallpaperLog.append(f"成功 {s} 张，失败 {f} 张")

    def _reloadTheme(self):
        try:
            setTheme(cfg.themeMode.value)
            self._loadStyleSheet()
            InfoBar.success(title="主题刷新", content="样式表已重新加载", parent=self, duration=2000)
        except Exception as e:
            logger.error(f"刷新主题失败: {e}")
            InfoBar.error(title="主题刷新", content=f"失败: {e}", parent=self, duration=3000)

    def _restartApp(self):
        InfoBar.info(title="重启应用", content="正在重启...", parent=self, duration=2000)
        QTimer.singleShot(800, lambda: subprocess.Popen([sys.executable] + sys.argv))

    def _runNetworkDiag(self):
        target = self.networkTargetCombo.currentText().strip()
        if not target: return
        self.networkLogEdit.clear()
        self.networkLogEdit.append(f"[{time.strftime('%H:%M:%S')}] 开始诊断: {target}")
        QApplication.processEvents()

        try:
            start = time.time()
            ip = socket.gethostbyname(target)
            dns_ms = (time.time() - start) * 1000
            self.networkDnsLabel.setText(f"{ip} ({dns_ms:.0f}ms)")
            self.networkLogEdit.append(f"  DNS 解析: {ip} ({dns_ms:.0f}ms)")
        except Exception as e:
            self.networkDnsLabel.setText(f"失败 ({e})")
            self.networkLogEdit.append(f"  DNS 解析失败: {e}")

        QApplication.processEvents()

        try:
            start = time.time()
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(5)
            port = 443
            sock.connect((target, port))
            latency = (time.time() - start) * 1000
            sock.close()
            self.networkConnectLabel.setText("✓ 正常")
            self.networkLatencyLabel.setText(f"{latency:.0f} ms")
            self.networkLogEdit.append(f"  连通性: OK (端口 {port}, {latency:.0f}ms)")
        except socket.timeout:
            self.networkConnectLabel.setText("✗ 超时")
            self.networkLatencyLabel.setText(">5000ms")
            self.networkLogEdit.append(f"  连通性: 超时 (>5s)")
        except Exception as e:
            self.networkConnectLabel.setText(f"✗ 失败")
            self.networkLatencyLabel.setText("-")
            self.networkLogEdit.append(f"  连通性失败: {e}")

        QApplication.processEvents()

        try:
            start = time.time()
            api_url = cfg.poetryApiUrl.value
            resp = requests.get(api_url, timeout=5)
            elapsed = (time.time() - start) * 1000
            if resp.status_code == 200:
                self.networkPoetryLabel.setText(f"✓ {elapsed:.0f}ms")
                self.networkLogEdit.append(f"  一言 API: OK ({elapsed:.0f}ms)")
            else:
                self.networkPoetryLabel.setText(f"HTTP {resp.status_code}")
                self.networkLogEdit.append(f"  一言 API: HTTP {resp.status_code}")
        except Exception as e:
            self.networkPoetryLabel.setText("失败")
            self.networkLogEdit.append(f"  一言 API: {e}")

        self.networkLogEdit.append("--- 诊断完成 ---")

    def _runNetworkDiagAll(self):
        combo = self.networkTargetCombo
        items = [combo.itemText(i) for i in range(combo.count())]
        self.networkLogEdit.clear()
        self.networkLogEdit.append(f"[{time.strftime('%H:%M:%S')}] 开始全部目标诊断...\n")
        original = combo.currentIndex()
        for idx, target in enumerate(items):
            combo.setCurrentIndex(idx)
            self.networkLogEdit.append(f"\n{'='*30} [{idx+1}/{len(items)}] {target} {'='*30}")
            self._runNetworkDiag()
        combo.setCurrentIndex(original)
        self.networkLogEdit.append(f"\n[{time.strftime('%H:%M:%S')}] 全部诊断完成")

    def _setupTimers(self):
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
        self.setStyleSheet(load_qss('developer_panel.qss'))

    def _updateTheme(self):
        self._loadStyleSheet()

    def eventFilter(self, obj, event):
        if not hasattr(self, 'elementCheckEnabled'): return super().eventFilter(obj, event)
        if obj == self.mainWindow and event.type() == QEvent.Type.Paint and hasattr(self, 'debugUpdateToggle') and self.debugUpdateToggle.isChecked():
            self.frameCount += 1
            currentTime = time.time()
            if currentTime - self.lastFpsTime >= 0.5:
                self.currentFps = self.frameCount / (currentTime - self.lastFpsTime)
                self.fpsLabel.setText(f"{self.currentFps:.1f}")
                self.frameCount = 0
                self.lastFpsTime = currentTime
        if not self.elementCheckEnabled:
            return super().eventFilter(obj, event)
        if event.type() == QEvent.Type.Enter:
            element_info = []
            element_info.append(f"对象名称：{obj.objectName()}")
            element_info.append(f"类    型：{obj.__class__.__name__}")
            element_info.append(f"可    见：{obj.isVisible()}")
            if isinstance(obj, QWidget): element_info.append(f"启    用：{obj.isEnabled()}")
            if hasattr(obj, 'geometry'):
                geom = obj.geometry()
                element_info.append(f"位    置：({geom.x()}, {geom.y()})")
                element_info.append(f"大    小：{geom.width()}x{geom.height()}")
            self.elementInfoEdit.setText("\n".join(element_info))
        return super().eventFilter(obj, event)

    def _updateFPS(self):
        if not self.debugUpdateToggle.isChecked(): return
        if self.mainWindow.isVisible():
            self.frameCount += 1
            currentTime = time.time()
            if currentTime - self.lastFpsTime >= 0.5:
                self.currentFps = self.frameCount / (currentTime - self.lastFpsTime)
                self.fpsLabel.setText(f"{self.currentFps:.1f}")
                self.frameCount = 0
                self.lastFpsTime = currentTime

    def _checkWindowChanges(self):
        if not self.debugUpdateToggle.isChecked(): return
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
        if not self.debugUpdateToggle.isChecked(): return
        try:
            mem_info = self.process.memory_info()
            mem_mb = mem_info.rss / 1024 / 1024
            self.memoryLabel.setText(f"{mem_mb:.1f} MB")
        except Exception:
            self.memoryLabel.setText("N/A")
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
        self.windowStateLabel.setText("可见" if self.mainWindow.isVisible() else "隐藏")

    def _updateResourceMonitor(self):
        try:
            wallpaper_dir = os.path.normpath(os.path.join(BASE_DIR, 'wallpaper'))
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
            else:
                self.wallpaperSizeLabel.setText("-")
                self.wallpaperCountLabel.setText("0")
        except Exception as e:
            logger.error(f"更新资源监控失败：{e}")

    def _updateWindowDebug(self):
        pass

    def _testPoetryAPI(self):
        start_time = time.time()
        try:
            api_url = cfg.poetryApiUrl.value
            response = requests.get(api_url, timeout=10)
            elapsed = (time.time() - start_time) * 1000
            self.poetryResultLabel.setText(f"✓ 成功 ({elapsed:.0f}ms): {response.text[:50]}")
            self.rawDataEdit.setText(response.text)
            InfoBar.success(title="API 测试", content="一言 API 测试成功", parent=self, duration=2000)
        except Exception as e:
            elapsed = (time.time() - start_time) * 1000
            self.poetryResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): {str(e)}")
            logger.error(f"一言 API 测试失败：{e}")
            InfoBar.error(title="API 测试", content=f"一言 API 测试失败：{str(e)}", parent=self, duration=3000)

    def _testWeatherAPI(self):
        start_time = time.time()
        try:
            city_name = cfg.city.value if hasattr(cfg, 'city') and cfg.city.value else "北京"
            city_db = RegionDatabase()
            city_code = city_db.get_code(city_name)
            if not city_code: city_code = "101010100"
            weather_service = WeatherService(city_code)
            weather_data = weather_service.get_weather()
            elapsed = (time.time() - start_time) * 1000
            if weather_data:
                self.weatherResultLabel.setText(f"✓ 成功 ({elapsed:.0f}ms): {weather_data['weather_text']} {weather_data['temperature']}")
                self.rawDataEdit.setText(f"温度：{weather_data['temperature']}\n天气：{weather_data['weather_text']}\n代码：{weather_data['weather_code']}\n图标：{weather_data['weather_icon']}")
                InfoBar.success(title="API 测试", content=f"天气 API 测试成功 - {weather_data['weather_text']} {weather_data['temperature']}", parent=self, duration=2000)
            else:
                self.weatherResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): 未获取到数据")
                self.rawDataEdit.setText("返回数据为空")
                InfoBar.warning(title="API 测试", content="未获取到天气数据", parent=self, duration=3000)
        except Exception as e:
            elapsed = (time.time() - start_time) * 1000
            self.weatherResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): {str(e)}")
            logger.error(f"天气 API 测试失败：{e}")
            InfoBar.error(title="API 测试", content=f"天气 API 测试失败：{str(e)}", parent=self, duration=3000)

    def _clearCache(self):
        try:
            wallpaper_dir = os.path.normpath(os.path.join(BASE_DIR, 'wallpaper'))
            if os.path.exists(wallpaper_dir):
                deleted_count = 0
                deleted_size = 0
                for root, dirs, files in os.walk(wallpaper_dir):
                    for f in files:
                        fp = os.path.join(root, f)
                        try:
                            deleted_size += os.path.getsize(fp)
                            os.remove(fp)
                            deleted_count += 1
                        except Exception as e:
                            logger.warning(f"删除壁纸文件失败：{fp}, {e}")
                self._updateResourceMonitor()
                InfoBar.success(title="清理完成", content=f"已清理 {deleted_count} 个文件，释放 {deleted_size / 1024:.1f} KB", parent=self, duration=3000)
            else:
                InfoBar.info(title="清理缓存", content="壁纸文件夹不存在", parent=self, duration=2000)
        except Exception as e:
            logger.error(f"清理缓存失败：{e}")
            InfoBar.error(title="清理失败", content=str(e), parent=self, duration=3000)

    def _clearLogs(self):
        try:
            log_dir = os.path.normpath(os.path.join(BASE_DIR, 'logs'))
            if os.path.exists(log_dir):
                for root, dirs, files in os.walk(log_dir):
                    for f in files:
                        if f.endswith('.log'):
                            fp = os.path.join(root, f)
                            try:
                                os.remove(fp)
                            except Exception:
                                pass
                InfoBar.success(title="清理日志", content="日志已清理", parent=self, duration=2000)
                self._updateResourceMonitor()
        except Exception as e:
            logger.error(f"清理日志失败：{e}")
            InfoBar.error(title="清理失败", content=str(e), parent=self, duration=3000)

    def _forceRepaint(self):
        try:
            self.mainWindow.update()
            self.mainWindow.repaint()
            InfoBar.success(title="重绘", content="界面已强制重绘", parent=self, duration=1500)
        except Exception as e:
            logger.error(f"强制重绘失败：{e}")
            InfoBar.error(title="重绘失败", content=str(e), parent=self, duration=3000)

    def _toggleElementCheck(self, enabled):
        self.elementCheckEnabled = enabled
        if enabled:
            QApplication.instance().installEventFilter(self)
            InfoBar.success(title="元素检查", content="鼠标悬停查看组件信息", parent=self, duration=3000)
        else:
            QApplication.instance().removeEventFilter(self)
            InfoBar.info(title="元素检查", content="已禁用", parent=self, duration=2000)

    def _refreshComponentTree(self):
        pass

    def _togglePopOut(self):
        if hasattr(self, '_popOutWindow') and self._popOutWindow is not None:
            self._restoreFromPopOut()
        else:
            self._popOut()

    def _saveWidgetRefs(self):
        self._savedWidgetRefs = {}
        for attr in list(vars(self)):
            obj = getattr(self, attr)
            if isinstance(obj, QWidget): self._savedWidgetRefs[attr] = obj

    def _restoreWidgetRefs(self):
        if not hasattr(self, '_savedWidgetRefs'): return
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
                    if panel: panel._restoreFromPopOut()
                    event.accept()

            self._popOutWindow = _PopOutWindow(self)
            self._popOutWindow.setObjectName('debugPanel')
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

            content_layout.addWidget(self._createSystemMonitorCard())
            content_layout.addWidget(self._createQuickActionsCard())
            content_layout.addWidget(self._createNetworkDiagCard())
            content_layout.addWidget(self._createAPITestCard())
            content_layout.addWidget(self._createWeatherDebugCard())
            content_layout.addWidget(self._createElementCheckCard())
            content_layout.addWidget(self._createBatchWallpaperCard())

            scroll = ScrollArea(self._popOutWindow)
            scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
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
        pop_win = getattr(self, '_popOutWindow', None)
        if pop_win is None: return
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
