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
UI组件模块
"""

import logging
import os
import re
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from typing import Optional

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

import pythoncom
import webbrowser

from PyQt6.QtCore import (
    QFileInfo,
    QPointF,
    QPoint,
    QRectF,
    Qt,
    QThread,
    QTimer,
    pyqtProperty,
    QSize,
    pyqtSignal, QObject,
    QByteArray, QPropertyAnimation, QEasingCurve
)
from PyQt6.QtGui import (
    QBrush,
    QColor,
    QCursor,
    QFont,
    QFontMetrics,
    QLinearGradient,
    QPainter,
    QPainterPath,
    QPen,
    QPixmap, QIcon
)
from PyQt6.QtWidgets import (
    QFileIconProvider, QLabel, QSizePolicy, QWidget, QVBoxLayout, QHBoxLayout, QApplication, QProgressBar, QFrame, QGraphicsOpacityEffect
)
from qfluentwidgets import InfoBar, isDarkTheme, RoundMenu, Action, FluentIcon as FIF
from win32com.shell import shell, shellcon

from core.config import cfg, save_cfg
from services.media import MediaInfo, Lyrics, get_media_info, fetch_all_info, close as close_media
from core.constants import load_qss
from data.software_list import get_software_icon_path

logger = logging.getLogger("ClassLively.ui.component")


class DraggableWidget(QWidget):
    positionChanged = pyqtSignal(float, float)
    settingRequested = pyqtSignal(str)
    def __init__(self, parent=None, component_id: str = ""):
        super().__init__(parent)
        self.component_id = component_id
        
        self._dragging = False
        self._drag_start_pos = QPoint()
        self._widget_start_pos = QPoint()
        self._click_start_pos = QPoint()
        
        self._percent_x = 0.5
        self._percent_y = 0.5
        
        self._draggable = False
        self._anchor_mode = "topleft"
        
        self._show_border = False
        self._border_color = QColor(120, 120, 120)
        self._hovered = False
        self._cached_primary_color = QColor(48, 195, 97)
        self._cached_hover_color = QColor(108, 255, 157)
        
        self.setMouseTracking(True)
        self.setAutoFillBackground(False)
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Preferred)
    
    def setDraggable(self, enabled: bool):
        self._draggable = enabled
        self._show_border = enabled
        self.update()
        if enabled:
            self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            self.raise_()
        else:
            self.setCursor(QCursor(Qt.CursorShape.ArrowCursor))
    
    def setPositionPercent(self, x: float, y: float):
        self._percent_x = max(0.0, min(1.0, x))
        self._percent_y = max(0.0, min(1.0, y))
        self._updatePositionFromPercent()
    
    def getPositionPercent(self) -> tuple:
        return (self._percent_x, self._percent_y)
    
    def setAnchorMode(self, mode: str):
        valid_modes = ["topleft", "top", "topright", "left", "center", "right", 
                       "bottomleft", "bottom", "bottomright"]
        if mode in valid_modes:self._anchor_mode = mode
    
    def _updatePositionFromPercent(self):
        parent = self.parentWidget()
        if not parent:return
        parent_rect = parent.rect()
        widget_size = self.size()
        available_width = parent_rect.width() - widget_size.width()
        available_height = parent_rect.height() - widget_size.height()
        if available_width > 0 and available_height > 0:
            x = int(available_width * self._percent_x)
            y = int(available_height * self._percent_y)
            
            if self._anchor_mode == "topright":
                x = int(parent_rect.width() - widget_size.width() * (1 + (1 - self._percent_x)))
            elif self._anchor_mode == "bottomleft":
                y = int(parent_rect.height() - widget_size.height() * (1 + (1 - self._percent_y)))
            elif self._anchor_mode == "bottomright":
                x = int(parent_rect.width() - widget_size.width() * (1 + (1 - self._percent_x)))
                y = int(parent_rect.height() - widget_size.height() * (1 + (1 - self._percent_y)))
            elif self._anchor_mode == "center":
                x = int((parent_rect.width() - widget_size.width()) / 2)
                y = int((parent_rect.height() - widget_size.height()) / 2)
            
            self.move(x, y)
    
    def _calculatePercentFromPosition(self) -> tuple:
        parent = self.parentWidget()
        if not parent:return (self._percent_x, self._percent_y)
        parent_rect = parent.rect()
        widget_geom = self.geometry()
        available_width = parent_rect.width() - widget_geom.width()
        available_height = parent_rect.height() - widget_geom.height()
        if available_width > 0:
            percent_x = widget_geom.x() / available_width
        else:
            percent_x = 0.5
        
        if available_height > 0:
            percent_y = widget_geom.y() / available_height
        else:
            percent_y = 0.5
        
        return (max(0.0, min(1.0, percent_x)), max(0.0, min(1.0, percent_y)))
    
    def paintEvent(self, event):
        super().paintEvent(event)
        
        if self._dragging:return
        if self._show_border or self._hovered:
            painter = QPainter(self)
            painter.setRenderHint(painter.RenderHint.Antialiasing)
            if self._hovered:
                pen_width = 1
                border_color = self._cached_hover_color
                pen_style = Qt.PenStyle.DashLine
            else:
                pen_width = 1
                border_color = QColor(160, 160, 160)
                pen_style = Qt.PenStyle.DashLine
            
            pen = QPen(border_color)
            pen.setWidth(pen_width)
            pen.setStyle(pen_style)
            painter.setPen(pen)
            rect = self.rect().adjusted(1, 1, -1, -1)
            painter.drawRoundedRect(rect, 6, 6)
            
            if self._show_border:
                painter.setPen(QColor(200, 200, 200))
                font = QFont()
                font.setPointSize(8)
                painter.setFont(font)
                label_text = f"⚙ {self.component_id}  点击设置"
                painter.drawText(8, 18, label_text)
            
            painter.end()
    
    def updateThemeColor(self):
        theme_color = cfg.themeColor.value
        if isinstance(theme_color, str):
            primary_color = QColor(theme_color)
        else:
            primary_color = theme_color
        self._cached_primary_color = primary_color
        self._cached_hover_color = QColor(
            min(255, primary_color.red() + 60),
            min(255, primary_color.green() + 60),
            min(255, primary_color.blue() + 60)
        )
        self.update()
    
    def enterEvent(self, event):
        if self._draggable:
            self._hovered = True
            self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            self.update()
        super().enterEvent(event)
    
    def leaveEvent(self, event):
        """鼠标离开事件"""
        self._hovered = False
        if self._draggable and not self._dragging:self.setCursor(QCursor(Qt.CursorShape.ArrowCursor))
        self.update()
        super().leaveEvent(event)
    
    def mousePressEvent(self, event):
        if self._draggable and event.button() == Qt.MouseButton.LeftButton:
            self._dragging = True
            self._drag_start_pos = event.globalPosition().toPoint()
            self._widget_start_pos = self.pos()
            self._click_start_pos = event.globalPosition().toPoint()
            self.setCursor(QCursor(Qt.CursorShape.ClosedHandCursor))
            self.update()
            self.raise_()
            event.accept()
            return
        
        super().mousePressEvent(event)
    
    def mouseDoubleClickEvent(self, event):
        if self._draggable and event.button() == Qt.MouseButton.LeftButton:
            self._dragging = False
            self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            self.settingRequested.emit(self.component_id)
            event.accept()
            return
        super().mouseDoubleClickEvent(event)
    
    def mouseMoveEvent(self, event):
        if self._dragging and self._draggable:
            current_pos = event.globalPosition().toPoint()
            delta = current_pos - self._drag_start_pos
            new_pos = self._widget_start_pos + delta
            parent = self.parentWidget()
            if parent:
                parent_rect = parent.rect()
                new_pos.setX(max(0, min(new_pos.x(), parent_rect.width() - self.width())))
                new_pos.setY(max(0, min(new_pos.y(), parent_rect.height() - self.height())))
            
            main_window = self._getMainWindow()
            align_lines = []
            if main_window and hasattr(main_window, '_computeSnap'):
                snapped_x, snapped_y, align_lines = main_window._computeSnap(
                    new_pos.x(), new_pos.y(), self.width(), self.height(), self
                )
                new_pos.setX(snapped_x)
                new_pos.setY(snapped_y)
            
            self.move(new_pos)
            self._percent_x, self._percent_y = self._calculatePercentFromPosition()
            self.positionChanged.emit(self._percent_x, self._percent_y)
            
            if main_window and hasattr(main_window, '_guideOverlay') and main_window._guideOverlay:
                main_window._guideOverlay.setAlignLines(align_lines)
            
            event.accept()
            return
        
        super().mouseMoveEvent(event)
    
    def _getMainWindow(self):
        widget = self.parentWidget()
        while widget:
            if hasattr(widget, '_computeSnap'):
                return widget
            widget = widget.parentWidget()
        return None
    
    def mouseReleaseEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton and self._dragging:
            self._dragging = False
            if self._draggable:
                self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            else:
                self.setCursor(QCursor(Qt.CursorShape.ArrowCursor))
            
            self.update()

            main_window = self._getMainWindow()
            if main_window and hasattr(main_window, 'clearDragAlignLines'):
                main_window.clearDragAlignLines()

            if self._draggable and hasattr(self, '_click_start_pos'):
                delta = event.globalPosition().toPoint() - self._click_start_pos
                if abs(delta.x()) < 5 and abs(delta.y()) < 5:
                    self.settingRequested.emit(self.component_id)
            
            event.accept()
            return
        
        super().mouseReleaseEvent(event)
    
    def onParentResize(self):
        self._updatePositionFromPercent()


class DraggableContainer(DraggableWidget):
    
    def __init__(self, parent=None, component_id: str = "", layout_direction: str = "vertical"):
        super().__init__(parent, component_id)
        
        if layout_direction == "vertical":
            self.inner_layout = QVBoxLayout(self)
        else:
            self.inner_layout = QHBoxLayout(self)
        
        self.inner_layout.setContentsMargins(10, 10, 10, 10)
        self.inner_layout.setSpacing(5)
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Preferred)
        self._content_visible = True
    
    def setContentVisible(self, visible: bool):
        """隐藏/显示内容"""
        self._content_visible = visible
        for i in range(self.inner_layout.count()):
            item = self.inner_layout.itemAt(i)
            if item and item.widget():
                item.widget().setVisible(visible)
        if visible:
            self.inner_layout.setContentsMargins(10, 10, 10, 10)
            self.setMinimumSize(80, 40)
            self.setMaximumSize(16777215, 16777215)
            self.inner_layout.activate()
            self.adjustSize()
        else:
            self.inner_layout.setContentsMargins(16, 10, 16, 10)
            name_map = {
                "clock": "时钟", "weather": "天气", "poetry": "一言",
                "countdown": "倒计时", "school_info": "学校信息",
                "media": "媒体信息", "quick_launch": "快捷启动",
            }
            display_name = name_map.get(self.component_id, self.component_id)
            text = f"⚙ {display_name} 点击设置"
            font = QFont()
            font.setPointSize(8)
            fm = QFontMetrics(font)
            text_width = fm.horizontalAdvance(text) + 24
            self.setFixedSize(max(text_width, 100), 36)
        self.updateGeometry()
        self.update()
    
    def addWidget(self, widget):
        self.inner_layout.addWidget(widget)
        self.inner_layout.activate()
        self.adjustSize()
        self.updateGeometry()
    
    def updateSize(self):
        self.inner_layout.activate()
        self.adjustSize()
        self.updateGeometry()
    
    def showEvent(self, event):
        super().showEvent(event)
        if self.inner_layout:
            self.inner_layout.activate()
            self.adjustSize()
    
    def sizeHint(self) -> QSize:
        if self.inner_layout:self.inner_layout.activate()
        base_size = super().sizeHint()
        if not self._content_visible:
            name_map = {
                "clock": "时钟", "weather": "天气", "poetry": "一言",
                "countdown": "倒计时", "school_info": "学校信息",
                "media": "媒体信息", "quick_launch": "快捷启动",
            }
            display_name = name_map.get(self.component_id, self.component_id)
            text = f"⚙ {display_name} 点击设置"
            font = QFont()
            font.setPointSize(8)
            fm = QFontMetrics(font)
            return QSize(max(fm.horizontalAdvance(text) + 24, 100), 36)
        return base_size
    
    def minimumSizeHint(self) -> QSize:
        if not self._content_visible:
            return self.sizeHint()
        return QSize(80, 40)
    
    def paintEvent(self, event):
        if not self._content_visible:
            painter = QPainter(self)
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            pen = QPen(QColor(160, 160, 160))
            pen.setWidth(1)
            pen.setStyle(Qt.PenStyle.DashLine)
            painter.setPen(pen)
            rect = self.rect().adjusted(1, 1, -1, -1)
            painter.drawRoundedRect(rect, 6, 6)
            painter.setPen(QColor(200, 200, 200))
            font = QFont()
            font.setPointSize(8)
            painter.setFont(font)
            name_map = {
                "clock": "时钟", "weather": "天气", "poetry": "一言",
                "countdown": "倒计时", "school_info": "学校信息",
                "media": "媒体信息", "quick_launch": "快捷启动",
            }
            display_name = name_map.get(self.component_id, self.component_id)
            label_text = f"⚙ {display_name} 点击设置"
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, label_text)
            painter.end()
            return
        super().paintEvent(event)



class LyricsWidget(QWidget):
    """歌词显示控件"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self._original_text = ""
        self._text_size = 14
        self._lyrics = None
        self._lyrics_color = "#FFFFFFB3"

        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)
        self.setFixedHeight(self._text_size + 8)

    def set_text_size(self, size: int):
        self._text_size = max(size, 10)
        self.setFixedHeight(self._text_size + 8)
        self.update()

    def set_visible_lines(self, n: int):
        pass

    def set_lyrics(self, lyrics):
        self._lyrics = lyrics
        if lyrics and not lyrics.is_empty() and lyrics.lines:
            line = lyrics.lines[0]
            text = line.text if line else ""
        else:
            text = ""
        self._update_text(text)

    def set_lyrics_color(self, color: str):
        self._lyrics_color = color
        self.update()

    def update_position(self, ms: int):
        if not self._lyrics or self._lyrics.is_empty():
            return
        advance = cfg.mediaLyricsAdvance.value
        adjusted_ms = ms + advance
        _, idx = self._lyrics.get_line_at_time(adjusted_ms)
        if idx >= 0 and idx < len(self._lyrics.lines):
            text = self._lyrics.lines[idx].text
        else:
            text = ""
        self._update_text(text)

    def _update_text(self, text):
        self._original_text = text
        self.update()

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setRenderHint(QPainter.RenderHint.TextAntialiasing)

        if not self._original_text:
            return

        font = QFont("HarmonyOS Sans", self._text_size)
        font.setWeight(QFont.Weight.DemiBold)
        p.setFont(font)

        fm = p.fontMetrics()
        available = max(self.width() - 4, 0)
        elided = fm.elidedText(self._original_text, Qt.TextElideMode.ElideRight, available)

        lyrics_color = QColor(self._lyrics_color)
        p.setPen(lyrics_color)
        p.drawText(0, 0, self.width(), self.height(),
                   Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
                   elided)

    def clear(self):
        self._original_text = ""
        self._lyrics = None
        self.update()


