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
组件基类模块

定义所有组件的通用接口和基类
"""

from PyQt5.QtWidgets import QWidget, QLabel, QVBoxLayout, QHBoxLayout
from PyQt5.QtCore import QTimer
from abc import ABC, abstractmethod
from typing import Dict, Any, Optional


class BaseComponent(ABC):
    """组件基类"""
    
    # 组件类型
    COMPONENT_TYPE = "Base"
    
    def __init__(self, parent: Optional[QWidget] = None):
        """
        初始化组件
        
        Args:
            parent: 父组件
        """
        self.parent = parent
        self.widget: Optional[QWidget] = None
        self.data: Dict[str, Any] = {}
    
    @abstractmethod
    def create_widget(self) -> QWidget:
        """
        创建组件的 UI 控件
        
        Returns:
            QWidget: 组件的 UI 控件
        """
        pass
    
    @abstractmethod
    def update_content(self):
        """
        更新组件内容
        
        子类必须实现此方法来定义如何更新内容
        """
        pass
    
    @abstractmethod
    def get_config(self) -> Dict[str, Any]:
        """
        获取组件配置
        
        Returns:
            组件配置字典
        """
        pass
    
    @abstractmethod
    def load_config(self, config: Dict[str, Any]):
        """
        加载组件配置
        
        Args:
            config: 组件配置字典
        """
        pass
    
    def cleanup(self):
        """
        清理组件资源
        
        子类可以重写此方法来清理定时器、网络连接等资源
        """
        if self.widget:
            self.widget.deleteLater()
            self.widget = None


class ContainerComponent(BaseComponent):
    """容器组件基类（用于包含多个子控件的组件）"""
    
    def __init__(self, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.container: Optional[QWidget] = None
        self.layout: Optional[QVBoxLayout] = None
    
    def _create_container(self, layout_type: str = 'vertical') -> QWidget:
        """
        创建容器
        
        Args:
            layout_type: 布局类型 ('vertical' 或 'horizontal')
            
        Returns:
            容器控件
        """
        self.container = QWidget()
        
        if layout_type == 'vertical':
            self.layout = QVBoxLayout(self.container)
        else:
            self.layout = QHBoxLayout(self.container)
        
        self.layout.setContentsMargins(10, 10, 10, 10)
        self.layout.setSpacing(5)
        self.container.setStyleSheet("background-color: transparent;")
        
        return self.container
    
    def add_widget_to_layout(self, widget: QWidget):
        """
        添加控件到布局
        
        Args:
            widget: 要添加的控件
        """
        if self.layout:
            self.layout.addWidget(widget)
