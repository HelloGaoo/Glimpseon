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
路径初始化模块 - 最先加载，无项目依赖

使用方法：
    from core.paths import PACKAGE_ROOT, APP_DIR, DATA_CONFIG, ...

环境变量（由启动器设置）：
    Glimpseon_PackageRoot - 根目录路径
    Glimpseon_AppDir      - 版本目录路径
"""

import json
import os
import sys
from pathlib import Path


def _detect_package_root() -> str:
    """检测根目录"""
    # 优先使用环境变量
    env_root = os.environ.get("Glimpseon_PackageRoot")
    if env_root and os.path.isdir(env_root):
        return os.path.normpath(env_root)

    # 打包模式：exe 所在目录
    if getattr(sys, 'frozen', False):
        return os.path.dirname(os.path.abspath(sys.executable))

    # 开发模式：paths.py -> core -> app-* -> 根目录
    # 向上三级
    current = Path(__file__).resolve()
    return str(current.parent.parent.parent)


def _detect_app_dir(package_root: str) -> str:
    """检测版本目录"""
    # 优先使用环境变量
    env_app = os.environ.get("Glimpseon_AppDir")
    if env_app and os.path.isdir(env_app):
        return os.path.normpath(env_app)

    # 扫描 app-* 目录，找 current=1 的
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
                    return os.path.join(package_root, entry)
            except:
                continue
    except:
        pass

    # 兼容：返回根目录
    return package_root


def _detect_meipass() -> str | None:
    """检测打包资源目录"""
    if getattr(sys, 'frozen', False):
        return getattr(sys, '_MEIPASS', None)
    return None


# === 初始化路径常量 ===
# 这些在模块加载时就确定了，之后不会改变

PACKAGE_ROOT = _detect_package_root()
APP_DIR = _detect_app_dir(PACKAGE_ROOT)
MEIPASS_DIR = _detect_meipass()

# data 目录（用户数据）
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

# 兼容别名
BASE_DIR = PACKAGE_ROOT
WALLPAPER_DIR = DATA_WALLPAPER

# 从 record.json 读取版本信息
_record_path = os.path.join(APP_DIR, "record.json")
try:
    with open(_record_path, 'r', encoding='utf-8') as _f:
        _record = json.load(_f)
    VERSION = _record.get("version", "1.0.0")
    BUILD_DATE = _record.get("build_date", "")
except:
    VERSION = "1.0.0"
    BUILD_DATE = ""


def ensure_data_dirs():
    """确保所有数据目录存在"""
    dirs = [
        DATA_ROOT, DATA_CONFIG, DATA_LOG, DATA_CACHE,
        DATA_TEMP, DATA_PROFILE, DATA_USER, DATA_ICON, DATA_WALLPAPER,
        DATA_CLASSPHOTOS
    ]
    for d in dirs:
        if not os.path.exists(d):
            try:
                os.makedirs(d, exist_ok=True)
            except:
                pass


def get_resource_path(relative_path: str) -> str:
    """
    获取资源文件路径

    查找顺序：
    1. APP_DIR/relative_path
    2. MEIPASS_DIR/relative_path (打包资源)
    3. 返回 APP_DIR/relative_path (即使不存在)
    """
    # APP_DIR 优先
    app_path = os.path.join(APP_DIR, relative_path)
    if os.path.exists(app_path):
        return app_path

    # MEIPASS (打包资源)
    if MEIPASS_DIR:
        meipass_path = os.path.join(MEIPASS_DIR, relative_path)
        if os.path.exists(meipass_path):
            return meipass_path

    return app_path