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

"""软件更新模块"""

import logging
import shutil
import zipfile
from pathlib import Path

import requests
import warnings

from core.constants import PACKAGE_ROOT, DATA_TEMP, ensure_data_dirs, VERSION
from core.record import create_record, save_record, load_record, deactivate_version

warnings.filterwarnings("ignore", message="Unverified HTTPS request")

logger = logging.getLogger("Glimpseon.core.updater")

GITHUB_API = "https://api.github.com/repos/HelloGaoo/Glimpseon/releases/latest"
GITHUB_RELEASES = "https://github.com/HelloGaoo/Glimpseon/releases"


def get_github_changelog(max_retries=3):
    for attempt in range(max_retries):
        try:
            if attempt > 0:
                logger.info(f"重试获取更新日志")

            logger.info(f"获取更新日志 {GITHUB_API}")
            response = requests.get(GITHUB_API, timeout=10, verify=False)
            logger.debug(f"更新日志请求返回 {response.status_code}")
            response.raise_for_status()

            body = response.json().get('body') or None
            logger.debug(f"更新日志内容长度 {len(body) if body else 0}")
            return body

        except requests.exceptions.Timeout:
            logger.warning(f"请求超时")
            if attempt == max_retries - 1:
                logger.error("获取更新日志失败")
                return None
        except requests.exceptions.RequestException as e:
            logger.error(f"获取更新日志失败 {str(e)}")
            return None
        except Exception as e:
            logger.error(f"获取更新日志失败 {str(e)}")
            return None

    return None


def check_github_version(max_retries=3):
    result = {
        'success': False,
        'version': None,
        'download_url': None,
        'changelog': None,
        'error': None
    }

    response = None
    last_err = None
    for attempt in range(max_retries):
        try:
            logger.info(f"获取版本信息 {GITHUB_API}")
            response = requests.get(GITHUB_API, timeout=10, verify=False)
            response.raise_for_status()
            break
        except requests.exceptions.Timeout:
            last_err = "请求超时"
            logger.warning(f"{last_err} ({attempt + 1}/{max_retries})")
        except requests.exceptions.RequestException as e:
            last_err = f"网络错误 {str(e)}"
            logger.warning(f"{last_err} ({attempt + 1}/{max_retries})")

    if response is None:
        result['error'] = last_err or "请求失败"
        logger.error(result['error'])
        return result

    try:
        release_info = response.json()
        latest_version = release_info.get('tag_name', '')

        if latest_version.startswith('v'):
            latest_version = latest_version[1:]

        result['version'] = latest_version
        logger.debug(f"接口返回版本号: {latest_version}")

        for asset in release_info.get('assets', []):
            if asset['name'].endswith('.zip'):
                result['download_url'] = asset['browser_download_url']
                break

        if result['download_url']:
            logger.info(f"找到更新包: {result['download_url']}")
        else:
            logger.warning(f"最新版本 {latest_version} 无 .zip 更新包")

        changelog = release_info.get('body') or None
        if changelog:
            result['changelog'] = changelog
        else:
            logger.warning("Release 无描述 用默认内容")
            result['changelog'] = (
                f"# 版本 {latest_version}\n\n"
                f"请访问 [Releases 页]({GITHUB_RELEASES}) 查看更新详情"
            )

        result['success'] = True
        logger.info(f"最新版本 {latest_version}")
        logger.info(f"版本 {VERSION} vs 远端 {latest_version} -> {'已是最新' if latest_version == VERSION else '有可用更新'}")

    except Exception as e:
        result['error'] = f"解析错误 {str(e)}"
        logger.error(result['error'])

    return result


def download_update(download_url, progress_callback=None, max_retries=3):
    ensure_data_dirs()
    temp_dir = Path(DATA_TEMP) / "update"
    temp_dir.mkdir(parents=True, exist_ok=True)
    download_path = temp_dir / "update.zip"
    logger.info(f"更新包下载路径: {download_path}")

    for attempt in range(max_retries):
        try:
            if attempt > 0:
                logger.info(f"第{attempt + 1}次重试下载更新")

            logger.info(f"下载更新 {download_url}")
            response = requests.get(download_url, stream=True, timeout=60, verify=False)
            response.raise_for_status()

            total_size = int(response.headers.get('content-length', 0))
            if total_size <= 0:
                logger.warning("更新包响应缺Content-Length 无下载进度")
            downloaded_size = 0

            with open(download_path, 'wb') as f:
                for chunk in response.iter_content(chunk_size=8192):
                    if chunk:
                        f.write(chunk)
                        downloaded_size += len(chunk)
                        if progress_callback and total_size > 0:
                            progress_callback(downloaded_size, total_size)

            logger.info(f"已下载 {downloaded_size}/{total_size}字节")
            logger.info(f"更新已下载 {download_path}")
            return str(download_path)

        except requests.exceptions.Timeout:
            logger.warning(f"下载超时")
            if attempt == max_retries - 1:
                logger.error("下载更新失败")
                return None
        except requests.exceptions.RequestException as e:
            logger.error(f"下载更新失败 {str(e)}")
            return None
        except Exception as e:
            logger.error(f"下载更新出错 {str(e)}")
            return None

    return None


