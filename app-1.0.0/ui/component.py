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

"""
UI组件模块
"""

import ctypes
import json
import logging
import os
import re
import shutil
import sys
import time
import datetime
import webbrowser
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from typing import Optional, Dict
import threading
from collections import deque

from comtypes import GUID
from ctypes import wintypes

import pythoncom

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
    QMimeData,
    QEvent,
)
from PyQt6.QtGui import (
    QBrush,
    QColor,
    QCursor,
    QFont,
    QFontMetrics,
    QIcon,
    QImageReader,
    QLinearGradient,
    QPainter,
    QPainterPath,
    QPen,
    QPixmap,
    QDrag,
)
from PyQt6.QtSvg import QSvgRenderer
from PyQt6.QtWidgets import (
    QFileIconProvider, QGridLayout, QLabel, QSizePolicy, QWidget, QVBoxLayout, QHBoxLayout, QApplication, QProgressBar, QGraphicsOpacityEffect,
    QScrollArea, QStackedWidget, QPushButton, QListWidget, QListWidgetItem, QLineEdit, QSlider, QFileDialog, QTextEdit
)
from qfluentwidgets import InfoBar, isDarkTheme, RoundMenu, Action, FluentWindow, setTheme, ScrollArea, PushButton, ToolButton, TransparentToolButton, StrongBodyLabel, CardWidget, BodyLabel, ComboBox, SpinBox, SwitchButton, HorizontalFlipView, VerticalFlipView, PrimaryPushButton, Pivot, MessageBoxBase, ProgressBar, LineEdit, ColorPickerButton
from win32com.shell import shell

from core.config import cfg, save_cfg
from core.utils import tr, FUI, get_cached_content, save_cache
from services.media import MediaInfo, Lyrics, get_media_info, fetch_all_info, close as close_media
from services.news import NewsService
from core.constants import BASE_DIR, APP_DIR, DATA_CONFIG, DATA_CLASSPHOTOS, DATA_NOTES, load_qss, NEWS_ICONS, get_resPath
from resource.software_list import get_software_icon_path
from core.component import (
    ComponentDefinition,
    ComponentRegistry,
)

logger = logging.getLogger("Glimpseon.ui.component")


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
            "name": "班级卡片",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
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
    "news": {
        "baidu": {
            "name": "百度热搜",
            "class": None,
            "default_config": {},
            "default_size": (360, 220),
        },
        "weibo": {
            "name": "微博热搜",
            "class": None,
            "default_config": {},
            "default_size": (360, 220),
        },
        "jinritoutiao": {
            "name": "今日头条",
            "class": None,
            "default_config": {},
            "default_size": (360, 220),
        },
        "tenxunwang": {
            "name": "腾讯网",
            "class": None,
            "default_config": {},
            "default_size": (360, 220),
        },
        "xcvts": {
            "name": "央视新闻",
            "class": None,
            "default_config": {},
            "default_size": (360, 220),
        },
    },
    "linkage": {
        "timetable_preview": {
            "name": "今日课表",
            "class": None,
            "default_config": {},
            "default_size": (300, 550),
        },
        "timetable_nowlesson": {
            "name": "当前课程",
            "class": None,
            "default_config": {
                "show_teacher": True,
                "show_next": True,
                "show_duration": True,
                "show_countdown": True,
                "prepare_minutes": 3,
            },
            "default_size": (400, 200),
        },
    },
    "Math": {
        "calculator": {
            "name": "计算器",
            "class": None,
            "default_config": {},
            "default_size": (280, 420),
        },
    },
    "writing": {
        "pad": {
            "name": "书写板",
            "class": None,
            "default_config": {},
            "default_size": (400, 100),
        },
    },
    "class_album": {
        "horizontal": {
            "name": "横向相册",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
        },
        "vertical": {
            "name": "纵向相册",
            "class": None,
            "default_config": {},
            "default_size": (200, 400),
        },
    },
    "sticky_note": {
        "default": {
            "name": "便签",
            "class": None,
            "default_config": {"color": "yellow"},
            "default_size": (280, 280),
        },
    },
    "timer": {
        "countdown": {
            "name": "计时与倒计时",
            "class": None,
            "default_config": {},
            "default_size": (360, 320),
        },
    },
}


COMPONENTS_CONFIG_PATH = os.path.join(DATA_CONFIG, "components.json")


class ComponentManager:
    """组件管理器"""

    MAX_COMPONENTS = 100

    def __init__(self, home_interface):
        self.home = home_interface
        self.components = {}  # id: DraggableContainer 实例
        self._component_data = {}  # id: 原始配置数据

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

            data["components"].append(comp_data)

        try:
            with open(COMPONENTS_CONFIG_PATH, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2, ensure_ascii=False)
            logger.info(f"保存组件配置到: {COMPONENTS_CONFIG_PATH}")
        except Exception as e:
            logger.error(f"保存组件配置失败: {e}")

    def add_component(self, comp_type: str, comp_style: str, config=None) -> str:
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
        }

        comp_class = style_info["class"]
        try:
            instance = comp_class(self.home, comp_data)
            instance.resize(*default_size)
            instance._size_explicitly_set = True
            instance.show()
            # show() 触发 showEvent 调 adjustSize() 可能改变尺寸
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

    def update_component_config(self, comp_id: str, config: dict):
        """更新组件配置"""
        if comp_id not in self._component_data:
            logger.warning(f"组件不存在: {comp_id}")
            return

        self._component_data[comp_id]["config"] = config
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
        self._selected = False
        self._resizing = False
        self._resize_start_pos = QPoint()
        self._resize_start_size = QSize()
        self._show_border = False
        self._hovered = False
        self._cached_primary_color = QColor(48, 195, 97)

        self.setMouseTracking(True)
        self.setAutoFillBackground(False)
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Preferred)
    
    def setDraggable(self, enabled: bool):
        self._draggable = enabled
        if enabled:
            self._set_children_mouse_transparent(True)
        else:
            self._set_children_mouse_transparent(False)
        self._show_border = enabled
        self.update()
        if enabled:
            self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            self.raise_()
        else:
            self.setCursor(QCursor(Qt.CursorShape.ArrowCursor))

    def _set_children_mouse_transparent(self, transparent: bool):
        for child in self.findChildren(QWidget):
            try:
                child.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, transparent)
            except RuntimeError:
                pass  # 已销毁的子部件

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
                self._saved_min_size = self.minimumSize()
                self.setMinimumSize(1, 1)
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
            event.accept()
            return
        super().mouseDoubleClickEvent(event)
    
    def mouseMoveEvent(self, event):
        if self._resizing and self._draggable:
            delta = event.globalPosition().toPoint() - self._resize_start_pos
            orig_w = self._resize_start_size.width()
            orig_h = self._resize_start_size.height()
            # 等比缩放
            new_w = max(40, orig_w + delta.x())
            new_h = max(40, orig_h + delta.y())
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
    
    def _save_position(self):
        """保存当前位置到配置"""
        main_win = self._getMainWindow()
        if main_win and hasattr(main_win, 'homeInterface') and main_win.homeInterface:
            mgr = getattr(main_win.homeInterface, 'component_manager', None)
            if mgr:
                mgr.save_components()

    def mouseReleaseEvent(self, event):
        if event.button() != Qt.MouseButton.LeftButton:
            super().mouseReleaseEvent(event)
            return

        # 缩放结束
        if self._resizing:
            self._resizing = False
            if hasattr(self, '_saved_min_size'):
                self.setMinimumSize(self._saved_min_size)
                del self._saved_min_size
            self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            self._save_position()
            event.accept()
            return

        # 拖动结束
        if self._dragging:
            self._dragging = False
            self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor if self._draggable else Qt.CursorShape.ArrowCursor))

        self.update()

        main_window = self._getMainWindow()
        if main_window and hasattr(main_window, 'clearDragAlignLines'):
            main_window.clearDragAlignLines()

        # 点击检测（未发生明显拖动则视为点击选中）
        if self._draggable and hasattr(self, '_click_start_pos'):
            delta = event.globalPosition().toPoint() - self._click_start_pos
            if abs(delta.x()) < 5 and abs(delta.y()) < 5:
                self.selected.emit(self.component_id)
                event.accept()
                return

        # 保存位置
        self._percent_x, self._percent_y = self._calculatePercentFromPosition()
        self._save_position()
        event.accept()
    
    def onParentResize(self):
        self._updatePositionFromPercent()


def _create_edit_controls(parent_widget, component_widget, on_delete_clicked, on_config_clicked):
    """创建编辑控件"""
    from core.config import cfg

    btn_size = 48
    icon_size = 22
    gap = 8

    def _build_btn(icon, on_clicked):
        btn = ToolButton(icon, parent_widget)
        btn.setFixedSize(btn_size, btn_size)
        btn.setIconSize(QSize(icon_size, icon_size))
        btn.setCursor(QCursor(Qt.CursorShape.ArrowCursor))
        btn.hide()
        btn.clicked.connect(on_clicked)
        return btn

    config_btn = _build_btn(FUI.SETTING, on_config_clicked)
    delete_btn = _build_btn(FUI.DELETE, on_delete_clicked)

    def _btn_style(btn, hover_rgb):
        opacity = cfg.componentCardOpacity.value / 100.0
        radius = cfg.componentCardRadius.value
        if isDarkTheme():
            c = QColor(40, 40, 40)
            border_c = "rgba(255,255,255,0.10)"
        else:
            c = QColor(255, 255, 255)
            border_c = "rgba(0,0,0,0.08)"
        c.setAlpha(int(255 * opacity))
        hr, hg, hb = hover_rgb
        btn.setStyleSheet(f"""
            ToolButton {{
                background-color: rgba({c.red()}, {c.green()}, {c.blue()}, {c.alpha() / 255:.2f});
                border-radius: {radius}px;
                border: 1px solid {border_c};
            }}
            ToolButton:hover {{
                background-color: rgba({hr}, {hg}, {hb}, 0.85);
                border: 1px solid rgba({hr}, {hg}, {hb}, 0.9);
            }}
        """)

    # 配置按钮 hover 蓝色 删除按钮 hover 红色
    _CONFIG_HOVER = (0, 120, 212)
    _DELETE_HOVER = (220, 80, 80)

    def _apply_config_style():
        _btn_style(config_btn, _CONFIG_HOVER)

    def _apply_delete_style():
        _btn_style(delete_btn, _DELETE_HOVER)

    _apply_config_style()
    _apply_delete_style()

    def _reposition():
        comp_pos = component_widget.mapTo(parent_widget, QPoint(0, 0))
        # 右下角
        del_x = comp_pos.x() + component_widget.width() - delete_btn.width() + 4
        del_y = comp_pos.y() + component_widget.height() + 4
        delete_btn.move(del_x, del_y)
        # 左边
        config_btn.move(del_x - config_btn.width() - gap, del_y)

    config_btn.reposition = _reposition
    delete_btn.reposition = _reposition

    def _apply_style():
        _apply_config_style()
        _apply_delete_style()

    config_btn.apply_style = _apply_style
    delete_btn.apply_style = _apply_style

    return config_btn, delete_btn


class ComponentConfigDialog(MessageBoxBase):
    """组件配置面板"""

    def __init__(self, component_widget, component_id, comp_data, home_interface):
        main_window = None
        if home_interface:
            main_window = home_interface.window()
        if main_window is None:
            main_window = component_widget.window() if component_widget else None
        super().__init__(main_window)

        self._component_widget = component_widget
        self._component_id = component_id
        self._comp_data = comp_data or {}
        self._home = home_interface
        self._config = dict(self._comp_data.get("config", {}))
        comp_type = self._comp_data.get("type", "")
        comp_style = self._comp_data.get("style", "")
        self._comp_key = f"{comp_type}|{comp_style}"
        self._fields = []

        self._init_ui()
        self._load_config()

    def _init_ui(self):
        self.setWindowTitle(tr("component_edit.config_title"))
        self.widget.setMinimumSize(520, 480)

        self._pivot = Pivot(self)
        self._stack = QStackedWidget(self)

        # 基础设置
        self._basic_page = ScrollArea()
        self._basic_page.setWidgetResizable(True)
        self._basic_page.setStyleSheet("background: transparent; border: none;")
        basic_content = QWidget()
        self._build_basic_page(basic_content)
        self._basic_page.setWidget(basic_content)
        self._stack.addWidget(self._basic_page)
        self._pivot.addItem(
            "basic", tr("component_edit.config_basic"),
            onClick=lambda: self._switch_page(0),
        )

        # 进阶设置
        self._advanced_page = ScrollArea()
        self._advanced_page.setWidgetResizable(True)
        self._advanced_page.setStyleSheet("background: transparent; border: none;")
        advanced_content = QWidget()
        self._build_advanced_page(advanced_content)
        self._advanced_page.setWidget(advanced_content)
        self._stack.addWidget(self._advanced_page)
        self._pivot.addItem(
            "advanced", tr("component_edit.config_advanced"),
            onClick=lambda: self._switch_page(1),
        )

        layout = self.viewLayout
        layout.setContentsMargins(24, 8, 24, 8)
        layout.setSpacing(12)
        layout.addWidget(self._pivot)
        layout.addWidget(self._stack)

        self.yesButton.setText(tr("component_edit.config_save"))
        self.cancelButton.setText(tr("component_edit.config_cancel"))

        # 默认基础设置
        self._pivot.setCurrentItem("basic")
        self._stack.setCurrentIndex(0)

    def _switch_page(self, idx):
        self._stack.setCurrentIndex(idx)

    # 配置字段
    class _Field:
        """配置字段基类"""
        def __init__(self, key, label_text, default):
            self.key = key
            self.label_text = label_text
            self.default = default

        def build(self, dialog) -> object:
            """创建并返回行布局"""
            raise NotImplementedError

        def load(self, config: dict):
            """从配置字典加载值"""
            raise NotImplementedError

        def save(self, result: dict):
            """从控件读取值写入结果字典"""
            raise NotImplementedError

    class _SwitchField(_Field):
        """开关字段"""
        def __init__(self, key, label_text, default):
            super().__init__(key, label_text, default)
            self._widget = None

        def build(self, dialog):
            row = QHBoxLayout()
            row.setContentsMargins(0, 0, 0, 0)
            row.addWidget(BodyLabel(self.label_text, dialog))
            row.addStretch()
            self._widget = SwitchButton(dialog)
            row.addWidget(self._widget)
            return row

        def load(self, config):
            self._widget.setChecked(bool(config.get(self.key, self.default)))

        def save(self, result):
            result[self.key] = self._widget.isChecked()

    class _SpinField(_Field):
        """数值输入字段"""
        def __init__(self, key, label_text, default, min_v, max_v):
            super().__init__(key, label_text, default)
            self._min = min_v
            self._max = max_v
            self._widget = None

        def build(self, dialog):
            row = QHBoxLayout()
            row.setContentsMargins(0, 0, 0, 0)
            row.addWidget(BodyLabel(self.label_text, dialog))
            row.addStretch()
            self._widget = SpinBox(dialog)
            self._widget.setRange(self._min, self._max)
            self._widget.setValue(self.default)
            self._widget.setFixedWidth(200)
            row.addWidget(self._widget)
            return row

        def load(self, config):
            self._widget.setValue(int(config.get(self.key, self.default)))

        def save(self, result):
            result[self.key] = self._widget.value()

    class _TextField(_Field):
        """多行文本字段"""
        def __init__(self, key, label_text, default, placeholder=""):
            super().__init__(key, label_text, default)
            self._placeholder = placeholder
            self._widget = None

        def build(self, dialog):
            row = QVBoxLayout()
            row.setContentsMargins(0, 0, 0, 0)
            row.setSpacing(4)
            row.addWidget(BodyLabel(self.label_text, dialog))
            self._widget = QLineEdit(dialog)
            self._widget.setPlaceholderText(self._placeholder)
            row.addWidget(self._widget)
            return row

        def load(self, config):
            self._widget.setText(str(config.get(self.key, self.default)))

        def save(self, result):
            result[self.key] = self._widget.text()

    class _TextRowField(_Field):
        """单行文本字段"""
        def __init__(self, key, label_text, default):
            super().__init__(key, label_text, default)
            self._widget = None

        def build(self, dialog):
            row = QHBoxLayout()
            row.setContentsMargins(0, 0, 0, 0)
            lbl = BodyLabel(self.label_text, dialog)
            lbl.setFixedWidth(80)
            self._widget = LineEdit(dialog)
            self._widget.setText(str(self.default))
            self._widget.setMinimumWidth(200)
            row.addWidget(lbl)
            row.addWidget(self._widget, 1)
            return row

        def load(self, config):
            self._widget.setText(str(config.get(self.key, self.default)))

        def save(self, result):
            result[self.key] = self._widget.text()

    class _SliderField(_Field):
        """滑块字段"""
        def __init__(self, key, label_text, default, min_v, max_v, suffix=""):
            super().__init__(key, label_text, default)
            self._min = min_v
            self._max = max_v
            self._suffix = suffix
            self._widget = None
            self._val_lbl = None

        def build(self, dialog):
            row = QVBoxLayout()
            row.setContentsMargins(0, 0, 0, 0)
            row.setSpacing(4)
            top = QHBoxLayout()
            top.setContentsMargins(0, 0, 0, 0)
            top.addWidget(BodyLabel(self.label_text, dialog))
            top.addStretch()
            self._val_lbl = BodyLabel(f"{self.default}{self._suffix}", dialog)
            self._val_lbl.setFixedWidth(60)
            self._val_lbl.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
            top.addWidget(self._val_lbl)
            row.addLayout(top)
            self._widget = QSlider(Qt.Orientation.Horizontal, dialog)
            self._widget.setRange(self._min, self._max)
            self._widget.setValue(self.default)
            self._widget.valueChanged.connect(lambda v: self._val_lbl.setText(f"{v}{self._suffix}"))
            row.addWidget(self._widget)
            return row

        def load(self, config):
            self._widget.setValue(int(config.get(self.key, self.default)))

        def save(self, result):
            result[self.key] = self._widget.value()

    class _ColorField(_Field):
        """颜色字段"""
        def __init__(self, key, label_text, default_mode, default_color):
            super().__init__(key, label_text, default_mode)
            self._default_color = default_color
            self._combo = None
            self._picker = None

        def build(self, dialog):
            row = QHBoxLayout()
            row.setContentsMargins(0, 0, 0, 0)
            lbl = BodyLabel(self.label_text, dialog)
            lbl.setFixedWidth(80)
            self._combo = ComboBox(dialog)
            self._combo.addItems([tr("component_edit.bg_mode_opacity"), tr("component_edit.bg_mode_custom")])
            self._combo.setFixedWidth(130)
            self._combo.setCurrentIndex(1 if self.default == "custom" else 0)
            self._picker = ColorPickerButton(QColor(self._default_color), "", dialog)
            self._picker.setFixedWidth(60)
            self._combo.currentIndexChanged.connect(lambda idx: self._picker.setVisible(idx == 1))
            self._picker.setVisible(self._combo.currentIndex() == 1)
            row.addWidget(lbl)
            row.addWidget(self._combo)
            row.addWidget(self._picker)
            row.addStretch()
            return row

        def load(self, config):
            mode = config.get(self.key + "_mode", self.default)
            self._combo.setCurrentIndex(1 if mode == "custom" else 0)
            color = config.get(self.key + "_color", self._default_color)
            self._picker.setColor(QColor(str(color)))

        def save(self, result):
            result[self.key + "_mode"] = "custom" if self._combo.currentIndex() == 1 else "opacity"
            c = self._picker.color()
            result[self.key + "_color"] = c.name() if c.isValid() else "#ffffff"

    # 组件配置注册表
    # key 对应返回 [(分组标题, [字段实例, ...]), ...] 的函数。
    def _basic_defs(self):
        """返回当前组件的基础配置定义。"""
        defs = {
            "linkage|timetable_nowlesson": lambda: [
                (tr("component_edit.group_content"), [
                    self._SwitchField("show_teacher", tr("component_edit.config_show_teacher"), True),
                    self._SwitchField("show_next", tr("component_edit.config_show_next"), True),
                    self._SwitchField("show_duration", tr("component_edit.config_show_duration"), True),
                ]),
                (tr("component_edit.group_countdown"), [
                    self._SwitchField("show_countdown", tr("component_edit.config_show_countdown"), True),
                    self._SpinField("prepare_minutes", tr("component_edit.config_prepare_minutes"), 3, 1, 10),
                ]),
            ],
            "clock|digital": lambda: [
                (tr("component_edit.group_display"), [
                    self._SwitchField("show_seconds", tr("component_edit.config_show_seconds"), True),
                    self._SwitchField("show_lunar", tr("component_edit.config_show_lunar"), True),
                ]),
            ],
            "countdown|event": lambda: [
                (tr("component_edit.group_target"), [
                    self._TextField("target_name", tr("component_edit.config_target_name"), ""),
                    self._TextField("target_date", tr("component_edit.config_target_date"), "", "YYYY-MM-DD"),
                ]),
            ],
            "school_info|class_info": lambda: [
                (tr("component_edit.group_info"), [
                    self._TextRowField("class", tr("component_edit.config_class"), ""),
                    self._TextRowField("school", tr("component_edit.config_school"), ""),
                    self._TextRowField("count", tr("component_edit.config_count"), ""),
                    self._TextRowField("slogan", tr("component_edit.config_slogan"), ""),
                ]),
            ],
            "weather|icon_temp": lambda: [
                (tr("component_edit.group_display"), [
                    self._SwitchField("show_icon", tr("component_edit.config_show_icon"), True),
                ]),
            ],
            "media|player": lambda: [
                (tr("component_edit.group_display"), [
                    self._SwitchField("show_progress", tr("component_edit.config_show_progress"), True),
                ]),
            ],
            "quick_launch|dock": lambda: [
                (tr("component_edit.group_display"), [
                    self._SpinField("icon_size", tr("component_edit.config_icon_size"), 64, 24, 96),
                ]),
            ],
        }
        return defs.get(self._comp_key)

    def _advanced_defs(self):
        """返回当前组件的进阶配置定义。未单独定义的组件使用默认配置。"""
        defs = {
            "school_info|class_info": lambda: [
                # 外观：不透明度 + 圆角 + 字号缩放
                ("", [
                    self._SliderField("bg_opacity", tr("component_edit.config_bg_opacity"), 55, 0, 100, "%"),
                    self._SliderField("corner_radius", tr("component_edit.config_corner_radius"), 16, 0, 29, "px"),
                    self._SliderField("font_scale", tr("component_edit.config_font_scale"), 100, 50, 200, "%"),
                ]),
                # 字号
                (tr("component_edit.group_font_size"), [
                    self._SpinField("class_size", tr("component_edit.config_class_size"), 48, 12, 80),
                    self._SpinField("school_size", tr("component_edit.config_school_size"), 25, 10, 50),
                    self._SpinField("count_size", tr("component_edit.config_count_size"), 19, 8, 30),
                    self._SpinField("slogan_size", tr("component_edit.config_slogan_size"), 23, 10, 40),
                ]),
                # 背景颜色：主背景 + 上层背景
                (tr("component_edit.group_bg_color"), [
                    self._ColorField("main_bg", tr("component_edit.config_main_bg"), "opacity", "#ffffff"),
                    self._ColorField("top_bg", tr("component_edit.config_top_bg"), "opacity", "#ffffff"),
                ]),
            ],
        }
        default = lambda: [
            (tr("component_edit.group_appearance"), [
                self._SliderField("bg_opacity", tr("component_edit.config_bg_opacity"), 55, 0, 100, "%"),
                self._SliderField("corner_radius", tr("component_edit.config_corner_radius"), 16, 0, 29, "px"),
            ]),
            (tr("component_edit.group_font"), [
                self._SliderField("font_scale", tr("component_edit.config_font_scale"), 100, 50, 200, "%"),
            ]),
        ]
        return defs.get(self._comp_key, default)

    # 页面构建

    def _build_basic_page(self, page):
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 8, 0, 0)
        layout.setSpacing(14)
        groups = self._basic_defs()
        if groups:
            for title, fields in groups():
                self._add_group(layout, title, fields)
        else:
            label = BodyLabel(tr("component_edit.feature_pending_desc"), page)
            label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            layout.addWidget(label)
        layout.addStretch()

    def _build_advanced_page(self, page):
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 8, 0, 0)
        layout.setSpacing(14)
        for title, fields in self._advanced_defs()():
            self._add_group(layout, title, fields)
        layout.addStretch()

    def _add_group(self, layout, title, fields):
        """添加一个配置分组"""
        # 标题可选 例如班级卡片那个外观配置
        group_box = QWidget()
        group_box.setStyleSheet("QWidget { background: transparent; }")
        group_layout = QVBoxLayout(group_box)
        group_layout.setContentsMargins(0, 0, 0, 0)
        group_layout.setSpacing(8)
        if title:
            group_layout.addWidget(StrongBodyLabel(title, self))
        content = QWidget()
        content.setStyleSheet("QWidget { background: transparent; }")
        content_layout = QVBoxLayout(content)
        content_layout.setContentsMargins(12, 4, 0, 4)
        content_layout.setSpacing(10)
        for field in fields:
            content_layout.addLayout(field.build(self))
            self._fields.append(field)
        group_layout.addWidget(content)
        layout.addWidget(group_box)

    # 配置读写

    def _load_config(self):
        for field in self._fields:
            field.load(self._config)

    def get_config(self) -> dict:
        result = dict(self._config)
        for field in self._fields:
            field.save(result)
        return result


