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

"""通知模块"""
import logging
import os
from PyQt6.QtCore import (
    QObject, QTimer, pyqtSlot, Qt, pyqtSignal,
)
from PyQt6.QtWidgets import (
    QFrame, QApplication, QVBoxLayout,
)
from PyQt6.QtGui import QFont
from qfluentwidgets import BodyLabel
from plyer import notification as plyer_notification

from core.constants import FONT_PRIMARY
import threading
import subprocess
import uuid

logger = logging.getLogger("Glimpseon.core.notification")

# 0=Normal 1=Bold 2=Black
_WEIGHT_MAP = {0: QFont.Weight.Normal, 1: QFont.Weight.Bold, 2: QFont.Weight.Black}


class NotifType:
    SCROLL = "scroll"
    CORNER = "corner"
    FULLSCREEN = "fullscreen"

# 强制置顶
try:
    import ctypes
    from ctypes import wintypes
    HWND_TOPMOST = -1
    SWP_NOMOVE = 0x0002
    SWP_NOSIZE = 0x0001
    SWP_SHOWWINDOW = 0x0040

    def force_topmost(hwnd):
        ctypes.windll.user32.SetWindowPos(
            hwnd, HWND_TOPMOST,
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW,
        )
except ImportError:
    def force_topmost(hwnd): pass


class _BasePopup(QFrame):
    """去边框置顶弹窗"""

    def __init__(self, parent=None, mouse_through=False):
        super().__init__(parent)
        self._mouse_through = mouse_through
        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint
            | Qt.WindowType.WindowStaysOnTopHint
            | Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating, True)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        if mouse_through:
            self.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, True)

    def _apply_mouse_through(self):
        """鼠标穿透"""
        if not self._mouse_through:
            return
        try:
            hwnd = int(self.winId())
            # WS_EX_TRANSPARENT = 0x20
            GWL_EXSTYLE = -20
            current = ctypes.windll.user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
            ctypes.windll.user32.SetWindowLongW(hwnd, GWL_EXSTYLE, current | 0x20)
        except Exception:
            pass

    def show(self):
        super().show()
        try:
            hwnd = int(self.winId())
            ctypes.windll.user32.SetWindowPos(
                hwnd, -1, 0, 0, 0, 0,  # HWND_TOPMOST = -1
                0x0002 | 0x0001 | 0x0040  # SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW
            )
            self._apply_mouse_through()
        except Exception:
            pass


