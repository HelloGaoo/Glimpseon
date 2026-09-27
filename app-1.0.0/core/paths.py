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
路径初始化模块
"""

import json
import logging
import os
import sys
from pathlib import Path

logger = logging.getLogger("Glimpseon.core.paths")


def _detect_package_root() -> str:
    env_root = os.environ.get("Glimpseon_PackageRoot")
    if env_root and os.path.isdir(env_root):
        root = os.path.normpath(env_root)
        logger.debug(f"包根目录: 环境变量指定 {root}")
        return root

    if getattr(sys, 'frozen', False):
        root = os.path.dirname(os.path.abspath(sys.executable))
        logger.debug(f"包根目录: 冻结模式 {root}")
        return root

    current = Path(__file__).resolve()
    root = str(current.parent.parent.parent)
    logger.debug(f"包根目录: 按模块位置推导 {root}")
    return root


def _detect_app_dir(package_root: str) -> str:
    env_app = os.environ.get("Glimpseon_AppDir")
    if env_app and os.path.isdir(env_app):
        app = os.path.normpath(env_app)
        logger.debug(f"应用目录: 环境变量指定 {app}")
        return app

    try:
        for entry in os.listdir(package_root):
            if not entry.startswith("app-"):
                continue
            record_path = os.path.join(package_root, entry, "record.json")
            if not os.path.isfile(record_path):
                continue
            try:
                with open(record_path, 'r', encoding='utf-8') as f:
                    record = json.load(f)
                if record.get("current", 0) == 1 and not record.get("partial", False):
                    logger.debug(f"应用目录: 用 {entry} (record.json current=1)")
                    return os.path.join(package_root, entry)
            except (json.JSONDecodeError, OSError) as e:
                logger.debug(f"应用目录: record.json 读取失败 {entry}: {e}")
                continue
    except OSError as e:
        logger.warning(f"应用目录: 扫描包根目录失败: {e}")

    logger.warning(f"应用目录: 未找到有效 app-x.y.z 用包根目录 {package_root}")
    return package_root


def _detect_meipass() -> str | None:
    if getattr(sys, 'frozen', False):
        return getattr(sys, '_MEIPASS', None)
    return None


PACKAGE_ROOT = _detect_package_root()
APP_DIR = _detect_app_dir(PACKAGE_ROOT)
MEIPASS_DIR = _detect_meipass()

DATA_ROOT = os.path.join(PACKAGE_ROOT, "data")
DATA_CONFIG = os.path.join(DATA_ROOT, "config")
DATA_LOG = os.path.join(DATA_ROOT, "log")
DATA_CACHE = os.path.join(DATA_ROOT, "cache")
DATA_TEMP = os.path.join(DATA_ROOT, "temp")
DATA_PROFILE = os.path.join(DATA_ROOT, "profile")
DATA_USER = os.path.join(DATA_ROOT, "user")
DATA_ICON = os.path.join(DATA_ROOT, "icon")
DATA_WALLPAPER = os.path.join(DATA_ROOT, "wallpaper")
DATA_CLASSPHOTOS = os.path.join(DATA_ROOT, "classphotos")
DATA_NOTES = os.path.join(DATA_ROOT, "notes")

BASE_DIR = PACKAGE_ROOT
WALLPAPER_DIR = DATA_WALLPAPER

_record_path = os.path.join(APP_DIR, "record.json")
try:
    with open(_record_path, 'r', encoding='utf-8') as _f:
        _record = json.load(_f)
    VERSION = _record.get("version", "1.0.0")
    BUILD_DATE = _record.get("build_date", "")
except (json.JSONDecodeError, OSError) as e:
    logger.warning(f"record.json 读取失败 用默认版本号: {e}")
    VERSION = "1.0.0"
    BUILD_DATE = ""


def ensure_data_dirs():
    dirs = [
        DATA_ROOT, DATA_CONFIG, DATA_LOG, DATA_CACHE,
        DATA_TEMP, DATA_PROFILE, DATA_USER, DATA_ICON, DATA_WALLPAPER,
        DATA_CLASSPHOTOS, DATA_NOTES
    ]
    created = []
    for d in dirs:
        if not os.path.exists(d):
            try:
                os.makedirs(d, exist_ok=True)
                created.append(d)
            except OSError as e:
                logger.warning(f"数据目录创建失败: {d} - {e}")
    if created:
        logger.debug(f"数据目录已创建{len(created)}个: {[os.path.basename(c) for c in created]}")


def get_resource_path(relative_path: str) -> str:
    """查找顺序：APP_DIR MEIPASS_DIR  APP_DIR"""
    app_path = os.path.join(APP_DIR, relative_path)
    if os.path.exists(app_path):
        return app_path

    if MEIPASS_DIR:
        meipass_path = os.path.join(MEIPASS_DIR, relative_path)
        if os.path.exists(meipass_path):
            logger.debug(f"资源用 Meipass: {relative_path}")
            return meipass_path

    logger.debug(f"资源未命中 返回应用目录路径: {relative_path}")
    return app_path