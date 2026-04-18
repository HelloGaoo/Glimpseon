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
import json
import os
import logging
from core.constants import BASE_DIR

logger = logging.getLogger(__name__)
QUICK_LAUNCH_CONFIG_PATH = os.path.join(BASE_DIR, 'config', 'quick_launch.json')


class QuickLaunchConfig:
    def __init__(self):
        self.show_quick_launch = True
        self.quick_launch_apps = []
        self.load()
    
    def load(self):
        if not os.path.exists(QUICK_LAUNCH_CONFIG_PATH):
            logger.info("快捷启动配置文件不存在，使用默认配置")
            self._create_default_config()
            return
        try:
            with open(QUICK_LAUNCH_CONFIG_PATH, 'r', encoding='utf-8') as f:
                data = json.load(f)
            self.show_quick_launch = data.get('ShowQuickLaunch', True)
            self.quick_launch_apps = data.get('QuickLaunchApps', [])
            logger.info(f"已从 {QUICK_LAUNCH_CONFIG_PATH} 加载快捷启动配置")
        except Exception as e:
            logger.error(f"加载快捷启动配置失败：{e}")
            self._create_default_config()
    
    def save(self):
        """保存配置文件"""
        try:
            config_dir = os.path.dirname(QUICK_LAUNCH_CONFIG_PATH)
            if not os.path.exists(config_dir):os.makedirs(config_dir)
            data = {
                'ShowQuickLaunch': self.show_quick_launch,
                'QuickLaunchApps': self.quick_launch_apps
            }
            with open(QUICK_LAUNCH_CONFIG_PATH, 'w', encoding='utf-8') as f:json.dump(data, f, ensure_ascii=False, indent=4)
            logger.info(f"已保存快捷启动配置到 {QUICK_LAUNCH_CONFIG_PATH}")
            return True
        except Exception as e:
            logger.error(f"保存快捷启动配置失败：{e}")
            return False
    
    def _create_default_config(self):
        self.show_quick_launch = True
        self.quick_launch_apps = [
            {
                "name": "1",
                "path": "",
                "icon": "1.ico"
            },
            {
                "name": "2",
                "path": "",
                "icon": "2.ico"
            },
            {
                "name": "3",
                "path": "",
                "icon": "3.ico"
            },
            {
                "name": "4",
                "path": "",
                "icon": "4.ico"
            },
            {
                "name": "5",
                "path": "",
                "icon": "5.ico"
            }
        ]
        self.save()
    
    def set_show(self, show):
        self.show_quick_launch = show
        self.save()
    
    def set_apps(self, apps):
        self.quick_launch_apps = apps
        self.save()
    
    def remove_app(self, index):
        if 0 <= index < len(self.quick_launch_apps):
            self.quick_launch_apps.pop(index)
            self.save()
    
    def update_app(self, index, app):
        if 0 <= index < len(self.quick_launch_apps):
            self.quick_launch_apps[index] = app
            self.save()


ql_cfg = QuickLaunchConfig()