class DraggableContainer(DraggableWidget):

    def __init__(self, parent=None, component_id: str = "", layout_direction: str = "vertical"):
        super().__init__(parent, component_id)

        self._content_visible = True
        self._delete_button = None
        self._config_button = None
        # _scale_factor 由 resizeEvent 根据当前尺寸 / natural_size 计算，
        # 子类在样式方法中用 self._scaled_px(base) 缩放字体/图标，apply_scale 重应用样式
        self._scale_factor = 1.0
        self._natural_size = None
        self._size_explicitly_set = False
        self._applying_scale = False
        # 卡片背景标准配置 11**14年了终于统一背景了。。。
        self._bg_opacity = None
        self._corner_radius = None
        self._bg_mode = "opacity"
        self._bg_color = "#ffffff"

        if layout_direction == "vertical":
            self.inner_layout = QVBoxLayout(self)
        else:
            self.inner_layout = QHBoxLayout(self)

        self.inner_layout.setContentsMargins(10, 10, 10, 10)
        self.inner_layout.setSpacing(5)
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Preferred)
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)

        self._resize_debounce_timer = QTimer(self)
        self._resize_debounce_timer.setSingleShot(True)
        self._resize_debounce_timer.timeout.connect(self._on_resize_debounce)
        """下面这些到pass 都是更新样式相关"""
        cfg.componentCardOpacity.valueChanged.connect(self._on_card_config_changed)
        cfg.componentCardRadius.valueChanged.connect(self._on_card_config_changed)

        cfg.themeChanged.connect(self._on_card_config_changed)

    def _on_card_config_changed(self):
        self._apply_card_style()
        if hasattr(self, '_apply_style'):
            self._apply_style()

    def apply_config(self, config: dict):
        self._bg_opacity = config.get("bg_opacity", self._bg_opacity)
        self._corner_radius = config.get("corner_radius", self._corner_radius)
        self._on_card_config_changed()

    def _on_resize_debounce(self):
        if self._delete_button and self._delete_button.isVisible():
            self._delete_button.reposition()
        if self._config_button and self._config_button.isVisible():
            self._config_button.reposition()

    def _set_natural_size(self, w: int, h: int):
        self._natural_size = QSize(w, h)

    def _scaled_px(self, base_px: int) -> int:
        return max(1, int(base_px * self._scale_factor))

    def _recompute_scale(self) -> bool:
        """重算缩放因子"""
        if not self._natural_size:
            return False
        nw = self._natural_size.width()
        nh = self._natural_size.height()
        if nw <= 0 or nh <= 0:
            return False
        sw = self.width() / nw
        sh = self.height() / nh
        # 取小 下限 0.3
        new_factor = max(0.3, min(sw, sh))
        if abs(new_factor - self._scale_factor) < 0.02:
            return False
        self._scale_factor = new_factor
        return True

    def apply_scale(self, factor: float):
        pass


    def _card_bg_css(self, obj_name=None, bg_mode=None,
                     bg_color=None, opacity=None, radius=None):
        """生成卡片背景"""
        obj_name = obj_name or self.objectName()
        if not obj_name:
            return ""
        is_dark = isDarkTheme()
        op_val = opacity if opacity is not None else self._bg_opacity
        op_val = op_val if op_val is not None else cfg.componentCardOpacity.value
        rd_val = radius if radius is not None else self._corner_radius
        rd_val = rd_val if rd_val is not None else cfg.componentCardRadius.value
        mode = bg_mode or self._bg_mode
        if mode == "custom":
            c = QColor(bg_color or self._bg_color)
        else:
            c = QColor(30, 30, 30) if is_dark else QColor(255, 255, 255)
            c.setAlpha(int(255 * op_val / 100.0))
        return (f"#{obj_name} {{ background-color: rgba({c.red()}, {c.green()}, "
                f"{c.blue()}, {c.alpha() / 255:.2f}); border-radius: {rd_val}px; }}")

    def _apply_card_style(self, target=None, obj_name=None, bg_mode=None,
                          bg_color=None, opacity=None, radius=None):
        """应用卡片背景到 target 默认 self"""
        target = target or self
        obj_name = obj_name or target.objectName()
        css = self._card_bg_css(obj_name, bg_mode, bg_color, opacity, radius)
        if css:
            target.setStyleSheet(css)


    def setContentVisible(self, visible: bool):
        """隐藏/显示内容"""
        self._content_visible = visible
        if visible:
            for i in range(self.inner_layout.count()):
                item = self.inner_layout.itemAt(i)
                if item and item.widget():
                    item.widget().setVisible(visible)
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
        if not getattr(self, '_update_pending', False):
            self._update_pending = True
            QTimer.singleShot(0, self._doUpdateSize)

    def _doUpdateSize(self):
        self._update_pending = False
        self.inner_layout.activate()
        if not getattr(self, '_size_explicitly_set', False):
            self.adjustSize()
        self.updateGeometry()

    def _ensureEditControls(self):
        if self._delete_button is None:
            home = self._getHomeInterface()
            parent_widget = home if home else self
            self._config_button, self._delete_button = _create_edit_controls(
                parent_widget, self, self._on_delete_clicked, self._on_config_clicked
            )
            try:
                cfg.componentCardOpacity.valueChanged.connect(self._update_edit_controls_style)
                cfg.componentCardRadius.valueChanged.connect(self._update_edit_controls_style)
            except Exception:
                pass

    def _update_edit_controls_style(self):
        if self._config_button and hasattr(self._config_button, 'apply_style'):
            self._config_button.apply_style()
        if self._delete_button and hasattr(self._delete_button, 'apply_style'):
            self._delete_button.apply_style()

    def showEditControls(self, visible: bool):
        self._ensureEditControls()
        self._config_button.setVisible(visible)
        self._delete_button.setVisible(visible)
        if visible:
            self._config_button.reposition()
            self._config_button.raise_()
            self._delete_button.reposition()
            self._delete_button.raise_()

    def _on_delete_clicked(self):
        home = self._getHomeInterface()
        if home:
            home.deleteSelectedComponent(self.component_id)

    def _on_config_clicked(self):
        home = self._getHomeInterface()
        if home and hasattr(home, 'component_manager'):
            comp_data = home.component_manager.get_component_data(self.component_id)
            dialog = ComponentConfigDialog(self, self.component_id, comp_data, home)
            if dialog.exec():
                new_config = dialog.get_config()
                home.component_manager.update_component_config(self.component_id, new_config)
                if hasattr(self, 'apply_config'):
                    self.apply_config(new_config)

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
        if self._config_button and self._config_button.isVisible():
            self._config_button.reposition()
        if not self._applying_scale and self._recompute_scale():
            self._applying_scale = True
            try:
                self.apply_scale(self._scale_factor)
            finally:
                self._applying_scale = False
        self._resize_debounce_timer.start(50)

    def moveEvent(self, event):
        super().moveEvent(event)
        if self._delete_button and self._delete_button.isVisible():
            self._delete_button.reposition()
        if self._config_button and self._config_button.isVisible():
            self._config_button.reposition()

    def showEvent(self, event):
        super().showEvent(event)
        if getattr(self, 'inner_layout', None):
            self.inner_layout.activate()
            if not getattr(self, '_size_explicitly_set', False):
                self.adjustSize()
            if self._natural_size is None:
                ns = self.sizeHint()
                if ns.isValid() and ns.width() > 0:
                    self._natural_size = ns

    def sizeHint(self) -> QSize:
        if getattr(self, '_size_explicitly_set', False):
            return self.size()
        if getattr(self, 'inner_layout', None):
            self.inner_layout.activate()
        if not getattr(self, '_content_visible', True):
            display_name = get_component_display_name(self.component_id)
            text = f"⚙ {display_name} {tr('component.click_to_settings')}"
            font = QFont()
            font.setPointSize(8)
            fm = QFontMetrics(font)
            return QSize(max(fm.horizontalAdvance(text) + 24, 100), 36)
        return super().sizeHint()

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
            parent_widget = self.parent()
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
        # 由父组件 MediaPlayerComponent 注入的缩放因子
        self._scale_factor = 1.0

        self._init_ui()
        self._setup_timer()
        self._apply_config()
        self._init_cover_animation()
        self._fetch_done.connect(self._on_fetched)

    def _scaled_px(self, base_px: int) -> int:
        """按当前缩放因子缩放像素值"""
        return max(1, int(base_px * self._scale_factor))

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

    def _default_cover(self):
        sz = self._scaled_px(cfg.mediaCoverSize.value)
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

    def _setup_timer(self):
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
            f"font-size: {self._scaled_px(sz + 2)}px; font-weight: 600;"
            f"color: {title_color};"
            f"font-family: 'HarmonyOS Sans', 'Microsoft YaHei', 'SimHei', sans-serif;"
        )
        self._artist.setStyleSheet(
            f"font-size: {self._scaled_px(sz)}px;"
            f"color: {artist_color};"
            f"font-family: 'HarmonyOS Sans', 'Microsoft YaHei', 'SimHei', sans-serif;"
        )
        self._time.setStyleSheet(f"color: {time_color};")
        self._dur.setStyleSheet(f"color: {time_color};")
        self._lyrics_w.set_text_size(self._scaled_px(cfg.mediaLyricsSize.value))
        self._lyrics_w.set_lyrics_color(lyrics_color)

        cover_sz = self._scaled_px(cfg.mediaCoverSize.value)
        self._cover_lbl.setFixedSize(cover_sz, cover_sz)
        if self._cover and not self._cover.isNull():
            cover_with_shadow = self._add_cover_shadow(self._cover, cover_sz)
            self._cover_lbl.setPixmap(cover_with_shadow)
        else:
            self._default_cover()

        self._bar.apply_style()
        # 如果嵌入在 MediaPlayerComponent 中，不设置固定大小
        if not self._is_embedded():
            self.setFixedSize(cfg.mediaWidth.value, cfg.mediaHeight.value)
        self._apply_background_style()

    def _is_embedded(self):
        """检查是否嵌入在 MediaPlayerComponent 中"""
        parent = self.parentWidget()
        return parent is not None and parent.objectName() == "mediaContainer"

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
        self._fetching = True
        self._pending_full = False
        self._thread = QThread()
        self._worker = _MediaFetchWorker(full)
        self._worker.moveToThread(self._thread)
        self._thread.started.connect(self._worker.run)
        self._worker.finished.connect(self._on_worker_done)
        self._thread.start()

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
        if not self._prog_timer.isActive():
            self._prog_timer.start(1000)
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
        self._prog_timer.stop()
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

    def _cleanup_detail_thread(self):
        if hasattr(self, '_detail_thread') and self._detail_thread:
            self._detail_thread.deleteLater()
            self._detail_thread = None
        if hasattr(self, '_detail_worker') and self._detail_worker:
            self._detail_worker.deleteLater()
            self._detail_worker = None

    def _fetch_kugou_thumbnail(self):
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
        self._scale_factor = 1.0

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
        return max(8, int(cfg.quickLaunchIconSize.value * self._scale_factor))

    def set_scale_factor(self, f):
        self._scale_factor = max(0.3, f)

    def _gap(self):
        return max(0, int(self._icon_gap * self._scale_factor))

    def _pad_x(self):
        return int(self.PAD_X * self._scale_factor)

    def _pad_y_top(self):
        return int(self.PAD_Y_TOP * self._scale_factor)

    def _pad_y_bottom(self):
        return int(self.PAD_Y_BOTTOM * self._scale_factor)

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
        w = n * sz + (n - 1) * self._gap() + self._pad_x() * 2
        h = sz + self._pad_y_top() + self._pad_y_bottom()
        x = (self.width() - w) / 2
        y = self.height() - h
        return QRectF(x, y, w, h)

    def _fix_size(self):
        sz = self._sz()
        n = len(self._apps)
        if n == 0:
            self.setFixedSize(0, 0)
            return
        gap = self._gap()
        px = self._pad_x()
        pyt = self._pad_y_top()
        pyb = self._pad_y_bottom()
        w_icons = n * sz + (n - 1) * gap + px * 2
        h_icons = sz + pyt + pyb
        scale_overflow = int(sz * (self.MAX_SCALE - self.BASE_SCALE))
        bounce_overflow = int(self.BOUNCE_H * self._scale_factor) + 10
        side_overflow = int(sz * (self.MAX_SCALE - self.BASE_SCALE) * 0.3)
        label_overflow = int(28 * self._scale_factor) if self._show_labels else 0
        drag_extra = int(sz * 0.5)
        w = w_icons + side_overflow * 2 + drag_extra
        h = h_icons + scale_overflow + bounce_overflow + label_overflow + drag_extra
        self.setFixedSize(w, h)

    def _icon_positions(self):
        sz = self._sz()
        n = len(self._scales)
        if n == 0:
            return []

        gap = self._gap()
        widths = [sz * sc for sc in self._scales]
        total = sum(widths) + (n - 1) * gap
        bg = self._bg_rect()
        content_w = bg.width() - self._pad_x() * 2
        start_x = bg.x() + self._pad_x() + (content_w - total) / 2

        pos = []
        cx = start_x
        for i in range(n):
            pos.append(cx + widths[i] / 2)
            cx += widths[i] + gap
        return pos

    def _icon_rect(self, i, positions=None):
        if positions is None:
            positions = self._icon_positions()
        s = self._sz() * self._scales[i]
        cx = positions[i]
        bg = self._bg_rect()
        by = bg.y() + bg.height() - self._pad_y_bottom()
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
        gap = self._gap()
        px = self._pad_x()
        bg = self._bg_rect()
        content_w = bg.width() - px * 2
        n = len(self._apps)
        total_w = n * sz + (n - 1) * gap
        start_x = bg.x() + px + (content_w - total_w) / 2

        new_target = -1
        for i in range(n):
            icon_x = start_x + i * (sz + gap)
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
        baseline_y = bg.y() + bg.height() - self._pad_y_bottom()
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
                    label_font.setPixelSize(max(8, int(14 * self._scale_factor)))
                    label_font.setWeight(QFont.Weight.Medium)
                    p.setFont(label_font)
                    fm = QFontMetrics(label_font)

                    display_name = name
                    if len(name) > 50:
                        display_name = name[:50] + "..."

                    padding_x = int(10 * self._scale_factor)
                    label_w = fm.horizontalAdvance(display_name) + padding_x * 2
                    label_h = int(24 * self._scale_factor)
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
py_datetime = datetime
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
        self.clockLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        self.dateLabel = QLabel("")
        self.dateLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.dateLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(32, 24, 32, 24)
        layout.setSpacing(8)
        layout.addWidget(self.clockLabel, 3)
        layout.addWidget(self.dateLabel, 1)

        self._set_natural_size(400, 200)
        self.setMinimumSize(120, 80)
        self._size_explicitly_set = True
        self.resize(400, 200)
        self._apply_style()
    def apply_scale(self, factor):
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
        self._apply_card_style()
        clock_color = cfg.clockColor.value
        color_str = clock_color.name() if hasattr(clock_color, 'name') else str(clock_color)
        clock_size = cfg.clockSize.value
        date_size = cfg.dateSize.value

        self.clockLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {self._scaled_px(clock_size)}px;
            font-weight: bold;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)

        self.dateLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {self._scaled_px(date_size)}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)
        self.updateSize()


class WeatherComponentBase(DraggableContainer):
    """天气基类：定时器 刷新间隔 天气数据更新 城市选择等公共"""

    def __init__(self, parent, component_data: dict, layout_direction: str = "vertical"):
        super().__init__(parent, component_id=component_data["id"], layout_direction=layout_direction)
        self._home = parent
        self._current_icon_path = None

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
        self.timer.setInterval(interval_map.get(cfg.weatherUpdateInterval.value, 300000))

    def _refresh_weather(self):
        if not cfg.showWeather.value:
            self.hide()
            return
        self.show()
        self._update_from_cache()

    def _on_weather_updated(self):
        self._update_from_cache()

    def _update_from_cache(self):
        """从缓存读取天气数据 后显示"""
        raise NotImplementedError

    def mousePressEvent(self, event):
        """城市标签单击打开区域选择"""
        if event.button() == Qt.MouseButton.LeftButton and hasattr(self, 'cityLabel'):
            local_pos = self.cityLabel.mapFrom(self, event.pos())
            if self.cityLabel.rect().contains(local_pos):
                self._onCityLabelClicked()
                event.accept()
                return
        super().mousePressEvent(event)

    def _onCityLabelClicked(self):
        """打开区域选择对话框"""
        from services.weather import RegionSelectorDialog, RegionDatabase, WeatherService

        parent = getattr(self._home, 'mainWindow', None) or self._home
        dialog = RegionSelectorDialog(parent)
        if dialog.exec():
            region = dialog.get_selected_region()
            if not region:
                return
            cfg.city.value = region
            db = RegionDatabase()
            lon, lat = db.get_coordinates(region)
            if lon is not None and lat is not None:
                cfg.longitude.value = lon
                cfg.latitude.value = lat
                logger.info(f"[天气组件] 选择城市: {region} (经纬度: {lon}, {lat})")
            self.cityLabel.setText(region)
            try:
                ws = WeatherService()
                data = ws.fetch_all()
                if data:
                    if not save_cache("weather", data, cfg.weatherUpdateInterval.value):
                        logger.warning("[天气组件] 缓存保存失败")
                    if hasattr(self._home, '_cached_weather'):
                        self._home._cached_weather = data
                    self._home.weather_updated.emit(data)
                    logger.info("[天气组件] 新城市天气获取成功")
            except Exception as e:
                logger.error(f"[天气组件] 新城市天气获取失败: {e}")


class WeatherIconTempComponent(WeatherComponentBase):
    """天气组件：图标 温度"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_data, "horizontal")
        self.setObjectName("weatherContainer")
        self._setup_ui()
        self._setup_timer()
        cfg.weatherSize.valueChanged.connect(self._apply_style)
        cfg.weatherIconSize.valueChanged.connect(self._update_icon_size)

    def _setup_ui(self):
        self.tempLabel = QLabel("")
        self.tempLabel.setObjectName("weatherTempLabel")
        self.tempLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.tempLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        self.iconLabel = QLabel("")
        self.iconLabel.setObjectName("weatherIconLabel")
        self.iconLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.iconLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.setSpacing(12)
        layout.addWidget(self.iconLabel, 1)
        layout.addWidget(self.tempLabel, 1)

        self._set_natural_size(200, 200)
        self.setMinimumSize(80, 80)
        self._size_explicitly_set = True
        self.resize(200, 200)
        self._apply_style()

    def _update_from_cache(self):
        cached = get_cached_content("weather", ignore_expiry=True)
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
            self._current_icon_path = icon_path
            icon_size = self._scaled_px(cfg.weatherIconSize.value)
            dpr = self.devicePixelRatioF()
            pm = render_svg_icon(icon_path, icon_size, dpr)
            if not pm.isNull():
                self.iconLabel.setPixmap(pm)

    def _update_icon_size(self):
        cached = get_cached_content("weather")
        if cached:
            self._update_display(cached)

    def _apply_style(self):
        self._apply_card_style()
        color = cfg.weatherTextColor.value
        color_str = color.name() if hasattr(color, 'name') else str(color)
        size = cfg.weatherSize.value

        self.tempLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {self._scaled_px(size)}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)
        self.updateSize()

    def apply_scale(self, factor):
        self._apply_style()
        if self._current_icon_path and os.path.exists(self._current_icon_path):
            icon_size = self._scaled_px(cfg.weatherIconSize.value)
            dpr = self.devicePixelRatioF()
            pm = render_svg_icon(self._current_icon_path, icon_size, dpr)
            if not pm.isNull():
                self.iconLabel.setPixmap(pm)


class WeatherHourlyComponent(WeatherComponentBase):
    """逐小时天气卡片"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_data, "vertical")
        self.setObjectName("weatherHourlyContainer")
        self._hourly_data = None
        self._hourly_icon_paths = [None] * 6
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
        self.currentIconLabel.setFixedSize(self._scaled_px(60), self._scaled_px(60))

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
            icon_label.setFixedSize(self._scaled_px(28), self._scaled_px(28))

            temp_label = QLabel("--°")
            temp_label.setAlignment(Qt.AlignmentFlag.AlignCenter)

            col_layout.addWidget(time_label)
            col_layout.addWidget(icon_label)
            col_layout.addWidget(temp_label)

            bottom_layout.addWidget(col, 1)
            self._hourly_widgets.append((time_label, icon_label, temp_label))

        layout.addWidget(self._top_row, 3)
        layout.addWidget(self._bottom_row, 1)

        self._set_natural_size(400, 200)
        self.setMinimumSize(160, 100)
        self._size_explicitly_set = True
        self.resize(400, 200)
        self._top_row.setMinimumHeight(self._scaled_px(100))
        self._apply_style()

    def _update_from_cache(self):
        from services.weather import WeatherService

        wd = get_cached_content("weather", ignore_expiry=True)

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

        weather_text = WeatherService.WEATHER_MAP.get(current_icon_code, ("未知", "2.svg"))[0]
        logger.info(f"[WeatherHourly] 城市:{cfg.city.value} 当前温度:{current_temp}° 天气代码:{current_icon_code} 天气:{weather_text} 图标:{icon_name}")

        if icon_path and os.path.exists(icon_path):
            self._current_icon_path = icon_path
            dpr = self.devicePixelRatioF()
            pm = render_svg_icon(icon_path, self._scaled_px(60), dpr)
            if not pm.isNull():
                self.currentIconLabel.setPixmap(pm)
            else:
                self.currentIconLabel.clear()
        else:
            self._current_icon_path = None
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
                    self._hourly_icon_paths[i] = icon_path
                    dpr = self.devicePixelRatioF()
                    pm = render_svg_icon(icon_path, self._scaled_px(28), dpr)
                    if not pm.isNull():
                        icon_label.setPixmap(pm)
                    else:
                        icon_label.clear()
                else:
                    self._hourly_icon_paths[i] = None
                    icon_label.clear()

                temp = hour_data.get("temp", "--")
                temp_label.setText(f"{temp}°")
            else:
                time_label.setText("--:00")
                icon_label.clear()
                temp_label.setText("--°")

        self._apply_style()

    def _apply_style(self):
        self._apply_card_style()
        color = cfg.weatherTextColor.value
        color_str = color.name() if hasattr(color, 'name') else str(color)

        self.cityLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {self._scaled_px(16)}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
            opacity: 0.7;
        """)

        self.currentTempLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {self._scaled_px(52)}px;
            font-weight: 300;
            font-family: {FONT_FAMILY};
            background-color: transparent;
            line-height: 1.0;
        """)

        self.alertLabel.setStyleSheet(f"""
            color: #ff6b6b;
            font-size: {self._scaled_px(12)}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)

        for time_label, icon_label, temp_label in self._hourly_widgets:
            time_label.setStyleSheet(f"""
                color: {color_str};
                font-size: {self._scaled_px(12)}px;
                font-family: {FONT_FAMILY};
                background-color: transparent;
                opacity: 0.7;
            """)
            temp_label.setStyleSheet(f"""
                color: {color_str};
                font-size: {self._scaled_px(12)}px;
                font-family: {FONT_FAMILY};
                background-color: transparent;
            """)

        self.updateSize()

    def apply_scale(self, factor):
        self._apply_style()
        dpr = self.devicePixelRatioF()
        self.currentIconLabel.setFixedSize(self._scaled_px(60), self._scaled_px(60))
        if self._current_icon_path and os.path.exists(self._current_icon_path):
            pm = render_svg_icon(self._current_icon_path, self._scaled_px(60), dpr)
            if not pm.isNull():
                self.currentIconLabel.setPixmap(pm)
        for i, (time_label, icon_label, temp_label) in enumerate(self._hourly_widgets):
            icon_label.setFixedSize(self._scaled_px(28), self._scaled_px(28))
            p = self._hourly_icon_paths[i] if i < len(self._hourly_icon_paths) else None
            if p and os.path.exists(p):
                pm = render_svg_icon(p, self._scaled_px(28), dpr)
                if not pm.isNull():
                    icon_label.setPixmap(pm)
        if self._top_row is not None:
            self._top_row.setMinimumHeight(self._scaled_px(100))


class WeatherWeeklyComponent(WeatherComponentBase):
    """逐日天气组件。"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_data, "vertical")
        self.setObjectName("weatherWeeklyContainer")
        self._daily_data = None
        self._daily_icon_paths = [None] * 4
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
        self.currentIconLabel.setFixedSize(self._scaled_px(48), self._scaled_px(48))

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
            row.setFixedHeight(self._scaled_px(20))
            row_layout = QHBoxLayout(row)
            row_layout.setContentsMargins(0, 0, 0, 0)
            row_layout.setSpacing(0)

            day_label = QLabel("--")
            day_label.setFixedWidth(self._scaled_px(40))
            day_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)

            icon_label = QLabel()
            icon_label.setFixedSize(self._scaled_px(18), self._scaled_px(18))
            icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)

            low_label = QLabel("--")
            low_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
            low_label.setObjectName(f"weeklyLow_{i}")

            spacer = QLabel()
            spacer.setFixedWidth(self._scaled_px(8))

            high_label = QLabel("--°")
            high_label.setFixedWidth(self._scaled_px(28))
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

        layout.addWidget(top, 3)
        layout.addWidget(bottom, 1)

        self._set_natural_size(200, 200)
        self.setMinimumSize(80, 80)
        self._size_explicitly_set = True
        self.resize(200, 200)
        self._apply_style()

    def _update_from_cache(self):
        from services.weather import WeatherService

        wd = get_cached_content("weather", ignore_expiry=True)

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

        weather_text = WeatherService.WEATHER_MAP.get(current_icon_code, ("未知", "2.svg"))[0]
        logger.info(f"[WeatherWeekly] 城市:{cfg.city.value} 当前温度:{current_temp}° 天气代码:{current_icon_code} 天气:{weather_text} 图标:{icon_name}")

        if icon_path and os.path.exists(icon_path):
            self._current_icon_path = icon_path
            dpr = self.devicePixelRatioF()
            pm = render_svg_icon(icon_path, self._scaled_px(48), dpr)
            if not pm.isNull():
                self.currentIconLabel.setPixmap(pm)
            else:
                self.currentIconLabel.clear()
        else:
            self._current_icon_path = None
            self.currentIconLabel.clear()

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
                self._daily_icon_paths[i] = icon_path
                dpr = self.devicePixelRatioF()
                pm = render_svg_icon(icon_path, self._scaled_px(18), dpr)
                if not pm.isNull():
                    icon_label.setPixmap(pm)
                else:
                    icon_label.clear()
            else:
                self._daily_icon_paths[i] = None
                icon_label.clear()

            low_label.setText(day_data.get("low", "--"))
            high_label.setText(f"{day_data.get('high', '--')}°")

        self._apply_style()

    def _apply_style(self):
        self._apply_card_style()
        color = cfg.weatherTextColor.value
        color_str = color.name() if hasattr(color, 'name') else str(color)

        self.cityLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {self._scaled_px(14)}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
            opacity: 0.7;
        """)

        self.currentTempLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {self._scaled_px(48)}px;
            font-weight: 300;
            font-family: {FONT_FAMILY};
            background-color: transparent;
            line-height: 1.0;
        """)

        for day_label, icon_label, low_label, high_label in self._forecast_rows:
            day_label.setStyleSheet(f"""
                color: {color_str};
                font-size: {self._scaled_px(11)}px;
                font-family: {FONT_FAMILY};
                background-color: transparent;
                opacity: 0.7;
            """)
            low_label.setStyleSheet(f"""
                color: {color_str};
                font-size: {self._scaled_px(11)}px;
                opacity: 0.6;
                font-family: {FONT_FAMILY};
                background-color: transparent;
            """)
            high_label.setStyleSheet(f"""
                color: {color_str};
                font-size: {self._scaled_px(11)}px;
                font-family: {FONT_FAMILY};
                background-color: transparent;
            """)

        self.updateSize()

    def apply_scale(self, factor):
        self._apply_style()
        dpr = self.devicePixelRatioF()
        self.currentIconLabel.setFixedSize(self._scaled_px(48), self._scaled_px(48))
        if self._current_icon_path and os.path.exists(self._current_icon_path):
            pm = render_svg_icon(self._current_icon_path, self._scaled_px(48), dpr)
            if not pm.isNull():
                self.currentIconLabel.setPixmap(pm)
        for i, (day_label, icon_label, low_label, high_label) in enumerate(self._forecast_rows):
            icon_label.setFixedSize(self._scaled_px(18), self._scaled_px(18))
            p = self._daily_icon_paths[i] if i < len(self._daily_icon_paths) else None
            if p and os.path.exists(p):
                pm = render_svg_icon(p, self._scaled_px(18), dpr)
                if not pm.isNull():
                    icon_label.setPixmap(pm)


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
        self.poetryLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.addWidget(self.poetryLabel, 1)

        self._set_natural_size(400, 200)
        self.setMinimumSize(120, 80)
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
        self._apply_card_style()
        color = cfg.poetryTextColor.value
        color_str = color.name() if hasattr(color, 'name') else str(color)
        size = cfg.poetrySize.value

        self.poetryLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {self._scaled_px(size)}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)
        self.updateSize()

    def apply_scale(self, factor):
        self._apply_style()


