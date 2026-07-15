# Glimpseon
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
常量定义模块

路径常量从 paths.py 导入，确保所有模块使用相同的路径
"""

import os

from qfluentwidgets import isDarkTheme

# 从 paths.py 导入所有路径常量
from core.paths import (
    PACKAGE_ROOT, APP_DIR, MEIPASS_DIR, BASE_DIR,
    DATA_ROOT, DATA_CONFIG, DATA_LOG, DATA_CACHE, DATA_TEMP,
    DATA_PROFILE, DATA_USER, DATA_ICON, DATA_WALLPAPER, DATA_CLASSPHOTOS, DATA_NOTES,
    WALLPAPER_DIR, VERSION, BUILD_DATE,
    ensure_data_dirs, get_resource_path
)

# ============================================================
# 应用信息
# ============================================================
APP_NAME = "Glimpseon"
APP_ICON = "resource/icons/CY.png"
APP_LICENSE = "LICENSE"

EXTERNAL_CLASSWIDGETS = "ClassWidgets"
EXTERNAL_CLASSISLAND = "ClassIsland"

TIMETABLE_SOURCES = ["Glimpseon", "ClassIsland", "ClassWidgets"]
TIMETABLE_SOURCE_GLIMPSEON = "Glimpseon"
TIMETABLE_SOURCE_CLASSISLAND = "classisland"
TIMETABLE_SOURCE_CLASSWIDGETS = "classwidgets"

RESOURCE_ROOT = "resource"
RESOURCE_ICONS = "resource/icons"
RESOURCE_QSS = "resource/qss"
RESOURCE_WALLPAPER = "resource/wallpaper"
RESOURCE_CITY_DB = "resource/city.db"
RESOURCE_CREDITS = "resource/credits.json"
RESOURCE_DEFAULT_WALLPAPER = "resource/wallpaper/default.jpg"

NEWS_ICONS = {
    "baidu": "resource/icons/news/baidu.svg",
    "weibo": "resource/icons/news/weibo.svg",
    "jinritoutiao": "resource/icons/news/jinritoutiao.svg",
    "tencent": "resource/icons/news/tencent.svg",
    "cctv": "resource/icons/news/cctv.svg",
}

get_resPath = get_resource_path

_qss_cache = {}


def clear_qss_cache():
    """清空 QSS 缓存"""
    _qss_cache.clear()


def load_qss(qss_filename: str) -> str:
    """加载 QSS 样式文件"""
    theme = 'dark' if isDarkTheme() else 'light'
    cache_key = (theme, qss_filename)
    if cache_key in _qss_cache:
        return _qss_cache[cache_key]

    qss_path = get_resource_path(os.path.join(RESOURCE_QSS, theme, qss_filename))
    if not os.path.exists(qss_path):
        return ''
    try:
        with open(qss_path, 'r', encoding='utf-8') as f:
            content = f.read()
        _qss_cache[cache_key] = content
        return content
    except:
        return ''