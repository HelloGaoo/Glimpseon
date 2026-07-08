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
UI组件模块
"""

import json
import logging
import os
import re
import sys
import time
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from typing import Optional, Dict

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

import pythoncom
import webbrowser

from PyQt6.QtCore import (
    QFileInfo,
    QPointF,
    QPoint,
    QRect,
    QRectF,
    Qt,
    QThread,
    QTimer,
    pyqtProperty,
    QSize,
    pyqtSignal, QObject,
    QByteArray, QPropertyAnimation, QEasingCurve,
    QTime, QDate,
    QMimeData, QDir,
)
from PyQt6.QtGui import (
    QBrush,
    QColor,
    QCursor,
    QFont,
    QFontMetrics,
    QLinearGradient,
    QPainter,
    QPainterPath,
    QPen,
    QPixmap, QIcon,
    QDrag, QImage, QPolygonF,
)
from PyQt6.QtSvg import QSvgRenderer
from PyQt6.QtWidgets import (
    QFileIconProvider, QGridLayout, QLabel, QSizePolicy, QWidget, QVBoxLayout, QHBoxLayout, QApplication, QProgressBar, QGraphicsOpacityEffect,
    QScrollArea, QStackedWidget, QPushButton, QListWidget, QListWidgetItem, QLineEdit,
)
from qfluentwidgets import InfoBar, isDarkTheme, RoundMenu, Action, FluentWindow, setTheme, ScrollArea, PushButton, ToolButton, TransparentToolButton, StrongBodyLabel, CardWidget, BodyLabel, SubtitleLabel, ComboBox, SpinBox, SwitchButton
from win32com.shell import shell, shellcon

from core.config import cfg, save_cfg
from core.utils import tr, FUI, get_cached_content, save_cache
from services.media import MediaInfo, Lyrics, get_media_info, fetch_all_info, close as close_media
from core.constants import BASE_DIR, load_qss
from data.software_list import get_software_icon_path
from core.component import (
    ComponentDefinition,
    ComponentRegistry,
    ResizeMode,
)

logger = logging.getLogger("ClassLively.ui.component")


def get_component_display_name(component_id: str) -> str:
    name_map = {
        "clock": tr("component.clock"),  # 时钟
        "weather": tr("component.weather"),  # 天气
        "poetry": tr("component.poetry"),  # 一言
        "countdown": tr("component.countdown"),  # 倒计时
        "school_info": tr("component.school_info"),  # 学校信息
        "media": tr("component.media"),  # 媒体信息
        "quick_launch": tr("component.quick_launch"),  # 快捷启动
    }
    return name_map.get(component_id, component_id)


# 组件系统
COMPONENT_STYLES = {
    "clock": {
        "digital": {
            "name": "数字时钟",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
        },
        "calendar_month": {
            "name": "月历",
            "class": None,
            "default_config": {},
            "default_size": (300, 300),
        },
    },
    "weather": {
        "icon_temp": {
            "name": "极简",
            "class": None,
            "default_config": {},
            "default_size": (200, 200),
        },
        "hourly": {
            "name": "逐小时天气",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
        },
        "weekly": {
            "name": "逐日天气",
            "class": None,
            "default_config": {},
            "default_size": (200, 200),
        },
    },
    "poetry": {
        "one_line": {
            "name": "一言",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
        },
    },
    "countdown": {
        "event": {
            "name": "事件倒计时",
            "class": None,
            "default_config": {},
            "default_size": (200, 200),
        },
    },
    "school_info": {
        "class_info": {
            "name": "课程信息",
            "class": None,
            "default_config": {},
            "default_size": (200, 200),
        },
    },
    "media": {
        "player": {
            "name": "播放器",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
        },
    },
    "quick_launch": {
        "dock": {
            "name": "快捷启动栏",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
        },
    },
}


COMPONENTS_CONFIG_PATH = os.path.join(BASE_DIR, "config", "components.json")


class ComponentManager:
    """组件管理器"""

    MAX_COMPONENTS = 100

    def __init__(self, home_interface):
        self.home = home_interface
        self.components = {}  # id > DraggableContainer 实例
        self._component_data = {}  # id > 原始配置数据

    def load_components(self):
        """从 config/components.json 加载组件"""
        if not os.path.exists(COMPONENTS_CONFIG_PATH):
            default_data = {"components": []}
            try:
                os.makedirs(os.path.dirname(COMPONENTS_CONFIG_PATH), exist_ok=True)
                with open(COMPONENTS_CONFIG_PATH, "w", encoding="utf-8") as f:
                    json.dump(default_data, f, indent=2, ensure_ascii=False)
            except Exception as e:
                logger.error(f"创建组件配置失败: {e}")
                return

        try:
            with open(COMPONENTS_CONFIG_PATH, "r", encoding="utf-8") as f:
                data = json.load(f)
        except Exception as e:
            logger.error(f"加载组件配置失败: {e}")
            return

        components_list = data.get("components", [])
        for comp_data in components_list[:self.MAX_COMPONENTS]:
            comp_id = comp_data.get("id")
            comp_type = comp_data.get("type")
            comp_style = comp_data.get("style")

            if not comp_id or not comp_type or not comp_style:
                logger.warning(f"组件数据不完整: {comp_data}")
                continue

            # 获取组件类
            style_info = COMPONENT_STYLES.get(comp_type, {}).get(comp_style)
            if not style_info or style_info.get("class") is None:
                logger.warning(f"组件样式未注册: {comp_type}/{comp_style}")
                continue

            comp_class = style_info["class"]
            try:
                instance = comp_class(self.home, comp_data)
                # 恢复组件尺寸 show() 设位置
                size_data = comp_data.get("size")
                if size_data and size_data.get("w") and size_data.get("h"):
                    instance.resize(size_data["w"], size_data["h"])
                else:
                    default_size = style_info.get("default_size", (200, 80))
                    instance.resize(*default_size)
                instance._size_explicitly_set = True
                if comp_data.get("enabled", True):
                    instance.show()
                else:
                    instance.hide()
                # show() 触发 showEvent 但 _size_explicitly_set=True 不会 adjustSize
                instance.setPositionPercent(
                    comp_data.get("position", {}).get("x", 0.5),
                    comp_data.get("position", {}).get("y", 0.5)
                )

                self.components[comp_id] = instance
                self._component_data[comp_id] = comp_data
                logger.info(f"加载组件: {comp_id} ({comp_type}/{comp_style})")
            except Exception as e:
                logger.error(f"创建组件失败 {comp_id}: {e}")

    def save_components(self):
        """保存到 config/components.json"""
        data = {"components": []}
        for comp_id, instance in self.components.items():
            pos_x, pos_y = instance.getPositionPercent()
            comp_data = {
                "id": comp_id,
                "type": self._component_data.get(comp_id, {}).get("type", "unknown"),
                "style": self._component_data.get(comp_id, {}).get("style", "unknown"),
                "position": {"x": pos_x, "y": pos_y},
                "size": {"w": instance.width(), "h": instance.height()},
                "enabled": instance.isVisible(),
                "config": self._component_data.get(comp_id, {}).get("config", {}),
            }
            pro = self._component_data.get(comp_id, {}).get("pro")
            if pro is not None:
                comp_data["pro"] = pro

            data["components"].append(comp_data)

        try:
            with open(COMPONENTS_CONFIG_PATH, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2, ensure_ascii=False)
            logger.info(f"保存组件配置到: {COMPONENTS_CONFIG_PATH}")
        except Exception as e:
            logger.error(f"保存组件配置失败: {e}")

    def add_component(self, comp_type: str, comp_style: str, config=None, pro=None) -> str:
        """添加新组件"""
        if len(self.components) >= self.MAX_COMPONENTS:
            logger.warning(f"组件数量上限: {self.MAX_COMPONENTS}")
            return ""

        style_info = COMPONENT_STYLES.get(comp_type, {}).get(comp_style)
        if not style_info or style_info.get("class") is None:
            logger.warning(f"组件样式未注册: {comp_type}/{comp_style}")
            return ""

        comp_id = self._generate_id(comp_type)
        default_size = style_info.get("default_size", (200, 80))
        comp_data = {
            "id": comp_id,
            "type": comp_type,
            "style": comp_style,
            "position": {"x": 0.5, "y": 0.5},
            "size": {"w": default_size[0], "h": default_size[1]},
            "enabled": True,
            "config": config or style_info.get("default_config", {}),
            "pro": pro,
        }

        comp_class = style_info["class"]
        try:
            instance = comp_class(self.home, comp_data)
            instance.resize(*default_size)
            instance._size_explicitly_set = True
            instance.show()
            # show() 触发 showEvent > adjustSize() 可能改变尺寸
            # 目的防止加载数据之后布局变了
            instance.setPositionPercent(0.5, 0.5)

            self.components[comp_id] = instance
            self._component_data[comp_id] = comp_data
            self.save_components()
            logger.info(f"添加组件: {comp_id} ({comp_type}/{comp_style})")
            return comp_id
        except Exception as e:
            logger.error(f"创建组件失败: {e}")
            return ""

    def remove_component(self, comp_id: str):
        """删除组件"""
        if comp_id not in self.components:
            logger.warning(f"组件不存在: {comp_id}")
            return

        instance = self.components[comp_id]
        instance.deleteLater()
        del self.components[comp_id]
        del self._component_data[comp_id]
        self.save_components()
        logger.info(f"删除组件: {comp_id}")

    def get_all_containers(self) -> list:
        """返回 DraggableContainer 实例"""
        return list(self.components.values())

    def get_component_data(self, comp_id: str) -> dict:
        """获取单个组件的配置数据"""
        return self._component_data.get(comp_id, {})

    def update_component_config(self, comp_id: str, config: dict, pro=None):
        """更新组件配置"""
        if comp_id not in self._component_data:
            logger.warning(f"组件不存在: {comp_id}")
            return

        self._component_data[comp_id]["config"] = config
        if pro is not None:
            self._component_data[comp_id]["pro"] = pro
        self.save_components()

    def _generate_id(self, comp_type: str) -> str:
        """生成组件ID"""
        existing_ids = set(self.components.keys())
        counter = 1
        while f"comp_{comp_type}_{counter}" in existing_ids:
            counter += 1
        return f"comp_{comp_type}_{counter}"


class DraggableWidget(QWidget):
    positionChanged = pyqtSignal(float, float)
    selected = pyqtSignal(str)
    # settingRequested = pyqtSignal(str)  # 弃
    def __init__(self, parent=None, component_id: str = ""):
        super().__init__(parent)
        self.component_id = component_id
        self._dragging = False
        self._drag_start_pos = QPoint()
        self._widget_start_pos = QPoint()
        self._click_start_pos = QPoint()
        self._percent_x = 0.5
        self._percent_y = 0.5
        self._draggable = False
        self._anchor_mode = "topleft"
        self._selected = False
        self._resizing = False
        self._resize_start_pos = QPoint()
        self._resize_start_size = QSize()
        self._resize_start_geo = QRect()
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

    def setSelected(self, selected: bool):
        self._selected = selected
        self.update()

    def isSelected(self) -> bool:
        return self._selected
    
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
                x = int(available_width * (1 - self._percent_x))
            elif self._anchor_mode == "bottomleft":
                y = int(available_height * (1 - self._percent_y))
            elif self._anchor_mode == "bottomright":
                x = int(available_width * (1 - self._percent_x))
                y = int(available_height * (1 - self._percent_y))
            elif self._anchor_mode == "center":
                x = int(available_width / 2)
                y = int(available_height / 2)
            
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

        if getattr(self, '_dragging', False): return

        # 选中后
        if getattr(self, '_selected', False):
            painter = QPainter(self)
            painter.setRenderHint(painter.RenderHint.Antialiasing)
            color = QColor(getattr(self, '_cached_primary_color', QColor(48, 195, 97)))

            # 选中框在组件边缘向内缩进4px
            border_rect = QRectF(self.rect()).adjusted(4, 4, -4, -4)

            # 外发光
            glow_color = QColor(color)
            glow_color.setAlpha(60)
            for i in range(4, 0, -1):
                glow_pen = QPen(glow_color)
                glow_pen.setWidthF(i * 2)
                painter.setPen(glow_pen)
                painter.setBrush(Qt.BrushStyle.NoBrush)
                painter.drawRoundedRect(border_rect, 8, 8)

            # 选中边框
            color.setAlpha(200)
            pen = QPen(color)
            pen.setWidthF(2)
            painter.setPen(pen)
            painter.setBrush(Qt.BrushStyle.NoBrush)
            painter.drawRoundedRect(QRectF(self.rect()).adjusted(3, 3, -3, -3), 8, 8)

            # 弧线
            w = self.width()
            h = self.height()
            arc_r = 18
            arc_cx = w - 1
            arc_cy = h - 1
            arc_rect = QRectF(arc_cx - arc_r, arc_cy - arc_r, arc_r * 2, arc_r * 2)

            outer_color = color.darker(150)
            outer_color.setAlpha(220)
            outer_pen = QPen(outer_color)
            outer_pen.setWidthF(7)
            outer_pen.setCapStyle(Qt.PenCapStyle.RoundCap)
            painter.setPen(outer_pen)
            painter.drawArc(arc_rect, int(-30 * 16), int(-60 * 16))

            # 内层弧线
            inner_color = QColor(color)
            inner_color.setAlpha(230)
            inner_pen = QPen(inner_color)
            inner_pen.setWidthF(4)
            inner_pen.setCapStyle(Qt.PenCapStyle.RoundCap)
            painter.setPen(inner_pen)
            painter.drawArc(arc_rect, int(-30 * 16), int(-60 * 16))

            painter.end()
            return

        if getattr(self, '_show_border', False) or getattr(self, '_hovered', False):
            painter = QPainter(self)
            painter.setRenderHint(painter.RenderHint.Antialiasing)

            if getattr(self, '_hovered', False):
                border_color = QColor(getattr(self, '_cached_primary_color', QColor(48, 195, 97)))
                border_color.setAlpha(160)
            else:
                border_color = QColor(0, 0, 0, 30) if not isDarkTheme() else QColor(255, 255, 255, 30)

            pen = QPen(border_color)
            pen.setWidthF(1)
            pen.setStyle(Qt.PenStyle.DashLine)
            painter.setPen(pen)
            painter.drawRoundedRect(QRectF(self.rect()).adjusted(1, 1, -1, -1), 4, 4)

            if getattr(self, '_show_border', False):
                display_name = get_component_display_name(getattr(self, 'component_id', ''))
                font = QFont("HarmonyOS Sans")
                font.setPixelSize(14)
                painter.setFont(font)
                painter.setPen(QColor(0, 0, 0, 100) if not isDarkTheme() else QColor(255, 255, 255, 100))
                painter.drawText(8, 16, display_name)

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
            if self._selected and self._hitResizeHandle(self.mapFromGlobal(QCursor.pos())):
                self.setCursor(QCursor(Qt.CursorShape.SizeFDiagCursor))
            else:
                self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            self.update()
        super().enterEvent(event)
    
    def leaveEvent(self, event):
        """鼠标离开事件"""
        self._hovered = False
        if self._draggable and not self._dragging:self.setCursor(QCursor(Qt.CursorShape.ArrowCursor))
        self.update()
        super().leaveEvent(event)
    
    def _hitResizeHandle(self, pos) -> bool:
        """检测点击位置是否在右下角缩放"""
        if not self._selected:
            return False
        handle_zone = 24
        return (pos.x() >= self.width() - handle_zone and
                pos.y() >= self.height() - handle_zone)

    def mousePressEvent(self, event):
        if self._draggable and event.button() == Qt.MouseButton.LeftButton:
            # 选中状态
            if self._selected and self._hitResizeHandle(event.position().toPoint()):
                self._resizing = True
                self._resize_start_pos = event.globalPosition().toPoint()
                self._resize_start_size = self.size()
                self._resize_start_geo = self.geometry()
                self.setCursor(QCursor(Qt.CursorShape.SizeFDiagCursor))
                event.accept()
                return

            self._dragging = True
            self._drag_start_pos = event.globalPosition().toPoint()
            self._widget_start_pos = self.pos()
            self._click_start_pos = event.globalPosition().toPoint()
            self.setCursor(QCursor(Qt.CursorShape.ClosedHandCursor))
            self.update()
            self.raise_()
            event.accept()
            return
        
        super().mousePressEvent(event)
    
    def mouseDoubleClickEvent(self, event):
        if self._draggable and event.button() == Qt.MouseButton.LeftButton:
            self._dragging = False
            self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            # self.settingRequested.emit(self.component_id)
            event.accept()
            return
        super().mouseDoubleClickEvent(event)
    
    def mouseMoveEvent(self, event):
        # 缩放模式
        if self._resizing and self._draggable:
            delta = event.globalPosition().toPoint() - self._resize_start_pos
            orig_w = self._resize_start_size.width()
            orig_h = self._resize_start_size.height()
            # 等比缩放
            new_w = max(self.minimumWidth() or 80, orig_w + delta.x())
            new_h = max(self.minimumHeight() or 40, orig_h + delta.y())
            scale_w = new_w / orig_w if orig_w > 0 else 1.0
            scale_h = new_h / orig_h if orig_h > 0 else 1.0
            scale = max(scale_w, scale_h)
            new_w = int(orig_w * scale)
            new_h = int(orig_h * scale)
            parent = self.parentWidget()
            if parent:
                new_w = min(new_w, parent.width() - self.x())
                new_h = min(new_h, parent.height() - self.y())
            self.resize(new_w, new_h)
            event.accept()
            return

        # 拖动模式
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
            align_lines = []
            if main_window and hasattr(main_window, '_computeSnap'):
                snapped_x, snapped_y, align_lines = main_window._computeSnap(
                    new_pos.x(), new_pos.y(), self.width(), self.height(), self
                )
                new_pos.setX(snapped_x)
                new_pos.setY(snapped_y)
            
            self.move(new_pos)
            self._percent_x, self._percent_y = self._calculatePercentFromPosition()
            self.positionChanged.emit(self._percent_x, self._percent_y)
            
            if main_window and hasattr(main_window, '_guideOverlay') and main_window._guideOverlay:
                main_window._guideOverlay.setAlignLines(align_lines)
            
            event.accept()
            return
        
        super().mouseMoveEvent(event)
    
    def _getMainWindow(self):
        widget = self.parentWidget()
        while widget:
            if hasattr(widget, '_computeSnap'):
                return widget
            widget = widget.parentWidget()
        return None
    
    def mouseReleaseEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            # 缩放结束
            if self._resizing:
                self._resizing = False
                self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
                # 保存
                home = self._getMainWindow()
                if home and hasattr(home, 'homeInterface') and home.homeInterface:
                    mgr = getattr(home.homeInterface, 'component_manager', None)
                    if mgr:
                        mgr.save_components()
                event.accept()
                return

            # 拖动结束
            if self._dragging:
                self._dragging = False
                if self._draggable:
                    self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
                else:
                    self.setCursor(QCursor(Qt.CursorShape.ArrowCursor))
                # 保存
                home = self._getMainWindow()
                if home and hasattr(home, 'homeInterface') and home.homeInterface:
                    mgr = getattr(home.homeInterface, 'component_manager', None)
                    if mgr:
                        mgr.save_components()
            
            self.update()

            main_window = self._getMainWindow()
            if main_window and hasattr(main_window, 'clearDragAlignLines'):
                main_window.clearDragAlignLines()

            if self._draggable and hasattr(self, '_click_start_pos'):
                delta = event.globalPosition().toPoint() - self._click_start_pos
                if abs(delta.x()) < 5 and abs(delta.y()) < 5:
                    self.selected.emit(self.component_id)
                    event.accept()
                    return
            
            # 保存
            self._percent_x, self._percent_y = self._calculatePercentFromPosition()
            main_win = self._getMainWindow()
            if main_win and hasattr(main_win, 'homeInterface') and main_win.homeInterface:
                mgr = main_win.homeInterface.component_manager if hasattr(main_win.homeInterface, 'component_manager') else None
                if mgr:
                    mgr.save_components()

            event.accept()
            return
        
        super().mouseReleaseEvent(event)
    
    def onParentResize(self):
        self._updatePositionFromPercent()


def _create_delete_button(parent_widget, component_widget, on_clicked):
    """创建删除按钮"""
    btn = ToolButton(FUI.DELETE, parent_widget)
    btn.setFixedSize(28, 28)
    btn.setIconSize(QSize(14, 14))
    btn.setCursor(QCursor(Qt.CursorShape.ArrowCursor))
    btn.hide()
    btn.clicked.connect(on_clicked)

    # 绑定定位方法
    def _reposition():
        comp_pos = component_widget.mapTo(parent_widget, QPoint(0, 0))
        x = comp_pos.x() + component_widget.width() - btn.width() + 4
        y = comp_pos.y() + component_widget.height() + 4
        btn.move(x, y)

    btn.reposition = _reposition
    return btn


class DraggableContainer(DraggableWidget):
    
    def __init__(self, parent=None, component_id: str = "", layout_direction: str = "vertical"):
        super().__init__(parent, component_id)

        self._content_visible = True
        self._delete_button = None
        self._scale_factor = 1.0
        self._size_explicitly_set = False  # 是否已显式设置尺寸

        if layout_direction == "vertical":
            self.inner_layout = QVBoxLayout(self)
        else:
            self.inner_layout = QHBoxLayout(self)

        self.inner_layout.setContentsMargins(10, 10, 10, 10)
        self.inner_layout.setSpacing(5)
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Preferred)
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
    
    def setContentVisible(self, visible: bool):
        """隐藏/显示内容"""
        self._content_visible = visible
        for i in range(self.inner_layout.count()):
            item = self.inner_layout.itemAt(i)
            if item and item.widget():
                item.widget().setVisible(visible)
        if visible:
            self.inner_layout.setContentsMargins(10, 10, 10, 10)
            self.setMinimumSize(80, 40)
            self.setMaximumSize(16777215, 16777215)
            self.inner_layout.activate()
            self.adjustSize()
        else:
            self.inner_layout.setContentsMargins(16, 10, 16, 10)
            display_name = get_component_display_name(self.component_id)
            text = f"⚙ {display_name} {tr('component.click_to_settings')}"
            font = QFont()
            font.setPointSize(8)
            fm = QFontMetrics(font)
            text_width = fm.horizontalAdvance(text) + 24
            self.setFixedSize(max(text_width, 100), 36)
        self.updateGeometry()
        self.update()
    
    def addWidget(self, widget):
        self.inner_layout.addWidget(widget)
        self.inner_layout.activate()
        self.adjustSize()
        self.updateGeometry()
    
    def updateSize(self):
        self.inner_layout.activate()
        if not getattr(self, '_size_explicitly_set', False):
            self.adjustSize()
        self.updateGeometry()

    def _ensureEditControls(self):
        if self._delete_button is None:
            home = self._getHomeInterface()
            # 按钮放在 home 上
            parent_widget = home if home else self
            self._delete_button = _create_delete_button(
                parent_widget, self, self._onDeleteClicked
            )

    def showEditControls(self, visible: bool):
        self._ensureEditControls()
        self._delete_button.setVisible(visible)
        if visible:
            self._delete_button.reposition()
            self._delete_button.raise_()

    def _onDeleteClicked(self):
        home = self._getHomeInterface()
        if home:
            home.deleteSelectedComponent(self.component_id)

    def _getHomeInterface(self):
        widget = self.parentWidget()
        while widget:
            if hasattr(widget, 'component_manager'):
                return widget
            widget = widget.parentWidget()
        return None

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if self._delete_button and self._delete_button.isVisible():
            self._delete_button.reposition()
        self._applyUniformScale()

    def _applyUniformScale(self):
        """缩放所有子控件的字体和间距"""
        if not getattr(self, '_content_visible', True):
            return
        # adjustSize 后的 preferred size
        natural_size = self.sizeHint()
        if natural_size.width() <= 0 or natural_size.height() <= 0:
            return
        current_w = self.width()
        if current_w <= 0:
            return
        new_scale = current_w / natural_size.width()
        if abs(new_scale - self._scale_factor) < 0.01:
            return
        self._scale_factor = new_scale

        for child in self.findChildren(QWidget):
            font = child.font()
            if not hasattr(child, '_orig_font_size'):
                child._orig_font_size = font.pointSizeF() if font.pointSizeF() > 0 else font.pixelSize()
                child._orig_is_pixel = font.pointSizeF() <= 0
            orig = child._orig_font_size
            if child._orig_is_pixel:
                font.setPixelSize(max(1, int(orig * new_scale)))
            else:
                font.setPointSizeF(orig * new_scale)
            child.setFont(font)

        if hasattr(self, 'inner_layout'):
            if not hasattr(self, '_orig_margins'):
                self._orig_margins = self.inner_layout.contentsMargins()
                self._orig_spacing = self.inner_layout.spacing()
            m = self._orig_margins
            self.inner_layout.setContentsMargins(
                int(m.left() * new_scale), int(m.top() * new_scale),
                int(m.right() * new_scale), int(m.bottom() * new_scale)
            )
            self.inner_layout.setSpacing(int(self._orig_spacing * new_scale))
    
    def showEvent(self, event):
        super().showEvent(event)
        if getattr(self, 'inner_layout', None):
            self.inner_layout.activate()
            if not getattr(self, '_size_explicitly_set', False):
                self.adjustSize()
    
    def sizeHint(self) -> QSize:
        if getattr(self, '_size_explicitly_set', False):
            return self.size()
        if getattr(self, 'inner_layout', None):self.inner_layout.activate()
        base_size = super().sizeHint()
        if not getattr(self, '_content_visible', True):
            display_name = get_component_display_name(self.component_id)
            text = f"⚙ {display_name} {tr('component.click_to_settings')}"
            font = QFont()
            font.setPointSize(8)
            fm = QFontMetrics(font)
            return QSize(max(fm.horizontalAdvance(text) + 24, 100), 36)
        return base_size
    
    def minimumSizeHint(self) -> QSize:
        if not getattr(self, '_content_visible', True):
            return self.sizeHint()
        return QSize(80, 40)
    
    def paintEvent(self, event):
        if not getattr(self, '_content_visible', True):
            painter = QPainter(self)
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            
            border_color = QColor(0, 0, 0, 30) if not isDarkTheme() else QColor(255, 255, 255, 30)
            pen = QPen(border_color)
            pen.setWidthF(1)
            pen.setStyle(Qt.PenStyle.DashLine)
            painter.setPen(pen)
            painter.drawRoundedRect(QRectF(self.rect()).adjusted(1, 1, -1, -1), 4, 4)

            display_name = get_component_display_name(self.component_id)
            font = QFont("HarmonyOS Sans")
            font.setPixelSize(14)
            painter.setFont(font)
            painter.setPen(QColor(0, 0, 0, 100) if not isDarkTheme() else QColor(255, 255, 255, 100))
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, display_name)
            painter.end()
            return
        super().paintEvent(event)



class LyricsWidget(QWidget):
    """歌词显示控件"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self._original_text = ""
        self._text_size = 14
        self._lyrics = None
        self._lyrics_color = "#FFFFFFB3"

        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)
        self.setFixedHeight(self._text_size + 8)

    def set_text_size(self, size: int):
        self._text_size = max(size, 10)
        self.setFixedHeight(self._text_size + 8)
        self.update()

    def set_visible_lines(self, n: int):
        pass

    def set_lyrics(self, lyrics):
        self._lyrics = lyrics
        if lyrics and not lyrics.is_empty() and lyrics.lines:
            line = lyrics.lines[0]
            text = line.text if line else ""
        else:
            text = ""
        self._update_text(text)

    def set_lyrics_color(self, color: str):
        self._lyrics_color = color
        self.update()

    def update_position(self, ms: int):
        if not self._lyrics or self._lyrics.is_empty():
            return
        advance = cfg.mediaLyricsAdvance.value
        adjusted_ms = ms + advance
        _, idx = self._lyrics.get_line_at_time(adjusted_ms)
        if idx >= 0 and idx < len(self._lyrics.lines):
            text = self._lyrics.lines[idx].text
        else:
            text = ""
        self._update_text(text)

    def _update_text(self, text):
        self._original_text = text
        self.update()

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setRenderHint(QPainter.RenderHint.TextAntialiasing)

        if not self._original_text:
            p.end()
            return

        font = QFont("HarmonyOS Sans", self._text_size)
        font.setWeight(QFont.Weight.DemiBold)
        p.setFont(font)

        fm = p.fontMetrics()
        available = max(self.width() - 4, 0)
        elided = fm.elidedText(self._original_text, Qt.TextElideMode.ElideRight, available)

        lyrics_color = QColor(self._lyrics_color)
        p.setPen(lyrics_color)
        p.drawText(0, 0, self.width(), self.height(),
                   Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter,
                   elided)
        p.end()

    def clear(self):
        self._original_text = ""
        self._lyrics = None
        self.update()


