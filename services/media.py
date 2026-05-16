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
import logging
import os
import re
import struct
import sys
import time
from dataclasses import dataclass
from typing import Optional, List, Tuple, Dict, Any

import requests

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

logger = logging.getLogger(__name__)


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
        self._api_last_time = 0
        self._available = self._check_deps()

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

    def get_info(self) -> Optional[MediaInfo]:
        data = self._read_memory()
        if not data:
            return None
        title, artist = self._parse_window_title()
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
                self._api_cache[key] = detail
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
                    self._api_cache[key] = lyrics
                    return lyrics
        return None

    def get_cover(self, url: str) -> Optional[bytes]:
        key = f"cover_{hash(url)}"
        if key in self._api_cache:
            return self._api_cache[key]
        try:
            self._api_wait()
            resp = requests.get(url, headers=self.API_HEADERS, timeout=15)
            if resp.status_code == 200 and 'image' in resp.headers.get('Content-Type', ''):
                data = resp.content
                if 1024 < len(data) < 10 * 1024 * 1024:
                    self._api_cache[key] = data
                    return data
        except Exception:
            pass
        return None

    def fetch_all(self, song_name: str, artist: str = "") -> Dict[str, Any]:
        result = {'song_id': None, 'detail': None, 'lyrics': None, 'cover': None}
        song_id = self._search_song(f"{song_name} {artist}".strip())
        if not song_id:
            return result
        result['song_id'] = song_id
        detail = self.get_detail(song_id)
        if detail:
            result['detail'] = detail
            if detail.cover_url:
                result['cover'] = self.get_cover(detail.cover_url)
        result['lyrics'] = self.get_lyrics(song_id)
        return result

    def close(self):
        if self._pm:
            try:
                self._pm.close_process()
            except Exception:
                pass
            self._pm = None
        self._api_cache.clear()

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
        if elapsed < 0.3:
            time.sleep(0.3 - elapsed)
        self._api_last_time = time.time()

    def _api_get(self, endpoint: str, params: dict = None) -> Optional[dict]:
        try:
            self._api_wait()
            resp = requests.get(f"{self.API_BASE}{endpoint}", params=params, headers=self.API_HEADERS, timeout=15)
            if resp.status_code == 200:
                data = resp.json()
                if data.get('code') == 200 or data.get('code') is None:
                    return data
        except Exception:
            pass
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
        if self._available:
            try:
                from winsdk.windows.media.control import GlobalSystemMediaTransportControlsSessionPlaybackStatus as PlaybackStatus
                self.STATUS_MAP = {
                    PlaybackStatus.CLOSED: "closed", PlaybackStatus.OPENED: "opened",
                    PlaybackStatus.CHANGING: "changing", PlaybackStatus.STOPPED: "stopped",
                    PlaybackStatus.PLAYING: "playing", PlaybackStatus.PAUSED: "paused",
                }
            except Exception:
                pass

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
            return None
        try:
            from winsdk.windows.media.control import GlobalSystemMediaTransportControlsSessionManager as MediaManager
            import asyncio

            async def _read():
                if not self._initialized:
                    self._manager = await MediaManager.request_async()
                    self._initialized = True

                session = self._manager.get_current_session()
                if not session:
                    return None

                info = MediaInfo()

                try:
                    app = session.source_app_user_model_id
                    info.app_name = app.split('.')[-1] if app and '.' in app else app or ""
                except Exception:
                    pass

                try:
                    pb = session.get_playback_info()
                    if pb:
                        info.playback_status = self.STATUS_MAP.get(pb.playback_status, "unknown")
                        info.is_playing = pb.playback_status.value == 4
                except Exception:
                    pass

                try:
                    tl = session.get_timeline_properties()
                    if tl:
                        info.position_ms = max(0, int(tl.position.total_seconds() * 1000))
                        info.duration_ms = max(0, int(tl.end_time.total_seconds() * 1000))
                except Exception:
                    pass

                try:
                    props = await session.try_get_media_properties_async()
                    if props:
                        info.title = props.title or ""
                        info.artist = props.artist or ""
                        info.album = props.album_title or ""
                        info.title_artist = f"{info.title} - {info.artist}" if info.artist else info.title
                        if props.thumbnail:
                            try:
                                stream = await props.thumbnail.open_read_async()
                                if stream and 0 < stream.size < 10 * 1024 * 1024:
                                    buf = bytes(stream.size)
                                    await stream.input_stream.read_async(buf, stream.size, 0)
                                    info.thumbnail_data = buf
                            except Exception:
                                pass
                except Exception:
                    pass

                return info

            loop = asyncio.new_event_loop()
            asyncio.set_event_loop(loop)
            try:
                return loop.run_until_complete(_read())
            finally:
                loop.close()

        except Exception as e:
            logger.debug(f"GSMTC读取失败: {e}")
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
        pass


class MediaProvider:
    """调度器"""

    def __init__(self):
        self._sources = [
            NeteaseCloudMusic(),
            GSMTCReader(),
        ]

    def get_info(self) -> Optional[MediaInfo]:
        for source in self._sources:
            if source.available:
                info = source.get_info()
                if info and info.is_valid():
                    return info
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
    ncm = get_netease()
    if ncm:
        return ncm.fetch_all(song_name, artist)
    return {'song_id': None, 'detail': None, 'lyrics': None, 'cover': None}

def close():
    _provider.close()
