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
    QByteArray,
    QDate,
    QEasingCurve,
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
    QFrame,
    QGraphicsBlurEffect,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
    QAbstractItemView,
    QFileDialog,
    QFileIconProvider,
)
from qfluentwidgets import (
    FluentIcon as FIF,
    InfoBar,
    PushButton,
    MessageBoxBase,
    CalendarPicker,
    TimePicker,
    ComboBox,
    SwitchButton,
    SpinBox,
    LineEdit,
    ListWidget,
    ToolButton,
    StrongBodyLabel,
    BodyLabel,
    SubtitleLabel,
    SmoothScrollArea,
    CardWidget,
)

from core.config import cfg, save_cfg
from core.constants import APP_NAME, BASE_DIR, get_resPath, load_qss
from core.logger import logger
from core.utils import get_cached_content, save_cache, tr, get_translation_manager, TranslatableWidget
from data.software_list import get_software_icon_path
from services.weather import WeatherService, RegionDatabase
from services.poetry import PoetryService
from ui.component import DraggableContainer, DraggableWidget, MediaWidget, QuickLaunchDock


class GuideLineOverlay(QWidget):
    """辅助线"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self._alignLines = []
        self.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)

    def setAlignLines(self, lines):
        self._alignLines = lines
        self.update()

    def showOverlay(self):
        self.show()
        self.raise_()

    def hideOverlay(self):
        self._alignLines = []
        self.hide()

    def paintEvent(self, event):
        if not self._alignLines:
            return

        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        w, h = self.width(), self.height()

        theme_color = cfg.themeColor.value
        if isinstance(theme_color, str):
            primary_color = QColor(theme_color)
        else:
            primary_color = theme_color

        pen = QPen(QColor(primary_color.red(), primary_color.green(), primary_color.blue(), 100))
        pen.setWidthF(1)
        pen.setStyle(Qt.PenStyle.DashLine)
        painter.setPen(pen)
        for direction, pos in self._alignLines:
            if direction == 'h':
                painter.drawLine(0, int(pos), w, int(pos))
            else:
                painter.drawLine(int(pos), 0, int(pos), h)

        painter.end()


class HomeInterface(QWidget, TranslatableWidget):
    """主界面"""

    def __init__(self, mainWindow, parent=None):
        super().__init__(parent)
        self.mainWindow = mainWindow
        self.setObjectName("home")
        self.isEditMode = False
        self._guideOverlay = None
        self._snapThreshold = 8

        self._initBackground()
        self._initLabels()
        self._initContainers()
        self._initQuickLaunch()
        self._initEditButton()
        self._initMediaWidget()
        self._initLayout()
        self._initTimers()

        self.editPanelCreated = False
        self.editPanel = None  # deprecated

        self.setStyleSheet(load_qss('home.qss'))
        cfg.themeChanged.connect(self._updateTheme)

        self.setup_translatable_ui()

        logger.info(tr("home.init_complete"))

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
        self.quickLaunchContainer = DraggableContainer(self, component_id="quick_launch", layout_direction="vertical")
        self.quickLaunchContainer.setObjectName("quickLaunchContainer")
        quickLaunchLayout = self.quickLaunchContainer.inner_layout
        quickLaunchLayout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        quickLaunchLayout.setContentsMargins(0, 0, 0, 0)
        self.quickLaunchDock = QuickLaunchDock(self)
        self.quickLaunchDock.setObjectName("quickLaunchDock")
        quickLaunchLayout.addWidget(self.quickLaunchDock)
        self.quickLaunchContainer.setPositionPercent(0.5, 0.92)
        self.quickLaunchContainer.positionChanged.connect(self._onQuickLaunchPositionChanged)
        self.quickLaunchContainer.adjustSize()
        self._draggable_widgets.append(self.quickLaunchContainer)
        self._updateQuickLaunch()

    def _initEditButton(self):
        self.editContainer = QWidget()
        self.editContainer.setObjectName("editContainer")
        self.editLayout = QVBoxLayout(self.editContainer)
        self.editLayout.setAlignment(Qt.AlignmentFlag.AlignBottom)
        self.editLayout.setContentsMargins(0, 0, 0, 20)

        self.editButton = PushButton(tr("home.edit"), parent=self.editContainer)
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
                if hasattr(widget, 'settingRequested'):
                    widget.settingRequested.connect(self._openComponentSetting)

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

        cfg.showQuickLaunch.valueChanged.connect(self._updateQuickLaunch)
        cfg.quickLaunchIconSize.valueChanged.connect(self._updateQuickLaunch)
        cfg.quickLaunchIconSpacing.valueChanged.connect(self._updateQuickLaunch)
        cfg.quickLaunchShowLabels.valueChanged.connect(self._updateQuickLaunch)
        cfg.quickLaunchApps.valueChanged.connect(self._updateQuickLaunch)

        QTimer.singleShot(0, self.loadComponentPositions)
        QTimer.singleShot(0, self._checkAndRefreshQuickLaunchIcons)

    def retranslateUi(self):
        """重新翻译主界面"""
        try:
            if hasattr(self, 'editButton'):
                self.editButton.setText(tr("home.edit"))
            if hasattr(self, 'countdownAddButton'):
                self.countdownAddButton.setText(tr("home.add"))
            if hasattr(self, 'countdownEditButton'):
                self.countdownEditButton.setText(tr("home.edit"))
            if hasattr(self, 'countdownDeleteButton'):
                self.countdownDeleteButton.setText(tr("home.delete"))
            if hasattr(self, 'quickLaunchEditButton'):
                self.quickLaunchEditButton.setText(tr("home.edit_apps"))
            if hasattr(self, 'quickLaunchShowLabelsSwitch'):
                self.quickLaunchShowLabelsSwitch.setOffText(tr("common.off"))
                self.quickLaunchShowLabelsSwitch.setOnText(tr("common.on"))

            logger.info(tr("home.retranslate_complete"))
        except Exception as e:
            logger.error(f"更新主界面翻译失败: {e}")

    def _initMediaWidgetTimers(self):
        try:
            cfg.showMediaInfo.valueChanged.connect(self._onShowMediaInfoChanged)
            cfg.showMediaCover.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaWidth.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaHeight.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaTextSize.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaCoverSize.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaLyricsSize.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaLyricsAdvance.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaUpdateInterval.valueChanged.connect(self._onMediaUpdateIntervalChanged)
            cfg.mediaBgColor.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaBgOpacity.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaBorderRadius.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaBorderWidth.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaBorderColor.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaTitleColor.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaArtistColor.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaTimeColor.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaLyricsColor.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaProgressColor.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaProgressTrackColor.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaProgressHeight.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaCoverBorderRadius.valueChanged.connect(self._onMediaSettingsChanged)
            cfg.mediaCoverBorderColor.valueChanged.connect(self._onMediaSettingsChanged)

            try:
                self.mainWindow.wallpaper.wallpaperChanged.connect(lambda: self._onMediaSettingsChanged(None))
            except Exception:
                pass

            if cfg.showMediaInfo.value:
                self.mediaWidget.start()
            else:
                if self.isEditMode:
                    self.mediaContainer.setContentVisible(False)
                    self.mediaContainer.show()
                else:
                    self.mediaContainer.hide()
        except Exception as e:
            logger.exception(f"初始化媒体控件失败: {e}")

    def _onShowMediaInfoChanged(self, value: bool):
        if value:
            self.mediaContainer.setContentVisible(True)
            self.mediaWidget.start()
        else:
            self.mediaWidget.stop()
            if self.isEditMode:
                self.mediaContainer.setContentVisible(False)
                self.mediaContainer.show()
            else:
                self.mediaContainer.hide()
        logger.info(f"媒体控件显示: {value}")

    def _onMediaSettingsChanged(self, value):
        if hasattr(self, 'mediaWidget'):
            self.mediaWidget.update_settings()
        if hasattr(self, 'mediaContainer'):
            self.mediaContainer.adjustSize()

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

        if hasattr(self, '_draggable_widgets'):
            for widget in self._draggable_widgets:
                if widget and hasattr(widget, 'onParentResize'):
                    widget.onParentResize()

        if hasattr(self, '_guideOverlay') and self._guideOverlay and self._guideOverlay.isVisible():
            self._updateGuideLinesPosition()

    def _updateClock(self):
        if not cfg.showClock.value:
            self.clockLabel.hide()
            self.dateLabel.hide()
            if hasattr(self, 'clockContainer'):
                if self.isEditMode:
                    self.clockContainer.setContentVisible(False)
                    self.clockContainer.show()
                else:
                    self.clockContainer.hide()
            return

        if hasattr(self, 'clockContainer'):
            self.clockContainer.setContentVisible(True)
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

    def _updateTheme(self):
        self.setStyleSheet(load_qss('home.qss'))

    def _updatePoetryInterval(self):
        self.poetryTimer.stop()
        interval_str = cfg.poetryUpdateInterval.value
        if interval_str == "从不":
            self._updatePoetry(cache_only=True)
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
        self._updatePoetry(cache_only=True)

    def _updatePoetry(self, cache_only=False):
        if not cfg.showPoetry.value:
            self.poetryLabel.hide()
            if hasattr(self, 'poetryContainer'):
                if self.isEditMode:
                    self.poetryContainer.setContentVisible(False)
                    self.poetryContainer.show()
                else:
                    self.poetryContainer.hide()
            return
        if hasattr(self, 'poetryContainer'):
            self.poetryContainer.setContentVisible(True)
        self.poetryLabel.show()

        if cache_only:
            cached = get_cached_content("poetry")
            text = cached if cached else ""
        else:
            text = PoetryService.get_poetry_with_cache()
        self.poetryLabel.setText(text)
        if hasattr(self, 'poetryContainer'):
            self.poetryContainer.updateSize()

    def _updateWeatherInterval(self):
        self.weatherTimer.stop()
        interval_str = cfg.weatherUpdateInterval.value
        if interval_str == "从不":
            self._updateWeather(cache_only=True)
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
        self._updateWeather(cache_only=True)

    def _updateWeather(self, cache_only=False):
        if not cfg.showWeather.value:
            self.weatherTempLabel.hide()
            self.weatherIconLabel.hide()
            if hasattr(self, 'weatherContainer'):
                if self.isEditMode:
                    self.weatherContainer.setContentVisible(False)
                    self.weatherContainer.show()
                else:
                    self.weatherContainer.hide()
            return

        if hasattr(self, 'weatherContainer'):
            self.weatherContainer.setContentVisible(True)
        self.weatherTempLabel.show()
        self.weatherIconLabel.show()

        cached = get_cached_content("weather")
        if cached:
            try:
                current_temp = cached.get('current_temp') or cached.get('temp')
                temp_unit = cached.get('temp_unit') or cached.get('unit', '°C')
                weather_code = cached.get('weather_code') or cached.get('code')
                if current_temp is not None:
                    weather_text = f"{current_temp}{temp_unit}"
                    self.weatherTempLabel.setText(weather_text)
                    self.current_weather_code = weather_code
                    self._updateWeatherIcon()
                    logger.info(f"使用缓存天气：{weather_text}")
                    if hasattr(self, 'weatherContainer'):
                        self.weatherContainer.updateSize()
                    if cache_only:
                        return
            except Exception as e:
                logger.warning(f"读取缓存天气数据失败：{e}")

        if cache_only:
            return

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
            if self.isEditMode:
                self.countdownContainer.setContentVisible(False)
                self.countdownContainer.show()
            else:
                self.countdownContainer.hide()
            return
        self.countdownContainer.setContentVisible(True)
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

        if hasattr(self, 'countdownContainer'):
            self.countdownContainer.updateSize()

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
            return fmt(tr("time.today"))
        elif total_seconds > 0:
            days = total_seconds // 86400
            hours = (total_seconds % 86400) // 3600
            minutes = (total_seconds % 3600) // 60
            seconds = total_seconds % 60
            if days >= 3:
                time_text = tr("time.days", n=days)
            elif days >= 1:
                time_text = tr("time.days_hours", n=days, h=hours)
            elif hours >= 1:
                time_text = tr("time.hours", h=hours)
            elif minutes >= 1:
                time_text = tr("time.minutes_seconds", m=minutes, s=seconds)
            else:
                time_text = tr("time.seconds", s=seconds)
            return fmt(time_text, tr("time.remaining"))
        else:
            past_days = abs(total_seconds) // 86400
            return fmt(tr("time.elapsed", n=past_days))

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
            self.schoolInfoContainer.setContentVisible(True)
        else:
            self.schoolClassLabel.setText("")
            self.schoolNameLabel.setText("")
            if self.isEditMode:
                self.schoolInfoContainer.setContentVisible(False)
                self.schoolInfoContainer.show()
            else:
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
        if not hasattr(self, 'quickLaunchContainer'):
            return
        if not cfg.showQuickLaunch.value:
            if self.isEditMode:
                self.quickLaunchContainer.setContentVisible(False)
                self.quickLaunchContainer.show()
            else:
                self.quickLaunchContainer.hide()
            return
        self.quickLaunchContainer.setContentVisible(True)
        self.quickLaunchContainer.show()
        apps = cfg.quickLaunchApps.value or []
        self.quickLaunchDock.update_icon_size(cfg.quickLaunchIconSize.value)
        self.quickLaunchDock.set_apps(apps)
        self.quickLaunchContainer.updateSize()

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
        if self.isEditMode:
            self.isEditMode = False
            self.mainWindow.navigationInterface.setEnabled(True)
            self._setDraggableEnabled(False)
            self._hideGuideLines()
        else:
            self.isEditMode = True
            self.mainWindow.navigationInterface.setEnabled(False)
            self._setDraggableEnabled(True)
            self._showGuideLines()
            self._updateEditButtonPosition()
        self._refreshDisabledComponents()

    def _refreshDisabledComponents(self):
        self._updateClock()
        self._updatePoetry(cache_only=True)
        self._updateWeather(cache_only=True)
        self._updateCountdown()
        self.updateSchoolInfo()
        self._updateQuickLaunch()

    def _openComponentSetting(self, component_id: str):
        from ui.component_settings import ComponentSettingDialog
        dialog = ComponentSettingDialog.create(component_id, self)
        if dialog:
            dialog.exec()

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

        self._guideOverlay.setAlignLines([])
        self._guideOverlay.showOverlay()

    def _hideGuideLines(self):
        if self._guideOverlay:
            self._guideOverlay.hideOverlay()

    def _updateGuideLinesPosition(self):
        if not self._guideOverlay or not self._guideOverlay.isVisible():
            return
        if not hasattr(self, 'homeContent') or not self.homeContent:
            return
        self._guideOverlay.setGeometry(self.homeContent.rect())

    def _computeSnap(self, x, y, w, h, dragging_widget=None):
        snapped_x, snapped_y = x, y
        align_lines = []
        threshold = self._snapThreshold

        drag_points_x = [x, x + w / 2, x + w]
        drag_points_y = [y, y + h / 2, y + h]

        best_dx = threshold + 1
        best_dy = threshold + 1
        snap_x_val = None
        snap_y_val = None

        if hasattr(self, 'homeContent') and self.homeContent:
            cw = self.homeContent.width()
            ch = self.homeContent.height()

            margin = 20
            v_refs = [0, margin, cw / 4, cw / 3, cw / 2, cw * 2 / 3, cw * 3 / 4, cw - margin, cw]
            h_refs = [0, margin, ch / 4, ch / 3, ch / 2, ch * 2 / 3, ch * 3 / 4, ch - margin, ch]

            for ref_pos in v_refs:
                for i, dp in enumerate(drag_points_x):
                    dx = abs(dp - ref_pos)
                    if dx <= threshold and dx < best_dx:
                        best_dx = dx
                        offsets = [0, w / 2, w]
                        snap_x_val = ref_pos - offsets[i]

            for ref_pos in h_refs:
                for i, dp in enumerate(drag_points_y):
                    dy = abs(dp - ref_pos)
                    if dy <= threshold and dy < best_dy:
                        best_dy = dy
                        offsets = [0, h / 2, h]
                        snap_y_val = ref_pos - offsets[i]

        if snap_x_val is not None:
            snapped_x = int(round(snap_x_val))
        if snap_y_val is not None:
            snapped_y = int(round(snap_y_val))

        if hasattr(self, 'homeContent') and self.homeContent:
            cw = self.homeContent.width()
            ch = self.homeContent.height()
            margin = 20
            v_refs = [0, margin, cw / 4, cw / 3, cw / 2, cw * 2 / 3, cw * 3 / 4, cw - margin, cw]
            h_refs = [0, margin, ch / 4, ch / 3, ch / 2, ch * 2 / 3, ch * 3 / 4, ch - margin, ch]

            final_points_x = [snapped_x, snapped_x + w / 2, snapped_x + w]
            final_points_y = [snapped_y, snapped_y + h / 2, snapped_y + h]

            for ref_pos in v_refs:
                for dp in final_points_x:
                    if abs(dp - ref_pos) <= 1:
                        align_lines.append(('v', ref_pos))
                        break

            for ref_pos in h_refs:
                for dp in final_points_y:
                    if abs(dp - ref_pos) <= 1:
                        align_lines.append(('h', ref_pos))
                        break

        return snapped_x, snapped_y, align_lines

    def getSnapPosition(self, x, y, widget_width, widget_height, dragging_widget=None):
        sx, sy, _ = self._computeSnap(x, y, widget_width, widget_height, dragging_widget)
        return sx, sy

    def showDragAlignLines(self, dragging_widget):
        if not self._guideOverlay or not self._guideOverlay.isVisible():
            return
        if not dragging_widget:
            self._guideOverlay.setAlignLines([])
            return

        geo = dragging_widget.geometry()
        _, _, align_lines = self._computeSnap(
            geo.x(), geo.y(), geo.width(), geo.height(), dragging_widget
        )
        self._guideOverlay.setAlignLines(align_lines)

    def clearDragAlignLines(self):
        if self._guideOverlay:
            self._guideOverlay.setAlignLines([])

    def _onClockPositionChanged(self, x: float, y: float):
        pass
    # 这几段是干啥的我忘了 为啥还要传入xy坐标 _initContainers函数有引用 

    def _onWeatherPositionChanged(self, x: float, y: float):
        pass
    # 这几段是干啥的我忘了 为啥还要传入xy坐标 _initContainers函数有引用 

    def _onPoetryPositionChanged(self, x: float, y: float):
        pass
    # 这几段是干啥的我忘了 为啥还要传入xy坐标 _initContainers函数有引用 

    def _onCountdownPositionChanged(self, x: float, y: float):
        pass
    # 这几段是干啥的我忘了 为啥还要传入xy坐标 _initContainers函数有引用 

    def _onSchoolInfoPositionChanged(self, x: float, y: float):
        pass
    # 这几段是干啥的我忘了 为啥还要传入xy坐标 _initContainers函数有引用 

    def _onMediaPositionChanged(self, x: float, y: float):
        pass
    # 这几段是干啥的我忘了 为啥还要传入xy坐标 _initContainers函数有引用 

    def _onQuickLaunchPositionChanged(self, x: float, y: float):
        pass
    # 这几段是干啥的我忘了 为啥还要传入xy坐标 _initContainers函数有引用 

    def _updateEditButtonPosition(self):
        if not hasattr(self, 'editLayout'):
            return

        while self.editLayout.count():
            item = self.editLayout.takeAt(0)
            if item.widget():
                item.widget().setParent(None)

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
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.setAutoFillBackground(True)
        self._updateTheme()

        scroll = SmoothScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        scroll.setFrameShape(QFrame.Shape.NoFrame)
        content = QWidget()
        scroll.setWidget(content)
        v = QVBoxLayout(content)
        v.setContentsMargins(16, 16, 16, 16)
        v.setSpacing(12)
        titleLayout = QHBoxLayout()
        titleLabel = StrongBodyLabel(tr("home.edit_panel"), self)
        titleLayout.addWidget(titleLabel)
        titleLayout.addStretch()

        self.positionButton = ToolButton(parent=self)
        self.positionButton.setFixedSize(32, 32)
        self.positionButton.setToolTip(tr("home.switch_to_left"))
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
        self._createCountdownListCard(v)
        self._addSeparator(v)
        self._createCountdownSettings(v)
        self._updateCountdownSettingsEnabled(cfg.showCountdown.value)
        self._addSeparator(v)
        self._createSchoolInfoSettings(v)
        self._updateSchoolInfoSettingsEnabled(cfg.showSchoolInfo.value)
        self._addSeparator(v)
        self._createQuickLaunchSettings(v)
        self._updateQuickLaunchSettingsEnabled(cfg.showQuickLaunch.value)
        self._addSeparator(v)
        self._createMediaSettings(v)
        self._updateMediaSettingsEnabled(cfg.showMediaInfo.value)
        self._connectConfigSignals()
        self.__connectSignalToSlot()

        v.addStretch()

        self.closeButton = PushButton(tr("common.close"), self, icon=FIF.CLOSE)
        self.closeButton.setFixedHeight(36)
        v.addWidget(self.closeButton)
        self.closeButton.clicked.connect(self.hidePanel)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.addWidget(scroll)

        # 动画
        self.anim = QPropertyAnimation(self, QByteArray(b'geometry'))
        self.anim.setEasingCurve(QEasingCurve.Type.OutCubic)
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

    def _updatePoetrySettingsEnabled(self, enabled):
        self.poetryApiCombo.setEnabled(enabled)
        self.poetrySizeSpin.setEnabled(enabled)
        self.poetryUpdateIntervalCombo.setEnabled(enabled)

    def _updateWeatherSettingsEnabled(self, enabled):
        self.cityButton.setEnabled(enabled)
        self.weatherSizeSpin.setEnabled(enabled)
        self.weatherIconSizeSpin.setEnabled(enabled)
        self.weatherUpdateIntervalCombo.setEnabled(enabled)

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

    def _updateSchoolInfoSettingsEnabled(self, enabled):
        self.schoolEdit.setEnabled(enabled)
        self.schoolClassEdit.setEnabled(enabled)
        self.schoolInfoTextColorCombo.setEnabled(enabled)
        self.schoolInfoTextSizeSpin.setEnabled(enabled)

    def _updateQuickLaunchSettingsEnabled(self, enabled):
        self.showQuickLaunchSwitch.setEnabled(enabled)
        self.quickLaunchEditButton.setEnabled(enabled)

    def _updateMediaSettingsEnabled(self, enabled):
        self.showMediaCoverSwitch.setEnabled(enabled)
        self.mediaWidthSpin.setEnabled(enabled)
        self.mediaLyricsAdvanceSpin.setEnabled(enabled)

    def _connectConfigSignals(self):
        """连接配置变化信到 UI 更新"""
        # 时间设置
        cfg.showClock.valueChanged.connect(self._updateShowClockSwitch)
        cfg.showClockSeconds.valueChanged.connect(self._updateShowSecondsSwitch)
        cfg.showLunarCalendar.valueChanged.connect(self._updateShowLunarSwitch)
        cfg.clockColor.valueChanged.connect(self._updateClockColorCombo)
        cfg.clockSize.valueChanged.connect(self._updateClockSizeSpin)
        cfg.dateSize.valueChanged.connect(self._updateDateSizeSpin)

        # 一言设置
        cfg.showPoetry.valueChanged.connect(self._updateShowPoetrySwitch)
        cfg.poetryApiUrl.valueChanged.connect(self._updatePoetryApiEdit)
        cfg.poetrySize.valueChanged.connect(self._updatePoetrySizeSpin)
        cfg.poetryUpdateInterval.valueChanged.connect(self._updatePoetryUpdateIntervalCombo)

        # 天气设置
        cfg.showWeather.valueChanged.connect(self._updateShowWeatherSwitch)
        cfg.weatherSize.valueChanged.connect(self._updateWeatherSizeSpin)
        cfg.weatherIconSize.valueChanged.connect(self._updateWeatherIconSizeSpin)
        cfg.weatherUpdateInterval.valueChanged.connect(self._updateWeatherUpdateIntervalCombo)
        cfg.city.valueChanged.connect(self._updateCityButton)

        # 倒计时设置
        cfg.showCountdown.valueChanged.connect(self._updateShowCountdownSwitch)
        cfg.countdownDisplayMode.valueChanged.connect(self._updateCountdownDisplayModeCombo)
        cfg.countdownTextSize.valueChanged.connect(self._updateCountdownTextSizeSpin)
        cfg.countdownConnectorSize.valueChanged.connect(self._updateCountdownConnectorSizeSpin)
        cfg.countdownCarouselInterval.valueChanged.connect(self._updateCountdownCarouselIntervalSpin)
        cfg.countdownList.valueChanged.connect(self._updateCountdownList)
        cfg.countdownTextColor.valueChanged.connect(self._updateCountdownTextColorCombo)
        cfg.countdownConnectorColor.valueChanged.connect(self._updateCountdownConnectorColorCombo)

        # 学校信息设置
        cfg.showSchoolInfo.valueChanged.connect(self._updateShowSchoolInfoSwitch)
        cfg.schoolInfoTextColor.valueChanged.connect(self._updateSchoolInfoTextColorCombo)
        cfg.schoolInfoTextSize.valueChanged.connect(self._updateSchoolInfoTextSizeSpin)
        cfg.school.valueChanged.connect(self._updateSchoolEdit)
        cfg.schoolClass.valueChanged.connect(self._updateSchoolClassEdit)

        # 媒体设置
        cfg.showMediaInfo.valueChanged.connect(self._updateShowMediaInfoSwitch)
        cfg.showMediaCover.valueChanged.connect(self._updateShowMediaCoverSwitch)
        cfg.mediaWidth.valueChanged.connect(self._updateMediaWidthSpin)
        cfg.mediaLyricsAdvance.valueChanged.connect(self._updateMediaLyricsAdvanceSpin)

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
        try:
            self.clockColorCombo.currentTextChanged.disconnect(self._onClockColorChanged)
        except TypeError:
            pass
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
        try:
            self.poetryApiCombo.currentTextChanged.disconnect(self._onPoetryApiChanged)
        except TypeError:
            pass
        if value == 'https://api.imlcd.cn/yy/api.php':
            self.poetryApiCombo.setCurrentText(tr("home.yiyan_api"))
        elif value == 'https://www.ffapi.cn/int/v1/shici':
            self.poetryApiCombo.setCurrentText(tr("home.poetry_api"))
        else:
            self.poetryApiCombo.setCurrentText(tr("home.yiyan_api"))
        self.poetryApiCombo.currentTextChanged.connect(self._onPoetryApiChanged)

    def _updatePoetrySizeSpin(self, value):
        """更新一言大小旋转框"""
        self.poetrySizeSpin.setValue(value)

    def _updatePoetryUpdateIntervalCombo(self, value):
        """更新一言更新间隔下拉框"""
        try:
            self.poetryUpdateIntervalCombo.currentTextChanged.disconnect(self._onPoetryUpdateIntervalChanged)
        except TypeError:
            pass
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
        try:
            self.weatherUpdateIntervalCombo.currentTextChanged.disconnect(self._onWeatherUpdateIntervalChanged)
        except TypeError:
            pass
        self.weatherUpdateIntervalCombo.setCurrentText(value)
        self.weatherUpdateIntervalCombo.currentTextChanged.connect(self._onWeatherUpdateIntervalChanged)

    def _updateCityButton(self, value):
        """更新城市按钮"""
        self.cityButton.setText(value)

    def _createTimeSettings(self, layout):
        titleLabel = StrongBodyLabel(tr("home.time_settings"), self)
        layout.addWidget(titleLabel)
        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel(tr("home.enable_clock"), self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.showClockSwitch = SwitchButton(self)
        self.showClockSwitch.setChecked(cfg.showClock.value)
        self.showClockSwitch.checkedChanged.connect(self._onShowClockChanged)
        enableLayout.addWidget(self.showClockSwitch)
        layout.addLayout(enableLayout)
        secondsLayout = QHBoxLayout()
        secondsLabel = BodyLabel(tr("home.show_seconds"), self)
        secondsLabel.setFixedWidth(100)
        secondsLayout.addWidget(secondsLabel)
        self.showSecondsSwitch = SwitchButton(self)
        self.showSecondsSwitch.setChecked(cfg.showClockSeconds.value)
        self.showSecondsSwitch.checkedChanged.connect(self._onShowSecondsChanged)
        secondsLayout.addWidget(self.showSecondsSwitch)
        layout.addLayout(secondsLayout)
        lunarLayout = QHBoxLayout()
        lunarLabel = BodyLabel(tr("home.show_lunar"), self)
        lunarLabel.setFixedWidth(100)
        lunarLayout.addWidget(lunarLabel)
        self.showLunarSwitch = SwitchButton(self)
        self.showLunarSwitch.setChecked(cfg.showLunarCalendar.value)
        self.showLunarSwitch.checkedChanged.connect(self._onShowLunarChanged)
        lunarLayout.addWidget(self.showLunarSwitch)
        layout.addLayout(lunarLayout)
        colorLayout = QHBoxLayout()
        colorLabel = BodyLabel(tr("home.clock_color"), self)
        colorLabel.setFixedWidth(100)
        colorLayout.addWidget(colorLabel)
        self.clockColorCombo = ComboBox(self)
        self.clockColorCombo.addItems([tr("home.primary_color"), tr("color.white"), tr("color.black")])
        self.clockColorCombo.setCurrentText(self._getColorText(cfg.clockColor.value))
        self.clockColorCombo.setFixedWidth(120)
        self.clockColorCombo.currentTextChanged.connect(self._onClockColorChanged)
        colorLayout.addWidget(self.clockColorCombo)
        layout.addLayout(colorLayout)
        clockSizeLayout = QHBoxLayout()
        clockSizeLabel = BodyLabel(tr("home.clock_size"), self)
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
        dateSizeLabel = BodyLabel(tr("home.date_size"), self)
        dateSizeLabel.setFixedWidth(100)
        dateSizeLayout.addWidget(dateSizeLabel)
        self.dateSizeSpin = SpinBox(self)
        self.dateSizeSpin.setRange(12, 50)
        self.dateSizeSpin.setValue(cfg.dateSize.value)
        self.dateSizeSpin.setFixedWidth(120)
        self.dateSizeSpin.valueChanged.connect(self._onDateSizeChanged)
        dateSizeLayout.addWidget(self.dateSizeSpin)
        layout.addLayout(dateSizeLayout)

    def _createPoetrySettings(self, layout):
        titleLabel = StrongBodyLabel(tr("home.poetry_settings"), self)
        layout.addWidget(titleLabel)
        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel(tr("home.enable_poetry"), self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.showPoetrySwitch = SwitchButton(self)
        self.showPoetrySwitch.setChecked(cfg.showPoetry.value)
        self.showPoetrySwitch.checkedChanged.connect(self._onShowPoetryChanged)
        enableLayout.addWidget(self.showPoetrySwitch)
        layout.addLayout(enableLayout)
        apiLayout = QHBoxLayout()
        apiLabel = BodyLabel(tr("home.poetry_api_url"), self)
        apiLabel.setFixedWidth(100)
        apiLayout.addWidget(apiLabel)
        self.poetryApiCombo = ComboBox(self)
        self.poetryApiCombo.addItems([
            tr("home.yiyan_api"),
            tr("home.poetry_api")
        ])
        if cfg.poetryApiUrl.value == 'https://www.ffapi.cn/int/v1/shici':
            self.poetryApiCombo.setCurrentText(tr("home.poetry_api"))
        else:
            self.poetryApiCombo.setCurrentText(tr("home.yiyan_api"))
        self.poetryApiCombo.setFixedWidth(120)
        self.poetryApiCombo.currentTextChanged.connect(self._onPoetryApiChanged)
        apiLayout.addWidget(self.poetryApiCombo)
        layout.addLayout(apiLayout)
        poetrySizeLayout = QHBoxLayout()
        poetrySizeLabel = BodyLabel(tr("home.poetry_size"), self)
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
        poetryIntervalLabel = BodyLabel(tr("home.poetry_update_interval"), self)
        poetryIntervalLabel.setFixedWidth(100)
        poetryIntervalLayout.addWidget(poetryIntervalLabel)
        self.poetryUpdateIntervalCombo = ComboBox(self)
        self.poetryUpdateIntervalCombo.addItems([tr("time.never"), tr("time.minutes_5"), tr("time.minutes_10"), tr("time.minutes_30"), tr("time.hour_1"), tr("time.hours_3"), tr("time.hours_6"), tr("time.hours_12"), tr("time.day_1")])
        self.poetryUpdateIntervalCombo.setCurrentText(cfg.poetryUpdateInterval.value)
        self.poetryUpdateIntervalCombo.setFixedWidth(120)
        self.poetryUpdateIntervalCombo.currentTextChanged.connect(self._onPoetryUpdateIntervalChanged)
        poetryIntervalLayout.addWidget(self.poetryUpdateIntervalCombo)
        layout.addLayout(poetryIntervalLayout)

    def _createWeatherSettings(self, layout):
        """创建天气设置部分"""
        titleLabel = StrongBodyLabel(tr("home.weather_settings"), self)
        layout.addWidget(titleLabel)
        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel(tr("home.enable_weather"), self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.showWeatherSwitch = SwitchButton(self)
        self.showWeatherSwitch.setChecked(cfg.showWeather.value)
        self.showWeatherSwitch.checkedChanged.connect(self._onShowWeatherChanged)
        enableLayout.addWidget(self.showWeatherSwitch)
        layout.addLayout(enableLayout)
        cityLayout = QHBoxLayout()
        cityLabel = BodyLabel(tr("home.city"), self)
        cityLabel.setFixedWidth(100)
        cityLayout.addWidget(cityLabel)
        self.cityButton = PushButton(cfg.city.value, self)
        self.cityButton.setFixedHeight(36)
        self.cityButton.setFixedWidth(120)
        self.cityButton.clicked.connect(self._onCityButtonClicked)
        cityLayout.addWidget(self.cityButton)
        layout.addLayout(cityLayout)
        weatherSizeLayout = QHBoxLayout()
        weatherSizeLabel = BodyLabel(tr("home.weather_text_size"), self)
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
        iconSizeLabel = BodyLabel(tr("home.weather_icon_size"), self)
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
        weatherIntervalLabel = BodyLabel(tr("home.weather_update_interval"), self)
        weatherIntervalLabel.setFixedWidth(100)
        weatherIntervalLayout.addWidget(weatherIntervalLabel)
        self.weatherUpdateIntervalCombo = ComboBox(self)
        self.weatherUpdateIntervalCombo.addItems([tr("time.never"), tr("time.minutes_5"), tr("time.minutes_15"), tr("time.minutes_30"), tr("time.hour_1"), tr("time.hours_3"), tr("time.hours_6"), tr("time.hours_12"), tr("time.hours_24")])
        self.weatherUpdateIntervalCombo.setCurrentText(cfg.weatherUpdateInterval.value)
        self.weatherUpdateIntervalCombo.setFixedWidth(120)
        self.weatherUpdateIntervalCombo.currentTextChanged.connect(self._onWeatherUpdateIntervalChanged)
        weatherIntervalLayout.addWidget(self.weatherUpdateIntervalCombo)
        layout.addLayout(weatherIntervalLayout)

    def _updateTheme(self):
        """更新主题"""
        self.setStyleSheet(load_qss('home.qss'))

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
        """退出编辑模式"""
        parent = self.parent()
        if not parent:return

        home = self.mainWindow.homeInterface if hasattr(self.mainWindow, 'homeInterface') else None

        if home and hasattr(home, 'isEditMode'):home.isEditMode = False

        if hasattr(parent, 'navigationInterface'):parent.navigationInterface.setEnabled(True)

        if home and hasattr(home, '_setDraggableEnabled'):home._setDraggableEnabled(False)

        if home and hasattr(home, '_hideGuideLines'):home._hideGuideLines()

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
        if hasattr(self.mainWindow, 'deleteSelectedComponent'):self.mainWindow.deleteSelectedComponent()

    def _onShowClockChanged(self, checked: bool):
        """启用时钟开关变化"""
        cfg.showClock.value = checked
        self._updateTimeSettingsEnabled(checked)
        if hasattr(self.mainWindow, '_MainWindow__updateClock'):self.mainWindow._MainWindow__updateClock()
        logger.info(f"时间设置：启用时钟={'开启' if checked else '关闭'}")

    def _onShowSecondsChanged(self, checked: bool):
        """显示秒针开关变化"""
        cfg.showClockSeconds.value = checked
        if hasattr(self.mainWindow, '_MainWindow__updateClock'):self.mainWindow._MainWindow__updateClock()
        logger.info(f"时间设置：显示秒针={'开启' if checked else '关闭'}")

    def _onShowLunarChanged(self, checked: bool):
        """显示农历开关变化"""
        cfg.showLunarCalendar.value = checked
        if hasattr(self.mainWindow, '_MainWindow__updateClock'):self.mainWindow._MainWindow__updateClock()
        logger.info(f"时间设置：显示农历={'开启' if checked else '关闭'}")

    def _getColorText(self, color, default='main'):
        """获取颜色文本表示"""
        if not hasattr(color, 'name'):
            if default == 'red':return tr("color.red")
            elif default == 'white':return tr("color.white")
            return tr("home.primary_color")
        color_hex = color.name().upper()
        try:
            theme_color = cfg.themeColor.value
            if hasattr(theme_color, 'name'):
                theme_hex = theme_color.name().upper()
                if theme_hex == color_hex:return tr("home.primary_color")
        except Exception:pass
        if color_hex == '#FF0000':return tr("color.red")
        elif color_hex == '#FFFFFF':return tr("color.white")
        elif color_hex == '#000000':return tr("color.black")
        return tr("home.primary_color")

    def _onClockColorChanged(self, text: str):
        """时钟颜色变化"""

        if text == tr("color.white"):cfg.clockColor.value = "#FFFFFF"
        elif text == tr("color.black"):cfg.clockColor.value = "#000000"
        else:cfg.clockColor.value = cfg.themeColor.value.name() if hasattr(cfg.themeColor.value, 'name') else str(cfg.themeColor.value)

        if hasattr(self.mainWindow, 'updateClockStyle'):self.mainWindow.updateClockStyle()
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
            for widget in self.mainWindow.homeContent.findChildren(QWidget):
                if widget.objectName() == 'poetryWidget':
                    widget.setVisible(checked)
        logger.info(f"一言设置：启用一言={'开启' if checked else '关闭'}")

    def _onPoetryApiChanged(self, text: str):
        """一言 API 地址变化"""
        if text == tr("home.yiyan_api"):
            cfg.poetryApiUrl.value = 'https://api.imlcd.cn/yy/api.php'
        elif text == tr("home.poetry_api"):
            cfg.poetryApiUrl.value = 'https://www.ffapi.cn/int/v1/shici'
        else:
            cfg.poetryApiUrl.value = 'https://api.imlcd.cn/yy/api.php'
        if hasattr(self.mainWindow, '_MainWindow__updatePoetry'):
            self.mainWindow._MainWindow__updatePoetry()
        logger.info(f"一言设置：API 地址={cfg.poetryApiUrl.value}")

    def _onPoetryUpdateIntervalChanged(self, text: str):
        """一言更新间隔变化"""
        cfg.poetryUpdateInterval.value = text
        if hasattr(self.mainWindow, '_MainWindow__updatePoetryInterval'):self.mainWindow._MainWindow__updatePoetryInterval()
        logger.info(f"一言设置：更新间隔={text}")

    def _onPoetrySizeChanged(self, value: int):
        """一言大小变化"""
        cfg.poetrySize.value = value
        if hasattr(self.mainWindow, 'updateClockStyle'):self.mainWindow.updateClockStyle()
        logger.info(f"一言设置：一言大小={value}px")

    def _onShowWeatherChanged(self, checked: bool):
        """启用天气开关变化"""
        cfg.showWeather.value = checked
        self._updateWeatherSettingsEnabled(checked)
        if hasattr(self.mainWindow, '_MainWindow__updateWeather'):self.mainWindow._MainWindow__updateWeather()
        logger.info(f"天气设置：启用天气={'开启' if checked else '关闭'}")

    def _onWeatherSizeChanged(self, value: int):
        """天气文字大小变化"""
        cfg.weatherSize.value = value
        if hasattr(self.mainWindow, 'updateClockStyle'):self.mainWindow.updateClockStyle()
        logger.info(f"天气设置：天气文字大小={value}px")

    def _onWeatherIconSizeChanged(self, value: int):
        """天气图标大小变化"""
        cfg.weatherIconSize.value = value
        if hasattr(self.mainWindow, '_MainWindow__updateWeatherIcon'):self.mainWindow._MainWindow__updateWeatherIcon()
        logger.info(f"天气设置：天气图标大小={value}px")

    def _onWeatherUpdateIntervalChanged(self, text: str):
        """天气更新间隔变化"""
        cfg.weatherUpdateInterval.value = text
        if hasattr(self.mainWindow, '_MainWindow__updateWeatherInterval'):self.mainWindow._MainWindow__updateWeatherInterval()
        logger.info(f"天气设置：更新间隔={text}")

    def _onCityButtonClicked(self):
        """城市选择按钮点击"""
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
            self.positionButton.setToolTip(tr("home.switch_to_right"))
        else:
            self.positionButton.setIcon(FIF.CARE_LEFT_SOLID)
            self.positionButton.setToolTip(tr("home.switch_to_left"))
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

        titleLabel = StrongBodyLabel(tr("home.countdown_settings"), self)
        layout.addWidget(titleLabel)

        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel(tr("home.enable_countdown"), self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.showCountdownSwitch = SwitchButton(self)
        self.showCountdownSwitch.setChecked(cfg.showCountdown.value)
        self.showCountdownSwitch.checkedChanged.connect(self._onShowCountdownChanged)
        enableLayout.addWidget(self.showCountdownSwitch)
        layout.addLayout(enableLayout)

        # 文字颜色
        textColorLayout = QHBoxLayout()
        textColorLabel = BodyLabel(tr("home.text_color"), self)
        textColorLabel.setFixedWidth(100)
        textColorLayout.addWidget(textColorLabel)
        self.countdownTextColorCombo = ComboBox(self)
        self.countdownTextColorCombo.addItems([tr("color.red"), tr("color.white"), tr("color.black"), tr("home.primary_color")])
        self.countdownTextColorCombo.setCurrentText(self._getColorText(cfg.countdownTextColor.value, 'red'))
        self.countdownTextColorCombo.setFixedWidth(120)
        self.countdownTextColorCombo.currentTextChanged.connect(self._onCountdownTextColorChanged)
        textColorLayout.addWidget(self.countdownTextColorCombo)
        layout.addLayout(textColorLayout)

        # 连接词颜色
        connectorColorLayout = QHBoxLayout()
        connectorColorLabel = BodyLabel(tr("home.connector_color"), self)
        connectorColorLabel.setFixedWidth(100)
        connectorColorLayout.addWidget(connectorColorLabel)
        self.countdownConnectorColorCombo = ComboBox(self)
        self.countdownConnectorColorCombo.addItems([tr("color.red"), tr("color.white"), tr("color.black"), tr("home.primary_color")])
        self.countdownConnectorColorCombo.setCurrentText(self._getColorText(cfg.countdownConnectorColor.value, 'white'))
        self.countdownConnectorColorCombo.setFixedWidth(120)
        self.countdownConnectorColorCombo.currentTextChanged.connect(self._onCountdownConnectorColorChanged)
        connectorColorLayout.addWidget(self.countdownConnectorColorCombo)
        layout.addLayout(connectorColorLayout)

        # 文字大小
        textSizeLayout = QHBoxLayout()
        textSizeLabel = BodyLabel(tr("home.text_size"), self)
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
        connectorSizeLabel = BodyLabel(tr("home.connector_size"), self)
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
        displayModeLabel = BodyLabel(tr("home.display_mode"), self)
        displayModeLabel.setFixedWidth(100)
        displayModeLayout.addWidget(displayModeLabel)
        self.countdownDisplayModeCombo = ComboBox(self)
        self.countdownDisplayModeCombo.addItems([tr("home.simultaneous"), tr("home.carousel")])
        self.countdownDisplayModeCombo.setCurrentText(tr("home.simultaneous") if cfg.countdownDisplayMode.value == 'simultaneous' else tr("home.carousel"))
        self.countdownDisplayModeCombo.setFixedWidth(120)
        self.countdownDisplayModeCombo.currentTextChanged.connect(self._onCountdownDisplayModeChanged)
        displayModeLayout.addWidget(self.countdownDisplayModeCombo)
        layout.addLayout(displayModeLayout)

        # 轮播间隔
        carouselIntervalLayout = QHBoxLayout()
        carouselIntervalLabel = BodyLabel(tr("home.carousel_interval"), self)
        carouselIntervalLabel.setFixedWidth(100)
        carouselIntervalLayout.addWidget(carouselIntervalLabel)
        self.countdownCarouselIntervalSpin = SpinBox(self)
        self.countdownCarouselIntervalSpin.setRange(1, 60)
        self.countdownCarouselIntervalSpin.setValue(cfg.countdownCarouselInterval.value)
        self.countdownCarouselIntervalSpin.setFixedWidth(120)
        self.countdownCarouselIntervalSpin.valueChanged.connect(self._onCountdownCarouselIntervalChanged)
        carouselIntervalLayout.addWidget(self.countdownCarouselIntervalSpin)
        layout.addLayout(carouselIntervalLayout)

        actionLayout = QHBoxLayout()
        actionLabel = BodyLabel(tr("home.countdown_actions"), self)
        actionLabel.setFixedWidth(100)
        actionLayout.addWidget(actionLabel)
        self.countdownAddButton = PushButton(FIF.ADD, tr("home.add"), self)
        self.countdownAddButton.clicked.connect(self._onCountdownAddClicked)
        actionLayout.addWidget(self.countdownAddButton)
        self.countdownEditButton = PushButton(FIF.EDIT, tr("home.edit"), self)
        self.countdownEditButton.clicked.connect(self._onCountdownEditClicked)
        actionLayout.addWidget(self.countdownEditButton)
        self.countdownDeleteButton = PushButton(FIF.DELETE, tr("home.delete"), self)
        self.countdownDeleteButton.clicked.connect(self._onCountdownDeleteClicked)
        actionLayout.addWidget(self.countdownDeleteButton)
        actionLayout.addStretch()
        layout.addLayout(actionLayout)

    def _createCountdownListCard(self, layout):
        self.countdownListCard = CardWidget(self)
        cardLayout = QVBoxLayout(self.countdownListCard)
        cardLayout.setContentsMargins(16, 12, 16, 12)
        cardLayout.setSpacing(10)

        listLabel = StrongBodyLabel(tr("home.countdown_list"), self)
        cardLayout.addWidget(listLabel)

        self.countdownListWidget = ListWidget(self.countdownListCard)
        self.countdownListWidget.setMinimumHeight(120)
        self._updateCountdownList()
        cardLayout.addWidget(self.countdownListWidget)

        layout.addWidget(self.countdownListCard)

    def _updateShowCountdownSwitch(self, value):
        self.showCountdownSwitch.setChecked(value)
        self._updateCountdownSettingsEnabled(value)

    def _updateCountdownDisplayModeCombo(self, value):
        try:
            self.countdownDisplayModeCombo.currentTextChanged.disconnect(self._onCountdownDisplayModeChanged)
        except TypeError:
            pass
        self.countdownDisplayModeCombo.setCurrentText(tr("home.simultaneous") if value == 'simultaneous' else tr("home.carousel"))
        self.countdownDisplayModeCombo.currentTextChanged.connect(self._onCountdownDisplayModeChanged)

    def _updateCountdownTextSizeSpin(self, value):
        self.countdownTextSizeSpin.setValue(value)

    def _updateCountdownConnectorSizeSpin(self, value):
        self.countdownConnectorSizeSpin.setValue(value)

    def _updateCountdownCarouselIntervalSpin(self, value):
        self.countdownCarouselIntervalSpin.setValue(value)

    def _updateCountdownTextColorCombo(self, value):
        self.countdownTextColorCombo.setCurrentText(self._getColorText(value, 'red'))

    def _updateCountdownConnectorColorCombo(self, value):
        self.countdownConnectorColorCombo.setCurrentText(self._getColorText(value, 'white'))

    def _updateShowSchoolInfoSwitch(self, value):
        self.schoolInfoSwitch.setChecked(value)
        self._updateSchoolInfoSettingsEnabled(value)

    def _updateSchoolInfoTextColorCombo(self, value):
        self.schoolInfoTextColorCombo.setCurrentText(self._getColorText(value, 'white'))

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
        except Exception:
            return ""

    def _updateCountdownList(self):
        if not hasattr(self, 'countdownListWidget') or self.countdownListWidget is None:
            return
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
        cfg.countdownDisplayMode.value = 'simultaneous' if text == tr("home.simultaneous") else 'carousel'
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
            InfoBar.warning(tr("home.edit_countdown"), tr("home.select_countdown_first"), parent=self, duration=3000)
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
            InfoBar.warning(tr("home.delete_countdown"), tr("home.select_countdown_first"), parent=self, duration=3000)
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

        if text == tr("color.red"):
            cfg.countdownTextColor.value = "#FF0000"
        elif text == tr("color.white"):
            cfg.countdownTextColor.value = "#FFFFFF"
        elif text == tr("color.black"):
            cfg.countdownTextColor.value = "#000000"
        else:
            cfg.countdownTextColor.value = cfg.themeColor.value.name() if hasattr(cfg.themeColor.value, 'name') else str(cfg.themeColor.value)

        if hasattr(self.mainWindow, 'updateCountdownStyle'):
            self.mainWindow.updateCountdownStyle()
        logger.info(f"倒计时设置：文字颜色={text}")

    def _onCountdownConnectorColorChanged(self, text: str):
        """倒计时连接词颜色变化"""

        if text == tr("color.red"):
            cfg.countdownConnectorColor.value = "#FF0000"
        elif text == tr("color.white"):
            cfg.countdownConnectorColor.value = "#FFFFFF"
        elif text == tr("color.black"):
            cfg.countdownConnectorColor.value = "#000000"
        else:
            cfg.countdownConnectorColor.value = cfg.themeColor.value.name() if hasattr(cfg.themeColor.value, 'name') else str(cfg.themeColor.value)

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

    def _onSchoolInfoTextColorChanged(self, text: str):

        if text == tr("color.white"):
            cfg.schoolInfoTextColor.value = "#FFFFFF"
        elif text == tr("color.black"):
            cfg.schoolInfoTextColor.value = "#000000"
        elif text == tr("color.red"):
            cfg.schoolInfoTextColor.value = "#FF0000"
        else:
            cfg.schoolInfoTextColor.value = cfg.themeColor.value.name() if hasattr(cfg.themeColor.value, 'name') else str(cfg.themeColor.value)

        if hasattr(self.mainWindow, 'updateSchoolInfoStyle'):
            self.mainWindow.updateSchoolInfoStyle()
        logger.info(f"学校信息：文字颜色={text}")

    def _onSchoolInfoTextSizeChanged(self, value: int):
        cfg.schoolInfoTextSize.value = value
        if hasattr(self.mainWindow, 'updateSchoolInfoStyle'):
            self.mainWindow.updateSchoolInfoStyle()
        logger.info(f"学校信息：文字大小={value}px")

    def _onShowQuickLaunchChanged(self, checked: bool):
        cfg.showQuickLaunch.value = checked
        save_cfg()
        if hasattr(self.mainWindow, '_MainWindow__updateQuickLaunch'):
            self.mainWindow._MainWindow__updateQuickLaunch()

    def _onQuickLaunchEditClicked(self):
        dialog = QuickLaunchEditDialog(self.mainWindow)
        dialog.exec()
        if hasattr(self.mainWindow, '_MainWindow__updateQuickLaunch'):
            self.mainWindow._MainWindow__updateQuickLaunch()

    def _onQuickLaunchIconSizeChanged(self, value: int):
        cfg.quickLaunchIconSize.value = value
        save_cfg()
        if hasattr(self.mainWindow, '_MainWindow__updateQuickLaunch'):
            self.mainWindow._MainWindow__updateQuickLaunch()

    def _onQuickLaunchIconSpacingChanged(self, value: int):
        cfg.quickLaunchIconSpacing.value = value
        save_cfg()
        if hasattr(self.mainWindow, '_MainWindow__updateQuickLaunch'):
            self.mainWindow._MainWindow__updateQuickLaunch()

    def _onQuickLaunchShowLabelsChanged(self, checked: bool):
        cfg.quickLaunchShowLabels.value = checked
        save_cfg()
        if hasattr(self.mainWindow, '_MainWindow__updateQuickLaunch'):
            self.mainWindow._MainWindow__updateQuickLaunch()

    def refreshQuickLaunchSettings(self):
        self.showQuickLaunchSwitch.setChecked(cfg.showQuickLaunch.value)
        self.quickLaunchIconSizeSpin.setValue(cfg.quickLaunchIconSize.value)
        self.quickLaunchIconSpacingSpin.setValue(cfg.quickLaunchIconSpacing.value)
        self.quickLaunchShowLabelsSwitch.setChecked(cfg.quickLaunchShowLabels.value)

    def refreshAllSettings(self):
        self.showClockSwitch.setChecked(cfg.showClock.value)
        self.showSecondsSwitch.setChecked(cfg.showClockSeconds.value)
        self.showLunarSwitch.setChecked(cfg.showLunarCalendar.value)
        self.clockColorCombo.setCurrentText(self._getColorText(cfg.clockColor.value))
        self.clockSizeSpin.setValue(cfg.clockSize.value)
        self.dateSizeSpin.setValue(cfg.dateSize.value)

        self.showPoetrySwitch.setChecked(cfg.showPoetry.value)
        if cfg.poetryApiUrl.value == 'https://www.ffapi.cn/int/v1/shici':
            self.poetryApiCombo.setCurrentText(tr("home.poetry_api"))
        else:
            self.poetryApiCombo.setCurrentText(tr("home.yiyan_api"))
        self.poetrySizeSpin.setValue(cfg.poetrySize.value)
        self.poetryUpdateIntervalCombo.setCurrentText(cfg.poetryUpdateInterval.value)

        self.showWeatherSwitch.setChecked(cfg.showWeather.value)
        self.cityButton.setText(cfg.city.value)
        self.weatherSizeSpin.setValue(cfg.weatherSize.value)
        self.weatherIconSizeSpin.setValue(cfg.weatherIconSize.value)
        self.weatherUpdateIntervalCombo.setCurrentText(cfg.weatherUpdateInterval.value)

        self.showCountdownSwitch.setChecked(cfg.showCountdown.value)
        self.countdownDisplayModeCombo.setCurrentText(tr("home.simultaneous") if cfg.countdownDisplayMode.value == 'simultaneous' else tr("home.carousel"))
        self.countdownTextSizeSpin.setValue(cfg.countdownTextSize.value)
        self.countdownConnectorSizeSpin.setValue(cfg.countdownConnectorSize.value)
        self.countdownCarouselIntervalSpin.setValue(cfg.countdownCarouselInterval.value)
        self.countdownTextColorCombo.setCurrentText(self._getColorText(cfg.countdownTextColor.value, 'red'))
        self.countdownConnectorColorCombo.setCurrentText(self._getColorText(cfg.countdownConnectorColor.value, 'white'))
        self._updateCountdownList()

        self.schoolInfoSwitch.setChecked(cfg.showSchoolInfo.value)
        self.schoolEdit.setText(cfg.school.value)
        self.schoolClassEdit.setText(cfg.schoolClass.value)
        self.schoolInfoTextColorCombo.setCurrentText(self._getColorText(cfg.schoolInfoTextColor.value, 'white'))
        self.schoolInfoTextSizeSpin.setValue(cfg.schoolInfoTextSize.value)

        self.refreshQuickLaunchSettings()
        self.refreshMediaSettings()

    def refreshMediaSettings(self):
        self.showMediaInfoSwitch.setChecked(cfg.showMediaInfo.value)
        self.showMediaCoverSwitch.setChecked(cfg.showMediaCover.value)
        self.mediaWidthSpin.setValue(cfg.mediaWidth.value)
        self.mediaLyricsAdvanceSpin.setValue(cfg.mediaLyricsAdvance.value)

    def _createSchoolInfoSettings(self, layout):
        """创建学校信息设置"""
        titleLabel = StrongBodyLabel(tr("home.school_info"), self)
        layout.addWidget(titleLabel)

        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel(tr("home.enable_school_info"), self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.schoolInfoSwitch = SwitchButton(self)
        self.schoolInfoSwitch.setChecked(cfg.showSchoolInfo.value)
        self.schoolInfoSwitch.checkedChanged.connect(self._onShowSchoolInfoChanged)
        enableLayout.addWidget(self.schoolInfoSwitch)
        layout.addLayout(enableLayout)

        schoolClassLayout = QHBoxLayout()
        schoolClassLabel = BodyLabel(tr("home.class_name"), self)
        schoolClassLabel.setFixedWidth(100)
        schoolClassLayout.addWidget(schoolClassLabel)
        self.schoolClassEdit = LineEdit(self)
        self.schoolClassEdit.setText(cfg.schoolClass.value)
        self.schoolClassEdit.setPlaceholderText(tr("home.class_name_example"))
        self.schoolClassEdit.setFixedWidth(120)
        self.schoolClassEdit.textChanged.connect(self._onSchoolClassChanged)
        schoolClassLayout.addWidget(self.schoolClassEdit)
        layout.addLayout(schoolClassLayout)

        schoolLayout = QHBoxLayout()
        schoolLabel = BodyLabel(tr("home.school_name"), self)
        schoolLabel.setFixedWidth(100)
        schoolLayout.addWidget(schoolLabel)
        self.schoolEdit = LineEdit(self)
        self.schoolEdit.setText(cfg.school.value)
        self.schoolEdit.setPlaceholderText(tr("home.school_name_example"))
        self.schoolEdit.setFixedWidth(120)
        self.schoolEdit.textChanged.connect(self._onSchoolChanged)
        schoolLayout.addWidget(self.schoolEdit)
        layout.addLayout(schoolLayout)

        textColorLayout = QHBoxLayout()
        textColorLabel = BodyLabel(tr("home.text_color"), self)
        textColorLabel.setFixedWidth(100)
        textColorLayout.addWidget(textColorLabel)
        self.schoolInfoTextColorCombo = ComboBox(self)
        self.schoolInfoTextColorCombo.addItems([tr("color.white"), tr("color.black"), tr("color.red"), tr("color.primary")])
        self.schoolInfoTextColorCombo.setCurrentText(self._getColorText(cfg.schoolInfoTextColor.value, 'white'))
        self.schoolInfoTextColorCombo.setFixedWidth(120)
        self.schoolInfoTextColorCombo.currentTextChanged.connect(self._onSchoolInfoTextColorChanged)
        textColorLayout.addWidget(self.schoolInfoTextColorCombo)
        layout.addLayout(textColorLayout)

        textSizeLayout = QHBoxLayout()
        textSizeLabel = BodyLabel(tr("home.text_size"), self)
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
        titleLabel = StrongBodyLabel(tr("home.quick_launch_bar"), self)
        layout.addWidget(titleLabel)

        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel(tr("home.enable_quick_launch"), self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.showQuickLaunchSwitch = SwitchButton(self)
        self.showQuickLaunchSwitch.setChecked(cfg.showQuickLaunch.value)
        self.showQuickLaunchSwitch.checkedChanged.connect(self._onShowQuickLaunchChanged)
        enableLayout.addWidget(self.showQuickLaunchSwitch)
        layout.addLayout(enableLayout)

        iconSizeLayout = QHBoxLayout()
        iconSizeLabel = BodyLabel(tr("home.icon_size"), self)
        iconSizeLabel.setFixedWidth(100)
        iconSizeLayout.addWidget(iconSizeLabel)
        self.quickLaunchIconSizeSpin = SpinBox(self)
        self.quickLaunchIconSizeSpin.setRange(32, 96)
        self.quickLaunchIconSizeSpin.setValue(cfg.quickLaunchIconSize.value)
        self.quickLaunchIconSizeSpin.setFixedWidth(120)
        self.quickLaunchIconSizeSpin.valueChanged.connect(lambda v: self._onQuickLaunchIconSizeChanged(v))
        iconSizeLayout.addWidget(self.quickLaunchIconSizeSpin)
        layout.addLayout(iconSizeLayout)

        iconSpacingLayout = QHBoxLayout()
        iconSpacingLabel = BodyLabel(tr("home.icon_spacing"), self)
        iconSpacingLabel.setFixedWidth(100)
        iconSpacingLayout.addWidget(iconSpacingLabel)
        self.quickLaunchIconSpacingSpin = SpinBox(self)
        self.quickLaunchIconSpacingSpin.setRange(4, 40)
        self.quickLaunchIconSpacingSpin.setValue(cfg.quickLaunchIconSpacing.value)
        self.quickLaunchIconSpacingSpin.setFixedWidth(120)
        self.quickLaunchIconSpacingSpin.valueChanged.connect(lambda v: self._onQuickLaunchIconSpacingChanged(v))
        iconSpacingLayout.addWidget(self.quickLaunchIconSpacingSpin)
        layout.addLayout(iconSpacingLayout)

        showLabelsLayout = QHBoxLayout()
        showLabelsLabel = BodyLabel(tr("home.show_name"), self)
        showLabelsLabel.setFixedWidth(100)
        showLabelsLayout.addWidget(showLabelsLabel)
        self.quickLaunchShowLabelsSwitch = SwitchButton(self)
        self.quickLaunchShowLabelsSwitch.setChecked(cfg.quickLaunchShowLabels.value)
        self.quickLaunchShowLabelsSwitch.setOffText(tr("common.off"))
        self.quickLaunchShowLabelsSwitch.setOnText(tr("common.on"))
        self.quickLaunchShowLabelsSwitch.checkedChanged.connect(lambda v: self._onQuickLaunchShowLabelsChanged(v))
        showLabelsLayout.addWidget(self.quickLaunchShowLabelsSwitch)
        layout.addLayout(showLabelsLayout)

        appsLayout = QHBoxLayout()
        appsLabel = BodyLabel(tr("home.app_management"), self)
        appsLabel.setFixedWidth(100)
        appsLayout.addWidget(appsLabel)
        self.quickLaunchEditButton = PushButton(tr("home.edit_apps"), self)
        self.quickLaunchEditButton.setFixedHeight(36)
        self.quickLaunchEditButton.setFixedWidth(120)
        self.quickLaunchEditButton.clicked.connect(self._onQuickLaunchEditClicked)
        appsLayout.addWidget(self.quickLaunchEditButton)
        layout.addLayout(appsLayout)

    def _createMediaSettings(self, layout):
        """创建媒体设置部分"""
        titleLabel = StrongBodyLabel(tr("home.media_info"), self)
        layout.addWidget(titleLabel)

        enableLayout = QHBoxLayout()
        enableLabel = BodyLabel(tr("home.enable_media"), self)
        enableLabel.setFixedWidth(100)
        enableLayout.addWidget(enableLabel)
        self.showMediaInfoSwitch = SwitchButton(self)
        self.showMediaInfoSwitch.setChecked(cfg.showMediaInfo.value)
        self.showMediaInfoSwitch.checkedChanged.connect(self._onShowMediaInfoChanged)
        enableLayout.addWidget(self.showMediaInfoSwitch)
        layout.addLayout(enableLayout)

        coverLayout = QHBoxLayout()
        coverLabel = BodyLabel(tr("home.show_cover"), self)
        coverLabel.setFixedWidth(100)
        coverLayout.addWidget(coverLabel)
        self.showMediaCoverSwitch = SwitchButton(self)
        self.showMediaCoverSwitch.setChecked(cfg.showMediaCover.value)
        self.showMediaCoverSwitch.checkedChanged.connect(self._onShowMediaCoverChanged)
        coverLayout.addWidget(self.showMediaCoverSwitch)
        layout.addLayout(coverLayout)

        widthLayout = QHBoxLayout()
        widthLabel = BodyLabel(tr("home.component_width"), self)
        widthLabel.setFixedWidth(100)
        widthLayout.addWidget(widthLabel)
        self.mediaWidthSpin = SpinBox(self)
        self.mediaWidthSpin.setRange(200, 800)
        self.mediaWidthSpin.setValue(cfg.mediaWidth.value)
        self.mediaWidthSpin.setFixedWidth(120)
        self.mediaWidthSpin.valueChanged.connect(self._onMediaWidthChanged)
        widthLayout.addWidget(self.mediaWidthSpin)
        layout.addLayout(widthLayout)

        lyricsAdvanceLayout = QHBoxLayout()
        lyricsAdvanceLabel = BodyLabel(tr("home.lyrics_advance"), self)
        lyricsAdvanceLabel.setFixedWidth(100)
        lyricsAdvanceLayout.addWidget(lyricsAdvanceLabel)
        self.mediaLyricsAdvanceSpin = SpinBox(self)
        self.mediaLyricsAdvanceSpin.setRange(0, 2000)
        self.mediaLyricsAdvanceSpin.setValue(cfg.mediaLyricsAdvance.value)
        self.mediaLyricsAdvanceSpin.setFixedWidth(120)
        self.mediaLyricsAdvanceSpin.valueChanged.connect(self._onMediaLyricsAdvanceChanged)
        lyricsAdvanceLayout.addWidget(self.mediaLyricsAdvanceSpin)
        layout.addLayout(lyricsAdvanceLayout)

    def _updateShowMediaInfoSwitch(self, value):
        self.showMediaInfoSwitch.setChecked(value)
        self._updateMediaSettingsEnabled(value)

    def _updateShowMediaCoverSwitch(self, value):
        self.showMediaCoverSwitch.setChecked(value)

    def _updateMediaWidthSpin(self, value):
        self.mediaWidthSpin.setValue(value)

    def _updateMediaLyricsAdvanceSpin(self, value):
        self.mediaLyricsAdvanceSpin.setValue(value)

    def _onShowMediaInfoChanged(self, checked: bool):
        cfg.showMediaInfo.value = checked
        self._updateMediaSettingsEnabled(checked)
        logger.info(f"媒体设置：启用媒体信息={'开启' if checked else '关闭'}")

    def _onShowMediaCoverChanged(self, checked: bool):
        cfg.showMediaCover.value = checked
        logger.info(f"媒体设置：显示封面={'开启' if checked else '关闭'}")

    def _onMediaWidthChanged(self, value: int):
        cfg.mediaWidth.value = value
        logger.info(f"媒体设置：组件宽度={value}px")

    def _onMediaLyricsAdvanceChanged(self, value: int):
        cfg.mediaLyricsAdvance.value = value
        logger.info(f"媒体设置：歌词提前时间={value}ms")


class CountdownEditDialog(MessageBoxBase):
    """倒计时编辑对话框"""

    def __init__(self, parent=None, countdown_data=None):
        super().__init__(parent)
        self._countdown_data = countdown_data
        self._result = None
        self._init_ui()

    def _init_ui(self):

        self.viewLayout.setSpacing(8)

        title = SubtitleLabel(tr("home.edit_countdown") if self._countdown_data else tr("home.add_countdown"))
        self.viewLayout.addWidget(title)
        infoLabel = BodyLabel(tr("home.countdown_description"))
        self.viewLayout.addWidget(infoLabel)

        titleLabel = BodyLabel(tr("home.target_name"))
        self.viewLayout.addWidget(titleLabel)
        self.titleEdit = LineEdit()
        self.titleEdit.setPlaceholderText(tr("home.target_name_example"))
        if self._countdown_data:
            self.titleEdit.setText(self._countdown_data.get('title', ''))
        self.viewLayout.addWidget(self.titleEdit)

        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)

        dateLabel = BodyLabel(tr("home.target_date"))
        self.viewLayout.addWidget(dateLabel)
        self.datePicker = CalendarPicker()
        if self._countdown_data:
            target_time = self._countdown_data.get('target_time', '')
            if target_time:
                try:
                    dt = datetime.datetime.strptime(target_time, '%Y-%m-%d %H:%M')
                    self.datePicker.setDate(QDate(dt.year, dt.month, dt.day))
                except Exception:
                    pass
        else:
            now = datetime.datetime.now()
            self.datePicker.setDate(QDate(now.year, now.month, now.day))
        self.viewLayout.addWidget(self.datePicker)

        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)

        timeLabel = BodyLabel(tr("home.target_time"))
        self.viewLayout.addWidget(timeLabel)
        self.timePicker = TimePicker()
        if self._countdown_data:
            target_time = self._countdown_data.get('target_time', '')
            if target_time:
                try:
                    dt = datetime.datetime.strptime(target_time, '%Y-%m-%d %H:%M')
                    self.timePicker.setTime(QTime(dt.hour, dt.minute))
                except Exception:
                    pass
        else:
            self.timePicker.setTime(QTime(0, 0))
        self.viewLayout.addWidget(self.timePicker)

        self.yesButton.setText(tr("common.confirm"))
        self.cancelButton.setText(tr("common.cancel"))

        self.widget.setMinimumWidth(360)

        try:
            self.yesButton.clicked.disconnect()
        except TypeError:
            pass
        self.yesButton.clicked.connect(self._on_ok)

    def _on_ok(self):
        try:
            title_text = self.titleEdit.text().strip()
            if not title_text:
                InfoBar.error(tr("common.error"), tr("home.enter_target_name"), parent=self, duration=3000)
                return

            qdate = self.datePicker.date
            qtime = self.timePicker.time
            if not qdate.isValid() or not qtime.isValid():
                InfoBar.error(tr("common.error"), tr("home.enter_valid_datetime"), parent=self, duration=3000)
                return
            dt = datetime.datetime(qdate.year(), qdate.month(), qdate.day(), qtime.hour(), qtime.minute())
            self._result = {
                'title': title_text,
                'target_time': dt.strftime('%Y-%m-%d %H:%M')
            }
            self.accept()
        except Exception as e:
            logger.error(f'保存倒计时失败：{e}')
            InfoBar.error(tr("common.error"), tr("home.enter_valid_datetime_error", error=str(e)), parent=self, duration=5000)

    def get_countdown(self):
        return self._result


class QuickLaunchEditDialog(MessageBoxBase):
    """快捷启动栏编辑对话框"""

    def __init__(self, parent=None):
        super().__init__(parent)
        apps = cfg.quickLaunchApps.value
        self._apps = list(apps) if apps else []
        self._init_ui()

    def _init_ui(self):
        self.viewLayout.setSpacing(8)
        title = SubtitleLabel(tr("home.edit_quick_launch"))
        self.viewLayout.addWidget(title)
        infoLabel = BodyLabel(tr("home.quick_launch_description"))
        self.viewLayout.addWidget(infoLabel)

        self.appListWidget = ListWidget(self)
        self.appListWidget.setFixedHeight(200)
        self.appListWidget.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        self.appListWidget.itemClicked.connect(self._on_item_clicked)
        self._update_app_list()
        self.viewLayout.addWidget(self.appListWidget)

        buttonLayout = QHBoxLayout()
        self.addButton = PushButton(tr("home.add_app"), self)
        self.addButton.clicked.connect(self._on_add_app)
        buttonLayout.addWidget(self.addButton)

        self.editButton = PushButton(tr("home.edit"), self)
        self.editButton.clicked.connect(self._on_edit_app)
        buttonLayout.addWidget(self.editButton)

        self.deleteButton = PushButton(tr("home.delete"), self)
        self.deleteButton.clicked.connect(self._on_delete_app)
        buttonLayout.addWidget(self.deleteButton)

        self.viewLayout.addLayout(buttonLayout)

        self.yesButton.setText(tr("common.done"))
        self.cancelButton.setText(tr("common.cancel"))
        self.widget.setMinimumWidth(400)

        self._selected_row = -1
        self.setAcceptDrops(True)

    def _on_item_clicked(self, item):
        self._selected_row = self.appListWidget.row(item)

    def _update_app_list(self):
        self.appListWidget.clear()
        for app in self._apps:
            name = app.get('name', tr("common.unknown"))
            path = app.get('path', '')
            display_text = f"{name} - {path if path else tr('home.no_path_configured')}"
            self.appListWidget.addItem(display_text)

    def _on_add_app(self):
        if len(self._apps) >= QuickLaunchDock.MAX_APPS:
            InfoBar.warning(tr("common.tip"), tr("home.max_apps_warning", max=QuickLaunchDock.MAX_APPS), parent=self, duration=3000)
            return
        dialog = AppEditDialog(self.parent())
        if dialog.exec():
            app_data = dialog.get_app_data()
            if app_data:
                self._apps.append(app_data)
                self._update_app_list()
                self._refresh_dock()

    def _on_edit_app(self):
        if self._selected_row < 0 or self._selected_row >= len(self._apps):
            InfoBar.warning(tr("common.tip"), tr("home.select_app_first"), parent=self, duration=2000)
            return

        dialog = AppEditDialog(self.parent(), self._apps[self._selected_row])
        if dialog.exec():
            app_data = dialog.get_app_data()
            if app_data:
                self._apps[self._selected_row] = app_data
                self._update_app_list()
                if 0 <= self._selected_row < self.appListWidget.count():
                    self.appListWidget.setCurrentRow(self._selected_row)
                self._refresh_dock()

    def _on_delete_app(self):
        if self._selected_row < 0 or self._selected_row >= len(self._apps):
            InfoBar.warning(tr("common.tip"), tr("home.select_app_first"), parent=self, duration=2000)
            return

        deleted_app = self._apps.pop(self._selected_row)
        self._delete_app_icon(deleted_app)
        self._update_app_list()
        if self.appListWidget.count() > 0:
            new_row = min(self._selected_row, self.appListWidget.count() - 1)
            self.appListWidget.setCurrentRow(new_row)
            self._selected_row = new_row
        self._refresh_dock()

    def _delete_app_icon(self, app_data):
        if not app_data:return
        icon_filename = app_data.get('icon', '')
        if not icon_filename or icon_filename in ('exe.ico', 'default.ico'):return

        base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        icon_path = os.path.join(base_dir, 'data', 'ql_icon', icon_filename)
        if os.path.exists(icon_path):
            try:
                os.remove(icon_path)
                logger.info(f"已删除图标文件：{icon_path}")
            except Exception as e:
                logger.warning(f"删除图标文件失败：{e}")

    def _refresh_dock(self):
        """刷新 dock 栏显示"""
        if hasattr(self, 'mainWindow') and hasattr(self.mainWindow, '_MainWindow__updateQuickLaunch'):
            self.mainWindow._MainWindow__updateQuickLaunch()

    def dragEnterEvent(self, e):
        if e.mimeData().hasUrls():
            urls = e.mimeData().urls()
            for url in urls:
                path = url.toLocalFile()
                if path and (path.lower().endswith('.exe') or path.lower().endswith('.lnk')):
                    e.acceptProposedAction()
                    return
        e.ignore()

    def dragMoveEvent(self, e):
        if e.mimeData().hasUrls():
            e.acceptProposedAction()
        else:
            e.ignore()

    def dropEvent(self, e):
        e.acceptProposedAction()
        urls = e.mimeData().urls()
        for url in urls:
            path = url.toLocalFile()
            if not path:
                continue
            if not (path.lower().endswith('.exe') or path.lower().endswith('.lnk')):
                continue
            if len(self._apps) >= QuickLaunchDock.MAX_APPS:
                InfoBar.warning(tr("common.tip"), tr("home.max_apps_warning", max=QuickLaunchDock.MAX_APPS), parent=self, duration=3000)
                return
            app_data = resolve_app_from_path(path)
            if app_data:
                self._apps.append(app_data)
                self._update_app_list()
                self._refresh_dock()

    def accept(self):
        cfg.quickLaunchApps.value = self._apps
        save_cfg()
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

        title = SubtitleLabel(tr("home.edit_app") if self._app_data else tr("home.add_app"))
        self.viewLayout.addWidget(title)

        descLabel = BodyLabel(tr("home.app_config_description"))
        self.viewLayout.addWidget(descLabel)

        nameLabel = BodyLabel(tr("home.app_name"))
        self.viewLayout.addWidget(nameLabel)
        self.nameEdit = LineEdit(self)
        self.nameEdit.setPlaceholderText(tr("home.app_name_example"))
        if self._app_data:self.nameEdit.setText(self._app_data.get('name', ''))
        self.viewLayout.addWidget(self.nameEdit)
        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)

        pathLabel = BodyLabel(tr("home.app_path"))
        self.viewLayout.addWidget(pathLabel)
        pathLayout = QHBoxLayout()
        self.pathEdit = LineEdit(self)
        self.pathEdit.setPlaceholderText(tr("home.app_path_example"))
        if self._app_data:self.pathEdit.setText(self._app_data.get('path', ''))
        self.pathEdit.textChanged.connect(self._on_path_changed)
        pathLayout.addWidget(self.pathEdit)
        self.browseButton = PushButton(tr("common.browse"), self)
        self.browseButton.setFixedWidth(60)
        self.browseButton.clicked.connect(self._on_browse)
        pathLayout.addWidget(self.browseButton)
        self.viewLayout.addLayout(pathLayout)
        spacer = QWidget()
        spacer.setFixedHeight(8)
        self.viewLayout.addWidget(spacer)

        iconPathLabel = BodyLabel(tr("home.icon_path"))
        self.viewLayout.addWidget(iconPathLabel)
        iconInputLayout = QHBoxLayout()
        self.iconPreviewLabel = QLabel(self)
        self.iconPreviewLabel.setObjectName("iconPreviewLabel")
        self.iconPreviewLabel.setFixedSize(48, 48)
        self.iconPreviewLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._set_default_icon()
        iconInputLayout.addWidget(self.iconPreviewLabel)
        self.iconPathEdit = LineEdit(self)
        self.iconPathEdit.setPlaceholderText(tr("home.icon_path_placeholder"))
        if self._app_data:self.iconPathEdit.setText(self._app_data.get('icon', ''))
        self.iconPathEdit.textChanged.connect(self._on_icon_path_changed)
        iconInputLayout.addWidget(self.iconPathEdit)
        self.iconBrowseButton = PushButton(tr("common.browse"), self)
        self.iconBrowseButton.setFixedWidth(60)
        self.iconBrowseButton.clicked.connect(self._on_icon_browse)
        iconInputLayout.addWidget(self.iconBrowseButton)
        self.viewLayout.addLayout(iconInputLayout)

        self.yesButton.setText(tr("common.confirm"))
        self.cancelButton.setText(tr("common.cancel"))
        self.widget.setMinimumWidth(400)

        try:
            self.yesButton.clicked.disconnect()
        except TypeError:
            pass
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
        icon_path = get_software_icon_path(icon_filename)
        if os.path.exists(icon_path):
            pixmap = QPixmap(icon_path)
            if not pixmap.isNull():
                scaled = pixmap.scaled(36, 36, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)
                self.iconPreviewLabel.setPixmap(scaled)
            else:
                self._set_default_icon()
        else:
            self._set_default_icon()

    def _extract_icon(self, exe_path):
        try:
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
                pixmap = pixmap.scaled(target_size, target_size, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)

            icon_filename = self._get_icon_name()
            icon_dir = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'data', 'ql_icon')
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

        file_path, _ = QFileDialog.getOpenFileName(
            self,
            '选择图标',
            '',
            'Image Files (*.ico *.png *.jpg *.jpeg *.bmp);;All Files (*)'
        )

        if file_path:
            self.iconPathEdit.setText(file_path)

    def _on_browse(self):

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
