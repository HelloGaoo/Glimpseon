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
进程管理模块
"""

import ctypes
import os
import time
from ctypes import wintypes

import psutil

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
        if self._mutex_handle is not None:return self._is_owner
        self._mutex_handle = kernel32.CreateMutexW(None, True, self.MUTEX_NAME)
        last_error = kernel32.GetLastError()
        if self._mutex_handle is None or self._mutex_handle == 0:
            logger.error(f"创建互斥锁失败，句柄: {self._mutex_handle}")
            return True
        if last_error == ERROR_ALREADY_EXISTS:
            logger.info("检测到已有实例运行 (互斥锁已存在)")
            self._is_owner = False
            return False
        self._is_owner = True
        logger.info("互斥锁获取成功，当前为唯一实例")
        return True
    def release(self):
        if self._mutex_handle is not None and self._mutex_handle != 0:
            if self._is_owner:kernel32.ReleaseMutex(self._mutex_handle)
            kernel32.CloseHandle(self._mutex_handle)
            self._mutex_handle = None
            self._is_owner = False
            logger.info("互斥锁已释放")

    @property
    def is_owner(self) -> bool:
        return self._is_owner
_instance_manager: SingleInstanceManager = None
def get_instance_manager() -> SingleInstanceManager:
    global _instance_manager
    if _instance_manager is None:_instance_manager = SingleInstanceManager()
    return _instance_manager

def check_single_instance() -> bool:
    manager = get_instance_manager()
    return manager.try_acquire()


def release_single_instance():
    manager = get_instance_manager()
    manager.release()


def _is_classlively_process(proc, current_pid: int) -> bool:
    try:
        pid = proc.pid
        if pid == current_pid:return False
        name = proc.name()
        if name == 'ClassLively.exe':return True
        if name in ('python.exe', 'pythonw.exe'):
            try:
                cmdline = proc.cmdline()
                if not cmdline:return False
                cmdline_str = ' '.join(cmdline)
                if 'debugpy' in cmdline_str.lower():return False
                if any('ClassLively' in arg for arg in cmdline):return True
            except (psutil.NoSuchProcess, psutil.AccessDenied, IndexError):pass
    except (psutil.NoSuchProcess, psutil.AccessDenied):pass
    return False


def kill_old_instances() -> bool:
    current_pid = os.getpid()
    logger.info(f"进程: {current_pid}，正在查找旧实例")
    pids_to_kill = []
    for proc in psutil.process_iter(['pid', 'name']):
        try:
            if _is_classlively_process(proc, current_pid):
                pids_to_kill.append(proc.pid)
                logger.info(f"旧实例 PID: {proc.pid}, 名称: {proc.name()}")
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue
    if not pids_to_kill:return True
    terminated_count = 0
    for pid in pids_to_kill:
        try:
            proc = psutil.Process(pid)
            if not proc.is_running():
                logger.warning(f"进程 {pid} 已停止")
                continue
            proc.kill()
            logger.info(f"已终止旧实例 PID: {pid}")
            terminated_count += 1
        except psutil.NoSuchProcess:
            logger.warning(f"进程 {pid} 不存在")
        except psutil.AccessDenied as e:
            logger.error(f"终止进程 {pid}失败: {e}")
        except Exception as e:
            logger.error(f"终止进程 {pid} 失败: {e}", exc_info=True)

    logger.info(f"共终止 {terminated_count} 个")

    start_time = time.time()
    while time.time() - start_time < 5:
        remaining = 0
        for pid in pids_to_kill:
            try:
                if psutil.Process(pid).is_running():
                    remaining += 1
            except psutil.NoSuchProcess:
                continue
        if remaining == 0:
            logger.info("所有旧实例已退出")
            return True
    return True

def force_acquire_single_instance() -> bool:
    manager = get_instance_manager()

    if manager.is_owner:return True
    if manager._mutex_handle is not None and not manager._is_owner:
        kernel32.CloseHandle(manager._mutex_handle)
        manager._mutex_handle = None
        manager._is_owner = False
    kill_old_instances()
    return manager.try_acquire()
