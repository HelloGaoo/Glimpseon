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
音乐服务模块
"""

import logging
import re
import sys
import os
import time
from dataclasses import dataclass
from typing import List, Optional, Tuple, Dict, Any

import requests

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

logger = logging.getLogger(__name__)

NCM_API_BASE = "https://music163.xuanmou.com.cn"

HEADERS = {
    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
}

REQUEST_TIMEOUT = 15


@dataclass
class LyricLine:
    """单行歌词"""
    time_ms: int
    text: str
    
    def __lt__(self, other):
        return self.time_ms < other.time_ms
    
    def __eq__(self, other):
        if not isinstance(other, LyricLine):return False
        return self.time_ms == other.time_ms and self.text == other.text


@dataclass
class Lyrics:
    lines: List[LyricLine]
    raw_lrc: str
    song_id: Optional[int] = None
    song_name: str = ""
    artist_name: str = ""
    
    def is_empty(self) -> bool:
        return len(self.lines) == 0
    
    def get_line(self, time_ms: int) -> Tuple[Optional[LyricLine], int]:
        if self.is_empty():return None, -1
        for i in range(len(self.lines) - 1, -1, -1):
            if self.lines[i].time_ms <= time_ms:return self.lines[i], i
        return self.lines[0], 0
    
    def get_surrounding_lines(self, time_ms: int, count: int = 3) -> List[Tuple[LyricLine, bool]]:
        if self.is_empty():return []
        _, current_idx = self.get_line(time_ms)
        result = []
        half = count // 2
        start_idx = max(0, current_idx - half)
        end_idx = min(len(self.lines), start_idx + count)
        if end_idx - start_idx < count:start_idx = max(0, end_idx - count)
        for i in range(start_idx, end_idx):result.append((self.lines[i], i == current_idx))
        return result


@dataclass
class SongDetail:
    """歌曲信息"""
    song_id: int
    name: str
    artists: List[str]
    album_name: str
    cover_url: str
    duration: int


class LyricParser:
    """LRC"""
    TIME_PATTERN = re.compile(r'\[(\d{1,2}):(\d{1,2})(?:\.(\d{1,3}))?\]')
    
    @classmethod
    def parse(cls, lrc_content: str) -> List[LyricLine]:
        if not lrc_content:return []
        lines = []
        for line in lrc_content.split('\n'):
            line = line.strip()
            if not line:continue
            matches = cls.TIME_PATTERN.findall(line)
            if not matches:continue
            text = cls.TIME_PATTERN.sub('', line).strip()
            if not text:continue
            
            for match in matches:
                try:
                    minutes = int(match[0])
                    seconds = int(match[1])
                    milliseconds = int(match[2]) if match[2] else 0
                    
                    if len(match[2]) == 2:milliseconds *= 10
                    elif len(match[2]) == 1:milliseconds *= 100
                    
                    time_ms = minutes * 60 * 1000 + seconds * 1000 + milliseconds
                    lines.append(LyricLine(time_ms=time_ms, text=text))
                except (ValueError, IndexError):
                    continue
        
        lines.sort()
        return lines


class NeteaseCloudService:
    """网易云音乐服务"""
    
    def __init__(self):
        self._cache: Dict[str, Any] = {}
        self._cache_max_size = 200
        self._last_request_time = 0
        self._request_interval = 0.3
    
    def _wait_rate_limit(self):
        elapsed = time.time() - self._last_request_time
        if elapsed < self._request_interval:
            time.sleep(self._request_interval - elapsed)
        self._last_request_time = time.time()
    
    def _get_cache_key(self, *args) -> str:
        return "_".join(str(a) for a in args).lower().strip()
    
    def _get_cache(self, key: str) -> Optional[Any]:
        return self._cache.get(key)
    
    def _save_cache(self, key: str, data: Any):
        if len(self._cache) >= self._cache_max_size:
            keys = list(self._cache.keys())
            for k in keys[:len(keys) // 2]:
                del self._cache[k]
        self._cache[key] = data
    
    def _request(self, endpoint: str, params: Dict = None) -> Optional[Dict]:
        if params is None:params = {}
        
        try:
            self._wait_rate_limit()
            
            url = f"{NCM_API_BASE}{endpoint}"
            response = requests.get(
                url,
                params=params,
                headers=HEADERS,
                timeout=REQUEST_TIMEOUT
            )
            
            if response.status_code != 200:
                logger.debug(f"请求失败 {endpoint}: HTTP {response.status_code}")
                return None
            
            data = response.json()
            
            if data.get('code') != 200 and data.get('code') is not None:
                logger.debug(f"API返回错误 {endpoint}: code={data.get('code')}")
                return None
            
            return data
            
        except requests.exceptions.Timeout:
            logger.warning(f"请求超时: {endpoint}")
            return None
        except requests.exceptions.RequestException as e:
            logger.warning(f"请求异常 {endpoint}: {e}")
            return None
        except Exception as e:
            logger.error(f"请求错误 {endpoint}: {e}")
            return None
    
    def search_song(self, keyword: str, artist: str = "") -> Optional[int]:
        """搜索歌曲 返回song_id"""
        if not keyword:return None
        
        search_text = keyword
        if artist:search_text = f"{keyword} {artist}"
        
        logger.debug(f"搜索歌曲: {search_text}")
        
        data = self._request("/cloudsearch", {
            'keywords': search_text,
            'limit': '5',
            'type': '1'
        })
        
        if not data:return None
        
        result = data.get('result', {})
        songs = result.get('songs', [])
        
        if not songs:
            logger.debug(f"未找到歌曲: {search_text}")
            return None
        
        best_match = songs[0]
        song_id = best_match.get('id')
        song_name = best_match.get('name', '')
        artists = [a.get('name', '') for a in best_match.get('artists', [])]
        
        logger.info(f"找到歌曲: {song_name} - {'/'.join(artists)} (ID: {song_id})")
        return song_id
    
    def get_song_detail(self, song_id: int) -> Optional[SongDetail]:
        """获取信息"""
        if not song_id:return None
        
        cache_key = self._get_cache_key("detail", song_id)
        cached = self._get_cache(cache_key)
        if cached:return cached
        logger.debug(f"获取歌曲详情: ID={song_id}")
        data = self._request("/song/detail", {
            'ids': str(song_id),
        })
        if not data:return None
        songs = data.get('songs', [])
        if not songs:return None
        song = songs[0]
        al = song.get('al', {})
        ars = [a.get('name', '') for a in song.get('ar', [])]
        
        detail = SongDetail(
            song_id=song.get('id', song_id),
            name=song.get('name', ''),
            artists=ars,
            album_name=al.get('name', ''),
            cover_url=al.get('picUrl', ''),
            duration=song.get('dt', 0)
        )
        
        self._save_cache(cache_key, detail)
        logger.debug(f"歌曲详情: {detail.name} - 封面: {bool(detail.cover_url)}")
        return detail
    
    def get_lyrics(self, song_id: int) -> Optional[Lyrics]:
        """获取歌词"""
        if not song_id:return None
        
        cache_key = self._get_cache_key("lyric", song_id)
        cached = self._get_cache(cache_key)
        if cached and isinstance(cached, Lyrics):return cached
        
        logger.debug(f"获取歌词: ID={song_id}")
        
        data = self._request("/lyric", {
            'id': str(song_id),
        })
        
        if not data:return None
        
        lrc_data = data.get('lrc', {})
        lyric_content = lrc_data.get('lyric', '')
        
        if not lyric_content:
            tlyric_data = data.get('tlyric', {})
            lyric_content = tlyric_data.get('lyric', '')
        
        if not lyric_content:
            logger.debug(f"无歌词内容: ID={song_id}")
            return None
        
        lines = LyricParser.parse(lyric_content)
        if not lines:return None
        
        lyrics = Lyrics(
            lines=lines,
            raw_lrc=lyric_content,
            song_id=song_id
        )
        
        self._save_cache(cache_key, lyrics)
        logger.info(f"获取到歌词: {len(lines)}行")
        return lyrics
    
    def fetch_cover_image(self, cover_url: str) -> Optional[bytes]:
        """下载封面图片"""
        if not cover_url:return None
        
        cache_key = self._get_cache_key("cover_img", hash(cover_url))
        cached = self._get_cache(cache_key)
        if cached:return cached
        
        try:
            self._wait_rate_limit()
            
            response = requests.get(
                cover_url,
                headers={
                    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
                    'Referer': 'https://music.163.com/'
                },
                timeout=REQUEST_TIMEOUT
            )
            
            if response.status_code == 200:
                content_type = response.headers.get('Content-Type', '')
                if 'image' in content_type:
                    img_data = response.content
                    if 1024 < len(img_data) < 10 * 1024 * 1024:
                        self._save_cache(cache_key, img_data)
                        logger.debug(f"下载封面成功: {len(img_data)} bytes")
                        return img_data
            
            return None
            
        except Exception as e:
            logger.debug(f"下载封面失败: {e}")
            return None
    
    def fetch_all_info(self, song_name: str, artist: str = "") -> Dict[str, Any]:
        result = {
            'song_id': None,
            'detail': None,
            'lyrics': None,
            'cover_data': None
        }
        
        song_id = self.search_song(song_name, artist)
        if not song_id:return result
        
        result['song_id'] = song_id
        
        detail = self.get_song_detail(song_id)
        if detail:
            result['detail'] = detail
            if detail.cover_url:
                result['cover_data'] = self.fetch_cover_image(detail.cover_url)
        
        result['lyrics'] = self.get_lyrics(song_id)
        
        return result
    
    def clear_cache(self):
        self._cache.clear()


_netease_service_instance: Optional[NeteaseCloudService] = None


def get_netease_service() -> NeteaseCloudService:
    global _netease_service_instance
    if _netease_service_instance is None:_netease_service_instance = NeteaseCloudService()
    return _netease_service_instance


def get_lyrics_service() -> NeteaseCloudService:
    return get_netease_service()
