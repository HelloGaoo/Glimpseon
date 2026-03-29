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
可拖拽控件模块
"""
from PyQt5.QtCore import QDate, QTime, QRect, Qt, QTimer
from PyQt5.QtWidgets import QLabel, QVBoxLayout, QWidget
import datetime
import cnlunar
import logging

from config import cfg

logger = logging.getLogger(__name__)


class MovableWidget(QWidget):
    """包装任意控件为可拖拽、可选择的容器"""
    
    def __init__(self, inner_widget, mainWindow, parent=None):
        super().__init__(parent or getattr(mainWindow, 'homeContent', None))
        self.mainWindow = mainWindow
        self.inner = inner_widget
        self.inner.setParent(self)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.addWidget(self.inner)
        self._drag_pos = None
        self._resize_pos = None
        self.setAttribute(Qt.WA_StyledBackground, True)
        self.setSelected(False)
        self.isDraggable = True
        if hasattr(self.inner, 'layout'):
            layout = self.inner.layout()
            if layout:
                for i in range(layout.count()):
                    widget = layout.itemAt(i).widget()
                    if isinstance(widget, QLabel) and ':' in widget.text():
                        # 是时钟组件，添加定时器
                        self.clockTimer = QTimer(self)
                        self.clockTimer.timeout.connect(self.updateClock)
                        self.clockTimer.start(1000)
                        # 保存时钟和日期标签
                        self.clockLabel = widget
                        if i + 1 < layout.count():
                            self.dateLabel = layout.itemAt(i + 1).widget()
                        break

    def mousePressEvent(self, e):
        if e.button() == Qt.LeftButton and self.isDraggable:
            rect = self.rect()
            resize_area = QRect(rect.width() - 10, rect.height() - 10, 10, 10)
            if resize_area.contains(e.pos()):
                self._resize_pos = e.pos()
            else:
                self._drag_pos = e.pos()
                try:
                    self.raise_()
                except Exception:
                    pass
                if hasattr(self.mainWindow, 'selectComponent'):
                    self.mainWindow.selectComponent(self)
        super().mousePressEvent(e)

    def mouseMoveEvent(self, e):
        if self.isDraggable:
            rect = self.rect()
            resize_area = QRect(rect.width() - 10, rect.height() - 10, 10, 10)
            if resize_area.contains(e.pos()):
                self.setCursor(Qt.SizeFDiagCursor)
            else:
                self.setCursor(Qt.ArrowCursor)
        else:
            self.setCursor(Qt.ArrowCursor)
        
        # 调整大小
        if self.isDraggable and self._resize_pos and (e.buttons() & Qt.LeftButton):
            delta = e.pos() - self._resize_pos
            new_width = max(50, self.width() + delta.x())
            new_height = max(50, self.height() + delta.y())
            self.resize(new_width, new_height)
            self._resize_pos = e.pos()
        # 拖动
        elif self.isDraggable and self._drag_pos and (e.buttons() & Qt.LeftButton):
            new_pos = self.mapToParent(e.pos() - self._drag_pos)
            parent_rect = self.parent().rect() if self.parent() else QRect(0, 0, 10000, 10000)
            x = max(0, min(new_pos.x(), parent_rect.width() - self.width()))
            y = max(0, min(new_pos.y(), parent_rect.height() - self.height()))
            self.move(x, y)
        super().mouseMoveEvent(e)
        
    def updateClock(self):
        """更新时钟显示"""
        if hasattr(self, 'clockLabel'):
            currentTime = QTime.currentTime()
            currentDate = QDate.currentDate()
            
            if cfg.showClockSeconds.value:
                timeString = currentTime.toString("HH:mm:ss")
            else:
                timeString = currentTime.toString("HH:mm")
            self.clockLabel.setText(timeString)
            
            # 公历日期
            solarString = currentDate.toString("yyyy 年 M 月 d 日 dddd")
            
            # 根据配置决定是否显示农历
            if hasattr(self, 'dateLabel') and cfg.showLunarCalendar.value:
                # 农历日期
                try:
                    # 将 QDate 转换为 datetime.datetime 对象
                    py_datetime = datetime.datetime(currentDate.year(), currentDate.month(), currentDate.day(), 0, 0, 0)
                    lunar = cnlunar.Lunar(py_datetime)
                    lunarMonthCn = lunar.lunarMonthCn
                    lunarDayCn = lunar.lunarDayCn
                    # 去掉月份中的"大"、"小"字
                    lunarMonthCn = lunarMonthCn.replace("大", "").replace("小", "")
                    lunarString = f"{lunarMonthCn}{lunarDayCn}"
                    dateString = f"{solarString} {lunarString}"
                except Exception as e:
                    logging.error(f"农历显示错误：{e}")
                    dateString = solarString
                
                self.dateLabel.setText(dateString)

    def mouseReleaseEvent(self, e):
        self._drag_pos = None
        self._resize_pos = None
        self.setCursor(Qt.ArrowCursor)
        super().mouseReleaseEvent(e)

    def setSelected(self, selected: bool):
        if selected:
            self.setStyleSheet('border: 2px dashed #0078D4;')
        else:
            self.setStyleSheet('border: none;')

    def text(self):
        if isinstance(self.inner, (QLabel, QPushButton)):
            return self.inner.text()
        return ''

    def setText(self, txt: str):
        if isinstance(self.inner, (QLabel, QPushButton)):
            self.inner.setText(txt)