class MediaProgressBar(QProgressBar):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setTextVisible(False)
        self.apply_style()

    def apply_style(self):
        height = cfg.mediaProgressHeight.value
        self.setFixedHeight(height)

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(Qt.PenStyle.NoPen)

        track_color = QColor(cfg.mediaProgressTrackColor.value)
        p.setBrush(track_color)
        h = self.height()
        p.drawRoundedRect(0, 1, self.width(), h - 2, 3, 3)

        if self.value() > 0:
            w = int(self.width() * self.value() / self.maximum()) if self.maximum() > 0 else 0
            # allow parent widget to override progress color (e.g. follow wallpaper)
            progress_color = QColor(cfg.mediaProgressColor.value)
            # try:
            parent_widget = self.parent()
            # except Exception:
            #     parent_widget = None
            if parent_widget and hasattr(parent_widget, '_override_progress_color') and parent_widget._override_progress_color:
                progress_color = parent_widget._override_progress_color
            p.setBrush(progress_color)
            p.drawRoundedRect(0, 1, w, h - 2, 3, 3)

        p.end()


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


class _MediaFetchWorker(QObject):
    """获取(QThread)媒体信息回传主线程"""
    finished = pyqtSignal(object, bool)

    def __init__(self, full=True):
        super().__init__()
        self._full = full

    def run(self):
        m = None
        try:
            m = get_media_info()
            if not m or not m.is_valid():
                m = None
        except Exception as e:
            logger.error(f"媒体信息获取异常: {e}")
        self.finished.emit(m, self._full)


class _KugouThumbWorker(QObject):
    """获取(QThread)酷狗图"""
    finished = pyqtSignal(object)

    def run(self):
        info = None
        try:
            from services.media import get_gstmtc
            gsmtc = get_gstmtc()
            if gsmtc and gsmtc.available:
                info = gsmtc.get_info()
        except Exception:
            pass
        self.finished.emit(info)


class MediaWidget(QWidget):
    """媒体信息显示控件"""

    _fetch_done = pyqtSignal(object, bool)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("mediaWidget")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)

        self._media: Optional[MediaInfo] = None
        self._lyrics: Optional[Lyrics] = None
        self._last_ta = ""
        self._cover: Optional[QPixmap] = None
        self._has_gstmtc_cover: bool = False
        self._fetching = False
        self._pending_full = False
        self._cache = {}
        self._duration = 0
        self._position = 0
        self._playing = False
        self._thread = None
        self._worker = None
        self._kugou_thread = None
        self._kugou_worker = None
        self._info_cache = OrderedDict()
        self._rapid_update_count = 0
        self._normal_interval = cfg.mediaUpdateInterval.value * 1000

        self._init_ui()
        self._setup_timers()
        self._apply_config()
        self._init_cover_animation()
        self._fetch_done.connect(self._on_fetched)

    @staticmethod
    def _qss_color(color_val):
        if isinstance(color_val, QColor):
            c = color_val
        elif color_val == "primary":
            from qfluentwidgets import Theme, isDarkTheme
            c = QColor(0, 0, 0) if not isDarkTheme() else QColor(255, 255, 255)
        else:
            if hasattr(color_val, 'name'):
                color_val = color_val.name()
            c = QColor(color_val)
        return f"rgba({c.red()}, {c.green()}, {c.blue()}, {round(c.alpha() / 255, 2)})"

    def _init_ui(self):
        self.setStyleSheet(load_qss('home.qss'))

        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(16, 14, 16, 12)
        main_layout.setSpacing(0)

        top_row = QHBoxLayout()
        top_row.setSpacing(18)
        top_row.setContentsMargins(0, 0, 0, 0)

        self._cover_lbl = QLabel()
        self._cover_lbl.setObjectName("mediaCoverLabel")
        self._cover_lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._default_cover()
        top_row.addWidget(self._cover_lbl, 0, Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)

        right_col = QVBoxLayout()
        right_col.setSpacing(6)
        right_col.setContentsMargins(0, 2, 0, 0)

        self._title = QLabel(tr("media.not_playing"))  # 未在播放
        self._title.setObjectName("mediaTitleLabel")
        self._title.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        right_col.addWidget(self._title)

        self._artist = QLabel("")
        self._artist.setObjectName("mediaArtistLabel")
        self._artist.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        right_col.addWidget(self._artist)

        self._lyrics_w = LyricsWidget()
        self._lyrics_w.setObjectName("mediaLyricsWidget")
        self._lyrics_w.set_visible_lines(1)
        right_col.addWidget(self._lyrics_w)

        prog = QWidget()
        prog.setObjectName("mediaProgressContainer")
        pl = QHBoxLayout(prog)
        pl.setContentsMargins(0, 4, 0, 0)
        pl.setSpacing(8)

        self._time = QLabel("0:00")
        self._time.setObjectName("mediaTimeLabel")
        self._time.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        pl.addWidget(self._time)

        self._bar = MediaProgressBar()
        self._bar.setRange(0, 100)
        pl.addWidget(self._bar, 1)

        self._dur = QLabel("0:00")
        self._dur.setObjectName("mediaDurationLabel")
        self._dur.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        pl.addWidget(self._dur)

        self._prog_container = prog
        right_col.addWidget(self._prog_container)

        top_row.addLayout(right_col, 1)
        main_layout.addLayout(top_row)

        self.setSizePolicy(QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Fixed)
        
    def _get_content_height(self) -> int:
        return cfg.mediaHeight.value

    def _default_cover(self):
        sz = cfg.mediaCoverSize.value
        radius = cfg.mediaCoverBorderRadius.value
        pm = QPixmap(sz, sz)
        pm.fill(Qt.GlobalColor.transparent)
        p = QPainter(pm)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(Qt.PenStyle.NoPen)

        border_color = QColor(cfg.mediaCoverBorderColor.value)
        p.setBrush(border_color)
        p.drawRoundedRect(0, 0, sz, sz, radius, radius)

        inner_color = QColor(255, 255, 255, 25)
        p.setBrush(inner_color)
        p.drawRoundedRect(2, 2, sz - 4, sz - 4, max(radius - 2, 2), max(radius - 2, 2))

        shadow_color = QColor(0, 0, 0, 40)
        for i in range(3):
            p.setPen(Qt.PenStyle.NoPen)
            p.setBrush(QColor(0, 0, 0, 15 - i * 4))
            offset = (i + 1) * 2
            p.drawRoundedRect(offset, offset, sz, sz, radius, radius)
        p.end()
        self._cover_lbl.setPixmap(pm)

    def _setup_timers(self):
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._update)
        self._prog_timer = QTimer(self)
        self._prog_timer.timeout.connect(self._update_progress)

    def _apply_config(self):
        sz = max(cfg.mediaTextSize.value, 10)
        title_color = self._qss_color(cfg.mediaTitleColor.value)
        artist_color = self._qss_color(cfg.mediaArtistColor.value)
        time_color = self._qss_color(cfg.mediaTimeColor.value)
        lyrics_color = cfg.mediaLyricsColor.value

        self._title.setStyleSheet(
            f"font-size: {sz + 2}px; font-weight: 600;"
            f"color: {title_color};"
            f"font-family: 'HarmonyOS Sans', 'Microsoft YaHei', 'SimHei', sans-serif;"
        )
        self._artist.setStyleSheet(
            f"font-size: {sz}px;"
            f"color: {artist_color};"
            f"font-family: 'HarmonyOS Sans', 'Microsoft YaHei', 'SimHei', sans-serif;"
        )
        self._time.setStyleSheet(f"color: {time_color};")
        self._dur.setStyleSheet(f"color: {time_color};")
        self._lyrics_w.set_text_size(cfg.mediaLyricsSize.value)
        self._lyrics_w.set_visible_lines(1)
        self._lyrics_w.set_lyrics_color(lyrics_color)

        cover_sz = cfg.mediaCoverSize.value
        self._cover_lbl.setFixedSize(cover_sz, cover_sz)
        if self._cover and not self._cover.isNull():
            cover_with_shadow = self._add_cover_shadow(self._cover, cover_sz)
            self._cover_lbl.setPixmap(cover_with_shadow)
        else:
            self._default_cover()

        self._bar.apply_style()
        self.setFixedSize(cfg.mediaWidth.value, cfg.mediaHeight.value)
        self._apply_background_style()

    def _get_wallpaper_color(self):
        """提取壁纸主色 失败返回None"""
        try:
            home = self.parent()
            mw = getattr(home, 'mainWindow', None) if home is not None else None
            if mw and hasattr(mw, 'wallpaper') and mw.wallpaper:
                return mw.wallpaper.get_dominant_color()
        except Exception:
            pass
        return None

    @staticmethod
    def _get_system_accent_color():
        """AccentPalette[2]读取系统主题色 失败none"""
        import winreg
        try:
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                r'Software\Microsoft\Windows\CurrentVersion\Explorer\Accent')
            palette = winreg.QueryValueEx(key, 'AccentPalette')[0]
            winreg.CloseKey(key)
            if len(palette) >= 12:
                b, g, r = palette[8], palette[9], palette[10]
                return QColor(r, g, b)
        except Exception:
            pass
        return None

    def _apply_background_style(self):
        # 独立处理进度条颜色：支持 'wallpaper' / 'system' / 具体颜色值
        progress_color_val = cfg.mediaProgressColor.value
        actual_progress_value = progress_color_val.name() if hasattr(progress_color_val, 'name') else progress_color_val
        if actual_progress_value == 'wallpaper':
            self._override_progress_color = self._get_wallpaper_color()
        elif actual_progress_value == 'system':
            self._override_progress_color = self._get_system_accent_color()
        elif actual_progress_value != 'primary':
            self._override_progress_color = QColor(actual_progress_value)
        else:
            self._override_progress_color = None

        if not cfg.mediaUseCustomBg.value:
            self.setStyleSheet("")
            from qfluentwidgets import isDarkTheme
            text_color = '#FFFFFF' if isDarkTheme() else '#000000'
        else:
            bg_opacity = cfg.mediaBgOpacity.value
            border_radius = cfg.mediaBorderRadius.value

            from qfluentwidgets import isDarkTheme
            if isDarkTheme():
                c = QColor(30, 30, 30)
            else:
                c = QColor(255, 255, 255)
            c.setAlpha(int(255 * bg_opacity / 100))

            if c.alpha() == 0:
                bg_css = "background-color: transparent;"
            else:
                bg_css = f"background-color: {self._qss_color(c)};"

            self.setStyleSheet(
                f"#mediaWidget {{"
                f"{bg_css}"
                f"border-radius: {border_radius}px;"
                f"}}"
            )

            bright = (c.red() * 299 + c.green() * 587 + c.blue() * 114) / 1000
            text_color = '#000000' if bright > 160 else '#FFFFFF'

        title_color_hex = text_color
        artist_color_hex = text_color + '66'
        time_color_hex = text_color + 'CC'
        try:
            base_title = self._title.styleSheet().split('color:')[0]
        except Exception:
            base_title = ''
        try:
            base_artist = self._artist.styleSheet().split('color:')[0]
        except Exception:
            base_artist = ''
        self._title.setStyleSheet(base_title + f"color: {title_color_hex};")
        self._artist.setStyleSheet(base_artist + f"color: {artist_color_hex};")
        self._time.setStyleSheet(f"color: {time_color_hex};")
        self._dur.setStyleSheet(f"color: {time_color_hex};")
        self._lyrics_w.set_lyrics_color(cfg.mediaLyricsColor.value)

    def _add_cover_shadow(self, pixmap: QPixmap, size: int) -> QPixmap:
        radius = cfg.mediaCoverBorderRadius.value
        result = QPixmap(size + 8, size + 8)
        result.fill(Qt.GlobalColor.transparent)

        p = QPainter(result)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)

        shadow_color = QColor(0, 0, 0, 50)
        for i in range(4):
            p.setPen(Qt.PenStyle.NoPen)
            p.setBrush(QColor(0, 0, 0, 20 - i * 4))
            offset = (i + 1) * 2
            p.drawRoundedRect(offset, offset, size, size, radius, radius)

        rounded = QPixmap(size, size)
        rounded.fill(Qt.GlobalColor.transparent)
        p2 = QPainter(rounded)
        p2.setRenderHint(QPainter.RenderHint.Antialiasing)
        p2.setPen(Qt.PenStyle.NoPen)
        p2.setBrush(Qt.BrushStyle.SolidPattern)
        p2.drawRoundedRect(0, 0, size, size, radius, radius)
        p2.setCompositionMode(QPainter.CompositionMode.CompositionMode_SourceAtop)
        p2.drawPixmap(0, 0, pixmap.scaled(size, size, Qt.AspectRatioMode.KeepAspectRatioByExpanding, Qt.TransformationMode.SmoothTransformation))
        p2.end()

        p.drawPixmap(4, 4, rounded)
        p.end()
        return result

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
        self._fetch_in_thread(full=True)

    def _fetch_in_thread(self, full=True):
        if self._fetching:
            if full:
                self._pending_full = True
            return
        # import threading
        self._fetching = True
        self._pending_full = False
        # t = threading.Thread(target=self._do_fetch, args=(full,), daemon=True)
        # t.start()
        self._thread = QThread()
        self._worker = _MediaFetchWorker(full)
        self._worker.moveToThread(self._thread)
        self._thread.started.connect(self._worker.run)
        self._worker.finished.connect(self._on_worker_done)
        self._thread.start()

    # def _do_fetch(self, full):
    #     try:
    #         m = get_media_info()
    #         if not m or not m.is_valid():
    #             logger.debug(f"媒体组件: 无效信息")
    #     except Exception as e:
    #         logger.error(f"媒体信息获取异常: {e}")
    #         m = None
    #     self._fetch_done.emit(m, full)

    def _on_worker_done(self, m, full):
        if self._thread:
            self._thread.quit()
            self._thread.wait(2000)
            self._thread.deleteLater()
            self._thread = None
        if self._worker:
            self._worker.deleteLater()
            self._worker = None
        self._fetch_done.emit(m, full)

    def _on_fetched(self, m, full):
        self._fetching = False
        if not cfg.showMediaInfo.value:
            return
        if not m or not m.is_valid():
            self._no_media()
            if self._pending_full:
                QTimer.singleShot(100, lambda: self._fetch_in_thread(full=True))
            return
        is_new_song = m.title_artist != self._last_ta
        if is_new_song:
            logger.debug(f"媒体组件: 新歌曲 {m.title} - {m.artist}")
        self._media = m
        if full:
            self._display(m)
            self.show()
            if is_new_song and self._rapid_update_count == 0:
                self._rapid_update_count = 5
                self._timer.setInterval(500)
            elif self._rapid_update_count > 0:
                self._rapid_update_count -= 1
                if self._rapid_update_count == 0:
                    self._timer.setInterval(self._normal_interval)
            if self._pending_full:
                QTimer.singleShot(100, lambda: self._fetch_in_thread(full=True))
        else:
            if m.position_ms > self._position:
                self._position = m.position_ms
            self._playing = m.is_playing
            self._duration = m.duration_ms
            if getattr(m, 'thumbnail_data', None) and not self._has_gstmtc_cover:
                self._has_gstmtc_cover = True
                self._load_cover(m.thumbnail_data)
            if self._duration > 0:
                self._bar.setValue(min(100, int(self._position / self._duration * 100)))
                self._time.setText(self._fmt(self._position))
                self._dur.setText(self._fmt(self._duration))
            if self._playing and self._lyrics and not self._lyrics.is_empty():
                self._lyrics_w.update_position(self._position)

    def stop(self):
        self._timer.stop()
        self._prog_timer.stop()

    def _update(self):
        self._fetch_in_thread(full=True)

    def _no_media(self):
        self._title.setText(tr("media.not_playing"))  # 未在播放
        self._title.setWordWrap(False)
        self._artist.setText("")
        self._artist.show()
        self._lyrics_w.clear()
        self._lyrics_w.show()
        self._bar.setValue(0)
        self._time.setText("0:00")
        self._dur.setText("0:00")
        self._default_cover()
        self._media = None
        self._lyrics = None
        self._last_ta = ""
        self._has_gstmtc_cover = False
        self._playing = False
        self._duration = 0
        if cfg.showMediaInfo.value:
            self.show()

    def _display(self, m: MediaInfo):
        title = m.title or tr("media.unknown_song")  # 未知歌曲
        artist = m.artist or ""

        if m.title_artist != self._last_ta:
            self._last_ta = m.title_artist
            self._position = m.position_ms
            self._playing = m.is_playing
            self._duration = m.duration_ms
            self._has_gstmtc_cover = False
            
            app_name = getattr(m, 'app_name', '') or ''
            is_web_browser = any(browser in app_name.lower() for browser in ['chrome', 'edge', 'firefox', 'msedge'])
            
            self._cover_anim.stop()
            self._default_cover()
            self._cover = None
            self._lyrics = None
            self._lyrics_w.clear()
            self._cover_lbl.repaint()
            self._lyrics_w.repaint()

            if is_web_browser and not artist:
                self._title.setText(title)
                self._title.setWordWrap(True)
                self._artist.hide()
                self._lyrics_w.hide()
            else:
                self._title.setText(title)
                self._title.setWordWrap(False)
                self._artist.setText(artist)
                self._artist.show()
                self._lyrics_w.show()

            if getattr(m, 'thumbnail_data', None):
                self._has_gstmtc_cover = True
                self._load_cover(m.thumbnail_data)
            elif app_name == 'Kugou':
                self._fetch_kugou_thumbnail()
            elif is_web_browser:
                self._has_gstmtc_cover = True

            if not is_web_browser:
                self._fetch(title, artist)

        self._playing = m.is_playing
        if m.position_ms > self._position and m.position_ms > 0:
            self._position = m.position_ms
        if m.duration_ms > 0:
            self._duration = m.duration_ms

        self._prog_container.show()
        if self._duration > 0:
            self._bar.setValue(min(100, int(self._position / self._duration * 100)))
            self._time.setText(self._fmt(self._position))
            self._dur.setText(self._fmt(self._duration))
        else:
            self._bar.setValue(0)
            self._time.setText(self._fmt(self._position))

        self._cover_lbl.setVisible(cfg.showMediaCover.value)

    def _update_progress(self):
        if self._playing and self._duration > 0:
            self._position = min(self._position + self._prog_timer.interval(), self._duration)
            pct = min(100, int(self._position / self._duration * 100))
            self._bar.setValue(pct)
            self._time.setText(self._fmt(self._position))
        self._fetch_in_thread(full=False)

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
            cover_with_shadow = self._add_cover_shadow(pm, sz)
            self._cover_lbl.setPixmap(cover_with_shadow)

            self._cover_anim.stop()
            self._cover_opacity.setOpacity(0.0)
            self._cover_anim.setStartValue(0.0)
            self._cover_anim.setEndValue(1.0)
            self._cover_anim.start()

    def _fetch(self, title: str, artist: str):
        cache_key = f"{title} - {artist}"
        if cache_key in self._info_cache:
            info = self._info_cache.pop(cache_key)
            self._info_cache[cache_key] = info  # 插入到末尾（LRU）
            self._apply_fetched_info(info)
            return

        if getattr(self, '_detail_fetching', False):
            return
        self._detail_fetching = True

        self._detail_worker = FetchWorker(title, artist)
        self._detail_thread = QThread()
        self._detail_worker.moveToThread(self._detail_thread)
        self._detail_thread.started.connect(self._detail_worker.run)
        self._detail_worker.finished.connect(self._on_fetch_finished)
        self._detail_worker.finished.connect(self._detail_thread.quit)
        self._detail_thread.finished.connect(self._cleanup_detail_thread)
        self._detail_thread.start()
    
    def _on_fetch_finished(self, info: dict):
        cache_key = f"{self._media.title} - {self._media.artist}" if self._media else ""
        if cache_key:
            self._info_cache[cache_key] = info
            if len(self._info_cache) > 50:
                self._info_cache.popitem(last=False)
        self._apply_fetched_info(info)
    
    def _apply_fetched_info(self, info: dict):
        try:
            if info.get('detail'):
                self._duration = info['detail'].duration
            if info.get('cover') and cfg.showMediaCover.value and not self._has_gstmtc_cover:
                self._load_cover(info['cover'])
            self._lyrics = info.get('lyrics')
            self._lyrics_w.set_lyrics(self._lyrics)
        except Exception as e:
            logger.debug(f"应用歌曲信息失败: {e}")
        finally:
            self._detail_fetching = False

    def _cleanup_thread(self):
        if self._thread:
            self._thread.deleteLater()
            self._thread = None
        if self._worker:
            self._worker.deleteLater()
            self._worker = None

    def _cleanup_detail_thread(self):
        if hasattr(self, '_detail_thread') and self._detail_thread:
            self._detail_thread.deleteLater()
            self._detail_thread = None
        if hasattr(self, '_detail_worker') and self._detail_worker:
            self._detail_worker.deleteLater()
            self._detail_worker = None

    def _fetch_kugou_thumbnail(self):
        # import threading
        # def _do():
        #     try:
        #         from services.media import get_gstmtc
        #         gsmtc = get_gstmtc()
        #         if gsmtc and gsmtc.available:
        #             info = gsmtc.get_info()
        #             if info and getattr(info, 'thumbnail_data', None):
        #                 self._fetch_done.emit(info, False)
        #     except Exception:
        #         pass
        # t = threading.Thread(target=_do, daemon=True)
        # t.start()
        self._kugou_thread = QThread()
        self._kugou_worker = _KugouThumbWorker()
        self._kugou_worker.moveToThread(self._kugou_thread)
        self._kugou_thread.started.connect(self._kugou_worker.run)
        self._kugou_worker.finished.connect(self._on_kugou_done)
        self._kugou_thread.start()

    def _on_kugou_done(self, info):
        if self._kugou_thread:
            self._kugou_thread.quit()
            self._kugou_thread.wait(2000)
            self._kugou_thread.deleteLater()
            self._kugou_thread = None
        if self._kugou_worker:
            self._kugou_worker.deleteLater()
            self._kugou_worker = None
        if info and getattr(info, 'thumbnail_data', None):
            self._fetch_done.emit(info, False)

    def update_settings(self):
        self._apply_config()
        self.update()

    def clear_cache(self):
        self._info_cache.clear()
        close_media()



