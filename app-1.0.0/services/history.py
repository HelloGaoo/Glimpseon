# Glimpseon
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

"""历史上的今天服务"""

import logging
from typing import Any, Dict, Optional
import requests

from core.utils import get_cached_content, save_cache

HISTORY_TODAY_API_URL = "https://tmini.net/api/today"
CACHE_NAME = "history_today"
CACHE_INTERVAL = "12h"

logger = logging.getLogger("Glimpseon.services.history")


class HistoryService:
    @staticmethod
    def _create_session() -> requests.Session:
        session = requests.Session()
        session.headers.update({
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        })
        logger.debug("已创建历史数据请求会话")
        return session

    @staticmethod
    def _save_cache(content: Any) -> None:
        if not save_cache(CACHE_NAME, content, CACHE_INTERVAL):
            logger.warning(f"缓存保存失败: {CACHE_NAME}")

    @classmethod
    def fetch_history_today(cls, use_cache: bool = True) -> Optional[Dict[str, Any]]:
        """获取历史上的今天
        返回:
            {"date": "YYYY年MM月DD日", "events": [{title, year, desc, link}, ...]}
        """
        if use_cache:
            cached = get_cached_content(CACHE_NAME)
            if cached is not None:
                logger.debug("历史数据缓存命中")
                return cached

        with cls._create_session() as session:
            try:
                response = session.get(HISTORY_TODAY_API_URL, params={"type": "json"}, timeout=10)
                if response.status_code != 200:
                    logger.error(f"请求失败: http {response.status_code}")
                    return None

                try:
                    data = response.json()
                except ValueError:
                    logger.error("请求失败: 非法json")
                    return None
            except requests.exceptions.RequestException as e:
                logger.error(f"请求失败: 网络异常 {e}")
                return None

            if not isinstance(data, dict) or data.get("code") not in (200, "200") or not isinstance(data.get("events"), list):
                code = data.get("code") if isinstance(data, dict) else None
                logger.error(f"数据格式异常: code={code} type={type(data).__name__}")
                return None

            result = {
                "date": data.get("date", ""),
                "events": data.get("events") or [],
            }
            cls._save_cache(result)
            events = result["events"]
            top_titles = "; ".join(
                (e.get("title") or "")[:24] for e in events[:3] if isinstance(e, dict)
            )
            logger.info(f"历史已获取 {result['date']} {len(events)}条 {top_titles}")
            return result