class MediaProgressBar(QProgressBar):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTextVisible(False)
        self.apply_style()

    def apply_style(self):
        height = cfg.mediaProgressHeight.value
        self.setFixedHeight(height)

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(Qt.PenStyle.NoPen)

        track_color = QColor(cfg.mediaProgressTrackColor.value)
        p.setBrush(track_color)
        h = self.height()
        p.drawRoundedRect(0, 1, self.width(), h - 2, 3, 3)

        if self.value() > 0:
            w = int(self.width() * self.value() / self.maximum()) if self.maximum() > 0 else 0
            # allow parent widget to override progress color (e.g. follow wallpaper)
            progress_color = QColor(cfg.mediaProgressColor.value)
            parent = getattr(self, 'parent', None)
            try:
                parent_widget = self.parent()
            except Exception:
                parent_widget = None
            if parent_widget and hasattr(parent_widget, '_override_progress_color') and parent_widget._override_progress_color:
                progress_color = parent_widget._override_progress_color
            p.setBrush(progress_color)
            p.drawRoundedRect(0, 1, w, h - 2, 3, 3)


class FetchWorker(QObject):
    finished = pyqtSignal(dict)
    def __init__(self, title: str, artist: str):
        super().__init__()
        self.title = title
        self.artist = artist
    
    def run(self):
        try:
            info = fetch_all_info(self.title, self.artist)
            self.finished.emit(info)
        except Exception as e:
            logger.debug(f"获取歌曲信息失败: {e}")
            self.finished.emit({})


