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

"""每日英语（每日一句）服务"""

import logging
from typing import Any, Dict, Optional
import requests

from core.utils import get_cached_content, save_cache

DAILY_SENTENCE_API_URL = "https://api.timelessq.com/english-sentence"
CACHE_NAME = "daily_sentence"
CACHE_INTERVAL = "12h"

logger = logging.getLogger("Glimpseon.services.sentence")


class SentenceService:
    @staticmethod
    def _create_session() -> requests.Session:
        session = requests.Session()
        session.headers.update({
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        })
        logger.debug("已创建每日一句请求会话")
        return session

    @staticmethod
    def _save_cache(content: Any) -> None:
        if not save_cache(CACHE_NAME, content, CACHE_INTERVAL):
            logger.warning(f"缓存保存失败: {CACHE_NAME}")

    @classmethod
    def fetch_daily_sentence(cls, use_cache: bool = True) -> Optional[Dict[str, Any]]:
        """获取每日英语句子

        返回:
            {"date": "YYYY-MM-DD", "sentence": {content, note, translation}}
        """
        if use_cache:
            cached = get_cached_content(CACHE_NAME)
            if cached is not None:
                logger.debug("每日一句缓存命中")
                return cached

        with cls._create_session() as session:
            try:
                response = session.get(DAILY_SENTENCE_API_URL, timeout=10)
                if response.status_code != 200:
                    logger.error(f"请求失败: http {response.status_code}")
                    return None

                try:
                    data = response.json()
                except ValueError:
                    logger.error("请求失败: 非法 json")
                    return None
            except requests.exceptions.RequestException as e:
                logger.error(f"请求失败: 网络异常 {e}")
                return None

            item = data.get("data") if isinstance(data, dict) else None
            if not isinstance(item, dict) or not item.get("content"):
                logger.error("数据格式异常: data 无效")
                return None

            result = {
                "date": str(item.get("date", "") or ""),
                "sentence": {
                    "content": item.get("content") or "",
                    "note": item.get("note") or "",
                    "translation": item.get("translation") or "",
                },
            }
            cls._save_cache(result)
            logger.info(f"每日一句已获取 {result['date']} {result['sentence']['content'][:40]}")
            return result
