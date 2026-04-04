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
天气服务模块
"""

import os
import sys
import requests
import logging
from typing import Optional, Dict, Any
from core.logger import logger

logger = logging.getLogger(__name__)


class WeatherService:
    """天气 API 服务类"""
    
    # 天气代码映射表
    WEATHER_MAP = {
        0: ("晴", "0.svg"),
        1: ("多云", "1.svg"),
        2: ("阴", "2.svg"),
        3: ("阵雨", "3.svg"),
        4: ("雷阵雨", "4.svg"),
        5: ("雷阵雨并伴有冰雹", "5.svg"),
        6: ("雨夹雪", "6.svg"),
        7: ("小雨", "7.svg"),
        8: ("中雨", "8.svg"),
        9: ("大雨", "9.svg"),
        10: ("暴雨", "10.svg"),
        11: ("大暴雨", "11.svg"),
        12: ("特大暴雨", "12.svg"),
        13: ("阵雪", "13.svg"),
        14: ("小雪", "14.svg"),
        15: ("中雪", "15.svg"),
        16: ("大雪", "16.svg"),
        17: ("暴雪", "17.svg"),
        18: ("雾", "18.svg"),
        19: ("冻雨", "19.svg"),
        20: ("沙尘暴", "20.svg"),
    }
    
    def __init__(self, city_code: str = "101010100"):
        """初始化"""
        self.city_code = city_code
        self.base_url = "https://weatherapi.market.xiaomi.com/wtr-v3/weather/all"
        self.api_params = {
            "appKey": "weather20151024",
            "sign": "zUFJoAR2ZVrDy1vF3D07",
            "isGlobal": False,
            "locale": "zh_cn"
        }
    
    def set_city_code(self, city_code: str):
        """设置城市代码·"""
        self.city_code = city_code
    
    def get_weather(self) -> Optional[Dict[str, Any]]:
        """
        获取天气数据
        
        Returns:
            天气数据字典，包含 temperature, weather_text, weather_code 等
            如果获取失败返回 None
        """
        try:
            location_key = f"weathercn:{self.city_code}"
            params = {
                **self.api_params,
                "locationKey": location_key,
                "latitude": "39.9042",
                "longitude": "116.4074"
            }
            
            logger.info(f"正在请求天气数据，城市代码：{self.city_code}")
            response = requests.get(self.base_url, params=params, timeout=10)
            
            if response.status_code != 200:
                logger.error(f"天气 API 请求失败，状态码：{response.status_code}")
                return None
            
            data = response.json()
            logger.debug(f"天气 API 响应数据：{data}")
            
            if 'current' not in data:
                logger.error("天气数据中缺少 current 字段")
                return None
            
            current = data['current']
            
            # 解析温度
            temperature = current.get('temperature', {})
            temp_value = temperature.get('value', 0)
            temp_unit = temperature.get('unit', '°C')
            
            # 解析天气状况
            weather_code = current.get('weather', 0)
            try:
                weather_code = int(weather_code)
            except (ValueError, TypeError):
                weather_code = 0
                logger.warning(f"天气代码无效：{weather_code}")
            
            # 获取天气文本和图标
            weather_text, weather_icon = self.WEATHER_MAP.get(weather_code, ("未知", "2.svg"))
            
            result = {
                'temperature': f"{temp_value}{temp_unit}",
                'weather_text': weather_text,
                'weather_code': weather_code,
                'weather_icon': weather_icon
            }
            
            logger.info(f"天气数据获取成功：{result}")
            return result
            
        except requests.exceptions.Timeout:
            logger.error("天气 API 请求超时")
            return None
        except requests.exceptions.RequestException as e:
            logger.error(f"天气 API 请求异常：{e}")
            return None
        except Exception as e:
            logger.error(f"解析天气数据失败：{e}")
            return None
    
    @staticmethod
    def get_weather_icon_path(icon_name: str) -> str:
        """
        获取天气图标路径
        
        Args:
            icon_name: 图标文件名
            
        Returns:
            图标完整路径
        """
        base_dir = None
        if getattr(sys, 'frozen', False):
            base_dir = os.path.dirname(sys.executable)
        else:
            base_dir = os.path.dirname(os.path.abspath(__file__))
            # 向上一级目录
            base_dir = os.path.dirname(base_dir)
        
        return os.path.join(base_dir, 'resource', 'icons', 'weather', icon_name)