class MediaWidget(QWidget):
    """媒体信息显示控件"""

    _fetch_done = pyqtSignal(object, bool)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("mediaWidget")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)

        self._media: Optional[MediaInfo] = None
        self._lyrics: Optional[Lyrics] = None
        self._last_ta = ""
        self._cover: Optional[QPixmap] = None
        self._has_gstmtc_cover: bool = False
        self._fetching = False
        self._pending_full = False
        self._cache = {}
        self._duration = 0
        self._position = 0
        self._playing = False
        self._thread = None
        self._worker = None
        self._info_cache = {}
        self._rapid_update_count = 0
        self._normal_interval = cfg.mediaUpdateInterval.value * 1000

        self._init_ui()
        self._setup_timers()
        self._apply_config()
        self._init_cover_animation()
        self._fetch_done.connect(self._on_fetched)

    @staticmethod
    def _qss_color(color_val):
        if isinstance(color_val, QColor):
            c = color_val
        elif color_val == "primary":
            from qfluentwidgets import Theme, isDarkTheme
            c = QColor(0, 0, 0) if not isDarkTheme() else QColor(255, 255, 255)
        else:
            if hasattr(color_val, 'name'):
                color_val = color_val.name()
            c = QColor(color_val)
        return f"rgba({c.red()}, {c.green()}, {c.blue()}, {round(c.alpha() / 255 * 100) / 100})"

    def _init_ui(self):
        self.setStyleSheet(load_qss('home.qss'))

        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(16, 14, 16, 12)
        main_layout.setSpacing(0)

        top_row = QHBoxLayout()
        top_row.setSpacing(18)
        top_row.setContentsMargins(0, 0, 0, 0)

        self._cover_lbl = QLabel()
        self._cover_lbl.setObjectName("mediaCoverLabel")
        self._cover_lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._default_cover()
        top_row.addWidget(self._cover_lbl, 0, Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)

        right_col = QVBoxLayout()
        right_col.setSpacing(6)
        right_col.setContentsMargins(0, 2, 0, 0)

        self._title = QLabel("未在播放")
        self._title.setObjectName("mediaTitleLabel")
        self._title.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        right_col.addWidget(self._title)

        self._artist = QLabel("")
        self._artist.setObjectName("mediaArtistLabel")
        self._artist.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        right_col.addWidget(self._artist)

        self._lyrics_w = LyricsWidget()
        self._lyrics_w.setObjectName("mediaLyricsWidget")
        self._lyrics_w.set_visible_lines(1)
        right_col.addWidget(self._lyrics_w)

        prog = QWidget()
        prog.setObjectName("mediaProgressContainer")
        pl = QHBoxLayout(prog)
        pl.setContentsMargins(0, 4, 0, 0)
        pl.setSpacing(8)

        self._time = QLabel("0:00")
        self._time.setObjectName("mediaTimeLabel")
        self._time.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        pl.addWidget(self._time)

        self._bar = MediaProgressBar()
        self._bar.setRange(0, 100)
        pl.addWidget(self._bar, 1)

        self._dur = QLabel("0:00")
        self._dur.setObjectName("mediaDurationLabel")
        self._dur.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        pl.addWidget(self._dur)

        self._prog_container = prog
        right_col.addWidget(self._prog_container)

        top_row.addLayout(right_col, 1)
        main_layout.addLayout(top_row)

        self.setSizePolicy(QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Fixed)
        
    def _get_content_height(self) -> int:
        return cfg.mediaHeight.value

    def _default_cover(self):
        sz = cfg.mediaCoverSize.value
        radius = cfg.mediaCoverBorderRadius.value
        pm = QPixmap(sz, sz)
        pm.fill(Qt.GlobalColor.transparent)
        p = QPainter(pm)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(Qt.PenStyle.NoPen)

        border_color = QColor(cfg.mediaCoverBorderColor.value)
        p.setBrush(border_color)
        p.drawRoundedRect(0, 0, sz, sz, radius, radius)

        inner_color = QColor(255, 255, 255, 25)
        p.setBrush(inner_color)
        p.drawRoundedRect(2, 2, sz - 4, sz - 4, max(radius - 2, 2), max(radius - 2, 2))

        shadow_color = QColor(0, 0, 0, 40)
        for i in range(3):
            p.setPen(Qt.PenStyle.NoPen)
            p.setBrush(QColor(0, 0, 0, 15 - i * 4))
            offset = (i + 1) * 2
            p.drawRoundedRect(offset, offset, sz, sz, radius, radius)
        p.end()
        self._cover_lbl.setPixmap(pm)

    def _setup_timers(self):
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._update)
        self._prog_timer = QTimer(self)
        self._prog_timer.timeout.connect(self._update_progress)

    def _apply_config(self):
        sz = max(cfg.mediaTextSize.value, 10)
        title_color = self._qss_color(cfg.mediaTitleColor.value)
        artist_color = self._qss_color(cfg.mediaArtistColor.value)
        time_color = self._qss_color(cfg.mediaTimeColor.value)
        lyrics_color = cfg.mediaLyricsColor.value

        self._title.setStyleSheet(
            f"font-size: {sz + 2}px; font-weight: 600;"
            f"color: {title_color};"
            f"font-family: 'HarmonyOS Sans', 'Microsoft YaHei', 'SimHei', sans-serif;"
        )
        self._artist.setStyleSheet(
            f"font-size: {sz}px;"
            f"color: {artist_color};"
            f"font-family: 'HarmonyOS Sans', 'Microsoft YaHei', 'SimHei', sans-serif;"
        )
        self._time.setStyleSheet(f"color: {time_color};")
        self._dur.setStyleSheet(f"color: {time_color};")
        self._lyrics_w.set_text_size(cfg.mediaLyricsSize.value)
        self._lyrics_w.set_visible_lines(1)
        self._lyrics_w.set_lyrics_color(lyrics_color)

        cover_sz = cfg.mediaCoverSize.value
        self._cover_lbl.setFixedSize(cover_sz, cover_sz)
        if self._cover and not self._cover.isNull():
            cover_with_shadow = self._add_cover_shadow(self._cover, cover_sz)
            self._cover_lbl.setPixmap(cover_with_shadow)
        else:
            self._default_cover()

        self._bar.apply_style()
        self.setFixedSize(cfg.mediaWidth.value, cfg.mediaHeight.value)
        self._apply_background_style()

    def _get_wallpaper_color(self):
        """提取壁纸主色 失败返回None"""
        try:
            home = self.parent()
            mw = getattr(home, 'mainWindow', None) if home is not None else None
            if mw and hasattr(mw, 'wallpaper') and mw.wallpaper:
                return mw.wallpaper.get_dominant_color()
        except Exception:
            pass
        return None

    @staticmethod
    def _get_system_accent_color():
        """AccentPalette[2]读取系统主题色 失败none"""
        import winreg
        try:
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                r'Software\Microsoft\Windows\CurrentVersion\Explorer\Accent')
            palette = winreg.QueryValueEx(key, 'AccentPalette')[0]
            winreg.CloseKey(key)
            if len(palette) >= 12:
                b, g, r = palette[8], palette[9], palette[10]
                return QColor(r, g, b)
        except Exception:
            pass
        return None

    def _apply_background_style(self):
        bg_color = cfg.mediaBgColor.value
        bg_opacity = cfg.mediaBgOpacity.value
        border_radius = cfg.mediaBorderRadius.value
        border_width = cfg.mediaBorderWidth.value
        border_color = cfg.mediaBorderColor.value
        # 支持将背景颜色设为特殊值 'wallpaper' 来跟随主界面壁纸颜色
        override_color = None
        actual_bg_value = bg_color.name() if hasattr(bg_color, 'name') else bg_color
        if actual_bg_value == 'wallpaper':
            override_color = self._get_wallpaper_color()

        if override_color:
            c = QColor(override_color)
            c.setAlpha(int(255 * bg_opacity / 100))
        else:
            c = QColor(actual_bg_value if isinstance(actual_bg_value, str) else bg_color)
            c.setAlpha(int(255 * bg_opacity / 100))

        # 独立处理进度条颜色：支持 'wallpaper' / 'system' / 具体颜色值
        progress_color_val = cfg.mediaProgressColor.value
        actual_progress_value = progress_color_val.name() if hasattr(progress_color_val, 'name') else progress_color_val
        if actual_progress_value == 'wallpaper':
            self._override_progress_color = self._get_wallpaper_color()
        elif actual_progress_value == 'system':
            self._override_progress_color = self._get_system_accent_color()
        elif actual_progress_value != 'primary':
            self._override_progress_color = QColor(actual_progress_value)
        else:
            self._override_progress_color = None

        if c.alpha() == 0:
            bg_css = "background-color: transparent;"
        else:
            bg_css = f"background-color: {self._qss_color(c)};"

        if border_width > 0:
            border_css = f"border: {border_width}px solid {self._qss_color(border_color)};"
        else:
            border_css = ""

        self.setStyleSheet(
            f"#mediaWidget {{"
            f"{bg_css}"
            f"border-radius: {border_radius}px;"
            f"{border_css}"
            f"}}"
        )

        # 根据背景色调整文字颜色，保证对比度
        try:
            if override_color:
                text_bg = override_color
            else:
                text_bg = QColor(actual_bg_value if isinstance(actual_bg_value, str) else bg_color)
            bright = (text_bg.red() * 299 + text_bg.green() * 587 + text_bg.blue() * 114) / 1000
            text_color = '#000000' if bright > 160 else '#FFFFFF'
            title_color_hex = text_color
            artist_color_hex = text_color + '66'
            time_color_hex = text_color + 'CC'
            # 只更新颜色部分
            try:
                base_title = self._title.styleSheet().split('color:')[0]
            except Exception:
                base_title = ''
            try:
                base_artist = self._artist.styleSheet().split('color:')[0]
            except Exception:
                base_artist = ''
            self._title.setStyleSheet(base_title + f"color: {title_color_hex};")
            self._artist.setStyleSheet(base_artist + f"color: {artist_color_hex};")
            self._time.setStyleSheet(f"color: {time_color_hex};")
            self._dur.setStyleSheet(f"color: {time_color_hex};")
            self._lyrics_w.set_lyrics_color(cfg.mediaLyricsColor.value if not override_color else time_color_hex)
        except Exception:
            pass

    def _add_cover_shadow(self, pixmap: QPixmap, size: int) -> QPixmap:
        radius = cfg.mediaCoverBorderRadius.value
        result = QPixmap(size + 8, size + 8)
        result.fill(Qt.GlobalColor.transparent)

        p = QPainter(result)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)

        shadow_color = QColor(0, 0, 0, 50)
        for i in range(4):
            p.setPen(Qt.PenStyle.NoPen)
            p.setBrush(QColor(0, 0, 0, 20 - i * 4))
            offset = (i + 1) * 2
            p.drawRoundedRect(offset, offset, size, size, radius, radius)

        rounded = QPixmap(size, size)
        rounded.fill(Qt.GlobalColor.transparent)
        p2 = QPainter(rounded)
        p2.setRenderHint(QPainter.RenderHint.Antialiasing)
        p2.setPen(Qt.PenStyle.NoPen)
        p2.setBrush(Qt.BrushStyle.SolidPattern)
        p2.drawRoundedRect(0, 0, size, size, radius, radius)
        p2.setCompositionMode(QPainter.CompositionMode.CompositionMode_SourceAtop)
        p2.drawPixmap(0, 0, pixmap.scaled(size, size, Qt.AspectRatioMode.KeepAspectRatioByExpanding, Qt.TransformationMode.SmoothTransformation))
        p2.end()

        p.drawPixmap(4, 4, rounded)
        p.end()
        return result

    def _init_cover_animation(self):
        self._cover_opacity = QGraphicsOpacityEffect(self)
        self._cover_opacity.setOpacity(1.0)
        self._cover_lbl.setGraphicsEffect(self._cover_opacity)
        
        self._cover_anim = QPropertyAnimation(self._cover_opacity, QByteArray(b"opacity"))
        self._cover_anim.setDuration(300)
        self._cover_anim.setEasingCurve(QEasingCurve.Type.OutCubic)

    def start(self):
        self._timer.start(cfg.mediaUpdateInterval.value * 1000)
        self._prog_timer.start(2000)
        self._fetch_in_thread(full=True)

    def _fetch_in_thread(self, full=True):
        if self._fetching:
            if full:
                self._pending_full = True
            return
        import threading
        self._fetching = True
        self._pending_full = False
        t = threading.Thread(target=self._do_fetch, args=(full,), daemon=True)
        t.start()

    def _do_fetch(self, full):
        try:
            m = get_media_info()
            if not m or not m.is_valid():
                logger.debug(f"媒体组件: 无效信息")
        except Exception as e:
            logger.error(f"媒体信息获取异常: {e}")
            m = None
        self._fetch_done.emit(m, full)

    def _on_fetched(self, m, full):
        self._fetching = False
        if not cfg.showMediaInfo.value:
            return
        if not m or not m.is_valid():
            self._no_media()
            if self._pending_full:
                QTimer.singleShot(100, lambda: self._fetch_in_thread(full=True))
            return
        is_new_song = m.title_artist != self._last_ta
        if is_new_song:
            logger.info(f"媒体组件: 新歌曲 {m.title} - {m.artist}")
        self._media = m
        if full:
            self._display(m)
            self.show()
            if is_new_song and self._rapid_update_count == 0:
                self._rapid_update_count = 5
                self._timer.setInterval(500)
            elif self._rapid_update_count > 0:
                self._rapid_update_count -= 1
                if self._rapid_update_count == 0:
                    self._timer.setInterval(self._normal_interval)
            if self._pending_full:
                QTimer.singleShot(100, lambda: self._fetch_in_thread(full=True))
        else:
            if m.position_ms > 0:
                self._position = m.position_ms
            self._playing = m.is_playing
            if getattr(m, 'thumbnail_data', None) and not self._has_gstmtc_cover:
                self._has_gstmtc_cover = True
                self._load_cover(m.thumbnail_data)
            if self._duration > 0:
                self._bar.setValue(min(100, int(self._position / self._duration * 100)))
                self._time.setText(self._fmt(self._position))
                if self._duration > 0:
                    self._dur.setText(self._fmt(self._duration))
            if self._playing and self._lyrics and not self._lyrics.is_empty():
                self._lyrics_w.update_position(self._position)

    def stop(self):
        self._timer.stop()
        self._prog_timer.stop()

    def _update(self):
        self._fetch_in_thread(full=True)

    def _no_media(self):
        self._title.setText("未在播放")
        self._artist.setText("")
        self._bar.setValue(0)
        self._time.setText("0:00")
        self._dur.setText("0:00")
        self._default_cover()
        self._lyrics_w.clear()
        self._media = None
        self._lyrics = None
        self._last_ta = ""
        self._has_gstmtc_cover = False
        self._playing = False
        self._duration = 0
        if cfg.showMediaInfo.value:
            self.show()

    def _display(self, m: MediaInfo):
        title = m.title or "未知歌曲"
        artist = m.artist or ""

        if m.title_artist != self._last_ta:
            self._last_ta = m.title_artist
            self._position = m.position_ms
            self._playing = m.is_playing
            self._duration = m.duration_ms
            self._has_gstmtc_cover = False
            
            app_name = getattr(m, 'app_name', '') or ''
            
            self._title.setText(title)
            self._artist.setText(artist)
            
            self._cover_anim.stop()
            self._default_cover()
            self._lyrics_w.clear()
            self._cover = None
            self._lyrics = None

            self._cover_lbl.repaint()
            self._lyrics_w.repaint()

            is_web_browser = any(browser in app_name.lower() for browser in ['chrome', 'edge', 'firefox', 'msedge'])
            
            if getattr(m, 'thumbnail_data', None):
                self._has_gstmtc_cover = True
                self._load_cover(m.thumbnail_data)
            elif app_name == 'Kugou':
                self._fetch_kugou_thumbnail()
            elif is_web_browser:
                self._has_gstmtc_cover = True

            if not is_web_browser:
                self._fetch(title, artist)

        self._playing = m.is_playing
        if m.position_ms > 0:
            self._position = m.position_ms

        self._prog_container.show()
        if self._duration > 0:
            self._bar.setValue(min(100, int(self._position / self._duration * 100)))
            self._time.setText(self._fmt(self._position))
            self._dur.setText(self._fmt(self._duration))
        else:
            self._bar.setValue(0)
            self._time.setText(self._fmt(self._position))

        self._cover_lbl.setVisible(cfg.showMediaCover.value)
        self._lyrics_w.show()

    def _update_progress(self):
        if not self._playing:
            return
        self._fetch_in_thread(full=False)

    @staticmethod
    def _fmt(ms: int) -> str:
        s = max(0, ms // 1000)
        return f"{s // 60}:{s % 60:02d}"

    def _load_cover(self, data: bytes):
        pm = QPixmap()
        pm.loadFromData(data)
        if not pm.isNull():
            self._cover = pm
            sz = cfg.mediaCoverSize.value
            cover_with_shadow = self._add_cover_shadow(pm, sz)
            self._cover_lbl.setPixmap(cover_with_shadow)

            self._cover_anim.stop()
            self._cover_opacity.setOpacity(0.0)
            self._cover_anim.setStartValue(0.0)
            self._cover_anim.setEndValue(1.0)
            self._cover_anim.start()

    def _fetch(self, title: str, artist: str):
        cache_key = f"{title} - {artist}"
        if cache_key in self._info_cache:
            info = self._info_cache[cache_key]
            self._apply_fetched_info(info)
            return
        
        if self._fetching:
            return
        self._fetching = True
        
        self._worker = FetchWorker(title, artist)
        self._thread = QThread()
        self._worker.moveToThread(self._thread)
        self._thread.started.connect(self._worker.run)
        self._worker.finished.connect(self._on_fetch_finished)
        self._worker.finished.connect(self._thread.quit)
        self._thread.finished.connect(self._cleanup_thread)
        self._thread.start()
    
    def _on_fetch_finished(self, info: dict):
        cache_key = f"{self._media.title} - {self._media.artist}" if self._media else ""
        if cache_key:
            self._info_cache[cache_key] = info
        self._apply_fetched_info(info)
    
    def _apply_fetched_info(self, info: dict):
        try:
            if info.get('detail'):
                self._duration = info['detail'].duration
            if info.get('cover') and cfg.showMediaCover.value and not self._has_gstmtc_cover:
                self._load_cover(info['cover'])
            self._lyrics = info.get('lyrics')
            self._lyrics_w.set_lyrics(self._lyrics)
        except Exception as e:
            logger.debug(f"应用歌曲信息失败: {e}")
        finally:
            self._fetching = False
    
    def _cleanup_thread(self):
        if self._thread:
            self._thread.deleteLater()
            self._thread = None
        if self._worker:
            self._worker.deleteLater()
            self._worker = None

    def _fetch_kugou_thumbnail(self):
        import threading
        def _do():
            try:
                from services.media import get_gstmtc
                gsmtc = get_gstmtc()
                if gsmtc and gsmtc.available:
                    info = gsmtc.get_info()
                    if info and getattr(info, 'thumbnail_data', None):
                        self._fetch_done.emit(info, False)
            except Exception:
                pass
        t = threading.Thread(target=_do, daemon=True)
        t.start()

    def update_settings(self):
        self._apply_config()
        self.update()

    def clear_cache(self):
        self._cache.clear()
        close_media()



DEFAULT_ICON_DIR = 'default_icon'

def get_default_icon_path(icon_filename='exe.ico'):
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base_dir, 'data', DEFAULT_ICON_DIR, icon_filename)

