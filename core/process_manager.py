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
from ctypes import wintypes

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
            logger.info(f"检测到已有实例运行 (互斥锁已存在)")
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
    def is_owner(self) -> bool:return self._is_owner
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
