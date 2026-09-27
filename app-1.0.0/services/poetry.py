# Glimpseon
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

"""一言服务"""

from typing import Optional

import requests

from core.config import cfg
from core.logger import logger
from core.utils import tr, get_cached_content, save_cache

FALLBACK_POETRY = tr("poetry.default")


class PoetryService:

    @staticmethod
    def get_poetry(api_url: Optional[str] = None) -> str:
        if api_url is None:
            api_url = cfg.poetryApiUrl.value

        try:
            logger.debug(f"一言 api {api_url}")
            response = requests.get(api_url, timeout=10)

            if response.status_code == 200:
                logger.debug(f"一言 已请求 {response.status_code}")
                text = response.text.strip()
                if text:
                    try:
                        data = response.json()
                        hitokoto = str(data.get("hitokoto", "")).strip()
                        if hitokoto:
                            source = data.get("from") or data.get("from_who")
                            if source:
                                return f"{hitokoto}——{source}"
                            return hitokoto
                    except Exception as e:
                        logger.debug(f"一言 json 解析失败 用原始文本: {e}")
                    # 非 json
                    logger.debug(f"一言返回非 json 文本 长度 {len(text)}")
                    return text
                logger.warning("一言 返回空内容")
                return FALLBACK_POETRY
            else:
                logger.error(f"一言 请求失败 {response.status_code}")
                return FALLBACK_POETRY
        except Exception as e:
            logger.error(f"获取一言失败 {e}")
            return FALLBACK_POETRY

    @staticmethod
    def get_poetry_with_cache() -> str:
        cached = get_cached_content("poetry")
        if cached:
            logger.debug("一言缓存命中")
            return cached

        text = PoetryService.get_poetry()
        save_cache("poetry", text, cfg.poetryUpdateInterval.value)
        return text
