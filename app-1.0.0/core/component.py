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
组件系统
"""

import json
import logging
import os
from dataclasses import dataclass, field
from enum import Enum
from typing import Dict, List, Optional, Type, Any

from PyQt6.QtCore import QObject, pyqtSignal, QPoint, QRect

logger = logging.getLogger("Glimpseon.core.component")


class ResizeMode(Enum):
    """组件大小调整"""
    FIXED = "fixed"           # 固定尺寸
    HORIZONTAL = "horizontal" # 仅水平调整
    VERTICAL = "vertical"     # 仅垂直调整
    FREE = "free"             # 自由调整


@dataclass
class ComponentDefinition:
    """组件定义"""
    id: str                           # 唯一标识
    display_name: str                 # 显示名称
    category: str                     # 分类
    icon: str                         # 图标名称
    min_width_cells: int = 1          # 最小宽度格子数
    min_height_cells: int = 1         # 最小高度格子数
    default_width_cells: int = 2      # 默认宽度格子数
    default_height_cells: int = 2     # 默认高度格子数
    resize_mode: ResizeMode = ResizeMode.FREE
    component_class: Optional[Type] = None  # 组件实现类
    default_config: Dict = field(default_factory=dict)
    
    def to_dict(self) -> dict:
        return {
            "id": self.id,
            "display_name": self.display_name,
            "category": self.category,
            "icon": self.icon,
            "min_width_cells": self.min_width_cells,
            "min_height_cells": self.min_height_cells,
            "default_width_cells": self.default_width_cells,
            "default_height_cells": self.default_height_cells,
            "resize_mode": self.resize_mode.value,
            "default_config": self.default_config,
        }
    
    @classmethod
    def from_dict(cls, data: dict, component_class: Optional[Type] = None) -> 'ComponentDefinition':
        return cls(
            id=data["id"],
            display_name=data["display_name"],
            category=data["category"],
            icon=data["icon"],
            min_width_cells=data.get("min_width_cells", 1),
            min_height_cells=data.get("min_height_cells", 1),
            default_width_cells=data.get("default_width_cells", 2),
            default_height_cells=data.get("default_height_cells", 2),
            resize_mode=ResizeMode(data.get("resize_mode", "free")),
            component_class=component_class,
            default_config=data.get("default_config", {}),
        )


@dataclass
class GridSettings:
    """网格设置"""
    short_side_cells: int = 6          # 短边格子数
    gap_ratio: float = 0.12            # 间隙比例
    inset_percent: int = 5             # 边距百分比
    
    def to_dict(self) -> dict:
        return {
            "short_side_cells": self.short_side_cells,
            "gap_ratio": self.gap_ratio,
            "inset_percent": self.inset_percent,
        }
    
    @classmethod
    def from_dict(cls, data: dict) -> 'GridSettings':
        return cls(
            short_side_cells=data.get("short_side_cells", 6),
            gap_ratio=data.get("gap_ratio", 0.12),
            inset_percent=data.get("inset_percent", 5),
        )


@dataclass
class GridMetrics:
    """网格计算结果"""
    column_count: int                  # 列数
    row_count: int                     # 行数
    cell_size: float                   # 格子大小
    gap_px: float                      # 间隙大小
    edge_inset_px: float               # 边距
    grid_width_px: float               # 网格总宽度（完整格子部分）
    grid_height_px: float              # 网格总高度（完整格子部分）
    
    @property
    def pitch(self) -> float:
        """格子间距（格子大小 + 间隙）"""
        return self.cell_size + self.gap_px








class GridLayoutService:
    """网格布局计算"""
    
    def calculate_grid_metrics(
        self,
        host_width: float,
        host_height: float,
        settings: GridSettings
    ) -> GridMetrics:
        """计算网格尺寸"""
        if host_width <= 1 or host_height <= 1:
            return GridMetrics(0, 0, 0, 0, 0, 0, 0)
        
        short_side_cells = max(1, settings.short_side_cells)
        gap_ratio = max(0, settings.gap_ratio)
        
        # 计算边距
        edge_inset_px = self._calculate_edge_inset(
            host_width, host_height, short_side_cells, settings.inset_percent
        )
        
        available_width = max(1, host_width - edge_inset_px * 2)
        available_height = max(1, host_height - edge_inset_px * 2)
        
        # 方向计算
        if host_width >= host_height:  # 横向
            row_count = short_side_cells
            denominator = row_count + max(0, row_count - 1) * gap_ratio
            if denominator <= 0:
                return GridMetrics(0, 0, 0, 0, 0, 0, 0)
            
            cell_size = available_height / denominator
            gap_px = cell_size * gap_ratio
            pitch = cell_size + gap_px
            
            column_count = max(1, int((available_width + gap_px) // pitch))
            grid_width = column_count * cell_size + max(0, column_count - 1) * gap_px
            grid_height = row_count * cell_size + max(0, row_count - 1) * gap_px
            
            return GridMetrics(
                column_count, row_count, cell_size, gap_px, edge_inset_px,
                grid_width, grid_height
            )
        else:  # 纵向
            column_count = short_side_cells
            denominator = column_count + max(0, column_count - 1) * gap_ratio
            if denominator <= 0:
                return GridMetrics(0, 0, 0, 0, 0, 0, 0)
            
            cell_size = available_width / denominator
            gap_px = cell_size * gap_ratio
            pitch = cell_size + gap_px
            
            row_count = max(1, int((available_height + gap_px) // pitch))
            grid_width = column_count * cell_size + max(0, column_count - 1) * gap_px
            grid_height = row_count * cell_size + max(0, row_count - 1) * gap_px
            
            return GridMetrics(
                column_count, row_count, cell_size, gap_px, edge_inset_px,
                grid_width, grid_height
            )
    
    def _calculate_edge_inset(
        self,
        host_width: float,
        host_height: float,
        short_side_cells: int,
        inset_percent: int
    ) -> float:
        """计算边距"""
        if host_width <= 1 or host_height <= 1:
            return 0
        
        cells = max(1, short_side_cells)
        short_side_px = max(1, min(host_width, host_height))
        base_cell = short_side_px / cells
        inset_ratio = max(0, min(30, inset_percent)) / 100.0
        return max(0, min(80, base_cell * inset_ratio))
    
    def get_cell_rect(
        self,
        metrics: GridMetrics,
        column: int,
        row: int,
        width_cells: int = 1,
        height_cells: int = 1
    ) -> QRect:
        """获取格子的屏幕坐标"""
        x = metrics.edge_inset_px + column * metrics.pitch
        y = metrics.edge_inset_px + row * metrics.pitch
        w = width_cells * metrics.cell_size + max(0, width_cells - 1) * metrics.gap_px
        h = height_cells * metrics.cell_size + max(0, height_cells - 1) * metrics.gap_px
        return QRect(int(x), int(y), int(w), int(h))
    
    def point_to_cell(
        self,
        metrics: GridMetrics,
        point: QPoint
    ) -> tuple:
        """屏幕坐标转格子坐标 网格外返回 (-1, -1)"""
        if metrics.cell_size <= 0:
            return (-1, -1)

        # 相对于网格起点
        rel_x = point.x() - metrics.edge_inset_px
        rel_y = point.y() - metrics.edge_inset_px

        # 完全在网格之外
        if rel_x < 0 or rel_y < 0:
            return (-1, -1)
        if rel_x > metrics.grid_width_px or rel_y > metrics.grid_height_px:
            return (-1, -1)

        # 格子索引
        column = int(rel_x / metrics.pitch)
        row = int(rel_y / metrics.pitch)

        # 检查是否在间隙中
        cell_local_x = rel_x - column * metrics.pitch
        cell_local_y = rel_y - row * metrics.pitch
        if cell_local_x > metrics.cell_size or cell_local_y > metrics.cell_size:
            return (-1, -1)  # 在间隙中

        # 边界检查
        column = max(0, min(column, metrics.column_count - 1))
        row = max(0, min(row, metrics.row_count - 1))
        
        return (row, column)
    
    def check_collision(
        self,
        placements: list,
        target_row: int,
        target_column: int,
        width_cells: int,
        height_cells: int,
        exclude_placement_id: Optional[str] = None,
        page_index: int = 0
    ) -> bool:
        """碰撞检测"""
        for p in placements:
            if p.placement_id == exclude_placement_id:
                continue
            if p.page_index != page_index:
                continue
            if not p.enabled:
                continue
            
            if self._rects_overlap(
                target_row, target_column, width_cells, height_cells,
                p.row, p.column, p.width_cells, p.height_cells
            ):
                return True
        
        return False
    
    def _rects_overlap(
        self,
        r1_row: int, r1_col: int, r1_w: int, r1_h: int,
        r2_row: int, r2_col: int, r2_w: int, r2_h: int
    ) -> bool:
        """检查重叠"""
        # 矩形1范围
        r1_row_end = r1_row + r1_h - 1
        r1_col_end = r1_col + r1_w - 1
        
        # 矩形2范围
        r2_row_end = r2_row + r2_h - 1
        r2_col_end = r2_col + r2_w - 1
        
        # 检查重叠
        if r1_row > r2_row_end or r2_row > r1_row_end:
            return False
        if r1_col > r2_col_end or r2_col > r1_col_end:
            return False
        
        return True


class ComponentRegistry(QObject):
    """组件注册"""
    
    definitions_changed = pyqtSignal()
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self._definitions: Dict[str, ComponentDefinition] = {}
    
    def register(self, definition: ComponentDefinition):
        if definition.id:
            self._definitions[definition.id] = definition
            self.definitions_changed.emit()
    
    def register_batch(self, definitions: List[ComponentDefinition]):
        for d in definitions:
            if d.id:
                self._definitions[d.id] = d
        self.definitions_changed.emit()
    
    def unregister(self, component_id: str):
        if component_id in self._definitions:
            del self._definitions[component_id]
            self.definitions_changed.emit()
    
    def get_definition(self, component_id: str) -> Optional[ComponentDefinition]:
        return self._definitions.get(component_id)
    
    def has_definition(self, component_id: str) -> bool:
        return component_id in self._definitions
    
    def get_all_definitions(self) -> List[ComponentDefinition]:
        return list(self._definitions.values())
    
    def get_definitions_by_category(self, category: str) -> List[ComponentDefinition]:
        return [d for d in self._definitions.values() if d.category == category]
    
    def get_categories(self) -> List[str]:
        return sorted(set(d.category for d in self._definitions.values()))
    
    def load_from_json(self, path: str, component_classes: Optional[Dict[str, Type]] = None):
        try:
            with open(path, 'r', encoding='utf-8') as f:
                data = json.load(f)
            
            for comp_data in data.get("components", []):
                try:
                    comp_class = None
                    if component_classes:
                        comp_class = component_classes.get(comp_data.get("id"))

                    definition = ComponentDefinition.from_dict(comp_data, comp_class)
                    self.register(definition)
                except Exception as e:
                    logger.warning(f"跳过: {e} data={comp_data}")
            
            logger.info(f"加载 {len(self._definitions)} 个组件:{path}")
        except FileNotFoundError:
            logger.warning(f"文件不存在: {path}")
        except Exception as e:
            logger.error(f"加载失败: {e}")
            


BUILTIN_COMPONENT_DEFINITIONS = [
    ComponentDefinition(
        id="clock_digital",
        display_name="数字时钟",
        category="Clock",
        icon="Clock",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={"show_seconds": True, "show_lunar": True},
    ),
    ComponentDefinition(
        id="clock_square_1",
        display_name="方形钟表I",
        category="Clock",
        icon="Clock",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
    ComponentDefinition(
        id="clock_square_2",
        display_name="方形钟表II",
        category="Clock",
        icon="Clock",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
    ComponentDefinition(
        id="clock_calendar_month",
        display_name="月历",
        category="Clock",
        icon="Calendar",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=3,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
    ComponentDefinition(
        id="clock_calendar_mini",
        display_name="简约月历",
        category="Clock",
        icon="Calendar",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
    ComponentDefinition(
        id="weather_icon_temp",
        display_name="天气",
        category="Weather",
        icon="WeatherSunny",
        min_width_cells=2,
        min_height_cells=1,
        default_width_cells=2,
        default_height_cells=1,
        resize_mode=ResizeMode.HORIZONTAL,
        default_config={"show_icon": True},
    ),
    ComponentDefinition(
        id="weather_hourly",
        display_name="逐小时天气",
        category="Weather",
        icon="WeatherSunny",
        min_width_cells=4,
        min_height_cells=2,
        default_width_cells=4,
        default_height_cells=2,
        resize_mode=ResizeMode.HORIZONTAL,
        default_config={},
    ),
    ComponentDefinition(
        id="weather_weekly",
        display_name="逐日天气",
        category="Weather",
        icon="WeatherSunny",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FIXED,
        default_config={},
    ),
    ComponentDefinition(
        id="poetry_one_line",
        display_name="一言",
        category="Info",
        icon="Book",
        min_width_cells=4,
        min_height_cells=1,
        default_width_cells=4,
        default_height_cells=1,
        resize_mode=ResizeMode.HORIZONTAL,
    ),
    ComponentDefinition(
        id="countdown_event",
        display_name="倒计时",
        category="Clock",
        icon="Calendar",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={"target_name": "", "target_date": ""},
    ),
    ComponentDefinition(
        id="countdown_days",
        display_name="倒数日",
        category="Clock",
        icon="Calendar",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={"event_name": "", "target_date": "", "title_bg_color": "#F98E1B"},
    ),
    ComponentDefinition(
        id="school_info_class_info",
        display_name="班级卡片",
        category="School",
        icon="Education",
        min_width_cells=2,
        min_height_cells=1,
        default_width_cells=2,
        default_height_cells=1,
        resize_mode=ResizeMode.HORIZONTAL,
        default_config={"school": "", "class": ""},
    ),
    ComponentDefinition(
        id="media_player",
        display_name="媒体播放器",
        category="Media",
        icon="Music",
        min_width_cells=2,
        min_height_cells=1,
        default_width_cells=2,
        default_height_cells=1,
        resize_mode=ResizeMode.HORIZONTAL,
        default_config={"show_progress": True},
    ),
    ComponentDefinition(
        id="quick_launch_dock",
        display_name="快捷启动",
        category="Launcher",
        icon="App",
        min_width_cells=4,
        min_height_cells=1,
        default_width_cells=4,
        default_height_cells=1,
        resize_mode=ResizeMode.HORIZONTAL,
        default_config={"icon_size": 64},
    ),
    ComponentDefinition(
        id="quick_launch_grid",
        display_name="快捷启动II",
        category="Launcher",
        icon="App",
        min_width_cells=4,
        min_height_cells=2,
        default_width_cells=4,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={"apps": []},
    ),
    ComponentDefinition(
        id="linkage_timetable_preview",
        display_name="今日课表",
        category="School",
        icon="Education",
        min_width_cells=2,
        min_height_cells=3,
        default_width_cells=2,
        default_height_cells=5,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
    ComponentDefinition(
        id="linkage_timetable_nowlesson",
        display_name="当前课程",
        category="School",
        icon="Education",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FIXED,
        default_config={
            "show_teacher": True,
            "show_next": True,
            "show_duration": True,
            "show_countdown": True,
            "prepare_minutes": 3,
        },
    ),
    ComponentDefinition(
        id="linkage_timetable_timeline",
        display_name="课程时间轴",
        category="School",
        icon="Education",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
    ComponentDefinition(
        id="Math_calculator",
        display_name="计算器",
        category="Tools",
        icon="Calculator",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FIXED,
    ),
    ComponentDefinition(
        id="news_baidu",
        display_name="百度新闻",
        category="Info",
        icon="News",
        min_width_cells=4,
        min_height_cells=2,
        default_width_cells=4,
        default_height_cells=2,
        resize_mode=ResizeMode.HORIZONTAL,
    ),
    ComponentDefinition(
        id="news_weibo",
        display_name="微博新闻",
        category="Info",
        icon="News",
        min_width_cells=4,
        min_height_cells=2,
        default_width_cells=4,
        default_height_cells=2,
        resize_mode=ResizeMode.HORIZONTAL,
    ),
    ComponentDefinition(
        id="news_jinritoutiao",
        display_name="今日头条新闻",
        category="Info",
        icon="News",
        min_width_cells=4,
        min_height_cells=2,
        default_width_cells=4,
        default_height_cells=2,
        resize_mode=ResizeMode.HORIZONTAL,
    ),
    ComponentDefinition(
        id="news_tenxunwang",
        display_name="腾讯网新闻",
        category="Info",
        icon="News",
        min_width_cells=4,
        min_height_cells=2,
        default_width_cells=4,
        default_height_cells=2,
        resize_mode=ResizeMode.HORIZONTAL,
    ),
    ComponentDefinition(
        id="news_xcvts",
        display_name="央视新闻",
        category="Info",
        icon="News",
        min_width_cells=4,
        min_height_cells=2,
        default_width_cells=4,
        default_height_cells=2,
        resize_mode=ResizeMode.HORIZONTAL,
    ),
    ComponentDefinition(
        id="writing_pad",
        display_name="书写板",
        category="Tools",
        icon="Edit",
        min_width_cells=4,
        min_height_cells=1,
        default_width_cells=4,
        default_height_cells=1,
        resize_mode=ResizeMode.FIXED,
    ),
    ComponentDefinition(
        id="class_album_horizontal",
        display_name="横向相册",
        category="School",
        icon="Photo",
        min_width_cells=2,
        min_height_cells=1,
        default_width_cells=2,
        default_height_cells=1,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
    ComponentDefinition(
        id="class_album_vertical",
        display_name="纵向相册",
        category="School",
        icon="Photo",
        min_width_cells=1,
        min_height_cells=2,
        default_width_cells=1,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
    ComponentDefinition(
        id="sticky_note",
        display_name="便签",
        category="Tools",
        icon="Edit",
        min_width_cells=1,
        min_height_cells=1,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={"color": "yellow"},
    ),
    ComponentDefinition(
        id="homework_board",
        display_name="作业板",
        category="School",
        icon="Education",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=3,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
    ComponentDefinition(
        id="timer_countdown",
        display_name="计时与倒计时",
        category="Clock",
        icon="StopWatch",
        min_width_cells=2,
        min_height_cells=2,
        default_width_cells=2,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
    ComponentDefinition(
        id="history_today",
        display_name="历史上的今天",
        category="Info",
        icon="History",
        min_width_cells=4,
        min_height_cells=2,
        default_width_cells=4,
        default_height_cells=2,
        resize_mode=ResizeMode.HORIZONTAL,
        default_config={},
    ),
    ComponentDefinition(
        id="word_daily",
        display_name="每日单词",
        category="Info",
        icon="LocalLanguage",
        min_width_cells=4,
        min_height_cells=2,
        default_width_cells=5,
        default_height_cells=2,
        resize_mode=ResizeMode.HORIZONTAL,
        default_config={},
    ),
    ComponentDefinition(
        id="sentence_daily",
        display_name="每日英语",
        category="Info",
        icon="ChatBubblesQuestion",
        min_width_cells=4,
        min_height_cells=2,
        default_width_cells=5,
        default_height_cells=2,
        resize_mode=ResizeMode.HORIZONTAL,
        default_config={},
    ),
    ComponentDefinition(
        id="system_performance",
        display_name="性能监测",
        category="System",
        icon="Gauge",
        min_width_cells=4,
        min_height_cells=2,
        default_width_cells=4,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
    ComponentDefinition(
        id="system_netspeed",
        display_name="网速监控",
        category="System",
        icon="Globe",
        min_width_cells=3,
        min_height_cells=2,
        default_width_cells=4,
        default_height_cells=2,
        resize_mode=ResizeMode.FREE,
        default_config={},
    ),
]


@dataclass
class PageMeta:
    """页面元数据"""
    name: str
    type: str = "info"  # "info" 组件页 / "nav" 导航页
    components: list = None   # info 页的组件列表
    items: list = None        # nav 页的导航项列表

    def __post_init__(self):
        if self.components is None:
            self.components = []
        if self.items is None:
            self.items = []

    def to_dict(self) -> dict:
        d = {"name": self.name, "type": self.type}
        if self.type == "info":
            d["components"] = self.components
        elif self.type == "nav":
            d["items"] = self.items
        return d

    @classmethod
    def from_dict(cls, d: dict) -> 'PageMeta':
        return cls(
            name=d.get("name", ""),
            type=d.get("type", "info"),
            components=d.get("components", []),
            items=d.get("items", []),
        )


class PageManager:
    """加载/保存/添加/删除/重命名页面
       home_layout.json:
    {
        "current_page": 0,
        "pages": [
            {
                "name": "信息页",
                "type": "info",
                "components": [
                    {"id": "...", "type": "...", "style": "...", "position": {"x":0.5,"y":0.5}, "size": {"w":200,"h":80}, "enabled": true, "config": {}}
                ]
            },
            {
                "name": "导航页",
                "type": "nav",
                "items": [
                    {"name": "...", "path": "...", "icon": "...", "type": "app"}
                ]
            }
        ]
    }
    """

    DEFAULT_PAGES = [
        {"name": "信息页", "type": "info", "components": []},
        {"name": "导航页", "type": "nav", "items": []},
    ]
    MAX_PAGES = 10

    def __init__(self, config_dir: str):
        self._config_dir = config_dir
        self._layout_file = os.path.join(config_dir, "home_layout.json")
        self._pages: List[PageMeta] = []
        self._current_page = 0
        self.load()

    def load(self):
        """加载配置"""
        if os.path.exists(self._layout_file):
            self._load_unified()
        else:
            self._pages = [PageMeta.from_dict(p) for p in self.DEFAULT_PAGES]
            self._current_page = 0
            self.save()

    def _load_unified(self):
        try:
            with open(self._layout_file, "r", encoding="utf-8") as f:
                data = json.load(f)
            pages_data = data.get("pages", [])
            if not pages_data:
                pages_data = self.DEFAULT_PAGES
            self._pages = [PageMeta.from_dict(p) for p in pages_data]
            self._current_page = max(0, min(int(data.get("current_page", 0)), len(self._pages) - 1))
        except Exception as e:
            logger.error(f"[PageManager] 加载失败: {e}")
            self._pages = [PageMeta.from_dict(p) for p in self.DEFAULT_PAGES]
            self._current_page = 0

    def save(self):
        try:
            os.makedirs(self._config_dir, exist_ok=True)
            data = {
                "current_page": self._current_page,
                "pages": [p.to_dict() for p in self._pages],
            }
            with open(self._layout_file, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2, ensure_ascii=False)
        except Exception as e:
            logger.error(f"[PageManager] 保存失败: {e}")

    def pages(self) -> List[PageMeta]:
        return list(self._pages)

    def page_count(self) -> int:
        return len(self._pages)

    def get_page(self, index: int) -> Optional[PageMeta]:
        if 0 <= index < len(self._pages):
            return self._pages[index]
        return None

    def get_current_page(self) -> int:
        return self._current_page

    def set_current_page(self, index: int):
        if 0 <= index < len(self._pages):
            self._current_page = index

    def add_page(self, name: str = "", page_type: str = "info") -> int:
        """添加页面 返回 新页面 index/失败 -1"""
        if len(self._pages) >= self.MAX_PAGES:
            return -1
        if not name:
            info_count = sum(1 for p in self._pages if p.type == "info")
            nav_count = sum(1 for p in self._pages if p.type == "nav")
            if page_type == "nav":
                name = f"导航页 {nav_count + 1}"
            else:
                name = f"信息页 {info_count + 1}"
        self._pages.append(PageMeta(name=name, type=page_type))
        self.save()
        return len(self._pages) - 1

    def rename_page(self, index: int, name: str):
        if 0 <= index < len(self._pages):
            self._pages[index].name = name
            self.save()

    def delete_page(self, index: int) -> bool:
        """删除页面 至少一 导航页不能删"""
        if len(self._pages) <= 1:
            return False
        if not (0 <= index < len(self._pages)):
            return False
        if self._pages[index].type == "nav":
            return False
        del self._pages[index]
        if self._current_page >= len(self._pages):
            self._current_page = len(self._pages) - 1
        elif self._current_page > index:
            self._current_page -= 1
        self.save()
        return True

    def is_nav_page(self, index: int) -> bool:
        p = self.get_page(index)
        return p is not None and p.type == "nav"

    # 如下是获取/设置组件列表
    def get_page_components(self, index: int) -> list:
        """信息页"""
        p = self.get_page(index)
        if p and p.type == "info":
            return p.components
        return []

    def set_page_components(self, index: int, components: list):
        if 0 <= index < len(self._pages) and self._pages[index].type == "info":
            self._pages[index].components = components
            self.save()

    def get_page_items(self, index: int) -> list:
        """导航页"""
        p = self.get_page(index)
        if p and p.type == "nav":
            return p.items
        return []

    def set_page_items(self, index: int, items: list):
        if 0 <= index < len(self._pages) and self._pages[index].type == "nav":
            self._pages[index].items = items
            self.save()