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
配置管理模块
"""

import logging
import os
import sys
from enum import Enum

from PyQt6.QtCore import QLocale
from qfluentwidgets import (
    BoolValidator,
    ColorConfigItem,
    ConfigItem,
    ConfigSerializer,
    OptionsConfigItem,
    OptionsValidator,
    QConfig,
    qconfig,
    RangeConfigItem,
    RangeValidator,
    Theme,
)

if getattr(sys, 'frozen', False):
    BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

if BASE_DIR not in sys.path:
    sys.path.insert(0, BASE_DIR)

from data.url_dir import url_dir

logger = logging.getLogger("ClassLively.core.config")


class ThemeSerializer(ConfigSerializer):
    """ 主题序列化 """

    def serialize(self, theme):
        return theme.value

    def deserialize(self, value: str):
        return Theme(value)


class Language(Enum):
    """ 语言枚举 """

    CHINESE_SIMPLIFIED = QLocale(QLocale.Language.Chinese, QLocale.Country.China)
    CHINESE_TRADITIONAL = QLocale(QLocale.Language.Chinese, QLocale.Country.HongKong)
    ENGLISH = QLocale(QLocale.Language.English)
    AUTO = QLocale()


class LanguageSerializer(ConfigSerializer):
    """ 语言序列化器 """

    def serialize(self, language):
        """ Language 枚举序列化 to 字符串"""
        mapping = {
            Language.CHINESE_SIMPLIFIED: "zh_CN",
            Language.CHINESE_TRADITIONAL: "zh_TW",
            Language.ENGLISH: "en_US",
            Language.AUTO: "Auto"
        }
        return mapping.get(language, "Auto")

    def deserialize(self, value: str):
        """字符串反序列化 to Language 枚举"""
        mapping = {
            "zh_CN": Language.CHINESE_SIMPLIFIED,
            "zh_TW": Language.CHINESE_TRADITIONAL,
            "en_US": Language.ENGLISH,
            "Auto": Language.AUTO
        }
        result = mapping.get(value)
        if result is None:
            logger.warning(f"未知的语言值: {value}")
            return Language.AUTO

        return result


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


class CountdownListSerializer(ConfigSerializer):
    """ 倒计时列表 """
    def serialize(self, countdown_list):
        if not countdown_list:
            return []
        return countdown_list
    def deserialize(self, value):
        if not value or not isinstance(value, list):
            return []
        return value


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
        "Wallpaper", "AutoGetInterval", "30m", OptionsValidator(["never", "10m", "30m", "1h", "3h", "6h", "12h", "1d", "3d", "5d", "7d"])
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
    wallpaperBrightness = RangeConfigItem(
        "Wallpaper", "Brightness", 0, RangeValidator(-100, 0)
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
        "Time", "ClockSize", 80, RangeValidator(40, 120)
    )
    dateSize = RangeConfigItem(
        "Time", "DateSize", 16, RangeValidator(10, 40)
    )
    timeOffset = RangeConfigItem(
        "Time", "TimeOffset", 0, RangeValidator(-9999, 9999)
    )
    autoTimeOffsetEnabled = ConfigItem(
        "Time", "AutoTimeOffsetEnabled", False, BoolValidator()
    )
    autoTimeOffsetIncrement = RangeConfigItem(
        "Time", "AutoTimeOffsetIncrement", 1, RangeValidator(-9999, 9999)
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
        "Poetry", "PoetryUpdateInterval", "10m", OptionsValidator(["never", "5m", "10m", "30m", "1h", "3h", "6h", "12h", "1d"])
    )
    poetrySize = RangeConfigItem(
        "Poetry", "PoetrySize", 16, RangeValidator(12, 50)
    )
    poetryTextColor = ColorConfigItem("Poetry", "PoetryTextColor", "#FFFFFF")
    weatherSize = RangeConfigItem(
        "Weather", "WeatherSize", 24, RangeValidator(5, 50)
    )
    weatherTextColor = ColorConfigItem("Weather", "WeatherTextColor", "#FFFFFF")
    weatherIconSize = RangeConfigItem(
        "Weather", "WeatherIconSize", 64, RangeValidator(32, 200)
    )
    weatherUpdateInterval = OptionsConfigItem(
        "Weather", "UpdateInterval", "5m", OptionsValidator(["never", "5m", "15m", "30m", "1h", "3h", "6h", "12h", "24h"])
    )
    city = ConfigItem(
        "Weather", "City", ""
    )
    cityCode = ConfigItem(
        "Weather", "CityCode", ""
    )
    latitude = ConfigItem(
        "Weather", "Latitude", 39.9042
    )
    longitude = ConfigItem(
        "Weather", "Longitude", 116.4074
    )
    debugMode = ConfigItem(
        "Other", "DebugMode", False, BoolValidator()
    )
    enableGpuAcceleration = ConfigItem(
        "Other", "EnableGpuAcceleration", True, BoolValidator(), restart=True
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
    downloadItemsPerPage = RangeConfigItem(
        "Download", "ItemsPerPage", 8, RangeValidator(4, 40)
    )

    showCountdown = ConfigItem(
        "Countdown", "ShowCountdown", True, BoolValidator()
    )
    countdownDisplayMode = OptionsConfigItem(
        "Countdown", "DisplayMode", "simultaneous", OptionsValidator(["simultaneous", "carousel"])
    )
    countdownTextColor = ColorConfigItem("Countdown", "TextColor", "#FF0000")
    countdownTextSize = RangeConfigItem(
        "Countdown", "TextSize", 35, RangeValidator(12, 120)
    )
    countdownConnectorColor = ColorConfigItem("Countdown", "ConnectorColor", "#FFFFFF")
    countdownConnectorSize = RangeConfigItem(
        "Countdown", "ConnectorSize", 35, RangeValidator(12, 60)
    )
    countdownCarouselInterval = RangeConfigItem(
        "Countdown", "CarouselInterval", 5, RangeValidator(1, 60)
    )
    countdownList = ConfigItem(
        "Countdown", "CountdownList", [], validator=None, serializer=CountdownListSerializer()
    )
    minimizeNotificationCount = ConfigItem(
        "Other", "MinimizeNotificationCount", 0, validator=None
    )
    school = ConfigItem(
        "School", "School", ""
    )
    schoolClass = ConfigItem(
        "School", "Class", ""
    )
    showSchoolInfo = ConfigItem(
        "School", "ShowSchoolInfo", False, BoolValidator()
    )
    schoolInfoTextColor = ColorConfigItem(
        "School", "SchoolInfoTextColor", "#FFFFFF"
    )
    schoolInfoTextSize = RangeConfigItem(
        "School", "SchoolInfoTextSize", 34, RangeValidator(12, 60)
    )
    
    showQuickLaunch = ConfigItem(
        "QuickLaunch", "ShowQuickLaunch", True, BoolValidator()
    )
    quickLaunchApps = ConfigItem(
        "QuickLaunch", "QuickLaunchApps", []
    )
    quickLaunchIconSize = RangeConfigItem(
        "QuickLaunch", "IconSize", 64, RangeValidator(32, 96)
    )
    quickLaunchIconSpacing = RangeConfigItem(
        "QuickLaunch", "IconSpacing", 12, RangeValidator(4, 40)
    )
    quickLaunchShowLabels = ConfigItem(
        "QuickLaunch", "ShowLabels", True, BoolValidator()
    )
    quickLaunchOffsetY = RangeConfigItem(
        "QuickLaunch", "OffsetY", 60, RangeValidator(0, 120)
    )
    
    showMediaInfo = ConfigItem(
        "Media", "ShowMediaInfo", True, BoolValidator()
    )
    showMediaCover = ConfigItem(
        "Media", "ShowMediaCover", True, BoolValidator()
    )
    showMediaProgress = ConfigItem(
        "Media", "ShowMediaProgress", True, BoolValidator()
    )
    showMediaLyrics = ConfigItem(
        "Media", "ShowMediaLyrics", True, BoolValidator()
    )
    mediaUpdateInterval = RangeConfigItem(
        "Media", "UpdateInterval", 1, RangeValidator(1, 5)
    )
    mediaTextSize = RangeConfigItem(
        "Media", "TextSize", 16, RangeValidator(12, 32)
    )
    mediaCoverSize = RangeConfigItem(
        "Media", "CoverSize", 64, RangeValidator(32, 128)
    )
    mediaLyricsSize = RangeConfigItem(
        "Media", "LyricsSize", 14, RangeValidator(10, 24)
    )
    mediaLyricsLines = RangeConfigItem(
        "Media", "LyricsLines", 3, RangeValidator(1, 7)
    )
    mediaWidth = RangeConfigItem(
        "Media", "Width", 360, RangeValidator(200, 800)
    )
    mediaHeight = RangeConfigItem(
        "Media", "Height", 130, RangeValidator(80, 300)
    )
    mediaLyricsAdvance = RangeConfigItem(
        "Media", "LyricsAdvance", 300, RangeValidator(0, 2000)
    )
    mediaUseCustomBg = ConfigItem(
        "Media", "UseCustomBg", False, BoolValidator()
    )
    mediaBgOpacity = RangeConfigItem(
        "Media", "BgOpacity", 60, RangeValidator(0, 100)
    )
    mediaBorderRadius = RangeConfigItem(
        "Media", "BorderRadius", 12, RangeValidator(0, 30)
    )
    mediaTitleColor = ColorConfigItem("Media", "TitleColor", "#FFFFFF")
    mediaArtistColor = ColorConfigItem("Media", "ArtistColor", "#FFFFFF99")
    mediaTimeColor = ColorConfigItem("Media", "TimeColor", "#FFFFFF80")
    mediaLyricsColor = ColorConfigItem("Media", "LyricsColor", "#FFFFFFB3")
    mediaProgressColor = ColorConfigItem("Media", "ProgressColor", "#30c361")
    mediaProgressTrackColor = ColorConfigItem("Media", "ProgressTrackColor", "#FFFFFF1A")
    mediaProgressHeight = RangeConfigItem(
        "Media", "ProgressHeight", 4, RangeValidator(2, 8)
    )
    mediaCoverBorderRadius = RangeConfigItem(
        "Media", "CoverBorderRadius", 10, RangeValidator(0, 20)
    )
    mediaCoverBorderColor = ColorConfigItem("Media", "CoverBorderColor", "#FFFFFF20")

    linkageEnabled = ConfigItem(
        "Linkage", "Enabled", False, BoolValidator()
    )
    linkageDataPath = ConfigItem(
        "Linkage", "DataPath", ""
    )
    linkagePollInterval = RangeConfigItem(
        "Linkage", "PollInterval", 5, RangeValidator(1, 30)
    )
    linkageSyncTimeConfig = ConfigItem(
        "Linkage", "SyncTimeConfig", False, BoolValidator()
    )
    
    classWidgetsEnabled = ConfigItem(
        "ClassWidgets", "Enabled", False, BoolValidator()
    )
    classWidgetsDataPath = ConfigItem(
        "ClassWidgets", "DataPath", ""
    )
    classWidgetsPollInterval = RangeConfigItem(
        "ClassWidgets", "PollInterval", 5, RangeValidator(1, 30)
    )
    
    usePreciseTime = ConfigItem(
        "PreciseTime", "UsePreciseTime", False, BoolValidator()
    )
    timeServer = ConfigItem(
        "PreciseTime", "TimeServer", "ntp.aliyun.com"
    )
    lastSyncTime = ConfigItem(
        "PreciseTime", "LastSyncTime", ""
    )

    gridShortSideCells = RangeConfigItem(
        "Grid", "ShortSideCells", 6, RangeValidator(6, 96)
    )
    gridInsetPercent = RangeConfigItem(
        "Grid", "InsetPercent", 5, RangeValidator(0, 30)
    )
    componentCardOpacity = RangeConfigItem(
        "Grid", "ComponentCardOpacity", 55, RangeValidator(0, 100)
    )
    componentCardRadius = RangeConfigItem(
        "Grid", "ComponentCardRadius", 16, RangeValidator(0, 29)
    )


cfg = Config()
CONFIG_PATH = os.path.join(BASE_DIR, 'config', 'config.json')
_cfg_loaded = False
if os.path.exists(CONFIG_PATH):
    try:
        qconfig.load(CONFIG_PATH, cfg)
        _cfg_loaded = True
        logger.info(f"已从 {CONFIG_PATH} 加载配置")
    except Exception as e:
        logger.error(f"加载配置失败：{e}")

def save_cfg():
    try:
        # 用 qconfig 的 save 
        qconfig.save()
    except Exception as e:
        logger.error(f"保存配置失败：{e}")

def _on_config_changed(*args):
    """配置改变时自动保存"""
    save_cfg()

saved_count = 0
for attr_name in dir(cfg):
    if not attr_name.startswith('_'):
        attr = getattr(cfg, attr_name)
        if isinstance(attr, ConfigItem) and hasattr(attr, 'valueChanged'):
            attr.valueChanged.connect(_on_config_changed)
            saved_count += 1

logger.info(f"已连接 {saved_count} 个配置项的自动保存")


def default_cfg():
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
            "DebugMode": False,
            "EnableGpuAcceleration": True,
            "AutoStart": False,
            "AutoOpenOnIdle": False,
            "IdleMinutes": 5,
            "AutoOpenMaximize": False,
            "AutoCheckUpdate": True,
            "AutoUpdate": False,
            "MinimizeNotificationCount": 0
        },
        "Wallpaper": {
            "SaveLimit": 50,
            "AutoGetInterval": "30m",
            "AutoSyncToDesktop": True,
            "WallpaperApi": "wp.upx8.com",
            "Brightness": 0
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
            "TimeOffset": 0,
            "AutoTimeOffsetEnabled": False,
            "AutoTimeOffsetIncrement": 1
        },
        "Poetry": {
            "ShowPoetry": True,
            "PoetryApiUrl": "https://www.ffapi.cn/int/v1/shici",
            "PoetryUpdateInterval": "10m",
            "PoetrySize": 16,
            "PoetryTextColor": "#FFFFFF"
        },
        "Weather": {
            "ShowWeather": True,
            "WeatherSize": 24,
            "WeatherTextColor": "#FFFFFF",
            "WeatherIconSize": 64,
            "UpdateInterval": "5m",
            "City": "",
            "Latitude": 39.9042,
            "Longitude": 116.4074
        },
        "QFluentWidgets": {
            "FontFamilies": ["HarmonyOS Sans"]
        },
        "Download": {
            "Source": "hk",
            "ItemsPerPage": 8
        },
        "Countdown": {
            "ShowCountdown": True,
            "DisplayMode": "simultaneous",
            "TextColor": "#FF0000",
            "TextSize": 35,
            "ConnectorColor": "#FFFFFF",
            "ConnectorSize": 35,
            "CarouselInterval": 5,
            "CountdownList": []
        },
        "School": {
            "School": "",
            "Class": "",
            "ShowSchoolInfo": False,
            "SchoolInfoTextColor": "#FFFFFF",
            "SchoolInfoTextSize": 34
        },
        "QuickLaunch": {
            "ShowQuickLaunch": True,
            "QuickLaunchApps": [],
            "IconSize": 64,
            "IconSpacing": 12,
            "ShowLabels": True,
            "OffsetY": 60
        },
        "Media": {
            "ShowMediaInfo": True,
            "ShowMediaCover": True,
            "ShowMediaProgress": True,
            "ShowMediaLyrics": True,
            "UpdateInterval": 1,
            "TextSize": 16,
            "CoverSize": 64,
            "LyricsSize": 14,
            "LyricsLines": 3,
            "LyricsAdvance": 300,
            "Width": 360,
            "Height": 130,
            "BgColor": "#000000",
            "BgOpacity": 60,
            "UseCustomBg": False,
            "BorderRadius": 12,
            "TitleColor": "#FFFFFF",
            "ArtistColor": "#FFFFFF99",
            "TimeColor": "#FFFFFF80",
            "LyricsColor": "#FFFFFFB3",
            "ProgressColor": "#30c361",
            "ProgressTrackColor": "#FFFFFF1A",
            "ProgressHeight": 4,
            "CoverBorderRadius": 10,
            "CoverBorderColor": "#FFFFFF20"
        },
        "Linkage": {
            "Enabled": False,
            "DataPath": "",
            "PollInterval": 5,
            "SyncTimeConfig": False
        },
        "ClassWidgets": {
            "Enabled": False,
            "DataPath": "",
            "PollInterval": 5
        },
        "PreciseTime": {
            "UsePreciseTime": False,
            "TimeServer": "ntp.aliyun.com",
            "LastSyncTime": ""
        },
        "Grid": {
            "ShortSideCells": 6,
            "InsetPercent": 5,
            "ComponentCardOpacity": 55,
            "ComponentCardRadius": 16
        }
    }


if not os.path.exists(os.path.join(BASE_DIR, 'config')):
    os.makedirs(os.path.join(BASE_DIR, 'config'))
if not _cfg_loaded and os.path.exists(CONFIG_PATH):
    try:
        qconfig.load(CONFIG_PATH, cfg)
    except Exception:
        pass

# 将 Appearance 下的 ComponentCardOpacity/ComponentCardRadius 移到 Grid 下
if os.path.exists(CONFIG_PATH):
    try:
        with open(CONFIG_PATH, 'r', encoding='utf-8') as f:
            _migrate_data = json.load(f)
        _migrated = False
        if "Appearance" in _migrate_data:
            for _key in ("ComponentCardOpacity", "ComponentCardRadius"):
                if _key in _migrate_data["Appearance"]:
                    _migrate_data.setdefault("Grid", {})[_key] = _migrate_data["Appearance"].pop(_key)
                    _migrated = True
            if not _migrate_data["Appearance"]:
                del _migrate_data["Appearance"]
        if _migrated:
            with open(CONFIG_PATH, 'w', encoding='utf-8') as f:
                json.dump(_migrate_data, f, indent=4, ensure_ascii=False)
    except Exception:
        pass
