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
通知模块
"""

import logging
from PyQt6.QtCore import (
    QObject, QTimer, QPropertyAnimation,
    QEasingCurve, pyqtSlot, Qt, pyqtSignal,
)
from PyQt6.QtWidgets import (
    QFrame, QLabel, QApplication, QVBoxLayout, QGraphicsOpacityEffect,
)
from PyQt6.QtGui import QFont
from plyer import notification as plyer_notification

from core.utils import tr

logger = logging.getLogger("ClassLively.core.notification")

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
    """无边框置顶弹窗"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint
            | Qt.WindowType.WindowStaysOnTopHint
            | Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating, True)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)

    def show(self):
        super().show()
        try:
            hwnd = int(self.winId())
            ctypes.windll.user32.SetWindowPos(
                hwnd, -1, 0, 0, 0, 0,  # HWND_TOPMOST = -1
                0x0002 | 0x0001 | 0x0040  # SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW
            )
        except Exception:
            pass


class ScrollBanner(_BasePopup):
    finished = pyqtSignal()

    def __init__(self, text: str, pixels_per_second: int, duration: int):
        super().__init__()
        self.text = text
        self.pixels_per_second = pixels_per_second
        self.duration = duration
        self._scroll_offset = 0.0
        self._timer = QTimer(self)
        self._last_time = None
        self._must_finish = False
        self._completed_loops = 0     # 已完整滚动次数
        self._setup_ui()
        self._start_animation()

    def _setup_ui(self):
        screen = QApplication.primaryScreen().availableGeometry()
        banner_height = 80
        self.setGeometry(0, 60, screen.width(), banner_height)

        self.label = QLabel(self.text, self)
        self.label.setStyleSheet("color: white; font-size: 36px; font-weight: bold; background: transparent;")
        font = QFont()
        font.setPointSize(24)
        self.label.setFont(font)
        self.label.adjustSize()

        self._scroll_offset = float(self.width())
        self.label.move(int(self._scroll_offset), (self.height() - self.label.height()) // 2)

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

            # 如果时间已到 且 已完成至少两圈： 关闭
            if self._must_finish and self._completed_loops >= 2:
                self._timer.stop()
                self.close()
                return

            # 否则开始下一圈
            self._scroll_offset = float(self.width())

        self.label.move(int(self._scroll_offset), self.label.y())

    def paintEvent(self, event):
        from PyQt6.QtGui import QPainter, QColor
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setBrush(QColor(0, 0, 0, 180))
        painter.setPen(Qt.PenStyle.NoPen)
        painter.drawRect(self.rect())

    def showEvent(self, event):
        super().showEvent(event)
        force_topmost(int(self.winId()))

    def _fade_out(self):
        """时间到标记结束"""
        self._must_finish = True

    def closeEvent(self, event):
        self.finished.emit()
        super().closeEvent(event)

class FullScreenPopup(_BasePopup):

    finished = pyqtSignal()     

    def __init__(self, text, duration):
        super().__init__()
        self.text = text
        self.duration = duration
        self._setup_ui()
        self._show_and_close()

    def _setup_ui(self):
        screen = QApplication.primaryScreen().availableGeometry()
        self.setGeometry(screen)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)

        layout = QVBoxLayout(self) 
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.label = QLabel(self.text, self)
        self.label.setStyleSheet("color: white; font-size: 36px; font-weight: bold; background: transparent;")
        self.label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.label.setWordWrap(True)
        font = QFont()
        font.setPointSize(24)
        self.label.setFont(font)
        layout.addWidget(self.label)

    def paintEvent(self, event):
        from PyQt6.QtGui import QPainter, QColor
        painter = QPainter(self)
        painter.setBrush(QColor(0, 0, 0, 180))
        painter.setPen(Qt.PenStyle.NoPen)
        painter.drawRect(self.rect())

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
    """通知排队"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self._active_windows = []   # 防止窗口被回收
        self._is_showing = False    # 是否正在显示滚动/全屏
        self._queue = []            # 等待显示的滚动/全屏通知

    @pyqtSlot(dict)
    def handle_notification(self, data: dict):
        notif_type = data.get("type", "")
        content = data.get("content", "")
        speed = data.get("speed", 5)
        duration = data.get("duration", 5)

        if not content:
            logger.warning("通知内容为空")
            return

        # 右下角通知不排队
        if notif_type == NotifType.CORNER:
            self._show_corner(content, duration)
            return

        # 滚动/全屏通知排队
        if self._is_showing:
            self._queue.append(data)
        else:
            if notif_type == NotifType.SCROLL:
                self._show_scroll(content, speed, duration)
            elif notif_type == NotifType.FULLSCREEN:
                self._show_fullscreen(content, duration)
            else:
                logger.warning(f"未知通知类型: {notif_type}")

    def _show_scroll(self, text, speed, duration):
        # speed: 1~20 即 30 ~ 400px/s
        pixels_per_second = 30 + (speed - 1) * 20
        banner = ScrollBanner(text, pixels_per_second, duration)
        banner.finished.connect(self._on_notification_finished)
        banner.destroyed.connect(lambda: self._active_windows.remove(banner))
        self._active_windows.append(banner)
        self._is_showing = True
        banner.show()

    def _show_fullscreen(self, text, duration):
        popup = FullScreenPopup(text, duration)
        popup.finished.connect(self._on_notification_finished)
        popup.destroyed.connect(lambda: self._active_windows.remove(popup))
        self._active_windows.append(popup)
        self._is_showing = True
        popup.show()

    def _show_corner(self, text, duration):
        plyer_notification.notify(
            title="ClassLively",
            message=text,
            timeout=duration,
        )

    def _on_notification_finished(self):
        """当前通知结束 下一个"""
        self._is_showing = False
        if self._queue:
            next_data = self._queue.pop(0)
            self.handle_notification(next_data)