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

from PyQt6.QtCore import Qt, QPoint, QSize, pyqtSignal
from PyQt6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout, QApplication, QSizePolicy
from PyQt6.QtGui import QCursor, QPainter, QColor, QPen, QFont

from core.config import cfg


class DraggableWidget(QWidget):
    positionChanged = pyqtSignal(float, float)
    def __init__(self, parent=None, component_id: str = ""):
        super().__init__(parent)
        self.component_id = component_id
        
        self._dragging = False
        self._drag_start_pos = QPoint()
        self._widget_start_pos = QPoint()
        
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
                label_text = f"☰ {self.component_id}"
                painter.drawText(8, 18, label_text)
                
                w = self.width()
                h = self.height()
                
                diagonal_pen = QPen(QColor(255, 255, 255, 100))
                diagonal_pen.setWidth(1)
                diagonal_pen.setStyle(Qt.PenStyle.DashLine)
                painter.setPen(diagonal_pen)
                painter.drawLine(0, 0, w, h)
                painter.drawLine(w, 0, 0, h)
                
                center_x = w // 2
                center_y = h // 2
                center_pen = QPen(self._cached_primary_color)
                center_pen.setWidth(2)
                center_pen.setStyle(Qt.PenStyle.DashLine)
                painter.setPen(center_pen)
                painter.drawLine(center_x, 0, center_x, h)
                painter.drawLine(0, center_y, w, center_y)
            
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
            self.setCursor(QCursor(Qt.CursorShape.ClosedHandCursor))
            self.update()
            self.raise_()
            event.accept()
            return
        
        super().mousePressEvent(event)
    
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
            if main_window and hasattr(main_window, 'getSnapPosition'):
                snapped_x, snapped_y = main_window.getSnapPosition(new_pos.x(), new_pos.y(), self.width(), self.height())
                new_pos.setX(snapped_x)
                new_pos.setY(snapped_y)
            
            self.move(new_pos)
            self._percent_x, self._percent_y = self._calculatePercentFromPosition()
            self.positionChanged.emit(self._percent_x, self._percent_y)
            
            if main_window and hasattr(main_window, 'updateWidgetGuideLines'):
                main_window.updateWidgetGuideLines()
            
            event.accept()
            return
        
        super().mouseMoveEvent(event)
    
    def _getMainWindow(self):
        """获取主窗口"""
        widget = self.parentWidget()
        while widget:
            if hasattr(widget, 'getSnapPosition'):
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
        return base_size
    
    def minimumSizeHint(self) -> QSize:
        return QSize(80, 40)
