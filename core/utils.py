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
工具
"""

import ctypes
import json
import logging
import os
import shutil
import sys
import time
import winreg
from ctypes import wintypes
from enum import Enum
from typing import Any, Dict, List, Optional

from PyQt6.QtCore import QObject
from PyQt6.QtGui import QFont, QFontDatabase
from PyQt6.QtWidgets import QApplication, QWidget
from qfluentwidgets import setFontFamilies

from core.config import cfg, save_cfg
from core.constants import APP_NAME, BASE_DIR, MEIPASS_DIR, get_resPath
from core.logger import logger

kernel32 = ctypes.windll.kernel32
ERROR_ALREADY_EXISTS = 183
MutexHandle = wintypes.HANDLE


class SingleInstanceManager:
    MUTEX_NAME = "ClassLively_SingleInstance_Mutex_{A7F3E2D1-8B4C-4F6A-9D0E-1C2B3A4F5E6D}"

    def __init__(self):
        self._mutex_handle: MutexHandle = None
        self._is_owner = False

    def try_acquire(self) -> bool:
        if self._mutex_handle is not None:
            return self._is_owner
        self._mutex_handle = kernel32.CreateMutexW(None, True, self.MUTEX_NAME)
        last_error = kernel32.GetLastError()
        if self._mutex_handle is None or self._mutex_handle == 0:
            logger.error(f"创建互斥失败，句柄: {self._mutex_handle}")
            return True
        if last_error == ERROR_ALREADY_EXISTS:
            logger.info("已有实例运行")
            self._is_owner = False
            return False
        self._is_owner = True
        return True

    def release(self):
        if self._mutex_handle is not None and self._mutex_handle != 0:
            if self._is_owner:
                kernel32.ReleaseMutex(self._mutex_handle)
            kernel32.CloseHandle(self._mutex_handle)
            self._mutex_handle = None
            self._is_owner = False
            logger.info("互斥已释放")

    @property
    def is_owner(self) -> bool:
        return self._is_owner


_instance_manager: SingleInstanceManager = None


def get_instance_manager() -> SingleInstanceManager:
    global _instance_manager
    if _instance_manager is None:
        _instance_manager = SingleInstanceManager()
    return _instance_manager


def check_single_instance() -> bool:
    manager = get_instance_manager()
    return manager.try_acquire()


def release_single_instance():
    manager = get_instance_manager()
    manager.release()


def verify_single_instance():
    """检查是否已经有实例运行"""
    allow_multiple = cfg.allowMultipleInstances.value
    is_debug_mode = cfg.debugMode.value
    if allow_multiple:
        return True
    if is_debug_mode:
        return True
    is_only_instance = check_single_instance()
    if is_only_instance:
        return True
    logger.info("已有实例运行")
    return False


HARMONYOS_FONT_FILES = [
    "HarmonyOS_Sans_Thin.ttf",
    "HarmonyOS_Sans_Light.ttf",
    "HarmonyOS_Sans_Regular.ttf",
    "HarmonyOS_Sans_Medium.ttf",
    "HarmonyOS_Sans_Bold.ttf",
    "HarmonyOS_Sans_Black.ttf"
]
HARMONYOS_FONT_FAMILIES = [
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


def _get_fontdir() -> str:
    return get_resPath(os.path.join("font", "HarmonyOS_Sans"))


def _check_fonts_installed() -> bool:
    system_font_dir = os.path.join(os.environ['WINDIR'], 'Fonts')
    for font_file in HARMONYOS_FONT_FILES:
        system_font_path = os.path.join(system_font_dir, font_file)
        if not os.path.exists(system_font_path):
            return False
    return True


def _install_system_fonts() -> bool:
    if _check_fonts_installed():
        return True
    system_font_dir = os.path.join(os.environ['WINDIR'], 'Fonts')
    local_font_dir = _get_fontdir()
    if not os.path.exists(local_font_dir):
        logger.warning(f"字体目录不存在：{local_font_dir}")
        return False
    
    installed_any = False
    try:
        for font_file in HARMONYOS_FONT_FILES:
            local_font_path = os.path.join(local_font_dir, font_file)
            system_font_path = os.path.join(system_font_dir, font_file)
            if os.path.exists(local_font_path) and not os.path.exists(system_font_path):
                try:
                    shutil.copy2(local_font_path, system_font_path)
                    result = ctypes.windll.gdi32.AddFontResourceW(system_font_path)
                    if result > 0:
                        installed_any = True
                        logger.debug(f"已安装字体：{font_file}")
                    else:
                        logger.warning(f"AddFontResourceW：{font_file}")
                except Exception as e:
                    logger.warning(f"安装单个字体失败 {font_file}：{e}")
                    continue
        if installed_any:
            try:
                HWND_BROADCAST = 0xFFFF
                WM_FONTCHANGE = 0x001D
                SMTO_ABORTIFHUNG = 0x0002
                
                result = ctypes.wintypes.DWORD()
                send_result = ctypes.windll.user32.SendMessageTimeoutW(
                    HWND_BROADCAST, WM_FONTCHANGE, 0, 0, 
                    SMTO_ABORTIFHUNG, 500, ctypes.byref(result)
                )
            except Exception as e:
                logger.warning(f"广播字体变更消息失败：{e}")
        
        return True
    except Exception as e:
        logger.warning(f"安装字体到系统失败：{e}")
        return False


def _load_app_fonts(max_retries: int = 3, retry_delay: float = 0.1) -> bool:
    font_dir = _get_fontdir()
    if not os.path.exists(font_dir):
        logger.warning(f"字体目录不存在：{font_dir}")
        return False
    
    font_loaded = False
    failed_fonts = []
    
    for font_file in HARMONYOS_FONT_FILES:
        font_path = os.path.join(font_dir, font_file)
        if not os.path.exists(font_path):
            logger.warning(f"字体文件不存在：{font_path}")
            failed_fonts.append(font_file)
            continue
        
        loaded = False
        for attempt in range(max_retries):
            try:
                font_id = QFontDatabase.addApplicationFont(font_path)
                if font_id != -1:
                    loaded = True
                    font_loaded = True
                    logger.debug(f"成功加载字体：{font_file} (尝试 {attempt + 1})")
                    break
                else:
                    if attempt < max_retries - 1:
                        time.sleep(retry_delay)
            except Exception as e:
                logger.warning(f"加载字体 {font_file} 时发生错误 (尝试 {attempt + 1})：{e}")
                if attempt < max_retries - 1:
                    time.sleep(retry_delay)
        
        if not loaded:
            failed_fonts.append(font_file)
            logger.warning(f"字体加载失败（已重试{max_retries}次）：{font_file}")

    if font_loaded:
        logger.info(f"字体已加载到应用程序 ({len(HARMONYOS_FONT_FILES) - len(failed_fonts)}/{len(HARMONYOS_FONT_FILES)} 成功)")
        if failed_fonts:
            logger.warning(f"以下字体加载失败：{', '.join(failed_fonts)}")
    else:
        logger.warning("未成功加载任何字体")
    return font_loaded


def apply_fonts(app: QApplication, use_harmonyos: bool = True):
    if use_harmonyos:
        setFontFamilies(HARMONYOS_FONT_FAMILIES, save=False)
        app.setFont(QFont("HarmonyOS Sans", 10))
        logger.info("字体已设置为：HarmonyOS Sans")
    else:
        setFontFamilies(FALLBACK_FONT_FAMILIES, save=False)
        app.setFont(QFont("Microsoft YaHei", 10))
        logger.info("字体已设置为：Microsoft YaHei")


def initialize_fonts(app: QApplication, install_to_system: bool = True):
    if install_to_system:
        _install_system_fonts()
    
    font_loaded = _load_app_fonts(max_retries=3, retry_delay=0.1)
    apply_fonts(app, use_harmonyos=font_loaded)
    logger.info("字体初始化完成")


CACHE_DIR = "data/cache"

INTERVAL_MAP = {
    "从不": 0,
    "5 分钟": 300,
    "10 分钟": 600,
    "15 分钟": 900,
    "30 分钟": 1800,
    "1 小时": 3600,
    "3 小时": 10800,
    "6 小时": 21600,
    "12 小时": 43200,
    "1 天": 86400,
    "3 天": 259200,
    "5 天": 432000,
    "7 天": 604800,
    "10分钟": 600,
    "30分钟": 1800,
    "1小时": 3600,
    "3小时": 10800,
    "6小时": 21600,
    "12小时": 43200,
    "1天": 86400,
    "3天": 259200,
    "5天": 432000,
    "7天": 604800,
}


def get_cache_dir():
    cache_dir = os.path.join(BASE_DIR, CACHE_DIR)
    os.makedirs(cache_dir, exist_ok=True)
    return cache_dir


def get_cache_path(cache_name: str) -> str:
    cache_dir = get_cache_dir()
    return os.path.join(cache_dir, f"{cache_name}.json")


def parse_interval(interval_str: str) -> int:
    return INTERVAL_MAP.get(interval_str.strip(), 0)


def save_cache(cache_name: str, content: Any, interval_str: str = "30分钟"):
    cache_path = get_cache_path(cache_name)
    interval_seconds = parse_interval(interval_str)

    now = time.time()
    cache_data = {
        "content": content,
        "timestamp": now,
        "expires_at": now + interval_seconds if interval_seconds > 0 else float('inf'),
        "interval": interval_str,
    }

    try:
        with open(cache_path, 'w', encoding='utf-8') as f:
            json.dump(cache_data, f, ensure_ascii=False, indent=2)
        logger.debug(f"缓存已保存: {cache_name}, 过期时间: {interval_str}")
        return True
    except Exception as e:
        logger.error(f"保存缓存失败 {cache_name}: {e}")
        return False


def load_cache(cache_name: str) -> Optional[dict]:
    cache_path = get_cache_path(cache_name)

    if not os.path.exists(cache_path):
        return None

    try:
        with open(cache_path, 'r', encoding='utf-8') as f:
            cache_data = json.load(f)

        now = time.time()
        expires_at = cache_data.get("expires_at", 0)

        if now >= expires_at:
            logger.debug(f"缓存已过期: {cache_name}")
            return None

        logger.debug(f"读取缓存成功: {cache_name}, 剩余有效期: {int(expires_at - now)}秒")
        return cache_data
    except Exception as e:
        logger.error(f"读取缓存失败 {cache_name}: {e}")
        return None


def get_cached_content(cache_name: str) -> Optional[Any]:
    cache_data = load_cache(cache_name)
    if cache_data:
        return cache_data.get("content")
    return None


def is_cache_valid(cache_name: str) -> bool:
    cache_data = load_cache(cache_name)
    return cache_data is not None


def clear_cache(cache_name: str):
    cache_path = get_cache_path(cache_name)
    if os.path.exists(cache_path):
        try:
            os.remove(cache_path)
            logger.info(f"缓存已清除: {cache_name}")
        except Exception as e:
            logger.error(f"清除缓存失败 {cache_name}: {e}")


def clear_all_cache():
    cache_dir = get_cache_dir()
    for filename in os.listdir(cache_dir):
        if filename.endswith('.json'):
            try:
                os.remove(os.path.join(cache_dir, filename))
                logger.info(f"缓存文件已删除: {filename}")
            except Exception as e:
                logger.error(f"删除缓存文件失败 {filename}: {e}")


def get_cache_info(cache_name: str) -> Optional[dict]:
    cache_path = get_cache_path(cache_name)

    if not os.path.exists(cache_path):
        return None

    try:
        with open(cache_path, 'r', encoding='utf-8') as f:
            cache_data = json.load(f)

        now = time.time()
        timestamp = cache_data.get("timestamp", 0)
        expires_at = cache_data.get("expires_at", 0)

        return {
            "name": cache_name,
            "cached_time": time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(timestamp)),
            "expires_time": time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(expires_at)) if expires_at != float('inf') else tr("time.never"),
            "remaining_seconds": max(0, int(expires_at - now)),
            "is_expired": now >= expires_at,
            "interval": cache_data.get("interval", "未知"),
        }
    except Exception as e:
        logger.error(f"获取缓存信息失败 {cache_name}: {e}")
        return None


def extract_files():
    if not getattr(sys, 'frozen', False) or not MEIPASS_DIR:
        return

    bundled_folders = ['resource', 'font', 'data']

    for folder in bundled_folders:
        src_folder = os.path.join(MEIPASS_DIR, folder)
        dst_folder = os.path.join(BASE_DIR, folder)

        if not os.path.exists(src_folder):
            continue

        if not os.path.exists(dst_folder):
            try:
                shutil.copytree(src_folder, dst_folder)
                logger.info(f"已提取文件夹: {folder}")
            except Exception as e:
                logger.error(f"提取文件夹 {folder} 失败: {e}")
        else:
            for root, dirs, files in os.walk(src_folder):
                rel_path = os.path.relpath(root, src_folder)
                dst_root = os.path.join(dst_folder, rel_path) if rel_path != '.' else dst_folder

                if not os.path.exists(dst_root):
                    os.makedirs(dst_root, exist_ok=True)

                for file in files:
                    src_file = os.path.join(root, file)
                    dst_file = os.path.join(dst_root, file)
                    if not os.path.exists(dst_file):
                        try:
                            shutil.copy2(src_file, dst_file)
                            logger.info(f"已提取文件: {os.path.join(folder, rel_path, file)}")
                        except Exception as e:
                            logger.error(f"提取文件 {os.path.join(folder, rel_path, file)} 失败：{e}")


def check_autostart():
    try:
        key_path = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_READ)
        try:
            value, _ = winreg.QueryValueEx(key, APP_NAME)
            winreg.CloseKey(key)
            return True, value
        except FileNotFoundError:
            winreg.CloseKey(key)
            return False, None
    except Exception as e:
        logger.error(f"获取开机自启动状态失败: {e}")
        return False, None


def set_autostart(enabled, delay_seconds=5):
    try:
        key_path = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
        try:
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_SET_VALUE)
        except FileNotFoundError:
            key = winreg.CreateKey(winreg.HKEY_CURRENT_USER, key_path)

        if enabled:
            if getattr(sys, 'frozen', False):
                exe_path = sys.executable
                if delay_seconds > 0:
                    command = f'cmd /c "timeout /t {delay_seconds} /nobreak >nul && start \"\" \"{exe_path}\" --autostart"'
                else:
                    command = f'"{exe_path}" --autostart'
            else:
                python_exe = sys.executable
                script_path = os.path.abspath(sys.argv[0]) if sys.argv else os.path.abspath(__file__)
                if delay_seconds > 0:
                    command = f'cmd /c "timeout /t {delay_seconds} /nobreak >nul && start \"\" \"{python_exe}\" \"{script_path}\" --autostart"'
                else:
                    command = f'"{python_exe}" "{script_path}" --autostart'
            winreg.SetValueEx(key, APP_NAME, 0, winreg.REG_SZ, command)
            success, stored_value = check_autostart()
            if success and stored_value == command:
                return True
            else:
                logger.error("设置开机自启动失败")
                return False
        else:
            try:
                winreg.DeleteValue(key, APP_NAME)
            except FileNotFoundError:
                logger.info("开机自启动项不存在")
            winreg.CloseKey(key)
            success, _ = check_autostart()
            if not success:
                return True
            else:
                logger.error("删除开机自启动项失败")
                return False

    except PermissionError as e:
        logger.error(f"设置开机自启动失败 - 权限不足: {e}")
        return False
    except Exception as e:
        logger.error(f"设置开机自启动失败: {e}")
        return False


def sync_autostart_cfg():
    try:
        config_auto_start = cfg.autoStart.value
        actual_auto_start, _ = check_autostart()

        logger.info(f"同步自启动状态 - 配置: {config_auto_start}, 实际: {actual_auto_start}")

        if config_auto_start != actual_auto_start:
            result = set_autostart(config_auto_start)
            if not result:
                logger.error("自启动状态同步失败")
                if actual_auto_start != config_auto_start:
                    cfg.autoStart.value = actual_auto_start
            return result
        else:
            return True

    except Exception as e:
        logger.error(f"同步自启动状态失败: {e}")
        return False


def auto_start_launch():
    return '--autostart' in sys.argv or '/autostart' in sys.argv










_i18n_logger = logging.getLogger("ClassLively.core.i18n")
class LanguageCode(Enum):
    ZH_CN = "zh_CN"
    ZH_TW = "zh_TW"
    EN_US = "en_US"


class TranslationManager(QObject):

    def __init__(self):
        super().__init__()
        self._current_language = LanguageCode.ZH_CN.value
        self._translations: Dict[str, Dict[str, str]] = {}
        self._load_translations()

    def _load_translations(self):
        locale_dir = os.path.join(BASE_DIR, "locale")
        if not os.path.exists(locale_dir):
            _i18n_logger.warning(f"Locale directory not found: {locale_dir}")
            return
        for lang_code in LanguageCode:
            file_path = os.path.join(locale_dir, f"{lang_code.value}.json")
            if os.path.exists(file_path):
                try:
                    with open(file_path, "r", encoding="utf-8") as f:
                        self._translations[lang_code.value] = json.load(f)
                    _i18n_logger.debug(f"Loaded translation: {lang_code.value}")
                except Exception as e:
                    _i18n_logger.error(f"Failed to load translation {lang_code.value}: {e}")
            else:
                _i18n_logger.warning(f"Translation file not found: {file_path}")

    def get_available_languages(self) -> List[str]:
        return [lang.value for lang in LanguageCode]

    def set_language(self, language_code: str) -> bool:
        if language_code not in [lang.value for lang in LanguageCode]:
            _i18n_logger.warning(f"无效语言代码: {language_code}")
            return False
        self._current_language = language_code
        return True

    def tr(self, key: str, **kwargs) -> str:
        lang_translations = self._translations.get(self._current_language, {})
        text = self._get_nested_value(lang_translations, key, key)
        if kwargs:
            try:
                text = text.format(**kwargs)
            except (KeyError, ValueError) as e:
                _i18n_logger.debug(f"Translation format error for key '{key}': {e}")
        return text

    @staticmethod
    def _get_nested_value(d: dict, key: str, default):
        keys = key.split('.')
        value = d
        for k in keys:
            if isinstance(value, dict) and k in value:
                value = value[k]
            else:
                return default
        return value if isinstance(value, str) else default


_translation_manager: Optional[TranslationManager] = None


def get_translation_manager() -> TranslationManager:
    global _translation_manager
    if _translation_manager is None:
        _translation_manager = TranslationManager()
    return _translation_manager


def tr(key: str, **kwargs) -> str:
    return get_translation_manager().tr(key, **kwargs)









# Mixin

class TranslatableWidget:
    def setup_translatable_ui(self):
        pass