def _render_svg_logo(icon_path, height=30):
    """显示SVG logo"""
    renderer = QSvgRenderer(icon_path)
    if not renderer.isValid():
        return QPixmap()
    default_size = renderer.defaultSize()
    if default_size.isValid() and default_size.height() > 0:
        ratio = default_size.width() / default_size.height()
        width = int(height * ratio)
    else:
        width = height
    pm = QPixmap(width, height)
    pm.fill(Qt.GlobalColor.transparent)
    p = QPainter(pm)
    p.setRenderHint(QPainter.RenderHint.Antialiasing)
    p.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)
    renderer.render(p)
    p.end()
    return pm


class NewsComponent(DraggableContainer):
    """新闻组件基类"""

    # 子类配置
    _source = ""           # 数据源标识
    _icon_key = ""         # NEWS_ICONS 中的键
    _object_name = ""      # 容器 objectName
    _num_color = "#ffffff" # 序号颜色
    _item_count = 4        # 显示条目数
    _use_cctv_api = False  # 是否使用央视新闻 API

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName(self._object_name)
        self._home = parent
        self._news_titles = ["--"] * self._item_count
        self._news_urls = [""] * self._item_count
        self._icon_path = get_resPath(NEWS_ICONS[self._icon_key])
        self._setup_ui()
        self._setup_timer()

    def _setup_ui(self):
        dpr = self.devicePixelRatioF()
        pm = _render_svg_logo(self._icon_path, self._scaled_px(30))
        pm.setDevicePixelRatio(dpr)

        self.iconLabel = QLabel()
        self.iconLabel.setPixmap(pm)
        self.iconLabel.setFixedSize(int(pm.width() / dpr), int(pm.height() / dpr))
        self.iconLabel.setObjectName("newsHeaderIcon")

        header_layout = QHBoxLayout()
        header_layout.setContentsMargins(0, 0, 0, 0)
        header_layout.addWidget(self.iconLabel)
        header_layout.addStretch()

        self.itemWidgets = []
        for i in range(self._item_count):
            item_label = QLabel("--")
            item_label.setWordWrap(True)
            item_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignTop)
            item_label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
            item_label.setObjectName("newsItemLabel")
            item_label.mousePressEvent = lambda e, idx=i: self._on_news_clicked(idx)
            item_label.setCursor(Qt.CursorShape.PointingHandCursor)
            self.itemWidgets.append(item_label)

        layout = self.inner_layout
        layout.setContentsMargins(16, 16, 16, 16)
        layout.setSpacing(6)
        layout.addLayout(header_layout)
        for widget in self.itemWidgets:
            layout.addWidget(widget, 1)

        self._set_natural_size(360, 220)
        self.setMinimumSize(150, 100)
        self._size_explicitly_set = True
        self.resize(360, 220)
        self._apply_style()

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._refresh_news)
        self.timer.start(300000)
        self._refresh_news()

    def _refresh_news(self):
        if self._use_cctv_api:
            data = NewsService.fetch_cctv_news(use_cache=True)
        else:
            data = NewsService.fetch_daily_news(self._source, use_cache=True)
        if not data:
            data = get_cached_content(f"news_{self._source}", ignore_expiry=True)
        self._update_display(data)

    def _update_display(self, data):
        count = self._item_count
        titles = ["--"] * count
        urls = [""] * count
        if isinstance(data, list) and data:
            for index in range(count):
                if index < len(data):
                    item = data[index] or {}
                    titles[index] = item.get("title") or item.get("name") or "--"
                    urls[index] = item.get("url") or item.get("link") or ""
        self._news_urls = urls
        self._news_titles = titles
        self._render_items()

    def _render_items(self):
        sz_num = self._scaled_px(12)
        sz_text = self._scaled_px(15)
        for i, (label, text) in enumerate(zip(self.itemWidgets, self._news_titles)):
            label.setText(
                f"<span style='font-size:{sz_num}px;color:{self._num_color};font-family:{FONT_FAMILY};'>"
                f"{i+1}.</span> "
                f"<span style='font-size:{sz_text}px;color:#ffffff;font-family:{FONT_FAMILY};'>{text}</span>"
            )

    def _on_news_clicked(self, index):
        if 0 <= index < len(self._news_urls) and self._news_urls[index]:
            webbrowser.open(self._news_urls[index])

    def _apply_style(self):
        self._apply_card_style()
        self.updateSize()

    def apply_scale(self, factor):
        dpr = self.devicePixelRatioF()
        pm = _render_svg_logo(self._icon_path, self._scaled_px(30))
        pm.setDevicePixelRatio(dpr)
        self.iconLabel.setPixmap(pm)
        self.iconLabel.setFixedSize(int(pm.width() / dpr), int(pm.height() / dpr))
        self._render_items()


class NewsBaiduComponent(NewsComponent):
    """百度热搜组件"""
    _source = "baidu"
    _icon_key = "baidu"
    _object_name = "newsBaiduContainer"
    _num_color = "#2932e1"


class NewsWeiboComponent(NewsComponent):
    """微博热搜组件"""
    _source = "weibo"
    _icon_key = "weibo"
    _object_name = "newsWeiboContainer"
    _num_color = "#e89214"


class NewsJinritoutiaoComponent(NewsComponent):
    """今日头条组件"""
    _source = "jinritoutiao"
    _icon_key = "jinritoutiao"
    _object_name = "newsJinritoutiaoContainer"
    _num_color = "#ff353c"


class NewsTenxunwangComponent(NewsComponent):
    """腾讯网组件"""
    _source = "tenxunwang"
    _icon_key = "tencent"
    _object_name = "newsTenxunwangContainer"
    _num_color = "#106eb0"


class NewsCCTVComponent(NewsComponent):
    """央视新闻组件"""
    _source = "xcvts"
    _icon_key = "cctv"
    _object_name = "newsCCTVContainer"
    _num_color = "#e53928"
    _item_count = 3
    _use_cctv_api = True


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
        self.countdownLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.addWidget(self.countdownLabel, 1)

        self._set_natural_size(200, 200)
        self.setMinimumSize(80, 80)
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
        self._apply_card_style()
        color = cfg.countdownTextColor.value
        color_str = color.name() if hasattr(color, 'name') else str(color)
        size = cfg.countdownTextSize.value

        self.countdownLabel.setStyleSheet(f"""
            color: {color_str};
            font-size: {self._scaled_px(size)}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)
        self.updateSize()

    def apply_scale(self, factor):
        self._apply_style()


class SchoolInfoComponent(DraggableContainer):
    """班级卡片组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("schoolInfoContainer")
        self._home = parent
        self._read_config(component_data.get("config", {}))
        self._setup_ui()

    def _read_config(self, config):
        # 哇哦。。
        self._class = config.get("class", getattr(self, "_class", "")) or ""
        self._school = config.get("school", getattr(self, "_school", "")) or ""
        self._count = config.get("count", getattr(self, "_count", "")) or ""
        self._slogan = config.get("slogan", getattr(self, "_slogan", "")) or ""
        self._class_size = config.get("class_size", getattr(self, "_class_size", 48))
        self._school_size = config.get("school_size", getattr(self, "_school_size", 25))
        self._count_size = config.get("count_size", getattr(self, "_count_size", 19))
        self._slogan_size = config.get("slogan_size", getattr(self, "_slogan_size", 23))
        self._text_color = config.get("text_color", getattr(self, "_text_color", ""))
        self._font_scale = config.get("font_scale", getattr(self, "_font_scale", 100))
        self._bg_opacity = config.get("bg_opacity", getattr(self, "_bg_opacity", None))
        self._corner_radius = config.get("corner_radius", getattr(self, "_corner_radius", None))
        self._main_bg_mode = config.get("main_bg_mode", getattr(self, "_main_bg_mode", "opacity"))
        self._main_bg_color = config.get("main_bg_color", getattr(self, "_main_bg_color", "#ffffff"))
        self._top_bg_mode = config.get("top_bg_mode", getattr(self, "_top_bg_mode", "opacity"))
        self._top_bg_color = config.get("top_bg_color", getattr(self, "_top_bg_color", "#ffffff"))

    def apply_config(self, config):
        self._read_config(config)
        self._apply_style()
        self._update_info()

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(6)

        # 上层背景：班级 学校
        self.topWidget = QWidget(self)
        self.topWidget.setObjectName("schoolInfoTopBg")
        top_layout = QVBoxLayout(self.topWidget)
        top_layout.setContentsMargins(20, 16, 20, 12)
        top_layout.setSpacing(4)

        # 班级 人数
        class_row = QHBoxLayout()
        class_row.setContentsMargins(0, 0, 0, 0)
        self.classLabel = QLabel("")
        self.classLabel.setObjectName("schoolClassLabel")
        self.classLabel.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self.countLabel = QLabel("")
        self.countLabel.setObjectName("schoolCountLabel")
        self.countLabel.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        class_row.addWidget(self.classLabel, 1)
        class_row.addWidget(self.countLabel, 0)
        top_layout.addLayout(class_row)

        # 学校名
        self.nameLabel = QLabel("")
        self.nameLabel.setObjectName("schoolNameLabel")
        self.nameLabel.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        top_layout.addWidget(self.nameLabel)

        # 口号 组件背景为背景
        self.sloganLabel = QLabel("")
        self.sloganLabel.setObjectName("schoolSloganLabel")
        self.sloganLabel.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self.sloganLabel.setContentsMargins(20, 4, 20, 4)
        self.sloganLabel.setWordWrap(True)
        self.sloganLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        layout.addWidget(self.topWidget, 3)
        layout.addWidget(self.sloganLabel, 1)

        self._set_natural_size(400, 200)
        self.setMinimumSize(150, 100)
        self._size_explicitly_set = True
        self.resize(400, 200)
        self._apply_style()
        self._update_info()

    def _apply_style(self):
        is_dark = isDarkTheme()
        if self._text_color:
            text_color = self._text_color
        else:
            text_color = "#e0e0e0" if is_dark else "#1a1a1a"
        sub_color = "#aaaaaa" if is_dark else "#666666"
        count_color = "#777777" if is_dark else "#999999"

        # 组件背景
        self._apply_card_style(
            bg_mode=self._main_bg_mode, bg_color=self._main_bg_color)
        # 上层背景
        self._apply_card_style(
            target=self.topWidget, obj_name="schoolInfoTopBg",
            bg_mode=self._top_bg_mode, bg_color=self._top_bg_color)

        # 字号
        class_sz = self._scaled_px(self._class_size)
        school_sz = self._scaled_px(self._school_size)
        count_sz = self._scaled_px(self._count_size)
        slogan_sz = self._scaled_px(self._slogan_size)

        self.classLabel.setStyleSheet(
            f"color: {text_color}; font-size: {class_sz}px; font-weight: bold; "
            f"font-family: {FONT_FAMILY}; background: transparent;")
        self.nameLabel.setStyleSheet(
            f"color: {sub_color}; font-size: {school_sz}px; "
            f"font-family: {FONT_FAMILY}; background: transparent;")
        self.countLabel.setStyleSheet(
            f"color: {count_color}; font-size: {count_sz}px; "
            f"font-family: {FONT_FAMILY}; background: transparent;")
        self.sloganLabel.setStyleSheet(
            f"color: {sub_color}; font-size: {slogan_sz}px; "
            f"font-family: {FONT_FAMILY}; background: transparent;")

    def apply_scale(self, factor):
        self._apply_style()

    def _scaled_px(self, base_px: int) -> int:
        font_scale = self._font_scale / 100.0
        return max(1, int(base_px * font_scale * self._scale_factor))

    def _update_info(self):
        class_text = self._class
        school_text = self._school
        slogan_text = self._slogan

        if not class_text and hasattr(self._home, 'schoolClassLabel'):
            class_text = self._home.schoolClassLabel.text()
        if not school_text and hasattr(self._home, 'schoolNameLabel'):
            school_text = self._home.schoolNameLabel.text()

        self.classLabel.setText(class_text or "")
        self.nameLabel.setText(school_text or "")
        self.sloganLabel.setText(slogan_text or "")
        if self._count:
            self.countLabel.setText(f"{self._count}人")
        else:
            self.countLabel.setText("")

    def showEvent(self, event):
        super().showEvent(event)
        self._apply_style()
        self._update_info()


class MediaPlayerComponent(DraggableContainer):
    """媒体播放器组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("mediaContainer")
        self._home = parent
        self._setup_ui()
        self._setup_timer()

    def _setup_ui(self):
        self.mediaWidget = MediaWidget(self)
        self.mediaWidget.setObjectName("mediaWidget")
        self.mediaWidget.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        self.mediaWidget.setMinimumSize(1, 1)
        self.mediaWidget.setMaximumSize(16777215, 16777215)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        layout.addWidget(self.mediaWidget, 1)

        nat_w = cfg.mediaWidth.value
        nat_h = cfg.mediaHeight.value
        self._set_natural_size(nat_w, nat_h)
        self.setMinimumSize(150, 80)
        self._size_explicitly_set = True
        self.resize(nat_w, nat_h)

        # 同步尺寸变化
        cfg.mediaWidth.valueChanged.connect(self._sync_media_size)
        cfg.mediaHeight.valueChanged.connect(self._sync_media_size)

    def _sync_media_size(self):
        """同步尺寸变化"""
        self.resize(cfg.mediaWidth.value, cfg.mediaHeight.value)
        if hasattr(self.mediaWidget, '_apply_config'):
            self.mediaWidget._apply_config()

    def apply_scale(self, factor):
        # 将缩放因子传递给 MediaWidget，让其缩放字体/封面/歌词
        if self.mediaWidget is not None:
            self.mediaWidget._scale_factor = factor
            if hasattr(self.mediaWidget, '_apply_config'):
                self.mediaWidget._apply_config()

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

        self._set_natural_size(400, 200)
        self.setMinimumSize(120, 80)
        self._size_explicitly_set = True
        self.resize(400, 200)

        # 加载应用
        apps = cfg.quickLaunchApps.value
        if apps:
            self.dock.set_apps(apps)

    def apply_scale(self, factor):
        self.dock.set_scale_factor(factor)
        self.dock._fix_size()
        self.dock.update()

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
    """返回 (holiday, solar_term, lunar_day)"""
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
            sub_font = QFont('Microsoft YaHei')
            sub_font.setPixelSize(int(sz * 0.35))  # 节日字号
            sub_font.setBold(True)
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
        self._up_btn.setFixedSize(self._scaled_px(28), self._scaled_px(18))
        self._up_btn.clicked.connect(self._go_prev_month)

        self._down_btn = TransparentToolButton(self)
        self._down_btn.setIcon(FUI.CHEVRON_DOWN.icon())
        self._down_btn.setFixedSize(self._scaled_px(28), self._scaled_px(18))
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

        self._set_natural_size(300, 300)
        self.setMinimumSize(120, 120)
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
        title_sz = self._scaled_px(20)
        wk_sz = self._scaled_px(13)
        self.setStyleSheet(f"""
            {self._card_bg_css()}
            #calTitle {{
                color: #f0f0f0;
                font-size: {title_sz}px;
                font-weight: 600;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}
            #calWk {{
                color: #d0d0d0;
                font-size: {wk_sz}px;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}
            #calWk[wkend="true"] {{
                color: #b0b0b0;
            }}
        """)

    def apply_scale(self, factor):
        self._up_btn.setFixedSize(self._scaled_px(28), self._scaled_px(18))
        self._down_btn.setFixedSize(self._scaled_px(28), self._scaled_px(18))
        self._apply_style()


class _TimetableRow(QWidget):
    """单行课程/课间"""
    progressChanged = None 

    def __init__(self, is_current=False, is_break=False, is_past=False, parent=None):
        super().__init__(parent)
        self._is_current = is_current
        self._is_break = is_break
        self._is_past = is_past
        if is_current:
            self.setObjectName("timetableRowCurrent")
        elif is_past:
            self.setObjectName("timetableRowPast")
        else:
            self.setObjectName("timetableRow")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)

        self._main_layout = QVBoxLayout(self)
        self._main_layout.setContentsMargins(0, 0, 0, 0)
        self._main_layout.setSpacing(0)

        # 内容行
        self._content = QWidget()
        self._content.setObjectName("timetableRowInner")
        self._content_layout = QHBoxLayout(self._content)
        self._content_layout.setContentsMargins(10, 7, 10, 7)
        self._content_layout.setSpacing(12)
        self._main_layout.addWidget(self._content)

        # 进度条 当前行显示
        self._progress = QProgressBar(self)
        self._progress.setObjectName("timetableProgress")
        self._progress.setFixedHeight(3)
        self._progress.setTextVisible(False)
        self._progress.setRange(0, 100)
        self._progress.setValue(0)
        if not is_current:
            self._progress.hide()
        self._main_layout.addWidget(self._progress)

    def addWidget(self, w, stretch=0):
        self._content_layout.addWidget(w, stretch)

    def setProgress(self, pct):
        """0~100"""
        if self._is_current:
            self._progress.show()
            self._progress.setValue(int(max(0, min(100, pct))))
    def set_current(self, active: bool):
        self._is_current = active
        if active:
            self.setObjectName("timetableRowCurrent")
            # 有进度条就显示
            if hasattr(self, '_progress_bar'):
                self._progress_bar.show()
        else:
            if self._is_past:
                self.setObjectName("timetableRowPast")
            else:
                self.setObjectName("timetableRow")
            # 隐藏进度条
            if hasattr(self, '_progress_bar'):
                self._progress_bar.hide()
        # 刷新样式
        self.style().unpolish(self)
        self.style().polish(self)

class TimetablePreviewComponent(DraggableContainer):
    """今日课表预览"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("timetableContainer")
        self._home = parent
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        self._bridge = None
        self._timetable_page = None
        self._schedule_rows = []
        self._current_row_data = None  # start_time, end_time 用于算进度
        self._past_skip_groups = 0  # 已跳过多少组5节
        self._last_user_scroll_time = 0   # 上一次用户操作滚动条的时间戳
        self._after_school_mode = False          # 是否已进入放学预览状态
        self._preview_timer = QTimer(self)       # 控制 30 秒预览
        self._preview_timer.setSingleShot(True)
        self._preview_timer.timeout.connect(self._exit_preview)

        self._scroll_animation_timer = QTimer(self)  # 逐帧滚动
        self._scroll_direction = 1               # 1向下，-1向上
        self._scroll_step = 2                    # 每次滚动像素数
        self._scroll_animation_timer.timeout.connect(self._animate_scroll)
        self._setup_ui()
        self._connect_timetable_page()
        self._refresh_schedule()

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setContentsMargins(10, 10, 10, 10)
        layout.setSpacing(6)

        # 标题
        self._title_label = QLabel("今日课表")
        self._title_label.setObjectName("timetableTitle")
        layout.addWidget(self._title_label)

        # 滚动区域
        self._scroll = QScrollArea(self)
        self._scroll.setObjectName("timetableScroll")
        self._scroll.setWidgetResizable(True)
        self._scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self._scroll.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self._scroll.setFrameShape(QScrollArea.Shape.NoFrame)

        self._scroll_content = QWidget()
        self._scroll_content.setObjectName("timetableScrollContent")
        self._scroll_layout = QVBoxLayout(self._scroll_content)
        self._scroll_layout.setContentsMargins(0, 0, 0, 0)
        self._scroll_layout.setSpacing(4)
        self._scroll_layout.addStretch()

        self._scroll.setWidget(self._scroll_content)
        layout.addWidget(self._scroll, 1)

        self._set_natural_size(300, 550)
        self.setMinimumSize(160, 200)
        self._size_explicitly_set = True
        self.resize(300, 550)
        self._apply_style()

        # 监听卡片设置变化
        from core.config import cfg

        # 定时刷新 自动滚动
        self._refresh_timer = QTimer(self)
        self._refresh_timer.timeout.connect(self._on_timer)
        self._refresh_timer.start(5000)

        # 进度条更新
        self._progress_timer = QTimer(self)
        self._progress_timer.timeout.connect(self._update_progress)
        self._progress_timer.start(1000)

        # 监听用户手动滚动
        self._scroll.verticalScrollBar().sliderReleased.connect(self._on_user_scroll_action)
        # 监听滚轮事件
        self._scroll.installEventFilter(self)
    def _on_user_scroll_action(self):
        """手动拖动最后时间"""
        self._last_user_scroll_time = time.time()

    def eventFilter(self, obj, event):
        """监听滚轮事件"""
        if obj == self._scroll and event.type() == QEvent.Type.Wheel:
            self._last_user_scroll_time = time.time()
        return super().eventFilter(obj, event)
    def _scroll_to_top(self):
        """滚动到列表顶部"""
        self._scroll.verticalScrollBar().setValue(0)
    def _exit_preview(self):
        """停止滚动 显示明日课表"""
        self._scroll_animation_timer.stop()
        self._preview_timer.stop()
        self._after_school_mode = False
        self._title_label.setText("明日课表")
        self._scroll_to_top()

    def _animate_scroll(self):
        """上下自动滚动"""
        sb = self._scroll.verticalScrollBar()
        val = sb.value()
        max_val = sb.maximum()
        new_val = val + self._scroll_direction * self._scroll_step
        if new_val >= max_val:
            new_val = max_val
            self._scroll_direction = -1
        elif new_val <= sb.minimum():
            new_val = sb.minimum()
            self._scroll_direction = 1
        sb.setValue(new_val)

    def _rebuild_schedule(self, schedule):
        """用给定课表数据重建UI"""
        while self._scroll_layout.count() > 1:
            item = self._scroll_layout.takeAt(0)
            if item and item.widget():
                item.widget().deleteLater()
        self._schedule_rows.clear()
        self._current_row_data = None

        if not schedule:
            # 无课表时显示空行
            row = self._build_empty_row()
            self._scroll_layout.insertWidget(self._scroll_layout.count() - 1, row)
            self._schedule_rows.append(row)
            return


        from datetime import datetime as _dt, time as _time
        now_time = _dt.now().time()

        last_current_row = None
        last_current_times = None

        for row_data in schedule:
            subject, teacher, start, end, index, is_current, is_break, break_name = row_data

            if self._after_school_mode:
                is_past = False
            else:
                try:
                    eh, em = map(int, end.split(":"))
                    is_past = (not is_current and not is_break and _time(eh, em) <= now_time)
                except Exception:
                    is_past = False

            if is_break and not is_current:
                continue

            # 构建行
            if is_break and is_current:
                row = self._build_break_row(start, end, break_name, is_current=False)
            else:
                row = self._build_class_row(index, subject, teacher, start, end,
                                            is_current=False, is_past=is_past)

            self._scroll_layout.insertWidget(self._scroll_layout.count() - 1, row)
            self._schedule_rows.append(row)

            if is_current:
                last_current_row = row
                last_current_times = (start, end)

        if last_current_row:
            last_current_row.set_current(True)
            self._current_row_data = last_current_times

        self._update_progress()    
    def _connect_timetable_page(self):
        """连接 TimetablePage 获取课表数据"""
        try:
            for w in QApplication.topLevelWidgets():
                if hasattr(w, 'timetablePage'):
                    self._timetable_page = w.timetablePage
                    self._timetable_page.scheduleChanged.connect(self._refresh_schedule)
                    if self._timetable_page.bridge:
                        self._bridge = self._timetable_page.bridge
                        self._bridge.stateChanged.connect(self._on_state_changed)
                        self._bridge.connectedChanged.connect(self._on_connected_changed)
                    logger.info("[Timetable] 已连接 timetablePage")
                    break
        except Exception as e:
            logger.debug(f"[Timetable] 连接失败: {e}")

    def _on_state_changed(self, state):
        self._refresh_schedule()

    def _on_connected_changed(self, connected):
        if connected:
            self._refresh_schedule()

    def _on_timer(self):
        if not self._timetable_page:
            self._connect_timetable_page()

        # 检查是否放学
        if self._bridge:
            try:
                from core.linkage import TimeState 
                state = self._bridge.get_state()
                if state and state.time_state == TimeState.AFTER_SCHOOL:
                    if not self._after_school_mode:
                        self._enter_preview_mode()
                    return 
            except Exception:
                pass

        # 正常模式下刷新今日课表
        self._refresh_schedule()

        # 自动滚动逻辑 有15分钟保护
        past_count = sum(1 for r in self._schedule_rows
                        if isinstance(r, _TimetableRow) and r._is_past)
        target_groups = past_count // 5
        if target_groups > self._past_skip_groups:
            self._past_skip_groups = target_groups
        skip_rows = self._past_skip_groups * 5

        scroll_idx = skip_rows
        for i, r in enumerate(self._schedule_rows):
            if isinstance(r, _TimetableRow) and r._is_current:
                scroll_idx = i
                break
            if isinstance(r, _TimetableRow) and not r._is_past and i >= skip_rows:
                if scroll_idx == skip_rows:
                    scroll_idx = i
                break

        if 0 <= scroll_idx < len(self._schedule_rows):
            if time.time() - self._last_user_scroll_time > 15:
                self._scroll_to_row(scroll_idx)

        self._update_progress()
    def _enter_preview_mode(self):
        self._after_school_mode = True
        self._title_label.setText("明日课表")

        import datetime as _dt
        tomorrow = _dt.date.today() + _dt.timedelta(days=1)
        dotnet_wd = tomorrow.isoweekday() % 7 

        schedule = []
        if self._timetable_page:
            try:
                schedule = self._timetable_page.get_schedule_by_weekday(tomorrow.isoweekday())
            except Exception:
                pass
        elif self._bridge:
            schedule = self._bridge.get_schedule_by_weekday(dotnet_wd)

        self._rebuild_schedule(schedule)
        self._scroll_to_top()

        self._scroll_animation_timer.start(50)
        self._preview_timer.start(30000)
    def _refresh_schedule(self):
        """刷新课表内容"""
        if self._after_school_mode:
            return 

        if self._bridge:
            try:
                from core.linkage import TimeState
                state = self._bridge.get_state()
                if state and state.time_state == TimeState.AFTER_SCHOOL:
                    return
            except Exception:
                pass

        schedule = []
        if self._timetable_page:
            try:
                schedule = self._timetable_page.get_today_schedule()
            except Exception:
                pass

        self._rebuild_schedule(schedule)
    def _build_class_row(self, index, subject, teacher, start, end, is_current, is_past=False):
        """课程行: [第几节] [课程] [老师姓氏]老师 [HH:MM]~[HH:MM]"""
        row = _TimetableRow(is_current=is_current, is_break=False, is_past=is_past, parent=self)
        past_suffix = "Past" if is_past else ""

        # 第几节
        idx_lbl = QLabel(f"第{index}节")
        idx_lbl.setObjectName("timetableIdx" + past_suffix)
        idx_lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        row.addWidget(idx_lbl)

        # 课程名
        subj_lbl = QLabel(subject or "—")
        subj_lbl.setObjectName("timetableSubj" + past_suffix)
        row.addWidget(subj_lbl, 1)

        # 时间
        time_lbl = QLabel(f"{start}~{end}")
        time_lbl.setObjectName("timetableTime" + past_suffix)
        time_lbl.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        row.addWidget(time_lbl)

        return row

    def _build_break_row(self, start, end, break_name, is_current=True):
        row = _TimetableRow(is_current=is_current, is_break=True, parent=self)

        lbl = QLabel(break_name or "课间")
        lbl.setObjectName("timetableBreakLabel")
        lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        row.addWidget(lbl, 1)

        time_lbl = QLabel(f"{start}~{end}")
        time_lbl.setObjectName("timetableTime")
        time_lbl.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        row.addWidget(time_lbl)

        return row

    def _build_empty_row(self):
        """空行"""
        row = _TimetableRow(is_current=False, is_break=False, parent=self)
        lbl = QLabel("今天没有课程")
        lbl.setObjectName("timetableEmpty")
        lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        row.addWidget(lbl, 1)
        return row

    def _update_progress(self):
        """更新当前行的进度条"""
        if not self._current_row_data or not self._schedule_rows:
            return
        start_s, end_s = self._current_row_data
        from datetime import datetime as _dt, time as _time
        now = _dt.now()
        today = now.date()
        try:
            sh, sm = map(int, start_s.split(":"))
            eh, em = map(int, end_s.split(":"))
            start_t = _dt.combine(today, _time(sh, sm))
            end_t = _dt.combine(today, _time(eh, em))
            total = (end_t - start_t).total_seconds()
            if total > 0:
                elapsed = (now - start_t).total_seconds()
                pct = max(0, min(100, elapsed / total * 100))
                for r in self._schedule_rows:
                    if isinstance(r, _TimetableRow) and r._is_current:
                        r.setProgress(pct)
                        return
        except Exception as e:
            logger.debug(f"[Timetable] 进度计算失败: {e}")

    def _scroll_to_row(self, idx, viewport_offset_ratio=0.3):
        """
        滚动到第 idx 行 位于视口上方ratio 处
        """
        if not (0 <= idx < len(self._schedule_rows)):
            return
        row = self._schedule_rows[idx]
        QTimer.singleShot(50, lambda: self._do_scroll(row, viewport_offset_ratio))

    def _do_scroll(self, row, ratio):
        # d老师立大功
        try:
            # 检查 row 是否已被删除
            from PyQt6.QtCore import pyqtSignal
            if not row or not hasattr(row, 'mapTo'):
                return
            # 目标行在 scroll_content 上的 y 坐标
            row_y = row.mapTo(self._scroll_content, QPoint(0, 0)).y()
            sb = self._scroll.verticalScrollBar()
            viewport_h = self._scroll.viewport().height()
            # 让此行在视口上方 ratio 处
            target_y = row_y - int(viewport_h * ratio)
            # 限制范围
            target_y = max(sb.minimum(), min(target_y, sb.maximum()))
            sb.setValue(target_y)
        except RuntimeError:
            # C++ object 已删除，忽略
            return

    def _apply_style(self, *args):
        """跟随组件系统的背景"""
        from core.config import cfg
        opacity = cfg.componentCardOpacity.value / 100.0
        radius = cfg.componentCardRadius.value

        is_dark = isDarkTheme()
        if is_dark:
            # 暗
            row_bg = f"rgba(255, 255, 255, {opacity * 0.08:.3f})"          # 普通行背景
            current_row_bg = f"rgba(255, 255, 255, {opacity * 0.18:.3f})"  # 当前课程行高亮
            past_row_bg = f"rgba(255, 255, 255, {opacity * 0.04:.3f})"     # 已上课淡化
            text = "#e0e0e0"          # 主文字颜色
            text_sub = "#888888"      # 次要文字颜色
            past_text = "#999999"     # 已上课文字颜色
            accent = "#4cc2ff"        # 强调色
            progress_bg = "rgba(255, 255, 255, 0.06)"  # 进度条背景
        else:
            # 亮
            row_bg = f"rgba(0, 0, 0, {opacity * 0.04:.3f})"
            current_row_bg = f"rgba(0, 0, 0, {opacity * 0.10:.3f})"
            past_row_bg = f"rgba(0, 0, 0, {opacity * 0.02:.3f})"
            text = "#1a1a1a"
            text_sub = "#888888"
            past_text = "#999999"
            accent = "#4cc2ff"
            progress_bg = "rgba(0, 0, 0, 0.05)"

        sz_title = self._scaled_px(17)
        sz_idx = self._scaled_px(15)
        sz_subj = self._scaled_px(20)
        sz_time = self._scaled_px(15)
        sz_empty = self._scaled_px(15)

        self.setStyleSheet(f"""
            /* 滚动区域 */
            #timetableScroll {{
                background: transparent;
                border: none;
            }}
            #timetableScrollContent {{
                background: transparent;
            }}

            /* 标题 */
            #timetableTitle {{
                color: {text};
                font-size: {sz_title}px;
                font-weight: 600;
                font-family: {FONT_FAMILY};
                background: transparent;
                padding-bottom: 2px;
            }}

            /* 普通行样式 */
            #timetableRow {{
                background-color: {row_bg};
                border-radius: {radius}px;
                border: none;
            }}
            /* 当前课程行 */
            #timetableRowCurrent {{
                background-color: {current_row_bg};
                border-radius: {radius}px;
                border: none;
            }}
            /* 已过去课程行 */
            #timetableRowPast {{
                background-color: {past_row_bg};
                border-radius: {radius}px;
                border: none;
            }}
            #timetableRowInner {{
                background: transparent;
            }}

            /* 第几节 */
            #timetableIdx {{
                color: {text};
                font-size: {sz_idx}px;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}
            #timetableIdxPast {{
                color: {past_text};
                font-size: {sz_idx}px;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}

            /* 课程名 */
            #timetableSubj {{
                color: {text};
                font-size: {sz_subj}px;
                font-weight: 500;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}
            #timetableSubjPast {{
                color: {past_text};
                font-size: {sz_subj}px;
                font-weight: 500;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}

            /* 时间 */
            #timetableTime {{
                color: {text};
                font-size: {sz_time}px;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}
            #timetableTimePast {{
                color: {past_text};
                font-size: {sz_time}px;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}

            /* 课间休息 */
            #timetableBreakLabel {{
                color: {text};
                font-size: {sz_subj}px;
                font-weight: 500;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}

            /* 空状态 */
            #timetableEmpty {{
                color: {text_sub};
                font-size: {sz_empty}px;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}

            /* 进度条 */
            #timetableProgress {{
                background-color: {progress_bg};
                border: none;
                border-radius: 1px;
            }}
            #timetableProgress::chunk {{
                background-color: {accent};
                border-radius: 1px;
            }}
        """)

    def apply_scale(self, factor):
        self._apply_style()

    def showEvent(self, e):
        super().showEvent(e)
        self._apply_style()



