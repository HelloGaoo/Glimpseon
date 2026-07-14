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
record.json 管理模块
"""

import hashlib
import json
import logging
import os
from datetime import datetime
from pathlib import Path

logger = logging.getLogger("Glimpseon.core.record")


def scan_files(directory: Path) -> dict:
    """扫描目录生成文件清单"""
    files = {}
    directory = Path(directory)
    
    for file in directory.rglob("*"):
        if file.is_file() and file.name != "record.json":
            try:
                rel_path = file.relative_to(directory)
                files[str(rel_path)] = {
                    "hash": hashlib.sha256(file.read_bytes()).hexdigest(),
                    "size": file.stat().st_size
                }
            except Exception as e:
                logger.warning(f"扫描文件失败 {file}: {e}")
    
    return files


def create_record(version: str, app_dir: Path, current: int = 1, partial: bool = False) -> dict:
    """创建 record.json 内容"""
    return {
        "current": current,
        "partial": partial,
        "version": version,
        "files": scan_files(app_dir),
        "variables": {
            "install_time": datetime.now().isoformat()
        }
    }


def save_record(record: dict, record_path: Path):
    """保存 record.json"""
    try:
        record_path = Path(record_path)
        record_path.parent.mkdir(parents=True, exist_ok=True)
        record_path.write_text(json.dumps(record, indent=2, ensure_ascii=False), encoding="utf-8")
        logger.info(f"record.json 已保存: {record_path}")
        return True
    except Exception as e:
        logger.error(f"保存 record.json 失败: {e}")
        return False


def load_record(record_path: Path) -> dict:
    """加载 record.json"""
    try:
        record_path = Path(record_path)
        if not record_path.exists():
            return None
        return json.loads(record_path.read_text(encoding="utf-8"))
    except Exception as e:
        logger.error(f"加载 record.json 失败: {e}")
        return None


def activate_version(version_dir: Path):
    """激活版本（设置 current=1）"""
    version_dir = Path(version_dir)
    record_path = version_dir / "record.json"
    
    record = load_record(record_path)
    if record:
        record["current"] = 1
        record["partial"] = False
        save_record(record, record_path)
        logger.info(f"版本已激活: {version_dir}")


def deactivate_version(version_dir: Path):
    """取消激活版本（设置 current=0）"""
    version_dir = Path(version_dir)
    record_path = version_dir / "record.json"
    
    record = load_record(record_path)
    if record:
        record["current"] = 0
        save_record(record, record_path)
        logger.info(f"版本已取消激活: {version_dir}")


def mark_partial(version_dir: Path, partial: bool = True):
    """标记为部分安装"""
    version_dir = Path(version_dir)
    record_path = version_dir / "record.json"
    
    record = load_record(record_path)
    if record:
        record["partial"] = partial
        save_record(record, record_path)


def get_current_version(package_root: Path) -> Path:
    """获取当前激活版本目录"""
    package_root = Path(package_root)
    
    # 扫描所有 app-* 目录
    app_dirs = [d for d in package_root.iterdir() 
                if d.is_dir() and d.name.startswith("app-")]
    
    valid_versions = []
    for app_dir in app_dirs:
        record_path = app_dir / "record.json"
        if not record_path.exists():
            continue
        
        record = load_record(record_path)
        if not record or record.get("partial", False):
            continue
        
        version_str = app_dir.name.replace("app-", "")
        try:
            version = tuple(map(int, version_str.split(".")))
        except:
            version = (0, 0, 0)
        
        valid_versions.append({
            "path": app_dir,
            "version": version,
            "current": record.get("current", 0)
        })
    
    if not valid_versions:
        return None
    
    # 排序：current=1 优先，然后按版本号降序
    valid_versions.sort(key=lambda x: (-x["current"], -x["version"][0], -x["version"][1], -x["version"][2]))
    
    return valid_versions[0]["path"]


def cleanup_old_versions(package_root: Path):
    """清理非激活的旧版本"""
    package_root = Path(package_root)
    
    for app_dir in package_root.iterdir():
        if not app_dir.is_dir() or not app_dir.name.startswith("app-"):
            continue
        
        record_path = app_dir / "record.json"
        if not record_path.exists():
            continue
        
        record = load_record(record_path)
        if record and record.get("current", 0) == 0:
            try:
                import shutil
                shutil.rmtree(app_dir)
                logger.info(f"已清理旧版本: {app_dir}")
            except Exception as e:
                logger.warning(f"清理旧版本失败 {app_dir}: {e}")