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

import json
import logging
import os
import sqlite3
import sys
from typing import Any, Dict, Optional

import requests

from PyQt6.QtCore import Qt
from qfluentwidgets import BodyLabel, MessageBoxBase, SearchLineEdit, SubtitleLabel, ListWidget

from core.config import cfg
from core.constants import BASE_DIR, get_resPath
from core.utils import tr

logger = logging.getLogger("ClassLively.services.weather")

WEATHER_API_URL = "https://weatherapi.market.xiaomi.com/wtr-v3/weather/all"
WEATHER_API_APPKEY = "weather20151024"
WEATHER_API_SIGN = "zUFJoAR2ZVrDy1vF3D07"


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

    ICON_MAP = {
        0: "0.svg", 1: "1.svg", 2: "2.svg", 3: "7.svg", 4: "4.svg",
        5: "5.svg", 6: "19.svg", 7: "7.svg", 8: "8.svg", 9: "9.svg",
        10: "10.svg", 11: "11.svg", 12: "11.svg", 13: "14.svg", 14: "14.svg",
        15: "15.svg", 16: "16.svg", 17: "17.svg", 18: "18.svg", 19: "19.svg",
        20: "20.svg", 21: "7.svg", 22: "8.svg", 23: "9.svg", 24: "10.svg",
        25: "11.svg", 26: "14.svg", 27: "15.svg", 28: "16.svg", 29: "18.svg",
        30: "20.svg", 31: "20.svg", 32: "3.svg", 33: "3.svg", 34: "16.svg",
        35: "18.svg", 50: "0.svg", 51: "1.svg", 52: "2.svg", 53: "18.svg",
        54: "7.svg", 55: "8.svg", 56: "9.svg", 57: "10.svg", 58: "4.svg",
        59: "5.svg", 60: "14.svg", 61: "15.svg", 62: "16.svg", 63: "18.svg",
        64: "18.svg", 65: "18.svg", 66: "3.svg", 67: "3.svg", 68: "11.svg",
        69: "17.svg", 70: "19.svg", 71: "19.svg", 72: "18.svg", 73: "18.svg",
        74: "20.svg", 75: "20.svg", 76: "18.svg", 77: "20.svg", 99: "0.svg",
    }

    # 天气代码 → 翻译键 映射
    WEATHER_TEXT_MAP = {
        0: "weather.sunny", 1: "weather.cloudy", 2: "weather.overcast", 3: "weather.shower", 4: "weather.thundershower",
        5: "weather.thundershower_with_hail", 6: "weather.sleet", 7: "weather.light_rain", 8: "weather.moderate_rain",
        9: "weather.heavy_rain", 10: "weather.rainstorm", 11: "weather.heavy_rainstorm", 12: "weather.extreme_rainstorm",
        13: "weather.snow_flurry", 14: "weather.light_snow", 15: "weather.moderate_snow", 16: "weather.heavy_snow", 17: "weather.snowstorm",
        18: "weather.fog", 19: "weather.freezing_rain", 20: "weather.sandstorm",
        29: "weather.dust", 30: "weather.sand", 31: "weather.strong_sandstorm",
        32: "weather.squall", 33: "weather.tornado", 34: "weather.weak_blowing_snow", 35: "weather.light_fog",
        53: "weather.haze",
        99: "weather.unknown",
    }

    # 天气代码 → 组合翻译键 映射（拼接两个翻译）
    WEATHER_COMBINED_TEXT_MAP = {
        21: ("weather.light_rain", "weather.moderate_rain"),
        22: ("weather.moderate_rain", "weather.heavy_rain"),
        23: ("weather.heavy_rain", "weather.rainstorm"),
        24: ("weather.rainstorm", "weather.heavy_rainstorm"),
        25: ("weather.heavy_rainstorm", "weather.extreme_rainstorm"),
        26: ("weather.light_snow", "weather.moderate_snow"),
        27: ("weather.moderate_snow", "weather.heavy_snow"),
        28: ("weather.heavy_snow", "weather.snowstorm"),
    }

    # 夜间天气代码 → 对应的白天翻译键
    WEATHER_NIGHT_MAP = {
        50: "weather.sunny", 51: "weather.cloudy", 52: "weather.overcast",
        54: "weather.light_rain", 55: "weather.moderate_rain", 56: "weather.heavy_rain", 57: "weather.rainstorm",
        58: "weather.thundershower", 59: "weather.hail", 60: "weather.light_snow", 61: "weather.moderate_snow",
        62: "weather.heavy_snow", 63: "weather.fog", 64: "weather.haze", 65: "weather.sand_dust",
        66: "weather.strong_wind", 67: "weather.typhoon", 68: "weather.rainstorm", 69: "weather.snowstorm",
        70: "weather.sleet", 71: "weather.freezing_rain", 72: "weather.rime", 73: "weather.frost",
        74: "weather.sandstorm", 75: "weather.sand", 76: "weather.dust", 77: "weather.strong_sandstorm",
    }

    @staticmethod
    def get_weather_text(code, tr_func):
        """获取翻译后的天气文本"""
        if code in WeatherService.WEATHER_TEXT_MAP:
            return tr_func(WeatherService.WEATHER_TEXT_MAP[code])
        if code in WeatherService.WEATHER_COMBINED_TEXT_MAP:
            k1, k2 = WeatherService.WEATHER_COMBINED_TEXT_MAP[code]
            return f"{tr_func(k1)} - {tr_func(k2)}"
        if code in WeatherService.WEATHER_NIGHT_MAP:
            return f"{tr_func(WeatherService.WEATHER_NIGHT_MAP[code])}({tr_func('weather.night')})"
        return tr_func("weather.unknown")

    @staticmethod
    def build_weather_code_map(tr_func):
        """天气代码→翻译文本映射"""
        result = {}
        all_codes = set()
        all_codes.update(WeatherService.WEATHER_TEXT_MAP.keys())
        all_codes.update(WeatherService.WEATHER_COMBINED_TEXT_MAP.keys())
        all_codes.update(WeatherService.WEATHER_NIGHT_MAP.keys())
        for code in all_codes:
            result[code] = WeatherService.get_weather_text(code, tr_func)
        return result

    def __init__(self, city_code: str = "101010100"):
        self.city_code = city_code
        self.base_url = WEATHER_API_URL
        self.api_params = {
            "appKey": WEATHER_API_APPKEY,
            "sign": WEATHER_API_SIGN,
            "isGlobal": False,
            "locale": "zh_cn"
        }

    def set_city_code(self, city_code: str):
        self.city_code = city_code

    def fetch_all(self) -> Optional[Dict[str, Any]]:
        """请求天气数据"""
        try:
            lat = cfg.latitude.value if cfg.latitude.value else 39.9042
            lon = cfg.longitude.value if cfg.longitude.value else 116.4074
            
            # 仅经纬度请求
            params = {
                **self.api_params,
                "latitude": str(lat),
                "longitude": str(lon)
            }

            logger.info(f"请求天气API，经纬度：{lat}, {lon}")
            response = requests.get(self.base_url, params=params, timeout=10)

            if response.status_code != 200:
                logger.error(f"天气 API 请求失败，状态码：{response.status_code}")
                return None

            data = response.json()
            logger.info(f"[API] {json.dumps(data, ensure_ascii=False)}")

            if 'current' not in data:
                logger.error("天气返回不完整")
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

            weather_text, _ = self.WEATHER_MAP.get(weather_code, ("未知", "2.svg"))

            logger.info(f"天气数据获取成功")
            return data

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

    @staticmethod
    def parse_hourly(hourly: dict) -> Optional[Dict[str, Any]]:
        """解析 forecastHourly"""
        if not hourly:
            return None

        temps_obj = hourly.get("temperature", {})
        weathers_obj = hourly.get("weather", {})
        temp_values = temps_obj.get("value", [])
        weather_values = weathers_obj.get("value", [])
        temp_unit = temps_obj.get("unit", "℃")
        pub_time = temps_obj.get("pubTime", "")

        hours = []
        for i in range(min(24, len(temp_values))):
            weather_code = 0
            if i < len(weather_values):
                val = weather_values[i]
                try:
                    weather_code = int(val)
                except (ValueError, TypeError):
                    if isinstance(val, dict):
                        weather_code = int(val.get("day", val.get("night", 0)))
                    elif isinstance(val, list) and len(val) > 0:
                        weather_code = int(val[0])

            icon_name = WeatherService.ICON_MAP.get(weather_code, "2.svg")
            hours.append({
                "temp": temp_values[i] if i < len(temp_values) else "--",
                "weather_code": weather_code,
                "icon": icon_name,
            })

        return {
            "hours": hours,
            "unit": temp_unit,
            "pub_time": pub_time,
        }

    @staticmethod
    def parse_daily(daily: dict) -> Optional[Dict[str, Any]]:
        """解析 forecastDaily"""
        if not daily:
            return None

        weather_obj = daily.get("weather", {})
        weather_values = weather_obj.get("value", [])

        temp_obj = daily.get("temperature", {})
        temp_values = temp_obj.get("value", [])

        days = []
        for i in range(min(15, len(temp_values))):
            weather_code = 0
            if i < len(weather_values):
                val = weather_values[i]
                try:
                    weather_code = int(val)
                except (ValueError, TypeError):
                    if isinstance(val, dict):
                        # API 返回 {'from': 白天code, 'to': 夜间code} 或 {'day': ..., 'night': ...}
                        weather_code = int(val.get("from", val.get("day", val.get("night", 0))))
                    elif isinstance(val, list) and len(val) > 0:
                        weather_code = int(val[0])

            high = "--"
            low = "--"
            if i < len(temp_values):
                val = temp_values[i]
                if isinstance(val, dict):
                    # API 返回 {'from': 高温, 'to': 低温} (与常识相反)
                    from_val = val.get("from", val.get("value", "--"))
                    to_val = val.get("to", val.get("value", "--"))
                    # 根据数值大小判断高低
                    try:
                        from_num = float(from_val)
                        to_num = float(to_val)
                        high = str(max(from_num, to_num))
                        low = str(min(from_num, to_num))
                    except (ValueError, TypeError):
                        high = str(from_val)
                        low = str(to_val)
                elif isinstance(val, (list, tuple)):
                    low = str(val[0]) if len(val) > 0 else "--"
                    high = str(val[1]) if len(val) > 1 else str(val[0])
                else:
                    high = str(val)
                    low = str(val)

            icon_name = WeatherService.ICON_MAP.get(weather_code, "2.svg")
            days.append({
                "weather_code": weather_code,
                "icon": icon_name,
                "high": high,
                "low": low,
            })

        return {"days": days}