class TimetableNowLessonComponent(DraggableContainer):
    """当前课程组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("nowLessonContainer")
        self._home = parent
        self._set_natural_size(400, 200)
        self.setMinimumSize(120, 80)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        self._bridge = None
        self._timetable_page = None
        self._current_row_data = None
        self._is_prepare_mode = False  # 是否处于课前播报模式

        self._read_config(component_data.get("config", {}))

        self._setup_ui()
        self._connect_timetable_page()

        self._timer = QTimer(self)
        self._timer.timeout.connect(self._refresh)
        self._timer.start(1000)
        self._refresh()

    def apply_config(self, config):
        """应用配置变更"""
        self._read_config(config)
        self._apply_style()
        self._refresh()

    def _read_config(self, config):
        # 哦哦。
        self._show_teacher = config.get("show_teacher", getattr(self, "_show_teacher", True))
        self._show_next = config.get("show_next", getattr(self, "_show_next", True))
        self._show_duration = config.get("show_duration", getattr(self, "_show_duration", True))
        self._show_countdown = config.get("show_countdown", getattr(self, "_show_countdown", True))
        self._prepare_minutes = config.get("prepare_minutes", getattr(self, "_prepare_minutes", 3))
        self._bg_opacity = config.get("bg_opacity", getattr(self, "_bg_opacity", None))
        self._corner_radius = config.get("corner_radius", getattr(self, "_corner_radius", None))
        self._font_scale = config.get("font_scale", getattr(self, "_font_scale", 100))

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setContentsMargins(16, 10, 16, 8)
        layout.setSpacing(0)

        # 左右分栏
        main = QWidget()
        main_layout = QHBoxLayout(main)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(14)

        # 左侧：课程名 倒计时/进度
        left = QWidget()
        left.setMinimumWidth(self._scaled_px(100))
        left.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)
        left_layout = QVBoxLayout(left)
        left_layout.setContentsMargins(0, 0, 0, 0)
        left_layout.setSpacing(4)

        self._subject_label = QLabel("--")
        self._subject_label.setObjectName("miniSubject")
        self._subject_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._subject_label.setWordWrap(True)
        self._subject_label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        left_layout.addWidget(self._subject_label, 3)

        self._countdown_label = QLabel("")
        self._countdown_label.setObjectName("miniCountdown")
        self._countdown_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        left_layout.addWidget(self._countdown_label, 1)

        self._time_progress_label = QLabel("-- min / -- min")
        self._time_progress_label.setObjectName("miniTimeLabel")
        self._time_progress_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        left_layout.addWidget(self._time_progress_label, 1)

        main_layout.addWidget(left, 1)

        # 右侧：老师 时间段 下节课
        right = QWidget()
        right.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)
        right_layout = QVBoxLayout(right)
        right_layout.setContentsMargins(0, 0, 0, 0)
        right_layout.setSpacing(8)

        self._teacher_label = QLabel("--")
        self._teacher_label.setObjectName("miniTeacher")
        self._teacher_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._teacher_label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        right_layout.addWidget(self._teacher_label, 1)

        self._time_label = QLabel("--:-- ~ --:--")
        self._time_label.setObjectName("miniTime")
        self._time_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._time_label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        right_layout.addWidget(self._time_label, 1)

        self._next_label = QLabel("下节课：--")
        self._next_label.setObjectName("miniNext")
        self._next_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._next_label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        right_layout.addWidget(self._next_label, 1)

        right_layout.addStretch()
        main_layout.addWidget(right, 1)

        layout.addWidget(main, 1)

        # 进度条
        self._bottom_progress = QProgressBar(self)
        self._bottom_progress.setFixedHeight(self._scaled_px(6))
        self._bottom_progress.setRange(0, 100)
        self._bottom_progress.setValue(0)
        self._bottom_progress.setTextVisible(False)
        layout.addWidget(self._bottom_progress)

        self._apply_style()

        # 监听主题变化
    def _apply_style(self):
        is_dark = isDarkTheme()

        if is_dark:
            text = "#e0e0e0"
            sub_text = "#aaaaaa"
            accent = "#4cc2ff"
        else:
            text = "#1a1a1a"
            sub_text = "#777777"
            accent = "#4cc2ff"

        # 背景
        self._apply_card_style(
            opacity=self._bg_opacity, radius=self._corner_radius)

        font_scale = self._font_scale / 100.0

        def fs(px):
            return max(1, int(px * font_scale * self._scale_factor))

        # 课程名
        self._subject_label.setStyleSheet(f"""
            color: {text};
            font-size: {fs(40)}px;
            font-weight: 600;
            font-family: {FONT_FAMILY};
            background: transparent;
            padding: 20px 0 4px 0;
        """)

        # 倒计时
        self._countdown_label.setStyleSheet(f"""
            color: {accent};
            font-size: {fs(18)}px;
            font-weight: 600;
            font-family: {FONT_FAMILY};
            background: transparent;
            padding: 2px 0;
            padding-left: 4px;
        """)

        # 时间进度
        self._time_progress_label.setStyleSheet(f"""
            color: {text};
            font-size: {fs(15)}px;
            font-weight: 600;
            font-family: {FONT_FAMILY};
            background: transparent;
            padding: 2px 0;
            padding-left: 4px;
        """)

        # 老师
        self._teacher_label.setStyleSheet(f"""
            color: {text};
            font-size: {fs(19)}px;
            font-weight: 600;
            font-family: {FONT_FAMILY};
            background: transparent;
            padding: 2px 0;
        """)

        # 时间段
        self._time_label.setStyleSheet(f"""
            color: {text};
            font-size: {fs(19)}px;
            font-weight: 600;
            font-family: {FONT_FAMILY};
            background: transparent;
            padding: 2px 0;
        """)

        # 下节课
        self._next_label.setStyleSheet(f"""
            color: {sub_text};
            font-size: {fs(19)}px;
            font-weight: 600;
            font-family: {FONT_FAMILY};
            background: transparent;
            padding: 2px 0;
        """)

    def apply_scale(self, factor):
        # 更新左侧最小宽度并重应用字体样式
        main_item = self.inner_layout.itemAt(0)
        if main_item and main_item.widget():
            left = main_item.widget().findChild(QWidget)
            if left:
                left.setMinimumWidth(self._scaled_px(100))
        self._apply_style()

    def _connect_timetable_page(self):
        try:
            for w in QApplication.topLevelWidgets():
                if hasattr(w, 'timetablePage'):
                    self._timetable_page = w.timetablePage
                    self._timetable_page.scheduleChanged.connect(self._refresh)
                    if self._timetable_page.bridge:
                        self._bridge = self._timetable_page.bridge
                    break
        except Exception:
            pass

    def _refresh(self):
        if not self._timetable_page:
            self._connect_timetable_page()
            if not self._timetable_page:
                return
        try:
            schedule = self._timetable_page.get_today_schedule()
        except Exception:
            return

        now = datetime.datetime.now()

        current_row = None
        next_row = None
        found_current = False
        for row in schedule:
            subject, teacher, start, end, idx, is_current, is_break, break_name = row
            if is_current:
                current_row = row
                found_current = True
            elif found_current and not is_break:
                next_row = row
                break

        # 课前播报
        prepare_row = None  # 要开始的课程行
        if current_row and current_row[6]:  # is_break = True
            if next_row:
                prepare_row = next_row
            else:
                found_curr = False
                for row in schedule:
                    if found_curr and not row[6]:
                        prepare_row = row
                        break
                    if row == current_row:
                        found_curr = True

        in_prepare = False
        if prepare_row and current_row and current_row[6]:  # 课间中有下一节课
            try:
                ps = prepare_row[2]  # start time "HH:MM"
                psh, psm = map(int, ps.split(":"))
                next_start_dt = now.replace(hour=psh, minute=psm, second=0, microsecond=0)
                diff = (next_start_dt - now).total_seconds()
                if 0 < diff <= self._prepare_minutes * 60:
                    in_prepare = True
            except Exception:
                pass

        if in_prepare:
            if self._timer.interval() != 1000:
                self._timer.setInterval(1000)
        else:
            if self._timer.interval() != 5000:
                self._timer.setInterval(5000)

        self._is_prepare_mode = in_prepare

        if in_prepare and prepare_row:
            self._render_prepare_mode(prepare_row, now)
        elif current_row:
            self._render_normal_mode(current_row, next_row, now)
        else:
            self._render_empty()

    def _render_prepare_mode(self, prepare_row, now):
        """课前播报模式"""
        subject, teacher, start, end, idx, is_current, is_break, break_name = prepare_row

        self._subject_label.setText(subject or "下节课")

        try:
            sh, sm = map(int, start.split(":"))
            next_start = now.replace(hour=sh, minute=sm, second=0, microsecond=0)
            diff = next_start - now
            total_sec = max(0, int(diff.total_seconds()))
            m, s = divmod(total_sec, 60)
            if self._show_countdown:
                self._countdown_label.setText(f"{m}:{s:02d} 后上课")
            else:
                self._countdown_label.setText("")
        except Exception:
            self._countdown_label.setText("")

        # 隐藏进度时间
        self._time_progress_label.setText("")

        # 老师
        if self._show_teacher:
            teacher_display = (teacher[0] + "老师") if teacher else ""
            self._teacher_label.setText(teacher_display)
        else:
            self._teacher_label.setText("")

        # 时间段
        self._time_label.setText(f"{start}~{end}")

        # 下节课程时长
        if self._show_duration:
            try:
                sh, sm = map(int, start.split(":"))
                eh, em = map(int, end.split(":"))
                total_min = (eh * 60 + em) - (sh * 60 + sm)
                self._next_label.setText(f"时长：{total_min}分钟")
            except Exception:
                self._next_label.setText("")
        else:
            self._next_label.setText("")

        # 进度条
        try:
            sh, sm = map(int, start.split(":"))
            next_start = now.replace(hour=sh, minute=sm, second=0, microsecond=0)
            diff = (next_start - now).total_seconds()
            total_prepare = self._prepare_minutes * 60
            pct = int(max(0, min(100, (1 - diff / total_prepare) * 100))) if total_prepare > 0 else 0
            self._bottom_progress.setValue(pct)
        except Exception:
            self._bottom_progress.setValue(0)

    def _render_normal_mode(self, current_row, next_row, now):
        """上课中 / 课间"""
        subject, teacher, start, end, idx, is_current, is_break, break_name = current_row

        # 倒计时清空
        self._countdown_label.setText("")

        # 课程名
        if is_break:
            self._subject_label.setText(break_name or "课间休息")
        else:
            self._subject_label.setText(subject or "--")

        # 老师显示
        if self._show_teacher:
            if is_break:
                self._teacher_label.setText("课间休息")
            else:
                teacher_display = (teacher[0] + "老师") if teacher else ""
                self._teacher_label.setText(teacher_display)
        else:
            self._teacher_label.setText("")

        # 时间段
        self._time_label.setText(f"{start}~{end}")

        # 下节课 / 时长
        if self._show_next:
            if next_row:
                next_subject = next_row[0]
                self._next_label.setText(f"下节课：{next_subject}" if next_subject else "下节课：--")
            else:
                self._next_label.setText("下节课：--")
        else:
            self._next_label.setText("")

        # 时间进度
        if self._show_duration:
            try:
                sh, sm = map(int, start.split(":"))
                eh, em = map(int, end.split(":"))
                start_sec = sh * 3600 + sm * 60
                end_sec = eh * 3600 + em * 60
                now_sec = now.hour * 3600 + now.minute * 60 + now.second
                total = end_sec - start_sec
                elapsed = max(0, now_sec - start_sec)
                pct = min(100, int(elapsed / total * 100)) if total > 0 else 0
                elapsed_min = elapsed // 60
                total_min = total // 60
                self._time_progress_label.setText(f"{elapsed_min}min / {total_min}min")
                self._bottom_progress.setValue(pct)
            except Exception:
                self._time_progress_label.setText("-- min / -- min")
                self._bottom_progress.setValue(0)
        else:
            self._time_progress_label.setText("")
            self._bottom_progress.setValue(0)

    def _render_empty(self):
        """无数据"""
        self._subject_label.setText("--")
        self._countdown_label.setText("")
        self._teacher_label.setText("--")
        self._time_label.setText("--:-- ~ --:--")
        self._next_label.setText("下节课：--")
        self._time_progress_label.setText("-- min / -- min")
        self._bottom_progress.setValue(0)

    def showEvent(self, e):
        super().showEvent(e)
        self._apply_style()


class CalculatorComponent(DraggableContainer):
    """计算器"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("calculatorContainer")
        self._home = parent
        self._expression = ""
        self._result_shown = False

        self._setup_ui()
        self._apply_style()

    def _setup_ui(self):
        # 历史表达式行
        self.history_display = QLabel("")
        self.history_display.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        self.history_display.setWordWrap(False)
        self.history_display.setTextFormat(Qt.TextFormat.RichText)
        self.history_display.setObjectName("calculatorHistory")
        self.history_display.setMinimumHeight(24)
        self.history_display.setMaximumHeight(36)
        self.history_display.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Minimum)
        self.history_display.setStyleSheet("color: rgba(255, 255, 255, 0.5); font-size: 14px; background: transparent; border: none; padding: 0 8px;")

        # 显示区
        self.display = QLabel("0")
        self.display.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        self.display.setWordWrap(False)
        self.display.setTextFormat(Qt.TextFormat.RichText)
        self.display.setObjectName("calculatorDisplay")
        self.display.setMinimumHeight(80)
        self.display.setMaximumHeight(140)
        self.display.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Minimum)

        # 按钮网格
        buttons_layout = QGridLayout()
        buttons_layout.setSpacing(8)
        buttons_layout.setContentsMargins(0, 0, 0, 0)

        button_specs = [
            ("⌫", 0, 0), ("C", 0, 1), ("%", 0, 2), ("÷", 0, 3),
            ("7", 1, 0), ("8", 1, 1), ("9", 1, 2), ("×", 1, 3),
            ("4", 2, 0), ("5", 2, 1), ("6", 2, 2), ("−", 2, 3),
            ("1", 3, 0), ("2", 3, 1), ("3", 3, 2), ("+", 3, 3),
            ("±", 4, 0), ("0", 4, 1), (".", 4, 2), ("=", 4, 3),
        ]

        self.buttons = {}
        for text, row, col in button_specs:
            btn = QPushButton(text)
            btn.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
            btn.setObjectName(f"calcBtn_{text}")
            btn.clicked.connect(lambda _, t=text: self._on_button_click(t))
            buttons_layout.addWidget(btn, row, col)
            self.buttons[text] = btn

        # 主布局
        main_layout = self.inner_layout
        main_layout.setContentsMargins(12, 20, 12, 12)
        main_layout.setSpacing(10)
        main_layout.addWidget(self.history_display)
        main_layout.addWidget(self.display)
        main_layout.addLayout(buttons_layout)

        self._set_natural_size(300, 480)
        self.setMinimumSize(160, 240)
        self._size_explicitly_set = True
        self.resize(300, 480)

    def _format_number(self, num_str: str) -> str:
        """格式化"""
        if not num_str:
            return num_str

        if 'e' in num_str or 'E' in num_str:
            return num_str

        is_negative = num_str.startswith('-')
        if is_negative:
            num_str = num_str[1:]

        def as_scientific(value):
            sci = f"{value:.15e}".replace('E', 'e')
            return sci.rstrip('0').rstrip('.') if '.' in sci else sci

        if '.' in num_str:
            integer_part, decimal_part = num_str.split('.', 1)
            if len(integer_part) > 15 or len(integer_part + decimal_part) > 18:
                try:
                    return ('-' if is_negative else '') + as_scientific(float(f"{integer_part}.{decimal_part}"))
                except ValueError:
                    pass
            try:
                integer_part = "{:,}".format(int(integer_part))
            except ValueError:
                return num_str
            formatted = ('-' if is_negative else '') + integer_part + '.' + decimal_part
            return formatted
        else:
            if len(num_str) > 15:
                try:
                    return ('-' if is_negative else '') + as_scientific(int(num_str))
                except ValueError:
                    return num_str
            try:
                integer_part = "{:,}".format(int(num_str))
                return ('-' if is_negative else '') + integer_part
            except ValueError:
                return num_str

    def _prepare_display_expr(self, expression: str) -> str:
        if expression.startswith('-') and re.fullmatch(r'-\d+\.?\d*', expression):
            return expression.replace('-', ' − ', 1)

        display_expr = re.sub(r'(^|[+\-*/])-(\d+\.?\d*)', lambda m: f"{m.group(1)}(-{m.group(2)})", expression)
        display_text = display_expr.replace("/", " ÷ ")
        display_text = display_text.replace("*", " × ")
        display_text = display_text.replace("-", " − ")
        display_text = display_text.replace("+", " + ")
        return display_text

    def _fit_label_font(self, label: QLabel, text: str, base_size: int, min_size: int = 12):
        raw_text = re.sub(r'<[^>]+>', '', text)
        raw_text = raw_text.replace('\r', '').replace('\n', '').replace('<br/>', ' ')
        font = label.font()
        size = base_size
        font.setPixelSize(size)
        if label.width() <= 0:
            label.setFont(font)
            return

        while size >= min_size:
            font.setPixelSize(size)
            metrics = QFontMetrics(font)
            if metrics.horizontalAdvance(raw_text) <= label.width() - 16:
                break
            size -= 1
        label.setFont(font)

    def _split_expression(self):
        if not self._expression:
            return "", ""

        if self._expression.endswith(tuple('+-*/')):
            return self._expression, ""

        match = re.search(r'(.+?[+\-*/])(-?\d*\.?\d*)$', self._expression)
        if match:
            return match.group(1), match.group(2)
        return "", self._expression

    def _apply_style(self):
        opacity = cfg.componentCardOpacity.value / 100.0
        radius = cfg.componentCardRadius.value

        is_dark = isDarkTheme()
        if is_dark:
            border_color = "rgba(255,255,255,0.05)"
            display_text = "#ffffff"
            btn_num_color = QColor(51, 51, 51)
            btn_num_press_color = QColor(77, 77, 77)
            btn_op_color = QColor(255, 159, 10)
            btn_op_text = "#ffffff"
        else:
            border_color = "rgba(0,0,0,0.05)"
            display_text = "#000000"
            btn_num_color = QColor(229, 229, 234)
            btn_num_press_color = QColor(209, 209, 214)
            btn_op_color = QColor(255, 159, 10)
            btn_op_text = "#ffffff"

        button_opacity = min(1.0, opacity * 0.8 + 0.15)
        press_opacity = min(1.0, opacity * 0.95 + 0.1)
        btn_op_alpha = max(0.55, min(1.0, opacity * 0.9 + 0.1))

        btn_num_color.setAlpha(int(255 * button_opacity))
        btn_num_press_color.setAlpha(int(255 * press_opacity))
        btn_op_color.setAlpha(int(255 * btn_op_alpha))

        btn_num_bg = f"rgba({btn_num_color.red()}, {btn_num_color.green()}, {btn_num_color.blue()}, {btn_num_color.alpha() / 255:.2f})"
        btn_num_press = f"rgba({btn_num_press_color.red()}, {btn_num_press_color.green()}, {btn_num_press_color.blue()}, {btn_num_press_color.alpha() / 255:.2f})"
        btn_op_bg = f"rgba({btn_op_color.red()}, {btn_op_color.green()}, {btn_op_color.blue()}, {btn_op_color.alpha() / 255:.2f})"

        bg_css = self._card_bg_css()
        self.setStyleSheet(f"""
            {bg_css}
            #calculatorContainer {{ border: 1px solid {border_color}; }}
        """)

        sz_display = self._scaled_px(24)
        sz_op = self._scaled_px(28)
        sz_num = self._scaled_px(24)
        sz_hist = self._scaled_px(14)

        self.display.setTextFormat(Qt.TextFormat.RichText)
        self.display.setStyleSheet(f"""
            color: {display_text};
            font-size: {sz_display}px;
            font-family: {FONT_FAMILY};
            font-weight: 300;
            background-color: transparent;
            border: none;
            padding: 6px 8px;
            line-height: 1.2;
            white-space: nowrap;
        """)

        self.history_display.setStyleSheet(f"color: rgba(255, 255, 255, 0.5); font-size: {sz_hist}px; background: transparent; border: none; padding: 0 8px;")

        operator_keys = {"÷", "×", "−", "+", "="}
        for text, btn in self.buttons.items():
            if text in operator_keys:
                btn.setStyleSheet(f"""
                    QPushButton {{
                        color: {btn_op_text};
                        font-size: {sz_op}px;
                        font-family: {FONT_FAMILY};
                        background-color: {btn_op_bg};
                        border: none;
                        border-radius: {radius}px;
                    }}
                    QPushButton:pressed {{
                        background-color: rgba(255, 159, 10, 0.7);
                    }}
                """)
            else:
                btn.setStyleSheet(f"""
                    QPushButton {{
                        color: #ffffff;
                        font-size: {sz_num}px;
                        font-family: {FONT_FAMILY};
                        background-color: {btn_num_bg};
                        border: none;
                        border-radius: {radius}px;
                    }}
                    QPushButton:pressed {{
                        background-color: {btn_num_press};
                    }}
                """)

        self.updateSize()

    def apply_scale(self, factor):
        self._apply_style()

    def _current_number_has_decimal(self) -> bool:
        match = re.search(r'(-?\d+\.?\d*)$', self._expression)
        return bool(match and "." in match.group(1))

    def _current_number_length(self) -> int:
        match = re.search(r'(-?\d+\.?\d*)$', self._expression)
        if not match:
            return 0
        number = match.group(1).lstrip('-')
        return len(number.replace('.', ''))

    def _append_operator(self, operator: str):
        if not self._expression:
            if operator == "-":
                self._expression = "-"
            return

        if self._expression[-1] in "+-*/":
            if operator == "-" and self._expression[-1] in "+*/":
                self._expression += "-"
            else:
                self._expression = re.sub(r'[+\-*/]+$', "", self._expression) + operator
        else:
            self._expression += operator

    # 计算逻辑
    def _on_button_click(self, key):
        # 用的全是这种图标 哪天改成fluent的
        calc_key = key
        if key == "÷":
            calc_key = "/"
        elif key == "×":
            calc_key = "*"
        elif key == "−":
            calc_key = "-"

        if key == "C":
            self._expression = ""
            self.display.setText("0")
            self._result_shown = False
            return

        if key == "⌫":
            if self._result_shown:
                self._expression = ""
                self._result_shown = False
            else:
                self._expression = self._expression[:-1]
            self._update_display()
            return

        if key == "=":
            self._calculate()
            return

        if key == "±":
            self._toggle_sign()
            return

        if key == "%":
            self._apply_percent()
            return

        if key == ".":
            if self._result_shown:
                self._expression = "0."
                self._result_shown = False
            elif not self._current_number_has_decimal():
                if not self._expression or self._expression[-1] in "+-*/":
                    self._expression += "0."
                else:
                    self._expression += "."
            self._update_display()
            return

        if key.isdigit():
            if self._result_shown:
                self._expression = calc_key
                self._result_shown = False
                self._update_display()
                return

            if self._current_number_length() >= 15:
                return

            self._expression += calc_key
            self._update_display()
            return

        if calc_key in "+-*/":
            self._append_operator(calc_key)
            self._result_shown = False
            self._update_display()
            return

        self._expression += calc_key
        self._update_display()

    def _update_display(self):
        """刷新显示内容"""
        if not self._expression:
            self.history_display.setText("")
            self.display.setText("0")
            self._fit_label_font(self.display, "0", 24, 12)
            return

        head, current = self._split_expression()
        if head:
            self.history_display.setText(f'<span style="color: rgba(255,255,255,0.5);">{self._prepare_display_expr(head)}</span>')
            self._fit_label_font(self.history_display, self.history_display.text(), 14, 10)
        else:
            self.history_display.setText("")

        if not current:
            if self._result_shown:
                return
            self.display.setText("0")
            self._fit_label_font(self.display, "0", 24, 12)
            return

        display_text = self._prepare_display_expr(current)
        self.display.setText(display_text)
        self._fit_label_font(self.display, display_text, 24, 12)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self._update_display()

    def _calculate(self):
        """表达式计算并显示结果"""
        if not self._expression:
            return

        expr = self._expression.rstrip('+-*/')
        expr = re.sub(r'\.$', '', expr)
        if not expr:
            self.display.setText("0")
            self._expression = ""
            self._result_shown = False
            return

        try:
            expr = re.sub(r'(-?\d+\.?\d*)%', r'(\1/100)', expr)
            result = eval(expr)

            if isinstance(result, float) and result.is_integer():
                result = int(result)
            elif isinstance(result, float):
                result = round(result, 10)

            result_str = str(result)
            self.history_display.setText(f'<span style="color: rgba(255,255,255,0.5);">{self._prepare_display_expr(expr)} =</span>')
            self.display.setText(self._format_number(result_str))
            self._expression = result_str
            self._result_shown = True
            self._fit_label_font(self.display, self.display.text(), 24, 12)
            self._fit_label_font(self.history_display, self.history_display.text(), 14, 10)
        except Exception as e:
            logger.error(f"计算错误: {e}")
            self.display.setText("错误")
            self._expression = ""
            self._result_shown = False

    def _toggle_sign(self):
        """切换正负"""
        if not self._expression:
            return
        if self._expression[-1] in '+-*/':
            return

        match = re.search(r'([+\-*/])(-?\d+\.?\d*)$', self._expression)
        if match:
            start, number = match.start(2), match.group(2)
            if number.startswith('-'):
                number = number[1:]
            else:
                number = '-' + number
            self._expression = self._expression[:start] + number
        else:
            if self._expression.startswith('-'):
                self._expression = self._expression[1:]
            else:
                self._expression = '-' + self._expression

        self._update_display()

    def _apply_percent(self):
        """转换为百分数"""
        m = re.search(r'(-?\d+\.?\d*)$', self._expression)
        if m:
            n = m.group(1)
            self._expression = self._expression[:m.start()] + f"({n}/100)"
            self._update_display()


