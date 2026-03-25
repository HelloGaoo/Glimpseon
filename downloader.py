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


import ctypes
import os
import subprocess
import time
import shutil
import requests
import py7zr
import urllib3
import zipfile
import pythoncom
from win32com.client import Dispatch
from concurrent.futures import ThreadPoolExecutor

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

SEVEN_ZIP_PASSWORD = 'zQt83iOY3xXLfDVg6SJ7ocnapy90I1d62w6jh79WlT0m1qPC8b55HU5Nk4ARZFBs'

DOWNLOAD_SOURCES = {
    "hk": {
        "name": "香港加速站",
        "prefix": "https://hk.gh-proxy.org/https://github.com"
    },
    "cloudflare": {
        "name": "CloudFlare加速站",
        "prefix": "https://gh-proxy.org/https://github.com"
    },
    "edgeone": {
        "name": "EdgeOne加速站",
        "prefix": "https://edgeone.gh-proxy.org/https://github.com"
    },
    "geekertao": {
        "name": "Geekertao加速站",
        "prefix": "https://ghfile.geekertao.top/https://github.com"
    }
}

DEFAULT_SOURCE = "hk"
current_source = DEFAULT_SOURCE




# 路径设置
if getattr(os.sys, 'frozen', False):
    BASE_DIR = os.path.dirname(os.path.abspath(os.sys.executable))
    MEIPASS_DIR = os.sys._MEIPASS
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))
    MEIPASS_DIR = None

LOGS_DIR = os.path.join(BASE_DIR, "Logs")
CACHE_DIR = os.path.join(BASE_DIR, "cache")
TEMP_DIR = os.path.join(BASE_DIR, "Temporary")
TOOLS_DIR = os.path.join(BASE_DIR, "Tools")
UPDATE_DIR = os.path.join(BASE_DIR, "Update")

