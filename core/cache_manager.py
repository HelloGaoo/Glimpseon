import json
import logging
import os
import time
from typing import Any, Optional

logger = logging.getLogger(__name__)

CACHE_DIR = "data/cache"

INTERVAL_MAP = {
    "从不": 0,
    "5 分钟": 300,
    "10 分钟": 600,
    "15 分钟": 900,
    "30 分钟": 1800,
    "1 小时": 3600,
    "3 小时": 10800,
    "6 小时": 21600,
    "12 小时": 43200,
    "1 天": 86400,
    "3 天": 259200,
    "5 天": 432000,
    "7 天": 604800,
    "10分钟": 600,
    "30分钟": 1800,
    "1小时": 3600,
    "3小时": 10800,
    "6小时": 21600,
    "12小时": 43200,
    "1天": 86400,
    "3天": 259200,
    "5天": 432000,
    "7天": 604800,
}


def get_cache_dir():
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    cache_dir = os.path.join(base_dir, CACHE_DIR)
    os.makedirs(cache_dir, exist_ok=True)
    return cache_dir


def get_cache_path(cache_name: str) -> str:
    cache_dir = get_cache_dir()
    return os.path.join(cache_dir, f"{cache_name}.json")


def parse_interval(interval_str: str) -> int:
    return INTERVAL_MAP.get(interval_str.strip(), 0)


def save_cache(cache_name: str, content: Any, interval_str: str = "30分钟"):
    cache_path = get_cache_path(cache_name)
    interval_seconds = parse_interval(interval_str)
    
    now = time.time()
    cache_data = {
        "content": content,
        "timestamp": now,
        "expires_at": now + interval_seconds if interval_seconds > 0 else float('inf'),
        "interval": interval_str,
    }
    
    try:
        with open(cache_path, 'w', encoding='utf-8') as f:
            json.dump(cache_data, f, ensure_ascii=False, indent=2)
        logger.debug(f"缓存已保存: {cache_name}, 过期时间: {interval_str}")
        return True
    except Exception as e:
        logger.error(f"保存缓存失败 {cache_name}: {e}")
        return False


def load_cache(cache_name: str) -> Optional[dict]:
    cache_path = get_cache_path(cache_name)
    
    if not os.path.exists(cache_path):
        return None
    
    try:
        with open(cache_path, 'r', encoding='utf-8') as f:
            cache_data = json.load(f)
        
        now = time.time()
        expires_at = cache_data.get("expires_at", 0)
        
        if now >= expires_at:
            logger.debug(f"缓存已过期: {cache_name}")
            return None
        
        logger.debug(f"读取缓存成功: {cache_name}, 剩余有效期: {int(expires_at - now)}秒")
        return cache_data
    except Exception as e:
        logger.error(f"读取缓存失败 {cache_name}: {e}")
        return None


def get_cached_content(cache_name: str) -> Optional[Any]:
    cache_data = load_cache(cache_name)
    if cache_data:
        return cache_data.get("content")
    return None


def is_cache_valid(cache_name: str) -> bool:
    cache_data = load_cache(cache_name)
    return cache_data is not None


def clear_cache(cache_name: str):
    cache_path = get_cache_path(cache_name)
    if os.path.exists(cache_path):
        try:
            os.remove(cache_path)
            logger.info(f"缓存已清除: {cache_name}")
        except Exception as e:
            logger.error(f"清除缓存失败 {cache_name}: {e}")


def clear_all_cache():
    cache_dir = get_cache_dir()
    for filename in os.listdir(cache_dir):
        if filename.endswith('.json'):
            try:
                os.remove(os.path.join(cache_dir, filename))
                logger.info(f"缓存文件已删除: {filename}")
            except Exception as e:
                logger.error(f"删除缓存文件失败 {filename}: {e}")


def get_cache_info(cache_name: str) -> Optional[dict]:
    cache_path = get_cache_path(cache_name)
    
    if not os.path.exists(cache_path):
        return None
    
    try:
        with open(cache_path, 'r', encoding='utf-8') as f:
            cache_data = json.load(f)
        
        now = time.time()
        timestamp = cache_data.get("timestamp", 0)
        expires_at = cache_data.get("expires_at", 0)
        
        return {
            "name": cache_name,
            "cached_time": time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(timestamp)),
            "expires_time": time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(expires_at)) if expires_at != float('inf') else "永不过期",
            "remaining_seconds": max(0, int(expires_at - now)),
            "is_expired": now >= expires_at,
            "interval": cache_data.get("interval", "未知"),
        }
    except Exception as e:
        logger.error(f"获取缓存信息失败 {cache_name}: {e}")
        return None
