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
基础滚动界面模块
提供所有设置类界面的基类，包含标题和滚动区域。
"""

from PyQt5.QtCore import Qt
from PyQt5.QtWidgets import QLabel, QVBoxLayout, QHBoxLayout, QWidget
from PyQt5.QtGui import QPixmap, QIcon
from qfluentwidgets import (
    CardWidget, FluentIcon as FIF, PrimaryPushButton, PushButton,
    InfoBar, isDarkTheme, ScrollArea, SmoothScrollArea, ExpandLayout
)


class BaseScrollAreaInterface(ScrollArea):
    """ 基础滚动区域界面 """
    
    def __init__(self, title: str, parent=None, width=1000, height=800, 
                 viewport_margins=(0, 120, 0, 20), title_position=(60, 63)):
        super().__init__(parent=parent)
        self.title = title
        self.scrollWidget = QWidget()
        self.titleLabel = QLabel(title, self)
        
        self.resize(width, height)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        self.setViewportMargins(*viewport_margins)
        self.setWidget(self.scrollWidget)
        self.setWidgetResizable(True)
        
        self.titleLabel.setObjectName('settingLabel')
        self.scrollWidget.setObjectName('scrollWidget')
        self.titleLabel.move(*title_position)


