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
媒体信息显示控件
"""

import logging
import os
import sys
from typing import Optional
from concurrent.futures import ThreadPoolExecutor

from PyQt6.QtCore import Qt, QTimer, QPropertyAnimation, QEasingCurve, QByteArray, pyqtProperty, pyqtSignal, QObject, QThread
from PyQt6.QtGui import QPixmap, QColor, QPainter, QFont
from PyQt6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout, QLabel, QProgressBar, QSizePolicy, QFrame, QGraphicsOpacityEffect

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

from core.config import cfg
from core.media import MediaInfo, Lyrics, get_media_info, fetch_all_info, close as close_media
from core.constants import load_qss

logger = logging.getLogger(__name__)


class LyricsWidget(QWidget):
    """歌词显示控件"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self._lines = []
        self._current_idx = -1
        self._lyrics: Optional[Lyrics] = None
        self._line_height = 24
        self._visible_lines = 3
        self._text_size = 14
        self._scroll_offset = 0.0

        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)
        self._update_height()

        self._anim = QPropertyAnimation(self, QByteArray(b'scrollOffset'))
        self._anim.setDuration(300)
        self._anim.setEasingCurve(QEasingCurve.Type.OutCubic)

    def get_scrollOffset(self): return self._scroll_offset
    def set_scrollOffset(self, v): self._scroll_offset = v; self.update()
    scrollOffset = pyqtProperty(float, fget=get_scrollOffset, fset=set_scrollOffset)

    def _update_height(self):
        self.setFixedHeight(self._visible_lines * self._line_height + 10)

    def set_text_size(self, size: int):
        self._text_size = size
        self._line_height = size + 10
        self._update_height()

    def set_visible_lines(self, n: int):
        self._visible_lines = n
        self._update_height()

    def set_lyrics(self, lyrics: Optional[Lyrics]):
        self._lyrics = lyrics
        self._current_idx = -1
        self._scroll_offset = 0.0
        self._lines = lyrics.lines if lyrics and not lyrics.is_empty() else []
        self.update()

    def update_position(self, ms: int):
        if not self._lyrics or self._lyrics.is_empty():
            return
        _, idx = self._lyrics.get_line_at_time(ms)
        if idx != self._current_idx and idx >= 0:
            self._current_idx = idx
            half = self._visible_lines // 2
            target = max(0, (idx - half) * self._line_height)
            self._anim.stop()
            self._anim.setStartValue(self._scroll_offset)
            self._anim.setEndValue(target)
            self._anim.start()

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setRenderHint(QPainter.RenderHint.TextAntialiasing)

        if not self._lines:
            p.setPen(QColor(255, 255, 255, 180))
            p.setFont(QFont("HarmonyOS Sans SC", self._text_size))
            p.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, "暂无歌词")
            return

        font = QFont("HarmonyOS Sans SC", self._text_size)
        p.setFont(font)
        p.setClipRect(0, 0, self.width(), self.height())

        y_off = self.height() // 2 - self._line_height // 2 - self._scroll_offset
        for i, line in enumerate(self._lines):
            y = y_off + i * self._line_height + self._line_height - 5
            if y < -self._line_height or y > self.height() + self._line_height:
                continue
            if i == self._current_idx:
                p.setPen(QColor(255, 255, 255, 255))
            else:
                dist = abs(i - self._current_idx) if self._current_idx >= 0 else 10
                p.setPen(QColor(255, 255, 255, max(80, 200 - dist * 30)))
            p.drawText(10, int(y), self.width() - 20, self._line_height, Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignVCenter, line.text)

    def clear(self):
        self._lines = []
        self._lyrics = None
        self._current_idx = -1
        self.update()


