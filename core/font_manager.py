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
字体管理模块
"""

import ctypes
import os
import shutil
from typing import List
from PyQt5.QtGui import QFont, QFontDatabase
from PyQt5.QtWidgets import QApplication
from core.constants import get_resource_path
from core.logger import logger

HARMONYOS_FONT_FILES = [
    "HarmonyOS_Sans_Thin.ttf",
    "HarmonyOS_Sans_Light.ttf",
    "HarmonyOS_Sans_Regular.ttf",
    "HarmonyOS_Sans_Medium.ttf",
    "HarmonyOS_Sans_Bold.ttf",
    "HarmonyOS_Sans_Black.ttf"
]
HARMONYOS_FONT_FAMILIES = [
    "HarmonyOS Sans SC",
    "HarmonyOS Sans",
    "Microsoft YaHei",
    "SimHei",
    "sans-serif"
]
FALLBACK_FONT_FAMILIES = [
    "Microsoft YaHei",
    "SimHei",
    "sans-serif"
]


def get_harmonyos_font_dir() -> str:
    return get_resource_path(os.path.join("font", "HarmonyOS_Sans"))

def is_fonts_installed_in_system() -> bool:
    """检查系统有吗"""
    system_font_dir = os.path.join(os.environ['WINDIR'], 'Fonts')
    for font_file in HARMONYOS_FONT_FILES:
        system_font_path = os.path.join(system_font_dir, font_file)
        if not os.path.exists(system_font_path):
            return False
    return True


def install_fonts_to_system() -> bool:
    """安装到系统"""
    if is_fonts_installed_in_system():return True
    system_font_dir = os.path.join(os.environ['WINDIR'], 'Fonts')
    local_font_dir = get_harmonyos_font_dir()
    if not os.path.exists(local_font_dir):
        logger.warning(f"鸿蒙字体目录不存在：{local_font_dir}")
        return False
    try:
        for font_file in HARMONYOS_FONT_FILES:
            local_font_path = os.path.join(local_font_dir, font_file)
            system_font_path = os.path.join(system_font_dir, font_file)
            if os.path.exists(local_font_path) and not os.path.exists(system_font_path):
                shutil.copy2(local_font_path, system_font_path)
                ctypes.windll.gdi32.AddFontResourceW(system_font_path)
                logger.debug(f"已安装字体：{font_file}")
        HWND_BROADCAST = 0xFFFF
        WM_FONTCHANGE = 0x001D
        SMTO_ABORTIFHUNG = 0x0002
        ctypes.windll.user32.SendMessageTimeoutW(
            HWND_BROADCAST, WM_FONTCHANGE, 0, 0, SMTO_ABORTIFHUNG, 1000, None
        )
        return True
    except Exception as e:
        logger.warning(f"安装鸿蒙字体到系统失败：{e}")
        return False


def load_fonts_to_application() -> bool:
    """加载到应用程序"""
    font_dir = get_harmonyos_font_dir()
    if not os.path.exists(font_dir):
        logger.warning(f"鸿蒙字体目录不存在：{font_dir}")
        return False
    font_loaded = False
    for font_file in HARMONYOS_FONT_FILES:
        try:
            font_path = os.path.join(font_dir, font_file)
            if os.path.exists(font_path):
                font_id = QFontDatabase.addApplicationFont(font_path)
                if font_id != -1:
                    font_loaded = True
                    logger.debug(f"成功加载字体：{font_file}")
                else:
                    logger.warning(f"字体加载失败：{font_file}")
        except Exception as e:
            logger.warning(f"加载字体 {font_file} 时发生错误：{e}")
    
    if font_loaded:logger.info("鸿蒙字体已加载到应用程序")
    else:logger.warning("未成功加载任何鸿蒙字体")
    return font_loaded


def apply_fonts(app: QApplication, use_harmonyos: bool = True):
    """应用字体设置"""
    from qfluentwidgets import setFontFamilies
    if use_harmonyos:
        setFontFamilies(HARMONYOS_FONT_FAMILIES, save=False)
        app.setFont(QFont("HarmonyOS Sans SC", 10))
        logger.info("字体已设置为：HarmonyOS Sans SC")
    else:
        setFontFamilies(FALLBACK_FONT_FAMILIES, save=False)
        app.setFont(QFont("Microsoft YaHei", 10))
        logger.info("字体已设置为：Microsoft YaHei")

def initialize_fonts(app: QApplication, install_to_system: bool = True):
    """初始化"""
    logger.info("开始初始化字体...")
    if install_to_system:install_fonts_to_system()
    font_loaded = load_fonts_to_application()
    apply_fonts(app, use_harmonyos=font_loaded)
    logger.info("字体初始化完成")