class _OverToolBtn(QWidget):
    """按钮"""

    clicked = pyqtSignal()

    def __init__(self, icon_pm: QPixmap, text: str, parent=None):
        super().__init__(parent)
        self.setFixedSize(56, 72)
        self._checked = False
        layout = QVBoxLayout(self)
        layout.setContentsMargins(2, 4, 2, 2)
        layout.setSpacing(2)

        self._icon_label = QLabel(self)
        self._icon_label.setPixmap(icon_pm)
        self._icon_label.setFixedSize(28, 28)
        self._icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self._icon_label, 0, Qt.AlignmentFlag.AlignCenter)

        self._text_label = QLabel(text, self)
        self._text_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._text_label.setStyleSheet("font-size:11px;color:#cccccc;border:none;background:transparent;")
        layout.addWidget(self._text_label, 0, Qt.AlignmentFlag.AlignCenter)

        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self._update_style()

    def _update_style(self):
        if self._checked:
            self.setStyleSheet(
                "_OverToolBtn{background:rgba(0,95,184,180);border-radius:6px;}"
            )
            self._text_label.setStyleSheet("font-size:11px;color:#ffffff;border:none;background:transparent;")
        else:
            self.setStyleSheet(
                "_OverToolBtn{background:rgba(60,60,60,160);border-radius:6px;}"
            )
            self._text_label.setStyleSheet("font-size:11px;color:#aaaaaa;border:none;background:transparent;")

    def setChecked(self, v: bool):
        self._checked = v
        self._update_style()

    def isChecked(self):
        return self._checked

    def mousePressEvent(self, event):
        self.clicked.emit()
        super().mousePressEvent(event)


# RealTimeStylus COM 定义

# RTS CLSID & IIDs
CLSID_RealTimeStylus = GUID("{E5757EA0-CEFB-11D2-A5D7-0000F8054F0C}")
IID_IRealTimeStylus = GUID("{A3DC27C8-269D-4E43-B0F9-78F5C114FE4B}")
IID_IRealTimeStylus2 = GUID("{A64CB03D-8E2E-4A4B-BD5C-6A9C90B99474}")
IID_IRealTimeStylus3 = GUID("{B9F17D8A-DD08-4E22-B0B0-D5905F7CF82C}")
IID_IStylusSyncPlugin = GUID("{D67A9CDA-C2D9-4C8D-BF6F-560DB4254E40}")

# 数据包属性 GUID
GUID_X = GUID("{598A6A85-3C4F-4E50-AF0E-9C42B42A4888}")
GUID_Y = GUID("{B9F0B728-BF01-45F4-91CC-3640C02B059A}")
GUID_NORMAL_PRESSURE = GUID("{4B2F21EA-0DF3-4E43-9F6B-C6D41DEB0C1A}")
GUID_WIDTH = GUID("{7359E68C-0C0D-4392-B2F1-9D17CF61ECF6}")
GUID_HEIGHT = GUID("{4803AC82-FA13-4F5D-86B0-50591453E2AB}")

# RealTimeStylus 数据兴趣标志
RTSDI_StylusDown = 0x00000020
RTSDI_Packets = 0x00000080
RTSDI_StylusUp = 0x00000010

# HRESULT 在部分中缺失
if not hasattr(wintypes, 'HRESULT'):
    wintypes.HRESULT = wintypes.LONG

S_OK = 0
E_NOINTERFACE = 0x80004002

# IID_IUnknown
IID_IUnknown = GUID("{00000000-0000-0000-C000-000000000046}")

# SUCCEEDED / FAILED 宏
def SUCCEEDED(hr):
    return hr >= 0
def FAILED(hr):
    return hr < 0

# WM_POINTER 常量与结构──
WM_POINTERDOWN   = 0x0246
WM_POINTERUPDATE = 0x0245
WM_POINTERUP     = 0x0247

POINTER_FLAG_NEW      = 0x00000001
POINTER_FLAG_INRANGE  = 0x00000002
POINTER_FLAG_INCONTACT = 0x00000004
POINTER_FLAG_FIRSTBUTTON = 0x00000010
POINTER_FLAG_SECONDBUTTON = 0x00000020
POINTER_FLAG_PRIMARY  = 0x00000040
POINTER_FLAG_CONFIDENCE = 0x00000080
POINTER_FLAG_CANCELLED = 0x00000100
POINTER_FLAG_DOWN     = 0x00010000
POINTER_FLAG_UPDATE   = 0x00020000
POINTER_FLAG_UP       = 0x00040000

class _POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]

PT_TOUCH = 2

_GetPointerType = ctypes.windll.user32.GetPointerType
_GetPointerType.restype = wintypes.BOOL
_GetPointerType.argtypes = [ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32)]

# WM_POINTER 常量与结构

# StylusInfo 结构
class _StylusInfo(ctypes.Structure):
    _fields_ = [
        ("tcid", wintypes.UINT),
        ("cid", wintypes.UINT),
        ("bIsInvertedCursor", wintypes.BOOL),
    ]

class _PropertyMetrics(ctypes.Structure):
    _fields_ = [
        ("nLogicalMin", wintypes.UINT),
        ("nLogicalMax", wintypes.UINT),
        ("nUnits", wintypes.UINT),
        ("fResolution", ctypes.c_float),
    ]

class _PacketProperty(ctypes.Structure):
    _fields_ = [
        ("guid", GUID),
        ("metrics", _PropertyMetrics),
    ]

# IStylusSyncPlugin 回调

# 注册表：COM 对象地址 到 _WritingOverlay 实例
_rts_plugin_registry = {}
_rts_plugin_lock = threading.Lock()

# 事件队列：RTS 线程 到 Qt 主线程
_rts_event_queue = deque()
_rts_event_lock = threading.Lock()

def _rts_push_event(ev_type, x, y, cid):
    """推送触控事件到队列"""
    with _rts_event_lock:
        _rts_event_queue.append((ev_type, x, y, cid))

# IStylusSyncPlugin 回调函数

# StylusInfo 指针解引用辅助
def _rts_read_stylus_info(p_stylus_info):
    si = ctypes.cast(p_stylus_info, ctypes.POINTER(_StylusInfo))
    return si.contents

# 从 LONG* 包数据中提取第 nth 个 LONG
def _rts_packet_long(packet, index):
    arr = ctypes.cast(packet, ctypes.POINTER(wintypes.LONG))
    return arr[index]

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)
def _plugin_QueryInterface(self_ptr, riid, ppv_out):
    """IUnknown::QueryInterface — 只暴露 IStylusSyncPlugin 和 IUnknown"""
    ppv = ctypes.cast(ppv_out, ctypes.POINTER(ctypes.c_void_p))
    with _rts_plugin_lock:
        ctx = _rts_plugin_registry.get(self_ptr)
        if not ctx:
            ppv[0] = None
            return 0x80004003  # E_POINTER
    riid_buf = ctypes.string_at(riid, 16)
    riid_g = GUID(bytes=riid_buf)
    if riid_g == IID_IStylusSyncPlugin or riid_g == IID_IUnknown:
        obj = ctypes.cast(self_ptr, ctypes.POINTER(_RtsPluginObject))
        obj.contents.refcount += 1
        ppv[0] = self_ptr
        return S_OK
    ppv[0] = None
    return E_NOINTERFACE

@ctypes.WINFUNCTYPE(wintypes.ULONG, ctypes.c_void_p)
def _plugin_AddRef(self_ptr):
    obj = ctypes.cast(self_ptr, ctypes.POINTER(_RtsPluginObject))
    obj.contents.refcount += 1
    return obj.contents.refcount

@ctypes.WINFUNCTYPE(wintypes.ULONG, ctypes.c_void_p)
def _plugin_Release(self_ptr):
    obj = ctypes.cast(self_ptr, ctypes.POINTER(_RtsPluginObject))
    obj.contents.refcount -= 1
    ref = obj.contents.refcount
    if ref == 0:
        with _rts_plugin_lock:
            _rts_plugin_registry.pop(self_ptr, None)
    return ref

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, ctypes.c_void_p)
def _plugin_RealTimeStylusEnabled(self_ptr, piRtsSrc, cTcid, pTcids):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, ctypes.c_void_p)
def _plugin_RealTimeStylusDisabled(self_ptr, piRtsSrc, cTcid, pTcids):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, wintypes.UINT)
def _plugin_StylusInRange(self_ptr, piRtsSrc, tcid, cid):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, wintypes.UINT)
def _plugin_StylusOutOfRange(self_ptr, piRtsSrc, tcid, cid):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, ctypes.c_void_p, ctypes.c_void_p)
def _plugin_StylusDown(self_ptr, piRtsSrc, pStylusInfo, cPktCount, pPacket, ppInOutPkts):
    """StylusDown — 触摸按下"""
    si = _rts_read_stylus_info(pStylusInfo)
    with _rts_plugin_lock:
        ctx = _rts_plugin_registry.get(self_ptr)
    if ctx is None:
        return S_OK
    
    overlay = ctx[0]  # ctx = (overlay, obj)
    scale_x = ctypes.c_float(1.0)
    scale_y = ctypes.c_float(1.0)
    try:
        # 获取 inkToDeviceScale 缓存在 overlay 上
        rts = overlay._rts
        if rts:
            cProps = wintypes.UINT(0)
            ppProps = ctypes.c_void_p(0)
            hr = rts.GetPacketDescriptionData(si.tcid,
                ctypes.byref(scale_x), ctypes.byref(scale_y),
                ctypes.byref(cProps), ctypes.byref(ppProps))
            if SUCCEEDED(hr):
                overlay._rts_scale_x = scale_x.value
                overlay._rts_scale_y = scale_y.value
                if ppProps.value:
                    ctypes.windll.ole32.CoTaskMemFree(ppProps.value)
    except Exception:
        pass
    
    x = _rts_packet_long(pPacket, 0) * scale_x.value
    y = _rts_packet_long(pPacket, 1) * scale_y.value
    _rts_push_event("down", x, y, si.cid)
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, ctypes.c_void_p, ctypes.c_void_p)
def _plugin_StylusUp(self_ptr, piRtsSrc, pStylusInfo, cPktCount, pPacket, ppInOutPkts):
    """StylusUp — 触摸抬起"""
    si = _rts_read_stylus_info(pStylusInfo)
    with _rts_plugin_lock:
        ctx = _rts_plugin_registry.get(self_ptr)
    if ctx is None:
        return S_OK
    _rts_push_event("up", 0.0, 0.0, si.cid)
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, wintypes.UINT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)
def _plugin_Packets(self_ptr, piRtsSrc, pStylusInfo, cPktCount, cPktBuffLength, pPacket, pcInOutPkts, ppInOutPkts):
    """Packets — 触摸移动"""
    si = _rts_read_stylus_info(pStylusInfo)
    with _rts_plugin_lock:
        ctx = _rts_plugin_registry.get(self_ptr)
    if ctx is None:
        return S_OK
    
    overlay = ctx[0]
    try:
        sx = overlay._rts_scale_x if hasattr(overlay, '_rts_scale_x') else 1.0
        sy = overlay._rts_scale_y if hasattr(overlay, '_rts_scale_y') else 1.0
    except Exception:
        sx = sy = 1.0
    
    x = _rts_packet_long(pPacket, 0) * sx
    y = _rts_packet_long(pPacket, 1) * sy
    _rts_push_event("move", x, y, si.cid)
    return S_OK

# 其余 IStylusSyncPlugin 返回 S_OK
@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, ctypes.c_void_p, ctypes.c_void_p)
def _plugin_StylusButtonUp(self_ptr, piRtsSrc, stylusId, pGuid, pPt):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, ctypes.c_void_p, ctypes.c_void_p)
def _plugin_StylusButtonDown(self_ptr, piRtsSrc, stylusId, pGuid, pPt):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, wintypes.UINT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)
def _plugin_InAirPackets(self_ptr, piRtsSrc, pStylusInfo, cPktCount, cPktBuffLength, pPacket, pcInOutPkts, ppInOutPkts):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, wintypes.UINT, wintypes.UINT, ctypes.c_void_p)
def _plugin_SystemEvent(self_ptr, piRtsSrc, tcid, cid, event, data):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)
def _plugin_TabletAdded(self_ptr, piRtsSrc, pTablet):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, wintypes.LONG)
def _plugin_TabletRemoved(self_ptr, piRtsSrc, lid):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT, ctypes.c_void_p)
def _plugin_CustomStylusDataAdded(self_ptr, piRtsSrc, pGuid, data, pData):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, wintypes.HRESULT, ctypes.c_void_p)
def _plugin_Error(self_ptr, piRtsSrc, pPlugin, dataInterest, hrError, ptr):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p)
def _plugin_UpdateMapping(self_ptr, piRtsSrc):
    return S_OK

@ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p)
def _plugin_DataInterest(self_ptr, pDataInterest):
    """DataInterest — 声明感兴趣的事件类型"""
    di = ctypes.cast(pDataInterest, ctypes.POINTER(wintypes.UINT))
    di[0] = RTSDI_StylusDown | RTSDI_Packets | RTSDI_StylusUp
    return S_OK

# IStylusSyncPlugin vtable 表
_rts_plugin_vtable = (ctypes.c_void_p * 20)(
    ctypes.cast(_plugin_QueryInterface, ctypes.c_void_p),
    ctypes.cast(_plugin_AddRef, ctypes.c_void_p),
    ctypes.cast(_plugin_Release, ctypes.c_void_p),
    ctypes.cast(_plugin_RealTimeStylusEnabled, ctypes.c_void_p),
    ctypes.cast(_plugin_RealTimeStylusDisabled, ctypes.c_void_p),
    ctypes.cast(_plugin_StylusInRange, ctypes.c_void_p),
    ctypes.cast(_plugin_StylusOutOfRange, ctypes.c_void_p),
    ctypes.cast(_plugin_StylusDown, ctypes.c_void_p),
    ctypes.cast(_plugin_StylusUp, ctypes.c_void_p),
    ctypes.cast(_plugin_StylusButtonUp, ctypes.c_void_p),
    ctypes.cast(_plugin_StylusButtonDown, ctypes.c_void_p),
    ctypes.cast(_plugin_InAirPackets, ctypes.c_void_p),
    ctypes.cast(_plugin_Packets, ctypes.c_void_p),
    ctypes.cast(_plugin_SystemEvent, ctypes.c_void_p),
    ctypes.cast(_plugin_TabletAdded, ctypes.c_void_p),
    ctypes.cast(_plugin_TabletRemoved, ctypes.c_void_p),
    ctypes.cast(_plugin_CustomStylusDataAdded, ctypes.c_void_p),
    ctypes.cast(_plugin_Error, ctypes.c_void_p),
    ctypes.cast(_plugin_UpdateMapping, ctypes.c_void_p),
    ctypes.cast(_plugin_DataInterest, ctypes.c_void_p),
)

class _RtsPluginObject(ctypes.Structure):
    """COM 对象布局：vtable 指针 refcount"""
    _fields_ = [
        ("lpVtbl", ctypes.POINTER(ctypes.c_void_p)),
        ("refcount", ctypes.c_long),
    ]