def get_ql_icon_path(icon_filename):
    if not icon_filename:
        return None
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    icon_path = os.path.join(base_dir, 'data', 'ql_icon', icon_filename)
    if os.path.exists(icon_path):
        return icon_path
    sw_path = os.path.join(base_dir, 'data', 'software_icon', icon_filename)
    if os.path.exists(sw_path):
        return sw_path
    default_icon = get_default_icon_path(icon_filename)
    if os.path.exists(default_icon):
        return default_icon
    return None

def get_ql_icon_save_dir():
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    icon_dir = os.path.join(base_dir, 'data', 'ql_icon')
    os.makedirs(icon_dir, exist_ok=True)
    return icon_dir

def get_folder_icon():
    return 'Directory.ico'

def get_url_icon():
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    icon_path = os.path.join(base_dir, 'data', 'software_icon', 'url.ico')
    if os.path.exists(icon_path):
        return 'url.ico'
    return 'exe.ico'

def resolve_app_from_path(file_path):
    real_path = file_path
    app_type = "app"
    
    if file_path.lower().endswith('.lnk'):
        try:
            shortcut = pythoncom.CoCreateInstance(
                shell.CLSID_ShellLink, None, pythoncom.CLSCTX_INPROC_SERVER, shell.IID_IShellLink
            )
            persist = shortcut.QueryInterface(pythoncom.IID_IPersistFile)
            persist.Load(file_path)
            real_path = shortcut.GetPath(shell.SLGP_RAWPATH)[0]
            if not real_path:
                real_path = file_path
        except Exception:
            pass

    if file_path.lower().endswith('.lnk'):
        name = os.path.splitext(os.path.basename(file_path))[0]
    else:
        name = os.path.splitext(os.path.basename(real_path))[0]
    
    if os.path.isdir(real_path):
        app_type = "folder"
        icon_filename = get_folder_icon()
        return {"name": name, "path": real_path, "icon": icon_filename, "type": app_type}
    
    provider = QFileIconProvider()
    fi = QFileInfo(real_path if os.path.exists(real_path) else file_path)
    icon = provider.icon(fi)
    icon_filename = 'exe.ico'
    sizes = icon.availableSizes()
    if sizes:
        best_size = max(sizes, key=lambda s: s.width() * s.height())
        pixmap = icon.pixmap(best_size)
        if not pixmap.isNull():
            target_size = 256
            if pixmap.width() < target_size:
                pixmap = pixmap.scaled(target_size, target_size, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)
            cleaned_name = re.sub(r'[^\w\u4e00-\u9fff]', '', name)
            if cleaned_name:
                icon_filename = cleaned_name + '.ico'
            else:
                icon_filename = 'default.ico'
            icon_dir = get_ql_icon_save_dir()
            icon_save_path = os.path.join(icon_dir, icon_filename)
            pixmap.save(icon_save_path, 'PNG')

    return {"name": name, "path": real_path, "icon": icon_filename, "type": app_type}

