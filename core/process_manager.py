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
提供 ClassLively 进程检测和管理功能
"""

import os
import time

import psutil

from core.logger import logger


def _is_classlively_process(proc_info, current_pid=None):
    """
    检查进程是否是 ClassLively 进程
    
    Args:
        proc_info: 进程信息字典，包含 'pid', 'name', 'cmdline'
        current_pid: 当前进程的 PID，如果提供则跳过该进程
        
    Returns:
        bool: 如果是 ClassLively 进程返回 True，否则返回 False
    """
    if current_pid is not None and proc_info['pid'] == current_pid:
        return False
    
    if proc_info['name'] == 'ClassLively.exe':
        return True
    
    if proc_info['name'] in ['python.exe', 'pythonw.exe']:
        try:
            cmdline = proc_info['cmdline']
            if not cmdline:
                return False
            cmdline_str = ' '.join(cmdline)
            if 'debugpy' in cmdline_str.lower():
                return False
            if any('ClassLively' in arg for arg in cmdline):
                return True
        except (psutil.NoSuchProcess, psutil.AccessDenied, IndexError):
            pass
    
    return False


def _find_classlively_processes(current_pid=None):
    """
    查找所有 ClassLively 进程
    
    Args:
        current_pid: 当前进程的 PID，如果提供则跳过该进程
        
    Returns:
        list: 包含进程 PID 的列表
    """
    pids = []
    for proc in psutil.process_iter(['pid', 'name', 'cmdline']):
        try:
            if _is_classlively_process(proc.info, current_pid):
                pids.append(proc.info['pid'])
                logger.info(f"发现 ClassLively 进程 PID: {proc.info['pid']}, 名称: {proc.info['name']}")
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue
    return pids


def _wait_for_processes_exit(pids, max_wait=3, check_interval=0.5):
    """
    等待进程退出
    
    Args:
        pids: 要等待的进程 PID 列表
        max_wait: 最大等待时间（秒）
        check_interval: 检查间隔（秒）
        
    Returns:
        int: 仍在运行的进程数量
    """
    start_time = time.time()
    
    while time.time() - start_time < max_wait:
        remaining = 0
        for pid in pids:
            try:
                proc = psutil.Process(pid)
                if proc.is_running():
                    remaining += 1
            except psutil.NoSuchProcess:
                continue
        if remaining == 0:
            break
        time.sleep(check_interval)
    
    return remaining


def check_old_instances():
    """检查是否有旧的 ClassLively 在运行"""
    try:
        current_pid = os.getpid()
        logger.debug(f"当前进程 PID: {current_pid}")
        
        processes = _find_classlively_processes(current_pid)
        
        if processes:
            logger.info(f"发现 {len(processes)} 个旧进程")
            return True
        
        logger.debug("未发现旧进程")
        return False
    except ImportError:
        return False
    except Exception as e:
        logger.error(f"检查旧实例失败: {e}")
        return False


def terminate_old_instances():
    """终止所有旧的 ClassLively 进程"""
    try:
        current_pid = os.getpid()
        logger.info(f"当前进程 PID: {current_pid}")
        
        processes_to_kill = _find_classlively_processes(current_pid)
        
        if not processes_to_kill:
            logger.info("未发现需要终止的进程")
            return True
        
        logger.info(f"准备终止 {len(processes_to_kill)} 个进程: {processes_to_kill}")
        terminated_count = 0
        
        for pid in processes_to_kill:
            try:
                logger.info(f"正在终止进程 {pid}")
                proc = psutil.Process(pid)
                if not proc.is_running():
                    logger.warning(f"进程 {pid} 已经停止")
                    continue
                proc.kill()
                logger.info(f"已强制终止 PID {pid}")
                terminated_count += 1
            except psutil.NoSuchProcess:
                logger.warning(f"进程 {pid} 已不存在")
                continue
            except psutil.AccessDenied as e:
                logger.error(f"无法终止进程 {pid}，权限不足: {e}")
                continue
            except Exception as e:
                logger.error(f"终止进程 {pid} 时发生错误: {e}", exc_info=True)
                continue
        
        logger.info(f"终止循环完成，共终止 {terminated_count} 个进程")
        
        if terminated_count > 0:
            logger.info(f"等待进程退出...")
            remaining = _wait_for_processes_exit(processes_to_kill, max_wait=3)
            
            if remaining > 0:
                logger.warning(f"仍有 {remaining} 个旧进程未退出，但将继续启动")
            else:
                logger.info("所有旧进程已退出")
        
        return True
    except ImportError as e:
        logger.error(f"缺少 psutil 模块: {e}")
        return False
    except Exception as e:
        logger.error(f"终止旧进程失败: {e}", exc_info=True)
        return False
