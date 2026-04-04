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
配置管理模块
"""

import os
import sys
import logging
from enum import Enum

from PyQt5.QtCore import QLocale
from qfluentwidgets import (
    qconfig, QConfig, ConfigItem, OptionsConfigItem, BoolValidator,
    ColorConfigItem, OptionsValidator, RangeConfigItem, RangeValidator,
    ConfigSerializer, Theme
)

logger = logging.getLogger(__name__)

if getattr(sys, 'frozen', False):
    BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

if BASE_DIR not in sys.path:
    sys.path.insert(0, BASE_DIR)

from config.url_dir import url_dir


class ThemeSerializer(ConfigSerializer):
    """ 主题序列化 """

    def serialize(self, theme):
        return theme.value

    def deserialize(self, value: str):
        return Theme(value)


class Language(Enum):
    """ 语言枚举 """

    CHINESE_SIMPLIFIED = QLocale(QLocale.Chinese, QLocale.China)
    CHINESE_TRADITIONAL = QLocale(QLocale.Chinese, QLocale.HongKong)
    ENGLISH = QLocale(QLocale.English)
    AUTO = QLocale()


class LanguageSerializer(ConfigSerializer):
    """ 语言序列化器 """

    def serialize(self, language):
        return language.value.name() if language != Language.AUTO else "Auto"

    def deserialize(self, value: str):
        return Language(QLocale(value)) if value != "Auto" else Language.AUTO


class LogLevel(Enum):
    """ 日志级别枚举 """
    DEBUG = "Debug"
    INFO = "Info"
    WARNING = "Warning"
    ERROR = "Error"


class LogLevelSerializer(ConfigSerializer):
    """ 日志级别序列化器 """

    def serialize(self, level):
        return level.value

    def deserialize(self, value: str):
        for level in LogLevel:
            if level.value == value:
                return level
        return LogLevel.INFO


class Config(QConfig):
    """ 应用配置 """

    themeMode = OptionsConfigItem(
        "MainWindow", "ThemeMode", Theme.AUTO, OptionsValidator([Theme.LIGHT, Theme.DARK, Theme.AUTO]), ThemeSerializer()
    )
    themeColor = ColorConfigItem("MainWindow", "ThemeColor", "#30c361")
    dpiScale = OptionsConfigItem(
        "MainWindow", "DpiScale", "Auto", OptionsValidator([1, 1.25, 1.5, 1.75, 2, "Auto"]), restart=True
    )
    language = OptionsConfigItem(
        "MainWindow", "Language", Language.AUTO, OptionsValidator(Language), LanguageSerializer(), restart=True
    )

    logLevel = OptionsConfigItem(
        "Log", "LogLevel", LogLevel.INFO, OptionsValidator(LogLevel), LogLevelSerializer(), restart=True
    )
    disableLog = ConfigItem(
        "Log", "DisableLog", False, BoolValidator(), restart=True
    )
    logMaxCount = RangeConfigItem(
        "Log", "MaxCount", 50, RangeValidator(10, 500)
    )
    logMaxDays = RangeConfigItem(
        "Log", "MaxDays", 7, RangeValidator(30, 365)
    )
    closeAction = OptionsConfigItem(
        "Other", "CloseAction", "minimize", OptionsValidator(["minimize", "close"])
    )
    allowMultipleInstances = ConfigItem(
        "Other", "AllowMultipleInstances", False, BoolValidator()
    )
    wallpaperSaveLimit = RangeConfigItem(
        "Wallpaper", "SaveLimit", 50, RangeValidator(10, 100)
    )
    autoGetInterval = OptionsConfigItem(
        "Wallpaper", "AutoGetInterval", "30分钟", OptionsValidator(["从不", "10分钟", "30分钟", "1小时", "3小时", "6小时", "12小时", "1天", "3天", "5天", "7天"])
    )
    autoSyncToDesktop = ConfigItem(
        "Wallpaper", "AutoSyncToDesktop", True, BoolValidator()
    )
    wallpaperApi = OptionsConfigItem(
        "Wallpaper", "WallpaperApi", "wp.upx8.com", OptionsValidator(["wp.upx8.com", "api.ltyuanfang.cn", "imlcd.cn_bg_high", "imlcd.cn_bg_mc", "imlcd.cn_bg_gq"])
    )
    backgroundBlurRadius = RangeConfigItem(
        "Appearance", "BackgroundBlurRadius", 0, RangeValidator(0, 30)
    )
    showClock = ConfigItem(
        "Time", "ShowClock", True, BoolValidator()
    )
    showClockSeconds = ConfigItem(
        "Time", "ShowClockSeconds", True, BoolValidator()
    )
    showLunarCalendar = ConfigItem(
        "Time", "ShowLunarCalendar", True, BoolValidator()
    )
    clockColor = ColorConfigItem("Time", "ClockColor", "#FFFFFF")
    clockSize = RangeConfigItem(
        "Time", "ClockSize", 120, RangeValidator(80, 200)
    )
    dateSize = RangeConfigItem(
        "Time", "DateSize", 20, RangeValidator(12, 50)
    )
    clockPosition = OptionsConfigItem(
        "Time", "ClockPosition", "顶部偏下", OptionsValidator(["左上预留", "左上", "右上预留", "右上", "左下预留", "左下", "右下预留", "右下", "中部", "顶部", "顶部偏下", "底部偏上", "底部"])
    )
    showPoetry = ConfigItem(
        "Poetry", "ShowPoetry", True, BoolValidator()
    )
    showWeather = ConfigItem(
        "Weather", "ShowWeather", True, BoolValidator()
    )
    poetryApiUrl = ConfigItem(
        "Poetry", "PoetryApiUrl", "https://www.ffapi.cn/int/v1/shici"
    )
    poetryUpdateInterval = OptionsConfigItem(
        "Poetry", "PoetryUpdateInterval", "10 分钟", OptionsValidator(["从不", "5 分钟", "10 分钟", "30 分钟", "1 小时", "3 小时", "6 小时", "12 小时", "1 天"])
    )
    poetrySize = RangeConfigItem(
        "Poetry", "PoetrySize", 16, RangeValidator(12, 50)
    )
    poetryPosition = OptionsConfigItem(
        "Poetry", "PoetryPosition", "底部预留", OptionsValidator(["顶部预留", "底部预留"])
    )
    weatherSize = RangeConfigItem(
        "Weather", "WeatherSize", 24, RangeValidator(5, 50)
    )
    weatherIconSize = RangeConfigItem(
        "Weather", "WeatherIconSize", 64, RangeValidator(32, 200)
    )
    weatherUpdateInterval = OptionsConfigItem(
        "Weather", "UpdateInterval", "5 分钟", OptionsValidator(["从不", "5 分钟", "15 分钟", "30 分钟", "1 小时", "3 小时", "6 小时", "12 小时", "24 小时"])
    )
    weatherPosition = OptionsConfigItem(
        "Weather", "WeatherPosition", "右上预留", OptionsValidator(["左上预留", "右上预留", "左下预留", "右下预留"])
    )
    city = ConfigItem(
        "Weather", "City", "北京"
    )
    latitude = ConfigItem(
        "Weather", "Latitude", 39.9042
    )
    longitude = ConfigItem(
        "Weather", "Longitude", 116.4074
    )
    developerMode = ConfigItem(
        "Other", "DeveloperMode", False, BoolValidator()
    )
    autoStart = ConfigItem(
        "Other", "AutoStart", False, BoolValidator()
    )
    autoOpenOnIdle = ConfigItem(
        "Other", "AutoOpenOnIdle", False, BoolValidator()
    )
    idleMinutes = RangeConfigItem(
        "Other", "IdleMinutes", 5, RangeValidator(1, 60)
    )
    autoOpenMaximize = ConfigItem(
        "Other", "AutoOpenMaximize", False, BoolValidator()
    )
    autoCheckUpdate = ConfigItem(
        "Other", "AutoCheckUpdate", True, BoolValidator()
    )
    autoUpdate = ConfigItem(
        "Other", "AutoUpdate", False, BoolValidator()
    )
    downloadSource = OptionsConfigItem(
        "Download", "Source", "hk", OptionsValidator(["original", "hk", "cloudflare", "edgeone", "geekertao"])
    )


cfg = Config()
CONFIG_PATH = os.path.join(BASE_DIR, 'config', 'config.json')
if os.path.exists(CONFIG_PATH):
    try:
        qconfig.load(CONFIG_PATH, cfg)
        logger.info(f"已从 {CONFIG_PATH} 加载配置")
    except Exception as e:
        logger.error(f"加载配置失败：{e}")

def save_config():
    try:
        # 用 qconfig 的 save 
        qconfig.save()
    except Exception as e:
        logger.error(f"保存配置失败：{e}")

def _on_config_changed(*args):
    """配置改变时自动保存"""
    save_config()

saved_count = 0
for attr_name in dir(cfg):
    if not attr_name.startswith('_'):
        attr = getattr(cfg, attr_name)
        if isinstance(attr, ConfigItem) and hasattr(attr, 'valueChanged'):
            attr.valueChanged.connect(_on_config_changed)
            saved_count += 1

logger.info(f"已连接 {saved_count} 个配置项的自动保存")


def get_default_config_dict():
    """ 获取默认配置字典 """
    return {
        "MainWindow": {
            "DpiScale": "Auto",
            "Language": "Auto",
            "ThemeColor": "#30c361",
            "ThemeMode": "Auto"
        },
        "Log": {
            "DisableLog": False,
            "LogLevel": "Info",
            "MaxCount": 50,
            "MaxDays": 7
        },
        "Other": {
            "CloseAction": "minimize",
            "AllowMultipleInstances": False,
            "DeveloperMode": False,
            "AutoStart": False,
            "AutoOpenOnIdle": False,
            "IdleMinutes": 5,
            "AutoOpenMaximize": False,
            "AutoCheckUpdate": True,
            "AutoUpdate": False
        },
        "Wallpaper": {
            "SaveLimit": 50,
            "AutoGetInterval": "30 分钟",
            "AutoSyncToDesktop": True,
            "WallpaperApi": "wp.upx8.com"
        },
        "Appearance": {
            "BackgroundBlurRadius": 0
        },
        "Time": {
            "ShowClock": True,
            "ShowClockSeconds": True,
            "ShowLunarCalendar": True,
            "ClockColor": "#FFFFFF",
            "ClockSize": 120,
            "DateSize": 20,
            "ClockPosition": "顶部偏下"
        },
        "Poetry": {
            "ShowPoetry": True,
            "PoetryApiUrl": "https://www.ffapi.cn/int/v1/shici",
            "PoetryUpdateInterval": "10 分钟",
            "PoetrySize": 16,
            "PoetryPosition": "底部预留"
        },
        "Weather": {
            "ShowWeather": True,
            "WeatherSize": 24,
            "WeatherIconSize": 64,
            "UpdateInterval": "5 分钟",
            "City": "北京",
            "Latitude": 39.9042,
            "Longitude": 116.4074,
            "WeatherPosition": "右上预留"
        },
        "QFluentWidgets": {
            "FontFamilies": ["HarmonyOS Sans SC"]
        },
        "Download": {
            "Source": "hk"
        }
    }


if not os.path.exists(os.path.join(BASE_DIR, 'config')):
    os.makedirs(os.path.join(BASE_DIR, 'config'))
qconfig.load(os.path.join(BASE_DIR, 'config', 'config.json'), cfg)