DEFAULT_ICON_DIR = 'default_icon'

def get_default_icon_path(icon_filename='exe.ico'):
    base_dir = BASE_DIR
    return os.path.join(base_dir, 'data', DEFAULT_ICON_DIR, icon_filename)

def get_ql_icon_path(icon_filename):
    if not icon_filename:
        return None
    base_dir = BASE_DIR
    icon_path = os.path.join(base_dir, 'data', 'ql_icon', icon_filename)
    if os.path.exists(icon_path):
        return icon_path
    sw_path = os.path.join(base_dir, 'data', 'software_icon', icon_filename)
    if os.path.exists(sw_path):
        return sw_path
    default_icon = get_default_icon_path(icon_filename)
    if os.path.exists(default_icon):
        return default_icon
    return None

def get_ql_icon_save_dir():
    base_dir = BASE_DIR
    icon_dir = os.path.join(base_dir, 'data', 'ql_icon')
    os.makedirs(icon_dir, exist_ok=True)
    return icon_dir

def get_folder_icon():
    return 'Directory.ico'

def get_url_icon():
    base_dir = BASE_DIR
    icon_path = os.path.join(base_dir, 'data', 'software_icon', 'url.ico')
    if os.path.exists(icon_path):
        return 'url.ico'
    return 'exe.ico'

def resolve_app_from_path(file_path):
    real_path = file_path
    app_type = "app"
    
    if file_path.lower().endswith('.lnk'):
        try:
            shortcut = pythoncom.CoCreateInstance(
                shell.CLSID_ShellLink, None, pythoncom.CLSCTX_INPROC_SERVER, shell.IID_IShellLink
            )
            persist = shortcut.QueryInterface(pythoncom.IID_IPersistFile)
            persist.Load(file_path)
            real_path = shortcut.GetPath(shell.SLGP_RAWPATH)[0]
            if not real_path:
                real_path = file_path
        except Exception:
            pass

    if file_path.lower().endswith('.lnk'):
        name = os.path.splitext(os.path.basename(file_path))[0]
    else:
        name = os.path.splitext(os.path.basename(real_path))[0]
    
    if os.path.isdir(real_path):
        app_type = "folder"
        icon_filename = get_folder_icon()
        return {"name": name, "path": real_path, "icon": icon_filename, "type": app_type}
    
    provider = QFileIconProvider()
    fi = QFileInfo(real_path if os.path.exists(real_path) else file_path)
    icon = provider.icon(fi)
    icon_filename = 'exe.ico'
    sizes = icon.availableSizes()
    if sizes:
        best_size = max(sizes, key=lambda s: s.width() * s.height())
        pixmap = icon.pixmap(best_size)
        if not pixmap.isNull():
            target_size = 256
            if pixmap.width() < target_size:
                pixmap = pixmap.scaled(target_size, target_size, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)
            cleaned_name = re.sub(r'[^\w\u4e00-\u9fff]', '', name)
            if cleaned_name:
                icon_filename = cleaned_name + '.ico'
            else:
                icon_filename = 'default.ico'
            icon_dir = get_ql_icon_save_dir()
            icon_save_path = os.path.join(icon_dir, icon_filename)
            pixmap.save(icon_save_path, 'PNG')

    return {"name": name, "path": real_path, "icon": icon_filename, "type": app_type}

def resolve_url_from_string(url_string, name=None):
    url = url_string.strip()
    if not url.startswith(('http://', 'https://')):
        url = 'https://' + url
    
    if not name:
        try:
            from urllib.parse import urlparse
            parsed = urlparse(url)
            name = parsed.netloc or url
        except Exception:
            name = url
    
    icon_filename = get_url_icon()
    return {"name": name, "path": url, "icon": icon_filename, "type": "url"}


