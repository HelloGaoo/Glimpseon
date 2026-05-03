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
歌词服务模块
"""

import logging
import re
import sys
import os
import time
from dataclasses import dataclass
from typing import List, Optional, Tuple

import requests

if getattr(sys, 'frozen', False):
    _BASE_DIR = os.path.dirname(os.path.abspath(sys.executable))
else:
    _BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _BASE_DIR not in sys.path:
    sys.path.insert(0, _BASE_DIR)

logger = logging.getLogger(__name__)


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
                    
                    if len(match[2]) == 2:
                        milliseconds *= 10
                    elif len(match[2]) == 1:
                        milliseconds *= 100
                    
                    time_ms = minutes * 60 * 1000 + seconds * 1000 + milliseconds
                    lines.append(LyricLine(time_ms=time_ms, text=text))
                except (ValueError, IndexError):
                    continue
        
        lines.sort()
        return lines


class LyricsService:
    """歌词服务"""
    
    SEARCH_API = "https://music.163.com/api/search/get"
    LYRIC_API = "https://music.163.com/api/song/lyric"
    
    HEADERS = {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
        'Referer': 'https://music.163.com/',
    }
    
    REQUEST_TIMEOUT = 10
    
    def __init__(self):
        self._cache: dict = {}
        self._cache_max_size = 100
        self._last_request_time = 0
        self._request_interval = 0.5
    
    def _wait_rate_limit(self):
        elapsed = time.time() - self._last_request_time
        if elapsed < self._request_interval:
            time.sleep(self._request_interval - elapsed)
        self._last_request_time = time.time()
    
    def _get_cache_key(self, song_name: str, artist_name: str = "") -> str:
        return f"{song_name}_{artist_name}".lower().strip()
    
    def _get_from_cache(self, key: str) -> Optional[Lyrics]:
        return self._cache.get(key)
    
    def _save_to_cache(self, key: str, lyrics: Lyrics):
        if len(self._cache) >= self._cache_max_size:
            keys = list(self._cache.keys())
            for k in keys[:len(keys) // 2]:
                del self._cache[k]
        
        self._cache[key] = lyrics
    
    def search_song(self, keyword: str) -> Optional[int]:
        if not keyword:
            return None
        
        try:
            self._wait_rate_limit()
            params = {
                's': keyword,
                'type': '1',
                'limit': '5',
            }
            response = requests.get(
                self.SEARCH_API,
                params=params,
                headers=self.HEADERS,
                timeout=self.REQUEST_TIMEOUT
            )
            if response.status_code != 200:
                logger.debug(f"搜索歌曲失败，状态码: {response.status_code}")
                return None
            data = response.json()
            if data.get('code') != 200:
                logger.debug(f"搜索歌曲 API 返回错误: {data.get('code')}")
                return None
            songs = data.get('result', {}).get('songs', [])
            if not songs:
                logger.debug(f"未找到歌曲: {keyword}")
                return None
            for song in songs:
                song_id = song.get('id')
                if song_id:return song_id
            
            return None
            
        except requests.exceptions.Timeout:
            logger.warning(f"搜索歌曲超时: {keyword}")
            return None
        except requests.exceptions.RequestException as e:
            logger.warning(f"搜索歌曲请求失败: {e}")
            return None
        except Exception as e:
            logger.error(f"搜索歌曲异常: {e}")
            return None
    
    def get_lyrics(self, song_id: int) -> Optional[str]:
        if not song_id:
            return None
        
        try:
            self._wait_rate_limit()
            
            params = {
                'id': song_id,
                'lv': '1',
                'tv': '-1',
            }
            
            response = requests.get(
                self.LYRIC_API,
                params=params,
                headers=self.HEADERS,
                timeout=self.REQUEST_TIMEOUT
            )
            
            if response.status_code != 200:
                logger.debug(f"获取歌词失败，状态码: {response.status_code}")
                return None
            
            data = response.json()
            
            if data.get('code') != 200:
                logger.debug(f"获取歌词 API 返回错误: {data.get('code')}")
                return None
            
            lrc = data.get('lrc', {})
            lyric_content = lrc.get('lyric', '')
            
            if not lyric_content:
                tlyric = data.get('tlyric', {})
                lyric_content = tlyric.get('lyric', '')
            
            return lyric_content if lyric_content else None
            
        except requests.exceptions.Timeout:
            logger.warning(f"获取歌词超时: song_id={song_id}")
            return None
        except requests.exceptions.RequestException as e:
            logger.warning(f"获取歌词请求失败: {e}")
            return None
        except Exception as e:
            logger.error(f"获取歌词异常: {e}")
            return None
    
    def fetch_lyrics(self, song_name: str, artist_name: str = "") -> Optional[Lyrics]:
        if not song_name:return None
        cache_key = self._get_cache_key(song_name, artist_name)
        cached = self._get_from_cache(cache_key)
        if cached:return cached
        keyword = f"{song_name} {artist_name}".strip()
        song_id = self.search_song(keyword)
        if not song_id:
            clean_keyword = re.sub(r'[^\w\s\u4e00-\u9fff]', '', song_name)
            if clean_keyword != keyword:song_id = self.search_song(clean_keyword)
        if not song_id:return None
        lrc_content = self.get_lyrics(song_id)
        if not lrc_content:return None
        lines = LyricParser.parse(lrc_content)
        if not lines:return None
        
        lyrics = Lyrics(
            lines=lines,
            raw_lrc=lrc_content,
            song_id=song_id,
            song_name=song_name,
            artist_name=artist_name
        )
        
        self._save_to_cache(cache_key, lyrics)
        
        return lyrics
    
    def clear_cache(self):
        self._cache.clear()


_lyrics_service_instance: Optional[LyricsService] = None


def get_lyrics_service() -> LyricsService:
    global _lyrics_service_instance
    if _lyrics_service_instance is None:_lyrics_service_instance = LyricsService()
    return _lyrics_service_instance
