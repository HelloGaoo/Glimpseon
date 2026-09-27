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

"""文件下载模块"""

import os
import ctypes
import shutil
import subprocess
import time

import py7zr
import pythoncom
import requests
import zipfile
from win32com.client import Dispatch

from core.constants import DATA_CACHE, DATA_TEMP
from core.logger import logger
from core.utils import tr

import warnings
warnings.filterwarnings("ignore", message="Unverified HTTPS request")

SEVEN_ZIP_PASSWORD = 'zQt83iOY3xXLfDVg6SJ7ocnapy90I1d62w6jh79WlT0m1qPC8b55HU5Nk4ARZFBs'

PRIORITY_CLASSES = {
    'idle': 0x40,
    'below_normal': 0x00004000,
    'normal': 0x20,
    'above_normal': 0x00008000,
    'high': 0x00000080,
    'realtime': 0x00000100,
}


def set_priority_pid(pid, level='below_normal'):
    try:
        level_const = PRIORITY_CLASSES.get(level, PRIORITY_CLASSES['normal'])
        PROCESS_SET_INFORMATION = 0x0200
        PROCESS_QUERY_INFORMATION = 0x0400
        handle = ctypes.windll.kernel32.OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, False, int(pid))
        if not handle:
            logger.debug(f"set_priority_pid: 进程 {pid} 句柄失败")
            return False
        try:
            res = ctypes.windll.kernel32.SetPriorityClass(handle, int(level_const))
            return bool(res)
        finally:
            ctypes.windll.kernel32.CloseHandle(handle)
    except Exception as e:
        logger.debug(f"set_priority_pid: 设置进程 {pid} 优先级 {level} 失败: {e}")
        return False


def _popen_low_priority(*popen_args, **popen_kwargs):
    process = subprocess.Popen(*popen_args, **popen_kwargs)
    set_priority_pid(process.pid, 'below_normal')
    logger.debug(f"低优先级子进程 pid={process.pid}")
    return process

DOWNLOAD_SOURCES = {
    "original": {
        "name_key": "download.source_github",
        "prefix": "https://github.com"
    },
    "hk": {
        "name_key": "download.source_hk",
        "prefix": "https://hk.gh-proxy.org/https://github.com"
    },
    "cloudflare": {
        "name_key": "download.source_cf",
        "prefix": "https://gh-proxy.org/https://github.com"
    },
    "edgeone": {
        "name_key": "download.source_edgeone",
        "prefix": "https://edgeone.gh-proxy.org/https://github.com"
    },
    "geekertao": {
        "name_key": "download.source_geekertao",
        "prefix": "https://ghfile.geekertao.top/https://github.com"
    }
}

DEFAULT_SOURCE = "hk"
import threading
_current_source = DEFAULT_SOURCE
_source_lock = threading.Lock()


def set_download_src(source_key):
    global _current_source
    with _source_lock:
        if source_key in DOWNLOAD_SOURCES:
            logger.info(f"设置下载源: {source_key}")
            _current_source = source_key
        else:
            logger.warning(f"无效下载源: {source_key} 用默认源: {DEFAULT_SOURCE}")
            _current_source = DEFAULT_SOURCE


CACHE_DIR = DATA_CACHE
TEMP_DIR = DATA_TEMP

DEFAULT_PHASE_ALLOCATION = {
    'download': 70,
    'decompress': 20,
    'install': 10,
}