SEVEN_ZIP_PATH = os.path.join(TOOLS_DIR, "7z.exe")

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

    def _set_progress_percent(self, software_name, percent):
        try:
            # 失败时回退到0
            last = self._last_progress.get(software_name, -1)
            if percent == 0 or percent >= last:
                self._last_progress[software_name] = percent
                if getattr(self, 'progress_callback', None) and callable(self.progress_callback):
                    try:
                        self.progress_callback(software_name, percent)
                    except Exception as e:
                        if self.installer_logger:
                            self.installer_logger.warning(f"{software_name}: 调用外部进度回调异常 - {e}")
            else:
                pass
        except Exception as e:
            if self.installer_logger:
                self.installer_logger.warning(f"{software_name}: 更新进度回调异常 - {e}")

    def _compute_phase_offsets(self, allocation: dict):
        """返回阶段顺序与每阶段开始偏移量字典。"""
        order = ['download', 'decompress', 'install']
        offsets = {}
        cur = 0
        for p in order:
            offsets[p] = cur
            cur += allocation.get(p, 0)
        return offsets

    def _update_phase_progress(self, software_name, phase, phase_percent, allocation=None):
        """根据单阶段百分比(0-100)更新整体进度。"""
        if allocation is None:
            allocation = DEFAULT_PHASE_ALLOCATION
        offsets = self._compute_phase_offsets(allocation)
        phase_alloc = allocation.get(phase, 0)
        start = offsets.get(phase, 0)
        total = start + (phase_percent / 100.0) * phase_alloc
        total = round(total, 1)
        self._set_progress_percent(software_name, total)
    
    def _wait_for_process(self, software_name, process_name, timeout=30, check_interval=1):
        if self.installer_logger:
            self.installer_logger.info(f"{software_name}: 等待进程 {process_name} 出现，超时 {timeout} 秒")
        start_time = time.time()
        
        while time.time() - start_time < timeout:
            try:
                result = subprocess.run(
                    ["tasklist", "/FI", f"IMAGENAME eq {process_name}", "/NH"],
                    capture_output=True, text=True, shell=False
                )
                if process_name in result.stdout:
                    if self.installer_logger:
                        self.installer_logger.info(f"{software_name}: 进程 {process_name} 已出现")
                    return True
            except Exception as err:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 检查进程 {process_name} 时出错 - {err}")
            
            time.sleep(check_interval)
        
        if self.installer_logger:
            self.installer_logger.warning(f"{software_name}: 等待进程 {process_name} 超时（{timeout} 秒）")
        return False
    
    def _wait_for_process_exit(self, software_name, process, timeout=None, check_interval=2):
        if self.installer_logger:
            if timeout:
                self.installer_logger.info(f"{software_name}: 等待进程退出，超时 {timeout} 秒")
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
                self.installer_logger.warning(f"{software_name}: 等待进程退出超时（{timeout} 秒）")
            return False
        else:
            process.wait()
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 进程已退出")
            return True
    
    def _wait_for_condition(self, software_name, condition_func, timeout=30, check_interval=1):
        if self.installer_logger:
            self.installer_logger.info(f"{software_name}: 等待条件满足，超时 {timeout} 秒")
        start_time = time.time()
        
        while time.time() - start_time < timeout:
            try:
                if condition_func():
                    if self.installer_logger:
                        self.installer_logger.info(f"{software_name}: 条件已满足")
                    return True
            except Exception as err:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 检查条件时出错 - {err}")
            
            time.sleep(check_interval)
        
        if self.installer_logger:
            self.installer_logger.warning(f"{software_name}: 等待条件满足超时（{timeout} 秒）")
        return False
    
    def _kill_process(self, software_name, process_name):
        """终止进程
        
        Args:
            software_name: 软件名称
            process_name: 进程名称
        """
        if self.installer_logger:
            self.installer_logger.info(f"{software_name}: 尝试终止进程: {process_name}")
        
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
                self.installer_logger.error(f"{software_name}: 终止进程时出错 - {str(err)}")

    # 剪辑师安装函数
    def _install_剪辑师(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
    

    
    # 知识胶囊安装函数
    def _install_知识胶囊(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 掌上看班安装函数
    def _install_掌上看班(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 激活工具安装函数
    def _install_激活工具(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始下载")
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
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希沃壁纸安装函数
    def _install_希沃壁纸(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            allocation = cache_file.get('phase_allocation', DEFAULT_PHASE_ALLOCATION)

            output_dir = r"C:\Windows\Web"
            self._decompress_7Z(software_name, installer_path, output_dir)
            try:
                self._update_phase_progress(software_name, 'decompress', 100, allocation)
            except Exception:
                pass
            
            SPI_SETDESKWALLPAPER = 20
            SPIF_UPDATEINIFILE = 0x01
            SPIF_SENDCHANGE = 0x02
            
            wallpaper_path = os.path.join(output_dir, "img0.jpg")
            if os.path.exists(wallpaper_path):
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 更改桌面背景")
                # 调用SystemParametersInfo函数更改桌面背景
                ctypes.windll.user32.SystemParametersInfoW(
                    SPI_SETDESKWALLPAPER, 
                    0, 
                    wallpaper_path, 
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
                )
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 桌面背景已更改")
                try:
                    self._update_phase_progress(software_name, 'install', 100, allocation)
                except Exception:
                    pass
            else:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 未找到壁纸文件: {wallpaper_path}")
            
            self._update_phase_progress(software_name, 'install', 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装完成")
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希沃管家安装函数
    def _install_希沃管家(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        """安装希沃管家 07
        
        Args:
            software_name: 软件名称
            cache_file: 缓存文件信息
        """
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    

    
    # 希沃快传安装函数
    def _install_希沃快传(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希沃集控安装函数
    def _install_希沃集控(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    

    
    # 希沃智能笔安装函数
    def _install_希沃智能笔(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    

    
    # 希沃易课堂安装函数
    def _install_希沃易课堂(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希沃输入法安装函数
    def _install_希沃输入法(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # PPT小工具安装函数
    def _install_PPT小工具(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希沃轻白板安装函数
    def _install_希沃轻白板(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希沃白板5安装函数
    def _install_希沃白板5(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    

    
    # 希沃课堂助手安装函数
    def _install_希沃课堂助手(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希沃电脑助手安装函数
    def _install_希沃电脑助手(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._update_phase_progress(software_name, 'install', 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希沃导播助手安装函数
    def _install_希沃导播助手(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希沃视频展台安装函数
    def _install_希沃视频展台(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希沃物联校园安装函数
    def _install_希沃物联校园(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    

    # 远程互动课堂安装函数
    def _install_远程互动课堂(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    

    
    # 省平台登录插件安装函数
    def _install_省平台登录插件(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始安装")

            process = subprocess.Popen([installer_path])
            
            self._wait_for_process(software_name, "省平台登录插件.exe", timeout=15, check_interval=2)
            
            self._kill_process(software_name, "省平台登录插件.exe")
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装完成")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希象传屏[发送端]安装函数
    def _install_希象传屏发送端(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    
    # 希沃品课[小组端]安装函数
    def _install_希沃品课小组端(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            install_dir = r"C:\Program Files (x86)\Seewo\SeewoPinK"
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 创建安装目录: {install_dir}")
            os.makedirs(install_dir, exist_ok=True)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始静默安装")
            process = subprocess.Popen([installer_path, "/S"])
            
            # 等待seewoPincoGroup.exe进程出现
            self._wait_for_process(software_name, "seewoPincoGroup.exe", timeout=20, check_interval=3)
            
            # 等待安装进程退出
            self._wait_for_process_exit(software_name, process, timeout=45, check_interval=5)
            
            self._kill_process(software_name, "seewoPincoGroup.exe")
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装完成")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 希沃品课[教师端]安装函数
    def _install_希沃品课教师端(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始静默安装")

            process = subprocess.Popen([installer_path, "/S"])
            
            # 等待seewoPincoTeacher.exe进程出现
            self._wait_for_process(software_name, "seewoPincoTeacher.exe", timeout=20, check_interval=3)
            
            # 等待安装进程退出
            self._wait_for_process_exit(software_name, process, timeout=45, check_interval=5)
            
            # 终止进程
            self._kill_process(software_name, "seewoPincoTeacher.exe")
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装完成")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    

    
    # 微信安装函数
    def _install_微信(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # QQ安装函数
    def _install_QQ(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # UU远程安装函数
    def _install_UU远程(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # 网易云音乐安装函数
    def _install_网易云音乐(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            self.silent_installation(software_name, installer_path)
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # office2021安装函数
    def _install_office2021(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始安装")

            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 运行office2021.exe安装程序")
            office_process = subprocess.Popen([installer_path])
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 等待office2021.exe进程结束")
            office_process.wait()
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: office2021.exe进程已结束")
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 检查并结束OfficeC2RClient.exe进程")
            try:
                subprocess.run(["taskkill", "/f", "/im", "OfficeC2RClient.exe"], check=False, shell=False)
            except Exception as e:
                if self.installer_logger:
                    self.installer_logger.error(f"{software_name}: 结束OfficeC2RClient.exe进程时出错: {str(e)}")
            
            def check_process_exited():
                try:
                    result = subprocess.run(
                        ["wmic", "process", "where", "name='OfficeC2RClient.exe'", "get", "name"],
                        capture_output=True, text=True, shell=False
                    )
                    return "OfficeC2RClient.exe" not in result.stdout
                except Exception:
                    return True
            
            self._wait_for_condition(software_name, check_process_exited, timeout=10, check_interval=1)
            
            try:
                subprocess.run(["taskkill", "/f", "/im", "OfficeC2RClient.exe"], check=False, shell=False)
            except Exception as e:
                if self.installer_logger:
                    self.installer_logger.error(f"{software_name}: 再次结束OfficeC2RClient.exe进程时出错: {str(e)}")
            
            self._set_progress_percent(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装完成")
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # ClassIsland2安装函数
    def _install_ClassIsland2(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始安装")
            
            install_dir = r"C:\ClassIsland2"
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 创建安装目录: {install_dir}")
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
                    self.installer_logger.info(f"{software_name}: 快捷方式创建成功")
            except Exception as e:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 创建快捷方式失败 - {str(e)}")
            finally:
                try:
                    pythoncom.CoUninitialize()
                except Exception:
                    pass
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装完成")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    # ClassWidgets安装函数
    def _install_ClassWidgets(self, software_name, cache_file, progress_callback=None, download_complete_callback=None):
        try:
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始下载")
            installer_path = self._download_file(software_name, cache_file, download_location="Temporary", progress_callback=progress_callback, download_complete_callback=download_complete_callback)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 开始安装")
            
            install_dir = r"C:\ClassWidgets"
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 创建安装目录: {install_dir}")
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
                    self.installer_logger.info(f"{software_name}: 快捷方式创建成功")
            except Exception as e:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 创建快捷方式失败 - {str(e)}")
            finally:
                try:
                    pythoncom.CoUninitialize()
                except Exception:
                    pass
            
            self._cleanup_temp_files(TEMP_DIR, cache_file["filename"], software_name)
            
            self._set_progress_percent(software_name, 100)
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 安装完成")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}", exc_info=True)
            self._set_progress_percent(software_name, 0)
            raise
    
    def _get_download_url(self, cache_file):
        """获取下载URL
        
        Args:
            cache_file: 缓存文件信息
            
        Returns:
            str: 下载URL
        """
        if "url" in cache_file:
            return cache_file["url"]
        elif "github_path" in cache_file:
            prefix = DOWNLOAD_SOURCES[current_source]["prefix"]
            return f"{prefix}{cache_file['github_path']}"
        return None
    
    def _download_file(self, software_name, cache_file, download_location="Temporary", progress_callback=None, download_complete_callback=None):
        """下载文件
        
        Args:
            software_name: 软件名称
            cache_file: 缓存文件信息
            download_location: 下载位置 ("Temporary" 或 "Cache")
            progress_callback: 进度回调函数
            download_complete_callback: 下载完成回调函数
            
        Returns:
            str: 下载文件的路径
        """
        url = self._get_download_url(cache_file)
        if not url:
            raise Exception("未找到下载URL")
        
        if download_location == "Temporary":
            save_path = os.path.join(TEMP_DIR, cache_file["filename"])
        else:
            save_path = os.path.join(CACHE_DIR, cache_file["filename"])
        
        os.makedirs(os.path.dirname(save_path), exist_ok=True)
        
        if self.installer_logger:
            self.installer_logger.info(f"{software_name}: 开始下载: {url}")

        allocation = cache_file.get('phase_allocation', DEFAULT_PHASE_ALLOCATION)

        def _internal_progress(p):
            try:
                float_p = float(p)
                self._update_phase_progress(software_name, 'download', float_p, allocation)
                # 总进度
                offsets = self._compute_phase_offsets(allocation)
                start = offsets.get('download', 0)
                total = start + (float_p / 100.0) * allocation.get('download', 0)
                total = round(total, 1)
            except Exception:
                total = None
            if progress_callback:
                try:
                    if total is None:
                        progress_callback(software_name, p)
                    else:
                        progress_callback(software_name, total)
                except Exception:
                    if self.installer_logger:
                        self.installer_logger.warning(f"{software_name}: 外部下载进度回调异常")
        
        try:
            response = requests.get(url, stream=True, verify=False, timeout=30)
            response.raise_for_status()
            
            total_size = int(response.headers.get('content-length', 0))
            downloaded_size = 0
            last_reported_progress = -1
            
            with open(save_path, 'wb') as f:
                for chunk in response.iter_content(chunk_size=8192):
                    if chunk:
                        f.write(chunk)
                        downloaded_size += len(chunk)
                        
                        # 计算下载进度
                        if total_size > 0:
                            progress = (downloaded_size / total_size) * 100
                            if int(progress) > last_reported_progress:
                                last_reported_progress = int(progress)                                # 不记录每次下载进度，减少日志量
                                _internal_progress(progress)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 下载完成：{save_path}")
            
            if download_complete_callback:
                try:
                    download_complete_callback(software_name)
                except TypeError:
                    download_complete_callback()
            
            return save_path
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 下载失败 - {str(err)}")
            raise
    
    def silent_installation(self, software_name, installer_path):
        """静默安装软件
        
        Args:
            software_name: 软件名称
            installer_path: 安装程序路径
        """
        if self.installer_logger:
            self.installer_logger.info(f"{software_name}: 开始静默安装")
        
        try:
            if installer_path.endswith('.exe'):
                process = subprocess.Popen(
                    [installer_path, '/S'],
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    shell=False
                )
    
                self._wait_for_process_exit(software_name, process, timeout=None)
                
                if process.returncode == 0:
                    if self.installer_logger:
                        self.installer_logger.info(f"{software_name}: 静默安装成功")
                else:
                    if self.installer_logger:
                        self.installer_logger.error(f"{software_name}: 静默安装失败，返回码: {process.returncode}")
                    raise Exception(f"安装失败，返回码: {process.returncode}")
            else:
                if self.installer_logger:
                    self.installer_logger.warning(f"{software_name}: 不支持的安装程序类型")
                raise Exception("不支持的安装程序类型")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 安装失败 - {str(err)}")
            raise
    
    def _cleanup_temp_files(self, temp_dir, filename, software_name):
        """清理临时文件
        
        Args:
            temp_dir: 临时目录
            filename: 文件名
            software_name: 软件名称
        """
        try:
            file_path = os.path.join(temp_dir, filename)
            if os.path.exists(file_path):
                os.remove(file_path)
                if self.installer_logger:
                    self.installer_logger.info(f"{software_name}: 已清理临时文件: {file_path}")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.warning(f"{software_name}: 清理临时文件时出错 - {err}")
    
    def _update_status(self, software_name, status):
        """更新状态
        
        Args:
            software_name: 软件名称
            status: 状态信息
        """
        mapping = {
            "安装完成": 100,
            "已安装": 100,
            "安装失败": 0,
            "下载中": 20,
            "解压中": 50,
            "配置中": 80,
        }
        try:
            if status in mapping:
                self._set_progress_percent(software_name, mapping[status])
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: {status}")
        except Exception:
            # 如果更新进度失败，仍然记录原始状态
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: {status}")
    
    def _decompress_7Z(self, software_name, archive_path, output_dir):
        """解压7z文件
        
        Args:
            software_name: 软件名称
            archive_path: 归档文件路径
            output_dir: 输出目录
        """
        if self.installer_logger:
            self.installer_logger.info(f"{software_name}: 开始解压到: {output_dir}")
        
        try:
            os.makedirs(output_dir, exist_ok=True)
            
            # 使用py7zr解压
            with py7zr.SevenZipFile(archive_path, 'r') as archive:
                archive.extractall(output_dir)
            
            if self.installer_logger:
                self.installer_logger.info(f"{software_name}: 解压完成")
        except Exception as err:
            if self.installer_logger:
                self.installer_logger.error(f"{software_name}: 解压失败 - {str(err)}")
            raise
