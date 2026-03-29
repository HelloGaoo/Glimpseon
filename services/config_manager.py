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
配置管理器模块

负责管理应用程序的所有配置文件，包括：
- 主配置文件 (config.json)
- 组件配置文件 (home_components.json)
"""

import json
import os
import logging
from typing import Dict, List, Any, Optional

logger = logging.getLogger(__name__)


class ConfigManager:
    """配置管理器类"""
    
    # 配置文件版本
    CONFIG_VERSION = "1.0"
    COMPONENTS_CONFIG_VERSION = "1.0"
    
    def __init__(self, base_dir: str):
        """
        初始化配置管理器
        
        Args:
            base_dir: 应用程序基础目录
        """
        self.base_dir = base_dir
        self.config_dir = os.path.join(base_dir, 'config')
        self.config_file = os.path.join(self.config_dir, 'config.json')
        self.components_file = os.path.join(self.config_dir, 'home_components.json')
        
        # 确保配置目录存在
        os.makedirs(self.config_dir, exist_ok=True)
    
    # ========== 主配置管理 ==========
    
    def load_config(self) -> Dict[str, Any]:
        """
        加载主配置文件
        
        Returns:
            配置字典
        """
        if not os.path.exists(self.config_file):
            logger.info("主配置文件不存在，使用默认配置")
            return self._get_default_config()
        
        try:
            with open(self.config_file, 'r', encoding='utf-8') as f:
                config = json.load(f)
            
            # 版本检查
            version = config.get('version', '0.0')
            if version != self.CONFIG_VERSION:
                logger.warning(f"配置文件版本不匹配：{version}，当前版本：{self.CONFIG_VERSION}")
                config = self._migrate_config(config)
            
            logger.info("主配置文件加载成功")
            return config
        except Exception as e:
            logger.error(f"加载主配置文件失败：{e}")
            return self._get_default_config()
    
    def save_config(self, config: Dict[str, Any]) -> bool:
        """
        保存主配置文件
        
        Args:
            config: 配置字典
            
        Returns:
            是否保存成功
        """
        try:
            config['version'] = self.CONFIG_VERSION
            
            os.makedirs(os.path.dirname(self.config_file), exist_ok=True)
            with open(self.config_file, 'w', encoding='utf-8') as f:
                json.dump(config, f, ensure_ascii=False, indent=4)
            
            logger.info("主配置文件保存成功")
            return True
        except Exception as e:
            logger.error(f"保存主配置文件失败：{e}")
            return False
    
    def _get_default_config(self) -> Dict[str, Any]:
        """获取默认配置"""
        return {
            'version': self.CONFIG_VERSION,
            'Other': {
                'AllowMultipleInstances': False
            }
        }
    
    def _migrate_config(self, old_config: Dict[str, Any]) -> Dict[str, Any]:
        """
        配置迁移
        
        Args:
            old_config: 旧版本配置
            
        Returns:
            新版本配置
        """
        logger.info("正在迁移配置文件...")
        # 这里可以添加版本迁移逻辑
        return old_config
    
    # ========== 组件配置管理 ==========
    
    def load_components(self) -> List[Dict[str, Any]]:
        """
        加载组件配置文件
        
        Returns:
            组件列表
        """
        if not os.path.exists(self.components_file):
            logger.info("组件配置文件不存在")
            return []
        
        try:
            with open(self.components_file, 'r', encoding='utf-8') as f:
                data = json.load(f)
            
            # 版本检查
            version = data.get('version', '0.0')
            if version != self.COMPONENTS_CONFIG_VERSION:
                logger.warning(f"组件配置文件版本不匹配：{version}")
            
            components = data.get('components', [])
            logger.info(f"加载了 {len(components)} 个组件")
            return components
        except Exception as e:
            logger.error(f"加载组件配置文件失败：{e}")
            return []
    
    def save_components(self, components: List[Dict[str, Any]]) -> bool:
        """
        保存组件配置文件
        
        Args:
            components: 组件列表
            
        Returns:
            是否保存成功
        """
        try:
            data = {
                'version': self.COMPONENTS_CONFIG_VERSION,
                'components': components
            }
            
            os.makedirs(os.path.dirname(self.components_file), exist_ok=True)
            with open(self.components_file, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=4)
            
            logger.info(f"保存了 {len(components)} 个组件")
            return True
        except Exception as e:
            logger.error(f"保存组件配置文件失败：{e}")
            return False
    
    # ========== 工具方法 ==========
    
    def get_config_value(self, key: str, default: Any = None) -> Any:
        """
        获取配置值
        
        Args:
            key: 配置键
            default: 默认值
            
        Returns:
            配置值
        """
        config = self.load_config()
        keys = key.split('.')
        value = config
        for k in keys:
            if isinstance(value, dict) and k in value:
                value = value[k]
            else:
                return default
        return value
    
    def set_config_value(self, key: str, value: Any) -> bool:
        """
        设置配置值
        
        Args:
            key: 配置键
            value: 配置值
            
        Returns:
            是否设置成功
        """
        config = self.load_config()
        keys = key.split('.')
        current = config
        for k in keys[:-1]:
            if k not in current:
                current[k] = {}
            current = current[k]
        current[keys[-1]] = value
        return self.save_config(config)
