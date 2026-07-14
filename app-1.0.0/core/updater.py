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
软件更新模块
使用完整下载方式，下载整个更新包到新版本目录
"""

import hashlib
import io
import json
import logging
import os
import re
import shutil
import sys
import zipfile
from datetime import datetime
from pathlib import Path

import requests
import warnings

from core.constants import PACKAGE_ROOT, DATA_TEMP, DATA_CACHE, APP_DIR, ensure_data_dirs
from core.record import create_record, save_record, load_record, activate_version, deactivate_version

warnings.filterwarnings("ignore", message="Unverified HTTPS request")

logger = logging.getLogger("Glimpseon.core.updater")

# GitHub API
GITHUB_API = "https://api.github.com/repos/HelloGaoo/Glimpseon/releases/latest"
GITHUB_RELEASES = "https://github.com/HelloGaoo/Glimpseon/releases"
CHANGELOG_URL = "https://cdn.gh-proxy.org/https://raw.githubusercontent.com/HelloGaoo/Glimpseon/main/changelog.md"


def get_github_changelog(max_retries=3):
    """从 GitHub 获取更新日志"""
    for attempt in range(max_retries):
        try:
            if attempt > 0:
                logger.info(f"第 {attempt + 1} 次重试获取更新日志")
            
            logger.info(f"正在从 GitHub 获取更新日志：{CHANGELOG_URL}")
            response = requests.get(CHANGELOG_URL, timeout=10, verify=False)
            logger.debug(f"更新日志请求返回：{response.status_code}")
            response.raise_for_status()
            
            content = response.text
            logger.debug(f"更新日志内容长度：{len(content)}")
            return content
            
        except requests.exceptions.Timeout:
            logger.warning(f"请求超时（尝试 {attempt + 1}/{max_retries}）")
            if attempt == max_retries - 1:
                logger.error("获取更新日志失败：多次重试后仍超时")
                return None
        except requests.exceptions.RequestException as e:
            logger.error(f"获取更新日志失败：{str(e)}")
            return None
        except Exception as e:
            logger.error(f"获取更新日志时出错：{str(e)}")
            return None
    
    return None


def check_github_version(max_retries=3):
    """从 GitHub 获取最新版本信息"""
    result = {
        'success': False,
        'version': None,
        'download_url': None,
        'changelog': None,
        'error': None
    }
    
    try:
        logger.info(f"正在从 GitHub 获取版本信息：{GITHUB_API}")
        response = requests.get(GITHUB_API, timeout=10, verify=False)
        response.raise_for_status()
        
        release_info = response.json()
        latest_version = release_info.get('tag_name', '')
        
        # 移除 v 前缀
        if latest_version.startswith('v'):
            latest_version = latest_version[1:]
        
        result['version'] = latest_version
        
        # 查找下载链接
        for asset in release_info.get('assets', []):
            if asset['name'].endswith('.zip'):
                result['download_url'] = asset['browser_download_url']
                break
        
        # 获取更新日志
        changelog = get_github_changelog()
        if changelog:
            result['changelog'] = changelog
        else:
            result['changelog'] = f"# 版本 {latest_version}\n\n请访问 {GITHUB_RELEASES} 查看更新详情"
        
        result['success'] = True
        logger.info(f"最新版本：{latest_version}")
        
    except requests.exceptions.Timeout:
        result['error'] = "请求超时"
        logger.error(result['error'])
    except requests.exceptions.RequestException as e:
        result['error'] = f"网络错误：{str(e)}"
        logger.error(result['error'])
    except Exception as e:
        result['error'] = f"解析错误：{str(e)}"
        logger.error(result['error'])
    
    return result


def download_update(download_url, progress_callback=None, max_retries=3):
    """下载更新包"""
    ensure_data_dirs()
    temp_dir = Path(DATA_TEMP) / "update"
    temp_dir.mkdir(parents=True, exist_ok=True)
    download_path = temp_dir / "update.zip"
    
    for attempt in range(max_retries):
        try:
            if attempt > 0:
                logger.info(f"第 {attempt + 1} 次重试下载更新")
            
            logger.info(f"正在下载更新：{download_url}")
            response = requests.get(download_url, stream=True, timeout=60, verify=False)
            response.raise_for_status()
            
            total_size = int(response.headers.get('content-length', 0))
            downloaded_size = 0
            
            with open(download_path, 'wb') as f:
                for chunk in response.iter_content(chunk_size=8192):
                    if chunk:
                        f.write(chunk)
                        downloaded_size += len(chunk)
                        if progress_callback and total_size > 0:
                            progress_callback(downloaded_size, total_size)
            
            logger.info(f"更新下载完成：{download_path}")
            return str(download_path)
            
        except requests.exceptions.Timeout:
            logger.warning(f"下载超时（尝试 {attempt + 1}/{max_retries}）")
            if attempt == max_retries - 1:
                logger.error("下载更新失败：多次重试后仍超时")
                return None
        except requests.exceptions.RequestException as e:
            logger.error(f"下载更新失败：{str(e)}")
            return None
        except Exception as e:
            logger.error(f"下载更新时出错：{str(e)}")
            return False
    
    return None


def extract_update(archive_path, target_version):
    """解压更新包到新版本目录"""
    package_root = Path(PACKAGE_ROOT)
    new_version_dir = package_root / f"app-{target_version}"
    
    try:
        logger.info(f"正在解压更新：{archive_path} -> {new_version_dir}")
        
        # 如果目标目录已存在，先删除
        if new_version_dir.exists():
            shutil.rmtree(new_version_dir)
        
        # 创建临时目录
        temp_dir = Path(DATA_TEMP) / "extract"
        if temp_dir.exists():
            shutil.rmtree(temp_dir)
        temp_dir.mkdir(parents=True, exist_ok=True)
        
        # 解压
        with zipfile.ZipFile(archive_path, 'r') as zf:
            zf.extractall(temp_dir)
        
        # 查找解压后的目录（可能有子目录）
        extracted_items = list(temp_dir.iterdir())
        if len(extracted_items) == 1 and extracted_items[0].is_dir():
            # 只有一个子目录，使用它
            extracted_dir = extracted_items[0]
        else:
            # 直接使用 temp_dir
            extracted_dir = temp_dir
        
        # 移动到目标目录
        shutil.move(str(extracted_dir), str(new_version_dir))
        
        # 创建 record.json（标记为 partial）
        record = create_record(target_version, new_version_dir, current=0, partial=True)
        save_record(record, new_version_dir / "record.json")
        
        logger.info(f"更新解压完成：{new_version_dir}")
        return str(new_version_dir)
        
    except Exception as e:
        logger.error(f"解压更新失败：{str(e)}")
        return None


def deploy_update(new_version_dir):
    """部署更新（激活新版本）"""
    package_root = Path(PACKAGE_ROOT)
    new_version_dir = Path(new_version_dir)
    
    try:
        logger.info(f"正在部署更新：{new_version_dir}")
        
        # 取消所有旧版本的激活状态
        for app_dir in package_root.glob("app-*"):
            if app_dir != new_version_dir:
                record_path = app_dir / "record.json"
                if record_path.exists():
                    deactivate_version(app_dir)
        
        # 激活新版本
        record = load_record(new_version_dir / "record.json")
        if record:
            record["current"] = 1
            record["partial"] = False
            save_record(record, new_version_dir / "record.json")
        
        logger.info("更新部署完成，下次启动将使用新版本")
        return True
        
    except Exception as e:
        logger.error(f"部署更新失败：{str(e)}")
        return False


def cleanup_update_files():
    """清理更新临时文件"""
    temp_dir = Path(DATA_TEMP) / "update"
    extract_dir = Path(DATA_TEMP) / "extract"
    
    try:
        if temp_dir.exists():
            shutil.rmtree(temp_dir)
        if extract_dir.exists():
            shutil.rmtree(extract_dir)
        logger.info("已清理更新临时文件")
    except Exception as e:
        logger.warning(f"清理临时文件失败：{e}")


def cleanup_old_versions():
    """清理非激活的旧版本"""
    package_root = Path(PACKAGE_ROOT)
    
    for app_dir in package_root.glob("app-*"):
        if not app_dir.is_dir():
            continue
        
        record_path = app_dir / "record.json"
        if not record_path.exists():
            continue
        
        record = load_record(record_path)
        if record and record.get("current", 0) == 0:
            try:
                shutil.rmtree(app_dir)
                logger.info(f"已清理旧版本: {app_dir.name}")
            except Exception as e:
                logger.warning(f"清理旧版本失败 {app_dir}: {e}")


def create_update_script(new_version_dir):
    """创建更新脚本（用于手动更新）"""
    new_version_dir = Path(new_version_dir)
    package_root = Path(PACKAGE_ROOT)
    
    script_content = f'''@echo off
chcp 65001 >nul
echo Glimpseon 更新
echo.

echo 新版本已准备好：{new_version_dir.name}
echo 下次启动时将自动使用新版本
echo.

echo 按任意键退出...
pause >nul
'''
    
    script_path = package_root / "update_ready.bat"
    with open(script_path, 'w', encoding='utf-8') as f:
        f.write(script_content)
    
    return str(script_path)


# 兼容旧接口
def check_github_verison(max_retries=3):
    """兼容旧接口"""
    result = check_github_version(max_retries)
    return {
        'success': result['success'],
        'version': result['version'],
        'build_date': None,
        'update_url': result['download_url'],
        'changelog': result['changelog'],
        'error': result['error']
    }