# ClassLively
# Copyright (C) 2026 HelloGaoo
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version of the License.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

"""新闻服务模块"""

import logging
from typing import Any, Dict, List, Optional

import requests

from core.logger import logger
from core.utils import get_cached_content, save_cache

CCTV_NEWS_API_URL = "https://api.xcvts.cn/api/hotlist/ysxw?type=json"
DAILY_NEWS_API_URL = "https://orz.ai/api/v1/dailynews/"
SUPPORTED_PLATFORMS = {"baidu", "weibo", "jinritoutiao", "tenxunwang"}
CACHE_INTERVAL = "30m"

logger = logging.getLogger("ClassLively.services.news")


class NewsService:
    """新闻服务类"""

    @staticmethod
    def _create_session() -> requests.Session:
        session = requests.Session()
        session.headers.update({
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        })
        return session

    @staticmethod
    def _parse_response_json(response: requests.Response) -> Optional[Any]:
        try:
            return response.json()
        except ValueError as e:
            logger.error(f"新闻 JSON 解析失败: {e}")
            return None

    @staticmethod
    def _save_cache(cache_name: str, content: Any) -> None:
        if not save_cache(cache_name, content, CACHE_INTERVAL):
            logger.warning(f"新闻缓存保存失败: {cache_name}")

    @classmethod
    def fetch_cctv_news(cls, use_cache: bool = True) -> Optional[List[Dict[str, Any]]]:
        """央视新闻xcvts.cn API"""
        cache_name = "news_cctv"
        if use_cache:
            cached = get_cached_content(cache_name)
            if cached is not None:
                return cached

        session = cls._create_session()
        try:
            response = session.get(CCTV_NEWS_API_URL, timeout=10)
            if response.status_code != 200:
                logger.error(f"央视新闻请求失败，状态码：{response.status_code}")
                return None

            data = cls._parse_response_json(response)
            if data is None:
                return None

            if isinstance(data, dict):
                news_list = data.get("data") or data.get("news") or []
            elif isinstance(data, list):
                news_list = data
            else:
                news_list = []

            if not isinstance(news_list, list):
                logger.error("央视新闻获取失败")
                return None

            cls._save_cache(cache_name, news_list)
            return news_list
        except requests.exceptions.RequestException as e:
            logger.error(f"央视新闻请求异常：{e}")
            return None

    @classmethod
    def fetch_daily_news(cls, platform: str, use_cache: bool = True) -> Optional[List[Dict[str, Any]]]:
        """dailynews热点新闻。"""
        platform = platform.strip().lower()
        if platform not in SUPPORTED_PLATFORMS:
            logger.warning(f"不支持的平台：{platform}")
            return None

        cache_name = f"news_{platform}"
        if use_cache:
            cached = get_cached_content(cache_name)
            if cached is not None:
                return cached

        session = cls._create_session()
        try:
            response = session.get(DAILY_NEWS_API_URL, params={"platform": platform}, timeout=10)
            if response.status_code != 200:
                logger.error(f"每日热点新闻请求失败：{response.status_code}")
                return None

            data = cls._parse_response_json(response)
            if data is None:
                return None

            news_list = None
            if isinstance(data, dict):
                news_list = data.get("data")
                if news_list is None and data.get("status") in ("200", 200):
                    news_list = []
            elif isinstance(data, list):
                news_list = data

            if not isinstance(news_list, list):
                logger.error("每日热点新闻获取失败")
                return None

            cls._save_cache(cache_name, news_list)
            return news_list
        except requests.exceptions.RequestException as e:
            logger.error(f"每日热点新闻请求异常：{e}")
            return None

    @classmethod
    def fetch_supported_daily_news(cls, use_cache: bool = True) -> Dict[str, Optional[List[Dict[str, Any]]]]:
        """获取支持的平台热点新闻。"""
        result = {}
        for platform in SUPPORTED_PLATFORMS:
            result[platform] = cls.fetch_daily_news(platform, use_cache=use_cache)
        return result
