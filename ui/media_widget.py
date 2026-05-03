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
媒体信息显示控件模块
"""

import logging
import sys
import os
from io import BytesIO
from typing import Optional

from PyQt6.QtCore import Qt, QTimer, QPropertyAnimation, QEasingCurve, QByteArray, pyqtProperty
from PyQt6.QtGui import QPixmap, QColor, QPainter, QFont, QLinearGradient
from PyQt6.QtWidgets import (
    QWidget,
    QVBoxLayout,
    QHBoxLayout,
    QLabel,
    QProgressBar,
    QFrame,
    QSizePolicy,
)

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

from core.config import cfg
from services.media import MediaInfo, get_media_info_sync, GSMTC_AVAILABLE
from services.lyrics import Lyrics, get_lyrics_service

logger = logging.getLogger(__name__)


class LyricsDisplayWidget(QWidget):
    """歌词显示控件"""
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self._lines: list = []
        self._current_index = -1
        self._lyrics: Optional[Lyrics] = None
        self._line_height = 24
        self._visible_lines = 3
        self._text_size = 14
        self._text_color = QColor(255, 255, 255, 180)
        self._highlight_color = QColor(255, 255, 255, 255)
        self._scroll_offset = 0.0
        self._target_offset = 0.0
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)
        self.setFixedHeight(self._visible_lines * self._line_height + 10)
        self._scroll_animation = QPropertyAnimation(self, QByteArray(b'scrollOffset'))
        self._scroll_animation.setDuration(300)
        self._scroll_animation.setEasingCurve(QEasingCurve.Type.OutCubic)
    
    def get_scrollOffset(self):
        return self._scroll_offset
    
    def set_scrollOffset(self, value):
        self._scroll_offset = value
        self.update()
    
    scrollOffset = pyqtProperty(float, fget=get_scrollOffset, fset=set_scrollOffset)
    
    def set_text_size(self, size: int):
        self._text_size = size
        self._line_height = size + 10
        self.setFixedHeight(self._visible_lines * self._line_height + 10)
        self.update()
    
    def set_visible_lines(self, count: int):
        self._visible_lines = count
        self.setFixedHeight(self._visible_lines * self._line_height + 10)
        self.update()
    
    def set_lyrics(self, lyrics: Optional[Lyrics]):
        self._lyrics = lyrics
        self._current_index = -1
        self._scroll_offset = 0.0
        self._target_offset = 0.0
        if lyrics and not lyrics.is_empty():
            self._lines = lyrics.lines
        else:
            self._lines = []
        self.update()
    
    def update_position(self, position_ms: int):
        if not self._lyrics or self._lyrics.is_empty():return
        line, idx = self._lyrics.get_line(position_ms)
        if idx != self._current_index and idx >= 0:
            self._current_index = idx
            self._animate_to_line(idx)
            self.update()
    
    def _animate_to_line(self, line_index: int):
        if not self._lines:return
        
        half = self._visible_lines // 2
        self._target_offset = max(0, (line_index - half) * self._line_height)
        self._scroll_animation.stop()
        self._scroll_animation.setStartValue(self._scroll_offset)
        self._scroll_animation.setEndValue(self._target_offset)
        self._scroll_animation.start()
    
    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setRenderHint(QPainter.RenderHint.TextAntialiasing)
        if not self._lines:
            painter.setPen(self._text_color)
            font = QFont("HarmonyOS Sans SC", self._text_size)
            painter.setFont(font)
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, "暂无歌词")
            return
        
        font = QFont("HarmonyOS Sans SC", self._text_size)
        painter.setFont(font)
        rect_height = self.height()
        painter.setClipRect(0, 0, self.width(), rect_height)
        y_offset = rect_height // 2 - self._line_height // 2 - self._scroll_offset
        for i, line in enumerate(self._lines):
            y = y_offset + i * self._line_height + self._line_height - 5
            if y < -self._line_height or y > rect_height + self._line_height:continue
            
            if i == self._current_index:
                gradient = QLinearGradient(0, y - self._text_size, 0, y)
                gradient.setColorAt(0, QColor(255, 255, 255, 255))
                gradient.setColorAt(1, QColor(200, 200, 200, 255))
                painter.setPen(QColor(255, 255, 255, 255))
                font.setBold(True)
                painter.setFont(font)
                font.setBold(False)
            else:
                distance = abs(i - self._current_index) if self._current_index >= 0 else 10
                alpha = max(80, 200 - distance * 30)
                painter.setPen(QColor(255, 255, 255, alpha))
            text_rect = self.width() - 20
            painter.drawText(10, int(y), text_rect, self._line_height, 
                           Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignVCenter, 
                           line.text)
    
    def clear(self):
        self._lines = []
        self._lyrics = None
        self._current_index = -1
        self._scroll_offset = 0.0
        self.update()


class ProgressBar(QProgressBar):
    """进度条"""
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTextVisible(False)
        self.setFixedHeight(4)
        self._bg_color = QColor(255, 255, 255, 60)
        self._progress_color = QColor(255, 255, 255, 200)
    
    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setPen(Qt.PenStyle.NoPen)
        painter.setBrush(self._bg_color)
        painter.drawRoundedRect(0, 0, self.width(), self.height(), 2, 2)
        
        if self.value() > 0:
            progress_width = int(self.width() * self.value() / self.maximum()) if self.maximum() > 0 else 0
            painter.setBrush(self._progress_color)
            painter.drawRoundedRect(0, 0, progress_width, self.height(), 2, 2)


class MediaWidget(QWidget):
    """媒体信息显示控件"""
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("mediaWidget")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self._current_media: Optional[MediaInfo] = None
        self._current_lyrics: Optional[Lyrics] = None
        self._last_title_artist = ""
        self._cover_pixmap: Optional[QPixmap] = None
        self._is_fetching_lyrics = False
        self._lyrics_cache: dict = {}
        self._init_ui()
        self._setup_timer()
        self._apply_config()
    
    def _init_ui(self):
        self._main_layout = QHBoxLayout(self)
        self._main_layout.setContentsMargins(15, 10, 15, 10)
        self._main_layout.setSpacing(15)
        self._cover_label = QLabel()
        self._cover_label.setFixedSize(64, 64)
        self._cover_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._cover_label.setObjectName("mediaCover")
        self._set_default_cover()
        self._main_layout.addWidget(self._cover_label)
        self._info_layout = QVBoxLayout()
        self._info_layout.setSpacing(6)
        self._info_layout.setContentsMargins(0, 0, 0, 0)
        self._title_label = QLabel("未在播放")
        self._title_label.setObjectName("mediaTitle")
        self._title_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._title_label.setWordWrap(False)
        self._info_layout.addWidget(self._title_label)
        self._artist_label = QLabel("")
        self._artist_label.setObjectName("mediaArtist")
        self._artist_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._artist_label.setWordWrap(False)
        self._info_layout.addWidget(self._artist_label)
        self._progress_container = QWidget()
        self._progress_layout = QHBoxLayout(self._progress_container)
        self._progress_layout.setContentsMargins(0, 0, 0, 0)
        self._progress_layout.setSpacing(8)
        self._time_label = QLabel("0:00")
        self._time_label.setObjectName("mediaTime")
        self._time_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._progress_layout.addWidget(self._time_label)
        self._progress_bar = ProgressBar()
        self._progress_bar.setRange(0, 100)
        self._progress_bar.setValue(0)
        self._progress_layout.addWidget(self._progress_bar, 1)
        self._duration_label = QLabel("0:00")
        self._duration_label.setObjectName("mediaTime")
        self._duration_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        self._progress_layout.addWidget(self._duration_label)
        self._info_layout.addWidget(self._progress_container)
        self._main_layout.addLayout(self._info_layout, 1)
        self._lyrics_widget = LyricsDisplayWidget()
        self._main_layout.addWidget(self._lyrics_widget, 1)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)
        self.setFixedHeight(90)
    
    def _set_default_cover(self):
        pixmap = QPixmap(64, 64)
        pixmap.fill(Qt.GlobalColor.transparent)
        painter = QPainter(pixmap)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setPen(QColor(255, 255, 255, 100))
        painter.setBrush(QColor(255, 255, 255, 30))
        painter.drawRoundedRect(0, 0, 64, 64, 8, 8)
        painter.drawPixmap(16, 16, QPixmap(32, 32))
        painter.end()
        self._cover_label.setPixmap(pixmap)
    
    def _setup_timer(self):
        self._update_timer = QTimer(self)
        self._update_timer.timeout.connect(self._update_media_info)
    
    def _apply_config(self):
        self._update_style()
        self._update_cover_size()
        self._update_lyrics_settings()
    
    def _update_style(self):
        text_size = cfg.mediaTextSize.value
        color_str = "#FFFFFF"
        
        self._title_label.setStyleSheet(f"""
            color: {color_str};
            font-size: {text_size}px;
            font-weight: bold;
            font-family: "HarmonyOS Sans SC", "Microsoft YaHei", sans-serif;
            background: transparent;
        """)
        
        self._artist_label.setStyleSheet(f"""
            color: rgba(255, 255, 255, 180);
            font-size: {text_size - 2}px;
            font-family: "HarmonyOS Sans SC", "Microsoft YaHei", sans-serif;
            background: transparent;
        """)
        
        self._time_label.setStyleSheet(f"""
            color: rgba(255, 255, 255, 150);
            font-size: 11px;
            font-family: "HarmonyOS Sans SC", "Microsoft YaHei", sans-serif;
            background: transparent;
        """)
        
        self._duration_label.setStyleSheet(f"""
            color: rgba(255, 255, 255, 150);
            font-size: 11px;
            font-family: "HarmonyOS Sans SC", "Microsoft YaHei", sans-serif;
            background: transparent;
        """)
    
    def _update_cover_size(self):
        size = cfg.mediaCoverSize.value
        self._cover_label.setFixedSize(size, size)
        if self._cover_pixmap and not self._cover_pixmap.isNull():
            scaled = self._cover_pixmap.scaled(
                size, size, 
                Qt.AspectRatioMode.KeepAspectRatioByExpanding,
                Qt.TransformationMode.SmoothTransformation
            )
            self._cover_label.setPixmap(scaled)
    
    def _update_lyrics_settings(self):
        self._lyrics_widget.set_text_size(cfg.mediaLyricsSize.value)
        self._lyrics_widget.set_visible_lines(cfg.mediaLyricsLines.value)
    
    def start(self):
        if not GSMTC_AVAILABLE:
            logger.warning("GSMTC 不可用，媒体控件不会启动")
            self.hide()
            return
        
        interval = cfg.mediaUpdateInterval.value * 1000
        self._update_timer.start(interval)
        self._update_media_info()
        logger.info("媒体控件已启动")
    
    def stop(self):
        self._update_timer.stop()
        logger.info("媒体控件已停止")
    
    def _update_media_info(self):
        if not cfg.showMediaInfo.value:
            self.hide()
            return
        
        media_info = get_media_info_sync()
        
        if media_info is None:
            self._show_no_media()
            return
        
        if not media_info.is_valid():
            self._show_no_media()
            return
        
        self._current_media = media_info
        self._update_display(media_info)
        self.show()
    
    def _show_no_media(self):
        self._title_label.setText("未在播放")
        self._artist_label.setText("")
        self._progress_bar.setValue(0)
        self._time_label.setText("0:00")
        self._duration_label.setText("0:00")
        self._set_default_cover()
        self._lyrics_widget.clear()
        self._current_media = None
        self._current_lyrics = None
        self._last_title_artist = ""
        if cfg.showMediaInfo.value:self.show()
    
    def _update_display(self, media_info: MediaInfo):
        title = media_info.title or "未知歌曲"
        artist = media_info.artist or ""
        self._title_label.setText(title)
        self._artist_label.setText(artist)
        
        if cfg.showMediaProgress.value:
            self._progress_container.show()
            progress = int(media_info.get_progress_percent() * 100)
            self._progress_bar.setValue(progress)
            self._time_label.setText(media_info.format_position())
            self._duration_label.setText(media_info.format_duration())
        else:
            self._progress_container.hide()
        
        if cfg.showMediaCover.value:
            self._cover_label.show()
            if media_info.thumbnail_data:
                self._load_cover(media_info.thumbnail_data)
            else:
                self._set_default_cover()
        else:
            self._cover_label.hide()
        
        if cfg.showMediaLyrics.value:
            self._lyrics_widget.show()
            self._update_lyrics(media_info)
        else:
            self._lyrics_widget.hide()
    
    def _load_cover(self, data: bytes):
        try:
            pixmap = QPixmap()
            pixmap.loadFromData(data)
            if not pixmap.isNull():
                self._cover_pixmap = pixmap
                size = cfg.mediaCoverSize.value
                scaled = pixmap.scaled(
                    size, size,
                    Qt.AspectRatioMode.KeepAspectRatioByExpanding,
                    Qt.TransformationMode.SmoothTransformation
                )
                self._cover_label.setPixmap(scaled)
            else:
                self._set_default_cover()
        except Exception as e:
            logger.debug(f"加载封面失败: {e}")
            self._set_default_cover()
    
    def _update_lyrics(self, media_info: MediaInfo):
        current_title_artist = media_info.title_artist
        
        if current_title_artist != self._last_title_artist:
            self._last_title_artist = current_title_artist
            self._fetch_lyrics_async(media_info.title, media_info.artist)
        
        if self._current_lyrics:
            self._lyrics_widget.update_position(media_info.position_ms)
    
    def _fetch_lyrics_async(self, title: str, artist: str):
        if self._is_fetching_lyrics:
            return
        
        cache_key = f"{title}_{artist}".lower()
        if cache_key in self._lyrics_cache:
            self._current_lyrics = self._lyrics_cache[cache_key]
            self._lyrics_widget.set_lyrics(self._current_lyrics)
            return
        
        self._is_fetching_lyrics = True
        
        try:
            service = get_lyrics_service()
            lyrics = service.fetch_lyrics(title, artist)
            
            if lyrics:
                self._current_lyrics = lyrics
                self._lyrics_cache[cache_key] = lyrics
                self._lyrics_widget.set_lyrics(lyrics)
            else:
                self._current_lyrics = None
                self._lyrics_widget.set_lyrics(None)
        except Exception as e:
            logger.debug(f"获取歌词失败: {e}")
            self._current_lyrics = None
        finally:
            self._is_fetching_lyrics = False
    
    def update_settings(self):
        self._update_style()
        self._update_cover_size()
        self._update_lyrics_settings()
        self.update()
    
    def clear_cache(self):
        self._lyrics_cache.clear()
        service = get_lyrics_service()
        service.clear_cache()
