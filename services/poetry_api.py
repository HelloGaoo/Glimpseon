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
诗词 API 服务模块

负责调用诗词 API 获取随机诗词
"""

import requests
import json
import logging
from typing import Optional, Dict, Any

logger = logging.getLogger(__name__)


class PoetryAPIService:
    """诗词 API 服务类"""
    
    def __init__(self, api_url: str = "https://www.ffapi.cn/int/v1/shici"):
        """
        初始化诗词 API 服务
        
        Args:
            api_url: API 地址
        """
        self.api_url = api_url
    
    def set_api_url(self, api_url: str):
        """
        设置 API 地址
        
        Args:
            api_url: API 地址
        """
        self.api_url = api_url
    
    def get_poetry(self) -> Optional[Dict[str, str]]:
        """
        获取诗词数据
        
        Returns:
            诗词数据字典，包含 content, author, origin
            如果获取失败返回 None
        """
        try:
            logger.info(f"正在请求诗词数据，API: {self.api_url}")
            response = requests.get(self.api_url, timeout=10)
            
            if response.status_code != 200:
                logger.error(f"诗词 API 请求失败，状态码：{response.status_code}")
                return None
            
            # 检查响应内容是否为空
            if not response.text or not response.text.strip():
                logger.error("诗词 API 返回空响应")
                return None
            
            # 尝试解析 JSON
            data = None
            try:
                data = response.json()
                logger.debug(f"诗词 API 响应数据 (JSON): {data}")
            except json.JSONDecodeError:
                # 不是 JSON，可能是纯文本格式
                logger.debug(f"诗词 API 返回纯文本：{response.text[:200]}")
                # 尝试解析纯文本格式："诗句——作者《标题》"
                text = response.text.strip()
                return self._parse_plain_poetry(text)
            
            # 解析诗词数据（兼容多种 API 格式）
            poetry_data = None
            
            # 格式 1: {"data": {...}}
            if 'data' in data and isinstance(data['data'], dict):
                poetry_data = data['data']
            # 格式 2: 直接返回诗词对象
            elif isinstance(data, dict) and 'content' in data:
                poetry_data = data
            # 格式 3: 返回数组
            elif isinstance(data, list) and len(data) > 0:
                poetry_data = data[0]
            
            if not poetry_data:
                logger.error("无法解析诗词数据格式")
                return None
            
            content = poetry_data.get('content', '')
            author = poetry_data.get('author', '')
            origin = poetry_data.get('origin', '')
            
            if not content:
                logger.error("诗词内容为空")
                return None
            
            result = {
                'content': content,
                'author': author,
                'origin': origin
            }
            
            logger.info(f"诗词数据获取成功：{content[:50]}...")
            return result
            
        except requests.exceptions.Timeout:
            logger.error("诗词 API 请求超时")
            return None
        except requests.exceptions.RequestException as e:
            logger.error(f"诗词 API 请求异常：{e}")
            return None
        except Exception as e:
            logger.error(f"解析诗词数据失败：{e}")
            return None
    
    def _parse_plain_poetry(self, text: str) -> Optional[Dict[str, str]]:
        """
        解析纯文本格式的诗词
        
        格式："酒逢知己千杯少，话不投机半句多。——《明贤集·七言集》"
        
        Args:
            text: 纯文本诗词
            
        Returns:
            诗词数据字典
        """
        try:
            logger.debug(f"解析纯文本诗词：{text}")
            
            # 尝试解析格式："内容——作者《标题》" 或 "内容——《标题》"
            if '——' in text:
                parts = text.split('——')
                content = parts[0].strip()
                
                if len(parts) > 1:
                    author_title = parts[1]
                    
                    # 提取标题（在《》中）
                    if '《' in author_title and '》' in author_title:
                        start = author_title.find('《')
                        end = author_title.find('》')
                        origin = author_title[start+1:end]
                        
                        # 提取作者（在《之前）
                        author = author_title[:start].strip()
                    else:
                        origin = ''
                        author = author_title.strip()
                else:
                    author = ''
                    origin = ''
            else:
                # 没有作者信息，只有内容
                content = text
                author = ''
                origin = ''
            
            if not content:
                logger.error("诗词内容为空")
                return None
            
            result = {
                'content': content,
                'author': author,
                'origin': origin
            }
            
            logger.info(f"纯文本诗词解析成功：{content[:30]}...")
            return result
            
        except Exception as e:
            logger.error(f"解析纯文本诗词失败：{e}")
            return None
    
    def format_poetry(self, poetry_data: Dict[str, str]) -> str:
        """
        格式化诗词文本
        
        Args:
            poetry_data: 诗词数据
            
        Returns:
            格式化后的诗词文本
        """
        content = poetry_data.get('content', '')
        author = poetry_data.get('author', '')
        origin = poetry_data.get('origin', '')
        
        if author and origin:
            return f"「{content}」\n——{author}《{origin}》"
        elif author:
            return f"「{content}」\n——{author}"
        else:
            return f"「{content}」"
