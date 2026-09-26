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
调试面板
"""

import copy
import os
import socket
import time
import datetime
from concurrent.futures import ThreadPoolExecutor
import psutil
import requests
from PyQt6.QtCore import pyqtSignal, QEvent, QTimer, Qt
from PyQt6.QtGui import QPixmap
from PyQt6.QtWidgets import (
    QApplication,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QVBoxLayout,
    QWidget,
)
from qfluentwidgets import (
    BodyLabel,
    CardWidget,
    ComboBox,
    ImageLabel,
    InfoBar,
    LineEdit,
    PrimaryPushButton,
    ProgressBar,
    PushButton,
    ScrollArea,
    SpinBox,
    StrongBodyLabel,
    SubtitleLabel,
    TextEdit,
    ToggleButton,
)

from core.config import cfg
from core.constants import BASE_DIR, WALLPAPER_DIR, DATA_CONFIG, DATA_LOG, get_resPath, load_qss, RESOURCE_ICONS, clear_qss_cache
from core.utils import tr, TranslatableWidget, FUI, save_cache, get_cached_content, request_restart
from core.logger import logger
from services.weather import WeatherService

from .common import BaseScrollAreaInterface

FPS_TIMER_MS = 100
RESOURCE_TIMER_MS = 10000


class DebugPanel(BaseScrollAreaInterface, TranslatableWidget):

    _diag_result = pyqtSignal(object)

    def __init__(self, mainWindow):
        super().__init__(tr("debug.title"), parent=None)  # 调试
        self.mainWindow = mainWindow
        self.setObjectName('debug')
        self._diag_result.connect(self._handle_diag_result)

        self.frameCount = 0
        self.lastFpsTime = time.time()
        self.currentFps = 0
        self.process = psutil.Process(os.getpid())

        try:
            cpu_times = self.process.cpu_times()
            self.last_cpu_usage = cpu_times.user + cpu_times.system
            self.last_cpu_time = time.time()
        except Exception:
            self.last_cpu_usage = 0
            self.last_cpu_time = time.time()

        self.elementCheckEnabled = False
        self._popOutWindow = None
        self._savedWeather = None
        self._diagExecutor = None

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
        self._buildCardsInto(scrollLayout)
        self._loadStyleSheet()
        # 等主窗口建好再装事件过滤器
        QTimer.singleShot(1000, lambda: self.mainWindow and self.mainWindow.installEventFilter(self))

    def _buildCardsInto(self, layout):
        self._weatherGridCreated = False
        layout.addWidget(self._createSystemMonitorCard())
        layout.addWidget(self._createQuickActionsCard())
        layout.addWidget(self._createNetworkDiagCard())
        layout.addWidget(self._createAPITestCard())
        layout.addWidget(self._createWeatherDebugCardShell())
        layout.addWidget(self._createElementCheckCard())
        layout.addWidget(self._createBatchWallpaperCard())

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
        layout.addLayout(self._cardTitle(FUI.APPLICATION, tr("debug.system_monitor"), card))  # 系统监控

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
        self.debugUpdateToggle.setIcon(FUI.SYNC)
        btnRow.addWidget(self.debugUpdateToggle)
        self.popOutButton = PushButton(FUI.FULL_SCREEN, tr("debug.btn_popout"), card)  # 弹出面板
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
        layout.addLayout(self._cardTitle(FUI.MENU, tr("debug.quick_actions"), card))  # 快捷操作

        row1 = QHBoxLayout()
        self.reloadThemeBtn = PrimaryPushButton(FUI.PALETTE, tr("debug.btn_refresh_theme"), card)  # 刷新主题
        self.reloadThemeBtn.clicked.connect(self._reloadTheme)
        row1.addWidget(self.reloadThemeBtn)
        self.clearCacheBtn = PushButton(FUI.DELETE, tr("debug.btn_clear_cache"), card)  # 清除缓存
        self.clearCacheBtn.clicked.connect(self._clearCache)
        row1.addWidget(self.clearCacheBtn)
        self.clearLogsBtn = PushButton(FUI.BROOM, tr("debug.btn_clear_logs"), card)  # 清空日志
        self.clearLogsBtn.clicked.connect(self._clearLogs)
        row1.addWidget(self.clearLogsBtn)
        layout.addLayout(row1)

        row2 = QHBoxLayout()
        self.openLogDirBtn = PushButton(FUI.FOLDER, tr("debug.btn_open_log_dir"), card)  # 打开日志目录
        self.openLogDirBtn.clicked.connect(lambda: os.startfile(os.path.join(BASE_DIR, 'logs')))
        row2.addWidget(self.openLogDirBtn)
        self.openWallpaperDirBtn = PushButton(FUI.FOLDER_ADD, tr("debug.btn_open_wallpaper_dir"), card)  # 打开壁纸目录
        self.openWallpaperDirBtn.clicked.connect(lambda: os.startfile(WALLPAPER_DIR))  # os.path.join(BASE_DIR, 'wallpaper')
        row2.addWidget(self.openWallpaperDirBtn)
        self.openConfigDirBtn = PushButton(FUI.SETTING, tr("debug.btn_open_config_dir"), card)  # 打开配置目录
        self.openConfigDirBtn.clicked.connect(lambda: os.startfile(DATA_CONFIG))
        row2.addWidget(self.openConfigDirBtn)
        layout.addLayout(row2)

        row3 = QHBoxLayout()
        self.forceRepaintBtn = PushButton(FUI.SYNC, tr("debug.btn_force_repaint"), card)  # 强制重绘
        self.forceRepaintBtn.clicked.connect(self._forceRepaint)
        row3.addWidget(self.forceRepaintBtn)
        self.restartAppBtn = PushButton(FUI.UPDATE, tr("debug.btn_restart_app"), card)  # 重启应用
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
        layout.addLayout(self._cardTitle(FUI.GLOBE, tr("debug.title_network_diag"), card))  # 网络诊断

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
        self.networkTestBtn = PrimaryPushButton(FUI.PLAY, tr("debug.btn_start_diag"), card)  # 开始诊断
        self.networkTestBtn.clicked.connect(lambda: self._runNetworkDiag())
        btnRow.addWidget(self.networkTestBtn)
        self.networkTestAllBtn = PushButton(tr("debug.btn_test_all"), card)  # 测试全部
        self.networkTestAllBtn.clicked.connect(self._runNetworkDiagAll)
        btnRow.addWidget(self.networkTestAllBtn)
        btnRow.addStretch()
        layout.addLayout(btnRow)

        self.networkLogEdit = TextEdit(card)
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
        layout.addLayout(self._cardTitle(FUI.CODE, tr("debug.title_api_test"), card))  # API测试

        poetryRow = QHBoxLayout()
        poetryRow.addWidget(StrongBodyLabel(tr("debug.label_poetry_api"), card))  # 诗词API
        poetryRow.addStretch()
        self.testPoetryButton = PrimaryPushButton(FUI.PLAY, tr("debug.btn_test"), card)  # 测试
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
        self.testWeatherButton = PrimaryPushButton(FUI.PLAY, tr("debug.btn_test"), card)  # 测试
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

        self.rawDataEdit = TextEdit(card)
        self.rawDataEdit.setPlaceholderText(tr("debug.label_api_raw_data"))  # API原始数据将显示在此处...
        self.rawDataEdit.setMaximumHeight(120)
        layout.addWidget(self.rawDataEdit)

        return card

    def _createWeatherDebugCardShell(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(12)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FUI.CLOUD, tr("debug.title_weather_sim"), card))  # 天气模拟

        self.weatherCodeMap = WeatherService.build_weather_code_map(tr)

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
        self.applyWeatherButton = PrimaryPushButton(FUI.PLAY, tr("debug.btn_apply_weather"), card)  # 应用天气
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
        card = self._weatherDebugCard
        col = 0
        row = 0
        for code, name in sorted(self.weatherCodeMap.items()):
            item = self._createWeatherIconItem(code, name, WeatherService.ICON_MAP.get(code, "0.svg"), card)
            self.weatherIconGridLayout.addWidget(item, row, col)
            col += 1
            if col >= 6:
                col = 0
                row += 1

    def _createWeatherIconItem(self, code, name, icon_file, parent_card):
        item = CardWidget()
        item.setFixedSize(115, 80)
        item.setCursor(Qt.CursorShape.PointingHandCursor)
        layout = QVBoxLayout(item)
        layout.setContentsMargins(4, 6, 4, 4)
        layout.setSpacing(2)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        imgLabel = ImageLabel(parent_card)
        imgLabel.setFixedSize(32, 32)
        icon_path = get_resPath(os.path.join(RESOURCE_ICONS, "weather", icon_file))
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path).scaled(28, 28, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.FastTransformation)
            imgLabel.setImage(pixmap)
        else:
            imgLabel.setImage(QPixmap(28, 28))
        layout.addWidget(imgLabel, alignment=Qt.AlignmentFlag.AlignHCenter)
        codeLabel = BodyLabel(f"{code}", parent_card)
        codeLabel.setObjectName("weatherCodeLabel")
        codeLabel.setAlignment(Qt.AlignmentFlag.AlignHCenter)
        layout.addWidget(codeLabel)
        nameLabel = BodyLabel(name[:5], parent_card)
        nameLabel.setObjectName("weatherNameLabel")
        nameLabel.setAlignment(Qt.AlignmentFlag.AlignHCenter)
        layout.addWidget(nameLabel)
        item.mousePressEvent = lambda e, c=code: self._onGridItemClick(c)
        return item

    def _onGridItemClick(self, code):
        idx = self.weatherCodeCombo.findData(code)
        if idx >= 0:
            self.weatherCodeCombo.setCurrentIndex(idx)

    def _onWeatherCodeChanged(self, index):
        code = self.weatherCodeCombo.currentData()
        name = self.weatherCodeMap.get(code, tr("weather.unknown"))  # 未知
        self.weatherNamePreviewLabel.setText(name)
        self._previewWeatherIcon(code)

    def _previewWeatherIcon(self, code):
        icon_file = WeatherService.ICON_MAP.get(code, "0.svg")
        icon_path = get_resPath(os.path.join(RESOURCE_ICONS, "weather", icon_file))
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path).scaled(40, 40, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.FastTransformation)
            self.weatherIconPreviewLabel.setImage(pixmap)

    def _applyWeatherToMain(self):
        code = self.weatherCodeCombo.currentData()
        if code is None:
            return
        home = getattr(self.mainWindow, 'homeInterface', None)
        if home is None:
            return
        # 以缓存真值结构为底,只覆盖天气码与温度,再走既有广播链刷新各天气组件
        data = get_cached_content("weather", ignore_expiry=True)
        if data is None:
            InfoBar.warning(title=tr("debug.title_weather_sim"), content=tr("debug.status_empty_data"),
                            parent=self, duration=2500)
            return
        if self._savedWeather is None:
            self._savedWeather = copy.deepcopy(data)
        current = data.setdefault("current", {})
        current["weather"] = int(code)
        temp_text = self.weatherTempInput.text().strip()
        if temp_text:
            try:
                current.setdefault("temperature", {})["value"] = float(temp_text)
            except ValueError:
                pass
        save_cache("weather", data, cfg.weatherUpdateInterval.value)
        home.weather_updated.emit(data)
        InfoBar.success(title=tr("debug.title_weather_sim"),
                        content=tr("debug.weather_sim_applied").format(code=code, name=self.weatherCodeMap.get(code, '')),
                        parent=self, duration=2500)

    def _resetWeatherDebug(self):
        self.weatherCodeCombo.setCurrentIndex(0)
        self.weatherTempInput.clear()
        if self._savedWeather is None:
            return
        save_cache("weather", self._savedWeather, cfg.weatherUpdateInterval.value)
        home = getattr(self.mainWindow, 'homeInterface', None)
        if home is not None:
            home.weather_updated.emit(self._savedWeather)
        self._savedWeather = None

    def _createElementCheckCard(self):
        card = CardWidget()
        layout = QVBoxLayout(card)
        layout.setSpacing(12)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.addLayout(self._cardTitle(FUI.SEARCH, tr("debug.title_element_check"), card))  # 元素检查

        enableRow = QHBoxLayout()
        enableRow.addWidget(BodyLabel(tr("debug.label_enable_hover") + ":", card))  # 启用悬停检查
        self.elementCheckToggle = ToggleButton(tr("debug.btn_enable"), card)  # 启用
        self.elementCheckToggle.toggled.connect(self._toggleElementCheck)
        enableRow.addWidget(self.elementCheckToggle)
        enableRow.addStretch()
        layout.addLayout(enableRow)

        self.elementInfoEdit = TextEdit(card)
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
        layout.addLayout(self._cardTitle(FUI.DOWNLOAD, tr("debug.title_batch_wallpaper"), card))  # 批量壁纸获取

        row = QHBoxLayout()
        row.addWidget(BodyLabel(tr("debug.label_fetch_count") + ":", card))  # 获取数量
        self.batchWallpaperSpin = SpinBox(card)
        self.batchWallpaperSpin.setRange(1, 100)
        self.batchWallpaperSpin.setValue(5)
        self.batchWallpaperSpin.setFixedWidth(120)
        row.addWidget(self.batchWallpaperSpin)
        row.addSpacing(16)
        self.batchWallpaperBtn = PrimaryPushButton(FUI.DOWNLOAD, tr("debug.btn_start_fetch"), card)  # 开始获取
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

        self.batchWallpaperProgress = ProgressBar(card)
        self.batchWallpaperProgress.setRange(0, 100)
        self.batchWallpaperProgress.setValue(0)
        self.batchWallpaperProgress.setFixedHeight(6)
        self.batchWallpaperProgress.setTextVisible(False)
        layout.addWidget(self.batchWallpaperProgress)

        self.batchWallpaperLog = TextEdit(card)
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
        # 停止后残留的定时回调直接退出,汇总只由停止路径做一次
        if not self._batchRunning:
            return
        if self._batchWallpaperIndex >= self._batchWallpaperCount:
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
                wallpaper_dir = WALLPAPER_DIR
                if not os.path.exists(wallpaper_dir): os.makedirs(wallpaper_dir)
                current_date = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
                wallpaper_path = os.path.join(wallpaper_dir, f'wallpaper_{current_date}.jpg')
                with open(wallpaper_path, 'wb') as f: f.write(response.content)
                wallpaper.current_pixmap = QPixmap(wallpaper_path)
                wallpaper.current_wallpaper_path = wallpaper_path
                wallpaper.current_wallpaper_source = source
                if not wallpaper.current_pixmap.isNull():
                    wallpaper._applyEffects()
                    wallpaper._updateMainWindowBackground()
                    wallpaper.historyManager.add(wallpaper_path, source, url)
                wallpaper.infoCard.updateInfo(wallpaper_path, source)
                self._batchSuccess += 1
                self.batchWallpaperLog.append(f"[{idx}/{total}] 成功 - {source}")
            else:
                self._batchFail += 1
                self.batchWallpaperLog.append(f"[{idx}/{total}] 失败 - HTTP {response.status_code}")
        except Exception as e:
            self._batchFail += 1
            self.batchWallpaperLog.append(f"[{idx}/{total}] 错误 - {str(e)}")
        self._batchWallpaperIndex += 1
        self.batchWallpaperProgress.setValue(
            int(self._batchWallpaperIndex / self._batchWallpaperCount * 100))
        QTimer.singleShot(800, self._batchGetNextWallpaper)

    def _stopBatchWallpaper(self):
        if not self._batchRunning:
            return
        self._batchRunning = False
        self.batchWallpaperLog.append(tr("debug.stopped"))  # 已停止
        self.batchWallpaperLog.append(f"成功 {getattr(self, '_batchSuccess', 0)} 张，失败 {getattr(self, '_batchFail', 0)} 张")
        self.batchWallpaperBtn.setEnabled(True)
        self.batchWallpaperStopBtn.setEnabled(False)

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
            clear_qss_cache()
            from core.utils import apply_theme
            apply_theme(cfg.themeMode.value)
            self._loadStyleSheet()
            InfoBar.success(title=tr("debug.theme_refresh"), content=tr("debug.stylesheet_reloaded"), parent=self, duration=2000)
        except Exception as e:
            logger.error(f"刷新主题失败: {e}")
            InfoBar.error(title=tr("debug.theme_refresh"), content=tr("debug.refresh_failed").format(error=e), parent=self, duration=3000)

    def _restartApp(self):
        InfoBar.info(title=tr("debug.btn_restart_app"), content=tr("debug.restarting"), parent=self, duration=2000)
        QTimer.singleShot(800, request_restart)

    def _runNetworkDiag(self, reset_log: bool = True):
        target = self.networkTargetCombo.currentText().strip()
        if not target:
            return

        def _diag():
            results = []
            results.append(f"[{time.strftime('%H:%M:%S')}] 开始诊断: {target}")

            try:
                start = time.time()
                ip = socket.gethostbyname(target)
                dns_ms = (time.time() - start) * 1000
                results.append(("dns", ip, dns_ms))
                results.append(f"  DNS 解析: {ip} ({dns_ms:.0f}ms)")
            except Exception as e:
                results.append(("dns_fail", str(e)))
                results.append(f"  DNS 解析失败: {e}")

            try:
                start = time.time()
                sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                sock.settimeout(5)
                port = 443
                sock.connect((target, port))
                latency = (time.time() - start) * 1000
                sock.close()
                results.append(("conn_ok", latency))
                results.append(f"  连通性: OK (端口 {port}, {latency:.0f}ms)")
            except socket.timeout:
                results.append(("conn_timeout",))
                results.append(f"  连通性: 超时 (>5s)")
            except Exception as e:
                results.append(("conn_fail", str(e)))
                results.append(f"  连通性失败: {e}")

            try:
                start = time.time()
                api_url = cfg.poetryApiUrl.value
                resp = requests.get(api_url, timeout=5)
                elapsed = (time.time() - start) * 1000
                if resp.status_code == 200:
                    results.append(("poetry_ok", elapsed))
                    results.append(f"  一言 API: OK ({elapsed:.0f}ms)")
                else:
                    results.append(("poetry_http", resp.status_code))
                    results.append(f"  一言 API: HTTP {resp.status_code}")
            except Exception as e:
                results.append(("poetry_fail",))
                results.append(f"  一言 API: {e}")

            results.append("--- 诊断完成 ---")
            return results

        if reset_log:
            self.networkLogEdit.clear()
        self.networkLogEdit.append(f"[{time.strftime('%H:%M:%S')}] 正在诊断: {target}...")

        if self._diagExecutor is None:
            self._diagExecutor = ThreadPoolExecutor(max_workers=2)
        future = self._diagExecutor.submit(_diag)
        future.add_done_callback(lambda f: self._diag_result.emit(f))

    def _handle_diag_result(self, future):
        try:
            results = future.result()
            for r in results:
                if isinstance(r, tuple):
                    if r[0] == "dns":
                        self.networkDnsLabel.setText(f"{r[1]} ({r[2]:.0f}ms)")
                    elif r[0] == "dns_fail":
                        self.networkDnsLabel.setText(f"失败 ({r[1]})")
                    elif r[0] == "conn_ok":
                        self.networkConnectLabel.setText("正常")
                        self.networkLatencyLabel.setText(f"{r[1]:.0f} ms")
                    elif r[0] == "conn_timeout":
                        self.networkConnectLabel.setText("超时")
                        self.networkLatencyLabel.setText(">5000ms")
                    elif r[0] == "conn_fail":
                        self.networkConnectLabel.setText("失败")
                        self.networkLatencyLabel.setText("-")
                    elif r[0] == "poetry_ok":
                        self.networkPoetryLabel.setText(f"{r[1]:.0f}ms")
                    elif r[0] == "poetry_http":
                        self.networkPoetryLabel.setText(f"HTTP {r[1]}")
                    elif r[0] == "poetry_fail":
                        self.networkPoetryLabel.setText(tr("debug.status_failed"))
                elif isinstance(r, str):
                    self.networkLogEdit.append(r)
        except Exception as e:
            self.networkLogEdit.append(f"诊断异常: {e}")

    def _runNetworkDiagAll(self):
        combo = self.networkTargetCombo
        items = [combo.itemText(i) for i in range(combo.count())]
        self.networkLogEdit.clear()
        self.networkLogEdit.append(f"[{time.strftime('%H:%M:%S')}] 开始全部目标诊断...\n")
        original = combo.currentIndex()
        for idx, target in enumerate(items):
            combo.setCurrentIndex(idx)
            self.networkLogEdit.append(f"\n{'='*30} [{idx+1}/{len(items)}] {target} {'='*30}")
            self._runNetworkDiag(reset_log=False)
        combo.setCurrentIndex(original)

    def _setupTimers(self):
        self.fpsTimer = QTimer(self)
        self.fpsTimer.timeout.connect(self._updateDebugInfo)
        self.resourceTimer = QTimer(self)
        self.resourceTimer.timeout.connect(self._updateResourceMonitor)
        self._startTimers()

    def _loadStyleSheet(self):
        self.setStyleSheet(load_qss('debug.qss'))

    def _updateTheme(self):
        self._loadStyleSheet()

    def eventFilter(self, obj, event):
        # super().__init__ 期间就会派发事件,那时 mainWindow/elementCheckEnabled 还没赋值
        if not hasattr(self, 'elementCheckEnabled'):
            return super().eventFilter(obj, event)
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

    @staticmethod
    def _iter_wallpaper_files():
        """壁纸目录下全部文件路径"""
        d = os.path.normpath(WALLPAPER_DIR)
        if not os.path.exists(d):
            return
        for root, _dirs, files in os.walk(d):
            for f in files:
                yield os.path.join(root, f)

    def _updateResourceMonitor(self):
        try:
            if not os.path.exists(os.path.normpath(WALLPAPER_DIR)):
                self.wallpaperSizeLabel.setText("-")
                self.wallpaperCountLabel.setText("0")
                return
            total_size = 0
            file_count = 0
            for fp in self._iter_wallpaper_files():
                try:
                    total_size += os.path.getsize(fp)
                    file_count += 1
                except Exception:
                    pass
            self.wallpaperSizeLabel.setText(f"{total_size / 1024 / 1024:.1f} MB")
            self.wallpaperCountLabel.setText(str(file_count))
        except Exception as e:
            logger.error(f"更新资源监控失败：{e}")

    def _testPoetryAPI(self):
        start_time = time.time()
        try:
            api_url = cfg.poetryApiUrl.value
            response = requests.get(api_url, timeout=10)
            elapsed = (time.time() - start_time) * 1000
            self.poetryResultLabel.setText(f"成功 ({elapsed:.0f}ms): {response.text[:50]}")
            self.rawDataEdit.setText(response.text)
            InfoBar.success(title=tr("debug.api_test"), content=tr("debug.poetry_api_success"), parent=self, duration=2000)
        except Exception as e:
            elapsed = (time.time() - start_time) * 1000
            self.poetryResultLabel.setText(f"失败 ({elapsed:.0f}ms): {str(e)}")
            logger.error(f"一言 API 测试失败：{e}")
            InfoBar.error(title=tr("debug.api_test"), content=tr("debug.poetry_api_failed").format(error=str(e)), parent=self, duration=3000)

    def _testWeatherAPI(self):
        start_time = time.time()
        try:
            weather_data = WeatherService().fetch_all()
            elapsed = (time.time() - start_time) * 1000
            current = (weather_data or {}).get("current", {})
            if weather_data and current:
                temp_block = current.get("temperature", {})
                temp = temp_block.get("value", "--")
                unit = temp_block.get("unit", "℃")
                try:
                    code = int(current.get("weather", 0) or 0)
                except (TypeError, ValueError):
                    code = 0
                name = self.weatherCodeMap.get(code, "")
                hourly = weather_data.get("forecastHourly", {}).get("temperature", {}).get("value", [])
                daily = weather_data.get("forecastDaily", {}).get("temperature", {}).get("value", [])
                self.weatherResultLabel.setText(f"成功 ({elapsed:.0f}ms): {name} {temp}{unit}")
                self.rawDataEdit.setText(
                    f"温度：{temp}{unit}\n天气：{name}\n代码：{code}\n"
                    f"逐小时：{len(hourly)}条\n每日：{len(daily)}条")
                InfoBar.success(title=tr("debug.api_test"),
                                content=tr("debug.weather_api_success").format(weather=name, temp=f"{temp}{unit}"),
                                parent=self, duration=2000)
            else:
                self.weatherResultLabel.setText(f"失败 ({elapsed:.0f}ms): 未获取到数据")
                self.rawDataEdit.setText(tr("debug.status_empty_data"))  # 暂无数据
                InfoBar.warning(title=tr("debug.api_test"), content=tr("debug.weather_no_data"), parent=self, duration=3000)
        except Exception as e:
            elapsed = (time.time() - start_time) * 1000
            self.weatherResultLabel.setText(f"失败 ({elapsed:.0f}ms): {str(e)}")
            logger.error(f"天气 API 测试失败：{e}")
            InfoBar.error(title=tr("debug.api_test"), content=tr("debug.weather_api_failed").format(error=str(e)), parent=self, duration=3000)

    def _clearCache(self):
        try:
            if not os.path.exists(os.path.normpath(WALLPAPER_DIR)):
                InfoBar.info(title=tr("debug.btn_clear_cache"), content=tr("debug.wallpaper_folder_not_exist"), parent=self, duration=2000)
                return
            deleted_count = 0
            deleted_size = 0
            for fp in self._iter_wallpaper_files():
                try:
                    deleted_size += os.path.getsize(fp)
                    os.remove(fp)
                    deleted_count += 1
                except Exception as e:
                    logger.warning(f"删除壁纸文件失败：{fp}, {e}")
            self._updateResourceMonitor()
            InfoBar.success(title=tr("debug.clear_complete"), content=tr("debug.clear_cache_result").format(count=deleted_count, size=f"{deleted_size / 1024:.1f}"), parent=self, duration=3000)
        except Exception as e:
            logger.error(f"清理缓存失败：{e}")
            InfoBar.error(title=tr("debug.clear_failed"), content=str(e), parent=self, duration=3000)

    def _clearLogs(self):
        try:
            log_dir = os.path.normpath(DATA_LOG)
            if not os.path.exists(log_dir):
                return
            removed = 0
            failed = 0
            for root, _dirs, files in os.walk(log_dir):
                for f in files:
                    if not (f.endswith('.log') or f.endswith('.log.zip')):
                        continue
                    try:
                        os.remove(os.path.join(root, f))
                        removed += 1
                    except Exception:
                        # 正在写入的日志被 RotatingFileHandler 持有,Windows 下删不掉
                        failed += 1
            if failed:
                logger.warning(f"日志清理: 成功 {removed} 个, 失败 {failed} 个(正在写入)")
            InfoBar.success(title=tr("debug.btn_clear_logs"), content=tr("debug.logs_cleared"), parent=self, duration=2000)
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

    def _togglePopOut(self):
        if self._popOutWindow is not None:
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

    def _startTimers(self):
        self.fpsTimer.start(FPS_TIMER_MS)
        self.resourceTimer.start(RESOURCE_TIMER_MS)

    def _popOut(self):
        try:
            screen_obj = QApplication.primaryScreen()
            if screen_obj is None:
                return
            screen = screen_obj.availableGeometry()

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
            self._popOutWindow.setWindowTitle(tr("debug.panel_title"))
            self._popOutWindow.setFixedSize(850, 750)
            self._popOutWindow.setStyleSheet(load_qss('debug.qss'))

            outer_layout = QVBoxLayout(self._popOutWindow)
            outer_layout.setContentsMargins(0, 0, 0, 0)
            outer_layout.setSpacing(0)

            container = QWidget()
            container.setObjectName('scrollWidget')
            content_layout = QVBoxLayout(container)
            content_layout.setContentsMargins(36, 20, 36, 20)
            content_layout.setSpacing(15)
            self._buildCardsInto(content_layout)
            self._populateWeatherIconGrid()

            scroll = ScrollArea(self._popOutWindow)
            scroll.setObjectName("debugScroll")
            scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
            scroll.setWidgetResizable(True)
            scroll.setWidget(container)
            outer_layout.addWidget(scroll)

            self._popOutWindow.move((screen.width() - self._popOutWindow.width()) // 2,
                                    (screen.height() - self._popOutWindow.height()) // 2)
            self._popOutWindow.show()
            self.popOutButton.setText(tr("debug.btn_restore_panel"))  # 还原面板

            mw = self.mainWindow
            if hasattr(mw, 'debugNavItem'): mw.debugNavItem.setVisible(False)
            if hasattr(mw, 'homeInterface'): mw.switchTo(mw.homeInterface)
        except Exception as e:
            logger.error(f"弹出调试面板失败: {e}")
            self._safeCleanupPopOut()

    def _restoreFromPopOut(self):
        pop_win = getattr(self, '_popOutWindow', None)
        if pop_win is None: return
        self._stopTimers()
        self._popOutWindow.deleteLater()
        self._popOutWindow = None
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
            if hasattr(pop_win, '_panel_ref'): pop_win._panel_ref = None
            pop_win.hide()
            pop_win.deleteLater()
        self._restoreWidgetRefs()
        if hasattr(self, '_savedViewportMargins'): self.setViewportMargins(self._savedViewportMargins)
        self.popOutButton.setText(tr("debug.btn_popout"))
        mw = self.mainWindow
        if hasattr(mw, 'debugNavItem') and cfg.debugMode.value: mw.debugNavItem.setVisible(True)
        self._startTimers()