class QuickLaunchDock(QWidget):
    MAX_SCALE = 1.45
    BASE_SCALE = 1.0
    MAGNIFY_RANGE = 100
    ANIM_SPEED = 0.22
    BOUNCE_H = 14
    BOUNCE_DUR = 800
    PAD_X = 20
    PAD_Y_BOTTOM = 6
    PAD_Y_TOP = 6
    RADIUS = 16
    FPS = 120
    MAX_APPS = 12
    
    _launch_result = pyqtSignal(str, str, bool)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("quickLaunchDock")
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setSizePolicy(QSizePolicy.Policy.Maximum, QSizePolicy.Policy.Maximum)
        self.setMouseTracking(True)
        self.setAcceptDrops(True)
        
        self._executor = ThreadPoolExecutor(max_workers=1)
        self._launch_result.connect(self._on_launch_result)
        self._icon_gap = cfg.quickLaunchIconSpacing.value
        self._show_labels = cfg.quickLaunchShowLabels.value

        self._apps = []
        self._pixmaps = []
        self._scales = []
        self._target_scales = []
        self._hover_idx = -1
        self._bounce_idx = -1
        self._bounce_y = 0.0
        self._bounce_active = False
        self._bounce_start_time = 0.0
        self._painting = False
        self._last_frame = 0.0
        
        self._dragging_idx = -1
        self._drag_start_pos = None
        self._is_internal_drag = False
        self._drop_target_idx = -1
        self._drag_pos = None

        self._timer = QTimer(self)
        self._timer.timeout.connect(self._tick)

    def _sz(self):
        return cfg.quickLaunchIconSize.value

    def set_apps(self, apps, animate_idx=-1):
        self._apps = list(apps)
        self._icon_gap = cfg.quickLaunchIconSpacing.value
        self._pixmaps = []
        for a in apps:
            fn = a.get("icon", "exe.ico")
            p = get_ql_icon_path(fn)
            pm = None
            if p and os.path.exists(p):
                raw = QPixmap(p)
                if not raw.isNull():
                    dpr = self.devicePixelRatioF() if hasattr(self, 'devicePixelRatioF') else self.devicePixelRatio()
                    raw.setDevicePixelRatio(dpr)
                    pm = raw
            self._pixmaps.append(pm)
        n = len(apps)
        self._scales = [self.BASE_SCALE] * n
        self._target_scales = [self.BASE_SCALE] * n
        self._fix_size()
        
        if animate_idx >= 0 and animate_idx < n:
            self._start_add_animation(animate_idx)
        
        self.update()
    
    def _start_add_animation(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        self._start_bounce(idx)

    def update_icon_size(self, size):
        self._icon_gap = cfg.quickLaunchIconSpacing.value
        self._show_labels = cfg.quickLaunchShowLabels.value
        self._pixmaps = []
        for a in self._apps:
            fn = a.get("icon", "exe.ico")
            p = get_ql_icon_path(fn)
            pm = None
            if p and os.path.exists(p):
                raw = QPixmap(p)
                if not raw.isNull():
                    dpr = self.devicePixelRatioF() if hasattr(self, 'devicePixelRatioF') else self.devicePixelRatio()
                    raw.setDevicePixelRatio(dpr)
                    pm = raw
            self._pixmaps.append(pm)
        self._fix_size()
        self.update()

    def _bg_rect(self):
        sz = self._sz()
        n = len(self._apps)
        if n == 0:
            return QRectF()
        w = n * sz + (n - 1) * self._icon_gap + self.PAD_X * 2
        h = sz + self.PAD_Y_TOP + self.PAD_Y_BOTTOM
        x = (self.width() - w) / 2
        y = self.height() - h
        return QRectF(x, y, w, h)

    def _fix_size(self):
        sz = self._sz()
        n = len(self._apps)
        if n == 0:
            self.setFixedSize(0, 0)
            return
        w_icons = n * sz + (n - 1) * self._icon_gap + self.PAD_X * 2
        h_icons = sz + self.PAD_Y_TOP + self.PAD_Y_BOTTOM
        scale_overflow = int(sz * (self.MAX_SCALE - self.BASE_SCALE))
        bounce_overflow = self.BOUNCE_H + 10
        side_overflow = int(sz * (self.MAX_SCALE - self.BASE_SCALE) * 0.3)
        label_overflow = 28 if self._show_labels else 0
        drag_extra = int(sz * 0.5)
        w = w_icons + side_overflow * 2 + drag_extra
        h = h_icons + scale_overflow + bounce_overflow + label_overflow + drag_extra
        self.setFixedSize(w, h)

    def _icon_positions(self):
        sz = self._sz()
        n = len(self._scales)
        if n == 0:
            return []

        widths = [sz * sc for sc in self._scales]
        total = sum(widths) + (n - 1) * self._icon_gap
        bg = self._bg_rect()
        content_w = bg.width() - self.PAD_X * 2
        start_x = bg.x() + self.PAD_X + (content_w - total) / 2

        pos = []
        cx = start_x
        for i in range(n):
            pos.append(cx + widths[i] / 2)
            cx += widths[i] + self._icon_gap
        return pos

    def _icon_rect(self, i, positions=None):
        if positions is None:
            positions = self._icon_positions()
        s = self._sz() * self._scales[i]
        cx = positions[i]
        bg = self._bg_rect()
        by = bg.y() + bg.height() - self.PAD_Y_BOTTOM
        return QRectF(cx - s / 2, by - s, s, s)

    def _smoothstep(self, t):
        t = max(0.0, min(1.0, t))
        return t * t * (3.0 - 2.0 * t)

    def _get_icon_at_pos(self, pos):
        if not self._apps:
            return -1
        pos_list = self._icon_positions()
        for i in range(len(self._apps)):
            r = self._icon_rect(i, pos_list)
            if r.contains(pos):
                return i
        return -1

    def mouseMoveEvent(self, e):
        if self._dragging_idx >= 0 and self._drag_start_pos and not self._is_internal_drag:
            dist = (e.position() - self._drag_start_pos).manhattanLength()
            if dist > 10:
                self._start_internal_drag(self._dragging_idx)
        
        if self._is_internal_drag and self._dragging_idx >= 0:
            rect = self.rect()
            sz = self._sz() * self.MAX_SCALE
            pos = e.position()
            x = max(rect.x() + sz / 2, min(pos.x(), rect.x() + rect.width() - sz / 2))
            y = max(rect.y() + sz / 2, min(pos.y(), rect.y() + rect.height() - sz / 2))
            self._drag_pos = QPointF(x, y)
            self._update_drop_target(e.position())
            self.update()
        
        self._calc_targets(e.position())
        super().mouseMoveEvent(e)

    def mousePressEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton:
            pl = self._icon_positions()
            for i in range(len(self._apps)):
                if self._icon_rect(i, pl).contains(e.position()):
                    self._drag_start_pos = e.position()
                    self._dragging_idx = i
                    break
        elif e.button() == Qt.MouseButton.RightButton:
            pl = self._icon_positions()
            for i in range(len(self._apps)):
                if self._icon_rect(i, pl).contains(e.position()):
                    self._show_context_menu(i, e.position())
                    break
        super().mousePressEvent(e)

    def mouseReleaseEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton:
            if self._is_internal_drag and self._dragging_idx >= 0:
                self._finish_drag_reorder()
            elif self._dragging_idx >= 0 and self._drag_start_pos:
                pl = self._icon_positions()
                if self._icon_rect(self._dragging_idx, pl).contains(e.position()):
                    self._click(self._dragging_idx)
            
            self._dragging_idx = -1
            self._drag_start_pos = None
            self._is_internal_drag = False
            self._drop_target_idx = -1
            self._drag_pos = None
            self.update()
        
        super().mouseReleaseEvent(e)

    def _start_internal_drag(self, idx):
        self._is_internal_drag = True
        self._dragging_idx = idx
        self._drag_pos = self._drag_start_pos
        self._drop_target_idx = idx
        self.update()

    def _update_drop_target(self, pos):
        if not self._apps:
            return
        
        sz = self._sz()
        bg = self._bg_rect()
        content_w = bg.width() - self.PAD_X * 2
        n = len(self._apps)
        total_w = n * sz + (n - 1) * self._icon_gap
        start_x = bg.x() + self.PAD_X + (content_w - total_w) / 2
        
        new_target = -1
        for i in range(n):
            icon_x = start_x + i * (sz + self._icon_gap)
            if pos.x() < icon_x + sz / 2:
                new_target = i
                break
        
        if new_target == -1:
            new_target = n
        
        if new_target != self._drop_target_idx:
            self._drop_target_idx = new_target

    def _finish_drag_reorder(self):
        if self._dragging_idx < 0 or self._drop_target_idx < 0:
            return
        
        if self._dragging_idx == self._drop_target_idx:
            return
        
        apps = list(self._apps)
        dragged_app = apps.pop(self._dragging_idx)
        
        insert_idx = self._drop_target_idx
        if insert_idx > self._dragging_idx:
            insert_idx -= 1
        
        apps.insert(insert_idx, dragged_app)
        
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=insert_idx)
        
        logger.info(f"快捷启动栏顺序已调整: {self._dragging_idx} -> {insert_idx}")

    def _show_context_menu(self, idx, pos):
        if idx < 0 or idx >= len(self._apps):
            return
        
        app = self._apps[idx]
        menu = RoundMenu(app.get("name", tr("quick_launch.app")), self)  # 应用

        open_action = Action(FUI.PLAY, tr("quick_launch.open"), self)  # 打开
        open_action.triggered.connect(lambda: self._click(idx))
        menu.addAction(open_action)

        menu.addSeparator()

        edit_action = Action(FUI.EDIT, tr("quick_launch.edit"), self)  # 编辑
        edit_action.triggered.connect(lambda: self._edit_app(idx))
        menu.addAction(edit_action)

        delete_action = Action(FUI.DELETE, tr("quick_launch.delete"), self)  # 删除
        delete_action.triggered.connect(lambda: self._delete_app(idx))
        menu.addAction(delete_action)
        
        menu.addSeparator()
        
        app_type = app.get("type", "app")
        if app_type == "url":
            path_info = f"{tr('quick_launch.url')}: {app.get('path', '')}"
        else:
            path_info = f"{tr('quick_launch.path')}: {app.get('path', '')}"
        
        info_action = Action(FUI.INFO, path_info, self)
        info_action.setEnabled(False)
        menu.addAction(info_action)
        
        menu.exec(QCursor.pos())

    def _edit_app(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        
        from ui.home import AppEditDialog
        
        dialog = AppEditDialog(self.window(), self._apps[idx])
        if dialog.exec():
            result = dialog.get_app_data()
            if result:
                self._apps[idx] = result
                cfg.quickLaunchApps.value = self._apps
                save_cfg()
                self.set_apps(self._apps)
                InfoBar.success(tr("quick_launch.save_success"), tr("quick_launch.shortcut_updated"), parent=self.window(), duration=2000)  # 保存成功 / 快捷方式已更新

    def _delete_app(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        
        app_name = self._apps[idx].get("name", tr("quick_launch.this_app"))  # 此应用
        
        mw = self.window()
        mask = QWidget()
        mask.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)
        mask.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        mask.setGeometry(0, 0, mw.width(), mw.height())
        mask.setStyleSheet("background-color: rgba(0, 0, 0, 120);")
        mask.show()
        
        from qfluentwidgets import MessageBox
        box = MessageBox(tr("quick_launch.confirm_delete"), tr("quick_launch.confirm_delete_msg", name=app_name), mask)  # 确认删除 / 确定要删除"{name}"吗？
        box.yesButton.setText(tr("quick_launch.delete"))  # 删除
        box.cancelButton.setText(tr("common.cancel"))  # 取消
        
        if box.exec():
            self._apps.pop(idx)
            cfg.quickLaunchApps.value = self._apps
            save_cfg()
            self.set_apps(self._apps)
            InfoBar.success(tr("quick_launch.delete_success"), tr("quick_launch.deleted", name=app_name), parent=self.window(), duration=2000)  # 删除成功 / 已删除"{name}"
        
        mask.close()
        mask.deleteLater()

    def leaveEvent(self, e):
        n = len(self._target_scales)
        self._target_scales = [self.BASE_SCALE] * n
        self._hover_idx = -1
        super().leaveEvent(e)

    def _calc_targets(self, pos):
        if not self._apps:
            return
        mx = pos.x()
        my = pos.y()
        pos_list = self._icon_positions()
        new_hover = -1

        for i in range(len(self._apps)):
            r = self._icon_rect(i, pos_list)
            if r.contains(pos):
                new_hover = i
                break

        if new_hover < 0:
            bg = self._bg_rect()
            if bg.contains(pos):
                min_dist = float('inf')
                for i in range(len(self._apps)):
                    cx = pos_list[i]
                    d = abs(mx - cx)
                    if d < min_dist:
                        min_dist = d
                        new_hover = i

        for i in range(len(self._apps)):
            if new_hover >= 0 and abs(i - new_hover) <= 2:
                cx = pos_list[i]
                d = abs(mx - cx)
                if d < self.MAGNIFY_RANGE:
                    t = self._smoothstep(1.0 - d / self.MAGNIFY_RANGE)
                    sc = self.BASE_SCALE + (self.MAX_SCALE - self.BASE_SCALE) * t
                else:
                    sc = self.BASE_SCALE
            else:
                sc = self.BASE_SCALE
            self._target_scales[i] = sc

        if new_hover != self._hover_idx:
            self._hover_idx = new_hover

        self._ensure_timer()

    def _ensure_timer(self):
        """确保动画定时器在运行"""
        if not self._timer.isActive():
            self._last_frame = 0.0
            self._timer.start(int(1000 / self.FPS))

    def _tick(self):
        now = time.time()
        dt = min(now - self._last_frame, 0.05) if self._last_frame > 0 else 0.016
        self._last_frame = now
        changed = False

        for i in range(len(self._scales)):
            if i >= len(self._target_scales):
                break
            cur = self._scales[i]
            tgt = self._target_scales[i]
            diff = tgt - cur
            if abs(diff) > 0.001:
                sp = self.ANIM_SPEED * (60.0 * dt)
                if abs(diff) < 0.008:
                    self._scales[i] = tgt
                else:
                    self._scales[i] += diff * min(sp, 1.0)
                changed = True

        if self._bounce_active:
            elapsed = (now - self._bounce_start_time) * 1000.0
            dur = float(self.BOUNCE_DUR)
            bh = float(self.BOUNCE_H)
            if elapsed >= dur:
                self._bounce_y = 0.0
                self._bounce_active = False
                self._bounce_idx = -1
            else:
                t = elapsed / dur
                kfs = [
                    (0.00, 0.0), (0.14, -bh), (0.28, 0.0),
                    (0.44, -bh * 0.50), (0.58, 0.0),
                    (0.72, -bh * 0.22), (0.86, 0.0), (1.00, 0.0),
                ]
                lo_t, lo_v = kfs[0], kfs[1]
                for j in range(len(kfs) - 1):
                    if kfs[j][0] <= t <= kfs[j + 1][0]:
                        lo_t, lo_v = kfs[j], kfs[j + 1]
                        break
                span = lo_v[0] - lo_t[0]
                if span > 0:
                    lt = (t - lo_t[0]) / span
                    lt = lt * lt * (3.0 - 2.0 * lt)
                    self._bounce_y = lo_t[1] + (lo_v[1] - lo_t[1]) * lt
                else:
                    self._bounce_y = lo_v[1]
                changed = True

        if changed:
            self.update()
        else:
            self._timer.stop()

    def dragEnterEvent(self, e):
        if e.mimeData().hasUrls():
            urls = e.mimeData().urls()
            for url in urls:
                path = url.toLocalFile()
                if path:
                    if path.lower().endswith(('.exe', '.lnk')) or os.path.isdir(path):
                        e.acceptProposedAction()
                        return
        elif e.mimeData().hasText():
            text = e.mimeData().text().strip()
            if text.startswith(('http://', 'https://', 'www.')):
                e.acceptProposedAction()
                return
        e.ignore()

    def dragMoveEvent(self, e):
        if e.mimeData().hasUrls() or e.mimeData().hasText():
            e.acceptProposedAction()
        else:
            e.ignore()

    def dropEvent(self, e):
        e.acceptProposedAction()
        
        if e.mimeData().hasUrls():
            urls = e.mimeData().urls()
            for url in urls:
                path = url.toLocalFile()
                if not path:
                    continue
                if os.path.isdir(path):
                    self._add_folder_from_path(path)
                elif path.lower().endswith(('.exe', '.lnk')):
                    self._add_app_from_path(path)
        
        elif e.mimeData().hasText():
            text = e.mimeData().text().strip()
            if text.startswith(('http://', 'https://', 'www.')):
                self._add_url(text)

    def _add_app_from_path(self, file_path):
        new_app = resolve_app_from_path(file_path)
        apps = list(cfg.quickLaunchApps.value)
        if len(apps) >= self.MAX_APPS:
            InfoBar.warning(
                title=tr("quick_launch.limit_reached"),  # 已达上限
                content=tr("quick_launch.max_apps", max=self.MAX_APPS),  # 最多添加 {max} 个应用
                parent=self.window(),
                duration=3000
            )
            return
        apps.append(new_app)
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=len(apps) - 1)
        InfoBar.success(tr("quick_launch.add_success"), tr("quick_launch.added", name=new_app['name']), parent=self.window(), duration=2000)  # 添加成功 / 已添加"{name}"

    def _add_folder_from_path(self, folder_path):
        name = os.path.basename(folder_path)
        new_item = {
            "name": name,
            "path": folder_path,
            "icon": get_folder_icon(),
            "type": "folder"
        }
        apps = list(cfg.quickLaunchApps.value)
        if len(apps) >= self.MAX_APPS:
            InfoBar.warning(
                title=tr("quick_launch.limit_reached"),  # 已达上限
                content=tr("quick_launch.max_items", max=self.MAX_APPS),  # 最多添加 {max} 个项目
                parent=self.window(),
                duration=3000
            )
            return
        apps.append(new_item)
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=len(apps) - 1)
        InfoBar.success(tr("quick_launch.add_success"), tr("quick_launch.added_folder", name=name), parent=self.window(), duration=2000)  # 添加成功 / 已添加文件夹"{name}"

    def _add_url(self, url):
        new_item = resolve_url_from_string(url)
        apps = list(cfg.quickLaunchApps.value)
        if len(apps) >= self.MAX_APPS:
            InfoBar.warning(
                title=tr("quick_launch.limit_reached"),  # 已达上限
                content=tr("quick_launch.max_items", max=self.MAX_APPS),  # 最多添加 {max} 个项目
                parent=self.window(),
                duration=3000
            )
            return
        apps.append(new_item)
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=len(apps) - 1)
        InfoBar.success(tr("quick_launch.add_success"), tr("quick_launch.added_url", name=new_item['name']), parent=self.window(), duration=2000)  # 添加成功 / 已添加网址"{name}"

    def _click(self, idx):
        a = self._apps[idx]
        path = a.get("path", "")
        name = a.get("name", "")
        app_type = a.get("type", "app")
        self._start_bounce(idx)
        if path:
            self._executor.submit(self._launch_thread, path, name, app_type)

    def _launch_thread(self, target, name, app_type):
        try:
            if not target:
                self._launch_result.emit(name, tr("quick_launch.no_path"), False)  # 路径不存在
                return
            
            if app_type == "url":
                webbrowser.open(target)
                self._launch_result.emit(name, target, True)
            elif app_type == "folder":
                if os.path.exists(target):
                    os.startfile(target)
                    self._launch_result.emit(name, target, True)
                else:
                    self._launch_result.emit(name, tr("quick_launch.folder_not_exist", path=target), False)
            else:
                if os.path.exists(target):
                    os.startfile(target)
                    self._launch_result.emit(name, target, True)
                else:
                    self._launch_result.emit(name, tr("quick_launch.path_not_exist", path=target), False)
        except Exception as e:
            self._launch_result.emit(name, str(e), False)

    def _on_launch_result(self, app_name, info, success):
        if success:
            logger.info(f"已启动：{app_name} ({info})")
            InfoBar.success(
                title=tr("quick_launch.launch_success"),  # 启动成功
                content=tr("quick_launch.opening", name=app_name),  # 正在打开 {name}
                parent=self.window(),
                duration=2000
            )
        else:
            logger.warning(f"启动失败：{app_name}, {info}")
            InfoBar.error(
                title=tr("quick_launch.launch_failed"),  # 启动失败
                content=f"{app_name}: {info}",
                parent=self.window(),
                duration=3000
            )

    def _start_bounce(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return
        self._bounce_idx = idx
        self._bounce_y = 0.0
        self._bounce_active = True
        self._bounce_start_time = time.time()
        self._ensure_timer()

    def _get_by(self):
        return self._bounce_y

    def _set_by(self, v):
        self._bounce_y = v
        self.update()

    bounceY = pyqtProperty(float, _get_by, _set_by)

    def paintEvent(self, event):
        if self._painting:
            return
        self._painting = True
        try:
            self._render()
        finally:
            self._painting = False

    def _render(self):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)

        bg = self._bg_rect()
        if bg.isEmpty():
            p.end()
            return

        dark = isDarkTheme()

        path = QPainterPath()
        path.addRoundedRect(bg, self.RADIUS, self.RADIUS)

        if dark:
            bg_c = QColor(30, 30, 32, 165)
            brd_c = QColor(255, 255, 255, 20)
            sh_top = QColor(255, 255, 255, 22)
            sh_mid = QColor(255, 255, 255, 6)
            inner_glow = QColor(255, 255, 255, 8)
        else:
            bg_c = QColor(235, 235, 240, 172)
            brd_c = QColor(0, 0, 0, 12)
            sh_top = QColor(255, 255, 255, 95)
            sh_mid = QColor(255, 255, 255, 18)
            inner_glow = QColor(255, 255, 255, 25)

        p.setPen(Qt.PenStyle.NoPen)

        shadow_path = QPainterPath()
        sr = QRectF(bg.x() + 1.5, bg.y() + 2, bg.width() - 3, bg.height() * 0.5)
        shadow_path.addRoundedRect(sr, self.RADIUS - 3, self.RADIUS - 3)
        p.setBrush(QBrush(inner_glow))
        p.drawPath(shadow_path)

        p.setBrush(bg_c)
        p.drawPath(path)

        grad = QLinearGradient(bg.x(), bg.y(), bg.x(), bg.y() + bg.height())
        grad.setColorAt(0.0, sh_top)
        grad.setColorAt(0.30, sh_mid)
        grad.setColorAt(1.0, QColor(0, 0, 0, 0))

        p.setCompositionMode(QPainter.CompositionMode.CompositionMode_Lighten)
        p.fillPath(path, grad)
        p.setCompositionMode(QPainter.CompositionMode.CompositionMode_SourceOver)

        pen = QPen(brd_c)
        pen.setWidth(1)
        p.setPen(pen)
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawPath(path)

        pl = self._icon_positions()
        baseline_y = bg.y() + bg.height() - self.PAD_Y_BOTTOM
        sz = self._sz()

        for i in range(len(self._apps)):
            if self._is_internal_drag and i == self._dragging_idx:
                continue
            
            pm = self._pixmaps[i]
            sc = self._scales[i]
            s = sz * sc
            
            if self._is_internal_drag and self._dragging_idx >= 0:
                if i >= self._drop_target_idx and i < self._dragging_idx:
                    cx = pl[i + 1]
                elif i < self._drop_target_idx and i > self._dragging_idx:
                    cx = pl[i - 1]
                else:
                    cx = pl[i]
            else:
                cx = pl[i]
            
            top = baseline_y - s
            if i == self._bounce_idx:
                top += self._bounce_y
            
            if pm and not pm.isNull():
                p.drawPixmap(
                    QRectF(cx - s / 2, top, s, s),
                    pm,
                    QRectF(0, 0, pm.width(), pm.height()),
                )
            else:
                p.setBrush(QColor(120, 120, 120, 60))
                p.setPen(QPen(QColor(120, 120, 120, 100), 1))
                r = QRectF(cx - s / 2, top, s, s)
                p.drawRoundedRect(r, 8, 8)
                p.setPen(QPen(QColor(180, 180, 180, 150), 2))
                font = p.font()
                font.setPixelSize(int(s * 0.4))
                p.setFont(font)
                p.drawText(r, Qt.AlignmentFlag.AlignCenter, "?")
            
            if i == self._hover_idx and self._show_labels and not self._is_internal_drag:
                name = self._apps[i].get("name", "")
                if name:
                    label_font = p.font()
                    label_font.setFamily("HarmonyOS Sans")
                    label_font.setPixelSize(14)
                    label_font.setWeight(QFont.Weight.Medium)
                    p.setFont(label_font)
                    fm = QFontMetrics(label_font)
                    
                    display_name = name
                    if len(name) > 50:
                        display_name = name[:50] + "..."
                    
                    padding_x = 10
                    label_w = fm.horizontalAdvance(display_name) + padding_x * 2
                    label_h = 24
                    label_x = cx - label_w / 2
                    label_y = top - label_h - 4
                    
                    widget_rect = self.rect()
                    if label_x < widget_rect.left() + 2:
                        label_x = widget_rect.left() + 2
                    if label_x + label_w > widget_rect.right() - 2:
                        label_x = widget_rect.right() - label_w - 2
                    if label_y < widget_rect.top() + 2:
                        label_y = top + sz + 4
                    
                    label_path = QPainterPath()
                    label_path.addRoundedRect(label_x, label_y, label_w, label_h, label_h / 2, label_h / 2)
                    p.setPen(Qt.PenStyle.NoPen)
                    p.setBrush(QColor(0, 0, 0, 220))
                    p.drawPath(label_path)
                    p.setPen(QColor(255, 255, 255, 255))
                    p.setFont(label_font)
                    text_rect = QRectF(label_x, label_y, label_w, label_h)
                    p.drawText(text_rect, Qt.AlignmentFlag.AlignCenter | Qt.TextFlag.TextSingleLine, display_name)

        if self._is_internal_drag and self._dragging_idx >= 0 and self._drag_pos:
            pm = self._pixmaps[self._dragging_idx] if self._dragging_idx < len(self._pixmaps) else None
            s = sz * self.MAX_SCALE
            cx = self._drag_pos.x()
            top = self._drag_pos.y() - s / 2
            
            if pm and not pm.isNull():
                p.drawPixmap(
                    QRectF(cx - s / 2, top, s, s),
                    pm,
                    QRectF(0, 0, pm.width(), pm.height()),
                )
            else:
                p.setBrush(QColor(120, 120, 120, 100))
                p.setPen(QPen(QColor(120, 120, 120, 150), 1))
                r = QRectF(cx - s / 2, top, s, s)
                p.drawRoundedRect(r, 8, 8)

        p.end()

    def minimumSizeHint(self):
        if not self._apps:
            return QSize(40, 40)
        bg = self._bg_rect()
        return QSize(int(bg.width()), int(bg.height()))

    def hideEvent(self, e):
        self._timer.stop()
        super().hideEvent(e)

    def showEvent(self, e):
        super().showEvent(e)


# 组件样式类
import datetime as py_datetime
FONT_FAMILY = '"HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif'


def render_svg_icon(icon_path: str, size: int, dpr: float = 1.0) -> QPixmap:
    """SVG图标"""
    renderer = QSvgRenderer(icon_path)
    if not renderer.isValid():
        return QPixmap()
    actual_size = int(size * dpr)
    pm = QPixmap(actual_size, actual_size)
    pm.fill(Qt.GlobalColor.transparent)
    p = QPainter(pm)
    p.setRenderHint(QPainter.RenderHint.Antialiasing)
    p.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)
    renderer.render(p)
    p.end()
    pm.setDevicePixelRatio(dpr)
    return pm