def _rts_create_plugin(overlay):
    """创建IStylusSyncPlugin COM 对象返回其地址指针值"""
    obj = _RtsPluginObject()
    obj.lpVtbl = ctypes.pointer(_rts_plugin_vtable)
    obj.refcount = 1
    addr = ctypes.addressof(obj)
    # obj 保存在注册表中
    with _rts_plugin_lock:
        _rts_plugin_registry[addr] = (overlay, obj)
    return addr

# IRealTimeStylus COM 接口辅助

# IClassFactory IID
_ICLASS_FACTORY_IID = GUID("{00000001-0000-0000-C000-000000000046}")
# RTS WinSxS 搜索模式（64-bit）
_WIN32_WINNT_RTS_WXSXS = "C:\\Windows\\WinSxS\\amd64_microsoft-windows-t..platform-comruntime*\\rtscom.dll"

def _find_rtscom():
    """查找rtscom.dll"""
    try:
        import glob as _glob
        paths = [p for p in _glob.glob(_WIN32_WINNT_RTS_WXSXS)
                 if '\\r\\' not in p and '\\r\\' not in p]
        if paths:
            # 按版本号排序取最新
            paths.sort(key=lambda p: _extract_version(p), reverse=True)
            logger.info("[RTS] 选择 rtscom.dll: %s", paths[0])
            return paths[0]
        logger.warning("[RTS] 未找到非-stub 的 rtscom.dll, 全部路径: %s",
                       _glob.glob(_WIN32_WINNT_RTS_WXSXS))
    except Exception as e:
        logger.warning("[RTS] _find_rtscom 异常: %s", e)
    return None

def _extract_version(path):
    """取版本号"""
    import re as _re
    m = _re.search(r'_10\.(\d+\.\d+\.\d+)_', path)
    if m:
        parts = m.group(1).split('.')
        return tuple(int(x) for x in parts)
    return (0, 0, 0)

def _find_manifest(dll_path):
    """根据 rtscom.dll 找WinSxS manifest"""
    try:
        # 路径格式: ...\amd64_..._<hash>\rtscom.dll
        import os as _os
        dir_name = _os.path.basename(_os.path.dirname(dll_path))
        manifest_name = dir_name + ".manifest"
        manifest_path = _os.path.join(
            r"C:\Windows\WinSxS\Manifests", manifest_name)
        if _os.path.exists(manifest_path):
            return manifest_path
    except Exception:
        pass
    return None


def _rts_create(hwnd):
    """加载 rtscom.dll 创建 IRealTimeStylus"""
    try:
        # 在 WinSxS 中找到 rtscom.dll
        dll_path = _find_rtscom()
        if not dll_path:
            logger.warning("[RTS] 未在 WinSxS 中找到 rtscom.dll")
            return None

        kernel32 = ctypes.windll.kernel32

        # 创建 Activation Context 让 WinSxS assembly 注册 COM 类
        manifest_path = _find_manifest(dll_path)
        act_ctx = None
        cookie = None
        if manifest_path:
            class ACTCTX(ctypes.Structure):
                _fields_ = [
                    ("cbSize", wintypes.DWORD),
                    ("dwFlags", wintypes.DWORD),
                    ("lpSource", wintypes.LPCWSTR),
                    ("wProcessorArchitecture", wintypes.WORD),
                    ("wLangId", wintypes.WORD),
                    ("lpAssemblyDirectory", wintypes.LPCWSTR),
                    ("lpResourceName", wintypes.LPCWSTR),
                    ("lpApplicationName", wintypes.LPCWSTR),
                    ("hModule", wintypes.HMODULE),
                ]
            actctx = ACTCTX()
            actctx.cbSize = ctypes.sizeof(ACTCTX)
            actctx.dwFlags = 0
            actctx.lpSource = manifest_path

            kernel32.CreateActCtxW.restype = ctypes.c_void_p
            kernel32.CreateActCtxW.argtypes = [ctypes.POINTER(ACTCTX)]
            act_ctx = kernel32.CreateActCtxW(ctypes.byref(actctx))
            if act_ctx and act_ctx != ctypes.c_void_p(-1).value:
                # ActivateActCtx 返回 cookie 用于后续 DeactivateActCtx
                cookie = ctypes.c_ulong_ptr(0)
                kernel32.ActivateActCtx.restype = wintypes.BOOL
                kernel32.ActivateActCtx.argtypes = [
                    ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong_ptr)]
                if not kernel32.ActivateActCtx(act_ctx, ctypes.byref(cookie)):
                    cookie = None
            else:
                act_ctx = None
        else:
            logger.warning("[RTS] 未找到匹配的 manifest 文件")

        # 加载 rtscom.dll
        kernel32.LoadLibraryExW.restype = wintypes.HMODULE
        kernel32.LoadLibraryExW.argtypes = [wintypes.LPCWSTR, wintypes.HANDLE, wintypes.DWORD]
        hmod = kernel32.LoadLibraryExW(dll_path, None, 0)
        if not hmod:
            logger.warning("[RTS] LoadLibraryExW 失败, err=%d",
                          ctypes.windll.kernel32.GetLastError())
            if cookie:
                kernel32.DeactivateActCtx.restype = wintypes.BOOL
                kernel32.DeactivateActCtx.argtypes = [wintypes.DWORD, ctypes.c_ulong_ptr]
                kernel32.DeactivateActCtx(0, cookie)
            if act_ctx:
                kernel32.ReleaseActCtx(ctypes.c_void_p(act_ctx))
            return None

        # 获取 DllGetClassObject
        kernel32.GetProcAddress.restype = ctypes.c_void_p
        kernel32.GetProcAddress.argtypes = [wintypes.HMODULE, ctypes.c_char_p]
        dll_get_class_obj = kernel32.GetProcAddress(hmod, b"DllGetClassObject")
        if not dll_get_class_obj:
            logger.warning("[RTS] GetProcAddress(DllGetClassObject) 失败, err=%d",
                          ctypes.windll.kernel32.GetLastError())
            kernel32.FreeLibrary.restype = wintypes.BOOL
            kernel32.FreeLibrary.argtypes = [wintypes.HMODULE]
            kernel32.FreeLibrary(hmod)
            if cookie:
                kernel32.DeactivateActCtx(0, cookie)
            if act_ctx:
                kernel32.ReleaseActCtx(ctypes.c_void_p(act_ctx))
            return None

        # DllGetClassObject(rclsid, riid, ppv)
        DllGetClassObjectType = ctypes.WINFUNCTYPE(
            wintypes.HRESULT,
            ctypes.POINTER(GUID),           # REFCLSID
            ctypes.POINTER(GUID),           # REFIID
            ctypes.POINTER(ctypes.c_void_p) # void**
        )
        fn_dll_get_class_obj = ctypes.cast(dll_get_class_obj, DllGetClassObjectType)

        # 获取 IClassFactory
        factory_ptr = ctypes.c_void_p(0)
        hr = fn_dll_get_class_obj(
            ctypes.byref(CLSID_RealTimeStylus),
            ctypes.byref(_ICLASS_FACTORY_IID),
            ctypes.byref(factory_ptr))

        if hr != S_OK or not factory_ptr.value:
            logger.warning("[RTS] DllGetClassObject 失败: HRESULT=0x%08X", hr & 0xFFFFFFFF)
            kernel32.FreeLibrary.restype = wintypes.BOOL
            kernel32.FreeLibrary.argtypes = [wintypes.HMODULE]
            kernel32.FreeLibrary(hmod)
            if cookie:
                kernel32.DeactivateActCtx(0, cookie)
            if act_ctx:
                kernel32.ReleaseActCtx(ctypes.c_void_p(act_ctx))
            return None

        # IClassFactory::CreateInstance(pUnkOuter, riid, ppv) — vtable slot 3
        factory_vtable = ctypes.cast(factory_ptr.value, ctypes.POINTER(ctypes.c_void_p))
        CreateInstanceType = ctypes.WINFUNCTYPE(
            wintypes.HRESULT,
            ctypes.c_void_p,                # this
            ctypes.c_void_p,                # pUnkOuter (NULL)
            ctypes.POINTER(GUID),           # riid
            ctypes.POINTER(ctypes.c_void_p) # ppv
        )
        fn_create_instance = ctypes.cast(factory_vtable[3], CreateInstanceType)

        rts_ptr = ctypes.c_void_p(0)
        hr = fn_create_instance(
            factory_ptr.value,
            None,                           # pUnkOuter = NULL
            ctypes.byref(IID_IRealTimeStylus),
            ctypes.byref(rts_ptr))

        # 释放 IClassFactory
        ReleaseType = ctypes.WINFUNCTYPE(wintypes.ULONG, ctypes.c_void_p)
        fn_release = ctypes.cast(factory_vtable[2], ReleaseType)
        fn_release(factory_ptr.value)

        if hr != S_OK or not rts_ptr.value:
            logger.warning("[RTS] CreateInstance 失败: HRESULT=0x%08X", hr & 0xFFFFFFFF)
            kernel32.FreeLibrary.restype = wintypes.BOOL
            kernel32.FreeLibrary.argtypes = [wintypes.HMODULE]
            kernel32.FreeLibrary(hmod)
            if cookie:
                kernel32.DeactivateActCtx(0, cookie)
            if act_ctx:
                kernel32.ReleaseActCtx(ctypes.c_void_p(act_ctx))
            return None

        # 释放激活上下文（DLL 已加载，COM 服务器已运行，清理上下文）
        if cookie:
            kernel32.DeactivateActCtx.restype = wintypes.BOOL
            kernel32.DeactivateActCtx.argtypes = [wintypes.DWORD, ctypes.c_ulong_ptr]
            kernel32.DeactivateActCtx(0, cookie)
        if act_ctx:
            kernel32.ReleaseActCtx(ctypes.c_void_p(act_ctx))

        wrapper = _RealTimeStylusWrapper(rts_ptr.value)
        wrapper._hmod = hmod  # 保持 DLL 在内存中不卸载
        logger.info("[RTS] 成功创建 RTS 对象 (WinSxS)")
        return wrapper

    except Exception as e:
        logger.warning("[RTS] 创建异常: %s", e)
        import traceback
        logger.warning("[RTS] traceback: %s", traceback.format_exc())
        return None

class _RealTimeStylusWrapper:
    """IRealTimeStylus 的轻量级 ctypes 包装"""
    def __init__(self, ptr):
        self.ptr = ptr
        ppv = ctypes.cast(ptr, ctypes.POINTER(ctypes.c_void_p))
        self.vtable = ctypes.cast(ppv[0], ctypes.POINTER(ctypes.c_void_p))

    def put_HWND(self, hwnd):
        func_type = ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, wintypes.HANDLE)
        func = ctypes.cast(self.vtable[6], func_type)
        return func(self.ptr, hwnd)

    def put_Enabled(self, enabled):
        func_type = ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, wintypes.BOOL)
        func = ctypes.cast(self.vtable[8], func_type)
        return func(self.ptr, enabled)

    def AddStylusSyncPlugin(self, index, plugin_ptr):
        func_type = ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, wintypes.UINT, ctypes.c_void_p)
        func = ctypes.cast(self.vtable[10], func_type)
        return func(self.ptr, index, plugin_ptr)

    def SetDesiredPacketDescription(self, count, guids_ptr):
        func_type = ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, wintypes.UINT, ctypes.c_void_p)
        func = ctypes.cast(self.vtable[26], func_type)
        return func(self.ptr, count, guids_ptr)

    def GetPacketDescriptionData(self, tcid, pScaleX, pScaleY, pPropCount, ppProps):
        func_type = ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, wintypes.UINT,
            ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)
        func = ctypes.cast(self.vtable[25], func_type)
        return func(self.ptr, tcid, pScaleX, pScaleY, pPropCount, ppProps)

    def GetAllTabletContextIds(self, pCount, ppTcids):
        func_type = ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)
        func = ctypes.cast(self.vtable[24], func_type)
        return func(self.ptr, pCount, ppTcids)

    def GetTabletFromTabletContextId(self, tcid, ppTablet):
        func_type = ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, wintypes.UINT, ctypes.c_void_p)
        func = ctypes.cast(self.vtable[22], func_type)
        return func(self.ptr, tcid, ppTablet)

    def QueryInterface(self, riid, ppv):
        func_type = ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)
        func = ctypes.cast(self.vtable[0], func_type)
        return func(self.ptr, riid, ppv)

    def Release(self):
        func_type = ctypes.WINFUNCTYPE(wintypes.ULONG, ctypes.c_void_p)
        func = ctypes.cast(self.vtable[2], func_type)
        return func(self.ptr)

    def get_ptr(self):
        return self.ptr

def _rts_query_interface(rts_ptr, iid):
    """在 IRealTimeStylus 指针上执行 QueryInterface"""
    ppv = ctypes.c_void_p(0)
    wrapper = _RealTimeStylusWrapper(rts_ptr)
    hr = wrapper.QueryInterface(ctypes.byref(iid), ctypes.byref(ppv))
    if hr == S_OK and ppv.value:
        return ppv.value
    return 0

def _rts_is2_put_flicks_enabled(rts2_ptr, enabled):
    """通过 IRealTimeStylus2 vtable 调用 put_FlicksEnabled"""
    ppv = ctypes.cast(rts2_ptr, ctypes.POINTER(ctypes.c_void_p))
    vtable = ctypes.cast(ppv[0], ctypes.POINTER(ctypes.c_void_p))
    func_type = ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, wintypes.BOOL)
    func = ctypes.cast(vtable[31], func_type)
    return func(rts2_ptr, enabled)

def _rts_is3_put_multi_touch_enabled(rts3_ptr, enabled):
    """通过 IRealTimeStylus3 vtable 调用 put_MultiTouchEnabled"""
    ppv = ctypes.cast(rts3_ptr, ctypes.POINTER(ctypes.c_void_p))
    vtable = ctypes.cast(ppv[0], ctypes.POINTER(ctypes.c_void_p))
    func_type = ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, wintypes.BOOL)
    func = ctypes.cast(vtable[31], func_type)
    return func(rts3_ptr, enabled)

def _rts_com_release(ptr):
    """通过 IUnknown::Release 释放 COM 接口指针"""
    if not ptr:
        return
    try:
        ppv = ctypes.cast(ptr, ctypes.POINTER(ctypes.c_void_p))
        vtable = ctypes.cast(ppv[0], ctypes.POINTER(ctypes.c_void_p))
        func_type = ctypes.WINFUNCTYPE(wintypes.ULONG, ctypes.c_void_p)
        func = ctypes.cast(vtable[2], func_type)
        func(ptr)
    except Exception:
        pass

def _disable_edge_gestures(hwnd):
    """通过 SHGetPropertyStoreForWindow 禁用边缘手势"""
    try:
        # SHGetPropertyStoreForWindow(HWND, REFIID, void**) 返回 HRESULT
        try:
            shgppfw = ctypes.windll.shell32.SHGetPropertyStoreForWindow
            shgppfw.argtypes = [wintypes.HWND, ctypes.c_void_p, ctypes.c_void_p]
            shgppfw.restype = wintypes.HRESULT
        except AttributeError:
            return False

        IID_IPropertyStore = GUID("{886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99}")
        pStore = ctypes.c_void_p(0)
        hr = shgppfw(hwnd, ctypes.byref(IID_IPropertyStore), ctypes.byref(pStore))
        if hr != S_OK or not pStore.value:
            return False

        # PKEY_EdgeGestureEnable = {32CE38B2-2C9A-41B1-9BC5-B3784394AA44}, pid=2
        class PROPERTYKEY(ctypes.Structure):
            _fields_ = [
                ("fmtid", GUID),
                ("pid", wintypes.UINT),
            ]

        pk = PROPERTYKEY()
        pk.fmtid = GUID("{32CE38B2-2C9A-41B1-9BC5-B3784394AA44}")
        pk.pid = 2

        # PROPVARIANT: vt=VT_BOOL (11), boolVal=VARIANT_FALSE (0)
        # PROPVARIANT 大小为 72 字节，对齐为 8 字节
        buf = ctypes.create_string_buffer(72)
        ctypes.memset(buf, 0, 72)
        ctypes.cast(buf, ctypes.POINTER(wintypes.USHORT))[0] = 11           # vt = VT_BOOL
        ctypes.cast(ctypes.addressof(buf) + 8, ctypes.POINTER(wintypes.SHORT))[0] = 0  # boolVal = VARIANT_FALSE

        # IPropertyStore vtable: 0=QI,1=AddRef,2=Release,3=GetCount,4=GetAt,5=GetValue,6=SetValue,7=Commit
        ppvStore = ctypes.cast(pStore.value, ctypes.POINTER(ctypes.c_void_p))
        vtableStore = ctypes.cast(ppvStore[0], ctypes.POINTER(ctypes.c_void_p))

        setvalue_type = ctypes.WINFUNCTYPE(wintypes.HRESULT, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)
        setvalue = ctypes.cast(vtableStore[6], setvalue_type)
        hr = setvalue(pStore.value, ctypes.byref(pk), ctypes.byref(buf))

        # Release IPropertyStore
        release_type = ctypes.WINFUNCTYPE(wintypes.ULONG, ctypes.c_void_p)
        release = ctypes.cast(vtableStore[2], release_type)
        release(pStore.value)

        return hr == S_OK
    except Exception:
        return False


