"""
天气组件模块

显示实时天气数据（温度、天气状况、图标）
"""

from PyQt5.QtWidgets import QWidget, QLabel, QHBoxLayout
from PyQt5.QtCore import Qt
from PyQt5.QtGui import QPixmap
from typing import Dict, Any, Optional
import sys
import os

from .base import ContainerComponent
from services.weather_api import WeatherAPIService


class WeatherComponent(ContainerComponent):
    """天气组件类"""
    
    COMPONENT_TYPE = "天气"
    
    def __init__(self, parent: Optional[QWidget] = None,
                 city_code: str = "101010100",
                 font_size: int = 24,
                 color: str = "#FFFFFF"):
        """
        初始化天气组件
        
        Args:
            parent: 父组件
            city_code: 城市代码
            font_size: 字体大小
            color: 字体颜色
        """
        super().__init__(parent)
        self.city_code = city_code
        self.font_size = font_size
        self.color = color
        
        self.weather_service = WeatherAPIService(city_code)
        
        self.temp_label: Optional[QLabel] = None
        self.icon_label: Optional[QLabel] = None
    
    def create_widget(self) -> QWidget:
        """创建天气 UI"""
        container = self._create_container('horizontal')
        
        # 温度标签
        self.temp_label = QLabel("")
        self.temp_label.setAlignment(Qt.AlignCenter)
        self.temp_label.setStyleSheet(f"""
            color: {self.color};
            font-size: {self.font_size}px;
            font-family: "HarmonyOS Sans SC", "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)
        
        # 天气图标标签
        self.icon_label = QLabel("")
        self.icon_label.setAlignment(Qt.AlignCenter)
        self.icon_label.setStyleSheet("background-color: transparent;")
        self.icon_label.setFixedSize(64, 64)
        
        self.add_widget_to_layout(self.temp_label)
        self.add_widget_to_layout(self.icon_label)
        
        self.widget = container
        return container
    
    def update_content(self):
        """更新天气数据"""
        if not self.temp_label or not self.icon_label:
            return
        
        weather_data = self.weather_service.get_weather()
        
        if weather_data:
            # 更新温度
            self.temp_label.setText(weather_data['temperature'])
            
            # 更新图标
            icon_path = self._get_icon_path(weather_data['weather_icon'])
            if os.path.exists(icon_path):
                pixmap = QPixmap(icon_path).scaled(
                    64, 64,
                    Qt.KeepAspectRatio,
                    Qt.SmoothTransformation
                )
                self.icon_label.setPixmap(pixmap)
    
    def _get_icon_path(self, icon_name: str) -> str:
        """
        获取图标路径
        
        Args:
            icon_name: 图标文件名
            
        Returns:
            图标完整路径
        """
        if getattr(sys, 'frozen', False):
            base_dir = os.path.dirname(sys.executable)
        else:
            base_dir = os.path.dirname(os.path.abspath(__file__))
            # 向上两级目录
            base_dir = os.path.dirname(os.path.dirname(base_dir))
        
        return os.path.join(base_dir, 'resource', 'icons', 'weather', icon_name)
    
    def set_city_code(self, city_code: str):
        """
        设置城市代码
        
        Args:
            city_code: 城市代码
        """
        self.city_code = city_code
        self.weather_service.set_city_code(city_code)
    
    def get_config(self) -> Dict[str, Any]:
        """获取组件配置"""
        return {
            'type': self.COMPONENT_TYPE,
            'city_code': self.city_code,
            'font_size': self.font_size,
            'color': self.color
        }
    
    def load_config(self, config: Dict[str, Any]):
        """加载组件配置"""
        self.city_code = config.get('city_code', "101010100")
        self.font_size = config.get('font_size', 24)
        self.color = config.get('color', "#FFFFFF")
        self.weather_service.set_city_code(self.city_code)
