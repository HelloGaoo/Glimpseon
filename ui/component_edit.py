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
组件编辑窗口
"""

from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import QApplication
from qfluentwidgets import FluentIcon as FIF, FluentWindow, setTheme

from core.config import cfg
from core.constants import load_qss


class ComponentEditWindow(FluentWindow):
    """组件编辑窗口"""

    def __init__(self, main_window):
        super().__init__()
        self.main_window = main_window
        self.setObjectName("componentEdit")
        self.resize(900, 650)

        # 窗口置顶，隐藏最小化和最大化按钮
        self.setWindowFlags(
            (self.windowFlags() | Qt.WindowType.WindowStaysOnTopHint)
            & ~Qt.WindowType.WindowMinimizeButtonHint
            & ~Qt.WindowType.WindowMaximizeButtonHint
        )

        self._applyTheme()

        # 中间显示
        self._centerOnScreen()

    def _centerOnScreen(self):
        screen = QApplication.primaryScreen()
        if screen:
            center = screen.availableGeometry().center()
            self.move(center.x() - self.width() // 2, center.y() - self.height() // 2)

    def _applyTheme(self):
        """应用主题"""
        theme = cfg.themeMode.value
        setTheme(theme)
        qss = load_qss('setting.qss')
        if qss:
            self.setStyleSheet(qss)

    def closeEvent(self, event):
        """释放资源"""
        event.accept()
