from PyQt6.QtCore import Qt, QPoint, QSize, pyqtSignal
from PyQt6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout, QApplication, QSizePolicy
from PyQt6.QtGui import QCursor, QPainter, QColor, QPen, QFont


class DraggableWidget(QWidget):
    """可拖拽的组件基类 - 支持百分比定位"""
    
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
        
        # 视觉反馈相关
        self._show_border = False
        self._border_color = QColor(100, 180, 255)
        self._hovered = False
        
        self.setMouseTracking(True)
        self.setAutoFillBackground(False)
        
        # 使用 Preferred 策略：适应内容大小
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Preferred)
    
    def setDraggable(self, enabled: bool):
        """设置是否可拖拽（编辑模式）"""
        self._draggable = enabled
        self._show_border = enabled
        self.update()
        
        if enabled:
            self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            self.raise_()
        else:
            self.setCursor(QCursor(Qt.CursorShape.ArrowCursor))
    
    def setPositionPercent(self, x: float, y: float):
        """设置百分比位置 (0.0~1.0)"""
        self._percent_x = max(0.0, min(1.0, x))
        self._percent_y = max(0.0, min(1.0, y))
        self._updatePositionFromPercent()
    
    def getPositionPercent(self) -> tuple:
        """获取当前位置的百分比"""
        return (self._percent_x, self._percent_y)
    
    def setAnchorMode(self, mode: str):
        """设置锚点模式"""
        valid_modes = ["topleft", "top", "topright", "left", "center", "right", 
                       "bottomleft", "bottom", "bottomright"]
        if mode in valid_modes:
            self._anchor_mode = mode
    
    def _updatePositionFromPercent(self):
        """根据百分比更新实际位置"""
        parent = self.parentWidget()
        if not parent:
            return
        
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
        """根据当前位置计算百分比"""
        parent = self.parentWidget()
        if not parent:
            return (self._percent_x, self._percent_y)
        
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
        """绘制边框和视觉反馈"""
        super().paintEvent(event)
        
        if self._show_border or self._dragging or self._hovered:
            painter = QPainter(self)
            painter.setRenderHint(painter.RenderHint.Antialiasing)
            
            pen_width = 3 if self._dragging else (2 if self._hovered else 1)
            border_color = QColor(255, 100, 100) if self._dragging else (
                QColor(100, 200, 255) if self._hovered else self._border_color
            )
            
            pen = QPen(border_color)
            pen.setWidth(pen_width)
            pen.setStyle(Qt.PenStyle.DashLine if not self._dragging else Qt.PenStyle.SolidLine)
            painter.setPen(pen)
            
            rect = self.rect().adjusted(1, 1, -1, -1)
            painter.drawRoundedRect(rect, 8, 8)
            
            if self._dragging or self._show_border:
                painter.setPen(QColor(255, 255, 255))
                font = QFont()
                font.setPointSize(9)
                font.setBold(True)
                painter.setFont(font)
                
                label_text = f"☰ {self.component_id}"
                painter.drawText(10, 20, label_text)
            
            painter.end()
    
    def enterEvent(self, event):
        """鼠标进入事件"""
        if self._draggable:
            self._hovered = True
            self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            self.update()
        super().enterEvent(event)
    
    def leaveEvent(self, event):
        """鼠标离开事件"""
        self._hovered = False
        if self._draggable and not self._dragging:
            self.setCursor(QCursor(Qt.CursorShape.ArrowCursor))
        self.update()
        super().leaveEvent(event)
    
    def mousePressEvent(self, event):
        """鼠标按下事件"""
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
        """鼠标移动事件"""
        if self._dragging and self._draggable:
            current_pos = event.globalPosition().toPoint()
            delta = current_pos - self._drag_start_pos
            
            new_pos = self._widget_start_pos + delta
            
            parent = self.parentWidget()
            if parent:
                parent_rect = parent.rect()
                new_pos.setX(max(0, min(new_pos.x(), parent_rect.width() - self.width())))
                new_pos.setY(max(0, min(new_pos.y(), parent_rect.height() - self.height())))
            
            self.move(new_pos)
            
            self._percent_x, self._percent_y = self._calculatePercentFromPosition()
            self.positionChanged.emit(self._percent_x, self._percent_y)
            
            event.accept()
            return
        
        super().mouseMoveEvent(event)
    
    def mouseReleaseEvent(self, event):
        """鼠标释放事件"""
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
        """父容器大小改变时调用 - 自动调整位置保持比例"""
        self._updatePositionFromPercent()


class DraggableContainer(DraggableWidget):
    """可拖拽的容器组件 - 用于包装现有布局"""
    
    def __init__(self, parent=None, component_id: str = "", layout_direction: str = "vertical"):
        super().__init__(parent, component_id)
        
        if layout_direction == "vertical":
            self.inner_layout = QVBoxLayout(self)
        else:
            self.inner_layout = QHBoxLayout(self)
        
        self.inner_layout.setContentsMargins(10, 10, 10, 10)
        self.inner_layout.setSpacing(5)
        
        # 使用 Preferred 策略：适应内容大小，但不会被强制扩展
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Preferred)
    
    def addWidget(self, widget):
        """添加子组件到容器中"""
        self.inner_layout.addWidget(widget)
        # 强制布局更新以计算正确的大小
        self.inner_layout.activate()
        self.adjustSize()
        self.updateGeometry()
    
    def showEvent(self, event):
        """显示时强制刷新布局"""
        super().showEvent(event)
        # 确保布局已激活并计算了正确的大小
        if self.inner_layout:
            self.inner_layout.activate()
            self.adjustSize()
    
    def sizeHint(self) -> QSize:
        """返回推荐大小（基于内容）"""
        # 强制激活布局以确保大小计算正确
        if self.inner_layout:
            self.inner_layout.activate()
        base_size = super().sizeHint()
        # 返回实际需要的大小
        return base_size
    
    def minimumSizeHint(self) -> QSize:
        """返回最小尺寸"""
        return QSize(80, 40)