class DigitalClockComponent(DraggableContainer):
    """数字时钟组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("clockContainer")
        self._home = parent  # HomeInterface reference
        self._setup_ui()
        self._setup_timer()
        self._update_time()

    def _setup_ui(self):
        self.clockLabel = QLabel("00:00:00")
        self.clockLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.dateLabel = QLabel("")
        self.dateLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(32, 24, 32, 24)
        layout.setSpacing(8)
        layout.addWidget(self.clockLabel)
        layout.addWidget(self.dateLabel)

        self.setMinimumSize(400, 200)
        self._size_explicitly_set = True
        self.resize(400, 200)
        self._apply_style()

    def _setup_timer(self):
        """定时器和信号连接"""
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._update_time)
        self.timer.start(1000)

        cfg.showClock.valueChanged.connect(self._update_time)
        cfg.showClockSeconds.valueChanged.connect(self._update_time)
        cfg.showLunarCalendar.valueChanged.connect(self._update_time)
        cfg.clockColor.valueChanged.connect(self._apply_style)
        cfg.clockSize.valueChanged.connect(self._apply_style)
        cfg.dateSize.valueChanged.connect(self._apply_style)

    def _update_time(self):
        """更新时钟显示"""
        if not cfg.showClock.value:
            self.clockLabel.hide()
            self.dateLabel.hide()
            if hasattr(self._home, 'isEditMode') and self._home.isEditMode:
                self.setContentVisible(False)
                self.show()
            else:
                self.hide()
            return

        self.setContentVisible(True)
        self.clockLabel.show()
        self.dateLabel.show()

        currentTime = QTime.currentTime()
        currentDate = QDate.currentDate()

        # 精确时间
        if cfg.usePreciseTime.value:
            try:
                from core.utils import precise_now
                pn = precise_now()
                currentTime = QTime(pn.hour, pn.minute, pn.second)
                currentDate = QDate(pn.year, pn.month, pn.day)
            except Exception:
                pass

        # 时间格式
        if cfg.showClockSeconds.value:
            timeString = currentTime.toString("HH:mm:ss")
        else:
            timeString = currentTime.toString("HH:mm")
        self.clockLabel.setText(timeString)

        # 日期格式
        _weekday_keys = ["weekday.monday", "weekday.tuesday", "weekday.wednesday",
                         "weekday.thursday", "weekday.friday", "weekday.saturday", "weekday.sunday"]
        weekday_name = tr(_weekday_keys[currentDate.dayOfWeek() - 1])
        solarString = tr("date.format", y=currentDate.year(), M=currentDate.month(),
                         d=currentDate.day(), w=weekday_name)

        # 农历
        if cfg.showLunarCalendar.value:
            try:
                import cnlunar
                py_datetime_obj = py_datetime.datetime(currentDate.year(), currentDate.month(), currentDate.day(), 0, 0, 0)
                lunar = cnlunar.Lunar(py_datetime_obj)
                lunarMonthCn = lunar.lunarMonthCn.replace("大", "").replace("小", "")
                lunarDayCn = lunar.lunarDayCn
                lunarString = f"{lunarMonthCn}{lunarDayCn}"
                dateString = f"{solarString} {lunarString}"
            except Exception as e:
                logger.error(f"农历计算错误: {e}")
                dateString = solarString
        else:
            dateString = solarString

        self.dateLabel.setText(dateString)
        self.updateSize()

    def _apply_style(self):
        """应用样式"""
        clock_color = cfg.clockColor.value
        color_str = clock_color.name() if hasattr(clock_color, 'name') else str(clock_color)
        clock_size = cfg.clockSize.value
        date_size = cfg.dateSize.value

        self.clockLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {clock_size}px;
            font-weight: bold;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)

        self.dateLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {date_size}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)
        self.updateSize()


class WeatherIconTempComponent(DraggableContainer):
    """天气组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="horizontal")
        self.setObjectName("weatherContainer")
        self._home = parent
        self._cached_weather = None
        self._setup_ui()
        self._setup_timer()

    def _setup_ui(self):
        self.tempLabel = QLabel("")
        self.tempLabel.setObjectName("weatherTempLabel")
        self.tempLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.iconLabel = QLabel("")
        self.iconLabel.setObjectName("weatherIconLabel")
        self.iconLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.setSpacing(12)
        layout.addWidget(self.iconLabel)
        layout.addWidget(self.tempLabel)

        self.setMinimumSize(200, 200)
        self._size_explicitly_set = True
        self.resize(200, 200)
        self._apply_style()

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._refresh_weather)
        self.timer.start(1000) 

        cfg.showWeather.valueChanged.connect(self._refresh_weather)
        cfg.weatherUpdateInterval.valueChanged.connect(self._update_interval)
        cfg.clockColor.valueChanged.connect(self._apply_style)
        cfg.weatherSize.valueChanged.connect(self._apply_style)
        cfg.weatherIconSize.valueChanged.connect(self._update_icon_size)

        if hasattr(self._home, 'weather_updated'):
            self._home.weather_updated.connect(self._refresh_weather)

        self._update_interval()
        self._refresh_weather()

    def _update_interval(self):
        interval_map = {"10s": 10000, "30s": 30000, "1m": 60000, "5m": 300000, "10m": 600000, "30m": 1800000}
        interval_str = cfg.weatherUpdateInterval.value
        self.timer.setInterval(interval_map.get(interval_str, 300000))

    def _refresh_weather(self):
        if not cfg.showWeather.value:
            self.hide()
            return
        self.show()

        cached = get_cached_content("weather", ignore_expiry=True)  # 过期也显示旧数据
        if cached:
            self._update_display(cached)
        else:
            self.tempLabel.setText("--°")
            self.iconLabel.clear()

    def _update_display(self, data):
        if not data:
            return

        from services.weather import WeatherService

        current = data.get("current", {})
        temp_obj = current.get("temperature", {})
        raw = temp_obj.get("value", "--")
        try:
            temp = int(round(float(raw)))
        except (ValueError, TypeError):
            temp = raw
        self.tempLabel.setText(f"{temp}°")

        weather_code = current.get("weather", 0)
        try:
            weather_code = int(weather_code)
        except (ValueError, TypeError):
            weather_code = 0
        icon_name = WeatherService.ICON_MAP.get(weather_code, "2.svg")
        icon_path = WeatherService.get_weather_icon_path(icon_name)

        weather_text = WeatherService.WEATHER_MAP.get(weather_code, ("未知", "2.svg"))[0]
        logger.info(f"[WeatherComponent] 当前温度:{temp}° 天气代码:{weather_code} 天气:{weather_text} 图标:{icon_name}")

        if icon_path and os.path.exists(icon_path):
            icon_size = cfg.weatherIconSize.value
            dpr = self.devicePixelRatioF() if hasattr(self, 'devicePixelRatioF') else self.devicePixelRatio()
            pm = render_svg_icon(icon_path, icon_size, dpr)
            if not pm.isNull():
                self.iconLabel.setPixmap(pm)

    def _update_icon_size(self):
        cached = get_cached_content("weather")
        if cached:
            self._update_display(cached)

    def _apply_style(self):
        color = cfg.weatherTextColor.value
        color_str = color.name() if hasattr(color, 'name') else str(color)
        size = cfg.weatherSize.value

        self.tempLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {size}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)
        self.updateSize()


class WeatherHourlyComponent(DraggableContainer):
    """逐小时天气卡片"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("weatherHourlyContainer")
        self._home = parent
        self._hourly_data = None
        self._setup_ui()
        self._setup_timer()

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        layout.setContentsMargins(28, 18, 28, 18)
        layout.setSpacing(6)

        # 上侧：城市 温度 图标 预警
        self._top_row = QWidget()
        self._top_row.setStyleSheet("background-color: transparent;")
        top_layout = QHBoxLayout(self._top_row)
        top_layout.setContentsMargins(0, 0, 0, 0)
        top_layout.setSpacing(0)

        # 左侧：城市 温度
        left_col = QWidget()
        left_col.setStyleSheet("background-color: transparent;")
        left_layout = QVBoxLayout(left_col)
        left_layout.setContentsMargins(0, 0, 0, 0)
        left_layout.setSpacing(2)

        # 能点击城市标签
        self.cityLabel = QLabel("--")
        self.cityLabel.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.cityLabel.setCursor(Qt.CursorShape.PointingHandCursor)
        self.cityLabel.setToolTip(tr("weather_service.select_region"))

        self.currentTempLabel = QLabel("--°")
        self.currentTempLabel.setAlignment(Qt.AlignmentFlag.AlignLeft)

        left_layout.addWidget(self.cityLabel)
        left_layout.addWidget(self.currentTempLabel)
        left_layout.addStretch()

        # 右侧：图标 预警
        right_col = QWidget()
        right_col.setStyleSheet("background-color: transparent;")
        right_layout = QVBoxLayout(right_col)
        right_layout.setContentsMargins(0, 0, 0, 0)
        right_layout.setSpacing(2)

        self.currentIconLabel = QLabel()
        self.currentIconLabel.setAlignment(Qt.AlignmentFlag.AlignRight)
        self.currentIconLabel.setFixedSize(60, 60)

        self.alertLabel = QLabel("")
        self.alertLabel.setAlignment(Qt.AlignmentFlag.AlignRight)

        right_layout.addWidget(self.currentIconLabel)
        right_layout.addWidget(self.alertLabel)
        right_layout.addStretch()

        top_layout.addWidget(left_col)
        top_layout.addStretch()
        top_layout.addWidget(right_col)

        # 下侧：6小时逐时预报
        self._bottom_row = QWidget()
        self._bottom_row.setStyleSheet("background-color: transparent;")
        bottom_layout = QHBoxLayout(self._bottom_row)
        bottom_layout.setContentsMargins(0, 0, 0, 0)
        bottom_layout.setSpacing(0)

        self._hourly_widgets = []
        for i in range(6):
            col = QWidget()
            col.setStyleSheet("background-color: transparent;")
            col_layout = QVBoxLayout(col)
            col_layout.setContentsMargins(0, 0, 0, 0)
            col_layout.setSpacing(3)
            col_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

            time_label = QLabel("--:00")
            time_label.setAlignment(Qt.AlignmentFlag.AlignCenter)

            icon_label = QLabel()
            icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            icon_label.setFixedSize(28, 28)

            temp_label = QLabel("--°")
            temp_label.setAlignment(Qt.AlignmentFlag.AlignCenter)

            col_layout.addWidget(time_label)
            col_layout.addWidget(icon_label)
            col_layout.addWidget(temp_label)

            bottom_layout.addWidget(col, 1)
            self._hourly_widgets.append((time_label, icon_label, temp_label))

        layout.addWidget(self._top_row)
        layout.addWidget(self._bottom_row)

        self.setMinimumSize(400, 200)
        self._size_explicitly_set = True
        self.resize(400, 200)
        self._top_row.setMinimumHeight(100)
        self._apply_style()

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._refresh_weather)
        self.timer.start(1000)

        cfg.showWeather.valueChanged.connect(self._refresh_weather)
        cfg.weatherUpdateInterval.valueChanged.connect(self._update_interval)
        cfg.weatherTextColor.valueChanged.connect(self._apply_style)

        if hasattr(self._home, 'weather_updated'):
            self._home.weather_updated.connect(self._on_weather_updated)

        self._update_interval()
        self._refresh_weather()

    def _update_interval(self):
        interval_map = {
            "10s": 10000, "30s": 30000, "1m": 60000,
            "5m": 300000, "10m": 600000, "30m": 1800000,
        }
        interval_str = cfg.weatherUpdateInterval.value
        self.timer.setInterval(interval_map.get(interval_str, 300000))

    def _refresh_weather(self):
        if not cfg.showWeather.value:
            self.hide()
            return
        self.show()
        self._update_from_cache()

    def _on_weather_updated(self):
        """天气数据更新回调"""
        self._update_from_cache()

    def _update_from_cache(self):
        from services.weather import WeatherService

        wd = get_cached_content("weather", ignore_expiry=True)  # 过期也显示旧的

        current_temp = "--"
        current_icon_code = 0
        if wd:
            current = wd.get("current", {})
            temp_obj = current.get("temperature", {})
            raw = temp_obj.get("value", "--")
            try:
                current_temp = int(round(float(raw)))
            except (ValueError, TypeError):
                current_temp = raw
            current_icon_code = current.get("weather", 0)
            try:
                current_icon_code = int(current_icon_code)
            except (ValueError, TypeError):
                current_icon_code = 0

        self.cityLabel.setText(cfg.city.value)
        self.currentTempLabel.setText(f"{current_temp}°")

        icon_name = WeatherService.ICON_MAP.get(current_icon_code, "2.svg")
        icon_path = WeatherService.get_weather_icon_path(icon_name)

        # 日志 当前天气
        weather_text = WeatherService.WEATHER_MAP.get(current_icon_code, ("未知", "2.svg"))[0]
        logger.info(f"[WeatherHourly] 城市:{cfg.city.value} 当前温度:{current_temp}° 天气代码:{current_icon_code} 天气:{weather_text} 图标:{icon_name}")

        if icon_path and os.path.exists(icon_path):
            dpr = self.devicePixelRatioF() if hasattr(self, 'devicePixelRatioF') else self.devicePixelRatio()
            pm = render_svg_icon(icon_path, 60, dpr)
            if not pm.isNull():
                self.currentIconLabel.setPixmap(pm)
            else:
                self.currentIconLabel.clear()
        else:
            self.currentIconLabel.clear()

        if wd and wd.get("forecastHourly"):
            parsed = WeatherService.parse_hourly(wd["forecastHourly"])
            if parsed:
                self._hourly_data = parsed
                logger.info(f"[WeatherHourly] 逐小时解析结果: {json.dumps(parsed, ensure_ascii=False)}")
                self._update_hourly_display()

    def _update_hourly_display(self):
        if not self._hourly_data:
            return

        from services.weather import WeatherService

        hours = self._hourly_data.get("hours", [])
        unit = self._hourly_data.get("unit", "℃")
        pub_time = self._hourly_data.get("pub_time", "")

        if unit in ("℃", "°C", "C", "c"):
            disp_unit = "℃"
        elif unit in ("℉", "°F", "F", "f"):
            disp_unit = "℉"
        else:
            disp_unit = "℃"

        start_hour = 0
        try:
            if pub_time:
                from datetime import datetime
                dt = datetime.fromisoformat(pub_time)
                start_hour = dt.hour
        except Exception:
            pass

        for i in range(6):
            time_label, icon_label, temp_label = self._hourly_widgets[i]
            if i < len(hours):
                hour_data = hours[i]
                hour = (start_hour + i) % 24
                time_label.setText(f"{hour:02d}:00")

                icon_name = hour_data.get("icon", "2.svg")
                icon_path = WeatherService.get_weather_icon_path(icon_name)
                if icon_path and os.path.exists(icon_path):
                    dpr = self.devicePixelRatioF() if hasattr(self, 'devicePixelRatioF') else self.devicePixelRatio()
                    pm = render_svg_icon(icon_path, 28, dpr)
                    if not pm.isNull():
                        icon_label.setPixmap(pm)
                    else:
                        icon_label.clear()
                else:
                    icon_label.clear()

                temp = hour_data.get("temp", "--")
                temp_label.setText(f"{temp}°")
            else:
                time_label.setText("--:00")
                icon_label.clear()
                temp_label.setText("--°")

        self._apply_style()

    def mousePressEvent(self, event):
        """城市标签单击打开选择城市"""
        if event.button() == Qt.MouseButton.LeftButton:
            # 将事件坐标转换为城市标签的本地坐标进行检测
            local_pos = self.cityLabel.mapFrom(self, event.pos())
            if self.cityLabel.rect().contains(local_pos):
                self._onCityLabelClicked()
                event.accept()
                return
        super().mousePressEvent(event)

    def _onCityLabelClicked(self):
        """打开框"""
        from services.weather import RegionSelectorDialog, RegionDatabase, WeatherService

        # 主窗口
        parent = getattr(self._home, 'mainWindow', None) or self._home

        dialog = RegionSelectorDialog(parent)
        if dialog.exec():
            region = dialog.get_selected_region()
            if not region:
                return

            # 保存城市名称和代码
            db = RegionDatabase()
            code = db.get_code(region)
            cfg.city.value = region
            if code:
                cfg.cityCode.value = code
                logger.info(f"[天气组件] 选择城市: {region} (code={code})")

            self.cityLabel.setText(region)

            # 请求
            if not code:
                code = "101010100"
            try:
                ws = WeatherService(code)
                data = ws.fetch_all()
                if data:
                    if not save_cache("weather", data, cfg.weatherUpdateInterval.value):
                        logger.warning("[天气组件] 缓存保存失败")
                    if hasattr(self._home, '_cached_weather'):
                        self._home._cached_weather = data
                    self._home.weather_updated.emit(data)
                    logger.info(f"[天气组件] 新城市天气获取成功")
            except Exception as e:
                logger.error(f"[天气组件] 新城市天气获取失败: {e}")

    def _apply_style(self):
        color = cfg.weatherTextColor.value
        color_str = color.name() if hasattr(color, 'name') else str(color)

        self.cityLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: 16px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
            opacity: 0.7;
        """)

        self.currentTempLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: 52px;
            font-weight: 300;
            font-family: {FONT_FAMILY};
            background-color: transparent;
            line-height: 1.0;
        """)

        self.alertLabel.setStyleSheet(f"""
            color: #ff6b6b;
            font-size: 12px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)

        for time_label, icon_label, temp_label in self._hourly_widgets:
            time_label.setStyleSheet(f"""
                color: {color_str};
                font-size: 12px;
                font-family: {FONT_FAMILY};
                background-color: transparent;
                opacity: 0.7;
            """)
            temp_label.setStyleSheet(f"""
                color: {color_str};
                font-size: 12px;
                font-family: {FONT_FAMILY};
                background-color: transparent;
            """)

        self.updateSize()