class _WritingOverlay(QWidget):
    """书写覆盖层"""

    STROKE_TOLERANCE = 15.0

    def __init__(self, component):
        """component: WritingPadComponent that owns this overlay"""
        super().__init__()
        self._component = component
        # 默认窗透明模式 TODO
        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint | Qt.WindowType.Tool | Qt.WindowType.WindowStaysOnTopHint
        )
        self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating, True)
        self.setMouseTracking(True)
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)

        # 计算全屏区域
        screen_rect = QRect()
        for screen in QApplication.screens():
            screen_rect = screen_rect.united(screen.geometry())
        self._screen_rect = screen_rect
        self.setGeometry(screen_rect)

        # 绘图状态
        self._buffer = QPixmap(screen_rect.size())
        self._buffer.fill(Qt.GlobalColor.transparent)
        self._temp_pixmap = QPixmap(screen_rect.size())
        self._temp_pixmap.fill(Qt.GlobalColor.transparent)
        self._temp_pixmaps = {} 
        self._strokes = []
        self._current_strokes = {}
        self._whiteboard = False
        self._mode = 1  # 0=mouse, 1=pen, 2=eraser
        self._painting = False 
        self._touch_active = False  
        self._active_touch_ids = set() 

        # 笔设置
        self._pen_color = QColor(component._pen_color)
        self._pen_width = component._pen_width
        self._draw_mode = component._draw_mode

        # 悬浮工具栏
        self._float_bar = None
        self._create_floating_toolbar()

        # 主窗口位置追踪
        self._main_win = None

        # WM_POINTER 多点触控
        self._rts = None

        # 触控事件队列
        self._touch_queue = deque()
        self._touch_queue_lock = threading.Lock()
        self._touch_timer = QTimer(self)
        self._touch_timer.setInterval(8)  # ~120fps，降低延迟
        self._touch_timer.timeout.connect(self._process_touch_queue)
        self._touch_timer.start()

    def _push_touch_event(self, ev_type, tid, pos):
        """将触控事件推入队列"""
        with self._touch_queue_lock:
            self._touch_queue.append((ev_type, tid, pos.x(), pos.y()))

    def _process_touch_queue(self):
        """在主事件循环中异步处理触控事件队列"""
        if self._painting:
            return
        # 取出所有待处理事件
        batch = []
        with self._touch_queue_lock:
            while self._touch_queue:
                batch.append(self._touch_queue.popleft())
        if not batch:
            return
        for ev_type, tid, x, y in batch:
            pos = QPointF(x, y)
            if ev_type == 0:  # DOWN
                if self._mode == 2:
                    self._erase_at(pos, tid)
                elif self._mode == 1:
                    self._start_stroke(pos, tid)
            elif ev_type == 1:  # UPDATE
                if self._mode == 2:
                    self._erase_at(pos, tid)
                elif self._mode == 1:
                    self._update_stroke(pos, tid)
            elif ev_type == 2:  # UP
                if self._mode == 1:
                    self._end_stroke(pos, tid)

    def nativeEvent(self, eventType, message):
        """WM_POINTER 只读采集"""
        if eventType != b"windows_generic_MSG":
            return False, 0
        if not self.isVisible() or not self.isEnabled():
            return False, 0
        try:
            msg = ctypes.wintypes.MSG.from_address(int(message))

            # WM_POINTER 触控
            if msg.message in (WM_POINTERDOWN, WM_POINTERUPDATE, WM_POINTERUP):
                pointerId = msg.wParam & 0xFFFF

                # WM_POINTER 的 HIWORD(wParam) 包含 pointer flags
                ptr_flags = (msg.wParam >> 16) & 0xFFFF

                # PT_TOUCH=2
                ptrType = ctypes.c_uint32(0)
                if not _GetPointerType(pointerId, ctypes.byref(ptrType)):
                    return False, 0
                if ptrType.value != PT_TOUCH:
                    return False, 0

                # 从 WM_POINTER 消息提取坐标
                x = ctypes.c_short(msg.lParam & 0xFFFF).value
                y = ctypes.c_short((msg.lParam >> 16) & 0xFFFF).value
                pos = QPoint(x, y)
                if pos.x() < -10000 or pos.x() > 100000 or pos.y() < -10000 or pos.y() > 100000:
                    return False, 0

                # 确定事件类型
                if msg.message == WM_POINTERDOWN:
                    ev_type = 0  # DOWN
                    self._active_touch_ids.add(pointerId)
                elif msg.message == WM_POINTERUP:
                    ev_type = 2  # UP
                    self._active_touch_ids.discard(pointerId)
                else:  # WM_POINTERUPDATE
                    # 过滤悬停
                    if not (ptr_flags & POINTER_FLAG_INCONTACT):
                        return False, 0
                    ev_type = 1  # MOVE

                self._touch_active = True
                self._push_touch_event(ev_type, int(pointerId), pos)

                # 抬起延迟清除 _touch_active
                if not self._active_touch_ids:
                    QTimer.singleShot(300, self._maybe_clear_touch_active)

                return False, 0

            return False, 0

        except Exception:
            import traceback
            traceback.print_exc()
            return False, 0

    def _make_icon_pm(self, draw_func, size=24):
        pm = QPixmap(size, size)
        pm.fill(Qt.GlobalColor.transparent)
        p = QPainter(pm)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        draw_func(p)
        p.end()
        return pm

    # 图标绘制函数
    @staticmethod
    def _draw_cursor(p):
        p.setPen(QPen(QColor(200, 200, 200), 2))
        p.drawLine(12, 17, 12, 5); p.drawLine(12, 5, 8, 10); p.drawLine(12, 5, 16, 10)
        p.drawLine(12, 12, 18, 18)

    @staticmethod
    def _draw_pen(p):
        p.setPen(QPen(QColor(200, 200, 200), 2))
        p.drawLine(5, 19, 14, 5); p.drawLine(14, 5, 19, 10); p.drawLine(5, 19, 5, 22)

    @staticmethod
    def _draw_eraser(p):
        p.setPen(QPen(QColor(200, 200, 200), 2))
        p.drawRect(4, 7, 18, 12)

    @staticmethod
    def _draw_trans(p):
        p.fillRect(2, 2, 20, 20, QColor(80, 80, 80, 120))

    @staticmethod
    def _draw_white(p):
        p.fillRect(2, 2, 20, 20, Qt.GlobalColor.white)

    # 悬浮工具栏
    def _create_floating_toolbar(self):
        self._float_bar = QWidget(self)
        self._float_bar.setObjectName("writingFloatBar")
        self._float_bar.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self._float_bar.setStyleSheet(
            "#writingFloatBar{background:rgba(30,30,30,210);border-radius:10px;}"
        )

        layout = QHBoxLayout(self._float_bar)
        layout.setContentsMargins(8, 4, 8, 4)
        layout.setSpacing(6)

        def make_btn(draw_func, text):
            pm = self._make_icon_pm(draw_func, 24)
            btn = _OverToolBtn(pm, text, self._float_bar)
            return btn

        self._f_mouse = make_btn(self._draw_cursor, "鼠标")
        self._f_pen = make_btn(self._draw_pen, "画笔")
        self._f_eraser = make_btn(self._draw_eraser, "擦除")

        self._f_mouse.clicked.connect(lambda: self._set_tool_mode(0))
        self._f_pen.clicked.connect(self._on_float_pen_clicked)
        self._f_eraser.clicked.connect(lambda: self._set_tool_mode(2))

        layout.addWidget(self._f_mouse)
        layout.addWidget(self._f_pen)
        layout.addWidget(self._f_eraser)

        # 分隔
        sep = QLabel("│", self._float_bar)
        sep.setStyleSheet("color:#555;font-size:18px;border:none;background:transparent;")
        sep.setFixedWidth(16)
        sep.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(sep)

        self._f_trans = make_btn(self._draw_trans, "透明")
        self._f_white = make_btn(self._draw_white, "白板")
        self._f_trans.clicked.connect(lambda: self._set_float_whiteboard(False))
        self._f_white.clicked.connect(lambda: self._set_float_whiteboard(True))
        layout.addWidget(self._f_trans)
        layout.addWidget(self._f_white)

        # 关闭
        close_btn = QLabel("✕", self._float_bar)
        close_btn.setFixedSize(40, 40)
        close_btn.setAlignment(Qt.AlignmentFlag.AlignCenter)
        close_btn.setStyleSheet(
            "color:#ff6666;font-size:20px;border:none;background:transparent;"
            "font-weight:bold;"
        )
        close_btn.mousePressEvent = lambda ev: self._close_overlay()
        layout.addWidget(close_btn)

        self._float_bar.adjustSize()
        self._update_float_buttons()

    def _update_float_buttons(self):
        self._f_mouse.setChecked(self._mode == 0)
        self._f_pen.setChecked(self._mode == 1)
        self._f_eraser.setChecked(self._mode == 2)
        self._f_trans.setChecked(not self._whiteboard)
        self._f_white.setChecked(self._whiteboard)

    def _set_tool_mode(self, mode):
        self._mode = mode
        self._update_float_buttons()
        self._component._set_mode(mode, from_overlay=True)

    def _on_float_pen_clicked(self):
        if self._mode == 1:
            self._component._show_pen_settings(overlay=self)
        else:
            self._set_tool_mode(1)

    def _set_float_whiteboard(self, on):
        self.show_overlay(on)
        self._component._set_whiteboard(on, from_overlay=True)

    def _cleanup_state(self):
        """清理触控和绘制状态"""
        self._touch_active = False
        self._active_touch_ids.clear()
        with self._touch_queue_lock:
            self._touch_queue.clear()
        self._current_strokes.clear()
        for tp in self._temp_pixmaps.values():
            tp.fill(Qt.GlobalColor.transparent)
        self._temp_pixmaps.clear()
        self._strokes.clear()

    def _maybe_clear_touch_active(self):
        """无新触控清除 _touch_active 恢复鼠标事件"""
        if not self._active_touch_ids and not self._current_strokes:
            self._touch_active = False

    def _close_overlay(self):
        if self._main_win:
            try:
                self._main_win.removeEventFilter(self)
            except:
                pass
            self._main_win = None
        self._cleanup_state()
        self._component._close_overlay()

    def hideEvent(self, event):
        self._cleanup_state()
        super().hideEvent(event)

    def closeEvent(self, event):
        self._touch_timer.stop()
        self._cleanup_state()
        super().closeEvent(event)

    # 覆盖层显示/隐藏
    def show_overlay(self, whiteboard):
        self.hide()  # 切换窗口标志前隐藏
        self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating, True)

        if whiteboard:
            # 白板模式
            self.setWindowFlags(
                Qt.WindowType.FramelessWindowHint | Qt.WindowType.Tool
            )
            self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, False)
            # 移除旧过滤器
            if self._main_win:
                try:
                    self._main_win.removeEventFilter(self)
                except:
                    pass
            self._main_win = self._component.window()
            if self._main_win:
                self.setGeometry(self._main_win.geometry())
                self._main_win.installEventFilter(self)
        else:
            # 透明模式：全屏置顶
            self.setWindowFlags(
                Qt.WindowType.FramelessWindowHint | Qt.WindowType.Tool | Qt.WindowType.WindowStaysOnTopHint
            )
            self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
            self.setGeometry(self._screen_rect)
            if self._main_win:
                try:
                    self._main_win.removeEventFilter(self)
                except:
                    pass
                self._main_win = None

        self._whiteboard = whiteboard
        self._update_float_buttons()
        # 定位工具栏顶部居中
        fb_w = self._float_bar.width()
        x = (self.width() - fb_w) // 2
        self._float_bar.move(max(0, x), 10)
        self._float_bar.raise_()
        self.show()
        # 窗口显示后禁用边缘手势
        hwnd = int(self.winId())
        if hwnd:
            _disable_edge_gestures(hwnd)
            logger.info("[WM_POINTER] 触控准备完成 (hwnd=%s)", hwnd)

    def eventFilter(self, obj, event):
        if self._whiteboard and obj is self._main_win:
            if event.type() in (QEvent.Type.Move, QEvent.Type.Resize):
                self.setGeometry(self._main_win.geometry())
        return super().eventFilter(obj, event)

    def clear_all(self):
        self._strokes.clear()
        self._current_strokes.clear()
        self._buffer.fill(Qt.GlobalColor.transparent)
        self._temp_pixmap.fill(Qt.GlobalColor.transparent)
        for tp in self._temp_pixmaps.values():
            tp.fill(Qt.GlobalColor.transparent)
        self._temp_pixmaps.clear()
        self.update()

    # 设置同步
    def setPenColor(self, color):
        self._pen_color = QColor(color)

    def setPenWidth(self, w):
        self._pen_width = max(1, w)

    def setDrawMode(self, mode):
        self._draw_mode = mode

    def _safe_paint(self, pixmap, callback):
        """QPixmap绘制操作"""
        if self._painting:
            return
        self._painting = True
        try:
            painter = QPainter(pixmap)
            callback(painter)
            painter.end()
        except Exception:
            pass
        finally:
            self._painting = False

    # 绘图
    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        if self._whiteboard:
            painter.fillRect(self.rect(), Qt.GlobalColor.white)
        painter.drawPixmap(0, 0, self._buffer)
        if not self._temp_pixmap.isNull():
            painter.drawPixmap(0, 0, self._temp_pixmap)
        # 绘制各 touch tid 的独立临时预览
        for tid, tp in list(self._temp_pixmaps.items()):
            if tp and not tp.isNull():
                painter.drawPixmap(0, 0, tp)

    def _paint_stroke(self, painter, s):
        pts = s["points"]
        if not pts:
            return
        painter.setPen(QPen(s["color"], s["width"],
                           Qt.PenStyle.SolidLine, Qt.PenCapStyle.RoundCap, Qt.PenJoinStyle.RoundJoin))
        mode = s.get("mode", "free")
        if mode == "free" and len(pts) > 1:
            for i in range(1, len(pts)):
                painter.drawLine(pts[i - 1], pts[i])
        elif mode == "line" and len(pts) >= 2:
            painter.drawLine(pts[0], pts[-1])
        elif mode == "rect" and len(pts) >= 2:
            r = QRectF(pts[0], pts[-1]).normalized()
            painter.drawRect(r)

    def _start_stroke(self, pos, tid=0):
        if self._mode != 1:
            return
        stroke = {
            "points": [pos],
            "color": QColor(self._pen_color),
            "width": self._pen_width,
            "mode": self._draw_mode,
        }
        self._current_strokes[tid] = stroke

    def _update_stroke(self, pos, tid=0):
        if self._mode != 1:
            return
        stroke = self._current_strokes.get(tid)
        if not stroke:
            return
        if "points" not in stroke:
            self._current_strokes.pop(tid, None)
            return
        pts = stroke["points"]
        mode = stroke.get("mode", "free")

        if mode == "free":
            if len(pts) >= 1:
                # 绘制到 buffer
                self._safe_paint(self._buffer, lambda painter: (
                    painter.setRenderHint(QPainter.RenderHint.Antialiasing),
                    painter.setPen(QPen(stroke["color"], stroke["width"],
                                        Qt.PenStyle.SolidLine, Qt.PenCapStyle.RoundCap, Qt.PenJoinStyle.RoundJoin)),
                    painter.drawLine(pts[-1], pos),
                ))
                # 保存所有点
                pts.append(pos)
                self.update()
        else:
            pts.append(pos)
            if len(pts) >= 2:
                # 每个 tid 使用临时绘图面
                if tid not in self._temp_pixmaps:
                    tp = QPixmap(self._screen_rect.size())
                    tp.fill(Qt.GlobalColor.transparent)
                    self._temp_pixmaps[tid] = tp
                else:
                    tp = self._temp_pixmaps[tid]
                    tp.fill(Qt.GlobalColor.transparent)
                self._safe_paint(tp, lambda painter: (
                    painter.setRenderHint(QPainter.RenderHint.Antialiasing),
                    painter.setPen(QPen(stroke["color"], stroke["width"],
                                       Qt.PenStyle.SolidLine, Qt.PenCapStyle.RoundCap)),
                    painter.drawLine(pts[0], pos) if mode == "line"
                    else painter.drawRect(QRectF(pts[0], pos).normalized()),
                ))
                self.update()

    def _end_stroke(self, pos, tid=0):
        stroke = self._current_strokes.pop(tid, None)
        if not stroke:
            self._temp_pixmaps.pop(tid, None)
            return
        pts = stroke["points"]
        mode = stroke.get("mode", "free")

        if mode == "free":
            if pts:
                pts.append(pos)
            self._strokes.append(stroke)
        else:
            if len(pts) >= 2:
                self._safe_paint(self._buffer, lambda painter: (
                    painter.setRenderHint(QPainter.RenderHint.Antialiasing),
                    painter.setPen(QPen(stroke["color"], stroke["width"],
                                        Qt.PenStyle.SolidLine, Qt.PenCapStyle.RoundCap)),
                    painter.drawLine(pts[0], pts[-1]) if mode == "line"
                    else painter.drawRect(QRectF(pts[0], pts[-1]).normalized()),
                ))
                stroke["points"] = [pts[0], pts[-1]]
                self._strokes.append(stroke)
        # 清理该 tid 的独立绘制表面
        self._temp_pixmaps.pop(tid, None)
        self._temp_pixmap.fill(Qt.GlobalColor.transparent)
        self.update()

    def _erase_at(self, pos, tid=0):
        if self._mode != 2:
            return
        tol = self.STROKE_TOLERANCE
        to_remove = []
        for s in self._strokes:
            for pt in s["points"]:
                if (pt - pos).manhattanLength() < tol:
                    to_remove.append(s)
                    break
        if not to_remove:
            return
        for s in to_remove:
            self._strokes.remove(s)
        self._buffer.fill(Qt.GlobalColor.transparent)
        self._safe_paint(self._buffer, lambda painter: (
            painter.setRenderHint(QPainter.RenderHint.Antialiasing),
            [self._paint_stroke(painter, s) for s in self._strokes],
        ))
        self.update()

    # 鼠标事件
    def mousePressEvent(self, event):
        if self._touch_active or self._current_strokes:
            return
        if self._mode == 2:
            self._erase_at(event.position())
        elif self._mode == 1:
            self._start_stroke(event.position())

    def mouseMoveEvent(self, event):
        if self._touch_active or self._current_strokes:
            return
        if self._mode == 2:
            self._erase_at(event.position())
        elif self._mode == 1:
            self._update_stroke(event.position())

    def mouseReleaseEvent(self, event):
        if self._touch_active or self._current_strokes:
            return
        if self._mode == 1:
            self._end_stroke(event.position())

    def keyPressEvent(self, event):
        if event.key() == Qt.Key.Key_Escape:
            self._close_overlay()
        super().keyPressEvent(event)


class _PenSettingsPopup(QWidget):
    """画笔设置弹出面板"""

    COLORS = [
        ("#FF0000", "红"), ("#FFA500", "橙"), ("#FFFF00", "黄"),
        ("#00AA00", "绿"), ("#0000FF", "蓝"), ("#800080", "紫"),
        ("#000000", "黑"), ("#FFFFFF", "白"),
    ]

    def __init__(self, component, overlay=None, parent=None):
        super().__init__(parent)
        self._component = component
        self._overlay = overlay
        self.setWindowFlags(Qt.WindowType.Popup | Qt.WindowType.FramelessWindowHint)
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.setObjectName("penSettingsPopup")
        self.setFixedSize(300, 230)
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(14, 12, 14, 12)
        layout.setSpacing(10)

        # 颜色
        cl = QLabel("颜色")
        cl.setStyleSheet("font-size:13px;color:#cccccc;border:none;background:transparent;")
        layout.addWidget(cl)
        cr = QHBoxLayout()
        cr.setSpacing(6)
        for hex_c, _ in self.COLORS:
            btn = QPushButton()
            btn.setFixedSize(30, 30)
            c = QColor(hex_c)
            border = "2px solid #555" if c.lightness() > 200 else "2px solid #888"
            btn.setStyleSheet(
                f"QPushButton{{background:{hex_c};border:{border};border-radius:15px;}}"
                f"QPushButton:hover{{border:2px solid white;}}"
            )
            btn.clicked.connect(lambda _, h=hex_c: self._pick_color(h))
            cr.addWidget(btn)
        cr.addStretch()
        layout.addLayout(cr)

        # 粗细
        tl = QLabel("粗细")
        tl.setStyleSheet("font-size:13px;color:#cccccc;border:none;background:transparent;")
        layout.addWidget(tl)
        self._slider = QSlider(Qt.Orientation.Horizontal)
        self._slider.setRange(1, 12)
        self._slider.setValue(self._component._pen_width)
        self._slider.valueChanged.connect(self._pick_width)
        layout.addWidget(self._slider)

        # 模式
        ml = QLabel("模式")
        ml.setStyleSheet("font-size:13px;color:#cccccc;border:none;background:transparent;")
        layout.addWidget(ml)
        mr = QHBoxLayout()
        mr.setSpacing(8)
        self._mode_btns = {}
        for name, display in [("free", "自由"), ("line", "直线"), ("rect", "矩形")]:
            btn = QPushButton(display)
            btn.setCheckable(True)
            btn.setFixedHeight(32)
            btn.setStyleSheet(
                "QPushButton{background:#333;color:#fff;border-radius:6px;padding:0 14px;font-size:13px;}"
                "QPushButton:checked{background:#005fb8;}"
                "QPushButton:hover{background:#444;}"
            )
            btn.clicked.connect(lambda _, n=name: self._pick_mode(n))
            mr.addWidget(btn)
            self._mode_btns[name] = btn
        mr.addStretch()
        layout.addLayout(mr)
        layout.addStretch()

        d = self._mode_btns.get(self._component._draw_mode)
        if d:
            d.setChecked(True)

    def _pick_color(self, hex_c):
        self._component._pen_color = hex_c
        if self._overlay:
            self._overlay.setPenColor(hex_c)

    def _pick_width(self, w):
        self._component._pen_width = w
        if self._overlay:
            self._overlay.setPenWidth(w)

    def _pick_mode(self, mode):
        for n, btn in self._mode_btns.items():
            btn.setChecked(n == mode)
        self._component._draw_mode = mode
        if self._overlay:
            self._overlay.setDrawMode(mode)

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setBrush(QColor(40, 40, 40, 235))
        painter.setPen(QPen(QColor(80, 80, 80), 1))
        painter.drawRoundedRect(self.rect().adjusted(1, 1, -1, -1), 12, 12)


class WritingPadComponent(DraggableContainer):
    """书写板组件"""

    MODE_MOUSE = 0
    MODE_PEN = 1
    MODE_ERASER = 2

    BTN_W = 60
    BTN_H = 88

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="horizontal")
        self.setObjectName("writingPadContainer")
        self.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self._mode = self.MODE_PEN
        self._whiteboard = False
        self._pen_color = "#FF0000"
        self._pen_width = 3
        self._draw_mode = "free"
        self._pen_popup = None
        self._overlay = None
        self._setup_ui()
        self._apply_style()

    # 图标绘制
    @staticmethod
    def _make_icon_pm(draw_func, size=28):
        pm = QPixmap(size, size)
        pm.fill(Qt.GlobalColor.transparent)
        p = QPainter(pm)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        draw_func(p)
        p.end()
        return pm

    @staticmethod
    def _draw_cursor(p):
        p.setPen(QPen(QColor(200, 200, 200), 2))
        p.drawLine(14, 20, 14, 6); p.drawLine(14, 6, 10, 11); p.drawLine(14, 6, 18, 11)
        p.drawLine(14, 14, 21, 21)

    @staticmethod
    def _draw_pen(p):
        p.setPen(QPen(QColor(200, 200, 200), 2))
        p.drawLine(6, 22, 16, 6); p.drawLine(16, 6, 22, 12); p.drawLine(6, 22, 6, 26)

    @staticmethod
    def _draw_eraser(p):
        p.setPen(QPen(QColor(200, 200, 200), 2))
        p.drawRect(5, 8, 20, 14)

    @staticmethod
    def _draw_trans(p):
        p.fillRect(3, 3, 22, 22, QColor(80, 80, 80, 120))

    @staticmethod
    def _draw_white(p):
        p.fillRect(3, 3, 22, 22, Qt.GlobalColor.white)

    # 按钮
    def _build_tool_btn(self, icon_func, text, checkable=True, checked=False):
        """返回 (QWidget, clicked_signal)"""
        container = QWidget(self)
        container.setFixedSize(self.BTN_W, self.BTN_H)
        container.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        layout = QVBoxLayout(container)
        layout.setContentsMargins(2, 4, 2, 2)
        layout.setSpacing(2)

        icon_lb = QLabel(container)
        icon_lb.setPixmap(self._make_icon_pm(icon_func, 28))
        icon_lb.setFixedSize(32, 32)
        icon_lb.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(icon_lb, 0, Qt.AlignmentFlag.AlignCenter)

        text_lb = QLabel(text, container)
        text_lb.setAlignment(Qt.AlignmentFlag.AlignCenter)
        text_lb.setObjectName("_toolBtnText")
        layout.addWidget(text_lb, 0, Qt.AlignmentFlag.AlignCenter)

        container._checked = checked
        container._checkable = checkable
        container._icon_lb = icon_lb
        container._text_lb = text_lb
        container._update_style = lambda: self._style_tool_btn(container)
        container.setCheckState = lambda v: setattr(container, '_checked', v) or container._update_style()
        container.isChecked = lambda: container._checked
        container._update_style()
        return container

    def _style_tool_btn(self, container):
        sz_text = self._scaled_px(11)
        if container._checked:
            container.setStyleSheet(
                "QWidget{background:rgba(0,95,184,100);border-radius:8px;}"
            )
            container._icon_lb.setStyleSheet("border:none;background:transparent;")
            container._text_lb.setStyleSheet(
                f"font-size:{sz_text}px;color:#ffffff;border:none;background:transparent;"
            )
        else:
            container.setStyleSheet(
                "QWidget{background:transparent;border-radius:8px;}"
                "QWidget:hover{background:rgba(255,255,255,20);}"
            )
            container._icon_lb.setStyleSheet("border:none;background:transparent;")
            container._text_lb.setStyleSheet(
                f"font-size:{sz_text}px;color:#aaaaaa;border:none;background:transparent;"
            )

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setContentsMargins(8, 4, 8, 4)
        layout.setSpacing(8)
        layout.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)

        # 鼠标
        self._mouse_box = self._build_tool_btn(self._draw_cursor, "鼠标")
        layout.addWidget(self._mouse_box)
        self._mouse_box.mousePressEvent = lambda ev: self._set_mode(self.MODE_MOUSE)

        # 画笔
        self._pen_box = self._build_tool_btn(self._draw_pen, "画笔")
        self._pen_box.setCheckState(True)
        layout.addWidget(self._pen_box)
        self._pen_box.mousePressEvent = lambda ev: self._on_pen_clicked()

        # 擦除
        self._eraser_box = self._build_tool_btn(self._draw_eraser, "擦除")
        layout.addWidget(self._eraser_box)
        self._eraser_box.mousePressEvent = lambda ev: self._set_mode(self.MODE_ERASER)

        # 分隔
        sep = QLabel("│", self)
        sep.setStyleSheet("color:#444;font-size:20px;border:none;background:transparent;")
        sep.setFixedWidth(20)
        sep.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(sep)

        # 透明
        self._trans_box = self._build_tool_btn(self._draw_trans, "透明")
        self._trans_box.setCheckState(True)
        layout.addWidget(self._trans_box)
        self._trans_box.mousePressEvent = lambda ev: self._set_whiteboard(False)

        # 白板
        self._white_box = self._build_tool_btn(self._draw_white, "白板")
        layout.addWidget(self._white_box)
        self._white_box.mousePressEvent = lambda ev: self._set_whiteboard(True)

        layout.addStretch()

        self._set_natural_size(400, 100)
        self.setMinimumSize(200, 60)
        self._size_explicitly_set = True
        self.resize(400, 100)
        self._update_button_states()

    # 按钮状态
    def _update_button_states(self):
        self._mouse_box.setCheckState(self._mode == self.MODE_MOUSE)
        self._pen_box.setCheckState(self._mode == self.MODE_PEN)
        self._eraser_box.setCheckState(self._mode == self.MODE_ERASER)

    # 模式切换
    def _set_mode(self, mode, from_overlay=False):
        self._mode = mode
        self._update_button_states()
        if self._pen_popup and self._pen_popup.isVisible():
            self._pen_popup.close()
        if mode == self.MODE_MOUSE:
            self._hide_overlay()
        elif mode in (self.MODE_PEN, self.MODE_ERASER) and not from_overlay:
            self._show_overlay()

    def _on_pen_clicked(self):
        if self._mode == self.MODE_PEN:
            if self._overlay and self._overlay.isVisible():
                self._show_pen_settings()
            else:
                self._show_overlay()
        else:
            self._set_mode(self.MODE_PEN)

    def _show_pen_settings(self, overlay=None):
        if self._pen_popup is None:
            self._pen_popup = _PenSettingsPopup(self, overlay)
        else:
            self._pen_popup._overlay = overlay or self._overlay
        src = self._pen_box
        pos = src.mapToGlobal(QPoint(src.width() + 4, 0))
        self._pen_popup.move(pos)
        self._pen_popup.show()

    def _set_whiteboard(self, on, from_overlay=False):
        self._whiteboard = on
        self._white_box.setCheckState(on)
        self._trans_box.setCheckState(not on)
        if self._overlay and self._overlay.isVisible():
            if not from_overlay:
                # 从组件工具栏切换模式 重新创建覆盖层
                self._overlay.show_overlay(on)
            else:
                self._overlay._whiteboard = on
                self._overlay._update_float_buttons()
                self._overlay.update()

    # 覆盖层
    def _show_overlay(self):
        if self._overlay is None:
            self._overlay = _WritingOverlay(self)
        self._overlay.show_overlay(self._whiteboard)
        self._overlay._mode = self._mode
        self._overlay._update_float_buttons()

    def _hide_overlay(self):
        if self._overlay and self._overlay.isVisible():
            self._overlay.hide()

    def _close_overlay(self):
        self._hide_overlay()
        self._mode = self.MODE_MOUSE
        self._update_button_states()
        if self._pen_popup and self._pen_popup.isVisible():
            self._pen_popup.close()

    def _apply_style(self):
        self._apply_card_style()
        self.updateSize()

    def apply_scale(self, factor):
        for box in (self._mouse_box, self._pen_box, self._eraser_box,
                    self._trans_box, self._white_box):
            if hasattr(box, '_update_style'):
                box._update_style()


