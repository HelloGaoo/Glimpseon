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
媒体信息服务模块
优先级: 1. 内存读取(实时进度) 2. GSMTC API 3. 窗口标题
"""

import asyncio
import logging
import sys
import os
import ctypes
from dataclasses import dataclass
from typing import Optional

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

logger = logging.getLogger(__name__)

GSMTC_AVAILABLE = False
MediaManager = None
PlaybackStatus = None

try:
    from winsdk.windows.media.control import (
        GlobalSystemMediaTransportControlsSessionManager as MediaManager,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus as PlaybackStatus,
    )
    GSMTC_AVAILABLE = True
    logger.info("Windows GSMTC API 已加载")
except Exception as e:
    logger.warning(f"加载 Windows GSMTC API 失败: {e}")

NCM_MEMORY_AVAILABLE = False
try:
    from services.ncm_memory import get_ncm_reader, NCMProgressReader
    NCM_MEMORY_AVAILABLE = True
    logger.info("网易云内存读取模块已加载")
except Exception as e:
    logger.warning(f"加载网易云内存读取模块失败: {e}")


def get_netease_window_title() -> Optional[str]:
    try:
        user32 = ctypes.windll.user32
        hwnd = user32.FindWindowW("OrpheusBrowserHost", None)
        if not hwnd:
            return None
        length = user32.GetWindowTextLengthW(hwnd)
        if length <= 0:
            return None
        buf = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, buf, length + 1)
        title = buf.value.strip()
        if title and len(title) > 2:
            return title
        return None
    except Exception as e:
        logger.debug(f"获取网易云窗口标题失败: {e}")
        return None


def parse_netease_title(title: str) -> tuple:
    if not title or ' - ' not in title:
        return "", ""
    parts = title.rsplit(' - ', 1)
    if len(parts) == 2:
        song_name = parts[0].strip()
        artist = parts[1].strip()
        if artist and (song_name != "网易云音乐"):
            return song_name, artist
    return "", ""


@dataclass
class MediaInfo:
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
        if self.duration_ms <= 0:
            return 0.0
        return min(1.0, max(0.0, self.position_ms / self.duration_ms))

    def format_position(self) -> str:
        return self._format_time(self.position_ms)

    def format_duration(self) -> str:
        return self._format_time(self.duration_ms)

    @staticmethod
    def _format_time(ms: int) -> str:
        seconds = max(0, ms // 1000)
        minutes = seconds // 60
        secs = seconds % 60
        return f"{minutes}:{secs:02d}"


class MediaService:
    PLAYBACK_STATUS_MAP = {
        PlaybackStatus.CLOSED: "closed",
        PlaybackStatus.OPENED: "opened",
        PlaybackStatus.CHANGING: "changing",
        PlaybackStatus.STOPPED: "stopped",
        PlaybackStatus.PLAYING: "playing",
        PlaybackStatus.PAUSED: "paused",
    } if GSMTC_AVAILABLE else {}

    def __init__(self):
        self._session_manager = None
        self._initialized = False

    @property
    def is_available(self) -> bool:
        return GSMTC_AVAILABLE

    async def initialize(self) -> bool:
        if not GSMTC_AVAILABLE:
            return False
        try:
            self._session_manager = await MediaManager.request_async()
            self._initialized = True
            return True
        except Exception as e:
            logger.error(f"初始化媒体服务失败: {e}")
            return False

    async def get_current_media(self) -> Optional[MediaInfo]:
        if not self._initialized or not self._session_manager:
            if not await self.initialize():
                return None
        try:
            session = self._session_manager.get_current_session()
            if not session:
                return MediaInfo()
            media_info = MediaInfo()
            try:
                app_name = session.source_app_user_model_id
                if app_name:
                    if '.' in app_name:
                        parts = app_name.split('.')
                        media_info.app_name = parts[-1] if parts[-1] else parts[0]
                    else:
                        media_info.app_name = app_name
            except Exception:
                pass
            try:
                playback_info = session.get_playback_info()
                if playback_info:
                    status_enum = playback_info.playback_status
                    media_info.playback_status = self.PLAYBACK_STATUS_MAP.get(status_enum, "unknown")
                    media_info.is_playing = status_enum == PlaybackStatus.PLAYING
            except Exception as e:
                logger.debug(f"获取播放状态失败: {e}")
            try:
                timeline = session.get_timeline_properties()
                if timeline:
                    media_info.position_ms = max(0, int(timeline.position.total_seconds() * 1000))
                    media_info.duration_ms = max(0, int(timeline.end_time.total_seconds() * 1000))
            except Exception as e:
                logger.debug(f"获取时间轴失败: {e}")
            try:
                media_properties = await session.try_get_media_properties_async()
                if media_properties:
                    media_info.title = media_properties.title or ""
                    media_info.artist = media_properties.artist or ""
                    media_info.album = media_properties.album_title or ""
                    media_info.title_artist = f"{media_info.title} - {media_info.artist}" if media_info.artist else media_info.title
                    if media_properties.thumbnail:
                        try:
                            stream = await media_properties.thumbnail.open_read_async()
                            if stream:
                                size = stream.size
                                if size > 0 and size < 10 * 1024 * 1024:
                                    reader = stream.input_stream
                                    buffer = bytes(size)
                                    await reader.read_async(buffer, size, 0)
                                    media_info.thumbnail_data = buffer
                        except Exception as e:
                            logger.debug(f"获取封面失败: {e}")
            except Exception as e:
                logger.debug(f"获取媒体属性失败: {e}")
            return media_info
        except Exception as e:
            logger.error(f"获取媒体信息失败: {e}")
            return None


_media_service_instance: Optional[MediaService] = None


def get_media_service() -> MediaService:
    global _media_service_instance
    if _media_service_instance is None:
        _media_service_instance = MediaService()
    return _media_service_instance


def get_media_info_sync() -> Optional[MediaInfo]:
    # 优先级1: 网易云内存读取（获取实时进度+歌曲ID）
    if NCM_MEMORY_AVAILABLE:
        try:
            reader = get_ncm_reader()
            if reader.is_available:
                progress_data = reader.read_progress()
                if progress_data:
                    song_id = progress_data.get('song_id', '')
                    playback_time = progress_data.get('playback_time', 0.0)
                    is_playing = progress_data.get('is_playing', True)
                    
                    position_ms = int(playback_time * 1000)
                    
                    window_title = get_netease_window_title()
                    song_name, artist = "", ""
                    if window_title:
                        song_name, artist = parse_netease_title(window_title)
                    
                    title_artist = f"{song_name} - {artist}" if artist else song_name
                    
                    info = MediaInfo(
                        title=song_name,
                        artist=artist,
                        title_artist=title_artist,
                        position_ms=position_ms,
                        is_playing=is_playing,
                        playback_status="playing" if is_playing else "paused",
                        app_name="NeteaseCloudMusic",
                        song_id=song_id,
                    )
                    
                    logger.debug(f"内存读取: {song_name} - {artist}, 进度={playback_time:.1f}s, ID={song_id}, 播放={is_playing}")
                    return info
        except Exception as e:
            logger.debug(f"网易云内存读取失败: {e}")

    # 优先级2: GSMTC API
    service = get_media_service()
    if service.is_available:
        try:
            loop = asyncio.new_event_loop()
            asyncio.set_event_loop(loop)
            try:
                result = loop.run_until_complete(service.get_current_media())
                if result and result.is_valid():
                    return result
            finally:
                try:
                    loop.close()
                except Exception:
                    pass
        except Exception as e:
            logger.debug(f"GSMTC 获取失败: {e}")

    # 优先级3: 窗口标题
    window_title = get_netease_window_title()
    if window_title:
        song_name, artist = parse_netease_title(window_title)
        if song_name or artist:
            return MediaInfo(
                title=song_name,
                artist=artist,
                title_artist=f"{song_name} - {artist}" if artist else song_name,
                is_playing=True,
                app_name="NeteaseCloudMusic"
            )

    return None
