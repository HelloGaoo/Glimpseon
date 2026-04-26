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

from PyQt5.QtCore import Qt, QRect, QTimer, pyqtSlot, pyqtSignal
import time
from PyQt5.QtWidgets import QApplication
from PyQt5.QtGui import QIcon, QPixmap, QPainter, QColor, QFont
from PyQt5.QtWidgets import QWidget, QVBoxLayout, QLabel, QHBoxLayout
from qfluentwidgets import ProgressBar, BodyLabel, StrongBodyLabel, isDarkTheme
import os

# 感谢chatgpt5mini
class SplashScreen(QWidget):
    """启动窗口"""
    status_signal = pyqtSignal(str)
    progress_signal = pyqtSignal(int)
    
    def __init__(self, app_name: str, version: str, icon_path: str = None):
        super().__init__()
        self.app_name = app_name
        self.version = version
        self.icon_path = icon_path
        self.status_signal.connect(self.updateStatus)
        self.progress_signal.connect(self.setProgress)
        self._initUI()
        self._loadIcon()
        self._loadQss()
    
    def _initUI(self):
        """初始化 UI"""
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setFixedSize(360, 160)
        
        self.content_widget = QWidget(self)
        self.content_widget.setObjectName("contentWidget")
        self.content_widget.setGeometry(0, 0, 360, 160)
        
        self._updateBackgroundStyle()
        
        content_layout = QVBoxLayout(self.content_widget)
        content_layout.setContentsMargins(20, 15, 20, 10)
        content_layout.setSpacing(10)
        header_layout = QHBoxLayout()
        header_layout.setSpacing(15)
        self.icon_label = QLabel()
        self.icon_label.setFixedSize(64, 64)
        header_layout.addWidget(self.icon_label)
        
        title_layout = QHBoxLayout()
        title_layout.setSpacing(8)
        self.app_name_label = StrongBodyLabel(self.app_name)
        title_layout.addWidget(self.app_name_label)
        
        self.version_label = BodyLabel(f"{self.version}")
        self.version_label.setObjectName("versionLabel")
        title_layout.addWidget(self.version_label)
        title_layout.addStretch()
        
        header_layout.addLayout(title_layout)
        header_layout.addStretch()
        content_layout.addLayout(header_layout)
        self.status_label = BodyLabel("正在初始化...")
        self.status_label.setObjectName("statusLabel")
        content_layout.addWidget(self.status_label)
        content_layout.addStretch(1)
        self.progress_bar = ProgressBar()
        self.progress_bar.setFixedHeight(4)
        self.progress_bar.setValue(0)
        self._current_progress = 0
        self._target_progress = 0
        self._anim_timer = QTimer(self)
        self._anim_timer.setInterval(8)
        self._anim_timer.timeout.connect(self._advance_progress)
        content_layout.addWidget(self.progress_bar)
        self.centerOnScreen()
        
    def _loadQss(self):
        """加载 QSS"""
        self.setStyleSheet(load_qss('splash_screen.qss'))
    
    def _updateBackgroundStyle(self):
        """更新背景"""
        is_dark = isDarkTheme()
        if is_dark:
            bg_color = "rgba(32, 32, 32, 200)"
        else:
            bg_color = "rgba(255, 255, 255, 200)"
        
        self.content_widget.setStyleSheet(f"""
            #contentWidget {{
                background-color: {bg_color};
                border-radius: 10px;
            }}
        """)
        
    def _loadIcon(self):
        """加载图标"""
        if self.icon_path and os.path.exists(self.icon_path):
            pixmap = QPixmap(self.icon_path).scaled(
                64, 64, Qt.KeepAspectRatio, Qt.SmoothTransformation
            )
            self.icon_label.setPixmap(pixmap)
        else:
            self.icon_label.setText("❓")
            self.icon_label.setFont(QFont("Segoe UI Emoji", 32))
            self.icon_label.setAlignment(Qt.AlignCenter)
        
    def centerOnScreen(self):
        """将窗口居中显示"""
        desktop = QApplication.desktop()
        screen_rect = desktop.availableGeometry()
        x = (screen_rect.width() - self.width()) // 2
        y = (screen_rect.height() - self.height()) // 2
        
        self.move(x, y)
        
    @pyqtSlot(str)
    def updateStatus(self, status: str):
        """更新状态文本"""
        self.status_label.setText(status)
        
    @pyqtSlot(int)
    def setProgress(self, value: int):
        """设置0-100"""
        try:
            v = int(max(0, min(100, value)))
        except Exception:
            return
        self._target_progress = v
        if not self._anim_timer.isActive():
            self._anim_timer.start()

    def _advance_progress(self):
        if self._current_progress < self._target_progress:
            step = max(1, (self._target_progress - self._current_progress) // 6)
            self._current_progress += step
            if self._current_progress > self._target_progress:
                self._current_progress = self._target_progress
            self.progress_bar.setValue(self._current_progress)
        else:
            self._anim_timer.stop()

    def waitForProgress(self, target: int = 100, timeout: float = 3.0):
        try:
            target = int(max(0, min(100, target)))
        except Exception:
            target = 100
        end = time.time() + float(timeout)
        while time.time() < end and self._current_progress < target:
            QApplication.processEvents()
            time.sleep(0.003)
    
    def paintEvent(self, event):
        pass
