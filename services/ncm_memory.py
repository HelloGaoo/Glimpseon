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
网易云音乐内存读取模块
基于 aliencaocao/netease_cloudmusic_discord_rpc 项目
使用 pymem 读取进程内存获取实时播放进度和歌曲ID
"""

import ctypes
import logging
import os
import re
import struct
import sys
from typing import Optional, Tuple

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

logger = logging.getLogger(__name__)

PYMEM_AVAILABLE = False
try:
    import pymem
    import pymem.process
    PYMEM_AVAILABLE = True
    logger.info("pymem 已加载，内存读取功能可用")
except ImportError:
    logger.warning("pymem 未安装，内存读取功能不可用。请运行: pip install pymem")
except Exception as e:
    logger.warning(f"加载 pymem 失败: {e}")

PSUTIL_AVAILABLE = False
try:
    import psutil
    PSUTIL_AVAILABLE = True
except ImportError:
    pass

WIN32_AVAILABLE = False
try:
    from win32api import GetFileVersionInfo, HIWORD, LOWORD
    WIN32_AVAILABLE = True
except ImportError:
    pass


V2_OFFSETS = {
    '2.7.1.1669': {'current': 0x8C8AF8, 'song_array': 0x8E9044},
    '2.10.3.3613': {'current': 0xA39550, 'song_array': 0xAE8F80},
    '2.10.5.3929': {'current': 0xA47548, 'song_array': 0xAF6FC8},
    '2.10.6.3993': {'current': 0xA65568, 'song_array': 0xB15654},
    '2.10.7.4239': {'current': 0xA66568, 'song_array': 0xB16974},
    '2.10.8.4337': {'current': 0xA74570, 'song_array': 0xB24F28},
    '2.10.10.4509': {'current': 0xA77580, 'song_array': 0xB282CC},
    '2.10.10.4689': {'current': 0xA79580, 'song_array': 0xB2AD10},
    '2.10.11.4930': {'current': 0xA7A580, 'song_array': 0xB2BCB0},
    '2.10.12.5241': {'current': 0xA7A580, 'song_array': 0xB2BCB0},
    '2.10.13.6067': {'current': 0xA7A590, 'song_array': 0xB2BCD0},
}

V3_AUDIO_PLAYER_PATTERN = b"\x48\x8D\x0D\x00\x00\x00\x00\xE8\x00\x00\x00\x00\x48\x8D\x0D\x00\x00\x00\x00\xE8\x00\x00\x00\x00\x90\x48\x8D\x0D\x00\x00\x00\x00\xE8\x00\x00\x00\x00\x48\x8D\x05\x00\x00\x00\x00\x48\x8D\xA5\x00\x00\x00\x00\x5F\x5D\xC3\xCC\xCC\xCC\xCC\xCC\x48\x89\x4C\x24\x00\x55\x57\x48\x81\xEC\x00\x00\x00\x00\x48\x8D\x6C\x24\x00\x48\x8D\x7C\x24"
V3_AUDIO_SCHEDULE_PATTERN = b"\x66\x0F\x2E\x0D\x00\x00\x00\x00\x7A\x00\x75\x00\x66\x0F\x2E\x15"

RE_SONG_ID = re.compile(r'(\d+)')


def _pattern_to_mask(pattern: bytes) -> Tuple[bytes, str]:
    """将带通配符的模式转换为pymem可用的格式"""
    mask = ""
    new_pattern = b""
    i = 0
    while i < len(pattern):
        b = pattern[i]
        if b == 0x00:
            mask += "?"
            new_pattern += b"\x00"
            i += 1
        else:
            mask += "x"
            new_pattern += bytes([b])
            i += 1
    return new_pattern, mask


class NCMProgressReader:
    def __init__(self):
        self._pm: Optional[pymem.Pymem] = None
        self._pid = 0
        self._version = ''
        self._is_v3 = False
        self._v3_schedule_ptr = 0
        self._v3_audio_player_ptr = 0
        self._cloudmusic_dll_base = 0
        self._first_run = True
        self._last_song_id = ''
        self._last_playback_time = 0.0
    
    @property
    def is_available(self) -> bool:
        return PYMEM_AVAILABLE and PSUTIL_AVAILABLE and WIN32_AVAILABLE
    
    def _find_process(self) -> Tuple[int, str]:
        if not PSUTIL_AVAILABLE:
            return 0, ''
        
        candidates = []
        for proc in psutil.process_iter(attrs=['name', 'pid']):
            if proc.info['name'] and proc.info['name'].lower() == 'cloudmusic.exe':
                try:
                    cmdline = proc.cmdline()
                    if any('--type=' in arg for arg in cmdline):
                        continue
                    candidates.append(proc)
                except (psutil.NoSuchProcess, psutil.AccessDenied):
                    continue
        
        if not candidates:
            return 0, ''
        
        proc = candidates[0]
        try:
            exe_path = proc.exe()
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            return 0, ''
        
        if not WIN32_AVAILABLE:
            return proc.info['pid'], ''
        
        try:
            ver_info = GetFileVersionInfo(exe_path, '\\')
            ver = (f"{HIWORD(ver_info['FileVersionMS'])}.{LOWORD(ver_info['FileVersionMS'])}."
                   f"{HIWORD(ver_info['FileVersionLS'])}.{LOWORD(ver_info['FileVersionLS'])}")
            return proc.info['pid'], ver
        except Exception as e:
            logger.debug(f"获取版本失败: {e}")
            return proc.info['pid'], ''
    
    def _get_cloudmusic_dll_base(self) -> int:
        if not self._pm:
            return 0
        try:
            for module in self._pm.list_modules():
                if module.name.lower() == 'cloudmusic.dll':
                    return module.lpBaseOfDll
        except Exception as e:
            logger.error(f"获取 cloudmusic.dll 基址失败: {e}")
        return 0
    
    def _read_float64(self, address: int) -> float:
        if not self._pm:
            return 0.0
        try:
            data = self._pm.read_bytes(address, 8)
            return struct.unpack('d', data)[0]
        except Exception as e:
            logger.debug(f"读取float64失败: {e}")
            return 0.0
    
    def _read_uint64(self, address: int) -> int:
        if not self._pm:
            return 0
        try:
            data = self._pm.read_bytes(address, 8)
            return struct.unpack('<Q', data)[0]
        except Exception:
            return 0
    
    def _read_uint32(self, address: int) -> int:
        if not self._pm:
            return 0
        try:
            data = self._pm.read_bytes(address, 4)
            return struct.unpack('<I', data)[0]
        except Exception:
            return 0
    
    def _read_int32(self, address: int) -> int:
        if not self._pm:
            return 0
        try:
            data = self._pm.read_bytes(address, 4)
            return struct.unpack('<i', data)[0]
        except Exception:
            return 0
    
    def _read_bytes(self, address: int, size: int) -> bytes:
        if not self._pm:
            return b''
        try:
            return self._pm.read_bytes(address, size)
        except Exception:
            return b''
    
    def _aob_scan(self, module_base: int, module_size: int, pattern: bytes) -> int:
        """AOB扫描，在模块内存中查找特征码"""
        try:
            data = self._pm.read_bytes(module_base, module_size)
            
            pattern_len = len(pattern)
            for i in range(len(data) - pattern_len):
                match = True
                for j in range(pattern_len):
                    if pattern[j] == 0x00:
                        continue
                    if data[i + j] != pattern[j]:
                        match = False
                        break
                if match:
                    return module_base + i
            return 0
        except Exception as e:
            logger.error(f"AOB扫描失败: {e}")
            return 0
    
    def _scan_v3_offsets(self) -> Tuple[int, int]:
        if not self._pm or not self._cloudmusic_dll_base:
            return 0, 0
        
        try:
            dll_size = 0
            for module in self._pm.list_modules():
                if module.name.lower() == 'cloudmusic.dll':
                    dll_size = module.SizeOfImage
                    break
            
            if dll_size == 0:
                return 0, 0
            
            schedule_addr = self._aob_scan(self._cloudmusic_dll_base, dll_size, V3_AUDIO_SCHEDULE_PATTERN)
            if not schedule_addr:
                logger.warning("V3 AOB扫描: AudioSchedulePattern 未找到")
                return 0, 0
            
            text_addr = schedule_addr + 4
            displacement = self._read_int32(text_addr)
            schedule_ptr = text_addr + displacement + 4
            
            player_addr = self._aob_scan(self._cloudmusic_dll_base, dll_size, V3_AUDIO_PLAYER_PATTERN)
            if not player_addr:
                logger.warning("V3 AOB扫描: AudioPlayerPattern 未找到")
                return 0, 0
            
            text_addr = player_addr + 3
            displacement = self._read_int32(text_addr)
            audio_player_ptr = text_addr + displacement + 4
            
            logger.info(f"V3 AOB扫描完成: schedule={hex(schedule_ptr)}, player={hex(audio_player_ptr)}")
            return schedule_ptr, audio_player_ptr
            
        except Exception as e:
            logger.error(f"V3 AOB扫描异常: {e}")
            return 0, 0
    
    def _read_v3_song_id(self) -> str:
        if not self._pm or not self._v3_audio_player_ptr:
            return ''
        try:
            audio_play_info = self._read_uint64(self._v3_audio_player_ptr + 0x50)
            if audio_play_info == 0:
                return ''
            
            str_ptr = audio_play_info + 0x10
            str_length = self._read_uint64(str_ptr + 0x10)
            
            if str_length <= 0:
                return ''
            
            read_length = min(int(str_length), 128)
            
            if str_length <= 15:
                raw = self._read_bytes(str_ptr, read_length)
            else:
                str_address = self._read_uint64(str_ptr)
                if str_address == 0:
                    return ''
                raw = self._read_bytes(str_address, read_length)
            
            song_str = raw.decode('utf-8')
            if not song_str or '_' not in song_str:
                return ''
            return song_str[:song_str.index('_')]
        except Exception as e:
            logger.debug(f"读取V3歌曲ID失败: {e}")
            return ''
    
    def _read_v2_song_id(self) -> str:
        if not self._pm or not self._cloudmusic_dll_base:
            return ''
        try:
            version_offsets = V2_OFFSETS.get(self._version)
            if not version_offsets:
                return ''
            
            songid_array = self._read_uint32(self._cloudmusic_dll_base + version_offsets['song_array'])
            raw = self._read_bytes(songid_array, 0x14)
            song_str = raw.decode('utf-16').split('_')[0]
            return song_str
        except Exception as e:
            logger.debug(f"读取V2歌曲ID失败: {e}")
            return ''
    
    def read_progress(self) -> Optional[dict]:
        if not self.is_available:
            return None
        
        try:
            current_pid = 0
            if self._pm:
                try:
                    current_pid = self._pm.process_id
                except Exception:
                    current_pid = 0
            
            if not current_pid or not psutil.pid_exists(current_pid):
                if self._pm:
                    try:
                        self._pm.close_process()
                    except Exception:
                        pass
                    self._pm = None
                    self._cloudmusic_dll_base = 0
                
                self._pid, self._version = self._find_process()
                if not self._pid:
                    return None
                self._first_run = True
            
            self._is_v3 = self._version.startswith('3.')
            
            if not self._is_v3 and self._version not in V2_OFFSETS:
                logger.debug(f"不支持的网易云版本: {self._version}")
                return None
            
            if self._pm is None:
                self._pm = pymem.Pymem()
                self._pm.open_process_from_id(self._pid)
            
            if self._first_run:
                logger.info(f"找到网易云进程: PID={self._pid}, 版本={self._version}")
                self._cloudmusic_dll_base = self._get_cloudmusic_dll_base()
                
                if self._is_v3:
                    self._v3_schedule_ptr, self._v3_audio_player_ptr = self._scan_v3_offsets()
                    if not self._v3_schedule_ptr:
                        return None
                else:
                    if not self._cloudmusic_dll_base:
                        logger.warning("未找到 cloudmusic.dll")
                        return None
                
                self._first_run = False
            
            if self._is_v3:
                playback_time = self._read_float64(self._v3_schedule_ptr)
                song_id = self._read_v3_song_id()
            else:
                version_offsets = V2_OFFSETS[self._version]
                playback_time = self._read_float64(self._cloudmusic_dll_base + version_offsets['current'])
                song_id = self._read_v2_song_id()
            
            if not RE_SONG_ID.match(song_id):
                return None
            
            is_playing = True
            if song_id == self._last_song_id:
                if abs(playback_time - self._last_playback_time) < 0.01:
                    is_playing = False
            else:
                is_playing = True
            
            self._last_song_id = song_id
            self._last_playback_time = playback_time
            
            return {
                'song_id': song_id,
                'playback_time': playback_time,
                'is_playing': is_playing,
                'version': self._version,
            }
            
        except Exception as e:
            logger.error(f"读取播放进度失败: {e}")
            return None
    
    def close(self):
        if self._pm:
            try:
                self._pm.close_process()
            except Exception:
                pass
            self._pm = None
            self._cloudmusic_dll_base = 0


_ncm_reader_instance: Optional[NCMProgressReader] = None


def get_ncm_reader() -> NCMProgressReader:
    global _ncm_reader_instance
    if _ncm_reader_instance is None:
        _ncm_reader_instance = NCMProgressReader()
    return _ncm_reader_instance