class WeatherWeeklyComponent(DraggableContainer):
    """逐日天气组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("weatherWeeklyContainer")
        self._home = parent
        self._daily_data = None
        self._setup_ui()
        self._setup_timer()

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        layout.setContentsMargins(14, 12, 14, 12)
        layout.setSpacing(4)

        # 上边：当前天气
        top = QWidget()
        top.setStyleSheet("background-color: transparent;")
        top_layout = QHBoxLayout(top)
        top_layout.setContentsMargins(0, 0, 0, 0)
        top_layout.setSpacing(0)

        # 左上：城市 当前温度
        top_left = QWidget()
        top_left.setStyleSheet("background-color: transparent;")
        tl_layout = QVBoxLayout(top_left)
        tl_layout.setContentsMargins(0, 0, 0, 0)
        tl_layout.setSpacing(2)

        self.cityLabel = QLabel("--")
        self.cityLabel.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.cityLabel.setCursor(Qt.CursorShape.PointingHandCursor)
        self.cityLabel.setToolTip(tr("weather_service.select_region"))

        self.currentTempLabel = QLabel("--°")
        self.currentTempLabel.setAlignment(Qt.AlignmentFlag.AlignLeft)

        tl_layout.addWidget(self.cityLabel)
        tl_layout.addWidget(self.currentTempLabel)
        tl_layout.addStretch()

        # 右上：图标
        top_right = QWidget()
        top_right.setStyleSheet("background-color: transparent;")
        tr_layout = QVBoxLayout(top_right)
        tr_layout.setContentsMargins(0, 0, 0, 0)
        tr_layout.setSpacing(0)

        self.currentIconLabel = QLabel()
        self.currentIconLabel.setAlignment(Qt.AlignmentFlag.AlignRight)
        self.currentIconLabel.setFixedSize(48, 48)

        tr_layout.addWidget(self.currentIconLabel)
        tr_layout.addStretch()

        top_layout.addWidget(top_left)
        top_layout.addStretch()
        top_layout.addWidget(top_right)

        # 下边：4天天气预报
        bottom = QWidget()
        bottom.setStyleSheet("background-color: transparent;")
        bottom_layout = QVBoxLayout(bottom)
        bottom_layout.setContentsMargins(0, 0, 0, 0)
        bottom_layout.setSpacing(0)

        self._forecast_rows = []
        for i in range(4):
            row = QWidget()
            row.setStyleSheet("background-color: transparent;")
            row.setFixedHeight(20)
            row_layout = QHBoxLayout(row)
            row_layout.setContentsMargins(0, 0, 0, 0)
            row_layout.setSpacing(0)

            day_label = QLabel("--")
            day_label.setFixedWidth(40)
            day_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)

            icon_label = QLabel()
            icon_label.setFixedSize(18, 18)
            icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)

            low_label = QLabel("--")
            low_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
            low_label.setObjectName(f"weeklyLow_{i}")

            spacer = QLabel()
            spacer.setFixedWidth(8)

            high_label = QLabel("--°")
            high_label.setFixedWidth(28)
            high_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
            high_label.setObjectName(f"weeklyHigh_{i}")

            row_layout.addWidget(day_label)
            row_layout.addStretch()
            row_layout.addWidget(icon_label)
            row_layout.addStretch()
            row_layout.addWidget(low_label)
            row_layout.addWidget(spacer)
            row_layout.addWidget(high_label)

            self._forecast_rows.append((day_label, icon_label, low_label, high_label))
            bottom_layout.addWidget(row)

        layout.addWidget(top)
        layout.addWidget(bottom)

        self.setMinimumSize(200, 200)
        self._size_explicitly_set = True
        self.resize(200, 200)
        self._apply_style()

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._refresh_weather)
        self.timer.start(1000)

        cfg.showWeather.valueChanged.connect(self._refresh_weather)
        cfg.weatherUpdateInterval.valueChanged.connect(self._update_interval)
        cfg.weatherTextColor.valueChanged.connect(self._apply_style)

        if hasattr(self._home, 'weather_updated'):
            self._home.weather_updated.connect(self._on_weather_updated)

        self._update_interval()
        self._refresh_weather()

    def _update_interval(self):
        interval_map = {
            "10s": 10000, "30s": 30000, "1m": 60000,
            "5m": 300000, "10m": 600000, "30m": 1800000,
        }
        interval_str = cfg.weatherUpdateInterval.value
        self.timer.setInterval(interval_map.get(interval_str, 300000))

    def _refresh_weather(self):
        if not cfg.showWeather.value:
            self.hide()
            return
        self.show()
        self._update_from_cache()

    def _on_weather_updated(self):
        """天气数据更新回调"""
        self._update_from_cache()

    def _update_from_cache(self):
        from services.weather import WeatherService

        wd = get_cached_content("weather", ignore_expiry=True)  # 过期也显示旧的

        # 当前天气
        current_temp = "--"
        current_icon_code = 0
        if wd:
            current = wd.get("current", {})
            temp_obj = current.get("temperature", {})
            raw = temp_obj.get("value", "--")
            try:
                current_temp = int(round(float(raw)))
            except (ValueError, TypeError):
                current_temp = raw
            current_icon_code = current.get("weather", 0)
            try:
                current_icon_code = int(current_icon_code)
            except (ValueError, TypeError):
                current_icon_code = 0

        self.cityLabel.setText(cfg.city.value)
        self.currentTempLabel.setText(f"{current_temp}°")

        icon_name = WeatherService.ICON_MAP.get(current_icon_code, "2.svg")
        icon_path = WeatherService.get_weather_icon_path(icon_name)

        # 日志：当前天气
        weather_text = WeatherService.WEATHER_MAP.get(current_icon_code, ("未知", "2.svg"))[0]
        logger.info(f"[WeatherWeekly] 城市:{cfg.city.value} 当前温度:{current_temp}° 天气代码:{current_icon_code} 天气:{weather_text} 图标:{icon_name}")

        if icon_path and os.path.exists(icon_path):
            dpr = self.devicePixelRatioF() if hasattr(self, 'devicePixelRatioF') else self.devicePixelRatio()
            pm = render_svg_icon(icon_path, 48, dpr)
            if not pm.isNull():
                self.currentIconLabel.setPixmap(pm)
            else:
                self.currentIconLabel.clear()
        else:
            self.currentIconLabel.clear()

        # 每日预报
        if wd and wd.get("forecastDaily"):
            parsed = WeatherService.parse_daily(wd["forecastDaily"])
            if parsed:
                self._daily_data = parsed
                logger.info(f"[WeatherWeekly] 每日预报解析结果: {json.dumps(parsed, ensure_ascii=False)}")
                if parsed.get("days"):
                    d0 = parsed["days"][0]
                    logger.info(f"[WeatherWeekly] 今日 高温:{d0.get('high')}° 低温:{d0.get('low')}° 天气代码:{d0.get('weather_code')} 图标:{d0.get('icon')}")
                self._update_daily_display()

    def _update_daily_display(self):
        if not self._daily_data:
            return

        from services.weather import WeatherService
        from datetime import datetime, timedelta

        days = self._daily_data.get("days", [])

        now = datetime.now()
        num_days = min(4, len(self._forecast_rows), len(days))

        for i in range(num_days):
            day_label, icon_label, low_label, high_label = self._forecast_rows[i]
            d = now + timedelta(days=i)
            day_data = days[i]

            if i == 0:
                day_label.setText("今日")
            else:
                weekday_names = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"]
                day_label.setText(weekday_names[d.weekday()])

            icon_name = day_data.get("icon", "2.svg")
            icon_path = WeatherService.get_weather_icon_path(icon_name)
            if icon_path and os.path.exists(icon_path):
                dpr = self.devicePixelRatioF() if hasattr(self, 'devicePixelRatioF') else self.devicePixelRatio()
                pm = render_svg_icon(icon_path, 18, dpr)
                if not pm.isNull():
                    icon_label.setPixmap(pm)
                else:
                    icon_label.clear()
            else:
                icon_label.clear()

            low_label.setText(day_data.get("low", "--"))
            high_label.setText(f"{day_data.get('high', '--')}°")

        self._apply_style()

    def mousePressEvent(self, event):
        """城市标签单击打开框"""
        if event.button() == Qt.MouseButton.LeftButton:
            # 检测城市标签点击
            local_pos = self.cityLabel.mapFrom(self, event.pos())
            if self.cityLabel.rect().contains(local_pos):
                self._onCityLabelClicked()
                event.accept()
                return
        super().mousePressEvent(event)

    def _onCityLabelClicked(self):
        """打开框"""
        from services.weather import RegionSelectorDialog, RegionDatabase, WeatherService

        # 主窗口
        parent = getattr(self._home, 'mainWindow', None) or self._home

        dialog = RegionSelectorDialog(parent)
        if dialog.exec():
            region = dialog.get_selected_region()
            if not region:
                return

            # 保存城市名称和代码
            db = RegionDatabase()
            code = db.get_code(region)
            cfg.city.value = region
            if code:
                cfg.cityCode.value = code
                logger.info(f"[天气组件] 选择城市: {region} (code={code})")

            self.cityLabel.setText(region)

            # 请求
            if not code:
                code = "101010100"
            try:
                ws = WeatherService(code)
                data = ws.fetch_all()
                if data:
                    if not save_cache("weather", data, cfg.weatherUpdateInterval.value):
                        logger.warning("[天气组件] 缓存保存失败")
                    if hasattr(self._home, '_cached_weather'):
                        self._home._cached_weather = data
                    self._home.weather_updated.emit(data)
                    logger.info(f"[天气组件] 新城市天气获取成功")
            except Exception as e:
                logger.error(f"[天气组件] 新城市天气获取失败: {e}")

    def _apply_style(self):
        color = cfg.weatherTextColor.value
        color_str = color.name() if hasattr(color, 'name') else str(color)

        # 城市名
        self.cityLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: 14px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
            opacity: 0.7;
        """)

        # 大温度
        self.currentTempLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: 48px;
            font-weight: 300;
            font-family: {FONT_FAMILY};
            background-color: transparent;
            line-height: 1.0;
        """)

        # 预报行
        for day_label, icon_label, low_label, high_label in self._forecast_rows:
            day_label.setStyleSheet(f"""
                color: {color_str};
                font-size: 11px;
                font-family: {FONT_FAMILY};
                background-color: transparent;
                opacity: 0.7;
            """)
            low_label.setStyleSheet(f"""
                color: {color_str};
                font-size: 11px;
                opacity: 0.6;
                font-family: {FONT_FAMILY};
                background-color: transparent;
            """)
            high_label.setStyleSheet(f"""
                color: {color_str};
                font-size: 11px;
                font-family: {FONT_FAMILY};
                background-color: transparent;
            """)

        self.updateSize()


class PoetryOneLineComponent(DraggableContainer):
    """一言组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("poetryContainer")
        self._home = parent
        self._setup_ui()
        self._setup_timer()

    def _setup_ui(self):
        self.poetryLabel = QLabel("")
        self.poetryLabel.setObjectName("poetryLabel")
        self.poetryLabel.setAlignment(Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignHCenter)
        self.poetryLabel.setWordWrap(False)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.addWidget(self.poetryLabel)

        self.setMinimumSize(400, 200)
        self._size_explicitly_set = True
        self.resize(400, 200)
        self._apply_style()

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._refresh_poetry)
        self.timer.start(1000)

        cfg.showPoetry.valueChanged.connect(self._refresh_poetry)
        cfg.poetryApiUrl.valueChanged.connect(self._refresh_poetry)
        cfg.poetryUpdateInterval.valueChanged.connect(self._update_interval)
        cfg.poetryTextColor.valueChanged.connect(self._apply_style) 
        cfg.poetrySize.valueChanged.connect(self._apply_style)

        if hasattr(self._home, 'poetry_updated'):
            self._home.poetry_updated.connect(self._update_poetry)

        self._update_interval()
        self._refresh_poetry()

    def _update_poetry(self, text):
        """收到信号更新显示"""
        if text:
            self.poetryLabel.setText(text)
            self.updateSize()

    def _update_interval(self):
        interval_map = {"10s": 10000, "30s": 30000, "1m": 60000, "5m": 300000, "10m": 600000, "30m": 1800000, "1h": 3600000}
        interval_str = cfg.poetryUpdateInterval.value
        self.timer.setInterval(interval_map.get(interval_str, 60000))

    def _refresh_poetry(self):
        if not cfg.showPoetry.value:
            self.hide()
            return
        self.show()

        # 缓存获取
        if hasattr(self._home, '_cached_poetry') and self._home._cached_poetry:
            self.poetryLabel.setText(self._home._cached_poetry)
        else:
            self.poetryLabel.setText("")

    def _apply_style(self):
        color = cfg.poetryTextColor.value   
        color_str = color.name() if hasattr(color, 'name') else str(color)
        size = cfg.poetrySize.value

        self.poetryLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {size}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)
        self.updateSize()


class CountdownEventComponent(DraggableContainer):
    """事件倒计时组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("countdownContainer")
        self._home = parent
        self._carousel_index = 0
        self._setup_ui()
        self._setup_timer()

    def _setup_ui(self):
        self.countdownLabel = QLabel("")
        self.countdownLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.addWidget(self.countdownLabel)

        self.setMinimumSize(200, 200)
        self._size_explicitly_set = True
        self.resize(200, 200)
        self._apply_style()

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._update_countdown)
        self.timer.start(1000)

        self.carousel_timer = QTimer(self)
        self.carousel_timer.timeout.connect(self._next_carousel)

        cfg.showCountdown.valueChanged.connect(self._update_countdown)
        cfg.countdownDisplayMode.valueChanged.connect(self._update_countdown)
        cfg.countdownCarouselInterval.valueChanged.connect(self._update_carousel_interval)
        cfg.countdownList.valueChanged.connect(self._update_countdown)
        cfg.countdownTextColor.valueChanged.connect(self._apply_style)
        cfg.countdownTextSize.valueChanged.connect(self._apply_style)

        self._update_carousel_interval()
        self._update_countdown()

    def _update_carousel_interval(self):
        interval = cfg.countdownCarouselInterval.value
        self.carousel_timer.setInterval(interval * 1000)
        if cfg.countdownDisplayMode.value == "carousel":
            self.carousel_timer.start()
        else:
            self.carousel_timer.stop()

    def _next_carousel(self):
        countdown_list = cfg.countdownList.value
        if countdown_list:
            self._carousel_index = (self._carousel_index + 1) % len(countdown_list)
            self._update_countdown()

    def _update_countdown(self):
        if not cfg.showCountdown.value:
            self.hide()
            return
        self.show()

        countdown_list = cfg.countdownList.value
        if not countdown_list:
            self.countdownLabel.setText(tr("countdown.no_events"))
            return

        display_mode = cfg.countdownDisplayMode.value

        if display_mode == "carousel":
            idx = self._carousel_index % len(countdown_list)
            event = countdown_list[idx]
            self._display_event(event)
        else:
            # 显示第一个或最近的
            event = countdown_list[0]
            self._display_event(event)

    def _display_event(self, event):
        name = event.get("name", "")
        target_time = event.get("targetTime")
        if not target_time:
            self.countdownLabel.setText(name)
            return

        try:
            target_dt = py_datetime.datetime.fromisoformat(target_time)
            now = py_datetime.datetime.now()
            delta = target_dt - now

            if delta.total_seconds() > 0:
                days = delta.days
                hours, remainder = divmod(delta.seconds, 3600)
                minutes, seconds = divmod(remainder, 60)
                text = f"{name}: {days}天 {hours}时 {minutes}分 {seconds}秒"
            else:
                text = f"{name}: 已到期"

            self.countdownLabel.setText(text)
        except Exception:
            self.countdownLabel.setText(name)

    def _apply_style(self):
        color = cfg.countdownTextColor.value
        color_str = color.name() if hasattr(color, 'name') else str(color)
        size = cfg.countdownTextSize.value

        self.countdownLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {size}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)
        self.updateSize()


class SchoolInfoComponent(DraggableContainer):
    """学校信息组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("schoolInfoContainer")
        self._home = parent
        self._setup_ui()

    def _setup_ui(self):
        self.classLabel = QLabel("")
        self.classLabel.setObjectName("schoolClassLabel")
        self.classLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.nameLabel = QLabel("")
        self.nameLabel.setObjectName("schoolNameLabel")
        self.nameLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.setSpacing(6)
        layout.addWidget(self.classLabel)
        layout.addWidget(self.nameLabel)

        self.setMinimumSize(200, 200)
        self._size_explicitly_set = True
        self.resize(200, 200)
        self._update_info()

    def _update_info(self):
        # 从 ClassWidgets 数据更新
        if hasattr(self._home, 'schoolClassLabel') and hasattr(self._home, 'schoolNameLabel'):
            self.classLabel.setText(self._home.schoolClassLabel.text())
            self.nameLabel.setText(self._home.schoolNameLabel.text())
        else:
            self.classLabel.setText("")
            self.nameLabel.setText("")


class MediaPlayerComponent(DraggableContainer):
    """媒体播放器组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("mediaContainer")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, False)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Maximum)
        self._home = parent
        self._setup_ui()
        self._setup_timer()

    def _setup_ui(self):
        # 嵌入原有的 MediaWidget
        self.mediaWidget = MediaWidget(self)
        self.mediaWidget.setObjectName("mediaWidget")

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignBottom | Qt.AlignmentFlag.AlignLeft)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(0)
        layout.addWidget(self.mediaWidget)

        self.setMinimumSize(400, 200)
        self._size_explicitly_set = True
        self.resize(400, 200)

    def _setup_timer(self):
        cfg.showMediaInfo.valueChanged.connect(self._on_visibility_changed)

    def _on_visibility_changed(self):
        if cfg.showMediaInfo.value:
            self.show()
        else:
            self.hide()


class QuickLaunchDockComponent(DraggableContainer):
    """快捷启动栏组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("quickLaunchContainer")
        self._home = parent
        self._setup_ui()
        self._setup_signals()

    def _setup_ui(self):
        self.dock = QuickLaunchDock(self)
        self.dock.setObjectName("quickLaunchDock")

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(20, 16, 20, 16)
        layout.addWidget(self.dock)

        self.setMinimumSize(400, 200)
        self._size_explicitly_set = True
        self.resize(400, 200)

        # 加载应用
        apps = cfg.quickLaunchApps.value
        if apps:
            self.dock.set_apps(apps)

    def _setup_signals(self):
        cfg.showQuickLaunch.valueChanged.connect(self._on_visibility_changed)
        cfg.quickLaunchApps.valueChanged.connect(self._update_apps)
        cfg.quickLaunchIconSize.valueChanged.connect(self._update_apps)
        cfg.quickLaunchIconSpacing.valueChanged.connect(self._update_apps)
        cfg.quickLaunchShowLabels.valueChanged.connect(self._update_apps)

    def _on_visibility_changed(self):
        if cfg.showQuickLaunch.value:
            self.show()
            self._update_apps()
        else:
            self.hide()

    def _update_apps(self):
        apps = cfg.quickLaunchApps.value
        if apps:
            self.dock.set_apps(apps)


SOLAR_TERMS_CN = [
    "小寒", "大寒", "立春", "雨水", "惊蛰", "春分",
    "清明", "谷雨", "立夏", "小满", "芒种", "夏至",
    "小暑", "大暑", "立秋", "处暑", "白露", "秋分",
    "寒露", "霜降", "立冬", "小雪", "大雪", "冬至",
]

MONTH_NAMES_CN = ["一月", "二月", "三月", "四月", "五月", "六月",
                  "七月", "八月", "九月", "十月", "十一月", "十二月"]

HOLIDAY_SHORT_MAP = {
    # 法定节假日
    "元旦节": "元旦",
    "春节": "春节",
    "清明节": "清明",
    "国际劳动节": "劳动",
    "端午节": "端午",
    "中秋节": "中秋",
    "国庆节": "国庆",
    # 传统节日
    "元宵节": "元宵",
    "小年": "小年",
    "七夕-魁星诞": "七夕",
    "中元节": "中元",
    "重阳节-酆都大帝诞": "重阳",
    "腊八节-释迦如来成佛之辰": "腊八",
    "春龙节-福德土地正神诞": "龙抬头",
    # 其他节日
    "情人节": "情人",
    "国际劳动妇女节": "妇女",
    "中国植树节": "植树",
    "孙中山逝世纪念日,中国植树节": "植树",
    "国际愚人节": "愚人",
    "中国青年节": "青年",
    "母亲节": "母亲",
    "国际儿童节": "儿童",
    "父亲节": "父亲",
    "中国共产党诞生日,香港回归纪念日": "建党",
    "中国人民解放军建军节": "建军",
    "中国教师节": "教师",
    "平安夜": "平安夜",
    "圣诞节": "圣诞",
    "国际和平日": "和平",
    "中国人民抗日战争纪念日": "抗日",
    "中国抗日战争胜利纪念日": "抗日",
    "抗美援朝纪念日": "抗美",
    "南京大屠杀纪念日": "公祭",
    "上海解放日": "解放",
    # 二十四节气
    "小寒": "小寒",
    "大寒": "大寒",
    "立春": "立春",
    "雨水": "雨水",
    "惊蛰": "惊蛰",
    "春分": "春分",
    "清明": "清明",
    "谷雨": "谷雨",
    "立夏": "立夏",
    "小满": "小满",
    "芒种": "芒种",
    "夏至": "夏至",
    "小暑": "小暑",
    "大暑": "大暑",
    "立秋": "立秋",
    "处暑": "处暑",
    "白露": "白露",
    "秋分": "秋分",
    "寒露": "寒露",
    "霜降": "霜降",
    "立冬": "立冬",
    "小雪": "小雪",
    "大雪": "大雪",
    "冬至": "冬至",
}


def _short_holiday(name: str) -> str:
    """节日名称简写"""
    if not name:
        return ""
    if "," in name:
        parts = [p.strip() for p in name.split(",")]
        for p in parts:
            if p in HOLIDAY_SHORT_MAP:
                return HOLIDAY_SHORT_MAP[p]
        return ""
    return HOLIDAY_SHORT_MAP.get(name, "")


def _get_day_info(year: int, month: int, day: int) -> tuple:
    """→ (holiday, solar_term, lunar_day)"""
    holiday = ""
    solar_term = ""
    lunar_day = ""
    try:
        import cnlunar
        dt = py_datetime.datetime(year, month, day, 0, 0, 0)
        lunar = cnlunar.Lunar(dt)
        term = lunar.todaySolarTerms
        if term and term in SOLAR_TERMS_CN:
            solar_term = term
        legal = lunar.get_legalHolidays().strip()
        if legal:
            holiday = legal
        else:
            other = lunar.get_otherHolidays().strip()
            if other:
                holiday = other
            else:
                other_lunar = lunar.get_otherLunarHolidays().strip()
                if other_lunar:
                    holiday = other_lunar
        # 农历日（初一显示月名，其他显示日）
        day_num = lunar.day
        lunar_day = lunar.getDayInChinese()
    except Exception:
        pass
    return holiday, solar_term, lunar_day


