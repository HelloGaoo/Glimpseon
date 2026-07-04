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
组件系统
"""

import json
import logging
from dataclasses import dataclass, field
from enum import Enum
from typing import Dict, List, Optional, Type, Any

from PyQt6.QtCore import QObject, pyqtSignal, QPoint, QRect, QSize
from PyQt6.QtWidgets import QWidget

logger = logging.getLogger("ClassLively")


class ResizeMode(Enum):
    """组件大小调整"""
    FIXED = "fixed"           # 固定尺寸
    HORIZONTAL = "horizontal" # 仅水平调整
    VERTICAL = "vertical"     # 仅垂直调整
    FREE = "free"             # 自由调整


@dataclass
class ComponentDefinition:
    """组件定义 - 描述一种组件类型"""
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
class ComponentPlacement:
    """组件放置实例 - 描述一个已放置的组件"""
    placement_id: str                  # 放置ID
    component_id: str                  # 组件定义ID
    row: int                           # 起始行
    column: int                        # 资始列
    width_cells: int                   # 当前宽度格子数
    height_cells: int                  # 当前高度格子数
    page_index: int = 0                # 页面索引
    enabled: bool = True               # 是否启用
    config: Dict = field(default_factory=dict)  # 组件配置
    
    def to_dict(self) -> dict:
        return {
            "placement_id": self.placement_id,
            "component_id": self.component_id,
            "row": self.row,
            "column": self.column,
            "width_cells": self.width_cells,
            "height_cells": self.height_cells,
            "page_index": self.page_index,
            "enabled": self.enabled,
            "config": self.config,
        }
    
    @classmethod
    def from_dict(cls, data: dict) -> 'ComponentPlacement':
        return cls(
            placement_id=data["placement_id"],
            component_id=data["component_id"],
            row=data["row"],
            column=data["column"],
            width_cells=data.get("width_cells", 2),
            height_cells=data.get("height_cells", 2),
            page_index=data.get("page_index", 0),
            enabled=data.get("enabled", True),
            config=data.get("config", {}),
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
    grid_width_px: float               # 网格总宽度
    grid_height_px: float              # 网格总高度
    
    @property
    def pitch(self) -> float:
        """格子间距（格子大小 + 间隙）"""
        return self.cell_size + self.gap_px








class EditSessionMode(Enum):
    """编辑会话模式"""
    NONE = 0
    PENDING_NEW = 1 # 准备添加新组件
    DRAGGING_NEW = 2 # 拖拽新组件到桌面
    DRAGGING_EXISTING = 3 # 拖拽已有组件
    RESIZING_EXISTING = 4 # 调整已有组件大小


@dataclass
class EditSession:
    """编辑会话状态"""
    mode: EditSessionMode = EditSessionMode.NONE
    component_id: Optional[str] = None
    placement_id: Optional[str] = None
    width_cells: int = 2
    height_cells: int = 2
    start_position: QPoint = field(default_factory=lambda: QPoint(0, 0))
    current_position: QPoint = field(default_factory=lambda: QPoint(0, 0))
    offset: QPoint = field(default_factory=lambda: QPoint(0, 0))
    target_row: int = -1
    target_column: int = -1
    
    @property
    def is_active(self) -> bool:
        return self.mode != EditSessionMode.NONE
    
    @property
    def is_dragging_new(self) -> bool:
        return self.mode == EditSessionMode.DRAGGING_NEW
    
    @property
    def is_dragging_existing(self) -> bool:
        return self.mode == EditSessionMode.DRAGGING_EXISTING
    
    @property
    def is_resizing(self) -> bool:
        return self.mode == EditSessionMode.RESIZING_EXISTING
    
    @property
    def has_target(self) -> bool:
        return self.target_row >= 0 and self.target_column >= 0





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
        """屏幕坐标转格子坐标"""
        if metrics.cell_size <= 0:
            return (-1, -1)
        
        # 相对于网格起点
        rel_x = point.x() - metrics.edge_inset_px
        rel_y = point.y() - metrics.edge_inset_px
        
        # 格子索引
        column = int(rel_x / metrics.pitch)
        row = int(rel_y / metrics.pitch)
        
        # 边界检查
        column = max(0, min(column, metrics.column_count - 1))
        row = max(0, min(row, metrics.row_count - 1))
        
        return (row, column)
    
    def check_collision(
        self,
        placements: List[ComponentPlacement],
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
        """注册组件定义"""
        if definition.id:
            self._definitions[definition.id] = definition
            self.definitions_changed.emit()
    
    def register_batch(self, definitions: List[ComponentDefinition]):
        """批量注册"""
        for d in definitions:
            if d.id:
                self._definitions[d.id] = d
        self.definitions_changed.emit()
    
    def unregister(self, component_id: str):
        """注销组件"""
        if component_id in self._definitions:
            del self._definitions[component_id]
            self.definitions_changed.emit()
    
    def get_definition(self, component_id: str) -> Optional[ComponentDefinition]:
        """获取组件定义"""
        return self._definitions.get(component_id)
    
    def has_definition(self, component_id: str) -> bool:
        """检查组件存在"""
        return component_id in self._definitions
    
    def get_all_definitions(self) -> List[ComponentDefinition]:
        """获取所有组件定义"""
        return list(self._definitions.values())
    
    def get_definitions_by_category(self, category: str) -> List[ComponentDefinition]:
        """按分类获取组件"""
        return [d for d in self._definitions.values() if d.category == category]
    
    def get_categories(self) -> List[str]:
        """获取所有分类"""
        return sorted(set(d.category for d in self._definitions.values()))
    
    def load_from_json(self, path: str, component_classes: Optional[Dict[str, Type]] = None):
        """加载组件定义"""
        try:
            with open(path, 'r', encoding='utf-8') as f:
                data = json.load(f)
            
            for comp_data in data.get("components", []):
                comp_class = None
                if component_classes:
                    comp_class = component_classes.get(comp_data["id"])
                
                definition = ComponentDefinition.from_dict(comp_data, comp_class)
                self.register(definition)
            
            logger.info(f"[ComponentRegistry] 从 {path} 加载 {len(self._definitions)} 个组件定义")
        except FileNotFoundError:
            logger.warning(f"[ComponentRegistry] 文件不存在: {path}")
        except Exception as e:
            logger.error(f"[ComponentRegistry] 加载失败: {e}")


class PlacementService(QObject):
    """放置管理"""
    
    placements_changed = pyqtSignal()
    placement_added = pyqtSignal(str)      # placement_id
    placement_removed = pyqtSignal(str)    # placement_id
    placement_updated = pyqtSignal(str)    # placement_id
    
    CONFIG_PATH = "config/component_placements.json"
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self._placements: Dict[str, ComponentPlacement] = {}
        self._grid_settings = GridSettings()
        self._widgets: Dict[str, QWidget] = {}  # placement_id -> widget
    
    def get_grid_settings(self) -> GridSettings:
        return self._grid_settings
    
    def set_grid_settings(self, settings: GridSettings):
        self._grid_settings = settings
        self.placements_changed.emit()
    
    def get_placement(self, placement_id: str) -> Optional[ComponentPlacement]:
        return self._placements.get(placement_id)
    
    def get_all_placements(self) -> List[ComponentPlacement]:
        return list(self._placements.values())
    
    def get_placements_by_page(self, page_index: int) -> List[ComponentPlacement]:
        return [p for p in self._placements.values() if p.page_index == page_index]
    
    def add_placement(
        self,
        component_id: str,
        row: int,
        column: int,
        width_cells: int,
        height_cells: int,
        page_index: int = 0,
        config: Optional[Dict] = None
    ) -> str:
        """添加新放置"""
        placement_id = self._generate_id()
        
        placement = ComponentPlacement(
            placement_id=placement_id,
            component_id=component_id,
            row=row,
            column=column,
            width_cells=width_cells,
            height_cells=height_cells,
            page_index=page_index,
            enabled=True,
            config=config or {},
        )
        
        self._placements[placement_id] = placement
        self.save()
        self.placement_added.emit(placement_id)
        
        return placement_id
    
    def remove_placement(self, placement_id: str):
        """删除放置"""
        if placement_id in self._placements:
            del self._placements[placement_id]
            
            # 删除对应的 widget
            if placement_id in self._widgets:
                widget = self._widgets[placement_id]
                widget.deleteLater()
                del self._widgets[placement_id]
            
            self.save()
            self.placement_removed.emit(placement_id)
    
    def update_placement(
        self,
        placement_id: str,
        row: Optional[int] = None,
        column: Optional[int] = None,
        width_cells: Optional[int] = None,
        height_cells: Optional[int] = None,
        config: Optional[Dict] = None
    ):
        """更新放置"""
        placement = self._placements.get(placement_id)
        if not placement:
            return
        
        if row is not None:
            placement.row = row
        if column is not None:
            placement.column = column
        if width_cells is not None:
            placement.width_cells = width_cells
        if height_cells is not None:
            placement.height_cells = height_cells
        if config is not None:
            placement.config = config
        
        self.save()
        self.placement_updated.emit(placement_id)
    
    def register_widget(self, placement_id: str, widget: QWidget):
        """注册放置对应的 widget"""
        self._widgets[placement_id] = widget
    
    def get_widget(self, placement_id: str) -> Optional[QWidget]:
        """获取放置对应的 widget"""
        return self._widgets.get(placement_id)
    
    def _generate_id(self) -> str:
        """生成唯一 ID"""
        import time
        return f"place_{int(time.time() * 1000) % 1000000}"
    
    def load(self):
        """从文件加载"""
        try:
            with open(self.CONFIG_PATH, 'r', encoding='utf-8') as f:
                data = json.load(f)
            
            self._grid_settings = GridSettings.from_dict(data.get("grid_settings", {}))
            
            self._placements.clear()
            for p_data in data.get("placements", []):
                placement = ComponentPlacement.from_dict(p_data)
                self._placements[placement.placement_id] = placement
            
            self.placements_changed.emit()
            logger.info(f"[PlacementService] 加载 {len(self._placements)} 个放置配置")
        except FileNotFoundError:
            logger.info(f"[PlacementService] 配置文件不存在，使用默认设置")
            self._create_default_placements()
        except Exception as e:
            logger.error(f"[PlacementService] 加载失败: {e}")
    
    def save(self):
        """保存"""
        try:
            data = {
                "grid_settings": self._grid_settings.to_dict(),
                "placements": [p.to_dict() for p in self._placements.values()],
            }
            
            with open(self.CONFIG_PATH, 'w', encoding='utf-8') as f:
                json.dump(data, f, indent=2, ensure_ascii=False)
            
            logger.debug(f"[PlacementService] 保存 {len(self._placements)} 个放置配置")
        except Exception as e:
            logger.error(f"[PlacementService] 保存失败: {e}")
    
    def _create_default_placements(self):
        """创建默认放置"""
        defaults = [
            ("clock_digital", 1, 2, 2, 2),
            ("weather_icon_temp", 0, 5, 2, 1),
            ("poetry_one_line", 4, 1, 4, 1),
            ("countdown_event", 2, 3, 2, 2),
            ("school_info_class", 0, 0, 2, 1),
            ("media_player", 5, 0, 2, 1),
            ("quick_launch_dock", 5, 2, 4, 1),
        ]
        
        for i, (comp_id, row, col, w, h) in enumerate(defaults):
            placement_id = f"place_{i+1:03d}"
            self._placements[placement_id] = ComponentPlacement(
                placement_id=placement_id,
                component_id=comp_id,
                row=row,
                column=col,
                width_cells=w,
                height_cells=h,
            )
        
        self.save()
        self.placements_changed.emit()




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
        id="school_info_class",
        display_name="学校信息",
        category="Info",
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
]