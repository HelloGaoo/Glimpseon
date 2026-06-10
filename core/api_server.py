# core/api_server.py
"""
ClassLively Python 后台服务 — FastAPI 包装层
将现有业务逻辑暴露为 REST API，供 C# WinUI 3 前端调用。

启动方式: python -m core.api_server
或: uvicorn core.api_server:app --host 127.0.0.1 --port 19856

硬性约束:
- 本文件为纯增量添加，不修改任何已有 .py 文件
- 所有数据读写统一走 cfg 对象，不引入新的持久化机制
- API 设计考虑未来 gRPC/命名管道升级（路由结构稳定）
"""

from __future__ import annotations

import os
import sys

# 确保项目根目录在路径中
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from fastapi import FastAPI, HTTPException, Query
from fastapi.responses import FileResponse
from pydantic import BaseModel
from typing import Any, Optional
import json
import threading

from core.config import cfg
from core.constants import WALLPAPER_DIR, BASE_DIR, APP_NAME, VERSION, BUILD_DATE
from core.logger import logger

app = FastAPI(title="ClassLively Backend", version="1.0.0")

# ── 延迟导入（避免循环依赖 + 启动时不加载 UI）──

_wallpaper_interface = None
_home_interface = None
_download_interface = None


def _get_wallpaper():
    global _wallpaper_interface
    if _wallpaper_interface is None:
        from ui.wallpaper import WallpaperInterface
        _wallpaper_interface = WallpaperInterface()
    return _wallpaper_interface


def _get_home():
    global _home_interface
    if _home_interface is None:
        from ui.home import HomeInterface
        _home_interface = HomeInterface()
    return _home_interface


def _get_download():
    global _download_interface
    if _download_interface is None:
        from ui.download import DownloadInterface
        _download_interface = DownloadInterface()
    return _download_interface


# ── 响应模型 ──

class ApiResponse(BaseModel):
    code: int = 0
    message: str = "ok"
    data: Any = None


class ConfigSetRequest(BaseModel):
    value: Any


# ── 健康检查 ──

@app.get("/api/health", tags=["system"])
async def health_check() -> ApiResponse:
    """健康检查 — 用于验证后台服务是否在线"""
    return ApiResponse(data={"status": "ok", "app": APP_NAME, "version": VERSION})


# ── 配置 API ──

@app.get("/api/config", tags=["config"])
async def get_config() -> ApiResponse:
    """获取全部配置项的当前值"""
    try:
        data = {}
        for item in cfg.items.values():
            data[item.name] = item.value
        return ApiResponse(data=data)
    except Exception as e:
        logger.error(f"[API] get_config error: {e}")
        return ApiResponse(code=500, message=str(e))


@app.post("/api/config/{key}", tags=["config"])
async def set_config(key: str, req: ConfigSetRequest) -> ApiResponse:
    """设置单个配置项"""
    try:
        item = cfg.items.get(key)
        if item is None:
            return ApiResponse(code=404, message=f"Config key not found: {key}")
        item.value = req.value
        return ApiResponse(message=f"Set {key} = {req.value}")
    except Exception as e:
        logger.error(f"[API] set_config({key}) error: {e}")
        return ApiResponse(code=500, message=str(e))


@app.get("/api/config/items", tags=["config"])
async def list_config_items() -> ApiResponse:
    """列出所有可用的配置项元信息"""
    try:
        items = []
        for name, item in cfg.items.items():
            items.append({
                "key": name,
                "value": item.value,
                "type": type(item.value).__name__
            })
        return ApiResponse(data=items)
    except Exception as e:
        logger.error(f"[API] list_config_items error: {e}")
        return ApiResponse(code=500, message=str(e))


# ── 壁纸 API ──

@app.get("/api/wallpaper/current", tags=["wallpaper"])
async def get_current_wallpaper() -> ApiResponse:
    """获取当前壁纸信息"""
    try:
        wp = _get_wallpaper()
        path = getattr(wp, 'currentWallpaperPath', None) or ""
        return ApiResponse(data={
            "path": path,
            "exists": os.path.exists(path) if path else False
        })
    except Exception as e:
        logger.error(f"[API] get_current_wallpaper error: {e}")
        return ApiResponse(code=500, message=str(e))


@app.post("/api/wallpaper/fetch", tags=["wallpaper"])
async def fetch_wallpaper(source: Optional[str] = Query(None)) -> ApiResponse:
    """获取一张新壁纸"""
    try:
        wp = _get_wallpaper()
        # 调用现有的获取逻辑
        if hasattr(wp, '_onGetClicked'):
            wp._onGetClicked()
        return ApiResponse(message="fetch triggered")
    except Exception as e:
        logger.error(f"[API] fetch_wallpaper error: {e}")
        return ApiResponse(code=500, message=str(e))


@app.post("/api/wallpaper/set-desktop", tags=["wallpaper"])
async def set_desktop_wallpaper(path: str) -> ApiResponse:
    """设为桌面壁纸"""
    try:
        from classlively_native import set_wallpaper
        result = set_wallpaper(path)
        return ApiResponse(data={"success": bool(result)})
    except Exception as e:
        logger.error(f"[API] set_desktop_wallpaper error: {e}")
        return ApiResponse(code=500, message=str(e))


