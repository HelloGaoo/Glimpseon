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
from core.utils import tr, TranslatableWidget
from core.logger import logger
from services.weather import WeatherService, RegionDatabase

from .common import BaseScrollAreaInterface, show_text_file


class DebugPanel(BaseScrollAreaInterface, TranslatableWidget):

    def __init__(self, mainWindow):
        super().__init__(tr("debug.title"), parent=None)  # 调试
        self.mainWindow = mainWindow
        self.setObjectName('debug')

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
        self.setup_translatable_ui()

    def showEvent(self, event):
        super().showEvent(event)
        if not self._weatherGridCreated:
            self._weatherGridCreated = True
            self._populateWeatherIconGrid()

    def _initUI(self):
        scrollLayout = QVBoxLayout(self.scrollWidget)
        scrollLayout.setSpacing(15)
        scrollLayout.setContentsMargins(60, 10, 60, 20)
        scrollLayout.addWidget(self._createSystemMonitorCard())
        scrollLayout.addWidget(self._createQuickActionsCard())
        scrollLayout.addWidget(self._createNetworkDiagCard())
        scrollLayout.addWidget(self._createAPITestCard())
        self._weatherGridCreated = False
        scrollLayout.addWidget(self._createWeatherDebugCardShell())

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
        layout.addLayout(self._cardTitle(FIF.APPLICATION, tr("debug.system_monitor"), card))  # 系统监控

        grid = QGridLayout()
        grid.setSpacing(12)
        grid.setHorizontalSpacing(20)

        grid.addWidget(StrongBodyLabel(tr("debug.label_fps"), card), 0, 0)  # 帧率
        self.fpsLabel = BodyLabel("0", card)
        self.fpsLabel.setObjectName("debugValueLabel")
        grid.addWidget(self.fpsLabel, 0, 1)

        grid.addWidget(StrongBodyLabel(tr("debug.label_memory"), card), 0, 2)  # 内存
        self.memoryLabel = BodyLabel("0 MB", card)
        self.memoryLabel.setObjectName("debugValueLabel")
        grid.addWidget(self.memoryLabel, 0, 3)

        grid.addWidget(StrongBodyLabel(tr("debug.label_cpu"), card), 1, 0)  # CPU
        self.cpuLabel = BodyLabel("0%", card)
        self.cpuLabel.setObjectName("debugValueLabel")
        grid.addWidget(self.cpuLabel, 1, 1)

        grid.addWidget(StrongBodyLabel(tr("debug.label_window_state"), card), 1, 2)  # 窗口状态
        self.windowStateLabel = BodyLabel(tr("debug.status_normal"), card)  # 正常
        grid.addWidget(self.windowStateLabel, 1, 3)

        grid.addWidget(StrongBodyLabel(tr("debug.label_wallpaper_folder"), card), 2, 0)  # 壁纸文件夹
        self.wallpaperSizeLabel = StrongBodyLabel("-", card)
        grid.addWidget(self.wallpaperSizeLabel, 2, 1)

        grid.addWidget(StrongBodyLabel(tr("debug.label_wallpaper_count"), card), 2, 2)  # 壁纸数量
        self.wallpaperCountLabel = StrongBodyLabel("0", card)
        grid.addWidget(self.wallpaperCountLabel, 2, 3)

        layout.addLayout(grid)

        line = QLabel(card)
        line.setObjectName("debugSeparator")
        line.setFixedHeight(1)
        layout.addWidget(line)

        btnRow = QHBoxLayout()
        self.debugUpdateToggle = ToggleButton(tr("debug.btn_enable_monitoring"), card)  # 启用监控
        self.debugUpdateToggle.setChecked(True)
        self.debugUpdateToggle.setIcon(FIF.SYNC)
        btnRow.addWidget(self.debugUpdateToggle)
        self.popOutButton = PushButton(FIF.FULL_SCREEN, tr("debug.btn_popout"), card)  # 弹出面板
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
        layout.addLayout(self._cardTitle(FIF.MENU, tr("debug.quick_actions"), card))  # 快捷操作

        row1 = QHBoxLayout()
        self.reloadThemeBtn = PrimaryPushButton(FIF.PALETTE, tr("debug.btn_refresh_theme"), card)  # 刷新主题
        self.reloadThemeBtn.clicked.connect(self._reloadTheme)
        row1.addWidget(self.reloadThemeBtn)
        self.clearCacheBtn = PushButton(FIF.DELETE, tr("debug.btn_clear_cache"), card)  # 清除缓存
        self.clearCacheBtn.clicked.connect(self._clearCache)
        row1.addWidget(self.clearCacheBtn)
        self.clearLogsBtn = PushButton(FIF.BROOM, tr("debug.btn_clear_logs"), card)  # 清空日志
        self.clearLogsBtn.clicked.connect(self._clearLogs)
        row1.addWidget(self.clearLogsBtn)
        layout.addLayout(row1)

        row2 = QHBoxLayout()
        self.openLogDirBtn = PushButton(FIF.FOLDER, tr("debug.btn_open_log_dir"), card)  # 打开日志目录
        self.openLogDirBtn.clicked.connect(lambda: os.startfile(os.path.join(BASE_DIR, 'logs')))
        row2.addWidget(self.openLogDirBtn)
        self.openWallpaperDirBtn = PushButton(FIF.FOLDER_ADD, tr("debug.btn_open_wallpaper_dir"), card)  # 打开壁纸目录
        self.openWallpaperDirBtn.clicked.connect(lambda: os.startfile(os.path.join(BASE_DIR, 'wallpaper')))
        row2.addWidget(self.openWallpaperDirBtn)
        self.openConfigDirBtn = PushButton(FIF.SETTING, tr("debug.btn_open_config_dir"), card)  # 打开配置目录
        self.openConfigDirBtn.clicked.connect(lambda: os.startfile(os.path.join(BASE_DIR, 'config')))
        row2.addWidget(self.openConfigDirBtn)
        layout.addLayout(row2)

        row3 = QHBoxLayout()
        self.forceRepaintBtn = PushButton(FIF.SYNC, tr("debug.btn_force_repaint"), card)  # 强制重绘
        self.forceRepaintBtn.clicked.connect(self._forceRepaint)
        row3.addWidget(self.forceRepaintBtn)
        self.restartAppBtn = PushButton(FIF.UPDATE, tr("debug.btn_restart_app"), card)  # 重启应用
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
        layout.addLayout(self._cardTitle(FIF.GLOBE, tr("debug.title_network_diag"), card))  # 网络诊断

        targetRow = QHBoxLayout()
        targetRow.addWidget(BodyLabel(tr("debug.label_test_target") + ":", card))  # 测试目标
        self.networkTargetCombo = ComboBox(card)
        self.networkTargetCombo.addItems(["www.baidu.com", "www.qq.com", "www.aliyun.com", "www.bilibili.com"])
        self.networkTargetCombo.setMinimumWidth(200)
        targetRow.addWidget(self.networkTargetCombo)
        targetRow.addStretch()
        layout.addLayout(targetRow)

        resultGrid = QGridLayout()
        resultGrid.setSpacing(8)

        resultGrid.addWidget(BodyLabel(tr("debug.label_connectivity") + ":", card), 0, 0)  # 连通性
        self.networkConnectLabel = StrongBodyLabel("-", card)
        resultGrid.addWidget(self.networkConnectLabel, 0, 1)

        resultGrid.addWidget(BodyLabel(tr("debug.label_latency") + ":", card), 0, 2)  # 延迟
        self.networkLatencyLabel = StrongBodyLabel("-", card)
        resultGrid.addWidget(self.networkLatencyLabel, 0, 3)

        resultGrid.addWidget(BodyLabel(tr("debug.label_dns") + ":", card), 1, 0)  # DNS
        self.networkDnsLabel = StrongBodyLabel("-", card)
        resultGrid.addWidget(self.networkDnsLabel, 1, 1)

        resultGrid.addWidget(BodyLabel(tr("debug.label_poetry_api") + ":", card), 1, 2)  # 诗词API
        self.networkPoetryLabel = StrongBodyLabel("-", card)
        resultGrid.addWidget(self.networkPoetryLabel, 1, 3)

        layout.addLayout(resultGrid)

        btnRow = QHBoxLayout()
        self.networkTestBtn = PrimaryPushButton(FIF.PLAY, tr("debug.btn_start_diag"), card)  # 开始诊断
        self.networkTestBtn.clicked.connect(self._runNetworkDiag)
        btnRow.addWidget(self.networkTestBtn)
        self.networkTestAllBtn = PushButton(tr("debug.btn_test_all"), card)  # 测试全部
        self.networkTestAllBtn.clicked.connect(self._runNetworkDiagAll)
        btnRow.addWidget(self.networkTestAllBtn)
        btnRow.addStretch()
        layout.addLayout(btnRow)

        self.networkLogEdit = QTextEdit(card)
        self.networkLogEdit.setPlaceholderText(tr("debug.label_placeholder_log"))  # 诊断日志将显示在此处...
        self.networkLogEdit.setMaximumHeight(100)
        self.networkLogEdit.setReadOnly(True)
        layout.addWidget(self.networkLogEdit)

        return card

    def _createAPITestCard(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(14)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FIF.CODE, tr("debug.title_api_test"), card))  # API测试

        poetryRow = QHBoxLayout()
        poetryRow.addWidget(StrongBodyLabel(tr("debug.label_poetry_api"), card))  # 诗词API
        poetryRow.addStretch()
        self.testPoetryButton = PrimaryPushButton(FIF.PLAY, tr("debug.btn_test"), card)  # 测试
        self.testPoetryButton.setFixedHeight(32)
        self.testPoetryButton.clicked.connect(self._testPoetryAPI)
        poetryRow.addWidget(self.testPoetryButton)
        layout.addLayout(poetryRow)
        self.poetryResultLabel = BodyLabel(tr("debug.label_result") + "-", card)  # 结果
        self.poetryResultLabel.setWordWrap(True)
        layout.addWidget(self.poetryResultLabel)

        weatherRow = QHBoxLayout()
        weatherRow.addWidget(StrongBodyLabel(tr("debug.label_weather_api"), card))  # 天气API
        weatherRow.addStretch()
        self.testWeatherButton = PrimaryPushButton(FIF.PLAY, tr("debug.btn_test"), card)  # 测试
        self.testWeatherButton.setFixedHeight(32)
        self.testWeatherButton.clicked.connect(self._testWeatherAPI)
        weatherRow.addWidget(self.testWeatherButton)
        layout.addLayout(weatherRow)
        self.weatherResultLabel = BodyLabel(tr("debug.label_result") + "-", card)  # 结果
        self.weatherResultLabel.setWordWrap(True)
        layout.addWidget(self.weatherResultLabel)

        line = QLabel(card)
        line.setObjectName("debugSeparator")
        line.setFixedHeight(1)
        layout.addWidget(line)

        self.rawDataEdit = QTextEdit(card)
        self.rawDataEdit.setPlaceholderText(tr("debug.label_api_raw_data"))  # API原始数据将显示在此处...
        self.rawDataEdit.setMaximumHeight(120)
        layout.addWidget(self.rawDataEdit)

        return card

    def _createWeatherDebugCardShell(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(12)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FIF.CLOUD, tr("debug.title_weather_sim"), card))  # 天气模拟

        self.weatherCodeMap = {
            0: tr("weather.sunny"), 1: tr("weather.cloudy"), 2: tr("weather.overcast"), 3: tr("weather.shower"), 4: tr("weather.thundershower"),
            5: tr("weather.thundershower_with_hail"), 6: tr("weather.sleet"), 7: tr("weather.light_rain"), 8: tr("weather.moderate_rain"),
            9: tr("weather.heavy_rain"), 10: tr("weather.rainstorm"), 11: tr("weather.heavy_rainstorm"), 12: tr("weather.extreme_rainstorm"),
            13: tr("weather.snow_flurry"), 14: tr("weather.light_snow"), 15: tr("weather.moderate_snow"), 16: tr("weather.heavy_snow"), 17: tr("weather.snowstorm"),
            18: tr("weather.fog"), 19: tr("weather.freezing_rain"), 20: tr("weather.sandstorm"), 21: f"{tr('weather.light_rain')} - {tr('weather.moderate_rain')}",
            22: f"{tr('weather.moderate_rain')} - {tr('weather.heavy_rain')}", 23: f"{tr('weather.heavy_rain')} - {tr('weather.rainstorm')}", 24: f"{tr('weather.rainstorm')} - {tr('weather.heavy_rainstorm')}",
            25: f"{tr('weather.heavy_rainstorm')} - {tr('weather.extreme_rainstorm')}", 26: f"{tr('weather.light_snow')} - {tr('weather.moderate_snow')}", 27: f"{tr('weather.moderate_snow')} - {tr('weather.heavy_snow')}",
            28: f"{tr('weather.heavy_snow')} - {tr('weather.snowstorm')}", 29: tr("weather.dust"), 30: tr("weather.sand"), 31: tr("weather.strong_sandstorm"),
            32: tr("weather.squall"), 33: tr("weather.tornado"), 34: tr("weather.weak_blowing_snow"), 35: tr("weather.light_fog"),
            50: f"{tr('weather.sunny')}({tr('weather.night')})", 51: f"{tr('weather.cloudy')}({tr('weather.night')})", 52: f"{tr('weather.overcast')}({tr('weather.night')})", 53: tr("weather.haze"),
            54: f"{tr('weather.light_rain')}({tr('weather.night')})", 55: f"{tr('weather.moderate_rain')}({tr('weather.night')})", 56: f"{tr('weather.heavy_rain')}({tr('weather.night')})", 57: f"{tr('weather.rainstorm')}({tr('weather.night')})",
            58: f"{tr('weather.thundershower')}({tr('weather.night')})", 59: f"{tr('weather.hail')}({tr('weather.night')})", 60: f"{tr('weather.light_snow')}({tr('weather.night')})", 61: f"{tr('weather.moderate_snow')}({tr('weather.night')})",
            62: f"{tr('weather.heavy_snow')}({tr('weather.night')})", 63: f"{tr('weather.fog')}({tr('weather.night')})", 64: f"{tr('weather.haze')}({tr('weather.night')})", 65: f"{tr('weather.sand_dust')}({tr('weather.night')})",
            66: f"{tr('weather.strong_wind')}({tr('weather.night')})", 67: f"{tr('weather.typhoon')}({tr('weather.night')})", 68: f"{tr('weather.rainstorm')}({tr('weather.night')})", 69: f"{tr('weather.snowstorm')}({tr('weather.night')})",
            70: f"{tr('weather.sleet')}({tr('weather.night')})", 71: f"{tr('weather.freezing_rain')}({tr('weather.night')})", 72: f"{tr('weather.rime')}({tr('weather.night')})", 73: f"{tr('weather.frost')}({tr('weather.night')})",
            74: f"{tr('weather.sandstorm')}({tr('weather.night')})", 75: f"{tr('weather.sand')}({tr('weather.night')})", 76: f"{tr('weather.dust')}({tr('weather.night')})", 77: f"{tr('weather.strong_sandstorm')}({tr('weather.night')})",
            99: tr("weather.unknown"),
        }

        selectRow = QHBoxLayout()
        selectRow.addWidget(BodyLabel(tr("debug.label_select_weather") + ":", card))  # 选择天气
        self.weatherCodeCombo = ComboBox(card)
        for code, name in sorted(self.weatherCodeMap.items()):
            self.weatherCodeCombo.addItem(f"{code} - {name}", userData=code)
        self.weatherCodeCombo.currentIndexChanged.connect(self._onWeatherCodeChanged)
        self.weatherCodeCombo.setMinimumWidth(280)
        selectRow.addWidget(self.weatherCodeCombo)
        selectRow.addStretch()
        layout.addLayout(selectRow)

        previewRow = QHBoxLayout()
        previewRow.addWidget(BodyLabel(tr("debug.label_icon_preview") + ":", card))  # 图标预览
        self.weatherIconPreviewLabel = ImageLabel(card)
        self.weatherIconPreviewLabel.setFixedSize(48, 48)
        previewRow.addWidget(self.weatherIconPreviewLabel)
        self.weatherNamePreviewLabel = BodyLabel("-", card)
        previewRow.addWidget(self.weatherNamePreviewLabel)
        previewRow.addStretch()
        layout.addLayout(previewRow)

        tempRow = QHBoxLayout()
        tempRow.addWidget(BodyLabel(tr("debug.label_temp_display") + ":", card))  # 温度显示
        self.weatherTempInput = LineEdit(card)
        self.weatherTempInput.setPlaceholderText(tr("debug.placeholder_temp_example"))  # 例如: 25
        self.weatherTempInput.setMaximumWidth(150)
        tempRow.addWidget(self.weatherTempInput)
        tempRow.addStretch()
        layout.addLayout(tempRow)

        buttonRow = QHBoxLayout()
        self.applyWeatherButton = PrimaryPushButton(FIF.PLAY, tr("debug.btn_apply_weather"), card)  # 应用天气
        self.applyWeatherButton.clicked.connect(self._applyWeatherToMain)
        buttonRow.addWidget(self.applyWeatherButton)
        self.resetWeatherButton = PushButton(tr("debug.btn_reset"), card)  # 重置
        self.resetWeatherButton.clicked.connect(self._resetWeatherDebug)
        buttonRow.addWidget(self.resetWeatherButton)
        buttonRow.addStretch()
        layout.addLayout(buttonRow)

        line = QLabel(card)
        line.setObjectName("debugSeparator")
        line.setFixedHeight(1)
        layout.addWidget(line)

        iconGridLabel = BodyLabel(tr("debug.label_icon_list") + ":", card)  # 图标列表
        layout.addWidget(iconGridLabel)

        self.weatherIconGrid = QWidget(card)
        self.weatherIconGridLayout = QGridLayout(self.weatherIconGrid)
        self.weatherIconGridLayout.setSpacing(8)
        self.weatherIconGrid.setObjectName("weatherIconGrid")

        self._weatherGridScroll = ScrollArea(card)
        self._weatherGridScroll.setWidget(self.weatherIconGrid)
        self._weatherGridScroll.setWidgetResizable(True)
        self._weatherGridScroll.setMinimumHeight(200)
        self._weatherGridScroll.setMaximumHeight(280)
        layout.addWidget(self._weatherGridScroll)

        self.weatherCodeCombo.setCurrentIndex(0)
        self._onWeatherCodeChanged(0)

        self._weatherDebugCard = card
        return card

    def _populateWeatherIconGrid(self):
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
        card = self._weatherDebugCard
        col = 0
        row = 0
        for code, name in sorted(self.weatherCodeMap.items()):
            item = self._createWeatherIconItem(code, name, icon_map.get(code, "0.svg"), card)
            self.weatherIconGridLayout.addWidget(item, row, col)
            col += 1
            if col >= 6:
                col = 0
                row += 1

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
            pixmap = QPixmap(icon_path).scaled(28, 28, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.FastTransformation)
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
        name = self.weatherCodeMap.get(code, tr("weather.unknown"))  # 未知
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
            pixmap = QPixmap(icon_path).scaled(40, 40, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.FastTransformation)
            self.weatherIconPreviewLabel.setImage(pixmap)

    def _applyWeatherToMain(self):
        code = self.weatherCodeCombo.currentData()
        if code is None: return
        mw = self.mainWindow
        home = mw.homeInterface
        self._savedWeatherCode = getattr(home, 'current_weather_code', None)
        self._savedWeatherTemp = home.weatherTempLabel.text() if hasattr(home, 'weatherTempLabel') else ""
        home.current_weather_code = code
        temp_text = self.weatherTempInput.text().strip()
        if temp_text:
            home.weatherTempLabel.setText(temp_text)
        elif hasattr(home, 'weatherTempLabel'):
            name = self.weatherCodeMap.get(code, "")
            home.weatherTempLabel.setText(f"模拟: {name}")
        home._updateWeatherIcon()
        InfoBar.success(title=tr("debug.title_weather_sim"), content=tr("debug.weather_sim_applied").format(code=code, name=self.weatherCodeMap.get(code, '')), parent=self, duration=2500)

    def _resetWeatherDebug(self):
        self.weatherCodeCombo.setCurrentIndex(0)
        self._onWeatherCodeChanged(0)
        self.weatherTempInput.clear()
        if hasattr(self, '_savedWeatherCode'): del self._savedWeatherCode
        if hasattr(self, '_savedWeatherTemp'): del self._savedWeatherTemp
        mw = self.mainWindow
        mw.homeInterface._updateWeather()

    def _createElementCheckCard(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(12)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FIF.SEARCH, tr("debug.title_element_check"), card))  # 元素检查

        enableRow = QHBoxLayout()
        enableRow.addWidget(BodyLabel(tr("debug.label_enable_hover") + ":", card))  # 启用悬停检查
        self.elementCheckToggle = ToggleButton(tr("debug.btn_enable"), card)  # 启用
        self.elementCheckToggle.toggled.connect(self._toggleElementCheck)
        enableRow.addWidget(self.elementCheckToggle)
        enableRow.addStretch()
        layout.addLayout(enableRow)

        self.elementInfoEdit = QTextEdit(card)
        self.elementInfoEdit.setPlaceholderText(tr("debug.placeholder_element_info"))  # 悬停在界面元素上查看信息...
        self.elementInfoEdit.setMaximumHeight(130)
        self.elementInfoEdit.setReadOnly(True)
        layout.addWidget(self.elementInfoEdit)

        return card

    def _createBatchWallpaperCard(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(12)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FIF.DOWNLOAD, tr("debug.title_batch_wallpaper"), card))  # 批量壁纸获取

        row = QHBoxLayout()
        row.addWidget(BodyLabel(tr("debug.label_fetch_count") + ":", card))  # 获取数量
        self.batchWallpaperSpin = SpinBox(card)
        self.batchWallpaperSpin.setRange(1, 100)
        self.batchWallpaperSpin.setValue(5)
        self.batchWallpaperSpin.setFixedWidth(120)
        row.addWidget(self.batchWallpaperSpin)
        row.addSpacing(16)
        self.batchWallpaperBtn = PrimaryPushButton(FIF.DOWNLOAD, tr("debug.btn_start_fetch"), card)  # 开始获取
        self.batchWallpaperBtn.setFixedHeight(32)
        self.batchWallpaperBtn.clicked.connect(self._batchGetWallpaper)
        row.addWidget(self.batchWallpaperBtn)
        self.batchWallpaperStopBtn = PushButton(tr("debug.btn_stop"), card)  # 停止
        self.batchWallpaperStopBtn.setFixedHeight(32)
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
        self.batchWallpaperLog.setPlaceholderText(tr("debug.placeholder_fetch_log"))  # 获取日志将显示在此处...
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
                    if hasattr(wallpaper, 'historyWidget'):
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
        self.batchWallpaperLog.append(tr("debug.stopped"))  # 已停止
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
            InfoBar.success(title=tr("debug.theme_refresh"), content=tr("debug.stylesheet_reloaded"), parent=self, duration=2000)
        except Exception as e:
            logger.error(f"刷新主题失败: {e}")
            InfoBar.error(title=tr("debug.theme_refresh"), content=tr("debug.refresh_failed").format(error=e), parent=self, duration=3000)

    def _restartApp(self):
        InfoBar.info(title=tr("debug.btn_restart_app"), content=tr("debug.restarting"), parent=self, duration=2000)
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
            self.networkPoetryLabel.setText(tr("debug.status_failed"))  # 失败
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
        self.setStyleSheet(load_qss('debug.qss'))

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
        self.windowStateLabel.setText(tr("debug.status_visible") if self.mainWindow.isVisible() else tr("debug.status_hidden"))  # 可见 / 隐藏

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
            InfoBar.success(title=tr("debug.api_test"), content=tr("debug.poetry_api_success"), parent=self, duration=2000)
        except Exception as e:
            elapsed = (time.time() - start_time) * 1000
            self.poetryResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): {str(e)}")
            logger.error(f"一言 API 测试失败：{e}")
            InfoBar.error(title=tr("debug.api_test"), content=tr("debug.poetry_api_failed").format(error=str(e)), parent=self, duration=3000)

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
                InfoBar.success(title=tr("debug.api_test"), content=tr("debug.weather_api_success").format(weather=weather_data['weather_text'], temp=weather_data['temperature']), parent=self, duration=2000)
            else:
                self.weatherResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): 未获取到数据")
                self.rawDataEdit.setText(tr("debug.status_empty_data"))  # 暂无数据
                InfoBar.warning(title=tr("debug.api_test"), content=tr("debug.weather_no_data"), parent=self, duration=3000)
        except Exception as e:
            elapsed = (time.time() - start_time) * 1000
            self.weatherResultLabel.setText(f"✗ 失败 ({elapsed:.0f}ms): {str(e)}")
            logger.error(f"天气 API 测试失败：{e}")
            InfoBar.error(title=tr("debug.api_test"), content=tr("debug.weather_api_failed").format(error=str(e)), parent=self, duration=3000)

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
                InfoBar.success(title=tr("debug.clear_complete"), content=tr("debug.clear_cache_result").format(count=deleted_count, size=f"{deleted_size / 1024:.1f}"), parent=self, duration=3000)
            else:
                InfoBar.info(title=tr("debug.btn_clear_cache"), content=tr("debug.wallpaper_folder_not_exist"), parent=self, duration=2000)
        except Exception as e:
            logger.error(f"清理缓存失败：{e}")
            InfoBar.error(title=tr("debug.clear_failed"), content=str(e), parent=self, duration=3000)

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
                InfoBar.success(title=tr("debug.btn_clear_logs"), content=tr("debug.logs_cleared"), parent=self, duration=2000)
                self._updateResourceMonitor()
        except Exception as e:
            logger.error(f"清理日志失败：{e}")
            InfoBar.error(title=tr("debug.clear_failed"), content=str(e), parent=self, duration=3000)
    
    def _forceRepaint(self):
        try:
            self.mainWindow.update()
            self.mainWindow.repaint()
            InfoBar.success(title=tr("debug.repaint"), content=tr("debug.repaint_success"), parent=self, duration=1500)
        except Exception as e:
            logger.error(f"强制重绘失败：{e}")
            InfoBar.error(title=tr("debug.repaint_failed"), content=str(e), parent=self, duration=3000)

    def _toggleElementCheck(self, enabled):
        self.elementCheckEnabled = enabled
        if enabled:
            QApplication.instance().installEventFilter(self)
            InfoBar.success(title=tr("debug.title_element_check"), content=tr("debug.element_check_enabled"), parent=self, duration=3000)
        else:
            QApplication.instance().removeEventFilter(self)
            InfoBar.info(title=tr("debug.title_element_check"), content=tr("debug.element_check_disabled"), parent=self, duration=2000)

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
            self._popOutWindow.setObjectName('debug')
            self._popOutWindow.setWindowTitle("调试面板 - ClassLively")
            self._popOutWindow.setFixedSize(850, 750)

            qss = load_qss('debug.qss')
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
            self.popOutButton.setText(tr("debug.btn_restore_panel"))  # 还原面板

            mw = self.mainWindow
            if hasattr(mw, 'debugNavItem'): mw.debugNavItem.setVisible(False)
            if hasattr(mw, 'homeInterface'): mw.switchTo(mw.homeInterface)

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
        self.popOutButton.setText(tr("debug.btn_popout"))  # 弹出面板
        mw = self.mainWindow
        if hasattr(mw, 'debugNavItem') and cfg.debugMode.value: mw.debugNavItem.setVisible(True)
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
        self.popOutButton.setText(tr("debug.btn_popout"))
        mw = self.mainWindow
        if hasattr(mw, 'debugNavItem') and cfg.debugMode.value: mw.debugNavItem.setVisible(True)
        self._startTimers()
