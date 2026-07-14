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

"""
一言服务模块
"""

import logging
from typing import Optional

import requests

from core.config import cfg
from core.logger import logger
from core.utils import tr,  get_cached_content, save_cache
FALLBACK_POETRY = tr("poetry.default")


class PoetryService:
    """一言服务"""

    API_MAP = {
        '一言 API': 'https://api.imlcd.cn/yy/api.php',
        '诗词 API': 'https://www.ffapi.cn/int/v1/shici',
    }

    @staticmethod
    def get_poetry(api_url: Optional[str] = None) -> str:
        """
        获取一句一言
        """
        if api_url is None:
            api_url = cfg.poetryApiUrl.value

        try:
            logger.debug(f"一言 API URL: {api_url}")
            response = requests.get(api_url, timeout=10)

            if response.status_code == 200:
                logger.debug(f"一言 API 请求成功，状态码：{response.status_code}")
                text = response.text.strip()
                if text:
                    return text
                logger.warning("一言 API 返回空内容")
                return FALLBACK_POETRY
            else:
                logger.error(f"一言 API 请求失败，状态码：{response.status_code}")
                return FALLBACK_POETRY
        except Exception as e:
            logger.error(f"获取一言失败：{e}")
            return FALLBACK_POETRY

    @staticmethod
    def get_poetry_with_cache() -> str:
        """获取一言"""
        cached = get_cached_content("poetry")
        if cached:
            return cached

        text = PoetryService.get_poetry()
        save_cache("poetry", text, cfg.poetryUpdateInterval.value)
        return text

    @staticmethod
    def get_api_name(url: str) -> str:
        """获取API名称"""
        for name, api_url in PoetryService.API_MAP.items():
            if api_url == url:
                return name
        return '一言 API'

    @staticmethod
    def get_api_url(name: str) -> str:
        """获取API URL"""
        return PoetryService.API_MAP.get(name, 'https://api.imlcd.cn/yy/api.php')
