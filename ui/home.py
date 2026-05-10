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
主界面模块
"""

import ctypes
import datetime
import json
import logging
import os
import sys
import time

import cnlunar
import requests
import win32gui
import win32ui
from PIL import Image
from pycaw.pycaw import AudioUtilities
from PyQt6.QtCore import (
    QDate,
    QEvent,
    QPropertyAnimation,
    QRect,
    Qt,
    QTime,
    QTimer,
)
from PyQt6.QtGui import (
    QColor,
    QIcon,
    QPainter,
    QPen,
    QPixmap,
)
from PyQt6.QtWidgets import (
    QApplication,
    QGraphicsBlurEffect,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
)
from qfluentwidgets import (
    FluentIcon as FIF,
    InfoBar,
    PushButton,
)

from core.config import cfg, save_cfg
from core.constants import APP_NAME, BASE_DIR, get_resPath
from core.logger import logger
from core.utils import get_cached_content, save_cache
from services.weather import WeatherService, RegionDatabase
from services.poetry import PoetryService
from ui.draggable_widget import DraggableContainer, DraggableWidget
from ui.media_widget import MediaWidget
from ui.dock import QuickLaunchDock


class GuideLineOverlay(QWidget):
    """辅助线"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self._snapLines = []
        self._visible = False
        self.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)

    def setSnapLines(self, lines):
        self._snapLines = lines
        if self._visible:
            self.update()

    def showOverlay(self):
        self._visible = True
        self.show()
        self.update()

    def hideOverlay(self):
        self._visible = False
        self._snapLines = []
        self.hide()
        self.repaint()

    def isVisible(self):
        return self._visible and super().isVisible()

    def paintEvent(self, event):
        if not self._visible or not self._snapLines:
            return

        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        w, h = self.width(), self.height()

        theme_color = cfg.themeColor.value
        if isinstance(theme_color, str):
            primary_color = QColor(theme_color)
        else:
            primary_color = theme_color

        for line_data in self._snapLines:
            if len(line_data) == 2:
                direction, pos = line_data
                is_center = (direction in ('h', 'v') and pos == 0.5)
            elif len(line_data) >= 3:
                direction, pos = line_data[0], line_data[1]
                is_center = line_data[2] if len(line_data) > 2 else False
            else:
                continue

            if is_center:
                color = QColor(primary_color)
                pen = QPen(color)
                pen.setWidth(3)
            elif direction.startswith('widget'):
                pen = QPen(QColor(100, 200, 255, 80))
                pen.setWidth(1)
            else:
                opacity = 120 if direction.endswith('third') else 80
                pen = QPen(QColor(255, 255, 255, opacity))
                pen.setWidth(1)

            pen.setStyle(Qt.PenStyle.DashLine)
            painter.setPen(pen)

            if direction.endswith('h') or direction == 'h':
                y = int(pos)
                painter.drawLine(0, y, w, y)
            else:
                x = int(pos)
                painter.drawLine(x, 0, x, h)

        painter.end()


