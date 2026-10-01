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
配置管理模块
"""

import logging
import json
import os
import sys
from enum import Enum
from pathlib import Path

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

from core.constants import DATA_CONFIG, ensure_data_dirs

ensure_data_dirs()

logger = logging.getLogger("Glimpseon.core.config")


class ThemeSerializer(ConfigSerializer):
    def serialize(self, theme):
        return theme.value

    def deserialize(self, value: str):
        return Theme(value)


class Language(Enum):
    CHINESE_SIMPLIFIED = QLocale(QLocale.Language.Chinese, QLocale.Country.China)
    CHINESE_TRADITIONAL = QLocale(QLocale.Language.Chinese, QLocale.Country.HongKong)
    ENGLISH = QLocale(QLocale.Language.English)
    AUTO = QLocale()


class LanguageSerializer(ConfigSerializer):
    def serialize(self, language):
        mapping = {
            Language.CHINESE_SIMPLIFIED: "zh_CN",
            Language.CHINESE_TRADITIONAL: "zh_TW",
            Language.ENGLISH: "en_US",
            Language.AUTO: "Auto"
        }
        return mapping.get(language, "Auto")

    def deserialize(self, value: str):
        mapping = {
            "zh_CN": Language.CHINESE_SIMPLIFIED,
            "zh_TW": Language.CHINESE_TRADITIONAL,
            "en_US": Language.ENGLISH,
            "Auto": Language.AUTO
        }
        result = mapping.get(value)
        if result is None:
            logger.warning(f"未知语言值 {value} 用 Auto")
            return Language.AUTO

        return result


class LogLevel(Enum):
    DEBUG = "Debug"
    INFO = "Info"
    WARNING = "Warning"
    ERROR = "Error"


class LogLevelSerializer(ConfigSerializer):
    def serialize(self, level):
        return level.value

    def deserialize(self, value: str):
        for level in LogLevel:
            if level.value == value:
                return level
        logger.warning(f"未知日志级别: {value} 用 Info")
        return LogLevel.INFO


class CountdownListSerializer(ConfigSerializer):
    """倒计时列表"""
    def serialize(self, countdown_list):
        if not countdown_list:
            return []
        return countdown_list
    def deserialize(self, value):
        if value is None:
            return []
        if not isinstance(value, list):
            logger.warning(f"倒计时列表配置异常 用空列表: {type(value).__name__}")
            return []
        return value


CONFIG_PATH = os.path.join(DATA_CONFIG, 'config.json')


class Config(QConfig):
    """应用配置"""

    def __init__(self):
        super().__init__()
        self.file = Path(CONFIG_PATH)
        logger.debug(f"配置对象就绪 {CONFIG_PATH}")

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
        "Log", "MaxDays", 30, RangeValidator(30, 365)
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
        "Poetry", "PoetryApiUrl", "https://v1.hitokoto.cn/"
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
        "Weather", "City", "北京市"
    )
    weatherSource = OptionsConfigItem(
        "Weather", "Source", "city", OptionsValidator(["city", "coords"])
    )
    latitude = ConfigItem(
        "Weather", "Latitude", 39.9042
    )
    longitude = ConfigItem(
        "Weather", "Longitude", 116.4074
    )
    weatherUnit = OptionsConfigItem(
        "Weather", "Unit", "c", OptionsValidator(["c", "f"])
    )
    weatherAlertExcluded = ConfigItem(
        "Weather", "AlertExcluded", ""
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
    scrollBannerBgHeight = RangeConfigItem(
        "Other", "ScrollBannerBgHeight", 80, RangeValidator(40, 300)
    )
    scrollBannerMouseThrough = ConfigItem(
        "Other", "ScrollBannerMouseThrough", True, BoolValidator()
    )
    notificationSyncBoard = ConfigItem(
        "Notification", "SyncToBoard", False, BoolValidator()
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
    showMediaLyrics = ConfigItem(
        "Media", "ShowMediaLyrics", True, BoolValidator()
    )
    mediaUpdateInterval = RangeConfigItem(
        "Media", "UpdateInterval", 1, RangeValidator(1, 5)
    )
    mediaTextSize = RangeConfigItem(
        "Media", "TextSize", 14, RangeValidator(10, 28)
    )
    mediaCoverSize = RangeConfigItem(
        "Media", "CoverSize", 56, RangeValidator(32, 128)
    )
    mediaLyricsSize = RangeConfigItem(
        "Media", "LyricsSize", 12, RangeValidator(8, 24)
    )
    mediaLyricsLines = RangeConfigItem(
        "Media", "LyricsLines", 3, RangeValidator(1, 7)
    )
    mediaWidth = RangeConfigItem(
        "Media", "Width", 360, RangeValidator(200, 800)
    )
    mediaHeight = RangeConfigItem(
        "Media", "Height", 160, RangeValidator(100, 300)
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

    profileSource = OptionsConfigItem(
        "Timetable", "ProfileSource", "Glimpseon",
        OptionsValidator(["Glimpseon", "classisland", "classwidgets"])
    )
    
    usePreciseTime = ConfigItem(
        "PreciseTime", "UsePreciseTime", True, BoolValidator()
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

try:
    qconfig.load(CONFIG_PATH, cfg)
except Exception as e:
    logger.error(f"设置配置路径失败 {e}")

_cfg_loaded = os.path.exists(CONFIG_PATH)
if _cfg_loaded:
    logger.info(f"从 {CONFIG_PATH} 加载配置")
else:
    logger.info(f"配置不存在 用默认: {CONFIG_PATH}")

def save_cfg():
    try:
        qconfig.save()
        logger.debug(f"配置已保存: {CONFIG_PATH}")
    except Exception as e:
        logger.error(f"保存配置失败 {e}")

def _on_config_changed(item: ConfigItem, *args):
    """配置改变时记录并保存"""
    value = item.value
    if hasattr(value, 'name'):
        value = value.name() if hasattr(value, 'name') and callable(value.name) else value.value
    logger.info(f"配置变更: {item.group}/{item.name} = {value}")
    save_cfg()

for attr_name in dir(cfg):
    if not attr_name.startswith('_'):
        attr = getattr(cfg, attr_name)
        if isinstance(attr, ConfigItem) and hasattr(attr, 'valueChanged'):
            attr.valueChanged.connect(lambda *a, it=attr: _on_config_changed(it, *a))

def default_cfg():
    """生成默认值"""
    result = {}
    for attr_name in dir(Config):
        if attr_name.startswith('_'):
            continue
        item = getattr(Config, attr_name)
        if isinstance(item, ConfigItem):
            result.setdefault(item.group, {})[item.name] = item.serializer.serialize(item.defaultValue)
    result["QFluentWidgets"] = {
        "FontFamilies": [
            "HarmonyOS Sans",
            "HarmonyOS Sans SC",
            "HarmonyOS Sans TC",
            "HarmonyOS Sans HC",
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "PingFang SC",
            "Source Han Sans SC",
            "Segoe UI"
        ]
    }
    logger.debug(f"已生成默认配置: {sum(len(v) for v in result.values() if isinstance(v, dict))}项")
    return result
