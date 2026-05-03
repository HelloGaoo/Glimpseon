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
"""

import asyncio
import logging
import sys
import os
from dataclasses import dataclass
from typing import Optional, Callable
from io import BytesIO

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
except Exception as e:
    logger.warning(f"加载 Windows GSMTC API 失败: {e}")


@dataclass
class MediaInfo:
    """媒体信息数据类"""
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
    
    def is_valid(self) -> bool:
        return bool(self.title or self.artist)
    
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
    """媒体信息服务类"""
    
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
        self._current_session = None
        self._last_media_info: Optional[MediaInfo] = None
        self._last_title_artist: str = ""
        self._initialized = False
    
    @property
    def is_available(self) -> bool:
        return GSMTC_AVAILABLE
    
    async def initialize(self) -> bool:
        if not GSMTC_AVAILABLE:
            logger.warning("GSMTC 不可用，无法初始化媒体服务")
            return False
        
        try:
            self._session_manager = await MediaManager.request_async()
            self._initialized = True
            logger.info("媒体服务初始化成功")
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
            if not session:return MediaInfo()
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
            
            self._last_media_info = media_info
            if media_info.title_artist:
                self._last_title_artist = media_info.title_artist
            
            return media_info
            
        except Exception as e:
            logger.error(f"获取媒体信息失败: {e}")
            return None
    
    def get_last_media_info(self) -> Optional[MediaInfo]:
        return self._last_media_info
    
    def get_last_title_artist(self) -> str:
        return self._last_title_artist


_media_service_instance: Optional[MediaService] = None


def get_media_service() -> MediaService:
    global _media_service_instance
    if _media_service_instance is None:
        _media_service_instance = MediaService()
    return _media_service_instance


def get_media_info_sync() -> Optional[MediaInfo]:
    service = get_media_service()
    if not service.is_available:
        return None
    
    try:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        try:
            return loop.run_until_complete(service.get_current_media())
        finally:
            loop.close()
    except Exception as e:
        logger.error(f"同步获取媒体信息失败: {e}")
        return None
