# ClassLively
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
媒体服务模块
"""

import ctypes
import ctypes.wintypes as wintypes
import logging
import os
import re
import struct
import sys
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass
from typing import Optional, List, Tuple, Dict, Any
from functools import lru_cache

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

logger = logging.getLogger("ClassLively.services.media")

def _check_and_install_deps():
    missing = []
    try:
        import pymem
    except ImportError:
        missing.append('pymem')
    try:
        import psutil
    except ImportError:
        missing.append('psutil')
    try:
        from win32api import GetFileVersionInfo
    except ImportError:
        missing.append('pywin32')
    try:
        from winsdk.windows.media.control import GlobalSystemMediaTransportControlsSessionManager
    except ImportError:
        missing.append('winsdk')

    if missing:
        logger.warning(f"媒体服务缺少依赖库: {', '.join(missing)}")
        return False
    return True


@dataclass
class MediaInfo:
    """媒体信息"""
    title: str = ""
    artist: str = ""
    album: str = ""
    title_artist: str = ""
    thumbnail_data: Optional[bytes] = None
    playback_status: str = "stopped"
    position_ms: int = 0
    duration_ms: int = 0
    is_playing: bool = False
    app_name: str = ""
    song_id: str = ""

    def is_valid(self) -> bool:
        return bool(self.title or self.artist or self.song_id)

    def get_progress_percent(self) -> float:
        return min(1.0, max(0.0, self.position_ms / self.duration_ms)) if self.duration_ms > 0 else 0.0

    @staticmethod
    def format_time(ms: int) -> str:
        s = max(0, ms // 1000)
        return f"{s // 60}:{s % 60:02d}"


@dataclass
class SongDetail:
    """歌曲详情"""
    song_id: int
    name: str
    artists: List[str]
    album_name: str
    cover_url: str
    duration: int


@dataclass
class LyricLine:
    """单行歌词"""
    time_ms: int
    text: str

    def __lt__(self, other):
        return self.time_ms < other.time_ms


@dataclass
class Lyrics:
    """歌词"""
    lines: List[LyricLine]
    raw_lrc: str
    song_id: Optional[int] = None

    def is_empty(self) -> bool:
        return len(self.lines) == 0

    def get_line_at_time(self, time_ms: int) -> Tuple[Optional[LyricLine], int]:
        if self.is_empty():
            return None, -1
        for i in range(len(self.lines) - 1, -1, -1):
            if self.lines[i].time_ms <= time_ms:
                return self.lines[i], i
        return self.lines[0], 0


class NeteaseCloudMusic:
    """网易云音乐"""

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

    V3_SCHEDULE_PATTERN = b"\x66\x0F\x2E\x0D\x00\x00\x00\x00\x7A\x00\x75\x00\x66\x0F\x2E\x15"
    V3_PLAYER_PATTERN = b"\x48\x8D\x0D\x00\x00\x00\x00\xE8\x00\x00\x00\x00\x48\x8D\x0D\x00\x00\x00\x00\xE8\x00\x00\x00\x00\x90\x48\x8D\x0D\x00\x00\x00\x00\xE8\x00\x00\x00\x00\x48\x8D\x05\x00\x00\x00\x00\x48\x8D\xA5\x00\x00\x00\x00\x5F\x5D\xC3\xCC\xCC\xCC\xCC\xCC\x48\x89\x4C\x24\x00\x55\x57\x48\x81\xEC\x00\x00\x00\x00\x48\x8D\x6C\x24\x00\x48\x8D\x7C\x24"

    API_BASE = "https://music163.xuanmou.com.cn"
    API_HEADERS = {'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'}

    def __init__(self):
        self._pm = None
        self._pid = 0
        self._version = ''
        self._is_v3 = False
        self._schedule_ptr = 0
        self._player_ptr = 0
        self._dll_base = 0
        self._dll_size = 0
        self._dll_data = None
        self._first = True
        self._last_id = ''
        self._last_time = 0.0
        self._api_cache = {}
        self._api_last_time = 0.0
        self._available = self._check_deps()

        self._session = self._create_session()
        self._executor = ThreadPoolExecutor(max_workers=3, thread_name_prefix='media_api')
        self._cache_max_size = 50
        self._cache_order = []

    def _check_deps(self) -> bool:
        try:
            import pymem
            import psutil
            from win32api import GetFileVersionInfo, HIWORD, LOWORD
            return True
        except ImportError:
            return False

    @property
    def available(self) -> bool:
        return self._available

    @property
    def name(self) -> str:
        return "NeteaseCloudMusic"

    def _create_session(self) -> requests.Session:
        session = requests.Session()
        adapter = HTTPAdapter(
            pool_connections=10,
            pool_maxsize=10,
            max_retries=Retry(total=3, backoff_factor=0.1)
        )
        session.mount('http://', adapter)
        session.mount('https://', adapter)
        session.headers.update(self.API_HEADERS)
        return session

    def _set_cache(self, key: str, value):
        if len(self._api_cache) >= self._cache_max_size and key not in self._api_cache:
            oldest = self._cache_order.pop(0)
            self._api_cache.pop(oldest, None)
        self._api_cache[key] = value
        if key in self._cache_order:
            self._cache_order.remove(key)
        self._cache_order.append(key)

    def get_info(self) -> Optional[MediaInfo]:
        data = self._read_memory()
        if not data:
            logger.debug("网易云音乐: 内存读取失败或无数据")
            return None
        title, artist = self._parse_window_title()
        if not title and not artist:
            logger.debug(f"网易云音乐: 窗口标题解析失败")
        else:
            logger.debug(f"网易云音乐: 窗口标题解析成功 - {title} - {artist}")
        return MediaInfo(
            title=title, artist=artist,
            title_artist=f"{title} - {artist}" if artist else title,
            position_ms=int(data['playback_time'] * 1000),
            is_playing=data['is_playing'],
            playback_status="playing" if data['is_playing'] else "paused",
            app_name=self.name, song_id=data['song_id'],
        )

    def get_detail(self, song_id: int) -> Optional[SongDetail]:
        key = f"detail_{song_id}"
        if key in self._api_cache:
            return self._api_cache[key]
        data = self._api_get("/song/detail", {'ids': str(song_id)})
        if data:
            songs = data.get('songs', [])
            if songs:
                s = songs[0]
                al = s.get('al', {})
                detail = SongDetail(
                    song_id=s.get('id'), name=s.get('name', ''),
                    artists=[a.get('name', '') for a in s.get('ar', [])],
                    album_name=al.get('name', ''), cover_url=al.get('picUrl', ''),
                    duration=s.get('dt', 0)
                )
                self._set_cache(key, detail)
                return detail
        return None

    def get_lyrics(self, song_id: int) -> Optional[Lyrics]:
        key = f"lyric_{song_id}"
        if key in self._api_cache:
            return self._api_cache[key]
        data = self._api_get("/lyric", {'id': str(song_id)})
        if data:
            lrc = data.get('lrc', {}).get('lyric', '') or data.get('tlyric', {}).get('lyric', '')
            if lrc:
                lines = self._parse_lrc(lrc)
                if lines:
                    lyrics = Lyrics(lines=lines, raw_lrc=lrc, song_id=song_id)
                    self._set_cache(key, lyrics)
                    return lyrics
        return None

    def get_cover(self, url: str) -> Optional[bytes]:
        key = f"cover_{hash(url)}"
        if key in self._api_cache:
            return self._api_cache[key]
        try:
            resp = self._session.get(url, timeout=8)
            if resp.status_code == 200 and 'image' in resp.headers.get('Content-Type', ''):
                data = resp.content
                if 1024 < len(data) < 10 * 1024 * 1024:
                    self._set_cache(key, data)
                    return data
        except Exception as e:
            logger.debug(f"获取封面失败: {e}")
        return None

    def fetch_all(self, song_name: str, artist: str = "") -> Dict[str, Any]:
        result = {'song_id': None, 'detail': None, 'lyrics': None, 'cover': None}
        start_time = time.time()

        song_id = self._search_song(f"{song_name} {artist}".strip())
        if not song_id:
            return result
        result['song_id'] = song_id

        detail = self.get_detail(song_id)
        if detail:
            result['detail'] = detail

        futures = {}
        if detail and detail.cover_url:
            futures['cover'] = self._executor.submit(self.get_cover, detail.cover_url)
        futures['lyrics'] = self._executor.submit(self.get_lyrics, song_id)

        for future in as_completed(futures.values()):
            try:
                future.result(timeout=10)
            except Exception as e:
                logger.debug(f"并行请求失败: {e}")

        if 'cover' in futures and detail and detail.cover_url:
            result['cover'] = self.get_cover(detail.cover_url)
        if 'lyrics' in futures:
            result['lyrics'] = self.get_lyrics(song_id)

        elapsed = time.time() - start_time
        logger.debug(f"fetch_all耗时: {elapsed:.2f}秒 (歌曲ID: {song_id})")

        return result

    def close(self):
        if self._pm:
            try:
                self._pm.close_process()
            except Exception:
                pass
            self._pm = None
        if self._session:
            try:
                self._session.close()
            except Exception:
                pass
            self._session = None
        if self._executor:
            try:
                self._executor.shutdown(wait=False)
            except Exception:
                pass
            self._executor = None
        self._api_cache.clear()
        self._cache_order.clear()

    def _read_memory(self) -> Optional[Dict[str, Any]]:
        if not self._available:
            return None
        try:
            import pymem
            import psutil
            from win32api import GetFileVersionInfo, HIWORD, LOWORD

            pid = 0
            if self._pm:
                try:
                    pid = self._pm.process_id
                except Exception:
                    pass

            if not pid or not psutil.pid_exists(pid):
                if self._pm:
                    try:
                        self._pm.close_process()
                    except Exception:
                        pass
                    self._pm = None
                self._first = True
                self._pid, self._version = self._find_process()
                if not self._pid:
                    return None

            self._is_v3 = self._version.startswith('3.')
            if not self._is_v3 and self._version not in self.V2_OFFSETS:
                logger.warning(f"网易云音乐: 不支持的版本 {self._version}")
                return None

            if self._pm is None:
                self._pm = pymem.Pymem()
                self._pm.open_process_from_id(self._pid)

            if self._first:
                self._dll_base, self._dll_size = self._get_dll()
                if self._is_v3:
                    self._dll_data = self._read_bytes(self._dll_base, self._dll_size)
                    self._schedule_ptr, self._player_ptr = self._scan_v3()
                    if not self._schedule_ptr:
                        logger.warning(f"网易云音乐: V3 AOB扫描失败 (版本 {self._version})")
                        return None
                elif not self._dll_base:
                    return None
                self._first = False

            if self._is_v3:
                playback_time = self._read_f64(self._schedule_ptr)
                song_id = self._read_v3_id()
            else:
                offsets = self.V2_OFFSETS[self._version]
                playback_time = self._read_f64(self._dll_base + offsets['current'])
                arr = self._read_u64(self._dll_base + offsets['song_array'])
                song_id = self._read_bytes(arr, 0x14).decode('utf-16').split('_')[0]

            if not re.match(r'\d+', song_id):
                return None

            is_playing = song_id != self._last_id or abs(playback_time - self._last_time) >= 0.01
            self._last_id = song_id
            self._last_time = playback_time

            return {'song_id': song_id, 'playback_time': playback_time, 'is_playing': is_playing}

        except Exception as e:
            logger.error(f"内存读取失败: {e}")
            return None

    def _find_process(self) -> Tuple[int, str]:
        import psutil
        from win32api import GetFileVersionInfo, HIWORD, LOWORD

        for proc in psutil.process_iter(attrs=['name', 'pid']):
            if proc.info['name'] and proc.info['name'].lower() == 'cloudmusic.exe':
                try:
                    if any('--type=' in a for a in proc.cmdline()):
                        continue
                    exe = proc.exe()
                    ver = GetFileVersionInfo(exe, '\\')
                    v = f"{HIWORD(ver['FileVersionMS'])}.{LOWORD(ver['FileVersionMS'])}.{HIWORD(ver['FileVersionLS'])}.{LOWORD(ver['FileVersionLS'])}"
                    return proc.info['pid'], v
                except Exception:
                    return proc.info['pid'], ''
        return 0, ''

    def _get_dll(self) -> Tuple[int, int]:
        for m in self._pm.list_modules():
            if m.name.lower() == 'cloudmusic.dll':
                return m.lpBaseOfDll, m.SizeOfImage
        return 0, 0

    def _read_bytes(self, addr: int, size: int) -> bytes:
        try:
            return self._pm.read_bytes(addr, size)
        except Exception:
            return b''

    def _read_f64(self, addr: int) -> float:
        try:
            return struct.unpack('d', self._pm.read_bytes(addr, 8))[0]
        except Exception:
            return 0.0

    def _read_u64(self, addr: int) -> int:
        try:
            return struct.unpack('<Q', self._pm.read_bytes(addr, 8))[0]
        except Exception:
            return 0

    def _read_i32(self, addr: int) -> int:
        try:
            return struct.unpack('<i', self._pm.read_bytes(addr, 4))[0]
        except Exception:
            return 0

    def _scan_v3(self) -> Tuple[int, int]:
        addr = self._aob(self.V3_SCHEDULE_PATTERN)
        if not addr:
            return 0, 0
        schedule = addr + 4 + self._read_i32(addr + 4) + 4

        addr = self._aob(self.V3_PLAYER_PATTERN)
        if not addr:
            return 0, 0
        player = addr + 3 + self._read_i32(addr + 3) + 4
        return schedule, player

    def _aob(self, pattern: bytes) -> int:
        if not self._dll_data:
            return 0
        plen = len(pattern)
        for i in range(len(self._dll_data) - plen):
            match = True
            for j in range(plen):
                if pattern[j] != 0x00 and self._dll_data[i + j] != pattern[j]:
                    match = False
                    break
            if match:
                return self._dll_base + i
        return 0

    def _read_v3_id(self) -> str:
        if not self._player_ptr:
            return ''
        try:
            info = self._read_u64(self._player_ptr + 0x50)
            if info == 0:
                return ''
            ptr = info + 0x10
            length = self._read_u64(ptr + 0x10)
            if length <= 0:
                return ''
            if length <= 15:
                raw = self._read_bytes(ptr, int(length))
            else:
                raw = self._read_bytes(self._read_u64(ptr), min(int(length), 128))
            s = raw.decode('utf-8')
            return s[:s.index('_')] if '_' in s else ''
        except Exception:
            return ''

    def _parse_window_title(self) -> Tuple[str, str]:
        try:
            user32 = ctypes.windll.user32
            hwnd = user32.FindWindowW("OrpheusBrowserHost", None)
            if not hwnd:
                return "", ""
            length = user32.GetWindowTextLengthW(hwnd)
            if length <= 0:
                return "", ""
            buf = ctypes.create_unicode_buffer(length + 1)
            user32.GetWindowTextW(hwnd, buf, length + 1)
            title = buf.value.strip()
            if len(title) <= 2 or ' - ' not in title:
                return "", ""
            parts = title.rsplit(' - ', 1)
            if len(parts) == 2 and parts[1].strip() and parts[0].strip() != "网易云音乐":
                return parts[0].strip(), parts[1].strip()
            return "", ""
        except Exception:
            return "", ""

    def _api_wait(self):
        elapsed = time.time() - self._api_last_time
        if elapsed < 0.1:
            time.sleep(0.1 - elapsed)
        self._api_last_time = time.time()

    def _api_get(self, endpoint: str, params: dict = None) -> Optional[dict]:
        try:
            self._api_wait()
            resp = self._session.get(f"{self.API_BASE}{endpoint}", params=params, timeout=10)
            if resp.status_code == 200:
                data = resp.json()
                if data.get('code') == 200 or data.get('code') is None:
                    return data
        except Exception as e:
            logger.debug(f"API请求失败: {endpoint} - {e}")
        return None

    def _search_song(self, keyword: str) -> Optional[int]:
        data = self._api_get("/cloudsearch", {'keywords': keyword, 'limit': '5', 'type': '1'})
        songs = data.get('result', {}).get('songs', []) if data else []
        return songs[0].get('id') if songs else None

    def _parse_lrc(self, lrc: str) -> List[LyricLine]:
        lines = []
        pat = re.compile(r'\[(\d{1,2}):(\d{1,2})(?:\.(\d{1,3}))?\]')
        for line in lrc.split('\n'):
            text = pat.sub('', line).strip()
            if not text:
                continue
            for m in pat.findall(line):
                try:
                    mins, secs, ms = int(m[0]), int(m[1]), int(m[2]) if m[2] else 0
                    if len(m[2]) == 2: ms *= 10
                    elif len(m[2]) == 1: ms *= 100
                    lines.append(LyricLine(time_ms=mins * 60000 + secs * 1000 + ms, text=text))
                except ValueError:
                    continue
        lines.sort()
        return lines


class GSMTCReader:
    """Windows GSMTC"""

    STATUS_MAP = {}

    def __init__(self):
        self._manager = None
        self._initialized = False
        self._available = self._check_deps()
        self._loop = None
        self._had_session = False
        self._last_media_key: str = ""
        if self._available:
            try:
                from winsdk.windows.media.control import GlobalSystemMediaTransportControlsSessionPlaybackStatus as PlaybackStatus
                self.STATUS_MAP = {
                    PlaybackStatus.CLOSED: "closed", PlaybackStatus.OPENED: "opened",
                    PlaybackStatus.CHANGING: "changing", PlaybackStatus.STOPPED: "stopped",
                    PlaybackStatus.PLAYING: "playing", PlaybackStatus.PAUSED: "paused",
                }
                logger.info("GSMTC: 初始化成功")
            except Exception as e:
                logger.warning(f"GSMTC: 初始化失败 {e}")

    def _check_deps(self) -> bool:
        try:
            from winsdk.windows.media.control import GlobalSystemMediaTransportControlsSessionManager
            return True
        except ImportError:
            return False

    @property
    def available(self) -> bool:
        return self._available

    @property
    def name(self) -> str:
        return "GSMTC"

    def get_info(self) -> Optional[MediaInfo]:
        if not self._available:
            logger.warning("GSMTC: 依赖库 winsdk 未安装")
            return None
        try:
            from winsdk.windows.media.control import GlobalSystemMediaTransportControlsSessionManager as MediaManager
            import asyncio

            async def _read():
                try:
                    if not self._initialized or self._manager is None:
                        logger.debug("GSMTC: 请求媒体会话管理")
                        self._manager = await MediaManager.request_async()
                        self._initialized = True

                    session = self._manager.get_current_session()
                    if not session:
                        if self._had_session:
                            logger.info("GSMTC: 无活跃媒体会话")
                            self._had_session = False
                        return None
                    if not self._had_session:
                        logger.info("GSMTC: 检测到活跃媒体会话")
                        self._had_session = True

                    info = MediaInfo()

                    try:
                        app = session.source_app_user_model_id
                        info.app_name = app.split('.')[-1] if app and '.' in app else app or ""
                        logger.debug(f"GSMTC: 应用名称={info.app_name}")
                    except Exception as e:
                        logger.warning(f"GSMTC: 获取应用名失败 {e}")

                    try:
                        pb = session.get_playback_info()
                        if pb:
                            info.playback_status = self.STATUS_MAP.get(pb.playback_status, "unknown")
                            info.is_playing = pb.playback_status.value == 4
                            logger.debug(f"GSMTC: 播放状态={info.playback_status}, 正在播放={info.is_playing}")
                    except Exception as e:
                        logger.warning(f"GSMTC: 获取播放状态失败 {e}")

                    try:
                        tl = session.get_timeline_properties()
                        if tl:
                            info.position_ms = max(0, int(tl.position.total_seconds() * 1000))
                            info.duration_ms = max(0, int(tl.end_time.total_seconds() * 1000))
                    except Exception as e:
                        logger.warning(f"GSMTC: 获取时间线失败 {e}")

                    try:
                        props = await session.try_get_media_properties_async()
                        if props:
                            info.title = props.title or ""
                            info.artist = props.artist or ""
                            info.album = props.album_title or ""
                            info.title_artist = f"{info.title} - {info.artist}" if info.artist else info.title
                            media_key = f"{info.title}|{info.artist}"
                            if media_key != self._last_media_key:
                                self._last_media_key = media_key
                                logger.info(f"GSMTC: 获取到媒体信息 - 标题={info.title}, 歌手={info.artist}")
                            
                            if hasattr(props, 'thumbnail') and props.thumbnail:
                                try:
                                    from winsdk.windows.storage.streams import Buffer, InputStreamOptions
                                    
                                    thumb = props.thumbnail
                                    stream = await thumb.open_read_async()
                                    if stream and 0 < stream.size < 10 * 1024 * 1024:
                                        buf = Buffer(stream.size)
                                        await stream.read_async(buf, buf.capacity, InputStreamOptions.READ_AHEAD)
                                        info.thumbnail_data = bytes(buf)
                                    elif stream:
                                        logger.warning(f"GSMTC: 缩略图大小异常: {stream.size} bytes")
                                    else:
                                        logger.warning("GSMTC: 无法打开缩略图流")
                                except Exception as e:
                                    logger.warning(f"GSMTC: 读取缩略图失败: {type(e).__name__}: {e}")
                            else:
                                logger.debug("GSMTC: 媒体源未提供缩略图")
                    except Exception as e:
                        logger.warning(f"GSMTC: 获取媒体属性失败 {e}")

                    return info

                except Exception as e:
                    logger.error(f"GSMTC: 读取过程出错 {e}")
                    return None

            try:
                if self._loop is None or self._loop.is_closed():
                    self._loop = asyncio.new_event_loop()
                    asyncio.set_event_loop(self._loop)

                result = self._loop.run_until_complete(_read())
                return result
            except RuntimeError as e:
                logger.warning(f"GSMTC: 事件循环错误: {e}")
                self._loop = asyncio.new_event_loop()
                asyncio.set_event_loop(self._loop)
                result = self._loop.run_until_complete(_read())
                return result
            except Exception as e:
                logger.error(f"GSMTC: 执行失败 {e}")
                return None

        except Exception as e:
            logger.error(f"GSMTC读取失败: {e}")
            return None

    def get_detail(self, song_id: int) -> Optional[SongDetail]:
        return None

    def get_lyrics(self, song_id: int) -> Optional[Lyrics]:
        return None

    def get_cover(self, url: str) -> Optional[bytes]:
        return None

    def fetch_all(self, song_name: str, artist: str = "") -> Dict[str, Any]:
        return {'song_id': None, 'detail': None, 'lyrics': None, 'cover': None}

    def close(self):
        try:
            if self._pm:
                self._pm.close_process()
                self._pm = None
        except Exception:
            pass
        self._mem_ready = False


class KugouMemoryReader:
    """酷狗音乐 - 窗口标题 时间模拟"""

    def __init__(self):
        self._available = True
        self._song_start_time = 0.0
        self._last_title_artist = ""
        self._paused_time = 0.0
        self._pause_start = 0.0
        self._was_playing = True
        self._duration_cache = {}
        import ctypes
        self._user32 = ctypes.windll.user32

    @property
    def available(self) -> bool:
        return self._available

    @property
    def name(self) -> str:
        return "KugouMemory"

    def get_info(self) -> Optional[MediaInfo]:
        try:
            title, artist = self._parse_window_title()
            if not title:
                return None

            ta = f"{title} - {artist}" if artist else title
            now = time.time()

            if ta != self._last_title_artist:
                self._last_title_artist = ta
                self._song_start_time = now
                self._paused_time = 0.0
                self._pause_start = 0.0
                self._was_playing = True

            is_playing = True
            if is_playing and not self._was_playing:
                if self._pause_start > 0:
                    self._paused_time += now - self._pause_start
                    self._pause_start = 0.0
            elif not is_playing and self._was_playing:
                self._pause_start = now
            self._was_playing = is_playing

            dur_ms = self._get_duration(title, artist)

            if is_playing:
                elapsed = now - self._song_start_time - self._paused_time
                position_ms = int(max(0, elapsed) * 1000)
            else:
                position_ms = int(max(0, now - self._pause_start - self._paused_time) * 1000)

            if dur_ms > 0 and position_ms > dur_ms:
                position_ms = dur_ms

            return MediaInfo(
                title=title, artist=artist,
                title_artist=ta,
                position_ms=position_ms,
                duration_ms=dur_ms,
                is_playing=is_playing,
                playback_status="playing" if is_playing else "paused",
                app_name="Kugou",
            )
        except Exception as e:
            logger.debug(f"酷狗读取失败: {e}")
            return None

    def _parse_window_title(self) -> Tuple[str, str]:
        try:
            results = []
            WNDENUMPROC = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.wintypes.HWND, ctypes.wintypes.LPARAM)
            def cb(h, _):
                cls_buf = ctypes.create_unicode_buffer(256)
                self._user32.GetClassNameW(h, cls_buf, 256)
                ln = self._user32.GetWindowTextLengthW(h)
                if 0 < ln < 300:
                    b = ctypes.create_unicode_buffer(ln + 1)
                    self._user32.GetWindowTextW(h, b, ln + 1)
                    t = b.value.strip()
                    if t and ' - 酷狗音乐' in t and '桌面歌词' not in t:
                        results.append(t)
                return True
            self._user32.EnumWindows(WNDENUMPROC(cb), 0)
            if results:
                return self._fix_kugou_title(results[0])
            return "", ""
        except Exception:
            return ""

    @staticmethod
    def _fix_kugou_title(raw: str) -> Tuple[str, str]:
        if ' - 酷狗音乐' in raw:
            raw = raw.replace(' - 酷狗音乐', '')
        if not raw or '-' not in raw:
            return raw or "", ""
        parts = raw.split('-', 1)
        right = parts[1].strip() if len(parts) > 1 else ""
        left = parts[0].strip()
        if '酷狗' in left:
            idx = left.find('酷狗')
            left = left[idx + 2:].strip() + left[:idx]
        if ' - ' in left:
            sub_parts = left.rsplit(' - ', 1)
            if len(sub_parts) == 2:
                return sub_parts[1].strip(), sub_parts[0].strip()
        return left, right

    def _get_duration(self, title: str, artist: str) -> int:
        cache_key = f"{title} - {artist}"
        if cache_key in self._duration_cache:
            return self._duration_cache[cache_key]
        try:
            keyword = f"{title} {artist}"
            url = f"http://songsearch.kugou.com/song_search_v2?keyword={keyword}&platform=WebFilter&format=json&page=1&pagesize=1"
            resp = requests.get(url, timeout=5,
                                headers={'User-Agent': 'Mozilla/5.0'})
            if resp.status_code == 200:
                data = resp.json()
                if data.get('error_code') == 0:
                    lists = data.get('data', {}).get('lists', [])
                    if lists:
                        dur = lists[0].get('Duration', 0)
                        dur_ms = int(dur) * 1000
                        self._duration_cache[cache_key] = dur_ms
                        return dur_ms
        except Exception:
            pass
        return 0

    def get_lyrics(self, title: str, artist: str, duration_ms: int = 0):
        import base64
        cache_key = f"kg_lyric_{title} - {artist}"
        if cache_key in self._duration_cache and isinstance(self._duration_cache.get(cache_key), Lyrics):
            return self._duration_cache[cache_key]
        try:
            keyword = f"{artist} - {title}"
            search_url = (f"http://lyrics.kugou.com/search?"
                          f"ver=1&man=yes&client=pc&keyword={keyword}"
                          f"&duration={duration_ms}&hash=")
            resp = requests.get(search_url, timeout=5,
                                headers={'User-Agent': 'Mozilla/5.0'})
            if resp.status_code != 200:
                return None
            data = resp.json()
            candidates = data.get('candidates', [])
            if not candidates:
                return None
            c = candidates[0]
            lyric_id = c.get('id')
            accesskey = c.get('accesskey')
            if not lyric_id or not accesskey:
                return None
            dl_url = (f"http://lyrics.kugou.com/download?"
                     f"ver=1&client=pc&id={lyric_id}&accesskey={accesskey}&fmt=lrc&charset=utf8")
            dl_resp = requests.get(dl_url, timeout=5,
                                   headers={'User-Agent': 'Mozilla/5.0'})
            if dl_resp.status_code != 200:
                return None
            dl_data = dl_resp.json()
            content_b64 = dl_data.get('content', '')
            if not content_b64:
                return None
            lrc_text = base64.b64decode(content_b64).decode('utf-8', errors='ignore').strip()
            if not lrc_text or len(lrc_text) < 10:
                return None
            lines = self._parse_lrc(lrc_text)
            if not lines:
                return None
            lyrics = Lyrics(lines=lines, raw_lrc=lrc_text, song_id=0)
            self._duration_cache[cache_key] = lyrics
            return lyrics
        except Exception as e:
            logger.debug(f"酷狗歌词获取失败: {e}")
            return None

    def get_cover(self, title: str, artist: str):
        cache_key = f"kg_cover_{title} - {artist}"
        if cache_key in self._duration_cache and isinstance(self._duration_cache.get(cache_key), bytes):
            return self._duration_cache[cache_key]
        try:
            keyword = f"{title} {artist}"
            url = f"http://songsearch.kugou.com/song_search_v2?keyword={keyword}&platform=WebFilter&format=json&page=1&pagesize=1"
            resp = requests.get(url, timeout=5,
                                headers={'User-Agent': 'Mozilla/5.0'})
            if resp.status_code == 200:
                data = resp.json()
                if data.get('error_code') == 0:
                    lists = data.get('data', {}).get('lists', [])
                    if lists:
                        cover_url = lists[0].get('Image', '').replace('/{size}', '')
                        if cover_url:
                            cr = requests.get(cover_url, timeout=8,
                                             headers={'User-Agent': 'Mozilla/5.0',
                                                      'Referer': 'http://www.kugou.com/'})
                            if cr.status_code == 200 and 1024 < len(cr.content) < 10 * 1024 * 1024:
                                self._duration_cache[cache_key] = cr.content
                                return cr.content
        except Exception as e:
            logger.debug(f"酷狗封面获取失败: {e}")
        return None

    @staticmethod
    def _parse_lrc(lrc: str):
        import re
        lines = []
        pat = re.compile(r'\[(\d{1,2}):(\d{1,2})(?:\.(\d{1,3}))?\]')
        for line in lrc.split('\n'):
            text = pat.sub('', line).strip()
            if not text:
                continue
            for m in pat.findall(line):
                try:
                    mins, secs, ms = int(m[0]), int(m[1]), int(m[2]) if m[2] else 0
                    if len(m[2]) == 2: ms *= 10
                    elif len(m[2]) == 1: ms *= 100
                    lines.append(LyricLine(time_ms=mins * 60000 + secs * 1000 + ms, text=text))
                except ValueError:
                    continue
        lines.sort()
        return lines

    def get_detail(self, song_id): return None
    def get_cover_legacy(self, url): return None

    def fetch_all(self, song_name, artist=""):
        result = {'song_id': None, 'detail': None, 'lyrics': None, 'cover': None}
        dur = self._get_duration(song_name, artist)
        if dur > 0:
            result['detail'] = type('obj', (object,), {'duration': dur})()
        lyrics = self.get_lyrics(song_name, artist, dur)
        if lyrics:
            result['lyrics'] = lyrics
        cover = self.get_cover(song_name, artist)
        if cover:
            result['cover'] = cover
        return result

    def close(self):
        pass


class QQMusicReader:
    """QQ音乐"""

    def __init__(self):
        self._available = self._check_deps()
        self._manager = None
        self._initialized = False
        self._last_title_artist = ""
        self._duration_cache = {}
        self._qq_hwnd = None
        self._uia_ready = False
        self._uia_attempted = False
        import ctypes
        self._user32 = ctypes.windll.user32

    def _find_qq_hwnd(self):
        WNDENUMPROC = ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
        best = [None, 0]
        def cb(h, _):
            cls_buf = ctypes.create_unicode_buffer(256)
            title_buf = ctypes.create_unicode_buffer(512)
            self._user32.GetClassNameW(h, cls_buf, 256)
            self._user32.GetWindowTextW(h, title_buf, 512)
            t = title_buf.value.strip()
            cn = cls_buf.value.lower()
            if 'qq' in t.lower() or ('txgui' in cn and 'qq' in t.lower()):
                rc = wintypes.RECT()
                self._user32.GetWindowRect(h, ctypes.byref(rc))
                area = (rc.right - rc.left) * (rc.bottom - rc.top)
                if area > best[1]:
                    best = [h, area]
            return True
        self._user32.EnumWindows(WNDENUMPROC(cb), 0)
        hwnd = best[0]
        if hwnd:
            rc = wintypes.RECT()
            self._user32.GetWindowRect(hwnd, ctypes.byref(rc))
            if (rc.right - rc.left) > 500 and (rc.bottom - rc.top) > 300:
                return hwnd
        return None

    def _read_uia_progress(self):
        import re
        time_re = re.compile(r'^(\d{1,2}):(\d{2})$')
        
        if not self._uia_attempted:
            self._uia_attempted = True
            try:
                import uiautomation as auto
                self._uia_lib = auto
            except ImportError:
                return -1, 0
        
        if not self._uia_ready or not self._qq_hwnd:
            hwnd = self._find_qq_hwnd()
            if not hwnd:
                return -1, 0
            try:
                win = self._uia_lib.ControlFromHandle(hwnd)
                self._qq_win = win
                self._qq_hwnd = hwnd
                self._uia_ready = True
            except Exception:
                return -1, 0
        
        try:
            results = []
            def scan(ctrl, depth=0):
                try:
                    n = ctrl.Name or ""
                    if time_re.match(n.strip()) and getattr(ctrl, 'ControlTypeName', '') == "TextControl":
                        r = ctrl.BoundingRectangle
                        results.append((n.strip(), r.left))
                    for child in ctrl.GetChildren():
                        scan(child, depth + 1)
                except Exception:
                    pass
            
            scan(self._qq_win, 0)
            
            if len(results) >= 2:
                results.sort(key=lambda x: x[1])
                pos_str = results[0][0]
                dur_str = results[1][0]
                
                m1 = time_re.match(pos_str)
                m2 = time_re.match(dur_str)
                if m1 and m2:
                    pos_s = int(m1.group(1)) * 60 + int(m1.group(2))
                    dur_s = int(m2.group(1)) * 60 + int(m2.group(2))
                    return pos_s * 1000, dur_s * 1000
            elif len(results) == 1:
                m = time_re.match(results[0][0])
                if m:
                    pos_s = int(m.group(1)) * 60 + int(m.group(2))
                    return pos_s * 1000, 0
            
            return -1, 0
        except Exception:
            self._uia_ready = False
            return -1, 0

    def _check_deps(self) -> bool:
        try:
            from winsdk.windows.media.control import GlobalSystemMediaTransportControlsSessionManager
            return True
        except ImportError:
            return False

    @property
    def available(self) -> bool:
        return self._available

    @property
    def name(self) -> str:
        return "QQMusic"

    def get_info(self) -> Optional[MediaInfo]:
        if not self._available:
            return None
        try:
            from winsdk.windows.media.control import (
                GlobalSystemMediaTransportControlsSessionManager as MediaManager,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus as PlaybackStatus
            )
            import asyncio

            async def _read():
                if not self._initialized or self._manager is None:
                    self._manager = await MediaManager.request_async()
                    self._initialized = True

                for i in range(self._manager.get_sessions().size):
                    sess = self._manager.get_sessions().get_at(i)
                    app_id = sess.source_app_user_model_id or ""
                    if 'qqmusic' not in app_id.lower():
                        continue

                    props = await sess.try_get_media_properties_async()
                    if not props:
                        continue
                    title = props.title or ""
                    artist = props.artist or ""
                    album = props.album_title or ""

                    pb = sess.get_playback_info()
                    status_val = pb.playback_status.value if pb and pb.playback_status else 3
                    is_playing = (status_val in (1, 4))

                    ta = f"{title} - {artist}" if artist else title

                    uia_pos_ms, uia_dur_ms = self._read_uia_progress()

                    tl = sess.get_timeline_properties()
                    dur_ms = 0
                    if uia_dur_ms > 0:
                        dur_ms = uia_dur_ms
                    elif tl and tl.end_time and tl.end_time.total_seconds() > 0:
                        dur_ms = int(tl.end_time.total_seconds() * 1000)
                    if dur_ms <= 0:
                        dur_ms = self._get_duration(title, artist)

                    if uia_pos_ms >= 0:
                        position_ms = uia_pos_ms
                    else:
                        position_ms = 0

                    if dur_ms > 0 and position_ms > dur_ms:
                        position_ms = dur_ms

                    info = MediaInfo(
                        title=title, artist=artist,
                        album=album,
                        title_artist=ta,
                        position_ms=position_ms,
                        duration_ms=dur_ms,
                        is_playing=is_playing,
                        playback_status="playing" if is_playing else "paused",
                        app_name="QQMusic",
                    )

                    if props.thumbnail:
                        try:
                            from winsdk.windows.storage.streams import Buffer, InputStreamOptions
                            stream = await props.thumbnail.open_read_async()
                            if stream and 0 < stream.size < 10 * 1024 * 1024:
                                buf = Buffer(stream.size)
                                await stream.read_async(buf, buf.capacity, InputStreamOptions.READ_AHEAD)
                                info.thumbnail_data = bytes(buf)
                        except Exception as e:
                            logger.debug(f"QQMusic: 获取封面失败: {e}")

                    return info

                return None

            try:
                if self._loop is None or self._loop.is_closed():
                    self._loop = asyncio.new_event_loop()
                    asyncio.set_event_loop(self._loop)

                result = self._loop.run_until_complete(_read())
                return result
            except RuntimeError:
                self._loop = asyncio.new_event_loop()
                asyncio.set_event_loop(self._loop)
                return self._loop.run_until_complete(_read())

        except Exception as e:
            logger.debug(f"QQ音乐读取失败: {e}")
            return None

    def get_lyrics(self, title: str, artist: str, duration_ms: int = 0):
        cache_key = f"qq_lyric_{title} - {artist}"
        if cache_key in {}:
            pass
        try:
            keyword = f"{artist} - {title}"
            url = (f"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_y.fcg?"
                   f"songmid=&g_tk=5381&format=json&incharset=utf8&outcharset=utf-8"
                   f"&nobase64=0&keyword={keyword}")
            resp = requests.get(url, timeout=5,
                                headers={'User-Agent': 'Mozilla/5.0',
                                         'Referer': 'https://y.qq.com/'})
            if resp.status_code != 200:
                return None
            data = resp.json()
            lyric_str = data.get('lyric', '')
            if not lyric_str:
                return None
            lines = []
            import re
            pat = re.compile(r'\[(\d{1,2}):(\d{1,2})(?:\.(\d{1,3}))?\]')
            for line in lyric_str.split('\n'):
                text = pat.sub('', line).strip()
                if not text:
                    continue
                for m in pat.findall(line):
                    mins, secs, ms = int(m[0]), int(m[1]), int(m[2]) if m[2] else 0
                    if len(ms) == 2: ms *= 10
                    elif len(ms) == 1: ms *= 100
                    lines.append(LyricLine(time_ms=mins * 60000 + secs * 1000 + ms, text=text))
            lines.sort()
            if lines:
                return Lyrics(lines=lines, raw_lrc=lyric_str, song_id=0)
        except Exception:
            pass
        return None

    def _get_duration(self, title: str, artist: str) -> int:
        cache_key = f"{title} - {artist}"
        if cache_key in self._duration_cache:
            return self._duration_cache[cache_key]
        try:
            keyword = f"{artist} {title}"
            url = (f"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?"
                   f"cr=1&new_json=1&format=json&aggr=1&lossless=0"
                   f"&n=1&w={keyword}")
            resp = requests.get(url, timeout=5,
                                headers={'User-Agent': 'Mozilla/5.0',
                                         'Referer': 'https://y.qq.com/'})
            if resp.status_code != 200:
                return 0
            data = resp.json()
            song_list = data.get('data', {}).get('song', {}).get('list', [])
            if song_list:
                dur = int(song_list[0].get('interval', 0))
                dur_ms = dur * 1000
                self._duration_cache[cache_key] = dur_ms
                return dur_ms
        except Exception:
            pass
        return 0

    def get_cover(self, title: str, artist: str):
        return None

    def get_detail(self, song_id): return None
    def get_cover_legacy(self, url): return None
    def fetch_all(self, song_name, artist=""):
        result = {'song_id': None, 'detail': None, 'lyrics': None, 'cover': None}
        lyrics = self.get_lyrics(song_name, artist)
        if lyrics:
            result['lyrics'] = lyrics
        return result

    def close(self):
        pass


class MediaProvider:
    """调度器"""

    def __init__(self):
        _check_and_install_deps()
        self._sources = [
            NeteaseCloudMusic(),
            QQMusicReader(),
            KugouMemoryReader(),
            GSMTCReader(),
        ]
        self._last_media_key: str = ""

    def get_info(self) -> Optional[MediaInfo]:
        for i, source in enumerate(self._sources):
            if source.available:
                try:
                    info = source.get_info()
                    if info and info.is_valid():
                        media_key = f"{info.title}|{info.artist}"
                        if media_key != self._last_media_key:
                            self._last_media_key = media_key
                            logger.info(f"媒体源 [{i}] {source.name}: 成功获取媒体信息 - {info.title} - {info.artist}")
                        return info
                    else:
                        logger.debug(f"媒体源 [{i}] {source.name}: 获取到无效信息")
                except Exception as e:
                    logger.error(f"媒体源 [{i}] {source.name}: 执行异常 {e}")
        return None

    def get_source(self, name: str):
        for source in self._sources:
            if source.name == name:
                return source
        return None

    def add_source(self, source):
        self._sources.insert(0, source)

    def close(self):
        for source in self._sources:
            source.close()


_provider = MediaProvider()


def get_media_info() -> Optional[MediaInfo]:
    return _provider.get_info()

def get_netease() -> NeteaseCloudMusic:
    return _provider.get_source("NeteaseCloudMusic")

def get_gstmtc() -> GSMTCReader:
    return _provider.get_source("GSMTC")

def fetch_all_info(song_name: str, artist: str = "") -> Dict[str, Any]:
    kg = _provider.get_source("KugouMemory")
    if kg and kg.available:
        result = kg.fetch_all(song_name, artist)
        if result.get('lyrics') or result.get('cover'):
            return result
    ncm = get_netease()
    if ncm:
        return ncm.fetch_all(song_name, artist)
    return {'song_id': None, 'detail': None, 'lyrics': None, 'cover': None}

def close():
    _provider.close()