class HomeInterface(QWidget):
    """主界面"""

    def __init__(self, mainWindow, parent=None):
        super().__init__(parent)
        self.mainWindow = mainWindow
        self.setObjectName("home")
        self.isEditMode = False
        self._guideOverlay = None
        self._snapLines = []
        self._snapThreshold = 10

        self._initBackground()
        self._initLabels()
        self._initContainers()
        self._initQuickLaunch()
        self._initEditButton()
        self._initMediaWidget()
        self._initLayout()
        self._initTimers()

        self.editPanelCreated = False
        self.editPanel = None

        logger.info("主界面初始化完成")

    def _initBackground(self):
        self.homeBackgroundImage = QLabel()
        self.homeBackgroundImage.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.homeBackgroundImage.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        self.originalPixmap = QPixmap(1, 1)
        self.originalPixmap.fill(Qt.GlobalColor.transparent)
        self.homeBackgroundImage.setPixmap(self.originalPixmap)
        self.homeBackgroundImage.setMinimumSize(100, 100)

        self.homeDimOverlay = QWidget()
        self.homeDimOverlay.setObjectName("dimOverlay")
        self.homeDimOverlay.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

    def _initLabels(self):
        self.clockLabel = QLabel("00:00:00")
        self.clockLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.dateLabel = QLabel("")
        self.dateLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.weatherTempLabel = QLabel("")
        self.weatherTempLabel.setObjectName("weatherTempLabel")
        self.weatherTempLabel.setAlignment(Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignRight)

        self.weatherIconLabel = QLabel("")
        self.weatherIconLabel.setObjectName("weatherIconLabel")
        self.weatherIconLabel.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignRight)

        self.poetryLabel = QLabel("")
        self.poetryLabel.setObjectName("poetryLabel")
        self.poetryLabel.setAlignment(Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignHCenter)
        self.poetryLabel.setWordWrap(False)

        self.countdownLabel = QLabel("")
        self.countdownLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.schoolClassLabel = QLabel("")
        self.schoolClassLabel.setObjectName("schoolClassLabel")
        self.schoolClassLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.schoolNameLabel = QLabel("")
        self.schoolNameLabel.setObjectName("schoolNameLabel")
        self.schoolNameLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.updateClockStyle()

    def _initContainers(self):
        self.clockContainer = DraggableContainer(self, component_id="clock", layout_direction="vertical")
        self.clockContainer.setObjectName("clockContainer")
        self.clockLayout = self.clockContainer.inner_layout
        self.clockLayout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self.clockLayout.setContentsMargins(0, 0, 0, 0)
        self.clockLayout.setSpacing(0)
        self.clockLayout.addWidget(self.clockLabel)
        self.clockLayout.addWidget(self.dateLabel)
        self.clockContainer.setPositionPercent(0.5, 0.25)
        self.clockContainer.positionChanged.connect(self._onClockPositionChanged)
        self.clockContainer.adjustSize()

        self.weatherContainer = DraggableContainer(self, component_id="weather", layout_direction="horizontal")
        self.weatherContainer.setObjectName("weatherContainer")
        weatherLayout = self.weatherContainer.inner_layout
        weatherLayout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignRight)
        weatherLayout.setContentsMargins(0, 0, 0, 0)
        weatherLayout.setSpacing(10)
        self.weatherTempLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.weatherIconLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        weatherLayout.addWidget(self.weatherTempLabel)
        weatherLayout.addWidget(self.weatherIconLabel)
        self.weatherContainer.setPositionPercent(0.9, 0.08)
        self.weatherContainer.positionChanged.connect(self._onWeatherPositionChanged)
        self.weatherContainer.adjustSize()

        self.schoolInfoContainer = DraggableContainer(self, component_id="school_info", layout_direction="vertical")
        self.schoolInfoContainer.setObjectName("schoolInfoContainer")
        self.schoolInfoLayout = self.schoolInfoContainer.inner_layout
        self.schoolInfoLayout.setSpacing(0)
        self.schoolInfoLayout.addWidget(self.schoolClassLabel)
        self.schoolInfoLayout.addWidget(self.schoolNameLabel)
        self.updateSchoolInfo()
        self.updateSchoolInfoStyle()
        self.schoolInfoContainer.setPositionPercent(0.08, 0.08)
        self.schoolInfoContainer.positionChanged.connect(self._onSchoolInfoPositionChanged)
        self.schoolInfoContainer.adjustSize()

        self.poetryContainer = DraggableContainer(self, component_id="poetry", layout_direction="vertical")
        self.poetryContainer.setObjectName("poetryContainer")
        poetryLayout = self.poetryContainer.inner_layout
        poetryLayout.setAlignment(Qt.AlignmentFlag.AlignBottom)
        poetryLayout.setContentsMargins(0, 0, 0, 0)
        poetryLayout.addWidget(self.poetryLabel)
        self.poetryContainer.setPositionPercent(0.5, 0.88)
        self.poetryContainer.positionChanged.connect(self._onPoetryPositionChanged)
        self.poetryContainer.adjustSize()

        self.countdownContainer = DraggableContainer(self, component_id="countdown", layout_direction="vertical")
        self.countdownContainer.setObjectName("countdownContainer")
        countdownLayout = self.countdownContainer.inner_layout
        countdownLayout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        countdownLayout.setContentsMargins(0, 0, 0, 0)
        countdownLayout.addWidget(self.countdownLabel)
        self.countdownContainer.setPositionPercent(0.5, 0.55)
        self.countdownContainer.positionChanged.connect(self._onCountdownPositionChanged)
        self.countdownContainer.adjustSize()

        self._draggable_widgets = [
            self.clockContainer,
            self.weatherContainer,
            self.poetryContainer,
            self.countdownContainer,
            self.schoolInfoContainer,
        ]

    def _initQuickLaunch(self):
        self.quickLaunchDock = QuickLaunchDock(self)
        self.quickLaunchDock.setObjectName("quickLaunchDock")
        self._updateQuickLaunch()

    def _initEditButton(self):
        self.editContainer = QWidget()
        self.editContainer.setObjectName("editContainer")
        self.editLayout = QVBoxLayout(self.editContainer)
        self.editLayout.setAlignment(Qt.AlignmentFlag.AlignBottom)
        self.editLayout.setContentsMargins(0, 0, 0, 20)

        self.editButton = PushButton("编辑", parent=self.editContainer)
        self.editButton.setObjectName("editButton")
        self.editButton.setFixedSize(80, 32)
        self.editButton.clicked.connect(self._enterEditMode)

        self.editLayout.addWidget(self.editButton)
        self.editContainer.setFixedSize(80, 52)
        self.editContainer.setSizePolicy(QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Fixed)

    def _initMediaWidget(self):
        self.mediaContainer = DraggableContainer(self, component_id="media", layout_direction="vertical")
        self.mediaContainer.setObjectName("mediaContainer")
        self.mediaContainer.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.mediaContainer.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Maximum)
        self.mediaContainerLayout = self.mediaContainer.inner_layout
        self.mediaContainerLayout.setAlignment(Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignLeft)
        self.mediaContainerLayout.setContentsMargins(0, 0, 0, 0)
        self.mediaContainerLayout.setSpacing(0)
        self.mediaWidget = MediaWidget(self)
        self.mediaWidget.setObjectName("mediaWidget")
        self.mediaContainerLayout.addWidget(self.mediaWidget)
        self.mediaContainer.setPositionPercent(0.12, 0.85)
        self.mediaContainer.positionChanged.connect(self._onMediaPositionChanged)
        self.mediaContainer.adjustSize()

        self._draggable_widgets.append(self.mediaContainer)

    def _initLayout(self):
        self.homeContent = QWidget(self)
        self.homeContent.setObjectName("homeContent")

        self.gridLayout = QGridLayout(self.homeContent)
        self.gridLayout.setContentsMargins(0, 0, 0, 0)
        self.gridLayout.setSpacing(0)

        self.gridLayout.addWidget(self.homeBackgroundImage, 0, 0, 1, 1)
        self.gridLayout.addWidget(self.homeDimOverlay, 0, 0, 1, 1)
        self.gridLayout.addWidget(self.editContainer, 0, 0, 1, 1, Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignRight)

        for widget in self._draggable_widgets:
            if widget:
                widget.setParent(self.homeContent)
                widget.show()
                if hasattr(widget, 'inner_layout') and widget.inner_layout:
                    widget.inner_layout.activate()
                    widget.adjustSize()
                widget._updatePositionFromPercent()

        self.quickLaunchDock.setParent(self.homeContent)
        self.quickLaunchDock.show()
        if self.quickLaunchDock.width() > 0 and self.quickLaunchDock.height() > 0:
            self.quickLaunchDock.move(
                (1000 - self.quickLaunchDock.width()) // 2,
                700 - self.quickLaunchDock.height() - 30
            )

        homeLayout = QVBoxLayout(self)
        homeLayout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        homeLayout.setContentsMargins(0, 0, 0, 0)
        homeLayout.addWidget(self.homeContent)

    def _initTimers(self):
        self.clockTimer = QTimer(self)
        self.clockTimer.timeout.connect(self._updateClock)
        self.clockTimer.start(1000)
        cfg.showClock.valueChanged.connect(self._updateClock)
        cfg.showClockSeconds.valueChanged.connect(self._updateClock)
        cfg.showLunarCalendar.valueChanged.connect(self._updateClock)
        cfg.clockColor.valueChanged.connect(self.updateClockStyle)
        cfg.clockSize.valueChanged.connect(self.updateClockStyle)
        cfg.dateSize.valueChanged.connect(self.updateClockStyle)
        cfg.poetrySize.valueChanged.connect(self.updateClockStyle)
        cfg.weatherSize.valueChanged.connect(self.updateClockStyle)
        cfg.weatherIconSize.valueChanged.connect(self._updateWeatherIcon)
        self._updateClock()

        self.poetryTimer = QTimer(self)
        self.poetryTimer.timeout.connect(self._updatePoetry)
        cfg.showPoetry.valueChanged.connect(self._updatePoetry)
        cfg.poetryApiUrl.valueChanged.connect(self._updatePoetry)
        cfg.poetryUpdateInterval.valueChanged.connect(self._updatePoetryInterval)
        self._updatePoetryInterval()

        self.countdownTimer = QTimer(self)
        self.countdownTimer.timeout.connect(self._updateCountdown)
        self.countdownCarouselIndex = 0
        cfg.showCountdown.valueChanged.connect(self._updateCountdown)
        cfg.countdownTextColor.valueChanged.connect(self._onCountdownStyleChanged)
        cfg.countdownTextSize.valueChanged.connect(self._onCountdownStyleChanged)
        cfg.countdownConnectorColor.valueChanged.connect(self._onCountdownStyleChanged)
        cfg.countdownConnectorSize.valueChanged.connect(self._onCountdownStyleChanged)
        cfg.countdownDisplayMode.valueChanged.connect(self._updateCountdown)
        cfg.countdownCarouselInterval.valueChanged.connect(self._updateCountdownCarouselInterval)
        cfg.countdownList.valueChanged.connect(self._updateCountdown)
        self.updateCountdownStyle()

        self._updateCountdownCarouselInterval()
        self._updateCountdown()
        self.countdownRefreshTimer = QTimer(self)
        self.countdownRefreshTimer.timeout.connect(self._updateCountdown)
        self.countdownRefreshTimer.start(1000)

        self.weatherTimer = QTimer(self)
        self.weatherTimer.timeout.connect(self._updateWeather)
        cfg.weatherUpdateInterval.valueChanged.connect(self._updateWeatherInterval)
        cfg.showWeather.valueChanged.connect(self._updateWeather)
        self._updateWeatherInterval()

        self._initMediaWidgetTimers()

        self._checkAndRefreshQuickLaunchIcons()

        self.loadComponentPositions()

    def _initMediaWidgetTimers(self):
        try:
            cfg.showMediaInfo.valueChanged.connect(self._onShowMediaInfoChanged)
            cfg.showMediaCover.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaWidth.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaLyricsAdvance.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaUpdateInterval.valueChanged.connect(self._onMediaUpdateIntervalChanged)

            if cfg.showMediaInfo.value:
                self.mediaWidget.start()
            else:
                self.mediaContainer.hide()
        except Exception as e:
            logger.exception(f"初始化媒体控件失败: {e}")

    def _onShowMediaInfoChanged(self, value: bool):
        if value:
            self.mediaContainer.show()
            self.mediaWidget.start()
        else:
            self.mediaWidget.stop()
            self.mediaContainer.hide()
        logger.info(f"媒体控件显示: {value}")

    def _onMediaSettingsChanged(self, value):
        if hasattr(self, 'mediaWidget'):
            self.mediaWidget.update_settings()

    def _onMediaUpdateIntervalChanged(self, value):
        if hasattr(self, 'mediaWidget') and cfg.showMediaInfo.value:
            self.mediaWidget.stop()
            self.mediaWidget.start()

    def showEvent(self, event):
        super().showEvent(event)
        QTimer.singleShot(100, self._refreshAllComponentSizes)

    def _refreshAllComponentSizes(self):
        if not hasattr(self, '_draggable_widgets'):
            return
        for widget in self._draggable_widgets:
            if widget and hasattr(widget, 'inner_layout') and widget.inner_layout:
                widget.inner_layout.activate()
                widget.adjustSize()
                widget._updatePositionFromPercent()

    def resizeEvent(self, event):
        super().resizeEvent(event)
        available_width = self.width()
        available_height = self.height()

        if hasattr(self, 'homeBackgroundImage') and self.homeBackgroundImage:
            try:
                if hasattr(self, 'originalPixmap') and self.originalPixmap is not None and not self.originalPixmap.isNull():
                    scaled_pixmap = self.originalPixmap.scaled(available_width, available_height, Qt.AspectRatioMode.IgnoreAspectRatio, Qt.TransformationMode.SmoothTransformation)
                    blur_effect = QGraphicsBlurEffect()
                    blur_radius = cfg.backgroundBlurRadius.value
                    blur_effect.setBlurRadius(blur_radius)
                    self.homeBackgroundImage.setGraphicsEffect(blur_effect)
                    self.homeBackgroundImage.setPixmap(scaled_pixmap)
            except Exception as e:
                logger.error(f"resizeEvent 错误：{e}")

        if hasattr(self, 'homeContent') and self.homeContent:
            if hasattr(self, 'quickLaunchDock') and self.quickLaunchDock:
                dock_width = self.quickLaunchDock.width()
                dock_height = self.quickLaunchDock.height()
                if dock_width > 0 and dock_height > 0:
                    self.quickLaunchDock.setGeometry(
                        (available_width - dock_width) // 2,
                        available_height - dock_height - 20,
                        dock_width,
                        dock_height
                    )

        if hasattr(self, '_draggable_widgets'):
            for widget in self._draggable_widgets:
                if widget and hasattr(widget, 'onParentResize'):
                    widget.onParentResize()

        if hasattr(self, 'editPanel') and self.editPanel:
            try:
                self.editPanel.updatePositionOnResize()
            except Exception:
                pass

        if hasattr(self, '_guideOverlay') and self._guideOverlay and self._guideOverlay.isVisible():
            self._updateGuideLinesPosition()

    def _updateClock(self):
        if not cfg.showClock.value:
            self.clockLabel.hide()
            self.dateLabel.hide()
            return

        self.clockLabel.show()
        self.dateLabel.show()

        currentTime = QTime.currentTime()
        currentDate = QDate.currentDate()

        if cfg.showClockSeconds.value:
            timeString = currentTime.toString("HH:mm:ss")
        else:
            timeString = currentTime.toString("HH:mm")
        self.clockLabel.setText(timeString)

        solarString = currentDate.toString("yyyy 年 M 月 d 日 dddd")

        if cfg.showLunarCalendar.value:
            try:
                py_datetime = datetime.datetime(currentDate.year(), currentDate.month(), currentDate.day(), 0, 0, 0)
                lunar = cnlunar.Lunar(py_datetime)
                lunarMonthCn = lunar.lunarMonthCn
                lunarDayCn = lunar.lunarDayCn
                lunarMonthCn = lunarMonthCn.replace("大", "").replace("小", "")
                lunarString = f"{lunarMonthCn}{lunarDayCn}"
                dateString = f"{solarString} {lunarString}"
            except Exception as e:
                logger.error(f"农历显示错误：{e}")
                dateString = solarString
        else:
            dateString = solarString

        self.dateLabel.setText(dateString)
        if hasattr(self, 'clockContainer'):
            self.clockContainer.updateSize()

    def updateClockStyle(self):
        clock_color = cfg.clockColor.value
        color_str = clock_color.name() if hasattr(clock_color, 'name') else str(clock_color)

        clock_size = cfg.clockSize.value
        date_size = cfg.dateSize.value
        poetry_size = cfg.poetrySize.value
        weather_size = cfg.weatherSize.value

        self.clockLabel.setStyleSheet(f"""
            color: {color_str}; 
            font-size: {clock_size}px; 
            font-weight: bold; 
            font-family: "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)

        self.dateLabel.setStyleSheet(f"""
            color: {color_str}; 
            font-size: {date_size}px; 
            font-family: "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)

        self.poetryLabel.setStyleSheet(f"""
            color: {color_str}; 
            font-size: {poetry_size}px; 
            font-family: "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)

        self.weatherTempLabel.setStyleSheet(f"""
            color: {color_str}; 
            font-size: {weather_size}px; 
            font-family: "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)
        if hasattr(self, 'clockContainer'):
            self.clockContainer.updateSize()
        if hasattr(self, 'poetryContainer'):
            self.poetryContainer.updateSize()
        if hasattr(self, 'weatherContainer'):
            self.weatherContainer.updateSize()

    def _updatePoetryInterval(self):
        self.poetryTimer.stop()
        interval_str = cfg.poetryUpdateInterval.value
        if interval_str == "从不":
            self._updatePoetry()
            return
        elif interval_str == "10 分钟":
            interval = 10 * 60 * 1000
        elif interval_str == "30 分钟":
            interval = 30 * 60 * 1000
        elif interval_str == "1 小时":
            interval = 60 * 60 * 1000
        elif interval_str == "3 小时":
            interval = 3 * 60 * 60 * 1000
        elif interval_str == "6 小时":
            interval = 6 * 60 * 60 * 1000
        elif interval_str == "12 小时":
            interval = 12 * 60 * 60 * 1000
        elif interval_str == "1 天":
            interval = 24 * 60 * 60 * 1000
        else:
            interval = 60 * 60 * 1000
        self.poetryTimer.start(interval)
        self._updatePoetry()

    def _updatePoetry(self):
        if not cfg.showPoetry.value:
            self.poetryLabel.hide()
            return
        self.poetryLabel.show()

        text = PoetryService.get_poetry_with_cache()
        self.poetryLabel.setText(text)
        if hasattr(self, 'poetryContainer'):
            self.poetryContainer.updateSize()

    def _updateWeatherInterval(self):
        self.weatherTimer.stop()
        interval_str = cfg.weatherUpdateInterval.value
        if interval_str == "从不":
            self._updateWeather()
            return
        elif interval_str == "15 分钟":
            interval = 15 * 60 * 1000
        elif interval_str == "30 分钟":
            interval = 30 * 60 * 1000
        elif interval_str == "1 小时":
            interval = 60 * 60 * 1000
        elif interval_str == "3 小时":
            interval = 3 * 60 * 60 * 1000
        elif interval_str == "6 小时":
            interval = 6 * 60 * 60 * 1000
        elif interval_str == "12 小时":
            interval = 12 * 60 * 60 * 1000
        elif interval_str == "24 小时":
            interval = 24 * 60 * 60 * 1000
        else:
            interval = 60 * 60 * 1000
        self.weatherTimer.start(interval)
        self._updateWeather()

    def _updateWeather(self):
        if not cfg.showWeather.value:
            self.weatherTempLabel.hide()
            self.weatherIconLabel.hide()
            return

        self.weatherTempLabel.show()
        self.weatherIconLabel.show()

        cached = get_cached_content("weather")
        if cached:
            try:
                weather_text = f"{cached.get('current_temp', '?')}{cached.get('temp_unit', '°C')}"
                self.weatherTempLabel.setText(weather_text)
                self.current_weather_code = cached.get('weather_code')
                self._updateWeatherIcon()
                logger.info(f"使用缓存天气：{weather_text}")
                if hasattr(self, 'weatherContainer'):
                    self.weatherContainer.updateSize()
                return
            except Exception as e:
                logger.warning(f"读取缓存天气数据失败：{e}")

        success = False
        try:
            city = cfg.city.value
            logger.info(f"正在更新天气，使用城市：{city}")

            city_db = RegionDatabase()
            city_code = city_db.get_code(city)

            if city_code:
                location_key = f"weathercn:{city_code}"
            else:
                location_key = "weathercn:101010100"
                logger.warning(f"未找到城市 {city} 的代码，使用默认值")

            logger.info(f"城市 {city} 对应的 locationKey: {location_key}")

            api_url = f"https://weatherapi.market.xiaomi.com/wtr-v3/weather/all?locationKey={location_key}&latitude=39.9042&longitude=116.4074&appKey=weather20151024&sign=zUFJoAR2ZVrDy1vF3D07&isGlobal=false&locale=zh_cn"
            response = requests.get(api_url, timeout=10)

            if response.status_code == 200:
                data = response.json()
                if 'current' in data:
                    current = data['current']

                    temperature = current.get('temperature', {})
                    current_temp = temperature.get('value', 0)
                    temp_unit = temperature.get('unit', '°C')

                    weather_code = current.get('weather', 0)
                    try:
                        weather_code = int(weather_code)
                    except (ValueError, TypeError):
                        weather_code = 0

                    weather_map = {
                        0: "晴", 1: "多云", 2: "阴", 3: "阵雨", 4: "雷阵雨",
                        5: "雷阵雨并伴有冰雹", 6: "雨夹雪", 7: "小雨", 8: "中雨",
                        9: "大雨", 10: "暴雨", 11: "大暴雨", 12: "特大暴雨",
                        13: "阵雪", 14: "小雪", 15: "中雪", 16: "大雪", 17: "暴雪",
                        18: "雾", 19: "冻雨", 20: "沙尘暴",
                    }

                    weather = weather_map.get(weather_code, "未知")
                    weather_text = f"{current_temp}{temp_unit}"
                    self.weatherTempLabel.setText(weather_text)
                    if hasattr(self, 'weatherContainer'):
                        self.weatherContainer.updateSize()

                    self.current_weather_code = weather_code
                    self._updateWeatherIcon()

                    cache_data = {
                        "current_temp": current_temp,
                        "temp_unit": temp_unit,
                        "weather_code": weather_code,
                        "weather": weather,
                    }
                    save_cache("weather", cache_data, cfg.weatherUpdateInterval.value)
                    success = True
            else:
                logger.error(f"天气 API 请求失败，状态码：{response.status_code}")
        except Exception as e:
            logger.error(f"天气更新失败：{e}")

        if not success:
            self.weatherTempLabel.setText("? °C")
            self.current_weather_code = None
            self.weatherIconLabel.clear()
            if hasattr(self, 'weatherContainer'):
                self.weatherContainer.updateSize()

    def _updateWeatherIcon(self):
        try:
            if not hasattr(self, 'current_weather_code') or self.current_weather_code is None:
                return

            icon_map = {
                0: "0.svg", 1: "1.svg", 2: "2.svg", 3: "7.svg", 4: "4.svg",
                5: "5.svg", 6: "19.svg", 7: "7.svg", 8: "8.svg", 9: "9.svg",
                10: "10.svg", 11: "11.svg", 12: "11.svg", 13: "14.svg",
                14: "14.svg", 15: "15.svg", 16: "16.svg", 17: "17.svg",
                18: "18.svg", 19: "19.svg", 20: "20.svg",
            }

            icon_file = icon_map.get(self.current_weather_code, "0.svg")
            icon_path = get_resPath(os.path.join("resource", "icons", "weather", icon_file))
            if os.path.exists(icon_path):
                icon = QIcon(icon_path)
                icon_size = cfg.weatherIconSize.value
                pixmap = icon.pixmap(icon_size, icon_size)
                self.weatherIconLabel.setPixmap(pixmap)
            else:
                self.weatherIconLabel.setText("")
        except Exception as e:
            logger.error(f"天气图标更新失败：{e}")
        if hasattr(self, 'weatherContainer'):
            self.weatherContainer.updateSize()

    def _updateCountdownCarouselInterval(self):
        self.countdownTimer.stop()
        interval = cfg.countdownCarouselInterval.value * 1000
        self.countdownTimer.start(interval)
        self._updateCountdown()

    def _updateCountdown(self):
        if not cfg.showCountdown.value:
            self.countdownContainer.hide()
            return
        self.countdownContainer.show()
        countdown_list = cfg.countdownList.value or []
        if not countdown_list:
            self.countdownLabel.setText("")
            return
        display_mode = cfg.countdownDisplayMode.value
        if display_mode == "simultaneous":
            texts = []
            for cd in countdown_list:
                text = self._formatCountdown(cd)
                if text:
                    texts.append(text)
            self.countdownLabel.setText("<br>".join(texts))
        else:
            if not hasattr(self, 'countdownCarouselIndex'):
                self.countdownCarouselIndex = 0
            if self.countdownCarouselIndex >= len(countdown_list):
                self.countdownCarouselIndex = 0
            cd = countdown_list[self.countdownCarouselIndex]
            text = self._formatCountdown(cd)
            if text:
                self.countdownLabel.setText(text)
            self.countdownCarouselIndex += 1

    def _formatCountdown(self, countdown):
        title = countdown.get('title', '')
        target_time_str = countdown.get('target_time', '')
        if not title or not target_time_str:
            return ""
        try:
            target_time = datetime.datetime.strptime(target_time_str, '%Y-%m-%d %H:%M')
        except ValueError:
            return ""
        now = datetime.datetime.now()
        delta = target_time - now
        total_seconds = int(delta.total_seconds())
        target_date = target_time.date()
        now_date = now.date()

        def fmt(text, connector=""):
            if hasattr(self, 'countdownTextColor'):
                if connector:
                    return (f'<span style="color: {self.countdownTextColor}; font-size: {self.countdownTitleSize}px; font-weight: bold; font-family: &quot;HarmonyOS Sans&quot;, &quot;Microsoft YaHei&quot;, &quot;SimHei&quot;, sans-serif;">{title}</span>'
                            f'<span style="color: {self.countdownConnectorColor}; font-size: {self.countdownConnectorSize}px; font-weight: bold; font-family: &quot;HarmonyOS Sans&quot;, &quot;Microsoft YaHei&quot;, &quot;SimHei&quot;, sans-serif;">{connector}</span>'
                            f'<span style="color: {self.countdownTextColor}; font-size: {self.countdownDaysSize}px; font-weight: bold; font-family: &quot;HarmonyOS Sans&quot;, &quot;Microsoft YaHei&quot;, &quot;SimHei&quot;, sans-serif;">{text}</span>')
                else:
                    return (f'<span style="color: {self.countdownTextColor}; font-size: {self.countdownTitleSize}px; font-weight: bold; font-family: &quot;HarmonyOS Sans&quot;, &quot;Microsoft YaHei&quot;, &quot;SimHei&quot;, sans-serif;">{title}</span>'
                            f'<span style="color: {self.countdownTextColor}; font-size: {self.countdownDaysSize}px; font-weight: bold; font-family: &quot;HarmonyOS Sans&quot;, &quot;Microsoft YaHei&quot;, &quot;SimHei&quot;, sans-serif;">{text}</span>')
            else:
                return f"{title}{connector}{text}"

        if target_date == now_date and total_seconds < 0:
            return fmt("就在今天")
        elif total_seconds > 0:
            days = total_seconds // 86400
            hours = (total_seconds % 86400) // 3600
            minutes = (total_seconds % 3600) // 60
            seconds = total_seconds % 60
            if days >= 3:
                time_text = f"{days}天"
            elif days >= 1:
                time_text = f"{days}天{hours}时"
            elif hours >= 1:
                time_text = f"{hours}时"
            elif minutes >= 1:
                time_text = f"{minutes}分{seconds}秒"
            else:
                time_text = f"{seconds}秒"
            return fmt(time_text, "仅剩")
        else:
            past_days = abs(total_seconds) // 86400
            return fmt(f"{past_days}天", "已过去")

    def updateCountdownStyle(self):
        text_color = cfg.countdownTextColor.value
        text_color_str = text_color.name() if hasattr(text_color, 'name') else str(text_color)
        text_size = cfg.countdownTextSize.value

        connector_color = cfg.countdownConnectorColor.value
        connector_color_str = connector_color.name() if hasattr(connector_color, 'name') else str(connector_color)
        connector_size = cfg.countdownConnectorSize.value

        self.countdownTextColor = text_color_str
        self.countdownTitleSize = text_size
        self.countdownConnectorColor = connector_color_str
        self.countdownConnectorSize = connector_size
        self.countdownDaysSize = text_size

        self.countdownLabel.setStyleSheet(f"""
            color: {text_color_str};
            font-size: {text_size}px;
            font-weight: bold;
            font-family: "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)

        if hasattr(self, 'countdownContainer'):
            self.countdownContainer.updateSize()

    def _onCountdownStyleChanged(self):
        self.updateCountdownStyle()
        self._updateCountdown()
        if hasattr(self, 'countdownContainer'):
            self.countdownContainer.updateSize()

    def updateSchoolInfo(self):
        school = cfg.school.value
        school_class = cfg.schoolClass.value
        if cfg.showSchoolInfo.value and (school or school_class):
            self.schoolClassLabel.setText(school_class if school_class else "")
            self.schoolNameLabel.setText(school if school else "")
            self.schoolInfoContainer.show()
        else:
            self.schoolClassLabel.setText("")
            self.schoolNameLabel.setText("")
            self.schoolInfoContainer.hide()

    def updateSchoolInfoStyle(self):
        text_color = cfg.schoolInfoTextColor.value
        text_color_str = text_color.name() if hasattr(text_color, 'name') else str(text_color)
        text_size = cfg.schoolInfoTextSize.value
        self.schoolInfoTextColor = text_color_str
        self.schoolInfoTextSize = text_size
        self.schoolClassLabel.setStyleSheet(f"color: {text_color_str}; font-size: {text_size}px; font-weight: bold; font-family: \"HarmonyOS Sans\", \"Microsoft YaHei\", \"SimHei\", sans-serif;")
        self.schoolNameLabel.setStyleSheet(f"color: {text_color_str}; font-size: {text_size}px; font-weight: bold; font-family: \"HarmonyOS Sans\", \"Microsoft YaHei\", \"SimHei\", sans-serif;")
        if hasattr(self, 'schoolInfoContainer'):
            self.schoolInfoContainer.updateSize()

    def _updateQuickLaunch(self):
        if not hasattr(self, 'quickLaunchDock'):
            return
        if not cfg.showQuickLaunch.value:
            self.quickLaunchDock.hide()
            return
        self.quickLaunchDock.show()
        apps = cfg.quickLaunchApps.value
        if not apps:
            self.quickLaunchDock.hide()
            return
        self.quickLaunchDock.update_icon_size(cfg.quickLaunchIconSize.value)
        self.quickLaunchDock.set_apps(apps)

    def _checkAndRefreshQuickLaunchIcons(self):
        apps = cfg.quickLaunchApps.value
        if not apps:
            return
        icon_dir = os.path.join(BASE_DIR, 'data', 'ql_icon')
        os.makedirs(icon_dir, exist_ok=True)
        for app in apps:
            app_path = app.get('path', '')
            icon_filename = app.get('icon', '')
            if not app_path or not icon_filename:
                continue
            icon_save_path = os.path.join(icon_dir, icon_filename)
            if os.path.exists(icon_save_path):
                continue
            if not os.path.exists(app_path):
                continue
            logger.info(f"快捷启动栏图标不存在，重新提取: {app.get('name', '')} -> {icon_filename}")
            try:
                new_icon = self._extractIcon(app_path, icon_filename)
                if new_icon and new_icon != 'exe.ico':
                    app['icon'] = new_icon
                    cfg.quickLaunchApps.value = apps
                    save_cfg()
            except Exception as e:
                logger.error(f"重新提取图标失败 {app.get('name', '')}: {e}")

    def _extractIcon(self, exe_path, icon_filename):
        try:
            hicon = None
            try:
                res = win32gui.PrivateExtractIcons(exe_path, 0, 256, 256, 1, 0)
                if res and res[0]:
                    hicon = res[0][0]
            except Exception:
                pass
            if not hicon:
                large, small = win32gui.ExtractIconEx(exe_path, 0)
                if large and large[0]:
                    hicon = large[0]
            if not hicon:
                return 'exe.ico'
            ico_info = win32gui.GetIconInfo(hicon)
            hbm_mask = ico_info[3]
            hbm_color = ico_info[4]
            hbm = hbm_color if hbm_color else hbm_mask
            bmp_obj = win32gui.GetObject(hbm)
            if not bmp_obj:
                if hbm_color:
                    win32gui.DeleteObject(hbm_color)
                if hbm_mask:
                    win32gui.DeleteObject(hbm_mask)
                win32gui.DestroyIcon(hicon)
                return 'exe.ico'

            width = bmp_obj.bmWidth
            height = bmp_obj.bmHeight
            hdc = win32gui.GetDC(0)
            hdc_src = win32ui.CreateDCFromHandle(hdc)
            hdc_dest = hdc_src.CreateCompatibleDC()
            bitmap = win32ui.CreateBitmap()
            bitmap.CreateCompatibleBitmap(hdc_src, width, height)
            hdc_dest.SelectObject(bitmap)
            win32gui.DrawIcon(hdc_dest.GetSafeHdc(), 0, 0, hicon)
            bmpstr = bitmap.GetBitmapBits(True)

            if hbm_color:
                img = Image.frombuffer('RGBA', (width, height), bmpstr, 'raw', 'BGRA', 0, 1)
            else:
                img = Image.frombuffer('L', (width, height), bmpstr, 'raw', 'L', 0, 1).convert('RGBA')

            icon_dir = os.path.join(BASE_DIR, 'data', 'ql_icon')
            os.makedirs(icon_dir, exist_ok=True)
            icon_save_path = os.path.join(icon_dir, icon_filename)
            img.save(icon_save_path, format='PNG')
            if hbm_color:
                win32gui.DeleteObject(hbm_color)
            if hbm_mask:
                win32gui.DeleteObject(hbm_mask)
            win32gui.DestroyIcon(hicon)
            win32gui.ReleaseDC(0, hdc)
            return icon_filename
        except Exception as e:
            logger.error(f"提取图标失败: {e}")
            return 'exe.ico'

    def _enterEditMode(self):
        if not hasattr(self, 'editPanel'):
            try:
                self._createEditPanel()
            except Exception:
                logger.exception('创建编辑面板失败')
                InfoBar.error('编辑模式', '无法创建编辑面板', parent=self, duration=3000)
                return

        if self.editPanel is None or self.editPanel.isVisible():
            if self.editPanel is not None:
                self.editPanel.hidePanel()
            self.isEditMode = False
            self.mainWindow.navigationInterface.setEnabled(True)
            self._setDraggableEnabled(False)
            self._hideGuideLines()
        else:
            self.editPanel.showPanel()
            self.isEditMode = True
            self.mainWindow.navigationInterface.setEnabled(False)
            self._setDraggableEnabled(True)
            self._showGuideLines()
            self._updateEditButtonPosition()

    def _createEditPanel(self):
        if hasattr(self, 'editPanel') and self.editPanel is not None:
            return
        from ui.edit_panel import EditPanel
        self.editPanel = EditPanel(self.mainWindow)
        pr = self.mainWindow.rect()

        if self.editPanel.isLeftSide:
            self.editPanel.setGeometry(-self.editPanel._width, 0, self.editPanel._width, pr.height())
        else:
            self.editPanel.setGeometry(pr.width(), 0, self.editPanel._width, pr.height())
        self.editPanel.hide()
        self.editPanel.setVisible(False)

        if not self.editPanelCreated:
            self._updateEditButtonPosition()
            self.editPanelCreated = True

    def _setDraggableEnabled(self, enabled: bool):
        if hasattr(self, '_draggable_widgets'):
            for widget in self._draggable_widgets:
                if widget and hasattr(widget, 'setDraggable'):
                    widget.setDraggable(enabled)
                    if enabled and hasattr(widget, 'updateThemeColor'):
                        widget.updateThemeColor()
                    if not enabled:
                        widget.repaint()

    def _showGuideLines(self):
        if not hasattr(self, 'homeContent') or not self.homeContent:
            return

        if self._guideOverlay is None:
            self._guideOverlay = GuideLineOverlay(self.homeContent)
            self._guideOverlay.setGeometry(self.homeContent.rect())

        content_rect = self.homeContent.rect()
        w, h = content_rect.width(), content_rect.height()

        snap_lines = []

        guide_positions = [
            (0.5, 'h', 'center'), (0.5, 'v', 'center'),
            (1 / 3, 'h', 'third'), (2 / 3, 'h', 'third'),
            (1 / 3, 'v', 'third'), (2 / 3, 'v', 'third'),
            (0.25, 'h', 'quarter'), (0.75, 'h', 'quarter'),
            (0.25, 'v', 'quarter'), (0.75, 'v', 'quarter'),
        ]

        for pos, direction, line_type in guide_positions:
            y_or_x = int(h * pos) if direction == 'h' else int(w * pos)
            is_center = (line_type == 'center')
            snap_lines.append((direction, y_or_x, is_center))

        if hasattr(self, '_draggable_widgets'):
            for widget in self._draggable_widgets:
                if not widget or not widget.isVisible():
                    continue
                geo = widget.geometry()
                positions = [
                    ('widget_v', geo.left(), False),
                    ('widget_v', geo.center().x(), True),
                    ('widget_v', geo.right(), False),
                    ('widget_h', geo.top(), False),
                    ('widget_h', geo.center().y(), True),
                    ('widget_h', geo.bottom(), False),
                ]
                snap_lines.extend(positions)

        self._snapLines = snap_lines
        self._guideOverlay.setSnapLines(snap_lines)
        self._guideOverlay.showOverlay()
        self._guideOverlay.raise_()

    def _hideGuideLines(self):
        self._snapLines = []
        if self._guideOverlay:
            self._guideOverlay.hideOverlay()

    def _updateGuideLinesPosition(self):
        if not self._guideOverlay or not self._guideOverlay.isVisible():
            return
        if not hasattr(self, 'homeContent') or not self.homeContent:
            return
        self._guideOverlay.setGeometry(self.homeContent.rect())
        self._showGuideLines()

    def updateWidgetGuideLines(self):
        if not self.isEditMode or not hasattr(self, 'homeContent'):
            return
        self._showGuideLines()

    def getSnapPosition(self, x: int, y: int, widget_width: int, widget_height: int) -> tuple:
        if not self._snapLines:
            return x, y

        snapped_x = x
        snapped_y = y

        center_x = x + widget_width // 2
        center_y = y + widget_height // 2
        right_x = x + widget_width
        bottom_y = y + widget_height

        for direction, pos in self._snapLines:
            if direction == 'v':
                for check_x in [x, center_x, right_x]:
                    if abs(check_x - pos) <= self._snapThreshold:
                        if check_x == x:
                            snapped_x = pos
                        elif check_x == center_x:
                            snapped_x = pos - widget_width // 2
                        elif check_x == right_x:
                            snapped_x = pos - widget_width
                        break
            else:
                for check_y in [y, center_y, bottom_y]:
                    if abs(check_y - pos) <= self._snapThreshold:
                        if check_y == y:
                            snapped_y = pos
                        elif check_y == center_y:
                            snapped_y = pos - widget_height // 2
                        elif check_y == bottom_y:
                            snapped_y = pos - widget_height
                        break

        return snapped_x, snapped_y

    def _onClockPositionChanged(self, x: float, y: float):
        self.updateWidgetGuideLines()

    def _onWeatherPositionChanged(self, x: float, y: float):
        self.updateWidgetGuideLines()

    def _onPoetryPositionChanged(self, x: float, y: float):
        self.updateWidgetGuideLines()

    def _onCountdownPositionChanged(self, x: float, y: float):
        self.updateWidgetGuideLines()

    def _onSchoolInfoPositionChanged(self, x: float, y: float):
        self.updateWidgetGuideLines()

    def _onMediaPositionChanged(self, x: float, y: float):
        self.updateWidgetGuideLines()

    def _updateEditButtonPosition(self):
        if not hasattr(self, 'editPanel') or not hasattr(self, 'editLayout'):
            return

        while self.editLayout.count():
            item = self.editLayout.takeAt(0)
            if item.widget():
                item.widget().setParent(None)

        if self.editPanel.isLeftSide:
            self.editLayout.setAlignment(Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignLeft)
            self.editLayout.setContentsMargins(20, 0, 0, 20)
            if hasattr(self, 'gridLayout'):
                self.gridLayout.setAlignment(self.editContainer, Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignLeft)
        else:
            self.editLayout.setAlignment(Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignRight)
            self.editLayout.setContentsMargins(0, 0, 20, 20)
            if hasattr(self, 'gridLayout'):
                self.gridLayout.setAlignment(self.editContainer, Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignRight)

        self.editLayout.addWidget(self.editButton)

    def saveComponentPositions(self):
        if not hasattr(self, '_draggable_widgets'):
            return

        positions = {}
        for widget in self._draggable_widgets:
            if widget and hasattr(widget, 'component_id') and hasattr(widget, 'getPositionPercent'):
                comp_id = widget.component_id
                x, y = widget.getPositionPercent()
                positions[comp_id] = {"x": round(x, 4), "y": round(y, 4)}

        try:
            config_path = os.path.join(BASE_DIR, 'config', 'component_positions.json')
            os.makedirs(os.path.dirname(config_path), exist_ok=True)
            with open(config_path, 'w', encoding='utf-8') as f:
                json.dump(positions, f, indent=2, ensure_ascii=False)
            logger.info(f"组件位置已保存: {positions}")
        except Exception as e:
            logger.error(f"保存组件位置失败: {e}")

    def loadComponentPositions(self):
        if not hasattr(self, '_draggable_widgets'):
            return

        try:
            config_path = os.path.join(BASE_DIR, 'config', 'component_positions.json')
            if not os.path.exists(config_path):
                logger.info("组件位置配置文件不存在，使用默认位置")
                return

            with open(config_path, 'r', encoding='utf-8') as f:
                positions = json.load(f)

            for widget in self._draggable_widgets:
                if widget and hasattr(widget, 'component_id') and hasattr(widget, 'setPositionPercent'):
                    comp_id = widget.component_id
                    if comp_id in positions:
                        pos = positions[comp_id]
                        widget.setPositionPercent(pos['x'], pos['y'])
                        logger.info(f"已加载 {comp_id} 位置: ({pos['x']}, {pos['y']})")

            logger.info("所有组件位置已加载完成")
        except Exception as e:
            logger.error(f"加载组件位置失败: {e}")