def extract_update(archive_path, target_version):
    package_root = Path(PACKAGE_ROOT)
    new_version_dir = package_root / f"app-{target_version}"

    try:
        logger.info(f"解压更新 {archive_path} -> {new_version_dir}")

        if new_version_dir.exists():
            logger.debug(f"移除已存在的新版本目录: {new_version_dir}")
            shutil.rmtree(new_version_dir)

        temp_dir = Path(DATA_TEMP) / "extract"
        if temp_dir.exists():
            logger.debug(f"移除残留解压临时目录: {temp_dir}")
            shutil.rmtree(temp_dir)
        temp_dir.mkdir(parents=True, exist_ok=True)

        with zipfile.ZipFile(archive_path, 'r') as zf:
            zf.extractall(temp_dir)

        extracted_items = list(temp_dir.iterdir())
        logger.debug(f"解压得到{len(extracted_items)}个顶层条目")
        if len(extracted_items) == 1 and extracted_items[0].is_dir():
            extracted_dir = extracted_items[0]
        else:
            extracted_dir = temp_dir

        shutil.move(str(extracted_dir), str(new_version_dir))

        record = create_record(target_version, new_version_dir, current=0, partial=True)
        save_record(record, new_version_dir / "record.json")

        logger.info(f"更新已解压 {new_version_dir}")
        return str(new_version_dir)

    except Exception as e:
        logger.error(f"解压更新失败 {str(e)}")
        return None


def deploy_update(new_version_dir):
    package_root = Path(PACKAGE_ROOT)
    new_version_dir = Path(new_version_dir)

    try:
        logger.info(f"更新 {new_version_dir}")

        for app_dir in package_root.glob("app-*"):
            if app_dir != new_version_dir:
                record_path = app_dir / "record.json"
                if record_path.exists():
                    logger.debug(f"停用旧版本目录: {app_dir}")
                    deactivate_version(app_dir)

        record = load_record(new_version_dir / "record.json")
        if record:
            logger.debug(f"新版本记录已加载 {record.get('version')}")
            record["current"] = 1
            record["partial"] = False
            save_record(record, new_version_dir / "record.json")
        else:
            logger.warning(f"新版本目录缺record.json {new_version_dir}")

        logger.info("已更新")
        return True

    except Exception as e:
        logger.error(f"更新失败 {str(e)}")
        return False


def cleanup_update_files():
    temp_dir = Path(DATA_TEMP) / "update"
    extract_dir = Path(DATA_TEMP) / "extract"
    logger.debug(f"清理更新临时文件: {temp_dir} {extract_dir}")

    try:
        if temp_dir.exists():
            shutil.rmtree(temp_dir)
        if extract_dir.exists():
            shutil.rmtree(extract_dir)
        logger.info("已清理更新临时文件")
    except Exception as e:
        logger.warning(f"清理临时文件失败 {e}")


def create_update_script(new_version_dir):
    new_version_dir = Path(new_version_dir)
    package_root = Path(PACKAGE_ROOT)
    logger.debug(f"准备生成更新脚本 新版本目录: {new_version_dir.name}")

    script_content = f'''@echo off
chcp 65001 >nul
echo Glimpseon 更新
echo.

echo 新版本已准备好 {new_version_dir.name}
echo 下次启动时将自动使用新版本
echo.

echo 按任意键退出...
pause >nul
'''

    script_path = package_root / "update_ready.bat"
    with open(script_path, 'w', encoding='utf-8') as f:
        f.write(script_content)

    logger.info(f"更新脚本已生成: {script_path}")
    return str(script_path)


def check_github_version_legacy(max_retries=3):
    """兼容"""
    logger.debug("走兼容版本检查接口")
    result = check_github_version(max_retries)
    return {
        'success': result['success'],
        'version': result['version'],
        'build_date': None,
        'update_url': result['download_url'],
        'changelog': result['changelog'],
        'error': result['error']
    }