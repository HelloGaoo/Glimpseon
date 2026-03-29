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
诗词组件模块

显示随机诗词
"""

from PyQt5.QtWidgets import QWidget, QLabel
from PyQt5.QtCore import Qt
from typing import Dict, Any, Optional

from .base import BaseComponent
from services.poetry_api import PoetryAPIService


class PoetryComponent(BaseComponent):
    """诗词组件类"""
    
    COMPONENT_TYPE = "诗词"
    
    def __init__(self, parent: Optional[QWidget] = None,
                 api_url: str = "https://www.ffapi.cn/int/v1/shici",
                 font_size: int = 16,
                 color: str = "#FFFFFF"):
        """
        初始化诗词组件
        
        Args:
            parent: 父组件
            api_url: API 地址
            font_size: 字体大小
            color: 字体颜色
        """
        super().__init__(parent)
        self.api_url = api_url
        self.font_size = font_size
        self.color = color
        
        self.poetry_service = PoetryAPIService(api_url)
        self.label: Optional[QLabel] = None
    
    def create_widget(self) -> QWidget:
        """创建诗词 UI"""
        self.label = QLabel("")
        self.label.setAlignment(Qt.AlignCenter)
        self.label.setStyleSheet(f"""
            color: {self.color};
            font-size: {self.font_size}px;
            font-family: "HarmonyOS Sans SC", "HarmonyOS Sans", "Microsoft YaHei", "SimHei", sans-serif;
            background-color: transparent;
        """)
        self.label.setWordWrap(True)
        
        self.widget = self.label
        return self.label
    
    def update_content(self):
        """更新诗词内容"""
        if not self.label:
            return
        
        poetry_data = self.poetry_service.get_poetry()
        
        if poetry_data:
            poetry_text = self.poetry_service.format_poetry(poetry_data)
            self.label.setText(poetry_text)
    
    def set_api_url(self, api_url: str):
        """
        设置 API 地址
        
        Args:
            api_url: API 地址
        """
        self.api_url = api_url
        self.poetry_service.set_api_url(api_url)
    
    def get_config(self) -> Dict[str, Any]:
        """获取组件配置"""
        return {
            'type': self.COMPONENT_TYPE,
            'api_url': self.api_url,
            'font_size': self.font_size,
            'color': self.color
        }
    
    def load_config(self, config: Dict[str, Any]):
        """加载组件配置"""
        self.api_url = config.get('api_url', "https://www.ffapi.cn/int/v1/shici")
        self.font_size = config.get('font_size', 16)
        self.color = config.get('color', "#FFFFFF")
        self.poetry_service.set_api_url(self.api_url)