class ScrollBanner(_BasePopup):
    finished = pyqtSignal()

    def __init__(self, text: str, pixels_per_second: int, duration: int,
                 bg_color="#000000", bg_alpha=180, text_color="#ffffff",
                 font_size=24, font_weight=0, bg_height=80, mouse_through=True):
        super().__init__(mouse_through=mouse_through)
        self.text = text
        self.pixels_per_second = pixels_per_second
        self.duration = duration
        self._bg_color = bg_color
        self._bg_alpha = bg_alpha
        self._text_color = text_color
        self._font_size = font_size
        self._font_weight = font_weight  # 0 = Normal, 1 = Bold, 2 = Black
        self._bg_height = bg_height
        self._scroll_offset = 0.0
        self._timer = QTimer(self)
        self._last_time = None
        self._must_finish = False
        self._completed_loops = 0
        self._setup_ui()
        self._start_animation()

    def _setup_ui(self):
        screen = QApplication.primaryScreen().availableGeometry()
        banner_height = self._bg_height
        self.setGeometry(0, 60, screen.width(), banner_height)

        self.label = BodyLabel(self.text, self)
        font = QFont(FONT_PRIMARY, self._font_size)
        font.setWeight(_WEIGHT_MAP.get(self._font_weight, QFont.Weight.Bold))
        self.label.setFont(font)
        self.label.setStyleSheet(
            f"color: {self._text_color}; background: transparent;"
        )
        self.label.adjustSize()

        self._scroll_offset = float(self.width())
        v_offset = max(0, (banner_height - self.label.height()) // 2)
        self.label.move(int(self._scroll_offset), v_offset)

    def _start_animation(self):
        self.show()
        self._timer.timeout.connect(self._scroll_step)
        self._timer.start(16)
        QTimer.singleShot(self.duration * 1000, self._fade_out)

    def _scroll_step(self):
        import time
        now = time.perf_counter()
        if self._last_time is None:
            self._last_time = now
            return
        dt = now - self._last_time
        self._last_time = now

        move = self.pixels_per_second * dt
        self._scroll_offset -= move

        if self._scroll_offset + self.label.width() <= 0:
            self._completed_loops += 1
            if self._must_finish:
                self._timer.stop()
                self.close()
                return
            self._scroll_offset = float(self.width())

        self.label.move(int(self._scroll_offset), self.label.y())

    def paintEvent(self, event):
        from PyQt6.QtGui import QPainter, QColor
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        bg = QColor(self._bg_color)
        bg.setAlpha(self._bg_alpha)
        painter.setBrush(bg)
        painter.setPen(Qt.PenStyle.NoPen)
        painter.drawRect(self.rect())
        painter.end()

    def showEvent(self, event):
        super().showEvent(event)
        force_topmost(int(self.winId()))

    def _fade_out(self):
        """时长到点:本轮滚完即关(已滚过一轮则立刻关)"""
        self._must_finish = True
        if self._completed_loops >= 1:
            self._timer.stop()
            self.close()

    def closeEvent(self, event):
        self._timer.stop()
        self.finished.emit()
        super().closeEvent(event)


class FullScreenPopup(_BasePopup):

    finished = pyqtSignal()

    def __init__(self, text, duration,
                 bg_color="#000000", bg_alpha=180, text_color="#ffffff",
                 font_size=24, font_weight=0):
        super().__init__()
        self.text = text
        self.duration = duration
        self._bg_color = bg_color
        self._bg_alpha = bg_alpha
        self._text_color = text_color
        self._font_size = font_size
        self._font_weight = font_weight
        self._setup_ui()
        self._show_and_close()

    def _setup_ui(self):
        screen = QApplication.primaryScreen().availableGeometry()
        self.setGeometry(screen)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.label = BodyLabel(self.text, self)
        font = QFont(FONT_PRIMARY, self._font_size)
        font.setWeight(_WEIGHT_MAP.get(self._font_weight, QFont.Weight.Bold))
        self.label.setFont(font)
        self.label.setStyleSheet(
            f"color: {self._text_color}; background: transparent;"
        )
        self.label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.label.setWordWrap(True)
        layout.addWidget(self.label)

    def paintEvent(self, event):
        from PyQt6.QtGui import QPainter, QColor
        painter = QPainter(self)
        bg = QColor(self._bg_color)
        bg.setAlpha(self._bg_alpha)
        painter.setBrush(bg)
        painter.setPen(Qt.PenStyle.NoPen)
        painter.drawRect(self.rect())
        painter.end()

    def _show_and_close(self):
        self.show()
        QTimer.singleShot(self.duration * 1000, self.close)

    def showEvent(self, event):
        super().showEvent(event)
        force_topmost(int(self.winId()))

    def closeEvent(self, event):
        self.finished.emit()
        super().closeEvent(event)


class NotificationManager(QObject):
    notification_finished = pyqtSignal()
    play_audio_signal = pyqtSignal(str) 

    def __init__(self, parent=None):
        super().__init__(parent)
        self._active_windows = []   # 持有弹窗防止被回收
        self._is_showing = False    # 是否正在滚动/全屏

        # TTS
        self._tts_voice = "zh-CN-XiaoxiaoNeural"
        self._tts_rate = "+0%"
        self._tts_volume = "+0%"

        # 播放器
        self._player = None           # QMediaPlayer
        self._audio_output = None     # QAudioOutput
        self._current_audio_file = "" # 文件路径


        self.play_audio_signal.connect(self._play_audio)

    def _format_tts_percent(self, value: int) -> str:
        delta = value - 100
        sign = "+" if delta >= 0 else ""
        return f"{sign}{delta}%"

    def _speak_text(self, text: str):
        if self._tts_voice == "done":
            return

        # 取快照
        voice, rate, volume = self._tts_voice, self._tts_rate, self._tts_volume

        def run_async():
            text_file = f"tts_text_{uuid.uuid4().hex[:8]}.txt"
            temp_file = f"tts_{uuid.uuid4().hex[:8]}.mp3"
            try:
                with open(text_file, "w", encoding="utf-8") as f:
                    f.write(text)
                cmd = [
                    "edge-tts",
                    "--voice", voice,
                    "--rate", rate,
                    "--volume", volume,
                    "-f", text_file,
                    "--write-media", temp_file
                ]
                subprocess.run(cmd, check=True, capture_output=True)
                self.play_audio_signal.emit(temp_file)
            except Exception as e:
                logger.error(f"TTS 生成失败: {e}")
                try:
                    if os.path.exists(temp_file):
                        os.remove(temp_file)
                except Exception:
                    pass
            finally:
                try:
                    if os.path.exists(text_file):
                        os.remove(text_file)
                except Exception:
                    pass

        threading.Thread(target=run_async, daemon=True).start()

    @pyqtSlot(str)
    def _play_audio(self, file_path: str):
        """播放音频"""
        from PyQt6.QtMultimedia import QMediaPlayer, QAudioOutput
        from PyQt6.QtCore import QUrl

        if self._player is not None:
            self._player.stop()
            self._player.deleteLater()
            self._player = None
        if self._audio_output is not None:
            self._audio_output.deleteLater()
            self._audio_output = None
        if self._current_audio_file and os.path.exists(self._current_audio_file):
            try:
                os.remove(self._current_audio_file)
            except Exception:
                pass

        self._current_audio_file = file_path

        # 创建新播放器
        self._player = QMediaPlayer()
        self._audio_output = QAudioOutput()
        self._player.setAudioOutput(self._audio_output)
        self._player.setSource(QUrl.fromLocalFile(file_path))

        # 清理文件
        def on_media_status_changed(status):
            if status == QMediaPlayer.MediaStatus.EndOfMedia:
                QTimer.singleShot(500, self.off_audio)
            elif status == QMediaPlayer.MediaStatus.InvalidMedia:
                QTimer.singleShot(500, self.off_audio)

        def on_error(error):
            logger.error(f"播放失败: {error}")
            self.off_audio()

        self._player.mediaStatusChanged.connect(on_media_status_changed)
        self._player.errorOccurred.connect(on_error)

        # 开始播放
        self._player.play()

    def off_audio(self):
        """关闭播放"""
        if self._player is not None:
            self._player.stop()
            self._player.deleteLater()
            self._player = None
        if self._audio_output is not None:
            self._audio_output.deleteLater()
            self._audio_output = None

        if self._current_audio_file and os.path.exists(self._current_audio_file):
            def delete_with_retry(path, attempt=0):
                try:
                    os.remove(path)
                    logger.debug(f"已删除临时文件: {path}")
                except PermissionError:
                    if attempt < 3:
                        QTimer.singleShot(300 * (attempt + 1), lambda: delete_with_retry(path, attempt + 1))
                    else:
                        logger.warning(f"无法删除临时文件: {path}")
            delete_with_retry(self._current_audio_file)
            self._current_audio_file = ""

    @pyqtSlot(dict)
    def handle_notification(self, data: dict):
        notif_type = data.get("type", "")
        content = data.get("content", "")
        speed = data.get("speed", 5)
        duration = data.get("duration", 5)
        bg_color = data.get("bg_color", "#000000")
        bg_alpha = data.get("bg_alpha", 180)
        text_color = data.get("text_color", "#ffffff")
        font_size = data.get("font_size", 24)
        font_weight = data.get("font_weight", 1)
        self._tts_voice = data.get("tts_voice", self._tts_voice)
        self._tts_rate = self._format_tts_percent(data.get("tts_rate", 100))
        self._tts_volume = self._format_tts_percent(data.get("tts_volume", 100))

        if not content:
            logger.warning("通知内容为空")
            return

        self._speak_text(content)

        # 右下角通知即时弹
        if notif_type == NotifType.CORNER:
            self._show_corner(content, duration)
        elif notif_type == NotifType.SCROLL:
            self._show_scroll(content, speed, duration, bg_color, bg_alpha, text_color, font_size, font_weight)
        elif notif_type == NotifType.FULLSCREEN:
            self._show_fullscreen(content, duration, bg_color, bg_alpha, text_color, font_size, font_weight)
        else:
            logger.warning(f"未知通知类型: {notif_type}")

    def _show_scroll(self, text, speed, duration, bg_color, bg_alpha, text_color, font_size, font_weight):
        from core.config import cfg
        banner = ScrollBanner(text, 30 + (speed - 1) * 20, duration,
                              bg_color, bg_alpha, text_color, font_size, font_weight,
                              bg_height=cfg.get(cfg.scrollBannerBgHeight),
                              mouse_through=cfg.get(cfg.scrollBannerMouseThrough))
        self._present(banner)

    def _show_fullscreen(self, text, duration, bg_color, bg_alpha, text_color, font_size, font_weight):
        popup = FullScreenPopup(text, duration,
                                 bg_color, bg_alpha, text_color, font_size, font_weight)
        self._present(popup)

    def _present(self, popup):
        """登记弹窗,结束后自行回收"""
        popup.finished.connect(lambda p=popup: self._release(p))
        self._active_windows.append(popup)
        self._is_showing = True

    def _release(self, popup):
        """弹窗收尾:移出持有列表并销毁,再派发下一条"""
        try:
            self._active_windows.remove(popup)
        except ValueError:
            pass
        popup.deleteLater()
        self._on_notification_finished()

    def _show_corner(self, text, duration):
        plyer_notification.notify(
            title="Glimpseon",
            message=text,
            timeout=duration,
        )

    def _on_notification_finished(self):
        """当前通知结束,通知界面派发下一条"""
        self._is_showing = False
        self.notification_finished.emit()
