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
天气服务模块
这个有参考classisland和classwidgets
"""

import logging
import os
import sqlite3
import sys
from typing import Any, Dict, Optional

import requests

from PyQt6.QtCore import QCoreApplication, Qt
from qfluentwidgets import BodyLabel, MessageBoxBase, SearchLineEdit, SubtitleLabel, ListWidget

from core.config import cfg
from core.constants import BASE_DIR, get_resPath
from core.logger import logger
from core.utils import tr

logger = logging.getLogger("ClassLively.services.weather")


class WeatherService:
    """天气 API 服务类"""

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
        self.city_code = city_code
        self.base_url = "https://weatherapi.market.xiaomi.com/wtr-v3/weather/all"
        self.api_params = {
            "appKey": "weather20151024",
            "sign": "zUFJoAR2ZVrDy1vF3D07",
            "isGlobal": False,
            "locale": "zh_cn"
        }

    def set_city_code(self, city_code: str):
        self.city_code = city_code

    def get_weather(self) -> Optional[Dict[str, Any]]:
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

            if 'current' not in data:
                logger.error("天气数据中缺少 current 字段")
                return None

            current = data['current']

            temperature = current.get('temperature', {})
            temp_value = temperature.get('value', 0)
            temp_unit = temperature.get('unit', '°C')

            weather_code = current.get('weather', 0)
            try:
                weather_code = int(weather_code)
            except (ValueError, TypeError):
                weather_code = 0
                logger.warning(f"天气代码无效：{weather_code}")

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
        base_dir = None
        if getattr(sys, 'frozen', False):
            base_dir = os.path.dirname(sys.executable)
        else:
            base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

        return os.path.join(base_dir, 'resource', 'icons', 'weather', icon_name)


class RegionDatabase:
    """地区数据管理器"""

    def __init__(self):
        self._db_path = get_resPath(os.path.join('data', 'xiaomi_weather.db'))
        if not os.path.exists(self._db_path):
            self._db_path = os.path.join(BASE_DIR, 'data', 'xiaomi_weather.db')

    def _connect(self):
        return sqlite3.connect(self._db_path)

    def search(self, keyword=None):
        try:
            if not os.path.exists(self._db_path):
                return []

            with self._connect() as conn:
                cursor = conn.cursor()
                if keyword is None or len(keyword.strip()) == 0:
                    cursor.execute('SELECT name FROM citys ORDER BY name')
                else:
                    cursor.execute(
                        'SELECT name FROM citys WHERE name LIKE ? ORDER BY name',
                        ('%' + keyword + '%',)
                    )
                return [row[0] for row in cursor.fetchall()]
        except Exception as err:
            print(f'搜索地区出错：{err}')
            return []

    def get_code(self, region_name):
        try:
            if not os.path.exists(self._db_path):
                return ''

            with self._connect() as conn:
                cursor = conn.cursor()
                cursor.execute('SELECT city_num FROM citys WHERE name = ?', (region_name,))
                result = cursor.fetchone()
                return result[0] if result else ''
        except Exception as err:
            print(f'获取地区代码失败：{err}')
            return ''

    def get_name(self, region_code):
        try:
            if not os.path.exists(self._db_path):
                return ''

            code = region_code
            if code and code.startswith('weathercn:'):
                code = code[10:]

            with self._connect() as conn:
                cursor = conn.cursor()
                cursor.execute('SELECT name FROM citys WHERE city_num LIKE ?', ('%' + code + '%',))
                result = cursor.fetchone()
                return result[0] if result else ''
        except Exception as err:
            print(f'通过代码获取地区名失败：{err}')
            return ''


class RegionSelectorDialog(MessageBoxBase):
    """地区选择对话框"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self._database = RegionDatabase()
        self._init_ui()
        self._select_current()

    def _init_ui(self):
        title = SubtitleLabel()
        title.setText(tr("weather.select_region"))

        self._search_input = SearchLineEdit()
        self._search_input.setPlaceholderText(tr("weather.region_placeholder"))
        self._search_input.setClearButtonEnabled(True)
        self._search_input.textChanged.connect(self._on_search)

        self._region_list = ListWidget()
        self._refresh_list()
        self._region_list.itemDoubleClicked.connect(self._on_double_click)

        self.viewLayout.addWidget(title)
        self.viewLayout.addWidget(self._search_input)
        self.viewLayout.addWidget(self._region_list)

        self.yesButton.setText(tr("common.confirm"))
        self.cancelButton.setText(tr("common.cancel"))

        self.widget.setMinimumWidth(520)
        self.widget.setMinimumHeight(620)

    def _refresh_list(self, keyword=None):
        self._region_list.clear()
        regions = self._database.search(keyword)
        self._region_list.addItems(regions)
        self._region_list.clearSelection()

    def _on_search(self, text):
        self._refresh_list(text if text.strip() else None)

    def _on_double_click(self, item):
        self.yesButton.click()

    def _select_current(self):
        try:
            current = cfg.city.value
            if current:
                items = self._region_list.findItems(current, Qt.MatchFlag.MatchExactly)
                if items:
                    self._region_list.setCurrentItem(items[0])
                    self._region_list.scrollToItem(items[0])
        except Exception as err:
            print(f'选中当前地区失败：{err}')

    def get_selected_region(self):
        selected = self._region_list.selectedItems()
        return selected[0].text() if selected else None