class Downloader:
    def __init__(self, logger=None, progress_callback=None):
        self.installer_logger = logger
        self.progress_callback = progress_callback
        self._last_progress = {}
        self._last_progress_tier = {}
        if logger:
            logger.debug(f"Downloader 初始化 progress_callback={'已注入' if progress_callback else '未注入'}")

    def set_progress(self, software_name, percent):
        last = self._last_progress.get(software_name, -1)
        if percent == 0 or percent >= last:
            self._last_progress[software_name] = percent
            tier = int(percent // 10)
            if tier != self._last_progress_tier.get(software_name, -1) or percent in (0, 100):
                self._last_progress_tier[software_name] = tier
                if self.installer_logger:
                    self.installer_logger.debug(f"{software_name}: 总进度 {tier * 10}% 当前 {percent}%")
            if self.progress_callback and callable(self.progress_callback):
                try:
                    self.progress_callback(software_name, percent)
                except Exception as e:
                    if self.installer_logger:
                        self.installer_logger.warning(f"进度回调异常: {e}")

    def _calc_phase_offsets(self, allocation: dict):
        order = ['download', 'decompress', 'install']
        offsets = {}
        cur = 0
        for p in order:
            offsets[p] = cur
            cur += allocation.get(p, 0)
        return offsets

    def _update_progress(self, software_name, phase, phase_percent, allocation=None):
        """更新进度

        Returns:
            折算后的总进度百分比
        """
        if allocation is None:
            allocation = DEFAULT_PHASE_ALLOCATION
        offsets = self._calc_phase_offsets(allocation)
        phase_alloc = allocation.get(phase, 0)
        start = offsets.get(phase, 0)
        total = round(start + (phase_percent / 100.0) * phase_alloc, 1)
        self.set_progress(software_name, total)
        return total

    def reset_progress(self, software_name):
        """清掉某软件的进度记录"""
        self._last_progress.pop(software_name, None)
        self._last_progress_tier.pop(software_name, None)
        if self.installer_logger:
            self.installer_logger.debug(f"{software_name}: 进度状态已重置")
    
    def _wait_process(self, software_name, process_name, timeout=30, check_interval=1):
        if self.installer_logger:
            self.installer_logger.info(f"{software_name}: 等待进程 {process_name} 超时{timeout}秒")
        start_time = time.time()
        
        while time.time() - start_time < timeout:
            try:
                result = subprocess.run(
                    ["tasklist", "/FI", f"IMAGENAME eq {process_name}", "/NH"],
                    capture_output=True, text=True, shell=False
                )
                if process_name in result.stdout:
                    if self.installer_logger:
                        self.installer_logger.info(f"{software_name}: 进程 {process_name} 存在")
                    return True
            except Exception as err:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 检查进程 {process_name} 出错 {err}")
            
            time.sleep(check_interval)
        
        if self.installer_logger:
            self.installer_logger.warning(f"{software_name}: 等待进程 {process_name} 超时{timeout}秒")
        return False
    
    def _wait_process_exit(self, software_name, process, timeout=None, check_interval=2):
        if self.installer_logger:
            if timeout:
                self.installer_logger.info(f"{software_name}: 等待进程超时{timeout}秒")
            else:
                self.installer_logger.info(f"{software_name}: 等待进程退出")
        
        if timeout:
            start_time = time.time()
            while time.time() - start_time < timeout:
                if process.poll() is not None:
                    if self.installer_logger:
                        self.installer_logger.info(f"{software_name}: 进程已退出")
                    return True
                time.sleep(check_interval)
            
            if self.installer_logger:
                self.installer_logger.warning(f"{software_name}: 等待进程退出超时 {timeout}秒")
            return False
        else:
            process.wait()
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 进程已退出")
            return True

    def _kill_process(self, software_name, process_name):
        if self.installer_logger:
            self.installer_logger.info(f"{software_name}: 终止进程 {process_name}")
        
        try:
            result = subprocess.run(
                ["taskkill", "/f", "/im", process_name],
                capture_output=True, text=True, shell=False
            )
            if result.returncode == 0:
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 进程 {process_name} 已终止")
            else:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 终止进程 {process_name} 失败: {result.stderr}")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 终止进程出错 {str(err)}")

    def _install_剪辑师(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)

            self.silent_installation(software_name, installer_path)

            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)

            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_知识胶囊(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_掌上看班(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_激活工具(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            output_dir = r"C:\Program Files (x86)\Seewo"
            self._decompress_7Z(software_name, installer_path, output_dir)
            
            source_shortcut = os.path.join(output_dir, "激活工具-WHYOS-Gaoo", "激活工具.lnk")
            dest_shortcut = os.path.join(r"C:\Users\Public\Desktop", "激活工具.lnk")
            
            if os.path.exists(source_shortcut):
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 复制快捷方式到桌面")
                shutil.copy2(source_shortcut, dest_shortcut)
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 快捷方式已复制到桌面")
            else:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 未找到快捷方式: {source_shortcut}")
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃壁纸(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            allocation = cache_file.get('phase_allocation', DEFAULT_PHASE_ALLOCATION)

            output_dir = r"C:\Windows\Web"
            self._decompress_7Z(software_name, installer_path, output_dir)
            self._update_progress(software_name, 'decompress', 100, allocation)

            # 调用 native 更改桌面背景
            from Glimpseon_native import set_wallpaper
            wallpaper_path = os.path.join(output_dir, "img0.jpg")
            if os.path.exists(wallpaper_path):
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 更改桌面背景")
                set_wallpaper(wallpaper_path)
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 桌面背景已更改")
                try:
                    self._update_progress(software_name, 'install', 100, allocation)
                except Exception as e:
                    if self.installer_logger:
                        self.installer_logger.warning(f"{software_name}: 更新壁纸安装阶段进度失败 - {e}")
            else:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 未找到壁纸文件: {wallpaper_path}")
            
            self._update_progress(software_name, 'install', 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 已安装")
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃管家(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃快传(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃集控(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃智能笔(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃易课堂(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃输入法(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_PPT小工具(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃轻白板(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃白板5(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise

    def _install_希沃课堂助手(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃电脑助手(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃导播助手(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃视频展台(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃物联校园(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_远程互动课堂(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_省平台登录插件(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")

            process = _popen_low_priority([installer_path])
            
            self._wait_process(software_name, "省平台登录插件.exe", timeout=15, check_interval=2)
            
            self._kill_process(software_name, "省平台登录插件.exe")
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 已安装")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希象传屏发送端(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃品课小组端(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            install_dir = r"C:\Program Files (x86)\Seewo\SeewoPinK"
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 建目录 {install_dir}")
            os.makedirs(install_dir, exist_ok=True)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 静默安装")
            process = _popen_low_priority([installer_path, "/S"])
            
            # 等待seewoPincoGroup.exe进程出现
            self._wait_process(software_name, "seewoPincoGroup.exe", timeout=20, check_interval=3)
            
            # 等待安装进程退出
            self._wait_process_exit(software_name, process, timeout=45, check_interval=5)
            
            self._kill_process(software_name, "seewoPincoGroup.exe")
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 已安装")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_希沃品课教师端(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 静默安装")

            process = _popen_low_priority([installer_path, "/S"])
            
            # 等待seewoPincoTeacher.exe进程出现
            self._wait_process(software_name, "seewoPincoTeacher.exe", timeout=20, check_interval=3)
            
            # 等待安装进程退出
            self._wait_process_exit(software_name, process, timeout=45, check_interval=5)
            
            # 终止进程
            self._kill_process(software_name, "seewoPincoTeacher.exe")
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 已安装")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_微信(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_QQ(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_UU远程(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_网易云音乐(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
            
            self.set_progress(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise

    def _install_office2021(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:            
            if self.installer_logger:self.installer_logger.info(f"{software_name}: 下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            if self.installer_logger:self.installer_logger.info(f"{software_name}: 解压")
            
            allocation = cache_file.get('phase_allocation', DEFAULT_PHASE_ALLOCATION)
            output_dir = TEMP_DIR
            self._decompress_7Z(software_name, installer_path, output_dir)
            try:
                self._update_progress(software_name, 'decompress', 100, allocation)
            except Exception as e:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 更新解压阶段进度失败 - {e}")
            if self.installer_logger:self.installer_logger.info(f"{software_name}: 安装")
            
            setup_exe = os.path.join(output_dir, "setup.exe")
            config_xml = os.path.join(output_dir, "config.xml")
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 运行 setup.exe /configure config.xml")
            office_process = _popen_low_priority([setup_exe, "/configure", config_xml])

            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 等待安装进程结束")
            office_process.wait()
            if office_process.returncode != 0:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: Office 安装进程返回码非零: {office_process.returncode}")
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装进程已结束")
            
            self.set_progress(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 已安装")
            
            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise
    
    def _install_ClassIsland2(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            
            install_dir = r"C:\ClassIsland2"
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 建目录 {install_dir}")
            os.makedirs(install_dir, exist_ok=True)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 解压文件到: {install_dir}")
            with zipfile.ZipFile(installer_path, 'r') as zip_ref:
                zip_ref.extractall(install_dir)
            
            shortcut_name = "ClassIsland2"
            target_path = os.path.join(install_dir, "ClassIsland.exe")
            public_desktop = os.path.join(os.environ.get("PUBLIC"), "Desktop")
            shortcut_path = os.path.join(public_desktop, f"{shortcut_name}.lnk")
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 创建快捷方式到公用桌面: {shortcut_path}")
            try:
                pythoncom.CoInitialize()
                shell = Dispatch('WScript.Shell')
                shortcut = shell.CreateShortCut(shortcut_path)
                shortcut.TargetPath = target_path
                shortcut.WorkingDirectory = install_dir
                shortcut.IconLocation = target_path
                shortcut.save()
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 快捷方式已创建")
            except Exception as e:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 创建快捷方式失败 - {str(e)}")
            finally:
                try:
                    pythoncom.CoUninitialize()
                except Exception as e:
                    if self.installer_logger:
                        self.installer_logger.warning(f"{software_name}: CoUninitialize 失败 - {e}")

            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)

            self.set_progress(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 已安装")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise

    def _install_ClassWidgets(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装")
            
            install_dir = r"C:\ClassWidgets"
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 建目录 {install_dir}")
            os.makedirs(install_dir, exist_ok=True)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 解压文件到: {install_dir}")
            with zipfile.ZipFile(installer_path, 'r') as zip_ref:
                zip_ref.extractall(install_dir)
            
            shortcut_name = "ClassWidgets"
            target_path = r"C:\ClassWidgets\ClassWidgets.exe"
            
            public_desktop = os.path.join(os.environ.get("PUBLIC"), "Desktop")
            shortcut_path = os.path.join(public_desktop, f"{shortcut_name}.lnk")
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 创建快捷方式到公用桌面: {shortcut_path}")
            try:
                pythoncom.CoInitialize()
                shell = Dispatch('WScript.Shell')
                shortcut = shell.CreateShortCut(shortcut_path)
                shortcut.TargetPath = target_path
                shortcut.WorkingDirectory = r"C:\ClassWidgets"
                shortcut.IconLocation = target_path
                shortcut.save()
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 快捷方式已创建")
            except Exception as e:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 创建快捷方式失败 - {str(e)}")
            finally:
                try:
                    pythoncom.CoUninitialize()
                except Exception as e:
                    if self.installer_logger:
                        self.installer_logger.warning(f"{software_name}: CoUninitialize 失败 - {e}")

            self._clean_tempdir(TEMP_DIR, cache_file["filename"], software_name)

            self.set_progress(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 已安装")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self.set_progress(software_name, 0)
            raise


    def _get_download_url(self, cache_file):
        """获取URL"""
        if "url" in cache_file:
            if self.installer_logger:
                self.installer_logger.debug(f"URL解析: 命中直链 url={cache_file['url']}")
            return cache_file["url"]
        elif "github_path" in cache_file:
            with _source_lock:
                src = _current_source
            prefix = DOWNLOAD_SOURCES[src]["prefix"]
            if self.installer_logger:
                self.installer_logger.debug(f"URL解析: github_path={cache_file['github_path']} 镜像源={src} 前缀={prefix}")
            return f"{prefix}{cache_file['github_path']}"
        if self.installer_logger:
            self.installer_logger.warning(f"URL解析失败: 无 url/github_path 字段 filename={cache_file.get('filename')}")
        return None
    
    def _download_file(self, software_name, cache_file, download_location="Temporary", progress_callback=None, download_complete_callback=None, download_rate_limit=0, progress_update_interval=0.5):
        """下载文件
            software_name: 软件名称
            cache_file: 缓存文件
            download_location: 下载位置"Temporary" 或 "Cache"
            progress_callback: 进度回调函数
            download_complete_callback: 下载完成回调
            download_rate_limit: 限速（bytes/s）
            progress_update_interval: UI更新间隔（s）
            return: 下载路径
        """
        url = self._get_download_url(cache_file)
        if not url:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 下载URL解析失败 filename={cache_file.get('filename')}")
            raise Exception(tr("download.error_no_url"))

        if download_location == "Temporary":
            save_path = os.path.join(TEMP_DIR, cache_file["filename"])
        else:
            save_path = os.path.join(CACHE_DIR, cache_file["filename"])

        os.makedirs(os.path.dirname(save_path), exist_ok=True)

        if self.installer_logger:
            self.installer_logger.debug(f"{software_name}: 保存路径: {save_path} (位置模式: {download_location})")
        if self.installer_logger:
            self.installer_logger.info(f"{software_name}: 下载 {url}")

        allocation = cache_file.get('phase_allocation', DEFAULT_PHASE_ALLOCATION)

        _update_fail_warned = False
        _ext_cb_warned = False
        _cb_fallback_warned = False

        def _internal_progress(p):
            nonlocal _update_fail_warned, _ext_cb_warned
            try:
                total = self._update_progress(software_name, 'download', float(p), allocation)
            except Exception as e:
                total = None
                if not _update_fail_warned:
                    _update_fail_warned = True
                    if self.installer_logger:
                        self.installer_logger.warning(f"{software_name}: 内部进度折算失败 后续不再重复记录 - {e}")
            if progress_callback:
                try:
                    if total is None:
                        progress_callback(software_name, p)
                    else:
                        progress_callback(software_name, total)
                except Exception as e:
                    if self.installer_logger and not _ext_cb_warned:
                        _ext_cb_warned = True
                        self.installer_logger.warning(f"{software_name}: 外部下载进度回调异常 仅记录一次 - {e}")
        max_retries = 3
        retry_count = 0
        headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8",
            "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.8",
            "Accept-Encoding": "gzip, deflate, br",
            "Connection": "keep-alive",
            "Upgrade-Insecure-Requests": "1",
            "Referer": "https://www.seewo.com/",
            "Cache-Control": "max-age=0"
        }

        while retry_count < max_retries:
            try:
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 请求下载 (重试 {retry_count + 1}/{max_retries})")
                with requests.Session() as session:
                    session.headers.update(headers)

                    response = session.get(url, stream=True, timeout=60, allow_redirects=False, verify=False)
                    if response.status_code in (301, 302):
                        redirect_url = response.headers.get("Location")
                        if redirect_url:
                            if self.installer_logger:
                                self.installer_logger.info(f"{software_name}: 跟随重定向到: {redirect_url}")
                            response = session.get(redirect_url, stream=True, timeout=60, verify=False)
                        else:
                            if self.installer_logger:
                                self.installer_logger.warning(f"{software_name}: 重定向缺Location头 状态码={response.status_code}")

                    response.raise_for_status()
                    total_size = int(response.headers.get('content-length', 0))
                    if total_size <= 0:
                        if self.installer_logger:
                            self.installer_logger.warning(f"{software_name}: 响应缺Content-Length 不校验大小")
                    if self.installer_logger:
                        self.installer_logger.info(f"{software_name}: 文件大小: {total_size} bytes")

                    downloaded_size = 0
                    start_time = time.time()
                    window_start = start_time
                    window_downloaded = 0
                    last_update_time = 0

                    with open(save_path, 'wb') as f:
                        for chunk in response.iter_content(chunk_size=8192):
                            if chunk:
                                f.write(chunk)
                                chunk_len = len(chunk)
                                downloaded_size += chunk_len

                                if download_rate_limit and download_rate_limit > 0:
                                    window_downloaded += chunk_len
                                    now_t = time.time()
                                    elapsed_window = now_t - window_start
                                    if elapsed_window >= 1.0:
                                        window_start = now_t
                                        window_downloaded = 0
                                    else:
                                        expected_time = window_downloaded / float(download_rate_limit)
                                        if expected_time > elapsed_window:
                                            time.sleep(expected_time - elapsed_window)

                                now = time.time()
                                if last_update_time == 0 or (now - last_update_time) >= progress_update_interval:
                                    elapsed_time = now - start_time if (now - start_time) > 0 else 1e-6
                                    speed = downloaded_size / elapsed_time
                                    if self.installer_logger:
                                        if speed < 1024:
                                            speed_str = f"{speed:.2f} B/s"
                                        elif speed < 1024 * 1024:
                                            speed_str = f"{speed / 1024:.2f} KB/s"
                                        else:
                                            speed_str = f"{speed / (1024 * 1024):.2f} MB/s"
                                        self.installer_logger.debug(f"{software_name}: 下载速度 {speed_str}")
                                    if total_size > 0:
                                        progress = int((downloaded_size / total_size) * 100)
                                        _internal_progress(progress)
                                    last_update_time = now

                    _internal_progress(100)
                    if self.installer_logger:
                        self.installer_logger.info(f"{software_name}: 已下载 {save_path}")

                    if download_complete_callback:
                        try:
                            download_complete_callback(software_name)
                        except TypeError:
                            if not _cb_fallback_warned:
                                _cb_fallback_warned = True
                                if self.installer_logger:
                                    self.installer_logger.debug(f"{software_name}: 下载回调无 software_name 参数 改无参调用 仅记录一次")
                            download_complete_callback()

                    return save_path
            except requests.exceptions.RequestException as e:
                retry_count += 1
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 下载失败 将重试 ({retry_count}/{max_retries}) - {str(e)}")
                time.sleep(5)
                if retry_count >= max_retries:
                    if self.installer_logger:
                        self.installer_logger.error(f"{software_name}: 下载失败 - {str(e)}", exc_info=True)
                    raise RuntimeError(str(e)) from e
            except OSError as e:
                if self.installer_logger:
                    self.installer_logger.error(f"{software_name}: 文件操作失败 - {str(e)}", exc_info=True)
                raise RuntimeError(str(e)) from e
            except Exception as e:
                if self.installer_logger:
                    self.installer_logger.error(f"{software_name}: 下载异常 - {str(e)}", exc_info=True)
                raise RuntimeError(str(e)) from e
    
    def silent_installation(self, software_name, installer_path):
        """静默安装"""
        if self.installer_logger:
            self.installer_logger.info(f"{software_name}: 静默安装")
            self.installer_logger.debug(f"{software_name}: 安装包路径: {installer_path}")
        
        try:
            if installer_path.endswith('.exe'):
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 安装方式 EXE 静默安装 /S 低优先级启动 标准输出丢弃")
                # 管道内容无人读取,安装器写满缓冲后会永久阻塞,这里直接丢弃
                process = _popen_low_priority(
                    [installer_path, '/S'],
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    shell=False
                )
    
                self._wait_process_exit(software_name, process, timeout=None)
                
                if process.returncode == 0:
                    if self.installer_logger:
                        self.installer_logger.info(f"{software_name}: 已静默安装")
                else:
                    if self.installer_logger:
                        self.installer_logger.error(f"{software_name}: 静默安装失败 返回码: {process.returncode}")
                    raise Exception(f"安装失败，返回码: {process.returncode}")
            else:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 不支持的安装程序类型")
                raise Exception(tr("download.unsupported_type"))
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}")
            raise

    def _decompress_7Z(self, software_name, archive_path, output_dir):
        """解压7z文件"""
        if self.installer_logger:
            self.installer_logger.info(f"{software_name}: 解压到 {output_dir}")
        
        try:
            os.makedirs(output_dir, exist_ok=True)
            with py7zr.SevenZipFile(archive_path, 'r', password=SEVEN_ZIP_PASSWORD) as archive:archive.extractall(output_dir)
            if self.installer_logger:self.installer_logger.info(f"{software_name}: 已解压")
        except Exception as err:
            if self.installer_logger:self.installer_logger.error(f"{software_name}: 解压失败 - {str(err)}")
            raise
    
    def _clean_tempdir(self, temp_dir, filename, software_name, max_retries=3, retry_delay=1.0):
        """清理单个临时文件（cleanup_temp_directory 清理整个目录）"""
        file_path = os.path.join(temp_dir, filename)
        if not os.path.exists(file_path):
            if self.installer_logger:
                self.installer_logger.debug(f"{software_name}: 无需清理 {file_path}")
            return
        if self.installer_logger:
            self.installer_logger.debug(f"{software_name}: 清理安装包 {file_path} 最多重试{max_retries}次")
        
        for attempt in range(max_retries):
            try:
                os.remove(file_path)
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 已清理 {file_path}")
                return
            except PermissionError as err:
                if attempt < max_retries - 1:
                    if self.installer_logger:
                        self.installer_logger.warning(f"{software_name}: 文件被占用 {retry_delay}秒后重试 ({attempt + 1}/{max_retries})")
                    time.sleep(retry_delay)
                else:
                    if self.installer_logger:
                        self.installer_logger.error(f"{software_name}: 清理 {file_path} 失败:{err}")
            except Exception as err:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 清理 {file_path} 失败:{err}")
                break


def cleanup_temp_directory(temp_dir=None, logger=None):
    """temp文件全部删除"""
    if temp_dir is None:temp_dir = TEMP_DIR
    if not os.path.exists(temp_dir):
        if logger:logger.debug(f"跳过清理 {temp_dir}")
        return
    try:
        if logger:logger.info(f"清理临时目录 {temp_dir}")
        count = 0
        for filename in os.listdir(temp_dir):
            file_path = os.path.join(temp_dir, filename)
            try:
                if os.path.isfile(file_path):
                    os.remove(file_path)
                    count += 1
                elif os.path.isdir(file_path):
                    shutil.rmtree(file_path)
                    count += 1
            except Exception as err:
                if logger:logger.warning(f"清理临时文件失败 {file_path} - {err}")
        
        if logger:logger.info(f"临时目录已清理 {count}项")
    except Exception as err:
        if logger:logger.error(f"清理临时目录失败 {err}")