class RegionDatabase:
    """地区数据管理器"""

    def __init__(self):
        self._db_path = os.path.join(BASE_DIR, 'data', 'city.db')
        if not os.path.exists(self._db_path):
            self._db_path = get_resPath(os.path.join('data', 'city.db'))

    def _connect(self):
        return sqlite3.connect(self._db_path)

    def search(self, keyword=None):
        try:
            if not os.path.exists(self._db_path):
                return []

            with self._connect() as conn:
                cursor = conn.cursor()
                if keyword is None or len(keyword.strip()) == 0:
                    cursor.execute('SELECT name FROM regions')
                else:
                    cursor.execute(
                        'SELECT name FROM regions WHERE name LIKE ?',
                        ('%' + keyword + '%',)
                    )
                names = [row[0] for row in cursor.fetchall()]
                
                # 按首字拼音首字母排序
                try:
                    from pypinyin import lazy_pinyin
                    names.sort(key=lambda x: lazy_pinyin(x[0])[0][0].lower() if x else '')
                except ImportError:
                    names.sort()
                
                return names
        except Exception as err:
            logger.error(f'搜索地区出错：{err}')
            return []

    def get_coordinates(self, region_name):
        """获取地区的经纬度"""
        try:
            if not os.path.exists(self._db_path):
                return None, None

            with self._connect() as conn:
                cursor = conn.cursor()
                cursor.execute('SELECT longitude, latitude FROM regions WHERE name = ?', (region_name,))
                result = cursor.fetchone()
                if result:
                    return result[0], result[1]
                return None, None
        except Exception as err:
            logger.error(f'获取经纬度失败：{err}')
            return None, None

    def get_code(self, region_name):
        """原城市代码获取 弃"""
        return ''

    def get_name(self, region_code):
        # try:
        #     if not os.path.exists(self._db_path):
        #         return ''

        #     code = region_code
        #     if code and code.startswith('weathercn:'):
        #         code = code[10:]

        #     with self._connect() as conn:
        #         cursor = conn.cursor()
        #         cursor.execute('SELECT name FROM citys WHERE city_num LIKE ?', ('%' + code + '%',))
        #         result = cursor.fetchone()
        #         return result[0] if result else ''
        # except Exception as err:
        #     # print(f'通过代码获取地区名失败：{err}')
        #     logger.error(f'获取地区名失败：{err}')
        #     return ''
        return


class RegionSelectorDialog(MessageBoxBase):
    """地区选择对话框"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self._database = RegionDatabase()
        self._init_ui()
        self._select_current()

    def _init_ui(self):
        title = SubtitleLabel()
        title.setText(tr("weather_service.select_region"))

        self._search_input = SearchLineEdit()
        self._search_input.setPlaceholderText(tr("weather_service.region_placeholder"))
        self._search_input.setClearButtonEnabled(True)
        self._search_input.textChanged.connect(self._on_search)

        self._region_list = ListWidget()
        self._refresh_list()
        self._region_list.itemDoubleClicked.connect(self._on_double_click)

        self.viewLayout.addWidget(title)
        self.viewLayout.addWidget(self._search_input)
        self.viewLayout.addWidget(self._region_list)

        self.yesButton.setText(tr("common.confirm"))  # 确定
        self.cancelButton.setText(tr("common.cancel"))  # 取消

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
            # print(f'选中当前地区失败：{err}')
            logger.warning(f'选中当前地区失败：{err}')

    def get_selected_region(self):
        selected = self._region_list.selectedItems()
        return selected[0].text() if selected else None