class ProgressBar(QProgressBar):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTextVisible(False)
        self.setFixedHeight(4)

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(QColor(255, 255, 255, 60))
        p.drawRoundedRect(0, 0, self.width(), self.height(), 2, 2)
        if self.value() > 0:
            w = int(self.width() * self.value() / self.maximum()) if self.maximum() > 0 else 0
            p.setBrush(QColor(255, 255, 255, 200))
            p.drawRoundedRect(0, 0, w, self.height(), 2, 2)


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

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("mediaWidget")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)

        self._media: Optional[MediaInfo] = None
        self._lyrics: Optional[Lyrics] = None
        self._last_ta = ""
        self._cover: Optional[QPixmap] = None
        self._fetching = False
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

    def _init_ui(self):
        self.setStyleSheet(load_qss('media_widget.qss'))

        layout = QHBoxLayout(self)
        layout.setContentsMargins(12, 8, 12, 8)
        layout.setSpacing(12)

        self._cover_lbl = QLabel()
        self._cover_lbl.setObjectName("mediaCoverLabel")
        self._cover_lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._default_cover()
        layout.addWidget(self._cover_lbl, 0, Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)

        right_col = QVBoxLayout()
        right_col.setSpacing(4)
        right_col.setContentsMargins(0, 0, 0, 0)

        info_row = QHBoxLayout()
        info_row.setSpacing(8)
        info_row.setContentsMargins(0, 0, 0, 0)

        info_col = QVBoxLayout()
        info_col.setSpacing(2)
        info_col.setContentsMargins(0, 0, 0, 0)

        self._title = QLabel("未在播放")
        self._title.setObjectName("mediaTitleLabel")
        self._title.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        info_col.addWidget(self._title)

        self._artist = QLabel("")
        self._artist.setObjectName("mediaArtistLabel")
        self._artist.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        info_col.addWidget(self._artist)

        prog = QWidget()
        prog.setObjectName("mediaProgressContainer")
        pl = QHBoxLayout(prog)
        pl.setContentsMargins(0, 0, 0, 0)
        pl.setSpacing(6)

        self._time = QLabel("0:00")
        self._time.setObjectName("mediaTimeLabel")
        self._time.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        pl.addWidget(self._time)

        self._bar = ProgressBar()
        self._bar.setRange(0, 100)
        pl.addWidget(self._bar, 1)

        self._dur = QLabel("0:00")
        self._dur.setObjectName("mediaDurationLabel")
        self._dur.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        pl.addWidget(self._dur)

        info_col.addWidget(prog)
        info_row.addLayout(info_col, 1)

        self._lyrics_w = LyricsWidget()
        self._lyrics_w.setObjectName("mediaLyricsWidget")

        right_col.addLayout(info_row, 1)
        right_col.addWidget(self._lyrics_w, 1)
        layout.addLayout(right_col, 1)

        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)

    def _get_content_height(self) -> int:
        cover_sz = cfg.mediaCoverSize.value
        lyric_h = self._lyrics_w.sizeHint().height() if self._lyrics_w.isVisible() else 0
        return max(cover_sz, 52 + (24 if cfg.showMediaProgress.value else 0), lyric_h) + 16

    def _default_cover(self):
        sz = cfg.mediaCoverSize.value
        pm = QPixmap(sz, sz)
        pm.fill(Qt.GlobalColor.transparent)
        p = QPainter(pm)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(QColor(255, 255, 255, 100))
        p.setBrush(QColor(255, 255, 255, 30))
        p.drawRoundedRect(0, 0, sz, sz, 8, 8)
        p.end()
        self._cover_lbl.setPixmap(pm)

    def _setup_timers(self):
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._update)
        self._prog_timer = QTimer(self)
        self._prog_timer.timeout.connect(self._update_progress)

    def _apply_config(self):
        sz = cfg.mediaTextSize.value
        self._title.setStyleSheet(f"font-size: {sz}px;")
        self._artist.setStyleSheet(f"font-size: {sz - 2}px;")

        cover_sz = cfg.mediaCoverSize.value
        self._cover_lbl.setFixedSize(cover_sz, cover_sz)
        if self._cover and not self._cover.isNull():
            self._cover_lbl.setPixmap(self._cover.scaled(cover_sz, cover_sz, Qt.AspectRatioMode.KeepAspectRatioByExpanding, Qt.TransformationMode.SmoothTransformation))
        else:
            self._default_cover()

        self._lyrics_w.set_text_size(cfg.mediaLyricsSize.value)
        self._lyrics_w.set_visible_lines(cfg.mediaLyricsLines.value)
        self.adjustSize()

    def _init_cover_animation(self):
        self._cover_opacity = QGraphicsOpacityEffect(self)
        self._cover_opacity.setOpacity(1.0)
        self._cover_lbl.setGraphicsEffect(self._cover_opacity)
        
        self._cover_anim = QPropertyAnimation(self._cover_opacity, QByteArray(b"opacity"))
        self._cover_anim.setDuration(300)
        self._cover_anim.setEasingCurve(QEasingCurve.Type.OutCubic)

    def start(self):
        self._timer.start(cfg.mediaUpdateInterval.value * 1000)
        self._prog_timer.start(1000)
        self._update()

    def stop(self):
        self._timer.stop()
        self._prog_timer.stop()

    def _update(self):
        if not cfg.showMediaInfo.value:
            self.hide()
            return
        m = get_media_info()
        if not m or not m.is_valid():
            self._no_media()
            return
        
        is_new_song = m.title_artist != self._last_ta
        self._media = m
        self._display(m)
        self.show()
        
        if is_new_song and self._rapid_update_count == 0:
            self._rapid_update_count = 5
            self._timer.setInterval(500)
        elif self._rapid_update_count > 0:
            self._rapid_update_count -= 1
            if self._rapid_update_count == 0:
                self._timer.setInterval(self._normal_interval)

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
            
            self._title.setText(title)
            self._artist.setText(artist)
            
            self._cover_anim.stop()
            self._default_cover()
            self._lyrics_w.clear()
            self._cover = None
            self._lyrics = None
            
            self._cover_lbl.repaint()
            self._lyrics_w.repaint()
            self.adjustSize()
            
            self._fetch(title, artist)

        self._playing = m.is_playing
        if m.position_ms > 0:
            self._position = m.position_ms

        if cfg.showMediaProgress.value:
            self._bar.parent().show()
            if self._duration > 0:
                self._bar.setValue(min(100, int(self._position / self._duration * 100)))
                self._time.setText(self._fmt(self._position))
                self._dur.setText(self._fmt(self._duration))
            else:
                self._bar.setValue(0)
                self._time.setText(self._fmt(self._position))
        else:
            self._bar.parent().hide()

        self._cover_lbl.setVisible(cfg.showMediaCover.value)
        self._lyrics_w.setVisible(cfg.showMediaLyrics.value)
        self.adjustSize()

    def _update_progress(self):
        if not self._playing or self._duration <= 0:
            return
        m = get_media_info()
        if m and m.is_valid() and m.position_ms > 0:
            self._position = m.position_ms
            self._playing = m.is_playing
            if cfg.showMediaProgress.value and self._duration > 0:
                self._bar.setValue(min(100, int(self._position / self._duration * 100)))
                self._time.setText(self._fmt(self._position))
            if self._lyrics and not self._lyrics.is_empty():
                self._lyrics_w.update_position(self._position)

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
            self._cover_lbl.setPixmap(pm.scaled(sz, sz, Qt.AspectRatioMode.KeepAspectRatioByExpanding, Qt.TransformationMode.SmoothTransformation))
            
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
            if info.get('cover') and cfg.showMediaCover.value:
                self._load_cover(info['cover'])
            self._lyrics = info.get('lyrics')
            self._lyrics_w.set_lyrics(self._lyrics)
            self.adjustSize()
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

    def update_settings(self):
        self._apply_config()
        self.update()

    def clear_cache(self):
        self._cache.clear()
        close_media()
