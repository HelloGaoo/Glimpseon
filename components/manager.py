"""
组件管理器模块

负责管理所有组件的创建、保存、加载、删除
"""

from typing import Dict, Any, List, Optional, Type
from PyQt5.QtWidgets import QWidget

from .base import BaseComponent
from .clock import ClockComponent
from .weather import WeatherComponent
from .poetry import PoetryComponent


class ComponentManager:
    """组件管理器类"""
    
    # 组件类型映射
    COMPONENT_CLASSES: Dict[str, Type[BaseComponent]] = {
        '时钟': ClockComponent,
        '天气': WeatherComponent,
        '诗词': PoetryComponent
    }
    
    def __init__(self, parent: QWidget, config_manager):
        """
        初始化组件管理器
        
        Args:
            parent: 父组件（主窗口）
            config_manager: 配置管理器实例
        """
        self.parent = parent
        self.config_manager = config_manager
        self.components: Dict[BaseComponent, Dict[str, Any]] = {}  # 组件 -> 配置
    
    def create_component(self, component_type: str, config: Optional[Dict[str, Any]] = None) -> Optional[BaseComponent]:
        """
        创建组件
        
        Args:
            component_type: 组件类型
            config: 组件配置
            
        Returns:
            创建的组件，失败返回 None
        """
        if component_type not in self.COMPONENT_CLASSES:
            return None
        
        component_class = self.COMPONENT_CLASSES[component_type]
        component = component_class(self.parent)
        
        if config:
            component.load_config(config)
        
        widget = component.create_widget()
        
        # 设置默认大小
        if component_type == '时钟':
            widget.resize(300, 150)
        elif component_type == '天气':
            widget.resize(200, 100)
        elif component_type == '诗词':
            widget.resize(400, 120)
        
        # 立即更新内容
        component.update_content()
        
        # 如果是时钟，启动定时器
        if component_type == '时钟' and hasattr(component, 'start_timer'):
            component.start_timer()
        
        return component
    
    def load_all_components(self, home_content: QWidget) -> List[BaseComponent]:
        """
        加载所有组件
        
        Args:
            home_content: 主界面内容容器
            
        Returns:
            加载的组件列表
        """
        components_data = self.config_manager.load_components()
        loaded_components = []
        
        for comp_data in components_data:
            component_type = comp_data.get('type', '')
            if component_type not in self.COMPONENT_CLASSES:
                continue
            
            component = self.create_component(component_type, comp_data)
            if not component:
                continue
            
            widget = component.widget
            widget.setParent(home_content)
            
            # 设置位置
            x = comp_data.get('x', 0)
            y = comp_data.get('y', 0)
            widget.move(x, y)
            widget.show()
            
            self.components[component] = comp_data
            loaded_components.append(component)
        
        return loaded_components
    
    def save_all_components(self, home_content: QWidget) -> bool:
        """
        保存所有组件
        
        Args:
            home_content: 主界面内容容器
            
        Returns:
            是否保存成功
        """
        components_data = []
        
        # 收集所有组件的配置
        for component in self.components.keys():
            widget = component.widget
            if not widget or widget.parent() != home_content:
                continue
            
            config = component.get_config()
            config['x'] = widget.x()
            config['y'] = widget.y()
            config['width'] = widget.width()
            config['height'] = widget.height()
            
            components_data.append(config)
        
        # 保存到文件
        return self.config_manager.save_components(components_data)
    
    def delete_component(self, component: BaseComponent) -> bool:
        """
        删除组件
        
        Args:
            component: 要删除的组件
            
        Returns:
            是否删除成功
        """
        if component not in self.components:
            return False
        
        # 从管理器中移除
        del self.components[component]
        
        # 清理组件资源
        component.cleanup()
        
        return True
    
    def get_component_by_widget(self, widget: QWidget) -> Optional[BaseComponent]:
        """
        根据 widget 获取组件
        
        Args:
            widget: QWidget
            
        Returns:
            对应的组件，找不到返回 None
        """
        for component in self.components.keys():
            if component.widget == widget or component.widget.isAncestorOf(widget):
                return component
        return None
    
    def cleanup_all(self):
        """清理所有组件"""
        for component in list(self.components.keys()):
            component.cleanup()
        self.components.clear()
