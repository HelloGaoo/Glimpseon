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

"""每日单词服务"""

import logging
from typing import Any, Dict, Optional
import requests

from core.utils import get_cached_content, save_cache

DAILY_WORD_API_URL = "https://uapis.cn/api/v1/daily/word"
CACHE_NAME = "daily_word"
CACHE_INTERVAL = "12h"
WORD_CATEGORY = "cet4"

logger = logging.getLogger("Glimpseon.services.word")


class WordService:
    @staticmethod
    def _create_session() -> requests.Session:
        session = requests.Session()
        session.headers.update({
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        })
        logger.debug("已创建每日单词请求会话")
        return session

    @staticmethod
    def _save_cache(content: Any) -> None:
        if not save_cache(CACHE_NAME, content, CACHE_INTERVAL):
            logger.warning(f"缓存保存失败: {CACHE_NAME}")

    @classmethod
    def fetch_daily_word(cls, use_cache: bool = True) -> Optional[Dict[str, Any]]:
        """获取每日单词

        返回:
            {"date": "YYYY-MM-DD", "word": {word, phonetic, translation, definition, collins, categories, examples}}
        """
        if use_cache:
            cached = get_cached_content(CACHE_NAME)
            if cached is not None:
                logger.debug("每日单词缓存命中")
                return cached

        with cls._create_session() as session:
            try:
                response = session.get(
                    DAILY_WORD_API_URL,
                    params={"category": WORD_CATEGORY},
                    timeout=10,
                )
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

            words = data.get("words") if isinstance(data, dict) else None
            if not isinstance(words, list) or not words or not isinstance(words[0], dict):
                logger.error("格式异常: words 无效")
                return None

            result = {
                "date": data.get("date", "") or "",
                "word": words[0],
            }
            cls._save_cache(result)
            w = result["word"]
            logger.info(f"每日单词已获取 {result['date']} {w.get('word', '')} {w.get('translation', '')[:30]}")
            return result