class ClassAlbumBaseComponent(DraggableContainer):
    """班级相册基类：横向/纵向共用逻辑，子类通过类属性配置方向和尺寸。"""

    SUPPORTED_EXTS = {'.png', '.jpg', '.jpeg', '.bmp', '.gif', '.webp'}
    MAX_IMAGE_DIMENSION = 4096

    # 子类配置
    _layout_direction = "horizontal"   # "horizontal" / "vertical"
    _flip_view_class = HorizontalFlipView
    _default_item_w = 400
    _default_item_h = 200
    _flip_min_size = (80, 60)          # flip_view 的 minimumSize
    _container_min_size = (120, 80)    # 容器的 minimumSize

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction=self._layout_direction)
        self.setObjectName("classAlbumContainer")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.setAcceptDrops(True)
        self._is_temp = component_data.get("id", "").startswith("temp_preview")
        if not self._is_temp:
            unique_id = component_data.get("placement_id") or component_data.get("id", "unknown")
            self._photos_dir = os.path.join(DATA_CLASSPHOTOS, f"album_{unique_id}")
            os.makedirs(self._photos_dir, exist_ok=True)
        self._item_w = self._default_item_w
        self._item_h = self._default_item_h
        self._setup_ui()
        if not self._is_temp:
            self._load_photos()
            self._setup_auto_flip()

    def _setup_ui(self):
        self.flip_view = self._flip_view_class(self)
        self.flip_view.setObjectName("classAlbumFlipView")
        self.flip_view.setBorderRadius(8)
        self.flip_view.setSpacing(0)
        self.flip_view.setItemSize(QSize(self._item_w, self._item_h))
        self.flip_view.setMinimumSize(*self._flip_min_size)
        self.flip_view.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        self.flip_view.setStyleSheet("background: transparent; border: none;")
        self.flip_view.viewport().setStyleSheet("background: transparent;")
        self.flip_view.installEventFilter(self)

        layout = self.inner_layout
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        layout.addWidget(self.flip_view)

        self._set_natural_size(self._item_w, self._item_h)
        self.setMinimumSize(*self._container_min_size)
        self._size_explicitly_set = True
        self.resize(self._item_w, self._item_h)
        self._apply_card_style()

    def _set_item_path(self, item, path: str):
        item.setData(Qt.ItemDataRole.DisplayRole, path)

    def eventFilter(self, obj, event):
        if obj == self.flip_view and event.type() == QEvent.Type.Resize:
            self._fit_item_size()
        return super().eventFilter(obj, event)

    def _fit_item_size(self):
        if not hasattr(self, 'flip_view') or not self.flip_view:
            return
        vp = self.flip_view.viewport()
        if vp and vp.width() > 0 and vp.height() > 0:
            s = QSize(vp.width(), vp.height())
            if s != self.flip_view.itemSize:
                self.flip_view.setItemSize(s)
                self._item_w = s.width()
                self._item_h = s.height()
                self._recomposite_all()

    def _prepare_image(self, pixmap):
        """缩放到 itemSize 内并合成到画布居中"""
        w, h = self._item_w, self._item_h
        scaled = pixmap.scaled(w, h, Qt.AspectRatioMode.KeepAspectRatio,
                               Qt.TransformationMode.SmoothTransformation)
        canvas = QPixmap(w, h)
        canvas.fill(Qt.GlobalColor.transparent)
        p = QPainter(canvas)
        p.drawPixmap((w - scaled.width()) // 2, (h - scaled.height()) // 2, scaled)
        p.end()
        return canvas

    def _recomposite_all(self):
        paths = []
        for i in range(self.flip_view.count()):
            item = self.flip_view.item(i)
            path = item.data(Qt.ItemDataRole.DisplayRole) or ""
            if path:
                paths.append(path)
        if paths:
            idx = self.flip_view.currentIndex()
            self.flip_view.clear()
            for p in paths:
                if os.path.exists(p):
                    pm = self._safe_load_pixmap(p)
                    if pm:
                        item = self.flip_view.addImage(self._prepare_image(pm))
                        if isinstance(item, QListWidgetItem):
                            self._set_item_path(item, p)
            self.flip_view.viewport().update()
            if idx < self.flip_view.count():
                self.flip_view.scrollToIndex(idx)

    def _setup_auto_flip(self):
        self._auto_timer = QTimer(self)
        self._auto_timer.setInterval(5000)
        self._auto_timer.timeout.connect(self._auto_flip_next)
        self._auto_timer.start()

    def _auto_flip_next(self):
        count = self.flip_view.count()
        if count <= 1:
            return
        idx = self.flip_view.currentIndex()
        if idx >= count - 1:
            self.flip_view.scrollToIndex(0)
        else:
            self.flip_view.scrollNext()

    def _safe_load_pixmap(self, path: str):
        reader = QImageReader(path)
        size = reader.size()
        if not size.isValid() or size.width() <= 0 or size.height() <= 0:
            return None
        if size.width() > self.MAX_IMAGE_DIMENSION or size.height() > self.MAX_IMAGE_DIMENSION:
            reader.setScaledSize(size.scaled(
                self.MAX_IMAGE_DIMENSION, self.MAX_IMAGE_DIMENSION,
                Qt.AspectRatioMode.KeepAspectRatio
            ))
        image = reader.read()
        if image.isNull():
            return None
        return QPixmap.fromImage(image)

    def _load_photos(self):
        self.flip_view.clear()
        if not os.path.isdir(self._photos_dir):
            return
        files = sorted(
            f for f in os.listdir(self._photos_dir)
            if os.path.splitext(f)[1].lower() in self.SUPPORTED_EXTS
        )
        for fname in files:
            fpath = os.path.join(self._photos_dir, fname)
            pm = self._safe_load_pixmap(fpath)
            if pm:
                item = self.flip_view.addImage(self._prepare_image(pm))
                if isinstance(item, QListWidgetItem):
                    self._set_item_path(item, fpath)

    def _import_files(self, file_paths: list):
        for src_path in file_paths:
            ext = os.path.splitext(src_path)[1].lower()
            if ext not in self.SUPPORTED_EXTS:
                continue
            fname = os.path.basename(src_path)
            dst_path = os.path.join(self._photos_dir, fname)
            if os.path.exists(dst_path):
                name, e = os.path.splitext(fname)
                c = 1
                while os.path.exists(os.path.join(self._photos_dir, f"{name}_{c}{e}")):
                    c += 1
                dst_path = os.path.join(self._photos_dir, f"{name}_{c}{e}")
            shutil.copy2(src_path, dst_path)
            pm = self._safe_load_pixmap(dst_path)
            if pm:
                item = self.flip_view.addImage(self._prepare_image(pm))
                if isinstance(item, QListWidgetItem):
                    self._set_item_path(item, dst_path)
        if self.flip_view.count() > 0:
            self._auto_timer.start()

    def dragEnterEvent(self, event):
        if event.mimeData().hasUrls():
            for url in event.mimeData().urls():
                if url.isLocalFile() and os.path.splitext(url.toLocalFile())[1].lower() in self.SUPPORTED_EXTS:
                    event.acceptProposedAction()
                    return
        event.ignore()

    def dragMoveEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()
        else:
            event.ignore()

    def dropEvent(self, event):
        paths = [url.toLocalFile() for url in event.mimeData().urls()
                 if url.isLocalFile() and os.path.splitext(url.toLocalFile())[1].lower() in self.SUPPORTED_EXTS]
        if paths:
            self._import_files(paths)
            event.acceptProposedAction()

    def showEvent(self, event):
        super().showEvent(event)
        if not self._is_temp:
            self._auto_timer.start()
        QTimer.singleShot(0, self._fit_item_size)

    def hideEvent(self, event):
        super().hideEvent(event)
        if not self._is_temp:
            self._auto_timer.stop()


class ClassAlbumHorizontalComponent(ClassAlbumBaseComponent):
    """横向班级相册"""

    _layout_direction = "horizontal"
    _flip_view_class = HorizontalFlipView
    _default_item_w = 400
    _default_item_h = 200
    _flip_min_size = (80, 60)
    _container_min_size = (120, 80)


class ClassAlbumVerticalComponent(ClassAlbumBaseComponent):
    """纵向班级相册"""

    _layout_direction = "vertical"
    _flip_view_class = VerticalFlipView
    _default_item_w = 200
    _default_item_h = 400
    _flip_min_size = (60, 80)
    _container_min_size = (80, 120)


class StickyNoteComponent(DraggableContainer):
    """便签组件"""

    STICKY_COLORS = {
        "yellow":  {"bg": "#FFF9C4", "header": "#FFF176", "text": "#5D4037"},
        "green":   {"bg": "#C8E6C9", "header": "#A5D6A7", "text": "#2E7D32"},
        "blue":    {"bg": "#BBDEFB", "header": "#90CAF9", "text": "#1565C0"},
        "pink":    {"bg": "#F8BBD0", "header": "#F48FB1", "text": "#880E4F"},
        "orange":  {"bg": "#FFE0B2", "header": "#FFCC80", "text": "#E65100"},
        "purple":  {"bg": "#E1BEE7", "header": "#CE93D8", "text": "#4A148C"},
    }

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("stickyNoteContainer")
        self._home = parent
        self._notes_dir = DATA_NOTES
        self._notes_file = os.path.join(self._notes_dir, f"{component_data['id']}.json")
        self._color_key = component_data.get("config", {}).get("color", "yellow")
        self._save_timer = QTimer(self)
        self._save_timer.setSingleShot(True)
        self._save_timer.setInterval(500)
        self._save_timer.timeout.connect(self._save_note)
        self._setup_ui()
        self._load_note()

    def _setup_ui(self):
        self._colors = self.STICKY_COLORS.get(self._color_key, self.STICKY_COLORS["yellow"])

        # 标题栏
        self._header = QWidget()
        self._header.setFixedHeight(self._scaled_px(36))
        header_layout = QHBoxLayout(self._header)
        header_layout.setContentsMargins(12, 0, 12, 0)

        self._color_dot = QLabel()
        self._color_dot.setFixedSize(self._scaled_px(10), self._scaled_px(10))
        dot_pm = QPixmap(10, 10)
        dot_pm.fill(QColor(self._colors['header']))
        self._color_dot.setPixmap(dot_pm)
        self._color_dot.setStyleSheet("border-radius: 5px;")

        self._date_label = QLabel(QDate.currentDate().toString("yyyy-MM-dd"))

        header_layout.addWidget(self._color_dot)
        header_layout.addSpacing(6)
        header_layout.addWidget(self._date_label)
        header_layout.addStretch()

        # 编辑区
        self._editor = QTextEdit()
        self._editor.setPlaceholderText(tr("sticky_note.placeholder"))
        self._editor.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self._editor.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self._editor.textChanged.connect(self._on_text_changed)
        self._editor.setContextMenuPolicy(Qt.ContextMenuPolicy.DefaultContextMenu)
        self._editor.setTabChangesFocus(False)

        layout = self.inner_layout
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        layout.addWidget(self._header)
        layout.addWidget(self._editor, 1)

        self._set_natural_size(280, 280)
        self.setMinimumSize(120, 100)
        self._size_explicitly_set = True
        self.resize(280, 280)
        self._apply_style()
    def _apply_style(self):
        colors = self._colors
        sz_date = self._scaled_px(11)
        sz_editor = self._scaled_px(13)
        self.setStyleSheet(f"""
            #stickyNoteContainer {{
                background-color: {colors['bg']};
                border-radius: {cfg.componentCardRadius.value}px;
                border: 1px solid {colors['header']};
            }}
        """)
        self._date_label.setStyleSheet(f"color: {colors['text']}; font-size: {sz_date}px; font-family: 'HarmonyOS Sans'; background: transparent;")
        self._editor.setStyleSheet(f"""
            QTextEdit {{
                background-color: transparent;
                border: none;
                color: {colors['text']};
                font-size: {sz_editor}px;
                font-family: 'HarmonyOS Sans';
                padding: 4px 12px 12px 12px;
                selection-background-color: {colors['header']};
            }}
            QTextEdit:focus {{
                outline: none;
            }}
        """)

    def apply_scale(self, factor):
        self._header.setFixedHeight(self._scaled_px(36))
        self._color_dot.setFixedSize(self._scaled_px(10), self._scaled_px(10))
        self._apply_style()

    def _on_text_changed(self):
        self._save_timer.start()

    def _save_note(self):
        text = self._editor.toPlainText()
        try:
            if not os.path.exists(self._notes_dir):
                os.makedirs(self._notes_dir, exist_ok=True)
            with open(self._notes_file, 'w', encoding='utf-8') as f:
                json.dump({"text": text, "color": self._color_key}, f, ensure_ascii=False)
        except Exception as e:
            logger.warning(f"保存便签失败: {e}")

    def _load_note(self):
        try:
            if os.path.exists(self._notes_file):
                with open(self._notes_file, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                    self._editor.setPlainText(data.get("text", ""))
        except Exception as e:
            logger.warning(f"加载便签失败: {e}")


# 更新注册表
COMPONENT_STYLES["clock"]["digital"]["class"] = DigitalClockComponent
COMPONENT_STYLES["weather"]["icon_temp"]["class"] = WeatherIconTempComponent
COMPONENT_STYLES["weather"]["hourly"]["class"] = WeatherHourlyComponent
COMPONENT_STYLES["weather"]["weekly"]["class"] = WeatherWeeklyComponent
COMPONENT_STYLES["poetry"]["one_line"]["class"] = PoetryOneLineComponent
COMPONENT_STYLES["news"]["baidu"]["class"] = NewsBaiduComponent
COMPONENT_STYLES["news"]["weibo"]["class"] = NewsWeiboComponent
COMPONENT_STYLES["news"]["jinritoutiao"]["class"] = NewsJinritoutiaoComponent
COMPONENT_STYLES["news"]["tenxunwang"]["class"] = NewsTenxunwangComponent
COMPONENT_STYLES["news"]["xcvts"]["class"] = NewsCCTVComponent
COMPONENT_STYLES["countdown"]["event"]["class"] = CountdownEventComponent
COMPONENT_STYLES["school_info"]["class_info"]["class"] = SchoolInfoComponent
COMPONENT_STYLES["media"]["player"]["class"] = MediaPlayerComponent
COMPONENT_STYLES["quick_launch"]["dock"]["class"] = QuickLaunchDockComponent
COMPONENT_STYLES["clock"]["calendar_month"]["class"] = CalendarMonthComponent
COMPONENT_STYLES["linkage"]["timetable_preview"]["class"] = TimetablePreviewComponent
COMPONENT_STYLES["linkage"]["timetable_nowlesson"]["class"] = TimetableNowLessonComponent
COMPONENT_STYLES["Math"]["calculator"]["class"] = CalculatorComponent
COMPONENT_STYLES["writing"]["pad"]["class"] = WritingPadComponent
COMPONENT_STYLES["class_album"]["horizontal"]["class"] = ClassAlbumHorizontalComponent
COMPONENT_STYLES["class_album"]["vertical"]["class"] = ClassAlbumVerticalComponent
COMPONENT_STYLES["sticky_note"]["default"]["class"] = StickyNoteComponent





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
        mime.setData("application/x-Glimpseon-component",
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
        self.setObjectName("componentLibrary")

        self._setup_navigation()
        self.setWindowTitle(tr("component_library.title"))
        self.resize(600, 400)

        if isDarkTheme():
            setTheme(cfg.themeMode.value)

        self.setStyleSheet(load_qss('component.qss'))
    
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

        #  展开导航栏
        self.navigationInterface.expand()
        self.navigationInterface.setReturnButtonVisible(False)
class TimeColumnWidget(QWidget):
    """时间列选择器"""
    valueChanged = pyqtSignal(int)

    def __init__(self, label: str, min_val: int = 0, max_val: int = 59, default: int = 0, parent=None):
        super().__init__(parent)
        self._min_val = min_val
        self._max_val = max_val
        self._value = max(min_val, min(max_val, default))
        self.setFixedWidth(80)
        self._init_ui(label)

    def _init_ui(self, label: str):
        layout = QVBoxLayout(self)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setSpacing(2)
        layout.setContentsMargins(2, 2, 2, 2)

        self._label_widget = BodyLabel(label, self)
        self._label_widget.setAlignment(Qt.AlignmentFlag.AlignCenter)
        label_font = QFont(FONT_FAMILY, 12)
        self._label_widget.setFont(label_font)
        layout.addWidget(self._label_widget)

        self._up_btn = ToolButton(FUI.CHEVRON_UP, self)
        self._up_btn.setFixedSize(40, 28)
        self._up_btn.clicked.connect(self._step_up)
        layout.addWidget(self._up_btn, 0, Qt.AlignmentFlag.AlignCenter)

        self._value_label = StrongBodyLabel(f"{self._value:02d}", self)
        self._value_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        val_font = QFont(FONT_FAMILY, 28, QFont.Weight.Bold)
        self._value_label.setFont(val_font)
        self._value_label.setFixedHeight(44)
        layout.addWidget(self._value_label, 0, Qt.AlignmentFlag.AlignCenter)

        self._down_btn = ToolButton(FUI.CHEVRON_DOWN, self)
        self._down_btn.setFixedSize(40, 28)
        self._down_btn.clicked.connect(self._step_down)
        layout.addWidget(self._down_btn, 0, Qt.AlignmentFlag.AlignCenter)

        self.setSizePolicy(QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Fixed)

    def _step_up(self):
        self.set_value(self._value + 1)

    def _step_down(self):
        self.set_value(self._value - 1)

    def set_value(self, val: int):
        old = self._value
        self._value = max(self._min_val, min(self._max_val, val))
        if self._value != old:
            self._value_label.setText(f"{self._value:02d}")
            self.valueChanged.emit(self._value)

    def value(self) -> int:
        return self._value

    def wheelEvent(self, event):
        delta = event.angleDelta().y()
        if delta > 0:
            self._step_up()
        elif delta < 0:
            self._step_down()
        super().wheelEvent(event)


class TimerTimeDisplayWidget(QWidget):
    """HH:MM:SS组件"""
    def __init__(self, parent=None):
        super().__init__(parent)
        layout = QHBoxLayout(self)
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setSpacing(6)
        layout.setContentsMargins(0, 0, 0, 0)

        self._label = StrongBodyLabel("00:00:00", self)
        font = QFont(FONT_FAMILY, 48, QFont.Weight.Bold)
        self._label.setFont(font)
        self._label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._label.setStyleSheet("color: %s;" % (
            "#ffffff" if isDarkTheme() else "#000000"
        ))
        layout.addWidget(self._label)

    def set_time(self, h: int, m: int, s: int):
        self._label.setText(f"{h:02d}:{m:02d}:{s:02d}")


class TimerCountdownComponent(DraggableContainer):
    """Pivot分栏"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("timerCountdownContainer")
        self._home = parent
        self._running = False
        self._paused = False
        self._elapsed_seconds = 0
        self._remaining_seconds = 0
        self._is_countdown = False
        self._timer = QTimer(self)
        self._timer.setInterval(1000)
        self._timer.timeout.connect(self._on_tick)
        self._init_ui()

    def _init_ui(self):
        # Pivot
        self._pivot = Pivot(self)
        self._pivot.setFixedHeight(self._scaled_px(32))
        self._pivot.addItem(
            "timer",
            tr("timer_countdown.timer"),
            onClick=lambda: self._switch_mode(False),
        )
        self._pivot.addItem(
            "countdown",
            tr("timer_countdown.countdown"),
            onClick=lambda: self._switch_mode(True),
        )

        self._mode_stack = QStackedWidget(self)

        #             计时
        self._timer_page = QWidget()
        tp_layout = QVBoxLayout(self._timer_page)
        tp_layout.setContentsMargins(0, 12, 0, 0)
        tp_layout.setSpacing(4)

        # 计时时间
        self._timer_display = TimerTimeDisplayWidget(self._timer_page)
        tp_layout.addWidget(self._timer_display, 0, Qt.AlignmentFlag.AlignHCenter)

        # 计时按钮
        self._timer_btn_stack = QStackedWidget(self._timer_page)
        self._timer_btn_stack.setFixedHeight(self._scaled_px(60))

        # 开始
        ts_btn = QWidget()
        ts_lay = QVBoxLayout(ts_btn)
        ts_lay.setContentsMargins(0, 0, 0, 0)
        ts_lay.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._ts_start = PrimaryPushButton(FUI.PLAY, tr("timer_countdown.start"), ts_btn)
        self._ts_start.setFixedSize(self._scaled_px(160), self._scaled_px(44))
        self._ts_start.clicked.connect(self._on_timer_start)
        ts_lay.addWidget(self._ts_start)
        self._timer_btn_stack.addWidget(ts_btn)  # index 0

        # 暂停 取消
        tr_btn = QWidget()
        tr_lay = QHBoxLayout(tr_btn)
        tr_lay.setContentsMargins(0, 0, 0, 0)
        tr_lay.setAlignment(Qt.AlignmentFlag.AlignCenter)
        tr_lay.setSpacing(16)
        self._ts_pause = PushButton(tr("timer_countdown.pause"), tr_btn)
        self._ts_pause.setFixedSize(self._scaled_px(100), self._scaled_px(40))
        self._ts_pause.clicked.connect(self._on_timer_pause)
        tr_lay.addWidget(self._ts_pause)
        self._ts_cancel = PushButton(tr("timer_countdown.cancel"), tr_btn)
        self._ts_cancel.setFixedSize(self._scaled_px(100), self._scaled_px(40))
        self._ts_cancel.clicked.connect(self._on_timer_cancel)
        tr_lay.addWidget(self._ts_cancel)
        self._timer_btn_stack.addWidget(tr_btn)  # index 1

        tp_layout.addWidget(self._timer_btn_stack, 0, Qt.AlignmentFlag.AlignHCenter)
        tp_layout.addStretch(1)
        self._mode_stack.addWidget(self._timer_page)  # index 0

        # 倒计时 
        self._cd_page = QWidget()
        cp_layout = QVBoxLayout(self._cd_page)
        cp_layout.setContentsMargins(0, 12, 0, 0)
        cp_layout.setSpacing(4)

        self._cd_content_stack = QStackedWidget(self._cd_page)

        # 时间 按钮
        cd_setup = QWidget()
        cds_lay = QVBoxLayout(cd_setup)
        cds_lay.setContentsMargins(0, 0, 0, 0)
        cds_lay.setAlignment(Qt.AlignmentFlag.AlignCenter)
        cds_lay.setSpacing(8)

        cols = QHBoxLayout()
        cols.setAlignment(Qt.AlignmentFlag.AlignCenter)
        cols.setSpacing(8)
        self._hh_col = TimeColumnWidget(tr("timer_countdown.hours"), 0, 99, 0)
        self._mm_col = TimeColumnWidget(tr("timer_countdown.minutes"), 0, 59, 0)
        self._ss_col = TimeColumnWidget(tr("timer_countdown.seconds"), 0, 59, 0)
        cols.addWidget(self._hh_col)
        cols.addWidget(self._mm_col)
        cols.addWidget(self._ss_col)
        cds_lay.addLayout(cols)

        self._cd_start = PrimaryPushButton(FUI.PLAY, tr("timer_countdown.start"), cd_setup)
        self._cd_start.setFixedSize(self._scaled_px(160), self._scaled_px(40))
        self._cd_start.clicked.connect(self._on_countdown_start)
        cds_lay.addWidget(self._cd_start, 0, Qt.AlignmentFlag.AlignCenter)
        self._cd_content_stack.addWidget(cd_setup)  # index 0

        # 时间显示 暂停 取消
        cd_run = QWidget()
        cdr_lay = QVBoxLayout(cd_run)
        cdr_lay.setContentsMargins(0, 0, 0, 0)
        cdr_lay.setAlignment(Qt.AlignmentFlag.AlignCenter)
        cdr_lay.setSpacing(10)

        self._cd_display = TimerTimeDisplayWidget(cd_run)
        cdr_lay.addWidget(self._cd_display, 0, Qt.AlignmentFlag.AlignCenter)

        cdr_btns = QHBoxLayout()
        cdr_btns.setAlignment(Qt.AlignmentFlag.AlignCenter)
        cdr_btns.setSpacing(16)
        self._cd_pause = PushButton(tr("timer_countdown.pause"), cd_run)
        self._cd_pause.setFixedSize(self._scaled_px(100), self._scaled_px(40))
        self._cd_pause.clicked.connect(self._on_countdown_pause)
        cdr_btns.addWidget(self._cd_pause)
        self._cd_cancel = PushButton(tr("timer_countdown.cancel"), cd_run)
        self._cd_cancel.setFixedSize(self._scaled_px(100), self._scaled_px(40))
        self._cd_cancel.clicked.connect(self._on_countdown_cancel)
        cdr_btns.addWidget(self._cd_cancel)
        cdr_lay.addLayout(cdr_btns)
        self._cd_content_stack.addWidget(cd_run)  # index 1

        cp_layout.addWidget(self._cd_content_stack, 0, Qt.AlignmentFlag.AlignHCenter)
        cp_layout.addStretch(1)
        self._mode_stack.addWidget(self._cd_page)  # index 1

        main_layout = self.inner_layout
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)
        main_layout.addWidget(self._pivot)
        main_layout.addWidget(self._mode_stack, 1)

        self._set_natural_size(240, 200)
        self.setMinimumSize(140, 120)
        self._size_explicitly_set = True
        self.resize(240, 200)
        self._apply_style()
        self._switch_mode(False)

    # 模式切换

    def _switch_mode(self, countdown: bool):
        if self._running:
            self._timer.stop()
            self._running = False
            self._paused = False
            self._elapsed_seconds = 0
            self._remaining_seconds = 0
        self._is_countdown = countdown
        self._pivot.setCurrentItem("countdown" if countdown else "timer")
        self._mode_stack.setCurrentIndex(1 if countdown else 0)
        # 重置到设置
        if countdown:
            self._cd_content_stack.setCurrentIndex(0)
            self._cd_display.set_time(0, 0, 0)
        else:
            self._timer_btn_stack.setCurrentIndex(0)
            self._timer_display.set_time(0, 0, 0)

    # 计时回调

    def _on_timer_start(self):
        self._elapsed_seconds = 0
        self._remaining_seconds = 0
        self._timer_btn_stack.setCurrentIndex(1)  # 暂停 取消
        self._ts_pause.setText(tr("timer_countdown.pause"))
        self._running = True
        self._paused = False
        self._timer.start()

    def _on_timer_pause(self):
        self._running = True  # keep running
        if self._paused:
            self._paused = False
            self._ts_pause.setText(tr("timer_countdown.pause"))
        else:
            self._paused = True
            self._ts_pause.setText(tr("timer_countdown.resume"))

    def _on_timer_cancel(self):
        self._timer.stop()
        self._running = False
        self._paused = False
        self._elapsed_seconds = 0
        self._remaining_seconds = 0
        self._timer_btn_stack.setCurrentIndex(0)
        self._timer_display.set_time(0, 0, 0)

    # 倒计时回调

    def _on_countdown_start(self):
        h = self._hh_col.value()
        m = self._mm_col.value()
        s = self._ss_col.value()
        total = h * 3600 + m * 60 + s
        if total <= 0:
            InfoBar.warning(
                title=tr("common.info"),
                content=tr("timer_countdown.set_time_hint"),
                parent=self,
                duration=2000,
            )
            return
        self._remaining_seconds = total
        self._elapsed_seconds = total
        self._cd_pause.setText(tr("timer_countdown.pause"))
        self._cd_content_stack.setCurrentIndex(1)  # 显示运行页
        self._update_display()
        self._running = True
        self._paused = False
        self._timer.start()

    def _on_countdown_pause(self):
        if self._paused:
            self._paused = False
            self._cd_pause.setText(tr("timer_countdown.pause"))
        else:
            self._paused = True
            self._cd_pause.setText(tr("timer_countdown.resume"))

    def _on_countdown_cancel(self):
        self._timer.stop()
        self._running = False
        self._paused = False
        self._elapsed_seconds = 0
        self._remaining_seconds = 0
        self._cd_content_stack.setCurrentIndex(0)
        self._cd_display.set_time(0, 0, 0)

    # 共享定时器

    def _on_tick(self):
        if not self._running or self._paused:
            return
        if self._is_countdown:
            self._remaining_seconds -= 1
            if self._remaining_seconds <= 0:
                self._remaining_seconds = 0
                self._update_display()
                self._timer.stop()
                self._running = False
                InfoBar.success(
                    title=tr("timer_countdown.finished_title"),
                    content=tr("timer_countdown.finished_body"),
                    parent=self,
                    duration=3000,
                )
                self._cd_content_stack.setCurrentIndex(0)
                self._cd_display.set_time(0, 0, 0)
                return
        else:
            self._elapsed_seconds += 1
        self._update_display()

    def _update_display(self):
        secs = self._remaining_seconds if self._is_countdown else self._elapsed_seconds
        h = secs // 3600
        m = (secs % 3600) // 60
        s = secs % 60
        if self._is_countdown:
            self._cd_display.set_time(h, m, s)
        else:
            self._timer_display.set_time(h, m, s)

    def _apply_style(self):
        self._apply_card_style()

    def apply_scale(self, factor):
        self._pivot.setFixedHeight(self._scaled_px(32))
        self._timer_btn_stack.setFixedHeight(self._scaled_px(60))
        self._ts_start.setFixedSize(self._scaled_px(160), self._scaled_px(44))
        self._ts_pause.setFixedSize(self._scaled_px(100), self._scaled_px(40))
        self._ts_cancel.setFixedSize(self._scaled_px(100), self._scaled_px(40))
        self._cd_start.setFixedSize(self._scaled_px(160), self._scaled_px(40))
        self._cd_pause.setFixedSize(self._scaled_px(100), self._scaled_px(40))
        self._cd_cancel.setFixedSize(self._scaled_px(100), self._scaled_px(40))

    def cleanup(self):
        self._timer.stop()


COMPONENT_STYLES["timer"]["countdown"]["class"] = TimerCountdownComponent
