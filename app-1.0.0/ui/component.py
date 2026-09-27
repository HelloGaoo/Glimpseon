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
ui 组件库
"""

import ctypes
import html as _html
import json
import logging
import os
import re
import shutil
import time
import datetime
from datetime import datetime as _dt, time as _time
import webbrowser
from string import Template
from qfluentwidgets import Theme as FTheme
from collections import OrderedDict, deque
from concurrent.futures import ThreadPoolExecutor
from typing import Optional
import threading

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
    QTimer,
    pyqtProperty,
    QSize,
    pyqtSignal, QObject, pyqtSlot,
    QByteArray, QPropertyAnimation, QEasingCurve,
    QTime, QDate, QLocale,
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
    QMouseEvent,
    QPainter,
    QPainterPath,
    QPen,
    QPixmap,
    QDrag,
)
from PyQt6.QtSvg import QSvgRenderer
from PyQt6.QtWidgets import (
    QFileIconProvider, QGridLayout, QLabel, QSizePolicy, QWidget, QVBoxLayout, QHBoxLayout, QApplication, QGraphicsOpacityEffect,
    QStackedWidget, QListWidgetItem, QFileDialog, QLayout
)
from qfluentwidgets import ProgressRing, InfoBar, isDarkTheme, RoundMenu, Action, FluentWindow, ScrollArea, PushButton, ToolButton, TransparentToolButton, StrongBodyLabel, CardWidget, BodyLabel, ComboBox, SpinBox, SwitchButton, HorizontalFlipView, VerticalFlipView, PrimaryPushButton, Pivot, MessageBoxBase, ProgressBar, LineEdit, ColorPickerButton, Slider, TextEdit, CaptionLabel, SubtitleLabel, FluentIcon, qconfig
from win32com.shell import shell

from core.config import cfg, save_cfg
from core.utils import tr, FUI, get_cached_content, save_cache, precise_now
from services.media import MediaInfo, Lyrics, get_media_info, get_service, media_control, media_next, media_prev
from services.news import NewsService
from services.history import HistoryService
from qfluentwidgets import MessageBox
from services.word import WordService
from services.sentence import SentenceService
from ui.common import create_html_view, HTML_BASE_URL
from core.constants import BASE_DIR, DATA_CLASSPHOTOS, DATA_NOTES, DATA_USER, load_qss, NEWS_ICONS, get_resPath, APP_ICON, FONT_FAMILY, FONT_PRIMARY
from core.component import (
    ComponentDefinition,
    ComponentRegistry,
)

py_datetime = datetime

logger = logging.getLogger("Glimpseon.ui.component")

def get_component_display_name(component_id: str) -> str:
    """组件类型 id > 本地化显示名

    Args:
        component_id: COMPONENT_STYLES 一级键

    Returns:
        名/原传
    """
    name_map = {
        "clock": tr("component.clock"),  # 时钟
        "weather": tr("component.weather"),  # 天气
        "poetry": tr("component.poetry"),  # 一言
        "countdown": tr("component.countdown"),  # 倒计时
        "school_info": tr("component.school_info"),  # 学校信息
        "media": tr("component.media"),  # 媒体信息
        "quick_launch": tr("component.quick_launch"),  # 快捷启动
        "system": tr("component.system"),  # 性能监测
    }
    m = re.match(r"comp_(.+)_\d+$", component_id)
    key = m.group(1) if m else component_id
    if key not in name_map:
        logger.warning(f"[组件] 未知的组件类型 {component_id} 直接显示原始 id")
    return name_map.get(key, component_id)

def _theme_pair(dark, light):
    """按主题取深浅"""
    return dark if isDarkTheme() else light

def _accent_color() -> QColor:
    """取主题色"""
    return QColor(cfg.themeColor.value)

def _accent_css(alpha: float = 1.0) -> str:
    """主题色的css"""
    c = _accent_color()
    if alpha >= 1.0:
        return c.name()
    return f"rgba({c.red()}, {c.green()}, {c.blue()}, {alpha:g})"

def _svg_to_pixmap(icon_path: str, width: int, height: int, dpr: float = 1.0) -> QPixmap:
    """
    Args:
        width: 宽
        height: 高
        dpr: 设备像素比

    Returns:
        结果/空
    """
    renderer = QSvgRenderer(icon_path)
    if not renderer.isValid():
        logger.warning(f"[组件] SVG 渲染失败 返回空图 {icon_path}")
        return QPixmap()
    pm = QPixmap(int(width * dpr), int(height * dpr))
    pm.fill(Qt.GlobalColor.transparent)
    p = QPainter(pm)
    p.setRenderHint(QPainter.RenderHint.Antialiasing)
    p.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)
    renderer.render(p)
    p.end()
    pm.setDevicePixelRatio(dpr)
    return pm

def render_svg_icon(icon_path: str, size: int, dpr: float = 1.0) -> QPixmap:
    """svg > QPixmap

    Returns:
        结果/空
    """
    return _svg_to_pixmap(icon_path, size, size, dpr)

def get_ql_icon_path(icon_filename):
    """快捷启动图标查找

    Returns:
        绝对路径/None
    """
    # 依次找 data/ql_icon、data/software_icon、data/default_icon;再试 .png/.ico 互换
    if not icon_filename:
        return None
    base_dir = BASE_DIR
    for sub in ('ql_icon', 'software_icon', 'default_icon'):
        p = os.path.join(base_dir, 'data', sub, icon_filename)
        if os.path.exists(p):
            return p
    root, ext = os.path.splitext(icon_filename)
    if ext.lower() in ('.png', '.ico'):
        alt_ext = '.ico' if ext.lower() == '.png' else '.png'
        alt_name = root + alt_ext
        for sub in ('ql_icon', 'software_icon', 'default_icon'):
            p = os.path.join(base_dir, 'data', sub, alt_name)
            if os.path.exists(p):
                return p
    return None

FOLDER_ICON = 'Directory.ico'  # 目录类型条目的图标文件名

def extract_app_icon(file_path, name=None, target_size=256):
    """取最大 exe/dll/lnk 图标

    Args:
        name: 保存文件名基底
        target_size: 目标边长(px)/256

    Returns:
        名/None
    """
    try:
        provider = QFileIconProvider()
        fi = QFileInfo(file_path)
        icon = provider.icon(fi)

        pixmap = QPixmap()
        sizes = icon.availableSizes()
        if sizes:
            best_size = max(sizes, key=lambda s: s.width() * s.height())
            pixmap = icon.pixmap(best_size)
        if pixmap.isNull():
            pixmap = icon.pixmap(256, 256)
        if pixmap.isNull():
            pixmap = icon.pixmap(32, 32)
        if pixmap.isNull():
            logger.warning(f"[QL] 提取不到图标: {file_path}")
            return None

        if pixmap.width() < target_size:
            pixmap = pixmap.scaled(target_size, target_size,
                                    Qt.AspectRatioMode.KeepAspectRatio,
                                    Qt.TransformationMode.SmoothTransformation)

        base = name or os.path.splitext(os.path.basename(file_path))[0]
        cleaned = re.sub(r'[^\w\u4e00-\u9fff]', '', base)
        icon_filename = (cleaned or 'app') + '.png'
        icon_dir = os.path.join(BASE_DIR, 'data', 'ql_icon')
        os.makedirs(icon_dir, exist_ok=True)
        pixmap.save(os.path.join(icon_dir, icon_filename), 'PNG')
        logger.debug(f"[QL] 图标已提取: {icon_filename} ({pixmap.width()}x{pixmap.height()})")
        return icon_filename
    except Exception as e:
        logger.error(f"提取图标失败: {e}")
        return None

def resolve_app_from_path(file_path):
    """拖入路径 > 快捷启动项

    Returns:
        条目 dict 键 name/path/icon/type
    """
    real_path = file_path
    is_lnk = file_path.lower().endswith('.lnk')

    # .lnk 经 COM 解析出真实目标,取不到用原路径;名称取 .lnk 本身(带目标扩展名,不适合做显示名)
    if is_lnk:
        try:
            shortcut = pythoncom.CoCreateInstance(
                shell.CLSID_ShellLink, None, pythoncom.CLSCTX_INPROC_SERVER, shell.IID_IShellLink
            )
            persist = shortcut.QueryInterface(pythoncom.IID_IPersistFile)
            persist.Load(file_path)
            real_path = shortcut.GetPath(shell.SLGP_RAWPATH)[0] or file_path
        except Exception as e:
            logger.warning(f"[QL] 解析快捷方式失败 用原路径 {file_path}: {e}")
        name = os.path.splitext(os.path.basename(file_path))[0]
    else:
        name = os.path.splitext(os.path.basename(real_path))[0]

    if os.path.isdir(real_path):
        return {"name": name, "path": real_path, "icon": FOLDER_ICON, "type": "folder"}

    # 提取不到图标用 exe.ico;目标可能已被删,不存在时用原路径试
    icon_filename = extract_app_icon(
        real_path if os.path.exists(real_path) else file_path, name) or 'exe.ico'

    return {"name": name, "path": real_path, "icon": icon_filename, "type": "app"}

def resolve_url_from_string(url_string, name=None):
    """网址 > url

    Args:
        url_string: 网址字符串

    Returns:
        条目 dict 键 name/path/icon/type
    """
    url = url_string.strip()
    if not url.startswith(('http://', 'https://')):
        url = 'https://' + url

    if not name:
        try:
            from urllib.parse import urlparse
            parsed = urlparse(url)
            name = parsed.netloc or url
        except Exception as e:
            logger.warning(f"[QL] 解析网址名称失败 用原字符串 {url_string}: {e}")
            name = url

    icon_filename = 'url.ico' if os.path.exists(
        os.path.join(BASE_DIR, 'data', 'software_icon', 'url.ico')) else 'exe.ico'
    return {"name": name, "path": url, "icon": icon_filename, "type": "url"}

# 组件注册表
COMPONENT_STYLES = {
    "clock": {
        "digital": {
            "name": "数字时钟",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
        },
        "square_1": {
            "name": "方形钟表I",
            "class": None,
            "default_config": {},
            "default_size": (200, 200),
        },
        "square_2": {
            "name": "方形钟表II",
            "class": None,
            "default_config": {},
            "default_size": (200, 200),
        },
        "calendar_month": {
            "name": "月历",
            "class": None,
            "default_config": {},
            "default_size": (200, 200),
        },
        "calendar_mini": {
            "name": "简约月历",
            "class": None,
            "default_config": {},
            "default_size": (200, 200),
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
        "days": {
            "name": "倒数日",
            "class": None,
            "default_config": {"event_name": "", "target_date": "", "title_bg_color": "#F98E1B"},
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
        "grid": {
            "name": "快捷启动II",
            "class": None,
            "default_config": {"apps": []},
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
        "timetable_timeline": {
            "name": "课程时间轴",
            "class": None,
            "default_config": {},
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
    "homework": {
        "board": {
            "name": "作业板",
            "class": None,
            "default_config": {},
            "default_size": (430, 236),
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
    "history": {
        "today": {
            "name": "历史上的今天",
            "class": None,
            "default_config": {},
            "default_size": (360, 240),
        },
    },
    "word": {
        "daily": {
            "name": "每日单词",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
        },
    },
    "sentence": {
        "daily": {
            "name": "每日英语",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
        },
    },
    "system": {
        "performance": {
            "name": "性能监测",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
        },
        "netspeed": {
            "name": "网速监控",
            "class": None,
            "default_config": {},
            "default_size": (400, 200),
        },
    },
}

class ComponentManager:
    """组件生命周期"""

    MAX_COMPONENTS = 100

    def __init__(self, home_interface):
        self.home = home_interface
        self.components = {} 
        self._component_data = {}
        logger.debug("[MGR] 组件管理器初始化")

    @property
    def page_manager(self):
        return getattr(self.home, 'page_manager', None)

    def load_components(self):
        pm = self.page_manager
        if pm is None:
            logger.warning("[ComponentManager] PageManager 未初始化")
            return

        total = 0
        for page_index, meta in enumerate(pm.pages()):
            if meta.type != "info":
                continue
            for comp_data in meta.components:
                if total >= self.MAX_COMPONENTS:
                    break
                comp_id = comp_data.get("id")
                comp_type = comp_data.get("type")
                comp_style = comp_data.get("style")

                if not comp_id or not comp_type or not comp_style:
                    logger.warning(f"组件数据不完整: {comp_data}")
                    continue

                style_info = COMPONENT_STYLES.get(comp_type, {}).get(comp_style)
                if not style_info or style_info.get("class") is None:
                    logger.warning(f"组件样式未注册: {comp_type}/{comp_style}")
                    continue

                comp_class = style_info["class"]
                try:
                    _t0 = time.time()
                    parent_widget = self._get_parent_widget(page_index)
                    instance = comp_class(parent_widget, comp_data)
                    size_data = comp_data.get("size")
                    if size_data and size_data.get("w") and size_data.get("h"):
                        instance.resize(size_data["w"], size_data["h"])
                    else:
                        default_size = style_info.get("default_size", (200, 80))
                        instance.resize(*default_size)
                    instance._init_dpi(comp_data.get("scale"))
                    instance._size_explicitly_set = True
                    instance.hide()
                    instance.setPositionPercent(
                        comp_data.get("position", {}).get("x", 0.5),
                        comp_data.get("position", {}).get("y", 0.5)
                    )

                    comp_data["page_index"] = page_index
                    self.components[comp_id] = instance
                    self._component_data[comp_id] = comp_data
                    total += 1
                    logger.debug(f"[CM] 组件 {comp_id} 创建耗时 {(time.time() - _t0) * 1000:.1f}ms")
                    logger.info(f"加载组件: {comp_id} ({comp_type}/{comp_style}) page={page_index}")
                except Exception as e:
                    logger.error(f"创建组件失败 {comp_id}: {e}")
            if total >= self.MAX_COMPONENTS:
                break
        logger.info(f"[ComponentManager] 共加载{total}个组件")

    def save_components(self):
        pm = self.page_manager
        if pm is None:
            return

        page_components = {}  # page_index > list of comp_data
        for comp_id, instance in self.components.items():
            pos_x, pos_y = instance.getPositionPercent()
            stored = self._component_data.get(comp_id, {})
            page_index = stored.get("page_index", 0)
            comp_data = {
                "id": comp_id,
                "type": stored.get("type", "unknown"),
                "style": stored.get("style", "unknown"),
                "position": {"x": pos_x, "y": pos_y},
                "size": {"w": instance.width(), "h": instance.height()},
                "scale": getattr(instance, "_dpi", 100),
                "enabled": stored.get("enabled", True),
                "page_index": page_index,
                "config": stored.get("config", {}),
            }
            page_components.setdefault(page_index, []).append(comp_data)

        for page_index, meta in enumerate(pm.pages()):
            if meta.type == "info":
                meta.components = page_components.get(page_index, [])

        logger.debug(f"[CM] 保存明细: {sum(len(v) for v in page_components.values())}个组件分布于{len(page_components)}页")
        pm.save()
        logger.info("[ComponentManager] 组件已保存")

    def add_component(self, comp_type: str, comp_style: str, config=None, page_index: int = 0) -> str:
        """
        Args:
            comp_type: 组件类型
            comp_style: 组件样式
            config: 初始配置 dict 可None

        Returns:
            新组件 id/空串
        """
        if len(self.components) >= self.MAX_COMPONENTS:
            logger.warning(f"组件数量上限: {self.MAX_COMPONENTS}")
            return ""

        style_info = COMPONENT_STYLES.get(comp_type, {}).get(comp_style)
        if not style_info or style_info.get("class") is None:
            logger.warning(f"组件样式未注册: {comp_type}/{comp_style}")
            return ""

        existing_ids = set(self.components.keys())
        counter = 1
        while f"comp_{comp_type}_{counter}" in existing_ids:
            counter += 1
        comp_id = f"comp_{comp_type}_{counter}"
        default_size = style_info.get("default_size", (200, 80))
        comp_data = {
            "id": comp_id,
            "type": comp_type,
            "style": comp_style,
            "position": {"x": 0.5, "y": 0.5},
            "size": {"w": default_size[0], "h": default_size[1]},
            "scale": 100,
            "enabled": True,
            "page_index": page_index,
            "config": config or style_info.get("default_config", {}),
        }

        comp_class = style_info["class"]
        try:
            parent_widget = self._get_parent_widget(page_index)
            instance = comp_class(parent_widget, comp_data)
            instance.resize(*default_size)
            instance._init_dpi(100)
            instance._size_explicitly_set = True
            instance.show()
            instance.setPositionPercent(0.5, 0.5)

            self.components[comp_id] = instance
            self._component_data[comp_id] = comp_data
            self.save_components()
            logger.info(f"添加组件: {comp_id} ({comp_type}/{comp_style}) page={page_index}")
            return comp_id
        except Exception as e:
            logger.error(f"创建组件失败: {e}")
            return ""

    def remove_component(self, comp_id: str):
        if comp_id not in self.components:
            logger.warning(f"组件不存在: {comp_id}")
            return

        instance = self.components[comp_id]
        for attr in ('_config_button', '_delete_button'):
            btn = getattr(instance, attr, None)
            if btn is not None:
                btn.setParent(None)
                btn.deleteLater()
        instance.deleteLater()
        del self.components[comp_id]
        del self._component_data[comp_id]
        self.save_components()
        logger.info(f"删除组件: {comp_id}")

    def get_all_containers(self) -> list:
        """
        Returns:
            容器列表
        """
        return list(self.components.values())

    def get_component_page(self, comp_id: str) -> int:
        return self._component_data.get(comp_id, {}).get("page_index", 0)

    def set_component_page(self, comp_id: str, page_index: int):
        """改组件归属页"""
        if comp_id in self._component_data:
            self._component_data[comp_id]["page_index"] = page_index
            logger.info(f"[CM] 组件 {comp_id} 归属页改为 {page_index}")
            self.save_components()

    def shift_pages_after_delete(self, deleted_index: int, fallback_index: int = 0):
        """删除页面后前移页码

        Args:
            fallback_index: 迁入目标页
        """
        moved = 0
        for comp_id, stored in self._component_data.items():
            pi = stored.get("page_index", 0)
            if pi == deleted_index:
                stored["page_index"] = fallback_index
                moved += 1
            elif pi > deleted_index:
                stored["page_index"] = pi - 1
                moved += 1
        if moved:
            logger.info(f"[CM] 删除页面 {deleted_index} 后迁移{moved}个组件至页 {fallback_index}")
        self.save_components()

    def get_component_data(self, comp_id: str) -> dict:
        return self._component_data.get(comp_id, {})

    def update_component_config(self, comp_id: str, config: dict):
        """配置弹窗保存

        Args:
            config: 新配置 dict
        """
        if comp_id not in self._component_data:
            logger.warning(f"组件不存在: {comp_id}")
            return

        self._component_data[comp_id]["config"] = config
        logger.info(f"[CM] 组件 {comp_id} 配置更新: 键={sorted(config.keys())}")
        self.save_components()

    def _get_parent_widget(self, page_index: int):
        try:
            if hasattr(self.home, "get_info_page_widget"):
                pw = self.home.get_info_page_widget(page_index)
                if pw is not None:
                    return pw
        except Exception as e:
            logger.warning(f"[CM] 获取页面容器失败(page={page_index}): {e}")
        return self.home


def _find_ancestor(widget, attr):
    while widget:
        if hasattr(widget, attr):
            return widget
        widget = widget.parentWidget()
    return None

def _find_timetable_page():
    """Returns:
        课表页/None
    """
    for w in QApplication.topLevelWidgets():
        page = getattr(w, 'timetablePage', None)
        if page is not None:
            return page
    return None

def _accepts_drop(mime) -> bool:
    """拖入内容是否可接受

    Args:
        mime: 拖入的 QMimeData

    Returns:
        exe/lnk/目录/网址文本 True
    """
    if mime.hasUrls():
        for url in mime.urls():
            path = url.toLocalFile()
            if path and (path.lower().endswith(('.exe', '.lnk')) or os.path.isdir(path)):
                return True
    elif mime.hasText():
        text = mime.text().strip()
        if text.startswith(('http://', 'https://', 'www.')):
            return True
    return False

def _iter_drop_items(mime):
    """把拖入内容解析成条目

    Args:
        mime: 拖入的 QMimeData

    Yields:
        条目 dict(name/path/icon/type)
    """
    logger.debug("解析拖入条目")
    if mime.hasUrls():
        for url in mime.urls():
            path = url.toLocalFile()
            if not path:
                continue
            if os.path.isdir(path):
                yield {"name": os.path.basename(path), "path": path,
                       "icon": FOLDER_ICON, "type": "folder"}
            elif path.lower().endswith(('.exe', '.lnk')):
                yield resolve_app_from_path(path)
    elif mime.hasText():
        text = mime.text().strip()
        if text.startswith(('http://', 'https://', 'www.')):
            yield resolve_url_from_string(text)

def _launch_target(target, app_type):
    """启动条目目标

    Args:
        target: exe 路径/目录/url
        app_type: app/folder/url

    Returns:
        True+原因/False+原因
    """
    if not target:
        return False, tr("quick_launch.no_path")
    try:
        if app_type == "url":
            webbrowser.open(target)
            logger.info(f"[QL] 打开网址 {target}")
            return True, target
        if os.path.exists(target):
            os.startfile(target)
            logger.info(f"[QL] 启动 {app_type}: {target}")
            return True, target
        key = "quick_launch.folder_not_exist" if app_type == "folder" else "quick_launch.path_not_exist"
        logger.warning(f"[QL] 启动失败 {app_type} {target}")
        return False, tr(key, path=target)
    except Exception as e:
        logger.error(f"[QL] 启动目标异常 {target}: {e}")
        return False, str(e)

def _show_launch_result(host, app_name, info, success):
    """
    Args:
        host: 发起启动的组件
        info: 成功/失败原因
    """
    if success:
        logger.info(f"已启动 {app_name} ({info})")
        InfoBar.success(tr("quick_launch.launch_success"),
                        tr("quick_launch.opening", name=app_name),
                        parent=host.window(), duration=2000)
    else:
        logger.warning(f"启动失败 {app_name} {info}")
        InfoBar.error(tr("quick_launch.launch_failed"), f"{app_name}: {info}",
                      parent=host.window(), duration=3000)

def _build_item_menu(parent, title, on_open, on_edit, edit_text, on_delete):
    """
    Args:
        parent: 菜单父级
        title: 菜单标题
        on_open: 打开回调
        on_edit: 改名回调
        edit_text: 改名项文案
        on_delete: 删除回调

    Returns:
        RoundMenu(未 exec)
    """
    menu = RoundMenu(title, parent)
    open_action = Action(FUI.PLAY, tr("quick_launch.open"), parent)
    open_action.triggered.connect(on_open)
    menu.addAction(open_action)
    menu.addSeparator()
    edit_action = Action(FUI.EDIT, edit_text, parent)
    edit_action.triggered.connect(on_edit)
    menu.addAction(edit_action)
    delete_action = Action(FUI.DELETE, tr("quick_launch.delete"), parent)
    delete_action.triggered.connect(on_delete)
    menu.addAction(delete_action)
    logger.debug(f"[QL] 构建右键菜单: {title}")
    return menu

def _color_name(value, fallback):
    """
    Args:
        value: ColorPickerButton.color
        fallback: 默认值

    Returns:
        颜色名
    """
    c = value() if callable(value) else value
    return c.name() if c.isValid() else fallback


class DraggableWidget(QWidget):
    """组件定位/交互基类"""
    positionChanged = pyqtSignal(float, float)
    selected = pyqtSignal(str)
    _accept_press = False

    def __init__(self, parent=None, component_id: str = ""):
        super().__init__(parent)
        self.component_id = component_id
        logger.debug(f"[DW] 初始化组件 {component_id}")
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

        self.setMouseTracking(True)
        self.setAutoFillBackground(False)
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Preferred)

    def setDraggable(self, enabled: bool):
        self._draggable = enabled
        for child in self.findChildren(QWidget):
            try:
                child.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, enabled)
            except RuntimeError as e:
                logger.debug(f"[DW] 子控件已销毁 {e}")
        self._show_border = enabled
        logger.debug(f"[DW] {self.component_id} 拖拽模式: {enabled}")
        self.update()
        if enabled:
            self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            self.raise_()
        else:
            self.setCursor(QCursor(Qt.CursorShape.ArrowCursor))

    def setSelected(self, selected: bool):
        self._selected = selected
        logger.debug(f"[DW] {self.component_id} 选中: {selected}")
        self.update()

    def setPositionPercent(self, x: float, y: float):
        self._percent_x = max(0.0, min(1.0, x))
        self._percent_y = max(0.0, min(1.0, y))
        self._updatePositionFromPercent()

    def getPositionPercent(self) -> tuple:
        return (self._percent_x, self._percent_y)

    def paintEvent(self, event):
        super().paintEvent(event)

        if getattr(self, '_dragging', False): return

        if getattr(self, '_selected', False):
            painter = QPainter(self)
            painter.setRenderHint(painter.RenderHint.Antialiasing)
            color = _accent_color()

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
                border_color = _accent_color()
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
                font = QFont(FONT_PRIMARY)
                font.setPixelSize(14)
                painter.setFont(font)
                painter.setPen(QColor(0, 0, 0, 100) if not isDarkTheme() else QColor(255, 255, 255, 100))
                painter.drawText(8, 16, display_name)

            painter.end()

    def enterEvent(self, event):
        if self._draggable:
            self._hovered = True
            logger.debug(f"[DW] {self.component_id} 悬停进入 (选中={self._selected})")
            if self._selected and self._hitResizeHandle(self.mapFromGlobal(QCursor.pos())):
                self.setCursor(QCursor(Qt.CursorShape.SizeFDiagCursor))
            else:
                self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor))
            self.update()
        super().enterEvent(event)

    def leaveEvent(self, event):
        self._hovered = False
        if self._draggable:
            logger.debug(f"[DW] {self.component_id} 悬停离开")
        if self._draggable and not self._dragging:self.setCursor(QCursor(Qt.CursorShape.ArrowCursor))
        self.update()
        super().leaveEvent(event)

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self._click_start_pos = event.globalPosition().toPoint()
            if self._draggable:
                # 选中状态
                if self._selected and self._hitResizeHandle(event.position().toPoint()):
                    self._resizing = True
                    self._resize_start_pos = event.globalPosition().toPoint()
                    self._resize_start_size = self.size()
                    self._resize_start_dpi = getattr(self, '_dpi', 100)
                    if getattr(self, '_base_size', None) is None:
                        f = self._resize_start_dpi / 100.0
                        self._base_size = QSize(
                            max(1, round(self._resize_start_size.width() / f)),
                            max(1, round(self._resize_start_size.height() / f)))
                    self._saved_min_size = self.minimumSize()
                    self.setMinimumSize(1, 1)
                    self.setCursor(QCursor(Qt.CursorShape.SizeFDiagCursor))
                    logger.debug(f"[DW] {self.component_id} 缩放 初始 {self._resize_start_size.width()}x{self._resize_start_size.height()}")
                    event.accept()
                    return

                self._dragging = True
                self._drag_start_pos = event.globalPosition().toPoint()
                self._widget_start_pos = self.pos()
                self.setCursor(QCursor(Qt.CursorShape.ClosedHandCursor))
                logger.debug(f"[DW] {self.component_id} 拖拽 {self._widget_start_pos.x()},{self._widget_start_pos.y()}")
                self.update()
                self.raise_()
                event.accept()
                return
            if self._accept_press:
                event.accept()
                return

        super().mousePressEvent(event)

    def mouseDoubleClickEvent(self, event):
        if self._draggable and event.button() == Qt.MouseButton.LeftButton:
            logger.debug(f"[DW] {self.component_id} 双击 ({event.position().toPoint().x()},{event.position().toPoint().y()})")
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
            new_w = max(40, orig_w + delta.x())
            new_h = max(40, orig_h + delta.y())
            scale = max(new_w / orig_w if orig_w > 0 else 1.0,
                        new_h / orig_h if orig_h > 0 else 1.0)
            start_dpi = getattr(self, '_resize_start_dpi', 100)
            new_dpi = max(1, min(300, int(round(start_dpi * scale))))
            base = getattr(self, '_base_size', None)
            if base and not base.isEmpty():
                parent = self.parentWidget()
                if parent:
                    max_f_w = (parent.width() - self.x()) / base.width()
                    max_f_h = (parent.height() - self.y()) / base.height()
                    new_dpi = min(new_dpi, max(1, int(min(max_f_w, max_f_h) * 100)))
            if new_dpi != getattr(self, '_dpi', 100):
                f = new_dpi / 100.0
                self._dpi = new_dpi
                self._scale_factor = f
                if base and not base.isEmpty():
                    self.resize(max(1, round(base.width() * f)),
                                max(1, round(base.height() * f)))
                    logger.debug(f"[DW] {self.component_id} 缩放至 {self.width()}x{self.height()} (DPI {new_dpi})")
                if hasattr(self, '_scale_timer') and not self._scale_timer.isActive():
                    self._scale_timer.start()
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

            self.move(new_pos)
            self._percent_x, self._percent_y = self._calculatePercentFromPosition()
            self.positionChanged.emit(self._percent_x, self._percent_y)

            event.accept()
            return

        super().mouseMoveEvent(event)

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
            if hasattr(self, '_scale_timer'):
                self._scale_timer.stop()
            if hasattr(self, '_apply_scale_now'):
                self._apply_scale_now()
            self._save_position()
            event.accept()
            return

        # 拖动结束
        if self._dragging:
            self._dragging = False
            self.setCursor(QCursor(Qt.CursorShape.OpenHandCursor if self._draggable else Qt.CursorShape.ArrowCursor))

        self.update()

        # 点击检测
        if self._draggable:
            delta = event.globalPosition().toPoint() - self._click_start_pos
            if abs(delta.x()) < 5 and abs(delta.y()) < 5:
                self.selected.emit(self.component_id)
                event.accept()
                return

        px, py = self._calculatePercentFromPosition()
        if (px, py) != (self._percent_x, self._percent_y):
            self._percent_x, self._percent_y = px, py
            self._save_position()
            logger.debug(f"[DRAG] 组件 {self.component_id} 移动到 ({px:.3f} {py:.3f})")
        event.accept()

    def onParentResize(self):
        self._updatePositionFromPercent()

    def _handle_config_click_release(self, event, allow_config: bool = True) -> bool:
        """单击开配置统一释放

        Returns:
            True/False
        """
        if event.button() != Qt.MouseButton.LeftButton or not hasattr(self, '_click_start_pos'):
            return False
        delta = event.globalPosition().toPoint() - self._click_start_pos
        if abs(delta.x()) >= 5 or abs(delta.y()) >= 5 or self._draggable:
            return False
        self._dragging = False
        if not allow_config:
            return False
        self._on_config_clicked()
        logger.debug(f"[DRG] 组件配置入口触发 id={getattr(self, 'component_id', '?')}")
        event.accept()
        return True

    def _updatePositionFromPercent(self):
        # 乘以父容器"可用区域"(减去自身尺寸),保持组件完整可见
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
        """Returns:
            (percent_x, percent_y),可用区域异常用 (0.5, 0.5)
        """
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

    def _hitResizeHandle(self, pos) -> bool:
        """Args:
            pos: 本控件坐标位置

        Returns:
            True
        """
        if not self._selected:
            return False
        handle_zone = 24
        hit = (pos.x() >= self.width() - handle_zone and
               pos.y() >= self.height() - handle_zone)
        if hit:
            logger.debug(f"[DW] {self.component_id} 命中调整手柄 ({pos.x()},{pos.y()})")
        return hit

    def _save_position(self):
        home = _find_ancestor(self.parentWidget(), 'component_manager')
        mgr = getattr(home, 'component_manager', None) if home else None
        if mgr:
            mgr.save_components()
            logger.debug(f"[DW] 保存 {self.component_id} 位置 ({self._percent_x:.3f} {self._percent_y:.3f})")

class DraggableContainer(DraggableWidget):
    """卡片容器基类"""

    def __init__(self, parent=None, component_id: str = "", layout_direction: str = "vertical"):
        super().__init__(parent, component_id)

        self._content_visible = True
        self._content_state_applied = False
        self._delete_button = None
        self._config_button = None
        # 内容 = _dpi/100，占位 = _base_size × 因子；
        # self._scaled_px(base) 缩放字体/图标，apply_scale 重应用
        self._dpi = 100
        self._base_size = None  # 100%
        self._scale_factor = 1.0
        self._size_explicitly_set = False
        self._applying_scale = False
        # 布局边距/间距基准值缓存
        self._layout_bases = None
        # 卡片背景标准配置 11**14年了终于统一背景了
        self._bg_opacity = None
        self._corner_radius = None

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
        self._scale_timer = QTimer(self)
        self._scale_timer.setInterval(30)
        self._scale_timer.timeout.connect(self._apply_scale_now)
        # 下面这些都是更新样式相关
        cfg.componentCardOpacity.valueChanged.connect(self._on_card_config_changed)
        cfg.componentCardRadius.valueChanged.connect(self._on_card_config_changed)

        cfg.themeChanged.connect(self._on_card_config_changed)
        qconfig.themeChangedFinished.connect(self._on_card_config_changed)
        logger.debug(f"[DRG] 组件容器创建 id={getattr(self, 'component_id', '?')}")

    def _init_dpi(self, scale=None):
        """
        Args:
            scale: 已保存的缩放%
        """
        w, h = self.width(), self.height()
        scale = max(1, min(300, int(scale) if scale is not None else 100))
        f = scale / 100.0
        self._dpi = scale
        self._scale_factor = f
        self._base_size = QSize(max(1, round(w / f)), max(1, round(h / f)))
        logger.debug(f"[CT] {self.component_id} DPI 初始化: {scale}% 基准 {w}x{h}")
        self.apply_scale(f)

    def apply_config(self, config: dict):
        _old_op = getattr(self, "_bg_opacity", None)
        _old_cr = getattr(self, "_corner_radius", None)
        self._bg_opacity = config.get("bg_opacity", self._bg_opacity)
        self._corner_radius = config.get("corner_radius", self._corner_radius)
        logger.info(f"[CT] {self.component_id} 应用配置: 键={sorted(config.keys())}")
        if (_old_op, _old_cr) != (self._bg_opacity, self._corner_radius):
            logger.info(f"[CT] {self.component_id} 样式 bg {_old_op} -> {self._bg_opacity} 圆角 {_old_cr} -> {self._corner_radius}")
        self._on_card_config_changed()

    def setContentVisible(self, visible: bool):
        """内容/占位切换"""
        if self._content_state_applied and visible == self._content_visible:
            return
        self._content_state_applied = True
        self._content_visible = visible
        logger.debug(f"[CT] {self.component_id} 内容可见性: {visible}")
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
            self.setFixedSize(self._placeholder_size())
        self.updateGeometry()
        self.update()

    def _placeholder_size(self) -> QSize:
        """占位尺寸"""
        text = f"⚙ {get_component_display_name(self.component_id)} {tr('component.click_to_settings')}"
        font = QFont()
        font.setPointSize(8)
        return QSize(max(QFontMetrics(font).horizontalAdvance(text) + 24, 100), 36)

    def addWidget(self, widget):
        self.inner_layout.addWidget(widget)
        self.inner_layout.activate()
        self.adjustSize()
        self.updateGeometry()

    def updateSize(self):
        if not getattr(self, '_update_pending', False):
            self._update_pending = True
            QTimer.singleShot(0, self._doUpdateSize)

    def showEditControls(self, visible: bool):
        """编辑模式显示配置/删除按钮"""
        if self._delete_button is None:
            home = self._getHomeInterface()
            parent_widget = home if home else self
            self._config_button, self._delete_button = _create_edit_controls(
                parent_widget, self, self._on_delete_clicked, self._on_config_clicked
            )
            try:
                cfg.componentCardOpacity.valueChanged.connect(self._update_edit_controls_style)
                cfg.componentCardRadius.valueChanged.connect(self._update_edit_controls_style)
            except Exception as e:
                logger.debug(f"[CT] 编辑控件样式联动配置连接失败: {e}")
        self._config_button.setVisible(visible)
        self._delete_button.setVisible(visible)
        logger.debug(f"[CT] {self.component_id} 编辑控件可见: {visible}")
        if visible:
            self._config_button.reposition()
            self._config_button.raise_()
            self._delete_button.reposition()
            self._delete_button.raise_()

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if self._delete_button and self._delete_button.isVisible():
            self._delete_button.reposition()
        if self._config_button and self._config_button.isVisible():
            self._config_button.reposition()
        self._resize_debounce_timer.start(50)

    def moveEvent(self, event):
        super().moveEvent(event)
        _p = (self.x(), self.y())
        _last = getattr(self, '_last_move_log_pos', None)
        if _last is None or abs(_p[0] - _last[0]) + abs(_p[1] - _last[1]) >= 20:
            self._last_move_log_pos = _p
            logger.debug(f"[CT] {self.component_id} 移动到 ({_p[0]} {_p[1]})")
        if self._delete_button and self._delete_button.isVisible():
            self._delete_button.reposition()
        if self._config_button and self._config_button.isVisible():
            self._config_button.reposition()

    def showEvent(self, event):
        super().showEvent(event)
        _first = not getattr(self, '_ct_shown_once', False)
        self._ct_shown_once = True
        if _first:
            logger.debug(f"[CT] {self.component_id} 首次显示")
        if getattr(self, 'inner_layout', None):
            self.inner_layout.activate()
            if not getattr(self, '_size_explicitly_set', False):
                self.adjustSize()

    def hideEvent(self, event):
        super().hideEvent(event)
        logger.debug(f"[CT] {self.component_id} 隐藏")

    def sizeHint(self) -> QSize:
        if getattr(self, '_size_explicitly_set', False):
            return self.size()
        if getattr(self, 'inner_layout', None):
            self.inner_layout.activate()
        if not getattr(self, '_content_visible', True):
            return self._placeholder_size()
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
            font = QFont()
            font.setPixelSize(14)
            painter.setFont(font)
            painter.setPen(QColor(0, 0, 0, 100) if not isDarkTheme() else QColor(255, 255, 255, 100))
            painter.drawText(self.rect(), Qt.AlignmentFlag.AlignCenter, display_name)
            painter.end()
            return
        super().paintEvent(event)

    def _on_card_config_changed(self):
        logger.debug(f"[CT] {self.component_id} 卡片配置变更")
        self._apply_card_style()
        if hasattr(self, '_apply_style'):
            self._apply_style()

    def _on_resize_debounce(self):
        logger.debug(f"[CT] {self.component_id} 防抖后尺寸 {self.width()}x{self.height()}")
        if self._delete_button and self._delete_button.isVisible():
            self._delete_button.reposition()
        if self._config_button and self._config_button.isVisible():
            self._config_button.reposition()


    def _scaled_px(self, base_px: int) -> int:
        """缩放换算
        Args:
            base_px: 基准像素

        Returns:
            换算后像素
        """
        return max(1, int(base_px * self._scale_factor))

    def _setup_periodic_refresh(self, interval_ms: int = 1800000):
        """
        Args:
            interval_ms: 刷新间隔
        """
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._refresh)
        self.timer.start(interval_ms)
        logger.debug(f"[CT] {self.component_id} 周期刷新已启动: {interval_ms}ms")
        self._refresh()

    def _transparent_widget(self, parent=None):
        w = QWidget(parent)
        w.setStyleSheet("background-color: transparent;")
        return w

    def _relay_web_drag(self, phase: str, x: float, y: float, view):
        """由html的鼠标拖拽转到组件容器

        Args:
            phase: 拖拽相位,"start" / "move" / "end"
        """
        if not self._draggable:
            return
        if phase == "start":
            logger.debug(f"[CT] {self.component_id} 网页拖拽转发 ({x:.0f} {y:.0f})")
        z = view.zoomFactor() or 1.0
        gp = view.mapToGlobal(QPoint(int(x * z), int(y * z)))
        lp = self.mapFromGlobal(gp)
        types = {"start": QEvent.Type.MouseButtonPress,
                 "move": QEvent.Type.MouseMove,
                 "end": QEvent.Type.MouseButtonRelease}
        ev = QMouseEvent(types[phase], QPointF(lp), QPointF(gp),
                         Qt.MouseButton.LeftButton,
                         Qt.MouseButton.NoButton if phase == "end" else Qt.MouseButton.LeftButton,
                         Qt.KeyboardModifier.NoModifier)
        if phase == "start":
            self.mousePressEvent(ev)
        elif self._dragging:
            if phase == "move":
                self.mouseMoveEvent(ev)
            else:
                self.mouseReleaseEvent(ev)

    def _card_bg_css(self, obj_name=None, bg_mode=None,
                     bg_color=None, opacity=None, radius=None, border=None):
        """
        Args:
            obj_name: objectName
            bg_mode: "opacity" 全局不透明度 / "custom" 自定义颜色
            bg_color: custom 模式背景色
            opacity: 不透明度
            border: 边框 QSS 片段 可选

        Returns:
            QSS 片段
        """
        # 参数缺省时依次取组件配置 > 全局 cfg.componentCardOpacity/componentCardRadius
        # 心疼啊这一轮5,340,002token花了两块多
        obj_name = obj_name or self.objectName()
        if not obj_name:
            return ""
        is_dark = isDarkTheme()
        op_val = opacity if opacity is not None else self._bg_opacity
        op_val = op_val if op_val is not None else cfg.componentCardOpacity.value
        rd_val = radius if radius is not None else self._corner_radius
        rd_val = rd_val if rd_val is not None else cfg.componentCardRadius.value
        mode = bg_mode or "opacity"
        if mode == "custom":
            c = QColor(bg_color or "#ffffff")
        else:
            c = QColor(30, 30, 30) if is_dark else QColor(255, 255, 255)
            c.setAlpha(int(255 * op_val / 100.0))
        css = (f"#{obj_name} {{ background-color: rgba({c.red()}, {c.green()}, "
               f"{c.blue()}, {c.alpha() / 255:.2f}); border-radius: {rd_val}px;")
        if border:
            css += f" border: {border};"
        return css + " }"

    def _doUpdateSize(self):
        _old_size = self.size()
        self._update_pending = False
        self.inner_layout.activate()
        if not getattr(self, '_size_explicitly_set', False):
            self.adjustSize()
        self.updateGeometry()
        if self.size() != _old_size:
            logger.debug(f"[CT] {self.component_id} 尺寸 {_old_size.width()}x{_old_size.height()} -> {self.size().width()}x{self.size().height()}")

    def _update_edit_controls_style(self):
        logger.debug(f"[CT] 组件 {self.component_id} 编辑控件样式刷新")
        if self._config_button and hasattr(self._config_button, 'apply_style'):
            self._config_button.apply_style()
        if self._delete_button and hasattr(self._delete_button, 'apply_style'):
            self._delete_button.apply_style()

    def _on_delete_clicked(self):
        logger.info(f"[CT] 组件 {self.component_id} 请求删除")
        home = self._getHomeInterface()
        if home:
            home.deleteSelectedComponent(self.component_id)

    def _on_config_clicked(self):
        home = self._getHomeInterface()
        if home and hasattr(home, 'component_manager'):
            comp_data = home.component_manager.get_component_data(self.component_id)
            dialog = ComponentConfigDialog(self, self.component_id, comp_data, home)

            lib_window = getattr(home, '_component_library_window', None)
            was_visible = bool(lib_window and lib_window.isVisible())
            if was_visible:
                lib_window.hide()

            result = dialog.exec()
            dialog.deleteLater() 

            if was_visible:
                lib_window.show()
                lib_window.raise_()

            if result:
                new_config = dialog.get_config()
                logger.info(f"[CT] 组件 {self.component_id} 配置已保存: 键={sorted(new_config.keys())}")
                home.component_manager.update_component_config(self.component_id, new_config)
                if hasattr(self, 'apply_config'):
                    self.apply_config(new_config)

    def _getHomeInterface(self):
        """
        Returns:
            HomeInterface/None
        """
        return _find_ancestor(self.parentWidget(), 'component_manager')

    def apply_scale(self, factor: float):
        if hasattr(self, '_apply_style'):
            self._apply_style()

    def _apply_scale_now(self):
        if self._applying_scale:
            return
        self._applying_scale = True
        try:
            self.apply_scale(self._scale_factor)
            if self._layout_bases is None:
                self._layout_bases = {}
            f = self._scale_factor
            for layout in self.findChildren(QLayout):
                base = self._layout_bases.get(id(layout))
                if base is None:
                    mm = layout.contentsMargins()
                    base = (mm.left(), mm.top(), mm.right(), mm.bottom(), layout.spacing())
                    self._layout_bases[id(layout)] = base
                layout.setContentsMargins(*(int(round(x * f)) for x in base[:4]))
                layout.setSpacing(int(round(base[4] * f)))
        finally:
            self._applying_scale = False

    def _apply_card_style(self, target=None, obj_name=None, bg_mode=None,
                          bg_color=None, opacity=None, radius=None, border=None):
        target = target or self
        obj_name = obj_name or target.objectName()
        css = self._card_bg_css(obj_name, bg_mode, bg_color, opacity, radius, border)
        if css:
            target.setStyleSheet(css)

def _create_edit_controls(parent_widget, component_widget, on_delete_clicked, on_config_clicked):
    """
    Args:
        parent_widget: 按钮挂父级

    Returns:
        (config_btn, delete_btn)
    """

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

    _CONFIG_HOVER = (0, 120, 212)
    _DELETE_HOVER = (220, 80, 80)

    def _reposition():
        comp_pos = component_widget.mapTo(parent_widget, QPoint(0, 0))
        # 右下角
        del_x = comp_pos.x() + component_widget.width() - delete_btn.width() + 4
        del_y = comp_pos.y() + component_widget.height() + 4
        delete_btn.move(del_x, del_y)
        # 左边
        config_btn.move(del_x - config_btn.width() - gap, del_y)

    def _apply_style():
        _btn_style(config_btn, _CONFIG_HOVER)
        _btn_style(delete_btn, _DELETE_HOVER)

    config_btn.reposition = _reposition
    delete_btn.reposition = _reposition
    config_btn.apply_style = _apply_style
    delete_btn.apply_style = _apply_style
    _apply_style()

    return config_btn, delete_btn

class _WebBridge(QObject):
    """html 组件桥"""

    def __init__(self, on_drag, on_commit=None, parent=None):
        super().__init__(parent)
        self._on_drag = on_drag
        self._on_commit = on_commit

    @pyqtSlot(str)
    def commit(self, payload_json: str):
        if self._on_commit is None:
            return
        try:
            logger.debug(f"[WB] 网页提交内容 ({len(payload_json)} 字符)")
            self._on_commit(payload_json)
        except Exception as e:
            logger.warning(f"网页提交失败: {e}")

    @pyqtSlot(float, float)
    def drag_start(self, x: float, y: float):
        logger.debug(f"[WB] 网页拖拽 ({x:.0f},{y:.0f})")
        self._on_drag("start", x, y)

    @pyqtSlot(float, float)
    def drag_move(self, x: float, y: float):
        self._on_drag("move", x, y)

    @pyqtSlot()
    def drag_end(self):
        logger.debug("[WB] 网页拖拽结束")
        self._on_drag("end", 0.0, 0.0)

def _install_web_view(container, bridge):
    """建 html 视图

    Args:
        container: 容器

    Returns:
        (view, channel)
    """
    from PyQt6.QtWebChannel import QWebChannel

    view = create_html_view(container, mouse_transparent=False)
    channel = QWebChannel(container)
    channel.registerObject("bridge", bridge)
    view.page().setWebChannel(channel)

    layout = container.inner_layout
    layout.setContentsMargins(0, 0, 0, 0)
    layout.setSpacing(0)
    layout.addWidget(view, 1)
    logger.debug(f"[WEB] 装配 组件={getattr(container, 'component_id', '?')} bridge={type(bridge).__name__}")
    return view, channel

class _HtmlCardComponent(DraggableContainer):
    """html 卡片组件基类"""

    _view_mouse_transparent = True          # 视图是否鼠标穿透
    _connect_load_finished = False          # 是否接 webView 的 loadFinished
    _min_size = (100, 100)                  # 最小尺寸
    _default_size = (200, 200)              # 初始尺寸

    def _setup_ui(self):
        if getattr(self, '_bridge', None) is not None:
            self.webView, self._channel = _install_web_view(self, self._bridge)
        else:
            self.webView = create_html_view(self, mouse_transparent=self._view_mouse_transparent)
            if self._connect_load_finished:
                self.webView.page().loadFinished.connect(self._on_load_finished)
        layout = self.inner_layout
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        layout.addWidget(self.webView)
        self.setMinimumSize(*self._min_size)
        self._size_explicitly_set = True
        self.resize(*self._default_size)
        self._hook_web_lifecycle()
        logger.debug(f"[HTML] 卡片视图就绪 id={self.component_id} 默认{self._default_size[0]}x{self._default_size[1]}")
        self._apply_style()

    def _hook_web_lifecycle(self):
        """挂载 WebEngine 页面生命周期日志（加载开始/完成、渲染进程终止）"""
        _page = self.webView.page()
        _cid = self.component_id
        _page.loadStarted.connect(
            lambda: logger.debug(f"[WEB] 组件 {_cid} 页面加载"))

        def _on_web_load_finished(ok):
            if not ok:
                logger.warning(f"[WEB] 组件 {self.component_id} 页面加载失败")
            elif not getattr(self, "_web_ok_logged", False):
                self._web_ok_logged = True
                logger.debug(f"[WEB] 组件 {self.component_id} 页面已加载")

        _page.loadFinished.connect(_on_web_load_finished)
        _page.renderProcessTerminated.connect(
            lambda status, code: logger.error(
                f"[WEB] 组件 {_cid} 渲染进程终止 状态={int(status)} 退出码={code}"))

    def _render(self):
        _html = self._build_html()
        logger.debug(f"[HTML] 组件 {self.component_id} html 构建: {len(_html)} 字符")
        self.webView.setHtml(_html, HTML_BASE_URL)

    def apply_scale(self, factor):
        self.webView.setZoomFactor(factor)

    def _apply_style(self):
        self._apply_card_style()
        self._render()

class ComponentConfigDialog(MessageBoxBase):
    """组件配置弹窗"""

    def __init__(self, component_widget, component_id, comp_data, home_interface):
        main_window = None
        if home_interface:
            main_window = home_interface.window()
        if main_window is None:
            main_window = component_widget.window() if component_widget else None
        super().__init__(main_window)

        self._comp_data = comp_data or {}
        self._config = dict(self._comp_data.get("config", {}))
        comp_type = self._comp_data.get("type", "")
        comp_style = self._comp_data.get("style", "")
        self._comp_key = f"{comp_type}|{comp_style}"
        self._fields = []

        self._setup_ui()
        for field in self._fields:
            field.load(self._config)
        logger.debug(f"[CFG] 配置弹窗打开: 组件={component_id} 类型={self._comp_key} 配置项数={len(self._fields)}")

    def _setup_ui(self):
        self.setWindowTitle(tr("component_edit.config_title"))
        self.widget.setMinimumSize(520, 480)

        self._pivot = Pivot(self)
        self._stack = QStackedWidget(self)

        # 基础设置
        self._basic_page = ScrollArea()
        self._basic_page.setWidgetResizable(True)
        basic_content = QWidget()
        layout = QVBoxLayout(basic_content)
        layout.setContentsMargins(0, 8, 0, 0)
        layout.setSpacing(14)
        groups = self._basic_defs()
        if groups:
            for title, fields in groups:
                self._add_group(layout, title, fields)
        else:
            label = BodyLabel(tr("component_edit.feature_pending_desc"), basic_content)
            label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            layout.addWidget(label)
        layout.addStretch()
        self._basic_page.setWidget(basic_content)
        self._basic_page.enableTransparentBackground()
        self._stack.addWidget(self._basic_page)
        self._pivot.addItem(
            "basic", tr("component_edit.config_basic"),
            onClick=lambda: self._stack.setCurrentIndex(0),
        )

        # 进阶设置
        self._advanced_page = ScrollArea()
        self._advanced_page.setWidgetResizable(True)
        advanced_content = QWidget()
        layout = QVBoxLayout(advanced_content)
        layout.setContentsMargins(0, 8, 0, 0)
        layout.setSpacing(14)
        for title, fields in self._advanced_defs():
            self._add_group(layout, title, fields)
        layout.addStretch()
        self._advanced_page.setWidget(advanced_content)
        self._advanced_page.enableTransparentBackground()
        self._stack.addWidget(self._advanced_page)
        self._pivot.addItem(
            "advanced", tr("component_edit.config_advanced"),
            onClick=lambda: self._stack.setCurrentIndex(1),
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

    # 配置字段
    class _Field:
        """配置字段基类"""
        # 子类写 build/load/save 三段:build 建控件并返回行布局,load 回填,save 读控件写结果
        def __init__(self, key, label_text, default):
            """Args:
                key: 配置字典键
                default: 配置缺项时的默认值
            """
            self.key = key
            self.label_text = label_text
            self.default = default

        def build(self, dialog) -> object:
            """创建控件与行布局"""
            raise NotImplementedError

        def load(self, config: dict):
            raise NotImplementedError

        def save(self, result: dict):
            """从控件读值写入结果"""
            # NotImplementedError: 子类必须实现
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
            val = bool(config.get(self.key, self.default))
            logger.debug(f"[CFG] 开关字段回填: {self.key} = {val}")
            self._widget.setChecked(val)

        def save(self, result):
            result[self.key] = self._widget.isChecked()
            logger.debug(f"[CFG] 开关字段保存: {self.key} = {result[self.key]}")

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
            val = int(config.get(self.key, self.default))
            logger.debug(f"[CFG] 数值字段回填: {self.key} = {val}")
            self._widget.setValue(val)

        def save(self, result):
            result[self.key] = self._widget.value()
            logger.debug(f"[CFG] 数值字段保存: {self.key} = {result[self.key]}")

    class _TextField(_Field):
        """文本字段"""
        def __init__(self, key, label_text, default, placeholder="", inline=False):
            super().__init__(key, label_text, default)
            self._placeholder = placeholder
            self._inline = inline
            self._widget = None

        def build(self, dialog):
            """标签 + LineEdit"""
            lbl = BodyLabel(self.label_text, dialog)
            self._widget = LineEdit(dialog)
            if self._inline:
                row = QHBoxLayout()
                lbl.setFixedWidth(80)
                self._widget.setMinimumWidth(200)
                row.addWidget(lbl)
                row.addWidget(self._widget, 1)
            else:
                row = QVBoxLayout()
                row.setSpacing(4)
                self._widget.setPlaceholderText(self._placeholder)
                row.addWidget(lbl)
                row.addWidget(self._widget)
            row.setContentsMargins(0, 0, 0, 0)
            return row

        def load(self, config):
            val = str(config.get(self.key, self.default))
            logger.debug(f"[CFG] 文本字段回填: {self.key} = '{val[:30]}'")
            self._widget.setText(val)

        def save(self, result):
            result[self.key] = self._widget.text()
            logger.debug(f"[CFG] 文本字段保存: {self.key} = '{result[self.key]}'")

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
            self._widget = Slider(Qt.Orientation.Horizontal, dialog)
            self._widget.setRange(self._min, self._max)
            self._widget.setValue(self.default)
            self._widget.valueChanged.connect(lambda v: self._val_lbl.setText(f"{v}{self._suffix}"))
            row.addWidget(self._widget)
            return row

        def load(self, config):
            val = int(config.get(self.key, self.default))
            logger.debug(f"[CFG] 滑块字段回填: {self.key} = {val}")
            self._widget.setValue(val)

        def save(self, result):
            result[self.key] = self._widget.value()
            logger.debug(f"[CFG] 滑块字段保存: {self.key} = {result[self.key]} (范围 {self._min}~{self._max})")

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
            color = config.get(self.key + "_color", self._default_color)
            logger.debug(f"[CFG] 颜色字段回填: {self.key} mode={mode} color={color}")
            self._combo.setCurrentIndex(1 if mode == "custom" else 0)
            self._picker.setColor(QColor(str(color)))

        def save(self, result):
            result[self.key + "_mode"] = "custom" if self._combo.currentIndex() == 1 else "opacity"
            result[self.key + "_color"] = _color_name(self._picker.color, "#ffffff")
            logger.debug(f"[CFG] 颜色字段保存: {self.key} mode={result[self.key + '_mode']} color={result[self.key + '_color']}")

    class _DateField(_Field):
        """日期字段"""
        def __init__(self, key, label_text, default=""):
            super().__init__(key, label_text, default)
            self._picker = None
            self._edit = None

        def _use_picker(self):
            return self._picker is not None

        def build(self, dialog):
            # DatePicker 用默认值(yyyy-MM-dd 解析成功才设);异常时用 LineEdit + 占位提示
            row = QHBoxLayout()
            row.setContentsMargins(0, 0, 0, 0)
            row.addWidget(BodyLabel(self.label_text, dialog))
            row.addStretch()
            try:
                from qfluentwidgets import DatePicker
                self._picker = DatePicker(dialog)
                d = QDate.fromString(str(self.default), "yyyy-MM-dd")
                if d.isValid():
                    self._picker.setDate(d)
                self._picker.setFixedWidth(240)
                row.addWidget(self._picker)
            except Exception as e:
                logger.debug(f"[CFG] DatePicker 不可用 用 LineEdit: {e}")
                self._picker = None
                self._edit = LineEdit(dialog)
                self._edit.setPlaceholderText("YYYY-MM-DD")
                self._edit.setFixedWidth(200)
                self._edit.setText(str(self.default))
                row.addWidget(self._edit)
            return row

        def load(self, config):
            val = str(config.get(self.key, self.default) or "")
            logger.debug(f"[CFG] 日期字段回填: {self.key} = '{val}'")
            if self._use_picker():
                d = QDate.fromString(val, "yyyy-MM-dd")
                if d.isValid():
                    self._picker.setDate(d)
            elif self._edit is not None:
                self._edit.setText(val)

        def save(self, result):
            if self._use_picker():
                result[self.key] = self._picker.date.toString("yyyy-MM-dd")
                logger.debug(f"[CFG] 日期字段保存: {self.key} = {result[self.key]} (选择器)")
            elif self._edit is not None:
                result[self.key] = self._edit.text()
                logger.debug(f"[CFG] 日期字段保存: {self.key} = '{result[self.key]}' (手动输入)")

    class _PlainColorField(_Field):
        """纯颜色字段"""
        def __init__(self, key, label_text, default_color):
            super().__init__(key, label_text, default_color)
            self._picker = None

        def build(self, dialog):
            row = QHBoxLayout()
            row.setContentsMargins(0, 0, 0, 0)
            lbl = BodyLabel(self.label_text, dialog)
            lbl.setFixedWidth(80)
            self._picker = ColorPickerButton(QColor(self.default), "", dialog, enableAlpha=False)
            self._picker.setFixedWidth(60)
            row.addWidget(lbl)
            row.addWidget(self._picker)
            row.addStretch()
            return row

        def load(self, config):
            # 非法颜色用 default
            color = str(config.get(self.key, self.default) or self.default)
            c = QColor(color)
            if not c.isValid():
                logger.warning(f"[CFG] 颜色值非法 用默认 {self.key}: '{color}' -> {self.default}")
            self._picker.setColor(c if c.isValid() else QColor(self.default))

        def save(self, result):
            result[self.key] = _color_name(self._picker.color, self.default)
            logger.debug(f"[CFG] 纯颜色字段保存: {self.key} = {result[self.key]}")

    class _AppListField(_Field):
        """应用列表字段"""
        def __init__(self, key, label_text, default):
            super().__init__(key, label_text, default)
            self._container = None
            self._dialog = None

        def build(self, dialog):
            self._dialog = dialog

            layout = QVBoxLayout()
            layout.setContentsMargins(0, 0, 0, 0)
            layout.setSpacing(8)
            layout.addWidget(BodyLabel(self.label_text, dialog))

            self._container = QVBoxLayout()
            self._container.setSpacing(6)
            layout.addLayout(self._container)

            self._add_btn = PushButton(FUI.ADD, tr("component_edit.config_app_add"), dialog)
            self._add_btn.clicked.connect(self._add_app)
            layout.addWidget(self._add_btn)

            self._render_list()
            return layout

        def _render_list(self):
            if not self._container:
                return
            while self._container.count():
                item = self._container.takeAt(0)
                if item and item.widget():
                    item.widget().deleteLater()

            apps = list(cfg.quickLaunchApps.value or [])
            if not apps:
                empty = BodyLabel(tr("component_edit.config_app_empty"), self._dialog)
                empty.setAlignment(Qt.AlignmentFlag.AlignCenter)
                self._container.addWidget(empty)
                return

            for i, app in enumerate(apps):
                self._container.addWidget(self._build_app_row(app, i))

        def _build_app_row(self, app, idx):
            # lambda 默认参数绑定 idx,防闭包晚绑定
            row = QWidget()
            row_layout = QHBoxLayout(row)
            row_layout.setContentsMargins(0, 0, 0, 0)
            row_layout.setSpacing(8)

            icon_lbl = QLabel()
            icon_lbl.setFixedSize(24, 24)
            icon_path = get_ql_icon_path(app.get("icon", ""))
            if icon_path and os.path.exists(icon_path):
                pm = QPixmap(icon_path)
                if not pm.isNull():
                    icon_lbl.setPixmap(pm.scaled(
                        24, 24, Qt.AspectRatioMode.KeepAspectRatio,
                        Qt.TransformationMode.SmoothTransformation))

            name_lbl = BodyLabel(app.get("name", ""), self._dialog)
            type_text = tr(f"component_edit.config_app_type_{app.get('type', 'app')}")
            type_lbl = BodyLabel(f"[{type_text}]", self._dialog)
            type_lbl.setObjectName("appTypeLabel")

            edit_btn = PushButton(tr("component_edit.config_app_edit"), self._dialog)
            edit_btn.setFixedHeight(32)
            edit_btn.clicked.connect(lambda _, i=idx: self._edit_app(i))

            del_btn = PushButton(tr("component_edit.config_app_delete"), self._dialog)
            del_btn.setFixedHeight(32)
            del_btn.clicked.connect(lambda _, i=idx: self._delete_app(i))

            row_layout.addWidget(icon_lbl)
            row_layout.addWidget(name_lbl, 1)
            row_layout.addWidget(type_lbl)
            row_layout.addWidget(edit_btn)
            row_layout.addWidget(del_btn)
            return row

        def _add_app(self):
            from ui.home import AppEditDialog
            d = AppEditDialog(self._dialog, None)
            rc = d.exec()
            d.deleteLater()
            if rc:
                result = d.get_app_data()
                if result:
                    apps = list(cfg.quickLaunchApps.value or [])
                    apps.append(result)
                    cfg.quickLaunchApps.value = apps
                    save_cfg()
                    logger.info(f"[CFG] 启动项新增: {result.get('name', '?')} (type={result.get('type', 'app')})")
                    self._render_list()

        def _edit_app(self, idx):
            from ui.home import AppEditDialog
            apps = list(cfg.quickLaunchApps.value or [])
            if idx >= len(apps):
                return
            d = AppEditDialog(self._dialog, apps[idx])
            rc = d.exec()
            d.deleteLater()
            if rc:
                result = d.get_app_data()
                if result:
                    logger.info(f"[CFG] 启动项修改 [{idx}]: '{apps[idx].get('name', '')}' -> '{result.get('name', '')}'")
                    apps[idx] = result
                    cfg.quickLaunchApps.value = apps
                    save_cfg()
                    self._render_list()

        def _delete_app(self, idx):
            apps = list(cfg.quickLaunchApps.value or [])
            if idx >= len(apps):
                return
            name = apps[idx].get("name", "")
            box = MessageBox(
                tr("component_edit.config_confirm_delete"),
                tr("component_edit.config_confirm_delete_msg", name=name),
                self._dialog
            )
            if box.exec():
                logger.info(f"[CFG] 启动项删除 [{idx}]: {name}")
                apps.pop(idx)
                cfg.quickLaunchApps.value = apps
                save_cfg()
                self._render_list()

        def load(self, config):
            """no-op,数据源是全局 cfg"""
            pass

        def save(self, result):
            """no-op,行内已落盘"""
            pass

    def _basic_defs(self):
        """基础页字段定义

        Returns:
            [(分组标题, [字段实例, ...]), ...]
        """
        defs = {
            "linkage|timetable_nowlesson": [
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
            "clock|digital": [
                (tr("component_edit.group_display"), [
                    self._SwitchField("show_seconds", tr("component_edit.config_show_seconds"), True),
                    self._SwitchField("show_lunar", tr("component_edit.config_show_lunar"), True),
                ]),
            ],
            "countdown|event": [
                (tr("component_edit.group_target"), [
                    self._TextField("event_name", tr("component_edit.config_event_name"), ""),
                    self._TextField("target_time", tr("component_edit.config_target_time"), "", "YYYY-MM-DD HH:MM"),
                ]),
            ],
            "countdown|days": [
                (tr("component_edit.group_target"), [
                    self._TextField("event_name", tr("component_edit.config_event_name"), ""),
                    self._DateField("target_date", tr("component_edit.config_target_date"), ""),
                    self._PlainColorField("title_bg_color", tr("component_edit.config_title_bg"), "#F98E1B"),
                ]),
            ],
            "school_info|class_info": [
                (tr("component_edit.group_info"), [
                    self._TextField("class", tr("component_edit.config_class"), "", inline=True),
                    self._TextField("school", tr("component_edit.config_school"), "", inline=True),
                    self._TextField("count", tr("component_edit.config_count"), "", inline=True),
                    self._TextField("slogan", tr("component_edit.config_slogan"), "", inline=True),
                ]),
            ],
            "weather|icon_temp": [
                (tr("component_edit.group_display"), [
                    self._SwitchField("show_icon", tr("component_edit.config_show_icon"), True),
                ]),
            ],
            "media|player": [
                (tr("component_edit.group_display"), [
                    self._SwitchField("show_progress", tr("component_edit.config_show_progress"), True),
                ]),
            ],
            "quick_launch|dock": [
                (tr("component_edit.group_apps"), [
                    self._AppListField("apps", tr("component_edit.config_apps"), []),
                ]),
            ],
        }
        return defs.get(self._comp_key, [])

    def _advanced_defs(self):
        """进阶页字段定义

        Returns:
            [(分组标题, [字段实例, ...]), ...]
        """
        defs = {
            "school_info|class_info": [
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
        default = [
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

    def _add_group(self, layout, title, fields):
        """字段收进 _fields

        Args:
            title: 分组标题,可为 None(不渲染标题行)
        """
        # 标题可选 例如班级卡片那个外观配置
        group_box = QWidget()
        group_layout = QVBoxLayout(group_box)
        group_layout.setContentsMargins(0, 0, 0, 0)
        group_layout.setSpacing(8)
        if title:
            group_layout.addWidget(StrongBodyLabel(title, self))
        content = QWidget()
        content_layout = QVBoxLayout(content)
        content_layout.setContentsMargins(12, 4, 0, 4)
        content_layout.setSpacing(10)
        for field in fields:
            content_layout.addLayout(field.build(self))
            self._fields.append(field)
        group_layout.addWidget(content)
        layout.addWidget(group_box)

    # 配置读写

    def get_config(self) -> dict:
        result = dict(self._config)
        for field in self._fields:
            field.save(result)
        logger.debug(f"[CFG] 配置已收集 键={sorted(result.keys())}")
        return result





WEEKDAY_KEYS = ("weekday.monday", "weekday.tuesday", "weekday.wednesday",
                "weekday.thursday", "weekday.friday", "weekday.saturday", "weekday.sunday")

SOLAR_TERMS_CN = [
    "小寒", "大寒", "立春", "雨水", "惊蛰", "春分",
    "清明", "谷雨", "立夏", "小满", "芒种", "夏至",
    "小暑", "大暑", "立秋", "处暑", "白露", "秋分",
    "寒露", "霜降", "立冬", "小雪", "大雪", "冬至",
]

# 中文月份名
MONTH_NAMES_CN = ["一月", "二月", "三月", "四月", "五月", "六月",
                  "七月", "八月", "九月", "十月", "十一月", "十二月"]

# 节日全称 > 简写
HOLIDAY_SHORT_MAP = {
    "元旦节": "元旦",
    "春节": "春节",
    "清明节": "清明",
    "国际劳动节": "劳动",
    "端午节": "端午",
    "中秋节": "中秋",
    "国庆节": "国庆",
    "元宵节": "元宵",
    "小年": "小年",
    "七夕-魁星诞": "七夕",
    "中元节": "中元",
    "重阳节-酆都大帝诞": "重阳",
    "腊八节-释迦如来成佛之辰": "腊八",
    "春龙节-福德土地正神诞": "龙抬头",
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
    if not name:
        return ""
    if "," in name:
        parts = [p.strip() for p in name.split(",")]
        for p in parts:
            if p in HOLIDAY_SHORT_MAP:
                return HOLIDAY_SHORT_MAP[p]
        return ""
    return HOLIDAY_SHORT_MAP.get(name, "")

_LUNAR_CACHE = {}    # 'y-m-d' -> (农历月文本, 农历日文本) | None(查询失败)
_DAY_INFO_CACHE = {}  # 'y-m-d' -> (节日, 节气)


def _lunar_text(year: int, month: int, day: int):
    key = f"{year}-{month}-{day}"
    if key not in _LUNAR_CACHE:
        try:
            import cnlunar
            lunar = cnlunar.Lunar(py_datetime.datetime(year, month, day, 0, 0, 0))
            _LUNAR_CACHE[key] = (lunar.lunarMonthCn.replace("大", "").replace("小", ""),
                                 lunar.lunarDayCn)
        except Exception as e:
            logger.debug(f"农历查询失败: {e}")
            _LUNAR_CACHE[key] = None
    return _LUNAR_CACHE[key]


def _get_day_info(year: int, month: int, day: int) -> tuple:
    key = f"{year}-{month}-{day}"
    if key in _DAY_INFO_CACHE:
        return _DAY_INFO_CACHE[key]
    holiday = ""
    solar_term = ""
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
    except Exception as e:
        logger.debug(f"农历角标查询失败: {e}")
    _DAY_INFO_CACHE[key] = (holiday, solar_term)
    return holiday, solar_term

class DigitalClockComponent(DraggableContainer):
    """数字时钟组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("clockContainer")
        self._last_time_text = None
        self._last_date_text = None
        self._setup_ui()
        self._setup_timer()
        self._update_time()
        logger.debug(f"[DC] 数字时钟就绪 id={self.component_id}")

    def _setup_ui(self):
        self.clockLabel = BodyLabel("00:00:00")
        self.clockLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.clockLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        self.dateLabel = CaptionLabel("")
        self.dateLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.dateLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(32, 24, 32, 24)
        layout.setSpacing(8)
        layout.addWidget(self.clockLabel, 3)
        layout.addWidget(self.dateLabel, 1)

        self.setMinimumSize(120, 80)
        self._size_explicitly_set = True
        self.resize(400, 200)
        self._apply_style()

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._update_time)
        self.timer.start(1000)
        logger.debug("[CLK] 数字时钟秒级定时启动")

        cfg.showClock.valueChanged.connect(self._update_time)
        cfg.showClockSeconds.valueChanged.connect(self._update_time)
        cfg.showLunarCalendar.valueChanged.connect(self._update_time)
        cfg.clockColor.valueChanged.connect(self._apply_style)
        cfg.clockSize.valueChanged.connect(self._apply_style)
        cfg.dateSize.valueChanged.connect(self._apply_style)

    def _update_time(self):
        if not cfg.showClock.value:
            self.clockLabel.hide()
            self.dateLabel.hide()
            home = self._getHomeInterface()
            if home is not None and home._edit_mode_active:
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

        _hm = currentTime.toString("HH:mm")
        if _hm != getattr(self, '_last_minute_key', ''):
            self._last_minute_key = _hm
            logger.debug(f"[DC] 时钟跨分钟: {_hm} (精确源={cfg.usePreciseTime.value})")

        if cfg.usePreciseTime.value:
            try:
                pn = precise_now()
                currentTime = QTime(pn.hour, pn.minute, pn.second)
                currentDate = QDate(pn.year, pn.month, pn.day)
            except Exception as e:
                if not getattr(self, "_precise_warned", False):
                    self._precise_warned = True
                    logger.warning(f"[DC] 精确时间源获取失败 用系统时间: {e}")

        if cfg.showClockSeconds.value:
            timeString = currentTime.toString("HH:mm:ss")
        else:
            timeString = currentTime.toString("HH:mm")
        self.clockLabel.setText(timeString)

        weekday_name = tr(WEEKDAY_KEYS[currentDate.dayOfWeek() - 1])
        solarString = tr("date.format", y=currentDate.year(), M=currentDate.month(),
                         d=currentDate.day(), w=weekday_name)

        if cfg.showLunarCalendar.value:
            lunar = _lunar_text(currentDate.year(), currentDate.month(), currentDate.day())
            dateString = f"{solarString} {lunar[0]}{lunar[1]}" if lunar else solarString
        else:
            dateString = solarString

        if timeString == self._last_time_text and dateString == self._last_date_text:
            return
        self._last_time_text = timeString
        self._last_date_text = dateString

        self.dateLabel.setText(dateString)
        self.updateSize()

    def _apply_style(self):
        self._apply_card_style()
        clock_color = cfg.clockColor.value
        color_str = clock_color.name() if hasattr(clock_color, 'name') else str(clock_color)
        clock_size = cfg.clockSize.value
        date_size = cfg.dateSize.value
        logger.debug(f"[DC] 样式 颜色={color_str} 时钟={self._scaled_px(clock_size)}px 日期={self._scaled_px(date_size)}px")

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

class _SvgClockComponent(_HtmlCardComponent):
    """SVG 表盘基类"""
    _min_size = (100, 100)
    _default_size = (200, 200)

    _radius_scale = 1

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName(self._object_name)
        self._setup_ui()
        logger.debug(f"[SVGCLK] 表盘初始化 id={self.component_id} 容器={self._object_name} 半径系数={self._radius_scale}")


    def _build_html(self) -> str:
        theme = self._theme_dark if isDarkTheme() else self._theme_light
        radius = cfg.componentCardRadius.value * self._radius_scale
        theme_name = "dark" if isDarkTheme() else "light"
        logger.debug(f"[SVGCLK] 重建表盘 html: id={self.component_id} 主题={theme_name} 半径={radius}")
        return self._HTML_TEMPLATE.substitute(font=FONT_FAMILY, radius=radius, **theme)

    def _apply_style(self):
        self.setStyleSheet(
            f"#{self._object_name} {{ background-color: transparent; "
            f"border-radius: {cfg.componentCardRadius.value}px; }}")
        self._render()

class SquareClock1Component(_SvgClockComponent):
    """方形钟表 I（SVG）"""

    _object_name = "squareClock1Container"
    _radius_scale = 2

    _theme_dark = {
        "face": "#000000",
        "tick_major": "#ffffff",
        "tick_minor": "#888888",
        "ink": "#ffffff",
        "second": "#C9A66B",
        "shadow_op": "0.5",
    }
    _theme_light = {
        "face": "#ffffff",
        "tick_major": "#1d1d1f",
        "tick_minor": "#777777",
        "ink": "#1d1d1f",
        "second": "#C9A66B",
        "shadow_op": "0.18",
    }
    # 下边这一段看哭了
    _HTML_TEMPLATE = Template('''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { width: 100%; height: 100%; background: transparent; overflow: hidden; }
  svg { width: 100%; height: 100%; display: block; }
</style>
</head>
<body>
<svg viewBox="0 0 400 400" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <filter id="shadow" x="-30%" y="-30%" width="160%" height="160%">
      <feDropShadow dx="0" dy="1.5" stdDeviation="2" flood-color="#000" flood-opacity="$shadow_op"/>
    </filter>
  </defs>

  <rect x="0" y="0" width="400" height="400" rx="$radius" ry="$radius" fill="$face"/>

  <!-- All ticks -->
  <g transform="translate(200,200)">
    <!-- Number-position hour ticks (12,3,6,9) -->
    <line x1="0" y1="-137" x2="0" y2="-177" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(0)"/>
    <line x1="0" y1="-137" x2="0" y2="-177" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(90)"/>
    <line x1="0" y1="-137" x2="0" y2="-177" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(180)"/>
    <line x1="0" y1="-137" x2="0" y2="-177" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(270)"/>

    <!-- Other hour ticks (1,2,4,5,7,8,10,11) — extended inward by half a small tick -->
    <line x1="0" y1="-157.5" x2="0" y2="-207.5" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(30)"/>
    <line x1="0" y1="-157.5" x2="0" y2="-207.5" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(60)"/>
    <line x1="0" y1="-157.5" x2="0" y2="-207.5" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(120)"/>
    <line x1="0" y1="-157.5" x2="0" y2="-207.5" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(150)"/>
    <line x1="0" y1="-157.5" x2="0" y2="-207.5" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(210)"/>
    <line x1="0" y1="-157.5" x2="0" y2="-207.5" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(240)"/>
    <line x1="0" y1="-157.5" x2="0" y2="-207.5" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(300)"/>
    <line x1="0" y1="-157.5" x2="0" y2="-207.5" stroke="$tick_major" stroke-width="3" stroke-linecap="butt" transform="rotate(330)"/>

    <!-- Normal small ticks -->
    <line x1="0" y1="-158.1" x2="0" y2="-178.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(6)"/>
    <line x1="0" y1="-161.4" x2="0" y2="-181.4" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(12)"/>
    <line x1="0" y1="-167.1" x2="0" y2="-187.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(18)"/>
    <line x1="0" y1="-175.6" x2="0" y2="-195.6" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(24)"/>
    <line x1="0" y1="-203.5" x2="0" y2="-223.5" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(36)"/>
    <line x1="0" y1="-203.5" x2="0" y2="-223.5" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(54)"/>
    <line x1="0" y1="-175.6" x2="0" y2="-195.6" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(66)"/>
    <line x1="0" y1="-167.1" x2="0" y2="-187.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(72)"/>
    <line x1="0" y1="-161.4" x2="0" y2="-181.4" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(78)"/>
    <line x1="0" y1="-158.1" x2="0" y2="-178.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(84)"/>
    <line x1="0" y1="-158.1" x2="0" y2="-178.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(96)"/>
    <line x1="0" y1="-161.4" x2="0" y2="-181.4" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(102)"/>
    <line x1="0" y1="-167.1" x2="0" y2="-187.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(108)"/>
    <line x1="0" y1="-175.6" x2="0" y2="-195.6" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(114)"/>
    <line x1="0" y1="-203.5" x2="0" y2="-223.5" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(126)"/>
    <line x1="0" y1="-203.5" x2="0" y2="-223.5" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(144)"/>
    <line x1="0" y1="-175.6" x2="0" y2="-195.6" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(156)"/>
    <line x1="0" y1="-167.1" x2="0" y2="-187.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(162)"/>
    <line x1="0" y1="-161.4" x2="0" y2="-181.4" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(168)"/>
    <line x1="0" y1="-158.1" x2="0" y2="-178.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(174)"/>
    <line x1="0" y1="-158.1" x2="0" y2="-178.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(186)"/>
    <line x1="0" y1="-161.4" x2="0" y2="-181.4" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(192)"/>
    <line x1="0" y1="-167.1" x2="0" y2="-187.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(198)"/>
    <line x1="0" y1="-175.6" x2="0" y2="-195.6" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(204)"/>
    <line x1="0" y1="-203.5" x2="0" y2="-223.5" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(216)"/>
    <line x1="0" y1="-203.5" x2="0" y2="-223.5" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(234)"/>
    <line x1="0" y1="-175.6" x2="0" y2="-195.6" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(246)"/>
    <line x1="0" y1="-167.1" x2="0" y2="-187.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(252)"/>
    <line x1="0" y1="-161.4" x2="0" y2="-181.4" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(258)"/>
    <line x1="0" y1="-158.1" x2="0" y2="-178.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(264)"/>
    <line x1="0" y1="-158.1" x2="0" y2="-178.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(276)"/>
    <line x1="0" y1="-161.4" x2="0" y2="-181.4" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(282)"/>
    <line x1="0" y1="-167.1" x2="0" y2="-187.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(288)"/>
    <line x1="0" y1="-175.6" x2="0" y2="-195.6" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(294)"/>
    <line x1="0" y1="-203.5" x2="0" y2="-223.5" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(306)"/>
    <line x1="0" y1="-203.5" x2="0" y2="-223.5" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(324)"/>
    <line x1="0" y1="-175.6" x2="0" y2="-195.6" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(336)"/>
    <line x1="0" y1="-167.1" x2="0" y2="-187.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(342)"/>
    <line x1="0" y1="-161.4" x2="0" y2="-181.4" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(348)"/>
    <line x1="0" y1="-158.1" x2="0" y2="-178.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(354)"/>

    <!-- Corner ticks (retracted by half a small tick) -->
    <line x1="0" y1="-219.1" x2="0" y2="-239.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(42)"/>
    <line x1="0" y1="-219.1" x2="0" y2="-239.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(48)"/>
    <line x1="0" y1="-219.1" x2="0" y2="-239.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(132)"/>
    <line x1="0" y1="-219.1" x2="0" y2="-239.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(138)"/>
    <line x1="0" y1="-219.1" x2="0" y2="-239.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(222)"/>
    <line x1="0" y1="-219.1" x2="0" y2="-239.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(228)"/>
    <line x1="0" y1="-219.1" x2="0" y2="-239.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(312)"/>
    <line x1="0" y1="-219.1" x2="0" y2="-239.1" stroke="$tick_minor" stroke-width="1.5" stroke-linecap="butt" transform="rotate(318)"/>
  </g>

  <!-- Numbers -->
  <g text-anchor="middle" fill="$ink" font-family="$font" font-weight="500">
    <text x="200" y="80" font-size="50" dy="0.35em">12</text>
    <text x="320" y="200" font-size="50" dy="0.35em">3</text>
    <text x="200" y="320" font-size="50" dy="0.35em">6</text>
    <text x="80" y="200" font-size="50" dy="0.35em">9</text>
  </g>

  <!-- Hands -->
  <g transform="translate(200,200)">
    <path id="hourHand" d="M -5 18 L -3 -72 L 0 -82 L 3 -72 L 5 18 Z" fill="$ink" transform="rotate(0)" filter="url(#shadow)"/>
    <path id="minuteHand" d="M -3.5 22 L -2.5 -118 L 0 -128 L 2.5 -118 L 3.5 22 Z" fill="$ink" transform="rotate(0)" filter="url(#shadow)"/>
    <g id="secondHand" transform="rotate(0)">
      <line x1="0" y1="28" x2="0" y2="-158" stroke="$second" stroke-width="2" stroke-linecap="round"/>
      <line x1="0" y1="0" x2="0" y2="32" stroke="$second" stroke-width="2" stroke-linecap="round"/>
    </g>
    <circle cx="0" cy="0" r="9" fill="$second"/>
    <circle cx="0" cy="0" r="4.5" fill="$face"/>
  </g>
</svg>
<script>
  var hourHand = document.getElementById('hourHand');
  var minuteHand = document.getElementById('minuteHand');
  var secondHand = document.getElementById('secondHand');
  function tick() {
    var now = new Date();
    var s = now.getSeconds() + now.getMilliseconds() / 1000;
    var m = now.getMinutes() + s / 60;
    var h = now.getHours() % 12 + m / 60;
    hourHand.setAttribute('transform', 'rotate(' + (h * 30) + ')');
    minuteHand.setAttribute('transform', 'rotate(' + (m * 6) + ')');
    secondHand.setAttribute('transform', 'rotate(' + (s * 6) + ')');
  }
  tick();
  requestAnimationFrame(function loop() { tick(); requestAnimationFrame(loop); });
</script>
</body>
</html>''')

class SquareClock2Component(_SvgClockComponent):
    """方形钟表 II（SVG）"""

    _object_name = "squareClock2Container"

    _theme_dark = {
        "face": "#000000",
        "ink": "#ffffff",
        "tick_old": "#333333",
        "tick_new": "#999999",
    }
    _theme_light = {
        "face": "#ffffff",
        "ink": "#000000",
        "tick_old": "#dddddd",
        "tick_new": "#555555",
    }

    _HTML_TEMPLATE = Template('''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { width: 100%; height: 100%; background: transparent; overflow: hidden; }
  svg { width: 100%; height: 100%; display: block; }
</style>
</head>
<body>
<svg viewBox="0 0 200 200" xmlns="http://www.w3.org/2000/svg">
  <rect x="0" y="0" width="200" height="200" rx="$radius" ry="$radius" fill="$face"/>
  <g id="ticks" stroke-width="2" stroke-linecap="round"></g>
  <text id="timeText" x="100" y="100" text-anchor="middle" dominant-baseline="central"
        fill="$ink" font-family="$font" font-weight="600" font-size="52" letter-spacing="-1">00:00</text>
</svg>
<script>
  var svgNS = 'http://www.w3.org/2000/svg';
  var ticksG = document.getElementById('ticks');
  var timeText = document.getElementById('timeText');
  var ticks = [];

  for (var i = 0; i < 60; i++) {
    var a = i * 6 * Math.PI / 180;
    var dx = Math.sin(a), dy = -Math.cos(a);
    var R = 90 / Math.max(Math.abs(dx), Math.abs(dy));
    if ((i * 6) % 90 === 42 || (i * 6) % 90 === 48) R -= 5;
    var l = document.createElementNS(svgNS, 'line');
    l.setAttribute('x1', (100 + dx * R).toFixed(2));
    l.setAttribute('y1', (100 + dy * R).toFixed(2));
    l.setAttribute('x2', (100 + dx * (R - 8)).toFixed(2));
    l.setAttribute('y2', (100 + dy * (R - 8)).toFixed(2));
    ticksG.appendChild(l);
    ticks.push(l);
  }

  var oldRGB = [parseInt('$tick_old'.substring(1, 3), 16), parseInt('$tick_old'.substring(3, 5), 16), parseInt('$tick_old'.substring(5, 7), 16)];
  var newRGB = [parseInt('$tick_new'.substring(1, 3), 16), parseInt('$tick_new'.substring(3, 5), 16), parseInt('$tick_new'.substring(5, 7), 16)];
  var GRAD_N = 35;

  function tick() {
    var now = new Date();
    var s = now.getSeconds();
    for (var i = 0; i < 60; i++) {
      var age = (s - i + 60) % 60;
      var c;
      if (age <= GRAD_N) {
        var t = Math.sqrt(age / GRAD_N);
        c = 'rgb(' + Math.round(newRGB[0] + (oldRGB[0] - newRGB[0]) * t) + ','
                   + Math.round(newRGB[1] + (oldRGB[1] - newRGB[1]) * t) + ','
                   + Math.round(newRGB[2] + (oldRGB[2] - newRGB[2]) * t) + ')';
      } else {
        c = '$tick_old';
      }
      ticks[i].setAttribute('stroke', c);
    }
    var hh = String(now.getHours()).padStart(2, '0');
    var mm = String(now.getMinutes()).padStart(2, '0');
    timeText.textContent = hh + ':' + mm;
  }
  tick();
  requestAnimationFrame(function loop() { tick(); requestAnimationFrame(loop); });
</script>
</body>
</html>''')

class _DayCell(QWidget):
    """月历单个格子"""

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
        """Args:
            day: 日号;空表示留白格
            sub_text: 角标文本(节日/节气/农历)
        """
        self._day = day
        self._sub_text = sub_text
        self._is_current_month = is_current_month
        self._is_today = is_today
        self._is_weekend = is_weekend
        self.update()

    def clear(self):
        # day=0 留白格
        self._day = 0
        self.update()

    def paintEvent(self, e):
        if self._day == 0:
            return
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)

        r = self.rect()
        w, h = r.width(), r.height()
        sz = min(w, h)

        mid = h * 0.62  # 62% 给日期

        day_font = QFont()
        day_font.setPixelSize(int(sz * 0.46))
        painter.setFont(day_font)

        if self._is_today:
            cx = r.center().x() + int(w * 0.02)
            cy = int(mid / 2)
            painter.setPen(Qt.PenStyle.NoPen)
            painter.setBrush(QColor("#00b7c3"))
            painter.drawEllipse(QPoint(cx, cy), int(sz * 0.40), int(sz * 0.40))

        dark = isDarkTheme()
        if self._is_today:
            painter.setPen(QColor("#ffffff"))
        elif not self._is_current_month:
            painter.setPen(QColor("#555555") if dark else QColor(0, 0, 0, 60))
        elif self._is_weekend:
            painter.setPen(QColor("#9a9a9a") if dark else QColor(0, 0, 0, 140))
        else:
            painter.setPen(QColor("#e8e8e8") if dark else QColor("#1a1a1a"))

        day_rect = QRect(r.left() + int(w * 0.04), 0, w, int(mid))
        painter.drawText(day_rect, Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignVCenter, str(self._day))

        # 节日文字
        if self._sub_text and self._is_current_month:
            sub_font = QFont()
            sub_font.setPixelSize(int(sz * 0.35))  # 节日字号
            sub_font.setBold(True)
            painter.setFont(sub_font)
            if self._is_today:
                painter.setPen(QColor("#ffffff"))
            else:
                painter.setPen(QColor("#c0c0c0") if dark else QColor(0, 0, 0, 170))
            sub_rect = QRect(r.left(), int(mid), w, int(h - mid))
            painter.drawText(sub_rect, Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignVCenter, self._sub_text)
        painter.end()

class CalendarMonthComponent(DraggableContainer):
    """月历组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("calendarContainer")
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        today = QDate.currentDate()
        self._display_year = today.year()
        self._display_month = today.month()

        self._cells = []
        self._setup_ui()
        self._setup_timer()
        self._refresh_calendar()
        logger.debug(f"[CAL] 月历就绪 id={self.component_id}")

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setContentsMargins(12, 10, 12, 8)
        layout.setSpacing(4)

        # 标题栏
        title_layout = QHBoxLayout()
        title_layout.setContentsMargins(0, 0, 0, 0)
        title_layout.setSpacing(0)

        self._title_label = SubtitleLabel("")
        self._title_label.setObjectName("calTitle")
        title_layout.addWidget(self._title_label)
        title_layout.addStretch()

        btn_layout = QVBoxLayout()
        btn_layout.setContentsMargins(0, 0, 0, 0)
        btn_layout.setSpacing(0)

        self._up_btn = TransparentToolButton(self)
        self._up_btn.setIcon(FUI.CHEVRON_UP.icon())
        self._up_btn.setFixedSize(self._scaled_px(28), self._scaled_px(18))
        self._up_btn.clicked.connect(lambda: self._go_month(-1))

        self._down_btn = TransparentToolButton(self)
        self._down_btn.setIcon(FUI.CHEVRON_DOWN.icon())
        self._down_btn.setFixedSize(self._scaled_px(28), self._scaled_px(18))
        self._down_btn.clicked.connect(lambda: self._go_month(1))

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
            lbl = CaptionLabel(n)
            lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
            lbl.setObjectName("calWk")
            if i == 0 or i == 6:
                lbl.setProperty("wkend", True)
            wk_layout.addWidget(lbl)
        layout.addLayout(wk_layout)

        # 6行日期
        grid_w = QWidget()
        grid = QGridLayout(grid_w)
        grid.setContentsMargins(0, 0, 0, 0)
        grid.setSpacing(0)
        for c in range(7):
            grid.setColumnStretch(c, 1)
        for r in range(6):
            grid.setRowStretch(r, 1)

        self._cells = []
        for r in range(6):
            row = []
            for c in range(7):
                cell = _DayCell(self)
                grid.addWidget(cell, r, c)
                row.append(cell)
            self._cells.append(row)

        layout.addWidget(grid_w, 1)

        self.setMinimumSize(120, 120)
        self._size_explicitly_set = True
        self.resize(300, 300)
        self._apply_style()

    def _setup_timer(self):
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._check_day_change)
        logger.debug("[CAL] 月历日切换定时器已创建")

    def showEvent(self, e):
        super().showEvent(e)
        _first = not getattr(self, "_cal_shown_once", False)
        self._cal_shown_once = True
        if _first:
            logger.debug(f"[CAL] 月历首次显示 id={self.component_id}")
        self._timer.start(60000)

    def hideEvent(self, e):
        self._timer.stop()
        logger.debug(f"[CAL] 月历隐藏 定时器已停止 id={self.component_id}")
        super().hideEvent(e)

    def _check_day_change(self):
        t = QDate.currentDate()
        last = getattr(self, "_last_seen_date", None)
        if last is not None and t != last:
            logger.info(f"[CAL] 跨日: {last.toString('yyyy-MM-dd')} -> {t.toString('yyyy-MM-dd')}")
        self._last_seen_date = t
        if t.year() == self._display_year and t.month() == self._display_month:
            self._refresh_calendar()

    def _go_month(self, delta: int):
        total = self._display_year * 12 + (self._display_month - 1) + delta
        self._display_year, m = divmod(total, 12)
        self._display_month = m + 1
        logger.debug(f"[CalendarComponent] 切换月份: {self._display_year}-{self._display_month:02d}")
        self._refresh_calendar()

    def _refresh_calendar(self):
        import calendar as cal
        year, month = self._display_year, self._display_month
        today = QDate.currentDate()

        # 标题
        self._title_label.setText(f"{MONTH_NAMES_CN[month-1]} {year}")

        first_wd = (cal.monthrange(year, month)[0] + 1) % 7  # 转为周日=0
        dim = cal.monthrange(year, month)[1]

        # 上月天数
        prev_month = month - 1 if month > 1 else 12
        prev_year = year if month > 1 else year - 1
        prev_dim = cal.monthrange(prev_year, prev_month)[1]

        # 清空
        for r in range(6):
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
        _lunar_marked = 0
        for r in range(6):
            for c in range(7):
                if r == 0 and c < first_wd:
                    continue
                if day > dim:
                    break

                is_today = (year == today.year() and month == today.month() and day == today.day())
                is_wkend = (c == 0 or c == 6)

                holiday, term = _get_day_info(year, month, day)
                sub = ""
                if holiday:
                    sub = _short_holiday(holiday)
                elif term:
                    sub = _short_holiday(term)
                if sub:
                    _lunar_marked += 1

                self._cells[r][c].set_data(
                    day=day, sub_text=sub, is_current_month=True,
                    is_today=is_today, is_weekend=is_wkend
                )

                day += 1

        self.updateSize()
        logger.debug(f"[CAL] 月历农历/节日标注: {_lunar_marked}天 ({year}-{month:02d})")
        logger.debug(f"[CAL] 月历已刷新 {year}-{month:02d} 今日 {today.month():02d}-{today.day():02d}")

    def apply_scale(self, factor):
        self._up_btn.setFixedSize(self._scaled_px(28), self._scaled_px(18))
        self._down_btn.setFixedSize(self._scaled_px(28), self._scaled_px(18))
        self._apply_style()

    def _apply_style(self):
        title_sz = self._scaled_px(20)
        wk_sz = self._scaled_px(13)
        title_c = _theme_pair("#f0f0f0", "#1a1a1a")
        wk_c = _theme_pair("rgba(255, 255, 255, 0.75)", "rgba(0, 0, 0, 0.55)")
        wkend_c = _theme_pair("rgba(255, 255, 255, 0.45)", "rgba(0, 0, 0, 0.35)")
        self.setStyleSheet(f"""
            {self._card_bg_css()}
            #calTitle {{
                color: {title_c};
                font-size: {title_sz}px;
                font-weight: 600;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}
            #calWk {{
                color: {wk_c};
                font-size: {wk_sz}px;
                font-family: {FONT_FAMILY};
                background: transparent;
            }}
            #calWk[wkend="true"] {{
                color: {wkend_c};
            }}
        """)
        for row in self._cells:
            for cell in row:
                cell.update()

class MiniCalendarComponent(_HtmlCardComponent):
    """简约月历(html)"""
    _view_mouse_transparent = False
    _min_size = (100, 100)
    _default_size = (200, 200)

    _object_name = "miniCalendarContainer"

    _theme_light = {
        "border_soft": "#eeeeee",
        "ink": "#444444",
        "title_ink": "#333333",
        "weekday_ink": "#999999",
        "other_ink": "#cccccc",
        "hover_bg": "#f2f2f2",
        "btn_ink": "#666666",
        "btn_hover_bg": "#f0f0f0",
        "today_ink": "#e5484d",
        "selected_ink": "#30c361",
    }
    _theme_dark = {
        "border_soft": "rgba(255,255,255,0.06)",
        "ink": "#d8d8dc",
        "title_ink": "#ececee",
        "weekday_ink": "#8a8a90",
        "other_ink": "#55555a",
        "hover_bg": "rgba(255,255,255,0.08)",
        "btn_ink": "#a0a0a6",
        "btn_hover_bg": "rgba(255,255,255,0.10)",
        "today_ink": "#ff6b6b",
        "selected_ink": "#4ade80",
    }

    _HTML_TEMPLATE = Template('''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body { width: 100%; height: 100%; background: transparent; overflow: hidden; }
  body { font-family: $font; }
  .calendar {
    width: 100%; height: 100%;
    display: flex; flex-direction: column;
    user-select: none; -webkit-user-select: none;
  }
  .cal-header {
    display: flex; align-items: center; justify-content: space-between;
    padding: 4px 6px;
    background: transparent;
    border-bottom: 1px solid $border_soft;
  }
  .cal-header button {
    width: 22px; height: 22px;
    border: none; background: transparent;
    color: $btn_ink; font-size: 13px; font-family: $font;
    cursor: pointer; border-radius: 4px; line-height: 22px;
    text-align: center; padding: 0;
    transition: background 0.15s;
  }
  .cal-header button:hover { background: $btn_hover_bg; color: $title_ink; }
  .cal-title {
    font-size: 12px; font-weight: 600; color: $title_ink; letter-spacing: 0.5px;
  }
  .cal-weekdays {
    display: grid; grid-template-columns: repeat(7, 1fr);
    padding: 2px 4px 0; background: transparent;
  }
  .cal-weekdays div {
    text-align: center; font-size: 9px; color: $weekday_ink;
    font-weight: 500; height: 16px; line-height: 16px;
  }
  .cal-days {
    display: grid; grid-template-columns: repeat(7, 1fr); grid-template-rows: repeat(6, 1fr);
    flex: 1; padding: 0 4px 4px;
    background: transparent;
  }
  .cal-day {
    display: flex; align-items: center; justify-content: center;
    font-size: 11px; color: $ink; font-weight: 400;
    border-radius: 4px; cursor: pointer; height: 100%;
    transition: background 0.15s, color 0.15s;
  }
  .cal-day:hover { background: $hover_bg; }
  .cal-day.other-month { color: $other_ink; }
  .cal-day.today { color: $today_ink; font-weight: 500; }
  .cal-day.selected { color: $selected_ink; font-weight: 500; }
</style>
</head>
<body>
<div class="calendar" id="calendar">
  <div class="cal-header">
    <button id="prev" aria-label="上个月">&#8249;</button>
    <div class="cal-title" id="title"></div>
    <button id="next" aria-label="下个月">&#8250;</button>
  </div>
  <div class="cal-weekdays">
    <div>日</div><div>一</div><div>二</div><div>三</div><div>四</div><div>五</div><div>六</div>
  </div>
  <div class="cal-days" id="days"></div>
</div>
<script>
  var daysEl = document.getElementById('days');
  var titleEl = document.getElementById('title');
  var today = new Date();
  var curYear = today.getFullYear();
  var curMonth = today.getMonth();

  function render(year, month) {
    titleEl.textContent = year + '年' + (month + 1) + '月';
    var startWeekday = new Date(year, month, 1).getDay();
    var daysInMonth = new Date(year, month + 1, 0).getDate();
    var daysInPrev = new Date(year, month, 0).getDate();
    daysEl.innerHTML = '';
    for (var i = startWeekday - 1; i >= 0; i--) {
      var d = document.createElement('div');
      d.className = 'cal-day other-month';
      d.textContent = daysInPrev - i;
      daysEl.appendChild(d);
    }
    var maxDay = Math.min(daysInMonth, 42 - startWeekday);
    for (var day = 1; day <= maxDay; day++) {
      var cell = document.createElement('div');
      cell.className = 'cal-day';
      cell.textContent = day;
      if (year === today.getFullYear() && month === today.getMonth() && day === today.getDate()) {
        cell.classList.add('today');
      }
      cell.addEventListener('click', function() {
        daysEl.querySelectorAll('.selected').forEach(function(el) { el.classList.remove('selected'); });
        this.classList.add('selected');
      });
      daysEl.appendChild(cell);
    }
    for (var n = startWeekday + maxDay; n < 42; n++) {
      daysEl.appendChild(document.createElement('div'));
    }
  }

  document.getElementById('prev').addEventListener('click', function() {
    curMonth--;
    if (curMonth < 0) { curMonth = 11; curYear--; }
    render(curYear, curMonth);
  });
  document.getElementById('next').addEventListener('click', function() {
    curMonth++;
    if (curMonth > 11) { curMonth = 0; curYear++; }
    render(curYear, curMonth);
  });

  render(curYear, curMonth);
</script>
</body>
</html>''')

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName(self._object_name)
        self._setup_ui()
        logger.debug(f"[MCAL] 简约月历初始化 id={self.component_id} 容器={self._object_name}")


    def _build_html(self) -> str:
        if not getattr(self, "_html_built_once", False):
            self._html_built_once = True
            logger.debug("[MCL] 迷你日历首次构建 html")
        theme = self._theme_dark if isDarkTheme() else self._theme_light
        return self._HTML_TEMPLATE.substitute(font=FONT_FAMILY, **theme)

class WeatherComponentBase(DraggableContainer):
    """天气组件族基类"""
    _weather_fetched = pyqtSignal(object)

    def __init__(self, parent, component_data: dict, layout_direction: str = "vertical"):
        super().__init__(parent, component_id=component_data["id"], layout_direction=layout_direction)
        self._home = parent
        self._home_interface = None
        p = self.parentWidget()
        while p is not None:
            if hasattr(p, 'weather_updated'):
                self._home_interface = p
                break
            p = p.parentWidget()
        self._current_icon_path = None
        self._weather_fetching = False
        logger.debug(f"[WX] 初始化 id={self.component_id} 信号={'有' if self._home_interface is not None else '无'}")

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._refresh_weather)
        self.timer.start(1000)
        logger.debug(f"[WX] 天气刷新定时器启动 id={self.component_id}")
        cfg.showWeather.valueChanged.connect(self._refresh_weather)
        cfg.weatherUpdateInterval.valueChanged.connect(self._update_interval)
        cfg.weatherTextColor.valueChanged.connect(self._apply_style)
        if self._home_interface is not None:
            self._home_interface.weather_updated.connect(self._update_from_cache)
        self._weather_fetched.connect(self._on_weather_fetched)
        self._update_interval()
        self._refresh_weather()

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton and hasattr(self, 'cityLabel'):
            local_pos = self.cityLabel.mapFrom(self, event.pos())
            if self.cityLabel.rect().contains(local_pos):
                logger.debug(f"[WX] 城市标签点击 id={getattr(self, 'component_id', '?')}")
                self._onCityLabelClicked()
                event.accept()
                return
        super().mousePressEvent(event)

    def _onCityLabelClicked(self):
        from services.weather import RegionSelectorDialog, RegionDatabase, WeatherService

        parent = getattr(self._home_interface, 'mainWindow', None) or self._home
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
                logger.info(f"选择城市: {region} (经纬度: {lon} {lat})")
            self.cityLabel.setText(region)

            if self._weather_fetching:
                return
            self._weather_fetching = True

            def _fetch():
                try:
                    ws = WeatherService()
                    data = ws.fetch_all()
                    self._weather_fetched.emit(data)
                except Exception as e:
                    logger.error(f"新城市天气获取失败: {e}")
                    self._weather_fetched.emit(None)

            threading.Thread(target=_fetch, daemon=True).start()
    def _update_interval(self):
        interval_map = {
            "10s": 10000, "30s": 30000, "1m": 60000,
            "5m": 300000, "10m": 600000, "30m": 1800000,
        }
        _old = self.timer.interval()
        self.timer.setInterval(interval_map.get(cfg.weatherUpdateInterval.value, 300000))
        if self.timer.interval() != _old:
            logger.info(f"[WX] 组件 {self.component_id} 刷新间隔变更: {_old}ms -> {self.timer.interval()}ms")

    def _refresh_weather(self):
        if not cfg.showWeather.value:
            if self.isVisible():
                logger.debug(f"[WX] 组件 {self.component_id} 显示开关已关闭 隐藏组件")
            self.hide()
            return
        if not self.isVisible():
            logger.debug(f"[WX] 组件 {self.component_id} 显示开关已开启 恢复显示")
        self.show()
        logger.debug(f"[WeatherComponent] 刷新 (城市: {cfg.city.value} 间隔: {cfg.weatherUpdateInterval.value})")
        self._update_from_cache()

    def _on_weather_fetched(self, data):
        self._weather_fetching = False
        if data:
            logger.info(f"[WeatherComponent] 天气数据已更新并广播 (城市: {cfg.city.value})")
            if not save_cache("weather", data, cfg.weatherUpdateInterval.value):
                logger.warning("缓存保存失败")
            if self._home_interface is not None:
                if hasattr(self._home_interface, '_cached_weather'):
                    self._home_interface._cached_weather = data
                self._home_interface.weather_updated.emit(data)
            else:
                self._update_from_cache()

    def _update_from_cache(self):
        raise NotImplementedError

    @staticmethod
    def _parse_current(data):
        """data.current > (温度, 天气码)

        Args:
            data: WeatherService.fetch_all dict/None
        """
        current = (data or {}).get("current", {})
        raw = current.get("temperature", {}).get("value", "--")
        try:
            temp = int(round(float(raw)))
        except (ValueError, TypeError):
            logger.warning(f"[WX] 温度解析失败 原样显示: '{raw}'")
            temp = raw
        try:
            code = int(current.get("weather", 0))
        except (ValueError, TypeError):
            logger.warning(f"[WX] 天气码解析失败 用 0: {current.get('weather')!r}")
            code = 0
        return temp, code

    def _current_from_cache(self):
        wd = get_cached_content("weather", ignore_expiry=True)
        if wd:
            temp, code = self._parse_current(wd)
            logger.debug("[WX] 天气走缓存")
        else:
            temp, code = "--", 0
        return wd, temp, code


class WeatherIconTempComponent(WeatherComponentBase):
    """极简天气"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_data, "horizontal")
        self.setObjectName("weatherContainer")
        self._setup_ui()
        self._setup_timer()
        logger.debug(f"[WX] 实况天气组件初始化 id={self.component_id}")
        cfg.weatherSize.valueChanged.connect(self._apply_style)
        cfg.weatherIconSize.valueChanged.connect(self._update_icon_size)

    def _setup_ui(self):
        self.tempLabel = BodyLabel("")
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

        self.setMinimumSize(80, 80)
        self._size_explicitly_set = True
        self.resize(200, 200)
        self._apply_style()

    def _update_from_cache(self):
        cached = get_cached_content("weather", ignore_expiry=True)
        if cached:
            self._update_display(cached)
        else:
            _had = getattr(self, "_wx_cache_miss_logged", False)
            if not _had:
                logger.debug(f"[WX] 无天气缓存 显示占位 '--°' id={self.component_id}")
                self._wx_cache_miss_logged = True
            self.tempLabel.setText("--°")
            self.iconLabel.clear()

    def _update_display(self, data):
        if not data:
            return

        from services.weather import WeatherService

        temp, weather_code = self._parse_current(data)
        self.tempLabel.setText(f"{temp}°")

        icon_name = WeatherService.ICON_MAP.get(weather_code, "2.svg")
        icon_path = WeatherService.get_weather_icon_path(icon_name)

        weather_text = WeatherService.WEATHER_MAP.get(weather_code, ("未知", "2.svg"))[0]
        logger.info(f"[WeatherComponent] 温度:{temp}° 码:{weather_code} 天气:{weather_text} 图标:{icon_name}")

        if icon_path and os.path.exists(icon_path):
            self._current_icon_path = icon_path
            icon_size = self._scaled_px(cfg.weatherIconSize.value)
            dpr = self.devicePixelRatioF()
            pm = render_svg_icon(icon_path, icon_size, dpr)
            if not pm.isNull():
                self.iconLabel.setPixmap(pm)
            else:
                logger.warning(f"[WX] 天气图标渲染为空: {icon_path}")

    def _update_icon_size(self):
        logger.debug(f"[WX] 图标尺寸变更: {cfg.weatherIconSize.value}px")
        cached = get_cached_content("weather", ignore_expiry=True)
        if cached:
            self._update_display(cached)

    def apply_scale(self, factor):
        self._apply_style()
        if self._current_icon_path and os.path.exists(self._current_icon_path):
            icon_size = self._scaled_px(cfg.weatherIconSize.value)
            dpr = self.devicePixelRatioF()
            pm = render_svg_icon(self._current_icon_path, icon_size, dpr)
            if not pm.isNull():
                self.iconLabel.setPixmap(pm)

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

class WeatherHourlyComponent(WeatherComponentBase):
    """逐小时天气"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_data, "vertical")
        self.setObjectName("weatherHourlyContainer")
        self._hourly_data = None
        self._hourly_icon_paths = [None] * 6
        self._setup_ui()
        self._setup_timer()
        logger.debug(f"[WXH] 逐小时天气组件初始化 id={self.component_id} 槽位数={len(self._hourly_icon_paths)}")

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        layout.setContentsMargins(28, 18, 28, 18)
        layout.setSpacing(6)

        # 上：城市 温度 图标 预警
        self._top_row = self._transparent_widget()
        top_layout = QHBoxLayout(self._top_row)
        top_layout.setContentsMargins(0, 0, 0, 0)
        top_layout.setSpacing(0)

        # 左：城市 温度
        left_col = self._transparent_widget()
        left_layout = QVBoxLayout(left_col)
        left_layout.setContentsMargins(0, 0, 0, 0)
        left_layout.setSpacing(2)

        self.cityLabel = SubtitleLabel("--")
        self.cityLabel.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.cityLabel.setCursor(Qt.CursorShape.PointingHandCursor)
        self.cityLabel.setToolTip(tr("weather_service.select_region"))

        self.currentTempLabel = BodyLabel("--°")
        self.currentTempLabel.setAlignment(Qt.AlignmentFlag.AlignLeft)

        left_layout.addWidget(self.cityLabel)
        left_layout.addWidget(self.currentTempLabel)
        left_layout.addStretch()

        # 右：图标 预警
        right_col = self._transparent_widget()
        right_layout = QVBoxLayout(right_col)
        right_layout.setContentsMargins(0, 0, 0, 0)
        right_layout.setSpacing(2)

        self.currentIconLabel = QLabel()
        self.currentIconLabel.setAlignment(Qt.AlignmentFlag.AlignRight)
        self.currentIconLabel.setFixedSize(self._scaled_px(60), self._scaled_px(60))

        self.alertLabel = CaptionLabel("")
        self.alertLabel.setAlignment(Qt.AlignmentFlag.AlignRight)

        right_layout.addWidget(self.currentIconLabel)
        right_layout.addWidget(self.alertLabel)
        right_layout.addStretch()

        top_layout.addWidget(left_col)
        top_layout.addStretch()
        top_layout.addWidget(right_col)

        # 下：6小时预报
        self._bottom_row = self._transparent_widget()
        bottom_layout = QHBoxLayout(self._bottom_row)
        bottom_layout.setContentsMargins(0, 0, 0, 0)
        bottom_layout.setSpacing(0)

        self._hourly_widgets = []
        for i in range(6):
            col = self._transparent_widget()
            col_layout = QVBoxLayout(col)
            col_layout.setContentsMargins(0, 0, 0, 0)
            col_layout.setSpacing(3)
            col_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

            time_label = CaptionLabel("--:00")
            time_label.setAlignment(Qt.AlignmentFlag.AlignCenter)

            icon_label = QLabel()
            icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            icon_label.setFixedSize(self._scaled_px(28), self._scaled_px(28))

            temp_label = CaptionLabel("--°")
            temp_label.setAlignment(Qt.AlignmentFlag.AlignCenter)

            col_layout.addWidget(time_label)
            col_layout.addWidget(icon_label)
            col_layout.addWidget(temp_label)

            bottom_layout.addWidget(col, 1)
            self._hourly_widgets.append((time_label, icon_label, temp_label))

        layout.addWidget(self._top_row, 3)
        layout.addWidget(self._bottom_row, 1)

        self.setMinimumSize(160, 100)
        self._size_explicitly_set = True
        self.resize(400, 200)
        self._top_row.setMinimumHeight(self._scaled_px(100))
        self._apply_style()

    def _update_from_cache(self):
        from services.weather import WeatherService

        wd, current_temp, current_icon_code = self._current_from_cache()

        self.cityLabel.setText(cfg.city.value)
        self.currentTempLabel.setText(f"{current_temp}°")

        icon_name = WeatherService.ICON_MAP.get(current_icon_code, "2.svg")
        icon_path = WeatherService.get_weather_icon_path(icon_name)

        weather_text = WeatherService.WEATHER_MAP.get(current_icon_code, ("未知", "2.svg"))[0]
        logger.info(f"[WeatherHourly] 城市:{cfg.city.value} 温度:{current_temp}° 码:{current_icon_code} 天气:{weather_text} 图标:{icon_name}")

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

        parsed = WeatherService.parse_hourly(wd["forecastHourly"]) if wd and wd.get("forecastHourly") else None
        if parsed:
            self._hourly_data = parsed
            logger.info(f"[WeatherHourly] 逐小时解析结果: {json.dumps(parsed, ensure_ascii=False)}")
        else:
            self._hourly_data = None
        self._update_hourly_display()

    def _update_hourly_display(self):
        data = self._hourly_data or {}
        hours = data.get("hours", [])
        pub_time = data.get("pub_time", "")

        from services.weather import WeatherService

        start_hour = 0
        try:
            if pub_time:
                start_hour = py_datetime.datetime.fromisoformat(pub_time).hour
        except Exception as e:
            logger.debug(f"[WeatherHourly] 发布时间解析失败({pub_time}) 从 0 时起算: {e}")

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
        logger.debug(f"[WXH] 逐小时已渲染 {len(hours)}小时 发布 {pub_time or '未知'}")

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

class WeatherWeeklyComponent(WeatherComponentBase):
    """逐日天气"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_data, "vertical")
        self.setObjectName("weatherWeeklyContainer")
        self._daily_data = None
        self._daily_icon_paths = [None] * 4
        self._setup_ui()
        self._setup_timer()
        logger.debug(f"[WXW] 每日天气组件初始化 id={self.component_id} 槽位数={len(self._daily_icon_paths)}")

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        layout.setContentsMargins(14, 12, 14, 12)
        layout.setSpacing(4)

        # 上：当前天气
        top = self._transparent_widget()
        top_layout = QHBoxLayout(top)
        top_layout.setContentsMargins(0, 0, 0, 0)
        top_layout.setSpacing(0)

        # 左上：城市 当前温度
        top_left = self._transparent_widget()
        tl_layout = QVBoxLayout(top_left)
        tl_layout.setContentsMargins(0, 0, 0, 0)
        tl_layout.setSpacing(2)

        self.cityLabel = SubtitleLabel("--")
        self.cityLabel.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.cityLabel.setCursor(Qt.CursorShape.PointingHandCursor)
        self.cityLabel.setToolTip(tr("weather_service.select_region"))

        self.currentTempLabel = BodyLabel("--°")
        self.currentTempLabel.setAlignment(Qt.AlignmentFlag.AlignLeft)

        tl_layout.addWidget(self.cityLabel)
        tl_layout.addWidget(self.currentTempLabel)
        tl_layout.addStretch()

        # 右上：图标
        top_right = self._transparent_widget()
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

        # 下：4天天气预报
        bottom = self._transparent_widget()
        bottom_layout = QVBoxLayout(bottom)
        bottom_layout.setContentsMargins(0, 0, 0, 0)
        bottom_layout.setSpacing(0)

        self._forecast_rows = []
        for i in range(4):
            row = self._transparent_widget()
            row.setFixedHeight(self._scaled_px(20))
            row_layout = QHBoxLayout(row)
            row_layout.setContentsMargins(0, 0, 0, 0)
            row_layout.setSpacing(0)

            day_label = CaptionLabel("--")
            day_label.setFixedWidth(self._scaled_px(40))
            day_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)

            icon_label = QLabel()
            icon_label.setFixedSize(self._scaled_px(18), self._scaled_px(18))
            icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)

            low_label = CaptionLabel("--")
            low_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
            low_label.setObjectName(f"weeklyLow_{i}")

            spacer = QLabel()
            spacer.setFixedWidth(self._scaled_px(8))

            high_label = CaptionLabel("--°")
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

            self._forecast_rows.append((row, day_label, icon_label, low_label, high_label, spacer))
            bottom_layout.addWidget(row)

        layout.addWidget(top, 3)
        layout.addWidget(bottom, 1)

        self.setMinimumSize(80, 80)
        self._size_explicitly_set = True
        self.resize(200, 200)
        self._apply_style()

    def _update_from_cache(self):
        from services.weather import WeatherService

        wd, current_temp, current_icon_code = self._current_from_cache()

        self.cityLabel.setText(cfg.city.value)
        self.currentTempLabel.setText(f"{current_temp}°")

        icon_name = WeatherService.ICON_MAP.get(current_icon_code, "2.svg")
        icon_path = WeatherService.get_weather_icon_path(icon_name)

        weather_text = WeatherService.WEATHER_MAP.get(current_icon_code, ("未知", "2.svg"))[0]
        logger.info(f"[WeatherWeekly] 城市:{cfg.city.value} 温度:{current_temp}° 码:{current_icon_code} 天气:{weather_text} 图标:{icon_name}")

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

        parsed = WeatherService.parse_daily(wd["forecastDaily"]) if wd and wd.get("forecastDaily") else None
        if parsed:
            self._daily_data = parsed
            logger.info(f"[WeatherWeekly] 每日预报解析结果: {json.dumps(parsed, ensure_ascii=False)}")
            if parsed.get("days"):
                d0 = parsed["days"][0]
                logger.info(f"[WeatherWeekly] 今日 高温:{d0.get('high')}° 低温:{d0.get('low')}° 码:{d0.get('weather_code')} 图标:{d0.get('icon')}")
        else:
            self._daily_data = None
        self._update_daily_display()

    def _update_daily_display(self):
        from services.weather import WeatherService

        days = (self._daily_data or {}).get("days", [])

        now = py_datetime.datetime.now()
        num_days = min(4, len(self._forecast_rows), len(days))
        if num_days > 0:
            logger.debug(f"[WXW] 周预报渲染: {num_days}天 (数据源{len(days)}天)")

        for i in range(num_days):
            row, day_label, icon_label, low_label, high_label, spacer = self._forecast_rows[i]
            d = now + py_datetime.timedelta(days=i)
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

        for i in range(num_days, len(self._forecast_rows)):
            row, day_label, icon_label, low_label, high_label, spacer = self._forecast_rows[i]
            self._daily_icon_paths[i] = None
            day_label.setText("--")
            icon_label.clear()
            low_label.setText("--")
            high_label.setText("--°")

        self._apply_style()
        logger.debug(f"[WXW] 每日已渲染 {num_days}/{len(days)}天")

    def apply_scale(self, factor):
        self._apply_style()
        dpr = self.devicePixelRatioF()
        self.currentIconLabel.setFixedSize(self._scaled_px(48), self._scaled_px(48))
        if self._current_icon_path and os.path.exists(self._current_icon_path):
            pm = render_svg_icon(self._current_icon_path, self._scaled_px(48), dpr)
            if not pm.isNull():
                self.currentIconLabel.setPixmap(pm)
        for i, (row, day_label, icon_label, low_label, high_label, spacer) in enumerate(self._forecast_rows):
            row.setFixedHeight(self._scaled_px(20))
            day_label.setFixedWidth(self._scaled_px(40))
            spacer.setFixedWidth(self._scaled_px(8))
            high_label.setFixedWidth(self._scaled_px(28))
            icon_label.setFixedSize(self._scaled_px(18), self._scaled_px(18))
            p = self._daily_icon_paths[i] if i < len(self._daily_icon_paths) else None
            if p and os.path.exists(p):
                pm = render_svg_icon(p, self._scaled_px(18), dpr)
                if not pm.isNull():
                    icon_label.setPixmap(pm)

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

        for row, day_label, icon_label, low_label, high_label, spacer in self._forecast_rows:
            row.setFixedHeight(self._scaled_px(20))
            day_label.setFixedWidth(self._scaled_px(40))
            spacer.setFixedWidth(self._scaled_px(8))
            high_label.setFixedWidth(self._scaled_px(28))
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

class PoetryOneLineComponent(DraggableContainer):
    """一言组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("poetryContainer")
        self._home = parent
        w = parent
        while w is not None:
            if hasattr(w, 'poetry_updated') and hasattr(w, '_cached_poetry'):
                self._home = w
                break
            w = w.parent()
        self._setup_ui()
        self._setup_timer()
        logger.debug(f"[POE] 一言就绪 id={self.component_id}")

    def _setup_ui(self):
        self.poetryLabel = BodyLabel("")
        self.poetryLabel.setObjectName("poetryLabel")
        self.poetryLabel.setAlignment(Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignVCenter)
        self.poetryLabel.setWordWrap(True)
        self.poetryLabel.setTextFormat(Qt.TextFormat.RichText)
        self.poetryLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.addWidget(self.poetryLabel, 1)

        self.setMinimumSize(120, 80)
        self._size_explicitly_set = True
        self.resize(400, 200)
        self._apply_style()

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._refresh_poetry)
        self.timer.start(1000)
        logger.debug(f"[POE] 诗词刷新定时器启动 id={self.component_id}")

        cfg.showPoetry.valueChanged.connect(self._refresh_poetry)
        cfg.poetryApiUrl.valueChanged.connect(self._refresh_poetry)
        cfg.poetryUpdateInterval.valueChanged.connect(self._update_interval)
        cfg.poetryTextColor.valueChanged.connect(self._apply_style)
        cfg.poetrySize.valueChanged.connect(self._apply_style)

        if hasattr(self._home, 'poetry_updated'):
            self._home.poetry_updated.connect(self._update_poetry)

        self._update_interval()
        self._refresh_poetry()

    def _format_poetry(self, text):
        """格式化文本
        Args:
            text: api返回的文本

        Returns:
            html 片段
        """
        if not text:
            logger.debug("[诗歌] 一言文本为空 返回空片段")
            return ""
        content = text.strip()
        attribution = ""
        for sep in ["——", "—"]:
            if sep in content:
                content, _, attribution = content.partition(sep)
                content = content.strip()
                attribution = attribution.strip()
                break
        # 逗号换行
        content = re.sub(r'([,，])', r'\1<br>', content).strip()
        content = content.removesuffix('<br>').rstrip()
        if attribution:
            return f"<div>{content}</div><div>{attribution}</div>"
        return f"<div>{content}</div>"

    def _update_poetry(self, text):
        if text:
            self.poetryLabel.setText(self._format_poetry(text))
            self.updateSize()
            logger.debug(f"[PoetryComponent] 一言展示更新: {text[:30]}")

    def _update_interval(self):
        interval_map = {"10s": 10000, "30s": 30000, "1m": 60000, "5m": 300000, "10m": 600000, "30m": 1800000, "1h": 3600000}
        interval_str = cfg.poetryUpdateInterval.value
        _old = self.timer.interval()
        self.timer.setInterval(interval_map.get(interval_str, 60000))
        if self.timer.interval() != _old:
            logger.info(f"[Poetry] 一言刷新间隔变更: {_old}ms -> {self.timer.interval()}ms")

    def _refresh_poetry(self):
        if not cfg.showPoetry.value:
            if self.isVisible():
                logger.debug("[Poetry] 一言显示开关已关闭 隐藏组件")
            self.hide()
            return
        if not self.isVisible():
            logger.debug("[Poetry] 一言显示开关已开启 恢复显示")
        self.show()

        text = None
        if hasattr(self._home, '_cached_poetry') and self._home._cached_poetry:
            text = self._home._cached_poetry
        if not text:
            text = get_cached_content("poetry")
        if text:
            logger.debug(f"[POE] 一言源{'首页缓存' if text == getattr(self._home, '_cached_poetry', None) else '本地缓存'}: {text[:30]}")
            self.poetryLabel.setText(self._format_poetry(text))
        else:
            logger.debug("[PoetryComponent] 无一言数据 显示为空")
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

def _render_svg_logo(icon_path, height=30):
    renderer = QSvgRenderer(icon_path)
    if not renderer.isValid():
        logger.warning(f"[NEWS] 来源图标渲染失败: {icon_path}")
        return QPixmap()
    default_size = renderer.defaultSize()
    ratio = 1.0
    if default_size.isValid() and default_size.height() > 0:
        ratio = default_size.width() / default_size.height()
    return _svg_to_pixmap(icon_path, int(height * ratio), height)

class NewsComponent(DraggableContainer):
    """新闻热榜基类"""

    # 子类配置
    _source = ""           # 数据源标识
    _icon_key = ""         # NEWS_ICONS 中的键
    _object_name = ""      # 容器 objectName
    _num_color_dark = "rgba(255, 255, 255, 0.55)"  # 序号颜色
    _num_color_light = "rgba(0, 0, 0, 0.45)"
    _text_color_dark = "#ffffff"   # 正文颜色
    _text_color_light = "#1a1a1a"
    _item_count = 4        # 显示条目数
    _use_cctv_api = False  # 是否央视新闻api

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName(self._object_name)
        self._news_titles = ["--"] * self._item_count
        self._news_urls = [""] * self._item_count
        self._icon_path = get_resPath(NEWS_ICONS[self._icon_key])
        self._setup_ui()
        self._setup_timer()
        logger.debug(f"[NEWS] 热榜就绪 id={self.component_id} 源={self._source} 条目={self._item_count}")

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
            item_label = BodyLabel("--")
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

        self.setMinimumSize(150, 100)
        self._size_explicitly_set = True
        self.resize(360, 220)
        self._apply_style()

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._refresh_news)
        self.timer.start(300000)
        logger.debug(f"[NewsComponent] 新闻刷新定时器启动: 300000ms (来源: {'央视' if self._use_cctv_api else self._source})")
        self._refresh_news()

    def _refresh_news(self):
        if self._use_cctv_api:
            data = NewsService.fetch_cctv_news(use_cache=True)
        else:
            data = NewsService.fetch_daily_news(self._source, use_cache=True)
        if not data:
            data = get_cached_content(f"news_{self._source}", ignore_expiry=True)
            if data:
                logger.debug(f"[NewsComponent] 接口无数据 用过期缓存: {len(data)}条")
            else:
                logger.debug(f"[NewsComponent] 无新闻数据 (来源: {'央视' if self._use_cctv_api else self._source})")
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
        _changed = getattr(self, "_news_titles", None) != titles
        self._news_urls = urls
        self._news_titles = titles
        if _changed and titles != ["--"] * count:
            logger.debug(f"[NewsComponent] 标题列表更新: 首条='{titles[0]}' 源={self._source or '央视'}")
        self._render_items()

    def _render_items(self):
        sz_num = self._scaled_px(12)
        sz_text = self._scaled_px(15)
        num_c = _theme_pair(self._num_color_dark, self._num_color_light)
        text_c = _theme_pair(self._text_color_dark, self._text_color_light)
        for i, (label, text) in enumerate(zip(self.itemWidgets, self._news_titles)):
            label.setText(
                f"<span style='font-size:{sz_num}px;color:{num_c};font-family:{FONT_FAMILY};'>"
                f"{i+1}.</span> "
                f"<span style='font-size:{sz_text}px;color:{text_c};font-family:{FONT_FAMILY};'>{text}</span>"
            )
        logger.debug(f"[NewsComponent] 新闻已渲染 {len(self._news_titles)}条")

    def _on_news_clicked(self, index):
        if 0 <= index < len(self._news_urls) and self._news_urls[index]:
            logger.info(f"[NewsComponent] 打开新闻: {self._news_titles[index]}")
            webbrowser.open(self._news_urls[index])

    def apply_scale(self, factor):
        """重刷样式"""
        dpr = self.devicePixelRatioF()
        pm = _render_svg_logo(self._icon_path, self._scaled_px(30))
        pm.setDevicePixelRatio(dpr)
        self.iconLabel.setPixmap(pm)
        self.iconLabel.setFixedSize(int(pm.width() / dpr), int(pm.height() / dpr))
        self._render_items()

    def _apply_style(self):
        self._apply_card_style()
        self._render_items()
        self.updateSize()

class CountdownEventComponent(DraggableContainer):
    """时间倒计时"""

    _accept_press = True

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("countdownContainer")
        self._warned_bad_time = None
        self._read_config(component_data.get("config", {}))
        self._setup_ui()
        self._setup_timer()
        logger.debug(f"[CD] 倒计时就绪 id={self.component_id} 事件='{self._event_name or '-'}'")

    def _setup_ui(self):
        self.countdownLabel = BodyLabel("")
        self.countdownLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.countdownLabel.setWordWrap(True)
        self.countdownLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(24, 20, 24, 20)
        layout.addWidget(self.countdownLabel, 1)

        self.setMinimumSize(80, 80)
        self._size_explicitly_set = True
        self.resize(200, 200)
        self._apply_style()

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._update_countdown)
        self.timer.start(1000)
        cfg.countdownTextColor.valueChanged.connect(self._apply_style)
        cfg.countdownTextSize.valueChanged.connect(self._apply_style)
        self._update_countdown()

    def apply_config(self, config):
        _old = (getattr(self, "_event_name", ""), getattr(self, "_target_time", ""))
        self._read_config(config)
        self._update_countdown()
        logger.info(f"[CD] 倒计时配置应用: {self._event_name or '未命名'} -> {self._target_time or '未设置'}")
        if _old != (self._event_name, self._target_time):
            logger.info(f"[CD] 倒计时配置变更: {_old} -> {(self._event_name, self._target_time)}")

    def mouseReleaseEvent(self, event):
        if self._handle_config_click_release(event):
            return
        super().mouseReleaseEvent(event)

    def _read_config(self, config):
        self._event_name = config.get("event_name", getattr(self, "_event_name", "")) or ""
        self._target_time = config.get("target_time", getattr(self, "_target_time", "")) or ""
        if self._event_name:
            logger.debug(f"[CountdownComponent] 倒计时: {self._event_name} -> {self._target_time}")

    def _is_configured(self):
        return bool(self._event_name and self._target_time)

    def _update_countdown(self):
        if not self._is_configured():
            self.countdownLabel.setText(tr("countdown.click_to_config"))
            self.updateSize()
            return

        name = self._event_name
        try:
            target_dt = py_datetime.datetime.fromisoformat(self._target_time)
            now = py_datetime.datetime.now()
            delta = target_dt - now
            now_positive = delta.total_seconds() > 0
            if getattr(self, "_prev_delta_positive", True) and not now_positive:
                logger.info(f"[CD] 倒计时归零: {name} (目标 {self._target_time})")
            self._prev_delta_positive = now_positive

            if delta.total_seconds() > 0:
                days = delta.days
                hours, remainder = divmod(delta.seconds, 3600)
                minutes, seconds = divmod(remainder, 60)
                text = f"{name}\n{days}天 {hours}时 {minutes}分 {seconds}秒"
            else:
                text = f"{name}\n{tr('countdown.expired')}"
            self.countdownLabel.setText(text)
        except Exception:
            # 时间格式无效时只显示事件名
            if self._warned_bad_time != self._target_time:
                self._warned_bad_time = self._target_time
                logger.warning(f"[CountdownComponent] 目标时间格式无效: {self._target_time}")
            self.countdownLabel.setText(name)
        self.updateSize()

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

class DaysMatterComponent(DraggableContainer):
    """倒数日组件"""
    # 太稀奇了今天居然没用html写
    _accept_press = True

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("daysMatterContainer")
        self._read_config(component_data.get("config", {}))
        self._setup_ui()
        self._setup_timer()
        logger.debug(f"[DM] 倒数日就绪 id={self.component_id} 事件='{self._event_name or '-'}'")

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        # 顶标题
        self.headerWidget = QWidget(self)
        self.headerWidget.setObjectName("daysMatterHeader")
        header_layout = QHBoxLayout(self.headerWidget)
        header_layout.setContentsMargins(8, 0, 8, 0)
        self.headerLabel = BodyLabel("", self.headerWidget)
        self.headerLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        header_layout.addWidget(self.headerLabel)

        # 中数字
        self.numLabel = BodyLabel("", self)
        self.numLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.numLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        # 底日期
        self.footerWidget = QWidget(self)
        self.footerWidget.setObjectName("daysMatterFooter")
        footer_layout = QHBoxLayout(self.footerWidget)
        footer_layout.setContentsMargins(6, 0, 6, 0)
        self.footerLabel = BodyLabel("", self.footerWidget)
        self.footerLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        footer_layout.addWidget(self.footerLabel)

        layout.addWidget(self.headerWidget)
        layout.addWidget(self.numLabel, 1)
        layout.addWidget(self.footerWidget)

        self.setMinimumSize(120, 120)
        self._size_explicitly_set = True
        self.resize(200, 200)
        self._apply_style()

    def _setup_timer(self):
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._update_days)
        self.timer.start(60 * 1000)
        self._update_days()

    def apply_config(self, config):
        _old = (getattr(self, "_event_name", ""), getattr(self, "_target_date", ""))
        self._read_config(config)
        self._apply_style()
        if _old != (self._event_name, self._target_date):
            logger.info(f"[DM] 倒数日配置变更: {_old} -> {(self._event_name, self._target_date)}")

    def _on_resize_debounce(self):
        super()._on_resize_debounce()
        self._update_days()

    def showEvent(self, event):
        super().showEvent(event)
        _first = not getattr(self, "_dm_shown_once", False)
        self._dm_shown_once = True
        if _first:
            logger.debug(f"[DM] 倒数日首次显示: '{self._event_name or '未命名'}' 目标={self._target_date or '未设置'}")
        self._apply_style()

    def mouseReleaseEvent(self, event):
        if self._handle_config_click_release(event, allow_config=not self._is_configured()):
            return
        super().mouseReleaseEvent(event)

    def _read_config(self, config):
        """取事件名/目标日/标题底色"""
        self._event_name = str(config.get("event_name", getattr(self, "_event_name", "")) or "")
        self._target_date = str(config.get("target_date", getattr(self, "_target_date", "")) or "")
        self._title_bg = str(config.get("title_bg_color", getattr(self, "_title_bg", "#F98E1B")) or "#F98E1B")
        logger.info(f"[DM] 倒数日读取配置: 事件='{self._event_name or '未命名'}' 目标日期={self._target_date or '未设置'}")

    def _is_configured(self):
        d = QDate.fromString(self._target_date, "yyyy-MM-dd")
        return d.isValid()

    def _num_font(self, px: int) -> QFont:
        f = QFont(FONT_FAMILY)
        f.setPixelSize(px)
        f.setBold(True)
        return f

    def _fit_font_px(self, text: str, base_px: int, max_width: int) -> int:
        px = base_px
        while px > 12:
            fm = QFontMetrics(self._num_font(px))
            if fm.horizontalAdvance(text) <= max_width:
                break
            px -= 2
        return px

    def _update_days(self):
        d = QDate.fromString(self._target_date, "yyyy-MM-dd")
        if not d.isValid():
            self.headerLabel.setText(tr("days_matter.default_title"))
            self.numLabel.setText("· · ·")
            self.footerLabel.setText(tr("countdown.click_to_config"))
            self.updateSize()
            return

        today = QDate.currentDate()
        diff = today.daysTo(d)  # >0 未来 / ==0 今天 / <0 过去
        _last_diff = getattr(self, '_last_days_diff', None)
        if _last_diff is not None and diff != _last_diff:
            logger.info(f"[DM] 倒数日跨日变化: {self._event_name or '未命名'} {_last_diff:+d} -> {diff:+d}天")
        self._last_days_diff = diff
        days = abs(diff)
        number_text = str(days)

        locale = QLocale.system()
        date_str = d.toString("yyyy-MM-dd")
        week_str = locale.toString(d, "dddd")
        if diff > 0:
            footer = f"{tr('days_matter.target_date')}: {date_str} {week_str}"
            status = tr("days_matter.still")
        elif diff == 0:
            footer = f"{tr('days_matter.target_date')}: {date_str} {week_str}"
            status = tr("days_matter.today")
        else:
            footer = f"{tr('days_matter.start_date')}: {date_str} {week_str}"
            status = tr("days_matter.past")

        name = self._event_name or tr("days_matter.default_title")
        self.headerLabel.setText(f"{name} {status}")
        self.footerLabel.setText(footer)

        available = max(40, self.width() - self._scaled_px(16))
        px = self._fit_font_px(number_text, self._scaled_px(72), available)
        self.numLabel.setFont(self._num_font(px))
        self.numLabel.setText(number_text)
        self.updateSize()

    def _apply_style(self):
        self._apply_card_style(opacity=100)
        is_dark = isDarkTheme()
        radius = self._corner_radius if self._corner_radius is not None else cfg.componentCardRadius.value

        c = QColor(self._title_bg)
        title_bg = c.name() if c.isValid() else "#F98E1B"
        self.headerWidget.setStyleSheet(
            f"#daysMatterHeader {{ background-color: {title_bg}; "
            f"border-top-left-radius: {radius}px; border-top-right-radius: {radius}px; }}")
        self.footerWidget.setStyleSheet(
            f"#daysMatterFooter {{ background-color: {'#2d2d2d' if is_dark else '#f5f5f5'}; "
            f"border-bottom-left-radius: {radius}px; border-bottom-right-radius: {radius}px; }}")

        self.headerLabel.setStyleSheet(
            f"color: #ffffff; font-size: {self._scaled_px(17)}px; font-weight: 600; "
            f"letter-spacing: 1px; "
            f"font-family: {FONT_FAMILY}; background: transparent;")
        num_color = "#f0f0f0" if is_dark else "#111111"
        self.numLabel.setStyleSheet(
            f"color: {num_color}; font-family: {FONT_FAMILY}; background: transparent;")
        footer_fs = max(8.0, self._scale_factor * 11.5)
        self.footerLabel.setStyleSheet(
            f"color: {'#9a9a9a' if is_dark else '#8e8e93'}; font-size: {footer_fs:.1f}px; "
            f"font-family: {FONT_FAMILY}; background: transparent;")

        self.headerWidget.setFixedHeight(self._scaled_px(44))
        self.footerWidget.setFixedHeight(self._scaled_px(34))
        self._update_days()

class SchoolInfoComponent(DraggableContainer):
    """班级信息卡片"""
    _accept_press = True

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("schoolInfoContainer")
        self._read_config(component_data.get("config", {}))
        logger.debug(f"[SCHOOL] 初始化 id={self.component_id} 班级={getattr(self, '_class', '') or '-'} 学校={getattr(self, '_school', '') or '-'}")
        self._setup_ui()

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
        self.classLabel = StrongBodyLabel("")
        self.classLabel.setObjectName("schoolClassLabel")
        self.classLabel.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self.countLabel = CaptionLabel("")
        self.countLabel.setObjectName("schoolCountLabel")
        self.countLabel.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        class_row.addWidget(self.classLabel, 1)
        class_row.addWidget(self.countLabel, 0)
        top_layout.addLayout(class_row)

        # 学校名
        self.nameLabel = BodyLabel("")
        self.nameLabel.setObjectName("schoolNameLabel")
        self.nameLabel.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        top_layout.addWidget(self.nameLabel)

        # 口号 组件背景为背景
        self.sloganLabel = BodyLabel("")
        self.sloganLabel.setObjectName("schoolSloganLabel")
        self.sloganLabel.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self.sloganLabel.setContentsMargins(20, 4, 20, 4)
        self.sloganLabel.setWordWrap(True)
        self.sloganLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        layout.addWidget(self.topWidget, 3)
        layout.addWidget(self.sloganLabel, 1)

        self.setMinimumSize(150, 100)
        self._size_explicitly_set = True
        self.resize(400, 200)
        self._apply_style()
        self._update_info()

    def apply_config(self, config):
        _old = (getattr(self, "_class", ""), getattr(self, "_school", ""), getattr(self, "_count", ""))
        self._read_config(config)
        logger.info(f"[SCHOOL] 配置应用 班级='{self._class or '-'}' 学校='{self._school or '-'}' 人数='{self._count or '-'}'")
        if _old != (self._class, self._school, self._count):
            logger.info(f"[SCHOOL] 班级信息配置变更: {_old} -> {(self._class, self._school, self._count)}")
        self._apply_style()
        self._update_info()

    def mouseReleaseEvent(self, event):
        if self._handle_config_click_release(event, allow_config=not self._is_configured()):
            return
        super().mouseReleaseEvent(event)

    def showEvent(self, event):
        super().showEvent(event)
        _first = not getattr(self, "_school_shown_once", False)
        self._school_shown_once = True
        if _first:
            logger.debug(f"[SCHOOL] 班级信息首次显示: 班级='{self._class or '-'}' 学校='{self._school or '-'}'")
        self._apply_style()
        self._update_info()

    def _read_config(self, config):
        # 哇哦
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
        logger.debug(f"[SCHOOL] 配置读取: 班级='{self._class}' 学校='{self._school}' 字号缩放={self._font_scale}%")

    def _scaled_px(self, base_px: int) -> int:
        """字体缩放换算"""
        font_scale = self._font_scale / 100.0
        return max(1, int(base_px * font_scale * self._scale_factor))

    def _is_configured(self):
        return bool(self._class or self._school or self._count or self._slogan)

    def _update_info(self):
        if not self._is_configured():
            self.classLabel.setText(tr("school_info_component.click_to_config"))
            self.classLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
            self.countLabel.setText("")
            self.nameLabel.setText("")
            self.sloganLabel.setText("")
            return

        self.classLabel.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        class_text = self._class
        school_text = self._school
        slogan_text = self._slogan


        self.classLabel.setText(class_text or "")
        self.nameLabel.setText(school_text or "")
        self.sloganLabel.setText(slogan_text or "")
        if self._count:
            self.countLabel.setText(f"{self._count}人")
        else:
            self.countLabel.setText("")
        logger.debug(f"[SCHOOL] 刷新 学校={school_text or '-'} 班级={class_text or '-'} 口号长度={len(slogan_text or '')}")

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

class MediaPlayerComponent(DraggableContainer):
    """媒体播放器组件"""
    #貌似用ai重写后core/ct和ses/media有的方法没用上
    _TEXT_QSS = (
        "color: {color};"
        "font-size: {size}px;"
        "font-weight: {weight};"
        "font-family: {family};"
        "background: transparent;"
    )

    _BTN_QSS = (
        "TransparentToolButton {{"
        "    background: transparent; border: none; padding: 0px; margin: 0px; color: {fg};"
        "}}"
        "TransparentToolButton:hover {{"
        "    background: transparent; border: none; color: {fg};"
        "}}"
        "TransparentToolButton:pressed {{"
        "    background: transparent; border: none; color: {fg};"
        "}}"
    )

    _media_ready = pyqtSignal(object, bool)  # 媒体信息是否刷新 ？
    _detail_ready = pyqtSignal(str, dict)    # 歌曲key 在线补全结果 {lyrics/cover/thumb/duration}
    _sync_done = pyqtSignal()                # 播放/暂停命令已发出

    def __init__(self, parent, component_data):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("mediaContainer")

        self._media: Optional[MediaInfo] = None
        self._lyrics: Optional[Lyrics] = None
        self._cover: Optional[QPixmap] = None
        self._last_ta = ""
        self._has_thumb = False
        self._fetching = False
        self._pending_full = False
        self._detail_fetching = False
        self._pending_key = ""
        self._duration = 0
        self._position = 0
        self._playing = False
        self._playing_sync_pending = False
        self._sync_retries = 0
        self._info_cache = OrderedDict()
        self._rapid_update_count = 0
        self._normal_interval = cfg.mediaUpdateInterval.value * 1000

        self._setup_ui()

        self._timer = QTimer(self)
        self._timer.timeout.connect(self._poll)
        self._prog_timer = QTimer(self)
        self._prog_timer.timeout.connect(self._update_progress)
        self._sync_confirm_timer = QTimer(self)
        self._sync_confirm_timer.setSingleShot(True)
        self._sync_confirm_timer.timeout.connect(self._sync_confirm_poll)

        self._style_timer = QTimer(self)
        self._style_timer.setSingleShot(True)
        self._style_timer.setInterval(150)
        self._style_timer.timeout.connect(self._apply_style)
        cfg.showMediaInfo.valueChanged.connect(self._on_visibility_changed)

        self._cover_opacity = QGraphicsOpacityEffect(self._cover_lbl)
        self._cover_opacity.setOpacity(1.0)
        self._cover_lbl.setGraphicsEffect(self._cover_opacity)
        self._cover_anim = QPropertyAnimation(self._cover_opacity, QByteArray(b"opacity"))
        self._cover_anim.setDuration(300)
        self._cover_anim.setEasingCurve(QEasingCurve.Type.OutCubic)
        self._apply_style()
        self._media_ready.connect(self._on_media)
        self._detail_ready.connect(self._on_detail)
        self._sync_done.connect(self._on_sync_done)
        self.start()
        logger.debug(f"[MEDIA] 媒体就绪 id={self.component_id} 间隔={self._normal_interval}ms")

    def _setup_ui(self):
        self.inner_layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)
        self.inner_layout.setContentsMargins(0, 0, 0, 0)
        self.inner_layout.setSpacing(0)

        content = QWidget(self)
        content.setObjectName("mediaWidget")
        content.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        content.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        self._content = content

        root = QHBoxLayout(content)
        root.setContentsMargins(16, 20, 12, 20)
        root.setSpacing(14)

        # 左侧封面
        self._cover_lbl = QLabel(content)
        self._cover_lbl.setObjectName("mediaCoverLabel")
        self._cover_lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._cover_lbl.setScaledContents(False)
        self._cover_lbl.setFixedSize(160, 160)
        root.addWidget(self._cover_lbl, 0, Qt.AlignmentFlag.AlignVCenter)

        # 右侧
        right_wrap = QWidget(content)
        right_col = QVBoxLayout(right_wrap)
        right_col.setContentsMargins(0, 0, 2, 0)
        right_col.setSpacing(0)

        top_block = QVBoxLayout()
        top_block.setContentsMargins(0, 0, 0, 0)
        top_block.setSpacing(2)

        self._title = QLabel(tr("media.not_playing"))
        self._title.setObjectName("mediaTitleLabel")
        self._title.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._title.setWordWrap(False)
        self._title.setFixedHeight(28)
        top_block.addWidget(self._title, 0)

        self._artist = QLabel("")
        self._artist.setObjectName("mediaArtistLabel")
        self._artist.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._artist.setWordWrap(False)
        self._artist.setFixedHeight(16)
        top_block.addWidget(self._artist, 0)

        right_col.addLayout(top_block, 0)
        right_col.addSpacing(8)

        # 歌词
        self._lyrics_lbl = QLabel("")
        self._lyrics_lbl.setObjectName("mediaLyricsLabel")
        self._lyrics_lbl.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._lyrics_lbl.setWordWrap(True)
        right_col.addWidget(self._lyrics_lbl, 1)

        # 进度 / 时间 / 按钮
        bottom_block = QVBoxLayout()
        bottom_block.setContentsMargins(0, 0, 0, 0)
        bottom_block.setSpacing(4)

        self._bar = ProgressBar(content)
        self._bar.setRange(0, 100)
        bottom_block.addWidget(self._bar, 0)

        self._time_lbl = QLabel("00:00 / 00:00")
        self._time_lbl.setObjectName("mediaTimeLabel")
        self._time_lbl.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        self._time_lbl.setMinimumHeight(14)
        bottom_block.addWidget(self._time_lbl, 0)

        btn_row = QHBoxLayout()
        btn_row.setContentsMargins(0, 2, 0, 0)
        btn_row.setSpacing(16)
        btn_row.addStretch(1)

        self._btn_prev = TransparentToolButton(content)
        self._btn_prev.setIconSize(QSize(20, 20))
        self._btn_prev.setFixedSize(28, 28)
        self._btn_prev.setCursor(QCursor(Qt.CursorShape.PointingHandCursor))
        self._btn_prev.clicked.connect(lambda: self._on_media_switch(-1))
        btn_row.addWidget(self._btn_prev, 0, Qt.AlignmentFlag.AlignVCenter)

        self._btn_play = TransparentToolButton(content)
        self._btn_play.setIconSize(QSize(24, 24))
        self._btn_play.setFixedSize(32, 32)
        self._btn_play.setCursor(QCursor(Qt.CursorShape.PointingHandCursor))
        self._btn_play.clicked.connect(self._on_play_pause)
        btn_row.addWidget(self._btn_play, 0, Qt.AlignmentFlag.AlignVCenter)

        self._btn_next = TransparentToolButton(content)
        self._btn_next.setIconSize(QSize(20, 20))
        self._btn_next.setFixedSize(28, 28)
        self._btn_next.setCursor(QCursor(Qt.CursorShape.PointingHandCursor))
        self._btn_next.clicked.connect(lambda: self._on_media_switch(1))
        btn_row.addWidget(self._btn_next, 0, Qt.AlignmentFlag.AlignVCenter)

        btn_row.addStretch(1)
        bottom_block.addLayout(btn_row, 0)

        right_col.addLayout(bottom_block, 0)
        root.addWidget(right_wrap, 1)

        self.inner_layout.addWidget(content, 1)

        self.setMinimumSize(200, 100)
        self._size_explicitly_set = True
        self.resize(400, 200)




    def apply_config(self, config: dict):
        super().apply_config(config)
        logger.debug(f"[MP] 配置 键={list(config.keys()) if isinstance(config, dict) else type(config).__name__}")
        self._apply_bg_style()

    def start(self):
        self._prog_timer.start(cfg.mediaUpdateInterval.value * 1000)
        self._prog_timer.start(1000)
        logger.info(f"[MP] 媒体组件启动 进度轮询间隔 {cfg.mediaUpdateInterval.value}s")
        self._spawn_media_fetch(full=True)

    def stop(self):
        logger.info("[MP] 媒体组件停止")
        self._timer.stop()
        self._prog_timer.stop()
        try:
            self._style_timer.stop()
            self._sync_confirm_timer.stop()
        except Exception as e:
            logger.debug(f"[MP] 辅助定时器停止失败: {e}")

    def closeEvent(self, event):
        logger.debug("[MP] 媒体组件关闭事件")
        self.stop()
        super().closeEvent(event)

    def _default_cover(self, sz: int = None):
        sz = self._scaled_px(160) if sz is None else sz
        logger.debug(f"[MP] 默认占位封面 尺寸={sz}px")
        radius = self._scaled_px(8)
        pm = QPixmap(sz, sz)
        pm.fill(Qt.GlobalColor.transparent)
        p = QPainter(pm)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(Qt.PenStyle.NoPen)

        if isDarkTheme():
            bg = QColor(40, 40, 48)
            icon_color = QColor(180, 180, 190, 160)
        else:
            bg = QColor(230, 230, 235)
            icon_color = QColor(120, 120, 130, 140)
        p.setBrush(bg)
        p.drawRoundedRect(0, 0, sz, sz, radius, radius)

        # 音符图标
        p.setBrush(icon_color)
        p.setPen(icon_color)
        cx, cy = sz / 2, sz / 2
        note_w = sz * 0.28
        note_h = sz * 0.38
        head_w = sz * 0.18
        head_h = sz * 0.13
        p.drawEllipse(QRectF(cx - note_w * 0.5, cy + note_h * 0.25, head_w, head_h))
        stem_w = max(sz * 0.025, 1.5)
        stem_h = note_h
        p.drawRect(QRectF(cx + note_w * 0.4 - stem_w, cy - note_h * 0.6, stem_w, stem_h))
        path = QPainterPath()
        path.moveTo(cx + note_w * 0.4, cy - note_h * 0.6)
        path.cubicTo(cx + note_w * 0.8, cy - note_h * 0.4,
                     cx + note_w * 0.6, cy - note_h * 0.1,
                     cx + note_w * 0.4, cy)
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.setPen(QPen(icon_color, max(sz * 0.025, 1.5)))
        p.drawPath(path)
        p.end()
        self._cover_lbl.setPixmap(pm)

    def _add_cover_shadow(self, pixmap: QPixmap, size: int) -> QPixmap:
        """封面裁圆投影

        Args:
            pixmap: 原始封面位图
        """
        radius = self._scaled_px(10)
        pad = self._scaled_px(8)
        result = QPixmap(size + pad, size + pad)
        result.fill(Qt.GlobalColor.transparent)

        p = QPainter(result)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)

        for i in range(4):
            p.setPen(Qt.PenStyle.NoPen)
            p.setBrush(QColor(0, 0, 0, 20 - i * 4))
            offset = (i + 1) * self._scaled_px(2)
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

        p.drawPixmap(pad // 2, pad // 2, rounded)
        p.end()
        return result

    def _load_cover(self, data: bytes):
        """淡入封面
        """
        pm = QPixmap()
        pm.loadFromData(data)
        if pm.isNull():
            logger.warning(f"[MP] 封面数据解码失败 ({len(data)}B)")
        if not pm.isNull():
            logger.debug(f"[MP] 封面已加载: {len(data)}B {pm.width()}x{pm.height()}")
            self._cover = pm
            sz = self._scaled_px(160)
            cover_with_shadow = self._add_cover_shadow(pm, sz)
            self._cover_lbl.setPixmap(cover_with_shadow)

            self._cover_anim.stop()
            self._cover_opacity.setOpacity(0.0)
            self._cover_anim.setStartValue(0.0)
            self._cover_anim.setEndValue(1.0)
            self._cover_anim.start()

    @staticmethod
    def _qss_color(color_val):
        if isinstance(color_val, QColor):
            c = color_val
        elif color_val == "primary":
            c = QColor(0, 0, 0) if not isDarkTheme() else QColor(255, 255, 255)
        else:
            if hasattr(color_val, 'name'):
                color_val = color_val.name()
            c = QColor(color_val)
        return f"rgba({c.red()}, {c.green()}, {c.blue()}, {round(c.alpha() / 255, 2)})"

    def _on_visibility_changed(self):
        _prev = self.isVisible()
        _new = cfg.showMediaInfo.value
        self.setVisible(_new)
        if _prev != _new:
            logger.debug(f"[MP] 媒体信息可见性切换: {_prev} -> {_new}")

    def __del__(self):
        try:
            self.stop()
        except Exception:
            pass

    def _poll(self):
        """轮询媒体信息"""
        self._spawn_media_fetch(full=True)

    def _spawn_media_fetch(self, full=True):
        """起线程拉媒体信息
        Args:
            full: True/False
        """
        if self._fetching:
            if full:
                self._pending_full = True
            return
        self._fetching = True
        self._pending_full = False
        logger.debug(f"[MP] 发起媒体信息拉取线程: full={full}")
        threading.Thread(target=self._media_worker, args=(full,), daemon=True).start()

    def _media_worker(self, full):
        """子线程读媒体信息

        Args:
            full: True/False
        """
        m = None
        try:
            m = get_media_info()
            if not m or not m.is_valid():
                m = None
        except Exception as e:
            logger.error(f"媒体信息获取异常: {e}")
        self._media_ready.emit(m, full)

    def _on_media(self, m, full):
        """收到媒体信息

        Args:
            m: 媒体信息(MediaInfo)/one
            full: True/False
        """
        self._fetching = False
        if not cfg.showMediaInfo.value:
            return
        if not m or not m.is_valid():
            self._no_media()
            if self._pending_full:
                QTimer.singleShot(100, lambda: self._spawn_media_fetch(full=True))
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
                QTimer.singleShot(100, lambda: self._spawn_media_fetch(full=True))
        else:
            if m.position_ms > 0:
                self._position = m.position_ms
            if not self._playing_sync_pending:
                self._playing = m.is_playing
            if m.duration_ms > 0:
                self._duration = m.duration_ms
            self._update_play_icon()
            if getattr(m, 'thumbnail_data', None) and not self._has_thumb:
                self._has_thumb = True
                self._load_cover(m.thumbnail_data)
            if self._duration > 0:
                self._bar.setValue(min(100, int(self._position / self._duration * 100)))
                self._time_lbl.setText(f"{self._fmt(self._position)} / {self._fmt(self._duration)}")
            else:
                self._time_lbl.setText(f"{self._fmt(self._position)} / --:--")
            if self._playing and self._lyrics and not self._lyrics.is_empty():
                self._update_lyrics(self._position)
            self._check_play_sync(m.is_playing)
            if self._pending_full:
                self._pending_full = False
                QTimer.singleShot(100, lambda: self._spawn_media_fetch(full=True))

    def _no_media(self):
        if getattr(self, "_media", None) is not None:
            logger.info("[MP] 无媒体播放 重置为空状态")
        self._title.setText(tr("media.not_playing"))
        self._title.setWordWrap(False)
        self._artist.setText("")
        self._artist.show()
        self._lyrics_lbl.setText("")
        self._lyrics_lbl.show()
        self._bar.setValue(0)
        self._time_lbl.setText("00:00 / 00:00")
        self._default_cover()
        self._media = None
        self._cover = None
        self._lyrics = None
        self._last_ta = ""
        self._has_thumb = False
        self._pending_key = ""
        self._playing = False
        self._duration = 0
        self._playing_sync_pending = False
        self._sync_retries = 0
        self._sync_confirm_timer.stop()
        self._update_play_icon()
        self._prog_timer.stop()
        if cfg.showMediaInfo.value:
            self.show()

    def _display(self, m: MediaInfo):
        title = m.title or tr("media.unknown_song")  # 未知歌曲
        artist = m.artist or ""

        if m.title_artist != self._last_ta:
            self._last_ta = m.title_artist
            logger.info(f"[MP] 切歌: {title} - {artist or '未知歌手'} 时长 {self._fmt(m.duration_ms)}")
            self._position = m.position_ms
            self._playing = m.is_playing
            if m.duration_ms > 0:
                self._duration = m.duration_ms
            self._has_thumb = False

            app_name = getattr(m, 'app_name', '') or ''
            is_web_browser = any(browser in app_name.lower() for browser in ['chrome', 'edge', 'firefox', 'msedge'])

            self._cover_anim.stop()
            self._default_cover()
            self._cover = None
            self._lyrics = None
            self._lyrics_lbl.setText("")
            self._cover_lbl.repaint()
            self._lyrics_lbl.repaint()

            if is_web_browser and not artist:
                self._title.setText(title)
                self._title.setWordWrap(True)
                self._artist.hide()
                self._lyrics_lbl.hide()
            else:
                self._title.setText(title)
                self._title.setWordWrap(False)
                self._artist.setText(artist)
                self._artist.show()
                self._lyrics_lbl.show()

            if getattr(m, 'thumbnail_data', None):
                self._has_thumb = True
                self._load_cover(m.thumbnail_data)
            elif is_web_browser:
                self._has_thumb = True

            if not is_web_browser:
                self._fetch(m)

        seek_jump = False
        if m.position_ms > 0:
            seek_jump = abs(m.position_ms - self._position) > self._normal_interval + 1500
            self._position = m.position_ms
        if not seek_jump and not self._playing_sync_pending:
            self._playing = m.is_playing
        if m.duration_ms > 0:
            self._duration = m.duration_ms

        self._update_play_icon()
        if self._duration > 0:
            self._bar.setValue(min(100, int(self._position / self._duration * 100)))
            self._time_lbl.setText(f"{self._fmt(self._position)} / {self._fmt(self._duration)}")
        else:
            self._bar.setValue(0)
            self._time_lbl.setText(f"{self._fmt(self._position)} / --:--")

        if self._playing and self._lyrics and not self._lyrics.is_empty():
            self._update_lyrics(self._position)

        self._check_play_sync(m.is_playing)
        self._cover_lbl.setVisible(cfg.showMediaCover.value)

    def _update_progress(self):
        if self._playing and self._duration > 0:
            self._position = min(self._position + self._prog_timer.interval(), self._duration)
            pct = min(100, int(self._position / self._duration * 100))
            self._bar.setValue(pct)
            if pct // 10 != getattr(self, '_last_prog_decile', -1):
                self._last_prog_decile = pct // 10
                logger.debug(f"[MP] 进度 {pct}% ({self._fmt(self._position)} / {self._fmt(self._duration)})")
            self._time_lbl.setText(f"{self._fmt(self._position)} / {self._fmt(self._duration)}")
            if self._lyrics and not self._lyrics.is_empty():
                self._update_lyrics(self._position)

    @staticmethod
    def _fmt(ms: int) -> str:
        """毫秒转时间

        Returns:
            m:ss 文本
        """
        s = max(0, ms // 1000)
        return f"{s // 60}:{s % 60:02d}"

    def _update_lyrics(self, ms: int):
        if not self._lyrics or self._lyrics.is_empty():
            return
        advance = cfg.mediaLyricsAdvance.value
        _, idx = self._lyrics.get_line_at_time(ms + advance)
        text = self._lyrics.lines[idx].text if 0 <= idx < len(self._lyrics.lines) else ""
        if getattr(self, "_last_lyric_idx", None) != idx:
            self._last_lyric_idx = idx
            logger.debug(f"[MP] 歌词行切换 [{idx}] {text[:20]}")
        self._lyrics_lbl.setText(text)

    def _fetch(self, m: MediaInfo):
        cache_key = m.title_artist
        if cache_key in self._info_cache:
            info = self._info_cache.pop(cache_key)
            self._info_cache[cache_key] = info  # LRU 刷新
            logger.debug(f"[MP] 歌曲详情缓存命中: {cache_key} (字段={list(info.keys()) or '无'})")
            self._apply_detail(cache_key, info)
            return
        self._pending_key = cache_key
        if self._detail_fetching:
            return
        self._detail_fetching = True
        logger.debug(f"[MP] 发起歌曲详情抓取: {cache_key}")
        threading.Thread(target=self._fetch_detail, args=(m,), daemon=True).start()

    def _fetch_detail(self, m: MediaInfo):
        """子线程抓详情"""
        result = {}
        try:
            svc = get_service(m.app_name)
            if svc:
                logger.debug(f"[MP] 详情服务命中: {m.app_name} -> {type(svc).__name__}")
                result['lyrics'] = svc.lyrics(m)
                cover = svc.cover(m)
                if cover:
                    result['cover'] = cover
                dur = svc.duration(m)
                if dur:
                    result['duration'] = dur
            if m.app_name == 'Kugou':
                gsmtc = get_service("GSMTC")
                if gsmtc:
                    gi = gsmtc.read()
                    if gi and gi.thumbnail_data:
                        result['thumb'] = gi.thumbnail_data
                        logger.debug("[MP] 用 GSMTC 缩略图补封面")
        except Exception as e:
            logger.debug(f"获取歌曲信息失败: {e}")
        self._detail_ready.emit(m.title_artist, result)

    def _on_detail(self, key: str, result: dict):
        """收到详情

        Args:
            key: 歌曲键(title_artist),与当前歌匹配用
            result: 详情结果 dict(歌词/封面/时长)
        """
        self._detail_fetching = False
        self._info_cache[key] = result
        logger.debug(f"[MP] 媒体详情就绪 key={key} 字段={list(result.keys()) or '无'}")
        if len(self._info_cache) > 50:
            self._info_cache.popitem(last=False)
        m = self._media
        if m and m.title_artist == key:
            self._apply_detail(key, result)

        if (m and m.title_artist != key and m.title_artist not in self._info_cache
                and self._pending_key == m.title_artist):
            self._fetch(m)

    def _on_play_pause(self):
        """播放/暂停按钮处理"""
        target = not self._playing
        self._playing = target
        logger.info(f"[MP] 用户{'播放' if target else '暂停'}指令")
        self._playing_sync_pending = True
        self._sync_retries = 0
        self._update_play_icon()
        action = "pause" if not target else "play"
        threading.Thread(target=self._control_worker, args=(action,), daemon=True).start()

    def _control_worker(self, action: str):
        """子线程执行控制命令
        Args:
            action: play/pause/next/prev
        """
        try:
            media_control(action)
        except Exception as e:
            logger.warning(f"[MP] 控制命令 '{action}' 执行失败: {e}")
        self._sync_done.emit()

    def _on_sync_done(self):
        logger.debug(f"[MP] 命令已执行 确认中 目标={'播放' if self._playing else '暂停'}")
        self._sync_confirm_timer.start(60)

    def _sync_confirm_poll(self):
        logger.debug(f"[MP] 同步确认轮询 (重试 {getattr(self, '_sync_retries', 0)} 目标={'播放' if self._playing else '暂停'})")
        self._spawn_media_fetch(full=True)

    def _check_play_sync(self, is_playing: bool):
        if not self._playing_sync_pending:
            return
        if is_playing == self._playing:
            self._playing_sync_pending = False
            self._sync_retries = 0
            logger.debug(f"[MP] 播放状态同步确认: {'播放中' if is_playing else '已暂停'}")
            return
        self._sync_retries += 1
        if self._sync_retries >= 6:
            self._playing_sync_pending = False
            self._sync_retries = 0
            logger.warning(f"[MP] 播放状态同步失败 目标={'播放' if self._playing else '暂停'} 实际={'播放' if is_playing else '暂停'}")
            return
        self._sync_confirm_timer.start(300)

    def _on_media_switch(self, delta: int):
        """切上下曲

        Args:
            delta: 1 下一曲,-1 上一曲
        """
        self._cover_anim.stop()
        self._cover_opacity.setOpacity(0.0)
        cmd = media_next if delta > 0 else media_prev
        logger.info(f"[MP] 切{'下一首' if delta > 0 else '上一首'}")
        threading.Thread(target=cmd, daemon=True).start()
        QTimer.singleShot(800, lambda: self._spawn_media_fetch(full=True))

    def _update_play_icon(self, icon_theme=None):
        if icon_theme is None:
            icon_theme = FTheme.DARK if isDarkTheme() else FTheme.LIGHT
        icon = FluentIcon.PAUSE_BOLD.icon(icon_theme) if self._playing else FluentIcon.PLAY.icon(icon_theme)
        self._btn_play.setIcon(icon)
        _prev = getattr(self, "_last_icon_playing", None)
        if _prev is not None and _prev != self._playing:
            logger.debug(f"[MP] 播放图标切换 -> {'播放中' if self._playing else '已暂停'}")
        self._last_icon_playing = self._playing

    def apply_scale(self, factor):
        self._scale_factor = factor
        self._apply_style()

    def _apply_style(self):
        dark = isDarkTheme()
        if dark:
            title_c = QColor(245, 245, 250, 255)      # F5F5FA
            artist_c = QColor(230, 230, 235, 200)
            time_c = QColor(220, 220, 225, 150)
            lyrics_c = QColor(235, 235, 240, 170)
            btn_fg = QColor(255, 255, 255, 235)       # 深色模式白图标
        else:
            title_c = QColor(31, 31, 31, 255)         # 1F1F1F
            artist_c = QColor(60, 60, 67, 214)
            time_c = QColor(60, 60, 67, 138)
            lyrics_c = QColor(44, 44, 46, 153)
            btn_fg = QColor(0, 0, 0, 220)             # 浅色模式黑图标

        def _text_qss(color, size, weight):
            return self._TEXT_QSS.format(
                color=self._qss_color(color), size=size, weight=weight, family=FONT_FAMILY)

        self._title.setStyleSheet(_text_qss(title_c, self._scaled_px(19), 700))
        self._artist.setStyleSheet(_text_qss(artist_c, self._scaled_px(11), 500))
        self._time_lbl.setStyleSheet(_text_qss(time_c, self._scaled_px(10), 500))
        self._lyrics_lbl.setStyleSheet(_text_qss(lyrics_c, self._scaled_px(12), 700))

        # 图标
        icon_theme = FTheme.DARK if dark else FTheme.LIGHT
        self._btn_prev.setIcon(FluentIcon.LEFT_ARROW.icon(icon_theme))
        self._btn_next.setIcon(FluentIcon.RIGHT_ARROW.icon(icon_theme))
        self._update_play_icon(icon_theme)

        # 按钮
        btn_qss = self._BTN_QSS.format(fg=self._qss_color(btn_fg))
        for btn in (self._btn_prev, self._btn_play, self._btn_next):
            btn.setStyleSheet(btn_qss)
        icon_prev = self._scaled_px(20)
        self._btn_prev.setIconSize(QSize(icon_prev, icon_prev))
        self._btn_prev.setFixedSize(self._scaled_px(28), self._scaled_px(28))
        icon_play = self._scaled_px(24)
        self._btn_play.setIconSize(QSize(icon_play, icon_play))
        self._btn_play.setFixedSize(self._scaled_px(32), self._scaled_px(32))
        self._btn_next.setIconSize(QSize(icon_prev, icon_prev))
        self._btn_next.setFixedSize(self._scaled_px(28), self._scaled_px(28))

        # 行高
        self._title.setFixedHeight(self._scaled_px(28))
        self._artist.setFixedHeight(self._scaled_px(16))
        self._time_lbl.setMinimumHeight(self._scaled_px(14))

        # 封面尺寸
        sz = self._scaled_px(160)
        self._cover_lbl.setFixedSize(sz, sz)
        if self._cover and not self._cover.isNull():
            cover_with_shadow = self._add_cover_shadow(self._cover, sz)
            self._cover_lbl.setPixmap(cover_with_shadow)
        else:
            self._default_cover(sz)

        # 进度条
        self._bar.setFixedHeight(self._scaled_px(3))
        self._apply_bg_style()

    def _apply_bg_style(self):
        if cfg.mediaUseCustomBg.value:
            bg_opacity = cfg.mediaBgOpacity.value
            border_radius = cfg.mediaBorderRadius.value

            if isDarkTheme():
                c = QColor(30, 30, 30)
            else:
                c = QColor(255, 255, 255)
            c.setAlpha(int(255 * bg_opacity / 100))

            self._apply_card_style(
                target=self._content, bg_mode="custom", bg_color=c,
                radius=border_radius, border="none")
        else:
            if isDarkTheme():
                border_color = "rgba(255, 255, 255, 0.06)"
            else:
                border_color = "rgba(0, 0, 0, 0.06)"
            self._apply_card_style(
                target=self._content,
                opacity=self._bg_opacity, radius=self._corner_radius,
                border=f"1px solid {border_color}")

    def _apply_detail(self, key: str, result: dict):
        logger.debug(f"[MP] 应用歌曲详情: {key} 字段={list(result.keys()) or '无'}")
        if not self._media or self._media.title_artist != key:
            return
        if result.get('duration'):
            self._duration = result['duration']
        if cfg.showMediaCover.value and not self._has_thumb:
            thumb = result.get('thumb') or result.get('cover')
            if thumb:
                self._has_thumb = True
                self._load_cover(thumb)
        lyrics = result.get('lyrics')
        if lyrics:
            self._lyrics = lyrics
            self._update_lyrics(self._position)

class QuickLaunchDock(QWidget):
    """快捷启动栏基类"""
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
    FPS = 60
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
        logger.debug(f"[QLD] 快速启动坞初始化 图标基准 {cfg.quickLaunchIconSize.value}px 标签显示={self._show_labels}")

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
                    dpr = self.devicePixelRatioF()
                    raw.setDevicePixelRatio(dpr)
                    pm = raw
            if pm is None:
                logger.warning(f"[QLD] 图标缺失: {a.get('name', '?')} (icon={fn})")
            self._pixmaps.append(pm)
        n = len(apps)
        self._scales = [self.BASE_SCALE] * n
        self._target_scales = [self.BASE_SCALE] * n
        self._fix_size()

        if 0 <= animate_idx < n:
            self._start_bounce(animate_idx)

        logger.debug(f"[QLD] 启动坞渲染: {n}个应用 图标 {self._sz()}px 动画下标 {animate_idx}")
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
        if (w, h) != (self.width(), self.height()):
            logger.debug(f"[QLD] 尺寸调整: {self.width()}x{self.height()} -> {w}x{h}")
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

    def mouseMoveEvent(self, e):
        if self._dragging_idx >= 0 and self._drag_start_pos and not self._is_internal_drag:
            dist = (e.position() - self._drag_start_pos).manhattanLength()
            if dist > 10:
                self._is_internal_drag = True
                self._drag_pos = self._drag_start_pos
                self._drop_target_idx = self._dragging_idx
                self.update()

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
                    logger.debug(f"[QLD] 按下图标 [{i}]: {self._apps[i].get('name', '?')}")
                    self._drag_start_pos = e.position()
                    self._dragging_idx = i
                    break
        elif e.button() == Qt.MouseButton.RightButton:
            pl = self._icon_positions()
            for i in range(len(self._apps)):
                if self._icon_rect(i, pl).contains(e.position()):
                    self._show_context_menu(i)
                    break
        super().mousePressEvent(e)

    def mouseReleaseEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton:
            if self._is_internal_drag and self._dragging_idx >= 0:
                self._finish_drag_reorder()
            elif self._dragging_idx >= 0 and self._drag_start_pos:
                pl = self._icon_positions()
                if self._icon_rect(self._dragging_idx, pl).contains(e.position()):
                    logger.debug(f"[QLD] 启动 [{self._dragging_idx}] {self._apps[self._dragging_idx].get('name', '?') if 0 <= self._dragging_idx < len(self._apps) else '?'}")
                    self._click(self._dragging_idx)

            self._dragging_idx = -1
            self._drag_start_pos = None
            self._is_internal_drag = False
            self._drop_target_idx = -1
            self._drag_pos = None
            self.update()

        super().mouseReleaseEvent(e)

    def leaveEvent(self, e):
        n = len(self._target_scales)
        self._target_scales = [self.BASE_SCALE] * n
        if self._hover_idx >= 0:
            logger.debug(f"[QLD] 离开 [{self._hover_idx}] {self._apps[self._hover_idx].get('name', '?') if 0 <= self._hover_idx < len(self._apps) else '?'}")
        self._hover_idx = -1
        super().leaveEvent(e)

    def dragEnterEvent(self, e):
        if _accepts_drop(e.mimeData()):
            logger.debug("[QLD] 拖拽进入 接受投放")
            e.acceptProposedAction()
        else:
            e.ignore()

    def dragMoveEvent(self, e):
        if e.mimeData().hasUrls() or e.mimeData().hasText():
            e.acceptProposedAction()
        else:
            e.ignore()

    def dropEvent(self, e):
        e.acceptProposedAction()
        for item in _iter_drop_items(e.mimeData()):
            logger.debug(f"[QLD] 拖入启动项: {item.get('name', '?')} (type={item.get('type', 'app')})")
            if item["type"] == "folder":
                self._add_quick_item(item, "quick_launch.added_folder")
            elif item["type"] == "url":
                self._add_quick_item(item, "quick_launch.added_url")
            else:
                self._add_quick_item(item, "quick_launch.added", "quick_launch.max_apps")

    def paintEvent(self, event):
        if self._painting:
            return
        self._painting = True
        try:
            self._render()
        finally:
            self._painting = False

    def minimumSizeHint(self):
        if not self._apps:
            return QSize(40, 40)
        bg = self._bg_rect()
        return QSize(int(bg.width()), int(bg.height()))

    def hideEvent(self, e):
        logger.debug("[QLD] 快捷启动条隐藏 停止动画定时器")
        self._timer.stop()
        super().hideEvent(e)
    def _update_drop_target(self, pos):
        if not self._apps:
            return

        sz = self._sz()
        gap = self._gap()
        px = self._pad_x()
        bg = self._bg_rect()
        content_w = bg.width() - px * 2
        n = len(self._apps)
        widths = [sz * sc for sc in self._scales]
        total_w = sum(widths) + (n - 1) * gap
        start_x = bg.x() + px + (content_w - total_w) / 2

        new_target = -1
        cx = start_x
        for i in range(n):
            if pos.x() < cx + widths[i] / 2:
                new_target = i
                break
            cx += widths[i] + gap

        if new_target == -1:
            new_target = n

        if new_target != self._drop_target_idx:
            logger.debug(f"[QLD] 拖拽落点变更: {self._drop_target_idx} -> {new_target}")
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

    def _show_context_menu(self, idx):
        if idx < 0 or idx >= len(self._apps):
            return

        app = self._apps[idx]
        logger.debug(f"[QLD] 打开启动项菜单 [{idx}]: {app.get('name', '?')}")
        menu = _build_item_menu(self, app.get("name", tr("quick_launch.app")),
                                lambda: self._click(idx), lambda: self._edit_app(idx),
                                tr("quick_launch.edit"), lambda: self._delete_app(idx))

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
        rc = dialog.exec()
        dialog.deleteLater()
        if rc:
            result = dialog.get_app_data()
            if result:
                old_name = self._apps[idx].get("name", "")
                logger.info(f"[QLD] 启动项编辑 [{idx}]: '{old_name}' -> '{result.get('name', '')}'")
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

        box = MessageBox(tr("quick_launch.confirm_delete"), tr("quick_launch.confirm_delete_msg", name=app_name), mask)  # 确认删除 / 确定要删除"{name}"吗？
        box.yesButton.setText(tr("quick_launch.delete"))  # 删除
        box.cancelButton.setText(tr("common.cancel"))  # 取消

        if box.exec():
            logger.info(f"[QLD] 启动项删除 [{idx}]: {app_name}")
            self._apps.pop(idx)
            cfg.quickLaunchApps.value = self._apps
            save_cfg()
            self.set_apps(self._apps)
            InfoBar.success(tr("quick_launch.delete_success"), tr("quick_launch.deleted", name=app_name), parent=self.window(), duration=2000)  # 删除成功 / 已删除"{name}"

        mask.close()
        mask.deleteLater()

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
                    t = max(0.0, min(1.0, 1.0 - d / self.MAGNIFY_RANGE))
                    t = t * t * (3.0 - 2.0 * t)
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

    def _add_quick_item(self, new_item, content_key, limit_key="quick_launch.max_items"):
        """Args:
            new_item: dict
            content_key: 成功提示
            limit_key: 上限提示
        """
        apps = list(cfg.quickLaunchApps.value)
        if len(apps) >= self.MAX_APPS:
            InfoBar.warning(
                title=tr("quick_launch.limit_reached"),  # 已达上限
                content=tr(limit_key, max=self.MAX_APPS),
                parent=self.window(),
                duration=3000
            )
            return
        apps.append(new_item)
        cfg.quickLaunchApps.value = apps
        save_cfg()
        self.set_apps(apps, animate_idx=len(apps) - 1)
        logger.info(f"[QLD] 添加 {new_item.get('name', '?')} (type={new_item.get('type', 'app')}) 当前 {len(apps)}/{self.MAX_APPS}")
        InfoBar.success(tr("quick_launch.add_success"), tr(content_key, name=new_item['name']), parent=self.window(), duration=2000)  # 添加成功

    def _click(self, idx):
        a = self._apps[idx]
        path = a.get("path", "")
        name = a.get("name", "")
        app_type = a.get("type", "app")
        logger.debug(f"[QLD] 点击启动项 [{idx}] {name} (type={app_type})")
        self._start_bounce(idx)
        if path:
            self._executor.submit(self._launch_thread, path, name, app_type)

    def _launch_thread(self, target, name, app_type):
        """子线程启动
        Args:
            target: 启动目标
            app_type: 条目类型
        """
        logger.debug(f"[QLD] 子线程启动: {name} ({app_type})")
        ok, info = _launch_target(target, app_type)
        self._launch_result.emit(name, info, ok)

    def _on_launch_result(self, app_name, info, success):
        """启动结果提示

        Args:
            info: 失败原因/成功目标
        """
        _show_launch_result(self, app_name, info, success)
        if success:
            logger.debug(f"[QLD] 已启动 {app_name} -> {info}")
        else:
            logger.warning(f"[QLD] 启动失败: {app_name} 原因: {info}")

    def _start_bounce(self, idx):
        """弹跳动画"""
        if idx < 0 or idx >= len(self._apps):
            return
        self._bounce_idx = idx
        self._bounce_y = 0.0
        self._bounce_active = True
        self._bounce_start_time = time.time()
        logger.debug(f"[QLD] 图标弹跳: {self._apps[idx].get('name', '?')}")
        self._ensure_timer()

    def _get_by(self):
        return self._bounce_y

    def _set_by(self, v):
        self._bounce_y = v
        self.update()

    bounceY = pyqtProperty(float, _get_by, _set_by)

    def _render(self):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)

        bg = self._bg_rect()
        if bg.isEmpty():
            p.end()
            return

        dark = isDarkTheme()
        radius = max(0, int(cfg.componentCardRadius.value * self._scale_factor))
        op = max(0.0, min(1.0, cfg.componentCardOpacity.value / 100.0))

        path = QPainterPath()
        path.addRoundedRect(bg, radius, radius)

        if dark:
            bg_c = QColor(30, 30, 32, int(255 * op))
            brd_c = QColor(255, 255, 255, 20)
            sh_top = QColor(255, 255, 255, 22)
            sh_mid = QColor(255, 255, 255, 6)
            inner_glow = QColor(255, 255, 255, 8)
        else:
            bg_c = QColor(235, 235, 240, int(255 * op))
            brd_c = QColor(0, 0, 0, 12)
            sh_top = QColor(255, 255, 255, 95)
            sh_mid = QColor(255, 255, 255, 18)
            inner_glow = QColor(255, 255, 255, 25)

        p.setPen(Qt.PenStyle.NoPen)

        shadow_path = QPainterPath()
        sr = QRectF(bg.x() + 1.5, bg.y() + 2, bg.width() - 3, bg.height() * 0.5)
        shadow_path.addRoundedRect(sr, max(0, radius - 3), max(0, radius - 3))
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
        pen.setWidth(max(1, int(1 * self._scale_factor)))
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
                p.setPen(QPen(QColor(120, 120, 120, 100), max(1, int(1 * self._scale_factor))))
                r = QRectF(cx - s / 2, top, s, s)
                p.drawRoundedRect(r, 8 * self._scale_factor, 8 * self._scale_factor)
                p.setPen(QPen(QColor(180, 180, 180, 150), max(1, int(2 * self._scale_factor))))
                font = p.font()
                font.setPixelSize(int(s * 0.4))
                p.setFont(font)
                p.drawText(r, Qt.AlignmentFlag.AlignCenter, "?")

            if i == self._hover_idx and self._show_labels and not self._is_internal_drag:
                name = self._apps[i].get("name", "")
                if name:
                    label_font = p.font()
                    label_font.setFamily(FONT_PRIMARY)
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


class QuickLaunchDockComponent(DraggableContainer):
    """快捷启动栏组件"""
    _accept_press = True

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("quickLaunchContainer")
        self._dock = None
        self._placeholder = None
        self._setup_ui()
        cfg.showQuickLaunch.valueChanged.connect(self._update_apps)
        cfg.quickLaunchApps.valueChanged.connect(self._update_apps)
        cfg.quickLaunchIconSize.valueChanged.connect(self._update_apps)
        cfg.quickLaunchIconSpacing.valueChanged.connect(self._update_apps)
        cfg.quickLaunchShowLabels.valueChanged.connect(self._update_apps)
        logger.debug(f"[QLC] 快速启动组件初始化 id={self.component_id}")

    def _setup_ui(self):
        logger.debug("[QLC] 快捷启动条组件 ui 初始化")
        layout = self.inner_layout
        layout.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.setContentsMargins(20, 16, 20, 16)

        self._dock = QuickLaunchDock(self)
        self._dock.setObjectName("quickLaunchDock")
        layout.addWidget(self._dock)

        self._placeholder = CaptionLabel(tr("quick_launch_component.click_to_config"))
        self._placeholder.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._placeholder.setWordWrap(True)
        self._placeholder.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        layout.addWidget(self._placeholder)

        self.setMinimumSize(120, 80)
        self._size_explicitly_set = True
        self.resize(400, 200)

        self._apply_style()
        self._update_apps()

    def mouseReleaseEvent(self, event):
        if self._handle_config_click_release(event, allow_config=not bool(cfg.quickLaunchApps.value)):
            return
        super().mouseReleaseEvent(event)

    def _update_apps(self):
        if not cfg.showQuickLaunch.value:
            self.hide()
            return
        self.show()

        apps = cfg.quickLaunchApps.value
        logger.debug(f"[QLC] 应用列表更新: {len(apps) if apps else 0}个应用 (显示={cfg.showQuickLaunch.value})")
        if apps:
            self._dock.set_apps(apps)
            self._dock.show()
            self._placeholder.hide()
        else:
            self._dock.set_apps([])
            self._dock.hide()
            self._placeholder.show()
        self.updateSize()

    def apply_scale(self, factor):
        if self._dock:
            self._dock.set_scale_factor(factor)
            self._dock._fix_size()
            self._dock.update()
        self._apply_style()

    def _apply_style(self):
        self._apply_card_style()
        if self._placeholder:
            is_dark = isDarkTheme()
            color = "#cccccc" if is_dark else "#666666"
            size = cfg.quickLaunchIconSize.value
            self._placeholder.setStyleSheet(f"""
                color: {color};
                font-size: {self._scaled_px(max(12, int(size * 0.22)))}px;
                font-family: {FONT_FAMILY};
                background-color: transparent;
            """)
        if self._dock:
            self._dock.update()
        self.updateSize()

class QuickLaunchGridComponent(DraggableContainer):
    """快捷启动八宫格"""

    CELL_COUNT = 8
    COLUMN_COUNT = 4
    _launch_result = pyqtSignal(str, str, bool)

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("quickLaunchGridContainer")
        self._config = dict(component_data.get("config", {}))
        self._apps = self._normalize_apps(self._config.get("apps"))
        self._executor = ThreadPoolExecutor(max_workers=1)
        self._launch_result.connect(self._on_launch_result)
        self._pixmaps = [None] * self.CELL_COUNT  # 各格子图标缓存
        self._press_idx = -1   # 左键按下的格子
        self._hover_idx = -1   # 悬停的格子
        self._drag_idx = -1    # 拖放悬停的格子
        self._plus_pm = None
        self._plus_key = ""
        self._setup_ui()
        self._apply_style()
        logger.debug(f"[QLG] 宫格启动组件初始化 id={self.component_id} 应用数={len(self._apps)} 格数={self.CELL_COUNT}")

    def _setup_ui(self):
        self.setAcceptDrops(True)
        self.setMinimumSize(180, 100)
        self._size_explicitly_set = True
        self.resize(400, 200)
        self._reload_pixmaps()

    def apply_config(self, config: dict):
        self._config = dict(config or {})
        apps = self._normalize_apps(self._config.get("apps"))
        if apps != self._apps:
            self._apps = apps
            self._reload_pixmaps()
            logger.info(f"[QLG] 宫格应用配置更新: {sum(1 for a in apps if a)}/{self.CELL_COUNT} 格已配置")
        super().apply_config(config)

    def paintEvent(self, event):
        super().paintEvent(event)  # 卡片背景 选中框
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)

        dark = isDarkTheme()
        for i, rect in enumerate(self._cell_rects()):
            side = rect.width()
            if side < 4:
                continue
            r = self._corner_radius if self._corner_radius is not None else cfg.componentCardRadius.value
            radius = max(0, min(int(r * self._scale_factor), int(side / 2)))

            if i == self._drag_idx or i == self._hover_idx:
                c = self._primary_color()
                fb = QColor(c.red(), c.green(), c.blue())
                fb.setAlpha(38)
                painter.setPen(Qt.PenStyle.NoPen)
                painter.setBrush(fb)
                painter.drawRoundedRect(rect, radius, radius)

            if i == self._drag_idx:
                c = self._primary_color()
                pen = QPen(QColor(c.red(), c.green(), c.blue(), 220))
                pen.setWidthF(2.0)
                pen.setStyle(Qt.PenStyle.DashLine)
            elif i == self._hover_idx:
                c = self._primary_color()
                pen = QPen(QColor(c.red(), c.green(), c.blue(), 180))
                pen.setWidthF(1.8)
            else:
                pen = QPen(QColor(255, 255, 255, 20) if dark else QColor(0, 0, 0, 12))
                pen.setWidthF(1.0)
            painter.setPen(pen)
            painter.setBrush(Qt.BrushStyle.NoBrush)
            painter.drawRoundedRect(rect, radius, radius)

            # 内容
            cx, cy = rect.center().x(), rect.center().y()
            app = self._apps[i]
            if app:
                icon_side = side * 0.52
                pm = self._pixmaps[i]
                if pm and not pm.isNull():
                    painter.drawPixmap(
                        QRectF(cx - icon_side / 2, cy - icon_side / 2, icon_side, icon_side),
                        pm,
                        QRectF(0, 0, pm.width(), pm.height()),
                    )
                else:
                    painter.setPen(QPen(QColor(160, 160, 160, 200)))
                    font = QFont(FONT_PRIMARY)
                    font.setPixelSize(max(10, int(side * 0.3)))
                    painter.setFont(font)
                    painter.drawText(rect, Qt.AlignmentFlag.AlignCenter, "?")
            else:
                plus = self._plus_pixmap(max(8, int(side * 0.34)))
                if plus and not plus.isNull():
                    dpr = plus.devicePixelRatio() or 1.0
                    w, h = plus.width() / dpr, plus.height() / dpr
                    painter.setOpacity(0.5)
                    painter.drawPixmap(QPointF(cx - w / 2, cy - h / 2), plus)
                    painter.setOpacity(1.0)

    def mousePressEvent(self, event):
        if not self._draggable:
            idx = self._hit_cell(event.position())
            if event.button() == Qt.MouseButton.RightButton and idx >= 0:
                self._show_cell_menu(idx, event.globalPosition().toPoint())
                event.accept()
                return
            if event.button() == Qt.MouseButton.LeftButton and idx >= 0:
                self._press_idx = idx
                logger.debug(f"[QLG] 按下宫格 [{idx}]")
                event.accept()
                return
        super().mousePressEvent(event)

    def mouseReleaseEvent(self, event):
        if not self._draggable and event.button() == Qt.MouseButton.LeftButton:
            if self._press_idx >= 0 and self._hit_cell(event.position()) == self._press_idx:
                app = self._apps[self._press_idx] if 0 <= self._press_idx < self.CELL_COUNT else None
                if app:
                    self._launch_app(app)
                else:
                    self._browse_add(self._press_idx)
            self._press_idx = -1
            event.accept()
            return
        self._press_idx = -1
        super().mouseReleaseEvent(event)

    def mouseMoveEvent(self, event):
        if not self._draggable:
            idx = self._hit_cell(event.position())
            if idx != self._hover_idx:
                self._hover_idx = idx
                if idx >= 0:
                    app = self._apps[idx]
                    self.setToolTip(app.get("name", "") if app else "")
                else:
                    self.setToolTip("")
                self.update()
            self.setCursor(QCursor(
                Qt.CursorShape.PointingHandCursor if idx >= 0 else Qt.CursorShape.ArrowCursor))
        super().mouseMoveEvent(event)

    def leaveEvent(self, event):
        if self._hover_idx != -1:
            self._hover_idx = -1
            if not self._draggable:
                self.setCursor(QCursor(Qt.CursorShape.ArrowCursor))
            self.update()
        super().leaveEvent(event)

    def dragEnterEvent(self, e):
        if _accepts_drop(e.mimeData()):
            logger.debug("[QLG] 拖拽进入宫格 接受投放")
            e.acceptProposedAction()
        else:
            e.ignore()

    def dragMoveEvent(self, e):
        if _accepts_drop(e.mimeData()):
            idx = self._hit_cell(e.position())
            if idx != self._drag_idx:
                logger.debug(f"[QLG] 拖拽高亮格切换: [{self._drag_idx}] -> [{idx}]")
                self._drag_idx = idx
                self.update()
            e.acceptProposedAction()
        else:
            e.ignore()

    def dragLeaveEvent(self, e):
        if self._drag_idx != -1:
            logger.debug(f"[QLG] 拖拽离开 清除高亮格 [{self._drag_idx}]")
            self._drag_idx = -1
            self.update()
        super().dragLeaveEvent(e)

    def dropEvent(self, e):
        idx = self._hit_cell(e.position())
        self._drag_idx = -1
        self.update()
        if idx >= 0:
            e.acceptProposedAction()
            added = next(_iter_drop_items(e.mimeData()), None)
            if added:
                logger.info(f"[QLG] 拖放入宫格[{idx}]: {added.get('name', '?')}")
                self._set_cell_app(idx, added)
        else:
            e.ignore()

    def _normalize_apps(self, apps):
        result = []
        for i in range(self.CELL_COUNT):
            item = apps[i] if isinstance(apps, list) and i < len(apps) else None
            result.append(item if isinstance(item, dict) else None)
        logger.debug(f"[QLG] 规范化启动项: {sum(1 for r in result if r)}/{self.CELL_COUNT} 格有效")
        return result

    def _reload_pixmaps(self):
        logger.debug(f"[QLG] 重载宫格图标 共 {self.CELL_COUNT} 格")
        for i in range(self.CELL_COUNT):
            self._load_pixmap(i)
        self.update()

    def _load_pixmap(self, idx):
        app = self._apps[idx] if 0 <= idx < self.CELL_COUNT else None
        pm = None
        if app:
            icon_path = get_ql_icon_path(app.get("icon", ""))
            if icon_path and os.path.exists(icon_path):
                raw = QPixmap(icon_path)
                if not raw.isNull():
                    raw.setDevicePixelRatio(self.devicePixelRatioF())
                    pm = raw
            if pm is None:
                logger.warning(f"[QLG] 图标缺失: 格子[{idx}] {app.get('name', '?')} (icon={app.get('icon', '')})")
        self._pixmaps[idx] = pm

    def _cell_rects(self) -> list:
        m = max(6, int(12 * self._scale_factor))
        gap = max(4, int(10 * self._scale_factor))
        cols = self.COLUMN_COUNT
        rows = self.CELL_COUNT // self.COLUMN_COUNT
        avail_w = self.width() - m * 2 - gap * (cols - 1)
        avail_h = self.height() - m * 2 - gap * (rows - 1)
        side = max(4, min(avail_w // cols, avail_h // rows))
        total_w = side * cols + gap * (cols - 1)
        total_h = side * rows + gap * (rows - 1)
        x0 = (self.width() - total_w) / 2.0
        y0 = (self.height() - total_h) / 2.0
        rects = []
        for i in range(self.CELL_COUNT):
            r, c = divmod(i, cols)
            rects.append(QRectF(x0 + c * (side + gap), y0 + r * (side + gap), side, side))
        return rects

    def _hit_cell(self, pos) -> int:
        for i, rect in enumerate(self._cell_rects()):
            if rect.contains(pos):
                return i
        return -1


    def _primary_color(self) -> QColor:
        tc = cfg.themeColor.value
        c = QColor(tc) if isinstance(tc, str) else QColor(tc)
        return c if c.isValid() else QColor(48, 195, 97)

    def _plus_pixmap(self, size_px: int) -> QPixmap:
        key = f"{size_px}|{isDarkTheme()}"
        if self._plus_key == key and self._plus_pm is not None:
            return self._plus_pm
        path = FUI.ADD.path()
        pm = render_svg_icon(path, size_px, self.devicePixelRatioF()) \
            if path and os.path.exists(path) else QPixmap()
        self._plus_pm = pm
        self._plus_key = key
        return pm

    def _browse_add(self, idx):
        """浏览选择 解析条目"""
        path, _ = QFileDialog.getOpenFileName(
            self.window(), tr("quick_launch_ii.select_program"), "",
            "Programs (*.exe *.lnk);;All Files (*)")
        if path:
            logger.debug(f"[QLG] 宫格[{idx}] 文件选择: {path}")
            self._set_cell_app(idx, resolve_app_from_path(path))
        else:
            logger.debug(f"[QLG] 宫格[{idx}] 文件选择已取消")

    def _set_cell_app(self, idx, app):
        """写入/替换 重载图标

        Args:
            app: 应用条目 dict(name/path/icon/type)
        """
        if not (0 <= idx < self.CELL_COUNT) or not app:
            return
        self._apps[idx] = app
        self._load_pixmap(idx)
        logger.info(f"[QLG] 宫格[{idx}] 设置应用: {app.get('name', '?')} (type={app.get('type', 'app')})")
        self.update()
        self._persist_config()
        InfoBar.success(tr("quick_launch.add_success"),
                        tr("quick_launch.added", name=app.get("name", "")),
                        parent=self.window(), duration=2000)

    def _show_cell_menu(self, idx, global_pos):
        """右键:打开/改名/删除

        Args:
            global_pos:弹出坐标
        """
        app = self._apps[idx] if 0 <= idx < self.CELL_COUNT else None
        logger.debug(f"[QLG] 宫格[{idx}] 右键菜单: {'应用' if app else '空格子'}")
        if app:
            menu = _build_item_menu(self, app.get("name", tr("quick_launch.app")),
                                    lambda: self._launch_app(app), lambda: self._rename_cell(idx),
                                    tr("quick_launch_ii.rename"), lambda: self._delete_cell(idx))
        else:
            menu = RoundMenu(tr("quick_launch_ii.add_app"), self)
            add_action = Action(FUI.ADD, tr("quick_launch_ii.add_app"), self)
            add_action.triggered.connect(lambda: self._browse_add(idx))
            menu.addAction(add_action)
        menu.exec(global_pos)

    def _rename_cell(self, idx):
        """行内弹窗改名"""
        app = self._apps[idx] if 0 <= idx < self.CELL_COUNT else None
        if not app:
            return
        box = MessageBoxBase(self.window())
        title = SubtitleLabel(tr("quick_launch_ii.rename_title"), box)
        box.viewLayout.addWidget(title)
        edit = LineEdit(box)
        edit.setText(app.get("name", ""))
        edit.setClearButtonEnabled(True)
        box.viewLayout.addWidget(edit)
        rc = box.exec()
        box.deleteLater()
        if rc:
            new_name = edit.text().strip()
            if new_name:
                logger.info(f"[QLG] 宫格[{idx}] 重命名: '{app.get('name', '')}' -> '{new_name}'")
                app["name"] = new_name
                self.setToolTip(new_name if self._hover_idx == idx else "")
                self._persist_config()
                InfoBar.success(tr("quick_launch.save_success"),
                                tr("quick_launch.shortcut_updated"),
                                parent=self.window(), duration=2000)

    def _delete_cell(self, idx):
        app = self._apps[idx] if 0 <= idx < self.CELL_COUNT else None
        if not app:
            return
        app_name = app.get("name", tr("quick_launch.this_app"))
        box = MessageBox(tr("quick_launch.confirm_delete"),
                         tr("quick_launch.confirm_delete_msg", name=app_name),
                         self.window())
        box.yesButton.setText(tr("quick_launch.delete"))
        box.cancelButton.setText(tr("common.cancel"))
        if box.exec():
            logger.info(f"[QLG] 宫格[{idx}] 删除应用: {app_name}")
            self._apps[idx] = None
            self._pixmaps[idx] = None
            self.setToolTip("")
            self.update()
            self._persist_config()
            InfoBar.success(tr("quick_launch.delete_success"),
                            tr("quick_launch.deleted", name=app_name),
                            parent=self.window(), duration=2000)

    def _launch_app(self, app):
        path = app.get("path", "")
        name = app.get("name", "")
        app_type = app.get("type", "app")
        logger.debug(f"[QLG] 启动宫格应用: {name} (type={app_type})")
        if path:
            self._executor.submit(self._launch_thread, path, name, app_type)

    def _launch_thread(self, target, name, app_type):
        logger.debug(f"[QLG] 子线程启动: {name} ({app_type})")
        ok, info = _launch_target(target, app_type)
        self._launch_result.emit(name, info, ok)

    def _on_launch_result(self, app_name, info, success):
        _show_launch_result(self, app_name, info, success)
        if success:
            logger.debug(f"[QLG] 已启动 {app_name} -> {info}")
        else:
            logger.warning(f"[QLG] 启动失败: {app_name} 原因: {info}")

    def _persist_config(self):
        self._config["apps"] = self._apps
        home = self._getHomeInterface()
        if home and hasattr(home, "component_manager"):
            home.component_manager.update_component_config(self.component_id, self._config)
            logger.debug(f"[QLG] 宫格配置已持久化: {sum(1 for a in self._apps if a)}个应用")

    def _apply_style(self):
        self._apply_card_style()

class _TimetableRow(QWidget):
    """今日课表单行"""

    def __init__(self, is_current=False, is_break=False, is_past=False, parent=None):
        super().__init__(parent)
        self._is_current = is_current
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
        self._progress = ProgressBar(self)
        self._progress.setObjectName("timetableProgress")
        self._progress.setFixedHeight(3)
        self._progress.setTextVisible(False)
        self._progress.setRange(0, 100)
        self._progress.setValue(0)
        if not is_current:
            self._progress.hide()
        self._main_layout.addWidget(self._progress)

    def addWidget(self, w, stretch=0):
        """Args:
            w: 课程名/教室/老师
        """
        self._content_layout.addWidget(w, stretch)

    def setProgress(self, pct):
        """Args:
            pct: 进度 0~1
        """
        if self._is_current:
            self._progress.show()
            self._progress.setValue(int(max(0, min(100, pct))))

    def set_current(self, active: bool):
        """Args:
            active: True 强调,False 灰化
        """
        logger.debug(f"[课表] 行高亮切换: {self._is_current} -> {active}")
        self._is_current = active
        if active:
            self.setObjectName("timetableRowCurrent")
            self._progress.show()
        else:
            if self._is_past:
                self.setObjectName("timetableRowPast")
            else:
                self.setObjectName("timetableRow")
            self._progress.hide()
        self.style().unpolish(self)
        self.style().polish(self)

    def apply_scale(self, factor: float):
        self._progress.setFixedHeight(max(1, int(3 * factor)))

class TimetablePreviewComponent(DraggableContainer):
    """今日课表预览"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("timetableContainer")
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        self._bridge = None
        self._timetable_page = None
        self._schedule_rows = []
        self._current_row_data = None
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

        self._sig = None                          # 当前行数据签名
        self._fast_retry_count = 0
        self._fast_timer = QTimer(self)           # 重试
        self._fast_timer.timeout.connect(self._fast_retry)

        self._setup_ui()
        self._connect_timetable_page()
        self._refresh_schedule()
        self._fast_timer.start(500)
        logger.debug(f"[TTP] 课表预览就绪 id={self.component_id} 信号={'连' if self._timetable_page is not None else '无'}")

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setContentsMargins(10, 10, 10, 10)
        layout.setSpacing(6)

        # 标题
        self._title_label = SubtitleLabel(tr("timetable.today_schedule"))
        self._title_label.setObjectName("timetableTitle")
        layout.addWidget(self._title_label)

        # 滚动区域
        self._scroll = ScrollArea(self)
        self._scroll.setObjectName("timetableScroll")
        self._scroll.setWidgetResizable(True)
        self._scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self._scroll.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self._scroll.setFrameShape(ScrollArea.Shape.NoFrame)

        self._scroll_content = QWidget()
        self._scroll_content.setObjectName("timetableScrollContent")
        self._scroll_layout = QVBoxLayout(self._scroll_content)
        self._scroll_layout.setContentsMargins(0, 0, 0, 0)
        self._scroll_layout.setSpacing(4)
        self._scroll_layout.addStretch()

        self._scroll.setWidget(self._scroll_content)
        layout.addWidget(self._scroll, 1)

        self.setMinimumSize(160, 200)
        self._size_explicitly_set = True
        self.resize(300, 550)
        self._apply_style()

        self._refresh_timer = QTimer(self)
        self._refresh_timer.timeout.connect(self._on_timer)

        self._progress_timer = QTimer(self)
        self._progress_timer.timeout.connect(self._update_progress)

        self._scroll.verticalScrollBar().sliderReleased.connect(self._on_user_scroll_action)

        self._scroll.installEventFilter(self)

    def _build_class_row(self, index, subject, teacher, start, end, is_current, is_past=False):
        """建课程行

        Args:
            index: 节次序号(1 起)
            teacher: 任课老师 可选
            start: 开始时间 HH:MM
            end: 结束时间 HH:MM
        """
        row = _TimetableRow(is_current=is_current, is_break=False, is_past=is_past, parent=self)
        past_suffix = "Past" if is_past else ""

        # 第几节
        idx_lbl = CaptionLabel(f"第{index}节")
        idx_lbl.setObjectName("timetableIdx" + past_suffix)
        idx_lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        idx_lbl.setFixedWidth(self._scaled_px(15) * 4 + 8)
        row.addWidget(idx_lbl)
        row._idx_lbl = idx_lbl

        # 课程名
        subj_lbl = BodyLabel(subject or "—")
        subj_lbl.setObjectName("timetableSubj" + past_suffix)
        row.addWidget(subj_lbl, 1)

        # 时间
        time_lbl = CaptionLabel(f"{start}~{end}")
        time_lbl.setObjectName("timetableTime" + past_suffix)
        time_lbl.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        row.addWidget(time_lbl)

        return row

    def _build_break_row(self, start, end, break_name, is_current=True):
        """建课间行

        Args:
            start: 开始时间 HH:MM
            end: 结束时间 HH:MM
            break_name: 课间名
        """
        row = _TimetableRow(is_current=is_current, is_break=True, parent=self)

        pad = QWidget()
        pad.setFixedWidth(self._scaled_px(15) * 4 + 8)
        row.addWidget(pad)
        row._idx_lbl = pad

        lbl = BodyLabel(break_name or "课间")
        lbl.setObjectName("timetableBreakLabel")
        lbl.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        row.addWidget(lbl, 1)

        time_lbl = CaptionLabel(f"{start}~{end}")
        time_lbl.setObjectName("timetableTime")
        time_lbl.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        row.addWidget(time_lbl)

        return row

    def eventFilter(self, obj, event):
        """手动滚动进预览模式

        Args:
            obj: 被监听对象
            event: 事件对象
        """
        if obj == self._scroll and event.type() == QEvent.Type.Wheel:
            self._last_user_scroll_time = time.time()
        return super().eventFilter(obj, event)

    def showEvent(self, e):
        """连数据源并重建"""
        super().showEvent(e)
        _first = not getattr(self, "_ttp_shown_once", False)
        self._ttp_shown_once = True
        if _first:
            logger.debug(f"[TTP] 课表预览首次显示 id={self.component_id}")
        self._apply_style()
        self._refresh_timer.start(5000)
        self._progress_timer.start(1000)
        if not (self._timetable_page and self._sig) and not self._fast_timer.isActive():
            self._fast_retry_count = 0
            self._fast_timer.start(500)

    def hideEvent(self, e):
        """停信号 定时器"""
        logger.debug("[TTP] 课表预览隐藏 停止刷新/进度定时器")
        self._refresh_timer.stop()
        self._progress_timer.stop()
        self._fast_timer.stop()
        super().hideEvent(e)

    def _on_user_scroll_action(self):
        """手动滚动延时"""
        logger.debug("[TTP] 用户手动滚动")
        self._last_user_scroll_time = time.time()

    def _exit_preview(self):
        """结束手动预览"""
        logger.debug("[TTP] 退出预览模式 恢复今日课表")
        self._scroll_animation_timer.stop()
        self._preview_timer.stop()
        self._after_school_mode = False
        self._sig = None  # 下次刷新重建为今日
        self._title_label.setText(tr("timetable.today_schedule"))
        self._scroll.verticalScrollBar().setValue(0)

    def _animate_scroll(self):
        """滚动到目标行"""
        logger.debug(f"[TTP] 启动滚动动画: 方向={self._scroll_direction} 步长={self._scroll_step}")
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

    def _row_flags(self, row_data):
        """计算行状态位

        Args:
            row_data: subject, teacher, start, end, index, is_current, is_break, break_name

        Returns:
            (skip, is_past)
        """
        subject, teacher, start, end, index, is_current, is_break, break_name = row_data
        if is_break and not is_current:
            return True, False
        if self._after_school_mode:
            return False, False
        try:
            eh, em = map(int, end.split(":"))
            is_past = (not is_current and not is_break and _time(eh, em) <= _dt.now().time())
        except Exception as e:
            if not getattr(self, "_endtime_warned", False):
                self._endtime_warned = True
                logger.warning(f"[TTP] 节次结束时间格式无效({end}) 按未结束处理: {e}")
            is_past = False
        return False, is_past

    def _fast_retry(self):
        """数据未好重试"""
        self._fast_retry_count += 1
        logger.debug(f"[TTP] 快速重试 #{self._fast_retry_count}")
        if not self._timetable_page:
            self._connect_timetable_page()
        self._refresh_schedule()
        if (self._timetable_page and self._sig and len(self._sig) > 0) or self._fast_retry_count > 60:
            self._fast_timer.stop()

    def _rebuild_schedule(self, schedule):
        """按课表重建行

        Args:
            schedule: 课表页最新课表数据 dict
        """
        while self._scroll_layout.count() > 1:
            item = self._scroll_layout.takeAt(0)
            if item and item.widget():
                item.widget().deleteLater()
        self._schedule_rows.clear()
        self._current_row_data = None

        if not schedule:
            row = _TimetableRow(is_current=False, is_break=False, parent=self)
            lbl = BodyLabel("今天没有课程")
            lbl.setObjectName("timetableEmpty")
            lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
            row.addWidget(lbl, 1)
            self._scroll_layout.insertWidget(self._scroll_layout.count() - 1, row)
            self._schedule_rows.append(row)
            logger.debug("[TTP] 空课表 仅空提示行")
            return

        last_current_row = None
        last_current_times = None

        for row_data in schedule:
            subject, teacher, start, end, index, is_current, is_break, break_name = row_data
            skip, is_past = self._row_flags(row_data)
            if skip:
                continue

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

        logger.debug(f"[TTP] 课表预览已重建 {len(self._schedule_rows)}行 当前节 {last_current_times or '-'}")
        self._update_progress()

    def _connect_timetable_page(self):
        self._timetable_page = _find_timetable_page()
        if self._timetable_page is None:
            logger.debug("[Timetable] 未找到课表页")
            return
        self._timetable_page.scheduleChanged.connect(self._refresh_schedule)
        self._bind_bridge()
        logger.info("[Timetable] 已连 timetablePage")

    def _bind_bridge(self):
        """绑定课表页当前数据源桥"""
        # 切换数据源后需重新绑定
        bridge = self._timetable_page.bridge if self._timetable_page else None
        if bridge is self._bridge:
            return
        if self._bridge:
            try:
                self._bridge.stateChanged.disconnect(self._on_state_changed)
                self._bridge.connectedChanged.disconnect(self._on_connected_changed)
            except Exception as e:
                logger.debug(f"[TTP] 旧数据源桥断连时无活动连接: {e}")
        self._bridge = bridge
        if bridge:
            bridge.stateChanged.connect(self._on_state_changed)
            bridge.connectedChanged.connect(self._on_connected_changed)
            logger.debug(f"[TTP] 数据源桥已绑定: {type(bridge).__name__ if bridge else '-'}")

    def _on_state_changed(self, state):
        logger.debug(f"[TTP] 课表状态变化: {state}")
        self._refresh_schedule()

    def _on_connected_changed(self, connected):
        """
        Args:
            connected: 课表页是否启用
        """
        logger.debug(f"[TTP] 课表页连接状态: {connected}")
        if connected:
            self._refresh_schedule()

    def _on_timer(self):
        # 刷新行状态
        if not self._timetable_page:
            self._connect_timetable_page()
        self._bind_bridge()

        # 检查是否放学
        if self._bridge:
            try:
                from core.linkage import TimeState
                state = self._bridge.get_state()
                if state and state.time_state == TimeState.AFTER_SCHOOL:
                    if not self._after_school_mode:
                        self._enter_preview_mode()
                    return
            except Exception as e:
                if not getattr(self, "_linkage_warned", False):
                    self._linkage_warned = True
                    logger.warning(f"[TTP] 读取课表联动状态失败: {e}")

        # 正常刷新今日课表
        self._refresh_schedule()

        # 自动滚动
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

        if 0 <= scroll_idx < len(self._schedule_rows) and time.time() - self._last_user_scroll_time > 15:
            row = self._schedule_rows[scroll_idx]
            QTimer.singleShot(50, lambda: self._do_scroll(row, 0.3))

        self._update_progress()

    def _enter_preview_mode(self):
        """进入手动预览"""
        logger.debug("[TTP] 进入放学/预览模式 切换为明日课表")
        self._after_school_mode = True
        self._title_label.setText("明日课表")

        tomorrow = datetime.date.today() + datetime.timedelta(days=1)
        dotnet_wd = tomorrow.isoweekday() % 7

        schedule = []
        if self._timetable_page:
            try:
                schedule = self._timetable_page.get_schedule_by_weekday(tomorrow.weekday())
            except Exception as e:
                logger.warning(f"[TTP] 获取明日课表失败: {e}")
        elif self._bridge:
            schedule = self._bridge.get_schedule_by_weekday(dotnet_wd)

        self._rebuild_schedule(schedule)
        self._scroll.verticalScrollBar().setValue(0)

        self._scroll_animation_timer.start(50)
        self._preview_timer.start(30000)

    def _refresh_schedule(self):
        if self._after_school_mode:
            return

        if self._bridge:
            try:
                from core.linkage import TimeState
                state = self._bridge.get_state()
                if state and state.time_state == TimeState.AFTER_SCHOOL:
                    return
            except Exception as e:
                if not getattr(self, "_linkage_warned", False):
                    self._linkage_warned = True
                    logger.warning(f"[TTP] 读取课表联动状态失败: {e}")

        schedule = []
        if self._timetable_page:
            try:
                schedule = self._timetable_page.get_today_schedule()
            except Exception as e:
                if not getattr(self, "_sched_warned", False):
                    self._sched_warned = True
                    logger.warning(f"[TTP] 获取今日课表失败: {e}")

        parts = []
        for row_data in schedule:
            skip, is_past = self._row_flags(row_data)
            if not skip:
                parts.append((tuple(row_data), is_past))
        sig = tuple(parts)

        if sig == self._sig:
            self._update_progress()
            return
        self._sig = sig
        logger.info(f"[TTP] 课表内容变化 重建显示: {len(schedule)}行")
        self._rebuild_schedule(schedule)

    def _update_progress(self):
        if not self._current_row_data or not self._schedule_rows:
            return
        start_s, end_s = self._current_row_data
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

    def _do_scroll(self, row, ratio):
        """滚动一步

        Args:
            ratio: 0~1
        """
        try:
            if not row or not hasattr(row, 'mapTo'):
                return
            row_y = row.mapTo(self._scroll_content, QPoint(0, 0)).y()
            sb = self._scroll.verticalScrollBar()
            viewport_h = self._scroll.viewport().height()
            target_y = row_y - int(viewport_h * ratio)
            target_y = max(sb.minimum(), min(target_y, sb.maximum()))
            sb.setValue(target_y)
        except RuntimeError:
            return

    def apply_scale(self, factor):
        idx_w = self._scaled_px(15) * 4 + 8
        for row in self._schedule_rows:
            row.apply_scale(factor)
            if getattr(row, '_idx_lbl', None):
                row._idx_lbl.setFixedWidth(idx_w)
        self._apply_style()

    def _apply_style(self, *args):
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
        bg_css = self._card_bg_css()
        self.setStyleSheet(f"""
            {bg_css}

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

class TimetableNowLessonComponent(DraggableContainer):
    """当前课程卡片"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("nowLessonContainer")
        self.setMinimumSize(120, 80)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)

        self._timetable_page = None

        self._read_config(component_data.get("config", {}))

        self._size_explicitly_set = True
        self._setup_ui()
        self._connect_timetable_page()

        self._timer = QTimer(self)
        self._timer.timeout.connect(self._refresh)
        self._refresh()
        logger.debug(f"[TTN] 当前课程就绪 id={self.component_id} 信号={'连' if self._timetable_page is not None else '无'}")

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setContentsMargins(16, 10, 16, 8)
        layout.setSpacing(0)

        # 左右栏
        main = QWidget()
        main_layout = QHBoxLayout(main)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(14)

        # 左：课程名 倒计时/进度
        left = QWidget()
        self._left = left
        left.setMinimumWidth(self._scaled_px(100))
        left.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)
        left_layout = QVBoxLayout(left)
        left_layout.setContentsMargins(0, 0, 0, 0)
        left_layout.setSpacing(4)

        self._subject_label = BodyLabel("--")
        self._subject_label.setObjectName("miniSubject")
        self._subject_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._subject_label.setWordWrap(True)
        self._subject_label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        left_layout.addWidget(self._subject_label, 3)

        self._countdown_label = CaptionLabel("")
        self._countdown_label.setObjectName("miniCountdown")
        self._countdown_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        left_layout.addWidget(self._countdown_label, 1)

        self._time_progress_label = CaptionLabel("-- min / -- min")
        self._time_progress_label.setObjectName("miniTimeLabel")
        self._time_progress_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        left_layout.addWidget(self._time_progress_label, 1)

        main_layout.addWidget(left, 1)

        # 右：老师 时间段 下节课
        right = QWidget()
        right.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)
        right_layout = QVBoxLayout(right)
        right_layout.setContentsMargins(0, 0, 0, 0)
        right_layout.setSpacing(8)

        self._teacher_label = CaptionLabel("--")
        self._teacher_label.setObjectName("miniTeacher")
        self._teacher_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._teacher_label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        right_layout.addWidget(self._teacher_label, 1)

        self._time_label = CaptionLabel("--:-- ~ --:--")
        self._time_label.setObjectName("miniTime")
        self._time_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._time_label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        right_layout.addWidget(self._time_label, 1)

        self._next_label = CaptionLabel("下节课 --")
        self._next_label.setObjectName("miniNext")
        self._next_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        self._next_label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        right_layout.addWidget(self._next_label, 1)

        right_layout.addStretch()
        main_layout.addWidget(right, 1)

        layout.addWidget(main, 1)

        # 进度条
        self._bottom_progress = ProgressBar(self)
        self._bottom_progress.setFixedHeight(self._scaled_px(6))
        self._bottom_progress.setRange(0, 100)
        self._bottom_progress.setValue(0)
        self._bottom_progress.setTextVisible(False)
        layout.addWidget(self._bottom_progress)

        self._apply_style()

    def apply_config(self, config):
        logger.debug("[TTN] 当前课程组件配置应用")
        self._read_config(config)
        self._apply_style()
        self._refresh()

    def showEvent(self, e):
        super().showEvent(e)
        _first = not getattr(self, "_ttn_shown_once", False)
        self._ttn_shown_once = True
        if _first:
            logger.debug("[TTN] 当前课程组件首次显示")
        self._apply_style()
        self._timer.start(1000)

    def hideEvent(self, e):
        logger.debug("[TTN] 当前课程组件隐藏 停止秒级定时器")
        self._timer.stop()
        super().hideEvent(e)

    def _read_config(self, config):
        # 哦哦
        _old = (getattr(self, "_show_teacher", None), getattr(self, "_show_next", None),
                getattr(self, "_show_countdown", None), getattr(self, "_prepare_minutes", None))
        self._show_teacher = config.get("show_teacher", getattr(self, "_show_teacher", True))
        self._show_next = config.get("show_next", getattr(self, "_show_next", True))
        self._show_duration = config.get("show_duration", getattr(self, "_show_duration", True))
        self._show_countdown = config.get("show_countdown", getattr(self, "_show_countdown", True))
        self._prepare_minutes = config.get("prepare_minutes", getattr(self, "_prepare_minutes", 3))
        self._bg_opacity = config.get("bg_opacity", getattr(self, "_bg_opacity", None))
        self._corner_radius = config.get("corner_radius", getattr(self, "_corner_radius", None))
        self._font_scale = config.get("font_scale", getattr(self, "_font_scale", 100))
        logger.debug(f"[TTN] 配置 教师={self._show_teacher} 下节={self._show_next} 时长={self._show_duration} 倒计时={self._show_countdown} 课前={self._prepare_minutes}min")
        if _old != (self._show_teacher, self._show_next, self._show_countdown, self._prepare_minutes):
            logger.info(f"[TTN] 配置变更 {_old} -> {(self._show_teacher, self._show_next, self._show_countdown, self._prepare_minutes)}")

    def _connect_timetable_page(self):
        self._timetable_page = _find_timetable_page()
        if self._timetable_page is None:
            logger.warning("[TTN] 课表页未找到 信号未连接")
            return
        self._timetable_page.scheduleChanged.connect(self._refresh)

    def _refresh(self):
        if not self._timetable_page:
            self._connect_timetable_page()
            if not self._timetable_page:
                return
        try:
            schedule = self._timetable_page.get_today_schedule()
        except Exception as e:
            if not getattr(self, "_ttn_sched_warned", False):
                self._ttn_sched_warned = True
                logger.warning(f"[TTN] 获取今日课表失败: {e}")
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

        if current_row and getattr(self, "_last_current_key", None) != (current_row[0], current_row[2]):
            self._last_current_key = (current_row[0], current_row[2])
            logger.info(f"[TimetableNowLesson] 当前课程: {current_row[0]} {current_row[2]}~{current_row[3]}")

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
            except Exception as e:
                if not getattr(self, "_ttn_time_warned", False):
                    self._ttn_time_warned = True
                    logger.warning(f"[TTN] 课前播报时间解析失败({ps}): {e}")

        if in_prepare and not getattr(self, "_was_in_prepare", False):
            logger.info(f"[TimetableNowLesson] 进入课前播报: {prepare_row[0]} {prepare_row[2]} 开课")
        self._was_in_prepare = in_prepare

        if in_prepare:
            if self._timer.interval() != 1000:
                self._timer.setInterval(1000)
        else:
            if self._timer.interval() != 5000:
                self._timer.setInterval(5000)

        if in_prepare and prepare_row:
            self._render_prepare_mode(prepare_row, now)
        elif current_row:
            self._render_normal_mode(current_row, next_row, now)
        else:
            self._subject_label.setText("--")
            self._countdown_label.setText("")
            self._teacher_label.setText("--")
            self._time_label.setText("--:-- ~ --:--")
            self._next_label.setText("下节课 --")
            self._time_progress_label.setText("-- min / -- min")
            self._bottom_progress.setValue(0)

    def _render_prepare_mode(self, prepare_row, now):
        """课间态
        Args:
            prepare_row: 下一节课的dict
        """
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
        except Exception as e:
            if not getattr(self, "_ttn_time_warned", False):
                self._ttn_time_warned = True
                logger.warning(f"[TTN] 课前倒计时解析失败({start}): {e}")
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
                self._next_label.setText(f"时长 {total_min}分钟")
            except Exception as e:
                if not getattr(self, "_ttn_time_warned", False):
                    self._ttn_time_warned = True
                    logger.warning(f"[TTN] 课间时长解析失败({start}~{end}): {e}")
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
        except Exception as e:
            if not getattr(self, "_ttn_time_warned", False):
                self._ttn_time_warned = True
                logger.warning(f"[TTN] 课前进度计算失败({start}): {e}")
            self._bottom_progress.setValue(0)

    def _render_normal_mode(self, current_row, next_row, now):
        """上课态

        Args:
            current_row: 当前节dict
            next_row: 下节课节次/None
        """
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
                self._next_label.setText(f"下节课 {next_subject}" if next_subject else "下节课 --")
            else:
                self._next_label.setText("下节课 --")
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
            except Exception as e:
                if not getattr(self, "_ttn_time_warned", False):
                    self._ttn_time_warned = True
                    logger.warning(f"[TTN] 上课时长解析失败({start}~{end}): {e}")
                self._time_progress_label.setText("-- min / -- min")
                self._bottom_progress.setValue(0)
        else:
            self._time_progress_label.setText("")
            self._bottom_progress.setValue(0)

    def apply_scale(self, factor):
        if getattr(self, '_left', None):
            self._left.setMinimumWidth(self._scaled_px(100))
        if getattr(self, '_bottom_progress', None):
            self._bottom_progress.setFixedHeight(self._scaled_px(6))
        self._apply_style()


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

class TimetableTimelineComponent(_HtmlCardComponent):
    """课程时间轴"""
    _connect_load_finished = True
    _min_size = (220, 130)
    _default_size = (400, 200)

    _object_name = "timetableTimelineContainer"

    _theme_light = {
        "accent": "#30c361",
        "accent2": "#7ce8a4",
        "glow": "rgba(48, 195, 97, 0.30)",
        "fill_tail": "rgba(48, 195, 97, 0.22)",
        "track1": "#ededf1",
        "track2": "#e2e2e8",
        "ink_future": "#000000",
        "ink_past": "#000000",
        "sub": "#000000",
        "name_bg": "#f5f5f7",
        "name_bd": "#e9e9ee",
        "dot_bg": "#ffffff",
        "dot_bd": "#c6c6ce",
        "dot_past": "rgba(48, 195, 97, 0.45)",
        "break": "#00b7c3",
        "break2": "#fbbf24",
        "break_tail": "rgba(0, 183, 195, 0.25)",
        "break_glow": "rgba(0, 183, 195, 0.38)",
        "empty_ink": "#000000",
    }
    _theme_dark = {
        "accent": "#30c361",
        "accent2": "#5fd98c",
        "glow": "rgba(48, 195, 97, 0.32)",
        "fill_tail": "rgba(48, 195, 97, 0.18)",
        "track1": "#3a3a41",
        "track2": "#323238",
        "ink_future": "#ffffff",
        "ink_past": "#ffffff",
        "sub": "#ffffff",
        "name_bg": "rgba(255, 255, 255, 0.08)",
        "name_bd": "rgba(255, 255, 255, 0.14)",
        "dot_bg": "rgba(255, 255, 255, 0.05)",
        "dot_bd": "#52525a",
        "dot_past": "rgba(48, 195, 97, 0.42)",
        "break": "#00b7c3",
        "break2": "#00d5e0",
        "break_tail": "rgba(0, 183, 195, 0.20)",
        "break_glow": "rgba(0, 183, 195, 0.35)",
        "empty_ink": "#ffffff",
    }

    _HTML_TEMPLATE = Template('''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { width: 100%; height: 100%; background: transparent; overflow: hidden; }
  body { font-family: $font; }
  #wrap {
    position: relative; width: 100%; height: 100%; overflow: hidden;
    -webkit-mask-image: linear-gradient(90deg, transparent 0, #000 26px, #000 calc(100% - 26px), transparent 100%);
    mask-image: linear-gradient(90deg, transparent 0, #000 26px, #000 calc(100% - 26px), transparent 100%);
  }
  #empty {
    position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%);
    font-size: 15px; font-weight: 600; letter-spacing: 4px; color: $empty_ink; display: none;
  }
  #lane {
    position: absolute; left: 0; top: 0; height: 100%;
    transition: transform .8s cubic-bezier(.22, .9, .26, 1);
    will-change: transform;
  }
  #lane.noanim { transition: none; }
  .track {
    position: absolute; left: 0; top: calc(58% - 1.5px); height: 3px;
    background: linear-gradient(90deg, $track1, $track2); border-radius: 2px;
  }
  .fill {
    position: absolute; left: 0; top: calc(58% - 1.5px); height: 3px;
    background: linear-gradient(90deg, $fill_tail, $accent); border-radius: 2px;
    box-shadow: 0 0 10px $glow;
  }
  .fill::after {
    content: ''; position: absolute; inset: 0; border-radius: inherit;
    background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.38), transparent);
    background-size: 90px 100%; background-repeat: repeat-x;
    animation: shimmer 2.4s linear infinite;
  }
  .fill::before {
    content: ''; position: absolute; right: -2px; top: 50%; transform: translateY(-50%);
    width: 7px; height: 7px; border-radius: 50%; background: #fff;
    box-shadow: 0 0 12px 2px $accent;
  }
  .fill.break { background: linear-gradient(90deg, $break_tail, $break); box-shadow: 0 0 10px $break_glow; }
  .fill.break::before { box-shadow: 0 0 12px 2px $break; }
  .fill.zero::before { opacity: 0; }
  .fill.done { background: linear-gradient(90deg, $fill_tail, $accent); }
  @keyframes shimmer { from { background-position: 0 0; } to { background-position: 90px 0; } }
  .node { position: absolute; top: 0; height: 100%; width: 0; }
  .node.enter { animation: rise .55s cubic-bezier(.2, .8, .3, 1) both; }
  @keyframes rise { from { opacity: 0; transform: translateY(14px); } to { opacity: 1; transform: none; } }
  .name {
    position: absolute; top: calc(58% - 68px); left: 0; transform: translateX(-50%);
    max-width: 118px; padding: 5px 15px; border-radius: 999px;
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    font-size: 16px; font-weight: 700; color: $ink_future;
    background: $name_bg; border: 1px solid $name_bd;
    transition: background .35s, color .35s, transform .35s, box-shadow .35s, border-color .35s;
  }
  .node.past .name { color: $ink_past; background: transparent; border-color: transparent; font-weight: 600; }
  .node.current .name {
    background: linear-gradient(135deg, $accent, $accent2);
    color: #fff; font-weight: 700; border-color: transparent;
    box-shadow: 0 5px 16px $glow, 0 1px 3px rgba(0, 0, 0, 0.15);
    transform: translateX(-50%) scale(1.1);
    animation: breathe 3s ease-in-out 1.2s infinite;
  }
  .node.current.breakph .name {
    background: linear-gradient(135deg, $break, $break2);
    box-shadow: 0 5px 16px $break_glow, 0 1px 3px rgba(0, 0, 0, 0.15);
  }
  .node.current.doneph .name {
    background: linear-gradient(135deg, $accent, $accent2);
    box-shadow: 0 5px 16px $glow, 0 1px 3px rgba(0, 0, 0, 0.15);
    animation: none;
  }
  @keyframes breathe {
    0%, 100% { transform: translateX(-50%) scale(1.1); }
    50% { transform: translateX(-50%) scale(1.16); }
  }
  .time {
    position: absolute; top: calc(58% - 32px); left: 0; transform: translateX(-50%);
    white-space: nowrap; font-size: 12px; font-weight: 600; color: $sub;
    font-variant-numeric: tabular-nums;
    letter-spacing: .4px; transition: color .35s;
  }
  .node.current .time { color: $ink_future; }
  .node.current.breakph .time { color: $ink_future; }
  .node.past .time { color: $ink_past; }
  .dot {
    position: absolute; top: calc(58% - 5px); left: -5px; width: 10px; height: 10px;
    border-radius: 50%; background: $dot_bg; border: 2px solid $dot_bd;
    transition: background .35s, border-color .35s, box-shadow .35s, width .35s, height .35s, left .35s, top .35s;
  }
  .node.past .dot { background: $dot_past; border-color: $dot_past; }
  .node.current .dot {
    width: 16px; height: 16px; left: -8px; top: calc(58% - 8px);
    background: $accent; border-color: $accent; box-shadow: 0 0 0 4px $glow;
  }
  .node.current.breakph .dot { background: $break; border-color: $break; box-shadow: 0 0 0 4px $break_glow; }
  .ring {
    position: absolute; inset: -3px; border-radius: 50%;
    border: 2px solid $accent; opacity: 0; pointer-events: none;
  }
  .node.current .ring { animation: ripple 5.2s cubic-bezier(.2, .6, .35, 1) infinite; }
  .node.current .ring.r2 { animation-delay: 2.6s; }
  .node.current.breakph .ring { border-color: $break; }
  @keyframes ripple {
    0% { transform: scale(0.5); opacity: 0.75; }
    75%, 100% { transform: scale(3.2); opacity: 0; }
  }
  .period {
    position: absolute; top: calc(58% + 16px); left: 0; transform: translateX(-50%);
    white-space: nowrap; font-size: 13px; font-weight: 600; color: $sub; transition: color .35s;
  }
  .node.current .period { color: $ink_future; font-weight: 700; }
  .node.current.breakph .period { color: $ink_future; }
  .node.past .period { color: $ink_past; }
  .cd {
    font-variant-numeric: tabular-nums; font-weight: 700;
    display: none;
  }
  .cd.on { display: inline; animation: fadein .3s ease both; }
  @keyframes fadein { from { opacity: 0; } to { opacity: 1; } }
  .swap { animation: swapin .38s cubic-bezier(.2, .8, .3, 1); }
  @keyframes swapin { from { opacity: 0; transform: translateY(4px); } to { opacity: 1; transform: none; } }
</style>
</head>
<body>
<div id="wrap">
  <div id="empty">今日课表未配置</div>
  <div id="lane">
    <div class="track" id="track"></div>
    <div class="fill" id="fill"></div>
  </div>
</div>
<script>
(function () {
  var nodes = /*DATA*/[];
  var SPACING = 120, PAD_L = 60, PAD_R = 95;
  var wrap = document.getElementById('wrap');
  var lane = document.getElementById('lane');
  var track = document.getElementById('track');
  var fill = document.getElementById('fill');
  var empty = document.getElementById('empty');
  var els = [];
  var laneW = 0, curOffset = 0, booted = false;

  function pt(s) { var p = s.split(':'); return (+p[0]) * 3600 + (+p[1]) * 60; }
  function nodeX(i) { return PAD_L + i * SPACING; }
  function mk(tag, cls, parent) { var d = document.createElement(tag); d.className = cls; parent.appendChild(d); return d; }

  function setTxt(elm, txt, anim) {
    if (elm.textContent === txt) return;
    elm.textContent = txt;
    if (!anim) return;
    elm.classList.remove('swap');
    void elm.offsetWidth;
    elm.classList.add('swap');
  }

  function build() {
    for (var i = 0; i < els.length; i++) lane.removeChild(els[i].root);
    els = [];
    if (!nodes.length) {
      laneW = 0; track.style.width = '0px'; fill.style.width = '0px';
      empty.style.display = 'block';
      return;
    }
    empty.style.display = 'none';
    laneW = nodeX(nodes.length - 1) + PAD_R;
    track.style.width = laneW + 'px';
    fill.style.width = '0px';
    for (var j = 0; j < nodes.length; j++) {
      var root = mk('div', 'node enter', lane);
      root.style.left = nodeX(j) + 'px';
      root.style.animationDelay = (j * 55) + 'ms';
      var nm = mk('div', 'name', root);
      var nms = mk('span', '', nm);
      var tm = mk('div', 'time', root);
      var dt = mk('div', 'dot', root);
      mk('i', 'ring', dt);
      mk('i', 'ring r2', dt);
      var pd = mk('div', 'period', root);
      var pds = mk('span', '', pd);
      var cd = mk('span', 'cd', pd);
      els.push({ root: root, name: nms, time: tm, period: pds, cd: cd });
    }
  }

  function compute() {
    if (!nodes.length) return { idx: -1 };
    var now = new Date();
    var t = now.getHours() * 3600 + now.getMinutes() * 60 + now.getSeconds();
    var last = nodes.length - 1;
    if (t < pt(nodes[0].start)) return { idx: 0, phase: 'class', prog: 0, before: true };
    for (var i = 0; i < nodes.length; i++) {
      var s = pt(nodes[i].start), e = pt(nodes[i].end);
      if (t >= s && t < e) return { idx: i, phase: 'class', prog: (t - s) / (e - s) };
      var b = nodes[i].break;
      if (b) {
        var bs = pt(b.start), be = pt(b.end);
        if (t >= bs && t < be) return { idx: i, phase: 'break', prog: (t - bs) / (be - bs) };
      }
    }
    var lb = nodes[last].break;
    var lastEnd = lb ? pt(lb.end) : pt(nodes[last].end);
    if (t >= lastEnd) return { idx: last, phase: 'done', prog: 1, done: true };
    for (var k = last; k >= 0; k--) {
      if (t >= pt(nodes[k].start)) {
        var bb = nodes[k].break;
        if (bb && t < pt(bb.end)) return { idx: k, phase: 'break', prog: 1 };
        return { idx: k, phase: 'class', prog: 1 };
      }
    }
    return { idx: 0, phase: 'class', prog: 0, before: true };
  }

  function fmt(sec) {
    var m = Math.floor(sec / 60), s = Math.floor(sec % 60);
    return m + ':' + (s < 10 ? '0' : '') + s;
  }

  function render(st) {
    if (st.idx < 0) return;
    var curIdx = st.before ? -1 : st.idx;
    for (var i = 0; i < els.length; i++) {
      var el = els[i], n = nodes[i];
      var cls = 'node ';
      if (i < st.idx || (st.done && i < st.idx)) cls += 'past';
      else if (st.done && i === st.idx) cls += 'current doneph';
      else if (i === curIdx) cls += 'current' + (st.phase === 'break' ? ' breakph' : '');
      else cls += 'future';
      if (el.root.className.indexOf('enter') >= 0) cls += ' enter';
      if (el.root.className !== cls) {
        el.root.className = cls;
        // 标签在课程名和课间之间切换 课间结束变回课程名
        if (st.done && i === st.idx) {
          setTxt(el.name, '放学', booted);
        } else if (i === st.idx && st.phase === 'break' && n.break) {
          setTxt(el.name, '课间', booted);
          setTxt(el.time, n.break.start + '-' + n.break.end, booted);
        } else {
          setTxt(el.name, n.name, booted);
          setTxt(el.time, n.start + '-' + n.end, booted);
        }
        setTxt(el.period, n.period, booted);
      }

      if (i === curIdx && !st.done) {
        var total = (st.phase === 'break' && n.break)
          ? pt(n.break.end) - pt(n.break.start)
          : pt(n.end) - pt(n.start);
        var left = total * (1 - Math.max(0, Math.min(1, st.prog)));
        var txt = ' · ' + fmt(left);
        if (el.cd.textContent !== txt) el.cd.textContent = txt;
      }
      el.cd.classList.toggle('on', i === curIdx && !st.done);
    }
    var right;
    if (st.before) right = 0;
    else if (st.done) right = laneW - 26;
    else right = nodeX(st.idx) + Math.max(0, Math.min(1, st.prog)) * SPACING;
    var w = Math.min(right, laneW - 26);
    fill.style.width = w + 'px';
    fill.classList.toggle('break', st.phase === 'break' && curIdx >= 0 && !st.done);
    fill.classList.toggle('done', !!st.done);
    fill.classList.toggle('zero', w < 8);
    var vw = wrap.clientWidth;
    if (vw > 0 && laneW > 0) {
      var target = st.before ? 0
        : st.done ? Math.max(0, laneW - vw)
        : Math.max(0, Math.min(nodeX(st.idx) - vw / 2, laneW - vw));
      if (Math.abs(target - curOffset) > 0.5) {
        curOffset = target;
        lane.style.transform = 'translateX(' + (-curOffset) + 'px)';
      }
    }
  }

  window.updateSchedule = function (data) {
    nodes = data || [];
    build();
    return nodes.length;
  };

  build();
  var st0 = compute();
  var vw0 = wrap.clientWidth;
  if (st0.idx >= 0 && !st0.before && vw0 > 0 && laneW > 0) {
    curOffset = Math.max(0, Math.min(nodeX(st0.idx) - vw0 / 2, laneW - vw0));
  } else {
    curOffset = 0;
  }
  lane.classList.add('noanim');
  lane.style.transform = 'translateX(' + (-curOffset) + 'px)';
  void lane.offsetWidth;
  setTimeout(function () { lane.classList.remove('noanim'); }, 80);

  (function tick() { render(compute()); booted = true; requestAnimationFrame(tick); })();
})();
</script>
</body>
</html>''')

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName(self._object_name)
        self._timetable_page = None
        self._nodes = []
        self._synced = False
        self._connect_timetable_page()
        self._nodes = self._get_nodes()
        self._setup_ui()
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._refresh)
        self._timer.start(1000)
        logger.debug(f"[TTT] 初始化 节点={len(self._nodes)} 信号={'连' if self._timetable_page is not None else '无'}")


    def _build_html(self) -> str:
        theme = self._theme_dark if isDarkTheme() else self._theme_light
        tc = cfg.themeColor.value
        tc = QColor(tc) if isinstance(tc, str) else tc
        theme = dict(theme)
        theme["accent"] = tc.name()[:7]
        theme["glow"] = f"rgba({tc.red()}, {tc.green()}, {tc.blue()}, 0.32)"
        theme["fill_tail"] = f"rgba({tc.red()}, {tc.green()}, {tc.blue()}, 0.20)"
        theme["dot_past"] = f"rgba({tc.red()}, {tc.green()}, {tc.blue()}, 0.45)"
        html = self._HTML_TEMPLATE.substitute(font=FONT_FAMILY, **theme)
        html = html.replace("/*DATA*/[]", json.dumps(self._nodes, ensure_ascii=False))
        logger.debug(f"[TTT] 生成时间线 html 长度 {len(html)} 节点{len(self._nodes)}个")
        return html

    def _refresh(self):
        nodes = self._get_nodes()
        if not nodes:
            iv = self._timer.interval()
            self._timer.setInterval(min(max(iv, 500) * 2, 30000))
            return
        if self._timer.interval() != 30000:
            self._timer.setInterval(30000)
        if nodes == self._nodes and self._synced:
            return
        self._nodes = nodes
        logger.debug(f"[TTT] 时间线刷新: 获取到{len(nodes)}个节点")
        self._push_nodes()

    def _push_nodes(self):
        self._synced = False
        try:
            js = f"if (window.updateSchedule) updateSchedule({json.dumps(self._nodes, ensure_ascii=False)});"
            self.webView.page().runJavaScript(js, self._on_push_result)
        except Exception as e:
            if not getattr(self, "_push_warned", False):
                self._push_warned = True
                logger.warning(f"[TTT] 课表节点推送失败 1s 后重试: {e}")
            self._timer.setInterval(1000)

    def _on_push_result(self, result):
        self._synced = bool(result)
        if not self._synced:
            logger.debug("[TimetableTimeline] 课表节点推送未确认 1s 后重试")
            self._timer.setInterval(1000)
        else:
            logger.debug(f"[TimetableTimeline] 课表节点已推送: {len(self._nodes)}个")

    def _on_load_finished(self, ok):
        logger.debug(f"[TTT] 时间线页面加载 ok={ok} 节点={len(self._nodes)}")
        if ok and self._nodes:
            self._push_nodes()

    def _connect_timetable_page(self):
        self._timetable_page = _find_timetable_page()
        if self._timetable_page is not None:
            self._timetable_page.scheduleChanged.connect(self._refresh)

    def _get_nodes(self):
        if not self._timetable_page:
            self._connect_timetable_page()
            if not self._timetable_page:
                return []
        try:
            schedule = self._timetable_page.get_today_schedule()
        except Exception as e:
            if not getattr(self, "_nodes_sched_warned", False):
                self._nodes_sched_warned = True
                logger.warning(f"[TimeNodes] 获取今日课表失败: {e}")
            return []
        nodes = []
        for row in schedule:
            subject, teacher, start, end, idx, is_current, is_break, break_name = row
            if is_break:
                if nodes:
                    nodes[-1]["break"] = {"start": start, "end": end}
                continue
            nodes.append({
                "name": subject or "—",
                "start": start,
                "end": end,
                "period": f"第{idx}节" if idx else "—",
                "break": None,
            })
        logger.debug(f"[TTT] 时间线已构建 {len(nodes)}节")
        return nodes

class CalculatorComponent(DraggableContainer):
    """计算器"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("calculatorContainer")
        self._expression = ""
        self._result_shown = False

        self._setup_ui()
        self._apply_style()
        logger.debug(f"[CALC] 计算器就绪 id={self.component_id}")

    def _setup_ui(self):
        # 历史表达式行
        self.history_display = CaptionLabel("")
        self.history_display.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        self.history_display.setWordWrap(False)
        self.history_display.setTextFormat(Qt.TextFormat.RichText)
        self.history_display.setObjectName("calculatorHistory")
        self.history_display.setMinimumHeight(24)
        self.history_display.setMaximumHeight(36)
        self.history_display.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Minimum)

        # 显示区
        self.display = BodyLabel("0")
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
            btn = PushButton(text)
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

        self.setMinimumSize(160, 240)
        self._size_explicitly_set = True
        self.resize(300, 480)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self._update_display()

    def _format_number(self, num_str: str) -> str:
        """数字转科学计数法"""
        if not num_str or 'e' in num_str or 'E' in num_str:
            return num_str

        sign = '-' if num_str.startswith('-') else ''
        if sign:
            num_str = num_str[1:]

        integer_part, _, decimal_part = num_str.partition('.')
        if len(integer_part) > 15 or len(integer_part) + len(decimal_part) > 18:
            try:
                value = float(num_str) if decimal_part else int(num_str)
                sci = f"{value:.15e}".replace('E', 'e')
                return sign + (sci.rstrip('0').rstrip('.') if '.' in sci else sci)
            except ValueError:
                return num_str
        # 千分位
        try:
            return sign + "{:,}".format(int(integer_part)) + ('.' + decimal_part if decimal_part else '')
        except ValueError:
            return num_str

    def _prepare_display_expr(self, expression: str) -> str:
        """表达式转显示串

        Returns:
            显示用串
        """
        if expression.startswith('-') and re.fullmatch(r'-\d+\.?\d*', expression):
            return expression.replace('-', ' − ', 1)

        display_expr = re.sub(r'(^|[+\-*/])-(\d+\.?\d*)', lambda m: f"{m.group(1)}(-{m.group(2)})", expression)
        display_text = display_expr.replace("/", " ÷ ")
        display_text = display_text.replace("*", " × ")
        display_text = display_text.replace("-", " − ")
        display_text = display_text.replace("+", " + ")
        return display_text

    def _fit_label_font(self, label: QLabel, text: str, base_size: int, min_size: int = 12):
        """适配字号"""
        raw_text = re.sub(r'<[^>]+>', '', text)
        raw_text = raw_text.replace('\r', '').replace('\n', '').replace('<br/>', ' ')
        font = label.font()
        base_size = self._scaled_px(base_size)
        min_size = self._scaled_px(min_size)
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
        """拆分当前表达式

        Returns:
            (左值, 运算符, 右值)/None
        """
        if not self._expression:
            return "", ""

        if self._expression.endswith(tuple('+-*/')):
            return self._expression, ""

        match = re.search(r'(.+?[+\-*/])(-?\d*\.?\d*)$', self._expression)
        if match:
            return match.group(1), match.group(2)
        return "", self._expression

    def _append_operator(self, operator: str):
        """运算符追加
        """
        logger.debug(f"[CALC] 追加运算符 '{operator}' 当前='{self._expression or '(空)'}'")
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

    def _on_button_click(self, key):
        calc_key = {"÷": "/", "×": "*", "−": "-"}.get(key, key)
        m = re.search(r'(-?\d+\.?\d*)$', self._expression)
        logger.debug(f"[CALC] 按键 '{key}' 当前表达式='{self._expression or '(空)'}'")

        if key == "C":
            logger.info(f"[CALC] 清空表达式 (原: '{self._expression}')")
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
        elif key == "=":
            self._calculate()
        elif key == "±":
            self._toggle_sign()
        elif key == "%":
            if m:
                n = m.group(1)
                self._expression = self._expression[:m.start()] + f"({n}/100)"
                self._update_display()
        elif key == ".":
            if self._result_shown:
                self._expression = "0."
                self._result_shown = False
            elif not (m and "." in m.group(1)):
                if not self._expression or self._expression[-1] in "+-*/":
                    self._expression += "0."
                else:
                    self._expression += "."
        elif key.isdigit():
            if self._result_shown:
                self._expression = calc_key
                self._result_shown = False
            elif m is None or len(m.group(1).lstrip('-').replace('.', '')) < 15:
                self._expression += calc_key
        elif calc_key in "+-*/":
            self._append_operator(calc_key)
            self._result_shown = False
        else:
            self._expression += calc_key

        self._update_display()

    def _update_display(self):
        if not self._expression:
            self.history_display.setText("")
            self.display.setText("0")
            self._fit_label_font(self.display, "0", 24, 12)
            return

        head, current = self._split_expression()
        if head:
            self.history_display.setText(f'<span style="color: {self._history_color};">{self._prepare_display_expr(head)}</span>')
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

    def _calculate(self):
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
            result = eval(expr, {"__builtins__": {}}, {})

            if isinstance(result, float) and result.is_integer():
                result = int(result)
            elif isinstance(result, float):
                result = round(result, 10)

            result_str = str(result)
            logger.debug(f"[CALC] 求值: {expr} = {result_str}")
            self.history_display.setText(f'<span style="color: {self._history_color};">{self._prepare_display_expr(expr)} =</span>')
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
        """负号切换"""
        if not self._expression:
            return
        if self._expression[-1] in '+-*/':
            return
        logger.debug(f"[CALC] 正负切换: '{self._expression}'")

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

    def _apply_style(self):
        opacity = cfg.componentCardOpacity.value / 100.0
        radius = cfg.componentCardRadius.value

        is_dark = isDarkTheme()
        if is_dark:
            border_color = "rgba(255,255,255,0.05)"
            display_text = "#ffffff"
            self._history_color = "rgba(255,255,255,0.5)"
            btn_num_color = QColor(51, 51, 51)
            btn_num_press_color = QColor(77, 77, 77)
            btn_op_color = QColor(255, 159, 10)
            btn_op_text = "#ffffff"
        else:
            border_color = "rgba(0,0,0,0.05)"
            display_text = "#000000"
            self._history_color = "rgba(0,0,0,0.45)"
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
        btn_radius = self._scaled_px(radius)

        self.history_display.setMinimumHeight(self._scaled_px(24))
        self.history_display.setMaximumHeight(self._scaled_px(36))
        self.display.setMinimumHeight(self._scaled_px(80))
        self.display.setMaximumHeight(self._scaled_px(140))

        pad_x = self._scaled_px(8)
        pad_y = self._scaled_px(6)

        self.display.setTextFormat(Qt.TextFormat.RichText)
        self.display.setStyleSheet(f"""
            color: {display_text};
            font-size: {sz_display}px;
            font-family: {FONT_FAMILY};
            font-weight: 300;
            background-color: transparent;
            border: none;
            padding: {pad_y}px {pad_x}px;
            line-height: 1.2;
            white-space: nowrap;
        """)

        self.history_display.setStyleSheet(f"color: {self._history_color}; font-size: {sz_hist}px; background: transparent; border: none; padding: 0 {pad_x}px;")

        operator_keys = {"÷", "×", "−", "+", "="}
        for text, btn in self.buttons.items():
            if text in operator_keys:
                btn.setStyleSheet(f"""
                    PushButton {{
                        color: {btn_op_text};
                        font-size: {sz_op}px;
                        font-family: {FONT_FAMILY};
                        background-color: {btn_op_bg};
                        border: none;
                        border-radius: {btn_radius}px;
                    }}
                    PushButton:pressed {{
                        background-color: rgba(255, 159, 10, 0.7);
                    }}
                """)
            else:
                btn.setStyleSheet(f"""
                    PushButton {{
                        color: #ffffff;
                        font-size: {sz_num}px;
                        font-family: {FONT_FAMILY};
                        background-color: {btn_num_bg};
                        border: none;
                        border-radius: {btn_radius}px;
                    }}
                    PushButton:pressed {{
                        background-color: {btn_num_press};
                    }}
                """)

        self.updateSize()

class _OverToolBtn(QWidget):
    """书写工具栏按钮"""

    clicked = pyqtSignal()
    doubleClicked = pyqtSignal()

    def __init__(self, icon, text: str, parent=None, scale: float = 1.0):
        super().__init__(parent)
        self._scale = scale
        self._checked = False
        self.setFixedSize(self._px(52), self._px(64))
        layout = QVBoxLayout(self)
        layout.setContentsMargins(2, 6, 2, 4)
        layout.setSpacing(3)

        self._icon_label = QLabel(self)
        self._icon_label.setFixedSize(self._px(24), self._px(24))
        self._icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self._icon_label, 0, Qt.AlignmentFlag.AlignCenter)

        self._text_label = CaptionLabel(text, self)
        self._text_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self._text_label, 0, Qt.AlignmentFlag.AlignCenter)

        self._icon = icon
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self._update_style()

    def _px(self, v: int) -> int:
        return max(1, int(v * self._scale))

    def set_scale(self, scale: float):
        self._scale = scale
        self.setFixedSize(self._px(52), self._px(64))
        self._update_style()

    def setChecked(self, v: bool):
        self._checked = v
        self._update_style()

    def mousePressEvent(self, event):
        self.clicked.emit()
        super().mousePressEvent(event)

    def mouseDoubleClickEvent(self, event):
        self.doubleClicked.emit()
        super().mouseDoubleClickEvent(event)

    def _update_style(self):
        if self._checked:
            self.setStyleSheet(
                f"_OverToolBtn{{background:rgba(0,120,212,200);border-radius:{self._px(6)}px;}}"
            )
            self._text_label.setStyleSheet(
                f"font-size:{self._px(12)}px;font-weight:600;font-family:{FONT_FAMILY};color:#ffffff;border:none;background:transparent;")
        else:
            self.setStyleSheet(
                f"_OverToolBtn{{background:transparent;border-radius:{self._px(6)}px;}}"
                "_OverToolBtn:hover{background:rgba(255,255,255,25);}"
            )
            self._text_label.setStyleSheet(
                f"font-size:{self._px(12)}px;font-family:{FONT_FAMILY};color:#aaaaaa;border:none;background:transparent;")
        self._icon_label.setPixmap(self._icon.icon().pixmap(self._px(24), self._px(24)))

# WM_POINTER

if not hasattr(wintypes, 'HRESULT'):
    wintypes.HRESULT = wintypes.LONG

S_OK = 0
WM_POINTERDOWN   = 0x0246
WM_POINTERUPDATE = 0x0245
WM_POINTERUP     = 0x0247
POINTER_FLAG_INCONTACT = 0x00000004
PT_TOUCH = 2

# GetPointerType 函数绑定(签名: (指针id, 类型出参) -> BOOL)
_GetPointerType = ctypes.windll.user32.GetPointerType
_GetPointerType.restype = wintypes.BOOL
_GetPointerType.argtypes = [ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32)]

def _disable_edge_gestures(hwnd):
    """禁用系统边缘手势"""
    try:
        # SHGetPropertyStoreForWindow(HWND, REFIID, void**) 返回 HRESULT
        try:
            shgppfw = ctypes.windll.shell32.SHGetPropertyStoreForWindow
            shgppfw.argtypes = [wintypes.HWND, ctypes.c_void_p, ctypes.c_void_p]
            shgppfw.restype = wintypes.HRESULT
        except AttributeError as e:
            logger.debug(f"[Writing] 系统不支持 SHGetPropertyStoreForWindow {e}")
            return False

        IID_IPropertyStore = GUID("{886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99}")
        pStore = ctypes.c_void_p(0)
        hr = shgppfw(hwnd, ctypes.byref(IID_IPropertyStore), ctypes.byref(pStore))
        if hr != S_OK or not pStore.value:
            return False

        # PKEY_EdgeGestureEnable = {32CE38B2-2C9A-41B1-9BC5-B3784394AA44}, pid=2
        class PROPERTYKEY(ctypes.Structure):
            """Windows 属性键结构"""

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
    except Exception as e:
        logger.debug(f"[Writing] 边缘手势禁用设置失败(可选功能): {e}")
        return False

class _WritingOverlay(QWidget):
    """全屏书写覆盖层"""
    STROKE_TOLERANCE = 15.0
    ERASE_BASE_MIN = 25.0
    ERASE_BASE_MAX = 200.0
    ERASE_INIT = 20.0
    # 擦除字典
    _ERASE_STATE_KEYS = ('_erase_prev_pos', '_erase_speed', '_erase_rubber',
'_erase_cursors', '_erase_live_pos',
                         '_erase_prev_sample')

    def __init__(self, component):
        super().__init__()
        self._component = component
        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint | Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating, True)
        self.setAutoFillBackground(False)
        self.setMouseTracking(True)
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)

        screen_rect = QRect()
        for screen in QApplication.screens():
            screen_rect = screen_rect.united(screen.geometry())
        self._screen_rect = screen_rect
        self.setGeometry(screen_rect)

        self._buffer = QPixmap(screen_rect.size())
        self._buffer.fill(Qt.GlobalColor.transparent)
        self._temp_pixmaps = {}
        self._strokes = []
        self._current_strokes = {}
        self._whiteboard = False
        self._mode = 1  # 0=mouse 1=pen 2=eraser
        self._active_touch_ids = set()

        self._pen_color = QColor(component._pen_color)
        self._pen_width = component._pen_width
        self._draw_mode = component._draw_mode

        self._history = []
        self._erase_session = []

        prim = QApplication.primaryScreen()
        pw = prim.geometry().width() if prim else 1920
        ph = prim.geometry().height() if prim else 1080
        self._drawing_scale = min(pw / 1920.0, ph / 1080.0)

        # 擦除状态: tid - value
        self._erase_prev_pos = {}
        self._erase_speed = {}
        self._erase_rubber = {}
        self._erase_cursors = {}

        self._mouse_down = False

        self._float_bar = None
        self._create_floating_toolbar()

        # 触控事件
        self._touch_queue = deque()
        self._touch_queue_lock = threading.Lock()
        self._touch_timer = QTimer(self)
        self._touch_timer.setInterval(0)
        self._touch_timer.timeout.connect(self._process_touch_queue)

        # 速度采样
        self._erase_live_pos = {}
        self._erase_prev_sample = {}
        self._erase_speed_timer = QTimer(self)
        self._erase_speed_timer.setInterval(50)
        self._erase_speed_timer.timeout.connect(self._sample_erase_speed)
        self._erase_speed_timer.start()

        # 擦除循环
        self._erase_loop_timer = QTimer(self)
        self._erase_loop_timer.setInterval(16)
        self._erase_loop_timer.timeout.connect(self._erase_loop_tick)

    # 覆盖层显示
    def show_overlay(self, whiteboard):
        """提权窗口

        Args:
            whiteboard: True 白板模式打开
        """
        self.hide()

        self.setWindowFlag(Qt.WindowType.WindowStaysOnTopHint, True)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setAttribute(Qt.WidgetAttribute.WA_ShowWithoutActivating, True)
        self.setAutoFillBackground(False)

        self.setGeometry(self._screen_rect)
        self._whiteboard = whiteboard
        logger.info(f"[PAD] 覆盖层 {'白板' if whiteboard else '批注'} 屏幕 {self._screen_rect.width()}x{self._screen_rect.height()}")
        self._update_float_buttons()
        fb_w = self._float_bar.width()
        fb_h = self._float_bar.height()
        x = (self.width() - fb_w) // 2
        y = self.height() - fb_h - 30
        self._float_bar.move(max(0, x), max(0, y))
        self._float_bar.raise_()
        self.show()
        hwnd = int(self.winId())
        if hwnd:
            _disable_edge_gestures(hwnd)

    def clear_all(self):
        logger.info(f"[PAD] 清空全部笔迹: {len(self._strokes)}条")
        self._strokes.clear()
        self._current_strokes.clear()
        self._history.clear()
        self._erase_session.clear()
        self._buffer.fill(Qt.GlobalColor.transparent)
        for tp in self._temp_pixmaps.values():
            tp.fill(Qt.GlobalColor.transparent)
        self._temp_pixmaps.clear()
        self.update()

    # 设置同步
    def setPenColor(self, color):
        self._pen_color = QColor(color)
        logger.debug(f"[PAD] 笔色切换: {self._pen_color.name()}")

    def setPenWidth(self, w):
        self._pen_width = max(1, w)
        logger.debug(f"[PAD] 笔宽切换: {self._pen_width}px")

    def setDrawMode(self, mode):
        self._draw_mode = mode
        logger.debug(f"[PAD] 绘制模式切换: mode={mode}")

    def nativeEvent(self, eventType, message):
        if eventType != b"windows_generic_MSG" or not self.isVisible() or not self.isEnabled():
            return False, 0
        try:
            msg = ctypes.wintypes.MSG.from_address(int(message))
            if msg.message in (WM_POINTERDOWN, WM_POINTERUPDATE, WM_POINTERUP):
                return self._collect_pointer(msg), 0
        except Exception as e:
            if not getattr(self, "_native_warned", False):
                self._native_warned = True
                logger.warning(f"[PAD] 触控消息处理异常 跳过后续同类消息日志: {e}")
        return False, 0

    def hideEvent(self, event):
        logger.debug("[WB] 书写浮层隐藏 清理状态")
        self._cleanup_state()
        super().hideEvent(event)

    def closeEvent(self, event):
        logger.debug("[WB] 书写浮层窗口关闭")
        self._touch_timer.stop()
        self._erase_speed_timer.stop()
        self._cleanup_state()
        super().closeEvent(event)

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        if self._whiteboard:
            painter.fillRect(self.rect(), Qt.GlobalColor.white)
        painter.drawPixmap(0, 0, self._buffer)
        for tid, tp in list(self._temp_pixmaps.items()):
            if tp and not tp.isNull():
                painter.drawPixmap(0, 0, tp)
        if self._mode == 2 and self._erase_cursors:
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            painter.setPen(QPen(QColor(130, 130, 130, 200), 3))
            painter.setBrush(Qt.BrushStyle.NoBrush)
            for pos, diameter in self._erase_cursors.values():
                painter.drawEllipse(pos, diameter / 2.0, diameter / 2.0)

    # 鼠标事件
    def mousePressEvent(self, event):
        if self._mouse_down:
            return
        self._mouse_down = True
        if self._mode == 2:
            self._erase_at(event.position())
        elif self._mode == 1:
            self._start_stroke(event.position())

    def mouseMoveEvent(self, event):
        if not self._mouse_down:
            return
        if self._mode == 2:
            self._erase_at(event.position())
        elif self._mode == 1:
            self._update_stroke(event.position())

    def mouseReleaseEvent(self, event):
        if not self._mouse_down:
            return
        self._mouse_down = False
        if self._mode == 1:
            self._end_stroke(event.position())
        elif self._mode == 2:
            self._reset_erase_state(0)
            if not self._erase_rubber:
                self._erase_loop_timer.stop()
                self._end_erase_session()
            self.update()

    def keyPressEvent(self, event):
        if event.key() == Qt.Key.Key_Escape:
            self._close_overlay()
        super().keyPressEvent(event)

    def _collect_pointer(self, msg) -> bool:
        pointerId = msg.wParam & 0xFFFF
        ptr_flags = (msg.wParam >> 16) & 0xFFFF

        ptrType = ctypes.c_uint32(0)
        if not _GetPointerType(pointerId, ctypes.byref(ptrType)) or ptrType.value != PT_TOUCH:
            return False

        x = ctypes.c_short(msg.lParam & 0xFFFF).value
        y = ctypes.c_short((msg.lParam >> 16) & 0xFFFF).value
        pos = QPoint(x, y)
        if not (-10000 <= pos.x() <= 100000 and -10000 <= pos.y() <= 100000):
            return False

        if self._float_bar and self._float_bar.isVisible() and self._float_bar.geometry().contains(pos):
            return False

        if msg.message == WM_POINTERDOWN:
            ev_type = 0
            self._active_touch_ids.add(pointerId)
        elif msg.message == WM_POINTERUP:
            ev_type = 2
            self._active_touch_ids.discard(pointerId)
        else:
            if not (ptr_flags & POINTER_FLAG_INCONTACT):
                return False
            ev_type = 1

        tid = int(pointerId)
        with self._touch_queue_lock:
            self._touch_queue.append((ev_type, tid, pos.x(), pos.y()))
        if not self._touch_timer.isActive():
            self._touch_timer.start()
        return True

    def _sample_erase_speed(self):
        for tid, cur in list(self._erase_live_pos.items()):
            prev = self._erase_prev_sample.get(tid)
            if prev is None:
                self._erase_prev_sample[tid] = QPointF(cur)
                self._erase_speed[tid] = 1.0
            else:
                dx = cur.x() - prev.x()
                dy = cur.y() - prev.y()
                dist = (dx * dx + dy * dy) ** 0.5
                # 欧氏距离 EMA 50/50
                self._erase_speed[tid] = (self._erase_speed.get(tid, 0.0) + dist) * 0.5
                self._erase_prev_sample[tid] = QPointF(cur)

    def _push_history(self, kind, data):
        logger.debug(f"[PAD] 历史入栈: {kind}")
        self._history.append((kind, data))
        if len(self._history) > 300:
            self._history.pop(0)

    def _process_touch_queue(self):
        batch = []
        with self._touch_queue_lock:
            while self._touch_queue:
                batch.append(self._touch_queue.popleft())
        if not batch:
            self._touch_timer.stop()
            return

        if self._mode == 1 and self._draw_mode == "free":
            self._process_free_batch(batch)
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
                elif self._mode == 2:
                    self._reset_erase_state(tid)
                    if not self._erase_rubber and not self._mouse_down:
                        self._erase_loop_timer.stop()
                        self._end_erase_session()
                        self.update()

    def _process_free_batch(self, batch):
        """批量绘制轨迹

        Args:
            batch: 轨迹点列表
        """
        tid_events = {}
        order = []
        for ev_type, tid, x, y in batch:
            if tid not in tid_events:
                tid_events[tid] = []
                order.append(tid)
            tid_events[tid].append((ev_type, QPointF(x, y)))

        for tid in order:
            events = tid_events[tid]
            up_pos = None
            move_points = []
            for ev_type, pos in events:
                if ev_type == 0:
                    self._start_stroke(pos, tid)
                elif ev_type == 1:
                    move_points.append(pos)
                elif ev_type == 2:
                    up_pos = pos

            if move_points:
                self._draw_free_path(tid, move_points)

            if up_pos is not None:
                self._end_stroke(up_pos, tid)

        self.update()

    def _draw_free_path(self, tid, points):
        """平滑折线把一笔多点画上 buffer
        """
        stroke = self._current_strokes.get(tid)
        if not stroke:
            return
        pts = stroke["points"]
        if not pts:
            for p in points:
                pts.append(p)
            return

        painter = QPainter(self._buffer)
        try:
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            painter.setPen(self._stroke_pen(stroke))
            prev = pts[-1]
            for pos in points:
                painter.drawLine(prev, pos)
                pts.append(pos)
                prev = pos
        except Exception as e:
            if not getattr(self, "_stroke_warned", False):
                self._stroke_warned = True
                logger.warning(f"[Writing] 笔迹绘制失败: {e}")
        finally:
            painter.end()

    def _create_floating_toolbar(self):
        """悬浮工具栏

        Returns:
            容器 widget
        """
        self._float_bar = QWidget(self)
        self._float_bar.setObjectName("writingFloatBar")
        self._float_bar.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self._float_bar.setStyleSheet(
            f"#writingFloatBar{{background:rgba(32,32,32,220);border-radius:12px;"
            f"font-family:{FONT_FAMILY};}}"
        )

        layout = QHBoxLayout(self._float_bar)
        layout.setContentsMargins(10, 6, 10, 6)
        layout.setSpacing(4)

        def make_btn(icon, text):
            btn = _OverToolBtn(icon, text, self._float_bar)
            return btn

        self._f_mouse = make_btn(FUI.CURSOR, "鼠标")
        self._f_pen = make_btn(FUI.PEN, "画笔")
        self._f_eraser = make_btn(FUI.ERASER, "擦除")
        self._f_undo = make_btn(FUI.UNDO, "撤回")

        self._f_mouse.clicked.connect(lambda: self._set_tool_mode(0))
        self._f_pen.clicked.connect(self._on_float_pen_clicked)
        self._f_eraser.clicked.connect(lambda: self._set_tool_mode(2))
        self._f_eraser.doubleClicked.connect(self.clear_all)
        self._f_undo.clicked.connect(self._undo_last_stroke)

        layout.addWidget(self._f_mouse)
        layout.addWidget(self._f_pen)
        layout.addWidget(self._f_eraser)
        layout.addWidget(self._f_undo)

        # 分隔
        sep = QLabel(self._float_bar)
        sep.setFixedSize(1, 36)
        sep.setStyleSheet("background:rgba(255,255,255,40);border:none;")
        layout.addWidget(sep)

        self._f_trans = make_btn(FUI.BLUR, "透明")
        self._f_white = make_btn(FUI.BOARD, "白板")
        self._f_trans.clicked.connect(lambda: self._set_float_whiteboard(False))
        self._f_white.clicked.connect(lambda: self._set_float_whiteboard(True))
        layout.addWidget(self._f_trans)
        layout.addWidget(self._f_white)

        sep2 = QLabel(self._float_bar)
        sep2.setFixedSize(1, 36)
        sep2.setStyleSheet("background:rgba(255,255,255,40);border:none;")
        layout.addWidget(sep2)

        close_btn = _OverToolBtn(FUI.CLOSE, "关闭", self._float_bar)
        close_btn.clicked.connect(self._close_overlay)
        layout.addWidget(close_btn)

        self._float_bar.adjustSize()
        self._update_float_buttons()

    def _update_float_buttons(self):
        self._f_mouse.setChecked(self._mode == 0)
        self._f_pen.setChecked(self._mode == 1)
        self._f_eraser.setChecked(self._mode == 2)
        self._f_trans.setChecked(not self._whiteboard)
        self._f_white.setChecked(self._whiteboard)

    def _is_in_blacklist(self, pos):
        """判定光标是否在工具栏上

        Args:
            pos: 光标位置
        """
        if self._float_bar and self._float_bar.isVisible():
            p = pos.toPoint() if isinstance(pos, QPointF) else pos
            if self._float_bar.geometry().contains(p):
                return True
        return False

    def _end_all_strokes(self):
        _n = len(self._current_strokes)
        for tid in list(self._current_strokes.keys()):
            stroke = self._current_strokes.get(tid)
            if stroke and stroke.get("mode") == "free" and stroke.get("points"):
                self._strokes.append(stroke)
                self._push_history("draw", stroke)
            self._temp_pixmaps.pop(tid, None)
        self._current_strokes.clear()
        self.update()
        if _n:
            logger.debug(f"[WB] 结束未完笔画 {_n}笔")

    def _undo_last_stroke(self):
        """撤销一笔并重画"""
        if not self._history:
            return
        logger.debug(f"[PAD] 撤销一笔 (剩余历史 {len(self._history) - 1})")
        self._history.pop()
        self._rebuild_buffer()

    def _set_tool_mode(self, mode):
        """同步工具模式与按钮态

        Args:
            mode: 目标模式:1 笔 / 2 橡皮(白板走 _set_float_whiteboard)
        """
        logger.debug(f"[PAD] 覆盖层工具模式: {self._mode} -> {mode}")
        self._end_all_strokes()
        self._mode = mode
        # 切出擦除模式时清除状态
        if mode != 2:
            self._erase_loop_timer.stop()
            self._reset_erase_state()
            self.update()
        self._update_float_buttons()
        self._component._set_mode(mode, from_overlay=True)

    def _on_float_pen_clicked(self):
        """弹笔设置弹窗"""
        logger.debug("[WB] 浮动笔按钮点击")
        self._end_all_strokes()
        if self._mode == 1:
            self._component._show_pen_settings(overlay=self, src=self._f_pen)
        else:
            self._set_tool_mode(1)

    def _set_float_whiteboard(self, on):
        """切换白板底色"""
        logger.debug(f"[WB] 浮动白板切换 -> {'白板' if on else '透明'}")
        self._end_all_strokes()
        self.show_overlay(on)
        self._component._set_whiteboard(on, from_overlay=True)

    def _reset_erase_state(self, tid=None):
        """清橡皮状态字典

        Args:
            tid: 触点 id,None 清全部
        """
        logger.debug(f"[PAD] 重置橡皮状态: tid={tid}")
        for name in self._ERASE_STATE_KEYS:
            state = getattr(self, name)
            if tid is None:
                state.clear()
            else:
                state.pop(tid, None)

    def _cleanup_state(self):
        self._end_all_strokes()
        self._end_erase_session()
        self._active_touch_ids.clear()
        self._mouse_down = False
        with self._touch_queue_lock:
            self._touch_queue.clear()
        for tp in self._temp_pixmaps.values():
            tp.fill(Qt.GlobalColor.transparent)
        self._temp_pixmaps.clear()
        self._erase_loop_timer.stop()
        self._erase_speed_timer.stop()
        self._reset_erase_state()

    def _close_overlay(self):
        logger.debug("[WB] 书写浮层关闭")
        self._cleanup_state()
        self._component._close_overlay()

    @staticmethod
    def _stroke_pen(stroke):
        """圆头笔

        Args:
            stroke: 笔画 dict
            
        Returns:
            QPen
        """
        return QPen(stroke["color"], stroke["width"],
                    Qt.PenStyle.SolidLine, Qt.PenCapStyle.RoundCap,
                    Qt.PenJoinStyle.RoundJoin)

    def _paint_shape(self, target, stroke, mode, p0, p1):
        """画直线/矩形

        Args:
            target: buffer /临时预览层
            stroke: 笔画 dict
            mode: line / rect
            p0: 起点
            p1: 终点
        """
        def _draw(painter):
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            painter.setPen(self._stroke_pen(stroke))
            if mode == "line":
                painter.drawLine(p0, p1)
            else:
                painter.drawRect(QRectF(p0, p1).normalized())
        self._safe_paint(target, _draw)

    def _safe_paint(self, pixmap, callback):
        painter = QPainter(pixmap)
        try:
            callback(painter)
        except Exception as e:
            if not getattr(self, "_safepaint_warned", False):
                self._safepaint_warned = True
                logger.warning(f"[Writing] 形状绘制失败: {e}")
        finally:
            painter.end()

    def _paint_stroke(self, painter, s):
        pts = s["points"]
        if not pts:
            return
        painter.setPen(self._stroke_pen(s))
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
        if self._is_in_blacklist(pos):
            return
        logger.debug(f"[WB] 起笔 tid={tid} 笔型={self._draw_mode}")
        if tid in self._current_strokes:
            old = self._current_strokes.pop(tid)
            if old and old.get("points") and old.get("mode") == "free":
                self._strokes.append(old)
                self._push_history("draw", old)
            self.update()
        self._temp_pixmaps.pop(tid, None)
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
        pts = stroke["points"]
        mode = stroke.get("mode", "free")

        if mode == "free":
            if pts:
                self._safe_paint(self._buffer, lambda painter: (
                    painter.setRenderHint(QPainter.RenderHint.Antialiasing),
                    painter.setPen(self._stroke_pen(stroke)),
                    painter.drawLine(pts[-1], pos),
                ))
                pts.append(pos)
                self.update()
        else:
            pts.append(pos)
            if len(pts) >= 2:
                if tid not in self._temp_pixmaps:
                    tp = QPixmap(self._screen_rect.size())
                    tp.fill(Qt.GlobalColor.transparent)
                    self._temp_pixmaps[tid] = tp
                else:
                    tp = self._temp_pixmaps[tid]
                    tp.fill(Qt.GlobalColor.transparent)
                self._paint_shape(tp, stroke, mode, pts[0], pos)
                self.update()

    def _end_stroke(self, pos, tid=0):
        stroke = self._current_strokes.pop(tid, None)
        if not stroke:
            self._temp_pixmaps.pop(tid, None)
            return
        logger.debug(f"[WB] 收笔 tid={tid} 点数={len(stroke['points'])} 笔型={stroke.get('mode', 'free')}")
        pts = stroke["points"]
        mode = stroke.get("mode", "free")

        if mode == "free":
            if pts:
                pts.append(pos)
            self._strokes.append(stroke)
            self._push_history("draw", stroke)
        else:
            if len(pts) >= 2:
                self._paint_shape(self._buffer, stroke, mode, pts[0], pts[-1])
                stroke["points"] = [pts[0], pts[-1]]
                self._strokes.append(stroke)
                self._push_history("draw", stroke)
        self._temp_pixmaps.pop(tid, None)
        self.update()

    def _erase_on_buffer(self, prev_pos, cur_pos, diameter):
        radius = diameter / 2.0
        painter = QPainter(self._buffer)
        try:
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            painter.setCompositionMode(QPainter.CompositionMode.CompositionMode_DestinationOut)
            if prev_pos != cur_pos:
                pen = QPen(QColor(0, 0, 0, 255), diameter)
                pen.setCapStyle(Qt.PenCapStyle.RoundCap)
                pen.setJoinStyle(Qt.PenJoinStyle.RoundJoin)
                painter.setPen(pen)
                painter.setBrush(Qt.BrushStyle.NoBrush)
                painter.drawLine(prev_pos, cur_pos)
            else:
                painter.setPen(Qt.PenStyle.NoPen)
                painter.setBrush(QBrush(QColor(0, 0, 0, 255)))
                painter.drawEllipse(cur_pos, radius, radius)
        finally:
            painter.end()

    def _erase_at(self, pos, tid=0):
        if self._mode != 2:
            return
        if self._is_in_blacklist(pos):
            return

        cur = QPointF(pos)
        ds = self._drawing_scale

        if tid not in self._erase_prev_pos:
            self._erase_prev_pos[tid] = QPointF(cur)
            self._erase_live_pos[tid] = QPointF(cur)
            self._erase_prev_sample[tid] = QPointF(cur)
            self._erase_speed[tid] = 1.0
            self._erase_rubber[tid] = ds * self.ERASE_INIT
            rubber = self._erase_rubber[tid]
            self._erase_on_buffer(cur, cur, rubber)
            self._erase_session.append((QPointF(cur), rubber))
            self._erase_cursors[tid] = (QPointF(cur), rubber)
            self.update()
            if not self._erase_loop_timer.isActive():
                self._erase_loop_timer.start()
            return

        self._erase_live_pos[tid] = cur
        if not self._erase_loop_timer.isActive():
            self._erase_loop_timer.start()

    def _erase_loop_tick(self):
        if self._mode != 2 or not self._erase_live_pos:
            self._erase_loop_timer.stop()
            return
        ds = self._drawing_scale
        for tid in list(self._erase_live_pos.keys()):
            cur = self._erase_live_pos.get(tid)
            if cur is None:
                continue
            prev = self._erase_prev_pos.get(tid)
            if prev is None:
                continue

            speed = self._erase_speed.get(tid, 0.0)

            # 目标直径
            if tid == 0:
                if speed <= 30:
                    t_size = max(self.ERASE_BASE_MIN, speed * 2.33 + 2.33)
                else:
                    t_size = min(self.ERASE_BASE_MAX, speed + 30)
            else:
                if speed <= 20:
                    t_size = max(self.ERASE_BASE_MIN, speed * 2.33 + 13.33)
                else:
                    t_size = min(self.ERASE_BASE_MAX, 3.0 * speed)
            trubber = t_size * ds

            # 当前直径追随目标
            rubber = self._erase_rubber[tid]
            if rubber < trubber:
                rubber = rubber + max(0.1, (trubber - rubber) / 50.0)
            elif rubber > trubber:
                rubber = rubber + min(-0.1, (trubber - rubber) / 50.0)
            self._erase_rubber[tid] = rubber

            self._erase_on_buffer(prev, cur, rubber)
            self._erase_prev_pos[tid] = QPointF(cur)
            self._erase_session.append((QPointF(cur), rubber))
            self._erase_cursors[tid] = (QPointF(cur), rubber)
        self.update()

    def _end_erase_session(self):
        if self._erase_session:
            logger.debug(f"[WB] 擦除会话结束 记录{len(self._erase_session)}个采样点")
            self._push_history("erase", self._erase_session)
            self._erase_session = []

    def _rebuild_buffer(self):
        logger.debug(f"[WB] 重建画布缓冲: 历史操作{len(self._history)}条")
        self._buffer.fill(Qt.GlobalColor.transparent)
        self._strokes.clear()
        for op_type, op_data in self._history:
            if op_type == "draw":
                stroke = op_data
                self._strokes.append(stroke)
                self._safe_paint(self._buffer, lambda p, s=stroke: (
                    p.setRenderHint(QPainter.RenderHint.Antialiasing),
                    self._paint_stroke(p, s),
                ))
            elif op_type == "erase":
                for pos, diameter in op_data:
                    self._erase_on_buffer(pos, pos, diameter)
        self.update()

class _PenSettingsPopup(QWidget):
    """笔设置弹出面板"""

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
        layout.setContentsMargins(16, 14, 16, 14)
        layout.setSpacing(10)

        label_style = f"font-size:13px;font-weight:600;font-family:{FONT_FAMILY};color:#cccccc;border:none;background:transparent;"

        # 颜色
        cl = CaptionLabel("颜色")
        cl.setStyleSheet(label_style)
        layout.addWidget(cl)
        cr = QHBoxLayout()
        cr.setSpacing(6)
        for hex_c, _ in self.COLORS:
            btn = PushButton()
            btn.setFixedSize(30, 30)
            c = QColor(hex_c)
            border = "2px solid #555" if c.lightness() > 200 else "2px solid #888"
            btn.setStyleSheet(
                f"PushButton{{background:{hex_c};border:{border};border-radius:15px;}}"
                f"PushButton:hover{{border:2px solid white;}}"
            )
            btn.clicked.connect(lambda _, h=hex_c: self._pick_color(h))
            cr.addWidget(btn)
        cr.addStretch()
        layout.addLayout(cr)

        # 粗细
        tl = CaptionLabel("粗细")
        tl.setStyleSheet(label_style)
        layout.addWidget(tl)
        self._slider = Slider(Qt.Orientation.Horizontal)
        self._slider.setRange(1, 12)
        self._slider.setValue(self._component._pen_width)
        self._slider.valueChanged.connect(self._pick_width)
        layout.addWidget(self._slider)

        # 模式
        ml = CaptionLabel("模式")
        ml.setStyleSheet(label_style)
        layout.addWidget(ml)
        mr = QHBoxLayout()
        mr.setSpacing(8)
        self._mode_btns = {}
        for name, display in [("free", "自由"), ("line", "直线"), ("rect", "矩形")]:
            btn = PushButton(display)
            btn.setCheckable(True)
            btn.setFixedHeight(32)
            btn.setStyleSheet(
                f"PushButton{{background:#333;color:#fff;border-radius:6px;padding:0 14px;"
                f"font-size:13px;font-family:{FONT_FAMILY};}}"
                f"PushButton:checked{{background:#005fb8;}}"
                f"PushButton:hover{{background:#444;}}"
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

    def paintEvent(self, event):
        """面板底与边框"""
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setBrush(QColor(40, 40, 40, 235))
        painter.setPen(QPen(QColor(80, 80, 80), 1))
        painter.drawRoundedRect(self.rect().adjusted(1, 1, -1, -1), 12, 12)

    def _pick_color(self, hex_c):
        """更新笔色 刷新高亮

        Args:
            hex_c: 颜色 hex 串
        """
        self._component._pen_color = hex_c
        logger.debug(f"[PEN] 选择笔色: {hex_c}")
        if self._overlay:
            self._overlay._end_all_strokes()
            self._overlay.setPenColor(hex_c)

    def _pick_width(self, w):
        """应用到覆盖层

        Args:
            w: 笔宽(px)
        """
        self._component._pen_width = w
        logger.debug(f"[PEN] 选择笔宽: {w}px")
        if self._overlay:
            self._overlay._end_all_strokes()
            self._overlay.setPenWidth(w)

    def _pick_mode(self, mode):
        """切绘制模式"""
        logger.debug(f"[PEN] 切换绘制模式: {mode}")
        for n, btn in self._mode_btns.items():
            btn.setChecked(n == mode)
        self._component._draw_mode = mode
        if self._overlay:
            self._overlay._end_all_strokes()
            self._overlay.setDrawMode(mode)

class WritingPadComponent(DraggableContainer):
    """书写板组件"""

    MODE_MOUSE = 0
    MODE_PEN = 1
    MODE_ERASER = 2

    BTN_W = 52
    BTN_H = 64

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
        logger.debug(f"[WPAD] 书写板就绪 id={self.component_id}")

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setContentsMargins(10, 6, 10, 6)
        layout.setSpacing(6)
        layout.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)

        # 鼠标
        self._mouse_box = self._build_tool_btn(FUI.CURSOR, "鼠标")
        layout.addWidget(self._mouse_box)
        self._mouse_box.clicked.connect(lambda: self._set_mode(self.MODE_MOUSE))

        # 画笔
        self._pen_box = self._build_tool_btn(FUI.PEN, "画笔")
        self._pen_box.setChecked(True)
        layout.addWidget(self._pen_box)
        self._pen_box.clicked.connect(self._on_pen_clicked)

        # 擦除
        self._eraser_box = self._build_tool_btn(FUI.ERASER, "擦除")
        layout.addWidget(self._eraser_box)
        self._eraser_box.clicked.connect(lambda: self._set_mode(self.MODE_ERASER))
        self._eraser_box.doubleClicked.connect(self._clear_all)

        self._undo_box = self._build_tool_btn(FUI.UNDO, "撤回")
        layout.addWidget(self._undo_box)
        self._undo_box.clicked.connect(self._undo_last_stroke)

        # 分隔
        self._sep = QLabel(self)
        self._sep.setFixedSize(self._scaled_px(1), self._scaled_px(36))
        self._sep.setStyleSheet("background:rgba(128,128,128,80);border:none;")
        layout.addWidget(self._sep)

        # 透明
        self._trans_box = self._build_tool_btn(FUI.BLUR, "透明")
        self._trans_box.setChecked(True)
        layout.addWidget(self._trans_box)
        self._trans_box.clicked.connect(lambda: self._set_whiteboard(False))

        # 白板
        self._white_box = self._build_tool_btn(FUI.BOARD, "白板")
        layout.addWidget(self._white_box)
        self._white_box.clicked.connect(lambda: self._set_whiteboard(True))

        layout.addStretch()

        self.setMinimumSize(240, 56)
        self._size_explicitly_set = True
        self.resize(420, 80)
        self._update_button_states()


    # 按钮
    def _build_tool_btn(self, icon, text, checked=False):
        btn = _OverToolBtn(icon, text, self, scale=self._scale_factor)
        if checked:
            btn.setChecked(True)
        return btn

    # 按钮状态
    def _update_button_states(self):
        self._mouse_box.setChecked(self._mode == self.MODE_MOUSE)
        self._pen_box.setChecked(self._mode == self.MODE_PEN)
        self._eraser_box.setChecked(self._mode == self.MODE_ERASER)

    # 模式切换
    def _set_mode(self, mode, from_overlay=False):
        logger.debug(f"[PAD] 书写板切模式: {self._mode} -> {mode} (from_overlay={from_overlay})")
        self._mode = mode
        self._update_button_states()
        if self._pen_popup and self._pen_popup.isVisible():
            self._pen_popup.close()
        if mode == self.MODE_MOUSE:
            self._hide_overlay()
        elif mode in (self.MODE_PEN, self.MODE_ERASER) and not from_overlay:
            self._show_overlay()

    def _on_pen_clicked(self):
        logger.debug(f"[PAD] 笔按钮点击 (当前模式 {self._mode})")
        if self._mode == self.MODE_PEN:
            if self._overlay and self._overlay.isVisible():
                self._show_pen_settings(overlay=self._overlay, src=self._overlay._f_pen)
            else:
                self._show_overlay()
        else:
            self._set_mode(self.MODE_PEN)

    def _show_pen_settings(self, overlay=None, src=None):
        logger.debug(f"[PAD] 打开笔设置弹窗 (src={'overlay浮钮' if src and src is not self._pen_box else '笔按钮'})")
        if self._pen_popup is None:
            self._pen_popup = _PenSettingsPopup(self, overlay)
        else:
            self._pen_popup._overlay = overlay or self._overlay
        if src is None:
            src = self._pen_box
        # 锚定按钮上方
        popup_h = self._pen_popup.height()
        self._pen_popup.move(src.mapToGlobal(QPoint(0, -popup_h - 4)))
        self._pen_popup.show()

    def _undo_last_stroke(self):
        if self._overlay and self._overlay.isVisible():
            self._overlay._undo_last_stroke()
        else:
            logger.debug("[PAD] 撤回无效: 覆盖层未显示")

    def _clear_all(self):
        if self._overlay and self._overlay.isVisible():
            logger.info("[PAD] 书写板清空请求")
            self._overlay.clear_all()

    def _set_whiteboard(self, on, from_overlay=False):
        logger.debug(f"[PAD] 白板模式: {on} (from_overlay={from_overlay})")
        self._whiteboard = on
        self._white_box.setChecked(on)
        self._trans_box.setChecked(not on)
        if self._overlay and self._overlay.isVisible():
            if not from_overlay:
                self._overlay.show_overlay(on)
            else:
                self._overlay._whiteboard = on
                self._overlay._update_float_buttons()
                self._overlay.update()

    # 覆盖层
    def _show_overlay(self):
        if self._overlay is None:
            self._overlay = _WritingOverlay(self)
            logger.debug("[PAD] 创建书写覆盖层实例")
        self._overlay.show_overlay(self._whiteboard)
        self._overlay._mode = self._mode
        self._overlay._update_float_buttons()

    def _hide_overlay(self):
        if self._overlay and self._overlay.isVisible():
            self._overlay.hide()

    def _close_overlay(self):
        logger.debug("[PAD] 关闭书写覆盖层 模式回鼠标")
        self._hide_overlay()
        self._mode = self.MODE_MOUSE
        self._update_button_states()
        if self._pen_popup and self._pen_popup.isVisible():
            self._pen_popup.close()

    def apply_scale(self, factor):
        for box in (self._mouse_box, self._pen_box, self._eraser_box,
                    self._undo_box, self._trans_box, self._white_box):
            box.set_scale(factor)
        if getattr(self, '_sep', None):
            self._sep.setFixedSize(self._scaled_px(1), self._scaled_px(36))

    def _apply_style(self):
        self._apply_card_style()
        self.updateSize()

class ClassAlbumComponent(DraggableContainer):
    """班级相册"""

    SUPPORTED_EXTS = {'.png', '.jpg', '.jpeg', '.bmp', '.gif', '.webp'}
    MAX_IMAGE_DIMENSION = 4096

    # 布局配置,纵向子类覆盖
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
            # 轮播定时器(仅非临时实例)
            self._auto_timer = QTimer(self)
            self._auto_timer.setInterval(5000)
            self._auto_timer.timeout.connect(self._auto_flip_next)
            self._auto_timer.start()
        logger.debug(f"[ALBUM] 班级相册就绪 id={self.component_id} 方向={self._layout_direction} 临时={self._is_temp}")

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

        self.setMinimumSize(*self._container_min_size)
        self._size_explicitly_set = True
        self.resize(self._item_w, self._item_h)
        self._apply_card_style()

    def eventFilter(self, obj, event):
        if obj == self.flip_view and event.type() == QEvent.Type.Resize:
            self._fit_item_size()
        return super().eventFilter(obj, event)

    def dragEnterEvent(self, event):
        if event.mimeData().hasUrls():
            for url in event.mimeData().urls():
                if url.isLocalFile() and os.path.splitext(url.toLocalFile())[1].lower() in self.SUPPORTED_EXTS:
                    logger.debug("[Album] 图片拖入 接受投放")
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
            logger.debug(f"[Album] 拖入{len(paths)}个文件: {os.path.basename(paths[0])}{'...' if len(paths) > 1 else ''}")
            self._import_files(paths)
            event.acceptProposedAction()

    def showEvent(self, event):
        super().showEvent(event)
        _first = not getattr(self, "_album_shown_once", False)
        self._album_shown_once = True
        if _first:
            logger.debug(f"[ALB] 相册组件首次显示: {self.flip_view.count()}张照片")
        if not self._is_temp:
            self._auto_timer.start()
        QTimer.singleShot(0, self._fit_item_size)

    def hideEvent(self, event):
        super().hideEvent(event)
        logger.debug("[ALB] 相册组件隐藏 停止自动轮播")
        if not self._is_temp:
            self._auto_timer.stop()

    def _set_item_path(self, item, path: str):
        """写 item 的 DisplayRole

        Args:
            path: 源图绝对路径
        """
        item.setData(Qt.ItemDataRole.DisplayRole, path)

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

    def _auto_flip_next(self):
        """循环播放"""
        count = self.flip_view.count()
        if count <= 1:
            return
        idx = self.flip_view.currentIndex()
        if idx >= count - 1:
            logger.debug("[ALB] 轮播回卷到第一张")
            self.flip_view.scrollToIndex(0)
        else:
            self.flip_view.scrollNext()

    def _safe_load_pixmap(self, path: str):
        reader = QImageReader(path)
        size = reader.size()
        if not size.isValid() or size.width() <= 0 or size.height() <= 0:
            logger.warning(f"[Album] 图片尺寸读取失败: {path}")
            return None
        if size.width() > self.MAX_IMAGE_DIMENSION or size.height() > self.MAX_IMAGE_DIMENSION:
            reader.setScaledSize(size.scaled(
                self.MAX_IMAGE_DIMENSION, self.MAX_IMAGE_DIMENSION,
                Qt.AspectRatioMode.KeepAspectRatio
            ))
        image = reader.read()
        if image.isNull():
            logger.warning(f"[Album] 图片读取失败: {path}")
            return None
        return QPixmap.fromImage(image)

    def _load_photos(self):
        self.flip_view.clear()
        if not os.path.isdir(self._photos_dir):
            logger.debug(f"[Album] 相册目录不存在: {self._photos_dir}")
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
        logger.debug(f"[Album] 相册已加载 {self.flip_view.count()}张 (目录={os.path.basename(self._photos_dir)})")

    def _import_files(self, file_paths: list):
        if self._is_temp:
            return
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
            logger.debug(f"[Album] 导入照片: {fname} -> {os.path.basename(dst_path)}")
            pm = self._safe_load_pixmap(dst_path)
            if pm:
                item = self.flip_view.addImage(self._prepare_image(pm))
                if isinstance(item, QListWidgetItem):
                    self._set_item_path(item, dst_path)
        if self.flip_view.count() > 0:
            self._auto_timer.start()

    def apply_scale(self, factor):
        if hasattr(self, 'flip_view') and self.flip_view:
            self.flip_view.setBorderRadius(self._scaled_px(8))

class ClassAlbumVerticalComponent(ClassAlbumComponent):
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
        self._notes_dir = DATA_NOTES
        self._notes_file = os.path.join(self._notes_dir, f"{component_data['id']}.json")
        self._color_key = component_data.get("config", {}).get("color", "yellow")
        self._save_timer = QTimer(self)
        self._save_timer.setSingleShot(True)
        self._save_timer.setInterval(500)
        self._save_timer.timeout.connect(self._save_note)
        self._setup_ui()
        try:
            if os.path.exists(self._notes_file):
                with open(self._notes_file, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                    self._editor.setPlainText(data.get("text", ""))
        except Exception as e:
            logger.warning(f"加载便签失败: {e}")
        logger.debug(f"[NOTE] 便签就绪 id={self.component_id} 颜色={self._color_key}")

    def _setup_ui(self):
        self._colors = self.STICKY_COLORS.get(self._color_key, self.STICKY_COLORS["yellow"])

        # 标题栏
        self._header = QWidget()
        self._header.setFixedHeight(self._scaled_px(36))
        header_layout = QHBoxLayout(self._header)
        header_layout.setContentsMargins(12, 0, 12, 0)

        self._color_dot = QLabel()
        ds = self._scaled_px(10)
        self._color_dot.setFixedSize(ds, ds)
        dot_pm = QPixmap(ds, ds)
        dot_pm.fill(QColor(self._colors['header']))
        self._color_dot.setPixmap(dot_pm)

        self._date_label = CaptionLabel(QDate.currentDate().toString("yyyy-MM-dd"))

        header_layout.addWidget(self._color_dot)
        header_layout.addSpacing(6)
        header_layout.addWidget(self._date_label)
        header_layout.addStretch()

        # 编辑区
        self._editor = TextEdit()
        self._editor.setPlaceholderText(tr("sticky_note.placeholder"))
        self._editor.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self._editor.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self._editor.textChanged.connect(self._save_timer.start)
        self._editor.setContextMenuPolicy(Qt.ContextMenuPolicy.DefaultContextMenu)
        self._editor.setTabChangesFocus(False)

        layout = self.inner_layout
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        layout.addWidget(self._header)
        layout.addWidget(self._editor, 1)

        self.setMinimumSize(120, 100)
        self._size_explicitly_set = True
        self.resize(280, 280)
        self._apply_style()

    def _save_note(self):
        text = self._editor.toPlainText()
        try:
            if not os.path.exists(self._notes_dir):
                os.makedirs(self._notes_dir, exist_ok=True)
            with open(self._notes_file, 'w', encoding='utf-8') as f:
                json.dump({"text": text, "color": self._color_key}, f, ensure_ascii=False)
            logger.debug(f"[Note] 便签已保存: {len(text)} 字 颜色={self._color_key}")
        except Exception as e:
            logger.warning(f"保存便签失败: {e}")

    def apply_scale(self, factor):
        self._header.setFixedHeight(self._scaled_px(36))
        ds = self._scaled_px(10)
        self._color_dot.setFixedSize(ds, ds)
        dot_pm = QPixmap(ds, ds)
        dot_pm.fill(QColor(self._colors['header']))
        self._color_dot.setPixmap(dot_pm)
        self._apply_style()

    def _apply_style(self):
        colors = self._colors
        sz_date = self._scaled_px(11)
        sz_editor = self._scaled_px(13)
        pad_ed_top = self._scaled_px(4)
        pad_ed_sides = self._scaled_px(12)
        dot_radius = self._scaled_px(5)
        bg_css = self._card_bg_css(bg_mode="custom", bg_color=colors["bg"])
        self.setStyleSheet(f"""
            {bg_css}
            #stickyNoteContainer {{
                border: 1px solid {colors['header']};
            }}
        """)
        self._color_dot.setStyleSheet(f"border-radius: {dot_radius}px;")
        self._date_label.setStyleSheet(f"color: {colors['text']}; font-size: {sz_date}px; font-family: {FONT_FAMILY}; background: transparent;")
        self._editor.setStyleSheet(f"""
            TextEdit {{
                background-color: transparent;
                border: none;
                color: {colors['text']};
                font-size: {sz_editor}px;
                font-family: {FONT_FAMILY};
                padding: {pad_ed_top}px {pad_ed_sides}px {pad_ed_sides}px {pad_ed_sides}px;
                selection-background-color: {colors['header']};
            }}
            TextEdit:focus {{
                outline: none;
            }}
        """)

class HomeworkBoardComponent(_HtmlCardComponent):
    """作业板"""
    _min_size = (320, 200)
    _default_size = (430, 236)

    _object_name = "homeworkBoardContainer"

    _PALETTE = ["#f59e0b", "#3b82f6", "#10b981", "#ef4444", "#8b5cf6", "#ec4899", "#06b6d4", "#84cc16"]

    _theme_light = {
        "ink": "rgba(0,0,0,0.89)", "sub": "rgba(0,0,0,0.62)", "line": "rgba(0,0,0,0.08)",
        "hover": "rgba(0,0,0,0.045)", "chip": "rgba(0,0,0,0.05)",
        "bar": "rgba(0,0,0,0.12)", "empty": "rgba(0,0,0,0.45)",
        "card": "rgba(255,255,255,0.55)", "cardline": "rgba(0,0,0,0.06)",
    }
    _theme_dark = {
        "ink": "rgba(255,255,255,0.95)", "sub": "rgba(255,255,255,0.62)", "line": "rgba(255,255,255,0.10)",
        "hover": "rgba(255,255,255,0.06)", "chip": "rgba(255,255,255,0.08)",
        "bar": "rgba(255,255,255,0.14)", "empty": "rgba(255,255,255,0.40)",
        "card": "rgba(255,255,255,0.055)", "cardline": "rgba(255,255,255,0.09)",
    }

    _HTML_TEMPLATE = Template('''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { width: 100%; height: 100%; background: transparent; overflow: hidden; }
  body { font-family: $font; color: $ink; user-select: none; }
  #app { display: flex; flex-direction: column; width: 100%; height: 100%; padding: 2px 12px 8px; }
  #topbar { display: flex; align-items: center; gap: 8px; padding: 3px 2px 8px; flex: none; cursor: grab; }
  body.hw-drag, body.hw-drag #topbar { cursor: grabbing; }
  #prog-wrap { display: flex; align-items: center; gap: 8px; min-width: 0; }
  #prog-text { font-size: 12px; font-weight: 600; color: $sub; white-space: nowrap; font-variant-numeric: tabular-nums; }
  #prog-track { width: 72px; height: 3px; border-radius: 2px; background: $bar; overflow: hidden; flex: none; }
  #prog-fill { height: 100%; width: 0; border-radius: 2px; background: $accent; transition: width .3s cubic-bezier(.16,1,.3,1); }
  .act {
    border: none; cursor: pointer; font-family: $font; font-size: 11.5px; font-weight: 600;
    color: $sub; background: $chip; padding: 3px 10px; border-radius: 4px;
    transition: background .15s, color .15s; flex: none;
  }
  .act:hover { background: $accent22; color: $accent; }
  .act:active { opacity: .8; }
  .act.danger:hover { background: rgba(229,72,77,.12); color: #e5484d; }
  #cols { flex: 1; overflow-y: auto; overflow-x: hidden; column-count: 2; column-gap: 10px; padding-right: 3px; }
  #cols::-webkit-scrollbar { width: 4px; }
  #cols::-webkit-scrollbar-thumb { background: $bar; border-radius: 2px; }
  #empty {
    height: 100%; display: flex; align-items: center; justify-content: center;
    color: $empty; font-size: 12px;
  }
  .sec {
    break-inside: avoid; background: $card; border: 1px solid $cardline; border-radius: 8px;
    padding: 8px 9px 7px; margin-bottom: 10px;
  }
  .sec-head { display: flex; align-items: center; gap: 7px; padding: 0 1px 5px; position: relative; }
  .sec-dot { width: 6px; height: 6px; border-radius: 2px; flex: none; }
  .sec-title { font-size: 12.5px; font-weight: 600; cursor: text; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .sec-count { font-size: 10.5px; color: $sub; font-variant-numeric: tabular-nums; flex: none; }
  .sec-del {
    margin-left: auto; border: none; cursor: pointer; background: transparent; color: $sub;
    font-size: 11px; font-weight: 600; padding: 1px 7px; border-radius: 4px; line-height: 1.5;
    opacity: 0; transition: opacity .15s, background .15s, color .15s; font-family: $font; flex: none;
  }
  .sec-head:hover .sec-del { opacity: 1; }
  .sec-del:hover { background: rgba(229,72,77,.12); color: #e5484d; }
  .sec-del.confirm { opacity: 1; background: rgba(229,72,77,.9); color: #fff; }
  .item { display: flex; align-items: center; gap: 8px; padding: 3px 5px 3px 3px; border-radius: 5px; position: relative; }
  .item:hover { background: $hover; }
  .circle { width: 16px; height: 16px; flex: none; cursor: pointer; position: relative; }
  .circle svg { width: 100%; height: 100%; display: block; }
  .circle .ring { fill: transparent; stroke: $dotbd; stroke-width: 1.4; transition: fill .18s, stroke .18s; }
  .circle:hover .ring { stroke: $accent; }
  .circle .tick { fill: none; stroke: #fff; stroke-width: 1.9; stroke-linecap: round; stroke-linejoin: round;
    stroke-dasharray: 20; stroke-dashoffset: 20; transition: stroke-dashoffset .25s cubic-bezier(.3,.8,.4,1) .04s; }
  .done .circle .ring { fill: $accent; stroke: $accent; }
  .done .circle .tick { stroke-dashoffset: 0; }
  .text { font-size: 12px; font-weight: 400; line-height: 1.45; position: relative; min-width: 0; flex: 1;
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: text; transition: color .2s, opacity .2s; }
  .text::after { content: ''; position: absolute; left: 0; top: 52%; height: 1.5px; width: 0;
    background: $sub; border-radius: 1px; transition: width .25s cubic-bezier(.3,.8,.4,1); }
  .done .text { color: $sub; opacity: .6; }
  .done .text::after { width: 100%; }
  .item-btns { display: flex; gap: 2px; opacity: 0; transition: opacity .15s; flex: none; }
  .item:hover .item-btns { opacity: 1; }
  .ibtn { border: none; cursor: pointer; background: transparent; color: $sub; width: 18px; height: 18px;
    border-radius: 4px; font-size: 11px; line-height: 18px; text-align: center; padding: 0; font-family: $font; transition: background .15s, color .15s; }
  .ibtn:hover { background: $chip; color: $accent; }
  .ibtn.del:hover { background: rgba(229,72,77,.12); color: #e5484d; }
  .add-row { display: flex; align-items: center; gap: 7px; padding: 3px 3px; cursor: pointer; border-radius: 5px; }
  .add-row:hover { background: $hover; }
  .add-row .plus { width: 15px; height: 15px; flex: none; position: relative; opacity: .55; }
  .add-row .plus::before, .add-row .plus::after { content: ''; position: absolute; background: $sub; border-radius: 1px; transition: background .15s; }
  .add-row .plus::before { left: 7px; top: 2px; width: 1.5px; height: 11px; }
  .add-row .plus::after { left: 2px; top: 6.2px; width: 11px; height: 1.5px; }
  .add-row .add-label { font-size: 11.5px; color: $sub; transition: color .15s; }
  .add-row:hover .plus::before, .add-row:hover .plus::after, .add-row:hover .add-label { background: $accent; color: $accent; opacity: 1; }
  .inline-input {
    flex: 1; min-width: 0; border: none; outline: none; background: $chip; font-family: $font;
    font-size: 12px; color: $ink; padding: 2.5px 7px; border-radius: 5px;
    box-shadow: inset 0 -1.5px 0 $accent;
  }
  .inline-input::placeholder { color: $empty; }
  .sec-input { font-size: 12.5px; font-weight: 600; }
</style>
</head>
<body>
<div id="app">
  <div id="topbar">
    <div id="prog-wrap">
      <span id="prog-text"></span>
      <div id="prog-track"><div id="prog-fill"></div></div>
    </div>
    <button class="act" id="btn-add-sub"></button>
    <button class="act danger" id="btn-clear" style="display:none"></button>
  </div>
  <div id="cols"></div>
</div>
<script src="qrc:///qtwebchannel/qwebchannel.js"></script>
<script>
var DATA = /*DATA*/[];
var T = { addSub: "$add_sub", clear: "$clear_done", item: "$item_ph", subject: "$subject_ph",
          empty: "$empty_hint", confirm: "$confirm_del" };
var PALETTE = $palette;
var bridge = null, pending = null;

var app = document.getElementById('app');
var cols = document.getElementById('cols');
var progText = document.getElementById('prog-text');
var progFill = document.getElementById('prog-fill');
var btnAddSub = document.getElementById('btn-add-sub');
var btnClear = document.getElementById('btn-clear');

btnAddSub.textContent = '+ ' + T.addSub;
btnClear.textContent = T.clear;
btnAddSub.onclick = function () { data().sections.push({ title: '', items: [] }); addSectionInline(data().sections.length - 1, true); };
btnClear.onclick = function () {
  var ch = false;
  data().sections.forEach(function (s) {
    var before = s.items.length;
    s.items = s.items.filter(function (it) { return !it.done; });
    if (s.items.length !== before) ch = true;
  });
  if (ch) { render(); commit(); }
};

function data() { return DATA; }
function esc(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }

function commit() {
  pending = JSON.stringify(DATA);
  if (bridge) { bridge.commit(pending); pending = null; }
}

function updateProgress() {
  var total = 0, done = 0;
  DATA.sections.forEach(function (s) { total += s.items.length; s.items.forEach(function (it) { if (it.done) done++; }); });
  progText.textContent = done + '/' + total;
  progFill.style.width = total ? (done * 100 / total) + '%' : '0';
  btnClear.style.display = done ? '' : 'none';
  document.getElementById('empty').style.display = DATA.sections.length ? 'none' : 'flex';
}

function svgCircle() {
  return '<svg viewBox="0 0 20 20"><rect class="ring" x="2.7" y="2.7" width="14.6" height="14.6" rx="4.4"/><path class="tick" d="M6.3 10.4 l2.5 2.5 L13.8 7.6"/></svg>';
}

function render() {
  cols.innerHTML = '';
  var empty = document.createElement('div');
  empty.id = 'empty'; empty.textContent = T.empty;
  cols.appendChild(empty);
  DATA.sections.forEach(function (sec, si) {
    var color = PALETTE[si % PALETTE.length];
    var box = document.createElement('div');
    box.className = 'sec'; box.style.columnBreakInside = 'avoid';
    var head = document.createElement('div');
    head.className = 'sec-head';
    var dot = document.createElement('span');
    dot.className = 'sec-dot'; dot.style.background = color;
    dot.style.boxShadow = '0 0 0 3px ' + color + '22';
    var title = document.createElement('span');
    title.className = 'sec-title'; title.textContent = sec.title;
    title.ondblclick = function () { editSectionTitle(si, title); };
    var count = document.createElement('span');
    count.className = 'sec-count';
    var del = document.createElement('button');
    del.className = 'sec-del'; del.textContent = '×';
    del.onclick = function () {
      if (del.classList.contains('confirm')) {
        DATA.sections.splice(si, 1); render(); commit();
      } else {
        del.classList.add('confirm'); del.textContent = T.confirm;
        setTimeout(function () { del.classList.remove('confirm'); del.textContent = '×'; }, 2500);
      }
    };
    head.appendChild(dot); head.appendChild(title); head.appendChild(count); head.appendChild(del);
    box.appendChild(head);
    sec.items.forEach(function (it, ii) { box.appendChild(itemRow(si, ii, it, color)); });
    box.appendChild(addRowSection(si));
    cols.appendChild(box);
  });
  updateProgress();
  refreshCounts();
}

function refreshCounts() {
  var secs = cols.querySelectorAll('.sec');
  DATA.sections.forEach(function (s, i) {
    if (!secs[i]) return;
    var done = 0; s.items.forEach(function (it) { if (it.done) done++; });
    var c = secs[i].querySelector('.sec-count');
    if (c) c.textContent = s.items.length ? done + '/' + s.items.length : '';
  });
}

function itemRow(si, ii, it, color) {
  var row = document.createElement('div');
  row.className = 'item' + (it.done ? ' done' : '');
  var circle = document.createElement('span');
  circle.className = 'circle'; circle.innerHTML = svgCircle();
  circle.onclick = function () { it.done = !it.done; row.classList.toggle('done', it.done); refreshCounts(); updateProgress(); commit(); };
  var text = document.createElement('span');
  text.className = 'text'; text.textContent = it.text;
  text.ondblclick = function () { editItem(si, ii, row, text); };
  var btns = document.createElement('span');
  btns.className = 'item-btns';
  var be = document.createElement('button');
  be.className = 'ibtn'; be.textContent = '✎';
  be.onclick = function () { editItem(si, ii, row, text); };
  var bd = document.createElement('button');
  bd.className = 'ibtn del'; bd.textContent = '×';
  bd.onclick = function () { DATA.sections[si].items.splice(ii, 1); render(); commit(); };
  btns.appendChild(be); btns.appendChild(bd);
  row.appendChild(circle); row.appendChild(text); row.appendChild(btns);
  return row;
}

function addRowSection(si) {
  var row = document.createElement('div');
  row.className = 'add-row';
  var plus = document.createElement('span'); plus.className = 'plus';
  var label = document.createElement('span'); label.className = 'add-label'; label.textContent = T.item;
  row.appendChild(plus); row.appendChild(label);
  row.onclick = function (e) { e.stopPropagation(); startAddItem(si, row); };
  return row;
}

function startAddItem(si, row) {
  row.onclick = null;
  row.innerHTML = '';
  var input = document.createElement('input');
  input.className = 'inline-input'; input.placeholder = T.item;
  row.appendChild(input);
  input.focus();
  var finish = function (save) {
    var v = input.value.trim();
    if (save && v) { DATA.sections[si].items.push({ text: v, done: false }); render(); commit(); }
    else render();
  };
  input.onkeydown = function (e) {
    if (e.key === 'Enter') finish(true);
    else if (e.key === 'Escape') finish(false);
  };
  input.onblur = function () { finish(true); };
}

function editItem(si, ii, row, text) {
  var it = DATA.sections[si].items[ii];
  var input = document.createElement('input');
  input.className = 'inline-input'; input.value = it.text;
  text.replaceWith(input);
  input.focus(); input.select();
  var finish = function (save) {
    var v = input.value.trim();
    if (save && v && v !== it.text) { it.text = v; commit(); }
    render();
  };
  input.onkeydown = function (e) {
    if (e.key === 'Enter') finish(true);
    else if (e.key === 'Escape') finish(false);
  };
  input.onblur = function () { finish(true); };
}

function addSectionInline(si, fresh) {
  render();
  var secs = cols.querySelectorAll('.sec');
  var box = secs[si]; if (!box) return;
  var head = box.querySelector('.sec-head');
  var input = document.createElement('input');
  input.className = 'inline-input sec-input'; input.placeholder = T.subject;
  head.innerHTML = '';
  head.appendChild(input);
  input.focus();
  var finish = function (save) {
    var v = input.value.trim();
    if (save && v) { DATA.sections[si].title = v; commit(); }
    else if (fresh) { DATA.sections.splice(si, 1); }
    render();
  };
  input.onkeydown = function (e) {
    if (e.key === 'Enter') finish(true);
    else if (e.key === 'Escape') finish(false);
  };
  input.onblur = function () { finish(true); };
}

function editSectionTitle(si, title) {
  var si2 = si;
  var input = document.createElement('input');
  input.className = 'inline-input sec-input'; input.value = DATA.sections[si2].title;
  title.replaceWith(input);
  input.focus(); input.select();
  var finish = function (save) {
    var v = input.value.trim();
    if (save && v && v !== DATA.sections[si2].title) { DATA.sections[si2].title = v; commit(); }
    render();
  };
  input.onkeydown = function (e) {
    if (e.key === 'Enter') finish(true);
    else if (e.key === 'Escape') finish(false);
  };
  input.onblur = function () { finish(true); };
}

var topbarEl = document.getElementById('topbar');
var hwDrag = false;
function hwSend(kind, x, y) {
  if (!bridge) return;
  if (kind === 0) bridge.drag_start(x, y);
  else if (kind === 1) bridge.drag_move(x, y);
  else bridge.drag_end();
}
topbarEl.addEventListener('mousedown', function (e) {
  if (e.button !== 0 || (e.target.closest && e.target.closest('.act'))) return;
  hwDrag = true; document.body.classList.add('hw-drag');
  hwSend(0, e.clientX, e.clientY); e.preventDefault();
});
window.addEventListener('mousemove', function (e) {
  if (hwDrag) hwSend(1, e.clientX, e.clientY);
});
window.addEventListener('mouseup', function () {
  if (hwDrag) { hwDrag = false; document.body.classList.remove('hw-drag'); hwSend(2, 0, 0); }
});

if (typeof QWebChannel !== 'undefined' && typeof qt !== 'undefined') {
  new QWebChannel(qt.webChannelTransport, function (channel) {
    bridge = channel.objects.bridge;
    if (pending) { bridge.commit(pending); pending = null; }
  });
}
render();
</script>
</body>
</html>''')

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName(self._object_name)
        self._data_file = os.path.join(DATA_USER, f"homework_{component_data['id']}.json")
        self._sections = self._load()
        self._save_timer = QTimer(self)
        self._save_timer.setSingleShot(True)
        self._save_timer.setInterval(400)
        self._save_timer.timeout.connect(self._save)
        self._bridge = _WebBridge(
            lambda phase, x, y: self._relay_web_drag(phase, x, y, self.webView),
            self._on_commit, self)
        self._setup_ui()
        logger.debug(f"[HW] 作业板组件初始化 id={self.component_id} 分区={len(self._sections)}")

    # ui

    def _build_html(self) -> str:
        theme = dict(_theme_pair(self._theme_dark, self._theme_light))
        theme["accent"] = _accent_css()
        theme["accent22"] = _accent_css(0.13)
        theme["dotbd"] = _theme_pair("rgba(255,255,255,0.35)", "rgba(0,0,0,0.28)")
        data_json = json.dumps({"sections": self._sections}, ensure_ascii=False).replace("</", "<\\/")
        html = self._HTML_TEMPLATE.substitute(
            font=FONT_FAMILY,
            add_sub=tr("homework.add_subject"),
            clear_done=tr("homework.clear_done"),
            item_ph=tr("homework.add_item"),
            subject_ph=tr("homework.subject_ph"),
            empty_hint=tr("homework.empty"),
            confirm_del=tr("homework.confirm_del"),
            palette=json.dumps(self._PALETTE),
            **theme,
        ).replace("/*DATA*/[]", data_json)
        logger.debug(f"[HW] 生成作业板 html 长度 {len(html)} 科目{len(self._sections)}个")
        return html

    # 数据
    def _load(self) -> list:
        try:
            if os.path.exists(self._data_file):
                with open(self._data_file, "r", encoding="utf-8") as f:
                    data = json.load(f)
                sections = data.get("sections")
                if isinstance(sections, list):
                    logger.debug(f"[HW] 作业板读取: {len(sections)}个科目 ({os.path.basename(self._data_file)})")
                    return sections
        except Exception as e:
            logger.warning(f"读取作业板失败: {e}")
        return []

    def _on_commit(self, sections_json: str):
        """校验 > 写配置 > 保存

        Args:
            sections_json: 网页回传json
        """
        try:
            sections = json.loads(sections_json)
            if not isinstance(sections, list):
                return
            clean = []
            for sec in sections:
                if not isinstance(sec, dict):
                    continue
                title = str(sec.get("title", "")).strip()[:30]
                items = []
                for it in sec.get("items", []):
                    if not isinstance(it, dict):
                        continue
                    text = str(it.get("text", "")).strip()[:120]
                    if text:
                        items.append({"text": text, "done": bool(it.get("done"))})
                if title or items:
                    clean.append({"title": title, "items": items})
            self._sections = clean
            logger.debug(f"[HW] 作业板数据提交: {len(clean)}个科目")
            self._save_timer.start()
        except Exception as e:
            logger.warning(f"作业板数据无效: {e}")

    def _save(self):
        try:
            os.makedirs(DATA_USER, exist_ok=True)
            with open(self._data_file, "w", encoding="utf-8") as f:
                json.dump({"sections": self._sections}, f, ensure_ascii=False, indent=1)
            logger.debug(f"[HW] 作业板已保存: {len(self._sections)}个科目")
        except Exception as e:
            logger.warning(f"保存作业板失败: {e}")

class TimeColumnWidget(QWidget):
    """计时器时间输入列"""
    valueChanged = pyqtSignal(int)

    def __init__(self, label: str, min_val: int = 0, max_val: int = 59, default: int = 0, parent=None):
        super().__init__(parent)
        self._min_val = min_val
        self._max_val = max_val
        self._value = max(min_val, min(max_val, default))
        self.setFixedWidth(80)
        self._setup_ui(label)

    def _setup_ui(self, label: str):
        lay = QVBoxLayout(self)
        lay.setAlignment(Qt.AlignmentFlag.AlignCenter)
        lay.setSpacing(2)
        lay.setContentsMargins(2, 2, 2, 2)

        self._label_widget = BodyLabel(label, self)
        self._label_widget.setAlignment(Qt.AlignmentFlag.AlignCenter)
        font = QFont()
        font.setPointSize(12)
        self._label_widget.setFont(font)
        lay.addWidget(self._label_widget)

        self._up_btn = ToolButton(FUI.CHEVRON_UP, self)
        self._up_btn.setFixedSize(40, 28)
        self._up_btn.clicked.connect(lambda: self.set_value(self._value + 1))
        lay.addWidget(self._up_btn, 0, Qt.AlignmentFlag.AlignCenter)

        self._value_label = StrongBodyLabel(f"{self._value:02d}", self)
        self._value_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        font = QFont()
        font.setPointSize(28)
        font.setWeight(QFont.Weight.Bold)
        self._value_label.setFont(font)
        self._value_label.setFixedHeight(44)
        lay.addWidget(self._value_label, 0, Qt.AlignmentFlag.AlignCenter)

        self._down_btn = ToolButton(FUI.CHEVRON_DOWN, self)
        self._down_btn.setFixedSize(40, 28)
        self._down_btn.clicked.connect(lambda: self.set_value(self._value - 1))
        lay.addWidget(self._down_btn, 0, Qt.AlignmentFlag.AlignCenter)

        self.setSizePolicy(QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Fixed)

    def set_value(self, val: int):
        old = self._value
        self._value = max(self._min_val, min(self._max_val, val))
        if self._value != old:
            logger.debug(f"[TC] 时间列 '{self._label_widget.text()}' 值变更: {old} -> {self._value}")
            self._value_label.setText(f"{self._value:02d}")
            self.valueChanged.emit(self._value)

    def value(self) -> int:
        return self._value

    def wheelEvent(self, event):
        if event.angleDelta().y() > 0:
            self.set_value(self._value + 1)
        else:
            self.set_value(self._value - 1)
        super().wheelEvent(event)

    def apply_scale(self, factor: float):
        self.setFixedWidth(max(1, int(80 * factor)))
        for btn in (self._up_btn, self._down_btn):
            btn.setFixedSize(max(1, int(40 * factor)), max(1, int(28 * factor)))
        self._value_label.setFixedHeight(max(1, int(44 * factor)))
        f = QFont()
        f.setPointSizeF(max(1.0, 12 * factor))
        self._label_widget.setFont(f)
        f = QFont()
        f.setPointSizeF(max(1.0, 28 * factor))
        f.setWeight(QFont.Weight.Bold)
        self._value_label.setFont(f)

class TimerTimeDisplayWidget(QWidget):
    """计时器倒计时数字显示"""
    def __init__(self, parent=None):
        super().__init__(parent)
        lay = QHBoxLayout(self)
        lay.setAlignment(Qt.AlignmentFlag.AlignCenter)
        lay.setContentsMargins(0, 0, 0, 0)
        self._label = StrongBodyLabel("00:00:00", self)
        font = QFont()
        font.setPointSize(48)
        font.setWeight(QFont.Weight.Bold)
        self._label.setFont(font)
        self._label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        lay.addWidget(self._label)

    def set_time(self, h: int, m: int, s: int):
        self._label.setText(f"{h:02d}:{m:02d}:{s:02d}")

    def apply_scale(self, factor: float):
        f = QFont()
        f.setPointSizeF(max(1.0, 48 * factor))
        f.setWeight(QFont.Weight.Bold)
        self._label.setFont(f)

class TimerCountdownComponent(DraggableContainer):
    """计时器组件"""

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName("timerCountdownContainer")
        self._running = False
        self._paused = False
        self._elapsed_seconds = 0
        self._remaining_seconds = 0
        self._is_countdown = False
        self._timer = QTimer(self)
        self._timer.setInterval(1000)
        self._timer.timeout.connect(self._on_tick)
        self._setup_ui()
        logger.debug(f"[TC] 计时器组件初始化 id={self.component_id}")

    def _setup_ui(self):
        self._pivot = Pivot(self)
        self._pivot.setFixedHeight(self._scaled_px(32))
        self._pivot.addItem("timer", tr("timer_countdown.timer"), onClick=lambda: self._switch_mode(False))
        self._pivot.addItem("countdown", tr("timer_countdown.countdown"), onClick=lambda: self._switch_mode(True))

        self._mode_stack = QStackedWidget(self)

        # 计时页
        self._timer_page = QWidget()
        tp = QVBoxLayout(self._timer_page)
        tp.setContentsMargins(0, 12, 0, 0)
        tp.setSpacing(4)

        self._timer_display = TimerTimeDisplayWidget(self._timer_page)
        tp.addWidget(self._timer_display, 0, Qt.AlignmentFlag.AlignHCenter)

        self._timer_btn_stack = QStackedWidget(self._timer_page)
        self._timer_btn_stack.setFixedHeight(self._scaled_px(40))

        self._ts_start = PrimaryPushButton(FUI.PLAY, tr("timer_countdown.start"))
        self._ts_start.setFixedSize(self._scaled_px(120), self._scaled_px(32))
        self._ts_start.clicked.connect(self._on_timer_start)
        self._timer_btn_stack.addWidget(self._ts_start)

        tr_w = QWidget()
        tr_l = QHBoxLayout(tr_w)
        tr_l.setContentsMargins(0, 0, 0, 0)
        tr_l.setAlignment(Qt.AlignmentFlag.AlignCenter)
        tr_l.setSpacing(12)
        self._ts_pause = PushButton(tr("timer_countdown.pause"))
        self._ts_pause.setFixedSize(self._scaled_px(80), self._scaled_px(30))
        self._ts_pause.clicked.connect(lambda: self._toggle_pause(self._ts_pause))
        self._ts_cancel = PushButton(tr("timer_countdown.cancel"))
        self._ts_cancel.setFixedSize(self._scaled_px(80), self._scaled_px(30))
        self._ts_cancel.clicked.connect(self._on_timer_cancel)
        tr_l.addWidget(self._ts_pause)
        tr_l.addWidget(self._ts_cancel)
        self._timer_btn_stack.addWidget(tr_w)

        tp.addWidget(self._timer_btn_stack, 0, Qt.AlignmentFlag.AlignHCenter)
        tp.addStretch(1)
        self._mode_stack.addWidget(self._timer_page)

        # 倒计时页
        self._cd_page = QWidget()
        cp = QVBoxLayout(self._cd_page)
        cp.setContentsMargins(0, 12, 0, 0)
        cp.setSpacing(4)

        self._cd_content_stack = QStackedWidget(self._cd_page)

        # 设置：三列 开始
        cd_setup = QWidget()
        cds = QVBoxLayout(cd_setup)
        cds.setContentsMargins(0, 0, 0, 0)
        cds.setAlignment(Qt.AlignmentFlag.AlignCenter)
        cds.setSpacing(8)
        cols = QHBoxLayout()
        cols.setAlignment(Qt.AlignmentFlag.AlignCenter)
        cols.setSpacing(8)
        self._hh_col = TimeColumnWidget(tr("timer_countdown.hours"), 0, 99, 0)
        self._mm_col = TimeColumnWidget(tr("timer_countdown.minutes"), 0, 59, 0)
        self._ss_col = TimeColumnWidget(tr("timer_countdown.seconds"), 0, 59, 0)
        cols.addWidget(self._hh_col)
        cols.addWidget(self._mm_col)
        cols.addWidget(self._ss_col)
        cds.addLayout(cols)
        self._cd_start = PrimaryPushButton(FUI.PLAY, tr("timer_countdown.start"))
        self._cd_start.setFixedSize(self._scaled_px(120), self._scaled_px(32))
        self._cd_start.clicked.connect(self._on_countdown_start)
        cds.addWidget(self._cd_start, 0, Qt.AlignmentFlag.AlignCenter)
        self._cd_content_stack.addWidget(cd_setup)

        # 运行：时间 暂停/取消
        cd_run = QWidget()
        cdr = QVBoxLayout(cd_run)
        cdr.setContentsMargins(0, 0, 0, 0)
        cdr.setAlignment(Qt.AlignmentFlag.AlignCenter)
        cdr.setSpacing(8)
        self._cd_display = TimerTimeDisplayWidget(cd_run)
        cdr.addWidget(self._cd_display, 0, Qt.AlignmentFlag.AlignCenter)
        cdr_btns = QHBoxLayout()
        cdr_btns.setAlignment(Qt.AlignmentFlag.AlignCenter)
        cdr_btns.setSpacing(12)
        self._cd_pause = PushButton(tr("timer_countdown.pause"))
        self._cd_pause.setFixedSize(self._scaled_px(80), self._scaled_px(30))
        self._cd_pause.clicked.connect(lambda: self._toggle_pause(self._cd_pause))
        self._cd_cancel = PushButton(tr("timer_countdown.cancel"))
        self._cd_cancel.setFixedSize(self._scaled_px(80), self._scaled_px(30))
        self._cd_cancel.clicked.connect(self._on_countdown_cancel)
        cdr_btns.addWidget(self._cd_pause)
        cdr_btns.addWidget(self._cd_cancel)
        cdr.addLayout(cdr_btns)
        self._cd_content_stack.addWidget(cd_run)

        cp.addWidget(self._cd_content_stack, 0, Qt.AlignmentFlag.AlignHCenter)
        cp.addStretch(1)
        self._mode_stack.addWidget(self._cd_page)

        ml = self.inner_layout
        ml.setContentsMargins(0, 0, 0, 0)
        ml.setSpacing(0)
        ml.addWidget(self._pivot)
        ml.addWidget(self._mode_stack, 1)

        self.setMinimumSize(130, 100)
        self._size_explicitly_set = True
        self.resize(220, 170)
        self._apply_card_style()
        self._switch_mode(False)

    def _reset_state(self):
        logger.debug("[TC] 计时器状态重置")
        self._timer.stop()
        self._running = False
        self._paused = False
        self._elapsed_seconds = 0
        self._remaining_seconds = 0

    def _switch_mode(self, countdown: bool):
        """只有停止时能切 切后重置

        Args:
            countdown: True 切倒计时,False 切正计时
        """
        if self._running:
            self._reset_state()
        _changed = getattr(self, "_is_countdown", None) != countdown
        self._is_countdown = countdown
        if _changed:
            logger.debug(f"[TC] 模式切换 -> {'倒计时' if countdown else '正计时'}")
        self._pivot.setCurrentItem("countdown" if countdown else "timer")
        self._mode_stack.setCurrentIndex(1 if countdown else 0)
        if countdown:
            self._cd_content_stack.setCurrentIndex(0)
            self._cd_display.set_time(0, 0, 0)
        else:
            self._timer_btn_stack.setCurrentIndex(0)
            self._timer_display.set_time(0, 0, 0)
        if _changed:
            logger.debug(f"[TIMER] 计时模式切换: {'倒计时' if countdown else '正计时'}")

    def _on_timer_start(self):
        """清零计时"""
        logger.debug("[TIMER] 正计时启动")
        self._elapsed_seconds = 0
        self._timer_btn_stack.setCurrentIndex(1)
        self._ts_pause.setText(tr("timer_countdown.pause"))
        self._running = True
        self._paused = False
        self._timer.start()

    def _toggle_pause(self, btn):
        self._paused = not self._paused
        logger.debug(f"[TIMER] {'暂停' if self._paused else '恢复'} ({'倒计时' if self._is_countdown else '正计时'})")
        btn.setText(tr("timer_countdown.resume" if self._paused else "timer_countdown.pause"))

    def _on_timer_cancel(self):
        """复位回输入态"""
        logger.debug(f"[TIMER] 正计时复位: 已计 {self._elapsed_seconds}s")
        self._reset_state()
        self._timer_btn_stack.setCurrentIndex(0)
        self._timer_display.set_time(0, 0, 0)

    def _on_countdown_start(self):
        """起跑"""
        total = self._hh_col.value() * 3600 + self._mm_col.value() * 60 + self._ss_col.value()
        if total <= 0:
            InfoBar.warning(title=tr("common.info"), content=tr("timer_countdown.set_time_hint"), parent=self, duration=2000)
            return
        logger.debug(f"[TIMER] 启动 {total}s ({self._hh_col.value()}h{self._mm_col.value()}m{self._ss_col.value()}s)")
        self._remaining_seconds = total
        self._cd_pause.setText(tr("timer_countdown.pause"))
        self._cd_content_stack.setCurrentIndex(1)
        self._update_display()
        self._running = True
        self._paused = False
        self._timer.start()

    def _on_countdown_cancel(self):
        """回输入态"""
        logger.debug(f"[TIMER] 倒计时取消: 剩余 {self._remaining_seconds}s")
        self._reset_state()
        self._cd_content_stack.setCurrentIndex(0)
        self._cd_display.set_time(0, 0, 0)

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
                logger.info(f"[TIMER] 结束 ({self._hh_col.value()}h{self._mm_col.value()}m{self._ss_col.value()}s)")
                InfoBar.success(title=tr("timer_countdown.finished_title"), content=tr("timer_countdown.finished_body"), parent=self, duration=3000)
                self._cd_content_stack.setCurrentIndex(0)
                self._cd_display.set_time(0, 0, 0)
                return
        else:
            self._elapsed_seconds += 1
        self._update_display()

    def _update_display(self):
        secs = self._remaining_seconds if self._is_countdown else self._elapsed_seconds
        h, m, s = secs // 3600, (secs % 3600) // 60, secs % 60
        display = self._cd_display if self._is_countdown else self._timer_display
        display.set_time(h, m, s)

    def apply_scale(self, factor):
        self._pivot.setFixedHeight(self._scaled_px(32))
        self._timer_btn_stack.setFixedHeight(self._scaled_px(40))
        for btn in (self._ts_start, self._cd_start):
            btn.setFixedSize(self._scaled_px(120), self._scaled_px(32))
        for btn in (self._ts_pause, self._ts_cancel, self._cd_pause, self._cd_cancel):
            btn.setFixedSize(self._scaled_px(80), self._scaled_px(30))
        for col in (self._hh_col, self._mm_col, self._ss_col):
            col.apply_scale(factor)
        self._timer_display.apply_scale(factor)
        self._cd_display.apply_scale(factor)


class HistoryTodayComponent(DraggableContainer):
    """历史上的今天"""

    _object_name = "historyTodayContainer"
    _item_count = 5
    _header_icon = FUI.HISTORY
    _year_color = "#30c361"
    _title_color_dark = "#ffffff"
    _title_color_light = "#1a1a1a"
    _date_color_dark = "rgba(255, 255, 255, 0.6)"
    _date_color_light = "rgba(40, 40, 40, 0.7)"

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName(self._object_name)
        self._event_links = [""] * self._item_count
        self._event_titles = ["--"] * self._item_count
        self._event_years = [""] * self._item_count
        self._date_text = ""
        self._setup_ui()
        self._setup_periodic_refresh()
        logger.debug(f"[HIS] 今日历史组件初始化 id={self.component_id}")

    def _setup_ui(self):
        # 图标
        self.iconLabel = QLabel()
        self.iconLabel.setObjectName("historyHeaderIcon")
        self.iconLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._render_header_icon(self._scaled_px(28))

        # 标题
        self.titleLabel = BodyLabel(tr("history_today.title"))
        self.titleLabel.setObjectName("historyHeaderTitle")
        self.titleLabel.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)

        # 日期
        self.dateLabel = CaptionLabel("")
        self.dateLabel.setObjectName("historyDateLabel")
        self.dateLabel.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)

        header_layout = QHBoxLayout()
        header_layout.setContentsMargins(0, 0, 0, 0)
        header_layout.setSpacing(8)
        header_layout.addWidget(self.iconLabel)
        header_layout.addWidget(self.titleLabel, 1)
        header_layout.addWidget(self.dateLabel, 0)

        # 事件列表
        self.itemWidgets = []
        for i in range(self._item_count):
            item_label = BodyLabel("--")
            item_label.setObjectName("historyItemLabel")
            item_label.setWordWrap(True)
            item_label.setAlignment(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignTop)
            item_label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
            item_label.mousePressEvent = lambda e, idx=i: self._on_item_clicked(idx)
            item_label.setCursor(Qt.CursorShape.PointingHandCursor)
            self.itemWidgets.append(item_label)

        layout = self.inner_layout
        layout.setContentsMargins(16, 16, 16, 16)
        layout.setSpacing(6)
        layout.addLayout(header_layout)
        for widget in self.itemWidgets:
            layout.addWidget(widget, 1)

        self.setMinimumSize(150, 100)
        self._size_explicitly_set = True
        self.resize(360, 240)
        self._apply_style()

    def _render_header_icon(self, size: int):
        dpr = self.devicePixelRatioF()
        try:
            svg_path = self._header_icon.path()
        except Exception as e:
            logger.warning(f"获取图标路径失败 {e}")
            self.iconLabel.clear()
            self.iconLabel.setFixedSize(size, size)
            return
        pm = _render_svg_logo(svg_path, size)
        pm.setDevicePixelRatio(dpr)
        self.iconLabel.setPixmap(pm)
        self.iconLabel.setFixedSize(int(pm.width() / dpr), int(pm.height() / dpr))

    def _refresh(self):
        data = HistoryService.fetch_history_today(use_cache=True)
        if not data:
            data = get_cached_content("history_today", ignore_expiry=True)
            if data:
                logger.debug("[History] 历史上的今日: 接口无数据 用过期缓存")
        self._update_display(data)

    def _update_display(self, data):
        count = self._item_count
        titles = ["--"] * count
        years = [""] * count
        links = [""] * count
        self._date_text = ""

        if isinstance(data, dict):
            self._date_text = data.get("date", "") or ""
            events = data.get("events") or []
            for i in range(count):
                if i < len(events):
                    item = events[i] or {}
                    titles[i] = item.get("title") or "--"
                    years[i] = str(item.get("year", "") or "")
                    links[i] = item.get("link") or ""

        _first = not getattr(self, "_ht_logged_once", False)
        if _first or self._event_titles != titles:
            logger.debug(f"[History] {self._date_text or '未知日期'} 共{sum(1 for t in titles if t != '--')}条 首条={titles[0] if titles[0] != '--' else '-'})")
            self._ht_logged_once = True
        self._event_titles = titles
        self._event_years = years
        self._event_links = links

        self.dateLabel.setText(self._date_text)
        self._render_items()

    def _render_items(self):
        logger.debug(f"[HIST] 渲染历史今天条目: {len(self._event_titles or [])}条")
        sz_year = self._scaled_px(12)
        sz_text = self._scaled_px(14)
        title_color = self._title_color_dark if isDarkTheme() else self._title_color_light
        year_color = self._year_color

        for year, label, text in zip(self._event_years, self.itemWidgets, self._event_titles):
            year_part = ""
            if year:
                year_part = (f"<span style='font-size:{sz_year}px;color:{year_color};"
                             f"font-weight:700;font-family:{FONT_FAMILY};'>{year} · </span>")
            label.setText(
                f"{year_part}"
                f"<span style='font-size:{sz_text}px;color:{title_color};font-family:{FONT_FAMILY};'>{text}</span>"
            )

    def _on_item_clicked(self, index):
        """打开详情链接
        """
        if 0 <= index < len(self._event_links) and self._event_links[index]:
            logger.info(f"[History] 打开历史事件: {self._event_years[index]} {self._event_titles[index]}")
            webbrowser.open(self._event_links[index])

    def apply_scale(self, factor):
        self._render_header_icon(self._scaled_px(28))
        self._apply_style()

    def _apply_style(self):
        self._apply_card_style()
        date_color = self._date_color_dark if isDarkTheme() else self._date_color_light
        title_color = self._title_color_dark if isDarkTheme() else self._title_color_light
        self.titleLabel.setStyleSheet(f"""
            color: {title_color};
            font-size: {self._scaled_px(15)}px;
            font-weight: 600;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)
        self.dateLabel.setStyleSheet(f"""
            color: {date_color};
            font-size: {self._scaled_px(11)}px;
            font-family: {FONT_FAMILY};
            background-color: transparent;
        """)
        self._render_items()
        self.updateSize()

class DailyWordComponent(_HtmlCardComponent):
    """每日单词"""
    _min_size = (200, 110)
    _default_size = (400, 200)

    _object_name = "dailyWordContainer"

    _theme_dark = {
        "accent": "#30c361",
        "accent_bg": "rgba(48, 195, 97, 0.14)",
        "accent_b": "rgba(48, 195, 97, 0.5)",
        "word1": "#f5f5f5",
        "word2": "#9dbdae",
        "zh": "rgba(255, 255, 255, 0.85)",
        "zh_sub": "rgba(255, 255, 255, 0.62)",
        "ex_en": "#e8e8e8",
        "date_c": "rgba(255, 255, 255, 0.45)",
        "sep_c": "rgba(255, 255, 255, 0.25)",
    }
    _theme_light = {
        "accent": "#30c361",
        "accent_bg": "rgba(48, 195, 97, 0.12)",
        "accent_b": "rgba(48, 195, 97, 0.45)",
        "word1": "#1f2937",
        "word2": "#4f7a6b",
        "zh": "rgba(40, 40, 40, 0.88)",
        "zh_sub": "rgba(40, 40, 40, 0.65)",
        "ex_en": "#2d3a33",
        "date_c": "rgba(60, 60, 60, 0.5)",
        "sep_c": "rgba(0, 0, 0, 0.22)",
    }

    # 词性拆行
    _POS_SPLIT_RE = re.compile(r'\s+(?=[a-zA-Z]{1,10}\.\s)')
    _POS_MARK_RE = re.compile(r'^([a-zA-Z]{1,10}\.)\s*(.*)$')

    _HTML_TEMPLATE = Template('''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { width: 100%; height: 100%; background: transparent; overflow: hidden; }
  body {
    font-family: $font;
    display: flex; flex-direction: column; justify-content: center;
    padding: 20px 24px 22px 26px; position: relative;
  }
  .bg-letter {
    position: absolute; top: -20px; right: 2px;
    font-family: Georgia, serif; font-weight: 700; font-size: 150px; line-height: 1;
    background: linear-gradient(160deg, $accent, transparent 85%);
    -webkit-background-clip: text; -webkit-text-fill-color: transparent;
    opacity: .16;
    animation: pop .8s ease-out both;
  }
  .date {
    position: absolute; top: 15px; right: 18px;
    font-family: Consolas, 'Courier New', monospace;
    font-size: 12px; letter-spacing: 2.5px; color: $date_c;
    animation: fade .9s .25s ease-out both;
  }
  .head {
    display: flex; align-items: baseline; gap: 12px; flex-wrap: wrap;
    animation: rise .55s .05s ease-out both;
  }
  .word {
    font-family: Georgia, 'Times New Roman', serif;
    font-weight: 700; font-size: 40px; line-height: 1.1;
    background: linear-gradient(120deg, $word1, $word2);
    -webkit-background-clip: text; -webkit-text-fill-color: transparent;
  }
  .phonetic {
    font-size: 14px; color: $accent; white-space: nowrap;
    border: 1px solid $accent_b; border-radius: 999px;
    padding: 2px 10px;
  }
  .trans {
    margin-top: 12px; display: flex; flex-direction: column; gap: 5px;
    animation: rise .55s .15s ease-out both;
  }
  .line { font-size: 16.5px; color: $zh; line-height: 1.45; }
  .pos {
    display: inline-block; font-size: 12px; color: $accent;
    background: $accent_bg; border-radius: 4px;
    padding: 1px 6px; margin-right: 8px;
    transform: translateY(-1px);
  }
  .sep {
    margin-top: 13px; width: 60%;
    border-top: 1.5px dashed $sep_c;
    animation: grow .6s .25s ease-out both;
  }
  .example { margin-top: 12px; animation: rise .55s .32s ease-out both; }
  .ex-en {
    font-family: Georgia, 'Times New Roman', serif; font-style: italic;
    font-size: 18px; line-height: 1.4; color: $ex_en;
  }
  .ex-zh { margin-top: 4px; font-size: 15px; color: $zh_sub; }
  @keyframes rise { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: none; } }
  @keyframes pop  { from { opacity: 0; transform: scale(.6); } to { opacity: .16; transform: none; } }
  @keyframes fade { from { opacity: 0; } to { opacity: 1; } }
  @keyframes grow { from { width: 0; } to { width: 60%; } }
</style>
</head>
<body>
  <div class="bg-letter">$letter</div>
  <div class="date">$date</div>
  <div class="head">
    <span class="word">$word</span>
    $phonetic_html
  </div>
  <div class="trans">$trans_html</div>
  $example_section
</body>
</html>''')

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName(self._object_name)
        self._word = None
        self._date = ""
        self._setup_ui()
        self._setup_periodic_refresh()
        logger.debug(f"[DWD] 每日一词组件初始化 id={self.component_id}")


    def _build_html(self) -> str:
        theme = self._theme_dark if isDarkTheme() else self._theme_light
        word = "--"
        phonetic = ""
        translation = ""
        ex_text = ""
        ex_trans = ""
        date = self._date or datetime.date.today().isoformat()
        if self._word:
            word = self._word.get("word") or "--"
            phonetic = self._word.get("phonetic") or ""
            translation = self._word.get("translation") or ""
            examples = self._word.get("examples") or []
            if examples:
                ex = examples[0] or {}
                ex_text = ex.get("text") or ""
                ex_trans = ex.get("translation") or ""
        date = date.replace("-", ".")

        # 背景水印字母
        letter = word[0].upper() if word and word != "--" else ""

        # 音标
        phonetic_html = f"<span class=\"phonetic\">{_html.escape(phonetic)}</span>" if phonetic else ""

        # 翻译
        trans_lines = []
        for line in self._POS_SPLIT_RE.split(translation.strip()):
            line = line.strip()
            if not line:
                continue
            m = self._POS_MARK_RE.match(line)
            if m:
                trans_lines.append(
                    f"<div class=\"line\"><span class=\"pos\">{_html.escape(m.group(1))}</span>{_html.escape(m.group(2))}</div>"
                )
            else:
                trans_lines.append(f"<div class=\"line\">{_html.escape(line)}</div>")
        trans_html = "".join(trans_lines)

        # 例句区
        example_section = ""
        if ex_text:
            example_section = (
                "<div class=\"sep\"></div>"
                "<div class=\"example\">"
                f"<div class=\"ex-en\">&ldquo;{_html.escape(ex_text)}&rdquo;</div>"
                + (f"<div class=\"ex-zh\">{_html.escape(ex_trans)}</div>" if ex_trans else "")
                + "</div>"
            )

        html = self._HTML_TEMPLATE.substitute(
            font=FONT_FAMILY,
            letter=_html.escape(letter),
            word=_html.escape(word),
            date=_html.escape(date),
            phonetic_html=phonetic_html,
            trans_html=trans_html,
            example_section=example_section,
            **theme,
        )
        logger.debug(f"[DWD] 生成每日一词 html 长度 {len(html)} 单词='{word}'")
        return html

    def _refresh(self):
        data = WordService.fetch_daily_word(use_cache=True)
        if not data:
            data = get_cached_content("daily_word", ignore_expiry=True)
            if data:
                logger.debug("[Word] 每日单词接口无数据 用缓存")
        if data:
            self._word = data.get("word")
            self._date = str(data.get("date", "") or "")
            logger.debug(f"[Word] 每日单词已更新: {self._word} ({self._date})")
        else:
            logger.debug("[Word] 每日单词无数据")
        self._render()

class DailySentenceComponent(_HtmlCardComponent):
    """每日英语"""
    _min_size = (200, 110)
    _default_size = (400, 200)

    _object_name = "dailySentenceContainer"

    _theme_dark = {
        "accent": "#30c361",
        "en1": "#f5f5f5",
        "en2": "#9dbdae",
        "zh": "rgba(255, 255, 255, 0.72)",
        "date_c": "rgba(255, 255, 255, 0.45)",
    }
    _theme_light = {
        "accent": "#30c361",
        "en1": "#1f2937",
        "en2": "#4f7a6b",
        "zh": "rgba(40, 40, 40, 0.78)",
        "date_c": "rgba(60, 60, 60, 0.5)",
    }

    _HTML_TEMPLATE = Template('''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { width: 100%; height: 100%; background: transparent; overflow: hidden; }
  body {
    font-family: $font;
    display: flex; flex-direction: column; justify-content: center;
    padding: 24px 26px 26px 58px; position: relative;
  }
  .quote {
    position: absolute; top: -4px; left: 14px;
    font-family: Georgia, serif; font-size: 118px; line-height: 1;
    background: linear-gradient(135deg, $accent, transparent 92%);
    -webkit-background-clip: text; -webkit-text-fill-color: transparent;
    animation: pop .7s cubic-bezier(.2, .9, .3, 1.4) both;
  }
  .en {
    font-family: Georgia, 'Times New Roman', serif;
    font-style: italic; font-weight: 600; font-size: 27px; line-height: 1.42;
    background: linear-gradient(120deg, $en1, $en2);
    -webkit-background-clip: text; -webkit-text-fill-color: transparent;
    animation: rise .6s .05s ease-out both;
  }
  .zh {
    margin-top: 14px; padding-left: 12px;
    border-left: 3px solid $accent;
    color: $zh; font-size: 17px; line-height: 1.5;
    animation: rise .6s .18s ease-out both;
  }
  .date {
    position: absolute; top: 15px; right: 18px;
    font-family: Consolas, 'Courier New', monospace;
    font-size: 12px; letter-spacing: 2.5px;
    color: $date_c;
    animation: fade .9s .3s ease-out both;
  }
  .bar {
    position: absolute; left: 26px; bottom: 12px; height: 2px;
    background: linear-gradient(90deg, $accent, transparent);
    animation: grow .8s .25s cubic-bezier(.2, .8, .2, 1) both;
  }
  @keyframes rise { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: none; } }
  @keyframes pop  { from { opacity: 0; transform: scale(.55); } to { opacity: 1; transform: none; } }
  @keyframes fade { from { opacity: 0; } to { opacity: 1; } }
  @keyframes grow { from { width: 0; } to { width: 150px; } }
</style>
</head>
<body>
  <div class="quote">&ldquo;</div>
  <div class="en">$content</div>
  $zh_html
  <div class="date">$date</div>
  <div class="bar"></div>
</body>
</html>''')

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName(self._object_name)
        self._sentence = None
        self._date = ""
        self._setup_ui()
        self._setup_periodic_refresh()
        logger.debug(f"[DSE] 每日一句组件初始化 id={self.component_id}")


    def _build_html(self) -> str:
        theme = self._theme_dark if isDarkTheme() else self._theme_light
        content = "--"
        note = ""
        date = self._date or datetime.date.today().isoformat()
        if self._sentence:
            content = self._sentence.get("content") or "--"
            note = self._sentence.get("note") or ""
        date = date.replace("-", ".")

        zh_html = ""
        if note:
            zh_html = f"<div class=\"zh\">{_html.escape(note)}</div>"

        html = self._HTML_TEMPLATE.substitute(
            font=FONT_FAMILY,
            content=_html.escape(content),
            zh_html=zh_html,
            date=_html.escape(date),
            **theme,
        )
        logger.debug(f"[DSE] 生成每日英语 html 长度 {len(html)}")
        return html

    def _refresh(self):
        data = SentenceService.fetch_daily_sentence(use_cache=True)
        if not data:
            data = get_cached_content("daily_sentence", ignore_expiry=True)
            if data:
                logger.debug("[Sentence] 每日英语接口无数据 用缓存")
        if data:
            self._sentence = data.get("sentence")
            self._date = str(data.get("date", "") or "")
            logger.debug(f"[Sentence] 每日英语已更新 ({self._date})")
        else:
            logger.debug("[Sentence] 每日英语无数据")
        self._render()

_perf_cpu_lock = threading.Lock()
_perf_cpu_baseline = None  # (busy, total)

def _read_system_cpu():
    """读cpu占用
    """
    global _perf_cpu_baseline
    import psutil
    with _perf_cpu_lock:
        t = psutil.cpu_times()
        total = sum(t)
        busy = total - t.idle - getattr(t, "iowait", 0.0)
        prev = _perf_cpu_baseline
        _perf_cpu_baseline = (busy, total)
        if prev is None:
            return None
        d_total = total - prev[1]
        if d_total <= 0:
            logger.debug("[Perf] CPU 时间片无增量 占用按 0 处理")
            return 0.0
        return round((busy - prev[0]) / d_total * 100, 1)

_GPU_PDH_LOCK = threading.Lock()
_GPU_PDH = None    # {'query': 句柄, 'pairs': [(计数器, luid)], 'fails': 连败数, 'built_at': 建表时刻}
_PERF_CPU_WARNED = False
_PERF_GPU_WARNED = False


def _collect_gpu_pdh():
    """读gpu占用
    """
    global _GPU_PDH
    import win32pdh
    with _GPU_PDH_LOCK:
        st = _GPU_PDH
        stale = st is not None and (st['fails'] >= 3 or time.time() - st['built_at'] > 300)
        if st is None or stale:
            if st is not None:
                try:
                    win32pdh.CloseQuery(st['query'])
                except Exception:
                    pass
            items = win32pdh.EnumObjectItems(None, None, "GPU Engine", win32pdh.PERF_DETAIL_WIZARD)
            query = win32pdh.OpenQuery()
            pairs = []
            for inst in items[1]:
                if not inst.endswith("engtype_3D"):
                    continue
                try:
                    path = win32pdh.MakeCounterPath(
                        (None, "GPU Engine", inst, None, 0, "Utilization Percentage"))
                    luid = inst[inst.index("luid_"):inst.index("_phys_")] if (
                        "luid_" in inst and "_phys_" in inst) else inst
                    pairs.append((win32pdh.AddCounter(query, path), luid))
                except Exception as e:
                    logger.debug(f"[Perf] GPU 计数器路径构建失败({inst}): {e}")
            st = _GPU_PDH = {'query': query, 'pairs': pairs, 'fails': 0,
                             'built_at': time.time(), 'fresh': True}
        if not st['pairs']:
            return None
        try:
            win32pdh.CollectQueryData(st['query'])
        except Exception as e:
            st['fails'] += 1
            if st['fails'] == 1:
                logger.warning(f"[Perf] GPU 数据采集失败(连续{st['fails']}次): {e}")
            return None
        if st.pop('fresh', False):
            return None 
        per_gpu = {}
        n_bad = 0
        for h, luid in st['pairs']:
            try:
                v = win32pdh.GetFormattedCounterValue(h, win32pdh.PDH_FMT_DOUBLE)[1]
            except Exception:
                n_bad += 1  
                continue
            per_gpu[luid] = per_gpu.get(luid, 0.0) + v
        if st['pairs'] and n_bad >= len(st['pairs']):
            st['fails'] = 3 
            return None
        if not per_gpu:
            return None
        st['fails'] = 0
        return min(100.0, max(0.0, max(per_gpu.values())))


def _collect_perf_data(with_gpu: bool = True) -> dict:
    """采 CPU/RAM/GPU 使用率

    Args:
        with_gpu: False 时跳过 GPU 查询

    Returns:
        dict(cpu, ram, gpu),百分比
    """
    data = {"cpu": None, "ram": None, "gpu": None}
    # CPU / RAM
    try:
        import psutil
        cpu = _read_system_cpu()
        data["cpu"] = 0.0 if cpu is None else cpu
        data["ram"] = psutil.virtual_memory().percent
    except Exception as e:
        global _PERF_CPU_WARNED
        if not _PERF_CPU_WARNED:
            _PERF_CPU_WARNED = True
            logger.warning(f"[Perf] CPU/内存占用采集失败: {e}")
    # GPU：Windows GPU Engine(*engtype_3D)\Utilization Percentage
    try:
        gpu = _collect_gpu_pdh()
        if gpu is not None:
            data["gpu"] = round(gpu, 1)
    except Exception as e:
        global _PERF_GPU_WARNED
        if not _PERF_GPU_WARNED:
            _PERF_GPU_WARNED = True
            logger.warning(f"[Perf] GPU 占用采集失败: {e}")
    return data


class _PerfSampler(QObject):
    """性能采样器
    Signals:
        perf_ready(dict): {"cpu","ram","gpu"} 百分比
        net_ready(dict): {"d","u"} 比特/秒
    """
    perf_ready = pyqtSignal(dict)
    net_ready = pyqtSignal(dict)

    def __init__(self):
        super().__init__()
        self._refs = 0
        self._busy = False
        self._tick_count = 0
        self._last_gpu = None
        self._last_io = None
        self._last_t = None
        self._timer = QTimer(self)
        self._timer.setInterval(1000)
        self._timer.timeout.connect(self._tick)

    def add_ref(self, owner: QObject):
        """注册订阅组件"""
        self._refs += 1
        owner.destroyed.connect(self._on_ref_gone)
        if self._refs == 1 and not self._timer.isActive():
            self._timer.start()
            logger.debug(f"[PerfSampler] 采样启动 (订阅组件{self._refs}个)")
        else:
            logger.debug(f"[PerfSampler] 新组件订阅 共{self._refs}个 (复用同一份采样)")

    def _on_ref_gone(self):
        self._refs = max(0, self._refs - 1)
        if self._refs == 0 and self._timer.isActive():
            self._timer.stop()
            logger.debug("[PerfSampler] 无订阅组件 采样停止")

    def _tick(self):
        if self._busy:
            return
        self._busy = True
        threading.Thread(target=self._worker, daemon=True).start()

    def _worker(self):
        try:
            self._tick_count += 1
            with_gpu = self._tick_count % 2 == 1 or self._last_gpu is None
            perf = _collect_perf_data(with_gpu=with_gpu)
            if perf.get("gpu") is None and self._last_gpu is not None:
                perf["gpu"] = self._last_gpu
            else:
                self._last_gpu = perf.get("gpu")
            self.perf_ready.emit(perf)
            import psutil
            io = psutil.net_io_counters()
            now = time.monotonic()
            if self._last_io is None:
                d = u = 0.0
            else:
                dt = max(now - self._last_t, 1e-3)
                d = max(0, io.bytes_recv - self._last_io.bytes_recv) * 8 / dt
                u = max(0, io.bytes_sent - self._last_io.bytes_sent) * 8 / dt
            self._last_io, self._last_t = io, now
            self.net_ready.emit({"d": round(d, 1), "u": round(u, 1)})
        except Exception as e:
            logger.warning(f"[PerfSampler] 采样失败: {e}")
        finally:
            self._busy = False


_perf_sampler = None


def _get_perf_sampler() -> _PerfSampler:
    """采样器单例"""
    global _perf_sampler
    if _perf_sampler is None:
        _perf_sampler = _PerfSampler()
        logger.debug("[PerfSampler] 性能采样器单例已创建")
    return _perf_sampler

class PerformanceMonitorComponent(DraggableContainer):
    """性能监测组件"""

    _min_size = (220, 130)
    _default_size = (400, 200)

    _object_name = "performanceMonitorContainer"

    _metrics = (
        ("cpu", "#0078d4", "CPU"),
        ("ram", "#8b5cf6", "RAM"),
        ("gpu", "#10b981", "GPU"),
    )

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName(self._object_name)
        self._data = None
        self._setup_ui()
        _get_perf_sampler().perf_ready.connect(self._on_perf_ready)
        _get_perf_sampler().add_ref(self)
        logger.debug(f"[PM] 性能监测就绪 id={self.component_id}")

    def _setup_ui(self):
        layout = self.inner_layout
        layout.setContentsMargins(8, 4, 8, 4)
        row = QHBoxLayout()
        row.setSpacing(0)
        self._rings = {}
        for key, color, label in self._metrics:
            ring = ProgressRing(self)
            ring.setRange(0, 100)
            ring.setValue(0)
            ring.setTextVisible(True)
            ring.setFormat("%p%")
            ring.setCustomBarColor(QColor(color), QColor(color))
            ring.setStrokeWidth(self._scaled_px(8))
            ring.setFixedSize(self._scaled_px(92), self._scaled_px(92))
            ring.setFont(self._ring_font())
            box = QWidget(self)
            v = QVBoxLayout(box)
            v.setContentsMargins(0, 0, 0, 0)
            v.setSpacing(self._scaled_px(6))
            v.addStretch(1)
            v.addWidget(ring, 0, Qt.AlignmentFlag.AlignCenter)
            cap = CaptionLabel(label, box)
            cap.setAlignment(Qt.AlignmentFlag.AlignCenter)
            v.addWidget(cap, 0, Qt.AlignmentFlag.AlignCenter)
            v.addStretch(1)
            row.addWidget(box, 1)
            self._rings[key] = ring
        layout.addLayout(row, 1)
        self.setMinimumSize(*self._min_size)
        self._size_explicitly_set = True
        self.resize(*self._default_size)
        self._apply_style()

    def _ring_font(self):
        f = QFont(FONT_FAMILY)
        f.setPixelSize(self._scaled_px(20))
        f.setBold(True)
        return f

    def _apply_style(self):
        self._apply_card_style()
        self.updateSize()

    def apply_scale(self, factor):
        super().apply_scale(factor)
        for key, color, label in self._metrics:
            ring = self._rings[key]
            ring.setFixedSize(self._scaled_px(88), self._scaled_px(88))
            ring.setStrokeWidth(self._scaled_px(8))
            ring.setFont(self._ring_font())
        self.updateSize()

    def _on_perf_ready(self, data: dict):
        if self._data == data and self._data is not None:
            return
        _first = not getattr(self, "_perf_logged_once", False)
        self._data = data
        if _first and isinstance(data, dict):
            logger.debug(f"[Perf] 首次性能采样: CPU={data.get('cpu', '?')}% 内存={data.get('ram', '?')}% GPU={data.get('gpu', '?')}%")
            self._perf_logged_once = True
        for key, color, label in self._metrics:
            v = data.get(key)
            if v is not None:
                self._rings[key].setValue(int(round(float(v))))

class NetworkSpeedComponent(_HtmlCardComponent):
    """网速监控（html）"""
    _min_size = (300, 160)
    _default_size = (400, 200)

    _object_name = "netSpeedContainer"

    _theme_light = {
        "ink": "rgba(0,0,0,0.89)", "sub": "rgba(0,0,0,0.60)",
        "grid": "rgba(0,0,0,0.07)", "gridv": "rgba(0,0,0,0.10)",
    }
    _theme_dark = {
        "ink": "rgba(255,255,255,0.95)", "sub": "rgba(255,255,255,0.60)",
        "grid": "rgba(255,255,255,0.08)", "gridv": "rgba(255,255,255,0.12)",
    }

    _HTML_TEMPLATE = Template('''<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  html, body { width: 100%; height: 100%; background: transparent; overflow: hidden; }
  body { font-family: $font; color: $ink; user-select: none; cursor: grab; }
  body.ns-drag { cursor: grabbing; }
  #app { display: flex; flex-direction: column; width: 100%; height: 100%; padding: 2px 12px 8px; }
  #topbar { display: flex; align-items: center; gap: 10px; padding: 3px 2px 6px; flex: none; }
  #title { font-size: 12px; font-weight: 700; color: $sub; letter-spacing: .5px; }
  .stat { font-size: 11.5px; font-weight: 600; color: $sub; font-variant-numeric: tabular-nums; white-space: nowrap; }
  .stat b { color: $line; font-weight: 700; margin-right: 2px; }
  #stat-down { margin-left: auto; }
  #chart { flex: 1; min-height: 0; position: relative; }
  canvas { display: block; width: 100%; height: 100%; }
</style>
</head>
<body>
<div id="app">
  <div id="topbar">
    <span id="title">$title</span>
    <span class="stat" id="stat-down"><b>&#8595;$recv</b> <span id="dv">0 B/s</span></span>
    <span class="stat" id="stat-up"><b>&#8593;$send</b> <span id="uv">0 B/s</span></span>
  </div>
  <div id="chart"><canvas id="cv"></canvas></div>
</div>
<script src="qrc:///qtwebchannel/qwebchannel.js"></script>
<script>
var MAXPTS = 60;                 // 60 秒窗口
var down = [], up = [];
var scale = 50000;               // 纵轴上限（bps）
var lowCount = 0;
var GRID_H = 4;                  // 横向 4 格
var bridge = null;

var dv = document.getElementById('dv');
var uv = document.getElementById('uv');
var cv = document.getElementById('cv');
var ctx = cv.getContext('2d');
var W = 0, H = 0;

function fmtBits(v) {
  if (v >= 1e9) return (v / 1e9).toFixed(1) + ' Gbps';
  if (v >= 1e6) return (v / 1e6).toFixed(1) + ' Mbps';
  if (v >= 1e3) return Math.round(v / 1e3) + ' Kbps';
  return Math.round(v) + ' bps';
}
function fmtBytes(v) {
  if (v >= 1048576) return (v / 1048576).toFixed(1) + ' MB/s';
  if (v >= 1024) return (v / 1024).toFixed(1) + ' KB/s';
  return Math.round(v) + ' B/s';
}

function resize() {
  var r = document.getElementById('chart').getBoundingClientRect();
  if (r.width < 10 || r.height < 10) return;
  var dpr = window.devicePixelRatio || 1;
  W = r.width; H = r.height;
  cv.width = Math.round(W * dpr);
  cv.height = Math.round(H * dpr);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  draw();
}

function draw() {
  if (W < 10 || H < 10) return;
  ctx.clearRect(0, 0, W, H);
  var i, x, y;
  // 横向网格
  ctx.lineWidth = 1;
  ctx.strokeStyle = '$grid';
  for (i = 0; i <= GRID_H; i++) {
    y = Math.round(H * i / GRID_H) + 0.5;
    ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke();
  }
  // 纵向网格
  var step = W / (MAXPTS - 1);
  ctx.strokeStyle = '$gridv';
  for (x = W; x > 0; x -= step * 10) {
    var gx = Math.round(x) + 0.5;
    ctx.beginPath(); ctx.moveTo(gx, 0); ctx.lineTo(gx, H); ctx.stroke();
  }
  if (down.length < 2) return;
  // 接收
  ctx.beginPath();
  for (i = 0; i < down.length; i++) {
    x = W - (down.length - 1 - i) * step;
    y = H - Math.min(down[i] / scale, 1) * H;
    if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y);
  }
  ctx.lineTo(x, H); ctx.lineTo(W - (down.length - 1) * step, H); ctx.closePath();
  ctx.fillStyle = '$fill';
  ctx.fill();
  ctx.beginPath();
  for (i = 0; i < down.length; i++) {
    x = W - (down.length - 1 - i) * step;
    y = H - Math.min(down[i] / scale, 1) * H;
    if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y);
  }
  ctx.strokeStyle = '$line'; ctx.lineWidth = 1.6; ctx.lineJoin = 'round'; ctx.stroke();
  // 发送
  ctx.beginPath();
  ctx.setLineDash([3, 3]);
  for (i = 0; i < up.length; i++) {
    x = W - (up.length - 1 - i) * step;
    y = H - Math.min(up[i] / scale, 1) * H;
    if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y);
  }
  ctx.strokeStyle = '$line'; ctx.lineWidth = 1.3; ctx.stroke();
  ctx.setLineDash([]);
  // 刻度
  ctx.font = '11px sans-serif';
  ctx.fillStyle = '$sub';
  ctx.textAlign = 'right'; ctx.textBaseline = 'top';
  ctx.fillText(fmtBits(scale), W - 4, 3);
}

function adaptScale() {
  var m = 1, a = down.concat(up);
  for (var i = 0; i < a.length; i++) if (a[i] > m) m = a[i];
  while (m > scale) { scale *= 2; lowCount = 0; }
  if (m < scale / 4) {
    if (lowCount >= 4 && scale > 50000) { scale /= 2; lowCount = 0; }
    else lowCount++;
  } else lowCount = 0;
}

window.updateNet = function (s) {
  s = s || {};
  down.push(s.d || 0); up.push(s.u || 0);
  if (down.length > MAXPTS) { down.shift(); up.shift(); }
  dv.textContent = fmtBytes((s.d || 0) / 8);
  uv.textContent = fmtBytes((s.u || 0) / 8);
  adaptScale();
  draw();
  return true;
};

window.addEventListener('resize', resize);

// 拖拽
var nsDrag = false;
function send(kind, x, y) {
  if (!bridge) return;
  if (kind === 0) bridge.drag_start(x, y);
  else if (kind === 1) bridge.drag_move(x, y);
  else bridge.drag_end();
}
document.getElementById('app').addEventListener('mousedown', function (e) {
  if (e.button !== 0) return;
  nsDrag = true; document.body.classList.add('ns-drag');
  send(0, e.clientX, e.clientY); e.preventDefault();
});
window.addEventListener('mousemove', function (e) {
  if (nsDrag) send(1, e.clientX, e.clientY);
});
window.addEventListener('mouseup', function () {
  if (nsDrag) { nsDrag = false; document.body.classList.remove('ns-drag'); send(2, 0, 0); }
});

if (typeof QWebChannel !== 'undefined' && typeof qt !== 'undefined') {
  new QWebChannel(qt.webChannelTransport, function (channel) {
    bridge = channel.objects.bridge;
  });
}
resize();
</script>
</body>
</html>''')

    def __init__(self, parent, component_data: dict):
        super().__init__(parent, component_id=component_data["id"], layout_direction="vertical")
        self.setObjectName(self._object_name)
        self._bridge = _WebBridge(
            lambda phase, x, y: self._relay_web_drag(phase, x, y, self.webView),
            parent=self)
        self._setup_ui()
        # 网速差分由全局采样器统一计算, 组件只负责推送
        _get_perf_sampler().net_ready.connect(self._on_net_sample)
        _get_perf_sampler().add_ref(self)
        logger.debug(f"[Net] 网速就绪 id={self.component_id}")


    def _build_html(self) -> str:
        theme = dict(_theme_pair(self._theme_dark, self._theme_light))
        theme["line"] = "#e2543a"
        theme["fill"] = "rgba(226, 84, 58, 0.20)"
        html = self._HTML_TEMPLATE.substitute(
            font=FONT_FAMILY,
            title=tr("netspeed.title"),
            recv=tr("netspeed.recv"),
            send=tr("netspeed.send"),
            **theme,
        )
        logger.debug(f"[Net] 生成网速组件 html 长度 {len(html)}")
        return html

    def _on_net_sample(self, sample: dict):
        """收到全局采样器的网速数据, 推给页面"""
        try:
            self.webView.page().runJavaScript(f"if (window.updateNet) updateNet({json.dumps(sample)});")
        except Exception as e:
            if not getattr(self, "_net_push_warned", False):
                self._net_push_warned = True
                logger.warning(f"[Net] 网速数据推送失败: {e}")

_NEWS_SOURCES = {
    "baidu":        dict(_source="baidu", _icon_key="baidu",
                         _object_name="newsBaiduContainer"),
    "weibo":        dict(_source="weibo", _icon_key="weibo",
                         _object_name="newsWeiboContainer"),
    "jinritoutiao": dict(_source="jinritoutiao", _icon_key="jinritoutiao",
                         _object_name="newsJinritoutiaoContainer"),
    "tenxunwang":   dict(_source="tenxunwang", _icon_key="tencent",
                         _object_name="newsTenxunwangContainer"),
    "xcvts":        dict(_source="xcvts", _icon_key="cctv",
                         _object_name="newsCCTVContainer",
                         _item_count=3, _use_cctv_api=True),
}

COMPONENT_STYLES["clock"]["digital"]["class"] = DigitalClockComponent
COMPONENT_STYLES["clock"]["square_1"]["class"] = SquareClock1Component
COMPONENT_STYLES["clock"]["square_2"]["class"] = SquareClock2Component
COMPONENT_STYLES["clock"]["calendar_month"]["class"] = CalendarMonthComponent
COMPONENT_STYLES["clock"]["calendar_mini"]["class"] = MiniCalendarComponent
COMPONENT_STYLES["weather"]["icon_temp"]["class"] = WeatherIconTempComponent
COMPONENT_STYLES["weather"]["hourly"]["class"] = WeatherHourlyComponent
COMPONENT_STYLES["weather"]["weekly"]["class"] = WeatherWeeklyComponent
COMPONENT_STYLES["poetry"]["one_line"]["class"] = PoetryOneLineComponent
for _news_key, _news_cfg in _NEWS_SOURCES.items():
    COMPONENT_STYLES["news"][_news_key]["class"] = type(
        f"NewsComponent_{_news_key}", (NewsComponent,), dict(_news_cfg))
COMPONENT_STYLES["countdown"]["event"]["class"] = CountdownEventComponent
COMPONENT_STYLES["countdown"]["days"]["class"] = DaysMatterComponent
COMPONENT_STYLES["school_info"]["class_info"]["class"] = SchoolInfoComponent
COMPONENT_STYLES["media"]["player"]["class"] = MediaPlayerComponent
COMPONENT_STYLES["quick_launch"]["dock"]["class"] = QuickLaunchDockComponent
COMPONENT_STYLES["quick_launch"]["grid"]["class"] = QuickLaunchGridComponent
COMPONENT_STYLES["linkage"]["timetable_preview"]["class"] = TimetablePreviewComponent
COMPONENT_STYLES["linkage"]["timetable_nowlesson"]["class"] = TimetableNowLessonComponent
COMPONENT_STYLES["linkage"]["timetable_timeline"]["class"] = TimetableTimelineComponent
COMPONENT_STYLES["Math"]["calculator"]["class"] = CalculatorComponent
COMPONENT_STYLES["writing"]["pad"]["class"] = WritingPadComponent
COMPONENT_STYLES["class_album"]["horizontal"]["class"] = ClassAlbumComponent
COMPONENT_STYLES["class_album"]["vertical"]["class"] = ClassAlbumVerticalComponent
COMPONENT_STYLES["sticky_note"]["default"]["class"] = StickyNoteComponent
COMPONENT_STYLES["homework"]["board"]["class"] = HomeworkBoardComponent
COMPONENT_STYLES["history"]["today"]["class"] = HistoryTodayComponent
COMPONENT_STYLES["sentence"]["daily"]["class"] = DailySentenceComponent
COMPONENT_STYLES["word"]["daily"]["class"] = DailyWordComponent
COMPONENT_STYLES["system"]["performance"]["class"] = PerformanceMonitorComponent
COMPONENT_STYLES["system"]["netspeed"]["class"] = NetworkSpeedComponent
COMPONENT_STYLES["timer"]["countdown"]["class"] = TimerCountdownComponent

# 组件库面板

# 预览图目录
PREVIEW_DIR = os.path.join(os.path.dirname(os.path.dirname(__file__)), "resources", "component_preview")

class ComponentCard(CardWidget):
    """组件库预览卡片"""

    def __init__(self, definition: ComponentDefinition, parent=None):
        super().__init__(parent)
        self.definition = definition
        self._preview_pixmap = None
        self._drag_start = None

        self._setup_ui()
        self._load_preview()
        logger.debug(f"[LIB] 组件卡片就绪: {definition.id}")

    def _setup_ui(self):
        self.setFixedSize(180, 120)
        self.setCursor(Qt.CursorShape.OpenHandCursor)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(6)

        # 预览图
        self.preview_label = QLabel()
        self.preview_label.setObjectName("preview_label")
        self.preview_label.setFixedSize(164, 80)
        self.preview_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self.preview_label)

        # 名称
        name_label = BodyLabel(self.definition.display_name)
        name_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(name_label)

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self._drag_start = event.position().toPoint()
            self.setCursor(Qt.CursorShape.ClosedHandCursor)
        super().mousePressEvent(event)

    def mouseReleaseEvent(self, event):
        """短按视为选中"""
        logger.debug(f"[LIB] 组件卡片选中: {self.definition.id}")
        self.setCursor(Qt.CursorShape.OpenHandCursor)
        super().mouseReleaseEvent(event)

    def mouseMoveEvent(self, event):
        """位移超阈值启 QDrag"""
        if not (event.buttons() & Qt.MouseButton.LeftButton):
            return

        if self._drag_start is None:
            return
        distance = (event.position().toPoint() - self._drag_start).manhattanLength()
        if distance >= QApplication.startDragDistance():
            self._start_drag()

        super().mouseMoveEvent(event)

    def _load_preview(self):
        """无缓存画默认占位"""
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

        self.preview_label.setText(tr("component_library.no_preview"))
        logger.debug(f"[LIB] 组件预览图缺失: {self.definition.id}")
        self.preview_label.setStyleSheet("background: rgba(255,255,255,0.05); border-radius: 6px;")

    def _start_drag(self):
        """发起拖拽"""
        drag = QDrag(self)

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
        logger.debug(f"[CARD] 发起组件拖拽: type={comp_type} style={comp_style}")

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
    """组件库分类页"""

    def __init__(self, category: str, registry: ComponentRegistry, parent=None):
        """建分类页

        Args:
                    category: 分类名(COMPONENT_STYLES 一级键)
        """
        super().__init__(parent)
        self.category = category
        self.registry = registry
        self.setObjectName(f"category_{category}")

        self._setup_ui()

    def _setup_ui(self):
        logger.debug(f"[LIB] 初始化组件库分类页: {self.category}")
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
    """组件库悬浮窗"""

    def __init__(self, registry, parent=None):
        super().__init__(parent)
        self.registry = registry
        self.setObjectName("componentLibrary")

        self._setup_navigation()
        self.setWindowTitle(tr("component_library.title"))
        self.resize(500, 550)
        self.setWindowIcon(QIcon(get_resPath(APP_ICON)))
        self.setWindowFlags(self.windowFlags() | Qt.WindowType.WindowStaysOnTopHint)

        cfg.themeChanged.connect(self._on_theme_changed)
        self._on_theme_changed()
        logger.info(f"[Library] 组件库窗口已创建 分类={self.registry.get_categories()}")

    def _setup_navigation(self):
        logger.debug("[LIB] 组件库导航初始化")
        categories = self.registry.get_categories()

        icon_map = {
            "Clock": FUI.SYNC,
            "Weather": FUI.PHOTO,
            "Info": FUI.INFO,
            "Media": FUI.ALBUM,
            "Launcher": FUI.APPLICATION,
            "School": FUI.EDUCATION,
            "Tools": FUI.BRUSH,
            "System": FUI.GAUGE,
        }

        for category in categories:
            page = CategoryPage(category, self.registry, self)
            icon = icon_map.get(category, FUI.HOME)
            localized = tr(f"component_library.category_{category.lower()}")
            self.addSubInterface(page, icon, localized)

        #  展开导航栏
        self.navigationInterface.expand()
        self.navigationInterface.setReturnButtonVisible(False)

    def _on_theme_changed(self):
        logger.debug("[LIB] 组件库主题变更 重载样式")
        self.setStyleSheet(load_qss('component.qss'))

# 导航页（NavigationPage）

class NavItemCell(QWidget):
    """导航页单个格子"""

    clicked = pyqtSignal(int)
    requestMenu = pyqtSignal(int, QPoint)

    def __init__(self, index: int, item: dict, parent=None):
        super().__init__(parent)
        self._index = index
        self._item = item or {}
        self.setObjectName("navItemCell")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.setMouseTracking(True)
        self.setCursor(Qt.CursorShape.PointingHandCursor)

        self._font = QFont()
        self._font.setPointSize(9)
        self._font.setStyleHint(QFont.StyleHint.SansSerif)
        self.setFont(self._font)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(8, 10, 8, 8)
        layout.setSpacing(4)

        self.iconLabel = QLabel(self)
        self.iconLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.iconLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Expanding)
        layout.addWidget(self.iconLabel, 1)

        self.textLabel = BodyLabel(self)
        self.textLabel.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.textLabel.setWordWrap(True)
        self.textLabel.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)

        self.textLabel.setFixedHeight(32)
        layout.addWidget(self.textLabel, 0)

        self._hovered = False
        self._refresh()

    def setItem(self, item: dict):
        self._item = item or {}
        self._refresh()

    def setIconSize(self, px: int):
        pm = getattr(self, '_orig_icon', None)
        if pm is None or pm.isNull():
            return
        self.iconLabel.setPixmap(pm.scaled(
            px, px,
            Qt.AspectRatioMode.KeepAspectRatio,
            Qt.TransformationMode.SmoothTransformation,
        ))

    def resizeEvent(self, event):
        super().resizeEvent(event)
        side = min(self.width(), self.height())
        icon_px = max(24, int(side * 0.55))
        self.setIconSize(icon_px)
        text_h = max(28, int(side * 0.22))
        self.textLabel.setFixedHeight(text_h)
        font = QFont()
        font.setPointSize(max(8, int(side / 16)))
        font.setStyleHint(QFont.StyleHint.SansSerif)
        self.textLabel.setFont(font)

    def enterEvent(self, event):
        """放大动效"""
        self._hovered = True
        self.update()
        super().enterEvent(event)

    def leaveEvent(self, event):
        """复位动效"""
        self._hovered = False
        self.update()
        super().leaveEvent(event)

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self._press_pos = event.position().toPoint()
            self._drag_moved = False
            event.accept()
            return
        super().mousePressEvent(event)

    def mouseReleaseEvent(self, event):
        """短按启动目标"""
        if event.button() == Qt.MouseButton.LeftButton:
            if not getattr(self, "_drag_moved", False):
                self.clicked.emit(self._index)
            self._press_pos = None
            event.accept()
            return
        super().mouseReleaseEvent(event)

    def mouseMoveEvent(self, event):
        """位移超阈值标记拖动"""
        if hasattr(self, "_press_pos") and self._press_pos is not None:
            moved = (event.position().toPoint() - self._press_pos).manhattanLength()
            if moved > 6:
                self._drag_moved = True
            event.accept()
            return
        super().mouseMoveEvent(event)

    def contextMenuEvent(self, event):
        """发 requestMenu 给宿主"""
        self.requestMenu.emit(self._index, event.globalPos())
        event.accept()

    def paintEvent(self, event):
        """悬停圆角底 图标 标题"""
        super().paintEvent(event)
        if self._hovered:
            painter = QPainter(self)
            painter.setRenderHint(QPainter.RenderHint.Antialiasing)
            c = _accent_color()
            pen = QPen(QColor(c.red(), c.green(), c.blue(), 180))
            pen.setWidthF(1.6)
            painter.setPen(pen)
            painter.setBrush(Qt.BrushStyle.NoBrush)
            painter.drawRoundedRect(self.rect().adjusted(1, 1, -1, -1), 10, 10)

    def _refresh(self):
        name = self._item.get("name", "")
        logger.debug(f"[Nav] 刷新导航格子 [{self._index}] '{name}'")
        self.textLabel.setText(name)
        icon_filename = self._item.get("icon", "exe.ico")
        p = get_ql_icon_path(icon_filename)
        pm = QPixmap(p) if p and os.path.exists(p) else QPixmap()
        if pm.isNull():
            fallback = get_ql_icon_path("exe.ico")
            if fallback and os.path.exists(fallback):
                pm = QPixmap(fallback)
        # 原图留底:resizeEvent 每次从原图缩放,避免逐级降质
        self._orig_icon = pm
        self.iconLabel.setPixmap(pm)

class NavigationPage(QWidget):
    """导航页"""

    def __init__(self, parent=None, page_index: int = 0, page_manager=None):
        super().__init__(parent)
        self._page_index = page_index
        self._page_manager = page_manager
        self.setObjectName("navigationPage")
        self.setAcceptDrops(True)
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)

        self._items = []
        self._cells = []  # NavItemCell 列表
        self._executor = ThreadPoolExecutor(max_workers=1)
        self._margin = 24
        self._gap = 12
        self._min_cell = 96
        self._max_cell = 160

        self._placeholder = BodyLabel(self)
        self._placeholder.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._placeholder.setText(tr("nav_page.empty_hint"))
        ph_font = QFont()
        ph_font.setPointSize(10)
        ph_font.setStyleHint(QFont.StyleHint.SansSerif)
        self._placeholder.setFont(ph_font)
        self._updatePlaceholderColor()
        self._placeholder.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents)
        self._placeholder.hide()
        try:
            cfg.themeChanged.connect(self._updatePlaceholderColor)
        except Exception as e:
            logger.debug(f"[Nav] 主题变化信号连接失败: {e}")

        self.load()
        self._buildCells()
        self._relayout()
        logger.debug(f"[Nav] 导航页就绪 page={self._page_index} {len(self._items)}项")

    def load(self):
        if self._page_manager is not None:
            self._items = self._page_manager.get_page_items(self._page_index)
        else:
            self._items = []
        logger.debug(f"[Nav] 导航页加载: {len(self._items)}项 (page={self._page_index})")

    def save(self):
        if self._page_manager is not None:
            self._page_manager.set_page_items(self._page_index, self._items)
            logger.debug(f"[Nav] 导航页保存: {len(self._items)}项 (page={self._page_index})")

    def items(self):
        return list(self._items)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self._relayout()

    def dragEnterEvent(self, event):
        md = event.mimeData()
        if md.hasUrls() or md.hasText():
            event.acceptProposedAction()
        else:
            event.ignore()

    def dragMoveEvent(self, event):
        if event.mimeData().hasUrls() or event.mimeData().hasText():
            event.acceptProposedAction()
        else:
            event.ignore()

    def dropEvent(self, event):
        """文件/网址分别入库"""
        md = event.mimeData()
        added = False
        if md.hasUrls():
            for url in md.urls():
                local = url.toLocalFile()
                if local:
                    self._addItemByPath(local)
                    added = True
                else:
                    s = url.toString()
                    if s.startswith(("http://", "https://")):
                        self._addItemByUrl(s)
                        added = True
        elif md.hasText():
            text = md.text().strip()
            if text:
                if os.path.exists(text):
                    self._addItemByPath(text)
                    added = True
                elif text.startswith(("http://", "https://")) or ("." in text and " " not in text):
                    self._addItemByUrl(text)
                    added = True
        if added:
            logger.info(f"[Nav] 已拖放入库 page={self._page_index}")
            event.acceptProposedAction()
        else:
            event.ignore()

    def mousePressEvent(self, event):
        """空白处按下清格子选中态"""
        if event.button() == Qt.MouseButton.RightButton:
            logger.debug("[Nav] 导航页空白处右键 弹出添加菜单")
            menu = RoundMenu(parent=self)
            add_file = Action(FUI.FOLDER, tr("nav_page.add_file"))
            add_file.triggered.connect(self._addByDialog)
            menu.addAction(add_file)
            add_url = Action(FUI.LINK, tr("nav_page.add_url"))
            add_url.triggered.connect(self._addUrlByDialog)
            menu.addAction(add_url)
            menu.exec(event.globalPosition().toPoint())
            event.accept()
        else:
            super().mousePressEvent(event)

    def _updatePlaceholderColor(self):
        color = _theme_pair("rgba(230,230,230,0.95)", "rgba(60,60,60,1.0)")
        self._placeholder.setStyleSheet(
            f"color: {color}; font-size: 16px; background: transparent;"
        )

    def _buildCells(self):
        """重建全部格子"""
        for c in self._cells:
            c.setParent(None)
            c.deleteLater()
        self._cells = []
        for i, item in enumerate(self._items):
            cell = NavItemCell(i, item, self)
            cell.clicked.connect(self._onCellClick)
            cell.requestMenu.connect(self._onCellMenu)
            cell.show()
            self._cells.append(cell)
        logger.debug(f"[Nav] 重建导航格子: {len(self._cells)}个")

    def _relayout(self):
        """计算列数并摆放"""
        w = max(1, self.width())
        h = max(1, self.height())
        avail_w = w - self._margin * 2 + self._gap
        # 列数：每格在 [min_cell, max_cell] 之间
        cols = max(1, int(avail_w / (self._min_cell + self._gap)))
        cell_w = (avail_w - self._gap * (cols - 1)) / cols
        if cell_w > self._max_cell:
            cols = max(1, int(avail_w / (self._max_cell + self._gap)))
            cell_w = (avail_w - self._gap * (cols - 1)) / cols
        cell_size = int(max(self._min_cell * 0.8, cell_w))
        avail_h = h - self._margin * 2 + self._gap
        rows = max(1, int(avail_h / (cell_size + self._gap)))

        total_w = cols * cell_size + (cols - 1) * self._gap
        total_h = rows * cell_size + (rows - 1) * self._gap
        start_x = int((w - total_w) / 2)
        start_y = int((h - total_h) / 2)
        if start_x < self._margin:
            start_x = self._margin
        if start_y < self._margin:
            start_y = self._margin

        for i, cell in enumerate(self._cells):
            r = i // cols
            c = i % cols
            x = start_x + c * (cell_size + self._gap)
            y = start_y + r * (cell_size + self._gap)
            cell.setFixedSize(cell_size, cell_size)
            cell._index = i
            cell.move(x, y)

        if not self._cells:
            self._placeholder.setGeometry(self.rect())
            self._placeholder.show()
            self._placeholder.raise_()
        else:
            self._placeholder.hide()

    def _addItemByPath(self, file_path: str):
        """解析路径成条目入库"""
        try:
            info = resolve_app_from_path(file_path)
            self._items.append(info)
            self.save()
            self._buildCells()
            self._relayout()
            self._notifyAdded(info.get("name", ""))
            logger.info(f"[Nav] 条目入库: {info.get('name', '?')} (type={info.get('type', 'app')})")
        except Exception as e:
            logger.error(f"[NavigationPage] 添加文件失败: {e}")

    def _addItemByUrl(self, url: str, name: str = ""):
        """网址建条目入库

        Args:
            name: 显示名,可空
        """
        try:
            info = resolve_url_from_string(url, name=name or None)
            self._items.append(info)
            self.save()
            self._buildCells()
            self._relayout()
            self._notifyAdded(info.get("name", ""))
            logger.info(f"[Nav] 网址条目入库: {info.get('name', '?')} -> {url[:60]}")
        except Exception as e:
            logger.error(f"[NavigationPage] 添加网址失败: {e}")

    def _notifyAdded(self, name: str):
        """InfoBar 成功提示"""
        try:
            InfoBar.success(
                title=tr("nav_page.added"),
                content=name,
                parent=self.window(),
                duration=1500,
            )
        except Exception as e:
            logger.debug(f"[Nav] 成功提示显示失败 {e}")

    def _onCellClick(self, index: int):
        """按类型启动目标"""
        if not (0 <= index < len(self._items)):
            return
        item = self._items[index]
        path = item.get("path", "")
        name = item.get("name", "")
        app_type = item.get("type", "app")
        logger.debug(f"[Nav] 点击导航项 [{index}] {name} (type={app_type})")
        if path:
            self._executor.submit(self._launch_thread, path, name, app_type)

    def _launch_thread(self, target, name, app_type):
        """
        Args:
            target: 启动目标(exe 路径/目录/url)
            app_type: 条目类型(app/folder/url)
        """
        ok, info = _launch_target(target, app_type)
        if not ok:
            logger.warning(f"[NavigationPage] 启动失败: {info}")

    def _onCellMenu(self, index: int, global_pos: QPoint):
        """改名/删除转发

        Args:
            global_pos: 菜单弹出位置
        """
        if not (0 <= index < len(self._items)):
            return
        menu = RoundMenu(parent=self)
        rename_act = Action(FUI.EDIT, tr("nav_page.rename"))
        rename_act.triggered.connect(lambda: self._renameItem(index))
        menu.addAction(rename_act)
        menu.addSeparator()
        del_act = Action(FUI.DELETE, tr("nav_page.delete"))
        del_act.triggered.connect(lambda: self._deleteItem(index))
        menu.addAction(del_act)
        logger.debug(f"[Nav] 弹出导航项菜单 [{index}]")
        menu.exec(global_pos)

    def _renameItem(self, index: int):
        """弹窗改名并保存"""
        if not (0 <= index < len(self._items)):
            return
        old_name = self._items[index].get("name", "")
        box = MessageBoxBase(self.window())
        title = SubtitleLabel(tr("nav_page.rename_title"), box)
        box.viewLayout.addWidget(title)
        edit = LineEdit(box)
        edit.setText(old_name)
        edit.setClearButtonEnabled(True)
        box.viewLayout.addWidget(edit)
        rc = box.exec()
        box.deleteLater()
        if rc:
            new_name = edit.text().strip()
            if new_name:
                logger.info(f"[Nav] 导航项改名 [{index}]: '{old_name}' -> '{new_name}'")
                self._items[index]["name"] = new_name
                self.save()
                self._cells[index].setItem(self._items[index])

    def _deleteItem(self, index: int):
        """确认后删除并保存重建"""
        if not (0 <= index < len(self._items)):
            return
        item_name = self._items[index].get("name", "")
        msg = MessageBox(
            tr("nav_page.delete"),
            tr("nav_page.delete_confirm", name=item_name),
            self.window(),
        )
        if not msg.exec():
            return
        logger.info(f"[Nav] 导航项删除 [{index}]: {item_name}")
        del self._items[index]
        self.save()
        self._buildCells()
        self._relayout()

    def _addByDialog(self):
        """文件浏览选择应用/目录新增条目"""
        path, _ = QFileDialog.getOpenFileName(self, tr("nav_page.select_file"), "", "所有文件 (*.*)")
        if path:
            logger.debug(f"[Nav] 浏览添加条目: {path}")
            self._addItemByPath(path)
            return
        # 如果用户取消文件选择 再问是否选文件夹
        folder = QFileDialog.getExistingDirectory(self, tr("nav_page.select_folder"))
        if folder:
            logger.debug(f"[Nav] 浏览添加文件夹: {folder}")
            self._addItemByPath(folder)

    def _addUrlByDialog(self):
        """弹窗输入网址新增条目"""
        box = MessageBoxBase(self.window())
        title = SubtitleLabel(tr("nav_page.add_url_title"), box)
        box.viewLayout.addWidget(title)
        edit = LineEdit(box)
        edit.setPlaceholderText("https://")
        edit.setClearButtonEnabled(True)
        box.viewLayout.addWidget(edit)
        rc = box.exec()
        box.deleteLater()
        if rc:
            url = edit.text().strip()
            if url:
                logger.debug(f"[Nav] 通过弹窗添加网址: {url}")
                self._addItemByUrl(url)