@app.get("/api/wallpaper/history", tags=["wallpaper"])
async def get_history(page: int = Query(1, ge=1), per_page: int = Query(20, ge=1, le=100)) -> ApiResponse:
    """获取壁纸历史记录"""
    try:
        history_dir = WALLPAPER_DIR
        if not os.path.exists(history_dir):
            return ApiResponse(data=[])

        files = [f for f in os.listdir(history_dir)
                 if f.lower().endswith(('.jpg', '.jpeg', '.png', '.bmp', '.webp'))]
        files.sort(reverse=True)

        start = (page - 1) * per_page
        end = start + per_page
        page_files = files[start:end]

        items = []
        for f in page_files:
            full_path = os.path.join(history_dir, f)
            items.append({
                "path": full_path,
                "filename": f,
                "size": os.path.getsize(full_path),
                "mtime": os.path.getmtime(full_path)
            })

        return ApiResponse(data={
            "items": items,
            "total": len(files),
            "page": page,
            "per_page": per_page
        })
    except Exception as e:
        logger.error(f"[API] get_history error: {e}")
        return ApiResponse(code=500, message=str(e))


# ── 天气 API ──

@app.get("/api/weather", tags=["weather"])
async def get_weather(city: Optional[str] = Query(None)) -> ApiResponse:
    """获取天气信息"""
    try:
        from services.weather import WeatherService
        ws = WeatherService()
        city_name = city or cfg.city.value
        info = ws.get_weather(city_name)
        if info is None:
            return ApiResponse(code=404, message="Weather data not available")
        return ApiResponse(data={
            "temp": getattr(info, 'temperature', ''),
            "icon": getattr(info, 'icon_code', ''),
            "description": getattr(info, 'description', ''),
            "city": city_name
        })
    except Exception as e:
        logger.error(f"[API] get_weather error: {e}")
        return ApiResponse(code=500, message=str(e))


# ── 一言 API ──

@app.get("/api/poetry", tags=["poetry"])
async def get_poetry() -> ApiResponse:
    """获取一言/诗词"""
    try:
        from services.poetry import PoetryService
        text = PoetryService.get_poetry()
        return ApiResponse(data={"text": text})
    except Exception as e:
        logger.error(f"[API] get_poetry error: {e}")
        return ApiResponse(code=500, message=str(e))


# ── 系统 API ──

@app.get("/api/system/idle-ms", tags=["system"])
async def idle_ms() -> ApiResponse:
    """获取系统空闲毫秒数"""
    try:
        from classlively_native import idle_get_milliseconds
        ms = idle_get_milliseconds()
        return ApiResponse(data={"ms": ms})
    except Exception as e:
        logger.error(f"[API] idle_ms error: {e}")
        return ApiResponse(code=500, message=str(e), data={"ms": -1})


# ── 媒体 API ──

@app.get("/api/media/info", tags=["media"])
async def media_info() -> ApiResponse:
    """获取当前媒体播放信息"""
    try:
        from services.media import MediaInfo, get_media_info
        info = get_media_info()
        if info is None:
            return ApiResponse(data={})

        lyrics_text = ""
        if hasattr(info, 'lyrics') and info.lyrics:
            lyrics_text = str(info.lyrics)

        return ApiResponse(data={
            "title": getattr(info, 'title', ''),
            "artist": getattr(info, 'artist', ''),
            "album": getattr(info, 'album', ''),
            "cover_path": getattr(info, 'cover_path', '') or "",
            "lyrics": lyrics_text,
            "progress": getattr(info, 'position', 0),
            "duration": getattr(info, 'duration', 0),
            "is_playing": getattr(info, 'is_playing', False)
        })
    except Exception as e:
        logger.error(f"[API] media_info error: {e}")
        return ApiResponse(code=500, message=str(e), data={})


# ── 下载 API ──

@app.get("/api/software/list", tags=["software"])
async def software_list(category: Optional[str] = Query(None)) -> ApiResponse:
    """获取软件列表"""
    try:
        from data.software_list import SOFTWARE_CATEGORIES
        if category and category in SOFTWARE_CATEGORIES:
            apps = SOFTWARE_CATEGORIES[category]
        else:
            apps = []
            for cat_apps in SOFTWARE_CATEGORIES.values():
                apps.extend(cat_apps)

        items = []
        for app_data in apps:
            items.append({
                "name": app_data.get("name", ""),
                "description": app_data.get("description", ""),
                "icon": app_data.get("icon", ""),
                "link": app_data.get("link", ""),
                "category": category or ""
            })
        return ApiResponse(data=items)
    except Exception as e:
        logger.error(f"[API] software_list error: {e}")
        return ApiResponse(code=500, message=str(e), data=[])


@app.post("/api/software/download/{name}", tags=["software"])
async def software_download(name: str) -> ApiResponse:
    """触发软件下载"""
    try:
        dl = _get_download()
        # 查找并触发下载
        if hasattr(dl, '_startDownload'):
            dl._startDownload(name)
        return ApiResponse(message=f"Download triggered: {name}")
    except Exception as e:
        logger.error(f"[API] software_download error: {e}")
        return ApiResponse(code=500, message=str(e))


# ── 启动入口 ──

if __name__ == "__main__":
    import uvicorn
    logger.info("[API Server] Starting ClassLively Backend on http://127.0.0.1:19856")
    uvicorn.run(app, host="127.0.0.1", port=19856, log_level="info")
