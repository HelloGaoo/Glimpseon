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

"""常量定义"""

import os

from qfluentwidgets import isDarkTheme

from core.paths import (
    PACKAGE_ROOT, APP_DIR, MEIPASS_DIR, BASE_DIR,
    DATA_ROOT, DATA_CONFIG, DATA_LOG, DATA_CACHE, DATA_TEMP,
    DATA_PROFILE, DATA_USER, DATA_ICON, DATA_WALLPAPER, DATA_CLASSPHOTOS, DATA_NOTES,
    WALLPAPER_DIR, VERSION, BUILD_DATE,
    ensure_data_dirs, get_resource_path
)

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

FONT_PRIMARY = "HarmonyOS Sans"
FONT_FAMILY = ("'HarmonyOS Sans', 'HarmonyOS Sans SC', 'HarmonyOS Sans TC', 'HarmonyOS Sans HC', "
               "'Microsoft YaHei UI', 'Microsoft YaHei', 'PingFang SC', 'Source Han Sans SC', "
               "'Segoe UI', 'Arial', sans-serif")

FALLBACK_FONT_QSS = f"""
QWidget, QLabel, QPushButton, QComboBox, QLineEdit, QTextEdit,
QPlainTextEdit, QCheckBox, QRadioButton, QGroupBox, QTabWidget,
QTabBar, QAbstractItemView, QMenu, QToolTip, QStatusBar,
QSpinBox, QDoubleSpinBox, QDateTimeEdit, QHeaderView {{
    font-family: {FONT_FAMILY};
}}
"""


def clear_qss_cache():
    from core.logger import logger as _logger
    count = len(_qss_cache)
    _qss_cache.clear()
    _logger.debug(f"已清空 QSS 缓存 ({count}项)")


def load_qss(qss_filename: str) -> str:
    from core.logger import logger as _logger
    theme = 'dark' if isDarkTheme() else 'light'
    cache_key = (theme, qss_filename)
    if cache_key in _qss_cache:
        _logger.debug(f"QSS 命中缓存: {qss_filename} ({theme})")
        return _qss_cache[cache_key]

    _logger.debug(f"QSS 缓存未命中 读取文件: {qss_filename} ({theme})")
    qss_path = get_resource_path(os.path.join(RESOURCE_QSS, theme, qss_filename))
    if not os.path.exists(qss_path):
        _logger.warning(f"QSS文件不存在: {qss_path}")
        return ''
    try:
        with open(qss_path, 'r', encoding='utf-8-sig') as f:
            content = f.read()
        content = FALLBACK_FONT_QSS + "\n" + content
        _qss_cache[cache_key] = content
        _logger.debug(f"QSS 已加载 {qss_filename} ({theme}) 长度 {len(content)}")
        return content
    except Exception as e:
        _logger.error(f"QSS加载失败 {qss_path}: {e}")
        return ''


def _log_module_init():
    try:
        from core.logger import logger as _logger
        _logger.debug(f"常量模块就绪 资源根={get_resource_path(RESOURCE_ROOT)}")
    except ImportError:
        # core.logger 正在初始化中(循环导入), 跳过本次日志
        pass


_log_module_init()