class _DayCell(QWidget):
    """单个日期格子"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("calCell")
        self._day = 0
        self._is_current_month = True
        self._is_today = False
        self._is_weekend = False
        self._sub_text = ""

    def set_data(self, day: int, sub_text: str, is_current_month: bool,
                 is_today: bool, is_weekend: bool):
        self._day = day
        self._sub_text = sub_text
        self._is_current_month = is_current_month
        self._is_today = is_today
        self._is_weekend = is_weekend
        self.update()

    def clear(self):
        self._day = 0
        self._sub_text = ""
        self._is_current_month = True
        self._is_today = False
        self._is_weekend = False
        self.update()

    def paintEvent(self, e):
        if self._day == 0:
            return
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        r = self.rect()
        w, h = r.width(), r.height()
        sz = min(w, h)

        mid = h * 0.62  # 分割线位置 62% 给日期

        # 日期数字
        day_font = QFont("HarmonyOS Sans")
        day_font.setPixelSize(int(sz * 0.46))
        painter.setFont(day_font)

        if self._is_today:
            cx = r.center().x() + int(w * 0.02)
            cy = int(mid / 2)
            painter.setPen(Qt.PenStyle.NoPen)
            painter.setBrush(QColor("#00b7c3"))
            painter.drawEllipse(QPoint(cx, cy), int(sz * 0.40), int(sz * 0.40))

        if self._is_today:
            painter.setPen(QColor("#ffffff"))
        elif not self._is_current_month:
            painter.setPen(QColor("#555555"))
        elif self._is_weekend:
            painter.setPen(QColor("#9a9a9a"))
        else:
            painter.setPen(QColor("#e8e8e8"))

        day_rect = QRect(r.left() + int(w * 0.04), 0, w, int(mid))
        painter.drawText(day_rect, Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignVCenter, str(self._day))

        # 节日文字
        if self._sub_text and self._is_current_month:
            sub_font = QFont("HarmonyOS Sans")
            sub_font.setPixelSize(int(sz * 0.26))
            painter.setFont(sub_font)
            painter.setPen(QColor("#ffffff") if self._is_today else QColor("#c0c0c0"))
            sub_rect = QRect(r.left(), int(mid), w, int(h - mid))
            painter.drawText(sub_rect, Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignVCenter, self._sub_text)


class CalendarMonthComponent(DraggableContainer):
    """月历"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("calendarContainer")
        self._home = parent
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        today = QDate.currentDate()
        self._display_year = today.year()
        self._display_month = today.month()

        self._cells = []
        self._setup_ui()
        self._setup_timer()
        self._refresh_calendar()

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setContentsMargins(12, 10, 12, 8)
        layout.setSpacing(4)

        # 标题栏
        title_layout = QHBoxLayout()
        title_layout.setContentsMargins(0, 0, 0, 0)
        title_layout.setSpacing(0)

        self._title_label = QLabel("")
        self._title_label.setObjectName("calTitle")
        title_layout.addWidget(self._title_label)
        title_layout.addStretch()

        btn_layout = QVBoxLayout()
        btn_layout.setContentsMargins(0, 0, 0, 0)
        btn_layout.setSpacing(0)

        self._up_btn = TransparentToolButton(self)
        self._up_btn.setIcon(FUI.CHEVRON_UP.icon())
        self._up_btn.setFixedSize(28, 18)
        self._up_btn.clicked.connect(self._go_prev_month)

        self._down_btn = TransparentToolButton(self)
        self._down_btn.setIcon(FUI.CHEVRON_DOWN.icon())
        self._down_btn.setFixedSize(28, 18)
        self._down_btn.clicked.connect(self._go_next_month)

        btn_layout.addWidget(self._up_btn)
        btn_layout.addWidget(self._down_btn)
        title_layout.addLayout(btn_layout)

        layout.addLayout(title_layout)

        # 星期标题
        wk_names = ["日", "一", "二", "三", "四", "五", "六"]
        wk_layout = QHBoxLayout()
        wk_layout.setContentsMargins(0, 0, 0, 0)
        wk_layout.setSpacing(0)
        for i, n in enumerate(wk_names):
            lbl = QLabel(n)
            lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
            lbl.setObjectName("calWk")
            if i == 0 or i == 6:
                lbl.setProperty("wkend", True)
            wk_layout.addWidget(lbl)
        layout.addLayout(wk_layout)

        # 5行日期
        grid_w = QWidget()
        grid = QGridLayout(grid_w)
        grid.setContentsMargins(0, 0, 0, 0)
        grid.setSpacing(0)
        for c in range(7):
            grid.setColumnStretch(c, 1)
        for r in range(5):
            grid.setRowStretch(r, 1)

        self._cells = []
        for r in range(5):
            row = []
            for c in range(7):
                cell = _DayCell(self)
                grid.addWidget(cell, r, c)
                row.append(cell)
            self._cells.append(row)

        self._grid = grid

        layout.addWidget(grid_w, 1)

        self.setMinimumSize(300, 300)
        self._size_explicitly_set = True
        self.resize(300, 300)
        self._apply_style()

    def _setup_timer(self):
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._check_day_change)
        self._timer.start(60000)

    def _check_day_change(self):
        t = QDate.currentDate()
        if t.year() == self._display_year and t.month() == self._display_month:
            self._refresh_calendar()

    def _go_prev_month(self):
        self._display_month -= 1
        if self._display_month < 1:
            self._display_month = 12
            self._display_year -= 1
        self._refresh_calendar()

    def _go_next_month(self):
        self._display_month += 1
        if self._display_month > 12:
            self._display_month = 1
            self._display_year += 1
        self._refresh_calendar()

    def _refresh_calendar(self):
        import calendar as cal
        year, month = self._display_year, self._display_month
        today = QDate.currentDate()

        # 标题
        self._title_label.setText(f"{MONTH_NAMES_CN[month-1]} {year}")

        first_wd = cal.monthrange(year, month)[0]  # 0=Monday
        dim = cal.monthrange(year, month)[1]

        # 上月天数
        prev_month = month - 1 if month > 1 else 12
        prev_year = year if month > 1 else year - 1
        prev_dim = cal.monthrange(prev_year, prev_month)[1]

        # 清空
        for r in range(5):
            for c in range(7):
                self._cells[r][c].clear()

        # 填充上月末尾日期
        for i in range(first_wd):
            col = i
            day_num = prev_dim - first_wd + 1 + i
            self._cells[0][col].set_data(
                day=day_num, sub_text="", is_current_month=False,
                is_today=False, is_weekend=(col == 0 or col == 6)
            )

        # 填充本月
        day = 1
        for r in range(5):
            for c in range(7):
                if r == 0 and c < first_wd:
                    continue
                if day > dim:
                    break

                is_today = (year == today.year() and month == today.month() and day == today.day())
                is_wkend = (c == 0 or c == 6)

                holiday, term, _ = _get_day_info(year, month, day)
                sub = ""
                if holiday:
                    sub = _short_holiday(holiday)
                elif term:
                    sub = _short_holiday(term)

                self._cells[r][c].set_data(
                    day=day, sub_text=sub, is_current_month=True,
                    is_today=is_today, is_weekend=is_wkend
                )

                day += 1

        self.updateSize()

    def _apply_style(self):
        self.setStyleSheet(f"""
            #calTitle {{
                color: #f0f0f0;
                font-size: 20px;
                font-weight: 600;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}
            #calWk {{
                color: #d0d0d0;
                font-size: 13px;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}
            #calWk[wkend="true"] {{
                color: #b0b0b0;
            }}
        """)


# 更新注册表

COMPONENT_STYLES["clock"]["digital"]["class"] = DigitalClockComponent
COMPONENT_STYLES["weather"]["icon_temp"]["class"] = WeatherIconTempComponent
COMPONENT_STYLES["weather"]["hourly"]["class"] = WeatherHourlyComponent
COMPONENT_STYLES["weather"]["weekly"]["class"] = WeatherWeeklyComponent
COMPONENT_STYLES["poetry"]["one_line"]["class"] = PoetryOneLineComponent
COMPONENT_STYLES["countdown"]["event"]["class"] = CountdownEventComponent
COMPONENT_STYLES["school_info"]["class_info"]["class"] = SchoolInfoComponent
COMPONENT_STYLES["media"]["player"]["class"] = MediaPlayerComponent
COMPONENT_STYLES["quick_launch"]["dock"]["class"] = QuickLaunchDockComponent
COMPONENT_STYLES["clock"]["calendar_month"]["class"] = CalendarMonthComponent








# 组件库面板

# 预览图目录
PREVIEW_DIR = os.path.join(os.path.dirname(os.path.dirname(__file__)), "resources", "component_preview")


class ComponentCard(CardWidget):
    """组件卡片"""
    
    def __init__(self, definition: ComponentDefinition, parent=None):
        super().__init__(parent)
        self.definition = definition
        self._preview_pixmap = None
        
        self._setup_ui()
        self._load_preview()
    
    def _setup_ui(self):
        """设置 UI"""
        self.setFixedSize(180, 120)
        self.setCursor(Qt.CursorShape.OpenHandCursor)
        
        layout = QVBoxLayout(self)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(6)
        
        # 预览图
        self.preview_label = QLabel()
        self.preview_label.setFixedSize(164, 80)
        self.preview_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.preview_label.setStyleSheet("background: transparent; border-radius: 6px;")
        layout.addWidget(self.preview_label)
        
        # 名称
        name_label = BodyLabel(self.definition.display_name)
        name_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(name_label)
    
    def _load_preview(self):
        """加载预览图"""
        image_name = f"{self.definition.id}.png"
        image_path = os.path.join(PREVIEW_DIR, image_name)
        
        if os.path.exists(image_path):
            pixmap = QPixmap(image_path)
            if not pixmap.isNull():
                scaled = pixmap.scaled(
                    164, 80,
                    Qt.AspectRatioMode.KeepAspectRatioByExpanding,
                    Qt.TransformationMode.SmoothTransformation
                )
                self.preview_label.setPixmap(scaled)
                self._preview_pixmap = scaled
                return
        
        # 无图片显示占位
        self.preview_label.setText(tr("component_library.no_preview"))
        self.preview_label.setStyleSheet("background: rgba(255,255,255,0.05); border-radius: 6px;")
    
    def mousePressEvent(self, event):
        """鼠标按下"""
        if event.button() == Qt.MouseButton.LeftButton:
            self._drag_start = event.position().toPoint()
            self.setCursor(Qt.CursorShape.ClosedHandCursor)
        super().mousePressEvent(event)
    
    def mouseReleaseEvent(self, event):
        """鼠标释放"""
        self.setCursor(Qt.CursorShape.OpenHandCursor)
        super().mouseReleaseEvent(event)
    
    def mouseMoveEvent(self, event):
        """开始拖拽"""
        if not (event.buttons() & Qt.MouseButton.LeftButton):
            return

        if not hasattr(self, '_drag_start'):
            return
        distance = (event.position().toPoint() - self._drag_start).manhattanLength()
        if distance >= QApplication.startDragDistance():
            self._start_drag()

        super().mouseMoveEvent(event)

    def _start_drag(self):
        """开始拖拽"""
        drag = QDrag(self)

        # 解析 definition.id 为 type|style 格式
        def_id = self.definition.id
        comp_type, comp_style = "", ""
        for t_name in COMPONENT_STYLES:
            if def_id.startswith(t_name + "_"):
                comp_type = t_name
                comp_style = def_id[len(t_name) + 1:]
                break
            elif def_id == t_name:
                comp_type = t_name
                comp_style = list(COMPONENT_STYLES[t_name].keys())[0]
                break
        if not comp_type:
            comp_type, comp_style = def_id.split("_", 1) if "_" in def_id else (def_id, "default")

        mime = QMimeData()
        mime.setData("application/x-classlively-component",
                    f"{comp_type}|{comp_style}".encode('utf-8'))
        drag.setMimeData(mime)
        
        # 设置拖拽图
        if self._preview_pixmap:
            drag.setPixmap(self._preview_pixmap)
        else:
            pixmap = self.grab()
            drag.setPixmap(pixmap.scaled(150, 80, Qt.AspectRatioMode.KeepAspectRatio))
        
        drag.setHotSpot(QPoint(90, 60))
        drag.exec(Qt.DropAction.CopyAction)
        
        self.setCursor(Qt.CursorShape.OpenHandCursor)



class CategoryPage(ScrollArea):
    """分类页面"""
    
    def __init__(self, category: str, registry: ComponentRegistry, parent=None):
        super().__init__(parent)
        self.category = category
        self.registry = registry
        self.setObjectName(f"category_{category}")
        
        self._setup_ui()
    
    def _setup_ui(self):
        """设置 UI"""
        self.setWidgetResizable(True)
        self.setStyleSheet("background: transparent;")
        
        container = QWidget()
        layout = QVBoxLayout(container)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.setSpacing(12)
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        
        # 分类标题
        title = StrongBodyLabel(self.category)
        layout.addWidget(title)
        
        # 组件卡片网格
        cards_widget = QWidget()
        cards_layout = QHBoxLayout(cards_widget)
        cards_layout.setSpacing(12)
        cards_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
        
        definitions = self.registry.get_definitions_by_category(self.category)
        for defn in definitions:
            card = ComponentCard(defn, self)
            cards_layout.addWidget(card)
        
        if not definitions:
            hint = BodyLabel(tr("component_library.no_components"))
            cards_layout.addWidget(hint)
        
        layout.addWidget(cards_widget)
        layout.addStretch()
        
        self.setWidget(container)


class ComponentLibraryWindow(FluentWindow):
    """组件库窗口"""
    
    def __init__(self, registry, parent=None):
        super().__init__(parent)
        self.registry = registry
        
        self._setup_navigation()
        self.setWindowTitle(tr("component_library.title"))
        self.resize(600, 400)
        
        if isDarkTheme():
            setTheme(cfg.themeMode.value)
        
        self.setStyleSheet(load_qss('setting.qss'))
    
    def _setup_navigation(self):
        """设置导航"""
        # 获取分类
        categories = self.registry.get_categories()

        icon_map = {
            "Clock": FUI.SYNC,        # 时钟
            "Weather": FUI.PHOTO,     # 天气
            "Info": FUI.INFO,         # 信息
            "Media": FUI.ALBUM,       # 媒体
            "Launcher": FUI.APPLICATION,  # 启动器
        }
        
        for category in categories:
            page = CategoryPage(category, self.registry, self)
            icon = icon_map.get(category, FUI.HOME)
            self.addSubInterface(page, icon, category)



class StyleCardWidget(CardWidget):
    """样式卡片"""

    def __init__(self, component_type: str, style_name: str, style_info: dict, parent=None):
        super().__init__(parent)
        self.component_type = component_type
        self.style_name = style_name
        self.style_info = style_info
        self.setFixedSize(260, 180)
        self._drag_start_pos = None
        self._preview_pixmap = None
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(12, 12, 12, 12)
        layout.setSpacing(6)
        layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignHCenter)

        # 预览图片区域
        self.preview_label = QLabel()
        self.preview_label.setFixedSize(236, 120)
        self.preview_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.preview_label.setStyleSheet("""
            background-color: transparent;
            border-radius: 8px;
        """)
        
        # 加载预览图片
        self._load_preview_image()
        
        layout.addWidget(self.preview_label)

        # 样式名称
        name_label = StrongBodyLabel(self.style_info.get("name", self.style_name))
        name_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(name_label)

    def _load_preview_image(self):
        """加载预览图片，不存在则显示空白"""
        # resources/component_preview/{type}_{style}.png
        image_name = f"{self.component_type}_{self.style_name}.png"
        image_path = os.path.join(PREVIEW_DIR, image_name)
        
        if os.path.exists(image_path):
            pixmap = QPixmap(image_path)
            if not pixmap.isNull():
                target_size = QSize(236, 120)
                scaled = pixmap.scaled(
                    target_size,
                    Qt.AspectRatioMode.KeepAspectRatioByExpanding,
                    Qt.TransformationMode.SmoothTransformation
                )
                if scaled.width() > target_size.width() or scaled.height() > target_size.height():
                    x = (scaled.width() - target_size.width()) // 2
                    y = (scaled.height() - target_size.height()) // 2
                    scaled = scaled.copy(x, y, target_size.width(), target_size.height())
                
                self.preview_label.setPixmap(scaled)
                self.preview_label.setStyleSheet("border-radius: 8px;")
                self._preview_pixmap = scaled
                return
        
        self.preview_label.clear()
        self.preview_label.setStyleSheet("""
            background-color: rgba(255, 255, 255, 0.05);
            border-radius: 8px;
        """)

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self._drag_start_pos = event.position().toPoint()
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event):
        if not (event.buttons() & Qt.MouseButton.LeftButton):
            return
        if self._drag_start_pos is None:
            return

        distance = (event.position().toPoint() - self._drag_start_pos).manhattanLength()
        if distance >= QApplication.startDragDistance():
            self._start_drag()

        super().mouseMoveEvent(event)

    def _start_drag(self):
        drag = QDrag(self)

        # 设置拖拽数组件类型和样式
        mime_data = QMimeData()
        mime_data.setData("application/x-classlively-component", 
                          f"{self.component_type}|{self.style_name}".encode('utf-8'))
        drag.setMimeData(mime_data)

        # 创建拖拽预览图
        if self._preview_pixmap and not self._preview_pixmap.isNull():
            drag.setPixmap(self._preview_pixmap)
        else:
            pixmap = self.grab()
            scaled = pixmap.scaled(200, 100, Qt.AspectRatioMode.KeepAspectRatio, 
                                   Qt.TransformationMode.SmoothTransformation)
            drag.setPixmap(scaled)
        
        drag.setHotSpot(QPoint(100, 60))

        # 执行拖拽
        drag.exec(Qt.DropAction.CopyAction)
        self._drag_start_pos = None