def resolve_url_from_string(url_string, name=None):
    url = url_string.strip()
    if not url.startswith(('http://', 'https://')):
        url = 'https://' + url
    
    if not name:
        try:
            from urllib.parse import urlparse
            parsed = urlparse(url)
            name = parsed.netloc or url
        except Exception:
            name = url
    
    icon_filename = get_url_icon()
    return {"name": name, "path": url, "icon": icon_filename, "type": "url"}


class QuickLaunchDock(QWidget):
    MAX_SCALE = 1.45
    BASE_SCALE = 1.0
    MAGNIFY_RANGE = 100
    ANIM_SPEED = 0.22
    BOUNCE_H = 14
    BOUNCE_DUR = 800
    PAD_X = 20
    PAD_Y_BOTTOM = 6
    PAD_Y_TOP = 6
    RADIUS = 16
    FPS = 120
    MAX_APPS = 12
    
    _launch_result = pyqtSignal(str, str, bool)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("quickLaunchDock")
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setSizePolicy(QSizePolicy.Policy.Maximum, QSizePolicy.Policy.Maximum)
        self.setMouseTracking(True)
        self.setAcceptDrops(True)
        
        self._executor = ThreadPoolExecutor(max_workers=1)
        self._launch_result.connect(self._on_launch_result)
        self._icon_gap = cfg.quickLaunchIconSpacing.value
        self._show_labels = cfg.quickLaunchShowLabels.value

        self._apps = []
        self._pixmaps = []
        self._scales = []
        self._target_scales = []
        self._hover_idx = -1
        self._bounce_idx = -1
        self._bounce_y = 0.0
        self._bounce_active = False
        self._bounce_start_time = 0.0
        self._painting = False
        self._last_frame = 0.0
        
        self._dragging_idx = -1
        self._drag_start_pos = None
        self._is_internal_drag = False
        self._drop_target_idx = -1
        self._drag_pos = None

        self._timer = QTimer(self)
        self._timer.timeout.connect(self._tick)
        self._timer.start(int(1000 / self.FPS))

    def _sz(self):
        return cfg.quickLaunchIconSize.value

    def set_apps(self, apps, animate_idx=-1):
        self._apps = list(apps)
        self._icon_gap = cfg.quickLaunchIconSpacing.value
        self._pixmaps = []
        for a in apps:
            fn = a.get("icon", "exe.ico")
            p = get_ql_icon_path(fn)
            pm = None
            if p and os.path.exists(p):
                raw = QPixmap(p)
                if not raw.isNull():
                    dpr = self.devicePixelRatioF() if hasattr(self, 'devicePixelRatioF') else self.devicePixelRatio()
                    raw.setDevicePixelRatio(dpr)
                    pm = raw
            self._pixmaps.append(pm)
        n = len(apps)
        self._scales = [self.BASE_SCALE] * n
        self._target_scales = [self.BASE_SCALE] * n
        self._fix_size()
        
        if animate_idx >= 0 and animate_idx < n:
            self._start_add_animation(animate_idx)
        
        self.update()
    
    def _start_add_animation(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        self._start_bounce(idx)

    def update_icon_size(self, size):
        self._icon_gap = cfg.quickLaunchIconSpacing.value
        self._show_labels = cfg.quickLaunchShowLabels.value
        self._pixmaps = []
        for a in self._apps:
            fn = a.get("icon", "exe.ico")
            p = get_ql_icon_path(fn)
            pm = None
            if p and os.path.exists(p):
                raw = QPixmap(p)
                if not raw.isNull():
                    dpr = self.devicePixelRatioF() if hasattr(self, 'devicePixelRatioF') else self.devicePixelRatio()
                    raw.setDevicePixelRatio(dpr)
                    pm = raw
            self._pixmaps.append(pm)
        self._fix_size()
        self.update()

    def _bg_rect(self):
        sz = self._sz()
        n = len(self._apps)
        if n == 0:
            return QRectF()
        w = n * sz + (n - 1) * self._icon_gap + self.PAD_X * 2
        h = sz + self.PAD_Y_TOP + self.PAD_Y_BOTTOM
        x = (self.width() - w) / 2
        y = self.height() - h - cfg.quickLaunchOffsetY.value
        return QRectF(x, y, w, h)

    def _fix_size(self):
        sz = self._sz()
        n = len(self._apps)
        if n == 0:
            self.setFixedSize(0, 0)
            return
        w_icons = n * sz + (n - 1) * self._icon_gap + self.PAD_X * 2
        h_icons = sz + self.PAD_Y_TOP + self.PAD_Y_BOTTOM
        scale_overflow = int(sz * (self.MAX_SCALE - self.BASE_SCALE))
        bounce_overflow = self.BOUNCE_H + 10
        side_overflow = int(sz * (self.MAX_SCALE - self.BASE_SCALE) * 0.3)
        label_overflow = 28 if self._show_labels else 0
        offset_y = cfg.quickLaunchOffsetY.value
        drag_extra = int(sz * 0.5)
        w = w_icons + side_overflow * 2 + drag_extra
        h = h_icons + scale_overflow + bounce_overflow + label_overflow + offset_y + drag_extra
        self.setFixedSize(w, h)

    def _icon_positions(self):
        sz = self._sz()
        n = len(self._scales)
        if n == 0:
            return []

        widths = [sz * sc for sc in self._scales]
        total = sum(widths) + (n - 1) * self._icon_gap
        bg = self._bg_rect()
        content_w = bg.width() - self.PAD_X * 2
        start_x = bg.x() + self.PAD_X + (content_w - total) / 2

        pos = []
        cx = start_x
        for i in range(n):
            pos.append(cx + widths[i] / 2)
            cx += widths[i] + self._icon_gap
        return pos

    def _icon_rect(self, i, positions=None):
        if positions is None:
            positions = self._icon_positions()
        s = self._sz() * self._scales[i]
        cx = positions[i]
        bg = self._bg_rect()
        by = bg.y() + bg.height() - self.PAD_Y_BOTTOM
        return QRectF(cx - s / 2, by - s, s, s)

    def _smoothstep(self, t):
        t = max(0.0, min(1.0, t))
        return t * t * (3.0 - 2.0 * t)

    def _get_icon_at_pos(self, pos):
        if not self._apps:
            return -1
        pos_list = self._icon_positions()
        for i in range(len(self._apps)):
            r = self._icon_rect(i, pos_list)
            if r.contains(pos):
                return i
        return -1

    def mouseMoveEvent(self, e):
        if self._dragging_idx >= 0 and self._drag_start_pos and not self._is_internal_drag:
            dist = (e.position() - self._drag_start_pos).manhattanLength()
            if dist > 10:
                self._start_internal_drag(self._dragging_idx)
        
        if self._is_internal_drag and self._dragging_idx >= 0:
            rect = self.rect()
            sz = self._sz() * self.MAX_SCALE
            pos = e.position()
            x = max(rect.x() + sz / 2, min(pos.x(), rect.x() + rect.width() - sz / 2))
            y = max(rect.y() + sz / 2, min(pos.y(), rect.y() + rect.height() - sz / 2))
            self._drag_pos = QPointF(x, y)
            self._update_drop_target(e.position())
            self.update()
        
        self._calc_targets(e.position())
        super().mouseMoveEvent(e)

    def mousePressEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton:
            pl = self._icon_positions()
            for i in range(len(self._apps)):
                if self._icon_rect(i, pl).contains(e.position()):
                    self._drag_start_pos = e.position()
                    self._dragging_idx = i
                    break
        elif e.button() == Qt.MouseButton.RightButton:
            pl = self._icon_positions()
            for i in range(len(self._apps)):
                if self._icon_rect(i, pl).contains(e.position()):
                    self._show_context_menu(i, e.position())
                    break
        super().mousePressEvent(e)

    def mouseReleaseEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton:
            if self._is_internal_drag and self._dragging_idx >= 0:
                self._finish_drag_reorder()
            elif self._dragging_idx >= 0 and self._drag_start_pos:
                pl = self._icon_positions()
                if self._icon_rect(self._dragging_idx, pl).contains(e.position()):
                    self._click(self._dragging_idx)
            
            self._dragging_idx = -1
            self._drag_start_pos = None
            self._is_internal_drag = False
            self._drop_target_idx = -1
            self._drag_pos = None
            self.update()
        
        super().mouseReleaseEvent(e)

    def _start_internal_drag(self, idx):
        self._is_internal_drag = True
        self._dragging_idx = idx
        self._drag_pos = self._drag_start_pos
        self._drop_target_idx = idx
        self.update()

    def _update_drop_target(self, pos):
        if not self._apps:
            return
        
        sz = self._sz()
        bg = self._bg_rect()
        content_w = bg.width() - self.PAD_X * 2
        n = len(self._apps)
        total_w = n * sz + (n - 1) * self._icon_gap
        start_x = bg.x() + self.PAD_X + (content_w - total_w) / 2
        
        new_target = -1
        for i in range(n):
            icon_x = start_x + i * (sz + self._icon_gap)
            if pos.x() < icon_x + sz / 2:
                new_target = i
                break
        
        if new_target == -1:
            new_target = n
        
        if new_target != self._drop_target_idx:
            self._drop_target_idx = new_target

    def _finish_drag_reorder(self):
        if self._dragging_idx < 0 or self._drop_target_idx < 0:
            return
        
        if self._dragging_idx == self._drop_target_idx:
            return
        
        apps = list(self._apps)
        dragged_app = apps.pop(self._dragging_idx)
        
        insert_idx = self._drop_target_idx
        if insert_idx > self._dragging_idx:
            insert_idx -= 1
        
        apps.insert(insert_idx, dragged_app)
        
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=insert_idx)
        
        logger.info(f"快捷启动栏顺序已调整: {self._dragging_idx} -> {insert_idx}")

    def _show_context_menu(self, idx, pos):
        if idx < 0 or idx >= len(self._apps):
            return
        
        app = self._apps[idx]
        menu = RoundMenu(app.get("name", "应用"), self)
        
        open_action = Action(FIF.PLAY, "打开", self)
        open_action.triggered.connect(lambda: self._click(idx))
        menu.addAction(open_action)
        
        menu.addSeparator()
        
        edit_action = Action(FIF.EDIT, "编辑", self)
        edit_action.triggered.connect(lambda: self._edit_app(idx))
        menu.addAction(edit_action)
        
        delete_action = Action(FIF.DELETE, "删除", self)
        delete_action.triggered.connect(lambda: self._delete_app(idx))
        menu.addAction(delete_action)
        
        menu.addSeparator()
        
        app_type = app.get("type", "app")
        if app_type == "url":
            path_info = f"网址: {app.get('path', '')}"
        else:
            path_info = f"路径: {app.get('path', '')}"
        
        info_action = Action(FIF.INFO, path_info, self)
        info_action.setEnabled(False)
        menu.addAction(info_action)
        
        menu.exec(QCursor.pos())

    def _edit_app(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        
        from ui.home import AppEditDialog
        
        dialog = AppEditDialog(self.window(), self._apps[idx])
        if dialog.exec():
            result = dialog.get_app_data()
            if result:
                self._apps[idx] = result
                cfg.quickLaunchApps.value = self._apps
                save_cfg()
                self.set_apps(self._apps)
                InfoBar.success("保存成功", "快捷方式已更新", parent=self.window(), duration=2000)

    def _delete_app(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        
        app_name = self._apps[idx].get("name", "此应用")
        
        mw = self.window()
        mask = QWidget(mw)
        mask.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)
        mask.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        mask.setGeometry(0, 0, mw.width(), mw.height())
        mask.setStyleSheet("background-color: rgba(0, 0, 0, 120);")
        mask.show()
        
        from qfluentwidgets import MessageBox
        box = MessageBox("确认删除", f"确定要从快捷启动栏删除 \"{app_name}\" 吗？", mask)
        box.yesButton.setText("删除")
        box.cancelButton.setText("取消")
        
        if box.exec():
            self._apps.pop(idx)
            cfg.quickLaunchApps.value = self._apps
            save_cfg()
            self.set_apps(self._apps)
            InfoBar.success("删除成功", f"已删除 {app_name}", parent=self.window(), duration=2000)
        
        mask.close()
        mask.deleteLater()

    def leaveEvent(self, e):
        n = len(self._target_scales)
        self._target_scales = [self.BASE_SCALE] * n
        self._hover_idx = -1
        super().leaveEvent(e)

    def _calc_targets(self, pos):
        if not self._apps:
            return
        mx = pos.x()
        my = pos.y()
        pos_list = self._icon_positions()
        new_hover = -1

        for i in range(len(self._apps)):
            r = self._icon_rect(i, pos_list)
            if r.contains(pos):
                new_hover = i
                break

        if new_hover < 0:
            bg = self._bg_rect()
            if bg.contains(pos):
                min_dist = float('inf')
                for i in range(len(self._apps)):
                    cx = pos_list[i]
                    d = abs(mx - cx)
                    if d < min_dist:
                        min_dist = d
                        new_hover = i

        for i in range(len(self._apps)):
            if new_hover >= 0 and abs(i - new_hover) <= 2:
                cx = pos_list[i]
                d = abs(mx - cx)
                if d < self.MAGNIFY_RANGE:
                    t = self._smoothstep(1.0 - d / self.MAGNIFY_RANGE)
                    sc = self.BASE_SCALE + (self.MAX_SCALE - self.BASE_SCALE) * t
                else:
                    sc = self.BASE_SCALE
            else:
                sc = self.BASE_SCALE
            self._target_scales[i] = sc

        if new_hover != self._hover_idx:
            self._hover_idx = new_hover

    def _tick(self):
        now = time.time()
        dt = min(now - self._last_frame, 0.05) if self._last_frame > 0 else 0.016
        self._last_frame = now
        changed = False

        for i in range(len(self._scales)):
            if i >= len(self._target_scales):
                break
            cur = self._scales[i]
            tgt = self._target_scales[i]
            diff = tgt - cur
            if abs(diff) > 0.001:
                sp = self.ANIM_SPEED * (60.0 * dt)
                if abs(diff) < 0.008:
                    self._scales[i] = tgt
                else:
                    self._scales[i] += diff * min(sp, 1.0)
                changed = True

        if self._bounce_active:
            elapsed = (now - self._bounce_start_time) * 1000.0
            dur = float(self.BOUNCE_DUR)
            bh = float(self.BOUNCE_H)
            if elapsed >= dur:
                self._bounce_y = 0.0
                self._bounce_active = False
                self._bounce_idx = -1
            else:
                t = elapsed / dur
                kfs = [
                    (0.00, 0.0), (0.14, -bh), (0.28, 0.0),
                    (0.44, -bh * 0.50), (0.58, 0.0),
                    (0.72, -bh * 0.22), (0.86, 0.0), (1.00, 0.0),
                ]
                lo_t, lo_v = kfs[0], kfs[1]
                for j in range(len(kfs) - 1):
                    if kfs[j][0] <= t <= kfs[j + 1][0]:
                        lo_t, lo_v = kfs[j], kfs[j + 1]
                        break
                span = lo_v[0] - lo_t[0]
                if span > 0:
                    lt = (t - lo_t[0]) / span
                    lt = lt * lt * (3.0 - 2.0 * lt)
                    self._bounce_y = lo_t[1] + (lo_v[1] - lo_t[1]) * lt
                else:
                    self._bounce_y = lo_v[1]
                changed = True

        if changed:
            self.update()

    def dragEnterEvent(self, e):
        if e.mimeData().hasUrls():
            urls = e.mimeData().urls()
            for url in urls:
                path = url.toLocalFile()
                if path:
                    if path.lower().endswith(('.exe', '.lnk')) or os.path.isdir(path):
                        e.acceptProposedAction()
                        return
        elif e.mimeData().hasText():
            text = e.mimeData().text().strip()
            if text.startswith(('http://', 'https://', 'www.')):
                e.acceptProposedAction()
                return
        e.ignore()

    def dragMoveEvent(self, e):
        if e.mimeData().hasUrls() or e.mimeData().hasText():
            e.acceptProposedAction()
        else:
            e.ignore()

    def dropEvent(self, e):
        e.acceptProposedAction()
        
        if e.mimeData().hasUrls():
            urls = e.mimeData().urls()
            for url in urls:
                path = url.toLocalFile()
                if not path:
                    continue
                if os.path.isdir(path):
                    self._add_folder_from_path(path)
                elif path.lower().endswith(('.exe', '.lnk')):
                    self._add_app_from_path(path)
        
        elif e.mimeData().hasText():
            text = e.mimeData().text().strip()
            if text.startswith(('http://', 'https://', 'www.')):
                self._add_url(text)

    def _add_app_from_path(self, file_path):
        new_app = resolve_app_from_path(file_path)
        apps = list(cfg.quickLaunchApps.value)
        if len(apps) >= self.MAX_APPS:
            InfoBar.warning(
                title="数量限制",
                content=f"快捷启动栏最多只能添加 {self.MAX_APPS} 个应用",
                parent=self.window(),
                duration=3000
            )
            return
        apps.append(new_app)
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=len(apps) - 1)
        InfoBar.success("添加成功", f"已添加 {new_app['name']}", parent=self.window(), duration=2000)

    def _add_folder_from_path(self, folder_path):
        name = os.path.basename(folder_path)
        new_item = {
            "name": name,
            "path": folder_path,
            "icon": get_folder_icon(),
            "type": "folder"
        }
        apps = list(cfg.quickLaunchApps.value)
        if len(apps) >= self.MAX_APPS:
            InfoBar.warning(
                title="数量限制",
                content=f"快捷启动栏最多只能添加 {self.MAX_APPS} 个项目",
                parent=self.window(),
                duration=3000
            )
            return
        apps.append(new_item)
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=len(apps) - 1)
        InfoBar.success("添加成功", f"已添加文件夹 {name}", parent=self.window(), duration=2000)

    def _add_url(self, url):
        new_item = resolve_url_from_string(url)
        apps = list(cfg.quickLaunchApps.value)
        if len(apps) >= self.MAX_APPS:
            InfoBar.warning(
                title="数量限制",
                content=f"快捷启动栏最多只能添加 {self.MAX_APPS} 个项目",
                parent=self.window(),
                duration=3000
            )
            return
        apps.append(new_item)
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=len(apps) - 1)
        InfoBar.success("添加成功", f"已添加网址 {new_item['name']}", parent=self.window(), duration=2000)

    def _click(self, idx):
        a = self._apps[idx]
        path = a.get("path", "")
        name = a.get("name", "")
        app_type = a.get("type", "app")
        self._start_bounce(idx)
        if path:
            self._executor.submit(self._launch_thread, path, name, app_type)

    def _launch_thread(self, target, name, app_type):
        try:
            if not target:
                self._launch_result.emit(name, "未配置路径", False)
                return
            
            if app_type == "url":
                webbrowser.open(target)
                self._launch_result.emit(name, target, True)
            elif app_type == "folder":
                if os.path.exists(target):
                    os.startfile(target)
                    self._launch_result.emit(name, target, True)
                else:
                    self._launch_result.emit(name, f"文件夹不存在: {target}", False)
            else:
                if os.path.exists(target):
                    os.startfile(target)
                    self._launch_result.emit(name, target, True)
                else:
                    self._launch_result.emit(name, f"路径不存在: {target}", False)
        except Exception as e:
            self._launch_result.emit(name, str(e), False)

    def _launch_app_thread(self, app_path, app_name):
        self._launch_thread(app_path, app_name, "app")

    def _on_launch_result(self, app_name, info, success):
        if success:
            logger.info(f"已启动：{app_name} ({info})")
            InfoBar.success(
                title="启动成功",
                content=f"正在打开 {app_name}",
                parent=self.window(),
                duration=2000
            )
        else:
            logger.warning(f"启动失败：{app_name}, {info}")
            InfoBar.error(
                title="启动失败",
                content=f"{app_name}: {info}",
                parent=self.window(),
                duration=3000
            )

    def _start_bounce(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        self._bounce_idx = idx
        self._bounce_y = 0.0
        self._bounce_active = True
        self._bounce_start_time = time.time()

    def _get_by(self):
        return self._bounce_y

    def _set_by(self, v):
        self._bounce_y = v
        self.update()

    bounceY = pyqtProperty(float, _get_by, _set_by)

    def paintEvent(self, event):
        if self._painting:
            return
        self._painting = True
        try:
            self._render()
        finally:
            self._painting = False

    def _render(self):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)

        bg = self._bg_rect()
        if bg.isEmpty():
            p.end()
            return

        dark = isDarkTheme()

        path = QPainterPath()
        path.addRoundedRect(bg, self.RADIUS, self.RADIUS)

        if dark:
            bg_c = QColor(30, 30, 32, 165)
            brd_c = QColor(255, 255, 255, 20)
            sh_top = QColor(255, 255, 255, 22)
            sh_mid = QColor(255, 255, 255, 6)
            inner_glow = QColor(255, 255, 255, 8)
        else:
            bg_c = QColor(235, 235, 240, 172)
            brd_c = QColor(0, 0, 0, 12)
            sh_top = QColor(255, 255, 255, 95)
            sh_mid = QColor(255, 255, 255, 18)
            inner_glow = QColor(255, 255, 255, 25)

        p.setPen(Qt.PenStyle.NoPen)

        shadow_path = QPainterPath()
        sr = QRectF(bg.x() + 1.5, bg.y() + 2, bg.width() - 3, bg.height() * 0.5)
        shadow_path.addRoundedRect(sr, self.RADIUS - 3, self.RADIUS - 3)
        p.setBrush(QBrush(inner_glow))
        p.drawPath(shadow_path)

        p.setBrush(bg_c)
        p.drawPath(path)

        grad = QLinearGradient(bg.x(), bg.y(), bg.x(), bg.y() + bg.height())
        grad.setColorAt(0.0, sh_top)
        grad.setColorAt(0.30, sh_mid)
        grad.setColorAt(1.0, QColor(0, 0, 0, 0))

        p.setCompositionMode(QPainter.CompositionMode.CompositionMode_Lighten)
        p.fillPath(path, grad)
        p.setCompositionMode(QPainter.CompositionMode.CompositionMode_SourceOver)

        pen = QPen(brd_c)
        pen.setWidth(1)
        p.setPen(pen)
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawPath(path)

        pl = self._icon_positions()
        baseline_y = bg.y() + bg.height() - self.PAD_Y_BOTTOM
        sz = self._sz()

        for i in range(len(self._apps)):
            if self._is_internal_drag and i == self._dragging_idx:
                continue
            
            pm = self._pixmaps[i]
            sc = self._scales[i]
            s = sz * sc
            
            if self._is_internal_drag and self._dragging_idx >= 0:
                if i >= self._drop_target_idx and i < self._dragging_idx:
                    cx = pl[i + 1]
                elif i < self._drop_target_idx and i > self._dragging_idx:
                    cx = pl[i - 1]
                else:
                    cx = pl[i]
            else:
                cx = pl[i]
            
            top = baseline_y - s
            if i == self._bounce_idx:
                top += self._bounce_y
            
            if pm and not pm.isNull():
                p.drawPixmap(
                    QRectF(cx - s / 2, top, s, s),
                    pm,
                    QRectF(0, 0, pm.width(), pm.height()),
                )
            else:
                p.setBrush(QColor(120, 120, 120, 60))
                p.setPen(QPen(QColor(120, 120, 120, 100), 1))
                r = QRectF(cx - s / 2, top, s, s)
                p.drawRoundedRect(r, 8, 8)
                p.setPen(QPen(QColor(180, 180, 180, 150), 2))
                font = p.font()
                font.setPixelSize(int(s * 0.4))
                p.setFont(font)
                p.drawText(r, Qt.AlignmentFlag.AlignCenter, "?")
            
            if i == self._hover_idx and self._show_labels and not self._is_internal_drag:
                name = self._apps[i].get("name", "")
                if name:
                    label_font = p.font()
                    label_font.setFamily("HarmonyOS Sans,Microsoft YaHei,sans-serif")
                    label_font.setPixelSize(14)
                    label_font.setWeight(QFont.Weight.Medium)
                    p.setFont(label_font)
                    fm = QFontMetrics(label_font)
                    
                    display_name = name
                    if len(name) > 50:
                        display_name = name[:50] + "..."
                    
                    padding_x = 10
                    label_w = fm.horizontalAdvance(display_name) + padding_x * 2
                    label_h = 24
                    label_x = cx - label_w / 2
                    label_y = top - label_h - 4
                    
                    widget_rect = self.rect()
                    if label_x < widget_rect.left() + 2:
                        label_x = widget_rect.left() + 2
                    if label_x + label_w > widget_rect.right() - 2:
                        label_x = widget_rect.right() - label_w - 2
                    if label_y < widget_rect.top() + 2:
                        label_y = top + sz + 4
                    
                    label_path = QPainterPath()
                    label_path.addRoundedRect(label_x, label_y, label_w, label_h, label_h / 2, label_h / 2)
                    p.setPen(Qt.PenStyle.NoPen)
                    p.setBrush(QColor(0, 0, 0, 220))
                    p.drawPath(label_path)
                    p.setPen(QColor(255, 255, 255, 255))
                    p.setFont(label_font)
                    text_rect = QRectF(label_x, label_y, label_w, label_h)
                    p.drawText(text_rect, Qt.AlignmentFlag.AlignCenter | Qt.TextFlag.TextSingleLine, display_name)

        if self._is_internal_drag and self._dragging_idx >= 0 and self._drag_pos:
            pm = self._pixmaps[self._dragging_idx] if self._dragging_idx < len(self._pixmaps) else None
            s = sz * self.MAX_SCALE
            cx = self._drag_pos.x()
            top = self._drag_pos.y() - s / 2
            
            if pm and not pm.isNull():
                p.drawPixmap(
                    QRectF(cx - s / 2, top, s, s),
                    pm,
                    QRectF(0, 0, pm.width(), pm.height()),
                )
            else:
                p.setBrush(QColor(120, 120, 120, 100))
                p.setPen(QPen(QColor(120, 120, 120, 150), 1))
                r = QRectF(cx - s / 2, top, s, s)
                p.drawRoundedRect(r, 8, 8)

        p.end()

    def minimumSizeHint(self):
        bg = self._bg_rect()
        return QSize(int(bg.width()), int(bg.height()))

    def hideEvent(self, e):
        self._timer.stop()
        super().hideEvent(e)

    def showEvent(self, e):
        self._timer.start(int(1000 / self.FPS))
        super().showEvent(e)