class ComponentTypePage(QWidget):
    """组件类型页面"""

    def __init__(self, component_type: str, styles: dict, edit_window, parent=None):
        super().__init__(parent)
        self.component_type = component_type
        self.styles = styles
        self.edit_window = edit_window
        self._current_index = 0
        self._style_cards = []
        self._selected_component_id = None  # 当前选中的组件
        
        # 必设 objectName os必上b站
        self.setObjectName(f"componentPage_{component_type}")
        
        self._setup_ui()
        self._refresh_component_list()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(16)

        # 已添加组件列表区域
        list_header = StrongBodyLabel(tr("component_edit.added_components"))
        layout.addWidget(list_header)

        self.component_list = QListWidget(self)
        self.component_list.setFixedHeight(120)
        self.component_list.setSelectionMode(QListWidget.SelectionMode.SingleSelection)
        self.component_list.itemClicked.connect(self._on_component_selected)
        layout.addWidget(self.component_list)

        # 删除按钮
        delete_btn = PushButton(FUI.DELETE, tr("component_edit.delete_selected"), self)
        delete_btn.setFixedHeight(32)
        delete_btn.clicked.connect(self._delete_selected_component)
        layout.addWidget(delete_btn, alignment=Qt.AlignmentFlag.AlignLeft)

        # 分隔线
        separator = QWidget()
        separator.setFixedHeight(2)
        separator.setStyleSheet("background-color: rgba(255, 255, 255, 0.1);")
        layout.addWidget(separator)

        # 样式选择区域
        style_header = StrongBodyLabel(tr("component_edit.select_style"))
        layout.addWidget(style_header)

        style_area = QWidget()
        style_layout = QHBoxLayout(style_area)
        style_layout.setContentsMargins(0, 0, 0, 0)
        style_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

        # 左箭头
        self.prev_btn = ToolButton(FUI.LEFT_ARROW, self)
        self.prev_btn.setFixedSize(32, 32)
        self.prev_btn.clicked.connect(self._prev_style)
        style_layout.addWidget(self.prev_btn)

        # 样式卡片容器
        self.card_container = QWidget()
        self.card_container.setFixedSize(220, 140)
        self.card_layout = QVBoxLayout(self.card_container)
        self.card_layout.setContentsMargins(0, 0, 0, 0)
        style_layout.addWidget(self.card_container)

        # 右箭头
        self.next_btn = ToolButton(FUI.RIGHT_ARROW, self)
        self.next_btn.setFixedSize(32, 32)
        self.next_btn.clicked.connect(self._next_style)
        style_layout.addWidget(self.next_btn)

        layout.addWidget(style_area)

        # 添加按钮
        add_btn = PushButton(FUI.ADD, tr("component_edit.add_component"), self)
        add_btn.setFixedHeight(36)
        add_btn.clicked.connect(self._add_component)
        layout.addWidget(add_btn, alignment=Qt.AlignmentFlag.AlignCenter)

        # 配置面板区域
        self.config_header = StrongBodyLabel(tr("component_edit.component_config"))
        self.config_header.hide()
        layout.addWidget(self.config_header)

        self.config_panel = QWidget()
        self.config_panel_layout = QVBoxLayout(self.config_panel)
        self.config_panel_layout.setContentsMargins(0, 0, 0, 0)
        self.config_panel.hide()
        layout.addWidget(self.config_panel)

        # 初始化样式卡片
        self._create_style_cards()
        self._show_current_card()

    def _refresh_component_list(self):
        """刷新已添加组件列表"""
        self.component_list.clear()
        
        home = self.edit_window.main_window.homeInterface
        if not home or not hasattr(home, 'component_manager'):
            return
        
        manager = home.component_manager
        
        # 获取当前类型的所有组件
        for comp_id, comp_data in manager._component_data.items():
            if comp_data.get("type") == self.component_type:
                style_name = comp_data.get("style", "default")
                style_info = self.styles.get(style_name, {})
                display_name = style_info.get("name", style_name)
                
                item = QListWidgetItem(f"{display_name} ({comp_id})")
                item.setData(Qt.ItemDataRole.UserRole, comp_id)
                self.component_list.addItem(item)

    def _on_component_selected(self, item):
        """选中组件后显示配置面板"""
        self._selected_component_id = item.data(Qt.ItemDataRole.UserRole)
        self._show_config_panel()

    def _show_config_panel(self):
        """显示选中组件的配置面板"""
        # 清空配置面板
        while self.config_panel_layout.count():
            item = self.config_panel_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()

        if not self._selected_component_id:
            self.config_header.hide()
            self.config_panel.hide()
            return

        # 获取组件数据
        home = self.edit_window.main_window.homeInterface
        if not home:
            return
        
        manager = home.component_manager
        comp_data = manager._component_data.get(self._selected_component_id)
        if not comp_data:
            return

        # 根据组件类型创建配置项
        config_items = self._get_config_items_for_type(self.component_type)
        
        for config_key, config_info in config_items.items():
            card = self._create_config_card(config_key, config_info, comp_data)
            if card:
                self.config_panel_layout.addWidget(card)

        self.config_header.show()
        self.config_panel.show()

    def _get_config_items_for_type(self, comp_type: str) -> dict:
        """获取组件类型的配置项定义"""
        # 基础配置项
        base_config = {
            "enabled": {"name": tr("component_edit.config_enabled"), "type": "switch"},
        }
        
        # 类型特定配置
        type_config = {
            "clock": {
                "show_seconds": {"name": tr("component_edit.config_show_seconds"), "type": "switch"},
                "show_lunar": {"name": tr("component_edit.config_show_lunar"), "type": "switch"},
                "font_size": {"name": tr("component_edit.config_font_size"), "type": "spinbox", "min": 10, "max": 200},
            },
            "weather": {
                "show_icon": {"name": tr("component_edit.config_show_icon"), "type": "switch"},
                "font_size": {"name": tr("component_edit.config_font_size"), "type": "spinbox", "min": 10, "max": 100},
            },
            "poetry": {
                "font_size": {"name": tr("component_edit.config_font_size"), "type": "spinbox", "min": 10, "max": 50},
            },
            "countdown": {
                "target_name": {"name": tr("component_edit.config_target_name"), "type": "lineedit"},
                "target_date": {"name": tr("component_edit.config_target_date"), "type": "lineedit"},
            },
            "school_info": {
                "school_name": {"name": tr("component_edit.config_school"), "type": "lineedit"},
                "class_name": {"name": tr("component_edit.config_class"), "type": "lineedit"},
            },
            "media": {
                "show_progress": {"name": tr("component_edit.config_show_progress"), "type": "switch"},
            },
            "quick_launch": {
                "icon_size": {"name": tr("component_edit.config_icon_size"), "type": "spinbox", "min": 32, "max": 128},
            },
        }
        
        return {**base_config, **type_config.get(comp_type, {})}

    def _create_config_card(self, config_key: str, config_info: dict, comp_data: dict) -> QWidget:
        """创建配置项卡片"""
        config_type = config_info.get("type", "switch")
        config_name = config_info.get("name", config_key)
        current_value = comp_data.get("config", {}).get(config_key)

        card = CardWidget(self.config_panel)
        card_layout = QHBoxLayout(card)
        card_layout.setContentsMargins(16, 12, 16, 12)

        # 配置名称
        name_label = BodyLabel(config_name)
        card_layout.addWidget(name_label)

        card_layout.addStretch()

        # 配置控件
        if config_type == "switch":
            switch = SwitchButton(self)
            switch.setChecked(bool(current_value) if current_value is not None else True)
            switch.checkedChanged.connect(lambda checked: self._update_config(config_key, checked))
            card_layout.addWidget(switch)
        elif config_type == "spinbox":
            spinbox = SpinBox(self)
            spinbox.setRange(config_info.get("min", 1), config_info.get("max", 100))
            spinbox.setValue(int(current_value) if current_value else config_info.get("min", 1))
            spinbox.valueChanged.connect(lambda value: self._update_config(config_key, value))
            card_layout.addWidget(spinbox)
        elif config_type == "lineedit":
            lineedit = QLineEdit(self)
            lineedit.setText(str(current_value) if current_value else "")
            lineedit.setFixedWidth(200)
            lineedit.textChanged.connect(lambda text: self._update_config(config_key, text))
            card_layout.addWidget(lineedit)

        return card

    def _update_config(self, config_key: str, value):
        """更新组件配置"""
        if not self._selected_component_id:
            return
        
        home = self.edit_window.main_window.homeInterface
        if not home:
            return
        
        manager = home.component_manager
        
        # 更新配置数据
        comp_data = manager._component_data.get(self._selected_component_id)
        if comp_data:
            if "config" not in comp_data:
                comp_data["config"] = {}
            comp_data["config"][config_key] = value
            
            manager.save_components()

    def _delete_selected_component(self):
        """删除选中的组件"""
        if not self._selected_component_id:
            return

        home = self.edit_window.main_window.homeInterface
        if not home:
            return

        manager = home.component_manager

        # 通过 manager 的方法删除，确保清理逻辑一致
        manager.remove_component(self._selected_component_id)

        self._selected_component_id = None
        self._refresh_component_list()
        self._show_config_panel()

    def _create_style_cards(self):
        for style_name, style_info in self.styles.items():
            card = StyleCardWidget(self.component_type, style_name, style_info, self)
            self._style_cards.append(card)

    def _show_current_card(self):
        # 清空容器
        while self.card_layout.count():
            item = self.card_layout.takeAt(0)
            if item.widget():
                item.widget().setParent(None)

        if self._style_cards:
            card = self._style_cards[self._current_index]
            card.setParent(self.card_container)
            self.card_layout.addWidget(card)

        # 更新箭头状态
        self.prev_btn.setEnabled(self._current_index > 0)
        self.next_btn.setEnabled(self._current_index < len(self._style_cards) - 1)

    def _prev_style(self):
        if self._current_index > 0:
            self._current_index -= 1
            self._show_current_card()

    def _next_style(self):
        if self._current_index < len(self._style_cards) - 1:
            self._current_index += 1
            self._show_current_card()

    def _add_component(self):
        if self._style_cards:
            style_keys = list(self.styles.keys())
            idx = min(self._current_index, len(style_keys) - 1)
            current_style = style_keys[idx]
            self.edit_window._on_add_component(self.component_type, current_style)


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

        self._type_pages = {}
        self._setup_pages()
        self._setup_navigation()
        self._setup_added_list()
        self._applyTheme()

        # 中间显示
        self._centerOnScreen()

    def _setup_pages(self):
        """创建各类型页面"""
        self._stack = QStackedWidget(self)

        type_icons = {
            "clock": FUI.DATE_TIME,
            "weather": FUI.CLOUD,
            "poetry": FUI.BOOK_SHELF,
            "countdown": FUI.STOP_WATCH,
            "school_info": FUI.EDUCATION,
            "media": FUI.MUSIC,
            "quick_launch": FUI.LINK,
        }

        type_names = {
            "clock": tr("component.clock"),
            "weather": tr("component.weather"),
            "poetry": tr("component.poetry"),
            "countdown": tr("component.countdown"),
            "school_info": tr("component.school_info"),
            "media": tr("component.media"),
            "quick_launch": tr("component.quick_launch"),
        }

        for type_name, styles in COMPONENT_STYLES.items():
            page = ComponentTypePage(type_name, styles, self)
            self._type_pages[type_name] = page
            self._stack.addWidget(page)

        self._type_icons = type_icons
        self._type_names = type_names

    def _setup_navigation(self):
        """设置导航栏"""
        for type_name, page in self._type_pages.items():
            icon = self._type_icons.get(type_name, FUI.SETTING)
            name = self._type_names.get(type_name, type_name)
            self.addSubInterface(page, icon, name)

        self.navigationInterface.expand()
        self.navigationInterface.setReturnButtonVisible(False)

    def _setup_added_list(self):
        """已添加组件列表"""
        # 暂时跳过，后续实现
        pass

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

    def _on_add_component(self, component_type: str, component_style: str):
        """添加组件回调"""
        from qfluentwidgets import InfoBar

        # 获取 HomeInterface
        home = None
        if hasattr(self.main_window, 'homeInterface'):
            home = self.main_window.homeInterface

        if home and hasattr(home, 'component_manager') and home.component_manager:
            comp_id = home.component_manager.add_component(component_type, component_style)
            if comp_id:
                InfoBar.success(
                    tr("component_edit.add_success"),
                    f"{self._type_names.get(component_type, component_type)}: {component_style}",
                    parent=self,
                    duration=2000
                )
        else:
            # 组件系统尚未完全整合
            InfoBar.warning(
                tr("component_edit.feature_pending"),
                tr("component_edit.feature_pending_desc"),
                parent=self,
                duration=3000
            )

    def closeEvent(self, event):
        """释放资源"""
        event.accept()


class GridCanvas(QWidget):
    """网格桌面画布 - 管理组件放置和交互"""

    # 信号
    component_placed = pyqtSignal(str)       # placement_id
    component_removed = pyqtSignal(str)      # placement_id
    edit_mode_changed = pyqtSignal(bool)

    def __init__(self, main_window, parent=None):
        super().__init__(parent)
        self.main_window = main_window
        self.setObjectName("gridCanvas")

        from core.component import (
            GridLayoutService, ComponentRegistry, PlacementService,
            BUILTIN_COMPONENT_DEFINITIONS, EditSession, GridMetrics
        )
        self.grid_service = GridLayoutService()
        self.registry = ComponentRegistry(self)
        self.placement_srv = PlacementService(self)

        # 加载内置组件定义
        self.registry.register_batch(BUILTIN_COMPONENT_DEFINITIONS)

        # 加载放置配置
        self.placement_srv.load()

        # 网格计算结果
        self._metrics: Optional[GridMetrics] = None

        # 编辑状态
        self._edit_session = EditSession()
        self._is_edit_mode = False

        # 预览框
        self._preview_box = DragPreviewBox(self)

        # 组件容器
        self._component_widgets: Dict[str, QWidget] = {}

        # UI 设置
        self.setAcceptDrops(True)
        self._setup_ui()

        # 尺寸计算
        self._update_grid_metrics()

        # 创建加了的组件
        self._create_placed_components()

        self.placement_srv.placement_added.connect(self._on_placement_added)
        self.placement_srv.placement_removed.connect(self._on_placement_removed)
        self.placement_srv.placement_updated.connect(self._on_placement_updated)

        self.setStyleSheet(load_qss('home.qss'))

    def _setup_ui(self):
        """设置布局"""
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

    def _update_grid_metrics(self):
        """更新网格尺寸"""
        from core.component import GridMetrics
        settings = self.placement_srv.get_grid_settings()
        self._metrics = self.grid_service.calculate_grid_metrics(
            self.width(), self.height(), settings
        )

    def resizeEvent(self, event):
        """窗口大小变化时重新计算网格"""
        super().resizeEvent(event)
        self._update_grid_metrics()

        # 更新所有组件位置
        for placement_id, widget in self._component_widgets.items():
            placement = self.placement_srv.get_placement(placement_id)
            if placement and self._metrics:
                rect = self.grid_service.get_cell_rect(
                    self._metrics,
                    placement.column,
                    placement.row,
                    placement.width_cells,
                    placement.height_cells
                )
                widget.setGeometry(rect)

    def _create_placed_components(self):
        """创建已放置的组件"""
        for placement in self.placement_srv.get_all_placements():
            self._create_component_widget(placement)

    def _create_component_widget(self, placement) -> Optional[QWidget]:
        """创建单个组件"""
        from core.component import ComponentPlacement
        definition = self.registry.get_definition(placement.component_id)
        if not definition:
            logger.warning(f"[GridCanvas] 组件定义不存在: {placement.component_id}")
            return None

        # 获取组件类
        comp_class = definition.component_class
        if not comp_class:
            # 从 COMPONENT_STYLES 获取 - 支持多词类型 (school_info, quick_launch_dock)
            comp_id = placement.component_id
            for type_name in COMPONENT_STYLES:
                if comp_id.startswith(type_name + "_"):
                    style_name = comp_id[len(type_name) + 1:]
                    style_info = COMPONENT_STYLES[type_name].get(style_name, {})
                    comp_class = style_info.get('class')
                    break
                elif comp_id == type_name:
                    first_style = next(iter(COMPONENT_STYLES[type_name]))
                    style_info = COMPONENT_STYLES[type_name][first_style]
                    comp_class = style_info.get('class')
                    break

        if not comp_class:
            logger.warning(f"[GridCanvas] 组件类不存在: {placement.component_id}")
            return None

        # 计算位置和大小
        if self._metrics:
            rect = self.grid_service.get_cell_rect(
                self._metrics,
                placement.column,
                placement.row,
                placement.width_cells,
                placement.height_cells
            )
        else:
            rect = QRect(100, 100, 200, 200)

        # 构造函数期望 component_data: dict
        try:
            component_data = {"id": placement.component_id, **placement.config}
            widget = comp_class(self, component_data)
            widget.setGeometry(rect)
            widget.show()

            self._component_widgets[placement.placement_id] = widget
            self.placement_srv.register_widget(placement.placement_id, widget)

            logger.info(f"[GridCanvas] 创建组件: {placement.placement_id}")
            return widget
        except Exception as e:
            logger.error(f"[GridCanvas] 创建组件失败: {e}")
            return None

    def _on_placement_added(self, placement_id: str):
        """放置添加时创建组件"""
        placement = self.placement_srv.get_placement(placement_id)
        if placement:
            self._create_component_widget(placement)
        self.component_placed.emit(placement_id)

    def _on_placement_removed(self, placement_id: str):
        """放置移除时删除组件"""
        if placement_id in self._component_widgets:
            widget = self._component_widgets[placement_id]
            widget.deleteLater()
            del self._component_widgets[placement_id]
        self.component_removed.emit(placement_id)

    def _on_placement_updated(self, placement_id: str):
        """放置更新时移动/调整组件"""
        placement = self.placement_srv.get_placement(placement_id)
        widget = self._component_widgets.get(placement_id)

        if placement and widget and self._metrics:
            rect = self.grid_service.get_cell_rect(
                self._metrics,
                placement.column,
                placement.row,
                placement.width_cells,
                placement.height_cells
            )
            widget.setGeometry(rect)


    # 拖拽处理

    def dragEnterEvent(self, event):
        """拖拽进入"""
        from core.component import EditSessionMode, EditSession
        mime = event.mimeData()
        if mime.hasFormat("application/x-classlively-component"):
            event.acceptProposedAction()

            # 编辑会话
            data = bytes(mime.data("application/x-classlively-component")).decode('utf-8')
            parts = data.split('|')
            if len(parts) >= 2:
                comp_id = parts[0]
                # 组件定义
                definition = self.registry.get_definition(comp_id)
                if definition:
                    self._edit_session = EditSession(
                        mode=EditSessionMode.DRAGGING_NEW,
                        component_id=comp_id,
                        width_cells=definition.default_width_cells,
                        height_cells=definition.default_height_cells,
                        start_position=event.position().toPoint(),
                        current_position=event.position().toPoint(),
                    )
                    self._show_preview()
        else:
            event.ignore()

    def dragMoveEvent(self, event):
        """拖拽移动"""
        if self._edit_session.is_dragging_new:
            pos = event.position().toPoint()
            self._edit_session.current_position = pos

            # 计算目标格子
            if self._metrics:
                row, col = self.grid_service.point_to_cell(self._metrics, pos)
                self._edit_session.target_row = row
                self._edit_session.target_column = col

                # 检查碰撞
                is_collision = self.grid_service.check_collision(
                    self.placement_srv.get_all_placements(),
                    row, col,
                    self._edit_session.width_cells,
                    self._edit_session.height_cells,
                )

                self._update_preview_position()
                self._preview_box.set_collision(is_collision)

            event.acceptProposedAction()

    def dragLeaveEvent(self, event):
        """拖拽离开"""
        from core.component import EditSession
        self._hide_preview()
        self._edit_session = EditSession()

    def dropEvent(self, event):
        """放置"""
        from core.component import EditSession
        if self._edit_session.is_dragging_new and self._edit_session.has_target:
            # 检查碰撞
            is_collision = self.grid_service.check_collision(
                self.placement_srv.get_all_placements(),
                self._edit_session.target_row,
                self._edit_session.target_column,
                self._edit_session.width_cells,
                self._edit_session.height_cells,
            )

            if not is_collision:
                # 创建放置
                placement_id = self.placement_srv.add_placement(
                    self._edit_session.component_id,
                    self._edit_session.target_row,
                    self._edit_session.target_column,
                    self._edit_session.width_cells,
                    self._edit_session.height_cells,
                )
                logger.info(f"[GridCanvas] 放置组件: {placement_id}")

        self._hide_preview()
        self._edit_session = EditSession()
        event.acceptProposedAction()

    def _show_preview(self):
        """显示预览框"""
        if self._metrics and self._edit_session.is_dragging_new:
            rect = self.grid_service.get_cell_rect(
                self._metrics,
                self._edit_session.target_column,
                self._edit_session.target_row,
                self._edit_session.width_cells,
                self._edit_session.height_cells,
            )
            self._preview_box.setGeometry(rect)
            self._preview_box.show()
            self._preview_box.raise_()

    def _update_preview_position(self):
        """更新预览框位置"""
        if self._metrics and self._edit_session.is_dragging_new:
            rect = self.grid_service.get_cell_rect(
                self._metrics,
                self._edit_session.target_column,
                self._edit_session.target_row,
                self._edit_session.width_cells,
                self._edit_session.height_cells,
            )
            self._preview_box.setGeometry(rect)

    def _hide_preview(self):
        """隐藏预览框"""
        self._preview_box.hide()

    # 编辑模式

    def enter_edit_mode(self):
        """进入编辑模式"""
        self._is_edit_mode = True
        self.edit_mode_changed.emit(True)

    def exit_edit_mode(self):
        """退出编辑模式"""
        self._is_edit_mode = False
        self.edit_mode_changed.emit(False)

    def is_edit_mode(self) -> bool:
        return self._is_edit_mode

    # 绘制网格

    def paintEvent(self, event):
        """绘制"""
        super().paintEvent(event)

        if self._is_edit_mode and self._metrics:
            painter = QPainter(self)
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)

            # 绘制网格线
            grid_color = QColor(200, 200, 200, 220) if not isDarkTheme() else QColor(255, 255, 255, 30)
            pen = QPen(grid_color, 1)
            painter.setPen(pen)

            # 垂直线
            for col in range(self._metrics.column_count + 1):
                x = self._metrics.edge_inset_px + col * self._metrics.pitch
                painter.drawLine(int(x), int(self._metrics.edge_inset_px),
                                int(x), int(self._metrics.edge_inset_px + self._metrics.grid_height_px))

            # 水平线
            for row in range(self._metrics.row_count + 1):
                y = self._metrics.edge_inset_px + row * self._metrics.pitch
                painter.drawLine(int(self._metrics.edge_inset_px), int(y),
                                int(self._metrics.edge_inset_px + self._metrics.grid_width_px), int(y))

    # 公共 API

    def get_registry(self):
        """获取组件注册中心"""
        return self.registry

    def get_placement_service(self):
        """获取放置服务"""
        return self.placement_srv

    def get_grid_metrics(self):
        """获取网格尺寸"""
        return self._metrics

    def get_all_components(self) -> Dict[str, QWidget]:
        """获取所有组件"""
        return self._component_widgets.copy()
